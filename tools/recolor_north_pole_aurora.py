"""Aurora reskin of the North Pole spear (owner 02.11.2026): the vanilla sprite's colours are
mapped by role onto the aurora palette (palettes/north-pole-aurora.gpl). The shaft runs
violet at the butt -> teal -> mint at the head, like the arc ribbon. Silhouette untouched.
Usage: python3 tools/recolor_north_pole_aurora.py SRC DST"""
import sys
from PIL import Image

H = lambda h: tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))
NIGHT, DEEP, VIOLET, BVIOLET = H('#04081e'), H('#261254'), H('#803ade'), H('#b95aff')
TEAL, BTEAL, MINT, BMINT, PALE, CORE = H('#1ca8c4'), H('#14c8dc'), H('#46e89c'), H('#3cff8c'), H('#d6ffe8'), H('#e1fff5')

# shaft/head body colours: (dark, mid, light) per band along the spear
BANDS = [(DEEP, VIOLET, BVIOLET), (DEEP, TEAL, BTEAL), (DEEP, TEAL, MINT)]
BODY = {H('#1e3c3a'): 0, H('#38726d'): 1, H('#53aaa4'): 2}
FIXED = {
    H('#292008'): NIGHT,                      # dark spike tips / outline
    H('#006793'): VIOLET, H('#095f71'): TEAL,  # gem shadows
    H('#6ad2ff'): BMINT,                      # blade glow
    H('#afd8da'): PALE, H('#b4eaed'): PALE,
    H('#e8feff'): CORE, H('#e7e3ef'): CORE,
    H('#8c8aa5'): VIOLET, H('#b5b6ce'): BVIOLET,  # metal spikes
}


def recolor(src, dst):
    im = Image.open(src).convert('RGBA')
    w, h = im.size
    px = im.load()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            c = (r, g, b)
            t = (x + (h - 1 - y)) / (w + h - 2)  # 0 at the butt (bottom-left), 1 at the tip
            band = 0 if t < 0.38 else 1 if t < 0.66 else 2
            if c in BODY:
                c = BANDS[band][BODY[c]]
            elif c in FIXED:
                c = FIXED[c]
            else:
                raise SystemExit(f'unmapped colour #{r:02x}{g:02x}{b:02x} at {x},{y}')
            px[x, y] = c + (255,)
    im.save(dst)


if __name__ == '__main__':
    recolor(sys.argv[1], sys.argv[2])
