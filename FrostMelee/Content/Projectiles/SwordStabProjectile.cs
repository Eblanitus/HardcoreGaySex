using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Fx = FrostMelee.Content.Weapons.IceBladeFx;

namespace FrostMelee.Content.Projectiles
{
    // Выпад мечом: прямой удар остриём вперёд вместо дуги.
    // ai[0] — сколько тиков живёт выпад (интервал тычков из SwordPlayer)
    // ai[2] — тип предмета-меча (по нему рисуем его текстуру)
    public class SwordStabProjectile : ModProjectile
    {
        // Страховочная текстура (своя отрисовка всё равно по ai[2])
        public override string Texture => "FrostMelee/Items/IceSpear";

        // Профиль выпада (нелинейный, «резкий»):
        // первые ThrustPortion времени — бросок вперёд с резким стартом (ease-out),
        // остальное — медленный возврат (ease-in). Урон только пока остриё
        // выброшено (HitWindow) — на возврате меч уже не бьёт.
        private const float ThrustPortion = 0.3f;
        private const float HitWindow = 0.45f;

        public override void SetDefaults()
        {
            // Клонируем ванильное копьё: friendly, Melee, ownerHitCheck, penetrate и т.д.
            Projectile.CloneDefaults(ProjectileID.Spear);

            // Страховка по времени жизни: клон может дать недостаточный timeLeft —
            // PreAI потом обрежет до длительности выпада.
            Projectile.timeLeft = 60;

            // Меч: свой маленький хитбокс и защита от мультихита за выпад
            Projectile.width = 20;
            Projectile.height = 20;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 10;
            Projectile.tileCollide = false;
        }

        private int Duration => (int)MathHelper.Max(1f, Projectile.ai[0]);

        // 0 → 1 за время жизни выпада
        private float Life => 1f - Projectile.timeLeft / (float)Duration;

        public override bool PreAI()
        {
            Player player = Main.player[Projectile.owner];

            // Длительность выпада (ai[0] задаёт тот, кто спавнит)
            int duration = Duration;

            // КРИТИЧНО: клон ванильного копья даёт hide = true — снаряд НЕ рисуется
            // в своём проходе. Его рисует слой игрока через heldProj.
            // Без heldProj тычок с hide=true не рисуется НИГДЕ.
            player.heldProj = Projectile.whoAmI;

            if (Projectile.timeLeft > duration)
                Projectile.timeLeft = duration;

            // Звук выпада — на первом тике, у всех клиентов
            if (Projectile.localAI[0] == 0f)
            {
                Projectile.localAI[0] = 1f;
                SoundEngine.PlaySound(SoundID.Item1 with { Pitch = 0.5f, Volume = 0.6f }, player.Center);
            }

            // velocity хранит направление выпада
            Projectile.velocity = Projectile.velocity.SafeNormalize(Vector2.UnitX);
            Projectile.spriteDirection = Projectile.velocity.X >= 0 ? 1 : -1;

            // Нелинейный выпад: резкий бросок вперёд, затем неспешный возврат
            float life = Life;
            float extend;
            if (life < ThrustPortion)
            {
                float t = life / ThrustPortion;
                extend = 1f - (1f - t) * (1f - t) * (1f - t); // ease-out cubic
            }
            else
            {
                float t = (life - ThrustPortion) / (1f - ThrustPortion);
                extend = 1f - t * t; // ease-in quad
            }

            // Выпад делает РУКА: она разгибается вместе с выпадом (согнута → прямая),
            // а меч привязан к кисти.
            // Рука чуть выше линии выпада (ArmLift): кисть у плеча, удар сверху,
            // меч при этом смотрит ровно на цель
            float armRot = Projectile.velocity.ToRotation() - ArmLift * Projectile.spriteDirection - MathHelper.PiOver2;
            Player.CompositeArmStretchAmount stretch = ArmStretch(extend);
            player.SetCompositeArmFront(true, stretch, armRot);
            HideBackArm(player, Projectile.velocity);

            // Геометрия реального спрайта (приклад/остриё по пикселям — SickleSprite).
            SickleSprite.Geometry geo = SickleSprite.Get((int)Projectile.ai[2]);
            Vector2 hand = SickleSprite.HandGrip(player, stretch, armRot);
            Vector2 butt = hand - Projectile.velocity * GripInset;
            Projectile.Center = butt + Projectile.velocity * (geo.Length * 0.5f);

            // Поворот: вектор «приклад → остриё» спрайта ставим вдоль выпада.
            // При взгляде влево спрайт зеркалим (флип в PreDraw), угол — зеркального.
            // ЗНАК = (не +=) — иначе угол копится каждый тик и меч крутится.
            bool flipped = Projectile.spriteDirection == -1;
            Projectile.rotation = Projectile.velocity.ToRotation() - geo.AngleFor(flipped);

            // Ice Blade правит остриё об воздух (визуал, все клиенты)
            if (!Main.dedServ && (int)Projectile.ai[2] == ItemID.IceBlade)
                UpdateHone(player, geo);

            return false; // своя AI вместо ванильной
        }

