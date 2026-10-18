using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Скрытый снаряд-художник: не виден сам, но своим PreDraw рисует
    // ледяную дорожку (IceSheetSystem.DrawTrail) внутри ванильного батча
    // снарядов — конвейера, где полупрозрачность в этой сборке доказанно
    // работает (им же рисуются кроссфейды клинков мода).
    //
    // Живёт, пока есть отметины льда; когда дорожка растаяла — убивается сам.
    public class IceSheetDrawer : ModProjectile
    {
        public override string Texture => "FrostMelee/Textures/Effects/IceCoat";

        public override void SetDefaults()
        {
            Projectile.width = 2;
            Projectile.height = 2;
            Projectile.friendly = false;
            Projectile.hostile = false;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 30;
            Projectile.damage = 0;
        }

        public override void AI()
        {
            IceSheetSystem.PurgeExpired();

            if (!IceSheetSystem.Any)
            {
                Projectile.Kill();
                return;
            }

            // Продлеваем себя, пока есть что рисовать
            Projectile.timeLeft = 30;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            IceSheetSystem.DrawTrail();
            return false; // сам снаряд не рисуется
        }
    }
}
