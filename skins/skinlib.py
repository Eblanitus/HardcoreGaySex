# -*- coding: utf-8 -*-
"""Shared helpers for building 64x64 Minecraft skins (classic model)."""
import random
from PIL import Image

LAYOUT = {
    'head': {'base': (0, 0), 'ov': (32, 0), 'faces': {
        'front':  (8, 8, 8, 8),  'back': (24, 8, 8, 8),
        'right':  (0, 8, 8, 8),  'left': (16, 8, 8, 8),
        'top':    (8, 0, 8, 8),  'bottom': (16, 0, 8, 8)}},
    'body': {'base': (16, 16), 'ov': (16, 32), 'faces': {
        'front':  (4, 4, 8, 12), 'back': (16, 4, 8, 12),
        'right':  (0, 4, 4, 12), 'left': (12, 4, 4, 12),
        'top':    (4, 0, 8, 4),  'bottom': (12, 0, 8, 4)}},
    'rarm': {'base': (40, 16), 'ov': (40, 32), 'faces': {
        'front':  (4, 4, 4, 12), 'back': (12, 4, 4, 12),
        'right':  (0, 4, 4, 12), 'left': (8, 4, 4, 12),
        'top':    (4, 0, 4, 4),  'bottom': (8, 0, 4, 4)}},
    'larm': {'base': (32, 48), 'ov': (48, 48), 'faces': {
        'front':  (4, 4, 4, 12), 'back': (12, 4, 4, 12),
        'right':  (0, 4, 4, 12), 'left': (8, 4, 4, 12),
        'top':    (4, 0, 4, 4),  'bottom': (8, 0, 4, 4)}},
    'rleg': {'base': (0, 16), 'ov': (0, 32), 'faces': {
        'front':  (4, 4, 4, 12), 'back': (12, 4, 4, 12),
        'right':  (0, 4, 4, 12), 'left': (8, 4, 4, 12),
        'top':    (4, 0, 4, 4),  'bottom': (8, 0, 4, 4)}},
    'lleg': {'base': (16, 48), 'ov': (0, 48), 'faces': {
        'front':  (4, 4, 4, 12), 'back': (12, 4, 4, 12),
        'right':  (0, 4, 4, 12), 'left': (8, 4, 4, 12),
        'top':    (4, 0, 4, 4),  'bottom': (8, 0, 4, 4)}},
}


def mirror(g):
    return [r[::-1] for r in g]


class Painter:
    """Paints character grids into a skin image, with palette + dithering."""

    def __init__(self, palette, noisy=None, seed=0):
        self.pal = palette
        self.noisy = noisy or {}
        self.rnd = random.Random(seed)
        self.img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))

    def _check(self, part, face, grid):
        w = LAYOUT[part]['faces'][face][2]
        h = LAYOUT[part]['faces'][face][3]
        if len(grid) != h:
            raise ValueError(f"{part}.{face}: {len(grid)} rows, expected {h}")
        for j, row in enumerate(grid):
            if len(row) != w:
                raise ValueError(f"{part}.{face} row {j}: {len(row)} px, "
                                 f"expected {w} ({row!r})")

    def paint(self, part, face, grid, overlay=False, noisy=True, flip=False):
        self._check(part, face, grid)
        p = LAYOUT[part]
        x0, y0 = p['ov'] if overlay else p['base']
        dx, dy, w, h = p['faces'][face]
        g = mirror(grid) if flip else grid
        for j, row in enumerate(g):
            for i, ch in enumerate(row):
                if ch == '.':
                    continue
                col = self.pal[ch]
                if noisy and ch in self.noisy and self.rnd.random() < 0.12:
                    col = self.pal[self.rnd.choice(self.noisy[ch])]
                self.img.putpixel((x0 + dx + i, y0 + dy + j), col)

    def save(self, path):
        self.img.save(path)
        return self.img
