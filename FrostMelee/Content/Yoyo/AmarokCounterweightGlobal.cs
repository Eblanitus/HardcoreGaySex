using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Yoyo
{
    // Ванильные противовесы втягиваются, когда ЛКМ отпущена (!channel).
    // В ПКМ-режиме наводки ЛКМ отпущена, поэтому на время их AI считаем кнопку зажатой.
    public class AmarokCounterweightGlobal : GlobalProjectile
    {
        private bool spoofed;
        private bool savedChannel;

        public override bool InstancePerEntity => true;

        public override bool AppliesToEntity(Projectile entity, bool lateInstantiation) =>
            entity.type >= ProjectileID.BlackCounterweight && entity.type <= ProjectileID.YellowCounterweight;

        public override bool PreAI(Projectile projectile)
        {
            spoofed = false;
            if (projectile.owner != Main.myPlayer)
                return true;

            Player owner = Main.player[projectile.owner];
            if (owner.GetModPlayer<AmarokPlayer>().HomingWanted)
            {
                savedChannel = owner.channel;
                owner.channel = true;
                spoofed = true;
            }
            return true;
        }

        public override void PostAI(Projectile projectile)
        {
            if (spoofed)
            {
                Main.player[projectile.owner].channel = savedChannel;
                spoofed = false;
            }
        }
    }
}
