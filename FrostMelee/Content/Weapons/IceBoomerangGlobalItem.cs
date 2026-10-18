using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Ледяной бумеранг (БУМЕРАНГИ.md): удар в руке, лёгкий и тяжёлый броски, прицел.
    // Сейчас ванилла 1:1 — механики сюда, когда дойдём.
    public class IceBoomerangGlobalItem : GlobalItem
    {
        public override bool AppliesToEntity(Item entity, bool lateInstantiation) => true;

        private static bool IsBoomerang(Item item) => item.type == ItemID.IceBoomerang;

        public override void SetDefaults(Item item)
        {
            if (!IsBoomerang(item))
                return;
            // TODO(БУМЕРАНГИ.md): удар в руке — короткий взмах в упор;
            // TODO(БУМЕРАНГИ.md): лёгкий бросок — слабый, возвращается, проходит насквозь;
            // TODO(БУМЕРАНГИ.md): тяжёлый бросок — заряжается, втыкается, идти забирать;
            // TODO(БУМЕРАНГИ.md): прицел — линия полёта и что будет после отскока;
            // TODO(БУМЕРАНГИ.md): сквозь стены не летит ничего.
        }
    }
}
