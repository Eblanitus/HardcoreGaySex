using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace FrostMelee.Content
{
    // Ледяной огонь на Фростборне. Меч не обрастает льдом и не растягивается —
    // по клинку гуляет синее пламя: языки лижут лезвие, за остриём поднимается
    // косматая грива, вверх улетают искры. Чем больше зарядов, тем больше и ярче
    // пламя. Огонь выходит за остриё на IceTipPerCharge за заряд — это и есть
    // прибавка длины: по ней же бьют хитбоксы.
    public static class FrostFire
    {
        private static readonly Color IceCore = new Color(235, 250, 255);
        private static readonly Color IceTail = new Color(120, 210, 255);
        private static readonly Color IceEdge = new Color(40, 110, 230);

        // Сколько пикселей пламени выходит за остриё (0 — зарядов нет)
        public static float TipExtra(Player p, int itemType)
        {
            if (p == null || !p.active || itemType != ItemID.Frostbrand)
                return 0f;
            return p.GetModPlayer<FrostKindlePlayer>().ReachCharges * FrostKindle.IceTipPerCharge;
        }

        // butt — приклад спрайта, tip — остриё, heat — 0..1 (сколько зарядов из максимума)
        public static void Draw(Vector2 butt, Vector2 tip, float heat)
        {
            if (Main.dedServ || heat <= 0.01f)
                return;
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow == null || glow.IsDisposed)
                return;

            float h = MathHelper.Clamp(heat, 0f, 1f);
            Vector2 axis = (tip - butt).SafeNormalize(Vector2.UnitX);
            float length = Vector2.Distance(butt, tip);
            if (length < 8f)
                return;
            Vector2 normal = new Vector2(-axis.Y, axis.X);
            float time = Main.GlobalTimeWrappedHourly;
            float rot = axis.ToRotation();
            float extra = FrostKindle.MaxReachCharges * FrostKindle.IceTipPerCharge * h;
            float power = 0.45f + 0.55f * h;

            // Языки вдоль клинка: к острию крупнее и злее
            int tongues = 3 + (int)Math.Round(3f * h);
            for (int i = 0; i < tongues; i++)
            {
                float s = (i + 1f) / (tongues + 0.6f);
                float seed = i * 2.399f;
                float wob = (float)Math.Sin(time * (2.2f + 0.35f * i) + seed);
                float wob2 = (float)Math.Sin(time * (3.6f + 0.5f * i) + seed * 1.7f);
                Vector2 at = Vector2.Lerp(butt, tip, s);
                Vector2 pos = at
                    + normal * (wob * (2.5f + 4.5f * h) * (0.35f + 0.65f * s))
                    + axis * (wob2 * (1.5f + 3f * h) * s)
                    + new Vector2(0f, -(0.5f + 0.5f * wob2) * (1.5f + 4f * h) * s);
                float size = (6f + 12f * h) * (0.5f + 0.5f * s) * (0.88f + 0.12f * wob);
                Flame(glow, pos, size, rot, power);
            }

            // Грива за остриём — та часть огня, что даёт длину
            int plume = 2 + (int)Math.Round(3f * h);
            for (int i = 0; i < plume; i++)
            {
                float s = (i + 0.5f) / plume;
                float seed = 7.31f + i * 1.913f;
                float wob = (float)Math.Sin(time * (2.7f + 0.4f * i) + seed);
                float wob2 = (float)Math.Sin(time * (4.1f + 0.3f * i) + seed * 1.3f);
                Vector2 pos = tip
                    + axis * (extra * s)
                    + normal * (wob * (1.5f + 2.5f * h) * s)
                    + new Vector2(0f, -1f) * ((0.5f + 0.5f * wob2) * extra * 0.35f * s);
                float size = (7f + 11f * h) * (1f - 0.45f * s);
                Flame(glow, pos, size, rot, power);
            }

            // Искры: поднимаются от клинка вверх и гаснут
            int sparks = 1 + (int)Math.Round(3f * h);
            for (int i = 0; i < sparks; i++)
            {
                float seed = 3.17f + i * 2.77f;
                float life = (time * (0.55f + 0.1f * i) + seed) % 1f;
                Vector2 at = Vector2.Lerp(butt, tip, 0.35f + 0.65f * life);
                Vector2 pos = at
                    + normal * ((float)Math.Sin(seed * 5f) * (2f + 2f * h))
                    + new Vector2(0f, -life * (10f + 16f * h));
                float size = (3.5f + 3f * h) * (1f - life * 0.7f);
                Blob(glow, pos, size, size, 0f, Additive(IceCore, 0.55f * (1f - life) * power));
            }
        }

        // Язык пламени: синий ореол, голубое тело, белое ядро
        private static void Flame(Texture2D glow, Vector2 pos, float size, float rot, float power)
        {
            Blob(glow, pos, size * 1.45f, size * 1.15f, rot, Additive(IceEdge, 0.22f * power));
            Blob(glow, pos, size, size * 0.92f, rot, Additive(IceTail, 0.30f * power));
            Blob(glow, pos, size * 0.5f, size * 0.55f, rot, Additive(IceCore, 0.26f * power));
        }

        private static void Blob(Texture2D glow, Vector2 pos, float w, float h, float rot, Color color)
        {
            if (w < 0.5f || h < 0.5f)
                return;
            Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, color, rot,
                glow.Size() / 2f, new Vector2(w / glow.Width, h / glow.Height), SpriteEffects.None, 0f);
        }

        private static Color Additive(Color c, float alpha) => c * MathHelper.Clamp(alpha, 0f, 1f);
    }
}
