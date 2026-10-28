using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using FrostMelee.Content.Items.Prototypes;
using FrostMelee.Content.Projectiles;

namespace FrostMelee.Content.Weapons
{
    // One input owner for Snowball, Polar Star and Lantern. The item never fires
    // through vanilla Shoot as well. Polar Star replaces the retired GlacialFlail; there is no second controller.
    public class SharedFlailPlayer : ModPlayer
    {
        public const int RepeatDelay = 10;
        public bool CanControl { get; private set; }
        public bool LeftHeld { get; private set; }
        public bool RightHeld { get; private set; }
        public bool LeftPressed { get; private set; }
        public Vector2 Aim { get; private set; } = Vector2.UnitX;

        private bool previousLeft;
        private int repeatCooldown;
        private int activeIndex = -1;

        private bool IsActiveFlail(int index) => index >= 0 && index < Main.maxProjectiles
            && Main.projectile[index].active && Main.projectile[index].owner == Player.whoAmI
            && Main.projectile[index].ModProjectile is SharedFlailProjectile;

        private bool HasActiveFlail()
        {
            if (IsActiveFlail(activeIndex))
                return true;
            activeIndex = -1;
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                if (!IsActiveFlail(i))
                    continue;
                activeIndex = i;
                return true;
            }
            return false;
        }

        public override void SetControls()
        {
            // RMB is a weapon input here, not open-door/chest/smart-interact input.
            if (Player.whoAmI == Main.myPlayer && Player.HeldItem.ModItem is PrototypeFlail
                && Main.mouseRight && !Player.mouseInterface && !Main.drawingPlayerChat
                && !Main.blockMouse && !Main.mapFullscreen)
                Player.controlUseTile = false;
        }

        // Run after vanilla has refreshed noItems/CC and processed normal item use.
        public override void PostUpdate()
        {
            if (Player.whoAmI != Main.myPlayer)
                return;

            bool blocked = Player.mouseInterface || Main.drawingPlayerChat || Main.blockMouse
                || Main.mapFullscreen || Main.gamePaused;
            CanControl = Player.active && !Player.dead && !Player.ghost && !Player.noItems && !Player.CCed
                && Player.HeldItem.ModItem is PrototypeFlail && !blocked;
            LeftHeld = CanControl && Main.mouseLeft;
            RightHeld = CanControl && Main.mouseRight;
            LeftPressed = LeftHeld && !previousLeft;
            previousLeft = Main.mouseLeft; // UI suppression must not manufacture a new click.

            if (!CanControl)
            {
                repeatCooldown = 0;
                return; // The active head sees CanControl=false and returns.
            }

            Aim = (Main.MouseWorld - Player.MountedCenter).SafeNormalize(new Vector2(Player.direction, 0f));
            if (HasActiveFlail())
            {
                repeatCooldown = RepeatDelay;
                return; // Existing heads consume click edges; never spawn a second head.
            }
            if (repeatCooldown > 0)
                repeatCooldown--;
            if (repeatCooldown > 0 || (!LeftHeld && !RightHeld))
                return;

            Item held = Player.HeldItem;
            var mode = RightHeld ? SharedFlailProjectile.FlailMode.Spin : SharedFlailProjectile.FlailMode.Flight;
            float meleeSpeed = MathHelper.Clamp(Player.GetTotalAttackSpeed(DamageClass.Melee), 0.1f, 10f);
            Vector2 velocity = mode == SharedFlailProjectile.FlailMode.Flight
                ? Aim * held.shootSpeed * meleeSpeed + Player.velocity : Vector2.Zero;
            activeIndex = Projectile.NewProjectile(Player.GetSource_ItemUse(held), Player.MountedCenter,
                velocity, held.shoot, Player.GetWeaponDamage(held), Player.GetWeaponKnockback(held),
                Player.whoAmI, (float)mode, 0f, Aim.X >= 0f ? 1f : -1f);
            if (IsActiveFlail(activeIndex))
                SoundEngine.PlaySound(SoundID.Item1, Player.Center);
            repeatCooldown = RepeatDelay;
        }
    }
}
