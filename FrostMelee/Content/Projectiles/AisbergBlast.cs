using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    // Всплеск ледяной энергии расколовшейся глыбы Айсберга.
    // Невидимый мгновенный снаряд: урон по радиусу (ai[0]) и Frostbite.
    // ai[1] — кадр глыбы, ai[2] — её масштаб: по ним рисуется раскол (ShatterFx).
    // Поджог сырой (FrostFireSystem.Ignite), без запаса языков Фростбранда:
    // у Айсберга запаса нет, от Фростбранда взят только сам взрыв.
    public class AisbergBlast : ModProjectile
    {
        public override string Texture => "FrostMelee/Items/Frostbrand";

        public override void SetDefaults()
        {
            Projectile.width = 16;
            Projectile.height = 16;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 3;
            // Несколько глыб рядом не складывают взрывы по одной цели пачкой
            Projectile.usesIDStaticNPCImmunity = true;
            Projectile.idStaticNPCHitCooldown = 20;
            Projectile.aiStyle = -1;
        }

        public override void AI()
        {
            if (Projectile.localAI[0] == 0f)
            {
                Projectile.localAI[0] = 1f;
                int r = System.Math.Max(8, (int)Projectile.ai[0]);
                Vector2 center = Projectile.Center;
                Projectile.width = r * 2;
                Projectile.height = r * 2;
                Projectile.Center = center;
                // Вид раскола — на всех клиентах: снаряд взрыва синкается,
                // а флаг раскола у самой глыбы знает только владелец
                if (!Main.dedServ)
                    ShatterFx(center, r, (int)Projectile.ai[1], Projectile.ai[2] > 0f ? Projectile.ai[2] : 1f);
            }
            Projectile.velocity = Vector2.Zero;
        }

        // Раскол айсберга: глыба разваливается на куски собственного спрайта
        // (AisbergShard), из трещины бьёт ледяной огонь по краю радиуса
        // взрыва, крупная ванильная крошка, хлопок света. Частиц мало и
        // крупно (ванильный стиль), главное — настоящие куски глыбы.
        private static void ShatterFx(Vector2 center, int radius, int frame, float scale)
        {
            bool large = scale > 1.2f;
            int f = System.Math.Abs(frame) % 3;
            int top = f * 32;

            // 4 четверти кадра — крупные куски, разлетаются от центра и вверх
            for (int i = 0; i < 4; i++)
            {
                int qx = i % 2, qy = i / 2;
                Rectangle part = new Rectangle(qx * 16, top + qy * 16, 16, 16);
                Vector2 off = new Vector2(qx == 0 ? -1f : 1f, qy == 0 ? -1f : 1f);
                Vector2 vel = off * new Vector2(Main.rand.NextFloat(1.8f, 3.2f), Main.rand.NextFloat(0.8f, 1.6f))
                    + new Vector2(0f, -Main.rand.NextFloat(3f, 4.5f));
                Dusts.AisbergShard.Spawn(center + off * 4f * scale, vel * (large ? 1.15f : 1f), part, scale);
            }
            // мелкие сколы 8x8 из середины кадра
            int bits = large ? 6 : 4;
            for (int i = 0; i < bits; i++)
            {
                Rectangle part = new Rectangle(8 + Main.rand.Next(3) * 4, top + 8 + Main.rand.Next(3) * 4, 8, 8);
                Vector2 vel = Main.rand.NextVector2CircularEdge(1f, 1f) * Main.rand.NextFloat(3f, 5.5f)
                    + new Vector2(0f, -2.5f);
                Dusts.AisbergShard.Spawn(center, vel, part, scale);
            }

            // Край взрыва: кольцо ледяного огня (Frostbite) — показывает, докуда задело
            int ring = large ? 12 : 8;
            for (int i = 0; i < ring; i++)
            {
                Vector2 dir = (MathHelper.TwoPi * i / ring + Main.rand.NextFloat(-0.15f, 0.15f)).ToRotationVector2();
                Dust d = Dust.NewDustPerfect(center + dir * radius * 0.35f, DustID.IceTorch,
                    dir * radius * 0.06f, 0, default, large ? 2.4f : 1.9f);
                d.noGravity = true;
            }
            // крупная ванильная ледяная крошка
            int ice = large ? 8 : 5;
            for (int i = 0; i < ice; i++)
            {
                Dust d = Dust.NewDustPerfect(center, DustID.Ice,
                    Main.rand.NextVector2Circular(4.5f, 4.5f) + new Vector2(0f, -1.5f), 0, default,
                    Main.rand.NextFloat(1.2f, 1.6f));
                d.noGravity = false;
            }

            Lighting.AddLight(center, 0.45f, 0.85f, 1.1f);
            Terraria.Audio.SoundEngine.PlaySound(SoundID.Shatter with { Volume = large ? 0.8f : 0.55f,
                Pitch = large ? -0.35f : 0f }, center);
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            Weapons.FrostFireSystem.Ignite(target);
        }

        public override bool PreDraw(ref Color lightColor) => false;
    }
}
