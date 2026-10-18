using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using FrostMelee.Content.Projectiles;
using FrostMelee.Content.Weapons;

namespace FrostMelee.Content
{
    // Ледяное горение мечей (Ice Blade, Frostbrand).
    //
    // Прямой удар его НЕ вешает — иначе любой моб горит с первого тычка.
    // Меч только студит: каждый удар в морозном месте даёт стак «холода».
    // Морозно — это иней от взмаха под врагом, вода, дождь, снежный биом
    // или воткнутый в землю ледяной меч.
    // Три стака — враг вспыхивает: Ледяной клинок обычным Frostburn,
    // Фростборн — хардмодным Frostbite (ванильный Frostburn2).
    // Вне мороза удар даёт ровно один стак, и тот гаснет за две секунды:
    // сколько ни бей, поджечь не выйдет.
    //
    // Свойства приёмов (у каждого своя роль):
    // * взмах  — широкий холод по всем, кого задел;
    // * тычок  — два холода за укол: одиночную цель поджигает вдвое быстрее;
    // * встреча — враг сам насадился на выставленный клинок: уже остывший вспыхивает сразу;
    // * бросок — воткнутый меч морозит вокруг себя, а застрявший во враге держит на нём холод.
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

        // Frostbrand: за каждого горящего, которого ударил, на клинке нарастает лёд.
        // Спрайт не растягивается: иней ползёт по лезвию от острия к рукояти,
        // а за остриём вырастает ледяное жало — оно и даёт прибавку длины.
        public const float IceTipPerCharge = 3f;  // px льда за остриём на заряд
        public const int MaxReachCharges = 5;
        public const int ReachLife = 480;          // 8 с живёт заряд
        public const float BurnSpreadRadius = 60f; // умерший горящий поджигает соседей
        public const int StabStacks = 2;           // тычок кладёт два холода за укол
        public const float LodgedColdRadius = 60f; // воткнутый меч морозит вокруг себя

        // Каким приёмом нанесён удар
        public const int FromSwing = 0;   // взмах (дуга)
        public const int FromStab = 1;    // тычок
        public const int FromBrace = 2;   // встреча (выставленный клинок)
        public const int FromThrow = 3;   // бросок

        // Два тира горения: ранний Ледяной клинок жжёт обычным Frostburn,
        // хардмодный Фростборн — Frostbite (ванильный Frostburn2, тикает больнее).
        public static int BurnBuffFor(int itemType) =>
            itemType == ItemID.Frostbrand ? BuffID.Frostburn2 : BuffID.Frostburn;

        // Насколько меч оброс льдом: 0 — чистый, 1 — все заряды
        public static float Growth01(Player p) =>
            p == null || !p.active
                ? 0f
                : p.GetModPlayer<FrostKindlePlayer>().ReachCharges / (float)MaxReachCharges;

        // «Горит» — любой из двух тиров
        public static bool IsBurning(NPC npc) =>
            npc != null && (npc.HasBuff(BuffID.Frostburn) || npc.HasBuff(BuffID.Frostburn2));

        // Продлить текущее горение (какого бы тира оно ни было)
        public static void ExtendBurn(NPC target, int ticks)
        {
            int idx = target.FindBuffIndex(BuffID.Frostburn2);
            if (idx < 0)
                idx = target.FindBuffIndex(BuffID.Frostburn);
            if (idx >= 0)
                target.buffTime[idx] = Math.Min(target.buffTime[idx] + ticks, BurnTicks + ticks);
        }

