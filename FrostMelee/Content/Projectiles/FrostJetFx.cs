using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    public class FrostJetFx : ModSystem
    {
        // 280, а не 160: долго торчащий меч на второй стадии разгорания
        // выпускает вдвое больше частиц и не должен выжирать весь запас
        private const int MaxParticles = 280;
        private const int BladeGraceTicks = 2;

        private enum FlameKind
        {
            Blade,
            Near,
            Halo,
            Trail,
            Spark
        }

        private sealed class Particle
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public Vector2 Drift;
            public int Age;
            public int Life;
            public float Size;
            public float Rotation;
            public float RotationSpeed;
            public FlameKind Kind;
            public bool Anchored;
            public float Along;
            public float Side;
            public float Wobble;
            public float Temperature;
            public float Coupling;
            public float Stretch;
            public float Flow;
        }

        private static readonly List<Particle> particles = new();

        private static Vector2 bladeButt;
        private static Vector2 bladeTip;
        private static Vector2 bladeDir = Vector2.UnitX;
        private static Vector2 bladeVelocity;
        private static uint bladeFrame;
        private static bool bladeLineValid;
        // Крупность частиц на время текущего вызова EmitBlade*: читается в
        // EmitLineParticle и искрах. Всё это клиентская косметика в одном
        // потоке внутри тика — статиком безопасно
        private static float emitSizeScale = 1f;

        // Огонь на клинке В РУКЕ (взмах/тычок/встреча): частицы привязаны к линии
        // клинка и тянутся за ней. Линия одна на всех (bladeButt/bladeTip),
        // поэтому так горит только один меч за раз — тот, что в руке
        public static void EmitBlade(Vector2 butt, Vector2 tip, Vector2 dir, float intensity = 1f, float sizeScale = 1f)
            => EmitBladeInternal(butt, tip, dir, intensity, anchored: true, sizeScale);

        // Огонь на БРОШЕННОМ клинке (летит или застрял): частицы рождаются на
        // лезвии и гаснут на месте, ни к чему не привязаны и общую линию не
        // трогают. Привязывать нельзя: линия одна, и меч в руке с застрявшим
        // мечом начнут дёргать частицы друг к другу — огонь летает между ними
        // «как электричество» (баг, замеченный владельцем на Дирклопсе)
        public static void EmitBladeLoose(Vector2 butt, Vector2 tip, Vector2 dir, float intensity = 1f, float sizeScale = 1f)
            => EmitBladeInternal(butt, tip, dir, intensity, anchored: false, sizeScale);

        private static void EmitBladeInternal(Vector2 butt, Vector2 tip, Vector2 dir, float intensity, bool anchored, float sizeScale)
        {
            if (Main.dedServ)
                return;

            dir = dir.SafeNormalize(Vector2.UnitX);
            Vector2 perp = new Vector2(-dir.Y, dir.X);
            intensity = MathHelper.Clamp(intensity, 0.15f, 1f);
            emitSizeScale = MathHelper.Clamp(sizeScale, 0.5f, 2.5f);

            if (anchored)
            {
                bladeVelocity = bladeLineValid
                    ? ((butt - bladeButt) + (tip - bladeTip)) * 0.5f
                    : Vector2.Zero;
                bladeButt = butt;
                bladeTip = tip;
                bladeDir = dir;
                bladeFrame = Main.GameUpdateCount;
                bladeLineValid = true;
            }

            int bladeCount = System.Math.Max(1, (int)System.Math.Round(4f * intensity));
            for (int i = 0; i < bladeCount; i++)
            {
                float along = MathHelper.Clamp((i + 0.5f) / bladeCount + Main.rand.NextFloat(-0.07f, 0.07f), 0.02f, 0.96f);
                EmitLineParticle(butt, tip, dir, perp, FlameKind.Blade, along, -0.02f, 0.06f, 0.06f, 6, 10, 0.26f, 0.36f, anchored);
            }

            int nearCount = intensity >= 0.55f ? 2 : 1;
            for (int i = 0; i < nearCount; i++)
            {
                float along = nearCount == 2 ? (i == 0 ? 0.28f : 0.7f) : 0.5f;
                EmitLineParticle(butt, tip, dir, perp, FlameKind.Near, along, 0f, 0.14f, 0.12f, 7, 11, 0.28f, 0.38f, anchored);
            }

            int haloCount = intensity >= 0.55f ? 2 : 1;
            for (int i = 0; i < haloCount; i++)
            {
                float along = haloCount == 2 ? (i == 0 ? 0.18f : 0.82f) : 0.5f;
                EmitLineParticle(butt, tip, dir, perp, FlameKind.Halo, along, 0.05f, 0.3f, 0.22f, 9, 14, 0.36f, 0.48f, anchored);
            }

            if (intensity >= 0.35f)
                EmitLineParticle(butt, tip, dir, perp, FlameKind.Trail, Main.rand.NextFloat(0.15f, 0.85f),
                    0.2f, 0.6f, 0.3f, 12, 17, 0.24f, 0.34f, false);

            if (Main.rand.NextFloat() < 0.1f * intensity)
            {
                float along = Main.rand.NextFloat(0.1f, 0.8f);
                Vector2 position = Vector2.Lerp(butt, tip, along) + perp * Main.rand.NextFloat(-2.5f, 2.5f);
                // Меч в руке (взмах): искры быстрые, им положено. Свободный огонь
                // (застрявший меч): медленные тлеющие угольки — быстрые искры на
                // неподвижном клинке выглядят электрическими разрядами
                Vector2 velocity = anchored
                    ? dir * Main.rand.NextFloat(1f, 2.2f) + perp * Main.rand.NextFloat(-0.4f, 0.4f)
                    : dir * Main.rand.NextFloat(0.1f, 0.35f) + perp * Main.rand.NextFloat(-0.15f, 0.15f);
                velocity += new Vector2(0f, -Main.rand.NextFloat(0.1f, anchored ? 0.6f : 0.35f));
                Add(position, velocity, FlameKind.Spark, Main.rand.Next(15, 23), Main.rand.NextFloat(0.08f, 0.14f) * emitSizeScale);
            }
        }

        public static void EmitJet(Vector2 center, Vector2 dir)
        {
            if (Main.dedServ)
                return;

            dir = dir.SafeNormalize(Vector2.UnitX);
            Vector2 perp = new Vector2(-dir.Y, dir.X);

            EmitJetParticle(center, dir, perp, FlameKind.Blade, -0.02f, 0.06f, 0.09f, 6, 9, 0.26f, 0.36f);
            EmitJetParticle(center, dir, perp, FlameKind.Blade, -0.01f, 0.05f, 0.09f, 6, 9, 0.26f, 0.36f);
            EmitJetParticle(center, dir, perp, FlameKind.Near, 0f, 0.15f, 0.12f, 7, 10, 0.28f, 0.38f);
            EmitJetParticle(center, dir, perp, FlameKind.Near, 0.04f, 0.2f, 0.12f, 7, 10, 0.28f, 0.38f);
            EmitJetParticle(center, dir, perp, FlameKind.Halo, 0.05f, 0.35f, 0.22f, 9, 13, 0.36f, 0.48f);

            if (Main.rand.NextFloat() < 0.5f)
                EmitJetParticle(center, dir, perp, FlameKind.Trail, 0.25f, 0.7f, 0.3f, 12, 17, 0.24f, 0.34f);

            if (Main.rand.NextFloat() < 0.08f)
            {
                Vector2 position = center + perp * Main.rand.NextFloat(-3.5f, 3.5f) + dir * Main.rand.NextFloat(0f, 4f);
                Vector2 velocity = dir * Main.rand.NextFloat(1.2f, 2.2f)
                    + perp * Main.rand.NextFloat(-0.45f, 0.45f)
                    + new Vector2(0f, -Main.rand.NextFloat(0.2f, 0.7f));
                Add(position, velocity, FlameKind.Spark, Main.rand.Next(15, 24), Main.rand.NextFloat(0.08f, 0.14f));
            }
        }

        private static void EmitLineParticle(Vector2 butt, Vector2 tip, Vector2 dir, Vector2 perp,
            FlameKind kind, float along, float speedMin, float speedMax, float sideSpeed,
            int lifeMin, int lifeMax, float sizeMin, float sizeMax, bool anchored)
        {
            float side = Main.rand.NextFloat(-3.5f, 3.5f);
            Vector2 position = Vector2.Lerp(butt, tip, along) + perp * side;
            Vector2 velocity = dir * Main.rand.NextFloat(speedMin, speedMax)
                + perp * Main.rand.NextFloat(-sideSpeed, sideSpeed);
            Particle p = Add(position, velocity, kind, Main.rand.Next(lifeMin, lifeMax), Main.rand.NextFloat(sizeMin, sizeMax) * emitSizeScale);
            if (anchored)
            {
                p.Anchored = true;
                p.Along = along;
                p.Side = side;
                p.Wobble = Main.rand.NextFloat(MathHelper.TwoPi);
                p.Coupling = kind switch
                {
                    FlameKind.Blade => 0.9f,
                    FlameKind.Near => 0.65f,
                    _ => 0.4f
                };
                p.Stretch = kind switch
                {
                    FlameKind.Blade => 1.8f,
                    FlameKind.Near => 1.55f,
                    _ => 1.3f
                };
                p.Flow = kind switch
                {
                    FlameKind.Blade => 0.022f,
                    FlameKind.Near => 0.015f,
                    _ => 0.008f
                };
            }
        }

        private static void EmitJetParticle(Vector2 center, Vector2 dir, Vector2 perp,
            FlameKind kind, float speedMin, float speedMax, float sideSpeed,
            int lifeMin, int lifeMax, float sizeMin, float sizeMax)
        {
            Vector2 position = center + perp * Main.rand.NextFloat(-4f, 4f) + dir * Main.rand.NextFloat(-2f, 2.5f);
            Vector2 velocity = dir * Main.rand.NextFloat(speedMin, speedMax)
                + perp * Main.rand.NextFloat(-sideSpeed, sideSpeed);
            Add(position, velocity, kind, Main.rand.Next(lifeMin, lifeMax), Main.rand.NextFloat(sizeMin, sizeMax));
        }

        private static Particle Add(Vector2 position, Vector2 velocity, FlameKind kind, int life, float size)
        {
            if (particles.Count >= MaxParticles)
                RemoveForRoom();

            float angle = Main.rand.NextFloat(MathHelper.TwoPi);
            float drift = kind switch
            {
                FlameKind.Blade => Main.rand.NextFloat(0.005f, 0.02f),
                FlameKind.Near => Main.rand.NextFloat(0.008f, 0.03f),
                FlameKind.Halo => Main.rand.NextFloat(0.015f, 0.05f),
                FlameKind.Trail => Main.rand.NextFloat(0.025f, 0.07f),
                _ => Main.rand.NextFloat(0.03f, 0.08f)
            };
            Particle p = new Particle
            {
                Position = position,
                Velocity = velocity,
                Drift = angle.ToRotationVector2() * drift,
                Age = 0,
                Life = life,
                Size = size,
                Rotation = angle,
                RotationSpeed = Main.rand.NextFloat(-0.08f, 0.08f),
                Kind = kind,
                Temperature = kind switch
                {
                    FlameKind.Blade => 1f,
                    FlameKind.Near => 0.85f,
                    FlameKind.Halo => 0.65f,
                    FlameKind.Trail => 0.55f,
                    _ => 0.7f
                },
                Stretch = 1f
            };
            particles.Add(p);
            return p;
        }

        private static void RemoveForRoom()
        {
            int index = particles.FindIndex(p => p.Kind == FlameKind.Spark);
            if (index < 0)
                index = particles.FindIndex(p => p.Kind == FlameKind.Trail);
            if (index < 0)
                index = particles.FindIndex(p => p.Kind == FlameKind.Halo);
            if (index < 0)
                index = particles.FindIndex(p => p.Kind == FlameKind.Near);
            if (index < 0)
                index = 0;
            particles.RemoveAt(index);
        }

        public override void PostUpdateEverything()
        {
            if (Main.dedServ || particles.Count == 0)
                return;

            bool hasBlade = Main.GameUpdateCount - bladeFrame <= BladeGraceTicks;

            for (int i = particles.Count - 1; i >= 0; i--)
            {
                Particle p = particles[i];
                // Ускоренное старение ×3 — только уборка остатков пламени меча
                // В РУКЕ, когда его убрали. Свободные частицы (брошенный и
                // застрявший меч, струя) стареют всегда в обычном темпе: на
                // неподвижном застрявшем мече ×3 превращало огонь в быстрый
                // стробоскоп «как электричество» (замечание владельца)
                bool cleanup = p.Anchored && !hasBlade;
                p.Age += cleanup ? 3 : 1;
                if (p.Age >= p.Life)
                {
                    particles.RemoveAt(i);
                    continue;
                }

                p.Velocity += p.Drift * 0.12f;
                switch (p.Kind)
                {
                    case FlameKind.Blade:
                        p.Velocity *= 0.72f;
                        break;
                    case FlameKind.Near:
                        p.Velocity *= 0.8f;
                        break;
                    case FlameKind.Halo:
                        p.Velocity *= 0.88f;
                        break;
                    case FlameKind.Trail:
                        p.Velocity *= 0.96f;
                        p.Velocity.Y -= 0.006f;
                        break;
                    default:
                        p.Velocity *= 0.985f;
                        p.Velocity.Y -= 0.012f;
                        break;
                }

                p.Temperature = MathHelper.Max(0f, p.Temperature - (cleanup ? 2f : 0.65f) / p.Life);

                if (p.Anchored && hasBlade)
                {
                    p.Along = MathHelper.Clamp(p.Along - p.Flow, 0.02f, 0.96f);
                    Vector2 perp = new Vector2(-bladeDir.Y, bladeDir.X);
                    Vector2 anchor = Vector2.Lerp(bladeButt, bladeTip, p.Along) + perp * p.Side;
                    float follow = 1f - (float)System.Math.Exp(-p.Coupling * 0.35f);
                    float wobble = (float)System.Math.Sin((p.Age + p.Wobble) * 0.55f) * 1.1f;
                    p.Position = Vector2.Lerp(p.Position, anchor, follow)
                        + perp * wobble
                        + p.Velocity * 0.2f;
                    p.Velocity = Vector2.Lerp(p.Velocity, bladeVelocity, p.Coupling * 0.4f);
                    p.Rotation = bladeDir.ToRotation() + wobble * 0.04f;
                    float baseStretch = p.Kind switch
                    {
                        FlameKind.Blade => 1.6f,
                        FlameKind.Near => 1.45f,
                        _ => 1.2f
                    };
                    p.Stretch = MathHelper.Clamp(baseStretch + bladeVelocity.Length() * 0.07f, 1.2f, 2.8f);
                }
                else
                {
                    p.Position += p.Velocity;
                    if (p.Velocity.LengthSquared() > 0.04f)
                        p.Rotation = p.Velocity.ToRotation();
                    p.Stretch = MathHelper.Clamp(1.05f + p.Velocity.Length() * 0.08f, 1.05f, 2.4f);
                }
            }
        }

        public override void PostDrawTiles()
        {
            if (Main.dedServ || particles.Count == 0)
                return;

            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow == null || glow.IsDisposed)
                return;

            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp,
                DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

            Vector2 origin = glow.Size() / 2f;
            foreach (Particle p in particles)
            {
                float t = p.Age / (float)p.Life;
                float fade = p.Kind switch
                {
                    FlameKind.Blade => MathHelper.Clamp((1f - t) * 2.2f, 0f, 1f),
                    FlameKind.Near => MathHelper.Clamp((1f - t) * 1.6f, 0f, 1f),
                    _ => (1f - t) * (1f - t)
                };
                float scale = p.Size * (1f - 0.18f * t);
                float heat = MathHelper.Clamp(p.Temperature, 0f, 1f);
                Color outerBase = p.Kind switch
                {
                    FlameKind.Blade => new Color(25, 110, 255),
                    FlameKind.Near => new Color(20, 90, 245),
                    FlameKind.Halo => new Color(10, 55, 210),
                    FlameKind.Trail => new Color(35, 130, 255),
                    _ => new Color(80, 200, 255)
                };
                Color outer = Color.Lerp(new Color(5, 20, 90), outerBase, heat);
                Color core = Color.Lerp(new Color(35, 95, 210), new Color(190, 245, 255), heat);
                float outerAlpha = p.Kind switch
                {
                    FlameKind.Blade => 0.42f,
                    FlameKind.Near => 0.34f,
                    FlameKind.Halo => 0.24f,
                    FlameKind.Trail => 0.18f,
                    _ => 0.4f
                };
                float coreAlpha = p.Kind switch
                {
                    FlameKind.Blade => 0.72f,
                    FlameKind.Near => 0.5f,
                    FlameKind.Halo => 0.3f,
                    FlameKind.Trail => 0.24f,
                    _ => 0.5f
                };
                float stretch = MathHelper.Clamp(p.Stretch, 1f, 2.8f);
                Vector2 scaleVec = new Vector2(scale * stretch, scale * (0.9f - 0.1f * (stretch - 1f)));

                Main.EntitySpriteDraw(glow, p.Position - Main.screenPosition, null,
                    outer * (outerAlpha * fade), p.Rotation, origin, scaleVec, SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(glow, p.Position - Main.screenPosition, null,
                    core * (coreAlpha * fade), p.Rotation, origin,
                    scaleVec * 0.58f, SpriteEffects.None, 0f);
            }

            Main.spriteBatch.End();
        }

        public override void OnWorldUnload()
        {
            particles.Clear();
            bladeLineValid = false;
            bladeVelocity = Vector2.Zero;
        }

        public override void Unload()
        {
            particles.Clear();
            bladeLineValid = false;
            bladeVelocity = Vector2.Zero;
        }
    }
}
