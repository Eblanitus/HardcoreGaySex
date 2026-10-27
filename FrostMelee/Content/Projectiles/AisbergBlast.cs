using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    // Всплеск ледяной энергии расколовшейся глыбы Айсберга.
    // Невидимый мгновенный снаряд: урон по радиусу (ai[0]) и Frostbite.
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
            }
            Projectile.velocity = Vector2.Zero;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            Weapons.FrostFireSystem.Ignite(target);
        }

        // Взрыв круглый: бьёт только тех, кто реально в радиусе. Угол
        // квадратной коробки доставал на 41% дальше радиуса
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            float r = System.Math.Max(8f, Projectile.ai[0]);
            Vector2 closest = new Vector2(
                MathHelper.Clamp(Projectile.Center.X, targetHitbox.Left, targetHitbox.Right),
                MathHelper.Clamp(Projectile.Center.Y, targetHitbox.Top, targetHitbox.Bottom));
            return Vector2.DistanceSquared(closest, Projectile.Center) <= r * r;
        }

        public override bool PreDraw(ref Color lightColor) => false;
    }
}
