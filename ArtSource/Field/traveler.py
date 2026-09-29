"""Rebuild M3_Traveler. Blender 4.5: blender -b --python ArtSource/Field/traveler.py"""
from pathlib import Path
import bpy, bmesh, math, json
import numpy as np
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/Art/Field'; SRC=ROOT/'ArtSource/Field'
for d in [OUT/'Models',OUT/'Textures',SRC]: d.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
# Four horizontal atlas bands: moss cloth, dark leather, flax scarf, black metal.
N=1024; y,x=np.mgrid[0:N,0:N]; rng=np.random.default_rng(29092026)
noise=rng.random((N,N)); weave=.025*np.sin(x*math.pi/2)*np.sin(y*math.pi/2)
colors=np.array([[.215,.235,.145],[.13,.082,.046],[.31,.28,.20],[.075,.077,.064]])
band=np.minimum(y//256,3); base=colors[band]*(.91+.15*noise[:,:,None])+weave[:,:,None]
rough=np.choose(band,[.87,.69,.91,.6])+noise*.035
# Tangent normal generated from the same weave height field. Green is +Y.
h=weave+noise*.009; dy,dx=np.gradient(h); normal=np.stack([-dx*1.6,-dy*1.6,np.ones_like(h)],axis=-1); normal/=np.linalg.norm(normal,axis=-1)[:,:,None]
def tex(name,rgb,alpha=None):
 a=np.ones((N,N,4),dtype=np.float32); a[:,:,:3]=rgb; a[:,:,3]=1 if alpha is None else alpha
 im=bpy.data.images.new('Traveler_'+name,width=N,height=N,alpha=True); im.colorspace_settings.name='sRGB' if name=='Albedo' else 'Non-Color'; im.pixels.foreach_set(a.ravel()); im.filepath_raw=str(OUT/'Textures'/('Traveler_'+name+'.png')); im.file_format='PNG'; im.save(); return im
albedo=tex('Albedo',np.clip(base,0,1)); norm=tex('Normal',normal*.5+.5); rt=tex('Roughness',np.repeat(rough[:,:,None],3,2)); ao=tex('AO',np.ones((N,N,3))); ms=tex('MetallicSmoothness',np.zeros((N,N,3)),1-rough)
mat=bpy.data.materials.new('Traveler_ClothLeather'); mat.use_nodes=True
nt=mat.node_tree; bs=nt.nodes.get('Principled BSDF')
for im,socket in [(albedo,'Base Color'),(rt,'Roughness')]:
 n=nt.nodes.new('ShaderNodeTexImage'); n.image=im
 if socket!='Base Color': im.colorspace_settings.name='Non-Color'
 nt.links.new(n.outputs['Color'],bs.inputs[socket])
n=nt.nodes.new('ShaderNodeTexImage'); n.image=norm; norm.colorspace_settings.name='Non-Color'; nm=nt.nodes.new('ShaderNodeNormalMap'); nm.inputs['Strength'].default_value=.38; nt.links.new(n.outputs['Color'],nm.inputs['Color']); nt.links.new(nm.outputs[0],bs.inputs['Normal'])
verts=[]; faces=[]; uvs=[]; weights=[]
def surface(rings,segments,region,weightfn,cap=True):
 start=len(verts)
 for j,ring in enumerate(rings):
  for k in range(segments):
   t=2*math.pi*k/segments; p=ring(t); verts.append(p); weights.append(weightfn(p)); uvs.append((.02+.96*k/segments,(region+.04+.92*j/max(1,len(rings)-1))/4))
 for j in range(len(rings)-1):
  for k in range(segments):
   a=start+j*segments+k; b=start+j*segments+(k+1)%segments; faces.append((a,b,b+segments,a+segments))
 if cap:
  faces.append(tuple(start+k for k in reversed(range(segments)))); faces.append(tuple(start+(len(rings)-1)*segments+k for k in range(segments)))
def fixed(b): return lambda p:{b:1}
def ellipsoid(c,r,reg,b,seg=24,rows=16):
 seg=max(10,int(seg*.68)); rows=max(6,int(rows*.68))
 rings=[]
 for j in range(rows+1):
  a=-math.pi/2+math.pi*j/rows; rr=max(.004,math.cos(a)); z=c[2]+r[2]*math.sin(a)
  rings.append(lambda t,rr=rr,z=z:(c[0]+r[0]*rr*math.cos(t),c[1]+r[1]*rr*math.sin(t),z))
 surface(rings,seg,reg,b if callable(b) else fixed(b))
def torso_weight(p):
 z=p[2]
 if z<.96:
  # The lower coat follows the thighs so knees cannot emerge through a rigid hem.
  follow=min(.95,max(0,(.96-z)/.30))
  left=max(0,min(1,(p[0]+.06)/.12))
  return {'Hips':1-follow,'LeftUpperLeg':follow*left,'RightUpperLeg':follow*(1-left)}
 if z<1.2:
  a=(z-.96)/.24; return {'Hips':1-a,'Spine':a}
 a=min(1,(z-1.2)/.2);return {'Spine':1-a,'Chest':a}
# Tunic with deliberately irregular, broad fluted hem.
rings=[]
for j in range(17):
 z=.53+j*.055; w=.30-(min(1,max(0,(z-.7)/.45))*.10) if z<1.22 else .20+min(1,(z-1.22)/.12)*.065
 depth=.175 if z<.95 else .13
 rings.append(lambda t,z=z,w=w,depth=depth:(w*math.cos(t)*(1+.03*math.sin(7*t)),depth*math.sin(t),z+.008*math.sin(5*t)))
surface(rings,32,0,torso_weight)
# Head hidden beneath hood; lower face wrapped in a sand-colored scarf.
ellipsoid((0,-.022,1.587),(.142,.136,.163),0,'Head',32,20)
ellipsoid((0,-.132,1.576),(.104,.04,.094),3,'Head',24,12)
ellipsoid((0,-.143,1.53),(.111,.035,.055),2,'Head',24,10)
# Draped neck cowl and folded lip around the hood opening.
ellipsoid((0,0,1.423),(.21,.166,.061),0,'Neck',24,10)
# Arms are rigged in a T-pose, with tapered sleeves and mittens.
for side,s in [('Left',1),('Right',-1)]:
 rings=[]
 for j in range(17):
  xx=.20+j*.0275; radius=.091-j*.0022
  rings.append(lambda t,xx=xx,radius=radius,s=s:(s*xx,radius*math.sin(t),1.395+radius*math.cos(t)))
 def arm_w(p,side=side):
  xx=abs(p[0]); a=max(0,min(1,(xx-.37)/.10));return {side+'UpperArm':1-a,side+'LowerArm':a}
 surface(rings,20,0,arm_w)
 ellipsoid((s*.699,-.002,1.395),(.09,.047,.052),1,side+'Hand',20,10)
 # Pants mostly emerge below hem; substantial worn boots with forward toe.
 def leg_w(p,side=side):
  a=max(0,min(1,(p[2]-.45)/.14));return {side+'LowerLeg':1-a,side+'UpperLeg':a}
 ellipsoid((s*.116,0,.535),(.088,.082,.345),1,leg_w,20,14)
 ellipsoid((s*.118,-.046,.105),(.097,.174,.105),1,side+'Foot',24,12)
 ellipsoid((s*.118,.005,.227),(.094,.091,.165),1,side+'LowerLeg',20,12)
 # Straps over shoulders and down front.
 ellipsoid((s*.142,-.135,1.247),(.026,.023,.183),1,'Chest',12,8)
# An old heavy pack; canvas flap, leather pocket, and bedroll.
ellipsoid((0,.219,1.193),(.205,.115,.247),1,'Chest',28,18)
ellipsoid((0,.286,1.361),(.212,.083,.105),0,'Chest',24,10)
ellipsoid((0,.322,1.14),(.13,.042,.11),1,'Chest',20,10)
ellipsoid((0,.229,1.456),(.243,.082,.077),2,'Chest',24,12)
# Cloak is a rear semicircle with a rippled hem, which leaves boots visible.
rings=[]
for j in range(16):
 z=.46+j*.061; f=j/15; w=.34*(1-f)+.22*f
 rings.append(lambda t,z=z,w=w,f=f:(w*math.cos(t),.065+(.195-.035*f)*math.sin(t),z+.025*(1-f)*math.cos(t*7)))
# Ring full wrap is behind the body and gives a readable heavy travel silhouette.
surface(rings,28,0,torso_weight)
verts=[(p[0],p[1],0 if p[2]<.032 else p[2]) for p in verts]
mesh=bpy.data.meshes.new('TravelerMesh'); mesh.from_pydata(verts,[],faces); mesh.update()
# Mirrored sleeves run along opposite axes; orient every closed part outward.
bm=bmesh.new(); bm.from_mesh(mesh); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(mesh); bm.free(); mesh.update()
obj=bpy.data.objects.new('M3_Traveler',mesh); bpy.context.collection.objects.link(obj); obj.data.materials.append(mat)
uv=mesh.uv_layers.new(name='UVMap')
for poly in mesh.polygons:
 poly.use_smooth=True
 for li in poly.loop_indices: uv.data[li].uv=uvs[mesh.loops[li].vertex_index]
# Match a humanoid bone hierarchy and Unity's required mapping names.
arm=bpy.data.armatures.new('Traveler_Humanoid'); rig=bpy.data.objects.new('Traveler_Rig',arm); bpy.context.collection.objects.link(rig); bpy.context.view_layer.objects.active=rig; rig.select_set(True); bpy.ops.object.mode_set(mode='EDIT')
def bone(name,head,tail,parent=None):
 b=arm.edit_bones.new(name); b.head=head;b.tail=tail
 if parent:b.parent=arm.edit_bones[parent]
bone('Hips',(0,0,.89),(0,0,1.04));bone('Spine',(0,0,1.04),(0,0,1.23),'Hips');bone('Chest',(0,0,1.23),(0,0,1.40),'Spine');bone('Neck',(0,0,1.40),(0,0,1.49),'Chest');bone('Head',(0,0,1.49),(0,0,1.72),'Neck')
for side,s in [('Left',1),('Right',-1)]:
 bone(side+'Shoulder',(s*.03,0,1.395),(s*.22,0,1.395),'Chest');bone(side+'UpperArm',(s*.22,0,1.395),(s*.43,0,1.395),side+'Shoulder');bone(side+'LowerArm',(s*.43,0,1.395),(s*.65,0,1.395),side+'UpperArm');bone(side+'Hand',(s*.65,0,1.395),(s*.76,0,1.395),side+'LowerArm')
 bone(side+'UpperLeg',(s*.116,0,.9),(s*.116,0,.51),'Hips');bone(side+'LowerLeg',(s*.116,0,.51),(s*.116,0,.15),side+'UpperLeg');bone(side+'Foot',(s*.116,0,.15),(s*.116,-.13,.07),side+'LowerLeg');bone(side+'Toes',(s*.116,-.13,.07),(s*.116,-.21,.055),side+'Foot')
bpy.ops.object.mode_set(mode='OBJECT')
for b in arm.bones:obj.vertex_groups.new(name=b.name)
for i,ws in enumerate(weights):
 for b,w in ws.items():
  if w>0:obj.vertex_groups[b].add([i],w,'REPLACE')
obj.parent=rig; mod=obj.modifiers.new('HumanoidSkin','ARMATURE');mod.object=rig
# Triangulate deterministic export topology; retain the mesh as an editable source.
tri=obj.modifiers.new('ExportTriangles','TRIANGULATE');bpy.context.view_layer.objects.active=obj; bpy.ops.object.modifier_apply(modifier=tri.name)
scene=bpy.context.scene;scene.render.fps=30
for pb in rig.pose.bones:pb.rotation_mode='XYZ'
for name,end in [('Walk_Tired',37),('Idle_Breathing',121)]:
 rig.animation_data_create(); action=bpy.data.actions.new(name);rig.animation_data.action=action
 for frame in range(1,end+1):
  t=(frame-1)/(end-1)*2*math.pi
  for pb in rig.pose.bones:pb.rotation_euler=(0,0,0);pb.location=(0,0,0)
  rig.pose.bones['Spine'].rotation_euler.x=.08
  rig.pose.bones['Chest'].rotation_euler.x=.055+.012*math.sin(t)
  rig.pose.bones['Head'].rotation_euler.x=.05
  if name=='Walk_Tired':
   rig.pose.bones['Hips'].location.y=-.12
  for side,s in [('Left',1),('Right',-1)]:
   rig.pose.bones[side+'UpperArm'].rotation_euler.x=-1.34
   rig.pose.bones[side+'LowerArm'].rotation_euler.x=-.12
   if name=='Walk_Tired':
    phase=((frame-1)/(end-1)+(0 if s==1 else .5))%1
    if phase<.5:
     foot_y=-.39+1.56*phase; lift=0
    else:
     q=(phase-.5)*2
     foot_y=(2*q**3-3*q*q+1)*.39+(q**3-2*q*q+q)*.78+(-2*q**3+3*q*q)*(-.39)+(q**3-q*q)*.78
     lift=.075*math.sin(math.pi*q)**1.2
    # Planar two-link IK: stance moves backward relative to the body at
    # exactly 1.3m/s, cancelling forward preview translation.
    down=.78-(.15+lift); distance=math.sqrt(foot_y**2+down**2)
    angle=math.atan2(foot_y,down)
    upper=angle-math.acos(max(-1,min(1,(.39**2+distance**2-.36**2)/(2*.39*distance))))
    lower=angle+math.acos(max(-1,min(1,(.36**2+distance**2-.39**2)/(2*.36*distance))))
    rig.pose.bones[side+'UpperLeg'].rotation_euler.x=upper
    rig.pose.bones[side+'LowerLeg'].rotation_euler.x=lower-upper
    rig.pose.bones[side+'Foot'].rotation_euler.x=-lower
    rig.pose.bones[side+'UpperArm'].rotation_euler.z=s*.13*math.sin(phase*2*math.pi)
  for pb in rig.pose.bones:pb.keyframe_insert('rotation_euler',frame=frame,group=pb.name);pb.keyframe_insert('location',frame=frame,group=pb.name)
 action.use_fake_user=True
rig.animation_data.action=None
for pb in rig.pose.bones:pb.rotation_euler=(0,0,0);pb.location=(0,0,0)
# Export only rig + skinned model, with both named actions.
bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig
fbx=OUT/'Models/M3_Traveler.fbx'
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE','MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,path_mode='STRIP',use_mesh_modifiers=True)
report={'triangles':len(obj.data.polygons),'height_m':round(max(v.co.z for v in obj.data.vertices)-min(v.co.z for v in obj.data.vertices),4),'dimensions_m':list(obj.dimensions),'unweighted_vertices':sum(not v.groups for v in obj.data.vertices),'bone_names':list(arm.bones.keys()),'animation_names':sorted(a.name for a in bpy.data.actions),'loop_endpoints_identical':True,'root_motion':False,'texture_size':[1024,1024],'rest_pose':'T pose; Blender Z up, -Y forward','fbx_axes':'-Z forward, Y up','walk_cycle_seconds':1.2,'intended_game_speed_mps':1.3,'idle_cycle_seconds':4.0}
# Verify loop endpoint transforms on all keyed components from evaluated actions.
for action in bpy.data.actions:
 rig.animation_data.action=action; end=37 if action.name=='Walk_Tired' else 121
 scene.frame_set(1); first=[tuple(p.rotation_euler)+tuple(p.location) for p in rig.pose.bones]
 scene.frame_set(end);last=[tuple(p.rotation_euler)+tuple(p.location) for p in rig.pose.bones]
 report['loop_endpoints_identical'] &= all(abs(a-b)<1e-5 for aa,bb in zip(first,last) for a,b in zip(aa,bb))
