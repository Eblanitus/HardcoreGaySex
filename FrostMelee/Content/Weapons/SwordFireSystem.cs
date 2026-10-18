using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Постоянный огонь на клинке Frostbrand: рисуем ПОСЛЕ ТАЙЛОВ, тем же
    // приёмом, что FrostZoneSystem и SickleArc (свой Begin/End) — гарантированно
    // видимый слой поверх мира, без данных внутри отрисовки игрока или дуги.
    // Позу клинка отдаёт SwordPlayer.TryGetBladeLine — она честно следует за
    // взмахом, тычком, «встречей» и замахом броска, а если меч брошен (в руке
    // пусто) — огня нет вообще.
    public class SwordFireSystem : ModSystem
    {
        public override void PostDrawTiles()
        {
            if (Main.dedServ || Main.gameMenu)
                return;
            Rectangle view = new Rectangle((int)Main.screenPosition.X - 160, (int)Main.screenPosition.Y - 160,
                Main.screenWidth + 320, Main.screenHeight + 320);
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
            Vector2 origin = glow.Size() / 2f;
            Vector2 bladeDir = (tip - butt).SafeNormalize(Vector2.UnitX);
            float bladeLen = (tip - butt).Length();
            bool busy = player.itemAnimation > 0;
            float time = (float)Main.GlobalTimeWrappedHourly * 2.2f;

            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

            // Пламя вдоль всего клинка: вытянутый язычный овал + вспышки
            float breathe = 0.8f + 0.2f * (float)Math.Sin(time * 3.1f);
            Vector2 mid = Vector2.Lerp(butt, tip, 0.55f) - Main.screenPosition;
            float length = bladeLen * 1.5f;
            float width = (busy ? 30f : 22f) * breathe;
            Main.spriteBatch.Draw(glow, mid, null, Fire(IceArcFx.FireEdge, 0.5f),
                bladeDir.ToRotation(), origin, new Vector2(length / glow.Width, width / glow.Height),
                SpriteEffects.None, 0f);
            Main.spriteBatch.Draw(glow, mid, null, Fire(IceArcFx.FireCore, 0.8f),
                bladeDir.ToRotation(), origin, new Vector2(length * 0.7f / glow.Width, width * 0.5f / glow.Height),
                SpriteEffects.None, 0f);

            // Языки: три вспышки на лезвии, гуще у острия, с покачиванием поперёк
            for (int i = 0; i < 3; i++)
            {
                float t = 0.35f + 0.28f * i;
                float size = (busy ? 34f : 26f) + 18f * t;
                Vector2 at = Vector2.Lerp(butt, tip, t)
                    + bladeDir.RotatedBy(MathHelper.PiOver2) * 5f * (float)Math.Sin(time * 1.7f + i * 2.3f)
                    - Main.screenPosition;
                Main.spriteBatch.Draw(glow, at, null, Fire(IceArcFx.FireEdge, 0.55f),
                    bladeDir.ToRotation(), origin, new Vector2(size * 1.6f, size * 0.7f) / glow.Width,
                    SpriteEffects.None, 0f);
                Main.spriteBatch.Draw(glow, at, null, Fire(IceArcFx.FireCore, 0.9f),
                    0f, origin, new Vector2(size * 0.7f, size * 0.55f) / glow.Width,
                    SpriteEffects.None, 0f);
            }

            // Вспышка на острие
            Vector2 tipPos = tip - Main.screenPosition;
            Main.spriteBatch.Draw(glow, tipPos, null, Fire(IceArcFx.FireEdge, 0.6f),
                0f, origin, 40f / glow.Width, SpriteEffects.None, 0f);
            Main.spriteBatch.Draw(glow, tipPos, null, Fire(IceArcFx.FireCore, 0.9f),
                0f, origin, 22f / glow.Width, SpriteEffects.None, 0f);

            Main.spriteBatch.End();
            Vector2 lightAt = Vector2.Lerp(butt, tip, 0.6f);
            Lighting.AddLight(lightAt, 0.1f, 0.5f, 0.65f);
        }

        // Цвет огня с яркой альфой: в AlphaBlend-пакете рисуется полупрозрачным свечением
        private static Color Fire(Color baseColor, float alpha)
        {
            Color c = baseColor;
            c.A = (byte)(Math.Clamp(alpha, 0f, 1f) * 255f);
            return c;
        }
    }
}

