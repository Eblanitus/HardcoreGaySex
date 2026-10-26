using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    // Один язык холодного пламени Фростбранда — живая единица конечного запаса
    // (балансир владельца: у меча всего SwordPlayer.MaxFlames языков).
    // Состояния (ai[0]):
    //   0 — возвращается от мёртвого горящего врага к игроку и БЬЁТ всех на пути;
    //   1 — висит огоньком у игрока (урона нет, ждёт взмаха);
    //   2 — ушёл со взмахом: бьёт и поджигает первого врага, становясь его пламенем.
    // Бафф вернувшегося огня — сам этот огонёк: он наносит урон.
    // ai[1] — фаза (виляние/место в орбите). Счёт языков и запас — SwordPlayer.
    public class FrostFlameWisp : ModProjectile
    {
        public override string Texture => "Terraria/Images/Extra_" + ExtrasID.ThePerfectGlow;

        private const int StateReturn = 0, StateOrbit = 1, StateLaunched = 2;
        private const int ReturnTimeout = 240;    // не долетел за 4 с — растаял в меч (запас свободен)
        private const int LaunchedLife = 20;      // тиков полёта со взмахом
        private const float LaunchedSpeed = 13f;
        private const int TrailLen = 8;
        private static readonly Color Core = new Color(235, 250, 255);
        private static readonly Color Flame = new Color(150, 220, 255);
        private static readonly Color Deep = new Color(60, 140, 255);

        private Vector2[] trail;
        private readonly float[] ribbonWidths = new float[TrailLen];
        private readonly Color[] ribbonColors = new Color[TrailLen];

        public bool IsOrbiting => (int)Projectile.ai[0] == StateOrbit;
        private int State => (int)Projectile.ai[0];
        private float Phase => Projectile.ai[1];
        private int Age
        {
            get => (int)Projectile.localAI[0];
            set => Projectile.localAI[0] = value;
        }

        public override void SetDefaults()
        {
            Projectile.width = 16;
            Projectile.height = 16;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1; // каждую цель — один раз за пролёт
            Projectile.tileCollide = false;      // движение ручное (ShouldUpdatePosition)
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 3600;          // смертью управляют состояния, это страховка
            Projectile.aiStyle = -1;
        }

        public override bool ShouldUpdatePosition() => false;

        // В орбите огонёк свой и безобидный; бьёт только в полёте (возврат и взмах)
        public override bool? CanDamage() => State != StateOrbit;

        public void Launch(Vector2 dir)
        {
            Projectile.ai[0] = StateLaunched;
            Age = 0;
            Projectile.velocity = dir * LaunchedSpeed;
            Projectile.netUpdate = true;
        }

        public override void AI()
        {
            Player owner = Main.player[Projectile.owner];
            if (!owner.active || owner.dead)
            {
                Projectile.Kill();
                return;
            }
            if (trail == null)
            {
                trail = new Vector2[TrailLen];
                for (int i = 0; i < TrailLen; i++)
                    trail[i] = Projectile.Center;
            }
            Age++;
            bool mine = Projectile.owner == Main.myPlayer;

            switch (State)
            {
                case StateReturn:
                {
                    Vector2 to = owner.MountedCenter - Projectile.Center;
                    float dist = to.Length();
                    float ramp = MathHelper.Clamp(Age / 26f, 0f, 1f);
                    Projectile.velocity += to.SafeNormalize(Vector2.Zero) * MathHelper.Lerp(0.3f, 1.6f, ramp);
                    // Резинка возврата: чем дальше огонёк отстал, тем выше
                    // потолок скорости — быстрого игрока догонит, а не будет
                    // тащиться цепочкой комет по траектории полёта
                    float maxSpeed = MathHelper.Lerp(7f, 17f, ramp) + MathHelper.Clamp((dist - 160f) * 0.03f, 0f, 16f);
                    if (Projectile.velocity.Length() > maxSpeed)
                        Projectile.velocity = Projectile.velocity.SafeNormalize(Vector2.Zero) * maxSpeed;
                    // лёгкое виляние пламени поперёк хода, как у языков забора
                    Projectile.velocity += Projectile.velocity.SafeNormalize(Vector2.Zero).RotatedBy(MathHelper.PiOver2)
                        * (float)Math.Sin(Age * 0.45f + Phase) * 0.3f;
                    Projectile.Center += Projectile.velocity;
                    if (mine && (Age > ReturnTimeout || dist > 1400f))
                        Projectile.Kill(); // не долетел — растаял в меч, запас освободился
                    else if (mine && dist < 16f)
                    {
                        Projectile.ai[0] = StateOrbit;
                        Age = 0;
                        Projectile.velocity = Vector2.Zero;
                        Projectile.netUpdate = true;
                    }
                    break;
                }
                case StateOrbit:
                {
                    float ang = Phase + Age * 0.045f;
                    Vector2 want = owner.Center + new Vector2((float)Math.Cos(ang) * 38f, (float)Math.Sin(ang) * 24f - 12f);
                    Projectile.Center = Vector2.Lerp(Projectile.Center, want, 0.35f);
                    Projectile.velocity = Vector2.Zero;
                    if (mine && Age > Weapons.SwordPlayer.OrbitMeltTicks)
                        Projectile.Kill(); // не потратили — влился обратно в меч
                    break;
                }
                case StateLaunched:
                {
                    Projectile.Center += Projectile.velocity;
                    if (Age > LaunchedLife || Collision.SolidCollision(Projectile.position, Projectile.width, Projectile.height))
                        Projectile.Kill(); // промахнулся — растаял в меч
                    break;
                }
            }

            for (int k = TrailLen - 1; k > 0; k--)
                trail[k] = trail[k - 1];
            trail[0] = Projectile.Center;

            Lighting.AddLight(Projectile.Center, 0.2f, 0.45f, 0.65f);
            if (!Main.dedServ && Main.rand.NextBool(6))
            {
                Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.IceTorch,
                    -Projectile.velocity * 0.1f + Main.rand.NextVector2Circular(0.4f, 0.4f), 100, default, Main.rand.NextFloat(0.8f, 1.2f));
                d.noGravity = true;
            }
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            // Ушедший со взмахом огонёк становится пламенем первого врага,
            // которого задел: поджёг — и сам в нём остался (запас переехал).
            // Уже горящего «своего» только обновляет и летит дальше
            if (State != StateLaunched || Projectile.owner != Main.myPlayer)
                return;
            var sp = Main.player[Projectile.owner].GetModPlayer<Weapons.SwordPlayer>();
            if (sp.HoldsFlame(target))
            {
                target.AddBuff(BuffID.Frostburn, Weapons.FrostFireSystem.BurnTicks);
                return;
            }
            sp.AddFlameHolder(target);
            target.AddBuff(BuffID.Frostburn, Weapons.FrostFireSystem.BurnTicks);
            Projectile.Kill();
        }

        private static Color Add(Color c, float a)
        {
            Color r = c * MathHelper.Clamp(a, 0f, 1f);
            r.A = 0;
            return r;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            if (trail == null)
                return false;
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow == null || glow.IsDisposed)
                return false;
            Vector2 go = glow.Size() / 2f;

            // Хвост — цельная лента пламени по следу: огонёк тянется живым
            // языком, а не цепочкой отдельных пятен (как струя FrostFireJet)
            float trailSpan = 0f;
            for (int k = 1; k < TrailLen; k++)
                trailSpan += Vector2.Distance(trail[k], trail[k - 1]);
            if (trailSpan > 2f)
            {
                GraphicsDevice device = Main.graphics.GraphicsDevice;
                Main.spriteBatch.End();
                try
                {
                    for (int pass = 0; pass < 2; pass++)
                    {
                        for (int i = 0; i < TrailLen; i++)
                        {
                            float f = i / (float)(TrailLen - 1);   // 0 — голова, 1 — хвост
                            ribbonWidths[i] = MathHelper.Lerp(11f, 2f, f) * (pass == 0 ? 1.8f : 0.8f);
                            ribbonColors[i] = pass == 0
                                ? Color.Lerp(new Color(40, 120, 245), Deep, f) * 0.5f
                                : Color.Lerp(Color.Lerp(Core, Flame, MathHelper.Clamp(f * 3f, 0f, 1f)), Deep,
                                    MathHelper.Clamp((f - 0.4f) / 0.6f, 0f, 1f)) * 0.88f;
                        }
                        FlameRibbon.Draw(device, trail, ribbonWidths, ribbonColors, TrailLen);
                    }
                }
                finally
                {
                    Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                        DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
                }
            }
            float flick = 0.85f + 0.15f * (float)Math.Sin(Age * 0.9f + Phase);
            Main.EntitySpriteDraw(glow, Projectile.Center - Main.screenPosition, null, Add(Flame, 0.95f), 0f,
                go, 17f * flick / glow.Width, SpriteEffects.None, 0f);
            Main.EntitySpriteDraw(glow, Projectile.Center - Main.screenPosition, null, Add(Core, 1f), 0f,
                go, 7f / glow.Width, SpriteEffects.None, 0f);
            return false;
        }
    }
}
