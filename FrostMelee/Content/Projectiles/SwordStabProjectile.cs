using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    // Выпад мечом: прямой удар остриём вперёд вместо дуги.
    // ai[0] — сколько тиков живёт выпад (интервал тычков из SwordPlayer)
    // ai[2] — тип предмета-меча (по нему рисуем его текстуру)
    public class SwordStabProjectile : ModProjectile
    {
        // Страховочная текстура (своя отрисовка всё равно по ai[2])
        public override string Texture => "FrostMelee/Items/IceSpear";

        // Профиль выпада (нелинейный, «резкий»):
        // первые ThrustPortion времени — бросок вперёд с резким стартом (ease-out),
        // остальное — медленный возврат (ease-in). Урон только пока остриё
        // выброшено (HitWindow) — на возврате меч уже не бьёт.
        private const float ThrustPortion = 0.3f;
        private const float HitWindow = 0.45f;

        public override void SetDefaults()
        {
            // Клонируем ванильное копьё: friendly, Melee, ownerHitCheck, penetrate и т.д.
            Projectile.CloneDefaults(ProjectileID.Spear);

            // Страховка по времени жизни: клон может дать недостаточный timeLeft —
            // PreAI потом обрежет до длительности выпада.
            Projectile.timeLeft = 60;

            // Меч: свой маленький хитбокс и защита от мультихита за выпад
            Projectile.width = 20;
            Projectile.height = 20;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 10;
            Projectile.tileCollide = false;
        }

        private int Duration => (int)MathHelper.Max(1f, Projectile.ai[0]);

        // 0 → 1 за время жизни выпада
        private float Life => 1f - Projectile.timeLeft / (float)Duration;

        public override bool PreAI()
        {
            Player player = Main.player[Projectile.owner];

            // Длительность выпада (ai[0] задаёт тот, кто спавнит)
            int duration = Duration;

            // КРИТИЧНО: клон ванильного копья даёт hide = true — снаряд НЕ рисуется
            // в своём проходе. Его рисует слой игрока через heldProj.
            // Без heldProj тычок с hide=true не рисуется НИГДЕ.
            player.heldProj = Projectile.whoAmI;

            if (Projectile.timeLeft > duration)
                Projectile.timeLeft = duration;

            // Звук выпада — на первом тике, у всех клиентов
            if (Projectile.localAI[0] == 0f)
            {
                Projectile.localAI[0] = 1f;
                SoundEngine.PlaySound(SoundID.Item1 with { Pitch = 0.5f, Volume = 0.6f }, player.Center);
            }

            // velocity хранит направление выпада
            Projectile.velocity = Projectile.velocity.SafeNormalize(Vector2.UnitX);
            Projectile.spriteDirection = Projectile.velocity.X >= 0 ? 1 : -1;

            // Нелинейный выпад: резкий бросок вперёд, затем неспешный возврат
            float life = Life;
            float extend;
            if (life < ThrustPortion)
            {
                float t = life / ThrustPortion;
                extend = 1f - (1f - t) * (1f - t) * (1f - t); // ease-out cubic
            }
            else
            {
                float t = (life - ThrustPortion) / (1f - ThrustPortion);
                extend = 1f - t * t; // ease-in quad
            }

            // Выпад делает РУКА: она разгибается вместе с выпадом (согнута → прямая),
            // а меч привязан к кисти.
            // Рука чуть выше линии выпада (ArmLift): кисть у плеча, удар сверху,
            // меч при этом смотрит ровно на цель
            float armRot = Projectile.velocity.ToRotation() - ArmLift * Projectile.spriteDirection - MathHelper.PiOver2;
            Player.CompositeArmStretchAmount stretch = ArmStretch(extend);
            player.SetCompositeArmFront(true, stretch, armRot);
            HideBackArm(player, Projectile.velocity);

            // Геометрия реального спрайта (приклад/остриё по пикселям — SickleSprite).
            SickleSprite.Geometry geo = SickleSprite.Get((int)Projectile.ai[2]);
            Vector2 hand = SickleSprite.HandGrip(player, stretch, armRot);
            Vector2 butt = hand - Projectile.velocity * GripInset;
            Projectile.Center = butt + Projectile.velocity * (geo.Length * 0.5f);

            // Поворот: вектор «приклад → остриё» спрайта ставим вдоль выпада.
            // При взгляде влево спрайт зеркалим (флип в PreDraw), угол — зеркального.
            // ЗНАК = (не +=) — иначе угол копится каждый тик и меч крутится.
            bool flipped = Projectile.spriteDirection == -1;
            Projectile.rotation = Projectile.velocity.ToRotation() - geo.AngleFor(flipped);

            return false; // своя AI вместо ванильной
        }

        // Насколько рука в выпаде поднята над линией удара (рад)
        public const float ArmLift = 0.3f;

        // Вторая рука — как у ванильных копий (useStyle Shoot): композитная задняя рука
        // ВЫКЛЮЧЕНА, а кадр тела выбирается по наклону оружия (как в ванильном PlayerFrame:
        // выше 0.75 рад — кадр 2, ниже -0.6 рад — кадр 4, иначе кадр 3).
        public static void HideBackArm(Player player, Vector2 dir)
        {
            player.SetCompositeArmBack(false, Player.CompositeArmStretchAmount.None, 0f);
            float elevation = (float)System.Math.Asin(MathHelper.Clamp(-dir.Y * player.gravDir, -1f, 1f));
            int row = 3;
            if (elevation > 0.75f)
                row = 2;
            else if (elevation < -0.6f)
                row = 4;
            player.bodyFrame.Y = player.bodyFrame.Height * row;
        }

        // Сколько рукояти торчит позади кисти (px)
        protected virtual float GripInset => 4f;

        // Выпад рукой: согнута на старте, прямая на пике выпада
        private static Player.CompositeArmStretchAmount ArmStretch(float extend)
        {
            if (extend < 0.25f)
                return Player.CompositeArmStretchAmount.None;
            if (extend < 0.5f)
                return Player.CompositeArmStretchAmount.Quarter;
            if (extend < 0.8f)
                return Player.CompositeArmStretchAmount.ThreeQuarters;
            return Player.CompositeArmStretchAmount.Full;
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            // Бьёт только фаза выпада: на медленном возврате урона нет
            if (Life > HitWindow)
                return false;

            // Хитбокс — только кончик: отрезок длиной BladeHitLength, который кончается
            // РОВНО на видимом острие
            Vector2 dir = Projectile.velocity.SafeNormalize(Vector2.UnitX);
            float half = SickleSprite.Get((int)Projectile.ai[2]).Length * 0.5f;
            Vector2 tipPos = Projectile.Center + dir * half;
            Vector2 bladeStart = tipPos - dir * System.Math.Min(BladeHitLength, half * 2f);
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                bladeStart, tipPos, BladeHitWidth, ref collisionPoint);
        }

        // Бьющая часть у острия: длина вдоль меча и толщина (px)
        private const float BladeHitLength = 14f;
        private const float BladeHitWidth = 8f;

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            // Тычок меча спавнится с knockBack = 0 (SwordTypes.StabKnockbackMult).
            // Отключаем отбрасывание полностью, чтобы его не вернули бонусы
            // отбрасывания игрока (перчатки и т.п.)
            if (Projectile.knockBack <= 0f)
                modifiers.DisableKnockback();
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
        }

        public override bool PreDraw(ref Color lightColor)
        {
            int itemType = (int)Projectile.ai[2];
            if (itemType <= 0)
                return true;
            // Текстуры предметов ванилла грузит лениво — у других игроков её может не быть
            Main.instance.LoadItem(itemType);
            Texture2D texture = TextureAssets.Item[itemType].Value;
            if (texture == null || texture.IsDisposed)
                return true;

            // Якорь отрисовки — реальный ПРИКЛАД спрайта, поставленный у кисти.
            // При флипе картинка зеркалится внутри прямоугольника, а origin — нет,
            // поэтому приклад берём уже зеркальный (ButtFor(flipped)).
            SickleSprite.Geometry geo = SickleSprite.Get(itemType);
            bool flipped = Projectile.spriteDirection == -1;
            Vector2 dir = Projectile.velocity.SafeNormalize(Vector2.UnitX);
            Vector2 drawPos = Projectile.Center - dir * (geo.Length * 0.5f);
            Vector2 origin = geo.ButtFor(flipped);
            SpriteEffects effects = flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            Main.EntitySpriteDraw(texture, drawPos - Main.screenPosition, null,
                lightColor, Projectile.rotation, origin, 1f, effects, 0f);
            return false;
        }
    }
}
