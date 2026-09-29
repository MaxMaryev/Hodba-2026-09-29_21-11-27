"""Independent acceptance checks; run with Blender --background --python this file."""
import json
import struct
import zlib
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Art/Field/Textures'

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

def main():
    report = ROOT / 'ArtSource/Field/texture-validation.json'
    assert report.exists(), 'Texture generation and validation report missing'
    data = json.loads(report.read_text())
    for prefix in ['T1_Ash', 'T2_Ripples', 'T3_Crust']:
        for suffix in ['Albedo', 'Normal', 'Height', 'AO', 'Roughness', 'MetallicSmoothness']:
            p = OUT / f'{prefix}_{suffix}.png'
            assert p.exists(), str(p)
            header = p.read_bytes()[:33]
            w, h, depth, color = struct.unpack('>IIBB', header[16:26])
            assert (w,h) == (2048,2048), (p,w,h)
            assert depth == (16 if suffix == 'Height' else 8), (p,depth)
        result = data['materials'][prefix]
        diagnostic = decode(ROOT / 'ArtSource/Field/Previews' / f'{prefix}_Diagnostic_3x3_Frontal_Grazing.png')
        assert diagnostic.shape == (816,1536,3)
        assert result['seam_max'] == 0, result
        assert result['normal_length_error'] < 1e-5, result
        decoded={suffix:decode(OUT/f'{prefix}_{suffix}.png') for suffix in ['Albedo','Normal','Height','AO','Roughness','MetallicSmoothness']}
        for suffix,a in decoded.items():
            assert np.array_equal(a[0],a[-1]), (prefix,suffix,'V seam')
            assert np.array_equal(a[:,0],a[:,-1]), (prefix,suffix,'U seam')
        normal=decoded['Normal']*2-1
        assert np.max(np.abs(np.linalg.norm(normal,axis=-1)-1))<.014
        assert np.min(normal[...,2])>0
        packed=decoded['MetallicSmoothness']
        assert np.max(packed[...,:3])==0
        assert np.max(np.abs(packed[...,3]+decoded['Roughness'][...,0]-1))<1/255+.00001
        if prefix=='T2_Ripples':
            power=np.abs(np.fft.rfft(decoded['Height'][...,0].mean(axis=0)))
            frequency=int(np.argmax(power[10:100]))+10
            pitch=4/frequency
            assert .08<=pitch<=.12, pitch
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
    print('PASS: texture dimensions, PNG bit depth, seamless edges, normal lengths, mirror and depth')

if __name__ == '__main__':
    main()
