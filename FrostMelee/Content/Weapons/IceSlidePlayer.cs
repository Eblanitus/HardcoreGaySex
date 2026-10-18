using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Скольжение ледяного клинка: эффект, а не проверка.
    //
    // Стоит именно в PostUpdateRunSpeeds — это единственный хук, из которого
    // запись в maxRunSpeed / runAcceleration / slippy доживает до HorizontalMovement().
    // Из PostUpdate() (где вызов был раньше) она стиралась следующим ResetEffects().
    //
    // Что даёт:
    //   slippy = true   — ванильный «игрок на льду». Без коньков runAcceleration *= 0.7f,
    //                       с Ледовыми коньками (Ice Skates) *= 2.1f.
    //                       То есть коньки стакаются с ледяным мечом сами, отдельного кода не нужно.
    //   maxRunSpeed *= SlideBoost — потолок скорости выше обычного на время скольжения.
    //
    // Ощущение в итоге: разгоняешься медленнее, но уезжаешь дальше обычного.
    public class IceSlidePlayer : ModPlayer
    {
        private ulong lastSlideFrame;

        public override void PostUpdateRunSpeeds()
        {
            if (Player.whoAmI != Main.myPlayer)
                return;

            if (!IceSlideSystem.CanSlide(Player, Player.GetModPlayer<SwordPlayer>()))
                return;

            Player.slippy = true;
            Player.maxRunSpeed *= IceSlideSystem.SlideBoost;

            // Раз в секунду — отметка, что скольжение действительно активно.
            // Пишется в client.log, на экране не мешает. Убедившись, что работает, строку можно удалить.
            if (Main.GameUpdateCount - lastSlideFrame >= 60)
            {
                lastSlideFrame = Main.GameUpdateCount;
                Mod.Logger.Info($"[IceSlide] активен: maxRunSpeed={Player.maxRunSpeed:F2}, runAcceleration={Player.runAcceleration:F4}");
            }

            if (Main.dedServ)
                return;

            // Визуал: ледяная крошка из-под ног
            if (Main.rand.NextBool(3))
            {
                Vector2 foot = Player.Bottom + new Vector2(Main.rand.NextFloat(-6f, 6f), -2f);
                Dust d = Dust.NewDustPerfect(foot, DustID.SnowflakeIce,
                    new Vector2(Player.velocity.X * 0.1f, -0.4f), 120, default, 0.9f);
                d.noGravity = true;
            }
        }
    }
}
