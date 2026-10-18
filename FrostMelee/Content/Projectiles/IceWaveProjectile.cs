using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Audio;

namespace FrostMelee.Content.Projectiles
{
    // Волна льда по поверхности от броска ледяного клинка.
    // Идёт по земле как атака Deerclops, но сильно уменьшенная: короче, слабее,
    // урон треть от броска и ещё x0.35, плюс Slow на секунду.
    // При убийстве броском — длиннее и дольше (LongWaveTicks).
    //
    // Проработка эффекта: волна НЕ только пыль.
    // - За собой она намораживает настоящую ледяную глазурь на блоках
    //   (IceSheetSystem.MarkSurface) — атака буквально замораживает землю,
    //   след лежит 4 секунды, как от скольжения.
    // - Гребень читается тремя слоями пыли: куски льда вперёд,
    //   осколки вверх, снежинки назад.
    // - В конце жизни волна рассыпается веером осколков.
    public class IceWaveProjectile : ModProjectile
    {
        public override string Texture => "Terraria/Images/Projectile_0"; // невидимый, рисуем сами Dust

        private int maxTicks = 40;   // маленькая волна: 40 тиков * 4 px = ~160 px
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
                maxTicks = 150; // волна убийства: 150 тиков * 4 px = ~600 px, 2.5 с
                waveSpeed = 4f;
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

            // Волна намораживает глазурь за собой: земля замерзает по пути
            Point mark = Projectile.Center.ToTileCoordinates();
            Weapons.IceSheetSystem.MarkSurface(mark.X, mark.Y, Main.player[Projectile.owner]);
            Weapons.IceSheetSystem.MarkSurface(mark.X + (int)dir, mark.Y, Main.player[Projectile.owner]);

            // Гребень: куски льда вперёд по ходу
            float crest = longWave ? 1.7f : 1.3f;
            Dust ice = Dust.NewDustPerfect(Projectile.Center + new Vector2(dir * 10f, 6f), DustID.Ice,
                new Vector2(dir * Main.rand.NextFloat(1f, 2.2f), -Main.rand.NextFloat(0.8f, 1.6f)), 100, default, crest);
            ice.noGravity = true;

            // Осколки вверх
            if (Main.rand.NextBool(2)) {
                Dust d = Dust.NewDustPerfect(Projectile.Center + new Vector2(Main.rand.NextFloat(-8f, 8f), 8f), DustID.Frost,
                    new Vector2(dir * 0.3f, -Main.rand.NextFloat(1.2f, 2.2f)), 120, default, longWave ? 1.5f : 1.1f);
                d.noGravity = true;
            }

            // Снежинки назад, в след
            if (Main.rand.NextBool(3)) {
                Dust s = Dust.NewDustPerfect(Projectile.Center + new Vector2(-dir * 8f, 4f), DustID.SnowflakeIce,
                    new Vector2(-dir * Main.rand.NextFloat(0.4f, 1f), -0.4f), 130, default, 0.9f);
                s.noGravity = true;
            }

            // Лёгкий свет, у длинной волны ярче
            if (longWave)
                Lighting.AddLight(Projectile.Center, 0.15f, 0.5f, 0.75f);
            else
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

        public override void OnKill(int timeLeft)
        {
            // Конец волны: веер осколков, чтобы граница пробега читалась
            float dir = Projectile.ai[1] != 0 ? Projectile.ai[1] : 1f;
            for (int i = 0; i < 10; i++)
            {
                float ang = -MathHelper.PiOver2 + Main.rand.NextFloat(-0.9f, 0.9f);
                Dust d = Dust.NewDustPerfect(Projectile.Center + new Vector2(0f, 6f), DustID.Frost,
                    ang.ToRotationVector2() * Main.rand.NextFloat(1.5f, 3.5f) + new Vector2(dir * 0.5f, 0f),
                    110, default, Main.rand.NextFloat(1f, 1.6f));
                d.noGravity = true;
            }
        }

        // Гребень волны рисуем сами в ванильном батче снарядов: виден
        // гарантированно, пока снаряд жив, даже если пыль и отметки не читаются.
        // Растянутая глазурь IceCoat лежит на земле по ходу волны.
        public override bool PreDraw(ref Color lightColor)
        {
            var tex = Weapons.IceSheetSystem.CoatTex;
            bool longWave = Projectile.ai[0] > 0.5f;

            // Нелинейное появление и затухание гребня
            float appear = MathHelper.Clamp(Projectile.localAI[1] / 12f, 0f, 1f);
            appear = appear * appear * (3f - 2f * appear);
            float endFade = MathHelper.Clamp((maxTicks - Projectile.localAI[1]) / 20f, 0f, 1f);
            endFade = endFade * endFade * (3f - 2f * endFade);

            Color light = Lighting.GetColor((int)Projectile.Center.X / 16, (int)Projectile.Center.Y / 16);
            Color c = new Color((byte)(light.R * 0.75f), (byte)(light.G * 0.9f), light.B,
                (byte)(215f * appear * endFade));
            float wantWidth = longWave ? 64f : 48f;
            Vector2 pos = Projectile.Center + new Vector2(0f, 10f) - Main.screenPosition;

            if (tex.IsLoaded)
            {
                // Нормируем к нужной ширине гребня: текстура может быть любой
                Vector2 scale = new Vector2(wantWidth / tex.Value.Width, 2f);
                Vector2 origin = new Vector2(tex.Value.Width / 2f, tex.Value.Height);
                Main.spriteBatch.Draw(tex.Value, pos, null, c, 0f, origin, scale, SpriteEffects.None, 0f);
            }
            else
            {
                // Запасной гребень magic pixel, если текстура не загрузилась
                Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value,
                    new Rectangle((int)pos.X - (int)(wantWidth / 2f), (int)pos.Y - 8, (int)wantWidth, 8), c);
            }
            return false;
        }
    }
}
