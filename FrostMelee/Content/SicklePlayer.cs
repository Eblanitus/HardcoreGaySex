using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using FrostMelee.Content.Items;
using FrostMelee.Content.Projectiles;

namespace FrostMelee.Content
{
    // Мозг системы приёмов серпа (ФИЛОСОФИЯ.md, единый набор для всей мили):
    // ЛКМ зажать — взмах (ванильный, дугу спавнит SickleGlobalItem)
    // ПКМ тап — тумблер копья
    // ПКМ зажать >порога — тычки
    // ПКМ зажата + клик ЛКМ — бросок (замах играет после клика)
    // Режимы не перекрываются: взмах, копьё и тычок в кадре только по одному.
    public class SicklePlayer : ModPlayer
    {
        // === Заморозка ледяного копья: шкала 0..1. Копят тычок и выставленное копьё, тратит иней взмаха ===
        // Бросок шкалу не меняет: копит в полёте и сразу тратит на свой иней, итог ноль
        public const float FrostPerStab = 0.057f;       // тычок: шкала растёт на столько (~18 тычков до полной)
        public const float FrostPerBraceHit = 0.029f;  // удар выставленным копьём (по цели, с кулдауном удара), бонус к разгону
        public const float FrostPerBraceTick = 0.0037f; // выставленное копьё: набор за тик на максимальном разгоне (полная шкала ~4.5 с), растёт с разгоном
        public const float FrostBraceMinSpeed = 0.05f; // разгон (0..1), ниже которого набора нет (стоя шкала не копится и таяние не откладывается)
        public const float FrostSpendPerSwing = 0.143f; // взмах оставляет иней и снимает столько шкалы (~7 взмахов с полной до нуля)
        public const float FrostStrongAt = 0.7f;       // шкала в начале взмаха, с которой иней сильный (Frostburn + Slow, другой вид)
        public const int FrostHoldTicks = 120;         // пауза после последнего набора до пассивного таяния (2 с)
        public const int FrostMeltTicks = 1680;        // пассивное таяние полной шкалы до нуля (28 с)
        public const float FrostDripChance = 0.05f;    // капля за тик при таянии на полной шкале (растёт со шкалой)

        public const int TapThresholdTicks = 18;   // ~300мс при 60 TPS — порог тап/зажатие
        public const int ThrowHoldTicks = 6;       // задержка клик → вылет (замах)
        // Поза броска: замах назад за ThrowHoldTicks, выброс вперёд при вылете, возврат руки
        public const int ThrowFollowTicks = 14;     // сколько тиков длится выброс + возврат после вылета
        private const float ThrowWindupBack = 2.4f; // рука в замахе: угол назад-вверх от линии броска (рад)

        // Режим приёма. Брошенность — свойство предмета (ThrownLockItem), не режима.
        public enum Mode { Swing, Stab, Spear }
        public Mode CurrentMode = Mode.Swing;

        public bool SpearMode => CurrentMode == Mode.Spear;
        public bool StabActive => CurrentMode == Mode.Stab;
        // Серп в руках брошен (лежит где-то в мире)
        public bool HeldThrown => HoldingSickle && IsLocked(Player.HeldItem);

        // Шкала заморозки (только у локального игрока) и пауза до таяния
        public float Frost;
        public float FrostShown;   // шкала для отрисовки: плавно догоняет Frost, чтобы вид не прыгал при тратах и наборе
        private const float FrostShownEase = 0.08f;
        private int frostHold;

        // Взмах ледяным копьём: возвращает шкалу в момент старта (по ней сила инея) и снимает FrostSpendPerSwing.
        // Паузу таяния не трогает: тает только то, что не потрачено
        public float SpendFrostForSwing()
        {
            if (Player.whoAmI != Main.myPlayer || Player.HeldItem.type != ItemID.IceSickle)
                return 0f;
            float level = Frost;
            Frost = Math.Max(0f, Frost - FrostSpendPerSwing);
            return level;
        }

        // Набор от тычка/выставленного копья: поднимает шкалу и откладывает таяние
        public void AddFrost(float amount)
        {
            if (Player.whoAmI != Main.myPlayer || Player.HeldItem.type != ItemID.IceSickle)
                return;
            Frost = MathHelper.Clamp(Frost + amount, 0f, 1f);
            frostHold = FrostHoldTicks;
        }

