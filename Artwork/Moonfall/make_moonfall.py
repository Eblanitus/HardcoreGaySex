"""Moonfall - a Terraria-style sword item sprite, authored pixel by pixel.

Style rules taken from vanilla item sprites (and from how vanilla sprites are
built by hand):
  * one pixel dimension, no anti-aliasing, no filters, no gradients
  * every part carries a 1px outline in a dark *version of its own colour*
  * short ramps: outline / shadow / body / light / specular
  * light from the upper-left, so the cutting edge and the top rim glow
  * flat planes, not round tubes: a blade is bevel, flat and spine
  * chunky silhouette first - it has to read at inventory size

Run:  python3 make_moonfall.py
"""

import math
from PIL import Image

W, H = 52, 58

# ---------------------------------------------------------------- materials
(NONE, STEEL, DARK, EDGE, GOLD, GRIP, GEM) = range(7)

RAMP = {
    #         outline        shadow          body            light           specular
    STEEL: [(28, 20, 50), (54, 42, 94), (100, 82, 150), (156, 142, 212), (226, 220, 255)],
    DARK:  [(15, 10, 26), (30, 19, 48), (50, 34, 76), (78, 54, 110), (116, 90, 152)],
    EDGE:  [(24, 42, 70), (56, 110, 152), (110, 190, 226), (182, 240, 255), (255, 255, 255)],
    GOLD:  [(46, 26, 11), (108, 66, 23), (170, 118, 44), (222, 178, 92), (255, 242, 205)],
    GRIP:  [(15, 10, 19), (31, 22, 36), (53, 38, 60), (79, 58, 86), (110, 86, 116)],
    GEM:   [(20, 38, 66), (50, 100, 142), (102, 178, 214), (172, 236, 255), (255, 255, 255)],
}

ROOT = (14.0, 43.0)
TIP = (46.0, 11.0)                      # exactly 45 degrees, like vanilla swords
UX, UY = 0.70710678, -0.70710678        # up the blade
VX, VY = 0.70710678, 0.70710678         # across the blade


def at(along, across):
    return (ROOT[0] + UX * along + VX * across,
            ROOT[1] + UY * along + VY * across)


class Sprite:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.mat = [[NONE] * h for _ in range(w)]
        self.idx = [[0] * h for _ in range(w)]
        self.no_outline = set()

    # ------------------------------------------------------------ painting
    def put(self, x, y, mat, idx):
        if 0 <= x < self.w and 0 <= y < self.h:
            self.mat[x][y] = mat
            self.idx[x][y] = idx

    def uv(self, x, y):
        px, py = x + 0.5 - ROOT[0], y + 0.5 - ROOT[1]
        return (px * UX + py * UY, px * VX + py * VY)

    def poly(self, pts, mat, shade):
        """Fill a polygon. shade(along, across, x, y) -> ramp index."""
        inside = set()
        n = len(pts)
        for y in range(self.h):
            py = y + 0.5
            xs = []
            for i in range(n):
                x1, y1 = pts[i]
                x2, y2 = pts[(i + 1) % n]
                if (y1 <= py < y2) or (y2 <= py < y1):
                    xs.append(x1 + (py - y1) * (x2 - x1) / (y2 - y1))
            xs.sort()
            for i in range(0, len(xs) - 1, 2):
                for x in range(self.w):
                    if xs[i] <= x + 0.5 <= xs[i + 1]:
                        inside.add((x, y))
        for (x, y) in inside:
            u, v = self.uv(x, y)
            self.mat[x][y] = mat
            self.idx[x][y] = shade(u, v, x, y)
        return inside

    def disc(self, cx, cy, r, mat, shade, squash=1.0):
        pts = []
        for i in range(40):
            a = 2 * math.pi * i / 40
            pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r * squash))
        return self.poly(pts, mat, shade)

    # ------------------------------------------------------------- render
    def render(self):
        img = Image.new("RGBA", (self.w, self.h), (0, 0, 0, 0))
        p = img.load()
        for x in range(self.w):
            for y in range(self.h):
                m = self.mat[x][y]
                if m != NONE:
                    p[x, y] = RAMP[m][max(0, min(4, self.idx[x][y]))] + (255,)
        for x in range(self.w):
            for y in range(self.h):
                if self.mat[x][y] != NONE or (x, y) in self.no_outline:
                    continue
                votes = {}
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        nx, ny = x + dx, y + dy
                        if 0 <= nx < self.w and 0 <= ny < self.h and self.mat[nx][ny] != NONE:
                            votes[self.mat[nx][ny]] = votes.get(self.mat[nx][ny], 0) + 1
                if votes:
                    p[x, y] = RAMP[max(votes, key=votes.get)][0] + (255,)
        return img


