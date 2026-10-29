using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Read-only spear view of the shared controller. Do not maintain two input
    // machines: SicklePlayer already handles timings, cancellation and projectiles.
    // This is an extension point for future North Pole mechanics, not a second AI.
    public class SpearPlayer : ModPlayer
    {
        public enum SpearMove { None, Swing, Thrust, Meet, Throw }

        public bool HoldingSpear => !Player.HeldItem.IsAir && Player.HeldItem.type == ItemID.NorthPole;

        public SpearMove CurrentMove
        {
            get
            {
                if (!HoldingSpear || Player.dead)
                    return SpearMove.None;
                var shared = Player.GetModPlayer<SicklePlayer>();
                if (shared.HeldThrown)
                    return SpearMove.Throw;
                return shared.CurrentMode switch
                {
                    SicklePlayer.Mode.Stab => SpearMove.Thrust,
                    SicklePlayer.Mode.Spear => SpearMove.Meet,
                    _ => SpearMove.Swing
                };
            }
        }
    }
}
