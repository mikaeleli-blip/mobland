"""Mobland modelling helpers for Blender (bpy + bmesh), used headless.

Coordinates while modelling: Blender native, metres. X = right, Y = forward (bow / front), Z = up.
A model is a Builder: parts are made in small bmeshes, given a material slot, then merged.
Export writes a compact binary (.bytes) the game loads directly (see Assets/.../ModelLibrary.cs):
axes are converted to Unity (x, z, y), which also flips handedness so winding stays correct.
"""
import math
import struct

import bpy  # must come before bmesh
import bmesh
from mathutils import Matrix, Vector


def V(x, y, z):
    return Vector((x, y, z))


class Builder:
    def __init__(self, name, slots):
        self.name = name
        self.slots = list(slots)
        self.bm = bmesh.new()

    def slot(self, s):
        if s not in self.slots:
            self.slots.append(s)
        return self.slots.index(s)

    # ------------------------------------------------------------ merge a part
    def add(self, part_bm, slot, recalc=True, smooth=True):
        if recalc:
            bmesh.ops.recalc_face_normals(part_bm, faces=part_bm.faces[:])
        idx = self.slot(slot)
        for f in part_bm.faces:
            f.material_index = idx
            f.smooth = smooth
        me = bpy.data.meshes.new('tmp')
        part_bm.to_mesh(me)
        part_bm.free()
        self.bm.from_mesh(me)
        bpy.data.meshes.remove(me)

    def add_classified(self, part_bm, classify, smooth=True):
        """Merge a part, choosing each face's slot with classify(face) -> slot name."""
        bmesh.ops.recalc_face_normals(part_bm, faces=part_bm.faces[:])
        for f in part_bm.faces:
            f.material_index = self.slot(classify(f))
            f.smooth = smooth
        me = bpy.data.meshes.new('tmp')
        part_bm.to_mesh(me)
        part_bm.free()
        self.bm.from_mesh(me)
        bpy.data.meshes.remove(me)

    # ------------------------------------------------------------ primitives
    def box(self, slot, center, size, bevel=0.0, segs=2, rot=None, smooth=False):
        bm = bmesh.new()
        m = Matrix.Translation(Vector(center))
        if rot is not None:
            m = m @ rot
        m = m @ Matrix.Diagonal(Vector((size[0], size[1], size[2], 1.0)))
        bmesh.ops.create_cube(bm, size=1.0, matrix=m)
        if bevel > 0:
            bmesh.ops.bevel(bm, geom=bm.edges[:], offset=bevel, segments=segs, profile=0.5, affect='EDGES', clamp_overlap=True)
        self.add(bm, slot, smooth=smooth or bevel > 0)

    def box_mm(self, slot, mn, mx, bevel=0.0, segs=2):
        c = [(a + b) / 2 for a, b in zip(mn, mx)]
        s = [abs(b - a) for a, b in zip(mn, mx)]
        self.box(slot, c, s, bevel, segs)

    def cyl(self, slot, center, radius, depth, segs=12, axis='Z', radius2=None, cap=True, smooth=True):
        bm = bmesh.new()
        rot = {'Z': Matrix.Identity(4), 'X': Matrix.Rotation(math.pi / 2, 4, 'Y'), 'Y': Matrix.Rotation(math.pi / 2, 4, 'X')}[axis]
        m = Matrix.Translation(Vector(center)) @ rot
        bmesh.ops.create_cone(bm, cap_ends=cap, cap_tris=False, segments=segs, radius1=radius,
                              radius2=radius if radius2 is None else radius2, depth=depth, matrix=m)
        self.add(bm, slot, smooth=smooth)

    def sphere(self, slot, center, radii, u=12, v=8):
        bm = bmesh.new()
        m = Matrix.Translation(Vector(center)) @ Matrix.Diagonal(Vector((radii[0], radii[1], radii[2], 1.0)))
        bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=1.0, matrix=m)
        self.add(bm, slot)

    def beam(self, slot, a, b, w, h=None):
        a, b = Vector(a), Vector(b)
        d = b - a
        L = d.length
        if L < 1e-5:
            return
        rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        bm = bmesh.new()
        m = Matrix.Translation((a + b) / 2) @ rot @ Matrix.Diagonal(Vector((w, h or w, L, 1.0)))
        bmesh.ops.create_cube(bm, size=1.0, matrix=m)
        self.add(bm, slot, smooth=False)

    def tube(self, slot, a, b, r, segs=8):
        a, b = Vector(a), Vector(b)
        d = b - a
        rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        bm = bmesh.new()
        m = Matrix.Translation((a + b) / 2) @ rot
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r, radius2=r, depth=d.length, matrix=m)
        self.add(bm, slot)

    # ------------------------------------------------------------ loft / sweep
    def loft(self, slot, sections, closed=True, cap_start=True, cap_end=True, smooth=True, recalc=True):
        """sections: list of point-lists (same count). Consecutive sections are bridged with quads."""
        bm = bmesh.new()
        rings = [[bm.verts.new(Vector(p)) for p in sec] for sec in sections]
        n = len(sections[0])
        for r0, r1 in zip(rings, rings[1:]):
            for i in range(n if closed else n - 1):
                j = (i + 1) % n
                try:
                    bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
                except ValueError:
                    pass
        if closed:
            for ring, ok in ((rings[0], cap_start), (rings[-1], cap_end)):
                if ok:
                    try:
                        bm.faces.new(ring)
                    except ValueError:
                        pass
        bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
        self.add(bm, slot, recalc=recalc, smooth=smooth)

    def sweep(self, slot, path, profile, up=None, closed_path=False, smooth=True, mirror_x=False):
        """Sweep a 2D profile [(u, v), ...] along a 3D path. u runs along `side`, v along the frame normal."""
        secs = []
        n = len(path)
        for i, p in enumerate(path):
            p = Vector(p)
            if closed_path:
                t = Vector(path[(i + 1) % n]) - Vector(path[i - 1])
            else:
                t = Vector(path[min(i + 1, n - 1)]) - Vector(path[max(i - 1, 0)])
            t.normalize()
            if up is None:
                side = Vector((1, 0, 0))            # path lies in the YZ plane
                nrm = t.cross(side).normalized()     # outward / upward from the path
            else:
                nrm = Vector(up).normalized()
                side = nrm.cross(t).normalized()
            sec = []
            for (u, v) in profile:
                uu = -u if mirror_x else u
                sec.append(p + side * uu + nrm * v)
            secs.append(sec)
        if closed_path:
            secs.append(secs[0])
        self.loft(slot, secs, closed=True, cap_start=not closed_path, cap_end=not closed_path, smooth=smooth)

    def ring(self, slot, center, axis, radius, profile, segs=16):
        """Sweep a closed 2D profile (r_offset, axial) around an axis -> tyres, rims, hoops."""
        c = Vector(center)
        ax = Vector(axis).normalized()
        a = ax.orthogonal().normalized()
        b = ax.cross(a).normalized()
        secs = []
        for i in range(segs):
            ang = 2 * math.pi * i / segs
            radial = a * math.cos(ang) + b * math.sin(ang)
            secs.append([c + radial * (radius + dr) + ax * dz for (dr, dz) in profile])
        secs.append(secs[0])
        self.loft(slot, secs, closed=True, cap_start=False, cap_end=False)

    # ------------------------------------------------------------ finish
    def finish(self, sharp_angle=35.0):
        me = bpy.data.meshes.new(self.name)
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts[:], dist=1e-5)
        self.bm.to_mesh(me)
        self.bm.free()
        obj = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(obj)
        for s in self.slots:
            obj.data.materials.append(material(s))
        try:
            me.set_sharp_from_angle(angle=math.radians(sharp_angle))
        except Exception:
            pass
        return obj


