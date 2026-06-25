#!/usr/bin/env python
"""Regenerate Elektron mascot assets from the source frame, in ONE coherent
coordinate system so the eyelids always line up with the figure.

Output (consumed by build_lottie.py):
  assets/elektron-figure.png   transparent full-body cutout
  assets/eyelid-l.png, -r.png  blink lids
  ../../mascot-frames/_work/rig.json   {W,H, eyes, skin, lids}
"""
import os, json
import cv2, numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
A = os.path.join(HERE, "assets")
WORK = os.path.join(ROOT, "mascot-frames", "_work")
SRC = os.path.join(ROOT, "mascot-frames", "elektron_full.png")
os.makedirs(A, exist_ok=True); os.makedirs(WORK, exist_ok=True)

FACE_CUT = 0.58   # keep white (eyes/teeth) only above this fraction of height
UPSCALE = 2

# --- 1. background removal: fixed-range flood from all borders --------------
img0 = cv2.imread(SRC, cv2.IMREAD_COLOR)
h, w = img0.shape[:2]
mask = np.zeros((h + 2, w + 2), np.uint8)
flags = 4 | (255 << 8) | cv2.FLOODFILL_MASK_ONLY | cv2.FLOODFILL_FIXED_RANGE
tol = (30, 30, 30)
step = max(2, w // 80)
seeds = []
for x in range(0, w, step): seeds += [(x, 0), (x, h - 1)]
for y in range(0, h, step): seeds += [(0, y), (w - 1, y)]
for (x, y) in seeds:
    if mask[y + 1, x + 1] == 0:
        cv2.floodFill(img0.copy(), mask, (x, y), 0, tol, tol, flags)
bg = mask[1:-1, 1:-1].astype(bool)
alpha = np.where(bg, 0, 255).astype(np.uint8)

# largest fg component = figure (drops stray UI bits)
fg = cv2.morphologyEx((alpha > 0).astype(np.uint8), cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8))
num, lab, stats, _ = cv2.connectedComponentsWithStats(fg, 8)
big = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
alpha = ((lab == big).astype(np.uint8)) * 255

# upscale for crispness
img = cv2.resize(img0, (w * UPSCALE, h * UPSCALE), interpolation=cv2.INTER_CUBIC)
alpha = cv2.resize(alpha, (w * UPSCALE, h * UPSCALE), interpolation=cv2.INTER_NEAREST)
H, W = img.shape[:2]
rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)

# --- 2. remove enclosed white background pockets below the face -------------
# (gaps between legs / under arms that the border flood can't reach).
mx = rgb.max(2).astype(int); mn = rgb.min(2).astype(int)
white = ((alpha > 30) & (mn > 225) & ((mx - mn) < 22)).astype(np.uint8)
# face region is defined relative to current figure bbox
ys0, xs0 = np.where(alpha > 10)
ytop, ybot = ys0.min(), ys0.max()
face_y = ytop + (ybot - ytop) * FACE_CUT
n, wl, wstats, wcent = cv2.connectedComponentsWithStats(white, 8)
for i in range(1, n):
    if wcent[i][1] >= face_y:        # below the face -> background, drop it
        alpha[wl == i] = 0

# --- 3. choke matte 1px to kill white fringe, soft AA -----------------------
alpha = cv2.erode(alpha, np.ones((3, 3), np.uint8), iterations=1)
alpha = cv2.GaussianBlur(alpha, (3, 3), 0)

# --- 4. crop to content (this is now the canonical coordinate system) -------
ys, xs = np.where(alpha > 10); pad = 8
y0 = max(0, ys.min() - pad); x0 = max(0, xs.min() - pad)
y1 = min(H, ys.max() + pad); x1 = min(W, xs.max() + pad)
rgb = rgb[y0:y1, x0:x1]; alpha = alpha[y0:y1, x0:x1]
H, W = alpha.shape
Image.fromarray(np.dstack([rgb, alpha]), "RGBA").save(os.path.join(A, "elektron-figure.png"))
print("figure", (W, H))

