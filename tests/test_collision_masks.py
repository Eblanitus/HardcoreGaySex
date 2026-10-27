"""Asset-data regressions; these do not replace tModLoader gameplay tests."""
import base64
import importlib.util
from pathlib import Path
import re
import unittest

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('generator', ROOT / 'tools/generate_collision_masks.py')
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)


class CollisionMaskTests(unittest.TestCase):
    def test_alpha_threshold_and_bit_order(self):
        image = Image.new('RGBA', (9, 1))
        image.putdata([(255, 0, 0, a) for a in (0, 128, 129, 255, 127, 0, 255, 128, 255)])
        self.assertEqual(generator.pack_alpha(image), bytes((0b01001100, 1)))

    def test_generated_file_is_current(self):
        self.assertEqual(generator.OUTPUT.read_text(), generator.render())

    def test_every_baked_pixel_matches_original_asset(self):
        text = generator.OUTPUT.read_text()
        for name, filename in generator.SPRITES:
            with self.subTest(sprite=name):
                match = re.search(rf'Mask {name} = new Mask\((\d+), (\d+),\s*"([A-Za-z0-9+/=]+)"\)', text)
                self.assertIsNotNone(match)
                width, height = map(int, match.group(1, 2))
                bits = base64.b64decode(match.group(3), validate=True)
                self.assertEqual(len(bits), (width * height + 7) // 8)
                with Image.open(ROOT / filename) as image:
                    self.assertEqual((width, height), image.size)
                    alpha = image.convert('RGBA').getchannel('A').tobytes()
                self.assertTrue(any(a > 128 for a in alpha))
                for index, a in enumerate(alpha):
                    self.assertEqual(bool(bits[index // 8] & (1 << (index % 8))), a > 128,
                                     f'{filename} pixel {index}')

    def test_collision_builder_does_not_depend_on_graphics(self):
        # Architectural guard: no GPU-derived fallback for the supported weapon family.
        source = (ROOT / 'FrostMelee/Content/SickleSprite.cs').read_text()
        for dependency in ('Texture2D', 'GetData(', 'Main.instance.LoadItem', 'SickleTextures.Held('):
            self.assertNotIn(dependency, source)
        for name in ('IceSickle', 'IceBlade', 'Frostbrand'):
            self.assertIn(f'ItemID.{name} => {name}', generator.OUTPUT.read_text())
        self.assertIn('BuildZone(WeaponCollisionMasks.Frozen', source)


if __name__ == '__main__':
    unittest.main()
