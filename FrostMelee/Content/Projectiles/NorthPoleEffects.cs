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
            // on every client: the flying spear sheds snow and burns with frost fire, the same
            // vanilla Frostbite particles with our flame as on burning enemies (owner 02.11.2026:
            // the icy glows did not fit the throw)
            NorthPoleFx.ShakeSnow(spear.Center, 1f, 8f);
            if (Main.GameUpdateCount % 2 == 0)
                FrostJetFx.LooseBurnDust(spear.Center + Main.rand.NextVector2Circular(4f, 4f), -spear.velocity * 0.1f + new Vector2(0f, -0.6f), NorthPoleDrift.BurnGroup);
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

        // "Пурга" (owner 02.11.2026: the brace icicles did not work as a mechanic). Running into an
        // enemy with the braced spear lets a blizzard gust burst out from behind it along the spear:
        // a snow cone that blows the crowd behind the target away. Faster run = longer gust and
        // a harder push. One gust per half second per player, so nothing piles up.
        public static void BraceGust(Projectile brace, NPC target)
        {
            if (!Owned(brace) || target.friendly) return;
            NorthPolePlayer modPlayer = Main.player[brace.owner].GetModPlayer<NorthPolePlayer>();
            if (modPlayer.GustCooldown > 0) return;
            modPlayer.GustCooldown = NorthPoleBalance.GustCooldown;
            Vector2 direction = brace.ai[1].ToRotationVector2();
            float extent = Math.Abs(direction.X) * target.width * 0.5f + Math.Abs(direction.Y) * target.height * 0.5f;
            Vector2 origin = target.Center + direction * (extent * 0.6f);
            float speed = MathHelper.Clamp(Main.player[brace.owner].velocity.Length() / NorthPoleBalance.GustFullSpeed, 0, 1);
            Projectile.NewProjectile(brace.GetSource_FromThis(), origin, Vector2.Zero,
                ModContent.ProjectileType<NorthPoleGust>(), Math.Max(1, (int)(brace.damage * NorthPoleBalance.GustDamage)),
                MathHelper.Lerp(NorthPoleBalance.GustKnockMin, NorthPoleBalance.GustKnockMax, speed),
                brace.owner, brace.ai[1], speed, target.whoAmI);
        }

        // Weighty landing of the thrown spear (owner 02.11.2026): a dull crack, a small camera
        // punch along the flight and calm snow knocked off the spot.
        public static void ThrowImpact(Projectile spear, Vector2 at, Vector2 velocity)
        {
            if (Main.dedServ) return;
            SoundEngine.PlaySound(SoundID.Item50 with { Volume = 0.8f, Pitch = -0.4f }, at);
            SnowDust(at, 12, 3f);
            if (spear.owner == Main.myPlayer && velocity != Vector2.Zero)
                Main.instance.CameraModifiers.Add(new Terraria.Graphics.CameraModifiers.PunchCameraModifier(
                    at, velocity.SafeNormalize(Vector2.UnitX), 4f, 8f, 14, 1200f, "NorthPoleThrow"));
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
        public const int StyleDawn = 3;    // Polar Dawn snow from hits (attack speed + health): amber dawn, red heart
        public static readonly Color DGold = new Color(255, 214, 120), DAmber = new Color(248, 160, 40), DEmber = new Color(196, 96, 20);

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
        // Every art pixel is a 2x2 block, drawn at scale 1 snapped to the 2-px grid, so nothing gets
        // smoothed or blurred. Symmetric flakes are not rotated at all. The owner's hand-corrected
        // sprites are the reference (02.11.2026): opaque thin 1-px lines, no shadow, no outline.
        // Their canvas may be larger than the drawing, so sprites are centred on the opaque area.
        public const string PixelFolder = "FrostMelee/Textures/NorthPolePixel/";
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

        private static Vector2 EvenHalf(Rectangle r) =>
            new Vector2(MathF.Floor(r.Width / 4f) * 2f, MathF.Floor(r.Height / 4f) * 2f);

        private static readonly Dictionary<Texture2D, Rectangle> boundsCache = new();
        // Opaque area of a sprite (once per texture): the owner's editor may leave extra canvas
        public static Rectangle Bounds(Texture2D t)
        {
            if (boundsCache.TryGetValue(t, out Rectangle r)) return r;
            Color[] data = new Color[t.Width * t.Height];
            t.GetData(data);
            int x0 = t.Width, y0 = t.Height, x1 = -1, y1 = -1;
            for (int y = 0; y < t.Height; y++)
                for (int x = 0; x < t.Width; x++)
                    if (data[y * t.Width + x].A > 0)
                    { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
            r = x1 < 0 ? new Rectangle(0, 0, t.Width, t.Height) : new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
            boundsCache[t] = r;
            return r;
        }

        public static void Sprite(string name, Vector2 world, Color color, float rotation = 0f)
        {
            Texture2D t = Pixel(name);
            if (t == null) return;
            Rectangle src = Bounds(t);
            Main.EntitySpriteDraw(t, Snap(world), src, color, rotation, EvenHalf(src), 1f, SpriteEffects.None, 0);
        }

        // Snowflake by kind of snow: throw (cyan), buff (rose, round) or swing (mint -> violet,
        // size steps with the radius — bigger charge, bigger and more branched flake).
        // Slow spin of a snowflake around its own axis (owner 03.11.2026): every flake has its own
        // direction and speed (from the synced identity), and the bigger it is the faster it turns.
        // size01: 0 = smallest flake, 1 = biggest. Radians per tick.
        public static float Spin(Projectile p, float size01)
        {
            uint h = (uint)p.identity * 2654435761u;
            float sign = (h & 0x10000) == 0 ? 1f : -1f;
            float jitter = 0.75f + 0.5f * ((h >> 20) % 1000) / 1000f;
            return sign * jitter * MathHelper.Lerp(0.006f, 0.04f, MathHelper.Clamp(size01, 0f, 1f));
        }
        public static void Snowflake(Vector2 center, float radius, float rotation, float detail, float alpha, int style = StyleSwing)
        {
            if (alpha <= 0.01f) return;
            string name = style == StyleThrow ? "ThrowFlake"
                : style == StyleBuff ? "BuffFlake"
                : style == StyleDawn ? "DawnFlake"
                : "SwingFlake" + Math.Clamp((int)Math.Round(radius / 2f), 3, 11);
            Sprite(name, center, Color.White * alpha, rotation);
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
                Rectangle src = Bounds(t);
                Main.EntitySpriteDraw(t, Snap(p.oldPos[i] + p.Size / 2f), src, Color.White * (alpha * fade * 0.55f),
                    rotate ? p.oldRot[i] + rotationOffset : 0f, EvenHalf(src), 1f, SpriteEffects.None, 0);
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
            ProjectileID.Sets.TrailingMode[Type] = 2;
        }
        public override void SetDefaults()
        {
            Projectile.CloneDefaults(ProjectileID.NorthPoleSnowflake);
            AIType = ProjectileID.NorthPoleSnowflake;
            Projectile.tileCollide = false;
            Projectile.DamageType = DamageClass.Melee;
        }
        private int driftTicks;
        private float spin;
        // a longer trail that gives Frostbite (owner 02.11.2026): snow hangs where the flake passed
        public override void PostAI()
        {
            if (++driftTicks % 3 == 0) NorthPoleDrift.Add(Projectile.Center, Projectile.owner);
            spin += NorthPoleArt.Spin(Projectile, 0.2f);
            Projectile.rotation = spin;
        }
        public override bool PreDraw(ref Color lightColor)
        {
            float fade = Math.Min(1, Projectile.timeLeft / 20f) * Projectile.Opacity;
            NorthPoleArt.PixelTrail(Projectile, "ThrowFlake", fade, 0f, true);
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
        private int slot;     // hover place among the five growing flakes
        public int Slot { get => slot; set { slot = Math.Clamp(value, 0, NorthPoleBalance.MaxFlakes - 1); Projectile.netUpdate = true; } }
        public bool Targets(NPC target) => (int)Projectile.ai[2] == target.whoAmI && targetType == target.type;

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write((byte)slot);
            writer.Write(storedDamage);
            writer.Write(targetType);
            writer.Write(lastTarget.X);
            writer.Write(lastTarget.Y);
            writer.Write((ushort)flightTicks);
            writer.Write((byte)Math.Clamp(launchDelay, 0, 255));
        }
        public override void ReceiveExtraAI(BinaryReader reader)
        {
            slot = Math.Clamp((int)reader.ReadByte(), 0, NorthPoleBalance.MaxFlakes - 1);
            storedDamage = Math.Clamp(reader.ReadInt32(), 0, 1000000);
            targetType = reader.ReadInt32();
            float x = reader.ReadSingle(), y = reader.ReadSingle();
            lastTarget = float.IsFinite(x) && float.IsFinite(y) ? new Vector2(x, y) : Projectile.Center;
            flightTicks = Math.Clamp((int)reader.ReadUInt16(), 0, 180);
            launchDelay = reader.ReadByte();
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
        public override bool ShouldUpdatePosition() => !Charging && launchDelay <= 0;
        public override bool? CanDamage() => Charging || launchDelay > 0 ? false : null;

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

        // Each flake flies at the enemy nearest to IT (owner 02.11.2026), walls don't matter.
        private bool PickNearest()
        {
            float best = 800 * 800;
            bool found = false;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC n = Main.npc[i];
                float distance = Vector2.DistanceSquared(n.Center, Projectile.Center);
                if (n.CanBeChasedBy(Projectile) && distance < best)
                { best = distance; lastTarget = n.Center; Projectile.ai[2] = i; targetType = n.type; found = true; }
            }
            return found;
        }

        // Released flakes leave one by one, 0.1-0.4 s apart (owner 02.11.2026): the owner's
        // NorthPolePlayer hands out the launch delays, the flake hovers in its place until then.
        private int launchDelay;
        public bool Waiting => !Charging && launchDelay > 0;

        public void Release(bool weak)
        {
            if (!NorthPoleEffects.Owned(Projectile) || !Charging) return;
            Projectile.damage = Math.Max(1, (int)(storedDamage * MathHelper.Lerp(0.4f, 1.5f, Level)
                * (weak ? NorthPoleBalance.WeakShot : 1)));
            Projectile.ai[0] = 1;
            flightTicks = 0;
            launchDelay = Main.player[Projectile.owner].GetModPlayer<NorthPolePlayer>().NextLaunchDelay();
            Projectile.netUpdate = true;
            if (launchDelay <= 0) Launch();
        }

        private void Launch()
        {
            launchDelay = 0;
            bool found = PickNearest();
            Projectile.netUpdate = true;
            if (!found && LiveTarget() == null)
            {
                // nobody to fly at (e.g. the spear was put away in an empty room): burst right here
                impacted = true;
                Projectile.Kill();
                return;
            }
            if (!found) lastTarget = LiveTarget().Center;
            Projectile.velocity = (lastTarget - Projectile.Center).SafeNormalize(new Vector2(Main.player[Projectile.owner].direction, 0))
                * NorthPoleBalance.ShotSpeed;
            SoundEngine.PlaySound(SoundID.Item28, Projectile.Center);
        }

        private void ResizeForCharge()
        {
            Projectile.scale = MathHelper.Lerp(0.6f, 1.8f, Level);
            Vector2 center = Projectile.Center;
            Projectile.width = Projectile.height = (int)(26 * Projectile.scale);
            Projectile.Center = center;
        }

        // Up to five flakes float in a fan above and behind the player, one place each;
        // snow is not stopped by tiles. Angles from straight up, towards the back.
        private static readonly float[] SlotAngles = { 0.55f, 0f, 1.1f, -0.55f, 1.65f };
        private static Vector2 HoverPoint(Player player, int slot)
        {
            float angle = SlotAngles[Math.Clamp(slot, 0, SlotAngles.Length - 1)];
            float bob = MathF.Sin((float)Main.GameUpdateCount * 0.05f + slot * 1.3f) * 5;
            Vector2 offset = new Vector2(-player.direction * MathF.Sin(angle) * 72, -MathF.Cos(angle) * 72 * player.gravDir + bob);
            return player.MountedCenter + offset;
        }

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
                // spear put away: the flakes are not lost, they fly off and burst (owner 02.11.2026)
                if (Projectile.owner == Main.myPlayer && player.HeldItem.type != ItemID.NorthPole) { Release(true); return; }
                Projectile.Center = Vector2.Lerp(Projectile.Center, HoverPoint(player, slot), 0.25f);
                Projectile.velocity = Vector2.Zero;
                Projectile.rotation += NorthPoleArt.Spin(Projectile, Level);
                NPC target = LiveTarget();
                if (target != null) lastTarget = target.Center;
                // Root cause of "flakes stop growing mid-fight": a flake belongs to one enemy and
                // grows only from hits on it. When that enemy died, the flake hung forever: hits on
                // others never grew it, and as hits kept coming the misses never released it; with
                // all five places taken by such orphans nothing grew at all. Now it flies off at once.
                else if (Projectile.owner == Main.myPlayer && Projectile.ai[1] > 0) { Release(false); return; }
                if (!Main.dedServ && Main.rand.NextFloat() < 0.1f + Level * 0.3f)
                {
                    Dust d = Dust.NewDustPerfect(Projectile.Center + Main.rand.NextVector2Circular(Projectile.width, Projectile.height) * 0.5f,
                        DustID.SnowflakeIce, new Vector2(0, 0.6f), 0, default, 0.7f);
                    d.noGravity = true;
                }
            }
            else
            {
                if (launchDelay > 0)
                {
                    // waits for its turn in its place; the spear may already be put away
                    Projectile.Center = Vector2.Lerp(Projectile.Center, HoverPoint(player, slot), 0.25f);
                    Projectile.velocity = Vector2.Zero;
                    Projectile.rotation += NorthPoleArt.Spin(Projectile, Level);
                    if (--launchDelay <= 0 && Projectile.owner == Main.myPlayer) Launch();
                    return;
                }
                // Keeps homing on the target; if it died, takes the enemy nearest to the flake;
                // with nobody left it bursts where the target was.
                NPC target = LiveTarget();
                if (target == null && Projectile.owner == Main.myPlayer && PickNearest())
                { target = LiveTarget(); Projectile.netUpdate = true; }
                if (target != null) lastTarget = target.Center;
                Vector2 to = lastTarget - Projectile.Center;
                if (to.Length() < 16 && target == null) { impacted = true; Projectile.Kill(); return; }
                Projectile.velocity = Vector2.Lerp(Projectile.velocity,
                    to.SafeNormalize(Projectile.velocity) * NorthPoleBalance.ShotSpeed, 0.12f);
                Projectile.rotation += NorthPoleArt.Spin(Projectile, Level);
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
            // owner 02.11.2026: no smooth particle rings ("Noita"), a few chunky vanilla flakes
            for (int i = 0; i < 8; i++)
                NorthPoleFx.AuroraDust(Projectile.Center, (MathHelper.TwoPi * i / 8 + Main.rand.NextFloat(-0.3f, 0.3f)).ToRotationVector2() * (3f + Level * 2f), 1.4f);
            for (int i = 0; i < 6; i++)
                NorthPoleFx.ShakeSnow(Projectile.Center, 1f, 16f);
            if (!Main.dedServ) SoundEngine.PlaySound(SoundID.Item27, Projectile.Center);
            if (!NorthPoleEffects.Owned(Projectile)) return;
            // the big flake breaks into small snowflakes that scatter and explode (owner 02.11.2026)
            for (int i = 0; i < NorthPoleBalance.MiniFlakes; i++)
            {
                Vector2 velocity = (Projectile.rotation + MathHelper.TwoPi * i / NorthPoleBalance.MiniFlakes).ToRotationVector2() * 6.5f;
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), Projectile.Center, velocity,
                    ModContent.ProjectileType<NorthPoleMiniFlake>(), Math.Max(1, (int)(Projectile.damage * NorthPoleBalance.MiniFlakeDamage)),
                    Projectile.knockBack * 0.5f, Projectile.owner, Main.rand.Next(16, 26));
            }
        }
        public override bool PreDraw(ref Color lightColor)
        {
            // No soft glow around the flake: the diamond-shaped ThePerfectGlow halo looked uneven
            // (owner 02.11.2026). The flake is lit by real light (AI) and pixel snow bursts instead.
            float radius = Projectile.width * 0.5f + 2;
            float alpha = 1f;
            if (!Charging)
                NorthPoleArt.PixelTrail(Projectile, "SwingFlake" + Math.Clamp((int)Math.Round(radius / 2f), 3, 11), 1f, 0f, true);
            if (NorthPoleArt.DrawCustom("Snowflake", Projectile.Center, Projectile.rotation, radius * 2, Color.White * alpha))
                return false;
            // flake, then one orbiting mote per charge step
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

    // Small snowflakes of the burst: scatter, slow down and explode in a small
    // blast (on contact with an enemy or when their fuse ends). Through blocks.
    // ai[0]: fuse ticks; localAI[0]: 1 once exploding.
    public class NorthPoleMiniFlake : ModProjectile
    {
        private const int BlastTicks = 3;
        public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.NorthPoleSnowflake;
        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 3;
            ProjectileID.Sets.TrailCacheLength[Type] = 5;
            ProjectileID.Sets.TrailingMode[Type] = 2;
        }
        public override void SetDefaults()
        {
            Projectile.width = Projectile.height = 14;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1; // each enemy is hit once: contact and blast together
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 40;
        }
        private bool Exploding => Projectile.localAI[0] > 0;
        public override void AI()
        {
            if (!Exploding)
            {
                Projectile.velocity *= 0.94f;
                Projectile.rotation += NorthPoleArt.Spin(Projectile, 0f);
                if (++Projectile.localAI[1] >= Math.Clamp(Projectile.ai[0], 8, 36)) Explode();
                if (!Main.dedServ && Main.rand.NextBool(3))
                {
                    Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.SnowflakeIce, -Projectile.velocity * 0.1f, 0, default, 0.7f);
                    d.noGravity = true;
                }
            }
            Lighting.AddLight(Projectile.Center, 0.2f, 0.45f, 0.5f);
        }
        private void Explode()
        {
            if (Exploding) return;
            Projectile.localAI[0] = 1;
            Projectile.velocity = Vector2.Zero;
            Projectile.timeLeft = BlastTicks;
            Projectile.Resize(NorthPoleBalance.MiniBlastRadius * 2, NorthPoleBalance.MiniBlastRadius * 2);
            if (Main.dedServ) return;
            SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.45f, Pitch = 0.4f }, Projectile.Center);
            NorthPoleEffects.SnowDust(Projectile.Center, 6, 4f);
        }
        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) => Explode();
        public override bool PreDraw(ref Color lightColor)
        {
            if (Exploding) return false; // the blast is drawn by pixel particles
            if (NorthPoleArt.DrawCustom("MiniFlake", Projectile.Center, Projectile.rotation, 16, Color.White))
                return false;
            NorthPoleArt.PixelTrail(Projectile, "MiniFlake", 0.6f, 0f, true);
            NorthPoleArt.Sprite("MiniFlake", Projectile.Center, Color.White, Projectile.rotation);
            return false;
        }
    }

    // No damage; only the owning player can collect this projectile with their body.
    // ai[0]: age, ai[1]: look variant, ai[2]: kind (0 Blessing — rose, 1 Polar Dawn — amber).
    // Falls from the sky THROUGH blocks.
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
        private bool Dawn => (int)Projectile.ai[2] == NorthPolePlayer.KindDawn;
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
            Projectile.rotation += NorthPoleArt.Spin(Projectile, 0.45f);
            if (Dawn) Lighting.AddLight(Projectile.Center, 0.55f, 0.38f, 0.12f);
            else Lighting.AddLight(Projectile.Center, 0.5f, 0.25f, 0.4f);
            if (NorthPoleEffects.Owned(Projectile) && Projectile.ai[0] >= 10 && Projectile.Hitbox.Intersects(player.Hitbox))
            {
                player.AddBuff(Dawn ? ModContent.BuffType<NorthPoleDawn>() : ModContent.BuffType<NorthPoleBlessing>(), NorthPoleBalance.BlessingTicks);
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
                Dust d = Dust.NewDustPerfect(Projectile.Center, Dawn ? DustID.OrangeTorch : DustID.PinkTorch, Main.rand.NextVector2Circular(2f, 2f), 0, default, 0.9f);
                d.noGravity = true;
            }
        }
        public override bool PreDraw(ref Color lightColor)
        {
            // Item-like: soft rose glow that pulses (no ring), light bobbing, round soft flake.
            float pulse = 0.5f + 0.5f * MathF.Sin(Projectile.ai[0] * 0.15f + Projectile.whoAmI);
            float fade = Math.Min(1, Projectile.timeLeft / 30f) * Math.Min(1, Projectile.ai[0] / 10f);
            Vector2 at = Projectile.Center + new Vector2(0, Pulled ? 0 : MathF.Sin(Projectile.ai[0] * 0.08f) * 2f);
            Color inner = Dawn ? NorthPoleArt.DAmber : Rose;
            if (NorthPoleArt.DrawCustom(Dawn ? "DawnPickup" : "Pickup", at, Projectile.rotation, 22, Color.White * fade))
                return false;
            NorthPoleArt.Snowflake(at, 12, Projectile.rotation, 0f, (0.85f + 0.15f * pulse) * fade, Dawn ? NorthPoleArt.StyleDawn : NorthPoleArt.StyleBuff);
            NorthPoleFx.Sparkle(at + new Vector2(7, -8), 12f * pulse, 0f, 2, NorthPoleFx.Add(Color.White, pulse * fade), NorthPoleFx.Add(inner, pulse * fade));
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

    // Blizzard gust of the braced spear. ai[0]: direction; ai[1]: player speed [0..1];
    // ai[2]: the braced enemy (it already took the spear, the gust blows past it).
    // A cone of wind that reaches out over GrowTicks, hits each enemy once and pushes it along
    // the spear. Drawn only with vanilla snow dust blown along the cone (owner: no smooth stream).
    public class NorthPoleGust : ModProjectile
    {
        public const int Life = 16, GrowTicks = 8, StreamTicks = 10;
        public override string Texture => "FrostMelee/Textures/Effects/IceSpike0";
        private int age;
        public override void SendExtraAI(BinaryWriter writer) => writer.Write(age);
        public override void ReceiveExtraAI(BinaryReader reader)
        {
            age = Math.Clamp(reader.ReadInt32(), 0, Life);
            Projectile.timeLeft = Math.Max(1, Life - age);
        }
        private Vector2 Direction => Projectile.ai[0].ToRotationVector2();
        private float Power => MathHelper.Clamp(Projectile.ai[1], 0, 1);
        private float FullLength => MathHelper.Lerp(NorthPoleBalance.GustMinLength, NorthPoleBalance.GustMaxLength, Power);
        private float Reach => FullLength * (1 - MathF.Pow(1 - MathHelper.Clamp(age / (float)GrowTicks, 0, 1), 2));
        public override void SetDefaults()
        {
            Projectile.width = Projectile.height = 8;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
            Projectile.tileCollide = false; // wind and snow blow over blocks
            Projectile.ignoreWater = true;
            Projectile.timeLeft = Life;
        }
        public override bool ShouldUpdatePosition() => false;
        public override void AI()
        {
            age++;
            Vector2 dir = Direction;
            if (!Main.dedServ)
            {
                if (age == 1)
                {
                    SoundEngine.PlaySound(SoundID.Item28 with { Volume = 0.55f, Pitch = -0.35f }, Projectile.Center);
                    NorthPoleEffects.SnowDust(Projectile.Center, 6, 3f);
                    // no camera punch here (owner 03.11.2026: no screen shake on the brace hit)
                }
                // Owner 02.11.2026: the smooth pixel stream read as "Noita". Plain vanilla snow dust
                // instead, like the Blizzard Staff: few chunky flakes in short puffs, vanilla motion.
                if (age <= StreamTicks && age % 2 == 1)
                {
                    int count = 2 + (int)(3 * Power);
                    for (int i = 0; i < count; i++)
                    {
                        float spread = Main.rand.NextFloat(-1f, 1f) * NorthPoleBalance.GustHalfAngle;
                        Vector2 v = dir.RotatedBy(spread) * FullLength / 14f * Main.rand.NextFloat(0.6f, 1f);
                        bool heavy = i % 3 == 0;
                        Dust d = Dust.NewDustPerfect(Projectile.Center, heavy ? DustID.Snow : DustID.SnowflakeIce, v, 0, default,
                            Main.rand.NextFloat(1.1f, 1.5f));
                        d.noGravity = !heavy;
                    }
                }
            }
            float glow = 1f - age / (float)Life;
            for (int k = 1; k <= 3; k++)
                Lighting.AddLight(Projectile.Center + dir * Reach * k / 3f, 0.25f * glow, 0.45f * glow, 0.55f * glow);
        }
        public override bool? CanHitNPC(NPC target) => target.whoAmI == (int)Projectile.ai[2] ? false : null;
        // Cone: the enemy's hitbox is within reach of the gust's mouth and inside the opening angle
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            float reach = Reach;
            if (reach <= 0) return false;
            Vector2 origin = Projectile.Center;
            Vector2 nearest = Vector2.Clamp(origin, targetHitbox.TopLeft(), targetHitbox.BottomRight());
            Vector2 to = targetHitbox.Center.ToVector2() - origin;
            float dist = to.Length();
            if (Vector2.Distance(origin, nearest) > reach) return false;
            if (dist < 1f) return true;
            float margin = MathF.Atan2(Math.Max(targetHitbox.Width, targetHitbox.Height) * 0.5f, dist);
            float off = Math.Abs(MathHelper.WrapAngle(to.ToRotation() - Projectile.ai[0]));
            return off <= NorthPoleBalance.GustHalfAngle + margin;
        }
        // the push always goes along the spear, not away from the gust's centre
        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            float dx = Direction.X;
            modifiers.HitDirectionOverride = Math.Abs(dx) < 0.1f ? (target.Center.X >= Projectile.Center.X ? 1 : -1) : Math.Sign(dx);
        }
        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            target.AddBuff(BuffID.Frostburn2, NorthPoleBalance.FrostbiteTicks);
            if (!Main.dedServ)
                NorthPoleEffects.SnowDust(target.Center, 4, 2f);
        }
        public override bool PreDraw(ref Color lightColor)
        {
            // owner texture: drawn along the cone, mouth at the bottom
            if (NorthPoleArt.Custom("Gust") is Texture2D t && Reach > 0)
            {
                float fade = Math.Min(1, Projectile.timeLeft / 6f);
                float width = 2f * Reach * MathF.Tan(NorthPoleBalance.GustHalfAngle);
                Main.EntitySpriteDraw(t, Projectile.Center - Main.screenPosition, null, Color.White * fade,
                    Projectile.ai[0] + MathHelper.PiOver2, new Vector2(t.Width / 2f, t.Height),
                    new Vector2(width / t.Width, Reach / t.Height), SpriteEffects.None, 0);
            }
            return false; // otherwise the pixel snow is the whole look
        }
    }
    // North Pole's own effect style: "polar night" — aurora ribbons (mint/teal
    // shifting to violet) with snow shaken off them. Same techniques as the
    // other weapons: ThePerfectGlow strokes and additive A = 0 colours; the
    // flakes and sparkles themselves are 2x2 pixel sprites (NorthPoleArt).
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
