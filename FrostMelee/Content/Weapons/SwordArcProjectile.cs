using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using FrostMelee.Content;
using FrostMelee.Content.Projectiles;

namespace FrostMelee.Content.Weapons
{
    // Снаряд дуги для ледяных мечей (текстура — ванильная дуга меча Excalibur).
    // Копия WoodSwordArcProjectile (WoodSwordsOverhaul) + ледяной след:
    // на swept-веер ложится слабый иней FrostTrailSystem (только Slow, без Frostbite).
    // Дуга — единственный удар взмаха (у самого меча хитбокс убран, см. SwordGlobalItem):
    // бьёт от рукояти до края нарисованной дуги, по каждой цели раз за взмах.
    // В зоне клинка — полный урон и отбрасывание, дальше к краю они падают до крошечных.
    public class SwordArcProjectile : ModProjectile
    {
        public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.Excalibur;

        // === Геометрия нарисованной дуги (по ней же считается хитбокс) ===
        // Снята с текстуры Excalibur: кадр 170×170, центр дуги — центр кадра.
        // В игре видна в основном тонкая линия (кадр 3): в текстуре она лежит на радиусе
        // 78..82 px и по углу от -74° (хвост) до +56° (голова, сторона взмаха) от оси снаряда.
        // Тело дуги (кадр 0) почти прозрачное (цвет ×0.15 / ×0.25) и в хитбокс не входит.
        // PreDraw рисует только линию: масштаб BodyDrawScale, сверху ×LineDrawScale. Тело не рисуется.
        private const float BodyDrawScale = 0.75f;
        private const float LineDrawScale = 0.8f;
        private const float LineTexOuter = 82f;
        private static readonly float ArcAngleBack = MathHelper.ToRadians(74f);
        private static readonly float ArcAngleFront = MathHelper.ToRadians(56f);
        // Дуга растёт за взмах: масштаб от ArcScaleStart до ArcScaleStart + ArcScaleGrowth (× ai[2])
        private const float ArcScaleStart = 1f;
        private const float ArcScaleGrowth = 0.6f;

        // Урон и отбрасывание на краю полностью раскрытой дуги (доля от полных).
        // Отброс на краю заметный (дуга = контроль, не урон).
        private const float EdgeDamageMult = 0.15f;
        private const float EdgeKnockbackMult = 0.2f;
        // Урон падает от полного до EdgeDamageMult за первую долю промежутка «клинок → внешний край»
        // (smoothstep), дальше держится на EdgeDamageMult. Отброс по-прежнему спадает линейно.
        private const float DamageFalloffSpan = 0.28f;
        // Нижняя граница урона дуги до защиты врага, в единицах урона: дробных/нулевых значений нет
        private const float MinEdgeDamage = 1f;

        // Насколько кисть в ванильном взмахе отстоит от центра игрока (до ~15px):
        // остриё дотягивается на длину клинка плюс это смещение
        public const float HandReach = 15f;

        // Докуда от центра игрока достаёт сам клинок: длина клинка (рукоять → остриё
        // по реальным пикселям) × размер оружия + HandReach. Ближе — полный урон, и хитбокс
        // никогда не короче клинка. Задаёт SwordGlobalItem.Shoot у владельца:
        // попадания считает только он.
        public float BladeReach;

        // Каким мечом спавнена дуга: ItemID предмета. Задаёт SwordGlobalItem.Shoot
        // вместе с BladeReach.
        public int SwordItem;

        // true — показать пылью зону клинка (полный урон) и внешний край хитбокса
        public static bool DebugHitbox = false;

        // Палитра ленты следует за полировкой клинка: шершавый меч даёт
        // матовый морозный шлейф, отполированный — яркое сияние
        private void GlowColors(out Color core, out Color tail, out Color edge) {
            float polish = 1f - Main.player[Projectile.owner].GetModPlayer<SwordPlayer>().IceFrostShown;
            IceArcFx.PolishColors(polish, out core, out tail, out edge);
        }

        public override void SetStaticDefaults() {
            Main.projFrames[Type] = 4;
        }

