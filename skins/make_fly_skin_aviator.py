# -*- coding: utf-8 -*-
"""Variant 3: "fly aviator" — 64x64 classic model.

Deliberately different from the previous two (dark chitin / charcoal suit):
warm amber fuzz, brick-red faceted compound eyes like a real housefly, a rust
leather bomber jacket with a cream shearling collar, leather flight goggles
pushed up on the forehead, khaki trousers and boots. Wings are spread wide
instead of folded flat.
"""
from skinlib import Painter

OUT = "skins/fly_skin_aviator.png"

PAL = {
    # warm fuzz (face / head)
    '1': (240, 226, 198), '2': (219, 196, 158), '3': (192, 164, 120),
    '4': (160, 130, 92), '5': (128, 100, 70), '6': (100, 76, 54),
    # brick-red compound eye
    'e': (52, 10, 10), 'E': (110, 22, 20), 'G': (160, 40, 28),
    'J': (212, 82, 46), 'K': (246, 140, 86),
    # ocelli
    'o': (120, 52, 30), 'O': (176, 88, 44),
    # mouth parts
    'm': (58, 38, 30), 'M': (104, 70, 52), 'n': (150, 108, 82),
    # leather jacket
    'a': (58, 32, 24), 'b': (92, 50, 34), 'c': (124, 72, 46),
    'd': (158, 98, 60),
    # shearling / cream
    'w': (232, 220, 196), 'W': (250, 244, 230), 'v': (196, 180, 152),
    # brass / zip
    'Z': (172, 136, 78), 'z': (120, 92, 54),
    # goggle glass
    'g': (36, 42, 50), 'h': (62, 72, 84),
    # khaki trousers
    'k': (104, 96, 72), 'l': (124, 114, 86), 'j': (84, 78, 58),
    # boots
    'x': (48, 38, 30), 'y': (74, 58, 44),
    # wings (pale, translucent look)
    'V': (226, 236, 250), 'U': (168, 200, 236), 'X': (120, 160, 214),
    # emblem (pale wing patch)
    'P': (168, 214, 228),
}

NOISY = {
    '1': ['1', '1', '2'], '2': ['2', '2', '1', '3'], '3': ['3', '3', '2', '4'],
    '4': ['4', '4', '3', '5'], '6': ['6', '6', '5'],
    'b': ['b', 'b', 'c'], 'c': ['c', 'c', 'b', 'd'], 'd': ['d', 'd', 'c'],
    'a': ['a', 'a', 'b'],
    'k': ['k', 'k', 'l'], 'l': ['l', 'l', 'l', 'k'], 'j': ['j', 'j', 'k'],
}

# ------------------------------------------------------------------ head
HEAD_FRONT = [
    "22111122",
    "eEG22GEe",
    "EGJ22JGE",
    "GGG33GGG",
    "EGG33GGE",
    "eEG33GEe",
    "6b3333b6",
    "6nMmmMn6",
]
HEAD_SIDE = [     # x=0 back ... x=7 front
    "22111122",
    "22231eEG",
    "22334EGJ",
    "22334EGG",
    "22334EGG",
    "22222eEG",
    "62222abM",
    "6222bbmn",
]
HEAD_BACK = [
    "22111122",
    "12323221",
    "21532212",
    "23532232",
    "22533352",
    "32533323",
    "66533666",
    "66666666",
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
    "66666666",
    "66666666",
    "66555566",
    "66555566",
    "66555566",
    "66555566",
    "66666666",
    "66666666",
]

# hat layer: leather flight goggles pushed up on the forehead
GOG_FRONT = [
    "mmm..mmm",
    "mggmmggm",
    "mhgmmghm",
    "mZZmmZZm",
    "........",
    "........",
    "........",
    "........",
]
GOG_SIDE = [      # thin leather strap around the skull
    "........",
    "mmmmmmmm",
    "........",
    "........",
    "........",
    "........",
    "........",
    "........",
]
GOG_BACK = [
    "........",
    "mmmmmmmm",
    "........",
    "........",
    "........",
    "........",
    "........",
    "........",
]
GOG_TOP = [
    "........",
    "........",
    "........",
    "........",
    "........",
    "........",
    "..m..m..",
    "..m..m..",
]
GOG_BOT = ["........"] * 8

