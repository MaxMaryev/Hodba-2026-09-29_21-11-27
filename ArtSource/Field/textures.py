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
    payload=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,depth,color,0,0,0))+chunk(b'IDAT',zlib.compress(raw,6))+chunk(b'IEND',b'')
    temporary=path.with_suffix(path.suffix+'.tmp')
    temporary.write_bytes(payload)
    try:
        temporary.replace(path)
    except OSError:
        path.write_bytes(payload)
        temporary.unlink(missing_ok=True)

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

def seal(a):
    """Duplicate the first row/column at the far edge for byte-exact PNG seams."""
    a[-1] = a[0]
    a[:, -1] = a[:, 0]
    return a

def pixel_noise(n, seed):
    rng = np.random.default_rng(seed)
    core = rng.normal(0, 1, (n-1, n-1)).astype(np.float32)
    return np.pad(core, ((0,1),(0,1)), mode='wrap')

def standardized(a):
    core = a[:-1,:-1]
    return (a-float(core.mean()))/max(float(core.std()),1e-6)

def grain_mask(n, coverage, radius_px, seed):
    """Evenly spaced jittered grains on a torus, with soft circular footprints."""
    core_n=n-1
    radius=max(1,int(radius_px))
    area=np.pi*radius*radius*.72
    count=max(1,int(coverage*core_n*core_n/area))
    side=int(np.ceil(np.sqrt(count)))
    rng=np.random.default_rng(seed)
    result=np.zeros((core_n,core_n),dtype=np.float32)
    placed=0
    for gy in range(side):
        for gx in range(side):
            if placed>=count: break
            cy=int((gy+rng.uniform(.18,.82))*core_n/side)%core_n
            cx=int((gx+rng.uniform(.18,.82))*core_n/side)%core_n
            r=max(1,int(round(radius*rng.uniform(.72,1.28))))
            yy=np.arange(-r,r+1)
            xx=np.arange(-r,r+1)
            kernel=np.clip(1-(yy[:,None]**2+xx[None,:]**2)/(r*r+1e-6),0,1)
            yi=(cy+yy)%core_n
            xi=(cx+xx)%core_n
            result[np.ix_(yi,xi)]=np.maximum(result[np.ix_(yi,xi)],kernel)
            placed+=1
    return np.pad(result,((0,1),(0,1)),mode='wrap')

def centered_height(raw):
    h=.5+.19*standardized(raw)
    h=np.clip(h,.015,.985)
    h+=.5-float(h[:-1,:-1].mean())
    return seal(np.clip(h,0,1).astype(np.float32))

def tone(base, shade):
    shade=shade/max(float(shade[:-1,:-1].mean(dtype=np.float64)),1e-6)
    result=np.clip(base[None,None,:]*shade[...,None],0,1)
    target=base
    current=result[:-1,:-1].mean(axis=(0,1),dtype=np.float64)
    result=np.clip(result*(target/current)[None,None,:],0,1)
    return seal(result.astype(np.float32))

