"""Generates North Pole pixel-art sprites in Terraria style.

Each sprite is drawn on an art grid with a 1-px dark outline and a small
palette, then upscaled x2 with nearest neighbour, so every art pixel is a
2x2 block, as in vanilla Terraria textures. Output:
FrostMelee/Textures/NorthPolePixel/*.png (drawn at scale 1 by NorthPoleArt).

Run: python3 tools/make_north_pole_pixels.py
"""
import math
from pathlib import Path
from PIL import Image

OUT = Path(__file__).resolve().parents[1] / 'FrostMelee' / 'Textures' / 'NorthPolePixel'

# palettes: outline, darkest .. lightest
SWING = [(38, 18, 84), (128, 58, 222), (28, 168, 196), (70, 232, 156), (214, 255, 232)]
THROW = [(10, 44, 84), (18, 112, 172), (44, 196, 246), (178, 242, 255), (255, 255, 255)]
BUFF = [(86, 18, 70), (196, 54, 172), (250, 112, 186), (255, 196, 226), (255, 248, 252)]


def save(grid, name):
    h, w = len(grid), len(grid[0])
    im = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    for y in range(h):
        for x in range(w):
            if grid[y][x] is not None:
                im.putpixel((x, y), grid[y][x] + (255,) if len(grid[y][x]) == 3 else grid[y][x])
    im = im.resize((w * 2, h * 2), Image.NEAREST)
    OUT.mkdir(parents=True, exist_ok=True)
    im.save(OUT / f'{name}.png')


def outline(cells, w, h, color):
    """cells: dict (x,y)->rgb. Adds a 1-px dark drop shadow down-right of the shape (like
    vanilla particles/projectiles) instead of a full outline: a full 4-neighbour outline turned
    thin diagonal arms into a dark checkerboard. Thin bright shapes still read on snow."""
    grid = [[None] * w for _ in range(h)]
    for (x, y), c in cells.items():
        grid[y][x] = c
    for (x, y) in list(cells):
        for dx, dy in ((1, 1), (1, 0), (0, 1)):
            nx, ny = x + dx, y + dy
            if 0 <= nx < w and 0 <= ny < h and grid[ny][nx] is None:
                grid[ny][nx] = color + (200,) if (dx, dy) == (1, 1) else color + (150,)
    return grid


def shade(pal, t):
    """t 0 (centre / tip) .. 1 (far): lightest -> darkest body colour (pal[1..4])."""
    i = min(3, int(t * 4))
    return pal[4 - i]


def snowflake(R, pal, branches=True):
    """Classic pixel snowflake: 4 straight arms + 4 diagonal arms, V-branches on the straight ones."""
    size = 2 * R + 3
    c = R + 1
    cells = {}

    def put(x, y, d):
        if 0 <= x < size and 0 <= y < size:
            t = d / max(1, R)
            cur = cells.get((x, y))
            col = shade(pal, t)
            if cur is None or pal.index(col) > pal.index(cur):
                cells[(x, y)] = col

    for k in range(R + 1):
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            put(c + dx * k, c + dy * k, k)
    diag = max(1, round(R * 0.62))
    for k in range(1, diag + 1):
        for dx, dy in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
            put(c + dx * k, c + dy * k, k * 1.25)
    if branches and R >= 4:
        at = max(2, round(R * 0.55))
        blen = max(1, round(R * 0.3))
        for j in range(1, blen + 1):
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                px, py = -dy, dx  # perpendicular
                for s in (1, -1):
                    put(c + dx * (at + j) + px * j * s, c + dy * (at + j) + py * j * s, at + j)
    # bright heart
    for dx, dy in ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)):
        cells[(c + dx, c + dy)] = pal[4]
    return outline(cells, size, size, pal[0])


