"""Harbour boats: freighter, tugboat, rum-runner, barge. Bow = +Y, waterline at Z = 0."""
import math
import bmesh
from mathutils import Vector
from mbl import Builder


def smooth01(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3 - 2 * x)


class Hull:
    """Lofted hull. s = 0 stern .. 1 bow, t = 0 keel .. 1 deck edge."""

    def __init__(self, L, B, draft, deck, sb=0.72, ss=0.18, bow_rake=2.0, stern_over=1.0,
                 v_shape=0.45, fine_bow=1.3, flare=0.9, transom=0.0, ns=30, nv=9):
        self.L, self.B, self.draft, self.deck = L, B, draft, deck
        self.sb, self.ss, self.bow_rake, self.stern_over = sb, ss, bow_rake, stern_over
        self.v_shape, self.fine_bow, self.flare, self.transom = v_shape, fine_bow, flare, transom
        self.ns, self.nv = ns, nv

    def half_beam(self, s, t):
        wv = 1 - (1 - min(t / 0.32, 1.0)) ** 2.2 if self.v_shape >= 0.4 else t ** self.v_shape
        if s > self.sb:
            q = (s - self.sb) / (1 - self.sb)
            p = self.fine_bow + self.flare * t
            wl = max(0.0, 1 - q ** p) ** 0.55
        elif s < self.ss:
            q = (self.ss - s) / self.ss
            if self.transom > 0:
                wl = 1 - (1 - self.transom) * q ** 1.6
            else:
                wl = max(0.0, 1 - q ** (1.8 + 0.8 * t)) ** 0.5 * (0.75 + 0.25 * t)
        else:
            wl = 1.0
        return max(0.03, self.B / 2 * wv * wl)

    def point(self, s, t, side):
        zk = -self.draft
        zd = self.deck(s)
        z = zk + (zd - zk) * t
        y = -self.L / 2 + s * self.L
        if s > self.sb:
            y += self.bow_rake * t * smooth01((s - self.sb) / (1 - self.sb))
        if s < self.ss and self.transom <= 0:
            y -= self.stern_over * t * smooth01((self.ss - s) / self.ss)
        return Vector((side * self.half_beam(s, t), y, z))

    def deck_edge(self, side, t=1.0, n=None):
        n = n or self.ns
        return [self.point(i / (n - 1), t, side) for i in range(n)]

    def build(self, b, hull_slot='Hull', bottom='Bottom', boot='Boot', deck='Deck', boot_h=0.25):
        ns, nv = self.ns, self.nv
        # cluster stations towards the ends for smooth bow/stern
        ss_list = [0.5 - 0.5 * math.cos(math.pi * i / (ns - 1)) for i in range(ns)]
        secs = []
        for s in ss_list:
            right = [self.point(s, j / (nv - 1), 1) for j in range(nv)]
            left = [self.point(s, j / (nv - 1), -1) for j in reversed(range(1, nv))]
            secs.append(right + left)
        bm = bmesh.new()
        rings = [[bm.verts.new(p) for p in sec] for sec in secs]
        n = len(secs[0])
        for r0, r1 in zip(rings, rings[1:]):
            for i in range(n):
                j = (i + 1) % n
                try:
                    bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
                except ValueError:
                    pass
        for ring in (rings[0], rings[-1]):
            try:
                bm.faces.new(ring)
            except ValueError:
                pass
        bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-4)

        def classify(f):
            c = f.calc_center_median()
            nz = f.normal.z
            if nz > 0.6 and c.z > self.deck(0.5) - 0.3:
                return deck
            if c.z < 0:
                return bottom
            if c.z < boot_h:
                return boot
            return hull_slot
        b.add_classified(bm, classify)


def rails(b, hull, slot, h=1.0, every=1.6, s0=0.02, s1=0.98, t=1.0):
    """Stanchions + two rails along both deck edges."""
    n = max(4, int(hull.L * (s1 - s0) / every))
    for side in (-1, 1):
        pts = [hull.point(s0 + (s1 - s0) * i / (n - 1), t, side) for i in range(n)]
        pts = [p + Vector((-side * 0.08, 0, 0)) for p in pts]
        for p in pts:
            b.beam(slot, p, p + Vector((0, 0, h)), 0.05)
        for hh in (h * 0.5, h):
            for a, c in zip(pts, pts[1:]):
                b.beam(slot, a + Vector((0, 0, hh)), c + Vector((0, 0, hh)), 0.04)