def preview(img, scale=9, bg=(22, 24, 34, 255)):
    out = Image.new("RGBA", (img.width * scale, img.height * scale), bg)
    out.alpha_composite(img.resize((img.width * scale, img.height * scale), Image.NEAREST))
    return out


# ================================================================ blade
s = Sprite(W, H)
BLADE_LEN = math.dist(ROOT, TIP)


def half_edge(u):
    return 0.5 * (10.4 - 2.0 * u ** 1.7 - 8.4 * max(0.0, (u - 0.70) / 0.30) ** 1.35)


def half_spine(u):
    return half_edge(u) * max(0.0, 1.0 - (u / 0.915) ** 12)


# silhouette: sample the outline into a polygon so the fill stays crisp
left_pts, right_pts = [], []
N = 90
U0 = 1.7 / BLADE_LEN                      # the blade starts inside the guard
for i in range(N + 1):
    u = U0 + (1.0 - U0) * i / N
    left_pts.append(at(BLADE_LEN * u, -half_edge(u)))
    right_pts.append(at(BLADE_LEN * u, +half_spine(u)))
blade_pts = left_pts + right_pts[::-1]
s.poly(blade_pts, STEEL, lambda u, v, x, y: 2)

# ---------------------------------------------------------------- planes
# Cel shading: two bold planes divided by one crisp line, a white-hot cutting
# edge, and a near-black spine -- the way vanilla metal is painted.
for y in range(H):
    for x in range(W):
        if s.mat[x][y] != STEEL:
            continue
        u, v = s.uv(x, y)
        u_n = u / BLADE_LEN
        w = half_edge(u_n) + half_spine(u_n)
        c = v + half_edge(u_n)                  # pixels from the cutting edge
        if c < 1.0 or w <= 2.2:
            s.mat[x][y], s.idx[x][y] = EDGE, 4
        elif c < 2.1:
            s.mat[x][y], s.idx[x][y] = EDGE, 3
        elif c < 3.1:
            s.idx[x][y] = 3                     # lit bevel
        elif c < w - 2.0:
            s.idx[x][y] = 2                     # the shadowed plane of the flat
        elif c < w - 1.0:
            s.idx[x][y] = 1                     # a sliver of deep shadow
        else:
            s.idx[x][y] = 1                     # spine wall
        # yokote: the crease where the point facet begins
        if BLADE_LEN * 0.882 < u < BLADE_LEN * 0.898 and c > 1.0:
            s.idx[x][y] = max(1, s.idx[x][y] - 1) if s.mat[x][y] == STEEL else s.idx[x][y]

# ---------------------------------------------------------------- the point
# The last three pixels are drawn by hand: a hairline of white-hot metal, so
# the tip reads as sharp instead of ending in a blunt square.
for y in range(H):
    for x in range(W):
        if s.mat[x][y] not in (STEEL, EDGE, DARK):
            continue
        u, v = s.uv(x, y)
        u_n = u / BLADE_LEN
        if u_n <= 0.955:
            continue
        c = v + half_edge(u_n)
        if c < 0.8:
            s.mat[x][y], s.idx[x][y] = EDGE, 4
        elif c < 1.7:
            s.mat[x][y], s.idx[x][y] = EDGE, 3
        else:
            s.mat[x][y] = NONE

