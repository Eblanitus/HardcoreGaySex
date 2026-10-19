using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Визуал приёмов Ice Blade. Логика обратна копью: копьё в дуге СБРАСЫВАЕТ накопленный
    // иней (широкая морозная комета), а клинок ТОЧИТСЯ об воздух — с него счищается
    // собственная наледь, и чем он острее, тем тоньше и ярче всё, что он оставляет.
    // Полировка P (0 — в инее, 1 — отточен) читается на глаз:
    //   P≈0 — матовая морозная дымка, грубые риски, сыплется ледяная стружка;
    //   P≈1 — стружки нет, волосяная белая кромка, бегущий блик и искры правки.
    // Два режима точат по-разному:
    //   дуга  — правка всей длины: блик бежит от пяты к острию, за кромкой веер рисок;
    //   тычок — правка острия: воздух рассекается иглой, обтекает клинок V-волной
    //           и гонит блик от острия к пяте.
    // Только визуал: урон, хитбоксы и полировку не трогает.
    public static class IceBladeFx
    {
        // --- Палитра ---
        public static readonly Color FrostMatte = new Color(170, 195, 215);   // наледь, стружка, дымка
        public static readonly Color ScratchRough = new Color(115, 145, 175); // грубая риска
        public static readonly Color ScratchHoned = new Color(150, 215, 255); // риска на отточенном
        public static readonly Color EdgeCore = new Color(245, 252, 255);     // кромка, блик
        public static readonly Color EdgeRim = new Color(120, 200, 255);      // ореол кромки

        // Полировка владельца: 0 — весь в инее, 1 — отточен (та же кривая, что кроссфейд спрайта)
        public static float PolishOf(Player player) => 1f - IceBladeVisual.Mix(player, ItemID.IceBlade);

        public static float Smooth01(float x)
        {
            x = MathHelper.Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        // Аддитивный цвет: альфа уходит в яркость RGB (A = 0)
        public static Color Additive(Color c, float alpha)
        {
            Color r = c * MathHelper.Clamp(alpha, 0f, 1f);
            r.A = 0;
            return r;
        }

        public static Texture2D Glow()
        {
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            return glow == null || glow.IsDisposed ? null : glow;
        }

        // =====================================================================
        // Частицы: стружка наледи (матовая) и искры правки (светятся)
        // =====================================================================

        public static void SpawnShaving(Vector2 pos, Vector2 vel, float scale)
        {
            if (Main.dedServ)
                return;
            Dust d = Dust.NewDustPerfect(pos, ModContent.DustType<IceShavingDust>(), vel, 0, FrostMatte, scale);
            d.noGravity = true;
        }

        public static void SpawnSpark(Vector2 pos, Vector2 vel, float scale)
        {
            if (Main.dedServ)
                return;
            Dust d = Dust.NewDustPerfect(pos, ModContent.DustType<HoneSparkDust>(), vel, 0, EdgeCore, scale);
            d.noGravity = true;
        }

        // Сколько штук выпустить при «ожидаемом» rate за тик: целая часть + шанс на дробную
        public static int Count(float rate)
        {
            if (rate <= 0f)
                return 0;
            int n = (int)rate;
            if (Main.rand.NextFloat() < rate - n)
                n++;
            return n;
        }

        // =====================================================================
        // Спрайтовые элементы (рисуются при ОТКРЫТОМ spriteBatch)
        // =====================================================================

        // Мазок вдоль отрезка a→b: растянутое мягкое пятно (мировые координаты)
        public static void Stroke(Texture2D glow, Vector2 a, Vector2 b, float width, Color color)
        {
            if (width < 0.3f)
                return;
            Vector2 delta = b - a;
            Main.EntitySpriteDraw(glow, (a + b) * 0.5f - Main.screenPosition, null, color, delta.ToRotation(),
                glow.Size() / 2f, new Vector2((delta.Length() + width) / glow.Width, width / glow.Height),
                SpriteEffects.None, 0f);
        }

        public static void Dot(Texture2D glow, Vector2 pos, float size, Color color)
        {
            Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, color, 0f, glow.Size() / 2f,
                size / glow.Width, SpriteEffects.None, 0f);
        }

        // Правленая часть клинка и бегущий по ней блик.
        // from — откуда идёт правка (пята для дуги, остриё для тычка), glint — где блик сейчас.
        public static void DrawHone(Texture2D glow, Vector2 from, Vector2 glint, Vector2 bladeDir, float polish, float alpha)
        {
            if (alpha <= 0.02f)
                return;
            // Свежая кромка позади блика: на шершавом едва видна, на отточенном — тонкая белая
            if (Vector2.DistanceSquared(from, glint) > 4f) {
                Stroke(glow, from, glint, 3.2f, Additive(EdgeRim, alpha * (0.15f + 0.3f * polish)));
                Stroke(glow, from, glint, 1.3f, Additive(EdgeCore, alpha * (0.2f + 0.6f * polish)));
            }
            // Блик: продольный штрих по клинку + короткий поперечный + ядро
            float size = 22f * (0.7f + 0.5f * polish);
            Vector2 along = bladeDir * size * 0.5f;
            Stroke(glow, glint - along, glint + along, 3.5f, Additive(EdgeRim, alpha * 0.8f));
            Vector2 across = bladeDir.RotatedBy(MathHelper.PiOver2) * size * 0.22f;
            Stroke(glow, glint - across, glint + across, 2f, Additive(EdgeCore, alpha * (0.4f + 0.5f * polish)));
            Dot(glow, glint, 9f * (0.6f + 0.6f * polish), Additive(EdgeCore, alpha));
        }

        // «Дзинь» — звезда отточенного металла: длинный луч вдоль rotation, короче поперёк,
        // тонкие диагонали. На шершавом клинке маленькая и тусклая.
        public static void DrawTing(Texture2D glow, Vector2 pos, float size, float rotation, float polish, float alpha)
        {
            if (alpha <= 0.02f || size <= 1f)
                return;
            Vector2 origin = glow.Size() / 2f;
            Vector2 p = pos - Main.screenPosition;
            float thin = 0.055f;
            Color rim = Additive(EdgeRim, alpha * 0.9f);
            Color core = Additive(EdgeCore, alpha);
            Main.EntitySpriteDraw(glow, p, null, rim, rotation, origin,
                new Vector2(size / glow.Width, size * thin * 1.6f / glow.Height), SpriteEffects.None, 0f);
            Main.EntitySpriteDraw(glow, p, null, core, rotation, origin,
                new Vector2(size * 0.9f / glow.Width, size * thin / glow.Height), SpriteEffects.None, 0f);
            Main.EntitySpriteDraw(glow, p, null, core, rotation + MathHelper.PiOver2, origin,
                new Vector2(size * 0.55f / glow.Width, size * thin / glow.Height), SpriteEffects.None, 0f);
            float diag = size * 0.28f * polish;
            if (diag > 1f) {
                Main.EntitySpriteDraw(glow, p, null, rim, rotation + MathHelper.PiOver4, origin,
                    new Vector2(diag / glow.Width, size * thin * 0.8f / glow.Height), SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(glow, p, null, rim, rotation - MathHelper.PiOver4, origin,
                    new Vector2(diag / glow.Width, size * thin * 0.8f / glow.Height), SpriteEffects.None, 0f);
            }
            Main.EntitySpriteDraw(glow, p, null, core, 0f, origin, size * 0.16f / glow.Width, SpriteEffects.None, 0f);
        }

        // =====================================================================
        // Дуга: вершинные полосы по истории углов клинка
        // =====================================================================
        // Три слоя от центра игрока, у каждого своя маска:
        //   дымка — матовая морозная крошка у кромки, только у шершавого клинка;
        //   риски — тонкие концентрические царапины правки по всему вееру;
        //   кромка — волосяная линия рассечённого воздуха на радиусе линии дуги.
        // u — вдоль полосы (0 голова, 1 хвост), v — поперёк (0 внешний край, 1 внутренний).

        private const int MaxPoints = 32;
        private static BasicEffect effect;
        private static Texture2D hazeMask, scratchMask, edgeMask;
        private static BlendState additive;
        private static RasterizerState raster;
        private static readonly VertexPositionColorTexture[] verts = new VertexPositionColorTexture[MaxPoints * 2];

        public static void Unload()
        {
            if (Main.dedServ)
                return;
            BasicEffect e = effect;
            Texture2D a = hazeMask, b = scratchMask, c = edgeMask;
            effect = null;
            hazeMask = scratchMask = edgeMask = null;
            additive = null;
            raster = null;
            Main.QueueMainThreadAction(() =>
            {
                e?.Dispose();
                a?.Dispose();
                b?.Dispose();
                c?.Dispose();
            });
        }

        private static uint Hash(int x, int y, int seed)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519u);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }

        private static float Hash01(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFF) / 65535f;

        // Value-шум, по x замкнут с периодом periodX (маска тайлится вдоль полосы)
        private static float Noise(float x, float y, int periodX, int seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int xa = ((x0 % periodX) + periodX) % periodX, xb = (xa + 1) % periodX;
            float a = MathHelper.Lerp(Hash01(xa, y0, seed), Hash01(xb, y0, seed), fx);
            float b = MathHelper.Lerp(Hash01(xa, y0 + 1, seed), Hash01(xb, y0 + 1, seed), fx);
            return MathHelper.Lerp(a, b, fy);
        }

        private static Texture2D MakeMask(GraphicsDevice device, int w, int h, Func<float, float, float> value)
        {
            Texture2D tex = new Texture2D(device, w, h);
            Color[] data = new Color[w * h];
            for (int y = 0; y < h; y++) {
                float v = (y + 0.5f) / h;
                for (int x = 0; x < w; x++) {
                    float u = (x + 0.5f) / w;
                    int b = (int)(MathHelper.Clamp(value(u, v), 0f, 1f) * 255f + 0.5f);
                    data[y * w + x] = new Color(b, b, b, b);   // premultiplied: rgb = alpha
                }
            }
            tex.SetData(data);
            return tex;
        }

        // Дымка: зернистая морозная крошка, тайлится по u (рисунок плывёт к хвосту)
        private static float HazeValue(float u, float v)
        {
            float profile = (float)Math.Exp(-Math.Pow((v - 0.4f) / 0.3f, 2));
            float grain = Noise(u * 32f, v * 7f, 32, 11);
            float fine = Noise(u * 96f, v * 20f, 96, 23);
            float g = 0.25f + 0.75f * (float)Math.Pow(grain, 1.6f) * (0.55f + 0.45f * fine);
            float speck = Hash01((int)(u * 160f), (int)(v * 24f), 37) > 0.94f ? 0.55f : 0f;
            return profile * Math.Min(1f, g + speck);
        }

        // Риски: несколько тонких концентрических царапин разной длины, с рваной яркостью
        private const int Scratches = 7;
        private static readonly float[] scratchV = new float[Scratches];
        private static readonly float[] scratchLen = new float[Scratches];
        private static readonly float[] scratchW = new float[Scratches];
        private static readonly float[] scratchGain = new float[Scratches];

        private static float ScratchValue(float u, float v)
        {
            float best = 0f;
            for (int k = 0; k < Scratches; k++) {
                float d = (v - scratchV[k]) / scratchW[k];
                float line = (float)Math.Exp(-d * d);
                if (line < 0.01f)
                    continue;
                float len = scratchLen[k];
                float along = Smooth01((len - u) / (len * 0.35f)) * Smooth01(u / 0.025f);
                float torn = 0.45f + 0.55f * Noise(u * 40f, k * 3.1f, 40, 53 + k);
                best = Math.Max(best, line * along * torn * scratchGain[k]);
            }
            return best;
        }

        // Кромка: резкая волосяная линия + узкий ореол. Профиль только поперёк — длину гасят вершины
        private const float EdgeLineV = 0.38f;
        private static float EdgeValue(float u, float v)
        {
            float d = v - EdgeLineV;
            return (float)Math.Exp(-Math.Pow(d / 0.07f, 2)) + 0.3f * (float)Math.Exp(-Math.Pow(d / 0.25f, 2));
        }

        private static bool Ensure(GraphicsDevice device)
        {
            if (device == null)
                return false;
            if (effect == null || effect.IsDisposed) {
                effect = new BasicEffect(device)
                {
                    TextureEnabled = true,
                    VertexColorEnabled = true,
                    LightingEnabled = false,
                    FogEnabled = false
                };
            }
            if (hazeMask == null || hazeMask.IsDisposed)
                hazeMask = MakeMask(device, 256, 32, HazeValue);
            if (scratchMask == null || scratchMask.IsDisposed) {
                Random rng = new Random(19);
                for (int k = 0; k < Scratches; k++) {
                    scratchV[k] = (k + 0.25f + 0.5f * (float)rng.NextDouble()) / Scratches;
                    scratchLen[k] = 0.35f + 0.6f * (float)rng.NextDouble();
                    scratchW[k] = 0.008f + 0.008f * (float)rng.NextDouble();
                    scratchGain[k] = 0.55f + 0.45f * (float)rng.NextDouble();
                }
                scratchMask = MakeMask(device, 256, 160, ScratchValue);
            }
            if (edgeMask == null || edgeMask.IsDisposed)
                edgeMask = MakeMask(device, 8, 64, EdgeValue);
            additive ??= new BlendState
            {
                ColorSourceBlend = Blend.One,
                ColorDestinationBlend = Blend.One,
                AlphaSourceBlend = Blend.Zero,
                AlphaDestinationBlend = Blend.One
            };
            raster ??= new RasterizerState { CullMode = CullMode.None };
            return true;
        }

        // Один слой: полоса из n пар вершин между радиусами outer(i)+outAdd и inner(i).
        // fade — показатель затухания к хвосту (через альфу вершин), uRepeat/uScroll — тайлинг маски.
        private static void Strip(GraphicsDevice device, Texture2D mask, SamplerState sampler, float[] angles, float[] radius,
            int n, Vector2 center, float outAdd, float inAdd, float innerFixed, Color head, Color tail, float alpha,
            float fade, float uRepeat, float uScroll)
        {
            for (int i = 0; i < n; i++) {
                float t = i / (float)(n - 1);
                Vector2 dir = angles[i].ToRotationVector2();
                float rOut = radius[i] + outAdd;
                float rIn = innerFixed > 0f ? innerFixed : radius[i] + inAdd;
                float a = alpha * (float)Math.Pow(1f - t, fade) * Smooth01(t / 0.06f + 0.35f);
                Color c = Color.Lerp(head, tail, t) * a;
                c.A = 0;
                float uv = t * uRepeat - uScroll;
                verts[i * 2] = new VertexPositionColorTexture(new Vector3(center + dir * rOut - Main.screenPosition, 0f), c, new Vector2(uv, 0f));
                verts[i * 2 + 1] = new VertexPositionColorTexture(new Vector3(center + dir * rIn - Main.screenPosition, 0f), c, new Vector2(uv, 1f));
            }
            effect.Texture = mask;
            device.SamplerStates[0] = sampler;
            effect.CurrentTechnique.Passes[0].Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, verts, 0, n * 2 - 2);
        }

        // angles[0] — угол клинка сейчас, дальше назад по тикам; radius — радиус линии дуги на тот тик.
        // scratchInner — докуда к центру идут риски (px). Вызывать при ОТКРЫТОМ spriteBatch:
        // батч закрывается на время полос и открывается заново с настройками снарядов.
        public static void DrawArcStrips(Vector2 center, float[] angles, float[] radius, int n, float scratchInner,
            float polish, float env, float time)
        {
            if (Main.dedServ || n < 2 || env <= 0.02f)
                return;
            n = Math.Min(n, MaxPoints);
            GraphicsDevice device = Main.graphics.GraphicsDevice;
            if (!Ensure(device))
                return;
            Main.spriteBatch.End();
            try {
                effect.World = Matrix.Identity;
                effect.View = Main.GameViewMatrix.TransformationMatrix;
                effect.Projection = Matrix.CreateOrthographicOffCenter(0f, device.Viewport.Width, device.Viewport.Height, 0f, -1f, 1f);
                device.BlendState = additive;
                device.DepthStencilState = DepthStencilState.None;
                device.RasterizerState = raster;

                float rough = 1f - polish;
                // дымка: крошка наледи, сорванная с кромки (у отточенного её нет)
                if (rough > 0.03f)
                    Strip(device, hazeMask, SamplerState.LinearWrap, angles, radius, n, center, 6f, -18f, 0f,
                        FrostMatte, ScratchRough, env * rough * 0.55f, 1.3f, 1.6f, time * 0.035f);
                // риски правки: грубые и частые в инее, тонкие и холодные на отточенном
                Strip(device, scratchMask, SamplerState.LinearClamp, angles, radius, n, center, -2f, 0f, scratchInner,
                    Color.Lerp(ScratchRough, ScratchHoned, polish), Color.Lerp(ScratchRough, EdgeRim, polish * 0.5f),
                    env * MathHelper.Lerp(0.75f, 0.4f, polish), 0.5f, 1f, 0f);
                // кромка: тем ярче и тоньше, чем острее клинок
                float edgeWidth = MathHelper.Lerp(11f, 6f, polish);
                Strip(device, edgeMask, SamplerState.LinearClamp, angles, radius, n, center, edgeWidth * EdgeLineV, -edgeWidth * (1f - EdgeLineV), 0f,
                    Color.Lerp(FrostMatte, EdgeCore, polish), Color.Lerp(ScratchRough, EdgeRim, polish),
                    env * MathHelper.Lerp(0.45f, 1f, polish), MathHelper.Lerp(1.6f, 1.1f, polish), 1f, 0f);
            }
            finally {
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                    DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
            }
        }
    }

    // Ледяная стружка: сорванная воздухом наледь. Матовая (не светится), кувыркается —
    // длина мигает от плашмя к ребру, и плашмя она на миг ловит свет.
    public class IceShavingDust : ModDust
    {
        public override string Texture => "Terraria/Images/Dust";

        public override void OnSpawn(Dust dust)
        {
            dust.fadeIn = 0f;                              // возраст, тиков
            dust.alpha = Main.rand.Next(26, 42);           // срок жизни, тиков
            dust.rotation = Main.rand.NextFloat(MathHelper.TwoPi);
            dust.frame = new Rectangle(Main.rand.Next(1000), 0, 0, 0); // фаза кувырка
            dust.noLight = true;
        }

        public override bool Update(Dust dust)
        {
            dust.fadeIn++;
            dust.position += dust.velocity;
            dust.velocity *= 0.9f;
            dust.velocity.Y += 0.04f;                      // оседает медленно, как снег
            dust.rotation += dust.velocity.X * 0.08f + 0.05f;
            if (dust.fadeIn >= dust.alpha)
                dust.active = false;
            return false;
        }

        public override bool PreDraw(Dust dust)
        {
            Texture2D glow = IceBladeFx.Glow();
            if (glow == null)
                return false;
            float age = dust.fadeIn / Math.Max(1, dust.alpha);
            float a = IceBladeFx.Smooth01(age / 0.1f) * (1f - age * age);
            float tumble = (float)Math.Cos(dust.fadeIn * 0.35f + dust.frame.X * 0.1f);
            float len = (2.5f + 6.5f * Math.Abs(tumble)) * dust.scale;
            float width = 2.6f * dust.scale;
            Color light = Lighting.GetColor((int)(dust.position.X / 16f), (int)(dust.position.Y / 16f));
            Color body = dust.color.MultiplyRGB(Color.Lerp(light, Color.White, 0.55f)) * (a * 0.85f);
            Vector2 origin = glow.Size() / 2f;
            Vector2 p = dust.position - Main.screenPosition;
            Main.spriteBatch.Draw(glow, p, null, body, dust.rotation, origin,
                new Vector2(len / glow.Width, width / glow.Height), SpriteEffects.None, 0f);
            // плашмя к свету — мгновенный блеск
            if (Math.Abs(tumble) > 0.93f)
                Main.spriteBatch.Draw(glow, p, null, IceBladeFx.Additive(IceBladeFx.EdgeCore, a * 0.7f), 0f, origin,
                    3.5f * dust.scale / glow.Width, SpriteEffects.None, 0f);
            return false;
        }
    }

    // Искра правки: короткий светящийся штрих по скорости, быстро гаснет
    public class HoneSparkDust : ModDust
    {
        public override string Texture => "Terraria/Images/Dust";

        public override void OnSpawn(Dust dust)
        {
            dust.fadeIn = 0f;
            dust.alpha = Main.rand.Next(8, 15);
            dust.noLight = true;
        }

        public override bool Update(Dust dust)
        {
            dust.fadeIn++;
            dust.position += dust.velocity;
            dust.velocity *= 0.87f;
            dust.velocity.Y += 0.06f;
            if (dust.fadeIn >= dust.alpha)
                dust.active = false;
            return false;
        }

        public override bool PreDraw(Dust dust)
        {
            Texture2D glow = IceBladeFx.Glow();
            if (glow == null)
                return false;
            float age = dust.fadeIn / Math.Max(1, dust.alpha);
            float a = 1f - age;
            Vector2 v = dust.velocity;
            float speed = v.Length();
            float len = (2f + speed * 2.4f) * dust.scale;
            Vector2 origin = glow.Size() / 2f;
            Vector2 p = dust.position - v * 0.6f - Main.screenPosition;
            float rot = speed > 0.01f ? v.ToRotation() : 0f;
            Main.spriteBatch.Draw(glow, p, null, IceBladeFx.Additive(IceBladeFx.EdgeRim, a * 0.8f), rot, origin,
                new Vector2(len / glow.Width, 2.4f * dust.scale / glow.Height), SpriteEffects.None, 0f);
            Main.spriteBatch.Draw(glow, p, null, IceBladeFx.Additive(dust.color, a), rot, origin,
                new Vector2(len * 0.7f / glow.Width, 1.1f * dust.scale / glow.Height), SpriteEffects.None, 0f);
            return false;
        }
    }
}
