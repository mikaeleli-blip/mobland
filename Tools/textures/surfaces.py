"""Ground, roof and material surfaces (tileable, 512 px)."""
import numpy as np
from texlib import *


def asphalt(out, seed=301):
    reseed(seed)
    H = W = 512                  # 8 m
    c = Canvas(H, W)
    n = fbm(H, W, beta=1.8, lo=1, seed=seed)
    fine = fbm(H, W, beta=0.3, lo=60, seed=seed + 1)
    base = rgb('#2b2a28')
    c.albedo = fill(H, W, base) * (0.78 + 0.35 * n + 0.18 * fine)[..., None]
    # aggregate
    agg = speckle(H, W, 0.04, seed=seed + 2)
    c.albedo = lerp(c.albedo, fill(H, W, rgb('#6d6a64')), agg * 0.5)
    # cracks: thin ridges of a band-passed noise
    cr = fbm(H, W, beta=1.4, lo=3, hi=40, seed=seed + 3)
    crack = (np.abs(cr - 0.5) < 0.008).astype(np.float32)
    crack = blur(crack, 1) * (fbm(H, W, beta=2, lo=1, seed=seed + 4) > 0.55)
    c.albedo *= (1 - 0.7 * np.clip(crack * 3, 0, 1))[..., None]
    # patches + oil stains
    p = fbm(H, W, beta=2.6, lo=1, seed=seed + 5)
    patch = (p > 0.72).astype(np.float32)
    c.albedo = lerp(c.albedo, c.albedo * 0.7, patch)
    oil = fbm(H, W, beta=2.4, lo=2, seed=seed + 6)
    c.albedo = lerp(c.albedo, fill(H, W, rgb('#151412')), np.clip((oil - 0.7) * 3, 0, 1) * 0.7)
    c.height = 0.5 + 0.1 * fine + 0.2 * agg - 0.3 * crack + 0.05 * patch
    c.smooth = (0.18 + 0.25 * np.clip((oil - 0.6) * 3, 0, 1) + 0.1 * patch).astype(np.float32)
    c.save(f'{out}/asphalt', normal_strength=3.0)


def cobbles(out, seed=311):
    """Belgian block (granite setts) - 4 m tile."""
    reseed(seed)
    H = W = 512
    c = Canvas(H, W)
    rows, cols = 40, 16
    alb, hgt = stone_blocks(H, W, rows=rows, cols=cols, joint_px=2.5, color=rgb('#5d5a55'), var=0.15, seed=seed,
                            joint_depth=0.35, rough=0.15)
    # rounded tops
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    ch, cw = H / rows, W / cols
    row = np.floor(yy / ch)
    fy = (yy - row * ch) / ch
    fx = ((xx + np.where(row % 2 == 1, cw / 2, 0)) % cw) / cw
    dome = np.sin(np.pi * fy) * np.sin(np.pi * fx)
    c.albedo = alb * (0.8 + 0.25 * dome)[..., None]
    c.height = hgt + 0.2 * dome
    c.smooth = (0.25 + 0.2 * dome).astype(np.float32)
    c.grime(0.3, 0.0, seed=seed + 2)
    c.save(f'{out}/cobbles', normal_strength=5.0)


