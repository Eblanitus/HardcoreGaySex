using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace WoodSwordsOverhaul.Content.Projectiles
{
    // Копьё-выставление: палка зафиксирована перед игроком.
    // Урон зависит от скорости сближения — враг сам натыкается на копьё
    // (доктрина: риск/награда; принцип как у ванильного турнирного копья,
    // где урон растёт со скоростью). Стоячий враг у копья почти не получает урона,
    // но копьё всё равно отталкивает его — это контроль.
    // ai[1] — угол копья (задаётся при спавне в StickPlayer, синхронизируется)
    // ai[2] — тип предмета (по нему рисуем текстуру палки)
    public class SpearBraceProjectile : ModProjectile
    {
        // Множитель урона от скорости сближения (px/тик вдоль копья), линейно:
        // ClosingSpeedForMin и ниже → MinDamageMult, ClosingSpeedForMax и выше → MaxDamageMult.
        // Урон снаряда = оружие × SpearDamageMult, т.е. множители считаются от него.
        // Стоя на месте — слабее обычного тычка (тычок палки ×0.7), обычный бег без
        // ускорителей (~3) ≈ ×1, бег в сапогах (~6.75) ≈ ×2.1, потолок ×2.5 на полном разбеге.
        // Раньше потолок стоял на 12 px/тик — недостижимо (бег ≤ ~8), урон не росла вообще.
        private const float MinDamageMult = 0.4f;
        private const float MaxDamageMult = 2.5f;
        private const float ClosingSpeedForMin = 1f;
        private const float ClosingSpeedForMax = 8f;

        // Страховочная текстура (своя отрисовка всё равно по ai[2])
        public override string Texture => "WoodSwordsOverhaul/Content/Items/Sticks/WoodStick";

        public override void SetDefaults()
        {
            Projectile.width = 24;
            Projectile.height = 24;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 15;
            Projectile.aiStyle = -1;
            Projectile.timeLeft = 2; // продлевается каждый тик, пока режим активен
            // Рисуемся в слоте heldProj (как ванильные копья), а не в слое снарядов:
            // тогда порядок слоёв «задняя рука → копьё → передняя рука» (ArmOverItemComposite).
            // Иначе копьё целиком под игроком и левая рука его перекрывает.
            Projectile.hide = true;
        }

        private Vector2 SpearDir => Vector2.UnitX.RotatedBy(Projectile.ai[1]);

        public override void AI()
        {
            Player player = Main.player[Projectile.owner];
            Projectile.timeLeft = 2;

            // heldProj каждый тик (как ванильное копьё и DaggerStabProjectile):
            // без этого снаряд с hide=true не рисуется НИГДЕ, плюс даёт порядок
            // слоёв «рука-копьё-рука» и авто-смерть при смене предмета.
            // Ставим у ВСЕХ клиентов — иначе у других игроков копьё не видно.
            player.heldProj = Projectile.whoAmI;

            // Режим копья знает только владелец: только он решает, жить ли копью.
            // (Сервер раньше тоже «решал», но режима не знает — и убивал копьё в MP.)
            if (Projectile.owner == Main.myPlayer)
            {
                StickPlayer stickPlayer = player.GetModPlayer<StickPlayer>();
                if (!stickPlayer.SpearMode || player.dead ||
                    player.HeldItem.IsAir || !StickTypes.IsWoodSword(player.HeldItem))
                {
                    Projectile.Kill();
                    return;
                }

                // Игрок ВСЕГДА смотрит в сторону копья (как ванильный Jousting Lance):
                // бег/движение не разворачивает персонажа.
                player.ChangeDir(SpearDir.X >= 0 ? 1 : -1);
            }
            else if (!player.active || player.dead)
            {
                Projectile.Kill();
                return;
            }

            // Хват как у турнирного копья: палку держит ПЕРЕДНЯЯ рука, согнутая у пояса,
            // хват у приклада, всё древко впереди. Вторая рука — как у Jousting Lance /
            // Hallowed Lance: у тела только при копье вверх, иначе спрятана за первой.
            // Раньше обе руки были вытянуты к палке у лица — выглядело как духовая трубка.
            // Позу и позицию считают все клиенты сами (от игрока и синхронизированного
            // угла ai[1]), поэтому у других игроков копьё и рука тоже правильные.
            Vector2 dir = SpearDir;
            float armRot = Projectile.ai[1] - MathHelper.PiOver2;

            // Выставление как у турнирного копья: первые DeployTicks копьё выдвигается
            // вперёд (рука разгибается, древко едет из-за спины вперёд),
            // дальше висит неподвижно. Счётчик в localAI[0] тикает у всех клиентов.
            if (Projectile.localAI[0] < DeployTicks)
                Projectile.localAI[0]++;
            float t = Projectile.localAI[0] / DeployTicks;
            float deploy = 1f - (1f - t) * (1f - t); // ease-out quad: заметный выдвиг, мягкая остановка
            // Поза как у ванильного Jousting Lance: кисть у корпуса на уровне пояса
            // (рука согнута и опущена вперёд-вниз), копьё горизонтально от кисти,
            // хват у самого приклада. Раньше рука была почти выпрямлена и задрана к
            // груди, а треть древка торчала назад — выглядело странно.
            Player.CompositeArmStretchAmount stretch = deploy < 0.5f
                ? Player.CompositeArmStretchAmount.None
                : Player.CompositeArmStretchAmount.Quarter;
            // Рука смотрит не вдоль копья, а ниже на ArmDrop (в сторону взгляда)
            float armAngle = Projectile.ai[1] + ArmDrop * (dir.X >= 0 ? 1f : -1f);
            armRot = armAngle - MathHelper.PiOver2;

            player.SetCompositeArmFront(true, stretch, armRot);
            // Вторая рука — как у ванильных копий (useStyle Shoot): вбок и вниз она
            // тянется вдоль копья за передней и спрятана за ней; копьё смотрит ВВЕРХ
            // (круче UpAngle над горизонтом — тот же порог 0.75 рад, что у ваниллы при
            // выборе «верхнего» кадра) — вторая рука остаётся у тела.
            float elevation = (float)System.Math.Asin(MathHelper.Clamp(-dir.Y, -1f, 1f));
            if (elevation > UpAngle)
                player.SetCompositeArmBack(false, Player.CompositeArmStretchAmount.None, 0f);
            else
                player.SetCompositeArmBack(true, stretch, armRot);
            Vector2 hand = StickSprite.HandGrip(player, stretch, armRot);

            // Геометрия реального спрайта (приклад/остриё по пикселям — StickSprite).
            // Center — середина палки; приклад — на GripBehind длины позади кисти.
            StickSprite.Geometry geo = StickSprite.Get((int)Projectile.ai[2]);
            Vector2 butt = hand - dir * (geo.Length * (GripBehind + DeployPullBack * (1f - deploy)));
            Projectile.Center = butt + dir * (geo.Length * 0.5f);

            // Поворот: вектор «приклад → остриё» спрайта вдоль копья (угол из пикселей).
            // При взгляде влево спрайт зеркалим, угол — зеркального. SET, не +=.
            Projectile.spriteDirection = dir.X >= 0 ? 1 : -1;
            Projectile.rotation = Projectile.ai[1] - geo.AngleFor(Projectile.spriteDirection == -1);
        }

        // Выдвиг при выставлении: длительность (тики) и насколько древко
        // в начале утянуто назад (доля длины палки)
        private const float DeployTicks = 20f;   // ~0.33 с
        private const float DeployPullBack = 0.5f;

        // Насколько рука опущена относительно копья (рад): кисть у пояса, как у Lance
        private const float ArmDrop = 0.35f;

        // Выше этого угла над горизонтом (рад) вторая рука остаётся у тела
        private const float UpAngle = 0.75f;

        // Какая доля древка уходит назад за кисть: хват у самого приклада, как у Lance
        private const float GripBehind = 0.06f;

        // Бьёт остриё: отрезок вдоль палки от острия назад, толщина (px)
        private const float TipHitLength = 20f;
        private const float TipHitWidth = 10f;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            Vector2 dir = SpearDir;
            float length = StickSprite.Get((int)Projectile.ai[2]).Length;
            Vector2 tipPos = Projectile.Center + dir * (length * 0.5f);
            Vector2 start = tipPos - dir * System.Math.Min(TipHitLength, length);
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                start, tipPos, TipHitWidth, ref collisionPoint);
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            // Урон от сближения: скорость игрока относительно врага в проекции на копьё.
            // Враг бежит на копьё или игрок разгоняется на врага — больно.
            // Враг стоит или убегает — почти без урона (но отталкивание остаётся).
            Player player = Main.player[Projectile.owner];
            float closingSpeed = Vector2.Dot(player.velocity - target.velocity, SpearDir);
            modifiers.SourceDamage *= Utils.Remap(closingSpeed, ClosingSpeedForMin, ClosingSpeedForMax, MinDamageMult, MaxDamageMult);
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

            // Якорь — реальный приклад спрайта (зеркальный при флипе: картинка
            // зеркалится внутри прямоугольника, а origin — нет). Масштаб 1.
            StickSprite.Geometry geo = StickSprite.Get(itemType);
            bool flipped = Projectile.spriteDirection == -1;
            Vector2 drawPos = Projectile.Center - SpearDir * (geo.Length * 0.5f);
            Vector2 origin = geo.ButtFor(flipped);
            SpriteEffects effects = flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            Main.EntitySpriteDraw(texture, drawPos - Main.screenPosition, null,
                lightColor, Projectile.rotation, origin, 1f, effects, 0f);
            return false;
        }
    }
}