# --- 5. detect eyes (green irises) in final coordinates ---------------------
Ri, Gi, Bi = rgb[:, :, 0].astype(int), rgb[:, :, 1].astype(int), rgb[:, :, 2].astype(int)
yy, _ = np.mgrid[0:H, 0:W]
green = (Gi > 90) & (Gi > Ri + 25) & (Gi > Bi + 25) & (alpha > 200) & (yy < H * 0.5)
gy, gx = np.where(green)
order = np.argsort(gx); gx, gy = gx[order], gy[order]
cut = int(np.argmax(np.diff(gx)))
eyes = []
for sx, sy in [(gx[:cut + 1], gy[:cut + 1]), (gx[cut + 1:], gy[cut + 1:])]:
    eyes.append([int(sx.min()), int(sy.min()), int(sx.max()), int(sy.max())])
eyes.sort(key=lambda e: e[0])
# skin tone sampled just below each eye
skin = np.mean([rgb[min(H - 1, e[3] + (e[3] - e[1])) - 4: e[3] + (e[3] - e[1]) + 4,
                    (e[0] + e[2]) // 2 - 4:(e[0] + e[2]) // 2 + 4].reshape(-1, 3).mean(0)
                for e in eyes], 0)
skin = tuple(int(v) for v in skin.round())
print("eyes", eyes, "skin", skin)

# --- 6. generate blink lids -------------------------------------------------
lash = (120, 74, 48); shade = (232, 188, 162)
lids = []
for name, box in zip(["l", "r"], eyes):
    x0e, y0e, x1e, y1e = box; bw0, bh0 = x1e - x0e, y1e - y0e
    ex, et, eb = int(bw0 * 0.26), int(bh0 * 0.30), int(bh0 * 0.14)
    bx0, by0 = x0e - ex, y0e - et; bx1, by1 = x1e + ex, y1e + eb
    bw, bh = bx1 - bx0, by1 - by0
    lid = Image.new("RGBA", (bw, bh), (0, 0, 0, 0)); d = ImageDraw.Draw(lid)
    d.ellipse([2, 2, bw - 2, bh - 2], fill=skin + (255,))
    d.arc([2, -int(bh * 0.25), bw - 2, int(bh * 0.9)], 200, 340, fill=shade + (255,), width=max(2, bh // 14))
    ly = int(bh * 0.52)
    d.arc([int(bw * 0.06), ly - int(bh * 0.30), int(bw * 0.94), ly + int(bh * 0.34)],
          18, 162, fill=lash + (255,), width=max(3, bh // 11))
    lid = lid.filter(ImageFilter.GaussianBlur(0.6))
    fn = "web/widget/assets/eyelid-%s.png" % name
    lid.save(os.path.join(ROOT, fn))
    lids.append({"name": name, "file": fn, "x": bx0, "y": by0, "w": lid.size[0], "h": lid.size[1]})

# --- 7. detect mouth + build a "closed mouth" overlay for lip-sync ----------
eye_bottom = max(e[3] for e in eyes)
cxm = (min(e[0] for e in eyes) + max(e[2] for e in eyes)) // 2
yy2, xx2 = np.mgrid[0:H, 0:W]
bright = rgb.max(2)
band = (alpha > 200) & (yy2 > eye_bottom + 55) & (yy2 < eye_bottom + 210) & (np.abs(xx2 - cxm) < W * 0.16)
dark = band & (bright < 120)
teeth = band & (Ri > 222) & (Gi > 212) & (Bi > 205)
lips = band & (Ri > 120) & (Ri > Gi + 30) & (Ri > Bi + 15) & (Gi < 160)
mm = cv2.morphologyEx((dark | teeth | lips).astype(np.uint8), cv2.MORPH_CLOSE, np.ones((13, 13), np.uint8))
nm, ml, mst, _ = cv2.connectedComponentsWithStats(mm, 8)
mi = 1 + int(np.argmax(mst[1:, 4]))
mxb, myb, mwb, mhb = [int(v) for v in mst[mi][:4]]

ex, et, eb = int(mwb * 0.10), int(mhb * 0.55), int(mhb * 0.45)
bx0, by0 = mxb - ex, myb - et
bx1, by1 = mxb + mwb + ex, myb + mhb + eb
bw, bh = bx1 - bx0, by1 - by0
# local skin around the mouth (ring), so the patch blends
ring = np.zeros((H, W), bool)
ring[max(0, by0 - 8):by1 + 8, max(0, bx0 - 8):bx1 + 8] = True
ring[by0:by1, bx0:bx1] = False
ringpx = rgb[ring & (alpha > 200) & (mn := rgb.min(2)) > 150]
mskin = tuple(int(v) for v in (np.median(ringpx, 0) if len(ringpx) else skin))
lipc = (176, 96, 86)
# feathered skin patch: solid in the centre (hides teeth), edges fade into skin
skin_layer = Image.new("RGBA", (bw, bh), mskin + (255,))
amask = Image.new("L", (bw, bh), 0)
ImageDraw.Draw(amask).ellipse([int(bw * 0.06), int(bh * 0.10),
                               int(bw * 0.94), int(bh * 0.90)], fill=255)
amask = amask.filter(ImageFilter.GaussianBlur(max(2.5, bh * 0.16)))
skin_layer.putalpha(amask)
# relaxed closed-lip smile line, on its own feathered layer
lip = Image.new("RGBA", (bw, bh), (0, 0, 0, 0))
ly = int(bh * 0.54)
ImageDraw.Draw(lip).arc([int(bw * 0.16), ly - int(bh * 0.30), int(bw * 0.84), ly + int(bh * 0.30)],
                        14, 166, fill=lipc + (255,), width=max(3, bh // 11))
lip = lip.filter(ImageFilter.GaussianBlur(0.8))
mouth = Image.alpha_composite(skin_layer, lip)
mouth.save(os.path.join(A, "mouth-closed.png"))
mouth_lid = {"file": "web/widget/assets/mouth-closed.png", "x": bx0, "y": by0, "w": bw, "h": bh}
print("mouth bbox", [mxb, myb, mwb, mhb], "overlay", (bw, bh), "skin", mskin)

# --- 8. head crop (hair-top -> neck) for the launcher avatar ----------------
roww = (alpha > 10).sum(1)
top = int(np.argmax(roww > 5))                      # first row with real content (hair top)
# chibi head: no narrow neck -> chin is just below the mouth
mouth_bottom = myb + mhb
chin = min(H, mouth_bottom + int(mhb * 0.7))
# head width from rows ABOVE the mouth only (hair/face/ears, no shoulders)
cols = np.where((alpha[top:myb] > 10).any(0))[0]
hx0, hx1 = int(cols.min()), int(cols.max())
padx = int((hx1 - hx0) * 0.06); pady = int((chin - top) * 0.05)
hx0 = max(0, hx0 - padx); hx1 = min(W, hx1 + padx)
hy0 = max(0, top - pady); hy1 = min(H, chin + pady)
head = Image.open(os.path.join(A, "elektron-figure.png")).convert("RGBA").crop((hx0, hy0, hx1, hy1))
head.save(os.path.join(A, "elektron-head.png"))
print("head crop", head.size, "bbox", [hx0, hy0, hx1, hy1], "chin@", chin)

json.dump({"W": W, "H": H, "eyes": eyes, "skin": list(skin), "lids": lids,
           "mouth_lid": mouth_lid, "head": [hx0, hy0, hx1, hy1]},
          open(os.path.join(WORK, "rig.json"), "w"), indent=1)
print("wrote rig.json")
