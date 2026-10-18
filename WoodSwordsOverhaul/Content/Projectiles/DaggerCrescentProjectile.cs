using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace WoodSwordsOverhaul.Content.Projectiles
{
	// Взмах заточки (замена дуги и ванильной анимации) в духе Terragrim: клинок в руке проворачивается
	// на ~140° вокруг игрока, а следом идёт полумесяц, внешний край которого проходит по кончику клинка.
	// Прогресс нелинейный (резкий старт, замедление к концу), общий для клинка и полумесяца.
	// Чередование: чётный взмах сверху вниз, нечётный снизу вверх (зеркально при player.direction = -1).
	// Ванильный предмет во взмахе скрыт (noUseGraphic, см. WoodSwordsGlobalItem), клинок и руку рисует
	// и ставит сам снаряд — так же, как DaggerStabProjectile в тычке, поэтому он виден всем клиентам.
	// Меч сам не бьёт (noHitbox), удар один на цель за взмах.
	// Полумесяц рисуется процедурно теми же цветами, прозрачностью и слоями, что и дуга палок
	// (WoodSwordArcProjectile.PreDraw). Хитбокс считается по тем же сегментам, что и отрисовка,
	// по текущему углу — совпадает с видимой формой.
	// velocity — единичный вектор на курсор (на момент взмаха); Center ставится в AI с вычетом velocity,
	// чтобы движок не уносил снаряд (как в WoodSwordArcProjectile).
	// ai[0] — тип предмета в руке, ai[1] — размер оружия (GetAdjustedItemScale),
	// ai[2] — длительность взмаха в тиках со знаком: + по часовой на экране, − против.
	// Всё синхронизируется при спавне.
	public class DaggerCrescentProjectile : ModProjectile
	{
		// Текстура не используется (PreDraw рисует сам), нужна только как заглушка
		public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.Excalibur;

		// === Подстройка ===
		// Не короче стольких тиков (на случай очень быстрого оружия)
		private const int MinLife = 4;
		// Полураскрытие самого полумесяца вокруг его середины
		private static readonly float HalfSpan = MathHelper.ToRadians(70f);
		// На сколько градусов клинок и полумесяц проворачиваются вокруг игрока за взмах
		// (середина идёт по дуге симметрично относительно линии на курсор)
		public static readonly float SweepAngle = MathHelper.ToRadians(140f);
		// Нелинейность взмаха: резкий старт, замедление к концу (прогресс = 1 − (1 − t)^степень).
		// 1 — линейно; чем больше, тем резче старт. Один прогресс на клинок и полумесяц.
		private const float EaseExponent = 2.2f;
		// Толщина тела в пике (px при длине клинка ReferenceBlade): растёт от Min к Peak и обратно.
		// Тело растёт ВНУТРЬ от внешнего края, который всегда на кончике клинка.
		private const float ReferenceBlade = 33f;
		private const float MinThickness = 3f;
		private const float PeakThickness = 10f;
		// Перекос: ведущая сторона толще, хвост тоньше (0 — симметричный)
		private const float LeadSkew = 0.35f;
		// Толщина тонкой линии по внешнему краю (px)
		private const float LineWidth = 1.5f;
		private const int Segments = 12;
		// Сколько рукояти торчит позади кисти (px), как у тычка
		private const float GripInset = 4f;

		// Отбрасывание полумесяца относительно отбрасывания меча (задаёт Shoot)
		public const float KnockbackMult = 1.5f;
		public const float MinKnockback = 4f;

		// true — показать пылью сегменты хитбокса
		public static bool DebugHitbox = false;

		// Чередование взмахов (на клиенте владельца): true — следующий взмах сверху вниз
		private static bool nextSwingDown = true;
		public static bool TakeSwingDown() {
			bool down = nextSwingDown;
			nextSwingDown = !nextSwingDown;
			return down;
		}

		private static readonly Vector2[] points = new Vector2[Segments + 1];
		private static readonly float[] thickness = new float[Segments + 1];

		public override void SetDefaults() {
			Projectile.width = 16;
			Projectile.height = 16;
			Projectile.friendly = true;
			Projectile.DamageType = DamageClass.Melee;
			Projectile.penetrate = -1;
			Projectile.usesLocalNPCImmunity = true;
			Projectile.localNPCHitCooldown = -1; // каждая цель — раз за взмах
			Projectile.tileCollide = false;
			Projectile.ignoreWater = true;
			Projectile.ownerHitCheck = true;     // сквозь стены не бьёт
			Projectile.ownerHitCheckDistance = 300f;
			Projectile.usesOwnerMeleeHitCD = false;
			Projectile.stopsDealingDamageAfterPenetrateHits = true;
			Projectile.aiStyle = -1;
			Projectile.noEnchantmentVisuals = true;
		}

		// Длительность = длительность взмаха предмета
		private int LifeTicks => Math.Max(MinLife, (int)Math.Round(Math.Abs(Projectile.ai[2])));

		private float AimAngle => Projectile.velocity.LengthSquared() > 0.0001f ? Projectile.velocity.ToRotation() : 0f;

		public override void AI() {
			Player player = Main.player[Projectile.owner];
			if (!player.active || player.dead) {
				Projectile.Kill();
				return;
			}
			Projectile.localAI[0]++;
			// Живёт только пока идёт взмах этим предметом: иначе (взмах кончился, предмет сменили)
			// исчезает сразу, а не висит в простое
			if (Projectile.localAI[0] > 1f && (player.itemAnimation <= 0 || player.HeldItem.type != (int)Projectile.ai[0])) {
				Projectile.Kill();
				return;
			}
			BuildShape(player, Projectile.localAI[0], out Vector2 pivot, out float bladeAngle);
			Projectile.Center = pivot - Projectile.velocity;

			// Рука тянется по клинку; снаряды обновляются после игроков, поэтому эта поза
			// перебивает ванильную позу взмаха и доживает до отрисовки
			player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full, bladeAngle - MathHelper.PiOver2);

			if (DebugHitbox && Projectile.owner == Main.myPlayer) {
				for (int i = 0; i <= Segments; i++)
					Dust.NewDustPerfect(points[i], DustID.BlueFairy, Vector2.Zero).noGravity = true;
			}

			if (Projectile.localAI[0] >= LifeTicks)
				Projectile.Kill();
		}

		// Радиус внешнего края на последнем BuildShape (нужен отрисовке спрайта Terragrim)
		private float shapeReach;

		private float ItemScale => Projectile.ai[1] > 0f ? Projectile.ai[1] : 1f;

		// Угол клинка в руке на момент time: нелинейно (резкий старт, замедление к концу)
		private float BladeAngleAt(float time) {
			float t = MathHelper.Clamp(time / LifeTicks, 0f, 1f);
			float progress = 1f - (float)Math.Pow(1f - t, EaseExponent);
			float sign = Projectile.ai[2] < 0f ? -1f : 1f; // + по часовой: сверху вниз при взгляде вправо
			return AimAngle + sign * (-SweepAngle * 0.5f + SweepAngle * progress);
		}

		// Заполняет points/thickness: центральная линия полумесяца и толщина тела в каждой точке.
		// Внешний край — окружность вокруг игрока радиусом до кончика клинка в руке.
		private void BuildShape(Player player, float time, out Vector2 pivot, out float bladeAngle) {
			float t = MathHelper.Clamp(time / LifeTicks, 0f, 1f);
			float sign = Projectile.ai[2] < 0f ? -1f : 1f;
			pivot = player.RotatedRelativePoint(player.MountedCenter);
			bladeAngle = BladeAngleAt(time);

			// Радиус внешнего края = реальное расстояние от игрока до кончика клинка:
			// кисть + (длина клинка по пикселям спрайта × размер оружия) вдоль клинка
			StickSprite.Geometry geo = StickSprite.Get((int)Projectile.ai[0]);
			float bladeLength = geo.Length * ItemScale;
			Vector2 hand = StickSprite.HandGrip(player, Player.CompositeArmStretchAmount.Full, bladeAngle - MathHelper.PiOver2);
			Vector2 tip = hand + bladeAngle.ToRotationVector2() * (bladeLength - GripInset);
			float reach = Math.Max(1f, Vector2.Distance(tip, pivot));
			shapeReach = reach;

			// Тонкий → толстый → тонкий
			float k = bladeLength / ReferenceBlade;
			float maxThick = k * MathHelper.Lerp(MinThickness, PeakThickness, (float)Math.Sin(t * MathHelper.Pi));
			for (int i = 0; i <= Segments; i++) {
				float u = i / (float)Segments * 2f - 1f; // -1..1 вдоль полумесяца
				Vector2 dir = (bladeAngle + u * HalfSpan).ToRotationVector2();
				// Тело утолщается к середине и сходит на нет в рогах; ведущая сторона (по ходу вращения) толще
				// cos в рогах (u = ±1) из-за округления может уйти в минус и дать NaN в Pow — режем в 0
				float profile = (float)Math.Pow(Math.Max(0.0, Math.Cos(u * Math.PI * 0.5)), 0.7) * (1f + LeadSkew * u * sign);
				thickness[i] = maxThick * profile;
				// Центральная линия отступает внутрь на полтолщины: внешний край — ровно на радиусе кончика
				points[i] = pivot + dir * (reach - thickness[i] * 0.5f);
			}
		}

		public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) {
			BuildShape(Main.player[Projectile.owner], Projectile.localAI[0], out _, out _);
			for (int i = 0; i < Segments; i++) {
				float halfWidth = 0.5f * Math.Max(2f, (thickness[i] + thickness[i + 1]) * 0.5f);
				if (!IsFinite(points[i]) || !IsFinite(points[i + 1]))
					continue;
				if (DistanceSegmentToRect(points[i], points[i + 1], targetHitbox) <= halfWidth)
					return true;
			}
			return false;
		}

		public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers) {
			// Отталкиваем от игрока в любую сторону, в том числе за спиной
			modifiers.HitDirectionOverride = (Main.player[Projectile.owner].Center.X < target.Center.X) ? 1 : (-1);
		}

		public override void OnHitPlayer(Player target, Player.HurtInfo info) {
			info.HitDirection = (Main.player[Projectile.owner].Center.X < target.Center.X) ? 1 : (-1);
		}

		public override bool PreDraw(ref Color lightColor) {
			Player player = Main.player[Projectile.owner];
			// В простое (взмах не идёт) не рисуем вообще
			if (Projectile.localAI[0] > 1f && (player.itemAnimation <= 0 || player.HeldItem.type != (int)Projectile.ai[0]))
				return false;
			BuildShape(player, Projectile.localAI[0], out Vector2 pivot, out float bladeAngle);

			// Прозрачность и цвета — как у дуги палок (WoodSwordArcProjectile.PreDraw)
			float t = MathHelper.Clamp(Projectile.localAI[0] / LifeTicks, 0f, 1f);
			float lerpTime = Utils.Remap(t, 0f, 0.6f, 0f, 1f) * Utils.Remap(t, 0.6f, 1f, 1f, 0f);
			float lightingColor = Lighting.GetColor(pivot.ToTileCoordinates()).ToVector3().Length() / (float)Math.Sqrt(3.0);
			lightingColor = Utils.Remap(lightingColor, 0.2f, 1f, 0f, 1f);
			Color middleMediumColor = Color.Brown * 0.25f;
			Color frontLightColor = Color.SandyBrown * 0.25f;

			// Основной вид — кадры ванильного снаряда Terragrim; если раскладку не удалось надёжно
			// определить — процедурный полумесяц (его хитбокс тот же)
			if (!DrawTerragrimCrescent(pivot, bladeAngle, t, lerpTime, lightingColor, middleMediumColor, frontLightColor))
				DrawProceduralCrescent(pivot, lerpTime, lightingColor, middleMediumColor, frontLightColor);

			DrawBlade(player, bladeAngle, lightColor);
			return false;
		}

		// Процедурный полумесяц: те же слои, что у дуги палок (запасной вид)
		private static void DrawProceduralCrescent(Vector2 pivot, float lerpTime, float lightingColor, Color middleMediumColor, Color frontLightColor) {
			Texture2D pixel = TextureAssets.MagicPixel.Value;
			Rectangle pixelSource = new Rectangle(0, 0, 1, 1);
			Vector2 pixelOrigin = new Vector2(0.5f, 0.5f);

			// Слои как у дуги: тело (почти прозрачное), передняя часть (чуть уже), тонкая линия
			for (int pass = 0; pass < 3; pass++) {
				float widthMult = pass == 0 ? 1f : (pass == 1 ? 0.975f : 0f);
				Color color = pass == 0 ? middleMediumColor * lightingColor * lerpTime * 0.15f
					: (pass == 1 ? frontLightColor * lightingColor * lerpTime * 0.25f
					: Color.White * 0.25f * lerpTime);
				for (int i = 0; i < Segments; i++) {
					Vector2 a = points[i];
					Vector2 b = points[i + 1];
					Vector2 delta = b - a;
					if (!IsFinite(a) || !IsFinite(b))
						continue;
					float width = pass == 2 ? LineWidth : (thickness[i] + thickness[i + 1]) * 0.5f * widthMult;
					if (width < 0.5f)
						continue;
					Vector2 center = (a + b) * 0.5f;
					if (pass == 2) {
						// Линия — по внешнему краю полумесяца, как тонкая линия дуги
						Vector2 outward = (center - pivot).SafeNormalize(Vector2.UnitX);
						center += outward * (thickness[i] + thickness[i + 1]) * 0.25f;
					}
					Main.EntitySpriteDraw(pixel, center - Main.screenPosition, pixelSource, color,
						delta.ToRotation(), pixelOrigin, new Vector2(delta.Length(), width), SpriteEffects.None, 0f);
				}
			}
		}

		// === Спрайт ванильного Terragrim ===
		// Снаряд Terragrim — вертикальная полоса кадров (в гифке 7 кадров на взмах, кадр 64×60):
		// полумесяц разворачивается вокруг центра кадра, тонкий → толстый → тонкий. Раскладку не
		// угадываем, а проверяем по пикселям: ID берём по имени, число кадров пробуем из
		// Main.projFrames и 7, у каждого кадра меряем направление полумесяца (центр масс
		// пикселей) и радиус внешнего края. Не сошлось — spriteOk = false и рисуется процедурный.
		private static bool spriteChecked, spriteOk, spriteClockwise;
		private static int spriteId, spriteFrames;
		private static float[] spriteAngle;
		private static float spriteRadius;

		public override void Unload() {
			spriteChecked = spriteOk = false;
			spriteAngle = null;
		}

		private static void MeasureSprite() {
			if (Main.dedServ || !ProjectileID.Search.ContainsName("Terragrim")) {
				spriteChecked = true;
				return;
			}
			int id = ProjectileID.Search.GetId("Terragrim");
			Main.instance.LoadProjectile(id);
			var asset = TextureAssets.Projectile[id];
			// Пока текстура грузится — повторим на следующем кадре
			if (asset == null || !asset.IsLoaded)
				return;
			Texture2D texture = asset.Value;
			if (texture == null || texture.IsDisposed)
				return;
			spriteChecked = true;
			int[] candidates = { Main.projFrames[id], 7 };
			foreach (int frames in candidates) {
				if (frames > 1 && TryMeasureFrames(texture, frames)) {
					spriteId = id;
					spriteFrames = frames;
					spriteOk = true;
					return;
				}
			}
		}

		private static bool TryMeasureFrames(Texture2D texture, int frames) {
			int width = texture.Width;
			if (texture.Height % frames != 0)
				return false;
			int cellHeight = texture.Height / frames;
			Color[] data = new Color[width * texture.Height];
			texture.GetData(data);
			float cx = width * 0.5f, cy = cellHeight * 0.5f;

			float[] angles = new float[frames];
			float radiusSum = 0f;
			var distances = new System.Collections.Generic.List<float>();
			for (int f = 0; f < frames; f++) {
				distances.Clear();
				float sx = 0f, sy = 0f;
				for (int y = 0; y < cellHeight; y++) {
					for (int x = 0; x < width; x++) {
						if (data[(f * cellHeight + y) * width + x].A < 40)
							continue;
						float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
						sx += dx;
						sy += dy;
						distances.Add((float)Math.Sqrt(dx * dx + dy * dy));
					}
				}
				if (distances.Count < 12)
					return false; // пустой кадр — раскладка не та
				distances.Sort();
				radiusSum += distances[Math.Min(distances.Count - 1, (int)(distances.Count * 0.98f))];
				angles[f] = (float)Math.Atan2(sy, sx);
			}
			float radius = radiusSum / frames;
			if (radius < 8f || radius > width * 0.6f)
				return false;

			// Куда в самом спрайте вращается полумесяц (углы растут — по часовой на экране)
			float total = 0f;
			for (int f = 0; f + 1 < frames; f++)
				total += MathHelper.WrapAngle(angles[f + 1] - angles[f]);
			spriteAngle = angles;
			spriteRadius = radius;
			spriteClockwise = total >= 0f;
			return true;
		}

		// Рисует кадр Terragrim: кадр по времени взмаха, поворот на текущий угол клинка, отражение по
		// стороне вращения, масштаб под радиус кольца. Цвета и прозрачность — слои дуги палок.
		// false — спрайт недоступен (рисуем процедурно).
		private bool DrawTerragrimCrescent(Vector2 pivot, float midAngle, float t, float lerpTime, float lightingColor, Color middleMediumColor, Color frontLightColor) {
			if (!spriteChecked)
				MeasureSprite();
			if (!spriteOk)
				return false;
			Texture2D texture = TextureAssets.Projectile[spriteId].Value;
			if (texture == null || texture.IsDisposed)
				return false;

			int frame = Math.Min(spriteFrames - 1, (int)(t * spriteFrames));
			Rectangle source = texture.Frame(1, spriteFrames, 0, frame);
			Vector2 origin = source.Size() * 0.5f;
			float scale = shapeReach / spriteRadius;
			// Сторона вращения у нас (ai[2]) и у спрайта совпадают — рисуем как есть, иначе зеркалим по вертикали
			bool clockwise = Projectile.ai[2] >= 0f;
			bool flip = clockwise != spriteClockwise;
			// Зеркало по вертикали меняет угол спрайта на минус, поэтому поворот считается по-разному
			float rotation = flip ? midAngle + spriteAngle[frame] : midAngle - spriteAngle[frame];
			SpriteEffects effects = flip ? SpriteEffects.FlipVertically : SpriteEffects.None;
			Vector2 position = pivot - Main.screenPosition;

			// Слои как у дуги: тело (почти прозрачное), передняя часть (чуть уже), линия
			Main.EntitySpriteDraw(texture, position, source, middleMediumColor * lightingColor * lerpTime * 0.15f, rotation, origin, scale, effects, 0f);
			Main.EntitySpriteDraw(texture, position, source, frontLightColor * lightingColor * lerpTime * 0.25f, rotation, origin, scale * 0.975f, effects, 0f);
			Main.EntitySpriteDraw(texture, position, source, Color.White * 0.25f * lerpTime, rotation, origin, scale, effects, 0f);
			return true;
		}

		private static bool IsFinite(Vector2 v) => !float.IsNaN(v.X) && !float.IsNaN(v.Y) && !float.IsInfinity(v.X) && !float.IsInfinity(v.Y);

		// Клинок в руке (ванильный предмет во взмахе скрыт): приклад у кисти, остриё по bladeAngle.
		// Та же схема, что у DaggerStabProjectile: якорь — реальный приклад спрайта, угол — по
		// реальным пикселям, при взгляде влево спрайт зеркалится.
		private void DrawBlade(Player player, float bladeAngle, Color lightColor) {
			int itemType = (int)Projectile.ai[0];
			if (itemType <= 0)
				return;
			Main.instance.LoadItem(itemType);
			Texture2D texture = TextureAssets.Item[itemType].Value;
			if (texture == null || texture.IsDisposed)
				return;

			StickSprite.Geometry geo = StickSprite.Get(itemType);
			bool flipped = player.direction == -1;
			Vector2 dir = bladeAngle.ToRotationVector2();
			Vector2 hand = StickSprite.HandGrip(player, Player.CompositeArmStretchAmount.Full, bladeAngle - MathHelper.PiOver2);
			Vector2 butt = hand - dir * GripInset;
			SpriteEffects effects = flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
			Main.EntitySpriteDraw(texture, butt - Main.screenPosition, null, lightColor,
				bladeAngle - geo.AngleFor(flipped), geo.ButtFor(flipped), ItemScale, effects, 0f);
		}

		// === Геометрия: расстояние от отрезка до прямоугольника цели ===

		private static float DistanceSegmentToRect(Vector2 a, Vector2 b, Rectangle rect) {
			if (SegmentIntersectsRect(a, b, rect))
				return 0f;
			float best = Math.Min(DistancePointToRect(a, rect), DistancePointToRect(b, rect));
			best = Math.Min(best, DistancePointToSegment(new Vector2(rect.Left, rect.Top), a, b));
			best = Math.Min(best, DistancePointToSegment(new Vector2(rect.Right, rect.Top), a, b));
			best = Math.Min(best, DistancePointToSegment(new Vector2(rect.Right, rect.Bottom), a, b));
			best = Math.Min(best, DistancePointToSegment(new Vector2(rect.Left, rect.Bottom), a, b));
			return best;
		}

		private static float DistancePointToRect(Vector2 p, Rectangle rect) {
			float dx = Math.Max(Math.Max(rect.Left - p.X, 0f), p.X - rect.Right);
			float dy = Math.Max(Math.Max(rect.Top - p.Y, 0f), p.Y - rect.Bottom);
			return (float)Math.Sqrt(dx * dx + dy * dy);
		}

		private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b) {
			Vector2 ab = b - a;
			float lengthSq = ab.LengthSquared();
			float t = lengthSq > 0f ? MathHelper.Clamp(Vector2.Dot(p - a, ab) / lengthSq, 0f, 1f) : 0f;
			return Vector2.Distance(p, a + ab * t);
		}

		// Отсечение отрезка прямоугольником (Лианга — Барски)
		private static bool SegmentIntersectsRect(Vector2 a, Vector2 b, Rectangle rect) {
			float t0 = 0f, t1 = 1f;
			Vector2 d = b - a;
			return ClipTest(-d.X, a.X - rect.Left, ref t0, ref t1)
				&& ClipTest(d.X, rect.Right - a.X, ref t0, ref t1)
				&& ClipTest(-d.Y, a.Y - rect.Top, ref t0, ref t1)
				&& ClipTest(d.Y, rect.Bottom - a.Y, ref t0, ref t1);
		}

		private static bool ClipTest(float p, float q, ref float t0, ref float t1) {
			if (p == 0f)
				return q >= 0f;
			float r = q / p;
			if (p < 0f) {
				if (r > t1)
					return false;
				if (r > t0)
					t0 = r;
			}
			else {
				if (r < t0)
					return false;
				if (r < t1)
					t1 = r;
			}
			return true;
		}
	}
}
