using Terraria;
using Terraria.ID;

namespace ReaperMelee.Content
{
    // Коса смерти, переделанная под доктрину HardcoreMelee
    // (ФИЛОСОФИЯ.md): взмах (ЛКМ), тычки (ПКМ зажать), копьё (ПКМ тап — тумблер),
    // бросок (ПКМ+ЛКМ, коса остаётся в земле/цели, без автовозврата).
    // Один источник правды: SicklePlayer, SickleGlobalItem, SickleUI берут отсюда.
    public static class SickleTypes
    {
        public static readonly int[] Sickles = new int[]
        {
            ItemID.DeathSickle   // Коса смерти
        };

        public static bool IsSickle(Item item)
        {
            if (item == null || item.IsAir)
                return false;
            foreach (int sickleId in Sickles)
            {
                if (item.type == sickleId)
                    return true;
            }
            return false;
        }

        // === Стихии (ФИЛОСОФИЯ.md) ===
        // Коса — режущее оружие: сильнее во взмахе и в копье (заходит боком),
        // слабее в тычках. Коса смерти тяжёлая: бьёт больно, но медленно.
        public static float SwingDamageMult(Item item) => 1f;

        public static float StabDamageMult(Item item) => 0.7f;

        public static float SpearDamageMult(Item item) => 1.1f;

        // Бросок: коса летит ВСЕГДА остриём (лезвием) вперёд, как копьё, и втыкается
        // в мягкий блок/врага на скорости. После отскока кувыркается (SickleThrowProjectile).
        public static bool ThrownAlwaysTipFirst(int itemType) => true;

        // Серп режет, а не расталкивает: тычки слабо отбрасывают цель
        public static float StabKnockbackMult(Item item) => 0.5f;

        // Тычки тяжёлые и неторопливые
        public static float StabTimeMult(Item item) => 1.15f;
    }
}
