using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons;

// Ледяной бумеранг по boomer.md: ЛКМ — удар в руке (ванильный взмах настоящим предметом),
// ПКМ-тап — лёгкий бросок (сам возвращается), ПКМ-держать — зарядка с прицелом и
// тяжёлый бросок (втыкается — иди забирай). Броски спавнит BoomerangPlayer;
// ванильный снаряд бумеранга отключён. Сквозь стены не летит ничего.
public class IceBoomerangGlobalItem : GlobalItem
{
	public override bool AppliesToEntity(Item item, bool lateInstantiation) => item.type == ItemID.IceBoomerang;

	public override void SetDefaults(Item item)
	{
		item.useStyle = ItemUseStyleID.Swing;
		item.noMelee = false; // удар в руке бьёт по-настоящему
		item.shoot = ProjectileID.None; // броски — только наши, по ПКМ
		item.autoReuse = true;
	}

	public override bool CanUseItem(Item item, Player player)
	{
		// Брошенный бумеранг ещё в воздухе/воткнут — рукой махать нечем.
		BoomerangPlayer bp = player.GetModPlayer<BoomerangPlayer>();
		return !bp.Charging && !bp.BoomerangOut();
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
