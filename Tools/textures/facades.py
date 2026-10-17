"""Facade atlases: 4 bays x 4 floors per texture (upper floors), 4 bays x 1 floor (ground floors).

Each atlas cell is one window bay of one floor. Buildings map UVs so one cell = one bay, and pick a
random bay/floor offset, so the 16 variations (curtains, shades, lit/unlit) don't line up between buildings.
"""
import numpy as np
from texlib import *

BW = 256          # bay width in px
LIGHT_WARM = rgb('#ffb866')
LIGHT_COOL = rgb('#e6e2c8')
LIGHT_DIM = rgb('#c27a3a')

CURTAINS = [rgb('#d9cfb4'), rgb('#e8e2d0'), rgb('#7a2b26'), rgb('#3d5240'), rgb('#4d5866'), rgb('#8a6a3c'), rgb('#c9b77e')]
SHADES = [rgb('#d4c39a'), rgb('#e2d7b8'), rgb('#4f6148'), rgb('#9a8c6a')]


def pick(rng, arr):
    return arr[int(rng.integers(len(arr)))]


# ---------------------------------------------------------------- glass + interiors

def glass(c, x0, y0, x1, y1, rng, lit, light, interior=None, dirty=0.0):
    """Fill a pane area with reflective glass + optional interior (curtains, shade, blinds) + emission."""
    x0, y0, x1, y1 = int(round(x0)), int(round(y0)), int(round(x1)), int(round(y1))
    if x1 <= x0 or y1 <= y0:
        return
    h, w = y1 - y0, x1 - x0
    t = np.linspace(0, 1, h, dtype=np.float32)[:, None] * np.ones((1, w), np.float32)
    s = np.linspace(0, 1, w, dtype=np.float32)[None, :] * np.ones((h, 1), np.float32)
    refl = (1 - t) * 0.55 + 0.25 * np.clip(1 - np.abs((s - t * 0.6) - 0.25) * 4, 0, 1)
    base = rgb('#0d1014')
    sky = rgb('#4a5662') if not lit else rgb('#3a3a36')
    alb = lerp(fill(h, w, base), fill(h, w, sky), refl * 0.6)
    emit = np.zeros((h, w, 3), np.float32)
    if lit:
        glow = (0.55 + 0.45 * (1 - t)) * (0.85 + 0.3 * rng.random())
        emit = light[None, None, :] * glow[..., None]

    if interior is None:
        interior = pick(rng, ['dark', 'dark', 'curtain', 'curtain', 'shade', 'shade', 'blinds', 'lace'])

    if interior == 'curtain':
        col = pick(rng, CURTAINS)
        cw = int(w * rng.uniform(0.18, 0.32))
        folds = 0.75 + 0.25 * np.sin(s * w / 3.0)
        for a, b in ((0, cw), (w - cw, w)):
            alb[:, a:b] = lerp(alb[:, a:b], col * folds[:, a:b, None], 0.75)
            if lit:
                emit[:, a:b] = light * col * 0.9 * folds[:, a:b, None]
    elif interior == 'lace':
        col = rgb('#e9e4d4')
        alb = lerp(alb, fill(h, w, col) * (0.8 + 0.2 * np.sin(s * w / 2.0))[..., None], 0.55)
        if lit:
            emit = light * col * 0.85
    elif interior == 'shade':
        col = pick(rng, SHADES)
        sh = int(h * rng.uniform(0.15, 0.7))
        alb[:sh] = lerp(alb[:sh], fill(sh, w, col), 0.85)
        alb[sh - 2:sh] *= 0.5
        if lit:
            emit[:sh] = light * col * 1.1
    elif interior == 'blinds':
        col = rgb('#cfc6ac')
        bh = int(h * rng.uniform(0.4, 1.0))
        stripes = ((np.arange(bh) % 5) < 3).astype(np.float32)[:, None, None]
        alb[:bh] = lerp(alb[:bh], fill(bh, w, col) * (0.75 + 0.25 * stripes), 0.8)
        if lit:
            emit[:bh] = light * col * (0.4 + 0.6 * (1 - stripes))

    if dirty > 0:
        n = fbm(h, w, beta=2.0, lo=2, seed=int(rng.integers(1 << 30)))
        alb = lerp(alb, fill(h, w, rgb('#55574c')), (n * dirty)[..., None])

    c.rect(x0, y0, x1, y1, albedo=alb, height=0.22, smooth=0.9 - dirty * 0.5, metal=0.0, emit=emit)


