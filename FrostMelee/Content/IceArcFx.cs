using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace FrostMelee.Content
{
    // Общий ледяной вид дуги взмаха (серп и мечи рисуют одну текстуру Excalibur).
    // 1) Линия дуги в два прохода: глубокий голубой обод чуть шире + светлое ядро
    //    с медленным «дыханием» яркости — лёд читается сразу, без shaders.
    // 2) Мерцающие кристаллики вдоль линии: звёзды из ThePerfectGlow, от хвоста
    //    (глубже, мельче) к голове (белее, крупнее). Голова — со стороны движения.
    // Только визуал: хитбоксы дуг не трогаются.
    public static class IceArcFx
    {
        // Геометрия линии дуги Excalibur: хвост -74°, голова +56° от оси (как в хитбоксе)
        public static readonly float TailAngle = MathHelper.ToRadians(74f);
        public static readonly float HeadAngle = MathHelper.ToRadians(56f);

        // Ледяные цвета: обод — глубокий голубой, ядро — почти белый лёд
        private static readonly Color Rim = new Color(70, 150, 255);
        private static readonly Color Core = new Color(225, 245, 255);

        private const int Sparkles = 5;      // кристалликов вдоль линии
        private const float SparkleSpeed = 5f; // скорость мерцания

        // Линия дуги: обод (масштаб ×1.06 — halo вокруг ядра) + ядро с дыханием.
        // rimOverride/coreOverride — цвета другой стихии (ледяной огонь Frostbrand)
        public static void DrawLine(Texture2D texture, Rectangle frame, Vector2 position, float rotation,
            Vector2 origin, float scale, SpriteEffects fx, float alpha, Color? rimOverride = null, Color? coreOverride = null)
        {
            Color rim = rimOverride ?? Rim;
            Color core = coreOverride ?? Core;
            float breathe = 0.8f + 0.2f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 5f);
            Main.EntitySpriteDraw(texture, position, frame, rim * alpha, rotation, origin, scale * 1.06f, fx, 0f);
            Main.EntitySpriteDraw(texture, position, frame, core * (alpha * breathe), rotation, origin, scale, fx, 0f);
        }

        // Кристаллики вдоль линии. center — точка-вершина клина (центр игрока),
        // rotation — текущий угол текстуры, dir — сторона (ai[0] >= 0 ? 1 : -1),
        // radius — мировой радиус линии, ticks — тики от спавна (для фазы мерцания).
        // rimOverride/coreOverride — цвета другой стихии (ледяной огонь Frostbrand)
        public static void DrawSparkles(Vector2 center, float rotation, int dir, float radius, float ticks, float alpha,
            Color? rimOverride = null, Color? coreOverride = null)
        {
            Color rim = rimOverride ?? Rim;
            Color core = coreOverride ?? Core;
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow == null || glow.IsDisposed)
                return;
            float tail = rotation - dir * TailAngle;
            float head = rotation + dir * HeadAngle;
            Vector2 origin = glow.Size() / 2f;
            for (int i = 0; i < Sparkles; i++)
            {
                float t = (i + 0.35f) / Sparkles;   // 0 — хвост, 1 — голова
                float tw = 0.5f + 0.5f * (float)Math.Sin(ticks * SparkleSpeed + i * 1.7f);
                if (tw < 0.3f)
                    continue;
                float ang = MathHelper.Lerp(tail, head, t);
                Vector2 pos = center + ang.ToRotationVector2() * radius;
                float size = (7f + 9f * t) * tw;
                Color c = Color.Lerp(rim, core, 0.2f + 0.8f * t);
                float rot = ang + MathHelper.PiOver4 + ticks * 0.4f;
                for (int r = 0; r < 2; r++)
                {
                    float rr = rot + MathHelper.PiOver2 * r;
                    Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, c * (alpha * tw), rr, origin,
                        new Vector2(size / glow.Width, size * 0.16f / glow.Height), SpriteEffects.None, 0f);
                }
                Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, core * (alpha * tw), 0f, origin,
                    size * 0.3f / glow.Width, SpriteEffects.None, 0f);
            }
        }

        // Угол линии дуги для приёмов: голова со стороны движения (dir), хвост — другой край
        public static float HeadAngleOf(float rotation, int dir) => rotation + dir * HeadAngle;
        public static float TailAngleOf(float rotation, int dir) => rotation - dir * TailAngle;

        // Языки ледяного огня вдоль линии дуги (Frostbrand): крупные мерцающие
        // пятна холодного пламени поверх линии, к голове крупнее и ярче.
        // Вызывать при ОТКРЫТОМ spriteBatch; center — вершина клина, radius — мировой радиус линии.
        public static void DrawFlameSpots(Vector2 center, float rotation, int dir, float radius, float ticks, float alpha)
        {
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow == null || glow.IsDisposed || alpha <= 0.01f || radius <= 1f)
                return;
            const int Count = 7;
            Vector2 origin = glow.Size() / 2f;
            for (int i = 0; i < Count; i++)
            {
                float t = i / (float)(Count - 1);   // 0 — хвост, 1 — голова
                float flicker = 0.55f + 0.45f * (float)Math.Sin(ticks * 6f + i * 2.6f);
                if (flicker < 0.2f)
                    continue;
                float ang = MathHelper.Lerp(TailAngleOf(rotation, dir), HeadAngleOf(rotation, dir), t);
                // пламя дышит чуть выше линии
                float lift = 1f + 0.02f * (float)Math.Sin(ticks * 4f + i * 1.3f);
                Vector2 pos = center + ang.ToRotationVector2() * radius * lift;
                float size = (16f + 16f * t) * flicker;
                Color edge = Additive(FireEdge, 0.5f * alpha * flicker);
                Color core = Additive(FireLineCore, 0.8f * alpha * flicker);
                Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, edge, ang, origin,
                    new Vector2(size * 1.4f / glow.Width, size * 0.8f / glow.Height), SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, core, ang + MathHelper.PiOver4, origin,
                    size * 0.45f / glow.Width, SpriteEffects.None, 0f);
            }
        }

        // === Общая ледяная палитра серии (ядро/хвост/край) ===
        public static readonly Color SwooshCore = new Color(235, 250, 255);
        public static readonly Color SwooshTail = new Color(120, 210, 255);
        public static readonly Color SwooshEdge = new Color(40, 110, 230);

        // === Ледяной огонь (Frostbrand): холодное пламя Frostburn ===
        // Палитра от льда ОТЛИЧАЕТСЯ: льду нужен бирюзово-зелёный, огню —
        // голубо-лиловый (ванильный цвет эффекта Frostburn) с белым жаром.
        // Линия: сиренево-голубой обод + почти белое ядро.
        public static readonly Color FireLineRim = new Color(140, 170, 255);
        public static readonly Color FireLineCore = new Color(245, 248, 255);
        public static readonly Color FireCore = new Color(250, 252, 255);
        public static readonly Color FireTail = new Color(170, 205, 255);
        public static readonly Color FireEdge = new Color(90, 130, 255);

        // Ледяная дымка: полупрозрачный синий, который ВИДЕН и на светлом фоне.
        // Аддитивное свечение на снегу/небе не даёт ничего (белое + светлое = белое),
        // поэтому у пламени есть нижний слой-вуаль: он подкрашивает то, что за ним.
        // Альфа уходит в RGB и в A — обычный premultiplied AlphaBlend.
        public static Color Veil(Color c, float alpha)
        {
            return c * Math.Clamp(alpha, 0f, 1f);
        }

        // Один язык пламени: изогнутая СУЖАЮЩАЯСЯ лента из нескольких мазков.
        // Три прохода: вуаль (видна на светлом фоне) → обод → раскалённое ядро.
        // Тонкая прямая линия читается как волосок или царапина, поэтому язык
        // обязательно гнётся (bend) и сходит на нет к концу (width → 0).
        // veil может быть Color.Transparent — тогда проход пропускается.
        public static void DrawTongue(Texture2D glow, Vector2 origin, Vector2 dir, float bend,
            float length, float width, Color veil, Color rim, Color core)
        {
            if (length < 3f || width < 0.6f)
                return;
            const int Steps = 5;
            Vector2 p = origin;
            Vector2 d = dir.SafeNormalize(Vector2.UnitX);
            for (int k = 0; k < Steps; k++)
            {
                float t1 = (k + 1) / (float)Steps;
                d = d.RotatedBy(bend / Steps);
                Vector2 q = p + d * (length / Steps);
                float w0 = Math.Max(0.5f, width * (1f - k / (float)Steps * 0.9f));
                float w1 = Math.Max(0.4f, width * (1f - t1 * 0.9f));
                if (veil.A > 0)
                    DrawSegment(glow, p, q, w0 * 1.15f, w1 * 1.15f, veil);
                DrawSegment(glow, p, q, w0, w1, rim);
                DrawSegment(glow, p, q, w0 * 0.55f, w1 * 0.55f, core);
                p = q;
            }
        }

        // Ледяной огонь Frostbrand НА клинке — передний слой, поверх спрайта.
        // Раскалённая ЖИЛКА по оси (волнистая, сужается к острию) + два языка у
        // острия. Никаких прямых линий поперёк кромки: на 2× зуме они выглядели
        // начёсом из волосков, а не пламенем.
        // butt/tip — мировые координаты приклада и острия; intensity 0..1.
        public static void DrawBladeFireLine(Vector2 butt, Vector2 tip, float intensity)
        {
            if (intensity <= 0.02f)
                return;
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow == null || glow.IsDisposed)
                return;
            float len = Vector2.Distance(butt, tip);
            if (len < 8f)
                return;
            Vector2 dir = (tip - butt) / len;
            Vector2 normal = new Vector2(-dir.Y, dir.X);
            Vector2 root = Vector2.Lerp(butt, tip, 0.16f);   // рукоять не горит
            float time = Main.GlobalTimeWrappedHourly;

            // Сердцевина: три мазка с бегущей волной, к острию тоньше
            Vector2 prev = root;
            for (int k = 1; k <= 3; k++)
            {
                float u = k / 3f;
                float wave = (float)Math.Sin(time * 2.4f + u * 6f) * 1.1f * (float)Math.Sin(Math.PI * u);
                Vector2 at = Vector2.Lerp(root, tip, u) + normal * wave;
                float w0 = MathHelper.Lerp(3.4f, 1.2f, (k - 1) / 3f);
                float w1 = MathHelper.Lerp(3.4f, 1.2f, u);
                DrawSegment(glow, prev, at, w0, w1, Additive(FireLineRim, 0.15f * intensity));
                DrawSegment(glow, prev, at, w0 * 0.55f, w1 * 0.55f, Additive(FireLineCore, 0.20f * intensity));
                prev = at;
            }

            // Языки по кромке — три с каждой стороны, тянутся вперёд вдоль лезвия.
            // Широкие и сужающиеся: тонкие линии читались как волоски.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 3; i++)
                {
                    float u = 0.52f + 0.18f * i;               // 0.52 .. 0.88
                    float flick = 0.55f + 0.45f * (float)Math.Sin(time * 4.7f + i * 2.4f + side * 1.7f);
                    Vector2 at = Vector2.Lerp(root, tip, u) + normal * side * 3f;
                    Vector2 d = (dir * 0.72f + normal * side * 0.55f).SafeNormalize(dir);
                    DrawTongue(glow, at, d, side * 0.35f, (12f + 9f * u) * flick * intensity, 3.2f,
                        Color.Transparent,
                        Additive(FireEdge, 0.30f * intensity * flick),
                        Additive(FireCore, 0.36f * intensity * flick));
                }
            }

            // Острие: мягкая точка + короткий крест
            float pulse = 0.65f + 0.35f * (float)Math.Sin(time * 5.3f);
            DrawSparkle(glow, tip, 12f * pulse, dir.ToRotation(), 2,
                Additive(FireLineRim, 0.22f * intensity), Additive(FireCore, 0.35f * intensity * pulse));
        }

        // Аддитивный цвет: альфа уходит в яркость RGB (A = 0), альфа экрана не трогается
        public static Color Additive(Color c, float alpha)
        {
            Color r = c * Math.Clamp(alpha, 0f, 1f);
            r.A = 0;
            return r;
        }

        // Мазок вдоль отрезка a→b: растянутое мягкое пятно ThePerfectGlow (координаты мировые)
        public static void DrawSegment(Texture2D glow, Vector2 a, Vector2 b, float widthA, float widthB, Color color)
        {
            float width = (widthA + widthB) * 0.5f;
            if (width < 0.5f)
                return;
            Vector2 delta = b - a;
            Main.EntitySpriteDraw(glow, (a + b) * 0.5f - Main.screenPosition, null, color, delta.ToRotation(),
                glow.Size() / 2f, new Vector2((delta.Length() + width) / glow.Width, width / glow.Height),
                SpriteEffects.None, 0f);
        }

        // Звезда из rays лучей: тонкие растянутые пятна, повёрнутые равномерно
        public static void DrawSparkle(Texture2D glow, Vector2 pos, float size, float rotation, int rays, Color color, Color centerColor)
        {
            Vector2 origin = glow.Size() / 2f;
            for (int i = 0; i < rays; i++)
            {
                float rot = rotation + MathHelper.Pi * i / rays;
                Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, color, rot, origin,
                    new Vector2(size / glow.Width, size * 0.09f / glow.Height), SpriteEffects.None, 0f);
            }
            Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, centerColor, 0f, origin,
                size * 0.28f / glow.Width, SpriteEffects.None, 0f);
        }

        // Ледяная нова на пике взмаха: неровное кольцо льда вокруг игрока + иглы наружу.
        // Вызывать при ОТКРЫТОМ spriteBatch (отрисует себя поверх). center, radius — мировые.
        public static void DrawNovaRing(Texture2D glow, Vector2 center, float radius, float peak, float time)
        {
            if (glow == null || glow.IsDisposed || peak <= 0.03f || radius <= 1f)
                return;
            Color core = SwooshCore;
            Color tail = SwooshTail;
            const int Segments = 18;
            const int Spikes = 6;
            float spin = time * 0.35f;
            // кольцо: сегменты по окружности, мерцание делает лёд неровным
            Vector2 prev = center + spin.ToRotationVector2() * radius;
            for (int i = 1; i <= Segments; i++)
            {
                float ang = spin + MathHelper.TwoPi * i / Segments;
                Vector2 cur = center + ang.ToRotationVector2() * radius;
                float tw = 0.7f + 0.3f * (float)Math.Sin(i * 2.3f + time * 4f);
                float w = radius * 0.045f * tw;
                DrawSegment(glow, prev, cur, w, w,
                    Additive(Color.Lerp(core, tail, 0.2f + 0.3f * tw), peak * (0.45f + 0.55f * tw)));
                prev = cur;
            }
            // иглы: чередование длинных и коротких, противовращение кольцу
            for (int i = 0; i < Spikes; i++)
            {
                float ang = -spin * 1.4f + MathHelper.TwoPi * i / Spikes;
                Vector2 dir = ang.ToRotationVector2();
                float to = radius * 0.98f + radius * (i % 2 == 0 ? 0.3f : 0.18f);
                DrawSegment(glow, center + dir * (radius * 0.98f), center + dir * to,
                    radius * 0.05f, radius * 0.012f, Additive(Color.Lerp(tail, core, 0.5f), peak * 0.9f));
            }
            Main.EntitySpriteDraw(glow, center - Main.screenPosition, null, Additive(core, peak * 0.35f), 0f,
                glow.Size() / 2f, radius * 0.6f / glow.Width, SpriteEffects.None, 0f);
        }
    }
}
