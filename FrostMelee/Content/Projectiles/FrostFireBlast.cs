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
                Projectile.width = r * 2;
                Projectile.height = r * 2;
            }
            Projectile.velocity = Vector2.Zero;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            Weapons.FrostFireSystem.Ignite(target, Projectile.owner);
        }

        public override bool PreDraw(ref Color lightColor) => false;
    }
}
