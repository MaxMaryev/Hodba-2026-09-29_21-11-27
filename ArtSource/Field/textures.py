"""Deterministic physically scaled texture authoring. Blender 4.5 / NumPy.

PNG files use a deliberately duplicated periodic boundary texel (2047 intervals).
Height is linear metres remapped to the ranges in texture-validation.json.
Tangent normal convention is OpenGL/+Y; PNG rows descend while UV V ascends.
"""
import json
import struct
import zlib
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Art/Field/Textures'
PREVIEW = ROOT / 'ArtSource/Field/Previews'
SEED = 29092026

def png(path, a, depth=8):
    a = np.clip(a, 0, 1)
    a = np.rint(a * ((1 << depth)-1)).astype('>u2' if depth == 16 else np.uint8)
    h,w = a.shape[:2]
    channels = 1 if a.ndim == 2 else a.shape[2]
    color = {1:0, 3:2, 4:6}[channels]
    def chunk(kind, payload):
        return struct.pack('>I',len(payload))+kind+payload+struct.pack('>I',zlib.crc32(kind+payload)&0xffffffff)
    raw = b''.join(b'\0'+row.tobytes() for row in a)
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,depth,color,0,0,0))+chunk(b'IDAT',zlib.compress(raw,6))+chunk(b'IEND',b''))

def noise(n, cells, seed):
    rng = np.random.default_rng(seed)
    grid = rng.uniform(-1,1,(cells,cells)).astype(np.float32)
    t = np.linspace(0,cells,n,dtype=np.float32)
    i = np.floor(t).astype(int) % cells
    f = t-np.floor(t)
    f = f*f*(3-2*f)
    a = grid[i[:,None],i[None,:]]
    b = grid[i[:,None],(i[None,:]+1)%cells]
    c = grid[(i[:,None]+1)%cells,i[None,:]]
    d = grid[(i[:,None]+1)%cells,(i[None,:]+1)%cells]
    result = (a*(1-f[None,:])+b*f[None,:])*(1-f[:,None])+(c*(1-f[None,:])+d*f[None,:])*f[:,None]
    return result

def normals(h, size, periodic=True):
    if periodic:
        core = h[:-1,:-1]
        dx = (np.roll(core,-1,1)-np.roll(core,1,1))/(2*size[0]/(h.shape[1]-1))
        dy = (np.roll(core,-1,0)-np.roll(core,1,0))/(2*size[1]/(h.shape[0]-1))
        dx = np.pad(dx,((0,1),(0,1)),mode='wrap')
        dy = np.pad(dy,((0,1),(0,1)),mode='wrap')
    else:
        dy,dx = np.gradient(h,size[1]/(h.shape[0]-1),size[0]/(h.shape[1]-1))
    n = np.stack([-dx,dy,np.ones_like(h)],axis=-1)
    n /= np.linalg.norm(n,axis=-1,keepdims=True)
    return n

def rgb(hexcolor):
    return np.array([int(hexcolor[i:i+2],16)/255 for i in (0,2,4)],dtype=np.float32)

def label(canvas, text, x, y):
    # Tiny embedded bitmap alphabet avoids a platform font/Pillow dependency.
    glyphs = {
        'A':'01110 10001 10001 11111 10001 10001 10001', 'C':'01111 10000 10000 10000 10000 10000 01111',
        'D':'11110 10001 10001 10001 10001 10001 11110', 'F':'11111 10000 10000 11110 10000 10000 10000',
        'G':'01111 10000 10000 10111 10001 10001 01110', 'I':'11111 00100 00100 00100 00100 00100 11111',
        'L':'10000 10000 10000 10000 10000 10000 11111', 'N':'10001 11001 11001 10101 10011 10011 10001',
        'O':'01110 10001 10001 10001 10001 10001 01110', 'R':'11110 10001 10001 11110 10100 10010 10001',
        'S':'01111 10000 10000 01110 00001 00001 11110', 'T':'11111 00100 00100 00100 00100 00100 00100',
        'Z':'11111 00001 00010 00100 01000 10000 11111', '3':'11110 00001 00001 01110 00001 00001 11110',
        'X':'10001 10001 01010 00100 01010 10001 10001', '/':'00001 00001 00010 00100 01000 10000 10000'}
    for char in text:
        if char in glyphs:
            for row,bits in enumerate(glyphs[char].split()):
                for col,bit in enumerate(bits):
                    if bit=='1': canvas[y+row*3:y+row*3+3,x+col*3:x+col*3+3]=.92
        x+=18