        // Насколько рука в выпаде поднята над линией удара (рад)
        public const float ArmLift = 0.3f;

        // Вторая рука — как у ванильных копий (useStyle Shoot): композитная задняя рука
        // ВЫКЛЮЧЕНА, а кадр тела выбирается по наклону оружия (как в ванильном PlayerFrame:
        // выше 0.75 рад — кадр 2, ниже -0.6 рад — кадр 4, иначе кадр 3).
        public static void HideBackArm(Player player, Vector2 dir)
        {
            player.SetCompositeArmBack(false, Player.CompositeArmStretchAmount.None, 0f);
            float elevation = (float)System.Math.Asin(MathHelper.Clamp(-dir.Y * player.gravDir, -1f, 1f));
            int row = 3;
            if (elevation > 0.75f)
                row = 2;
            else if (elevation < -0.6f)
                row = 4;
            player.bodyFrame.Y = player.bodyFrame.Height * row;
        }

        // Сколько рукояти торчит позади кисти (px)
        protected virtual float GripInset => 4f;

        // Выпад рукой: согнута на старте, прямая на пике выпада
        private static Player.CompositeArmStretchAmount ArmStretch(float extend)
        {
            if (extend < 0.25f)
                return Player.CompositeArmStretchAmount.None;
            if (extend < 0.5f)
                return Player.CompositeArmStretchAmount.Quarter;
            if (extend < 0.8f)
                return Player.CompositeArmStretchAmount.ThreeQuarters;
            return Player.CompositeArmStretchAmount.Full;
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            // Бьёт только фаза выпада: на медленном возврате урона нет
            if (Life > HitWindow)
                return false;

            // Хитбокс — только кончик: отрезок длиной BladeHitLength, который кончается
            // РОВНО на видимом острие
            Vector2 dir = Projectile.velocity.SafeNormalize(Vector2.UnitX);
            float half = SickleSprite.Get((int)Projectile.ai[2]).Length * 0.5f;
            Vector2 tipPos = Projectile.Center + dir * half;
            Vector2 bladeStart = tipPos - dir * System.Math.Min(BladeHitLength, half * 2f);
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                bladeStart, tipPos, BladeHitWidth, ref collisionPoint);
        }