def sidewalk(out, seed=321):
    """Concrete flags 1.5 m - tile = 6 m (4 x 4 slabs)."""
    reseed(seed)
    H = W = 512
    c = Canvas(H, W)
    n = fbm(H, W, beta=1.6, lo=2, seed=seed)
    fine = fbm(H, W, beta=0.4, lo=60, seed=seed + 1)
    yy, xx = np.mgrid[0:H, 0:W]
    slab = W / 4
    joint = ((xx % slab) < 2) | ((yy % slab) < 2)
    sid = ((yy // slab) * 4 + (xx // slab)).astype(np.int64)
    tone = np.random.default_rng(seed).uniform(0.85, 1.1, 16)[sid]
    base = rgb('#6f6c66')
    c.albedo = fill(H, W, base) * (tone * (0.82 + 0.28 * n + 0.1 * fine))[..., None]
    c.albedo[joint] *= 0.55
    stains = fbm(H, W, beta=2.3, lo=2, seed=seed + 4)
    c.albedo = lerp(c.albedo, c.albedo * 0.6, np.clip((stains - 0.65) * 3, 0, 1))
    gum = speckle(H, W, 0.0015, seed=seed + 5)
    c.albedo = lerp(c.albedo, fill(H, W, rgb('#2c2b29')), blur(gum, 1) * 3)
    cr = fbm(H, W, beta=1.4, lo=4, hi=40, seed=seed + 3)
    crack = blur((np.abs(cr - 0.5) < 0.006).astype(np.float32), 1) * (fbm(H, W, beta=2, lo=1, seed=seed + 6) > 0.6)
    c.albedo *= (1 - 0.6 * np.clip(crack * 3, 0, 1))[..., None]
    c.height = 0.55 + 0.05 * fine - 0.3 * joint - 0.2 * crack
    c.smooth = np.full((H, W), 0.2, np.float32)
    c.save(f'{out}/sidewalk', normal_strength=3.0)


def curb(out, seed=331):
    reseed(seed)
    H = W = 256
    c = Canvas(H, W)
    alb, hgt = stone_blocks(H, W, rows=1, cols=2, joint_px=2, color=rgb('#7b7872'), var=0.05, seed=seed,
                            joint_depth=0.2, rough=0.1)
    c.albedo, c.height = alb, hgt
    c.grime(0.4, 0.0, seed=seed + 1)
    c.save(f'{out}/curb', normal_strength=3.0)


def roof_tar(out, seed=341):
    reseed(seed)
    H = W = 512                  # 6 m
    c = Canvas(H, W)
    n = fbm(H, W, beta=2.0, lo=1, seed=seed)
    fine = fbm(H, W, beta=0.4, lo=80, seed=seed + 1)
    base = rgb('#2f2d2b')
    c.albedo = fill(H, W, base) * (0.75 + 0.4 * n + 0.15 * fine)[..., None]
    # roll seams every ~0.9 m
    yy = np.mgrid[0:H, 0:W][0]
    seam = ((yy % 77) < 3).astype(np.float32)
    c.albedo *= (1 - 0.25 * seam)[..., None]
    # patches (newer, darker tar) and bleached areas
    p = fbm(H, W, beta=2.6, lo=1, seed=seed + 2)
    c.albedo = lerp(c.albedo, fill(H, W, rgb('#1b1a19')), np.clip((p - 0.68) * 4, 0, 1))
    c.albedo = lerp(c.albedo, fill(H, W, rgb('#5e5a52')), np.clip((0.25 - p) * 3, 0, 1) * 0.6)
    gravel = speckle(H, W, 0.05, seed=seed + 3)
    c.albedo = lerp(c.albedo, fill(H, W, rgb('#77736a')), gravel * 0.5)
    c.height = 0.5 + 0.1 * fine + 0.15 * seam + 0.1 * gravel
    c.smooth = (0.12 + 0.2 * np.clip((p - 0.68) * 4, 0, 1)).astype(np.float32)
    c.save(f'{out}/roof_tar', normal_strength=3.0)


def grass(out, seed=351):
    reseed(seed)
    H = W = 512
    c = Canvas(H, W)
    n = fbm(H, W, beta=1.8, lo=1, seed=seed)
    fine = fbm(H, W, beta=0.2, lo=100, seed=seed + 1)
    c.albedo = lerp(fill(H, W, rgb('#3c5228')), fill(H, W, rgb('#5d6b33')), n)
    c.albedo *= (0.75 + 0.4 * fine)[..., None]
    worn = np.clip((fbm(H, W, beta=2.4, lo=1, seed=seed + 2) - 0.68) * 3, 0, 1)
    c.albedo = lerp(c.albedo, fill(H, W, rgb('#6b5a42')), worn * 0.8)
    c.height = 0.5 + 0.3 * fine
    c.smooth[:] = 0.1
    c.save(f'{out}/grass', normal_strength=4.0)


def dirt(out, seed=361):
    reseed(seed)
    H = W = 512
    c = Canvas(H, W)
    n = fbm(H, W, beta=1.9, lo=1, seed=seed)
    fine = fbm(H, W, beta=0.4, lo=60, seed=seed + 1)
    c.albedo = lerp(fill(H, W, rgb('#3e342a')), fill(H, W, rgb('#6c5c49')), n) * (0.8 + 0.3 * fine)[..., None]
    st = speckle(H, W, 0.01, seed=seed + 2)
    st = blur(st, 2) * 4
    c.albedo = lerp(c.albedo, fill(H, W, rgb('#8a8378')), np.clip(st, 0, 1))
    debris = speckle(H, W, 0.002, seed=seed + 3)
    c.albedo = lerp(c.albedo, fill(H, W, rgb('#7a3a2a')), np.clip(blur(debris, 2) * 6, 0, 1))
    c.height = 0.5 + 0.2 * fine + 0.3 * np.clip(st, 0, 1)
    c.smooth[:] = 0.08
    c.save(f'{out}/dirt', normal_strength=4.0)


def planks(out, seed=371):
    """Weathered wood planks running along U - 4 m tile."""
    reseed(seed)
    H = W = 512
    c = Canvas(H, W)
    rng = np.random.default_rng(seed)
    n = fbm(H, W, beta=1.5, lo=2, seed=seed)
    grain = fbm(H // 16, W, beta=1.0, lo=4, seed=seed + 1)
    grain = np.repeat(grain, 16, axis=0)
    pw = 32                       # 0.25 m planks
    yy = np.mgrid[0:H, 0:W][0]
    pid = yy // pw
    gap = (yy % pw) < 2
    tone = rng.uniform(0.75, 1.15, H // pw + 1)[pid]
    base = rgb('#5b4a38')
    c.albedo = fill(H, W, base) * (tone * (0.8 + 0.25 * n + 0.15 * grain))[..., None]
    c.albedo[gap] *= 0.3
    c.height = np.where(gap, 0.2, 0.55 + 0.1 * grain)
    c.smooth[:] = 0.15
    c.save(f'{out}/planks', normal_strength=4.0)


def water(out, seed=381):
    reseed(seed)
    H = W = 512
    c = Canvas(H, W)
    n = fbm(H, W, beta=2.2, lo=2, seed=seed)
    waves = fbm(H, W, beta=1.6, lo=6, hi=60, seed=seed + 1)
    c.albedo = lerp(fill(H, W, rgb('#14211f')), fill(H, W, rgb('#22332f')), n)
    c.height = waves
    c.smooth[:] = 0.93
    c.save(f'{out}/water', normal_strength=2.5)


def steel(out, seed=391):
    """Painted riveted steel (elevated railway) - 2 m tile."""
    reseed(seed)
    H = W = 256
    c = Canvas(H, W)
    n = fbm(H, W, beta=1.8, lo=2, seed=seed)
    base = rgb('#2f3a33')
    c.albedo = fill(H, W, base) * (0.8 + 0.35 * n)[..., None]
    rust = np.clip((fbm(H, W, beta=2.2, lo=2, seed=seed + 1) - 0.62) * 4, 0, 1)
    c.albedo = lerp(c.albedo, fill(H, W, rgb('#5a3420')), rust)
    yy, xx = np.mgrid[0:H, 0:W]
    rivet = (((xx % 32) - 16) ** 2 + ((yy % 64) - 8) ** 2 < 5).astype(np.float32)
    plate = ((yy % 128) < 2).astype(np.float32)
    c.height = 0.5 + 0.4 * rivet - 0.25 * plate + 0.05 * n
    c.albedo *= (1 - 0.3 * plate)[..., None]
    c.smooth = (0.35 - 0.25 * rust).astype(np.float32)
    c.metal = (0.5 - 0.4 * rust).astype(np.float32)
    c.grime(0.3, 0.4, seed=seed + 3)
    c.save(f'{out}/steel', normal_strength=4.0)


def concrete(out, seed=401):
    reseed(seed)
    H = W = 256
    c = Canvas(H, W)
    n = fbm(H, W, beta=1.8, lo=1, seed=seed)
    fine = fbm(H, W, beta=0.3, lo=60, seed=seed + 1)
    c.albedo = fill(H, W, rgb('#625e58')) * (0.8 + 0.3 * n + 0.1 * fine)[..., None]
    c.height = 0.5 + 0.1 * fine
    c.smooth[:] = 0.2
    c.grime(0.3, 0.4, seed=seed + 2)
    c.save(f'{out}/concrete', normal_strength=2.0)
