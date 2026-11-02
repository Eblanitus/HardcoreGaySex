using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    // Взмах Северного полюса — «Полярный компас» (02.11.2026, вторая редакция по скрину из игры).
    // Своя форма, не общая лента серпа, и вся — в пределах длины копья:
    // • Ось — тонкая яркая стрелка вдоль копья до острия, идёт по кругу вместе с ним.
    // • Меридианы — за осью через каждые 15° остаются короткие радиальные деления разной
    //   толщины (каждое 45° длиннее и толще, каждые 90° — самое длинное). Ведущие, у стрелки,
    //   горят мятой сияния и уходят в фиолет, старые — холодный белый/голубой; гаснут с хвоста
    //   за ~0.2 с, после конца взмаха всё исчезает примерно за 0.3 с.
    // • Широта — тонкая окружность у внешнего конца делений, только на пройденном угле.
    // • Пик взмаха — полярная звезда на острие (едет с ним): 4 сужающихся луча разной длины,
    //   север длиннее, лучи не сходятся в центре (не перекрестие прицела); одно маленькое кольцо.
    // • Под каждым штрихом — тёмная подложка обычным смешением, чтобы читалось на снегу.
    // Рисуется после пыли (On_Main.DrawDust, как огонь Фростборна): поверх врагов и игрока.
    // Чисто визуально: на каждом клиенте свой экземпляр из того же снаряда взмаха.
    public class PolarCompassFx : ModSystem
    {
        // Масштаб: всё внутри длины копья (на скрине из игры шкала больше копья читалась интерфейсом)
        public const float MeridianStep = MathHelper.Pi / 12f;  // 15° между делениями (24 на круг)
        public const int MeridianLife = 10;                     // тиков живёт деление: эффект гаснет ~0.2-0.3 с
        public const float MeridianOuter = 0.86f;               // внешний конец деления, доля длины копья
        public const float MinorInner = 0.74f;                  // внутренний конец: обычное деление
        public const float MajorInner = 0.6f;                   // каждое 3-е (45°)
        public const float CardinalInner = 0.44f;               // каждое 6-е (90°)
        public const float LatitudeRadius = 0.9f;               // окружность-широта, доля длины копья
        public const float AxisInner = 0.1f;                    // ось начинается чуть от центра
        public const float UnderPad = 2f;                       // подложка шире штриха на столько px с каждой стороны
        public const float UnderAlpha = 0.55f;
        public const int LeadTicks = 5;                         // столько тиков деление несёт цвет сияния
        public const int StarLife = 18;
        public const float StarNorth = 44f;                     // лучи звезды, px: север самый длинный,
        public const float StarSouth = 24f;                     // юг короче,
        public const float StarSide = 16f;                      // запад/восток короче всех
        public const float StarGap = 3f;                        // лучи не сходятся в центре — не перекрестие
        public const int RingLife = 12;
        public const float RingMax = 22f;                       // радиус кольца в конце, px
        public const int HitLife = 10;

        public static readonly Color Cold = new Color(236, 246, 255);
        public static readonly Color Pale = new Color(160, 205, 255);
        public static readonly Color Night = new Color(6, 10, 34);
        public static readonly Color Mint = new Color(90, 245, 190);
        public static readonly Color Violet = new Color(170, 95, 255);

        internal struct Meridian
        {
            public float Angle;
            public int Born;
            public int Index;
        }

        public class Swing
        {
            internal int Owner;
            internal float Start;
            internal float Face;
            internal int Next = 1;
            internal float Axis;
            internal float Radius;
            internal bool Open = true;
            internal int LastTrack = Now;
            internal int ClosedAt;
            internal int PeakTick = -1;
            internal float PeakAngle;
            internal float PeakRadius;
            internal readonly List<Meridian> Marks = new List<Meridian>();

            // Каждый тик взмаха: ось на угле angle; за ней ровно через MeridianStep остаются деления.
            // Замах назад делений не оставляет — только движение вперёд по кругу.
            public void Track(float angle, float radius, bool peak)
            {
                if (!Open)
                    return;
                Axis = angle;
                Radius = radius;
                LastTrack = Now;
                float swept = (angle - Start) * Face;
                while (swept >= Next * MeridianStep)
                {
                    Marks.Add(new Meridian { Angle = Start + Face * Next * MeridianStep, Born = Now, Index = Next });
                    Next++;
                }
                if (peak && PeakTick < 0)
                {
                    PeakTick = Now;
                    PeakAngle = angle;
                    PeakRadius = radius;
                }
            }

            public void Close()
            {
                if (!Open)
                    return;
                Open = false;
                ClosedAt = Now;
            }

            internal bool Done
            {
                get
                {
                    if (Open)
                        return false;
                    int age = Now - ClosedAt;
                    return age > MeridianLife && (PeakTick < 0 || Now - PeakTick > Math.Max(StarLife, RingLife));
                }
            }
        }

        private struct Hit
        {
            public Vector2 Pos;
            public int Born;
        }

        private static readonly List<Swing> swings = new List<Swing>();
        private static readonly List<Hit> hits = new List<Hit>();

        private static int Now => (int)Main.GameUpdateCount;

        public static Swing Begin(int owner, float startAngle, float face)
        {
            if (Main.dedServ)
                return null;
            Swing s = new Swing { Owner = owner, Start = startAngle, Face = face < 0f ? -1f : 1f, Axis = startAngle };
            swings.Add(s);
            return s;
        }

        public static void AddHit(Vector2 pos)
        {
            if (Main.dedServ)
                return;
            hits.Add(new Hit { Pos = pos, Born = Now });
            if (hits.Count > 8)
                hits.RemoveAt(0);
        }

        public override void Load()
        {
            if (Main.dedServ)
                return;
            On_Main.DrawDust += DrawAfterDust;
        }

        public override void OnWorldUnload()
        {
            swings.Clear();
            hits.Clear();
        }

        public override void PostUpdateProjectiles()
        {
            for (int i = swings.Count - 1; i >= 0; i--)
            {
                Swing s = swings[i];
                Player p = s.Owner >= 0 && s.Owner < Main.maxPlayers ? Main.player[s.Owner] : null;
                // снаряд взмаха пропал без Kill — закрываем сами, чтобы стрелка не зависла
                if (s.Open && Now - s.LastTrack > 3)
                    s.Close();
                if (p == null || !p.active || s.Done)
                {
                    swings.RemoveAt(i);
                    continue;
                }
                s.Marks.RemoveAll(m => Now - m.Born > MeridianLife);
            }
            hits.RemoveAll(h => Now - h.Born > HitLife);
        }

        private static void DrawAfterDust(On_Main.orig_DrawDust orig, Main self)
        {
            orig(self);
            if ((swings.Count == 0 && hits.Count == 0) || Main.gameMenu)
                return;
            SpriteBatch sb = Main.spriteBatch;
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            try
            {
                // Сначала все подложки, потом весь свет: тёмное не ложится поверх соседних штрихов
                for (int pass = 0; pass < 2; pass++)
                {
                    foreach (Swing s in swings)
                        DrawSwing(s, pass == 0);
                    foreach (Hit h in hits)
                        DrawHit(h, pass == 0);
                }
            }
            finally
            {
                sb.End();
            }
        }

        // === Рисование ===

        private static float MarkAlpha(int born)
        {
            float k = 1f - (Now - born) / (float)MeridianLife;
            return k <= 0f ? 0f : (float)Math.Pow(k, 1.4f);
        }

        // Цвет деления по возрасту: ведущие (свежие, у стрелки) — мята сияния, уходят в фиолет,
        // старые — холодный белый/голубой по роду деления
        private static Color MarkColor(int age, int kind)
        {
            Color rest = kind == 2 ? Cold : kind == 1 ? Color.Lerp(Pale, Cold, 0.5f) : Pale;
            if (age <= 1)
                return Mint;
            if (age < LeadTicks)
                return Color.Lerp(Mint, Violet, (age - 1) / (float)(LeadTicks - 1));
            return Color.Lerp(Violet, rest, MathHelper.Clamp((age - LeadTicks) / 3f, 0f, 1f));
        }

        private static void DrawSwing(Swing s, bool under)
        {
            Player p = Main.player[s.Owner];
            Vector2 c = p.MountedCenter;
            float r = s.Radius;
            if (r <= 1f)
                return;

            // меридианы-деления + засечки на широте; толщина по роду и тает с возрастом
            float lat = r * LatitudeRadius;
            for (int i = 0; i < s.Marks.Count; i++)
            {
                Meridian m = s.Marks[i];
                float a = MarkAlpha(m.Born);
                if (a <= 0.02f)
                    continue;
                int kind = m.Index % 6 == 0 ? 2 : m.Index % 3 == 0 ? 1 : 0;
                float inner = kind == 2 ? CardinalInner : kind == 1 ? MajorInner : MinorInner;
                float width = (kind == 2 ? 2.4f : kind == 1 ? 1.6f : 1f) * (0.6f + 0.4f * a);
                float tick = kind == 2 ? 6f : kind == 1 ? 4f : 2f;
                Color col = MarkColor(Now - m.Born, kind);
                Vector2 dir = m.Angle.ToRotationVector2();
                Stroke(c + dir * r * inner, c + dir * r * MeridianOuter, width, col, a, under);
                Stroke(c + dir * lat, c + dir * (lat + tick), width, col, a, under);
            }

            // широта: дуга между соседними делениями, затем от последнего до оси (у стрелки — мята)
            for (int i = 1; i < s.Marks.Count; i++)
            {
                float a = Math.Min(MarkAlpha(s.Marks[i - 1].Born), MarkAlpha(s.Marks[i].Born));
                if (a > 0.02f)
                    Arc(c, lat, s.Marks[i - 1].Angle, s.Marks[i].Angle, 3, 1f,
                        MarkColor(Now - s.Marks[i].Born, 0), a * 0.85f, under);
            }
            if (s.Open && s.Marks.Count > 0)
                Arc(c, lat, s.Marks[s.Marks.Count - 1].Angle, s.Axis, 3, 1f, Mint, 0.9f, under);

            Vector2 axisDir = s.Axis.ToRotationVector2();
            Vector2 tip = c + axisDir * r;
            // ось-стрелка — вдоль самого копья, до его острия
            if (s.Open)
            {
                Stroke(c + axisDir * r * AxisInner, tip, 2f, Cold, 0.9f, under);
                if (!under && NorthPoleFx.Glow is Texture2D glow)
                {
                    Vector2 mid = c + axisDir * r * 0.55f;
                    Main.spriteBatch.Draw(glow, mid - Main.screenPosition, null, Add(Mint, 0.3f), s.Axis,
                        glow.Size() / 2f, new Vector2(r * 0.95f / glow.Width, 8f / glow.Height), SpriteEffects.None, 0f);
                    Main.spriteBatch.Draw(glow, tip - Main.screenPosition, null, Add(Cold, 0.8f), 0f,
                        glow.Size() / 2f, 10f / glow.Width, SpriteEffects.None, 0f);
                }
            }

            // звезда — на острие: пока взмах идёт, едет вместе с ним, потом гаснет на месте
            if (s.PeakTick >= 0)
                DrawStar(tip, Now - s.PeakTick, under);
        }

        // Полярная звезда: 4 сужающихся луча разной длины (север длиннее), в центре
        // не сходятся — не перекрестие прицела; одно небольшое кольцо
        private static void DrawStar(Vector2 pos, int age, bool under)
        {
            if (age < StarLife)
            {
                float env = age < 2 ? (age + 1) / 3f : 1f - (age - 2) / (float)(StarLife - 2);
                env = MathHelper.Clamp(env, 0f, 1f);
                float grow = 0.7f + 0.3f * env;
                Ray(pos, -MathHelper.PiOver2, StarNorth * grow, Mint, env, under);  // север
                Ray(pos, MathHelper.PiOver2, StarSouth * grow, Violet, env, under); // юг
                Ray(pos, 0f, StarSide * grow, Violet, env * 0.85f, under);          // восток
                Ray(pos, MathHelper.Pi, StarSide * grow, Violet, env * 0.85f, under); // запад
                if (!under && NorthPoleFx.Glow is Texture2D glow)
                {
                    Vector2 o = glow.Size() / 2f;
                    Vector2 at = pos - Main.screenPosition;
                    Main.spriteBatch.Draw(glow, at, null, Add(Violet, 0.4f * env), 0f, o, 22f / glow.Width, SpriteEffects.None, 0f);
                    Main.spriteBatch.Draw(glow, at, null, Add(Color.White, env), 0f, o, 9f / glow.Width, SpriteEffects.None, 0f);
                }
            }
            if (age < RingLife)
            {
                float t = age / (float)RingLife;
                float radius = 8f + RingMax * (1f - (1f - t) * (1f - t));
                Arc(pos, radius, 0f, MathHelper.TwoPi, 28, 1.2f, Color.Lerp(Mint, Violet, t), 0.7f * (1f - t), under);
            }
        }

        // Луч: от StarGap наружу, тремя отрезками — толще и ярче у центра, тоньше к концу
        private static void Ray(Vector2 pos, float angle, float length, Color tint, float alpha, bool under)
        {
            Vector2 d = angle.ToRotationVector2();
            float span = length - StarGap;
            for (int k = 0; k < 3; k++)
            {
                float t0 = k / 3f, t1 = (k + 1) / 3f;
                Color col = Color.Lerp(Cold, tint, t1);
                Stroke(pos + d * (StarGap + span * t0), pos + d * (StarGap + span * t1), 2.2f - 0.7f * k, col,
                    alpha * (1f - 0.3f * k), under);
            }
        }

        private static void DrawHit(Hit h, bool under)
        {
            int age = Now - h.Born;
            float a = 1f - age / (float)HitLife;
            if (a <= 0f)
                return;
            float len = 14f * (0.7f + 0.3f * a);
            for (int k = 0; k < 4; k++)
            {
                Vector2 d = (k * MathHelper.PiOver2 + MathHelper.PiOver4).ToRotationVector2();
                Stroke(h.Pos, h.Pos + d * len, 1.4f, Cold, a, under);
            }
            if (!under && NorthPoleFx.Glow is Texture2D glow)
                Main.spriteBatch.Draw(glow, h.Pos - Main.screenPosition, null, Add(Violet, 0.6f * a), 0f,
                    glow.Size() / 2f, 22f / glow.Width, SpriteEffects.None, 0f);
        }

        // === Примитивы ===

        private static Color Add(Color c, float alpha)
        {
            Color r = c * MathHelper.Clamp(alpha, 0f, 1f);
            r.A = 0;
            return r;
        }

        // Штрих: в проходе подложки — тёмная полоса шире на UnderPad (обычное смешение),
        // в проходе света — тонкая яркая линия (аддитивно, A = 0). MagicPixel — строго (0,0,1,1).
        private static void Stroke(Vector2 a, Vector2 b, float width, Color color, float alpha, bool under)
        {
            if (alpha <= 0.01f)
                return;
            Vector2 d = b - a;
            float len = d.Length();
            if (len < 0.5f)
                return;
            Texture2D px = TextureAssets.MagicPixel.Value;
            float rot = d.ToRotation();
            if (under)
            {
                Vector2 back = d / len * UnderPad;
                Main.spriteBatch.Draw(px, a - back - Main.screenPosition, new Rectangle(0, 0, 1, 1),
                    Night * (UnderAlpha * alpha), rot, new Vector2(0f, 0.5f),
                    new Vector2(len + UnderPad * 2f, width + UnderPad * 2f), SpriteEffects.None, 0f);
            }
            else
            {
                Main.spriteBatch.Draw(px, a - Main.screenPosition, new Rectangle(0, 0, 1, 1),
                    Add(color, alpha), rot, new Vector2(0f, 0.5f), new Vector2(len, width), SpriteEffects.None, 0f);
            }
        }

        private static void Arc(Vector2 c, float radius, float from, float to, int segments, float width, Color color,
            float alpha, bool under)
        {
            if (alpha <= 0.01f)
                return;
            Vector2 prev = c + from.ToRotationVector2() * radius;
            for (int i = 1; i <= segments; i++)
            {
                float ang = MathHelper.Lerp(from, to, i / (float)segments);
                Vector2 next = c + ang.ToRotationVector2() * radius;
                Stroke(prev, next, width, color, alpha, under);
                prev = next;
            }
        }
    }
}
