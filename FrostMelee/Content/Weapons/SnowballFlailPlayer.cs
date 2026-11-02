using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using FrostMelee.Content.Items.Prototypes;

namespace FrostMelee.Content.Weapons
{
    // Independent server-owned resource packets never rewrite projectile phase.
    public class SnowballFlailPlayer : ModPlayer
    {
        public const int FullGrowthTicks = 90;
        public int GrowthTicks { get; private set; }
        public float Mass => GrowthTicks / (float)FullGrowthTicks;
        private Item heldInstance;
        private int heldSlot = -1, lastSent = -1, syncTicks;
        public void CheckHeldItem()
        {
            Item held = Player.HeldItem;
            if (!Player.active || Player.dead || Player.ghost || held.ModItem is not SnowballFlail
                || heldSlot != Player.selectedItem || !ReferenceEquals(heldInstance, held)) GrowthTicks = 0;
            heldInstance = held;
            heldSlot = Player.selectedItem;
        }
        public override void PostUpdate()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient) return;
            CheckHeldItem();
            if (++syncTicks >= 6 || (GrowthTicks == 0 && lastSent != 0))
            {
                syncTicks = 0;
                if (GrowthTicks != lastSent) SendMass();
            }
        }
        public override void UpdateDead()
        {
            if (Main.netMode != NetmodeID.MultiplayerClient) Clear();
        }
        public void Grow() => GrowthTicks = System.Math.Min(FullGrowthTicks, GrowthTicks + 1);
        public void Clear()
        {
            if (GrowthTicks == 0) return;
            GrowthTicks = 0;
            SendMass();
        }
        public void ReceiveGrowth(int ticks) => GrowthTicks = System.Math.Clamp(ticks, 0, FullGrowthTicks);
        public override void SyncPlayer(int toWho, int fromWho, bool newPlayer) => SendMass(toWho);
        private void SendMass(int toWho = -1)
        {
            if (Main.netMode != NetmodeID.Server) return;
            ModPacket packet = Mod.GetPacket();
            packet.Write(global::FrostMelee.FrostMelee.SnowballMassMessage);
            packet.Write((byte)Player.whoAmI);
            packet.Write((byte)GrowthTicks);
            packet.Send(toWho);
            lastSent = GrowthTicks;
        }
    }
}
