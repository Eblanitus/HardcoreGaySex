using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using FrostMelee.Content.Projectiles;

namespace FrostMelee.Content.Items
{
    // Глобалка для серпов: ЛКМ остаётся ВАНИЛЬНЫМ взмахом (серп сам крутится),
    // но удар наносит дуга (SickleArcProjectile) — серп сам не бьёт, иначе
    // двойной урон за взмах. Остальные приёмы (тычки/копьё/бросок) живут в
    // SicklePlayer. Единый источник правды по списку — SickleTypes.
    public class SickleGlobalItem : GlobalItem
    {
        // Докуда достаёт клинок при масштабе дуги 1 (как у WoodSwords: клинок 45.9px + кисть 15px)
        private const float ArcReachAtScale1 = 61f;
        // Серп крутится СЕРЕДИНОЙ вокруг центра персонажа (SickleSpinSwingLayer),
        // поэтому лезвие достаёт на полдлины. Length меряет между центрами концов,
        // реальное острие дальше: по пикселям ~1.3 полудлины (лёд 70px, смерть 96px).
        public const float SpinTipReach = 1.3f;

        private bool IsSickle(Item item) => SickleTypes.IsSickle(item);

        public override bool AppliesToEntity(Item entity, bool lateInstantiation) => true;

        // Дуга каждый взмах. Стиль — ВАНИЛЬНЫЙ взмах (палка/серп крутится как меч):
        // доктрина «ЛКМ зажать = взмах» для всей мили. Ванильный снаряд серпа
        // заменён на дугу; хитбокс самого предмета убран в UseItemHitbox,
        // весь урон взмаха — дуга. noMelee/noUseGraphic выключены, чтобы
        // префиксы и ванильный спрайт во взмахе работали как обычно.
        public override void SetDefaults(Item item)
        {
            if (IsSickle(item))
            {
                // В игре предмет зовётся «Замороженное копьё», а не «Ледяная коса»
                if (item.type == ItemID.IceSickle)
                    item.SetNameOverride(Language.GetTextValue("Mods.FrostMelee.FrozenSpearName"));
                item.useStyle = ItemUseStyleID.Swing;
                item.UseSound = SoundID.Item1;
                // shoot — любой наш снаряд, лишь бы > 0 (иначе ванилла не зовёт Shoot);
                // дугу Shoot спавнит сам. Не дуга: Weapon Out Lite в копейной позе рисует
                // текстуру item.shoot, если она больше предмета, а у дуги это лист
                // Excalibur — в руке висела бы дуга. У SickleBraceProjectile картинка мелкая.
                item.shoot = ModContent.ProjectileType<SickleBraceProjectile>();
                item.noMelee = false;
                // Во время взмаха ванильный спрайт серпа в руке НЕ рисуем —
                // серп раскручивает наш слой SickleSpinSwingLayer (360° вокруг
                // персонажа). Иначе было бы ДВА серпа: ванильный свинг + наш слой.
                // На холостой позе (itemAnimation=0) предмет рисуется как обычно.
                item.noUseGraphic = true;
                item.shootsEveryUse = true;
                item.autoReuse = true;
            }
        }

        // Серп сам не бьёт: иначе он и дуга попадали по одной цели за взмах
        public override void UseItemHitbox(Item item, Player player, ref Rectangle hitbox, ref bool noHitbox)
        {
            if (IsSickle(item))
                noHitbox = true;
        }

        // Подсказка по управлению приёмами
        public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
        {
            if (!IsSickle(item) || !FrostMeleeConfigDefaults.ShowControlTooltips)
                return;
            tooltips.Add(new TooltipLine(Mod, "SickleControls",
                Language.GetTextValue(item.type == ItemID.NorthPole
                    ? "Mods.FrostMelee.BaseSpearControlsTooltip" : "Mods.FrostMelee.ControlsTooltip")));
        }

