using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.Graphics.CameraModifiers;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    // Зона инея от добивания врага при броске ледяного копья (SickleThrowProjectile.SpawnFrostZone).
    // Живёт отдельно от полос FrostTrailSystem (иней взмаха не трогаем): расходится заливкой по тайлам
    // от центра взрыва, только через пустые клетки в радиусе (сплошные блоки останавливают), ложится
    // по поверхностям (клетки у блоков гуще, в открытом воздухе реже). Взрыв: вспышка, ударное кольцо,
    // веер осколков, звук, тряска камеры, свет. Рисуется мягкими пятнами (градиентная текстура
    // 24x24 генерируется в коде, без ресурсов), в пиксельном стиле. Slow держится, пока цель в зоне.
    public class FrostZoneSystem : ModSystem
    {
        // --- рост взрыва ---
        private const int GrowTicks = 12;           // фронт доходит до края за столько тиков
        private const float GrowSharpness = 4f;     // ease-out (1-t)^N: почти весь радиус за первые ~6 тиков
        // --- пятна инея ---
        private const int BlobTexSize = 24;
        private const float BlobAlpha = 0.5f;       // прозрачность одного пятна (они перекрываются)
        private const float BlobSize = 30f;         // диаметр пятна, px
        private const float StrongAlphaMult = 1.3f;
        private const float AirWeight = 0.55f;      // плотность пятен в открытом воздухе против поверхности
        private const float AirChance = 0.3f;       // шанс пятна в клетке без соседних блоков
        private const int SurfaceBlobs = 3;         // пятен на клетке у блока
        private const float SurfaceHug = 7f;        // насколько пятна жмутся к блоку, px
        private const float EdgeSparse = 0.5f;      // у края пятна реже (доля вероятности пропуска у самого края)
        // --- взрыв ---
        private const int FlashTicks = 7;
        private const int RingTicks = 16;
        private const float ShakeBase = 5f;         // сила тряски камеры: Base + PerSize * (радиус / 192)
        private const float ShakePerSize = 9f;
        private const int FanBase = 36;             // осколков веера: Base + PerSize * (радиус / 192)
        private const int FanPerSize = 40;
        private const float SparkPerTick = 3f;      // искр иней-зоны за тик (сильный x2)
        private const int SlowCheckEvery = 3;

        private class Zone
        {
            public readonly List<Vector2> Pos = new List<Vector2>();
            public readonly List<int> Born = new List<int>();
            public readonly List<int> Seed = new List<int>();
            public readonly List<float> Edge = new List<float>();   // 0 центр .. 1 край
            public readonly List<float> Weight = new List<float>(); // плотность: поверхность 1, воздух AirWeight
            public readonly HashSet<Point> Cells = new HashSet<Point>();
            public readonly HashSet<int> Burned = new HashSet<int>();
            public Vector2 Center;
            public float Radius;
            public int Tick, Life;
            public bool Strong, Mine;
        }

        private static readonly List<Zone> zones = new List<Zone>();
        private static Texture2D blob;

        // Общий стиль инея: мягкое пятно и цвета (их же берёт FrostTrailSystem для полос взмаха и броска)
        internal static readonly Color WeakBody = new Color(160, 215, 248);
        internal static readonly Color StrongBody = new Color(90, 200, 255);
        internal static Texture2D BlobTexture() => Main.dedServ ? null : GetBlob();

        public override void OnWorldUnload()
        {
            zones.Clear();
        }

        public override void Unload()
        {
            zones.Clear();
            Main.QueueMainThreadAction(() =>
            {
                blob?.Dispose();
                blob = null;
            });
        }

        // Рост радиуса: 0..1 → 0..1, резкий ease-out
        private static float EaseOut(float t) => 1f - (float)Math.Pow(1f - MathHelper.Clamp(t, 0f, 1f), GrowSharpness);

        private static bool Solid(int x, int y) => !WorldGen.InWorld(x, y, 2) || WorldGen.SolidTile(x, y);

        public static void Spawn(Vector2 center, float radius, int lifeTicks, bool strong, bool mine)
        {
            Point start = center.ToTileCoordinates();
            if (Solid(start.X, start.Y))
            {
                bool found = false;
                for (int dx = -1; dx <= 1 && !found; dx++)
                    for (int dy = -1; dy <= 1 && !found; dy++)
                        if (!Solid(start.X + dx, start.Y + dy))
                        {
                            start = new Point(start.X + dx, start.Y + dy);
                            found = true;
                        }
                if (!found)
                    return;
            }

            // Заливка по пустым клеткам (8 соседей, по диагонали не пролезаем между двумя блоками)
            var dist = new Dictionary<Point, float> { [start] = 0f };
            var queue = new Queue<Point>();
            queue.Enqueue(start);
            int guard = 0;
            while (queue.Count > 0 && guard++ < 6000)
            {
                Point p = queue.Dequeue();
                float pd = dist[p];
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;
                        bool diag = dx != 0 && dy != 0;
                        if (diag && Solid(p.X + dx, p.Y) && Solid(p.X, p.Y + dy))
                            continue;
                        Point n = new Point(p.X + dx, p.Y + dy);
                        if (Solid(n.X, n.Y) || Vector2.Distance(n.ToWorldCoordinates(8f, 8f), center) > radius)
                            continue;
                        float nd = pd + (diag ? 1.414f : 1f);
                        if (dist.TryGetValue(n, out float old) && old <= nd)
                            continue;
                        dist[n] = nd;
                        queue.Enqueue(n);
                    }
                }
            }

            var zone = new Zone { Center = center, Radius = radius, Life = Math.Max(30, lifeTicks), Strong = strong, Mine = mine };
            var items = new List<(int born, Vector2 pos, int seed, float edge, float weight)>();
            float radiusTiles = Math.Max(1f, radius / 16f);
            foreach (var kv in dist)
            {
                Point c = kv.Key;
                zone.Cells.Add(c);
                Vector2 cc = c.ToWorldCoordinates(8f, 8f);
                float u = MathHelper.Clamp(Vector2.Distance(cc, center) / radius, 0f, 1f);
                int solidN = 0;
                Vector2 toSolid = Vector2.Zero;
                if (Solid(c.X - 1, c.Y)) { solidN++; toSolid.X -= 1f; }
                if (Solid(c.X + 1, c.Y)) { solidN++; toSolid.X += 1f; }
                if (Solid(c.X, c.Y - 1)) { solidN++; toSolid.Y -= 1f; }
                if (Solid(c.X, c.Y + 1)) { solidN++; toSolid.Y += 1f; }
                int blobs;
                float weight;
                if (solidN > 0)
                {
                    blobs = SurfaceBlobs;
                    weight = 1f;
                }
                else
                {
                    blobs = Main.rand.NextFloat() < AirChance ? 1 : 0;
                    weight = AirWeight;
                }
                float front = MathHelper.Clamp(kv.Value / radiusTiles, 0f, 1f);
                int bornBase = (int)(GrowTicks * (1f - (float)Math.Pow(1f - front, 1f / GrowSharpness)));
                Vector2 hug = toSolid == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(toSolid);
                for (int b = 0; b < blobs; b++)
                {
                    if (Main.rand.NextFloat() < u * u * EdgeSparse)
                        continue;
                    Vector2 pos = cc + Main.rand.NextVector2Circular(8f, 8f) + hug * Main.rand.NextFloat(2f, SurfaceHug);
                    items.Add((bornBase + Main.rand.Next(2), pos, Main.rand.Next(), u, weight));
                }
            }
            items.Sort((a, b) => a.born.CompareTo(b.born));
            foreach (var it in items)
            {
                zone.Born.Add(it.born);
                zone.Pos.Add(it.pos);
                zone.Seed.Add(it.seed);
                zone.Edge.Add(it.edge);
                zone.Weight.Add(it.weight);
            }
            zones.Add(zone);

            if (!Main.dedServ)
                Explode(zone, strong);
        }

        // Взрыв: звук, тряска, веер осколков (не в стены)
        private static void Explode(Zone zone, bool strong)
        {
            Vector2 c = zone.Center;
            float size = zone.Radius / 192f;
            SoundEngine.PlaySound(SoundID.Item27 with { Volume = 1f, Pitch = -0.3f }, c);
            SoundEngine.PlaySound(SoundID.Item30 with { Volume = 0.8f, Pitch = -0.6f }, c);
            Main.instance.CameraModifiers.Add(new PunchCameraModifier(c, Main.rand.NextVector2Unit(),
                ShakeBase + ShakePerSize * size, 9f, 24, 1200f, "IceZoneBlast"));

            int fan = FanBase + (int)(FanPerSize * size);
            for (int i = 0; i < fan; i++)
            {
                float ang = (i + Main.rand.NextFloat()) / fan * MathHelper.TwoPi;
                Vector2 dir = ang.ToRotationVector2();
                float free = FreeDistance(c, dir, 96f);
                if (free < 24f)
                    continue;
                float speed = Math.Min(Main.rand.NextFloat(5f, 15f), free * 0.12f);
                int type = Main.rand.NextBool(3) ? DustID.SnowflakeIce : (Main.rand.NextBool() ? DustID.IceTorch : DustID.Frost);
                Dust d = Dust.NewDustPerfect(c + dir * 6f, type, dir * speed, 100, default, Main.rand.NextFloat(1.1f, 1.9f));
                d.noGravity = true;
            }
            for (int i = 0; i < 14; i++)
            {
                Vector2 dir = Main.rand.NextVector2Unit();
                float free = FreeDistance(c, dir, 64f);
                if (free < 16f)
                    continue;
                // осколки льда падают дугой
                Dust d = Dust.NewDustPerfect(c, DustID.Ice, dir * Math.Min(Main.rand.NextFloat(3f, 8f), free * 0.1f) + new Vector2(0f, -2f),
                    0, default, Main.rand.NextFloat(1.1f, 1.6f));
                d.noGravity = false;
            }
        }

        // Сколько px от точки по направлению до первого сплошного блока (до max)
        private static float FreeDistance(Vector2 from, Vector2 dir, float max)
        {
            for (float d = 8f; d <= max; d += 8f)
            {
                Point t = (from + dir * d).ToTileCoordinates();
                if (Solid(t.X, t.Y))
                    return d - 8f;
            }
            return max;
        }

        public override void PostUpdateEverything()
        {
            if (zones.Count == 0)
                return;
            for (int zi = zones.Count - 1; zi >= 0; zi--)
            {
                Zone z = zones[zi];
                z.Tick++;
                if (z.Tick >= z.Life + GrowTicks + 2)
                {
                    zones.RemoveAt(zi);
                    continue;
                }

                if (!Main.dedServ)
                {
                    // свет взрыва: яркий в начале, гаснет
                    if (z.Tick <= RingTicks)
                    {
                        float k = 1f - z.Tick / (float)RingTicks;
                        Lighting.AddLight(z.Center, 0.7f * k, 1.0f * k, 1.3f * k);
                    }
                    SpawnSparks(z);
                }
                if (z.Mine && z.Tick % SlowCheckEvery == 0)
                    SlowNpcs(z);
            }
        }

        private static void SpawnSparks(Zone z)
        {
            if (z.Pos.Count == 0 || z.Tick > z.Life)
                return;
            float fade = 1f - z.Tick / (float)z.Life;
            float want = SparkPerTick * (z.Strong ? 2f : 1f) * fade;
            int n = (int)want + (Main.rand.NextFloat() < want - (int)want ? 1 : 0);
            for (int k = 0; k < n; k++)
            {
                int i = Main.rand.Next(z.Pos.Count);
                if (z.Tick < z.Born[i])
                    continue;
                int type = Main.rand.NextBool(3) ? DustID.SnowflakeIce : (Main.rand.NextBool() ? DustID.Frost : DustID.IceTorch);
                Dust d = Dust.NewDustPerfect(z.Pos[i] + Main.rand.NextVector2Circular(4f, 4f), type,
                    new Vector2(Main.rand.NextFloat(-0.25f, 0.25f), Main.rand.NextFloat(0.05f, 0.35f)), 120,
                    default, Main.rand.NextFloat(0.5f, 0.85f));
                d.noGravity = true;
                d.fadeIn = 0.3f;
            }
        }

        // Slow обновляется, пока цель стоит в залитых клетках; сильный иней один раз вешает Frostbite
        private static void SlowNpcs(Zone z)
        {
            for (int n = 0; n < Main.maxNPCs; n++)
            {
                NPC npc = Main.npc[n];
                if (!npc.active || npc.friendly || npc.dontTakeDamage || npc.immortal)
                    continue;
                Point a = npc.Hitbox.TopLeft().ToTileCoordinates();
                Point b = npc.Hitbox.BottomRight().ToTileCoordinates();
                bool inside = false;
                for (int x = a.X; x <= b.X && !inside; x++)
                    for (int y = a.Y; y <= b.Y && !inside; y++)
                        inside = z.Cells.Contains(new Point(x, y));
                if (!inside)
                    continue;
                npc.AddBuff(BuffID.Slow, SickleArcProjectile.FrostSlowTicks);
                if (z.Strong && z.Burned.Add(n))
                    npc.AddBuff(BuffID.Frostburn2, SickleArcProjectile.FrostbiteDuration);
            }
        }

        // Мягкое пятно: радиальный градиент с зерном по краю, премультиплированная альфа
        private static Texture2D GetBlob()
        {
            if (blob != null && !blob.IsDisposed)
                return blob;
            blob = new Texture2D(Main.graphics.GraphicsDevice, BlobTexSize, BlobTexSize);
            Color[] data = new Color[BlobTexSize * BlobTexSize];
            float half = BlobTexSize * 0.5f;
            for (int y = 0; y < BlobTexSize; y++)
            {
                for (int x = 0; x < BlobTexSize; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    float grain = (((x * 73856093) ^ (y * 19349663)) & 255) / 255f;
                    d += (grain - 0.5f) * 0.3f;
                    float a = d >= 1f ? 0f : (float)Math.Pow(1f - d, 1.5f);
                    byte v = (byte)(255 * MathHelper.Clamp(a, 0f, 1f));
                    data[y * BlobTexSize + x] = new Color(v, v, v, v);
                }
            }
            blob.SetData(data);
            return blob;
        }

        public override void PostDrawTiles()
        {
            if (zones.Count == 0 || Main.dedServ)
                return;
            Texture2D tex = GetBlob();
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            if (tex == null || pixel == null || pixel.IsDisposed)
                return;
            Rectangle view = new Rectangle((int)Main.screenPosition.X - 40, (int)Main.screenPosition.Y - 40,
                Main.screenWidth + 80, Main.screenHeight + 80);
            Vector2 origin = new Vector2(BlobTexSize * 0.5f);
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
            Color weakBody = WeakBody;
            Color strongBody = StrongBody;
            foreach (Zone z in zones)
            {
                Color body = z.Strong ? strongBody : weakBody;
                float alphaMul = BlobAlpha * (z.Strong ? StrongAlphaMult : 1f);
                for (int i = 0; i < z.Pos.Count; i++)
                {
                    int age = z.Tick - z.Born[i];
                    if (age < 0)
                        continue;
                    Vector2 p = z.Pos[i];
                    if (!view.Contains(p.ToPoint()))
                        continue;
                    float life = Math.Clamp(age / (float)z.Life, 0f, 1f);
                    float grow = Utils.GetLerpValue(0f, 3f, age, true);        // быстро проявляется
                    float melt = 1f - life * life * life;                       // долго держится, тает к концу
                    float u = z.Edge[i];
                    float a = alphaMul * z.Weight[i] * grow * melt * (1f - 0.75f * u * u);
                    if (a < 0.01f)
                        continue;
                    int seed = z.Seed[i];
                    float sx = 0.85f + 0.4f * ((seed & 255) / 255f);
                    float sy = 0.85f + 0.4f * (((seed >> 8) & 255) / 255f);
                    float size = BlobSize * (1f - 0.35f * u) * (0.5f + 0.5f * grow) * (0.65f + 0.35f * melt) / BlobTexSize;
                    Color light = Lighting.GetColor((int)(p.X / 16f), (int)(p.Y / 16f));
                    float lm = 0.6f + 0.4f * Math.Max(light.R, Math.Max(light.G, light.B)) / 255f;
                    Main.spriteBatch.Draw(tex, p - Main.screenPosition, null, body * (a * lm), 0f, origin,
                        new Vector2(size * sx, size * sy), SpriteEffects.None, 0f);
                }

                DrawBlast(z, tex, pixel, origin);
            }
            Main.spriteBatch.End();
        }

        // Вспышка в центре и тонкое ударное кольцо, которое не проходит сквозь блоки
        private static void DrawBlast(Zone z, Texture2D tex, Texture2D pixel, Vector2 origin)
        {
            Vector2 sc = z.Center - Main.screenPosition;
            if (z.Tick < FlashTicks)
            {
                float t = z.Tick / (float)FlashTicks;
                float d = MathHelper.Clamp(40f + z.Radius * 0.35f, 40f, 130f) * (0.5f + 0.9f * EaseOut(t));
                float a = 1f - t;
                Main.spriteBatch.Draw(tex, sc, null, new Color(150, 215, 255) * (a * 0.8f), 0f, origin,
                    new Vector2(d * 1.5f / BlobTexSize), SpriteEffects.None, 0f);
                Main.spriteBatch.Draw(tex, sc, null, Color.White * a, 0f, origin,
                    new Vector2(d / BlobTexSize), SpriteEffects.None, 0f);
            }
            if (z.Tick < RingTicks)
            {
                float t = z.Tick / (float)RingTicks;
                float rr = z.Radius * EaseOut(z.Tick / (float)GrowTicks) * 1.03f;
                float a = (float)Math.Pow(1f - t, 1.5f);
                float thick = MathHelper.Lerp(5f, 1.5f, t);
                int segs = Math.Clamp((int)(rr * MathHelper.TwoPi / 6f), 24, 96);
                float segLen = rr * MathHelper.TwoPi / segs + 2f;
                Color c = (z.Strong ? new Color(200, 240, 255) : new Color(225, 245, 255)) * a;
                for (int k = 0; k < segs; k++)
                {
                    float ang = k * MathHelper.TwoPi / segs;
                    Vector2 pos = z.Center + ang.ToRotationVector2() * rr;
                    if (!z.Cells.Contains(pos.ToTileCoordinates()))
                        continue;
                    Main.spriteBatch.Draw(pixel, pos - Main.screenPosition, new Rectangle(0, 0, 1, 1), c,
                        ang + MathHelper.PiOver2, new Vector2(0.5f), new Vector2(segLen, thick), SpriteEffects.None, 0f);
                }
            }
        }
    }
}
