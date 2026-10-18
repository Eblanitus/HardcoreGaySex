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
            //    на светлом фоне — снег, небо, светлая земля: аддитив на белом не
            //    даёт ничего, а синяя полупрозрачная дымка подкрашивает то, что за ней.
            DrawGlow(glow, mid, dir.ToRotation(), bladeLen * 1.20f, 38f * breathe,
                IceArcFx.Veil(IceArcFx.ColdFireVeil, 0.14f * heat * breathe));
            DrawGlow(glow, mid, dir.ToRotation(), bladeLen * 1.04f, 28f * breathe,
                IceArcFx.Additive(IceArcFx.FireTail, 0.18f * heat * breathe));
            DrawGlow(glow, mid, dir.ToRotation(), bladeLen * 0.90f, 14f * breathe,
                IceArcFx.Additive(IceArcFx.FireEdge, 0.16f * heat * breathe));

            // 2. Языки по кромке здесь НЕ рисуются: этот слой лежит за спрайтом,
            //    и всё, что попадает в силуэт, съедается. Они живут в переднем слое
            //    (IceArcFx.DrawBladeFireLine) — там они видны целиком.

            // 3. Факел на острие: острие торчит из силуэта, поэтому здесь он читается.
            float pulse = 0.7f + 0.3f * (float)Math.Sin(time * 4.6f);
            Vector2 tipPos = tip + dir * 4f;
            DrawGlow(glow, tipPos, dir.ToRotation(), 38f * pulse, 24f * pulse,
                IceArcFx.Veil(IceArcFx.ColdFireVeil, 0.15f * heat * pulse));
            DrawGlow(glow, tipPos, dir.ToRotation(), 30f * pulse, 17f * pulse,
                IceArcFx.Additive(IceArcFx.FireEdge, 0.26f * heat * pulse));
            for (int i = -1; i <= 1; i++)
            {
                float flick = 0.6f + 0.4f * (float)Math.Sin(time * 5.1f + i * 2.3f);
                IceArcFx.DrawTongue(glow, tip + dir * 2f, dir.RotatedBy(i * 0.34f), i * 0.35f,
                    (26f + 10f * flick) * heat, 5.0f,
                    IceArcFx.Veil(IceArcFx.ColdFireVeil, 0.15f * heat * flick),
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
