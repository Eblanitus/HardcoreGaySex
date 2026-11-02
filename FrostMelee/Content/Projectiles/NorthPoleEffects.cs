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

        public static void Dot(Vector2 at, float size, Color color, float rotation = MathHelper.PiOver4) =>
            Main.EntitySpriteDraw(TextureAssets.MagicPixel.Value, at - Main.screenPosition, Px, color, rotation,
                new Vector2(0.5f), new Vector2(Math.Max(1, size)), SpriteEffects.None, 0);

        // Six-armed snowflake. detail 0..1 adds side branches; that is how the
        // growing flake shows its charge level.
        public static void Snowflake(Vector2 center, float radius, float rotation, float detail, float alpha)
        {
            float thick = Math.Max(1f, radius * 0.09f);
            // tiny flakes turn to mush with branches; big ones get up to three pairs
            int branches = radius < 8 ? 0 : radius < 12 ? 1 : 1 + (int)Math.Round(MathHelper.Clamp(detail, 0, 1) * 2);
            for (int pass = 0; pass < 2; pass++)
            {
                bool glow = pass == 0;
                Color color = glow ? Additive(Ice, 0.3f * alpha) : Core * alpha;
                float w = glow ? thick * 2f : thick;
                for (int i = 0; i < 6; i++)
                {
                    Vector2 dir = (rotation + MathHelper.TwoPi * i / 6).ToRotationVector2();
                    Vector2 tip = center + dir * radius;
                    Line(center, tip, w, color);
                    for (int b = 0; b < branches; b++)
                    {
                        float t = 0.45f + b * 0.18f;
                        Vector2 at = center + dir * radius * t;
                        float len = radius * (0.3f - b * 0.07f);
                        Line(at, at + dir.RotatedBy(1.05f) * len, w * 0.75f, color);
                        Line(at, at + dir.RotatedBy(-1.05f) * len, w * 0.75f, color);
                    }
                    if (!glow) Dot(tip, thick * 1.4f, Additive(Core, alpha));
                }
            }
            Dot(center, thick * 2.2f, Additive(Ice, 0.5f * alpha));
            Dot(center, thick * 1.2f, Core * alpha);
        }

        // Tapered icicle from base to tip, with a lit edge and a cold core.
        public static void Icicle(Vector2 baseCenter, Vector2 tip, float width, float alpha)
        {
            Vector2 d = tip - baseCenter;
            float length = d.Length();
            if (length < 1) return;
            Vector2 dir = d / length, side = dir.RotatedBy(MathHelper.PiOver2);
            const int steps = 10;
            for (int k = 0; k < steps; k++)
            {
                float t0 = k / (float)steps, t1 = (k + 1) / (float)steps;
                float w = width * (1 - t0 * 0.92f);
                Vector2 a = baseCenter + dir * length * t0, b = baseCenter + dir * length * t1;
                Line(a, b, w * 1.7f, Additive(Ice, 0.22f * alpha));
                Line(a, b, w, Color.Lerp(Deep, Ice, t0) * (0.9f * alpha));
                Line(a + side * w * 0.22f, b + side * w * 0.22f, Math.Max(1, w * 0.28f), Core * alpha);
            }
            Dot(tip, Math.Max(2, width * 0.3f), Additive(Core, alpha));
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
        public override void SetStaticDefaults() => Main.projFrames[Type] = 3;
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
            if (!NorthPoleArt.DrawCustom("TrailFlake", Projectile.Center, Projectile.rotation, 14, Color.White * fade))
                NorthPoleArt.Snowflake(Projectile.Center, 6.5f, Projectile.rotation, 0.3f + Projectile.ai[1] * 0.2f, fade);
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
            if (!Charging) NorthPoleArt.Afterimages(Projectile, radius, Level);
            if (NorthPoleArt.DrawCustom("Snowflake", Projectile.Center, Projectile.rotation, radius * 2, Color.White * alpha))
                return false;
            // halo, flake, then one orbiting mote per charge step
            NorthPoleArt.Dot(Projectile.Center, radius * (1.1f + pulse * 0.6f), NorthPoleArt.Additive(NorthPoleArt.Deep, 0.18f + pulse * 0.25f), 0);
            NorthPoleArt.Snowflake(Projectile.Center, radius * (1 + pulse * 0.15f), Projectile.rotation, Level, alpha);
            if (Charging)
            {
                int steps = (int)Projectile.ai[1];
                for (int i = 0; i < steps; i++)
                {
                    float a = -Projectile.rotation * 1.6f + MathHelper.TwoPi * i / Math.Max(1, NorthPoleBalance.FullCharge);
                    NorthPoleArt.Dot(Projectile.Center + a.ToRotationVector2() * (radius + 7), 3, NorthPoleArt.Additive(NorthPoleArt.Core, 0.9f));
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
            for (int i = Projectile.oldPos.Length - 1; i >= 1; i--)
            {
                if (Projectile.oldPos[i] == Vector2.Zero) continue;
                float fade = (1 - i / (float)Projectile.oldPos.Length) * 0.4f * Projectile.Opacity;
                Vector2 c = Projectile.oldPos[i] + Projectile.Size / 2;
                NorthPoleArt.Line(c - dir * 6, c + dir * 6, 3, NorthPoleArt.Additive(NorthPoleArt.Ice, fade));
            }
            if (NorthPoleArt.DrawCustom("Shard", Projectile.Center, Projectile.rotation, 20, Color.White * Projectile.Opacity))
                return false;
            NorthPoleArt.Icicle(Projectile.Center - dir * 9, Projectile.Center + dir * 10, 6, Projectile.Opacity);
            return false;
        }
    }

    // No damage; only the owning player can collect this projectile with their body.
    // ai[0]: age, ai[1]: look variant. Falls from the sky THROUGH blocks.
    public class NorthPolePickup : ModProjectile
    {
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
        public override void AI()
        {
            if (Projectile.owner < 0 || Projectile.owner >= Main.maxPlayers) { Projectile.Kill(); return; }
            Player player = Main.player[Projectile.owner];
            if (!player.active || player.dead) { Projectile.Kill(); return; }
            Projectile.ai[0]++;
            // Falls fast from the top of the screen, drifts slowly while passing the
            // player's height so it can be caught, then falls away and melts.
            float below = Projectile.Center.Y - player.Center.Y;
            float fall = Math.Abs(below) < 72 ? 1.2f : below < 0 ? 5f : 2.5f;
            Projectile.velocity.Y = MathHelper.Lerp(Projectile.velocity.Y, fall, 0.12f);
            Projectile.velocity.X = MathF.Sin((Projectile.ai[0] + Projectile.whoAmI * 13) * 0.05f) * 0.8f;
            Projectile.rotation += 0.03f;
            if (below > 360) { Projectile.Kill(); return; }
            Lighting.AddLight(Projectile.Center, 0.2f, 0.35f, 0.5f);
            if (NorthPoleEffects.Owned(Projectile) && Projectile.ai[0] >= 12 && Projectile.Hitbox.Intersects(player.Hitbox))
            {
                player.AddBuff(ModContent.BuffType<NorthPoleBlessing>(), NorthPoleBalance.BlessingTicks);
                if (Main.netMode == NetmodeID.MultiplayerClient)
                    NetMessage.SendData(MessageID.PlayerBuffs, number: player.whoAmI);
                SoundEngine.PlaySound(SoundID.Item4 with { Volume = 0.6f, Pitch = 0.4f }, player.Center);
                NorthPoleEffects.SnowDust(Projectile.Center, 10, 2.5f);
                Projectile.Kill();
            }
        }
        public override void OnKill(int timeLeft) => NorthPoleEffects.SnowDust(Projectile.Center, 4);
        public override bool PreDraw(ref Color lightColor)
        {
            float twinkle = 0.75f + 0.25f * MathF.Sin(Projectile.ai[0] * 0.2f + Projectile.whoAmI);
            float fade = Math.Min(1, Projectile.timeLeft / 30f) * Math.Min(1, Projectile.ai[0] / 10f);
            if (NorthPoleArt.DrawCustom("Pickup", Projectile.Center, Projectile.rotation, 22, Color.White * fade))
                return false;
            NorthPoleArt.Dot(Projectile.Center, 20, NorthPoleArt.Additive(new Color(255, 230, 160), 0.18f * twinkle * fade), 0);
            NorthPoleArt.Snowflake(Projectile.Center, 9, Projectile.rotation, 0.5f + Projectile.ai[1] * 0.25f, twinkle * fade);
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
                NorthPoleArt.Snowflake(target.Center + new Vector2(half.X * 0.4f, -half.Y * 0.85f), 4, 0, 0, (glint - 0.6f) * 2.5f * fade);
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
            NorthPoleArt.Icicle(Projectile.Center, tip, Width, fade);
            // two smaller side spikes break out of the base
            Vector2 side = Direction.RotatedBy(MathHelper.PiOver2);
            NorthPoleArt.Icicle(Projectile.Center + side * Width * 0.4f, Projectile.Center + Direction.RotatedBy(0.45f) * length * 0.35f, Width * 0.45f, fade);
            NorthPoleArt.Icicle(Projectile.Center - side * Width * 0.4f, Projectile.Center + Direction.RotatedBy(-0.45f) * length * 0.35f, Width * 0.45f, fade);
            return false;
        }
    }
}
