using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Приёмы копья (КОПЬЯ.md). Каркас для Северного полюса.
    // Замороженное копьё сюда НЕ входит — у него своя система (SicklePlayer).
    // Сейчас только состояния и входы — ванилла 1:1, механики впишем в методы приёмов.
    public class SpearPlayer : ModPlayer
    {
        public enum SpearMove { None, Swing, Thrust, Meet, Throw }

        public SpearMove CurrentMove = SpearMove.None;
        public int MoveTimer;

        private static bool IsSpear(Item item) =>
            item.type == ItemID.NorthPole;

        public bool HoldingSpear =>
            !Player.HeldItem.IsAir && IsSpear(Player.HeldItem);

        public override void ResetEffects()
        {
            // Состояние сбрасывается здесь, приёмы выставляют своё каждый тик
        }

        public override void PreUpdate()
        {
            if (Player.whoAmI != Main.myPlayer)
                return;
            if (!HoldingSpear || Player.dead)
            {
                CurrentMove = SpearMove.None;
                return;
            }
            // TODO(КОПЬЯ.md): разбор кнопок по приёмам (взмах/тычки/встреча/бросок).
            // TODO: вызвать StartSwing/StartThrust/StartMeet/StartThrow по вводу.
        }

        // Взмах: круг маленького радиуса, держит толпу вплотную
        public void StartSwing()
        {
            // TODO: круговой взмах
        }

        // Тычки: частые и точные, главный урон в одну цель
        public void StartThrust()
        {
            // TODO: серия уколов
        }

        // Встреча: копьё выставлено, враг натыкается сам, урон от скорости сближения
        public void StartMeet()
        {
            // TODO: стойка + урон от сближения
        }

        // Бросок: летит строго остриём, втыкается и остаётся, идти забирать
        public void StartThrow()
        {
            // TODO: бросок копья
        }
    }
}
