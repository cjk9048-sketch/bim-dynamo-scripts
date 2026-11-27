"""DH 관로 — 그림 목록(JSON)을 PNG 로. 화면 · Civil 없이 횡단도 · 종단도 꼴을 눈으로 확인하는 데 쓴다.
쓰기: python -X utf8 render.py <그림 목록.json> <그림.png> [x0,y0,x1,y1] [가로 픽셀]"""
import json, sys
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager
from matplotlib.patches import Polygon, Circle

for f in ("C:/Windows/Fonts/malgun.ttf",):
    try:
        font_manager.fontManager.addfont(f)
        plt.rcParams["font.family"] = font_manager.FontProperties(fname=f).get_name()
    except Exception:
        pass
plt.rcParams["axes.unicode_minus"] = False

ACI = {1: "#e00000", 2: "#b08a00", 3: "#00a000", 4: "#00a8b8", 5: "#0030e0", 6: "#d000d0", 7: "#000000", 8: "#707070", 9: "#b0b0b0"}
LAY = {"지반선": (1, "-"), "터파기": (3, "-"), "가시설": (6, "-"), "지하수위추정선": (5, "--"), "깊이구분선": (8, "--"),
       "지층선": (8, "-."), "관": (4, "-"), "폴대": (1, "-"), "문자": (7, "-"), "토적표": (3, "-"), "해치": (9, "-"),
       "포장": (8, "-"), "도곽": (7, "-"), "제목": (7, "-"), "지시선": (1, "-"), "밴드": (7, "-"), "밴드문자": (2, "-"),
       "지장물": (6, "-"), "부속": (2, "-"), "치수": (1, "-"), "경고": (1, "-")}
HATCH = {"SAND": "....", "GRAVEL": "ooo", "CONC": "xx"}

items = json.load(open(sys.argv[1], encoding="utf-8"))
out = sys.argv[2]
crop = [float(v) for v in sys.argv[3].split(",")] if len(sys.argv) > 3 and sys.argv[3] != "-" else None
px = int(sys.argv[4]) if len(sys.argv) > 4 else 2000

xs, ys = [], []
for it in items:
    for p in it["Pts"]:
        xs.append(p["X"]); ys.append(p["Y"])
x0, x1, y0, y1 = (min(xs), max(xs), min(ys), max(ys)) if not crop else (crop[0], crop[2], crop[1], crop[3])
w, h = max(x1 - x0, 1e-6), max(y1 - y0, 1e-6)
fig = plt.figure(figsize=(px / 200.0, px / 200.0 * h / w))
ax = fig.add_axes([0, 0, 1, 1])
ax.set_xlim(x0, x1); ax.set_ylim(y0, y1); ax.set_axis_off(); ax.set_aspect("equal")
pt_per_unit = (px / 200.0 * 72.0) / w        # 도면 1 단위가 몇 포인트인가(글자 크기)
for it in items:
    aci, ls = LAY.get(it["Layer"], (7, "-"))
    col = ACI.get(aci, "#000000")
    pts = [(p["X"], p["Y"]) for p in it["Pts"]]
    k = it["Kind"]
    if k in (0, 1):
        if it["Closed"]:
            pts = pts + [pts[0]]
        ax.plot([p[0] for p in pts], [p[1] for p in pts], color=col, lw=0.7, ls=ls, solid_capstyle="butt")
    elif k == 2:
        ax.add_patch(Circle(pts[0], it["R"], fill=False, ec=col, lw=0.8))
    elif k == 4:
        if it["Pattern"] == "SOLID":
            ax.add_patch(Polygon(pts, closed=True, fc="#9a9a9a", ec="none"))
        else:
            ax.add_patch(Polygon(pts, closed=True, fc="none", ec="#909090", lw=0.0, hatch=HATCH.get(it["Pattern"], "//")))
        for hole in it.get("Holes", []):
            ax.add_patch(Circle((hole["Item1"], hole["Item2"]), hole["Item3"], fc="white", ec="none"))
    elif k == 3:
        ha = ("left", "center", "right")[it["Align"]]
        ax.text(pts[0][0], pts[0][1], it["Text"], color=col, fontsize=max(1.0, it["H"] * pt_per_unit * 0.95),
                ha=ha, va="baseline" if it["VAlign"] == 0 else "center", rotation=it["Rot"], rotation_mode="anchor")
# 원(관)은 채움 뒤에 다시 그려 가려지지 않게
for it in items:
    if it["Kind"] == 2:
        aci, _ = LAY.get(it["Layer"], (7, "-"))
        ax.add_patch(Circle((it["Pts"][0]["X"], it["Pts"][0]["Y"]), it["R"], fill=False, ec=ACI.get(aci, "#000"), lw=0.9))
fig.savefig(out, dpi=200, facecolor="white")
print("그림:", out, "범위 %.2f,%.2f ~ %.2f,%.2f" % (x0, y0, x1, y1))
