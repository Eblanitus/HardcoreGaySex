using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles;

// Тяжёлый бросок бумеранга (зарядка ПКМ): летит по дуге под гравитацией,
// ОТСКАКИВАЕТ от стен и пола с потерей скорости, от первого врага отлетает и падает.
// Ледяная фишка — он сам как шайба: полого лёг на пол — скользит почти без
// трения по любому материалу; в воде всплывает и скользит по поверхности.
// Останавливается на земле и лежит, пока не подберёшь. Пока он жив, предметом
// пользоваться нельзя (BoomerangPlayer/GlobalItem). Сквозь стены не летит никогда.
public class BoomerangHeavyProjectile : ModProjectile
{
	public const float Gravity = 0.25f;
	public const float MaxFallSpeed = 14f;
	public const float BaseSpeed = 10f;
	public const float ChargeSpeedBonus = 7f;
	public const float BounceLoss = 0.78f;    // остаток скорости после отскока от тайла
	public const float GroundFriction = 0.92f; // обычный отскок от пола уже скользкий
	// Скольжение шайбой: свой лёд, материал пола не важен.
	public const float SlideEntrySpeedLoss = 0.97f; // пологий удар о пол почти не гасит ход
	public const float SlideMinShallowness = 2f;    // |vx| должен быть минимум вдвое больше |vy|
	public const float SlideMinHorizontalSpeed = 3f;
	public const float SlideFriction = 0.995f;     // шайба почти не тормозится
	public const float SlideGroundStick = 0.35f;   // лёгкий прижим к полу вместо полного падения
	public const float SlideRestSpeed = 0.45f;
	public const int SlideAirGraceTicks = 5;       // мелкие уступы не рвут скольжение сразу
	// Вода: лёд легче воды, поэтому тяжёлый не тонет.
	public const float WaterBuoyancy = 0.30f;
	public const float WaterDrag = 0.98f;
	public const float WaterVerticalDrag = 0.90f; // вода быстро гасит нырок вниз
	public const float MaxFloatRiseSpeed = 3f;
	public const float FloatDraft = 2f; // на сколько центр шайбы сидит ниже поверхности
	public const float RestSpeed = 1.6f;      // медленнее = лёг и лежит
	public const float EnemyBounceBack = 0.65f; // отскок назад от врага
	public const float EnemyPopUp = -5f;        // и вверх перед падением
	public const float PickupRadius = 36f;
	public const float CatchRadius = 48f; // перехват на лету, когда пролетает мимо игрока
	public const int FrostburnTicks = 240;

	public static float LaunchSpeed(float charge) => BaseSpeed + ChargeSpeedBonus * charge;
	public static Vector2 EnemyBounceVelocity(Vector2 vel) => new Vector2(-vel.X * EnemyBounceBack, EnemyPopUp);

	private const int StateFlying = 0;  // боеспособен
	private const int StateFalling = 1; // потрачен (попал по врагу), безвреден, отскакивает до остановки
	private const int StateResting = 2; // лежит, ждёт подбора
	private const int StateSliding = 3; // шайба по полу или по воде; боеспособность — в slideArmed

	// Скольжение после попадания остаётся безвредным: правило «первый враг
	// потратил бросок» здесь не ослабляем. Флаг лежит в ai[2], чтобы синхронизировался.
	private ref float SlideArmedFlag => ref Projectile.ai[2];
	private bool slideArmed {
		get => SlideArmedFlag != 0f;
		set => SlideArmedFlag = value ? 1f : 0f;
	}
	private int ticksSinceSlideContact;

	public override string Texture => $"Terraria/Images/Item_{ItemID.IceBoomerang}";

	public override void SetDefaults()
	{
		Projectile.width = 28;
		Projectile.height = 28;
		Projectile.friendly = true;
		Projectile.DamageType = DamageClass.Melee;
		Projectile.penetrate = -1; // один хит обеспечивают CanHitNPC и смена состояния
		Projectile.usesLocalNPCImmunity = true;
		Projectile.localNPCHitCooldown = -1;
		Projectile.tileCollide = true;
		Projectile.ignoreWater = true;
		Projectile.timeLeft = 1800; // предохранитель полёта (прыгает долго); лежащий живёт до подбора
		Projectile.aiStyle = -1;
	}

	private ref float State => ref Projectile.ai[1];

	private int spinDir = 1;
	private bool leftOwner; // улетел от игрока — теперь можно ловить при возврате

