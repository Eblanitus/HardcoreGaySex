using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Северный полюс (КОПЬЯ.md): длина и укол. После Плантеры, отдельно от ледяной семьи.
    // Приёмы живут в SpearPlayer, здесь только крючок предмета.
    // Сейчас ванилла 1:1 — механики сюда, когда дойдём.
    public class NorthPoleGlobalItem : GlobalItem
    {
        public override bool AppliesToEntity(Item entity, bool lateInstantiation) => true;

        private static bool IsSpear(Item item) => item.type == ItemID.NorthPole;

        public override void SetDefaults(Item item)
        {
            if (!IsSpear(item))
                return;
            // TODO(КОПЬЯ.md): взмах — круг маленького радиуса (толпа вплотную);
            // TODO(КОПЬЯ.md): тычки — частые и точные, главный урон в одну цель;
            // TODO(КОПЬЯ.md): встреча — враг натыкается сам, урон от скорости сближения;
            // TODO(КОПЬЯ.md): бросок — летит строго остриём, втыкается, идти забирать.
            // TODO: ванильные снежинки — решить, остаются или заменяются приёмами.
        }
    }
}