def window_double_hung(c, x0, y0, x1, y1, frame_col, rng, lit, light, muntins=1, frame=7, interior=None):
    """Classic NYC double-hung window inside the opening (x0..x1, y0 top..y1 bottom)."""
    # reveal (shadowed opening)
    c.rect(x0, y0, x1, y1, albedo=rgb('#1a1715'), height=0.18, smooth=0.1)
    mid = (y0 + y1) / 2
    inner = (x0 + frame, y0 + frame, x1 - frame, y1 - frame)
    # frame
    c.rect(x0 + 3, y0 + 3, x1 - 3, y1 - 3, albedo=frame_col, height=0.42, smooth=0.35)
    upper = (inner[0], inner[1], inner[2], mid - 4)
    lower = (inner[0], mid + 4, inner[2], inner[3])
    if interior is None:
        interior = pick(rng, ['dark', 'dark', 'curtain', 'curtain', 'shade', 'shade', 'blinds', 'lace'])
    for (a, b, cc, d) in (upper, lower):
        if muntins >= 2:
            m = (a + cc) / 2
            glass(c, a, b, m - 2, d, rng, lit, light, interior)
            glass(c, m + 2, b, cc, d, rng, lit, light, interior)
        else:
            glass(c, a, b, cc, d, rng, lit, light, interior)
    # meeting rail
    c.rect(inner[0], mid - 4, inner[2], mid + 4, albedo=frame_col * 0.9, height=0.45, smooth=0.35)
    # shadow from the head of the opening onto the top sash
    c.albedo[int(y0 + frame):int(y0 + frame + 6), int(inner[0]):int(inner[2])] *= 0.55


def window_grid(c, x0, y0, x1, y1, cols, rows, frame_col, rng, lit_frac, light, dirty=0.3, bar=4, broken=0.03):
    """Industrial steel multi-pane window."""
    c.rect(x0, y0, x1, y1, albedo=rgb('#151413'), height=0.18, smooth=0.1)
    c.rect(x0 + 2, y0 + 2, x1 - 2, y1 - 2, albedo=frame_col, height=0.4, smooth=0.3, metal=0.4)
    pw = (x1 - x0 - 4 - bar) / cols
    ph = (y1 - y0 - 4 - bar) / rows
    lit = rng.random() < lit_frac
    for r in range(rows):
        for q in range(cols):
            a = x0 + 2 + bar + q * pw
            b = y0 + 2 + bar + r * ph
            if rng.random() < broken:
                c.rect(a, b, a + pw - bar, b + ph - bar, albedo=rgb('#060606'), height=0.12, smooth=0.0,
                       emit=light * 0.7 if lit else None)
                continue
            painted = rng.random() < 0.12
            if painted:
                col = rgb('#8d8f7c') * rng.uniform(0.8, 1.1)
                c.rect(a, b, a + pw - bar, b + ph - bar, albedo=col, height=0.24, smooth=0.25,
                       emit=light * col * 0.6 if lit else None)
            else:
                glass(c, a, b, a + pw - bar, b + ph - bar, rng, lit, light, interior='dark', dirty=dirty)


def sill(c, x0, x1, y, color, hgt=9, shadow=10):
    c.rect(x0, y, x1, y + hgt, albedo=color, height=0.85, smooth=0.2)
    c.rect(x0, y + hgt - 2, x1, y + hgt, albedo=color * 0.7)
    c.albedo[int(y + hgt):int(y + hgt + shadow), int(x0):int(x1)] *= np.linspace(0.5, 1.0, int(shadow))[:, None, None]


def lintel(c, x0, x1, y0, y1, color, keystone=False):
    c.rect(x0, y0, x1, y1, albedo=color, height=0.75, smooth=0.2)
    c.rect(x0, y1 - 2, x1, y1, albedo=color * 0.75)
    c.rect(x0, y0, x1, y0 + 2, albedo=color * 1.08)
    if keystone:
        m = (x0 + x1) / 2
        c.rect(m - 7, y0 - 3, m + 7, y1 + 2, albedo=color * 1.04, height=0.85)


