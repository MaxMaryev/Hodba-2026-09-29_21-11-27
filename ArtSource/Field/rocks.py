"""Deterministic low-poly volcanic rocks. Run inside Blender 4.5 LTS.

Meshes are constructed in metres, Z up in authoring; FBX converts to Y up.
The boulder ash apron faces Blender -Y / Unity +Z. No lighting is baked.
"""
import bpy
import bmesh
import numpy as np
import json
import math
import struct
import zlib
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'ArtSource/Field'
OUT = ROOT / 'Assets/Art/Field'
SEED = 29092026
NAMES = ['Flat', 'Angular', 'Rounded', 'Split', 'Elongated', 'Wedge', 'Squat', 'Asymmetric']
SIZES = [.05, .08, .12, .16, .22, .28, .34, .40]
for folder in [OUT / 'Models', OUT / 'Textures', SOURCE / 'Previews']:
    folder.mkdir(parents=True, exist_ok=True)


def png(path, array):
    a = np.ascontiguousarray(np.clip(array, 0, 255).astype(np.uint8))
    h, w = a.shape[:2]
    channels = a.shape[2] if a.ndim == 3 else 1
    kind = {1: 0, 3: 2, 4: 6}[channels]
    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff)
    payload = b''.join(b'\x00' + row.tobytes() for row in a)
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, kind, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(payload, 6)) + chunk(b'IEND', b''))


def atlas(name, count, columns, rows, big=False):
    """Independent padded UV cells; features remain in register across PBR maps."""
    s = 1024
    albedo = np.zeros((s, s, 3), np.float32)
    normal = np.zeros((s, s, 3), np.float32)
    roughness = np.zeros((s, s), np.float32)
    ao = np.zeros((s, s), np.float32)
    for tile in range(columns * rows):
        w, h = s // columns, s // rows
        # Pixels outside .06-.94 are edge extension, preserving mip gutters.
        u, v = np.meshgrid(np.clip((np.arange(w) / (w - 1) - .06) / .88, 0, 1), np.clip((np.arange(h) / (h - 1) - .06) / .88, 0, 1))
        rng = np.random.default_rng(SEED + tile + (1000 if big else 0))
        grain = rng.random((h, w)).astype(np.float32)
        wave = (np.sin(2 * np.pi * (u * 13 + .17 * np.sin(v * 31))) + np.sin(2 * np.pi * (u * 29 - v * 5))) * .18
        pores = np.zeros_like(u)
        for _ in range(110 if big else 55):
            cx, cy = rng.random(2)
            dx = np.minimum(abs(u - cx), 1 - abs(u - cx))
            radius = rng.uniform(.003, .015)
            pores += np.exp(-((dx / radius) ** 2 + ((v - cy) / (radius * 1.1)) ** 2) * 2)
        pores = np.clip(pores, 0, 1)
        micro = .28 * wave + .14 * (grain - .5) - .7 * pores
        wind_side = (1 - np.sin(u * 2 * np.pi)) * .5
        if big:
            snowline = .12 + .51 * wind_side ** 2 + .025 * np.sin(u * 44)
            ash = np.clip((snowline - v) * 18, 0, 1)
            ash = np.maximum(ash, np.clip((v - .88) * 7, 0, .5))
        else:
            ash = np.clip((v - .60 + .045 * np.sin(u * 32) + wave * .04) * 4, 0, .68) * (.45 + .55 * grain)
        stone = np.stack([46 + wave * 13, 44 + wave * 12, 42 + wave * 11], axis=-1)
        stone += (grain[..., None] - .5) * 13
        # Chemical colour variation only; cavity occlusion is a separate map.
        stone += pores[..., None] * np.array([2, 1, 0])
        ashcolor = np.array([184, 176, 164]) + (grain[..., None] - .5) * 12
        color = stone * (1 - ash[..., None]) + ashcolor * ash[..., None]
        if not big:
            color *= (.82 + .18 * np.clip(v * 4, 0, 1))[..., None]
        bump = micro * (1 - ash * .88) + grain * ash * .05
        dy, dx = np.gradient(bump)
        n = np.stack([-dx * 2.2, -dy * 2.2, np.ones_like(dx)], axis=-1)
        n /= np.linalg.norm(n, axis=-1)[..., None]
        # PNG rows run top to bottom, while Blender UV v increases upward.
        r, c = tile // columns, tile % columns
        sl = (slice(s - (r + 1) * h, s - r * h), slice(c * w, (c + 1) * w))
        def gutter(image):
            # Extend actual edge texels into the reserved 6% gutters.
            # Match spherical U seam and collapse the top pole to one colour.
            x0, x1 = math.ceil(.06 * (w - 1)), math.floor(.94 * (w - 1))
            y0, y1 = math.ceil(.06 * (h - 1)), math.floor(.94 * (h - 1))
            image[:, x0] = image[:, x1] = (image[:, x0] + image[:, x1]) * .5
            image[y1] = image[y1].mean(axis=0)
            return image[np.clip(np.arange(h), y0, y1)[:, None], np.clip(np.arange(w), x0, x1)[None, :]][::-1]
        albedo[sl] = gutter(color)
        normal[sl] = gutter((n * .5 + .5) * 255)
        roughness[sl] = gutter(np.clip(.88 + grain * .07 + ash * .035, 0, 1) * 255)
        ao[sl] = gutter((1 - pores * (1 - ash) * .33) * 255)
    packed = np.zeros((s, s, 4), np.float32)
    packed[..., 3] = 255 - roughness
    for suffix, a in [('Albedo', albedo), ('Normal', normal), ('Roughness', roughness), ('AO', ao), ('MetallicSmoothness', packed)]:
        png(OUT / 'Textures' / f'{name}_{suffix}.png', a)
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Roughness'].default_value = .94
    for suffix, slot in [('Albedo', 'Base Color'), ('Roughness', 'Roughness'), ('Normal', 'Normal')]:
        tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
        tex.image = bpy.data.images.load(str(OUT / 'Textures' / f'{name}_{suffix}.png'), check_existing=True)
        tex.image.colorspace_settings.name = 'sRGB' if suffix == 'Albedo' else 'Non-Color'
        if suffix == 'Normal':
            node = mat.node_tree.nodes.new('ShaderNodeNormalMap')
            mat.node_tree.links.new(tex.outputs['Color'], node.inputs['Color'])
            mat.node_tree.links.new(node.outputs['Normal'], bsdf.inputs[slot])
        else:
            mat.node_tree.links.new(tex.outputs['Color'], bsdf.inputs[slot])
    return mat


