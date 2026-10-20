# -*- coding: utf-8 -*-
"""Variant 2: "a fly in a suit" — 64x64 classic model.

Same character as fly_skin.png (silver fuzz, green compound eyes), but this
one is properly dressed: charcoal pinstripe jacket with a silver fur collar,
white shirt with a green tie (matching the eyes), brass buttons and cufflinks,
a pocket square, trousers with a crease and two-tone dress shoes.
The wings come out of two slits on the jacket back.
"""
from skinlib import Painter

OUT = "skins/fly_skin_suit.png"

PAL = {
    # suit
    'a': (26, 24, 31), 'b': (48, 45, 57), 'c': (62, 59, 72),
    'd': (92, 88, 105), 'D': (116, 112, 130),
    # shirt
    'w': (226, 230, 238), 'W': (252, 253, 255),
    # tie (green, matches the eyes)
    't': (20, 78, 40), 'T': (40, 142, 68), 'u': (76, 196, 100),
    'U': (140, 244, 158),
    # silver fuzz
    '1': (240, 243, 248), '2': (208, 212, 220), '3': (172, 176, 188),
    '4': (136, 140, 152), '5': (112, 116, 128),
    # compound eye
    'e': (6, 42, 24), 'E': (16, 104, 46), 'G': (42, 176, 84), 'J': (108, 240, 136),
    # ocelli
    'o': (140, 46, 32), 'O': (206, 84, 52),
    # mouth parts
    'm': (52, 34, 32), 'M': (98, 64, 52), 'n': (144, 102, 80),
    # claws / shoes
    'x': (232, 228, 204), 'y': (178, 172, 146),
    # wings
    'l': (236, 246, 255), 'v': (182, 214, 250), 'V': (132, 176, 236),
    # trousers (lighter than the jacket on purpose)
    's': (74, 78, 94), 'S': (98, 102, 120), 'r': (54, 57, 70),
    # leather belt
    'B': (48, 34, 30), 'k': (74, 55, 46),
    # brass
    'K': (216, 188, 126),
    # antennae
    'p': (78, 30, 138), 'q': (168, 100, 238), 'Q': (214, 162, 255),
}

NOISY = {
    '1': ['1', '1', '2'], '2': ['2', '2', '1', '3'], '3': ['3', '3', '2', '4'],
    'b': ['b', 'b', 'c'], 'c': ['c', 'c', 'b', 'd'], 'd': ['d', 'd', 'c'],
    'a': ['a', 'a', 'b'], 'D': ['D', 'D', 'd'],
}

