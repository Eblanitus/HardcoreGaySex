using System.ComponentModel;
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
    }

    // Доступ из любых мест: до загрузки конфига и на выделенном сервере — включено
    public static class FrostMeleeConfigDefaults
    {
        public static bool ShowStanceIndicator =>
            ModContent.GetInstance<FrostMeleeConfig>()?.ShowStanceIndicator ?? true;

        public static bool ShowControlTooltips =>
            ModContent.GetInstance<FrostMeleeConfig>()?.ShowControlTooltips ?? true;
    }
}
