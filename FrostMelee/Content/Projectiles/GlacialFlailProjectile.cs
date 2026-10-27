using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles;

// Глыба ледникового цепа — гибрид обеих подкатегорий цепов в одном движке.
// Thrown-половина (ЛКМ): крутка вокруг игрока с набором инерции, отпустил —
// баллистический бросок с пробитием, зажал в полёте — глыба падает и лежит
// на земле, пока держишь. Launched-половина (ПКМ): прямой выстрел без
// гравитации, после первого врага/стены сразу назад; из крутки выстрел
// сохраняет часть разгона и после поимки возвращается в крутку.
// Крутка вплотную — главный урон (с разгона 65% ещё и Frostbite), всё
// дальнее слабее. Сквозь стены не бьёт, хитбокс — только сама глыба.
public class GlacialFlailProjectile : ModProjectile
{
	public const int RampTicks = 150;             // 2.5 с до полной раскрутки
	public const float OrbitRadiusMin = 36f;      // радиус круга на старте
	public const float OrbitRadiusMax = 58f;      // и на полном разгоне
	public const float OrbitRaise = 10f;          // круг вокруг плеча, а не пояса: глыба не шкрябает пол низом
	public const float OrbitVerticalSquash = 0.82f; // круг чуть сплющен по вертикали, как у ванильных цепов
	public const float OrbitLowerSquash = 0.55f;  // нижняя половина ещё площе — над землёй
	public const float SpinSpeedMin = 0.09f;      // рад/тик: ленивый старт
	public const float SpinSpeedMax = 0.24f;      // полный разгон (~2.3 оборота/с)
	public const float ThrowSpeedMin = 11f;       // бросок без раскрутки
	public const float ThrowSpeedMax = 19f;       // бросок с полной раскрутки
	public const float MaxChainLength = 250f;     // дальше цепь не пускает
	public const int MaxThrowTicks = 26;          // страховка по времени полёта
	public const float ThrowGravity = 0.16f;      // глыба тяжёлая — в броске проседает
	public const float ReturnSpeed = 20f;
	public const float ReturnAcceleration = 2.4f;
	public const float SpinDamageMin = 0.65f;     // множитель урона крутки без разгона
	public const float SpinDamageMax = 1.4f;      // и на полном разгоне
	public const float ThrowDamageMult = 0.65f;   // бросок на цепи слабее крутки вплотную
	public const float FrostbiteRamp = 0.65f;     // с этого разгона крутка вешает Frostbite
	public const int FrostbiteTicks = 180;

	// Launched-половина (ПКМ): прямой выстрел без гравитации
	public const float AiLaunchOut = 3f;          // ai[0] при спавне выстрелом (порядок enum State!)
	public const float LaunchSpeedMin = 17f;      // выстрел без разгона
	public const float LaunchSpeedMax = 22f;      // выстрел с полной крутки
	public const float LaunchRange = 260f;        // длина цепи выстрела
	public const float LaunchReturnSpeed = 24f;   // назад быстрее, чем туда
	public const float LaunchDamageBase = 0.8f;   // урон выстрела без разгона
	public const float LaunchDamageRampBonus = 0.35f; // добавка от полной крутки
	public const float SavedSpinKeep = 0.6f;      // сколько разгона остаётся после выстрела из крутки

	// Упавшая глыба thrown-половины: лежит, пока держат ЛКМ
	public const float DropGravity = 0.8f;
	public const float MaxDroppedRange = 370f;    // дальше цепь дёргает глыбу домой
	public const float DropDamageMult = 0.5f;

	public static float LaunchSpeed(float ramp) => MathHelper.Lerp(LaunchSpeedMin, LaunchSpeedMax, MathHelper.Clamp(ramp, 0f, 1f));

	private const int SpinHitCooldown = 16;
	private const int ThrowHitCooldown = 10;
	private const int SpinArmTicks = 8; // первые тики глыба ещё выходит на круг и не бьёт

	// Порядок важен: GlacialFlailPlayer спавнит выстрел числом AiLaunchOut
	private enum State
	{
		Spin,        // 0 thrown: крутка
		Throw,       // 1 thrown: баллистический полёт
		Return,      // 2 thrown: возврат домой
		LaunchOut,   // 3 launched: прямой выстрел
		LaunchReturn,// 4 launched: быстрый возврат выстрела
		Drop         // 5 thrown: глыба лежит на земле
	}

	private State CurrentState
	{
		get => (State)Projectile.ai[0];
		set => Projectile.ai[0] = (float)value;
	}

