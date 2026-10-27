using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using FrostMelee.Content.Weapons;

namespace FrostMelee.Content.Projectiles
{
    // Бросок: серп летит по баллистике и кувыркается, бьётся о землю,
    // подпрыгивает и ложится плашмя. Втыкается (во врага и в мягкий тайл)
    // только если коснулся острием вперёд (доктрина: бросок не окупается,
    // подбери сам — без автовозврата). Забрать — подойти вплотную.
    //
    // ai[0] — индекс NPC, в котором застрял (-1 = нет)
    // ai[1] — состояние: 0 летит, 1 лежит, 2 застрял во враге, 3 подлетает, 4 в тайле
    // ai[2] — тип предмета (для отрисовки)
    public class SickleThrowProjectile : ModProjectile
    {
        public const float ThrowSpeed = 12f;        // начальная скорость (px/тик)
        private const float Gravity = 0.25f;        // тянет вниз с первого тика
        private const float MaxFallSpeed = 14f;
        private const float AirDrag = 0.995f;
        private const float SpinPerSpeed = 0.035f;  // кувырок: рад/тик на 1 px/тик
        private const float MaxSpin = 0.4f;
        private const float MinDamageSpeed = 5f;    // медленный серп не ранит
        private const float GroundBounce = 0.3f;
        private const float MinBounceSpeed = 3f;
        private const float GroundFriction = 0.6f;
        private const float WallBounce = 0.3f;
        private const float LyingFriction = 0.8f;
        private const int SettleTicks = 8;
        private const int FlightBox = 12;
        private const float HitWidth = 6f;         // запасной хитбокс без рисунка
        private const float PickupRange = 24f;      // подобрать почти вплотную
        private const int PickupDelay = 20;
        private const float PickupStartSpeed = 3f;
        private const float PickupAccel = 1.2f;
        private const float PickupMaxSpeed = 16f;
        private const float PickupCatchDist = 12f;
        private const int PickupMaxTicks = 45;
        private const float StickAngle = 0.49f;     // допуск острия (~28°)
        private const float StickMinSpeed = 7f;
        private const float SideHitDamageMult = 0.5f;
        private const float StickEmbedDepth = 12f; // глубже, чем 6: меч не балансирует на кончике
        // Втыкается только та часть клинка, которой вообще втыкаются: передний
        // кусок у кончика. Касание серединой клинка или рукоятью — не втыкание,
        // а удар плашмя (иначе меч «застревал» в воздухе рядом с врагом:
        // кончик далеко сбоку, а защёлка дёргала меч к цели)
        private const float StickTipZoneFraction = 0.35f; // доля длины от кончика
        private const float StickTipMaxGap = 20f;         // кончик не дальше этого от врага
        private const float StickTipReach = 16f;          // кончик не дальше этого от земли
        // Авто-возврат через N тиков; 0 = никогда (доктрина: подбери сам)
        private const int AutoReturnTicks = 0;

        // === Застрявший Frostbrand: разгорающаяся аура ===
        private const int LodgedAuraGrowTicks = 120;   // 2 с до полного разгорания
        private const float LodgedAuraMinRadius = 28f;
        private const float LodgedAuraMaxRadius = 88f; // дальше не растёт
        private const int LodgedHeatTicks = 480;       // «долго сидит»: ещё 8 с пламя только густеет (картинка)
        private const int LodgedBurnPulse = 15;        // как часто поджигать/продлевать

        // === Ледяная коса (только ItemID.IceSickle, серп смерти не трогаем) ===
        // Нелинейный полёт: резкий старт и плавное торможение (ease-out), дальность примерно прежняя
        private const float LaunchSpeedBoost = 1.4f;    // стартовая скорость x от ThrowSpeed
        private const float LaunchEaseDrag = 0.984f;    // торможение за тик, пока скорость выше минимума
        private const int LaunchEaseTicks = 60;         // торможение только в первые N тиков полёта
        private const float LaunchEaseMinSpeed = 8.4f;  // ниже этой скорости торможение не добавляется
        private const float MaxStepSpeed = 15.5f;       // за тик не больше тайла (16 px): коллизия не проскакивает слой
        private const float BuriedEjectStep = 2f;       // шаг выталкивания из блока вверх, px
        private const int BuriedEjectMaxSteps = 400;    // не дальше ~50 тайлов
        // Полоса инея в полёте: та же зона замедления, что у взмаха (FrostTrailSystem в SickleArcProjectile.cs;
        // иней, время жизни и Slow на цель живут там)
        private const float TrailMinSpeed = 4f;         // медленная коса след не оставляет
        private const float TrailSpacing = 8f;          // шаг точек вдоль полёта, px (на 12 px/тик это 1-2 точки за тик)
        // Зона инея от смерти врага (бросок ледяного копья): радиус и срок растут с шкалой заморозки SicklePlayer.Frost.
        // 0.1 → ~3 тайла и 3 с, 1.0 → 12 тайлов и 12 с (линейно). После зоны шкала обнуляется.
        private const float ZoneMinFrost = 0.05f;           // ниже — зоны нет
        private const float ZoneRadiusBaseTiles = 2f;       // радиус = Base + PerFrost * шкала, тайлов
        private const float ZoneRadiusPerFrostTiles = 10f;
        private const float ZoneSecondsBase = 2f;           // срок = Base + PerFrost * шкала, секунд
        private const float ZoneSecondsPerFrost = 10f;
        private const float TrailSpread = 4f;           // разброс точки поперёк, px
        private const int TrailMaxStepsPerTick = 12;

        private const int StateFlying = 0, StateLying = 1, StateLodged = 2, StatePickup = 3, StateStuckTile = 4;

        // Мягкие тайлы, в которые втыкается острие
        private static readonly HashSet<int> SoftTiles = new HashSet<int> {
            TileID.Dirt, TileID.Mud, TileID.Silt, TileID.Slush, TileID.ClayBlock, TileID.SnowBlock,
            TileID.Sand, TileID.Ebonsand, TileID.Pearlsand, TileID.Crimsand,
            TileID.Grass, TileID.CorruptGrass, TileID.CrimsonGrass, TileID.HallowedGrass,
            TileID.JungleGrass, TileID.MushroomGrass,
            633, // пепельная трава
            57   // пепел
        };

