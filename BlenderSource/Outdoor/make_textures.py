import os, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = r"C:\MyUnityGames\AftershockIsle\AftershockIsle\Assets\Models\Outdoor\Textures"
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(7)
N = 512


def tile_noise(size, octaves=5, base=4, persistence=0.55):
    """Tileable value noise 0..1."""
    total = np.zeros((size, size), np.float32)
    amp = 1.0
    norm = 0.0
    for o in range(octaves):
        cells = base * (2 ** o)
        grid = rng.random((cells, cells)).astype(np.float32)
        img = Image.fromarray((grid * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC)
        # make seamless by wrapping resize: resize a tiled 3x3 and crop centre
        big = np.tile(grid, (3, 3))
        big_img = Image.fromarray((big * 255).astype(np.uint8)).resize((size * 3, size * 3), Image.BICUBIC)
        arr = np.asarray(big_img, np.float32)[size:2 * size, size:2 * size] / 255.0
        total += arr * amp
        norm += amp
        amp *= persistence
    total /= norm
    total = (total - total.min()) / (total.max() - total.min() + 1e-6)
    return total


def to_img(arr):
    arr = np.clip(arr, 0, 1)
    return Image.fromarray((arr * 255).astype(np.uint8), "RGB")


def speckle(shape, strength=0.05):
    return (rng.random(shape).astype(np.float32) - 0.5) * 2 * strength


def draw_cracks(img, count, color, width=1, seg=9, wrap=True):
    d = ImageDraw.Draw(img)
    for _ in range(count):
        x, y = rng.random() * N, rng.random() * N
        ang = rng.random() * math.tau
        pts = [(x, y)]
        for _ in range(seg):
            ang += (rng.random() - 0.5) * 1.1
            step = 10 + rng.random() * 26
            x += math.cos(ang) * step
            y += math.sin(ang) * step
            pts.append((x, y))
        for ox in (-N, 0, N):
            for oy in (-N, 0, N):
                d.line([(px + ox, py + oy) for px, py in pts], fill=color, width=width)
        # branch
        if rng.random() < 0.6:
            bi = rng.integers(2, len(pts) - 1)
            bx, by = pts[bi]
            ba = ang + (rng.random() - 0.5) * 2.2
            bp = [(bx, by)]
            for _ in range(4):
                ba += (rng.random() - 0.5) * 0.9
                bx += math.cos(ba) * 14
                by += math.sin(ba) * 14
                bp.append((bx, by))
            for ox in (-N, 0, N):
                for oy in (-N, 0, N):
                    d.line([(px + ox, py + oy) for px, py in bp], fill=color, width=max(1, width - 1))


# ---------------------------------------------------------------- asphalt
def asphalt():
    n1 = tile_noise(N, 6, 3)
    n2 = tile_noise(N, 4, 8)
    base = 0.17 + n1 * 0.10 + n2 * 0.04
    rgb = np.stack([base * 0.98, base, base * 1.04], -1)
    rgb += speckle(rgb.shape, 0.035)
    # gravel specks
    sp = rng.random((N, N)) > 0.992
    rgb[sp] += 0.12
    # patches (lighter/darker repaired rectangles)
    img = to_img(rgb)
    d = ImageDraw.Draw(img, "RGBA")
    for _ in range(5):
        w, h = rng.integers(60, 160), rng.integers(40, 120)
        x, y = rng.integers(0, N - w), rng.integers(0, N - h)
        tone = int(rng.integers(30, 58))
        d.rectangle([x, y, x + w, y + h], fill=(tone, tone, tone + 2, 70))
    # oil stains
    for _ in range(4):
        r = int(rng.integers(18, 44))
        x, y = int(rng.integers(0, N)), int(rng.integers(0, N))
        d.ellipse([x - r, y - r // 2, x + r, y + r // 2], fill=(12, 12, 14, 80))
    draw_cracks(img, 3, (26, 26, 28), 2)
    draw_cracks(img, 5, (34, 34, 36), 1)
    img = img.filter(ImageFilter.GaussianBlur(0.6))
    img.save(os.path.join(OUT, "outdoor_asphalt.png"))


# ---------------------------------------------------------------- sidewalk slabs
def sidewalk():
    n1 = tile_noise(N, 6, 3)
    base = 0.46 + n1 * 0.10
    rgb = np.stack([base, base * 0.99, base * 0.95], -1)
    rgb += speckle(rgb.shape, 0.03)
    img = to_img(rgb)
    d = ImageDraw.Draw(img, "RGBA")
    cells = 4
    cs = N // cells
    for i in range(cells):
        for j in range(cells):
            tone = int(rng.integers(-14, 14))
            d.rectangle([i * cs, j * cs, (i + 1) * cs, (j + 1) * cs], fill=(128 + tone, 126 + tone, 120 + tone, 34))
    for k in range(cells):
        d.line([(k * cs, 0), (k * cs, N)], fill=(34, 33, 31, 255), width=4)
        d.line([(0, k * cs), (N, k * cs)], fill=(34, 33, 31, 255), width=4)
    # moss and dirt
    for _ in range(14):
        x, y, r = int(rng.integers(0, N)), int(rng.integers(0, N)), int(rng.integers(8, 28))
        d.ellipse([x - r, y - r, x + r, y + r], fill=(60, 72, 40, 40))
    draw_cracks(img, 3, (38, 37, 35), 2)
    img.filter(ImageFilter.GaussianBlur(0.5)).save(os.path.join(OUT, "outdoor_sidewalk.png"))


# ---------------------------------------------------------------- concrete wall
def concrete():
    n1 = tile_noise(N, 6, 3)
    n2 = tile_noise(N, 3, 12)
    base = 0.50 + n1 * 0.12 - n2 * 0.05
    rgb = np.stack([base, base * 0.99, base * 0.96], -1)
    rgb += speckle(rgb.shape, 0.03)
    # vertical rain streaks
    streak = tile_noise(N, 2, 24)
    streak = np.repeat(streak[:1, :], N, axis=0) * 0.25 + streak * 0.75
    rgb -= (streak[..., None] * 0.03)
    img = to_img(rgb)
    d = ImageDraw.Draw(img, "RGBA")
    for k in range(0, N, 128):
        d.line([(0, k), (N, k)], fill=(40, 40, 38, 190), width=3)
        d.line([(k, 0), (k, N)], fill=(40, 40, 38, 110), width=2)
    for _ in range(10):
        x, y = int(rng.integers(0, N)), int(rng.integers(0, N))
        h = int(rng.integers(40, 160))
        d.rectangle([x, y, x + int(rng.integers(6, 20)), y + h], fill=(25, 24, 22, 40))
    draw_cracks(img, 3, (40, 39, 37), 2)
    img.filter(ImageFilter.GaussianBlur(0.5)).save(os.path.join(OUT, "outdoor_concrete.png"))


# ---------------------------------------------------------------- brick
def brick(name, hue):
    n1 = tile_noise(N, 5, 4)
    rgb = np.zeros((N, N, 3), np.float32)
    rgb[..., :] = np.array(hue, np.float32)
    rgb *= (0.8 + n1[..., None] * 0.35)
    img = to_img(rgb)
    d = ImageDraw.Draw(img, "RGBA")
    bh, bw = 32, 64
    for row in range(N // bh):
        off = (bw // 2) if row % 2 else 0
        for col in range(-1, N // bw + 1):
            x0 = col * bw + off
            tone = int(rng.integers(-26, 26))
            d.rectangle([x0, row * bh, x0 + bw, row * bh + bh], fill=(128 + tone, 90 + tone // 2, 80 + tone // 2, 60))
            d.rectangle([x0, row * bh, x0 + bw, row * bh + bh], outline=(58, 52, 48, 255), width=3)
    for _ in range(12):
        x, y = int(rng.integers(0, N)), int(rng.integers(0, N))
        r = int(rng.integers(10, 30))
        d.ellipse([x - r, y - r, x + r, y + r], fill=(20, 18, 16, 36))
    draw_cracks(img, 2, (40, 34, 30), 2)
    img.filter(ImageFilter.GaussianBlur(0.5)).save(os.path.join(OUT, name))


# ---------------------------------------------------------------- plaster
def plaster(name, tint):
    n1 = tile_noise(N, 6, 3)
    n2 = tile_noise(N, 3, 10)
    base = 0.58 + n1 * 0.10 - n2 * 0.06
    rgb = np.stack([base * tint[0], base * tint[1], base * tint[2]], -1)
    rgb += speckle(rgb.shape, 0.025)
    streak = tile_noise(N, 2, 20)
    streak = np.repeat(streak[:1, :], N, axis=0) * 0.2 + streak * 0.8
    rgb -= streak[..., None] * 0.035
    img = to_img(rgb)
    d = ImageDraw.Draw(img, "RGBA")
    # exposed brick where plaster chipped off
    for _ in range(6):
        x, y = int(rng.integers(0, N - 60)), int(rng.integers(0, N - 40))
        w, h = int(rng.integers(20, 70)), int(rng.integers(14, 48))
        pts = [(x + rng.integers(0, w), y + rng.integers(0, h)) for _ in range(7)]
        d.polygon(pts, fill=(122, 78, 62, 215))
    draw_cracks(img, 3, (60, 54, 48), 2)
    img.filter(ImageFilter.GaussianBlur(0.5)).save(os.path.join(OUT, name))


# ---------------------------------------------------------------- palette (8x8 cells of 64px)
PALETTE = [
    # row 0: neutral greys (light -> dark)
    (0.80, 0.80, 0.78), (0.66, 0.66, 0.64), (0.52, 0.52, 0.50), (0.40, 0.40, 0.39),
    (0.30, 0.30, 0.30), (0.20, 0.20, 0.21), (0.12, 0.12, 0.13), (0.05, 0.05, 0.06),
    # row 1: rust / metal
    (0.46, 0.24, 0.14), (0.58, 0.31, 0.16), (0.36, 0.20, 0.13), (0.32, 0.30, 0.28),
    (0.48, 0.50, 0.52), (0.60, 0.62, 0.64), (0.26, 0.28, 0.31), (0.38, 0.33, 0.28),
    # row 2: wood
    (0.45, 0.31, 0.18), (0.36, 0.25, 0.15), (0.55, 0.42, 0.27), (0.26, 0.18, 0.11),
    (0.62, 0.52, 0.36), (0.30, 0.22, 0.16), (0.50, 0.44, 0.34), (0.20, 0.16, 0.12),
    # row 3: faded car paints
    (0.28, 0.38, 0.52), (0.55, 0.20, 0.17), (0.70, 0.68, 0.62), (0.26, 0.40, 0.30),
    (0.62, 0.52, 0.22), (0.34, 0.34, 0.38), (0.20, 0.20, 0.24), (0.46, 0.30, 0.38),
    # row 4: glass / lights / rubber
    (0.10, 0.14, 0.17), (0.18, 0.24, 0.27), (0.28, 0.36, 0.38), (0.70, 0.62, 0.38),
    (0.74, 0.20, 0.12), (0.09, 0.09, 0.09), (0.16, 0.16, 0.15), (0.85, 0.78, 0.55),
    # row 5: paint / signs / hazard
    (0.78, 0.66, 0.16), (0.74, 0.72, 0.66), (0.80, 0.34, 0.10), (0.18, 0.34, 0.20),
    (0.20, 0.28, 0.46), (0.62, 0.14, 0.10), (0.88, 0.86, 0.80), (0.14, 0.14, 0.14),
    # row 6: foliage / dirt / sand
    (0.30, 0.34, 0.16), (0.40, 0.42, 0.20), (0.22, 0.20, 0.12), (0.44, 0.36, 0.24),
    (0.52, 0.45, 0.32), (0.34, 0.28, 0.20), (0.24, 0.21, 0.17), (0.58, 0.54, 0.44),
    # row 7: cloth / tarps / sandbags / emissive-ish
    (0.52, 0.46, 0.32), (0.44, 0.39, 0.28), (0.28, 0.34, 0.40), (0.50, 0.24, 0.20),
    (0.30, 0.40, 0.30), (0.82, 0.55, 0.20), (0.90, 0.82, 0.55), (0.60, 0.60, 0.55),
]


def palette():
    cell = 64
    img = np.zeros((cell * 8, cell * 8, 3), np.float32)
    for idx, c in enumerate(PALETTE):
        r, k = divmod(idx, 8)
        block = np.zeros((cell, cell, 3), np.float32)
        block[:] = np.array(c, np.float32)
        noise = tile_noise(cell, 3, 3)
        block *= (0.93 + noise[..., None] * 0.14)
        block += speckle(block.shape, 0.018)
        img[r * cell:(r + 1) * cell, k * cell:(k + 1) * cell] = block
    to_img(img).save(os.path.join(OUT, "outdoor_palette.png"))


asphalt()
sidewalk()
concrete()
brick("outdoor_brick.png", (0.62, 0.34, 0.26))
plaster("outdoor_plaster_beige.png", (1.0, 0.93, 0.78))
plaster("outdoor_plaster_grey.png", (0.86, 0.88, 0.90))
palette()
print(sorted(os.listdir(OUT)))
