# -*- coding: utf-8 -*-
"""Tiny orthographic Minecraft-skin renderer (classic 64x64 model)."""
import math, sys
from PIL import Image, ImageDraw, ImageFilter

SS = 4  # supersampling

FACES = {
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

# box: (cx, cy, cz), (hx, hy, hz) in skin pixels; y is up, feet at 0
BOXES = [
    ('head', (0, 28, 0), (4, 4, 4), 0.5),
    ('body', (0, 18, 0), (4, 6, 2), 0.0),
    ('larm', (6, 18, 0), (2, 6, 2), 0.0),
    ('rarm', (-6, 18, 0), (2, 6, 2), 0.0),
    ('lleg', (2, 6, 0), (2, 6, 2), 0.0),
    ('rleg', (-2, 6, 0), (2, 6, 2), 0.0),
]


def tex_rect(im, part, face, overlay):
    dx, dy, w, h = FACES[part]['faces'][face]
    if overlay:
        ox, oy = FACES[part]['ov']   # absolute origin of the outer layer
        return im.crop((ox + dx, oy + dy, ox + dx + w, oy + dy + h))
    bx, by = FACES[part]['base']
    return im.crop((bx + dx, by + dy, bx + dx + w, by + dy + h))


def quad_points(center, half, face, grow=0.0):
    cx, cy, cz = center
    hx, hy, hz = (half[0] + grow, half[1] + grow, half[2] + grow)
    L, R = cx - hx, cx + hx
    B, T = cy - hy, cy + hy
    N, F = cz - hz, cz + hz
    if face == 'front':   # +z, u -> +x, v -> -y
        return [(L, T, F), (R, T, F), (R, B, F), (L, B, F)]
    if face == 'back':    # -z, u -> -x
        return [(R, T, N), (L, T, N), (L, B, N), (R, B, N)]
    if face == 'right':   # -x face (texture 'right': u=0 back -> u=7 front)
        return [(L, T, N), (L, T, F), (L, B, F), (L, B, N)]
    if face == 'left':    # +x face (texture 'left': u=0 front -> u=7 back)
        return [(R, T, F), (R, T, N), (R, B, N), (R, B, F)]
    if face == 'top':     # +y, u -> +x, v -> +z
        return [(L, T, N), (R, T, N), (R, T, F), (L, T, F)]
    if face == 'bottom':  # -y, u -> +x, v -> -z
        return [(L, B, F), (R, B, F), (R, B, N), (L, B, N)]
    raise ValueError(face)


def render(skin_path, yaw_deg, pitch_deg, out, size=420, bg=(216, 222, 232), shadow=True):
    im = Image.open(skin_path).convert('RGBA')
    W = H = size * SS
    canvas = Image.new('RGBA', (W, H), bg + (255,))
    dr = ImageDraw.Draw(canvas)

    yaw, pitch = math.radians(yaw_deg), math.radians(pitch_deg)
    dirv = (math.sin(yaw) * math.cos(pitch), -math.sin(pitch), math.cos(yaw) * math.cos(pitch))
    Fv = tuple(-c for c in dirv)
    # right = normalize(cross(F, up))
    up0 = (0, 1, 0)
    rx = Fv[1] * up0[2] - Fv[2] * up0[1]
    ry = Fv[2] * up0[0] - Fv[0] * up0[2]
    rz = Fv[0] * up0[1] - Fv[1] * up0[0]
    rl = math.hypot(rx, ry, rz) or 1
    R = (rx / rl, ry / rl, rz / rl)
    U = (R[1] * Fv[2] - R[2] * Fv[1], R[2] * Fv[0] - R[0] * Fv[2], R[0] * Fv[1] - R[1] * Fv[0])

    target = (0.0, 16.0, 0.0)
    scale = size * SS / 42.0

    def project(p):
        q = (p[0] - target[0], p[1] - target[1], p[2] - target[2])
        sx = (q[0] * R[0] + q[1] * R[1] + q[2] * R[2]) * scale + W / 2
        sy = -(q[0] * U[0] + q[1] * U[1] + q[2] * U[2]) * scale + H / 2
        d = q[0] * Fv[0] + q[1] * Fv[1] + q[2] * Fv[2]
        return sx, sy, d

    if shadow:
        # soft ellipse on the "ground"
        sh = Image.new('RGBA', (W, H), (0, 0, 0, 0))
        sd = ImageDraw.Draw(sh)
        g = project((0, 0, 0))
        rx_s = 13 * scale * 0.62
        ry_s = 5 * scale * 0.62
        sd.ellipse([g[0] - rx_s, g[1] - ry_s * 0.55, g[0] + rx_s, g[1] + ry_s * 0.55],
                   fill=(40, 44, 60, 80))
        sh = sh.filter(ImageFilter.GaussianBlur(scale * 0.5))
        canvas.alpha_composite(sh)

    SHADE = {'front': 1.0, 'back': 0.82, 'right': 0.88, 'left': 0.88,
             'top': 1.10, 'bottom': 0.62}
    quads = []
    for part, center, half, grow in BOXES:
        for face in FACES[part]['faces']:
            pts = quad_points(center, half, face)
            sp = [project(p) for p in pts]
            depth = sum(p[2] for p in sp) / 4.0
            quads.append((depth, part, face, sp, False))
            if part in ('head', 'body', 'rarm', 'larm', 'rleg', 'lleg'):
                hov = 0.5 if part in ('head', 'body') else 0.25
                pts2 = quad_points(center, half, face, grow=hov)
                sp2 = [project(p) for p in pts2]
                depth2 = sum(p[2] for p in sp2) / 4.0
                quads.append((depth2, part, face, sp2, True))
    quads.sort(key=lambda q: -q[0])

    for depth, part, face, sp, overlay in quads:
        tile = tex_rect(im, part, face, overlay)
        tw, th = tile.size
        if tile.getextrema()[3][1] == 0 and overlay:
            continue
        # affine map from texture uv -> screen
        # corners order: (0,0) (1,0) (1,1) (0,1)
        x0, y0 = sp[0][0], sp[0][1]
        x1, y1 = sp[1][0], sp[1][1]
        x3, y3 = sp[3][0], sp[3][1]
        xs = x1 - x0, x3 - x0
        ys = y1 - y0, y3 - y0
        minx = max(0, int(min(p[0] for p in sp)) - 1)
        maxx = min(W - 1, int(max(p[0] for p in sp)) + 1)
        miny = max(0, int(min(p[1] for p in sp)) - 1)
        maxy = min(H - 1, int(max(p[1] for p in sp)) + 1)
        if maxx <= minx or maxy <= miny:
            continue
        # det
        det = xs[0] * ys[1] - xs[1] * ys[0]
        if abs(det) < 1e-9:
            continue
        px = canvas.load()
        tpx = tile.load()
        for sy in range(miny, maxy + 1):
            for sx in range(minx, maxx + 1):
                vx = sx + 0.5 - x0
                vy = sy + 0.5 - y0
                a = (vx * ys[1] - vy * xs[1]) / det
                b = (xs[0] * vy - ys[0] * vx) / det
                if a < 0 or a > 1 or b < 0 or b > 1:
                    continue
                tx = min(tw - 1, int(a * tw))
                ty = min(th - 1, int(b * th))
                col = tpx[tx, ty]
                if col[3] == 0:
                    continue
                if col[3] == 255:
                    sh = SHADE[face]
                    px[sx, sy] = (min(255, int(col[0] * sh)), min(255, int(col[1] * sh)),
                                  min(255, int(col[2] * sh)), 255)
                else:
                    dst = px[sx, sy]
                    al = col[3] / 255.0
                    px[sx, sy] = (int(col[0] * al + dst[0] * (1 - al)),
                                  int(col[1] * al + dst[1] * (1 - al)),
                                  int(col[2] * al + dst[2] * (1 - al)), 255)
    out_im = canvas.resize((size, size), Image.LANCZOS)
    out_im.convert('RGB').save(out)
    return out_im


def contact_sheet(skin, out, size=300):
    views = [(18, 6), (150, 6), (90, 4), (0, 0)]
    ims = []
    for i, (y, p) in enumerate(views):
        tmp = f'/tmp/_r{i}.png'
        render(skin, y, p, tmp, size=size)
        ims.append(Image.open(tmp))
    W = size * len(ims)
    c = Image.new('RGB', (W, size), (216, 222, 232))
    for i, im in enumerate(ims):
        c.paste(im, (i * size, 0))
    c.save(out)
    print(out, c.size)
    return c


if __name__ == '__main__':
    src = sys.argv[1] if len(sys.argv) > 1 else 'skins/fly_skin.png'
    contact_sheet(src, sys.argv[2] if len(sys.argv) > 2 else 'skins/fly_3d.png')
