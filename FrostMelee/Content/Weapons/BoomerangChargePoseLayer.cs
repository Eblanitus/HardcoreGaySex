using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons;

// Рисует бумеранг в руке при зарядке тяжёлого броска (ванилла предмет-«взмах»
// в покое не рисует). Позу и кисть задаёт BoomerangPlayer.ApplyChargePose
// (PoseItemVisible, PoseHandOffset, PoseItemDir). Слой сразу после предмета
// в руке, положение берётся от кисти каждый кадр. Копия SickleThrowPoseLayer;
// ледяного переката у бумеранга нет — mix 0.
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

		SickleSprite.Geometry geo = SickleSprite.Get(itemType);
		bool flipped = player.direction == -1;
		Vector2 dir = bp.PoseItemDir;
		Vector2 butt = player.Center + bp.PoseHandOffset - dir * BoomerangPlayer.ChargeGripInset;
		float rotation = dir.ToRotation() - geo.AngleFor(flipped);
		Color color = Lighting.GetColor((butt / 16f).ToPoint());

		FrozenSpear.AddBlend(ref drawInfo, texture, butt, geo, rotation, flipped, color, 1f, 0f);
	}
}
