using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

        public static void Trail(Projectile spear)
        {
            if (!Owned(spear) || Collision.SolidCollision(spear.position, spear.width, spear.height)) return;
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

        public static void BraceSpike(Projectile brace, NPC target)
        {
            if (!Owned(brace) || target.friendly) return;
            Vector2 direction = brace.ai[1].ToRotationVector2();
            float extent = Math.Abs(direction.X) * target.width * 0.5f + Math.Abs(direction.Y) * target.height * 0.5f;
            Vector2 origin = target.Center + direction * (extent + 4);
            if (!Collision.CanHitLine(target.Center, 1, 1, origin, 1, 1)
                || Collision.SolidCollision(origin - new Vector2(4), 8, 8)) return;
            float speed = MathHelper.Clamp(Main.player[brace.owner].velocity.Length() / NorthPoleBalance.SpikeFullSpeed, 0, 1);
            Projectile.NewProjectile(brace.GetSource_FromThis(), origin, Vector2.Zero,
                ModContent.ProjectileType<NorthPoleBraceSpike>(), brace.damage, brace.knockBack,
                brace.owner, brace.ai[1], speed, target.whoAmI);
        }

        public static void SnowDust(Vector2 point, int count)
        {
            if (Main.dedServ) return;
            for (int i = 0; i < count; i++)
            {
                Dust d = Dust.NewDustPerfect(point, DustID.SnowflakeIce, Main.rand.NextVector2Circular(3, 3));
                d.noGravity = true;
            }
        }
    }

    // Actual vanilla North Pole snowflake AI/frames, but terrain collision stays
    // enabled on EVERY peer, not an unsynchronized assignment on the spawner.
    public class NorthPoleTrail : ModProjectile
    {
        public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.NorthPoleSnowflake;
        public override void SetStaticDefaults() => Main.projFrames[Type] = 3;
        public override void SetDefaults()
        {
            Projectile.CloneDefaults(ProjectileID.NorthPoleSnowflake);
            AIType = ProjectileID.NorthPoleSnowflake;
            Projectile.tileCollide = true;
            Projectile.DamageType = DamageClass.Melee;
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
        public override void SetStaticDefaults() => Main.projFrames[Type] = 3;
        public override void SetDefaults()
        {
            Projectile.width = Projectile.height = 24;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = 1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 180;
            Projectile.netImportant = true;
        }
        public override bool ShouldUpdatePosition() => !Charging;
        public override bool? CanDamage() => Charging ? false : null;
        public override bool? CanHitNPC(NPC target) => Collision.CanHitLine(Projectile.Center, 1, 1, target.Center, 1, 1) ? null : false;

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
            if (Projectile.ai[1] >= NorthPoleBalance.FullCharge) Release(false);
        }

        public void Release(bool weak)
        {
            if (!NorthPoleEffects.Owned(Projectile) || !Charging) return;
            if (Collision.SolidCollision(Projectile.position, Projectile.width, Projectile.height))
                Projectile.Center = HoverPoint(Main.player[Projectile.owner]);
            int targetId = (int)Projectile.ai[2];
            NPC target = targetId >= 0 && targetId < Main.maxNPCs ? Main.npc[targetId] : null;
            if (target != null && target.type == targetType && target.CanBeChasedBy(Projectile)) lastTarget = target.Center;
            else
            {
                float best = 800 * 800;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    NPC n = Main.npc[i];
                    float distance = Vector2.DistanceSquared(n.Center, Projectile.Center);
                    if (n.CanBeChasedBy(Projectile) && distance < best
                        && Collision.CanHitLine(Projectile.Center, 1, 1, n.Center, 1, 1))
                    { best = distance; lastTarget = n.Center; }
                }
            }
            float level = MathHelper.Clamp(Projectile.ai[1] / NorthPoleBalance.FullCharge, 0, 1);
            Projectile.damage = Math.Max(1, (int)(storedDamage * MathHelper.Lerp(0.4f, 1.5f, level)
                * (weak ? NorthPoleBalance.WeakShot : 1)));
            Projectile.ai[0] = 1;
            Projectile.velocity = (lastTarget - Projectile.Center).SafeNormalize(new Vector2(Main.player[Projectile.owner].direction, 0))
                * NorthPoleBalance.ShotSpeed;
            Projectile.tileCollide = true;
            flightTicks = 0;
            Projectile.netUpdate = true;
            SoundEngine.PlaySound(SoundID.Item28, Projectile.Center);
        }

        private void ResizeForCharge()
        {
            float level = MathHelper.Clamp(Projectile.ai[1] / NorthPoleBalance.FullCharge, 0, 1);
            Projectile.scale = MathHelper.Lerp(0.6f, 1.8f, level);
            Vector2 center = Projectile.Center;
            Projectile.width = Projectile.height = (int)(26 * Projectile.scale);
            Projectile.Center = center;
        }

        private Vector2 HoverPoint(Player player)
        {
            Vector2 preferred = player.MountedCenter + new Vector2(-player.direction * 34,
                -64 + MathF.Sin((float)Main.GameUpdateCount * 0.05f) * 5);
            for (int i = 0; i < 5; i++)
            {
                Vector2 candidate = i == 0 ? preferred : player.MountedCenter + new Vector2(
                    i == 2 ? 32 : i == 3 ? -32 : 0, i == 4 ? -48 : -24);
                if (!Collision.SolidCollision(candidate - Projectile.Size / 2, Projectile.width, Projectile.height)
                    && Collision.CanHitLine(player.Center, 1, 1, candidate, 1, 1)) return candidate;
            }
            // No full-sized free space in a narrow tunnel: keep it harmless
            // while charging; a launched flake will collide normally.
            return player.MountedCenter - Vector2.UnitY * 24;
        }

        public override void AI()
        {
            if (Projectile.owner < 0 || Projectile.owner >= Main.maxPlayers) { Projectile.Kill(); return; }
            Player player = Main.player[Projectile.owner];
            if (!player.active || player.dead) { Projectile.Kill(); return; }
            Projectile.timeLeft = 2; // observers don't expire on the charging->flight transition
            ResizeForCharge();
            Projectile.rotation += 0.04f;
            if (Charging)
            {
                if (Projectile.owner == Main.myPlayer && player.HeldItem.type != ItemID.NorthPole) { Projectile.Kill(); return; }
                Vector2 desired = HoverPoint(player);
                Projectile.Center = Vector2.Lerp(Projectile.Center, desired, 0.25f);
                Projectile.velocity = Vector2.Zero;
                Projectile.tileCollide = false;
                int target = (int)Projectile.ai[2];
                if (target >= 0 && target < Main.maxNPCs && Main.npc[target].active && Main.npc[target].type == targetType)
                    lastTarget = Main.npc[target].Center;
            }
            else
            {
                Projectile.tileCollide = true;
                if (++flightTicks > 180) Projectile.Kill();
            }
            Lighting.AddLight(Projectile.Center, 0.15f, 0.35f, 0.45f);
        }
        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) => impacted = true;
        public override bool OnTileCollide(Vector2 oldVelocity) { impacted = true; return true; }
        public override void OnKill(int timeLeft)
        {
            NorthPoleEffects.SnowDust(Projectile.Center, 12);
            if (!impacted || Charging || !NorthPoleEffects.Owned(Projectile)) return;
            for (int i = 0; i < NorthPoleBalance.Shards; i++)
            {
                Vector2 velocity = (MathHelper.TwoPi * i / NorthPoleBalance.Shards).ToRotationVector2() * 7;
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), Projectile.Center, velocity,
                    ModContent.ProjectileType<NorthPoleShard>(), Math.Max(1, (int)(Projectile.damage * NorthPoleBalance.ShardDamage)),
                    Projectile.knockBack * 0.4f, Projectile.owner, Main.rand.Next(3));
            }
        }
        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D t = TextureAssets.Projectile[Type].Value;
            Rectangle frame = t.Frame(1, 3, 0, 0);
            Main.EntitySpriteDraw(t, Projectile.Center - Main.screenPosition, frame, Color.White,
                Projectile.rotation, frame.Size() / 2, Projectile.scale, SpriteEffects.None, 0);
            return false;
        }
    }

    public class NorthPoleShard : ModProjectile
    {
        public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.FrostShard;
        public override void SetStaticDefaults() => Main.projFrames[Type] = 3;
        public override void SetDefaults()
        {
            Projectile.width = Projectile.height = 12;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = 2;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
            Projectile.tileCollide = true;
            Projectile.timeLeft = 90;
        }
        public override void AI()
        {
            Projectile.frame = Math.Clamp((int)Projectile.ai[0], 0, 2);
            Projectile.velocity.Y += 0.2f;
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;
        }
        public override void OnKill(int timeLeft) => NorthPoleEffects.SnowDust(Projectile.Center, 3);
    }

    // No damage; only the owning player can collect this projectile with their body.
    public class NorthPolePickup : ModProjectile
    {
        public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.NorthPoleSnowflake;
        public override void SetStaticDefaults() => Main.projFrames[Type] = 3;
        public override void SetDefaults()
        {
            Projectile.width = Projectile.height = 18;
            Projectile.tileCollide = true;
            Projectile.ignoreWater = true;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 360;
            Projectile.netImportant = true;
        }
        public override bool? CanDamage() => false;
        public override void AI()
        {
            if (Projectile.owner < 0 || Projectile.owner >= Main.maxPlayers) { Projectile.Kill(); return; }
            Player player = Main.player[Projectile.owner];
            if (!player.active || player.dead) { Projectile.Kill(); return; }
            Projectile.frame = Math.Clamp((int)Projectile.ai[1], 0, 2);
            Projectile.rotation += 0.03f;
            Projectile.velocity.Y = Math.Min(3f, Projectile.velocity.Y + 0.08f);
            Projectile.ai[0]++;
            if (NorthPoleEffects.Owned(Projectile) && Projectile.ai[0] >= 12 && Projectile.Hitbox.Intersects(player.Hitbox))
            {
                player.AddBuff(ModContent.BuffType<NorthPoleBlessing>(), NorthPoleBalance.BlessingTicks);
                if (Main.netMode == NetmodeID.MultiplayerClient)
                    NetMessage.SendData(MessageID.PlayerBuffs, number: player.whoAmI);
                SoundEngine.PlaySound(SoundID.Item4, player.Center);
                Projectile.Kill();
            }
        }
        public override bool OnTileCollide(Vector2 oldVelocity) { Projectile.velocity = Vector2.Zero; return false; }
        public override void OnKill(int timeLeft) => NorthPoleEffects.SnowDust(Projectile.Center, 5);
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
            if (!Main.dedServ && Main.rand.NextBool(3))
            {
                Vector2 rim = Main.rand.NextVector2Unit() * new Vector2(target.width, target.height) * 0.5f;
                Dust d = Dust.NewDustPerfect(target.Center + rim, DustID.Snow, Vector2.Zero, 0, Color.White, 1.1f);
                d.noGravity = true;
            }
        }
        public override bool PreDraw(ref Color lightColor)
        {
            int id = (int)Projectile.ai[0];
            if (id < 0 || id >= Main.maxNPCs || !Main.npc[id].active) return false;
            NPC target = Main.npc[id];
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            for (int i = 0; i < 10; i++)
            {
                Vector2 rim = (MathHelper.TwoPi * i / 10).ToRotationVector2() * new Vector2(target.width, target.height) * 0.42f;
                Main.EntitySpriteDraw(pixel, target.Center + rim - Main.screenPosition, null,
                    new Color(220, 245, 255, 200) * Math.Min(1, Projectile.timeLeft / 30f), i * 0.7f,
                    new Vector2(0.5f), new Vector2(5, 3), SpriteEffects.None, 0);
            }
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
            Projectile.tileCollide = false; // custom, non-moving, tile-clipped growth
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 42;
        }
        public override bool ShouldUpdatePosition() => false;
        public override void AI()
        {
            float t = MathHelper.Clamp(++age / 18f, 0, 1);
            float grow = 1 - (1 - t) * (1 - t);
            float wanted = MathHelper.Lerp(NorthPoleBalance.SpikeMinLength, NorthPoleBalance.SpikeMaxLength,
                MathHelper.Clamp(Projectile.ai[1], 0, 1)) * grow;
            length = 0;
            int width = (int)Math.Ceiling(Width);
            for (float distance = 0; distance <= wanted; distance += 4)
            {
                Vector2 point = Projectile.Center + Direction * distance;
                if (Collision.SolidCollision(point - new Vector2(width * 0.5f), width, width)) break;
                length = distance;
            }
        }
        public override bool? CanHitNPC(NPC target) => target.whoAmI == (int)Projectile.ai[2]
            || !Collision.CanHitLine(Projectile.Center, 1, 1, target.Center, 1, 1) ? false : null;
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
            Texture2D t = TextureAssets.Projectile[Type].Value;
            float fade = Math.Min(1, Projectile.timeLeft / 12f);
            Main.EntitySpriteDraw(t, Projectile.Center - Main.screenPosition, null, Color.White * fade,
                Direction.ToRotation() + MathHelper.PiOver2, new Vector2(t.Width / 2f, t.Height),
                new Vector2(Width / t.Width, length / t.Height), SpriteEffects.None, 0);
            return false;
        }
    }
}
