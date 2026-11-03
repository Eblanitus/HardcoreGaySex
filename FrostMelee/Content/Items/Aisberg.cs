using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Items
{
    // Айсберг: тяжёлый пост-Плантерный меч движка мечей.
    // Приёмы общие (SwordGlobalItem/SwordPlayer), своя механика — льдины:
    // откалываются только от прямого попадания взмахом и при втыкании броска,
    // ложатся на поле и сами не взрываются. Взмах отбивает их во врагов,
    // тычок и встреча раскалывают на месте (AisbergChunk).
    public class Aisberg : ModItem
    {
        // Спрайт меча — Iceberg.png (02.11.2026). Старый Aisberg.png не используется.
        public override string Texture => "FrostMelee/Content/Items/Iceberg";

        public override void SetStaticDefaults()
        {
            Weapons.SwordTypes.AisbergType = Type;
        }

        public override void SetDefaults()
        {
            // Движок мечей узнаёт собственный предмет только по этому типу:
            // числовой ID модового предмета появляется после регистрации.
            Weapons.SwordTypes.AisbergType = Type;

            Item.damage = 165;
            Item.DamageType = DamageClass.Melee;
            Item.width = 70;
            Item.height = 76;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.useTime = 34;
            Item.useAnimation = 34;
            Item.autoReuse = true;
            Item.knockBack = 8f;
            Item.UseSound = SoundID.Item1;
            Item.rare = ItemRarityID.Yellow;
            Item.value = Item.sellPrice(gold: 20);

            // То же ставит SwordGlobalItem всем мечам движка. Дублируем здесь,
            // чтобы предмет был целым сам по себе: дугу спавнит Shoot глобалки.
            Item.shoot = ModContent.ProjectileType<Projectiles.SickleBraceProjectile>();
            Item.shootsEveryUse = true;
            Item.noUseGraphic = true;
        }

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ItemID.IceBlade)
                .AddIngredient(ItemID.Frostbrand)
                .AddIngredient(ItemID.BrokenHeroSword)
                .AddTile(TileID.MythrilAnvil)
                .Register();
        }
    }
}
