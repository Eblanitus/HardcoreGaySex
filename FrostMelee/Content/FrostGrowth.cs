using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace FrostMelee.Content
{
    // Наледь Frostbrand. Заряд не растягивает спрайт меча — он наращивает на клинке лёд:
    // иней ползёт по лезвию от острия к рукояти, по краям встают кристаллы, а за остриём
    // вырастает ледяное жало. Меч «разрастается» льдом, как пламя, но сам спрайт остаётся
    // своего размера. Жало — это и есть прибавка длины: по нему же бьют хитбоксы.
    public static class FrostGrowth
    {
        private static readonly Color IceCore = new Color(235, 250, 255);
        private static readonly Color IceTail = new Color(120, 210, 255);
        private static readonly Color IceEdge = new Color(40, 110, 230);

        // Сколько пикселей льда торчит за остриём (0 — зарядов нет)
        public static float TipExtra(Player p, int itemType)
        {
            if (p == null || !p.active || itemType != ItemID.Frostbrand)
                return 0f;
            return p.GetModPlayer<FrostKindlePlayer>().ReachCharges * FrostKindle.IceTipPerCharge;
        }

        // butt — приклад спрайта, tip — остриё спрайта (оба в мире).
        // growth01 — 0..1, сколько зарядов набрано.
        public static void Draw(Vector2 butt, Vector2 tip, float growth01)
        {
            if (Main.dedServ || growth01 <= 0.01f)
                return;
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow == null || glow.IsDisposed)
                return;

            float t = MathHelper.Clamp(growth01, 0f, 1f);
            Vector2 axis = (tip - butt).SafeNormalize(Vector2.UnitX);
            float length = Vector2.Distance(butt, tip);
            if (length < 8f)
                return;
            Vector2 normal = new Vector2(-axis.Y, axis.X);

            // Иней ползёт от острия назад: чем больше зарядов, тем длиннее обмёрзшая часть
            float crust = MathHelper.Lerp(0.20f, 0.80f, t);
            Vector2 crustStart = tip - axis * (length * crust);

            // Мягкое холодное свечение по обмёрзшему лезвию
            Segment(glow, crustStart, tip, 5f + 6f * t, Additive(IceTail, 0.14f + 0.16f * t));
            Segment(glow, Vector2.Lerp(crustStart, tip, 0.25f), tip, 2f + 3f * t,
                Additive(IceCore, 0.10f + 0.12f * t));

            // Кристаллы по краям лезвия: растут от острия к рукояти, по одному на заряд
            int shards = 2 + (int)(t * 8f);
            for (int i = 0; i < shards; i++)
            {
                float fade = MathHelper.Clamp(t * 10f - i, 0f, 1f);
                if (fade <= 0.02f)
                    continue;
                float along = 1f - crust * (i + 0.5f) / shards;
                Vector2 at = Vector2.Lerp(butt, tip, MathHelper.Clamp(along, 0f, 1f));
                int side = i % 2 == 0 ? 1 : -1;
                float len = (2.5f + 3.5f * t) * fade;
                Segment(glow, at, at + normal * (side * len), 1.6f + 1.2f * t,
                    Additive(IceCore, 0.45f * fade));
                Segment(glow, at, at + normal * (side * len * 0.6f), 1f,
                    Additive(IceTail, 0.30f * fade));
            }

            // Ледяное жало за остриём — та самая прибавка длины
            float extra = FrostKindle.MaxReachCharges * FrostKindle.IceTipPerCharge * t;
            Vector2 tipEnd = tip + axis * extra;
            Segment(glow, tip - axis * 2f, tipEnd, 5f + 3f * t, Additive(IceEdge, 0.35f + 0.15f * t));
            Segment(glow, tip - axis * 2f, tipEnd, 3f + 2f * t, Additive(IceTail, 0.45f + 0.15f * t));
            Segment(glow, tip, Vector2.Lerp(tip, tipEnd, 0.85f), 1.8f, Additive(IceCore, 0.55f + 0.2f * t));
            Sparkle(glow, tipEnd, 5f + 5f * t, Additive(IceCore, 0.45f + 0.2f * t));
        }

        private static void Segment(Texture2D glow, Vector2 a, Vector2 b, float width, Color color)
        {
            if (width < 0.5f)
                return;
            Vector2 delta = b - a;
            if (delta.LengthSquared() < 0.01f)
                return;
            Vector2 scale = new Vector2((delta.Length() + width) / glow.Width, width / glow.Height);
            Main.EntitySpriteDraw(glow, (a + b) * 0.5f - Main.screenPosition, null, color, delta.ToRotation(),
                glow.Size() / 2f, scale, SpriteEffects.None, 0f);
        }

        private static void Sparkle(Texture2D glow, Vector2 pos, float size, Color color)
        {
            Vector2 origin = glow.Size() / 2f;
            for (int i = 0; i < 2; i++)
                Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, color, MathHelper.PiOver4 + MathHelper.PiOver2 * i,
                    origin, new Vector2(size / glow.Width, size * 0.10f / glow.Height), SpriteEffects.None, 0f);
            Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, color, 0f, origin,
                size * 0.30f / glow.Width, SpriteEffects.None, 0f);
        }

        private static Color Additive(Color c, float alpha) => c * MathHelper.Clamp(alpha, 0f, 1f);
    }
}
