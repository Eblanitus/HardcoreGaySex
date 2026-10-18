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
    // по клинку гуляет синее пламя: спрайтовые языки лижут лезвие, за остриём
    // поднимается большая грива, вверх улетают искры. Чем больше зарядов, тем
    // крупнее и ярче огонь. Огонь выходит за остриё на IceTipPerCharge за заряд —
    // это и есть прибавка длины: по ней же бьют хитбоксы.
    public static class FrostFire
    {
        private static readonly Color IceCore = new Color(245, 252, 255);
        private static readonly Color IceTail = new Color(130, 215, 255);
        private static readonly Color IceEdge = new Color(45, 120, 240);

        // Спрайт огня (Textures/Effects/FrostFlame.png, 96×192, языки вверх)
        private static Texture2D flameTex;
        private static bool flameTried;
        private static Texture2D FlameTex
        {
            get
            {
                if (!flameTried && !Main.dedServ)
                {
                    flameTried = true;
                    try
                    {
                        flameTex = ModContent.Request<Texture2D>("FrostMelee/Textures/Effects/FrostFlame").Value;
                    }
                    catch { flameTex = null; }
                }
                return (flameTex == null || flameTex.IsDisposed) ? null : flameTex;
            }
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

        private static void Add(Texture2D tex, Vector2 pos, Color color, float rotation, Vector2 scale)
        {
            if (tex == null || scale.X * tex.Width < 1.5f || scale.Y * tex.Height < 1.5f)
                return;
            batch.Add(new DrawData(tex, pos - Main.screenPosition, null, color, rotation,
                tex.Size() / 2f, scale, SpriteEffects.None, 0f));
        }

        private static void Build(Vector2 butt, Vector2 tip, float heat)
        {
            batch.Clear();
            if (Main.dedServ || heat <= 0.01f)
                return;
            Texture2D flame = FlameTex;
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if ((flame == null && glow == null))
                return;

            float h = MathHelper.Clamp(heat, 0f, 1f);
            Vector2 axis = (tip - butt).SafeNormalize(Vector2.UnitX);
            float length = Vector2.Distance(butt, tip);
            if (length < 8f)
                return;
            Vector2 normal = new Vector2(-axis.Y, axis.X);
            float time = Main.GlobalTimeWrappedHourly;
            float rot = axis.ToRotation();
            // Пламя растёт вдоль оси клинка: спрайт смотрит вверх, поворот +90°
            float flameRot = rot + MathHelper.PiOver2;
            float extra = FrostKindle.MaxReachCharges * FrostKindle.IceTipPerCharge * h;
            // Пульс — огонь живой, яркость дышит
            float pulse = 0.92f + 0.16f * (float)Math.Sin(time * 5.3);
            float power = (0.55f + 0.45f * h) * pulse;

            // 1) Свечение вдоль всего лезвия — сразу видно с любой стороны
            if (glow != null)
            {
                Vector2 mid = (butt + tip) * 0.5f;
                float w1 = 9f + 13f * h;
                Add(glow, mid, A(IceEdge, 0.34f * power), rot,
                    new Vector2((length + w1) / glow.Width, w1 / glow.Height));
                Add(glow, mid, A(IceTail, 0.30f * power), rot,
                    new Vector2((length + w1 * 0.7f) / glow.Width, w1 * 0.55f / glow.Height));
                Add(glow, mid, A(IceCore, 0.22f * power), rot,
                    new Vector2(length / glow.Width, w1 * 0.30f / glow.Height));
            }

            if (flame == null)
                return;

            // 2) Языки вдоль клинка — от рукояти к острию, к концу крупнее
            int tongues = 4 + (int)Math.Round(4f * h);
            for (int i = 0; i < tongues; i++)
            {
                float s = (i + 1f) / (tongues + 0.6f);
                float seed = i * 2.399f;
                float wob = (float)Math.Sin(time * (2.4f + 0.32f * i) + seed);
                float wob2 = (float)Math.Sin(time * (3.8f + 0.45f * i) + seed * 1.7f);
                Vector2 pos = Vector2.Lerp(butt, tip, s)
                    + normal * (wob * (2f + 4f * h) * (0.35f + 0.65f * s))
                    + axis * (wob2 * (1f + 2f * h) * s);
                float hgt = length * (0.34f + 0.30f * h) * (0.65f + 0.50f * s) * (0.92f + 0.08f * wob);
                float wid = (11f + 13f * h) * (0.55f + 0.45f * s);
                float r = flameRot + wob * 0.20f;
                // подложка (тёмно-синий ореол шире) + сам язык + белое ядро
                Add(flame, pos, A(IceEdge, 0.42f * power), r,
                    new Vector2(wid * 1.55f / flame.Width, hgt * 1.12f / flame.Height));
                Add(flame, pos, A(IceCore, 0.90f * power), r,
                    new Vector2(wid / flame.Width, hgt / flame.Height));
                Add(flame, Vector2.Lerp(pos, tip, 0.15f), A(Color.White, 0.55f * power), r,
                    new Vector2(wid * 0.55f / flame.Width, hgt * 0.70f / flame.Height));
            }

            // 3) Грива за остриём — та часть огня, что даёт длину.
            // Три языка веером (центральный самый высокий), вылетающие по оси клинка.
            float plumeH = extra + 16f + 30f * h;
            float[] sideAngle = { -0.34f, 0f, 0.34f };
            float[] sideH = { 0.62f, 1f, 0.72f };
            float[] sideOff = { -0.28f, 0f, 0.30f };
            for (int k = 0; k < 3; k++)
            {
                float wob = (float)Math.Sin(time * (3.1f + 0.6f * k) + k * 2.1f);
                Vector2 dir = axis.RotatedBy(sideAngle[k]);
                Vector2 pos = tip
                    + axis * (extra * 0.40f + 4f * h)
                    + dir * (sideOff[k] * (10f + 10f * h))
                    + normal * (wob * (1.5f + 2.5f * h));
                float hgt = plumeH * sideH[k] * (0.90f + 0.10f * wob);
                float wid = (13f + 15f * h) * sideH[k];
                float r = flameRot + sideAngle[k] * 0.6f + wob * 0.16f;
                Add(flame, pos, A(IceEdge, 0.45f * power), r,
                    new Vector2(wid * 1.5f / flame.Width, hgt * 1.10f / flame.Height));
                Add(flame, pos, A(IceCore, 0.95f * power), r,
                    new Vector2(wid / flame.Width, hgt / flame.Height));
                Add(flame, Vector2.Lerp(pos, pos + axis * hgt * 0.4f, 0.5f), A(Color.White, 0.55f * power), r,
                    new Vector2(wid * 0.5f / flame.Width, hgt * 0.55f / flame.Height));
            }

            // 4) Искры: поднимаются от клинка вверх, гаснут
            int sparks = 3 + (int)Math.Round(4f * h);
            for (int i = 0; i < sparks; i++)
            {
                float seed = 3.17f + i * 2.77f;
                float life = (time * (0.55f + 0.1f * i) + seed) % 1f;
                Vector2 at = Vector2.Lerp(butt, tip, 0.30f + 0.70f * life);
                Vector2 pos = at
                    + normal * ((float)Math.Sin(seed * 5f) * (2f + 3f * h))
                    + new Vector2(0f, -life * (12f + 20f * h));
                float size = (4.5f + 4f * h) * (1f - life * 0.6f);
                if (glow != null)
                    Add(glow, pos, A(i % 2 == 0 ? IceCore : IceTail, 0.75f * (1f - life) * power), 0f,
                        new Vector2(size / glow.Width, size / glow.Height));
            }
        }
    }
}
