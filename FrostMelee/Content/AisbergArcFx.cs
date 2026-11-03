using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;

namespace FrostMelee.Content
{
    // Дуга Айсберга — «айсберг в разрезе». Над линией воды узкая яркая
    // верхушка с пиками, под ней огромная тёмная подводная масса с
    // гранёным краем: видно десятую часть, бьёт всё остальное.
    //
    // Слои по записанному пути клинка (углы и радиусы вокруг кисти):
    // 1) подводная масса — тёмно-синяя, обычное смешение, рваный гранёный
    //    внутренний край (прямые отрезки между узлами, как сколы льда);
    // 2) грани — бирюза поверх массы, свой рисунок сколов, тоньше;
    // 3) кромка сколов — тонкая светлая линия по внутреннему краю граней;
    // 4) линия воды — тонкая белёсая черта;
    // 5) верхушка — яркий циан с пиками над внешним краем, белое только
    //    у головы (у острия), как у понравившихся владельцу дуг.
    // Без текстур и масок: чистая геометрия с жёстким краем — пиксельно
    // резкая, без мягкого свечения и «ноиты».
    // Рисует только при ЗАКРЫТОМ spriteBatch (состояния ставит сам).
    public static class AisbergArcFx
    {
        public static readonly Color Deep = new Color(8, 30, 48);
        public static readonly Color Facet = new Color(12, 96, 118);
        public static readonly Color FacetRim = new Color(60, 170, 200);
        public static readonly Color Waterline = new Color(190, 245, 255);
        public static readonly Color Cyan = new Color(40, 220, 240);
        public static readonly Color White = new Color(240, 252, 255);

        private const float TopWidth = 0.09f;     // верхушка: доля радиуса
        private const float DeepMin = 0.40f;      // подводная масса: доля радиуса, разброс сколов
        private const float DeepMax = 0.62f;
        private const float FacetMin = 0.45f;     // грани: доля глубины массы
        private const float FacetMax = 0.75f;
        private const float PeakHeight = 0.12f;   // пики верхушки над внешним краем: доля радиуса
        private const float DeepAlpha = 0.82f;
        private const float FacetAlpha = 0.8f;
        private const float RimWidth = 2f;        // px
        private const float WaterWidth = 2f;      // px
        private const int Knots = 7;              // узлов сколов по длине ленты

        private static BasicEffect effect;
        private static RasterizerState raster;
        private static BlendState additive;
        private static VertexPositionColor[] verts;

        public static void Unload()
        {
            BasicEffect e = effect;
            effect = null;
            raster = null;
            additive = null;
            verts = null;
            Main.QueueMainThreadAction(() => e?.Dispose());
        }

        private static bool Ensure(GraphicsDevice device)
        {
            if (device == null)
                return false;
            if (effect == null || effect.IsDisposed)
            {
                effect = new BasicEffect(device)
                {
                    TextureEnabled = false,
                    VertexColorEnabled = true,
                    LightingEnabled = false,
                    FogEnabled = false
                };
            }
            raster ??= new RasterizerState { CullMode = CullMode.None };
            additive ??= new BlendState
            {
                ColorSourceBlend = Blend.One,
                ColorDestinationBlend = Blend.One,
                AlphaSourceBlend = Blend.Zero,
                AlphaDestinationBlend = Blend.One
            };
            return true;
        }

        // Псевдослучайное 0..1 по номеру узла и зерну взмаха: рисунок сколов
        // свой у каждого взмаха и неподвижен относительно головы ленты —
        // масса едет за клинком целиком, как одна глыба
        private static float Hash(int seed, int k)
        {
            uint h = (uint)(seed * 73856093) ^ (uint)(k * 19349663) ^ 0x9E3779B9u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
        }

        // Ломаная по узлам: значение на доле длины t
        private static float Jagged(int seed, float t, float min, float max)
        {
            float f = MathHelper.Clamp(t, 0f, 1f) * (Knots - 1);
            int k = Math.Min((int)f, Knots - 2);
            float a = MathHelper.Lerp(min, max, Hash(seed, k));
            float b = MathHelper.Lerp(min, max, Hash(seed, k + 1));
            return MathHelper.Lerp(a, b, f - k);
        }

        // Пики над водой: 2–3 острых зубца в головной половине ленты
        private static float Peaks(int seed, float t)
        {
            float best = 0f;
            for (int p = 0; p < 3; p++)
            {
                float at = 0.06f + 0.16f * p + 0.06f * Hash(seed + 101, p);
                float halfW = 0.05f + 0.03f * Hash(seed + 202, p);
                float h = (0.55f + 0.45f * Hash(seed + 303, p)) * (1f - 0.3f * p);
                float d = Math.Abs(t - at) / halfW;
                if (d < 1f)
                    best = Math.Max(best, h * (1f - d));
            }
            return best;
        }

        private static Color Premul(Color c, float a) =>
            new Color((byte)(c.R * a), (byte)(c.G * a), (byte)(c.B * a), (byte)(255 * a));

        private static Color Add(Color c, float a) =>
            new Color((byte)(c.R * a), (byte)(c.G * a), (byte)(c.B * a), (byte)0);

