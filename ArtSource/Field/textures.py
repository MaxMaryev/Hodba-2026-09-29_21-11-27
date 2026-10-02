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

def decode_png(path):
    raw=path.read_bytes()
    w,h,depth,color=struct.unpack('>IIBB',raw[16:26])
    payload=b''; offset=8
    while offset<len(raw):
        length=struct.unpack('>I',raw[offset:offset+4])[0]
        if raw[offset+4:offset+8]==b'IDAT': payload+=raw[offset+8:offset+8+length]
        offset+=length+12
    channels={0:1,2:3,6:4}[color]
    stride=w*channels*(depth//8)
    rows=zlib.decompress(payload)
    pixels=b''.join(rows[i*(stride+1)+1:(i+1)*(stride+1)] for i in range(h))
    return np.frombuffer(pixels,dtype='>u2' if depth==16 else np.uint8).reshape(h,w,channels).astype(np.float32)/((1<<depth)-1)

def binomial_blur(core):
    vertical=(np.roll(core,1,0)+2*core+np.roll(core,-1,0))*.25
    return (np.roll(vertical,1,1)+2*vertical+np.roll(vertical,-1,1))*.25

def one_pixel_variance(core):
    core=core.astype(np.float64)
    return float(np.var(core-binomial_blur(core))/np.var(core))

def gradient_isotropy(height):
    dx=(np.roll(height,-1,1)-np.roll(height,1,1))*.5
    dy=(np.roll(height,-1,0)-np.roll(height,1,0))*.5
    bins=np.histogram(np.arctan2(dy,dx),bins=8,range=(-np.pi,np.pi))[0]
    return float(bins.max()/bins.min())

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

def spectral_noise(n, seed, sigmas):
    """Isotropic Gaussian-filtered periodic noise without a lattice frequency."""
    m=n-1
    rng=np.random.default_rng(seed)
    spectrum=np.fft.rfft2(rng.standard_normal((m,m),dtype=np.float32))
    fy=np.fft.fftfreq(m).astype(np.float32)[:,None]
    fx=np.fft.rfftfreq(m).astype(np.float32)[None,:]
    radius2=fy*fy+fx*fx
    fields=[]
    for sigma in sigmas:
        filt=np.exp((-2*np.pi*np.pi*sigma*sigma)*radius2)
        core=np.fft.irfft2(spectrum*filt,s=(m,m)).astype(np.float32)
        fields.append(standardized(np.pad(core,((0,1),(0,1)),mode='wrap')))
    return fields

def standardized(a):
    core = a[:-1,:-1]
    return (a-float(core.mean()))/max(float(core.std()),1e-6)

def grain_mask(n, coverage, radius_px, seed):
    """Jittered angular grains; sizes follow a small-grain-heavy power law."""
    core_n=n-1
    radius=max(1,int(radius_px))
    area=np.pi*(.48*radius+.52)**2*.8
    count=max(1,int(coverage*core_n*core_n/area))
    side=int(np.ceil(np.sqrt(count)))
    rng=np.random.default_rng(seed)
    result=np.zeros((core_n,core_n),dtype=np.float32)
    for cell in rng.choice(side*side,size=count,replace=False):
        gy,gx=divmod(int(cell),side)
        cy=int((gy+rng.uniform(.18,.82))*core_n/side)%core_n
        cx=int((gx+rng.uniform(.18,.82))*core_n/side)%core_n
        r=1+(radius-1)*rng.random()**2.3
        aspect=rng.uniform(1,2.5)
        theta=rng.uniform(0,2*np.pi)
        margin=int(np.ceil(r*np.sqrt(aspect)+1))
        yy=np.arange(-margin,margin+1)
        xx=np.arange(-margin,margin+1)
        xr=xx[None,:]*np.cos(theta)+yy[:,None]*np.sin(theta)
        yr=-xx[None,:]*np.sin(theta)+yy[:,None]*np.cos(theta)
        angle=np.arctan2(yr,xr)
        jagged=1+.14*np.sin(3*angle+rng.uniform(0,2*np.pi))+.10*np.sin(5*angle+rng.uniform(0,2*np.pi))
        distance=np.sqrt((xr/(r*np.sqrt(aspect)))**2+(yr*np.sqrt(aspect)/r)**2)
        kernel=np.clip((jagged-distance)*r/.5+.5,0,1)
        if rng.random()<.35: kernel*=.5
        yi=(cy+yy)%core_n
        xi=(cx+xx)%core_n
        result[np.ix_(yi,xi)]=np.maximum(result[np.ix_(yi,xi)],kernel)
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

def coarse_free(a,tile_m,sigma_m=.04):
    """Periodic high-pass keeping the mean: structure over ~25 cm repeats with the tile and
    reads as a lattice of lines in the distance; spots at that scale come from the game's own non-tiling maps."""
    core=a[:-1,:-1].astype(np.float32)
    m=core.shape[0]
    sigma=sigma_m*m/tile_m
    fy=np.fft.fftfreq(m).astype(np.float32)[:,None]
    fx=np.fft.rfftfreq(m).astype(np.float32)[None,:]
    keep=1-np.exp((-2*np.pi*np.pi*sigma*sigma)*(fy*fy+fx*fx))
    keep[0,0]=1
    core=np.fft.irfft2(np.fft.rfft2(core)*keep,s=(m,m)).astype(np.float32)
    return np.pad(core,((0,1),(0,1)),mode='wrap')

def build_ash(n,tile_m,seed,micro=False):
    # At 1 mm/px (T1) and 0.5 mm/px (T5), the dominant grains occupy 2–20 mm.
    short,clumps=spectral_noise(n,seed,[1.8 if micro else 1.5,6 if micro else 4.5])
    long=spectral_noise(n,seed+1,[11 if micro else 7])[0]
    dark_radius=8 if micro else 4
    light_radius=7 if micro else 3
    dark=grain_mask(n,.005 if micro else .0075,dark_radius,seed+2)
    light=grain_mask(n,.007 if micro else .010,light_radius,seed+3)
    micrograin=spectral_noise(n,seed+4,[1.05])[0]
    shade=1+.024*clumps+.011*long+.066*short+.009*micrograin
    shade=shade*(1-dark)+(.48+.025*clumps)*dark
    shade=shade*(1-light)+(1.20+.015*short)*light
    if not micro: shade=coarse_free(shade,tile_m)
    albedo=tone(rgb('B8B1A7'),shade)
    hshort,hclumps,hlong=spectral_noise(n,seed+21,[2.1 if micro else 1.8,5.5 if micro else 4.5,11 if micro else 8])
    pits=np.clip((-hclumps-.8)*.55,0,1)
    height_raw=.58*hshort+.38*hclumps+.18*hlong-.25*pits+.06*dark+.07*light
    if not micro: height_raw=coarse_free(height_raw,tile_m)
    height=centered_height(height_raw)
    masks={'dark_coverage':float((dark[:-1,:-1]>.15).mean()),
           'light_coverage':float((light[:-1,:-1]>.15).mean())}
    return albedo,height,masks

def longest_true_run(rows):
    longest=0
    for row in rows:
        gaps=np.flatnonzero(~row)
        if len(gaps)>1:
            longest=max(longest,int(np.max(np.diff(gaps)-1)))
    return longest

def longest_axis_crack(crack_mask,mm_per_pixel):
    """Scan U/V ±5° while requiring the local crack tangent to agree."""
    core=crack_mask[:-1,:-1]
    gx=(np.roll(core,-1,1)-np.roll(core,1,1))*.5
    gy=(np.roll(core,-1,0)-np.roll(core,1,0))*.5
    jxx=binomial_blur(gx*gx)
    jxy=binomial_blur(gx*gy)
    jyy=binomial_blur(gy*gy)
    gradient_angle=.5*np.arctan2(2*jxy,jxx-jyy)
    tangent=gradient_angle+np.pi/2
    coherence=np.sqrt((jxx-jyy)**2+4*jxy*jxy)/(jxx+jyy+1e-8)
    binary=(core>.56)&(coherence>.55)
    m=binary.shape[0]
    rows=np.arange(m)
    longest=0
    for source in (binary&(np.abs(np.sin(tangent))<=np.sin(np.deg2rad(5))),
                   (binary&(np.abs(np.cos(tangent))<=np.sin(np.deg2rad(5)))).T):
        for slope in (0,-1/24,1/24,-1/12,1/12):
            sheared=np.empty_like(source)
            for column in range(m):
                sheared[:,column]=source[(rows+int(round(slope*column)))%m,column]
            longest=max(longest,longest_true_run(sheared))
    return longest*mm_per_pixel

def fft_grid_peak_ratio(field, frequencies=(8,11,12,280,390)):
    core=field[:-1,:-1]-float(field[:-1,:-1].mean())
    power=np.abs(np.fft.rfft2(core))**2
    axis=(power[0]+power[:,0][:power.shape[1]])*.5
    ratios=[]
    for f in frequencies:
        if f+7<len(axis):
            background=np.mean(np.r_[axis[max(1,f-10):max(2,f-5)],axis[f+6:f+11]])
            ratios.append(float(np.mean(axis[f-2:f+3])/max(background,1e-9)))
    return max(ratios)

def branch_mask(primary,count,seed):
    core=primary[:-1,:-1]
    result=np.zeros_like(core)
    starts=np.argwhere(core>.62)
    rng=np.random.default_rng(seed)
    for _ in range(count):
        sy,sx=starts[rng.integers(0,len(starts))]
        length=rng.integers(int(core.shape[0]*.025),int(core.shape[0]*.085))
        angle=rng.uniform(0,2*np.pi)
        bend_phase=rng.uniform(0,2*np.pi)
        for step in range(length):
            taper=1-step/max(length-1,1)
            bend=2.2*np.sin(2*np.pi*step/22+bend_phase)*np.sin(np.pi*step/length)
            cy=sy+np.sin(angle)*step+np.cos(angle)*bend
            cx=sx+np.cos(angle)*step-np.sin(angle)*bend
            y=int(round(cy))%core.shape[0]
            x=int(round(cx))%core.shape[1]
            for oy in range(-2,3):
                for ox in range(-2,3):
                    value=taper*np.exp(-.5*(oy*oy+ox*ox)/1.15)
                    yy=(y+oy)%core.shape[0]; xx=(x+ox)%core.shape[1]
                    result[yy,xx]=max(result[yy,xx],value)
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
    if prefix in ('T1_Ash','T5_AshMicro'):
        height_png=decode_png(OUT/f'{prefix}_Height.png')[:-1,:-1,0]
        albedo_png=decode_png(OUT/f'{prefix}_Albedo.png')[:-1,:-1]
        luminance=np.sum(albedo_png*np.array([.2126,.7152,.0722]),axis=-1)
        material['height_one_pixel_variance']=one_pixel_variance(height_png)
        material['albedo_one_pixel_variance']=one_pixel_variance(luminance)
        material['gradient_isotropy_max_min']=gradient_isotropy(height_png)
    if extra: material.update(extra)
    report['materials'][prefix]=material
    crop_start=(albedo.shape[0]-320)//2
    for name in ('Albedo','Normal'):
        source=decode_png(OUT/f'{prefix}_{name}.png')
        png(PREVIEW/f'{prefix}_{name}_Crop320_1to1.png',source[crop_start:crop_start+320,crop_start:crop_start+320])
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
        if index==1:
            png(PREVIEW/f'{prefix}_Grazing_3x3.png',np.tile(relit[::diagnostic_stride,::diagnostic_stride],(3,3,1)))
    png(PREVIEW/f'{prefix}_Diagnostic_3x3_Frontal_Grazing.png',diagnostic)

def generate():
    report={'seed':SEED,'generator':'Blender Python / NumPy; no external images','materials':{},
        'baseline_657bf8a_one_pixel_variance':{
            'T1_Ash':{'height':.33109214901924133,'albedo':.3774512895921421},
            'T5_AshMicro':{'height':.33147355914115906,'albedo':.3845536495363128}},
        'encoding':{'albedo':'sRGB','other_maps':'linear','height':'unsigned 16-bit linear; decode sample * maximum millimetres','seam_method':'periodic fields; duplicated boundary texel; wrapped central derivatives'}}
    n=2048
    albedo,h,ash_masks=build_ash(n,2,SEED)
    save_ground('T1_Ash',albedo,h,2,6,report,ash_masks)
    print('T1 ash saved',flush=True)
    x=np.linspace(0,1,n,dtype=np.float32)[None,:]
    y=np.linspace(0,1,n,dtype=np.float32)[:,None]
    # Periodic Voronoi plate cracks; offset coordinates add irregular fracture bends.
    wiggle_x=spectral_noise(n,SEED+20,[5.5])[0]
    wiggle_y=spectral_noise(n,SEED+21,[5.5])[0]
    wx=x+.011*noise(n,11,SEED+20)+.00155*wiggle_x
    wy=y+.011*noise(n,12,SEED+21)+.00155*wiggle_y
    first=np.full((n,n),10,dtype=np.float32); second=first.copy()
    rng=np.random.default_rng(SEED+22)
    sites=[]
    attempts=0
    while len(sites)<42 and attempts<20000:
        candidate=rng.random(2)
        if all(np.linalg.norm(np.minimum(np.abs(candidate-site),1-np.abs(candidate-site)))>=.04 for site in sites):
            sites.append(candidate)
        attempts+=1
    nearest=np.zeros((n,n),dtype=np.uint8)
    plate_tilt=np.zeros((n,n),dtype=np.float32)
    tilt_vectors=rng.uniform(-1,1,(len(sites),2))
    plate_tones=rng.choice([-.018,.018],size=len(sites))+rng.uniform(-.001,.001,len(sites))
    for index,(sx,sy) in enumerate(sites):
        dx=np.abs(wx-sx); dx=np.minimum(dx,1-dx)
        dy=np.abs(wy-sy); dy=np.minimum(dy,1-dy)
        distance=dx*dx+dy*dy
        second=np.minimum(second,np.maximum(first,distance))
        selected=distance<first
        signed_dx=(wx-sx+.5)%1-.5
        signed_dy=(wy-sy+.5)%1-.5
        local_tilt=.0025*(signed_dx*tilt_vectors[index,0]+signed_dy*tilt_vectors[index,1])
        plate_tilt=np.where(selected,local_tilt,plate_tilt)
        nearest=np.where(selected,index,nearest)
        first=np.minimum(first,distance)
    site_min=min(float(np.linalg.norm(np.minimum(np.abs(a-b),1-np.abs(a-b)))) for i,a in enumerate(sites) for b in sites[i+1:])
    boundary=np.sqrt(second)-np.sqrt(first)
    widthfield=np.clip(1+.45*spectral_noise(n,SEED+29,[6])[0],.45,1.7)
    crack=np.exp(-(boundary/(.00140*widthfield))**2)
    branches=branch_mask(crack,34,SEED+23)
    crack=np.maximum(crack,branches)
    interruption=np.clip(1.35+1.8*noise(n,19,SEED+24),0,1)
    crack*=interruption
    lip=np.clip(np.exp(-(boundary/.0048)**2)-np.exp(-(boundary/(.0022*widthfield))**2),0,1)
    deposits=crack*np.clip(.3+1.5*noise(n,33,SEED+25),0,1)
    plate_micro,plate_broad=spectral_noise(n,SEED+26,[1.6,5])
    micro=.00028*plate_micro+.00012*plate_broad
    physical=.006+micro+plate_tilt-.0062*crack+.0010*lip+.00045*deposits
    h=seal(np.clip(physical/.012,0,1).astype(np.float32))
    h+=.5-float(h[:-1,:-1].mean()); h=seal(np.clip(h,0,1))
    shade=1+plate_tones[nearest]+.012*plate_micro+.009*plate_broad-.025*crack+.035*deposits
    albedo=tone(rgb('8C857B'),shade)
    png(PREVIEW/'T3_Crust_CrackMask.png',seal(crack.copy()))
    png(PREVIEW/'T3_Crust_PlateNoise.png',np.clip(.5+.15*plate_micro,0,1))
    crust_extra={'crack_width_mm':[2,6],'crack_depth_mm':[3.4,7.6],'cell_size_m':[.4,1.2],
        'edge_lift_mm':1.0,'t_junction_branches':34,'site_count':len(sites),'site_min_spacing_m':site_min*4,
        'longest_axis_crack_mm':longest_axis_crack(crack,4*1000/(n-1)),
        'fft_grid_peak_ratio':fft_grid_peak_ratio(plate_micro),
        'plate_tilt_peak_mm':float(np.percentile(np.abs(plate_tilt[:-1,:-1]),99.9)*1000),
        'plate_tone_range_pct':float((np.max(plate_tones)-np.min(plate_tones))*100)}
    save_ground('T3_Crust',albedo,h,4,12,report,crust_extra)
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