def pediment(c, x0, x1, y0, y1, color):
    """Brownstone-style projecting window hood with shadow."""
    c.rect(x0, y0, x1, y1, albedo=color, height=0.8, smooth=0.15)
    c.rect(x0 - 4, y0, x1 + 4, y0 + 6, albedo=color * 1.1, height=0.95)
    c.rect(x0, y1 - 3, x1, y1, albedo=color * 0.6)
    c.albedo[int(y1):int(y1 + 8), int(x0):int(x1)] *= np.linspace(0.45, 1.0, 8)[:, None, None]


def floor_cells(rows, cols, bw, fh):
    for j in range(rows):
        for i in range(cols):
            yield i, j, i * bw, j * fh, (i + 1) * bw, (j + 1) * fh


def lit_choice(rng, frac):
    lit = rng.random() < frac
    r = rng.random()
    light = LIGHT_WARM if r < 0.75 else (LIGHT_COOL if r < 0.9 else LIGHT_DIM)
    return lit, light


# ---------------------------------------------------------------- upper-floor atlases

def brick_tenement(name, brick_colors, stone, frame_col, seed, out):
    reseed(seed)
    rng = np.random.default_rng(seed)
    FH = 320
    H, W = FH * 4, BW * 4
    c = Canvas(H, W)
    alb, hgt, _ = brick_field(H, W, courses=4 * 48, bricks_per_row=4 * 12, colors=brick_colors, seed=seed,
                              mortar_color=rgb('#9a9286') * 0.85)
    c.albedo, c.height = alb, hgt
    c.smooth[:] = 0.12
    for i, j, x0, y0, x1, y1 in floor_cells(4, 4, BW, FH):
        cx = (x0 + x1) / 2
        ww, wh = 100, 172
        wb = y1 - 78            # window bottom (sill top)
        wt = wb - wh
        lit, light = lit_choice(rng, 0.42)
        window_double_hung(c, cx - ww / 2, wt, cx + ww / 2, wb, frame_col, rng, lit, light,
                           muntins=2 if seed % 2 else 1)
        sill(c, cx - ww / 2 - 9, cx + ww / 2 + 9, wb, stone)
        lintel(c, cx - ww / 2 - 8, cx + ww / 2 + 8, wt - 18, wt, stone, keystone=(seed % 3 == 0))
    c.grime(amount=0.35, streaks=0.25, seed=seed + 11)
    c.save(f'{out}/{name}', normal_strength=5.0, save_emit=True)


def brownstone(name, stone_col, frame_col, seed, out):
    reseed(seed)
    rng = np.random.default_rng(seed)
    FH = 330
    H, W = FH * 4, BW * 4
    c = Canvas(H, W)
    alb, hgt = stone_blocks(H, W, rows=4 * 7, cols=4 * 2, joint_px=1.2, color=stone_col, var=0.04, seed=seed,
                            joint_depth=0.08, rough=0.08)
    c.albedo, c.height = alb, hgt
    c.smooth[:] = 0.16
    for i, j, x0, y0, x1, y1 in floor_cells(4, 4, BW, FH):
        cx = (x0 + x1) / 2
        ww, wh = 98, 196
        wb = y1 - 70
        wt = wb - wh
        lit, light = lit_choice(rng, 0.45)
        # stone surround
        c.rect(cx - ww / 2 - 8, wt - 4, cx + ww / 2 + 8, wb, albedo=stone_col * 1.05, height=0.62)
        window_double_hung(c, cx - ww / 2, wt, cx + ww / 2, wb, frame_col, rng, lit, light, muntins=1)
        sill(c, cx - ww / 2 - 12, cx + ww / 2 + 12, wb, stone_col * 1.1, hgt=10)
        pediment(c, cx - ww / 2 - 14, cx + ww / 2 + 14, wt - 30, wt - 4, stone_col * 1.08)
    c.grime(amount=0.25, streaks=0.2, seed=seed + 11)
    c.save(f'{out}/{name}', normal_strength=5.0, save_emit=True)