        // Номер броска для блокировки предмета (задаёт SicklePlayer, только у владельца)
        public int Token;

        // Страховочная текстура (своя отрисовка всё равно по ai[2])
        public override string Texture => "FrostMelee/Items/IceSpear";

        private int State
        {
            get => (int)Projectile.ai[1];
            set => Projectile.ai[1] = value;
        }

        private SickleSprite.Geometry Geo => SickleSprite.Get((int)Projectile.ai[2]);

        private bool initialized;
        private bool harmless;
        private float spinDir = 1f;
        private float spinScale = 1f;
        private bool lyingInit;
        private int settleTimer;
        private float settleFromHeading, settleToHeading;
        private Vector2 settleFromOffset;
        private float lyingBelow;
        private bool lodgeInit;
        private Vector2 lodgeOffset;
        private bool stuckInit;
        private Point stuckTile;
        private bool cleanNpcHit;
        private bool stickNpcHit;
        private int lodgedAuraTicks;
        private int lodgedBurnPulse;

        private FrostTrailSystem.Trail frostTrail;
        private Vector2 lastTrailPos;
        private float frostAtThrow;     // шкала заморозки владельца в момент броска (только владелец)
        private int launchTicks;      // тиков полёта с торможением ease-out
        private bool zoneSpawned;       // зона один раз за бросок

        private bool IsIce => (int)Projectile.ai[2] == ItemID.IceSickle;
        private bool IsFrostbrand => (int)Projectile.ai[2] == ItemID.Frostbrand;
        private bool IsAisberg => Weapons.SwordTypes.IsAisbergId((int)Projectile.ai[2]);

        // Карамелька-приманка: неподвижный леденец (лёжа, в земле или во
        // враге) тратит заряд сладости владельца (CandyCanePlayer) — тик
        // приманки за тик заряда. Заряд кончился — «съеден»: не манит и серый
        private int candyStillTicks; // только ритм сладкой пыли, тикает у всех
        private Weapons.CandyCanePlayer CandyOwner =>
            Main.player[Projectile.owner].GetModPlayer<Weapons.CandyCanePlayer>();
        public bool CandyEaten => (int)Projectile.ai[2] == ItemID.CandyCaneSword &&
            CandyOwner.SweetTicks <= 0;
        public bool CandyLureActive => (int)Projectile.ai[2] == ItemID.CandyCaneSword &&
            (State == StateLying || State == StateStuckTile || State == StateLodged) && !CandyEaten;

        // Серый («съеденный») спрайт карамельки и его геометрия: один раз на всех
        private static Asset<Texture2D> candyGrayAsset;
        private static SickleSprite.Geometry? candyGrayGeo;

        public override void SetDefaults()
        {
            Projectile.width = FlightBox;
            Projectile.height = FlightBox;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
            Projectile.tileCollide = true;
            Projectile.ignoreWater = false;
            Projectile.netImportant = true;
            Projectile.timeLeft = AutoReturnTicks > 0 ? AutoReturnTicks : 2;
            Projectile.aiStyle = -1;
        }

        public override void AI()
        {
            if (!initialized)
            {
                initialized = true;
                spinDir = Projectile.velocity.X >= 0f ? 1f : -1f;
                if (Projectile.velocity != Vector2.Zero)
                    Projectile.rotation = Projectile.velocity.ToRotation();
                if (IsIce)
                    Projectile.velocity *= LaunchSpeedBoost;
                if (IsIce && Projectile.owner == Main.myPlayer)
                    frostAtThrow = Main.player[Projectile.owner].GetModPlayer<SicklePlayer>().Frost;
            }
            Projectile.localAI[0]++;
            // Воткнувшийся меч рисуется ПОД врагом/блоками (DrawBehind ниже):
            // тело врага перекрывает вошедшую часть клинка, и меч выглядит
            // именно воткнувшимся в него, а не наклейкой поверх спрайта
            // (требование владельца: «под текстурой, а не поверх текстуры»)
            Projectile.hide = State == StateLodged || State == StateStuckTile;
            if (AutoReturnTicks <= 0)
                Projectile.timeLeft = 2;

            if (IsIce && State != StateFlying)
                CloseFrostTrail();
            if (State == StateFlying && harmless || State == StateLying)
                EjectIfBuried();

            switch (State)
            {
                case StateLodged:
                    LodgedAI();
                    break;
                case StateStuckTile:
                    StuckTileAI();
                    break;
                case StatePickup:
                    PickupAI();
                    return;
                case StateLying:
                    LyingAI();
                    break;
                default:
                    FlyingAI();
                    break;
            }

            // Меч может застрять в состоянии полёта, если OnTileCollide не пришёл
            // (стык блоков, склон): визуально лежит, но подбор не срабатывает.
            // Уже невредимый и почти остановившийся считаем лежащим.
            if (State == StateFlying && harmless && Projectile.velocity.LengthSquared() < 0.25f)
            {
                State = StateLying;
                lyingInit = false;
                Projectile.netUpdate = true;
            }

            // Неподвижный леденец пахнет сладко и манит врагов (рулит ими
            // CandyCaneLureNPC), пока не кончится заряд сладости владельца:
            // тик неподвижности = тик заряда, считает только владелец
            if ((int)Projectile.ai[2] == ItemID.CandyCaneSword &&
                (State == StateLying || State == StateStuckTile || State == StateLodged))
            {
                candyStillTicks++;
                if (Projectile.owner == Main.myPlayer && CandyOwner.SweetTicks > 0)
                    CandyOwner.SweetTicks--;
                if (!Main.dedServ && !CandyEaten && candyStillTicks % 18 == 0)
                {
                    Dust d = Dust.NewDustPerfect(Projectile.Center + Main.rand.NextVector2Circular(10f, 6f),
                        DustID.Snow, new Vector2(0f, -0.6f), 0, new Color(255, 120, 140), 1f);
                    d.noGravity = true;
                }
            }

            TryPickup();
        }

