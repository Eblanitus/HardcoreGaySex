using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using FrostMelee.Content.Projectiles;

namespace FrostMelee.Content
{
    // Ледяное горение мечей (Ice Blade, Frostbrand).
    //
    // Прямой удар его НЕ вешает — иначе любой моб горит с первого тычка.
    // Меч только студит: каждый удар в морозном месте даёт стак «холода».
    // Морозно — это иней от взмаха под врагом, вода, дождь или снежный биом.
    // Три стака — враг вспыхивает синим (ванильный Frostburn2).
    // Вне мороза удар даёт ровно один стак, и тот гаснет за две секунды:
    // сколько ни бей, поджечь не выйдет.
    public static class FrostKindle
    {
        public const int MaxChill = 3;
        public const int ChillLife = 240;          // 4 с — сколько живёт набранный холод
        public const int LoneChillLife = 120;      // 2 с — одиночный стак вне мороза
        public const int BurnTicks = 300;          // 5 с ледяного горения
        public const int SpreadBurnTicks = 180;    // 3 с соседям от умершего горящего
        public const int SnowStacks = 2;           // в снежном биоме удар считается за два
        public const float ColdRadius = 26f;       // насколько близко к пятну инея считается «на льду»

        // Ice Blade: бьёт по горящим сильнее и продлевает горение
        public const float IceBladeDamageMult = 1.18f;
        public const int IceBladeExtendTicks = 60; // +1 с

        // Frostbrand: за каждого горящего, которого ударил, взмах длиннее
        public const float ReachPerCharge = 5f;    // px к радиусу взмаха за заряд
        public const int MaxReachCharges = 5;
        public const int ReachLife = 480;          // 8 с живёт заряд
        public const float BurnSpreadRadius = 60f; // умерший горящий поджигает соседей

        // Удар ледяным мечом по врагу. itemType — какой именно меч ударил.
        public static void OnSwordHit(Player owner, NPC target, int itemType)
        {
            if (owner == null || target == null || !target.active || target.friendly || target.immortal)
                return;

            FrostKindleNPC g = target.GetGlobalNPC<FrostKindleNPC>();
            bool cold = IsCold(owner, target);

            if (cold)
            {
                int add = owner.ZoneSnow ? SnowStacks : 1;
                g.Chill = Math.Min(MaxChill, g.Chill + add);
                g.ChillTime = ChillLife;
                if (g.Chill >= MaxChill)
                    Ignite(target, g);
            }
            else if (g.Chill <= 1)
            {
                // Вне мороза холод не копится: один стак и короткая жизнь
                g.Chill = 1;
                g.ChillTime = LoneChillLife;
            }
            else
            {
                // Уже набранное в морозе не сбрасываем, но и не пополняем — только тает
                g.ChillTime = LoneChillLife;
            }

            if (target.HasBuff(BuffID.Frostburn2))
            {
                if (itemType == ItemID.IceBlade)
                {
                    int idx = target.FindBuffIndex(BuffID.Frostburn2);
                    if (idx >= 0)
                        target.buffTime[idx] = Math.Min(target.buffTime[idx] + IceBladeExtendTicks,
                            BurnTicks + IceBladeExtendTicks);
                }
                else if (itemType == ItemID.Frostbrand)
                {
                    // Фростборн растёт от горящих: по одному заряду на врага
                    owner.GetModPlayer<FrostKindlePlayer>().AddReachCharge(target.whoAmI);
                }
            }

            ChillDust(target, g.Chill);
        }

        // Морозно ли там, где стоит враг
        public static bool IsCold(Player owner, NPC npc)
        {
            if (Main.raining)
                return true;
            if (npc.wet || Collision.WetCollision(npc.position, npc.width, npc.height))
                return true;
            if (owner != null && owner.ZoneSnow)
                return true;
            // Иней от взмаха: свои же снежные полосы под ногами
            return FrostTrailSystem.AnyNear(npc.Center, npc.width * 0.5f + ColdRadius);
        }

        // Поджиг: синий ожог + вспышка искр
        private static void Ignite(NPC npc, FrostKindleNPC g)
        {
            npc.AddBuff(BuffID.Frostburn2, BurnTicks);
            g.Chill = 0;
            g.ChillTime = 0;

            if (Main.dedServ)
                return;
            for (int i = 0; i < 14; i++)
            {
                Dust d = Dust.NewDustPerfect(npc.Center + Main.rand.NextVector2Circular(npc.width * 0.5f, npc.height * 0.5f),
                    i % 2 == 0 ? DustID.SnowflakeIce : DustID.Frost,
                    Main.rand.NextVector2Circular(2.2f, 2.2f) - new Vector2(0f, 0.8f), 100, default,
                    Main.rand.NextFloat(1f, 1.6f));
                d.noGravity = true;
            }
        }

