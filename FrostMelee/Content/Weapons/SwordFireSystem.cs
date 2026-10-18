using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Ледяной огонь Frostbrand, слой «позади клинка».
    //
    // PostDrawTiles срабатывает ДО игрока и снарядов, поэтому всё нарисованное здесь
    // оказывается ЗА спрайтом меча: наружу выходит только то, что торчит за силуэт.
    // Из этого следует форма эффекта — не «перья» поперёк кромки (они на 2× зуме
    // превращались в начёс из волосков), а длинные ИЗОГНУТЫЕ сужающиеся языки,
    // которые выходят из-под клинка и уходят вперёд, к острию, плюс мягкий ореол
    // и факел на самом острие.
    //
    // Само лезвие «раскаляет» передний слой IceArcFx.DrawBladeFireLine: его зовут
    // держатели меча (SickleBraceProjectile — «встреча», SwordStabProjectile — тычок,
    // SwordArcProjectile — взмах). Передний слой даёт жилку и короткие языки по
    // кромке, задний — объём вокруг силуэта. Пыль не используется вовсе: ванильная
    // BlueTorch рассыпалась по клинку алмазной крошкой и читалась как мусор.
    //
    // Позу клинка отдаёт SwordPlayer.TryGetBladeLine: замах броска, тычок, «встреча»,
    // взмах, стойка. Меч брошен (в руке пусто) — огня нет вообще.
    public class SwordFireSystem : ModSystem
    {
        private const int TonguesPerSide = 4;   // языков на каждой стороне клинка
        private static readonly Color ColdVeil = new Color(40, 100, 225); // синяя дымка
        private const int Motes = 3;            // холодные искры вокруг клинка
        private const float HiltSkip = 0.16f;   // доля клинка от приклада, которая не горит

        public override void PostDrawTiles()
        {
            if (Main.dedServ || Main.gameMenu)
                return;
            Rectangle view = new Rectangle((int)Main.screenPosition.X - 200, (int)Main.screenPosition.Y - 200,
                Main.screenWidth + 400, Main.screenHeight + 400);
            foreach (Player player in Main.ActivePlayers)
            {
                if (player.dead || player.HeldItem.IsAir || player.HeldItem.type != ItemID.Frostbrand)
                    continue;
                if (!BladeLine(player, out Vector2 butt, out Vector2 tip))
                    continue;
                if (!view.Contains(butt.ToPoint()) && !view.Contains(tip.ToPoint()))
                    continue;
                DrawBladeFlame(player, butt, tip);
            }
        }

        // Линия лезвия: та же, что у огня в SwordPlayer — взмах, тычок, встреча,
        // замах броска или стойка. false — меч брошен (в руке пусто) или позы нет.
        private static bool BladeLine(Player player, out Vector2 butt, out Vector2 tip)
        {
            butt = tip = default;
            if (!SwordTypes.IsSword(player.HeldItem))
                return false;
            return player.GetModPlayer<SwordPlayer>().TryGetBladeLine(out butt, out tip);
        }

        private static void DrawBladeFlame(Player player, Vector2 butt, Vector2 tip)
        {
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow == null || glow.IsDisposed)
                return;
            float len = Vector2.Distance(butt, tip);
            if (len < 10f)
                return;

            Vector2 dir = (tip - butt) / len;
            Vector2 normal = new Vector2(-dir.Y, dir.X);
            Vector2 up = normal.Y > 0f ? -normal : normal;   // «вверх» по экрану: тепло поднимается
            Vector2 root = Vector2.Lerp(butt, tip, HiltSkip); // рукоять и гарда не горят
            float bladeLen = Vector2.Distance(root, tip);

            bool busy = player.itemAnimation > 0;
            float time = Main.GlobalTimeWrappedHourly;
            float heat = busy ? 1f : 0.85f;                  // в бою пламя злее
            float breathe = 0.86f + 0.14f * (float)Math.Sin(time * 2.9f);
            Vector2 mid = (root + tip) * 0.5f;

            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

            // 1. Дымка + ореол. Вуаль (AlphaBlend) нужна, чтобы пламя было видно
            //    на светлом фоне — снег, небо, светлая земля: аддитив на белом не даёт
            //    ничего, а синяя полупрозрачная дымка подкрашивает то, что за ней.
            DrawGlow(glow, mid, dir.ToRotation(), bladeLen * 1.15f, 34f * breathe,
                IceArcFx.Veil(ColdVeil, 0.13f * heat * breathe));
            DrawGlow(glow, mid, dir.ToRotation(), bladeLen * 1.02f, 26f * breathe,
                IceArcFx.Additive(IceArcFx.FireTail, 0.16f * heat * breathe));
            DrawGlow(glow, mid, dir.ToRotation(), bladeLen * 0.88f, 13f * breathe,
                IceArcFx.Additive(IceArcFx.FireEdge, 0.15f * heat * breathe));

            // 2. Языки вдоль клинка: 4 с каждой стороны. Длинные, изогнутые,
            //    сужающиеся — в отличие от прежних тонких прямых «волосков».
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < TonguesPerSide; i++)
                {
                    float u = 0.25f + 0.22f * i;             // 0.25 .. 0.91
                    float ph = i * 2.3f + side * 1.9f;
                    float flick = 0.55f + 0.45f * (float)Math.Sin(time * 3.6f + ph);
                    float length = (20f + 16f * u) * (0.65f + 0.45f * flick) * heat;
                    float width = 4.2f + 2.2f * (1f - u);
                    Vector2 at = Vector2.Lerp(root, tip, u) + normal * side * 7f;
                    Vector2 d = (dir * 0.60f + normal * side * 0.65f + up * 0.25f).SafeNormalize(dir);
                    IceArcFx.DrawTongue(glow, at, d, side * 0.45f, length, width,
                        IceArcFx.Veil(ColdVeil, 0.16f * heat * flick),
                        IceArcFx.Additive(IceArcFx.FireEdge, 0.34f * heat * flick),
                        IceArcFx.Additive(IceArcFx.FireLineCore, 0.40f * heat * flick));
                }
            }

            // 2b. Один язык сдувает назад у гарды — пламя не бывает односторонним
            {
                float flick = 0.5f + 0.5f * (float)Math.Sin(time * 3.1f + 4.4f);
                Vector2 at = Vector2.Lerp(root, tip, 0.24f) - normal * 2f;
                Vector2 d = (up * 0.5f - dir * 0.8f).SafeNormalize(-dir);
                IceArcFx.DrawTongue(glow, at, d, -0.3f, 18f * flick * heat, 3.8f,
                    IceArcFx.Veil(ColdVeil, 0.12f * heat * flick),
                    IceArcFx.Additive(IceArcFx.FireEdge, 0.26f * heat * flick),
                    IceArcFx.Additive(IceArcFx.FireLineCore, 0.30f * heat * flick));
            }

            // 3. Факел на острие — главный акцент: дымка + пятно + три языка вперёд
            float pulse = 0.7f + 0.3f * (float)Math.Sin(time * 4.6f);
            Vector2 tipPos = tip + dir * 4f;
            DrawGlow(glow, tipPos, dir.ToRotation(), 34f * pulse, 22f * pulse,
                IceArcFx.Veil(ColdVeil, 0.14f * heat * pulse));
            DrawGlow(glow, tipPos, dir.ToRotation(), 26f * pulse, 15f * pulse,
                IceArcFx.Additive(IceArcFx.FireEdge, 0.24f * heat * pulse));
            for (int i = -1; i <= 1; i++)
            {
                float flick = 0.6f + 0.4f * (float)Math.Sin(time * 5.1f + i * 2.3f);
                Vector2 d = dir.RotatedBy(i * 0.30f);
                IceArcFx.DrawTongue(glow, tip + dir * 2f, d, i * 0.35f, (24f + 8f * flick) * heat, 4.8f,
                    IceArcFx.Veil(ColdVeil, 0.14f * heat * flick),
                    IceArcFx.Additive(IceArcFx.FireEdge, 0.32f * heat * flick),
                    IceArcFx.Additive(IceArcFx.FireLineCore, 0.38f * heat * flick));
            }

            // 4. Холодные искры: мягкие точки, медленно кружат у клинка
            for (int i = 0; i < Motes; i++)
            {
                float u = (i + 0.5f) / Motes;
                float ph = i * 2.1f;
                Vector2 at = Vector2.Lerp(root, tip, u)
                    + normal * (float)Math.Sin(time * 1.9f + ph) * (7f + i * 2f)
                    + up * (float)Math.Cos(time * 1.3f + ph) * 4f;
                float size = 2.4f + 1.6f * (float)Math.Sin(time * 3.3f + ph);
                if (size < 0.6f)
                    continue;
                DrawGlow(glow, at, 0f, size * 2f, size * 2f,
                    IceArcFx.Additive(IceArcFx.FireCore, 0.28f * heat));
            }

            Main.spriteBatch.End();
        }

        // Растянутое аддитивное пятно: length/width — мировые размеры эллипса
        private static void DrawGlow(Texture2D glow, Vector2 center, float rotation, float length, float width, Color color)
        {
            if (length < 0.5f || width < 0.5f)
                return;
            Main.spriteBatch.Draw(glow, center - Main.screenPosition, null, color, rotation, glow.Size() / 2f,
                new Vector2(length / glow.Width, width / glow.Height), SpriteEffects.None, 0f);
        }
    }
}
