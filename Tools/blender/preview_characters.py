"""Render preview sheets of the character archetypes (no export to the game).

    <python-with-bpy> Tools/blender/preview_characters.py OUTDIR [names...]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import bpy  # noqa: E402
import mbl  # noqa: E402
import characters  # noqa: E402


def main():
    out = sys.argv[1]
    names = sys.argv[2:] or list(characters.ALL)
    os.makedirs(out, exist_ok=True)
    for n in names:
        mbl.reset_scene()
        obj = characters.ALL[n]()
        me = obj.data
        print(f'{n:18s} {len(me.vertices):6d} verts {sum(len(p.vertices) - 2 for p in me.polygons):6d} tris  h={obj.dimensions.z:.2f}m')
        bpy.context.scene.cycles.samples = 32
        mbl.render_preview([obj], os.path.join(out, n + '.png'), size=(520, 820), cam_dist=4.9,
                           target=(0, 0, 0.92), elev=8, azim=158, lens=55)
        # game-camera view: high and far
        cam = bpy.context.scene.camera
        import math
        from mathutils import Vector
        t = Vector((0, 0, 0.8))
        e, a = math.radians(52), math.radians(150)
        cam.location = t + Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e))) * 5.5
        cam.rotation_euler = (t - cam.location).to_track_quat('-Z', 'Y').to_euler()
        bpy.context.scene.render.resolution_x, bpy.context.scene.render.resolution_y = 260, 260
        bpy.context.scene.render.filepath = os.path.join(out, n + '_top.png')
        bpy.ops.render.render(write_still=True)


if __name__ == '__main__':
    main()
