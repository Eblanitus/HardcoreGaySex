using Terraria;
using Terraria.ModLoader;
using FrostMelee.Content.Items.Prototypes;

namespace FrostMelee.Content.Weapons
{
    // Session resource, not saved to an item/world. Returning the head does not
    // consume it; switching slots/instances or dying does. Growth is server-owned.
    public class SnowballFlailPlayer : ModPlayer
    {
        public const int FullGrowthTicks = 90;
        public int GrowthTicks { get; private set; }
        public float Mass => GrowthTicks / (float)FullGrowthTicks;
        private Item heldInstance;
        private int heldSlot = -1;

        public void CheckHeldItem()
        {
            Item held = Player.HeldItem;
            if (!Player.active || Player.dead || Player.ghost || held.ModItem is not SnowballFlail
                || heldSlot != Player.selectedItem || !ReferenceEquals(heldInstance, held))
                GrowthTicks = 0;
            heldInstance = held;
            heldSlot = Player.selectedItem;
        }

        public override void PostUpdate() => CheckHeldItem();
        public override void UpdateDead() => GrowthTicks = 0;
        public void Grow() => GrowthTicks = System.Math.Min(FullGrowthTicks, GrowthTicks + 1);
        public void Clear() => GrowthTicks = 0;
        public void ReceiveGrowth(int ticks)
        {
            CheckHeldItem();
            if (Player.HeldItem.ModItem is SnowballFlail)
                GrowthTicks = System.Math.Clamp(ticks, 0, FullGrowthTicks);
        }
    }
}
