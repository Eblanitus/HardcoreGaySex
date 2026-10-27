using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using FrostMelee.Content.Projectiles;

namespace FrostMelee.Content.Weapons;

// ПКМ ледникового цепа — launched-половина: прямой выстрел глыбой.
// Глыба уже крутится по ЛКМ — снаряд сам увидит LaunchHeld и выстрелит
// из крутки. Глыбы нет — ПКМ выпускает её сразу выстрелом и держится
// как авто-огонь, но не чаще паузы после поимки.
public class GlacialFlailPlayer : ModPlayer
{
	public const int LaunchGapTicks = 12; // пауза между выстрелами ПКМ после поимки глыбы

	public bool LaunchHeld;

	private int launchCooldown;

	public bool HoldingFlail =>
		!Player.HeldItem.IsAir && Player.HeldItem.type == ModContent.ItemType<Items.GlacialFlail>();

	public bool Deployed =>
		Player.ownedProjectileCounts[ModContent.ProjectileType<GlacialFlailProjectile>()] > 0;

	public override void PreUpdate()
	{
		if (Player.whoAmI != Main.myPlayer)
			return;

		LaunchHeld = false;
		if (!HoldingFlail || Player.dead)
		{
			launchCooldown = 0;
			return;
		}

		bool uiBlocked = Player.mouseInterface || Main.drawingPlayerChat || Main.blockMouse ||
			Main.mapFullscreen || Main.gamePaused || Player.cursorItemIconEnabled ||
			Main.SmartInteractShowingGenuine;
		LaunchHeld = Main.mouseRight && !uiBlocked;

		if (Deployed)
		{
			launchCooldown = LaunchGapTicks; // после поимки сначала пауза, потом следующий выстрел
			return;
		}
		if (launchCooldown > 0)
			launchCooldown--;

		if (!LaunchHeld || Player.channel || Player.noItems || Player.CCed || launchCooldown > 0)
			return;

		Item held = Player.HeldItem;
		Vector2 dir = (Main.MouseWorld - Player.Center).SafeNormalize(new Vector2(Player.direction, 0f));
		Projectile.NewProjectile(Player.GetSource_ItemUse(held),
			Player.MountedCenter + dir * 20f, dir * GlacialFlailProjectile.LaunchSpeed(0f),
			ModContent.ProjectileType<GlacialFlailProjectile>(),
			Player.GetWeaponDamage(held), Player.GetWeaponKnockback(held), Player.whoAmI,
			GlacialFlailProjectile.AiLaunchOut);
		launchCooldown = LaunchGapTicks;
	}
}