def window_strip(b, y0, y1, x, z, h, slot='CabinGlow', side_both=True, n=None, w=0.55):
    """Row of windows on the sides of a deckhouse (slightly proud of the wall)."""
    n = n or max(1, int(abs(y1 - y0) / 1.2))
    for i in range(n):
        y = y0 + (i + 0.5) * (y1 - y0) / n
        for sx in ((-1, 1) if side_both else (1,)):
            b.box(slot, (sx * (x + 0.01), y, z), (0.04, w, h))


def lifeboat(b, center, length=4.2, beam=1.3, slot='Super'):
    cx, cy, cz = center
    secs = []
    for i in range(9):
        s = i / 8
        hb = beam / 2 * math.sin(math.pi * (0.08 + 0.84 * s)) ** 0.7
        y = cy - length / 2 + s * length
        secs.append([(cx + hb, y, cz + 0.5), (cx + hb * 0.7, y, cz), (cx - hb * 0.7, y, cz), (cx - hb, y, cz + 0.5)])
    b.loft(slot, secs)
    b.box('Tarp', (cx, cy, cz + 0.52), (beam * 0.85, length * 0.8, 0.06))


def cowl_vent(b, x, y, z, h=1.6, r=0.25, face=1):
    b.cyl('Super', (x, y, z + h / 2), r, h, segs=10)
    b.sphere('Super', (x, y + face * 0.12, z + h + 0.1), (r * 1.4, r * 1.4, r * 1.3), 10, 6)
    b.cyl('Black', (x, y + face * 0.33, z + h + 0.12), r * 1.05, 0.05, segs=10, axis='Y')


# ---------------------------------------------------------------- freighter

