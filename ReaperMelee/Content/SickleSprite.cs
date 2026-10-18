using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace ReaperMelee.Content
{
    // Геометрия серпа по реальным пикселям спрайта (приклад, острие, длина, угол).
    // ВАЖНО: у серпов (в отличие от палок WoodSwords) острие смотрит ВНИЗ-ВЛЕВО,
    // приклад — ВВЕРХ-ВПРАВО. Поэтому измеряем по диагонали «приклад = максимум
    // x-y, острие = минимум x-y» — инвертированная логика StickSprite.
    // Замер sprite_tip_angle: DeathSpear 90x90 (острие 137°).
    public static class SickleSprite
    {
        public readonly struct Geometry
        {
            public readonly Vector2 Butt;   // приклад в пикселях текстуры (центр пикселя)
            public readonly Vector2 Tip;    // острие в пикселях текстуры
            public readonly int Width;
            // Толщина по обе стороны от оси приклад → острие (px).
            // SidePos — в сторону нормали (-dir.Y, dir.X), SideNeg — противоположная.
            public readonly float SidePos;
            public readonly float SideNeg;
            // Butt и Tip — центры крайних 22% спрайта, сам рисунок торчит дальше:
            // древко за Butt на RearOverhang px, лезвие за Tip на FrontOverhang px (вдоль оси).
            public readonly float RearOverhang;
            public readonly float FrontOverhang;

            public Geometry(Vector2 butt, Vector2 tip, int width, float sidePos = 3f, float sideNeg = 3f,
                float rearOverhang = 0f, float frontOverhang = 0f)
            {
                Butt = butt;
                Tip = tip;
                Width = width;
                SidePos = sidePos;
                SideNeg = sideNeg;
                RearOverhang = rearOverhang;
                FrontOverhang = frontOverhang;
            }

            public float Length => Vector2.Distance(Butt, Tip);

            // От середины оси приклад→острие до самой дальней точки рисунка вперёд / назад (px)
            public float FrontReach => Length * 0.5f + FrontOverhang;
            public float RearReach => Length * 0.5f + RearOverhang;

            // Приклад/острие с учётом зеркалирования (FlipHorizontally зеркалит
            // картинку внутри прямоугольника, а origin — нет)
            public Vector2 ButtFor(bool flipped) => flipped ? new Vector2(Width - Butt.X, Butt.Y) : Butt;
            public Vector2 TipFor(bool flipped) => flipped ? new Vector2(Width - Tip.X, Tip.Y) : Tip;

            // Угол вектора приклад → острие в спрайте
            public float AngleFor(bool flipped) => (TipFor(flipped) - ButtFor(flipped)).ToRotation();
        }

        // Где серп держит кисть. Player.GetFrontHandPosition отдаёт точку примерно
        // на GripRaise px НИЖЕ нарисованной кисти — поднимаем хват до неё.
        public const float GripRaise = 3f;

        public static Vector2 HandGrip(Player player, Player.CompositeArmStretchAmount stretch, float armRot) =>
            player.GetFrontHandPosition(stretch, armRot) - new Vector2(0f, GripRaise * player.gravDir);

        private static readonly Dictionary<int, Geometry> cache = new();
        private static readonly Dictionary<int, Geometry> inHandCache = new();
        private static readonly Dictionary<int, Texture2D> inHandTex = new();

        public static void ClearCache()
        {
            cache.Clear();
            inHandCache.Clear();
            inHandTex.Clear();
            zoneCache.Clear();
        }

        // Геометрия ИНВЕНТАРНОГО спрайта (то, что в TextureAssets.Item[type]).
        public static Geometry Get(int itemType)
        {
            if (Main.dedServ || itemType <= 0)
                return Fallback;
            if (cache.TryGetValue(itemType, out Geometry cached))
                return cached;
            Main.instance.LoadItem(itemType);
            Asset<Texture2D> asset = SickleTextures.Held(itemType) ?? TextureAssets.Item[itemType];
            if (asset == null || !asset.IsLoaded)
                return Fallback;
            Texture2D texture = asset.Value;
            if (texture == null || texture.IsDisposed)
                return Fallback;
            Geometry geometry = Measure(texture);
            cache[itemType] = geometry;
            return geometry;
        }

        // Путь к ДЛИННОМУ спрайту «в руке» (боевой). null — если предмет не серп.
        public static string InHandTexturePath(int itemType)
        {
            if (itemType == ItemID.DeathSickle) return "ReaperMelee/Items/DeathSpearProjectile";
            return null;
        }

        // Длинная текстура «в руке» (кэш). null — если не серп / не клиент.
        public static Texture2D InHandTexture(int itemType)
        {
            if (Main.dedServ)
                return null;
            if (inHandTex.TryGetValue(itemType, out Texture2D t))
                return t;
            string path = InHandTexturePath(itemType);
            if (path == null)
                return null;
            Texture2D tex = ModContent.Request<Texture2D>(path).Value;
            inHandTex[itemType] = tex;
            return tex;
        }

        // Геометрия ДЛИННОГО спрайта «в руке» (отдельный кэш — измеряем другой файл).
        public static Geometry GetInHand(int itemType)
        {
            if (Main.dedServ || itemType <= 0)
                return Fallback;
            if (inHandCache.TryGetValue(itemType, out Geometry cached))
                return cached;
            Texture2D texture = InHandTexture(itemType);
            if (texture == null || texture.IsDisposed)
                return Get(itemType); // запас: инвентарная геометрия
            Geometry geometry = Measure(texture);
            inHandCache[itemType] = geometry;
            return geometry;
        }

        // =====================================================================
        // ЗОНА УДАРА: лезвие серпа по пикселям спрайта
        // =====================================================================
        // Урон наносит то, что видно: лезвие (голова серпа: у серпа смерти оба
        // лезвия и крюк), а не отрезок оси и не прямоугольник. Зона снимается с
        // того же рисунка, что рисуют все приёмы (TextureAssets.Item), и описана в
        // полярных координатах вокруг СЕРЕДИНЫ оси приклад→острие — это точка, где
        // стоит Projectile.Center у тычка/копья/броска и центр вращения у взмаха.
        // Одна и та же зона годится для неподвижной позы и для вращения: при вращении
        // зону просто «заметают» от прошлого угла к текущему (нет туннелирования).

        // true — бьёт и древко (вся картинка). По умолчанию только лезвие.
        public static bool ShaftHits = false;

        // Запас на видимый край (px): пиксель — квадрат, а в зоне хранятся центры
        public const float HitPad = 0.7f;

        private const float ZoneBinR = 3f;         // ширина радиальной корзины (px)
        private const float ZoneGapPx = 2.5f;      // дырки уже этого внутри лезвия заливаем
        private const float ZoneHeadThickness = 9f; // толще — уже лезвие, тоньше — древко
        private const float MaxSectorSpan = 2.6f;  // шире (~150°) режем пополам: клин должен быть выпуклым

        // Один кусок лезвия: кольцо R0..R1 от середины оси и угол A0..A1 от оси
        // (для НЕзеркального спрайта; угол растёт в сторону нормали (-dir.Y, dir.X))
        public readonly struct BladeSector
        {
            public readonly float R0, R1, A0, A1;

            public BladeSector(float r0, float r1, float a0, float a1)
            {
                R0 = r0;
                R1 = r1;
                A0 = a0;
                A1 = a1;
            }
        }

        public sealed class BladeZone
        {
            public BladeSector[] Sectors;
            public float RMin;   // ближняя точка лезвия к середине оси (масштаб 1)
            public float RMax;   // дальняя точка лезвия — реальное острие (масштаб 1)
        }

        private static readonly Dictionary<int, BladeZone> zoneCache = new();

        // Зона лезвия предмета. null — не клиент / нет рисунка (звать с запасным хитбоксом).
        public static BladeZone GetZone(int itemType)
        {
            if (Main.dedServ || itemType <= 0)
                return null;
            if (zoneCache.TryGetValue(itemType, out BladeZone cached))
                return cached;
            Geometry geometry = Get(itemType);
            Asset<Texture2D> asset = SickleTextures.Held(itemType) ?? TextureAssets.Item[itemType];
            if (asset == null || !asset.IsLoaded)
                return null;
            Texture2D texture = asset.Value;
            if (texture == null || texture.IsDisposed)
                return null;
            BladeZone zone = BuildZone(texture, geometry, ZoneHeadThickness);
            zoneCache[itemType] = zone;
            return zone;
        }

        private static BladeZone BuildZone(Texture2D texture, Geometry geo, float headThickness)
        {
            int w = texture.Width, h = texture.Height;
            Color[] data = new Color[w * h];
            texture.GetData(data);

            Vector2 axis = geo.Tip - geo.Butt;
            float len = axis.Length();
            axis = len < 1f ? Vector2.UnitX : axis / len;
            Vector2 normal = new Vector2(-axis.Y, axis.X);
            float half = geo.Length * 0.5f;

            // Все пиксели в осях «вдоль серпа / в сторону» от приклада
            var along = new List<float>();
            var side = new List<float>();
            float maxAlong = 0f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (data[y * w + x].A <= 128)
                        continue;
                    Vector2 rel = new Vector2(x + 0.5f, y + 0.5f) - geo.Butt;
                    float a = Vector2.Dot(rel, axis);
                    along.Add(a);
                    side.Add(Vector2.Dot(rel, normal));
                    maxAlong = Math.Max(maxAlong, a);
                }
            }

            // Где кончается древко: с середины оси идём к острию, пока ширина узкая
            float headStart = float.NegativeInfinity;
            if (!ShaftHits)
            {
                for (float start = half; start < maxAlong && float.IsNegativeInfinity(headStart); start += 2f)
                {
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int i = 0; i < along.Count; i++)
                    {
                        if (along[i] < start || along[i] >= start + 2f)
                            continue;
                        lo = Math.Min(lo, side[i]);
                        hi = Math.Max(hi, side[i]);
                    }
                    if (hi - lo >= headThickness)
                        headStart = start - 1f;
                }
            }

            // Полярные координаты пикселей лезвия вокруг середины оси
            var radii = new List<float>();
            var angles = new List<float>();
            float rMin = float.MaxValue, rMax = 0f;
            for (int i = 0; i < along.Count; i++)
            {
                if (along[i] < headStart)
                    continue;
                float px = along[i] - half;
                float py = side[i];
                float r = (float)Math.Sqrt(px * px + py * py);
                radii.Add(r);
                angles.Add((float)Math.Atan2(py, px));
                rMin = Math.Min(rMin, r);
                rMax = Math.Max(rMax, r);
            }

            var sectors = new List<BladeSector>();
            if (radii.Count > 0)
            {
                int binCount = (int)(rMax / ZoneBinR) + 1;
                var bins = new List<float>[binCount];
                for (int i = 0; i < radii.Count; i++)
                {
                    int b = Math.Min(binCount - 1, (int)(radii[i] / ZoneBinR));
                    (bins[b] ??= new List<float>()).Add(angles[i]);
                }
                for (int b = 0; b < binCount; b++)
                {
                    if (bins[b] == null)
                        continue;
                    bins[b].Sort();
                    float gap = ZoneGapPx / Math.Max(1f, (b + 0.5f) * ZoneBinR);
                    float runStart = bins[b][0], prev = runStart;
                    for (int i = 1; i < bins[b].Count; i++)
                    {
                        float a = bins[b][i];
                        if (a - prev > gap)
                        {
                            sectors.Add(new BladeSector(b * ZoneBinR, (b + 1) * ZoneBinR, runStart, prev));
                            runStart = a;
                        }
                        prev = a;
                    }
                    sectors.Add(new BladeSector(b * ZoneBinR, (b + 1) * ZoneBinR, runStart, prev));
                }
            }

            return new BladeZone
            {
                Sectors = sectors.ToArray(),
                RMin = radii.Count > 0 ? rMin : 0f,
                RMax = rMax
            };
        }

        private static readonly Vector2[] clipA = new Vector2[16];
        private static readonly Vector2[] clipB = new Vector2[16];

        // Пересекает ли зона лезвия прямоугольник цели.
        //  center   — середина оси приклад→острие в мире
        //  angleA/B — угол оси (приклад→острие) в прошлом и текущем кадре: лезвие заметает
        //             всё между ними. Неподвижная поза — одинаковые углы.
        //  mirrored — спрайт нарисован зеркально (FlipHorizontally)
        //  scale    — множитель размера рисунка
        // Точная проверка «кольцевой сектор × прямоугольник»: прямоугольник обрезается
        // клином, потом сравниваются радиусы. Выборки точек нет.
        public static bool BladeOverlaps(BladeZone zone, Rectangle target, Vector2 center,
            float angleA, float angleB, bool mirrored, float scale)
        {
            if (zone == null || zone.Sectors.Length == 0)
                return false;

            // Грубый отсев: ближняя точка цели дальше острия — выходим сразу
            float nearX = MathHelper.Clamp(center.X, target.Left, target.Right) - center.X;
            float nearY = MathHelper.Clamp(center.Y, target.Top, target.Bottom) - center.Y;
            float reach = zone.RMax * scale + HitPad;
            if (nearX * nearX + nearY * nearY > reach * reach)
                return false;

            float angleLo = Math.Min(angleA, angleB);
            float angleHi = Math.Max(angleA, angleB);
            for (int i = 0; i < zone.Sectors.Length; i++)
            {
                BladeSector s = zone.Sectors[i];
                // Зеркало отражает угол относительно оси
                float a0 = mirrored ? -s.A1 : s.A0;
                float a1 = mirrored ? -s.A0 : s.A1;
                float mid = Math.Max(1f, (s.R0 + s.R1) * 0.5f * scale);
                float padAngle = Math.Min(HitPad / mid, 0.25f);
                if (SectorHitsRect(target, center, angleLo + a0 - padAngle, angleHi + a1 + padAngle,
                    s.R0 * scale - HitPad, s.R1 * scale + HitPad))
                    return true;
            }
            return false;
        }

        private static bool SectorHitsRect(Rectangle rect, Vector2 center, float lo, float hi, float r0, float r1)
        {
            if (hi - lo > MaxSectorSpan)
            {
                float mid = (lo + hi) * 0.5f;
                return SectorHitsRect(rect, center, lo, mid, r0, r1) ||
                    SectorHitsRect(rect, center, mid, hi, r0, r1);
            }
            r0 = Math.Max(0f, r0);

            clipA[0] = new Vector2(rect.Left, rect.Top) - center;
            clipA[1] = new Vector2(rect.Right, rect.Top) - center;
            clipA[2] = new Vector2(rect.Right, rect.Bottom) - center;
            clipA[3] = new Vector2(rect.Left, rect.Bottom) - center;

            Vector2 startDir = lo.ToRotationVector2();
            Vector2 endDir = hi.ToRotationVector2();
            int count = ClipHalfPlane(clipA, 4, clipB, new Vector2(-startDir.Y, startDir.X));
            if (count == 0)
                return false;
            count = ClipHalfPlane(clipB, count, clipA, new Vector2(endDir.Y, -endDir.X));
            if (count == 0)
                return false;

            // Расстояния от центра до обрезанного многоугольника: ближняя точка и дальняя вершина
            float nearestSq = float.MaxValue, farthestSq = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector2 a = clipA[i];
                Vector2 edge = clipA[(i + 1) % count] - a;
                float lengthSq = edge.LengthSquared();
                float t = lengthSq > 0f ? MathHelper.Clamp(-Vector2.Dot(a, edge) / lengthSq, 0f, 1f) : 0f;
                nearestSq = Math.Min(nearestSq, (a + edge * t).LengthSquared());
                farthestSq = Math.Max(farthestSq, a.LengthSquared());
            }
            return nearestSq <= r1 * r1 && farthestSq >= r0 * r0;
        }

        private static int ClipHalfPlane(Vector2[] input, int count, Vector2[] output, Vector2 normal)
        {
            int outCount = 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 a = input[i];
                Vector2 b = input[(i + 1) % count];
                float da = Vector2.Dot(a, normal);
                float db = Vector2.Dot(b, normal);
                if (da >= 0f)
                    output[outCount++] = a;
                if ((da >= 0f) != (db >= 0f))
                    output[outCount++] = a + (b - a) * (da / (da - db));
            }
            return outCount;
        }

        // Отладка (SickleArcProjectile.DebugHitbox): зелёная пыль по границе зоны лезвия
        public static void EmitDebugDust(BladeZone zone, Vector2 center, float angle, bool mirrored, float scale)
        {
            if (zone == null || Main.dedServ)
                return;
            for (int i = 0; i < zone.Sectors.Length; i++)
            {
                BladeSector s = zone.Sectors[i];
                float a0 = mirrored ? -s.A1 : s.A0;
                float a1 = mirrored ? -s.A0 : s.A1;
                for (int k = 0; k < 3; k++)
                {
                    float a = angle + MathHelper.Lerp(a0, a1, k * 0.5f);
                    Dust dust = Dust.NewDustPerfect(center + a.ToRotationVector2() * (s.R1 * scale), DustID.GemEmerald, Vector2.Zero);
                    dust.noGravity = true;
                }
            }
        }

        // Запасная геометрия: приклад (рукоять) внизу-слево, острие (лезвие) вверху-справо.
        private static Geometry Fallback => new Geometry(new Vector2(0.5f, 31.5f), new Vector2(31.5f, 0.5f), 32);

        // ЗАМЕР по главной оси (ковариация) + концы по толщине. Робастен к кривым
        // серпам (длинные *SpearProjectile нарисованы дугой — min/max x-y на них врёт).
        // Рукоять (приклад) = ТОНКИЙ конец, лезвие (острие) = ТОЛСТЫЙ конец.
        private static Geometry Measure(Texture2D texture)
        {
            int w = texture.Width, h = texture.Height;
            Color[] data = new Color[w * h];
            texture.GetData(data);

            var pts = new List<Vector2>(w * h / 4);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (data[y * w + x].A > 128)
                        pts.Add(new Vector2(x + 0.5f, y + 0.5f));

            if (pts.Count < 4)
                return new Geometry(new Vector2(0.5f, h - 0.5f), new Vector2(w - 0.5f, 0.5f), w);

            // Центроид
            Vector2 c = Vector2.Zero;
            foreach (var p in pts) c += p;
            c /= pts.Count;

            // Ковариация → главная ось
            float sxx = 0f, syy = 0f, sxy = 0f;
            foreach (var p in pts)
            {
                float dx = p.X - c.X, dy = p.Y - c.Y;
                sxx += dx * dx; syy += dy * dy; sxy += dx * dy;
            }
            sxx /= pts.Count; syy /= pts.Count; sxy /= pts.Count;
            float theta = 0.5f * (float)Math.Atan2(2.0 * sxy, sxx - syy);
            Vector2 u = new Vector2((float)Math.Cos(theta), (float)Math.Sin(theta));
            Vector2 nrm = new Vector2(-u.Y, u.X);

            // Проекции на главную ось → диапазон
            float tmin = float.MaxValue, tmax = float.MinValue;
            var proj = new List<float>(pts.Count);
            foreach (var p in pts)
            {
                float t = Vector2.Dot(p - c, u);
                proj.Add(t);
                if (t < tmin) tmin = t;
                if (t > tmax) tmax = t;
            }
            float range = tmax - tmin;
            if (range < 2f)
                return new Geometry(new Vector2(0.5f, h - 0.5f), new Vector2(w - 0.5f, 0.5f), w);

            // Крайние 22% с каждого конца → толщина (разброс по перпендикуляру)
            float cut = range * 0.22f;
            Vector2 endA = Vector2.Zero, endB = Vector2.Zero;
            int cntA = 0, cntB = 0;
            float minNA = float.MaxValue, maxNA = float.MinValue;
            float minNB = float.MaxValue, maxNB = float.MinValue;
            for (int i = 0; i < pts.Count; i++)
            {
                float t = proj[i];
                float d = Vector2.Dot(pts[i] - c, nrm);
                if (t <= tmin + cut)
                {
                    endA += pts[i]; cntA++;
                    if (d < minNA) minNA = d;
                    if (d > maxNA) maxNA = d;
                }
                else if (t >= tmax - cut)
                {
                    endB += pts[i]; cntB++;
                    if (d < minNB) minNB = d;
                    if (d > maxNB) maxNB = d;
                }
            }
            if (cntA == 0 || cntB == 0)
                return new Geometry(new Vector2(0.5f, h - 0.5f), new Vector2(w - 0.5f, 0.5f), w);

            endA /= cntA; endB /= cntB;
            float thickA = maxNA - minNA;
            float thickB = maxNB - minNB;

            // Рукоять = ТОНКИЙ конец, лезвие = ТОЛСТЫЙ конец.
            Vector2 butt, tip;
            if (thickA < thickB) { butt = endA; tip = endB; }
            else { butt = endB; tip = endA; }

            // Толщина по обе стороны от оси приклад→острие (для лежащего серпа).
            Vector2 axis = tip - butt;
            float len = axis.Length();
            if (len < 1f) axis = Vector2.UnitX; else axis /= len;
            Vector2 normal = new Vector2(-axis.Y, axis.X);
            float sidePos = 0f, sideNeg = 0f;
            float alongMin = 0f, alongMax = len;
            foreach (var p in pts)
            {
                Vector2 rel = p - butt;
                float side = Vector2.Dot(rel, normal);
                sidePos = Math.Max(sidePos, side);
                sideNeg = Math.Max(sideNeg, -side);
                float along = Vector2.Dot(rel, axis);
                alongMin = Math.Min(alongMin, along);
                alongMax = Math.Max(alongMax, along);
            }

            return new Geometry(butt, tip, w, sidePos + 0.5f, sideNeg + 0.5f,
                -alongMin + 0.5f, alongMax - len + 0.5f);
        }
    }
}
