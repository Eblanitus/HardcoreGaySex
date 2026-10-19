using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    // Ледяной пик волны броска: ванильные спрайты шипов Deerclops
    // (кадры Deerclops_Ice_Spike.gif, IceSpike0/1/2). Как в ванили:
    // каждый шип берёт один из трёх спрайтов и свой личный размер,
    // растёт из земли ease-out и тает нелинейно.
    //
    // Основание ставится ТОЧНО на верхнюю кромку твёрдого тайла
    // (снап в AI на первом тике), поэтому шипы не висят на блок выше.
    // Урона и коллизий нет — весь урон и Slow даёт сама волна.
    public class IceWaveSpike : ModProjectile
    {
        public override string Texture => "FrostMelee/Textures/Effects/IceSpike0";

        private static Asset<Texture2D>[] stages;

        private static Asset<Texture2D>[] Stages => stages ??= new[]
        {
            ModContent.Request<Texture2D>("FrostMelee/Textures/Effects/IceSpike0"),
            ModContent.Request<Texture2D>("FrostMelee/Textures/Effects/IceSpike1"),
            ModContent.Request<Texture2D>("FrostMelee/Textures/Effects/IceSpike2"),
        };

        public override void SetDefaults()
        {
            Projectile.width = 2;
            Projectile.height = 2;
            Projectile.friendly = false;
            Projectile.hostile = false;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 52;
            Projectile.damage = 0;
        }

        public override void AI()
        {
            if (Projectile.localAI[0] == 0f)
            {
                Projectile.localAI[0] = 1f;

                // Снап основания к поверхности: вниз до твёрдого тайла,
                // основание шипа ставим ровно на его верхнюю кромку.
                // Земли в пределах 6 тайлов нет — шип не рождается в пустоте
                Point p = Projectile.Center.ToTileCoordinates();
                bool snapped = false;
                for (int i = 0; i < 6; i++)
                {
                    int y = p.Y + i;
                    if (!WorldGen.InWorld(p.X, y, 2))
                        break;

                    Tile t = Main.tile[p.X, y];
                    if (t.HasTile && Main.tileSolid[t.TileType])
                    {
                        Projectile.Center = new Vector2(Projectile.Center.X, y * 16f);
                        snapped = true;
                        break;
                    }
                }

                if (!snapped)
                {
                    Projectile.Kill();
                    return;
                }

                // Частицы у основания: выброс из земли в момент рождения
                for (int i = 0; i < 5; i++)
                {
                    float ang = -MathHelper.PiOver2 + Main.rand.NextFloat(-1.2f, 1.2f);
                    Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.SnowflakeIce,
                        ang.ToRotationVector2() * Main.rand.NextFloat(0.8f, 2.2f),
                        120, default, Main.rand.NextFloat(0.7f, 1.2f));
                    d.noGravity = true;
                }
                for (int i = 0; i < 3; i++)
                {
                    Dust d = Dust.NewDustPerfect(Projectile.Center + new Vector2(0f, 2f), DustID.Frost,
                        new Vector2(Main.rand.NextFloat(-1.5f, 1.5f), -0.3f), 110, default, 0.9f);
                    d.noGravity = true;
                }
            }

            Projectile.velocity = Vector2.Zero;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Asset<Texture2D>[] st = Stages;
            if (!st[0].IsLoaded || !st[1].IsLoaded || !st[2].IsLoaded)
                return false;

            int age = 52 - Projectile.timeLeft;

            // Рост из земли: ease-out за 10 тиков
            float grow = MathHelper.Clamp(age / 10f, 0f, 1f);
            grow = 1f - (1f - grow) * (1f - grow);

            // Личный размер шипа: ai[0] = разброс, ai[1] = какой из трёх спрайтов,
            // ai[2] = множитель дистанции (1 у меча, меньше к концу пробега),
            // ai[3] = направление волны (для наклона).
            // Спрайт 200 px высотой, базовый масштаб 0.16-0.30 даёт 32-60 px:
            // ростом с садового гнома, как в ванильной атаке Deerclops
            float sizeMul = Projectile.ai[2] > 0f ? Projectile.ai[2] : 1f;
            float scale = grow * (0.16f + Projectile.ai[0] * 0.14f) * sizeMul;
            Texture2D tex = st[(int)Projectile.ai[1] % 3].Value;

            // Наклон в сторону появления: шип заваливается по ходу волны,
            // наклон нарастает вместе с ростом
            float dir = Projectile.ai[3] != 0f ? Projectile.ai[3] : 1f;
            float rotation = dir * 0.22f * grow;

            // Таяние: последние 14 тиков, нелинейно
            float fadeLin = Projectile.timeLeft < 14 ? Projectile.timeLeft / 14f : 1f;
            float fade = fadeLin * fadeLin * (3f - 2f * fadeLin);

            Color light = Lighting.GetColor((int)Projectile.Center.X / 16, (int)Projectile.Center.Y / 16);
            Color c = new Color((byte)(light.R * 0.85f), (byte)(light.G * 0.95f), light.B,
                (byte)(255f * fade));
            Vector2 origin = new Vector2(tex.Width / 2f, tex.Height);
            Main.EntitySpriteDraw(tex, Projectile.Center - Main.screenPosition,
                null, c, rotation, origin, scale, SpriteEffects.None, 0f);
            return false;
        }
    }
}
