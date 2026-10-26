using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using FrostMelee.Content;
using FrostMelee.Content.Projectiles;

namespace FrostMelee.Content.Weapons
{
    // Снаряд дуги для ледяных мечей (текстура — ванильная дуга меча Excalibur).
    // Копия WoodSwordArcProjectile (WoodSwordsOverhaul) + ледяной след у Ice Blade:
    // на его swept-веер ложится слабый невидимый иней FrostTrailSystem (только Slow).
    // У Фростбранда инея нет вовсе — это фишка копья, а меч огненный.
    // Дуга — единственный удар взмаха (у самого меча хитбокс убран, см. SwordGlobalItem):
    // бьёт от рукояти до края нарисованной дуги, по каждой цели раз за взмах.
    // В зоне клинка — полный урон и отбрасывание, дальше к краю они падают до крошечных.
    public class SwordArcProjectile : ModProjectile
    {
        public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.Excalibur;

        // === Геометрия нарисованной дуги (по ней же считается хитбокс) ===
        // Снята с текстуры Excalibur: кадр 170×170, центр дуги — центр кадра.
        // В игре видна в основном тонкая линия (кадр 3): в текстуре она лежит на радиусе
        // 78..82 px и по углу от -74° (хвост) до +56° (голова, сторона взмаха) от оси снаряда.
        // Тело дуги (кадр 0) почти прозрачное (цвет ×0.15 / ×0.25) и в хитбокс не входит.
        // PreDraw рисует только линию: масштаб BodyDrawScale, сверху ×LineDrawScale. Тело не рисуется.
        private const float BodyDrawScale = 0.75f;
        private const float LineDrawScale = 0.8f;
        private const float LineTexOuter = 82f;
        private static readonly float ArcAngleBack = MathHelper.ToRadians(74f);
        private static readonly float ArcAngleFront = MathHelper.ToRadians(56f);
        // Толщина клинка для зоны полного урона: клин между углами клинка соседних
        // тиков расширяем на этот угол в обе стороны (≈3 px на острие)
        private static readonly float BladeWedgePad = MathHelper.ToRadians(3f);
        // Дуга растёт за взмах: масштаб от ArcScaleStart до ArcScaleStart + ArcScaleGrowth (× ai[2])
        private const float ArcScaleStart = 1f;
        private const float ArcScaleGrowth = 0.6f;

        // Урон и отбрасывание на краю полностью раскрытой дуги (доля от полных).
        // Отброс на краю заметный (дуга = контроль, не урон).
        private const float EdgeDamageMult = 0.15f;
        private const float EdgeKnockbackMult = 0.2f;
        // Урон падает от полного до EdgeDamageMult за первую долю промежутка «клинок → внешний край»
        // (smoothstep), дальше держится на EdgeDamageMult. Отброс по-прежнему спадает линейно.
        private const float DamageFalloffSpan = 0.28f;
        // Нижняя граница урона дуги до защиты врага, в единицах урона: дробных/нулевых значений нет
        private const float MinEdgeDamage = 1f;

        // Насколько кисть в ванильном взмахе отстоит от центра игрока (до ~15px):
        // остриё дотягивается на длину клинка плюс это смещение
        public const float HandReach = 15f;

        // Докуда от центра игрока достаёт сам клинок: длина клинка (рукоять → остриё
        // по реальным пикселям) × размер оружия + HandReach. Ближе — полный урон, и хитбокс
        // никогда не короче клинка. Задаёт SwordGlobalItem.Shoot у владельца:
        // попадания считает только он.
        public float BladeReach;

        // Каким мечом спавнена дуга: ItemID предмета. Задаёт SwordGlobalItem.Shoot
        // вместе с BladeReach.
        public int SwordItem;

        // true — показать пылью зону клинка (полный урон) и внешний край хитбокса
        public static bool DebugHitbox = false;

        public override void SetStaticDefaults() {
            Main.projFrames[Type] = 4;
        }

        public override void SetDefaults() {
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
            // Colliding вызывается только для целей в этом радиусе от игрока.
            // 300 — как у ванильных дуг (Excalibur)
            Projectile.ownerHitCheckDistance = 300f;
            // Меч сам не бьёт, так что делить с ним кулдаун не нужно:
            // дуга бьёт каждую цель раз за взмах (localNPCHitCooldown = -1)
            Projectile.usesOwnerMeleeHitCD = false;
            Projectile.stopsDealingDamageAfterPenetrateHits = true;
            Projectile.aiStyle = -1;
            Projectile.noEnchantmentVisuals = true;
        }

        public override void AI() {
            Projectile.localAI[0]++;
            Player player = Main.player[Projectile.owner];
            // Клинок взмаха рисуется этим снарядом (SwingBladeLayer): одинаковый
            // угол у ленты и меча гарантирован источником, а не синхронизацией
            player.GetModPlayer<SwordPlayer>().swingArc = Projectile;
            float maxAnim = Math.Max(1f, Projectile.ai[1]);
            GetArcState(player, Projectile.localAI[0], out float rotation, out float scale);
            Projectile.rotation = rotation;
            Projectile.scale = scale;
            Projectile.Center = player.RotatedRelativePoint(player.MountedCenter) - Projectile.velocity;

            if (Projectile.owner == Main.myPlayer)
                RecordFrostTrail(player);

            // Frostbrand: взмах несёт за собой веер струи холодного пламени
            if (SwordItem == ItemID.Frostbrand && Projectile.owner == Main.myPlayer
                && Projectile.localAI[0] % 2 == 0)
                SpawnArcJets(player);

            // Визуал (все клиенты): Ice Blade точится об воздух; Фростбранд
            // записывает настоящий путь клинка для ленты и сыплет угли пламени
            if (!Main.dedServ) {
                if (IsIceBlade(player))
                    UpdateHone(player, maxAnim);
                else if ((SwordItem != 0 ? SwordItem : player.HeldItem.type) == ItemID.Frostbrand) {
                    RecordFlameBlade(player);
                    SpawnFlameSparks(player, maxAnim);
                }
                // Праздничные мечи: своих частиц взмаха нет — только шаблонная дуга
            }

            if (DebugHitbox && Projectile.owner == Main.myPlayer)
                ShowHitbox(player);

            // Конец взмаха — строго вместе с анимацией руки: если itemAnimation
            // уже 0, игра вернула руку в обычную позу (itemLocation ближе к телу),
            // а дуга ещё жива — на этот последний кадр клинок прыгал рукояткой
            // вглубь игрока, а дуга оставалась на старой записи (владелец нашёл
            // покадровым разбором). Умираем в тот же тик, лишнего кадра нет
            if (Projectile.localAI[0] >= maxAnim
                || (Projectile.owner == Main.myPlayer && player.itemAnimation <= 0)) {
                Projectile.Kill();
            }

            // Визуальные эффекты от фляг
            float enchantmentDistance = 40f * Projectile.ai[2];
            Vector2 enchantmentPosition = Projectile.Center + Projectile.rotation.ToRotationVector2() * enchantmentDistance;
            Rectangle rectangle = Utils.CenteredRectangle(enchantmentPosition, new Vector2(30f * Projectile.ai[2], 30f * Projectile.ai[2]));
            Projectile.EmitEnchantmentVisualsAt(rectangle.TopLeft(), rectangle.Width, rectangle.Height);
        }

        // Поворот и масштаб дуги на момент time (тиков от спавна) —
        // одна формула для отрисовки (AI), пыли и для хитбокса (Colliding).
        // Пока идёт взмах, дуга идёт ЗА КЛИНКОМ: угол берём из боевой позы
        // ваниллы (itemLocation/itemRotation), так дуга не отстаёт от меча.
        // Без активного взмаха (хвост дуги) — старая линейная формула.
        // Мастер-кривая взмаха: ОДНА формула угла для клинка (itemRotation
        // в SwordPlayer), головы дуги и каждой точки ленты через её time.
        // История ленты поэтому настоящая: прошлые time дают прошлые углы,
        // шлейф тянется за клинком, а не схлопывается в текущий угол.
        // Кривая СКОРОСТИ взмаха мечей — нелинейная (решение владельца):
        // БЫСТРОЕ НАЧАЛО и замедляющийся конец. Клинок срывается с места
        // рывком, большую часть дуги проходит в первой половине времени и
        // к концу плавно затухает (ease-out: 1-(1-t)^степень; степень больше —
        // резче старт и длиннее затухание).
        // Форма копья наоборот (замах назад → разгон через середину) на мече
        // ощущалась ужасно — тягучее начало; владелец велел перевернуть.
        // Сначала кривая была только у Фростбранда, потом владелец велел дать
        // ту же Ice Blade — его дуга с ней выглядит так же живо.
        // Функцию обязаны звать ВСЕ места с формулой угла взмаха: ArcRotation
        // здесь и itemRotation в SwordPlayer.PostUpdate — иначе клинок, дуга
        // и хитбокс разойдутся по разным кривым.
        public const float SwingEaseOutPower = 1.8f;  // резкость старта взмаха (1 — линейно; владелец выбрал середину между линейным и резким 2.6)

