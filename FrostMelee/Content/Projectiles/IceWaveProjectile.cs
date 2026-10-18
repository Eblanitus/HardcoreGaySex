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

            // Ледяные пики по ходу волны: атака как у Deerclops, но мелкая.
            // Шаг чаще у длинной волны убийства
            int spikeEvery = longWave ? 4 : 6;
            if ((int)Projectile.localAI[1] % spikeEvery == 0)
            {
                Vector2 sp = Projectile.Center + new Vector2(Main.rand.NextFloat(-6f, 6f), 8f);
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), sp, Vector2.Zero,
                    ModContent.ProjectileType<IceWaveSpike>(), 0, 0f, Projectile.owner,
                    Main.rand.NextFloat(1f), 0f);
            }

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

            // Граница пробега: ещё три пика подряд
            for (int i = 0; i < 3; i++)
            {
                Vector2 sp = Projectile.Center + new Vector2(dir * i * 10f, 8f);
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), sp, Vector2.Zero,
                    ModContent.ProjectileType<IceWaveSpike>(), 0, 0f, Projectile.owner,
                    Main.rand.NextFloat(1f), 0f);
            }
        }

        public override bool PreDraw(ref Color lightColor) => false; // визуал — пики и пыль
    }
}