        private void UpdateFrost()
        {
            if (Player.dead)
            {
                Frost = 0f;
                frostHold = 0;
            }
            FrostShown += (Frost - FrostShown) * FrostShownEase;
            if (Math.Abs(Frost - FrostShown) < 0.002f)
                FrostShown = Frost;
            // Чужие рисовальщики (Weapon Out, ваниль в руке) видят замороженный спрайт по шкале
            SickleTextures.UpdateView(FrostShown);
            if (Frost <= 0f)
                return;
            if (frostHold > 0)
            {
                frostHold--;
                return;
            }
            Frost = Math.Max(0f, Frost - 1f / FrostMeltTicks);

            // Таяние: редкие капли и крошки льда с копья в руке (без свечения)
            if (Main.dedServ || Frost < 0.05f || Player.HeldItem.type != ItemID.IceSickle ||
                Main.rand.NextFloat() >= FrostDripChance * Frost)
                return;
            Vector2 at = Player.MountedCenter + new Vector2(Player.direction * 14f, -8f) + Main.rand.NextVector2Circular(8f, 10f);
            Dust d = Dust.NewDustPerfect(at, Main.rand.NextBool(3) ? DustID.Frost : DustID.SnowflakeIce,
                new Vector2(Main.rand.NextFloat(-0.3f, 0.3f), Main.rand.NextFloat(0.2f, 0.7f)), 90, default,
                Main.rand.NextFloat(0.55f, 0.9f));
            d.noGravity = false;
        }

        // Внутренние таймеры
        private int rmbHoldTicks;      // сколько тиков держится ПКМ
        private int stabCooldown;      // кулдаун между тычками
        private int throwHoldTicks;    // тиков с клика броска (>0 — замах идёт)
        private bool prevRmb, prevLmb;
        private bool rmbHeld;          // ПКМ зажата (после фильтра UI) — для CanUseItem
        private bool throwNeedsRelease; // после броска — отпусти кнопки, иначе при смене слота полетит следующая
        private int lastHeldType = -1;  // смена предмета в руке сбрасывает приём
        // Номера бросков, чьи снаряды ещё живы (пересобирается каждый тик)
        private readonly HashSet<int> activeThrowTokens = new();
        // Индексы своих снарядов — вместо перебора всех 1000 каждый тик
        private int spearProj = -1;
        private int stabProj = -1;
        // Направление, куда смотрят руки: НЕ живой курсор, а оружие
        private Vector2 armAimDir = Vector2.UnitX;
        // Поза броска (только локальный игрок)
        private int throwFollowTicks;                 // сколько тиков ещё идёт выброс/возврат
        private Vector2 throwAimDir = Vector2.UnitX;  // линия броска, зафиксированная при вылете
        // Серп в руке в замахе: рисует SickleThrowPoseLayer. Кисть — смещением от центра игрока
        public bool PoseItemVisible;
        public Vector2 PoseHandOffset;
        public Vector2 PoseItemDir = Vector2.UnitX;

        public bool HoldingSickle =>
            !Player.HeldItem.IsAir && SickleTypes.IsSickle(Player.HeldItem);

        // Каденс тычков = эффективный каденс свинга текущего оружия × стихия:
        // ледяной серп тычет чаще, серп смерти медленнее.
        private int StabInterval(Item held)
        {
            int swing = CombinedHooks.TotalAnimationTime(held.useAnimation, Player, held);
            return Math.Max(6, (int)Math.Round(swing * SickleTypes.StabTimeMult(held)));
        }

