using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using FrostMelee.Content.Projectiles;

namespace FrostMelee.Content.Weapons;

// Ввод бумеранга: ЛКМ держать — бесконечный поток лёгких бросков (сами возвращаются),
// ПКМ-тап — размах: бьёт в упор и раскручивает следующий бросок, ПКМ-держать —
// зарядка с прицелом, отпустить — тяжёлый бросок (отскакивает и падает, идти забирать).
// Пока тяжёлый бумеранг не в руке, предметом пользоваться нельзя.
public class BoomerangPlayer : ModPlayer
{
	public const int TapThresholdTicks = 18; // как у мечей (SwordPlayer)
	public const int ChargeFullTicks = 45;
	public const int LightThrowIntervalTicks = 18; // темп лёгких бросков при зажатом ЛКМ
	public const float LightDamageMult = 0.2f;  // пугалка: возвращается сам, поэтому урона почти нет
	public const float HeavyDamageMin = 1.1f;
	public const float HeavyDamageMax = 2.0f;

	// Раскрутка от размаха (ПКМ-тап): попадание не требуется, это разминка перед броском,
	// но враг под размахом получает обычный урон взмаха.
	public const int SpinDurationTicks = 180; // 3 секунды на следующий бросок
	public const float SpinLightSpeedMult = 1.3f; // лёгкий летит на 30% быстрее и дальше
	public const float SpinHeavyStartCharge = 0.5f; // тяжёлый начинает зарядку с 50%
	public const float SpinSwingExponent = 1.8f; // разгон: медленное начало, быстрый конец (у мечей наоборот)

	// Поза зарядки тяжёлого: рука на замахе, как у броска меча (SwordPlayer).
	// Бумеранг слой сажает в кисть за нижнее крыло и ведёт по углу руки.
	public const float ChargeWindupBack = 2.4f; // угол руки назад-вверх от линии броска (рад)
	public const float WindupArcLift = 0.5f; // подъём кисти над плечом в середине замаха (рад): без него рука с бумерангом режет через бедро

	// Анимация броска: замах лёгкого (бумеранг в руке) → вылет → доработка руки.
	// Замах + доработка = темп лёгких бросков, ритм потока не меняется.
	// Дорабатывает рука и после тяжёлого броска (из позы зарядки).
	public const int LightWindupTicks = 5;
	public const int ThrowFollowTicks = 13;

	public bool Charging;
	public float Charge;
	// Позу рисует BoomerangChargePoseLayer от кисти за нижнее крыло спрайта
	public bool PoseItemVisible;
	public Vector2 PoseHandOffset;
	public float PoseItemRotation;
	public int SpinTicks;
	// ПКМ-тап просит ванильный взмах: SetControls подставляет нажатие, CanUseItem по этому
	// флагу отличает запрос от обычного ЛКМ (ЛКМ предмет сам по себе не использует).
	public bool SwingRequested;

	private int rmbHoldTicks;
	private int lightCooldown;
	private int lightWindupTicks; // >0 — замах лёгкого, вылет при нуле
	private int throwFollowTicks; // >0 — рука дорабатывает после вылета (лёгкий и тяжёлый)
	private Vector2 throwAimDir = Vector2.UnitX; // направление уже улетевшего броска
	private int swingRequestAge;
	private bool spinGrantedForSwing;
	private bool spinChargeActive;
	private int lastHeldType;

	public static bool IsBoomerang(Item item) => item.type == ItemID.IceBoomerang;

	public bool HoldingBoomerang() => IsBoomerang(Player.HeldItem);

	public bool LightOut() => OwnProjectileActive(ModContent.ProjectileType<BoomerangLightProjectile>());

	public bool HeavyOut() => OwnProjectileActive(ModContent.ProjectileType<BoomerangHeavyProjectile>());

	private bool OwnProjectileActive(int type)
	{
		for (int i = 0; i < Main.maxProjectiles; i++) {
			Projectile p = Main.projectile[i];
			if (p.active && p.owner == Player.whoAmI && p.type == type)
				return true;
		}
		return false;
	}

