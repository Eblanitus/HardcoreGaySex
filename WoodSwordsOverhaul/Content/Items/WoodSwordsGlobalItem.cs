using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.Localization;
using Terraria.ModLoader;
using WoodSwordsOverhaul.Content.Projectiles;

namespace WoodSwordsOverhaul.Content.Items
{
	// GlobalItem добавляет дугу всему оружию серии (у заточек — взмах-полумесяц).
	// Что считается оружием серии — только StickTypes (единый источник правды).
	public class WoodSwordsGlobalItem : GlobalItem
	{
		// Длина клинка, при которой дуга имеет масштаб 1. Обычная палка 32px
		// (рукоять → остриё 44.5px) даёт прежний масштаб дуги 0.97.
		private const float ArcSizeLength = 45.9f;

		// Заточки: true — взмах-полумесяц (DaggerCrescentProjectile), false — обычная дуга как у палок.
		// Код полумесяца не удалён, пока просто выключен; вернуть — поставить true.
		public const bool UseDaggerCrescent = false;

		// Проверка через единый реестр (9 мечей + заточки)
		private bool IsWoodSword(Item item) => Content.StickTypes.IsWoodSword(item);

		// Применяем GlobalItem ко всем предметам, а уже внутри методов проверяем IsWoodSword.
		// Это надёжнее, чем фильтровать по type на ранней стадии и не зависеть от порядка инициализации.
		public override bool AppliesToEntity(Item entity, bool lateInstantiation) => true;

		// Добавляем снаряд дуги деревянным мечам
		public override void SetDefaults(Item item) {
			if (IsWoodSword(item)) {
				item.shoot = ModContent.ProjectileType<WoodSwordArcProjectile>();
				// noMelee не включаем, чтобы для игры предмет остался обычным мечом
				// (префиксы и пр.), а хитбокс меча убираем в UseItemHitbox: весь урон взмаха — дуга
				item.noMelee = false;
				item.shootsEveryUse = true; // Дуга каждый взмах
				// Палку во взмахе рисует StickSwingLayer — прикладом от кисти, углом
				// дуги (ванилла ставит предмет тремя ступенями по третям анимации,
				// на нашей кривой это прыжок рукояти на ~2/3 взмаха). Ванильную
				// картинку предмета в использовании скрываем у всей серии;
				// у заточки с полумесяцем клинок и так ведёт DaggerCrescentProjectile
				item.noUseGraphic = true;
			}
		}

		// Меч сам не бьёт: иначе он и дуга попадали по одной цели за взмах и DPS рос.
		// Взмах — одна дуга, урон которой зависит от радиуса (WoodSwordArcProjectile).
		public override void UseItemHitbox(Item item, Player player, ref Rectangle hitbox, ref bool noHitbox) {
			if (IsWoodSword(item))
				noHitbox = true;
		}

		// Подсказка по управлению приёмами — иначе игрок о них не узнает
		public override void ModifyTooltips(Item item, List<TooltipLine> tooltips) {
			if (!IsWoodSword(item) || !WoodSwordsConfigDefaults.ShowControlTooltips)
				return;
			tooltips.Add(new TooltipLine(Mod, "StickControls",
				Language.GetTextValue("Mods.WoodSwordsOverhaul.ControlsTooltip")));
		}

		// Создаём дугу с параметрами от меча (тип снаряда задаём явно — не зависим от SetDefaults)
		public override bool Shoot(Item item, Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback) {
			if (!IsWoodSword(item))
				return base.Shoot(item, player, source, position, velocity, type, damage, knockback);

			if (player.itemAnimationMax <= 0)
				return false;

			int arcType = ModContent.ProjectileType<WoodSwordArcProjectile>();
			float adjustedItemScale = player.GetAdjustedItemScale(item);
			// Дуга — единственный удар взмаха: полный урон взмаха (со стихией оружия)
			// и полное отбрасывание, без спада — хитбокс равен самой палке (BladeHit)
			int arcDamage = Math.Max(1, (int)Math.Round(damage * Content.StickTypes.SwingDamageMult(item)));

			// Размер дуги — от реальной длины клинка, а не от ширины картинки: у заточек
			// лезвие короче при той же 32px картинке, и дуга меньше. Длина — от рукояти
			// (во взмахе ванилла держит предмет за левый нижний угол спрайта; спрайты
			// серии квадратные) до острия по реальным пикселям (StickSprite).
			var sprite = Content.StickSprite.Get(item.type);
			float bladeLength = Vector2.Distance(new Vector2(0f, sprite.Width), sprite.Tip);

			// Заточка с полумесяцем (при UseDaggerCrescent): взмах-полумесяц вместо дуги, клинок в руке идёт по той же дуге. Направление
			// чередуется: чётный взмах сверху вниз, нечётный снизу вверх (зеркально при direction = -1).
			// Тот же урон взмаха (с множителем стихии), отбрасывание заметнее, одна цель — один удар.
			if (UseDaggerCrescent && Content.StickTypes.IsDagger(item)) {
				// Один полумесяц на игрока: если useTime короче взмаха и Shoot сработал снова в том же
				// взмахе — пропускаем; от прошлого взмаха (уже во второй половине жизни) — убираем
				int crescentType = ModContent.ProjectileType<DaggerCrescentProjectile>();
				for (int i = 0; i < Main.maxProjectiles; i++) {
					Projectile other = Main.projectile[i];
					if (!other.active || other.type != crescentType || other.owner != player.whoAmI)
						continue;
					if (other.localAI[0] < Math.Abs(other.ai[2]) * 0.5f)
						return false;
					other.Kill();
				}
				Vector2 aim = (Main.MouseWorld - player.MountedCenter).SafeNormalize(new Vector2(player.direction, 0f));
				float swingSign = (DaggerCrescentProjectile.TakeSwingDown() ? 1f : -1f) * player.direction * player.gravDir;
				Projectile.NewProjectile(
					source,
					player.MountedCenter,
					aim,
					ModContent.ProjectileType<DaggerCrescentProjectile>(),
					arcDamage,
					Math.Max(knockback * DaggerCrescentProjectile.KnockbackMult, DaggerCrescentProjectile.MinKnockback),
					player.whoAmI,
					item.type,
					adjustedItemScale,
					Math.Max(1, player.itemAnimationMax) * swingSign
				);
				return false;
			}

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
				Main.projectile[arcIndex].ModProjectile is WoodSwordArcProjectile arc)
				arc.BladeReach = bladeLength * adjustedItemScale + WoodSwordArcProjectile.HandReach;
			return false;
		}
	}
}