        // Бьющая часть у острия: длина вдоль меча и толщина (px)
        private const float BladeHitLength = 14f;
        private const float BladeHitWidth = 8f;

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            // Тычок меча спавнится с knockBack = 0 (SwordTypes.StabKnockbackMult).
            // Отключаем отбрасывание полностью, чтобы его не вернули бонусы
            // отбрасывания игрока (перчатки и т.п.)
            if (Projectile.knockBack <= 0f)
                modifiers.DisableKnockback();
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            if ((int)Projectile.ai[2] == ItemID.IceBlade) {
                Main.player[Projectile.owner].GetModPlayer<Content.Weapons.SwordPlayer>().AddPolish();
            }
            // Фростбранд: поджигает; умер, пока горел, — взрыв холодного огня
            if ((int)Projectile.ai[2] == ItemID.Frostbrand) {
                bool wasBurning = Content.Weapons.FrostFireSystem.WasBurning(target);
                Content.Weapons.FrostFireSystem.Ignite(target, Projectile.owner);
                if (wasBurning && target.life <= 0 && Projectile.owner == Main.myPlayer)
                    Content.Weapons.FrostFireSystem.Explode(target, 120f, Projectile.damage / 2, Projectile.owner, false);
            }
        }

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

            // Якорь отрисовки — реальный ПРИКЛАД спрайта, поставленный у кисти.
            // При флипе картинка зеркалится внутри прямоугольника, а origin — нет,
            // поэтому приклад берём уже зеркальный (ButtFor(flipped)).
            SickleSprite.Geometry geo = SickleSprite.Get(itemType);
            bool flipped = Projectile.spriteDirection == -1;
            Vector2 dir = Projectile.velocity.SafeNormalize(Vector2.UnitX);
            Vector2 drawPos = Projectile.Center - dir * (geo.Length * 0.5f);
            Vector2 origin = geo.ButtFor(flipped);
            SpriteEffects effects = flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            Player owner = Main.player[Projectile.owner];
            if (itemType == ItemID.IceBlade) {
                // Ice Blade: игла и V-волна под клинком, блик и «дзинь» поверх
                Texture2D glow = Fx.Glow();
                if (glow != null)
                    DrawHoneBehind(owner, geo, glow);
                float mix = Content.Weapons.IceBladeVisual.Mix(owner, itemType);
                Content.Weapons.IceBladeVisual.DrawBlend(texture, drawPos, SickleSprite.Get(itemType), Projectile.rotation, flipped, lightColor, 1f, mix);
                if (glow != null)
                    DrawHoneFront(owner, geo, glow);
                return false;
            } else {
                Main.EntitySpriteDraw(texture, drawPos - Main.screenPosition, null,
                    lightColor, Projectile.rotation, origin, 1f, effects, 0f);
            }

            // Шлейф тычка: то же семейство эффекта, что у дуги взмаха —
            // лента-шлейф за остриём и яркая голова, палитра от полировки
            DrawThrustTrail(owner);
            return false;
        }

        // === Шлейф тычка: StabRibbon + вспышка острия, палитра как у дуги ===
        private const int TrailPoints = 10;
        private const float TrailTicks = 8f;
        private static readonly Vector2[] trailC = new Vector2[TrailPoints];
        private static readonly Vector2[] trailN = new Vector2[TrailPoints];

        private static float Smooth01(float x)
        {
            x = MathHelper.Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        // То же продвижение выпада, что в PreAI, но для произвольного момента life
        private float ExtendAt(float life)
        {
            if (life < ThrustPortion)
            {
                float t = life / ThrustPortion;
                return 1f - (1f - t) * (1f - t) * (1f - t);
            }
            float t2 = (life - ThrustPortion) / (1f - ThrustPortion);
            return 1f - t2 * t2;
        }

        // === Ice Blade: правка острия об воздух ===
        // Дуга правит всю длину клинка, тычок — остриё: воздух впереди рассекается иглой,
        // обтекает клинок V-волной вдоль кромок, блик бежит от острия к пяте.
        // На пике — «дзинь»: с острия по инерции слетает наледь (шершавый) или
        // назад по волне брызжут искры (отточенный).
        private const float NeedleLength = 46f;     // длина иглы рассечённого воздуха, px
        private const float WakeSpread = 0.22f;     // рад: раскрытие V-волны от оси
        private const float TingWidthTicks = 1.8f;  // ширина пика «дзинь», тиков

        private float Env(float life) => Smooth01(life * 12f) * Smooth01((1f - life) * 6f);

        // Остриё на пике выпада — от него отрывается игла
        private Vector2 PeakTip(Player player, SickleSprite.Geometry geo)
        {
            Vector2 vel = Projectile.velocity;
            float armRot = vel.ToRotation() - ArmLift * Projectile.spriteDirection - MathHelper.PiOver2;
            Vector2 hand = SickleSprite.HandGrip(player, Player.CompositeArmStretchAmount.Full, armRot);
            return hand + vel * (geo.Length - GripInset);
        }

        private void UpdateHone(Player player, SickleSprite.Geometry geo)
        {
            float life = Life;
            float env = Env(life);
            float polish = Fx.PolishOf(player);
            float rough = 1f - polish;
            Vector2 dir = Projectile.velocity;
            Vector2 nrm = dir.RotatedBy(MathHelper.PiOver2);
            Vector2 tip = Projectile.Center + dir * (geo.Length * 0.5f);
            Vector2 butt = Projectile.Center - dir * (geo.Length * 0.5f);

            // Пока клинок летит вперёд, встречный воздух сдирает наледь вдоль клинка назад
            if (life < 0.55f && env > 0.05f) {
                int shavings = Fx.Count(rough * 1.6f * env);
                for (int i = 0; i < shavings; i++) {
                    Vector2 at = Vector2.Lerp(tip, butt, Main.rand.NextFloat(0.05f, 0.8f)) + nrm * Main.rand.NextFloat(-3f, 3f);
                    Vector2 vel = -dir * Main.rand.NextFloat(1f, 2.5f) + nrm * Main.rand.NextFloat(-0.8f, 0.8f);
                    Fx.SpawnShaving(at, vel, Main.rand.NextFloat(0.7f, 1.05f));
                }
            }

            // Пик выпада — один раз: «дзинь» на острие
            if (Projectile.localAI[1] == 0f && life >= ThrustPortion) {
                Projectile.localAI[1] = 1f;
                int chips = (int)System.Math.Round(rough * 5f);
                for (int i = 0; i < chips; i++)
                    Fx.SpawnShaving(tip, dir.RotatedByRandom(0.35f) * Main.rand.NextFloat(2f, 4.5f), Main.rand.NextFloat(0.9f, 1.3f));
                int sparks = (int)System.Math.Round(2f + polish * 4f);
                for (int i = 0; i < sparks; i++) {
                    float side = (i % 2 == 0 ? 1f : -1f) * (WakeSpread + Main.rand.NextFloat(-0.1f, 0.1f));
                    Fx.SpawnSpark(tip, (-dir).RotatedBy(side) * Main.rand.NextFloat(2f, 4f), Main.rand.NextFloat(0.9f, 1.2f));
                }
            }

            if (env > 0.05f)
                Lighting.AddLight(tip, Fx.EdgeRim.ToVector3() * (0.2f + 0.5f * polish) * env * 0.7f);
        }

        // Под спрайтом: игла впереди острия и V-волна вдоль кромок
        private void DrawHoneBehind(Player player, SickleSprite.Geometry geo, Texture2D glow)
        {
            float life = Life;
            float env = Env(life);
            if (env <= 0.02f)
                return;
            float polish = Fx.PolishOf(player);
            Vector2 dir = Projectile.velocity;
            Vector2 tip = Projectile.Center + dir * (geo.Length * 0.5f);

            // Игла: растёт вместе с выпадом, после пика отрывается и улетает вперёд, тая
            float needleA = env * (0.35f + 0.65f * polish);
            Vector2 start;
            float len;
            if (life < ThrustPortion) {
                start = tip;
                len = NeedleLength * (0.6f + 0.4f * polish) * ExtendAt(life);
            } else {
                float after = (life - ThrustPortion) * Duration;  // тиков после пика
                start = PeakTip(player, geo) + dir * after * 3f;
                len = NeedleLength * (0.6f + 0.4f * polish) * (1f - 0.5f * Smooth01(after / 8f));
                needleA *= 1f - Smooth01((life - ThrustPortion) / 0.35f);
            }
            if (needleA > 0.02f && len > 2f) {
                Fx.Stroke(glow, start, start + dir * len, 3f, Fx.Additive(Fx.EdgeRim, needleA * 0.55f));
                Fx.Stroke(glow, start, start + dir * len * 0.6f, 1.2f, Fx.Additive(Fx.EdgeCore, needleA));
            }

            // V-волна: воздух расходится от острия вдоль кромок. Шершавый клинок гонит
            // матовую морозную волну, отточенный — две тонкие светлые линии
            float wake = env * (1f - Smooth01((life - 0.25f) / 0.3f));
            if (wake > 0.02f) {
                float wakeLen = geo.Length * 0.55f * (0.5f + 0.5f * ExtendAt(life));
                float rough = 1f - polish;
                for (int s = -1; s <= 1; s += 2) {
                    Vector2 back = (-dir).RotatedBy(s * WakeSpread);
                    Vector2 a = tip - dir * 2f;
                    Vector2 b = a + back * wakeLen;
                    if (rough > 0.03f)
                        Fx.Stroke(glow, a, b, 5f, Fx.FrostMatte * (wake * rough * 0.4f));
                    Fx.Stroke(glow, a, Vector2.Lerp(a, b, 0.8f), 1.4f, Fx.Additive(Fx.EdgeRim, wake * (0.3f + 0.7f * polish)));
                }
            }
        }

        // Поверх спрайта: блик от острия к пяте и «дзинь» на пике
        private void DrawHoneFront(Player player, SickleSprite.Geometry geo, Texture2D glow)
        {
            float life = Life;
            float env = Env(life);
            if (env <= 0.02f)
                return;
            float polish = Fx.PolishOf(player);
            Vector2 dir = Projectile.velocity;
            Vector2 tip = Projectile.Center + dir * (geo.Length * 0.5f);
            Vector2 butt = Projectile.Center - dir * (geo.Length * 0.5f);

            float s = Smooth01((life - 0.08f) / 0.5f);
            Vector2 glint = Vector2.Lerp(tip, butt, s * 0.85f);
            Fx.DrawHone(glow, tip, glint, dir, polish, env * (0.45f + 0.55f * polish) * Smooth01(life / 0.08f));

            float d = (life - ThrustPortion) * Duration / TingWidthTicks;
            float ting = (float)System.Math.Exp(-d * d) * env;
            if (ting > 0.03f)
                Fx.DrawTing(glow, tip, 22f + 30f * polish, dir.ToRotation(), polish, ting * (0.55f + 0.45f * polish));
        }

        private void DrawThrustTrail(Player player)
        {
            if (Main.dedServ)
                return;

            float life = Life;
            float env = Smooth01(life * 12f) * Smooth01((1f - life) * 6f);
            if (env <= 0.02f)
                return;

            SickleSprite.Geometry geo = SickleSprite.Get((int)Projectile.ai[2]);
            Vector2 vel = Projectile.velocity;
            float armRot = vel.ToRotation() - ArmLift * Projectile.spriteDirection - MathHelper.PiOver2;

            // История острия аналитически: рука разгибалась по той же кривой,
            // поэтому лента ложится ровно туда, где прошёл кончик клинка
            int n = 0;
            float step = TrailTicks / Duration / (TrailPoints - 1);
            for (int i = 0; i < TrailPoints; i++)
            {
                float li = life - i * step;
                if (li < 0f)
                    break;
                Vector2 hand = SickleSprite.HandGrip(player, ArmStretch(ExtendAt(li)), armRot);
                trailC[n++] = hand + vel * (geo.Length - GripInset);
            }
            if (n < 2)
                return;

            Vector2 nrm = vel.RotatedBy(MathHelper.PiOver2);
            for (int i = 0; i < n; i++)
                trailN[i] = nrm;

            float polish = 1f - player.GetModPlayer<Weapons.SwordPlayer>().IceFrostShown;
            IceArcFx.PolishColors(polish, out Color core, out Color tail, out Color edge);
            StabRibbon.Draw(trailC, trailN, n, 11f, core, tail, edge, env);

            // Голова шлейфа: яркая точка у острия, как у дуги взмаха
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow == null || glow.IsDisposed)
                return;
            Main.EntitySpriteDraw(glow, trailC[0] - Main.screenPosition, null,
                IceArcFx.Additive(core, env), 0f, glow.Size() / 2f,
                14f / glow.Width, SpriteEffects.None, 0f);
        }
    }
}
