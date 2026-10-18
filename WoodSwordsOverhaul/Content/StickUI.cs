using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace WoodSwordsOverhaul.Content
{
    // Лёгкий индикатор приёма (ФИЛОСОФИЯ.md: «игрок должен видеть активный приём»).
    // Маленькая надпись над головой, пока в руках оружие серии.
    // Взмах — режим по умолчанию, поэтому он приглушён; остальные приёмы яркие.
    public class StickUI : ModSystem
    {
        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            int index = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Entity Health Bars"));
            if (index == -1)
                index = 0;

            layers.Insert(index, new LegacyGameInterfaceLayer(
                "WoodSwordsOverhaul: Stance Indicator",
                () =>
                {
                    DrawStance();
                    return true;
                },
                InterfaceScaleType.Game));
        }

        private static void DrawStance()
        {
            // В настройках мода можно выключить индикатор
            if (!WoodSwordsConfigDefaults.ShowStanceIndicator)
                return;
            Player player = Main.LocalPlayer;
            if (player == null || !player.active || player.dead || player.ghost)
                return;

            StickPlayer stickPlayer = player.GetModPlayer<StickPlayer>();
            if (!stickPlayer.HoldingStick)
                return;

            string key;
            Color color;
            // Брошено — именно та палка, что в руках (другие можно бросать и брать)
            if (stickPlayer.HeldThrown)
            {
                key = "Thrown";
                color = new Color(255, 120, 90);
            }
            else
            {
                switch (stickPlayer.CurrentMode)
                {
                    case StickPlayer.Mode.Spear:
                        key = "Spear";
                        color = new Color(255, 210, 90);
                        break;
                    case StickPlayer.Mode.Dagger:
                        key = "Dagger";
                        color = new Color(140, 220, 255);
                        break;
                    default:
                        key = "Swing";
                        color = Color.White * 0.45f;
                        break;
                }
            }

            string text = Language.GetTextValue("Mods.WoodSwordsOverhaul.Stance." + key);
            // Над головой игрока (с учётом гравитации — при перевёрнутой гравитации снизу)
            Vector2 anchor = player.gravDir == 1f ? player.Top : player.Bottom;
            Vector2 pos = anchor - Main.screenPosition + new Vector2(0f, -14f * player.gravDir);
            Utils.DrawBorderString(Main.spriteBatch, text, pos, color, 0.7f, 0.5f, 1f);
        }
    }
}
