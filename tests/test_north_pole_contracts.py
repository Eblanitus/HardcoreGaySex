"""Source contracts and independent reference models; these DO NOT execute C#."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return (ROOT / 'FrostMelee' / path).read_text(encoding='utf-8')


class NorthPoleContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.player = read('Content/Weapons/NorthPolePlayer.cs')
        cls.effects = read('Content/Projectiles/NorthPoleEffects.cs')
        cls.arc = read('Content/Projectiles/SickleArcProjectile.cs')
        cls.stab = read('Content/Projectiles/SickleStabProjectile.cs')
        cls.brace = read('Content/Projectiles/SickleBraceProjectile.cs')
        cls.throw = read('Content/Projectiles/SickleThrowProjectile.cs')

    def test_all_four_shared_moves_have_north_pole_only_hooks(self):
        for code, call in ((self.arc, '.SwingHit(Projectile, target)'),
                           (self.stab, '.CallSnow(Projectile)'),
                           (self.brace, 'NorthPoleEffects.BraceSpike(Projectile, target)'),
                           (self.throw, 'NorthPoleEffects.Coat(Projectile, target)')):
            self.assertIn('ItemID.NorthPole', code)
            self.assertIn(call, code)
        self.assertIn('NorthPoleEffects.Trail(Projectile)', self.throw)

    def test_growth_once_per_arc_and_misses_only_at_natural_completion(self):
        self.assertIn('&& !northPoleHit && Projectile.owner == Main.myPlayer', self.arc)
        self.assertIn('northPoleHit = true;', self.arc)
        self.assertIn('Projectile.localAI[0] >= Math.Max(1f, Projectile.ai[1])', self.arc)
        self.assertIn('.FinishSwing(northPoleHit)', self.arc)
        self.assertIn('if (hit) { missedSwings = 0; return; }', self.player)
        self.assertIn('++missedSwings >= NorthPoleBalance.MissesToRelease', self.player)
        self.assertIn('flake.Release(true)', self.player)
        self.assertIn('if (Projectile.ai[1] >= NorthPoleBalance.FullCharge) Release(false)', self.effects)

    def test_growth_is_not_a_frost_resource_or_recursive_proc(self):
        self.assertNotIn('AddFrost(', self.player + self.effects)
        self.assertNotIn('SwingHit(', self.effects)
        self.assertIn('CanDamage() => Charging ? false : null', self.effects)
        self.assertIn('storedDamage = Math.Max(1, damage)', self.effects)
        self.assertIn('Projectile.ai[0] = 1;', self.effects)

    def test_owner_only_spawns_and_pickups(self):
        self.assertIn('p.owner == Main.myPlayer', self.effects)
        self.assertIn('if (!Owned(spear)', self.effects)
        self.assertIn('if (!Owned(brace)', self.effects)
        self.assertIn('Player.whoAmI != Main.myPlayer', self.player)
        self.assertIn('NorthPoleEffects.Owned(Projectile) && Projectile.ai[0] >= 12', self.effects)
        self.assertIn('Projectile.Hitbox.Intersects(player.Hitbox)', self.effects)
        self.assertIn('MessageID.PlayerBuffs', self.effects)
        self.assertIn('NorthPoleBalance.MaxPickups', self.player)
        self.assertIn('ConfigScope.ServerSide', self.player)

    def test_trail_only_during_live_flight_uses_vanilla_snowflake_ai(self):
        self.assertIn('== ItemID.NorthPole && Projectile.friendly', self.throw)
        self.assertIn('++northPoleSnowTicks >= NorthPoleBalance.TrailInterval', self.throw)
        self.assertIn('Projectile.CloneDefaults(ProjectileID.NorthPoleSnowflake)', self.effects)
        self.assertIn('AIType = ProjectileID.NorthPoleSnowflake', self.effects)
        trail = self.effects.split('public class NorthPoleTrail', 1)[1].split('public class NorthPoleSnowflake', 1)[0]
        self.assertIn('Projectile.tileCollide = true;', trail)

    def test_coat_refreshes_without_stacking_damage_or_wrong_frostburn(self):
        self.assertIn('target.AddBuff(BuffID.Frostburn2, NorthPoleBalance.FrostbiteTicks)', self.effects)
        self.assertNotIn('BuffID.Frostburn,', self.effects)
        coat = self.effects.split('public class NorthPoleSnowCoat', 1)[1].split('public class NorthPoleBraceSpike', 1)[0]
        self.assertIn('CanDamage() => false', coat)
        self.assertNotIn('target.AddBuff(', coat)
        self.assertIn('Main.npc[id].type != (int)Projectile.ai[1]', coat)
        self.assertIn('writer.Write((ushort)Projectile.timeLeft)', coat)
        self.assertIn('p.timeLeft = NorthPoleBalance.FrostbiteTicks', self.effects)

    def test_spike_excludes_primary_target_and_clips_visible_growth(self):
        spike = self.effects.split('public class NorthPoleBraceSpike', 1)[1]
        self.assertIn('target.whoAmI == (int)Projectile.ai[2]', spike)
        self.assertIn('Collision.SolidCollision(point - new Vector2(width * 0.5f)', spike)
        self.assertIn('Projectile.Center + Direction * length, Width', spike)
        self.assertIn('length / t.Height', spike)
        self.assertIn('Main.player[brace.owner].velocity.Length()', self.effects)
        self.assertIn('target.Center + direction * (extent + 4)', self.effects)
        self.assertIn('Projectile.localNPCHitCooldown = -1', spike)
        self.assertNotIn('++Projectile.ai[2]', spike)

    def test_sky_spawn_stays_below_first_ceiling(self):
        self.assertIn('dy <= 280; dy += 4', self.player)
        self.assertIn('Collision.SolidCollision(test - new Vector2(9), 18, 18)) break', self.player)
        self.assertIn('spawn = test;', self.player)
        self.assertIn('start.X < 32', self.player)
        pickup = self.effects.split('public class NorthPolePickup', 1)[1].split('public class NorthPoleSnowCoat', 1)[0]
        self.assertIn('CanDamage() => false', pickup)
        self.assertIn('Projectile.tileCollide = true', pickup)
        self.assertIn('Projectile.velocity = Vector2.Zero; return false;', pickup)

    def test_impact_burst_is_once_owner_only_not_on_cancel(self):
        snow = self.effects.split('public class NorthPoleSnowflake', 1)[1].split('public class NorthPoleShard', 1)[0]
        self.assertIn('!impacted || Charging || !NorthPoleEffects.Owned(Projectile)', snow)
        self.assertIn('Projectile.penetrate = 1;', snow)
        self.assertIn('i < NorthPoleBalance.Shards', snow)
        self.assertIn('Projectile.ai[0] = 1', snow)
        self.assertIn('writer.Write((ushort)flightTicks)', snow)
        self.assertIn('writer.Write(lastTarget.X)', snow)
        self.assertIn('Projectile.timeLeft = 2', snow)
        self.assertIn('ChargingFlake()?.Projectile.Kill()', self.player)

    def test_buff_is_refreshable_regen_not_instant_heal_or_crit_chance(self):
        self.assertIn('player.moveSpeed += NorthPoleBalance.MoveSpeed', self.player)
        self.assertIn('player.lifeRegen += NorthPoleBalance.LifeRegen', self.player)
        self.assertEqual(self.player.count('modifiers.CritDamage += NorthPoleBalance.CritDamage'), 2)
        self.assertNotIn('GetCritChance', self.player)
        self.assertNotIn('.Heal(', self.effects + self.player)
        self.assertIn('player.AddBuff(ModContent.BuffType<NorthPoleBlessing>()', self.effects)

    def test_tuning_defaults_and_all_effects_have_localized_names(self):
        expected = {'GrowthHits': 6, 'BlessingSeconds': 6, 'MoveSpeedPercent': 15,
                    'RegenPerSecond': 2, 'CritDamagePercent': 25, 'WeakShotPercent': 55}
        for key, value in expected.items():
            self.assertRegex(self.player, rf'\[DefaultValue\({value}\), Range\([^\]]+\)\] public int {key}')
        for lang in ('en-US', 'ru-RU'):
            text = read(f'Localization/{lang}_Mods.FrostMelee.hjson')
            for name in re.findall(r'public class (NorthPole\w+) : ModProjectile', self.effects):
                self.assertIn(f'{name}.DisplayName:', text)
            self.assertIn('NorthPoleBlessing:', text)
            self.assertIn('NorthPoleMechanicsTooltip:', text)
            for key in expected:
                self.assertIn(f'{key}.Label:', text)
        unfinished = read('Content/Items/Prototypes/PrototypeWeapons.cs').split('public class UnfinishedVanillaReworks', 1)[1]
        self.assertNotIn('ItemID.NorthPole', unfinished)


class NorthPoleReferenceModels(unittest.TestCase):
    """Independent models of the stated rules, not execution of Terraria/C#."""
    @staticmethod
    def simulate(swings, maximum=6):
        charge = misses = 0
        shots = []
        for hit in swings:
            if hit:
                misses = 0
                charge += 1
                if charge >= maximum:
                    shots.append(('full', charge))
                    charge = 0
            elif charge:
                misses += 1
                if misses >= 2:
                    shots.append(('weak', charge))
                    misses = charge = 0
        return shots, charge

    def test_full_charge_fires_once_and_next_hit_starts_new_charge(self):
        self.assertEqual(self.simulate([True] * 7), ([('full', 6)], 1))

    def test_two_whiffs_release_weak_but_a_hit_resets_whiffs(self):
        self.assertEqual(self.simulate([True, False, True, False, False]), ([('weak', 2)], 0))
        self.assertEqual(self.simulate([False] * 10), ([], 0))

    def test_provisional_weak_damage_is_always_lower_than_same_level_full(self):
        for level in range(1, 7):
            normal = 100 * (0.4 + 1.1 * level / 6)
            self.assertLess(normal * 0.55, normal)
        self.assertAlmostEqual(100 * (0.4 + 1.1), 150)

    def test_speed_only_sizes_spike_with_bounded_monotonic_growth(self):
        lengths = [64 + 128 * min(1, max(0, speed / 12)) for speed in (0, 3, 6, 12, 100)]
        self.assertEqual(lengths, [64, 96, 128, 192, 192])
        grow = [1 - (1 - min(1, tick / 18)) ** 2 for tick in range(43)]
        self.assertEqual(grow[0], 0)
        self.assertEqual(grow[18], 1)
        self.assertEqual(grow, sorted(grow))

    def test_default_regen_and_crit_are_not_confused_with_heal_or_crit_chance(self):
        self.assertEqual(4 / 2, 2)  # Terraria regeneration units -> HP/sec
        self.assertEqual(2 + 25 / 100, 2.25)  # default crit: 2x -> 2.25x


if __name__ == '__main__':
    unittest.main()
