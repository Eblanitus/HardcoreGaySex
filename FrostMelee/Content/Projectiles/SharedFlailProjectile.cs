using System;
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
    // Shared flail and lasso base. No weapon-specific snow/star/lantern resource.
    // Adapted mechanics from tModLoader ExampleAdvancedFlailProjectile (stable).
    // ai[0] = mode, ai[1] = state timer, ai[2] = facing at launch. These are synced
    // by SyncProjectile. Only the owner reads input or decides transitions.
    public abstract partial class SharedFlailProjectile : ModProjectile
    {
        public enum FlailMode { Spin, Flight, Drop, Return, Wrap }
        public abstract int SourceItemType { get; }
        protected virtual float ChainLength => 320f;
        protected virtual float OrbitRadius => 40f;
        protected virtual float OrbitSpeed => 0.24f;
        protected virtual float ReturnSpeed => 20f;
        protected virtual float Gravity => 0.8f;

        public FlailMode Mode => Projectile.ai[0] >= 0f && Projectile.ai[0] <= (float)FlailMode.Wrap
            ? (FlailMode)(int)Projectile.ai[0] : FlailMode.Return;
        private bool IsAuthority => Projectile.owner == Main.myPlayer;
        private bool initialized;
        private ref float StateTimer => ref Projectile.ai[1];
        private int syncTicks;

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.HeldProjDoesNotUsePlayerGfxOffY[Type] = true;
        }

        public override void SetDefaults()
        {
            Projectile.width = Projectile.height = 28;
            Projectile.aiStyle = -1;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.MeleeNoSpeed;
            Projectile.penetrate = -1;
            Projectile.ignoreWater = true;
            Projectile.tileCollide = true;
            Projectile.ownerHitCheck = false;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 10;
            Projectile.netImportant = true;
            Projectile.timeLeft = 2;
        }

        private void ChangeMode(FlailMode next)
        {
            if (!IsAuthority || Mode == next)
                return;
            Projectile.ai[0] = (float)next;
            if (next == FlailMode.Return)
                Projectile.velocity *= 0.3f; // ExampleMod's retraction brakes the outward throw first.
            StateTimer = 0f;
            Projectile.ResetLocalNPCHitImmunity();
            Projectile.netUpdate = true;
            Projectile.friendly = next != FlailMode.Return;
            Projectile.tileCollide = next == FlailMode.Flight || next == FlailMode.Drop || next == FlailMode.Wrap;
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
            if (!player.active || player.dead || player.ghost || !float.IsFinite(Projectile.Center.X)
                || !float.IsFinite(Projectile.Center.Y) || !float.IsFinite(Projectile.velocity.X)
                || !float.IsFinite(Projectile.velocity.Y) || !float.IsFinite(StateTimer)
                || !float.IsFinite(Projectile.ai[2]) || Projectile.Distance(player.Center) > 900f)
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
                else if (HandleLassoInput(player, input))
                {
                    // A bound-target yank takes priority over ordinary head dropping.
                }
                else if (initialized)
                {
                    // Ignore the spawning click. Only a NEW click drops a deployed head.
                    if (input.LeftPressed && (Mode == FlailMode.Spin || Mode == FlailMode.Flight))
                    {
                        // Like ExampleMod: shed most of the flight momentum on drop.
                        Projectile.velocity *= 0.2f;
                        ChangeMode(FlailMode.Drop);
                    }
                    else if (Mode == FlailMode.Spin && !input.RightHeld)
                        ChangeMode(FlailMode.Return);
                    else if (Mode == FlailMode.Drop && !input.LeftHeld)
                        ChangeMode(FlailMode.Return);
                }

                if (++syncTicks >= 15)
                {
                    syncTicks = 0;
                    Projectile.netUpdate = true;
                }
                if ((Mode == FlailMode.Flight || Mode == FlailMode.Drop)
                    && Vector2.DistanceSquared(player.MountedCenter, Projectile.Center + Projectile.velocity) > ChainLength * ChainLength)
                    ChangeMode(FlailMode.Return);
                if ((Mode == FlailMode.Flight && StateTimer > 90f) || (Mode == FlailMode.Return && StateTimer > 180f))
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

            float meleeSpeed = MathHelper.Clamp(player.GetTotalAttackSpeed(DamageClass.Melee), 0.1f, 10f);
            if (Mode != FlailMode.Wrap)
                StateTimer += Mode == FlailMode.Spin ? meleeSpeed : 1f;
            Projectile.ownerHitCheck = Mode == FlailMode.Spin;
            Projectile.localNPCHitCooldown = Mode == FlailMode.Spin ? 20 : 10;
            Projectile.friendly = Mode != FlailMode.Return;
            Projectile.tileCollide = Mode == FlailMode.Flight || Mode == FlailMode.Drop || Mode == FlailMode.Wrap;
            switch (Mode)
            {
                case FlailMode.Spin:
                    // Deterministic prediction on observers, never their mouse position.
                    float angle = StateTimer * OrbitSpeed * (Projectile.ai[2] < 0f ? -1f : 1f)
                        + (Projectile.ai[2] < 0f ? MathHelper.Pi : 0f);
                    Vector2 offset = angle.ToRotationVector2() * OrbitRadius;
                    // ExampleMod's flattened lower ellipse keeps the head above the floor.
                    offset.Y *= 0.8f;
                    if (offset.Y * player.gravDir > 0f)
                        offset.Y *= 0.35f;
                    Vector2 anchor = player.MountedCenter - Vector2.UnitY * 8f * player.gravDir;
                    Vector2 wanted = anchor + offset;
                    Projectile.Center = ClearOrbitPosition(anchor, wanted);
                    Projectile.velocity = Vector2.Zero;
                    Projectile.rotation = angle;
                    break;
                case FlailMode.Wrap:
                    LassoAI(player, meleeSpeed);
                    break;
                case FlailMode.Flight:
                    Projectile.rotation += 0.25f;
                    break;
                case FlailMode.Drop:
                    Projectile.velocity.Y = MathHelper.Clamp(Projectile.velocity.Y + Gravity * player.gravDir, -12f, 12f);
                    Projectile.velocity.X *= 0.96f;
                    if (Math.Abs(Projectile.velocity.X) < 0.05f)
                        Projectile.velocity.X = 0f;
                    Projectile.rotation += Projectile.velocity.X * 0.04f;
                    break;
                default:
                    Vector2 delta = player.MountedCenter - Projectile.Center;
                    float distance = delta.Length();
                    float returnSpeed = ReturnSpeed * meleeSpeed;
                    Vector2 targetVelocity = player.velocity + delta.SafeNormalize(Vector2.Zero) * returnSpeed;
                    Projectile.velocity = Projectile.velocity.MoveTowards(targetVelocity, 6f * meleeSpeed);
                    // Catch using relative motion, not just a fixed radius. This also
                    // catches fast heads crossing a moving player between two ticks.
                    Vector2 step = Projectile.velocity - player.velocity;
                    float t = step.LengthSquared() > 0f
                        ? MathHelper.Clamp(Vector2.Dot(delta, step) / step.LengthSquared(), 0f, 1f) : 0f;
                    if (IsAuthority && (distance <= 16f || (delta - step * t).LengthSquared() <= 144f))
                    {
                        Projectile.Kill();
                        return;
                    }
                    Projectile.rotation += 0.25f;
                    break;
            }

            if (boundItemHeld && !player.noItems && !player.CCed)
            {
                player.heldProj = Projectile.whoAmI;
                player.SetDummyItemTime(2);
                Vector2 aim = (Projectile.Center - player.MountedCenter).SafeNormalize(new Vector2(player.direction, 0f));
                // Do not turn the player twice per orbit/return just to follow the ball.
                int face = Projectile.ai[2] < 0f ? -1 : 1;
                if (IsAuthority)
                    player.ChangeDir(face);
                player.itemRotation = MathHelper.WrapAngle(aim.ToRotation() + (face < 0 ? MathHelper.Pi : 0f));
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

        public override bool? CanDamage() => Mode == FlailMode.Return
            || (Mode == FlailMode.Spin && StateTimer <= 12f) ? false : null;

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            modifiers.HitDirectionOverride = target.Center.X < Main.player[Projectile.owner].Center.X ? -1 : 1;
            if (Mode == FlailMode.Spin)
                modifiers.Knockback *= 0.25f;
            else if (Mode == FlailMode.Drop)
                modifiers.Knockback *= 0.5f;
        }

        public override bool TileCollideStyle(ref int width, ref int height, ref bool fallThrough, ref Vector2 hitboxCenterFrac)
        {
            // A dropped head rests on platforms as well as solid blocks.
            fallThrough = false;
            return true;
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            bool hitX = oldVelocity.X != Projectile.velocity.X;
            bool hitY = oldVelocity.Y != Projectile.velocity.Y;
            bool impact = (hitX && Math.Abs(oldVelocity.X) > 4f) || (hitY && Math.Abs(oldVelocity.Y) > 4f);
            if (Mode == FlailMode.Flight || Mode == FlailMode.Wrap)
            {
                if (Mode == FlailMode.Wrap)
                    lassoFailure = "Blocked";
                // One transition to an intangible return: cannot stick to a corner.
                ChangeMode(FlailMode.Return);
            }
            else if (Mode == FlailMode.Drop)
            {
                if (hitX) Projectile.velocity.X = 0f;
                if (hitY) Projectile.velocity.Y = 0f;
                if (IsAuthority && impact)
                    Projectile.netUpdate = true;
            }
            if (impact)
            {
                Collision.HitTiles(Projectile.position, oldVelocity, Projectile.width, Projectile.height);
                SoundEngine.PlaySound(SoundID.Dig, Projectile.Center);
            }
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
            if (Projectile.owner < 0 || Projectile.owner >= Main.maxPlayers)
                return false;
            // ExampleMod's arm-position correction for slopes and stepping up tiles.
            Vector2 start = Main.GetPlayerArmPosition(Projectile);
            start.Y -= Main.player[Projectile.owner].gfxOffY;
            Texture2D chain = ModContent.Request<Texture2D>("FrostMelee/Content/Projectiles/SharedFlailChain").Value;
            NPC bound = BoundNPC();
            if (bound != null)
                DrawLasso(chain, start, bound);
            else
                DrawChainSegment(chain, start, Projectile.Center);
            // Explicit center/origin: the 32px sprites and 28px hitboxes must not
            // produce an off-center wobble when rotated.
            Texture2D head = TextureAssets.Projectile[Type].Value;
            Main.EntitySpriteDraw(head, Projectile.Center - Main.screenPosition, null,
                Projectile.GetAlpha(lightColor), Projectile.rotation, head.Size() * 0.5f,
                Projectile.scale, SpriteEffects.None, 0f);
            return false;
        }
    }
}
