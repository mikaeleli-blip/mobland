"""Build Mobland vehicle/boat models with Blender's Python module (bpy) and export them for the game.

    <python-with-bpy> Tools/blender/build_models.py [names...] [--preview DIR]

Outputs Assets/Mobland/Resources/Mobland/Models/<name>.bytes (loaded by ModelLibrary.cs).
"""
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import mbl  # noqa: E402
import cars  # noqa: E402

try:
    import boats  # noqa: E402
    BOATS = boats.ALL
except ImportError:
    BOATS = {}

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Assets', 'Mobland', 'Resources', 'Mobland', 'Models')


def main():
    args = sys.argv[1:]
    preview = None
    if '--preview' in args:
        i = args.index('--preview')
        preview = args[i + 1]
        del args[i:i + 2]
    models = {**cars.ALL, **BOATS}
    names = args or list(models)
    os.makedirs(OUT, exist_ok=True)
    for name in names:
        mbl.reset_scene()
        obj = models[name]()
        nv, nt = mbl.export_bytes(obj, os.path.join(OUT, name + '.bytes'))
        d = obj.dimensions
        print(f'{name:14s} {nv:6d} verts {nt:6d} tris   size {d.x:.2f} x {d.y:.2f} x {d.z:.2f} m')
        if preview:
            os.makedirs(preview, exist_ok=True)
            big = max(d) > 8
            mbl.render_preview([obj], os.path.join(preview, name + '.png'),
                               target=(0, 0, d.z * 0.35), cam_dist=max(d) * (1.7 if big else 1.55),
                               elev=24, azim=(-38 if big else 140))


if __name__ == '__main__':
    main()
