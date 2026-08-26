# -*- coding: utf-8 -*-
import shapefile, os, math, statistics
OUT = os.path.join(os.path.dirname(__file__), "output", "pipes")
r = shapefile.Reader(OUT, encoding="utf-8")
fields = [f[0] for f in r.fields if f[0] != "DeletionFlag"]
li = fields.index("Length_m")
ratios = []
for sr in r.iterShapeRecords():
    pts = sr.shape.points
    if len(pts) < 2:
        continue
    (x1,y1),(x2,y2) = pts[0], pts[-1]
    geom = math.hypot(x2-x1, y2-y1)   # 좌표단위 길이
    L = sr.record[li]                  # 파일에 적힌 길이(m)
    if L and L > 0 and geom > 0:
        ratios.append(geom / L)
r.close()
ratios.sort()
print("표본 수:", len(ratios))
print("중앙값 비율 (좌표길이/실제m):", round(statistics.median(ratios), 5))
print("평균 비율:", round(statistics.mean(ratios), 5))
print("1피트=0.3048m 이므로 1/0.3048 =", round(1/0.3048,5))
print("하위10%:", round(ratios[len(ratios)//10],4), "상위10%:", round(ratios[len(ratios)*9//10],4))