        private static void EnsureVerts(int points)
        {
            if (verts == null || verts.Length < points * 2)
                verts = new VertexPositionColor[Math.Max(64, points * 2)];
        }

        // Полоса между радиусами rA(i) и rB(i) вокруг center
        private static void Strip(GraphicsDevice device, float[] angles, int n, Vector2 center,
            Func<int, float> rA, Func<int, float> rB, Func<int, Color> colA, Func<int, Color> colB, BlendState blend)
        {
            EnsureVerts(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 dir = angles[i].ToRotationVector2();
                verts[i * 2] = new VertexPositionColor(new Vector3(center + dir * rA(i) - Main.screenPosition, 0f), colA(i));
                verts[i * 2 + 1] = new VertexPositionColor(new Vector3(center + dir * rB(i) - Main.screenPosition, 0f), colB(i));
            }
            device.BlendState = blend;
            effect.CurrentTechnique.Passes[0].Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, verts, 0, n * 2 - 2);
        }

        // angles/rOut — путь острия от головы (0) к хвосту, center — кисть.
        // env — огибающая взмаха, seed — зерно рисунка сколов
        public static void Draw(GraphicsDevice device, float[] angles, float[] rOut, int n, Vector2 center,
            float env, int seed)
        {
            if (n < 2 || env <= 0.02f || !Ensure(device))
                return;

            float[] t = new float[n];
            float[] fade = new float[n];
            float[] water = new float[n];
            float[] deep = new float[n];
            float[] facet = new float[n];
            float[] peak = new float[n];
            for (int i = 0; i < n; i++)
            {
                float ti = i / (float)(n - 1);
                float r = rOut[i];
                float taper = 1f - 0.55f * ti;
                t[i] = ti;
                fade[i] = env * (float)Math.Pow(1f - ti, 1.3f);
                water[i] = r - r * TopWidth * (1f - 0.6f * ti);
                float depth = r * Jagged(seed, ti, DeepMin, DeepMax) * taper;
                deep[i] = Math.Max(4f, water[i] - depth);
                facet[i] = Math.Max(deep[i], water[i] - depth * Jagged(seed + 37, ti, FacetMin, FacetMax));
                peak[i] = r + r * PeakHeight * Peaks(seed, ti);
            }

            effect.World = Matrix.Identity;
            effect.View = Main.GameViewMatrix.TransformationMatrix;
            effect.Projection = Matrix.CreateOrthographicOffCenter(0f, device.Viewport.Width, device.Viewport.Height, 0f, -1f, 1f);
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = raster;

            // 1) подводная масса
            Strip(device, angles, n, center, i => water[i], i => deep[i],
                i => Premul(Deep, DeepAlpha * fade[i]), i => Premul(Deep, DeepAlpha * fade[i]), BlendState.AlphaBlend);
            // 2) грани
            Strip(device, angles, n, center, i => water[i], i => facet[i],
                i => Premul(Facet, FacetAlpha * fade[i]), i => Premul(Deep, FacetAlpha * fade[i]), BlendState.AlphaBlend);
            // 3) кромка сколов по внутреннему краю граней и массы
            Strip(device, angles, n, center, i => facet[i] + RimWidth, i => facet[i],
                i => Add(FacetRim, 0.55f * fade[i]), i => Add(FacetRim, 0.55f * fade[i]), additive);
            Strip(device, angles, n, center, i => deep[i] + RimWidth, i => deep[i],
                i => Add(Facet, 0.6f * fade[i]), i => Add(Facet, 0.6f * fade[i]), additive);
            // 4) линия воды
            Strip(device, angles, n, center, i => water[i] + WaterWidth * 0.5f, i => water[i] - WaterWidth * 0.5f,
                i => Add(Waterline, 0.8f * fade[i]), i => Add(Waterline, 0.8f * fade[i]), additive);
            // 5) верхушка с пиками: белое только у головы
            Strip(device, angles, n, center, i => peak[i], i => water[i],
                i => Add(Color.Lerp(White, Cyan, Math.Min(1f, t[i] * 4f)), fade[i]),
                i => Add(Cyan, 0.85f * fade[i]), additive);
        }

        // Пик взмаха: от подводной массы у головы отваливаются куски льда —
        // крупная ванильная крошка, падает вниз. Свет по верхушке.
        public static void PeakCrumble(Vector2 center, float angle, float radius)
        {
            if (Main.dedServ)
                return;
            Vector2 dir = angle.ToRotationVector2();
            for (int i = 0; i < 3; i++)
            {
                Vector2 at = center + dir * radius * Main.rand.NextFloat(0.5f, 0.85f);
                Dust d = Dust.NewDustPerfect(at, DustID.Ice, new Vector2(Main.rand.NextFloat(-1f, 1f), Main.rand.NextFloat(0.5f, 2f)),
                    0, default, Main.rand.NextFloat(1.2f, 1.5f));
                d.noGravity = false;
            }
        }

        public static void Light(Vector2 center, float angle, float radius, float env)
        {
            Vector2 dir = angle.ToRotationVector2();
            Lighting.AddLight(center + dir * radius, 0.15f * env, 0.6f * env, 0.75f * env);
            Lighting.AddLight(center + dir * radius * 0.6f, 0.03f * env, 0.15f * env, 0.25f * env);
        }
    }
}
