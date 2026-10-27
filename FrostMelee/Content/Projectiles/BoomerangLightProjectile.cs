using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles;

// Лёгкий бросок бумеранга (ЛКМ держать): пугалка, а не охотничье оружие.
// Проходит врагов насквозь и сам возвращается, поэтому урона почти не наносит
// и Frostburn не вешает. Ледяная фишка — холодный вихрь: врагов рядом с путём
// сдувает и тормозит без урона. Сквозь стены НЕ летит ни на вылете, ни на
// возврате (tileCollide весь полёт) — от поверхностей отскакивает.
public class BoomerangLightProjectile : ModProjectile
{
	public const float LaunchSpeed = 13f;
	public const float ReturnSpeed = 15f;
	public const float OutDrag = 0.955f; // тормоз на вылете — дальность ~12 тайлов
	public const int OutMaxTicks = 26;
	public const float BounceLoss = 0.85f; // отскок от тайлов почти без потери скорости
	public const int ReturnMaxTicks = 300; // предохранитель: не долетел домой — исчез
	public const float CatchRadius = 28f;

	// Холодный вихрь: работает и без попадания, но урона не наносит.
	public const float VortexRadius = 64f;
	public const float VortexPush = 0.55f; // импульс раз в VortexIntervalTicks, с затуханием к краю
	public const int VortexSlowTicks = 45;
	public const int VortexIntervalTicks = 6;

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

		if (Projectile.localAI[0] % VortexIntervalTicks == 0f)
			ApplyColdVortex();

		if (Projectile.localAI[0] % 3f == 0f) {
			Dust d = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height, DustID.Frost, 0f, 0f, 150, default, 0.8f);
			d.velocity *= 0.2f;
			d.noGravity = true;
		}
	}

	// Холодный вихрь: вращение ледяной лопасти сдувает врагов рядом с путём.
	// Толчок идёт наружу и по касательной в сторону вращения; урона здесь нет,
	// только короткий Slow. Толкает только владелец — иначе в сети импульс задвоится.
	private void ApplyColdVortex()
	{
		if (Projectile.owner != Main.myPlayer)
			return;

		for (int i = 0; i < Main.maxNPCs; i++) {
			NPC npc = Main.npc[i];
			if (!npc.active || npc.friendly || npc.life <= 0)
				continue;

			Vector2 offset = npc.Center - Projectile.Center;
			float dist = offset.Length();
			if (dist <= 0.01f || dist > VortexRadius)
				continue;

			npc.AddBuff(BuffID.Slow, VortexSlowTicks);

			float resist = MathHelper.Clamp(npc.knockBackResist, 0f, 1f);
			if (resist <= 0f)
				continue;

			Vector2 away = offset / dist;
			Vector2 tangent = new Vector2(-away.Y, away.X); // по часовой, как крутится спрайт
			float falloff = 1f - dist / VortexRadius;
			Vector2 push = (away * 0.65f + tangent * 0.75f).SafeNormalize(away)
				* VortexPush * (0.35f + 0.65f * falloff) * resist;
			npc.velocity += push;
		}

		// Одна ванильная пылинка по кольцу вихря — поток должен быть виден.
		float angle = Main.rand.NextFloat(MathHelper.TwoPi);
		Vector2 ringOffset = angle.ToRotationVector2() * (VortexRadius * 0.65f);
		Vector2 ringTangent = new Vector2(-ringOffset.Y, ringOffset.X).SafeNormalize(Vector2.Zero);
		Dust d = Dust.NewDustDirect(Projectile.Center + ringOffset - new Vector2(2f), 4, 4,
			DustID.Frost, ringTangent.X * 1.4f, ringTangent.Y * 1.4f, 150, default, 0.75f);
		d.noGravity = true;
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
}
