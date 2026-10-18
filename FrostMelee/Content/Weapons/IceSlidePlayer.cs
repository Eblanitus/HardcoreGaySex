using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Скольжение ледяного клинка: состояние + эффект + визуал.
    //
    // Состояние: Sliding. Включается от IceSlideSystem.CanSlide (разгон >= 4 px/тик
    // вдоль выставленного копья). Держится, пока игрок докатывается
    // (IceSlideSystem.StillCoasting), то есть замедление НЕ обрывает скольжение —
    // отпускаешь клавиши и едешь до почти полной остановки, а не встаёшь мгновенно.
    //
    // Эффект выставляется из двух хуков (см. комментарий в IceSlideSystem):
    //   PostUpdateRunSpeeds — slippy + потолок скорости, гарантированно в движение кадра;
    //   PostUpdate          — дубль slippy, чтобы флаг пережил границу кадра.
    //
    // Визуал: пока скользим, вытянутый вперёд меч «намораживает» дорожку —
    // под игроком и чуть впереди остаётся тающая глянцевая полоса (IceSlideDeco),
    // плюс ледяная крошка из-под ног.
    public class IceSlidePlayer : ModPlayer
    {
        public bool Sliding;

        private int coastTicks;
        private int decalTimer;

        public override void PostUpdateRunSpeeds()
        {
            if (Player.whoAmI != Main.myPlayer)
                return;

            UpdateState();
            if (!Sliding)
                return;

            Player.slippy = true;
            Player.maxRunSpeed *= IceSlideSystem.SlideBoost;

            if (Main.dedServ)
                return;

            SpawnVisuals();
        }

        public override void PostUpdate()
        {
            if (Player.whoAmI != Main.myPlayer)
                return;

            // Дубль флага в старой точке выставления: на твёрдой земле ваниль,
            // похоже, не сбрасывает slippy каждый кадр, и запись отсюда несёт
            // скольжение через границу кадра — на ней держалось докатывание
            // в версии до переноса.
            if (Sliding)
                Player.slippy = true;
        }

        private void UpdateState()
        {
            SwordPlayer sp = Player.GetModPlayer<SwordPlayer>();
            bool was = Sliding;

            if (IceSlideSystem.CanSlide(Player, sp))
            {
                Sliding = true;
                coastTicks = IceSlideSystem.MaxCoastTicks;
            }
            else if (Sliding)
            {
                coastTicks--;
                if (!IceSlideSystem.StillCoasting(Player, sp) || coastTicks <= 0)
                    Sliding = false;
            }

            if (Sliding && !was)
                Mod.Logger.Info($"[IceSlide] engage: vel={Player.velocity.Length():F2}");
            else if (!Sliding && was)
                Mod.Logger.Info($"[IceSlide] disengage: vel={Player.velocity.Length():F2}");
        }

        private void SpawnVisuals()
        {
            // Глянец скользкой дорожки: каждые 3 тика, чуть впереди по ходу
            decalTimer--;
            if (decalTimer <= 0)
            {
                decalTimer = 3;
                Vector2 pos = Player.Bottom + new Vector2(Player.direction * Main.rand.NextFloat(6f, 22f), -2f);
                Projectile.NewProjectile(Player.GetSource_FromThis(), pos, Vector2.Zero,
                    ModContent.ProjectileType<IceSlideDeco>(), 0, 0f, Player.whoAmI);
            }

            // Ледяная крошка из-под ног
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