	public override void AI()
	{
		Player owner = Main.player[Projectile.owner];
		int state = (int)State;

		// Перехват на лету: пролетает мимо игрока — игрок всегда стягивает.
		// Сначала должен улететь за радиус, иначе ловил бы сам бросок в момент старта.
		if (state != StateResting && Projectile.owner == Main.myPlayer && owner.active && !owner.dead) {
			float dist = Vector2.Distance(Projectile.Center, owner.Center);
			if (!leftOwner) {
				if (dist > CatchRadius)
					leftOwner = true;
			}
			else if (dist < CatchRadius) {
				SoundEngine.PlaySound(SoundID.Grab, Projectile.Center);
				Projectile.Kill();
				return;
			}
		}

		if (state == StateResting) {
			Projectile.timeLeft = 2; // лежит, пока не подберут
			if (Projectile.owner == Main.myPlayer && owner.active && !owner.dead
				&& Vector2.Distance(Projectile.Center, owner.Center) < PickupRadius) {
				SoundEngine.PlaySound(SoundID.Grab, Projectile.Center);
				Projectile.Kill();
				return;
			}
			// Лёд в воде всплывает даже со дна; без опоры лежащий тоже должен упасть.
			if (TryGetWaterSurface(Projectile.Center, out float restWaterSurfaceY)) {
				if (Projectile.Center.Y > restWaterSurfaceY + FloatDraft + 4f)
					WakeFromRest();
			}
			else if (!SolidAt(Projectile.Center + new Vector2(0f, 4f)))
				WakeFromRest();
			return;
		}

		if (Projectile.velocity.X != 0f)
			spinDir = Projectile.velocity.X >= 0f ? 1 : -1;
		Projectile.rotation += (state == StateSliding ? 0.25f : 0.4f) * spinDir;

		bool handledByWater = false;
		if (TryGetWaterSurface(Projectile.Center, out float waterSurfaceY)) {
			float floatCenterY = waterSurfaceY + FloatDraft;
			if (Projectile.Center.Y > floatCenterY + 2f) {
				// Погрузился — лёд выталкивает обратно на поверхность.
				Projectile.velocity.Y = MathHelper.Max(Projectile.velocity.Y * WaterVerticalDrag - WaterBuoyancy, -MaxFloatRiseSpeed);
				Projectile.velocity.X *= WaterDrag;
				handledByWater = true;
			}
			else if (Projectile.Center.Y >= floatCenterY - 4f) {
				// Лежит на воде: поверхность держит шайбу точно так же, как пол.
				Projectile.Center = new Vector2(Projectile.Center.X,
					MathHelper.Lerp(Projectile.Center.Y, floatCenterY, 0.35f));
				Projectile.velocity.Y *= 0.35f;
				if (Math.Abs(Projectile.velocity.X) <= SlideRestSpeed) {
					ComeToRest();
					return;
				}
				if (state == StateSliding) {
					ticksSinceSlideContact = 0;
					Projectile.velocity.X *= SlideFriction;
				}
				else if (Math.Abs(Projectile.velocity.X) >= SlideMinHorizontalSpeed)
					StartSlide(Projectile.velocity.X, state == StateFlying);
				else
					Projectile.velocity.X *= SlideFriction;
				handledByWater = true;
			}
		}

		if (!handledByWater) {
			if (state == StateSliding) {
				bool grounded = SolidAt(Projectile.Center + new Vector2(0f, 4f));
				if (grounded) {
					ticksSinceSlideContact = 0;
					Projectile.velocity.X *= SlideFriction;
					if (Math.Abs(Projectile.velocity.X) <= SlideRestSpeed) {
						ComeToRest();
						return;
					}
					Projectile.velocity.Y = MathHelper.Min(Projectile.velocity.Y + SlideGroundStick, 2f);
				}
				else {
					Projectile.velocity.Y = MathHelper.Min(Projectile.velocity.Y + Gravity, MaxFallSpeed);
					if (++ticksSinceSlideContact > SlideAirGraceTicks) {
						bool armed = slideArmed;
						State = armed ? StateFlying : StateFalling;
						slideArmed = false;
						Projectile.friendly = armed;
						Projectile.netUpdate = true;
						state = (int)State;
					}
				}
			}
			else
				Projectile.velocity.Y = MathHelper.Min(Projectile.velocity.Y + Gravity, MaxFallSpeed);
		}

		if (state == StateFalling || (state == StateSliding && !slideArmed))
			Projectile.timeLeft = 2; // обязательно долетит до остановки
	}

	// Бьёт в боевом полёте и в боевом скольжении; потраченный/лежащий безвреден.
	public override bool? CanHitNPC(NPC target) =>
		(int)State == StateFlying || ((int)State == StateSliding && slideArmed) ? null : false;

