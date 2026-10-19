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

        // Палитра по степени полировки клинка: P=0 (в инее) — бледный матовый
        // мороз, P=1 (отполирован) — яркое бело-голубое сияние. Дуга и тычок
        // берут цвета отсюда, поэтому эффект совпадает с состоянием клинка.
        public static void PolishColors(float polish, out Color core, out Color tail, out Color edge)
        {
            polish = MathHelper.Clamp(polish, 0f, 1f);
            core = Color.Lerp(new Color(160, 200, 225), new Color(255, 255, 255), polish);
            tail = Color.Lerp(new Color(85, 125, 160), new Color(110, 195, 255), polish);
            edge = Color.Lerp(new Color(115, 155, 185), new Color(175, 235, 255), polish);
        }

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

        // === Общая ледяная палитра серии (ядро/хвост/край) ===
        public static readonly Color SwooshCore = new Color(235, 250, 255);
        public static readonly Color SwooshTail = new Color(120, 210, 255);
        public static readonly Color SwooshEdge = new Color(40, 110, 230);

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
