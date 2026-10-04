"""İzekip Shell duvar kağıdı: kabuğun mor->mavi->camgöbeği paletiyle, yumuşak ışık
toplarıyla, koyu zemin üzerinde. 2560x1440 üretir (DesktopWindow UniformToFill ile
her ekrana kendi boyutuna gore kirpar/gerer, tek dosya yeter).
"""
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter

W, H = 2560, 1440
OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Wallpaper.png")

DEEP = (6, 7, 14)        # en koyu zemin
NAVY = (14, 16, 34)
PURPLE = (124, 58, 237)
BLUE = (37, 99, 235)
CYAN = (6, 182, 212)

random.seed(7)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def base_gradient():
    img = Image.new("RGB", (W, H))
    px = img.load()
    for y in range(H):
        t = y / (H - 1)
        # Ust koyu lacivert, alt neredeyse siyah: derinlik hissi.
        row = lerp(NAVY, DEEP, t ** 0.7)
        for x in range(W):
            px[x, y] = row
    return img


def glow(cx, cy, r, color, opacity):
    size = r * 2
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    steps = 60
    for i in range(steps, 0, -1):
        t = i / steps
        a = int(opacity * (1 - t) ** 2.2)
        rr = r * t
        d.ellipse([r - rr, r - rr, r + rr, r + rr], fill=(*color, a))
    layer = layer.filter(ImageFilter.GaussianBlur(r * 0.18))
    return layer, (cx - r, cy - r)


def add_grain(img, amount=5):
    px = img.load()
    for _ in range(int(W * H * 0.0025)):
        x, y = random.randrange(W), random.randrange(H)
        v = random.randint(-amount, amount)
        r, g, b = px[x, y]
        px[x, y] = (max(0, min(255, r + v)), max(0, min(255, g + v)), max(0, min(255, b + v)))


def main():
    img = base_gradient().convert("RGBA")

    # Buyuk, yumusak isik toplari: sol alt mor, sag ust mavi, merkez camgobegi serpintisi.
    glows = [
        (int(W * 0.10), int(H * 1.05), int(H * 1.05), PURPLE, 235),
        (int(W * 0.95), int(H * -0.08), int(H * 0.95), BLUE, 215),
        (int(W * 0.60), int(H * 0.42), int(H * 0.62), CYAN, 150),
        (int(W * 0.26), int(H * 0.12), int(H * 0.36), PURPLE, 120),
        (int(W * 0.80), int(H * 0.70), int(H * 0.40), BLUE, 100),
    ]
    for cx, cy, r, color, op in glows:
        layer, pos = glow(cx, cy, r, color, op)
        img.alpha_composite(layer, pos)

    # Ince cizgi dokusu: Fluent'in "akis cizgileri" hissi, cok dusuk opaklikta.
    lines = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ld = ImageDraw.Draw(lines)
    for i in range(14):
        y0 = int(H * (0.1 + i * 0.065)) + random.randint(-20, 20)
        amp = random.randint(18, 46)
        freq = random.uniform(0.0016, 0.0027)
        phase = random.uniform(0, math.tau)
        pts = []
        for x in range(0, W + 1, 12):
            y = y0 + math.sin(x * freq + phase) * amp
            pts.append((x, y))
        op = random.randint(5, 12)
        ld.line(pts, fill=(255, 255, 255, op), width=1)
    lines = lines.filter(ImageFilter.GaussianBlur(0.6))
    img.alpha_composite(lines)

    # Hafif vinyet: sadece en disteki kenarlar biraz koyulasir.
    vin = Image.new("L", (W, H), 0)
    vd = ImageDraw.Draw(vin)
    m = int(W * 0.22)
    vd.ellipse([-m, -m, W + m, H + m], fill=255)
    vin = vin.filter(ImageFilter.GaussianBlur(180))
    dark = Image.new("RGBA", (W, H), (0, 0, 0, 55))
    img = Image.composite(img, Image.alpha_composite(img, dark), vin)

    final = img.convert("RGB")
    add_grain(final, amount=4)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    final.save(OUT, optimize=True)
    print("saved", OUT, final.size)


if __name__ == "__main__":
    main()
