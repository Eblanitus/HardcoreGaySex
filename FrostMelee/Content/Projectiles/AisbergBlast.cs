using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    // Всплеск ледяной энергии расколовшейся глыбы Айсберга.
    // Невидимый мгновенный снаряд: урон по радиусу (ai[0]) и Frostbite.
    // Вид взрыва — только ванильная пыль и куски спрайта глыбы (ShatterFx):
    // гладкие кольца и лучи владелец назвал «векторной графикой».
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

        // Раскол айсберга — опасный взрыв, а не хрупкая игрушка: та же вспышка
        // ледяного огня, что у взрыва Фростбранда (FrostFireSystem.Burst:
        // языки пламени долетают до края зоны урона, хлопок, свет), низкий
        // треск льда, и глыба разлетается на горящие куски своего спрайта
        // (AisbergShard): они тянут за собой ледяной огонь и, упав, оставляют
        // на земле лёд. Сразу видно, докуда бьёт и что поджигает.
        private static void ShatterFx(Vector2 center, int radius, int frame, float scale)
        {
            bool large = scale > 1.2f;
            int f = System.Math.Abs(frame) % 3;
            int top = f * 32;

            Weapons.FrostFireSystem.Burst(center, radius, large);
            Terraria.Audio.SoundEngine.PlaySound(SoundID.Item14 with { Volume = large ? 0.9f : 0.65f,
                Pitch = large ? -0.1f : 0.2f }, center);
            Terraria.Audio.SoundEngine.PlaySound(SoundID.Item27 with { Volume = large ? 1f : 0.7f,
                Pitch = large ? -0.7f : -0.45f }, center);
            // Ледяной огонь веером до края зоны урона — крупная ванильная пыль
            // Frostbite, не зависит от настройки ядрёности огня: взрыв должен
            // читаться всегда
            int flames = large ? 22 : 14;
            for (int i = 0; i < flames; i++)
            {
                Vector2 dir = (MathHelper.TwoPi * i / flames + Main.rand.NextFloat(-0.2f, 0.2f)).ToRotationVector2();
                Dust d = Dust.NewDustPerfect(center + dir * 6f, DustID.IceTorch,
                    dir * radius * Main.rand.NextFloat(0.07f, 0.1f), 0, default, Main.rand.NextFloat(2.2f, large ? 3.2f : 2.7f));
                d.noGravity = true;
            }
            // Край зоны урона — кольцо ванильного инея: пыль рождается на
            // середине радиуса и разлетается до края, видно, докуда бьёт
            int ring = large ? 18 : 12;
            for (int i = 0; i < ring; i++)
            {
                Vector2 dir = (MathHelper.TwoPi * i / ring).ToRotationVector2();
                Dust d = Dust.NewDustPerfect(center + dir * radius * 0.5f, DustID.Frost,
                    dir * radius * 0.05f, 0, default, large ? 1.9f : 1.6f);
                d.noGravity = true;
            }
            // крупная ледяная крошка падает вниз
            int ice = large ? 10 : 6;
            for (int i = 0; i < ice; i++)
            {
                Dust d = Dust.NewDustPerfect(center, DustID.Ice,
                    Main.rand.NextVector2Circular(5f, 5f) + new Vector2(0f, -2f), 0, default,
                    Main.rand.NextFloat(1.3f, 1.8f));
                d.noGravity = false;
            }
            Lighting.AddLight(center, 0.6f, 1.1f, 1.4f);

            // 4 четверти кадра — горящие куски, разлетаются взрывом
            float force = 1f + 0.35f * (scale - 1f);
            for (int i = 0; i < 4; i++)
            {
                int qx = i % 2, qy = i / 2;
                Rectangle part = new Rectangle(qx * 16, top + qy * 16, 16, 16);
                Vector2 off = new Vector2(qx == 0 ? -1f : 1f, qy == 0 ? -1f : 1f);
                Vector2 vel = off * new Vector2(Main.rand.NextFloat(2.6f, 4.2f), Main.rand.NextFloat(1f, 2f))
                    + new Vector2(0f, -Main.rand.NextFloat(4f, 5.5f));
                Dusts.AisbergShard.Spawn(center + off * 4f * scale, vel * force, part, scale, true);
            }
            // пара мелких сколов без огня
            int bits = large ? 3 : 2;
            for (int i = 0; i < bits; i++)
            {
                Rectangle part = new Rectangle(8 + Main.rand.Next(3) * 4, top + 8 + Main.rand.Next(3) * 4, 8, 8);
                Vector2 vel = Main.rand.NextVector2CircularEdge(1f, 1f) * Main.rand.NextFloat(3.5f, 6f) * force
                    + new Vector2(0f, -3f);
                Dusts.AisbergShard.Spawn(center, vel, part, scale, false);
            }
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            Weapons.FrostFireSystem.Ignite(target);
        }

        public override bool PreDraw(ref Color lightColor) => false;
    }
}
