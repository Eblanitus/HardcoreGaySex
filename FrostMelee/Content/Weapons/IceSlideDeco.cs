using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Декоративная «скользкая дорожка»: глянец льда на земле, который оставляет
    // за собой вытянутый вперёд меч, пока игрок скользит в стойке копья.
    // Чисто визуальный снаряд: без урона, без коллизий, живёт ~0.6 с и тает.
    //
    // Текстура — длинная лента AmarokIceStrip (44×504, вертикальная):
    // кладём её на бок поворотом на 90° и сплющиваем по толщине,
    // получается блестящая полоса на поверхности земли.
    public class IceSlideDeco : ModProjectile
    {
        public const int LifeTicks = 240; // 4 секунды: дорожка видна после проезда

        public override string Texture => "FrostMelee/Items/AmarokIceStrip";

        public override void SetDefaults()
        {
            Projectile.width = 8;
            Projectile.height = 8;
            Projectile.friendly = false;
            Projectile.hostile = false;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.penetrate = -1;
            Projectile.timeLeft = LifeTicks;
            Projectile.damage = 0;
        }

        public override void AI()
        {
            // Лежит на месте и тает
            Projectile.velocity = Vector2.Zero;
            // Тает плавно в последнюю секунду жизни
            if (Projectile.timeLeft < 60)
                Projectile.alpha = 255 - (int)(255f * Projectile.timeLeft / 60f);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = ModContent.Request<Texture2D>(Texture).Value;
            if (tex == null || tex.IsDisposed)
                return false;

            float fade = Projectile.timeLeft < 60 ? Projectile.timeLeft / 60f : 1f;
            // локаль: X = толщина ленты (44), Y = её длина (504);
            // после поворота на 90° длина ложится вдоль земли
            Vector2 scale = new Vector2(0.18f, 0.35f);
            Color c = new Color(190, 235, 255) * (0.5f * fade);
            Vector2 origin = new Vector2(tex.Width * 0.5f, tex.Height * 0.5f);

            Main.EntitySpriteDraw(tex, Projectile.Center - Main.screenPosition, null, c,
                MathHelper.PiOver2, origin, scale, SpriteEffects.None, 0f);
            return false;
        }
    }
}
