using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using FrostMelee.Content.Weapons;

namespace FrostMelee.Content.Projectiles
{
    public abstract partial class SharedFlailProjectile
    {
        public bool LassoInProgress => Mode == FlailMode.LassoCast || Mode == FlailMode.Approach
            || Mode == FlailMode.Wrap || Mode == FlailMode.Yank;
        private int orbitTarget = -1, orbitType, blockedWrapTicks;
        private float loopX, loopUpper, loopLower, loopLift, orbitStartAngle, winding;
        private Vector2 previousTargetCenter;
        private byte failure;
        private static readonly string[] FailureNames = { "Returning", "Blocked", "TooShort", "Interrupted", "LostTarget", "Timeout", "Occupied" };
        internal static string FailureName(int value) => FailureNames[Math.Clamp(value, 0, FailureNames.Length - 1)];
        private NPC OrbitNPC => orbitTarget >= 0 && orbitTarget < Main.maxNPCs
            && Main.npc[orbitTarget].active && Main.npc[orbitTarget].type == orbitType ? Main.npc[orbitTarget] : null;
        public NPC BoundNPC() => OrbitNPC != null && OrbitNPC.GetGlobalNPC<FlailLassoNPC>().Matches(Projectile) ? OrbitNPC : null;
        private Vector2 LoopPoint(NPC target, float angle)
        {
            Vector2 unit = angle.ToRotationVector2();
            return target.Center - Vector2.UnitY * loopLift + new Vector2(unit.X * loopX, unit.Y * (unit.Y >= 0 ? loopLower : loopUpper));
        }
        private void SetLoopGeometry(NPC target)
        {
            loopX = target.width * 0.5f + Projectile.width * 0.5f + 4f;
            loopUpper = target.height * 0.5f + Projectile.height * 0.5f + 4f;
            loopLower = loopUpper;
            loopLift = 0f;
            Vector2 probe = target.Center + Vector2.UnitY * loopLower;
            if (Collision.SolidCollision(probe - Projectile.Size * 0.5f, Projectile.width, Projectile.height)
                || GroundedAt(target.Bottom, 1f))
            {
                loopLift = Math.Max(0, (Projectile.height - target.height) * 0.5f + 4f);
                loopLower = Math.Max(2f, target.height * 0.5f + loopLift - Projectile.height * 0.5f - 2f);
            }
            orbitStartAngle = -MathHelper.PiOver2;
        }
        private float FullLoopLength(NPC target)
        {
            float length = 0;
            Vector2 last = LoopPoint(target, orbitStartAngle);
            for (int i = 1; i <= 64; i++)
            {
                Vector2 next = LoopPoint(target, orbitStartAngle + MathHelper.TwoPi * i / 64f);
                length += Vector2.Distance(last, next);
                last = next;
            }
            return length;
        }
        private bool HasChainBudget(NPC target) => Vector2.Distance(Main.GetPlayerArmPosition(Projectile),
            LoopPoint(target, orbitStartAngle)) + FullLoopLength(target) <= ChainLength;
        private bool ChainVisible(NPC target) => Collision.CanHitLine(Main.player[Projectile.owner].MountedCenter,
            1, 1, LoopPoint(target, orbitStartAngle), 1, 1);

        public bool CanMaintainLasso(NPC npc) => (Mode == FlailMode.Wrap || Mode == FlailMode.Yank)
            && OrbitNPC == npc && FlailLassoNPC.Eligible(npc)
            && Main.player[Projectile.owner].GetModPlayer<SharedFlailPlayer>().CanContinueAttack
            && StillHeld(Main.player[Projectile.owner])
            && HasChainBudget(npc) && ChainVisible(npc);

