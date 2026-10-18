using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Audio;

namespace FrostMelee.Content.Projectiles
{
    // Волна льда по поверхности от броска ледяного клинка.
    // Идёт по земле как атака Deerclops, но слабее. При убийстве броском — длиннее.
    public class IceWaveProjectile : ModProjectile
    {
        public override string Texture => "Terraria/Images/Projectile_0"; // невидимый, рисуем сами Dust

        private int maxTicks = 40; // маленькая волна ~40 тиков (~0.66с) = ~120px при скорости 3
        private float waveSpeed = 4f;

        public override void SetDefaults()
        {
            Projectile.width = 24;
            Projectile.height = 16;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 60;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 20;
            Projectile.aiStyle = -1;
        }

        public override void AI()
        {
            // ai[0] = longWave 0/1, ai[1] = dir X (-1/1)
            bool longWave = Projectile.ai[0] > 0.5f;
            if (longWave) {
                maxTicks = 110; // ~110 тиков = ~330px
                waveSpeed = 3.5f;
            }

            if (Projectile.localAI[0] == 0f) {
                Projectile.localAI[0] = 1f;
                Projectile.timeLeft = maxTicks + 10;
                // звук льда
                SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.5f, Pitch = 0.3f }, Projectile.Center);
            }

            Projectile.localAI[1]++;

            // Движение по горизонтали, прилипает к земле
            float dir = Projectile.ai[1] != 0 ? Projectile.ai[1] : Projectile.direction != 0 ? Projectile.direction : 1f;
            Projectile.velocity.X = dir * waveSpeed;
            // Гравитация к земле — ищем землю под собой
            Point tilePos = (Projectile.Center + new Vector2(0, 16)).ToTileCoordinates();
            // Если над пустым тайлом — падаем, если в тайле — поднимаемся
            if (!WorldGen.SolidTile(tilePos.X, tilePos.Y)) {
                Projectile.velocity.Y += 0.4f;
                if (Projectile.velocity.Y > 6f) Projectile.velocity.Y = 6f;
            } else {
                Projectile.velocity.Y = 0f;
                // чуть вверх чтобы не зарываться
                Projectile.Center = new Vector2(Projectile.Center.X, tilePos.Y * 16 - 18);
            }

            // Пыль волны
            if (Main.rand.NextBool(2)) {
                Dust d = Dust.NewDustPerfect(Projectile.Center + new Vector2(Main.rand.NextFloat(-8f,8f), 8f), DustID.Frost, new Vector2(dir * 0.3f, -0.6f), 120, default, 1.1f);
                d.noGravity = true;
            }
            // Лёгкий свет
            Lighting.AddLight(Projectile.Center, 0.1f, 0.4f, 0.6f);

            if (Projectile.localAI[1] > maxTicks) {
                Projectile.Kill();
            }
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            // чуть шире хитбокса для надёжности
            Rectangle r = projHitbox;
            r.Inflate(4, 4);
            return r.Intersects(targetHitbox);
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            // Эффект совсем маленький — только Slow на 1с, без урона бонуса
            modifiers.SourceDamage *= 0.35f; // волна слабая
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            target.AddBuff(BuffID.Slow, 60); // 1 сек замедления
        }

        public override bool PreDraw(ref Color lightColor) => false; // рисуем пылью, без спрайта
    }
}