	private void ResetCharge()
	{
		rmbHoldTicks = 0;
		Charging = false;
		Charge = 0f;
		spinChargeActive = false;
	}

	public override void PreUpdate()
	{
		// Ввод читает только локальный игрок (в сетевой игре снаряд синхронизируется сам).
		if (Player.whoAmI != Main.myPlayer)
			return;

		Item held = Player.HeldItem;
		if (held.type != lastHeldType) {
			lastHeldType = held.type;
			ResetCharge();
			SwingRequested = false;
			SpinTicks = 0;
			spinGrantedForSwing = false;
			lightCooldown = 0;
			lightWindupTicks = 0;
			throwFollowTicks = 0;
		}

		if (!IsBoomerang(held) || Player.dead) {
			ResetCharge();
			SwingRequested = false;
			SpinTicks = 0;
			spinGrantedForSwing = false;
			lightWindupTicks = 0;
			throwFollowTicks = 0;
			return;
		}

		// Запрос взмаха живёт недолго: взмах начался или срок истёк — сбрасываем.
		// Начавшийся размах сразу даёт раскрутку — попадание для неё не нужно.
		if (SwingRequested) {
			swingRequestAge++;
			if (Player.itemAnimation > 0 && !spinGrantedForSwing) {
				SpinTicks = SpinDurationTicks;
				spinGrantedForSwing = true;
			}
			if (Player.itemAnimation > 0 || swingRequestAge > 3) {
				SwingRequested = false;
				swingRequestAge = 0;
			}
		}

		if (SpinTicks > 0)
			SpinTicks--;

		bool uiBlocked = Player.mouseInterface || Main.drawingPlayerChat || Main.blockMouse
			|| Main.mapFullscreen || Main.gamePaused || Player.cursorItemIconEnabled
			|| Main.SmartInteractShowingGenuine;

		bool rmb = Main.mouseRight && !uiBlocked;
		bool lmb = Main.mouseLeft && !uiBlocked;
		bool swinging = Player.itemAnimation > 0;

		// Анимация броска идёт своим ходом: доработка руки не зависит от кнопок
		// (и дорабатывает, пока тяжёлый в полёте — HeavyOut ниже), замах лёгкого
		// при нуле выпускает снаряд. Взмах, зарядка и UI замах отменяют.
		if (throwFollowTicks > 0)
			throwFollowTicks--;
		if (lightWindupTicks > 0) {
			if (swinging || Charging || uiBlocked)
				lightWindupTicks = 0;
			else if (--lightWindupTicks <= 0)
				LightThrow(held);
		}

		if (uiBlocked || HeavyOut()) {
			ResetCharge();
			lightCooldown = 0;
			return;
		}

		// ЛКМ держать — лёгкие броски без конца, но одновременно живёт только один:
		// следующий уходит сразу, как только предыдущий пойман. Клик начинает
		// замах — снаряд вылетает через LightWindupTicks (таймер выше).
		if (lmb && !Charging && !swinging) {
			if (lightCooldown > 0)
				lightCooldown--;
			if (lightCooldown <= 0 && lightWindupTicks <= 0 && !LightOut()) {
				lightWindupTicks = LightWindupTicks;
				lightCooldown = LightThrowIntervalTicks;
			}
		}
		else if (!lmb)
			lightCooldown = 0; // следующее нажатие бросает сразу

		if (rmb) {
			if (!swinging) {
				rmbHoldTicks++;
				if (rmbHoldTicks >= TapThresholdTicks) {
					// Раскрутка тратится в момент начала зарядки: тяжёлый стартует с 50%.
					if (!Charging && SpinTicks > 0) {
						spinChargeActive = true;
						SpinTicks = 0;
					}
					Charging = true;
					float chargeTicks = (rmbHoldTicks - TapThresholdTicks) / (float)ChargeFullTicks;
					Charge = MathHelper.Clamp((spinChargeActive ? SpinHeavyStartCharge : 0f) + chargeTicks, 0f, 1f);
					Player.ChangeDir(Main.MouseWorld.X >= Player.Center.X ? 1 : -1);
					if (Main.GameUpdateCount % 2 == 0)
						DrawAim();
				}
			}
			return;
		}

		// Отпустили ПКМ: шла зарядка — тяжёлый бросок, был тап — размах.
		if (rmbHoldTicks > 0 && !swinging) {
			if (Charging)
				HeavyThrow(held, Charge);
			else {
				SwingRequested = true;
				spinGrantedForSwing = false;
				swingRequestAge = 0;
			}
		}
		ResetCharge();
	}

