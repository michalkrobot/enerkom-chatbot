#!/usr/bin/env python
"""Build a self-contained cut-out Lottie for the Elektron mascot.

Layers (top->bottom): eyelid_l, eyelid_r, figure, rig(null).
- rig (null): drives body motion (sway/tilt/nod/shake + bob), pivots at upper torso.
- figure: breathing (scaleY) about torso anchor.
- eyelids: blink (scaleY 0->100->0), parented to rig so they track head motion.

States are frame segments on one 30fps timeline; the widget plays the matching
segment with lottie-web playSegments(). Thinking "..." dots are rendered by the
widget in CSS, not here.
"""
import json, base64, os

FR = 30
# segment windows [in, out] in frames
SEG = {
    "idle":     (0,   90),
    "thinking": (90,  195),
    "talking":  (195, 285),
    "notfound": (285, 360),
}
OP = 360

HERE = os.path.dirname(os.path.abspath(__file__))
A = os.path.join(HERE, "assets")
ROOT = os.path.join(HERE, "..", "..")
RIG = json.load(open(os.path.join(ROOT, "mascot-frames", "_work", "rig.json")))

SCALE = 0.55  # downscale geometry + raster for web (retina at ~200px display)

def sc(v):
    return v * SCALE

W, H = sc(RIG["W"]), sc(RIG["H"])

from PIL import Image

def web_asset(src_rel, dst_name):
    """Downscale a source PNG by SCALE, write to assets/, return (path, w, h)."""
    src = os.path.join(ROOT, src_rel)
    im = Image.open(src).convert("RGBA")
    w, h = int(round(im.width * SCALE)), int(round(im.height * SCALE))
    im = im.resize((w, h), Image.LANCZOS)
    dst = os.path.join(A, dst_name)
    im.save(dst, optimize=True)
    return dst, w, h

def b64(path):
    with open(path, "rb") as f:
        return "data:image/png;base64," + base64.b64encode(f.read()).decode()

# ---- keyframe helpers -------------------------------------------------------
def ease(out_x=0.6, out_y=0.0, in_x=0.4, in_y=1.0):
    return {"o": {"x": [out_x], "y": [out_y]}, "i": {"x": [in_x], "y": [in_y]}}

def kf(frames):
    """frames: list of (t, value_list). Smooth in/out between them."""
    out = []
    for idx, (t, v) in enumerate(frames):
        node = {"t": t, "s": v}
        if idx < len(frames) - 1:
            node.update(ease())
        out.append(node)
    return {"a": 1, "k": out}

def static(v):
    return {"a": 0, "k": v}

def kf_hold(frames):
    """Stepped keyframes (snap, no interpolation) — for lip-sync flapping."""
    return {"a": 1, "k": [{"t": t, "s": v, "h": 1} for t, v in frames]}

# ---- composition padding (headroom for the thinking thought-bubble) --------
PAD_T = round(H * 0.28)
PAD_R = round(W * 0.30)
OX, OY = 0.0, float(PAD_T)
COMP_W, COMP_H = W + PAD_R, H + PAD_T

# ---- transforms -------------------------------------------------------------
PIVOT = [OX + W / 2, OY + H * 0.52]      # upper-torso pivot for head motion
FIG_ANCHOR = [OX + W / 2, OY + H * 0.50] # breathing anchor (torso)

# rig null: rotation (sway/tilt/nod/shake) ----------------------------------
rig_rot = kf([
    # idle micro-sway
    (0, [0]), (22, [0.7]), (45, [0]), (68, [-0.7]), (90, [0]),
    # thinking: tilt head and hold
    (110, [-5]), (180, [-5]), (195, [0]),
    # talking: nodding oscillation
    (205, [2.2]), (215, [-1.8]), (225, [2.2]), (235, [-1.8]),
    (245, [2.2]), (255, [-1.8]), (265, [2.0]), (275, [-1.0]), (285, [0]),
    # notfound: head shake "no"
    (296, [-6]), (307, [6]), (318, [-5]), (329, [4]), (340, [0]), (360, [0]),
])
# rig null: position (bob), x=pivotX constant, y bob during talk + slight think
PX, PY = PIVOT[0], PIVOT[1]
rig_pos = kf([
    (0, [PX, PY, 0]), (90, [PX, PY, 0]),
    (110, [PX, PY + 6, 0]), (180, [PX, PY + 6, 0]), (195, [PX, PY, 0]),
    # talk bob synced with nod
    (205, [PX, PY + 5, 0]), (225, [PX, PY + 5, 0]), (245, [PX, PY + 5, 0]),
    (265, [PX, PY + 4, 0]), (285, [PX, PY, 0]), (360, [PX, PY, 0]),
])

