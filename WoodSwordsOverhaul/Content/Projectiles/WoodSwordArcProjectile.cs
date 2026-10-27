using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace WoodSwordsOverhaul.Content.Projectiles
{
	// Снаряд дуги для деревянных мечей (текстура — ванильная дуга меча Excalibur).
	// Дуга — единственный удар взмаха (у самого меча хитбокс убран, см. WoodSwordsGlobalItem):
	// хитбокс — сама нарисованная палка (BladeHit), дальше острия взмах не бьёт.
	public class WoodSwordArcProjectile : ModProjectile
	{
		public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.Excalibur;

		// === Геометрия дуги Excalibur (текстура 170×170, центр — центр кадра) ===
		// Тонкая линия (кадр 3) в текстуре лежит на радиусе 78..82 px и по углу от
		// -74° (хвост) до +56° (голова, сторона взмаха) от оси снаряда.
		// Рисуется (PreDraw) только эта линия — ультрапрозрачной полоской по пути
		// острия: центром на кисти, головой ровно на палке, внешним краем на острие
		// (как дуги мечей FrostMelee). Хитбокс — сама палка (BladeHit): полоска
		// идёт ровно по внешней границе зоны урона, дальше острия — ничего.
		private const float BodyDrawScale = 0.75f;
		private const float LineDrawScale = 0.8f;
		private const float LineTexOuter = 82f;
		private static readonly float ArcAngleBack = MathHelper.ToRadians(74f);
		private static readonly float ArcAngleFront = MathHelper.ToRadians(56f);
		// Толщина палки для зоны полного урона: клин между углами палки соседних
		// тиков расширяем на этот угол в обе стороны (≈3 px на острие)
		private static readonly float BladeWedgePad = MathHelper.ToRadians(3f);
		// Дуга растёт за взмах: масштаб от ArcScaleStart до ArcScaleStart + ArcScaleGrowth (× ai[2])
		private const float ArcScaleStart = 1f;
		private const float ArcScaleGrowth = 0.6f;

		// Насколько кисть в ванильном взмахе отстоит от центра игрока (до ~15px):
		// остриё дотягивается на длину клинка плюс это смещение
		public const float HandReach = 15f;

		// Докуда от центра игрока достаёт сам клинок: длина клинка (рукоять → остриё
		// по реальным пикселям) × размер оружия + HandReach. Ближе — полный урон, и хитбокс
		// никогда не короче клинка. Задаёт WoodSwordsGlobalItem.Shoot у владельца:
		// попадания считает только он.
		public float BladeReach;

		// true — показать пылью зону клинка (полный урон) и внешний край хитбокса
		public static bool DebugHitbox = false;

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
			// Colliding вызывается только для целей в этом радиусе от игрока. Было 100 —
			// короче самой дуги кактуса (до ~105px); 300 — как у ванильных дуг (Excalibur)
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
			// Клинок взмаха рисуется от этой дуги (StickSwingLayer): одинаковый
			// угол у дуги и палки гарантирован источником, а не синхронизацией
			player.GetModPlayer<StickPlayer>().swingArc = Projectile;
			float maxAnim = Math.Max(1f, Projectile.ai[1]);
			GetArcState(player, Projectile.localAI[0], out float rotation, out float scale);
			Projectile.rotation = rotation;
			Projectile.scale = scale;
			Projectile.Center = player.RotatedRelativePoint(player.MountedCenter) - Projectile.velocity;

			if (DebugHitbox && Projectile.owner == Main.myPlayer)
				ShowHitbox(player);

			// Конец взмаха — строго вместе с анимацией руки: при itemAnimation == 0
			// игра уже вернула руку в обычную позу, и доживающий кадр дуги давал
			// прыжок рукояти (урок FrostMelee, покадровый разбор владельца)
			if (Projectile.localAI[0] >= maxAnim
				|| (Projectile.owner == Main.myPlayer && player.itemAnimation <= 0)) {
				Projectile.Kill();
			}

			// Визуальные эффекты от фляг
			float enchantmentDistance = 40f * Projectile.ai[2];
			Vector2 enchantmentPosition = Projectile.Center + Projectile.rotation.ToRotationVector2() * enchantmentDistance;
			Rectangle rectangle = Utils.CenteredRectangle(enchantmentPosition, new Vector2(30f * Projectile.ai[2], 30f * Projectile.ai[2]));
			Projectile.EmitEnchantmentVisualsAt(rectangle.TopLeft(), rectangle.Width, rectangle.Height);
		}

		// === Скорость взмаха: нелинейная, как у FrostMelee (серия HardcoreMelee) ===
		// Быстрый старт, замедляющийся конец (ease-out: 1-(1-t)^степень).
		// Функцию обязаны звать ВСЕ места с формулой угла взмаха: GetArcState
		// здесь и itemRotation в StickPlayer.PostUpdate — иначе клинок, дуга
		// и хитбокс разойдутся по разным кривым.
		public const float SwingEaseOutPower = 1.8f;  // резкость старта взмаха (1 — линейно)

		public static float SwingProgressCurve(float progress) {
			progress = MathHelper.Clamp(progress, 0f, 1f);
			return 1f - (float)Math.Pow(1f - progress, SwingEaseOutPower);
		}

		// Поворот и масштаб дуги на момент time (тиков от спавна) —
		// одна формула для отрисовки (AI) и для хитбокса (Colliding)
		private void GetArcState(Player player, float time, out float rotation, out float scale) {
			float percentageOfLife = time / Math.Max(1f, Projectile.ai[1]);
			rotation = Projectile.velocity.ToRotation() + Projectile.ai[0] * MathHelper.Pi * SwingProgressCurve(percentageOfLife) + player.fullRotation + MathHelper.Pi;

			scale = (ArcScaleStart + percentageOfLife * ArcScaleGrowth) * Projectile.ai[2];
		}

		// Углы контрольного сектора на момент time (+ центр/радиус серверного
		// запасного пути): позади палки и только в уже заметённой части взмаха —
		// голова на палке, хвост не дальше
		// ArcAngleBack+ArcAngleFront назад и не раньше начала взмаха. Раньше
		// сектор стоял головой на +56° ВПЕРЕДИ палки и бил туда, где палка
		// ещё не была; бьёт теперь только сама палка (BladeHit)
		private void GetHitSector(Player player, float time, out Vector2 center, out float outer, out float startAngle, out float sweep) {
			GetArcState(player, time, out float rotation, out float scale);
			// Центр — живая позиция игрока, а не Projectile.Center: тот ставится в AI
			// и на бегу может отставать от игрока на тик
			center = player.RotatedRelativePoint(player.MountedCenter);
			outer = Math.Max(LineTexOuter * scale * BodyDrawScale * LineDrawScale, BladeReach);
			float dir = Projectile.ai[0] >= 0f ? 1f : -1f;
			GetArcState(player, 0f, out float startRot, out _);
			float back = rotation - dir * (ArcAngleBack + ArcAngleFront);
			back = dir > 0f ? Math.Max(back, startRot) : Math.Min(back, startRot);
			startAngle = dir > 0f ? back : rotation;
			sweep = Math.Abs(rotation - back);
		}

		// Клин зоны палки: от кисти, между углом палки прошлого и текущего тика
		// (заметённая за тик часть пути) плюс толщина палки по краям. Углы — та же
		// формула, что рисует палку (GetArcState), поэтому зона и картинка в один тик
		private void GetBladeWedge(Player player, out Vector2 hand, out float startAngle, out float sweep) {
			hand = player.itemLocation;
			float time = Projectile.localAI[0];
			GetArcState(player, time, out float now, out _);
			GetArcState(player, Math.Max(0f, time - 1f), out float prev, out _);
			startAngle = Math.Min(now, prev) - BladeWedgePad;
			sweep = Math.Abs(now - prev) + BladeWedgePad * 2f;
		}

		// Настоящая длина палки (приклад → остриё) в мире сейчас — та же геометрия,
		// что у полоски дуги в PreDraw. 0 — геометрии нет (выделенный сервер):
		// BladeHit уходит в запасной сектор, как у копий серии
		private float BladeLengthNow(Player player) {
			if (Main.dedServ || player.HeldItem.IsAir)
				return 0f;
			return StickSprite.Get(player.HeldItem.type).Length * player.GetAdjustedItemScale(player.HeldItem);
		}

		// Попадание взмаха = сама нарисованная палка: заметённый за тик клин от
		// кисти до острия (GetBladeWedge). Дальше острия взмах НЕ бьёт вообще:
		// прежняя контрольная оболочка за остриём (как линия у копий серии) у
		// палок не нарисована ничем, кроме полоски на самом острие, — владелец
		// ловил попадания по воздуху за кончиком палки («невидимый хитбокс») и
		// велел убрать. Урон поэтому всегда полный, спада за палкой больше нет.
		// Нет геометрии (сервер) — запасной сектор от центра не дальше палки.
		private bool BladeHit(Rectangle target) {
			Player player = Main.player[Projectile.owner];
			float bladeLen = BladeLengthNow(player);
			if (bladeLen <= 1f) {
				GetHitSector(player, Projectile.localAI[0], out Vector2 center, out _,
					out float startAngle, out float sweep);
				return NearestInWedge(target, center, startAngle, sweep, out float distance, out _)
					&& distance <= BladeReach;
			}

			GetBladeWedge(player, out Vector2 hand, out float bladeStart, out float bladeSweep);
			return NearestInWedge(target, hand, bladeStart, bladeSweep, out float bladeDist, out _)
				&& bladeDist <= bladeLen;
		}

		public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) {
			return BladeHit(targetHitbox);
		}

		private static readonly Vector2[] clipBufferA = new Vector2[16];
		private static readonly Vector2[] clipBufferB = new Vector2[16];

		// Ближайшая и самая дальняя точки прямоугольника внутри клина {угол от
		// startAngle до startAngle + sweep}, sweep <= π. false — прямоугольник в
		// клин не попадает. Точно, без выборки точек: режем прямоугольник клином —
		// остаётся выпуклый многоугольник, ищем его крайние по радиусу точки.
		private static bool NearestInWedge(Rectangle rect, Vector2 center, float startAngle, float sweep,
			out float distance, out float farthest) {
			distance = 0f;
			farthest = 0f;
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
			float nearestSq = float.MaxValue, farthestSq = 0f;
			for (int i = 0; i < count; i++) {
				Vector2 a = clipBufferA[i];
				Vector2 edge = clipBufferA[(i + 1) % count] - a;
				float lengthSq = edge.LengthSquared();
				float t = lengthSq > 0f ? MathHelper.Clamp(-Vector2.Dot(a, edge) / lengthSq, 0f, 1f) : 0f;
				nearestSq = Math.Min(nearestSq, (a + edge * t).LengthSquared());
				farthestSq = Math.Max(farthestSq, a.LengthSquared());
			}
			distance = (float)Math.Sqrt(nearestSq);
			farthest = (float)Math.Sqrt(farthestSq);
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

		// Отладка (DebugHitbox): клин зоны палки этого тика от кисти — изумрудная
		// пыль на острие, голубая на середине палки. Это и есть весь хитбокс
		private void ShowHitbox(Player player) {
			float bladeLen = BladeLengthNow(player);
			if (bladeLen > 1f) {
				GetBladeWedge(player, out Vector2 hand, out float bladeStart, out float bladeSweep);
				for (int i = 0; i <= 4; i++) {
					Vector2 dir = (bladeStart + bladeSweep * i / 4f).ToRotationVector2();
					Dust.NewDustPerfect(hand + dir * bladeLen, DustID.GemEmerald, Vector2.Zero).noGravity = true;
					Dust.NewDustPerfect(hand + dir * bladeLen * 0.5f, DustID.BlueFairy, Vector2.Zero).noGravity = true;
				}
			}
			else {
				GetHitSector(player, Projectile.localAI[0], out Vector2 center, out _, out float startAngle, out float sweep);
				for (int i = 0; i <= 12; i++) {
					Vector2 dir = (startAngle + sweep * i / 12f).ToRotationVector2();
					Dust.NewDustPerfect(center + dir * BladeReach, DustID.BlueFairy, Vector2.Zero).noGravity = true;
				}
			}
		}

		public override void CutTiles() {
			Vector2 starting = (Projectile.rotation - MathHelper.PiOver4).ToRotationVector2() * 40f * Projectile.scale;
			Vector2 ending = (Projectile.rotation + MathHelper.PiOver4).ToRotationVector2() * 40f * Projectile.scale;
			float width = 40f * Projectile.scale;
			Utils.PlotTileLine(Projectile.Center + starting, Projectile.Center + ending, width, DelegateMethods.CutTiles);
		}

		public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers) {
			// Хитбокс = сама палка (BladeHit): урон и отбрасывание всегда полные,
			// спада за остриём нет — дальше острия взмах не бьёт вообще.
			// Отталкиваем от игрока в любую сторону, в том числе за спиной. Раньше это
			// стояло в OnHitNPC и не работало: там hit — копия, удар уже нанесён.
			modifiers.HitDirectionOverride = (Main.player[Projectile.owner].Center.X < target.Center.X) ? 1 : (-1);
		}

		public override void OnHitPlayer(Player target, Player.HurtInfo info) {
			info.HitDirection = (Main.player[Projectile.owner].Center.X < target.Center.X) ? 1 : (-1);
		}

		public override bool PreDraw(ref Color lightColor) {
			Player player = Main.player[Projectile.owner];
			Item held = player.HeldItem;
			if (held.IsAir)
				return false;
			Texture2D texture = TextureAssets.Projectile[Type].Value;
			if (texture == null || texture.IsDisposed)
				texture = ModContent.Request<Texture2D>("Terraria/Images/Projectile_" + ProjectileID.Excalibur).Value;
			if (texture == null || texture.IsDisposed)
				return false;
			Rectangle sourceRectangle = texture.Frame(1, 4);
			Vector2 origin = sourceRectangle.Size() / 2f;
			SpriteEffects spriteEffects = ((!(Projectile.ai[0] >= 0f)) ? SpriteEffects.FlipVertically : SpriteEffects.None);
			float maxAnim = Math.Max(1f, Projectile.ai[1]);
			float percentageOfLife = Projectile.localAI[0] / maxAnim;
			float lerpTime = Utils.Remap(percentageOfLife, 0f, 0.6f, 0f, 1f) * Utils.Remap(percentageOfLife, 0.6f, 1f, 1f, 0f);

			// Полоска по пути острия, как у мечей: центр — кисть (там приклад
			// палки), голова линии ровно на палке (вся полоска позади неё),
			// внешний край — на острие по настоящей длине спрайта. Раньше линия
			// рисовалась вокруг центра игрока растущим формульным радиусом с
			// головой на +56° впереди палки — после перевода взмаха на нашу
			// кривую она висела отдельно от палки (скрин владельца).
			// Искр у кисти (DrawPrettyStarSparkle) по-прежнему нет.
			float bladeLen = StickSprite.Get(held.type).Length * player.GetAdjustedItemScale(held);
			float dir = Projectile.ai[0] >= 0f ? 1f : -1f;
			float lineRot = Projectile.rotation - dir * ArcAngleFront;
			Vector2 position = player.itemLocation - Main.screenPosition;
			Main.EntitySpriteDraw(texture, position, texture.Frame(1, 4, 0, 3), Color.White * 0.25f * lerpTime, lineRot, origin, bladeLen / LineTexOuter, spriteEffects, 0f);

			return false;
		}
	}
}
