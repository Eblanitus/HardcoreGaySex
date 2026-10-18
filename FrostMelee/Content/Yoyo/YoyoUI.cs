using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace FrostMelee.Content.Yoyo
{
    // Надпись над головой: какой приём йо-йо сейчас (ЙОЙО.md: игрок должен видеть приём).
    public class YoyoUI : ModSystem
    {
        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            int index = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Entity Health Bars"));
            if (index == -1)
                index = 0;

            layers.Insert(index, new LegacyGameInterfaceLayer(
                "FrostMelee: Yoyo Indicator",
                () =>
                {
                    DrawMode();
                    return true;
                },
                InterfaceScaleType.Game));
        }

        private static void DrawMode()
        {
            // В настройках мода можно выключить индикатор
            if (!FrostMeleeConfigDefaults.ShowStanceIndicator)
                return;
            Player player = Main.LocalPlayer;
            if (player == null || !player.active || player.dead || player.ghost)
                return;
            if (player.HeldItem.IsAir || player.HeldItem.type != ItemID.Amarok)
                return;

            AmarokPlayer yp = player.GetModPlayer<AmarokPlayer>();
            string key = "Aim";
            Color color = Color.White * 0.45f;
            if (yp.CircleActive)
            {
                key = "Circle";
                color = new Color(255, 210, 90);
            }
            else if (yp.HomingWanted)
            {
                key = "Homing";
                color = new Color(140, 220, 255);
            }

            string text = Language.GetTextValue("Mods.FrostMelee.YoyoMode." + key);
            Vector2 anchor = player.gravDir == 1f ? player.Top : player.Bottom;
            Vector2 pos = anchor - Main.screenPosition + new Vector2(0f, -14f * player.gravDir);
            Utils.DrawBorderString(Main.spriteBatch, text, pos, color, 0.7f, 0.5f, 1f);
        }
    }
}
