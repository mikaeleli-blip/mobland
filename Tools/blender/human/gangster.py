"""1920s gangster built on the MakeHuman base body (CC0): three-piece suit, shoes, hair, hat.

Everything is generated from the body's own shape (joint positions + cross-sections), so the clothes fit
any build. Front = +Y, Z up, character's right = +X.
"""
import math
import os
import sys

import bpy
import bmesh
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
import mhbody  # noqa: E402
from mbl import Builder, set_material  # noqa: E402


# ---------------------------------------------------------------- geometry helpers

def seg_closest(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    q = a + ab * t
    return (p - q).length, t, q


def hull2d(pts):
    pts = sorted(set((round(p[0], 5), round(p[1], 5)) for p in pts))
    if len(pts) < 3:
        return pts

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo, up = [], []
    for p in pts:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], p) <= 0:
            lo.pop()
        lo.append(p)
    for p in reversed(pts):
        while len(up) >= 2 and cross(up[-2], up[-1], p) <= 0:
            up.pop()
        up.append(p)
    return lo[:-1] + up[:-1]


def ray_poly(c, ang, poly):
    """Distance from centre c along angle to the polygon boundary (polygon contains c)."""
    dx, dy = math.cos(ang), math.sin(ang)
    best = 0.0
    n = len(poly)
    for i in range(n):
        x1, y1 = poly[i]
        x2, y2 = poly[(i + 1) % n]
        ex, ey = x2 - x1, y2 - y1
        den = dx * ey - dy * ex
        if abs(den) < 1e-12:
            continue
        t = ((x1 - c[0]) * ey - (y1 - c[1]) * ex) / den
        u = ((x1 - c[0]) * dy - (y1 - c[1]) * dx) / den
        if t > 0 and -1e-6 <= u <= 1 + 1e-6:
            best = max(best, t)
    return best


def smooth_ring(r, it=2):
    for _ in range(it):
        n = len(r)
        r = [(r[i - 1] + 2 * r[i] + r[(i + 1) % n]) / 4 for i in range(n)]
    return r


# ---------------------------------------------------------------- the figure

