using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Ледяная глазурь: след скольжения как лёд на верхнем блоке. Рисуются
    // ТОЛЬКО верхние 6 пикселей тайла (текстура IceCoat 16x6), а не вся
    // плитка: блок не перекрашивается, по кромке лежит мокрая ледяная
    // кромка, как блик на ванильном льду.
    // Данные — отметки на сетке тайлов ((x, y) верхнего твёрдого тайла
    // -> кадр истечения), ставятся IceSlidePlayer под ногами скользящего.
    //
    // МЕТОД ОТРИСОВКИ (поменян после трёх неудачных итераций):
    // раньше рисовали своим батчем в ModSystem.PostDrawTiles — в сборной
    // пользователя альфа в этом конвейере не работала, и тайл целиком
    // перекрашивался в белый при любых цифрах прозрачности.
    // Теперь рисует IceSheetDrawer своим PreDraw — внутри ванильного батча
    // снарядов, того самого, в котором у этого мода уже работают
    // полупрозрачные кроссфейды клинков. Прозрачность текстуры и цвета
    // там доказанно живая.
    //
    // Освещение из мира (Lighting.GetColor) с уводом в синеву,
    // потолок альфы задаётся вызовом (IceSlideSystem.SheetCapAlpha).
    //
    // Ограничения сознательные:
    //   - не кладём на склоны и полублоки: обмазка легла бы мимо поверхности;
    //   - не кладём, если сверху уже стоит твёрдый тайл (поверхность не открыта);
    //   - отметки клиентские: в мультиплеере чужую дорожку не видно.
    public class IceSheetSystem : ModSystem
    {
        public const int LifeFrames = 240;  // 4 секунды
        public const int FadeFrames = 60;   // тает в последнюю секунду
        public const int AppearFrames = 24; // намерзает за 0.4 секунды, смусстеп

        private static readonly Dictionary<Point, int> marks = new();
        private static Asset<Texture2D> coatTex;
        private static int drawer = -1;
        private static bool loadWarned;

        public static bool Any => marks.Count > 0;

        public static Asset<Texture2D> CoatTex =>
            coatTex ??= ModContent.Request<Texture2D>("FrostMelee/Textures/Effects/IceCoat");

        public override void OnWorldUnload()
        {
            marks.Clear();
            drawer = -1;
        }

        public override void Unload()
        {
            marks.Clear();
            coatTex = null;
            drawer = -1;
            loadWarned = false;
        }

        // Положить слой на поверхность: ищем сверху вниз первый твёрдый тайл
        // с открытым верхом и отмечаем его. yStart — тайл под ногами игрока.
        public static void MarkSurface(int x, int yStart, Player owner)
        {
            // Предохранитель от разрастания словаря (например, на сервере,
            // где художник не спавнится и чисткой никто не занимается)
            if (marks.Count > 1024)
                PurgeExpired();

            for (int y = yStart; y < yStart + 4; y++)
            {
                if (!WorldGen.InWorld(x, y, 2))
                    return;

                Tile tile = Main.tile[x, y];
                if (!tile.HasTile || !Main.tileSolid[tile.TileType])
                    continue;

                // На склонах и полублоках шапка легла бы мимо поверхности
                if (tile.Slope != 0 || tile.IsHalfBlock)
                    return;

                // Поверхность должна быть открыта: сверху нет твёрдого тайла
                Tile above = Main.tile[x, y - 1];
                if (above.HasTile && Main.tileSolid[above.TileType])
                    return;

                Point key = new Point(x, y);
                int until = (int)Main.GameUpdateCount + LifeFrames;
                if (!marks.TryGetValue(key, out int old) || old < until)
                    marks[key] = until;

                EnsureDrawer(owner);
                return;
            }
        }

        // Один скрытый снаряд-художник на всех: рисует дорожку в ванильном
        // батче снарядов и живёт, пока есть отметины
        private static void EnsureDrawer(Player owner)
        {
            if (Main.dedServ)
                return;

            if (drawer >= 0 && drawer < Main.maxProjectiles)
            {
                Projectile d = Main.projectile[drawer];
                if (d.active && d.type == ModContent.ProjectileType<IceSheetDrawer>())
                    return;
            }

            drawer = Projectile.NewProjectile(owner.GetSource_FromThis(), owner.Bottom,
                Vector2.Zero, ModContent.ProjectileType<IceSheetDrawer>(), 0, 0f, owner.whoAmI);
        }

        // Убрать просроченное; вызывает художник каждый тик
        public static void PurgeExpired()
        {
            if (marks.Count == 0)
                return;

            int now = (int)Main.GameUpdateCount;
            List<Point> dead = null;
            foreach (KeyValuePair<Point, int> kv in marks)
                if (kv.Value - now <= 0)
                    (dead ??= new List<Point>()).Add(kv.Key);

            if (dead == null)
                return;

            foreach (Point p in dead)
                marks.Remove(p);
        }

        // Рисование дорожки: вызывается из PreDraw снаряда-художника,
        // то есть внутри ванильного батча снарядов с живой альфой
        public static void DrawTrail()
        {
            if (marks.Count == 0)
                return;

            // Если текстура глазури не загрузилась (любой формат/путь),
            // рисуем запасным magic pixel: лёд виден всегда, а в лог
            // один раз пишем, что текстура не подхватилась
            Texture2D tex = CoatTex.IsLoaded ? CoatTex.Value : null;
            if (tex == null && !loadWarned)
            {
                loadWarned = true;
                ModContent.GetInstance<global::FrostMelee.FrostMelee>().Logger.Warn("[IceSheet] текстура IceCoat не загрузилась, рисую запасным пикселем");
            }

            int now = (int)Main.GameUpdateCount;
            Rectangle screen = new Rectangle((int)Main.screenPosition.X - 32, (int)Main.screenPosition.Y - 32,
                Main.screenWidth + 64, Main.screenHeight + 64);

            foreach (KeyValuePair<Point, int> kv in marks)
            {
                int remain = kv.Value - now;
                if (remain <= 0)
                    continue;

                Vector2 world = new Vector2(kv.Key.X * 16f, kv.Key.Y * 16f);
                if (!screen.Contains((int)world.X, (int)world.Y))
                    continue;

                // Таяние НЕПРЕРЫВНОЕ и нелинейное: льдинка становится
                // прозрачнее каждый тик всей своей жизни, а не обрывается в конце
                float t = (float)remain / LifeFrames;
                float fade = t * t * (3f - 2f * t);

                // Плавное появление: первые AppearFrames тиков альфа растёт
                // смусстепом, лёд «намерзает», а не вспыхивает готовой полосой
                int age = now - (kv.Value - LifeFrames);
                float appear = age < AppearFrames ? age / (float)AppearFrames : 1f;
                appear = appear * appear * (3f - 2f * appear);
                Color light = Lighting.GetColor(kv.Key.X, kv.Key.Y);
                byte a = (byte)(IceSlideSystem.SheetCapAlpha * fade * appear);
                Color c = new Color((byte)(light.R * 0.72f), (byte)(light.G * 0.88f), light.B, a);

                // EntitySpriteDraw — тот же вызов, которым в моде рисуются
                // рабочие полупрозрачные слои (кроссфейды клинков, копьё)
                if (tex != null)
                {
                    // Текстура игрока рисуется 1:1: её размер и есть покрытие
                    Main.EntitySpriteDraw(tex, world - Main.screenPosition, null, c,
                        0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
                }
                else
                {
                    Main.EntitySpriteDraw(TextureAssets.MagicPixel.Value,
                        world - Main.screenPosition, null, c, 0f, Vector2.Zero,
                        new Vector2(16f, 5f), SpriteEffects.None, 0f);
                }
            }
        }
    }
}
