using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using FrostMelee.Content.Projectiles;

namespace FrostMelee.Content.Weapons
{
    // Единственная фишка карамельного меча: брошенный леденец лежит и пахнет
    // сладко — враги вокруг агрятся на него и идут к нему, а не к игроку.
    // Через EatTicks лежания леденец «съеден»: бледнеет (отрисовка в
    // SickleThrowProjectile) и манить перестаёт. Новый бросок — снова сладкий.
    // Боссов и червей не манит. Рулёжка, как весь локальный учёт мода,
    // в гостевом мультиплеере выключена, чтобы не спорить с сервером.
    public class CandyCaneLureNPC : GlobalNPC
    {
        public const int EatTicks = 600;          // 10 секунд — и леденец съеден
        private const float LureRadius = 520f;    // откуда враги чуют запах
        private const float MinWalkSpeed = 1.6f;  // медленнее к леденцу не плетутся
        private const float ArriveDistance = 36f; // дошёл — топчется у леденца

        public override void PostAI(NPC npc)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;
            if (npc.boss || npc.friendly || npc.dontTakeDamage || npc.immortal || npc.realLife != -1)
                return;

            // Ближайший живой (несъеденный) леденец в радиусе запаха
            Projectile lure = null;
            float best = LureRadius;
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (!p.active || p.ModProjectile is not SickleThrowProjectile thrown || !thrown.CandyLureActive)
                    continue;
                float d = npc.Distance(p.Center);
                if (d < best)
                {
                    best = d;
                    lure = p;
                }
            }
            if (lure == null)
                return;

            Vector2 to = lure.Center - npc.Center;
            if (to.Length() < ArriveDistance)
            {
                // Добрался до сладкого — топчется на месте, «ест»
                npc.velocity.X *= 0.8f;
                return;
            }

            int dir = to.X >= 0f ? 1 : -1;
            npc.direction = dir;
            npc.spriteDirection = dir;
            if (npc.noGravity)
            {
                float speed = Math.Max(npc.velocity.Length(), MinWalkSpeed);
                npc.velocity = Vector2.Normalize(to) * speed;
            }
            else
            {
                npc.velocity.X = dir * Math.Max(Math.Abs(npc.velocity.X), MinWalkSpeed);
            }
        }
    }
}
