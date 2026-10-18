using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.Localization;
using Terraria.ModLoader;
using FrostMelee.Content.Projectiles;
using Terraria.ID;

namespace FrostMelee.Content.Weapons
{
    // Глобалка для ледяных мечей (Ice Blade, Frostbrand): ЛКМ остаётся ВАНИЛЬНЫМ
    // взмахом (меч крутится как обычно), но удар наносит дуга (SwordArcProjectile) —
    // сам меч не бьёт, иначе двойной урон за взмах. Остальные приёмы (тычки/
    // встреча/бросок) живут в SwordPlayer. Единый источник правды — SwordTypes.
public class SwordGlobalItem : GlobalItem
{

        // Длина клинка, при которой дуга имеет масштаб 1 (как у палок WoodSwords:
        // палка 32px → рукоять-остриё 44.5px даёт масштаб ~0.97)
        private const float ArcSizeLength = 45.9f;

        public override bool AppliesToEntity(Item entity, bool lateInstantiation) => true;

        // Дуга каждый взмах. Стиль — ВАНИЛЬНЫЙ взмах: меч сам крутится,
        // ванильный снаряд (сосулька/снежок) заменён на дугу — весь урон взмаха
        // одна дуга. noUseGraphic не трогаем: в руке рисует ванилла, как у палок.
        public override void SetDefaults(Item item)
        {
            if (!SwordTypes.IsSword(item))
                return;
            item.useStyle = ItemUseStyleID.Swing;
            item.UseSound = SoundID.Item1;
            // shoot — любой наш снаряд, лишь бы > 0 (иначе ванилла не зовёт Shoot);
            // дугу Shoot спавнит сам. Не дуга: Weapon Out Lite в стойке встречи
            // рисует текстуру item.shoot, если она больше предмета — у дуги это
            // лист Excalibur, в руке висел бы. У SickleBraceProjectile картинка мелкая.
            item.shoot = ModContent.ProjectileType<SickleBraceProjectile>();
            item.noMelee = false;
            item.shootsEveryUse = true;
            item.autoReuse = true;
        }

        // Меч сам не бьёт: иначе он и дуга попадали по одной цели за взмах
        public override void UseItemHitbox(Item item, Player player, ref Rectangle hitbox, ref bool noHitbox)
        {
            if (SwordTypes.IsSword(item))
                noHitbox = true;
        }

        // Подсказка по управлению приёмами
        public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
        {
            if (!SwordTypes.IsSword(item))
                return;
            // Что делает лёд именно этого меча
            if (item.type == ItemID.IceBlade)
                tooltips.Add(new TooltipLine(Mod, "IceBladeKindle",
                    Language.GetTextValue("Mods.FrostMelee.IceBladeKindleTooltip")));
            else if (item.type == ItemID.Frostbrand)
                tooltips.Add(new TooltipLine(Mod, "FrostbrandKindle",
                    Language.GetTextValue("Mods.FrostMelee.FrostbrandKindleTooltip")));
            if (!FrostMeleeConfigDefaults.ShowControlTooltips)
                return;
            tooltips.Add(new TooltipLine(Mod, "SwordControls",
                Language.GetTextValue("Mods.FrostMelee.SwordControlsTooltip")));
        }

        // Создаём дугу с параметрами от меча
        public override bool Shoot(Item item, Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            if (!SwordTypes.IsSword(item))
                return base.Shoot(item, player, source, position, velocity, type, damage, knockback);

            if (player.itemAnimationMax <= 0)
                return false;

            int arcType = ModContent.ProjectileType<SwordArcProjectile>();
            float adjustedItemScale = player.GetAdjustedItemScale(item);
            // Дуга — единственный удар взмаха: полный урон взмаха (со стихией меча).
            // К краю дуги урон падает — см. SwordArcProjectile.ModifyHitNPC
            int arcDamage = Math.Max(1, (int)Math.Round(damage * SwordTypes.SwingDamageMult(item)));

            // Размер дуги — от реальной длины клинка (рукоять → остриё по пикселям;
            // для мечей замер меч-специфичный: остриё дальний от угла, см. SickleSprite).
            var sprite = SickleSprite.Get(item.type);
            float bladeLength = sprite.Length;

            // Frostbrand растёт от горящих: масштаб клинка уже увеличен (ModPlayer.ModifyItemScale),
            // поэтому дуга и зона полного урона считаются от подросшего меча сами.
            float baseReach = bladeLength * adjustedItemScale + SwordArcProjectile.HandReach;

            float finalScale = adjustedItemScale * bladeLength / ArcSizeLength;

            int arcIndex = Projectile.NewProjectile(
                source,
                player.MountedCenter,
                new Vector2(player.direction, 0f),
                arcType,
                arcDamage,
                knockback,
                player.whoAmI,
                player.direction * player.gravDir,
                Math.Max(1, player.itemAnimationMax),
                finalScale
            );
            // Докуда достаёт сам клинок — там полный урон
            if (arcIndex >= 0 && arcIndex < Main.maxProjectiles &&
                Main.projectile[arcIndex].ModProjectile is SwordArcProjectile arc) {
                arc.BladeReach = baseReach;
                // Кто взмахнул: от меча зависят ледяные свойства удара
                arc.SwordItem = item.type;
            }
            return false;
        }
    }
}
