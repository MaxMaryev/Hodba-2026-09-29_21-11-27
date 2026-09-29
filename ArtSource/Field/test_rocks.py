"""Acceptance checks on delivered geometry, not just generator functions."""
import json
import numpy as np
from pathlib import Path
from test_textures import decode

ROOT = Path(__file__).resolve().parents[2]
report_path = ROOT / 'ArtSource/Field/rock-validation.json'
assert report_path.exists(), 'Missing generated rock validation report'
report = json.loads(report_path.read_text())
assert len(report['models']) == 11
for model in report['models']:
    assert model['passed'], model
    assert model['fbx_roundtrip_passed'], model
    assert model['boundary_edges'] == 0, model
    assert model['nonmanifold_edges'] == 0, model
    assert model['degenerate_faces'] == 0, model
    assert model['min_triangle_area'] > 0, model
    assert model['signed_volume'] > 0, model
    assert abs(model['max_dimension_m'] - model['target_m']) < 0.0001, model
    assert abs(model['base_z']) < 0.000001, model
    assert model['uv_in_atlas_tile'], model
    assert (ROOT / model['fbx']).exists(), model
for name in ('RocksSmall', 'Boulders'):
    for channel in ('Albedo', 'Normal', 'AO', 'Roughness', 'MetallicSmoothness'):
        assert (ROOT / f'Assets/Art/Field/Textures/{name}_{channel}.png').exists()
        pixels = decode(ROOT / f'Assets/Art/Field/Textures/{name}_{channel}.png')
        assert pixels.shape[:2] == (1024, 1024)
        cols, rows = (4, 2) if name == 'RocksSmall' else (2, 2)
        w, h = 1024 // cols, 1024 // rows
        for y in range(rows):
            for x in range(cols):
                tile = pixels[y*h:(y+1)*h, x*w:(x+1)*w]
                assert np.array_equal(tile[:, 0], tile[:, 8]), (name, channel, 'left gutter')
                assert np.array_equal(tile[:, -1], tile[:, -9]), (name, channel, 'right gutter')
                assert np.array_equal(tile[0], tile[8]), (name, channel, 'top gutter')
                assert np.array_equal(tile[-1], tile[-9]), (name, channel, 'bottom gutter')
print('PASS: 11 rock models, closed meshes, bounds, UVs, exported FBX and PBR maps')
