using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Холодное пламя Фростбранда (FROSTBRAND_ИТОГ.md).
    //
    // Поджигает только струя (клинок — никогда), и запас огня КОНЕЧЕН:
    // у меча SwordPlayer.MaxFlames языков, каждый горящий враг держит один.
    // Умер горящим — язык возвращается к игроку живым огоньком
    // (FrostFlameWisp) и бьёт врагов по пути. Учёт запаса — TryIgnite.
    //
    // Смерть горящего врага от тычка или броска — взрыв холодного огня,
    // поджигающий и задевающий соседей (радиус и силу задаёт приём).
    //
    // Чем больше врагов горит рядом с владельцем, тем ярче светится меч
    // (SwordPlayer держит свечение по CountBurningNear).
    public class FrostFireSystem : ModSystem
    {
        public const int BurnTicks = 180;         // 3 с горения

        private class BurstVisual
        {
            public Vector2 Center;
            public float Radius;
            public bool Big;
            public int Age;
            public int Life;
        }

        private static readonly List<BurstVisual> bursts = new();

        public override void OnWorldUnload()
        {
            bursts.Clear();
        }

        public override void Unload()
        {
            bursts.Clear();
        }

        // Горит ли цель
        public static bool IsBurning(NPC npc) => npc.FindBuffIndex(BuffID.Frostburn) >= 0;

        // Горел ли враг к моменту смерти (для взрывов тычка и броска)
        public static bool WasBurning(NPC npc) => IsBurning(npc);

        // Сколько тиков горения осталось (0 — не горит)
        public static int BurnRemaining(NPC npc)
        {
            int index = npc.FindBuffIndex(BuffID.Frostburn);
            return index < 0 ? 0 : npc.buffTime[index];
        }

        // Тычок забирает горение с цели: снимает бафф и возвращает остаток тиков
        public static int TakeBurn(NPC npc)
        {
            int index = npc.FindBuffIndex(BuffID.Frostburn);
            if (index < 0)
                return 0;
            int ticks = npc.buffTime[index];
            npc.DelBuff(index);
            return ticks;
        }

        // Сырой поджог без учёта запаса (внутренний и запасной путь)
        public static void Ignite(NPC npc)
        {
            if (npc == null || !npc.active || npc.friendly || npc.immortal || npc.dontTakeDamage)
                return;

            npc.AddBuff(BuffID.Frostburn, BurnTicks);
        }

        // Поджог с учётом конечного запаса владельца: новый враг загорается,
        // только если есть свободный язык (в мече или висящий у игрока
        // огоньком); уже держащего свой язык обновляем бесплатно.
        // Возвращает false — запаса нет, враг НЕ загорелся
        public static bool TryIgnite(NPC npc, Player owner)
        {
            if (npc == null || !npc.active || npc.friendly || npc.immortal || npc.dontTakeDamage)
                return false;
            if (owner == null || !owner.active)
            {
                Ignite(npc);
                return true;
            }
            SwordPlayer sp = owner.GetModPlayer<SwordPlayer>();
            if (!sp.FlameAuthority)
            {
                // Сервер и чужие клиенты учёт не ведут (как полировка в MP):
                // поджог не блокируем, запас считает клиент владельца
                npc.AddBuff(BuffID.Frostburn, BurnTicks);
                return true;
            }
            if (sp.HoldsFlame(npc))
            {
                npc.AddBuff(BuffID.Frostburn, BurnTicks);
                return true;
            }
            if (sp.FreeFlames > 0 || sp.TryConsumeOrbitingFlame())
            {
                sp.AddFlameHolder(npc);
                npc.AddBuff(BuffID.Frostburn, BurnTicks);
                return true;
            }
            return false;
        }

        // Вспышка холодного огня: звук, кольцо пламени, свечение.
        // big = взрыв броска: больше пыли и ярче вспышка
        public static void Burst(Vector2 center, float radius, bool big)
        {
            SoundEngine.PlaySound(SoundID.DD2_ExplosiveTrapExplode with { Volume = big ? 0.9f : 0.5f, Pitch = 0.2f }, center);

            if (Main.dedServ)
                return;

            int dust = big ? 26 : 14;
            for (int i = 0; i < dust; i++)
            {
                Vector2 v = Main.rand.NextFloat(MathHelper.TwoPi).ToRotationVector2()
                    * Main.rand.NextFloat(2f, big ? 7f : 5f);
                Dust d = Dust.NewDustPerfect(center, DustID.IceTorch, v, 100, default,
                    Main.rand.NextFloat(1f, big ? 2.2f : 1.6f));
                d.noGravity = true;
            }

            Lighting.AddLight(center, big ? 0.9f : 0.6f, big ? 1.1f : 0.8f, 1.3f);
            bursts.Add(new BurstVisual
            {
                Center = center,
                Radius = radius,
                Big = big,
                // Чуть дольше прежних 10/14 тиков: вспышку успеваешь прочитать
                Life = big ? 20 : 15
            });
            // Цельные языки веером из центра — пламя взрыва, а не только
            // светящийся круг и пыль; пыль выше остаётся искрами. Языки
            // долетают почти до края зоны урона (radius)
            Projectiles.FrostJetFx.BurstTongues(center, big, radius);
        }

        public override void PostUpdateEverything()
        {
            if (Main.dedServ || bursts.Count == 0)
                return;

            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                BurstVisual b = bursts[i];
                b.Age++;
                if (b.Age >= b.Life)
                    bursts.RemoveAt(i);
            }
        }

        public static bool HasBursts => bursts.Count > 0;

        // Вспышка рисуется в буфер огня FrostJetFx (аддитивный батч открыт им,
        // координаты — от origin буфера). Свечение раскрывается от центра до
        // края зоны урона: видимое пятно = кого задело (раньше мягкий край
        // текстуры гас на половине радиуса, и зона урона была шире видимой)
        public static void DrawBursts(Texture2D glow, Vector2 origin)
        {
            if (bursts.Count == 0)
                return;
            Vector2 center = glow.Size() / 2f;
            foreach (BurstVisual b in bursts)
            {
                float t = b.Age / (float)b.Life;
                float open = 1f - (1f - t) * (1f - t) * (1f - t);
                float fade = (1f - t) * (1f - t);
                // Мягкий край ThePerfectGlow заметен примерно до 0.6 его
                // половины — поэтому масштаб с запасом, чтобы видимый край
                // доходил до радиуса урона
                float scale = b.Radius * 2f * 1.55f * (0.45f + 0.55f * open) / glow.Width;
                float a = b.Big ? 0.75f : 0.55f;
                Main.EntitySpriteDraw(glow, b.Center - origin, null, new Color(40, 120, 255) * (fade * a * 0.55f),
                    0f, center, scale, SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(glow, b.Center - origin, null, new Color(150, 220, 255) * (fade * a),
                    0f, center, scale * 0.4f, SpriteEffects.None, 0f);
            }
        }

        // Взрыв погибшего горящего врага: задевает и поджигает соседей.
        // damage <= 0 — только вспышка без урона.
        public static void Explode(NPC dead, float radius, int damage, int owner, bool big)
        {
            Vector2 c = dead.Center;
            Burst(c, radius, big);

            if (damage <= 0)
                return;

            // Урон взрыва — blast-снарядом через ванильные коллизии:
            // без прямых вызовов StrikeNPC, урон и мультихит-защита ванильные
            Projectile.NewProjectile(Main.player[owner].GetSource_FromThis(), c, Vector2.Zero,
                ModContent.ProjectileType<Projectiles.FrostFireBlast>(), damage, 0f, owner, radius);
        }

        // Сколько врагов горит рядом: для свечения меча в руке
        public static int CountBurningNear(Vector2 center, float radius)
        {
            int cnt = 0;
            float r2 = radius * radius;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC n = Main.npc[i];
                if (!n.active || n.friendly)
                    continue;
                if (Vector2.DistanceSquared(n.Center, center) > r2)
                    continue;
                if (IsBurning(n))
                    cnt++;
            }
            return cnt;
        }
    }
}
