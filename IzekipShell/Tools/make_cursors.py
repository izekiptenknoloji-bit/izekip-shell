"""İzekip Shell imlec seti: Assets/Cursors altina .cur ve .ani dosyalari uretir.

Her imlec 32 birimlik bir izgarada tanimlanir, 512 px'te cizilip 32-128 px boyutlarina
kucultulur. Gorunum: mor-mavi gecisli dolgu, yuvarlak beyaz kontur, yumusak golge.
Calistirma: python Tools/make_cursors.py
"""
import io
import math
import os
import struct

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

CANVAS = 512
U = CANVAS / 32  # bir izgara birimi kac piksel
SIZES = [32, 48, 64, 96, 128]
ANI_SIZES = [32, 48, 64, 96]
OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Cursors")

TOP = (139, 92, 246)     # mor
BOTTOM = (59, 130, 246)  # mavi


def p(x, y):
    return (x * U, y * U)


def blank():
    return Image.new("L", (CANVAS, CANVAS), 0)


def poly(points):
    m = blank()
    ImageDraw.Draw(m).polygon([p(*q) for q in points], fill=255)
    return m


def rrect(x0, y0, x1, y1, r):
    m = blank()
    ImageDraw.Draw(m).rounded_rectangle([*p(x0, y0), *p(x1, y1)], radius=r * U, fill=255)
    return m


def line(points, width):
    m = blank()
    d = ImageDraw.Draw(m)
    pts = [p(*q) for q in points]
    d.line(pts, fill=255, width=int(width * U), joint="curve")
    for q in (pts[0], pts[-1]):
        r = width * U / 2
        d.ellipse([q[0] - r, q[1] - r, q[0] + r, q[1] + r], fill=255)
    return m


def ellipse(x0, y0, x1, y1):
    m = blank()
    ImageDraw.Draw(m).ellipse([*p(x0, y0), *p(x1, y1)], fill=255)
    return m


def union(*masks):
    out = masks[0]
    for m in masks[1:]:
        out = ImageChops.lighter(out, m)
    return out


def minus(a, b):
    return ImageChops.subtract(a, b)


def dilate(mask, units):
    # Bulanik + esik: yuvarlak koseli genisletme.
    blurred = mask.filter(ImageFilter.GaussianBlur(units * U * 0.6))
    return blurred.point(lambda v: 255 if v > 18 else int(v * 255 / 18))


def rotate(mask, angle):
    return mask.rotate(angle, resample=Image.BICUBIC, center=(CANVAS / 2, CANVAS / 2))


def gradient():
    g = Image.new("RGB", (CANVAS, CANVAS))
    d = ImageDraw.Draw(g)
    for y in range(CANVAS):
        t = y / (CANVAS - 1)
        d.line([(0, y), (CANVAS, y)], fill=tuple(int(TOP[i] + (BOTTOM[i] - TOP[i]) * t) for i in range(3)))
    return g


GRADIENT = gradient()

# Sekiller 32 birimlik tuvali neredeyse dolduruyordu; varsayilan Windows imlecine
# gore cok iri duruyordu. Her imleci kendi tiklama noktasinin (hotspot) etrafinda
# kuculterek hem boyutu azaltiyoruz hem de tiklanan piksel yerinde kaliyor.
SHRINK = 0.68


def shrink(img, hot, factor=SHRINK):
    hx, hy = hot[0] * U, hot[1] * U
    small = img.resize((int(CANVAS * factor), int(CANVAS * factor)), Image.LANCZOS)
    off = (int(hx - hx * factor), int(hy - hy * factor))
    canvas = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    canvas.paste(small, off, small)
    return canvas


def render(shape, outline=1.5, extra=None):
    """shape: dolgu maskesi. extra: (maske, renk) ciftleri, dolgunun ustune."""
    edge = dilate(shape, outline)
    img = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))

    shadow = ImageChops.offset(edge, int(0.5 * U), int(1.1 * U)).filter(ImageFilter.GaussianBlur(1.3 * U))
    img.paste((0, 0, 0, 255), (0, 0), shadow.point(lambda v: int(v * 0.42)))
    img.paste((255, 255, 255, 255), (0, 0), edge)
    img.paste(GRADIENT, (0, 0), shape)
    for mask, color in extra or []:
        img.paste(color, (0, 0), mask)
    return img


