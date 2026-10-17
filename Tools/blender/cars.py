"""1920s New York vehicles. Front = +Y in Blender. Run via build_models.py."""
import math
from mathutils import Vector
from mbl import Builder

# ---------------------------------------------------------------- shared parts


def rrect(y, hw, z0, z1, r=0.06):
    """Chamfered rectangle section in the XZ plane at Y=y (8 points)."""
    return [(hw, y, z0 + r), (hw, y, z1 - r), (hw - r, y, z1), (-hw + r, y, z1),
            (-hw, y, z1 - r), (-hw, y, z0 + r), (-hw + r, y, z0), (hw - r, y, z0)]


def arc(yc, zc, R, a0, a1, n):
    return [(0.0, yc + R * math.cos(math.radians(a)), zc + R * math.sin(math.radians(a)))
            for a in [a0 + (a1 - a0) * i / (n - 1) for i in range(n)]]


def fender_profile(x_in, x_out):
    return [(x_in, 0.0), (x_out - 0.03, 0.0), (x_out, -0.035), (x_out + 0.008, -0.09),
            (x_out - 0.02, -0.09), (x_out - 0.03, -0.035), (x_in, -0.022)]


def fender_pair(b, path, x_in, x_out, slot):
    prof = fender_profile(x_in, x_out)
    b.sweep(slot, path, prof)
    b.sweep(slot, path, prof, mirror_x=True)


def front_fender_path(yc, R, rb_y, rb_z=0.53, start=6, end=168):
    p = arc(yc, R, R + 0.09, start, end, 13)
    _, y1, z1 = p[-1]
    for t in (0.33, 0.66, 1.0):           # glide down to the running board
        p.append((0.0, y1 + (rb_y - y1) * t, z1 + (rb_z - z1) * (t ** 0.8)))
    return p


def rear_fender_path(yc, R, rb_y, rb_z=0.53, start=18, end=176):
    a = arc(yc, R, R + 0.09, start, end, 12)
    _, y0, z0 = a[0]
    lead = [(0.0, rb_y + (y0 - rb_y) * t, rb_z + (z0 - rb_z) * (t ** 1.3)) for t in (0.0, 0.4, 0.75)]
    return lead + a


def wheel(b, c, R=0.38, width=0.11, axis=(1, 0, 0), spokes=8, kind='spoke', whitewall=False):
    c = Vector(c)
    ax = Vector(axis).normalized()
    tr = 0.055
    tire = [(tr * math.cos(2 * math.pi * k / 6), (width / 2) * math.sin(2 * math.pi * k / 6)) for k in range(6)]
    b.ring('Tire', c, ax, R - tr, tire, segs=14)
    if whitewall:
        b.ring('Whitewall', c + ax * (width / 2 - 0.004), ax, R - 0.09, [(0.03, 0.0), (0.03, 0.012), (-0.03, 0.012), (-0.03, 0.0)], segs=14)
        b.ring('Whitewall', c - ax * (width / 2 - 0.004), ax, R - 0.09, [(0.03, 0.0), (0.03, -0.012), (-0.03, -0.012), (-0.03, 0.0)], segs=14)
    rim_r = R - 2 * tr - 0.012
    if kind == 'spoke':
        b.ring('Spokes', c, ax, rim_r, [(0.022, -0.035), (0.022, 0.035), (-0.022, 0.035), (-0.022, -0.035)], segs=12)
        u = ax.orthogonal().normalized()
        v = ax.cross(u).normalized()
        for k in range(spokes):
            a = 2 * math.pi * k / spokes
            d = u * math.cos(a) + v * math.sin(a)
            b.beam('Spokes', c + d * 0.06, c + d * (rim_r - 0.015), 0.034, 0.03)
        hub_slot = 'Black'
    else:  # disc wheel
        b.cyl('Rim', tuple(c), rim_r + 0.01, 0.07, segs=16, axis=_axis_name(ax))
        hub_slot = 'Chrome'
    b.cyl(hub_slot, tuple(c), 0.075, 0.13, segs=8, axis=_axis_name(ax))
    b.cyl('Chrome', tuple(c), 0.05, 0.17, segs=8, axis=_axis_name(ax))


def _axis_name(ax):
    a = [abs(ax.x), abs(ax.y), abs(ax.z)]
    return 'XYZ'[a.index(max(a))]


def headlamp(b, x, y, z, r=0.11, body='Black', to_z=0.62):
    b.cyl(body, (x, y, z), r, 0.17, segs=14, axis='Y')
    b.cyl('Lens', (x, y + 0.09, z), r * 0.86, 0.02, segs=14, axis='Y')
    b.sphere(body, (x, y - 0.08, z), (r * 0.95, r * 0.7, r * 0.95), 10, 6)
    b.tube(body, (x, y - 0.05, z - r), (x, y - 0.05, to_z), 0.025, 6)


