using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.ModLoader;

namespace WoodSwordsOverhaul.Content.Items
{
    // Метка «этот экземпляр брошен» на самом предмете (а не на типе):
    // можно бросить несколько палок, по одной на предмет, и даже две одинаковые.
    // Token — случайный номер броска; предмет заблокирован, пока у владельца
    // жив снаряд броска с тем же номером (StickPlayer.IsLocked). Подобрал палку —
    // снаряд исчез — блок снят сам. Номер не сохраняется: после перезахода в мир
    // снарядов уже нет, и предмет свободен.
    public class ThrownLockItem : GlobalItem
    {
        public int Token;

        public override bool InstancePerEntity => true;

        public override bool AppliesToEntity(Item entity, bool lateInstantiation) =>
            lateInstantiation && StickTypes.IsWoodSword(entity);

        // Брошенный предмет в слоте — бледный полупрозрачный силуэт:
        // сразу видно, что палки в руках нет, хотя слот занят
        public const float GhostOpacity = 0.35f;
        private static readonly Color GhostTint = new Color(150, 150, 150);

        public override bool PreDrawInInventory(Item item, SpriteBatch spriteBatch, Vector2 position,
            Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale)
        {
            if (!IsLockedForLocal(item))
                return true;

            Texture2D texture = TextureAssets.Item[item.type].Value;
            Color ghost = drawColor.MultiplyRGB(GhostTint) * GhostOpacity;
            spriteBatch.Draw(texture, position, frame, ghost, 0f, origin, scale, SpriteEffects.None, 0f);
            return false;
        }

        public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
        {
            if (!IsLockedForLocal(item))
                return;
            tooltips.Add(new TooltipLine(Mod, "StickThrown",
                Language.GetTextValue("Mods.WoodSwordsOverhaul.ThrownTooltip"))
            {
                OverrideColor = new Color(255, 120, 90)
            });
        }

        private static bool IsLockedForLocal(Item item)
        {
            Player player = Main.LocalPlayer;
            return player != null && player.active && player.GetModPlayer<StickPlayer>().IsLocked(item);
        }
    }
}
