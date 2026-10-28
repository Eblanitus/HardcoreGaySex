using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using FrostMelee.Content.Weapons;

namespace FrostMelee.Content.Projectiles
{
    // Shared mechanical base only: no lasso, snow growth, star effects or lantern resource.
    // ai[0] = mode, ai[1] = orbit angle, ai[2] = orbit direction. These are synced
    // by SyncProjectile. Only the owner reads input or decides transitions.
    public abstract class SharedFlailProjectile : ModProjectile
    {
        public enum FlailMode { Spin, Flight, Drop, Return }
        public abstract int SourceItemType { get; }
        protected virtual float ChainLength => 320f;
        protected virtual float OrbitRadius => 58f;
        protected virtual float OrbitSpeed => 0.24f;
        protected virtual float ReturnSpeed => 20f;
        protected virtual float Gravity => 0.35f;

        public FlailMode Mode => Projectile.ai[0] >= 0f && Projectile.ai[0] <= (float)FlailMode.Return
            ? (FlailMode)(int)Projectile.ai[0] : FlailMode.Return;
        private bool IsAuthority => Projectile.owner == Main.myPlayer;
        private bool initialized;
        private int stateAge;
        private int syncTicks;

        public override void SetDefaults()
        {
            Projectile.width = Projectile.height = 28;
            Projectile.aiStyle = -1;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.ignoreWater = true;
            Projectile.tileCollide = true;
            Projectile.ownerHitCheck = true;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 15;
            Projectile.netImportant = true;
            Projectile.timeLeft = 2;
        }

        private void ChangeMode(FlailMode next)
        {
            if (!IsAuthority || Mode == next)
                return;
            Projectile.ai[0] = (float)next;
            stateAge = 0;
            Projectile.netUpdate = true;
            Projectile.friendly = next != FlailMode.Return;
            Projectile.tileCollide = next == FlailMode.Flight || next == FlailMode.Drop;
        }

        public override void AI()
        {
            if (Projectile.owner < 0 || Projectile.owner >= Main.maxPlayers)
            {
                Projectile.Kill();
                return;
            }
            Player player = Main.player[Projectile.owner];
            // Also runs on dedicated servers: a disconnected owner cannot send Kill.
            if (!player.active || player.dead)
            {
                Projectile.Kill();
                return;
            }
            Projectile.timeLeft = 2;
            bool boundItemHeld = player.HeldItem.type == SourceItemType;

            if (IsAuthority)
            {
                var input = player.GetModPlayer<SharedFlailPlayer>();
                if (!boundItemHeld || !input.CanControl)
                    ChangeMode(FlailMode.Return);
                else if (initialized)
                {
                    // Ignore the spawning click. Only a NEW click drops a deployed head.
                    if (input.LeftPressed && (Mode == FlailMode.Spin || Mode == FlailMode.Flight))
                    {
                        if (Mode == FlailMode.Spin)
                            Projectile.velocity = new Vector2((Projectile.ai[2] < 0f ? -1f : 1f) * 2f, 1f);
                        ChangeMode(FlailMode.Drop);
                    }
                    else if (Mode == FlailMode.Spin && !input.RightHeld)
                        ChangeMode(FlailMode.Return);
                    else if (Mode == FlailMode.Drop && !input.LeftHeld)
                        ChangeMode(FlailMode.Return);
                }

                if (++syncTicks >= 6)
                {
                    syncTicks = 0;
                    Projectile.netUpdate = true;
                }
                stateAge++;
                if ((Mode == FlailMode.Flight || Mode == FlailMode.Drop)
                    && Vector2.DistanceSquared(player.MountedCenter, Projectile.Center + Projectile.velocity) > ChainLength * ChainLength)
                    ChangeMode(FlailMode.Return);
                if ((Mode == FlailMode.Flight && stateAge > 90) || (Mode == FlailMode.Return && stateAge > 180))
                {
                    if (Mode == FlailMode.Return)
                    {
                        Projectile.Kill();
                        return;
                    }
                    ChangeMode(FlailMode.Return);
                }
            }
            initialized = true;

            Projectile.friendly = Mode != FlailMode.Return;
            Projectile.tileCollide = Mode == FlailMode.Flight || Mode == FlailMode.Drop;
            switch (Mode)
            {
                case FlailMode.Spin:
                    // Deterministic prediction on observers, never their mouse position.
                    Projectile.ai[1] += OrbitSpeed * (Projectile.ai[2] < 0f ? -1f : 1f);
                    Vector2 wanted = player.MountedCenter + Projectile.ai[1].ToRotationVector2() * OrbitRadius;
                    Projectile.Center = ClearOrbitPosition(player.MountedCenter, wanted);
                    Projectile.velocity = Vector2.Zero;
                    Projectile.rotation = Projectile.ai[1];
                    break;
                case FlailMode.Flight:
                    Projectile.rotation += 0.25f;
                    break;
                case FlailMode.Drop:
                    Projectile.velocity.Y = Math.Min(Projectile.velocity.Y + Gravity, 12f);
                    Projectile.velocity.X *= 0.96f;
                    if (Math.Abs(Projectile.velocity.X) < 0.05f)
                        Projectile.velocity.X = 0f;
                    Projectile.rotation += Projectile.velocity.X * 0.04f;
                    break;
                default:
                    Vector2 delta = player.MountedCenter - Projectile.Center;
                    float distance = delta.Length();
                    if (IsAuthority && distance <= ReturnSpeed + 4f)
                    {
                        Projectile.Kill();
                        return;
                    }
                    Projectile.velocity = player.velocity + delta.SafeNormalize(Vector2.Zero) * Math.Min(ReturnSpeed, distance);
                    Projectile.rotation += 0.25f;
                    break;
            }

            if (boundItemHeld)
            {
                player.heldProj = Projectile.whoAmI;
                player.itemTime = 2;
                player.itemAnimation = 2;
                Vector2 aim = (Projectile.Center - player.MountedCenter).SafeNormalize(new Vector2(player.direction, 0f));
                int face = Mode == FlailMode.Spin ? (Projectile.ai[2] < 0f ? -1 : 1) : (aim.X < 0f ? -1 : 1);
                if (IsAuthority)
                    player.ChangeDir(face);
                player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, aim.ToRotation() - MathHelper.PiOver2);
            }
        }