        public override void PreUpdate()
        {
            // Логика ввода — только для локального игрока
            if (Player.whoAmI != Main.myPlayer)
                return;

            if (throwFollowTicks > 0)
                throwFollowTicks--;

            // --- Какие брошенные серпы ещё лежат: подобранный снимает блок с предмета ---
            RefreshThrowTokens();
            HideFromWeaponOut();

            // --- Смена предмета в руке: старый приём исчезает сразу ---
            if (Player.HeldItem.type != lastHeldType)
            {
                lastHeldType = Player.HeldItem.type;
                KillStanceProjectiles();
                ResetToSwing();
            }

            // Клики по UI не должны запускать приёмы
            bool uiBlocked = Player.mouseInterface || Main.drawingPlayerChat || Main.blockMouse ||
                Main.mapFullscreen || Main.gamePaused || Player.cursorItemIconEnabled ||
                Main.SmartInteractShowingGenuine;
            bool rmb = Main.mouseRight && !uiBlocked;
            bool lmb = Main.mouseLeft && !uiBlocked;
            rmbHeld = rmb;

            // --- Сброс при смерти / смене оружия ---
            if (!HoldingSickle || Player.dead)
            {
                if (CurrentMode != Mode.Swing)
                    KillStanceProjectiles();
                ResetToSwing();
                prevRmb = rmb; prevLmb = lmb;
                return;
            }

            Item held = Player.HeldItem;
            // Ванильный взмах ещё идёт: копьё и тычки ждут его конца
            bool swinging = Player.itemAnimation > 0;

            // Курсор ушёл на UI посреди нажатия — отменяем незавершённый тап/бросок
            if (uiBlocked)
            {
                rmbHoldTicks = 0;
                throwHoldTicks = 0;
                stabCooldown = 0;
                if (CurrentMode == Mode.Stab)
                    CurrentMode = Mode.Swing;
                if (CurrentMode == Mode.Spear && !swinging)
                    EnsureSpearProjectile(held);
                prevRmb = rmb; prevLmb = lmb;
                return;
            }

            // --- Брошенным предметом пользоваться нельзя (другим серпом — можно) ---
            if (IsLocked(held))
            {
                if (CurrentMode != Mode.Swing)
                    KillStanceProjectiles();
                ResetToSwing();
                if (!(rmb && lmb))
                    throwNeedsRelease = false;
                prevRmb = rmb; prevLmb = lmb;
                return;
            }

            int stabInterval = StabInterval(held);

            // === БРОСОК: клик ЛКМ при зажатой ПКМ запускает бросок ===
            if (throwHoldTicks == 0 && rmb && lmb && !throwNeedsRelease)
            {
                throwHoldTicks = 1;
                rmbHoldTicks = 0;
                stabCooldown = 0;
                KillStanceProjectiles();
                CurrentMode = Mode.Swing;
            }
            else if (throwHoldTicks > 0)
                throwHoldTicks++;
            if (throwHoldTicks > 0)
            {
                if (throwHoldTicks >= ThrowHoldTicks)
                {
                    DoThrow(held);
                    throwHoldTicks = 0;
                }
                prevRmb = rmb; prevLmb = lmb;
                return;
            }
            if (!(rmb && lmb))
                throwNeedsRelease = false;

            // === ПКМ удерживается ===
            if (rmb)
            {
                rmbHoldTicks++;

                if (rmbHoldTicks >= TapThresholdTicks)
                {
                    // Зажатие — тычки. При входе фиксируем стартовый угол рук
                    // и сразу готовим первый тычок.
                    if (CurrentMode != Mode.Stab)
                    {
                        armAimDir = (Main.MouseWorld - Player.MountedCenter).SafeNormalize(Vector2.UnitX);
                        stabCooldown = stabInterval;
                    }
                    CurrentMode = Mode.Stab;

                    // Спавним тычок с интервалом = каденс оружия.
                    if (stabCooldown < stabInterval)
                        stabCooldown++;
                    if (stabCooldown >= stabInterval && !swinging)
                    {
                        SpawnStab(held, stabInterval);
                        stabCooldown = 0;
                    }
                }
            }
            else
            {
                // ПКМ отпущена
                if (rmbHoldTicks > 0 && rmbHoldTicks < TapThresholdTicks)
                {
                    // Быстрый тап — тумблер копья
                    CurrentMode = (CurrentMode == Mode.Spear) ? Mode.Swing : Mode.Spear;
                }
                rmbHoldTicks = 0;
                stabCooldown = 0;

                if (CurrentMode == Mode.Stab)
                    CurrentMode = Mode.Swing;
            }

            // === ЛКМ (взмах) снимает копьё и обрывает возврат тычка ===
            if (lmb)
            {
                if (CurrentMode == Mode.Spear)
                    CurrentMode = Mode.Swing;
                KillStanceProjectiles();
            }

            // === Копьё: спавним хват, если режим активен и взмах доиграл ===
            if (CurrentMode == Mode.Spear && !swinging)
                EnsureSpearProjectile(held);

            prevRmb = rmb;
            prevLmb = lmb;
        }

