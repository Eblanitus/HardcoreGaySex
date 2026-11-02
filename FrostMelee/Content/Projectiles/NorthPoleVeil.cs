using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    // Second trail of the North Pole arc (owner 02.11.2026: "other weapons often have two trails
    // at once"). The aurora ribbon follows the spear; this one stays in the world where the tip
    // passed: a veil of aurora curtains hanging DOWN from the tip's path, like a real aurora
    // hangs from the sky. It unfolds downwards, shimmers (curtains flow along), and fades in
    // under a second. Same build as the other ribbons: dark normal-blend backing first (reads on
    // snow), then an additive mint -> teal -> violet body through a curtain mask. Visual only.
    public class NorthPoleVeil : ModSystem
    {
        public const int Life = 52;                 // ticks a point of the veil lives
        public const float Drop = 54f;              // full curtain height under the path, px
        public const float UnfoldTicks = 10f;       // curtains unfold downwards this fast
        public const float BackAlpha = 0.6f;        // dark backing (reads on snow)
        public const float BodyAlpha = 0.85f;
        public const float UScale = 1f / 110f;      // mask repeats every 110 px along the path
        public const float Flow = 0.012f;           // curtains flow along, cycles per tick

        private static readonly Color Night = new Color(4, 8, 30);
        private static readonly Color Mint = new Color(60, 255, 140);
        private static readonly Color Teal = new Color(20, 200, 220);
        private static readonly Color Violet = new Color(185, 90, 255);

        private struct Point
        {
            public Vector2 Pos;
            public int Born, Swing;
            public float U;   // distance along the path, px
        }

        private static readonly List<Point> points = new();
        private static int tick;
        private static BasicEffect effect;
        private static Texture2D mask;
        private static BlendState additive;
        private static RasterizerState noCull;
        private static VertexPositionColorTexture[] verts = new VertexPositionColorTexture[256];

        // Called every tick of a North Pole swing with points along the tip's path since the last tick
        public static void Add(Vector2 pos, int swing)
        {
            if (Main.dedServ) return;
            float u = 0f;
            if (points.Count > 0 && points[^1].Swing == swing)
            {
                Point last = points[^1];
                float d = Vector2.Distance(last.Pos, pos);
                if (d < 4f) return;
                u = last.U + d;
            }
            points.Add(new Point { Pos = pos, Born = tick, Swing = swing, U = u });
            if (points.Count > 600) points.RemoveAt(0);
        }

        public override void OnWorldUnload() => points.Clear();

        public override void Unload()
        {
            BasicEffect e = effect; Texture2D m = mask;
            effect = null; mask = null;
            if (e != null || m != null)
                Main.QueueMainThreadAction(() => { e?.Dispose(); m?.Dispose(); });
        }

        public override void PostUpdateEverything()
        {
            tick++;
            int dead = 0;
            while (dead < points.Count && tick - points[dead].Born >= Life) dead++;
            if (dead > 0) points.RemoveRange(0, dead);
        }

        private static bool Ensure(GraphicsDevice device)
        {
            if (device == null) return false;
            if (effect == null || effect.IsDisposed)
                effect = new BasicEffect(device) { TextureEnabled = true, VertexColorEnabled = true, LightingEnabled = false, FogEnabled = false };
            if (mask == null || mask.IsDisposed)
            {
                // u along the path (wraps), v down the curtain: soft vertical folds, bright at the top
                const int w = 128, h = 64;
                mask = new Texture2D(device, w, h);
                Color[] data = new Color[w * h];
                for (int y = 0; y < h; y++)
                {
                    float v = (y + 0.5f) / h;
                    float fall = MathF.Pow(1f - v, 1.4f) * MathHelper.Clamp(v / 0.06f, 0f, 1f);
                    for (int x = 0; x < w; x++)
                    {
                        float u = (x + 0.5f) / w * MathHelper.TwoPi;
                        float folds = 0.45f + 0.35f * MathF.Pow(0.5f + 0.5f * MathF.Sin(u * 3f), 2f)
                            + 0.2f * MathF.Pow(0.5f + 0.5f * MathF.Sin(u * 7f + 1.3f), 3f);
                        int b = (int)(MathHelper.Clamp(folds * fall, 0f, 1f) * 255f + 0.5f);
                        data[y * w + x] = new Color(b, b, b, b);
                    }
                }
                mask.SetData(data);
            }
            additive ??= new BlendState
            {
                ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.One,
                AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One
            };
            noCull ??= new RasterizerState { CullMode = CullMode.None };
            return true;
        }

        // Behind NPCs and players, like a sky phenomenon, in front of tiles
        public override void PostDrawTiles()
        {
            if (Main.dedServ || points.Count < 2) return;
            GraphicsDevice device = Main.graphics.GraphicsDevice;
            if (!Ensure(device)) return;
            effect.World = Matrix.Identity;
            effect.View = Main.GameViewMatrix.TransformationMatrix;
            effect.Projection = Matrix.CreateOrthographicOffCenter(0f, device.Viewport.Width, device.Viewport.Height, 0f, -1f, 1f);
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = noCull;
            device.SamplerStates[0] = SamplerState.LinearWrap;
            int start = 0;
            for (int i = 1; i <= points.Count; i++)
            {
                if (i < points.Count && points[i].Swing == points[start].Swing) continue;
                DrawRun(device, start, i - start);
                start = i;
            }
        }

        private static void DrawRun(GraphicsDevice device, int start, int count)
        {
            if (count < 2) return;
            if (verts.Length < count * 2) verts = new VertexPositionColorTexture[count * 2];
            float flow = tick * Flow;
            for (int pass = 0; pass < 2; pass++)
            {
                bool back = pass == 0;
                for (int k = 0; k < count; k++)
                {
                    Point p = points[start + k];
                    int age = tick - p.Born;
                    float t = age / (float)Life;
                    float fade = MathHelper.Clamp(age / 3f, 0f, 1f) * (1f - t * t);
                    // the newest end (the tip) and the oldest end taper off
                    float ends = MathHelper.Clamp(k / 3f, 0f, 1f) * MathHelper.Clamp((count - 1 - k) / 3f, 0f, 1f);
                    float a = fade * (0.35f + 0.65f * ends);
                    float drop = Drop * MathHelper.Clamp(age / UnfoldTicks, 0.15f, 1f) * (back ? 1.15f : 1f);
                    float u = p.U * UScale - flow;
                    Vector2 top = p.Pos - Main.screenPosition - (back ? new Vector2(0, 3) : Vector2.Zero);
                    Vector2 bottom = top + new Vector2(MathF.Sin(age * 0.08f + p.U * 0.03f) * 4f, drop);
                    Color cTop, cBottom;
                    if (back)
                    {
                        cTop = Night * (BackAlpha * a);
                        cBottom = Night * (BackAlpha * a * 0.6f);
                    }
                    else
                    {
                        float hue = 0.5f + 0.5f * MathF.Sin(p.U * 0.02f - tick * 0.05f);
                        cTop = Color.Lerp(Mint, Color.Lerp(Mint, Color.White, 0.4f), hue * 0.5f) * (BodyAlpha * a);
                        cBottom = Color.Lerp(Teal, Violet, 0.4f + 0.6f * t) * (BodyAlpha * a);
                        cTop.A = cBottom.A = 0;
                    }
                    verts[k * 2] = new VertexPositionColorTexture(new Vector3(top, 0f), cTop, new Vector2(u, 0f));
                    verts[k * 2 + 1] = new VertexPositionColorTexture(new Vector3(bottom, 0f), cBottom, new Vector2(u, 1f));
                }
                device.BlendState = back ? BlendState.AlphaBlend : additive;
                effect.Texture = mask;
                effect.CurrentTechnique.Passes[0].Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleStrip, verts, 0, count * 2 - 2);
            }
        }
    }
}