	private ref float StateTimer => ref Projectile.ai[1];
	private ref float SpinDirection => ref Projectile.ai[2];

	private float SpinRamp => MathHelper.Clamp(StateTimer / RampTicks, 0f, 1f);

	private bool started;
	private bool fullSpinPing;
	private float releaseRamp;   // разгон крутки в момент броска/выстрела
	private int savedSpinTicks;  // тики крутки перед выстрелом: часть вернётся при поимке
	private int tileSoundCooldown;

	public override void SetStaticDefaults()
	{
		ProjectileID.Sets.HeldProjDoesNotUsePlayerGfxOffY[Type] = true;
	}

	public override void SetDefaults()
	{
		Projectile.width = 36;
		Projectile.height = 36;
		Projectile.friendly = true;
		Projectile.DamageType = DamageClass.Melee;
		Projectile.penetrate = -1;
		Projectile.tileCollide = true;
		Projectile.ignoreWater = true;
		Projectile.usesLocalNPCImmunity = true;
		Projectile.localNPCHitCooldown = SpinHitCooldown;
		Projectile.ownerHitCheck = true; // сквозь стены не бьёт с первого же тика
		Projectile.aiStyle = -1; // полёт полностью наш, ванильный цеп не зовём
		Projectile.netImportant = true;
		Projectile.timeLeft = 2;
	}

	public override void AI()
	{
		Player player = Main.player[Projectile.owner];
		if (!player.active || player.dead || player.noItems || player.CCed ||
			Vector2.Distance(Projectile.Center, player.Center) > 900f)
		{
			Projectile.Kill();
			return;
		}
		if (Projectile.owner == Main.myPlayer && Main.mapFullscreen)
		{
			Projectile.Kill();
			return;
		}

		if (!started)
		{
			started = true;
			SpinDirection = Projectile.velocity.X < 0f ? -1f : 1f;
			if (Projectile.velocity.X == 0f)
				SpinDirection = player.direction >= 0 ? 1f : -1f;
			if (CurrentState == State.LaunchOut)
			{
				// Спавн сразу выстрелом по ПКМ: скорость строго по прямой
				Vector2 dir = Projectile.velocity.SafeNormalize(new Vector2(player.direction, 0f));
				Projectile.velocity = player.velocity + dir * LaunchSpeed(0f);
				SoundEngine.PlaySound(SoundID.Item1 with { Volume = 0.6f, Pitch = 0.1f }, Projectile.Center);
			}
			else
				SoundEngine.PlaySound(SoundID.Item1 with { Volume = 0.45f, Pitch = -0.25f }, Projectile.Center);
		}

		if (tileSoundCooldown > 0)
			tileSoundCooldown--;

		Vector2 anchor = player.MountedCenter;
		Projectile.timeLeft = 2;
		player.heldProj = Projectile.whoAmI;
		player.SetDummyItemTime(2); // второй цеп кнопками не наспамить

		// Оружие сменили посреди полёта — глыба просто возвращается и исчезает
		if (player.HeldItem.type != ModContent.ItemType<Items.GlacialFlail>() && CurrentState != State.Return)
			BeginReturn();

		switch (CurrentState)
		{
			case State.Spin:
				SpinAI(player, anchor);
				break;

			case State.Throw:
				Projectile.tileCollide = true;
				Projectile.localNPCHitCooldown = ThrowHitCooldown;
				StateTimer++;
				Projectile.velocity.Y += ThrowGravity;
				if (Projectile.velocity.Y > 16f)
					Projectile.velocity.Y = 16f;
				if (player.controlUseItem && StateTimer > 3f)
					BeginDrop(); // зажал ЛКМ в полёте — глыба падает и лежит
				else if (StateTimer > MaxThrowTicks || Projectile.Distance(anchor) > MaxChainLength)
					BeginReturn();
				player.ChangeDir(Projectile.Center.X >= player.Center.X ? 1 : -1);
				break;

			case State.Return:
				if (!ReturnAI(player, anchor))
					return; // поймали глыбу
				break;

			case State.LaunchOut:
				// Прямая линия без гравитации; скорость задана при выстреле
				Projectile.tileCollide = true;
				Projectile.localNPCHitCooldown = ThrowHitCooldown;
				StateTimer++;
				if (Projectile.Distance(anchor) > LaunchRange)
					BeginLaunchReturn();
				player.ChangeDir(Projectile.Center.X >= player.Center.X ? 1 : -1);
				break;

			case State.LaunchReturn:
				if (!LaunchReturnAI(player, anchor))
					return; // поймана: ушла в крутку или исчезла
				break;

			case State.Drop:
				Projectile.tileCollide = true;
				Projectile.localNPCHitCooldown = ThrowHitCooldown;
				StateTimer++;
				if (!player.controlUseItem || Projectile.Distance(anchor) > MaxDroppedRange)
					BeginReturn();
				else
				{
					Projectile.velocity.Y += DropGravity;
					if (Projectile.velocity.Y > 18f)
						Projectile.velocity.Y = 18f;
					Projectile.velocity.X *= 0.95f;
					player.ChangeDir(Projectile.Center.X >= player.Center.X ? 1 : -1);
				}
				break;
		}

		Projectile.direction = Projectile.velocity.X >= 0f ? 1 : -1;
		Projectile.spriteDirection = Projectile.direction;
		Projectile.ownerHitCheck = true; // без прямой видимости от игрока не бьём

		if (CurrentState != State.Spin)
		{
			if (Projectile.velocity.Length() > 1f)
				Projectile.rotation = Projectile.velocity.ToRotation() + Projectile.velocity.X * 0.06f;
			else
				Projectile.rotation += Projectile.velocity.X * 0.08f;
		}

		// Рука с рукоятью смотрит на глыбу, как у ванильных цепов
		player.itemRotation = Projectile.DirectionFrom(anchor).ToRotation();
		if (Projectile.Center.X < anchor.X)
			player.itemRotation += MathHelper.Pi;
		player.itemRotation = MathHelper.WrapAngle(player.itemRotation);

		Lighting.AddLight(Projectile.Center, 0.18f, 0.42f, 0.62f);
	}

