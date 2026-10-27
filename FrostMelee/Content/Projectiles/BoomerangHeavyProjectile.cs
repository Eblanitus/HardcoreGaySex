using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles;

// Тяжёлый бросок бумеранга (зарядка ПКМ): летит по дуге под гравитацией,
// втыкается в первого врага или в землю и ОСТАЁТСЯ там — иди забирай сам.
// Пока он жив, предметом нельзя пользоваться (BoomerangPlayer/GlobalItem).
// Сквозь стены не летит никогда.
public class BoomerangHeavyProjectile : ModProjectile
{
	public const float Gravity = 0.25f;
	public const float MaxFallSpeed = 14f;
	public const float BaseSpeed = 10f;
	public const float ChargeSpeedBonus = 7f;
	public const float PickupRadius = 36f;
	public const int FrostburnTicks = 240;

	public static float LaunchSpeed(float charge) => BaseSpeed + ChargeSpeedBonus * charge;

	private const int StateFlying = 0;
	private const int StateFalling = 1; // враг-носитель умер — бумеранг падает
	private const int StateStuck = 2;

	public override string Texture => $"Terraria/Images/Item_{ItemID.IceBoomerang}";

	public override void SetDefaults()
	{
		Projectile.width = 28;
		Projectile.height = 28;
		Projectile.friendly = true;
		Projectile.DamageType = DamageClass.Melee;
		// penetrate = -1: при penetrate=1 движок убивает снаряд сразу после OnHitNPC
		// и втыкание не успевает сработать. Один хит обеспечивает CanHitNPC + friendly=false.
		Projectile.penetrate = -1;
		Projectile.usesLocalNPCImmunity = true;
		Projectile.localNPCHitCooldown = -1;
		Projectile.tileCollide = true;
		Projectile.ignoreWater = true;
		Projectile.timeLeft = 600; // предохранитель полёта; воткнутый живёт до подбора
		Projectile.aiStyle = -1;
	}

	private ref float LodgedNpc => ref Projectile.ai[0]; // whoAmI врага, -1 = нет
	private ref float State => ref Projectile.ai[1];

	private Vector2 lodgeOffset;
	private bool lodgeInit;
	private int spinDir = 1;

	public override void AI()
	{
		Player owner = Main.player[Projectile.owner];
		int state = (int)State;

		if (state == StateStuck) {
			Projectile.timeLeft = 2; // живёт, пока не подберут
			if (LodgedNpc >= 0f) {
				NPC npc = Main.npc[(int)LodgedNpc];
				if (npc.active && npc.life > 0) {
					if (!lodgeInit) {
						lodgeOffset = Projectile.Center - npc.Center;
						lodgeInit = true;
					}
					Projectile.Center = npc.Center + lodgeOffset;
				}
				else {
					// Носитель умер — бумеранг падает на землю.
					LodgedNpc = -1f;
					lodgeInit = false;
					State = StateFalling;
					Projectile.tileCollide = true;
					Projectile.velocity = Vector2.Zero;
					Projectile.netUpdate = true;
				}
			}
			if (Projectile.owner == Main.myPlayer && owner.active && !owner.dead
				&& Vector2.Distance(Projectile.Center, owner.Center) < PickupRadius) {
				SoundEngine.PlaySound(SoundID.Grab, Projectile.Center);
				Projectile.Kill();
			}
			return;
		}

		// Полёт (бросок или падение после смерти носителя).
		if (Projectile.velocity.X != 0f)
			spinDir = Projectile.velocity.X >= 0f ? 1 : -1;
		Projectile.rotation += 0.4f * spinDir;
		Projectile.velocity.Y = MathHelper.Min(Projectile.velocity.Y + Gravity, MaxFallSpeed);

		if (state == StateFalling)
			Projectile.timeLeft = 2; // долетит до земли и воткнётся
	}

	// Бьёт только в полёте броска — воткнутый/падающий безвреден.
	public override bool? CanHitNPC(NPC target) => (int)State == StateFlying ? null : false;

	public override bool OnTileCollide(Vector2 oldVelocity)
	{
		Stick(-1f);
		SoundEngine.PlaySound(SoundID.Dig, Projectile.Center);
		return false;
	}

	public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
	{
		target.AddBuff(BuffID.Frostburn, FrostburnTicks);
		if ((int)State == StateFlying) {
			Stick(target.whoAmI);
			SoundEngine.PlaySound(SoundID.NPCHit5, Projectile.Center);
		}
	}

	private void Stick(float npcIndex)
	{
		State = StateStuck;
		LodgedNpc = npcIndex;
		Projectile.velocity = Vector2.Zero;
		Projectile.friendly = false;
		Projectile.tileCollide = false;
		// Рисуется поверх врага — должно быть видно, что воткнулся.
		Projectile.netUpdate = true;
	}
}
