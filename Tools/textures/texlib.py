"""Small numpy toolkit for tileable procedural PBR textures.

Conventions
-----------
* Arrays are (H, W) or (H, W, 3) float32, row 0 = TOP of the image (as saved).
* "height" maps are in arbitrary units (~0..1); normals are derived from them.
* Everything here is tileable: noise is generated in the frequency domain.
"""
import numpy as np
from PIL import Image

RNG = np.random.default_rng(1925)


def reseed(seed):
    global RNG
    RNG = np.random.default_rng(seed)


# ---------------------------------------------------------------- noise

def fbm(h, w, beta=2.0, lo=2.0, hi=None, seed=None):
    """Tileable fractal noise in [0,1]. beta: spectral falloff, lo/hi: frequency band in cycles per image."""
    rng = np.random.default_rng(seed) if seed is not None else RNG
    white = rng.standard_normal((h, w))
    F = np.fft.fft2(white)
    fy = np.fft.fftfreq(h)[:, None] * h
    fx = np.fft.fftfreq(w)[None, :] * w
    f = np.sqrt(fx * fx + fy * fy)
    f[0, 0] = 1.0
    amp = 1.0 / f ** (beta / 2.0)
    amp[f < lo] = 0.0
    if hi is not None:
        amp[f > hi] = 0.0
    out = np.real(np.fft.ifft2(F * amp))
    out -= out.min()
    m = out.max()
    return (out / m if m > 0 else out).astype(np.float32)


def speckle(h, w, density=0.02, seed=None):
    rng = np.random.default_rng(seed) if seed is not None else RNG
    return (rng.random((h, w)) < density).astype(np.float32)


def blur(a, r=1):
    """Cheap tileable box blur."""
    out = a.copy()
    for _ in range(r):
        out = (out + np.roll(out, 1, 0) + np.roll(out, -1, 0) + np.roll(out, 1, 1) + np.roll(out, -1, 1)) / 5.0
    return out


# ---------------------------------------------------------------- colour helpers

def rgb(hexstr):
    hexstr = hexstr.lstrip('#')
    return np.array([int(hexstr[i:i + 2], 16) / 255.0 for i in (0, 2, 4)], np.float32)


def fill(h, w, color):
    return np.broadcast_to(np.asarray(color, np.float32), (h, w, 3)).copy()


def lerp(a, b, t):
    t = np.asarray(t, np.float32)
    if t.ndim == 2 and np.ndim(a) == 3 or (np.ndim(b) == 3 and t.ndim == 2):
        t = t[..., None]
    return a + (b - a) * t


def mul(col, f):
    return col * (f[..., None] if np.ndim(f) == 2 else f)


# ---------------------------------------------------------------- canvas with layers

class Canvas:
    """albedo (sRGB 0..1), height (0..1), smooth (0..1), metal (0..1), emit (rgb, 0..1)."""

    def __init__(self, h, w):
        self.h, self.w = h, w
        self.albedo = fill(h, w, (0.5, 0.5, 0.5))
        self.height = np.full((h, w), 0.5, np.float32)
        self.smooth = np.full((h, w), 0.15, np.float32)
        self.metal = np.zeros((h, w), np.float32)
        self.emit = np.zeros((h, w, 3), np.float32)

    # rect in pixel coords, y measured from TOP
    def rect(self, x0, y0, x1, y1, albedo=None, height=None, smooth=None, metal=None, emit=None, add_height=None):
        x0, x1 = int(round(x0)), int(round(x1))
        y0, y1 = int(round(y0)), int(round(y1))
        x0, x1 = max(0, min(x0, x1)), min(self.w, max(x0, x1))
        y0, y1 = max(0, min(y0, y1)), min(self.h, max(y0, y1))
        if x1 <= x0 or y1 <= y0:
            return
        sl = (slice(y0, y1), slice(x0, x1))
        if albedo is not None:
            a = np.asarray(albedo, np.float32)
            self.albedo[sl] = a if a.ndim == 1 else a[: y1 - y0, : x1 - x0]
        if height is not None:
            self.height[sl] = height
        if add_height is not None:
            self.height[sl] += add_height
        if smooth is not None:
            self.smooth[sl] = smooth
        if metal is not None:
            self.metal[sl] = metal
        if emit is not None:
            e = np.asarray(emit, np.float32)
            self.emit[sl] = e if e.ndim == 1 else e[: y1 - y0, : x1 - x0]

    def grime(self, amount=0.25, streaks=0.15, seed=None):
        """Overall dirt + vertical rain streaks."""
        h, w = self.h, self.w
        n = fbm(h, w, beta=2.2, lo=1, seed=seed)
        dirt = 1.0 - amount * (n - 0.3).clip(0, 1)
        # vertical streaks: stretch noise vertically
        s = fbm(h // 8 if h >= 64 else h, w, beta=1.6, lo=2, seed=None if seed is None else seed + 1)
        s = np.array(Image.fromarray((s * 255).astype(np.uint8)).resize((w, h), Image.BILINEAR), np.float32) / 255.0
        dirt *= 1.0 - streaks * (s - 0.5).clip(0, 1) * 2.0
        self.albedo *= dirt[..., None]

    def save(self, path_base, normal_strength=4.0, save_emit=False):
        save_srgb(path_base + '.png', self.albedo)
        save_normal(path_base + '_n.png', self.height, normal_strength)
        save_mask(path_base + '_m.png', self.metal, self.smooth)
        if save_emit:
            save_srgb(path_base + '_e.png', self.emit)


# ---------------------------------------------------------------- output

def save_srgb(path, a):
    img = (np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)
    Image.fromarray(img, 'RGB').save(path, optimize=True)


def save_gray(path, a):
    Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8), 'L').save(path, optimize=True)


