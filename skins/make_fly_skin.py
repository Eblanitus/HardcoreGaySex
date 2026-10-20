# -*- coding: utf-8 -*-
"""Fly skin, 64x64, classic (Steve, 4-px arms) model.

Source-skin DNA kept:
  silver-grey hair          -> silver fuzz (crown, thorax collar, hips)
  green eyes                -> faceted compound eyes
  black outfit, purple tie  -> chitin plating with a purple stripe / wing veins
Fly anatomy: grey frons between the eyes, antennae on the hat layer, ocelli
on the crown, folded translucent wings on the jacket layer, banded abdomen,
pale clawed tarsi.
"""
import random
from PIL import Image

OUT = "skins/fly_skin.png"
SEED = 12

PAL = {
    # silver fuzz
    '1': (238, 241, 246), '2': (206, 210, 218), '3': (172, 176, 188),
    '4': (138, 142, 154), '5': (104, 108, 120),
    # chitin
    'a': (10, 8, 13), 'b': (26, 23, 32), 'c': (46, 42, 54), 'd': (74, 68, 84),
    # purple accent (the old tie)
    'p': (78, 30, 138), 'P': (134, 64, 212), 'q': (168, 100, 238),
    'Q': (206, 148, 255),
    # compound eye
    'e': (6, 42, 24), 'E': (16, 104, 46), 'G': (42, 176, 84), 'g': (28, 142, 66),
    'J': (108, 240, 136),
    # ocelli
    'o': (140, 46, 32), 'O': (206, 84, 52),
    # mouth parts / proboscis
    'm': (52, 34, 32), 'M': (98, 64, 52), 'n': (144, 102, 80),
    # claws
    'x': (232, 228, 204), 'y': (178, 172, 146),
    # wings (translucent)
    'v': (176, 208, 248), 'V': (130, 172, 232), 'w': (222, 236, 254),
    'W': (250, 253, 255), 'u': (192, 148, 250),
}

NOISY = {
    '1': ['1', '1', '2'], '2': ['2', '2', '1', '3'], '3': ['3', '3', '2', '4'],
    '4': ['4', '4', '3', '5'],
    'b': ['b', 'b', 'b', 'c'], 'c': ['c', 'c', 'c', 'b', 'd'],
    'd': ['d', 'd', 'c'], 'a': ['a', 'a', 'b'],
}
rnd = random.Random(SEED)

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


def paint(img, part, face, grid, overlay=False, noisy=True, flip=False):
    p = LAYOUT[part]
    x0, y0 = p['ov'] if overlay else p['base']
    dx, dy, w, h = p['faces'][face]
    g = [r[::-1] for r in grid] if flip else grid
    for j, row in enumerate(g):
        if j >= h:
            break
        for i, ch in enumerate(row):
            if i >= w or ch == '.':
                continue
            col = PAL[ch]
            if noisy and ch in NOISY and rnd.random() < 0.15:
                col = PAL[rnd.choice(NOISY[ch])]
            img.putpixel((x0 + dx + i, y0 + dy + j), col)


def mirror(g):
    return [r[::-1] for r in g]


# ---------------------------------------------------------------- HEAD
HEAD_FRONT = [
    "22111122",   # silver fringe
    "eEG33GEe",   # faceted compound eyes, narrow grey frons between them
    "EGG33GGE",
    "GGG44GGG",
    "EGG44GGE",
    "eEG44GEe",
    "ab4444ba",   # cheeks
    "anMmmMna",   # proboscis / mouth parts
]
HEAD_SIDE = [     # x=0 back -> x=7 front
    "22111122",
    "22222eEG",
    "22223EGJ",
    "22234EGG",
    "22224EGG",
    "22222eEG",
    "a2222abM",
    "a222bbmn",
]
HEAD_BACK = [   # fuzzy occiput with a darker parting line
    "22111122",
    "12323221",
    "21532212",
    "23532232",
    "a2533352",
    "aa2332aa",
    "aabbbbaa",
    "aabbbbaa",
]
HEAD_TOP = [      # row 0 = back ... row 7 = front; three ocelli near the front
    "22222222",
    "21333312",
    "12333321",
    "22233222",
    "22222222",
    "23222232",
    "2o2222o2",
    "22oOo222",
]
HEAD_BOT = [
    "aaaaaaaa",
    "aaaaaaaa",
    "aabbbbaa",
    "aabbbbaa",
    "aabbbbaa",
    "aabbbbaa",
    "aaaaaaaa",
    "aaaaaaaa",
]
# hat layer: antennae
HAT_FRONT = [     # antenna bases; the rest is a 1-px rim around the eyes
    "..Q..Q..",
    "..q..q..",
    "e......e",
    "E......G",
    "G......G",
    "E......G",
    "e......e",
    "........",
    "........",
]
HAT_SIDE = [      # x=0 back ... x=7 front: eye wraps around the front corners
    "........",
    "......EG",
    ".....EEG",
    ".....EGG",
    ".....eEG",
    "......eG",
    "........",
    "........",
]
HAT_TOP = [       # row 7 = front edge; antennae run back over the crown
    "........",
    "........",
    "........",
    "........",
    "..p..p..",
    "..P..P..",
    "..q..q..",
    "..q..q..",
]
HAT_BOT = ["........"] * 8

