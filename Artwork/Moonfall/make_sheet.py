"""Builds the presentation sheet for the Moonfall item sprite.

Run:  python3 make_sheet.py
Out:  Moonfall_sheet.png
"""

from PIL import Image, ImageDraw, ImageFont

ITEM = "Moonfall_item.png"
OUT = "Moonfall_sheet.png"

W, H = 1120, 690
BG = (16, 17, 24)
PANEL = (25, 27, 37)
EDGE = (43, 46, 60)
INK = (228, 228, 242)
DIM = (140, 142, 166)
ACCENT = (150, 226, 255)
GOLD = (224, 180, 96)

FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
FONT_B = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
FONT_M = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf"

f_title = ImageFont.truetype(FONT_B, 38)
f_sub = ImageFont.truetype(FONT, 17)
f_h = ImageFont.truetype(FONT_B, 15)
f_body = ImageFont.truetype(FONT, 15)
f_small = ImageFont.truetype(FONT, 13)
f_mono = ImageFont.truetype(FONT_M, 12)

item = Image.open(ITEM).convert("RGBA")
sheet = Image.new("RGBA", (W, H), BG + (255,))
d = ImageDraw.Draw(sheet)


def panel(box, fill=PANEL, radius=8, outline=EDGE):
    d.rounded_rectangle(box, radius=radius, fill=fill + (255,), outline=outline + (255,))


def checker(box, cell=8):
    x0, y0, x1, y1 = box
    for y in range(y0, y1, cell):
        for x in range(x0, x1, cell):
            c = (31, 33, 43) if ((x // cell) + (y // cell)) % 2 == 0 else (25, 27, 35)
            d.rectangle([x, y, min(x + cell - 1, x1 - 1), min(y + cell - 1, y1 - 1)], fill=c + (255,))


def scaled(img, k):
    return img.resize((img.width * k, img.height * k), Image.NEAREST)


# ---------------------------------------------------------------- header
d.text((40, 26), "MOONFALL", font=f_title, fill=INK)
d.text((42, 76), "оружие в стиле Terraria 1.4.5   ·   52 × 58 px   ·   1 px = 1 px   ·   24 цвета",
       font=f_sub, fill=DIM)
d.line([40, 108, W - 40, 108], fill=EDGE, width=1)

# ---------------------------------------------------------------- hero
HERO = (40, 126, 470, 636)
panel(HERO)
checker((HERO[0] + 9, HERO[1] + 9, HERO[2] - 9, HERO[3] - 9))
big = scaled(item, 7)
sheet.alpha_composite(big, ((HERO[0] + HERO[2]) // 2 - big.width // 2,
                            (HERO[1] + HERO[3]) // 2 - big.height // 2 - 8))
d.text((HERO[0] + 16, HERO[3] - 30), "×7, nearest neighbour — без сглаживания", font=f_small, fill=DIM)

# ---------------------------------------------------------------- sizes
CX = 496
d.text((CX, 126), "РЕАЛЬНЫЕ РАЗМЕРЫ", font=f_h, fill=ACCENT)
panel((CX, 152, CX + 200, 216))
one = scaled(item, 1)
sheet.alpha_composite(one, (CX + 12, 152 + 32 - one.height // 2))
d.text((CX + 80, 160), "1×", font=f_body, fill=INK)
d.text((CX + 80, 180), "как в игре", font=f_small, fill=DIM)

panel((CX, 228, CX + 200, 372))
two = scaled(item, 2)
sheet.alpha_composite(two, (CX + 12, 228 + 72 - two.height // 2))
d.text((CX + 128, 246), "2×", font=f_body, fill=INK)
d.text((CX + 128, 266), "UI", font=f_small, fill=DIM)
d.text((CX + 128, 284), "104×116", font=f_small, fill=DIM)

# inventory slot mock
d.text((CX, 392), "В СЛОТЕ ИНВЕНТАРЯ", font=f_h, fill=ACCENT)
panel((CX, 418, CX + 200, 528))
slot = (CX + 26, 434, CX + 26 + 88, 434 + 88)
d.rounded_rectangle(slot, radius=4, fill=(38, 60, 104, 255), outline=(96, 134, 192, 255))
d.rounded_rectangle([slot[0] + 3, slot[1] + 3, slot[2] - 3, slot[3] - 3], radius=2,
                    outline=(58, 88, 142, 255))
sheet.alpha_composite(scaled(item, 2), (slot[0] + 44 - two.width // 2,
                                        slot[1] + 44 - two.height // 2))
d.text((CX + 126, 448), "ячейка 44 px,", font=f_small, fill=DIM)
d.text((CX + 126, 466), "иконка выходит", font=f_small, fill=DIM)
d.text((CX + 126, 484), "за границы —", font=f_small, fill=DIM)
d.text((CX + 126, 502), "как в ваниле", font=f_small, fill=DIM)

# ---------------------------------------------------------------- notes
NX = 716
panel((NX, 126, W - 40, 636))
d.text((NX + 22, 146), "КАК ЭТО НАРИСОВАНО", font=f_h, fill=GOLD)

f_body = ImageFont.truetype(FONT, 14)
PARAS = [
    ("Пиксель", "один размер, без сглаживания и фильтров."),
    ("Обводка", "1 px, затемнённый оттенок самой детали."),
    ("Свет", "сверху-слева — кромка раскалена, обух холодный."),
    ("Пять ступеней", "на материал: тень, тело, свет, блик."),
    ("Клинок", "плоские планы, а не труба: кромка, фаска, плоскость, обух."),
    ("Остриё", "дорисовано руками — три пикселя тонкой стали."),
    ("Гарда и рукоять", "полосы под 45°, выложенные построчно."),
]

ty = 178
for head, rest in PARAS:
    d.text((NX + 22, ty), head + " — ", font=f_body, fill=INK)
    xoff = d.textlength(head + " — ", font=f_body)
    words, lines, cur = rest.split(" "), [], ""
    for w_ in words:
        test = (cur + " " + w_).strip()
        if d.textlength(test, font=f_body) + xoff <= 320:
            cur = test
        else:
            lines.append(cur)
            cur = w_
    if cur:
        lines.append(cur)
    for i, ln in enumerate(lines):
        d.text((NX + 22 + (xoff if i == 0 else 0), ty), ln, font=f_body, fill=DIM)
        ty += 20
    ty += 8

# palette
d.line([NX + 22, 528, W - 62, 528], fill=EDGE, width=1)
d.text((NX + 22, 540), "ПАЛИТРА", font=f_h, fill=ACCENT)
swatches = [("outline", (46, 30, 60)), ("shadow", (54, 42, 94)), ("body", (100, 82, 150)),
            ("light", (156, 142, 212)), ("spec", (226, 220, 255)), ("edge", (182, 240, 255)),
            ("gold", (222, 178, 92)), ("gold dark", (108, 66, 23)),
            ("grip", (53, 38, 60)), ("gem", (172, 236, 255))]
f_hex = ImageFont.truetype(FONT_M, 11)
for i, (name, (r, g, b)) in enumerate(swatches):
    col, row = i % 5, i // 5
    px = NX + 22 + col * 66
    py = 562 + row * 40
    d.rectangle([px, py, px + 24, py + 16], fill=(r, g, b, 255), outline=(58, 61, 78, 255))
    d.text((px, py + 21), f"#{r:02x}{g:02x}{b:02x}", font=f_hex, fill=DIM)

sheet.convert("RGB").save(OUT)
print("saved", OUT)
