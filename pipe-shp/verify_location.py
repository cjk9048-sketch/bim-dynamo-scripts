# -*- coding: utf-8 -*-
import os, shapefile
OUT = os.path.join(os.path.dirname(__file__), "output", "pipes")
r = shapefile.Reader(OUT); bb = r.bbox; r.close()
cx = (bb[0]+bb[2])/2; cy = (bb[1]+bb[3])/2
print("중심 좌표(m, UTM36S):", round(cx,1), round(cy,1))
try:
    from pyproj import Transformer
    t = Transformer.from_crs(32736, 4326, always_xy=True)
    lon, lat = t.transform(cx, cy)
    print("중심 경위도: %.4f E, %.4f" % (lon, lat))
    print("도도마 실제:  35.7395 E, -6.1731")
except Exception as e:
    print("pyproj 없음:", e)
