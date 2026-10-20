import numpy as np
from PIL import Image, ImageDraw

src = r"C:\Users\ASUS\.cursor\projects\f-code-unity-TapGJ\assets\c__Users_ASUS_AppData_Roaming_Cursor_User_workspaceStorage_f009d8fead5b45ec2e50e85234c5f33b_images_beijin__2_-6ecc0505-c244-4144-974d-c553c530c1e5.jpg"
im = Image.open(src).convert("RGB")
a = np.asarray(im).astype(np.float32)
lum = a.mean(2)
dx = np.abs(np.diff(lum, axis=1, prepend=lum[:, :1]))
dy = np.abs(np.diff(lum, axis=0, prepend=lum[:1, :]))

seeds = [275, 355, 434, 514, 594, 675, 754]
fits = []
for seed in seeds:
    pts = []
    x = float(seed)
    for yy in range(55, 390, 4):
        sl = dx[yy, :]
        lo, hi = int(x) - 22, int(x) + 22
        seg = sl[lo:hi]
        if seg.size == 0:
            continue
        k = int(np.argmax(seg))
        if seg[k] < 12:
            continue
        nx = lo + k
        x = 0.7 * x + 0.3 * nx
        pts.append((yy, nx, float(seg[k])))
    pts = np.array(pts)
    good = pts[pts[:, 2] > 20]
    b, c = np.polyfit(good[:, 0], good[:, 1], 1)
    fits.append((b, c))
    print(f"V n={len(good)} x={c:.1f}+{b:.4f}*y  at50={c+b*50:.1f} at400={c+b*400:.1f}")

def xs_at(y):
    return np.array([c + b * y for b, c in fits])

for y in (50, 200, 400):
    xs = xs_at(y)
    idx = np.arange(1, 8)
    slope, intercept = np.polyfit(idx, xs, 1)
    print(f"y={y} spacing={slope:.2f} edge0={intercept:.1f} edge8={intercept+8*slope:.1f}")

ys = np.array([50.2, 85.4, 122.2, 161.1, 203.0, 247.4, 295.1, 346.2])
best = None
for v in np.linspace(-4000, -200, 4000):
    t = 1.0 / (ys - v)
    idx = np.arange(8)
    B, A = np.polyfit(idx, t, 1)
    pred = v + 1.0 / (A + B * idx)
    err = np.mean((pred - ys) ** 2)
    if best is None or err < best[0]:
        best = (err, v, A, B)
err, v, A, B = best
print(f"horizon y={v:.1f} rms={err**0.5:.3f}")
for i in range(9):
    print(i, v + 1.0 / (A + B * i))

def edge_x(y, y0, x0, y1, x1):
    return x0 + (x1 - x0) * (y - y0) / (y1 - y0)

def intersect(y_of_x, edge):
    x = 400.0
    for _ in range(8):
        y = y_of_x(x)
        x = edge_x(y, *edge)
    return x, y_of_x(x)

top = lambda x: 53.2 - 0.0057 * x
bx = np.array([160, 200, 240, 280, 320, 360, 400, 440, 480, 640, 760, 840, 880], float)
by = np.array([399, 398, 399, 399, 401, 401, 402, 405, 401, 401, 398, 400, 406], float)
bd, bc = np.polyfit(bx, by, 1)
bottom = lambda x, bd=bd, bc=bc: bc + bd * x
print(f"bottom y={bc:.2f}+{bd:.5f}*x")
left = (50, 200.6, 400, 105.2)
right = (50, 829.3, 400, 927.1)
TL = intersect(top, left)
TR = intersect(top, right)
BL = intersect(bottom, left)
BR = intersect(bottom, right)
print("TL", TL, "TR", TR, "BR", BR, "BL", BL)

corners = [BL, BR, TR, TL]

def cross(a, b):
    return a[0] * b[1] - a[1] * b[0]

A = np.array(BR) - np.array(TR)
B = np.array(TL) - np.array(TR)
bend = np.array(BL) - np.array(BR) + np.array(TR) - np.array(TL)
det = cross(A, B)
persp = np.array([cross(bend, B), cross(A, bend)]) / det
horiz = np.array(BR) * (1 + persp[0]) - np.array(BL)
vert = np.array(TL) * (1 + persp[1]) - np.array(BL)