def office(name, stone_col, frame_col, seed, out):
    reseed(seed)
    rng = np.random.default_rng(seed)
    FH = 320
    H, W = FH * 4, BW * 4
    c = Canvas(H, W)
    alb, hgt = stone_blocks(H, W, rows=4 * 4, cols=4 * 2, joint_px=1.0, color=stone_col, var=0.03, seed=seed,
                            joint_depth=0.06, rough=0.04)
    c.albedo, c.height = alb, hgt
    c.smooth[:] = 0.22
    for i, j, x0, y0, x1, y1 in floor_cells(4, 4, BW, FH):
        cx = (x0 + x1) / 2
        # piers at bay edges
        c.rect(x0, y0, x0 + 26, y1, albedo=stone_col * 1.06, height=0.7)
        c.rect(x1 - 26, y0, x1, y1, albedo=stone_col * 1.06, height=0.7)
        c.rect(x0 + 24, y0, x0 + 27, y1, albedo=stone_col * 0.8)
        ww, wh = 150, 196
        wb = y1 - 70
        wt = wb - wh
        # spandrel panel
        c.rect(cx - ww / 2, wb + 10, cx + ww / 2, y1 - 6, albedo=stone_col * 0.82, height=0.38)
        c.rect(cx - ww / 2 + 10, wb + 18, cx + ww / 2 - 10, y1 - 14, albedo=stone_col * 0.9, height=0.45)
        lit, light = lit_choice(rng, 0.38)
        interior = pick(rng, ['dark', 'blinds', 'blinds', 'shade', 'dark'])
        # tripartite steel window: transom + two lights
        c.rect(cx - ww / 2, wt, cx + ww / 2, wb, albedo=rgb('#141414'), height=0.2)
        c.rect(cx - ww / 2 + 2, wt + 2, cx + ww / 2 - 2, wb - 2, albedo=frame_col, height=0.4, smooth=0.45, metal=0.6)
        tb = wt + 46
        glass(c, cx - ww / 2 + 7, wt + 7, cx + ww / 2 - 7, tb - 3, rng, lit, light, 'dark')
        glass(c, cx - ww / 2 + 7, tb + 3, cx - 3, wb - 7, rng, lit, light, interior)
        glass(c, cx + 3, tb + 3, cx + ww / 2 - 7, wb - 7, rng, lit, light, interior)
        c.albedo[int(wt + 7):int(wt + 12), int(cx - ww / 2 + 7):int(cx + ww / 2 - 7)] *= 0.55
    c.grime(amount=0.2, streaks=0.3, seed=seed + 11)
    c.save(f'{out}/{name}', normal_strength=4.0, save_emit=True)


def warehouse(name, brick_colors, frame_col, seed, out):
    reseed(seed)
    rng = np.random.default_rng(seed)
    FH = 320
    H, W = FH * 4, BW * 4
    c = Canvas(H, W)
    alb, hgt, _ = brick_field(H, W, courses=4 * 52, bricks_per_row=4 * 11, colors=brick_colors, seed=seed,
                              mortar_color=rgb('#8f877b') * 0.8)
    c.albedo, c.height = alb, hgt
    for i, j, x0, y0, x1, y1 in floor_cells(4, 4, BW, FH):
        cx = (x0 + x1) / 2
        ww, wh = 150, 170
        wb = y1 - 70
        wt = wb - wh
        # segmental arch: soldier course of bricks above the opening
        for k in range(15):
            ax = cx - ww / 2 - 6 + k * (ww + 12) / 15
            arc = 10 * np.sin(np.pi * (k + 0.5) / 15)
            c.rect(ax, wt - 20 - arc, ax + (ww + 12) / 15 - 1, wt + 2 - arc,
                   albedo=brick_colors[0] * rng.uniform(0.75, 1.0), height=0.66)
        window_grid(c, cx - ww / 2, wt, cx + ww / 2, wb, 4, 4, frame_col, rng, 0.12, LIGHT_COOL, dirty=0.55)
        sill(c, cx - ww / 2 - 6, cx + ww / 2 + 6, wb, rgb('#7d776d'), hgt=8)
    c.grime(amount=0.45, streaks=0.35, seed=seed + 11)
    c.save(f'{out}/{name}', normal_strength=5.0, save_emit=True)


