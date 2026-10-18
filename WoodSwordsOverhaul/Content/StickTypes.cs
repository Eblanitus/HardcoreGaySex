using Terraria;
using Terraria.ID;

namespace WoodSwordsOverhaul.Content
{
    // Единый список 9 деревянных мечей, которые мод превращает в палки.
    // Один источник правды — WoodSwordsGlobalItem, StickPlayer и StickUI берут отсюда.
    public static class StickTypes
    {
        public static readonly int[] WoodSwords = new int[] {
            ItemID.WoodenSword,        // Деревянный меч
            ItemID.RichMahoganySword,  // Меч из красного дерева
            ItemID.EbonwoodSword,      // Меч из эбонитового дерева
            ItemID.ShadewoodSword,     // Меч из мрачного дерева
            ItemID.PearlwoodSword,     // Меч из жемчужного дерева
            ItemID.BorealWoodSword,    // Меч из северного дерева
            ItemID.PalmWoodSword,      // Меч из пальмового дерева
            ItemID.AshWoodSword,       // Меч из пепельного дерева
            ItemID.CactusSword         // Кактусовый меч
        };

        public static bool IsWoodSword(Item item)
        {
            foreach (int swordId in WoodSwords)
            {
                if (item.type == swordId)
                    return true;
            }

            // Заточки ( daggers ) — тоже оружие серии: получают все режимы приёмов
            if (IsDagger(item))
                return true;

            return false;
        }

        public static bool IsDagger(Item item) => item.ModItem is Items.Daggers.StickDagger;

        // === Стихии (ФИЛОСОФИЯ.md, «Объединение оружий») ===
        // Все умеют всё, но палка сильнее во взмахе и копье, заточка — в тычках.
        // Множители урона по приёму:
        public static float SwingDamageMult(Item item) => IsDagger(item) ? 0.7f : 1f;
        public static float StabDamageMult(Item item) => IsDagger(item) ? 1.15f : 0.7f;
        public static float SpearDamageMult(Item item) => IsDagger(item) ? 0.6f : 1f;

        // Бросок: палка втыкается только если коснулась острием вперёд (в допуске по углу).
        // Для будущих метательных копий — true: летят строго остриём вперёд, без кувырка,
        // угол не проверяется (скорость и мягкость тайла — по-прежнему).
        public static bool ThrownAlwaysTipFirst(int itemType) => false;

        // Отбрасывание врага тычком: у заточки его нет (быстрые тычки не должны
        // расталкивать цель), у палки — обычное
        public static float StabKnockbackMult(Item item) => IsDagger(item) ? 0f : 1f;

        // Множитель длительности тычка (меньше = чаще и резче)
        public static float StabTimeMult(Item item) => IsDagger(item) ? 0.7f : 1.3f;
    }
}
