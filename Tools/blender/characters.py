"""Mobland characters (preview stage). Front = +Y, character's right hand = +X, feet at Z = 0.

Body: a skeleton of vertices/edges with per-joint radii, skinned with Blender's Skin modifier and subdivided,
then coloured by region (head, torso, arms, hands, legs). Clothes, hats, faces and props are separate parts.
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

from mbl import Builder, set_material

# ---------------------------------------------------------------- skeleton


def skeleton(build='avg', female=False):
    """Returns (points dict, radii dict, edges list). Positions in metres."""
    P = {
        'pel': (0, 0, 0.96), 'spine': (0, 0.0, 1.13), 'chest': (0, 0, 1.3), 'neck': (0, 0.0, 1.49), 'neck2': (0, 0.008, 1.54),
        'shR': (0.205, 0, 1.44), 'elR': (0.29, -0.01, 1.17), 'wrR': (0.335, 0.03, 0.93), 'haR': (0.35, 0.045, 0.85),
        'hipR': (0.1, 0, 0.91), 'knR': (0.11, 0.02, 0.5), 'anR': (0.11, -0.01, 0.1), 'toR': (0.11, 0.12, 0.05),
    }
    R = {
        'pel': (0.175, 0.12), 'spine': (0.16, 0.11), 'chest': (0.2, 0.13), 'neck': (0.058, 0.058), 'neck2': (0.052, 0.052),
        'shR': (0.082, 0.08), 'elR': (0.06, 0.06), 'wrR': (0.044, 0.044), 'haR': (0.052, 0.034),
        'hipR': (0.1, 0.1), 'knR': (0.074, 0.074), 'anR': (0.055, 0.055), 'toR': (0.045, 0.034),
    }
    hscale = 1.0
    if female:
        P.update({'shR': (0.16, 0, 1.42), 'elR': (0.235, -0.01, 1.17), 'wrR': (0.28, 0.03, 0.94), 'haR': (0.29, 0.045, 0.87),
                  'hipR': (0.09, 0, 0.93)})
        R.update({'chest': (0.15, 0.105), 'spine': (0.115, 0.085), 'pel': (0.165, 0.11), 'neck': (0.045, 0.045), 'neck2': (0.042, 0.042),
                  'shR': (0.055, 0.055), 'elR': (0.038, 0.038), 'wrR': (0.03, 0.03), 'haR': (0.036, 0.025),
                  'hipR': (0.085, 0.085), 'knR': (0.05, 0.05), 'anR': (0.035, 0.035), 'toR': (0.032, 0.025)})
        hscale = 0.95
    elif build == 'heavy':
        R.update({'pel': (0.2, 0.15), 'spine': (0.2, 0.17), 'chest': (0.21, 0.145), 'neck': (0.07, 0.07),
                  'shR': (0.08, 0.08), 'elR': (0.06, 0.06), 'hipR': (0.105, 0.105), 'knR': (0.07, 0.07)})
        P['spine'] = (0, 0.03, 1.14)
        hscale = 0.98
    elif build == 'big':
        P.update({'shR': (0.235, 0, 1.46), 'elR': (0.32, -0.01, 1.19), 'wrR': (0.37, 0.03, 0.95), 'haR': (0.385, 0.045, 0.86)})
        R.update({'chest': (0.23, 0.15), 'spine': (0.18, 0.13), 'pel': (0.175, 0.125), 'neck': (0.075, 0.075), 'neck2': (0.065, 0.065),
                  'shR': (0.095, 0.09), 'elR': (0.07, 0.07), 'wrR': (0.05, 0.05), 'haR': (0.06, 0.04),
                  'hipR': (0.105, 0.105), 'knR': (0.075, 0.075), 'anR': (0.055, 0.055)})
        hscale = 1.06
    elif build == 'slim':
        R.update({'chest': (0.18, 0.115), 'spine': (0.145, 0.1), 'pel': (0.16, 0.11),
                  'shR': (0.07, 0.07), 'elR': (0.052, 0.052), 'hipR': (0.09, 0.09), 'knR': (0.065, 0.065)})
        hscale = 1.03

    # mirror right -> left
    for k in list(P):
        if k.endswith('R'):
            x, y, z = P[k]
            P[k[:-1] + 'L'] = (-x, y, z)
            R[k[:-1] + 'L'] = R[k]
    P = {k: Vector((v[0] * (1 + (hscale - 1) * 0.5), v[1], v[2] * hscale)) for k, v in P.items()}
    E = [('pel', 'spine'), ('spine', 'chest'), ('chest', 'neck'), ('neck', 'neck2')]
    for s in 'RL':
        E += [('chest', 'sh' + s), ('sh' + s, 'el' + s), ('el' + s, 'wr' + s), ('wr' + s, 'ha' + s),
              ('pel', 'hip' + s), ('hip' + s, 'kn' + s), ('kn' + s, 'an' + s), ('an' + s, 'to' + s)]
    return P, R, E


class Figure:
    def __init__(self, name, build='avg', female=False):
        self.name, self.build, self.female = name, build, female
        self.P, self.R, self.E = skeleton(build, female)
        self.b = None
        self.verts = []
        self.hc = None  # head centre

    # ------------------------------------------------------------ body
    def body(self, b, regions):
        self.b = b
        names = list(self.P)
        me = bpy.data.meshes.new('skel')
        me.from_pydata([tuple(self.P[n]) for n in names], [(names.index(a), names.index(c)) for a, c in self.E], [])
        obj = bpy.data.objects.new('skel', me)
        bpy.context.scene.collection.objects.link(obj)
        sk = obj.modifiers.new('skin', 'SKIN')
        sk.use_smooth_shade = True
        sk.branch_smoothing = 0.6
        for i, n in enumerate(names):
            sv = me.skin_vertices[0].data[i]
            sv.radius = self.R[n]
            sv.use_root = (n == 'pel')
        sub = obj.modifiers.new('sub', 'SUBSURF')
        sub.levels = 2
        sub.render_levels = 2
        dg = bpy.context.evaluated_depsgraph_get()
        eo = obj.evaluated_get(dg)
        ev = bpy.data.meshes.new_from_object(eo)
        bm = bmesh.new()
        bm.from_mesh(ev)
        self.verts = [v.co.copy() for v in bm.verts]
        bpy.data.objects.remove(obj)
        bpy.data.meshes.remove(ev)

        P = self.P
        neck_z = P['neck'].z - 0.01
        sh_x = P['shR'].x
        wr_z = P['wrR'].z
        el_z = P['elR'].z
        pel_z = P['pel'].z

        def cls(f):
            c = f.calc_center_median()
            ax = abs(c.x)
            if c.z > neck_z:
                return regions.get('neck', 'Skin')
            ha = P['haR']
            if ax > ha.x - 0.075 and ha.z - 0.12 < c.z < wr_z + 0.015:
                return regions.get('hands', 'Skin')
            if ax > sh_x * 0.72 and c.z > wr_z and c.z > pel_z - 0.05:
                if c.z < el_z - 0.02 and 'forearms' in regions:
                    return regions['forearms']
                return regions.get('arms', 'Jacket')
            if c.z < pel_z - 0.07:
                if c.z < 0.13:
                    return regions.get('feet', 'Shoes')
                return regions.get('legs', 'Trousers')
            return regions.get('torso', 'Jacket')
        b.add_classified(bm, cls)
        self.head()

    def front_y(self, z, x=0.0, tol=0.025):
        ys = [v.y for v in self.verts if abs(v.z - z) < tol and abs(v.x - x) < 0.03]
        return max(ys) if ys else 0.12

    def radius_at(self, z, tol=0.02):
        pts = [v for v in self.verts if abs(v.z - z) < tol and abs(v.x) < self.P['shR'].x * 0.75]
        if not pts:
            return 0.15, 0.11
        return max(abs(v.x) for v in pts), max(abs(v.y) for v in pts)

    # ------------------------------------------------------------ head + face
    def head(self, older=False):
        b = self.b
        n2 = self.P['neck2']
        s = 0.92 if self.female else (1.06 if self.build == 'big' else 1.0)
        hc = Vector((0, 0.012, n2.z + 0.105 * s))
        self.hc = hc
        self.hs = s
        b.sphere('Skin', hc, (0.086 * s, 0.098 * s, 0.112 * s), 16, 12)
        b.sphere('Skin', hc + Vector((0, 0.022, -0.062)) * s, (0.072 * s, 0.075 * s, 0.058 * s), 14, 8)   # jaw
        b.sphere('Skin', hc + Vector((0, 0.1, -0.008)) * s, (0.015 * s, 0.024 * s, 0.026 * s), 8, 6)      # nose
        for sx in (-1, 1):
            b.sphere('Skin', hc + Vector((sx * 0.086, -0.004, 0.0)) * s, (0.014 * s, 0.026 * s, 0.033 * s), 8, 6)  # ears
            b.sphere('Eyes', hc + Vector((sx * 0.032, 0.086, 0.02)) * s, (0.011 * s, 0.008 * s, 0.009 * s), 8, 6)
            b.box('Brows', tuple(hc + Vector((sx * 0.033, 0.092, 0.043)) * s), (0.036 * s, 0.012 * s, 0.009 * s))

    def mustache(self, slot='Hair', big=False):
        s, hc = self.hs, self.hc
        if big:
            self.b.sphere(slot, hc + Vector((0, 0.099, -0.036)) * s, (0.045 * s, 0.014 * s, 0.013 * s), 10, 6)
        else:
            self.b.sphere(slot, hc + Vector((0, 0.1, -0.034)) * s, (0.03 * s, 0.01 * s, 0.008 * s), 10, 6)

    def hair(self, style='short', slot='Hair'):
        b, hc, s = self.b, self.hc, self.hs
        if style == 'short':        # visible below a hat
            b.sphere(slot, hc + Vector((0, -0.018, 0.012)) * s, (0.091 * s, 0.098 * s, 0.1 * s), 14, 10)
        elif style == 'slick':
            b.sphere(slot, hc + Vector((0, -0.012, 0.03)) * s, (0.092 * s, 0.103 * s, 0.098 * s), 16, 10)
        elif style == 'bald':
            b.sphere(slot, hc + Vector((0, -0.03, -0.005)) * s, (0.09 * s, 0.085 * s, 0.075 * s), 14, 10)
        elif style == 'bob':
            b.sphere(slot, hc + Vector((0, -0.01, 0.025)) * s, (0.098 * s, 0.108 * s, 0.1 * s), 16, 10)
            secs = []
            for i in range(9):
                t = i / 8
                a = math.radians(-10 + 200 * t)
                secs.append([hc + Vector((math.cos(a) * 0.103 * s * k, math.sin(a) * -0.108 * s * k, z * s))
                             for (k, z) in ((1.0, 0.01), (1.06, -0.05), (0.96, -0.07), (0.9, 0.0))])
            b.loft(slot, secs)

    # ------------------------------------------------------------ hats
    def _ring(self, slot, z, rin, rout, droop=(0.0, 0.0), curl=0.0, thick=0.008, n=24):
        """Elliptical brim: rin/rout = (rx, ry). droop=(front/back drop, side lift), curl raises the outer edge."""
        c = self.hc
        secs = []
        for i in range(n):
            a = 2 * math.pi * i / n
            ca, sa = math.cos(a), math.sin(a)
            dz = -droop[0] * sa * sa + droop[1] * ca * ca
            pi_ = Vector((c.x + ca * rin[0], c.y + sa * rin[1], z))
            po = Vector((c.x + ca * rout[0], c.y + sa * rout[1], z + dz + curl))
            secs.append([pi_, po, po + Vector((0, 0, thick)), pi_ + Vector((0, 0, thick))])
        secs.append(secs[0])
        self.b.loft(slot, secs, closed=True, cap_start=False, cap_end=False)

    def _crown(self, slot, z0, profile, n=20, cap=True):
        """profile: list of (dz, rx, ry). Lofted crown from z0 upward."""
        c = self.hc
        secs = []
        for (dz, rx, ry) in profile:
            secs.append([Vector((c.x + math.cos(2 * math.pi * i / n) * rx, c.y + math.sin(2 * math.pi * i / n) * ry, z0 + dz)) for i in range(n)])
        self.b.loft(slot, secs, closed=True, cap_start=False, cap_end=cap)

    def hat(self, kind, hat='Hat', band='Band'):
        c, s = self.hc, self.hs
        z0 = c.z + 0.05 * s
        k = s
        if kind == 'fedora':
            self._crown(hat, z0, [(0, 0.097 * k, 0.11 * k), (0.06, 0.098 * k, 0.11 * k), (0.105, 0.087 * k, 0.1 * k),
                                  (0.125, 0.062 * k, 0.075 * k), (0.118, 0.02 * k, 0.05 * k)])
            self._crown(band, z0 - 0.001, [(0, 0.1 * k, 0.113 * k), (0.03, 0.1 * k, 0.113 * k)], cap=False)
            self._ring(hat, z0, (0.096 * k, 0.108 * k), (0.172 * k, 0.188 * k), droop=(0.02, 0.018))
        elif kind == 'homburg':
            self._crown(hat, z0, [(0, 0.097 * k, 0.11 * k), (0.11, 0.094 * k, 0.106 * k), (0.14, 0.08 * k, 0.093 * k),
                                  (0.13, 0.025 * k, 0.06 * k)])
            self._crown(band, z0 - 0.001, [(0, 0.1 * k, 0.113 * k), (0.035, 0.1 * k, 0.113 * k)], cap=False)
            self._ring(hat, z0, (0.096 * k, 0.108 * k), (0.15 * k, 0.165 * k), droop=(0.004, 0.012), curl=0.014)
        elif kind == 'bowler':
            prof = [(0.12 * (1 - math.cos(t * math.pi / 2)) if False else 0.125 * math.sin(t * math.pi / 2),
                     0.098 * k * math.cos(t * math.pi / 2.15), 0.11 * k * math.cos(t * math.pi / 2.15)) for t in [i / 7 for i in range(8)]]
            self._crown(hat, z0, prof)
            self._crown(band, z0 - 0.001, [(0, 0.1 * k, 0.113 * k), (0.022, 0.1 * k, 0.113 * k)], cap=False)
            self._ring(hat, z0, (0.096 * k, 0.108 * k), (0.138 * k, 0.15 * k), droop=(0.006, 0.02), curl=0.008)
        elif kind == 'boater':
            self._crown(hat, z0, [(0, 0.1 * k, 0.112 * k), (0.075, 0.1 * k, 0.112 * k)])
            self._crown(band, z0 + 0.008, [(0, 0.102 * k, 0.114 * k), (0.035, 0.102 * k, 0.114 * k)], cap=False)
            self._ring(hat, z0, (0.099 * k, 0.111 * k), (0.18 * k, 0.192 * k), thick=0.01)
        elif kind == 'chauffeur':
            self._crown(hat, z0 - 0.01, [(0, 0.098 * k, 0.11 * k), (0.06, 0.112 * k, 0.125 * k), (0.075, 0.112 * k, 0.125 * k)])
            self._crown(band, z0 - 0.011, [(0, 0.1 * k, 0.112 * k), (0.03, 0.1 * k, 0.112 * k)], cap=False)
            # visor (front half ring)
            secs = []
            for i in range(9):
                a = math.radians(25 + 130 * i / 8)
                ca, sa = math.cos(a), math.sin(a)
                pi_ = Vector((c.x + ca * 0.096 * k, c.y + sa * 0.108 * k, z0 - 0.01))
                po = Vector((c.x + ca * 0.12 * k, c.y + sa * 0.17 * k, z0 - 0.03))
                secs.append([pi_, po, po + Vector((0, 0, 0.007)), pi_ + Vector((0, 0, 0.007))])
            self.b.loft('Visor', secs)
        elif kind == 'eyeshade':
            self._crown(band, z0 - 0.02, [(0, 0.095 * k, 0.107 * k), (0.03, 0.095 * k, 0.107 * k)], cap=False)
            secs = []
            for i in range(9):
                a = math.radians(20 + 140 * i / 8)
                ca, sa = math.cos(a), math.sin(a)
                pi_ = Vector((c.x + ca * 0.094 * k, c.y + sa * 0.106 * k, z0 + 0.005))
                po = Vector((c.x + ca * 0.12 * k, c.y + sa * 0.19 * k, z0 - 0.025))
                secs.append([pi_, po, po + Vector((0, 0, 0.005)), pi_ + Vector((0, 0, 0.005))])
            self.b.loft('Visor', secs)
        elif kind == 'cloche':
            prof = [(-0.07 + 0.15 * math.sin(t * math.pi / 2), 0.107 * k * math.cos(t * math.pi / 2.1) + 0.01 * (1 - t) ** 3,
                     0.117 * k * math.cos(t * math.pi / 2.1) + 0.01 * (1 - t) ** 3) for t in [i / 8 for i in range(9)]]
            self._crown(hat, z0, prof)
            self._crown(band, z0 - 0.045, [(0, 0.108 * k, 0.118 * k), (0.025, 0.108 * k, 0.118 * k)], cap=False)
            self.b.sphere(band, c + Vector((0.075, 0.06, 0.03)) * 1.0, (0.025, 0.02, 0.025), 8, 6)   # brooch / bow

    # ------------------------------------------------------------ clothing
    def shirt_front(self, shirt='Shirt', tie='Tie', z_top=1.47, z_bot=1.22, width=0.055, tie_on=True, bow=False):
        zt, zb = z_top * self._hs(), z_bot * self._hs()
        self.collar(shirt)
        fy_t, fy_b = self.front_y(zt), self.front_y(zb)
        front = [Vector((-width, fy_t + 0.004, zt)), Vector((width, fy_t + 0.004, zt)), Vector((0, fy_b + 0.004, zb))]
        back = [p + Vector((0, -0.02, 0)) for p in front]
        self.b.loft(shirt, [front, back], smooth=False)
        if bow:
            fy = self.front_y(zt - 0.03)
            self.b.sphere(tie, Vector((0, fy + 0.012, zt - 0.03)), (0.045, 0.012, 0.018), 8, 4)
        elif tie_on:
            self.b.box(tie, (0, self.front_y(zt - 0.02) + 0.01, zt - 0.02), (0.03, 0.02, 0.03))
            self.b.beam(tie, (0, self.front_y(zt - 0.03) + 0.009, zt - 0.03), (0, self.front_y(zb + 0.02) + 0.009, zb + 0.02), 0.04, 0.008)

    def collar(self, slot='Shirt'):
        nz = self.P['neck'].z
        r = self.R['neck'][0]
        self.b.loft(slot, [self._ellipse(nz - 0.035, r + 0.022, r + 0.02, yoff=0.004), self._ellipse(nz + 0.045, r + 0.014, r + 0.012, yoff=0.008),
                           self._ellipse(nz + 0.045, r + 0.004, r + 0.002, yoff=0.008), self._ellipse(nz - 0.035, r + 0.01, r + 0.008, yoff=0.004)],
                    closed=True, cap_start=False, cap_end=False)

    def _hs(self):
        return self.P['neck'].z / 1.49

    def lapels(self, slot='Jacket', z_top=1.47, z_bot=1.2, double=False, buttons='Buttons'):
        hs = self._hs()
        zt, zb = z_top * hs, z_bot * hs
        for sx in (-1, 1):
            pts = [Vector((sx * 0.055, 0, zt)), Vector((sx * 0.125, 0, zt - 0.07)), Vector((sx * (0.035 if not double else 0.08), 0, zb))]
            front = [Vector((p.x, self.front_y(p.z, p.x) + 0.01, p.z)) for p in pts]
            back = [p + Vector((0, -0.02, 0)) for p in front]
            self.b.loft(slot, [front, back], smooth=False)
        if double:
            for z in (1.15, 1.06):
                for sx in (-1, 1):
                    zz = z * hs
                    self.b.sphere(buttons, Vector((sx * 0.05, self.front_y(zz, sx * 0.05) + 0.006, zz)), (0.011, 0.006, 0.011), 6, 4)
        else:
            for z in (1.13, 1.04):
                zz = z * hs
                self.b.sphere(buttons, Vector((0.005, self.front_y(zz) + 0.006, zz)), (0.011, 0.006, 0.011), 6, 4)

    def jacket_hem(self, slot='Jacket', length=0.17, flare=0.025):
        z0 = self.P['pel'].z + 0.02
        rx, ry = self.radius_at(z0)
        secs = []
        for (dz, ex) in ((0, 0.012), (-length * 0.5, 0.012 + flare * 0.5), (-length, 0.012 + flare)):
            secs.append(self._ellipse(z0 + dz, rx + ex, ry + ex * 0.8))
        self.b.loft(slot, secs, closed=True, cap_start=False, cap_end=False)

    def _ellipse(self, z, rx, ry, n=20, arc=None, yoff=0.0):
        if arc is None:
            return [Vector((math.cos(2 * math.pi * i / n) * rx, yoff + math.sin(2 * math.pi * i / n) * ry, z)) for i in range(n)]
        a0, a1 = arc
        return [Vector((math.cos(a0 + (a1 - a0) * i / (n - 1)) * rx, yoff + math.sin(a0 + (a1 - a0) * i / (n - 1)) * ry, z)) for i in range(n)]

    def coat(self, slot='Coat', hem_z=0.42, open_front=False, collar='Coat', collar_up=False, wide=0.0, sleeves_slot=None):
        """Long coat lofted over the body. open_front leaves a gap (draped over shoulders)."""
        P = self.P
        sh = P['shR']
        zs = [sh.z + 0.03, P['chest'].z, P['spine'].z, P['pel'].z, (P['pel'].z + hem_z) / 2, hem_z]
        secs_out = []
        for i, z in enumerate(zs):
            rx, ry = self.radius_at(z) if i > 0 else (sh.x * 0.9, 0.11)
            ex = 0.03 + wide + (0.0 if i < 3 else 0.025 * (i - 2) + wide * 0.5)
            if i == 0:
                rx, ry = sh.x + 0.05 + wide, 0.115
                ex = 0
            secs_out.append((z, rx + ex, ry + ex * 0.8))
        if not open_front:
            self.b.loft(slot, [self._ellipse(z, rx, ry) for (z, rx, ry) in secs_out] + [self._ellipse(zs[-1] + 0.0001, 0.001, 0.001)],
                        closed=True, cap_start=False, cap_end=False)
        else:
            a0, a1 = math.radians(90 + 32), math.radians(90 - 32 + 360)
            secs = []
            for (z, rx, ry) in secs_out:
                outer = self._ellipse(z, rx, ry, n=18, arc=(a0, a1))
                inner = list(reversed(self._ellipse(z, rx - 0.012, ry - 0.012, n=18, arc=(a0, a1))))
                secs.append(outer + inner)
            top = secs_out[0]
            self.b.loft(slot, secs, closed=True, cap_start=True, cap_end=True)
        # collar
        nz = P['neck'].z
        if collar_up:
            self.b.loft(collar, [self._ellipse(nz - 0.04, 0.1, 0.085, yoff=-0.01), self._ellipse(nz + 0.05, 0.088, 0.078, yoff=-0.012),
                                 self._ellipse(nz + 0.05, 0.075, 0.066, yoff=-0.012), self._ellipse(nz - 0.04, 0.085, 0.07, yoff=-0.01)],
                        closed=True, cap_start=False, cap_end=False)
        elif collar:
            self.b.loft(collar, [self._ellipse(nz - 0.04, 0.13 + wide, 0.1, yoff=-0.01, arc=(math.radians(110), math.radians(430))),
                                 self._ellipse(nz + 0.0, 0.1 + wide, 0.085, yoff=-0.015, arc=(math.radians(110), math.radians(430))),
                                 self._ellipse(nz - 0.01, 0.085 + wide, 0.07, yoff=-0.015, arc=(math.radians(110), math.radians(430)))],
                        closed=False, smooth=True)
            self.b.loft(collar, [self._ellipse(nz - 0.01, 0.085 + wide, 0.07, yoff=-0.015, arc=(math.radians(110), math.radians(430))),
                                 self._ellipse(nz + 0.0, 0.1 + wide, 0.085, yoff=-0.015, arc=(math.radians(110), math.radians(430))),
                                 self._ellipse(nz - 0.04, 0.13 + wide, 0.1, yoff=-0.01, arc=(math.radians(110), math.radians(430)))],
                        closed=False, smooth=True)

    def fur_collar(self, slot='Fur'):
        P = self.P
        nz = P['neck'].z
        c = Vector((0, -0.01, nz - 0.03))
        self.b.ring(slot, c, (0, 0, 1), 0.15 if self.build != 'heavy' else 0.17,
                    [(0.05 * math.cos(2 * math.pi * k / 8), 0.045 * math.sin(2 * math.pi * k / 8)) for k in range(8)], segs=18)

    def vest(self, slot='Vest', chain='Gold'):
        """Waistcoat: adds a V and buttons; torso region should already be the vest colour."""
        hs = self._hs()
        for z in (1.25, 1.17, 1.09, 1.01):
            zz = z * hs
            self.b.sphere('Buttons', Vector((0.0, self.front_y(zz) + 0.005, zz)), (0.009, 0.005, 0.009), 6, 4)
        if chain:
            z = 1.07 * hs
            self.b.beam(chain, (0.0, self.front_y(z) + 0.006, z), (0.1, self.front_y(z - 0.03, 0.1) + 0.004, z - 0.03), 0.008)

    def suspenders(self, slot='Suspenders'):
        hs = self._hs()
        for sx in (-1, 1):
            pts = [(sx * 0.1, 1.46), (sx * 0.08, 1.3), (sx * 0.07, 1.1), (sx * 0.075, 0.96)]
            pts = [Vector((x, self.front_y(z * hs, x) + 0.004, z * hs)) for x, z in pts]
            for a, c in zip(pts, pts[1:]):
                self.b.beam(slot, a, c, 0.025, 0.006)
            back = [Vector((sx * 0.1, -0.11, 1.46 * hs)), Vector((sx * 0.03, -0.12, 1.2 * hs)), Vector((sx * 0.02, -0.12, 0.98 * hs))]
            for a, c in zip(back, back[1:]):
                self.b.beam(slot, a, c, 0.025, 0.006)

    def rolled_cuffs(self, slot='Shirt'):
        el = self.P['elR']
        for sx in (-1, 1):
            c = Vector((sx * el.x, el.y, el.z - 0.03))
            self.b.ring(slot, c, (sx * 0.15, 0.05, 1), 0.055 if self.build == 'big' else 0.045,
                        [(0.012, -0.02), (0.012, 0.02), (-0.004, 0.02), (-0.004, -0.02)], segs=12)

    def belt(self, slot='Belt', buckle='Gold'):
        z = self.P['pel'].z - 0.02
        rx, ry = self.radius_at(z)
        self.b.loft(slot, [self._ellipse(z - 0.02, rx + 0.006, ry + 0.006), self._ellipse(z + 0.02, rx + 0.006, ry + 0.006)],
                    closed=True, cap_start=False, cap_end=False)
        self.b.box(buckle, (0, ry + 0.01, z), (0.04, 0.008, 0.032))

    def shoes(self, slot='Shoes', spats=None, heels=False):
        for sx in (-1, 1):
            an = self.P['anR' if sx > 0 else 'anL']
            if heels:
                self.b.box(slot, (an.x, 0.04, 0.035), (0.065, 0.2, 0.05), bevel=0.02)
                self.b.box(slot, (an.x, -0.04, 0.03), (0.03, 0.03, 0.06))
            else:
                self.b.box(slot, (an.x, 0.045, 0.042), (0.1, 0.27, 0.085), bevel=0.035, segs=2)
            if spats:
                self.b.box(spats, (an.x, 0.0, 0.1), (0.105, 0.14, 0.08), bevel=0.02)

    def dress(self, slot='Dress', fringe='Fringe', hem_z=0.6):
        P = self.P
        zs = [P['chest'].z + 0.07, P['chest'].z - 0.05, P['spine'].z, P['pel'].z - 0.04, hem_z + 0.12, hem_z]
        secs = []
        for i, z in enumerate(zs):
            rx, ry = self.radius_at(z)
            ex = 0.01 if i < 4 else 0.03 + 0.03 * (i - 3)
            secs.append(self._ellipse(z, rx + ex, ry + ex * 0.8))
        self.b.loft(slot, secs, closed=True, cap_start=True, cap_end=False)
        for z in (P['pel'].z - 0.12, (P['pel'].z + hem_z) / 2 - 0.02, hem_z + 0.04):
            rx, ry = self.radius_at(z)
            e = 0.035 + (P['pel'].z - z) * 0.12
            self.b.loft(fringe, [self._ellipse(z + 0.012, rx + e, ry + e * 0.8), self._ellipse(z - 0.07, rx + e + 0.025, ry + e * 0.8 + 0.02),
                                 self._ellipse(z - 0.07, rx + e + 0.015, ry + e * 0.8 + 0.01), self._ellipse(z + 0.012, rx + e - 0.008, ry + e * 0.8 - 0.008)],
                        closed=True, cap_start=False, cap_end=False)
        for sx in (-1, 1):
            self.b.beam(slot, (sx * 0.08, self.front_y(P['chest'].z + 0.06, sx * 0.08), P['chest'].z + 0.07),
                        (sx * 0.1, -0.04, P['neck'].z - 0.02), 0.018, 0.008)

    def pearls(self, slot='Pearl'):
        nz = self.P['neck'].z
        for i in range(22):
            a = 2 * math.pi * i / 22
            dip = max(0.0, math.sin(a)) ** 2 * 0.14
            p = Vector((math.cos(a) * 0.075, -0.005 + math.sin(a) * 0.075, nz - 0.03 - dip))
            if math.sin(a) > 0:
                p.y = self.front_y(p.z, p.x) + 0.012 if dip > 0.02 else p.y + 0.01
            self.b.sphere(slot, p, (0.011, 0.011, 0.011), 6, 4)

    def stole(self, slot='Fur'):
        nz = self.P['neck'].z
        self.b.ring(slot, Vector((0, -0.01, nz - 0.05)), (0, 0.15, 1), 0.14,
                    [(0.045 * math.cos(2 * math.pi * k / 8), 0.04 * math.sin(2 * math.pi * k / 8)) for k in range(8)], segs=18)

    def gloves(self, slot):
        pass  # hands region handles gloves

    # ------------------------------------------------------------ face extras
    def glasses(self, frame='Metal', lens=None, round_=True):
        hc, s = self.hc, self.hs
        for sx in (-1, 1):
            c = hc + Vector((sx * 0.033, 0.098, 0.018)) * s
            self.b.ring(frame, c, (0, 1, 0), 0.021 * s, [(0.003, -0.002), (0.003, 0.002), (-0.003, 0.002), (-0.003, -0.002)], segs=12)
            if lens:
                self.b.cyl(lens, tuple(c), 0.02 * s, 0.003, segs=12, axis='Y')
            self.b.beam(frame, c + Vector((sx * 0.02, -0.005, 0)), hc + Vector((sx * 0.088, 0.0, 0.02)) * s, 0.004)
        self.b.beam(frame, hc + Vector((-0.012, 0.1, 0.022)) * s, hc + Vector((0.012, 0.1, 0.022)) * s, 0.004)

    def cigar(self, cigarette=False):
        hc, s = self.hc, self.hs
        a = hc + Vector((0.018, 0.098, -0.045)) * s
        d = Vector((0.03, 0.09, -0.012)) if not cigarette else Vector((0.025, 0.07, 0.0))
        r = 0.009 if not cigarette else 0.004
        self.b.tube('Cigar' if not cigarette else 'Paper', a, a + d, r, 8)
        self.b.sphere('Ember', a + d * 1.02, (r * 1.05, r * 1.05, r * 1.05), 6, 4)

    def goggles(self):
        hc, s = self.hc, self.hs
        z = hc.z + 0.072 * s
        for sx in (-1, 1):
            c = Vector((sx * 0.035, hc.y + 0.105 * s, z))
            self.b.cyl('Brass', tuple(c), 0.026, 0.03, segs=12, axis='Y')
            self.b.cyl('Glass', tuple(c + Vector((0, 0.016, 0))), 0.021, 0.004, segs=12, axis='Y')
        self.b.loft('Leather', [self._ellipse_at(hc, z - 0.012, 0.1 * s, 0.112 * s), self._ellipse_at(hc, z + 0.012, 0.1 * s, 0.112 * s)],
                    closed=True, cap_start=False, cap_end=False)

    def _ellipse_at(self, c, z, rx, ry, n=20):
        return [Vector((c.x + math.cos(2 * math.pi * i / n) * rx, c.y + math.sin(2 * math.pi * i / n) * ry, z)) for i in range(n)]

    # ------------------------------------------------------------ props (in the right hand)
    def hand(self, side='R'):
        return Vector(self.P['ha' + side])

    def place(self, local_fn, at, pitch=0.0, yaw=0.0, roll=0.0):
        M = Matrix.Translation(at) @ Matrix.Rotation(yaw, 4, 'Z') @ Matrix.Rotation(pitch, 4, 'X') @ Matrix.Rotation(roll, 4, 'Y')
        local_fn(M)

    def _box(self, slot, M, c, size):
        self.b.box(slot, tuple(M @ Vector(c)), size, rot=M.to_3x3().to_4x4())

    def _tube(self, slot, M, a, c, r, segs=8):
        self.b.tube(slot, M @ Vector(a), M @ Vector(c), r, segs)

    def tommy_gun(self, M):
        self._box('Gunmetal', M, (0, 0.06, 0.065), (0.045, 0.3, 0.065))
        self._tube('Gunmetal', M, (0, 0.2, 0.075), (0, 0.5, 0.075), 0.011)
        self._tube('Gunmetal', M, (0, 0.21, 0.075), (0, 0.38, 0.075), 0.022, 10)
        for k in range(5):
            y = 0.23 + k * 0.033
            self._tube('Gunmetal', M, (0, y, 0.075), (0, y + 0.008, 0.075), 0.027, 10)
        self._tube('Gunmetal', M, (-0.028, 0.13, -0.03), (0.028, 0.13, -0.03), 0.085, 16)   # drum magazine
        self._tube('Wood', M, (0, 0.33, 0.045), (0, 0.35, -0.06), 0.019, 8)                  # fore grip
        self._tube('Wood', M, (0, -0.03, 0.04), (0, -0.06, -0.07), 0.02, 8)                  # pistol grip
        self._box('Wood', M, (0, -0.2, 0.035), (0.04, 0.26, 0.055))                          # stock
        self._box('Wood', M, (0, -0.33, 0.02), (0.042, 0.03, 0.1))

    def lupara(self, M):
        for sx in (-1, 1):
            self._tube('Gunmetal', M, (sx * 0.012, 0.05, 0.04), (sx * 0.012, 0.4, 0.04), 0.012, 8)
        self._box('Gunmetal', M, (0, 0.02, 0.035), (0.04, 0.09, 0.05))
        self._box('Wood', M, (0, 0.18, 0.02), (0.035, 0.18, 0.03))
        self._box('Wood', M, (0, -0.12, 0.0), (0.038, 0.24, 0.06))

    def revolver(self, M):
        self._tube('Gunmetal', M, (0, 0.02, 0.05), (0, 0.17, 0.05), 0.008, 8)
        self._tube('Gunmetal', M, (0, 0.0, 0.045), (0, 0.04, 0.045), 0.02, 10)
        self._tube('Wood', M, (0, -0.02, 0.03), (0, -0.04, -0.05), 0.016, 8)

    def bat(self, M):
        secs = []
        for i in range(8):
            t = i / 7
            r = 0.016 + 0.022 * t ** 1.6
            z = -0.05 + t * 0.85
            secs.append([M @ Vector((math.cos(2 * math.pi * k / 10) * r, math.sin(2 * math.pi * k / 10) * r, -z)) for k in range(10)])
        self.b.loft('Wood', secs)

    def cane(self, M):
        self._tube('Cane', M, (0, 0, 0.02), (0, 0.05, -0.83), 0.012, 8)
        self.b.sphere('Silver', M @ Vector((0, 0, 0.03)), (0.022, 0.022, 0.02), 8, 6)

    def violin_case(self, M):
        secs = []
        for i in range(10):
            t = i / 9
            w = 0.07 + 0.065 * math.sin(math.pi * min(1, t * 1.3)) ** 0.7 * (0.65 + 0.35 * t)
            z = -0.06 - t * 0.74
            secs.append([M @ Vector((x, y, z)) for (x, y) in ((0.045, w), (0.045, -w), (-0.045, -w), (-0.045, w))])
        self.b.loft('Case', secs)
        self._box('Leather', M, (0, 0, -0.04), (0.02, 0.07, 0.03))

    def ledger(self, M):
        self._box('Book', M, (0, 0.04, -0.08), (0.04, 0.17, 0.23))
        self._box('Paper', M, (0, 0.04, -0.08), (0.03, 0.172, 0.225))

    def knuckles(self, side='R'):
        h = self.hand(side)
        self.b.box('Brass', tuple(h + Vector((0, 0.03, 0.0))), (0.07, 0.025, 0.03), bevel=0.008)

    def holder(self, M):
        self._tube('Black', M, (0, 0, 0), (0, 0.06, 0.2), 0.004, 6)
        self._tube('Paper', M, (0, 0.06, 0.2), (0, 0.072, 0.24), 0.005, 6)
        self.b.sphere('Ember', M @ Vector((0, 0.073, 0.245)), (0.005, 0.005, 0.005), 6, 4)


# ---------------------------------------------------------------- palette

BLACK_SUIT = (0.03, 0.03, 0.035)
CHARCOAL = (0.09, 0.09, 0.1)
GREY = (0.22, 0.22, 0.23)
NAVY_PIN = (0.04, 0.05, 0.09)
CREAM = (0.75, 0.7, 0.58)
WHITE_SHIRT = (0.85, 0.84, 0.8)
RED = (0.45, 0.04, 0.04)
SKIN_OLIVE = (0.55, 0.38, 0.27)
SKIN_LIGHT = (0.68, 0.5, 0.38)
HAIR_BLACK = (0.02, 0.018, 0.016)
HAIR_GREY = (0.45, 0.44, 0.42)


def base_colors(skin=SKIN_OLIVE, hair=HAIR_BLACK):
    set_material('Skin', skin, 0.55)
    set_material('Eyes', (0.02, 0.015, 0.012), 0.2)
    set_material('Brows', hair, 0.8)
    set_material('Hair', hair, 0.45)
    set_material('Shoes', (0.02, 0.02, 0.02), 0.25)
    set_material('Shirt', WHITE_SHIRT, 0.6)
    set_material('Buttons', (0.05, 0.05, 0.05), 0.3)
    set_material('Gunmetal', (0.06, 0.06, 0.065), 0.35, 0.8)
    set_material('Wood', (0.3, 0.16, 0.07), 0.45)
    set_material('Gold', (0.8, 0.6, 0.2), 0.25, 1.0)
    set_material('Brass', (0.7, 0.5, 0.2), 0.3, 1.0)
    set_material('Silver', (0.8, 0.8, 0.8), 0.2, 1.0)
    set_material('Metal', (0.6, 0.6, 0.6), 0.25, 1.0)
    set_material('Cigar', (0.25, 0.14, 0.07), 0.7)
    set_material('Paper', (0.9, 0.88, 0.82), 0.7)
    set_material('Ember', (1.0, 0.35, 0.05), 0.5, 0, emit=4.0)
    set_material('Black', (0.02, 0.02, 0.02), 0.4)
    set_material('Leather', (0.2, 0.11, 0.05), 0.5)


# ---------------------------------------------------------------- archetypes

def don():
    """The Don: heavy, older, three-piece suit, overcoat draped over the shoulders, homburg, cane, carnation."""
    base_colors(SKIN_OLIVE, HAIR_GREY)
    set_material('Jacket', BLACK_SUIT, 0.55); set_material('Trousers', BLACK_SUIT, 0.55); set_material('Vest', BLACK_SUIT, 0.55)
    set_material('Coat', (0.06, 0.055, 0.05), 0.75); set_material('Fur', (0.12, 0.09, 0.06), 0.95)
    set_material('Hat', (0.05, 0.05, 0.05), 0.6); set_material('Band', RED, 0.5); set_material('Tie', (0.35, 0.03, 0.03), 0.35)
    set_material('Cane', (0.03, 0.03, 0.03), 0.25); set_material('Flower', (0.7, 0.03, 0.05), 0.6)
    b = Builder('mob_don', [])
    f = Figure('don', 'heavy')
    f.body(b, {'torso': 'Jacket', 'arms': 'Jacket', 'legs': 'Trousers'})
    f.hair('short'); f.mustache(big=True)
    f.shirt_front(); f.lapels(); f.jacket_hem(); f.vest()
    f.coat('Coat', hem_z=0.5, open_front=True, collar=None, wide=0.05)
    f.fur_collar()
    f.hat('homburg')
    f.shoes(spats='Shirt')
    hs = f._hs()
    b.sphere('Flower', Vector((-0.1, f.front_y(1.42 * hs, -0.1) + 0.015, 1.42 * hs)), (0.022, 0.015, 0.022), 8, 6)
    f.place(f.cane, f.hand('R') + Vector((0, 0.02, 0)))
    return b.finish()


def consigliere():
    """Consigliere: slim, grey three-piece, bowler hat, round spectacles, ledger."""
    base_colors(SKIN_LIGHT, HAIR_GREY)
    set_material('Jacket', GREY, 0.6); set_material('Trousers', GREY, 0.6); set_material('Vest', (0.15, 0.15, 0.16), 0.6)
    set_material('Hat', (0.03, 0.03, 0.03), 0.4); set_material('Band', (0.02, 0.02, 0.02), 0.3)
    set_material('Tie', (0.15, 0.03, 0.1), 0.4); set_material('Book', (0.3, 0.07, 0.05), 0.6)
    b = Builder('mob_consigliere', [])
    f = Figure('consigliere', 'slim')
    f.body(b, {'torso': 'Jacket', 'arms': 'Jacket', 'legs': 'Trousers'})
    f.hair('short')
    f.shirt_front(); f.lapels(); f.jacket_hem(); f.vest()
    f.glasses('Metal', round_=True)
    f.hat('bowler')
    f.shoes()
    f.place(f.ledger, f.hand('L'), yaw=0.0)
    return b.finish()


def capo():
    """Capo: double-breasted navy pinstripe, wide fedora with red band, spats, cigar, pocket square."""
    base_colors(SKIN_OLIVE, HAIR_BLACK)
    set_material('Jacket', NAVY_PIN, 0.5); set_material('Trousers', NAVY_PIN, 0.5)
    set_material('Hat', (0.55, 0.5, 0.42), 0.6); set_material('Band', (0.05, 0.05, 0.05), 0.4)
    set_material('Tie', RED, 0.3); set_material('Square', (0.85, 0.85, 0.8), 0.5); set_material('Spats', (0.85, 0.83, 0.78), 0.6)
    b = Builder('mob_capo', [])
    f = Figure('capo', 'avg')
    f.body(b, {'torso': 'Jacket', 'arms': 'Jacket', 'legs': 'Trousers'})
    f.hair('short')
    f.shirt_front(); f.lapels(double=True); f.jacket_hem()
    f.hat('fedora')
    f.cigar()
    f.shoes(spats='Spats')
    hs = f._hs()
    b.box('Square', (-0.11, f.front_y(1.36 * hs, -0.11) + 0.008, 1.36 * hs), (0.05, 0.01, 0.03))
    b.box('Gold', (0.33, 0.06, 0.88), (0.02, 0.02, 0.015))   # pinky ring
    return b.finish()


def soldier():
    """Button man: plain black suit, black fedora, Thompson submachine gun."""
    base_colors(SKIN_OLIVE, HAIR_BLACK)
    set_material('Jacket', BLACK_SUIT, 0.6); set_material('Trousers', BLACK_SUIT, 0.6)
    set_material('Hat', (0.03, 0.03, 0.03), 0.55); set_material('Band', (0.2, 0.2, 0.2), 0.5); set_material('Tie', (0.03, 0.03, 0.03), 0.4)
    b = Builder('mob_soldier', [])
    f = Figure('soldier', 'avg')
    f.body(b, {'torso': 'Jacket', 'arms': 'Jacket', 'legs': 'Trousers'})
    f.hair('short')
    f.shirt_front(); f.lapels(); f.jacket_hem()
    f.hat('fedora')
    f.shoes()
    f.place(f.tommy_gun, f.hand('R') + Vector((-0.03, 0.02, 0.03)), pitch=-0.12, yaw=0.12)
    return b.finish()


def hitman():
    """Hitman: tall and thin, long charcoal overcoat with the collar up, low fedora, sawn-off lupara."""
    base_colors(SKIN_LIGHT, HAIR_BLACK)
    set_material('Jacket', CHARCOAL, 0.6); set_material('Trousers', CHARCOAL, 0.6)
    set_material('Coat', (0.11, 0.1, 0.1), 0.85); set_material('Hat', (0.12, 0.11, 0.1), 0.6); set_material('Band', (0.02, 0.02, 0.02), 0.4)
    set_material('Scarf', (0.4, 0.38, 0.33), 0.9)
    b = Builder('mob_hitman', [])
    f = Figure('hitman', 'slim')
    f.body(b, {'torso': 'Coat', 'arms': 'Coat', 'legs': 'Trousers', 'hands': 'Black'})
    f.hair('short')
    f.coat('Coat', hem_z=0.36, collar='Coat', collar_up=True)
    f.hat('fedora')
    f.shoes()
    f.place(f.lupara, f.hand('R') + Vector((0.0, 0.02, 0.02)), pitch=-1.15)
    return b.finish()


def enforcer():
    """Enforcer: huge, waistcoat over a white shirt with rolled sleeves, slicked hair, baseball bat, brass knuckles."""
    base_colors(SKIN_OLIVE, HAIR_BLACK)
    set_material('Vest', (0.12, 0.1, 0.09), 0.7); set_material('Trousers', (0.14, 0.12, 0.1), 0.7)
    set_material('Belt', (0.05, 0.03, 0.02), 0.4)
    b = Builder('mob_enforcer', [])
    f = Figure('enforcer', 'big')
    f.body(b, {'torso': 'Vest', 'arms': 'Shirt', 'forearms': 'Skin', 'legs': 'Trousers'})
    f.hair('slick')
    f.shirt_front('Shirt', 'Shirt', tie_on=False, width=0.06)
    f.vest(chain=None)
    f.rolled_cuffs()
    f.belt()
    f.shoes()
    f.knuckles('L')
    f.place(f.bat, f.hand('R'), pitch=0.25)
    return b.finish()


def bodyguard():
    """Bodyguard: broad, black suit and tie, dark round glasses, no hat, revolver in hand."""
    base_colors(SKIN_LIGHT, HAIR_BLACK)
    set_material('Jacket', (0.02, 0.02, 0.025), 0.5); set_material('Trousers', (0.02, 0.02, 0.025), 0.5)
    set_material('Tie', (0.02, 0.02, 0.02), 0.3); set_material('Shades', (0.01, 0.01, 0.01), 0.05)
    b = Builder('mob_bodyguard', [])
    f = Figure('bodyguard', 'big')
    f.body(b, {'torso': 'Jacket', 'arms': 'Jacket', 'legs': 'Trousers'})
    f.hair('bald')
    f.shirt_front(); f.lapels(); f.jacket_hem()
    f.glasses('Black', lens='Shades')
    f.shoes()
    f.place(f.revolver, f.hand('R') + Vector((0, 0.01, 0.03)), pitch=-0.9)
    return b.finish()


def wheelman():
    """Wheelman: tan driving duster, chauffeur cap with goggles, leather gloves."""
    base_colors(SKIN_OLIVE, HAIR_BLACK)
    set_material('Coat', (0.48, 0.4, 0.28), 0.9); set_material('Trousers', (0.15, 0.13, 0.11), 0.7)
    set_material('Hat', (0.07, 0.07, 0.08), 0.6); set_material('Band', (0.04, 0.04, 0.04), 0.5); set_material('Visor', (0.02, 0.02, 0.02), 0.2)
    set_material('Gloves', (0.22, 0.12, 0.05), 0.5); set_material('Glass', (0.1, 0.12, 0.13), 0.05)
    b = Builder('mob_wheelman', [])
    f = Figure('wheelman', 'avg')
    f.body(b, {'torso': 'Coat', 'arms': 'Coat', 'legs': 'Trousers', 'hands': 'Gloves'})
    f.hair('short')
    f.coat('Coat', hem_z=0.3, collar='Coat', wide=0.015)
    f.hat('chauffeur')
    f.goggles()
    f.shoes('Leather')
    return b.finish()


def bookie():
    """Bookie: heavyset, striped shirt with sleeve garters, waistcoat, green eyeshade, cigar, cash in hand."""
    base_colors(SKIN_LIGHT, HAIR_BLACK)
    set_material('Shirt', (0.78, 0.72, 0.62), 0.7); set_material('Vest', (0.25, 0.19, 0.12), 0.7)
    set_material('Trousers', (0.18, 0.15, 0.12), 0.7); set_material('Band', (0.05, 0.12, 0.07), 0.5)
    set_material('Visor', (0.08, 0.35, 0.15), 0.3); set_material('Garter', (0.45, 0.05, 0.05), 0.5)
    set_material('Cash', (0.35, 0.45, 0.3), 0.8); set_material('Tie', (0.2, 0.12, 0.05), 0.5)
    b = Builder('mob_bookie', [])
    f = Figure('bookie', 'heavy')
    f.body(b, {'torso': 'Vest', 'arms': 'Shirt', 'legs': 'Trousers'})
    f.hair('bald')
    f.shirt_front(width=0.05)
    f.vest()
    f.hat('eyeshade')
    f.cigar()
    for sx in (-1, 1):
        sh, el = f.P['shR' if sx > 0 else 'shL'], f.P['elR' if sx > 0 else 'elL']
        c = sh.lerp(el, 0.62)
        b.ring('Garter', c, el - sh, 0.064,
               [(0.01, -0.012), (0.01, 0.012), (-0.002, 0.012), (-0.002, -0.012)], segs=12)
    b.box('Cash', tuple(f.hand('R') + Vector((0, 0.05, 0.0))), (0.03, 0.16, 0.07))
    f.shoes()
    return b.finish()


def young_hood():
    """Young hood: slim, cream suit with suspenders showing, straw boater, switchblade."""
    base_colors(SKIN_OLIVE, HAIR_BLACK)
    set_material('Shirt', (0.55, 0.62, 0.72), 0.7); set_material('Trousers', CREAM, 0.7)
    set_material('Suspenders', (0.45, 0.05, 0.05), 0.5)
    set_material('Hat', (0.78, 0.68, 0.45), 0.85); set_material('Band', (0.45, 0.05, 0.05), 0.5)
    b = Builder('mob_hood', [])
    f = Figure('hood', 'slim')
    f.body(b, {'torso': 'Shirt', 'arms': 'Shirt', 'forearms': 'Skin', 'legs': 'Trousers'})
    f.hair('short')
    f.suspenders()
    f.rolled_cuffs()
    f.hat('boater')
    f.shoes('Leather')
    h = f.hand('R')
    b.box('Silver', tuple(h + Vector((0, 0.07, 0.0))), (0.008, 0.1, 0.018))
    b.box('Black', tuple(h + Vector((0, 0.0, 0.0))), (0.018, 0.08, 0.022))
    return b.finish()


def moll():
    """Moll: flapper dress with fringe, cloche hat, fur stole, pearls, cigarette holder."""
    base_colors(SKIN_LIGHT, HAIR_BLACK)
    set_material('Dress', (0.35, 0.03, 0.05), 0.35); set_material('Fringe', (0.25, 0.02, 0.04), 0.3)
    set_material('Hat', (0.05, 0.05, 0.05), 0.5); set_material('Band', (0.7, 0.55, 0.2), 0.3, 1.0)
    set_material('Fur', (0.85, 0.82, 0.78), 0.95); set_material('Pearl', (0.92, 0.9, 0.86), 0.15)
    set_material('Stockings', (0.6, 0.45, 0.35), 0.3); set_material('Lips', (0.55, 0.05, 0.06), 0.3)
    b = Builder('mob_moll', [])
    f = Figure('moll', female=True)
    f.body(b, {'torso': 'Dress', 'arms': 'Skin', 'legs': 'Stockings', 'feet': 'Shoes'})
    f.hair('bob')
    f.dress()
    f.stole()
    f.pearls()
    f.hat('cloche')
    b.sphere('Lips', f.hc + Vector((0, 0.094, -0.045)) * f.hs, (0.018, 0.008, 0.007), 8, 4)
    f.shoes(heels=True)
    f.place(f.holder, f.hand('R') + Vector((0, 0.03, 0.02)), pitch=-0.3)
    return b.finish()


def violin_man():
    """The 'musician': brown overcoat and fedora, carrying a violin case (with what's inside it)."""
    base_colors(SKIN_OLIVE, HAIR_BLACK)
    set_material('Coat', (0.22, 0.15, 0.1), 0.85); set_material('Trousers', (0.12, 0.1, 0.09), 0.7)
    set_material('Hat', (0.2, 0.14, 0.09), 0.65); set_material('Band', (0.04, 0.03, 0.02), 0.5)
    set_material('Case', (0.05, 0.04, 0.035), 0.4)
    b = Builder('mob_musician', [])
    f = Figure('musician', 'avg')
    f.body(b, {'torso': 'Coat', 'arms': 'Coat', 'legs': 'Trousers'})
    f.hair('short'); f.mustache()
    f.coat('Coat', hem_z=0.45, collar='Coat')
    f.hat('fedora')
    f.shoes()
    f.place(f.violin_case, f.hand('R') + Vector((0.03, 0.0, 0.0)))
    return b.finish()


ALL = {
    'mob_don': don, 'mob_consigliere': consigliere, 'mob_capo': capo, 'mob_soldier': soldier,
    'mob_hitman': hitman, 'mob_enforcer': enforcer, 'mob_bodyguard': bodyguard, 'mob_wheelman': wheelman,
    'mob_bookie': bookie, 'mob_hood': young_hood, 'mob_moll': moll, 'mob_musician': violin_man,
}