class Gangster:
    N = 36  # points per torso section

    def __init__(self, build='avg', arm_drop=16.0):
        self.mh, self.V, self.J = mhbody.build_male(build)
        self.build = build
        self.b = Builder('gangster', [])
        self._lower_arms(arm_drop)
        self._label()

    # ------------------------------------------------------------ pose + segmentation
    def _lower_arms(self, deg):
        J, V = self.J, self.V
        for side, sx in (('r', 1), ('l', -1)):
            sh = J[side + '-shoulder']
            el = J[side + '-elbow']
            d = (el - sh).normalized()
            ang = math.radians(deg) * sx
            R = Matrix.Rotation(ang, 4, 'Y')
            pivot = sh + Vector((-sx * 0.02, 0, 0.02))
            for v in V:
                if v.x * sx < 0.1:
                    continue
                t = (v - sh).dot(d)
                w = max(0.0, min(1.0, (t + 0.03) / 0.09))
                w = w * w * (3 - 2 * w)
                if w <= 0:
                    continue
                p = R @ (v - pivot) + pivot
                v.x, v.y, v.z = v.x + (p.x - v.x) * w, v.y + (p.y - v.y) * w, v.z + (p.z - v.z) * w
            for k in list(J):
                if k.startswith(side + '-') and any(s in k for s in ('elbow', 'hand', 'finger')):
                    J[k] = R @ (J[k] - pivot) + pivot

    def _label(self):
        J = self.J
        top = Vector((0, J['head'].y, J['head'].z + 0.16))
        segs = [('torso', J['pelvis'], J['spine-1'], 0.16), ('torso', J['spine-1'], J['neck'] + Vector((0, 0, -0.05)), 0.15),
                ('neck', J['neck'] + Vector((0, 0, -0.04)), J['head'] + Vector((0, -0.02, -0.04)), 0.06),
                ('head', J['head'] + Vector((0, 0, -0.06)), top, 0.1)]
        for s in 'rl':
            segs += [('torso', J[s + '-clavicle'], J[s + '-shoulder'], 0.09),
                     ('uarm' + s, J[s + '-shoulder'], J[s + '-elbow'], 0.06), ('farm' + s, J[s + '-elbow'], J[s + '-hand'], 0.045),
                     ('hand' + s, J[s + '-hand'], J[s + '-finger-3-4'], 0.035),
                     ('thigh' + s, J[s + '-upper-leg'], J[s + '-knee'], 0.085), ('shin' + s, J[s + '-knee'], J[s + '-ankle'], 0.06),
                     ('foot' + s, J[s + '-ankle'], J[s + '-toe-3-4'], 0.045)]
        self.segs = segs
        body_ids = sorted({i for f, _ in self.mh.groups['body'] for i in f})
        self.body_ids = body_ids
        self.label = {}
        for i in body_ids:
            p = self.V[i]
            best, lab = 1e9, None
            for (name, a, b, r) in segs:
                d, _, _ = seg_closest(p, a, b)
                if d / r < best:
                    best, lab = d / r, name
            # hips/buttocks belong to the torso down to the crotch
            if lab.startswith('thigh') and p.z > J['pelvis'].z - 0.06 and abs(p.x) < 0.17:
                lab = 'torso'
            self.label[i] = lab

    def verts_with(self, labels, zmin=-1, zmax=9):
        return [self.V[i] for i in self.body_ids if self.label[i] in labels and zmin <= self.V[i].z <= zmax]

    # ------------------------------------------------------------ body (only visible parts kept)
    def body(self, keep=('head', 'neck', 'handr', 'handl'), extra_keep=None):
        faces = []
        for f, _ in self.mh.groups['body']:
            labs = [self.label[i] for i in f]
            if any(l in keep for l in labs) or (extra_keep and extra_keep(f, labs)):
                faces.append(f)
        self._add_faces('Skin', faces)
        # face regions: lips / stubble are separate slots for colour
        self.face_details()
        self.eyes()

    def _add_faces(self, slot, faces, classify=None):
        bm = bmesh.new()
        idx = {}
        for f in faces:
            vs = []
            for i in f:
                if i not in idx:
                    idx[i] = bm.verts.new(self.V[i])
                vs.append(idx[i])
            try:
                bm.faces.new(vs)
            except ValueError:
                pass
        if classify:
            self.b.add_classified(bm, classify)
        else:
            self.b.add(bm, slot, recalc=False)

    def face_details(self):
        """Stubble on the jaw, lips, eyebrows as thin shells over the face."""
        J = self.J
        eye_z = J['r-eye'].z
        hc_y = J['head'].y
        faces = []
        brow = []
        for f, _ in self.mh.groups['body']:
            ps = [self.V[i] for i in f]
            c = sum(ps, Vector()) / len(ps)
            if self.label[f[0]] not in ('head', 'neck'):
                continue
            ax = abs(c.x)
            # eyebrow band
            if 0.012 < ax < 0.062 and eye_z + 0.018 < c.z < eye_z + 0.034 - (ax - 0.03) * 0.15 and c.y > hc_y + 0.07:
                brow.append(f)
        bm = bmesh.new()
        idx = {}
        for f in brow:
            vs = []
            for i in f:
                if i not in idx:
                    p = self.V[i]
                    n = (p - Vector((0, hc_y - 0.02, eye_z))).normalized()
                    idx[i] = bm.verts.new(p + n * 0.0025)
                vs.append(idx[i])
            try:
                bm.faces.new(vs)
            except ValueError:
                pass
        self.b.add(bm, 'Brow', recalc=False)

    def eyes(self):
        for side in 'rl':
            g = self.mh.groups['helper-%s-eye' % side]
            ids = {i for f, _ in g for i in f}
            pts = [self.V[i] for i in ids]
            c = sum(pts, Vector()) / len(pts)
            r = max((p - c).length for p in pts)
            self.b.sphere('EyeWhite', c, (r * 0.97, r * 0.97, r * 0.97), 14, 10)
            self.b.sphere('Iris', c + Vector((0, r * 0.82, 0)), (r * 0.5, r * 0.22, r * 0.5), 12, 6)
            self.b.sphere('Pupil', c + Vector((0, r * 0.92, 0)), (r * 0.24, r * 0.12, r * 0.24), 10, 6)

    # ------------------------------------------------------------ shells from the body
    def section(self, z, labels=('torso',), band=0.012, grow=0.0, n=None, center=None):
        """Smoothed convex section of the body at height z (radii by angle around centre)."""
        n = n or self.N
        pts = [(p.x, p.y) for p in self.verts_with(labels, z - band, z + band)]
        if len(pts) < 3:
            return None
        hull = hull2d(pts)
        c = center or (0.0, sum(p[1] for p in hull) / len(hull))
        rs = [ray_poly(c, 2 * math.pi * k / n, hull) + grow for k in range(n)]
        return c, smooth_ring(rs, 1)

    def ring_pts(self, c, rs, z, a0=0.0, a1=None, n=None):
        n = n or len(rs)
        out = []
        for k in range(n):
            a = 2 * math.pi * k / len(rs)
            out.append(Vector((c[0] + math.cos(a) * rs[k], c[1] + math.sin(a) * rs[k], z)))
        return out

    # ------------------------------------------------------------ trousers
    def trousers(self, slot='Trousers', hem_z=0.075, cuff=True, width=1.0):
        J = self.J
        waist_z = J['spine-4'].z + 0.02
        crotch_z = J['pelvis'].z - 0.13           # legs split here (hidden under the jacket skirt)
        # upper part: torso sections from waist down to crotch, then two legs
        secs = []
        for z in [waist_z - k * 0.025 for k in range(int((waist_z - crotch_z) / 0.025) + 1)]:
            js = getattr(self, 'jacket_secs', None)
            jsec = min(js, key=lambda e: abs(e[0] - z)) if js else None
            ctr = jsec[1] if jsec and abs(jsec[0] - z) < 0.03 else None
            s = self.section(z, ('torso', 'thighr', 'thighl'), grow=0.012, center=ctr)
            if s:
                c, rs = s
                if ctr is not None:
                    rs = [min(r, self._interp(jsec[2], 2 * math.pi * k / len(rs)) - 0.014) for k, r in enumerate(rs)]
                secs.append(self.ring_pts(c, rs, z))
        self.b.loft(slot, secs, closed=True, cap_start=False, cap_end=False)
        self.waist_top = waist_z
        for side, sx in (('r', 1), ('l', -1)):
            hip, knee, ank = J[side + '-upper-leg'], J[side + '-knee'], J[side + '-ankle']
            top_c = Vector((hip.x * 0.95, hip.y, crotch_z + 0.03))
            bot_c = Vector((ank.x * 0.96, ank.y + 0.01, hem_z))
            secs = []
            n = 20
            zs = [crotch_z + 0.03 - k * 0.04 for k in range(int((crotch_z + 0.03 - hem_z) / 0.04) + 1)] + [hem_z]
            for z in zs:
                t = (top_c.z - z) / (top_c.z - bot_c.z)
                cx = top_c.x + (bot_c.x - top_c.x) * t
                cy = top_c.y + (bot_c.y - top_c.y) * t
                s = self.section(z, ('thigh' + side, 'shin' + side), band=0.02, grow=0.016, n=n, center=(cx, cy))
                base = s[1] if s else [0.07] * n
                # wide 1920s cut: never narrower than a straight-hanging tube
                hang = (0.07 if z < knee.z else 0.074) * width
                rs = smooth_ring([max(r, hang) for r in base], 2)
                rs = self._inside_jacket(cx, cy, z, rs)
                secs.append((cx, cy, z, rs))
            # smooth radii vertically so knees and calves don't show through the cloth
            for _ in range(3):
                secs = [secs[0]] + [(e[0], e[1], e[2], [(a + 2 * b2 + c2) / 4 for a, b2, c2 in zip(p[3], e[3], q[3])])
                                    for p, e, q in zip(secs, secs[1:], secs[2:])] + [secs[-1]]
            secs = [[Vector((cx + math.cos(2 * math.pi * k / n) * rs[k], cy + math.sin(2 * math.pi * k / n) * rs[k], z)) for k in range(n)]
                    for (cx, cy, z, rs) in secs]
            self.b.loft(slot, secs, closed=True, cap_start=False, cap_end=False)
            if cuff:   # turn-ups
                c = Vector((bot_c.x, bot_c.y, hem_z))
                ring = lambda r, dz: [c + Vector((math.cos(2 * math.pi * k / n) * r * width, math.sin(2 * math.pi * k / n) * r * width, dz)) for k in range(n)]
                self.b.loft(slot, [ring(0.083, 0.0), ring(0.083, 0.04), ring(0.076, 0.04), ring(0.076, 0.0)], closed=True, cap_start=False, cap_end=False)

    # ------------------------------------------------------------ shirt bits, vest, tie
    def collar(self, slot='Shirt'):
        J = self.J
        nz = J['neck'].z - 0.04
        s = self.section(nz, ('neck', 'torso'), grow=0.004, n=24)
        c, rs = s
        secs = []
        for dz, g in ((0.0, 0.006), (0.055, 0.004), (0.055, -0.002), (0.0, -0.002)):
            secs.append([Vector((c[0] + math.cos(2 * math.pi * k / 24) * (rs[k] + g), c[1] + math.sin(2 * math.pi * k / 24) * (rs[k] + g), nz + dz))
                         for k in range(24)])
        self.b.loft(slot, secs, closed=True, cap_start=False, cap_end=False)
        # collar points
        fy = c[1] + rs[6]
        for sx in (-1, 1):
            self.b.loft(slot, [[Vector((sx * 0.012, fy + 0.006, nz + 0.05)), Vector((sx * 0.05, fy - 0.01, nz + 0.05)), Vector((sx * 0.035, fy + 0.002, nz - 0.03))],
                               [Vector((sx * 0.012, fy - 0.002, nz + 0.05)), Vector((sx * 0.05, fy - 0.018, nz + 0.05)), Vector((sx * 0.035, fy - 0.006, nz - 0.03))]],
                        smooth=False)
        self.neck_c, self.neck_rs, self.neck_z = c, rs, nz

    def front_y(self, z, x=0.0, labels=('torso',), grow=0.0):
        pts = [p for p in self.verts_with(labels, z - 0.015, z + 0.015) if abs(p.x - x) < 0.03]
        return (max(p.y for p in pts) if pts else 0.12) + grow

    def shirt_front(self, slot='Shirt'):
        """Visible shirt in the vest V: a plate over the chest."""
        J = self.J
        z0, z1 = J['spine-1'].z - 0.06, self.neck_z + 0.01
        secs = []
        for k in range(6):
            z = z0 + (z1 - z0) * k / 5
            row = []
            for x in (-0.07, -0.035, 0.0, 0.035, 0.07):
                row.append(Vector((x, self.front_y(z, x, grow=0.004), z)))
            secs.append(row)
        self.b.loft(slot, secs, closed=False, smooth=True)
        back = [[p + Vector((0, -0.012, 0)) for p in row] for row in secs]
        self.b.loft(slot, [list(reversed(r)) for r in back], closed=False, smooth=True)

    def tie(self, slot='Tie', knot_z=None):
        J = self.J
        kz = knot_z or self.neck_z + 0.015
        bot = J['spine-1'].z - 0.07        # the rest is under the waistcoat
        fy = self.front_y(kz - 0.02, grow=0.012)
        self.b.box(slot, (0, fy, kz - 0.005), (0.032, 0.022, 0.036), bevel=0.006)
        rows = []
        for k in range(10):
            t = k / 9
            z = kz - 0.025 - (kz - 0.025 - bot) * t
            y = self.front_y(z, grow=0.0075)
            w = 0.018 + 0.017 * t
            rows.append([Vector((-w, y, z)), Vector((0, y + 0.003, z)), Vector((w, y, z))])
        tip = Vector((0, rows[-1][1].y, rows[-1][1].z - 0.04))
        rows.append([tip, tip, tip])
        self.b.loft(slot, rows, closed=False)
        self.b.loft(slot, [[p + Vector((0, -0.004, 0)) for p in reversed(r)] for r in rows], closed=False)

    def vest(self, slot='Vest', buttons='Buttons', chain='Gold'):
        J = self.J
        z_bot = self.waist_top - 0.045
        z_top = self.neck_z - 0.035
        v_bottom = J['spine-1'].z - 0.02          # where the V neckline ends
        secs = []
        zs = [z_bot + k * 0.02 for k in range(int((z_top - z_bot) / 0.02) + 1)]
        n = self.N
        for z in zs:
            s = self.section(z, ('torso',), grow=0.011 if z < J['spine-1'].z else 0.009)
            if not s:
                continue
            c, rs = s
            js = getattr(self, 'jacket_secs', None)
            if js:
                zz, jc, jrs, _ = min(js, key=lambda e: abs(e[0] - z))
                rs = [min(r, self._interp(jrs, 2 * math.pi * k / len(rs)) - 0.012) for k, r in enumerate(rs)]
            # open V at the front above v_bottom: angle gap around +Y
            if z > v_bottom:
                gap = math.radians(5 + 24 * min(1.0, (z - v_bottom) / (z_top - v_bottom)))
            else:
                gap = math.radians(1.0)
            ring = []
            for k in range(n + 1):
                a = math.pi / 2 + gap + (2 * math.pi - 2 * gap) * k / n
                r = self._interp(rs, a)
                ring.append(Vector((c[0] + math.cos(a) * r, c[1] + math.sin(a) * r, z)))
            secs.append(ring)
        self._thick_loft(slot, secs, 0.006)
        # pointed front hem
        for sx in (-1, 1):
            p0 = Vector((sx * 0.004, self.front_y(z_bot, sx * 0.01, grow=0.012), z_bot))
            p1 = Vector((sx * 0.1, self.front_y(z_bot, sx * 0.1, grow=0.01), z_bot))
            p2 = Vector((sx * 0.02, self.front_y(z_bot - 0.05, sx * 0.02, grow=0.016), z_bot - 0.055))
            self.b.loft(slot, [[p0, p1, p2], [q + Vector((0, -0.008, 0)) for q in (p0, p1, p2)]], smooth=False)
        for k in range(5):
            z = v_bottom - 0.01 - k * (v_bottom - z_bot - 0.03) / 4
            self.b.sphere(buttons, Vector((0, self.front_y(z, grow=0.014), z)), (0.007, 0.004, 0.007), 8, 5)
        if chain:
            zc = z_bot + 0.07
            a = Vector((0, self.front_y(zc, grow=0.015), zc))
            pts = []
            for k in range(9):
                t = k / 8
                x = -0.11 * t
                z = zc - 0.035 * math.sin(math.pi * t) - 0.01 * t
                pts.append(Vector((x, self.front_y(z, x, grow=0.016), z)))
            for p, q in zip(pts, pts[1:]):
                self.b.beam(chain, p, q, 0.004)
        self.vest_v = v_bottom

    def _interp(self, rs, a):
        n = len(rs)
        f = (a % (2 * math.pi)) / (2 * math.pi) * n
        i = int(f) % n
        t = f - int(f)
        return rs[i] * (1 - t) + rs[(i + 1) % n] * t

    def _thick_loft(self, slot, secs, th):
        """Loft open rings and give them thickness (outer + inner surfaces, closed edges)."""
        outer = secs
        inner = []
        for ring in secs:
            c = sum(ring, Vector()) / len(ring)
            inner.append([p + (Vector((c.x, c.y, p.z)) - p).normalized() * th for p in ring])
        loops = [o + list(reversed(i)) for o, i in zip(outer, inner)]
        self.b.loft(slot, loops, closed=True, cap_start=True, cap_end=True)

    # ------------------------------------------------------------ jacket
    def jacket(self, slot='Jacket', lapel=None, buttons='Buttons', hem_drop=0.17, square='Square', two_button=True):
        J = self.J
        lapel = lapel or slot
        n = self.N
        hem_z = J['pelvis'].z - hem_drop
        chest_z = J['spine-1'].z
        sh_z = J['r-shoulder'].z
        top_z = sh_z + 0.05
        button_z = J['spine-3'].z + 0.0          # top (fastened) button
        hip_s = self.section(J['pelvis'].z - 0.04, ('torso',), grow=0.04)
        zs = [hem_z + k * 0.02 for k in range(int((top_z - hem_z) / 0.02) + 1)] + [top_z]
        secs = []
        open_secs = []
        raw = []
        for z in zs:
            p_lo, p_hi = J['pelvis'].z - 0.05, J['pelvis'].z + 0.07
            arm_z = sh_z - 0.11
            if z <= p_lo:
                c, rs = hip_s[0], [r * (1.0 + 0.03 * (p_lo - z) / hem_drop) for r in hip_s[1]]
            else:
                if z > arm_z:
                    # above the armpit include the deltoids so the shoulder line is natural
                    s = self.section(min(z, sh_z + 0.01), ('torso', 'uarmr', 'uarml'), grow=0.02)
                    c, rs = s
                    cap = J['r-shoulder'].x + 0.055
                    rs = [min(r, cap / max(0.2, abs(math.cos(2 * math.pi * k / n)))) for k, r in enumerate(rs)]
                    if z > sh_z - 0.03:
                        u = (z - (sh_z - 0.03)) / (top_z - (sh_z - 0.03))
                        rs = [r * (1 - 0.12 * u * abs(math.cos(2 * math.pi * k / n))) for k, r in enumerate(rs)]
                else:
                    s = self.section(z, ('torso',), grow=0.022)
                    c, rs = s
                if z < p_hi:      # blend into the hip/skirt section
                    t = (p_hi - z) / (p_hi - p_lo)
                    rs = [r * (1 - t) + h * t for r, h in zip(rs, hip_s[1])]
                    c = (c[0] * (1 - t) + hip_s[0][0] * t, c[1] * (1 - t) + hip_s[0][1] * t)
            raw.append((z, c, rs))
        # smooth the shape vertically (removes steps where the body sampling changes)
        for _ in range(3):
            raw = [raw[0]] + [(e[0], ((p[1][0] + 2 * e[1][0] + q[1][0]) / 4, (p[1][1] + 2 * e[1][1] + q[1][1]) / 4),
                               [(x + 2 * y + w) / 4 for x, y, w in zip(p[2], e[2], q[2])]) for p, e, q in zip(raw, raw[1:], raw[2:])] + [raw[-1]]
        for (z, c, rs) in raw:
            # front opening above the button, closed (with a little overlap) below
            if z > button_z:
                gap = math.radians(4 + 26 * min(1.0, (z - button_z) / (chest_z + 0.06 - button_z)) ** 0.85)
            elif z < hem_z + 0.12:
                gap = math.radians(4 + 14 * (hem_z + 0.12 - z) / 0.12)     # cut-away front of the skirt
            else:
                gap = math.radians(1.5)
            ring = []
            for k in range(n + 1):
                a = math.pi / 2 + gap + (2 * math.pi - 2 * gap) * k / n
                r = self._interp(rs, a)
                ring.append(Vector((c[0] + math.cos(a) * r, c[1] + math.sin(a) * r, z)))
            secs.append(ring)
            open_secs.append((z, c, rs, gap))
        self._thick_loft(slot, secs, 0.008)
        self.jacket_secs = open_secs
        # shoulder seams / top: close to the collar ring
        nc, nrs, nz = self.neck_c, self.neck_rs, self.neck_z
        z, c, rs, gap = open_secs[-1]
        top = []
        bottom = []
        for k in range(n + 1):
            a = math.pi / 2 + gap + (2 * math.pi - 2 * gap) * k / n
            r = self._interp(rs, a)
            bottom.append(Vector((c[0] + math.cos(a) * r, c[1] + math.sin(a) * r, z)))
            rn = self._interp(nrs, a) + 0.016
            top.append(Vector((nc[0] + math.cos(a) * rn, nc[1] + math.sin(a) * rn, nz + 0.005)))
        rings = []
        for k in range(6):
            t = k / 5
            w = 0.5 - 0.5 * math.cos(math.pi * t)
            lift = 0.012 * math.sin(math.pi * t)
            rings.append([p * (1 - w) + q * w + Vector((0, 0, lift)) for p, q in zip(bottom, top)])
        self._thick_loft(slot, rings, 0.008)
        # collar at the back of the neck
        coll = []
        for dz, g in ((0.0, 0.018), (0.025, 0.02), (0.042, 0.012)):
            coll.append([Vector((nc[0] + math.cos(a) * (self._interp(nrs, a) + g), nc[1] + math.sin(a) * (self._interp(nrs, a) + g), nz + 0.0 + dz))
                         for a in [math.pi / 2 + math.radians(35) + (2 * math.pi - math.radians(70)) * k / 24 for k in range(25)]])
        self._thick_loft(lapel, coll, 0.006)
        # lapels: flaps folded back along the opening edge
        self._lapels(lapel, open_secs, button_z, chest_z)
        # sleeves
        for side, sx in (('r', 1), ('l', -1)):
            self._sleeve(slot, side)
        # buttons + pockets + pocket square
        bz = [button_z - 0.01, button_z - 0.1] if two_button else [button_z - 0.01]
        for z in bz:
            y = self._jacket_front(z) + 0.004
            self.b.sphere(buttons, Vector((0.012, y, z)), (0.01, 0.005, 0.01), 8, 5)
        for sx in (-1, 1):
            # flap pockets: a thin strip laid on the jacket surface
            z = J['pelvis'].z - 0.06
            zz, c, rs, _ = min(self.jacket_secs, key=lambda e: abs(e[0] - z))
            mid = math.pi / 2 - sx * math.radians(27)
            loops = []
            for da in (-14, -7, 0, 7, 14):
                a = mid + math.radians(da)
                r = self._interp(rs, a)
                o = Vector((math.cos(a), math.sin(a), 0))
                p0 = Vector((c[0], c[1], zz)) + o * (r + 0.004)
                loops.append([p0, p0 + o * 0.006, p0 + o * 0.006 + Vector((0, 0, -0.05)), p0 + Vector((0, 0, -0.05))])
            self.b.loft(slot, loops, closed=True)
        z = chest_z + 0.01
        x = -0.135
        y = self._jacket_front(z, x)
        self.b.box(slot, (x, y + 0.002, z), (0.09, 0.008, 0.015))
        if square:
            self.b.loft(square, [[Vector((x - 0.035, y + 0.006, z + 0.006)), Vector((x - 0.01, y + 0.006, z + 0.035)),
                                  Vector((x + 0.012, y + 0.006, z + 0.022)), Vector((x + 0.035, y + 0.006, z + 0.006))],
                                 [Vector((x - 0.035, y - 0.002, z + 0.006)), Vector((x - 0.01, y - 0.002, z + 0.035)),
                                  Vector((x + 0.012, y - 0.002, z + 0.022)), Vector((x + 0.035, y - 0.002, z + 0.006))]], smooth=False)

    def _inside_jacket(self, cx, cy, z, rs, margin=0.014):
        js = getattr(self, 'jacket_secs', None)
        if not js or z > js[-1][0] or z < js[0][0] - 0.01:
            return rs
        zz, c, jrs, _ = min(js, key=lambda e: abs(e[0] - z))
        out = []
        n = len(rs)
        for k, r in enumerate(rs):
            a = 2 * math.pi * k / n
            # shrink r until the point is inside the jacket section
            for _ in range(20):
                px, py = cx + math.cos(a) * r, cy + math.sin(a) * r
                ang = math.atan2(py - c[1], px - c[0])
                lim = self._interp(jrs, ang) - margin
                if math.hypot(px - c[0], py - c[1]) <= lim or r < 0.03:
                    break
                r *= 0.95
            out.append(r)
        return out

    def _jacket_front(self, z, x=0.0):
        zz, c, rs, gap = min(self.jacket_secs, key=lambda e: abs(e[0] - z))
        best = None
        for k in range(181):
            a = math.pi * k / 180
            r = self._interp(rs, a)
            px, py = c[0] + math.cos(a) * r, c[1] + math.sin(a) * r
            if best is None or abs(px - x) < best[0]:
                best = (abs(px - x), py)
        return best[1]

    def _lapels(self, slot, secs, button_z, chest_z):
        top_z = secs[-1][0]
        notch_z = chest_z + 0.08
        for sx in (-1, 1):
            loops = []
            for (z, c, rs, gap) in secs:
                if z < button_z - 0.005:
                    continue
                t = (z - button_z) / max(1e-3, (notch_z - button_z))
                if z <= notch_z:
                    width = 0.012 + 0.06 * min(1.0, t) ** 0.7
                else:
                    width = 0.04 - 0.012 * (z - notch_z) / max(1e-3, top_z - notch_z)
                a_edge = math.pi / 2 - gap if sx > 0 else math.pi / 2 + gap
                r = self._interp(rs, a_edge)
                a_out = a_edge - sx * width / max(r, 0.05)
                ring = []
                for u in (0.0, 0.5, 1.0):
                    a = a_edge + (a_out - a_edge) * u
                    rr = self._interp(rs, a) + 0.007 + 0.004 * (1 - u)
                    ring.append(Vector((c[0] + math.cos(a) * rr, c[1] + math.sin(a) * rr, z)))
                inner = [Vector((p.x - math.cos(math.atan2(p.y - c[1], p.x - c[0])) * 0.005,
                                 p.y - math.sin(math.atan2(p.y - c[1], p.x - c[0])) * 0.005, p.z)) for p in ring]
                loops.append(ring + list(reversed(inner)))
            self.b.loft(slot, loops, closed=True, cap_start=True, cap_end=True)

    def _sleeve(self, slot, side):
        J = self.J
        sh, el, wr = J[side + '-shoulder'], J[side + '-elbow'], J[side + '-hand']
        sx = 1 if side == 'r' else -1
        start = sh + (el - sh).normalized() * 0.02 + Vector((-sx * 0.015, 0, -0.01))
        end = wr + (el - wr).normalized() * 0.07
        path = [(1 - t) ** 2 * start + 2 * (1 - t) * t * el + t ** 2 * end for t in [k / 12 for k in range(13)]]
        n = 18
        secs = []
        verts_a = self.verts_with(('uarm' + side, 'farm' + side))
        a_prev = None
        for i, p in enumerate(path):
            d = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
            a_vec = Vector((0, 1, 0)) if a_prev is None else a_prev
            a_vec = (a_vec - d * a_vec.dot(d)).normalized()     # parallel transport: no twisting
            a_prev = a_vec
            bvec = d.cross(a_vec).normalized()
            near = [v for v in verts_a if abs((v - p).dot(d)) < 0.02 and ((v - p) - d * (v - p).dot(d)).length < 0.12]
            r_body = max(((v - p) - d * (v - p).dot(d)).length for v in near) if near else 0.045
            t = i / (len(path) - 1)
            r = max(r_body + 0.014, 0.068 - 0.024 * t)
            if i == 0:
                r = max(r, 0.064)
            secs.append([p + (a_vec * math.cos(2 * math.pi * k / n) + bvec * math.sin(2 * math.pi * k / n)) * r for k in range(n)])
        self.b.loft(slot, secs, closed=True, cap_start=False, cap_end=False)
        # shirt cuff peeking out
        p = wr + (el - wr).normalized() * 0.055
        d = (wr - el).normalized()
        a = d.orthogonal().normalized()
        bvec = d.cross(a).normalized()
        cuff = [[p + d * dd + (a * math.cos(2 * math.pi * k / n) + bvec * math.sin(2 * math.pi * k / n)) * rr for k in range(n)]
                for dd, rr in ((-0.02, 0.046), (0.022, 0.044), (0.022, 0.038), (-0.02, 0.04))]
        self.b.loft('Shirt', cuff, closed=True, cap_start=False, cap_end=False)

    # ------------------------------------------------------------ shoes
    def shoes(self, slot='Shoes', sole='Sole'):
        J = self.J
        for side in 'rl':
            ank, toe = J[side + '-ankle'], J[side + '-toe-3-4']
            heel_y = ank.y - 0.075
            toe_y = toe.y + 0.022
            cx = (ank.x + toe.x) / 2
            n = 16
            secs = []
            ys = [heel_y + (toe_y - heel_y) * k / 11 for k in range(12)]
            for k, y in enumerate(ys):
                t = k / 11
                w = 0.045 + 0.012 * math.sin(math.pi * min(1, t * 1.15)) - 0.02 * max(0, t - 0.8) / 0.2
                h = (0.115 if t < 0.35 else 0.115 - 0.07 * (t - 0.35) / 0.65) - 0.025 * max(0, t - 0.85) / 0.15
                if k == 0 or k == 11:
                    w *= 0.75
                ring = []
                for j in range(n):
                    a = 2 * math.pi * j / n
                    x = cx + math.cos(a) * w
                    z = 0.015 + (math.sin(a) * 0.5 + 0.5) * h
                    ring.append(Vector((x, y, z)))
                secs.append(ring)
            self.b.loft(slot, secs, closed=True)
            # sole + heel
            self.b.box(sole, (cx, (heel_y + toe_y) / 2 + 0.005, 0.009), (0.105, toe_y - heel_y + 0.012, 0.018), bevel=0.006, segs=1)
            self.b.box(sole, (cx, heel_y + 0.03, 0.02), (0.075, 0.065, 0.04), bevel=0.004, segs=1)

    # ------------------------------------------------------------ hair + hat
    def hair(self, slot='Hair', hairline=0.06, thickness=0.01):
        """Short hair: a shell over the scalp, following the head."""
        J = self.J
        eye_z = J['r-eye'].z
        hy = J['head'].y
        faces = []
        for f, _ in self.mh.groups['body']:
            if self.label[f[0]] not in ('head', 'neck'):
                continue
            ps = [self.V[i] for i in f]
            c = sum(ps, Vector()) / len(ps)
            front = c.y - hy
            limit = eye_z + hairline if front > 0.03 else (eye_z + 0.005 if front > -0.035 else eye_z - 0.075)
            if abs(c.x) > 0.06 and front > -0.01:      # temples / sideburns
                limit = eye_z + 0.0 if front < 0.06 else limit
            if c.z > limit:
                faces.append(f)
        center = Vector((0, hy - 0.01, eye_z))
        bm = bmesh.new()
        idx = {}
        for f in faces:
            vs = []
            for i in f:
                if i not in idx:
                    p = self.V[i]
                    nrm = (p - center).normalized()
                    idx[i] = bm.verts.new(p + nrm * thickness)
                vs.append(idx[i])
            try:
                bm.faces.new(vs)
            except ValueError:
                pass
        bmesh.ops.solidify(bm, geom=bm.faces[:], thickness=-0.004)
        self.b.add(bm, slot, recalc=False)

    def head_section(self, z, grow=0.0, n=28):
        return self.section(z, ('head',), band=0.008, grow=grow, n=n)

    def fedora(self, hat='Hat', band='Band', brim=0.062, height=0.13, pinch=True):
        J = self.J
        z0 = J['r-eye'].z + 0.058
        s = self.head_section(z0, grow=0.012)
        c, rs = s
        n = len(rs)
        secs = []
        for dz, scale in ((0.0, 1.0), (height * 0.45, 1.01), (height * 0.8, 0.95), (height, 0.82)):
            ring = []
            for k in range(n):
                a = 2 * math.pi * k / n
                r = rs[k] * scale
                z = z0 + dz
                if pinch and dz >= height * 0.8:
                    # front pinch + centre crease
                    z -= 0.012 * max(0.0, math.sin(a)) ** 4
                    z -= 0.018 * (abs(math.cos(a)) < 0.4) * (dz == height)
                ring.append(Vector((c[0] + math.cos(a) * r, c[1] + math.sin(a) * r, z)))
            secs.append(ring)
        topc = Vector((c[0], c[1], z0 + height - 0.02))
        secs.append([topc + (p - topc) * 0.25 for p in secs[-1]])
        self.b.loft(hat, secs, closed=True, cap_start=False, cap_end=True)
        self.b.loft(band, [[Vector((c[0] + math.cos(2 * math.pi * k / n) * (rs[k] + 0.003), c[1] + math.sin(2 * math.pi * k / n) * (rs[k] + 0.003), z0 + dz))
                            for k in range(n)] for dz in (0.0, 0.032)], closed=True, cap_start=False, cap_end=False)
        # brim: snap-brim (down at front, up at back/sides)
        loops = []
        for k in range(n + 1):
            a = 2 * math.pi * (k % n) / n
            r0 = rs[k % n]
            r1 = r0 + brim * (1.0 if math.sin(a) > -0.2 else 0.9)
            droop = -0.022 * max(0.0, math.sin(a)) ** 2 + 0.012 * math.cos(a) ** 2 + 0.008 * max(0.0, -math.sin(a))
            pi_ = Vector((c[0] + math.cos(a) * r0, c[1] + math.sin(a) * r0, z0))
            po = Vector((c[0] + math.cos(a) * r1, c[1] + math.sin(a) * r1, z0 + droop))
            loops.append([pi_, po, po + Vector((0, 0, 0.006)), pi_ + Vector((0, 0, 0.006))])
        self.b.loft(hat, loops, closed=True, cap_start=False, cap_end=False)

    def flat_cap(self, cap='Cap', button='Cap'):
        J = self.J
        z0 = J['r-eye'].z + 0.05
        s = self.head_section(z0, grow=0.012)
        c, rs = s
        n = len(rs)
        secs = []
        for dz, sc, fwd in ((0.0, 1.0, 0.0), (0.05, 1.08, 0.02), (0.085, 1.0, 0.035), (0.1, 0.6, 0.03)):
            secs.append([Vector((c[0] + math.cos(2 * math.pi * k / n) * rs[k] * sc,
                                 c[1] + fwd * max(0, math.sin(2 * math.pi * k / n)) + math.sin(2 * math.pi * k / n) * rs[k] * sc,
                                 z0 + dz - 0.03 * max(0, math.sin(2 * math.pi * k / n)) * (dz > 0.04))) for k in range(n)])
        top = Vector((c[0], c[1] + 0.01, z0 + 0.1))
        secs.append([top + (p - top) * 0.2 for p in secs[-1]])
        self.b.loft(cap, secs, closed=True, cap_start=False, cap_end=True)
        # peak
        loops = []
        for k in range(11):
            a = math.radians(35 + 110 * k / 10)
            r0 = self._interp(rs, a)
            pi_ = Vector((c[0] + math.cos(a) * r0, c[1] + math.sin(a) * r0, z0 + 0.005))
            po = Vector((c[0] + math.cos(a) * (r0 + 0.012), c[1] + math.sin(a) * (r0 + 0.055), z0 - 0.012))
            loops.append([pi_, po, po + Vector((0, 0, 0.006)), pi_ + Vector((0, 0, 0.006))])
        self.b.loft(cap, loops, closed=True)
        self.b.sphere(button, top + Vector((0, 0, 0.004)), (0.012, 0.012, 0.006), 8, 4)

    def mustache(self, slot='Hair', size=1.0):
        J = self.J
        m = J['mouth']
        # find upper lip front
        pts = [self.V[i] for i in self.body_ids if self.label[i] == 'head' and abs(self.V[i].x) < 0.01 and abs(self.V[i].z - (m.z + 0.012)) < 0.006]
        fy = max(p.y for p in pts) if pts else m.y + 0.06
        z = m.z + 0.014
        secs = []
        for k in range(9):
            t = k / 8
            x = (-0.032 + 0.064 * t) * size
            yy = fy - 0.004 - 0.02 * (abs(x) / 0.032) ** 2
            zz = z - 0.004 * (abs(x) / 0.032) ** 1.5
            secs.append([Vector((x, yy + 0.008, zz + 0.007)), Vector((x, yy + 0.01, zz - 0.004)), Vector((x, yy - 0.004, zz - 0.004)),
                         Vector((x, yy - 0.004, zz + 0.008))])
        self.b.loft(slot, secs)

    def finish(self, name):
        self.b.name = name
        return self.b.finish(sharp_angle=60)


