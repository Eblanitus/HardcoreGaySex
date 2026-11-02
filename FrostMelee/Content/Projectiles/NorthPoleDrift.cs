using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    // Lingering snow trail of the thrown North Pole's snowflakes (owner 02.11.2026):
    // each trail flake leaves snow that hangs for 3 seconds and gives
    // Frostbite to enemies passing through it. Only the owner applies the debuff.
    public class NorthPoleDrift : ModSystem
    {
        public const int Life = 180;          // 3 s
        public const int DebuffTicks = 120;
        public const float Reach = 12f;       // px around each point
        private const int MaxPoints = 400;

        private struct Point
        {
            public Vector2 Pos;
            public int Age, Owner, Seed;
        }

        private static readonly List<Point> points = new();
        public static bool HasPoints => points.Count > 0;

        public static void Add(Vector2 pos, int owner)
        {
            if (points.Count >= MaxPoints) points.RemoveAt(0);
            points.Add(new Point { Pos = pos, Owner = owner, Seed = Main.rand.Next(1000) });
        }

        public override void OnWorldUnload() => points.Clear();

        public override void PostUpdateNPCs()
        {
            bool check = Main.GameUpdateCount % 6 == 0;
            for (int i = points.Count - 1; i >= 0; i--)
            {
                Point p = points[i];
                if (++p.Age >= Life) { points.RemoveAt(i); continue; }
                points[i] = p;
                if (!check || p.Owner != Main.myPlayer || Main.netMode == NetmodeID.Server) continue;
                Rectangle zone = new Rectangle((int)(p.Pos.X - Reach), (int)(p.Pos.Y - Reach), (int)(Reach * 2), (int)(Reach * 2));
                for (int n = 0; n < Main.maxNPCs; n++)
                {
                    NPC npc = Main.npc[n];
                    if (npc.active && !npc.friendly && npc.lifeMax > 5 && !npc.dontTakeDamage && npc.Hitbox.Intersects(zone))
                        npc.AddBuff(BuffID.Frostburn2, DebuffTicks); // syncs from the client by itself
                }
            }
        }

        // Visual (owner 02.11.2026: the smooth pixel motes read as "Noita"): every point now and
        // then lets go of a plain vanilla snow flake that hangs and drifts down, like vanilla dust.
        public override void PostUpdateDusts()
        {
            if (Main.dedServ) return;
            foreach (Point p in points)
            {
                if ((Main.GameUpdateCount + p.Seed) % 24 != 0) continue;
                Dust d = Dust.NewDustPerfect(p.Pos + Main.rand.NextVector2Circular(6f, 6f), DustID.SnowflakeIce,
                    new Vector2(Main.rand.NextFloat(-0.3f, 0.3f), Main.rand.NextFloat(0.2f, 0.6f)), 0, default, Main.rand.NextFloat(0.9f, 1.2f));
                d.noGravity = true;
            }
        }
    }
}
