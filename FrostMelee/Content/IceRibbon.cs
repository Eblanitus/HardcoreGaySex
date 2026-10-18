using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace FrostMelee.Content
{
    // Вершинная лента-шлейф дуги взмаха — общее для серпа и мечей.
    // Математика та же, что была в SickleArcProjectile (перенесена без изменений):
    // градиентная полоса с диагональным срезом + бегущие штрихи, аддитивное
    // смешение по premultiplied (A = 0). Обобщена на историю точек с любой
    // геометрией: массивы углов и радиусов на точку.
    public static class IceRibbon
    {
        // --- градиентный слой ---
        private const float Alpha = 0.95f;       // общая яркость
        private const float CutSlope = 1f;       // наклон диагонального среза маски
        private const float MaskPower = 1.25f;   // кривая падения яркости к хвосту
        private const float TipSoft = 0.07f;     // мягкость внешнего края (доля ширины)
        private const float HeadSoft = 0.02f;    // мягкость начала у головы (доля длины)
        private const float CoreSpan = 0.55f;    // до какой доли ленты цвет светлее ядра
        private const float EdgeMix = 0.4f;      // насколько хвост темнеет к цвету края

        // --- бегущие штрихи ---
        private const float StreakAlpha = 0.85f;
        private const float StreakRepeat = 2.5f;
        private const float StreakSpeed = 0.06f;
        private const float StreakTaper = 0.8f;
        private const float StreakWidthMult = 0.95f;
        private const int StreakCount = 9;
        private const float StreakThickness = 0.03f;

        // --- кристаллы на внешнем крае ---
        private const float CrystalAlpha = 0.55f;
        private const float CrystalMinSize = 7f;
        private const float CrystalMaxSize = 17f;

        private static Texture2D trailMask;
        private static Texture2D streakMask;
        private static BasicEffect effect;
        private static BlendState blend;
        private static RasterizerState raster;
        private static VertexPositionColorTexture[] verts;

        public static void Unload()
        {
            Texture2D a = trailMask, b = streakMask;
            BasicEffect e = effect;
            trailMask = null;
            streakMask = null;
            effect = null;
            blend = null;
            raster = null;
            verts = null;
            Main.QueueMainThreadAction(() =>
            {
                a?.Dispose();
                b?.Dispose();
                e?.Dispose();
            });
        }

        private static bool Ensure(GraphicsDevice device)
        {
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
            if (trailMask == null || trailMask.IsDisposed)
                trailMask = MakeMask(128, 64, MaskValue);
            if (streakMask == null || streakMask.IsDisposed)
                streakMask = MakeStreakMask();
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

        public static bool Ready => effect != null && !effect.IsDisposed && trailMask != null && streakMask != null;

        private static float Smooth01(float x)
        {
            x = MathHelper.Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        private static Texture2D MakeMask(int w, int h, Func<float, float, float> value)
        {
            Texture2D tex = new Texture2D(Main.graphics.GraphicsDevice, w, h);
            Color[] data = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    int b = (int)(MathHelper.Clamp(value(u, v), 0f, 1f) * 255f + 0.5f);
                    data[y * w + x] = new Color(b, b, b, b);   // premultiplied: rgb = alpha
                }
            }
            tex.SetData(data);
            return tex;
        }

        // Маска градиента: u — вдоль ленты (0 голова, 1 хвост), v — поперёк (0 внешний край, 1 внутренний)
        private static float MaskValue(float u, float v)
        {
            float cut = MathHelper.Clamp(1f - u - v * CutSlope, 0f, 1f);
            float a = (float)Math.Pow(cut, MaskPower);
            a *= Smooth01(v / Math.Max(0.001f, TipSoft));
            a *= Smooth01(u / Math.Max(0.001f, HeadSoft));
            return a;
        }

        // Маска штрихов: тонкие продольные полосы разной длины, тайлится по u
        private static Texture2D MakeStreakMask()
        {
            Random rng = new Random(7);
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
            return MakeMask(256, 64, (u, v) =>
            {
                float best = 0f;
                for (int s = 0; s < StreakCount; s++)
                {
                    float d = (v - pos[s]) / StreakThickness;
                    float line = (float)Math.Exp(-d * d);
                    if (line < 0.01f)
                        continue;
                    float along = 0.5f + 0.5f * (float)Math.Sin(MathHelper.TwoPi * (freq[s] * u + phase[s]));
                    best = Math.Max(best, line * gain[s] * (float)Math.Pow(along, 2.5f));
                }
                return best;
            });
        }

        private static void EnsureVerts(int points)
        {
            if (verts == null || verts.Length < points * 2)
                verts = new VertexPositionColorTexture[Math.Max(64, points * 2)];
        }

        // Один проход: полоса из n пар вершин вокруг center (внешний край rOut[i], внутренний rIn[i])
        private static void RunPass(GraphicsDevice device, Texture2D mask, SamplerState sampler, bool streaks,
            float[] angles, int n, Vector2 center, float[] rOut, float[] rIn, float scrollBase, float env,
            Color core, Color tail, Color edge)
        {
            EnsureVerts(n);
            float scroll = scrollBase * StreakSpeed;
            for (int i = 0; i < n; i++)
            {
                float t = n > 1 ? i / (float)(n - 1) : 0f;
                Vector2 dir = angles[i].ToRotationVector2();
                float inner = rIn[i];
                float uv = t;
                float strength;
                Color baseColor = Color.Lerp(Color.Lerp(core, tail, Smooth01(t / CoreSpan)), edge, t * t * EdgeMix);
                if (streaks)
                {
                    inner = rOut[i] - (rOut[i] - rIn[i]) * StreakWidthMult * (float)Math.Pow(1f - t, StreakTaper);
                    uv = t * StreakRepeat - scroll;
                    strength = StreakAlpha * env * (float)Math.Pow(1f - t, 1.2f) * Utils.GetLerpValue(0f, 0.05f, t, true);
                    baseColor = Color.Lerp(baseColor, core, 0.5f);
                }
                else
                {
                    strength = Alpha * env;
                }
                Color c = baseColor * strength;
                c.A = 0;
                verts[i * 2] = new VertexPositionColorTexture(new Vector3(center + dir * rOut[i] - Main.screenPosition, 0f), c, new Vector2(uv, 0f));
                verts[i * 2 + 1] = new VertexPositionColorTexture(new Vector3(center + dir * inner - Main.screenPosition, 0f), c, new Vector2(uv, 1f));
            }
            effect.Texture = mask;
            device.SamplerStates[0] = sampler;
            effect.CurrentTechnique.Passes[0].Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, verts, 0, n * 2 - 2);
        }

        // Лента по истории углов. Вызывать при ЗАКРЫТОМ spriteBatch (внутри End/Begin окна).
        // scrollBase — счётчик тиков для анимации штрихов; env — общая яркость 0..1.
        public static void Draw(GraphicsDevice device, float[] angles, int n, Vector2 center,
            float[] rOut, float[] rIn, float scrollBase, float env, Color core, Color tail, Color edge)
        {
            if (n < 2 || env <= 0.02f || !Ensure(device))
                return;
            effect.World = Matrix.Identity;
            effect.View = Main.GameViewMatrix.TransformationMatrix;
            effect.Projection = Matrix.CreateOrthographicOffCenter(0f, device.Viewport.Width, device.Viewport.Height, 0f, -1f, 1f);
            device.BlendState = blend;
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = raster;
            RunPass(device, trailMask, SamplerState.LinearClamp, false, angles, n, center, rOut, rIn, scrollBase, env, core, tail, edge);
            if (StreakAlpha > 0f)
                RunPass(device, streakMask, SamplerState.LinearWrap, true, angles, n, center, rOut, rIn, scrollBase, env, core, tail, edge);
        }

        // Инеевые кристаллы-иглы на внешнем крае ленты (рисуются при ОТКРЫТОМ батче):
        // каждая вторая точка истории, размер и наклон детерминированы по индексу,
        // ярче у головы, мерцают в такте времени. Читаются как ледяные наросты.
        public static void DrawEdgeCrystals(Texture2D glow, float[] angles, int n, Vector2 center,
            float[] rOut, float time, float env, Color core, Color tail)
        {
            if (glow == null || glow.IsDisposed || env <= 0.02f)
                return;
            Vector2 origin = glow.Size() / 2f;
            for (int i = 1; i < n - 1; i += 2)
            {
                float t = i / (float)(n - 1);              // 0 — голова, 1 — хвост
                float shimmer = 0.55f + 0.45f * (float)Math.Sin(time * 3.1f + i * 0.9f);
                float a = CrystalAlpha * env * shimmer * (1f - t * 0.55f);
                if (a < 0.03f)
                    continue;
                Vector2 dir = angles[i].ToRotationVector2();
                Vector2 pos = center + dir * (rOut[i] + 2f);
                float size = CrystalMinSize + (CrystalMaxSize - CrystalMinSize) * (1f - t) *
                             (0.65f + 0.35f * (((i * 2654435761u) >> 8) & 255) / 255f);
                // игла наружу по лучу + короткие боковые + яркое ядро — осколок льда
                Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, Color.Lerp(tail, core, 0.6f) * a,
                    angles[i], origin, new Vector2(size / glow.Width, size * 0.14f / glow.Height), SpriteEffects.None, 0f);
                float side = angles[i] + MathHelper.PiOver4 * 0.5f;
                Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, tail * (a * 0.6f), side, origin,
                    new Vector2(size * 0.45f / glow.Width, size * 0.1f / glow.Height), SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, core * (a * 0.7f), side - MathHelper.PiOver4, origin,
                    new Vector2(size * 0.35f / glow.Width, size * 0.09f / glow.Height), SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, core * a, 0f, origin,
                    size * 0.22f / glow.Width, SpriteEffects.None, 0f);
            }
        }
    }
}
