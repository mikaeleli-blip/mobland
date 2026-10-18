"""Render a 4-view model sheet (front / left / back / right) for a character.

    <python-with-bpy> Tools/blender/human/sheet.py OUT.png name [name...]
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402
import mbl  # noqa: E402
import gangster  # noqa: E402


def setup_scene():
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 40
    scene.cycles.use_denoising = True
    world = bpy.data.worlds.new('w')
    world.use_nodes = True
    bg = world.node_tree.nodes['Background']
    bg.inputs['Color'].default_value = (0.62, 0.66, 0.72, 1)
    bg.inputs['Strength'].default_value = 0.9
    scene.world = world
    bpy.ops.mesh.primitive_plane_add(size=50, location=(0, 0, 0))
    g = bpy.context.object
    gm = bpy.data.materials.new('ground')
    gm.use_nodes = True
    gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.5, 0.52, 0.56, 1)
    g.data.materials.append(gm)
    for rot, energy in (((math.radians(50), 0, math.radians(-35)), 3.0), ((math.radians(60), 0, math.radians(150)), 1.2)):
        sun = bpy.data.lights.new('sun', 'SUN')
        sun.energy = energy
        sun.angle = math.radians(8)
        so = bpy.data.objects.new('sun', sun)
        so.rotation_euler = rot
        scene.collection.objects.link(so)
    cam = bpy.data.cameras.new('cam')
    cam.type = 'ORTHO'
    cam.ortho_scale = 2.15
    co = bpy.data.objects.new('cam', cam)
    scene.collection.objects.link(co)
    scene.camera = co
    return co


def render_views(path_base, size=(420, 900)):
    scene = bpy.context.scene
    co = scene.camera
    scene.render.resolution_x, scene.render.resolution_y = size
    out = []
    t = Vector((0, 0, 0.98))
    for label, azim in (('front', 180), ('left', -90), ('back', 0), ('right', 90)):
        a = math.radians(azim)
        e = math.radians(4)
        co.location = t + Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e))) * 8
        co.rotation_euler = (t - co.location).to_track_quat('-Z', 'Y').to_euler()
        p = f'{path_base}_{label}.png'
        scene.render.filepath = p
        bpy.ops.render.render(write_still=True)
        out.append(p)
    return out


def main():
    out = sys.argv[1]
    names = sys.argv[2:]
    rows = []
    for n in names:
        mbl.reset_scene()
        setup_scene()
        obj = gangster.ALL[n]()
        me = obj.data
        print(f'{n}: {len(me.vertices)} verts, {sum(len(p.vertices) - 2 for p in me.polygons)} tris, height {obj.dimensions.z:.2f} m')
        rows.append(render_views(os.path.splitext(out)[0] + '_' + n))
    from PIL import Image, ImageDraw, ImageFont
    W, H = 420, 900
    sheet = Image.new('RGB', (W * 4, H * len(rows) + 40), (200, 204, 210))
    d = ImageDraw.Draw(sheet)
    f = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf', 20)
    for i, lab in enumerate(('FRONT', 'LEFT SIDE', 'BACK', 'RIGHT SIDE')):
        d.text((i * W + W // 2 - 50, 10), lab, fill=(40, 40, 40), font=f)
    for r, row in enumerate(rows):
        for i, p in enumerate(row):
            sheet.paste(Image.open(p).convert('RGB'), (i * W, 40 + r * H))
    sheet.save(out, quality=90)


if __name__ == '__main__':
    main()