        private void FlyingAI()
        {
            Projectile.tileCollide = true;
            // Ледяной бросок: резкий старт, потом плавное торможение (гравитацию не трогаем)
            if (IsIce && !harmless && launchTicks < LaunchEaseTicks)
            {
                launchTicks++;
                if (Projectile.velocity.Length() > LaunchEaseMinSpeed)
                    Projectile.velocity *= LaunchEaseDrag;
            }
            Projectile.velocity.X *= AirDrag;
            Projectile.velocity.Y = Math.Min(Projectile.velocity.Y + Gravity, MaxFallSpeed);
            float rawSpeed = Projectile.velocity.Length();
            if (rawSpeed > MaxStepSpeed)
                Projectile.velocity *= MaxStepSpeed / rawSpeed;

            float speed = Projectile.velocity.Length();
            if (!harmless && AlwaysTipFirst)
                Projectile.rotation = Projectile.velocity.ToRotation();
            else
                Projectile.rotation += Math.Min(speed * SpinPerSpeed, MaxSpin) * spinScale * spinDir;

            Projectile.friendly = !harmless && speed >= MinDamageSpeed;

            if (IsIce)
                RecordFrostTrail(speed);
            if (IsFrostbrand)
                EmitFrostJet();

            if (SickleArcProjectile.DebugHitbox && Projectile.owner == Main.myPlayer && Projectile.friendly)
                SickleSprite.EmitDebugDust(SickleSprite.GetZone((int)Projectile.ai[2]), Projectile.Center,
                    Projectile.rotation, false, Projectile.scale);
        }

        // Страховка: упавшая/лежащая коса не должна сидеть в толще блоков (выпала из умершего врага внутри земли,
        // провалилась). Центр внутри сплошного тайла: поднимаем вверх до свободного места
        private void EjectIfBuried()
        {
            if (!SolidTileAt(Projectile.Center, out _, out _))
                return;
            Vector2 p = Projectile.Center;
            for (int i = 0; i < BuriedEjectMaxSteps && SolidTileAt(p, out _, out _); i++)
                p.Y -= BuriedEjectStep;
            Projectile.Center = p;
            Projectile.velocity = new Vector2(0f, -1f);
            Projectile.netUpdate = true;
        }

        private void LyingAI()
        {
            if (!lyingInit)
                BeginLying();

            Projectile.friendly = false;
            Projectile.tileCollide = true;
            Projectile.velocity.X *= LyingFriction;
            if (Math.Abs(Projectile.velocity.X) < 0.05f)
                Projectile.velocity.X = 0f;
            Projectile.velocity.Y = Math.Min(Projectile.velocity.Y + Gravity, MaxFallSpeed);

            if (settleTimer < SettleTicks)
                settleTimer++;
            Projectile.rotation = MathHelper.Lerp(settleFromHeading, settleToHeading, SettleProgress);
        }

        private void LodgedAI()
        {
            Projectile.friendly = false;
            Projectile.tileCollide = false;
            Projectile.velocity = Vector2.Zero;

            int npcIndex = (int)Projectile.ai[0];
            if (npcIndex >= 0 && npcIndex < Main.maxNPCs && Main.npc[npcIndex].active)
            {
                NPC npc = Main.npc[npcIndex];
                if (!lodgeInit)
                {
                    lodgeInit = true;
                    lodgeOffset = TipLodgeOffset(npc);
                }
                Projectile.Center = npc.Center + lodgeOffset;
                UpdateLodgedFrostbrand(npc);
                return;
            }

            // Враг умер — серп выпадает и падает на землю
            Projectile.ai[0] = -1f;
            State = StateFlying;
            harmless = true;
            spinScale = 0.4f;
            lodgedAuraTicks = 0;
            lodgedBurnPulse = 0;
            Projectile.velocity = new Vector2(0f, -1.5f);
            Projectile.tileCollide = true;
            Projectile.netUpdate = true;
        }

        private void StuckTileAI()
        {
            Projectile.friendly = false;
            Projectile.tileCollide = false;
            Projectile.velocity = Vector2.Zero;

            if (!stuckInit)
            {
                stuckInit = true;
                Vector2 tip = Projectile.Center + Projectile.rotation.ToRotationVector2() * Geo.FrontReach;
                Vector2 axis = Projectile.rotation.ToRotationVector2();
                bool found = false;
                for (float d = -4f; d <= 6f && !found; d += 1f)
                    found = SolidTileAt(tip + axis * d, out stuckTile, out _);
                if (!found)
                    stuckTile = new Point(-1, -1);
            }

            if (stuckTile.X >= 0 && SolidTileAt(stuckTile.ToWorldCoordinates(8f, 8f), out _, out _))
            {
                UpdateLodgedFrostbrand(null);
                return;
            }

            // Опоры нет — серп выпадает
            stuckInit = false;
            State = StateFlying;
            harmless = true;
            spinScale = 0.4f;
            lodgedAuraTicks = 0;
            lodgedBurnPulse = 0;
            Projectile.velocity = new Vector2(0f, -1.5f);
            Projectile.tileCollide = true;
            Projectile.netUpdate = true;
        }

        private static bool SolidTileAt(Vector2 worldPos, out Point tilePos, out int type)
        {
            tilePos = worldPos.ToTileCoordinates();
            type = -1;
            if (!WorldGen.InWorld(tilePos.X, tilePos.Y, 2))
                return false;
            Tile tile = Main.tile[tilePos.X, tilePos.Y];
            if (!tile.HasUnactuatedTile || !(Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType]))
                return false;
            type = tile.TileType;
            return true;
        }

        private bool AlwaysTipFirst => SickleTypes.ThrownAlwaysTipFirst((int)Projectile.ai[2]);

        private bool IsCleanHit(Vector2 velocity)
        {
            // Чистый удар: почти идеально кончиком вперёд (допуск StickAngle).
            // Даёт полный урон. Втыкание и волна шире — IsPointHit + касание остриём
            if (harmless)
                return false;
            if (velocity.Length() < StickMinSpeed)
                return false;
            if (AlwaysTipFirst)
                return true;
            return Math.Abs(MathHelper.WrapAngle(velocity.ToRotation() - Projectile.rotation)) <= StickAngle;
        }