        public override void SetDefaults() {
            Projectile.width = 16;
            Projectile.height = 16;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.ownerHitCheck = true;
            // Colliding вызывается только для целей в этом радиусе от игрока.
            // 300 — как у ванильных дуг (Excalibur)
            Projectile.ownerHitCheckDistance = 300f;
            // Меч сам не бьёт, так что делить с ним кулдаун не нужно:
            // дуга бьёт каждую цель раз за взмах (localNPCHitCooldown = -1)
            Projectile.usesOwnerMeleeHitCD = false;
            Projectile.stopsDealingDamageAfterPenetrateHits = true;
            Projectile.aiStyle = -1;
            Projectile.noEnchantmentVisuals = true;
        }

        public override void AI() {
            Projectile.localAI[0]++;
            Player player = Main.player[Projectile.owner];
            float maxAnim = Math.Max(1f, Projectile.ai[1]);
            GetArcState(player, Projectile.localAI[0], out float rotation, out float scale);
            Projectile.rotation = rotation;
            Projectile.scale = scale;
            Projectile.Center = player.RotatedRelativePoint(player.MountedCenter) - Projectile.velocity;

            if (Projectile.owner == Main.myPlayer)
                RecordFrostTrail(player);

            // Кристаллики инея вдоль линии дуги (визуал, все клиенты)
            if (!Main.dedServ)
                SpawnFrostDust(player);

            if (DebugHitbox && Projectile.owner == Main.myPlayer)
                ShowHitbox(player);

            if (Projectile.localAI[0] >= maxAnim) {
                Projectile.Kill();
            }

            // Визуальные эффекты от фляг
            float enchantmentDistance = 40f * Projectile.ai[2];
            Vector2 enchantmentPosition = Projectile.Center + Projectile.rotation.ToRotationVector2() * enchantmentDistance;
            Rectangle rectangle = Utils.CenteredRectangle(enchantmentPosition, new Vector2(30f * Projectile.ai[2], 30f * Projectile.ai[2]));
            Projectile.EmitEnchantmentVisualsAt(rectangle.TopLeft(), rectangle.Width, rectangle.Height);
        }

        // Поворот и масштаб дуги на момент time (тиков от спавна) —
        // одна формула для отрисовки (AI), пыли и для хитбокса (Colliding).
        // Пока идёт взмах, дуга идёт ЗА КЛИНКОМ: угол берём из боевой позы
        // ваниллы (itemLocation/itemRotation), так дуга не отстаёт от меча.
        // Без активного взмаха (хвост дуги) — старая линейная формула.
        private float ArcRotation(Player player, float time) {
            if (!player.HeldItem.IsAir && player.itemAnimation > 0) {
                SickleSprite.Geometry geo = SickleSprite.Get(player.HeldItem.type);
                if (geo.Length > 1f) {
                    // Берём не «от игрока до меча» (это рукоять, а не клинок), а само
                    // направление клинка: приклад → остриё.
                    // Ванилла рисует предмет при origin в УГЛУ кадра, но для вектора
                    // это неважно — важны только точки приклада и острия и зеркалирование.
                    // itemRotation уже в зеркальной системе взгляда, флип учтён в TipFor/ButtFor,
                    // поэтому никаких +pi: именно из-за них дуга при взмахе влево уходила вправо.
                    bool flipped = player.direction == -1;
                    float itemRot = player.itemRotation;
                    Vector2 bladeDir = (geo.TipFor(flipped) - geo.ButtFor(flipped)).RotatedBy(itemRot);
                    if (bladeDir.LengthSquared() > 1f)
                        return bladeDir.ToRotation();
                }
            }
            float percentageOfLife = MathHelper.Clamp(time, 0f, Math.Max(1f, Projectile.ai[1])) / Math.Max(1f, Projectile.ai[1]);
            return Projectile.velocity.ToRotation() + Projectile.ai[0] * MathHelper.Pi * percentageOfLife + player.fullRotation + MathHelper.Pi;
        }

        private float ArcScaleAt(float time) {
            float percentageOfLife = MathHelper.Clamp(time, 0f, Math.Max(1f, Projectile.ai[1])) / Math.Max(1f, Projectile.ai[1]);
            return (ArcScaleStart + percentageOfLife * ArcScaleGrowth) * Projectile.ai[2];
        }