        private void AbortLasso(string reason)
        {
            if (!IsAuthority || Mode == FlailMode.Return) return;
            int index = Array.IndexOf(FailureNames, reason);
            failure = (byte)(LassoInProgress ? Math.Max(0, index) : 0);
            if (failure > 0)
            {
                if (Main.netMode == NetmodeID.Server)
                {
                    var packet = Mod.GetPacket();
                    packet.Write(global::FrostMelee.FrostMelee.FlailFailureMessage);
                    packet.Write(failure);
                    packet.Send(Projectile.owner);
                }
                else Main.player[Projectile.owner].GetModPlayer<SharedFlailPlayer>().ReportFailure(FailureNames[failure]);
                Mod.Logger.Debug($"Flail cancelled: owner={Projectile.owner}, mode={Mode}, target={orbitTarget}, reason={reason}, winding={winding}");
            }
            ChangeMode(FlailMode.Return);
        }
        private bool HandleLassoInput(Player player, SharedFlailPlayer input)
        {
            bool cast = input.ConsumeLassoPress();
            if (LassoInProgress) return true; // Additional presses do nothing.
            if (!cast || (Mode != FlailMode.Spin && Mode != FlailMode.Flight)) return false;
            ChangeMode(FlailMode.LassoCast);
            RampTicks = 0;
            Launch(player, input.Aim);
            return true;
        }
        private void AcquireLassoTarget(Player player)
        {
            // Test only the path the tile solver actually allowed. A target
            // behind a wall can never be acquired by a speculative long ray.
            NPC chosen = null;
            Vector2 point = Projectile.Center;
            for (int segment = 1; segment < damagePath.Count && chosen == null; segment++)
            {
                float nearest = float.MaxValue;
                Vector2 from = damagePath[segment - 1], to = damagePath[segment];
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    NPC npc = Main.npc[i];
                    if (!FlailLassoNPC.Eligible(npc) || npc.GetGlobalNPC<FlailLassoNPC>().Bound) continue;
                    float hit = 0;
                    if (Collision.CheckAABBvLineCollision(npc.position, npc.Size, from, to, Projectile.width, ref hit) && hit < nearest)
                    {
                        chosen = npc;
                        nearest = hit;
                        point = from + (to - from).SafeNormalize(Vector2.Zero) * Math.Min(hit, Vector2.Distance(from, to));
                    }
                }
            }
            if (chosen == null) return;
            orbitTarget = chosen.whoAmI;
            orbitType = chosen.type;
            SetLoopGeometry(chosen);
            if (!HasChainBudget(chosen)) { AbortLasso("TooShort"); return; }
            if (!ChainVisible(chosen)) { AbortLasso("Blocked"); return; }
            Projectile.Center = point;
            previousTargetCenter = chosen.Center;
            Phase = orbitStartAngle;
            winding = 0;
            Projectile.velocity = Vector2.Zero;
            ChangeMode(FlailMode.Approach);
        }

        private void LassoAI(Player player, float speed)
        {
            NPC target = OrbitNPC;
            if (target == null || !FlailLassoNPC.Eligible(target)) { AbortLasso("LostTarget"); return; }
            if (!HasChainBudget(target)) { AbortLasso("TooShort"); return; }
            if (!ChainVisible(target))
            {
                if (++blockedWrapTicks >= 12) AbortLasso("Blocked");
                return; // Keep progress during transient occlusion, never advance it.
            }
            if (Mode == FlailMode.Yank)
            {
                var binding = target.GetGlobalNPC<FlailLassoNPC>();
                if (IsAuthority && !binding.Matches(Projectile)) { EndLasso(); return; }
                if (binding.Matches(Projectile)) Projectile.Center = target.Center + binding.HeadOffset;
                Projectile.velocity = Vector2.Zero;
                return;
            }
            float angularSpeed = Math.Max(0.01f, Math.Min(0.4f, 0.4f * speed));
            float wrapLimit = Math.Max(180f, MathF.Ceiling(MathHelper.TwoPi / angularSpeed) + 60f);
            if (StateTimer > (Mode == FlailMode.Approach ? 60 : wrapLimit)) { AbortLasso("Timeout"); return; }
            // Carry the orbit with the NPC using the very same tile solver.
            Vector2 translation = target.Center - previousTargetCenter;
            if (translation.LengthSquared() > 128f * 128f) { AbortLasso("LostTarget"); return; }
            Vector2 before = Projectile.Center;
            bool followed = MoveHead(Projectile.Center + translation, false);
            previousTargetCenter += Projectile.Center - before;
            if (!followed) { if (++blockedWrapTicks >= 12) AbortLasso("Blocked"); return; }

            bool reached;
            if (Mode == FlailMode.Approach)
            {
                Vector2 entry = LoopPoint(target, orbitStartAngle);
                reached = MoveHead(Projectile.Center.MoveTowards(entry, 32f * speed), false);
                if (reached && Vector2.DistanceSquared(Projectile.Center, entry) <= 1f)
                {
                    // Approach is NOT part of winding. No radius-band test can
                    // reset a genuine wrap just because entry took several ticks.
                    winding = 0;
                    Phase = orbitStartAngle;
                    ChangeMode(FlailMode.Wrap);
                }
            }
            else
            {
                reached = true;
                float remaining = Math.Min(0.4f * speed, 0.4f);
                remaining = Math.Min(remaining, MathHelper.TwoPi - winding);
                int slices = Math.Max(1, (int)Math.Ceiling(remaining / 0.05f));
                float slice = remaining / slices;
                for (int i = 0; i < slices; i++)
                {
                    float angle = Phase + slice * facing;
                    if (!MoveHead(LoopPoint(target, angle), false)) { reached = false; break; }
                    Phase = angle;
                    winding = Math.Min(MathHelper.TwoPi, winding + slice);
                }
                if (IsAuthority && winding >= MathHelper.TwoPi - 0.0001f)
                {
                    var binding = target.GetGlobalNPC<FlailLassoNPC>();
                    if (binding.TryBind(target, this))
                    {
                        winding = MathHelper.TwoPi;
                        ChangeMode(FlailMode.Yank);
                        binding.TryYank(target, this);
                    }
                    else AbortLasso("Occupied");
                }
            }
            if (reached) blockedWrapTicks = 0;
            else if (++blockedWrapTicks >= 12) AbortLasso("Blocked");
            Projectile.velocity = Vector2.Zero;
            Projectile.rotation = Phase;
        }

