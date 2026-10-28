using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using FrostMelee.Content.Projectiles.Prototypes;

namespace FrostMelee.Content.Items.Prototypes
{
    // Functional scaffolds, not final balance or unique mechanics. Keep them out
    // of AmarokPlayer/SwordPlayer until their own designs are implemented.
    public abstract class PrototypeWeapon : ModItem
    {
        public override string Texture => $"FrostMelee/Content/Items/Prototypes/{Name}";

        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            tooltips.Add(new TooltipLine(Mod, "Prototype", Language.GetTextValue("Mods.FrostMelee.PrototypeNotice")));
        }

        public override bool CanUseItem(Player player) => player.ownedProjectileCounts[Item.shoot] == 0;
    }

    public abstract class PrototypeYoyo : PrototypeWeapon
    {
        protected abstract int ProjectileType { get; }
        protected abstract int BaseDamage { get; }
        protected abstract int Rarity { get; }

        public override void SetStaticDefaults()
        {
            ItemID.Sets.Yoyo[Type] = true;
            ItemID.Sets.GamepadExtraRange[Type] = 15;
            ItemID.Sets.GamepadSmartQuickReach[Type] = true;
        }

        public override void SetDefaults()
        {
            Item.CloneDefaults(ItemID.Amarok);
            Item.width = Item.height = 24;
            Item.damage = BaseDamage;
            Item.rare = Rarity;
            Item.shoot = ProjectileType;
            Item.shootSpeed = 14f;
            Item.value = Item.sellPrice(silver: Rarity == ItemRarityID.White ? 20 : 500);
        }
    }

    public class FlinxYoyo : PrototypeYoyo
    {
        protected override int ProjectileType => ModContent.ProjectileType<FlinxYoyoProjectile>();
        protected override int BaseDamage => 12;
        protected override int Rarity => ItemRarityID.White;

        public override void AddRecipes() => CreateRecipe()
            .AddIngredient(ItemID.FlinxFur, 8).AddIngredient(ItemID.BorealWood, 12)
            .AddTile(TileID.WorkBenches).Register();
    }

    public class YetiYoyo : PrototypeYoyo
    {
        protected override int ProjectileType => ModContent.ProjectileType<YetiYoyoProjectile>();
        protected override int BaseDamage => 95;
        protected override int Rarity => ItemRarityID.Yellow;

        public override void AddRecipes() => CreateRecipe()
            .AddIngredient<FlinxYoyo>().AddIngredient<YetiPaw>()
            .AddIngredient(ItemID.Ectoplasm, 8).AddTile(TileID.MythrilAnvil).Register();
    }

    public abstract class PrototypeFlail : PrototypeWeapon
    {
        protected abstract int ProjectileType { get; }
        protected abstract int BaseDamage { get; }
        protected abstract int Rarity { get; }

        // SharedFlailPlayer owns input and spawning, not vanilla item use.
        public override bool CanUseItem(Player player) => false;

        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            base.ModifyTooltips(tooltips);
            if (FrostMeleeConfigDefaults.ShowControlTooltips)
                tooltips.Add(new TooltipLine(Mod, "SharedFlailControls", Language.GetTextValue("Mods.FrostMelee.SharedFlailControlsTooltip")));
        }

        public override void SetDefaults()
        {
            Item.CloneDefaults(ItemID.BallOHurt); // Keep the existing prototype's item stats.
            // ExampleAdvancedFlail uses MeleeNoSpeed, then scales projectile movement
            // explicitly. Avoid applying melee speed twice through item use timing.
            Item.DamageType = DamageClass.MeleeNoSpeed;
            Item.channel = false;
            Item.noMelee = true;
            Item.noUseGraphic = true;
            Item.width = Item.height = 32;
            Item.damage = BaseDamage;
            Item.rare = Rarity;
            Item.shoot = ProjectileType;
            Item.shootSpeed = 12f;
            Item.value = Item.sellPrice(silver: 80);
        }
    }

    public class SnowballFlail : PrototypeFlail
    {
        protected override int ProjectileType => ModContent.ProjectileType<SnowballFlailProjectile>();
        protected override int BaseDamage => 18;
        protected override int Rarity => ItemRarityID.Blue;

        public override void AddRecipes() => CreateRecipe()
            .AddIngredient(ItemID.SnowBlock, 40).AddIngredient(ItemID.IceBlock, 10)
            .AddIngredient(ItemID.Chain, 8).AddTile(TileID.Anvils).Register();
    }

    // Saved GlacialFlail items resolve to the replacement instead of UnloadedItem.
    [LegacyName("GlacialFlail")]
    public class PolarStar : PrototypeFlail
    {
        protected override int ProjectileType => ModContent.ProjectileType<PolarStarProjectile>();
        protected override int BaseDamage => 64;
        protected override int Rarity => ItemRarityID.Pink;

        public override void AddRecipes() => CreateRecipe()
            .AddIngredient(ItemID.FallenStar, 15).AddIngredient(ItemID.FrostCore)
            .AddIngredient(ItemID.SoulofLight, 10).AddIngredient(ItemID.Chain, 10)
            .AddTile(TileID.MythrilAnvil).Register();
    }

    public class LanternFlail : PrototypeFlail
    {
        protected override int ProjectileType => ModContent.ProjectileType<LanternFlailProjectile>();
        protected override int BaseDamage => 22;
        protected override int Rarity => ItemRarityID.Blue;

        public override void AddRecipes() => CreateRecipe()
            .AddIngredient(ItemID.Torch, 5).AddIngredient(ItemID.Chain, 8)
            .AddRecipeGroup(RecipeGroupID.IronBar, 10).AddTile(TileID.Anvils).Register();
    }

    // Formerly deferred concept: only a vanilla-style boomerang scaffold here.
    public class SnowBoomerang : PrototypeWeapon
    {
        public override void SetDefaults()
        {
            Item.CloneDefaults(ItemID.EnchantedBoomerang);
            Item.width = Item.height = 32;
            Item.damage = 80;
            Item.rare = ItemRarityID.Yellow;
            Item.shoot = ModContent.ProjectileType<SnowBoomerangProjectile>();
            Item.value = Item.sellPrice(gold: 5);
        }

        public override void AddRecipes() => CreateRecipe()
            .AddIngredient(ItemID.IceBoomerang).AddIngredient(ItemID.SnowBlock, 50)
            .AddIngredient(ItemID.Ectoplasm, 8).AddIngredient(ItemID.LihzahrdBrick, 20)
            .AddTile(TileID.MythrilAnvil).AddCondition(Condition.DownedGolem).Register();
    }

    public class YetiPaw : ModItem
    {
        public override string Texture => "FrostMelee/Content/Items/Prototypes/YetiPaw";

        public override void SetDefaults()
        {
            Item.width = Item.height = 24;
            Item.maxStack = Item.CommonMaxStack;
            Item.rare = ItemRarityID.Yellow;
            Item.value = Item.sellPrice(gold: 1);
        }
    }

    public class PrototypeMaterialDrops : GlobalNPC
    {
        public override void ModifyNPCLoot(NPC npc, NPCLoot npcLoot)
        {
            if (npc.type == NPCID.Yeti)
                npcLoot.Add(ItemDropRule.Common(ModContent.ItemType<YetiPaw>(), 4));
        }
    }

    // Reworks must stay on the existing vanilla items, never duplicate ModItems.
    public class UnfinishedVanillaReworks : GlobalItem
    {
        public override bool AppliesToEntity(Item item, bool lateInstantiation) =>
            item.type == ItemID.NorthPole || item.type == ItemID.ChristmasTreeSword;

        public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
        {
            string key = item.type == ItemID.NorthPole ? "NorthPolePrototypeNotice" : "TreeSwordPrototypeNotice";
            tooltips.Add(new TooltipLine(Mod, "Prototype", Language.GetTextValue("Mods.FrostMelee." + key)));
        }
    }
}
