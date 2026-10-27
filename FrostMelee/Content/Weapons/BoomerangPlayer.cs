using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using FrostMelee.Content.Projectiles;

namespace FrostMelee.Content.Weapons;

// Ввод бумеранга: ЛКМ — обычный взмах в руке (ванильный предмет),
// ПКМ-тап — лёгкий бросок (сам возвращается), ПКМ-держать — зарядка с прицелом,
// отпустить — тяжёлый бросок (втыкается, идти забирать). Схема philosophy.md.
// Пока любой брошенный бумеранг жив — предметом пользоваться нельзя.
public class BoomerangPlayer : ModPlayer
{
	public const int TapThresholdTicks = 18; // как у мечей (SwordPlayer)
	public const int ChargeFullTicks = 45;
	public const float LightDamageMult = 0.8f;  // бесплатный возврат = слабый урон
	public const float HeavyDamageMin = 1.1f;
	public const float HeavyDamageMax = 2.0f;

	public bool Charging;
	public float Charge;

	private int rmbHoldTicks;
	private int lastHeldType;

	public static bool IsBoomerang(Item item) => item.type == ItemID.IceBoomerang;

	public bool HoldingBoomerang() => IsBoomerang(Player.HeldItem);

	public bool BoomerangOut() => OwnProjectileActive(ModContent.ProjectileType<BoomerangLightProjectile>())
		|| OwnProjectileActive(ModContent.ProjectileType<BoomerangHeavyProjectile>());

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
		}

		if (!IsBoomerang(held) || Player.dead) {
			ResetCharge();
			return;
		}

		bool uiBlocked = Player.mouseInterface || Main.drawingPlayerChat || Main.blockMouse
			|| Main.mapFullscreen || Main.gamePaused || Player.cursorItemIconEnabled
			|| Main.SmartInteractShowingGenuine;

		bool rmb = Main.mouseRight && !uiBlocked;
		bool swinging = Player.itemAnimation > 0;

		if (uiBlocked || BoomerangOut()) {
			ResetCharge();
			return;
		}

		if (rmb) {
			if (!swinging) {
				rmbHoldTicks++;
				if (rmbHoldTicks >= TapThresholdTicks) {
					Charging = true;
					Charge = MathHelper.Clamp((rmbHoldTicks - TapThresholdTicks) / (float)ChargeFullTicks, 0f, 1f);
					Player.ChangeDir(Main.MouseWorld.X >= Player.Center.X ? 1 : -1);
					if (Main.GameUpdateCount % 2 == 0)
						DrawAim();
				}
			}
			return;
		}

		// Отпустили ПКМ.
		if (rmbHoldTicks > 0 && !swinging) {
			if (Charging)
				HeavyThrow(held, Charge);
			else
				LightThrow(held);
		}
		ResetCharge();
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
		int damage = Math.Max(1, (int)MathF.Round(Player.GetWeaponDamage(held) * LightDamageMult));
		Projectile.NewProjectile(Player.GetSource_ItemUse(held), ThrowOrigin(dir), dir * BoomerangLightProjectile.LaunchSpeed,
			ModContent.ProjectileType<BoomerangLightProjectile>(), damage, Player.GetWeaponKnockback(held), Player.whoAmI);
		SoundEngine.PlaySound(SoundID.Item1, Player.Center);
	}

	private void HeavyThrow(Item held, float charge)
	{
		Vector2 dir = AimDirection();
		int damage = Math.Max(1, (int)MathF.Round(Player.GetWeaponDamage(held) * MathHelper.Lerp(HeavyDamageMin, HeavyDamageMax, charge)));
		Projectile.NewProjectile(Player.GetSource_ItemUse(held), ThrowOrigin(dir), dir * BoomerangHeavyProjectile.LaunchSpeed(charge),
			ModContent.ProjectileType<BoomerangHeavyProjectile>(), damage, Player.GetWeaponKnockback(held) * 1.3f, Player.whoAmI);
		SoundEngine.PlaySound(SoundID.Item1, Player.Center);
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
