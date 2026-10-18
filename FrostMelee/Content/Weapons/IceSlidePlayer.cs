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
    // (IceSlideSystem.StillCoasting): замедление не обрывает скольжение мгновенно,
    // но на скорости ниже CoastStopSpeed (почти шаг) оно честно заканчивается.
    //
    // Эффект выставляется только из PostUpdateRunSpeeds и только пока состояние
    // Sliding активно: ваниль читает флаг в движении того же кадра, а между
    // кадрами ничего не утекает. Дубль флага в PostUpdate, который был раньше,
    // утекал в кадры обычной ходьбы — скольжение липло к низким скоростям.
    // Докатывание держится не утечкой, а самим состоянием (StillCoasting).
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
            // Хвост докатывания на тихом ходу не рисуем: глянец и крошка
            // только пока скорость выше VisualMinSpeed
            if (System.Math.Abs(Player.velocity.X) < IceSlideSystem.VisualMinSpeed)
                return;

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
