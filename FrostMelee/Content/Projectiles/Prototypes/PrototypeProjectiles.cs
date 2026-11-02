using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles.Prototypes
{
    public abstract class PrototypeYoyoProjectile : ModProjectile
    {
        protected abstract string SpriteName { get; }
        protected abstract int Size { get; }
        protected virtual float Lifetime => 6f;
        protected virtual float Range => 160f;
        protected virtual float Speed => 10f;
        public override string Texture => $"FrostMelee/Content/Items/Prototypes/{SpriteName}";

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.YoyosLifeTimeMultiplier[Type] = Lifetime;
            ProjectileID.Sets.YoyosMaximumRange[Type] = Range;
            ProjectileID.Sets.YoyosTopSpeed[Type] = Speed;
        }

        public override void SetDefaults()
        {
            Projectile.CloneDefaults(ProjectileID.Amarok);
            // aiStyle 99 already supplies vanilla movement. Do not set AIType:
            // swapping to Amarok's type would hide this type's yoyo range/time sets.
            Projectile.width = Projectile.height = Size;
            Projectile.DamageType = DamageClass.Melee;
        }
    }

    public class FlinxYoyoProjectile : PrototypeYoyoProjectile
    {
        protected override string SpriteName => "FlinxYoyo";
        protected override int Size => 24;
    }

    public class YetiYoyoProjectile : PrototypeYoyoProjectile
    {
        protected override string SpriteName => "YetiYoyo";
        protected override int Size => 32;
        protected override float Lifetime => 16f;
        protected override float Range => 300f;
        protected override float Speed => 16f;
    }

    public abstract class PrototypeFlailProjectile : SharedFlailProjectile
    {
        protected abstract string SpriteName { get; }
        public override string Texture => $"FrostMelee/Content/Items/Prototypes/{SpriteName}";
    }

    public class PolarStarProjectile : PrototypeFlailProjectile
    {
        protected override string SpriteName => "PolarStar";
        public override int SourceItemType => ModContent.ItemType<global::FrostMelee.Content.Items.Prototypes.PolarStar>();
    }

    public class SnowBoomerangProjectile : ModProjectile
    {
        public override string Texture => "FrostMelee/Content/Items/Prototypes/SnowBoomerang";

        public override void SetDefaults()
        {
            Projectile.CloneDefaults(ProjectileID.EnchantedBoomerang);
            AIType = ProjectileID.EnchantedBoomerang;
            Projectile.DamageType = DamageClass.Melee;
        }
    }
}