def bumper(b, y, z, hw, slot='Chrome', depth=0.06, h=0.09):
    b.box(slot, (0, y, z), (hw * 2, depth, h), bevel=0.02)
    for sx in (-1, 1):
        b.beam(slot, (sx * 0.42, y, z), (sx * 0.42, y - math.copysign(0.25, y), 0.55), 0.05)


def frame(b, y0, y1, hw=0.42, z=0.53):
    for sx in (-1, 1):
        b.box_mm('Black', (sx * hw - 0.04, y0, z - 0.05), (sx * hw + 0.04, y1, z + 0.05))


def running_boards(b, y0, y1, x_in=0.58, x_out=0.86, z=0.5):
    for sx in (-1, 1):
        lo, hi = sorted((sx * x_in, sx * x_out))
        b.box_mm('Black', (lo, y0, z - 0.025), (hi, y1, z + 0.025), bevel=0.01, segs=1)


def greenhouse(b, y0, y1, hw, z0, z1, pillars, roof_over=0.06, roof_slot='Paint', pillar_slot='Paint'):
    """Glass block + pillars + crowned roof."""
    b.box_mm('Glass', (-hw + 0.02, y0, z0), (hw - 0.02, y1, z1))
    for py in pillars:
        for sx in (-1, 1):
            b.box(pillar_slot, (sx * (hw - 0.005), py, (z0 + z1) / 2), (0.05, 0.07, z1 - z0))
        b.box(pillar_slot, (0, py, z1 - 0.02), (hw * 2, 0.05, 0.05))
    b.box(pillar_slot, (0, y1, z0 + 0.02), (hw * 2 - 0.02, 0.06, 0.06))
    # crowned roof: loft across X
    secs = []
    for i in range(7):
        t = i / 6
        x = -hw - roof_over + t * 2 * (hw + roof_over)
        crown = 0.045 * math.sin(math.pi * t)
        secs.append([(x, y0 - roof_over, z1 - 0.02), (x, y1 + roof_over * 1.6, z1 - 0.02),
                     (x, y1 + roof_over * 1.6, z1 + 0.05 + crown), (x, y0 - roof_over, z1 + 0.05 + crown)])
    b.loft(roof_slot, secs, closed=True)


def hood(b, y0, y1, hw, z0, z1, louvers=6, slot='Paint'):
    b.loft(slot, [rrect(y0, hw, z0, z1, 0.09), rrect(y1, hw * 0.97, z0, z1 - 0.01, 0.09)])
    b.box('Chrome', (0, (y0 + y1) / 2, z1 + 0.005), (0.025, y1 - y0, 0.02))
    for k in range(louvers):
        y = y0 + 0.12 + k * (y1 - y0 - 0.24) / max(1, louvers - 1)
        for sx in (-1, 1):
            b.box('Black', (sx * (hw + 0.004), y, (z0 + z1) / 2 + 0.03), (0.012, 0.025, (z1 - z0) * 0.45))


def radiator(b, y, hw, z0, z1, shell='Black', cap=True):
    b.box(shell, (0, y + 0.06, (z0 + z1) / 2), (hw * 2, 0.12, z1 - z0), bevel=0.03)
    b.box('Glass', (0, y + 0.125, (z0 + z1) / 2 + 0.01), (hw * 1.6, 0.012, (z1 - z0) * 0.82))
    for k in range(7):
        x = -hw * 0.8 + k * hw * 1.6 / 6
        b.box(shell, (x, y + 0.128, (z0 + z1) / 2 + 0.01), (0.012, 0.01, (z1 - z0) * 0.82))
    if cap:
        b.cyl('Chrome', (0, y + 0.06, z1 + 0.03), 0.035, 0.06, segs=8)


def cowl(b, y0, y1, body_hw, body_z0, body_z1, hood_hw, hood_z0, hood_z1, slot='Paint'):
    b.loft(slot, [rrect(y0, body_hw, body_z0, body_z1, 0.07), rrect(y1, hood_hw, hood_z0, hood_z1, 0.09)])


# ---------------------------------------------------------------- vehicles


