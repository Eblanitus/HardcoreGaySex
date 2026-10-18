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
    // оказывается ЗА спрайтом меча. Раньше это считалось недостатком, но на деле это
    // и есть нужный эффект: мягкая подсветка выходит только за силуэт — ореол вокруг
    // кромки, шипы пламени, торчащие наружу, и факел на острие. Всё, что нарисовано
    // «внутри» лезвия, просто съедается спрайтом, поэтому его здесь нет.
    //
    // Само лезвие «раскаляет» передний слой IceArcFx.DrawBladeFireLine: его зовут
    // держатели меча (SickleBraceProjectile — «встреча», SwordStabProjectile — тычок,
    // SwordArcProjectile — взмах). Так передний и задний слои не спорят, а дополняют
    // друг друга: ядро видно на клинке, ореол и шипы — вокруг него.
    //
    // Позу клинка отдаёт SwordPlayer.TryGetBladeLine: замах броска, тычок, «встреча»,
    // взмах, стойка. Меч брошен (в руке пусто) — огня нет вообще.
    public class SwordFireSystem : ModSystem
    {
        private const int SpikesPerSide = 7;    // шипов-языков с каждой стороны кромки
        private const int Motes = 5;            // холодные искры вокруг клинка
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

            // 1. Ореол: широкий холодный + узкий яркий. Один слой давал «туман» —
            //    два дают объём: тёмная кромка, за ней сиреневое свечение, у самого
            //    спрайта почти белое.
            DrawGlow(glow, mid, dir.ToRotation(), bladeLen * 1.02f, (busy ? 26f : 20f) * breathe,
                IceArcFx.Additive(IceArcFx.FireEdge, 0.26f * heat * breathe));
            DrawGlow(glow, mid, dir.ToRotation(), bladeLen * 0.90f, (busy ? 15f : 12f) * breathe,
                IceArcFx.Additive(IceArcFx.FireTail, 0.18f * heat * breathe));

            // 2. Шипы-языки по обе стороны кромки. Раньше вместо них были три круглых
            //    блика — они и читались как кляксы. Шипы торчат из-под клинка наружу,
            //    отогнуты назад и вверх, у острия длиннее.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < SpikesPerSide; i++)
                {
                    float u = (i + 0.5f) / SpikesPerSide;
                    float t = 0.14f + 0.84f * u;
                    float ph = i * 2.7f + side * 1.9f;
                    float flick = 0.5f + 0.5f * (float)Math.Sin(time * 4.4f + ph);
                    flick *= flick;   // резче гаснет
                    if (flick < 0.12f)
                        continue;

                    float spikeLen = (6f + 16f * u) * (0.55f + 0.75f * flick) * heat;
                    Vector2 at = Vector2.Lerp(root, tip, t) + normal * side * 2.5f;
                    Vector2 outward = (normal * side * 0.9f + up * 0.35f - dir * 0.55f).SafeNormalize(normal * side);
                    Vector2 apex = at + outward * spikeLen;

                    DrawSegment(glow, at, apex, 3.0f, 0.6f,
                        IceArcFx.Additive(IceArcFx.FireEdge, 0.34f * heat * flick));
                    DrawSegment(glow, Vector2.Lerp(at, apex, 0.25f), Vector2.Lerp(at, apex, 0.9f),
                        1.6f, 0.4f, IceArcFx.Additive(IceArcFx.FireLineCore, 0.42f * heat * flick));
                }
            }

            // 3. Звёздочки по кромке — «искры инея», вспыхивают вразнобой
            for (int i = 0; i < 4; i++)
            {
                float t = 0.2f + 0.24f * i;
                float ph = i * 2.1f;
                float tw = 0.45f + 0.55f * (float)Math.Sin(time * 5.3f + ph);
                if (tw < 0.3f)
                    continue;
                Vector2 at = Vector2.Lerp(root, tip, t) + normal * (i % 2 == 0 ? 4f : -4f);
                IceArcFx.DrawSparkle(glow, at, (7f + 4f * tw) * heat, dir.ToRotation() + MathHelper.PiOver4,
                    2, IceArcFx.Additive(IceArcFx.FireLineRim, 0.30f * heat * tw),
                    IceArcFx.Additive(IceArcFx.FireCore, 0.45f * heat * tw));
            }

            // 4. Факел на острие: пульсирующее пятно + трёхлучевая звезда, чуть впереди клинка
            float pulse = 0.65f + 0.35f * (float)Math.Sin(time * 5.3f);
            Vector2 tipPos = tip + dir * 2f;
            DrawGlow(glow, tipPos, dir.ToRotation(), 34f * pulse, 20f * pulse,
                IceArcFx.Additive(IceArcFx.FireEdge, 0.30f * heat * pulse));
            IceArcFx.DrawSparkle(glow, tipPos, 26f * pulse, dir.ToRotation(), 3,
                IceArcFx.Additive(IceArcFx.FireLineRim, 0.32f * heat * pulse),
                IceArcFx.Additive(IceArcFx.FireCore, 0.55f * heat * pulse));

            // 5. Мотыльки-искры: медленно кружат у клинка, гаснут и вспыхивают
            for (int i = 0; i < Motes; i++)
            {
                float u = (i + 0.5f) / Motes;
                float ph = i * 2.1f;
                Vector2 at = Vector2.Lerp(root, tip, u)
                    + normal * (float)Math.Sin(time * 1.9f + ph) * (5f + i)
                    + up * (float)Math.Cos(time * 1.3f + ph) * 3f;
                float size = 2.0f + 1.6f * (float)Math.Sin(time * 3.3f + ph);
                if (size < 0.5f)
                    continue;
                DrawGlow(glow, at, 0f, size * 2f, size * 2f,
                    IceArcFx.Additive(IceArcFx.FireCore, 0.30f * heat));
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

        // Мазок вдоль отрезка a→b с сужением: аддитивное пятно, растянутое по отрезку
        private static void DrawSegment(Texture2D glow, Vector2 a, Vector2 b, float widthA, float widthB, Color color)
        {
            float width = (widthA + widthB) * 0.5f;
            if (width < 0.4f)
                return;
            Vector2 delta = b - a;
            Main.spriteBatch.Draw(glow, (a + b) * 0.5f - Main.screenPosition, null, color, delta.ToRotation(),
                glow.Size() / 2f, new Vector2((delta.Length() + width) / glow.Width, width / glow.Height),
                SpriteEffects.None, 0f);
        }
    }
}
