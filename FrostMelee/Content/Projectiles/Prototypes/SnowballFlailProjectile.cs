using System;
using System.IO;
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
        private bool started;
        private int syncTicks;
        protected override float MovementSpeedMultiplier => 1f - 0.25f * mass;
        public override int LassoDamage => (int)Math.Round(Projectile.damage * (1f + 0.8f * mass));

        public override void AI()
        {
            if (Projectile.owner < 0 || Projectile.owner >= Main.maxPlayers)
            {
                Projectile.Kill();
                return;
            }
            Player player = Main.player[Projectile.owner];
            Resource.CheckHeldItem();
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                if (player.active && !player.dead && !player.ghost && !player.noItems && !player.CCed
                    && player.HeldItem.type == SourceItemType && Mode == FlailMode.Spin)
                    Resource.Grow(); // Real ticks: attack speed does not shorten the 1.5 seconds.
                mass = Resource.Mass;
                if (!started || ++syncTicks >= 6)
                {
                    syncTicks = 0;
                    Projectile.netUpdate = true;
                }
            }
            else if (!started && Projectile.owner == Main.myPlayer)
                mass = Resource.Mass; // Cached across head returns; corrected by server snapshots.

            if (!started && Projectile.owner == Main.myPlayer && Mode == FlailMode.Flight)
                Projectile.velocity = player.velocity + (Projectile.velocity - player.velocity) * MovementSpeedMultiplier;
            if (!started && Projectile.owner == Main.myPlayer)
                Projectile.netUpdate = true;
            started = true;
            ResizeHead();
            base.AI();
        }

        private void ResizeHead()
        {
            Vector2 center = Projectile.Center;
            Projectile.width = Projectile.height = (int)Math.Round(28f + 16f * mass);
            // Placeholder sprite is 32px; visible head and damage box share a size.
            Projectile.scale = Projectile.width / 32f;
            Projectile.Center = center;
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            base.ModifyHitNPC(target, ref modifiers);
            modifiers.SourceDamage *= 1f + 0.8f * mass;
        }

        public override void ModifyHitPlayer(Player target, ref Player.HurtModifiers modifiers)
        {
            modifiers.SourceDamage *= 1f + 0.8f * mass;
        }

        // Unlike the shared scaffold, a snowball pierces: every target receives
        // the same mass bonus. Walls, chain length and timeout still return it.
        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) { }
        public override void OnHitPlayer(Player target, Player.HurtInfo info) { }

        public override void OnLassoYank(NPC target)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient || mass <= 0.5f)
                return;
            Vector2 impact = target.Center;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc.active && !npc.friendly && !npc.dontTakeDamage && npc.CanBeChasedBy()
                    && Vector2.DistanceSquared(npc.Center, impact) <= 80f * 80f)
                    npc.AddBuff(BuffID.Slow, 120);
            }
            ShatterFx(impact);
            Resource.Clear();
            mass = 0f;
            ResizeHead();
            Projectile.netUpdate = true;
        }

        private static void ShatterFx(Vector2 center)
        {
            if (Main.dedServ)
                return;
            for (int i = 0; i < 24; i++)
                Dust.NewDustPerfect(center, DustID.Snow,
                    (MathHelper.TwoPi * i / 24f).ToRotationVector2() * 4f).noGravity = true;
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            base.SendExtraAI(writer);
            writer.Write((byte)Math.Round(mass * SnowballFlailPlayer.FullGrowthTicks));
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            base.ReceiveExtraAI(reader);
            int ticks = Math.Clamp((int)reader.ReadByte(), 0, SnowballFlailPlayer.FullGrowthTicks);
            // Owner packets control movement, never the server's snow resource.
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;
            if (mass > 0.5f && ticks == 0 && Mode == FlailMode.Wrap)
                ShatterFx(Projectile.Center);
            mass = ticks / (float)SnowballFlailPlayer.FullGrowthTicks;
            Resource.ReceiveGrowth(ticks);
            ResizeHead();
        }
    }
}