def scaled(img, size):
    return img.resize((size, size), Image.LANCZOS)


# ---- .cur / .ani yazimi ----

def dib(img):
    w, h = img.size
    px = img.load()
    xor = bytearray()
    for y in range(h - 1, -1, -1):
        for x in range(w):
            r, g, b, a = px[x, y]
            xor += bytes((b, g, r, a))
    row = ((w + 31) // 32) * 4
    andmask = bytearray()
    for y in range(h - 1, -1, -1):
        bits = bytearray(row)
        for x in range(w):
            if px[x, y][3] == 0:
                bits[x // 8] |= 0x80 >> (x % 8)
        andmask += bits
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, len(xor) + len(andmask), 0, 0, 0, 0)
    return header + bytes(xor) + bytes(andmask)


def cur_bytes(img, hot, sizes):
    images = [(s, dib(scaled(img, s))) for s in sizes]
    out = io.BytesIO()
    out.write(struct.pack("<HHH", 0, 2, len(images)))
    offset = 6 + 16 * len(images)
    for s, data in images:
        hx, hy = round(hot[0] / 32 * s), round(hot[1] / 32 * s)
        out.write(struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, hx, hy, len(data), offset))
        offset += len(data)
    for _, data in images:
        out.write(data)
    return out.getvalue()


def chunk(tag, data):
    pad = b"\0" if len(data) % 2 else b""
    return tag + struct.pack("<I", len(data)) + data + pad


def ani_bytes(frames, hot, jiffies):
    anih = struct.pack("<IIIIIIIII", 36, len(frames), len(frames), 0, 0, 0, 0, jiffies, 1)
    icons = b"".join(chunk(b"icon", cur_bytes(f, hot, ANI_SIZES)) for f in frames)
    body = b"ACON" + chunk(b"anih", anih) + chunk(b"LIST", b"fram" + icons)
    return b"RIFF" + struct.pack("<I", len(body)) + body


def save(name, data):
    with open(os.path.join(OUT, name), "wb") as f:
        f.write(data)


# ---- Sekiller ----

ARROW = [(3, 2), (3, 24.5), (8.6, 19.4), (12.4, 28.2), (16.2, 26.6), (12.5, 18), (19.8, 18)]


def arrow_mask():
    return poly(ARROW)


def hand_mask():
    finger = rrect(9.2, 1.6, 13.6, 17, 2.2)
    f2 = rrect(13.2, 10.2, 17.2, 17, 2)
    f3 = rrect(16.8, 11.2, 20.6, 17.5, 1.9)
    f4 = rrect(20.2, 12.8, 23.8, 18.5, 1.8)
    palm = rrect(8, 14, 23.8, 28.5, 4.5)
    thumb = rotate(rrect(3.2, 13.6, 7.6, 23.5, 2.2), 0)
    thumb = union(thumb, poly([(5.4, 22), (9, 26.5), (9.5, 18), (7, 17)]))
    return union(finger, f2, f3, f4, palm, thumb)


def ibeam_mask():
    return union(line([(16, 6.5), (16, 25.5)], 2.2), line([(12.5, 5.2), (19.5, 5.2)], 2.2), line([(12.5, 26.8), (19.5, 26.8)], 2.2))


def cross_mask():
    return union(line([(16, 3), (16, 12.5)], 2), line([(16, 19.5), (16, 29)], 2),
                 line([(3, 16), (12.5, 16)], 2), line([(19.5, 16), (29, 16)], 2), ellipse(14.9, 14.9, 17.1, 17.1))


def double_arrow_h():
    shaft = rrect(8, 14.6, 24, 17.4, 1.2)
    left = poly([(3, 16), (10, 9.5), (10, 22.5)])
    right = poly([(29, 16), (22, 9.5), (22, 22.5)])
    return union(shaft, left, right)


def move_mask():
    # Dort kucuk ok: buyuk uclar birlesip baklava gibi gorunmesin.
    shaft = union(rrect(7, 15, 25, 17, 1), rrect(15, 7, 17, 25, 1))
    heads = poly([(3, 16), (8.5, 11.8), (8.5, 20.2)])
    return union(shaft, heads, rotate(heads, 90), rotate(heads, 180), rotate(heads, 270))


