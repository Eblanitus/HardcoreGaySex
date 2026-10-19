using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Отметка льда на тайле: отдельно момент рождения и момент истечения.
    // Created не сбрасывается повторными отметками, поэтому анимация
    // появления не мигает под игроком и идёт один раз, плавно.
    public struct IceMark
    {
        public int Created;
        public int Expires;
    }

    // Ледяная глазурь скольжения и волны: отметки на сетке тайлов + отрисовка.
    //
    // Отрисовка — ModSystem.PostDrawTiles: этот хук вызывается всегда и не
    // зависит от снарядов в кадре. Раньше рисовал снаряд-художник, но ваниль
    // кульлит снаряды за экраном, и вместе с ним «пропадал» весь лёд,
    // когда игрок вертелся и уезжал от места спавна художника.
    //
    // Прозрачность: появление — смусстеп от Created за AppearFrames,
    // таяние — непрерывный смусстеп от остатка жизни. Оба умножаются
    // на альфу вызова; освещение берётся из мира.
    //
    // Лёд функционален: HasLiveMark используют скольжение и коньки
    // (IceSlidePlayer) — стоя на своей дорожке, игрок скользит по-настоящему.
    public class IceSheetSystem : ModSystem
    {
        public const int LifeFrames = 240;  // 4 секунды жизни
        public const int AppearFrames = 24; // намерзание 0.4 с, смусстеп

        private static readonly Dictionary<Point, IceMark> marks = new();
        private static Asset<Texture2D> coatTex;
        private static bool loadWarned;

        public static bool Any => marks.Count > 0;

        public static Asset<Texture2D> CoatTex =>
            coatTex ??= ModContent.Request<Texture2D>("FrostMelee/Textures/Effects/IceCoat");

        public override void OnWorldUnload()
        {
            marks.Clear();
        }

        public override void Unload()
        {
            marks.Clear();
            coatTex = null;
            loadWarned = false;
        }

        // Положить глазурь на поверхность: первый твёрдый тайл с открытым
        // верхом по колонке. Повторная отметка продлевает срок, но НЕ
        // сбрасывает Created — анимация появления не перезапускается.
        public static void MarkSurface(int x, int yStart)
        {
            if (marks.Count > 1024)
                PurgeExpired();

            for (int y = yStart; y < yStart + 4; y++)
            {
                if (!WorldGen.InWorld(x, y, 2))
                    return;

                Tile tile = Main.tile[x, y];
                if (!tile.HasTile || !Main.tileSolid[tile.TileType])
                    continue;

                // На склонах и полублоках глазурь легла бы мимо поверхности
                if (tile.Slope != 0 || tile.IsHalfBlock)
                    return;

                // Поверхность открыта: сверху нет твёрдого тайла
                Tile above = Main.tile[x, y - 1];
                if (above.HasTile && Main.tileSolid[above.TileType])
                    return;

                int now = (int)Main.GameUpdateCount;
                Point key = new Point(x, y);
                if (marks.TryGetValue(key, out IceMark old))
                {
                    old.Expires = System.Math.Max(old.Expires, now + LifeFrames);
                    marks[key] = old;
                }
                else
                {
                    marks[key] = new IceMark { Created = now, Expires = now + LifeFrames };
                }
                return;
            }
        }

        // Живая ли глазурь на тайле: для рабочего льда дорожки
        public static bool HasLiveMark(int x, int y)
        {
            if (!marks.TryGetValue(new Point(x, y), out IceMark m))
                return false;

            return m.Expires > (int)Main.GameUpdateCount;
        }

        public static void PurgeExpired()
        {
            if (marks.Count == 0)
                return;

            int now = (int)Main.GameUpdateCount;
            List<Point> dead = null;
            foreach (KeyValuePair<Point, IceMark> kv in marks)
                if (kv.Value.Expires - now <= 0)
                    (dead ??= new List<Point>()).Add(kv.Key);

            if (dead == null)
                return;

            foreach (Point p in dead)
                marks.Remove(p);
        }

        public override void PostDrawTiles()
        {
            if (Main.dedServ || marks.Count == 0)
                return;

            PurgeExpired();

            Texture2D tex = CoatTex.IsLoaded ? CoatTex.Value : null;
            if (tex == null && !loadWarned)
            {
                loadWarned = true;
                ModContent.GetInstance<global::FrostMelee.FrostMelee>().Logger.Warn("[IceSheet] текстура IceCoat не загрузилась, рисую запасным пикселем");
            }

            int now = (int)Main.GameUpdateCount;
            Rectangle screen = new Rectangle((int)Main.screenPosition.X - 64, (int)Main.screenPosition.Y - 64,
                Main.screenWidth + 128, Main.screenHeight + 128);

            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null,
                Main.GameViewMatrix.TransformationMatrix);

            foreach (KeyValuePair<Point, IceMark> kv in marks)
            {
                int remain = kv.Value.Expires - now;
                if (remain <= 0)
                    continue;

                Vector2 world = new Vector2(kv.Key.X * 16f, kv.Key.Y * 16f);
                if (!screen.Contains((int)world.X, (int)world.Y))
                    continue;

                // Появление: смусстеп от рождения отметки, однократно
                int age = now - kv.Value.Created;
                float ap = MathHelper.Clamp(age / (float)AppearFrames, 0f, 1f);
                float appear = ap * ap * (3f - 2f * ap);

                // Таяние: непрерывный смусстеп всей жизни
                float t = (float)remain / LifeFrames;
                float fade = t * t * (3f - 2f * t);

                Color light = Lighting.GetColor(kv.Key.X, kv.Key.Y);
                byte a = (byte)(IceSlideSystem.SheetCapAlpha * fade * appear);
                Color c = new Color((byte)(light.R * 0.72f), (byte)(light.G * 0.88f), light.B, a);

                if (tex != null)
                {
                    // Текстура игрока рисуется 1:1: её размер и есть покрытие
                    Main.spriteBatch.Draw(tex, world - Main.screenPosition, null, c,
                        0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
                }
                else
                {
                    Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value,
                        new Rectangle((int)(world.X - Main.screenPosition.X),
                            (int)(world.Y - Main.screenPosition.Y), 16, 5), c);
                }
            }

            Main.spriteBatch.End();
        }
    }
}
