using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Рисует меч в руке на замахе броска (ванилла предмет-«взмах» в покое не рисует).
    // Копия SickleThrowPoseLayer: позу и кисть задаёт SwordPlayer.ApplyThrowPose
    // (PoseItemVisible, PoseHandOffset, PoseItemDir). Слой сразу после предмета в руке:
    // кисть закрывает хват, меч не отрывается от руки — положение берётся от кисти
    // каждый кадр. Рисуем ванильную текстуру предмета (SickleTextures.Normal даёт
    // оригинал, иней на мечах не растёт — FrozenSpear.Mix вернул бы 0).
    public class SwordThrowPoseLayer : PlayerDrawLayer
    {
        private const float GripInset = 4f; // сколько рукояти позади кисти, как в тычке

        public override Position GetDefaultPosition() => new AfterParent(PlayerDrawLayers.HeldItem);

        public override bool GetDefaultVisibility(PlayerDrawSet drawInfo) =>
            drawInfo.drawPlayer.whoAmI == Main.myPlayer &&
            drawInfo.drawPlayer.GetModPlayer<SwordPlayer>().PoseItemVisible;

        protected override void Draw(ref PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            SwordPlayer sp = player.GetModPlayer<SwordPlayer>();
            if (player.HeldItem.IsAir || !SwordTypes.IsSword(player.HeldItem))
                return;
            int itemType = player.HeldItem.type;
            Texture2D texture = SickleTextures.Normal(itemType);
            if (texture == null || texture.IsDisposed)
                return;

            SickleSprite.Geometry geo = SickleSprite.Get(itemType);
            bool flipped = player.direction == -1;
            Vector2 dir = sp.PoseItemDir;
            Vector2 butt = player.Center + sp.PoseHandOffset - dir * GripInset;
            float rotation = dir.ToRotation() - geo.AngleFor(flipped);
            SpriteEffects effects = flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Color color = Lighting.GetColor((butt / 16f).ToPoint());

            // Ледяного переката на мечах нет — микс всегда 0, рисуем обычный спрайт
            FrozenSpear.AddBlend(ref drawInfo, texture, butt, geo, rotation, flipped, color, 1f, 0f);
        }
    }
}
