using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Yoyo
{
    // Осколок взрыва полного льда Амарока: разлетаются кругом,
    // бьют несильно, живут секунду, сквозь стены не летят, чуть тянет вниз.
    // Спрайт — Content/Yoyo/AmarokShardProjectile.png (16x8, смотрит вправо)
    public class AmarokShardProjectile : ModProjectile
    {
        public override void SetDefaults()
        {
            Projectile.width = 10;
            Projectile.height = 10;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = 1;
            Projectile.tileCollide = true;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 70;
            Projectile.aiStyle = -1;
        }

        public override void AI()
        {
            // Только визуал (ai[0]=1, напр. осколки раскола): не бьёт и не наносит урона
            if (Projectile.ai[0] == 1f)
            {
                Projectile.friendly = false;
                Projectile.damage = 0;
            }

            // Трей как у копья: множество частиц, откат назад по курсу,
            // долгие fadeIn. Лента инея тянется за летящей сосулькой
            Vector2 back = -Projectile.velocity * Main.rand.NextFloat(0.04f, 0.09f);
            Dust d1 = Dust.NewDustPerfect(Projectile.Center, DustID.Frost,
                back + Main.rand.NextVector2Circular(0.25f, 0.25f), 110, default, Main.rand.NextFloat(0.7f, 1f));
            d1.noGravity = true;
            d1.fadeIn = 0.4f;
            if (Main.rand.NextBool(2))
            {
                Dust d2 = Dust.NewDustPerfect(Projectile.Center + Main.rand.NextVector2Circular(4f, 4f),
                    DustID.SnowflakeIce, back * 0.6f, 150, default, Main.rand.NextFloat(0.4f, 0.7f));
                d2.noGravity = true;
                d2.fadeIn = 0.2f;
            }

            Projectile.velocity.Y = MathHelper.Min(Projectile.velocity.Y + 0.12f, 9f);
            Projectile.rotation += 0.5f * (Projectile.velocity.X >= 0f ? 1f : -1f);
            Lighting.AddLight(Projectile.Center, new Vector3(0.4f, 0.8f, 1.2f) * 0.6f);
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            // Визуальные осколки раскола живут тихо: без звука и без брызг
            if (Projectile.ai[0] == 1f)
                return true;
            SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.4f }, Projectile.Center);
            for (int i = 0; i < 4; i++)
            {
                Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.Frost,
                    Main.rand.NextVector2Circular(2f, 2f), 120, default, 0.9f);
                d.noGravity = true;
            }
            return true;
        }

        // Растворение в иней в конце жизни
        public override void OnKill(int timeLeft)
        {
            for (int i = 0; i < 3; i++)
            {
                Dust d = Dust.NewDustPerfect(Projectile.Center,
                    Main.rand.NextBool(2) ? DustID.SnowflakeIce : DustID.Frost,
                    Main.rand.NextVector2Circular(1f, 1f), 140, default, Main.rand.NextFloat(0.6f, 1f));
                d.noGravity = true;
            }
        }

        // Рисуем спрайт Content/Yoyo/AmarokShardProjectile.png: смотрит вправо,
        // в игре поворачивается по курсу
        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = TextureAssets.Projectile[Projectile.type].Value;
            if (tex == null || tex.IsDisposed)
                return false;
            Vector2 pos = Projectile.Center - Main.screenPosition;
            Vector2 origin = new Vector2(tex.Width * 0.5f, tex.Height * 0.5f);
            float rot = Projectile.velocity.SafeNormalize(Vector2.UnitX).ToRotation();
            Main.EntitySpriteDraw(tex, pos, null, lightColor, rot, origin, Projectile.scale, SpriteEffects.None, 0f);
            return false;
        }
    }
}
