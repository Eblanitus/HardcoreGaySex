using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using FrostMelee.Content.Projectiles;

namespace FrostMelee.Content.Weapons
{
    // Единственная фишка карамельного меча: брошенный леденец неподвижен
    // (лёжа, в земле или во враге) и пахнет сладко — враги вокруг агрятся
    // на него и идут к нему, а не к игроку. Приманка работает на заряде
    // сладости (CandyCanePlayer): тик приманки = тик заряда, и возвращается
    // заряд с той же скоростью, но только пока меч подобран — спамить
    // бросками нельзя. По мере траты леденец сереет: на земле и в инвентаре.
    // Боссов и червей не манит. Рулёжка и учёт заряда, как вся локальная
    // бухгалтерия мода, в гостевом мультиплеере выключены.
    public class CandyCaneLureNPC : GlobalNPC
    {
        public const int MaxSweetTicks = 600;     // полный заряд: 10 секунд приманки
        private const float LureRadius = 520f;    // откуда враги чуют запах
        private const float MinWalkSpeed = 1.6f;  // медленнее к леденцу не плетутся
        private const float ArriveDistance = 36f; // дошёл — топчется у леденца

        public override void PostAI(NPC npc)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;
            if (npc.boss || npc.friendly || npc.dontTakeDamage || npc.immortal || npc.realLife != -1)
                return;

            // Ближайший живой (несъеденный) леденец в радиусе запаха
            Projectile lure = null;
            float best = LureRadius;
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (!p.active || p.ModProjectile is not SickleThrowProjectile thrown || !thrown.CandyLureActive)
                    continue;
                float d = npc.Distance(p.Center);
                if (d < best)
                {
                    best = d;
                    lure = p;
                }
            }
            if (lure == null)
                return;

            Vector2 to = lure.Center - npc.Center;
            if (to.Length() < ArriveDistance)
            {
                // Добрался до сладкого — топчется на месте, «ест»
                npc.velocity.X *= 0.8f;
                return;
            }

            int dir = to.X >= 0f ? 1 : -1;
            npc.direction = dir;
            npc.spriteDirection = dir;
            if (npc.noGravity)
            {
                float speed = Math.Max(npc.velocity.Length(), MinWalkSpeed);
                npc.velocity = Vector2.Normalize(to) * speed;
            }
            else
            {
                npc.velocity.X = dir * Math.Max(Math.Abs(npc.velocity.X), MinWalkSpeed);
            }
        }
    }

    // Заряд сладости у игрока. Тратит его неподвижный брошенный леденец
    // (SickleThrowProjectile), по тику за тик приманки. Возвращается с той
    // же скоростью — тик за тик, — но только пока меч не брошен: съеденный
    // леденец сначала подбери и подожди, сколько потратил.
    public class CandyCanePlayer : ModPlayer
    {
        public int SweetTicks = CandyCaneLureNPC.MaxSweetTicks;

        // Доля оставшейся сладости: 1 — красный, 0 — полностью серый
        public float SweetFrac => SweetTicks / (float)CandyCaneLureNPC.MaxSweetTicks;

        public override void PostUpdate()
        {
            if (SweetTicks < CandyCaneLureNPC.MaxSweetTicks && !CandyOut())
                SweetTicks++;
        }

        // Жив ли сейчас брошенный этим игроком леденец
        private bool CandyOut()
        {
            int type = ModContent.ProjectileType<SickleThrowProjectile>();
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p.active && p.type == type && p.owner == Player.whoAmI &&
                    (int)p.ai[2] == ItemID.CandyCaneSword)
                    return true;
            }
            return false;
        }
    }

    // Индикатор заряда прямо на иконке: в инвентаре леденец сереет по мере
    // потраченной сладости и краснеет обратно по мере восстановления —
    // та же серая текстура, что у брошенного меча на земле.
    public class CandyCaneGlobalItem : GlobalItem
    {
        private static Asset<Texture2D> grayAsset;

        public override void PostDrawInInventory(Item item, SpriteBatch spriteBatch, Vector2 position,
            Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale)
        {
            if (item.type != ItemID.CandyCaneSword)
                return;
            float gray = 1f - Main.LocalPlayer.GetModPlayer<CandyCanePlayer>().SweetFrac;
            if (gray <= 0.01f)
                return;
            grayAsset ??= ModContent.Request<Texture2D>(
                "FrostMelee/Items/Candy Cane Sword gray", AssetRequestMode.ImmediateLoad);
            Texture2D tex = grayAsset.Value;
            if (tex == null || tex.IsDisposed)
                return;
            // Серая текстура в ванильном размере 44x70, иконка — нынешние
            // 30x47: масштаб по ширине кадра, центр на центр
            float gScale = scale * frame.Width / (float)tex.Width;
            spriteBatch.Draw(tex, position, null, drawColor * gray, 0f,
                tex.Size() / 2f, gScale, SpriteEffects.None, 0f);
        }
    }
}
