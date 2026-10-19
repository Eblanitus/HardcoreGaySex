using Terraria;
using Terraria.ID;

namespace FrostMelee.Content
{
    // Ледяной серп, переделанный под доктрину HardcoreMelee
    // (ФИЛОСОФИЯ.md): взмах (ЛКМ), тычки (ПКМ зажать), копьё (ПКМ тап — тумблер),
    // бросок (ПКМ+ЛКМ, серп остаётся в земле/цели, без автовозврата).
    // Один источник правды: SicklePlayer, SickleGlobalItem, SickleUI берут отсюда.
    public static class SickleTypes
    {
        public static readonly int[] Sickles = new int[]
        {
            ItemID.IceSickle,    // Ледяной серп
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
        // Серп — режущее оружие: сильнее во взмахе и в копье (заходит боком),
        // слабее в тычках.
        public static float SwingDamageMult(Item item) => 1f;

        public static float StabDamageMult(Item item) => 0.7f;

        public static float SpearDamageMult(Item item) => 0.9f;

        // Бросок: коса летит ВСЕГДА остриём (лезвием) вперёд, как копьё, и втыкается
        // в мягкий блок/врага на скорости. После отскока кувыркается (SickleThrowProjectile).
        // Мечи — наоборот: крученый бросок как у палок (SwordTypes.ThrownAlwaysTipFirst = false),
        // поэтому здесь остриём-вперёд летят только серпы.
        public static bool ThrownAlwaysTipFirst(int itemType) =>
            !Weapons.SwordTypes.IsSwordId(itemType);

        // Серп режет, а не расталкивает: тычки слабо отбрасывают цель
        public static float StabKnockbackMult(Item item) => 0.5f;

        // Тычки частые и резкие
        public static float StabTimeMult(Item item) => 0.85f;
    }
}
