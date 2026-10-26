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

        // Живой язык цельного пламени: цепочка узлов от основания к кончику.
        // Основание сидит на горящем враге (NpcIndex) или летит свободно
        // (вспышка взрыва); узлы тянутся вверх с вилянием и отставанием —
        // пламя лижет и тянется за движением, а не висит россыпью пятен
        private sealed class Tongue
        {
            public readonly Vector2[] Nodes = new Vector2[7];
            public Vector2 Base;
            public Vector2 Vel;
            public Vector2 GrowDir = new Vector2(0f, -1f);
            public int Age;
            public int Life;
            public float Phase;
            public float Length;
            public float Width;
            public int NpcIndex = -1;
            public float Across;
            public float Strength = 1f;
        }

        private static readonly List<Tongue> tongues = new();
        private const int MaxTongues = 56;
        private static readonly Vector2[] ribbonPoints = new Vector2[96];
        private static readonly float[] ribbonWidths = new float[96];
        private static readonly Color[] ribbonColors = new Color[96];
        private static readonly List<Particle> sheetMain = new();
        private static readonly List<Particle> sheetHalo = new();

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
            {
                Particle trail = EmitLineParticle(butt, tip, dir, perp, FlameKind.Trail, Main.rand.NextFloat(0.15f, 0.85f),
                    0.2f, 0.6f, 0.3f, 9, 13, 0.24f, 0.34f, false);
                // Свободная частица клинка В РУКЕ уносит часть скорости клинка:
                // без этого на быстром начале взмаха она остаётся висеть там,
                // где родилась, и выглядит отставшей (замечание владельца)
                if (anchored && trail != null)
                    trail.Velocity += bladeVelocity * 0.5f;
            }

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
                if (anchored)
                    velocity += bladeVelocity * 0.3f;
                Add(position, velocity, FlameKind.Spark, Main.rand.Next(15, 23), Main.rand.NextFloat(0.08f, 0.14f) * emitSizeScale);
            }
        }

        // baseVelocity — скорость самой струи: частицы уносят её часть и летят
        // вместе с веером. Без этого они рождались почти неподвижными, и пока
        // клинок уходил дальше, позади взмаха оставались висеть сгустки
        // «отставшего» пламени (замечание владельца, обведённые сгустки)
        public static void EmitJet(Vector2 center, Vector2 dir, Vector2 baseVelocity = default)
        {
            if (Main.dedServ)
                return;

            dir = dir.SafeNormalize(Vector2.UnitX);
            Vector2 perp = new Vector2(-dir.Y, dir.X);

            EmitJetParticle(center, dir, perp, FlameKind.Blade, -0.02f, 0.06f, 0.09f, 6, 9, 0.26f, 0.36f, baseVelocity);
            EmitJetParticle(center, dir, perp, FlameKind.Blade, -0.01f, 0.05f, 0.09f, 6, 9, 0.26f, 0.36f, baseVelocity);
            EmitJetParticle(center, dir, perp, FlameKind.Near, 0f, 0.15f, 0.12f, 7, 10, 0.28f, 0.38f, baseVelocity);
            EmitJetParticle(center, dir, perp, FlameKind.Near, 0.04f, 0.2f, 0.12f, 7, 10, 0.28f, 0.38f, baseVelocity);
            EmitJetParticle(center, dir, perp, FlameKind.Halo, 0.05f, 0.35f, 0.22f, 9, 13, 0.36f, 0.48f, baseVelocity);

            if (Main.rand.NextFloat() < 0.5f)
                EmitJetParticle(center, dir, perp, FlameKind.Trail, 0.25f, 0.7f, 0.3f, 9, 13, 0.24f, 0.34f, baseVelocity);

            if (Main.rand.NextFloat() < 0.08f)
            {
                Vector2 position = center + perp * Main.rand.NextFloat(-3.5f, 3.5f) + dir * Main.rand.NextFloat(0f, 4f);
                Vector2 velocity = dir * Main.rand.NextFloat(1.2f, 2.2f)
                    + perp * Main.rand.NextFloat(-0.45f, 0.45f)
                    + new Vector2(0f, -Main.rand.NextFloat(0.2f, 0.7f))
                    + baseVelocity * 0.4f;
                Add(position, velocity, FlameKind.Spark, Main.rand.Next(11, 17), Main.rand.NextFloat(0.08f, 0.14f));
            }
        }

        private static Particle EmitLineParticle(Vector2 butt, Vector2 tip, Vector2 dir, Vector2 perp,
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
            return p;
        }

        private static void EmitJetParticle(Vector2 center, Vector2 dir, Vector2 perp,
            FlameKind kind, float speedMin, float speedMax, float sideSpeed,
            int lifeMin, int lifeMax, float sizeMin, float sizeMax, Vector2 baseVelocity)
        {
            Vector2 position = center + perp * Main.rand.NextFloat(-4f, 4f) + dir * Main.rand.NextFloat(-2f, 2.5f);
            float inherit = kind switch
            {
                FlameKind.Blade => 0.9f,
                FlameKind.Near => 0.8f,
                FlameKind.Halo => 0.65f,
                _ => 0.55f
            };
            Vector2 velocity = dir * Main.rand.NextFloat(speedMin, speedMax)
                + perp * Main.rand.NextFloat(-sideSpeed, sideSpeed)
                + baseVelocity * inherit;
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

        // Языки вспышки холодного огня (взрыв горящего врага): веер коротких
        // языков из центра, тянутся вверх и тают. Вызывает FrostFireSystem.Burst
        public static void BurstTongues(Vector2 center, bool big)
        {
            if (Main.dedServ)
                return;
            int count = big ? 9 : 6;
            for (int i = 0; i < count && tongues.Count < MaxTongues; i++)
            {
                Vector2 dir = Main.rand.NextFloat(MathHelper.TwoPi).ToRotationVector2();
                Tongue t = new Tongue
                {
                    Base = center,
                    Vel = dir * Main.rand.NextFloat(1.2f, big ? 3.6f : 2.6f),
                    GrowDir = (dir + new Vector2(0f, -0.8f)).SafeNormalize(new Vector2(0f, -1f)),
                    Life = Main.rand.Next(16, 26),
                    Phase = Main.rand.NextFloat(MathHelper.TwoPi),
                    Length = Main.rand.NextFloat(big ? 34f : 24f, big ? 58f : 40f),
                    Width = Main.rand.NextFloat(11f, 17f)
                };
                for (int k = 0; k < t.Nodes.Length; k++)
                    t.Nodes[k] = center + t.GrowDir * (t.Length * 0.3f * k / (t.Nodes.Length - 1));
                tongues.Add(t);
            }
        }

        private static Vector2 NpcTongueBase(NPC npc, float across)
            => new Vector2(npc.position.X + npc.width * across, npc.position.Y + npc.height * 0.3f);

        private static void SpawnNpcTongue(NPC npc, float across)
        {
            Tongue t = new Tongue
            {
                NpcIndex = npc.whoAmI,
                Across = MathHelper.Clamp(across, 0.1f, 0.9f),
                Phase = Main.rand.NextFloat(MathHelper.TwoPi),
                Length = MathHelper.Clamp((npc.height * 0.85f + 16f) * Main.rand.NextFloat(0.75f, 1.15f), 26f, 115f),
                Width = MathHelper.Clamp(npc.width * 0.42f * Main.rand.NextFloat(0.8f, 1.2f), 11f, 42f),
                Strength = 0f // разгорится плавно в UpdateTongues
            };
            t.Base = NpcTongueBase(npc, t.Across);
            for (int k = 0; k < t.Nodes.Length; k++)
                t.Nodes[k] = t.Base + new Vector2(0f, -t.Length * 0.3f * k / (t.Nodes.Length - 1));
            tongues.Add(t);
        }

        private static float SmoothFlame(float x)
        {
            x = MathHelper.Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        private static void UpdateTongues()
        {
            // Подпитка языков на горящих врагах в кадре: 1–3 языка по ширине тела.
            // Горящий враг = держатель языка запаса меча: пламя на нём теперь
            // настоящее цельное, а не ванильные искры Frostburn
            Rectangle screen = new Rectangle((int)Main.screenPosition.X - 240, (int)Main.screenPosition.Y - 240,
                Main.screenWidth + 480, Main.screenHeight + 480);
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!npc.active || npc.friendly || !screen.Intersects(npc.Hitbox))
                    continue;
                if (!Weapons.FrostFireSystem.IsBurning(npc))
                    continue;
                int desired = (int)MathHelper.Clamp(npc.width / 26, 1, 3);
                int have = 0;
                foreach (Tongue t in tongues)
                    if (t.NpcIndex == i)
                        have++;
                for (int k = have; k < desired && tongues.Count < MaxTongues; k++)
                    SpawnNpcTongue(npc, (k + 0.5f) / desired + Main.rand.NextFloat(-0.08f, 0.08f));
            }

            float time = Main.GameUpdateCount;
            for (int i = tongues.Count - 1; i >= 0; i--)
            {
                Tongue t = tongues[i];
                t.Age++;
                if (t.NpcIndex >= 0)
                {
                    NPC npc = Main.npc[t.NpcIndex];
                    bool alive = npc.active && Weapons.FrostFireSystem.IsBurning(npc);
                    t.Strength = MathHelper.Clamp(t.Strength + (alive ? 0.06f : -0.08f), 0f, 1f);
                    if (t.Strength <= 0.01f)
                    {
                        tongues.RemoveAt(i);
                        continue;
                    }
                    if (npc.active)
                        t.Base = NpcTongueBase(npc, t.Across);
                    t.GrowDir = new Vector2(0f, -1f);
                }
                else
                {
                    if (t.Age >= t.Life)
                    {
                        tongues.RemoveAt(i);
                        continue;
                    }
                    t.Base += t.Vel;
                    t.Vel *= 0.94f;
                    t.Vel.Y -= 0.06f;
                }

                float grow = SmoothFlame(System.Math.Min(1f, t.Age / 8f));
                float shrink = t.NpcIndex >= 0 ? 1f : 1f - SmoothFlame((t.Age - (t.Life - 9)) / 9f);
                // Лижущее дыхание: длина языка гуляет, кончик виляет всё сильнее к концу
                float breathe = 0.78f + 0.22f * (float)System.Math.Sin(time * 0.23f + t.Phase);
                float effLen = t.Length * grow * shrink * breathe * (0.35f + 0.65f * t.Strength);
                int n = t.Nodes.Length;
                float seg = effLen / (n - 1);
                t.Nodes[0] = t.Base;
                Vector2 dir = t.GrowDir;
                for (int k = 1; k < n; k++)
                {
                    float sway = ((float)System.Math.Sin(time * 0.31f + t.Phase + k * 0.85f) * 0.16f
                        + (float)System.Math.Sin(time * 0.83f + t.Phase * 2f + k * 2.1f) * 0.06f) * k / (n - 1);
                    Vector2 want = t.Nodes[k - 1] + dir.RotatedBy(sway) * seg;
                    // Отставание узлов даёт текучесть: враг дёрнулся — пламя потянулось за ним
                    t.Nodes[k] = Vector2.Lerp(t.Nodes[k], want, 0.6f);
                    dir = (t.Nodes[k] - t.Nodes[k - 1]).SafeNormalize(dir);
                }
            }
        }

        public override void PostUpdateEverything()
        {
            if (Main.dedServ)
                return;

            UpdateTongues();

            if (particles.Count == 0)
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
                    // Резина (пожелание владельца): тяга к точке на клинке растёт
                    // с расстоянием — у самой кромки пламя сидит туго и густо, а
                    // оторвавшийся хвост не висит по дуге взмаха рядом белых
                    // языков (flame-1), а пружинит обратно к клинку
                    float anchorDist = (anchor - p.Position).Length();
                    float follow = 1f - (float)System.Math.Exp(-(p.Coupling * 0.55f + anchorDist * 0.022f));
                    float wobble = (float)System.Math.Sin((p.Age + p.Wobble) * 0.55f) * 1.1f;
                    p.Position = Vector2.Lerp(p.Position, anchor, follow)
                        + perp * wobble
                        + p.Velocity * 0.2f;
                    // Далеко от клинка пламя быстро остывает и тает, а не висит
                    if (anchorDist > 44f)
                        p.Temperature = MathHelper.Max(0f, p.Temperature - 0.05f);
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

        // Один язык двумя проходами: широкая тёмно-синяя оболочка и яркое
        // бело-голубое ядро поверх. Ширина — колокол: узко у основания,
        // жирно на трети, в нитку на кончике
        private static void DrawTongue(GraphicsDevice device, Tongue t)
        {
            int n = t.Nodes.Length;
            float time = Main.GameUpdateCount;
            float flick = 0.8f + 0.12f * (float)System.Math.Sin(time * 0.9f + t.Phase * 3f)
                + 0.08f * (float)System.Math.Sin(time * 2.3f + t.Phase * 7f);
            float env = t.Strength * flick;
            if (t.NpcIndex < 0)
                env *= MathHelper.Clamp((t.Life - t.Age) / 8f, 0f, 1f) * MathHelper.Clamp(t.Age / 3f, 0f, 1f);
            if (env <= 0.02f)
                return;
            for (int pass = 0; pass < 2; pass++)
            {
                float wMul = pass == 0 ? 1.85f : 0.85f;
                for (int k = 0; k < n; k++)
                {
                    float f = k / (float)(n - 1);
                    ribbonPoints[k] = t.Nodes[k];
                    float profile = (float)System.Math.Pow(
                        System.Math.Sin(System.Math.PI * MathHelper.Clamp(0.12f + 0.88f * f, 0f, 1f)), 0.75f);
                    // Рваный край: ширина «кипит» по длине и во времени,
                    // иначе язык — гладкая капля-свечка (flame-1)
                    float ragged = 0.78f + 0.22f * (float)System.Math.Sin(time * 1.35f + t.Phase * 7f + k * 2.2f)
                        * (float)System.Math.Sin(time * 0.7f + k * 1.3f);
                    ribbonWidths[k] = t.Width * profile * ragged * wMul;
                    // Белое — только жар у самого основания; тело языка синее
                    ribbonColors[k] = pass == 0
                        ? Color.Lerp(new Color(28, 105, 245), new Color(8, 34, 130), f) * (0.55f * env)
                        : Color.Lerp(
                            Color.Lerp(new Color(205, 247, 255), new Color(110, 200, 255), MathHelper.Clamp(f * 3.2f, 0f, 1f)),
                            new Color(30, 110, 250), MathHelper.Clamp((f - 0.3f) * 1.6f, 0f, 1f)) * (0.85f * env);
                }
                FlameRibbon.Draw(device, ribbonPoints, ribbonWidths, ribbonColors, n);
            }
        }

        // Цельный лист пламени по клинку В РУКЕ: привязанные частицы уже лежат
        // вдоль клинка — соединяем их лентой по порядку вдоль лезвия, и россыпь
        // пятен превращается в одно пламя на кромке. Спрайты частиц поверх
        // остаются приглушённым жаром (см. PostDrawTiles)
        private static void DrawBladeSheets(GraphicsDevice device)
        {
            sheetMain.Clear();
            sheetHalo.Clear();
            foreach (Particle p in particles)
            {
                if (!p.Anchored)
                    continue;
                if (p.Kind == FlameKind.Halo)
                    sheetHalo.Add(p);
                else if (p.Kind == FlameKind.Blade || p.Kind == FlameKind.Near)
                    sheetMain.Add(p);
            }
            DrawSheet(device, sheetHalo, 58f, 0.34f);
            DrawSheet(device, sheetMain, 34f, 0.95f);
        }

        private static void DrawSheet(GraphicsDevice device, List<Particle> list, float widthScale, float strength)
        {
            if (list.Count < 3)
                return;
            list.Sort((a, b) => a.Along.CompareTo(b.Along));
            int n = System.Math.Min(list.Count, ribbonPoints.Length);
            float time = Main.GameUpdateCount;
            for (int i = 0; i < n; i++)
            {
                Particle p = list[i];
                ribbonPoints[i] = p.Position;
                // Резина листа (пожелание владельца): где пламя растянуто
                // (соседние частицы далеко) и где оно далеко от самого
                // клинка — лента тоньше и тусклее; у кромки, где густо, —
                // жирная и яркая. Всё остаётся одной связанной лентой
                float gap = 0f;
                if (i > 0)
                    gap += Vector2.Distance(p.Position, list[i - 1].Position);
                if (i < n - 1)
                    gap += Vector2.Distance(p.Position, list[i + 1].Position);
                gap /= (i == 0 || i == n - 1) ? 1 : 2;
                float stretch = MathHelper.Clamp(gap / 9f, 0.6f, 3f);
                float bladeDist = DistanceToBlade(p.Position);
                float thin = MathHelper.Clamp(1.15f - bladeDist * 0.02f, 0.35f, 1.15f) / (0.55f + 0.45f * stretch);
                // Ширина дышит бегущей волной вдоль клинка: пламя «течёт» к острию
                float wave = 0.85f + 0.15f * (float)System.Math.Sin(time * 0.7f - p.Along * 11f + p.Wobble);
                ribbonWidths[i] = p.Size * widthScale * wave * thin;
                float heat = MathHelper.Clamp(p.Temperature, 0f, 1f);
                // Белое — только на полном жаре (степень 1.5): тело листа синее
                Color c = Color.Lerp(new Color(24, 98, 238), new Color(168, 234, 255), (float)System.Math.Pow(heat, 1.5f));
                float t = p.Age / (float)p.Life;
                float dim = MathHelper.Clamp(1.1f - bladeDist * 0.016f, 0.25f, 1f) / (0.6f + 0.4f * stretch);
                ribbonColors[i] = c * (strength * dim * MathHelper.Clamp((1f - t) * 2f, 0f, 1f));
            }
            FlameRibbon.Draw(device, ribbonPoints, ribbonWidths, ribbonColors, n);
        }

        private static float DistanceToBlade(Vector2 point)
        {
            if (!bladeLineValid)
                return 0f;
            Vector2 ab = bladeTip - bladeButt;
            float lenSq = ab.LengthSquared();
            float t = lenSq > 0.01f ? MathHelper.Clamp(Vector2.Dot(point - bladeButt, ab) / lenSq, 0f, 1f) : 0f;
            return Vector2.Distance(point, bladeButt + ab * t);
        }

        // Свободные частицы (брошенный/застрявший меч, струи): каждая — короткий
        // язычок по ходу движения с подъёмом вверх; плотная россыпь сливается
        // в общее пламя, как языки настоящего костра
        private static void DrawLooseLicks(GraphicsDevice device)
        {
            float time = Main.GameUpdateCount;
            foreach (Particle p in particles)
            {
                if (p.Anchored || p.Kind == FlameKind.Spark)
                    continue;
                Vector2 up = (p.Velocity * 0.65f + new Vector2(0f, -0.85f)).SafeNormalize(new Vector2(0f, -1f));
                Vector2 perp = new Vector2(-up.Y, up.X);
                float len = 6f + p.Size * 44f;
                // Разброс размеров: одинаковые язычки стояли гирляндой свечек
                float sizeJitter = 0.8f + 0.35f * (float)System.Math.Sin(p.Rotation * 13.7f);
                float wob = (float)System.Math.Sin(time * 0.5f + p.Rotation * 7f + p.Age * 0.4f) * 1.6f;
                float t = p.Age / (float)p.Life;
                float fade = MathHelper.Clamp((1f - t) * 1.8f, 0f, 1f) * MathHelper.Clamp(t * 6f + 0.25f, 0f, 1f);
                float heat = MathHelper.Clamp(p.Temperature, 0f, 1f);
                Color hot = Color.Lerp(new Color(32, 112, 245), new Color(168, 234, 255), (float)System.Math.Pow(heat, 1.4f));
                Color cold = Color.Lerp(new Color(8, 34, 130), new Color(40, 125, 250), heat);
                ribbonPoints[0] = p.Position - up * len * 0.35f;
                ribbonPoints[1] = p.Position + perp * wob * 0.4f;
                ribbonPoints[2] = p.Position + up * len * 0.65f + perp * wob;
                ribbonWidths[0] = p.Size * 17f * sizeJitter;
                ribbonWidths[1] = p.Size * 27f * sizeJitter;
                ribbonWidths[2] = 1f;
                ribbonColors[0] = hot * (0.78f * fade);
                ribbonColors[1] = hot * (0.62f * fade);
                ribbonColors[2] = cold * (0.5f * fade);
                FlameRibbon.Draw(device, ribbonPoints, ribbonWidths, ribbonColors, 3);
            }
        }

        public override void PostDrawTiles()
        {
            if (Main.dedServ || (particles.Count == 0 && tongues.Count == 0))
                return;

            // Сначала цельное пламя лентами (батч на входе закрыт): языки на
            // врагах и вспышках, лист по клинку, язычки свободных частиц
            GraphicsDevice device = Main.graphics.GraphicsDevice;
            foreach (Tongue t in tongues)
                DrawTongue(device, t);
            DrawBladeSheets(device);
            DrawLooseLicks(device);

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
                Color core = Color.Lerp(new Color(40, 110, 230), new Color(172, 236, 255), (float)System.Math.Pow(heat, 1.4f));
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
                // Тело пламени теперь несут ленты выше; спрайты частиц — только
                // жар поверх и искры, поэтому приглушены (полная сила — искрам).
                // Привязанные к клинку гасим сильнее всех: вытянутые белые ядра
                // спрайтов и давали «гирлянду свечек» по дуге взмаха (flame-1)
                float bodyDim = p.Kind == FlameKind.Spark ? 1f : (p.Anchored ? 0.22f : 0.4f);
                float coreDim = p.Kind == FlameKind.Spark ? 1f : (p.Anchored ? 0.35f : 0.5f);

                Main.EntitySpriteDraw(glow, p.Position - Main.screenPosition, null,
                    outer * (outerAlpha * fade * bodyDim), p.Rotation, origin, scaleVec, SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(glow, p.Position - Main.screenPosition, null,
                    core * (coreAlpha * fade * coreDim), p.Rotation, origin,
                    scaleVec * 0.58f, SpriteEffects.None, 0f);
            }

            Main.spriteBatch.End();
        }

        public override void OnWorldUnload()
        {
            particles.Clear();
            tongues.Clear();
            bladeLineValid = false;
            bladeVelocity = Vector2.Zero;
        }

        public override void Unload()
        {
            particles.Clear();
            tongues.Clear();
            bladeLineValid = false;
            bladeVelocity = Vector2.Zero;
            FlameRibbon.Unload();
        }
    }
}
