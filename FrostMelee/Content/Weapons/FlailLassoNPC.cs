using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using FrostMelee.Content.Projectiles;

namespace FrostMelee.Content.Weapons
{
    // Restraint and impact damage are server-owned. Client projectile snapshots
    // cannot invent a binding or charge the snow resource.
    public class FlailLassoNPC : GlobalNPC
    {
        public override bool InstancePerEntity => true;
        public int Owner { get; private set; } = -1;
        public int HeadIdentity { get; private set; } = -1;
        public bool Yanking { get; private set; }
        private int ticks;
        private float pulledDistance;
        private Vector2 anchor;
        public Vector2 HeadOffset { get; private set; }
        public bool Bound => Owner >= 0;
        public static bool Eligible(NPC npc) => npc.active && npc.life > 0 && !npc.friendly
            && !npc.townNPC && !npc.boss && !npc.dontTakeDamage && !npc.immortal
            && npc.realLife < 0 && npc.knockBackResist > 0f && npc.CanBeChasedBy();
        public bool Matches(Projectile head) => Bound && Owner == head.owner && HeadIdentity == head.identity;
        public bool TryBind(NPC npc, SharedFlailProjectile head)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient || Bound || !Eligible(npc) || !head.CanMaintainLasso(npc)) return false;
            Owner = head.Projectile.owner;
            HeadIdentity = head.Projectile.identity;
            anchor = npc.position;
            HeadOffset = head.Projectile.Center - npc.Center;
            pulledDistance = 0;
            ticks = 18;
            Yanking = false;
            npc.velocity = Vector2.Zero;
            npc.netUpdate = true;
            return true;
        }
        public void TryYank(NPC npc, SharedFlailProjectile head)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient || !Matches(head.Projectile)
                || Yanking || !head.CanMaintainLasso(npc)) return;
            Yanking = true;
            ticks = 18;
            npc.netUpdate = true;
            // No damage or snow shatter here. This only starts actual movement.
        }
        private SharedFlailProjectile FindHead()
        {
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && Matches(p) && p.ModProjectile is SharedFlailProjectile head) return head;
            }
            return null;
        }
        private void Release(NPC npc, SharedFlailProjectile head, bool impact, string reason = "Interrupted")
        {
            // Clear binding BEFORE damage/hooks: lethal impacts cannot recursively
            // repeat it, and the next NPC update always sees a released target.
            Owner = HeadIdentity = -1;
            Yanking = false;
            ticks = 0;
            npc.velocity = Vector2.Zero;
            npc.netUpdate = true;
            if (impact) head?.CompleteLassoImpact(npc);
            head?.EndLasso(impact ? "Returning" : reason);
        }
        public override bool PreAI(NPC npc)
        {
            if (!Bound) return true;
            SharedFlailProjectile head = FindHead();
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                if (!Eligible(npc) || head == null || !head.CanMaintainLasso(npc))
                {
                    Release(npc, head, false);
                    return true;
                }
                if (Yanking)
                {
                    Vector2 delta = Main.player[Owner].MountedCenter - npc.Center;
                    if (delta.LengthSquared() <= 40f * 40f)
                    {
                        Release(npc, head, true);
                        return true;
                    }
                    if (--ticks < 0)
                    {
                        Release(npc, head, pulledDistance > 1f, "Timeout");
                        return true;
                    }
                    // Sweep in short steps so fast pulls cannot tunnel a narrow
                    // wall. Clipped Y is allowed when X can still slide on a floor.
                    Vector2 wanted = delta.SafeNormalize(Vector2.Zero) * head.LassoPullSpeed;
                    int slices = System.Math.Max(1, (int)System.Math.Ceiling(wanted.Length() / 4f));
                    bool moved = false;
                    for (int i = 0; i < slices; i++)
                    {
                        Vector2 step = Collision.TileCollision(anchor, wanted / slices, npc.width, npc.height, false, false);
                        if (step.LengthSquared() <= 0.01f || Collision.SolidCollision(anchor + step, npc.width, npc.height)) break;
                        anchor += step;
                        pulledDistance += step.Length();
                        moved = true;
                    }
                    if (!moved)
                    {
                        Release(npc, head, false, "Blocked"); // Blocked pull never spends snow.
                        return true;
                    }
                    npc.netUpdate = true;
                }
            }
            npc.position = anchor;
            npc.velocity = Vector2.Zero;
            return false;
        }
        public override void SendExtraAI(NPC npc, BitWriter bitWriter, BinaryWriter writer)
        {
            writer.Write((short)Owner); writer.Write(HeadIdentity); writer.Write(Yanking);
            writer.Write(anchor.X); writer.Write(anchor.Y); writer.Write(HeadOffset.X); writer.Write(HeadOffset.Y);
        }
        public override void ReceiveExtraAI(NPC npc, BitReader bitReader, BinaryReader reader)
        {
            int owner = reader.ReadInt16(), identity = reader.ReadInt32();
            bool yanking = reader.ReadBoolean();
            Vector2 position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            Vector2 offset = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            if (Main.netMode != NetmodeID.MultiplayerClient) return;
            Owner = owner >= 0 && owner < Main.maxPlayers && float.IsFinite(position.X) && float.IsFinite(position.Y)
                && float.IsFinite(offset.X) && float.IsFinite(offset.Y) ? owner : -1;
            HeadIdentity = identity; Yanking = yanking; anchor = position; HeadOffset = offset;
        }
    }
}
