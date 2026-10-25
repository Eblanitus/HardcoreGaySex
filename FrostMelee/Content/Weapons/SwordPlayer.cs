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
        private int throwFollowTicks;
        private float fireGlow;   // свечение Фростбранда от горящих рядом врагов
        // Насколько сильно горит сам клинок Frostbrand (0..1): счёт горящих врагов
        // и сглаживание живут в PostUpdate. От этого питаются пламя на клинке
        // (FrostJetFx.EmitBlade) и яркость ленты взмаха — «чем больше врагов горит,
        // тем ярче горит меч» (FROSTBRAND_ИТОГ.md). База FireIntensity > 0:
        // клинок из холодного огня горит всегда, враги доводят его до полной.
        public float FireGlow => fireGlow;
        public float FireIntensity => 0.55f + 0.45f * fireGlow;

        // === Конечный холодный огонь Фростбранда (балансир владельца) ===
        // У меча всего MaxFlames языков пламени. Каждый подожжённый враг
        // держит один; запас пуст — струя новых не поджигает (своих горящих
        // обновляет бесплатно). Враг умер горящим — язык возвращается к
        // игроку живым огоньком (FrostFlameWisp): бьёт врагов по пути,
        // висит у игрока и уходит со следующим взмахом. Тычок забрал
        // горение — язык сразу в орбиту. Потух сам по времени — молча в меч.
        // Учёт ведёт только клиент владельца (как полировка в MP).
        public const int MaxFlames = 5;
        public const int OrbitMeltTicks = 360; // 6 с: не потраченный огонёк вливается обратно в меч
        private readonly Dictionary<int, Vector2> flameHolders = new(); // whoAmI врага → его последнее место
        private int wispCount; // живых огоньков (возврат+орбита+взмах), пересчёт в UpdateFlames

        public bool FlameAuthority =>
            Main.netMode == NetmodeID.SinglePlayer || Player.whoAmI == Main.myPlayer;
        public bool HoldsFlame(NPC npc) => flameHolders.ContainsKey(npc.whoAmI);
        public int FreeFlames => Math.Max(0, MaxFlames - flameHolders.Count - wispCount);

        // Какая доля огня при мече и игроке (запас + живые огоньки), а не сидит
        // на врагах. Дуга взмаха по ней показывает, есть ли чем поджигать
        public float FlameFullness => FlameAuthority
            ? MathHelper.Clamp((FreeFlames + wispCount) / (float)MaxFlames, 0f, 1f)
            : 1f;
        public void AddFlameHolder(NPC npc) => flameHolders[npc.whoAmI] = npc.Center;

        private int FlameDamage()
        {
            Item held = Player.HeldItem;
            if (held != null && !held.IsAir && held.type == ItemID.Frostbrand)
                return Math.Max(1, Player.GetWeaponDamage(held) / 2);
            for (int i = 0; i < Player.inventory.Length; i++)
            {
                Item it = Player.inventory[i];
                if (it != null && !it.IsAir && it.type == ItemID.Frostbrand)
                    return Math.Max(1, Player.GetWeaponDamage(it) / 2);
            }
            return 20;
        }

        public void SpawnReturnFlame(Vector2 from)
        {
            if (Player.whoAmI != Main.myPlayer)
                return;
            Projectile.NewProjectile(Player.GetSource_FromThis(), from, Vector2.Zero,
                ModContent.ProjectileType<FrostFlameWisp>(), FlameDamage(), 0f, Player.whoAmI,
                0f, Main.rand.NextFloat(MathHelper.TwoPi));
        }

        public void SpawnOrbitFlame()
        {
            if (Player.whoAmI != Main.myPlayer)
                return;
            Projectile.NewProjectile(Player.GetSource_FromThis(), Player.Center, Vector2.Zero,
                ModContent.ProjectileType<FrostFlameWisp>(), FlameDamage(), 0f, Player.whoAmI,
                1f, Main.rand.NextFloat(MathHelper.TwoPi));
        }

        // Тычок забрал горение у цели: её язык уходит из врага в орбиту у игрока.
        // Чужой огонь (не из запаса) тоже можно подобрать, пока есть место
        public void OnFlameDrained(NPC target)
        {
            bool wasHolder = flameHolders.Remove(target.whoAmI);
            if (wasHolder || flameHolders.Count + wispCount < MaxFlames)
                SpawnOrbitFlame();
        }

        // Запас в мече пуст, но у игрока висит огонёк — струя берёт его
        public bool TryConsumeOrbitingFlame()
        {
            if (Player.whoAmI != Main.myPlayer)
                return false;
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.owner == Player.whoAmI && p.ModProjectile is FrostFlameWisp wisp && wisp.IsOrbiting)
                {
                    p.Kill();
                    wispCount = Math.Max(0, wispCount - 1);
                    return true;
                }
            }
            return false;
        }

        // Взмах: висящие огоньки срываются веером по прицелу (зовёт SwordGlobalItem)
        public void LaunchOrbitingFlames()
        {
            if (Player.whoAmI != Main.myPlayer || Player.HeldItem == null || Player.HeldItem.type != ItemID.Frostbrand)
                return;
            List<FrostFlameWisp> ready = new();
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.owner == Player.whoAmI && p.ModProjectile is FrostFlameWisp wisp && wisp.IsOrbiting)
                    ready.Add(wisp);
            }
            if (ready.Count == 0)
                return;
            Vector2 dir = (Main.MouseWorld - Player.MountedCenter).SafeNormalize(new Vector2(Player.direction, 0f));
            for (int i = 0; i < ready.Count; i++)
                ready[i].Launch(dir.RotatedBy((i - (ready.Count - 1) * 0.5f) * 0.16f));
        }

        private void UpdateFlames()
        {
            int count = 0;
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.owner == Player.whoAmI && p.ModProjectile is FrostFlameWisp)
                    count++;
            }
            wispCount = count;

            if (flameHolders.Count == 0)
                return;
            foreach (int who in new List<int>(flameHolders.Keys))
            {
                NPC npc = Main.npc[who];
                if (npc.active && FrostFireSystem.IsBurning(npc))
                {
                    flameHolders[who] = npc.Center;
                    continue;
                }
                Vector2 from = flameHolders[who];
                flameHolders.Remove(who);
                // умер горящим — язык летит обратно огоньком; просто потух —
                // запас молча вернулся в меч
                if (!npc.active)
                    SpawnReturnFlame(from);
            }
        }
        // Живая дуга взмаха этого игрока: SwingBladeLayer рисует клинок её углом
        public Projectile swingArc;                 // сколько тиков ещё идёт выброс/возврат
        private Vector2 throwAimDir = Vector2.UnitX;  // линия броска, зафиксированная при вылете
        // Меч в руке в замахе: рисует SwordThrowPoseLayer. Кисть — смещением от центра игрока
        public bool PoseItemVisible;
        public Vector2 PoseHandOffset;
        public Vector2 PoseItemDir = Vector2.UnitX;

        // === Ледяной огонь на клинке Frostbrand ===
        // Горит там, где меч нарисован СЕЙЧАС: позу отдаёт TryGetBladeLine (замах,
        // тычок, встреча, взмах, стойка). Летящий брошенный меч зажигает сам
        // SickleThrowProjectile — в руке в это время пусто.

        public bool HoldingSword =>
            !Player.HeldItem.IsAir && SwordTypes.IsSword(Player.HeldItem);

        // === Полировка ледяного клинка (только IceBlade) — наоборот копью ===
        // Копьё замораживается, клинок размораживается и точится.
        // Визуал как у копья (кроссфейд Rough -> Ice_Blade), по той же механике.
        // Теряет леденистость ~10с боем, заледеневает ~30с без боя.
        // Состояние инея/полировки — СВОЁ у каждого меча: разморозил один,
        // второй остаётся в своём состоянии (баг общего состояния убран)
        private readonly System.Collections.Generic.Dictionary<int, float> frost = new();
        private readonly System.Collections.Generic.Dictionary<int, float> frostShown = new();

        public float GetFrost(int itemType) => frost.TryGetValue(itemType, out float v) ? v : 1f;
        public float GetFrostShown(int itemType) => frostShown.TryGetValue(itemType, out float v) ? v : 1f;
        private void SetFrost(int itemType, float v) { frost[itemType] = v; }

        // Совместимость: состояние IceBlade для старых вызовов
        public float IceFrost { get => GetFrost(ItemID.IceBlade); set => SetFrost(ItemID.IceBlade, value); }
        public float IceFrostShown => GetFrostShown(ItemID.IceBlade);
        private const float IceFrostShownEase = 0.08f;
        public const int IceThawTicks = 600;   // 10 сек до 1->0 боем (если бить)
        public const int IceFreezeTicks = 1800; // 30 сек до 0->1 в покое
        public const float IcePolishPerHit = 0.07f; // ~14 попаданий до полной полировки
        private int iceHitCooldown; // чтобы не точить несколько раз за один взмах по толпе
        private int iceIdleTicks; // тиков без попаданий до заледенения
        private bool lastRoughLogged = true; // для строки диагностики полировки

        // === Отдача полировки на все приёмы меча ===
        // P = 0 (весь в инее) .. 1 (отполирован): быстрее и больнее, но меньше отдача
        // Полировка конкретного меча: у каждого своя. Не-мечи — ноль
        public static float PolishOf(Player player, Item item) =>
            item != null && SwordTypes.IsSword(item)
                ? 1f - player.GetModPlayer<SwordPlayer>().GetFrostShown(item.type)
                : 0f;

        public static float PolishDamageMult(Player player, Item item) =>
            1f + 0.25f * PolishOf(player, item);      // до +25% урона

        public static float PolishKnockbackMult(Player player, Item item) =>
            1f - 0.45f * PolishOf(player, item);       // до -45% отбрасывания

        public static float PolishSpeedMult(Player player, Item item) =>
            1f - 0.15f * PolishOf(player, item);       // до -15% времени использования

        // Хуки ModPlayer УМНОЖАЮТ время использования/анимации
        // (tML: TotalUseTime = useTime × UseTimeMultiplier, аналогично анимация):
        // <1 = быстрее. PolishSpeedMult — уже множитель ВРЕМЕНИ (0.85 при полной
        // полировке), возвращаем как есть. Раньше здесь стояло 1f / PolishSpeedMult —
        // отполированный меч получался на ~18% МЕДЛЕННЕЕ шершавого вместо быстрее.
        public override float UseTimeMultiplier(Item item) =>
            SwordTypes.IsSword(item) ? PolishSpeedMult(Player, item) : 1f;

        public override float UseAnimationMultiplier(Item item) =>
            SwordTypes.IsSword(item) ? PolishSpeedMult(Player, item) : 1f;

        // true — клинок на этом попадании реально очистился (шкала инея упала).
        // Тип меча задаёт ВЫЗВАВШИЙ (снаряд, который попал), а не HeldItem:
        // брошенный клинок бьёт позже броска, когда в руке уже другое оружие, —
        // по HeldItem полировка падала на чужой меч (бросил Ice Blade, в руках
        // Frostbrand — точился Frostbrand, у которого полировки быть не должно).
        public bool AddPolish(int itemType)
        {
            if (Player.whoAmI != Main.myPlayer || !SwordTypes.IsSwordId(itemType)) return false;
            if (iceHitCooldown > 0) return false;
            int key = itemType;
            float before = GetFrost(key);
            SetFrost(key, MathHelper.Clamp(before - IcePolishPerHit, 0f, 1f));
            iceHitCooldown = 10; // 10 тиков кулдауна между полировками (защита от мультихита дуги)
            iceIdleTicks = 0;
            Mod.Logger.Info($"[Polish] попадание: меч {key}, frost={GetFrost(key):F2}");
            return GetFrost(key) < before;
        }

        // Клинок очищается прямо сейчас: после настоящей полировки видимый иней ещё
        // сходит к новому значению. Только пока это идёт, сыплются частицы очищения
        public bool IsPolishing(int itemType) => GetFrostShown(itemType) - GetFrost(itemType) > 0.003f;

        private void UpdateIceFrost()
        {
            if (Player.whoAmI != Main.myPlayer) return;

            if (iceHitCooldown > 0) iceHitCooldown--;

            // Плавный визуал и заморозка в покое — у КАЖДОГО меча свои
            foreach (int key in SwordTypes.Swords)
            {
                float f = GetFrost(key);
                float shown = GetFrostShown(key);
                shown += (f - shown) * IceFrostShownEase;
                if (System.Math.Abs(f - shown) < 0.002f) shown = f;
                frostShown[key] = shown;

                bool heldNow = HoldingSword && Player.HeldItem.type == key;
                if (Player.dead)
                {
                    SetFrost(key, 1f);
                }
                else if (!heldNow || iceIdleTicks > 60)
                {
                    // вне рук или в покое — медленно заледеневает обратно
                    if (f < 1f) SetFrost(key, MathHelper.Clamp(f + 1f / IceFreezeTicks, 0f, 1f));
                }
            }

            // В бою точим только попаданиями (AddPolish). В покое — заледеневает.
            iceIdleTicks++;

            // Диагностика полировки: переход через порог смены вида (IceBlade)
            bool roughNow = GetFrostShown(ItemID.IceBlade) >= 0.45f;
            if (roughNow != lastRoughLogged)
            {
                lastRoughLogged = roughNow;
                Mod.Logger.Info($"[Polish] вид: {(roughNow ? "шершавый" : "острый")}, shown={GetFrostShown(ItemID.IceBlade):F2}");
            }

            IceBladeVisual.UpdateView(GetFrostShown(ItemID.IceBlade));
        }

        // Каденс тычков = эффективный каденс свинга × стихия (у меча резкие тычки)
        private int StabInterval(Item held)
        {
            int swing = CombinedHooks.TotalAnimationTime(held.useAnimation, Player, held);
            return Math.Max(6, (int)Math.Round(swing * SwordTypes.StabTimeMult(held)));
        }

        public override void PreUpdate()
        {
            // Полировка/заледенение — каждый тик, даже вне ввода
            UpdateIceFrost();

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

        public bool TryGetSpearDir(out float angle) => TryGetSpearAngle(out angle);

        public override void PostUpdate()
        {
            // Поза и разворот — только для локального игрока, именно PostUpdate:
            // ванилла каждый тик перебивает руки в PlayerFrame и разворот в движении.
            if (Player.whoAmI != Main.myPlayer)
                return;

            // Конечный огонь Фростбранда: умер горящий — язык возвращается
            UpdateFlames();

            // Взмах меча: клинок и дуга идут по ОДНОЙ формуле угла.
            // Ваниль рисует предмет в руке по своему itemRotation (своя кривая
            // взмаха), дуга живёт по нашей кривой — из-за расхождения кривых
            // лента визуально отставала от клинка. Перебиваем itemRotation
            // нашей кривой: ванильный клинок и дуга становятся одним углом.
            if (CurrentMode == Mode.Swing && HoldingSword && Player.itemAnimation > 0 && Player.itemAnimationMax > 0)
            {
                float progress = 1f - Player.itemAnimation / (float)Player.itemAnimationMax;
                float theta = Player.direction >= 0
                    ? MathHelper.Pi * (1f + progress)
                    : -MathHelper.Pi * progress;
                theta += Player.fullRotation;
                bool flipped = Player.direction == -1;
                SickleSprite.Geometry geo = SickleSprite.Get(Player.HeldItem.type);
                float baseAng = (geo.TipFor(flipped) - geo.ButtFor(flipped)).ToRotation();
                Player.itemRotation = MathHelper.WrapAngle(theta - baseAng);
            }

            // Фростбранд: чем больше врагов горит рядом, тем ярче светится меч
            if (HoldingSword && Player.HeldItem.type == ItemID.Frostbrand)
            {
                int burning = FrostFireSystem.CountBurningNear(Player.Center, 500f);
                float target = System.Math.Min(burning, 5) / 5f;
                fireGlow += (target - fireGlow) * 0.08f;
                if (fireGlow > 0.02f)
                {
                    Lighting.AddLight(Player.Center, 0.25f * fireGlow, 0.55f * fireGlow, 0.75f * fireGlow);
                    if (!Main.dedServ && Main.rand.NextFloat() < 0.15f + 0.35f * fireGlow)
                    {
                        Dust d = Dust.NewDustPerfect(Player.Center + new Vector2(Player.direction * 18f, -10f),
                            DustID.IceTorch,
                            new Vector2(Main.rand.NextFloat(-0.4f, 0.4f), -Main.rand.NextFloat(0.6f, 1.4f)),
                            100, default, Main.rand.NextFloat(0.8f, 1.4f));
                        d.noGravity = true;
                    }
                }
            }
            else
            {
                fireGlow *= 0.92f;
            }
            // Скольжение на льду — только когда уже разогнался с копьём, а не постоял
            if (SpearMode) {
                IceSlideSystem.TrySlide(Player, this);
            }
            // Повторно: PostUpdate Weapon Out может идти раньше нашего PreUpdate
            HideFromWeaponOut();
            PoseItemVisible = false;
            if (!HoldingSword || Player.dead)
                return;
            bool throwing = ApplyThrowPose();
            EmitBladeFlame();
            if (throwing)
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

        // Только пламя-частицы на клинке (FrostJetFx) — снарядов струи здесь нет.
        // Струю как поджигающие снаряды пускают: дуга веером (SwordArcProjectile.
        // SpawnArcJets) и встреча факелом (SickleBraceProjectile). У тычка струи
        // НЕТ ни в каком виде (решение владельца): он только забирает чужой огонь
        private void EmitBladeFlame()
        {
            if (Player.HeldItem.type != ItemID.Frostbrand || CurrentMode == Mode.Spear)
                return;
            if (CurrentMode == Mode.Swing && Player.itemAnimation <= 0 && !PoseItemVisible)
                return;
            if (CurrentMode == Mode.Stab && !IsMyProjectile(stabProj, ModContent.ProjectileType<SwordStabProjectile>()))
                return;
            if (!TryGetBladeLine(out Vector2 butt, out Vector2 tip))
                return;

            Vector2 dir = (tip - butt).SafeNormalize(Vector2.UnitX);
            float retract = CurrentMode == Mode.Stab ? StabRetract01() : 0f;
            // Каждый тик: на быстром взмахе частицы раз в 2 тика дают разрывы,
            // и огонь кажется прыгающим вслед за клинком. Сила пламени — от числа
            // горящих врагов рядом (FireIntensity), на возврате тычка — слабее.
            FrostJetFx.EmitBlade(butt, tip, dir, FireIntensity * (1f - 0.8f * retract));
        }

        public float StabRetractProgress => StabRetract01();

        private float StabRetract01()
        {
            if (!IsMyProjectile(stabProj, ModContent.ProjectileType<SwordStabProjectile>()))
                return 0f;
            return Main.projectile[stabProj].ModProjectile is SwordStabProjectile stab
                ? stab.Retract01
                : 0f;
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
            // 5. Взмах: поза, которой SwingBladeLayer рисует клинок
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
            float half = projType == ModContent.ProjectileType<SwordStabProjectile>()
                && proj.ModProjectile is SwordStabProjectile stab
                ? stab.BladeHalfLength
                : SickleSprite.Get((int)proj.ai[2]).Length * 0.5f;
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
        public bool TryGetSpearAngle(out float angle)
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
            int damage = Math.Max(1, (int)(Player.GetWeaponDamage(held) * SwordTypes.StabDamageMult(held) * PolishDamageMult(Player, held)));
            stabProj = Projectile.NewProjectile(
                Player.GetSource_ItemUse(held),
                Player.MountedCenter,
                dir * 8f,
                ModContent.ProjectileType<SwordStabProjectile>(),
                damage,
                held.knockBack * SwordTypes.StabKnockbackMult(held) * PolishKnockbackMult(Player, held),
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
            int damage = Math.Max(1, (int)(Player.GetWeaponDamage(held) * SwordTypes.SpearDamageMult(held) * PolishDamageMult(Player, held)));
            spearProj = Projectile.NewProjectile(
                Player.GetSource_ItemUse(held),
                Player.MountedCenter + dir * 18f,
                Vector2.Zero,
                meetType,
                damage,
                held.knockBack * PolishKnockbackMult(Player, held),
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
                Math.Max(1, (int)(Player.GetWeaponDamage(held) * PolishDamageMult(Player, held))),
                held.knockBack * PolishKnockbackMult(Player, held),
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
