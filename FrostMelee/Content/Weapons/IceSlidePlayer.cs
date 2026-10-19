using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Скольжение ледяного клинка: состояние + эффект + визуал + рабочий лёд.
    //
    // Состояние: Sliding. Включается от IceSlideSystem.CanSlide (разгон >= 6 px/тик
    // вдоль выставленного копья, держится EngageHoldTicks тиков подряд — пики
    // скорости не считаются). Держится, пока игрок докатывается
    // (IceSlideSystem.StillCoasting): замедление не обрывает скольжение мгновенно,
    // но на скорости ниже CoastStopSpeed (почти шаг) оно честно заканчивается.
    //
    // Рабочий лёд: своя дорожка глазури — это не только картинка. Стоя или идя
    // по живой отметке (OnModIce), игрок получает настоящую ледяную физику:
    // скольжение без коньков и хватку с ускорением с коньками.
    //
    // Коньки (Player.iceSkate) на льду клинка работают как на ванильном льду:
    //   без коньков — slippy: скользко, разгон x0.7, потолок скорости +15%;
    //   с коньками — хватка (slippy выключен), разгон x2.1 и потолок +25%:
    //   аксессуар реально баффает передвижение от клинка, а не гасит его.
    // Ванильный лёд под ногами не трогаем: им владеет сама игра.
    //
    // Визуал: пока скользим, меч намораживает глазурь на блоках
    // (IceSheetSystem.MarkSurface) и крошку из-под ног.
    public class IceSlidePlayer : ModPlayer
    {
        public bool Sliding;

        private int coastTicks;
        private int fastTicks;

        public override void PostUpdateRunSpeeds()
        {
            if (Player.whoAmI != Main.myPlayer)
                return;

            UpdateState();

            bool onBladeIce = Sliding || OnModIce();
            if (!onBladeIce)
                return;

            if (HasSkates())
            {
                // Коньки на льду клинка: хватка (без скольжения) и ускорение x2.1,
                // как в ванили. Потолок скорости в ванили коньки не меняют,
                // но здесь он сознательная награда льда: без потолка в льде
                // нет смысла, решено игроком
                Player.slippy = false;
                Player.runAcceleration *= 2.1f;
                Player.maxRunSpeed *= 1.25f;
            }
            else
            {
                // Без коньков: скользко, потолок скорости выше
                Player.slippy = true;
                Player.maxRunSpeed *= IceSlideSystem.SlideBoost;
            }

            if (Main.dedServ)
                return;

            SpawnVisuals();
        }

        // Любые коньки: базовые, Искромётные ботинки, Ботинки земного
        // покрова и прочие тиры из коньков. Ванильное поле iceSkate
        // выставляется не всеми апгрейдами, поэтому читаем слоты
        // аксессуаров напрямую (соц-слоты не дают эффектов и не читаются)
        private bool HasSkates()
        {
            if (Player.iceSkate)
                return true;

            for (int i = 3; i < 3 + Player.maxAccessories; i++)
            {
                int t = Player.armor[i].type;
                if (t == ItemID.IceSkates || t == ItemID.FrostsparkBoots || t == ItemID.TerrasparkBoots)
                    return true;
            }

            return false;
        }

        // Стоим ли на живой глазури своей дорожки (тайл под ногами или соседний)
        private bool OnModIce()
        {
            int y = (int)((Player.Bottom.Y + 2f) / 16f);
            int x = (int)(Player.Center.X / 16f);
            return IceSheetSystem.HasLiveMark(x, y)
                || IceSheetSystem.HasLiveMark(x - 1, y)
                || IceSheetSystem.HasLiveMark(x + 1, y);
        }

        private void UpdateState()
        {
            SwordPlayer sp = Player.GetModPlayer<SwordPlayer>();
            bool was = Sliding;

            if (IceSlideSystem.CanSlide(Player, sp))
            {
                // Порог держится несколько тиков подряд — случайные пики
                // (приземление, склон, импульс) скольжение не включают
                fastTicks++;
                if (fastTicks >= IceSlideSystem.EngageHoldTicks)
                {
                    Sliding = true;
                    coastTicks = IceSlideSystem.MaxCoastTicks;
                }
            }
            else
            {
                fastTicks = 0;
                if (Sliding)
                {
                    coastTicks--;
                    if (!IceSlideSystem.StillCoasting(Player, sp) || coastTicks <= 0)
                        Sliding = false;
                }
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

            if (Main.GameUpdateCount % 60 == 0)
                Mod.Logger.Info("[IceSheet] скольжение активно, ставлю метки глазури");

            // Ледяной слой на сетке тайлов: отметки под ногами и чуть по ходу
            int y = (int)((Player.Bottom.Y + 2f) / 16f);
            int x0 = (int)((Player.Left.X - 2f) / 16f);
            int x1 = (int)((Player.Right.X + 2f) / 16f);
            for (int x = x0; x <= x1; x++)
                IceSheetSystem.MarkSurface(x, y);

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