def soft_flake(R, pal):
    """Buff snow: round soft puff — filled disc heart, 8 short stubby arms with 2x2 blobs."""
    size = 2 * R + 3
    c = R + 1
    cells = {}
    disc = max(2, round(R * 0.45))
    for y in range(size):
        for x in range(size):
            d = math.hypot(x - c, y - c)
            if d <= disc + 0.3:
                cells[(x, y)] = pal[4] if d < disc * 0.45 else pal[3] if d < disc * 0.8 else pal[2]
    for a in range(8):
        ang = a * math.pi / 4
        dx, dy = round(math.cos(ang)), round(math.sin(ang))
        arm = R - 1 if a % 2 == 0 else max(disc + 1, R - 2)
        for k in range(disc, arm + 1):
            cells.setdefault((c + dx * k, c + dy * k), pal[2])
        bx, by = c + dx * arm, c + dy * arm
        for ox, oy in ((0, 0), (1, 0), (0, 1), (1, 1)):
            x, y = bx + ox - (1 if dx < 0 else 0), by + oy - (1 if dy < 0 else 0)
            if 0 <= x < size and 0 <= y < size:
                cells[(x, y)] = pal[3] if (ox, oy) == (0, 0) else pal[2]
    return outline(cells, size, size, pal[0])


def icicle(width, length, pal):
    """Tapered icicle, tip at the top row, base at the bottom: straight diagonal sides,
    violet base -> teal -> mint tip, a lit facet stripe left of the centre, dark right edge."""
    w = width + 2
    h = length + 2
    cx = w / 2
    cells = {}
    for y in range(1, h - 1):
        t = (y - 1) / max(1, length - 1)          # 0 tip .. 1 base
        half = max(0.5, (width / 2) * t ** 0.85)
        body = pal[3] if t < 0.3 else pal[2] if t < 0.65 else pal[1]
        xs = [x for x in range(1, w - 1) if abs(x + 0.5 - cx) <= half]
        for x in xs:
            cells[(x, y)] = body
        if xs:
            cells[(xs[0], y)] = pal[4] if t < 0.45 else pal[3]          # lit edge
            if len(xs) >= 3:
                facet = xs[len(xs) // 2 - 1]
                cells[(facet, y)] = pal[4] if t < 0.3 else pal[3]       # facet stripe
                cells[(xs[-1], y)] = pal[1] if t < 0.65 else pal[0]     # shadow edge
    return outline(cells, w, h, pal[0])


def shard(pal):
    """Ice Queen-like frost shard pointing right: long diamond, widest near the base."""
    L, W = 12, 5
    cells = {}
    mid = (W - 1) / 2
    for x in range(L):
        t = x / (L - 1)                    # 0 base .. 1 tip
        half = 2.2 * (t / 0.3) if t < 0.3 else 2.2 * (1 - (t - 0.3) / 0.7)
        body = pal[1] if t < 0.3 else pal[2] if t < 0.65 else pal[3]
        ys = [y for y in range(W) if abs(y - mid) <= half + 0.01]
        for y in ys:
            cells[(x + 1, y + 1)] = body
        if ys:
            cells[(x + 1, ys[0] + 1)] = pal[4] if t > 0.3 else pal[3]   # lit top edge
    return outline(cells, L + 2, W + 2, pal[0])


def sparkle(r):
    """4-point star, white with alpha falloff along the rays (tinted by the draw colour)."""
    size = 2 * r + 1
    grid = [[None] * size for _ in range(size)]
    c = r
    for k in range(r + 1):
        a = int(255 * (1 - k / (r + 1)) ** 0.8)
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            grid[c + dy * k][c + dx * k] = (255, 255, 255, a)
    if r >= 2:
        for dx, dy in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
            grid[c + dy][c + dx] = (255, 255, 255, 110)
    return grid


SPIKE_WIDTHS = (4, 7, 10)
SPIKE_LENGTHS = (8, 16, 24, 36, 48, 64, 80, 96)


def main():
    for R in range(3, 12):
        save(snowflake(R, SWING), f'SwingFlake{R}')
    save(snowflake(3, THROW, branches=False), 'ThrowFlake')
    save(soft_flake(5, BUFF), 'BuffFlake')
    save(shard(SWING), 'Shard')
    # icicles: 3 widths x 7 lengths (art px); the game picks the closest and crops it while it grows
    for i, wd in enumerate(SPIKE_WIDTHS):
        for ln in SPIKE_LENGTHS:
            save(icicle(wd, ln, SWING), f'Spike{i}_{ln}')
    for i, r in enumerate((2, 4, 7)):
        save(sparkle(r), f'Sparkle{i}')
    save(snowflake(2, THROW, branches=False), 'Glint')


if __name__ == '__main__':
    main()
