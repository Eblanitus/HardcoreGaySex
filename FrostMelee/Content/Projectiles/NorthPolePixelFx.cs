using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    // Pixel particles of North Pole, drawn by the owner's sprite reference (02.11.2026):
    // opaque 2x2 sprites that do not fade with alpha but shrink step by step
    // (small flake -> star mote -> plus -> one pixel), like Terraria's own pixel dust.
    // Visual only, local to every client.
    public class NorthPolePixelFx : ModSystem
    {
        public const byte Swing = 0, Throw = 1, Flake = 2, Spark = 3, Snow = 4;
        private static readonly string[][] Stages =
        {
            new[] { "Mote2", "Mote1", "Mote0" },
            new[] { "TMote2", "TMote1", "TMote0" },
            new[] { "SwingFlake4", "SwingFlake3", "Mote2", "Mote1", "Mote0" },
            new[] { "Sparkle1", "Sparkle0", "Mote1", "Mote0" },
            new[] { "Snow2", "Snow1", "Snow0" },   // calm white snow: the default for North Pole bursts
        };

        private struct Particle
        {
            public Vector2 Pos, Vel;
            public float Gravity, Drag;
            public int Age, Life;
            public byte Set;
            public Color Tint;
        }

        private const int MaxParticles = 700;
        private static readonly List<Particle> particles = new();

        public static void Spawn(Vector2 pos, Vector2 vel, byte set, int life, float gravity = 0.04f, float drag = 0.96f, Color? tint = null)
        {
            if (Main.dedServ || particles.Count >= MaxParticles) return;
            particles.Add(new Particle
            {
                Pos = pos, Vel = vel, Gravity = gravity, Drag = drag,
                Life = Math.Max(2, life), Set = (byte)Math.Min(set, Stages.Length - 1), Tint = tint ?? Color.White
            });
        }

        // Ring burst of pixel snow (impacts, bursts, growth steps)
        public static void Burst(Vector2 at, int count, float speed, byte set, int life)
        {
            for (int i = 0; i < count; i++)
            {
                Vector2 v = (MathHelper.TwoPi * i / count + Main.rand.NextFloat(-0.2f, 0.2f)).ToRotationVector2()
                    * speed * Main.rand.NextFloat(0.6f, 1f);
                Spawn(at, v, set, life + Main.rand.Next(-life / 4, life / 4 + 1));
            }
        }

        public override void Load()
        {
            if (!Main.dedServ) On_Main.DrawDust += DrawOverDust;
        }

        public override void Unload()
        {
            if (!Main.dedServ) On_Main.DrawDust -= DrawOverDust;
            particles.Clear();
        }

        public override void OnWorldUnload() => particles.Clear();

        public override void PostUpdateDusts()
        {
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                Particle p = particles[i];
                if (++p.Age >= p.Life) { particles.RemoveAt(i); continue; }
                p.Vel = p.Vel * p.Drag + new Vector2(0f, p.Gravity);
                p.Pos += p.Vel;
                particles[i] = p;
            }
        }

        private static void DrawOverDust(On_Main.orig_DrawDust orig, Main self)
        {
            orig(self);
            if (Main.gameMenu || (particles.Count == 0 && !NorthPoleDrift.HasPoints)) return;
            SpriteBatch sb = Main.spriteBatch;
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            try
            {
                foreach (Particle p in particles)
                {
                    string[] stages = Stages[p.Set];
                    int stage = Math.Min(stages.Length - 1, p.Age * stages.Length / p.Life);
                    NorthPoleArt.Sprite(stages[stage], p.Pos, p.Tint);
                }
                NorthPoleDrift.Draw();
            }
            finally
            {
                sb.End();
            }
        }
    }

    // Lingering snow trail of the thrown North Pole's snowflakes (owner 02.11.2026):
    // each trail flake leaves cyan pixel snow that hangs for 3 seconds and gives
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

        // Called from the pixel batch: each point is a cyan mote that twinkles, then shrinks away
        public static void Draw()
        {
            foreach (Point p in points)
            {
                float left = 1f - p.Age / (float)Life;
                bool twinkle = (Main.GameUpdateCount + p.Seed) % 40 < 4;
                string name = twinkle ? "TMote2" : left > 0.5f ? "TMote1" : "TMote0";
                Vector2 drift = new Vector2(MathF.Sin((p.Age + p.Seed) * 0.05f) * 2f, p.Age * 0.06f);
                NorthPoleArt.Sprite(name, p.Pos + drift, Color.White);
            }
        }
    }
}
