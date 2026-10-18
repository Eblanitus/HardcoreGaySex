using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Ледяная обмазка: след скольжения как лёд, Намороженный НА верхний слой
    // блоков. Не шапка над кромкой и не партиклы: текстура IceCoat 16x16
    // накладывается ровно на верхний твёрдый тайл, и блок выглядит обледеневшим.
    //
    // Храним отметки на сетке тайлов:
    //   (x, y) верхнего твёрдого тайла -> номер кадра, до которого обмазка живёт.
    // Отметка ставится под ногами скользящего игрока (IceSlidePlayer),
    // живёт 4 секунды и плавно тает в последнюю.
    //
    // Рисуем в PostDrawTiles: поверх ЛЮБЫХ блоков, но до игрока, NPC и снарядов —
    // обледеневшая поверхность оказывается под ногами, как и должна.
    // Освещение берём из мира (Lighting.GetColor), поэтому в темноте лёд тёмный,
    // у факелов — тёплый: читается как свойство блока, а не наклейка.
    //
    // Ограничения сознательные:
    //   - не кладём на склоны и полублоки: обмазка легла бы мимо поверхности;
    //   - не кладём, если сверху уже стоит твёрдый тайл (поверхность не открыта);
    //   - отметки клиентские: в мультиплеере чужую дорожку не видно.
    public class IceSheetSystem : ModSystem
    {
        public const int LifeFrames = 240;  // 4 секунды
        public const int FadeFrames = 60;   // тает в последнюю секунду

        private static readonly Dictionary<Point, int> marks = new();
        private static Asset<Texture2D> capTex;

        public override void OnWorldUnload()
        {
            marks.Clear();
        }

        public override void Unload()
        {
            marks.Clear();
            capTex = null;
        }

        // Положить слой на поверхность: ищем сверху вниз первый твёрдый тайл
        // с открытым верхом и отмечаем его. yStart — тайл под ногами игрока.
        public static void MarkSurface(int x, int yStart)
        {
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
                return;
            }
        }

        public override void PostDrawTiles()
        {
            if (Main.dedServ || marks.Count == 0)
                return;

            capTex ??= ModContent.Request<Texture2D>("FrostMelee/Textures/Effects/IceCoat");
            if (!capTex.IsLoaded)
                return;

            int now = (int)Main.GameUpdateCount;
            Rectangle screen = new Rectangle((int)Main.screenPosition.X - 32, (int)Main.screenPosition.Y - 32,
                Main.screenWidth + 64, Main.screenHeight + 64);

            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null,
                Main.GameViewMatrix.TransformationMatrix);

            List<Point> dead = null;
            foreach (KeyValuePair<Point, int> kv in marks)
            {
                int remain = kv.Value - now;
                if (remain <= 0)
                {
                    (dead ??= new List<Point>()).Add(kv.Key);
                    continue;
                }

                // Ровно на тайл: обмазка лежит НА верхнем блоке, а не над ним
                Vector2 world = new Vector2(kv.Key.X * 16f, kv.Key.Y * 16f);
                if (!screen.Contains((int)world.X, (int)world.Y))
                    continue;

                float fade = remain < FadeFrames ? remain / (float)FadeFrames : 1f;
                Color c = Lighting.GetColor(kv.Key.X, kv.Key.Y) * (0.85f * fade);
                Main.spriteBatch.Draw(capTex.Value, world - Main.screenPosition, null, c,
                    0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
            }

            Main.spriteBatch.End();

            if (dead != null)
                foreach (Point p in dead)
                    marks.Remove(p);
        }
    }
}
