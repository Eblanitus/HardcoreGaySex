using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using FrostMelee.Content.Items;
using FrostMelee.Content.Projectiles;

namespace FrostMelee.Content.Weapons
{
    // Мозг приёмов ледяных мечей (Ice Blade, Frostbrand) по МЕЧИ.md.
    // Набор ввода как у серпа (SicklePlayer — копия без шкалы заморозки):
    // ЛКМ зажать — взмах (ванильный, дугу спавнит SwordGlobalItem → SwordArcProjectile)
    // ПКМ тап — тумблер «встречи» (меч выставлен, урон от скорости сближения)
    // ПКМ зажать >порога — тычки остриём
    // ПКМ зажата + клик ЛКМ — бросок (меч остаётся в мире, забирать самому)
    // Режимы не перекрываются: взмах, встреча и тычок в кадре только по одному.
    // Тычки/встреча/бросок рисуют и бьют универсальные снаряды серпа
    // (SickleStabProjectile / SickleBraceProjectile / SickleThrowProjectile):
    // они рисуют любой предмет по ai[2] (SickleSprite меряет спрайт по пикселям),
    // поэтому для мечей их не дублируем — только реестр множителей в SwordTypes.
    public class SwordPlayer : ModPlayer
    {
        public const int TapThresholdTicks = 18;   // ~300мс при 60 TPS — порог тап/зажатие
        public const int ThrowHoldTicks = 6;       // задержка клик → вылет (замах)
        public const int ThrowFollowTicks = 14;    // сколько тиков длится выброс + возврат после вылета
        private const float ThrowWindupBack = 2.4f; // рука в замахе: угол назад-вверх от линии броска (рад)
        // Сколько рукояти торчит позади кисти — общая цифра для позы замаха
        // (SwordThrowPoseLayer) и для огня на клинке (TryGetBladeLine)
        public const float ThrowGripInset = 4f;

        // Режим приёма. Брошенность — свойство предмета (ThrownLockItem), не режима.
        public enum Mode { Swing, Stab, Spear } // Spear = «встреча» меча
        public Mode CurrentMode = Mode.Swing;

        public bool SpearMode => CurrentMode == Mode.Spear;
        public bool StabActive => CurrentMode == Mode.Stab;
        // Меч в руках брошен (лежит где-то в мире)
        public bool HeldThrown => HoldingSword && IsLocked(Player.HeldItem);

        // Внутренние таймеры
        private int rmbHoldTicks;      // сколько тиков держится ПКМ
        private int stabCooldown;      // кулдаун между тычками
        private int throwHoldTicks;    // тиков с клика броска (>0 — замах идёт)
        private bool prevRmb, prevLmb;
        private bool rmbHeld;          // ПКМ зажата (после фильтра UI) — для CanUseItem
        private bool throwNeedsRelease; // после броска — отпусти кнопки, иначе при смене слота полетит следующий
        private int lastHeldType = -1;  // смена предмета в руке сбрасывает приём
        // Номера бросков, чьи снаряды ещё живы (пересобирается каждый тик)
        private readonly HashSet<int> activeThrowTokens = new HashSet<int>();
        // Индексы своих снарядов — вместо перебора всех 1000 каждый тик
        private int spearProj = -1;
        private int stabProj = -1;
        // Направление, куда смотрят руки: НЕ живой курсор, а оружие
        private Vector2 armAimDir = Vector2.UnitX;
        // Поза броска (только локальный игрок)
        private int throwFollowTicks;                 // сколько тиков ещё идёт выброс/возврат
        private Vector2 throwAimDir = Vector2.UnitX;  // линия броска, зафиксированная при вылете
        // Меч в руке в замахе: рисует SwordThrowPoseLayer. Кисть — смещением от центра игрока
        public bool PoseItemVisible;
        public Vector2 PoseHandOffset;
        public Vector2 PoseItemDir = Vector2.UnitX;

