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
    // Точки выставления эффекта — по результату четырёх игровых проверок:
    //   PostUpdate      — «ледяной» флаг slippy. Запись из этой точки доживает
    //                     до движения следующего кадра; запись из
    //                     PostUpdateRunSpeeds ваниль стирает до того, как
    //                    flag прочитают (проверено: без PostUpdate чувства нет).
    //                     Флаг взят под полный контроль: slippy = Sliding ИЛИ
    //                     под ногами ванильный лёд. Поэтому липкости больше нет:
    //                     состояние кончилось — флаг сразу вернулся к ванильному
    //                     значению, а не живёт до прыжка, как раньше.
    //   PostUpdateRunSpeeds — потолок скорости (maxRunSpeed *= SlideBoost):
    //                     по документации tModLoader это единственный хук, где
    //                     эта запись применяется к движению того же кадра.
    //
    // Визуал: пока скользим, вытянутый вперёд меч «намораживает» дорожку —
    // глянцевая полоса (IceSlideDeco) теперь живёт несколько секунд и тает
    // в конце жизни: видно, где ты проехал, и лёд не исчезает мгновенно.
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

            Player.maxRunSpeed *= IceSlideSystem.SlideBoost;

            if (Main.dedServ)
                return;

            SpawnVisuals();
        }

        public override void PostUpdate()
        {
            if (Player.whoAmI != Main.myPlayer)
                return;

            // Владение флагом: скользим ИЛИ стоишь на ванильном льду.
            // Между кадрами ничего не утекает и не застревает.
            Player.slippy = Sliding || OnIceFloor();
        }

        // Под ногами ванильный лёд (там скольжение положено самой игрой)
        private bool OnIceFloor()
        {
            int x = (int)(Player.Center.X / 16f);
            int y = (int)((Player.Bottom.Y + 4f) / 16f);
            if (!WorldGen.InWorld(x, y))
                return false;

            Tile tile = Main.tile[x, y];
            return tile.HasTile && TileID.Sets.IceSkateSlippery[tile.TileType];
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

            // Глянец скользкой дорожки: каждые 3 тика, чуть впереди по ходу.
            // Полоса живёт несколько секунд (LifeTicks в IceSlideDeco).
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
