using System;
using System.Collections.Generic;
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
    // Server/SP owns spawning, transitions, phase, wrap progress and resource.
    // Clients predict movement between snapshots; normal contact damage still
    // uses Terraria's owning-client projectile pipeline (including hit hooks).
    public abstract partial class SharedFlailProjectile : ModProjectile
    {
        public enum FlailMode { Spin, Flight, Return, LassoCast, Approach, Wrap, Yank }
        public abstract int SourceItemType { get; }
        protected virtual float ChainLength => 320f;
        protected virtual float ReturnSpeed => 20f;
        protected virtual float MovementSpeedMultiplier => 1f;
        protected virtual float WeaponDamageMultiplier => 1f;
        public virtual int LassoDamage => (int)Math.Round(Projectile.damage * WeaponDamageMultiplier);
        public virtual float LassoPullSpeed => 12f * MovementSpeedMultiplier;
        public virtual void OnLassoYank(NPC target) { } // Impact, not the start of pulling.
        public bool SpinningThisTick { get; private set; }
        public int SpinTicks { get; private set; }
        public int RampTicks { get; private set; }
        public FlailMode Mode => Projectile.ai[0] >= 0 && Projectile.ai[0] <= (float)FlailMode.Yank
            ? (FlailMode)(int)Projectile.ai[0] : FlailMode.Return;
        private bool IsAuthority => Main.netMode != NetmodeID.MultiplayerClient;
        private ref float StateTimer => ref Projectile.ai[1];
        private ref float Phase => ref Projectile.ai[2];
        private bool initialized, impactApplied, contactDamage;
        private FlailMode contactMode;
        private int syncTicks, facing = 1, lastSoundMode = -1;
        private readonly List<Vector2> damagePath = new();
        private int boundSlot = -1;
        private Item boundItem;

        public override void SetStaticDefaults() => ProjectileID.Sets.HeldProjDoesNotUsePlayerGfxOffY[Type] = true;
        public override void SetDefaults()
        {
            Projectile.width = Projectile.height = 28;
            Projectile.aiStyle = -1;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.MeleeNoSpeed;
            Projectile.penetrate = -1;
            Projectile.ignoreWater = true;
            // All states share the same swept movement solver, not engine
            // movement plus a second, conflicting radial-position correction.
            Projectile.tileCollide = false;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 10;
            Projectile.netImportant = true;
            Projectile.timeLeft = 2;
        }
        public override bool ShouldUpdatePosition() => false;

        private void ChangeMode(FlailMode next)
        {
            if (!IsAuthority || Mode == next) return;
            Projectile.ai[0] = (float)next;
            StateTimer = 0f;
            if (next == FlailMode.Return)
            {
                Projectile.velocity *= 0.3f;
                RampTicks = 0;
            }
            Projectile.netUpdate = true;
            // Do not reset NPC immunity at every state transition: a piercing
            // throw cannot hit one target twice just by changing its state.
        }

        public override void AI()
        {
            contactDamage = false;
            if (Projectile.owner < 0 || Projectile.owner >= Main.maxPlayers)
            {
                if (IsAuthority) Projectile.Kill();
                return;
            }
            Player player = Main.player[Projectile.owner];
            if (!player.active || player.dead || player.ghost || !Finite(Projectile.Center)
                || !Finite(Projectile.velocity) || !float.IsFinite(StateTimer) || !float.IsFinite(Phase)
                || Projectile.Distance(player.Center) > 900f)
            {
                Projectile.friendly = false;
                if (IsAuthority) Projectile.Kill();
                return;
            }
            var input = player.GetModPlayer<SharedFlailPlayer>();
            if (!initialized)
            {
                initialized = true;
                boundSlot = player.selectedItem;
                boundItem = player.HeldItem;
                facing = input.Aim.X < 0 ? -1 : 1;
                if (IsAuthority)
                {
                    SpinFrame(player, out Vector2 anchor, out _, out _, out _);
                    Projectile.Center = anchor;
                    Phase = facing > 0 ? MathHelper.PiOver4 : MathHelper.Pi + MathHelper.PiOver4;
                    if (Mode == FlailMode.Flight) Launch(player, input.Aim);
                    Projectile.netUpdate = true;
                }
            }
            damagePath.Clear();
            damagePath.Add(Projectile.Center);
            SpinningThisTick = false;
            Projectile.timeLeft = 2;
            if (IsAuthority)
            {
                bool sameItem = StillHeld(player);
                if (!sameItem || !input.CanContinueAttack)
                    AbortLasso("Interrupted");
                else if (!HandleLassoInput(player, input) && Mode == FlailMode.Spin && !input.RightHeld)
                    ChangeMode(FlailMode.Return);
            }
            if (!Main.dedServ && lastSoundMode != (int)Mode && (Mode == FlailMode.Flight || Mode == FlailMode.LassoCast))
                SoundEngine.PlaySound(SoundID.Item1, Projectile.Center);
            lastSoundMode = (int)Mode;
            StateTimer += 1f; // Real elapsed ticks, never attack-speed-scaled.
            float speed = MathHelper.Clamp(player.GetTotalAttackSpeed(DamageClass.Melee), 0.1f, 10f);
            Projectile.ownerHitCheck = Mode == FlailMode.Spin;
            Projectile.localNPCHitCooldown = Mode == FlailMode.Spin ? 20 : 10;
            Projectile.friendly = Mode == FlailMode.Spin || Mode == FlailMode.Flight;
            contactMode = Mode;
            contactDamage = Mode == FlailMode.Flight;
            switch (Mode)
            {
                case FlailMode.Spin:
                    SpinTicks++;
                    contactDamage = SpinTicks > 12;
                    if (IsAuthority) facing = input.Aim.X < 0 ? -1 : 1;
                    Spin(player);
                    RampTicks = SpinningThisTick ? Math.Min(90, RampTicks + 1) : 0;
                    break;
                case FlailMode.Flight:
                case FlailMode.LassoCast:
                    FlailMode movingMode = Mode;
                    Vector2 end = Projectile.Center + Projectile.velocity;
                    Vector2 fromPlayer = end - player.MountedCenter;
                    bool lengthLimited = fromPlayer.LengthSquared() > ChainLength * ChainLength;
                    if (lengthLimited) end = player.MountedCenter + fromPlayer.SafeNormalize(Vector2.UnitX) * ChainLength;
                    bool reached = MoveHead(end, true);
                    if (movingMode == FlailMode.LassoCast && IsAuthority)
                        AcquireLassoTarget(player);
                    if (IsAuthority && Mode == movingMode)
                    {
                        if (!reached) AbortLasso("Blocked");
                        else if (lengthLimited || StateTimer > 90 || Projectile.Distance(player.MountedCenter) >= ChainLength)
                            AbortLasso("TooShort");
                    }
                    Projectile.rotation += 0.25f;
                    break;
                case FlailMode.Approach:
                case FlailMode.Wrap:
                case FlailMode.Yank:
                    LassoAI(player, speed * MovementSpeedMultiplier);
                    break;
                default:
                    Return(player, speed);
                    break;
            }
            if (IsAuthority && ++syncTicks >= (LassoInProgress ? 3 : 6))
            {
                syncTicks = 0;
                Projectile.netUpdate = true;
            }
            if (player.HeldItem.type == SourceItemType && !player.noItems && !player.CCed)
            {
                player.heldProj = Projectile.whoAmI;
                player.SetDummyItemTime(2);
                int poseFacing = Mode == FlailMode.Spin ? facing : (Projectile.Center.X < player.MountedCenter.X ? -1 : 1);
                player.ChangeDir(poseFacing);
                Vector2 handDirection = (Projectile.Center - player.MountedCenter).SafeNormalize(new Vector2(facing, 0));
                player.itemRotation = MathHelper.WrapAngle(handDirection.ToRotation()
                    + (Projectile.Center.X < player.MountedCenter.X ? MathHelper.Pi : 0f));
                player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full,
                    handDirection.ToRotation() - MathHelper.PiOver2);
            }
        }

        private bool StillHeld(Player player) => player.HeldItem.type == SourceItemType && player.selectedItem == boundSlot
            && ReferenceEquals(boundItem, player.HeldItem);

        private bool GroundedAt(Vector2 feet, float gravity)
        {
            Vector2 center = feet - Vector2.UnitY * (Projectile.height * 0.5f + 3f) * gravity;
            Vector2 probe = Vector2.UnitY * 6f * gravity;
            Vector2 allowed = Collision.TileCollision(center - Projectile.Size * 0.5f, probe,
                Projectile.width, Projectile.height, false, false, (int)gravity);
            return (allowed - probe).LengthSquared() > 0.01f;
        }

        private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
        private void Launch(Player player, Vector2 aim)
        {
            float speed = MathHelper.Clamp(player.GetTotalAttackSpeed(DamageClass.Melee), 0.1f, 10f);
            facing = aim.X < 0 ? -1 : 1;
            Projectile.velocity = aim * player.HeldItem.shootSpeed * speed * MovementSpeedMultiplier + player.velocity;
        }

        private void SpinFrame(Player player, out Vector2 anchor, out float x, out float upper, out float lower)
        {
            // ExampleMod's new Vector2(direction) has length sqrt(2), not one.
            x = 30f * MathF.Sqrt(2f);
            upper = x * 0.8f;
            lower = upper * 0.5f;
            anchor = player.MountedCenter + Vector2.UnitY * player.gfxOffY;
            Vector2 floorProbe = anchor + Vector2.UnitY * lower * player.gravDir;
            if (Collision.SolidCollision(floorProbe - Projectile.Size * 0.5f, Projectile.width, Projectile.height)
                || GroundedAt(player.gravDir > 0 ? player.Bottom : player.Top, player.gravDir))
            {
                float footRoom = player.gravDir > 0 ? player.Bottom.Y - anchor.Y : anchor.Y - player.Top.Y;
                float lift = Math.Max(0, Projectile.height * 0.5f + 4f - footRoom);
                anchor.Y -= lift * player.gravDir;
                lower = Math.Max(2f, Math.Min(lower, footRoom + lift - Projectile.height * 0.5f - 2f));
            }
        }

        private void Spin(Player player)
        {
            SpinFrame(player, out Vector2 anchor, out float x, out float upper, out float lower);
            // Vanilla period is 12 real ticks. Snow slows the integrated angular
            // step by up to 25%; attack speed does not change the warm-up clock.
            float delta = MathHelper.Pi / 6f * MovementSpeedMultiplier * facing;
            int slices = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / 0.06f));
            for (int i = 0; i < slices; i++)
            {
                float angle = Phase + delta / slices;
                Vector2 unit = angle.ToRotationVector2();
                float vertical = unit.Y * player.gravDir > 0 ? lower : upper;
                Vector2 wanted = anchor + new Vector2(unit.X * x, unit.Y * vertical);
                if (!MoveHead(wanted, true)) break;
                Phase = MathHelper.WrapAngle(angle);
                SpinningThisTick = true;
            }
            Projectile.velocity = Vector2.Zero;
            Projectile.rotation = Phase;
        }

        // Swept head motion in <=4px steps, shared by spin, throw and wrapping.
        // Never test a radial ray from the player or teleport through a corner.
        private bool MoveHead(Vector2 wanted, bool recordDamage)
        {
            Vector2 delta = wanted - Projectile.Center;
            int steps = Math.Max(1, (int)Math.Ceiling(delta.Length() / 4f));
            Vector2 step = delta / steps;
            for (int i = 0; i < steps; i++)
            {
                float gravity = LassoInProgress ? 1f : Main.player[Projectile.owner].gravDir;
                Vector2 allowed = Collision.TileCollision(Projectile.position, step, Projectile.width, Projectile.height, false, false, (int)gravity);
                Vector2 next = Projectile.position + allowed;
                if (Collision.SolidCollision(next, Projectile.width, Projectile.height)) return false;
                Projectile.position = next;
                if (recordDamage) damagePath.Add(Projectile.Center);
                if ((allowed - step).LengthSquared() > 0.01f) return false;
            }
            return true;
        }

        private void Return(Player player, float speed)
        {
            Vector2 delta = player.MountedCenter - Projectile.Center;
            float distance = delta.Length();
            Projectile.velocity = Projectile.velocity.MoveTowards(player.velocity
                + delta.SafeNormalize(Vector2.Zero) * ReturnSpeed * speed * MovementSpeedMultiplier,
                6f * speed * MovementSpeedMultiplier);
            Vector2 relativeStep = Projectile.velocity - player.velocity;
            float t = relativeStep.LengthSquared() > 0 ? MathHelper.Clamp(Vector2.Dot(delta, relativeStep) / relativeStep.LengthSquared(), 0, 1) : 0;
            if (IsAuthority && (distance <= 16f || (delta - relativeStep * t).LengthSquared() <= 144f || StateTimer > 180))
                Projectile.Kill();
            else
                Projectile.Center += Projectile.velocity;
            Projectile.rotation += 0.25f;
        }

        public override bool? CanDamage() => contactDamage ? null : false;
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            // Keep the approved physical-head rule, not vanilla's full 55px disk.
            // Minkowski expansion + thin segment covers the entire swept AABB.
            Vector2 half = Projectile.Size * 0.5f;
            Rectangle expanded = targetHitbox;
            expanded.Inflate((int)Math.Ceiling(half.X), (int)Math.Ceiling(half.Y));
            foreach (Vector2 point in damagePath)
                if (expanded.Contains(point.ToPoint())) return true;
            for (int i = 1; i < damagePath.Count; i++)
            {
                if (Collision.CheckAABBvLineCollision(expanded.TopLeft(), expanded.Size(), damagePath[i - 1], damagePath[i]))
                    return true;
            }
            return projHitbox.Intersects(targetHitbox);
        }
        private float ContactFactor(Vector2 target)
        {
            float factor = contactMode == FlailMode.Spin ? MathHelper.Lerp(0.6f, 1.4f, RampTicks / 90f)
                : MathHelper.Lerp(1.2f, 0.5f, MathHelper.Clamp((Vector2.Distance(target,
                    Main.player[Projectile.owner].MountedCenter) - 96f) / (ChainLength - 96f), 0, 1));
            return factor * WeaponDamageMultiplier;
        }
        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            modifiers.HitDirectionOverride = target.Center.X < Main.player[Projectile.owner].Center.X ? -1 : 1;
            modifiers.SourceDamage *= ContactFactor(target.Center);
            if (contactMode == FlailMode.Spin) modifiers.Knockback *= 0.25f;
        }
        public override void ModifyHitPlayer(Player target, ref Player.HurtModifiers modifiers)
        {
            modifiers.SourceDamage *= ContactFactor(target.Center);
        }
        // Infinite pierce + local NPC cooldown. Neither weapon returns on NPC hit.
        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) { }

        public void CompleteLassoImpact(NPC target)
        {
            if (!IsAuthority || impactApplied || Mode != FlailMode.Yank) return;
            impactApplied = true;
            Main.player[Projectile.owner].ApplyDamageToNPC(target, LassoDamage, 0f,
                Main.player[Projectile.owner].Center.X < target.Center.X ? -1 : 1, false, DamageClass.MeleeNoSpeed);
            OnLassoYank(target);
        }
        public void EndLasso(string reason = "Returning") => AbortLasso(reason);

        public override bool PreDraw(ref Color lightColor)
        {
            if (Projectile.owner < 0 || Projectile.owner >= Main.maxPlayers) return false;
            Vector2 hand = Main.GetPlayerArmPosition(Projectile);
            hand.Y -= Main.player[Projectile.owner].gfxOffY;
            Texture2D chain = ModContent.Request<Texture2D>("FrostMelee/Content/Projectiles/SharedFlailChain").Value;
            if (LassoInProgress && OrbitNPC != null && Mode != FlailMode.LassoCast)
                DrawLasso(chain, hand, OrbitNPC);
            else
                DrawChainSegment(chain, hand, Projectile.Center);
            Texture2D head = TextureAssets.Projectile[Type].Value;
            Main.EntitySpriteDraw(head, Projectile.Center - Main.screenPosition, null,
                Projectile.GetAlpha(lightColor), Projectile.rotation, head.Size() * 0.5f,
                Projectile.scale, SpriteEffects.None, 0f);
            return false;
        }
    }
}
