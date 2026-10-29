using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using FrostMelee.Content.Weapons;

namespace FrostMelee.Content.Projectiles.Prototypes
{
    public class SnowballFlailProjectile : PrototypeFlailProjectile
    {
        protected override string SpriteName => "SnowballFlail";
        public override int SourceItemType => Terraria.ModLoader.ModContent.ItemType<global::FrostMelee.Content.Items.Prototypes.SnowballFlail>();
        private SnowballFlailPlayer Resource => Main.player[Projectile.owner].GetModPlayer<SnowballFlailPlayer>();
        private float mass;
        protected override float MovementSpeedMultiplier => 1f - 0.25f * mass;
        protected override float WeaponDamageMultiplier => 1f + 0.8f * mass;

        public override void AI()
        {
            if (Projectile.owner < 0 || Projectile.owner >= Main.maxPlayers) return;
            if (Main.netMode != NetmodeID.MultiplayerClient) Resource.CheckHeldItem();
            mass = Resource.Mass;
            Vector2 center = Projectile.Center;
            Projectile.width = Projectile.height = (int)Math.Round(28f + 16f * mass);
            Projectile.scale = Projectile.width / 32f;
            Projectile.Center = center;
            base.AI();
            if (Main.netMode != NetmodeID.MultiplayerClient && Mode == FlailMode.Spin && SpinningThisTick)
                Resource.Grow(); // 90 real moving-spin ticks, no attack-speed scaling.
        }
        public override void OnLassoYank(NPC target)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient || mass <= 0.5f) return;
            Vector2 impact = target.Center;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc.active && !npc.friendly && !npc.dontTakeDamage && npc.CanBeChasedBy()
                    && Vector2.DistanceSquared(npc.Center, impact) <= 80f * 80f) npc.AddBuff(BuffID.Slow, 120);
            }
            Resource.Clear();
            mass = 0;
            if (Main.netMode == NetmodeID.Server)
            {
                var packet = Mod.GetPacket();
                packet.Write(global::FrostMelee.FrostMelee.FlailShatterMessage);
                packet.Write(impact.X); packet.Write(impact.Y);
                packet.Send();
            }
            else ShatterFx(impact);
        }
        internal static void ShatterFx(Vector2 center)
        {
            if (Main.dedServ) return;
            for (int i = 0; i < 24; i++)
                Dust.NewDustPerfect(center, DustID.Snow, (MathHelper.TwoPi * i / 24f).ToRotationVector2() * 4f).noGravity = true;
        }
    }
}
