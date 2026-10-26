using System;
using System.Collections.Generic;
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

        // Амплитуда выдвига в тычке: меч полноразмерный (масштаб 1, как во взмахе
        // и встрече), но вдоль линии выпада сдвигается между оттянутой и выдвинутой
        // позой только на эту долю своей длины. Раньше эта же константа резала
        // масштаб спрайта и хитбокс — в тычке меч рисовался миниатюрным (0.35)
        // и колол лишь на треть клинка.
        public const float StabReachMult = 0.35f;

        public float BladeLength =>
            SickleSprite.Get((int)Projectile.ai[2]).Length;

        public float BladeHalfLength => BladeLength * 0.5f;

        // Тычок забирает горение: чем дольше цель горела, тем сильнее добивающий удар
        private const float BurnDamageBonus = 0.6f;
        private readonly Dictionary<int, int> takenBurn = new();

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

        public bool IsRetracting => Life >= ThrustPortion;

        public float Retract01 => Life <= ThrustPortion
            ? 0f
            : MathHelper.Clamp((Life - ThrustPortion) / (1f - ThrustPortion), 0f, 1f);

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
            // Как у ванильных копий: полноразмерный клинок сам выезжает вперёд и уходит
            // назад вдоль линии выпада. На пике (extend = 1) приклад у кисти, и остриё
            // достаёт на всю длину клинка; на старте и на возврате клинок оттянут назад
            // на StabReachMult своей длины.
            Vector2 butt = hand - Projectile.velocity * (GripInset + geo.Length * StabReachMult * (1f - extend));
            Projectile.Center = butt + Projectile.velocity * (geo.Length * 0.5f);

            // Поворот: вектор «приклад → остриё» спрайта ставим вдоль выпада.
            // При взгляде влево спрайт зеркалим (флип в PreDraw), угол — зеркального.
            // ЗНАК = (не +=) — иначе угол копится каждый тик и меч крутится.
            bool flipped = Projectile.spriteDirection == -1;
            Projectile.rotation = Projectile.velocity.ToRotation() - geo.AngleFor(flipped);

            // Огонь Фростбранда пускает сам снаряд (как встреча): линия клинка
            // ровно та, что только что поставлена выше. Раньше огонь пускал
            // SwordPlayer.PostUpdate — ДО обновления снарядов, по позиции
            // прошлого тика: на быстром выпаде пламя сидело позади клинка
            if ((int)Projectile.ai[2] == ItemID.Frostbrand)
            {
                var sp = player.GetModPlayer<Weapons.SwordPlayer>();
                FrostJetFx.EmitBlade(butt, butt + Projectile.velocity * geo.Length, Projectile.velocity,
                    sp.FireIntensity * (1f - 0.8f * Retract01), 1f, FrostJetFx.HandSource(Projectile.owner));
            }

            // Все колющие мечи чистят остриё об воздух: наледь и искры (визуал, все клиенты)
            if (!Main.dedServ)
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

            // Хитбокс — всё видимое лезвие, а не только кончик:
            // от рукояти до острия, чтобы удар не проходил сквозь середину меча
            Vector2 dir = Projectile.velocity.SafeNormalize(Vector2.UnitX);
            SickleSprite.Geometry geo = SickleSprite.Get((int)Projectile.ai[2]);
            Vector2 tipPos = Projectile.Center + dir * geo.FrontReach;
            Vector2 rearPos = Projectile.Center - dir * BladeHalfLength;
            Vector2 bladeStart = Vector2.Lerp(rearPos, tipPos, 0.18f);
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                bladeStart, tipPos, BladeHitWidth, ref collisionPoint);
        }

        // Толщина хитбокса вдоль лезвия (px)
        private const float BladeHitWidth = 10f;

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            // Тычок меча спавнится с knockBack = 0 (SwordTypes.StabKnockbackMult).
            // Отключаем отбрасывание полностью, чтобы его не вернули бонусы
            // отбрасывания игрока (перчатки и т.п.)
            if (Projectile.knockBack <= 0f)
                modifiers.DisableKnockback();

            // Фростбранд: тычок вытягивает горение из цели и превращает его в урон
            if ((int)Projectile.ai[2] == ItemID.Frostbrand) {
                int burn = Content.Weapons.FrostFireSystem.TakeBurn(target);
                if (burn > 0) {
                    takenBurn[target.whoAmI] = burn;
                    float ratio = burn / (float)Content.Weapons.FrostFireSystem.BurnTicks;
                    modifiers.SourceDamage *= 1f + BurnDamageBonus * ratio;
                }
            }
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            if ((int)Projectile.ai[2] == ItemID.IceBlade) {
                Main.player[Projectile.owner].GetModPlayer<Content.Weapons.SwordPlayer>().AddPolish((int)Projectile.ai[2]);
            }
            // Фростбранд: забрал горение — бонусный урон. Сам тычок НЕ поджигает
            // и струи у него нет: не горел враг — его подожгут веер дуги или
            // факел встречи. Подготовка тычка — дело других приёмов.
            // Умер от тычка с забранным горением — взрыв холодного огня.
            if ((int)Projectile.ai[2] == ItemID.Frostbrand) {
                bool tookBurn = takenBurn.Remove(target.whoAmI, out int burnTaken);
                // анимация: холодное пламя стекается с врага в клинок (FrostDrainFx)
                if (tookBurn)
                {
                    FrostDrainFx.Play(target, Main.player[Projectile.owner], burnTaken);
                    // Забранный огонь не растворяется: язык уходит в орбиту у
                    // игрока и уйдёт со следующим взмахом (конечный запас)
                    Main.player[Projectile.owner].GetModPlayer<Weapons.SwordPlayer>().OnFlameDrained(target);
                }
                if (tookBurn && target.life <= 0 && Projectile.owner == Main.myPlayer) {
                    Content.Weapons.FrostFireSystem.Explode(target, 120f, Projectile.damage / 2, Projectile.owner, false);
                }
            }
        }

        public override void OnKill(int timeLeft)
        {
            takenBurn.Clear();
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
            float drawScale = 1f; // полноразмерный меч, как во взмахе и встрече
            Vector2 drawPos = Projectile.Center - dir * (geo.Length * drawScale * 0.5f);
            Vector2 origin = geo.ButtFor(flipped);
            SpriteEffects effects = flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            Player owner = Main.player[Projectile.owner];

            // Blade
            if (itemType == ItemID.IceBlade) {
                float mix = Content.Weapons.IceBladeVisual.Mix(owner, itemType);
                Content.Weapons.IceBladeVisual.DrawBlend(texture, drawPos, geo, Projectile.rotation, flipped, lightColor, drawScale, mix);
            } else {
                Main.EntitySpriteDraw(texture, drawPos - Main.screenPosition, null,
                    lightColor, Projectile.rotation, origin, drawScale, effects, 0f);
            }

            return false;
        }

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

        // === Ice Blade: правка клинка встречным воздухом ===
        // Дуга правит клинок от пяты к острию, тычок — наоборот, от острия к пяте: клинок
        // входит в воздух как в ножны. С кромок сдувает наледь и искры правки, а в воздухе
        // остаётся след: ванильный разрез всегда, лента-шлейф — по заточке.
        private const float StoneStart = 0.05f;      // доля выпада, когда правка трогается с острия
        private const float StoneSpan = 0.42f;       // за какую долю выпада она доходит до пяты

        private float Env(float life) => Smooth01(life * 12f) * Smooth01((1f - life) * 6f);

        // Где правка: t вдоль клинка (1 — остриё, 0 — пята)
        private static float StoneProgress(float life) => Smooth01((life - StoneStart) / StoneSpan);

        private void BladeEnds(SickleSprite.Geometry geo, out Vector2 heel, out Vector2 tip)
        {
            Vector2 dir = Projectile.velocity;
            tip = Projectile.Center + dir * (geo.Length * 0.5f);
            heel = Fx.HeelOf(Projectile.Center - dir * (geo.Length * 0.5f), tip);
        }

        // Ванильная основа: струи вдоль клинка и росчерк за остриём, под клинком
        private void UpdateHone(Player player, SickleSprite.Geometry geo)
        {
            float life = Life;
            float env = Env(life);
            if (env <= 0.05f)
                return;
            float polish = Fx.PolishOf(player, (int)Projectile.ai[2]);
            float rough = 1f - polish;
            Vector2 dir = Projectile.velocity;
            BladeEnds(geo, out Vector2 heel, out Vector2 tip);
            float hw = Fx.BladeHalfWidth;
            Vector2 n = dir.RotatedBy(MathHelper.PiOver2);
            float s = StoneProgress(life);
            float t = 1f - s;

            // Частицы очищения — только пока клинок реально очищается (после настоящей полировки)
            bool cleaning = player.GetModPlayer<Content.Weapons.SwordPlayer>().IsPolishing((int)Projectile.ai[2]);

            // Под бруском поток сдирает наледь с кромок и уносит назад вдоль клинка
            if (cleaning && s > 0f && s < 1f) {
                int shavings = Fx.Count((0.3f + 0.7f * rough) * 0.9f * env);
                for (int i = 0; i < shavings; i++) {
                    int side = Main.rand.NextBool() ? 1 : -1;
                    Vector2 vel = -dir * Main.rand.NextFloat(1.2f, 2.6f) + n * side * Main.rand.NextFloat(0.3f, 1f);
                    Fx.SpawnShaving(Fx.EdgePoint(heel, tip, hw, t, side), vel, Main.rand.NextFloat(0.7f, 1.05f));
                }
                int sparks = Fx.Count((0.2f + 0.5f * polish) * 0.8f * env);
                for (int i = 0; i < sparks; i++) {
                    int side = Main.rand.NextBool() ? 1 : -1;
                    Vector2 vel = -dir * Main.rand.NextFloat(2f, 3.5f) + n * side * Main.rand.NextFloat(0.2f, 0.8f);
                    Fx.SpawnSpark(Fx.EdgePoint(heel, tip, hw, t, side), vel, 0.8f);
                }
                int snow = Fx.Count(env * 0.5f);
                for (int i = 0; i < snow; i++) {
                    int side = Main.rand.NextBool() ? 1 : -1;
                    Dust d = Dust.NewDustPerfect(Fx.EdgePoint(heel, tip, hw, t, side), DustID.Snow,
                        -dir * Main.rand.NextFloat(0.5f, 1.4f) + n * side * Main.rand.NextFloat(0.15f, 0.5f),
                        0, default, Main.rand.NextFloat(0.7f, 1f));
                    d.noGravity = true;
                }
                Lighting.AddLight(Vector2.Lerp(heel, tip, t), Fx.EdgeRim.ToVector3() * (0.2f + 0.5f * polish) * env * 0.7f);
            }

            // Пик выпада — один раз: правленые кромки звенят, остатки наледи стряхивает
            // с них наружу (не с острия, а по всей правленой длине)
            if (Projectile.localAI[1] == 0f && life >= ThrustPortion) {
                Projectile.localAI[1] = 1f;
                if (!cleaning)
                    return;
                int chips = (int)System.Math.Round(rough * 2f);
                for (int i = 0; i < chips; i++) {
                    int side = i % 2 == 0 ? 1 : -1;
                    Vector2 at = Fx.EdgePoint(heel, tip, hw, Main.rand.NextFloat(t, 1f), side);
                    Fx.SpawnShaving(at, n * side * Main.rand.NextFloat(0.8f, 1.8f) - dir * Main.rand.NextFloat(0.2f, 0.8f),
                        Main.rand.NextFloat(0.8f, 1.2f));
                }
                Lighting.AddLight(Vector2.Lerp(heel, tip, 0.6f), Fx.EdgeCore.ToVector3() * (0.25f + 0.5f * polish));
            }
        }
    }
}