        public override void PostUpdate()
        {
            // Поза и разворот — только для локального игрока, именно PostUpdate:
            // ванилла каждый тик перебивает руки в PlayerFrame и разворот в движении.
            if (Player.whoAmI != Main.myPlayer)
                return;
            UpdateFrost();
            // Повторно: PostUpdate Weapon Out может идти раньше нашего PreUpdate
            HideFromWeaponOut();
            PoseItemVisible = false;
            SpinSwingVisible = false;
            if (!HoldingSickle || Player.dead)
                return;
            if (ApplyThrowPose())
                return;

            // Взмах закончился — сбрасываем захват старта для следующего взмаха
            if (Player.itemAnimation <= 0)
                spinStarted = false;

            // === ВЗМАХ: серп раскручивается 360° вокруг персонажа ===
            // Пока идёт ванильный взмах (itemAnimation > 0), прячем ванильный спрайт
            // серпа в руке (noUseGraphic-эффект через heldProj=-1) и крутим серп
            // по кругу сами. Ту же кривую (SpinCurve) берёт дуга урона — синхронно.
            if (Player.itemAnimation > 0 && CurrentMode == Mode.Swing && !IsLocked(Player.HeldItem))
            {
                ApplySpinSwing();
                return;
            }

            if (CurrentMode == Mode.Spear || CurrentMode == Mode.Stab)
            {
                // Руки следуют за ОРУЖИЕМ, а не за живым курсором
                Vector2 weaponDir = armAimDir;
                if (CurrentMode == Mode.Spear && TryGetSpearAngle(out float spearAngle))
                {
                    weaponDir = Vector2.UnitX.RotatedBy(spearAngle);
                    armAimDir = weaponDir;
                }

                // ВАЖНО: ChangeDir ПЕРВЫМ — при смене разворота ванилла сама
                // инвертирует compositeArm.rotation
                Player.ChangeDir(weaponDir.X >= 0 ? 1 : -1);

                // В режиме копья руку ставит сам SickleBraceProjectile — здесь только тычки
                if (CurrentMode == Mode.Stab)
                {
                    float armRot = weaponDir.ToRotation() - SickleStabProjectile.ArmLift * Player.direction - MathHelper.PiOver2;
                    Player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, armRot);
                    SickleStabProjectile.HideBackArm(Player, weaponDir);
                }
            }
        }

        private static float LerpAngle(float a, float b, float t) =>
            a + MathHelper.WrapAngle(b - a) * MathHelper.Clamp(t, 0f, 1f);

        // === Взмах-раскрутка: серп облетает персонажа на 360° ===
        // Время взмаха и прогресс считаем от ванильного itemAnimation, чтобы
        // старт/стоп совпадали с дугой урона (та же длительность ai[1]).
        // Слой SickleSpinSwingLayer рисует сам серп по этим полям.
        public bool SpinSwingVisible;
        public float SpinSwingAngle;     // мировой угол серпа (ось приклад→острие)
        public Vector2 SpinSwingCenter;  // центр вращения (центр игрока)

        // Радиус раскрутки: 0 = серп крутится ВОКРУГ ЦЕНТРА персонажа (рукоять
        // в центре тела, лезвие описывает круг). Больше значения — серп отлетает
        // от тела (крутится краем).
        public const float SpinRadius = 0f;

        // Захват старта взмаха: направление взгляда и стартовый угол фиксируем
        // ОДИН раз (в тик, когда itemAnimation появился), иначе:
        // 1) ChangeDir по углу руки каждый тик флипает спрайт — персонаж «крутится»;
        // 2) живой курсор каждый тик дёргает стартовый угол.
        private bool spinStarted;
        private int spinPrevAnim;
        private float spinFaceDir;
        // Сторона взмаха (+1 вправо / -1 влево), нужна слою для зеркала серпа
        public float SpinSwingFace => spinFaceDir >= 0f ? 1f : -1f;
        private float spinBaseAngle;