def factory(name, brick_colors, frame_col, seed, out):
    reseed(seed)
    rng = np.random.default_rng(seed)
    FH = 320
    H, W = FH * 4, BW * 4
    c = Canvas(H, W)
    alb, hgt, _ = brick_field(H, W, courses=4 * 60, bricks_per_row=4 * 12, colors=brick_colors, seed=seed,
                              mortar_color=rgb('#7d766b') * 0.8)
    c.albedo, c.height = alb, hgt
    for i, j, x0, y0, x1, y1 in floor_cells(4, 4, BW, FH):
        cx = (x0 + x1) / 2
        # piers
        c.rect(x0, y0, x0 + 22, y1, add_height=0.12)
        c.rect(x1 - 22, y0, x1, y1, add_height=0.12)
        ww, wh = 196, 230
        wb = y1 - 50
        wt = wb - wh
        window_grid(c, cx - ww / 2, wt, cx + ww / 2, wb, 6, 6, frame_col, rng, 0.3, LIGHT_COOL, dirty=0.6, broken=0.06)
        c.rect(cx - ww / 2 - 4, wt - 12, cx + ww / 2 + 4, wt, albedo=rgb('#6e6a62'), height=0.7)
        sill(c, cx - ww / 2 - 4, cx + ww / 2 + 4, wb, rgb('#6e6a62'), hgt=8)
    c.grime(amount=0.55, streaks=0.45, seed=seed + 11)
    c.save(f'{out}/{name}', normal_strength=5.0, save_emit=True)


# ---------------------------------------------------------------- ground floors (4 bays x 1 floor)

def shopfront(name, paint, seed, out):
    reseed(seed)
    rng = np.random.default_rng(seed)
    FH = 400                     # ~4 m tall ground floor
    H, W = FH, BW * 4
    c = Canvas(H, W)
    c.albedo = fill(H, W, paint * 0.9)
    c.height[:] = 0.6
    c.smooth[:] = 0.35
    for i in range(4):
        x0, x1 = i * BW, (i + 1) * BW
        # cast-iron pilaster on the left edge of every bay
        c.rect(x0, 0, x0 + 22, H, albedo=paint, height=0.8, smooth=0.4, metal=0.3)
        c.rect(x0 + 6, 0, x0 + 16, H, albedo=paint * 0.8, height=0.72)
        door = (i == 1)
        lit = rng.random() < 0.85
        light = LIGHT_WARM if rng.random() < 0.8 else LIGHT_COOL
        gx0, gx1 = x0 + 28, x1 - 6
        # transom
        for k in range(4):
            a = gx0 + k * (gx1 - gx0) / 4
            glass(c, a + 3, 40, a + (gx1 - gx0) / 4 - 3, 92, rng, lit, light * 0.8, 'dark')
        c.rect(gx0, 92, gx1, 104, albedo=paint * 1.1, height=0.75)
        if door:
            # recessed door with glass upper half
            dx0, dx1 = gx0 + 70, gx0 + 170
            c.rect(dx0, 104, dx1, H, albedo=rgb('#2a2017'), height=0.25, smooth=0.3)
            glass(c, dx0 + 12, 120, dx1 - 12, 250, rng, lit, light, 'blinds')
            c.rect(dx0 + 12, 260, dx1 - 12, H - 16, albedo=rgb('#3a2a1c'), height=0.3)
            c.rect(dx0 + 80, 258, dx1 - 16, 266, albedo=rgb('#b08d4a'), metal=0.9, smooth=0.6)
            glass(c, gx0, 104, dx0 - 6, 320, rng, lit, light, 'dark')
            glass(c, dx1 + 6, 104, gx1, 320, rng, lit, light, 'dark')
            c.rect(gx0, 320, dx0 - 6, H - 8, albedo=paint * 0.75, height=0.5)
            c.rect(dx1 + 6, 320, gx1, H - 8, albedo=paint * 0.75, height=0.5)
        else:
            glass(c, gx0, 104, gx1, 320, rng, lit, light, pick(rng, ['dark', 'dark', 'lace', 'shade']))
            # goods on display (silhouettes)
            for _ in range(int(rng.integers(3, 7))):
                bx = rng.uniform(gx0 + 5, gx1 - 30)
                bh = rng.uniform(15, 60)
                col = pick(rng, [rgb('#3b2a1a'), rgb('#5b4a32'), rgb('#2e3b2c'), rgb('#6b5a46'), rgb('#7a2b26')])
                c.rect(bx, 318 - bh, bx + rng.uniform(12, 34), 318, albedo=col, height=0.24, smooth=0.3,
                       emit=(light * col * 1.2) if lit else None)
            # kick plate panels
            c.rect(gx0, 320, gx1, H - 8, albedo=paint * 0.8, height=0.55)
            c.rect(gx0 + 10, 334, gx1 - 10, H - 20, albedo=paint * 0.7, height=0.48)
        c.rect(x0, H - 8, x1, H, albedo=rgb('#5d5a54'), height=0.7)  # sill plate
    c.grime(amount=0.3, streaks=0.1, seed=seed + 11)
    c.save(f'{out}/{name}', normal_strength=4.0, save_emit=True)


