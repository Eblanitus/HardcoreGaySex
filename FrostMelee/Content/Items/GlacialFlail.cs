using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace FrostMelee.Content.Items
{
    // Ледниковый цеп: тяжёлая ледяная глыба на цепи. Главная сила — не бросок
    // на цепи, а раскрутка вплотную к врагам: чем дольше держишь, тем быстрее
    // глыба и тем больнее бьёт. Отпустил — глыба улетает к курсору слабее и
    // возвращается. Одна фишка: инерция раскрутки.
    public class GlacialFlail : ModItem
    {
        public const int DropDenominator = 40; // 1/40 с ледяной черепахи после первого мех-босса

        public override void SetDefaults()
        {
            Item.damage = 72;
            Item.DamageType = DamageClass.Melee;
            Item.width = 48;
            Item.height = 48;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.useTime = 45;
            Item.useAnimation = 45;
            Item.channel = true;
            Item.noMelee = true;
            Item.noUseGraphic = true;
            Item.knockBack = 8f;
            Item.shoot = ModContent.ProjectileType<Projectiles.GlacialFlailProjectile>();
            Item.shootSpeed = 14f;
            Item.UseSound = null; // звуки даёт снаряд: раскрутка, бросок, поимка
            Item.rare = ItemRarityID.Pink;
            Item.value = Item.sellPrice(gold: 6);
        }

        public override bool CanUseItem(Player player) =>
            player.ownedProjectileCounts[ModContent.ProjectileType<Projectiles.GlacialFlailProjectile>()] <= 0;

        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            if (!FrostMeleeConfigDefaults.ShowControlTooltips)
                return;
            tooltips.Add(new TooltipLine(Mod, "GlacialFlailControls",
                Language.GetTextValue("Mods.FrostMelee.GlacialFlailControlsTooltip")));
        }
    }

    // Дроп: ледяные черепахи живут с начала хардмода, но цеп падает только
    // после первого механического босса (дорожка в plans.md, этап 4).
    public class GlacialFlailDrops : GlobalNPC
    {
        public override void ModifyNPCLoot(NPC npc, NPCLoot npcLoot)
        {
            if (npc.type != NPCID.IceTortoise)
                return;
            npcLoot.Add(ItemDropRule.ByCondition(
                Condition.DownedMechBossAny.ToDropCondition(ShowItemDropInUI.Always),
                ModContent.ItemType<GlacialFlail>(), GlacialFlail.DropDenominator));
        }
    }
}