# ---------------------------------------------------------------- materials (preview colours only)

PREVIEW = {
    'Paint': (0.05, 0.05, 0.05, 0.5), 'Paint2': (0.02, 0.02, 0.02, 0.5), 'Black': (0.015, 0.015, 0.015, 0.45),
    'Chrome': (0.8, 0.8, 0.8, 0.15, 1.0), 'Glass': (0.02, 0.025, 0.03, 0.05), 'Lens': (0.9, 0.9, 0.8, 0.05),
    'Tire': (0.02, 0.02, 0.02, 0.8), 'Spokes': (0.45, 0.32, 0.18, 0.5), 'Leather': (0.25, 0.12, 0.06, 0.45),
    'Canvas': (0.25, 0.22, 0.16, 0.9), 'Wood': (0.35, 0.24, 0.14, 0.7), 'Tail': (0.6, 0.05, 0.03, 0.3),
    'Hull': (0.03, 0.03, 0.03, 0.5), 'Bottom': (0.35, 0.06, 0.04, 0.7), 'Boot': (0.8, 0.78, 0.72, 0.5),
    'Deck': (0.42, 0.33, 0.22, 0.8), 'Super': (0.78, 0.74, 0.65, 0.6), 'Funnel': (0.55, 0.12, 0.08, 0.5),
    'Band': (0.02, 0.02, 0.02, 0.5), 'Rope': (0.45, 0.38, 0.25, 0.95), 'Tarp': (0.18, 0.22, 0.15, 0.9),
    'Mahogany': (0.28, 0.08, 0.03, 0.15), 'Brass': (0.7, 0.5, 0.2, 0.25, 1.0), 'CabinGlow': (0.8, 0.6, 0.3, 0.2),
    'Lamp': (0.9, 0.85, 0.7, 0.2), 'Coal': (0.03, 0.03, 0.03, 0.9), 'Red': (0.5, 0.05, 0.04, 0.5),
    'Steel': (0.12, 0.13, 0.13, 0.5, 0.6), 'Box': (0.2, 0.25, 0.18, 0.6),
}