        // Умерший горящий поджигает соседей
        public static void Spread(NPC dead)
        {
            float r2 = BurnSpreadRadius * BurnSpreadRadius;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC other = Main.npc[i];
                if (!other.active || other.whoAmI == dead.whoAmI || other.friendly ||
                    other.dontTakeDamage || other.immortal)
                    continue;
                if (Vector2.DistanceSquared(other.Center, dead.Center) > r2)
                    continue;
                other.AddBuff(BuffID.Frostburn2, SpreadBurnTicks);
                other.GetGlobalNPC<FrostKindleNPC>().Chill = 0;
            }

            if (Main.dedServ)
                return;
            for (int i = 0; i < 10; i++)
            {
                float ang = MathHelper.TwoPi * i / 10f;
                Dust d = Dust.NewDustPerfect(dead.Center + ang.ToRotationVector2() * 12f,
                    DustID.Frost, ang.ToRotationVector2() * 2.4f, 120, default, 1.1f);
                d.noGravity = true;
            }
        }

        // Холод висит на враге, пока не растает: редкие искры, тем гуще, чем больше стаков
        public static void ChillAura(NPC npc, int chill)
        {
            if (Main.dedServ || chill <= 0)
                return;
            Dust d = Dust.NewDustPerfect(
                npc.Center + Main.rand.NextVector2Circular(npc.width * 0.5f, npc.height * 0.5f),
                DustID.Frost, new Vector2(0f, -0.25f), 150, default, 0.55f + 0.12f * chill);
            d.noGravity = true;
        }

        // Мелкий иней на враге: видно, сколько он уже набрал
        private static void ChillDust(NPC npc, int chill)
        {
            if (Main.dedServ || chill <= 0)
                return;
            int count = 1 + chill;
            for (int i = 0; i < count; i++)
            {
                Dust d = Dust.NewDustPerfect(npc.Center + Main.rand.NextVector2Circular(npc.width * 0.45f, npc.height * 0.45f),
                    Main.rand.NextBool(3) ? DustID.SnowflakeIce : DustID.Frost,
                    new Vector2(0f, Main.rand.NextFloat(-0.7f, -0.2f)), 130, default,
                    Main.rand.NextFloat(0.65f, 1f));
                d.noGravity = true;
            }
        }
    }

    // Стаки холода на враге и распространение огня с трупа
    public class FrostKindleNPC : GlobalNPC
    {
        public int Chill;       // 0..MaxChill
        public int ChillTime;   // сколько тиков ещё живёт набранное
        public bool WasBurning;

        public override bool InstancePerEntity => true;

        public override void PostAI(NPC npc)
        {
            WasBurning = npc.HasBuff(BuffID.Frostburn2);
            if (ChillTime > 0 && --ChillTime == 0)
                Chill = 0;
            // Холод видно на враге, пока он не растаял
            if (Chill > 0 && Main.GameUpdateCount % 10 == 0)
                FrostKindle.ChillAura(npc, Chill);
        }

        public override void OnKill(NPC npc)
        {
            if (WasBurning)
                FrostKindle.Spread(npc);
        }
    }

    // Заряды длины взмаха Frostbrand: по одному за каждого горящего врага
    public class FrostKindlePlayer : ModPlayer
    {
        private readonly int[] reachNpc = new int[FrostKindle.MaxReachCharges];
        private readonly long[] reachUntil = new long[FrostKindle.MaxReachCharges];

        // Прибавка к радиусу взмаха в пикселях
        public float ReachBonus
        {
            get
            {
                long now = (long)Main.GameUpdateCount;
                int n = 0;
                for (int i = 0; i < reachUntil.Length; i++)
                    if (reachUntil[i] > now)
                        n++;
                return n * FrostKindle.ReachPerCharge;
            }
        }

        public int ReachCharges
        {
            get
            {
                long now = (long)Main.GameUpdateCount;
                int n = 0;
                for (int i = 0; i < reachUntil.Length; i++)
                    if (reachUntil[i] > now)
                        n++;
                return n;
            }
        }

        // Один и тот же враг продлевает свой заряд, но второй не даёт
        public void AddReachCharge(int npcIndex)
        {
            long now = (long)Main.GameUpdateCount;
            long until = now + FrostKindle.ReachLife;

            for (int i = 0; i < reachNpc.Length; i++)
            {
                if (reachNpc[i] == npcIndex && reachUntil[i] > now)
                {
                    reachUntil[i] = until;
                    return;
                }
            }

            int slot = -1;
            for (int i = 0; i < reachUntil.Length; i++)
            {
                if (reachUntil[i] <= now)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0)
            {
                slot = 0;
                for (int i = 1; i < reachUntil.Length; i++)
                    if (reachUntil[i] < reachUntil[slot])
                        slot = i;
            }
            reachNpc[slot] = npcIndex;
            reachUntil[slot] = until;
        }

        public override void OnRespawn(Player player)
        {
            Array.Clear(reachNpc, 0, reachNpc.Length);
            Array.Clear(reachUntil, 0, reachUntil.Length);
        }
    }
}