# Independent FBX reimport validates exported payload, not just Blender source.
original=set(bpy.data.objects);bpy.ops.import_scene.fbx(filepath=str(fbx),use_anim=True)
imported=[o for o in bpy.data.objects if o not in original]; imesh=[o for o in imported if o.type=='MESH'];irig=[o for o in imported if o.type=='ARMATURE']
report['fbx_roundtrip_valid']=bool(imesh and irig and len(irig[0].data.bones)==21 and all(v.groups for m in imesh for v in m.data.vertices))
report['fbx_roundtrip_bones']=len(irig[0].data.bones) if irig else 0
for o in imported:bpy.data.objects.remove(o,do_unlink=True)
(SRC/'traveler_validation.json').write_text(json.dumps(report,indent=2))
rig.animation_data.action=bpy.data.actions['Idle_Breathing'];scene.frame_set(1)
# Studio preview with a long cast shadow, emphasizing silhouette and covered face.
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.012));ground=bpy.context.object;ground.name='PreviewGround'
gm=bpy.data.materials.new('PreviewGround');gm.diffuse_color=(.16,.17,.15,1);ground.data.materials.append(gm)
world=scene.world;world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.18,.20,.24,1);world.node_tree.nodes['Background'].inputs[1].default_value=.45
def aim(o,p):o.rotation_euler=(Vector(p)-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.light_add(type='AREA',location=(-3,-4,6));light=bpy.context.object;light.data.energy=950;light.data.shape='DISK';light.data.size=4;aim(light,(0,0,1))
bpy.ops.object.camera_add(location=(2.65,-4.4,2.1));cam=bpy.context.object;aim(cam,(0,0,.88));cam.data.type='ORTHO';cam.data.ortho_scale=2.45;scene.camera=cam
scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=False;scene.render.threads_mode='FIXED';scene.render.threads=4;scene.render.resolution_x=900;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG';scene.render.filepath=str(SRC/'traveler_studio.png');bpy.ops.wm.save_as_mainfile(filepath=str(SRC/'M3_Traveler.blend'));bpy.ops.render.render(write_still=True)
light.data.size=.08;light.location=(-3,-2,3);light.data.energy=1200;aim(light,(0,0,0));cam.location=(3,-5,5);aim(cam,(.35,.4,.4));cam.data.ortho_scale=3.9;scene.render.filepath=str(SRC/'traveler_shadow.png');bpy.ops.render.render(write_still=True)
print('TRAVELER_VALIDATION '+json.dumps(report))



