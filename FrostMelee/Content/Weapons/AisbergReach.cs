using Terraria;

namespace FrostMelee.Content.Weapons
{
    // Радиус атаки Айсберга (решение владельца 03.11.2026): больше, чем у
    // Терра-клинка, во всех режимах. Ванильная дуга Терра-клинка бьёт конусом
    // от центра игрока на 94 px × масштаб, масштаб растёт за взмах с 1 до 1,6:
    // от 94 до ~150 px. Клинок Айсберга (Iceberg.png) — ~99 px, поэтому сам
    // меч не раздуваем: дальше острия бьёт ЛЁД, и он нарисован —
    //   взмах  — ледяная плита дуги тянется от острия до радиуса (SwordArcProjectile);
    //   тычок  — ледяной шип с острия на время выпада (SwordStabProjectile);
    //   встреча — ледяной шип с острия, пока клинок выставлен (SickleBraceProjectile).
    // Хитбокс везде ровно там, где нарисован лёд. Радиусы — от кисти.
    // Всё умножается на масштаб оружия игрока, как у ванильной дуги.
    public static class AisbergReach
    {
        public const float Swing = 175f;
        public const float Stab = 175f;
        public const float Brace = 175f;

        // Шип начинается внутри кончика клинка на столько px: без щели между ними
        public const float SpikeOverlap = 8f;

        public static float Scale(Player player) =>
            player.HeldItem.IsAir ? 1f : player.GetAdjustedItemScale(player.HeldItem);

        // Длина шипа от его основания (SpikeOverlap внутри клинка) до радиуса
        public static float SpikeLength(Player player, float reach, float bladeLength) =>
            System.Math.Max(0f, reach * Scale(player) - bladeLength) + SpikeOverlap;
    }
}