# ---------------------------------------------------------------- materials + fabric textures

def fabric_image(name, kind, base, accent, size=512):
    import numpy as np
    rng = np.random.default_rng(abs(hash(name)) % (2 ** 32))
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    base = np.array(base, np.float32)
    accent = np.array(accent, np.float32)
    if kind == 'herringbone':
        col = 40
        band = (xx // col).astype(int) % 2
        d = np.where(band == 0, xx + yy, xx - yy)
        twill = (np.sin(d * 2 * np.pi / 10) > 0.1).astype(np.float32)
        img = base[None, None] * (0.72 + 0.42 * twill[..., None])
        edge = ((xx % col) < 1).astype(np.float32)
        img *= (1 - 0.15 * edge)[..., None]
    elif kind == 'pinstripe':
        twill = (np.sin((xx + yy) * 2 * np.pi / 5) > 0.3).astype(np.float32)
        img = base[None, None] * (0.9 + 0.12 * twill[..., None])
        stripe = ((xx % 40) < 1.6).astype(np.float32)
        img = img * (1 - stripe[..., None]) + accent[None, None] * stripe[..., None]
    else:  # tweed
        twill = (np.sin((xx + yy) * 2 * np.pi / 6) > 0.0).astype(np.float32)
        img = base[None, None] * (0.85 + 0.2 * twill[..., None])
    fleck = rng.random((size, size)) < 0.05
    tone = rng.random((size, size, 1)) * 0.6 + 0.7
    img = np.where(fleck[..., None], img * tone + accent[None, None] * 0.25, img)
    noise = rng.random((size, size, 1)).astype(np.float32) * 0.08 + 0.96
    img = np.clip(img * noise, 0, 1)
    im = bpy.data.images.new(name, size, size, alpha=False)
    rgba = np.concatenate([img, np.ones((size, size, 1), np.float32)], axis=2)
    im.pixels.foreach_set(np.ascontiguousarray(rgba[::-1], dtype=np.float32).ravel())
    im.pack()
    return im


def fabric_material(name, kind, base, accent, rough=0.85, scale=4.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get('Principled BSDF')
    bsdf.inputs['Roughness'].default_value = rough
    img = fabric_image(name + '_tex', kind, base, accent)
    tex = nt.nodes.new('ShaderNodeTexImage')
    tex.image = img
    tex.projection = 'BOX'
    tex.projection_blend = 0.25
    coord = nt.nodes.new('ShaderNodeTexCoord')
    mapping = nt.nodes.new('ShaderNodeMapping')
    mapping.inputs['Scale'].default_value = (scale, scale, scale)
    nt.links.new(coord.outputs['Object'], mapping.inputs['Vector'])
    nt.links.new(mapping.outputs['Vector'], tex.inputs['Vector'])
    nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    return m


def common_materials(skin=(0.62, 0.43, 0.33), hair=(0.025, 0.02, 0.018)):
    set_material('Skin', skin, 0.5)
    set_material('Brow', hair, 0.8)
    set_material('Hair', hair, 0.4)
    set_material('EyeWhite', (0.85, 0.83, 0.8), 0.2)
    set_material('Iris', (0.16, 0.09, 0.04), 0.2)
    set_material('Pupil', (0.01, 0.01, 0.01), 0.1)
    set_material('Shirt', (0.88, 0.86, 0.8), 0.6)
    set_material('Buttons', (0.08, 0.06, 0.05), 0.3)
    set_material('Gold', (0.85, 0.62, 0.22), 0.25, 1.0)
    set_material('Square', (0.92, 0.9, 0.86), 0.6)
    set_material('Sole', (0.04, 0.03, 0.025), 0.6)


# ---------------------------------------------------------------- characters

def italian_01():
    """Brown herringbone tweed three-piece, brown fedora, burgundy tie, brown oxfords."""
    common_materials()
    fabric_material('Jacket', 'herringbone', (0.25, 0.19, 0.14), (0.45, 0.35, 0.25))
    fabric_material('Vest', 'herringbone', (0.25, 0.19, 0.14), (0.45, 0.35, 0.25))
    fabric_material('Trousers', 'herringbone', (0.25, 0.19, 0.14), (0.45, 0.35, 0.25))
    set_material('Tie', (0.28, 0.04, 0.05), 0.35)
    set_material('Hat', (0.2, 0.14, 0.1), 0.75)
    set_material('Band', (0.07, 0.05, 0.04), 0.5)
    set_material('Shoes', (0.18, 0.08, 0.04), 0.3)
    g = Gangster('avg')
    g.body()
    g.hair()
    g.collar()
    g.shirt_front()
    g.jacket()
    g.trousers()
    g.tie()
    g.vest()
    g.shoes()
    g.fedora()
    return g.finish('italian_01')


ALL = {'italian_01': italian_01}
