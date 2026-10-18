using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content
{
    // «Замороженное» ледяное копьё: Items/IceSpearFrozen.png (106x106) — тот же спрайт копья, но цельный ледяной штырь.
    // Ось древка совпадает с IceSpearHeld.png (100x106), холст шире на 6 px вправо. Рисуется с тем же якорем (приклад),
    // поворотом и зеркалом, что и обычное копьё. Вид по шкале заморозки игрока (SicklePlayer.Frost): кроссфейд
    // обычный спрайт → замороженный; Mix() даёт долю, одинаково для всех приёмов.
    public static class FrozenSpear
    {
        // Шкала с которой в хитбоксе работает замороженная зона (до неё — обычная)
        public const float ZoneFrostAt = 0.5f;

        private static Asset<Texture2D> texture;

        public static void Unload()
        {
            texture = null;
        }

        private static float Smooth01(float x)
        {
            x = MathHelper.Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        // Доля замороженного вида 0..1 (0 — обычный спрайт). Только ледяной серп
        public static float Mix(Player player, int itemType)
        {
            if (itemType != ItemID.IceSickle || player == null || !player.active)
                return 0f;
            return Smooth01(player.GetModPlayer<SicklePlayer>().FrostShown);
        }

        // Бьёт замороженная зона (шкала >= ZoneFrostAt)
        public static bool UseZone(Player player, int itemType)
        {
            if (itemType != ItemID.IceSickle || player == null || !player.active)
                return false;
            return player.GetModPlayer<SicklePlayer>().Frost >= ZoneFrostAt;
        }

        // Текстура замороженного копья: по ней же строится зона урона (SickleSprite.GetZone с frozen). null — не клиент
        public static Texture2D GetTexture()
        {
            if (Main.dedServ)
                return null;
            texture ??= ModContent.Request<Texture2D>("FrostMelee/Items/IceSpearFrozen", AssetRequestMode.ImmediateLoad);
            Texture2D tex = texture.Value;
            return tex == null || tex.IsDisposed ? null : tex;
        }

        // Параметры отрисовки замороженного спрайта в кадре обычного: якорь — приклад (зеркальный кадр шире на 6 px)
        private static bool Params(SickleSprite.Geometry geo, bool flipped, out Texture2D tex, out Vector2 origin, out SpriteEffects fx)
        {
            tex = GetTexture();
            origin = Vector2.Zero;
            fx = flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            if (tex == null)
                return false;
            // Зеркальный кадр: x -> ширина_текстуры - x (origin при FlipHorizontally не зеркалится)
            origin = flipped ? new Vector2(tex.Width - geo.Butt.X, geo.Butt.Y) : geo.Butt;
            return true;
        }

        // drawPos — мировая позиция приклада; alpha 0..1
        public static void Draw(Vector2 drawPos, SickleSprite.Geometry geo, float rotation, bool flipped,
            Color lightColor, float scale, float alpha)
        {
            if (Main.dedServ || alpha <= 0.005f)
                return;
            if (!Params(geo, flipped, out Texture2D tex, out Vector2 origin, out SpriteEffects fx))
                return;
            Main.EntitySpriteDraw(tex, drawPos - Main.screenPosition, null, lightColor * alpha, rotation, origin, scale, fx, 0f);
        }

        // Кроссфейд: обычный спрайт (1 - mix) и замороженный (mix)
        public static void DrawBlend(Texture2D normal, Vector2 drawPos, SickleSprite.Geometry geo, float rotation,
            bool flipped, Color lightColor, float scale, float mix)
        {
            if (Main.dedServ)
                return;
            SpriteEffects fx = flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            if (mix < 0.995f && normal != null && !normal.IsDisposed)
                Main.EntitySpriteDraw(normal, drawPos - Main.screenPosition, null, lightColor * (1f - mix), rotation,
                    geo.ButtFor(flipped), scale, fx, 0f);
            Draw(drawPos, geo, rotation, flipped, lightColor, scale, mix);
        }

        // То же для слоёв игрока (PlayerDrawLayer)
        public static void AddBlend(ref PlayerDrawSet drawInfo, Texture2D normal, Vector2 drawPos, SickleSprite.Geometry geo,
            float rotation, bool flipped, Color lightColor, float scale, float mix)
        {
            SpriteEffects fx = flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            if (mix < 0.995f)
                drawInfo.DrawDataCache.Add(new DrawData(normal, drawPos - Main.screenPosition, null, lightColor * (1f - mix),
                    rotation, geo.ButtFor(flipped), scale, fx, 0));
            if (mix > 0.005f && Params(geo, flipped, out Texture2D tex, out Vector2 origin, out _))
                drawInfo.DrawDataCache.Add(new DrawData(tex, drawPos - Main.screenPosition, null, lightColor * mix,
                    rotation, origin, scale, fx, 0));
        }
    }
}
