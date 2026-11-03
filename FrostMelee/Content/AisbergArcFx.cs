using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;

namespace FrostMelee.Content
{
    // Дуга Айсберга — «ледяная плита». Не тонкая лента, а широкий пласт льда
    // по пути клинка: процедурная текстура граней (ячейки льда с трещинами,
    // пиксель 2x2, без мягких градиентов), белый внешний край, к центру —
    // светло-голубой лёд и рваный сколотый внутренний край. По граням
    // бежит блик — свет скользит по льду от острия к хвосту.
    //
    // Слои (по записанному пути клинка вокруг кисти):
    // Палитра — светлая, с самих глыбы и меча (бледно-голубой, голубой,
    // бирюза, белый). Тёмную часть палитры меча владелец забраковал
    // (03.11.2026): тёмные дуга и взрыв не сочетались с яркими глыбой и мечом.
    // 1) плита — текстура граней, обычное смешение, плотная у головы;
    // 2) бегущий блик — та же текстура аддитивно, волной вдоль плиты;
    // 3) кромка — жёсткая яркая линия по пути острия, белая только у головы.
    // Текстура запекается при первом вызове. Рисует только при ЗАКРЫТОМ
    // spriteBatch (состояния ставит сам).
    //
    // Здесь же рисуется взрыв глыбы (DrawBlast): светлое кольцо с белым
    // краем расходится до радиуса урона, из центра бьют ледяные шипы.
    public static class AisbergArcFx
    {
        // Цвета сняты со спрайтов глыбы (AisbergChunk.png) и меча (Iceberg.png)
        public static readonly Color Pale = new Color(173, 222, 242);
        public static readonly Color Sky = new Color(127, 181, 241);
        public static readonly Color Steel = new Color(91, 165, 207);
        public static readonly Color Shade = new Color(86, 125, 185);
        public static readonly Color Cyan = new Color(54, 232, 252);
        public static readonly Color Ice = new Color(220, 236, 247);
        public static readonly Color White = new Color(244, 250, 255);

        // --- плита ---
        private const int TexW = 96;              // текселей вдоль (тексель = 2 px мира)
        private const int TexH = 28;              // текселей поперёк
        private const int Cells = 18;             // ячеек льда на повтор текстуры
        private const float SlabWidth = 0.62f;    // ширина плиты у головы: доля радиуса
        private const float SlabWidthTail = 0.3f; // на хвосте
        private const float SlabAlpha = 0.95f;
        private const float EdgeWidth = 3f;       // кромка, px
        private const float GlintAlpha = 0.45f;
        private const float GlintSpeed = 0.06f;   // бег блика: доли длины за тик
        private const float GlintWidth = 0.18f;   // ширина волны блика: доля длины

        private static Texture2D slab;
        private static BasicEffect texEffect;
        private static BasicEffect colorEffect;
        private static RasterizerState raster;
        private static BlendState additive;
        private static VertexPositionColorTexture[] tverts;
        private static VertexPositionColor[] cverts;
        private static float[] uAlong;

        public static void Unload()
        {
            Texture2D s = slab;
            BasicEffect a = texEffect, b = colorEffect;
            slab = null;
            texEffect = null;
            colorEffect = null;
            raster = null;
            additive = null;
            tverts = null;
            cverts = null;
            uAlong = null;
            Main.QueueMainThreadAction(() =>
            {
                s?.Dispose();
                a?.Dispose();
                b?.Dispose();
            });
        }

        private static bool Ensure(GraphicsDevice device)
        {
            if (device == null)
                return false;
            if (texEffect == null || texEffect.IsDisposed)
                texEffect = new BasicEffect(device) { TextureEnabled = true, VertexColorEnabled = true, LightingEnabled = false, FogEnabled = false };
            if (colorEffect == null || colorEffect.IsDisposed)
                colorEffect = new BasicEffect(device) { TextureEnabled = false, VertexColorEnabled = true, LightingEnabled = false, FogEnabled = false };
            if (slab == null || slab.IsDisposed)
                slab = MakeSlab(device);
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

        private static float Hash(int a, int b)
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ 0x9E3779B9u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
        }