def freighter():
    b = Builder('boat_freighter', ['Hull', 'Bottom', 'Boot', 'Deck', 'Super', 'Funnel', 'Band', 'Black', 'Steel',
                                   'Tarp', 'Glass', 'CabinGlow', 'Lamp', 'Rope', 'Wood'])
    L, B = 40.0, 7.6

    def deck(s):
        return 3.2 + 2.0 * smooth01((s - 0.8) / 0.2) + 1.0 * smooth01((0.15 - s) / 0.15)

    hull = Hull(L, B, 2.8, deck, sb=0.70, ss=0.16, bow_rake=2.6, stern_over=1.4)
    hull.build(b)
    Y = lambda s: -L / 2 + s * L
    D = deck(0.5)

    rails(b, hull, 'Super', h=0.9, every=2.0)

    # forecastle + poop decks (raised deck surfaces drawn by the sheer) - add bulwark caps there
    for (s0, s1) in ((0.84, 0.99),):
        for side in (-1, 1):
            pts = [hull.point(s0 + (s1 - s0) * i / 6, 1.0, side) for i in range(7)]
            for a, c in zip(pts, pts[1:]):
                b.beam('Hull', a + Vector((-side * 0.1, 0, 0.45)), c + Vector((-side * 0.1, 0, 0.45)), 0.18, 0.9)

    # cargo hatches with tarpaulins
    for s in (0.22, 0.32, 0.68, 0.78):
        b.box('Steel', (0, Y(s), D + 0.45), (3.6, 3.0, 0.9), bevel=0.04, segs=1)
        b.box('Tarp', (0, Y(s), D + 0.95), (3.3, 2.7, 0.18), bevel=0.06)

    # masts, crosstrees, cargo booms, stays
    for s, face in ((0.27, 1), (0.73, -1)):
        y = Y(s)
        top = D + 13.0
        b.cyl('Super', (0, y, D + 6.5), 0.22, 13.0, segs=10, radius2=0.14)
        b.box('Super', (0, y, D + 9.5), (3.0, 0.18, 0.18))
        b.cyl('Black', (0, y, D + 1.2), 0.5, 1.0, segs=10)              # mast house
        for sx in (-1, 1):
            b.tube('Super', (sx * 0.35, y, D + 1.6), (sx * 0.9, y + face * 6.0, D + 5.2), 0.1, 6)
            b.tube('Black', (0, y, top), (sx * hull.half_beam(s, 1) * 0.9, y - face * 0.3, D + 0.9), 0.025, 4)
        b.tube('Black', (0, y, top), (0, Y(0.99 if face == -1 else 0.02), deck(0.99 if face == -1 else 0.02) + 0.9), 0.03, 4)
        b.sphere('Lamp', (0, y, top + 0.2), (0.18, 0.18, 0.18), 8, 5)

    # midship superstructure
    y0, y1 = Y(0.42), Y(0.6)
    hw = B / 2 - 0.7
    b.box_mm('Super', (-hw, y0, D), (hw, y1, D + 2.5), bevel=0.05, segs=1)
    window_strip(b, y0 + 0.5, y1 - 0.5, hw, D + 1.5, 0.55)
    b.box_mm('Deck', (-hw - 0.3, y0 - 0.3, D + 2.5), (hw + 0.3, y1 + 0.3, D + 2.65))
    by0, by1 = Y(0.5), Y(0.6)
    b.box_mm('Super', (-hw + 0.6, by0, D + 2.65), (hw - 0.6, by1, D + 4.9), bevel=0.05, segs=1)
    window_strip(b, by0 + 0.4, by1 - 0.4, hw - 0.6, D + 3.9, 0.55)
    b.box_mm('Deck', (-B / 2 - 0.2, by1 - 1.2, D + 4.9), (B / 2 + 0.2, by1 + 0.1, D + 5.05))   # bridge wings
    b.box_mm('Super', (-1.7, by1 - 2.2, D + 5.05), (1.7, by1 - 0.1, D + 7.0), bevel=0.04, segs=1)  # wheelhouse
    b.box('Glass', (0, by1 - 0.08, D + 6.25), (3.0, 0.04, 0.9))
    for k in range(5):
        b.box('CabinGlow', (-1.2 + k * 0.6, by1 - 0.06, D + 6.25), (0.45, 0.03, 0.75))
    b.box_mm('Black', (-1.9, by1 - 2.4, D + 7.0), (1.9, by1 + 0.15, D + 7.12))
    # funnel
    fy = Y(0.45)
    b.cyl('Funnel', (0, fy, D + 2.65 + 3.3), 1.05, 6.6, segs=16)
    b.cyl('Band', (0, fy, D + 2.65 + 6.6 + 0.5), 1.06, 1.0, segs=16)
    b.cyl('Black', (0, fy, D + 2.65 + 7.05), 0.95, 0.12, segs=16)
    b.tube('Steel', (0.6, fy - 0.9, D + 2.65), (0.6, fy - 0.9, D + 10.2), 0.12, 6)       # steam pipe
    cowl_vent(b, 2.0, Y(0.4), D, face=1)
    cowl_vent(b, -2.0, Y(0.4), D, face=1)
    cowl_vent(b, 0, Y(0.62), D, h=1.2, face=-1)
    # lifeboats on davits
    for sx in (-1, 1):
        for s in (0.44, 0.53):
            x = sx * (hw - 0.4)
            lifeboat(b, (x, Y(s) + 1.1, D + 3.1))
            for dy in (-0.7, 2.9):
                b.tube('Black', (sx * (hw - 0.9), Y(s) + dy, D + 2.65), (x, Y(s) + dy, D + 4.6), 0.06, 6)
    # poop house aft + flagpole
    b.box_mm('Super', (-2.2, Y(0.05), deck(0.08)), (2.2, Y(0.14), deck(0.08) + 2.0), bevel=0.04, segs=1)
    window_strip(b, Y(0.06), Y(0.13), 2.2, deck(0.08) + 1.2, 0.45, n=3)
    b.tube('Black', (0, Y(0.015), deck(0.01)), (0, Y(0.0) - 0.3, deck(0.01) + 3.5), 0.04, 6)
    # windlass + anchors
    b.box('Black', (0, Y(0.92), deck(0.92) + 0.4), (2.0, 1.0, 0.8), bevel=0.05)
    for sx in (-1, 1):
        p = hull.point(0.95, 0.85, sx)
        b.box('Black', (p.x + sx * 0.1, p.y, p.z), (0.2, 0.8, 0.9), bevel=0.05)
    return b.finish()