	public override bool OnTileCollide(Vector2 oldVelocity)
	{
		bool hitWall = Projectile.velocity.X != oldVelocity.X;
		bool hitFloor = false;

		if ((int)State == StateSliding) {
			// В скольжении стенка просто разворачивает шайбу, а пол не подбрасывает.
			if (hitWall)
				Projectile.velocity.X = -oldVelocity.X * BounceLoss;
			if (Projectile.velocity.Y != oldVelocity.Y)
				Projectile.velocity.Y = 0f;
			ticksSinceSlideContact = 0;
			if (hitWall)
				SoundEngine.PlaySound(SoundID.Dig, Projectile.Center);
			return false;
		}

		if (hitWall)
			Projectile.velocity.X = -oldVelocity.X * BounceLoss;
		if (Projectile.velocity.Y != oldVelocity.Y) {
			hitFloor = oldVelocity.Y > 0f;
			// Полого лёг на пол — не подпрыгивает, а уходит в скольжение шайбой.
			if (!hitWall && hitFloor && IsShallowFloorHit(oldVelocity)) {
				StartSlide(oldVelocity.X, (int)State == StateFlying);
				SoundEngine.PlaySound(SoundID.Dig, Projectile.Center);
				return false;
			}
			Projectile.velocity.Y = -oldVelocity.Y * BounceLoss;
			Projectile.velocity.X *= GroundFriction;
		}
		SoundEngine.PlaySound(SoundID.Dig, Projectile.Center);
		// Ложится только после удара об пол сверху вниз — не «прилипает» к стенке на вершине дуги.
		if (hitFloor && Projectile.velocity.Length() < RestSpeed)
			ComeToRest();
		return false;
	}

	public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
	{
		target.AddBuff(BuffID.Frostburn, FrostburnTicks);
		if ((int)State == StateFlying || ((int)State == StateSliding && slideArmed)) {
			// Отскочил от врага и падает — дальше только отскоки от тайлов до остановки.
			State = StateFalling;
			slideArmed = false;
			Projectile.velocity = EnemyBounceVelocity(Projectile.velocity);
			Projectile.friendly = false;
			Projectile.netUpdate = true;
			SoundEngine.PlaySound(SoundID.NPCHit5, Projectile.Center);
		}
	}

	private void StartSlide(float horizontalSpeed, bool armed)
	{
		State = StateSliding;
		slideArmed = armed;
		ticksSinceSlideContact = 0;
		Projectile.velocity.X = horizontalSpeed * SlideEntrySpeedLoss;
		Projectile.velocity.Y = 0f;
		Projectile.friendly = armed;
		Projectile.tileCollide = true;
		Projectile.netUpdate = true;
	}

	private void WakeFromRest()
	{
		State = StateFalling;
		slideArmed = false;
		Projectile.friendly = false;
		Projectile.tileCollide = true;
		Projectile.velocity.Y = -1f;
		Projectile.netUpdate = true;
	}

	private static bool IsShallowFloorHit(Vector2 velocity) =>
		velocity.Y > 0f
		&& Math.Abs(velocity.X) >= SlideMinHorizontalSpeed
		&& Math.Abs(velocity.X) >= Math.Abs(velocity.Y) * SlideMinShallowness;

	// Поверхность именно воды: лава, мёд и шиммер шайбу не держат.
	// Опрашиваем низ хитбокса и идём вверх до верхнего жидкого тайла.
	// HasWater в этой версии tML нет — проверяем тип жидкости напрямую.
	private static bool IsWaterTile(int x, int y) =>
		Main.tile[x, y].LiquidAmount > 0 && Main.tile[x, y].LiquidType == LiquidID.Water;

	private static bool TryGetWaterSurface(Vector2 center, out float surfaceY)
	{
		surfaceY = 0f;
		Point probe = new Vector2(center.X, center.Y + 12f).ToTileCoordinates();
		if (probe.X < 0 || probe.X >= Main.maxTilesX || probe.Y < 0 || probe.Y >= Main.maxTilesY
			|| !IsWaterTile(probe.X, probe.Y))
			return false;

		int y = probe.Y;
		while (y > 0 && IsWaterTile(probe.X, y - 1))
			y--;

		surfaceY = y * 16f + (256f - Main.tile[probe.X, y].LiquidAmount) / 16f;
		return true;
	}

	private void ComeToRest()
	{
		State = StateResting;
		slideArmed = false;
		ticksSinceSlideContact = 0;
		Projectile.velocity = Vector2.Zero;
		Projectile.friendly = false;
		Projectile.tileCollide = false;
		Projectile.netUpdate = true;
	}

