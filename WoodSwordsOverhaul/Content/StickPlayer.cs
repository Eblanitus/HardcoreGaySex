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
using WoodSwordsOverhaul.Content.Items;
using WoodSwordsOverhaul.Content.Projectiles;

namespace WoodSwordsOverhaul.Content
{
    // Мозг системы приёмов палки (см. ФИЛОСОФИЯ.md):
    // ЛКМ зажать — взмах (ванильный, дугу спавнит WoodSwordsGlobalItem)
    // ПКМ тап — тумблер копья
    // ПКМ зажать >порога — тычки кинжалом
    // ПКМ зажата + клик ЛКМ — бросок (замах играет после клика)
    // Режимы не перекрываются: взмах, копьё и тычок в кадре только по одному.
    public class StickPlayer : ModPlayer
    {
        public const int TapThresholdTicks = 18;   // ~300мс при 60 TPS — порог тап/зажатие
        public const int ThrowHoldTicks = 6;       // задержка клик → вылет (замах)
        // Кактусовая палка во взмахе: ванильный спрайт висит дальше руки, сдвигаем его
        // вдоль клинка к рукояти на это число пикселей (подстройка: 1–3 px)
        public const float CactusSwingHandInset = 2f;
        // Поза броска: замах назад за ThrowHoldTicks, выброс вперёд при вылете, возврат руки
        public const int ThrowFollowTicks = 14;     // сколько тиков длится выброс + возврат после вылета
        private const float ThrowWindupBack = 2.4f; // рука в замахе: угол назад-вверх от линии броска (рад)
        // Темп тычков НЕ константа: считается от каденса свинга оружия
        // (со всеми бонусами скорости) и стихии оружия — см. StabInterval.

        // Режим приёма. Брошенность — свойство предмета (ThrownLockItem), не режима.
        public enum Mode { Swing, Dagger, Spear }
        public Mode CurrentMode = Mode.Swing;

        public bool SpearMode => CurrentMode == Mode.Spear;
        public bool DaggerActive => CurrentMode == Mode.Dagger;
        // Палка в руках брошена (лежит где-то в мире)
        public bool HeldThrown => HoldingStick && IsLocked(Player.HeldItem);

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
        // (копьё статично, тычки резкие — иначе руки плавают отдельно)
        private Vector2 armAimDir = Vector2.UnitX;
        // Поза броска (только локальный игрок)
        private int throwFollowTicks;                 // сколько тиков ещё идёт выброс/возврат
        private Vector2 throwAimDir = Vector2.UnitX;  // линия броска, зафиксированная при вылете
        // Палка в руке в замахе: рисует StickThrowPoseLayer. Кисть — смещением от центра игрока
        public bool PoseItemVisible;
        public Vector2 PoseHandOffset;
        public Vector2 PoseItemDir = Vector2.UnitX;

        public bool HoldingStick =>
            !Player.HeldItem.IsAir && StickTypes.IsWoodSword(Player.HeldItem);

        // Каденс тычков = эффективный каденс свинга текущего оружия (как ванилла
        // считает itemAnimationMax: со всеми бонусами скорости), умноженный на стихию:
        // заточка тычет чаще, палка реже.
        private int StabInterval(Item held)
        {
            int swing = CombinedHooks.TotalAnimationTime(held.useAnimation, Player, held);
            return Math.Max(6, (int)Math.Round(swing * StickTypes.StabTimeMult(held)));
        }