        private void GetArcState(Player player, float time, out float rotation, out float scale) {
            rotation = ArcRotation(player, time);
            scale = ArcScaleAt(time);
        }

        // === Ледяной след: слабый иней вдоль веера взмаха (только Slow) ===
        // Точки брать по текущему углу дуги, на трёх радиусах через лезвие —
        // за взмах собирается веер снега, как у взмаха серпа, но слабый.
        private FrostTrailSystem.Trail frostTrail;

        private void RecordFrostTrail(Player player) {
            if (Main.dedServ)
                return;
            float outer = Math.Max(LineTexOuter * ArcScaleAt(Projectile.localAI[0]) * BodyDrawScale * LineDrawScale, BladeReach);
            if (outer <= 1f)
                return;
            if (frostTrail == null)
                frostTrail = FrostTrailSystem.Begin(true, false);
            // За тик заметается угол между прошлым и текущим поворотом линии:
            // точки раскладываем по трем направлениям на трех радиусах,
            // чтобы веер снега не редел на быстрых взмахах
            float rot0 = ArcRotation(player, Projectile.localAI[0] - 1f);
            float rot1 = ArcRotation(player, Projectile.localAI[0]);
            for (int s = 0; s <= 2; s++) {
                Vector2 dir = MathHelper.Lerp(rot0, rot1, s / 2f).ToRotationVector2();
                foreach (float f in FrostTrailRadii)
                    frostTrail.Add(player.MountedCenter + dir * outer * f + Main.rand.NextVector2Circular(3f, 3f));
            }
        }

        private static readonly float[] FrostTrailRadii = new float[] { 0.5f, 0.8f, 1f };

        // Инеевая роса вдоль линии дуги: кристаллики появляются чуть позади
        // движущейся головы — свежая полоса льда тут же осыпается снегом.
        private void SpawnFrostDust(Player player) {
            Vector2 center = player.RotatedRelativePoint(player.MountedCenter);
            float radius = LineTexOuter * ArcScaleAt(Projectile.localAI[0]) * BodyDrawScale * LineDrawScale;
            if (radius <= 1f)
                return;
            int dir = Projectile.ai[0] >= 0f ? 1 : -1;
            for (int i = 0; i < 2; i++) {
                float back = MathHelper.ToRadians(Main.rand.NextFloat(0f, 45f));   // от головы к хвосту
                float ang = IceArcFx.HeadAngleOf(Projectile.rotation, dir) - dir * back;
                Vector2 at = center + ang.ToRotationVector2() * radius * Main.rand.NextFloat(0.9f, 1.06f);
                Dust d = Dust.NewDustPerfect(at, Main.rand.NextBool(3) ? DustID.SnowflakeIce : DustID.Frost,
                    Main.rand.NextVector2Circular(0.8f, 0.8f), 110, default, Main.rand.NextFloat(0.7f, 1.2f));
                d.noGravity = true;
                d.fadeIn = 0.35f;
            }
        }

        // Настоящая линия лезвия в мире: по ванильной позе (itemLocation/itemRotation),
        // работает только во время взмаха.
        public static bool GetBladeLine(Player player, out Vector2 butt, out Vector2 tip) {
            butt = tip = default;
            if (player.HeldItem.IsAir || player.itemAnimation <= 0)
                return false;
            SickleSprite.Geometry geo;
            try { geo = SickleSprite.Get(player.HeldItem.type); }
            catch { return false; }
            if (geo.Length <= 1f)
                return false;
            Texture2D itemTexture = TextureAssets.Item[player.HeldItem.type].Value;
            bool flipped = player.direction == -1;
            // Ванильный origin предмета в руке — угол кадра, а не центр
            // (PlayerDrawLayers: W/2 - W/2*direction по X, H по Y).
            Vector2 origin = new Vector2(itemTexture.Width * 0.5f - itemTexture.Width * 0.5f * player.direction,
                itemTexture.Height);
            // itemRotation уже зеркальная, флип учтён в TipFor/ButtFor — +pi здесь лишний
            float rot = player.itemRotation;
            float scale = player.GetAdjustedItemScale(player.HeldItem);
            butt = player.itemLocation + DrawOffset(geo.ButtFor(flipped), origin, flipped).RotatedBy(rot) * scale;
            tip = player.itemLocation + DrawOffset(geo.TipFor(flipped), origin, flipped).RotatedBy(rot) * scale;
            return true;
        }