def save_material(prefix,h,base,variation,rough,ao,size,physical_range,report):
    n = normals(h,(size,size))
    albedo = np.clip(base[None,None,:]*(1+variation[...,None]),0,1)
    maps = {'Albedo':albedo,'Normal':n*.5+.5,'Height':(h-physical_range[0])/(physical_range[1]-physical_range[0]),'AO':ao,'Roughness':rough,
            'MetallicSmoothness':np.stack([np.zeros_like(h)]*3+[1-rough],axis=-1)}
    for name,a in maps.items():
        png(OUT/f'{prefix}_{name}.png',a,16 if name=='Height' else 8)
    seam = max(float(np.max(np.abs(a[0]-a[-1]))) for a in maps.values())
    seam = max(seam,max(float(np.max(np.abs(a[:,0]-a[:,-1]))) for a in maps.values()))
    report['materials'][prefix] = {'size_m':[size,size],'resolution':[2048,2048],'height_range_m':physical_range,'actual_height_range_m':[float(h.min()),float(h.max())],
        'seam_max':seam,'normal_length_error':float(np.max(np.abs(np.linalg.norm(n,axis=-1)-1))), 'normal_convention':'+Y / OpenGL', 'height_bit_depth':16}
    # A swatch and explicitly separate relit diagnostic; albedo files have no lighting.
    stride=4
    light=np.array([-.35,.45,.82]); light/=np.linalg.norm(light)
    shaded=albedo*(.38+.62*np.clip(np.sum(n*light,axis=-1),0,1))[...,None]*ao[...,None]
    strip=np.concatenate([albedo[::stride,::stride],shaded[::stride,::stride],(n*.5+.5)[::stride,::stride]],axis=1)
    png(PREVIEW/f'{prefix}_Albedo_Relit_Normal.png',strip)
    tile=shaded[::stride,::stride]
    png(PREVIEW/f'{prefix}_Tiling2x2.png',np.tile(tile,(2,2,1)))
    diagnostic=np.full((816,1536,3),.09,dtype=np.float32)
    for index,(direction,title) in enumerate([([0,0,1],'DIAGNOSTIC / FRONTAL / 3X3'),([.985,0,.174],'DIAGNOSTIC / GRAZING / 3X3')]):
        light=np.array(direction,dtype=np.float32); light/=np.linalg.norm(light)
        relit=albedo*(.22+.78*np.clip(np.sum(n*light,axis=-1),0,1))[...,None]*ao[...,None]
        diagnostic[48:,index*768:(index+1)*768]=np.tile(relit[::8,::8],(3,3,1))
        label(diagnostic,title,index*768+18,14)
    png(PREVIEW/f'{prefix}_Diagnostic_3x3_Frontal_Grazing.png',diagnostic)

