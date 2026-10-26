using System.ComponentModel;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace FrostMelee
{
    // Клиентские настройки интерфейса FrostMelee.
    // Здесь прячем то, что изучившему игру больше не нужно шума:
    // надпись режима над головой и списки приёмов в тултипах
    public class FrostMeleeConfig : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ClientSide;

        [Label("Показывать режим атаки над головой")]
        [Tooltip("Отключает надпись приёма (Взмах/Тычок/Копьё/Брошено/Точка/Наведён/Круг) над персонажем.")]
        [DefaultValue(true)]
        public bool ShowStanceIndicator { get; set; }

        [Label("Подсказки управления в описании")]
        [Tooltip("Отключает списки приёмов в тултипах оружия.")]
        [DefaultValue(true)]
        public bool ShowControlTooltips { get; set; }

        // Вид ледяного огня Frostbrand владелец крутит сам под свой вкус
        // (подобрать «ядерно/не ядерно» вслепую по скриншотам не вышло).
        // 100 — текущий настроенный вид; настройки только про картинку,
        // урон и поджог не трогают
        [Label("Ядрёность ледяного огня (%)")]
        [Tooltip("Сколько самого синего пламени Frostbrand и насколько оно горячее. 100 — как сейчас. Меньше — огня меньше, он холоднее и синее; больше — огня больше, он крупнее и ярче.")]
        [Range(0, 200)]
        [Increment(5)]
        [Slider]
        [DefaultValue(100)]
        public int FrostFireIntensity { get; set; } = 100;

        [Label("Тужость ледяного огня (%)")]
        [Tooltip("Насколько плотно пламя сидит на клинке. 100 — как сейчас. Больше — туже прилипает к лезвию, меньше плавает вокруг и отстаёт; меньше — свободнее и шире разлетается.")]
        [Range(20, 300)]
        [Increment(5)]
        [Slider]
        [DefaultValue(100)]
        public int FrostFireTightness { get; set; } = 100;
    }

    // Доступ из любых мест: до загрузки конфига и на выделенном сервере — включено
    public static class FrostMeleeConfigDefaults
    {
        public static bool ShowStanceIndicator =>
            ModContent.GetInstance<FrostMeleeConfig>()?.ShowStanceIndicator ?? true;

        public static bool ShowControlTooltips =>
            ModContent.GetInstance<FrostMeleeConfig>()?.ShowControlTooltips ?? true;

        // Ядрёность пламени: 1 — текущий вид (настройка 100). Работает НЕ
        // прозрачностью готовой картинки (замечание владельца), а самим
        // огнём: этот множитель — сколько частиц и языков рождается,
        // FireSizeScale — насколько они крупные, FireHeatScale — насколько
        // горячим (ярким и белым) рождается пламя и как долго держит жар
        public static float FireIntensityScale =>
            MathHelper.Clamp((ModContent.GetInstance<FrostMeleeConfig>()?.FrostFireIntensity ?? 100) / 100f, 0f, 2f);

        // Крупность пламени от ядрёности: корень, чтобы размер не улетал
        // следом за количеством (объём и так растёт от числа частиц)
        public static float FireSizeScale =>
            MathHelper.Clamp((float)System.Math.Sqrt(FireIntensityScale), 0.65f, 1.45f);

        // Жар пламени от ядрёности: 1 при настройке 100; ниже — пламя
        // холоднее и синее с рождения, выше — дольше держится белым жаром
        public static float FireHeatScale =>
            FireIntensityScale <= 0f ? 0f : MathHelper.Clamp(0.6f + 0.4f * FireIntensityScale, 0.6f, 1.4f);

        // Множитель тужости пламени: 1 — текущая посадка (настройка 100)
        public static float FireTightnessScale =>
            MathHelper.Clamp((ModContent.GetInstance<FrostMeleeConfig>()?.FrostFireTightness ?? 100) / 100f, 0.2f, 3f);
    }
}
