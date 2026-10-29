using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using FrostMelee.Content.Weapons;

namespace FrostMelee.Content.Projectiles
{
    public abstract partial class SharedFlailProjectile
    {
        private int orbitTarget = -1;
        private int orbitType;
        private bool lassoActive;
        public bool LassoInProgress => Mode == FlailMode.LassoCast || Mode == FlailMode.Wrap;
        private bool hadBinding;
        private int wrapTicks;
        private int blockedWrapTicks;
        private Vector2 previousTargetCenter;
        private bool followingTarget;
        private int trackedTarget = -1;
        private float previousAngle;
        private float wrapStartAngle;
        private float winding;
        private bool trackingAngle;
        private string lassoFailure;

        private static float LoopLength(NPC npc)
        {
            float a = npc.width * 0.5f + 4f;
            float b = npc.height * 0.5f + 4f;
            // Ramanujan's ellipse perimeter: the visible loop consumes real chain.
            return MathHelper.Pi * (3f * (a + b) - MathF.Sqrt((3f * a + b) * (a + 3f * b)));
        }

        private Vector2 OrbitCenter(NPC npc)
        {
            // A large head beside a short NPC otherwise already intersects the
            // floor at the SIDE of the ellipse, before reaching its lower half.
            float lift = Math.Max(0f, (Projectile.height - npc.height) * 0.5f + 4f);
            return npc.Center - Vector2.UnitY * lift;
        }

        private Vector2 OrbitAxes(NPC npc, float y)
        {
            float lower = Math.Max(2f, npc.Bottom.Y - OrbitCenter(npc).Y - Projectile.height * 0.5f - 2f);
            return new Vector2(npc.width * 0.5f + Projectile.width * 0.5f + 4f,
                y >= 0f ? lower : npc.height * 0.5f + Projectile.height * 0.5f + 4f);
        }

        private Vector2 OrbitOffset(NPC npc, float angle)
        {
            Vector2 unit = angle.ToRotationVector2();
            return unit * OrbitAxes(npc, unit.Y);
        }

        private float TargetOrbitRadius(NPC npc)
        {
            Vector2 axes = OrbitAxes(npc, -1f);
            return Math.Max(axes.X, axes.Y + npc.Center.Y - OrbitCenter(npc).Y);
        }

        private void FollowLassoTarget(NPC target)
        {
            if (followingTarget)
            {
                Vector2 movement = target.Center - previousTargetCenter;
                // Follow target translation without counting it as angular travel.
                // Still tile-resolved: this cannot pull the head through a wall.
                if (movement.LengthSquared() <= 128f * 128f)
                    Projectile.position += Collision.TileCollision(Projectile.position, movement,
                        Projectile.width, Projectile.height, false, false);
                else
                {
                    trackingAngle = false;
                    winding = 0f; // Teleports cannot complete an orbit.
                }
            }
            previousTargetCenter = target.Center;
            followingTarget = true;
        }
        private NPC OrbitNPC => orbitTarget >= 0 && orbitTarget < Main.maxNPCs
            && Main.npc[orbitTarget].active && Main.npc[orbitTarget].type == orbitType ? Main.npc[orbitTarget] : null;

