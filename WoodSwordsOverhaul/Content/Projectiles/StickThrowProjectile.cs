using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace WoodSwordsOverhaul.Content.Projectiles
{
    // Бросок: палка летит по баллистике и кувыркается, бьётся о землю,
    // пару раз подпрыгивает и ложится плашмя — нижним краем спрайта на землю.
    // Втыкается (во врага и в мягкий тайл) только если коснулась острием вперёд, на достаточной
    // скорости: угол между полётом и осью клинка в допуске StickAngle. Боком/торцом — отскок,
    // по врагу такой удар вдвое слабее. В камень, руду, кирпичи не втыкается — отскок.
    // Враг умер или блок выкопали — палка падает.
    // Забрать — подойти вплотную: палка подлетает в руки. Сам предмет остаётся в инвентаре, но этот
    // экземпляр заблокирован (ThrownLockItem / StickPlayer.IsLocked), пока палка лежит.
    // Ранит только в чистом полёте: после первого удара о землю/стену/врага — нет
    // (лежащая палка не ловушка). Урон — обычный урон оружия, без бонусов.
    //
    // ai[0] — индекс NPC, в котором застряла (-1 = нет)
    // ai[1] — состояние: 0 летит, 1 лежит, 2 застряла во враге, 3 подлетает в руки (подбор),
    //         4 воткнулась в тайл
    // ai[2] — тип предмета (для отрисовки)
    // rotation — мировой угол оси «приклад → остриё» (не угол спрайта)
    public class StickThrowProjectile : ModProjectile
    {
        // === Константы броска (крутить здесь) ===
        public const float ThrowSpeed = 12f;        // начальная скорость (px/тик)
        private const float Gravity = 0.25f;        // тянет вниз с первого тика — честная дуга
        private const float MaxFallSpeed = 14f;
        private const float AirDrag = 0.995f;       // горизонтальное сопротивление воздуха за тик
        private const float SpinPerSpeed = 0.035f;  // кувырок: рад/тик на 1 px/тик скорости
        private const float MaxSpin = 0.4f;
        private const float MinDamageSpeed = 5f;    // медленная палка не ранит
        private const float GroundBounce = 0.3f;    // доля вертикальной скорости после удара о землю
        private const float MinBounceSpeed = 3f;    // слабее — не подпрыгивает, а ложится
        private const float GroundFriction = 0.6f;  // горизонтальная скорость после удара о землю
        private const float WallBounce = 0.3f;      // отскок от стены
        private const float LyingFriction = 0.8f;   // скольжение лёжа (множитель за тик)
        private const int SettleTicks = 8;          // за сколько тиков палка заваливается плашмя
        private const int FlightBox = 12;           // коллизия с блоками в полёте (px)
        private const float HitWidth = 6f;          // толщина бьющей оси в полёте (px)
        private const float PickupRange = 24f;      // подобрать: от края персонажа до палки (px), почти вплотную
        private const int PickupDelay = 20;         // тиков после броска, пока подобрать нельзя
        private const float PickupStartSpeed = 3f;  // анимация подбора: палка подлетает в руки
        private const float PickupAccel = 1.2f;     // прибавка скорости за тик
        private const float PickupMaxSpeed = 16f;
        private const float PickupCatchDist = 12f;  // ближе этого к центру персонажа — в инвентаре
        private const int PickupMaxTicks = 45;      // страховка: дольше — забираем сразу
        private const float StickAngle = 0.49f;     // допуск острия к направлению полёта (~28°)
        private const float StickMinSpeed = 7f;     // медленнее — не втыкается
        private const float SideHitDamageMult = 0.5f; // боковой/торцевой/слабый удар по врагу
        private const float StickEmbedDepth = 6f;   // на сколько острие уходит в тайл (px)
        // Авто-возврат через N тиков; 0 = никогда (доктрина: бросок не окупается, подбери сам)
        private const int AutoReturnTicks = 0;

        private const int StateFlying = 0, StateLying = 1, StateLodged = 2, StatePickup = 3, StateStuckTile = 4;

        // Мягкие тайлы, в которые втыкается острие: земля, грязь/ил, песок, глина, снег, трава
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

        // Номер броска для блокировки предмета (задаёт StickPlayer, только у владельца)
        public int Token;

        // Страховочная текстура (своя отрисовка всё равно по ai[2])
        public override string Texture => "WoodSwordsOverhaul/Content/Items/Sticks/WoodStick";

        private int State
        {
            get => (int)Projectile.ai[1];
            set => Projectile.ai[1] = value;
        }

        private StickSprite.Geometry Geo => StickSprite.Get((int)Projectile.ai[2]);

        // Локальное состояние: каждый клиент считает его сам
        private bool initialized;
        private bool harmless;        // уже ударилась — больше не ранит
        private float spinDir = 1f;   // направление кувырка
        private float spinScale = 1f; // после отскоков крутится медленнее
        private bool lyingInit;
        private int settleTimer;
        private float settleFromHeading, settleToHeading;
        private Vector2 settleFromOffset; // визуальный центр относительно низа хитбокса в момент касания
        private float lyingBelow;         // от оси до нижнего края лежащего спрайта (px)
        private bool lodgeInit;
        private Vector2 lodgeOffset;
        private bool stuckInit;       // опора воткнутой в тайл найдена
        private Point stuckTile;      // тайл, в который воткнулась
        private bool cleanNpcHit;     // удар по врагу острием вперёд на скорости

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
            Projectile.ignoreWater = false; // в воде летит медленнее
            Projectile.netImportant = true; // лежит долго — новые игроки тоже должны её видеть
            Projectile.timeLeft = AutoReturnTicks > 0 ? AutoReturnTicks : 2;
            Projectile.aiStyle = -1;
        }

        public override void AI()
        {
            if (!initialized)
            {
                initialized = true;
                spinDir = Projectile.velocity.X >= 0f ? 1f : -1f;
                // Летит остриём вперёд, дальше кувыркается
                if (Projectile.velocity != Vector2.Zero)
                    Projectile.rotation = Projectile.velocity.ToRotation();
            }
            Projectile.localAI[0]++;
            if (AutoReturnTicks <= 0)
                Projectile.timeLeft = 2;

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
            Projectile.velocity.X *= AirDrag;
            Projectile.velocity.Y = Math.Min(Projectile.velocity.Y + Gravity, MaxFallSpeed);

            // Кувырок: чем быстрее летит, тем быстрее крутится
            // (метательные копья без кувырка: остриё всегда по полёту)
            float speed = Projectile.velocity.Length();
            if (!harmless && AlwaysTipFirst)
                Projectile.rotation = Projectile.velocity.ToRotation();
            else
                Projectile.rotation += Math.Min(speed * SpinPerSpeed, MaxSpin) * spinScale * spinDir;

            Projectile.friendly = !harmless && speed >= MinDamageSpeed;
        }

        private void LyingAI()
        {
            if (!lyingInit)
                BeginLying(); // только что упала (или клиент узнал о лежании из синхронизации)

            Projectile.friendly = false;
            Projectile.tileCollide = true;
            // Лёжа немного скользит и останавливается; гравитация остаётся —
            // если землю из-под палки выкопать, она упадёт ниже
            Projectile.velocity.X *= LyingFriction;
            if (Math.Abs(Projectile.velocity.X) < 0.05f)
                Projectile.velocity.X = 0f;
            Projectile.velocity.Y = Math.Min(Projectile.velocity.Y + Gravity, MaxFallSpeed);

            // Заваливается плашмя: угол плавно к горизонтали
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
                    lodgeOffset = ClampLodgeOffset(Projectile.Center - npc.Center, npc);
                }
                // Едет вместе с врагом там, куда воткнулась
                Projectile.Center = npc.Center + lodgeOffset;
                return;
            }

            // Враг умер или исчез — палка выпадает и падает на землю
            Projectile.ai[0] = -1f;
            State = StateFlying;
            harmless = true;
            spinScale = 0.4f;
            Projectile.velocity = new Vector2(0f, -1.5f);
            Projectile.tileCollide = true;
            Projectile.netUpdate = true;
        }

        // Воткнулась в тайл: стоит на месте; выкопали блок — падает
        private void StuckTileAI()
        {
            Projectile.friendly = false;
            Projectile.tileCollide = false;
            Projectile.velocity = Vector2.Zero;

            if (!stuckInit)
            {
                // Опора — блок у острия (клиент мог узнать о втыкании из синхронизации)
                stuckInit = true;
                Vector2 tip = Projectile.Center + Projectile.rotation.ToRotationVector2() * (Geo.Length * 0.5f);
                Vector2 axis = Projectile.rotation.ToRotationVector2();
                bool found = false;
                for (float d = -4f; d <= 6f && !found; d += 1f)
                    found = SolidTileAt(tip + axis * d, out stuckTile, out _);
                if (!found)
                    stuckTile = new Point(-1, -1);
            }

            if (stuckTile.X >= 0 && SolidTileAt(stuckTile.ToWorldCoordinates(8f, 8f), out _, out _))
                return;

            // Опоры нет (блок выкопали) — палка выпадает
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

        private bool AlwaysTipFirst => StickTypes.ThrownAlwaysTipFirst((int)Projectile.ai[2]);

        // Чистый удар: остриём вперёд (угол между полётом и осью клинка в допуске) и
        // на достаточной скорости. Иначе — отскок (и вдвое слабее по врагу)
        private bool IsCleanHit(Vector2 velocity)
        {
            if (velocity.Length() < StickMinSpeed)
                return false;
            if (AlwaysTipFirst)
                return true;
            return Math.Abs(MathHelper.WrapAngle(velocity.ToRotation() - Projectile.rotation)) <= StickAngle;
        }

        // Втыкание в тайл: идём лучами вдоль полёта, находим ближайшую поверхность;
        // втыкаемся, только если все задетые блоки мягкие. Острие уходит на StickEmbedDepth.
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
                return false; // не нашли, во что упёрлись — отскок

            for (int i = 0; i < ProbeOffsets.Length; i++)
            {
                if (dists[i] >= 0f && dists[i] <= nearest + 4f && !SoftTiles.Contains(types[i]))
                    return false; // камень, руда, кирпич, платформа: отскок
            }
            centerDist = dists[0] >= 0f ? dists[0] : nearest + 5f;

            StickSprite.Geometry geo = Geo;
            Vector2 axis = Projectile.rotation.ToRotationVector2();
            float embed = Math.Min(StickEmbedDepth, geo.Length * 0.4f);
            Vector2 contact = Projectile.Center + dir * centerDist;

            SoundEngine.PlaySound(SoundID.Dig with { Volume = 0.7f, Pitch = 0.2f }, contact);
            Projectile.Center = contact - axis * (geo.Length * 0.5f - embed); // острие на embed за поверхностью
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

        private static Vector2 ClampLodgeOffset(Vector2 offset, NPC npc) =>
            new Vector2(
                MathHelper.Clamp(offset.X, -npc.width * 0.4f, npc.width * 0.4f),
                MathHelper.Clamp(offset.Y, -npc.height * 0.4f, npc.height * 0.4f));

        private float SettleProgress
        {
            get
            {
                float t = settleTimer / (float)SettleTicks;
                return 1f - (1f - t) * (1f - t); // ease-out: быстро падает, мягко ложится
            }
        }

        // Переход «упала → лежит»: хитбокс становится ровно лежащим спрайтом
        // (длина палки × её толщина), низом на земле. Раньше хитбокс 24×24 стоял
        // центром на земле, и короткие заточки висели над ним в воздухе.
        private void BeginLying()
        {
            lyingInit = true;
            State = StateLying;
            harmless = true;
            Projectile.friendly = false;

            StickSprite.Geometry geo = Geo;
            float heading = Projectile.rotation;
            bool tipRight = Math.Cos(heading) >= 0f;
            float target = tipRight ? 0f : MathHelper.Pi;
            settleFromHeading = heading;
            settleToHeading = heading + MathHelper.WrapAngle(target - heading);
            settleTimer = 0;

            // Какая сторона спрайта внизу, зависит от того, куда смотрит остриё
            lyingBelow = tipRight ? geo.SidePos : geo.SideNeg;
            float above = tipRight ? geo.SideNeg : geo.SidePos;

            Vector2 oldCenter = Projectile.Center;
            Vector2 bottom = Projectile.Bottom;
            int width = Math.Max(FlightBox, (int)Math.Ceiling(geo.Length));
            int height = Math.Max(4, (int)Math.Ceiling(lyingBelow + above));

            // Ставим широкий хитбокс так, чтобы он не залез в стену рядом
            // (упала у стены — сдвигаем вбок; не влезает совсем — остаётся узкий)
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
            // Проверяем чуть выше: низ стоит ровно на земле, это не пересечение
            if (Collision.SolidCollision(pos - new Vector2(0f, 0.1f), width, height))
                return false;
            Projectile.position = pos;
            Projectile.width = width;
            Projectile.height = height;
            return true;
        }

        public override bool TileCollideStyle(ref int width, ref int height, ref bool fallThrough, ref Vector2 hitboxCenterFrac)
        {
            fallThrough = false; // ложится и на платформы
            return true;
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            bool hitX = Math.Abs(Projectile.velocity.X - oldVelocity.X) > 0.01f;
            bool hitY = Math.Abs(Projectile.velocity.Y - oldVelocity.Y) > 0.01f;

            if (State == StateLying)
            {
                // Лежит: просто стоит на земле / упирается в стену
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
                // Стена: слабый отскок назад, кувырок в обратную сторону
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
                    // Земля: подпрыгнуть, если удар сильный; иначе лечь
                    impact = Math.Max(impact, oldVelocity.Y);
                    Projectile.velocity.X *= GroundFriction;
                    if (oldVelocity.Y > MinBounceSpeed)
                    {
                        Projectile.velocity.Y = -oldVelocity.Y * GroundBounce;
                        spinScale *= 0.5f;
                    }
                    else
                    {
                        // Ляжет в следующем AI (там же меняется хитбокс —
                        // не посреди ванильного шага движения)
                        Projectile.velocity.Y = 0f;
                        State = StateLying;
                    }
                }
                else
                {
                    // Потолок: гасим подъём, падает дальше
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

        // В полёте бьёт ось палки по её реальной длине (как нарисована), а не квадрат
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            if (State != StateFlying)
                return false;
            Vector2 axis = Projectile.rotation.ToRotationVector2();
            float half = Geo.Length * 0.5f;
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                Projectile.Center - axis * half, Projectile.Center + axis * half, HitWidth, ref collisionPoint);
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            // Бок/торец или слабый бросок: урон вдвое меньше. Бонусов за бросок нет нигде
            cleanNpcHit = IsCleanHit(Projectile.velocity);
            if (!cleanNpcHit)
                modifiers.SourceDamage *= SideHitDamageMult;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
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

            // Застреваем во враге остриём по ходу полёта и больше не бьём
            if (Projectile.velocity != Vector2.Zero)
                Projectile.rotation = Projectile.velocity.ToRotation();
            Projectile.velocity = Vector2.Zero;
            Projectile.ai[0] = target.whoAmI;
            State = StateLodged;
            harmless = true;
            Projectile.friendly = false;
            Projectile.tileCollide = false;
            lodgeInit = true;
            lodgeOffset = ClampLodgeOffset(Projectile.Center - target.Center, target);
            Projectile.netUpdate = true;
        }

        private void TryPickup()
        {
            // Решает только владелец (состояние синхронизируется); летящую — нельзя
            if (Projectile.owner != Main.myPlayer || (State != StateLying && State != StateLodged && State != StateStuckTile) ||
                Projectile.localAI[0] <= PickupDelay)
                return;
            Player player = Main.player[Projectile.owner];
            if (!player.active || player.dead)
                return;

            // Зазор между хитбоксом персонажа и хитбоксом палки (у лежащей он ровно
            // по спрайту): подойти почти вплотную, а не за полэкрана
            Rectangle pBox = player.Hitbox;
            Rectangle box = Projectile.Hitbox;
            float gapX = Math.Max(0, Math.Max(box.Left - pBox.Right, pBox.Left - box.Right));
            float gapY = Math.Max(0, Math.Max(box.Top - pBox.Bottom, pBox.Top - box.Bottom));
            if (gapX * gapX + gapY * gapY >= PickupRange * PickupRange)
                return;

            BeginPickup();
            Projectile.netUpdate = true;
        }

        // Подбор: палка срывается с места и подлетает в руки, как ванильный предмет
        private void BeginPickup()
        {
            Vector2 visual = VisualCenter(Geo);
            State = StatePickup;
            Projectile.ai[0] = -1f;
            Projectile.width = FlightBox;
            Projectile.height = FlightBox;
            Projectile.Center = visual;
            Projectile.velocity = new Vector2(0f, -PickupStartSpeed); // чуть подскакивает
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
                    Projectile.velocity = Vector2.Zero; // ждём Kill от владельца
                }
                return;
            }

            // Разгоняется к персонажу и не проскакивает мимо
            float speed = Math.Min(PickupStartSpeed + PickupAccel * pickupTimer, PickupMaxSpeed);
            Projectile.velocity = Vector2.Lerp(Projectile.velocity, toPlayer / dist * Math.Min(speed, dist), 0.35f);
            Projectile.rotation += 0.3f * spinDir;
            // У самых рук уменьшается — «ушла в инвентарь»
            Projectile.scale = MathHelper.Clamp(dist / 48f, 0.5f, 1f);
        }

        private int pickupTimer;

        private Vector2 VisualCenter(StickSprite.Geometry geo) =>
            State == StateLying && lyingInit
                ? Projectile.Bottom + Vector2.Lerp(settleFromOffset, new Vector2(0f, -lyingBelow), SettleProgress)
                : Projectile.Center;

        public override bool PreDraw(ref Color lightColor)
        {
            int itemType = (int)Projectile.ai[2];
            if (itemType <= 0)
                return true;
            // Текстуры предметов ванилла грузит лениво — у других игроков её может не быть
            Main.instance.LoadItem(itemType);
            Texture2D texture = TextureAssets.Item[itemType].Value;
            if (texture == null || texture.IsDisposed)
                return true;

            // Рисуем от реального приклада вдоль оси rotation (геометрия по пикселям,
            // как у тычка и копья). Без зеркалирования: кувырок и так крутит спрайт,
            // а у лежащей нижний край считается для незеркального спрайта.
            StickSprite.Geometry geo = StickSprite.Get(itemType);
            float heading = Projectile.rotation;
            Vector2 axis = heading.ToRotationVector2();
            Vector2 center = VisualCenter(geo);
            float scale = Projectile.scale;
            Vector2 butt = center - axis * (geo.Length * 0.5f * scale);

            Main.EntitySpriteDraw(texture, butt - Main.screenPosition, null,
                lightColor, heading - geo.AngleFor(false), geo.Butt, scale, SpriteEffects.None, 0f);
            return false;
        }
    }
}
