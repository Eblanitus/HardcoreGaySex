using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee
{
    /// <summary>
    /// Ice Sickle: Overhaul - Part of HardcoreMelee series.
    /// Полный набор приёмов доктрины (ФИЛОСОФИЯ.md):
    /// ЛКМ — ванильный взмах (дуга), ПКМ тап — копьё, ПКМ зажать — тычки,
    /// ПКМ+ЛКМ — бросок (серп остаётся в земле/цели, без автовозврата).
    /// </summary>
    public class FrostMelee : Mod
    {
        // Запасные ванильные текстуры серпов — чтобы вернуть при выгрузке мода
        private static readonly Dictionary<int, Asset<Texture2D>> originalTextures = new();

        // Отпечаток сборки: пишется в client.log при каждой загрузке мода.
        // По нему видно, дошли ли правки до игры: сверяй тег с последним коммитом.
        public const string BuildTag = "20261025-sword-stick-3";

        public override void Load()
        {
            Logger.Info($"[FrostMelee] загружена сборка {BuildTag}");
            // Жёстко подменяем текстуры ванильных серпов на наши спрайты.
            // БЕЗ этого в руке (взмах/тычок/копьё/бросок) и в геометрии
            // спрайта используется ВАНИЛЬНЫЙ серп: новый спрайт виден только
            // в инвентаре/на земле, а в бою — чёрный ванильный серп, и угол
            // поворота уезжает (атака «сверху вниз»), т.к. геометрия ванильной
            // картинки другая. Подмена делает TextureAssets.Item[type] нашим
            // спрайтом ВЕЗДЕ — как в WoodSwordsOverhaul.
            OverrideWeaponTextures();
        }

        // Weapon Out Lite (рисует оружие в покое в руке/за спиной): держать косы в
        // КОПЕЙНОЙ позе. Картинка *SpearHeld ориентирована как ванильный предмет-копьё
        // (древко внизу-слева, остриё вверху-справа) — то, что ждут его копейные позы.
        // Без жёсткой зависимости: мода нет или API другой — ничего не делаем.
        public override void PostSetupContent()
        {
            if (Main.dedServ || !ModLoader.TryGetMod("WeaponOutLite", out Mod weaponOut))
                return;
            foreach (int sickleId in Content.SickleTypes.Sickles)
            {
                try
                {
                    weaponOut.Call("RegisterSpear", sickleId);
                }
                catch (System.Exception ex)
                {
                    Logger.Warn($"Weapon Out Lite: не удалось задать копейную позу для {sickleId}: {ex.Message}");
                }
            }
        }

        public override void Unload()
        {
            foreach (var pair in originalTextures)
                TextureAssets.Item[pair.Key] = pair.Value;
            originalTextures.Clear();
            Content.SickleTextures.Unload();
            Content.Weapons.IceBladeVisual.Unload();
            // Кэш геометрии спрайтов снимаем при выгрузке (ссылки на ассеты)
            Content.SickleSprite.ClearCache();
        }

        private void OverrideWeaponTextures()
        {
            // На выделенном сервере текстур нет
            if (Main.dedServ)
                return;

            // В бою (взмах/тычок/копьё/бросок) оружие в руке = ДЛИННЫЙ спрайт,
            // через проверенный TextureAssets.Item. Инвентарь рисует короткий сам
            // (SickleGlobalItem.PreDrawInInventory).
            // *SpearHeld — те же *SpearProjectile, повёрнутые на 90° по часовой: древко
            // внизу-СЛЕВА, лезвие вверху-справа, как у ванильных картинок оружия. Так их
            // правильно держит Weapon Out Lite (он берёт TextureAssets.Item и считает
            // рукоять внизу-слева; с зеркальной картинкой коса висела в воздухе).
            // Наш код ориентации не зависит: приклад/остриё меряются по пикселям (SickleSprite).
            TryOverride(ItemID.IceSickle, "FrostMelee/Items/IceSpearHeld");

            // Ледяные мечи: свои спрайты в ВАНИЛЬНОЙ ориентации (рукоять внизу-слева,
            // остриё вверху-справа) — то, что ждут ваниль в руке, мерилка мечей
            // (SickleSprite.MeasureSword) и Weapon Out Lite. Один слот на всё:
            // инвентарь, взмах, тычки/встреча/бросок и геометрия дуги.
            TryOverride(ItemID.IceBlade, "FrostMelee/Items/Ice_Blade");
            Content.Weapons.IceBladeVisual.RegisterClean(TextureAssets.Item[ItemID.IceBlade]);
            TryOverride(ItemID.Frostbrand, "FrostMelee/Items/Frostbrand");
        }

        private void TryOverride(int itemId, string texturePath)
        {
            try
            {
                Asset<Texture2D> replacement = ModContent.Request<Texture2D>(texturePath);
                if (!originalTextures.ContainsKey(itemId))
                    originalTextures[itemId] = TextureAssets.Item[itemId];
                TextureAssets.Item[itemId] = replacement;
                Content.SickleTextures.Register(itemId, replacement);
            }
            catch (System.Exception ex)
            {
                // Если спрайт не найден — пишем в лог и не падаем
                Logger.Warn($"Не удалось подменить текстуру {texturePath}: {ex.Message}");
            }
        }
    }
}