# figure breathing scale (idle only; constant elsewhere) --------------------
fig_scale = kf([
    (0, [100, 100, 100]), (45, [100.4, 101.3, 100]), (90, [100, 100, 100]),
])

# eyelid blink scaleY (default 0 = open). Blinks in idle + thinking.
def blink(t):
    return [(t, [100, 0, 100]), (t + 3, [100, 100, 100]), (t + 6, [100, 0, 100])]

lid_scale_frames = [(0, [100, 0, 100])]
for t in (50, 150, 330):               # idle, thinking, notfound blinks
    lid_scale_frames += blink(t)
lid_scale_frames += [(360, [100, 0, 100])]
lid_scale_frames = sorted(lid_scale_frames, key=lambda x: x[0])
lid_scale = kf(lid_scale_frames)

# ---- assemble layers --------------------------------------------------------
fig_path, fig_w, fig_h = web_asset("web/widget/assets/elektron-figure.png",
                                   "elektron-figure-web.png")
assets = [{
    "id": "fig", "w": fig_w, "h": fig_h, "u": "", "e": 1, "p": b64(fig_path),
}]

lid_layers = []
for i, lid in enumerate(RIG["lids"]):
    aid = "lid_" + lid["name"]
    lpath, lw, lh = web_asset(lid["file"], "eyelid-%s-web.png" % lid["name"])
    assets.append({"id": aid, "w": lw, "h": lh, "u": "", "e": 1, "p": b64(lpath)})
    cx = OX + sc(lid["x"]) + lw / 2
    top = OY + sc(lid["y"])
    lid_layers.append({
        "ddd": 0, "ind": 10 + i, "ty": 2, "nm": aid, "refId": aid,
        "parent": 1,  # rig null
        "ks": {
            "o": static(100), "r": static(0),
            "p": static([cx, top, 0]), "a": static([lw / 2, 0, 0]),
            "s": lid_scale,
        },
        "ao": 0, "ip": 0, "op": OP, "st": 0, "bm": 0,
    })

# mouth overlay: closed-mouth covers the open smile; snaps on/off during talk
ml = RIG["mouth_lid"]
mlp = web_asset(ml["file"], "mouth-closed-web.png")
assets.append({"id": "mouth", "w": mlp[1], "h": mlp[2], "u": "", "e": 1, "p": b64(mlp[0])})
TALK_S, TALK_E = SEG["talking"]
mouth_op = [(0, [0]), (TALK_S, [0])]
t = TALK_S + 7
flap = 0
while t < TALK_E - 4:
    flap ^= 1
    mouth_op.append((t, [100 if flap else 0]))
    t += 7
mouth_op.append((TALK_E, [0]))
mouth_layer = {
    "ddd": 0, "ind": 5, "ty": 2, "nm": "mouth", "refId": "mouth", "parent": 1,
    "ks": {
        "o": kf_hold(mouth_op), "r": static(0),
        "p": static([OX + sc(ml["x"]) + mlp[1] / 2, OY + sc(ml["y"]) + mlp[2] / 2, 0]),
        "a": static([mlp[1] / 2, mlp[2] / 2, 0]), "s": static([100, 100, 100]),
    },
    "ao": 0, "ip": 0, "op": OP, "st": 0, "bm": 0,
}

# thinking thought-bubble (vector shapes) — visible only during [90,195] ------
THINK_S, THINK_E = SEG["thinking"]
TEAL = [0.114, 0.62, 0.459]; WHITE = [1, 1, 1]

def _el(cx, cy, d):
    return {"ty": "el", "d": 1, "p": {"a": 0, "k": [cx, cy]}, "s": {"a": 0, "k": [d, d]}}