        public override void PreUpdate()
        {
            // Логика ввода — только для локального игрока
            if (Player.whoAmI != Main.myPlayer)
                return;

            if (throwFollowTicks > 0)
                throwFollowTicks--;

            // --- Какие брошенные палки ещё лежат: подобранная снимает блок с предмета ---
            RefreshThrowTokens();
            HideThrownFromWeaponOut();

            // --- Смена предмета в руке: старый приём (копьё/тычок) исчезает сразу,
            // а не доигрывает поверх нового оружия ---
            if (Player.HeldItem.type != lastHeldType)
            {
                lastHeldType = Player.HeldItem.type;
                KillStanceProjectiles();
                ResetToSwing();
            }

            // Клики по UI не должны запускать приёмы: курсор над инвентарём/интерфейсом,
            // открыт чат или карта, курсор над дверью/сундуком (иконка взаимодействия).
            bool uiBlocked = Player.mouseInterface || Main.drawingPlayerChat || Main.blockMouse ||
                Main.mapFullscreen || Main.gamePaused || Player.cursorItemIconEnabled ||
                Main.SmartInteractShowingGenuine;
            bool rmb = Main.mouseRight && !uiBlocked;
            bool lmb = Main.mouseLeft && !uiBlocked;
            rmbHeld = rmb;

            // --- Сброс при смерти / смене оружия ---
            if (!HoldingStick || Player.dead)
            {
                if (CurrentMode != Mode.Swing)
                    KillStanceProjectiles();
                ResetToSwing();
                prevRmb = rmb; prevLmb = lmb;
                return;
            }

            Item held = Player.HeldItem;
            // Ванильный взмах ещё идёт: копьё и тычки ждут его конца, чтобы
            // в кадре не было двух палок сразу (взмах + выдвигающееся копьё)
            bool swinging = Player.itemAnimation > 0;

            // Курсор ушёл на UI посреди нажатия — отменяем незавершённый тап/бросок,
            // чтобы отпускание ПКМ над инвентарём не переключило копьё
            if (uiBlocked)
            {
                rmbHoldTicks = 0;
                throwHoldTicks = 0;
                stabCooldown = 0;
                if (CurrentMode == Mode.Dagger)
                    CurrentMode = Mode.Swing;
                if (CurrentMode == Mode.Spear && !swinging)
                    EnsureSpearProjectile(held);
                prevRmb = rmb; prevLmb = lmb;
                return;
            }

            // --- Брошенным предметом пользоваться нельзя (другой палкой — можно) ---
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
            // Достаточно нажать (не держать): замах доигрывается сам ThrowHoldTicks тиков
            // даже если кнопки уже отпущены, затем вылет по курсору в момент вылета.
            if (throwHoldTicks == 0 && rmb && lmb && !throwNeedsRelease)
            {
                throwHoldTicks = 1;
                rmbHoldTicks = 0;
                stabCooldown = 0;
                KillStanceProjectiles(); // копьё/тычок не торчат поверх позы замаха
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
                    // Зажатие — кинжал (тычки). При входе фиксируем стартовый
                    // угол рук и сразу готовим первый тычок (без лишнего ожидания).
                    if (CurrentMode != Mode.Dagger)
                    {
                        armAimDir = (Main.MouseWorld - Player.MountedCenter).SafeNormalize(Vector2.UnitX);
                        stabCooldown = stabInterval;
                    }
                    CurrentMode = Mode.Dagger;

                    // Спавним тычок с интервалом = каденс оружия.
                    // Пока доигрывает взмах — ждём (первый тычок сразу после него).
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

                if (CurrentMode == Mode.Dagger)
                    CurrentMode = Mode.Swing;
            }

            // === ЛКМ (взмах) снимает копьё и обрывает возврат тычка ===
            // Именно здесь (до ItemCheck этого же тика): снаряды убраны раньше,
            // чем ванилла начнёт взмах — в кадре сразу только взмах.
            // Не только по нажатию: ЛКМ, зажатая во время тапа ПКМ, тоже снимает копьё.
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
            // Поза и разворот — только для локального игрока.
            // ВАЖНО: именно PostUpdate, а не PreUpdate: ванилла каждый тик
            // перебивает руки в PlayerFrame/ItemCheck_ApplyHoldStyle и разворот
            // в коде движения — всё, что ставим в PreUpdate, сносится до отрисовки.
            // Здесь мы последние перед отрисовкой. Вне режимов руки не трогаем:
            // ванилла сама вернёт позу held-предмета/свинга.
            if (Player.whoAmI != Main.myPlayer)
                return;
            // Повторно: PostUpdate Weapon Out может идти раньше нашего PreUpdate-вызова
            HideThrownFromWeaponOut();
            PoseItemVisible = false;
            if (!HoldingStick || Player.dead)
                return;
            if (ApplyThrowPose())
                return;
            // Кактусовая палка во взмахе: сдвиг ванильного спрайта к руке. Только взмах
            // и только кактус — тычок, копьё, заточки и остальные палки не трогаем.
            if (Player.itemAnimation > 0)
            {
                if (CurrentMode == Mode.Swing && Player.HeldItem.type == ItemID.CactusSword)
                    Player.itemLocation -= CactusSwingBladeDir() * CactusSwingHandInset;
                return;
            }

            if (CurrentMode == Mode.Spear || CurrentMode == Mode.Dagger)
            {
                // Руки следуют за ОРУЖИЕМ, а не за живым курсором: копьё статично
                // (берём зафиксированный угол из активного снаряда), тычки резкие
                // (угол последнего тычка из armAimDir). Иначе руки плавают отдельно.
                // Как у турнирного копья из ExampleMod: угол берётся от оружия.
                Vector2 weaponDir = armAimDir;
                if (CurrentMode == Mode.Spear && TryGetSpearAngle(out float spearAngle))
                {
                    weaponDir = Vector2.UnitX.RotatedBy(spearAngle);
                    armAimDir = weaponDir;
                }

                // Разворот зафиксирован за направлением оружия: бег не разворачивает
                // персонажа (как ванильное копьё Jousting Lance).
                // ВАЖНО: ChangeDir ПЕРВЫМ — при смене разворота ванилла сама делает
                // compositeArm.rotation *= -1. Если ставить руки до него, инверсия
                // применится поверх наших (двойное зеркало — руки за спиной влево).
                Player.ChangeDir(weaponDir.X >= 0 ? 1 : -1);

                // Руки вперёд в сторону оружия (обе руки на палке).
                // Формула: мировой угол минус π/2 (ноль руки смотрит вниз),
                // БЕЗ ручного зеркалирования: отрисовка (DrawPlayer_28) берёт угол
                // как есть, флип по развороту зеркалит только текстуру. Ручное
                // зеркалирование давало двойной флип влево — руки за спиной.
                // В режиме копья руку ставит сам SpearBraceProjectile (хват одной рукой,
                // вторая у тела, как у турнирного копья) — здесь только тычки.
                if (CurrentMode == Mode.Dagger)
                {
                    float armRot = weaponDir.ToRotation() - DaggerStabProjectile.ArmLift * Player.direction - MathHelper.PiOver2;
                    Player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, armRot);
                    // Работает только передняя рука с палкой, вторая — как у ванильных копий
                    DaggerStabProjectile.HideBackArm(Player, weaponDir);
                }
            }
        }

