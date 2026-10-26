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

        // След центров для цельной ленты струи (PreDraw): голова — history[0]
        private const int HistLen = 10;
        private readonly Vector2[] history = new Vector2[HistLen];
        private readonly float[] ribbonWidths = new float[HistLen];
        private readonly Color[] ribbonColors = new Color[HistLen];
        private int histN;

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

            if (histN == 0)
                for (int k = 0; k < HistLen; k++)
                    history[k] = Projectile.Center;
            for (int k = HistLen - 1; k > 0; k--)
                history[k] = history[k - 1];
            history[0] = Projectile.Center;
            if (histN < HistLen)
                histN++;

            Vector2 dir = Projectile.velocity.SafeNormalize(Vector2.UnitX);
            // Досрочной смерти у струи нет: сколько задали при спавне, столько живёт.
            // Старая «смерть на возврате тычка» заодно убивала струи взмаха,
            // если игрок сразу после взмаха начинал тычок

            if (Projectile.timeLeft % 2 == 0)
                // Скорость снаряда — частицам: пламя летит вместе со струёй,
                // а не остаётся висеть в месте рождения позади взмаха
                FrostJetFx.EmitJet(Projectile.Center, dir, Projectile.velocity);

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

            // Цельная струя: лента по следу снаряда — жирная яркая голова и
            // тающий хвост вместо пары светящихся эллипсов. Частицы
            // FrostJetFx.EmitJet остаются вокруг искрами и жаром
            int n = histN;
            if (n >= 2)
            {
                float headW = 15f * pulse * (0.9f + 0.1f * reachScale);
                GraphicsDevice device = Main.graphics.GraphicsDevice;
                Main.spriteBatch.End();
                try
                {
                    for (int pass = 0; pass < 2; pass++)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            float f = i / (float)(n - 1);   // 0 — голова, 1 — хвост
                            ribbonWidths[i] = headW * (1f - f * 0.85f) * (pass == 0 ? 1.8f : 0.8f);
                            ribbonColors[i] = pass == 0
                                ? Color.Lerp(new Color(30, 105, 240), new Color(8, 34, 130), f) * (0.45f * flicker * fade)
                                : Color.Lerp(new Color(200, 246, 255), new Color(45, 130, 250), f) * (0.95f * fade);
                        }
                        FlameRibbon.Draw(device, history, ribbonWidths, ribbonColors, n);
                    }
                }
                finally
                {
                    Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                        DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
                }
            }

            // Яркое ядро у головы струи
            Vector2 origin = glow.Size() / 2f;
            Main.EntitySpriteDraw(glow, Projectile.Center - Main.screenPosition + dir * 1f, null,
                new Color(150, 220, 255) * (0.8f * fade), dir.ToRotation(), origin,
                new Vector2(12f / glow.Width, 7f / glow.Height), SpriteEffects.None, 0f);
            return false;
        }
    }
}