def _fill(rgb, o=None):
    return {"ty": "fl", "c": {"a": 0, "k": rgb}, "o": o or {"a": 0, "k": 100}, "r": 1}
def _stroke(rgb, w):
    return {"ty": "st", "c": {"a": 0, "k": rgb}, "o": {"a": 0, "k": 100}, "w": {"a": 0, "k": w}, "lc": 2, "lj": 2}
def _tr():
    return {"ty": "tr", "p": {"a": 0, "k": [0, 0]}, "a": {"a": 0, "k": [0, 0]},
            "s": {"a": 0, "k": [100, 100]}, "r": {"a": 0, "k": 0}, "o": {"a": 0, "k": 100}}
def _grp(items):
    return {"ty": "gr", "it": items + [_tr()]}

MAIN_D = W * 0.28
mcx, mcy = OX + W * 0.72, OY * 0.36
# three pulsing dots inside the main bubble (left-to-right "…")
dots = []
for d in range(3):
    dx = mcx + (d - 1) * MAIN_D * 0.28
    pulse = [(THINK_S, [60])]
    for c in range(THINK_S, THINK_E, 24):
        base = c + d * 6
        pulse += [(base, [60]), (base + 4, [100]), (base + 9, [60])]
    pulse += [(THINK_E, [60])]
    pulse = sorted({t: v for t, v in pulse}.items())
    dots.append(_grp([_el(dx, mcy, MAIN_D * 0.26), _fill(TEAL, kf([(t, v) for t, v in pulse]))]))
# Lottie paints shapes[0] on top -> dots first, then bubble, then tail (behind)
shapes = dots + [
    _grp([_el(mcx, mcy, MAIN_D), _fill(WHITE), _stroke(TEAL, 4)]),
    _grp([_el(OX + W * 0.590, OY * 0.62, MAIN_D * 0.40), _fill(WHITE), _stroke(TEAL, 3)]),
    _grp([_el(OX + W * 0.520, OY * 0.80, MAIN_D * 0.22), _fill(WHITE), _stroke(TEAL, 3)]),
]

bubble_layer = {
    "ddd": 0, "ind": 6, "ty": 4, "nm": "think-bubble",
    "ks": {
        "o": kf([(0, [0]), (THINK_S, [0]), (THINK_S + 6, [100]),
                 (THINK_E - 6, [100]), (THINK_E, [0]), (OP, [0])]),
        "r": static(0), "p": static([0, 0, 0]), "a": static([0, 0, 0]),
        "s": static([100, 100, 100]),
    },
    "shapes": shapes, "ao": 0, "ip": 0, "op": OP, "st": 0, "bm": 0,
}

figure_layer = {
    "ddd": 0, "ind": 2, "ty": 2, "nm": "figure", "refId": "fig",
    "parent": 1,
    "ks": {
        "o": static(100), "r": static(0),
        "p": static([FIG_ANCHOR[0], FIG_ANCHOR[1], 0]),
        "a": static([FIG_ANCHOR[0], FIG_ANCHOR[1], 0]),
        "s": fig_scale,
    },
    "ao": 0, "ip": 0, "op": OP, "st": 0, "bm": 0,
}

rig_null = {
    "ddd": 0, "ind": 1, "ty": 3, "nm": "rig",
    "ks": {
        "o": static(0), "r": rig_rot, "p": rig_pos,
        "a": static([PX, PY, 0]), "s": static([100, 100, 100]),
    },
    "ao": 0, "ip": 0, "op": OP, "st": 0, "bm": 0,
}

doc = {
    "v": "5.7.4", "fr": FR, "ip": 0, "op": OP, "w": COMP_W, "h": COMP_H,
    "nm": "elektron", "ddd": 0, "assets": assets,
    "layers": [bubble_layer] + lid_layers + [mouth_layer, figure_layer, rig_null],
    "markers": [
        {"tm": s, "cm": name, "dr": e - s} for name, (s, e) in SEG.items()
    ],
    "meta": {"segments": SEG},
}

out = os.path.join(A, "elektron.json")
json.dump(doc, open(out, "w"), separators=(",", ":"))
print("wrote", out, "%.0f KB" % (os.path.getsize(out) / 1024))
print("segments:", SEG)
