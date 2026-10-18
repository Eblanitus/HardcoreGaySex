using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Наледь на мече в руке. Спрайт не масштабируется — слой рисует лёд поверх клинка:
    // иней по лезвию, кристаллы по краям и ледяное жало за остриём. Линию клинка берём
    // у SwordPlayer (он знает позу всех приёмов), поэтому лёд лежит ровно на мече
    // и в стойке, и в замахе, и во взмахе.
    public class FrostGrowthLayer : PlayerDrawLayer
    {
        public override Position GetDefaultPosition() => new AfterParent(PlayerDrawLayers.HeldItem);

        public override bool GetDefaultVisibility(PlayerDrawSet drawInfo) => true;

        protected override void Draw(ref PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            if (player.HeldItem.IsAir || player.HeldItem.type != ItemID.Frostbrand)
                return;
            float growth = FrostKindle.Growth01(player);
            if (growth <= 0.01f)
                return;
            // Меч брошен или позы нет — рисовать нечего
            SwordPlayer sp = player.GetModPlayer<SwordPlayer>();
            if (!sp.TryGetBladeLine(out var butt, out var tip))
                return;
            // Тычок и встреча: меч рисует снаряд, он же рисует и наледь
            if (sp.BladeLineIsProjectile)
                return;
            FrostGrowth.Draw(butt, tip, growth);
        }
    }
}
