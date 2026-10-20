using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ID;
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
    // Урон и Slow наносит сам шип по видимому контуру спрайта,
    // волна (IceWaveProjectile) только несёт и расставляет шипы.
    public class IceWaveSpike : ModProjectile
    {
        public override string Texture => "FrostMelee/Textures/Effects/IceSpike0";

        private const int LifeTicks = 52;       // должно совпадать с timeLeft в SetDefaults
        private const int GrowTicks = 10;       // тиков на рост из земли
        private const int FadeTicks = 14;       // последние тики таяния
        private const float Tilt = 0.22f;       // наклон по ходу волны, рад
        private const float DamageMult = 0.35f; // волна слабая (как раньше у IceWaveProjectile)
        private const int SlowTicks = 60;       // 1 с замедления
        public const float SpawnKnockback = 6f; // шипы должны выталкивать врагов из роя

        private static Asset<Texture2D>[] stages;

        private static Asset<Texture2D>[] Stages => stages ??= new[]
        {
            ModContent.Request<Texture2D>("FrostMelee/Textures/Effects/IceSpike0-export"),
            ModContent.Request<Texture2D>("FrostMelee/Textures/Effects/IceSpike1-export"),
            ModContent.Request<Texture2D>("FrostMelee/Textures/Effects/IceSpike2-export"),
        };

        // Контур непрозрачных пикселей -export png в координатах холста
        // (x вправо, y вниз от левого верхнего угла) и полный размер холста.
        // Рендер идёт 1:1 с текстурой, поэтому хитбокс берёт те же пиксели.
        // Перерисуешь/обрежешь png — пересчитай контур, иначе хитбокс разойдётся.
        private static readonly Vector2[][] StageOutline =
        {
            // stage 0: 46x126, 57 vertices
            new Vector2[]
            {
                new Vector2(0f, 36f), new Vector2(6f, 42f), new Vector2(10f, 64f), new Vector2(12f, 64f),
                new Vector2(12f, 70f), new Vector2(16f, 74f), new Vector2(16f, 70f), new Vector2(20f, 68f),
                new Vector2(20f, 62f), new Vector2(22f, 62f), new Vector2(22f, 58f), new Vector2(24f, 58f),
                new Vector2(24f, 54f), new Vector2(26f, 54f), new Vector2(26f, 50f), new Vector2(28f, 50f),
                new Vector2(28f, 46f), new Vector2(30f, 46f), new Vector2(30f, 42f), new Vector2(34f, 38f),
                new Vector2(34f, 32f), new Vector2(38f, 30f), new Vector2(40f, 10f), new Vector2(42f, 10f),
                new Vector2(42f, 4f), new Vector2(46f, 0f), new Vector2(46f, 36f), new Vector2(44f, 36f),
                new Vector2(44f, 52f), new Vector2(42f, 52f), new Vector2(40f, 76f), new Vector2(38f, 76f),
                new Vector2(38f, 88f), new Vector2(36f, 88f), new Vector2(36f, 94f), new Vector2(42f, 90f),
                new Vector2(42f, 104f), new Vector2(40f, 104f), new Vector2(40f, 108f), new Vector2(38f, 108f),
                new Vector2(38f, 112f), new Vector2(36f, 112f), new Vector2(36f, 116f), new Vector2(34f, 116f),
                new Vector2(30f, 126f), new Vector2(16f, 126f), new Vector2(14f, 124f), new Vector2(14f, 118f),
                new Vector2(12f, 118f), new Vector2(12f, 112f), new Vector2(10f, 112f), new Vector2(10f, 104f),
                new Vector2(8f, 104f), new Vector2(6f, 86f), new Vector2(4f, 86f), new Vector2(2f, 64f),
                new Vector2(0f, 64f),
            },
            // stage 1: 40x60, 30 vertices
            new Vector2[]
            {
                new Vector2(0f, 38f), new Vector2(4f, 40f), new Vector2(6f, 46f), new Vector2(10f, 46f),
                new Vector2(8f, 12f), new Vector2(16f, 18f), new Vector2(16f, 22f), new Vector2(20f, 26f),
                new Vector2(20f, 22f), new Vector2(24f, 22f), new Vector2(24f, 18f), new Vector2(30f, 14f),
                new Vector2(30f, 10f), new Vector2(40f, 0f), new Vector2(40f, 30f), new Vector2(38f, 30f),
                new Vector2(38f, 38f), new Vector2(36f, 38f), new Vector2(36f, 46f), new Vector2(34f, 46f),
                new Vector2(34f, 50f), new Vector2(40f, 48f), new Vector2(40f, 52f), new Vector2(34f, 58f),
                new Vector2(30f, 58f), new Vector2(30f, 60f), new Vector2(10f, 60f), new Vector2(10f, 58f),
                new Vector2(6f, 58f), new Vector2(0f, 52f),
            },
            // stage 2: 42x78, 26 vertices
            new Vector2[]
            {
                new Vector2(10f, 30f), new Vector2(12f, 32f), new Vector2(16f, 30f), new Vector2(16f, 36f),
                new Vector2(18f, 36f), new Vector2(18f, 40f), new Vector2(22f, 44f), new Vector2(22f, 24f),
                new Vector2(28f, 18f), new Vector2(32f, 0f), new Vector2(36f, 4f), new Vector2(36f, 24f),
                new Vector2(42f, 18f), new Vector2(42f, 48f), new Vector2(40f, 48f), new Vector2(40f, 56f),
                new Vector2(38f, 56f), new Vector2(38f, 64f), new Vector2(42f, 64f), new Vector2(42f, 68f),
                new Vector2(32f, 78f), new Vector2(20f, 78f), new Vector2(16f, 68f), new Vector2(12f, 66f),
                new Vector2(12f, 60f), new Vector2(10f, 60f),
            },
        };

        private static readonly Point[] StageTexSize =
        {
            new Point(46, 126),
            new Point(40, 60),
            new Point(42, 78),
        };

        private const int MaxOutlinePoints = 96;
        private readonly Vector2[] worldOutline = new Vector2[MaxOutlinePoints];

        public override void SetDefaults()
        {
            Projectile.width = 2;
            Projectile.height = 2;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.penetrate = -1;
            Projectile.timeLeft = LifeTicks;
            Projectile.knockBack = SpawnKnockback;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1; // один шип бьёт каждую цель один раз
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

        private int Age => LifeTicks - Projectile.timeLeft;

        private float Grow
        {
            get
            {
                float t = MathHelper.Clamp(Age / (float)GrowTicks, 0f, 1f);
                return 1f - (1f - t) * (1f - t);
            }
        }

        private float SizeMul
        {
            get
            {
                float packed = Projectile.ai[2];
                return packed != 0f ? Math.Abs(packed) : 1f;
            }
        }

        private float VisualScale => Grow * (0.8f + Projectile.ai[0] * 0.4f) * SizeMul;

        private float TiltAngle => (Projectile.ai[2] < 0f ? -1f : 1f) * Tilt * Grow;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            if (!Projectile.friendly || Projectile.damage <= 0)
                return false;

            float scale = VisualScale;
            if (scale < 0.01f)
                return false;

            int stage = (int)Projectile.ai[1] % 3;
            if (stage < 0)
                stage += 3;
            Vector2[] outline = StageOutline[stage];
            Point tex = StageTexSize[stage];

            // Быстрый отсев: квадрат вокруг основания заведомо больше шипа
            float reach = Math.Max(tex.X, tex.Y) * scale + 4f;
            Rectangle broad = new Rectangle(
                (int)(Projectile.Center.X - reach), (int)(Projectile.Center.Y - reach),
                (int)(reach * 2f), (int)(reach * 2f));
            if (!broad.Intersects(targetHitbox))
                return false;

            // Контур в координатах текстуры, origin отрисовки — низ по центру холста.
            // Та же трансформация, что в PreDraw: масштаб, наклон, позиция.
            Vector2 origin = new Vector2(tex.X * 0.5f, tex.Y);
            float rot = TiltAngle;
            float cos = (float)Math.Cos(rot);
            float sin = (float)Math.Sin(rot);
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 p = (outline[i] - origin) * scale;
                worldOutline[i] = Projectile.Center + new Vector2(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos);
            }

            return PolygonIntersectsRect(worldOutline, outline.Length, targetHitbox);
        }

        // Контур вогнутый (между зубцами есть пустоты), поэтому SAT не подходит:
        // угол цели внутри контура, вершина контура внутри цели или пересечение рёбер.
        private static bool PolygonIntersectsRect(Vector2[] poly, int count, Rectangle rect)
        {
            Vector2[] box =
            {
                new Vector2(rect.Left, rect.Top),
                new Vector2(rect.Right, rect.Top),
                new Vector2(rect.Right, rect.Bottom),
                new Vector2(rect.Left, rect.Bottom),
            };

            for (int i = 0; i < 4; i++)
            {
                if (PointInPolygon(poly, count, box[i]))
                    return true;
            }

            for (int i = 0; i < count; i++)
            {
                if (PointInRect(poly[i], rect))
                    return true;
            }

            for (int i = 0; i < count; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % count];
                for (int j = 0; j < 4; j++)
                {
                    if (SegmentsIntersect(a, b, box[j], box[(j + 1) % 4]))
                        return true;
                }
            }

            return false;
        }

        private static bool PointInRect(Vector2 p, Rectangle rect)
        {
            return p.X >= rect.Left && p.X <= rect.Right && p.Y >= rect.Top && p.Y <= rect.Bottom;
        }

        private static bool PointInPolygon(Vector2[] poly, int count, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) &&
                    p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            const float eps = 0.001f;
            float d1 = Cross(b - a, c - a);
            float d2 = Cross(b - a, d - a);
            float d3 = Cross(d - c, a - c);
            float d4 = Cross(d - c, b - c);

            if (((d1 > eps && d2 < -eps) || (d1 < -eps && d2 > eps)) &&
                ((d3 > eps && d4 < -eps) || (d3 < -eps && d4 > eps)))
            {
                return true;
            }

            return (Math.Abs(d1) <= eps && OnSegment(a, b, c))
                || (Math.Abs(d2) <= eps && OnSegment(a, b, d))
                || (Math.Abs(d3) <= eps && OnSegment(c, d, a))
                || (Math.Abs(d4) <= eps && OnSegment(c, d, b));
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        private static bool OnSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            const float eps = 0.001f;
            return p.X >= Math.Min(a.X, b.X) - eps && p.X <= Math.Max(a.X, b.X) + eps
                && p.Y >= Math.Min(a.Y, b.Y) - eps && p.Y <= Math.Max(a.Y, b.Y) + eps;
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            modifiers.SourceDamage *= DamageMult;
            // Толкаем в сторону от шипа: рой иначе наносит слишком много мелких
            // ударов подряд, и ДПС раздувается на кадрах неуязвимости.
            modifiers.HitDirectionOverride = target.Center.X >= Projectile.Center.X ? 1 : -1;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            target.AddBuff(BuffID.Slow, SlowTicks);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Asset<Texture2D>[] st = Stages;
            if (!st[0].IsLoaded || !st[1].IsLoaded || !st[2].IsLoaded)
                return false;

            // Рост из земли: ease-out за 10 тиков
            float grow = Grow;

            // Личный размер шипа: ai[0] = разброс, ai[1] = какой из трёх спрайтов,
            // ai[2] = множитель дистанции (1 у меча, меньше к концу пробега),
            // знак = направление волны (для наклона).
            float scale = VisualScale;
            Texture2D tex = st[(int)Projectile.ai[1] % 3].Value;

            // Масштаб 1:1 с текстурой: какой высоты png нарисуешь
            // и запушишь, такой высоты сосулька и стоит в мире.
            // ai[0] даёт личный разброс, sizeMul — градиент от меча к хвосту.
            float rotation = TiltAngle;

            // Таяние: последние 14 тиков, нелинейно
            float fadeLin = Projectile.timeLeft < FadeTicks ? Projectile.timeLeft / (float)FadeTicks : 1f;
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
