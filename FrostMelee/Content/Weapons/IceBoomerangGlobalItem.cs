using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons;

// Ледяной бумеранг по boomer.md: ЛКМ держать — поток лёгких бросков (сами возвращаются),
// ПКМ-тап — удар в руке (ванильный взмах настоящим предметом), ПКМ-держать — зарядка
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

	public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
	{
		Player player = Main.LocalPlayer;
		if (player != null && player.GetModPlayer<BoomerangPlayer>().HeavyOut())
			tooltips.Add(new TooltipLine(Mod, "ThrownBoomerang", Language.GetTextValue("Mods.FrostMelee.ThrownTooltipBoomerang")));
		if (FrostMeleeConfigDefaults.ShowControlTooltips)
			tooltips.Add(new TooltipLine(Mod, "BoomerangControls", Language.GetTextValue("Mods.FrostMelee.BoomerangControlsTooltip")));
	}
}