def sedan():
    b = Builder('car_sedan', ['Paint', 'Black', 'Chrome', 'Glass', 'Tire', 'Spokes', 'Lens', 'Tail'])
    FY, RY, R, WX = 1.27, -1.27, 0.38, 0.72
    frame(b, -1.75, 1.8)
    running_boards(b, -0.68, 0.66)
    b.box_mm('Paint', (-0.66, -1.62, 0.58), (0.66, 0.56, 1.30), bevel=0.06)
    b.box('Black', (0, -0.53, 1.30), (1.34, 2.2, 0.03))                       # beltline
    cowl(b, 0.55, 0.88, 0.66, 0.62, 1.30, 0.43, 0.72, 1.24)
    hood(b, 0.88, 1.74, 0.43, 0.72, 1.24)
    radiator(b, 1.74, 0.44, 0.62, 1.32)
    greenhouse(b, -1.48, 0.5, 0.64, 1.30, 1.88, pillars=(0.5, -0.42, -1.46))
    for sx in (-1, 1):
        b.box('Chrome', (sx * 0.665, 0.25, 1.12), (0.02, 0.1, 0.025))         # door handles
        b.box('Chrome', (sx * 0.665, -0.95, 1.12), (0.02, 0.1, 0.025))
    fender_pair(b, front_fender_path(FY, R, 0.66), 0.58, 0.86, 'Black')
    fender_pair(b, rear_fender_path(RY, R, -0.68), 0.58, 0.86, 'Black')
    for sx in (-1, 1):
        headlamp(b, sx * 0.52, 1.66, 1.0)
        for y in (FY, RY):
            wheel(b, (sx * WX, y, R), R)
    b.tube('Chrome', (-0.52, 1.6, 0.92), (0.52, 1.6, 0.92), 0.018)
    bumper(b, 2.02, 0.52, 0.82)
    bumper(b, -1.86, 0.52, 0.78)
    wheel(b, (0, -1.74, 0.86), R, axis=(0, 1, 0))                                 # spare on the back
    b.box('Tail', (-0.52, -1.64, 0.78), (0.08, 0.05, 0.07))
    return b.finish()


def limousine():
    b = Builder('car_limo', ['Paint', 'Paint2', 'Chrome', 'Glass', 'Tire', 'Rim', 'Lens', 'Tail', 'Whitewall'])
    FY, RY, R, WX = 1.78, -1.62, 0.42, 0.76
    frame(b, -2.3, 2.3, hw=0.45)
    running_boards(b, -1.15, 0.28, 0.62, 0.92)
    b.box_mm('Paint2', (-0.72, -2.18, 0.56), (0.72, 0.36, 0.96), bevel=0.05)
    b.box_mm('Paint', (-0.71, -2.15, 0.94), (0.71, 0.36, 1.24), bevel=0.05)
    b.box('Chrome', (0, -0.9, 0.955), (1.45, 2.56, 0.025))                    # beltline moulding
    cowl(b, 0.35, 0.62, 0.72, 0.6, 1.24, 0.46, 0.72, 1.2)
    hood(b, 0.62, 2.12, 0.46, 0.72, 1.2, louvers=12)
    radiator(b, 2.12, 0.46, 0.6, 1.3, shell='Chrome')
    b.sphere('Chrome', (0, 2.18, 1.38), (0.03, 0.08, 0.05), 8, 5)               # mascot
    greenhouse(b, -2.0, 0.3, 0.7, 1.24, 1.76, pillars=(0.3, -0.75, -1.98), roof_slot='Paint2', pillar_slot='Paint')
    fender_pair(b, front_fender_path(FY, R, 0.28, start=4, end=170), 0.6, 0.93, 'Paint2')
    fender_pair(b, rear_fender_path(RY, R, -1.15), 0.6, 0.93, 'Paint2')
    for sx in (-1, 1):
        headlamp(b, sx * 0.56, 2.02, 1.02, r=0.13, body='Chrome', to_z=0.66)
        wheel(b, (sx * 0.83, 0.55, 0.86), R, axis=(1, 0, 0.18), kind='disc', whitewall=True)   # side-mounted spares
        b.cyl('Paint2', (sx * 0.9, 0.55, 0.87), R - 0.06, 0.04, segs=16, axis='X')
        for y in (FY, RY):
            wheel(b, (sx * WX, y, R), R, kind='disc', whitewall=True)
        for y in (-0.15, -1.25):
            b.box('Chrome', (sx * 0.725, y, 1.1), (0.02, 0.12, 0.025))
    b.tube('Chrome', (-0.56, 1.96, 0.9), (0.56, 1.96, 0.9), 0.02)
    bumper(b, 2.42, 0.54, 0.88)
    b.box('Chrome', (0, 2.42, 0.42), (1.7, 0.05, 0.05), bevel=0.015)
    bumper(b, -2.6, 0.54, 0.85)
    b.box_mm('Paint2', (-0.62, -2.55, 0.72), (0.62, -2.2, 1.18), bevel=0.04)  # trunk on the rack
    b.box('Chrome', (0, -2.38, 0.68), (1.2, 0.4, 0.03))
    for sx in (-1, 1):
        b.box('Tail', (sx * 0.62, -2.2, 0.8), (0.07, 0.05, 0.07))
    return b.finish()


