using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Клинок взмаха рисуется ОТ снаряда дуги, а не ванильной кривой.
    // Ваниль рисует меч в руке по своей кривой взмаха и своим часам,
    // дуга живёт по мастер-кривой и своим тикам: два независимых источника
    // угла всегда разъезжаются (три правки углов это не лечили).
    // Здесь на время взмаха ванильный предмет в руке прячется, а клинок
    // рисуется тем же Projectile.rotation, из которого рисуется голова
    // ленты: клинок и дуга — один угол одного снаряда, рассинхрон невозможен.
    public class SwingBladeLayer : PlayerDrawLayer
    {
        public override Position GetDefaultPosition() => new BeforeParent(PlayerDrawLayers.HeldItem);

        public override bool GetDefaultVisibility(PlayerDrawSet drawInfo)
        {
            Player p = drawInfo.drawPlayer;
            Projectile arc = p.GetModPlayer<SwordPlayer>().swingArc;
            // itemAnimation > 0: после конца анимации игра возвращает руку в
            // обычную позу (itemLocation ближе к телу) — клинок на такой кадр
            // прыгал рукояткой вглубь игрока, пока дуга ещё доживала свой тик
            return arc != null && arc.active && arc.owner == p.whoAmI
                && arc.type == ModContent.ProjectileType<SwordArcProjectile>()
                && (p.whoAmI != Main.myPlayer || p.itemAnimation > 0);
        }

        protected override void Draw(ref PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            Projectile arc = player.GetModPlayer<SwordPlayer>().swingArc;
            if (arc == null || !arc.active || arc.owner != player.whoAmI)
                return;

            Item held = player.HeldItem;
            if (held.IsAir || !SwordTypes.IsSword(held))
                return;

            // Ванильный use-график меча выключен (noUseGraphic в SwordGlobalItem),
            // клинок на взмахе рисуется только здесь, углом дуги

            Main.instance.LoadItem(held.type);
            Texture2D texture = TextureAssets.Item[held.type].Value;
            if (texture == null || texture.IsDisposed)
                return;

            SickleSprite.Geometry geo = SickleSprite.Get(held.type);
            bool flipped = player.direction == -1;
            Color light = Lighting.GetColor((int)player.itemLocation.X / 16, (int)player.itemLocation.Y / 16);
            // Якорь — приклад в кисти, остриё — по углу дуги: угол спрайта (приклад→остриё)
            // вычитаем, как во всех остальных отрисовках оружия (SickleBrace/Stab/SpinLayer)
            float drawRot = arc.rotation - geo.AngleFor(flipped);

            if (held.type == ItemID.IceBlade)
            {
                // Кроссфейд шершавый/отполированный для IceBlade
                IceBladeVisual.DrawBlend(texture, player.itemLocation, geo, drawRot, flipped, light, 1f,
                    IceBladeVisual.Mix(player, held.type));
            }
            else
            {
                Main.EntitySpriteDraw(texture, player.itemLocation - Main.screenPosition, null, light,
                    drawRot, geo.ButtFor(flipped), 1f,
                    flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
            }
        }
    }
}
