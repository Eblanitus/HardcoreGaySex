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

        // Цвета самого клинка, снятые со спрайта Ice_Blade.png: бирюзовый лёд
        public static readonly Color BladeTeal = new Color(3, 173, 192);      // тело клинка
        public static readonly Color BladeTealDeep = new Color(2, 95, 115);   // тень клинка
        public static readonly Color BladeCyan = new Color(54, 232, 252);     // блик клинка

        // Палитра дуг самого клинка: тень → тело → блик, всё в бирюзе клинка.
        // Всё, что меч оставляет в воздухе (шершавый сектор, лента, ванильная дуга, шлейф),
        // берёт цвета отсюда — читается как его собственный лёд, а не как дуга косы.
        public static void ArcColors(float polish, out Color core, out Color tail, out Color edge) {
            polish = MathHelper.Clamp(polish, 0f, 1f);
            core = Color.Lerp(BladeTeal, BladeCyan, 0.3f + 0.7f * polish);
            tail = Color.Lerp(BladeTealDeep, BladeTeal, 0.15f + 0.7f * polish);
            edge = Color.Lerp(BladeTealDeep, BladeCyan, 0.3f + 0.7f * polish);
        }

        // Линия дуги меча: светлая бирюза клинка (у ванили и косы линия белая)
        public static Color ArcLineColor(float polish) =>
            Color.Lerp(BladeTeal, Color.White, 0.3f + 0.4f * MathHelper.Clamp(polish, 0f, 1f));

        // Полировка владельца: 0 — весь в инее, 1 — отточен (та же кривая, что кроссфейд спрайта)
        // Полировка КОНКРЕТНОГО меча: у IceBlade и Frostbrand состояния независимы
        public static float PolishOf(Player player, int itemType) =>
            itemType == ItemID.IceBlade
                ? 1f - IceBladeVisual.Mix(player, ItemID.IceBlade)
                : 1f - player.GetModPlayer<SwordPlayer>().GetFrostShown(itemType);

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
            Color shine = Color.Lerp(BladeTealDeep, BladeTeal, polish);

            // тело: не плотная ванильная заливка, а полупрозрачное свечение формы взмаха
            // (как ореол у косы); слегка отстаёт от клинка — видно движение
            Main.EntitySpriteDraw(tex, pos, body, Additive(shine, fade * (0.16f + 0.12f * polish)),
                rotation - dir * MathHelper.PiOver4 * 0.5f * (1f - progress), origin, scale, fx, 0f);
            Main.EntitySpriteDraw(tex, pos, body, Additive(Color.Lerp(shine, BladeCyan, 0.4f), fade * (0.08f + 0.1f * polish)),
                rotation, origin, scale * 0.975f, fx, 0f);

            // правка: светлые бирюзовые дуги. Шершавый — одна тусклая, отточенный — вторая внутри
            Color lineCol = ArcLineColor(polish);
            Main.EntitySpriteDraw(tex, pos, line, lineCol * (fade * (0.28f + 0.17f * polish)),
                rotation + dir * 0.01f, origin, scale, fx, 0f);
            float second = Smooth01((polish - 0.4f) / 0.4f);
            if (second > 0.01f)
                Main.EntitySpriteDraw(tex, pos, line, Color.Lerp(lineCol, BladeCyan, 0.5f) * (fade * 0.22f * second),
                    rotation - dir * 0.05f, origin, scale * 0.85f, fx, 0f);
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

        public static void Unload()
        {
        }
    }
}
