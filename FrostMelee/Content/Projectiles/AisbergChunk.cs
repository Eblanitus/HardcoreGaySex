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
    // ai[1] — вариант рисунка 0..2. ai[2] — урон всплеска при расколе.
    //
    // «Льдины»: глыба сама не взрывается. Падая, бьёт врагов собой и
    // ложится на землю льдиной; полежав, тихо тает. Взорвать её
    // (AisbergBlast: урон по площади + Frostbite) может только владелец:
    // - взмах отбивает льдину от себя, как бильярдный шар (KnockTouched),
    //   отбитая раскалывается о первого врага или о стену;
    // - тычок раскалывает на месте, встреча — с заморозкой поверхности
    //   (CrackTouched).
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

        private bool initialized;
        private bool cracked;
        private bool freezeSurfaceOnCrack;
        private int landedTicks;
        private float spin;

        // ai[0]: 0/1 — падает (обычная/крупная), 2/3 — лежит льдиной,
        // 4/5 — отбита взмахом и летит
        private bool IsLarge => (int)Projectile.ai[0] % 2 == 1;
        private bool Landed => Projectile.ai[0] >= 1.5f && Projectile.ai[0] < 3.5f;
        private bool Knocked => Projectile.ai[0] >= 3.5f;
        private float BlastRadius => IsLarge ? 130f : 80f;
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
            // Падающая глыба проходит сквозь врагов (кулдаун ниже), отбитая
            // раскалывается о первого сама (Crack в OnHitNPC)
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
                Projectile.scale = IsLarge ? LargeScale : SmallScale;
                Vector2 center = Projectile.Center;
                Projectile.width = Projectile.height = IsLarge ? 48 : 24;
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
                    Crack(false);
            }
            else
            {
                Projectile.velocity.Y = Math.Min(Projectile.velocity.Y + Gravity, MaxFall);
                Projectile.velocity.X *= 0.995f;
                Projectile.rotation += spin;
            }

            Lighting.AddLight(Projectile.Center, 0.08f, 0.2f, 0.3f);
            if (!Main.dedServ && Age % 6 == 0)
            {
                Dust d = Dust.NewDustPerfect(Projectile.Center + Main.rand.NextVector2Circular(8f, 8f),
                    DustID.Frost, -Projectile.velocity * 0.08f, 130, default, 0.8f);
                d.noGravity = true;
            }
        }

        // Лежащая льдина сама не бьёт: она снаряд для взмаха, а не ловушка
        public override bool? CanDamage() => !cracked && !Landed && (Knocked || Age > GraceTicks);

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            // Отбитая разбивается о стену или пол
            if (Knocked)
            {
                Crack(false);
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
                Crack(false);
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

            Projectile.ai[0] = IsLarge ? 5f : 4f;
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

        public void Crack(bool freezeSurface)
        {
            if (cracked || !Projectile.active)
                return;
            cracked = true;
            freezeSurfaceOnCrack |= freezeSurface;
            Projectile.Kill();
        }

        public override void OnKill(int timeLeft)
        {
            if (!Main.dedServ)
            {
                int dustCount = IsLarge ? 20 : 10;
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

            SoundEngine.PlaySound(SoundID.Item27 with { Volume = IsLarge ? 0.8f : 0.5f, Pitch = IsLarge ? -0.2f : 0.15f },
                Projectile.Center);
            Lighting.AddLight(Projectile.Center, 0.25f, 0.55f, 0.8f);

            if (Projectile.owner != Main.myPlayer)
                return;

            int blastDamage = Projectile.ai[2] > 0f ? (int)Projectile.ai[2] : Math.Max(1, Projectile.damage / 2);
            Projectile.NewProjectile(Projectile.GetSource_FromThis(), Projectile.Center, Vector2.Zero,
                ModContent.ProjectileType<AisbergBlast>(), blastDamage, Projectile.knockBack,
                Projectile.owner, BlastRadius);

            if (freezeSurfaceOnCrack)
                FreezeSurface(Projectile.Center, IsLarge ? 3 : 2);
        }

        // Заморозить поверхность под местом раскола своей глазурью
        // (IceSheetSystem: та же дорожка, что у скольжения и волны).
        private static void FreezeSurface(Vector2 center, int radiusTiles)
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
        public static void CrackTouched(Projectile blade, Func<Rectangle, bool> overlaps, bool freezeSurface)
        {
            if (blade == null || blade.owner != Main.myPlayer)
                return;
            int type = ModContent.ProjectileType<AisbergChunk>();
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (!p.active || p.type != type || p.owner != blade.owner)
                    continue;
                if (p.ModProjectile is AisbergChunk chunk && !chunk.cracked && overlaps(p.Hitbox))
                    chunk.Crack(freezeSurface);
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
