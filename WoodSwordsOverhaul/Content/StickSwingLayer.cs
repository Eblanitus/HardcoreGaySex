using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace WoodSwordsOverhaul.Content
{
    // Палка во взмахе рисуется ОТ снаряда дуги, а не ванильной кривой (как
    // SwingBladeLayer у FrostMelee). Ванилла ставит предмет своим itemRotation
    // и тремя ступенями itemLocation по третям анимации; дуга живёт по нашей
    // кривой — два независимых источника всегда разъезжаются, а ступень на
    // ~2/3 взмаха роняет рукоять вниз скачком. Здесь ванильный предмет в
    // использовании скрыт (noUseGraphic в WoodSwordsGlobalItem), а палка
    // рисуется тем же Projectile.rotation, что и дуга, прикладом от кисти:
    // itemLocation ставит StickPlayer.PostUpdate по композитной руке вдоль
    // палки. Палка и дуга — один угол одного снаряда, рассинхрон невозможен.
    public class StickSwingLayer : PlayerDrawLayer
    {
        public override Position GetDefaultPosition() => new BeforeParent(PlayerDrawLayers.HeldItem);

        public override bool GetDefaultVisibility(PlayerDrawSet drawInfo)
        {
            Player p = drawInfo.drawPlayer;
            Projectile arc = p.GetModPlayer<StickPlayer>().swingArc;
            // itemAnimation > 0: после конца анимации игра возвращает руку в
            // обычную позу (itemLocation ближе к телу) — палка на такой кадр
            // прыгнула бы рукоятью вглубь игрока, пока дуга доживает свой тик
            return arc != null && arc.active && arc.owner == p.whoAmI
                && arc.type == ModContent.ProjectileType<Projectiles.WoodSwordArcProjectile>()
                && (p.whoAmI != Main.myPlayer || p.itemAnimation > 0);
        }

        protected override void Draw(ref PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            Projectile arc = player.GetModPlayer<StickPlayer>().swingArc;
            if (arc == null || !arc.active || arc.owner != player.whoAmI)
                return;

            Item held = player.HeldItem;
            if (held.IsAir || !StickTypes.IsWoodSword(held))
                return;

            Main.instance.LoadItem(held.type);
            Texture2D texture = TextureAssets.Item[held.type].Value;
            if (texture == null || texture.IsDisposed)
                return;

            StickSprite.Geometry geo = StickSprite.Get(held.type);
            bool flipped = player.direction == -1;
            Color light = Lighting.GetColor((player.itemLocation / 16f).ToPoint());
            // Якорь — приклад в кисти, остриё — по углу дуги: угол спрайта
            // (приклад→остриё) вычитаем, как во всех отрисовках оружия серии
            float drawRot = arc.rotation - geo.AngleFor(flipped);
            SpriteEffects effects = flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            drawInfo.DrawDataCache.Add(new DrawData(texture, player.itemLocation - Main.screenPosition, null,
                light, drawRot, geo.ButtFor(flipped), player.GetAdjustedItemScale(held), effects, 0));
        }
    }
}