# ---------------------------------------------------------------- tug

def tugboat():
    b = Builder('boat_tug', ['Hull', 'Bottom', 'Boot', 'Deck', 'Super', 'Funnel', 'Band', 'Black', 'Rope',
                             'Glass', 'CabinGlow', 'Lamp', 'Tire', 'Red', 'Steel'])
    L, B = 16.0, 5.4

    def deck(s):
        return 1.5 + 1.1 * smooth01((s - 0.72) / 0.28) + 0.25 * smooth01((0.2 - s) / 0.2)

    hull = Hull(L, B, 1.9, deck, sb=0.62, ss=0.22, bow_rake=0.9, stern_over=0.9, fine_bow=1.6, flare=0.7, ns=26)
    hull.build(b, boot_h=0.3)
    Y = lambda s: -L / 2 + s * L
    D = deck(0.5)
    # bulwark (low wall) + heavy rope fender along the sheer
    for side in (-1, 1):
        pts = [hull.point(0.02 + 0.96 * i / 15, 1.0, side) for i in range(16)]
        for a, c in zip(pts, pts[1:]):
            b.beam('Hull', a + Vector((-side * 0.08, 0, 0.35)), c + Vector((-side * 0.08, 0, 0.35)), 0.14, 0.7)
        fend = [hull.point(0.03 + 0.94 * i / 19, 0.82, side) + Vector((side * 0.12, 0, 0)) for i in range(20)]
        for a, c in zip(fend, fend[1:]):
            b.tube('Rope', a, c, 0.16, 8)
        for s in (0.25, 0.4, 0.55):
            p = hull.point(s, 0.6, side)
            b.ring('Tire', (p.x + side * 0.18, p.y, p.z), (1, 0, 0), 0.32, [(0.1, -0.08), (0.1, 0.08), (-0.1, 0.08), (-0.1, -0.08)], segs=10)
    # bow pudding
    stem = hull.point(0.995, 0.75, 1)
    b.sphere('Rope', (0, stem.y + 0.25, stem.z), (0.55, 0.45, 0.6), 10, 6)
    b.sphere('Rope', (0, stem.y + 0.05, stem.z - 0.7), (0.4, 0.4, 0.5), 10, 6)

    # deckhouse + wheelhouse
    y0, y1 = Y(0.3), Y(0.68)
    b.box_mm('Super', (-1.75, y0, D), (1.75, y1, D + 2.1), bevel=0.06, segs=1)
    window_strip(b, y0 + 0.4, y1 - 0.4, 1.75, D + 1.35, 0.5, w=0.45)
    b.box_mm('Black', (-1.95, y0 - 0.2, D + 2.1), (1.95, y1 + 0.2, D + 2.22))
    wy0, wy1 = Y(0.56), Y(0.7)
    b.box_mm('Super', (-1.5, wy0, D + 2.22), (1.5, wy1, D + 4.1), bevel=0.05, segs=1)
    b.box_mm('Glass', (-1.52, wy0 + 0.25, D + 3.0), (1.52, wy1 - 0.15, D + 3.75))
    for k in range(4):
        b.box('CabinGlow', (-1.05 + k * 0.7, wy1 + 0.01, D + 3.38), (0.5, 0.04, 0.6))
    b.box_mm('Black', (-1.75, wy0 - 0.2, D + 4.1), (1.75, wy1 + 0.35, D + 4.25))
    b.ring('Red', (1.53, (wy0 + wy1) / 2, D + 2.7), (1, 0, 0), 0.28, [(0.06, -0.04), (0.06, 0.04), (-0.06, 0.04), (-0.06, -0.04)], segs=12)
    # funnel
    fy = Y(0.44)
    b.cyl('Funnel', (0, fy, D + 2.22 + 1.7), 0.78, 3.4, segs=14)
    b.cyl('Band', (0, fy, D + 2.22 + 2.4), 0.79, 0.5, segs=14)
    b.cyl('Black', (0, fy, D + 2.22 + 3.35), 0.8, 0.25, segs=14)
    # mast + lights
    b.tube('Black', (0, Y(0.66), D + 4.25), (0, Y(0.66), D + 7.2), 0.07, 6)
    b.box('Black', (0, Y(0.66), D + 6.4), (1.0, 0.08, 0.08))
    for z in (6.7, 7.2):
        b.sphere('Lamp', (0, Y(0.66), D + z), (0.12, 0.12, 0.12), 8, 5)
    # towing bitt + hook aft
    b.box('Black', (0, Y(0.2), D + 0.5), (1.2, 0.35, 1.0))
    for sx in (-1, 1):
        b.cyl('Black', (sx * 0.5, Y(0.2), D + 0.6), 0.18, 1.2, segs=8)
    b.ring('Rope', (0, Y(0.12), D + 0.12), (0, 0, 1), 0.45, [(0.06, -0.06), (0.06, 0.06), (-0.06, 0.06), (-0.06, -0.06)], segs=12)
    b.ring('Rope', (0, Y(0.12), D + 0.24), (0, 0, 1), 0.38, [(0.06, -0.06), (0.06, 0.06), (-0.06, 0.06), (-0.06, -0.06)], segs=12)
    return b.finish()


