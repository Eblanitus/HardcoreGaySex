using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace FrostMelee.Content
{
    // Вершинная лента-шлейф тычка. Механика ленты взмаха (SickleArcProjectile.DrawTrail) один в один:
    // BasicEffect + полоса пар вершин, запечённая маска градиента и второй проход бегущих штрихов, аддитив по premultiplied.
    // Отличие только в геометрии: лента лежит симметрично по следу острия, ширина — поперёк оси копья.
    // При тычке острие едет вдоль своей же оси, поэтому односторонняя лента «от острия внутрь», как у взмаха,
    // вырождается в линию или ложится треугольником прямо на лезвие (это и был «белый блик» у кончика).
    public static class StabRibbon
    {
        // --- Маска градиента: u — вдоль ленты (0 голова у острия, 1 хвост), v — поперёк (0 и 1 края, 0.5 след острия) ---
        private const float MaskPower = 1.25f;   // кривая падения яркости к хвосту (TrailMaskPower у взмаха)
        private const float TailWidth = 0.35f;   // ширина на хвосте, доля ширины у головы: срез сужает ленту к следу острия
        private const float EdgeSoft = 0.35f;    // мягкость краёв, доля полуширины
        private const float HeadSoft = 0.12f;    // мягкое начало у острия, доля длины: у самого кончика ленты почти нет
        private const float CoreSpan = 0.55f;    // до какой доли ленты цвет светлее, дальше цвет хвоста (TrailCoreSpan)
        private const float EdgeMix = 0.4f;      // насколько самый хвост темнеет к цвету края (TrailEdgeMix)
        private const float HeadLight = 0.4f;    // голова: 0 — цвет хвоста, 1 — белое ядро. Не белая: лёд голубой, смерть фиолетовая

        // --- Бегущие штрихи: второй проход, как у взмаха ---
        private const float StreakAlpha = 0.85f;     // яркость штрихов (0 — выключить слой)
        private const float StreakRepeat = 1.5f;     // сколько раз рисунок повторяется вдоль ленты (у взмаха 2.5, лента короче)
        private const float StreakSpeed = 0.06f;     // сдвиг рисунка к хвосту, циклов за тик
        private const float StreakTaper = 0.8f;      // как сужается полоса штрихов к хвосту
        private const float StreakWidthMult = 0.95f; // ширина полосы штрихов относительно градиентного слоя
        private const int StreakCount = 9;           // сколько штрихов поперёк ленты в маске
        private const float StreakThickness = 0.03f; // толщина штриха, доля ширины ленты

        private const int MaxPoints = 32;

        private static BasicEffect effect;
        private static Texture2D mask;
        private static Texture2D streakMask;
        private static BlendState blend;
        private static RasterizerState raster;
        private static readonly VertexPositionColorTexture[] verts = new VertexPositionColorTexture[MaxPoints * 2];

        public static void Unload()
        {
            if (Main.dedServ)
                return;
            BasicEffect e = effect;
            Texture2D m = mask, s = streakMask;
            effect = null;
            mask = null;
            streakMask = null;
            blend = null;
            raster = null;
            Main.QueueMainThreadAction(() =>
            {
                e?.Dispose();
                m?.Dispose();
                s?.Dispose();
            });
        }

        private static float Smooth01(float x)
        {
            x = MathHelper.Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        private static Texture2D MakeMask(GraphicsDevice device, int w, int h, Func<float, float, float> value)
        {
            Texture2D tex = new Texture2D(device, w, h);
            Color[] data = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    int b = (int)(MathHelper.Clamp(value(u, v), 0f, 1f) * 255f + 0.5f);
                    data[y * w + x] = new Color(b, b, b, b);   // premultiplied: rgb = alpha = маска
                }
            }
            tex.SetData(data);
            return tex;
        }

        // Диагональный срез взмаха, отзеркаленный от следа острия: у головы полоса во всю ширину,
        // к хвосту сужается к следу и гаснет. Края мягкие, к середине чуть ярче.
        private static float MaskValue(float u, float v)
        {
            float d = Math.Abs(v * 2f - 1f);                 // 0 — след острия, 1 — край ленты
            float w = 1f - u * (1f - TailWidth);             // видимая полуширина на этой длине
            float a = (float)Math.Pow(1f - u, MaskPower);
            a *= Smooth01((w - d) / Math.Max(0.001f, EdgeSoft * w));
            a *= Smooth01(u / Math.Max(0.001f, HeadSoft));
            a *= 1f - 0.35f * d;
            return a;
        }

        // Маска штрихов взмаха: тонкие продольные полосы разной длины, по u тайлится (целые частоты)
        private static Texture2D MakeStreakMask(GraphicsDevice device)
        {
            System.Random rng = new System.Random(7);
            float[] pos = new float[StreakCount];
            float[] phase = new float[StreakCount];
            int[] freq = new int[StreakCount];
            float[] gain = new float[StreakCount];
            for (int s = 0; s < StreakCount; s++)
            {
                pos[s] = (s + 0.3f + 0.4f * (float)rng.NextDouble()) / StreakCount;
                phase[s] = (float)rng.NextDouble();
                freq[s] = 2 + rng.Next(4);
                gain[s] = 0.55f + 0.45f * (float)rng.NextDouble();
            }
            return MakeMask(device, 256, 64, (u, v) =>
            {
                float best = 0f;
                for (int s = 0; s < StreakCount; s++)
                {
                    float dv = (v - pos[s]) / StreakThickness;
                    float line = (float)Math.Exp(-dv * dv);
                    if (line < 0.01f)
                        continue;
                    float along = 0.5f + 0.5f * (float)Math.Sin(MathHelper.TwoPi * (freq[s] * u + phase[s]));
                    best = Math.Max(best, line * gain[s] * (float)Math.Pow(along, 2.5f));
                }
                return best;
            });
        }

        private static bool Ensure()
        {
            GraphicsDevice device = Main.graphics.GraphicsDevice;
            if (device == null)
                return false;
            if (effect == null || effect.IsDisposed)
            {
                effect = new BasicEffect(device)
                {
                    TextureEnabled = true,
                    VertexColorEnabled = true,
                    LightingEnabled = false,
                    FogEnabled = false
                };
            }
            if (mask == null || mask.IsDisposed)
                mask = MakeMask(device, 128, 64, MaskValue);
            if (streakMask == null || streakMask.IsDisposed)
                streakMask = MakeStreakMask(device);
            // Аддитив по premultiplied: итог = цвет вершины × маска, альфу экрана не трогаем
            blend ??= new BlendState
            {
                ColorSourceBlend = Blend.One,
                ColorDestinationBlend = Blend.One,
                AlphaSourceBlend = Blend.Zero,
                AlphaDestinationBlend = Blend.One
            };
            raster ??= new RasterizerState { CullMode = CullMode.None };
            return true;
        }

        // center[i] — след острия в мире, normal[i] — единичная нормаль к оси копья в тот тик; i = 0 — голова (сейчас),
        // дальше назад по времени. halfWidth — полуширина ленты у головы, px.
        public static void Draw(Vector2[] center, Vector2[] normal, int n, float halfWidth, Color core, Color tail, Color edge, float alpha)
        {
            if (Main.dedServ || n < 2 || alpha <= 0.01f || halfWidth <= 0.5f)
                return;
            n = Math.Min(n, MaxPoints);
            if (!Ensure())
                return;

            GraphicsDevice device = Main.graphics.GraphicsDevice;
            Viewport vp = device.Viewport;
            Color head = Color.Lerp(tail, core, HeadLight);
            Main.spriteBatch.End();
            try
            {
                effect.World = Matrix.Identity;
                effect.View = Main.GameViewMatrix.TransformationMatrix;
                effect.Projection = Matrix.CreateOrthographicOffCenter(0f, vp.Width, vp.Height, 0f, -1f, 1f);
                device.BlendState = blend;
                device.DepthStencilState = DepthStencilState.None;
                device.RasterizerState = raster;
                Pass(device, mask, SamplerState.LinearClamp, center, normal, n, halfWidth, false, head, tail, edge, alpha);
                if (StreakAlpha > 0f)
                    Pass(device, streakMask, SamplerState.LinearWrap, center, normal, n, halfWidth, true, head, tail, edge, alpha);
            }
            finally
            {
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                    DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
            }
        }

        // Один проход ленты: полоса из n пар вершин по обе стороны следа острия (DrawTrailPass у взмаха)
        private static void Pass(GraphicsDevice device, Texture2D tex, SamplerState sampler, Vector2[] center, Vector2[] normal,
            int n, float halfWidth, bool streaks, Color head, Color tail, Color edge, float alpha)
        {
            // 500 тиков × 0.06 = 30 целых циклов: рисунок штрихов при сбросе счётчика не прыгает
            float scroll = (Main.GameUpdateCount % 500u) * StreakSpeed;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                Color baseColor = Color.Lerp(Color.Lerp(head, tail, Smooth01(t / CoreSpan)), edge, t * t * EdgeMix);
                float w = halfWidth;
                float uv = t;
                float strength;
                if (streaks)
                {
                    w = halfWidth * StreakWidthMult * (float)Math.Pow(1f - t, StreakTaper);
                    uv = t * StreakRepeat - scroll;
                    strength = StreakAlpha * alpha * (float)Math.Pow(1f - t, 1.2f) * MathHelper.Clamp(t / 0.05f, 0f, 1f);
                    baseColor = Color.Lerp(baseColor, head, 0.5f);
                }
                else
                {
                    strength = alpha;
                }
                Color c = baseColor * strength;
                c.A = 0;
                Vector2 p = center[i] - Main.screenPosition;
                Vector2 off = normal[i] * w;
                verts[i * 2] = new VertexPositionColorTexture(new Vector3(p + off, 0f), c, new Vector2(uv, 0f));
                verts[i * 2 + 1] = new VertexPositionColorTexture(new Vector3(p - off, 0f), c, new Vector2(uv, 1f));
            }

            effect.Texture = tex;
            device.SamplerStates[0] = sampler;
            effect.CurrentTechnique.Passes[0].Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, verts, 0, n * 2 - 2);
        }
    }
}
