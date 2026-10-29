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
            if (player.HeldItem.ModItem is SnowballFlail && FrostMeleeConfigDefaults.ShowStanceIndicator)
            {
                float mass = player.GetModPlayer<SnowballFlailPlayer>().Mass;
                Vector2 bar = new Vector2(24f, Main.screenHeight - 130f);
                Utils.DrawBorderString(spriteBatch,
                    Language.GetTextValue("Mods.FrostMelee.SnowballMass", (int)System.Math.Round(mass * 100f)),
                    bar, Color.LightCyan, 0.85f);
                Texture2D pixel = Terraria.GameContent.TextureAssets.MagicPixel.Value;
                spriteBatch.Draw(pixel, new Rectangle(24, Main.screenHeight - 106, 100, 4), Color.DarkSlateGray);
                spriteBatch.Draw(pixel, new Rectangle(24, Main.screenHeight - 106, (int)(100f * mass), 4), Color.LightCyan);
            }
            SharedFlailPlayer input = player.GetModPlayer<SharedFlailPlayer>();
            if (!input.CanControl || !input.RightHeld)
                return;
            string key = input.ActiveHead?.LassoHint(player, input.AimWorld) ?? "NoTarget";
            Color color = key == "Bound" ? Color.LightGreen
                : key == "Casting" || key == "Wrapping" || key == "Yanking" ? Color.Gold : Color.LightGray;
            Vector2 position = new Vector2(24f, Main.screenHeight - 100f);
            Utils.DrawBorderString(spriteBatch, Language.GetTextValue("Mods.FrostMelee.LassoStatus." + key), position, color, 0.85f);
        }
    }
}
