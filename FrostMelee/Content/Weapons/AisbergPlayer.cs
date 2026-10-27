using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace FrostMelee.Content.Weapons
{
    // Состояние Айсберга у игрока: темп появления глыб и скорость на своей
    // замороженной поверхности. Глыбы спавнит только владелец (OnHit дуги),
    // поэтому учёт локальный, как полировка и запас огня Фростбранда.
    public class AisbergPlayer : ModPlayer
    {
        private const int BlockCooldownNormal = 48;
        private const int BlockCooldownFast = 18;

        private int blockCooldown;
        private int fastBlockTicks;

        public override void PostUpdate()
        {
            if (blockCooldown > 0)
                blockCooldown--;
            if (fastBlockTicks > 0)
                fastBlockTicks--;
        }

        // true — этому попаданию взмаха можно отколоть глыбы.
        public bool TryStartBlockCooldown()
        {
            if (blockCooldown > 0)
                return false;
            blockCooldown = fastBlockTicks > 0 ? BlockCooldownFast : BlockCooldownNormal;
            return true;
        }

        // Тычок забрал Frostbite: текущее ожидание короче, а следующие глыбы
        // некоторое время появляются в быстром темпе.
        public void SpeedUpBlockCreation(int burnTicks)
        {
            float ratio = MathHelper.Clamp(burnTicks / (float)FrostFireSystem.BurnTicks, 0f, 1f);
            blockCooldown = System.Math.Max(0, blockCooldown - (20 + (int)(30f * ratio)));
            fastBlockTicks = System.Math.Max(fastBlockTicks, 180 + (int)(180f * ratio));
        }

        // Замороженная встречей поверхность: Айсберг на ней заметно быстрее.
        // Базовую ледяную физику дорожки даёт IceSlidePlayer, здесь только
        // добавка именно владельцу Айсберга.
        public override void PostUpdateRunSpeeds()
        {
            if (Player.whoAmI != Main.myPlayer)
                return;
            if (Player.HeldItem == null || Player.HeldItem.IsAir ||
                !SwordTypes.IsAisbergId(Player.HeldItem.type))
                return;
            if (Player.GetModPlayer<SwordPlayer>().HeldThrown)
                return;
            if (!StandingOnIce())
                return;

            Player.runAcceleration *= 1.6f;
            Player.maxRunSpeed *= 1.45f;
            Player.accRunSpeed *= 1.45f;
        }

        private bool StandingOnIce()
        {
            int y = (int)((Player.Bottom.Y + 2f) / 16f);
            int x = (int)(Player.Center.X / 16f);
            return IceSheetSystem.HasLiveMark(x, y)
                || IceSheetSystem.HasLiveMark(x - 1, y)
                || IceSheetSystem.HasLiveMark(x + 1, y);
        }
    }
}