        private bool IsPointHit(Vector2 velocity)
        {
            // Втыкание кувыркающегося меча (решение владельца): не только
            // идеально кончиком по полёту, а когда острие впереди центра меча
            // по ходу полёта — вся передняя половина кувырка. Втыкается меч
            // под своим настоящим углом, вдоль собственной оси, без всякого
            // доворота. Прилетел рукоятью вперёд (острие в задней половине) —
            // втыкаться нечем: отскок
            if (harmless)
                return false;
            if (velocity.Length() < StickMinSpeed)
                return false;
            if (AlwaysTipFirst)
                return true;
            return Math.Abs(MathHelper.WrapAngle(velocity.ToRotation() - Projectile.rotation)) < MathHelper.PiOver2;
        }

        // Коснулся ли врага передний кусок клинка у кончика, и сам кончик при
        // этом рядом с врагом (а не где-то сбоку). Без этого меч застревал от
        // касания серединой клинка и висел на враге багануто: защёлка
        // (TipLodgeOffset) дёргала его так, чтобы кончик оказался внутри
        private bool TipSectionTouches(NPC target)
        {
            SickleSprite.Geometry geo = Geo;
            Vector2 axis = Projectile.rotation.ToRotationVector2();
            Vector2 tip = Projectile.Center + axis * geo.FrontReach;
            Rectangle box = target.Hitbox;

            Vector2 nearest = new Vector2(
                MathHelper.Clamp(tip.X, box.Left, box.Right),
                MathHelper.Clamp(tip.Y, box.Top, box.Bottom));
            if (Vector2.Distance(tip, nearest) > StickTipMaxGap)
                return false;

            // Точки вдоль переднего куска клинка: хоть одна внутри врага (с
            // запасом 4 px) — коснулся остриём. Сэмплинг вместо
            // CheckAABBvLineCollision: с ней условие почти никогда не
            // выполнялось и волна ледяного меча пропала совсем
            box.Inflate(4, 4);
            float zoneLen = (geo.FrontReach + geo.RearReach) * StickTipZoneFraction;
            for (float d = 0f; d <= zoneLen; d += 4f)
            {
                if (box.Contains((tip - axis * d).ToPoint()))
                    return true;
            }
            return box.Contains((tip - axis * zoneLen).ToPoint());
        }

        private bool TryLodgeInTile(Vector2 oldVelocity)
        {
            if (!IsPointHit(oldVelocity))
                return false;

            // Честное втыкание БЕЗ доворота (доворот до оси полёта — костыль,
            // замечание владельца): меч остаётся под углом, под которым
            // прилетел, а кончик уходит в землю вдоль СОБСТВЕННОЙ оси клинка.
            // Ищем, где ось клинка пересекает поверхность: кончик уже внутри —
            // шагаем назад до выхода; ещё снаружи — шагаем вперёд до встречи
            SickleSprite.Geometry geo = Geo;
            Vector2 axis = Projectile.rotation.ToRotationVector2();
            Vector2 tip = Projectile.Center + axis * geo.FrontReach;
            Vector2 contact;
            int contactType;
            if (SolidTileAt(tip, out _, out contactType))
            {
                contact = tip;
                bool exited = false;
                for (float d = 1f; d <= 48f; d += 1f)
                {
                    Vector2 p = tip - axis * d;
                    if (!SolidTileAt(p, out _, out int t))
                    {
                        exited = true;
                        break;
                    }
                    contact = p;
                    contactType = t;
                }
                if (!exited)
                    return false; // клинок целиком в земле — не втыкание, а закапывание
            }
            else
            {
                contact = tip;
                contactType = -1;
                bool found = false;
                // Только пара пикселей допуска: кончик далеко от земли вдоль
                // оси — значит, удар приняла середина/рукоять, не втыкание
                float reach = StickTipReach;
                for (float d = 1f; d <= reach; d += 1f)
                {
                    Vector2 p = tip + axis * d;
                    if (!SolidTileAt(p, out _, out int t))
                        continue;
                    contact = p;
                    contactType = t;
                    found = true;
                    break;
                }
                if (!found)
                    return false; // кончик земли не достал (чиркнул боком) — не втыкается
            }
            if (!SoftTiles.Contains(contactType))
                return false; // камень, руда, кирпич, платформа: отскок

            float embed = Math.Min(StickEmbedDepth, geo.Length * 0.4f);
            // Кончик — на embed глубже поверхности вдоль оси, центр — от него.
            // Угол (rotation) не трогаем вообще
            Vector2 newCenter = contact + axis * embed - axis * geo.FrontReach;
            // Середина или рукоять при таком угле оказались бы внутри земли —
            // меч не воткнулся, а лёг в неё боком: не втыкаем, пусть отскочит
            // и ляжет как положено
            if (SolidTileAt(newCenter, out _, out _) || SolidTileAt(newCenter - axis * geo.RearReach, out _, out _))
                return false;
            SoundEngine.PlaySound(SoundID.Dig with { Volume = 0.7f, Pitch = 0.2f }, contact);
            Projectile.Center = newCenter;
            Projectile.velocity = Vector2.Zero;
            Projectile.ai[0] = -1f;
            State = StateStuckTile;
            stuckInit = false;
            lodgedAuraTicks = 0;
            lodgedBurnPulse = 0;
            harmless = true;
            Projectile.friendly = false;
            Projectile.tileCollide = false;
            Projectile.netUpdate = true;
            // Кристалл льда из земли при втыкании ледяного клинка (обычный, т.к. не убийство)
            if ((int)Projectile.ai[2] == ItemID.IceBlade && Projectile.owner == Main.myPlayer) {
                SpawnIceCrystal((int)Projectile.ai[2], false);
            }
            // Айсберг: крупные глыбы в момент втыкания, из точки контакта
            if (IsAisberg && Projectile.owner == Main.myPlayer) {
                AisbergChunk.SpawnThrowChunks(Main.player[Projectile.owner],
                    Projectile.GetSource_FromThis(), contact, Projectile.damage, Projectile.knockBack);
            }
            return true;
        }

