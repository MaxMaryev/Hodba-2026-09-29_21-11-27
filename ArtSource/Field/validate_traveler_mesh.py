from pathlib import Path
import bpy
import bmesh
import json

root = Path(__file__).resolve().parents[2]
bpy.ops.wm.open_mainfile(filepath=str(root / 'ArtSource/Field/M3_Traveler.blend'))
bm = bmesh.new(); bm.from_mesh(bpy.data.objects['M3_Traveler'].data)
unseen = set(bm.faces); volumes = []
while unseen:
    first = unseen.pop(); stack = [first]; component = [first]
    while stack:
        for edge in stack.pop().edges:
            for neighbor in edge.link_faces:
                if neighbor in unseen:
                    unseen.remove(neighbor); stack.append(neighbor); component.append(neighbor)
    volume = sum(f.verts[0].co.dot(f.verts[1].co.cross(f.verts[2].co)) / 6 for f in component)
    volumes.append(volume)
report = {'closed_parts': len(volumes), 'part_signed_volumes': volumes, 'nonmanifold_edges': sum(not e.is_manifold for e in bm.edges), 'degenerate_triangles': sum(f.calc_area() < 1e-12 for f in bm.faces)}
bm.free()
(root / 'ArtSource/Field/traveler-mesh-validation.json').write_text(json.dumps(report, indent=2))
assert report['nonmanifold_edges'] == 0, report
assert report['degenerate_triangles'] == 0, report
assert min(volumes) > 0, report
print('PASS traveler closed components, outward faces and nondegenerate triangles')
