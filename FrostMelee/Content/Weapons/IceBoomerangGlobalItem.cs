using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons;

// Ледяной бумеранг по boomer.md: ЛКМ держать — поток лёгких бросков (сами возвращаются),
// ПКМ-тап — удар в руке (ванильный взмах, хитбокс — по видимому спрайту), ПКМ-держать — зарядка
// с прицелом и тяжёлый бросок (отскакивает и падает — иди забирай). Броски и запрос
// взмаха — BoomerangPlayer; ванильный снаряд бумеранга отключён. Сквозь стены ничего.
public class IceBoomerangGlobalItem : GlobalItem
{
	public override bool AppliesToEntity(Item item, bool lateInstantiation) => item.type == ItemID.IceBoomerang;

	public override void SetDefaults(Item item)
	{
		item.damage = 22; // ванильный 17 + 30% ≈ 22 (база для удара и обоих бросков)
		item.useStyle = ItemUseStyleID.Swing;
		item.noMelee = false; // удар в руке бьёт по-настоящему
		item.noUseGraphic = false; // у ванильного бумеранга графика в руке скрыта — возвращаем
		item.shoot = ProjectileID.None; // броски — только наши (BoomerangPlayer)
		item.autoReuse = true;
	}

	public override bool CanUseItem(Item item, Player player)
	{
		// Ванильный взмах — только по запросу ПКМ-тапа. Обычный ЛКМ предмет не использует
		// (ЛКМ — это лёгкие броски), а брошенный тяжёлый — махать нечем.
		BoomerangPlayer bp = player.GetModPlayer<BoomerangPlayer>();
		return bp.SwingRequested && !bp.HeavyOut();
	}

	public override void UseItemHitbox(Item item, Player player, ref Rectangle hitbox, ref bool noHitbox)
	{
		if (noHitbox)
			return;

		// Ванильный хитбокс взмаха не учитывает поворот: это просто спрайт 18×32,
		// грубо расширенный по фазам анимации. У бумеранга из-за этого зона удара
		// уезжает от видимого спрайта. Строим прямоугольник по четырём углам
		// именно того кадра, который сейчас рисуется в руке: та же точка
		// itemLocation, тот же origin ванильной отрисовки, itemRotation и масштаб.
		const float spriteWidth = 18f;
		const float spriteHeight = 32f;
		float scale = player.GetAdjustedItemScale(item);
		Vector2 origin = new Vector2(
			spriteWidth * 0.5f - spriteWidth * 0.5f * player.direction,
			player.gravDir == -1f ? 0f : spriteHeight);

		float minX = float.MaxValue;
		float minY = float.MaxValue;
		float maxX = float.MinValue;
		float maxY = float.MinValue;

		for (int i = 0; i < 4; i++) {
			Vector2 corner = new Vector2(
				(i & 1) == 0 ? 0f : spriteWidth,
				(i & 2) == 0 ? 0f : spriteHeight);
			Vector2 point = player.itemLocation
				+ (corner - origin).RotatedBy(player.itemRotation) * scale;

			minX = MathF.Min(minX, point.X);
			minY = MathF.Min(minY, point.Y);
			maxX = MathF.Max(maxX, point.X);
			maxY = MathF.Max(maxY, point.Y);
		}

		int x = (int)MathF.Floor(minX);
		int y = (int)MathF.Floor(minY);
		hitbox = new Rectangle(
			x,
			y,
			Math.Max(1, (int)MathF.Ceiling(maxX) - x),
			Math.Max(1, (int)MathF.Ceiling(maxY) - y));
	}

	public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
	{
		Player player = Main.LocalPlayer;
		if (player != null && player.GetModPlayer<BoomerangPlayer>().HeavyOut())
			tooltips.Add(new TooltipLine(Mod, "ThrownBoomerang", Language.GetTextValue("Mods.FrostMelee.ThrownTooltipBoomerang")));
		if (FrostMeleeConfigDefaults.ShowControlTooltips)
			tooltips.Add(new TooltipLine(Mod, "BoomerangControls", Language.GetTextValue("Mods.FrostMelee.BoomerangControlsTooltip")));
	}
}