def residential_base(name, stone_col, frame_col, seed, out):
    """Rusticated stone ground floor for tenements: windows + a door every 4 bays."""
    reseed(seed)
    rng = np.random.default_rng(seed)
    FH = 360
    H, W = FH, BW * 4
    c = Canvas(H, W)
    alb, hgt = stone_blocks(H, W, rows=6, cols=4 * 2, joint_px=3, color=stone_col, var=0.06, seed=seed,
                            joint_depth=0.25, rough=0.1)
    c.albedo, c.height = alb, hgt
    for i in range(4):
        x0, x1 = i * BW, (i + 1) * BW
        cx = (x0 + x1) / 2
        lit, light = lit_choice(rng, 0.5)
        if i == 2:
            # entrance door with transom
            c.rect(cx - 62, 50, cx + 62, H, albedo=stone_col * 1.1, height=0.7)
            c.rect(cx - 50, 64, cx + 50, H, albedo=rgb('#1c1612'), height=0.2)
            glass(c, cx - 44, 70, cx + 44, 110, rng, lit, light, 'dark')
            c.rect(cx - 44, 116, cx + 44, H, albedo=rgb('#3b2c1f'), height=0.32, smooth=0.3)
            c.rect(cx - 38, 126, cx - 4, H - 10, albedo=rgb('#33261a'), height=0.28)
            c.rect(cx + 4, 126, cx + 38, H - 10, albedo=rgb('#33261a'), height=0.28)
            glass(c, cx - 34, 140, cx - 8, 230, rng, lit, light, 'lace')
            glass(c, cx + 8, 140, cx + 34, 230, rng, lit, light, 'lace')
        else:
            window_double_hung(c, cx - 50, 110, cx + 50, 280, frame_col, rng, lit, light)
            sill(c, cx - 58, cx + 58, 280, stone_col * 1.15, hgt=9)
            # iron bars on ground floor windows
            for k in range(6):
                bx = cx - 45 + k * 18
                c.rect(bx, 108, bx + 3, 282, albedo=rgb('#141414'), height=0.75, metal=0.6, smooth=0.4)
    c.grime(amount=0.4, streaks=0.15, seed=seed + 11)
    c.save(f'{out}/{name}', normal_strength=5.0, save_emit=True)


def loading_doors(name, brick_colors, paint, seed, out):
    reseed(seed)
    rng = np.random.default_rng(seed)
    FH = 420
    H, W = FH, BW * 4
    c = Canvas(H, W)
    alb, hgt, _ = brick_field(H, W, courses=64, bricks_per_row=4 * 11, colors=brick_colors, seed=seed)
    c.albedo, c.height = alb, hgt
    for i in range(4):
        x0, x1 = i * BW, (i + 1) * BW
        cx = (x0 + x1) / 2
        dw = 196
        c.rect(cx - dw / 2 - 8, 56, cx + dw / 2 + 8, 72, albedo=rgb('#5a5650'), height=0.8, metal=0.6)  # steel lintel
        c.rect(cx - dw / 2, 72, cx + dw / 2, H, albedo=rgb('#151311'), height=0.2)
        open_door = rng.random() < 0.15
        if open_door:
            lit = rng.random() < 0.5
            c.rect(cx - dw / 2 + 4, 76, cx + dw / 2 - 4, H, albedo=rgb('#0b0a09'), height=0.15,
                   emit=LIGHT_DIM * 0.6 if lit else None)
            continue
        # two sliding plank doors
        col = paint * rng.uniform(0.85, 1.1)
        for a, b in ((cx - dw / 2 + 4, cx - 2), (cx + 2, cx + dw / 2 - 4)):
            planks = ((np.arange(int(b - a)) % 16) < 1).astype(np.float32)
            n = fbm(H - 76, int(b - a), beta=2.0, lo=2, seed=int(rng.integers(1 << 30)))
            pa = fill(H - 76, int(b - a), col) * (0.85 + 0.25 * n)[..., None]
            pa *= (1 - 0.45 * planks)[None, :, None]
            c.rect(a, 76, b, H, albedo=pa, height=0.4, smooth=0.2)
            # cross brace
            c.rect(a + 6, (76 + H) / 2 - 6, b - 6, (76 + H) / 2 + 6, albedo=col * 0.8, height=0.48)
        c.rect(cx - dw / 2, 74, cx + dw / 2, 80, albedo=rgb('#2c2c2c'), height=0.6, metal=0.8, smooth=0.5)  # rail
    c.grime(amount=0.55, streaks=0.2, seed=seed + 11)
    c.save(f'{out}/{name}', normal_strength=5.0, save_emit=True)


