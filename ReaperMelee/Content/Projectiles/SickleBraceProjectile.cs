using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using ReLogic.Content;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace ReaperMelee.Content.Projectiles
{
    // Копьё-выставление: серп зафиксирован перед игроком.
    // Урон зависит от скорости сближения — враг сам натыкается на серп
    // (доктрина: риск/награда, принцип ванильного турнирного копья).
    // ai[1] — угол серпа (задаётся при спавне в SicklePlayer, синхронизируется)
    // ai[2] — тип предмета (по нему рисуем текстуру)
    public class SickleBraceProjectile : ModProjectile
    {
        // Множитель урона от скорости сближения (px/тик вдоль серпа), линейно:
        // ClosingSpeedForMin и ниже → MinDamageMult, ClosingSpeedForMax и выше → MaxDamageMult.
        private const float MinDamageMult = 0.4f;
        private const float MaxDamageMult = 2.5f;
        private const float ClosingSpeedForMin = 1f;
        private const float ClosingSpeedForMax = 8f;

        // === Эффект от разгона ===
        // Разгон = скорость игрока вдоль копья, нормированная теми же границами, что и урон (0..1),
        // сглаженная по тикам. Эффект: 0 до BraceFxStart, плавно (smoothstep) растёт до BraceFxFull.
        private const float BraceFxStart = 0.25f;    // нормированный разгон, с которого появляется эффект
        private const float BraceFxFull = 1.0f;      // разгон, при котором эффект максимален
        private const float BraceFxSmooth = 0.12f;   // доля сглаживания за тик (меньше — плавнее и медленнее)
        private const float BraceFxScale = 0.8f;     // потолок яркости/размера относительно тычка (тычок ярче)
        private const float BraceHaloSize = 50f;
        private const float BraceHaloAlpha = 0.2f;
        private const float BraceShaftWidth = 8f;
        private const float BraceShaftAlpha = 0.2f;
        private const float BraceHeadAhead = 0f;     // голова кометы впереди острия при полном разгоне
        private const float BraceHeadSize = 56f;
        private const float BraceCometTail = 55f;    // хвост кометы назад от острия
        private const float BraceCometHeadWidth = 32f;
        private const float BraceCometTailWidth = 8f;
        private const int BraceCometSteps = 8;
        private const int BraceTrailLen = 8;         // сколько тиков истории острия тянется след
        private const float BraceTrailWidth = 22f;
        private const float BraceTrailAlpha = 0.3f;
        private const int BraceSparksPerTick = 1;    // искры у острия за тик при полном эффекте
        private const float BraceSparkForward = 0.4f;
        private const float BraceSmokeChance = 0.3f; // тёмный дым за тик при разгоне
        private const float BraceStarAlpha = 0.8f;
        private const float BraceStarSize = 20f;
        private const float BraceLightMult = 1.3f;

        private static readonly Color DeathCore = new Color(225, 195, 255);
        private static readonly Color DeathTail = new Color(150, 80, 220);
        private static readonly Color DeathEdge = new Color(55, 15, 105);

        // Зона урона = то, что нарисовано: лезвие по пикселям спрайта
        private SickleSprite.BladeZone HitZone =>
            SickleSprite.GetZone((int)Projectile.ai[2]);

        private float braceSpeed01;                  // сглаженный нормированный разгон
        private float braceFx;                       // итоговая сила эффекта 0..1 (после smoothstep)
        private readonly Vector2[] tipHistory = new Vector2[BraceTrailLen];
        private bool tipHistoryReady;

        // Страховочная текстура (своя отрисовка всё равно по ai[2])
        public override string Texture => "ReaperMelee/Items/DeathSpear";

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
            // Рисуемся в слоте heldProj (как ванильные копья)
            Projectile.hide = true;
        }

        private Vector2 SpearDir => Vector2.UnitX.RotatedBy(Projectile.ai[1]);

        public override void AI()
        {
            Player player = Main.player[Projectile.owner];
            Projectile.timeLeft = 2;

            // heldProj каждый тик (снаряд с hide=true иначе не рисуется НИГДЕ)
            player.heldProj = Projectile.whoAmI;

            // Режим копья знает только владелец: только он решает, жить ли копью
            if (Projectile.owner == Main.myPlayer)
            {
                SicklePlayer sicklePlayer = player.GetModPlayer<SicklePlayer>();
                if (!sicklePlayer.SpearMode || player.dead ||
                    player.HeldItem.IsAir || !SickleTypes.IsSickle(player.HeldItem))
                {
                    Projectile.Kill();
                    return;
                }

                // Игрок ВСЕГДА смотрит в сторону серпа (как ванильный Jousting Lance)
                player.ChangeDir(SpearDir.X >= 0 ? 1 : -1);
            }
            else if (!player.active || player.dead)
            {
                Projectile.Kill();
                return;
            }

            // Хват как у турнирного копья: серп держит ПЕРЕДНЯЯ рука, согнутая у пояса
            Vector2 dir = SpearDir;
            float armRot = Projectile.ai[1] - MathHelper.PiOver2;

            // Выдвиг: первые DeployTicks серп выдвигается вперёд, дальше висит
            if (Projectile.localAI[0] < DeployTicks)
                Projectile.localAI[0]++;
            float t = Projectile.localAI[0] / DeployTicks;
            float deploy = 1f - (1f - t) * (1f - t); // ease-out quad

            Player.CompositeArmStretchAmount stretch = deploy < 0.5f
                ? Player.CompositeArmStretchAmount.None
                : Player.CompositeArmStretchAmount.Quarter;
            // Рука смотрит не вдоль серпа, а ниже на ArmDrop
            float armAngle = Projectile.ai[1] + ArmDrop * (dir.X >= 0 ? 1f : -1f);
            armRot = armAngle - MathHelper.PiOver2;

            player.SetCompositeArmFront(true, stretch, armRot);
            // Вторая рука — как у ванильных копий (useStyle Shoot)
            float elevation = (float)System.Math.Asin(MathHelper.Clamp(-dir.Y, -1f, 1f));
            if (elevation > UpAngle)
                player.SetCompositeArmBack(false, Player.CompositeArmStretchAmount.None, 0f);
            else
                player.SetCompositeArmBack(true, stretch, armRot);
            Vector2 hand = SickleSprite.HandGrip(player, stretch, armRot);

            // Геометрия реального спрайта (приклад/острие по пикселям)
            SickleSprite.Geometry geo = SickleSprite.Get((int)Projectile.ai[2]);
            Vector2 butt = hand - dir * (geo.Length * (GripBehind + DeployPullBack * (1f - deploy)));
            Projectile.Center = butt + dir * (geo.Length * 0.5f);

            // Поворот: вектор «приклад → острие» спрайта вдоль серпа. SET, не +=.
            Projectile.spriteDirection = dir.X >= 0 ? 1 : -1;
            Projectile.rotation = Projectile.ai[1] - geo.AngleFor(Projectile.spriteDirection == -1);

            UpdateBraceFx(player, dir, geo);

            if (SickleArcProjectile.DebugHitbox && Projectile.owner == Main.myPlayer)
                SickleSprite.EmitDebugDust(HitZone, Projectile.Center,
                    Projectile.ai[1], Projectile.spriteDirection == -1, 1f);
        }

        private const float DeployTicks = 20f;   // ~0.33 с
        private const float DeployPullBack = 0.5f;
        private const float ArmDrop = 0.35f;     // кисть у пояса, как у Lance
        private const float UpAngle = 0.75f;     // выше — вторая рука у тела
        private const float GripBehind = 0.06f;  // хват у самого приклада

        // Запасной хитбокс (если нет рисунка): отрезок вдоль серпа от острия назад
        private const float TipHitLength = 20f;
        private const float TipHitWidth = 10f;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            Vector2 dir = SpearDir;

            // Бьёт видимое лезвие: зона по пикселям серпа в позе, которую рисует PreDraw
            SickleSprite.BladeZone zone = HitZone;
            if (zone != null)
                return SickleSprite.BladeOverlaps(zone, targetHitbox, Projectile.Center, Projectile.ai[1],
                    Projectile.ai[1], Projectile.spriteDirection == -1, 1f);

            float length = SickleSprite.Get((int)Projectile.ai[2]).Length;
            Vector2 tipPos = Projectile.Center + dir * (length * 0.5f);
            Vector2 start = tipPos - dir * System.Math.Min(TipHitLength, length);
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                start, tipPos, TipHitWidth, ref collisionPoint);
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            // Урон от сближения: враг бежит на серп или игрок разгоняется — больно
            Player player = Main.player[Projectile.owner];
            float closingSpeed = Vector2.Dot(player.velocity - target.velocity, SpearDir);
            modifiers.SourceDamage *= Utils.Remap(closingSpeed, ClosingSpeedForMin, ClosingSpeedForMax, MinDamageMult, MaxDamageMult);
        }


        private static float Smooth01(float x)
        {
            x = MathHelper.Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        private static Color Additive(Color c, float alpha)
        {
            Color r = c * MathHelper.Clamp(alpha, 0f, 1f);
            r.A = 0;
            return r;
        }

        private void GlowColors(out Color core, out Color tail, out Color edge)
        {
            core = DeathCore;
            tail = DeathTail;
            edge = DeathEdge;
        }

        // Считает силу эффекта по разгону, ведёт историю острия, пускает пыль и свет (не на сервере)
        private void UpdateBraceFx(Player player, Vector2 dir, SickleSprite.Geometry geo)
        {
            if (Main.dedServ)
                return;

            // Разгон: скорость игрока вдоль копья (цель неизвестна до удара), те же границы, что у урона
            float along = Vector2.Dot(player.velocity, dir);
            float target = Utils.GetLerpValue(ClosingSpeedForMin, ClosingSpeedForMax, along, true);
            braceSpeed01 += (target - braceSpeed01) * BraceFxSmooth;
            braceFx = Smooth01((braceSpeed01 - BraceFxStart) / Math.Max(0.01f, BraceFxFull - BraceFxStart));

            Vector2 tip = Projectile.Center + dir * (geo.Length * 0.5f);
            if (!tipHistoryReady)
            {
                for (int i = 0; i < tipHistory.Length; i++)
                    tipHistory[i] = tip;
                tipHistoryReady = true;
            }
            else
            {
                for (int i = tipHistory.Length - 1; i > 0; i--)
                    tipHistory[i] = tipHistory[i - 1];
                tipHistory[0] = tip;
            }

            float f = braceFx * BraceFxScale;
            if (f <= 0.02f)
                return;

            Vector2 head = tip + dir * BraceHeadAhead * braceFx;
            Vector3 light = new Vector3(0.85f, 0.3f, 1.35f);
            Lighting.AddLight(head, light * f * BraceLightMult);
            Lighting.AddLight(tip - dir * BraceCometTail * 0.5f * braceFx, light * f * 0.6f * BraceLightMult);

            // Искры у острия и чуть впереди (число и вероятность растут с эффектом)
            for (int i = 0; i < BraceSparksPerTick; i++)
            {
                if (Main.rand.NextFloat() > f)
                    continue;
                int type = Main.rand.NextBool(3) ? DustID.Shadowflame : DustID.PurpleTorch;
                Vector2 pos = Vector2.Lerp(tip, head, Main.rand.NextFloat()) + Main.rand.NextVector2Circular(4f, 4f);
                Dust d = Dust.NewDustPerfect(pos, type,
                    Main.rand.NextVector2Circular(0.5f, 0.5f) + dir * BraceSparkForward * braceFx, 100,
                    default, Main.rand.NextFloat(0.9f, 1.4f) * (0.6f + 0.4f * braceFx));
                d.noGravity = true;
                d.fadeIn = 0.4f;
            }

            if (Main.rand.NextFloat() < BraceSmokeChance * f)
            {
                Dust w = Dust.NewDustPerfect(head + Main.rand.NextVector2Circular(5f, 5f), DustID.Smoke,
                    Main.rand.NextVector2Circular(0.5f, 0.5f) + dir * 0.5f, 180, new Color(60, 15, 100),
                    Main.rand.NextFloat(1.1f, 1.7f));
                w.noGravity = true;
                w.fadeIn = 0.5f;
            }

            // Искры/снежинки вдоль древка
            if (Main.rand.NextFloat() < 0.2f * f)
            {
                Vector2 hp = Vector2.Lerp(tip, tip - dir * (geo.Length * 0.3f), Main.rand.NextFloat());
                Dust s = Dust.NewDustPerfect(hp + Main.rand.NextVector2Circular(3f, 3f),
                    DustID.PurpleTorch,
                    Main.rand.NextVector2Circular(0.6f, 0.6f), 110, default, Main.rand.NextFloat(0.8f, 1.2f));
                s.noGravity = true;
            }
        }

        private static void DrawSegment(Texture2D glow, Vector2 a, Vector2 b, float width, Color color)
        {
            if (width < 0.5f)
                return;
            Vector2 delta = b - a;
            Vector2 scale = new Vector2((delta.Length() + width) / glow.Width, width / glow.Height);
            Main.EntitySpriteDraw(glow, (a + b) * 0.5f - Main.screenPosition, null, color, delta.ToRotation(),
                glow.Size() / 2f, scale, SpriteEffects.None, 0f);
        }

        private static void DrawSparkle(Texture2D glow, Vector2 pos, float size, float rotation, Color color)
        {
            Vector2 origin = glow.Size() / 2f;
            for (int i = 0; i < 2; i++)
                Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, color, rotation + MathHelper.PiOver2 * i,
                    origin, new Vector2(size / glow.Width, size * 0.09f / glow.Height), SpriteEffects.None, 0f);
            Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, color, 0f, origin,
                size * 0.28f / glow.Width, SpriteEffects.None, 0f);
        }

        // Свечение от разгона: ореол, свет вдоль древка, комета вперёд от острия, след острия назад, блёстки
        private void DrawBraceFx(SickleSprite.Geometry geo)
        {
            if (Main.dedServ || braceFx <= 0.02f)
                return;
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow == null || glow.IsDisposed)
                return;

            float fx = braceFx;
            float env = fx * BraceFxScale;
            GlowColors(out Color core, out Color tail, out Color edge);
            Vector2 dir = SpearDir;
            Vector2 tip = Projectile.Center + dir * (geo.Length * 0.5f);
            Vector2 head = tip + dir * BraceHeadAhead * fx;
            Vector2 origin = glow.Size() / 2f;

            // 1) След острия по истории позиций (яркость и длина растут с разгоном)
            if (tipHistoryReady)
            {
                int steps = Math.Max(1, (int)Math.Ceiling(tipHistory.Length * fx));
                Vector2 prev = tip;
                for (int j = 0; j < steps && j < tipHistory.Length; j++)
                {
                    float t = (j + 1f) / tipHistory.Length;
                    DrawSegment(glow, prev, tipHistory[j], BraceTrailWidth * fx * (1f - t * 0.8f),
                        Additive(Color.Lerp(core, tail, t), BraceTrailAlpha * env * (1f - t)));
                    prev = tipHistory[j];
                }
            }

            // 2) Ореол вокруг лезвия и свет вдоль древка
            Main.EntitySpriteDraw(glow, tip - dir * 8f - Main.screenPosition, null, Additive(edge, BraceHaloAlpha * env), 0f, origin,
                BraceHaloSize * (0.5f + 0.5f * fx) / glow.Width, SpriteEffects.None, 0f);
            Vector2 shaftBack = tip - dir * (geo.Length * 0.95f);
            const int shaftSteps = 6;
            Vector2 sa = tip;
            for (int j = 1; j <= shaftSteps; j++)
            {
                float t = j / (float)shaftSteps;
                Vector2 sb = Vector2.Lerp(tip, shaftBack, t);
                DrawSegment(glow, sa, sb, BraceShaftWidth * (1f - t * 0.5f),
                    Additive(Color.Lerp(core, tail, t), BraceShaftAlpha * env * (1f - t * 0.6f)));
                sa = sb;
            }
        }


        public override bool PreDraw(ref Color lightColor)
        {
            int itemType = (int)Projectile.ai[2];
            if (itemType <= 0)
                return true;
            Texture2D texture = SickleTextures.Normal(itemType);
            if (texture == null || texture.IsDisposed)
                return true;

            // Якорь — реальный приклад спрайта (зеркальный при флипе)
            SickleSprite.Geometry geo = SickleSprite.Get(itemType);
            bool flipped = Projectile.spriteDirection == -1;
            Vector2 drawPos = Projectile.Center - SpearDir * (geo.Length * 0.5f);
            Vector2 origin = geo.ButtFor(flipped);
            SpriteEffects effects = flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            Main.EntitySpriteDraw(texture, drawPos - Main.screenPosition, null,
                lightColor, Projectile.rotation, origin, 1f, effects, 0f);
            DrawBraceFx(geo);
            return false;
        }
    }
}