        private Vector2 ClearOrbitPosition(Vector2 start, Vector2 end)
        {
            // A rotating head cannot teleport through a wall merely because its
            // angular position is assigned directly instead of using tile velocity.
            Vector2 delta = end - start;
            int steps = Math.Max(1, (int)Math.Ceiling(delta.Length() / 4f));
            Vector2 last = start;
            for (int i = 1; i <= steps; i++)
            {
                Vector2 next = start + delta * (i / (float)steps);
                if (Collision.SolidCollision(next - Projectile.Size * 0.5f, Projectile.width, Projectile.height))
                    break;
                last = next;
            }
            return last;
        }

        public override bool? CanDamage() => Mode == FlailMode.Return ? false : null;

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            if (IsAuthority && Mode == FlailMode.Flight)
                ChangeMode(FlailMode.Return);
            else if (IsAuthority && Mode == FlailMode.Drop && oldVelocity.LengthSquared() > 1f)
                Projectile.netUpdate = true;
            // Drop keeps the engine's clipped velocity and rests on the ground.
            // Return is intangible and harmless; no tile collision destroys the head.
            return false;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            if (Mode == FlailMode.Flight)
                ChangeMode(FlailMode.Return);
        }

        public override void OnHitPlayer(Player target, Player.HurtInfo info)
        {
            if (Mode == FlailMode.Flight)
                ChangeMode(FlailMode.Return);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Vector2 start = Main.player[Projectile.owner].MountedCenter;
            Vector2 delta = Projectile.Center - start;
            float distance = delta.Length();
            if (!float.IsFinite(distance) || distance < 1f)
                return true;
            Vector2 direction = delta / distance;
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            int links = Math.Min(300, (int)(distance / 6f));
            for (int i = 0; i < links; i++)
            {
                Vector2 at = start + direction * (i * 6f);
                Point tile = at.ToTileCoordinates();
                Color color = Lighting.GetColor(tile.X, tile.Y);
                Main.EntitySpriteDraw(pixel, at - Main.screenPosition, null,
                    color * 0.8f, direction.ToRotation(), new Vector2(0.5f),
                    new Vector2(5f, i % 2 == 0 ? 2f : 3f), SpriteEffects.None, 0f);
            }
            return true; // Only the head has a hitbox; the chain is cosmetic.
        }
    }
}