	public override void SetControls()
	{
		// ПКМ-тап просит взмах: подставляем ваниле нажатие использования предмета.
		if (SwingRequested)
			Player.controlUseItem = true;
	}

	// Угол размаха: та же ванильная формула взмаха, но ход по времени нелинейный
	// с разгоном (степень > 1): начало медленное, конец быстрый. У мечей кривая
	// обратная. Один источник угла на двоих: отрисовка (PostUpdate ниже) и
	// хитбокс удара (IceBoomerangGlobalItem.UseItemHitbox).
	public static float SpinSwingRotation(Player player)
	{
		if (player.itemAnimationMax <= 0)
			return player.itemRotation;
		float progress = MathHelper.Clamp(1f - player.itemAnimation / (float)player.itemAnimationMax, 0f, 1f);
		float eased = MathF.Pow(progress, SpinSwingExponent);
		return SwingRotationForRemaining(player, 1f - eased);
	}

	// Та же ванильная формула по «оставшейся доле» взмаха: 1 — полный замах.
	// Только для самого размаха и его хитбокса: позы бросков ведут бумеранг
	// по руке (HeldBoomerangRotation), иначе на части углов он висит отдельно.
	public static float SwingRotationForRemaining(Player player, float remaining)
	{
		float rotation = (remaining - 0.5f) * -player.direction * 3.5f - player.direction * 0.3f;
		return player.gravDir == -1f ? -rotation : rotation;
	}

	public override void PostUpdate()
	{
		if (Player.whoAmI != Main.myPlayer)
			return;
		PoseItemVisible = false;
		if (!HoldingBoomerang())
			return;
		if (Charging) {
			ApplyChargePose();
			return;
		}
		if (ApplyThrowPose())
			return;
		// Ваниль каждый тик ставит свой линейный itemRotation — перебиваем его
		// разгонной кривой размаха перед отрисовкой, как SwordPlayer для мечей.
		if (Player.itemAnimation > 0 && Player.itemAnimationMax > 0)
			Player.itemRotation = SpinSwingRotation(Player);
	}

	// Замах зарядки тяжёлого: рука уходит назад-вверх от линии броска, как замах
	// броска меча (SwordPlayer.ApplyThrowPose). Глубина замаха растёт с зарядом
	// и выходит на полную раньше полного заряда. Бумеранг в руке рисует
	// BoomerangChargePoseLayer — ванилла предмет в покое не рисует.
	private void ApplyChargePose()
	{
		Vector2 aim = AimDirection();
		float p = MathHelper.Clamp(Charge * 1.6f, 0f, 1f);
		int dirSign = aim.X >= 0 ? 1 : -1;
		float back = aim.ToRotation() - dirSign * ChargeWindupBack;
		float armWorld = WindupArmAngle(back, p);
		Player.CompositeArmStretchAmount stretch = p < 0.5f
			? Player.CompositeArmStretchAmount.None
			: Player.CompositeArmStretchAmount.Quarter;
		float armRot = armWorld - MathHelper.PiOver2;
		Player.SetCompositeArmFront(true, stretch, armRot);
		SickleStabProjectile.HideBackArm(Player, armWorld.ToRotationVector2());

		PoseItemVisible = true;
		PoseItemRotation = HeldBoomerangRotation(armWorld);
		PoseHandOffset = SickleSprite.HandGrip(Player, stretch, armRot) - Player.Center;
	}