def touring():
    b = Builder('car_touring', ['Paint', 'Black', 'Chrome', 'Glass', 'Tire', 'Spokes', 'Lens', 'Leather', 'Canvas', 'Tail'])
    FY, RY, R, WX = 1.27, -1.27, 0.38, 0.72
    frame(b, -1.75, 1.8)
    running_boards(b, -0.68, 0.66)
    # open tub: floor + walls
    b.box_mm('Black', (-0.62, -1.58, 0.6), (0.62, 0.5, 0.78))
    b.box_mm('Paint', (0.6, -1.62, 0.58), (0.67, 0.55, 1.2), bevel=0.02)
    b.box_mm('Paint', (-0.67, -1.62, 0.58), (-0.6, 0.55, 1.2), bevel=0.02)
    b.box_mm('Paint', (-0.67, -1.66, 0.58), (0.67, -1.5, 1.22), bevel=0.04)
    cowl(b, 0.5, 0.88, 0.67, 0.6, 1.22, 0.43, 0.72, 1.24)
    hood(b, 0.88, 1.74, 0.43, 0.72, 1.24)
    radiator(b, 1.74, 0.44, 0.62, 1.32)
    # seats
    for (y, back_y) in ((-0.2, -0.38), (-1.1, -1.38)):
        b.box('Leather', (0, y, 0.9), (1.18, 0.5, 0.22), bevel=0.05)
        b.box('Leather', (0, back_y, 1.18), (1.18, 0.14, 0.62), bevel=0.05)
    # windscreen
    b.box('Glass', (0, 0.52, 1.43), (1.2, 0.02, 0.42))
    for sx in (-1, 1):
        b.tube('Chrome', (sx * 0.61, 0.52, 1.2), (sx * 0.61, 0.52, 1.66), 0.015)
    b.tube('Chrome', (-0.61, 0.52, 1.65), (0.61, 0.52, 1.65), 0.015)
    # steering wheel
    b.ring('Black', (-0.32, 0.22, 1.32), (0, 0.6, 0.8), 0.18, [(0.012, -0.012), (0.012, 0.012), (-0.012, 0.012), (-0.012, -0.012)], segs=14)
    b.tube('Black', (-0.32, 0.22, 1.32), (-0.32, 0.45, 1.0), 0.02)
    # folded top
    b.box('Canvas', (0, -1.66, 1.32), (1.3, 0.34, 0.26), bevel=0.1, segs=3)
    for sx in (-1, 1):
        b.tube('Black', (sx * 0.64, -1.55, 1.2), (sx * 0.64, -1.62, 1.4), 0.015)
    fender_pair(b, front_fender_path(FY, R, 0.66), 0.58, 0.86, 'Black')
    fender_pair(b, rear_fender_path(RY, R, -0.68), 0.58, 0.86, 'Black')
    for sx in (-1, 1):
        headlamp(b, sx * 0.52, 1.66, 1.0)
        for y in (FY, RY):
            wheel(b, (sx * WX, y, R), R)
    b.tube('Chrome', (-0.52, 1.6, 0.92), (0.52, 1.6, 0.92), 0.018)
    bumper(b, 2.02, 0.52, 0.82)
    wheel(b, (0, -1.86, 0.86), R, axis=(0, 1, 0))
    b.box('Tail', (-0.52, -1.68, 0.78), (0.08, 0.05, 0.07))
    return b.finish()


def _truck_front(b, R):
    FY = 1.62
    frame(b, -3.0, 2.15, hw=0.45)
    b.box_mm('Black', (-0.6, -0.1, 0.62), (0.6, 0.15, 0.74))
    cowl(b, 0.95, 1.12, 0.62, 0.7, 1.35, 0.44, 0.78, 1.3)
    hood(b, 1.12, 2.0, 0.44, 0.78, 1.3)
    radiator(b, 2.0, 0.45, 0.66, 1.4)
    # cab
    b.box_mm('Paint', (-0.68, 0.15, 0.66), (0.68, 0.98, 1.38), bevel=0.04)
    greenhouse(b, 0.22, 0.95, 0.66, 1.38, 1.98, pillars=(0.95, 0.24), roof_over=0.08)
    fender_pair(b, front_fender_path(FY, R, 0.9, rb_z=0.6), 0.55, 0.86, 'Black')
    running_boards(b, 0.2, 0.9, 0.55, 0.86, z=0.58)
    for sx in (-1, 1):
        headlamp(b, sx * 0.55, 1.92, 1.06, to_z=0.7)
        wheel(b, (sx * 0.74, FY, R), R)
    bumper(b, 2.3, 0.56, 0.85)
    return FY


