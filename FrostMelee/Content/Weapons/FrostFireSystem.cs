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
    // Общее правило — Горение: любой удар любым приёмом поджигает холодным
    // огнём. Стаки 1-2 — обычный Frostburn; третий стак подряд — полное
    // возгорание (Frostbite, «горит несколько секунд»). Сам урон-по-времени
    // дают ванильные дебаффы: баланс и сетевая синхронизация — их.
    // Стаки сбрасываются, если между ударами прошло больше StackWindow.
    //
    // Смерть горящего врага от тычка или броска — взрыв холодного огня,
    // поджигающий и задевающий соседей (радиус и силу задаёт приём).
    //
    // Чем больше врагов горит рядом с владельцем, тем ярче светится меч
    // (SwordPlayer держит свечение по CountBurningNear).
    public class FrostFireSystem : ModSystem
    {
        private const int StackWindow = 300;      // 5 с между ударами, иначе стаки с нуля
        public const int BurnTicks = 180;         // 3 с обычного горения
        public const int FullBurnTicks = 300;     // 5 с полного возгорания
        public const int FullIgnitionStacks = 3;  // три удара — полностью загорается

        private class State
        {
            public int stacks;
            public int lastHit;
        }

        private static readonly Dictionary<int, State> states = new();

        public override void OnWorldUnload() => states.Clear();

        public override void Unload() => states.Clear();

        public static bool IsBurning(NPC npc) =>
            npc.FindBuffIndex(BuffID.Frostburn) >= 0;

        // Горел ли враг к моменту смерти (для взрывов тычка и броска)
        public static bool WasBurning(NPC npc) =>
            IsBurning(npc) || (states.TryGetValue(npc.whoAmI, out State st) && st.stacks > 0);

        // Удар любым приёмом Фростбранда: стак горения
        public static void Ignite(NPC npc, int owner)
        {
            if (npc == null || !npc.active || npc.friendly || npc.immortal || npc.dontTakeDamage)
                return;

            int now = (int)Main.GameUpdateCount;
            if (!states.TryGetValue(npc.whoAmI, out State st) || now - st.lastHit > StackWindow)
            {
                st = new State();
                states[npc.whoAmI] = st;
            }
            st.lastHit = now;
            st.stacks++;

            if (st.stacks >= FullIgnitionStacks)
            {
                // Полное возгорание: длинный Frostburn + Slow (тело сковано
                // холодом) + вспышка. Отдельного «сильного» дебаффа в этой
                // сборке tML нет, связка даёт то же ощущение
                npc.AddBuff(BuffID.Frostburn, FullBurnTicks);
                npc.AddBuff(BuffID.Slow, FullBurnTicks);
                st.stacks = 0;
                Burst(npc.Center, 60f, false);
            }
            else
            {
                npc.AddBuff(BuffID.Frostburn, BurnTicks);
            }
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

            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow == null || glow.IsDisposed)
                return;
            Main.EntitySpriteDraw(glow, center - Main.screenPosition, null,
                new Color(120, 200, 255) * (big ? 0.9f : 0.6f), 0f, glow.Size() / 2f,
                (big ? 90f : 55f) / glow.Width, SpriteEffects.None, 0f);
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
                ModContent.ProjectileType<FrostFireBlast>(), damage, 0f, owner, radius);
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

        // Чистка состояний мёртвых NPC
        public override void PostUpdateNPCs()
        {
            if (states.Count == 0)
                return;

            List<int> dead = null;
            foreach (KeyValuePair<int, State> kv in states)
            {
                NPC n = Main.npc[kv.Key];
                if (!n.active || (!IsBurning(n) && (int)Main.GameUpdateCount - kv.Value.lastHit > StackWindow))
                    (dead ??= new List<int>()).Add(kv.Key);
            }

            if (dead == null)
                return;

            foreach (int k in dead)
                states.Remove(k);
        }
    }
}
