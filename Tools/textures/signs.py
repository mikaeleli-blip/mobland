"""Shop sign atlas, rooftop billboard atlas, smoke puff."""
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from texlib import save_srgb, save_normal, save_mask, rgb, fbm

FONT_SERIF = '/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf'
FONT_SANS = '/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf'

SHOP_NAMES = [
    'GROCERY', 'DELICATESSEN', 'BARBER SHOP', 'BAKERY', 'TAILOR', 'PAWN BROKER', 'HARDWARE', 'CAFE',
    'PHARMACY', 'SHOES REPAIRED', 'MEAT MARKET', 'LAUNDRY', 'CIGARS', 'BILLIARDS', 'LUNCHEONETTE', 'FISH MARKET',
]
# board colour, letter colour, neon colour (None = painted only)
SIGN_STYLES = [
    ('#1f3326', '#e8d8a8', None), ('#2a1a14', '#d9b25a', '#ffb347'), ('#e6dcc4', '#5a1d18', None),
    ('#1a1a1a', '#e7e1cf', '#ff4a3d'), ('#5a1d18', '#efe3c2', None), ('#14213a', '#e2c87a', '#4ad1ff'),
    ('#2b2b2b', '#cfc6ac', None), ('#3a2416', '#f0d9a0', '#ff6ec7'),
]

FICTIONAL_ADS = [
    ('GOTHAM CIGARS', 'A REAL SMOKE FOR A REAL MAN', '#7a2b26', '#efe3c2'),
    ('HUDSON GINGER ALE', 'THE PRIDE OF NEW YORK', '#1f3d5a', '#f2e6c8'),
    ('EMPIRE TIRES', 'RIDE ON THE BEST', '#d6c28a', '#1c1c1c'),
    ('DOLLY COLA', '5 CENTS', '#b5281f', '#ffffff'),
    ('ACME LAXATIVES', 'FOR THE WHOLE FAMILY', '#e8e0c8', '#2a4a2e'),
    ('ATLAS INSURANCE', 'SAFE AS A BANK', '#24392b', '#e6d49a'),
    ('ROYAL BISCUITS', 'BAKED IN BROOKLYN', '#3a2a5a', '#f0d48a'),
    ('VELVET SOAP', 'WHITER WASH', '#7fa8b8', '#1a2a3a'),
]


def _fit_font(draw, text, path, max_w, max_h):
    size = max_h
    while size > 8:
        f = ImageFont.truetype(path, size)
        b = draw.textbbox((0, 0), text, font=f)
        if b[2] - b[0] <= max_w and b[3] - b[1] <= max_h:
            return f, b
        size -= 2
    f = ImageFont.truetype(path, 8)
    return f, draw.textbbox((0, 0), text, font=f)


def shop_signs(out):
    """16 boards, each 1024x64 (16:1). Emission: neon letters for some signs, dim back-light for others."""
    W, RH = 1024, 64
    alb = Image.new('RGB', (W, RH * 16))
    emi = Image.new('RGB', (W, RH * 16), (0, 0, 0))
    da, de = ImageDraw.Draw(alb), ImageDraw.Draw(emi)
    rng = np.random.default_rng(7)
    for i, name in enumerate(SHOP_NAMES):
        board, letter, neon = SIGN_STYLES[i % len(SIGN_STYLES)]
        y0 = i * RH
        da.rectangle([0, y0, W, y0 + RH], fill=board)
        da.rectangle([4, y0 + 4, W - 5, y0 + RH - 5], outline=letter, width=2)
        font, b = _fit_font(da, name, FONT_SERIF if i % 2 else FONT_SANS, W - 120, RH - 22)
        tw, th = b[2] - b[0], b[3] - b[1]
        tx, ty = (W - tw) // 2 - b[0], y0 + (RH - th) // 2 - b[1]
        da.text((tx, ty), name, font=font, fill=letter)
        if neon:
            layer = Image.new('RGB', (W, RH), (0, 0, 0))
            ImageDraw.Draw(layer).text((tx, ty - y0), name, font=font, fill=neon)
            glow = layer.filter(ImageFilter.GaussianBlur(3))
            layer = Image.blend(glow, layer, 0.6)
            emi.paste(layer, (0, y0))
        elif rng.random() < 0.6:
            # lamp-lit: letters faintly warm
            de.text((tx, ty), name, font=font, fill=(110, 80, 40))
    a = np.asarray(alb, np.float32) / 255.0
    n = fbm(a.shape[0], a.shape[1], beta=2.0, lo=2, seed=5)
    a *= (0.8 + 0.25 * n)[..., None]
    save_srgb(f'{out}/signs.png', a)
    save_srgb(f'{out}/signs_e.png', np.asarray(emi, np.float32) / 255.0)
    save_normal(f'{out}/signs_n.png', np.full(a.shape[:2], 0.5, np.float32))
    m = np.zeros(a.shape[:2], np.float32)
    save_mask(f'{out}/signs_m.png', m, m + 0.3)


def billboards(out):
    """8 painted rooftop billboards, each 1024x256 (4:1) stacked vertically -> 1024x2048."""
    W, RH = 1024, 256
    img = Image.new('RGB', (W, RH * 8))
    d = ImageDraw.Draw(img)
    for i, (title, tag, bg, fg) in enumerate(FICTIONAL_ADS):
        y0 = i * RH
        d.rectangle([0, y0, W, y0 + RH], fill=bg)
        d.rectangle([10, y0 + 10, W - 11, y0 + RH - 11], outline=fg, width=6)
        f1, b1 = _fit_font(d, title, FONT_SERIF, W - 120, 120)
        d.text(((W - (b1[2] - b1[0])) // 2 - b1[0], y0 + 40 - b1[1] + (120 - (b1[3] - b1[1])) // 2), title, font=f1, fill=fg)
        f2, b2 = _fit_font(d, tag, FONT_SANS, W - 300, 48)
        d.text(((W - (b2[2] - b2[0])) // 2 - b2[0], y0 + 175 - b2[1]), tag, font=f2, fill=fg)
    a = np.asarray(img, np.float32) / 255.0
    n = fbm(a.shape[0], a.shape[1], beta=2.0, lo=2, seed=9)
    peel = np.clip((fbm(a.shape[0], a.shape[1], beta=1.6, lo=6, seed=10) - 0.78) * 6, 0, 1)
    a = a * (0.75 + 0.3 * n)[..., None]
    a = a * (1 - peel[..., None]) + np.array([0.55, 0.5, 0.42], np.float32) * peel[..., None]
    # vertical paper seams
    a[:, ::128] *= 0.8
    save_srgb(f'{out}/billboards.png', a)
    save_normal(f'{out}/billboards_n.png', 0.5 + 0.1 * peel, 2.0)
    m = np.zeros(a.shape[:2], np.float32)
    save_mask(f'{out}/billboards_m.png', m, m + 0.15)


def smoke(out):
    S = 128
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    r = np.sqrt((xx - S / 2) ** 2 + (yy - S / 2) ** 2) / (S / 2)
    n = fbm(S, S, beta=2.0, lo=2, seed=21)
    a = np.clip(1 - r, 0, 1) ** 1.5 * (0.6 + 0.6 * n)
    a = np.clip(a, 0, 1)
    rgba = np.zeros((S, S, 4), np.uint8)
    rgba[..., :3] = 255
    rgba[..., 3] = (a * 255).astype(np.uint8)
    Image.fromarray(rgba, 'RGBA').save(f'{out}/smoke.png')
