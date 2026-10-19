using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace FrostMelee.Content.Weapons
{
    // Визуал приёмов Ice Blade. Логика обратна копью: копьё в дуге СБРАСЫВАЕТ накопленный
    // иней, а клинок ТОЧИТСЯ об воздух — с него счищается собственная наледь, и чем он
    // острее, тем чище и ярче всё, что он оставляет.
    // Дуга: здесь — вариант шершавого клинка; по мере заточки SwordArcProjectile
    // перетекает в дугу косы (DrawKeenArc). Стиль шершавой дуги:
    //   тело дуги — кадр 0 текстуры Excalibur, но полупрозрачным свечением, а не заливкой;
    //   правка — тонкие белые дуги (кадр 3), как у ванили и косы;
    //   блеск — звёздочка из «слезы» (Extra 98) на голове дуги и на клинке;
    //   частицы — редкая ванильная пыль (льдинки, искры самоцвета).
    // Полировка P (0 — в инее, 1 — отточен) читается на глаз:
    //   P≈0 — свечение матовое бледное, белая дуга одна и тусклая, изредка льдинки;
    //   P≈1 — свечение голубое, две белые дуги, за головой пара звёздочек, редкие искры.
    // Только визуал: урон, хитбоксы и полировку не трогает.
    public static class IceBladeFx
    {
        // --- Палитра ---
        public static readonly Color FrostMatte = new Color(170, 195, 215);   // наледь
        public static readonly Color EdgeCore = new Color(245, 252, 255);     // белый блик
        public static readonly Color EdgeRim = new Color(120, 200, 255);      // голубой ореол

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

        // =====================================================================
        // Частицы — ванильная пыль
        // =====================================================================

        // Льдинка наледи: падает, как обломки льда
        public static void SpawnShaving(Vector2 pos, Vector2 vel, float scale)
        {
            if (Main.dedServ)
                return;
            Dust d = Dust.NewDustPerfect(pos, DustID.Ice, vel, 0, default, scale);
            d.noGravity = false;
        }

        // Искра правки: белый или голубой блеск самоцвета, гаснет на лету
        public static void SpawnSpark(Vector2 pos, Vector2 vel, float scale)
        {
            if (Main.dedServ)
                return;
            int type = Main.rand.NextBool(3) ? DustID.GemSapphire : DustID.GemDiamond;
            Dust d = Dust.NewDustPerfect(pos, type, vel, 0, default, scale * 0.7f);
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
        // Звёздочка — как ванильная DrawPrettyStarSparkle (слеза Extra 98 крестом)
        // =====================================================================
        // length — длина длинного луча, cross — короткого (px), rotation — куда смотрит длинный.
        // Ширина лучей — доля длины, как у ванили; ядро — белое, ореол — shine.
        public static void Star(Vector2 world, float length, float cross, float rotation, Color shine, float alpha)
        {
            if (alpha <= 0.02f || length <= 1f)
                return;
            Texture2D tex = TextureAssets.Extra[98].Value;
            if (tex == null || tex.IsDisposed)
                return;
            Vector2 pos = world - Main.screenPosition;
            Vector2 origin = tex.Size() / 2f;
            float fat = 0.38f;
            Vector2 longScale = new Vector2(length * fat / tex.Width, length / tex.Height);
            Vector2 crossScale = new Vector2(cross * fat / tex.Width, cross / tex.Height);
            Color halo = Additive(shine, alpha);
            Color core = Additive(Color.White, alpha * 0.8f);
            // текстура — вертикальная слеза: +π/2 кладёт её вдоль rotation
            float rLong = rotation + MathHelper.PiOver2;
            Main.EntitySpriteDraw(tex, pos, null, halo, rLong, origin, longScale, SpriteEffects.None, 0f);
            Main.EntitySpriteDraw(tex, pos, null, halo, rotation, origin, crossScale, SpriteEffects.None, 0f);
            Main.EntitySpriteDraw(tex, pos, null, core, rLong, origin, longScale * 0.6f, SpriteEffects.None, 0f);
            Main.EntitySpriteDraw(tex, pos, null, core, rotation, origin, crossScale * 0.6f, SpriteEffects.None, 0f);
        }

        // =====================================================================
        // Дуга взмаха по рецепту ванильного Excalibur, в ледяных тонах
        // =====================================================================
        // tex — текстура Excalibur (4 кадра), center — центр снаряда (мир), rotation/fx/scale —
        // как у ванильного снаряда, dir — сторона взмаха (ai[0] ≥ 0 ? 1 : -1),
        // progress — доля взмаха, fade — ванильная огибающая (0→1 к 60 %, дальше гаснет).
        public static void DrawSwingArc(Texture2D tex, Vector2 center, float rotation, float dir, float scale,
            SpriteEffects fx, float progress, float fade, float polish)
        {
            if (fade <= 0.01f)
                return;
            Vector2 pos = center - Main.screenPosition;
            Rectangle body = tex.Frame(1, 4);
            Rectangle line = tex.Frame(1, 4, 0, 3);
            Vector2 origin = body.Size() / 2f;
            Color shine = Color.Lerp(FrostMatte, EdgeRim, polish);

            // тело: не плотная ванильная заливка, а полупрозрачное свечение формы взмаха
            // (как ореол у косы); слегка отстаёт от клинка — видно движение
            Main.EntitySpriteDraw(tex, pos, body, Additive(shine, fade * (0.16f + 0.12f * polish)),
                rotation - dir * MathHelper.PiOver4 * 0.5f * (1f - progress), origin, scale, fx, 0f);
            Main.EntitySpriteDraw(tex, pos, body, Additive(Color.Lerp(shine, EdgeCore, 0.4f), fade * (0.08f + 0.1f * polish)),
                rotation, origin, scale * 0.975f, fx, 0f);

            // правка: белые дуги. Шершавый — одна бледная матовая, отточенный — вторая внутри
            Color lineCol = Color.Lerp(FrostMatte, Color.White, polish);
            Main.EntitySpriteDraw(tex, pos, line, lineCol * (fade * (0.28f + 0.17f * polish)),
                rotation + dir * 0.01f, origin, scale, fx, 0f);
            float second = Smooth01((polish - 0.4f) / 0.4f);
            if (second > 0.01f)
                Main.EntitySpriteDraw(tex, pos, line, Color.White * (fade * 0.22f * second),
                    rotation - dir * 0.05f, origin, scale * 0.85f, fx, 0f);

            // голова дуги — звёздочка там, где клинок режет воздух; на отточенном за ней
            // ещё одна-две гаснущие
            float sweep = Utils.Remap(progress, 0f, 1f, 0f, MathHelper.PiOver4) * dir;
            float radius = (tex.Width * 0.5f - 5f) * scale;
            int trailing = (int)Math.Round(2f * polish);
            for (int i = 0; i <= trailing; i++) {
                float ang = rotation + sweep - dir * i * 0.22f;
                float k = 1f - i / (float)(trailing + 1);
                Star(center + ang.ToRotationVector2() * radius,
                    Utils.Remap(progress, 0f, 1f, 18f, 7f) * scale * k, 8f * scale * k, ang + MathHelper.PiOver2,
                    shine, fade * k * (0.4f + 0.45f * polish));
            }
        }

        // =====================================================================
        // Правка на самом клинке
        // =====================================================================
        // Брусок — блик-звезда, бегущая по клинку (дуга: от пяты к острию, тычок: наоборот).
        // Длинный луч — поперёк клинка (брусок), короткий — вдоль. В конце правки — «дзинь».
        // Координата t вдоль клинка: 0 — пята (где кончается гарда), 1 — остриё.

        public const float HeelFrac = 0.3f;        // где на оси приклад→остриё начинается клинок
        public const float BladeHalfWidth = 4f;    // полуширина клинка Ice Blade поперёк оси, px
        private const float TipTaper = 0.25f;      // последняя доля клинка, где кромки сходятся в остриё

        public static Vector2 HeelOf(Vector2 butt, Vector2 tip) => Vector2.Lerp(butt, tip, HeelFrac);

        // Точка на кромке side (±1) — для частиц, срывающихся с кромок
        public static Vector2 EdgePoint(Vector2 heel, Vector2 tip, float halfWidth, float t, int side)
        {
            Vector2 d = tip - heel;
            Vector2 n = new Vector2(-d.Y, d.X).SafeNormalize(Vector2.UnitY);
            float w = halfWidth * MathHelper.Clamp((1f - t) / TipTaper, 0f, 1f);
            return heel + d * t + n * w * side;
        }

        public static void DrawStone(Vector2 heel, Vector2 tip, float t, float polish, float alpha)
        {
            if (alpha <= 0.02f)
                return;
            Vector2 at = Vector2.Lerp(heel, tip, t);
            float along = (tip - heel).ToRotation();
            Star(at, 14f + 6f * polish, 7f + 4f * polish, along + MathHelper.PiOver2,
                Color.Lerp(FrostMatte, EdgeRim, polish), alpha);
        }

        // «Дзинь» на острие: крупная звезда, длинный луч вдоль клинка
        public static void DrawTing(Vector2 tip, Vector2 bladeDir, float polish, float alpha)
        {
            Star(tip, 24f + 12f * polish, 12f + 6f * polish, bladeDir.ToRotation(), EdgeRim, alpha);
        }

        // =====================================================================
        // Тычок: клинок вспарывает воздух
        // =====================================================================
        // Рецепт ванильных колющих (Piercing Starlight / Daybreak): широкая «слеза» Extra 98
        // слоями вдоль выпада — тёмное тело, светлая середина, белое ядро; у острия — клин
        // рассечённого воздуха. Поверх — пиксельные линии скорости: сплошные бруски толщиной
        // в пиксель спрайта Terraria (2 px), бегут назад вдоль кромок.
        // Шершавый: матовое тело, бледные бруски, одна дорожка; отточенный: голубое сияние,
        // белые бруски, две дорожки.
        private const float ArtPixel = 2f;   // один пиксель рисунка Terraria в мировых px

        private static Vector2 Snap(Vector2 v) =>
            new Vector2((float)Math.Round(v.X / ArtPixel) * ArtPixel, (float)Math.Round(v.Y / ArtPixel) * ArtPixel);

        // Сплошной брусок: от a длиной len вдоль dir, толщиной thick (мировые px)
        private static void Bar(Vector2 a, Vector2 dir, float len, float thick, Color c)
        {
            Texture2D px = TextureAssets.MagicPixel.Value;
            if (px == null || px.IsDisposed)
                return;
            Main.EntitySpriteDraw(px, Snap(a) - Main.screenPosition, new Rectangle(0, 0, 1, 1), c, dir.ToRotation(),
                new Vector2(0f, 0.5f), new Vector2(Math.Max(ArtPixel, len), thick), SpriteEffects.None, 0f);
        }

        // Слеза Extra 98 вдоль rotation: длина len, ширина width (мировые px)
        private static void Tear(Texture2D tex, Vector2 at, float rotation, float len, float width, Color c)
        {
            Main.EntitySpriteDraw(tex, at - Main.screenPosition, null, c, rotation + MathHelper.PiOver2, tex.Size() / 2f,
                new Vector2(width / tex.Width, len / tex.Height), SpriteEffects.None, 0f);
        }

        public static void DrawThrustCut(Vector2 tip, Vector2 dir, float bladeLen, float halfWidth,
            float flow, float polish, float alpha)
        {
            if (alpha <= 0.02f)
                return;
            Texture2D tex = TextureAssets.Extra[98].Value;
            if (tex == null || tex.IsDisposed)
                return;
            Vector2 n = dir.RotatedBy(MathHelper.PiOver2);
            float rot = dir.ToRotation();
            Color shine = Color.Lerp(FrostMatte, EdgeRim, polish);
            Color deep = Color.Lerp(new Color(90, 115, 140), new Color(40, 110, 230), polish);

            // разрез: слоистая слеза от острия назад вдоль клинка и дальше
            float len = bladeLen * 1.25f + 34f;
            Vector2 mid = tip - dir * (len * 0.5f - 6f);
            float width = 18f + 6f * polish;
            Color body = polish < 0.5f ? deep * (alpha * 0.55f) : Additive(deep, alpha * 0.8f);
            Tear(tex, mid, rot, len, width, body);
            Tear(tex, mid + dir * 4f, rot, len * 0.85f, width * 0.6f, Additive(shine, alpha * (0.35f + 0.4f * polish)));
            Tear(tex, mid + dir * 8f, rot, len * 0.7f, width * 0.25f,
                Additive(Color.Lerp(FrostMatte, EdgeCore, polish), alpha * (0.3f + 0.6f * polish)));

            // клин у острия: воздух расходится на две стороны
            for (int side = -1; side <= 1; side += 2) {
                Vector2 wedge = dir.RotatedBy(side * 0.5f);
                Tear(tex, tip - wedge * 10f + n * side * 2f, wedge.ToRotation(), 22f + 8f * polish, 8f,
                    Additive(shine, alpha * (0.45f + 0.4f * polish)));
            }

            // пиксельные линии скорости вдоль кромок: бегут назад и тают к хвосту
            const float Period = 20f;
            float tailLen = bladeLen + 30f;
            int lanes = polish > 0.5f ? 2 : 1;
            Color barCol = Color.Lerp(FrostMatte, EdgeCore, polish);
            for (int lane = 0; lane < lanes; lane++) {
                float gap = halfWidth + 8f + lane * 8f;
                for (int side = -1; side <= 1; side += 2) {
                    float phase = (flow + lane * 7f + (side > 0 ? 0f : Period * 0.5f)) % Period;
                    for (float x = phase; x < tailLen; x += Period) {
                        float k = x / tailLen;
                        float a = alpha * (1f - k) * Smooth01(x / 8f) * (lane == 0 ? 1f : 0.65f);
                        if (a <= 0.03f)
                            continue;
                        float widen = Math.Min(1f, x / (bladeLen * TipTaper));
                        Vector2 at = tip - dir * x + n * side * (gap * (0.5f + 0.5f * widen) + Math.Max(0f, x - bladeLen) * 0.25f);
                        float barLen = (10f + 8f * polish) * (1f - 0.4f * k);
                        Color c = polish < 0.5f ? barCol * (a * 0.7f) : Additive(barCol, a * 0.9f);
                        // брусок тянется назад от точки, толщина — 2 пикселя рисунка у первой дорожки
                        Bar(at, -dir, barLen, lane == 0 ? ArtPixel * 2f : ArtPixel, c);
                    }
                }
            }
        }

        public static void Unload()
        {
        }
    }
}
