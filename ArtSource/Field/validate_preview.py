"""Verify rendered frame differences; run with Blender and optional -- preview-directory."""
import bpy
import numpy as np
from pathlib import Path
import sys
import json

folder = Path(sys.argv[sys.argv.index('--')+1]) if '--' in sys.argv else Path(__file__).resolve().parent / 'Previews'
arrays = []
for phase in [0, 1]:
    image = bpy.data.images.load(str(folder / f'Field_Shadow_Phase{phase}.png'))
    pixels = np.empty(len(image.pixels), np.float32); image.pixels.foreach_get(pixels)
    arrays.append(pixels.reshape(-1, 4)[:, :3])
delta = np.max(np.abs(arrays[0] - arrays[1]), axis=1)
report = {'changed_shadow_pixels': int(np.count_nonzero(delta > .01)), 'maximum_pixel_change': float(delta.max()), 'total_pixels': len(delta)}
(folder.parent / 'shadow-image-validation.json').write_text(json.dumps(report, indent=2))
assert report['changed_shadow_pixels'] > 100, report
print('PASS: animated shadow changes in rendered Unity captures', report)
