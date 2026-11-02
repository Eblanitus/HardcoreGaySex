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
    // Взмах Северного полюса — «Полярный компас» (02.11.2026). Своя форма, не общая лента серпа:
    // • Ось — тонкая яркая стрелка от игрока до острия, идёт по кругу вместе с копьём.
    // • Меридианы — за осью через каждые 10° остаются тонкие радиальные штрихи-деления
    //   (каждый 3-й длиннее, каждый 9-й, через 90°, — самый длинный). Гаснут по одному,
    //   первыми — самые старые, с хвоста.
    // • Широта — тонкая окружность по внешнему краю с засечками, только на пройденном угле.
    // • Пик взмаха — на острие вспыхивает полярная звезда (4 длинных луча крестом),
    //   от неё расходятся два тонких кольца.
    // • Цвет — холодный белый и бледно-голубой; мята и фиолет сияния только на звезде и кольцах.
    // • Под каждым штрихом — тёмная подложка обычным смешением, чтобы читалось на снегу.
    // Рисуется после пыли (On_Main.DrawDust, как огонь Фростборна): поверх врагов и игрока.
    // Чисто визуально: на каждом клиенте свой экземпляр из того же снаряда взмаха.
    public class PolarCompassFx : ModSystem
    {
        public const float MeridianStep = MathHelper.Pi / 18f;  // 10° между делениями
        public const int MeridianLife = 16;                     // тиков живёт деление
        public const float MeridianOuter = 0.97f;               // внешний конец деления, доля длины копья
        public const float MinorInner = 0.62f;                  // внутренний конец: обычное деление
        public const float MajorInner = 0.44f;                  // каждое 3-е (30°)
        public const float CardinalInner = 0.2f;                // каждое 9-е (90°)
        public const float LatitudeRadius = 1.05f;              // окружность-широта, доля длины копья
        public const float AxisInner = 0.1f;                    // ось начинается чуть от центра
        public const float UnderPad = 2.5f;                     // подложка шире штриха на столько px с каждой стороны
        public const float UnderAlpha = 0.6f;
        public const int StarLife = 26;
        public const float StarRay = 64f;                       // длина луча звезды, px
        public const int RingLife = 28;
        public const int RingDelay = 6;                         // второе кольцо позже на столько тиков
        public const float RingMax = 74f;                       // радиус кольца в конце, px
        public const int HitLife = 12;

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
                    return age > MeridianLife && (PeakTick < 0 || Now - PeakTick > Math.Max(StarLife, RingLife + RingDelay));
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

        private static void DrawSwing(Swing s, bool under)
        {
            Player p = Main.player[s.Owner];
            Vector2 c = p.MountedCenter;
            float r = s.Radius;
            if (r <= 1f)
                return;

            // меридианы-деления + засечки на широте
            float lat = r * LatitudeRadius;
            for (int i = 0; i < s.Marks.Count; i++)
            {
                Meridian m = s.Marks[i];
                float a = MarkAlpha(m.Born);
                if (a <= 0.02f)
                    continue;
                bool cardinal = m.Index % 9 == 0;
                bool major = m.Index % 3 == 0;
                float inner = cardinal ? CardinalInner : major ? MajorInner : MinorInner;
                float width = cardinal ? 2.2f : major ? 1.7f : 1.2f;
                float tick = cardinal ? 10f : major ? 6f : 3f;
                Vector2 dir = m.Angle.ToRotationVector2();
                // свежее деление на миг ярче — «щелчок» стрелки
                float flash = Now - m.Born < 3 ? 1f : 0f;
                Color col = Color.Lerp(Pale, Cold, cardinal ? 0.9f : major ? 0.6f : 0.25f + 0.6f * flash);
                Stroke(c + dir * r * inner, c + dir * r * MeridianOuter, width, col, a, under);
                Stroke(c + dir * lat, c + dir * (lat + tick), width, col, a, under);
            }

            // широта: дуга между соседними делениями, затем от последнего до оси
            for (int i = 1; i < s.Marks.Count; i++)
            {
                float a = Math.Min(MarkAlpha(s.Marks[i - 1].Born), MarkAlpha(s.Marks[i].Born));
                if (a > 0.02f)
                    Arc(c, lat, s.Marks[i - 1].Angle, s.Marks[i].Angle, 3, 1.2f, Pale, a * 0.9f, under);
            }
            if (s.Open && s.Marks.Count > 0)
                Arc(c, lat, s.Marks[s.Marks.Count - 1].Angle, s.Axis, 3, 1.2f, Pale, 0.9f, under);

            // ось-стрелка
            if (s.Open)
            {
                Vector2 dir = s.Axis.ToRotationVector2();
                Vector2 tip = c + dir * r;
                Stroke(c + dir * r * AxisInner, tip, 2.4f, Cold, 1f, under);
                if (!under)
                {
                    Texture2D glow = NorthPoleFx.Glow;
                    if (glow != null)
                    {
                        // мягкий холодный ореол вдоль стрелки и точка на острие
                        Vector2 mid = c + dir * r * 0.55f;
                        Main.spriteBatch.Draw(glow, mid - Main.screenPosition, null, Add(Pale, 0.35f), s.Axis,
                            glow.Size() / 2f, new Vector2(r * 0.95f / glow.Width, 10f / glow.Height), SpriteEffects.None, 0f);
                        Main.spriteBatch.Draw(glow, tip - Main.screenPosition, null, Add(Cold, 0.9f), 0f,
                            glow.Size() / 2f, 12f / glow.Width, SpriteEffects.None, 0f);
                    }
                }
            }

            if (s.PeakTick >= 0)
                DrawStar(c + s.PeakAngle.ToRotationVector2() * s.PeakRadius, Now - s.PeakTick, under);
        }

        // Полярная звезда: 4 длинных луча крестом + два расходящихся кольца
        private static void DrawStar(Vector2 pos, int age, bool under)
        {
            if (age < StarLife)
            {
                float env = age < 3 ? (age + 1) / 4f : 1f - (age - 3) / (float)(StarLife - 3);
                env = MathHelper.Clamp(env, 0f, 1f);
                float len = StarRay * (0.75f + 0.25f * env);
                for (int k = 0; k < 4; k++)
                {
                    Vector2 d = (k * MathHelper.PiOver2 - MathHelper.PiOver2).ToRotationVector2();
                    float l = k % 2 == 0 ? len : len * 0.72f;        // вертикальная ось длиннее — как на картах
                    Stroke(pos, pos + d * l, 1.6f, Cold, env, under);
                }
                if (!under)
                {
                    Texture2D glow = NorthPoleFx.Glow;
                    if (glow != null)
                    {
                        Vector2 o = glow.Size() / 2f;
                        Vector2 at = pos - Main.screenPosition;
                        // цветная «аура» лучей: мята по вертикали, фиолет по горизонтали
                        Main.spriteBatch.Draw(glow, at, null, Add(Mint, 0.8f * env), MathHelper.PiOver2, o,
                            new Vector2(len * 2f / glow.Width, 9f / glow.Height), SpriteEffects.None, 0f);
                        Main.spriteBatch.Draw(glow, at, null, Add(Violet, 0.8f * env), 0f, o,
                            new Vector2(len * 1.44f / glow.Width, 9f / glow.Height), SpriteEffects.None, 0f);
                        Main.spriteBatch.Draw(glow, at, null, Add(Violet, 0.45f * env), 0f, o,
                            30f * env / glow.Width, SpriteEffects.None, 0f);
                        Main.spriteBatch.Draw(glow, at, null, Add(Color.White, env), 0f, o,
                            13f / glow.Width, SpriteEffects.None, 0f);
                    }
                }
            }
            for (int ring = 0; ring < 2; ring++)
            {
                int ra = age - ring * RingDelay;
                if (ra < 0 || ra >= RingLife)
                    continue;
                float t = ra / (float)RingLife;
                float radius = 10f + RingMax * (1f - (1f - t) * (1f - t)) * (ring == 0 ? 1f : 0.7f);
                float a = (1f - t) * (ring == 0 ? 1f : 0.8f);
                Color col = Color.Lerp(Mint, Violet, ring == 0 ? t : 0.4f + 0.6f * t);
                Arc(pos, radius, 0f, MathHelper.TwoPi, 36, 1.5f, col, a, under);
            }
        }

        private static void DrawHit(Hit h, bool under)
        {
            int age = Now - h.Born;
            float a = 1f - age / (float)HitLife;
            if (a <= 0f)
                return;
            float len = 20f * (0.7f + 0.3f * a);
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
