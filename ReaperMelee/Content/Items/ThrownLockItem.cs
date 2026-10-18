using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.ModLoader;

namespace ReaperMelee.Content.Items
{
    // Метка «этот экземпляр брошен» на самом предмете: можно бросить несколько
    // серпов. Token — случайный номер броска; предмет заблокирован, пока у
    // владельца жив снаряд броска с тем же номером. Подобрал — блок снят сам.
    public class ThrownLockItem : GlobalItem
    {
        public int Token;

        public override bool InstancePerEntity => true;

        public override bool AppliesToEntity(Item entity, bool lateInstantiation) =>
            lateInstantiation && SickleTypes.IsSickle(entity);

        // Брошенный предмет в слоте — бледный полупрозрачный силуэт
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
            if (!IsLockedForLocal(item) || !ReaperMeleeConfigDefaults.ShowControlTooltips)
                return;
            tooltips.Add(new TooltipLine(Mod, "SickleThrown",
                Language.GetTextValue("Mods.ReaperMelee.ThrownTooltip"))
            {
                OverrideColor = new Color(255, 120, 90)
            });
        }

        private static bool IsLockedForLocal(Item item)
        {
            Player player = Main.LocalPlayer;
            return player != null && player.active && player.GetModPlayer<SicklePlayer>().IsLocked(item);
        }
    }
}
