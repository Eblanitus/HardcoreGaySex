using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace FrostMelee.Content.Weapons
{
    // Скольжение в режиме копья с ледяным клинком.
    // Появляется только когда игрок уже разогнался, а не просто постоял с копьём.
    //
    // ВАЖНО: этот класс — только ПРОВЕРКА («можно ли скользить»), без побочных
    // эффектов. Сам эффект выставляет IceSlidePlayer из PostUpdateRunSpeeds.
    //
    // Почему не здесь: ванильный Player.Update каждый кадр пересчитывает поля
    // движения. Порядок такой (номера строк ванильного Player.cs):
    //   ~14652  ResetEffects()            — статы движения сбрасываются к базовым
    //   ~18280  ResetFloorFlags()         — slippy = false, затем выставляется из тайла под ногами
    //   ~20594  PostUpdateRunSpeeds()     — ЕДИНСТВЕННАЯ точка, где запись ещё что-то меняет
    //   ~20594  HorizontalMovement()      — здесь maxRunSpeed / runAcceleration / slippy расходуются
    //   ~21811  PostUpdate()              — скорость уже потрачена
    // Документация tModLoader по Player.maxRunSpeed и Player.runAcceleration:
    //   «should only be modified in ModPlayer.PostUpdateRunSpeeds, ModItem.HorizontalWingSpeeds,
    //    or ExtraJump.UpdateHorizontalSpeeds(Player) to correctly function».
    // Запись из PostUpdate() стиралась следующим ResetEffects() и до движения не доезжала.
    public static class IceSlideSystem
    {
        // Порог скорости, ниже которого скольжения нет (px/тик).
        // Осознанно выше базового бега (maxRunSpeed = 3f): без ботинок или маунта
        // разогнаться до него нельзя, а на низкой скорости скольжение бессмысленно.
        // Для справки, accRunSpeed ванильной обуви: Hermes Boots = 6f, Lightning Boots = 6.75f.
        public const float MinSpeed = 4f;

        // Насколько поднимается потолок скорости на время скольжения.
        public const float SlideBoost = 1.15f;

        // Больше не используется: «трение» скольжения даёт ванильный флаг slippy.
        // Оставлен, чтобы не ломать ссылки; можно удалить.
        public const float SlideFriction = 0.985f;

        // Проверка: разогнался ли игрок вдоль выставленного копья.
        // Ничего не меняет — только отвечает да/нет.
        public static bool CanSlide(Player player, SwordPlayer sp)
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

            // Скользим только по ровной траектории — в прыжке и на склоне не работает
            if (System.Math.Abs(player.velocity.Y) > 0.5f) return false;

            return true;
        }

        // Совместимость со старым вызовом в SwordPlayer.PostUpdate().
        // Побочных эффектов больше нет — они переехали в IceSlidePlayer.
        public static bool TrySlide(Player player, SwordPlayer sp) => CanSlide(player, sp);
    }
}
