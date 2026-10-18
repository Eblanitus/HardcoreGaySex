using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace ReaperMelee.Content.Projectiles
{
    // Выпад серпом: косой «взмах-тычок» по эллипсу вместо прямого выпада.
    // ai[0] — сколько тиков живёт выпад (интервал тычков из SicklePlayer)
    // ai[2] — тип предмета (по нему рисуем его текстуру)
    public class SickleStabProjectile : ModProjectile
    {
        // Страховочная текстура (своя отрисовка всё равно по ai[2])
        public override string Texture => "ReaperMelee/Items/DeathSpear";

        // === Траектория выпада: эллипс, вытянутый вдоль линии на курсор ===
        // За всё время жизни (t: 0 → 1) ТОЧКА ХВАТА (место на древке, за которое держат) обходит эллипс. В системе
        // «ось X вдоль курсора, Y поперёк», с началом у плеча игрока:
        //   P(t) = ( a·cos(th) + a + k,  b·sin(th) ),   th = π + 2π·t
        // t=0 — копьё у тела (хват в k от плеча, k = длина согнутой руки), t=0.5 — копьё выдвинуто вперёд максимально
        // (хват в 2a + k, y = 0), t=1 — снова у тела; по пути хват описывает боковую петлю: сначала в одну сторону,
        // потом в другую. Копьё едет независимо от руки: рука тянется до предела, остальное добирает скольжение
        // древка в кисти. Жёсткий предел выноса — по длине древка: приклад в пике не дальше кисти.
        // Форма дуги у каждого тычка своя (как у настоящей глефы): сплюснутость b/a выбирается случайно,
        // чаще средняя, крайности реже. Значение считается от id снаряда, поэтому все клиенты видят ту же дугу
        private const float EllipseFlatMin = 0.02f;     // b/a: почти прямой тычок как копьём
        private const float EllipseFlatMax = 0.3f;      // b/a: самая широкая дуга (при a≈42 px это ≈13 px в сторону)
        public static bool RandomSide = true;           // сторона первой петли тоже случайная (иначе всегда EllipseSide)
        private const float EllipseOffset = 0f;         // k = длина согнутой руки + это, px (хват при t=0)
        private const float EllipseReachMult = 0.5f;    // верхняя граница a в длинах копья; фактически a режется длиной древка (приклад в пике у кисти)
        private const float EllipseSide = 1f;           // в какую сторону первая петля: +1 — вверх, -1 — вниз (зеркалится по направлению игрока; при RandomSide — основа)
        // Как у глефы: игрок держит копьё не за конец, а ближе к середине. Спереди копьё выступает на (1 − GripFromButt)·длину,
        // сзади остаётся хвост GripFromButt·длины (в пике хвост уходит в кисть: рука держит у приклада)
        private const float GripFromButt = 0.4f;        // доля длины древка от нижнего конца до хвата
        private const float EllipseAxisBlend = 0.35f;   // ось: 0 — вдоль курсора, 1 — вдоль линии «плечо → хват» (чем меньше, тем меньше качается копьё; боковая петля хвата от этого не меняется)
        private const float MaxAxisDeviation = 0.3f;    // рад: предел отклонения оси от линии на курсор (угол качания копья)
        // true — время t идёт рывок-пауза-возврат (кривая ниже), false — линейно, как у ванили
        public static bool UseEase = false;
        // Кривая для UseEase (доли времени жизни; остаток после Thrust+Hold — возврат)
        private const float ThrustPortion = 0.22f;   // рывок вперёд
        private const float HoldPortion = 0.14f;     // пауза в точке выпада
        private const float ThrustPower = 3f;        // резкость рывка: ease-out (1-(1-t)^N), больше — резче старт
        // Урон только пока серп выброшен: t от 0 до HitWindow (t=0.5 — пик, дальше возврат)
        private const float HitWindow = 0.6f;

        // === Рука: держит серп за древко, скользя по нему ===
        private const float GripMaxFrac = 0.7f;      // дальше этой доли длины от приклада кисть по древку не уезжает
        private const float ArmFitTol = 4f;          // px: на сколько кисть может уехать от точки хвата по древку, прежде чем сменится ступень руки (больше половины шага ступеней — без дрожи)
        private const float SweepStep = 8f;          // px: шаг проверки хитбокса вдоль пути серпа за тик

        // === Свечение выпада ===
        public static bool StabGlow = true;
        private const float GlowRiseTicks = 4f;      // нарастание с начала выпада
        private const float GlowFadeTicks = 10f;     // гашение к концу (между ними держится)
        private const float HeadAhead = 12f;         // px: голова кометы впереди острия (в точке выпада)
        private const float HeadSize = 70f;          // размер мягкой головы, px
        private const float HeadAlpha = 0.45f;
        private const float CometTailLen = 70f;      // px: хвост кометы назад от острия вдоль древка
        private const float CometHeadWidth = 40f;    // ширина кометы у головы, px
        private const float CometTailWidth = 10f;     // ширина на конце хвоста, px
        private const int CometSteps = 8;
        private const float CometAlpha = 0.5f;
        private const float TrailTicks = 7f;         // сколько тиков назад тянется след острия при рывке
        private const float TrailWidth = 28f;        // его ширина у острия, px
        private const float TrailAlpha = 0.45f;
        private const float PeakWidthTicks = 2f;     // ширина вспышки (тиков), плавно
        private const float PeakFlashSize = 40f;      // размер блика на кончике в точке выпада (без попадания), px
        private const float PeakFlashAlpha = 0.4f;
        private const float PeakStarSize = 35f;      // длина лучей звезды блика, px
        private const int PeakBurstSparks = 2;       // пыль-искры в момент макс. выпада (без попадания)
        private const float PeakBurstSpeed = 1.8f;
        private const float PeakLightBoost = 0.7f;
        // === Эффект удара: в точке попадания на лезвии (ближайшая к цели точка зоны лезвия) ===
        private const float HitFlashSize = 90f;      // вспышка, px
        private const float HitFlashAlpha = 0.5f;
        private const float HitStarSize = 70f;       // длина лучей звезды, px
        private const int HitFlashTicks = 6;         // сколько тиков держится вспышка
        private const int HitBurstSparks = 8;       // искры взрыва
        private const float HitBurstSpeed = 3f;    // их скорость от точки удара
        private const int HitFlakes = 3;             // снежинки / клубы тёмного дыма
        private const float HitLightBoost = 1f;
        private const int SparksPerTick = 2;         // искры у острия за тик
        private const float SparkDrift = 0.5f;
        private const float SparkForward = 0.5f;     // скорость искр вперёд по ходу выпада
        private const int StarCount = 2;             // звёздочки-блёстки на острии/комете
        private const float StarSize = 24f;          // px
        private const float StarAlpha = 0.45f;
        private const float StarSpeed = 0.6f;        // скорость мерцания
        private const float DarkSparkChance = 0.3f;  // шанс тёмного клуба дыма за тик
        private const float HaloSize = 90f;         // мягкий ореол вокруг лезвия, px
        private const float HaloAlpha = 0.2f;
        private const float ShaftGlowWidth = 9f;    // свечение вдоль древка от кисти к острию, px
        private const float ShaftGlowAlpha = 0.25f;
        private const float ShaftSparkChance = 0.25f; // искры/снежинки вдоль древка за тик
        private const float LightMult = 0.9f;

        private static readonly Color DeathCore = new Color(225, 195, 255);
        private static readonly Color DeathTail = new Color(150, 80, 220);
        private static readonly Color DeathEdge = new Color(55, 15, 105);

        public override void SetDefaults()
        {
            // Клонируем ванильное копьё: friendly, Melee, ownerHitCheck, penetrate
            Projectile.CloneDefaults(ProjectileID.Spear);
            Projectile.timeLeft = 60;
            Projectile.width = 20;
            Projectile.height = 20;
            Projectile.usesLocalNPCImmunity = true;
            // Один выпад — одно попадание по цели (окно удара бывает длиннее 10 тиков)
            Projectile.localNPCHitCooldown = -1;
            Projectile.tileCollide = false;
        }

        private int Duration => (int)MathHelper.Max(1f, Projectile.ai[0]);

        // 0 → 1 за время жизни выпада
        private float Life => 1f - Projectile.timeLeft / (float)Duration;

        // === Путь по эллипсу ===

        // Доля жизни, на которой t достигает 1: последний тик жизни — это возврат к стартовой позе,
        // чтобы выпад не обрывался скачком (следующий тычок стартует с t = 0 из той же позы)
        private float LifeSpan => Math.Max(1, Duration - 1) / (float)Duration;

        // Время на эллипсе t (0..1) по доле жизни: линейно или рывок-пауза-возврат (пик всегда при t = 0.5)
        private float PathT(float life)
        {
            life = MathHelper.Clamp(life / LifeSpan, 0f, 1f);
            if (!UseEase)
                return life;
            if (life < ThrustPortion)
            {
                float t = life / ThrustPortion;
                return 0.5f * (1f - (float)Math.Pow(1f - t, ThrustPower));
            }
            life -= ThrustPortion;
            if (life < HoldPortion)
                return 0.5f;
            float r = (life - HoldPortion) / Math.Max(0.01f, 1f - ThrustPortion - HoldPortion);
            return 0.5f + 0.5f * Smooth01(r);
        }

        // Доля жизни, на которой серп максимально вынесен
        private float PeakLife => (UseEase ? ThrustPortion + HoldPortion * 0.5f : 0.5f) * LifeSpan;

        // Вынос 0..1: 0 у тела, 1 в точке выпада (проекция на ось курсора, 0.5·(1 − cos 2πt))
        private float ExtendOf(float life) =>
            0.5f * (1f - (float)Math.Cos(MathHelper.TwoPi * PathT(life)));

        // Центр серпа и ось (приклад → остриё) на доле жизни life. Начало эллипса — плечо (pathOrigin)
        private void PoseAt(Vector2 aim, SickleSprite.Geometry geo, float life, out Vector2 center, out Vector2 axis)
        {
            float theta = MathHelper.Pi + MathHelper.TwoPi * PathT(life);
            float length = geo.Length;
            // Предел выноса хвата — по длине древка: приклад (хват минус GripFromButt·длины) не дальше кисти
            float shaftCap = gripReach + GripInset - 2f + GripFromButt * length;
            float k = Math.Min(gripNear + EllipseOffset, shaftCap);
            float a = Math.Max(0f, Math.Min(EllipseReachMult * length, (shaftCap - k) * 0.5f));
            float b = flat * a;
            float side = sideSign * Projectile.spriteDirection;
            Vector2 local = new Vector2(a * (float)Math.Cos(theta) + a + k,
                                        side * b * (float)Math.Sin(theta));
            float aimAngle = aim.ToRotation();
            Vector2 rel = local.RotatedBy(aimAngle);
            float relLen = rel.Length();
            if (relLen > shaftCap)
                rel *= shaftCap / relLen;
            Vector2 grip = pathOrigin + rel;

            // Ось: близка к курсору, отклонение ограничено MaxAxisDeviation
            float lineAngle = rel.ToRotation();
            float dev = MathHelper.WrapAngle(lineAngle - aimAngle) * EllipseAxisBlend;
            dev = MathHelper.Clamp(dev, -MaxAxisDeviation, MaxAxisDeviation);
            axis = (aimAngle + dev).ToRotationVector2();
            // Центр оси приклад→острие: хват стоит на GripFromButt длины от приклада
            center = grip + axis * ((0.5f - GripFromButt) * length);

            // Страховка: кисть должна доставать до древка. Ближайшая к плечу точка допустимого хвата
            // не дальше длины руки, иначе сдвигаем копьё к плечу (в штатной геометрии не срабатывает)
            Vector2 w = center - pathOrigin;
            float tau = MathHelper.Clamp(-Vector2.Dot(w, axis), -length * 0.5f - GripInset, -length * 0.5f + length * GripMaxFrac);
            Vector2 nearest = w + axis * tau;
            float dist = nearest.Length();
            float limit = Math.Max(1f, gripReach - 1f);
            if (dist > limit)
                center -= nearest * ((dist - limit) / dist);
        }

        // Расстояние от центра оси до самой дальней точки лезвия вперёд по оси (px).
        // По зоне лезвия, т.е. по тому же рисунку, что рисуют и по которому бьют; запас — центроид + выступ
        private float tipReach;
        private float ComputeTipReach(SickleSprite.Geometry geo)
        {
            float best = 0f;
            SickleSprite.BladeZone zone = HitZone;
            if (zone != null)
            {
                for (int i = 0; i < zone.Sectors.Length; i++)
                {
                    SickleSprite.BladeSector sec = zone.Sectors[i];
                    float lo = Math.Min(sec.A0, sec.A1), hi = Math.Max(sec.A0, sec.A1);
                    best = Math.Max(best, sec.R1 * (float)Math.Cos(MathHelper.Clamp(0f, lo, hi)));
                }
            }
            return best > 1f ? best : geo.Length * 0.5f + MathHelper.Max(0f, geo.FrontOverhang);
        }

        // Остриё в мире на доле жизни life
        private Vector2 TipAtLife(Vector2 aim, SickleSprite.Geometry geo, float life)
        {
            PoseAt(aim, geo, life, out Vector2 c, out Vector2 ax);
            return c + ax * tipReach;
        }

        // Плечо игрока в мире (начало эллипса) и поза этого тика (ставит PreAI)
        private Vector2 pathOrigin;
        private Vector2 poseCenter;
        private float axisAngle;
        // Поза прошлого тика: серп заметает путь между ними, чтобы не проскакивать цели
        private Vector2 prevCenter;
        private float prevAngle;
        private bool hasPose;
        // Длина руки на полной ступени (px) и форма дуги этого тычка (бросается один раз на первом тике)
        private float gripReach;
        private float gripNear;
        private float flat;
        private float sideSign = 1f;
        private bool rolled;

        // Целочисленный хэш → 0..1
        private static float Hash01(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return (x & 0xFFFFFFu) / 16777216f;
        }

        // Форма дуги: от id снаряда и владельца, чтобы у всех клиентов она была одинаковой
        private void RollShape()
        {
            rolled = true;
            uint seed = unchecked((uint)(Projectile.identity * 73856093) ^ (uint)(Projectile.owner * 19349663) ^ 0x9E3779B9u);
            // Среднее двух бросков — треугольное распределение: средние значения чаще, крайности реже
            float u = (Hash01(seed) + Hash01(unchecked(seed + 0x68E31DA4u))) * 0.5f;
            flat = MathHelper.Lerp(EllipseFlatMin, EllipseFlatMax, u);
            sideSign = EllipseSide * (RandomSide && Hash01(unchecked(seed + 0xB5297A4Du)) < 0.5f ? -1f : 1f);
        }

        // === Рука ===
        // Рука рисуется ступенями длины (None…Full), а серп едет плавно по эллипсу независимо от них:
        // кисть просто подбирает на древке точку, до которой достаёт рука данной ступени.
        private static readonly Player.CompositeArmStretchAmount[] ArmLevels =
        {
            Player.CompositeArmStretchAmount.None,
            Player.CompositeArmStretchAmount.Quarter,
            Player.CompositeArmStretchAmount.ThreeQuarters,
            Player.CompositeArmStretchAmount.Full,
        };
        // Плечо, длина и базовый угол руки для каждой ступени. Заполняется в начале каждого PreAI
        // и читается только внутри него (поэтому общие статические массивы безопасны)
        private static readonly Vector2[] armShoulder = new Vector2[4];
        private static readonly float[] armReach = new float[4];
        private static readonly float[] armBase = new float[4];
        // Ступень руки прошлого тика (-1 — ещё не выбрана): остаёмся на ней, пока рука достаёт
        private int armLevel = -1;

        // Рука — вращение вокруг плеча: две точки кисти при повороте 0 и π дают плечо и длину руки
        private static void MeasureArm(Player player)
        {
            for (int l = 0; l < ArmLevels.Length; l++)
            {
                Vector2 h0 = SickleSprite.HandGrip(player, ArmLevels[l], 0f);
                Vector2 h1 = SickleSprite.HandGrip(player, ArmLevels[l], MathHelper.Pi);
                Vector2 shoulder = (h0 + h1) * 0.5f;
                Vector2 v = h0 - shoulder;
                armShoulder[l] = shoulder;
                armReach[l] = v.Length();
                armBase[l] = v.ToRotation();
            }
        }

        // Хват на древке для ступени l: передняя точка, где древко на расстоянии длины руки от плеча
        // (иначе ближайшая к плечу), в пределах от приклада до GripMaxFrac длины.
        // err — на сколько кисть на этой ступени расходится с древком (px)
        private void ArmFit(int l, Vector2 center, Vector2 axis, float length, out float rot, out float err)
        {
            Vector2 w = center - armShoulder[l];
            float reach = armReach[l];
            float b = Vector2.Dot(w, axis);
            float disc = b * b - w.LengthSquared() + reach * reach;
            float tau = disc >= 0f ? -b + (float)Math.Sqrt(disc) : -b;
            tau = MathHelper.Clamp(tau, -length * 0.5f - GripInset, -length * 0.5f + length * GripMaxFrac);
            Vector2 arm = center + axis * tau - armShoulder[l];
            rot = arm.ToRotation() - armBase[l];
            // Расхождение: кисть мимо длины руки + на сколько она уехала по древку от точки хвата
            err = Math.Abs(arm.Length() - reach) + Math.Abs(tau - (GripFromButt - 0.5f) * length);
        }

        // Ступень и поворот руки. Держимся за прошлую ступень, пока она достаёт (рука не дрожит
        // на границах), иначе берём самую короткую, что достаёт; если не достаёт ни одна — с меньшим расхождением
        private int SolveArm(Vector2 center, Vector2 axis, float length, out float armRot)
        {
            int best = ArmLevels.Length - 1, firstFit = -1;
            float bestErr = float.MaxValue, bestRot = 0f, fitRot = 0f, stayRot = 0f;
            bool stay = false;
            for (int l = 0; l < ArmLevels.Length; l++)
            {
                ArmFit(l, center, axis, length, out float rot, out float err);
                if (err < bestErr)
                {
                    bestErr = err;
                    best = l;
                    bestRot = rot;
                }
                if (err <= ArmFitTol)
                {
                    if (firstFit < 0)
                    {
                        firstFit = l;
                        fitRot = rot;
                    }
                    if (l == armLevel)
                    {
                        stay = true;
                        stayRot = rot;
                    }
                }
            }
            if (stay)
            {
                armRot = stayRot;
                return armLevel;
            }
            if (firstFit >= 0)
            {
                armRot = fitRot;
                return firstFit;
            }
            armRot = bestRot;
            return best;
        }

        public override bool PreAI()
        {
            Player player = Main.player[Projectile.owner];

            int duration = Duration;

            // heldProj КАЖДЫЙ тик БЕЗ УСЛОВИЙ: клон ванильного копья даёт hide=true,
            // снаряд рисуется только через слой игрока (DrawProj(heldProj))
            player.heldProj = Projectile.whoAmI;

            if (Projectile.timeLeft > duration)
                Projectile.timeLeft = duration;

            // Звук выпада — на первом тике
            if (Projectile.localAI[0] == 0f)
            {
                Projectile.localAI[0] = 1f;
                SoundEngine.PlaySound(SoundID.Item1 with { Pitch = 0.5f, Volume = 0.6f }, player.Center);
            }

            // velocity хранит направление выпада
            Projectile.velocity = Projectile.velocity.SafeNormalize(Vector2.UnitX);
            Projectile.spriteDirection = Projectile.velocity.X >= 0 ? 1 : -1;
            Vector2 dir = Projectile.velocity;

            float life = Life;
            float extend = ExtendOf(life);

            // Геометрия реального спрайта
            SickleSprite.Geometry geo = SickleSprite.Get((int)Projectile.ai[2]);

            // Плечо — начало эллипса (самая длинная ступень руки, она не зависит от поворота)
            MeasureArm(player);
            pathOrigin = armShoulder[ArmLevels.Length - 1];
            gripReach = armReach[ArmLevels.Length - 1];
            gripNear = armReach[0];
            if (!rolled)
                RollShape();

            // Поза серпа на эллипсе
            PoseAt(dir, geo, life, out Vector2 center, out Vector2 axis);
            if (hasPose)
            {
                prevCenter = poseCenter;
                prevAngle = axisAngle;
            }
            poseCenter = center;
            axisAngle = axis.ToRotation();
            if (!hasPose)
            {
                prevCenter = poseCenter;
                prevAngle = axisAngle;
                hasPose = true;
            }
            Projectile.Center = poseCenter;

            // Рука тянется к древку: поворот плавный, ступень длины меняется редко
            armLevel = SolveArm(poseCenter, axis, geo.Length, out float armRot);
            player.SetCompositeArmFront(true, ArmLevels[armLevel], armRot);
            HideBackArm(player, dir);

            // Поворот: вектор «приклад → острие» вдоль оси. SET, не +=.
            bool flipped = Projectile.spriteDirection == -1;
            Projectile.rotation = axisAngle - geo.AngleFor(flipped);

            if (SickleArcProjectile.DebugHitbox && Projectile.owner == Main.myPlayer && PathT(life) <= HitWindow)
                SickleSprite.EmitDebugDust(HitZone, poseCenter,
                    axisAngle, flipped, 1f);

            tipReach = ComputeTipReach(geo);
            Vector2 tip = poseCenter + axis * tipReach;
            StabGlowEffects(tip, axis, extend, life);
            RecordRibbon(player, poseCenter - axis * (geo.Length * 0.5f), Projectile.rotation);

            return false;
        }

        // Насколько рука в выпаде поднята над линией удара (рад). Нужна SicklePlayer для позы между тычками
        public const float ArmLift = 0.3f;

        // Вторая рука — как у ванильных копий: композитная задняя рука ВЫКЛЮЧЕНА,
        // кадр тела по наклону оружия
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

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            // Бьёт только пока серп выброшен
            if (PathT(Life) > HitWindow)
                return false;

            Vector2 dir = axisAngle.ToRotationVector2();

            // Бьёт видимое лезвие: зона по пикселям серпа в той же позе, что рисует PreDraw
            // (poseCenter — середина оси приклад→острие, зеркало как при отрисовке).
            // Серп за тик проходит заметный путь по эллипсу — проверяем промежуточные позы
            SickleSprite.BladeZone zone = HitZone;
            if (zone != null)
            {
                bool flipped = Projectile.spriteDirection == -1;
                int steps = (int)MathHelper.Clamp((float)Math.Ceiling(Vector2.Distance(prevCenter, poseCenter) / SweepStep), 1f, 12f);
                float turn = MathHelper.WrapAngle(axisAngle - prevAngle);
                for (int i = 1; i <= steps; i++)
                {
                    float f0 = (i - 1) / (float)steps;
                    float f1 = i / (float)steps;
                    if (SickleSprite.BladeOverlaps(zone, targetHitbox, Vector2.Lerp(prevCenter, poseCenter, f1),
                        prevAngle + turn * f0, prevAngle + turn * f1, flipped, 1f))
                        return true;
                }
                return false;
            }

            // Запас без рисунка: отрезок, кончающийся на остриё по центроиду
            float length = SickleSprite.Get((int)Projectile.ai[2]).Length;
            Vector2 tipPos = poseCenter + dir * (length * 0.5f);
            Vector2 bladeStart = tipPos - dir * System.Math.Min(BladeHitLength, length);
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                bladeStart, tipPos, BladeHitWidth, ref collisionPoint);
        }

        // Запасной хитбокс (если нет рисунка): бьющая часть у острия, длина вдоль серпа и толщина (px)
        private const float BladeHitLength = 14f;
        private const float BladeHitWidth = 8f;

        // === Эффект удара ===
        // Вспышки (рисуются в PreDraw) этого выпада: позиция и тик появления. Массивы заводятся при первом ударе
        private const int HitFlashSlots = 4;
        private Vector2[] hitFlashPos;
        private uint[] hitFlashStart;
        private int hitFlashCount;

        // Ближайшая к цели точка зоны лезвия (в текущей позе), без зоны — остриё
        private Vector2 NearestBladePoint(Vector2 target)
        {
            Vector2 best = poseCenter + axisAngle.ToRotationVector2() * tipReach;
            SickleSprite.BladeZone zone = HitZone;
            if (zone == null)
                return best;
            bool mirrored = Projectile.spriteDirection == -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < zone.Sectors.Length; i++)
            {
                SickleSprite.BladeSector sec = zone.Sectors[i];
                float a0 = mirrored ? -sec.A1 : sec.A0;
                float a1 = mirrored ? -sec.A0 : sec.A1;
                for (int ri = 0; ri < 3; ri++)
                {
                    float r = MathHelper.Lerp(sec.R0, sec.R1, ri * 0.5f);
                    for (int ai = 0; ai < 3; ai++)
                    {
                        float ang = axisAngle + MathHelper.Lerp(a0, a1, ai * 0.5f);
                        Vector2 p = poseCenter + ang.ToRotationVector2() * r;
                        float dsq = Vector2.DistanceSquared(p, target);
                        if (dsq < bestSq)
                        {
                            bestSq = dsq;
                            best = p;
                        }
                    }
                }
            }
            return best;
        }

        // Удар по цели: вспышка, взрыв искр и снежинки / тёмный дым в точке на лезвии.
        // OnHit вызывается у владельца, поэтому эффект видит владелец (частицы не дублируются)
        private void SpawnHitEffect(Vector2 targetCenter)
        {
            if (Main.dedServ || !StabGlow)
                return;
            Vector2 point = NearestBladePoint(targetCenter);
            Vector2 dir = axisAngle.ToRotationVector2();

            hitFlashPos ??= new Vector2[HitFlashSlots];
            hitFlashStart ??= new uint[HitFlashSlots];
            int slot = hitFlashCount % HitFlashSlots;
            hitFlashPos[slot] = point;
            hitFlashStart[slot] = Main.GameUpdateCount;
            hitFlashCount++;

            for (int i = 0; i < HitBurstSparks; i++)
            {
                int type = Main.rand.NextBool() ? DustID.Shadowflame : DustID.PurpleTorch;
                Vector2 vel = Main.rand.NextVector2Unit() * HitBurstSpeed * Main.rand.NextFloat(0.3f, 1f) + dir * 0.8f;
                Dust d = Dust.NewDustPerfect(point, type, vel, 100, default, Main.rand.NextFloat(0.7f, 1.1f));
                d.noGravity = true;
            }
            for (int i = 0; i < HitFlakes; i++)
            {
                Vector2 vel = Main.rand.NextVector2Unit() * Main.rand.NextFloat(0.6f, 2.2f);
                Dust f = Dust.NewDustPerfect(point, DustID.Smoke, vel * 0.6f, 180, new Color(60, 15, 100), Main.rand.NextFloat(0.9f, 1.3f));
                f.noGravity = true;
                f.fadeIn = 0.4f;
            }
            Vector3 light = new Vector3(0.85f, 0.3f, 1.35f);
            Lighting.AddLight(point, light * HitLightBoost * LightMult);
        }

        // Зона урона = то, что нарисовано: лезвие по пикселям спрайта
        private SickleSprite.BladeZone HitZone => SickleSprite.GetZone((int)Projectile.ai[2]);

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            SpawnHitEffect(target.Center);
        }

        public override void OnHitPlayer(Player target, Player.HurtInfo info)
        {
            SpawnHitEffect(target.Center);
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            // Серп режет, а не расталкивает: отброс ослаблен (SickleTypes.StabKnockbackMult),
            // а при нулевом отбросе отключаем его совсем
            if (Projectile.knockBack <= 0f)
                modifiers.DisableKnockback();
        }

        // === Свечение ===
        private void GlowColors(out Color core, out Color tail, out Color edge)
        {
            core = DeathCore;
            tail = DeathTail;
            edge = DeathEdge;
        }

        private static float Smooth01(float x)
        {
            x = MathHelper.Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        // Яркость: GlowRiseTicks нарастает, GlowFadeTicks гаснет к концу, между — держится
        private float GlowEnvelope(float life)
        {
            float age = life * Duration;
            float rise = Smooth01(age / Math.Max(0.01f, GlowRiseTicks));
            float fall = Smooth01((Duration - age) / Math.Max(0.01f, GlowFadeTicks));
            return rise * fall;
        }

        // Колокол вокруг момента максимального выпада (конец рывка), 0..1
        private float PeakPulse(float life)
        {
            float d = (life - PeakLife) * Duration / Math.Max(0.01f, PeakWidthTicks);
            return (float)Math.Exp(-d * d);
        }

        private static Color Additive(Color c, float alpha)
        {
            Color r = c * MathHelper.Clamp(alpha, 0f, 1f);
            r.A = 0;
            return r;
        }

        // tip — реальное остриё на эллипсе, dir — ось серпа (приклад → остриё)
        private void StabGlowEffects(Vector2 tip, Vector2 dir, float extend, float life)
        {
            if (!StabGlow || Main.dedServ)
                return;
            float env = GlowEnvelope(life);
            if (env <= 0.02f)
                return;

            float shaftLen = SickleSprite.Get((int)Projectile.ai[2]).Length;
            float peak = PeakPulse(life);
            Vector2 head = tip + dir * HeadAhead * extend;

            Vector3 light = new Vector3(0.85f, 0.3f, 1.35f);
            Lighting.AddLight(head, light * (env + peak * PeakLightBoost) * LightMult);
            Lighting.AddLight(tip - dir * CometTailLen * 0.5f, light * env * 0.6f * LightMult);
            // Кометы, искр и вспышки у острия больше нет: остались свет и лента-шлейф (StabRibbonDraw)
            return;

            // Искры на острие и чуть впереди
            for (int i = 0; i < SparksPerTick; i++)
            {
                if (Main.rand.NextFloat() > env + 0.15f)
                    continue;
                int type = Main.rand.NextBool(3) ? DustID.Shadowflame : DustID.PurpleTorch;
                Vector2 pos = Vector2.Lerp(tip, head, Main.rand.NextFloat()) + Main.rand.NextVector2Circular(4f, 4f);
                Dust d = Dust.NewDustPerfect(pos, type,
                    Main.rand.NextVector2Circular(SparkDrift, SparkDrift) + dir * SparkForward * extend, 100,
                    default, Main.rand.NextFloat(0.6f, 0.95f));
                d.noGravity = true;
                d.fadeIn = 0.4f;
            }

            if (Main.rand.NextFloat() < ShaftSparkChance * env)
            {
                Vector2 hp = Vector2.Lerp(tip, tip - dir * (shaftLen * 0.9f), Main.rand.NextFloat());
                Dust s = Dust.NewDustPerfect(hp + Main.rand.NextVector2Circular(3f, 3f),
                    DustID.PurpleTorch,
                    Main.rand.NextVector2Circular(0.6f, 0.6f), 110, default, Main.rand.NextFloat(0.8f, 1.3f));
                s.noGravity = true;
            }

            if (Main.rand.NextFloat() < DarkSparkChance * env)
            {
                Dust w = Dust.NewDustPerfect(head + Main.rand.NextVector2Circular(5f, 5f), DustID.Smoke,
                    Main.rand.NextVector2Circular(0.5f, 0.5f) + dir * 0.6f, 180, new Color(60, 15, 100),
                    Main.rand.NextFloat(1.2f, 1.8f));
                w.noGravity = true;
                w.fadeIn = 0.5f;
            }

            // Взрыв искр один раз — в момент максимального выпада
            if (Projectile.localAI[1] == 0f && life >= PeakLife - 0.5f / Duration)
            {
                Projectile.localAI[1] = 1f;
                for (int i = 0; i < PeakBurstSparks; i++)
                {
                    int type = Main.rand.NextBool() ? DustID.Shadowflame : DustID.PurpleTorch;
                    Dust d = Dust.NewDustPerfect(tip, type,
                        Main.rand.NextVector2Circular(PeakBurstSpeed, PeakBurstSpeed) + dir * PeakBurstSpeed * 0.6f,
                        100, default, Main.rand.NextFloat(0.7f, 1.1f));
                    d.noGravity = true;
                }
            }
        }

        // Звезда из n лучей: тонкие растянутые пятна ThePerfectGlow, повёрнутые на 180/n градусов
        private static void DrawSparkle(Texture2D glow, Vector2 pos, float size, float rotation, int rays, Color color, Color centerColor)
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

        // Мазок вдоль отрезка a→b: растянутое мягкое пятно ThePerfectGlow
        private static void DrawSegment(Texture2D glow, Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 delta = b - a;
            float len = delta.Length();
            if (width < 0.5f)
                return;
            Vector2 scale = new Vector2((len + width) / glow.Width, width / glow.Height);
            Main.EntitySpriteDraw(glow, (a + b) * 0.5f - Main.screenPosition, null, color, delta.ToRotation(),
                glow.Size() / 2f, scale, SpriteEffects.None, 0f);
        }

        // === Послеобразы тычка ===
        // Лента тут не работает: острие едет вдоль своей оси, ленте не во что развернуться. Вместо неё за копьём рисуются
        // несколько тонированных полупрозрачных копий спрайта по позам прошлых тиков (кольцевой буфер). Лента (StabRibbon) отключена.
        private const int GhostCount = 4;              // копий за копьём
        private const int GhostStepTicks = 1;          // шаг между копиями по времени (тиков)
        private const float GhostAlpha = 0.45f;        // прозрачность ближайшей копии, дальше убывает до 0
        private static readonly Color GhostDeath = new Color(195, 115, 255);
        // [0] — текущий тик. Позиция якоря отрисовки хранится относительно игрока
        private Vector2[] ghostPos;
        private float[] ghostRot;
        private int ghostCount;

        private void RecordRibbon(Player player, Vector2 drawPos, float rotation)
        {
            if (Main.dedServ)
                return;
            int size = GhostCount * GhostStepTicks + 1;
            ghostPos ??= new Vector2[size];
            ghostRot ??= new float[size];
            int keep = Math.Min(ghostCount, size - 1);
            for (int i = keep; i > 0; i--)
            {
                ghostPos[i] = ghostPos[i - 1];
                ghostRot[i] = ghostRot[i - 1];
            }
            ghostPos[0] = drawPos - player.MountedCenter;
            ghostRot[0] = rotation;
            ghostCount = keep + 1;
        }

        // Копии рисуются от самой старой к новой, под основным копьём
        private void DrawAfterimages(Texture2D texture, SickleSprite.Geometry geo, bool flipped, SpriteEffects effects, float env)
        {
            if (!StabGlow || Main.dedServ || ghostPos == null)
                return;
            Vector2 anchor = Main.player[Projectile.owner].MountedCenter;
            Color tint = GhostDeath;
            Vector2 origin = geo.ButtFor(flipped);
            for (int k = GhostCount; k >= 1; k--)
            {
                int idx = k * GhostStepTicks;
                if (idx >= ghostCount)
                    continue;
                float a = GhostAlpha * env * (1f - (k - 1f) / GhostCount);
                if (a <= 0.01f)
                    continue;
                Vector2 pos = anchor + ghostPos[idx];
                Main.EntitySpriteDraw(texture, pos - Main.screenPosition, null, tint * a,
                    ghostRot[idx], origin, 1f, effects, 0f);
            }
        }

        // Вспышки ударов: звезда и мягкое пятно в точках попадания, быстро гаснут
        private void DrawHitFlashes()
        {
            if (!StabGlow || Main.dedServ || hitFlashPos == null)
                return;
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow == null || glow.IsDisposed)
                return;
            GlowColors(out Color core, out Color tail, out Color edge);
            Vector2 origin = glow.Size() / 2f;
            int count = Math.Min(hitFlashCount, HitFlashSlots);
            for (int i = 0; i < count; i++)
            {
                int age = (int)(Main.GameUpdateCount - hitFlashStart[i]);
                if (age < 0 || age >= HitFlashTicks)
                    continue;
                float k = 1f - age / (float)HitFlashTicks;
                float grow = 0.6f + 0.6f * (1f - k);
                Vector2 pos = hitFlashPos[i];
                Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, Additive(edge, k * HitFlashAlpha), 0f, origin,
                    HitFlashSize * grow / glow.Width, SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(glow, pos - Main.screenPosition, null, Additive(core, k * HitFlashAlpha), 0f, origin,
                    HitFlashSize * 0.4f * grow / glow.Width, SpriteEffects.None, 0f);
                DrawSparkle(glow, pos, HitStarSize * (0.5f + k * 0.5f), i * 0.7f + MathHelper.PiOver4, 2,
                    Additive(Color.Lerp(tail, core, 0.5f), k), Additive(core, k));
            }
        }

        public override void Unload()
        {
            StabRibbon.Unload();
        }

        public override bool PreDraw(ref Color lightColor)
        {
            int itemType = (int)Projectile.ai[2];
            if (itemType <= 0)
                return true;
            // Пока PreAI не посчитал первую позу, рисовать нечего (иначе серп мелькнул бы в старом месте)
            if (!hasPose)
                return false;
            Texture2D texture = SickleTextures.Normal(itemType);
            if (texture == null || texture.IsDisposed)
                return true;

            // Якорь отрисовки — реальный ПРИКЛАД спрайта на оси, считанной на эллипсе
            SickleSprite.Geometry geo = SickleSprite.Get(itemType);
            bool flipped = Projectile.spriteDirection == -1;
            Vector2 dir = axisAngle.ToRotationVector2();
            Vector2 drawPos = poseCenter - dir * (geo.Length * 0.5f);
            Vector2 origin = geo.ButtFor(flipped);
            SpriteEffects effects = flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            // Послеобразы — ПОД копьём
            DrawAfterimages(texture, geo, flipped, effects, GlowEnvelope(Life));

            Main.EntitySpriteDraw(texture, drawPos - Main.screenPosition, null,
                lightColor, Projectile.rotation, origin, 1f, effects, 0f);

            DrawHitFlashes();
            return false;
        }
    }
}