# ---------------------------------------------------------------- BODY
BODY_FRONT = [    # fuzz collar, chitin thorax, purple stripe = the old tie
    "21111112",
    "32222223",
    "bccccccb",
    "bbcddcbb",
    "bcdpPdcb",
    "bcdpPdcb",
    "bbdpPpbb",
    "bcdpPdcb",
    "bcdpPdcb",
    "bbdQQdbb",
    "abdpPdba",
    "aabppbaa",
]
BODY_SIDE = [     # x=0 back -> x=3 front
    "2112",
    "3223",
    "bccb",
    "bcdb",
    "bcdP",
    "bcdp",
    "bbdp",
    "bcdP",
    "bcdP",
    "bbdQ",
    "abdP",
    "aabb",
]
BODY_BACK = [     # banded abdomen
    "21111112",
    "32222223",
    "bccccccb",
    "babbbbab",
    "bcddddcb",
    "babbbbab",
    "bcddddcb",
    "babbbbab",
    "bcddddcb",
    "abbbbbba",
    "aabppbaa",
    "aaa..aaa".replace('.', 'a'),
]
BODY_TOP = [
    "22222222",
    "12333321",
    "12333321",
    "c222222c",
]
BODY_BOT = [
    "aaaaaaaa",
    "aabppbaa",
    "aabppbaa",
    "aaaaaaaa",
]
BODY_OV_FRONT = [  # collar plates
    "........",
    "........",
    ".d....d.",
    ".c....c.",
    ".c....c.",
    ".c....c.",
    "........",
    "........",
    "........",
    "........",
    "........",
    "........",
]
BODY_OV_BACK = [   # folded wings: two blades with a dark gap between them
    "........",
    ".W....W.",
    ".Wv..vW.",
    ".Wv..vW.",
    ".WvvvvW.",
    ".Wv..vW.",
    ".Wv..vW.",
    "..W..W..",
    "..WvvW..",
    "...WW...",
    "........",
    "........",
]
BODY_OV_SIDE = [   # wing blade seen edge-on
    "....",
    "3...",
    "W...",
    "w...",
    "w...",
    "W...",
    "3...",
    "....",
    "....",
    "....",
    "....",
    "....",
]
BODY_OV_TOP = [    # wing roots on the shoulders
    "........",
    ".uu..uu.",
    ".WW..WW.",
    "........",
]

# ---------------------------------------------------------------- ARMS
ARM_FRONT = [
    "3113",
    "1dd1",
    "bccb",
    "cbbc",
    "cbbc",
    "abba",
    "cbbc",
    "bccb",
    "cbbc",
    "abba",
    "bxxb",
    "ayya",
]
ARM_SIDE = ARM_FRONT
ARM_BACK = ARM_FRONT
ARM_TOP = ["3113", "1dd1", "bccb", "3113"]
ARM_BOT = ["aaaa", "axxa", "ayya", "aaaa"]

# spaulder on the jacket layer of the shoulder
ARM_OV_BACK = [    # spaulder + wing tip hanging over the shoulder
    "....", "....", "....", "WW..", "Wv..", "Wv..",
    "v...", "....", "....", "....", "....", "....",
]
ARM_OV_FRONT = [
    "....", "....", ".dd.", "....", "....", "....",
    "....", "....", "....", "....", "....", "....",
]

