"""Generate deliberately simple, distinguishable placeholders; not final artwork."""
from pathlib import Path
from PIL import Image, ImageDraw
ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'FrostMelee/Content/Items/Prototypes'
OUT.mkdir(parents=True, exist_ok=True)
INK = '#19283d'

def canvas():
    im = Image.new('RGBA', (32, 32))
    return im, ImageDraw.Draw(im)

def save(im, name):
    im.save(OUT / f'{name}.png')

im,d=canvas()
d.ellipse((4,5,27,27), fill=INK)
d.ellipse((6,7,25,25),fill='#b6a5be')
for box in [(7,3,12,10),(19,3,24,10),(3,13,9,19),(23,13,29,19),(9,23,14,29),(18,23,23,29)]:
    d.rectangle(box,fill=INK)
    d.rectangle((box[0]+1,box[1]+1,box[2]-1,box[3]-1), fill='#e4dce7')
d.ellipse((9,10,22,23),fill='#efe6ed')
d.ellipse((13,13,18,18),fill='#625477')
save(im,'FlinxYoyo')

for name, color, pad in [('YetiYoyo','#add9e7','#5a8ba9'),('YetiPaw','#e1dfce','#797b89')]:
    im,d=canvas()
    d.polygon([(8,26),(7,13),(5,10),(6,5),(10,4),(13,8),(14,3),(18,3),(20,8),(23,5),(27,7),(27,12),(25,15),(25,27),(18,30)],fill=INK)
    d.polygon([(10,25),(9,13),(7,9),(8,6),(10,6),(13,12),(16,5),(18,5),(20,12),(24,7),(25,9),(23,15),(23,25),(18,28)],fill=color)
    d.ellipse((12,14,22,24),fill=pad)
    for x,y in [(9,8),(16,6),(23,9)]:
        d.rectangle((x,y,x+1,y+3),fill='#f5ffff')
    if name=='YetiYoyo':
        d.line((9,25,23,25),fill=INK,width=2)
        d.line((12,27,21,27),fill='#f0ffff')
    save(im,name)

im,d=canvas()
d.ellipse((3,3,28,28),fill=INK)
d.ellipse((5,5,26,26),fill='#a0c4d9')
d.ellipse((6,5,24,23),fill='#dfedf0')
d.ellipse((7,6,20,17),fill='#fcffff')
for x,y in [(10,23),(20,20),(23,12),(6,16)]:
    d.rectangle((x,y,x+2,y+1),fill='#779dbd')
save(im,'SnowballFlail')

im,d=canvas()
star=[(16,1),(20,11),(28,6),(23,15),(30,17),(21,21),(24,29),(16,24),(8,29),(11,20),(2,17),(11,13),(6,5),(13,10)]
d.polygon(star,fill=INK)
d.polygon([(16,4),(18,14),(25,9),(20,16),(27,17),(18,19),(21,26),(16,21),(10,26),(14,18),(5,17),(14,15),(10,9),(15,13)],fill='#80c7e6')
d.polygon([(16,4),(16,16),(10,9),(15,14)],fill='#e7ffff')
d.polygon([(16,16),(27,17),(18,19),(16,21)],fill='#4b88c0')
d.rectangle((14,14,18,18),fill='#eaffff')
save(im,'PolarStar')



im,d=canvas()
d.polygon([(3,6),(8,3),(27,19),(29,27),(24,29),(19,23)],fill=INK)
d.polygon([(5,7),(8,5),(25,20),(27,26),(24,26),(20,21)],fill='#83b5d4')
d.polygon([(5,7),(8,5),(22,18),(19,17)],fill='#efffff')
d.line([(21,20),(25,24)],fill='#eaffff',width=2)
save(im,'SnowBoomerang')
print('Generated 7 prototype item textures.')