def no_mask():
    ring = minus(ellipse(4, 4, 28, 28), ellipse(8, 8, 24, 24))
    slash = rotate(rrect(5.5, 14.4, 26.5, 17.6, 1), 45)
    return union(ring, slash)


def up_mask():
    return union(poly([(16, 2), (24.5, 12), (19, 12), (19, 29), (13, 29), (13, 12), (7.5, 12)]))


def spinner(cx, cy, r, width, phase):
    """Halka izi + donen yay. Maskeler: (iz, yay)."""
    box = [*p(cx - r, cy - r), *p(cx + r, cy + r)]
    track = blank()
    ImageDraw.Draw(track).ellipse(box, outline=255, width=int(width * U))
    arc = blank()
    start = phase * 360 - 90
    ImageDraw.Draw(arc).arc(box, start, start + 110, fill=255, width=int(width * U))
    return track, arc


def help_extra():
    badge = ellipse(17.5, 17.5, 30.5, 30.5)
    q = blank()
    font = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", int(10.5 * U))
    ImageDraw.Draw(q).text(p(24, 24.2), "?", font=font, fill=255, anchor="mm")
    return badge, q


def main():
    os.makedirs(OUT, exist_ok=True)
    preview = []

    def static(name, mask, hot, outline=1.5, extra=None):
        img = shrink(render(mask, outline, extra), hot)
        save(name, cur_bytes(img, hot, SIZES))
        preview.append(img)

    static("arrow.cur", arrow_mask(), (3, 2))
    static("hand.cur", hand_mask(), (11.4, 2))
    static("ibeam.cur", ibeam_mask(), (16, 16), outline=1.3)
    static("cross.cur", cross_mask(), (16, 16), outline=1.3)
    h = double_arrow_h()
    static("size_we.cur", h, (16, 16))
    static("size_ns.cur", rotate(h, 90), (16, 16))
    static("size_nwse.cur", rotate(h, -45), (16, 16))
    static("size_nesw.cur", rotate(h, 45), (16, 16))
    static("move.cur", move_mask(), (16, 16))
    static("no.cur", no_mask(), (16, 16))
    static("up.cur", up_mask(), (16, 2))

    badge, q = help_extra()
    help_img = shrink(render(union(arrow_mask(), badge), 1.5, [(q, (255, 255, 255, 255))]), (3, 2))
    save("help.cur", cur_bytes(help_img, (3, 2), SIZES))
    preview.append(help_img)

    # Bekleme: koyu halka uzerinde beyaz donen yay. Calisiyor: ok + kucuk halka.
    frames, busy = 18, []
    for i in range(frames):
        track, arc = spinner(16, 16, 10.5, 3.6, i / frames)
        disc = ellipse(4.2, 4.2, 27.8, 27.8)
        img = shrink(render(minus(disc, ellipse(9.6, 9.6, 22.4, 22.4)), 1.3, [(arc, (255, 255, 255, 255))]), (16, 16))
        busy.append(img)
    save("busy.ani", ani_bytes(busy, (16, 16), 3))
    preview.append(busy[0])

    working = []
    for i in range(frames):
        track, arc = spinner(23.5, 23.5, 5.2, 2.4, i / frames)
        ring = minus(ellipse(16.6, 16.6, 30.4, 30.4), ellipse(20.6, 20.6, 26.4, 26.4))
        img = shrink(render(union(arrow_mask(), ring), 1.5, [(arc, (255, 255, 255, 255))]), (3, 2))
        working.append(img)
    save("working.ani", ani_bytes(working, (3, 2), 3))
    preview.append(working[0])

    # Onizleme: acik ve koyu zemin uzerinde 64 px.
    cell = 80
    sheet = Image.new("RGBA", (cell * len(preview), cell * 2), (255, 255, 255, 255))
    ImageDraw.Draw(sheet).rectangle([0, cell, sheet.width, cell * 2], fill=(32, 32, 32, 255))
    for i, img in enumerate(preview):
        small = scaled(img, 64)
        sheet.alpha_composite(small, (i * cell + 8, 8))
        sheet.alpha_composite(small, (i * cell + 8, cell + 8))
    sheet.save(os.path.join(OUT, "..", "..", "Tools", "cursors_preview.png"))


if __name__ == "__main__":
    main()
