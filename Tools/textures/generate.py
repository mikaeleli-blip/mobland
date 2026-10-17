"""Generate all Mobland textures.

    python Tools/textures/generate.py

Writes PNGs to Assets/Mobland/Resources/Mobland/Tex. File suffixes tell the Unity importer what they are:
    name.png     albedo (sRGB)
    name_n.png   normal map
    name_m.png   metallic (R) + smoothness (A), linear
    name_e.png   emission (lit windows at night), sRGB
The C# side (FacadeCatalog / SurfaceLibrary) refers to these names.
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(__file__))
from texlib import rgb  # noqa: E402
import facades as F  # noqa: E402
import surfaces as S  # noqa: E402
import signs as G  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Assets', 'Mobland', 'Resources', 'Mobland', 'Tex')

BRICK = {
    'red': [rgb('#8c3b2a'), rgb('#7d3426'), rgb('#94452f'), rgb('#6f2f24')],
    'brown': [rgb('#5e3226'), rgb('#6a3a2b'), rgb('#52291f'), rgb('#704232')],
    'yellow': [rgb('#b89462'), rgb('#a8855a'), rgb('#c29f6c'), rgb('#9c7a50')],
    'soot': [rgb('#4f4440'), rgb('#5a4d47'), rgb('#433a37'), rgb('#62544c')],
}
FRAMES = {
    'red': rgb('#2c3a2e'),       # dark green
    'brown': rgb('#d8d0bc'),     # cream
    'yellow': rgb('#3c2a20'),    # brown
    'soot': rgb('#1d1d1d'),      # black
}


def main():
    os.makedirs(OUT, exist_ok=True)
    only = set(sys.argv[1:])

    jobs = []
    for k, (key, cols) in enumerate(BRICK.items()):
        jobs.append((f'fac_tenement_{key}', lambda key=key, cols=cols, k=k: F.brick_tenement(
            f'fac_tenement_{key}', cols, rgb('#b9b09c'), FRAMES[key], 10 + k, OUT)))
        jobs.append((f'wall_brick_{key}', lambda key=key, cols=cols, k=k: F.brick_plain(
            f'wall_brick_{key}', cols, 50 + k, OUT)))
    jobs += [
        ('fac_brownstone', lambda: F.brownstone('fac_brownstone', rgb('#6b4a3a'), rgb('#2a211b'), 21, OUT)),
        ('fac_sandstone', lambda: F.brownstone('fac_sandstone', rgb('#a08467'), rgb('#e1d8c4'), 22, OUT)),
        ('fac_office_lime', lambda: F.office('fac_office_lime', rgb('#b5ab95'), rgb('#3a3229'), 31, OUT)),
        ('fac_office_granite', lambda: F.office('fac_office_granite', rgb('#8f8d88'), rgb('#26292b'), 32, OUT)),
        ('fac_office_terra', lambda: F.office('fac_office_terra', rgb('#c7ac86'), rgb('#3d3326'), 33, OUT)),
        ('fac_warehouse_red', lambda: F.warehouse('fac_warehouse_red', BRICK['red'], rgb('#2a3229'), 41, OUT)),
        ('fac_warehouse_soot', lambda: F.warehouse('fac_warehouse_soot', BRICK['soot'], rgb('#3b2a22'), 42, OUT)),
        ('fac_factory', lambda: F.factory('fac_factory', BRICK['brown'], rgb('#2e3330'), 43, OUT)),
        ('gf_shop_green', lambda: F.shopfront('gf_shop_green', rgb('#24392b'), 61, OUT)),
        ('gf_shop_black', lambda: F.shopfront('gf_shop_black', rgb('#1c1c1b'), 62, OUT)),
        ('gf_shop_red', lambda: F.shopfront('gf_shop_red', rgb('#5a1d18'), 63, OUT)),
        ('gf_residential', lambda: F.residential_base('gf_residential', rgb('#8a8377'), rgb('#1f1f1f'), 64, OUT)),
        ('gf_loading', lambda: F.loading_doors('gf_loading', BRICK['red'], rgb('#3a4a35'), 65, OUT)),
        ('gf_lobby', lambda: F.office_lobby('gf_lobby', rgb('#c9c0aa'), 66, OUT)),
        ('gf_basement', lambda: F.basement_brownstone('gf_basement', rgb('#6b4a3a'), 67, OUT)),
        ('wall_stone_lime', lambda: F.stone_plain('wall_stone_lime', rgb('#c9c0aa'), 70, OUT)),
        ('wall_stone_brown', lambda: F.stone_plain('wall_stone_brown', rgb('#6b4a3a'), 71, OUT, rows=8, cols=2)),
        ('asphalt', lambda: S.asphalt(OUT)),
        ('cobbles', lambda: S.cobbles(OUT)),
        ('sidewalk', lambda: S.sidewalk(OUT)),
        ('curb', lambda: S.curb(OUT)),
        ('roof_tar', lambda: S.roof_tar(OUT)),
        ('grass', lambda: S.grass(OUT)),
        ('dirt', lambda: S.dirt(OUT)),
        ('planks', lambda: S.planks(OUT)),
        ('water', lambda: S.water(OUT)),
        ('steel', lambda: S.steel(OUT)),
        ('concrete', lambda: S.concrete(OUT)),
        ('signs', lambda: G.shop_signs(OUT)),
        ('billboards', lambda: G.billboards(OUT)),
        ('smoke', lambda: G.smoke(OUT)),
    ]

    for name, fn in jobs:
        if only and name not in only:
            continue
        t = time.time()
        fn()
        print(f'{name:24s} {time.time() - t:5.1f}s')


if __name__ == '__main__':
    main()
