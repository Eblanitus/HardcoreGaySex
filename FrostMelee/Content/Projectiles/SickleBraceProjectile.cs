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
using FrostMelee.Content.Yoyo;

namespace FrostMelee.Content.Projectiles
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
        private const float BraceFxSmooth = 0.12f;   // доля сглаживания за тик (меньше — плавнее и медленнее)

        private static readonly Color IceCore = new Color(235, 250, 255);
        private static readonly Color IceTail = new Color(120, 210, 255);
        private static readonly Color IceEdge = new Color(40, 110, 230);

        // === Только ледяное копьё: иней, Frostbite, раскол льда на полном разгоне ===
        private const int RimeCrystals = 14;          // кристалликов на лезвии при полном инее
        private const float RimeBladeFrac = 0.6f;    // доля длины копья от острия назад, покрытая инеем
        private const float RimeEdgeWidth = 8f;
        private const float RimeEdgeAlpha = 0.4f;
        private const float RimeCrystalSize = 11f;
        private const int RimeSpikes = 5;          // ледяные шипы, торчащие с лезвия назад по ходу движения
        private const float RimeSpikeMin = 8f;       // длина шипа, px (растёт с инеем до максимума)
        private const float RimeSpikeMax = 16f;
        private const float RimeSpikeWidth = 5f;
        private const float RimeSpikeSweep = 0.9f;   // рад: отклонение шипа от нормали назад
        private const float BraceShatterAt = 0.85f;  // нормированный разгон, с которого удар раскалывает лёд
        private const float BraceShatterRadius = 120f;
        private const int BraceShatterSlowTicks = 120; // 2 с (BuffID.Slow)
        private const int BraceShatterShards = 28;
        private const float BraceShatterSpeed = 5.5f;
        private const float BraceShatterFlashSize = 130f;
        private const int BraceShatterFlashTicks = 9;

        private const float CrumbChance = 0.3f;     // шанс ледяной крошки за тик при полном разгоне (растёт с разгоном)
        private const float CrumbSpan = 0.3f;        // доля длины копья от острия назад, откуда отрываются крошки
        private const float IcicleBaseScale = 1.0f;  // нормальный масштаб спрайта (48 px, диагональ ~68 px), держится первые IcicleGrowFrom доли разгона

        private bool IsIce => (int)Projectile.ai[2] == ItemID.IceSickle;

        // Зона урона = то, что нарисовано: обычный спрайт переходит в замороженный (крупнее и длиннее) по шкале заморозки игрока.
        // При шкале >= 0.5 бьёт замороженная зона, до неё — обычная
        private SickleSprite.BladeZone HitZone =>
            SickleSprite.GetZone((int)Projectile.ai[2], FrozenSpear.UseZone(Main.player[Projectile.owner], (int)Projectile.ai[2]));
        private float braceRime;                     // сила инея 0..1
        private readonly HashSet<int> shatteredNpcs = new HashSet<int>();
        private Vector2 shatterFlashPos;
        private uint shatterFlashStart;
        private bool shatterFlashActive;

        private float braceSpeed01;                  // сглаженный нормированный разгон

        // Страховочная текстура (своя отрисовка всё равно по ai[2])
        public override string Texture => "FrostMelee/Items/IceSpear";

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

            // Режим стойки знает только владелец: только он решает, жить ли копью.
            // Снаряд общий для серпа и мечей (МЕЧИ.md): у серпа режим в SicklePlayer,
            // у меча режим «встреча» — в SwordPlayer.
            if (Projectile.owner == Main.myPlayer)
            {
                bool spearMode = false;
                bool holdingStanceWeapon = false;
                if (!player.HeldItem.IsAir)
                {
                    if (SickleTypes.IsSickle(player.HeldItem))
                    {
                        holdingStanceWeapon = true;
                        spearMode = player.GetModPlayer<SicklePlayer>().SpearMode;
                    }
                    else if (Weapons.SwordTypes.IsSword(player.HeldItem))
                    {
                        holdingStanceWeapon = true;
                        spearMode = player.GetModPlayer<Weapons.SwordPlayer>().SpearMode;
                    }
                }
                if (!spearMode || !holdingStanceWeapon || player.dead)
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

            // Копьё копит заморозку каждый тик, пока выставлено: чем быстрее игрок, тем быстрее набор (стоя почти ноль)
            if (IsIce && Projectile.owner == Main.myPlayer && braceSpeed01 > SicklePlayer.FrostBraceMinSpeed)
                player.GetModPlayer<SicklePlayer>().AddFrost(SicklePlayer.FrostPerBraceTick * braceSpeed01);

            if (SickleArcProjectile.DebugHitbox && Projectile.owner == Main.myPlayer)
                SickleSprite.EmitDebugDust(HitZone, Projectile.Center,
                    Projectile.ai[1], Projectile.spriteDirection == -1, 1f);
        }

        private const float DeployTicks = 20f;   // ~0.33 с
        private const float DeployPullBack = 0.5f;
        private const float ArmDrop = 0.35f;     // кисть у пояса, как у Lance
        private const float UpAngle = 0.75f;     // выше — вторая рука у тела
        private const float GripBehind = 0.06f;  // хват у самого приклада

        // Отрезок ледяного жала за остриём спрайта
        private bool HitTipLine(Vector2 dir, float spriteLength, float extra, Rectangle targetHitbox)
        {
            Vector2 tip = Projectile.Center + dir * (spriteLength * 0.5f);
            float cp = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                tip, tip + dir * extra, TipHitWidth, ref cp);
        }

        // Запасной хитбокс (если нет рисунка): отрезок вдоль серпа от острия назад
        private const float TipHitLength = 20f;
        private const float TipHitWidth = 10f;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            Vector2 dir = SpearDir;

            // Бьёт видимое лезвие: зона по пикселям серпа в позе, которую рисует PreDraw
            SickleSprite.BladeZone zone = HitZone;
            if (zone != null)
            {
                if (SickleSprite.BladeOverlaps(zone, targetHitbox, Projectile.Center, Projectile.ai[1],
                    Projectile.ai[1], Projectile.spriteDirection == -1, 1f))
                    return true;
                return false;
            }

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

        // Считает силу разгона по скорости игрока вдоль копья (для раскола льда), пускает ледяные крошки
        private void UpdateBraceFx(Player player, Vector2 dir, SickleSprite.Geometry geo)
        {
            if (Main.dedServ)
                return;

            // Разгон: скорость игрока вдоль копья (цель неизвестна до удара), те же границы, что у урона
            float along = Vector2.Dot(player.velocity, dir);
            float target = Utils.GetLerpValue(ClosingSpeedForMin, ClosingSpeedForMax, along, true);
            braceSpeed01 += (target - braceSpeed01) * BraceFxSmooth;

            braceRime = 0f; // иней/кристаллы/шипы отключены: вместо них замороженный спрайт копья (FrozenSpear)

            // Ледяные крошки отрываются с замороженной головы и падают вниз под гравитацией,
            // частота растёт с разгоном. Без свечения, без искр и света
            Vector2 tip = Projectile.Center + dir * (geo.Length * 0.5f);
            float mix = FrozenMix();
            if (mix > 0.05f && Main.rand.NextFloat() < CrumbChance * mix)
            {
                Vector2 cp = Vector2.Lerp(tip, tip - dir * geo.Length * CrumbSpan, Main.rand.NextFloat())
                    + Main.rand.NextVector2Circular(4f, 4f);
                Dust cd = Dust.NewDustPerfect(cp, Main.rand.NextBool(3) ? DustID.Frost : DustID.SnowflakeIce,
                    new Vector2(Main.rand.NextFloat(-0.4f, 0.4f), Main.rand.NextFloat(0f, 0.6f)), 90, default,
                    Main.rand.NextFloat(0.55f, 0.9f));
                cd.noGravity = false;
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

        // Иней: голубо-белая кайма по лезвию и кристаллики, число и яркость растут с разгоном
        private void DrawRime(SickleSprite.Geometry geo)
        {
            if (Main.dedServ)
                return;
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow == null || glow.IsDisposed)
                return;
            Vector2 dir = SpearDir;
            Vector2 normal = new Vector2(-dir.Y, dir.X);
            Vector2 tip = Projectile.Center + dir * (geo.Length * 0.5f);
            Vector2 back = tip - dir * (geo.Length * RimeBladeFrac);

            if (braceRime > 0.02f)
            {
                DrawSegment(glow, tip, back, RimeEdgeWidth * (0.4f + 0.6f * braceRime),
                    Additive(IceTail, RimeEdgeAlpha * braceRime));
                DrawSegment(glow, tip, Vector2.Lerp(tip, back, 0.7f), RimeEdgeWidth * 0.45f * braceRime,
                    Additive(IceCore, RimeEdgeAlpha * braceRime));
                for (int i = 0; i < RimeCrystals; i++)
                {
                    float a = Smooth01(braceRime * 1.5f - i / (float)RimeCrystals * 0.9f);
                    if (a <= 0.02f)
                        continue;
                    float along = (i + 0.5f) / RimeCrystals;
                    float side = (i % 2 == 0 ? 1f : -1f) * (3f + (i * 7 % 5));
                    Vector2 pos = Vector2.Lerp(tip, back, along) + normal * side;
                    float tw = 0.75f + 0.25f * (float)Math.Sin(Main.GameUpdateCount * 0.2f + i * 1.9f + Projectile.whoAmI);
                    DrawSparkle(glow, pos, RimeCrystalSize * a * tw * (1.1f - along * 0.4f), i * 0.9f + 0.4f,
                        Additive(IceCore, 0.45f * a));
                }
            }

            // Ледяные шипы: торчат с лезвия в обе стороны, отклонены назад, длина растёт с инеем
            if (braceRime > 0.05f)
            {
                for (int i = 0; i < RimeSpikes; i++)
                {
                    float a = Smooth01(braceRime * 1.3f - i / (float)RimeSpikes * 0.5f);
                    if (a <= 0.02f)
                        continue;
                    float along = (i + 0.4f) / RimeSpikes * 0.9f;
                    Vector2 root = Vector2.Lerp(tip, back, along);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float len = MathHelper.Lerp(RimeSpikeMin, RimeSpikeMax, a) * (0.8f + 0.2f * ((i + side) % 2 == 0 ? 1f : 0.5f));
                        Vector2 sd = (normal * side * (float)Math.Cos(RimeSpikeSweep) - dir * (float)Math.Sin(RimeSpikeSweep));
                        Vector2 spikeTip = root + sd * len;
                        DrawSegment(glow, root, spikeTip, RimeSpikeWidth * (0.5f + 0.5f * a),
                            Additive(IceTail, 0.55f * a));
                        DrawSegment(glow, root, Vector2.Lerp(root, spikeTip, 0.8f), RimeSpikeWidth * 0.4f,
                            Additive(IceCore, a));
                    }
                }
            }

            // Вспышка раскола льда
            if (shatterFlashActive)
            {
                int age = (int)(Main.GameUpdateCount - shatterFlashStart);
                if (age < 0 || age >= BraceShatterFlashTicks)
                {
                    shatterFlashActive = false;
                    return;
                }
                float k = 1f - age / (float)BraceShatterFlashTicks;
                float grow = 0.6f + 0.8f * (1f - k);
                Vector2 origin = glow.Size() / 2f;
                Main.EntitySpriteDraw(glow, shatterFlashPos - Main.screenPosition, null, Additive(IceEdge, k), 0f, origin,
                    BraceShatterFlashSize * grow / glow.Width, SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(glow, shatterFlashPos - Main.screenPosition, null, Additive(IceCore, k), 0f, origin,
                    BraceShatterFlashSize * 0.45f * grow / glow.Width, SpriteEffects.None, 0f);
                DrawSparkle(glow, shatterFlashPos, BraceShatterFlashSize * (0.6f + 0.4f * k), MathHelper.PiOver4,
                    Additive(Color.Lerp(IceTail, IceCore, 0.5f), k));
                DrawSparkle(glow, shatterFlashPos, BraceShatterFlashSize * 0.7f * (0.6f + 0.4f * k), 0f,
                    Additive(IceCore, k));
            }
        }


        // Замороженное копьё (FrozenSpear): плавный кроссфейд обычный спрайт → замороженный по шкале заморозки игрока
        // (SicklePlayer.Frost, растёт от атак). Обычный гаснет (1 - mix), замороженный проявляется (mix). Размер не меняется
        public override void Unload()
        {
            FrozenSpear.Unload();
        }

        private float FrozenMix() => FrozenSpear.Mix(Main.player[Projectile.owner], (int)Projectile.ai[2]);

        private void DrawIcicle(SickleSprite.Geometry geo, Vector2 drawPos, bool flipped, Color lightColor)
        {
            if (Main.dedServ)
                return;
            FrozenSpear.Draw(drawPos, geo, Projectile.rotation, flipped, lightColor, IcicleBaseScale, FrozenMix());
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            if (Projectile.owner == Main.myPlayer)
                Main.player[Projectile.owner].GetModPlayer<SicklePlayer>().AddFrost(SicklePlayer.FrostPerBraceHit);

            // Раскол льда: на полном разгоне, один раз на цель за выставление
            if (braceSpeed01 < BraceShatterAt || !shatteredNpcs.Add(target.whoAmI))
                return;

            Vector2 center = target.Center;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC n = Main.npc[i];
                if (!n.active || n.friendly || n.dontTakeDamage || n.immortal)
                    continue;
                if (Vector2.Distance(n.Center, center) <= BraceShatterRadius + Math.Max(n.width, n.height) * 0.5f)
                    n.AddBuff(BuffID.Slow, BraceShatterSlowTicks);
            }

            shatterFlashPos = center;
            shatterFlashStart = Main.GameUpdateCount;
            shatterFlashActive = true;

            if (Main.dedServ)
                return;
            SoundEngine.PlaySound(SoundID.Item27, center);
            // Осколки раскола — настоящие летящие сосульки (только визуал: ai[0]=1)
            for (int i = 0; i < BraceShatterShards; i++)
            {
                Vector2 dir = Main.rand.NextVector2Circular(1f, 1f);
                if (dir.LengthSquared() < 0.01f)
                    dir = Vector2.UnitX;
                dir.Normalize();
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), center + dir * 12f,
                    dir * BraceShatterSpeed * Main.rand.NextFloat(0.6f, 1.2f) + SpearDir * 1.5f,
                    ModContent.ProjectileType<AmarokShardProjectile>(), 1, 0f, Projectile.owner, 1f);
            }
            for (int i = 0; i < 10; i++)
            {
                Dust d = Dust.NewDustPerfect(center, DustID.Frost,
                    Vector2.UnitX.RotatedBy(i / 10f * MathHelper.TwoPi) * BraceShatterSpeed * 0.8f, 120, default, 1.6f);
                d.noGravity = true;
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
                lightColor * (1f - FrozenMix()), Projectile.rotation, origin, 1f, effects, 0f);
            DrawRime(geo);
            DrawIcicle(geo, drawPos, flipped, lightColor);
            return false;
        }
    }
}