	private void SpinAI(Player player, Vector2 anchor)
	{
		Projectile.tileCollide = true;
		Projectile.localNPCHitCooldown = SpinHitCooldown;
		StateTimer++;

		// Отпустил ЛКМ — баллистический бросок; ПКМ на крутке — прямой выстрел
		if (Projectile.owner == Main.myPlayer)
		{
			Vector2 aim = anchor.DirectionTo(Main.MouseWorld).SafeNormalize(new Vector2(player.direction, 0f));
			player.ChangeDir(aim.X >= 0f ? 1 : -1);
			if (!player.channel)
			{
				LaunchThrown(player, aim);
				return;
			}
			if (player.GetModPlayer<Weapons.GlacialFlailPlayer>().LaunchHeld)
			{
				BeginLaunched(player, aim);
				return;
			}
		}
		else if (!player.channel)
		{
			LaunchThrown(player, Projectile.velocity.SafeNormalize(new Vector2(player.direction, 0f)));
			return;
		}

		float ramp = SpinRamp;
		float angularSpeed = MathHelper.Lerp(SpinSpeedMin, SpinSpeedMax, ramp * ramp);

		Vector2 orbitAnchor = anchor + new Vector2(0f, player.gfxOffY - OrbitRaise * player.gravDir);
		Vector2 offset = Projectile.Center - orbitAnchor;
		float angle = offset.LengthSquared() > 144f ? offset.ToRotation() : Projectile.velocity.ToRotation();
		float nextAngle = angle + SpinDirection * angularSpeed;
		float wantedRadius = MathHelper.Lerp(OrbitRadiusMin, OrbitRadiusMax, ramp);
		float radius = FreeOrbitRadius(orbitAnchor, nextAngle, wantedRadius, player.gravDir);
		Vector2 radial = nextAngle.ToRotationVector2();
		Vector2 target = orbitAnchor + OrbitOffset(radial, radius, player.gravDir);

		// Ведём глыбу по кругу рулёжкой: касательная скорость круга +
		// подтяжка к точке орбиты. Стены честно тормозят через tileCollide.
		Vector2 tangent = radial.RotatedBy(SpinDirection * MathHelper.PiOver2) * (radius * angularSpeed);
		Vector2 correction = (target - Projectile.Center) * 0.3f;
		if (correction.Length() > 10f)
			correction = correction.SafeNormalize(Vector2.Zero) * 10f;
		Projectile.velocity = player.velocity + tangent + correction;
		Projectile.rotation += SpinDirection * angularSpeed * 1.5f;

		if (!Main.dedServ && Main.rand.NextFloat() < 0.15f + 0.45f * ramp)
		{
			Dust d = Dust.NewDustPerfect(Projectile.Center + Main.rand.NextVector2Circular(12f, 12f),
				DustID.Frost, -Projectile.velocity * 0.08f, 120, default, Main.rand.NextFloat(0.7f, 1.2f));
			d.noGravity = true;
		}

		// Полный разгон слышно и видно один раз за раскрутку
		if (ramp >= 1f && !fullSpinPing)
		{
			fullSpinPing = true;
			SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.45f, Pitch = 0.3f }, Projectile.Center);
			if (!Main.dedServ)
			{
				for (int i = 0; i < 10; i++)
				{
					Vector2 dir = Vector2.UnitX.RotatedBy(i / 10f * MathHelper.TwoPi);
					Dust d = Dust.NewDustPerfect(Projectile.Center + dir * 14f, DustID.SnowflakeIce,
						dir * 2.2f, 100, default, 1.2f);
					d.noGravity = true;
				}
			}
		}
	}

	private void LaunchThrown(Player player, Vector2 aim)
	{
		releaseRamp = SpinRamp;
		CurrentState = State.Throw;
		StateTimer = 0f;
		Projectile.velocity = player.velocity + aim * MathHelper.Lerp(ThrowSpeedMin, ThrowSpeedMax, releaseRamp);
		Projectile.ResetLocalNPCHitImmunity();
		Projectile.localNPCHitCooldown = ThrowHitCooldown;
		Projectile.netUpdate = true;
		SoundEngine.PlaySound(SoundID.Item1 with { Volume = 0.7f, Pitch = -0.1f }, Projectile.Center);
	}

	// Выстрел launched-половины из крутки: разгон запоминаем — после поимки
	// крутка продолжится не с нуля
	private void BeginLaunched(Player player, Vector2 aim)
	{
		savedSpinTicks = (int)StateTimer;
		releaseRamp = SpinRamp;
		CurrentState = State.LaunchOut;
		StateTimer = 0f;
		Projectile.velocity = player.velocity + aim * LaunchSpeed(releaseRamp);
		Projectile.ResetLocalNPCHitImmunity();
		Projectile.localNPCHitCooldown = ThrowHitCooldown;
		Projectile.netUpdate = true;
		SoundEngine.PlaySound(SoundID.Item1 with { Volume = 0.65f, Pitch = 0.2f }, Projectile.Center);
	}

	private void BeginLaunchReturn()
	{
		if (CurrentState != State.LaunchOut)
			return;
		CurrentState = State.LaunchReturn;
		StateTimer = 0f;
		Projectile.tileCollide = false;
		Projectile.netUpdate = true;
	}

	// Глыба падает и лежит, пока держат ЛКМ (thrown-половина)
	private void BeginDrop()
	{
		CurrentState = State.Drop;
		StateTimer = 0f;
		Projectile.velocity *= 0.2f;
		Projectile.tileCollide = true;
		Projectile.netUpdate = true;
	}

	private void BeginReturn()
	{
		if (CurrentState == State.Return)
			return;
		CurrentState = State.Return;
		StateTimer = 0f;
		Projectile.tileCollide = false; // домой — напрямую, но бьёт только при прямой видимости
		Projectile.netUpdate = true;
	}

	// false — глыба поймана и снаряд убит
	private bool ReturnAI(Player player, Vector2 anchor)
	{
		Projectile.tileCollide = false;
		Projectile.localNPCHitCooldown = ThrowHitCooldown;
		StateTimer++;

		// И на возврате зажатая ЛКМ роняет глыбу, как у ванильных цепов
		// (но не дальше длины цепи упавшей глыбы, иначе туда-сюда по кругу)
		if (player.controlUseItem && StateTimer > 3f && Projectile.Distance(anchor) <= MaxDroppedRange)
		{
			BeginDrop();
			return true;
		}

		if (!FlyHome(player, anchor, ReturnSpeed, ReturnAcceleration))
		{
			if (Projectile.owner == Main.myPlayer)
				SoundEngine.PlaySound(SoundID.Grab, player.Center);
			Projectile.Kill();
			return false;
		}
		return true;
	}

	// false — выстрел пойман: ЛКМ ещё зажата — обратно в крутку с частью
	// разгона, иначе глыба исчезает (следующий выстрел выпустит GlacialFlailPlayer)
	private bool LaunchReturnAI(Player player, Vector2 anchor)
	{
		Projectile.tileCollide = false;
		Projectile.localNPCHitCooldown = ThrowHitCooldown;
		StateTimer++;

		if (!FlyHome(player, anchor, LaunchReturnSpeed, 100f))
		{
			if (Projectile.owner == Main.myPlayer)
				SoundEngine.PlaySound(SoundID.Grab, player.Center);
			if (player.channel)
			{
				CurrentState = State.Spin;
				StateTimer = savedSpinTicks * SavedSpinKeep;
				fullSpinPing = SpinRamp >= 1f;
				savedSpinTicks = 0;
				Projectile.tileCollide = true;
				Projectile.netUpdate = true;
			}
			else
				Projectile.Kill();
			return false;
		}
		return true;
	}

	// Общий полёт домой. false — долетела до рук (сама никого не убивает)
	private bool FlyHome(Player player, Vector2 anchor, float speed, float acceleration)
	{
		Vector2 toPlayer = anchor - Projectile.Center;
		float dist = toPlayer.Length();
		if (dist <= 26f)
			return false;

		Vector2 desired = player.velocity + toPlayer / dist * speed;
		Projectile.velocity = Projectile.velocity.MoveTowards(desired, acceleration);

		// Пролетела мимо рук насквозь за один тик — всё равно поймана
		Vector2 nextFromAnchor = anchor.DirectionFrom(Projectile.Center + Projectile.velocity).SafeNormalize(Vector2.Zero);
		if (Vector2.Dot(toPlayer / dist, nextFromAnchor) < 0f)
			return false;

		player.ChangeDir(Projectile.Center.X >= player.Center.X ? 1 : -1);
		return true;
	}

	// Смещение точки круга: по вертикали сплющено, нижняя половина — ещё
	// сильнее, чтобы глыба не шкрябала пол на каждом обороте
	private static Vector2 OrbitOffset(Vector2 radial, float radius, float gravDir)
	{
		Vector2 offset = radial * radius;
		offset.Y *= OrbitVerticalSquash;
		if (offset.Y * gravDir > 0f)
			offset.Y *= OrbitLowerSquash;
		return offset;
	}

	// Радиус круга у стены ужимаем, пока точка орбиты не выйдет из блоков:
	// глыба не должна телепортироваться внутрь стены ради красивой окружности
	private float FreeOrbitRadius(Vector2 anchor, float angle, float wanted, float gravDir)
	{
		Vector2 radial = angle.ToRotationVector2();
		Vector2 half = new Vector2(Projectile.width / 2f, Projectile.height / 2f);
		for (float radius = wanted; radius >= 28f; radius -= 6f)
		{
			Vector2 center = anchor + OrbitOffset(radial, radius, gravDir);
			if (!Collision.SolidCollision(center - half, Projectile.width, Projectile.height))
				return radius;
		}
		return 24f;
	}

	public override bool? CanDamage()
	{
		// Первые тики глыба только выходит на круг — не бьёт из рук в упор даром
		if (CurrentState == State.Spin && StateTimer < SpinArmTicks)
			return false;
		return base.CanDamage();
	}

	public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
	{
		if (CurrentState == State.Spin)
		{
			float ramp = SpinRamp;
			modifiers.SourceDamage *= MathHelper.Lerp(SpinDamageMin, SpinDamageMax, ramp);
			modifiers.Knockback *= MathHelper.Lerp(0.65f, 1.35f, ramp);
		}
		else if (CurrentState == State.LaunchOut || CurrentState == State.LaunchReturn)
		{
			// Выстрел: база выше броска, с крутки ещё и разгон сверху
			modifiers.SourceDamage *= LaunchDamageBase + LaunchDamageRampBonus * releaseRamp;
			modifiers.Knockback *= 0.8f + 0.3f * releaseRamp;
		}
		else if (CurrentState == State.Drop)
		{
			modifiers.SourceDamage *= DropDamageMult;
			modifiers.Knockback *= 0.5f;
		}
		else
		{
			modifiers.SourceDamage *= ThrowDamageMult;
			modifiers.Knockback *= 0.8f;
		}
		// Отброс всегда от игрока, даже когда глыба летит домой
		modifiers.HitDirectionOverride = Main.player[Projectile.owner].Center.X < target.Center.X ? 1 : -1;
	}

	public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
	{
		// Frostbite — только с разогнанной крутки и выстрела из неё, не с броска
		if ((CurrentState == State.Spin && SpinRamp >= FrostbiteRamp) ||
			(CurrentState == State.LaunchOut && releaseRamp >= FrostbiteRamp))
			target.AddBuff(BuffID.Frostburn2, FrostbiteTicks);
		// Выстрел не пробивает: первый враг разворачивает глыбу домой
		if (CurrentState == State.LaunchOut)
			BeginLaunchReturn();
		if (Main.dedServ)
			return;
		for (int i = 0; i < 5; i++)
		{
			Dust d = Dust.NewDustPerfect(target.Center + Main.rand.NextVector2Circular(8f, 8f),
				DustID.Frost, Main.rand.NextVector2Circular(2.5f, 2.5f), 120, default, Main.rand.NextFloat(0.8f, 1.2f));
			d.noGravity = true;
		}
	}

	public override bool OnTileCollide(Vector2 oldVelocity)
	{
		bool hitX = Math.Abs(oldVelocity.X - Projectile.velocity.X) > 0.01f;
		bool hitY = Math.Abs(oldVelocity.Y - Projectile.velocity.Y) > 0.01f;
		bool impact = false;

		if (CurrentState == State.Throw)
		{
			// Врезалась — глухой отскок и домой, вдоль стен не шлифует
			if (hitX)
				Projectile.velocity.X = -oldVelocity.X * 0.3f;
			if (hitY)
				Projectile.velocity.Y = -oldVelocity.Y * 0.3f;
			impact = Math.Abs(oldVelocity.X) > 4f || Math.Abs(oldVelocity.Y) > 4f;
			BeginReturn();
		}
		else if (CurrentState == State.Spin)
		{
			// У стены скользит: гасим только компоненту в стену, рулёжка
			// в следующем тике увезёт вдоль неё (как йо-йо Амарока)
			if (hitX)
				Projectile.velocity.X = 0f;
			if (hitY)
				Projectile.velocity.Y = 0f;
			impact = Math.Abs(oldVelocity.X) > 5f || Math.Abs(oldVelocity.Y) > 5f;
		}
		else if (CurrentState == State.LaunchOut)
		{
			// Стена разворачивает выстрел домой, как первый враг
			impact = Math.Abs(oldVelocity.X) > 4f || Math.Abs(oldVelocity.Y) > 4f;
			BeginLaunchReturn();
		}
		else if (CurrentState == State.Drop)
		{
			// Лежащая глыба не отскакивает: глухо села и лежит
			if (hitX)
				Projectile.velocity.X = 0f;
			if (hitY)
				Projectile.velocity.Y = 0f;
			impact = Math.Abs(oldVelocity.X) > 5f || Math.Abs(oldVelocity.Y) > 5f;
		}

		if (impact && tileSoundCooldown <= 0)
		{
			tileSoundCooldown = 8;
			SoundEngine.PlaySound(SoundID.Dig, Projectile.Center);
			if (!Main.dedServ)
			{
				for (int i = 0; i < 6; i++)
				{
					Dust d = Dust.NewDustPerfect(Projectile.Center + Main.rand.NextVector2Circular(10f, 10f),
						DustID.Frost, Main.rand.NextVector2Circular(2f, 2f), 120, default, Main.rand.NextFloat(0.7f, 1.1f));
					d.noGravity = true;
				}
			}
			Projectile.netUpdate = true;
		}
		return false;
	}

	// Цепь рисуем сами ванильной текстурой звеньев (Chain3 — та, что ваниль
	// даёт цепам без своей): от глыбы к руке игрока, звено за звеном.
	public override bool PreDraw(ref Color lightColor)
	{
		Texture2D chain = TextureAssets.Chain3.Value;
		if (chain == null || chain.IsDisposed)
			return true;

		Vector2 armPosition = Main.GetPlayerArmPosition(Projectile);
		armPosition.Y -= Main.player[Projectile.owner].gfxOffY;

		Vector2 origin = chain.Size() / 2f;
		Vector2 drawPosition = Projectile.Center;
		Vector2 toArm = armPosition.MoveTowards(drawPosition, 4f) - drawPosition;
		Vector2 unit = toArm.SafeNormalize(Vector2.Zero);
		float segmentLength = chain.Height;
		if (segmentLength <= 0f)
			segmentLength = 10f;
		float rotation = unit.ToRotation() + MathHelper.PiOver2;
		float remaining = toArm.Length() + segmentLength / 2f;

		while (remaining > 0f)
		{
			Color color = Lighting.GetColor((int)drawPosition.X / 16, (int)(drawPosition.Y / 16f));
			Main.spriteBatch.Draw(chain, drawPosition - Main.screenPosition, null, color,
				rotation, origin, 1f, SpriteEffects.None, 0f);
			drawPosition += unit * segmentLength;
			remaining -= segmentLength;
		}
		return true; // саму глыбу рисует ваниль поверх цепи
	}
}
