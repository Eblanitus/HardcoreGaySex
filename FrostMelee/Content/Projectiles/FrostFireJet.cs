using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    // Узкая струя холодного пламени из лезвия Фростбранда в стойке встречи
    // (FROSTBRAND_ИТОГ.md, п.3): держишь выставленным — жжёт, отпустил — гаснет.
    //
    // Струя живёт 16 тиков при 9 px/тик, то есть бьёт примерно на 140 px:
    // «работает только вблизи». Проходит сквозь врагов с локальным кулдауном,
    // гаснет на блоках. Каждый попадание поджигает холодным огнём.
    // Визуал — аддитивное свечение и факелы холодного пламени вдоль струи.
    public class FrostFireJet : ModProjectile
    {
        // Базовая текстура нужна только для загрузки типа; рисуем сами
        public override string Texture => "FrostMelee/Items/Frostbrand";

        public override void SetDefaults()
        {
            Projectile.width = 14;
            Projectile.height = 14;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.tileCollide = true;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 16;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 15;
        }

        public override void AI()
        {
            // След холодного пламени вдоль струи
            if (!Main.dedServ && Main.rand.NextBool(2))
            {
                Dust d = Dust.NewDustPerfect(Projectile.Center + Main.rand.NextVector2Circular(5f, 5f),
                    DustID.IceTorch, -Projectile.velocity * 0.15f + Main.rand.NextVector2Circular(0.5f, 0.5f),
                    100, default, Main.rand.NextFloat(0.9f, 1.6f));
                d.noGravity = true;
            }
            Lighting.AddLight(Projectile.Center, 0.2f, 0.45f, 0.6f);
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            Weapons.FrostFireSystem.Ignite(target, Projectile.owner);
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            // Уперлась в блок — гаснет с puff-ом
            for (int i = 0; i < 6; i++)
            {
                Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.IceTorch,
                    Main.rand.NextVector2Circular(2f, 2f), 100, default, 1.1f);
                d.noGravity = true;
            }
            return true;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow == null || glow.IsDisposed)
                return false;

            float fade = Projectile.timeLeft < 5 ? Projectile.timeLeft / 5f : 1f;
            Color c = new Color(140, 210, 255) * (0.7f * fade);
            Main.EntitySpriteDraw(glow, Projectile.Center - Main.screenPosition, null, c,
                0f, glow.Size() / 2f, 26f / glow.Width, SpriteEffects.None, 0f);
            return false;
        }
    }
}
