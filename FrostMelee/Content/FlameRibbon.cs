using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace FrostMelee.Content
{
    // Цельная лента холодного пламени по цепочке точек — «тело» огня вместо
    // россыпи отдельных пятен-частиц (просьба владельца: огонь должен
    // читаться как пламя, а не как пыль). Точки идут от основания языка к
    // кончику; ширина и цвет задаются на точку, мягкость краёв и затухание
    // к кончику даёт процедурная маска. Техника та же, что у IceRibbon:
    // BasicEffect, аддитивное смешение по premultiplied (A = 0), вызов
    // только при ЗАКРЫТОМ spriteBatch (состояния устройства ставим внутри).
    //
    // Живой огонь обычно рисуют двумя проходами по одной цепочке: широкий
    // тёмно-синий (внешняя оболочка) и поверх — узкий ярко-голубой (ядро).
    public static class FlameRibbon
    {
        private static Texture2D flameMask;
        private static BasicEffect effect;
        private static BlendState blend;
        private static RasterizerState raster;
        private static VertexPositionColorTexture[] verts;

        public static void Unload()
        {
            Texture2D a = flameMask;
            BasicEffect e = effect;
            flameMask = null;
            effect = null;
            blend = null;
            raster = null;
            verts = null;
            Main.QueueMainThreadAction(() =>
            {
                a?.Dispose();
                e?.Dispose();
            });
        }

        public static bool Ready => effect != null && !effect.IsDisposed && flameMask != null;

        private static float Smooth01(float x)
        {
            x = MathHelper.Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        // Маска: u — вдоль языка (0 основание, 1 кончик), v — поперёк (0/1 края).
        // Поперёк — мягкая «труба» sin^1.4, вдоль — быстрый подъём от основания
        // и длинный хвост затухания к кончику: язык не обрывается, а тает
        private static float MaskValue(float u, float v)
        {
            float across = (float)Math.Pow(Math.Sin(Math.PI * v), 1.4f);
            float along = Smooth01(u / 0.1f) * (float)Math.Pow(1f - u, 0.55f);
            return across * along;
        }

        private static Texture2D MakeMask()
        {
            const int w = 128, h = 32;
            Texture2D tex = new Texture2D(Main.graphics.GraphicsDevice, w, h);
            Color[] data = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    int b = (int)(MathHelper.Clamp(MaskValue(u, v), 0f, 1f) * 255f + 0.5f);
                    data[y * w + x] = new Color(b, b, b, b);   // premultiplied: rgb = alpha
                }
            }
            tex.SetData(data);
            return tex;
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
            if (flameMask == null || flameMask.IsDisposed)
                flameMask = MakeMask();
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

        private static void EnsureVerts(int points)
        {
            if (verts == null || verts.Length < points * 2)
                verts = new VertexPositionColorTexture[Math.Max(64, points * 2)];
        }

        // Одна лента: points[0..n) — ось от основания к кончику (мировые
        // координаты), widths[i] — полная ширина в px, colors[i] — цвет с уже
        // учтённой яркостью (альфа игнорируется, внутри ставится 0)
        public static void Draw(GraphicsDevice device, Vector2[] points, float[] widths, Color[] colors, int n)
        {
            if (n < 2 || points == null || widths == null || colors == null || !Ensure(device))
                return;

            effect.World = Matrix.Identity;
            effect.View = Main.GameViewMatrix.TransformationMatrix;
            effect.Projection = Matrix.CreateOrthographicOffCenter(0f, device.Viewport.Width, device.Viewport.Height, 0f, -1f, 1f);
            device.BlendState = blend;
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = raster;
            device.SamplerStates[0] = SamplerState.LinearClamp;
            effect.Texture = flameMask;

            EnsureVerts(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 back = points[Math.Max(0, i - 1)];
                Vector2 fwd = points[Math.Min(n - 1, i + 1)];
                Vector2 dir = (fwd - back).SafeNormalize(new Vector2(0f, -1f));
                Vector2 perp = new Vector2(-dir.Y, dir.X);
                float half = Math.Max(0f, widths[i]) * 0.5f;
                Color c = colors[i];
                c.A = 0;
                float u = i / (float)(n - 1);
                Vector2 p = points[i] - Main.screenPosition;
                verts[i * 2] = new VertexPositionColorTexture(new Vector3(p + perp * half, 0f), c, new Vector2(u, 0f));
                verts[i * 2 + 1] = new VertexPositionColorTexture(new Vector3(p - perp * half, 0f), c, new Vector2(u, 1f));
            }
            effect.CurrentTechnique.Passes[0].Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, verts, 0, n * 2 - 2);
        }
    }
}
