using System;
using System.IO;
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
        private uint yankRequest;
        private uint handledYank;
        private bool lassoHeld;
        private bool hadBinding;
        private int wrapTicks;
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

        private Vector2 OrbitAxes(NPC npc, float y)
        {
            // Like the player spin, flatten the lower half so a ground enemy
            // can be encircled without sending the head deep into the floor.
            float lower = Math.Max(2f, npc.height * 0.5f - Projectile.height * 0.5f - 2f);
            return new Vector2(npc.width * 0.5f + Projectile.width * 0.5f + 4f,
                y >= 0f ? lower : npc.height * 0.5f + Projectile.height * 0.5f + 4f);
        }

        private Vector2 OrbitOffset(NPC npc, float angle)
        {
            Vector2 unit = angle.ToRotationVector2();
            return unit * OrbitAxes(npc, unit.Y);
        }

        private float TargetOrbitRadius(NPC npc) => npc.Size.Length() * 0.5f + Projectile.width * 0.5f + 4f;
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
            if (Mode != FlailMode.Wrap || !lassoHeld || !FlailLassoNPC.Eligible(npc))
                return false;
            Player player = Main.player[Projectile.owner];
            return player.active && !player.dead && !player.CCed && !player.noItems
                && player.HeldItem.type == SourceItemType
                && Vector2.Distance(player.MountedCenter, npc.Center) + LoopLength(npc)
                    + Vector2.Distance(npc.Center, Projectile.Center) <= ChainLength
                && Collision.CanHitLine(player.MountedCenter, 1, 1, npc.Center, 1, 1)
                && Collision.CanHitLine(npc.Center, 1, 1, Projectile.Center, 1, 1);
        }

        // Called before the base drop-click handler, so yank has priority over drop.
        private bool HandleLassoInput(Player player, SharedFlailPlayer input)
        {
            if (lassoHeld != input.RightHeld)
            {
                lassoHeld = input.RightHeld;
                Projectile.netUpdate = true;
            }
            bool comboPressed = input.ConsumeLassoPress();
            if (Mode == FlailMode.Wrap || Mode == FlailMode.LassoCast)
            {
                if (!input.RightHeld || (Mode == FlailMode.Wrap && OrbitNPC == null) || !input.CanControl)
                    ChangeMode(FlailMode.Return);
                else if (comboPressed && BoundNPC() != null)
                {
                    yankRequest++;
                    Projectile.netUpdate = true;
                }
                return true;
            }
            if ((Mode != FlailMode.Spin && Mode != FlailMode.Flight) || !input.RightHeld || !comboPressed)
                return false;
            // Always cast along the aim. Target acquisition happens on the swept
            // flight path, never from a mandatory cursor-hover precondition.
            ChangeMode(FlailMode.LassoCast);
            float speed = MathHelper.Clamp(player.GetTotalAttackSpeed(DamageClass.Melee), 0.1f, 10f);
            Projectile.velocity = input.Aim * player.HeldItem.shootSpeed * speed + player.velocity;
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
                if (Main.netMode != NetmodeID.MultiplayerClient && yankRequest != handledYank)
                    bound.GetGlobalNPC<FlailLassoNPC>().TryYank(bound, this);
            }
            else if (hadBinding || ++wrapTicks > 180)
            {
                ChangeMode(FlailMode.Return);
                Projectile.velocity = Vector2.Zero;
            }
            else
            {
                Vector2 wanted = target.Center + OrbitOffset(target, StateTimer);
                // Approach the orbit without teleportation; actual tile-resolved
                // movement, not the desired angle, is what the server measures.
                if (Vector2.DistanceSquared(Projectile.Center, wanted) < 12f * 12f)
                    StateTimer += 0.12f * meleeSpeed * (Projectile.ai[2] < 0f ? -1f : 1f);
                wanted = target.Center + OrbitOffset(target, StateTimer);
                Vector2 delta = wanted - Projectile.Center;
                Projectile.velocity = delta.SafeNormalize(Vector2.Zero) * Math.Min(12f * meleeSpeed, delta.Length());
                // Clients predict only the visible growing chain. NPC binding
                // remains strictly server-owned.
                TrackWinding(target);
            }
            if (Main.netMode != NetmodeID.MultiplayerClient)
                handledYank = yankRequest; // No queued pre-capture yank can trigger later.
            Projectile.rotation += 0.15f;
        }

        private void TrackWinding(NPC target)
        {
            Vector2 relative = Projectile.Center - target.Center;
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
            writer.Write(yankRequest);
            writer.Write(lassoHeld);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            int target = reader.ReadInt16();
            int type = reader.ReadInt32();
            uint request = reader.ReadUInt32();
            bool held = reader.ReadBoolean();
            if (IsAuthority)
                return;
            orbitTarget = target >= 0 && target < Main.maxNPCs ? target : -1;
            orbitType = type;
            yankRequest = request;
            lassoHeld = held;
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
            DrawChainSegment(chain, hand, entry);
            int count = Math.Max(1, (int)Math.Ceiling(Math.Abs(turn) / MathHelper.TwoPi * 32f));
            Vector2 last = entry;
            for (int i = 1; i <= count; i++)
            {
                float angle = start + turn * i / count;
                Vector2 next = npc.Center + angle.ToRotationVector2() * radius;
                DrawChainSegment(chain, last, next);
                last = next;
            }
            DrawChainSegment(chain, last, Projectile.Center);
        }

        private static void DrawChainSegment(Texture2D chain, Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            float length = delta.Length();
            if (!float.IsFinite(length) || length < 0.1f)
                return;
            float spacing = Math.Max(1f, chain.Height - 2f);
            int links = Math.Min(200, (int)Math.Ceiling(length / spacing));
            Vector2 direction = delta / length;
            for (int i = 0; i < links; i++)
            {
                Vector2 at = from + direction * (i * spacing);
                Point tile = at.ToTileCoordinates();
                Main.EntitySpriteDraw(chain, at - Main.screenPosition, null, Lighting.GetColor(tile.X, tile.Y),
                    direction.ToRotation() + MathHelper.PiOver2, chain.Size() * 0.5f, 1f, SpriteEffects.None, 0f);
            }
        }
    }
}
