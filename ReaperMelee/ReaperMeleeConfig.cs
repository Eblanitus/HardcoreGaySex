using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace ReaperMelee
{
    // Клиентские настройки интерфейса ReaperMelee (как в FrostMelee).
    // Здесь прячем то, что изучившему игру больше не нужно шума:
    // надпись режима над головой и списки приёмов в тултипах
    public class ReaperMeleeConfig : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ClientSide;

        [Label("Показывать режим атаки над головой")]
        [Tooltip("Отключает надпись приёма (Взмах/Тычок/Копьё/Брошено) над персонажем.")]
        [DefaultValue(true)]
        public bool ShowStanceIndicator { get; set; }

        [Label("Подсказки управления в описании")]
        [Tooltip("Отключает списки приёмов в тултипах оружия.")]
        [DefaultValue(true)]
        public bool ShowControlTooltips { get; set; }
    }

    // Доступ из любых мест: до загрузки конфига и на выделенном сервере — включено
    public static class ReaperMeleeConfigDefaults
    {
        public static bool ShowStanceIndicator =>
            ModContent.GetInstance<ReaperMeleeConfig>()?.ShowStanceIndicator ?? true;

        public static bool ShowControlTooltips =>
            ModContent.GetInstance<ReaperMeleeConfig>()?.ShowControlTooltips ?? true;
    }
}
