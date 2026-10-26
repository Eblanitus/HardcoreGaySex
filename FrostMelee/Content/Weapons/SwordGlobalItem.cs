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
            // Ванильный use-график меча выключен: клинок на взмахе рисует
            // SwingBladeLayer углом дуги (один источник угла — один клинок).
            // В покое предмет в руке рисует ваниль как обычно
            item.noUseGraphic = true;
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
            // Дуга — единственный удар взмаха: полный урон взмаха (со стихией меча),
            // без спада — хитбокс равен самому клинку (SwordArcProjectile.BladeHit)
            int arcDamage = Math.Max(1, (int)Math.Round(damage * SwordTypes.SwingDamageMult(item) * SwordPlayer.PolishDamageMult(player, item)));

            // Размер дуги — от реальной длины клинка (рукоять → остриё по пикселям;
            // для мечей замер меч-специфичный: остриё дальний от угла, см. SickleSprite).
            var sprite = SickleSprite.Get(item.type);
            float bladeLength = sprite.Length;

            float baseReach = bladeLength * adjustedItemScale + SwordArcProjectile.HandReach;

            float finalScale = adjustedItemScale * bladeLength / ArcSizeLength;

            int arcIndex = Projectile.NewProjectile(
                source,
                player.MountedCenter,
                new Vector2(player.direction, 0f),
                arcType,
                arcDamage,
                knockback * SwordPlayer.PolishKnockbackMult(player, item),
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

            // Фростбранд: вернувшиеся языки пламени висят у игрока огоньками
            // и уходят вместе со взмахом (бьют и поджигают — FrostFlameWisp)
            if (item.type == ItemID.Frostbrand)
                player.GetModPlayer<SwordPlayer>().LaunchOrbitingFlames();
            return false;
        }
    }
}
