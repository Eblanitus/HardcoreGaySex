using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using FrostMelee.Content.Items.Prototypes;

namespace FrostMelee.Content.Weapons
{
    // Status is visible from the first RMB tick, not only after a successful bind.
    public class FlailLassoUI : ModSystem
    {
        public override void PostDrawInterface(SpriteBatch spriteBatch)
        {
            Player player = Main.LocalPlayer;
            if (Main.gameMenu || !player.active || player.dead || player.HeldItem.ModItem is not PrototypeFlail)
                return;
            SharedFlailPlayer input = player.GetModPlayer<SharedFlailPlayer>();
            if (!input.CanControl || !input.RightHeld)
                return;
            string key = input.ActiveHead?.LassoHint(player, input.AimWorld) ?? "NoTarget";
            Color color = key == "Bound" ? Color.LightGreen
                : key == "Wrapping" || key == "Yanking" ? Color.Gold : Color.LightGray;
            Vector2 position = new Vector2(24f, Main.screenHeight - 100f);
            Utils.DrawBorderString(spriteBatch, Language.GetTextValue("Mods.FrostMelee.LassoStatus." + key), position, color, 0.85f);
        }
    }
}
