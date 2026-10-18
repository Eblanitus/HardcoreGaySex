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
        private const int Tongues = 5;          // языков вдоль клинка (в основном к острию)
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

            // 1. Ореол: два мягких слоя. Спереди клинок остаётся чистым — весь объём
            //    копится за силуэтом и выходит по краям.
            DrawGlow(glow, mid, dir.ToRotation(), bladeLen * 1.06f, 21f * breathe,
                IceArcFx.Additive(IceArcFx.FireTail, 0.14f * heat * breathe));
            DrawGlow(glow, mid, dir.ToRotation(), bladeLen * 0.90f, 11f * breathe,
                IceArcFx.Additive(IceArcFx.FireEdge, 0.13f * heat * breathe));

            // 2. Языки вдоль клинка: длинные, изогнутые, сужающиеся. У острия их
            //    больше и они длиннее — глаз читает «пламя сдувает вперёд».
            for (int i = 0; i < Tongues; i++)
            {
                float u = 0.44f + 0.13f * i;             // 0.44 .. 0.96
                int side = i % 2 == 0 ? 1 : -1;
                float ph = i * 1.9f;
                float flick = 0.55f + 0.45f * (float)Math.Sin(time * 3.6f + ph);
                float length = (18f + 18f * u) * (0.6f + 0.5f * flick) * heat;
                float width = 3.6f + 2.0f * (1f - u);
                Vector2 at = Vector2.Lerp(root, tip, u) + normal * side * 6f;
                Vector2 d = (dir * 0.55f + normal * side * 0.70f + up * 0.30f).SafeNormalize(dir);
                IceArcFx.DrawTongue(glow, at, d, side * 0.5f, length, width,
                    IceArcFx.Additive(IceArcFx.FireEdge, 0.30f * heat * flick),
                    IceArcFx.Additive(IceArcFx.FireLineCore, 0.34f * heat * flick));
            }

            // 2b. Один язык сдувает назад у гарды — пламя не бывает односторонним
            {
                float flick = 0.5f + 0.5f * (float)Math.Sin(time * 3.1f + 4.4f);
                Vector2 at = Vector2.Lerp(root, tip, 0.24f) - normal * 2f;
                Vector2 d = (up * 0.5f - dir * 0.8f).SafeNormalize(-dir);
                IceArcFx.DrawTongue(glow, at, d, -0.3f, 14f * flick * heat, 3.4f,
                    IceArcFx.Additive(IceArcFx.FireEdge, 0.24f * heat * flick),
                    IceArcFx.Additive(IceArcFx.FireLineCore, 0.28f * heat * flick));
            }

            // 3. Факел на острие — главный акцент: мягкое пятно + три языка вперёд
            float pulse = 0.7f + 0.3f * (float)Math.Sin(time * 4.6f);
            Vector2 tipPos = tip + dir * 3f;
            DrawGlow(glow, tipPos, dir.ToRotation(), 26f * pulse, 15f * pulse,
                IceArcFx.Additive(IceArcFx.FireEdge, 0.22f * heat * pulse));
            for (int i = -1; i <= 1; i++)
            {
                float flick = 0.6f + 0.4f * (float)Math.Sin(time * 5.1f + i * 2.3f);
                Vector2 d = dir.RotatedBy(i * 0.30f);
                IceArcFx.DrawTongue(glow, tip + dir * 1.5f, d, i * 0.35f, (18f + 6f * flick) * heat, 4.4f,
                    IceArcFx.Additive(IceArcFx.FireEdge, 0.30f * heat * flick),
                    IceArcFx.Additive(IceArcFx.FireLineCore, 0.36f * heat * flick));
            }

            // 4. Холодные искры: мягкие точки, медленно кружат у клинка
            for (int i = 0; i < Motes; i++)
            {
                float u = (i + 0.5f) / Motes;
                float ph = i * 2.1f;
                Vector2 at = Vector2.Lerp(root, tip, u)
                    + normal * (float)Math.Sin(time * 1.9f + ph) * (5f + i)
                    + up * (float)Math.Cos(time * 1.3f + ph) * 3f;
                float size = 2.2f + 1.4f * (float)Math.Sin(time * 3.3f + ph);
                if (size < 0.6f)
                    continue;
                DrawGlow(glow, at, 0f, size * 2f, size * 2f,
                    IceArcFx.Additive(IceArcFx.FireCore, 0.26f * heat));
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
