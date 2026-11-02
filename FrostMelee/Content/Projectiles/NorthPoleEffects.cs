using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using FrostMelee.Content.Weapons;

namespace FrostMelee.Content.Projectiles
{
    internal static class NorthPoleEffects
    {
        public static bool Owned(Projectile p) => p.owner == Main.myPlayer && p.owner >= 0
            && p.owner < Main.maxPlayers && Main.player[p.owner].active && !Main.player[p.owner].dead;

        // Like vanilla North Pole: the trail snow is spawned even inside tiles
        // and falls through them.
        public static void Trail(Projectile spear)
        {
            // on every client: the flying spear sheds snow and aurora motes
            NorthPoleFx.ShakeSnow(spear.Center, 1f, 8f);
            NorthPoleFx.AuroraDust(spear.Center, -spear.velocity * 0.1f, 1.1f);
            if (!Owned(spear)) return;
            Projectile.NewProjectile(spear.GetSource_FromThis(), spear.Center, Vector2.Zero,
                ModContent.ProjectileType<NorthPoleTrail>(), Math.Max(1, (int)(spear.damage * NorthPoleBalance.TrailDamage)),
                spear.knockBack * 0.55f, spear.owner, 0, Main.rand.Next(3));
        }

        public static void Coat(Projectile spear, NPC target)
        {
            if (!Owned(spear) || !target.active || target.life <= 0 || target.friendly) return;
            target.AddBuff(BuffID.Frostburn2, NorthPoleBalance.FrostbiteTicks);
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.owner == spear.owner && p.ModProjectile is NorthPoleSnowCoat
                    && (int)p.ai[0] == target.whoAmI && (int)p.ai[1] == target.type)
                {
                    p.timeLeft = NorthPoleBalance.FrostbiteTicks;
                    p.netUpdate = true;
                    return;
                }
            }
            Projectile.NewProjectile(spear.GetSource_FromThis(), target.Center, Vector2.Zero,
                ModContent.ProjectileType<NorthPoleSnowCoat>(), 0, 0, spear.owner, target.whoAmI, target.type);
        }

        // The icicle grows out of the enemy's back. It is ice magic, not a solid
        // body: walls and floors behind the enemy do not cut it off.
        public static void BraceSpike(Projectile brace, NPC target)
        {
            if (!Owned(brace) || target.friendly) return;
            Vector2 direction = brace.ai[1].ToRotationVector2();
            float extent = Math.Abs(direction.X) * target.width * 0.5f + Math.Abs(direction.Y) * target.height * 0.5f;
            Vector2 origin = target.Center + direction * (extent * 0.6f);
            float speed = MathHelper.Clamp(Main.player[brace.owner].velocity.Length() / NorthPoleBalance.SpikeFullSpeed, 0, 1);
            Projectile.NewProjectile(brace.GetSource_FromThis(), origin, Vector2.Zero,
                ModContent.ProjectileType<NorthPoleBraceSpike>(), brace.damage, brace.knockBack,
                brace.owner, brace.ai[1], speed, target.whoAmI);
        }

        public static void SnowDust(Vector2 point, int count, float spread = 3f)
        {
            if (Main.dedServ) return;
            for (int i = 0; i < count; i++)
            {
                Dust d = Dust.NewDustPerfect(point, i % 3 == 0 ? DustID.Snow : DustID.SnowflakeIce,
                    Main.rand.NextVector2Circular(spread, spread), 0, default, Main.rand.NextFloat(0.8f, 1.3f));
                d.noGravity = i % 3 != 0;
            }
        }
    }

    // Own procedural look for every North Pole effect. If the owner later draws
    // a texture, drop it into Textures/NorthPole/ under the name listed in
    // docs/уникальные.md and it replaces the procedural drawing automatically.
    internal static class NorthPoleArt
    {
        public const string Folder = "FrostMelee/Textures/NorthPole/";
        private static readonly Rectangle Px = new Rectangle(0, 0, 1, 1); // MagicPixel only with (0,0,1,1)
        private static readonly Dictionary<string, Asset<Texture2D>> cache = new();

        public static readonly Color Core = new Color(240, 252, 255);
        public static readonly Color Ice = new Color(160, 222, 255);
        public static readonly Color Deep = new Color(70, 150, 235);

        public static Color Additive(Color c, float alpha) => new Color(c.R, c.G, c.B, 0) * alpha;

        public static Texture2D Custom(string name)
        {
            if (Main.dedServ) return null;
            if (!cache.TryGetValue(name, out Asset<Texture2D> asset))
            {
                ModContent.RequestIfExists(Folder + name, out asset, AssetRequestMode.ImmediateLoad);
                cache[name] = asset;
            }
            return asset?.Value;
        }

        // Draws the drawn-by-owner texture, if there is one. Returns false to
        // fall back to the procedural look.
        public static bool DrawCustom(string name, Vector2 world, float rotation, float size, Color color)
        {
            Texture2D t = Custom(name);
            if (t == null) return false;
            float scale = size / Math.Max(t.Width, t.Height);
            Main.EntitySpriteDraw(t, world - Main.screenPosition, null, color, rotation, t.Size() / 2, scale, SpriteEffects.None, 0);
            return true;
        }

        public static void Line(Vector2 a, Vector2 b, float thickness, Color color)
        {
            Vector2 d = b - a;
            float length = d.Length();
            if (length < 0.5f) return;
            Main.EntitySpriteDraw(TextureAssets.MagicPixel.Value, a - Main.screenPosition, Px, color, d.ToRotation(),
                new Vector2(0, 0.5f), new Vector2(length, Math.Max(1, thickness)), SpriteEffects.None, 0);
        }

        // Square "pixel" on the 2-px grid, even size (art pixels are 2x2)
        public static void Dot(Vector2 at, float size, Color color, float rotation = 0f)
        {
            float s = Math.Max(2f, MathF.Round(size / 2f) * 2f);
            Main.EntitySpriteDraw(TextureAssets.MagicPixel.Value, Snap(at), Px, color, 0f,
                new Vector2(0.5f), new Vector2(s), SpriteEffects.None, 0);
        }

        // === Aurora style (owner 02.11.2026: "все снежинки и сосульки в стиле северного сияния") ===
        // Same palette as the swing arc: mint -> teal -> violet. The colour flows along each arm /
        // icicle over time (shimmer), every stroke has a dark night underlay so it reads on snow,
        // and a soft additive glow in the palette sits around it.
        public static readonly Color AMint = new Color(60, 255, 140);
        public static readonly Color ATeal = new Color(20, 200, 220);
        public static readonly Color AViolet = new Color(185, 90, 255);
        public static readonly Color ANight = new Color(4, 8, 30);

        // Each kind of snow has its own part of the aurora, so they never look alike
        // (owner 02.11.2026: buff snow and combat flakes, throw and thrust flakes must differ):
        public const int StyleSwing = 0;   // swing flake + icicles: mint -> teal -> violet (the arc)
        public const int StyleThrow = 1;   // flakes behind the thrown spear: white-cyan -> cyan -> deep teal, long tail
        public const int StyleBuff = 2;    // buff snow from thrusts: the warm rose part of the aurora, round and soft
        public static readonly Color TIce = new Color(215, 252, 255), TCyan = new Color(40, 215, 255), TDeep = new Color(15, 130, 190);
        public static readonly Color BPale = new Color(255, 220, 238), BRose = new Color(255, 110, 185), BMagenta = new Color(215, 55, 205);

        // t 0..1 along the shape; phase drifts with time so the colours flow (shimmer)
        public static Color AuroraAt(float t, float phase, int style = StyleSwing)
        {
            float s = MathHelper.Clamp(t + 0.22f * MathF.Sin(phase), 0f, 1f);
            Color a = AMint, b = ATeal, c = AViolet;
            if (style == StyleThrow) { a = TIce; b = TCyan; c = TDeep; }
            else if (style == StyleBuff) { a = BPale; b = BRose; c = BMagenta; }
            return s < 0.5f ? Color.Lerp(a, b, s * 2f) : Color.Lerp(b, c, (s - 0.5f) * 2f);
        }

        public static float Phase(Vector2 at) => (float)Main.GlobalTimeWrappedHourly * 2.4f + at.X * 0.013f + at.Y * 0.007f;

        // === Pixel art (owner 02.11.2026: "текстуры строго в террарии стиле 2х2 пикселя") ===
        // Sprites come from tools/make_north_pole_pixels.py: every art pixel is a 2x2 block, small
        // palettes per kind of snow, dark drop shadow. They are drawn at scale 1, snapped to the
        // 2-px grid, so nothing gets smoothed or blurred. Symmetric flakes are not rotated at all.
        public const string PixelFolder = "FrostMelee/Textures/NorthPolePixel/";
        public static readonly int[] SpikeWidths = { 4, 7, 10 };                     // art px
        public static readonly int[] SpikeLengths = { 8, 16, 24, 36, 48, 64, 80, 96 }; // art px
        private static readonly Dictionary<string, Asset<Texture2D>> pixelCache = new();

        public static Texture2D Pixel(string name)
        {
            if (Main.dedServ) return null;
            if (!pixelCache.TryGetValue(name, out Asset<Texture2D> asset))
            {
                ModContent.RequestIfExists(PixelFolder + name, out asset, AssetRequestMode.ImmediateLoad);
                pixelCache[name] = asset;
            }
            return asset?.Value;
        }

        // Screen position on the 2-px grid: art pixels stay 2x2 on screen
        public static Vector2 Snap(Vector2 world)
        {
            Vector2 p = world - Main.screenPosition;
            return new Vector2(MathF.Floor(p.X / 2f) * 2f, MathF.Floor(p.Y / 2f) * 2f);
        }

        private static Vector2 EvenHalf(Texture2D t) =>
            new Vector2(MathF.Floor(t.Width / 4f) * 2f, MathF.Floor(t.Height / 4f) * 2f);

        public static void Sprite(string name, Vector2 world, Color color, float rotation = 0f)
        {
            Texture2D t = Pixel(name);
            if (t == null) return;
            Main.EntitySpriteDraw(t, Snap(world), null, color, rotation, EvenHalf(t), 1f, SpriteEffects.None, 0);
        }

        // Snowflake by kind of snow: throw (cyan), buff (rose, round) or swing (mint -> violet,
        // size steps with the radius — bigger charge, bigger and more branched flake).
        public static void Snowflake(Vector2 center, float radius, float rotation, float detail, float alpha, int style = StyleSwing)
        {
            if (alpha <= 0.01f) return;
            string name = style == StyleThrow ? "ThrowFlake"
                : style == StyleBuff ? "BuffFlake"
                : "SwingFlake" + Math.Clamp((int)Math.Round(radius / 2f), 3, 11);
            Sprite(name, center, Color.White * alpha);
        }

        // Icicle from base to tip: the closest pre-drawn width/length, cropped from the tip while
        // it is shorter (it grows out tip first). fullLength — length it will reach (picks the sprite).
        public static void Icicle(Vector2 baseCenter, Vector2 tip, float width, float alpha, float fullLength = 0f)
        {
            Vector2 d = tip - baseCenter;
            float length = d.Length();
            if (length < 2 || alpha <= 0.01f) return;
            int wi = width < 11 ? 0 : width < 17 ? 1 : 2;              // screen px -> 4/7/10 art px
            float want = Math.Max(length, fullLength) / 2f;           // art px
            int li = SpikeLengths.Length - 1;
            for (int i = 0; i < SpikeLengths.Length; i++)
                if (SpikeLengths[i] >= want) { li = i; break; }
            Texture2D t = Pixel($"Spike{wi}_{SpikeLengths[li]}");
            if (t == null) return;
            int show = Math.Clamp((int)(length / 2f) * 2 + 4, 4, t.Height);   // even rows, +outline
            Rectangle src = new Rectangle(0, 0, t.Width, show);
            float rot = d.ToRotation() + MathHelper.PiOver2;              // sprite tip points up
            Main.EntitySpriteDraw(t, Snap(baseCenter), src, Color.White * alpha, rot,
                new Vector2(MathF.Floor(t.Width / 4f) * 2f, show), 1f, SpriteEffects.None, 0);
        }

        // Vanilla-style afterimages: the same sprite at old positions, fading
        public static void PixelTrail(Projectile p, string name, float alpha, float rotationOffset = 0f, bool rotate = false)
        {
            for (int i = p.oldPos.Length - 1; i >= 1; i--)
            {
                if (p.oldPos[i] == Vector2.Zero) continue;
                float fade = 1f - i / (float)p.oldPos.Length;
                Texture2D t = Pixel(name);
                if (t == null) return;
                Main.EntitySpriteDraw(t, Snap(p.oldPos[i] + p.Size / 2f), null, Color.White * (alpha * fade * 0.55f),
                    rotate ? p.oldRot[i] + rotationOffset : 0f, EvenHalf(t), 1f, SpriteEffects.None, 0);
            }
        }

        public static void Afterimages(Projectile p, float radius, float detail)
        {
            for (int i = p.oldPos.Length - 1; i >= 1; i--)
            {
                if (p.oldPos[i] == Vector2.Zero) continue;
                float fade = 1 - i / (float)p.oldPos.Length;
                Snowflake(p.oldPos[i] + p.Size / 2, radius * (0.6f + 0.4f * fade), p.oldRot[i], detail * 0.5f, 0.35f * fade);
            }
        }
    }

    // Actual vanilla North Pole snowflake AI and, like vanilla, no tile collision:
    // the trail snow falls through blocks.
    public class NorthPoleTrail : ModProjectile
    {
        public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.NorthPoleSnowflake;
        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 3;
            ProjectileID.Sets.TrailCacheLength[Type] = 12;   // visual only: long cyan tail tells throw snow apart
            ProjectileID.Sets.TrailingMode[Type] = 0;
        }
        public override void SetDefaults()
        {
            Projectile.CloneDefaults(ProjectileID.NorthPoleSnowflake);
            AIType = ProjectileID.NorthPoleSnowflake;
            Projectile.tileCollide = false;
            Projectile.DamageType = DamageClass.Melee;
        }
        public override bool PreDraw(ref Color lightColor)
        {
            float fade = Math.Min(1, Projectile.timeLeft / 20f) * Projectile.Opacity;
            NorthPoleArt.PixelTrail(Projectile, "ThrowFlake", fade);
            if (!NorthPoleArt.DrawCustom("TrailFlake", Projectile.Center, Projectile.rotation, 14, Color.White * fade))
                NorthPoleArt.Snowflake(Projectile.Center, 6.5f, Projectile.rotation, 0.3f + Projectile.ai[1] * 0.2f, fade, NorthPoleArt.StyleThrow);
            return false;
        }
    }

    // ai[0]: charging/flight, ai[1]: level, ai[2]: last struck NPC.
    public class NorthPoleSnowflake : ModProjectile
    {
        public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.NorthPoleSnowflake;
        public bool Charging => Projectile.ai[0] == 0;
        private int storedDamage, targetType, flightTicks;
        private bool impacted;
        private Vector2 lastTarget;
        private float pulse; // visual flash on each growth step, local only

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write(storedDamage);
            writer.Write(targetType);
            writer.Write(lastTarget.X);
            writer.Write(lastTarget.Y);
            writer.Write((ushort)flightTicks);
        }
        public override void ReceiveExtraAI(BinaryReader reader)
        {
            storedDamage = Math.Clamp(reader.ReadInt32(), 0, 1000000);
            targetType = reader.ReadInt32();
            float x = reader.ReadSingle(), y = reader.ReadSingle();
            lastTarget = float.IsFinite(x) && float.IsFinite(y) ? new Vector2(x, y) : Projectile.Center;
            flightTicks = Math.Clamp((int)reader.ReadUInt16(), 0, 180);
        }
        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 3;
            ProjectileID.Sets.TrailCacheLength[Type] = 8;
            ProjectileID.Sets.TrailingMode[Type] = 2;
        }
        public override void SetDefaults()
        {
            Projectile.width = Projectile.height = 24;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = 1;
            Projectile.tileCollide = false; // a snowflake: flies through blocks to its target
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 180;
            Projectile.netImportant = true;
        }
        public override bool ShouldUpdatePosition() => !Charging;
        public override bool? CanDamage() => Charging ? false : null;

        private float Level => MathHelper.Clamp(Projectile.ai[1] / NorthPoleBalance.FullCharge, 0, 1);

        public void Grow(NPC target, int damage)
        {
            if (!NorthPoleEffects.Owned(Projectile) || !Charging) return;
            Projectile.ai[1] = Math.Min(NorthPoleBalance.FullCharge, Projectile.ai[1] + 1);
            ResizeForCharge();
            Projectile.ai[2] = target.whoAmI;
            targetType = target.type;
            lastTarget = target.Center;
            storedDamage = Math.Max(1, damage);
            Projectile.netUpdate = true;
            pulse = 1;
            SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.35f, Pitch = -0.3f + Level * 0.8f }, Projectile.Center);
            NorthPoleEffects.SnowDust(Projectile.Center, 4 + (int)(Level * 6), 2f);
            if (Projectile.ai[1] >= NorthPoleBalance.FullCharge) Release(false);
        }

        private NPC LiveTarget()
        {
            int id = (int)Projectile.ai[2];
            NPC target = id >= 0 && id < Main.maxNPCs ? Main.npc[id] : null;
            return target != null && target.type == targetType && target.CanBeChasedBy(Projectile) ? target : null;
        }

        public void Release(bool weak)
        {
            if (!NorthPoleEffects.Owned(Projectile) || !Charging) return;
            NPC target = LiveTarget();
            if (target != null) lastTarget = target.Center;
            else
            {
                // The last target died: take the nearest enemy, walls don't matter.
                float best = 800 * 800;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    NPC n = Main.npc[i];
                    float distance = Vector2.DistanceSquared(n.Center, Projectile.Center);
                    if (n.CanBeChasedBy(Projectile) && distance < best)
                    { best = distance; lastTarget = n.Center; Projectile.ai[2] = i; targetType = n.type; }
                }
            }
            Projectile.damage = Math.Max(1, (int)(storedDamage * MathHelper.Lerp(0.4f, 1.5f, Level)
                * (weak ? NorthPoleBalance.WeakShot : 1)));
            Projectile.ai[0] = 1;
            Projectile.velocity = (lastTarget - Projectile.Center).SafeNormalize(new Vector2(Main.player[Projectile.owner].direction, 0))
                * NorthPoleBalance.ShotSpeed;
            flightTicks = 0;
            Projectile.netUpdate = true;
            SoundEngine.PlaySound(SoundID.Item28, Projectile.Center);
        }

        private void ResizeForCharge()
        {
            Projectile.scale = MathHelper.Lerp(0.6f, 1.8f, Level);
            Vector2 center = Projectile.Center;
            Projectile.width = Projectile.height = (int)(26 * Projectile.scale);
            Projectile.Center = center;
        }

        // Floats over the shoulder behind the player; snow is not stopped by tiles.
        private static Vector2 HoverPoint(Player player) => player.MountedCenter + new Vector2(-player.direction * 34,
            -64 * player.gravDir + MathF.Sin((float)Main.GameUpdateCount * 0.05f) * 5);

        public override void AI()
        {
            if (Projectile.owner < 0 || Projectile.owner >= Main.maxPlayers) { Projectile.Kill(); return; }
            Player player = Main.player[Projectile.owner];
            if (!player.active || player.dead) { Projectile.Kill(); return; }
            Projectile.timeLeft = 2; // observers don't expire on the charging->flight transition
            ResizeForCharge();
            pulse = Math.Max(0, pulse - 0.06f);
            if (Charging)
            {
                if (Projectile.owner == Main.myPlayer && player.HeldItem.type != ItemID.NorthPole) { Projectile.Kill(); return; }
                Projectile.Center = Vector2.Lerp(Projectile.Center, HoverPoint(player), 0.25f);
                Projectile.velocity = Vector2.Zero;
                Projectile.rotation += 0.02f + Level * 0.03f;
                NPC target = LiveTarget();
                if (target != null) lastTarget = target.Center;
                if (!Main.dedServ && Main.rand.NextFloat() < 0.1f + Level * 0.3f)
                {
                    Dust d = Dust.NewDustPerfect(Projectile.Center + Main.rand.NextVector2Circular(Projectile.width, Projectile.height) * 0.5f,
                        DustID.SnowflakeIce, new Vector2(0, 0.6f), 0, default, 0.7f);
                    d.noGravity = true;
                }
            }
            else
            {
                // Keeps homing on the target; without one it bursts where the target was.
                NPC target = LiveTarget();
                if (target != null) lastTarget = target.Center;
                Vector2 to = lastTarget - Projectile.Center;
                if (to.Length() < 16 && target == null) { impacted = true; Projectile.Kill(); return; }
                Projectile.velocity = Vector2.Lerp(Projectile.velocity,
                    to.SafeNormalize(Projectile.velocity) * NorthPoleBalance.ShotSpeed, 0.12f);
                Projectile.rotation += 0.25f;
                if (++flightTicks > 180) { Projectile.Kill(); return; } // lost in flight: melts, no burst
                if (!Main.dedServ && Main.rand.NextBool(2))
                {
                    Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.SnowflakeIce, -Projectile.velocity * 0.15f, 0, default, 1f);
                    d.noGravity = true;
                }
            }
            Lighting.AddLight(Projectile.Center, 0.15f + Level * 0.2f, 0.35f + Level * 0.25f, 0.45f + Level * 0.3f);
        }
        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) => impacted = true;
        public override void OnKill(int timeLeft)
        {
            NorthPoleEffects.SnowDust(Projectile.Center, 12 + (int)(Level * 14), 3 + Level * 3);
            if (!impacted || Charging) return;
            // разрыв: кольцо огоньков сияния и снежный выброс (на всех клиентах)
            for (int i = 0; i < 28; i++)
                NorthPoleFx.AuroraDust(Projectile.Center, (MathHelper.TwoPi * i / 28).ToRotationVector2() * (4f + Level * 3f), 1.4f);
            for (int i = 0; i < 10; i++)
                NorthPoleFx.ShakeSnow(Projectile.Center, 1f, 16f);
            if (!Main.dedServ) SoundEngine.PlaySound(SoundID.Item27, Projectile.Center);
            if (!NorthPoleEffects.Owned(Projectile)) return;
            for (int i = 0; i < NorthPoleBalance.Shards; i++)
            {
                Vector2 velocity = (Projectile.rotation + MathHelper.TwoPi * i / NorthPoleBalance.Shards).ToRotationVector2() * 7;
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), Projectile.Center, velocity,
                    ModContent.ProjectileType<NorthPoleShard>(), Math.Max(1, (int)(Projectile.damage * NorthPoleBalance.ShardDamage)),
                    Projectile.knockBack * 0.4f, Projectile.owner, Main.rand.Next(3));
            }
        }
        public override bool PreDraw(ref Color lightColor)
        {
            float radius = Projectile.width * 0.5f + 2;
            float alpha = Charging ? 0.85f + pulse * 0.15f : 1f;
            NorthPoleFx.Palette(out Color auroraCore, out Color auroraTail, out Color auroraEdge, Projectile.whoAmI);
            if (Charging)
            {
                float breathe = 0.5f + 0.5f * MathF.Sin((float)Main.GameUpdateCount * 0.08f);
                NorthPoleFx.Bloom(Projectile.Center, radius * (4.5f + Level * 2f), NorthPoleFx.Add(auroraEdge, 0.18f + 0.1f * breathe + pulse * 0.3f));
                NorthPoleFx.Bloom(Projectile.Center, radius * 2.6f, NorthPoleFx.Add(auroraTail, 0.3f + pulse * 0.4f));
                if (pulse > 0.05f)
                    NorthPoleFx.Ring(Projectile.Center, radius * (1.2f + (1 - pulse) * 1.6f), 6f, NorthPoleFx.Add(auroraTail, pulse * 0.7f));
            }
            else
            {
                NorthPoleArt.PixelTrail(Projectile, "SwingFlake" + Math.Clamp((int)Math.Round(radius / 2f), 3, 11), 1f);
                NorthPoleFx.Bloom(Projectile.Center, radius * 4f, NorthPoleFx.Add(auroraTail, 0.35f));
            }
            if (NorthPoleArt.DrawCustom("Snowflake", Projectile.Center, Projectile.rotation, radius * 2, Color.White * alpha))
                return false;
            // halo, flake, then one orbiting mote per charge step
            NorthPoleFx.Bloom(Projectile.Center, radius * (2.2f + pulse * 1.2f), NorthPoleFx.Add(NorthPoleArt.AViolet, 0.18f + pulse * 0.25f));
            NorthPoleArt.Snowflake(Projectile.Center, radius * (1 + pulse * 0.15f), Projectile.rotation, Level, alpha);
            if (Charging)
            {
                int steps = (int)Projectile.ai[1];
                for (int i = 0; i < steps; i++)
                {
                    float a = -Projectile.rotation * 1.6f + MathHelper.TwoPi * i / Math.Max(1, NorthPoleBalance.FullCharge);
                    NorthPoleArt.Dot(Projectile.Center + a.ToRotationVector2() * (radius + 7), 4, NorthPoleArt.Additive(NorthPoleArt.AuroraAt(i / (float)Math.Max(1, NorthPoleBalance.FullCharge), 0f), 0.9f));
                }
            }
            return false;
        }
    }

    // Icicles of the burst, like the Ice Queen's frost shards: they fly through blocks.
    public class NorthPoleShard : ModProjectile
    {
        public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.FrostShard;
        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 3;
            ProjectileID.Sets.TrailCacheLength[Type] = 5;
            ProjectileID.Sets.TrailingMode[Type] = 0;
        }
        public override void SetDefaults()
        {
            Projectile.width = Projectile.height = 12;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = 2;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 60;
        }
        public override void AI()
        {
            Projectile.frame = Math.Clamp((int)Projectile.ai[0], 0, 2);
            Projectile.velocity.Y += 0.2f;
            Projectile.rotation = Projectile.velocity.ToRotation();
            Projectile.Opacity = Math.Min(1, Projectile.timeLeft / 15f);
        }
        public override void OnKill(int timeLeft) => NorthPoleEffects.SnowDust(Projectile.Center, 3);
        public override bool PreDraw(ref Color lightColor)
        {
            Vector2 dir = Projectile.rotation.ToRotationVector2();
            if (NorthPoleArt.DrawCustom("Shard", Projectile.Center, Projectile.rotation, 20, Color.White * Projectile.Opacity))
                return false;
            NorthPoleArt.PixelTrail(Projectile, "Shard", Projectile.Opacity, 0f, true);
            if (NorthPoleArt.Pixel("Shard") is Texture2D shard)
                Main.EntitySpriteDraw(shard, NorthPoleArt.Snap(Projectile.Center), null, Color.White * Projectile.Opacity,
                    Projectile.rotation, new Vector2(MathF.Floor(shard.Width / 4f) * 2f, MathF.Floor(shard.Height / 4f) * 2f), 1f, SpriteEffects.None, 0);
            return false;
        }
    }

    // No damage; only the owning player can collect this projectile with their body.
    // ai[0]: age, ai[1]: look variant. Falls from the sky THROUGH blocks.
    public class NorthPolePickup : ModProjectile
    {
        // Not a hazard to dodge but a pickup: drifts down from the sky and, once
        // near its owner, is pulled to them like hearts and mana stars. The pull is
        // short and weak on purpose (owner 02.11.2026: "not so casual"): it only
        // finishes the catch, the player still has to run to the snowflake.
        public const float MagnetRange = 110f;
        public const float MagnetSpeed = 6.5f;
        public const float MagnetAccel = 0.25f;
        // Buff snow: the warm rose part of the aurora — unlike every combat flake (cold palettes)
        private static readonly Color Rose = NorthPoleArt.BRose;

        public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.NorthPoleSnowflake;
        public override void SetStaticDefaults() => Main.projFrames[Type] = 3;
        public override void SetDefaults()
        {
            Projectile.width = Projectile.height = 22;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.penetrate = -1;
            Projectile.timeLeft = NorthPoleBalance.PickupLife;
            Projectile.netImportant = true;
        }
        public override bool? CanDamage() => false;
        private bool Pulled => Projectile.localAI[0] > 0;
        public override void AI()
        {
            if (Projectile.owner < 0 || Projectile.owner >= Main.maxPlayers) { Projectile.Kill(); return; }
            Player player = Main.player[Projectile.owner];
            if (!player.active || player.dead) { Projectile.Kill(); return; }
            Projectile.ai[0]++;
            Vector2 to = player.Center - Projectile.Center;
            if (!Pulled && Projectile.ai[0] >= 10 && to.Length() < MagnetRange)
                Projectile.localAI[0] = 1;
            if (Pulled)
            {
                // like vanilla item grab: accelerate straight into the player
                float speed = Math.Min(MagnetSpeed, Projectile.velocity.Length() + MagnetAccel);
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, to.SafeNormalize(Vector2.UnitY) * speed, 0.12f);
                Projectile.timeLeft = Math.Max(Projectile.timeLeft, 30);
            }
            else
            {
                // gentle snowfall from the top of the screen, through roofs
                Projectile.velocity.Y = MathHelper.Lerp(Projectile.velocity.Y, 3f, 0.08f);
                Projectile.velocity.X = MathF.Sin((Projectile.ai[0] + Projectile.whoAmI * 13) * 0.05f) * 0.8f;
                if (Projectile.Center.Y - player.Center.Y > 360) { Projectile.Kill(); return; }
            }
            Projectile.rotation += 0.03f;
            Lighting.AddLight(Projectile.Center, 0.5f, 0.25f, 0.4f);
            if (NorthPoleEffects.Owned(Projectile) && Projectile.ai[0] >= 10 && Projectile.Hitbox.Intersects(player.Hitbox))
            {
                player.AddBuff(ModContent.BuffType<NorthPoleBlessing>(), NorthPoleBalance.BlessingTicks);
                if (Main.netMode == NetmodeID.MultiplayerClient)
                    NetMessage.SendData(MessageID.PlayerBuffs, number: player.whoAmI);
                SoundEngine.PlaySound(SoundID.Grab with { Pitch = 0.3f }, player.Center);
                SoundEngine.PlaySound(SoundID.Item4 with { Volume = 0.4f, Pitch = 0.6f }, player.Center);
                Projectile.Kill();
            }
        }
        public override void OnKill(int timeLeft)
        {
            if (Main.dedServ) return;
            for (int i = 0; i < 8; i++)
            {
                Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.PinkTorch, Main.rand.NextVector2Circular(2f, 2f), 0, default, 0.9f);
                d.noGravity = true;
            }
        }
        public override bool PreDraw(ref Color lightColor)
        {
            // Item-like: soft rose glow that pulses (no ring), light bobbing, round soft flake.
            float pulse = 0.5f + 0.5f * MathF.Sin(Projectile.ai[0] * 0.15f + Projectile.whoAmI);
            float fade = Math.Min(1, Projectile.timeLeft / 30f) * Math.Min(1, Projectile.ai[0] / 10f);
            Vector2 at = Projectile.Center + new Vector2(0, Pulled ? 0 : MathF.Sin(Projectile.ai[0] * 0.08f) * 2f);
            NorthPoleFx.Bloom(at, 50f + 14f * pulse, NorthPoleFx.Add(NorthPoleArt.BMagenta, (0.15f + 0.15f * pulse) * fade));
            NorthPoleFx.Bloom(at, 30f + 6f * pulse, NorthPoleFx.Add(Rose, (0.18f + 0.15f * pulse) * fade));
            if (NorthPoleArt.DrawCustom("Pickup", at, Projectile.rotation, 22, Color.White * fade))
                return false;
            NorthPoleArt.Snowflake(at, 12, Projectile.rotation, 0f, (0.85f + 0.15f * pulse) * fade, NorthPoleArt.StyleBuff);
            NorthPoleFx.Sparkle(at + new Vector2(7, -8), 12f * pulse, 0f, 2, NorthPoleFx.Add(Color.White, pulse * fade), NorthPoleFx.Add(Rose, pulse * fade));
            return false;
        }
    }

    // A visual attachment, not an extra damage hitbox or a second NPC buff loop.
    public class NorthPoleSnowCoat : ModProjectile
    {
        public override void SendExtraAI(BinaryWriter writer) => writer.Write((ushort)Projectile.timeLeft);
        public override void ReceiveExtraAI(BinaryReader reader) =>
            Projectile.timeLeft = Math.Clamp((int)reader.ReadUInt16(), 0, NorthPoleBalance.FrostbiteTicks);
        public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.NorthPoleSnowflake;
        public override void SetDefaults()
        {
            Projectile.width = Projectile.height = 2;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.penetrate = -1;
            Projectile.timeLeft = NorthPoleBalance.FrostbiteTicks;
            Projectile.netImportant = true;
        }
        public override bool ShouldUpdatePosition() => false;
        public override bool? CanDamage() => false;
        public override void AI()
        {
            int id = (int)Projectile.ai[0];
            if (id < 0 || id >= Main.maxNPCs || !Main.npc[id].active || Main.npc[id].life <= 0
                || Main.npc[id].type != (int)Projectile.ai[1]) { Projectile.Kill(); return; }
            NPC target = Main.npc[id];
            Projectile.Center = target.Center;
            if (!Main.dedServ && Main.rand.NextBool(4))
            {
                Vector2 rim = Main.rand.NextVector2Unit() * new Vector2(target.width, target.height) * 0.5f;
                Dust d = Dust.NewDustPerfect(target.Center + rim, DustID.Snow, new Vector2(0, 0.5f), 0, Color.White, 1f);
                d.noGravity = false;
            }
        }
        public override bool PreDraw(ref Color lightColor)
        {
            int id = (int)Projectile.ai[0];
            if (id < 0 || id >= Main.maxNPCs || !Main.npc[id].active) return false;
            NPC target = Main.npc[id];
            float fade = Math.Min(1, Projectile.timeLeft / 30f);
            Vector2 half = new Vector2(target.width, target.height) * 0.5f;
            float age = NorthPoleBalance.FrostbiteTicks - Projectile.timeLeft;
            float bulk = Math.Max(target.width, target.height);
            NorthPoleFx.Bloom(target.Center, bulk * 2.2f, NorthPoleFx.Add(new Color(150, 210, 255), (0.16f + 0.06f * MathF.Sin(age * 0.12f)) * fade));
            if (age < 16)
                NorthPoleFx.Ring(target.Center, bulk * (0.5f + age / 16f * 0.8f), 5f, NorthPoleFx.Add(Color.White, (1f - age / 16f) * 0.8f));
            if (NorthPoleArt.Custom("SnowCoat") is Texture2D custom)
            {
                Main.EntitySpriteDraw(custom, target.Center - Main.screenPosition, null, Color.White * fade, 0,
                    custom.Size() / 2, new Vector2(target.width / (float)custom.Width, target.height / (float)custom.Height) * 1.15f,
                    SpriteEffects.None, 0);
                return false;
            }
            // Snow stuck on the upper half (it is thrown snow, it sits on top),
            // lumps lit by the scene, frost glints on top.
            Color snow = Color.Lerp(lightColor, Color.White, 0.6f);
            for (int i = 0; i < 14; i++)
            {
                float a = MathHelper.Pi + MathHelper.Pi * (i + 0.5f) / 14 + (i % 2) * 0.12f;
                Vector2 rim = a.ToRotationVector2() * half * (0.78f + (i * 37 % 5) * 0.05f);
                float size = 4 + (i * 53 % 4);
                NorthPoleArt.Dot(target.Center + rim, size, snow * (0.9f * fade), 0);
                NorthPoleArt.Dot(target.Center + rim - new Vector2(0, size * 0.35f), size * 0.55f, Color.White * fade, 0);
            }
            float glint = MathF.Sin((float)Main.GameUpdateCount * 0.15f + id);
            if (glint > 0.6f)
                NorthPoleArt.Sprite("Glint", target.Center + new Vector2(half.X * 0.4f, -half.Y * 0.85f), Color.White * ((glint - 0.6f) * 2.5f * fade));
            return false;
        }
    }

    // ai[0]: direction; ai[1]: player speed snapshot [0..1]; ai[2]: first NPC (excluded).
    public class NorthPoleBraceSpike : ModProjectile
    {
        public override string Texture => "FrostMelee/Textures/Effects/IceSpike0";
        private float length;
        private int age;
        public override void SendExtraAI(BinaryWriter writer) => writer.Write(age);
        public override void ReceiveExtraAI(BinaryReader reader)
        {
            age = Math.Clamp(reader.ReadInt32(), 0, 42);
            Projectile.timeLeft = Math.Max(1, 42 - age);
        }
        private float Width => MathHelper.Lerp(8, 20, MathHelper.Clamp(Projectile.ai[1], 0, 1));
        private Vector2 Direction => Projectile.ai[0].ToRotationVector2();
        public override void SetDefaults()
        {
            Projectile.width = Projectile.height = 8;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
            Projectile.tileCollide = false; // non-moving; ice grows through blocks
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 42;
        }
        public override bool ShouldUpdatePosition() => false;
        public override void AI()
        {
            float t = MathHelper.Clamp(++age / 18f, 0, 1);
            float grow = 1 - (1 - t) * (1 - t);
            float previous = length;
            length = MathHelper.Lerp(NorthPoleBalance.SpikeMinLength, NorthPoleBalance.SpikeMaxLength,
                MathHelper.Clamp(Projectile.ai[1], 0, 1)) * grow;
            if (age == 1 && !Main.dedServ) SoundEngine.PlaySound(SoundID.Item30 with { Volume = 0.6f }, Projectile.Center);
            if (!Main.dedServ && length > previous)
                for (int i = 0; i < 2; i++)
                {
                    Dust d = Dust.NewDustPerfect(Projectile.Center + Direction * length, DustID.SnowflakeIce,
                        Direction.RotatedByRandom(0.8f) * Main.rand.NextFloat(1, 3), 0, default, 1.1f);
                    d.noGravity = true;
                }
            Lighting.AddLight(Projectile.Center + Direction * length * 0.5f, 0.2f, 0.4f, 0.6f);
        }
        public override bool? CanHitNPC(NPC target) => target.whoAmI == (int)Projectile.ai[2] ? false : null;
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            float point = 0;
            return length > 0 && Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                Projectile.Center, Projectile.Center + Direction * length, Width, ref point);
        }
        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) => target.AddBuff(BuffID.Frostburn2, NorthPoleBalance.FrostbiteTicks);
        public override bool PreDraw(ref Color lightColor)
        {
            if (length <= 0) return false;
            float fade = Math.Min(1, Projectile.timeLeft / 12f);
            if (NorthPoleArt.Custom("Spike") is Texture2D t)
            {
                Main.EntitySpriteDraw(t, Projectile.Center - Main.screenPosition, null, Color.White * fade,
                    Direction.ToRotation() + MathHelper.PiOver2, new Vector2(t.Width / 2f, t.Height),
                    new Vector2(Width / t.Width, length / t.Height), SpriteEffects.None, 0);
                return false;
            }
            Vector2 tip = Projectile.Center + Direction * length;
            NorthPoleFx.Palette(out Color core, out Color tail, out Color edge, Projectile.whoAmI);
            if (age < 14)
                NorthPoleFx.Ring(Projectile.Center, Width * (1f + age * 0.25f), 5f, NorthPoleFx.Add(tail, (1f - age / 14f) * 0.8f));
            NorthPoleFx.Stroke(Projectile.Center, tip, Width * 3f, NorthPoleFx.Add(edge, 0.25f * fade));
            NorthPoleFx.Stroke(Projectile.Center, tip, Width * 1.6f, NorthPoleFx.Add(tail, 0.35f * fade));
            NorthPoleArt.Icicle(Projectile.Center, tip, Width, fade,
                MathHelper.Lerp(NorthPoleBalance.SpikeMinLength, NorthPoleBalance.SpikeMaxLength, MathHelper.Clamp(Projectile.ai[1], 0, 1)));
            if (age < 24)
                NorthPoleFx.Sparkle(tip, 40f * (1f - age / 24f), Direction.ToRotation(), 2, NorthPoleFx.Add(tail, fade), NorthPoleFx.Add(core, fade));
            // two smaller side spikes break out of the base
            Vector2 side = Direction.RotatedBy(MathHelper.PiOver2);
            NorthPoleArt.Icicle(Projectile.Center + side * Width * 0.4f, Projectile.Center + Direction.RotatedBy(0.45f) * length * 0.35f, Width * 0.45f, fade);
            NorthPoleArt.Icicle(Projectile.Center - side * Width * 0.4f, Projectile.Center + Direction.RotatedBy(-0.45f) * length * 0.35f, Width * 0.45f, fade);
            return false;
        }
    }
    // North Pole's own effect style: "polar night" — aurora ribbons (mint/teal
    // shifting to violet) with snow shaken off them. Same techniques as the
    // other weapons: ThePerfectGlow strokes and additive A = 0 colours; the
    // flakes, icicles and sparkles themselves are 2x2 pixel sprites (NorthPoleArt).
    internal static class NorthPoleFx
    {
        public static readonly Color AuroraCore = new Color(225, 255, 245);
        private static readonly Color MintA = new Color(80, 240, 180), MintB = new Color(110, 200, 255);
        private static readonly Color VioletA = new Color(150, 85, 255), VioletB = new Color(70, 120, 255);

        // Slowly breathing aurora: the tail drifts teal<->sky, the edge violet<->blue
        public static void Palette(out Color core, out Color tail, out Color edge, float phase = 0f)
        {
            float s = 0.5f + 0.5f * MathF.Sin((float)Main.GlobalTimeWrappedHourly * 1.7f + phase);
            core = AuroraCore;
            tail = Color.Lerp(MintA, MintB, s);
            edge = Color.Lerp(VioletA, VioletB, s);
        }

        public static Texture2D Glow => Main.dedServ ? null : TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;

        public static Color Add(Color c, float alpha)
        {
            Color r = c * MathHelper.Clamp(alpha, 0f, 1f);
            r.A = 0;
            return r;
        }

        // Soft stroke a->b (world coordinates)
        public static void Stroke(Vector2 a, Vector2 b, float width, Color color)
        {
            Texture2D glow = Glow;
            if (glow == null || width < 0.5f) return;
            Vector2 d = b - a;
            Main.EntitySpriteDraw(glow, (a + b) * 0.5f - Main.screenPosition, null, color, d.ToRotation(),
                glow.Size() / 2f, new Vector2((d.Length() + width) / glow.Width, width / glow.Height), SpriteEffects.None, 0f);
        }

        public static void Bloom(Vector2 at, float size, Color color)
        {
            Texture2D glow = Glow;
            if (glow == null) return;
            Main.EntitySpriteDraw(glow, at - Main.screenPosition, null, color, 0f, glow.Size() / 2f, size / glow.Width, SpriteEffects.None, 0f);
        }

        // Star of n thin rays, like DrawSparkle of the arcs
        // Pixel 4-point star (2x2 art pixels), size picks one of three sprites; tinted by color
        public static void Sparkle(Vector2 at, float size, float rotation, int rays, Color color, Color center)
        {
            if (size < 4f) return;
            string name = size < 16f ? "Sparkle0" : size < 36f ? "Sparkle1" : "Sparkle2";
            NorthPoleArt.Sprite(name, at, color);
            NorthPoleArt.Dot(at, 2f, center);
        }

        // Expanding frost ring of soft strokes
        public static void Ring(Vector2 at, float radius, float width, Color color, int segments = 24)
        {
            Vector2 prev = at + new Vector2(radius, 0);
            for (int i = 1; i <= segments; i++)
            {
                Vector2 next = at + (MathHelper.TwoPi * i / segments).ToRotationVector2() * radius;
                Stroke(prev, next, width, color);
                prev = next;
            }
        }

        // Aurora curtain: faint vertical drapes hanging below a point
        public static void Curtain(Vector2 top, float height, float width, float alpha, float phase)
        {
            Palette(out _, out Color tail, out Color edge, phase);
            float sway = MathF.Sin((float)Main.GlobalTimeWrappedHourly * 2.3f + phase) * 4f;
            Stroke(top, top + new Vector2(sway, height * 0.55f), width, Add(tail, alpha));
            Stroke(top + new Vector2(sway, height * 0.45f), top + new Vector2(sway * 1.6f, height), width * 0.8f, Add(edge, alpha * 0.6f));
        }


        // Snow shaken off an effect: falls with gravity, unlike the floaty ice sparks of other weapons
        public static void ShakeSnow(Vector2 at, float chance, float spread = 6f)
        {
            if (Main.dedServ || Main.rand.NextFloat() > chance) return;
            Dust d = Dust.NewDustPerfect(at + Main.rand.NextVector2Circular(spread, spread), DustID.Snow,
                new Vector2(Main.rand.NextFloat(-0.4f, 0.4f), Main.rand.NextFloat(0.2f, 1.2f)), 0, default, Main.rand.NextFloat(0.7f, 1.1f));
            d.noGravity = false;
        }

        // Aurora-tinted sparkle dust that hangs in the air
        public static void AuroraDust(Vector2 at, Vector2 velocity, float scale = 1f)
        {
            if (Main.dedServ) return;
            Palette(out _, out Color tail, out Color edge, Main.rand.NextFloat(6f));
            Dust d = Dust.NewDustPerfect(at, DustID.RainbowMk2, velocity, 0, Main.rand.NextBool() ? tail : edge, scale * 0.6f);
            d.noGravity = true;
            d.fadeIn = 0.6f;
        }
    }
}