        // Полоса инея за косой: точки вдоль пройденного за тик пути идут в общий FrostTrailSystem,
        // он рисует иней и сам замедляет врагов в нём (владелец, один раз на цель)
        private void RecordFrostTrail(float speed)
        {
            bool flyingFast = State == StateFlying && !harmless && speed >= TrailMinSpeed;
            if (!flyingFast)
            {
                CloseFrostTrail();
                return;
            }
            if (Main.dedServ)
                return;
            if (frostTrail == null)
            {
                frostTrail = FrostTrailSystem.Begin(Projectile.owner == Main.myPlayer, frostAtThrow >= SicklePlayer.FrostStrongAt);
                lastTrailPos = Projectile.Center;
            }

            Vector2 delta = Projectile.Center - lastTrailPos;
            float dist = delta.Length();
            if (float.IsNaN(dist) || dist < 0.5f)
                return;
            int n = Math.Clamp((int)Math.Ceiling(dist / TrailSpacing), 1, TrailMaxStepsPerTick);
            for (int i = 1; i <= n; i++)
                frostTrail.Add(Vector2.Lerp(lastTrailPos, Projectile.Center, i / (float)n) +
                               Main.rand.NextVector2Circular(TrailSpread, TrailSpread));
            lastTrailPos = Projectile.Center;
        }

        // Огонь на летящем Frostbrand: тот же клинок, что рисует PreDraw.
        // EmitBladeLoose, а не EmitBlade: брошенный меч не должен делить
        // привязанную линию клинка с мечом в руке (см. FrostJetFx)
        private void EmitFrostJet()
        {
            if (State != StateFlying)
                return;

            SickleSprite.Geometry geo = Geo;
            float half = geo.Length * 0.5f * Projectile.scale;
            if (half <= 1f)
                return;

            Vector2 axis = Projectile.rotation.ToRotationVector2();
            Vector2 center = VisualCenter(geo);
            Vector2 butt = center - axis * half;
            Vector2 tip = center + axis * half;
            int source = FrostJetFx.LooseSource(Projectile.whoAmI);
            FrostJetFx.EmitBladeLoose(butt, tip, axis,
                Main.player[Projectile.owner].GetModPlayer<Weapons.SwordPlayer>().FireIntensity, 1f, source);
            if (Main.GameUpdateCount % 2 == 0)
                FrostJetFx.EmitJet(center, axis, default, source);
        }

        // Застрявший Frostbrand разгорается: аура растёт до предела, поджигает соседей,
        // а если клинок висит в враге — всё время обновляет ему Frostbite.
        private void UpdateLodgedFrostbrand(NPC lodged)
        {
            if (!IsFrostbrand)
                return;

            lodgedAuraTicks++;
            float grow = MathHelper.Clamp(lodgedAuraTicks / (float)LodgedAuraGrowTicks, 0f, 1f);
            float radius = MathHelper.Lerp(LodgedAuraMinRadius, LodgedAuraMaxRadius, grow);

            bool authority = Main.netMode != NetmodeID.MultiplayerClient || Projectile.owner == Main.myPlayer;
            if (authority && ++lodgedBurnPulse >= LodgedBurnPulse)
            {
                lodgedBurnPulse = 0;
                if (lodged != null && lodged.active)
                    Weapons.FrostFireSystem.TryIgnite(lodged, Main.player[Projectile.owner]);

                float r2 = radius * radius;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    NPC npc = Main.npc[i];
                    if (!Weapons.FrostFireSystem.CanBurn(npc))
                        continue;
                    if (Vector2.DistanceSquared(npc.Center, Projectile.Center) <= r2)
                        Weapons.FrostFireSystem.TryIgnite(npc, Main.player[Projectile.owner]);
                }
            }

            if (Main.dedServ)
                return;

            // Вторая, медленная стадия разгорания — только картинка: аура и
            // поджог предел уже набрали (grow за 2 с), а пламя на клинке,
            // если меч долго сидит, ещё ~8 секунд густеет, крупнеет и светит
            // сильнее (просьба владельца: «побольше эффекта, когда долго сидит»)
            float heat = MathHelper.Clamp((lodgedAuraTicks - LodgedAuraGrowTicks) / (float)LodgedHeatTicks, 0f, 1f);
            float light = 1f + 0.4f * heat;
            Lighting.AddLight(Projectile.Center,
                (0.25f + 0.45f * grow) * light,
                (0.45f + 0.7f * grow) * light,
                (0.7f + 0.9f * grow) * light);

