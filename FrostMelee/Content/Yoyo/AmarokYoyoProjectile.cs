using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Yoyo
{
    // Своё йо-йо вместо ванильного (ЙОЙО.md):
    // удар — отскок к игроку — вылет обратно (темп от расстояния: близко бьёт чаще),
    // наводка по ПКМ, круг по ЛКМ+ПКМ, лёд нарастает и срывается ударом с бонусом.
    // Перчатка/сумка: двойник рядом + противовес в цель каждый третий удар.
    public class AmarokYoyoProjectile : ModProjectile
    {
        private enum State { Out, Bounce, Homing, Circle, Return }

        private const float MaxRange = 260f;        // длина верёвки, px
        private const float ReferenceSpeed = 14f;   // ванильная скорость Амарока: от неё все множители
        private const float CircleRadius = 70f;          // радиус орбиты круга (при скорости йо-йо 14)
        private const float CircleSpeedStart = 0.07f;    // рад/тик на старте круга при скорости йо-йо 14
        private const float CircleSpeedMax = 0.25f;      // полный разгон (~2.4 оборота/с)
        private const float CircleRampTime = 300f;       // тиков до полного разгона (5 с)
        private const float CircleRadiusGrow = 0.4f;     // радиус дорастает на 40% к разгону
        private const float CircleCatchSpeedStart = 6f;  // догон орбитной точки на старте
        private const float CircleCatchSpeedMax = 34f;   // догон точки на полном разгоне
        private const int BounceCatchDist = 42;     // долетело до рук — снова наружу
        private const int MaxBounceTicks = 90;      // страховка от застревания

        private const int GrowTicks = 25;           // тиков на одну сосульку (~6 с до полного)
        private const int MaxChunks = 14;           // ступеней льда = кадров спрайта
        private const int BurstShards = 10;         // осколков во взрыве полного льда
        private const float BurstDamageMult = 0.5f; // урон осколка от удара
        private const float BurstSpeed = 9f;
        private const int BaseSize = 16;
        private const int SizePerChunk = 1;         // px к ширине за сосульку
        private const float SpriteOverhang = 1.42f; // квадрат в повороте 45° вылезает за хитбокс на корень из двух
        private const float IceGrowEase = 0.35f;    // сглаживание визуального счёта сосулек
        private const int PopTicks = 12;            // тиков пружины появления новой сосульки
        private const float PopAmount = 0.22f;      // насколько выпрыгивает масштаб при появлении
        private const int FullSlowTicks = 60;      // тормоз с полного набора
        private const float CloneOrbitRadius = 40f; // радиус орбиты двойника
        private const float CloneFollowSpeed = 12f; // скорость догоняющего двойника
        private const int DetachCooldown = 45;      // отрыв сосульки не чаще, чем раз в это число тиков
        private const int HitCooldownBase = 10;     // тиков между ударами по цели, как ванила
        private const float FrostbiteChance = 0.67f; // как у ванильного Амарока
        private const int FrostbiteTicks = 150;

        private State CurrentState = State.Out;
        private bool started;
        private float itemSpeed;   // shootSpeed йо-йо в руках: от неё пляшут все скорости
        private int chunks;
        private int growTimer;
        private float circleAngle;
        private float cloneAngle;
        private bool circleFlourish;   // звук-кольцо полного разгона круга, раз за канал
        private int detachCooldown;    // отрыв сосульки не чаще, чем раз в DetachCooldown тиков
        private float visualChunks;    // сглаженный счёт сосулек для анимации (только рисование)
        private int popTimer;          // пружина появления новой сосульки

        // Единая формула: скорости считаются от shootSpeed йо-йо в руках
        // (у ванильного Амарока она 14). Константы выше заданы в единицах
        // эталона: умножаем на SpeedMult — поведение повторяется у любого йо-йо
        private float YoyoSpeed => itemSpeed >= 1f ? itemSpeed : ReferenceSpeed;
        private float SpeedMult => YoyoSpeed / ReferenceSpeed;
        private int stateTimer;

        // Двойник от перчатки йо-йо: его выпускает ваниль при ударе с ai[0] == 1
        private bool IsClone => Projectile.ai[0] == 1f;

        // Спрайт — лента льда AmarokIceStrip: 14 кадров столбиком,
        // кадр 0 — чистое йо-йо, дальше сосулек всё больше
        public override string Texture => "FrostMelee/Items/AmarokIceStrip";

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = IceFrames;
        }

        public override void SetDefaults()
        {
            Projectile.width = BaseSize;
            Projectile.height = BaseSize;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.tileCollide = true;
            Projectile.ignoreWater = true;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = HitCooldownBase; // как ванила: не чаще 6 ударов в секунду по цели
            // Стиль обычного йо-йо: его проверяет сумка, перчатка и противовесы.
            // Ванильный полёт при этом пропущен (PreAI), летим полностью сами
            Projectile.aiStyle = ProjAIStyleID.Yoyo;
            Projectile.timeLeft = 2;
            Projectile.netImportant = true;
        }

        // Ванильный полёт пропущен (летим сами в PostAI),
        // а стиль 99 оставлен: по нему нас узнают сумка и противовесы
        public override bool PreAI() => false;

        public override void PostAI()
        {
            Player player = Main.player[Projectile.owner];
            AmarokPlayer mp = player.GetModPlayer<AmarokPlayer>();

            // Звук выброса — один раз за вылет (двойник молчит)
            if (!started)
            {
                started = true;
                if (!IsClone)
                    SoundEngine.PlaySound(SoundID.Item1, Projectile.Center);
            }

            // Скорость из характеристик йо-йо в руках: множители ниже считаются от неё
            Item held = player.HeldItem;
            if (held != null && !held.IsAir && ItemID.Sets.Yoyo[held.type] && held.shootSpeed > 0f)
                itemSpeed = held.shootSpeed;

            // Двойник живёт своей жизнью: крутится вокруг основного и копит свой лёд
            if (IsClone)
            {
                Projectile.timeLeft = 2;
                Projectile.rotation += 0.4f;
                CloneAI(player);
                GrowIce();
                // Анимация сосулек и у двойника
                if (popTimer > 0)
                    popTimer--;
                visualChunks += (chunks - visualChunks) * IceGrowEase;
                return;
            }

            // Оружие сменили или игрок мёртв — домой и исчезнуть
            if (!player.active || player.dead || player.HeldItem.type != ItemID.Amarok)
            {
                CurrentState = State.Return;
            }
            else if (CurrentState != State.Return && !player.channel && !mp.HomingWanted)
            {
                // Отпустил ЛКМ и ПКМ свободна — рывок домой, как в игре
                CurrentState = State.Return;
            }
            Projectile.timeLeft = 2;
            stateTimer++;
            // Вне круга — базовая частота ударов: после отмены круга не остаётся
            // разогнанный кулдаун
            if (CurrentState != State.Circle)
                Projectile.localNPCHitCooldown = HitCooldownBase;
            // Домой — сквозь блоки, в бою — честный отскок
            Projectile.tileCollide = CurrentState != State.Return;

            // Лёд нарастает, пока йо-йо в бою (не возвращается). Главное и двойник копят оба
            GrowIce();
            // Анимация сосулек: визуальный кадр плывёт к счёту, новая пружинит
            if (popTimer > 0)
                popTimer--;
            visualChunks += (chunks - visualChunks) * IceGrowEase;

            switch (CurrentState)
            {
                case State.Out:
                    // Наводка важнее точки
                    if (mp.HoldingAmarok && mp.HomingWanted && mp.HomingValid())
                    {
                        CurrentState = State.Homing;
                        stateTimer = 0;
                        break;
                    }
                    if (mp.HoldingAmarok && mp.CircleActive)
                    {
                        CurrentState = State.Circle;
                        stateTimer = 0;
                        circleAngle = (Projectile.Center - player.Center).ToRotation();
                        break;
                    }
                    if (!player.channel)
                    {
                        if (mp.HomingWanted)
                        {
                            // Некого бить — подтягивается к рукам и ждёт там, а не висит в воздухе
                            Vector2 dir = (Main.MouseWorld - player.Center).SafeNormalize(Vector2.UnitX);
                            MoveTowards(player.Center + dir * 50f, YoyoSpeed * 0.6f);
                        }
                        else
                        {
                            // ЛКМ отпущена, цели нет — висеть и ждать
                            Projectile.velocity *= 0.85f;
                        }
                        break;
                    }
                    MoveTowards(CursorAnchor(player), YoyoSpeed);
                    break;

                case State.Homing:
                    if (!mp.HomingWanted)
                    {
                        CurrentState = State.Out;
                        stateTimer = 0;
                        break;
                    }
                    if (!mp.HomingValid())
                    {
                        mp.ClearHoming();
                        CurrentState = State.Out;
                        stateTimer = 0;
                        break;
                    }
                    if (mp.CircleActive)
                    {
                        CurrentState = State.Circle;
                        stateTimer = 0;
                        circleAngle = (Projectile.Center - player.Center).ToRotation();
                        mp.ClearHoming();
                        break;
                    }
                    // Поводок: цель ушла дальше верёвки — бросаем и возвращаемся к рукам
                    if (Vector2.Distance(Main.npc[mp.HomingTarget].Center, player.Center) > AmarokPlayer.HomingRange)
                    {
                        mp.ClearHoming();
                        CurrentState = State.Bounce;
                        stateTimer = 0;
                        break;
                    }
                    MoveTowards(Main.npc[mp.HomingTarget].Center, YoyoSpeed);
                    break;

                case State.Circle:
                    if (!mp.CircleActive)
                    {
                        CurrentState = State.Out;
                        stateTimer = 0;
                        break;
                    }
                    // Нелинейный разгон: старт ленивый, к концу резко догоняем,
                    // радиус дорастает вместе со скоростью
                    float ramp = MathHelper.Clamp(stateTimer / CircleRampTime, 0f, 1f);
                    float spin = MathHelper.Lerp(CircleSpeedStart, CircleSpeedMax, ramp * ramp) * SpeedMult;
                    circleAngle += spin;
                    float radius = CircleRadius * (1f + CircleRadiusGrow * ramp);
                    MoveTowards(player.Center + circleAngle.ToRotationVector2() * radius,
                        MathHelper.Lerp(CircleCatchSpeedStart, CircleCatchSpeedMax, ramp * ramp) * SpeedMult);
                    // Разогнанный круг проходит врагов чаще — и бьёт чаще
                    Projectile.localNPCHitCooldown = HitCooldownBase - (int)(5f * ramp);
                    // Лента инея вдоль орбиты: чем быстрее, тем гуще
                    if (!Main.dedServ && Main.rand.NextFloat() < 0.35f + 0.45f * ramp)
                    {
                        Vector2 back = circleAngle.ToRotationVector2().RotatedBy(-MathHelper.PiOver2);
                        Dust wheel = Dust.NewDustPerfect(Projectile.Center, DustID.Frost,
                            back * Main.rand.NextFloat(1.5f, 3f) + Main.rand.NextVector2Circular(0.6f, 0.6f),
                            120, default, Main.rand.NextFloat(0.8f, 1.3f));
                        wheel.noGravity = true;
                        wheel.fadeIn = 0.3f;
                    }
                    // Полный разгон: разовый скрип-звон льда и кольцо снежинок по орбите
                    if (ramp >= 1f && !circleFlourish)
                    {
                        circleFlourish = true;
                        SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.5f, Pitch = 0.6f }, Projectile.Center);
                        for (int f = 0; f < 14; f++)
                        {
                            Vector2 fdir = Vector2.UnitX.RotatedBy(f / 14f * MathHelper.TwoPi);
                            Dust ring = Dust.NewDustPerfect(player.Center + fdir * radius,
                                DustID.SnowflakeIce, fdir * 3f, 100, default, 1.3f);
                            ring.noGravity = true;
                            ring.fadeIn = 0.3f;
                        }
                    }
                    if (ramp < 1f)
                        circleFlourish = false;
                    break;

                case State.Bounce:
                    // После удара — к рукам. Долетело — снова наружу
                    ChasePlayer(player, YoyoSpeed);
                    if (Vector2.Distance(Projectile.Center, player.Center) <= BounceCatchDist || stateTimer > MaxBounceTicks)
                    {
                        CurrentState = State.Out;
                        stateTimer = 0;
                    }
                    break;

                case State.Return:
                    // Вернули до полного накопления — лёд сброшен без взрыва
                    if (chunks != 0)
                    {
                        chunks = 0;
                        visualChunks = 0f;
                        popTimer = 0;
                        ApplySize();
                    }
                    ChasePlayer(player, YoyoSpeed);
                    if (Vector2.Distance(Projectile.Center, player.Center) <= 26f)
                    {
                        if (Projectile.owner == Main.myPlayer)
                            SoundEngine.PlaySound(SoundID.Grab, player.Center);
                        Projectile.Kill();
                        return;
                    }
                    break;
            }

            Projectile.rotation += 0.4f;
        }

        // Якорь точки: курсор, но не дальше верёвки
        private Vector2 CursorAnchor(Player player)
        {
            Vector2 dir = (Main.MouseWorld - player.Center).SafeNormalize(Vector2.UnitX);
            float dist = MathHelper.Min(Vector2.Distance(Main.MouseWorld, player.Center), MaxRange);
            return player.Center + dir * dist;
        }

        // Догон игрока: наследуем его скорость + сближение.
        // Без этого падающий/летящий игрок улетает от тормозящего у цели йо-йо
        private void ChasePlayer(Player player, float closingSpeed)
        {
            Vector2 to = player.Center - Projectile.Center;
            float d = to.Length();
            if (d > 1f)
                Projectile.velocity = player.velocity + to / d * closingSpeed;
            else
                Projectile.velocity = player.velocity;
        }

        private void MoveTowards(Vector2 anchor, float maxSpeed)
        {
            Vector2 to = anchor - Projectile.Center;
            float d = to.Length();
            if (d < 2f)
            {
                // Уже на точке: глушим скорость полностью, иначе каждый тик
                // проскакиваем цель и дрожим на месте
                Projectile.velocity = Vector2.Zero;
                return;
            }
            float sp = MathHelper.Min(maxSpeed, d * 0.25f + 2f);
            Projectile.velocity = to / d * sp;
        }

        // Рост хитбокса от льда. Не разлезаться в блоки: пока йо-йо прижато
        // к стене, куски не отрастают наружу — как только съезжает, размер
        // догоняет счётчик. Иначе вылезает под стены
        private void ApplySize()
        {
            Vector2 c = Projectile.Center;
            int want = BaseSize + chunks * SizePerChunk;
            int size = want;
            // Проверяем с запасом на вращение: картинка на диагональных углах
            // расползается шире хитбокса — иначе её углы протыкают стену
            int test = (int)(size * SpriteOverhang);
            while (size > BaseSize && Collision.SolidCollision(
                c - new Vector2(test / 2f, test / 2f), test, test))
            {
                size -= SizePerChunk;
                test = (int)(size * SpriteOverhang);
            }
            Projectile.width = size;
            Projectile.height = size;
            Projectile.Center = c;
        }

        // Лёд нарастает, пока йо-йо в бою. Общий для основного и двойника:
        // каждый копит свои сосульки независимо, у двойника состояний нет —
        // растёт всегда, пока жив. До полного набора
        private void GrowIce()
        {
            if (Main.dedServ || chunks >= MaxChunks)
                return;
            bool inCombat = IsClone || CurrentState != State.Return;
            if (!inCombat)
                return;
            if (++growTimer >= GrowTicks)
            {
                growTimer = 0;
                chunks++;
                popTimer = PopTicks;
                ApplySize();
                Burst(Projectile.Center, DustID.SnowflakeIce, 6, 1.5f);
            }
        }

        // Касание врага: одна сосулька отрывается и прочь, йо-йо откатывается
        // на ступень. Лёд не копится безнаказанно во время боя — чем больше бьёшь,
        // тем чаще теряешь кусочек; полный набор достижим только попридержав йо-йо
        private void TryDetachIcicle()
        {
            if (Main.dedServ || Projectile.owner != Main.myPlayer)
                return;
            if (chunks <= 0 || detachCooldown > 0)
                return;
            chunks--;
            ApplySize();
            detachCooldown = DetachCooldown;
            Vector2 dir = Main.rand.NextVector2Circular(1f, 1f);
            if (dir.LengthSquared() < 0.01f)
                dir = Vector2.UnitX;
            dir.Normalize();
            Projectile.NewProjectile(Projectile.GetSource_FromThis(), Projectile.Center + dir * 12f,
                dir * Main.rand.NextFloat(5f, 8f), ModContent.ProjectileType<AmarokShardProjectile>(),
                (int)MathHelper.Max(1f, Projectile.damage * BurstDamageMult), 1f, Projectile.owner);
            Burst(Projectile.Center, DustID.Frost, 4, 2f);
        }

        // Двойник от перчатки: догоняет точку у основного как мини-йо-йо,
        // у стен скользит, сквозь блоки не проходит. Основного нет
        // или перчатку сняли — исчезнуть
        private void CloneAI(Player player)
        {
            // Основное ищем среди снарядов игрока: ваниль номер основного не передаёт
            int main = -1;
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.owner == Projectile.owner && p.type == Projectile.type &&
                    p.ModProjectile is AmarokYoyoProjectile y && !y.IsClone)
                {
                    main = i;
                    break;
                }
            }
            bool mainAlive = main >= 0;
            if (!mainAlive || !player.GetModPlayer<AmarokPlayer>().GloveActive())
            {
                Projectile.Kill();
                return;
            }
            // Точка орбиты крутится своим углом (круг главного не трогаем),
            // а сам двойник едет к ней рулёжкой: за быстрегим главным чуть
            // отстаёт и красиво догоняет, у стены не рвётся внутрь
            cloneAngle += 0.18f;
            Vector2 wanted = Main.projectile[main].Center + CloneOrbitRadius * cloneAngle.ToRotationVector2();
            MoveTowards(wanted, CloneFollowSpeed * SpeedMult);
        }

        // Двойник этого игрока уже есть
        private bool HasClone()
        {
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.owner == Projectile.owner && p.type == Projectile.type &&
                    p.ModProjectile is AmarokYoyoProjectile y && y.IsClone)
                    return true;
            }
            return false;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            if (Main.rand.NextFloat() < FrostbiteChance)
                target.AddBuff(BuffID.Frostburn2, FrostbiteTicks); // Frostbite, хардмодный как сам Амарок
            // Раз в DetachCooldown тиков, не чаще: циклы частых ударов круга
            // не спишут весь лёд за секунду
            if (detachCooldown > 0)
                detachCooldown--;
            // Полный набор: тормоз + взрыв сосульками кругом, лёд обнуляется и растёт заново.
            // Двойник тоже взрывается полным, но без ритма сумки и отскока
            if (chunks >= MaxChunks)
            {
                target.AddBuff(BuffID.Slow, FullSlowTicks);
                for (int i = 0; i < BurstShards; i++)
                {
                    // Старт с кольца вокруг цели, а не из её центра —
                    // иначе жертва втанчивает все осколки сразу
                    Vector2 dir = Vector2.UnitX.RotatedBy(i / (float)BurstShards * MathHelper.TwoPi);
                    Projectile.NewProjectile(Projectile.GetSource_FromThis(), target.Center + dir * 24f, dir * BurstSpeed,
                        ModContent.ProjectileType<AmarokShardProjectile>(),
                        (int)MathHelper.Max(1f, Projectile.damage * BurstDamageMult), 1f, Projectile.owner);
                }
                SoundEngine.PlaySound(SoundID.Item27, target.Center);
                Lighting.AddLight(target.Center, new Vector3(0.5f, 1f, 1.5f));
                // Короткая вспышка взрыва: мягкое свечение и кольцо осколков льда
                if (!Main.dedServ)
                {
                    Dust flash = Dust.NewDustPerfect(target.Center, DustID.Frost, Vector2.Zero, 60, default, 3.2f);
                    flash.noGravity = true;
                    flash.fadeIn = 0.6f;
                    for (int f = 0; f < 12; f++)
                    {
                        Vector2 fdir = Vector2.UnitX.RotatedBy(f / 12f * MathHelper.TwoPi);
                        Dust ring = Dust.NewDustPerfect(target.Center + fdir * 10f, DustID.SnowflakeIce,
                            fdir * 2.5f, 90, default, 1.4f);
                        ring.noGravity = true;
                    }
                }
                Burst(target.Center, DustID.SnowflakeIce, 14, 4f);
                chunks = 0;
                visualChunks = 0f;
                popTimer = 0;
                ApplySize();
            }
            else
            {
                // Неполный лёд: каждое касание врага отрывает сосульку и откатывает ступень
                TryDetachIcicle();
            }

            // Противовесы и двойника спавнит ваниль сама при ударе (стиль 99).
            // Но в режиме наводки (ЛКМ отпущена) ваниль двойника не выпускает —
            // выпускаем сами так же, как она: из игрока в цель, скорость 16, ai[0] = 1
            if (!IsClone && Projectile.owner == Main.myPlayer)
            {
                Player owner = Main.player[Projectile.owner];
                if (!owner.channel && owner.GetModPlayer<AmarokPlayer>().GloveActive() && !HasClone())
                {
                    Vector2 toTarget = (target.Center - owner.Center).SafeNormalize(Vector2.UnitX);
                    Projectile.NewProjectile(Projectile.GetSource_FromThis(), owner.Center, toTarget * 16f,
                        Projectile.type, Projectile.damage, Projectile.knockBack, Projectile.owner, 1f);
                }
            }

            if (Projectile.owner == Main.myPlayer)
                Main.player[Projectile.owner].GetModPlayer<AmarokPlayer>().ClearHoming();
            Burst(target.Center, DustID.Frost, 8, 2.5f);

            // После удара — отскок к игроку (кроме возврата домой)
            if (CurrentState != State.Return)
            {
                CurrentState = State.Bounce;
                stateTimer = 0;
            }
        }

        // Основное умерло — двойников с собой
        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            // У стены не отскакиваем, а скользим, как ванильное йо-йо:
            // гасим только компоненту скорости в стену, вдоль стены рулёжка
            // везёт плавно. Отражение тут ломает движение: рулёжка каждый тик
            // заново кидает скорость в стену, и йо-йо дёргается
            if (CurrentState == State.Return)
                return false;
            if (System.Math.Abs(Projectile.velocity.X - oldVelocity.X) > 0.01f)
                Projectile.velocity.X = 0f;
            if (System.Math.Abs(Projectile.velocity.Y - oldVelocity.Y) > 0.01f)
                Projectile.velocity.Y = 0f;
            return false;
        }

        private static void Burst(Vector2 at, int dustType, int count, float speed)
        {
            if (Main.dedServ)
                return;
            for (int i = 0; i < count; i++)
            {
                Dust d = Dust.NewDustPerfect(at + Main.rand.NextVector2Circular(6f, 6f), dustType,
                    Main.rand.NextVector2Circular(speed, speed), 120, default, Main.rand.NextFloat(0.7f, 1.1f));
                d.noGravity = true;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            // Само йо-йо = кадр ленты по набранному льду (0 — без льда, последний — полный набор).
            // Текстура грузится tModLoader'ом при загрузке мода по пути Texture
            Texture2D strip = TextureAssets.Projectile[Type].Value;
            if (strip == null || strip.IsDisposed)
                return false;
            int frameH = strip.Height / IceFrames;
            // Кадр по сглаженному счёту: сосулки нарастают и тают плавно,
            // а новая пружинит масштабом
            float vis = MathHelper.Clamp(visualChunks, 0f, MaxChunks);
            int frame = (int)System.Math.Round(vis * (IceFrames - 1) / MaxChunks);
            frame = Utils.Clamp(frame, 0, IceFrames - 1);
            Rectangle src = new Rectangle(0, frame * frameH, strip.Width, frameH);
            float popScale = 1f;
            if (popTimer > 0)
            {
                float p = popTimer / (float)PopTicks;
                popScale += PopAmount * p * p;
            }
            Main.EntitySpriteDraw(strip, Projectile.Center - Main.screenPosition, src,
                lightColor, Projectile.rotation, new Vector2(strip.Width / 2f, frameH / 2f), popScale, SpriteEffects.None, 0f);
            return false;
        }

        private const int IceFrames = 14;   // кадров в ленте AmarokIceStrip
    }
}
