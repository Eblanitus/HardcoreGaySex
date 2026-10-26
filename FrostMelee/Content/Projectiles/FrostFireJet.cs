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

        // Физика струи встречи: воздух понемногу возвращает её продольную
        // скорость к собственной (4.5), а не к скорости игрока.
        private const float BraceAirDrag = 0.07f;
        private const float BraceSideFollow = 0.45f;
        private const float BraceMinRelativeSpeed = 1.1f;
        private const float BraceMaxRelativeSpeed = 8.2f;

        // Возраст струи в тиках — по нему FrostJetFx выстраивает все живые
        // струи владельца в одну общую ленту (localAI[0])

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
            Projectile.localAI[0]++;

            // Кто спавнил, тот задаёт длину струи через ai[0] (тики жизни):
            // встреча — растущую, веер взмаха — короткую. 0 = обычная (SetDefaults)
            if (Projectile.localAI[1] == 0f)
            {
                Projectile.localAI[1] = 1f;
                if (Projectile.ai[0] > 0f)
                    Projectile.timeLeft = (int)Projectile.ai[0];
            }

            // Струя встречи (ai[1] == 0, ai[2] — её направление) живёт своей
            // скоростью, а не приклеена к игроку. При рождении она получает
            // скорость игрока, но воздух каждый тик возвращает продольную
            // часть к собственной скорости струи. Поэтому при беге вперёд меч
            // догоняет старые языки и прижимает их к себе, а при беге назад
            // уходит от них — языки остаются развеянными впереди. Скорость
            // игрока теперь нужна только как предел: меч не должен проехать
            // сквозь струю, а струя — бесконечно оторваться от него.
            // Боковое движение мягко следует за мечом, иначе поток целиком
            // останется висеть сбоку от клинка при прыжке и падении.
            // Струи веера взмаха (ai[1] == 1/2) сюда не входят.
            if (Projectile.ai[1] == 0f)
            {
                Vector2 jetDir = Projectile.ai[2].ToRotationVector2();
                Vector2 ownerVelocity = Main.player[Projectile.owner].velocity;
                float ownerAlong = Vector2.Dot(ownerVelocity, jetDir);
                Vector2 ownerSideVelocity = ownerVelocity - jetDir * ownerAlong;

                float alongSpeed = Vector2.Dot(Projectile.velocity, jetDir);
                Vector2 sideVelocity = Projectile.velocity - jetDir * alongSpeed;

                alongSpeed = MathHelper.Lerp(alongSpeed,
                    SickleBraceProjectile.FrostJetSpeed, BraceAirDrag);
                alongSpeed = MathHelper.Clamp(alongSpeed,
                    ownerAlong + BraceMinRelativeSpeed,
                    ownerAlong + BraceMaxRelativeSpeed);
                sideVelocity = Vector2.Lerp(sideVelocity, ownerSideVelocity, BraceSideFollow);

                Vector2 physicalVelocity = jetDir * alongSpeed + sideVelocity;
                if ((physicalVelocity - Projectile.velocity).LengthSquared() > 0.0001f)
                {
                    Projectile.velocity = physicalVelocity;
                    Projectile.netUpdate = true;
                }
            }

            Vector2 dir = Projectile.velocity.SafeNormalize(Vector2.UnitX);
            // Досрочной смерти у струи нет: сколько задали при спавне, столько живёт.
            // Старая «смерть на возврате тычка» заодно убивала струи взмаха,
            // если игрок сразу после взмаха начинал тычок

            if ((int)Projectile.localAI[0] % 3 == 0)
                // Скорость снаряда — частицам: пламя летит вместе со струёй,
                // а не остаётся висеть в месте рождения позади взмаха
                FrostJetFx.EmitJet(Projectile.Center, dir, Projectile.velocity, FrostJetFx.HandSource(Projectile.owner));

            // Пыль редкая: веер взмаха спавнит струй много, и частая пыль
            // каждой складывалась в рой крестиков, забивавший саму дугу.
            // Видимое пламя несёт FrostJetFx выше, пыль — только подсыпка
            if (!Main.dedServ && Main.rand.NextBool(8))
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
                    if (!Weapons.FrostFireSystem.CanBurn(npc))
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

            // Тело струи рисует FrostJetFx ОДНОЙ общей лентой через все живые
            // струи владельца от острия — огоньки закреплены друг за друга
            // (требование владельца). Здесь — только яркий узелок на этой струе
            Vector2 dir = Projectile.velocity.SafeNormalize(Vector2.UnitX);
            float fade = Projectile.timeLeft < 5 ? Projectile.timeLeft / 5f : 1f;
            Vector2 origin = glow.Size() / 2f;
            Main.EntitySpriteDraw(glow, Projectile.Center - Main.screenPosition + dir * 1f, null,
                new Color(150, 220, 255) * (0.28f * fade * FrostMeleeConfigDefaults.FireHeatScale), dir.ToRotation(), origin,
                new Vector2(9f * FrostMeleeConfigDefaults.FireSizeScale / glow.Width,
                    5.5f * FrostMeleeConfigDefaults.FireSizeScale / glow.Height), SpriteEffects.None, 0f);
            return false;
        }
    }
}