        // Удар ледяным мечом по врагу. itemType — какой именно меч ударил,
        // source — каким приёмом (From*).
        public static void OnSwordHit(Player owner, NPC target, int itemType, int source = FromSwing)
        {
            if (owner == null || target == null || !target.active || target.friendly || target.immortal)
                return;

            FrostKindleNPC g = target.GetGlobalNPC<FrostKindleNPC>();
            bool cold = IsCold(owner, target);
            int before = g.Chill;

            if (cold)
            {
                int add = source == FromStab ? StabStacks : 1;
                if (owner.ZoneSnow)
                    add *= SnowStacks;
                g.Chill = Math.Min(MaxChill, g.Chill + add);
                g.ChillTime = ChillLife;
                if (g.Chill >= MaxChill)
                    Ignite(target, g, BurnBuffFor(itemType));
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

            // Встреча: враг сам насадился на выставленный клинок — холод вспыхивает сразу
            if (source == FromBrace && cold && before > 0 && g.Chill > 0)
                Ignite(target, g, BurnBuffFor(itemType));

            if (IsBurning(target))
            {
                if (itemType == ItemID.IceBlade)
                {
                    // Клинок продлевает горение — любого тира
                    ExtendBurn(target, IceBladeExtendTicks);
                }
                else if (itemType == ItemID.Frostbrand)
                {
                    // Фростборн растёт от горящих: по одному заряду на врага,
                    // каким бы приёмом его ни ударили — длина проявится на взмахе
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
            // Воткнутый рядом ледяной меч морозит сам по себе
            if (LodgedSwordNear(npc.Center, npc.width * 0.5f + LodgedColdRadius))
                return true;
            // Иней от взмаха: свои же снежные полосы под ногами
            return FrostTrailSystem.AnyNear(npc.Center, npc.width * 0.5f + ColdRadius);
        }

        // --- Воткнутые ледяные мечи: источник холода и «якорь» для стаков ---

        private static bool IsLodgedSword(Projectile p) =>
            p != null && p.active && p.type == ModContent.ProjectileType<SickleThrowProjectile>() &&
            SwordTypes.IsSwordId((int)p.ai[2]) && p.velocity == Vector2.Zero;

        public static bool LodgedSwordNear(Vector2 point, float radius)
        {
            float r2 = radius * radius;
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (!IsLodgedSword(p))
                    continue;
                if (Vector2.DistanceSquared(p.Center, point) <= r2)
                    return true;
            }
            return false;
        }

        public static bool SwordStuckIn(NPC npc)
        {
            Rectangle box = npc.Hitbox;
            box.Inflate(12, 12);
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (!IsLodgedSword(p) || (int)p.ai[0] != npc.whoAmI)
                    continue;
                if (box.Contains(p.Center.ToPoint()))
                    return true;
            }
            return false;
        }

        // Поджиг: синий ожог + вспышка искр. buffType — тир меча;
        // слабый огонь не перебивает сильный: уже горящий Frostbite только продлеваем.
        private static void Ignite(NPC npc, FrostKindleNPC g, int buffType)
        {
            if (buffType == BuffID.Frostburn && npc.HasBuff(BuffID.Frostburn2))
                buffType = BuffID.Frostburn2;
            npc.AddBuff(buffType, BurnTicks);
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

        // Умерший горящий поджигает соседей тем же тиром, каким горел сам
        public static void Spread(NPC dead, int burnType)
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
                other.AddBuff(burnType, SpreadBurnTicks);
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
        public int BurnTier;    // 0 — не горит, 1 — Frostburn, 2 — Frostbite

        public override bool InstancePerEntity => true;

        public override void PostAI(NPC npc)
        {
            BurnTier = npc.HasBuff(BuffID.Frostburn2) ? 2 : npc.HasBuff(BuffID.Frostburn) ? 1 : 0;
            if (ChillTime > 0)
            {
                // Меч, застрявший в этом враге, не даёт холоду растаять
                if (ChillTime == 1 && FrostKindle.SwordStuckIn(npc))
                    ChillTime = FrostKindle.ChillLife;
                else if (--ChillTime == 0)
                    Chill = 0;
            }
            // Холод видно на враге, пока он не растаял
            if (Chill > 0 && Main.GameUpdateCount % 10 == 0)
                FrostKindle.ChillAura(npc, Chill);
        }

        public override void OnKill(NPC npc)
        {
            if (BurnTier > 0)
                FrostKindle.Spread(npc, BurnTier == 2 ? BuffID.Frostburn2 : BuffID.Frostburn);
        }
    }

    // Заряды роста меча Frostbrand: по одному за каждого горящего врага
    public class FrostKindlePlayer : ModPlayer
    {
        private readonly int[] reachNpc = new int[FrostKindle.MaxReachCharges];
        private readonly long[] reachUntil = new long[FrostKindle.MaxReachCharges];

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

        public override void OnRespawn()
        {
            Array.Clear(reachNpc, 0, reachNpc.Length);
            Array.Clear(reachUntil, 0, reachUntil.Length);
        }
    }
}