            SickleSprite.Geometry geo = Geo;
            Vector2 axis = Projectile.rotation.ToRotationVector2();
            float half = geo.Length * 0.5f * Projectile.scale;
            Vector2 butt = Projectile.Center - axis * half;
            Vector2 tip = Projectile.Center + axis * half;
            // Огонь сидит НА клинке и разгорается с grow — и всё. Кольца клубков
            // по воздуху вокруг меча больше нет: огонь по воздуху не
            // распространяется (замечание владельца). Сама аура-поджог по
            // радиусу выше работает как работала — убрана только картинка
            // На второй половине «нагрева» пламени вдвое больше (второй проход),
            // а сами языки крупнее — до +70% размера при полном heat
            int passes = heat >= 0.5f ? 2 : 1;
            int source = FrostJetFx.LooseSource(Projectile.whoAmI);
            for (int pass = 0; pass < passes; pass++)
                FrostJetFx.EmitBladeLoose(butt, tip, axis, 0.35f + 0.65f * grow, 1f + 0.7f * heat, source);
            // Зона поджога видна тихим свечением того же радиуса (только картинка)
            FrostJetFx.LodgedAura(Projectile.Center, radius, 0.4f + 0.6f * grow);
        }

        private void CloseFrostTrail()
        {
            frostTrail?.Close();
            frostTrail = null;
        }

        public override void OnKill(int timeLeft)
        {
            CloseFrostTrail();
        }

        private void SpawnIceCrystal(int itemType, bool isKill)
        {
            // Кристалл — только Ice Blade: у Frostbrand ледяных пик не бывает
            if (itemType != ItemID.IceBlade) return;
            // Один усиленный пик из земли в точке втыкания вместо полоски
            // волны. Бросок убил — тот же кристалл, только крупнее и больнее
            int dir = Projectile.velocity.X >= 0 ? 1 : -1;
            if (Projectile.velocity.LengthSquared() < 1f) dir = Main.player[Projectile.owner].direction;
            // Кристалл наземный: ищем поверхность глубоко вниз.
            // Враг высоко в воздухе и земли рядом нет — кристалла не будет:
            // наземная атака не должна висеть в пустоте
            Vector2 spawn = Projectile.Center;
            Point start = spawn.ToTileCoordinates();
            bool found = false;
            for (int i = 0; i < 80; i++) {
                int y = start.Y + i;
                if (!WorldGen.InWorld(start.X, y, 2)) break;
                if (WorldGen.SolidTile(start.X, y)) { spawn.Y = y * 16f; found = true; break; }
            }
            if (!found) return;
            // Урон — полный урон броска: множители обычного и убийственного
            // кристалла внутри IceWaveSpike (0.6 и 1.0). Параметры пика:
            // ai[0]=1 — личный размер в полую силу; ai[1]=6 обычный кристалл
            // (спрайт 0, самый большой), 9 — за убийство; ai[2] — масштаб со
            // знаком направления наклона
            float packedStage = isKill ? 9f : 6f;
            float sizeMul = (isKill ? 0.47f : 0.35f) * dir;
            Projectile.NewProjectile(Projectile.GetSource_FromThis(), spawn, Vector2.Zero,
                ModContent.ProjectileType<IceWaveSpike>(), Projectile.damage, IceWaveSpike.SpawnKnockback, Projectile.owner,
                1f, packedStage, sizeMul);
            SoundEngine.PlaySound(SoundID.Item27 with { Volume = isKill ? 0.8f : 0.6f, Pitch = isKill ? 0f : 0.3f }, spawn);
            if (isKill) {
                // звук усиления
                SoundEngine.PlaySound(SoundID.Item30 with { Volume = 0.7f }, spawn);
            }
        }

        // Добитый броском враг оставляет большую долгую зону инея (FrostZoneSystem.Spawn);
        // размер и срок — от накопленной в копье шкалы, потом копьё размерзается (шкала в ноль)
        private void SpawnFrostZone(Vector2 center)
        {
            SicklePlayer sp = Main.player[Projectile.owner].GetModPlayer<SicklePlayer>();
            float level = Math.Max(frostAtThrow, sp.Frost);
            if (level < ZoneMinFrost)
                return;
            zoneSpawned = true;
            float radius = (ZoneRadiusBaseTiles + ZoneRadiusPerFrostTiles * level) * 16f;
            int ticks = (int)((ZoneSecondsBase + ZoneSecondsPerFrost * level) * 60f);
            FrostZoneSystem.Spawn(center, radius, ticks, level >= SicklePlayer.FrostStrongAt, true);

            sp.Frost = 0f;
            sp.FrostShown = 0f;
        }

        // Втыкается ОСТРИЁМ: внутрь цели ограничиваем положение кончика лезвия, а не
        // середины оси. Иначе у длинной косы середина прыгала бы в цель, а лезвие торчало
        // бы далеко за ней. Возвращает смещение СЕРЕДИНЫ оси от центра цели.
        private Vector2 TipLodgeOffset(NPC npc)
        {
            Vector2 toTip = Projectile.rotation.ToRotationVector2() * Geo.FrontReach;
            return ClampLodgeOffset(Projectile.Center + toTip - npc.Center, npc) - toTip;
        }

        private static Vector2 ClampLodgeOffset(Vector2 offset, NPC npc) =>
            new Vector2(
                MathHelper.Clamp(offset.X, -npc.width * 0.4f, npc.width * 0.4f),
                MathHelper.Clamp(offset.Y, -npc.height * 0.4f, npc.height * 0.4f));

        private float SettleProgress
        {
            get
            {
                float t = settleTimer / (float)SettleTicks;
                return 1f - (1f - t) * (1f - t);
            }
        }

        private void BeginLying()
        {
            lyingInit = true;
            State = StateLying;
            harmless = true;
            Projectile.friendly = false;

            SickleSprite.Geometry geo = Geo;
            float heading = Projectile.rotation;
            bool tipRight = Math.Cos(heading) >= 0f;
            float target = tipRight ? 0f : MathHelper.Pi;
            settleFromHeading = heading;
            settleToHeading = heading + MathHelper.WrapAngle(target - heading);
            settleTimer = 0;

            lyingBelow = tipRight ? geo.SidePos : geo.SideNeg;
            float above = tipRight ? geo.SideNeg : geo.SidePos;

            Vector2 oldCenter = Projectile.Center;
            Vector2 bottom = Projectile.Bottom;
            // Лежащая коса — коробка по всей видимой длине (древко и лезвие торчат за центроидами)
            int width = Math.Max(FlightBox, (int)Math.Ceiling(geo.Length + geo.RearOverhang + geo.FrontOverhang));
            int height = Math.Max(4, (int)Math.Ceiling(lyingBelow + above));

            for (int shift = 0; shift <= width / 2; shift += 2)
            {
                if (TryPlaceBox(bottom, width, height, shift) || (shift > 0 && TryPlaceBox(bottom, width, height, -shift)))
                    break;
            }

            settleFromOffset = oldCenter - Projectile.Bottom;
            Projectile.netUpdate = true;
        }

        private bool TryPlaceBox(Vector2 bottom, int width, int height, int shift)
        {
            Vector2 pos = new Vector2(bottom.X - width * 0.5f + shift, bottom.Y - height);
            if (Collision.SolidCollision(pos - new Vector2(0f, 0.1f), width, height))
                return false;
            Projectile.position = pos;
            Projectile.width = width;
            Projectile.height = height;
            return true;
        }

        public override bool TileCollideStyle(ref int width, ref int height, ref bool fallThrough, ref Vector2 hitboxCenterFrac)
        {
            fallThrough = false;
            return true;
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            bool hitX = Math.Abs(Projectile.velocity.X - oldVelocity.X) > 0.01f;
            bool hitY = Math.Abs(Projectile.velocity.Y - oldVelocity.Y) > 0.01f;

            if (State == StateLying)
            {
                if (hitX)
                    Projectile.velocity.X = 0f;
                if (hitY)
                    Projectile.velocity.Y = 0f;
                return false;
            }
            if (State != StateFlying)
                return false;

            // Острие вперёд в мягкий тайл — втыкается
            if (TryLodgeInTile(oldVelocity))
                return false;

            float impact = 0f;
            if (hitX)
            {
                impact = Math.Max(impact, Math.Abs(oldVelocity.X));
                Projectile.velocity.X = -oldVelocity.X * WallBounce;
                spinDir = -spinDir;
                spinScale *= 0.6f;
                harmless = true;
            }
            if (hitY)
            {
                harmless = true;
                if (oldVelocity.Y > 0f)
                {
                    impact = Math.Max(impact, oldVelocity.Y);
                    Projectile.velocity.X *= GroundFriction;
                    if (oldVelocity.Y > MinBounceSpeed)
                    {
                        Projectile.velocity.Y = -oldVelocity.Y * GroundBounce;
                        spinScale *= 0.5f;
                    }
                    else
                    {
                        Projectile.velocity.Y = 0f;
                        State = StateLying;
                    }
                }
                else
                {
                    impact = Math.Max(impact, -oldVelocity.Y);
                    Projectile.velocity.Y = -oldVelocity.Y * 0.2f;
                }
            }

            if (impact > 2f)
                SoundEngine.PlaySound(SoundID.Dig with { Volume = MathHelper.Clamp(impact / 12f, 0.2f, 0.8f), Pitch = 0.4f }, Projectile.Center);
            Projectile.friendly = false;
            Projectile.netUpdate = true;
            return false;
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            if (State != StateFlying)
                return false;

            // Бьёт видимое лезвие: зона по пикселям серпа в позе, которую рисует PreDraw
            // (Center — середина оси приклад→острие, ось — Projectile.rotation, без зеркала)
            SickleSprite.BladeZone zone = SickleSprite.GetZone((int)Projectile.ai[2],
                FrozenSpear.UseZone(Main.player[Projectile.owner], (int)Projectile.ai[2]));
            if (zone != null)
            {
                if (SickleSprite.BladeOverlaps(zone, targetHitbox, Projectile.Center,
                    Projectile.rotation, Projectile.rotation, false, Projectile.scale))
                    return true;
                return false;
            }

            // Запас без рисунка: отрезок от центроида до центроида
            Vector2 axis = Projectile.rotation.ToRotationVector2();
            float half = Geo.Length * 0.5f;
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                Projectile.Center - axis * half, Projectile.Center + axis * half, HitWidth, ref collisionPoint);
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            // Чистый удар («лезвием по ходу», ±28°) — полный урон.
            // Втыкание (и волна ледяного меча): остриё в передней половине
            // кувырка И врага коснулся передний кусок клинка у кончика.
            // Серединой/рукоятью — слабый удар и отскок без втыкания
            cleanNpcHit = IsCleanHit(Projectile.velocity);
            stickNpcHit = IsPointHit(Projectile.velocity) && TipSectionTouches(target);
            if (!cleanNpcHit)
                modifiers.SourceDamage *= SideHitDamageMult;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            int thrownItem = (int)Projectile.ai[2];
            if (Weapons.SwordTypes.IsSwordId(thrownItem)) {
                if (thrownItem == ItemID.IceBlade)
                    Main.player[Projectile.owner].GetModPlayer<Weapons.SwordPlayer>().AddPolish(thrownItem);
                // Фростбранд: летящий меч сам НЕ поджигает. Убил горящего — взрыв
                // сильнее и шире тычкового. Поджигает застрявший клинок своей
                // разгорающейся аурой (UpdateLodgedFrostbrand)
                if (thrownItem == ItemID.Frostbrand) {
                    bool wasBurning = Weapons.FrostFireSystem.WasBurning(target);
                    if (wasBurning && target.life <= 0 && Projectile.owner == Main.myPlayer)
                        Weapons.FrostFireSystem.Explode(target, 200f, Projectile.damage * 7 / 10, Projectile.owner, true);
                }
                // Кристалл льда — когда меч втыкается остриём (а не чиркает боком):
                // то же условие, что и само втыкание ниже, иначе меч торчит
                // во враге, а кристалла нет — «эффект не срабатывает»
                if (stickNpcHit && Projectile.owner == Main.myPlayer) {
                    if (IsAisberg) {
                        Vector2 tip = Projectile.Center + Projectile.rotation.ToRotationVector2() * Geo.FrontReach;
                        AisbergChunk.SpawnThrowChunks(Main.player[Projectile.owner],
                            Projectile.GetSource_FromThis(), tip, Projectile.damage, Projectile.knockBack);
                    }
                    else {
                        bool isKill = target.life <= 0;
                        SpawnIceCrystal(thrownItem, isKill);
                    }
                }
            }
            if (IsIce && !zoneSpawned && Projectile.owner == Main.myPlayer && target.life <= 0)
                SpawnFrostZone(target.Center);

            if (!stickNpcHit)
            {
                // Остриём не достал: рукоятью, плашмя или серединой клинка.
                // Отскакивает от врага и падает, не втыкаясь
                Vector2 away = (Projectile.Center - target.Center).SafeNormalize(-Vector2.UnitY);
                Projectile.velocity = away * Math.Max(Projectile.velocity.Length() * 0.3f, 2f) + new Vector2(0f, -1.5f);
                spinDir = -spinDir;
                spinScale *= 0.6f;
                harmless = true;
                Projectile.friendly = false;
                SoundEngine.PlaySound(SoundID.Dig with { Volume = 0.5f, Pitch = 0.4f }, Projectile.Center);
                Projectile.netUpdate = true;
                return;
            }

            // Застреваем во враге как прилетел: кончик внутри цели
            // (TipLodgeOffset), угол — настоящий угол попадания.
            // Доворота до оси полёта нет — это костыль (замечание владельца)
            Projectile.velocity = Vector2.Zero;
            Projectile.ai[0] = target.whoAmI;
            State = StateLodged;
            harmless = true;
            Projectile.friendly = false;
            Projectile.tileCollide = false;
            lodgedAuraTicks = 0;
            lodgedBurnPulse = 0;
            lodgeInit = true;
            lodgeOffset = TipLodgeOffset(target);
            Projectile.netUpdate = true;
        }

        private void TryPickup()
        {
            if (Projectile.owner != Main.myPlayer || (State != StateLying && State != StateLodged && State != StateStuckTile) ||
                Projectile.localAI[0] <= PickupDelay)
                return;
            Player player = Main.player[Projectile.owner];
            if (!player.active || player.dead)
                return;

            if (PickupGap(player.Hitbox) >= PickupRange)
                return;

            BeginPickup();
            Projectile.netUpdate = true;
        }

        // Расстояние от игрока до косы (px). Лежит — коробка по длине рисунка.
        // Воткнута в блок или застряла во враге — вся видимая коса от древка до лезвия:
        // её хитбокс 12×12 стоит в середине оси, и за торчащее лезвие или древко не взять.
        private float PickupGap(Rectangle pBox)
        {
            if (State == StateLying)
            {
                Rectangle box = Projectile.Hitbox;
                float gapX = Math.Max(0, Math.Max(box.Left - pBox.Right, pBox.Left - box.Right));
                float gapY = Math.Max(0, Math.Max(box.Top - pBox.Bottom, pBox.Top - box.Bottom));
                return (float)Math.Sqrt(gapX * gapX + gapY * gapY);
            }

            SickleSprite.Geometry geo = Geo;
            Vector2 axis = Projectile.rotation.ToRotationVector2();
            Vector2 rear = Projectile.Center - axis * geo.RearReach;
            Vector2 front = Projectile.Center + axis * geo.FrontReach;
            int steps = Math.Max(1, (int)Math.Ceiling(Vector2.Distance(rear, front) / 4f));
            float bestSq = float.MaxValue;
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(rear, front, i / (float)steps);
                float gapX = Math.Max(0f, Math.Max(pBox.Left - p.X, p.X - pBox.Right));
                float gapY = Math.Max(0f, Math.Max(pBox.Top - p.Y, p.Y - pBox.Bottom));
                bestSq = Math.Min(bestSq, gapX * gapX + gapY * gapY);
            }
            return (float)Math.Sqrt(bestSq);
        }

        private void BeginPickup()
        {
            Vector2 visual = VisualCenter(Geo);
            State = StatePickup;
            Projectile.ai[0] = -1f;
            Projectile.width = FlightBox;
            Projectile.height = FlightBox;
            Projectile.Center = visual;
            Projectile.velocity = new Vector2(0f, -PickupStartSpeed);
            pickupTimer = 0;
        }

        private void PickupAI()
        {
            Projectile.friendly = false;
            Projectile.tileCollide = false;
            Player player = Main.player[Projectile.owner];
            pickupTimer++;

            Vector2 toPlayer = player.MountedCenter - Projectile.Center;
            float dist = toPlayer.Length();
            bool owner = Projectile.owner == Main.myPlayer;
            if (dist < PickupCatchDist || pickupTimer > PickupMaxTicks || !player.active || player.dead)
            {
                if (owner)
                {
                    SoundEngine.PlaySound(SoundID.Grab, player.Center);
                    Projectile.Kill();
                }
                else
                {
                    Projectile.velocity = Vector2.Zero;
                }
                return;
            }

            float speed = Math.Min(PickupStartSpeed + PickupAccel * pickupTimer, PickupMaxSpeed);
            Projectile.velocity = Vector2.Lerp(Projectile.velocity, toPlayer / dist * Math.Min(speed, dist), 0.35f);
            Projectile.rotation += 0.3f * spinDir;
            Projectile.scale = MathHelper.Clamp(dist / 48f, 0.5f, 1f);
        }

        private int pickupTimer;

        private Vector2 VisualCenter(SickleSprite.Geometry geo) =>
            State == StateLying && lyingInit
                ? Projectile.Bottom + Vector2.Lerp(settleFromOffset, new Vector2(0f, -lyingBelow), SettleProgress)
                : Projectile.Center;

        public override void DrawBehind(int index, List<int> behindNPCsAndTiles, List<int> behindNPCs,
            List<int> behindProjectiles, List<int> overPlayers, List<int> overWiresUI)
        {
            // Куда именно «под»: во враге — под NPC (тело перекрывает вошедший
            // клинок), в земле — под блоки (земля перекрывает закопанный кончик)
            if (State == StateLodged)
                behindNPCs.Add(index);
            else if (State == StateStuckTile)
                behindNPCsAndTiles.Add(index);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            int itemType = (int)Projectile.ai[2];
            if (itemType <= 0)
                return true;
            Texture2D texture = SickleTextures.Normal(itemType);
            if (texture == null || texture.IsDisposed)
                return true;

            SickleSprite.Geometry geo = SickleSprite.Get(itemType);
            float heading = Projectile.rotation;
            Vector2 axis = heading.ToRotationVector2();
            Vector2 center = VisualCenter(geo);
            float scale = Projectile.scale;
            Vector2 butt = center - axis * (geo.Length * 0.5f * scale);

            if (itemType == ItemID.IceBlade) {
                float mix = Content.Weapons.IceBladeVisual.Mix(Main.player[Projectile.owner], itemType);
                Content.Weapons.IceBladeVisual.DrawBlend(texture, butt, geo, heading - geo.AngleFor(false), false, lightColor, scale, mix);
                return false;
            }

            FrozenSpear.DrawBlend(texture, butt, geo, heading - geo.AngleFor(false), false, lightColor, scale,
                FrozenSpear.Mix(Main.player[Projectile.owner], itemType));

            // Леденец сереет по мере потраченного заряда сладости: серый спрайт
            // поверх той же позы (полный заряд — красный, пустой — серый).
            // Серый спрайт в ванильном размере 44x70, поэтому геометрия своя,
            // а масштаб подогнан под длину нынешнего клинка
            if (itemType == ItemID.CandyCaneSword &&
                CandyOwner.SweetTicks < Weapons.CandyCaneLureNPC.MaxSweetTicks)
            {
                candyGrayAsset ??= ModContent.Request<Texture2D>(
                    "FrostMelee/Items/Candy Cane Sword gray", AssetRequestMode.ImmediateLoad);
                Texture2D grayTex = candyGrayAsset.Value;
                if (grayTex != null && !grayTex.IsDisposed)
                {
                    candyGrayGeo ??= SickleSprite.GetSwordGeometry(grayTex);
                    SickleSprite.Geometry gGeo = candyGrayGeo.Value;
                    float gray = 1f - CandyOwner.SweetFrac;
                    float gScale = gGeo.Length > 1f ? scale * geo.Length / gGeo.Length : scale;
                    Main.EntitySpriteDraw(grayTex, butt - Main.screenPosition, null, lightColor * gray,
                        heading - gGeo.AngleFor(false), gGeo.ButtFor(false), gScale, SpriteEffects.None, 0f);
                }
            }
            return false;
        }
    }
}
