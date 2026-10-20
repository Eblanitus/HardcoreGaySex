# -*- coding: utf-8 -*-
"""Flat preview of a 64x64 skin: front / back / right / left."""
import sys
from PIL import Image, ImageDraw

F = {
 'head': {'top':(8,0,8,8),'bottom':(16,0,8,8),'right':(0,8,8,8),'front':(8,8,8,8),'left':(16,8,8,8),'back':(24,8,8,8)},
 'body': {'top':(20,16,8,4),'bottom':(28,16,8,4),'right':(16,20,4,12),'front':(20,20,8,12),'left':(28,20,4,12),'back':(32,20,8,12)},
 'rarm': {'top':(44,16,4,4),'bottom':(48,16,4,4),'right':(40,20,4,12),'front':(44,20,4,12),'left':(48,20,4,12),'back':(52,20,4,12)},
 'larm': {'top':(36,48,4,4),'bottom':(40,48,4,4),'right':(32,52,4,12),'front':(36,52,4,12),'left':(40,52,4,12),'back':(44,52,4,12)},
 'rleg': {'top':(4,16,4,4),'bottom':(8,16,4,4),'right':(0,20,4,12),'front':(4,20,4,12),'left':(8,20,4,12),'back':(12,20,4,12)},
 'lleg': {'top':(20,48,4,4),'bottom':(24,48,4,4),'right':(16,52,4,12),'front':(20,52,4,12),'left':(24,52,4,12),'back':(28,52,4,12)},
}
OV = {'head':(32,0),'body':(16,32),'rarm':(40,32),'larm':(48,48),'rleg':(0,32),'lleg':(0,48)}

def crop(im, part, face, ov=False):
    x,y,w,h = F[part][face]
    if ov:
        dx,dy = OV[part]; x+=dx; y+=dy
    return im.crop((x,y,x+w,y+h))

def view(im, which):
    c = Image.new('RGBA',(16,32),(0,0,0,0))
    def put(part, face, x, y, ov=False, flip=False):
        img = crop(im, part, face, ov)
        if flip: img = img.transpose(Image.FLIP_LEFT_RIGHT)
        c.alpha_composite(img,(x,y))
    if which in ('front',):
        for ov in (False,True):
            put('head','front',4,0,ov)
        for ov in (False,True):
            put('body','front',4,8,ov)
        for ov in (False,True):
            put('rarm','front',0,8,ov); put('larm','front',12,8,ov)
        for ov in (False,True):
            put('rleg','front',4,20,ov); put('lleg','front',8,20,ov)
    elif which=='back':
        for ov in (False,True):
            put('head','back',4,0,ov); put('body','back',4,8,ov)
        for ov in (False,True):
            put('larm','back',0,8,ov); put('rarm','back',12,8,ov)
            put('rleg','back',8,20,ov); put('lleg','back',4,20,ov)
    elif which=='right':
        for ov in (False,True):
            put('head','right',4,0,ov); put('body','right',4,8,ov)
            put('rarm','right',4,8,ov); put('rleg','right',4,20,ov)
            put('lleg','right',8,20,ov)
    elif which=='left':
        for ov in (False,True):
            put('head','left',4,0,ov); put('body','left',4,8,ov)
            put('larm','left',4,8,ov); put('rleg','left',4,20,ov)
            put('lleg','left',4,20,ov)
    return c

def sheet(path, out, scale=12, bg=(122,126,136,255)):
    im = Image.open(path).convert('RGBA')
    names = ('front','back','right','left')
    imgs = [view(im,n) for n in names]
    gap = 3
    W = sum(i.width for i in imgs)+gap*(len(imgs)-1)
    H = max(i.height for i in imgs)
    c = Image.new('RGBA',(W,H),bg)
    x=0
    for i in imgs:
        c.alpha_composite(i,(x,0)); x+=i.width+gap
    c = c.resize((c.width*scale,c.height*scale),Image.NEAREST)
    c.save(out); print(out,c.size)

def closeup(path, out, scale=24):
    im = Image.open(path).convert('RGBA')
    c = Image.new('RGBA',(8,8),(122,126,136,255))
    for ov in (False,True):
        c.alpha_composite(crop(im,'head','front',ov),(0,0))
    c = c.resize((8*scale,8*scale),Image.NEAREST)
    c.save(out); print(out,c.size)

if __name__=='__main__':
    p = sys.argv[1] if len(sys.argv)>1 else 'skins/fly_skin.png'
    sheet(p, 'skins/fly_preview.png')
    closeup(p, 'skins/fly_head.png')
