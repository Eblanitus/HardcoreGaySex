using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // North Pole is registered in SickleTypes, so the shared long-weapon hooks
    // supply swing, thrust, brace, throw, poses, UI and per-item throw locking.
    // Keep this profile for spear-specific defaults rather than a second input AI.
    public class NorthPoleGlobalItem : GlobalItem
    {
        public override bool AppliesToEntity(Item entity, bool lateInstantiation) =>
            entity.type == ItemID.NorthPole;

        public override void SetDefaults(Item item)
        {
            // Vanilla's held spear/channel attack is replaced by the shared moves.
            // Damage, use times, rarity, acquisition and vanilla texture stay intact.
            item.channel = false;
        }
    }
}
