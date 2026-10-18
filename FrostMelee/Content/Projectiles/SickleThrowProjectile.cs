using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

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
        private const float StickEmbedDepth = 6f;
        // Авто-возврат через N тиков; 0 = никогда (доктрина: подбери сам)
        private const int AutoReturnTicks = 0;

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

        // Лучи от хитбокса вдоль полёта: центр и углы
        private static readonly Vector2[] ProbeOffsets = {
            Vector2.Zero, new Vector2(-5f, -5f), new Vector2(5f, -5f), new Vector2(-5f, 5f), new Vector2(5f, 5f)
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

        private FrostTrailSystem.Trail frostTrail;
        private Vector2 lastTrailPos;
        private float frostAtThrow;     // шкала заморозки владельца в момент броска (только владелец)
        private int launchTicks;      // тиков полёта с торможением ease-out
        private bool zoneSpawned;       // зона один раз за бросок

        private bool IsIce => (int)Projectile.ai[2] == ItemID.IceSickle;

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
                return;
            }

            // Враг умер — серп выпадает и падает на землю
            Projectile.ai[0] = -1f;
            State = StateFlying;
            harmless = true;
            spinScale = 0.4f;
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
                return;

            // Опоры нет — серп выпадает
            stuckInit = false;
            State = StateFlying;
            harmless = true;
            spinScale = 0.4f;
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
            // Кувыркающаяся после отскока/падения коса острием не летит: ось ≠ ход полёта, воткнув её
            // по оси, тело оказывалось целиком внутри блока. Втыкается только летящая остриём вперёд
            if (harmless)
                return false;
            if (velocity.Length() < StickMinSpeed)
                return false;
            if (AlwaysTipFirst)
                return true;
            return Math.Abs(MathHelper.WrapAngle(velocity.ToRotation() - Projectile.rotation)) <= StickAngle;
        }

        private bool TryLodgeInTile(Vector2 oldVelocity)
        {
            if (!IsCleanHit(oldVelocity))
                return false;

            Vector2 dir = oldVelocity.SafeNormalize(Vector2.UnitY);
            float reach = oldVelocity.Length() + FlightBox;
            float nearest = float.MaxValue, centerDist = -1f;
            float[] dists = new float[ProbeOffsets.Length];
            int[] types = new int[ProbeOffsets.Length];
            for (int i = 0; i < ProbeOffsets.Length; i++)
            {
                dists[i] = -1f;
                for (float d = 0f; d <= reach; d += 1f)
                {
                    if (!SolidTileAt(Projectile.Center + ProbeOffsets[i] + dir * d, out _, out types[i]))
                        continue;
                    dists[i] = d;
                    nearest = Math.Min(nearest, d);
                    break;
                }
            }
            if (nearest == float.MaxValue)
                return false;

            for (int i = 0; i < ProbeOffsets.Length; i++)
            {
                if (dists[i] >= 0f && dists[i] <= nearest + 4f && !SoftTiles.Contains(types[i]))
                    return false; // камень, руда, кирпич, платформа: отскок
            }
            centerDist = dists[0] >= 0f ? dists[0] : nearest + 5f;

            SickleSprite.Geometry geo = Geo;
            Vector2 axis = Projectile.rotation.ToRotationVector2();
            float embed = Math.Min(StickEmbedDepth, geo.Length * 0.4f);
            Vector2 contact = Projectile.Center + dir * centerDist;

            SoundEngine.PlaySound(SoundID.Dig with { Volume = 0.7f, Pitch = 0.2f }, contact);
            // Острие (самая дальняя точка лезвия), а не центроид: рисунок торчит за ним на FrontOverhang
            Projectile.Center = contact - axis * (geo.FrontReach - embed);
            Projectile.velocity = Vector2.Zero;
            Projectile.ai[0] = -1f;
            State = StateStuckTile;
            stuckInit = false;
            harmless = true;
            Projectile.friendly = false;
            Projectile.tileCollide = false;
            Projectile.netUpdate = true;
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

        private void CloseFrostTrail()
        {
            frostTrail?.Close();
            frostTrail = null;
        }

        public override void OnKill(int timeLeft)
        {
            CloseFrostTrail();
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
                return SickleSprite.BladeOverlaps(zone, targetHitbox, Projectile.Center,
                    Projectile.rotation, Projectile.rotation, false, Projectile.scale);

            // Запас без рисунка: отрезок от центроида до центроида
            Vector2 axis = Projectile.rotation.ToRotationVector2();
            float half = Geo.Length * 0.5f;
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                Projectile.Center - axis * half, Projectile.Center + axis * half, HitWidth, ref collisionPoint);
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            cleanNpcHit = IsCleanHit(Projectile.velocity);
            if (!cleanNpcHit)
                modifiers.SourceDamage *= SideHitDamageMult;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            if (IsIce && !zoneSpawned && Projectile.owner == Main.myPlayer && target.life <= 0)
                SpawnFrostZone(target.Center);

            if (!cleanNpcHit)
            {
                // Не острием: отскакивает от врага и падает, не втыкаясь
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

            // Застреваем во враге остриём по ходу полёта
            if (Projectile.velocity != Vector2.Zero)
                Projectile.rotation = Projectile.velocity.ToRotation();
            Projectile.velocity = Vector2.Zero;
            Projectile.ai[0] = target.whoAmI;
            State = StateLodged;
            harmless = true;
            Projectile.friendly = false;
            Projectile.tileCollide = false;
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

            FrozenSpear.DrawBlend(texture, butt, geo, heading - geo.AngleFor(false), false, lightColor, scale,
                FrozenSpear.Mix(Main.player[Projectile.owner], itemType));
            return false;
        }
    }
}
