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

        // Количество живого ледяного огня Frostbrand владелец подобрал сам:
        // по умолчанию 50. Основное пламя уже нарисовано на текстуре самого
        // меча, поэтому эффект вокруг клинка должен быть еле заметным.
        // Настройка только про картинку: урон и поджог она не трогает
        [Label("Ядрёность ледяного огня (%)")]
        [Tooltip("Сколько самого синего пламени Frostbrand и насколько оно горячее. По умолчанию 50: основное пламя уже есть на самом мече, а живой огонь вокруг него должен быть еле заметным.")]
        [Range(0, 200)]
        [Increment(5)]
        [Slider]
        [DefaultValue(50)]
        public int FrostFireIntensity { get; set; } = 50;

        // Временная ручка подбора длины струи встречи. 100 — длина из
        // flame-25. Сразу стоит 60: ещё примерно на 40% короче, как
        // попросил владелец. Когда он выберет значение, его можно будет
        // вшить в код и убрать эту настройку.
        [Label("Длина струи встречи (%)")]
        [Tooltip("Временная настройка для подбора длины струи Frostbrand во встрече. 100 — прежняя длина, сейчас стоит 60 — примерно на 40% короче.")]
        [Range(20, 150)]
        [Increment(5)]
        [Slider]
        [DefaultValue(60)]
        public int FrostBraceJetLengthPercent { get; set; } = 60;
    }

    // Доступ из любых мест: до загрузки конфига и на выделенном сервере — включено
    public static class FrostMeleeConfigDefaults
    {
        public static bool ShowStanceIndicator =>
            ModContent.GetInstance<FrostMeleeConfig>()?.ShowStanceIndicator ?? true;

        public static bool ShowControlTooltips =>
            ModContent.GetInstance<FrostMeleeConfig>()?.ShowControlTooltips ?? true;

        // Ядрёность пламени: 1 — полная прежняя порция огня (настройка 100),
        // по умолчанию владелец выбрал 50 — половину. Работает НЕ
        // прозрачностью готовой картинки (замечание владельца), а самим
        // огнём: этот множитель — сколько частиц и языков рождается,
        // FireSizeScale — насколько они крупные, FireHeatScale — насколько
        // горячим (ярким и белым) рождается пламя и как долго держит жар
        public static float FireIntensityScale =>
            MathHelper.Clamp((ModContent.GetInstance<FrostMeleeConfig>()?.FrostFireIntensity ?? 50) / 100f, 0f, 2f);

        // Крупность пламени от ядрёности: корень, чтобы размер не улетал
        // следом за количеством (объём и так растёт от числа частиц)
        public static float FireSizeScale =>
            MathHelper.Clamp((float)System.Math.Sqrt(FireIntensityScale), 0.65f, 1.45f);

        // Жар пламени от ядрёности: 1 при настройке 100; ниже — пламя
        // холоднее и синее с рождения, выше — дольше держится белым жаром
        public static float FireHeatScale =>
            FireIntensityScale <= 0f ? 0f : MathHelper.Clamp(0.6f + 0.4f * FireIntensityScale, 0.6f, 1.4f);

        // Длина струи встречи относительно flame-25. По умолчанию 60%,
        // то есть ещё примерно на 40% короче. Если конфиг ещё не загружен,
        // используем то же значение, а не прежние 100%.
        public static float BraceJetLengthScale =>
            MathHelper.Clamp((ModContent.GetInstance<FrostMeleeConfig>()?.FrostBraceJetLengthPercent ?? 60) / 100f, 0.2f, 1.5f);
    }
}
