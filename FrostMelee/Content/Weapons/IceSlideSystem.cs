using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace FrostMelee.Content.Weapons
{
    // Скольжение в режиме копья с ледяным клинком.
    //
    // Модель состояния (живёт в IceSlidePlayer):
    //   ВКЛ  — CanSlide() вернул true: игрок разогнался вдоль выставленного копья.
    //   ДЕРЖ — после включения эффект держится, пока игрок ДОКАТЫВАЕТСЯ:
    //          скорость упала ниже порога включения, но ещё не почти нулевая,
    //          стойка копья не снята и не истёк предохранитель по времени.
    //   ВЫКЛ — докатился почти до нуля / снял стойку / умер / истёк предел.
    //
    // Раньше эффект обрывался ровно в момент замедления (порог MinSpeed),
    // поэтому вместо докатывания игрок резко «вставал» — скольжение исчезало.
    //
    // Почему эффект выставляется из двух хуков (PostUpdateRunSpeeds и PostUpdate):
    //   PostUpdateRunSpeeds — гарантированно попадает в HorizontalMovement этого
    //   же кадра (единственный хук, который ваниль читает до движения).
    //   PostUpdate — точка, где эффект жил в старой версии: ваниль, похоже,
    //   не пересчитывает slippy каждый кадр на твёрдой земле, и запись оттуда
    //   переносится на следующий кадр. Дублируем флаг там, чтобы докатывание
    //   не рвалось между кадрами.
    public static class IceSlideSystem
    {
        // Порог ВКЛЮЧЕНИЯ (px/тик): без ботинок или маунта разогнаться до него
        // нельзя, а на низкой скорости скольжение бессмысленно. Осознанно.
        // Для справки, accRunSpeed ванильной обуви: Hermes = 6f, Lightning = 6.75f.
        public const float MinSpeed = 4f;

        // Порог ДЕРЖАНИЯ: пока горизонтальная скорость выше этого, игрок
        // докатывается по инерции вместо того чтобы мгновенно встать.
        // Ниже — уже почти шаг, скольжение честно заканчивается.
        public const float CoastStopSpeed = 2f;

        // Предохранитель: максимум тиков докатывания без повторного разгона.
        public const int MaxCoastTicks = 90;

        // Ниже этой скорости визуал (глянец и крошка) не рисуется:
        // хвост докатывания не должен выглядеть как скольжение на ходу.
        public const float VisualMinSpeed = 2f;

        // Насколько поднимается потолок скорости, пока скользим.
        public const float SlideBoost = 1.15f;

        // Больше не используется: «трение» скольжения даёт ванильный флаг slippy.
        public const float SlideFriction = 0.985f;

        // Проверка ВКЛЮЧЕНИЯ: разогнался ли игрок вдоль выставленного копья.
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

        // Проверка ДЕРЖАНИЯ: игрок уже не разгоняется, но ещё катится.
        // Стойка и меч в руке обязательны: снял стойку — скольжение честно кончается.
        public static bool StillCoasting(Player player, SwordPlayer sp)
        {
            if (player.dead) return false;
            if (!sp.HoldingSword || player.HeldItem.type != ItemID.IceBlade) return false;
            if (!sp.SpearMode) return false;
            if (System.Math.Abs(player.velocity.X) < CoastStopSpeed) return false;
            if (System.Math.Abs(player.velocity.Y) > 0.5f) return false;
            return true;
        }

        // Совместимость со старым вызовом в SwordPlayer.PostUpdate().
        // Побочных эффектов нет — они в IceSlidePlayer.
        public static bool TrySlide(Player player, SwordPlayer sp) => CanSlide(player, sp);
    }
}
