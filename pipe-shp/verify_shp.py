# -*- coding: utf-8 -*-
import shapefile, os
OUT = os.path.join(os.path.dirname(__file__), "output", "pipes")
r = shapefile.Reader(OUT, encoding="utf-8")
print("shapeType:", r.shapeTypeName, "| 레코드 수:", len(r))
print("필드:", [f[0] for f in r.fields if f[0] != "DeletionFlag"])
print("\n--- 첫 2개 레코드 ---")
for i in range(2):
    sr = r.shapeRecord(i)
    print("기하(점들):", sr.shape.points)
    print("속성:", dict(zip([f[0] for f in r.fields if f[0]!="DeletionFlag"], sr.record)))
    print()
# bbox
print("전체 범위 bbox:", r.bbox)
r.close()
print("\n출력 폴더 파일들:")
for fn in sorted(os.listdir(os.path.join(os.path.dirname(__file__),"output"))):
    print("  ", fn)
