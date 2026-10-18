using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace FrostMelee.Content.Weapons
{
    // Лёгкий индикатор приёма для мечей (зеркало SickleUI, ФИЛОСОФИЯ.md:
    // «игрок должен видеть активный приём»). Маленькая надпись над головой,
    // пока в руке ледяной меч (Ice Blade, Frostbrand).
    public class SwordUI : ModSystem
    {
        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            int index = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Entity Health Bars"));
            if (index == -1)
                index = 0;

            layers.Insert(index, new LegacyGameInterfaceLayer(
                "FrostMelee: Sword Stance Indicator",
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
            if (!FrostMeleeConfigDefaults.ShowStanceIndicator)
                return;
            Player player = Main.LocalPlayer;
            if (player == null || !player.active || player.dead || player.ghost)
                return;

            SwordPlayer swordPlayer = player.GetModPlayer<SwordPlayer>();
            if (!swordPlayer.HoldingSword)
                return;

            string key;
            Color color;
            if (swordPlayer.HeldThrown)
            {
                key = "Thrown";
                color = new Color(255, 120, 90);
            }
            else
            {
                switch (swordPlayer.CurrentMode)
                {
                    case SwordPlayer.Mode.Spear:
                        key = "Meet";
                        color = new Color(255, 210, 90);
                        break;
                    case SwordPlayer.Mode.Stab:
                        key = "Stab";
                        color = new Color(140, 220, 255);
                        break;
                    default:
                        key = "Swing";
                        color = Color.White * 0.45f;
                        break;
                }
            }

            string text = Language.GetTextValue("Mods.FrostMelee.Stance." + key);
            Vector2 anchor = player.gravDir == 1f ? player.Top : player.Bottom;
            Vector2 pos = anchor - Main.screenPosition + new Vector2(0f, -14f * player.gravDir);
            Utils.DrawBorderString(Main.spriteBatch, text, pos, color, 0.7f, 0.5f, 1f);
        }
    }
}