	// Анимация броска: замах лёгкого (бумеранг в руке — рисует тот же слой,
	// что и зарядку) → рука выбрасывается по линии броска и возвращается
	// вниз, как доработка у мечей. После тяжёлого — та же доработка из позы
	// зарядки, без замаха. В доработке рука уже пустая: предмет летит снарядом.
	private bool ApplyThrowPose()
	{
		bool windup = lightWindupTicks > 0;
		if (!windup && throwFollowTicks <= 0)
			return false;
		if (!windup && Player.itemAnimation > 0) {
			throwFollowTicks = 0; // начался размах — рука его, не доработки
			return false;
		}

		Vector2 aim;
		float armWorld;
		Player.CompositeArmStretchAmount stretch;
		if (windup) {
			aim = AimDirection();
			float p = MathHelper.Clamp(1f - (lightWindupTicks - 1f) / LightWindupTicks, 0f, 1f);
			int dirSign = aim.X >= 0 ? 1 : -1;
			float back = aim.ToRotation() - dirSign * ChargeWindupBack;
			armWorld = WindupArmAngle(back, p);
			stretch = p < 0.5f
				? Player.CompositeArmStretchAmount.None
				: Player.CompositeArmStretchAmount.Quarter;
		}
		else {
			aim = throwAimDir;
			float t = 1f - throwFollowTicks / (float)ThrowFollowTicks;
			float back = aim.ToRotation() - (aim.X >= 0 ? 1 : -1) * ChargeWindupBack;
			if (t < 0.3f) {         // выброс: от замаха к линии броска
				armWorld = LerpAngle(back, aim.ToRotation(), t / 0.3f);
				stretch = Player.CompositeArmStretchAmount.ThreeQuarters;
			}
			else if (t < 0.55f) {   // рука замерла вытянутой
				armWorld = aim.ToRotation();
				stretch = Player.CompositeArmStretchAmount.Full;
			}
			else {                  // возврат вниз
				armWorld = LerpAngle(aim.ToRotation(), MathHelper.PiOver2, (t - 0.55f) / 0.45f);
				stretch = Player.CompositeArmStretchAmount.ThreeQuarters;
			}
		}

		Player.ChangeDir(aim.X >= 0 ? 1 : -1);
		float armRot = armWorld - MathHelper.PiOver2;
		Player.SetCompositeArmFront(true, stretch, armRot);
		SickleStabProjectile.HideBackArm(Player, armWorld.ToRotationVector2());

		if (windup) {
			PoseItemVisible = true;
			PoseItemRotation = HeldBoomerangRotation(armWorld);
			PoseHandOffset = SickleSprite.HandGrip(Player, stretch, armRot) - Player.Center;
		}
		return true;
	}

	// Хват позы — нижний конец спрайта (реальное крыло, не пустой угол кадра).
	// Ванильный origin взмаха стоит в пустом углу, а его угол не зависит от
	// руки: на части углов прицела бумеранг уезжал от кисти. Здесь ось
	// «хват → дальнее крыло» идёт ровно по руке, поэтому хват не разъезжается.
	internal static Vector2 HeldGrip(SickleSprite.Geometry geo, bool flipped, out Vector2 far)
	{
		Vector2 butt = geo.ButtFor(flipped);
		Vector2 tip = geo.TipFor(flipped);
		far = butt.Y > tip.Y ? tip : butt;
		return butt.Y > tip.Y ? butt : tip;
	}

	private float HeldBoomerangRotation(float armWorld)
	{
		SickleSprite.Geometry geo = SickleSprite.Get(Player.HeldItem.type);
		Vector2 grip = HeldGrip(geo, Player.direction == -1, out Vector2 far);
		Vector2 axis = far - grip;
		if (Player.gravDir == -1f)
			axis.Y = -axis.Y;
		return armWorld - axis.ToRotation();
	}

	private static float LerpAngle(float a, float b, float t) =>
		a + MathHelper.WrapAngle(b - a) * MathHelper.Clamp(t, 0f, 1f);

