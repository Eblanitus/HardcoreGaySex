using System;
using System.ComponentModel;
using Terraria.ModLoader.Config;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using FrostMelee.Content.Projectiles;

namespace FrostMelee.Content.Weapons
{
    public class NorthPoleTuning : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ServerSide;
        [DefaultValue(6), Range(2, 12)] public int GrowthHits { get; set; } = 6;
        [DefaultValue(6), Range(1, 20)] public int BlessingSeconds { get; set; } = 6;
        [DefaultValue(15), Range(0, 50)] public int MoveSpeedPercent { get; set; } = 15;
        [DefaultValue(2), Range(0, 10)] public int RegenPerSecond { get; set; } = 2;
        [DefaultValue(25), Range(0, 100)] public int CritDamagePercent { get; set; } = 25;
        [DefaultValue(55), Range(10, 100)] public int WeakShotPercent { get; set; } = 55;
    }

    // Provisional balance, NOT additional design decisions. Shared by all four moves.
    internal static class NorthPoleBalance
    {
        private static NorthPoleTuning Tuning => ModContent.GetInstance<NorthPoleTuning>();
        public static int FullCharge => Math.Clamp(Tuning.GrowthHits, 2, 12);
        public const int MissesToRelease = 2;
        public const int FrostbiteTicks = 240;
        public const int RainCooldown = 20;
        public const int MaxPickups = 6;
        public const int PickupLife = 480; // long enough to fall from the screen top past the player
        public static int BlessingTicks => Math.Clamp(Tuning.BlessingSeconds, 1, 20) * 60;
        public static float MoveSpeed => Math.Clamp(Tuning.MoveSpeedPercent, 0, 50) / 100f;
        public static int LifeRegen => Math.Clamp(Tuning.RegenPerSecond, 0, 10) * 2;
        public static float CritDamage => Math.Clamp(Tuning.CritDamagePercent, 0, 100) / 100f;
        public const int TrailInterval = 8; // vanilla North Pole's steady-state interval
        public const float TrailDamage = 0.7f;
        public static float WeakShot => Math.Clamp(Tuning.WeakShotPercent, 10, 100) / 100f;
        public const float ShotSpeed = 14f;
        public const int Shards = 6;
        public const float ShardDamage = 0.3f;
        public const float SpikeMinLength = 64f;
        public const float SpikeMaxLength = 192f;
        public const float SpikeFullSpeed = 12f;
    }

    // This weapon follows the existing SicklePlayer owning-client damage model.
    // Only the owner creates effects/collects pickups; vanilla projectile and buff
    // synchronization distributes them. This is NOT a new server-authoritative AI.
    public class NorthPolePlayer : ModPlayer
    {
        private int missedSwings, rainCooldown;

        private NorthPoleSnowflake ChargingFlake()
        {
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.owner == Player.whoAmI && p.ModProjectile is NorthPoleSnowflake flake && flake.Charging)
                    return flake;
            }
            return null;
        }

        public override void PostUpdate()
        {
            if (rainCooldown > 0) rainCooldown--;
            if (Player.whoAmI != Main.myPlayer) return;
            if (Player.dead || !Player.active || Player.HeldItem.type != ItemID.NorthPole)
            {
                ChargingFlake()?.Projectile.Kill();
                missedSwings = 0;
            }
        }

        public override void UpdateDead()
        {
            missedSwings = rainCooldown = 0;
            if (Player.whoAmI == Main.myPlayer) ChargingFlake()?.Projectile.Kill();
        }

        public void SwingHit(Projectile arc, NPC target)
        {
            if (Player.whoAmI != Main.myPlayer || Player.dead || Player.HeldItem.type != ItemID.NorthPole) return;
            missedSwings = 0;
            NorthPoleSnowflake flake = ChargingFlake();
            if (flake == null)
            {
                int i = Projectile.NewProjectile(arc.GetSource_FromThis(), Player.Center - new Vector2(0, 64),
                    Vector2.Zero, ModContent.ProjectileType<NorthPoleSnowflake>(), arc.damage, arc.knockBack,
                    Player.whoAmI, 0, 0, target.whoAmI);
                if (i < 0 || i >= Main.maxProjectiles) return;
                flake = Main.projectile[i].ModProjectile as NorthPoleSnowflake;
            }
            flake?.Grow(target, arc.damage);
        }

        // Called once on a naturally completed arc, never once per animation tick.
        public void FinishSwing(bool hit)
        {
            if (Player.whoAmI != Main.myPlayer || Player.dead) return;
            if (hit) { missedSwings = 0; return; }
            NorthPoleSnowflake flake = ChargingFlake();
            if (flake == null) { missedSwings = 0; return; }
            if (++missedSwings >= NorthPoleBalance.MissesToRelease)
            {
                missedSwings = 0;
                flake.Release(true);
            }
        }

        public void CallSnow(Projectile stab)
        {
            if (Player.whoAmI != Main.myPlayer || Player.dead || Player.HeldItem.type != ItemID.NorthPole
                || rainCooldown > 0 || Player.ownedProjectileCounts[ModContent.ProjectileType<NorthPolePickup>()] >= NorthPoleBalance.MaxPickups)
                return;
            // Snow really comes from the sky: from above the top edge of the screen,
            // straight through any roof, anywhere across the whole visible width
            // (screen edges included), not only over the player's head.
            // CallSnow runs only on the owner's client, so its screen is the right one.
            float viewWidth = Main.screenWidth / Math.Max(1f, Main.GameViewMatrix.Zoom.X);
            float viewCenter = Main.screenPosition.X + Main.screenWidth * 0.5f;
            float x = MathHelper.Clamp(viewCenter + Main.rand.NextFloat(-0.5f, 0.5f) * (viewWidth - 24f), 32, Main.maxTilesX * 16 - 32);
            float y = Math.Max(32, Math.Min(Main.screenPosition.Y - 24, Player.Center.Y - 360));
            Projectile.NewProjectile(stab.GetSource_FromThis(), new Vector2(x, y), new Vector2(0, 3f),
                ModContent.ProjectileType<NorthPolePickup>(), 0, 0, Player.whoAmI, 0, Main.rand.Next(3));
            rainCooldown = NorthPoleBalance.RainCooldown;
            // Readable cue on the spear itself: a puff of warm snow motes rises off the tip
            if (!Main.dedServ)
                for (int i = 0; i < 6; i++)
                {
                    Dust d = Dust.NewDustPerfect(stab.Center + Main.rand.NextVector2Circular(6, 6), DustID.GoldFlame,
                        new Vector2(Main.rand.NextFloat(-0.6f, 0.6f), -Main.rand.NextFloat(1.5f, 3.5f)), 0, default, 0.8f);
                    d.noGravity = true;
                }
        }

        public override void ModifyHitNPCWithItem(Item item, NPC target, ref NPC.HitModifiers modifiers)
        {
            if (Player.HasBuff(ModContent.BuffType<NorthPoleBlessing>())) modifiers.CritDamage += NorthPoleBalance.CritDamage;
        }
        public override void ModifyHitNPCWithProj(Projectile proj, NPC target, ref NPC.HitModifiers modifiers)
        {
            if (Player.HasBuff(ModContent.BuffType<NorthPoleBlessing>())) modifiers.CritDamage += NorthPoleBalance.CritDamage;
        }
    }

    public class NorthPoleBlessing : ModBuff
    {
        public override string Texture => "Terraria/Images/Buff_" + BuffID.Warmth;
        public override void SetStaticDefaults() => Main.buffNoSave[Type] = true;
        public override void Update(Player player, ref int buffIndex)
        {
            player.moveSpeed += NorthPoleBalance.MoveSpeed;
            player.lifeRegen += NorthPoleBalance.LifeRegen;
        }
    }
}