        // Текстура плиты: ячейки льда (Вороной, по X бесшовно), цвет ячейки
        // темнеет от внешнего края (v = 0) к толще (v = 1), швы между
        // ячейками — светлые трещины. Внутренний край рваный: у каждой
        // ячейки своя глубина, ниже неё прозрачно — край сколот по граням.
        // Всё непрозрачное или пустое, premultiplied.
        private static Texture2D MakeSlab(GraphicsDevice device)
        {
            Vector2[] seeds = new Vector2[Cells];
            float[] depth = new float[Cells];
            float[] shade = new float[Cells];
            for (int i = 0; i < Cells; i++)
            {
                seeds[i] = new Vector2((i + Hash(i, 1) * 0.8f) * TexW / Cells, Hash(i, 2) * TexH);
                depth[i] = 0.62f + 0.38f * Hash(i, 3);
                shade[i] = Hash(i, 4);
            }
            Color[] data = new Color[TexW * TexH];
            for (int y = 0; y < TexH; y++)
            {
                float v = (y + 0.5f) / TexH;
                for (int x = 0; x < TexW; x++)
                {
                    float best = float.MaxValue, second = float.MaxValue;
                    int cell = 0;
                    for (int i = 0; i < Cells; i++)
                    {
                        float dx = Math.Abs(x + 0.5f - seeds[i].X);
                        dx = Math.Min(dx, TexW - dx);
                        float dy = (y + 0.5f - seeds[i].Y) * 1.6f;
                        float d = dx * dx + dy * dy;
                        if (d < best)
                        {
                            second = best;
                            best = d;
                            cell = i;
                        }
                        else if (d < second)
                            second = d;
                    }
                    Color c;
                    if (v > depth[cell])
                        c = Color.Transparent;
                    else if (y == 0)
                        c = White;                                   // самый край — белая кромка льда
                    else if (y == 1)
                        c = Ice;
                    else
                    {
                        float k = MathHelper.Clamp(v / depth[cell], 0f, 1f);
                        Color body = k < 0.4f ? Color.Lerp(Pale, Sky, k / 0.4f)
                                   : Color.Lerp(Sky, Steel, (k - 0.4f) / 0.6f);
                        body = Color.Lerp(body, Shade, 0.3f * shade[cell] * k);
                        bool seam = (float)Math.Sqrt(second) - (float)Math.Sqrt(best) < 1.1f;
                        bool rim = depth[cell] - v < 1.5f / TexH;    // сколотый край ячейки
                        if (seam)
                            body = Color.Lerp(body, White, 0.75f - 0.3f * k);
                        else if (rim)
                            body = Color.Lerp(body, Cyan, 0.5f);
                        c = body;
                    }
                    data[y * TexW + x] = c;
                }
            }
            Texture2D tex = new Texture2D(device, TexW, TexH);
            tex.SetData(data);
            return tex;
        }

        private static void Setup(GraphicsDevice device, BasicEffect fx)
        {
            fx.World = Matrix.Identity;
            fx.View = Main.GameViewMatrix.TransformationMatrix;
            fx.Projection = Matrix.CreateOrthographicOffCenter(0f, device.Viewport.Width, device.Viewport.Height, 0f, -1f, 1f);
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = raster;
        }

        private static Color Mul(Color c, float a) =>
            new Color((byte)(c.R * a), (byte)(c.G * a), (byte)(c.B * a), (byte)(c.A * a));

        private static Color Add(Color c, float a) =>
            new Color((byte)(c.R * a), (byte)(c.G * a), (byte)(c.B * a), (byte)0);