# ---------------------------------------------------------------- rum-runner speedboat

def rumrunner():
    b = Builder('boat_rumrunner', ['Mahogany', 'Bottom', 'Boot', 'Deck', 'Chrome', 'Glass', 'Leather', 'Black',
                                   'Wood', 'Canvas', 'Lamp', 'Steel'])
    L, B = 10.0, 2.5

    def deck(s):
        return 0.75 + 0.25 * smooth01((s - 0.6) / 0.4)

    hull = Hull(L, B, 0.5, deck, sb=0.55, ss=0.06, bow_rake=0.6, stern_over=0.0, v_shape=0.6,
                fine_bow=1.4, flare=0.3, transom=0.85, ns=24, nv=8)
    hull.build(b, hull_slot='Mahogany', deck='Mahogany', boot='Boot', boot_h=0.12)
    Y = lambda s: -L / 2 + s * L
    # deck seams (white caulking lines)
    for x in (-0.6, -0.3, 0.0, 0.3, 0.6):
        b.box('Boot', (x, Y(0.8), deck(0.8) + 0.005), (0.025, L * 0.35, 0.01))
    # cockpits: dark wells with leather bench seats
    for s in (0.5, 0.34):
        y = Y(s)
        b.box('Black', (0, y, deck(s) + 0.02), (1.9, 1.5, 0.05))
        b.box('Leather', (0, y - 0.35, deck(s) + 0.2), (1.8, 0.55, 0.32), bevel=0.06)
        b.box('Leather', (0, y - 0.62, deck(s) + 0.45), (1.8, 0.14, 0.55), bevel=0.05)
    # steering wheel + V windscreen
    b.ring('Chrome', (-0.45, Y(0.56), deck(0.56) + 0.6), (0, 0.6, 0.8), 0.2, [(0.015, -0.015), (0.015, 0.015), (-0.015, 0.015), (-0.015, -0.015)], segs=12)
    wy = Y(0.6)
    for sx in (-1, 1):
        a = Vector((sx * 0.02, wy + 0.35, deck(0.6) + 0.05))
        c = Vector((sx * 1.05, wy, deck(0.6) + 0.05))
        mid = (a + c) / 2
        d = (c - a)
        b.loft('Glass', [[a, a + Vector((0, -0.1, 0.45))], [c, c + Vector((0, -0.1, 0.45))]], closed=False, smooth=False)
        b.loft('Glass', [[a + Vector((0, -0.01, 0)), a + Vector((0, -0.11, 0.45))], [c + Vector((0, -0.01, 0)), c + Vector((0, -0.11, 0.45))]], closed=False, smooth=False)
        b.tube('Chrome', a + Vector((0, -0.1, 0.45)), c + Vector((0, -0.1, 0.45)), 0.02, 6)
        b.tube('Chrome', c, c + Vector((0, -0.1, 0.45)), 0.02, 6)
    # engine hatch with chrome vents
    b.box('Mahogany', (0, Y(0.42), deck(0.42) + 0.12), (1.2, 0.9, 0.22), bevel=0.04)
    for k in range(4):
        b.box('Chrome', (-0.3 + k * 0.2, Y(0.42), deck(0.42) + 0.24), (0.05, 0.6, 0.02))
    # contraband: crates and a tarp in the stern
    for i, (x, y) in enumerate(((-0.5, 0.12), (0.0, 0.12), (0.5, 0.12), (-0.25, 0.19), (0.25, 0.19), (0.0, 0.05))):
        z = deck(0.12) + 0.22 + (0.42 if i == 5 else 0)
        b.box('Wood', (x, Y(y), z), (0.45, 0.6, 0.42), bevel=0.01, segs=1)
    b.box('Canvas', (0.25, Y(0.1), deck(0.1) + 0.55), (1.2, 1.0, 0.25), bevel=0.1, segs=2)
    # chrome cleats, bow light, flagstaff
    for s in (0.95, 0.04):
        b.box('Chrome', (0, Y(s), deck(s) + 0.05), (0.08, 0.25, 0.06))
    b.sphere('Lamp', (0, Y(0.9), deck(0.9) + 0.15), (0.06, 0.06, 0.06), 6, 4)
    b.tube('Chrome', (0, Y(0.01), deck(0.01)), (0, Y(0.0) - 0.15, deck(0.01) + 1.0), 0.015, 5)
    return b.finish()