        // Смещение точки спрайта от ванильного origin с учётом горизонтального флипа
        private static Vector2 DrawOffset(Vector2 point, Vector2 origin, bool flipped) {
            Vector2 local = point - origin;
            if (flipped)
                local.X = -local.X;
            return local;
        }

        public override void OnKill(int timeLeft) {
            frostTrail?.Close();
            frostTrail = null;
        }

        // === Лента-шлейф взмаха, головная точка и пик-вспышка: тот же IceRibbon, что у серпа ===
        // История углов — ArcRotation на столько тиков назад (TrailTicks), впереди головы только
        // текущая точка. Радиусы по ArcScaleAt каждого момента: лента растёт вместе с дугой.
        private const float TrailTicks = 14f;        // на сколько тиков назад тянется лента
        private const float TrailMaxAngle = 2.2f;    // рад: лента не оборачивается вокруг игрока
        private const int TrailPoints = 16;          // точек ленты
        private const float TrailInnerFrac = 0.55f;  // внутренний край ленты: доля радиуса от внешнего
        private const float TrailHeadDot = 18f;      // размер яркой точки у острия, px
        private const float PeakAt = 0.5f;           // доля взмаха, где пик
        private const float PeakWidthTicks = 3.5f;   // ширина пика (тиков)
        private const float PeakFlashSize = 70f;     // размер вспышки у острия, px
        private const float PeakFlashAlpha = 0.7f;   // её яркость
        private const float PeakStarSize = 64f;      // длина лучей звезды вспышки, px

        private static readonly float[] swooshAngles = new float[TrailPoints];
        private static readonly float[] swooshROut = new float[TrailPoints];
        private static readonly float[] swooshRIn = new float[TrailPoints];

