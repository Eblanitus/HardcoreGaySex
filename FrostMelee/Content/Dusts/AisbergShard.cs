using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace FrostMelee.Content.Dusts
{
    // Осколок расколотой глыбы Айсберга: кусок САМОГО спрайта глыбы
    // (dust.frame — прямоугольник внутри кадра AisbergChunk), летит,
    // крутится, падает и один раз отскакивает от земли. Пиксели те же, что
    // у глыбы, поэтому раскол читается как настоящий: глыба развалилась на
    // части, а не превратилась в облако частиц. Только визуал, урона нет.
    //
    // Горящий кусок (burning) тянет за собой ледяной огонь (LooseBurnDust)
    // и при первом касании земли замораживает её (лёд на земле — от
    // осколков раскола, IceSheetSystem через AisbergChunk.FreezeSurface).
    //
    // dust.customData — float[] { вращение за тик, отскоков осталось,
    // горит (1/0), уже заморозил землю (1/0) }.
    public class AisbergShard : ModDust
    {
        public override string Texture => "FrostMelee/Content/Projectiles/AisbergChunk";

        private const float Gravity = 0.28f;
        private const int LifeTicks = 50;
        private const int FadeTicks = 14;

        public override void OnSpawn(Dust dust)
        {
            dust.noGravity = true;
            dust.noLight = true;
            dust.alpha = 0;
        }

        // Спавн куска: frame — прямоугольник в листе AisbergChunk
        public static void Spawn(Vector2 at, Vector2 velocity, Rectangle frame, float scale, bool burning)
        {
            if (Main.dedServ)
                return;
            int id = Dust.NewDust(at, 0, 0, ModContent.DustType<AisbergShard>());
            Dust d = Main.dust[id];
            d.position = at;
            d.velocity = velocity;
            d.frame = frame;
            d.scale = scale;
            d.rotation = Main.rand.NextFloat(MathHelper.TwoPi);
            d.fadeIn = 0f;
            d.customData = new float[] { Main.rand.NextFloat(-0.25f, 0.25f), 1f, burning ? 1f : 0f, 0f };
        }

        public override bool Update(Dust dust)
        {
            float[] data = dust.customData as float[];
            dust.fadeIn++;   // возраст в тиках
            dust.velocity.Y = Math.Min(dust.velocity.Y + Gravity, 12f);
            dust.velocity.X *= 0.985f;

            // Земля: один отскок, потом осколок ложится и тает
            Vector2 size = new Vector2(dust.frame.Width, dust.frame.Height) * dust.scale * 0.5f;
            Vector2 next = Collision.TileCollision(dust.position - size * 0.5f, dust.velocity,
                (int)size.X, (int)size.Y, true, true);
            if (next.Y != dust.velocity.Y && dust.velocity.Y > 0f)
            {
                // Первое касание земли горящим куском — лёд на поверхности
                if (data != null && data[2] > 0f && data[3] == 0f)
                {
                    data[3] = 1f;
                    Projectiles.AisbergChunk.FreezeSurface(dust.position, dust.scale > 1.2f ? 2 : 1);
                }
                if (data != null && data[1] > 0f)
                {
                    data[1]--;
                    next.Y = -dust.velocity.Y * 0.35f;
                    next.X *= 0.6f;
                }
                else
                {
                    next.Y = 0f;
                    next.X *= 0.5f;
                }
                if (data != null)
                    data[0] *= 0.4f;
            }
            if (next.X != dust.velocity.X)
                next.X = -dust.velocity.X * 0.3f;
            dust.velocity = next;
            dust.position += dust.velocity;
            if (data != null)
                dust.rotation += data[0];

            // Горящий кусок: хвост ледяного огня, пока летит
            if (data != null && data[2] > 0f && (int)dust.fadeIn % 3 == 0 && dust.fadeIn < LifeTicks - FadeTicks)
                Projectiles.FrostJetFx.LooseBurnDust(dust.position, -dust.velocity * 0.15f + new Vector2(0f, -0.6f),
                    dust.dustIndex, 1.3f + 0.4f * dust.scale);

            int left = LifeTicks - (int)dust.fadeIn;
            if (left < FadeTicks)
                dust.alpha = (int)(255f * (1f - left / (float)FadeTicks));
            if (left <= 0)
                dust.active = false;
            return false;
        }

        public override bool PreDraw(Dust dust)
        {
            Texture2D tex = Texture2D.Value;
            Color light = Lighting.GetColor((int)(dust.position.X / 16f), (int)(dust.position.Y / 16f));
            // Подсветка льдом: в тени осколок не пропадает совсем
            light = Color.Lerp(light, new Color(150, 210, 230), 0.35f);
            float a = 1f - dust.alpha / 255f;
            Main.spriteBatch.Draw(tex, dust.position - Main.screenPosition, dust.frame, light * a,
                dust.rotation, new Vector2(dust.frame.Width, dust.frame.Height) * 0.5f, dust.scale,
                SpriteEffects.None, 0f);
            return false;
        }
    }
}
