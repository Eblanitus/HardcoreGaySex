using Terraria;
using Terraria.ID;

namespace FrostMelee.Content.Weapons
{
    // Ледяные мечи (Ice Blade, Frostbrand) под доктрину HardcoreMelee
    // (МЕЧИ.md): взмах-дуга (ЛКМ), встреча (ПКМ тап), тычки (ПКМ зажать),
    // бросок (ПКМ+ЛКМ, остаётся в земле/цели, забирать самому).
    // Один источник правды: SwordPlayer, SwordGlobalItem, SwordUI берут отсюда.
    public static class SwordTypes
    {
        public static readonly int[] Swords = new int[]
        {
            ItemID.IceBlade,   // Ледяной клинок
            ItemID.Frostbrand  // Морозный клинок
        };

        public static bool IsSword(Item item) => item != null && !item.IsAir && IsSwordId(item.type);

        public static bool IsSwordId(int itemType)
        {
            foreach (int type in Swords)
                if (type == itemType)
                    return true;
            return false;
        }

        // === Стихии (ФИЛОСОФИЯ.md) ===
        // Меч — «дуга и вес»: стихия взмах. Тычки и встреча возможны, но слабее.
        public static float SwingDamageMult(Item item) => 1f;

        public static float StabDamageMult(Item item) => 0.85f;

        public static float SpearDamageMult(Item item) => 0.8f;

        // Бросок: меч летит остриём (лезвием) вперёд, как копьё, и втыкается
        // в мягкий блок/врага на скорости (SickleThrowProjectile).
        public static bool ThrownAlwaysTipFirst(int itemType) => true;

        public static float StabKnockbackMult(Item item) => 0.5f;

        // Меч короткий и быстрый: тычки резкие
        public static float StabTimeMult(Item item) => 0.85f;
    }
}
