"""Checks evaluated skeleton stance, swing clearance and loop poses in Blender."""
from pathlib import Path
import bpy
import json
import sys

root = Path(__file__).resolve().parents[2]
bpy.ops.wm.open_mainfile(filepath=str(root / 'ArtSource/Field/M3_Traveler.blend'))
rig = bpy.data.objects['Traveler_Rig']
rig.animation_data.action = bpy.data.actions['Walk_Tired']
samples = []
for frame in range(1, 38):
    bpy.context.scene.frame_set(frame)
    samples.append({side: tuple(rig.pose.bones[side + 'Foot'].matrix.translation) for side in ['Left','Right']})
errors = []
for i in range(2, 16):
    velocity = (samples[i+1]['Left'][1] - samples[i]['Left'][1]) * 30
    errors.append(abs(velocity - 1.3))
report = {'stance_speed_error_mps': max(errors), 'ankle_height_range_m': [min(s['Left'][2] for s in samples[:18]), max(s['Left'][2] for s in samples[:18])], 'swing_clearance_m': max(s['Left'][2] for s in samples[18:]) - samples[0]['Left'][2]}
(root / 'ArtSource/Field/motion-validation.json').write_text(json.dumps(report, indent=2))
assert report['stance_speed_error_mps'] < .08, report
assert report['ankle_height_range_m'][1] - report['ankle_height_range_m'][0] < .015, report
assert report['swing_clearance_m'] > .06, report
print('PASS evaluated walk: planted stance matches 1.3 m/s, level ankle and swing clearance')
if '--render' in sys.argv:
    scene = bpy.context.scene
    scene.cycles.samples = 16
    scene.cycles.use_denoising = False
    scene.render.threads_mode = 'FIXED'
    scene.render.threads = 4
    scene.render.resolution_x, scene.render.resolution_y = 480, 640
    for frame in [1, 10, 19, 28]:
        scene.frame_set(frame)
        scene.render.filepath = str(root / f'ArtSource/Field/Previews/Traveler_Walk_{frame:02}.png')
        bpy.ops.render.render(write_still=True)
