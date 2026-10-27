using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles;

// Тяжёлый бросок бумеранга (зарядка ПКМ): летит по дуге под гравитацией,
// ОТСКАКИВАЕТ от стен и пола с потерей скорости, от первого врага отлетает и падает.
// Останавливается на земле и лежит, пока не подберёшь. Пока он жив, предметом
// пользоваться нельзя (BoomerangPlayer/GlobalItem). Сквозь стены не летит никогда.
public class BoomerangHeavyProjectile : ModProjectile
{
	public const float Gravity = 0.25f;
	public const float MaxFallSpeed = 14f;
	public const float BaseSpeed = 10f;
	public const float ChargeSpeedBonus = 7f;
	public const float BounceLoss = 0.78f;    // остаток скорости после отскока от тайла
	public const float GroundFriction = 0.88f; // трение о землю при отскоке от пола
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
			}
			return;
		}

		if (Projectile.velocity.X != 0f)
			spinDir = Projectile.velocity.X >= 0f ? 1 : -1;
		Projectile.rotation += 0.4f * spinDir;
		Projectile.velocity.Y = MathHelper.Min(Projectile.velocity.Y + Gravity, MaxFallSpeed);

		if (state == StateFalling)
			Projectile.timeLeft = 2; // обязательно долетит до остановки
	}

	// Бьёт только в боевом полёте — потраченный/лежащий безвреден.
	public override bool? CanHitNPC(NPC target) => (int)State == StateFlying ? null : false;

	public override bool OnTileCollide(Vector2 oldVelocity)
	{
		bool hitFloor = false;
		if (Projectile.velocity.X != oldVelocity.X)
			Projectile.velocity.X = -oldVelocity.X * BounceLoss;
		if (Projectile.velocity.Y != oldVelocity.Y) {
			hitFloor = oldVelocity.Y > 0f;
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
		if ((int)State == StateFlying) {
			// Отскочил от врага и падает — дальше только отскоки от тайлов до остановки.
			State = StateFalling;
			Projectile.velocity = EnemyBounceVelocity(Projectile.velocity);
			Projectile.friendly = false;
			Projectile.netUpdate = true;
			SoundEngine.PlaySound(SoundID.NPCHit5, Projectile.Center);
		}
	}

	private void ComeToRest()
	{
		State = StateResting;
		Projectile.velocity = Vector2.Zero;
		Projectile.friendly = false;
		Projectile.tileCollide = false;
		Projectile.netUpdate = true;
	}

	// Прицел: тот же полёт пошагово — гравитация, отскоки от тайлов, отскок от первого
	// врага на пути и падение до места остановки. Константы общие с настоящим снарядом.
	public static List<Vector2> TraceAim(Vector2 pos, Vector2 vel, int maxSteps = 900)
	{
		var points = new List<Vector2> { pos };
		bool armed = true;
		for (int step = 0; step < maxSteps; step++) {
			vel.Y = MathHelper.Min(vel.Y + Gravity, MaxFallSpeed);

			Vector2 next = pos + new Vector2(vel.X, 0f);
			if (SolidAt(next))
				vel.X = -vel.X * BounceLoss;
			else
				pos = next;

			bool wasFalling = vel.Y > 0f;
			next = pos + new Vector2(0f, vel.Y);
			if (SolidAt(next)) {
				vel.Y = -vel.Y * BounceLoss;
				vel.X *= GroundFriction;
				if (wasFalling && vel.Length() < RestSpeed)
					break; // здесь ляжет — pos последняя точка
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
