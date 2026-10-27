using Microsoft.Xna.Framework;
using Terraria.ID;

namespace FrostMelee.Content.Weapons
{
    // Праздничные мечи — подкласс мечей движка (реестр SwordTypes).
    // Приёмы у них шаблонные, как у всех мечей: взмах-дуга (SwordGlobalItem),
    // встреча, тычки и бросок (SwordPlayer) — всё это они получают просто
    // записью в SwordTypes.Swords. Уникальных механик у подкласса НЕТ:
    // ни полировки Ice Blade, ни ледяного огня Frostbrand, ни инея.
    // Здесь живёт только база подкласса: состав и внешнее отличие — цвета
    // дуги взмаха. Если подклассу когда-нибудь дадут свою механику, её
    // параметры и проверки вешать сюда.
    public static class FestiveSwords
    {
        public static readonly int[] Items = new int[]
        {
            ItemID.CandyCaneSword,      // Карамельный меч (меч-леденец)
            ItemID.ChristmasTreeSword   // Меч из ёлки
        };

        public static bool IsFestive(int itemType)
        {
            foreach (int type in Items)
                if (type == itemType)
                    return true;
            return false;
        }

        // Цвета дуги взмаха: ядро у головы, хвост и глубокий край —
        // тот же трёхцветный набор, что у остальных дуг движка.
        public static void ArcColors(int itemType, out Color core, out Color tail, out Color edge)
        {
            if (itemType == ItemID.ChristmasTreeSword)
            {
                // Ёлка: тёплое золотое ядро гирлянды, хвоя хвостом
                core = new Color(255, 238, 190);
                tail = new Color(110, 225, 130);
                edge = new Color(20, 140, 60);
            }
            else
            {
                // Карамель: бело-красная, как сам леденец
                core = new Color(255, 248, 248);
                tail = new Color(255, 115, 115);
                edge = new Color(195, 30, 50);
            }
        }
    }
}