def stake_truck():
    b = Builder('truck_stake', ['Paint', 'Black', 'Chrome', 'Glass', 'Tire', 'Spokes', 'Lens', 'Wood', 'Steel', 'Tail', 'Tarp'])
    R = 0.42
    _truck_front(b, R)
    RY = -1.7
    b.box_mm('Wood', (-1.0, -3.15, 0.84), (1.0, 0.1, 0.94))
    for sx in (-1, 1):
        for y in [-3.1 + i * 0.4 for i in range(9)]:
            b.box('Wood', (sx * 0.97, y, 1.32), (0.06, 0.06, 0.76))
        for z in (1.25, 1.62):
            b.box('Wood', (sx * 0.99, -1.52, z), (0.04, 3.28, 0.1))
    for x in [-0.9 + i * 0.36 for i in range(6)]:
        b.box('Wood', (x, -3.12, 1.32), (0.06, 0.06, 0.76))
    for z in (1.25, 1.62):
        b.box('Wood', (0, -3.14, z), (2.0, 0.04, 0.1))
    # rear mudguards + dual wheels
    for sx in (-1, 1):
        b.box('Black', (sx * 0.8, RY, 0.97), (0.5, 1.05, 0.04), bevel=0.01, segs=1)
        wheel(b, (sx * 0.62, RY, R), R, width=0.12)
        wheel(b, (sx * 0.86, RY, R), R, width=0.12)
    # cargo: crates, barrels, a tarp-covered heap
    crates = [(-0.55, -0.45, 0.6), (0.05, -0.5, 0.55), (0.5, -0.6, 0.5), (-0.5, -1.15, 0.5), (-0.5, -1.15, 0.5, 1)]
    for c in crates:
        x, y, s = c[0], c[1], c[2]
        z = 0.94 + s / 2 + (0.5 if len(c) > 3 else 0)
        b.box('Wood', (x, y, z), (s, s, s), bevel=0.01, segs=1)
    for (x, y) in ((0.35, -1.3), (0.6, -1.85), (0.1, -1.95), (-0.6, -2.4)):
        b.cyl('Wood', (x, y, 0.94 + 0.42), 0.27, 0.84, segs=12)
        for dz in (-0.25, 0.25):
            b.cyl('Steel', (x, y, 0.94 + 0.42 + dz), 0.28, 0.05, segs=12)
    b.box('Tarp', (-0.1, -2.75, 1.25), (1.6, 0.6, 0.6), bevel=0.2, segs=3)
    for sx in (-1, 1):
        b.box('Tail', (sx * 0.9, -3.17, 0.82), (0.07, 0.04, 0.07))
    return b.finish()


def box_van():
    b = Builder('truck_van', ['Paint', 'Black', 'Chrome', 'Glass', 'Tire', 'Spokes', 'Lens', 'Box', 'Tail'])
    R = 0.42
    _truck_front(b, R)
    RY = -1.7
    b.box_mm('Box', (-1.02, -3.15, 0.86), (1.02, 0.12, 2.38), bevel=0.05)
    b.box('Black', (0, -1.52, 2.4), (2.08, 3.3, 0.05), bevel=0.02)           # roof edge
    for sx in (-1, 1):
        b.box('Black', (sx * 1.025, -1.52, 0.95), (0.02, 3.2, 0.06))           # rub strip
    b.box('Black', (0, -3.155, 1.6), (0.02, 0.02, 1.4))                        # rear door split
    for sx in (-1, 1):
        b.box('Chrome', (sx * 0.12, -3.16, 1.5), (0.03, 0.02, 0.2))
        b.box('Black', (sx * 0.8, RY, 0.95), (0.5, 1.05, 0.04))
        wheel(b, (sx * 0.62, RY, R), R, width=0.12)
        wheel(b, (sx * 0.86, RY, R), R, width=0.12)
        b.box('Tail', (sx * 0.9, -3.17, 0.82), (0.07, 0.04, 0.07))
    return b.finish()


ALL = {'car_sedan': sedan, 'car_limo': limousine, 'car_touring': touring, 'truck_stake': stake_truck, 'truck_van': box_van}
