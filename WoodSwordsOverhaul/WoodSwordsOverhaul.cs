using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace WoodSwordsOverhaul
{
	// Please read https://github.com/tModLoader/tModLoader/wiki/Basic-tModLoader-Modding-Guide#mod-skeleton-contents for more information about the various files in a mod.
	public class WoodSwordsOverhaul : Mod
	{
		// Ванильные текстуры, которые мы подменили — возвращаем их при выгрузке,
		// иначе после отключения мода мечи ссылаются на выгруженные ассеты
		private static readonly Dictionary<int, Asset<Texture2D>> originalTextures = new();

		public override void Load() {
			Logger.Info("Wood Swords: Overhaul v2.0 loaded (приёмы палок, заточки, индикатор).");

			// Жёстко подменяем текстуры ванильных деревянных мечей на наши палки
			OverrideWoodSwordTextures();
		}

		public override void Unload() {
			foreach (var pair in originalTextures)
				TextureAssets.Item[pair.Key] = pair.Value;
			originalTextures.Clear();
			Content.StickSprite.ClearCache();
		}

		private void OverrideWoodSwordTextures() {
			// На выделенном сервере текстур нет
			if (Main.dedServ)
				return;

			TryOverride(ItemID.WoodenSword,      "WoodSwordsOverhaul/Content/Items/Sticks/WoodStick");
			TryOverride(ItemID.RichMahoganySword,"WoodSwordsOverhaul/Content/Items/Sticks/RedwoodStick");
			TryOverride(ItemID.EbonwoodSword,    "WoodSwordsOverhaul/Content/Items/Sticks/EbonwoodStick");
			TryOverride(ItemID.ShadewoodSword,   "WoodSwordsOverhaul/Content/Items/Sticks/ShadewoodStick");
			TryOverride(ItemID.PearlwoodSword,   "WoodSwordsOverhaul/Content/Items/Sticks/PearlStick");
			TryOverride(ItemID.BorealWoodSword,  "WoodSwordsOverhaul/Content/Items/Sticks/SnowStick");
			TryOverride(ItemID.PalmWoodSword,    "WoodSwordsOverhaul/Content/Items/Sticks/SandStick");
			TryOverride(ItemID.AshWoodSword,     "WoodSwordsOverhaul/Content/Items/Sticks/HellStick");
			TryOverride(ItemID.CactusSword,      "WoodSwordsOverhaul/Content/Items/Sticks/CactusStick");
		}

		private void TryOverride(int itemId, string texturePath) {
			try {
				Asset<Texture2D> replacement = ModContent.Request<Texture2D>(texturePath);
				if (!originalTextures.ContainsKey(itemId))
					originalTextures[itemId] = TextureAssets.Item[itemId];
				TextureAssets.Item[itemId] = replacement;
			}
			catch (System.Exception ex) {
				// Если какой‑то спрайт не найден — пишем в лог и не падаем
				Logger.Warn($"Не удалось подменить текстуру {texturePath}: {ex.Message}");
			}
		}
	}
}