        // === Ледяной огонь на клинке Frostbrand ===
        // Горит там, где меч нарисован СЕЙЧАС: позу отдаёт TryGetBladeLine (замах,
        // тычок, встреча, взмах, стойка). Брошенный меч не горит — в руке пусто.
        // Во взмахе пыль по клинку уже сыплет дуга, поэтому здесь мы молчим.

        public bool HoldingSword =>
            !Player.HeldItem.IsAir && SwordTypes.IsSword(Player.HeldItem);

        // Каденс тычков = эффективный каденс свинга × стихия (у меча резкие тычки)
        private int StabInterval(Item held)
        {
            int swing = CombinedHooks.TotalAnimationTime(held.useAnimation, Player, held);
            return Math.Max(6, (int)Math.Round(swing * SwordTypes.StabTimeMult(held)));
        }

        public override void PreUpdate()
        {
            // Логика ввода — только для локального игрока
            if (Player.whoAmI != Main.myPlayer)
                return;

            if (throwFollowTicks > 0)
                throwFollowTicks--;

            // --- Какие брошенные мечи ещё лежат: подобранный снимает блок с предмета ---
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
            if (!HoldingSword || Player.dead)
            {
                if (CurrentMode != Mode.Swing)
                    KillStanceProjectiles();
                ResetToSwing();
                prevRmb = rmb; prevLmb = lmb;
                return;
            }

            Item held = Player.HeldItem;
            // Ванильный взмах ещё идёт: встреча и тычки ждут его конца
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

            // --- Брошенным предметом пользоваться нельзя (другим мечом — можно) ---
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
                    // Быстрый тап — тумблер «встречи»
                    CurrentMode = (CurrentMode == Mode.Spear) ? Mode.Swing : Mode.Spear;
                }
                rmbHoldTicks = 0;
                stabCooldown = 0;

                if (CurrentMode == Mode.Stab)
                    CurrentMode = Mode.Swing;
            }

            // === ЛКМ (взмах) снимает встречу и обрывает возврат тычка ===
            if (lmb)
            {
                if (CurrentMode == Mode.Spear)
                    CurrentMode = Mode.Swing;
                KillStanceProjectiles();
            }

            // === Встреча: спавним хват, если режим активен и взмах доиграл ===
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
            // Повторно: PostUpdate Weapon Out может идти раньше нашего PreUpdate
            HideFromWeaponOut();
            PoseItemVisible = false;
            if (!HoldingSword || Player.dead)
                return;
            if (ApplyThrowPose())
                return;

