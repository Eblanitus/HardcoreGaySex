using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Единственная фишка карамельного меча: леденец липкий — удар на
    // полторы секунды тормозит врага. Системы вокруг никакой: один
    // таймер на враге и урезание его скорости каждый тик, пока липкий.
    // Боссов не тормозит. Учёт локальный, как полировка: в мультиплеере
    // клиент гостя фишку не вешает, чтобы не спорить с сервером о позиции.
    public class CandyCaneSlowNPC : GlobalNPC
    {
        private const int SlowTicks = 90;      // 1.5 секунды липкости
        private const float HorizontalMult = 0.8f;
        private const float JumpMult = 0.9f;   // прыжки и взлёты вялее

        public override bool InstancePerEntity => true;

        private int stickyTicks;

        // Попадание карамелькой (зовут OnHitNPC всех четырёх приёмов)
        public static void Apply(NPC npc)
        {
            if (npc == null || !npc.active || npc.friendly || npc.dontTakeDamage || npc.boss)
                return;
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;
            npc.GetGlobalNPC<CandyCaneSlowNPC>().stickyTicks = SlowTicks;
            Crumbs(npc, 6);
        }

        public override void PostAI(NPC npc)
        {
            if (stickyTicks <= 0)
                return;
            stickyTicks--;
            npc.velocity.X *= HorizontalMult;
            if (npc.velocity.Y < 0f)
                npc.velocity.Y *= JumpMult;
            if (stickyTicks % 15 == 0)
                Crumbs(npc, 1);
        }

        // Крошки карамели: красные и белые, цвета самого леденца
        private static void Crumbs(NPC npc, int count)
        {
            if (Main.dedServ)
                return;
            for (int i = 0; i < count; i++)
            {
                Color color = Main.rand.NextBool() ? new Color(230, 45, 65) : new Color(255, 250, 250);
                Dust.NewDustPerfect(
                    npc.Center + Main.rand.NextVector2Circular(npc.width * 0.3f, npc.height * 0.3f),
                    DustID.Snow, Main.rand.NextVector2Circular(1.2f, 1.2f),
                    0, color, Main.rand.NextFloat(0.9f, 1.3f));
            }
        }
    }
}