	// Путь руки на замахе: не прямым поворотом от «вниз» к «назад-вверх»
	// (кисть с бумерангом резала через бедро и дёргалась рывком), а дугой
	// через плечо: в середине поворота рука приподнята на WindupArcLift
	// в сторону хода, а сам ход сглажен smoothstep'ом — без рывка
	// на старте и в конце. Концы те же, что у прямого поворота.
	private float WindupArmAngle(float back, float p)
	{
		p = MathHelper.Clamp(p, 0f, 1f);
		float e = p * p * (3f - 2f * p);
		float travel = MathHelper.WrapAngle(back - MathHelper.PiOver2);
		float lift = MathF.Sin(e * MathHelper.Pi) * WindupArcLift * MathF.Sign(travel) * Player.gravDir;
		return MathHelper.PiOver2 + travel * e + lift;
	}

	private Vector2 ThrowOrigin(Vector2 dir)
	{
		Vector2 origin = Player.MountedCenter + dir * 16f;
		if (!Collision.CanHit(Player.MountedCenter, 0, 0, origin, 0, 0))
			origin = Player.MountedCenter; // стена вплотную — спавним в себе, не в тайле
		return origin;
	}

	private Vector2 AimDirection() => (Main.MouseWorld - Player.MountedCenter).SafeNormalize(new Vector2(Player.direction, 0f));

	private void LightThrow(Item held)
	{
		Vector2 dir = AimDirection();
		// Раскрутка тратится на первый же лёгкий: быстрее стартует — дальше улетает.
		bool spun = SpinTicks > 0;
		if (spun)
			SpinTicks = 0;
		float speed = BoomerangLightProjectile.LaunchSpeed * (spun ? SpinLightSpeedMult : 1f);
		int damage = Math.Max(1, (int)MathF.Round(Player.GetWeaponDamage(held) * LightDamageMult));
		Projectile.NewProjectile(Player.GetSource_ItemUse(held), ThrowOrigin(dir), dir * speed,
			ModContent.ProjectileType<BoomerangLightProjectile>(), damage, Player.GetWeaponKnockback(held), Player.whoAmI);
		SoundEngine.PlaySound(SoundID.Item1, Player.Center);
		throwAimDir = dir;
		throwFollowTicks = ThrowFollowTicks;
	}

	private void HeavyThrow(Item held, float charge)
	{
		Vector2 dir = AimDirection();
		int damage = Math.Max(1, (int)MathF.Round(Player.GetWeaponDamage(held) * MathHelper.Lerp(HeavyDamageMin, HeavyDamageMax, charge)));
		Projectile.NewProjectile(Player.GetSource_ItemUse(held), ThrowOrigin(dir), dir * BoomerangHeavyProjectile.LaunchSpeed(charge),
			ModContent.ProjectileType<BoomerangHeavyProjectile>(), damage, Player.GetWeaponKnockback(held) * 1.3f, Player.whoAmI);
		SoundEngine.PlaySound(SoundID.Item1, Player.Center);
		throwAimDir = dir;
		throwFollowTicks = ThrowFollowTicks;
	}

	// Прицел тяжёлого броска: точки пыли по полной симулированной траектории —
	// полёт, отскоки от тайлов, отскок от первого врага и падение до места остановки.
	// Симуляция живёт в BoomerangHeavyProjectile.TraceAim на общих константах.
	private void DrawAim()
	{
		Vector2 dir = AimDirection();
		var points = BoomerangHeavyProjectile.TraceAim(ThrowOrigin(dir), dir * BoomerangHeavyProjectile.LaunchSpeed(Charge));
		for (int i = 0; i < points.Count; i += 3) {
			Dust d = Dust.NewDustDirect(points[i] - new Vector2(2f), 4, 4, DustID.Frost, 0f, 0f, 150, default, 0.7f);
			d.noGravity = true;
			d.velocity = Vector2.Zero;
		}
		Dust end = Dust.NewDustDirect(points[^1] - new Vector2(6f), 12, 12, DustID.SnowflakeIce, 0f, 0f, 60, default, 1.1f);
		end.noGravity = true;
		end.velocity = Vector2.Zero;
	}
}
