"""Load the MakeHuman hm08 base mesh (CC0) and apply shape targets, without MakeHuman itself.

Coordinates are converted to Blender metres with the character facing +Y, Z up, character's right = +X.
"""
import os
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))


def _mh_to_blender(x, y, z):
    # MakeHuman: decimetres, Y up, facing +Z.  -> metres, Z up, facing +Y (handedness preserved)
    return Vector((-x * 0.1, z * 0.1, y * 0.1))


class MHMesh:
    def __init__(self, path=os.path.join(HERE, 'base.obj')):
        self.coords = []           # raw MH coords
        self.uvs = []
        self.groups = {}           # group -> list of faces [(vi..), (ti..)]
        group = None
        with open(path) as f:
            for line in f:
                if line.startswith('v '):
                    _, x, y, z = line.split()[:4]
                    self.coords.append([float(x), float(y), float(z)])
                elif line.startswith('vt '):
                    _, u, v = line.split()[:3]
                    self.uvs.append((float(u), float(v)))
                elif line.startswith('g '):
                    group = line.split()[1]
                    self.groups.setdefault(group, [])
                elif line.startswith('f '):
                    vs, ts = [], []
                    for tok in line.split()[1:]:
                        parts = tok.split('/')
                        vs.append(int(parts[0]) - 1)
                        ts.append(int(parts[1]) - 1 if len(parts) > 1 and parts[1] else -1)
                    self.groups[group].append((vs, ts))

    def apply_target(self, name, weight=1.0):
        if weight == 0:
            return
        path = os.path.join(HERE, name if name.endswith('.target') else name + '.target')
        with open(path) as f:
            for line in f:
                if not line.strip() or line.startswith('#'):
                    continue
                i, dx, dy, dz = line.split()
                c = self.coords[int(i)]
                c[0] += float(dx) * weight
                c[1] += float(dy) * weight
                c[2] += float(dz) * weight

    def verts(self):
        vs = [_mh_to_blender(*c) for c in self.coords]
        # stand on the ground
        body_ids = {i for (f, _) in self.groups['body'] for i in f}
        zmin = min(vs[i].z for i in body_ids)
        for v in vs:
            v.z -= zmin
        return vs

    def joints(self, verts):
        out = {}
        for g, faces in self.groups.items():
            if g.startswith('joint-'):
                ids = {i for (f, _) in faces for i in f}
                out[g[6:]] = sum((verts[i] for i in ids), Vector()) / len(ids)
        return out


def build_male(build='avg'):
    """Return (MHMesh, verts in Blender coords, joints)."""
    m = MHMesh()
    m.apply_target('caucasian-male-young', 1.0)
    if build == 'heavy':
        m.apply_target('universal-male-young-averagemuscle-maxweight', 0.75)
    elif build == 'slim':
        m.apply_target('universal-male-young-minmuscle-averageweight', 0.35)
        m.apply_target('male-young-averagemuscle-averageweight-maxheight', 0.25)
    elif build == 'strong':
        m.apply_target('universal-male-young-maxmuscle-averageweight', 0.5)
    else:
        m.apply_target('universal-male-young-maxmuscle-averageweight', 0.2)
        m.apply_target('male-young-averagemuscle-averageweight-maxheight', 0.15)
    v = m.verts()
    return m, v, m.joints(v)
