using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    // Мгновенный blast взрыва холодного огня: живёт 3 тика, бьёт всех в
    // радиусе (ai[0]) ванильными коллизиями и поджигает каждого попавшего.
    // Сам невидим: вспышку и пыль рисует FrostFireSystem.Burst в момент взрыва.
    public class FrostFireBlast : ModProjectile
    {
        public override string Texture => "FrostMelee/Items/Frostbrand";

        public override void SetDefaults()
        {
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 3;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
        }

        public override void AI()
        {
            if (Projectile.localAI[0] == 0f)
            {
                Projectile.localAI[0] = 1f;
                int r = (int)Projectile.ai[0];
                // Размер меняется после спавна — центр надо вернуть на место,
                // иначе зона урона уезжала вправо-вниз на весь радиус
                Vector2 center = Projectile.Center;
                Projectile.width = r * 2;
                Projectile.height = r * 2;
                Projectile.Center = center;
            }
            Projectile.velocity = Vector2.Zero;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            Weapons.FrostFireSystem.TryIgnite(target, Main.player[Projectile.owner]);
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
