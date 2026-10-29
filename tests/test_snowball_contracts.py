"""Source contracts + independent numerical models. No C#/Terraria execution."""
from pathlib import Path
import math
import struct
import unittest

ROOT = Path(__file__).resolve().parents[1] / 'FrostMelee'


def source(path):
    return (ROOT / path).read_text(encoding='utf-8')


class FlailControllerContracts(unittest.TestCase):
    def setUp(self):
        self.base = source('Content/Projectiles/SharedFlailProjectile.cs')
        self.lasso = source('Content/Projectiles/SharedFlailLasso.cs')
        self.input = source('Content/Weapons/SharedFlailPlayer.cs')
        self.npc = source('Content/Weapons/FlailLassoNPC.cs')
        self.head = source('Content/Projectiles/Prototypes/SnowballFlailProjectile.cs')
        self.resource = source('Content/Weapons/SnowballFlailPlayer.cs')
        self.protocol = source('FrostMelee.cs')

    def test_server_is_the_only_spawner_and_state_author(self):
        spawn = self.input.index('Projectile.NewProjectile(')
        client_return = self.input.index('if (Main.netMode == NetmodeID.MultiplayerClient)\n                return;')
        self.assertLess(client_return, spawn)
        self.assertIn('private bool IsAuthority => Main.netMode != NetmodeID.MultiplayerClient;', self.base)
        self.assertIn('if (!IsAuthority || Mode == next) return;', self.base)
        self.assertNotIn('Projectile.netUpdate = true', self.input)

    def test_input_validation_and_wire_sizes(self):
        # Fixed-size wire layouts, including the message type byte.
        for layout, size in (('<BIBBi2f', 19), ('<BBf', 6), ('<BBB', 3), ('<B2f', 9), ('<BB', 2)):
            self.assertEqual(struct.calcsize(layout), size)
            self.assertIn(f'length {"!=" if size == 6 else "=="} {size}', self.protocol)
        for guard in ('!float.IsFinite(aim.X)', 'aim.LengthSquared() > 4f',
                      'unchecked((int)(serial - receivedSequence)) <= 0', 'slot >= Player.inventory.Length'):
            self.assertIn(guard, self.input)
        self.assertIn('Main.player[whoAmI]', self.protocol)  # No client-supplied owner ID.
        self.assertIn('inputAge > 60', self.input)

    def test_input_is_one_click_and_ui_does_not_cancel_active_lasso(self):
        handler = self.lasso.split('private bool HandleLassoInput', 1)[1].split('private void AcquireLassoTarget', 1)[0]
        self.assertIn('if (LassoInProgress) return true;', handler)
        self.assertNotIn('input.RightHeld', handler)
        self.assertNotIn('input.LeftHeld', handler)
        self.assertNotIn('input.CanControl', self.base)
        self.assertIn('!input.CanContinueAttack', self.base)
        self.assertIn('previousLeft = Main.mouseLeft;', self.input)
        self.assertIn('!LeftHeld && !RightHeld && !pendingLassoPress', self.input)

    def test_drop_removed_and_both_heads_pierce(self):
        self.assertNotIn('FlailMode.Drop', self.base + self.input + self.lasso)
        self.assertIn('Projectile.penetrate = -1', self.base)
        self.assertIn('public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) { }', self.base)
        self.assertNotIn('ResetLocalNPCHitImmunity', self.base)
        self.assertIn('Projectile.localNPCHitCooldown = Mode == FlailMode.Spin ? 20 : 10', self.base)

    def test_real_clock_separate_from_integrated_phase_and_ramp(self):
        self.assertIn('StateTimer += 1f', self.base)
        self.assertIn('SpinTicks++', self.base)
        self.assertIn('contactDamage = SpinTicks > 12', self.base)
        self.assertIn('MathHelper.Pi / 6f * MovementSpeedMultiplier * facing', self.base)
        self.assertIn('30f * MathF.Sqrt(2f)', self.base)
        self.assertIn('lower = upper * 0.5f', self.base)
        self.assertIn('RampTicks / 90f', self.base)
        self.assertIn('MathHelper.Lerp(0.6f, 1.4f', self.base)
        self.assertIn('MathHelper.Lerp(1.2f, 0.5f', self.base)
        self.assertIn('if (IsAuthority) facing = input.Aim.X < 0 ? -1 : 1', self.base)

    def test_swept_motion_and_contact_do_not_shrink_radial_orbit(self):
        self.assertNotIn('ClearOrbitPosition', self.base)
        self.assertIn('public override bool ShouldUpdatePosition() => false', self.base)
        self.assertIn('Math.Ceiling(delta.Length() / 4f)', self.base)
        self.assertIn('Collision.TileCollision', self.base)
        self.assertIn('public override bool? Colliding', self.base)
        self.assertIn('expanded.Inflate', self.base)
        self.assertIn('damagePath[i - 1], damagePath[i]', self.base)
        self.assertIn('bool lengthLimited', self.base)

    def test_platform_support_and_slot_changes_are_checked(self):
        self.assertIn('GroundedAt(player.gravDir > 0 ? player.Bottom : player.Top, player.gravDir)', self.base)
        self.assertIn('GroundedAt(target.Bottom, 1f)', self.lasso)
        self.assertIn('&& StillHeld(Main.player[Projectile.owner])', self.lasso)
        self.assertIn('ReferenceEquals(boundItem, player.HeldItem)', self.base)
        self.assertIn('MathF.Ceiling(MathHelper.TwoPi / angularSpeed) + 60f', self.lasso)

    def test_lasso_acquires_only_the_tile_resolved_path(self):
        self.assertIn('segment < damagePath.Count', self.lasso)
        self.assertIn('from = damagePath[segment - 1]', self.lasso)
        self.assertIn('AcquireLassoTarget(player)', self.base)
        self.assertNotIn('target.Center + Projectile.velocity', self.lasso)

    def test_loop_geometry_is_shared_with_length_and_drawing(self):
        length = self.lasso.split('private float FullLoopLength', 1)[1].split('private bool HasChainBudget', 1)[0]
        draw = self.lasso.split('private void DrawLasso', 1)[1].split('private static void DrawChainSegment', 1)[0]
        self.assertIn('LoopPoint', length)
        self.assertIn('LoopPoint', draw)
        self.assertIn('MoveHead(LoopPoint(target, angle)', self.lasso)
        self.assertNotIn('Vector2 radius = npc.Size', self.lasso)
        self.assertNotIn('TrackWinding', self.lasso)
        self.assertNotIn('winding = 0f; // Packet', self.lasso)
        self.assertIn('++blockedWrapTicks >= 12', self.lasso)

    def test_full_wrap_automatically_binds_and_starts_yank(self):
        action = self.lasso.split('if (IsAuthority && winding >=', 1)[1].split('if (reached)', 1)[0]
        self.assertIn('binding.TryBind(target, this)', action)
        self.assertIn('ChangeMode(FlailMode.Yank)', action)
        self.assertIn('binding.TryYank(target, this)', action)
        self.assertNotIn('yankRequest', self.lasso)
        self.assertIn('ChangeMode(FlailMode.Approach)', self.lasso)

    def test_yank_impact_is_once_and_not_at_launch(self):
        start = self.npc.split('public void TryYank', 1)[1].split('private SharedFlailProjectile FindHead', 1)[0]
        self.assertIn('|| Yanking', start)
        self.assertNotIn('ApplyDamageToNPC', start)
        self.assertNotIn('OnLassoYank', start)
        impact = self.base.split('public void CompleteLassoImpact', 1)[1].split('public void EndLasso', 1)[0]
        self.assertLess(impact.index('impactApplied = true'), impact.index('ApplyDamageToNPC'))
        self.assertIn('OnLassoYank(target)', impact)
        self.assertIn('if (impact) head?.CompleteLassoImpact(npc)', self.npc)
        self.assertIn('Release(npc, head, false, "Blocked")', self.npc)
        self.assertIn('step.LengthSquared() <= 0.01f', self.npc)
        self.assertNotIn('(step - wanted).LengthSquared()', self.npc)

    def test_all_progress_and_failures_are_snapshotted(self):
        send = self.lasso.split('public override void SendExtraAI', 1)[1].split('public override void ReceiveExtraAI', 1)[0]
        for value in ('orbitTarget', 'orbitType', 'facing', 'SpinTicks', 'RampTicks', 'failure',
                      'loopX', 'loopUpper', 'loopLower', 'loopLift', 'orbitStartAngle', 'winding'):
            self.assertIn(value, send)
        self.assertIn('if (IsAuthority) return', self.lasso)
        self.assertIn('FlailFailureMessage', self.lasso)
        self.assertIn('FailureDisplayTicks = 180', self.input)

    def test_snow_resource_is_independent_and_server_owned(self):
        for contract in ('FullGrowthTicks = 90', 'heldSlot != Player.selectedItem',
                         '!ReferenceEquals(heldInstance, held)', 'Player.dead', 'Player.ghost'):
            self.assertIn(contract, self.resource)
        self.assertNotIn('SaveData', self.resource)
        self.assertNotIn('OnKill', self.head)
        self.assertNotIn('netUpdate', self.head)
        self.assertNotIn('SendExtraAI', self.head)
        self.assertIn('SnowballMassMessage', self.resource)
        self.assertIn('Mode == FlailMode.Spin && SpinningThisTick', self.head)
        self.assertIn('Main.netMode != NetmodeID.MultiplayerClient', self.head)
        self.assertIn('public override void SyncPlayer', self.resource)

    def test_snow_design_bounds_and_localization(self):
        for contract in ('1f - 0.25f * mass', '1f + 0.8f * mass', '28f + 16f * mass',
                         'mass <= 0.5f', '80f * 80f', 'BuffID.Slow, 120'):
            self.assertIn(contract, self.head)
        for locale in ('en-US', 'ru-RU'):
            text = source(f'Localization/{locale}_Mods.FrostMelee.hjson')
            for key in ('Approaching:', 'Interrupted:', 'LostTarget:', 'Timeout:', 'FlailRamp:'):
                self.assertIn(key, text)
            snow = text.split('SnowballFlail: {', 1)[1].split('PolarStar:', 1)[0]
            self.assertIn('+80%', snow)
            self.assertIn('−25%', snow)

    def test_approved_recipes(self):
        items = source('Content/Items/Prototypes/PrototypeWeapons.cs')
        snow = items.split('public class SnowballFlail', 1)[1].split('[LegacyName', 1)[0]
        for value in ('ItemID.BallOHurt, ItemID.TheMeatball', 'ItemID.SnowBlock, 50',
                      'ItemID.Gel, 20', 'Condition.DownedEowOrBoc'):
            self.assertIn(value, snow)
        polar = items.split('public class PolarStar', 1)[1].split('public class SnowBoomerang', 1)[0]
        for value in ('ItemID.FrostCore', 'ItemID.Sunfury', 'ItemID.FallenStar, 7', 'ItemID.MeteoriteBar, 20'):
            self.assertIn(value, polar)