        // angles/rOut — путь острия от головы (0) к хвосту, center — кисть.
        // env — огибающая взмаха, time — тики взмаха, seed — сдвиг рисунка
        public static void Draw(GraphicsDevice device, float[] angles, float[] rOut, int n, Vector2 center,
            float env, float time, int seed)
        {
            if (n < 2 || env <= 0.02f || !Ensure(device))
                return;
            if (tverts == null || tverts.Length < n * 2)
                tverts = new VertexPositionColorTexture[Math.Max(64, n * 2)];
            if (cverts == null || cverts.Length < n * 2)
                cverts = new VertexPositionColor[Math.Max(64, n * 2)];
            if (uAlong == null || uAlong.Length < n)
                uAlong = new float[Math.Max(32, n)];

            // u — длина по внешнему краю в текселях (2 px), от головы: грани не
            // тянутся по дуге, а лежат в мире и едут вместе с клинком
            float u0 = Hash(seed, 9) * TexW;
            uAlong[0] = u0;
            for (int i = 1; i < n; i++)
            {
                Vector2 a = angles[i - 1].ToRotationVector2() * rOut[i - 1];
                Vector2 b = angles[i].ToRotationVector2() * rOut[i];
                uAlong[i] = uAlong[i - 1] + Vector2.Distance(a, b) / 2f;
            }

            device.SamplerStates[0] = SamplerState.PointWrap;
            Setup(device, texEffect);
            texEffect.Texture = slab;

            // 1) плита
            FillSlab(n, angles, rOut, center, 0f, 0f, env, (t, f) => Mul(Color.White, SlabAlpha * f));
            DrawTex(device, n, BlendState.AlphaBlend);
            // 2) бегущий блик: волна света от острия к хвосту
            float wave = (time * GlintSpeed) % 1.4f - 0.2f;
            FillSlab(n, angles, rOut, center, 0f, 0f, env, (t, f) =>
            {
                float d = (t - wave) / GlintWidth;
                float k = Math.Max(0f, 1f - d * d);
                return Add(Color.White, GlintAlpha * k * f);
            });
            DrawTex(device, n, additive);

            // 3) кромка по пути острия
            Setup(device, colorEffect);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float f = Fade(t) * env;
                Color c = Color.Lerp(White, Cyan, Math.Min(1f, t * 5f));
                Vector2 dir = angles[i].ToRotationVector2();
                float w = EdgeWidth * (1f - 0.5f * t);
                cverts[i * 2] = new VertexPositionColor(new Vector3(center + dir * (rOut[i] + 1f) - Main.screenPosition, 0f), Add(c, f));
                cverts[i * 2 + 1] = new VertexPositionColor(new Vector3(center + dir * (rOut[i] + 1f - w) - Main.screenPosition, 0f), Add(c, f));
            }
            device.BlendState = additive;
            colorEffect.CurrentTechnique.Passes[0].Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, cverts, 0, n * 2 - 2);
        }

        // Плита плотная у головы и гаснет к хвосту
        private static float Fade(float t) => (float)Math.Pow(1f - t, 1.1f);

        private static void FillSlab(int n, float[] angles, float[] rOut, Vector2 center, float padOut, float padIn,
            float env, Func<float, float, Color> color)
        {
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float r = rOut[i];
                float width = r * MathHelper.Lerp(SlabWidth, SlabWidthTail, t);
                float rIn = Math.Max(4f, r - width - padIn);
                Vector2 dir = angles[i].ToRotationVector2();
                Color c = color(t, Fade(t) * env);
                float u = uAlong[i] / TexW;
                tverts[i * 2] = new VertexPositionColorTexture(new Vector3(center + dir * (r + padOut) - Main.screenPosition, 0f), c, new Vector2(u, 0f));
                tverts[i * 2 + 1] = new VertexPositionColorTexture(new Vector3(center + dir * rIn - Main.screenPosition, 0f), c, new Vector2(u, 1f));
            }
        }

        private static void DrawTex(GraphicsDevice device, int n, BlendState blend)
        {
            device.BlendState = blend;
            texEffect.CurrentTechnique.Passes[0].Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, tverts, 0, n * 2 - 2);
        }

        // === Взрыв глыбы ===
        // t — 0..1 время жизни вспышки, radius — радиус урона. Тёмное кольцо
        // ударной волны с ярким краем расходится до радиуса урона (видно,
        // докуда бьёт), из центра выстреливают и гаснут ледяные шипы.
        // Жёсткая геометрия, без мягкого свечения.
        private const int RingSegments = 40;
        private const int Spikes = 9;

        public static void DrawBlast(GraphicsDevice device, Vector2 center, float radius, float t, int seed)
        {
            if (t >= 1f || !Ensure(device))
                return;
            if (cverts == null || cverts.Length < (RingSegments + 1) * 2)
                cverts = new VertexPositionColor[Math.Max(128, (RingSegments + 1) * 2)];
            Setup(device, colorEffect);

            float open = 1f - (1f - t) * (1f - t) * (1f - t);
            float fade = 1f - t;
            float rWave = radius * (0.25f + 0.75f * open);
            float thick = 6f + radius * 0.22f * (1f - t);

            // светлая ледяная волна, поверх — бирюза и белый внешний край
            Ring(device, center, rWave, rWave - thick - 4f, Mul(new Color(Pale.R, Pale.G, Pale.B, 255), 0.7f * fade), BlendState.AlphaBlend);
            Ring(device, center, rWave, rWave - thick * 0.45f, Add(Cyan, 0.7f * fade), additive);
            Ring(device, center, rWave + 1f, rWave - 2f, Add(White, fade), additive);

            // шипы льда: выстреливают за первую треть и втягиваются
            float grow = t < 0.3f ? t / 0.3f : 1f - (t - 0.3f) / 0.7f;
            grow = MathHelper.Clamp(grow, 0f, 1f);
            if (grow > 0.02f)
            {
                int k = 0;
                for (int s = 0; s < Spikes; s++)
                {
                    float ang = MathHelper.TwoPi * (s + Hash(seed, s) * 0.6f) / Spikes;
                    float len = radius * (0.45f + 0.4f * Hash(seed, s + 50)) * grow;
                    float half = 4f + 5f * Hash(seed, s + 90);
                    Vector2 dir = ang.ToRotationVector2();
                    Vector2 side = new Vector2(-dir.Y, dir.X) * half;
                    Vector2 c0 = center - Main.screenPosition;
                    // светло-голубое тело шипа, сверху бирюза и белая сердцевина
                    Tri(ref k, c0 + side * 1.4f, c0 - side * 1.4f, c0 + dir * (len + 4f), Mul(new Color(Sky.R, Sky.G, Sky.B, 255), 0.9f * fade));
                }
                device.BlendState = BlendState.AlphaBlend;
                colorEffect.CurrentTechnique.Passes[0].Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleList, cverts, 0, k / 3);
                k = 0;
                for (int s = 0; s < Spikes; s++)
                {
                    float ang = MathHelper.TwoPi * (s + Hash(seed, s) * 0.6f) / Spikes;
                    float len = radius * (0.45f + 0.4f * Hash(seed, s + 50)) * grow;
                    float half = 4f + 5f * Hash(seed, s + 90);
                    Vector2 dir = ang.ToRotationVector2();
                    Vector2 side = new Vector2(-dir.Y, dir.X) * half;
                    Vector2 c0 = center - Main.screenPosition;
                    Tri(ref k, c0 + side, c0 - side, c0 + dir * len, Add(Cyan, 0.6f * fade));
                    Tri(ref k, c0 + side * 0.35f, c0 - side * 0.35f, c0 + dir * len * 0.8f, Add(White, fade));
                }
                device.BlendState = additive;
                colorEffect.CurrentTechnique.Passes[0].Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleList, cverts, 0, k / 3);
            }
        }

        private static void Tri(ref int k, Vector2 a, Vector2 b, Vector2 c, Color col)
        {
            cverts[k++] = new VertexPositionColor(new Vector3(a, 0f), col);
            cverts[k++] = new VertexPositionColor(new Vector3(b, 0f), col);
            cverts[k++] = new VertexPositionColor(new Vector3(c, 0f), col);
        }

        private static void Ring(GraphicsDevice device, Vector2 center, float rOut, float rIn, Color col, BlendState blend)
        {
            rIn = Math.Max(0f, rIn);
            for (int i = 0; i <= RingSegments; i++)
            {
                Vector2 dir = (MathHelper.TwoPi * i / RingSegments).ToRotationVector2();
                cverts[i * 2] = new VertexPositionColor(new Vector3(center + dir * rOut - Main.screenPosition, 0f), col);
                cverts[i * 2 + 1] = new VertexPositionColor(new Vector3(center + dir * rIn - Main.screenPosition, 0f), col);
            }
            device.BlendState = blend;
            colorEffect.CurrentTechnique.Passes[0].Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, cverts, 0, RingSegments * 2);
        }

        // Пик взмаха: от толщи плиты у головы отваливаются куски льда —
        // крупная ванильная крошка, падает вниз
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
            Lighting.AddLight(center + dir * radius, 0.2f * env, 0.7f * env, 0.85f * env);
            Lighting.AddLight(center + dir * radius * 0.6f, 0.05f * env, 0.3f * env, 0.4f * env);
        }
    }
}
