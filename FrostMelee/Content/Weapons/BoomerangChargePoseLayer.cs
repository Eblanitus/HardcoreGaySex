using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons;

// Рисует бумеранг в руке на замахах зарядки и лёгкого броска (ванилла
// предмет-«взмах» в покое не рисует). Кисть и угол задаёт BoomerangPlayer.
// Хват — строго ванильный для Swing: origin у нижнего края спрайта, флип по
// направлению и гравитации, масштаб GetAdjustedItemScale. Геометрию серпа
// сюда нельзя: она сажала бумеранг в руку верхним кончиком.
public class BoomerangChargePoseLayer : PlayerDrawLayer
{
	public override Position GetDefaultPosition() => new AfterParent(PlayerDrawLayers.HeldItem);

	public override bool GetDefaultVisibility(PlayerDrawSet drawInfo) =>
		drawInfo.drawPlayer.whoAmI == Main.myPlayer &&
		drawInfo.drawPlayer.GetModPlayer<BoomerangPlayer>().PoseItemVisible;

	protected override void Draw(ref PlayerDrawSet drawInfo)
	{
		Player player = drawInfo.drawPlayer;
		BoomerangPlayer bp = player.GetModPlayer<BoomerangPlayer>();
		if (player.HeldItem.IsAir || !BoomerangPlayer.IsBoomerang(player.HeldItem))
			return;
		int itemType = player.HeldItem.type;
		Texture2D texture = SickleTextures.Normal(itemType);
		if (texture == null || texture.IsDisposed)
			return;

		Vector2 hand = player.Center + bp.PoseHandOffset;
		Vector2 origin = new Vector2(
			texture.Width * 0.5f - texture.Width * 0.5f * player.direction,
			texture.Height);
		if (player.gravDir == -1f)
			origin.Y = texture.Height - origin.Y;

		SpriteEffects effects = SpriteEffects.None;
		if (player.direction == -1)
			effects |= SpriteEffects.FlipHorizontally;
		if (player.gravDir == -1f)
			effects |= SpriteEffects.FlipVertically;

		Color color = Lighting.GetColor((hand / 16f).ToPoint());
		drawInfo.DrawDataCache.Add(new DrawData(
			texture,
			hand - Main.screenPosition,
			null,
			player.HeldItem.GetAlpha(color),
			bp.PoseItemRotation,
			origin,
			player.GetAdjustedItemScale(player.HeldItem),
			effects,
			0));
	}
}
