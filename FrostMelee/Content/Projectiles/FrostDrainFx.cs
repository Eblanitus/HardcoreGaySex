using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    // Анимация «тычок забирает ледяное горение»: холодное пламя срывается с врага
    // языками и по дуге стекается обратно в клинок Фростбранда, клинок вспыхивает.
    // Только визуал: урона нет, бафф не трогает (это делает FrostFireSystem.TakeBurn).
    //
    // Точка вызова — FrostDrainFx.Play(target, owner, burnTicks) у владельца
    // (OnHitNPC тычка). Снаряд синхронизируется ваниллой — анимацию видят все.
    // ai[0] — whoAmI врага, ai[1] — доля забранного горения 0..1 (сколько языков и как ярко).
    public class FrostDrainFx : ModProjectile
    {
        public override string Texture => "Terraria/Images/Extra_" + ExtrasID.ThePerfectGlow;

        // --- Настройка вида (усилено по просьбе владельца: заметнее и ярче) ---
        private const int MinWisps = 7;           // языков пламени при слабом горении
        private const int MaxWisps = 18;          // при полном
        private const int TrailLen = 10;          // точек хвоста у языка
        private const int Lifetime = 100;         // страховка: снаряд живёт не дольше
        private const int RingTicks = 14;         // схлопывающееся кольцо на враге
        private const int ChargeTicks = 30;       // вспышка клинка после поглощения
        private static readonly Color Core = new Color(235, 250, 255);
        private static readonly Color Flame = new Color(150, 220, 255);
        private static readonly Color Deep = new Color(60, 140, 255);

        // Ручка вызова: вызывать у владельца, burnTicks — сколько горения забрано
        public static void Play(NPC target, Player owner, int burnTicks)
        {
            if (target == null || owner == null || owner.whoAmI != Main.myPlayer || burnTicks <= 0)
                return;
            float ratio = MathHelper.Clamp(burnTicks / (float)Content.Weapons.FrostFireSystem.BurnTicks, 0f, 1f);
            Projectile.NewProjectile(owner.GetSource_FromThis(), target.Center, Vector2.Zero,
                ModContent.ProjectileType<FrostDrainFx>(), 0, 0f, owner.whoAmI, target.whoAmI, ratio);
        }

        private struct Wisp
        {
            public Vector2 Pos, Vel;
            public int Delay;       // тиков до старта: языки срываются вразнобой
            public bool Done;
            public float Size, Phase;
            public Vector2[] Trail;
        }

        private Wisp[] wisps;
        private Vector2 origin;      // где стоял враг в момент забора
        private float originRadius;
        private int absorbed;        // сколько языков уже влилось в клинок
        private int lastAbsorbTick = -100;

        public override void SetDefaults()
        {
            Projectile.width = 8;
            Projectile.height = 8;
            Projectile.friendly = false;
            Projectile.hostile = false;
            Projectile.damage = 0;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = Lifetime;
            Projectile.aiStyle = -1;
        }

        public override bool? CanDamage() => false;
        public override bool ShouldUpdatePosition() => false;

        private float Ratio => MathHelper.Clamp(Projectile.ai[1], 0f, 1f);
        private int Age => Lifetime - Projectile.timeLeft;

        // Куда стекается пламя: точка на клинке ближе к острию; меча нет — кисть игрока
        private static Vector2 BladePoint(Player owner, float along)
        {
            if (owner.GetModPlayer<Content.Weapons.SwordPlayer>().TryGetBladeLine(out Vector2 butt, out Vector2 tip))
                return Vector2.Lerp(butt, tip, along);
            return owner.MountedCenter;
        }

        private void Init()
        {
            NPC npc = Main.npc[(int)Projectile.ai[0]];
            origin = npc.active ? npc.Center : Projectile.Center;
            originRadius = npc.active ? Math.Max(npc.width, npc.height) * 0.6f : 20f;
            int n = (int)MathHelper.Lerp(MinWisps, MaxWisps, Ratio);
            wisps = new Wisp[n];
            for (int i = 0; i < n; i++)
            {
                // старт — на «теле» врага, разлёт наружу, потом разворот к мечу
                Vector2 off = Main.rand.NextVector2Circular(originRadius * 0.7f, originRadius * 0.7f);
                Vector2 outDir = (off.LengthSquared() > 1f ? off : Main.rand.NextVector2Unit()).SafeNormalize(Vector2.UnitY);
                wisps[i] = new Wisp
                {
                    Pos = origin + off,
                    Vel = outDir.RotatedByRandom(0.6f) * Main.rand.NextFloat(2.5f, 4.5f) - Vector2.UnitY * 1.2f,
                    Delay = Main.rand.Next(0, 9),
                    Size = Main.rand.NextFloat(1.1f, 1.7f),
                    Phase = Main.rand.NextFloat(MathHelper.TwoPi),
                    Trail = new Vector2[TrailLen]
                };
                for (int k = 0; k < TrailLen; k++)
                    wisps[i].Trail[k] = wisps[i].Pos;
            }
            if (!Main.dedServ)
                SoundEngine.PlaySound(SoundID.Item28 with { Volume = 0.55f, Pitch = -0.3f + 0.2f * Ratio }, origin);
        }

        public override void AI()
        {
            if (wisps == null)
                Init();
            Player owner = Main.player[Projectile.owner];
            if (!owner.active || owner.dead)
            {
                Projectile.Kill();
                return;
            }
            // враг ещё жив — кольцо и языки идут от его текущего места
            NPC npc = Main.npc[(int)Projectile.ai[0]];
            if (npc.active)
                origin = Vector2.Lerp(origin, npc.Center, 0.5f);
            Projectile.Center = origin;

            int alive = 0;
            for (int i = 0; i < wisps.Length; i++)
            {
                ref Wisp w = ref wisps[i];
                if (w.Done)
                    continue;
                alive++;
                if (w.Delay > 0)
                {
                    w.Delay--;
                    w.Pos += npc.active ? npc.velocity : Vector2.Zero;   // ждёт на враге, не отстаёт
                    for (int k = 0; k < TrailLen; k++)
                        w.Trail[k] = w.Pos;
                    continue;
                }

                // самонаведение с нарастающей силой: сначала дуга наружу, потом рывок в клинок
                Vector2 target = BladePoint(owner, 0.55f + 0.35f * ((i * 0.37f) % 1f));
                Vector2 to = target - w.Pos;
                float dist = to.Length();
                float pull = MathHelper.Lerp(0.35f, 1.6f, MathHelper.Clamp((Age - w.Delay) / 26f, 0f, 1f));
                w.Vel += to.SafeNormalize(Vector2.Zero) * pull;
                float maxSpeed = MathHelper.Lerp(6f, 15f, MathHelper.Clamp((Age - w.Delay) / 30f, 0f, 1f));
                if (w.Vel.Length() > maxSpeed)
                    w.Vel = w.Vel.SafeNormalize(Vector2.Zero) * maxSpeed;
                // лёгкое виляние пламени поперёк хода
                w.Vel += w.Vel.SafeNormalize(Vector2.Zero).RotatedBy(MathHelper.PiOver2)
                    * (float)Math.Sin(Age * 0.5f + w.Phase) * 0.35f;

                for (int k = TrailLen - 1; k > 0; k--)
                    w.Trail[k] = w.Trail[k - 1];
                w.Pos += w.Vel;
                w.Trail[0] = w.Pos;

                if (!Main.dedServ && Main.rand.NextBool(2))
                {
                    Dust d = Dust.NewDustPerfect(w.Pos, DustID.IceTorch, -w.Vel * 0.15f, 100, default, Main.rand.NextFloat(0.9f, 1.4f) * w.Size);
                    d.noGravity = true;
                }
                Lighting.AddLight(w.Pos, 0.25f, 0.5f, 0.75f);

                // влился в клинок
                if (dist < 10f + w.Vel.Length())
                {
                    w.Done = true;
                    absorbed++;
                    lastAbsorbTick = Age;
                    if (!Main.dedServ)
                    {
                        for (int k = 0; k < 5; k++)
                        {
                            Dust d = Dust.NewDustPerfect(target, DustID.IceTorch, Main.rand.NextVector2Circular(2.2f, 2.2f), 80, default, 1.5f);
                            d.noGravity = true;
                        }
                        if (absorbed == 1)
                            SoundEngine.PlaySound(SoundID.Item30 with { Volume = 0.45f, Pitch = 0.4f }, target);
                    }
                }
            }

            if (absorbed > 0)
                Lighting.AddLight(BladePoint(owner, 0.7f), new Vector3(0.45f, 0.85f, 1.2f) * ChargePulse());

            // все влились и вспышка клинка отыграла — конец
            if (alive == 0 && Age - lastAbsorbTick > ChargeTicks)
                Projectile.Kill();
        }

        // 0..1: вспышка клинка после последнего поглощённого языка
        private float ChargePulse()
        {
            int since = Age - lastAbsorbTick;
            if (absorbed == 0 || since > ChargeTicks)
                return 0f;
            float t = since / (float)ChargeTicks;
            return (1f - t) * (1f - t) * MathHelper.Lerp(0.6f, 1f, Ratio);
        }

        private static Color Add(Color c, float a)
        {
            Color r = c * MathHelper.Clamp(a, 0f, 1f);
            r.A = 0;
            return r;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            if (wisps == null)
                return false;
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow == null || glow.IsDisposed)
                return false;
            Vector2 go = glow.Size() / 2f;
            float bright = MathHelper.Lerp(0.85f, 1.15f, Ratio);

            // 1) кольцо холодного огня схлопывается в врага — горение «срывают»
            if (Age < RingTicks)
            {
                float t = Age / (float)RingTicks;
                float r = originRadius * MathHelper.Lerp(1.4f, 0.2f, t * t);
                float a = (1f - t) * bright;
                const int Seg = 18;
                for (int i = 0; i < Seg; i++)
                {
                    float ang = MathHelper.TwoPi * i / Seg + Age * 0.15f;
                    Vector2 p = origin + ang.ToRotationVector2() * r;
                    float tw = 0.7f + 0.3f * (float)Math.Sin(i * 2.1f + Age);
                    Main.EntitySpriteDraw(glow, p - Main.screenPosition, null, Add(Flame, a * tw), ang,
                        go, new Vector2(15f / glow.Width, 6f / glow.Height), SpriteEffects.None, 0f);
                }
                Main.EntitySpriteDraw(glow, origin - Main.screenPosition, null, Add(Deep, a * 0.7f), 0f,
                    go, originRadius * 1.6f * (1f - 0.5f * t) / glow.Width, SpriteEffects.None, 0f);
                // яркая вспышка в месте срыва пламени в первые мгновения
                Main.EntitySpriteDraw(glow, origin - Main.screenPosition, null, Add(Core, a * 0.85f), 0f,
                    go, originRadius * (1.1f - 0.6f * t) / glow.Width, SpriteEffects.None, 0f);
            }

            // 2) языки пламени: сужающийся хвост + яркая голова
            foreach (Wisp w in wisps)
            {
                if (w.Done || w.Delay > 0)
                    continue;
                for (int k = TrailLen - 1; k > 0; k--)
                {
                    Vector2 a = w.Trail[k], b = w.Trail[k - 1];
                    Vector2 d = b - a;
                    float len = d.Length();
                    if (len < 0.5f)
                        continue;
                    float f = 1f - k / (float)TrailLen;
                    float wdt = (4f + 10f * f) * w.Size;
                    Color c = Color.Lerp(Deep, Flame, f);
                    Main.EntitySpriteDraw(glow, (a + b) * 0.5f - Main.screenPosition, null, Add(c, f * 0.95f * bright),
                        d.ToRotation(), go, new Vector2((len + wdt) / glow.Width, wdt / glow.Height), SpriteEffects.None, 0f);
                }
                float flick = 0.85f + 0.15f * (float)Math.Sin(Age * 0.9f + w.Phase);
                Main.EntitySpriteDraw(glow, w.Pos - Main.screenPosition, null, Add(Flame, 0.95f * bright), 0f,
                    go, 20f * w.Size * flick / glow.Width, SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(glow, w.Pos - Main.screenPosition, null, Add(Core, bright), 0f,
                    go, 9f * w.Size / glow.Width, SpriteEffects.None, 0f);
            }

            // 3) клинок принял пламя: свечение вдоль клинка и звезда у острия
            float pulse = ChargePulse();
            Player owner = Main.player[Projectile.owner];
            if (pulse > 0.02f && owner.GetModPlayer<Content.Weapons.SwordPlayer>().TryGetBladeLine(out Vector2 butt, out Vector2 tip))
            {
                Vector2 d = tip - butt;
                float len = d.Length();
                Vector2 mid = Vector2.Lerp(butt, tip, 0.6f);
                Main.EntitySpriteDraw(glow, mid - Main.screenPosition, null, Add(Flame, pulse * 0.9f), d.ToRotation(),
                    go, new Vector2(len * 1.1f / glow.Width, 22f / glow.Height), SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(glow, mid - Main.screenPosition, null, Add(Core, pulse * 0.75f), d.ToRotation(),
                    go, new Vector2(len * 0.8f / glow.Width, 8f / glow.Height), SpriteEffects.None, 0f);
                float star = 38f * pulse;
                for (int r = 0; r < 2; r++)
                    Main.EntitySpriteDraw(glow, tip - Main.screenPosition, null, Add(Core, pulse), d.ToRotation() + MathHelper.PiOver2 * r,
                        go, new Vector2(star / glow.Width, star * 0.12f / glow.Height), SpriteEffects.None, 0f);
            }
            return false;
        }
    }
}