            if (CurrentMode == Mode.Spear || CurrentMode == Mode.Stab)
            {
                // Руки следуют за ОРУЖИЕМ, а не за живым курсором
                Vector2 weaponDir = armAimDir;
                if (CurrentMode == Mode.Spear && TryGetSpearAngle(out float meetAngle))
                {
                    weaponDir = Vector2.UnitX.RotatedBy(meetAngle);
                    armAimDir = weaponDir;
                }

                // ВАЖНО: ChangeDir ПЕРВЫМ — при смене разворота ванилла сама
                // инвертирует compositeArm.rotation
                Player.ChangeDir(weaponDir.X >= 0 ? 1 : -1);

                // В режиме встречи руку ставит сам SickleBraceProjectile — здесь только тычки
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

        // === Реальная линия клинка (приклад → остриё) в мире ===
        // Нужна всему, что рисует огонь Frostbrand вне взмаха: пламя должно лежать
        // на клинке, а не у пояса. Позу берём у того, кто её реально держит:
        // замах броска → тычок → встреча → взмах → стойка (Weapon Out Lite).
        // false — меча в руке нет, он брошен, или рука уже пустая (выброс броска):
        // гореть нечему.
        // Линию отдал снаряд (тычок/встреча): он рисует меч сам и сам рисует наледь —
        // слою игрока в этот момент рисовать не надо, иначе лёд ляжет дважды
        public bool BladeLineIsProjectile;

        public bool TryGetBladeLine(out Vector2 butt, out Vector2 tip)
        {
            butt = tip = default;
            BladeLineIsProjectile = false;
            if (!HoldingSword || Player.dead)
                return false;
            // Брошенный экземпляр лежит в мире — в руке пусто
            if (HeldThrown)
                return false;

            SickleSprite.Geometry geo = SickleSprite.Get(Player.HeldItem.type);
            if (geo.Length <= 1f)
                return false;

            // 1. Замах броска: позу и кисть задал ApplyThrowPose, слой рисует от кисти
            if (PoseItemVisible)
            {
                Vector2 dir = PoseItemDir;
                butt = Player.Center + PoseHandOffset - dir * ThrowGripInset;
                tip = butt + dir * geo.Length;
                return true;
            }
            // 2. Выброс/возврат руки после броска: меч уже летит снарядом
            if (throwFollowTicks > 0)
                return false;

            // 3. Тычок: снаряд держит меч у кисти и знает своё направление
            if (TryGetStanceBladeLine(ModContent.ProjectileType<SwordStabProjectile>(), out butt, out tip))
            {
                BladeLineIsProjectile = true;
                return true;
            }
            // 4. Встреча: выставленный меч
            if (TryGetStanceBladeLine(ModContent.ProjectileType<SickleBraceProjectile>(), out butt, out tip))
            {
                BladeLineIsProjectile = true;
                return true;
            }
            // 5. Взмах: настоящая ванильная поза (itemLocation/itemRotation)
            if (Player.itemAnimation > 0 && SwordArcProjectile.GetBladeLine(Player, out butt, out tip))
                return true;

            // 6. Стойка: меч рисует Weapon Out Lite, его координат у нас нет — оценка
            float scale = Player.GetAdjustedItemScale(Player.HeldItem);
            Vector2 heldDir = new Vector2(Player.direction, -0.95f).SafeNormalize(Vector2.UnitX);
            butt = Player.Center + new Vector2(3f * Player.direction, 8f);
            tip = butt + heldDir * (geo.Length * scale);
            return true;
        }

        // Линия клинка выставленного снаряда (тычок/встреча): он ставит Center
        // ровно на середину оси приклад→остриё, длина — геометрия спрайта, scale 1.
        private bool TryGetStanceBladeLine(int projType, out Vector2 butt, out Vector2 tip)
        {
            butt = tip = default;
            // O(1): таких снарядов у игрока нет — полный перебор не нужен. Без этой
            // проверки огонь Frostbrand сканировал бы 1000 снарядов каждый тик.
            if (Player.ownedProjectileCounts[projType] <= 0)
                return false;
            // У локального игрока индекс уже есть; у чужого (мультиплеер) — ищем свой
            int index = projType == ModContent.ProjectileType<SickleBraceProjectile>() ? spearProj : stabProj;
            if (!IsMyProjectile(index, projType))
            {
                index = -1;
                for (int i = 0; i < Main.maxProjectiles; i++)
                {
                    Projectile p = Main.projectile[i];
                    if (p.active && p.type == projType && p.owner == Player.whoAmI)
                    {
                        index = i;
                        break;
                    }
                }
                if (index < 0)
                    return false;
            }

            Projectile proj = Main.projectile[index];
            // Тычок задаёт направление скоростью, встреча — углом ai[1] (фиксируется при спавне)
            Vector2 dir = projType == ModContent.ProjectileType<SwordStabProjectile>()
                ? proj.velocity.SafeNormalize(Vector2.UnitX)
                : Vector2.UnitX.RotatedBy(proj.ai[1]);
            float half = SickleSprite.Get((int)proj.ai[2]).Length * 0.5f;
            butt = proj.Center - dir * half;
            tip = proj.Center + dir * half;
            return true;
        }

        // Поза броска: замах (обе кнопки зажаты, меч в руке) → выброс вперёд
        // в момент вылета → возврат руки вниз. Копия SicklePlayer.ApplyThrowPose.
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

        // Угол выставленного меча из активного снаряда (ai[1] задаётся при спавне)
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

        // Убрать встречу и тычок немедленно (у владельца; Kill синхронизируется)
        private void KillStanceProjectiles()
        {
            if (IsMyProjectile(spearProj, ModContent.ProjectileType<SickleBraceProjectile>()))
                Main.projectile[spearProj].Kill();
            if (IsMyProjectile(stabProj, ModContent.ProjectileType<SwordStabProjectile>()))
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
                    p.ModProjectile is SickleThrowProjectile thrown && thrown.Token != 0)
                    activeThrowTokens.Add(thrown.Token);
            }
        }

        // Мод Weapon Out Lite рисует предмет в руках у персонажа. Прячем его, когда
        // меч рисуем мы сами (тычки, встреча, замах и выброс броска), иначе в кадре
        // два меча, и когда меч брошен (предмет-призрак в слоте висел бы за спиной).
        // Во взмахе (itemAnimation > 0) Weapon Out прячет предмет сам.
        private void HideFromWeaponOut()
        {
            if (Main.dedServ || !HoldingSword)
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
            int damage = Math.Max(1, (int)(Player.GetWeaponDamage(held) * SwordTypes.StabDamageMult(held)));
            stabProj = Projectile.NewProjectile(
                Player.GetSource_ItemUse(held),
                Player.MountedCenter,
                dir * 8f,
                ModContent.ProjectileType<SwordStabProjectile>(),
                damage,
                held.knockBack * SwordTypes.StabKnockbackMult(held),
                Player.whoAmI,
                duration, 0f, held.type);
        }

        // Синус угла (~20°), ниже которого меч выставляется строго горизонтально
        private const float SpearSnapSin = 0.34f;

        private void EnsureSpearProjectile(Item held)
        {
            // Один выставленный меч на игрока — если уже есть, не спавним
            int meetType = ModContent.ProjectileType<SickleBraceProjectile>();
            if (IsMyProjectile(spearProj, meetType))
                return;

            // Угол фиксируется ОДИН раз и передаётся в ai[1] сразу при спавне
            Vector2 dir = (Main.MouseWorld - Player.MountedCenter).SafeNormalize(Vector2.UnitX);
            if (Math.Abs(dir.Y) < SpearSnapSin)
                dir = new Vector2(dir.X >= 0 ? 1f : -1f, 0f);
            // Урон встречи: слабее взмаха за секунду, но растёт со скоростью сближения
            // (внутри снаряда ×0.4..×2.5 от скорости — см. SickleBraceProjectile)
            int damage = Math.Max(1, (int)(Player.GetWeaponDamage(held) * SwordTypes.SpearDamageMult(held)));
            spearProj = Projectile.NewProjectile(
                Player.GetSource_ItemUse(held),
                Player.MountedCenter + dir * 18f,
                Vector2.Zero,
                meetType,
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
            // Стоишь вплотную к стене — меч вылетает из центра, а не застревает в блоке
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
                Main.projectile[index].ModProjectile is not SickleThrowProjectile thrown)
                return;

            // Предмет остаётся в инвентаре, но этот экземпляр заблокирован
            int token = Main.rand.Next(1, int.MaxValue);
            thrown.Token = token;
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

            if (Player.whoAmI == Main.myPlayer && SwordTypes.IsSword(item))
            {
                // Пока зажата ПКМ, ЛКМ означает бросок, а не взмах
                if (rmbHeld || throwHoldTicks > 0)
                    return false;
                // В встрече/тычке взмаха нет: два приёма сразу не рисуем
                if (CurrentMode != Mode.Swing)
                    return false;
            }

            return base.CanUseItem(item);
        }
    }
}