        // itemType больше не нужен (кривая одна на оба меча), параметр оставлен,
        // чтобы не трогать места вызова.
        public static float SwingProgressCurve(float progress, int itemType) {
            progress = MathHelper.Clamp(progress, 0f, 1f);
            return 1f - (float)Math.Pow(1f - progress, SwingEaseOutPower);
        }

        // Обратная к SwingProgressCurve: изогнутый прогресс → обычная доля времени.
        // Нужна истории ленты: прошлые углы берём равномерно по углу взмаха,
        // а ArcRotation понимает только время.
        public static float SwingProgressInverse(float curved) {
            curved = MathHelper.Clamp(curved, 0f, 1f);
            return 1f - (float)Math.Pow(1f - curved, 1f / SwingEaseOutPower);
        }

        private float ArcRotation(Player player, float time) {
            int item = SwordItem != 0 ? SwordItem : player.HeldItem.type;
            if (!player.HeldItem.IsAir && player.itemAnimation > 0 && player.itemAnimationMax > 0) {
                float maxAnim = player.itemAnimationMax;
                float progress = SwingProgressCurve(MathHelper.Clamp(time, 0f, maxAnim) / maxAnim, item);
                float theta = player.direction >= 0
                    ? MathHelper.Pi * (1f + progress)
                    : -MathHelper.Pi * progress;
                return theta + player.fullRotation;
            }
            float percentageOfLife = SwingProgressCurve(
                MathHelper.Clamp(time, 0f, Math.Max(1f, Projectile.ai[1])) / Math.Max(1f, Projectile.ai[1]), item);
            return Projectile.velocity.ToRotation() + Projectile.ai[0] * MathHelper.Pi * percentageOfLife + player.fullRotation + MathHelper.Pi;
        }

        private float ArcScaleAt(float time) {
            float percentageOfLife = MathHelper.Clamp(time, 0f, Math.Max(1f, Projectile.ai[1])) / Math.Max(1f, Projectile.ai[1]);
            return (ArcScaleStart + percentageOfLife * ArcScaleGrowth) * Projectile.ai[2];
        }

        private void GetArcState(Player player, float time, out float rotation, out float scale) {
            rotation = ArcRotation(player, time);
            scale = ArcScaleAt(time);
        }

        // === Frostbrand: струя веером по дуге ===
        // У встречи струя — узкий язык вперёд из острия (SickleBraceProjectile).
        // У взмаха своя, адаптированная по дуге: короткие языки срываются
        // с внешней половины клинка и летят ПО ХОДУ взмаха (касательно), чуть
        // наружу. За взмах они заметают тот же сектор, что и пламя на клинке,
        // и поджигают, кого задели (FrostFireJet). Сам клинок не поджигает.
        private static readonly float[] ArcJetFractions = new float[] { 0.6f, 1f }; // точки клинка: доля приклад→остриё
        private const float ArcJetLife = 11f;   // тиков жизни языка веера