def material(name):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    c = PREVIEW.get(name, (0.5, 0.5, 0.5, 0.5))
    if bsdf:
        bsdf.inputs['Base Color'].default_value = (c[0], c[1], c[2], 1)
        bsdf.inputs['Roughness'].default_value = c[3]
        if len(c) > 4:
            bsdf.inputs['Metallic'].default_value = c[4]
        if name in ('CabinGlow', 'Lamp'):
            bsdf.inputs['Emission Color'].default_value = (1, 0.7, 0.4, 1)
            bsdf.inputs['Emission Strength'].default_value = 0.5
    return m


# ---------------------------------------------------------------- export

def export_bytes(obj, path):
    """Binary mesh: 'MBM1', vertex count, pos/normal (Unity axes), submesh count, (name, index count, indices)."""
    dg = bpy.context.evaluated_depsgraph_get()
    eo = obj.evaluated_get(dg)
    me = eo.to_mesh()
    me.calc_loop_triangles()
    cn = me.corner_normals
    verts = []
    index = {}
    subs = {i: [] for i in range(len(obj.data.materials))}
    for tri in me.loop_triangles:
        ids = []
        for li in tri.loops:
            vi = me.loops[li].vertex_index
            co = me.vertices[vi].co
            n = cn[li].vector
            key = (vi, round(n.x, 3), round(n.y, 3), round(n.z, 3))
            k = index.get(key)
            if k is None:
                k = len(verts)
                index[key] = k
                # Blender (x, y, z) -> Unity (x, z, y)
                verts.append((co.x, co.z, co.y, n.x, n.z, n.y))
            ids.append(k)
        subs[tri.material_index].extend(ids)
    with open(path, 'wb') as f:
        f.write(b'MBM1')
        f.write(struct.pack('<i', len(verts)))
        for v in verts:
            f.write(struct.pack('<6f', *v))
        used = [(i, s) for i, s in subs.items() if s]
        f.write(struct.pack('<i', len(used)))
        for i, s in used:
            name = obj.data.materials[i].name.encode('utf-8')
            f.write(struct.pack('<i', len(name)))
            f.write(name)
            f.write(struct.pack('<i', len(s)))
            f.write(struct.pack(f'<{len(s)}i', *s))
    eo.to_mesh_clear()
    return len(verts), sum(len(s) for s in subs.values()) // 3


# ---------------------------------------------------------------- preview render

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def render_preview(objs, path, size=(900, 560), cam_dist=None, target=(0, 0, 0.8), elev=22, azim=-35, lens=50):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.film_transparent = False
    world = bpy.data.worlds.new('w')
    world.use_nodes = True
    bg = world.node_tree.nodes['Background']
    bg.inputs['Color'].default_value = (0.55, 0.6, 0.68, 1)
    bg.inputs['Strength'].default_value = 0.8
    scene.world = world
    # ground
    bpy.ops.mesh.primitive_plane_add(size=400, location=(0, 0, 0))
    g = bpy.context.object
    gm = bpy.data.materials.new('ground')
    gm.use_nodes = True
    gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.18, 0.18, 0.18, 1)
    g.data.materials.append(gm)
    # sun
    sun = bpy.data.lights.new('sun', 'SUN')
    sun.energy = 3.5
    so = bpy.data.objects.new('sun', sun)
    so.rotation_euler = (math.radians(45), math.radians(10), math.radians(-30))
    scene.collection.objects.link(so)
    # camera
    if cam_dist is None:
        mx = max(max(o.dimensions) for o in objs)
        cam_dist = mx * 1.6
    cam = bpy.data.cameras.new('cam')
    cam.lens = lens
    co = bpy.data.objects.new('cam', cam)
    t = Vector(target)
    e, a = math.radians(elev), math.radians(azim)
    co.location = t + Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e))) * cam_dist
    co.rotation_euler = (t - co.location).to_track_quat('-Z', 'Y').to_euler()
    scene.collection.objects.link(co)
    scene.camera = co
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
