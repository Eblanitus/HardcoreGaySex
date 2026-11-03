using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace FrostMelee.Content.Projectiles
{
    // Глыба Айсберга. Текстура — расходник из Deerclops_Debris (лёд),
    // владелец перерисует. Лист 32x96: три кадра 32x32 по вертикали.
    //
    // ai[0] — вид: 0 обычная глыба взмаха, 1 крупная глыба броска.
    // ai[1] — вариант рисунка 0..2 + 3 × (масштаб × 10), если масштаб задан
    // явно (глыба тычка и встречи, SpawnSingleChunk); 0 — по виду.
    // ai[2] — урон всплеска при расколе.
    //
    // «Льдины»: глыба сама не взрывается. Падая, бьёт врагов собой и
    // отскакивает от них (BounceOffNpcs), потом ложится на землю льдиной;
    // полежав, тихо тает. Взорвать её (AisbergBlast: урон по площади
    // + Frostbite) может только владелец:
    // - взмах отбивает льдину от себя, как бильярдный шар (KnockTouched),
    //   отбитая раскалывается о первого врага или о стену;
    // - тычок и встреча раскалывают на месте (CrackTouched).
    // Глыбы рождаются от любой атаки: взмах — 3 обычных, тычок — 1 крупная,
    // встреча — 1, размер и сила от скорости, бросок — 3 крупных.
    // Лёд на земле оставляют осколки раскола (Dusts/AisbergShard).
    public class AisbergChunk : ModProjectile
    {
        private const int GraceTicks = 8;       // не бьёт сразу из места рождения на враге
        private const float Gravity = 0.3f;
        private const float MaxFall = 13f;
        private const float SmallScale = 0.95f;
        private const float LargeScale = 1.7f;
        private const int LandedLifeTicks = 480; // льдина лежит 8 с, потом тает без взрыва
        private const int MaxLying = 8;          // больше льдин на поле — старейшая тает
        private const float KnockSpeed = 15f;
        private const float KnockGravity = 0.12f;
        private const int KnockedLifeTicks = 75;
        private const int MaxBounces = 3;        // отскоков падающей глыбы от врагов
        private const float BounceKeep = 0.6f;   // доля скорости, что остаётся после отскока
        private const float BounceMinUp = 3.5f;  // минимальный подскок с головы врага

        private bool initialized;
        private bool cracked;
        private int landedTicks;
        private int bounces;
        private int bounceCooldown;
        private float spin;

        // ai[0]: 0/1 — падает (обычная/крупная), 2/3 — лежит льдиной,
        // 4/5 — отбита взмахом и летит
        private bool IsLargeKind => (int)Projectile.ai[0] % 2 == 1;
        private int SizeCode => Math.Abs((int)Projectile.ai[1]) / 3;
        private float Size => SizeCode > 0 ? SizeCode / 10f : (IsLargeKind ? LargeScale : SmallScale);
        private bool IsLarge => Size >= 1.3f;
        private bool Landed => Projectile.ai[0] >= 1.5f && Projectile.ai[0] < 3.5f;
        private bool Knocked => Projectile.ai[0] >= 3.5f;
        // 80 px у обычной (0.95), ~145 у крупной (1.7), растёт с размером
        private float BlastRadius => 84f * Size;
        private int Age => (int)Projectile.localAI[0];

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 3;
        }

        public override void SetDefaults()
        {
            Projectile.width = 24;
            Projectile.height = 24;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            // Падающая глыба отскакивает от врагов сама (BounceOffNpcs), отбитая
            // раскалывается о первого (Crack в OnHitNPC)
            Projectile.penetrate = -1;
            Projectile.tileCollide = true;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 360;
            // Все глыбы делят один кулдаун по цели: три куска одного взмаха
            // не должны бить одного врага три раза подряд.
            Projectile.usesIDStaticNPCImmunity = true;
            Projectile.idStaticNPCHitCooldown = 12;
            Projectile.aiStyle = -1;
        }

        public override void AI()
        {
            if (!initialized)
            {
                initialized = true;
                Projectile.frame = Math.Abs((int)Projectile.ai[1]) % 3;
                Projectile.scale = Size;
                Vector2 center = Projectile.Center;
                Projectile.width = Projectile.height = (int)(26f * Size);
                Projectile.Center = center;
                spin = (Projectile.whoAmI % 2 == 0 ? 1f : -1f) * (IsLarge ? 0.07f : 0.14f);
            }

            Projectile.localAI[0]++;
            if (Landed)
            {
                Projectile.velocity = Vector2.Zero;
                Projectile.tileCollide = false;
                Projectile.rotation *= 0.85f;
                if (Projectile.owner == Main.myPlayer && ++landedTicks >= LandedLifeTicks)
                    Projectile.Kill(); // растаяла: без раскола и взрыва
            }
            else if (Knocked)
            {
                Projectile.velocity.Y = Math.Min(Projectile.velocity.Y + KnockGravity, MaxFall);
                Projectile.rotation += spin * 2.5f;
                // Отбитая, но никуда не попавшая — всё равно лопается в конце полёта
                if (Projectile.owner == Main.myPlayer && Projectile.timeLeft <= 1)
                    Crack();
            }
            else
            {
                Projectile.velocity.Y = Math.Min(Projectile.velocity.Y + Gravity, MaxFall);
                Projectile.velocity.X *= 0.995f;
                Projectile.rotation += spin;
                if (bounceCooldown > 0)
                    bounceCooldown--;
                if (Age > GraceTicks)
                    BounceOffNpcs();
            }

            Lighting.AddLight(Projectile.Center, 0.08f, 0.2f, 0.3f);
            if (!Main.dedServ && Age % 6 == 0)
            {
                Dust d = Dust.NewDustPerfect(Projectile.Center + Main.rand.NextVector2Circular(8f, 8f),
                    DustID.Frost, -Projectile.velocity * 0.08f, 130, default, 0.8f);
                d.noGravity = true;
            }
        }

        // Падающая глыба твёрдая: о врага ударяется и отскакивает, а не
        // проходит насквозь. Сверху — подпрыгивает вверх и в сторону,
        // сбоку — отлетает назад. Урон при этом наносит обычный удар
        // снаряда (кулдаун по цели общий у всех глыб). После MaxBounces
        // отскоков глыба соскальзывает с врага вбок и падает дальше.
        private void BounceOffNpcs()
        {
            Rectangle box = Projectile.Hitbox;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!npc.active || npc.friendly || npc.dontTakeDamage || npc.lifeMax <= 5)
                    continue;
                Rectangle nb = npc.Hitbox;
                if (!box.Intersects(nb))
                    continue;

                Vector2 away = Projectile.Center - npc.Center;
                float side = away.X >= 0f ? 1f : -1f;
                bool fromAbove = Projectile.Center.Y < nb.Top + nb.Height * 0.35f;
                // Уже отлетает от этого врага — не отскакиваем повторно. Глыбу
                // из врага не выталкиваем: удар снаряда считается после AI,
                // глыба должна ещё касаться цели в этот тик
                bool closing = fromAbove ? Projectile.velocity.Y > 0f : Projectile.velocity.X * side < 0.5f;
                if (!closing || bounceCooldown > 0)
                    continue;
                if (bounces >= MaxBounces)
                {
                    // Хватит прыгать по одному врагу: соскальзывает вбок
                    Projectile.velocity.X = side * Math.Max(Math.Abs(Projectile.velocity.X), 3.5f);
                    return;
                }
                bounces++;
                bounceCooldown = 6;
                if (fromAbove)
                {
                    Projectile.velocity.Y = -Math.Max(BounceMinUp, Projectile.velocity.Y * BounceKeep);
                    Projectile.velocity.X = side * Math.Max(Math.Abs(Projectile.velocity.X) * 0.8f, 1.6f);
                }
                else
                {
                    Projectile.velocity.X = side * Math.Max(Math.Abs(Projectile.velocity.X) * BounceKeep, 2.5f);
                    Projectile.velocity.Y = Math.Min(Projectile.velocity.Y, -1.5f);
                }
                spin = -spin;
                Projectile.netUpdate = true;
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Item50 with { Volume = 0.45f, Pitch = IsLarge ? -0.4f : 0f },
                        Projectile.Center);
                    for (int k = 0; k < 3; k++)
                        Dust.NewDustPerfect(Projectile.Center, DustID.Ice,
                            Main.rand.NextVector2Circular(2f, 2f) + new Vector2(0f, -1f), 0, default, 1.1f);
                }
                return;
            }
        }

        // Лежащая льдина сама не бьёт: она снаряд для взмаха, а не ловушка
        public override bool? CanDamage() => !cracked && !Landed && (Knocked || Age > GraceTicks);

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            // Отбитая разбивается о стену или пол
            if (Knocked)
            {
                Crack();
                return true;
            }

            // Упала сверху на поверхность — ложится льдиной
            if (oldVelocity.Y > 0.5f && Projectile.velocity.Y == 0f)
            {
                Projectile.ai[0] += 2f;
                Projectile.velocity = Vector2.Zero;
                Projectile.tileCollide = false;
                Projectile.timeLeft = LandedLifeTicks + 30;
                landedTicks = 0;
                Projectile.netUpdate = true;
                if (Projectile.owner == Main.myPlayer)
                    MeltOldestOverLimit();
                return false;
            }

            // Боком о стену или о потолок — гасим эту ось и падаем дальше
            if (Projectile.velocity.X != oldVelocity.X)
                Projectile.velocity.X = 0f;
            if (Projectile.velocity.Y != oldVelocity.Y)
                Projectile.velocity.Y = 0f;
            return false;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            if (Knocked)
                Crack();
        }

        // Льдин на поле не больше MaxLying: лишняя старейшая тает
        private void MeltOldestOverLimit()
        {
            int count = 0;
            AisbergChunk oldest = null;
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (!p.active || p.type != Type || p.owner != Projectile.owner)
                    continue;
                if (p.ModProjectile is not AisbergChunk chunk || !chunk.Landed || chunk.cracked)
                    continue;
                count++;
                if (oldest == null || chunk.landedTicks > oldest.landedTicks)
                    oldest = chunk;
            }
            if (count > MaxLying && oldest != null)
                oldest.Projectile.Kill();
        }

        // Взмах задел льдину: она срывается от игрока по прямой, как шар от кия
        private void Knock(Player owner)
        {
            Vector2 dir = Projectile.Center - owner.Center;
            if (dir.LengthSquared() < 1f)
                dir = new Vector2(owner.direction, 0f);
            dir.Normalize();
            // Чуть вверх: лежащая на полу льдина не должна сразу чиркнуть о него
            dir = Vector2.Normalize(dir + new Vector2(0f, -0.15f));

            Projectile.ai[0] = IsLargeKind ? 5f : 4f;
            Projectile.velocity = dir * KnockSpeed;
            Projectile.tileCollide = true;
            Projectile.timeLeft = KnockedLifeTicks;
            Projectile.knockBack *= 2.5f; // удар отбитой льдины — это толчок, контроль
            Projectile.netUpdate = true;
            // Чтобы не застрять в полу, на котором лежала
            Projectile.position.Y -= 4f;

            SoundEngine.PlaySound(SoundID.Item50 with { Volume = 0.7f, Pitch = IsLarge ? -0.3f : 0.1f },
                Projectile.Center);
            if (!Main.dedServ)
            {
                for (int i = 0; i < 8; i++)
                {
                    Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.Frost,
                        -dir.RotatedByRandom(0.6f) * Main.rand.NextFloat(1f, 3f), 100, default, 1.1f);
                    d.noGravity = true;
                }
            }
        }

        public void Crack()
        {
            if (cracked || !Projectile.active)
                return;
            cracked = true;
            Projectile.Kill();
        }

        public override void OnKill(int timeLeft)
        {
            // Раскол рисует взрыв (AisbergBlast.ShatterFx), здесь — только таяние
            if (!Main.dedServ && !cracked)
            {
                int dustCount = IsLarge ? 8 : 5;
                for (int i = 0; i < dustCount; i++)
                {
                    Dust d = Dust.NewDustPerfect(Projectile.Center, Main.rand.NextBool(3) ? DustID.Ice : DustID.Frost,
                        Main.rand.NextVector2Circular(IsLarge ? 4f : 2.8f, IsLarge ? 4f : 2.8f),
                        100, default, Main.rand.NextFloat(0.9f, IsLarge ? 1.7f : 1.2f));
                    d.noGravity = false;
                }
            }

            if (!cracked)
                return;

            if (Projectile.owner != Main.myPlayer)
                return;

            int blastDamage = Projectile.ai[2] > 0f ? (int)Projectile.ai[2] : Math.Max(1, Projectile.damage / 2);
            Projectile.NewProjectile(Projectile.GetSource_FromThis(), Projectile.Center, Vector2.Zero,
                ModContent.ProjectileType<AisbergBlast>(), blastDamage, Projectile.knockBack,
                Projectile.owner, BlastRadius, Projectile.frame, Projectile.scale);

        }

        // Заморозить поверхность под местом раскола своей глазурью
        // (IceSheetSystem: та же дорожка, что у скольжения и волны).
        public static void FreezeSurface(Vector2 center, int radiusTiles)
        {
            if (Main.dedServ)
                return;
            Point tile = center.ToTileCoordinates();
            for (int x = tile.X - radiusTiles; x <= tile.X + radiusTiles; x++)
            {
                for (int y = tile.Y - 1; y <= tile.Y + 12; y++)
                {
                    if (!WorldGen.InWorld(x, y, 2))
                        break;
                    if (!WorldGen.SolidTile(x, y))
                        continue;
                    Weapons.IceSheetSystem.MarkSurface(x, y);
                    break;
                }
            }
        }

        // Расколоть свои глыбы, которых коснулся клинок приёма владельца.
        // overlaps — проверка хитбокса самого приёма (тычка или встречи).
        public static void CrackTouched(Projectile blade, Func<Rectangle, bool> overlaps)
        {
            if (blade == null || blade.owner != Main.myPlayer)
                return;
            int type = ModContent.ProjectileType<AisbergChunk>();
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (!p.active || p.type != type || p.owner != blade.owner)
                    continue;
                // Только что рождённую этим же ударом глыбу не колем сразу
                if (p.ModProjectile is AisbergChunk chunk && !chunk.cracked && chunk.Age > GraceTicks
                    && overlaps(p.Hitbox))
                    chunk.Crack();
            }
        }

        // Отбить свои льдины, которых коснулся клинок взмаха. swingAge — сколько
        // тиков идёт взмах: глыбы, отколотые этим же взмахом, моложе него и
        // не отбиваются, иначе они срывались бы прямо из места рождения.
        public static void KnockTouched(Projectile arc, int swingAge, Func<Rectangle, bool> overlaps)
        {
            if (arc == null || arc.owner != Main.myPlayer)
                return;
            Player owner = Main.player[arc.owner];
            int type = ModContent.ProjectileType<AisbergChunk>();
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (!p.active || p.type != type || p.owner != arc.owner)
                    continue;
                if (p.ModProjectile is not AisbergChunk chunk || chunk.cracked || chunk.Knocked)
                    continue;
                if (chunk.Age <= swingAge || !overlaps(p.Hitbox))
                    continue;
                chunk.Knock(owner);
            }
        }

        // Взмах попал по врагу: глыбы откалываются в месте попадания.
        // Пустой взмах сюда не попадает (зовёт только OnHitNPC дуги).
        public static void SpawnSwingChunks(Player owner, IEntitySource source, Vector2 at, int swordDamage, float knockback)
        {
            if (owner.whoAmI != Main.myPlayer)
                return;
            int impact = Math.Max(1, (int)(swordDamage * 0.3f));
            int blast = Math.Max(1, (int)(swordDamage * 0.45f));
            int type = ModContent.ProjectileType<AisbergChunk>();
            for (int i = 0; i < 3; i++)
            {
                float side = i - 1;
                Vector2 pos = at + new Vector2(side * 10f, -18f - Main.rand.NextFloat(8f));
                Vector2 vel = new Vector2(
                    owner.direction * (1.1f + Main.rand.NextFloat(1.2f)) + side * 0.8f,
                    -4.2f - Main.rand.NextFloat(1.8f)) + owner.velocity * 0.3f;
                Projectile.NewProjectile(source, pos, vel, type, impact, knockback * 0.4f,
                    owner.whoAmI, 0f, Main.rand.Next(3), blast);
            }
        }

        // Тычок и встреча попали по врагу: одна глыба над целью. size — масштаб
        // (0.95 обычная, 1.7 крупная), impactMult/blastMult — доли урона меча.
        public static void SpawnSingleChunk(Player owner, IEntitySource source, Vector2 at, int swordDamage,
            float knockback, float size, float impactMult, float blastMult)
        {
            if (owner.whoAmI != Main.myPlayer)
                return;
            int impact = Math.Max(1, (int)(swordDamage * impactMult));
            int blast = Math.Max(1, (int)(swordDamage * blastMult));
            int sizeCode = (int)Math.Round(MathHelper.Clamp(size, 0.5f, 3f) * 10f);
            Vector2 pos = at + new Vector2(0f, -22f - 8f * size);
            Vector2 vel = new Vector2(owner.direction * (1f + Main.rand.NextFloat(0.8f)), -5f - Main.rand.NextFloat(1.5f))
                + owner.velocity * 0.3f;
            Projectile.NewProjectile(source, pos, vel, ModContent.ProjectileType<AisbergChunk>(), impact,
                knockback * 0.5f, owner.whoAmI, size >= 1.3f ? 1f : 0f, Main.rand.Next(3) + 3 * sizeCode, blast);
        }

        // Бросок воткнулся: крупные глыбы из места втыкания.
        public static void SpawnThrowChunks(Player owner, IEntitySource source, Vector2 at, int swordDamage, float knockback)
        {
            if (owner.whoAmI != Main.myPlayer)
                return;
            int impact = Math.Max(1, (int)(swordDamage * 0.45f));
            int blast = Math.Max(1, (int)(swordDamage * 0.55f));
            int type = ModContent.ProjectileType<AisbergChunk>();
            for (int i = 0; i < 3; i++)
            {
                float side = i - 1;
                Vector2 pos = at + new Vector2(side * 14f, -26f);
                Vector2 vel = new Vector2(side * 2f + Main.rand.NextFloat(-0.6f, 0.6f),
                    -6.5f - Main.rand.NextFloat(2f));
                Projectile.NewProjectile(source, pos, vel, type, impact, knockback * 0.5f,
                    owner.whoAmI, 1f, Main.rand.Next(3), blast);
            }
        }
    }
}