        public string LassoHint(Player player, Vector2 mouse)
        {
            if (Mode == FlailMode.Return) return FailureNames[failure];
            if (Mode == FlailMode.LassoCast) return "Casting";
            if (Mode == FlailMode.Approach) return "Approaching";
            if (Mode == FlailMode.Wrap) return "Wrapping";
            if (Mode == FlailMode.Yank) return "Yanking";
            return "NoTarget";
        }
        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write((short)orbitTarget);
            writer.Write(orbitType);
            writer.Write((sbyte)facing);
            writer.Write((ushort)Math.Min(ushort.MaxValue, SpinTicks));
            writer.Write((byte)RampTicks);
            writer.Write(failure);
            writer.Write(loopX); writer.Write(loopUpper); writer.Write(loopLower); writer.Write(loopLift);
            writer.Write(orbitStartAngle); writer.Write(winding);
        }
        public override void ReceiveExtraAI(BinaryReader reader)
        {
            int target = reader.ReadInt16(), type = reader.ReadInt32(), face = reader.ReadSByte();
            int ticks = reader.ReadUInt16(), ramp = reader.ReadByte(), reason = reader.ReadByte();
            float x = reader.ReadSingle(), upper = reader.ReadSingle(), lower = reader.ReadSingle(), lift = reader.ReadSingle();
            float start = reader.ReadSingle(), progress = reader.ReadSingle();
            if (IsAuthority) return; // No client can overwrite the server's state.
            if (!float.IsFinite(x) || !float.IsFinite(upper) || !float.IsFinite(lower) || !float.IsFinite(lift)
                || !float.IsFinite(start) || !float.IsFinite(progress)) return;
            orbitTarget = target >= 0 && target < Main.maxNPCs ? target : -1;
            orbitType = type;
            facing = face < 0 ? -1 : 1;
            SpinTicks = ticks; RampTicks = Math.Min(90, ramp);
            failure = (byte)Math.Clamp(reason, 0, FailureNames.Length - 1);
            loopX = Math.Clamp(x, 0, 1000); loopUpper = Math.Clamp(upper, 0, 1000);
            loopLower = Math.Clamp(lower, 0, 1000); loopLift = Math.Clamp(lift, 0, 1000);
            orbitStartAngle = start; winding = Math.Clamp(progress, 0, MathHelper.TwoPi);
            if (OrbitNPC != null) previousTargetCenter = OrbitNPC.Center;
        }
        private void DrawLasso(Texture2D chain, Vector2 hand, NPC target)
        {
            if (Mode == FlailMode.Approach || winding <= 0)
            {
                DrawChainSegment(chain, hand, Projectile.Center);
                return;
            }
            // Movement, visible path AND chain-length budget use LoopPoint.
            var path = new List<Vector2> { hand, LoopPoint(target, orbitStartAngle) };
            int count = Math.Max(1, (int)Math.Ceiling(winding / 0.05f));
            for (int i = 1; i <= count; i++)
                path.Add(LoopPoint(target, orbitStartAngle + winding * facing * i / count));
            path.Add(Projectile.Center);
            DrawChainPath(chain, path);
        }
        private static void DrawChainSegment(Texture2D chain, Vector2 from, Vector2 to) => DrawChainPath(chain, new[] { from, to });
        private static void DrawChainPath(Texture2D chain, IReadOnlyList<Vector2> path)
        {
            float spacing = Math.Max(1f, chain.Height - 2f);
            float untilNextLink = spacing * 0.5f;
            int links = 0;
            for (int segment = 1; segment < path.Count; segment++)
            {
                Vector2 from = path[segment - 1], delta = path[segment] - from;
                float length = delta.Length();
                if (!float.IsFinite(length)) return;
                if (length <= 0) continue;
                Vector2 direction = delta / length;
                while (untilNextLink < length)
                {
                    if (++links > 200) return;
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