def block_deviation(luminance, pixels):
    core=luminance[:-1,:-1]
    pixels=max(1,int(pixels))
    h=(core.shape[0]//pixels)*pixels
    w=(core.shape[1]//pixels)*pixels
    blocks=core[:h,:w].reshape(h//pixels,pixels,w//pixels,pixels).mean(axis=(1,3))
    return float(np.max(np.abs(blocks/float(core.mean())-1)))

def metrics(albedo,height01,normal,tile_m,block_m):
    lum=np.sum(albedo*np.array([.2126,.7152,.0722],dtype=np.float32),axis=-1)
    core=lum[:-1,:-1]
    hh,ww=core.shape
    quadrants=[core[:hh//2,:ww//2],core[:hh//2,ww//2:],core[hh//2:,:ww//2],core[hh//2:,ww//2:]]
    mean=float(core.mean())
    return {
        'mean_srgb':[float(x*255) for x in albedo[:-1,:-1].mean(axis=(0,1),dtype=np.float64)],
        'luminance_std_ratio':float(core.std()/mean),
        'quadrant_mean_max_deviation':float(max(abs(float(q.mean())/mean-1) for q in quadrants)),
        'block_mean_max_deviation':block_deviation(lum,block_m/tile_m*(albedo.shape[0]-1)),
        'height_mean':float(height01[:-1,:-1].mean()),
        'height_albedo_correlation_abs':float(abs(np.corrcoef(core.ravel(),height01[:-1,:-1].ravel())[0,1])),
        'height_derived_from_albedo':False,
        'normal_length_error':float(np.max(np.abs(np.linalg.norm(normal,axis=-1)-1))),
    }

def build_ash(n,tile_m,seed,micro=False):
    fine_cells=520 if n==2048 else 300
    fine=.62*standardized(pixel_noise(n,seed))+.38*standardized(noise(n,fine_cells,seed+1))
    dark_radius=4 if micro else 2
    light_radius=3 if micro else 2
    dark=grain_mask(n,.005 if micro else .0075,dark_radius,seed+2)
    light=grain_mask(n,.007 if micro else .010,light_radius,seed+3)
    shade=1+.078*fine
    shade=shade*(1-dark)+(.48+.035*standardized(noise(n,310,seed+4)))*dark
    shade=shade*(1-light)+(1.20+.025*standardized(noise(n,370,seed+5)))*light
    albedo=tone(rgb('B8B1A7'),shade)
    clump_cells=50 if micro else 150
    pit=grain_mask(n,.016,2 if micro else 3,seed+20)
    height_raw=(.62*standardized(pixel_noise(n,seed+21))+
                .48*standardized(noise(n,fine_cells//2,seed+22))+
                .36*standardized(noise(n,clump_cells,seed+23))-.42*pit+
                .12*dark+.16*light)
    height=centered_height(height_raw)
    masks={'dark_coverage':float((dark[:-1,:-1]>.15).mean()),
           'light_coverage':float((light[:-1,:-1]>.15).mean())}
    return albedo,height,masks

def asymmetric_ripple(phase):
    p=phase-np.floor(phase)
    wind=np.clip(p/.70,0,1)
    lee=np.clip((1-p)/.30,0,1)
    a=wind*wind*(3-2*wind)
    b=lee*lee*(3-2*lee)
    return np.where(p<.70,a,b).astype(np.float32)

def branch_mask(primary,count,seed):
    core=primary[:-1,:-1]
    result=np.zeros_like(core)
    starts=np.argwhere(core>.62)
    rng=np.random.default_rng(seed)
    for _ in range(count):
        sy,sx=starts[rng.integers(0,len(starts))]
        length=rng.integers(int(core.shape[0]*.025),int(core.shape[0]*.085))
        angle=rng.uniform(0,2*np.pi)
        for step in range(length):
            taper=1-step/max(length-1,1)
            y=int(round(sy+np.sin(angle)*step))%core.shape[0]
            x=int(round(sx+np.cos(angle)*step))%core.shape[1]
            result[y,x]=max(result[y,x],taper)
            result[(y-1)%core.shape[0],x]=max(result[(y-1)%core.shape[0],x],taper*.6)
            result[(y+1)%core.shape[0],x]=max(result[(y+1)%core.shape[0],x],taper*.6)
            result[y,(x-1)%core.shape[1]]=max(result[y,(x-1)%core.shape[1]],taper*.6)
            result[y,(x+1)%core.shape[1]]=max(result[y,(x+1)%core.shape[1]],taper*.6)
    return np.pad(result,((0,1),(0,1)),mode='wrap')

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

def save_ground(prefix,albedo,height01,size,height_mm,report,extra=None):
    physical=height01*(height_mm/1000)
    n = normals(physical,(size,size))
    maps = {'Albedo':albedo,'Normal':n*.5+.5,'Height':height01}
    for name,a in maps.items():
        png(OUT/f'{prefix}_{name}.png',a,16 if name=='Height' else 8)
    seam = max(float(np.max(np.abs(a[0]-a[-1]))) for a in maps.values())
    seam = max(seam,max(float(np.max(np.abs(a[:,0]-a[:,-1]))) for a in maps.values()))
    material={'size_m':[size,size],'resolution':[albedo.shape[1],albedo.shape[0]],'height_range_mm':[0,height_mm],
        'actual_height_range_mm':[float(height01.min()*height_mm),float(height01.max()*height_mm)],
        'seam_max':seam,'normal_convention':'+Y / OpenGL','height_bit_depth':16}
    material.update(metrics(albedo,height01,n,size,.1))
    if extra: material.update(extra)
    report['materials'][prefix]=material
    # A swatch and explicitly separate relit diagnostic; albedo files have no lighting.
    stride=4
    light=np.array([-.35,.45,.82]); light/=np.linalg.norm(light)
    shaded=albedo*(.38+.62*np.clip(np.sum(n*light,axis=-1),0,1))[...,None]
    strip=np.concatenate([albedo[::stride,::stride],shaded[::stride,::stride],(n*.5+.5)[::stride,::stride]],axis=1)
    png(PREVIEW/f'{prefix}_Albedo_Relit_Normal.png',strip)
    tile=shaded[::stride,::stride]
    png(PREVIEW/f'{prefix}_Tiling2x2.png',np.tile(tile,(2,2,1)))
    diagnostic=np.full((816,1536,3),.09,dtype=np.float32)
    diagnostic_stride=albedo.shape[0]//256
    for index,(direction,title) in enumerate([([0,0,1],'DIAGNOSTIC / FRONTAL / 3X3'),([.985,0,.174],'DIAGNOSTIC / GRAZING / 3X3')]):
        light=np.array(direction,dtype=np.float32); light/=np.linalg.norm(light)
        relit=albedo*(.22+.78*np.clip(np.sum(n*light,axis=-1),0,1))[...,None]
        diagnostic[48:,index*768:(index+1)*768]=np.tile(relit[::diagnostic_stride,::diagnostic_stride],(3,3,1))
        label(diagnostic,title,index*768+18,14)
    png(PREVIEW/f'{prefix}_Diagnostic_3x3_Frontal_Grazing.png',diagnostic)

def generate():
    report={'seed':SEED,'generator':'Blender Python / NumPy; no external images','materials':{},'encoding':{'albedo':'sRGB','other_maps':'linear','height':'unsigned 16-bit linear; decode sample * maximum millimetres','seam_method':'periodic fields; duplicated boundary texel; wrapped central derivatives'}}
    n=2048
    albedo,h,ash_masks=build_ash(n,2,SEED)
    save_ground('T1_Ash',albedo,h,2,6,report,ash_masks)
    print('T1 ash saved',flush=True)
    x=np.linspace(0,1,n,dtype=np.float32)[None,:]
    y=np.linspace(0,1,n,dtype=np.float32)[:,None]
    # Forty nominal cycles make a 10 cm pitch; periodic warping bends and splits ridges.
    warp=.22*np.sin(2*np.pi*3*y)+.34*noise(n,9,SEED+10)+.10*noise(n,21,SEED+11)
    pitch_mod=.70*noise(n,4,SEED+12)
    phase=40*x+warp+.12*pitch_mod*np.sin(2*np.pi*2*x)
    primary=asymmetric_ripple(phase)
    split=asymmetric_ripple(phase+.52*noise(n,12,SEED+13))
    continuity=np.clip(1.15+1.6*noise(n,17,SEED+14),0,1)
    shape=(.82*primary+.18*split)*(.55+.45*continuity)
    shape=seal(shape)
    amp=.0075+.0018*noise(n,6,SEED+15)
    physical=.006+(shape-float(shape[:-1,:-1].mean()))*amp+.00022*standardized(noise(n,250,SEED+16))
    h=seal(np.clip(physical/.012,0,1).astype(np.float32))
    h+=.5-float(h[:-1,:-1].mean()); h=seal(np.clip(h,0,1))
    ripple_fine=.018*standardized(noise(n,330,SEED+17))
    albedo=tone(rgb('B8B1A7'),1+ripple_fine-.04*(shape-float(shape[:-1,:-1].mean())))
    save_ground('T2_Ripples',albedo,h,4,12,report,{'ridge_height_mm':[5.7,9.3],'nominal_pitch_cm':10,'wind_axis':'+U','crest_axis':'V'})
    print('T2 ripples saved',flush=True)
    # Periodic Voronoi plate cracks; offset coordinates add irregular fracture bends.
    wx=x+.011*noise(n,11,SEED+20)
    wy=y+.011*noise(n,12,SEED+21)
    first=np.full((n,n),10,dtype=np.float32); second=first.copy()
    rng=np.random.default_rng(SEED+22)
    sites=[]
    attempts=0
    while len(sites)<42 and attempts<20000:
        candidate=rng.random(2)
        if all(np.linalg.norm(np.minimum(np.abs(candidate-site),1-np.abs(candidate-site)))>=.04 for site in sites):
            sites.append(candidate)
        attempts+=1
    for sx,sy in sites:
        dx=np.abs(wx-sx); dx=np.minimum(dx,1-dx)
        dy=np.abs(wy-sy); dy=np.minimum(dy,1-dy)
        distance=dx*dx+dy*dy
        second=np.minimum(second,np.maximum(first,distance))
        first=np.minimum(first,distance)
    site_min=min(float(np.linalg.norm(np.minimum(np.abs(a-b),1-np.abs(a-b)))) for i,a in enumerate(sites) for b in sites[i+1:])
    boundary=np.sqrt(second)-np.sqrt(first)
    crack=np.exp(-(boundary/.00165)**2)
    branches=branch_mask(crack,34,SEED+23)
    crack=np.maximum(crack,branches)
    interruption=np.clip(1.35+1.8*noise(n,19,SEED+24),0,1)
    crack*=interruption
    lip=np.clip(np.exp(-(boundary/.0052)**2)-np.exp(-(boundary/.0024)**2),0,1)
    deposits=crack*np.clip(.3+1.5*noise(n,33,SEED+25),0,1)
    micro=.00035*standardized(noise(n,390,SEED+26))+.00012*standardized(pixel_noise(n,SEED+27))
    physical=.006+micro-.0062*crack+.0010*lip+.00045*deposits
    h=seal(np.clip(physical/.012,0,1).astype(np.float32))
    h+=.5-float(h[:-1,:-1].mean()); h=seal(np.clip(h,0,1))
    shade=1+.018*standardized(noise(n,280,SEED+28))-.025*crack+.035*deposits
    albedo=tone(rgb('8C857B'),shade)
    save_ground('T3_Crust',albedo,h,4,12,report,{'crack_width_mm':[2,6],'crack_depth_mm':[3.4,7.6],'cell_size_m':[.4,1.2],'edge_lift_mm':1.0,'t_junction_branches':34,'site_count':len(sites),'site_min_spacing_m':site_min*4})
    print('T3 crust saved',flush=True)
    albedo,h,ash_masks=build_ash(1024,.5,SEED+100,micro=True)
    save_ground('T5_AshMicro',albedo,h,.5,3,report,ash_masks)
    print('T5 ash micro saved',flush=True)
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