        // ИНВЕНТАРЬ: короткий спрайт в слоте. В бою оружие = длинный
        // (TextureAssets.Item подменён глобально на *SpearProjectile), а в слоте
        // должен быть короткий вид — рисуем его сами, вписав в 32×32.
        public override bool PreDrawInInventory(Item item, SpriteBatch spriteBatch, Vector2 position, Rectangle frame,
            Color drawColor, Color itemColor, Vector2 origin, float scale)
        {
            string shortPath = InventoryTexturePath(item.type);
            if (shortPath == null)
                return true;

            Texture2D texture = ModContent.Request<Texture2D>(shortPath).Value;
            if (texture == null || texture.IsDisposed)
                return true;

            float fitScale = 1f;
            int maxDim = Math.Max(texture.Width, texture.Height);
            if (maxDim > 32)
                fitScale = 32f / maxDim;
            fitScale *= Main.inventoryScale;

            // Замороженное копьё: иконка переходит в инвентарный вид по шкале заморозки игрока
            float mix = FrozenSpear.Mix(Main.LocalPlayer, item.type);
            Texture2D frozen = mix > 0.005f
                ? ModContent.Request<Texture2D>("FrostMelee/Items/IceSpeaFrozenUIr").Value
                : null;
            Vector2 drawOrigin = texture.Size() * 0.5f;
            spriteBatch.Draw(texture, position, null, frozen != null ? drawColor * (1f - mix) : drawColor, 0f, drawOrigin,
                fitScale, SpriteEffects.None, 0f);
            if (frozen != null)
            {
                float frozenFit = 32f / Math.Max(frozen.Width, frozen.Height) * Main.inventoryScale;
                spriteBatch.Draw(frozen, position, null, drawColor * mix, 0f, frozen.Size() * 0.5f, frozenFit,
                    SpriteEffects.None, 0f);
            }
            return false;
        }

        private static string InventoryTexturePath(int itemType)
        {
            if (itemType == ItemID.IceSickle) return "FrostMelee/Items/IceSpear";
            return null;
        }

        // Кастомные спрайты серпов теперь подменяются глобально в Mod.Load()
        // (OverrideSickleTextures), поэтому ручная отрисовка в мире/инвентаре
        // НЕ нужна — ванилла сама нарисует наш спрайт через TextureAssets.Item.

        // Создаём дугу с параметрами от серпа
        public override bool Shoot(Item item, Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            if (!IsSickle(item))
                return base.Shoot(item, player, source, position, velocity, type, damage, knockback);

            if (player.whoAmI != Main.myPlayer || player.itemAnimationMax <= 0)
                return false;

            int arcType = ModContent.ProjectileType<SickleArcProjectile>();
            float adjustedItemScale = player.GetAdjustedItemScale(item);
            // Полный урон взмаха (со стихией оружия)
            int arcDamage = Math.Max(1, (int)Math.Round(damage * SickleTypes.SwingDamageMult(item)));

            // Размер дуги — от видимого радиуса раскрутки серпа (не формула палок:
            // у серпа приклад внизу-справа, и расстояние от угла картинки не длина)
            var sprite = SickleSprite.Get(item.type);
            float bladeReach = sprite.Length * 0.5f * SpinTipReach * adjustedItemScale;

            float finalScale = bladeReach / ArcReachAtScale1;

            // Дуга стартует там же, где видимый серп: SicklePlayer.ApplySpinSwing
            // берёт стартовый угол на курсор, а дуга добавляет к углу velocity ещё π —
            // поэтому velocity смотрит ОТ курсора
            Vector2 aim = (Main.MouseWorld - player.MountedCenter).SafeNormalize(new Vector2(player.direction, 0f));

            int arcIndex = Projectile.NewProjectile(
                source,
                player.MountedCenter,
                -aim,
                arcType,
                arcDamage,
                knockback,
                player.whoAmI,
                player.direction * player.gravDir,
                Math.Max(1, player.itemAnimationMax),
                finalScale
            );
            // Докуда достаёт клинок — там полный урон
            if (arcIndex >= 0 && arcIndex < Main.maxProjectiles &&
                Main.projectile[arcIndex].ModProjectile is SickleArcProjectile arc)
            {
                arc.BladeReach = bladeReach;
                // Зона урона = видимое лезвие: нужен тот же серп, размер и сторона вращения,
                // что у SickleSpinSwingLayer (сторона — по direction, без gravDir, как у слоя)
                arc.ItemType = item.type;
                arc.ItemScale = adjustedItemScale;
                arc.SwingFace = player.direction >= 0 ? 1f : -1f;
                Main.projectile[arcIndex].netUpdate = true;
            }
            return false;
        }
    }
}
