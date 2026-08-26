# -*- coding: utf-8 -*-
import shapefile, os, math, statistics
OUTDIR = os.path.join(os.path.dirname(__file__), "output")
OUT = os.path.join(OUTDIR, "pipes")
r = shapefile.Reader(OUT, encoding="utf-8")
fields = [f[0] for f in r.fields if f[0] != "DeletionFlag"]
li = fields.index("Length_m")
ratios = []
for sr in r.iterShapeRecords():
    pts = sr.shape.points
    (x1,y1),(x2,y2) = pts[0], pts[-1]
    geom = math.hypot(x2-x1, y2-y1)
    L = sr.record[li]
    if L and L>0: ratios.append(geom/L)
print("선 수:", len(r))
print("기하길이/파일길이(m) 중앙값:", round(statistics.median(ratios),4), "(1.0이면 미터 일치)")
print("bbox(미터):", [round(v,1) for v in r.bbox])
# 위경도 환산(대략): UTM37S 역산 없이 northing->위도 추정
ymid = (r.bbox[1]+r.bbox[3])/2
lat = -(10000000 - ymid)/111320
print("중심 위도 추정:", round(lat,2), "도 (탄자니아 중부면 약 -6)")
r.close()
print("\n.prj 내용:")
with open(OUT+".prj", encoding="utf-8") as f: print("  ", f.read()[:80], "...")
print("\n출력 폴더 파일:")
for fn in sorted(os.listdir(OUTDIR)):
    sz = os.path.getsize(os.path.join(OUTDIR,fn))
    print(f"   {fn}  ({sz:,} bytes)")
