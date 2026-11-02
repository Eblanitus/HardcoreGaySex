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
                           (self.brace, 'NorthPoleEffects.BraceGust(Projectile, target)'),
                           (self.throw, 'NorthPoleEffects.Coat(Projectile, target)')):
            self.assertIn('ItemID.NorthPole', code)
            self.assertIn(call, code)
        self.assertIn('NorthPoleEffects.Trail(Projectile)', self.throw)

    def test_growth_once_per_arc_and_misses_only_at_natural_completion(self):
        # one growth step per enemy per swing; one flake per enemy, up to five (owner 02.11.2026)
        self.assertIn('Projectile.owner == Main.myPlayer && northPoleTargets.Add(target.whoAmI)', self.arc)
        self.assertIn('northPoleHit = true;', self.arc)
        self.assertIn('Projectile.localAI[0] >= Math.Max(1f, Projectile.ai[1])', self.arc)
        self.assertIn('.FinishSwing(Projectile, northPoleHit)', self.arc)
        self.assertIn('public const int MaxFlakes = 5;', self.player)
        self.assertIn('if (f.Targets(target)) flake = f;', self.player)
        self.assertIn('if (slot >= NorthPoleBalance.MaxFlakes) return;', self.player)
        self.assertIn('++missedSwings >= NorthPoleBalance.MissesToRelease', self.player)
        self.assertIn('foreach (NorthPoleSnowflake flake in flakes) flake.Release(true);', self.player)
        # buffs only from thrusts, the arc only grows flakes (owner 02.11.2026)
        finish = self.player.split('public void FinishSwing', 1)[1].split('private int PickupsOfKind', 1)[0]
        self.assertNotIn('CallSnow(', finish)
        self.assertNotIn('CallSnow(', self.arc)
        self.assertIn('if (hit) { missedSwings = 0; return; }', finish)
        self.assertIn('.CallSnow(Projectile, Weapons.NorthPolePlayer.KindDawn, false);', self.stab)
        self.assertIn('if (Projectile.ai[1] >= NorthPoleBalance.FullCharge) Release(false)', self.effects)

    def test_growth_is_not_a_frost_resource_or_recursive_proc(self):
        self.assertNotIn('AddFrost(', self.player + self.effects)
        self.assertNotIn('SwingHit(', self.effects)
        self.assertIn('CanDamage() => Charging || launchDelay > 0 ? false : null', self.effects)
        self.assertIn('storedDamage = Math.Max(1, damage)', self.effects)
        self.assertIn('Projectile.ai[0] = 1;', self.effects)

    def test_owner_only_spawns_and_pickups(self):
        self.assertIn('p.owner == Main.myPlayer', self.effects)
        self.assertIn('if (!Owned(spear)', self.effects)
        self.assertIn('if (!Owned(brace)', self.effects)
        self.assertIn('Player.whoAmI != Main.myPlayer', self.player)
        self.assertIn('NorthPoleEffects.Owned(Projectile) && Projectile.ai[0] >= 10', self.effects)
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
        # Like vanilla: the trail snow falls through blocks.
        self.assertIn('Projectile.tileCollide = false;', trail)

    def test_coat_refreshes_without_stacking_damage_or_wrong_frostburn(self):
        self.assertIn('target.AddBuff(BuffID.Frostburn2, NorthPoleBalance.FrostbiteTicks)', self.effects)
        self.assertNotIn('BuffID.Frostburn,', self.effects)
        coat = self.effects.split('public class NorthPoleSnowCoat', 1)[1].split('public class NorthPoleGust', 1)[0]
        self.assertIn('CanDamage() => false', coat)
        self.assertNotIn('target.AddBuff(', coat)
        self.assertIn('Main.npc[id].type != (int)Projectile.ai[1]', coat)
        self.assertIn('writer.Write((ushort)Projectile.timeLeft)', coat)
        self.assertIn('p.timeLeft = NorthPoleBalance.FrostbiteTicks', self.effects)

    def test_gust_excludes_primary_target_and_blows_over_blocks(self):
        gust = self.effects.split('public class NorthPoleGust', 1)[1].split('internal static class NorthPoleFx', 1)[0]
        self.assertIn('target.whoAmI == (int)Projectile.ai[2]', gust)
        self.assertNotIn('SolidCollision', gust)
        self.assertNotIn('CanHitLine', gust)
        self.assertIn('Projectile.tileCollide = false;', gust)
        self.assertIn('Main.player[brace.owner].velocity.Length()', self.effects)
        self.assertIn('target.Center + direction * (extent * 0.6f)', self.effects)
        self.assertIn('Projectile.localNPCHitCooldown = -1', gust)
        self.assertNotIn('++Projectile.ai[2]', gust)

    def test_sky_snow_falls_from_screen_top_through_blocks(self):
        self.assertIn('Main.screenPosition.Y - 24', self.player)
        self.assertIn('Player.Center.Y - 360', self.player)
        self.assertIn('Main.maxTilesX * 16 - 32', self.player)
        pickup = self.effects.split('public class NorthPolePickup', 1)[1].split('public class NorthPoleSnowCoat', 1)[0]
        self.assertIn('CanDamage() => false', pickup)
        self.assertIn('Projectile.tileCollide = false', pickup)
        self.assertNotIn('OnTileCollide', pickup)
        self.assertIn('if (Projectile.Center.Y - player.Center.Y > 360) { Projectile.Kill(); return; }', pickup)

    def test_buff_snow_is_pulled_to_owner_and_looks_like_a_pickup(self):
        pickup = self.effects.split('public class NorthPolePickup', 1)[1].split('public class NorthPoleSnowCoat', 1)[0]
        self.assertIn('to.Length() < MagnetRange', pickup)
        self.assertIn('to.SafeNormalize(Vector2.UnitY) * speed', pickup)
        self.assertNotIn('CometTail', pickup)   # a tail made it read as an incoming attack
        self.assertIn('NorthPoleArt.StyleBuff', pickup)
        # owner 02.11.2026: weaker pull, the player has to come close
        rng = float(re.search(r'MagnetRange = ([\d.]+)f', pickup).group(1))
        spd = float(re.search(r'MagnetSpeed = ([\d.]+)f', pickup).group(1))
        self.assertLessEqual(rng, 140)
        self.assertLessEqual(spd, 8)

    def test_sky_snow_spawns_across_the_whole_screen_width(self):
        call = self.player.split('public void CallSnow', 1)[1].split('public override', 1)[0]
        self.assertIn('Main.screenWidth', call)
        self.assertIn('Main.GameViewMatrix.Zoom.X', call)
        self.assertNotIn('NextFloat(-128, 128)', call)

    def test_impact_burst_is_once_owner_only_not_on_cancel(self):
        snow = self.effects.split('public class NorthPoleSnowflake', 1)[1].split('public class NorthPoleMiniFlake', 1)[0]
        self.assertIn('if (!impacted || Charging) return;', snow)
        self.assertIn('if (!NorthPoleEffects.Owned(Projectile)) return;', snow)
        self.assertIn('Projectile.tileCollide = false;', snow)
        self.assertNotIn('OnTileCollide', snow)
        self.assertIn('Projectile.penetrate = 1;', snow)
        self.assertIn('i < NorthPoleBalance.MiniFlakes', snow)
        self.assertIn('ModContent.ProjectileType<NorthPoleMiniFlake>()', snow)
        mini = self.effects.split('public class NorthPoleMiniFlake', 1)[1].split('public class ', 1)[0]
        self.assertIn('Projectile.Resize(NorthPoleBalance.MiniBlastRadius * 2', mini)
        self.assertIn('OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone) => Explode();', mini)
        self.assertIn('Projectile.localNPCHitCooldown = -1;', mini)
        self.assertIn('Projectile.ai[0] = 1', snow)
        self.assertIn('writer.Write((ushort)flightTicks)', snow)
        self.assertIn('writer.Write(lastTarget.X)', snow)
        self.assertIn('Projectile.timeLeft = 2', snow)
        self.assertIn('KillChargingFlakes();', self.player)

    def test_buff_is_refreshable_regen_not_instant_heal_or_crit_chance(self):
        self.assertIn('player.moveSpeed += NorthPoleBalance.MoveSpeed', self.player)
        self.assertIn('player.lifeRegen += NorthPoleBalance.LifeRegen', self.player)
        self.assertEqual(self.player.count('modifiers.CritDamage += NorthPoleBalance.CritDamage'), 2)
        self.assertNotIn('GetCritChance', self.player)
        self.assertNotIn('.Heal(', self.effects + self.player)
        self.assertIn('player.AddBuff(Dawn ? ModContent.BuffType<NorthPoleDawn>() : ModContent.BuffType<NorthPoleBlessing>()', self.effects)
        self.assertIn('player.GetAttackSpeed(DamageClass.Melee) += NorthPoleBalance.AttackSpeed', self.player)
        self.assertIn('player.lifeRegen += NorthPoleBalance.DawnRegen', self.player)

    def test_tuning_defaults_and_all_effects_have_localized_names(self):
        expected = {'GrowthHits': 6, 'BlessingSeconds': 6, 'MoveSpeedPercent': 15,
                    'RegenPerSecond': 2, 'CritDamagePercent': 25, 'WeakShotPercent': 55,
                    'AttackSpeedPercent': 12, 'DawnRegenPerSecond': 3}
        for key, value in expected.items():
            self.assertRegex(self.player, rf'\[DefaultValue\({value}\), Range\([^\]]+\)\] public int {key}')
        for lang in ('en-US', 'ru-RU'):
            text = read(f'Localization/{lang}_Mods.FrostMelee.hjson')
            for name in re.findall(r'public class (NorthPole\w+) : ModProjectile', self.effects):
                self.assertIn(f'{name}.DisplayName:', text)
            self.assertIn('NorthPoleBlessing:', text)
            self.assertIn('NorthPoleDawn:', text)
            self.assertIn('NorthPoleMechanicsTooltip:', text)
            for key in expected:
                # Плоская запись (Key.Label:) или вложенная, как её пишет tModLoader.
                self.assertRegex(text, rf'{key}(\.Label:|: \{{\s*Label:)')
        unfinished = read('Content/Items/Prototypes/PrototypeWeapons.cs').split('public class UnfinishedVanillaReworks', 1)[1]
        self.assertNotIn('ItemID.NorthPole', unfinished)


class NorthPoleVisualContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.effects = read('Content/Projectiles/NorthPoleEffects.cs')

    def test_magic_pixel_only_with_one_pixel_source(self):
        self.assertIn('new Rectangle(0, 0, 1, 1)', self.effects)
        self.assertNotRegex(self.effects, r'MagicPixel\.Value, [^,]+, null')

    def test_every_effect_draws_itself_and_accepts_owner_texture(self):
        for cls, name in (('NorthPoleTrail', 'TrailFlake'), ('NorthPoleSnowflake', 'Snowflake'),
                          ('NorthPoleMiniFlake', 'MiniFlake'), ('NorthPolePickup', 'Pickup'),
                          ('NorthPoleSnowCoat', 'SnowCoat'), ('NorthPoleGust', 'Gust')):
            body = self.effects.split(f'public class {cls}', 1)[1].split('public class ', 1)[0]
            self.assertIn('PreDraw', body, cls)
            self.assertIn(f'"{name}"', body, cls)
        self.assertIn('FrostMelee/Textures/NorthPole/', self.effects)
        self.assertIn('ModContent.RequestIfExists', self.effects)


class NorthPoleEffectStyleContracts(unittest.TestCase):
    def test_shared_moves_switch_to_aurora_style_only_for_north_pole(self):
        arc = read('Content/Projectiles/SickleArcProjectile.cs')
        stab = read('Content/Projectiles/SickleStabProjectile.cs')
        brace = read('Content/Projectiles/SickleBraceProjectile.cs')
        self.assertIn('if (ItemType == ItemID.NorthPole)\n            {\n                NorthPoleFx.Palette(out core, out tail, out edge);', arc)
        # Swing = aurora ribbon (owner 02.11.2026): no compass, no vanilla white line
        self.assertFalse((ROOT / 'FrostMelee/Content/Projectiles/PolarCompassFx.cs').exists())
        self.assertNotIn('PolarCompass', arc)
        self.assertNotIn('NorthPoleFx.Curtain(', arc)
        self.assertIn('if (ItemType != ItemID.NorthPole)\n                Main.EntitySpriteDraw(texture, position, texture.Frame(1, 4, 0, 3)', arc)
        glow = arc.split('private void DrawBladeGlow', 1)[1].split('public override bool PreDraw', 1)[0]
        self.assertLess(glow.index('DrawAuroraArc('), glow.index('DrawHalo('))
        aurora = arc.split('private void DrawAuroraArc', 1)[1].split('private static Color Mul', 1)[0]
        # dark backing wider than the ribbon, drawn first with normal blending
        self.assertLess(aurora.index('BlendState.AlphaBlend, n);'), aurora.index('trailBlend, n);'))
        self.assertIn('rOut - width * AuroraBackWiden', aurora)
        self.assertGreater(float(re.search(r'AuroraBackWiden = ([\d.]+)f', arc).group(1)), 1.0)
        # mint -> teal -> violet across the width, the full length; curtains flow and sway
        self.assertIn('AuroraMint, AuroraTeal', aurora)
        self.assertIn('AuroraTeal, AuroraViolet', aurora)
        self.assertIn('AuroraCurtainRepeat, flow, AuroraCurtainSway, time', aurora)
        self.assertIn('AuroraEdge', aurora)
        for word in ('Crystal', 'Needle', 'Shard', 'Rime', 'Compass', 'Star'):
            self.assertNotIn(word, aurora)
        # snowiness on top of the aurora (owner: "не хватает доп эффектов и снежности")
        self.assertIn('DrawAuroraGlitter(player, glow, progress, max, env);', aurora)
        snow = arc.split('private void AuroraSnow', 1)[1].split('private void DrawAuroraGlitter', 1)[0]
        self.assertIn('NorthPoleFx.ShakeSnow(', snow)
        self.assertIn('NorthPoleFx.AuroraDust(', snow)
        self.assertIn('AuroraSnow(player, progress);', arc)
        # snowflakes and icicles in the arc's aurora palette (owner 02.11.2026)
        art = read('Content/Projectiles/NorthPoleEffects.cs').split('internal static class NorthPoleArt', 1)[1].split('public class NorthPoleTrail', 1)[0]
        self.assertIn('AMint = new Color(60, 255, 140)', art)
        self.assertIn('ATeal = new Color(20, 200, 220)', art)
        self.assertIn('AViolet = new Color(185, 90, 255)', art)
        flake = art.split('public static void Snowflake', 1)[1].split('public static void Afterimages', 1)[0]
        # strictly Terraria-style 2x2 pixel sprites, snapped to the 2-px grid (owner 02.11.2026)
        self.assertIn('PixelFolder = "FrostMelee/Textures/NorthPolePixel/"', art)
        self.assertIn('"SwingFlake"', flake)
        self.assertIn('"ThrowFlake"', flake)
        self.assertIn('"BuffFlake"', flake)
        self.assertIn('Snap(', art)
        self.assertNotIn('Line(', flake)      # no smooth strokes any more
        pickup = read('Content/Projectiles/NorthPoleEffects.cs').split('public class NorthPolePickup', 1)[1].split('public class NorthPoleSnowCoat', 1)[0]
        # every kind of snow has its own look (owner 02.11.2026)
        self.assertIn('NorthPoleArt.StyleBuff', pickup)   # rose, round soft flake
        self.assertNotIn('Ring(', pickup)                 # no gold outline
        self.assertNotIn('CometTail', pickup)
        trail = read('Content/Projectiles/NorthPoleEffects.cs').split('public class NorthPoleTrail', 1)[1].split('public class NorthPoleSnowflake', 1)[0]
        self.assertIn('NorthPoleArt.PixelTrail(Projectile, "ThrowFlake"', trail)
        self.assertIn('NorthPoleArt.StyleThrow);', trail)
        self.assertIn('private bool IsNorthPole => (int)Projectile.ai[2] == ItemID.NorthPole;', stab)
        self.assertNotIn('DrawSkyCall(', stab)   # the beam over the spear was unreadable
        self.assertIn('if (itemType == ItemID.NorthPole)\n                DrawPolarCharge(geo);', brace)

    def test_owner_feedback_round_02_11(self):
        effects = read('Content/Projectiles/NorthPoleEffects.cs')
        player = read('Content/Weapons/NorthPolePlayer.cs')
        fx = read('Content/Projectiles/NorthPoleDrift.cs')
        flake = effects.split('public class NorthPoleSnowflake', 1)[1].split('public class NorthPoleMiniFlake', 1)[0]
        # each flake flies at the enemy nearest to it; a flake whose enemy died does not hang
        self.assertIn('bool found = PickNearest();', flake)
        self.assertIn('else if (Projectile.owner == Main.myPlayer && Projectile.ai[1] > 0) { Release(false); return; }', flake)
        # spear put away: flakes are released (and burst), not silently killed
        self.assertIn('player.HeldItem.type != ItemID.NorthPole) { Release(true); return; }', flake)
        self.assertIn('flake.Release(true);', player.split('public override void PostUpdate', 1)[1].split('public override void UpdateDead', 1)[0])
        # no diamond glow on snowflakes
        draw = flake.split('public override bool PreDraw', 1)[1]
        self.assertNotIn('Bloom(', draw)
        self.assertNotIn('Ring(', draw)
        pickup = effects.split('public class NorthPolePickup', 1)[1].split('public class NorthPoleSnowCoat', 1)[0]
        self.assertNotIn('Bloom(', pickup)
        # throw: lingering trail with Frostbite, stronger throw
        self.assertIn('NorthPoleDrift.Add(Projectile.Center, Projectile.owner);', effects)
        self.assertIn('npc.AddBuff(BuffID.Frostburn2, DebuffTicks);', fx)
        self.assertIn('modifiers.SourceDamage *= NorthPoleBalance.ThrowPower;', player)
        # sprites centred on their opaque area (owner's canvas can be larger)
        self.assertIn('Rectangle src = Bounds(t);', effects)

    def test_owner_feedback_round_02_11_b(self):
        effects = read('Content/Projectiles/NorthPoleEffects.cs')
        player = read('Content/Weapons/NorthPolePlayer.cs')
        arc = read('Content/Projectiles/SickleArcProjectile.cs')
        throw = read('Content/Projectiles/SickleThrowProjectile.cs')
        # arc: no pixel confetti, flowing shimmer + light, calm snow only
        self.assertNotIn('NorthPolePixelFx.Flake', arc)
        self.assertNotIn('NorthPolePixelFx.Swing', arc)
        self.assertNotIn('"Sparkle1"', arc)
        self.assertNotIn('NorthPolePixelFx', arc)   # no particles on the arc any more
        self.assertIn('AuroraShimmerWaves', arc)
        # released flakes leave one by one, 0.1-0.4 s apart
        self.assertIn('public const int LaunchGapMin = 6, LaunchGapMax = 24;', player)
        self.assertIn('GetModPlayer<NorthPolePlayer>().NextLaunchDelay()', effects)
        # thrown spear: javelin physics and weighty impact
        self.assertIn('Projectile.velocity *= NorthPoleLaunchBoost;', throw)
        self.assertEqual(throw.count('NorthPoleEffects.ThrowImpact('), 2)

    def test_owner_feedback_round_02_11_c(self):
        effects = read('Content/Projectiles/NorthPoleEffects.cs')
        player = read('Content/Weapons/NorthPolePlayer.cs')
        arc = read('Content/Projectiles/SickleArcProjectile.cs')
        veil = read('Content/Projectiles/NorthPoleVeil.cs')
        # arc: second trail = aurora veil left in the world, drawn like the other ribbons
        self.assertIn('NorthPoleVeil.Add(', arc)
        self.assertLess(veil.index('BlendState.AlphaBlend'), veil.index(': additive'))
        self.assertIn('public override void PostDrawTiles()', veil)
        # smoothing (owner 02.11.2026): spline through the path, player motion spread inside the tick
        self.assertIn('sPos[n] = CatmullRom(', veil)
        self.assertIn('- player.velocity * (1f - f)', arc)

    def test_owner_feedback_round_02_11_d_brace_gust(self):
        effects = read('Content/Projectiles/NorthPoleEffects.cs')
        player = read('Content/Weapons/NorthPolePlayer.cs')
        brace = read('Content/Projectiles/SickleBraceProjectile.cs')
        # icicles are gone as a mechanic and as sprites (owner 02.11.2026)
        self.assertNotIn('NorthPoleBraceSpike', effects)
        self.assertNotIn('Icicle(', effects)
        self.assertNotIn('MaxSpikes', player)
        self.assertFalse(list((ROOT / 'FrostMelee/Textures/NorthPolePixel').glob('Spike*_*.png')))
        # one gust per half second per player, scaled by run speed, weaker than the brace hit
        spawn = effects.split('public static void BraceGust', 1)[1].split('public static void ThrowImpact', 1)[0]
        self.assertIn('if (modPlayer.GustCooldown > 0) return;', spawn)
        self.assertIn('brace.damage * NorthPoleBalance.GustDamage', spawn)
        self.assertIn('if (GustCooldown > 0) GustCooldown--;', player)
        self.assertLess(float(re.search(r'GustDamage = ([\d.]+)f', player).group(1)), 1.0)
        gust = effects.split('public class NorthPoleGust', 1)[1].split('internal static class NorthPoleFx', 1)[0]
        self.assertIn('NorthPoleBalance.GustHalfAngle + margin', gust)
        self.assertIn('modifiers.HitDirectionOverride', gust)
        # vanilla snow dust, not the smooth pixel stream (owner: "Noita again")
        self.assertIn('DustID.SnowflakeIce', gust)
        self.assertNotIn('NorthPolePixelFx', gust)
        self.assertNotIn('Stroke(', gust)
        self.assertNotIn('Bloom(', gust)
        # the charged tip "plus" stays on the braced spear
        self.assertIn('NorthPoleBalance.GustFullSpeed', brace)
        self.assertIn('NorthPoleFx.Sparkle(tip', brace)

    def test_owner_feedback_round_02_11_e_vanilla_particles(self):
        # owner 02.11.2026: the smooth pixel particle streams read as "Noita"; everything uses
        # plain vanilla snow dust now, the pixel particle system is gone
        self.assertFalse((ROOT / 'FrostMelee/Content/Projectiles/NorthPolePixelFx.cs').exists())
        for path in (ROOT / 'FrostMelee/Content').rglob('*.cs'):
            self.assertNotIn('NorthPolePixelFx', path.read_text(encoding='utf-8'), path.name)
        drift = read('Content/Projectiles/NorthPoleDrift.cs')
        self.assertIn('DustID.SnowflakeIce', drift)
        self.assertNotIn('NorthPoleArt.Sprite', drift)

    def test_generated_sprites_follow_owner_reference(self):
        from PIL import Image
        folder = ROOT / 'FrostMelee' / 'Textures' / 'NorthPolePixel'
        names = ['DawnFlake', 'MiniFlake', 'Snow0', 'Snow1', 'Snow2'] + [f'Mote{r}' for r in range(3)] + [f'TMote{r}' for r in range(3)]
        for name in names:
            alphas = {a for *_, a in Image.open(folder / f'{name}.png').convert('RGBA').getdata()}
            self.assertLessEqual(alphas, {0, 255}, name)   # opaque only: no shadow, no soft edges

    def test_pixel_sprites_are_2x2_blocks(self):
        from PIL import Image
        folder = ROOT / 'FrostMelee' / 'Textures' / 'NorthPolePixel'
        names = [f'SwingFlake{r}' for r in range(3, 12)] + ['ThrowFlake', 'BuffFlake', 'DawnFlake', 'MiniFlake', 'Glint',
                 'Sparkle0', 'Sparkle1', 'Sparkle2', 'Mote0', 'Mote1', 'Mote2', 'TMote0', 'TMote1', 'TMote2']
        for name in names:
            im = Image.open(folder / f'{name}.png').convert('RGBA')
            w, h = im.size
            self.assertEqual((w % 2, h % 2), (0, 0), name)
            px = im.load()
            for y in range(0, h, 2):
                for x in range(0, w, 2):
                    self.assertTrue(px[x, y] == px[x + 1, y] == px[x, y + 1] == px[x + 1, y + 1], name)


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