# ------------------------------------------------------------------ head
HEAD_FRONT = [
    "22111122",
    "eEG22GEe",
    "EGG44GGE",
    "GGG44GGG",
    "EGG44GGE",
    "eEG44GEe",
    "ab4444ba",
    "anMmmMna",
]
HEAD_SIDE = [     # x=0 back ... x=7 front
    "22111122",
    "22332eEG",
    "22334EGJ",
    "22334EGG",
    "22334EGG",
    "22222eEG",
    "a2222abM",
    "a222bbmn",
]
HEAD_BACK = [
    "22111122",
    "12323221",
    "21532212",
    "23532232",
    "a2533352",
    "aa2332aa",
    "aabbbbaa",
    "aabbbbaa",
]
HEAD_TOP = [      # row 0 back ... row 7 front
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
HAT_FRONT = [
    "..Q..Q..",
    "..q..q..",
    "........",
    "........",
    "........",
    "........",
    "........",
    "........",
]
HAT_SIDE = [
    "........",
    "........",
    "........",
    "........",
    "........",
    "........",
    "........",
    "......p.",
]
HAT_TOP = [       # row 7 = front edge
    "........",
    "........",
    "........",
    "........",
    "..p..p..",
    "..p..p..",
    "..q..q..",
    "..q..q..",
]
HAT_BOT = ["........"] * 8

# ------------------------------------------------------------------ body
BODY_FRONT = [    # fur collar, white shirt V, tie, lapels, buttons
    "11111111",
    "1d2222d1",
    "dwwwwwwd",
    "bdwTTwdb",
    "bcdTTdcb",
    "bcbTTbcb",
    "cbbTTbbc",
    "cbbTTKbc",
    "cbcTTcbc",
    "cbcTTcbc",
    "abcTTcba",
    "aDdbbdDa",   # jacket hem
]
BODY_SIDE = [     # x=0 back ... x=3 front
    "1111",
    "3bb3",
    "cbcd",
    "cbwd",
    "cbTd",
    "cbTd",
    "cbTd",
    "cbTd",
    "cbTd",
    "cbTd",
    "abcT",
    "abba",
]
BODY_BACK = [     # jacket back: centre seam + vent, pinstripes
    "11111111",
    "3b2222b3",
    "bcdccdcb",
    "bcdacdcb",
    "bcdacdcb",
    "bcdacdcb",
    "bcdacdcb",
    "bcdacdcb",
    "bcdacdcb",
    "bcdacdcb",
    "abdaadba",
    "aabbbbaa",
]
BODY_TOP = [
    "11111111",
    "1wwwwww1",
    "1wwwwww1",
    "11111111",
]
BODY_BOT = [
    "aaaaaaaa",
    "aabbbbaa",
    "aabbbbaa",
    "aaaaaaaa",
]
BODY_OV_FRONT = [  # fur tufts on the shoulders + breast pocket square
    ".1....1.",
    "........",
    ".d....d.",
    "........",
    "........",
    "....WW..",
    "....W...",
    "........",
    "........",
    "........",
    "........",
    "........",
]
BODY_OV_BACK = [   # wings out of the two slits on the jacket back
    "........",
    "........",
    ".lV..Vl.",
    ".lV..Vl.",
    ".lV..Vl.",
    ".lV..Vl.",
    "..lV.Vl.",
    "..lV.Vl.",
    "..lV.Vl.",
    "...ll...",
    "........",
    "........",
]
BODY_OV_SIDE = [   # wing edge past the flank
    "....",
    "....",
    "V...",
    "V...",
    "V...",
    "V...",
    "v...",
    "v...",
    "....",
    "....",
    "....",
    "....",
]
BODY_OV_TOP = [    # fur on the shoulders
    "........",
    "..1111..",
    "..1221..",
    "........",
]

# ------------------------------------------------------------------ arms
ARM_FRONT = [
    "2112",   # fur shoulder
    "1cc1",
    "bccb",
    "cddc",
    "bccb",
    "cddc",
    "bccb",
    "cddc",
    "bccb",
    "cWWc",   # white shirt cuff
    "KxxK",   # cufflink + claw
    "ayya",
]
ARM_SIDE = [
    "2112",
    "1cc1",
    "bccb",
    "cddc",
    "bccb",
    "cddc",
    "bccb",
    "cddc",
    "bccb",
    "cWWc",
    "KxxK",
    "ayya",
]
ARM_BACK = [
    "2112",
    "1cc1",
    "bccb",
    "cddc",
    "bccb",
    "cddc",
    "bccb",
    "cddc",
    "bccb",
    "cWWc",
    "KxxK",
    "ayya",
]
ARM_TOP = ["2112", "1cc1", "bccb", "2112"]
ARM_BOT = ["aaaa", "axxa", "ayya", "aaaa"]

ARM_OV_FRONT = [  # epaulette seam
    "....", "....", ".DD.", "....", "....", "....",
    "....", "....", "....", "....", "....", "....",
]
ARM_OV_BACK = [   # wing tip past the shoulder
    "....", "....", "...V", "...V", "...V", "...v",
    "....", "....", "....", "....", "....", "....",
]

# ------------------------------------------------------------------ legs
LEG_FRONT = [
    "BkKB",   # belt with a brass buckle
    "rssr",
    "sSsr",   # vertical crease down the front of the trouser leg
    "sSsr",
    "sSsr",
    "sSsr",
    "sSsr",
    "sSsr",
    "sSsr",
    "rssr",   # trouser hem
    "axxa",   # shoe upper
    "ayya",   # sole
]
LEG_SIDE = LEG_FRONT
LEG_BACK = LEG_FRONT
LEG_TOP = ["BkkB", "rssr", "rssr", "BkkB"]
LEG_BOT = ["aaaa", "ayya", "ayya", "aaaa"]


def build(out=OUT):
    p = Painter(PAL, NOISY, seed=77)

    p.paint('head', 'front', HEAD_FRONT)
    p.paint('head', 'right', HEAD_SIDE)
    p.paint('head', 'left', HEAD_SIDE, flip=True)
    p.paint('head', 'back', HEAD_BACK)
    p.paint('head', 'top', HEAD_TOP)
    p.paint('head', 'bottom', HEAD_BOT)
    p.paint('head', 'front', HAT_FRONT, overlay=True, noisy=False)
    p.paint('head', 'right', HAT_SIDE, overlay=True, noisy=False)
    p.paint('head', 'left', HAT_SIDE, overlay=True, noisy=False, flip=True)
    p.paint('head', 'top', HAT_TOP, overlay=True, noisy=False)
    p.paint('head', 'bottom', HAT_BOT, overlay=True, noisy=False)

    p.paint('body', 'front', BODY_FRONT)
    p.paint('body', 'right', BODY_SIDE)
    p.paint('body', 'left', BODY_SIDE, flip=True)
    p.paint('body', 'back', BODY_BACK)
    p.paint('body', 'top', BODY_TOP)
    p.paint('body', 'bottom', BODY_BOT)
    p.paint('body', 'front', BODY_OV_FRONT, overlay=True, noisy=False)
    p.paint('body', 'right', BODY_OV_SIDE, overlay=True, noisy=False)
    p.paint('body', 'left', BODY_OV_SIDE, overlay=True, noisy=False, flip=True)
    p.paint('body', 'back', BODY_OV_BACK, overlay=True, noisy=False)
    p.paint('body', 'top', BODY_OV_TOP, overlay=True, noisy=False)

    p.paint('rarm', 'front', ARM_FRONT)
    p.paint('rarm', 'right', ARM_SIDE)
    p.paint('rarm', 'left', ARM_SIDE, flip=True)
    p.paint('rarm', 'back', ARM_BACK)
    p.paint('rarm', 'top', ARM_TOP)
    p.paint('rarm', 'bottom', ARM_BOT)
    p.paint('rarm', 'front', ARM_OV_FRONT, overlay=True, noisy=False)
    p.paint('rarm', 'right', ARM_OV_BACK, overlay=True, noisy=False)
    p.paint('rarm', 'back', ARM_OV_BACK, overlay=True, noisy=False)

    p.paint('larm', 'front', ARM_FRONT, flip=True)
    p.paint('larm', 'right', ARM_SIDE)
    p.paint('larm', 'left', ARM_SIDE, flip=True)
    p.paint('larm', 'back', ARM_BACK, flip=True)
    p.paint('larm', 'top', ARM_TOP)
    p.paint('larm', 'bottom', ARM_BOT)
    p.paint('larm', 'front', ARM_OV_FRONT, overlay=True, noisy=False, flip=True)
    p.paint('larm', 'left', ARM_OV_BACK, overlay=True, noisy=False, flip=True)
    p.paint('larm', 'back', ARM_OV_BACK, overlay=True, noisy=False, flip=True)

    p.paint('rleg', 'front', LEG_FRONT)
    p.paint('rleg', 'right', LEG_SIDE)
    p.paint('rleg', 'left', LEG_SIDE, flip=True)
    p.paint('rleg', 'back', LEG_BACK)
    p.paint('rleg', 'top', LEG_TOP)
    p.paint('rleg', 'bottom', LEG_BOT)

    p.paint('lleg', 'front', LEG_FRONT, flip=True)
    p.paint('lleg', 'right', LEG_SIDE)
    p.paint('lleg', 'left', LEG_SIDE, flip=True)
    p.paint('lleg', 'back', LEG_BACK, flip=True)
    p.paint('lleg', 'top', LEG_TOP)
    p.paint('lleg', 'bottom', LEG_BOT)

    return p.save(out)


if __name__ == "__main__":
    build()
    print("saved", OUT)
