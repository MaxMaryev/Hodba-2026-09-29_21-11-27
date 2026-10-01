"""Independent acceptance checks; run with Blender --background --python this file."""
import json
import struct
import zlib
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Art/Field/Textures'
MATERIALS = {
    'T1_Ash': ((2048, 2048), [184, 177, 167], 6),
    'T2_Ripples': ((2048, 2048), [184, 177, 167], 12),
    'T3_Crust': ((2048, 2048), [140, 133, 123], 12),
    'T5_AshMicro': ((1024, 1024), [184, 177, 167], 3),
}

def decode(path):
    raw=path.read_bytes()
    w,h,depth,color=struct.unpack('>IIBB',raw[16:26])
    offset=8; payload=b''
    while offset<len(raw):
        length=struct.unpack('>I',raw[offset:offset+4])[0]
        kind=raw[offset+4:offset+8]
        if kind==b'IDAT': payload+=raw[offset+8:offset+8+length]
        offset+=12+length
    channels={0:1,2:3,6:4}[color]
    stride=w*channels*(depth//8)
    rows=zlib.decompress(payload)
    assert len(rows)==h*(stride+1)
    assert all(rows[i*(stride+1)]==0 for i in range(h)), 'Expected PNG filter 0'
    pixels=b''.join(rows[i*(stride+1)+1:(i+1)*(stride+1)] for i in range(h))
    a=np.frombuffer(pixels,dtype='>u2' if depth==16 else np.uint8).reshape(h,w,channels)
    return a.astype(np.float32)/((1<<depth)-1)

def binomial_blur(core):
    vertical=(np.roll(core,1,0)+2*core+np.roll(core,-1,0))*.25
    return (np.roll(vertical,1,1)+2*vertical+np.roll(vertical,-1,1))*.25

def one_pixel_variance(core):
    core=np.asarray(core,dtype=np.float64)
    return float(np.var(core-binomial_blur(core))/np.var(core))

def gradient_isotropy(height):
    core=height[:-1,:-1,0]
    dx=(np.roll(core,-1,1)-np.roll(core,1,1))*.5
    dy=(np.roll(core,-1,0)-np.roll(core,1,0))*.5
    angle=np.arctan2(dy,dx)
    bins=np.histogram(angle,bins=8,range=(-np.pi,np.pi))[0]
    return float(bins.max()/bins.min())

def main():
    report = ROOT / 'ArtSource/Field/texture-validation.json'
    assert report.exists(), 'Texture generation and validation report missing'
    data = json.loads(report.read_text())
    for prefix, (resolution, target_rgb, height_mm) in MATERIALS.items():
        for suffix in ['Albedo', 'Normal', 'Height']:
            p = OUT / f'{prefix}_{suffix}.png'
            assert p.exists(), str(p)
            header = p.read_bytes()[:33]
            w, h, depth, color = struct.unpack('>IIBB', header[16:26])
            assert (w,h) == resolution, (p,w,h)
            assert depth == (16 if suffix == 'Height' else 8), (p,depth)
            assert color == (0 if suffix == 'Height' else 2), (p,color)
        result = data['materials'][prefix]
        diagnostic = decode(ROOT / 'ArtSource/Field/Previews' / f'{prefix}_Diagnostic_3x3_Frontal_Grazing.png')
        assert diagnostic.shape == (816,1536,3)
        grazing = decode(ROOT / 'ArtSource/Field/Previews' / f'{prefix}_Grazing_3x3.png')
        assert grazing.shape == (768,768,3)
        assert result['seam_max'] == 0, result
        assert result['normal_length_error'] < 1e-5, result
        decoded={suffix:decode(OUT/f'{prefix}_{suffix}.png') for suffix in ['Albedo','Normal','Height']}
        crop_start=(resolution[0]-320)//2
        for suffix in ('Albedo','Normal'):
            crop=decode(ROOT/'ArtSource/Field/Previews'/f'{prefix}_{suffix}_Crop320_1to1.png')
            assert crop.shape==(320,320,3)
            assert np.array_equal(crop,decoded[suffix][crop_start:crop_start+320,crop_start:crop_start+320])
        for suffix,a in decoded.items():
            assert np.array_equal(a[0],a[-1]), (prefix,suffix,'V seam')
            assert np.array_equal(a[:,0],a[:,-1]), (prefix,suffix,'U seam')
        normal=decoded['Normal']*2-1
        assert np.max(np.abs(np.linalg.norm(normal,axis=-1)-1))<.014
        assert np.min(normal[...,2])>0
        albedo8 = np.rint(decoded['Albedo'] * 255)
        mean_rgb = albedo8.mean(axis=(0,1), dtype=np.float64)
        assert np.max(np.abs(mean_rgb - target_rgb)) <= 4, (prefix, mean_rgb)
        assert result['height_range_mm'] == [0, height_mm], result
        assert abs(float(decoded['Height'][...,0].mean()) - .5) <= .06, (prefix, result)
        assert result['quadrant_mean_max_deviation'] <= .02, result
        assert result['height_derived_from_albedo'] is False, result
        if prefix in ('T1_Ash', 'T5_AshMicro'):
            luminance = np.sum(albedo8 * np.array([.2126, .7152, .0722]), axis=-1)
            ratio = float(luminance.std() / luminance.mean())
            assert .06 <= ratio <= .10, (prefix, ratio)
            assert result['block_mean_max_deviation'] <= .03, result
            hcore=decoded['Height'][:-1,:-1,0]
            lcore=luminance[:-1,:-1]
            hratio=one_pixel_variance(hcore)
            lratio=one_pixel_variance(lcore)
            assert abs(result['height_one_pixel_variance']-hratio)<.0002, (prefix,result,hratio)
            assert abs(result['albedo_one_pixel_variance']-lratio)<.0002, (prefix,result,lratio)
            assert hratio<=.10, (prefix,hratio)
            if prefix=='T1_Ash':
                assert lratio<=.25, (prefix,lratio)
                isotropy=gradient_isotropy(decoded['Height'])
                assert abs(result['gradient_isotropy_max_min']-isotropy)<.002, (prefix,isotropy)
                assert isotropy<=1.15, (prefix,isotropy)
        if prefix=='T2_Ripples':
            power=np.abs(np.fft.rfft(decoded['Height'][...,0].mean(axis=0)))
            frequency=int(np.argmax(power[10:100]))+10
            pitch=4/frequency
            assert .08<=pitch<=.12, pitch
            assert result['ridge_height_mm'][0] >= 5
            assert result['ridge_height_mm'][1] <= 10
            assert .5<=result['meander_wavelength_m']<=1.0, result
            assert .01<=result['meander_amplitude_m']<=.03, result
            assert 5<=result['branch_count_per_m2']<=25, result
            assert .20<=result['crest_height_modulation_fraction']<=.40, result
            from textures import ripple_metrics
            markers=decode(ROOT/'ArtSource/Field/Previews/T2_Ripples_BranchMarkers.png')[...,0]
            observed=ripple_metrics(decoded['Height'][...,0],int(np.count_nonzero(markers[:-1,:-1])))
            for key in ('meander_wavelength_m','meander_amplitude_m','branch_count_per_m2','crest_height_modulation_fraction'):
                assert abs(observed[key]-result[key])<.002, (key,observed[key],result[key])
        if prefix == 'T3_Crust':
            assert 30 <= result['site_count'] <= 60
            assert result['site_min_spacing_m'] >= .16
            assert result['crack_width_mm'][0] >= 2
            assert result['crack_width_mm'][1] <= 6
            assert result['crack_depth_mm'][0] >= 3
            assert result['crack_depth_mm'][1] <= 8
            assert result['longest_axis_crack_mm'] <= 50, result
            assert result['fft_grid_peak_ratio'] <= 2.0, result
            assert .35 <= result['plate_tilt_peak_mm'] <= .55, result
            assert 2 <= result['plate_tone_range_pct'] <= 4, result
            from textures import longest_axis_crack, fft_grid_peak_ratio
            cracks=decode(ROOT/'ArtSource/Field/Previews/T3_Crust_CrackMask.png')[...,0]
            plate_noise=decode(ROOT/'ArtSource/Field/Previews/T3_Crust_PlateNoise.png')[...,0]
            assert abs(longest_axis_crack(cracks,4000/2047)-result['longest_axis_crack_mm'])<2
            assert abs(fft_grid_peak_ratio(plate_noise)-result['fft_grid_peak_ratio'])<.02
    for side in ['Left', 'Right']:
        for suffix in ['Albedo', 'Height', 'Normal']:
            p = OUT / f'T4_Footprint{side}_{suffix}.png'
            w,h,depth,color = struct.unpack('>IIBB', p.read_bytes()[16:26])
            assert (w,h) == (256,512)
            assert depth == (16 if suffix == 'Height' else 8)
    assert data['footprint_mirror_error'] < 1e-6
    assert abs(data['footprint_depth_m'] - .02) < 1e-6
    left=decode(OUT/'T4_FootprintLeft_Normal.png')
    right=decode(OUT/'T4_FootprintRight_Normal.png')
    expected=left[:,::-1].copy(); expected[...,0]=1-expected[...,0]
    assert np.max(np.abs(right-expected))<1/255+.00001
    for suffix in ['Albedo','Height']:
        assert np.array_equal(decode(OUT/f'T4_FootprintLeft_{suffix}.png')[:,::-1],decode(OUT/f'T4_FootprintRight_{suffix}.png'))
    print('PASS: T1/T2/T3/T5 dimensions, color statistics, 1px variance, isotropy, height, seams, normals, ripples, cracks, FFT, crops and 3x3')

if __name__ == '__main__':
    main()
