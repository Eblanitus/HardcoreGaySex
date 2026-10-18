using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace WoodSwordsOverhaul.Content.Items.Daggers
{
    // Заточка: клонирует ванильный меч того же дерева и делается из его палки.
    // Все приёмы те же, что у палки, но стихия другая (см. StickTypes):
    // заточка сильнее и быстрее в тычках, слабее во взмахе и копье.
    // ЛКМ — обычный свинг (как ванильный меч, урон от самого удара).
    // ПКМ (см. StickPlayer / ФИЛОСОФИЯ.md):
    //   тап — копьё-выставление (фиксированный угол, как турнирное копьё)
    //   удержание — тычки кинжалом (спавн DaggerStabProjectile)
    //   ПКМ+ЛКМ — бросок
    public abstract class StickDagger : ModItem
    {
        // Ванильный меч (палка), с которого берём базовые характеристики и из которого точим
        protected abstract int CloneSource { get; }

        public override void SetDefaults()
        {
            // Ванильный меч того же дерева: свинг на ЛКМ — всё по умолчанию
            Item.CloneDefaults(CloneSource);
            Item.knockBack = MathHelper.Max(1f, Item.knockBack - 1f);
        }

        // Взмах заточкой слабее, чем палкой (стихия заточки — тычок)
        public override void ModifyHitNPC(Player player, NPC target, ref NPC.HitModifiers modifiers)
        {
            modifiers.SourceDamage *= StickTypes.SwingDamageMult(Item);
        }

        // Заточить палку: палка того же дерева → заточка. Обратно нельзя:
        // DisableDecraft закрывает разборку заточки в палку через Шиммер
        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(CloneSource)
                .AddTile(TileID.WorkBenches)
                .DisableDecraft()
                .Register();
        }
    }
}