        private void SpawnArcJets(Player player) {
            if (!GetBladeLine(player, out Vector2 butt, out Vector2 tip))
                return;
            Vector2 bladeDir = (tip - butt).SafeNormalize(Vector2.UnitX);
            // Касательная по ходу взмаха: ai[0] несёт знак направления взмаха
            // (как флип текстуры дуги). Внешний конец клинка движется быстрее —
            // скорость языка растёт с долей f, как у настоящего острия
            Vector2 tangent = new Vector2(-bladeDir.Y, bladeDir.X) * (Projectile.ai[0] >= 0f ? 1f : -1f);
            foreach (float f in ArcJetFractions) {
                Vector2 at = Vector2.Lerp(butt, tip, f);
                Vector2 vel = tangent * (1.2f + 2.4f * f) + bladeDir * 0.6f;
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), at, vel,
                    ModContent.ProjectileType<FrostFireJet>(), 0, 0.5f, Projectile.owner, ArcJetLife);
            }
        }

        // === Ледяной след Ice Blade: слабый невидимый иней вдоль веера взмаха (только Slow) ===
        // Точки брать по текущему углу дуги, на трёх радиусах через лезвие —
        // за взмах собирается веер снега, как у взмаха серпа, но слабый.
        private FrostTrailSystem.Trail frostTrail;

        private void RecordFrostTrail(Player player) {
            if (Main.dedServ)
                return;
            // Иней взмаха есть только у Ice Blade (невидимый — одно замедление):
            // Фростбранд огненный, инея у него нет вовсе (решение владельца),
            // праздничные мечи — без стихийных механик
            if (SwordItem != ItemID.IceBlade)
                return;
            float outer = Math.Max(LineTexOuter * ArcScaleAt(Projectile.localAI[0]) * BodyDrawScale * LineDrawScale, BladeReach);
            if (outer <= 1f)
                return;
            if (frostTrail == null)
                // Ice Blade точится, а не сыплет иней: замедление остаётся, пятна и искры инея не рисуются
                frostTrail = FrostTrailSystem.Begin(true, false, false);
            // За тик заметается угол между прошлым и текущим поворотом линии:
            // точки раскладываем по трем направлениям на трех радиусах,
            // чтобы веер снега не редел на быстрых взмахах
            float rot0 = ArcRotation(player, Projectile.localAI[0] - 1f);
            float rot1 = ArcRotation(player, Projectile.localAI[0]);
            for (int s = 0; s <= 2; s++) {
                Vector2 dir = MathHelper.Lerp(rot0, rot1, s / 2f).ToRotationVector2();
                foreach (float f in FrostTrailRadii)
                    frostTrail.Add(player.MountedCenter + dir * outer * f + Main.rand.NextVector2Circular(3f, 3f));
            }
        }

        private static readonly float[] FrostTrailRadii = new float[] { 0.5f, 0.8f, 1f };

        // Фростбранд: угли холодного пламени вдоль дуги (частицы в AI, как искры
        // у копья). Густота — от запаса огня: пустой меч почти не сыплет, только
        // редкую морозную пыль голого клинка. На пике взмаха — сноп с острия.
        private void SpawnFlameSparks(Player player, float maxAnim) {
            // Центр — кисть, а не центр тела: клинок вращается вокруг руки,
            // и угли на той же оси не отстают от меча ни на градус
            Vector2 center = player.itemLocation;
            float radius = LineTexOuter * ArcScaleAt(Projectile.localAI[0]) * BodyDrawScale * LineDrawScale;
            if (radius <= 1f)
                return;
            float full = player.GetModPlayer<SwordPlayer>().FlameFullness;
            int dir = Projectile.ai[0] >= 0f ? 1 : -1;
            float headAng = IceArcFx.HeadAngleOf(Projectile.rotation, dir);

            int embers = full > 0.02f ? 1 + (Main.rand.NextFloat() < full * 0.6f ? 1 : 0) : 0;
            for (int i = 0; i < embers; i++) {
                float back = MathHelper.ToRadians(Main.rand.NextFloat(0f, 60f));   // от головы к хвосту
                float ang = headAng - dir * back;
                Vector2 at = center + ang.ToRotationVector2() * radius * Main.rand.NextFloat(0.7f, 1.05f);
                // Угли сносит назад против хода взмаха и чуть вверх, как искры пламени
                Vector2 drift = (ang - dir * MathHelper.PiOver2).ToRotationVector2() * Main.rand.NextFloat(0.4f, 1.2f);
                drift.Y -= 0.25f;
                Dust d = Dust.NewDustPerfect(at, DustID.IceTorch, drift, 100, default, Main.rand.NextFloat(1f, 1.6f));
                d.noGravity = true;
                d.fadeIn = 0.4f;
            }
            if (embers == 0 && Main.rand.NextBool(2)) {
                Vector2 at = center + headAng.ToRotationVector2() * radius * Main.rand.NextFloat(0.85f, 1.05f);
                Dust d = Dust.NewDustPerfect(at, DustID.Frost, Main.rand.NextVector2Circular(0.5f, 0.5f), 140, default, Main.rand.NextFloat(0.8f, 1.1f));
                d.noGravity = true;
            }
            // Ровно в тик пика — сноп углей с острия
            if (full > 0.02f && (int)Projectile.localAI[0] == (int)(maxAnim * PeakAt)
                && GetBladeLine(player, out _, out Vector2 tip)) {
                for (int i = 0; i < 6; i++) {
                    Dust d = Dust.NewDustPerfect(tip, DustID.IceTorch, Main.rand.NextVector2Circular(3.2f, 3.2f), 100,
                        default, Main.rand.NextFloat(1.1f, 1.8f));
                    d.noGravity = true;
                }
            }
        }

        // Настоящая линия лезвия в мире — ровно та, которой SwingBladeLayer
        // рисует клинок: якорь player.itemLocation, origin geo.ButtFor, масштаб 1.
        public static bool GetBladeLine(Player player, out Vector2 butt, out Vector2 tip) {
            butt = tip = default;
            if (player.HeldItem.IsAir || player.itemAnimation <= 0)
                return false;
            SickleSprite.Geometry geo;
            try { geo = SickleSprite.Get(player.HeldItem.type); }
            catch { return false; }
            if (geo.Length <= 1f)
                return false;
            bool flipped = player.direction == -1;
            // itemRotation уже зеркальная, флип учтён в TipFor/ButtFor — +pi здесь лишний.
            // Слой рисует от приклада, поэтому приклад и есть itemLocation.
            butt = player.itemLocation;
            tip = butt + (geo.TipFor(flipped) - geo.ButtFor(flipped)).RotatedBy(player.itemRotation);
            return true;
        }

        public override void OnKill(int timeLeft) {
            frostTrail?.Close();
            frostTrail = null;
        }

        // === Лента по истории углов: общая для дуги Фростбранда и отточенного Ice Blade ===
        // История — ArcRotation на столько тиков назад (TrailTicks), впереди головы только
        // текущая точка. Радиусы по ArcScaleAt каждого момента: лента растёт вместе с дугой.
        private const float TrailTicks = 14f;        // на сколько тиков назад тянется лента
        private const float TrailMaxAngle = 2.2f;    // рад: лента не оборачивается вокруг игрока
        private const float TrailInnerFrac = 0.55f;  // внутренний край ленты: доля радиуса от внешнего
        private const float TrailHeadDot = 18f;      // размер яркой точки у острия, px
        private const float PeakAt = 0.5f;           // доля взмаха, где пик
        private const float PeakWidthTicks = 3.5f;   // ширина пика (тиков)
        private const float PeakFlashSize = 70f;     // размер вспышки у острия, px
        private const float PeakFlashAlpha = 0.7f;   // её яркость
        private const float PeakStarSize = 64f;      // длина лучей звезды вспышки, px

        // --- Дуга Фростбранда: палитра холодного пламени (та же, что у огоньков и забора) ---
        private static readonly Color FlameCore = new Color(235, 250, 255); // белый жар
        private static readonly Color FlameTail = new Color(115, 200, 255); // голубое пламя
        private static readonly Color FlameEdge = new Color(25, 85, 240);   // глубокий синий
        private const int FlamePoints = 30;            // точек ленты (гладкость как у копья)
        private const float FlameInnerFrac = 0.40f;    // ширина ленты: внешний край — остриё, внутренний — 0.40 его радиуса
        private const float FlameStreaks = 0.55f;      // потоки внутри тела ленты, а не полосы вместо него
        private const float FlameSectorOuter = 1.04f;  // внешний радиус фонового веера: доля длины клинка
        private const float FlameSectorAlpha = 0.30f;  // яркость первой (глубокой) копии веера
        private const float FlameSectorAlpha2 = 0.16f; // яркость второй (светлее, чуть меньше) копии
        private const int FlameTongues = 6;            // язычков-всполохов на хвосте ленты
        private const float FlameTongueSize = 34f;     // длина языка, px

        private static readonly float[] fbAngles = new float[FlamePoints];
        private static readonly float[] fbROut = new float[FlamePoints];
        private static readonly float[] fbRIn = new float[FlamePoints];

        // Настоящий путь клинка, записываемый каждый тик (RecordFlameBlade в AI):
        // угол и длина линии кисть→остриё, новейшая точка первой. Лента по этой
        // записи не может быть кривой относительно меча. Формульная история
        // (BuildTrailHistory) ею не была: ArcRotation переключается между двумя
        // формулами по текущей анимации игрока, и в конце взмаха вся лента
        // перестраивалась по другой кривой; плюс её центр — тело, а не кисть.
        private const int FlameTrailTicks = 18;   // на сколько тиков назад тянется лента
        private float[] fbHistAngle;
        private float[] fbHistRadius;
        private int fbHistCount;
        private Vector2 fbHand;
        private Vector2 fbTipPos;
        private bool fbHasBlade;

        private void RecordFlameBlade(Player player) {
            if (!GetBladeLine(player, out Vector2 butt, out Vector2 tip))
                return;
            fbHistAngle ??= new float[FlamePoints];
            fbHistRadius ??= new float[FlamePoints];
            float angle = (tip - butt).ToRotation();
            if (fbHistCount > 0)
                angle = fbHistAngle[0] + MathHelper.WrapAngle(angle - fbHistAngle[0]);
            for (int i = Math.Min(fbHistCount, FlamePoints - 1); i > 0; i--) {
                fbHistAngle[i] = fbHistAngle[i - 1];
                fbHistRadius[i] = fbHistRadius[i - 1];
            }
            fbHistAngle[0] = angle;
            fbHistRadius[0] = Vector2.Distance(butt, tip);
            fbHistCount = Math.Min(fbHistCount + 1, FlamePoints);
            fbHand = butt;
            fbTipPos = tip;
            fbHasBlade = true;
        }

        private static float Smooth01(float x) {
            x = MathHelper.Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        private static float SwingEnv(float ticks, float maxTicks) {
            // нарастает RiseTicks тиков, гаснет FadeTicks тиков, между ними держится
            return Smooth01(ticks / 3f) * Smooth01((maxTicks - ticks) / 7f);
        }

        private static float PeakPulse(float progress, float maxTicks) {
            float d = (progress - PeakAt) * maxTicks / Math.Max(0.01f, PeakWidthTicks);
            return (float)Math.Exp(-d * d);
        }

        // Радиус линии дуги на момент time, но никогда не короче клинка
        private float ArcLineRadius(float time) {
            return Math.Max(LineTexOuter * ArcScaleAt(time) * BodyDrawScale * LineDrawScale, BladeReach);
        }

        // Дуга Фростбранда — холодное пламя по следу клинка. Набор слоёв как у
        // дуги копья (фоновый слой, лента с бегущими штрихами, головка у
        // острия, детали на хвосте, пик-вспышка), но в палитре пламени и с
        // показом запаса: густота огня = FlameFullness. Запас пуст — фон и
        // языки пропадают, остаётся бледная лента: взмах никого нового не
        // подожжёт, и это видно сразу.
        private void DrawFlameArc(Player player, Texture2D texture, SpriteEffects fx, float progress, float maxAnim) {
            if (Main.dedServ)
                return;
            SwordPlayer sp = player.GetModPlayer<SwordPlayer>();
            float full = sp.FlameFullness;
            float env = SwingEnv(Projectile.localAI[0], maxAnim);
            float level = 0.3f + 0.7f * full;              // пустой меч заметно бледнее, но след виден
            float glowBoost = 0.85f + 0.35f * sp.FireGlow; // горящие рядом подкармливают яркость
            float flameEnv = env * level * glowBoost;

            // ФОНОВЫЙ слой — светящийся веер-сектор на всю дугу, как у косы
            // (там фон читается широкой светящейся площадью с ободком по кругу
            // острия, а не второй полоской у клинка): тело ванильной дуги
            // (кадр 0) двумя тихими копиями глубокого синего. Геометрия та же,
            // что у клинка и ленты: центр — кисть, голова сектора — по
            // записанному углу клинка, внешний радиус — длина клинка.
            // (Беда прежней линии была в формульном радиусе хитбокса из центра
            // тела — она и висела отдельной дугой. Фон мазками и второй лентой
            // владелец забраковал: слипались с лентой в один сноп.)
            if (full > 0.02f && fbHasBlade && fbHistCount >= 1) {
                int dir = Projectile.ai[0] >= 0f ? 1 : -1;
                float sectorRot = fbHistAngle[0] - dir * IceArcFx.HeadAngle;
                float sectorScale = fbHistRadius[0] * FlameSectorOuter / LineTexOuter;
                Rectangle body = texture.Frame(1, 4, 0, 0);
                Vector2 sectorOrigin = body.Size() / 2f;
                Vector2 sectorPos = fbHand - Main.screenPosition;
                Color deep = Color.Lerp(FlameEdge, FlameTail, 0.3f);
                Main.EntitySpriteDraw(texture, sectorPos, body, IceArcFx.Additive(deep, FlameSectorAlpha * flameEnv),
                    sectorRot, sectorOrigin, sectorScale, fx, 0f);
                Main.EntitySpriteDraw(texture, sectorPos, body, IceArcFx.Additive(Color.Lerp(deep, FlameTail, 0.45f), FlameSectorAlpha2 * flameEnv),
                    sectorRot, sectorOrigin, sectorScale * 0.97f, fx, 0f);
            }

            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow == null || glow.IsDisposed || env <= 0.02f)
                return;
            // Фаза мерцания и бега штрихов: минутный цикл (штрихи сдвигаются на
            // целое число повторов текстуры — стык цикла не виден)
            float gt = Main.GameUpdateCount % 3600;
            // Лента — по записанному пути настоящего клинка вокруг кисти
            // (fbHist*): голова ровно на острие, тело — где клинок правда был.
            // Внешний край чуть дышит языками: радиус дрожит, к хвосту сильнее.
            // Запасной путь (первый тик, записи ещё нет) — формульная история
            Vector2 center;
            int n = 0;
            if (fbHasBlade && fbHistCount >= 2) {
                center = fbHand;
                float headA = fbHistAngle[0];
                int cap = Math.Min(fbHistCount, FlameTrailTicks);
                for (int i = 0; i < cap; i++) {
                    if (i > 0 && Math.Abs(fbHistAngle[i] - headA) > TrailMaxAngle)
                        break;
                    float t = i / (float)Math.Max(1, cap - 1);
                    // Форма НЕлинейная (решение владельца): не полоска ровной
                    // толщины по кругу, а язык пламени. Утолщение к середине
                    // взмаха и схождение к краям (bulge: 0 у острия и на хвосте,
                    // 1 в середине), середина чуть выпячена наружу, а внешний
                    // край идёт волнами — крупная медленная волна по длине плюс
                    // мелкая дрожь, к хвосту сильнее. Углы по-прежнему честные
                    // с клинка — гуляют только радиус и толщина
                    float bulge = (float)Math.Sin(Math.PI * Math.Pow(t, 0.85f));
                    float wave = (float)Math.Sin(gt * 1.35f + t * 9f) * 0.05f
                               + (float)Math.Sin(gt * 2.6f + i * 1.15f) * 0.03f;
                    float flick = 1f + full * wave * (0.3f + 0.7f * t) + 0.06f * bulge - 0.04f * t * t;
                    fbAngles[i] = fbHistAngle[i];
                    fbROut[i] = fbHistRadius[i] * flick;
                    float innerFrac = MathHelper.Lerp(0.68f, 0.34f, bulge);
                    fbRIn[i] = Math.Max(BladeReach * 0.2f, fbROut[i] * innerFrac);
                    n++;
                }
            } else {
                center = player.RotatedRelativePoint(player.MountedCenter);
                n = BuildTrailHistory(player, fbAngles, fbROut, fbRIn);
                float outerNow = ArcLineRadius(Projectile.localAI[0]);
                for (int i = 0; i < n; i++) {
                    fbROut[i] = outerNow;
                    fbRIn[i] = Math.Max(BladeReach * 0.2f, fbROut[i] * FlameInnerFrac);
                }
            }
            if (n < 2)
                return;

            // Лента самого клинка поверх фонового веера: градиент + бегущие
            // штрихи-потоки. Её ядро заметно синее белого жара головки: чисто
            // белое ядро на большей части длины аддитивно выгорало в белое
            // пятно-фонарь, особенно на тёмном фоне. Белым остаётся только
            // жар у острия
            GraphicsDevice device = Main.graphics.GraphicsDevice;
            Main.spriteBatch.End();
            try {
                IceRibbon.Draw(device, fbAngles, n, center, fbROut, fbRIn,
                    gt, flameEnv, Color.Lerp(FlameCore, FlameTail, 0.3f), FlameTail, FlameEdge, streaks: true, streakStrength: FlameStreaks);
            }
            finally {
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                    DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
            }

            Vector2 gorigin = glow.Size() / 2f;
            float head = Projectile.rotation;
            Vector2 tip = fbHasBlade ? fbTipPos : center + head.ToRotationVector2() * fbROut[0];

            // Головка у острия: яркая капля жара, дрожит как пламя
            float headFlick = 0.85f + 0.15f * (float)Math.Sin(gt * 2.3f);
            Main.EntitySpriteDraw(glow, tip - Main.screenPosition, null,
                IceArcFx.Additive(FlameCore, flameEnv), 0f, gorigin,
                TrailHeadDot * 1.35f * headFlick / glow.Width, SpriteEffects.None, 0f);

            if (full > 0.02f) {
                // Языки на хвосте: короткие вытянутые всполохи вдоль хода ленты
                for (int i = 0; i < FlameTongues; i++) {
                    float t = (i + 0.5f) / FlameTongues * 0.85f;
                    float tw = 0.5f + 0.5f * (float)Math.Sin(gt * 1.4f + i * 2.4f);
                    if (tw < 0.3f)
                        continue;
                    Vector2 p0 = FlamePoint(center, n, t);
                    Vector2 p1 = FlamePoint(center, n, Math.Min(1f, t + 0.06f));
                    float tang = (p0 - p1).ToRotation(); // по ходу взмаха: от хвоста к голове
                    float len = FlameTongueSize * tw * (1f - t * 0.3f);
                    Color c = Color.Lerp(FlameTail, FlameCore, 0.35f + 0.4f * tw);
                    Main.EntitySpriteDraw(glow, p0 - Main.screenPosition, null,
                        IceArcFx.Additive(c, 0.95f * tw * flameEnv * (1f - t * 0.5f)), tang, gorigin,
                        new Vector2(len / glow.Width, len * 0.16f / glow.Height), SpriteEffects.None, 0f);
                }
            }

            // Пик-вспышка у острия: пятно + звезда, у пустого меча бледная
            float peak = PeakPulse(progress, maxAnim) * flameEnv;
            if (peak > 0.03f) {
                Main.EntitySpriteDraw(glow, tip - Main.screenPosition, null, IceArcFx.Additive(FlameEdge, peak * PeakFlashAlpha), 0f,
                    gorigin, PeakFlashSize * (0.7f + 0.5f * peak) / glow.Width, SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(glow, tip - Main.screenPosition, null, IceArcFx.Additive(FlameCore, peak * PeakFlashAlpha), 0f,
                    gorigin, PeakFlashSize * 0.4f / glow.Width, SpriteEffects.None, 0f);
                IceArcFx.DrawSparkle(glow, tip, PeakStarSize * peak, head + MathHelper.PiOver4, 2,
                    IceArcFx.Additive(Color.Lerp(FlameTail, FlameCore, 0.5f), peak * 0.9f), IceArcFx.Additive(FlameCore, peak));
            }
        }

        // Точка на ленте Фростбранда: t — доля вдоль (0 голова, 1 хвост), radius — доля внешнего радиуса
        private static Vector2 FlamePoint(Vector2 center, int n, float t, float radius = 1f) {
            float f = MathHelper.Clamp(t, 0f, 1f) * (n - 1);
            int i = Math.Min((int)f, n - 2);
            float k = f - i;
            float ang = MathHelper.Lerp(fbAngles[i], fbAngles[i + 1], k);
            float r = MathHelper.Lerp(fbROut[i], fbROut[i + 1], k);
            return center + ang.ToRotationVector2() * r * radius;
        }

        // === Ice Blade: клинок точится об воздух ===
        // Копьё во взмахе сбрасывает иней широкой кометой; меч наоборот — правит всю
        // длину клинка: блик бежит от пяты к острию, за кромкой остаётся веер рисок,
        // с шершавого клинка сыплется стружка наледи, с отточенного — искры правки.
        // В конце правки остриё «дзинькает». Чем выше полировка, тем тоньше и ярче след.
        private const int HonePoints = 16;          // точек истории угла клинка
        private const float HoneStart = 0.12f;      // доля взмаха, когда блик трогается с пяты
        private const float HoneSpan = 0.6f;        // за какую долю взмаха он доходит до острия
        private const float TingTicks = 7f;         // сколько тиков живёт «дзинь»

        // История — свой массив у каждого снаряда (ArcRotation во время взмаха даёт только
        // текущий угол клинка, поэтому прошлые углы приходится запоминать по тикам)
        private float[] honeAngles;
        private float[] honeRadii;
        private int honeCount;
        private float tingTick = -1f;

        private bool IsIceBlade(Player player) {
            int item = SwordItem != 0 ? SwordItem : player.HeldItem.type;
            return item == ItemID.IceBlade;
        }

        private static float HoneGlintAt(float progress) => Smooth01((progress - HoneStart) / HoneSpan);

        // Пята и остриё настоящего клинка (запасной вариант — по углу дуги) и блик между ними
        private void HoneLine(Player player, float progress, out Vector2 butt, out Vector2 tip, out Vector2 glint, out Vector2 bladeDir) {
            if (!GetBladeLine(player, out butt, out tip) || Vector2.DistanceSquared(butt, tip) < 4f) {
                Vector2 center = player.RotatedRelativePoint(player.MountedCenter);
                Vector2 d = Projectile.rotation.ToRotationVector2();
                butt = center + d * HandReach;
                tip = center + d * Math.Max(BladeReach, 30f);
            }
            bladeDir = (tip - butt).SafeNormalize(Projectile.rotation.ToRotationVector2());
            glint = Vector2.Lerp(IceBladeFx.HeelOf(butt, tip), tip, HoneGlintAt(progress));
        }

        // Куда клинок движется: +1 — угол растёт, -1 — убывает
        private int SwingSign() {
            if (honeCount >= 2 && Math.Abs(honeAngles[0] - honeAngles[1]) > 0.001f)
                return honeAngles[0] > honeAngles[1] ? 1 : -1;
            return Projectile.ai[0] >= 0f ? 1 : -1;
        }

        private void UpdateHone(Player player, float maxAnim) {
            honeAngles ??= new float[HonePoints];
            honeRadii ??= new float[HonePoints];
            // Сдвиг истории; угол разворачиваем относительно прошлого, чтобы не прыгал на 2π
            float angle = Projectile.rotation;
            if (honeCount > 0)
                angle = honeAngles[0] + MathHelper.WrapAngle(angle - honeAngles[0]);
            for (int i = Math.Min(honeCount, HonePoints - 1); i > 0; i--) {
                honeAngles[i] = honeAngles[i - 1];
                honeRadii[i] = honeRadii[i - 1];
            }
            honeAngles[0] = angle;
            honeRadii[0] = ArcLineRadius(Projectile.localAI[0]);
            honeCount = Math.Min(honeCount + 1, HonePoints);

            // Настоящий путь клинка вокруг кисти — та же запись, что у Фростбранда
            // (fbHist*): веер и лента Ice Blade строятся по ней, форма дуги общая
            RecordFlameBlade(player);

            float progress = Projectile.localAI[0] / maxAnim;
            float env = SwingEnv(Projectile.localAI[0], maxAnim);
            float polish = IceBladeFx.PolishOf(player, SwordItem);
            float rough = 1f - polish;
            HoneLine(player, progress, out Vector2 butt, out Vector2 tip, out Vector2 glint, out Vector2 bladeDir);
            int sign = SwingSign();
            Vector2 radial = Projectile.rotation.ToRotationVector2();
            Vector2 tangent = radial.RotatedBy(MathHelper.PiOver2 * sign);

            // Под бликом воздух снимает с клинка: наледь — стружкой, металл — искрами.
            // Всё летит назад, против хода клинка. Только пока клинок реально очищается
            bool cleaning = player.GetModPlayer<SwordPlayer>().IsPolishing(SwordItem != 0 ? SwordItem : player.HeldItem.type);
            if (env > 0.05f && cleaning) {
                int shavings = IceBladeFx.Count(env * rough * 0.45f);
                for (int i = 0; i < shavings; i++) {
                    Vector2 vel = -tangent * Main.rand.NextFloat(1.5f, 3.2f) + radial * Main.rand.NextFloat(0.3f, 1.4f);
                    IceBladeFx.SpawnShaving(glint + Main.rand.NextVector2Circular(3f, 3f), vel, Main.rand.NextFloat(0.8f, 1.2f));
                }
                int sparks = IceBladeFx.Count(env * (0.08f + 0.3f * polish));
                for (int i = 0; i < sparks; i++) {
                    Vector2 vel = -tangent * Main.rand.NextFloat(2f, 4.5f) + radial * Main.rand.NextFloat(-0.6f, 1.2f);
                    IceBladeFx.SpawnSpark(glint, vel, Main.rand.NextFloat(0.8f, 1.15f));
                }
                Lighting.AddLight(glint, IceBladeFx.EdgeRim.ToVector3() * (0.25f + 0.55f * polish) * env * 0.7f);
            }

            // Брусок сошёл с острия — «дзинь»: кромки звенят, остатки наледи стряхивает
            // с них наружу по всей длине (не сноп искр с острия)
            if (tingTick < 0f && HoneGlintAt(progress) >= 0.98f) {
                tingTick = Projectile.localAI[0];
                if (!cleaning)
                    return;
                Vector2 heel = IceBladeFx.HeelOf(butt, tip);
                Vector2 across = bladeDir.RotatedBy(MathHelper.PiOver2);
                int chips = (int)Math.Round(rough * 2f);
                for (int i = 0; i < chips; i++) {
                    int side = i % 2 == 0 ? 1 : -1;
                    Vector2 at = IceBladeFx.EdgePoint(heel, tip, IceBladeFx.BladeHalfWidth, Main.rand.NextFloat(0.2f, 1f), side);
                    IceBladeFx.SpawnShaving(at, across * side * Main.rand.NextFloat(0.8f, 1.8f) - tangent * Main.rand.NextFloat(0.3f, 1f),
                        Main.rand.NextFloat(0.8f, 1.2f));
                }
                Lighting.AddLight(Vector2.Lerp(heel, tip, 0.6f), IceBladeFx.EdgeCore.ToVector3() * (0.25f + 0.5f * polish));
            }
        }

        // Удар по цели: срывает с кромки крошку / высекает искру (только визуал)
        private static void HoneHitBurst(Player player, NPC target, int swordItem) {
            if (Main.dedServ)
                return;
            float polish = IceBladeFx.PolishOf(player, swordItem);
            Vector2 at = target.Hitbox.ClosestPointInRect(player.MountedCenter);
            Vector2 away = (at - player.MountedCenter).SafeNormalize(Vector2.UnitX);
            int chips = (int)Math.Round((1f - polish) * 2f);
            for (int i = 0; i < chips; i++)
                IceBladeFx.SpawnShaving(at, away.RotatedByRandom(1.1f) * Main.rand.NextFloat(1f, 2.6f), Main.rand.NextFloat(0.8f, 1.1f));
            int sparks = (int)Math.Round(polish * 2f);
            for (int i = 0; i < sparks; i++)
                IceBladeFx.SpawnSpark(at, away.RotatedByRandom(1.2f) * Main.rand.NextFloat(2f, 4f), 1f);
        }

        // Дуга Ice Blade. Шершавый клинок — своя дуга (IceBladeFx.DrawSwingArc: бледное свечение
        // формы Excalibur, матовая белая линия). По мере заточки она перетекает в дугу косы
        // (DrawKeenArc: лента IceRibbon, ореол, снежинки, вспышка).
        private const float KeenFrom = 0.25f;   // с какой полировки начинает проступать дуга косы
        private const float KeenSpan = 0.5f;    // за какой отрезок полировки переход завершается

        private void DrawHone(Player player, Texture2D texture, float scale, SpriteEffects fx, float progress, float fade, float maxAnim) {
            if (Main.dedServ)
                return;
            float polish = IceBladeFx.PolishOf(player, SwordItem);
            float keen = Smooth01((polish - KeenFrom) / KeenSpan);
            float dir = Projectile.ai[0] >= 0f ? 1f : -1f;
            // Форма как у Фростбранда: веер центром на кисти и размером по
            // настоящему клинку (запись fbHist*), а не формульный сектор вокруг
            // центра тела с растущим радиусом. Нет записи (первый тик) — старый путь.
            // Центр — ЖИВАЯ кисть в момент отрисовки (player.itemLocation), а не
            // записанная в AI fbHand: клинок слой рисует от живой кисти, а запись
            // могла отставать от неё на пиксель-другой хода руки — дуга казалась
            // ушедшей вглубь относительно клинка. Углы и радиус — по-прежнему запись
            bool rec = fbHasBlade && fbHistCount >= 1;
            // Ванильный слеш — всегда: и на тупом, и на отточенном клинке он остаётся
            IceBladeFx.DrawSwingArc(texture,
                rec ? player.itemLocation : Projectile.Center,
                rec ? fbHistAngle[0] : Projectile.rotation, dir,
                rec ? fbHistRadius[0] / LineTexOuter : scale * LineDrawScale, fx,
                progress, fade, polish);

            float env = SwingEnv(Projectile.localAI[0], maxAnim);
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            bool glowOk = glow != null && !glow.IsDisposed;
            // Дуга серпа (лента по следу удара) — только после заточки, и тем ярче, чем острее
            if (keen > 0.01f && glowOk)
                DrawKeenArc(player, texture, glow, scale, fx, fade, env, keen);

            // Бегущий блик-брусок (белая звезда от игрока к острию) убран по
            // решению владельца — мельтешил на клинке. Точка блика как логика
            // осталась: от неё сыплются стружка и искры в UpdateHone
        }

        // === Дуга косы на отточенном клинке ===
        // Тот же набор, что у SickleArcProjectile, и тот же двухслойный вид: у косы лента
        // идёт по следу удара (BuildTrailHistory), а оружие метит отдельная дуга — у меча это
        // белая линия (здесь) и шершавый сектор Excalibur (DrawSwingArc).
        // Дальше: лента IceRibbon по следу, мягкий ореол вдоль неё, снежинки и мерцание на хвосте.
        private const float KeenHaloWidth = 56f;      // ореол у головы, px
        private const float KeenHaloWidthTail = 14f;  // ореол на хвосте, px
        private const float KeenHaloAlpha = 0.22f;

        private const int KeenPoints = 28;
        private static readonly float[] keenAngles = new float[KeenPoints];
        private static readonly float[] keenROut = new float[KeenPoints];
        private static readonly float[] keenRIn = new float[KeenPoints];

        // История углов следа: голова — текущий угол дуги, дальше — прошлые углы
        // (ArcRotation). Радиус у ленты единый текущий — кольцом по краю веера. Лента ложится по реальному пути взмаха,
        // а не по фиксированному сектору; длина ограничена по доле взмаха и по углу (TrailMaxAngle).
        // Этим же строятся и шлейф, и лента отточенного клинка — как у косы.
        private int BuildTrailHistory(Player player, float[] angles, float[] rOut, float[] rIn) {
            float head = Projectile.rotation;
            float maxAnim = Math.Max(1f, Projectile.ai[1]);
            // Прошлые углы берём РАВНОМЕРНО ПО УГЛУ взмаха, а не по тикам: при
            // нелинейной скорости клинок в начале взмаха за те же тики проходит
            // угол в разы больше, и тиковая лента мгновенно растягивалась на всю
            // длину огромным веером (так сломалась дуга Ice Blade после кривой).
            // По изогнутому прогрессу лента на каждом участке дуги выглядит как
            // при старой линейной скорости — нелинейна только скорость головы.
            float pNow = SwingProgressCurve(MathHelper.Clamp(Projectile.localAI[0] / maxAnim, 0f, 1f), SwordItem);
            float stepP = TrailTicks / maxAnim / (angles.Length - 1);
            // Радиус — ЕДИНЫЙ текущий на всей длине ленты, как у Фростбранда:
            // лента кольцом ложится на внешний край веера, дуги стакаются по
            // высоте на всей длине, а не только у головы. Раньше радиус брался по
            // времени каждой точки — лента шла спиралью внутрь к хвосту и на
            // хвосте заметно расходилась с веером (две дуги разного размера)
            float rNow = ArcLineRadius(Projectile.localAI[0]);
            int n = 1;
            angles[0] = head;
            rOut[0] = rNow;
            rIn[0] = Math.Max(BladeReach * 0.2f, rOut[0] * TrailInnerFrac);
            for (int i = 1; i < angles.Length; i++) {
                float pi = pNow - i * stepP;
                if (pi < 0f)
                    break;
                float ti = SwingProgressInverse(pi) * maxAnim;
                float angle = head + MathHelper.WrapAngle(ArcRotation(player, ti) - head);
                float delta = Math.Abs(angle - head);
                if (delta > TrailMaxAngle) {
                    // последняя точка — ровно по TrailMaxAngle, дальше ленту не тянем
                    float prevDelta = Math.Abs(angles[n - 1] - head);
                    float f = MathHelper.Clamp((TrailMaxAngle - prevDelta) / Math.Max(0.0001f, delta - prevDelta), 0f, 1f);
                    angles[n] = MathHelper.Lerp(angles[n - 1], angle, f);
                    rOut[n] = rNow;
                    rIn[n] = Math.Max(BladeReach * 0.2f, rOut[n] * TrailInnerFrac);
                    n++;
                    break;
                }
                angles[n] = angle;
                rOut[n] = rNow;
                rIn[n] = Math.Max(BladeReach * 0.2f, rOut[n] * TrailInnerFrac);
                n++;
            }
            return n;
        }

        // История ленты по ЗАПИСИ настоящего клинка (fbHist*, как у Фростбранда):
        // углы и радиусы «кисть → остриё», записанные по тикам в AI, растянутые
        // на все точки ленты. Лента ложится на настоящий путь острия вокруг
        // кисти и заканчивается там, где клинок реально был, — та же форма
        // дуги, что у Фростбранда, а не формульный веер вокруг центра тела
        private int BuildRecordedHistory(float[] angles, float[] rOut, float[] rIn) {
            int cap = Math.Min(fbHistCount, (int)TrailTicks);
            if (cap < 2)
                return 0;
            int n = 0;
            for (int i = 0; i < angles.Length; i++) {
                float f = i / (float)(angles.Length - 1) * (cap - 1);
                int i0 = Math.Min((int)f, cap - 2);
                float k = f - i0;
                angles[n] = MathHelper.Lerp(fbHistAngle[i0], fbHistAngle[i0 + 1], k);
                rOut[n] = MathHelper.Lerp(fbHistRadius[i0], fbHistRadius[i0 + 1], k);
                rIn[n] = Math.Max(6f, rOut[n] * TrailInnerFrac);
                n++;
            }
            return n;
        }

        // Точка на ленте: t — доля вдоль (0 голова, 1 хвост), radius — доля внешнего радиуса
        private static Vector2 KeenPoint(Vector2 center, int n, float t, float radius) {
            float f = MathHelper.Clamp(t, 0f, 1f) * (n - 1);
            int i = Math.Min((int)f, n - 2);
            float k = f - i;
            float ang = MathHelper.Lerp(keenAngles[i], keenAngles[i + 1], k);
            float r = MathHelper.Lerp(keenROut[i], keenROut[i + 1], k);
            return center + ang.ToRotationVector2() * r * radius;
        }

        private void DrawKeenArc(Player player, Texture2D texture, Texture2D glow, float scale, SpriteEffects fx, float fade, float env, float keen) {
            IceBladeFx.ArcColors(IceBladeFx.PolishOf(player, SwordItem), out Color core, out Color tail, out Color edge);

            // Форма как у Фростбранда: центр — кисть, угол и радиус — настоящий
            // клинок из записи (fbHist*); лента — по записанному пути острия.
            // Нет записи (первый тик) — прежний формульный путь вокруг центра тела
            bool rec = fbHasBlade && fbHistCount >= 1;
            Vector2 center = rec ? player.itemLocation : Projectile.Center;
            float headAngle = rec ? fbHistAngle[0] : Projectile.rotation;
            float lineScale = rec ? fbHistRadius[0] / LineTexOuter : scale * LineDrawScale;

            // линейная дуга меча: светлая бирюза клинка (у ванили и косы она белая).
            // Посадка как у сектора DrawSwingArc: голова линии — ровно на клинке,
            // вся линия позади него
            float dir = Projectile.ai[0] >= 0f ? 1f : -1f;
            Rectangle line = texture.Frame(1, 4, 0, 3);
            Main.EntitySpriteDraw(texture, center - Main.screenPosition, line,
                IceBladeFx.ArcLineColor(IceBladeFx.PolishOf(player, SwordItem)) * (0.25f * fade * keen),
                headAngle - dir * IceArcFx.HeadAngle, texture.Frame(1, 4).Size() / 2f, lineScale, fx, 0f);

            DrawTrailRibbon(player, glow, env * keen, core, tail, edge, rec ? center : null, rec);
        }

        // Лента по следу взмаха + мягкий ореол под ней — общая часть дуг шаблона
        // (отточенный Ice Blade и праздничные мечи): ореол мазками, лента
        // IceRibbon. Цвета задаёт вызвавший. recorded=true — история из записи
        // настоящего клинка вокруг кисти (fbHist*, форма Фростбранда),
        // иначе формульная история вокруг центра тела (BuildTrailHistory)
        private void DrawTrailRibbon(Player player, Texture2D glow, float env, Color core, Color tail, Color edge,
            Vector2? centerOverride = null, bool recorded = false) {
            if (env <= 0.02f)
                return;
            Vector2 center = centerOverride ?? player.RotatedRelativePoint(player.MountedCenter);
            int n = recorded ? BuildRecordedHistory(keenAngles, keenROut, keenRIn)
                             : BuildTrailHistory(player, keenAngles, keenROut, keenRIn);
            if (n < 2)
                return;

            // ореол под лентой: мягкие мазки по следу головы, сужаются и гаснут к хвосту.
            // Путь мазков сдвинут ВНУТРЬ на полуширину: внешний край ореола вровень
            // с внешним краем ленты и веера. Раньше мазки центрировались ровно на
            // внешнем радиусе — половина каждого торчала наружу, и над дугой стоял
            // гребень «зубцов» ореола (лента казалась ниже второй дуги)
            Color haloCol = Color.Lerp(tail, edge, 0.5f);
            float rOutNow = keenROut[0];
            // Ширина мазков — не шире самой полосы ленты: на малом радиусе вокруг
            // кисти широкий ореол залил бы всё внутрь полосы, и дуга снова
            // читалась бы сплошным веером, а не полосой по пути клинка
            float haloScale = Math.Min(1f, Math.Max(0.2f, (rOutNow - keenRIn[0]) * 1.6f / KeenHaloWidth));
            float wStart = KeenHaloWidth * haloScale;
            Vector2 prev = KeenPoint(center, n, 0f,
                rOutNow > 1f ? Math.Max(0f, (rOutNow - wStart * 0.5f) / rOutNow) : 1f);
            // мазков много и они плотно перекрываются — ореол сплошной, без бусин
            const int HaloSteps = 24;
            for (int j = 1; j <= HaloSteps; j++) {
                float t0 = (j - 1) / (float)HaloSteps, t1 = j / (float)HaloSteps;
                float w0 = MathHelper.Lerp(KeenHaloWidth, KeenHaloWidthTail, t0) * haloScale;
                float w1 = MathHelper.Lerp(KeenHaloWidth, KeenHaloWidthTail, t1) * haloScale;
                Vector2 next = KeenPoint(center, n, t1,
                    rOutNow > 1f ? Math.Max(0f, (rOutNow - w1 * 0.5f) / rOutNow) : 1f);
                IceArcFx.DrawSegment(glow, prev, next, w0, w1,
                    IceArcFx.Additive(haloCol, KeenHaloAlpha * env * (1f - (t0 + t1) * 0.5f)));
                prev = next;
            }

            // лента
            GraphicsDevice device = Main.graphics.GraphicsDevice;
            Main.spriteBatch.End();
            try {
                IceRibbon.Draw(device, keenAngles, n, center, keenROut, keenRIn, Projectile.localAI[0], env, core, tail, edge, streaks: false);
            }
            finally {
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                    DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
            }
        }

        // Дуга праздничных мечей (подкласс FestiveSwords): чистый шаблон без
        // механик — тело и линия по рецепту шершавого Ice Blade (DrawSwingArc)
        // плюс лента с ореолом (DrawTrailRibbon), цвета свои у каждого меча.
        private void DrawFestiveArc(Player player, Texture2D texture, float scale, SpriteEffects fx, float fade, float maxAnim) {
            if (Main.dedServ)
                return;
            FestiveSwords.ArcColors(SwordItem != 0 ? SwordItem : player.HeldItem.type, out Color core, out Color tail, out Color edge);

            Vector2 pos = Projectile.Center - Main.screenPosition;
            Rectangle body = texture.Frame(1, 4);
            Rectangle line = texture.Frame(1, 4, 0, 3);
            Vector2 origin = body.Size() / 2f;
            // Посадка веера как у Фростбранда и Ice Blade: передний край — ровно
            // на клинке, веер целиком позади него
            float dir = Projectile.ai[0] >= 0f ? 1f : -1f;
            float rot = Projectile.rotation - dir * IceArcFx.HeadAngle;
            // тело: полупрозрачное свечение формы взмаха, две копии на одном угле
            Main.EntitySpriteDraw(texture, pos, body, IceArcFx.Additive(tail, fade * 0.16f),
                rot, origin, scale * LineDrawScale, fx, 0f);
            Main.EntitySpriteDraw(texture, pos, body, IceArcFx.Additive(Color.Lerp(tail, core, 0.4f), fade * 0.08f),
                rot, origin, scale * LineDrawScale * 0.975f, fx, 0f);
            // линия по кромке взмаха
            Main.EntitySpriteDraw(texture, pos, line, core * (fade * 0.3f),
                rot, origin, scale * LineDrawScale, fx, 0f);

            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow != null && !glow.IsDisposed)
                DrawTrailRibbon(player, glow, SwingEnv(Projectile.localAI[0], maxAnim), core, tail, edge);
        }

        // Углы контрольного сектора на момент time (+ центр/радиус серверного
        // запасного пути): позади клинка и только в уже заметённой части взмаха —
        // голова на клинке, хвост не дальше
        // ArcAngleBack+ArcAngleFront назад и не раньше начала взмаха. Раньше
        // сектор стоял головой на +56° ВПЕРЕДИ клинка и бил туда, где клинок
        // ещё не был; полный урон теперь даёт только сама полоса клинка (TryHit)
        private void GetHitSector(Player player, float time, out Vector2 center, out float outer, out float startAngle, out float sweep) {
            GetArcState(player, time, out float rotation, out float scale);
            // Центр — живая позиция игрока, а не Projectile.Center: тот ставится в AI
            // и на бегу может отставать от игрока на тик
            center = player.RotatedRelativePoint(player.MountedCenter);
            outer = Math.Max(LineTexOuter * scale * BodyDrawScale * LineDrawScale, BladeReach);
            float dir = Projectile.ai[0] >= 0f ? 1f : -1f;
            GetArcState(player, 0f, out float startRot, out _);
            float back = rotation - dir * (ArcAngleBack + ArcAngleFront);
            back = dir > 0f ? Math.Max(back, startRot) : Math.Min(back, startRot);
            startAngle = dir > 0f ? back : rotation;
            sweep = Math.Abs(rotation - back);
        }

        // Клин зоны клинка: от кисти, между углом клинка прошлого и текущего тика
        // (заметённая за тик часть пути) плюс толщина клинка по краям. Углы — та же
        // формула, что рисует клинок (GetArcState), поэтому зона и картинка в один тик
        private void GetBladeWedge(Player player, out Vector2 hand, out float startAngle, out float sweep) {
            hand = player.itemLocation;
            float time = Projectile.localAI[0];
            GetArcState(player, time, out float now, out _);
            GetArcState(player, Math.Max(0f, time - 1f), out float prev, out _);
            startAngle = Math.Min(now, prev) - BladeWedgePad;
            sweep = Math.Abs(now - prev) + BladeWedgePad * 2f;
        }

        // Настоящая длина клинка (приклад → остриё) в мире сейчас. БЕЗ масштаба
        // предмета: SwingBladeLayer и запись клинка (fbHistRadius) живут в
        // масштабе 1 — хитбокс обязан совпадать с нарисованным остриём, а не с
        // BladeReach из спавна. 0 — геометрии нет (выделенный сервер): TryHit
        // уходит в запасной сектор, как у копья
        private float BladeLengthNow(Player player) {
            if (Main.dedServ || player.HeldItem.IsAir)
                return 0f;
            int itemType = SwordItem != 0 ? SwordItem : player.HeldItem.type;
            if (itemType <= 0)
                return 0f;
            return SickleSprite.Get(itemType).Length;
        }

        // Спад от расстояния за кончиком клинка: 0 — как клинком, 1 — край линии дуги
        private float FalloffEdge(float distance, float reach) => FalloffEdge(distance, reach,
            LineTexOuter * BodyDrawScale * LineDrawScale * (ArcScaleStart + ArcScaleGrowth) * Projectile.ai[2]);

        private static float FalloffEdge(float distance, float reach, float maxReach) {
            if (maxReach <= reach)
                return 0f;
            return MathHelper.Clamp((distance - reach) / (maxReach - reach), 0f, 1f);
        }

        // Внешний радиус контрольной оболочки ОТ КИСТИ сейчас: остриё + прежняя
        // толщина оболочки (радиус линии от центра минус BladeReach). Оболочка
        // обязана сидеть на том же центре, что клинок и нарисованная дуга (кисть):
        // от центра тела получался мёртвый серп сбоку от игрока и перелёт с другой
        // стороны — хитбокс не совпадал с картинкой (замечание владельца)
        private float ControlOuterNow(Player player, float bladeLen) {
            GetArcState(player, Projectile.localAI[0], out _, out float arcScale);
            return bladeLen + (LineTexOuter * arcScale * BodyDrawScale * LineDrawScale - BladeReach);
        }

        // Попадание и «краевой» коэффициент урона edge (0 — полный, 1 — минимальный) —
        // как у копья (SickleArcProjectile.TryHit):
        // 1) Клинок: заметённый за тик клин от кисти до острия — полный урон.
        // 2) Контроль дальше острия: сектор позади клинка до края линии, урон падает.
        // Нет геометрии (сервер) — запасной сектор GetHitSector целиком.
        private bool TryHit(Rectangle target, out float edge) {
            edge = 1f;
            Player player = Main.player[Projectile.owner];
            GetHitSector(player, Projectile.localAI[0], out Vector2 center, out float outer,
                out float startAngle, out float sweep);

            float bladeLen = BladeLengthNow(player);
            if (bladeLen <= 1f) {
                if (!NearestInWedge(target, center, startAngle, sweep, out float distance, out _) || distance > outer)
                    return false;
                edge = FalloffEdge(distance, BladeReach);
                return true;
            }

            GetBladeWedge(player, out Vector2 hand, out float bladeStart, out float bladeSweep);
            if (NearestInWedge(target, hand, bladeStart, bladeSweep, out float bladeDist, out _) && bladeDist <= bladeLen) {
                edge = 0f;
                return true;
            }

            // Контроль: оболочка за остриём вокруг кисти в уже заметённой части
            // сектора (углы — GetHitSector). Цель обязана выступать за остриё
            float controlOuter = ControlOuterNow(player, bladeLen);
            if (controlOuter <= bladeLen)
                return false;
            if (!NearestInWedge(target, hand, startAngle, sweep, out float dist, out float farthest)
                || dist > controlOuter || farthest <= bladeLen)
                return false;
            float maxReachCenter = LineTexOuter * BodyDrawScale * LineDrawScale * (ArcScaleStart + ArcScaleGrowth) * Projectile.ai[2];
            edge = FalloffEdge(dist, bladeLen, bladeLen + (maxReachCenter - BladeReach));
            return true;
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) {
            return TryHit(targetHitbox, out _);
        }

        private static readonly Vector2[] clipBufferA = new Vector2[16];
        private static readonly Vector2[] clipBufferB = new Vector2[16];

        // Ближайшая и самая дальняя точки прямоугольника внутри клина {угол от
        // startAngle до startAngle + sweep}, sweep <= π. false — прямоугольник в
        // клин не попадает. Точно, без выборки точек: режем прямоугольник клином —
        // остаётся выпуклый многоугольник, ищем его крайние по радиусу точки.
        private static bool NearestInWedge(Rectangle rect, Vector2 center, float startAngle, float sweep,
            out float distance, out float farthest) {
            distance = 0f;
            farthest = 0f;
            clipBufferA[0] = new Vector2(rect.Left, rect.Top) - center;
            clipBufferA[1] = new Vector2(rect.Right, rect.Top) - center;
            clipBufferA[2] = new Vector2(rect.Right, rect.Bottom) - center;
            clipBufferA[3] = new Vector2(rect.Left, rect.Bottom) - center;

            // Клин — две полуплоскости через центр
            Vector2 startDir = startAngle.ToRotationVector2();
            Vector2 endDir = (startAngle + sweep).ToRotationVector2();
            int count = ClipByHalfPlane(clipBufferA, 4, clipBufferB, new Vector2(-startDir.Y, startDir.X));
            count = ClipByHalfPlane(clipBufferB, count, clipBufferA, new Vector2(endDir.Y, -endDir.X));
            if (count == 0)
                return false;

            // Центр — вершина клина, внутрь многоугольника он не попадает, поэтому
            // ближняя точка всегда лежит на одном из рёбер
            float nearestSq = float.MaxValue, farthestSq = 0f;
            for (int i = 0; i < count; i++) {
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

        // Оставляет часть выпуклого многоугольника, где Dot(точка, normal) >= 0
        private static int ClipByHalfPlane(Vector2[] input, int count, Vector2[] output, Vector2 normal) {
            int outCount = 0;
            for (int i = 0; i < count; i++) {
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

        // Отладка (DebugHitbox): всё от кисти, как настоящий хитбокс: голубая пыль —
        // остриё по углам контрольного сектора, розовая — край контрольной оболочки,
        // изумрудная — остриё по клину зоны клинка этого тика
        private void ShowHitbox(Player player) {
            GetHitSector(player, Projectile.localAI[0], out Vector2 center, out float outer, out float startAngle, out float sweep);
            float bladeLen = BladeLengthNow(player);
            if (bladeLen > 1f) {
                GetBladeWedge(player, out Vector2 hand, out float bladeStart, out float bladeSweep);
                float controlOuter = ControlOuterNow(player, bladeLen);
                for (int i = 0; i <= 12; i++) {
                    Vector2 dir = (startAngle + sweep * i / 12f).ToRotationVector2();
                    Dust.NewDustPerfect(hand + dir * bladeLen, DustID.BlueFairy, Vector2.Zero).noGravity = true;
                    Dust.NewDustPerfect(hand + dir * controlOuter, DustID.Pixie, Vector2.Zero).noGravity = true;
                }
                for (int i = 0; i <= 4; i++) {
                    Vector2 dir = (bladeStart + bladeSweep * i / 4f).ToRotationVector2();
                    Dust.NewDustPerfect(hand + dir * bladeLen, DustID.GemEmerald, Vector2.Zero).noGravity = true;
                }
            }
            else {
                for (int i = 0; i <= 12; i++) {
                    Vector2 dir = (startAngle + sweep * i / 12f).ToRotationVector2();
                    Dust.NewDustPerfect(center + dir * BladeReach, DustID.BlueFairy, Vector2.Zero).noGravity = true;
                    Dust.NewDustPerfect(center + dir * outer, DustID.Pixie, Vector2.Zero).noGravity = true;
                }
            }
        }

        public override void CutTiles() {
            Vector2 starting = (Projectile.rotation - MathHelper.PiOver4).ToRotationVector2() * 40f * Projectile.scale;
            Vector2 ending = (Projectile.rotation + MathHelper.PiOver4).ToRotationVector2() * 40f * Projectile.scale;
            float width = 40f * Projectile.scale;
            Utils.PlotTileLine(Projectile.Center + starting, Projectile.Center + ending, width, DelegateMethods.CutTiles);
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers) {
            // Урон и отбрасывание от зоны: клинком — как мечом, дальше острия к краю линии — до крошечных
            TryHit(target.Hitbox, out float edge);
            // Урон: крутой спад сразу за клинком, дальше крошечный; защита вычтется уже после этого
            float falloff = MathHelper.Clamp(edge / DamageFalloffSpan, 0f, 1f);
            falloff = falloff * falloff * (3f - 2f * falloff);
            float damageMult = MathHelper.Lerp(1f, EdgeDamageMult, falloff);
            if (Projectile.damage > 0)
                damageMult = Math.Max(damageMult, Math.Min(1f, MinEdgeDamage / Projectile.damage));
            modifiers.SourceDamage *= damageMult;
            modifiers.Knockback *= MathHelper.Lerp(1f, EdgeKnockbackMult, edge);
            // Отталкиваем от игрока в любую сторону, в том числе за спиной. Раньше это
            // стояло в OnHitNPC и не работало: там hit — копия, удар уже нанесён.
            modifiers.HitDirectionOverride = (Main.player[Projectile.owner].Center.X < target.Center.X) ? 1 : (-1);
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) {
            if (SwordItem == ItemID.IceBlade) {
                // крошка/искры с кромки — только если удар реально очистил клинок
                if (Main.player[Projectile.owner].GetModPlayer<SwordPlayer>().AddPolish(SwordItem))
                    HoneHitBurst(Main.player[Projectile.owner], target, SwordItem);
            }
            // Frostbrand сам НЕ поджигает: поджигает только струя. У взмаха это
            // веер струй по дуге (SpawnArcJets в AI) — пламя, которое клинок
            // несёт за собой по ходу взмаха
        }

        public override void OnHitPlayer(Player target, Player.HurtInfo info) {
            info.HitDirection = (Main.player[Projectile.owner].Center.X < target.Center.X) ? 1 : (-1);
        }

        public override bool PreDraw(ref Color lightColor) {
            Vector2 position = Projectile.Center - Main.screenPosition;
            Texture2D texture = TextureAssets.Projectile[Type].Value;
            if (texture == null || texture.IsDisposed)
                texture = ModContent.Request<Texture2D>("Terraria/Images/Projectile_" + ProjectileID.Excalibur).Value;
            if (texture == null || texture.IsDisposed)
                return false;
            Rectangle sourceRectangle = texture.Frame(1, 4);
            Vector2 origin = sourceRectangle.Size() / 2f;
            // Масштаб веера — от ArcLineRadius, как у ленты отточенной дуги:
            // внешний край сектора и внешний край ленты — один радиус в каждый
            // момент взмаха, дуги лежат одна на другой. Раньше веер брал голый
            // Projectile.scale (без «не короче клинка»), и в начале взмаха
            // заточенная дуга была заметно крупнее шершавой — две дуги разного
            // размера (замечание владельца)
            float scale = ArcLineRadius(Projectile.localAI[0]) / LineTexOuter / LineDrawScale;
            SpriteEffects spriteEffects = ((!(Projectile.ai[0] >= 0f)) ? SpriteEffects.FlipVertically : SpriteEffects.None);
            float maxAnim = Math.Max(1f, Projectile.ai[1]);
            float percentageOfLife = Projectile.localAI[0] / maxAnim;
            float lerpTime = Utils.Remap(percentageOfLife, 0f, 0.6f, 0f, 1f) * Utils.Remap(percentageOfLife, 0.6f, 1f, 1f, 0f);

            Player owner = Main.player[Projectile.owner];
            if (IsIceBlade(owner)) {
                DrawHone(owner, texture, scale, spriteEffects, percentageOfLife, lerpTime, maxAnim);
                return false;
            }
            // Праздничные мечи (подкласс FestiveSwords): шаблонная дуга в своих цветах
            if (FestiveSwords.IsFestive(SwordItem != 0 ? SwordItem : owner.HeldItem.type)) {
                DrawFestiveArc(owner, texture, scale, spriteEffects, lerpTime, maxAnim);
                return false;
            }
            // Второй меч движка — Фростбранд: у него своя дуга холодного пламени
            DrawFlameArc(owner, texture, spriteEffects, percentageOfLife, maxAnim);
            return false;
        }

        public override void Unload() {
            if (Main.dedServ)
                return;
            IceRibbon.Unload();
            IceBladeFx.Unload();
        }
    }
}
