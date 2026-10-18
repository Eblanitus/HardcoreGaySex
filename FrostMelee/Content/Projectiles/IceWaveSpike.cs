using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    // Ледяной пик волны броска: декорация в стиле атаки Deerclops, но маленькой.
    // Выстреливает из земли там, где прошла волна, растёт за ~0.17 с,
    // держится и тает. Урона и коллизий нет — весь урон даёт сама волна.
    //
    // Текстура IceSpike 12x22 (остриё вверх, основание вниз) — ЗАГЛУШКА,
    // игрок перерисует сам: FrostMelee/Textures/Effects/IceSpike.png.
    // Правила: любой размер, рисуется от нижнего центра вверх, RGBA,
    // прозрачность — канал альфа.
    public class IceWaveSpike : ModProjectile
    {
        public override string Texture => "FrostMelee/Textures/Effects/IceSpike";

        public override void SetDefaults()
        {
            Projectile.width = 2;
            Projectile.height = 2;
            Projectile.friendly = false;
            Projectile.hostile = false;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 46;
            Projectile.damage = 0;
        }

        public override void AI()
        {
            // Стоим в земле, растём и таем только в отрисовке
            Projectile.velocity = Vector2.Zero;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Asset<Texture2D> tex = ModContent.Request<Texture2D>(Texture);
            if (!tex.IsLoaded)
                return false;

            // Рост: ease-out за 10 тиков; ai[0] = случайный разброс размера
            float grow = MathHelper.Clamp((46 - Projectile.timeLeft) / 10f, 0f, 1f);
            grow = 1f - (1f - grow) * (1f - grow);
            float scale = grow * (0.8f + Projectile.ai[0] * 0.5f);

            // Таяние: последние 12 тиков, нелинейно
            float fadeLin = Projectile.timeLeft < 12 ? Projectile.timeLeft / 12f : 1f;
            float fade = fadeLin * fadeLin * (3f - 2f * fadeLin);

            Color light = Lighting.GetColor((int)Projectile.Center.X / 16, (int)Projectile.Center.Y / 16);
            Color c = new Color((byte)(light.R * 0.8f), (byte)(light.G * 0.92f), light.B,
                (byte)(235f * fade));
            Vector2 origin = new Vector2(tex.Value.Width / 2f, tex.Value.Height);
            Main.spriteBatch.Draw(tex.Value, Projectile.Center - Main.screenPosition,
                null, c, 0f, origin, scale, SpriteEffects.None, 0f);
            return false;
        }
    }
}
