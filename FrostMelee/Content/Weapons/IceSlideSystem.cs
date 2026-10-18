using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace FrostMelee.Content.Weapons
{
    // Скольжение в режиме копья с ледяным клинком.
    // Появляется только когда игрок уже разогнался, а не просто постоял с копьём.
    public static class IceSlideSystem
    {
        // Порог скорости, ниже которого скольжения нет (px/тик)
        public const float MinSpeed = 4f;
        // Сколько силы скольжения: трение ближе к 1 = скользит дольше
        public const float SlideFriction = 0.985f;
        // Макс доп скорость от скольжения
        public const float SlideBoost = 1.15f;

        public static bool TrySlide(Player player, SwordPlayer sp)
        {
            if (!sp.HoldingSword || player.HeldItem.type != ItemID.IceBlade) return false;
            if (!sp.SpearMode) return false;
            if (player.dead) return false;

            // Уже разогнался — проверка по вектору вдоль копья, а не просто стоял
            Vector2 dir = Vector2.UnitX;
            if (sp.TryGetSpearDir(out float angle))
                dir = angle.ToRotationVector2();
            else
                dir = new Vector2(player.direction, 0f);

            float along = Vector2.Dot(player.velocity, dir);
            if (along < MinSpeed) return false;
            if (!player.onGround && !player.slippy) {
                // В воздухе скольжения нет — только по земле/льду
                // но разрешаем если уже скользим
                if (player.velocity.Y != 0 && System.Math.Abs(player.velocity.Y) > 0.5f) return false;
            }

            // Включаем скольжение
            player.slippy = true; // ванильный флаг льда — уменьшает трение
            player.maxRunSpeed *= SlideBoost;
            player.runAcceleration *= 1.05f;

            // Визуал: крошка льда из-под ног
            if (Main.rand.NextBool(3)) {
                Vector2 foot = player.Bottom + new Vector2(Main.rand.NextFloat(-6f, 6f), -2f);
                Dust d = Dust.NewDustPerfect(foot, DustID.SnowflakeIce, new Vector2(player.velocity.X * 0.1f, -0.4f), 120, default, 0.9f);
                d.noGravity = true;
            }

            return true;
        }
    }
}
