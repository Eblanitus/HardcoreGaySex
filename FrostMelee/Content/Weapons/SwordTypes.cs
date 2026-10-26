using Terraria;
using Terraria.ID;

namespace FrostMelee.Content.Weapons
{
    // Мечи движка (Ice Blade, Frostbrand и подкласс праздничных — FestiveSwords)
    // под доктрину HardcoreMelee (МЕЧИ.md): взмах-дуга (ЛКМ), встреча (ПКМ тап),
    // тычки (ПКМ зажать), бросок (ПКМ+ЛКМ, остаётся в земле/цели, забирать самому).
    // Один источник правды: SwordPlayer, SwordGlobalItem, SwordUI берут отсюда.
    public static class SwordTypes
    {
        // Собственный ModItem: числовой тип известен только после регистрации
        // предмета, поэтому Айсберг сам записывает его сюда в SetDefaults.
        // До этого здесь 0, и проверки по нему просто ничего не находят.
        public static int AisbergType;

        public static bool IsAisbergId(int itemType) =>
            AisbergType > 0 && itemType == AisbergType;

        public static readonly int[] Swords = new int[]
        {
            ItemID.IceBlade,   // Ледяной клинок
            ItemID.Frostbrand, // Морозный клинок
            // Подкласс праздничных (база — FestiveSwords): шаблонные приёмы, без своих механик
            ItemID.CandyCaneSword,      // Карамельный меч (меч-леденец)
            ItemID.ChristmasTreeSword   // Меч из ёлки
        };

        public static bool IsSword(Item item) => item != null && !item.IsAir && IsSwordId(item.type);

        public static bool IsSwordId(int itemType)
        {
            if (IsAisbergId(itemType))
                return true;
            foreach (int type in Swords)
                if (type == itemType)
                    return true;
            return false;
        }

        // === Стихии (ФИЛОСОФИЯ.md) ===
        // Меч — «дуга и вес»: стихия взмах. Тычки и встреча возможны, но слабее.
        public static float SwingDamageMult(Item item) => 1f;

        public static float StabDamageMult(Item item) =>
            item != null && IsAisbergId(item.type) ? 0.8f : 0.85f;

        public static float SpearDamageMult(Item item) =>
            item != null && IsAisbergId(item.type) ? 1f : 0.8f;

        // Бросок мечей — как у палок, а не как у копья: крученый, в полёте
        // вертится, на земле ложится и settle-ится (SickleThrowProjectile).
        public static bool ThrownAlwaysTipFirst(int itemType) => false;

        public static float StabKnockbackMult(Item item) => 0.5f;

        // Меч короткий и быстрый: тычки резкие. Айсберг тяжёлый — без ускорения
        public static float StabTimeMult(Item item) =>
            item != null && IsAisbergId(item.type) ? 1f : 0.85f;
    }
}