def rock(index, target, mat, big=False):
    seg, rings = (24, 17) if big else (16, 7)
    rx, ry, height = [(1, .85, .45), (.92, .8, .95), (1, .91, .9), (1, .78, .83), (1, .37, .55), (1, .72, .65), (1, .91, .38), (1, .72, .81)][index % 8]
    if big:
        rx, ry, height = [(1, .84, 1.05), (.88, .9, 1.45), (1.15, .82, .85)][index]
    verts = []
    for k in range(rings):
        t = k / rings
        profile = math.sqrt(max(0, 1 - ((t - .28) / .75) ** 2))
        for j in range(seg):
            a = 2 * math.pi * j / seg
            irregular = 1 + .10 * math.sin(a * 3 + index * .81) + .05 * math.cos(a * 5 - t * 3 + index)
            if index in (1, 3, 5):
                irregular += .07 * math.cos(a * 4)
            x = rx * math.cos(a) * profile * irregular + .12 * t * math.sin(index + t * 2)
            y = ry * math.sin(a) * profile * irregular
            z = height * t * (1 + .08 * math.sin(a * 2 + index) * math.sin(math.pi * t))
            if index == 3 and not big and x > .15:
                x = .15 + (x - .15) * .10  # broad fracture plane, not a second disconnected piece
            if index == 5 and not big:
                z *= .65 + .35 * (x / rx + 1) * .5
            if big:
                # The low windward apron merges into the closed mesh.
                apron = max(0, -math.sin(a)) ** 4 * max(0, 1 - t / .6)
                y -= .50 * apron
                x *= 1 + .08 * apron
            verts.append((x, y, z))
    verts += [(0, 0, height), (0, 0, 0)]
    faces, uvcoords = [], []
    for k in range(rings - 1):
        for j in range(seg):
            nj = (j + 1) % seg
            a, b, c, d = k * seg + j, k * seg + nj, (k + 1) * seg + nj, (k + 1) * seg + j
            ua, ub = j / seg, (j + 1) / seg
            va, vb = k / rings, (k + 1) / rings
            faces.extend([(a, b, c), (a, c, d)])
            uvcoords.extend([[(ua, va), (ub, va), (ub, vb)], [(ua, va), (ub, vb), (ua, vb)]])
    for j in range(seg):
        nj = (j + 1) % seg
        faces.extend([((rings - 1) * seg + j, (rings - 1) * seg + nj, rings * seg), (nj, j, rings * seg + 1)])
        uvcoords.extend([[(j / seg, (rings - 1) / rings), ((j + 1) / seg, (rings - 1) / rings), ((j + .5) / seg, 1)], [((j + 1) / seg, 0), (j / seg, 0), ((j + .5) / seg, 0.001)]])
    a = np.array(verts)
    # Centre the complete footprint bounding box, including the asymmetric ash apron.
    a[:, :2] -= (a[:, :2].max(axis=0) + a[:, :2].min(axis=0)) * .5
    a *= target / np.ptp(a, axis=0).max()
    name = f'M2_{index + 1:02}' if big else f'M1_{index + 1:02}_{NAMES[index]}'
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(a.tolist(), [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    uv = mesh.uv_layers.new(name='UVMap')
    cols, rows = (2, 2) if big else (4, 2)
    col, row = index % cols, index // cols
    for poly, coords in zip(mesh.polygons, uvcoords):
        poly.use_smooth = poly.center.z > 0.00001
        for li, (u, v) in zip(poly.loop_indices, coords):
            uv.data[li].uv = ((col + .06 + .88 * u) / cols, (row + .06 + .88 * v) / rows)
    obj['target_max_dimension_m'] = target
    obj['seed'] = SEED + index
    obj['ash_windward_Unity'] = '+Z' if big else 'top'
    return obj


def inspect(obj, target, big):
    m = obj.data
    bm = bmesh.new()
    bm.from_mesh(m)
    boundary = sum(e.is_boundary for e in bm.edges)
    nonmanifold = sum(not e.is_manifold for e in bm.edges)
    volume = bm.calc_volume(signed=True)
    bm.free()
    coords = np.array([v.co[:] for v in m.vertices])
    areas = [p.area for p in m.polygons]
    uv = np.array([p.uv[:] for p in m.uv_layers.active.data])
    dimensions = np.ptp(coords, axis=0)
    tri = sum(len(p.vertices) - 2 for p in m.polygons)
    lo, hi = (600, 1200) if big else (150, 400)
    result = dict(name=obj.name, target_m=target, max_dimension_m=float(max(dimensions)), dimensions_m=dimensions.tolist(), triangles=tri, boundary_edges=boundary, nonmanifold_edges=nonmanifold, degenerate_faces=sum(a < 1e-14 for a in areas), min_triangle_area=min(areas), signed_volume=volume, base_z=float(coords[:, 2].min()), uv_in_atlas_tile=bool(np.all(uv > 0) and np.all(uv < 1)), pivot_center_xy=bool(np.max(abs(coords[:, :2].min(axis=0) + coords[:, :2].max(axis=0))) < 1e-6), fbx=f'Assets/Art/Field/Models/{obj.name}.fbx')
    result['passed'] = bool(lo <= tri <= hi and boundary == nonmanifold == 0 and volume > 0 and result['degenerate_faces'] == 0 and abs(max(dimensions) - target) < 1e-6 and result['pivot_center_xy'] and result['uv_in_atlas_tile'])
    return result


def export(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=str(OUT / 'Models' / f'{obj.name}.fbx'), use_selection=True, object_types={'MESH'}, axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True, add_leaf_bones=False, bake_anim=False, path_mode='STRIP')


def preview(objects, name, normalize=False):
    scene = bpy.context.scene
    copies = []
    columns = 4 if len(objects) == 8 else 3
    for i, obj in enumerate(objects):
        cp = obj.copy()
        cp.data = obj.data
        scene.collection.objects.link(cp)
        if normalize:
            cp.scale = (1 / max(obj.dimensions),) * 3
        cp.location = ((i % columns - (columns - 1) * .5) * 1.65, (i // columns) * 1.55, .025)
        copies.append(cp)
        text = bpy.data.curves.new('label', 'FONT')
        text.body = obj.name.replace('M1_', '').replace('M2_', 'Boulder ') + f'  {max(obj.dimensions):.2f}m'
        text.size = .095
        text.align_x = 'CENTER'
        label = bpy.data.objects.new('label', text)
        scene.collection.objects.link(label)
        label.location = (cp.location.x, cp.location.y - .65, .028)
        copies.append(label)
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, -.005))
    plane = bpy.context.object
    floor = bpy.data.materials.get('PreviewAsh') or bpy.data.materials.new('PreviewAsh')
    floor.diffuse_color = (.46, .42, .36, 1)
    floor.use_nodes = True
    floor.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value = (.46, .42, .36, 1)
    floor.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value = .95
    plane.data.materials.append(floor)
    copies.append(plane)
    bpy.ops.object.camera_add(location=(3.5, -7, 8))
    camera = bpy.context.object
    camera.rotation_euler = (Vector((0, .6 if len(objects) == 8 else 0, 0)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = 7.7 if len(objects) == 8 else 7.0
    scene.camera = camera
    copies.append(camera)
    bpy.ops.object.light_add(type='AREA', location=(-3, -4, 7))
    light = bpy.context.object
    light.data.energy = 1700
    light.data.shape = 'DISK'
    light.data.size = 5
    copies.append(light)
    scene.world.color = (.22, .22, .22)
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 24
    scene.cycles.use_denoising = False
    scene.render.threads_mode = 'FIXED'
    scene.render.threads = 4
    scene.render.resolution_x, scene.render.resolution_y = 1600, 1000
    scene.render.resolution_percentage = 100
    scene.view_settings.view_transform = 'AgX'
    scene.render.filepath = str(SOURCE / 'Previews' / name)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / (name.replace('.png', '.blend'))))
    bpy.ops.render.render(write_still=True)
    for o in copies:
        bpy.data.objects.remove(o, do_unlink=True)


def main():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    bpy.context.scene.unit_settings.system = 'METRIC'
    bpy.context.scene.unit_settings.scale_length = 1
    smallmat = atlas('RocksSmall', 8, 4, 2)
    bigmat = atlas('Boulders', 3, 2, 2, True)
    small = [rock(i, size, smallmat) for i, size in enumerate(SIZES)]
    big = [rock(i, size, bigmat, True) for i, size in enumerate([.6, 1.2, 1.8])]
    report = dict(blender=bpy.app.version_string, seed=SEED, windward_unity='+Z', models=[])
    for obj in small + big:
        result = inspect(obj, obj['target_max_dimension_m'], obj.name.startswith('M2'))
        if not result['passed']:
            raise RuntimeError(result)
        export(obj)
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(ROOT / result['fbx']))
        imported = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
        bpy.context.view_layer.update()
        # FBX has Y-up local vertices and an importer axis-conversion transform.
        # Validate reconstructed authoring-space geometry, not untransformed data.
        imported[0].data.transform(imported[0].matrix_world)
        imported[0].matrix_world.identity()
        roundtrip = inspect(imported[0], result['target_m'], obj.name.startswith('M2'))
        result['fbx_roundtrip_passed'] = bool(roundtrip['passed'] and roundtrip['triangles'] == result['triangles'])
        for o in set(bpy.data.objects) - before:
            bpy.data.objects.remove(o, do_unlink=True)
        report['models'].append(result)
    (SOURCE / 'rock-validation.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    for obj in small + big:
        obj.hide_render = True
    # Copies inherit render flag, so reset explicitly within preview invocation.
    for obj in small:
        obj.hide_render = False
        obj.location = (100, 100, 0)
    preview(small, 'M1_rocks.png', normalize=True)
    for obj in small:
        obj.hide_render = True
        obj.location = (0, 0, 0)
    for obj in big:
        obj.hide_render = False
        obj.location = (100, 100, 0)
    preview(big, 'M2_boulders.png', normalize=False)
    for obj in big:
        obj.location = (0, 0, 0)
    print('ROCKS COMPLETE:', len(report['models']), 'validated meshes')


if __name__ == '__main__':
    main()