        public NPC BoundNPC()
        {
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc.active && npc.GetGlobalNPC<FlailLassoNPC>().Matches(Projectile))
                    return npc;
            }
            return null;
        }

        public bool CanMaintainLasso(NPC npc)
        {
            if (Mode != FlailMode.Wrap || !lassoActive || !FlailLassoNPC.Eligible(npc))
                return false;
            Player player = Main.player[Projectile.owner];
            FlailLassoNPC binding = npc.GetGlobalNPC<FlailLassoNPC>();
            Vector2 headCenter = binding.Matches(Projectile) ? npc.Center + binding.HeadOffset : Projectile.Center;
            return player.active && !player.dead && !player.CCed && !player.noItems
                && player.HeldItem.type == SourceItemType
                && Vector2.Distance(player.MountedCenter, npc.Center) + LoopLength(npc)
                    + Vector2.Distance(npc.Center, headCenter) <= ChainLength
                && Collision.CanHitLine(player.MountedCenter, 1, 1, npc.Center, 1, 1)
                && Collision.CanHitLine(npc.Center, 1, 1, headCenter, 1, 1);
        }

        // A launched lasso owns the attack until the automatic yank/return finishes.
        private bool HandleLassoInput(Player player, SharedFlailPlayer input)
        {
            bool comboPressed = input.ConsumeLassoPress();
            if (Mode == FlailMode.Wrap || Mode == FlailMode.LassoCast)
            {
                // The click latches the whole cast/wrap. Releasing either mouse
                // button must not abort an attack that is already in progress.
                if ((Mode == FlailMode.Wrap && OrbitNPC == null) || !input.CanControl)
                    ChangeMode(FlailMode.Return);
                return true;
            }
            if ((Mode != FlailMode.Spin && Mode != FlailMode.Flight) || !comboPressed)
                return false;
            // Always cast along the aim. Target acquisition happens on the swept
            // flight path, never from a mandatory cursor-hover precondition.
            lassoActive = true;
            ChangeMode(FlailMode.LassoCast);
            float speed = MathHelper.Clamp(player.GetTotalAttackSpeed(DamageClass.Melee), 0.1f, 10f);
            Projectile.velocity = input.Aim * player.HeldItem.shootSpeed * speed * MovementSpeedMultiplier + player.velocity;
            Projectile.netUpdate = true;
            return true;
        }

        private void UpdateLassoCast(Player player)
        {
            Projectile.rotation += 0.25f;
            if (!IsAuthority)
                return;
            NPC chosen = null;
            float nearest = float.MaxValue;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!FlailLassoNPC.Eligible(npc) || npc.GetGlobalNPC<FlailLassoNPC>().Bound)
                    continue;
                float hit = 0f;
                if (!Collision.CheckAABBvLineCollision(npc.position, npc.Size, Projectile.Center,
                    Projectile.Center + Projectile.velocity, Projectile.width, ref hit)
                    || hit >= nearest
                    || !Collision.CanHitLine(Projectile.Center, 1, 1, npc.Center, 1, 1))
                    continue;
                chosen = npc;
                nearest = hit;
            }
            if (chosen == null)
                return;
            if (Vector2.Distance(player.MountedCenter, chosen.Center) + LoopLength(chosen) + TargetOrbitRadius(chosen) > ChainLength)
            {
                lassoFailure = "TooShort";
                ChangeMode(FlailMode.Return);
                return;
            }
            orbitTarget = chosen.whoAmI;
            orbitType = chosen.type;
            ChangeMode(FlailMode.Wrap);
            StateTimer = -MathHelper.PiOver2;
            previousTargetCenter = chosen.Center;
            followingTarget = true;
            Projectile.velocity = Vector2.Zero;
            Projectile.netUpdate = true;
        }

        private NPC SelectLassoTarget(Player player, Vector2 mouse, out string reason)
        {
            NPC nearest = null;
            float distance = float.MaxValue;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!npc.active || npc.friendly || npc.townNPC)
                    continue;
                Rectangle hover = npc.Hitbox;
                hover.Inflate(24, 24);
                float d = Vector2.DistanceSquared(mouse, npc.Center);
                if (hover.Contains(mouse.ToPoint()) && d < distance)
                {
                    nearest = npc;
                    distance = d;
                }
            }
            reason = "NoTarget";
            if (nearest == null)
                return null;
            if (!FlailLassoNPC.Eligible(nearest))
                reason = "Ineligible";
            else if (nearest.GetGlobalNPC<FlailLassoNPC>().Bound)
                reason = "Occupied";
            else if (Vector2.Distance(player.MountedCenter, nearest.Center) + LoopLength(nearest) + TargetOrbitRadius(nearest) > ChainLength)
                reason = "TooShort";
            else if (!Collision.CanHitLine(player.MountedCenter, 1, 1, nearest.Center, 1, 1))
                reason = "Blocked";
            else
            {
                reason = "Ready";
                return nearest;
            }
            return null;
        }

        public string LassoHint(Player player, Vector2 mouse)
        {
            if (Mode == FlailMode.Return)
                return lassoFailure ?? "Returning";
            if (Mode == FlailMode.LassoCast)
                return "Casting";
            if (Mode == FlailMode.Wrap)
            {
                NPC bound = BoundNPC();
                return bound == null ? "Wrapping" : bound.GetGlobalNPC<FlailLassoNPC>().Yanking ? "Yanking" : "Bound";
            }
            if (Mode != FlailMode.Spin)
                return "NeedSpin";
            SelectLassoTarget(player, mouse, out string reason);
            return reason;
        }

        private void LassoAI(Player player, float meleeSpeed)
        {
            NPC target = OrbitNPC;
            NPC bound = BoundNPC();
            if (Projectile.velocity.LengthSquared() > 0.25f)
                blockedWrapTicks = 0;
            if (target == null || !FlailLassoNPC.Eligible(target)
                || Vector2.Distance(player.MountedCenter, target.Center) + LoopLength(target) + TargetOrbitRadius(target) > ChainLength
                || !Collision.CanHitLine(player.MountedCenter, 1, 1, target.Center, 1, 1))
            {
                ChangeMode(FlailMode.Return);
                Projectile.velocity = Vector2.Zero;
                return;
            }
            if (bound != null)
            {
                hadBinding = true;
                Projectile.Center = bound.Center + bound.GetGlobalNPC<FlailLassoNPC>().HeadOffset;
                Projectile.velocity = Vector2.Zero;
                // Normally started in TrackWinding on the binding tick. The
                // server can safely retry here: TryYank is guarded by Yanking.
                if (Main.netMode != NetmodeID.MultiplayerClient)
                    bound.GetGlobalNPC<FlailLassoNPC>().TryYank(bound, this);
            }
            else if (hadBinding || ++wrapTicks > 180)
            {
                ChangeMode(FlailMode.Return);
                Projectile.velocity = Vector2.Zero;
            }
            else
            {
                FollowLassoTarget(target);
                Vector2 wanted = OrbitCenter(target) + OrbitOffset(target, StateTimer);
                // Approach the orbit without teleportation; actual tile-resolved
                // movement, not the desired angle, is what the server measures.
                if (Vector2.DistanceSquared(Projectile.Center, wanted) < 8f * 8f)
                {
                    // A quick automatic orbit after one click, not a slow held
                    // channel. Stay below TrackWinding's discontinuity threshold
                    // even with extreme attack speed so real movement can bind.
                    float angularStep = Math.Min(0.4f, 0.4f * meleeSpeed);
                    StateTimer += angularStep * (Projectile.ai[2] < 0f ? -1f : 1f);
                }
                wanted = OrbitCenter(target) + OrbitOffset(target, StateTimer);
                Vector2 delta = wanted - Projectile.Center;
                Projectile.velocity = delta.SafeNormalize(Vector2.Zero) * Math.Min(32f * meleeSpeed, delta.Length());
                // Clients predict only the visible growing chain. NPC binding
                // remains strictly server-owned.
                TrackWinding(target);
            }
            Projectile.rotation += 0.15f;
        }

        private void TrackWinding(NPC target)
        {
            Vector2 relative = Projectile.Center - OrbitCenter(target);
            Vector2 axes = OrbitAxes(target, relative.Y);
            Vector2 normalized = new Vector2(relative.X / axes.X, relative.Y / axes.Y);
            bool valid = CanMaintainLasso(target) && normalized.Length() >= 0.7f
                && normalized.Length() <= 1.3f;
            if (trackedTarget != target.whoAmI || !valid)
            {
                trackedTarget = target.whoAmI;
                trackingAngle = false;
                winding = 0f;
            }
            if (!valid)
                return;
            float angle = normalized.ToRotation();
            if (!trackingAngle)
                wrapStartAngle = angle;
            if (trackingAngle)
            {
                float step = MathHelper.WrapAngle(angle - previousAngle);
                if (Math.Abs(step) > 0.6f)
                {
                    winding = 0f; // Packet/teleport discontinuity is never a full wrap.
                    wrapStartAngle = angle;
                }
                else
                    winding += step; // Reversals undo winding instead of counting twice.
                if (Math.Abs(winding) >= MathHelper.TwoPi)
                {
                    if (Main.netMode == NetmodeID.MultiplayerClient)
                        winding = Math.Sign(winding) * MathHelper.TwoPi;
                    else if (target.GetGlobalNPC<FlailLassoNPC>().TryBind(target, this))
                    {
                        hadBinding = true;
                        winding = Math.Sign(winding) * MathHelper.TwoPi;
                        // Full physical wrap confirmed on the server: complete
                        // the attack now, without another click or packet.
                        target.GetGlobalNPC<FlailLassoNPC>().TryYank(target, this);
                    }
                    else
                    {
                        winding = 0f;
                        wrapStartAngle = angle;
                    }
                }
            }
            previousAngle = angle;
            trackingAngle = true;
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write((short)orbitTarget);
            writer.Write(orbitType);
            writer.Write(lassoActive);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            int target = reader.ReadInt16();
            int type = reader.ReadInt32();
            bool active = reader.ReadBoolean();
            if (IsAuthority)
                return;
            orbitTarget = target >= 0 && target < Main.maxNPCs ? target : -1;
            orbitType = type;
            lassoActive = active;
            followingTarget = false; // Incoming projectile position already includes target motion.
        }

        private void DrawLasso(Texture2D chain, Vector2 hand, NPC npc, bool bound)
        {
            // Lay down the loop progressively along the measured orbit. Before
            // a turn is complete, never display an already-closed decorative loop.
            float turn = bound ? (Projectile.ai[2] < 0f ? -MathHelper.TwoPi : MathHelper.TwoPi)
                : MathHelper.Clamp(winding, -MathHelper.TwoPi, MathHelper.TwoPi);
            if (!bound && (!trackingAngle || Math.Abs(turn) < 0.01f))
            {
                DrawChainSegment(chain, hand, Projectile.Center);
                return;
            }
            Vector2 radius = npc.Size * 0.5f + new Vector2(4f, 0f);
            Vector2 relative = Projectile.Center - npc.Center;
            float start = bound ? new Vector2(relative.X / radius.X, relative.Y / radius.Y).ToRotation() : wrapStartAngle;
            Vector2 entry = npc.Center + start.ToRotationVector2() * radius;
            // One continuous path: restarting the link phase at every tiny arc
            // segment stacks whole sprites into the thick ring seen in-game.
            var path = new List<Vector2> { hand, entry };
            int count = Math.Max(1, (int)Math.Ceiling(Math.Abs(turn) / MathHelper.TwoPi * 32f));
            for (int i = 1; i <= count; i++)
            {
                float angle = start + turn * i / count;
                Vector2 next = npc.Center + angle.ToRotationVector2() * radius;
                path.Add(next);
            }
            path.Add(Projectile.Center);
            DrawChainPath(chain, path);
        }

        private static void DrawChainSegment(Texture2D chain, Vector2 from, Vector2 to)
        {
            DrawChainPath(chain, new[] { from, to });
        }

        private static void DrawChainPath(Texture2D chain, IReadOnlyList<Vector2> path)
        {
            float spacing = Math.Max(1f, chain.Height - 2f);
            // Centres sit half a step into the path; keep the remaining distance
            // across vertices instead of creating a fresh link at each vertex.
            float untilNextLink = spacing * 0.5f;
            int links = 0;
            for (int segment = 1; segment < path.Count; segment++)
            {
                Vector2 from = path[segment - 1];
                Vector2 delta = path[segment] - from;
                float length = delta.Length();
                if (!float.IsFinite(length))
                    return;
                if (length <= 0f)
                    continue;
                Vector2 direction = delta / length;
                while (untilNextLink < length)
                {
                    if (++links > 200)
                        return; // One budget for the entire path, not per arc segment.
                    Vector2 at = from + direction * untilNextLink;
                    Point tile = at.ToTileCoordinates();
                    Main.EntitySpriteDraw(chain, at - Main.screenPosition, null, Lighting.GetColor(tile.X, tile.Y),
                        direction.ToRotation() + MathHelper.PiOver2, chain.Size() * 0.5f, 1f, SpriteEffects.None, 0f);
                    untilNextLink += spacing;
                }
                untilNextLink -= length;
            }
        }
    }
}