def save_normal(path, height, strength=4.0):
    """Tangent-space normal map from a height field (OpenGL convention, +Y up)."""
    hgt = height.astype(np.float32)
    dx = (np.roll(hgt, -1, 1) - np.roll(hgt, 1, 1)) * 0.5
    dy = (np.roll(hgt, 1, 0) - np.roll(hgt, -1, 0)) * 0.5   # image row 0 is top => flip for +Y up
    nx, ny, nz = -dx * strength, -dy * strength, np.ones_like(hgt)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx / l, ny / l, nz / l], -1)
    img = ((n * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8)
    Image.fromarray(img, 'RGB').save(path, optimize=True)


def save_mask(path, metal, smooth):
    """URP Lit metallic map: R = metallic, A = smoothness."""
    h, w = metal.shape
    img = np.zeros((h, w, 4), np.uint8)
    img[..., 0] = (np.clip(metal, 0, 1) * 255).astype(np.uint8)
    img[..., 1] = img[..., 0]
    img[..., 2] = img[..., 0]
    img[..., 3] = (np.clip(smooth, 0, 1) * 255).astype(np.uint8)
    Image.fromarray(img, 'RGBA').save(path, optimize=True)


# ---------------------------------------------------------------- patterns

def brick_field(h, w, courses, bricks_per_row, mortar_px=1.0, colors=None, var=0.12, seed=0,
                mortar_color=(0.55, 0.52, 0.47), offset_rows=True):
    """Running-bond brick. Returns (albedo, height, mortar_mask). Exactly tileable for integer counts."""
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    ch = h / courses
    row = np.floor(yy / ch).astype(np.int32)
    fy = yy - row * ch
    bw = w / bricks_per_row
    shift = np.where((row % 2 == 1) & offset_rows, bw * 0.5, 0.0)
    xs = (xx + shift) % w
    col = np.floor(xs / bw).astype(np.int32)
    fx = xs - col * bw

    mort = (fy < mortar_px) | (fx < mortar_px)
    ids = (row * 1009 + col) % (courses * bricks_per_row + 7)
    table = rng.random((courses * bricks_per_row + 7, 4)).astype(np.float32)

    if colors is None:
        colors = [rgb('#8a3b2b')]
    colors = np.asarray(colors, np.float32)
    pick = (table[ids, 0] * len(colors)).astype(np.int32).clip(0, len(colors) - 1)
    base = colors[pick]
    shade = 1.0 + (table[ids, 1] - 0.5) * 2 * var
    base = base * shade[..., None]
    # some darker "clinker" bricks
    clinker = table[ids, 2] < 0.06
    base[clinker] *= 0.6

    n = fbm(h, w, beta=1.2, lo=8, seed=seed + 3)
    base *= (0.9 + 0.2 * n)[..., None]

    mcol = np.asarray(mortar_color, np.float32)
    alb = np.where(mort[..., None], mcol * (0.85 + 0.3 * n[..., None]), base)
    hgt = np.where(mort, 0.35, 0.6 + 0.08 * n + 0.04 * (table[ids, 3] - 0.5))
    # soften brick edges
    edge = np.minimum(np.minimum(fy - mortar_px, ch - fy), np.minimum(fx - mortar_px, bw - fx)).clip(0, 2) / 2
    hgt = np.where(mort, hgt, 0.35 + (hgt - 0.35) * (0.5 + 0.5 * edge))
    return alb.astype(np.float32), hgt.astype(np.float32), mort


def stone_blocks(h, w, rows, cols, joint_px=1.5, color=(0.75, 0.72, 0.65), var=0.05, seed=0, offset_rows=True,
                 joint_depth=0.2, rough=0.06):
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    ch = h / rows
    row = np.floor(yy / ch).astype(np.int32)
    fy = yy - row * ch
    bw = w / cols
    shift = np.where((row % 2 == 1) & offset_rows, bw * 0.5, 0.0)
    xs = (xx + shift) % w
    col = np.floor(xs / bw).astype(np.int32)
    fx = xs - col * bw
    joint = (fy < joint_px) | (fx < joint_px)
    table = rng.random((rows * cols + 3, 2)).astype(np.float32)
    ids = (row * cols + col) % (rows * cols + 3)
    n = fbm(h, w, beta=1.6, lo=4, seed=seed + 5)
    base = np.asarray(color, np.float32) * ((1 + (table[ids, 0] - 0.5) * 2 * var) * (0.92 + 0.16 * n))[..., None]
    alb = np.where(joint[..., None], base * 0.78, base)
    hgt = np.where(joint, 0.5 - joint_depth, 0.5 + rough * n)
    return alb.astype(np.float32), hgt.astype(np.float32)
