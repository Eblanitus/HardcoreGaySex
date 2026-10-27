"""Aisberg: authored native-resolution pixel clusters; no resampling/antialiasing."""
from pathlib import Path
from math import sqrt
from PIL import Image, ImageDraw
ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'FrostMelee/Content/Items/Aisberg.png'
P = {
    'ink': '#101e39', 'deep': '#1c3658', 'ocean': '#234d79',
    'blue': '#286d9b', 'azure': '#369bc1', 'teal': '#51bfcf',
    'ice': '#84dae3', 'pale': '#bceff0', 'white': '#ecffff',
    'steel': '#52758e', 'silver': '#91adb7', 'grip': '#263747',
    'wrap': '#637e92', 'blackice': '#172b49',
}
im = Image.new('RGBA', (64, 64))
d = ImageDraw.Draw(im)
# Longitudinal axis from bottom-left pommel to top-right tip.
def xy(u, v):
    return (round(6 + (u + v) / sqrt(2)), round(57 + (-u + v) / sqrt(2)))
def poly(points, col):
    d.polygon([xy(*p) for p in points], fill=P[col])
def line(points, col, width=1):
    d.line([xy(*p) for p in points], fill=P[col], width=width)
# Pommel: faceted cold-steel socket and inset crystal.
poly([(-2,0),(2,-4),(7,-3),(9,0),(6,4),(2,4)], 'ink')
poly([(0,0),(3,-3),(6,-2),(7,0),(5,3),(2,2)], 'steel')
poly([(1,0),(3,-2),(6,-1),(5,1),(3,2)], 'ice')
poly([(1,0),(3,-2),(4,-1),(2,1)], 'white')
poly([(4,0),(6,-1),(5,2),(3,2)], 'blue')
# Dark handle: deliberately separate from the translucent blade.
poly([(6,-3),(19,-3),(21,0),(18,3),(6,3)], 'ink')
poly([(7,-1),(18,-1),(18,2),(7,2)], 'grip')
line([(8,-2),(10,2)], 'wrap')
line([(12,-2),(14,2)], 'silver')
line([(16,-2),(18,2)], 'wrap')
# Quillons: swept crystalline fins, not a flower-shaped guard.
poly([(16,-1),(15,-8),(18,-12),(22,-10),(23,-5),(26,-2),
      (25,3),(22,5),(21,10),(18,12),(16,8),(18,3)], 'ink')
poly([(17,-7),(19,-10),(21,-9),(21,-4),(24,-1),(21,1),(19,-2)], 'steel')
poly([(17,-7),(19,-10),(20,-9),(19,-5),(21,-2),(19,-3)], 'pale')
poly([(20,2),(24,2),(21,5),(20,9),(18,10),(18,7)], 'blue')
line([(20,4),(19,8),(18,9)], 'ice')
# Blade outline: a broad, asymmetric forged-glacier silhouette with one ice tooth.
poly([(23,-5),(29,-9),(33,-8),(35,-10),(41,-9),(49,-7),
      (58,-4),(72,0),(60,5),(48,8),(33,8),(24,5)], 'ink')
# Exterior cutting bevel on the light-facing edge.
poly([(24,-4),(29,-7),(33,-6),(36,-8),(42,-7),(50,-5),
      (60,-3),(69,0),(53,-1),(39,-3),(29,-2)], 'ice')
poly([(25,-4),(29,-6),(33,-5),(36,-7),(41,-6),(36,-4),(29,-3)], 'pale')
line([(37,-7),(43,-6),(49,-5)], 'white')
line([(52,-4),(61,-2),(68,0)], 'pale')
# Lower edge, reflected dark ocean underneath the glacier.
poly([(24,0),(29,-2),(39,-2),(53,-1),(69,0),(59,4),
      (47,6),(33,6),(25,4)], 'blue')
poly([(26,3),(33,5),(46,5),(58,3),(66,1),(54,3),(40,3),(31,1)], 'ocean')
line([(34,6),(46,6),(56,4)], 'azure')
# Large readable facets; each cluster has a structural purpose.
poly([(29,-2),(38,-4),(51,-2),(43,0),(33,1),(26,0)], 'teal')
poly([(38,-4),(47,-3),(58,-1),(48,-1),(43,0)], 'pale')
poly([(33,1),(43,0),(51,-2),(47,2),(40,4)], 'azure')
poly([(47,2),(58,-1),(67,0),(57,2),(49,4),(40,4)], 'teal')
poly([(29,-2),(32,-2),(33,1),(30,3),(26,2),(26,0)], 'azure')
# Deep trapped fracture: controlled branching, no random dithering.
line([(30,-1),(35,0),(39,-1),(43,0),(48,0),(53,1),(59,0)], 'deep')
line([(35,0),(36,3),(39,4)], 'ocean')
line([(43,0),(45,-2),(48,-2)], 'blue')
line([(53,1),(51,3)], 'blue')
# Small snow cap on the shoulder and a bright tip, not an exterior glow.
line([(29,-7),(32,-6)], 'white')
line([(35,-8),(37,-8)], 'pale')
line([(64,-1),(69,0)], 'white')
# Collar overlays blade root and anchors the construction.
poly([(21,-3),(24,-4),(27,-1),(26,3),(23,4),(21,2)], 'deep')
poly([(22,-2),(24,-3),(25,-1),(24,1),(22,1)], 'silver')
poly([(23,-2),(24,-2),(24,0),(23,0)], 'white')
line([(23,3),(25,2)], 'azure')
OUT.parent.mkdir(parents=True, exist_ok=True)
im.save(OUT)
# Large honest nearest-neighbour preview on a dark inventory-like backdrop.
bg = Image.new('RGB', (640,640), '#172330')
bg.paste(im.resize((640,640), Image.Resampling.NEAREST), (0,0), im.resize((640,640), Image.Resampling.NEAREST))
bg.save(ROOT / 'art/aisberg-preview.png')
print(f'{OUT.relative_to(ROOT)}: {im.size}, {len(im.getcolors())-1} opaque colors')
