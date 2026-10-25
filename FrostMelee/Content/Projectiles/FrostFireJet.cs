using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    // Струя холодного пламени Фростбранда. Урона не наносит: только поджигает.
    public class FrostFireJet : ModProjectile
    {
        public override string Texture => "FrostMelee/Items/Frostbrand";

        public override void SetDefaults()
        {
            Projectile.width = 18;
            Projectile.height = 18;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.tileCollide = true;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 12;
            Projectile.aiStyle = -1; // только свой AI, как у остальных снарядов мода
        }

        public override void AI()
        {
            // Кто спавнил, тот задаёт длину струи через ai[0] (тики жизни):
            // встреча — растущую, веер взмаха — короткую. 0 = обычная (SetDefaults)
            if (Projectile.localAI[1] == 0f)
            {
                Projectile.localAI[1] = 1f;
                if (Projectile.ai[0] > 0f)
                    Projectile.timeLeft = (int)Projectile.ai[0];
            }

            Vector2 dir = Projectile.velocity.SafeNormalize(Vector2.UnitX);
            // Досрочной смерти у струи нет: сколько задали при спавне, столько живёт.
            // Старая «смерть на возврате тычка» заодно убивала струи взмаха,
            // если игрок сразу после взмаха начинал тычок

            if (Projectile.timeLeft % 2 == 0)
                FrostJetFx.EmitJet(Projectile.Center, dir);

            // Пыль редкая: веер взмаха спавнит струй много, и частая пыль
            // каждой складывалась в рой крестиков, забивавший саму дугу.
            // Видимое пламя несёт FrostJetFx выше, пыль — только подсыпка
            if (!Main.dedServ && Main.rand.NextBool(4))
            {
                Dust d = Dust.NewDustPerfect(Projectile.Center + Main.rand.NextVector2Circular(5f, 5f),
                    DustID.IceTorch, -Projectile.velocity * 0.15f + Main.rand.NextVector2Circular(0.5f, 0.5f),
                    100, default, Main.rand.NextFloat(0.9f, 1.6f));
                d.noGravity = true;
            }

            // Поджог проверяется КАЖДЫЙ тик без хитрых шагов. История бага:
            // раньше шаг был 15 при жизни снаряда 12 — проверка не наступала
            // никогда, и струя не поджигала вообще (поджигал клинок, наоборот
            // дизайну). Больше никакой арифметики: каждый тик, точка.
            // Хитбокс чуть шире тела снаряда (Inflate): видимое пламя в PreDraw
            // заметно крупнее 18px, враг должен загораться от видимого огня.
            if (Main.netMode != NetmodeID.MultiplayerClient || Projectile.owner == Main.myPlayer)
            {
                Rectangle hitbox = Projectile.Hitbox;
                hitbox.Inflate(6, 6);
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    NPC npc = Main.npc[i];
                    if (!npc.active || npc.friendly || npc.dontTakeDamage || npc.immortal)
                        continue;
                    if (!npc.Hitbox.Intersects(hitbox))
                        continue;
                    Weapons.FrostFireSystem.TryIgnite(npc, Main.player[Projectile.owner]);
                }
            }

            Lighting.AddLight(Projectile.Center, 0.2f, 0.45f, 0.6f);
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            for (int i = 0; i < 6; i++)
            {
                Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.IceTorch,
                    Main.rand.NextVector2Circular(2f, 2f), 100, default, 1.1f);
                d.noGravity = true;
            }
            return true;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow == null || glow.IsDisposed)
                return false;

            Vector2 dir = Projectile.velocity.SafeNormalize(Vector2.UnitX);
            float fade = Projectile.timeLeft < 5 ? Projectile.timeLeft / 5f : 1f;
            float pulse = 1f + 0.08f * (float)System.Math.Sin(Main.GameUpdateCount * 0.35f + Projectile.whoAmI * 1.7f);
            float flicker = 0.85f + 0.15f * (float)System.Math.Sin(Main.GameUpdateCount * 0.5f + Projectile.whoAmI);
            float reachScale = MathHelper.Clamp(Projectile.ai[0] / 12f, 1f, 2.5f);
            float length = 22f * pulse * reachScale;
            float width = 16f * pulse * (0.9f + 0.1f * reachScale);
            float rotation = dir.ToRotation();
            Vector2 center = Projectile.Center - Main.screenPosition;
            Vector2 origin = glow.Size() / 2f;

            Main.EntitySpriteDraw(glow, center - dir * 2f, null,
                new Color(40, 115, 240) * (0.35f * flicker * fade), rotation, origin,
                new Vector2(length * 0.8f / glow.Width, width * 0.78f / glow.Height),
                SpriteEffects.None, 0f);
            Main.EntitySpriteDraw(glow, center + dir * 1f, null,
                new Color(120, 205, 255) * (0.85f * fade), rotation, origin,
                new Vector2(length * 0.42f / glow.Width, width * 0.38f / glow.Height),
                SpriteEffects.None, 0f);
            return false;
        }
    }
}