        private static float Smooth01(float x) {
            x = MathHelper.Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        private static float SwingEnv(float ticks, float maxTicks) {
            // нарастает RiseTicks тиков, гаснет FadeTicks тиков, между ними держится
            return Smooth01(ticks / 3f) * Smooth01((maxTicks - ticks) / 7f);
        }

        private static float PeakPulse(float progress, float maxTicks) {
            float d = (progress - PeakAt) * maxTicks / Math.Max(0.01f, PeakWidthTicks);
            return (float)Math.Exp(-d * d);
        }

        // Радиус линии дуги на момент time, но никогда не короче клинка
        private float ArcLineRadius(float time) {
            return Math.Max(LineTexOuter * ArcScaleAt(time) * BodyDrawScale * LineDrawScale, BladeReach);
        }

        private void DrawSwoosh(Player player, Texture2D glow, float progress, float maxAnim) {
            float env = SwingEnv(Projectile.localAI[0], maxAnim);
            if (env <= 0.02f || Main.dedServ)
                return;
            Vector2 center = player.RotatedRelativePoint(player.MountedCenter);
            GlowColors(out Color core, out Color tail, out Color edge);

            float head = Projectile.rotation;
            float step = TrailTicks / (TrailPoints - 1);
            int n = 1;
            swooshAngles[0] = head;
            swooshROut[0] = ArcLineRadius(Projectile.localAI[0]);
            swooshRIn[0] = Math.Max(BladeReach * 0.2f, swooshROut[0] * TrailInnerFrac);
            for (int i = 1; i < TrailPoints; i++) {
                float ti = Projectile.localAI[0] - i * step;
                if (ti < 0f)
                    break;
                float angle = ArcRotation(player, ti);
                float delta = Math.Abs(angle - head);
                if (delta > TrailMaxAngle) {
                    // последняя точка — ровно по TrailMaxAngle, дальше ленту не тянем
                    float prevDelta = Math.Abs(swooshAngles[n - 1] - head);
                    float f = (TrailMaxAngle - prevDelta) / Math.Max(0.0001f, delta - prevDelta);
                    swooshAngles[n] = MathHelper.Lerp(swooshAngles[n - 1], angle, MathHelper.Clamp(f, 0f, 1f));
                    n++;
                    break;
                }
                swooshAngles[n] = angle;
                swooshROut[n] = ArcLineRadius(Math.Max(0f, ti));
                swooshRIn[n] = Math.Max(BladeReach * 0.2f, swooshROut[n] * TrailInnerFrac);
                n++;
            }

            if (n >= 2) {
                GraphicsDevice device = Main.graphics.GraphicsDevice;
                Main.spriteBatch.End();
                try {
                    IceRibbon.Draw(device, swooshAngles, n, center, swooshROut, swooshRIn,
                        Projectile.localAI[0], env, core, tail, edge);
                }
                finally {
                    Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                        DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
                }
                // инеевые кристаллы по внешнему краю ленты
                IceRibbon.DrawEdgeCrystals(glow, swooshAngles, n, center, swooshROut, Projectile.localAI[0], env, core, tail);
            }

            // яркая точка у острия — голова ленты
            Vector2 tip = center + head.ToRotationVector2() * swooshROut[0];
            Main.EntitySpriteDraw(glow, tip - Main.screenPosition, null, IceArcFx.Additive(core, env), 0f,
                glow.Size() / 2f, TrailHeadDot * 1.6f / glow.Width, SpriteEffects.None, 0f);

            // пик-вспышка: пятно + звезда у острия и ледяная нова вокруг игрока
            float peak = PeakPulse(progress, maxAnim) * env;
            if (peak > 0.03f) {
                Vector2 origin = glow.Size() / 2f;
                Main.EntitySpriteDraw(glow, tip - Main.screenPosition, null, IceArcFx.Additive(edge, peak * PeakFlashAlpha), 0f,
                    origin, PeakFlashSize * (0.7f + 0.5f * peak) / glow.Width, SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(glow, tip - Main.screenPosition, null, IceArcFx.Additive(core, peak * PeakFlashAlpha), 0f,
                    origin, PeakFlashSize * 0.4f / glow.Width, SpriteEffects.None, 0f);
                IceArcFx.DrawSparkle(glow, tip, PeakStarSize * peak, head + MathHelper.PiOver4, 2,
                    IceArcFx.Additive(Color.Lerp(tail, core, 0.5f), peak * 0.9f), IceArcFx.Additive(core, peak));
            }
        }

        // Хитбокс на момент time — сектор вокруг игрока: от центра до внешнего края линии
        // (но не короче клинка), по углу от хвоста до головы дуги
        private void GetHitSector(Player player, float time, out Vector2 center, out float outer, out float startAngle, out float sweep) {
            GetArcState(player, time, out float rotation, out float scale);
            // Центр — живая позиция игрока, а не Projectile.Center: тот ставится в AI
            // и на бегу может отставать от игрока на тик
            center = player.RotatedRelativePoint(player.MountedCenter);
            // В начале взмаха линия ещё не дотянулась до острия — бьёт весь клинок
            outer = Math.Max(LineTexOuter * scale * BodyDrawScale * LineDrawScale, BladeReach);
            // ai[0] < 0 — текстура отзеркалена (FlipVertically): хвост и голова меняются местами
            startAngle = Projectile.ai[0] >= 0f ? rotation - ArcAngleBack : rotation - ArcAngleFront;
            sweep = ArcAngleBack + ArcAngleFront;
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) {
            // Хитбокс — точная форма нарисованной дуги (сектор), а не конус по 5 точкам цели
            GetHitSector(Main.player[Projectile.owner], Projectile.localAI[0], out Vector2 center,
                out float outer, out float startAngle, out float sweep);
            return NearestInWedge(targetHitbox, center, startAngle, sweep, out float distance) && distance <= outer;
        }

        // Насколько цель у края дуги: 0 — в зоне клинка, 1 — у края полностью раскрытой
        // дуги. Берётся ближайшая к игроку точка цели внутри дуги — ею цель и задета.
        private float EdgeFactor(Rectangle targetHitbox) {
            GetHitSector(Main.player[Projectile.owner], Projectile.localAI[0], out Vector2 center,
                out _, out float startAngle, out float sweep);
            // Сюда попадаем только после Colliding; если вдруг цель вне дуги — считаем краем
            if (!NearestInWedge(targetHitbox, center, startAngle, sweep, out float distance))
                return 1f;
            float maxReach = LineTexOuter * BodyDrawScale * LineDrawScale * (ArcScaleStart + ArcScaleGrowth) * Projectile.ai[2];
            if (maxReach <= BladeReach)
                return 0f;
            return MathHelper.Clamp((distance - BladeReach) / (maxReach - BladeReach), 0f, 1f);
        }

        private static readonly Vector2[] clipBufferA = new Vector2[16];
        private static readonly Vector2[] clipBufferB = new Vector2[16];

        // Ближайшая к центру точка прямоугольника внутри клина {угол от startAngle до
        // startAngle + sweep}, sweep <= π. false — прямоугольник в клин не попадает.
        // Точно, без выборки точек: режем прямоугольник клином — остаётся выпуклый
        // многоугольник, ищем его ближайшую к центру точку.
        private static bool NearestInWedge(Rectangle rect, Vector2 center, float startAngle, float sweep, out float distance) {
            distance = 0f;
            clipBufferA[0] = new Vector2(rect.Left, rect.Top) - center;
            clipBufferA[1] = new Vector2(rect.Right, rect.Top) - center;
            clipBufferA[2] = new Vector2(rect.Right, rect.Bottom) - center;
            clipBufferA[3] = new Vector2(rect.Left, rect.Bottom) - center;

            // Клин — две полуплоскости через центр
            Vector2 startDir = startAngle.ToRotationVector2();
            Vector2 endDir = (startAngle + sweep).ToRotationVector2();
            int count = ClipByHalfPlane(clipBufferA, 4, clipBufferB, new Vector2(-startDir.Y, startDir.X));
            count = ClipByHalfPlane(clipBufferB, count, clipBufferA, new Vector2(endDir.Y, -endDir.X));
            if (count == 0)
                return false;

            // Центр — вершина клина, внутрь многоугольника он не попадает, поэтому
            // ближняя точка всегда лежит на одном из рёбер
            float nearestSq = float.MaxValue;
            for (int i = 0; i < count; i++) {
                Vector2 a = clipBufferA[i];
                Vector2 edge = clipBufferA[(i + 1) % count] - a;
                float lengthSq = edge.LengthSquared();
                float t = lengthSq > 0f ? MathHelper.Clamp(-Vector2.Dot(a, edge) / lengthSq, 0f, 1f) : 0f;
                nearestSq = Math.Min(nearestSq, (a + edge * t).LengthSquared());
            }
            distance = (float)Math.Sqrt(nearestSq);
            return true;
        }

        // Оставляет часть выпуклого многоугольника, где Dot(точка, normal) >= 0
        private static int ClipByHalfPlane(Vector2[] input, int count, Vector2[] output, Vector2 normal) {
            int outCount = 0;
            for (int i = 0; i < count; i++) {
                Vector2 a = input[i];
                Vector2 b = input[(i + 1) % count];
                float da = Vector2.Dot(a, normal);
                float db = Vector2.Dot(b, normal);
                if (da >= 0f)
                    output[outCount++] = a;
                if ((da >= 0f) != (db >= 0f))
                    output[outCount++] = a + (b - a) * (da / (da - db));
            }
            return outCount;
        }

        // Отладка (DebugHitbox): пыль по границе зоны клинка и по внешнему краю хитбокса
        private void ShowHitbox(Player player) {
            GetHitSector(player, Projectile.localAI[0], out Vector2 center, out float outer, out float startAngle, out float sweep);
            for (int i = 0; i <= 12; i++) {
                Vector2 dir = (startAngle + sweep * i / 12f).ToRotationVector2();
                Dust.NewDustPerfect(center + dir * BladeReach, DustID.BlueFairy, Vector2.Zero).noGravity = true;
                Dust.NewDustPerfect(center + dir * outer, DustID.Pixie, Vector2.Zero).noGravity = true;
            }
        }

        public override void CutTiles() {
            Vector2 starting = (Projectile.rotation - MathHelper.PiOver4).ToRotationVector2() * 40f * Projectile.scale;
            Vector2 ending = (Projectile.rotation + MathHelper.PiOver4).ToRotationVector2() * 40f * Projectile.scale;
            float width = 40f * Projectile.scale;
            Utils.PlotTileLine(Projectile.Center + starting, Projectile.Center + ending, width, DelegateMethods.CutTiles);
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers) {
            // Урон и отбрасывание от радиуса: клинком — как мечом, к краю дуги — до крошечных
            float edge = EdgeFactor(target.Hitbox);
            // Урон: крутой спад сразу за клинком, дальше крошечный; защита вычтется уже после этого
            float falloff = MathHelper.Clamp(edge / DamageFalloffSpan, 0f, 1f);
            falloff = falloff * falloff * (3f - 2f * falloff);
            float damageMult = MathHelper.Lerp(1f, EdgeDamageMult, falloff);
            if (Projectile.damage > 0)
                damageMult = Math.Max(damageMult, Math.Min(1f, MinEdgeDamage / Projectile.damage));
            modifiers.SourceDamage *= damageMult;
            modifiers.Knockback *= MathHelper.Lerp(1f, EdgeKnockbackMult, edge);
            // Отталкиваем от игрока в любую сторону, в том числе за спиной. Раньше это
            // стояло в OnHitNPC и не работало: там hit — копия, удар уже нанесён.
            modifiers.HitDirectionOverride = (Main.player[Projectile.owner].Center.X < target.Center.X) ? 1 : (-1);
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) {
            if (SwordItem == ItemID.IceBlade) {
                Main.player[Projectile.owner].GetModPlayer<SwordPlayer>().AddPolish();
            }
            // Фростбранд: любой удар поджигает холодным огнём
            if (SwordItem == ItemID.Frostbrand) {
                FrostFireSystem.Ignite(target, Projectile.owner);
            }
        }

        public override void OnHitPlayer(Player target, Player.HurtInfo info) {
            info.HitDirection = (Main.player[Projectile.owner].Center.X < target.Center.X) ? 1 : (-1);
        }

        public override bool PreDraw(ref Color lightColor) {
            Vector2 position = Projectile.Center - Main.screenPosition;
            Texture2D texture = TextureAssets.Projectile[Type].Value;
            if (texture == null || texture.IsDisposed)
                texture = ModContent.Request<Texture2D>("Terraria/Images/Projectile_" + ProjectileID.Excalibur).Value;
            if (texture == null || texture.IsDisposed)
                return false;
            Rectangle sourceRectangle = texture.Frame(1, 4);
            Vector2 origin = sourceRectangle.Size() / 2f;
            float scale = Projectile.scale * BodyDrawScale;
            SpriteEffects spriteEffects = ((!(Projectile.ai[0] >= 0f)) ? SpriteEffects.FlipVertically : SpriteEffects.None);
            float maxAnim = Math.Max(1f, Projectile.ai[1]);
            float percentageOfLife = Projectile.localAI[0] / maxAnim;
            float lerpTime = Utils.Remap(percentageOfLife, 0f, 0.6f, 0f, 1f) * Utils.Remap(percentageOfLife, 0.6f, 1f, 1f, 0f);

            // Линия дуги (по ней считается хитбокс — см. LineTexOuter): ледяной обод +
            // светлое ядро с дыханием (IceArcFx) и мерцающие кристаллики вдоль неё.
            // Поверх — IceRibbon-лента по истории углов, кристаллы на кромке и пик-вспышка
            Player owner = Main.player[Projectile.owner];
            float lineScale = scale * LineDrawScale;
            IceArcFx.DrawLine(texture, texture.Frame(1, 4, 0, 3), position, Projectile.rotation, origin,
                lineScale, spriteEffects, 0.3f * lerpTime, null, null);
            Vector2 wedgeTip = owner.RotatedRelativePoint(owner.MountedCenter);
            IceArcFx.DrawSparkles(wedgeTip, Projectile.rotation,
                Projectile.ai[0] >= 0f ? 1 : -1, LineTexOuter * lineScale, Projectile.localAI[0], lerpTime, null, null);
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            if (glow != null && !glow.IsDisposed)
                DrawSwoosh(owner, glow, percentageOfLife, maxAnim);

            return false;
        }

        public override void Unload() {
            if (Main.dedServ)
                return;
            IceRibbon.Unload();
        }
    }
}