def map_grid(gx, gy):
    return (np.array(BL) + horiz * gx + vert * gy) / (1 + persp[0] * gx + persp[1] * gy)

overlay = im.copy()
draw = ImageDraw.Draw(overlay)
for i in range(9):
    pts = [tuple(map_grid(i / 8, j / 8)) for j in range(9)]
    draw.line(pts, fill=(220, 40, 40), width=1)
    pts = [tuple(map_grid(j / 8, i / 8)) for j in range(9)]
    draw.line(pts, fill=(220, 40, 40), width=1)
for gx in range(8):
    for gy in range(8):
        c = map_grid((gx + 0.5) / 8, (gy + 0.5) / 8)
        draw.ellipse((c[0] - 2, c[1] - 2, c[0] + 2, c[1] + 2), fill=(0, 180, 255))
out = r"C:\Users\ASUS\.cursor\projects\f-code-unity-TapGJ\assets\board_overlay.png"
overlay.save(out)
for name, box in {
    "z_tl": (150, 20, 280, 120),
    "z_tr": (760, 15, 900, 110),
    "z_bl": (60, 360, 200, 450),
    "z_br": (850, 350, 990, 460),
    "z_mid": (430, 160, 600, 280),
}.items():
    overlay.crop(box).resize(((box[2] - box[0]) * 3, (box[3] - box[1]) * 3), Image.NEAREST).save(
        rf"C:\Users\ASUS\.cursor\projects\f-code-unity-TapGJ\assets\{name}.png")
print("saved", out)

f = np.float32
left_px, top_px, width, height = f(160), f(60), f(1620), f(730)
scale = f(1920) / f(1024)
image_top = (f(1080) - f(575) * scale) * f(0.5)
raw = [BL, BR, TR, TL]
print("scale", float(scale), "imageTop", float(image_top))
norm = []
for px, py in raw:
    px, py = f(px), f(py)
    nx = (px * scale - left_px) / width
    ny = (top_px + height - image_top - py * scale) / height
    norm.append((float(nx), float(ny)))
    print("norm", float(nx), float(ny))

def cross2(a, b):
    return a[0] * b[1] - a[1] * b[0]

n = [np.array(p, np.float64) for p in norm]
a = n[1] - n[2]
b = n[3] - n[2]
bend = n[0] - n[1] + n[2] - n[3]
det = cross2(a, b)
persp = np.array([cross2(bend, b), cross2(a, bend)]) / det
horiz = n[1] * (1 + persp[0]) - n[0]
vert = n[3] * (1 + persp[1]) - n[0]

def mapn(gx, gy):
    return (n[0] + horiz * gx + vert * gy) / (1 + persp[0] * gx + persp[1] * gy)

cells = [(0, 0), (7, 0), (0, 7), (7, 7), (3, 3), (4, 4), (0, 3), (7, 3)]
for x, y in cells:
    gx = (x * 1.1 + 0.55) / 8.8
    gy = (y * 1.1 + 0.55) / 8.8
    m = mapn(gx, gy)
    cx = left_px + m[0] * width
    cy = top_px + height - m[1] * height
    print(f"cell {x},{y} comp {float(cx):.2f},{float(cy):.2f}")

print("--- bottom edge samples ---")
for x in range(160, 900, 40):
    sl = dy[360:440, x]
    yhit = None
    for i, val in enumerate(sl):
        if val > 28 and lum[360 + i - 1, x] > 160:
            yhit = 360 + i
            break
    print(x, yhit, "lum", round(float(lum[min(lum.shape[0] - 1, (yhit or 360)), x]), 1))

expected = [50, 85, 122, 161, 202, 247, 295, 346, 405]
for e in expected:
    pts = []
    for x in range(220, 820, 8):
        sl = dy[:, x]
        lo, hi = max(0, e - 18), min(lum.shape[0] - 1, e + 18)
        seg = sl[lo:hi]
        k = int(np.argmax(seg))
        if seg[k] < 18:
            continue
        pts.append((x, lo + k, float(seg[k])))
    pts = np.array(pts)
    if len(pts) < 5:
        print("H weak", e, len(pts))
        continue
    d, c0 = np.polyfit(pts[:, 0], pts[:, 1], 1)
    print(f"H~{e} n={len(pts)} y={c0:.1f}+{d:.4f}*x mid={c0+d*512:.1f} L={c0+d*160:.1f} R={c0+d*880:.1f}")
