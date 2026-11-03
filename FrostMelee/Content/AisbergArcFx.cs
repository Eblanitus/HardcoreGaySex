using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace FrostMelee.Content
{
    // Дуга Айсберга — «ледяная плита», нарисованная ПИКСЕЛЯМИ ТЕРРАРИИ.
    // Владелец (03.11.2026): гладкие полосы и кольца выглядят как векторная
    // графика, а не ваниль. Поэтому плита не рисуется полигонами: зона дуги
    // проходится по сетке мира с шагом 2 px (как пиксели спрайтов мода),
    // и каждая клетка — один квадрат 2x2 своего цвета.
    // края ступенчатые по сетке, прозрачность по длине плавная.
    //
    // Рисунок плиты — ячейки льда с белыми трещинами (Вороной, по длине
    // бесшовно), белая кромка снаружи, к центру светлый лёд и рваный
    // сколотый внутренний край. По граням бежит блик. Палитра — светлая,
    // со спрайтов глыбы и меча (тёмную владелец забраковал).
    // Вызывать при ОТКРЫТОМ spriteBatch (обычный AlphaBlend мира).
    public static class AisbergArcFx
    {
        // Цвета сняты со спрайтов глыбы (AisbergChunk.png) и меча (Iceberg.png)
        public static readonly Color Pale = new Color(173, 222, 242);
        public static readonly Color Sky = new Color(127, 181, 241);
        public static readonly Color Steel = new Color(91, 165, 207);
        public static readonly Color Shade = new Color(86, 125, 185);
        public static readonly Color Cyan = new Color(54, 232, 252);
        public static readonly Color Ice = new Color(220, 236, 247);
        public static readonly Color White = new Color(244, 250, 255);

        private const int Px = 2;                 // пиксель мира, px
        private const int TexW = 96;              // клеток рисунка вдоль (повтор)
        private const int TexH = 28;              // клеток поперёк
        private const int Cells = 18;             // ячеек льда на повтор
        private const float GlintSpeed = 0.06f;   // бег блика: доли длины за тик
        private const float GlintWidth = 0.14f;

        private static Color[] slab;
        private static readonly float[] rel = new float[64];
        private static readonly float[] arcLen = new float[64];

        public static void Unload() => slab = null;

        private static float Hash(int a, int b)
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ 0x9E3779B9u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
        }

        // Рисунок плиты (CPU): ячейки льда, светлые к краю, белые трещины,
        // рваный внутренний край (у ячейки своя глубина, глубже — пусто)
        private static Color[] MakeSlab()
        {
            Vector2[] seeds = new Vector2[Cells];
            float[] depth = new float[Cells];
            float[] shade = new float[Cells];
            for (int i = 0; i < Cells; i++)
            {
                seeds[i] = new Vector2((i + Hash(i, 1) * 0.8f) * TexW / Cells, Hash(i, 2) * TexH);
                depth[i] = 0.84f + 0.16f * Hash(i, 3);
                shade[i] = Hash(i, 4);
            }
            Color[] data = new Color[TexW * TexH];
            for (int y = 0; y < TexH; y++)
            {
                float v = (y + 0.5f) / TexH;
                for (int x = 0; x < TexW; x++)
                {
                    float best = float.MaxValue, second = float.MaxValue;
                    int cell = 0;
                    for (int i = 0; i < Cells; i++)
                    {
                        float dx = Math.Abs(x + 0.5f - seeds[i].X);
                        dx = Math.Min(dx, TexW - dx);
                        float dy = (y + 0.5f - seeds[i].Y) * 1.6f;
                        float d = dx * dx + dy * dy;
                        if (d < best)
                        {
                            second = best;
                            best = d;
                            cell = i;
                        }
                        else if (d < second)
                            second = d;
                    }
                    Color c;
                    if (v > depth[cell])
                        c = Color.Transparent;
                    else if (y == 0)
                        c = White;
                    else if (y == 1)
                        c = Ice;
                    else
                    {
                        float k = MathHelper.Clamp(v / depth[cell], 0f, 1f);
                        Color body = k < 0.4f ? Color.Lerp(Pale, Sky, k / 0.4f)
                                   : Color.Lerp(Sky, Steel, (k - 0.4f) / 0.6f);
                        body = Color.Lerp(body, Shade, 0.3f * shade[cell] * k);
                        bool seam = (float)Math.Sqrt(second) - (float)Math.Sqrt(best) < 1.1f;
                        bool rim = depth[cell] - v < 1.5f / TexH;
                        if (seam)
                            body = Color.Lerp(body, White, 0.75f - 0.3f * k);
                        else if (rim)
                            body = Color.Lerp(body, Cyan, 0.5f);
                        c = body;
                    }
                    data[y * TexW + x] = c;
                }
            }
            return data;
        }

        // Форма (angles, rOut, rIn) — общий движок дуги мечей (BuildBladeShape в
        // SwordArcProjectile, тот же, что у Фростбранда): путь острия от головы
        // (0) к хвосту вокруг center (кисть), внешний и внутренний край. Здесь
        // только рисунок плиты внутри этой формы.
        // env — огибающая взмаха, time — тики взмаха, seed — сдвиг рисунка
        public static void Draw(float[] angles, float[] rOut, float[] rIn, int n, Vector2 center, float env, float time, int seed)
        {
            if (n < 2 || env <= 0.02f || n > rel.Length)
                return;
            slab ??= MakeSlab();
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            if (pixel == null || pixel.IsDisposed)
                return;

            // Путь: угол от головы (растёт к хвосту) и длина по внешнему краю
            float sign = MathHelper.WrapAngle(angles[n - 1] - angles[0]) >= 0f ? 1f : -1f;
            rel[0] = 0f;
            arcLen[0] = 0f;
            float maxR = rOut[0];
            for (int i = 1; i < n; i++)
            {
                rel[i] = Math.Max(rel[i - 1], (angles[i] - angles[0]) * sign);
                arcLen[i] = arcLen[i - 1] + Vector2.Distance(angles[i - 1].ToRotationVector2() * rOut[i - 1],
                    angles[i].ToRotationVector2() * rOut[i]);
                maxR = Math.Max(maxR, rOut[i]);
            }
            float span = rel[n - 1];
            if (span < 0.01f)
                return;

            float u0 = Hash(seed, 9) * TexW;
            float glint = (time * GlintSpeed) % 1.4f - 0.2f;
            Rectangle one = new Rectangle(0, 0, 1, 1);
            Vector2 size = new Vector2(Px);

            // Клетки мира в квадрате вокруг кисти радиусом maxR, по сетке 2 px
            int x0 = (int)Math.Floor((center.X - maxR - Px) / Px), x1 = (int)Math.Ceiling((center.X + maxR + Px) / Px);
            int y0 = (int)Math.Floor((center.Y - maxR - Px) / Px), y1 = (int)Math.Ceiling((center.Y + maxR + Px) / Px);
            for (int gy = y0; gy <= y1; gy++)
            {
                for (int gx = x0; gx <= x1; gx++)
                {
                    Vector2 p = new Vector2(gx * Px + Px * 0.5f, gy * Px + Px * 0.5f) - center;
                    float r = p.Length();
                    if (r > maxR + 1f || r < 3f)
                        continue;
                    float d = MathHelper.WrapAngle((float)Math.Atan2(p.Y, p.X) - angles[0]) * sign;
                    if (d < 0f || d > span)
                        continue;
                    // доля пути t и радиус края в этой точке
                    int i = 1;
                    while (i < n - 1 && rel[i] < d)
                        i++;
                    float seg = Math.Max(1e-4f, rel[i] - rel[i - 1]);
                    float k = MathHelper.Clamp((d - rel[i - 1]) / seg, 0f, 1f);
                    float t = ((i - 1) + k) / (n - 1);
                    float ro = MathHelper.Lerp(rOut[i - 1], rOut[i], k);
                    float ri = MathHelper.Lerp(rIn[i - 1], rIn[i], k);
                    float width = Math.Max(1f, ro - ri);
                    if (r > ro || r < ro - width)
                        continue;

                    float v = (ro - r) / width;
                    float along = MathHelper.Lerp(arcLen[i - 1], arcLen[i], k) / Px + u0;
                    int tx = ((int)along % TexW + TexW) % TexW;
                    int ty = Math.Min(TexH - 1, (int)(v * TexH));
                    Color c = slab[ty * TexW + tx];
                    if (c.A == 0)
                        continue;

                    // прозрачность плавно: плотно у головы, тает к хвосту. Ступени
                    // по длине давали радиальные границы — дуга читалась секторами
                    float fade = env * (float)Math.Pow(1f - t, 1.4f);
                    if (fade < 0.03f)
                        continue;
                    // самый край у головы — белый
                    if (ty <= 1 && t < 0.2f)
                        c = White;
                    // блик бежит по граням: клетки в полосе блика светлее
                    if (Math.Abs(t - glint) < GlintWidth * 0.5f)
                        c = Color.Lerp(c, White, 0.5f);
                    Main.spriteBatch.Draw(pixel, new Vector2(gx * Px, gy * Px) - Main.screenPosition, one,
                        c * fade, 0f, Vector2.Zero, size, SpriteEffects.None, 0f);
                }
            }
        }

        // Ледяной шип за остриём (тычок и встреча, AisbergReach) — продолжение
        // клинка: тот же пиксель 2x2, те же цвета и то же сечение, что у
        // кончика Iceberg.png (снято с его пикселей): с одной стороны тёмная
        // бирюзовая кромка, бирюза, голубое ядро, белый блик, с другой синяя
        // кромка; самый кончик синий, как у спрайта. Начинается внутри кончика
        // клинка той же ширины, чуть раздаётся гранью и сходится в остриё.
        // Освещение мира учитывается, как у спрайта меча.
        // Хитбокс шипа — отрезок basePos → basePos + dir * length толщиной
        // SpikeMaxWidth: ровно этот рисунок.
        private static readonly Color BladeDark = new Color(2, 95, 115);
        private static readonly Color BladeTeal = new Color(3, 173, 192);
        private static readonly Color BladeBlue = new Color(23, 94, 175);
        private static readonly Color BladeWhite = new Color(244, 241, 255);

        public const float SpikeBaseHalf = 4.5f;   // полуширина у основания: кончик клинка Iceberg.png (~9 px)
        public const float SpikeBulge = 1.2f;      // раздача грани к SpikeBulgeAt длины
        public const float SpikeBulgeAt = 0.2f;
        public static float SpikeMaxWidth => SpikeBaseHalf * SpikeBulge * 2f;

        public static float SpikeHalfWidth(float t)
        {
            if (t <= SpikeBulgeAt)
                return SpikeBaseHalf * MathHelper.Lerp(1f, SpikeBulge, (float)Math.Sqrt(t / SpikeBulgeAt));
            float k = (t - SpikeBulgeAt) / (1f - SpikeBulgeAt);
            return SpikeBaseHalf * SpikeBulge * (float)Math.Pow(1f - k, 0.75f);
        }

        // flipped — спрайт меча зеркален (игрок смотрит влево): белый блик
        // клинка тогда по другую сторону оси
        public static void DrawSpike(Vector2 basePos, Vector2 dir, float length, float alpha, bool flipped)
        {
            if (Main.dedServ || length < 2f || alpha <= 0.02f)
                return;
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            if (pixel == null || pixel.IsDisposed)
                return;
            dir = dir.SafeNormalize(Vector2.UnitX);
            Vector2 normal = new Vector2(-dir.Y, dir.X);
            float whiteSide = flipped ? -1f : 1f;
            Rectangle one = new Rectangle(0, 0, 1, 1);
            Vector2 size = new Vector2(Px);

            Vector2 tip = basePos + dir * length;
            float half = SpikeMaxWidth * 0.5f + Px;
            int x0 = (int)Math.Floor((Math.Min(basePos.X, tip.X) - half) / Px), x1 = (int)Math.Ceiling((Math.Max(basePos.X, tip.X) + half) / Px);
            int y0 = (int)Math.Floor((Math.Min(basePos.Y, tip.Y) - half) / Px), y1 = (int)Math.Ceiling((Math.Max(basePos.Y, tip.Y) + half) / Px);
            for (int gy = y0; gy <= y1; gy++)
            {
                for (int gx = x0; gx <= x1; gx++)
                {
                    Vector2 p = new Vector2(gx * Px + Px * 0.5f, gy * Px + Px * 0.5f) - basePos;
                    float a = Vector2.Dot(p, dir);
                    if (a < 0f || a > length)
                        continue;
                    float t = a / length;
                    float halfW = Math.Max(SpikeHalfWidth(t), Px * 0.5f);
                    float c = Vector2.Dot(p, normal) * whiteSide;   // > 0 — сторона белого блика
                    if (Math.Abs(c) > halfW)
                        continue;
                    float u = c / halfW;                              // -1 тёмная кромка … +1 синяя кромка
                    Color col;
                    if (halfW - Math.Abs(c) < Px && halfW > Px * 1.5f)
                        col = c > 0f ? BladeBlue : BladeDark;         // кромки
                    else if (u < -0.3f)
                        col = BladeTeal;
                    else if (u < 0.35f)
                        col = Cyan;                                   // ядро
                    else
                        col = BladeWhite;                             // блик
                    if (t > 0.88f)
                        col = Math.Abs(u) < 0.5f ? BladeTeal : BladeBlue;   // кончик, как у спрайта
                    Vector2 world = basePos + p;
                    col = col.MultiplyRGB(Lighting.GetColor((int)(world.X / 16f), (int)(world.Y / 16f)));
                    // у клинка плотный (стык не виден), к кончику чуть прозрачнее — лёд
                    float fade = alpha * (1f - 0.3f * t * t);
                    // EntitySpriteDraw: шип рисуется и из слоя игрока (heldProj)
                    Main.EntitySpriteDraw(pixel, new Vector2(gx * Px, gy * Px) - Main.screenPosition, one,
                        col * fade, 0f, Vector2.Zero, size, SpriteEffects.None, 0f);
                }
            }
        }

        // Пик взмаха: от толщи плиты у головы отваливаются куски льда —
        // крупная ванильная крошка, падает вниз
        public static void PeakCrumble(Vector2 center, float angle, float radius)
        {
            if (Main.dedServ)
                return;
            Vector2 dir = angle.ToRotationVector2();
            for (int i = 0; i < 3; i++)
            {
                Vector2 at = center + dir * radius * Main.rand.NextFloat(0.5f, 0.85f);
                Dust d = Dust.NewDustPerfect(at, DustID.Ice, new Vector2(Main.rand.NextFloat(-1f, 1f), Main.rand.NextFloat(0.5f, 2f)),
                    0, default, Main.rand.NextFloat(1.2f, 1.5f));
                d.noGravity = false;
            }
        }

        public static void Light(Vector2 center, float angle, float radius, float env)
        {
            Vector2 dir = angle.ToRotationVector2();
            Lighting.AddLight(center + dir * radius, 0.2f * env, 0.7f * env, 0.85f * env);
            Lighting.AddLight(center + dir * radius * 0.6f, 0.05f * env, 0.3f * env, 0.4f * env);
        }
    }
}