        private static float LerpAngle(float a, float b, float t) =>
            a + MathHelper.WrapAngle(b - a) * MathHelper.Clamp(t, 0f, 1f);

        // Поза броска: замах (обе кнопки зажаты, палка в руке) → быстрый выброс вперёд
        // в момент вылета (палка из руки исчезла) → возврат руки вниз. Руку и кадр тела
        // ставим как в тычке; вторая рука спрятана (HideBackArm). Возвращает true, если поза активна.
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
                float t = 1f - throwFollowTicks / (float)ThrowFollowTicks; // 0..1
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

            // ChangeDir ПЕРВЫМ (см. комментарий в PostUpdate про инверсию руки)
            Player.ChangeDir(aim.X >= 0 ? 1 : -1);
            float armRot = armWorld - MathHelper.PiOver2;
            Player.SetCompositeArmFront(true, stretch, armRot);
            DaggerStabProjectile.HideBackArm(Player, armWorld.ToRotationVector2());

            if (windup)
            {
                PoseItemVisible = true;
                PoseItemDir = aim;
                PoseHandOffset = StickSprite.HandGrip(Player, stretch, armRot) - Player.Center;
            }
            return true;
        }

        // Направление клинка (от приклада к острию) в мире во время взмаха кактусовой палки.
        // Геометрия спрайта — по пикселям (StickSprite); зеркалим при direction = -1 и
        // поворачиваем на угол взмаха предмета.
        private Vector2 CactusSwingBladeDir()
        {
            StickSprite.Geometry geo = StickSprite.Get(ItemID.CactusSword);
            Vector2 blade = (geo.Tip - geo.Butt).SafeNormalize(new Vector2(1f, -1f));
            blade.X *= Player.direction;
            return blade.RotatedBy(Player.itemRotation).SafeNormalize(Vector2.UnitX);
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
            if (IsMyProjectile(spearProj, ModContent.ProjectileType<SpearBraceProjectile>()))
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
            if (IsMyProjectile(spearProj, ModContent.ProjectileType<SpearBraceProjectile>()))
                Main.projectile[spearProj].Kill();
            if (IsMyProjectile(stabProj, ModContent.ProjectileType<DaggerStabProjectile>()))
                Main.projectile[stabProj].Kill();
            spearProj = -1;
            stabProj = -1;
        }

        // Номера живых бросков этого игрока. Только у владельца: Token снаряда
        // задаётся локально при броске, у остальных клиентов он 0.
        private void RefreshThrowTokens()
        {
            activeThrowTokens.Clear();
            int throwType = ModContent.ProjectileType<StickThrowProjectile>();
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.type == throwType && p.owner == Player.whoAmI &&
                    p.ModProjectile is StickThrowProjectile stick && stick.Token != 0)
                    activeThrowTokens.Add(stick.Token);
            }
        }

        // Мод Weapon Out Lite рисует предмет в руках у персонажа (за спиной, на поясе).
        // Брошенная палка в слоте — предмет-призрак, и без этого он висел бы за спиной,
        // хотя сама палка лежит на земле. Mod.Call "HidePlayerHeldItem" прячет предмет
        // только на текущий кадр, поэтому зовём каждый тик, пока в руках брошенная палка.
        // Мягкая совместимость: без мода — ничего не происходит, ссылок на его сборку нет.
        private void HideThrownFromWeaponOut()
        {
            if (Main.dedServ || !HoldingStick || !IsLocked(Player.HeldItem))
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
            // Курсор почти строго над/под игроком: знак X скачет от тычка к тычку, и персонаж
            // зеркалится туда-сюда (тряска при ударе вниз). Держим текущую сторону (≤ ~6°).
            if (Math.Abs(dir.X) < StabSideDeadzone)
                dir = new Vector2(StabSideDeadzone * Player.direction, dir.Y).SafeNormalize(Vector2.UnitX);
            armAimDir = dir; // руки прыгают туда же, куда ушёл тычок — резко, а не плавно
            int damage = Math.Max(1, (int)(Player.GetWeaponDamage(held) * StickTypes.StabDamageMult(held)));
            stabProj = Projectile.NewProjectile(
                Player.GetSource_ItemUse(held),
                Player.MountedCenter,
                dir * 8f,
                ModContent.ProjectileType<DaggerStabProjectile>(),
                damage,
                held.knockBack * StickTypes.StabKnockbackMult(held),
                Player.whoAmI,
                duration, 0f, held.type); // длительность выпада = интервал тычков
        }

        // Синус угла (~20°), ниже которого копьё выставляется строго горизонтально
        private const float SpearSnapSin = 0.34f;

        private void EnsureSpearProjectile(Item held)
        {
            // Одно копьё на игрока — если уже есть, не спавним
            int spearType = ModContent.ProjectileType<SpearBraceProjectile>();
            if (IsMyProjectile(spearProj, spearType))
                return;

            // Угол фиксируется ОДИН раз и передаётся в ai[1] сразу при спавне —
            // так он синхронизируется на всех клиентах вместе со снарядом.
            Vector2 dir = (Main.MouseWorld - Player.MountedCenter).SafeNormalize(Vector2.UnitX);
            // Курсор почти на горизонте — копьё ровно горизонтально (как Jousting Lance
            // в обычной стойке), без случайного задирания на пару градусов.
            // Вверх/вниз копьё всё ещё направляется, если курсор заметно выше/ниже.
            if (Math.Abs(dir.Y) < SpearSnapSin)
                dir = new Vector2(dir.X >= 0 ? 1f : -1f, 0f);
            int damage = Math.Max(1, (int)(Player.GetWeaponDamage(held) * StickTypes.SpearDamageMult(held)));
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
            // Стоишь вплотную к стене — палка вылетает из центра, а не застревает в блоке
            Vector2 spawn = Player.MountedCenter + dir * 16f;
            if (!Collision.CanHit(Player.MountedCenter, 1, 1, spawn, 1, 1))
                spawn = Player.MountedCenter;
            int index = Projectile.NewProjectile(
                Player.GetSource_ItemUse(held),
                spawn,
                dir * StickThrowProjectile.ThrowSpeed,
                ModContent.ProjectileType<StickThrowProjectile>(),
                Player.GetWeaponDamage(held),
                held.knockBack,
                Player.whoAmI,
                -1f, 0f, held.type);
            if (index < 0 || index >= Main.maxProjectiles ||
                Main.projectile[index].ModProjectile is not StickThrowProjectile stick)
                return;

            // Предмет остаётся в инвентаре, но этот экземпляр заблокирован, пока
            // палка лежит в мире. Номер случайный — не совпадёт с чужими бросками.
            int token = Main.rand.Next(1, int.MaxValue);
            stick.Token = token;
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

            if (Player.whoAmI == Main.myPlayer && StickTypes.IsWoodSword(item))
            {
                // Пока зажата ПКМ, ЛКМ означает бросок, а не взмах:
                // иначе ванилла успевает начать взмах за тики ожидания броска
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

namespace WoodSwordsOverhaul.Content
{
    // Рисует палку в руке на замахе броска (ванилла держащийся предмет-«взмах» в покое не
    // рисует). Слой сразу после предмета в руке, до рукава: кисть закрывает хват, палка не
    // вылетает из руки — её положение берётся от кисти каждый кадр. В выброс не рисуется.
    public class StickThrowPoseLayer : PlayerDrawLayer
    {
        private const float GripInset = 4f; // сколько рукояти позади кисти, как в тычке

        public override Position GetDefaultPosition() => new AfterParent(PlayerDrawLayers.HeldItem);

        public override bool GetDefaultVisibility(PlayerDrawSet drawInfo) =>
            drawInfo.drawPlayer.whoAmI == Main.myPlayer &&
            drawInfo.drawPlayer.GetModPlayer<StickPlayer>().PoseItemVisible;

        protected override void Draw(ref PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            StickPlayer sp = player.GetModPlayer<StickPlayer>();
            int itemType = player.HeldItem.type;
            if (player.HeldItem.IsAir)
                return;
            Main.instance.LoadItem(itemType);
            Texture2D texture = TextureAssets.Item[itemType].Value;
            if (texture == null || texture.IsDisposed)
                return;

            StickSprite.Geometry geo = StickSprite.Get(itemType);
            bool flipped = player.direction == -1;
            Vector2 dir = sp.PoseItemDir;
            Vector2 butt = player.Center + sp.PoseHandOffset - dir * GripInset;
            float rotation = dir.ToRotation() - geo.AngleFor(flipped);
            SpriteEffects effects = flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Color color = Lighting.GetColor((butt / 16f).ToPoint());

            drawInfo.DrawDataCache.Add(new DrawData(texture, butt - Main.screenPosition, null,
                color, rotation, geo.ButtFor(flipped), 1f, effects, 0));
        }
    }
}
