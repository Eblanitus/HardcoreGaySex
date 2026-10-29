using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using FrostMelee.Content.Items.Prototypes;
using FrostMelee.Content.Projectiles;

namespace FrostMelee.Content.Weapons
{
    // Clients send input, not projectile state. Only the server/SP creates heads.
    public class SharedFlailPlayer : ModPlayer
    {
        public const int RepeatDelay = 10;
        public bool CanContinueAttack => Player.active && !Player.dead && !Player.ghost
            && !Player.noItems && !Player.CCed && Player.HeldItem.ModItem is PrototypeFlail;
        public bool CanControl { get; private set; }
        public bool LeftHeld { get; private set; }
        public bool RightHeld { get; private set; }
        public Vector2 Aim { get; private set; } = Vector2.UnitX;
        public Vector2 AimWorld => Player.MountedCenter + Aim * 240f;
        private bool previousLeft, pendingLassoPress;
        private int repeatCooldown, sendTicks, inputAge, inputSlot = -1, inputItem;
        private uint sequence, receivedSequence;
        private bool receivedInput;

        public SharedFlailProjectile ActiveHead
        {
            get
            {
                for (int i = 0; i < Main.maxProjectiles; i++)
                {
                    Projectile p = Main.projectile[i];
                    if (p.active && p.owner == Player.whoAmI && p.ModProjectile is SharedFlailProjectile head)
                        return head;
                }
                return null;
            }
        }

        public string LastFailure { get; private set; } = "Returning";
        public int FailureDisplayTicks { get; private set; }
        public void ReportFailure(string reason) { LastFailure = reason; FailureDisplayTicks = 180; }

        public bool ConsumeLassoPress()
        {
            bool result = pendingLassoPress;
            pendingLassoPress = false;
            return result;
        }

        public override void SetControls()
        {
            if (Player.whoAmI == Main.myPlayer && Player.HeldItem.ModItem is PrototypeFlail
                && Main.mouseRight && !Player.mouseInterface && !Main.drawingPlayerChat && !Main.blockMouse && !Main.mapFullscreen)
                Player.controlUseTile = false;
        }

        public override void PostUpdate()
        {
            if (FailureDisplayTicks > 0) FailureDisplayTicks--;
            if (Player.whoAmI == Main.myPlayer)
            {
                bool allowed = CanContinueAttack && !Player.mouseInterface && !Main.drawingPlayerChat
                    && !Main.blockMouse && !Main.mapFullscreen && !Main.gamePaused;
                bool left = allowed && Main.mouseLeft, right = allowed && Main.mouseRight;
                bool cast = right && left && !previousLeft && ActiveHead?.LassoInProgress != true;
                previousLeft = Main.mouseLeft; // Leaving UI cannot manufacture a press.
                Vector2 aim = (Main.MouseWorld - Player.MountedCenter).SafeNormalize(new Vector2(Player.direction, 0f));
                if (inputSlot != Player.selectedItem || inputItem != Player.HeldItem.type) pendingLassoPress = false;
                bool changed = left != LeftHeld || right != RightHeld || cast || allowed != CanControl
                    || inputSlot != Player.selectedItem || inputItem != Player.HeldItem.type;
                CanControl = allowed;
                LeftHeld = left;
                RightHeld = right;
                Aim = aim;
                inputSlot = Player.selectedItem;
                inputItem = Player.HeldItem.type;
                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    if (changed || (++sendTicks >= 6 && Player.HeldItem.ModItem is PrototypeFlail))
                    {
                        sendTicks = 0;
                        ModPacket packet = Mod.GetPacket();
                        packet.Write(global::FrostMelee.FrostMelee.FlailInputMessage);
                        packet.Write(++sequence);
                        packet.Write((byte)((left ? 1 : 0) | (right ? 2 : 0) | (cast ? 4 : 0) | (allowed ? 8 : 0)));
                        packet.Write((byte)inputSlot);
                        packet.Write(inputItem);
                        packet.Write(aim.X);
                        packet.Write(aim.Y);
                        packet.Send();
                    }
                    return;
                }
                if (!allowed) pendingLassoPress = false;
                if (cast) pendingLassoPress = true;
            }
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;
            if (Main.netMode == NetmodeID.Server && ++inputAge > 60)
            {
                LeftHeld = RightHeld = CanControl = false;
                pendingLassoPress = false;
            }
            if (!CanContinueAttack || inputSlot != Player.selectedItem || inputItem != Player.HeldItem.type)
            {
                LeftHeld = RightHeld = CanControl = false;
                pendingLassoPress = false;
            }
            SharedFlailProjectile active = ActiveHead;
            if (active != null)
            {
                repeatCooldown = RepeatDelay;
                return;
            }
            if (repeatCooldown > 0) repeatCooldown--;
            if (!CanControl || repeatCooldown > 0 || (!LeftHeld && !RightHeld && !pendingLassoPress))
                return;
            Item held = Player.HeldItem;
            var mode = RightHeld || pendingLassoPress ? SharedFlailProjectile.FlailMode.Spin : SharedFlailProjectile.FlailMode.Flight;
            Projectile.NewProjectile(Player.GetSource_ItemUse(held), Player.MountedCenter, Vector2.Zero,
                held.shoot, Player.GetWeaponDamage(held), Player.GetWeaponKnockback(held), Player.whoAmI, (float)mode);
            repeatCooldown = RepeatDelay;
        }

        public void ReceiveInput(BinaryReader reader)
        {
            uint serial = reader.ReadUInt32();
            byte flags = reader.ReadByte();
            int slot = reader.ReadByte(), item = reader.ReadInt32();
            Vector2 aim = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            if (Main.netMode != NetmodeID.Server || !Player.active || (flags & ~15) != 0
                || slot >= Player.inventory.Length || !float.IsFinite(aim.X) || !float.IsFinite(aim.Y)
                || aim.LengthSquared() < 0.25f || aim.LengthSquared() > 4f
                || (receivedInput && unchecked((int)(serial - receivedSequence)) <= 0))
                return;
            receivedInput = true;
            receivedSequence = serial;
            inputAge = 0;
            if (inputSlot != slot || inputItem != item) pendingLassoPress = false;
            inputSlot = slot;
            inputItem = item;
            CanControl = (flags & 8) != 0;
            if (!CanControl) pendingLassoPress = false;
            LeftHeld = CanControl && (flags & 1) != 0;
            RightHeld = CanControl && (flags & 2) != 0;
            Aim = Vector2.Normalize(aim);
            if (CanControl && LeftHeld && RightHeld && (flags & 4) != 0 && ActiveHead?.LassoInProgress != true)
                pendingLassoPress = true;
        }
    }
}
