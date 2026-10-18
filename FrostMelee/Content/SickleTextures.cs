using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content
{
    // Текстуры серпов. TextureAssets.Item[type] подменён на длинный боевой спрайт (Mod.Load), его же рисует Weapon Out Lite
    // и ваниль в руке. Для заморозки ледяного копья этот слот переключается на замороженный спрайт (SetFrozenView), а ВСЯ
    // наша отрисовка и геометрия берут обычный спрайт отсюда (Held / Normal), поэтому подмена слота им не мешает.
    public static class SickleTextures
    {
        // Шкала, при которой чужие рисовальщики (Weapon Out, ваниль) переключаются на замороженный спрайт; зазор — гистерезис
        private const float ViewOnAt = 0.55f;
        private const float ViewOffAt = 0.45f;

        private static readonly Dictionary<int, Asset<Texture2D>> held = new();
        private static Asset<Texture2D> frozen;
        private static bool frozenShown;

        // Подмена из Mod.Load: heldAsset — длинный обычный спрайт предмета
        public static void Register(int itemId, Asset<Texture2D> heldAsset)
        {
            held[itemId] = heldAsset;
        }

        public static Asset<Texture2D> Held(int itemId) => held.TryGetValue(itemId, out Asset<Texture2D> a) ? a : null;

        // Обычный (не замороженный) длинный спрайт предмета
        public static Texture2D Normal(int itemId)
        {
            Asset<Texture2D> a = Held(itemId) ?? TextureAssets.Item[itemId];
            return a?.Value;
        }

        // Вызывать каждый тик у локального игрока: переключает то, что видят Weapon Out и ваниль
        public static void UpdateView(float frost)
        {
            if (Main.dedServ)
                return;
            bool on = frost >= ViewOnAt || (frozenShown && frost > ViewOffAt);
            if (on == frozenShown || !held.TryGetValue(ItemID.IceSickle, out Asset<Texture2D> heldAsset))
                return;
            if (on)
            {
                frozen ??= ModContent.Request<Texture2D>("FrostMelee/Items/IceSpearFrozen", AssetRequestMode.ImmediateLoad);
                TextureAssets.Item[ItemID.IceSickle] = frozen;
            }
            else
            {
                TextureAssets.Item[ItemID.IceSickle] = heldAsset;
            }
            frozenShown = on;
        }

        public static void Unload()
        {
            held.Clear();
            frozen = null;
            frozenShown = false;
        }
    }
}