class FlailNumericalReferenceTests(unittest.TestCase):
    """Independent mathematical models, not execution of game code."""
    def test_free_spin_period_and_mass(self):
        self.assertAlmostEqual(2 * math.pi / (math.pi / 6), 12)
        self.assertAlmostEqual(2 * math.pi / (math.pi / 6 * .75), 16)
        self.assertAlmostEqual(30 * math.sqrt(2), 42.4264068712)

    def test_grounded_loop_clears_floor_for_all_head_sizes(self):
        for height in (12, 16, 24, 32, 48, 96):
            for head in (28, 36, 44):
                lift = max(0, (head - height) / 2 + 4)
                center = -height / 2 - lift
                lower = max(2, height / 2 + lift - head / 2 - 2)
                upper = height / 2 + head / 2 + 4
                for i in range(361):
                    sine = math.sin(math.radians(i))
                    bottom = center + sine * (lower if sine >= 0 else upper) + head / 2
                    self.assertLessEqual(bottom, -2 + 1e-6, (height, head, i))

    def test_wrap_pauses_at_obstacle_without_erasing_progress(self):
        winding = 0
        completed = 0
        history = []
        # Same per-tick rule as the documented design, with 4 blocked ticks.
        for tick in range(20):
            accepted = 0 if tick in (4, 5, 6, 7) else min(.4, 2 * math.pi - winding)
            winding += accepted
            history.append(winding)
            if winding >= 2 * math.pi:
                completed += 1
                break
        self.assertGreater(history[3], 0)
        self.assertEqual(history[3], history[7])
        self.assertGreater(history[8], history[7])
        self.assertEqual(completed, 1)
        self.assertAlmostEqual(winding, 2 * math.pi)

    def test_damage_curve_endpoints(self):
        def thrown(distance):
            t = max(0, min(1, (distance - 96) / (320 - 96)))
            return 1.2 + (.5 - 1.2) * t
        self.assertAlmostEqual(thrown(0), 1.2)
        self.assertAlmostEqual(thrown(96), 1.2)
        self.assertAlmostEqual(thrown(320), .5)
        self.assertGreater(thrown(100), thrown(200))
        self.assertAlmostEqual(.6 + (.8 * 90 / 90), 1.4)


if __name__ == '__main__':
    unittest.main()
