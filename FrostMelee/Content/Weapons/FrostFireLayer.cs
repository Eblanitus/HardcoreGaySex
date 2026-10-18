using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Ледяной огонь на мече в руке: слой рисует пламя поверх клинка.
    // Линию клинка отдаёт SwordPlayer.TryGetBladeLine — он знает позу всех
    // приёмов, поэтому огонь лежит ровно на мече и в стойке, и в замахе, и во взмахе.
    public class FrostFireLayer : PlayerDrawLayer
    {
        public override Position GetDefaultPosition() => new AfterParent(PlayerDrawLayers.HeldItem);

        public override bool GetDefaultVisibility(PlayerDrawSet drawInfo) => true;

        protected override void Draw(ref PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            if (player.HeldItem.IsAir || player.HeldItem.type != ItemID.Frostbrand)
                return;
            float heat = FrostKindle.Growth01(player);
            if (heat <= 0.01f)
                return;

            SwordPlayer sp = player.GetModPlayer<SwordPlayer>();
            if (!sp.TryGetBladeLine(out var butt, out var tip))
                return;
            // Тычок и встреча: меч рисует снаряд, он же рисует и огонь
            if (sp.BladeLineIsProjectile)
                return;
            // В кэш слоя — огонь ляжет поверх меча
            FrostFire.Draw(ref drawInfo, butt, tip, heat);
        }
    }
}