        private void ApplySpinSwing()
        {
            int max = Math.Max(1, Player.itemAnimationMax);

            // Старт взмаха: itemAnimation только что стал > 0. Фиксируем взгляд
            // и стартовый угол ОДИН раз на весь взмах.
            if (!spinStarted || Player.itemAnimation > spinPrevAnim)
            {
                spinStarted = true;
                spinFaceDir = Player.direction >= 0 ? 1f : -1f;
                spinBaseAngle = (Main.MouseWorld - Player.MountedCenter).ToRotation();
            }
            spinPrevAnim = Player.itemAnimation;

            // Прогресс 0..1 по взмаху (itemAnimation убывает от max к 0)
            float progress = 1f - Player.itemAnimation / (float)max;
            // Та же кривая, что у дуги: замах назад → круг → торможение
            float curve = SickleArcProjectile.SpinCurve(progress);

            // Взгляд зафиксирован на старте (НЕ меняется по кругу) — серп
            // раскручивается вокруг неподвижного тела, а не тянет его за собой.
            float angle = spinBaseAngle + spinFaceDir * MathHelper.TwoPi * curve;

            SpinSwingVisible = true;
            SpinSwingAngle = angle;
            SpinSwingCenter = Player.MountedCenter;

            // Направление тела ставим ОДИН раз (по зафиксированному взгляду),
            // а не каждый тик по углу руки — иначе спрайт флипается и «крутится».
            Player.ChangeDir(spinFaceDir >= 0f ? 1 : -1);

            // Рука держит древко. Древко проходит через центр персонажа, поэтому рука,
            // вытянутая вдоль древка, лежит на нём. Берём ту половину древка, что со
            // стороны взгляда, иначе рука смотрит назад. (Раньше при SpinRadius = 0
            // кисть целилась в сам центр и рука всегда торчала вправо по миру.)
            Vector2 shaftDir = Vector2.UnitX.RotatedBy(angle);
            Vector2 armDir;
            if (SpinRadius > 0f)
                armDir = (SpinSwingCenter + shaftDir * SpinRadius - Player.MountedCenter).SafeNormalize(shaftDir);
            else
                armDir = shaftDir.X * spinFaceDir >= 0f ? shaftDir : -shaftDir;
            float armRot = armDir.ToRotation() - MathHelper.PiOver2;
            Player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, armRot);
            SickleStabProjectile.HideBackArm(Player, armDir);