# ------------------------------------------------------------ connectivity
# A single pixel that touches the body only at a corner reads as a gap, so plug
# the corner the way a hand would: fill the missing orthogonal neighbour.
def plug_corners():
    crew = [m for m in range(1, 7)]
    for _ in range(2):
        for y in range(H):
            for x in range(W):
                if s.mat[x][y] not in crew:
                    continue
                if any(0 <= x + dx < W and 0 <= y + dy < H and s.mat[x + dx][y + dy] in crew
                       for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                    continue
                for dx, dy in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
                    nx, ny = x + dx, y + dy
                    if not (0 <= nx < W and 0 <= ny < H):
                        continue
                    if s.mat[nx][ny] in crew:
                        for (ax, ay) in ((x + dx, y), (x, y + dy)):
                            if s.mat[ax][ay] == NONE:
                                s.mat[ax][ay] = s.mat[x][y]
                                s.idx[ax][ay] = s.idx[x][y]
                                break
                        break


plug_corners()

# ================================================================ hilt
# The hilt is drawn run by run in screen space, the way the vanilla sprites are
# built: the blade axis runs "/", so the guard runs "\" and both are clean
# 45-degree bars whose edge pattern shifts by exactly one pixel per row.
CHAR = {
    "A": (GOLD, 4), "a": (GOLD, 3), "b": (GOLD, 2), "c": (GOLD, 1),
    "M": (GRIP, 4), "m": (GRIP, 3), "l": (GRIP, 2), "k": (GRIP, 1),
    "X": (GEM, 4), "x": (GEM, 3), "y": (GEM, 2),
}


def run(x0, y, text):
    for i, ch in enumerate(text):
        if ch == ".":
            continue
        mat, idx = CHAR[ch]
        s.put(x0 + i, y, mat, idx)


# --- grip: 45 degree bar, lit along its upper-left face ---------------------
GRIP_PAT = "mmllllkk"
RING_PAT = "Aabbbbbc"
for y in range(44, 52):
    cx = 57 - y                              # centre of the grip on this row
    pat = RING_PAT if y in (45, 49) else GRIP_PAT
    run(cx - 4, y, pat)

# leather wrap: a lit ridge that walks one pixel per row
for y in range(44, 52):
    run((57 - y) - 2, y, "M")
    run((57 - y) + 2, y, "k")

# --- pommel: gold ball ------------------------------------------------------
pommel_rows = {
    50: (3, "Aabc"),
    51: (2, "Aabbbc"),
    52: (2, "aabbbc"),
    53: (3, "abbbc"),
    54: (4, "ab"),
}
for y, (x0, text) in pommel_rows.items():
    run(x0, y, text)
run(3, 51, "Xx")
run(3, 52, "xy")

# --- guard: "\\" bar, tips swept towards the point --------------------------
GUARD_W = [3, 4, 6, 6, 6, 6, 6, 6, 6, 6, 6, 4, 3]
GUARD_PAT = {2: "Ac", 3: "Abc", 4: "Aabc", 6: "Aabbbc", 8: "Aabbbbbc"}
for i, y in enumerate(range(35, 48)):
    w = GUARD_W[i]
    tip = 0 < i < len(GUARD_W) - 1 and w <= 6
    cx = y - 26.2 + (1.0 if tip else 0.0)    # the crescent bends towards the blade
    run(int(round(cx)) - w // 2, y, GUARD_PAT[w])

# raised boss in the middle of the guard -- reads at 1:1, unlike a 2 px gem
run(14, 41, "AA")
run(14, 42, "aa")
run(15, 42, "ab")

# ================================================================ finishing
# --- notches bitten out of the spine ---------------------------------------
for (du, dv) in ((0.42, 4.4), (0.47, 4.0), (0.72, 4.0)):
    uu = BLADE_LEN * du
    x = int(round(ROOT[0] + UX * uu + VX * dv))
    y = int(round(ROOT[1] + UY * uu + VY * dv))
    if 0 <= x < W and 0 <= y < H and s.mat[x][y] in (STEEL, DARK):
        s.mat[x][y] = NONE

# --- two dust motes catching the light --------------------------------------
def sparkle(cx, cy, arms=1):
    s.put(cx, cy, GEM, 4)
    s.no_outline.add((cx, cy))
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        s.put(cx + dx, cy + dy, GEM, 2)
        s.no_outline.add((cx + dx, cy + dy))
    if arms > 1:
        for dx, dy in ((2, 0), (-2, 0), (0, 2), (0, -2)):
            s.put(cx + dx, cy + dy, GEM, 1)
            s.no_outline.add((cx + dx, cy + dy))


# no detached particles: vanilla item icons are clean

# --- one long polish streak down the lit side of the flat ------------------
for i in range(9):
    du = 0.26 + i * 0.021
    dv = 3.5
    uu = BLADE_LEN * du
    x = int(round(ROOT[0] + UX * uu + VX * dv))
    y = int(round(ROOT[1] + UY * uu + VY * dv))
    if 0 <= x < W and 0 <= y < H and s.mat[x][y] == STEEL and s.idx[x][y] == 2:
        s.idx[x][y] = 3

if __name__ == "__main__":
    item = s.render()
    item.save("Moonfall_item.png")
    preview(item).save("Moonfall_preview.png")
    print("ok", item.size)
