using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace WoodSwordsOverhaul
{
    // Клиентские настройки интерфейса WoodSwordsOverhaul (как в FrostMelee).
    // Здесь прячем то, что изучившему игру больше не нужно шума:
    // надпись режима над головой и список приёмов в тултипах
    public class WoodSwordsConfig : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ClientSide;

        [Label("Показывать режим атаки над головой")]
        [Tooltip("Отключает надпись приёма (Взмах/Тычок/Копьё/Заточка/Брошено) над персонажем.")]
        [DefaultValue(true)]
        public bool ShowStanceIndicator { get; set; }

        [Label("Подсказки управления в описании")]
        [Tooltip("Отключает список приёмов в тултипе оружия.")]
        [DefaultValue(true)]
        public bool ShowControlTooltips { get; set; }
    }

    // Доступ из любых мест: до загрузки конфига и на выделенном сервере — включено
    public static class WoodSwordsConfigDefaults
    {
        public static bool ShowStanceIndicator =>
            ModContent.GetInstance<WoodSwordsConfig>()?.ShowStanceIndicator ?? true;

        public static bool ShowControlTooltips =>
            ModContent.GetInstance<WoodSwordsConfig>()?.ShowControlTooltips ?? true;
    }
}