            // Прячем ванильный спрайт серпа в руке: heldProj=-1 + noUseGraphic.
            // Серп в руке рисует наш слой, а не ванилла.
            Player.heldProj = -1;
        }

        // Поза броска: замах (обе кнопки зажаты, серп в руке) → выброс вперёд
        // в момент вылета → возврат руки вниз.
        private bool ApplyThrowPose()
        {
            bool windup = throwHoldTicks > 0 && !throwNeedsRelease && !HeldThrown;
            if (!windup && throwFollowTicks <= 0)
                return false;

            Vector2 aim;
            float armWorld;
            Player.CompositeArmStretchAmount stretch;
            if (windup)
            {
                aim = (Main.MouseWorld - Player.MountedCenter).SafeNormalize(new Vector2(Player.direction, 0f));
                float p = Math.Min(1f, throwHoldTicks / (float)ThrowHoldTicks);
                int dirSign = aim.X >= 0 ? 1 : -1;
                float back = aim.ToRotation() - dirSign * ThrowWindupBack;
                armWorld = LerpAngle(MathHelper.PiOver2, back, p);
                stretch = p < 0.5f ? Player.CompositeArmStretchAmount.None : Player.CompositeArmStretchAmount.Quarter;
            }
            else
            {
                aim = throwAimDir;
                float t = 1f - throwFollowTicks / (float)ThrowFollowTicks;
                float back = aim.ToRotation() - (aim.X >= 0 ? 1 : -1) * ThrowWindupBack;
                if (t < 0.3f)         // выброс: от замаха к линии броска
                {
                    armWorld = LerpAngle(back, aim.ToRotation(), t / 0.3f);
                    stretch = Player.CompositeArmStretchAmount.ThreeQuarters;
                }
                else if (t < 0.55f)   // рука замерла вытянутой
                {
                    armWorld = aim.ToRotation();
                    stretch = Player.CompositeArmStretchAmount.Full;
                }
                else                  // возврат вниз
                {
                    armWorld = LerpAngle(aim.ToRotation(), MathHelper.PiOver2, (t - 0.55f) / 0.45f);
                    stretch = Player.CompositeArmStretchAmount.ThreeQuarters;
                }
            }

            Player.ChangeDir(aim.X >= 0 ? 1 : -1);
            float armRot = armWorld - MathHelper.PiOver2;
            Player.SetCompositeArmFront(true, stretch, armRot);
            SickleStabProjectile.HideBackArm(Player, armWorld.ToRotationVector2());

            if (windup)
            {
                PoseItemVisible = true;
                PoseItemDir = aim;
                PoseHandOffset = SickleSprite.HandGrip(Player, stretch, armRot) - Player.Center;
            }
            return true;
        }

        private bool IsMyProjectile(int index, int type)
        {
            if (index < 0 || index >= Main.maxProjectiles)
                return false;
            Projectile p = Main.projectile[index];
            return p.active && p.type == type && p.owner == Player.whoAmI;
        }

        // Угол зафиксированного копья из активного снаряда (ai[1] задаётся при спавне)
        private bool TryGetSpearAngle(out float angle)
        {
            if (IsMyProjectile(spearProj, ModContent.ProjectileType<SickleBraceProjectile>()))
            {
                angle = Main.projectile[spearProj].ai[1];
                return true;
            }
            angle = 0f;
            return false;
        }

        // Убрать копьё и тычок немедленно (у владельца; Kill синхронизируется)
        private void KillStanceProjectiles()
        {
            if (IsMyProjectile(spearProj, ModContent.ProjectileType<SickleBraceProjectile>()))
                Main.projectile[spearProj].Kill();
            if (IsMyProjectile(stabProj, ModContent.ProjectileType<SickleStabProjectile>()))
                Main.projectile[stabProj].Kill();
            spearProj = -1;
            stabProj = -1;
        }

        // Номера живых бросков этого игрока. Только у владельца.
        private void RefreshThrowTokens()
        {
            activeThrowTokens.Clear();
            int throwType = ModContent.ProjectileType<SickleThrowProjectile>();
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.type == throwType && p.owner == Player.whoAmI &&
                    p.ModProjectile is SickleThrowProjectile sickle && sickle.Token != 0)
                    activeThrowTokens.Add(sickle.Token);
            }
        }

        // Мод Weapon Out Lite рисует предмет в руках у персонажа. Прячем его, когда серп
        // рисуем мы сами (копьё, тычки, замах и выброс броска), иначе в кадре два серпа,
        // и когда серп брошен (предмет-призрак в слоте висел бы за спиной).
        // Во взмахе (itemAnimation > 0) Weapon Out прячет предмет сам.
        private void HideFromWeaponOut()
        {
            if (Main.dedServ || !HoldingSickle)
                return;
            bool selfDrawn = IsLocked(Player.HeldItem) || CurrentMode != Mode.Swing ||
                throwHoldTicks > 0 || throwFollowTicks > 0;
            if (!selfDrawn)
                return;
            if (!ModLoader.TryGetMod("WeaponOutLite", out Mod weaponOut))
                return;
            try
            {
                weaponOut.Call("HidePlayerHeldItem", Player.whoAmI);
            }
            catch (Exception)
            {
                // Другая версия мода без такой команды — просто не прячем
            }
        }

        // Этот экземпляр предмета брошен и ещё не подобран
        public bool IsLocked(Item item)
        {
            if (item == null || item.IsAir || !item.TryGetGlobalItem(out ThrownLockItem lockItem))
                return false;
            return lockItem.Token != 0 && activeThrowTokens.Contains(lockItem.Token);
        }

        private void ResetToSwing()
        {
            CurrentMode = Mode.Swing;
            rmbHoldTicks = 0;
            stabCooldown = 0;
            throwHoldTicks = 0;
        }

        // Мёртвая зона по X для направления тычка (доля единичного вектора)
        private const float StabSideDeadzone = 0.1f;

        private void SpawnStab(Item held, int duration)
        {
            Vector2 dir = (Main.MouseWorld - Player.MountedCenter).SafeNormalize(Vector2.UnitX);
            // Курсор почти строго над/под игроком: держим текущую сторону
            if (Math.Abs(dir.X) < StabSideDeadzone)
                dir = new Vector2(StabSideDeadzone * Player.direction, dir.Y).SafeNormalize(Vector2.UnitX);
            armAimDir = dir;
            AddFrost(FrostPerStab);
            int damage = Math.Max(1, (int)(Player.GetWeaponDamage(held) * SickleTypes.StabDamageMult(held)));
            stabProj = Projectile.NewProjectile(
                Player.GetSource_ItemUse(held),
                Player.MountedCenter,
                dir * 8f,
                ModContent.ProjectileType<SickleStabProjectile>(),
                damage,
                held.knockBack * SickleTypes.StabKnockbackMult(held),
                Player.whoAmI,
                duration, 0f, held.type);
        }

        // Синус угла (~20°), ниже которого копьё выставляется строго горизонтально
        private const float SpearSnapSin = 0.34f;

        private void EnsureSpearProjectile(Item held)
        {
            // Одно копьё на игрока — если уже есть, не спавним
            int spearType = ModContent.ProjectileType<SickleBraceProjectile>();
            if (IsMyProjectile(spearProj, spearType))
                return;

            // Угол фиксируется ОДИН раз и передаётся в ai[1] сразу при спавне
            Vector2 dir = (Main.MouseWorld - Player.MountedCenter).SafeNormalize(Vector2.UnitX);
            if (Math.Abs(dir.Y) < SpearSnapSin)
                dir = new Vector2(dir.X >= 0 ? 1f : -1f, 0f);
            int damage = Math.Max(1, (int)(Player.GetWeaponDamage(held) * SickleTypes.SpearDamageMult(held)));
            spearProj = Projectile.NewProjectile(
                Player.GetSource_ItemUse(held),
                Player.MountedCenter + dir * 18f,
                Vector2.Zero,
                spearType,
                damage,
                held.knockBack,
                Player.whoAmI,
                0f, dir.ToRotation(), held.type);
        }

        private void DoThrow(Item held)
        {
            if (!held.TryGetGlobalItem(out ThrownLockItem lockItem))
                return;

            KillStanceProjectiles();
            Vector2 dir = (Main.MouseWorld - Player.MountedCenter).SafeNormalize(Vector2.UnitX);
            // Стоишь вплотную к стене — серп вылетает из центра, а не застревает в блоке
            Vector2 spawn = Player.MountedCenter + dir * 16f;
            if (!Collision.CanHit(Player.MountedCenter, 1, 1, spawn, 1, 1))
                spawn = Player.MountedCenter;
            int index = Projectile.NewProjectile(
                Player.GetSource_ItemUse(held),
                spawn,
                dir * SickleThrowProjectile.ThrowSpeed,
                ModContent.ProjectileType<SickleThrowProjectile>(),
                Player.GetWeaponDamage(held),
                held.knockBack,
                Player.whoAmI,
                -1f, 0f, held.type);
            if (index < 0 || index >= Main.maxProjectiles ||
                Main.projectile[index].ModProjectile is not SickleThrowProjectile sickle)
                return;

            // Предмет остаётся в инвентаре, но этот экземпляр заблокирован
            int token = Main.rand.Next(1, int.MaxValue);
            sickle.Token = token;
            lockItem.Token = token;
            activeThrowTokens.Add(token);

            throwNeedsRelease = true;
            throwFollowTicks = ThrowFollowTicks;
            throwAimDir = dir;
            ResetToSwing();
            SoundEngine.PlaySound(SoundID.Item1 with { Pitch = -0.3f }, Player.Center);
        }

        public override bool CanUseItem(Item item)
        {
            // Заблокированный (брошенный) предмет использовать нельзя
            if (IsLocked(item))
                return false;

            if (Player.whoAmI == Main.myPlayer && SickleTypes.IsSickle(item))
            {
                // Пока зажата ПКМ, ЛКМ означает бросок, а не взмах
                if (rmbHeld || throwHoldTicks > 0)
                    return false;
                // В копье/тычке взмаха нет: два приёма сразу не рисуем
                if (CurrentMode != Mode.Swing)
                    return false;
            }

            return base.CanUseItem(item);
        }
    }
}
