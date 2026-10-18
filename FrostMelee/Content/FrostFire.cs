using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content
{
    // Ледяной огонь на Фростборне. Меч не обрастает льдом и не растягивается —
    // по клинку течёт большое синее пламя: языки стоят ОСНОВАНИЕМ на лезвии
    // и тянутся к острию, за остриём выходит длинная грива, вверх летят искры.
    // Огонь выходит за остриё на IceTipPerCharge за заряд — это и есть прибавка
    // длины: по ней же бьют хитбоксы.
    public static class FrostFire
    {
        private static readonly Color IceCore = new Color(245, 252, 255);
        private static readonly Color IceTail = new Color(130, 215, 255);
        private static readonly Color IceEdge = new Color(45, 120, 240);

        // Пиксель-арт текстуры (point sampling в игре, поэтому без градиентов):
        // FrostFlame.png 32×64 — язык огня (белое ядро, голубь, синяя кромка);
        // FrostSpark.png 8×8 — искра-ромб
        private static Texture2D flameTex;
        private static Texture2D sparkTex;
        private static bool texTried;
        private static Texture2D FlameTex => Load(ref flameTex);
        private static Texture2D SparkTex => Load(ref sparkTex);

        private static Texture2D Load(ref Texture2D slot)
        {
            if (!texTried && !Main.dedServ)
            {
                texTried = true;
                try { flameTex = ModContent.Request<Texture2D>("FrostMelee/Textures/Effects/FrostFlame").Value; }
                catch { flameTex = null; }
                try { sparkTex = ModContent.Request<Texture2D>("FrostMelee/Textures/Effects/FrostSpark").Value; }
                catch { sparkTex = null; }
            }
            return (slot == null || slot.IsDisposed) ? null : slot;
        }

        // Сколько пикселей пламени выходит за остриё (0 — зарядов нет)
        public static float TipExtra(Player p, int itemType)
        {
            if (p == null || !p.active || itemType != ItemID.Frostbrand)
                return 0f;
            return p.GetModPlayer<FrostKindlePlayer>().ReachCharges * FrostKindle.IceTipPerCharge;
        }

        // === Рисование: собираем DrawData, затем отдаём ===
        // Снаряды рисуют сразу, слой игрока кладёт в кэш поверх меча
        private static readonly List<DrawData> batch = new List<DrawData>();

        // butt — приклад спрайта, tip — остриё, heat — 0..1 (сколько зарядов из максимума)
        public static void Draw(Vector2 butt, Vector2 tip, float heat)
        {
            Build(butt, tip, heat);
            for (int i = 0; i < batch.Count; i++)
            {
                DrawData d = batch[i];
                Main.EntitySpriteDraw(d.texture, d.position, d.sourceRect, d.color,
                    d.rotation, d.origin, d.scale, d.effect, 0f);
            }
            batch.Clear();
        }

        // Тот же огонь, но в кэш слоя игрока — тогда он ляжет ПОВЕРХ меча в руке
        public static void Draw(ref PlayerDrawSet drawInfo, Vector2 butt, Vector2 tip, float heat)
        {
            Build(butt, tip, heat);
            for (int i = 0; i < batch.Count; i++)
                drawInfo.DrawDataCache.Add(batch[i]);
            batch.Clear();
        }

        private static Color A(Color c, float a)
        {
            a = MathHelper.Clamp(a, 0f, 1f);
            c.A = (byte)(255f * a);
            return c;
        }

        // origin: default — центр спрайта; для языков отдаём низ (основание на лезвии)
        private static void Add(Texture2D tex, Vector2 pos, Color color, float rotation,
            Vector2 scale, Vector2 origin)
        {
            if (tex == null || scale.X * tex.Width < 1.5f || scale.Y * tex.Height < 1.5f)
                return;
            batch.Add(new DrawData(tex, pos - Main.screenPosition, null, color, rotation,
                origin, scale, SpriteEffects.None, 0f));
        }

        private static void Build(Vector2 butt, Vector2 tip, float heat)
        {
            batch.Clear();
            if (Main.dedServ || heat <= 0.01f)
                return;
            Texture2D flame = FlameTex;
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (flame == null && glow == null)
                return;

            float h = MathHelper.Clamp(heat, 0f, 1f);
            Vector2 axis = (tip - butt).SafeNormalize(Vector2.UnitX);
            float length = Vector2.Distance(butt, tip);
            if (length < 8f)
                return;
            Vector2 normal = new Vector2(-axis.Y, axis.X);
            float time = Main.GlobalTimeWrappedHourly;
            float rot = axis.ToRotation();
            // Спрайт смотрит вверх — поворот +90° кладёт языки вдоль оси клинка к острию
            float flameRot = rot + MathHelper.PiOver2;
            float extra = FrostKindle.MaxReachCharges * FrostKindle.IceTipPerCharge * h;
            // Пульс — огонь живой, яркость дышит
            float pulse = 0.92f + 0.16f * (float)Math.Sin(time * 5.3);
            float power = (0.60f + 0.40f * h) * pulse;
            // Низ языка = точка привязки: основание стоит на лезвии, тело тянет к острию
            Vector2 baseOrigin = flame != null ? new Vector2(flame.Width * 0.5f, flame.Height) : Vector2.Zero;

            // 1) Свечение вдоль всего лезвия — сразу видно с любой стороны
            if (glow != null)
            {
                Vector2 mid = (butt + tip) * 0.5f;
                float w1 = 12f + 16f * h;
                Add(glow, mid, A(IceEdge, 0.40f * power), rot,
                    new Vector2((length + w1) / glow.Width, w1 / glow.Height), glow.Size() / 2f);
                Add(glow, mid, A(IceTail, 0.36f * power), rot,
                    new Vector2((length + w1 * 0.7f) / glow.Width, w1 * 0.55f / glow.Height),
                    glow.Size() / 2f);
                Add(glow, mid, A(IceCore, 0.26f * power), rot,
                    new Vector2(length / glow.Width, w1 * 0.28f / glow.Height), glow.Size() / 2f);
            }

            if (flame == null)
                return;

            // 2) Языки вдоль клинка: основание на лезвии, тело — к острию.
            // Крупные, чтобы читались и на одном заряде. К концу чуть меньше,
            // чтобы грива у острия была ясно главной.
            int tongues = 3 + (int)Math.Round(3f * h);
            for (int i = 0; i < tongues; i++)
            {
                float s = (i + 1f) / (tongues + 1f);
                float seed = i * 2.399f;
                float wob = (float)Math.Sin(time * (2.4f + 0.32f * i) + seed);
                float wob2 = (float)Math.Sin(time * (3.8f + 0.45f * i) + seed * 1.7f);
                Vector2 pos = Vector2.Lerp(butt, tip, s)
                    + normal * (wob * (2.5f + 4f * h) * (0.35f + 0.65f * s))
                    + axis * (wob2 * (1.5f + 2.5f * h));
                float hgt = length * (0.55f + 0.35f * h) * (1.15f - 0.45f * s) * (0.92f + 0.08f * wob);
                float wid = (14f + 16f * h) * (0.75f + 0.35f * s);
                float r = flameRot + wob * 0.18f;
                // синий ореол шире → сам пиксельный язык (в нём уже ядро и кромка)
                Add(flame, pos, A(IceEdge, 0.45f * power), r,
                    new Vector2(wid * 1.45f / flame.Width, hgt * 1.05f / flame.Height), baseOrigin);
                Add(flame, pos, A(Color.White, 0.95f * power), r,
                    new Vector2(wid / flame.Width, hgt / flame.Height), baseOrigin);
            }

            // 3) Грива за остриём — та часть огня, что даёт длину.
            // Пять языков веером, основания у самого острия, центральный самый длинный.
            float plumeH = extra + 20f + 36f * h;
            float[] sideAngle = { -0.42f, -0.20f, 0f, 0.20f, 0.42f };
            float[] sideH = { 0.55f, 0.75f, 1f, 0.80f, 0.60f };
            float[] sideOff = { -0.9f, -0.4f, 0f, 0.4f, 0.9f };
            for (int k = 0; k < 5; k++)
            {
                float wob = (float)Math.Sin(time * (3.1f + 0.55f * k) + k * 2.1f);
                Vector2 dir = axis.RotatedBy(sideAngle[k] * 0.5f);
                Vector2 pos = tip
                    + axis * (2f * h)
                    + normal * (sideOff[k] * (7f + 7f * h) + wob * (1.5f + 2f * h));
                float hgt = plumeH * sideH[k] * (0.90f + 0.10f * wob);
                float wid = (15f + 16f * h) * sideH[k];
                float r = rot + MathHelper.PiOver2 + sideAngle[k] + wob * 0.14f;
                Add(flame, pos, A(IceEdge, 0.48f * power), r,
                    new Vector2(wid * 1.4f / flame.Width, hgt * 1.04f / flame.Height), baseOrigin);
                Add(flame, pos, A(Color.White, 0.95f * power), r,
                    new Vector2(wid / flame.Width, hgt / flame.Height), baseOrigin);
            }

            // 4) Искры: поднимаются от клинка вверх, гаснут
            int sparks = 4 + (int)Math.Round(4f * h);
            for (int i = 0; i < sparks; i++)
            {
                float seed = 3.17f + i * 2.77f;
                float life = (time * (0.55f + 0.1f * i) + seed) % 1f;
                Vector2 at = Vector2.Lerp(butt, tip, 0.30f + 0.70f * life);
                Vector2 pos = at
                    + normal * ((float)Math.Sin(seed * 5f) * (2f + 3f * h))
                    + new Vector2(0f, -life * (14f + 22f * h));
                float size = (7f + 6f * h) * (1f - life * 0.55f);
                Texture2D spark = SparkTex;
                if (spark != null)
                    Add(spark, pos, A(Color.White, 0.95f * (1f - life) * power), time * 0.7f,
                        new Vector2(size / spark.Width, size / spark.Height), spark.Size() / 2f);
            }
        }
    }
}
