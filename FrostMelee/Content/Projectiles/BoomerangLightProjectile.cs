using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles;

// Лёгкий бросок бумеранга (ПКМ-тап): слабый, проходит врагов насквозь и сам возвращается.
// Сквозь стены НЕ летит ни на вылете, ни на возврате (tileCollide весь полёт) —
// от поверхностей отскакивает. Это единственный «безопасный» удар,
// поэтому урон ниже базового (доктрина: бесплатный дальний урон слаб).
public class BoomerangLightProjectile : ModProjectile
{
	public const float LaunchSpeed = 13f;
	public const float ReturnSpeed = 15f;
	public const float OutDrag = 0.955f; // тормоз на вылете — дальность ~12 тайлов
	public const int OutMaxTicks = 26;
	public const float BounceLoss = 0.85f; // отскок от тайлов почти без потери скорости
	public const int ReturnMaxTicks = 300; // предохранитель: не долетел домой — исчез
	public const float CatchRadius = 28f;
	public const int FrostburnTicks = 180;

	public override string Texture => $"Terraria/Images/Item_{ItemID.IceBoomerang}";

	public override void SetDefaults()
	{
		Projectile.width = 28;
		Projectile.height = 28;
		Projectile.friendly = true;
		Projectile.DamageType = DamageClass.Melee;
		Projectile.penetrate = -1; // врагов насквозь
		Projectile.usesLocalNPCImmunity = true;
		Projectile.localNPCHitCooldown = 12;
		Projectile.tileCollide = true;
		Projectile.ignoreWater = true;
		Projectile.timeLeft = 600; // предохранитель
		Projectile.aiStyle = -1;
	}

	private ref float Returning => ref Projectile.ai[0];

	public override void AI()
	{
		Player owner = Main.player[Projectile.owner];
		if (!owner.active || owner.dead) {
			Projectile.Kill();
			return;
		}

		Projectile.rotation += 0.35f;
		Projectile.localAI[0]++;

		if (Returning == 0f) {
			Projectile.velocity *= OutDrag;
			if (Projectile.localAI[0] >= OutMaxTicks || Projectile.velocity.Length() < 2.2f) {
				Returning = 1f;
				Projectile.netUpdate = true;
			}
		}
		else {
			Projectile.localAI[1]++;
			if (Projectile.localAI[1] > ReturnMaxTicks) {
				Projectile.Kill();
				return;
			}
			Vector2 desired = (owner.Center - Projectile.Center).SafeNormalize(Vector2.Zero) * ReturnSpeed;
			Projectile.velocity = Vector2.Lerp(Projectile.velocity, desired, 0.28f);
			if (Vector2.Distance(Projectile.Center, owner.Center) < CatchRadius) {
				Projectile.Kill(); // поймали
				return;
			}
		}

		if (Projectile.localAI[0] % 3f == 0f) {
			Dust d = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height, DustID.Frost, 0f, 0f, 150, default, 0.8f);
			d.velocity *= 0.2f;
			d.noGravity = true;
		}
	}

	public override bool OnTileCollide(Vector2 oldVelocity)
	{
		// Бумеранг всегда отскакивает от поверхностей; сквозь стены не летит.
		if (Projectile.velocity.X != oldVelocity.X)
			Projectile.velocity.X = -oldVelocity.X * BounceLoss;
		if (Projectile.velocity.Y != oldVelocity.Y)
			Projectile.velocity.Y = -oldVelocity.Y * BounceLoss;
		SoundEngine.PlaySound(SoundID.Dig, Projectile.Center);
		return false;
	}

	public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
	{
		if (Main.rand.NextBool(3))
			target.AddBuff(BuffID.Frostburn, FrostburnTicks);
	}
}