def office_lobby(name, stone_col, seed, out):
    reseed(seed)
    rng = np.random.default_rng(seed)
    FH = 460
    H, W = FH, BW * 4
    c = Canvas(H, W)
    granite = rgb('#4a4744')
    alb, hgt = stone_blocks(H, W, rows=5, cols=4 * 2, joint_px=1.5, color=granite, var=0.03, seed=seed,
                            joint_depth=0.1, rough=0.02)
    c.albedo, c.height = alb, hgt
    c.smooth[:] = 0.6
    bronze = rgb('#5a4426')
    for i in range(4):
        x0, x1 = i * BW, (i + 1) * BW
        cx = (x0 + x1) / 2
        lit = rng.random() < 0.7
        light = LIGHT_WARM
        c.rect(cx - 104, 40, cx + 104, H - 30, albedo=bronze, height=0.42, smooth=0.6, metal=0.8)
        glass(c, cx - 96, 48, cx + 96, 120, rng, lit, light, 'dark')
        if i == 0:
            # revolving door
            c.rect(cx - 96, 128, cx + 96, H - 38, albedo=rgb('#0d0c0b'), height=0.2)
            glass(c, cx - 70, 140, cx + 70, H - 44, rng, lit, light, 'dark')
            c.rect(cx - 2, 140, cx + 2, H - 44, albedo=bronze * 1.3, metal=0.9, smooth=0.7)
        else:
            glass(c, cx - 96, 128, cx - 2, H - 38, rng, lit, light, 'dark')
            glass(c, cx + 2, 128, cx + 96, H - 38, rng, lit, light, 'dark')
    c.grime(amount=0.15, streaks=0.1, seed=seed + 11)
    c.save(f'{out}/{name}', normal_strength=3.0, save_emit=True)


def basement_brownstone(name, stone_col, seed, out):
    reseed(seed)
    rng = np.random.default_rng(seed)
    FH = 160
    H, W = FH, BW * 4
    c = Canvas(H, W)
    alb, hgt = stone_blocks(H, W, rows=3, cols=4 * 2, joint_px=3, color=stone_col * 0.85, var=0.05, seed=seed,
                            joint_depth=0.3, rough=0.12)
    c.albedo, c.height = alb, hgt
    for i in range(4):
        cx = (i + 0.5) * BW
        lit, light = lit_choice(rng, 0.4)
        c.rect(cx - 48, 30, cx + 48, 130, albedo=rgb('#151210'), height=0.2)
        glass(c, cx - 42, 36, cx + 42, 124, rng, lit, light)
        for k in range(5):
            bx = cx - 40 + k * 20
            c.rect(bx, 28, bx + 3, 132, albedo=rgb('#151515'), height=0.8, metal=0.6)
    c.grime(amount=0.45, streaks=0.1, seed=seed + 11)
    c.save(f'{out}/{name}', normal_strength=5.0, save_emit=True)


# ---------------------------------------------------------------- blank walls (2 m x 2 m tiles, 128 px/m)

def brick_plain(name, brick_colors, seed, out, ghost_ad=False):
    reseed(seed)
    H = W = 256
    c = Canvas(H, W)
    alb, hgt, _ = brick_field(H, W, courses=30, bricks_per_row=10, colors=brick_colors, seed=seed,
                              mortar_color=rgb('#958d80') * 0.85)
    c.albedo, c.height = alb, hgt
    c.grime(amount=0.35, streaks=0.3, seed=seed + 1)
    c.save(f'{out}/{name}', normal_strength=5.0)


def stone_plain(name, color, seed, out, rows=6, cols=3):
    reseed(seed)
    H = W = 256
    c = Canvas(H, W)
    alb, hgt = stone_blocks(H, W, rows=rows, cols=cols, joint_px=1.2, color=color, var=0.04, seed=seed,
                            joint_depth=0.08, rough=0.05)
    c.albedo, c.height = alb, hgt
    c.smooth[:] = 0.2
    c.grime(amount=0.25, streaks=0.25, seed=seed + 1)
    c.save(f'{out}/{name}', normal_strength=4.0)