# ---------------------------------------------------------------- LEGS
LEG_FRONT = [
    "3113",
    "1dd1",
    "bccb",
    "cbbc",
    "adba",
    "cbbc",
    "bccb",
    "cbbc",
    "adba",
    "cbbc",
    "bxxb",
    "ayya",
]
LEG_SIDE = LEG_FRONT
LEG_BACK = LEG_FRONT
LEG_TOP = ["3113", "1331", "1331", "3113"]
LEG_BOT = ["aaaa", "axxa", "ayya", "aaaa"]


def build(out=OUT):
    img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))

    paint(img, 'head', 'front', HEAD_FRONT)
    paint(img, 'head', 'right', HEAD_SIDE)
    paint(img, 'head', 'left', mirror(HEAD_SIDE))
    paint(img, 'head', 'back', HEAD_BACK)
    paint(img, 'head', 'top', HEAD_TOP)
    paint(img, 'head', 'bottom', HEAD_BOT)
    paint(img, 'head', 'front', HAT_FRONT, overlay=True, noisy=False)
    paint(img, 'head', 'right', HAT_SIDE, overlay=True, noisy=False)
    paint(img, 'head', 'left', mirror(HAT_SIDE), overlay=True, noisy=False)
    paint(img, 'head', 'top', HAT_TOP, overlay=True, noisy=False)
    paint(img, 'head', 'bottom', HAT_BOT, overlay=True, noisy=False)

    paint(img, 'body', 'front', BODY_FRONT)
    paint(img, 'body', 'right', BODY_SIDE)
    paint(img, 'body', 'left', mirror(BODY_SIDE))
    paint(img, 'body', 'back', BODY_BACK)
    paint(img, 'body', 'top', BODY_TOP)
    paint(img, 'body', 'bottom', BODY_BOT)
    paint(img, 'body', 'front', BODY_OV_FRONT, overlay=True, noisy=False)
    paint(img, 'body', 'right', BODY_OV_SIDE, overlay=True, noisy=False)
    paint(img, 'body', 'left', mirror(BODY_OV_SIDE), overlay=True, noisy=False)
    paint(img, 'body', 'back', BODY_OV_BACK, overlay=True, noisy=False)
    paint(img, 'body', 'top', BODY_OV_TOP, overlay=True, noisy=False)

    paint(img, 'rarm', 'front', ARM_FRONT)
    paint(img, 'rarm', 'right', ARM_SIDE)
    paint(img, 'rarm', 'left', mirror(ARM_SIDE))
    paint(img, 'rarm', 'back', ARM_BACK)
    paint(img, 'rarm', 'top', ARM_TOP)
    paint(img, 'rarm', 'bottom', ARM_BOT)
    paint(img, 'rarm', 'right', ARM_OV_BACK, overlay=True, noisy=False)
    paint(img, 'rarm', 'front', ARM_OV_FRONT, overlay=True, noisy=False)

    paint(img, 'larm', 'front', mirror(ARM_FRONT))
    paint(img, 'larm', 'right', ARM_SIDE)
    paint(img, 'larm', 'left', mirror(ARM_SIDE))
    paint(img, 'larm', 'back', mirror(ARM_BACK))
    paint(img, 'larm', 'top', ARM_TOP)
    paint(img, 'larm', 'bottom', ARM_BOT)
    paint(img, 'larm', 'left', mirror(ARM_OV_BACK), overlay=True, noisy=False)
    paint(img, 'larm', 'front', mirror(ARM_OV_FRONT), overlay=True, noisy=False)

    paint(img, 'rleg', 'front', LEG_FRONT)
    paint(img, 'rleg', 'right', LEG_SIDE)
    paint(img, 'rleg', 'left', mirror(LEG_SIDE))
    paint(img, 'rleg', 'back', LEG_BACK)
    paint(img, 'rleg', 'top', LEG_TOP)
    paint(img, 'rleg', 'bottom', LEG_BOT)

    paint(img, 'lleg', 'front', mirror(LEG_FRONT))
    paint(img, 'lleg', 'right', LEG_SIDE)
    paint(img, 'lleg', 'left', mirror(LEG_SIDE))
    paint(img, 'lleg', 'back', mirror(LEG_BACK))
    paint(img, 'lleg', 'top', LEG_TOP)
    paint(img, 'lleg', 'bottom', LEG_BOT)

    img.save(out)
    return img


if __name__ == "__main__":
    build()
    print("saved", OUT)
