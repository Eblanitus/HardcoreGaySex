using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    // Снаряд дуги для серпов (текстура — ванильная дуга Excalibur).
    // Дуга — единственный удар взмаха (у самого серпа хитбокс убран,
    // см. SickleGlobalItem), по каждой цели раз за взмах. Бьёт ВИДИМОЕ лезвие:
    // точная зона по пикселям серпа (SickleSprite.GetZone), заметённая от прошлого
    // угла к текущему — полный урон. Древко не бьёт. Линия дуги (контроль: мелкий
    // урон и отброс) осталась только за кончиком лезвия, где она видна дальше серпа.
    public class SickleArcProjectile : ModProjectile
    {
        public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.Excalibur;

        // Геометрия нарисованной дуги Excalibur
        private const float BodyDrawScale = 0.75f;
        private const float LineDrawScale = 0.8f;
        private const float LineTexOuter = 82f;
        private static readonly float ArcAngleBack = MathHelper.ToRadians(74f);
        private static readonly float ArcAngleFront = MathHelper.ToRadians(56f);
        private const float ArcScaleStart = 1f;
        private const float ArcScaleGrowth = 0.6f;

        // Урон и отбрасывание на краю дуги
        private const float EdgeDamageMult = 0.15f;
        private const float EdgeKnockbackMult = 0.2f;
        private const float DamageFalloffSpan = 0.28f;
        private const float MinEdgeDamage = 1f;

        // Насколько кисть в ванильном взмахе отстоит от центра игрока
        public const float HandReach = 15f;

        // Докуда достаёт клинок: считает SickleGlobalItem у владельца
        public float BladeReach;

        // Что за серп и как он вращается: задаёт SickleGlobalItem.Shoot (бьёт только владелец,
        // синхронизировать не нужно). SwingFace — та же сторона, что у SickleSpinSwingLayer.
        public int ItemType;
        public float ItemScale = 1f;
        public float SwingFace = 1f;

        // false — убрать «контрольную» линию дуги за кончиком лезвия, бьёт только серп
        public static bool ArcLineControl = true;

        // Показывает пылью зону урона: зелёная — лезвие, голубая — кончик, розовая — край линии дуги
        public static bool DebugHitbox = false;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 4;
        }

        public override void SetDefaults()
        {
            Projectile.width = 16;
            Projectile.height = 16;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.ownerHitCheck = true;
            Projectile.ownerHitCheckDistance = 300f;
            Projectile.usesOwnerMeleeHitCD = false;
            Projectile.stopsDealingDamageAfterPenetrateHits = true;
            Projectile.aiStyle = -1;
            Projectile.noEnchantmentVisuals = true;
        }

        public override void AI()
        {
            Projectile.localAI[0]++;
            Player player = Main.player[Projectile.owner];
            // Взмах ледяным копьём тратит шкалу заморозки на иней: чем полнее была шкала, тем сильнее иней
            if (Projectile.localAI[0] == 1f && Projectile.owner == Main.myPlayer && ItemType == ItemID.IceSickle)
                frostStrong = player.GetModPlayer<SicklePlayer>().SpendFrostForSwing() >= SicklePlayer.FrostStrongAt;
            float maxAnim = Math.Max(1f, Projectile.ai[1]);
            GetArcState(player, Projectile.localAI[0], out float rotation, out float scale);
            Projectile.rotation = rotation;
            Projectile.scale = scale;
            Projectile.Center = player.RotatedRelativePoint(player.MountedCenter) - Projectile.velocity;

            if (DebugHitbox && Projectile.owner == Main.myPlayer)
                ShowHitbox(player);

            SwingGlowEffects(player);
            RecordFrostTrail(player);

            if (Projectile.localAI[0] >= maxAnim)
                Projectile.Kill();

            // Визуальные эффекты от фляг
            float enchantmentDistance = 40f * Projectile.ai[2];
            Vector2 enchantmentPosition = Projectile.Center + Projectile.rotation.ToRotationVector2() * enchantmentDistance;
            Rectangle rectangle = Utils.CenteredRectangle(enchantmentPosition, new Vector2(30f * Projectile.ai[2], 30f * Projectile.ai[2]));
            Projectile.EmitEnchantmentVisualsAt(rectangle.TopLeft(), rectangle.Width, rectangle.Height);
        }

        // === Нелинейная кривая вращения (замах → круг → торможение) ===
        // Доля времени на замах (отвод клинка назад) и глубина замаха (доля круга).
        private const float SpinWindupEnd = 0.14f;   // 14% времени — замах назад
        private const float SpinWindupDepth = 0.10f; // на 10% круга (~36°) отводим клинок назад

        // smootherstep: 6t^5 - 15t^4 + 10t^3 — нулевая скорость на концах (плавный старт/стоп).
        private static float Smootherstep(float t)
        {
            t = MathHelper.Clamp(t, 0f, 1f);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        // Нормализованный угол вращения [0..1] = полный круг, но НЕ линейно:
        // начало — плавный замах назад, середина — разгон, конец — мягкое торможение.
        // Скорость на стыках нулевая (обе части через smootherstep) — без рывка.
        // public static: эту же кривую использует SicklePlayer для вращения серпа в руке.
        public static float SpinCurve(float t)
        {
            if (t < SpinWindupEnd)
                return -SpinWindupDepth * Smootherstep(t / SpinWindupEnd);        // замах: клинок уходит назад
            float u = (t - SpinWindupEnd) / (1f - SpinWindupEnd);                 // остаток времени 0..1
            return -SpinWindupDepth + (1f + SpinWindupDepth) * Smootherstep(u);  // круг 360° с разгоном и торможением
        }

        private void GetArcState(Player player, float time, out float rotation, out float scale)
        {
            float percentageOfLife = time / Math.Max(1f, Projectile.ai[1]);
            // ПОЛНЫЙ КРУГ (360°) по НЕЛИНЕЙНОЙ кривой SpinCurve: замах назад → разгон → торможение.
            // TwoPi = 2π; множитель ai[0] (±1 по направлению взмаха) задаёт сторону вращения.
            rotation = Projectile.velocity.ToRotation() + Projectile.ai[0] * MathHelper.TwoPi * SpinCurve(percentageOfLife) + player.fullRotation + MathHelper.Pi;
            scale = (ArcScaleStart + percentageOfLife * ArcScaleGrowth) * Projectile.ai[2];
        }

        private void GetHitSector(Player player, float time, out Vector2 center, out float outer, out float startAngle, out float sweep)
        {
            GetArcState(player, time, out float rotation, out float scale);
            center = player.RotatedRelativePoint(player.MountedCenter);
            outer = Math.Max(LineTexOuter * scale * BodyDrawScale * LineDrawScale, BladeReach);
            startAngle = Projectile.ai[0] >= 0f ? rotation - ArcAngleBack : rotation - ArcAngleFront;
            sweep = ArcAngleBack + ArcAngleFront;
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            return TryHit(targetHitbox, out _);
        }

        // Угол оси серпа (приклад→острие) на долю взмаха progress: тот же, что рисует
        // SickleSpinSwingLayer (стартовый угол на курсор + сторона × круг по SpinCurve).
        private float BladeAngleAt(float progress)
        {
            return Projectile.velocity.ToRotation() + MathHelper.Pi
                + SwingFace * MathHelper.TwoPi * SpinCurve(progress);
        }

        // Прошлый и текущий угол лезвия. Прогресс берём у игрока (itemAnimation) —
        // его же использует слой, поэтому зона и картинка идут в один тик.
        private void GetBladeAngles(Player player, out float angleNow, out float anglePrev)
        {
            float max = Math.Max(1f, player.itemAnimationMax > 0 ? player.itemAnimationMax : Projectile.ai[1]);
            float progress = player.itemAnimation > 0
                ? 1f - player.itemAnimation / max
                : Projectile.localAI[0] / Math.Max(1f, Projectile.ai[1]);
            angleNow = BladeAngleAt(progress);
            anglePrev = BladeAngleAt(Math.Max(0f, progress - 1f / max));
        }

        // Спад от расстояния за кончиком лезвия: 0 — как лезвием, 1 — край линии дуги
        private float FalloffEdge(float distance, float reach)
        {
            float maxReach = LineTexOuter * BodyDrawScale * LineDrawScale * (ArcScaleStart + ArcScaleGrowth) * Projectile.ai[2];
            if (maxReach <= reach)
                return 0f;
            return MathHelper.Clamp((distance - reach) / (maxReach - reach), 0f, 1f);
        }

        // Зона лезвия по тому, что нарисовано: при шкале заморозки >= 0.5 замороженное копьё
        private SickleSprite.BladeZone Zone() =>
            SickleSprite.GetZone(ItemType, FrozenSpear.UseZone(Main.player[Projectile.owner], ItemType));

        // Попадание и «краевой» коэффициент урона edge (0 — полный, 1 — минимальный).
        // 1) Лезвие: точная зона по пикселям серпа, заметённая за тик — полный урон.
        // 2) Линия дуги (контроль) — только дальше кончика лезвия, урон падает к краю.
        // Нет рисунка (сервер) — прежний сектор от центра игрока до края дуги.
        private bool TryHit(Rectangle target, out float edge)
        {
            edge = 1f;
            Player player = Main.player[Projectile.owner];
            GetHitSector(player, Projectile.localAI[0], out Vector2 center, out float outer,
                out float startAngle, out float sweep);

            SickleSprite.BladeZone zone = Zone();
            if (zone == null)
            {
                if (!NearestInWedge(target, center, startAngle, sweep, out float distance, out _) || distance > outer)
                    return false;
                edge = FalloffEdge(distance, BladeReach);
                return true;
            }

            GetBladeAngles(player, out float angleNow, out float anglePrev);
            if (SickleSprite.BladeOverlaps(zone, target, player.MountedCenter, anglePrev, angleNow,
                SwingFace > 0f, ItemScale))
            {
                edge = 0f;
                return true;
            }

            if (!ArcLineControl)
                return false;
            GetArcState(player, Projectile.localAI[0], out _, out float arcScale);
            float lineOuter = LineTexOuter * arcScale * BodyDrawScale * LineDrawScale;
            float tipReach = zone.RMax * ItemScale;
            if (lineOuter <= tipReach)
                return false;
            if (!NearestInWedge(target, center, startAngle, sweep, out float dist, out float farthest) ||
                dist > lineOuter || farthest <= tipReach)
                return false;
            edge = FalloffEdge(dist, tipReach);
            return true;
        }

        private static readonly Vector2[] clipBufferA = new Vector2[16];
        private static readonly Vector2[] clipBufferB = new Vector2[16];

        private static bool NearestInWedge(Rectangle rect, Vector2 center, float startAngle, float sweep,
            out float distance, out float farthest)
        {
            distance = 0f;
            farthest = 0f;
            clipBufferA[0] = new Vector2(rect.Left, rect.Top) - center;
            clipBufferA[1] = new Vector2(rect.Right, rect.Top) - center;
            clipBufferA[2] = new Vector2(rect.Right, rect.Bottom) - center;
            clipBufferA[3] = new Vector2(rect.Left, rect.Bottom) - center;

            Vector2 startDir = startAngle.ToRotationVector2();
            Vector2 endDir = (startAngle + sweep).ToRotationVector2();
            int count = ClipByHalfPlane(clipBufferA, 4, clipBufferB, new Vector2(-startDir.Y, startDir.X));
            count = ClipByHalfPlane(clipBufferB, count, clipBufferA, new Vector2(endDir.Y, -endDir.X));
            if (count == 0)
                return false;

            float nearestSq = float.MaxValue, farthestSq = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector2 a = clipBufferA[i];
                Vector2 edge = clipBufferA[(i + 1) % count] - a;
                float lengthSq = edge.LengthSquared();
                float t = lengthSq > 0f ? MathHelper.Clamp(-Vector2.Dot(a, edge) / lengthSq, 0f, 1f) : 0f;
                nearestSq = Math.Min(nearestSq, (a + edge * t).LengthSquared());
                farthestSq = Math.Max(farthestSq, a.LengthSquared());
            }
            distance = (float)Math.Sqrt(nearestSq);
            farthest = (float)Math.Sqrt(farthestSq);
            return true;
        }

        private static int ClipByHalfPlane(Vector2[] input, int count, Vector2[] output, Vector2 normal)
        {
            int outCount = 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 a = input[i];
                Vector2 b = input[(i + 1) % count];
                float da = Vector2.Dot(a, normal);
                float db = Vector2.Dot(b, normal);
                if (da >= 0f)
                    output[outCount++] = a;
                if ((da >= 0f) != (db >= 0f))
                    output[outCount++] = a + (b - a) * (da / (da - db));
            }
            return outCount;
        }

        private void ShowHitbox(Player player)
        {
            GetHitSector(player, Projectile.localAI[0], out Vector2 center, out float outer, out float startAngle, out float sweep);
            SickleSprite.BladeZone zone = Zone();
            float inner = BladeReach;
            if (zone != null)
            {
                // Лезвие: то, что реально бьёт полным уроном
                GetBladeAngles(player, out float angleNow, out _);
                SickleSprite.EmitDebugDust(zone, player.MountedCenter, angleNow, SwingFace > 0f, ItemScale);
                inner = zone.RMax * ItemScale;
                GetArcState(player, Projectile.localAI[0], out _, out float arcScale);
                outer = LineTexOuter * arcScale * BodyDrawScale * LineDrawScale;
            }
            // Линия дуги: голубая — кончик лезвия (отсюда спад урона), розовая — край
            for (int i = 0; i <= 12; i++)
            {
                Vector2 dir = (startAngle + sweep * i / 12f).ToRotationVector2();
                Dust.NewDustPerfect(center + dir * inner, DustID.BlueFairy, Vector2.Zero).noGravity = true;
                if (zone == null || outer > inner)
                    Dust.NewDustPerfect(center + dir * outer, DustID.Pixie, Vector2.Zero).noGravity = true;
            }
        }

        public override void CutTiles()
        {
            Vector2 starting = (Projectile.rotation - MathHelper.PiOver4).ToRotationVector2() * 40f * Projectile.scale;
            Vector2 ending = (Projectile.rotation + MathHelper.PiOver4).ToRotationVector2() * 40f * Projectile.scale;
            float width = 40f * Projectile.scale;
            Utils.PlotTileLine(Projectile.Center + starting, Projectile.Center + ending, width, DelegateMethods.CutTiles);
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            // Урон и отбрасывание от радиуса: лезвием — как серпом, дальше кончика к краю линии — до крошечных
            TryHit(target.Hitbox, out float edge);
            float falloff = MathHelper.Clamp(edge / DamageFalloffSpan, 0f, 1f);
            falloff = falloff * falloff * (3f - 2f * falloff);
            float damageMult = MathHelper.Lerp(1f, EdgeDamageMult, falloff);
            if (Projectile.damage > 0)
                damageMult = Math.Max(damageMult, Math.Min(1f, MinEdgeDamage / Projectile.damage));
            modifiers.SourceDamage *= damageMult;
            modifiers.Knockback *= MathHelper.Lerp(1f, EdgeKnockbackMult, edge);
            modifiers.HitDirectionOverride = (Main.player[Projectile.owner].Center.X < target.Center.X) ? 1 : (-1);
        }

        // === Ледяная коса: Frostbite на попадании + полоса инея за дугой (только лёд, смерть не трогаем) ===
        private const int FrostbiteTicks = 270;       // 4.5 с Frostbite (BuffID.Frostburn2): вешает только сильный иней (FrostTrailSystem, зона добивания), не прямой удар
        private const int FrostTrailTicks = 60;       // сколько тиков держится иней на месте (потом тает)
        private const int FrostTrailSlowTicks = 75;   // длительность Slow на цели, вошедшей в иней (один раз за взмах на цель)
        private const float FrostTrailDensity = 0.012f; // шанс искорки инея на каждую точку следа за тик (редкие, угасают с возрастом)
        private const float FrostTrailSpacing = 10f;  // шаг точек вдоль дуги, px
        private const float FrostTrailRadius = 14f;   // насколько точка «захватывает» цель для замедления, px
        private const float FrostPatchSize = 7f;      // размер ледяного пятна у свежей точки, px (пиксельные квадраты)
        private const float FrostPatchMinScale = 0.35f; // до какой доли размера пятно сужается к концу таяния
        private const int FrostTrailCheckEvery = 3;   // проверка замедления раз в N тиков
        private static readonly float[] FrostTrailRadii = { 0.55f, 0.78f, 1f }; // доли длины клинка, где остаётся иней

        private FrostTrailSystem.Trail frostTrail;
        private bool frostStrong;   // шкала была полной в начале взмаха: иней сильный (только у владельца)

        // Пока идёт взмах, каждый тик добавляем точки на пройденной за тик части дуги (клинок по BladeAngleAt)
        private void RecordFrostTrail(Player player)
        {
            if (Main.dedServ)
                return;
            float max = SwingMaxTicks(player);
            float progress = SwingProgress(player);
            float prevProgress = Math.Max(0f, progress - 1f / max);
            float a0 = BladeAngleAt(prevProgress);
            float a1 = BladeAngleAt(progress);
            float tip = TipRadius();
            float arc = Math.Abs(a1 - a0) * tip;
            if (float.IsNaN(arc) || arc < 0.5f)
                return;
            if (frostTrail == null)
                frostTrail = FrostTrailSystem.Begin(Projectile.owner == Main.myPlayer, frostStrong);
            int n = Math.Clamp((int)Math.Ceiling(arc / FrostTrailSpacing), 1, 12);
            Vector2 center = player.MountedCenter;
            for (int i = 1; i <= n; i++)
            {
                float ang = MathHelper.Lerp(a0, a1, i / (float)n);
                Vector2 dir = ang.ToRotationVector2();
                foreach (float rm in FrostTrailRadii)
                    frostTrail.Add(center + dir * tip * rm + Main.rand.NextVector2Circular(3f, 3f));
            }
        }

        public override void OnKill(int timeLeft)
        {
            frostTrail?.Close();
            frostTrail = null;
        }

        // Параметры для FrostTrailSystem (константы живут здесь, наверху класса)
        internal static int FrostLife => FrostTrailTicks;
        internal static int FrostSlowTicks => FrostTrailSlowTicks;
        internal static int FrostbiteDuration => FrostbiteTicks;
        internal static float FrostDensity => FrostTrailDensity;
        internal static float FrostRadius => FrostTrailRadius;
        internal static int FrostCheckEvery => FrostTrailCheckEvery;
        internal static float FrostPatchSz => FrostPatchSize;
        internal static float FrostPatchMin => FrostPatchMinScale;

        public override void OnHitPlayer(Player target, Player.HurtInfo info)
        {
            info.HitDirection = (Main.player[Projectile.owner].Center.X < target.Center.X) ? 1 : (-1);
        }

        // === Свечение взмаха: вершинная лента по следу клинка + искры, кристаллики, пик-вспышка ===
        // Только визуал: зона урона не меняется. Рисуем аддитивно (A = 0).
        // Лёд — почти белое ядро и голубой хвост, смерть — светло-лиловое ядро и фиолетово-тёмный хвост.
        public static bool SwingGlow = true;

        // --- Огибающая по ходу взмаха (в тиках) ---
        private const float GlowRiseTicks = 3f;      // нарастание с начала взмаха
        private const float GlowFadeTicks = 7f;      // гашение в конце взмаха (между ними держится на максимуме)

        // --- Лента-шлейф: настоящая вершинная лента (VertexPositionColorTexture + BasicEffect), без своего .fx ---
        // Точки берём из той же кривой угла, что рисует серп (BladeAngleAt) — это и есть «история позиций»,
        // только без массива: прошлый угол лезвия считается напрямую. Лента лежит на дуге между
        // (TrailOuter - TrailWidth) и TrailOuter длины клинка от кисти. Диагональный срез — в запечённой маске.
        private const float CometTicks = 18f;          // длина хвоста искр/магии в тиках (ленты не касается)
        private const float TrailTicks = 12f;          // на сколько тиков назад тянется лента
        private const float TrailMaxAngle = 2.6f;      // и не больше этого угла, рад (иначе на разгоне лента обернётся вокруг игрока)
        private const int TrailPoints = 30;            // точек ленты (больше — глаже дуга)
        private const float TrailOuter = 1.0f;         // внешний (яркий) край ленты: доля длины клинка от кисти (1 — острие)
        private const float TrailWidth = 0.5f;         // ширина ленты: доля длины клинка (внутренний край = TrailOuter - TrailWidth)
        private const float TrailAlpha = 0.95f;        // общая яркость градиентного слоя
        private const float TrailCutSlope = 1f;        // наклон диагонального среза маски: больше — лента быстрее сужается к хвосту
        private const float TrailMaskPower = 1.25f;    // кривая падения яркости в маске (больше — резче затухание)
        private const float TrailTipSoft = 0.07f;      // мягкость внешнего края у острия (доля ширины ленты)
        private const float TrailHeadSoft = 0.02f;     // мягкость начала ленты у головы (доля длины ленты)
        private const float TrailCoreSpan = 0.55f;     // до какой доли ленты цвет светлее ядра, дальше уходит в цвет хвоста
        private const float TrailEdgeMix = 0.4f;       // насколько самый хвост темнеет к цвету края (0 — не темнеет)
        private const float TrailHeadDot = 18f;        // размер яркой точки у острия, px

        // --- Бегущие штрихи: второй проход, тонкие полосы вдоль ленты, сдвигаются по времени ---
        private const float StreakAlpha = 0.85f;       // яркость штрихов (0 — выключить слой)
        private const float StreakRepeat = 2.5f;       // сколько раз рисунок штрихов повторяется вдоль ленты
        private const float StreakSpeed = 0.06f;       // сдвиг рисунка к хвосту, циклов за тик
        private const float StreakTaper = 0.8f;        // как сужается полоса штрихов к хвосту (больше — быстрее)
        private const float StreakWidthMult = 0.95f;   // ширина полосы штрихов относительно градиентного слоя
        private const int StreakCount = 9;             // сколько штрихов поперёк ленты в маске
        private const float StreakThickness = 0.03f;   // толщина штриха (доля ширины ленты)

        // --- Широкий мягкий ореол под всем шлейфом ---
        private const float HaloTicks = 22f;         // длина ореола в тиках
        private const int HaloSteps = 10;
        private const float HaloWidth = 56f;         // ширина у острия, px
        private const float HaloWidthTail = 14f;     // ширина на конце, px
        private const float HaloAlpha = 0.22f;       // яркость у острия → 0 на конце

        // --- Рукоятка: то же самое, в меньшем масштабе ---
        private const float HandleScale = 0.35f;      // доля длины и ширины от острия
        private const float HandleAlphaMult = 0.4f / 0.9f; // альфа головы 0.4 вместо 0.9
        private const float HandleReach = 14f;        // px от кисти назад вдоль оси (конец древка), на масштабе 1

        // --- Искры ---
        private const int SparksPerTickTip = 4;
        private const int SparkEveryNTicksHandle = 1; // у рукоятки 1 искра раз в N тиков
        private const float SparkScaleMin = 0.9f;
        private const float SparkScaleMax = 1.6f;
        private const float SparkDrift = 1.1f;        // сила лёгкого дрейфа
        private const int SparksAlongTrailPerTick = 2; // искры, рассыпанные по хвосту (не только у острия)
        private const float SparkTrailSpread = 0.6f;   // как далеко по хвосту (доля CometTicks) могут появиться

        // --- Лёд: кристаллики, снежинки, морозный пар, иней у острия ---
        private const float IceCrystalChance = 0.5f;   // шанс кристаллика-пыли за тик (вдоль хвоста)
        private const float IceVaporChance = 0.45f;    // шанс клуба пара за тик
        private const float IceRimeChance = 0.6f;      // шанс «инея» (мелкой пыли) у острия за тик
        private const int IceSnowflakeCount = 3;       // сколько шестилучевых снежинок рисуем на хвосте
        private const float IceSnowflakeSize = 18f;    // их размер, px

        // --- Вспышка-импульс на пике взмаха ---
        private const float PeakAt = 0.5f;             // доля взмаха, где пик
        private const float PeakWidthTicks = 3f;       // ширина пика (тиков), плавно
        private const float PeakFlashSize = 70f;       // размер вспышки у острия, px
        private const float PeakFlashAlpha = 0.7f;     // её яркость
        private const float PeakStarSize = 64f;        // длина лучей звезды вспышки, px
        private const float PeakLightBoost = 0.8f;     // добавка света (множитель к базовому)
        private const int PeakBurstSparks = 14;        // искр-пыли в момент пика
        private const float PeakBurstSpeed = 3.5f;     // скорость разлёта

        // --- Блёстки-звёздочки вдоль хвоста (оба типа) ---
        private const int TwinkleCount = 5;            // сколько мерцающих звёздочек на хвосте
        private const float TwinkleSize = 12f;         // px
        private const float TwinkleAlpha = 0.9f;
        private const float TwinkleSpeed = 0.55f;      // скорость мерцания

        // --- Цвета: общая ледяная палитра серии ---
        private static readonly Color IceCore = new Color(235, 250, 255);   // ядро: почти белое
        private static readonly Color IceTail = new Color(120, 210, 255);   // хвост: голубой
        private static readonly Color IceEdge = new Color(40, 110, 230);    // край: глубокий синий

        private void GlowColors(out Color core, out Color tail, out Color edge)
        {
            core = IceCore;
            tail = IceTail;
            edge = IceEdge;
        }

        // Яркость: GlowRiseTicks нарастает с начала взмаха, GlowFadeTicks гаснет к концу
        private float GlowEnvelope(Player player, float progress)
        {
            float max = Math.Max(1f, player.itemAnimationMax > 0 ? player.itemAnimationMax : Projectile.ai[1]);
            float ticks = progress * max;
            float rise = Smootherstep(ticks / Math.Max(0.01f, GlowRiseTicks));
            float fall = Smootherstep((max - ticks) / Math.Max(0.01f, GlowFadeTicks));
            return rise * fall;
        }

        private float SwingProgress(Player player)
        {
            float max = Math.Max(1f, player.itemAnimationMax > 0 ? player.itemAnimationMax : Projectile.ai[1]);
            return player.itemAnimation > 0
                ? 1f - player.itemAnimation / max
                : Projectile.localAI[0] / Math.Max(1f, Projectile.ai[1]);
        }

        private float SwingMaxTicks(Player player)
        {
            return Math.Max(1f, player.itemAnimationMax > 0 ? player.itemAnimationMax : Projectile.ai[1]);
        }

        private float TipRadius()
        {
            SickleSprite.BladeZone zone = Zone();
            return (zone != null ? zone.RMax : BladeReach) * ItemScale;
        }

        // Ось серпа (острие) на долю взмаха progress; конец древка — противоположное направление
        private Vector2 EndPoint(Player player, float progress, bool tip, float radiusMult = 1f)
        {
            float axis = BladeAngleAt(Math.Max(0f, progress));
            Vector2 dir = axis.ToRotationVector2();
            return tip
                ? player.MountedCenter + dir * TipRadius() * radiusMult
                : player.MountedCenter - dir * HandleReach * ItemScale;
        }

        private void SwingGlowEffects(Player player)
        {
            if (!SwingGlow || Main.dedServ)
                return;
            float progress = SwingProgress(player);
            float env = GlowEnvelope(player, progress);
            if (env <= 0.02f)
                return;

            Vector2 tipPos = EndPoint(player, progress, true);
            Vector2 handlePos = EndPoint(player, progress, false);
            float axis = BladeAngleAt(progress);

            float max = SwingMaxTicks(player);
            float peak = PeakPulse(progress, max);

            Vector3 light = new Vector3(0.4f, 1f, 1.5f);
            Lighting.AddLight(tipPos, light * (env + peak * PeakLightBoost));
            Lighting.AddLight(player.MountedCenter + axis.ToRotationVector2() * TipRadius() * 0.55f, light * env * 0.8f);
            Lighting.AddLight(player.MountedCenter + axis.ToRotationVector2() * TipRadius() * 0.8f, light * env * 0.9f);

            // Искры: SparksPerTickTip на тик у острия, у рукоятки раз в N тиков
            for (int i = 0; i < SparksPerTickTip; i++)
                SpawnSpark(tipPos, env);
            if (SparkEveryNTicksHandle > 0 && (int)Projectile.localAI[0] % SparkEveryNTicksHandle == 0)
                SpawnSpark(handlePos, env * 0.7f);

            // Искры и магия по хвосту
            for (int i = 0; i < SparksAlongTrailPerTick; i++)
            {
                float back = Main.rand.NextFloat() * SparkTrailSpread * CometTicks / max;
                SpawnSpark(EndPoint(player, progress - back, true, Main.rand.NextFloat(0.8f, 1f)), env);
            }
            SpawnMagic(player, progress, max, env, tipPos);

            // Взрыв искр ровно в тик пика
            if ((int)Projectile.localAI[0] == (int)(max * PeakAt))
                for (int i = 0; i < PeakBurstSparks; i++)
                    SpawnBurstSpark(tipPos);
        }

        // Колокол вокруг PeakAt, 0..1
        private float PeakPulse(float progress, float max)
        {
            float d = (progress - PeakAt) * max / Math.Max(0.01f, PeakWidthTicks);
            return (float)Math.Exp(-d * d);
        }

        private void SpawnBurstSpark(Vector2 pos)
        {
            int type = Main.rand.NextBool() ? DustID.IceTorch : DustID.Frost;
            Dust d = Dust.NewDustPerfect(pos, type, Main.rand.NextVector2Circular(PeakBurstSpeed, PeakBurstSpeed), 100,
                default, Main.rand.NextFloat(1.2f, 2f));
            d.noGravity = true;
        }

        // Лёд: кристаллики, пар, иней
        private void SpawnMagic(Player player, float progress, float max, float env, Vector2 tipPos)
        {
            float back = Main.rand.NextFloat() * CometTicks / max;
            Vector2 pos = EndPoint(player, progress - back, true, Main.rand.NextFloat(0.75f, 1f));
            if (Main.rand.NextFloat() < IceCrystalChance * env)
            {
                Dust c = Dust.NewDustPerfect(pos, Main.rand.NextBool() ? DustID.Ice : DustID.SnowflakeIce,
                    Main.rand.NextVector2Circular(1.2f, 1.2f), 80, default, Main.rand.NextFloat(1f, 1.6f));
                c.noGravity = true;
            }
            if (Main.rand.NextFloat() < IceVaporChance * env)
            {
                Dust v = Dust.NewDustPerfect(pos, DustID.Smoke, Main.rand.NextVector2Circular(0.5f, 0.5f) + new Vector2(0f, -0.4f),
                    200, new Color(170, 225, 255), Main.rand.NextFloat(1.2f, 1.9f));
                v.noGravity = true;
                v.fadeIn = 0.5f;
            }
            if (Main.rand.NextFloat() < IceRimeChance * env)
            {
                Dust r = Dust.NewDustPerfect(tipPos + Main.rand.NextVector2Circular(8f, 8f), DustID.Frost,
                    Main.rand.NextVector2Circular(0.6f, 0.6f), 120, default, Main.rand.NextFloat(0.9f, 1.4f));
                r.noGravity = true;
            }
        }

        // Звезда из n лучей: тонкие растянутые пятна, повёрнутые равномерно
        private static void DrawSparkle(Texture2D glow, Vector2 pos, float size, float rotation, int rays,
            Color color, Color centerColor)
        {
            Vector2 origin = glow.Size() / 2f;
            for (int i = 0; i < rays; i++)
            {
                float rot = rotation + MathHelper.Pi * i / rays;
                Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, color, rot, origin,
                    new Vector2(size / glow.Width, size * 0.09f / glow.Height), SpriteEffects.None, 0f);
            }
            Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, centerColor, 0f, origin,
                size * 0.28f / glow.Width, SpriteEffects.None, 0f);
        }

        private void SpawnSpark(Vector2 pos, float env)
        {
            if (Main.rand.NextFloat() > env + 0.15f)
                return;
            int type = Main.rand.NextBool(3) ? DustID.SnowflakeIce : DustID.IceTorch;
            Dust d = Dust.NewDustPerfect(pos + Main.rand.NextVector2Circular(5f, 5f), type,
                Main.rand.NextVector2Circular(SparkDrift, SparkDrift), 100,
                default, Main.rand.NextFloat(SparkScaleMin, SparkScaleMax));
            d.noGravity = true;
            d.fadeIn = 0.4f;
            d.velocity.Y -= 0.15f;
        }

        // Аддитивный цвет: альфа уходит в яркость RGB (A = 0), альфу экрана не трогаем
        private static Color Additive(Color c, float alpha)
        {
            Color r = c * Math.Clamp(alpha, 0f, 1f);
            r.A = 0;
            return r;
        }

        // Мазок вдоль отрезка a→b: растянутое мягкое пятно (координаты мировые)
        private static void DrawSegment(Texture2D glow, Vector2 a, Vector2 b, float widthA, float widthB, Color color)
        {
            float width = (widthA + widthB) * 0.5f;
            if (width < 0.5f)
                return;
            Vector2 delta = b - a;
            Main.EntitySpriteDraw(glow, (a + b) * 0.5f - Main.screenPosition, null, color, delta.ToRotation(),
                glow.Size() / 2f, new Vector2((delta.Length() + width) / glow.Width, width / glow.Height),
                SpriteEffects.None, 0f);
        }

        // === Лента-шлейф: ресурсы (маски, эффект, блендинг создаются один раз) ===
        private static readonly float[] trailAngles = new float[TrailPoints];
        private static Texture2D trailMask;          // градиентная маска мерцающего шлейфа (первый проход)
        private static Texture2D streakMask;         // маска бегущих штрихов (второй проход)
        private static BasicEffect trailEffect;      // Position + Color, без текстур
        private static BlendState trailBlend;        // аддитивный блендинг (A = 0)
        private static RasterizerState trailRaster;
        private static VertexPositionColorTexture[] trailVerts = new VertexPositionColorTexture[0];

        public override void Unload()
        {
            if (Main.dedServ)
                return;
            Texture2D a = trailMask, b = streakMask;
            BasicEffect e = trailEffect;
            trailMask = null;
            streakMask = null;
            trailEffect = null;
            trailBlend = null;
            trailRaster = null;
            trailVerts = new VertexPositionColorTexture[0];
            Main.QueueMainThreadAction(() =>
            {
                a?.Dispose();
                b?.Dispose();
                e?.Dispose();
            });
        }

        private static float Smooth01(float x)
        {
            x = MathHelper.Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        // Запекаем маску ленты в текстуру один раз: яркость по формуле value(u,v)
        private static Texture2D MakeMask(int w, int h, Func<float, float, float> value)
        {
            Texture2D tex = new Texture2D(Main.graphics.GraphicsDevice, w, h);
            Color[] data = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    int b = (int)(MathHelper.Clamp(value(u, v), 0f, 1f) * 255f + 0.5f);
                    data[y * w + x] = new Color(b, b, b, b);   // premultiplied: rgb = alpha
                }
            }
            tex.SetData(data);
            return tex;
        }

        // Маска градиента: u — вдоль ленты (0 голова, 1 хвост), v — поперёк (0 внешний край, 1 внутренний)
        private static float TrailMaskValue(float u, float v)
        {
            float cut = MathHelper.Clamp(1f - u - v * TrailCutSlope, 0f, 1f);
            float a = (float)Math.Pow(cut, TrailMaskPower);
            a *= Smooth01(v / Math.Max(0.001f, TrailTipSoft));
            a *= Smooth01(u / Math.Max(0.001f, TrailHeadSoft));
            return a;
        }

        // Маска штрихов: тонкие продольные полосы разной длины, тайлится по u
        private static Texture2D MakeStreakMask()
        {
            Random rng = new Random(7);
            float[] pos = new float[StreakCount];
            float[] phase = new float[StreakCount];
            int[] freq = new int[StreakCount];
            float[] gain = new float[StreakCount];
            for (int s = 0; s < StreakCount; s++)
            {
                pos[s] = (s + 0.3f + 0.4f * (float)rng.NextDouble()) / StreakCount;
                phase[s] = (float)rng.NextDouble();
                freq[s] = 2 + rng.Next(4);
                gain[s] = 0.55f + 0.45f * (float)rng.NextDouble();
            }
            return MakeMask(256, 64, (u, v) =>
            {
                float best = 0f;
                for (int s = 0; s < StreakCount; s++)
                {
                    float d = (v - pos[s]) / StreakThickness;
                    float line = (float)Math.Exp(-d * d);
                    if (line < 0.01f)
                        continue;
                    float along = 0.5f + 0.5f * (float)Math.Sin(MathHelper.TwoPi * (freq[s] * u + phase[s]));
                    best = Math.Max(best, line * gain[s] * (float)Math.Pow(along, 2.5f));
                }
                return best;
            });
        }

        private static bool EnsureTrailAssets(GraphicsDevice device)
        {
            if (device == null)
                return false;
            if (trailEffect == null || trailEffect.IsDisposed)
            {
                trailEffect = new BasicEffect(device)
                {
                    TextureEnabled = true,
                    VertexColorEnabled = true,
                    LightingEnabled = false,
                    FogEnabled = false
                };
            }
            if (trailMask == null || trailMask.IsDisposed)
                trailMask = MakeMask(128, 64, TrailMaskValue);
            if (streakMask == null || streakMask.IsDisposed)
                streakMask = MakeStreakMask();
            trailBlend ??= new BlendState
            {
                ColorSourceBlend = Blend.One,
                ColorDestinationBlend = Blend.One,
                AlphaSourceBlend = Blend.Zero,
                AlphaDestinationBlend = Blend.One
            };
            trailRaster ??= new RasterizerState { CullMode = CullMode.None };
            return true;
        }

        private static void EnsureVerts(int points)
        {
            if (trailVerts.Length < points * 2)
                trailVerts = new VertexPositionColorTexture[Math.Max(64, points * 2)];
        }

        // Один проход: полоса из n пар вершин вокруг center. streaks=false — градиентный
        // слой, streaks=true — бегущие штрихи (узкая полоса, тайлится и сдвигается по времени).
        private static void DrawTrailPass(GraphicsDevice device, bool streaks, int n, Vector2 center,
            float rOut, float rIn, float scrollTicks, float env, Color core, Color tail, Color edge)
        {
            float scroll = scrollTicks * StreakSpeed;
            for (int i = 0; i < n; i++)
            {
                float t = n > 1 ? i / (float)(n - 1) : 0f;
                Vector2 dir = trailAngles[i].ToRotationVector2();
                float inner = rIn;
                float uv = t;
                float strength;
                Color baseColor = Color.Lerp(Color.Lerp(core, tail, Smooth01(t / TrailCoreSpan)), edge, t * t * TrailEdgeMix);
                if (streaks)
                {
                    inner = rOut - (rOut - rIn) * StreakWidthMult * (float)Math.Pow(1f - t, StreakTaper);
                    uv = t * StreakRepeat - scroll;
                    strength = StreakAlpha * env * (float)Math.Pow(1f - t, 1.2f) * Utils.GetLerpValue(0f, 0.05f, t, true);
                    baseColor = Color.Lerp(baseColor, core, 0.5f);
                }
                else
                {
                    strength = TrailAlpha * env;
                }
                Color c = baseColor * strength;
                c.A = 0;
                trailVerts[i * 2] = new VertexPositionColorTexture(new Vector3(center + dir * rOut - Main.screenPosition, 0f), c, new Vector2(uv, 0f));
                trailVerts[i * 2 + 1] = new VertexPositionColorTexture(new Vector3(center + dir * inner - Main.screenPosition, 0f), c, new Vector2(uv, 1f));
            }
            trailEffect.Texture = streaks ? streakMask : trailMask;
            device.SamplerStates[0] = streaks ? SamplerState.LinearWrap : SamplerState.LinearClamp;
            trailEffect.CurrentTechnique.Passes[0].Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, trailVerts, 0, n * 2 - 2);
        }

        // История позиций: углы лезвия от головы (текущий угол) назад по времени. Длина ограничена и по тикам,
        // и по углу; последняя точка подрезается ровно по TrailMaxAngle. На старте взмаха точек мало — лента короткая.
        private int BuildTrailAngles(float progress, float max)
        {
            float head = BladeAngleAt(progress);
            trailAngles[0] = head;
            int n = 1;
            float step = TrailTicks / max / (TrailPoints - 1);
            for (int i = 1; i < TrailPoints; i++)
            {
                float p = progress - i * step;
                if (p < 0f)
                    break;
                float angle = BladeAngleAt(p);
                float delta = Math.Abs(angle - head);
                if (delta > TrailMaxAngle)
                {
                    float prevDelta = Math.Abs(trailAngles[n - 1] - head);
                    float f = (TrailMaxAngle - prevDelta) / Math.Max(0.0001f, delta - prevDelta);
                    trailAngles[n] = MathHelper.Lerp(trailAngles[n - 1], angle, MathHelper.Clamp(f, 0f, 1f));
                    n++;
                    break;
                }
                trailAngles[n++] = angle;
            }
            return n;
        }

        // Лента по следу острия (градиент + бегущие штрихи) + яркая точка у острия.
        // SpriteBatch на время рисования ленты закрываем и открываем заново с настройками ванильных снарядов.
        private void DrawTrail(Player player, Texture2D glow, float progress, float max, float env)
        {
            float tip = TipRadius();
            int n = BuildTrailAngles(progress, max);
            GraphicsDevice device = Main.graphics.GraphicsDevice;
            if (tip > 1f && n >= 2 && EnsureTrailAssets(device))
            {
                GlowColors(out Color core, out Color tail, out Color edge);
                EnsureVerts(n);
                float rOut = tip * TrailOuter;
                float rIn = tip * (TrailOuter - TrailWidth);
                Vector2 center = player.MountedCenter;
                trailEffect.World = Matrix.Identity;
                trailEffect.View = Main.GameViewMatrix.TransformationMatrix;
                trailEffect.Projection = Matrix.CreateOrthographicOffCenter(0f, device.Viewport.Width, device.Viewport.Height, 0f, -1f, 1f);
                device.BlendState = trailBlend;
                device.DepthStencilState = DepthStencilState.None;
                device.RasterizerState = trailRaster;
                Main.spriteBatch.End();
                try
                {
                    DrawTrailPass(device, false, n, center, rOut, rIn, Projectile.localAI[0], env, core, tail, edge);
                    if (StreakAlpha > 0f)
                        DrawTrailPass(device, true, n, center, rOut, rIn, Projectile.localAI[0], env, core, tail, edge);
                }
                finally
                {
                    Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                        DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
                }
            }

            // яркая точка у острия — голова ленты
            GlowColors(out Color headCore, out _, out _);
            Main.EntitySpriteDraw(glow, EndPoint(player, progress, true) - Main.screenPosition, null,
                Additive(headCore, env), 0f, glow.Size() / 2f, TrailHeadDot * 1.6f / glow.Width, SpriteEffects.None, 0f);
        }

        // Широкий мягкий ореол под шлейфом
        private void DrawHalo(Player player, Texture2D glow, float progress, float max, float env, bool tip, float sizeMult)
        {
            GlowColors(out _, out Color tail, out Color edge);
            float alphaHead = HaloAlpha * env * (tip ? 1f : HandleAlphaMult);
            float length = HaloTicks * sizeMult;
            Vector2 prev = EndPoint(player, progress, tip);
            for (int j = 1; j <= HaloSteps; j++)
            {
                float t0 = (j - 1) / (float)HaloSteps;
                float t1 = j / (float)HaloSteps;
                Vector2 next = EndPoint(player, progress - t1 * length / max, tip);
                float w0 = MathHelper.Lerp(HaloWidth, HaloWidthTail, t0) * sizeMult;
                float w1 = MathHelper.Lerp(HaloWidth, HaloWidthTail, t1) * sizeMult;
                float a = alphaHead * (1f - (t0 + t1) * 0.5f);
                DrawSegment(glow, prev, next, w0, w1, Additive(Color.Lerp(tail, edge, 0.5f), a));
                prev = next;
            }
        }

        // Магические детали на хвосте: снежинки и мерцающие блёстки
        private void DrawMagicDetails(Player player, Texture2D glow, float progress, float max, float env)
        {
            GlowColors(out Color core, out Color tail, out Color edge);
            float time = Projectile.localAI[0] * TwinkleSpeed;
            int specialCount = IceSnowflakeCount;
            float specialSize = IceSnowflakeSize;
            int rays = 3;
            for (int i = 0; i < specialCount; i++)
            {
                float t = (i + 0.5f) / specialCount * 0.8f;
                float radius = 0.72f + 0.26f * (float)Math.Sin(i * 2.1f);
                Vector2 pos = EndPoint(player, progress - t * CometTicks / max, true, radius);
                float fade = (1f - t) * env;
                float spin = Projectile.localAI[0] * 0.12f + i * 1.3f;
                DrawSparkle(glow, pos, specialSize * (1f - t * 0.4f), spin, rays,
                    Additive(tail, fade * 0.85f), Additive(core, fade));
            }
            for (int i = 0; i < TwinkleCount; i++)
            {
                float t = (i + 0.5f) / TwinkleCount;
                float tw = 0.5f + 0.5f * (float)Math.Sin(time + i * 1.9f);
                if (tw < 0.25f)
                    continue;
                float radius = 0.62f + 0.36f * (float)Math.Abs(Math.Sin(i * 3.7f));
                Vector2 pos = EndPoint(player, progress - t * HaloTicks / max, true, radius);
                DrawSparkle(glow, pos, TwinkleSize * tw, i + time * 0.3f, 2,
                    Additive(core, TwinkleAlpha * tw * env * (1f - t * 0.6f)), Additive(core, TwinkleAlpha * tw * env));
            }
        }

        // Вспышка у острия на пике: мягкое пятно + длинная звезда
        private void DrawPeakFlash(Player player, Texture2D glow, float progress, float max, float env)
        {
            float peak = PeakPulse(progress, max) * env;
            if (peak <= 0.03f)
                return;
            GlowColors(out Color core, out Color tail, out Color edge);
            Vector2 tipPos = EndPoint(player, progress, true);
            Vector2 origin = glow.Size() / 2f;
            Main.EntitySpriteDraw(glow, tipPos - Main.screenPosition, null, Additive(edge, peak * PeakFlashAlpha), 0f, origin,
                PeakFlashSize * (0.7f + 0.5f * peak) / glow.Width, SpriteEffects.None, 0f);
            Main.EntitySpriteDraw(glow, tipPos - Main.screenPosition, null, Additive(core, peak * PeakFlashAlpha), 0f, origin,
                PeakFlashSize * 0.4f / glow.Width, SpriteEffects.None, 0f);
            float axis = BladeAngleAt(progress);
            DrawSparkle(glow, tipPos, PeakStarSize * peak, axis + MathHelper.PiOver4, 2,
                Additive(Color.Lerp(tail, core, 0.5f), peak * 0.9f), Additive(core, peak));
        }

        private void DrawBladeGlow(Player player)
        {
            if (!SwingGlow || Main.dedServ)
                return;
            float progress = SwingProgress(player);
            float env = GlowEnvelope(player, progress);
            if (env <= 0.02f)
                return;

            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow == null || glow.IsDisposed)
                return;

            float max = SwingMaxTicks(player);
            DrawHalo(player, glow, progress, max, env, true, 1f);
            DrawHalo(player, glow, progress, max, env, false, HandleScale);
            DrawTrail(player, glow, progress, max, env);
            DrawMagicDetails(player, glow, progress, max, env);
            DrawPeakFlash(player, glow, progress, max, env);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Vector2 position = Projectile.Center - Main.screenPosition;
            Texture2D texture = TextureAssets.Projectile[Type].Value;
            if (texture == null || texture.IsDisposed)
                texture = ModContent.Request<Texture2D>("Terraria/Images/Projectile_" + ProjectileID.Excalibur).Value;
            if (texture == null || texture.IsDisposed)
                return false;
            Rectangle sourceRectangle = texture.Frame(1, 4);
            Vector2 origin = sourceRectangle.Size() / 2f;
            float scale = Projectile.scale * BodyDrawScale;
            SpriteEffects spriteEffects = ((!(Projectile.ai[0] >= 0f)) ? SpriteEffects.FlipVertically : SpriteEffects.None);
            float maxAnim = Math.Max(1f, Projectile.ai[1]);
            float percentageOfLife = Projectile.localAI[0] / maxAnim;
            float lerpTime = Utils.Remap(percentageOfLife, 0f, 0.6f, 0f, 1f) * Utils.Remap(percentageOfLife, 0.6f, 1f, 1f, 0f);

            // Дуга: белая полупрозрачная линия (как у ванильного серпа)
            Main.EntitySpriteDraw(texture, position, texture.Frame(1, 4, 0, 3), Color.White * 0.25f * lerpTime,
                Projectile.rotation, origin, scale * LineDrawScale, spriteEffects, 0f);

            Player owner = Main.player[Projectile.owner];
            DrawBladeGlow(owner);

            return false;
        }
    }

    // Полоса инея: живёт отдельно от снаряда дуги (тот умирает в конце взмаха), хранит точки с временем рождения.
    // Частицы — на всех клиентах (каждый считает свои точки из того же взмаха), замедление вешает только владелец.
    public class FrostTrailSystem : ModSystem
    {
        public class Trail
        {
            public readonly List<Vector2> Pos = new List<Vector2>();
            public readonly List<int> Born = new List<int>();
            public readonly List<int> Seed = new List<int>();
            public readonly List<float> Weight = new List<float>(); // плотность пятна: у блока 1, в воздухе меньше, в блоке 0 (не рисуем)
            public readonly HashSet<int> Slowed = new HashSet<int>();
            public bool Mine;
            public bool Strong;   // сильный иней: от полной заморозки, другой вид, вешает Frostbite
            public bool Open = true;
            public bool Visible = true;   // false — только механика (Slow), без пятен и искр инея
            public int Tick;

            public void Add(Vector2 p)
            {
                Pos.Add(p);
                Born.Add(Tick);
                Seed.Add(Main.rand.Next());
                Weight.Add(SurfaceWeight(p));
            }

            public void Close() => Open = false;
        }

        private static readonly List<Trail> trails = new List<Trail>();

        // Есть ли рядом точка инея: по ней мечи решают, что враг стоит «на льду»
        public static bool AnyNear(Vector2 point, float radius)
        {
            float r2 = radius * radius;
            for (int ti = 0; ti < trails.Count; ti++)
            {
                List<Vector2> pos = trails[ti].Pos;
                for (int i = 0; i < pos.Count; i++)
                    if (Vector2.DistanceSquared(pos[i], point) <= r2)
                        return true;
            }
            return false;
        }

        // Мягкое пятно в стиле зоны (FrostZoneSystem): обычный иней тоньше и тише, сильный ярче и плотнее
        private const float TrailBlobAlpha = 0.3f;      // прозрачность одного пятна (перекрываются)
        private const float TrailBlobSizeMult = 3f;     // диаметр пятна = FrostPatchSz * это
        private const float AirWeight = 0.65f;          // плотность вдали от блоков против поверхности
        private const float SurfaceSizeBoost = 0.3f;    // у блока пятна крупнее на столько (доля)

        // Рядом с блоком (сама клетка пустая, сосед сплошной) — 1; в воздухе AirWeight; внутри блока 0
        private static float SurfaceWeight(Vector2 p)
        {
            Point t = p.ToTileCoordinates();
            if (!WorldGen.InWorld(t.X, t.Y, 3))
                return 0f;
            if (WorldGen.SolidTile(t.X, t.Y))
                return 0f;
            return WorldGen.SolidTile(t.X - 1, t.Y) || WorldGen.SolidTile(t.X + 1, t.Y) ||
                   WorldGen.SolidTile(t.X, t.Y - 1) || WorldGen.SolidTile(t.X, t.Y + 1) ? 1f : AirWeight;
        }

        // Сильный иней относительно обычного
        private const float StrongAlphaMult = 1.5f;     // плотность пятен
        private const float StrongSizeMult = 1.3f;      // размер пятен
        private const float StrongDensityMult = 3f;     // частота искр

        public static Trail Begin(bool mine, bool strong = false, bool visible = true)
        {
            Trail t = new Trail { Mine = mine, Strong = strong, Visible = visible };
            trails.Add(t);
            return t;
        }

        public override void OnWorldUnload()
        {
            trails.Clear();
        }

        public override void PostUpdateEverything()
        {
            if (trails.Count == 0)
                return;
            int life = SickleArcProjectile.FrostLife;
            for (int ti = trails.Count - 1; ti >= 0; ti--)
            {
                Trail t = trails[ti];
                t.Tick++;

                // тает: выкидываем протухшие точки (они лежат по порядку рождения)
                int dead = 0;
                while (dead < t.Born.Count && t.Tick - t.Born[dead] >= life)
                    dead++;
                if (dead > 0)
                {
                    t.Pos.RemoveRange(0, dead);
                    t.Born.RemoveRange(0, dead);
                    t.Seed.RemoveRange(0, dead);
                    t.Weight.RemoveRange(0, dead);
                }
                if (t.Pos.Count == 0)
                {
                    if (!t.Open)
                        trails.RemoveAt(ti);
                    continue;
                }

                if (!Main.dedServ && t.Visible)
                    SpawnFrost(t, life, t.Strong);
                if (t.Mine && t.Tick % SickleArcProjectile.FrostCheckEvery == 0)
                    SlowNpcs(t);
            }
        }

        // Полоса инея: полупрозрачные пиксельные пятна по точкам следа (MagicPixel 1x1) + светлая неровная кромка.
        // Лежит на месте под NPC (после тайлов), с возрастом сужается и прозрачнеет. Alpha-blend, без свечения.
        public override void PostDrawTiles()
        {
            if (trails.Count == 0 || Main.dedServ)
                return;
            Texture2D tex = FrostZoneSystem.BlobTexture();
            if (tex == null)
                return;
            int life = SickleArcProjectile.FrostLife;
            Rectangle view = new Rectangle((int)Main.screenPosition.X - 32, (int)Main.screenPosition.Y - 32,
                Main.screenWidth + 64, Main.screenHeight + 64);
            Vector2 origin = new Vector2(tex.Width * 0.5f);
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
            foreach (Trail t in trails)
            {
                if (!t.Visible)
                    continue;
                Color body = t.Strong ? FrostZoneSystem.StrongBody : FrostZoneSystem.WeakBody;
                float alphaMul = TrailBlobAlpha * (t.Strong ? StrongAlphaMult : 1f);
                float sizeMul = t.Strong ? StrongSizeMult : 1f;
                for (int i = 0; i < t.Pos.Count; i++)
                {
                    float weight = t.Weight[i];
                    if (weight <= 0f)
                        continue;
                    Vector2 p = t.Pos[i];
                    if (!view.Contains(p.ToPoint()))
                        continue;
                    float age = Math.Clamp((t.Tick - t.Born[i]) / (float)life, 0f, 1f);
                    // плавно появляется за первые тики и тает к концу
                    float a = Utils.GetLerpValue(0f, 0.08f, age, true) * (1f - age * age) * alphaMul * weight;
                    if (a < 0.01f)
                        continue;
                    float scale = MathHelper.Lerp(1f, SickleArcProjectile.FrostPatchMin, age * age);
                    int seed = t.Seed[i];
                    float rnd = (seed & 255) / 255f;
                    float surface = 1f + SurfaceSizeBoost * (weight >= 1f ? 1f : 0f);
                    float size = SickleArcProjectile.FrostPatchSz * TrailBlobSizeMult * sizeMul * scale *
                                 (0.8f + 0.4f * rnd) * surface / tex.Width;
                    float sx = 0.8f + 0.45f * (((seed >> 8) & 255) / 255f);
                    float sy = 0.8f + 0.45f * (((seed >> 16) & 255) / 255f);
                    float rot = ((seed >> 4) & 1023) / 1023f * MathHelper.TwoPi;
                    Color light = Lighting.GetColor((int)(p.X / 16f), (int)(p.Y / 16f));
                    float lm = 0.6f + 0.4f * Math.Max(light.R, Math.Max(light.G, light.B)) / 255f;
                    Main.spriteBatch.Draw(tex, p - Main.screenPosition, null, body * (a * lm), rot, origin,
                        new Vector2(size * sx, size * sy), SpriteEffects.None, 0f);
                }
            }
            Main.spriteBatch.End();
        }

        private static void SpawnFrost(Trail t, int life, bool strong)
        {
            float density = SickleArcProjectile.FrostDensity * (strong ? StrongDensityMult : 1f);
            for (int i = 0; i < t.Pos.Count; i++)
            {
                float fade = 1f - (t.Tick - t.Born[i]) / (float)life;
                if (Main.rand.NextFloat() > density * fade)
                    continue;
                int type = strong
                    ? (Main.rand.NextBool(2) ? DustID.IceTorch : DustID.Frost)
                    : (Main.rand.NextBool(3) ? DustID.SnowflakeIce : (Main.rand.NextBool() ? DustID.Frost : DustID.IceTorch));
                Dust d = Dust.NewDustPerfect(t.Pos[i] + Main.rand.NextVector2Circular(4f, 4f), type,
                    new Vector2(Main.rand.NextFloat(-0.25f, 0.25f), Main.rand.NextFloat(0.05f, 0.35f)), 120,
                    default, Main.rand.NextFloat(0.5f, 0.85f) * (strong ? 1.35f : 1f));
                d.noGravity = true;
                d.fadeIn = 0.3f;
            }
        }

        private static void SlowNpcs(Trail t)
        {
            int r = (int)SickleArcProjectile.FrostRadius;
            for (int n = 0; n < Main.maxNPCs; n++)
            {
                NPC npc = Main.npc[n];
                if (!npc.active || npc.friendly || npc.dontTakeDamage || npc.immortal || t.Slowed.Contains(n))
                    continue;
                Rectangle box = npc.Hitbox;
                box.Inflate(r, r);
                for (int i = 0; i < t.Pos.Count; i++)
                {
                    if (box.Contains(t.Pos[i].ToPoint()))
                    {
                        npc.AddBuff(BuffID.Slow, SickleArcProjectile.FrostSlowTicks);
                        // Сильный иней жжёт: тот же Frostbite, что на атаках
                        if (t.Strong)
                            npc.AddBuff(BuffID.Frostburn2, SickleArcProjectile.FrostbiteDuration);
                        t.Slowed.Add(n);
                        break;
                    }
                }
            }
        }
    }
}
