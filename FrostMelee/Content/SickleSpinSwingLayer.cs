using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace FrostMelee.Content
{
    // Рисует серп в руке во время взмаха-раскрутки (360° вокруг персонажа).
    // Угол и центр вращения задаёт SicklePlayer.ApplySpinSwing (там же кривая
    // замах→разгон→торможение, синхронно с дугой урона). Ванильный спрайт
    // серпа в руке на это время спрятан (heldProj=-1), поэтому рисуем сами.
    // Слой сразу после предмета в руке: кисть руки закрывает хват.
    public class SickleSpinSwingLayer : PlayerDrawLayer
    {
        public override Position GetDefaultPosition() => new AfterParent(PlayerDrawLayers.HeldItem);

        // Свой игрок — по полям SicklePlayer. Чужой (мультиплеер) — по его дуге урона:
        // она синхронизирована и крутится тем же углом, что и серп.
        public override bool GetDefaultVisibility(PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            if (player.whoAmI == Main.myPlayer)
                return player.GetModPlayer<SicklePlayer>().SpinSwingVisible;
            return player.itemAnimation > 0 && FindArc(player) != null;
        }

        private static Projectile FindArc(Player player)
        {
            int arcType = ModContent.ProjectileType<Projectiles.SickleArcProjectile>();
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.type == arcType && p.owner == player.whoAmI)
                    return p;
            }
            return null;
        }

        protected override void Draw(ref PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            SicklePlayer sp = player.GetModPlayer<SicklePlayer>();
            int itemType = player.HeldItem.type;
            if (player.HeldItem.IsAir || !SickleTypes.IsSickle(player.HeldItem))
                return;

            float spinAngle = sp.SpinSwingAngle;
            Vector2 spinCenter = sp.SpinSwingCenter;
            float spinFace = sp.SpinSwingFace;
            if (player.whoAmI != Main.myPlayer)
            {
                Projectile arc = FindArc(player);
                if (arc == null)
                    return;
                spinAngle = arc.rotation - player.fullRotation;
                spinCenter = player.MountedCenter;
                spinFace = arc.ai[0] >= 0f ? 1f : -1f; // ai[0] = направление взмаха
            }

            Texture2D texture = SickleTextures.Normal(itemType);
            if (texture == null || texture.IsDisposed)
                return;

            // Геометрия спрайта.
            SickleSprite.Geometry geo = SickleSprite.Get(itemType);

            // СЕРЕДИНА серпа — в центре вращения (центр персонажа). Рукоять
            // отстоит на полдлины НАЗАД по dir, тогда середина древка в центре,
            // и серп крутится «сквозь» тело, а не торчит из центра в одну сторону.
            // Размер серпа — как у дуги урона (префиксы размера, SickleGlobalItem.Shoot)
            float scale = player.GetAdjustedItemScale(player.HeldItem);
            Vector2 dir = Vector2.UnitX.RotatedBy(spinAngle);
            float half = geo.Length * 0.5f * scale;
            Vector2 butt = spinCenter - dir * half + dir * SicklePlayer.SpinRadius;

            // Вогнутая (режущая) сторона у спрайта — со стороны против часовой от оси
            // приклад→острие (замер по пикселям IceSpearHeld), поэтому при
            // вращении ПРОТИВ часовой (влево, spinFace < 0) ведёт именно она. При вращении
            // ПО часовой (вправо) зеркалим спрайт, чтобы вогнутая сторона вела и там.
            // Вектор «рукоять → острие» берём по фактической геометрии с учётом зеркала.
            bool flip = spinFace > 0f;
            float rotation = spinAngle - geo.AngleFor(flip);
            Vector2 origin = geo.ButtFor(flip);
            SpriteEffects effects = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Color color = Lighting.GetColor((butt / 16f).ToPoint());

            FrozenSpear.AddBlend(ref drawInfo, texture, butt, geo, rotation, flip, color, scale,
                FrozenSpear.Mix(player, itemType));
        }
    }
}