	// Прицел: тот же полёт пошагово — гравитация, отскоки от тайлов, скольжение
	// шайбы и вода, отскок от первого врага и падение до остановки. Константы общие.
	public static List<Vector2> TraceAim(Vector2 pos, Vector2 vel, int maxSteps = 900)
	{
		var points = new List<Vector2> { pos };
		bool armed = true;
		bool sliding = false;
		int slideAirTicks = 0;
		for (int step = 0; step < maxSteps; step++) {
			bool handledByWater = false;
			if (TryGetWaterSurface(pos, out float waterSurfaceY)) {
				float floatCenterY = waterSurfaceY + FloatDraft;
				if (pos.Y > floatCenterY + 2f) {
					vel.Y = MathHelper.Max(vel.Y * WaterVerticalDrag - WaterBuoyancy, -MaxFloatRiseSpeed);
					vel.X *= WaterDrag;
					handledByWater = true;
				}
				else if (pos.Y >= floatCenterY - 4f) {
					pos.Y = MathHelper.Lerp(pos.Y, floatCenterY, 0.35f);
					vel.Y *= 0.35f;
					if (Math.Abs(vel.X) <= SlideRestSpeed)
						break;
					if (sliding) {
						slideAirTicks = 0;
						vel.X *= SlideFriction;
					}
					else if (Math.Abs(vel.X) >= SlideMinHorizontalSpeed) {
						sliding = true;
						slideAirTicks = 0;
						vel.X *= SlideEntrySpeedLoss;
						vel.Y = 0f;
					}
					else
						vel.X *= SlideFriction;
					handledByWater = true;
				}
			}

			if (!handledByWater) {
				if (sliding) {
					if (SolidAt(pos + new Vector2(0f, 4f))) {
						slideAirTicks = 0;
						vel.X *= SlideFriction;
						if (Math.Abs(vel.X) <= SlideRestSpeed)
							break;
						vel.Y = MathHelper.Min(vel.Y + SlideGroundStick, 2f);
					}
					else {
						vel.Y = MathHelper.Min(vel.Y + Gravity, MaxFallSpeed);
						if (++slideAirTicks > SlideAirGraceTicks)
							sliding = false;
					}
				}
				else
					vel.Y = MathHelper.Min(vel.Y + Gravity, MaxFallSpeed);
			}

			bool hitWall = false;
			Vector2 next = pos + new Vector2(vel.X, 0f);
			if (SolidAt(next)) {
				hitWall = true;
				vel.X = -vel.X * BounceLoss;
				if (sliding)
					slideAirTicks = 0;
			}
			else
				pos = next;

			Vector2 oldVelocity = vel;
			bool wasFalling = vel.Y > 0f;
			next = pos + new Vector2(0f, vel.Y);
			if (SolidAt(next)) {
				if (sliding) {
					vel.Y = 0f;
					slideAirTicks = 0;
				}
				else if (!hitWall && wasFalling && IsShallowFloorHit(oldVelocity)) {
					sliding = true;
					slideAirTicks = 0;
					vel.X = oldVelocity.X * SlideEntrySpeedLoss;
					vel.Y = 0f;
				}
				else {
					vel.Y = -vel.Y * BounceLoss;
					vel.X *= GroundFriction;
					if (wasFalling && vel.Length() < RestSpeed)
						break; // здесь ляжет — pos последняя точка
				}
			}
			else
				pos = next;

			points.Add(pos);

			if (armed) {
				var box = new Rectangle((int)pos.X - 14, (int)pos.Y - 14, 28, 28);
				for (int i = 0; i < Main.maxNPCs; i++) {
					NPC npc = Main.npc[i];
					if (npc.active && !npc.friendly && npc.life > 0 && box.Intersects(npc.Hitbox)) {
						vel = EnemyBounceVelocity(vel);
						armed = false;
						sliding = false;
						break;
					}
				}
			}
		}
		points.Add(pos);
		return points;
	}

	// Приближение хитбокса 28×28: любой из четырёх углов в твёрдом тайле.
	private static bool SolidAt(Vector2 center)
	{
		return CornerSolid(center.X - 13f, center.Y - 13f)
			|| CornerSolid(center.X + 13f, center.Y - 13f)
			|| CornerSolid(center.X - 13f, center.Y + 13f)
			|| CornerSolid(center.X + 13f, center.Y + 13f);
	}

	private static bool CornerSolid(float x, float y)
	{
		Point t = new Vector2(x, y).ToTileCoordinates();
		return t.X < 0 || t.X >= Main.maxTilesX || t.Y < 0 || t.Y >= Main.maxTilesY
			|| WorldGen.SolidTile(t.X, t.Y);
	}
}
