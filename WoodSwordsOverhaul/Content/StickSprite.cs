using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;

namespace WoodSwordsOverhaul.Content
{
    // Реальная геометрия диагонального спрайта палки/заточки, измеренная по пикселям:
    // где приклад, где остриё, длина и угол. Константы «22.6px / 45°» не годятся:
    // у заточек лезвие короче спрайта (остриё ~(23..25, 8) в 32px), у кактуса приклад
    // не в самом углу, у части спрайтов угол не ровно 45°. Из-за этого хитбокс
    // уходил дальше видимого острия, а поворот/положение плыли.
    public static class StickSprite
    {
        public readonly struct Geometry
        {
            public readonly Vector2 Butt;   // приклад в пикселях текстуры (центр пикселя)
            public readonly Vector2 Tip;    // остриё в пикселях текстуры
            public readonly int Width;
            // Толщина по обе стороны от оси приклад→остриё (px, по реальным пикселям).
            // SidePos — в сторону нормали (-dir.Y, dir.X): у лежащей остриём вправо
            // палки это сторона «вниз», к земле. SideNeg — противоположная.
            public readonly float SidePos;
            public readonly float SideNeg;

            public Geometry(Vector2 butt, Vector2 tip, int width, float sidePos = 3f, float sideNeg = 3f)
            {
                Butt = butt;
                Tip = tip;
                Width = width;
                SidePos = sidePos;
                SideNeg = sideNeg;
            }

            public float Length => Vector2.Distance(Butt, Tip);

            // Приклад и остриё с учётом зеркалирования (FlipHorizontally зеркалит
            // картинку внутри прямоугольника, а origin — нет)
            public Vector2 ButtFor(bool flipped) => flipped ? new Vector2(Width - Butt.X, Butt.Y) : Butt;
            public Vector2 TipFor(bool flipped) => flipped ? new Vector2(Width - Tip.X, Tip.Y) : Tip;

            // Угол вектора приклад → остриё в спрайте
            public float AngleFor(bool flipped) => (TipFor(flipped) - ButtFor(flipped)).ToRotation();
        }

        // Где палку держит кисть. Player.GetFrontHandPosition отдаёт точку примерно
        // на GripRaise px НИЖЕ, чем рисуется кисть составной руки (сверено по скринам
        // пользователя: древко висело под перчаткой на ~3px и в тычке, и в копье —
        // «держит низко»). Поднимаем хват до нарисованной кисти.
        public const float GripRaise = 3f;

        public static Vector2 HandGrip(Player player, Player.CompositeArmStretchAmount stretch, float armRot) =>
            player.GetFrontHandPosition(stretch, armRot) - new Vector2(0f, GripRaise * player.gravDir);

        // Типичный 32px спрайт палки: приклад внизу-слева, остриё вверху-справа
        public static readonly Geometry Fallback = new Geometry(new Vector2(0.5f, 31.5f), new Vector2(31.5f, 0.5f), 32);

        private static readonly Dictionary<int, Geometry> cache = new();

        public static void ClearCache() => cache.Clear();

        public static Geometry Get(int itemType)
        {
            if (Main.dedServ || itemType <= 0)
                return Fallback;
            if (cache.TryGetValue(itemType, out Geometry cached))
                return cached;

            Main.instance.LoadItem(itemType);
            Asset<Texture2D> asset = TextureAssets.Item[itemType];
            // Пока текстура грузится, отдаём запасную геометрию и не кэшируем
            if (asset == null || !asset.IsLoaded)
                return Fallback;
            Texture2D texture = asset.Value;
            if (texture == null || texture.IsDisposed)
                return Fallback;

            Geometry geometry = Measure(texture);
            cache[itemType] = geometry;
            return geometry;
        }

        // Приклад — непрозрачный пиксель, крайний в сторону низ-лево (минимум x - y),
        // остриё — крайний в сторону верх-право (максимум x - y)
        private static Geometry Measure(Texture2D texture)
        {
            int w = texture.Width, h = texture.Height;
            Color[] data = new Color[w * h];
            texture.GetData(data);

            int bestButt = int.MaxValue, bestTip = int.MinValue;
            Point butt = Point.Zero, tip = Point.Zero;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (data[y * w + x].A == 0)
                        continue;
                    int d = x - y;
                    if (d < bestButt) { bestButt = d; butt = new Point(x, y); }
                    if (d > bestTip) { bestTip = d; tip = new Point(x, y); }
                }
            }

            if (bestButt == int.MaxValue)
                return new Geometry(new Vector2(0.5f, h - 0.5f), new Vector2(w - 0.5f, 0.5f), w);

            Vector2 buttPos = new Vector2(butt.X + 0.5f, butt.Y + 0.5f);
            Vector2 tipPos = new Vector2(tip.X + 0.5f, tip.Y + 0.5f);

            // Толщина: самый дальний непрозрачный пиксель по обе стороны от оси.
            // Нужна, чтобы лежащая палка касалась земли нижним краем, а не висела.
            Vector2 axis = (tipPos - buttPos).SafeNormalize(Vector2.UnitX);
            Vector2 normal = new Vector2(-axis.Y, axis.X);
            float sidePos = 0f, sideNeg = 0f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (data[y * w + x].A == 0)
                        continue;
                    float side = Vector2.Dot(new Vector2(x + 0.5f, y + 0.5f) - buttPos, normal);
                    sidePos = Math.Max(sidePos, side);
                    sideNeg = Math.Max(sideNeg, -side);
                }
            }
            // + полпикселя: считали по центрам пикселей
            return new Geometry(buttPos, tipPos, w, sidePos + 0.5f, sideNeg + 0.5f);
        }
    }
}
