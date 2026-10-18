using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Yoyo
{
    // Кнопки Амарока (ЙОЙО.md):
    // ЛКМ держать — направление, как в игре. Отпустил — рывок домой, если ПКМ свободна.
    // Зажать ПКМ (ЛКМ можно отпустить) — наводка: йо-йо само летит к ближайшему врагу.
    // ЛКМ + зажать ПКМ — круг вокруг персонажа.
    public class AmarokPlayer : ModPlayer
    {
        public const float HomingRange = 260f;   // наводка не дальше верёвки: 0-260 на всех режимах

        public bool CircleActive;    // круг: ЛКМ зажата (канал) + ПКМ зажата
        public bool HomingWanted;    // наводка: ПКМ зажата, ЛКМ отпущена, йо-йо выпущено
        public int HomingTarget = -1;
        public int HomingQuadrant = -1; // текущая четверть курсора (0..3), обновляется каждый тик

        public bool HoldingAmarok =>
            !Player.HeldItem.IsAir && Player.HeldItem.type == ItemID.Amarok;

        // Перчатка надета напрямую или сумкой: ваниль сама ставит yoyoGlove для обеих
        public bool GloveActive() => Player.yoyoGlove;

        public bool Deployed =>
            Player.ownedProjectileCounts[ModContent.ProjectileType<AmarokYoyoProjectile>()] > 0;

        public override void PreUpdate()
        {
            if (Player.whoAmI != Main.myPlayer)
                return;

            CircleActive = false;
            HomingWanted = false;

            if (!HoldingAmarok || Player.dead)
            {
                HomingTarget = -1;
                HomingQuadrant = -1;
                return;
            }

            // Клики по окнам не должны жать кнопки
            bool uiBlocked = Player.mouseInterface || Main.drawingPlayerChat || Main.blockMouse ||
                Main.mapFullscreen || Main.gamePaused || Player.cursorItemIconEnabled ||
                Main.SmartInteractShowingGenuine;
            bool rmb = Main.mouseRight && !uiBlocked;

            // Круг важнее: канал идёт (ЛКМ зажата) + ПКМ зажата
            if (Player.channel && rmb)
            {
                CircleActive = true;
            }
            // Наводка: ПКМ зажата, ЛКМ отпущена — само охотится.
            // Йо-йо не выпущено — вылетает и сразу охотится
            else if (rmb && !Player.channel)
            {
                if (!Deployed)
                {
                    int ownType = ModContent.ProjectileType<AmarokYoyoProjectile>();
                    Vector2 dir = (Main.MouseWorld - Player.Center).SafeNormalize(Vector2.UnitX);
                    Projectile.NewProjectile(Player.GetSource_ItemUse(Player.HeldItem), Player.Center,
                        dir * 12f, ownType, Player.GetWeaponDamage(Player.HeldItem),
                        Player.HeldItem.knockBack, Player.whoAmI);
                }
                HomingWanted = true;
                // Четверть живая: куда курсор смотрит сейчас — там и охота.
                // Увёл курсор в другую четверть — наводка бросает цель
                HomingQuadrant = QuadrantOf(Main.MouseWorld);
                if (!HomingValid())
                    HomingTarget = FindNearest();
            }

            if (!HomingWanted)
            {
                HomingTarget = -1;
                HomingQuadrant = -1;
            }
        }

        // Ближайший враг, которого можно бить, в радиусе наводки от игрока
        // и только в текущей четверти курсора (нерф автоатаки спиной)
        private int FindNearest()
        {
            int best = -1;
            float bestDist = HomingRange;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC n = Main.npc[i];
                if (!n.active || n.friendly || n.dontTakeDamage || n.immortal || n.lifeMax <= 5)
                    continue;
                if (HomingQuadrant >= 0 && QuadrantOf(n.Center) != HomingQuadrant)
                    continue;
                float d = Vector2.Distance(n.Center, Player.Center);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            return best;
        }

        // Четверть экрана относительно игрока: 0 лево-верх, 1 право-верх, 2 лево-низ, 3 право-низ
        private int QuadrantOf(Vector2 world)
        {
            int q = 0;
            if (world.X >= Player.Center.X)
                q |= 1;
            if (world.Y >= Player.Center.Y)
                q |= 2;
            return q;
        }

        public bool HomingValid()
        {
            if (HomingTarget < 0 || HomingTarget >= Main.maxNPCs)
                return false;
            NPC n = Main.npc[HomingTarget];
            if (!n.active || n.friendly || n.dontTakeDamage || n.immortal)
                return false;
            // Цель не в текущей четверти курсора — бросаем
            if (HomingQuadrant >= 0 && QuadrantOf(n.Center) != HomingQuadrant)
                return false;
            return true;
        }

        public void ClearHoming()
        {
            HomingTarget = -1;
        }
    }
}
