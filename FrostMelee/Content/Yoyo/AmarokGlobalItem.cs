using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace FrostMelee.Content.Yoyo
{
    // Амарок бьёт нашим снарядом вместо ванильного (ЙОЙО.md):
    // отскок после удара, наводка, круг, нарастающий лёд.
    public class AmarokGlobalItem : GlobalItem
    {
        public override bool AppliesToEntity(Item entity, bool lateInstantiation) => entity.type == ItemID.Amarok;

        public override void SetDefaults(Item item)
        {
            item.shoot = ModContent.ProjectileType<AmarokYoyoProjectile>();
            // Звук выброса играем сами один раз при вылете (снаряд),
            // иначе ванила крутит его каждый круг удержания кнопки
            item.UseSound = null;
        }

        public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
        {
            if (!FrostMeleeConfigDefaults.ShowControlTooltips)
                return;
            tooltips.Add(new TooltipLine(Mod, "AmarokControls",
                Language.GetTextValue("Mods.FrostMelee.YoyoControlsTooltip")));
        }

        public override bool Shoot(Item item, Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            if (player.whoAmI != Main.myPlayer)
                return false;
            int ownType = ModContent.ProjectileType<AmarokYoyoProjectile>();
            if (player.ownedProjectileCounts[ownType] > 0)
                return false;

            Vector2 dir = (Main.MouseWorld - player.Center).SafeNormalize(Vector2.UnitX);
            Projectile.NewProjectile(source, player.Center, dir * 12f, ownType,
                damage, knockback, player.whoAmI);
            return false;
        }
    }
}
