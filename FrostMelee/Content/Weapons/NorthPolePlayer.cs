using System;
using System.Collections.Generic;
using System.Linq;
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
        [DefaultValue(12), Range(0, 50)] public int AttackSpeedPercent { get; set; } = 12;
        [DefaultValue(3), Range(0, 10)] public int DawnRegenPerSecond { get; set; } = 3;
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
        public static float AttackSpeed => Math.Clamp(Tuning.AttackSpeedPercent, 0, 50) / 100f;
        public static int DawnRegen => Math.Clamp(Tuning.DawnRegenPerSecond, 0, 10) * 2;
        public const int MaxFlakes = 5;      // owner 02.11.2026: up to five flakes grow at once, one per enemy
        public static float WeakShot => Math.Clamp(Tuning.WeakShotPercent, 10, 100) / 100f;
        public const float ShotSpeed = 14f;
        public const int MiniFlakes = 5;          // the burst: small snowflakes that explode (owner 02.11.2026)
        public const float MiniFlakeDamage = 0.35f;
        public const int MiniBlastRadius = 40;
        // Brace icicle length follows the player's speed (owner 02.11.2026): standing still gives a stub,
        // a full sprint a long lance. Full length is reached at a normal top run speed.
        public const float SpikeMinLength = 32f;
        public const float SpikeMaxLength = 224f;
        public const float SpikeFullSpeed = 9f;
    }

    // This weapon follows the existing SicklePlayer owning-client damage model.
    // Only the owner creates effects/collects pickups; vanilla projectile and buff
    // synchronization distributes them. This is NOT a new server-authoritative AI.
    public class NorthPolePlayer : ModPlayer
    {
        private int missedSwings, rainCooldown;
        public const int KindBlessing = 0, KindDawn = 1;

        private static IEnumerable<NorthPoleSnowflake> ChargingFlakes(int owner)
        {
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.owner == owner && p.ModProjectile is NorthPoleSnowflake flake && flake.Charging)
                    yield return flake;
            }
        }

        private void KillChargingFlakes()
        {
            foreach (NorthPoleSnowflake flake in ChargingFlakes(Player.whoAmI).ToList())
                flake.Projectile.Kill();
        }

        public override void PostUpdate()
        {
            if (rainCooldown > 0) rainCooldown--;
            if (Player.whoAmI != Main.myPlayer) return;
            if (Player.dead || !Player.active || Player.HeldItem.type != ItemID.NorthPole)
            {
                KillChargingFlakes();
                missedSwings = 0;
            }
        }

        public override void UpdateDead()
        {
            missedSwings = rainCooldown = 0;
            if (Player.whoAmI == Main.myPlayer) KillChargingFlakes();
        }

        // One flake per enemy: a hit grows the flake of that enemy, or starts a new one
        // while fewer than MaxFlakes are growing. Each flake grows once per swing.
        public void SwingHit(Projectile arc, NPC target)
        {
            if (Player.whoAmI != Main.myPlayer || Player.dead || Player.HeldItem.type != ItemID.NorthPole) return;
            missedSwings = 0;
            NorthPoleSnowflake flake = null;
            int used = 0; // bit mask of taken hover slots
            foreach (NorthPoleSnowflake f in ChargingFlakes(Player.whoAmI))
            {
                used |= 1 << f.Slot;
                if (f.Targets(target)) flake = f;
            }
            if (flake == null)
            {
                int slot = 0;
                while (slot < NorthPoleBalance.MaxFlakes && (used & (1 << slot)) != 0) slot++;
                if (slot >= NorthPoleBalance.MaxFlakes) return; // five already growing
                int i = Projectile.NewProjectile(arc.GetSource_FromThis(), Player.Center - new Vector2(0, 64),
                    Vector2.Zero, ModContent.ProjectileType<NorthPoleSnowflake>(), arc.damage, arc.knockBack,
                    Player.whoAmI, 0, 0, target.whoAmI);
                if (i < 0 || i >= Main.maxProjectiles) return;
                flake = Main.projectile[i].ModProjectile as NorthPoleSnowflake;
                if (flake != null) flake.Slot = slot;
            }
            flake?.Grow(target, arc.damage);
        }

        // Called once on a naturally completed arc, never once per animation tick.
        // The arc only grows snowflakes; buff snow comes from thrusts only (owner 02.11.2026).
        public void FinishSwing(Projectile arc, bool hit)
        {
            if (Player.whoAmI != Main.myPlayer || Player.dead) return;
            if (hit) { missedSwings = 0; return; }
            List<NorthPoleSnowflake> flakes = ChargingFlakes(Player.whoAmI).ToList();
            if (flakes.Count == 0) { missedSwings = 0; return; }
            if (++missedSwings >= NorthPoleBalance.MissesToRelease)
            {
                missedSwings = 0;
                foreach (NorthPoleSnowflake flake in flakes) flake.Release(true);
            }
        }

        private int PickupsOfKind(int kind)
        {
            int count = 0, type = ModContent.ProjectileType<NorthPolePickup>();
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.owner == Player.whoAmI && p.type == type && (int)p.ai[2] == kind) count++;
            }
            return count;
        }

        public void CallSnow(Projectile source, int kind = KindBlessing, bool stab = true)
        {
            if (Player.whoAmI != Main.myPlayer || Player.dead || Player.HeldItem.type != ItemID.NorthPole
                || (stab && rainCooldown > 0) || PickupsOfKind(kind) >= NorthPoleBalance.MaxPickups)
                return;
            // Snow really comes from the sky: from above the top edge of the screen,
            // straight through any roof, anywhere across the whole visible width
            // (screen edges included), not only over the player's head.
            // CallSnow runs only on the owner's client, so its screen is the right one.
            float viewWidth = Main.screenWidth / Math.Max(1f, Main.GameViewMatrix.Zoom.X);
            float viewCenter = Main.screenPosition.X + Main.screenWidth * 0.5f;
            float x = MathHelper.Clamp(viewCenter + Main.rand.NextFloat(-0.5f, 0.5f) * (viewWidth - 24f), 32, Main.maxTilesX * 16 - 32);
            float y = Math.Max(32, Math.Min(Main.screenPosition.Y - 24, Player.Center.Y - 360));
            Projectile.NewProjectile(source.GetSource_FromThis(), new Vector2(x, y), new Vector2(0, 3f),
                ModContent.ProjectileType<NorthPolePickup>(), 0, 0, Player.whoAmI, 0, Main.rand.Next(3), kind);
            if (!stab) return;
            rainCooldown = NorthPoleBalance.RainCooldown;
            // Readable cue on the spear itself: a puff of rose snow motes (buff-snow colour) rises off the tip
            if (!Main.dedServ)
                for (int i = 0; i < 6; i++)
                {
                    Dust d = Dust.NewDustPerfect(source.Center + Main.rand.NextVector2Circular(6, 6), DustID.PinkTorch,
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

    // Second buff (owner 02.11.2026): a thrust that hits also calls amber
    // "Polar Dawn" snow that speeds up attacks and restores health.
    public class NorthPoleDawn : ModBuff
    {
        public override string Texture => "Terraria/Images/Buff_" + BuffID.Regeneration;
        public override void SetStaticDefaults() => Main.buffNoSave[Type] = true;
        public override void Update(Player player, ref int buffIndex)
        {
            player.GetAttackSpeed(DamageClass.Melee) += NorthPoleBalance.AttackSpeed;
            player.lifeRegen += NorthPoleBalance.DawnRegen;
        }
    }
}