def generate():
    report={'seed':SEED,'generator':'Blender Python / NumPy; no external images','materials':{},'encoding':{'albedo':'sRGB','other_maps':'linear','height':'unsigned 16-bit linear; decode min + sample*(max-min)','metallic_smoothness':'R=metallic=0, G=0, B=0, A=1-roughness','seam_method':'periodic fields; duplicated boundary texel; wrapped central derivatives'}}
    n=2048
    broad=noise(n,7,SEED)
    medium=noise(n,43,SEED+1)
    fine=noise(n,260,SEED+2)
    dust=noise(n,790,SEED+3)
    h=.0009*broad+.0005*medium+.00023*fine+.00009*dust
    save_material('T1_Ash',h,rgb('B8B0A4'),.035*broad+.022*medium+.025*fine+.012*dust,np.clip(.91+.025*medium+.02*fine,0,1),np.clip(.98+.018*fine+.012*dust,0,1),2,[-.002,.002],report)
    print('T1 ash saved',flush=True)
    x=np.linspace(0,1,n,dtype=np.float32)[None,:]
    y=np.linspace(0,1,n,dtype=np.float32)[:,None]
    # 40 ridges / 4 m = 10 cm. Integer frequencies guarantee periodicity.
    warp=.11*np.sin(2*np.pi*3*y)+.15*noise(n,9,SEED+10)
    phase=2*np.pi*(40*x+warp)
    ridge=(.5+.5*np.cos(phase))**2
    # Local attenuation plus secondary phase creates eroded interruptions and merges.
    broken=np.clip((noise(n,18,SEED+11)+.42)*2.3,0,1)
    secondary=(.5+.5*np.cos(phase+1.1*noise(n,13,SEED+12)))**3
    shape=(.78*ridge+.22*secondary)*(.24+.76*broken)
    shape[-1]=shape[0]; shape[:,-1]=shape[:,0]
    h=.018*shape+.0012*medium+.00055*fine+.00017*dust
    # Explicit endpoint identity eliminates float32 sin(2*pi) rounding differences.
    h[-1]=h[0]; h[:,-1]=h[:,0]
    v=.035*broad+.02*medium+.025*fine
    save_material('T2_Ripples',h,rgb('B8B0A4'),v,np.clip(.9+.03*medium+.02*fine,0,1),np.clip(.86+.12*shape+.015*fine,0,1),4,[-.003,.024],report)
    print('T2 ripples saved',flush=True)
    # Periodic Voronoi plate cracks; offset coordinates add irregular fracture bends.
    wx=x+.011*noise(n,11,SEED+20)
    wy=y+.011*noise(n,12,SEED+21)
    first=np.full((n,n),10,dtype=np.float32); second=first.copy()
    rng=np.random.default_rng(SEED+22)
    for row in range(8):
        for col in range(8):
            sx=(col+rng.uniform(.16,.84))/8; sy=(row+rng.uniform(.16,.84))/8
            dx=np.abs(wx-sx); dx=np.minimum(dx,1-dx)
            dy=np.abs(wy-sy); dy=np.minimum(dy,1-dy)
            distance=dx*dx+dy*dy
            second=np.minimum(second,np.maximum(first,distance))
            first=np.minimum(first,distance)
    boundary=np.sqrt(second)-np.sqrt(first)
    crack=np.exp(-(boundary/.00165)**2)
    lip=np.exp(-(boundary/.005)**2)-crack
    deposits=np.clip((noise(n,24,SEED+24)-.06)*2,0,1)
    h=.0012*broad+.0008*medium+.00025*fine-.009*crack+.0018*lip+.001*deposits
    h[-1]=h[0]; h[:,-1]=h[:,0]
    v=.065*medium+.03*fine+.035*broad-.1*crack+.10*deposits
    rough=np.clip(.84+.045*medium+.035*fine+.06*deposits,0,1)
    ao=np.clip(.98-.51*crack-.06*lip+.01*fine,0,1)
    for a in [v,rough,ao]: a[-1]=a[0]; a[:,-1]=a[:,0]
    save_material('T3_Crust',h,rgb('8A837A'),v,rough,ao,4,[-.013,.006],report)
    print('T3 crust saved',flush=True)
    # Physical boot envelope 14 x 32 cm; boot itself ~11 x 28 cm.
    yy,xx=np.mgrid[-1:1:512j,-1:1:256j].astype(np.float32)
    toe=((xx+.07)/.72)**2+((yy+.40)/.50)**2
    heel=((xx-.08)/.53)**2+((yy-.53)/.32)**2
    waist=((xx-.07)/.47)**2+((yy-.03)/.62)**2
    sdf=np.minimum(np.minimum(toe,heel),waist)-1
    inside=np.clip(-sdf/.12,0,1)
    # Slightly asymmetric medial arch keeps left/right readable.
    arch=np.exp(-((xx-.47)/.25)**2-((yy-.04)/.26)**2)
    inside*=1-.73*arch
    tread=(np.sin(yy*42+np.abs(xx)*8)>.15).astype(np.float32)
    center_channel=np.exp(-(xx/.075)**2)
    depth=inside*(.014+.006*tread)*(1-.13*center_channel)
    depth*=.02/depth.max()
    rim=.0042*np.exp(-(sdf/.15)**2)*(sdf>0)
    ejecta=.0016*np.exp(-((sdf-.27)/.2)**2)*(sdf>0)*( .5+.5*np.sin(43*xx+17*yy)*np.sin(37*yy))
    height=-depth+rim+ejecta
    alpha=np.maximum(inside,np.exp(-(np.maximum(sdf,0)/.40)**2))
    alpha*=np.clip((.98-np.maximum(abs(xx),abs(yy)))/.08,0,1)
    height*=alpha
    normal=normals(height,(.14,.32),False)
    color=np.clip(rgb('A49B8E')[None,None,:]*(1-.04*inside[...,None]),0,1)
    left=np.concatenate([color,alpha[...,None]],axis=-1)
    right_normal=normal[:,::-1].copy(); right_normal[...,0]*=-1
    for side,c,h,nm in [('Left',left,height,normal),('Right',left[:,::-1],height[:,::-1],right_normal)]:
        png(OUT/f'T4_Footprint{side}_Albedo.png',c)
        png(OUT/f'T4_Footprint{side}_Height.png',(h+.02)/.026,16)
        png(OUT/f'T4_Footprint{side}_Normal.png',nm*.5+.5)
    report['footprint_depth_m']=float(-height.min())
    report['footprint_mirror_error']=float(np.max(np.abs(right_normal-normals(height[:,::-1],(.14,.32),False))))
    report['footprint']={'size_m':[.14,.32],'height_range_m':[-.02,.006],'resolution':[256,512],'seamless':False,'alpha':'albedo alpha; height and normal use same mask','right':'mirrored left; tangent normal X inverted','raised_rim_m':float(height.max())}
    light=np.array([-.35,.45,.82]);light/=np.linalg.norm(light)
    bootshade=color*(.4+.6*np.clip(np.sum(normal*light,axis=-1),0,1))[...,None]
    back=np.full_like(color,.34)
    preview=bootshade*alpha[...,None]+back*(1-alpha[...,None])
    png(PREVIEW/'T4_Footprints.png',np.concatenate([preview,preview[:,::-1],normal*.5+.5,right_normal*.5+.5],axis=1))
    (ROOT/'ArtSource/Field/texture-validation.json').write_text(json.dumps(report,indent=2)+'\n')
    print('Texture generation complete',flush=True)

if __name__=='__main__':
    generate()
