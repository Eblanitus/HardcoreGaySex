using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace ReaperMelee.Content
{
    // Текстуры косы. TextureAssets.Item[type] подменён на длинный боевой спрайт (Mod.Load), его же рисует Weapon Out Lite
    // и ваниль в руке. Наша отрисовка и геометрия берут обычный спрайт отсюда (Held / Normal), поэтому подмена слота им не мешает.
    public static class SickleTextures
    {
        private static readonly Dictionary<int, Asset<Texture2D>> held = new();

        // Подмена из Mod.Load: heldAsset — длинный обычный спрайт предмета
        public static void Register(int itemId, Asset<Texture2D> heldAsset)
        {
            held[itemId] = heldAsset;
        }

        public static Asset<Texture2D> Held(int itemId) => held.TryGetValue(itemId, out Asset<Texture2D> a) ? a : null;

        // Длинный боевой спрайт предмета
        public static Texture2D Normal(int itemId)
        {
            Asset<Texture2D> a = Held(itemId) ?? TextureAssets.Item[itemId];
            return a?.Value;
        }

        public static void Unload()
        {
            held.Clear();
        }
    }
}