# ---------------------------------------------------------------- barge (coal scow)

def barge():
    b = Builder('boat_barge', ['Wood', 'Bottom', 'Deck', 'Black', 'Coal', 'Super', 'CabinGlow', 'Steel'])
    L, B, Dk, draft = 24.0, 8.0, 1.2, 1.3
    secs = []
    for i in range(7):
        s = i / 6
        y = -L / 2 + s * L
        rake = 2.2
        yb = y
        if s == 0:
            yb = y + rake
        elif s == 1:
            yb = y - rake
        secs.append([(B / 2, y, Dk), (-B / 2, y, Dk), (-B / 2, yb, -draft), (B / 2, yb, -draft)])
    bm = bmesh.new()
    rings = [[bm.verts.new(Vector(p)) for p in sec] for sec in secs]
    for r0, r1 in zip(rings, rings[1:]):
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
    bm.faces.new(rings[0])
    bm.faces.new(rings[-1])

    def cls(f):
        c = f.calc_center_median()
        if f.normal.z > 0.6 and c.z > Dk - 0.1:
            return 'Deck'
        return 'Bottom' if c.z < -0.1 else 'Wood'
    b.add_classified(bm, cls, smooth=False)
    # coaming walls + coal heaps
    b.box_mm('Wood', (-B / 2, -L / 2, Dk), (-B / 2 + 0.3, L / 2, Dk + 0.7))
    b.box_mm('Wood', (B / 2 - 0.3, -L / 2, Dk), (B / 2, L / 2, Dk + 0.7))
    for y in (-6.5, 0.0, 6.5):
        b.sphere('Coal', (0, y, Dk + 0.2), (3.2, 2.8, 1.6), 12, 6)
    b.box_mm('Super', (-1.4, -L / 2 + 0.5, Dk), (1.4, -L / 2 + 2.6, Dk + 2.0))
    b.box('CabinGlow', (1.41, -L / 2 + 1.5, Dk + 1.3), (0.04, 0.6, 0.5))
    b.box_mm('Black', (-1.6, -L / 2 + 0.3, Dk + 2.0), (1.6, -L / 2 + 2.8, Dk + 2.1))
    b.tube('Steel', (0.8, -L / 2 + 1.0, Dk + 2.1), (0.8, -L / 2 + 1.0, Dk + 3.2), 0.1, 6)
    for y in (-L / 2 + 3.5, L / 2 - 1.0):
        for sx in (-1, 1):
            b.cyl('Black', (sx * (B / 2 - 0.7), y, Dk + 0.35), 0.18, 0.7, segs=8)
    return b.finish()


ALL = {'boat_freighter': freighter, 'boat_tug': tugboat, 'boat_rumrunner': rumrunner, 'boat_barge': barge}