# ------------------------------------------------------------------ body
BODY_FRONT = [    # bomber jacket: shearling collar, bronze zip, pockets, patch
    "wWwwwwWw",
    "wvwwwwvw",
    "cdcZcdcb",
    "cbcZcbcb",
    "cdcZcdcb",
    "cbcZcbcb",
    "cdcZaacb",
    "cbcZaacb",
    "cdcZcdcb",
    "cbcZcbcb",
    "abdZdbcb",
    "aabzzbaa",
]
BODY_SIDE = [     # x=0 back ... x=3 front
    "wWww",
    "wvvc",
    "cbbc",
    "cddc",
    "cbbc",
    "cddc",
    "cbbc",
    "cddc",
    "cbbc",
    "cddc",
    "abba",
    "aabb",
]
BODY_BACK = [     # plain leather back with a yoke seam
    "wWwwwwWw",
    "wvwwwwvw",
    "bccccccb",
    "bcbbbbcb",
    "bcaaaacb",
    "bcaaaacb",
    "bcaaaacb",
    "bcaaaacb",
    "bcaaaacb",
    "bcaaaacb",
    "abcaacba",
    "aabbbbaa",
]
BODY_TOP = [
    "cccccccc",
    "cwwwwwwc",
    "cwWWWWwc",
    "cccccccc",
]
BODY_BOT = [
    "aaaaaaaa",
    "aabbbbaa",
    "aabbbbaa",
    "aaaaaaaa",
]
BODY_OV_FRONT = [  # shoulder epaulettes + zip pull
    ".d....d.",
    "........",
    ".d....d.",
    "........",
    "........",
    "........",
    "....Z...",
    "........",
    "........",
    "........",
    "........",
    "........",
]
BODY_OV_BACK = [   # two long wings lying over the jacket back
    "........",
    "........",
    "........",
    "........",
    "XV....VX",
    "XUV..VUX",
    "XUV..VUX",
    ".XU..UX.",
    ".XU..UX.",
    "..X..X..",
    "........",
    "........",
]

BODY_OV_SIDE = [   # wing edge peeking past the flank
    "....",
    "....",
    "....",
    "....",
    "X...",
    "X...",
    "U...",
    "U...",
    "X...",
    "....",
    "....",
    "....",
]

BODY_OV_TOP = [    # clean shearling shoulders, no marks
    "........",
    "........",
    "........",
    "........",
]

# ------------------------------------------------------------------ arms
ARM_FRONT = [
    "wWww",   # shearling shoulder
    "wvvc",
    "cbbc",
    "cddc",
    "cbbc",
    "cddc",
    "cbbc",
    "cddc",
    "cbbc",
    "dvvd",   # shearling cuff
    "ZxxZ",   # brass bracelet + claw
    "ayya",
]
ARM_SIDE = [
    "wWww",
    "wvvc",
    "cbbc",
    "cddc",
    "cbbc",
    "cddc",
    "cbbc",
    "cddc",
    "cbbc",
    "dvvd",
    "ZxxZ",
    "ayya",
]
ARM_BACK = [
    "wWww",
    "wvvc",
    "cbbc",
    "cddc",
    "cbbc",
    "cddc",
    "cbbc",
    "cddc",
    "cbbc",
    "dvvd",
    "ZxxZ",
    "ayya",
]
ARM_TOP = ["wWww", "wvvc", "cbbc", "wWww"]
ARM_BOT = ["aaaa", "axxa", "ayya", "aaaa"]

ARM_OV_FRONT = [
    "....", "....", "....", "....", "....", "....",
    "....", "....", "....", "....", "....", "....",
]
ARM_OV_BACK = [   # wing tip resting on the shoulder
    "....",
    "....",
    "....",
    "....",
    "X...",
    "X...",
    "....",
    "....",
    "....",
    "....",
    "....",
    "....",
]

# ------------------------------------------------------------------ legs
LEG_FRONT = [
    "kjjk",   # belt line under the jacket
    "kllk",
    "kllk",
    "kjjl",
    "kllk",
    "kllk",
    "kjjl",
    "kllk",
    "kllk",
    "jkkj",   # trouser hem over the boot
    "axxa",   # boot
    "ayya",   # sole
]
LEG_SIDE = LEG_FRONT
LEG_BACK = LEG_FRONT
LEG_TOP = ["kjjk", "kllk", "kllk", "kjjk"]
LEG_BOT = ["aaaa", "ayya", "ayya", "aaaa"]


def build(out=OUT):
    p = Painter(PAL, NOISY, seed=808)

    p.paint('head', 'front', HEAD_FRONT)
    p.paint('head', 'right', HEAD_SIDE)
    p.paint('head', 'left', HEAD_SIDE, flip=True)
    p.paint('head', 'back', HEAD_BACK)
    p.paint('head', 'top', HEAD_TOP)
    p.paint('head', 'bottom', HEAD_BOT)
    p.paint('head', 'front', GOG_FRONT, overlay=True, noisy=False)
    p.paint('head', 'right', GOG_SIDE, overlay=True, noisy=False)
    p.paint('head', 'left', GOG_SIDE, overlay=True, noisy=False, flip=True)
    p.paint('head', 'back', GOG_BACK, overlay=True, noisy=False)
    p.paint('head', 'top', GOG_TOP, overlay=True, noisy=False)
    p.paint('head', 'bottom', GOG_BOT, overlay=True, noisy=False)

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
