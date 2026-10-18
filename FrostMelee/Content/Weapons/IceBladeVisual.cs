using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Визуал ледяного клинка: наоборот копью — копьё замораживается, клинок размораживается.
    // RoughIceBlade.png — тупой в инее (1), Ice_Blade.png — отполированный (0).
    // Кроссфейд по шкале IceFrost игрока, как у FrozenSpear.
    public static class IceBladeVisual
    {
        private static Asset<Texture2D> roughTex;
        private static Asset<Texture2D> cleanTex;
        private static Asset<Texture2D> heldClean;
        private static bool roughShown;

        public static void RegisterClean(Asset<Texture2D> clean) => heldClean = clean;

        public static void UpdateView(float frost)
        {
            if (Main.dedServ) return;
            bool shouldRough = frost >= 0.55f || (roughShown && frost > 0.45f);
            if (shouldRough == roughShown || heldClean == null) return;
            if (shouldRough) {
                roughTex ??= ModContent.Request<Texture2D>("FrostMelee/Items/RoughIceBlade", AssetRequestMode.ImmediateLoad);
                TextureAssets.Item[ItemID.IceBlade] = roughTex;
            } else {
                TextureAssets.Item[ItemID.IceBlade] = heldClean;
            }
            roughShown = shouldRough;
        }

        public static void Unload()
        {
            roughTex = null;
            cleanTex = null;
            heldClean = null;
            roughShown = false;
        }

        private static float Smooth01(float x)
        {
            x = MathHelper.Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        // Доля инея 0..1 (1 = весь в инее). Только Ice Blade
        public static float Mix(Player player, int itemType)
        {
            if (itemType != ItemID.IceBlade || player == null || !player.active)
                return 0f;
            // SwordPlayer держит шкалу
            return Smooth01(player.GetModPlayer<SwordPlayer>().IceFrostShown);
        }

        public static Texture2D GetRoughTexture()
        {
            if (Main.dedServ) return null;
            roughTex ??= ModContent.Request<Texture2D>("FrostMelee/Items/RoughIceBlade", AssetRequestMode.ImmediateLoad);
            var t = roughTex.Value;
            return t == null || t.IsDisposed ? null : t;
        }

        public static Texture2D GetCleanTexture()
        {
            if (Main.dedServ) return null;
            cleanTex ??= ModContent.Request<Texture2D>("FrostMelee/Items/Ice_Blade", AssetRequestMode.ImmediateLoad);
            var t = cleanTex.Value;
            return t == null || t.IsDisposed ? null : t;
        }

        // Рисует кроссфейд в мире (снаряды)
        public static void DrawBlend(Texture2D normalFallback, Vector2 drawPos, SickleSprite.Geometry geo, float rotation, bool flipped, Color lightColor, float scale, float mix)
        {
            if (Main.dedServ) return;
            // normalFallback уже подменён на Ice_Blade через FrostMelee.Load, но для кроссфейда берём оба явно
            Texture2D rough = GetRoughTexture();
            Texture2D clean = GetCleanTexture() ?? normalFallback;
            if (rough == null || clean == null) return;

            SpriteEffects fx = flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            // clean = 1-mix, rough = mix
            if (mix < 0.995f && clean != null && !clean.IsDisposed)
                Main.EntitySpriteDraw(clean, drawPos - Main.screenPosition, null, lightColor * (1f - mix), rotation, geo.ButtFor(flipped), scale, fx, 0f);
            if (mix > 0.005f)
                Main.EntitySpriteDraw(rough, drawPos - Main.screenPosition, null, lightColor * mix, rotation, geo.ButtFor(flipped), scale, fx, 0f);
        }

        public static void AddBlend(ref PlayerDrawSet drawInfo, Texture2D normalFallback, Vector2 drawPos, SickleSprite.Geometry geo, float rotation, bool flipped, Color lightColor, float scale, float mix)
        {
            Texture2D rough = GetRoughTexture();
            Texture2D clean = GetCleanTexture() ?? normalFallback;
            SpriteEffects fx = flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            if (mix < 0.995f && clean != null)
                drawInfo.DrawDataCache.Add(new DrawData(clean, drawPos - Main.screenPosition, null, lightColor * (1f - mix), rotation, geo.ButtFor(flipped), scale, fx, 0));
            if (mix > 0.005f && rough != null)
                drawInfo.DrawDataCache.Add(new DrawData(rough, drawPos - Main.screenPosition, null, lightColor * mix, rotation, geo.ButtFor(flipped), scale, fx, 0));
        }
    }
}
