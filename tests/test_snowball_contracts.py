"""Source-contract regressions only: these tests do not run Terraria or C#."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1] / 'FrostMelee'


def source(path):
    return (ROOT / path).read_text(encoding='utf-8')


class SnowballSourceContracts(unittest.TestCase):
    def setUp(self):
        self.head = source('Content/Projectiles/Prototypes/SnowballFlailProjectile.cs')
        self.resource = source('Content/Weapons/SnowballFlailPlayer.cs')
        self.items = source('Content/Items/Prototypes/PrototypeWeapons.cs')

    def test_resource_is_session_only_and_checks_slot_and_instance(self):
        for contract in ('FullGrowthTicks = 90', 'heldSlot != Player.selectedItem',
                         '!ReferenceEquals(heldInstance, held)', 'Player.dead', 'Player.ghost'):
            self.assertIn(contract, self.resource)
        self.assertNotIn('SaveData', self.resource)
        self.assertNotIn('OnKill', self.head)  # Returning the head must not consume mass.

    def test_server_owns_growth_and_ignores_client_mass(self):
        growth = self.head.split('if (Main.netMode != NetmodeID.MultiplayerClient)', 1)[1]
        self.assertIn('Mode == FlailMode.Spin', growth.split('else if', 1)[0])
        self.assertIn('Resource.Grow();', growth.split('else if', 1)[0])
        receive = self.head.split('public override void ReceiveExtraAI', 1)[1]
        self.assertLess(receive.index('base.ReceiveExtraAI(reader)'), receive.index('reader.ReadByte()'))
        self.assertLess(receive.index('if (Main.netMode != NetmodeID.MultiplayerClient)'),
                        receive.index('Resource.ReceiveGrowth(ticks)'))
        self.assertIn('base.SendExtraAI(writer);', self.head)

    def test_design_bounds_and_strict_shatter_threshold(self):
        for contract in ('1f - 0.25f * mass', '1f + 0.8f * mass', '28f + 16f * mass',
                         'mass <= 0.5f', '80f * 80f', 'BuffID.Slow, 120'):
            self.assertIn(contract, self.head)
        self.assertIn('public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) { }', self.head)

    def test_yank_is_single_server_action_with_weapon_hook(self):
        npc = source('Content/Weapons/FlailLassoNPC.cs')
        yank = npc.split('public void TryYank', 1)[1].split('private SharedFlailProjectile FindHead', 1)[0]
        self.assertIn('Main.netMode == NetmodeID.MultiplayerClient', yank)
        self.assertIn('|| Yanking', yank)
        self.assertLess(yank.index('Yanking = true'), yank.index('player.ApplyDamageToNPC'))
        self.assertIn('head.LassoDamage', yank)
        self.assertEqual(yank.count('head.OnLassoYank(npc)'), 1)
        self.assertIn('head.LassoPullSpeed', npc)

    def test_approved_recipes_replace_placeholders(self):
        snow = self.items.split('public class SnowballFlail', 1)[1].split('[LegacyName', 1)[0]
        for contract in ('ItemID.BallOHurt, ItemID.TheMeatball', 'ItemID.SnowBlock, 50',
                         'ItemID.Gel, 20', 'Condition.DownedEowOrBoc', 'TileID.Anvils'):
            self.assertIn(contract, snow)
        polar = self.items.split('public class PolarStar', 1)[1].split('public class SnowBoomerang', 1)[0]
        for contract in ('ItemID.FrostCore', 'ItemID.Sunfury', 'ItemID.FallenStar, 7', 'ItemID.MeteoriteBar, 20'):
            self.assertIn(contract, polar)
        self.assertNotIn('ItemID.Chain', snow + polar)
        self.assertNotIn('ItemID.SoulofLight', polar)

    def test_lasso_click_latches_without_held_buttons(self):
        lasso = source('Content/Projectiles/SharedFlailLasso.cs')
        controls = source('Content/Weapons/SharedFlailPlayer.cs')
        handler = lasso.split('private bool HandleLassoInput', 1)[1].split('private void UpdateLassoCast', 1)[0]
        self.assertIn('lassoActive = true;', handler)
        self.assertNotIn('input.RightHeld', handler)
        self.assertNotIn('input.LeftHeld', handler)
        self.assertIn('comboPressed && BoundNPC() != null', handler)
        self.assertIn('LeftPressed && (RightHeld || ActiveHead?.LassoInProgress == true)', controls)
        self.assertNotIn('if (!CanControl || !RightHeld)', controls)
        self.assertIn('!LeftHeld && !RightHeld && !pendingLassoPress', controls)
        base = source('Content/Projectiles/SharedFlailProjectile.cs')
        self.assertIn('lassoActive = false;', base)

    def test_quick_wrap_respects_winding_discontinuity_guard(self):
        lasso = source('Content/Projectiles/SharedFlailLasso.cs')
        self.assertIn('float angularStep = Math.Min(0.4f, 0.4f * meleeSpeed);', lasso)
        self.assertIn('Math.Abs(step) > 0.6f', lasso)
        self.assertIn('Math.Min(32f * meleeSpeed, delta.Length())', lasso)
        # Source contract only; actual NPC motion and server winding need a game test.
        self.assertNotIn('StateTimer += 0.12f', lasso)

    def test_lasso_links_use_one_continuous_path(self):
        lasso = source('Content/Projectiles/SharedFlailLasso.cs')
        draw = lasso.split('private void DrawLasso(', 1)[1].split('private static void DrawChainSegment', 1)[0]
        self.assertIn('path.Add(next)', draw)
        self.assertEqual(draw.count('DrawChainPath(chain, path)'), 1)
        self.assertNotIn('DrawChainSegment(chain, last, next)', draw)
        sampler = lasso.split('private static void DrawChainPath', 1)[1]
        self.assertLess(sampler.index('float untilNextLink ='), sampler.index('for (int segment'))
        self.assertLess(sampler.index('int links = 0'), sampler.index('for (int segment'))
        self.assertIn('untilNextLink -= length;', sampler)
        self.assertIn('untilNextLink += spacing;', sampler)

    def test_both_locales_describe_implemented_growth(self):
        for locale in ('en-US', 'ru-RU'):
            text = source(f'Localization/{locale}_Mods.FrostMelee.hjson')
            snow = text.split('SnowballFlail: {', 1)[1].split('PolarStar:', 1)[0]
            self.assertIn('+80%', snow)
            self.assertIn('−25%', snow)
            self.assertIn('SnowballMass:', text)


if __name__ == '__main__':
    unittest.main()
