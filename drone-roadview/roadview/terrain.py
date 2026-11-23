"""수치지도 땅 높이 (DEM).

두 가지 원천:
  1) 사내 GIS DB (PostGIS public.contour — Civil 3D «서버 지표면»과 같은 자료). 접속 정보는 DH.Grading 설치 폴더의
     dh.keys.json «gisdb» (저장소에 없음). 범위로 잘라 받는다.
  2) 국토지리정보원 수치지도 DXF — 등고선 F00171{1,2}{0-4} · 표고점 F0027217 만 (그레이딩 NgiiDxf 규칙:
     713x 글자 빼기 · 표고 0 버림 · (0,0,z) 더미점 · BLOCKS 안 읽음).
점들로 삼각망(TIN)을 만들어 아무 점의 땅 높이를 선형 보간한다.
"""
from __future__ import annotations

import json
import math
import os
import re

import numpy as np

NGII_LAYERS = re.compile(r'^F00171[12][0-4]$|^F0027217$')


# ---------------------------------------------------------------- 사내 DB

def _keys():
    p = os.path.join(os.environ.get('APPDATA', ''), 'Autodesk', 'ApplicationPlugins', 'DH.Grading.bundle',
                     'Contents', 'dh.keys.json')
    if not os.path.exists(p):
        return None
    try:
        return json.load(open(p, encoding='utf-8-sig')).get('gisdb')
    except Exception:
        return None


def db_available():
    g = _keys()
    return bool(g and g.get('host') and g.get('user'))


def contours_from_db(bbox, crs_epsg=5186, simplify=0.5, timeout=20):
    """bbox(x0,y0,x1,y1 · 프로젝트 좌표계) 안 등고선 → [(N,3) 배열]. 접속 · 비밀번호는 어디에도 안 찍는다."""
    import pg8000.native
    g = _keys()
    if not g:
        raise RuntimeError('사내 GIS DB 접속 정보 없음 (DH.Grading 설치 · dh.keys.json)')
    try:
        con = pg8000.native.Connection(user=g['user'], password=g.get('password', ''), host=g['host'],
                                       port=int(g.get('port', 5432)), database=g.get('database', 'postgres'),
                                       timeout=timeout, ssl_context=None)
    except Exception as ex:
        raise RuntimeError('사내 GIS DB 에 못 붙음 (회사 망 · VPN 확인) — %s' % type(ex).__name__) from None
    try:
        srid_row = con.run('SELECT ST_SRID(geom) FROM public.contour LIMIT 1')
        dbsrid = int(srid_row[0][0])
        # pg8000 은 값을 글자로 넘길 수 있다 → 좌표계 번호가 proj 글자로 읽힘 (1123 실측) → 형을 못 박는다
        sql = """
WITH p AS (SELECT CAST(:x0 AS float8) x0, CAST(:y0 AS float8) y0, CAST(:x1 AS float8) x1, CAST(:y1 AS float8) y1,
                  CAST(:srid AS integer) srid, CAST(:dbsrid AS integer) dbsrid, CAST(:simp AS float8) simp),
     b AS (SELECT ST_MakeEnvelope(p.x0, p.y0, p.x1, p.y1, p.srid) AS gd,
                  ST_Transform(ST_MakeEnvelope(p.x0, p.y0, p.x1, p.y1, p.srid), p.dbsrid) AS g6, p.srid, p.simp FROM p)
SELECT c.cont, ST_AsText(ST_SimplifyPreserveTopology(
         ST_CollectionExtract(ST_Intersection(ST_Transform(c.geom, b.srid), b.gd), 2), b.simp)) AS wkt
FROM public.contour c, b
WHERE c.geom && b.g6 AND ST_Intersects(c.geom, b.g6)"""
        rows = con.run(sql, x0=bbox[0], y0=bbox[1], x1=bbox[2], y1=bbox[3], srid=crs_epsg, dbsrid=dbsrid,
                       simp=simplify)
    finally:
        con.close()
    lines = []
    for cont, wkt in rows:
        if cont is None or wkt is None:
            continue
        z = float(cont)
        if z == 0.0:
            continue
        for part in re.findall(r'\(([^()]+)\)', wkt):
            xy = [tuple(map(float, q.split()[:2])) for q in part.split(',') if q.strip()]
            if len(xy) >= 2:
                a = np.array([[x, y, z] for x, y in xy])
                lines.append(a)
    return lines


# ---------------------------------------------------------------- 수치지도 DXF

def points_from_ngii_dxf(paths, bbox=None):
    """수치지도 DXF 여러 장 → 높이 점 (N,3). bbox 로 거름(있으면)."""
    import ezdxf
    pts = []
    for path in paths:
        doc = ezdxf.readfile(path)
        for e in doc.modelspace():
            lay = e.dxf.layer.upper()
            if not NGII_LAYERS.match(lay):
                continue
            t = e.dxftype()
            cand = []
            if t == 'POLYLINE':
                cand = [tuple(v.dxf.location) for v in e.vertices]
            elif t == 'LWPOLYLINE':
                z = float(e.dxf.elevation) if e.dxf.hasattr('elevation') else 0.0
                cand = [(p[0], p[1], z) for p in e.get_points('xy')]
            elif t == 'POINT':
                cand = [tuple(e.dxf.location)]
            elif t == 'INSERT':
                cand = [tuple(e.dxf.insert)]
            elif t == 'LINE':
                cand = [tuple(e.dxf.start), tuple(e.dxf.end)]
            for x, y, z in cand:
                if z == 0.0 or (abs(x) < 1e-6 and abs(y) < 1e-6):
                    continue
                pts.append((x, y, z))
    a = np.array(pts, float).reshape(-1, 3)
    if bbox is not None and len(a):
        m = (a[:, 0] >= bbox[0]) & (a[:, 0] <= bbox[2]) & (a[:, 1] >= bbox[1]) & (a[:, 1] <= bbox[3])
        a = a[m]
    return a


# ---------------------------------------------------------------- 삼각망

class Dem:
    """높이 점 → 삼각망 선형 보간. 밖은 NaN."""

    def __init__(self, pts, densify=None):
        import matplotlib.tri as mtri
        pts = np.asarray(pts, float)
        # 같은 자리 점 합치기 (삼각망 깨짐 막기) — 1 cm 격자
        key = np.round(pts[:, :2] * 100).astype(np.int64)
        _, idx = np.unique(key, axis=0, return_index=True)
        pts = pts[np.sort(idx)]
        if len(pts) < 3:
            raise ValueError('높이 점이 모자람 (%d)' % len(pts))
        self.x0, self.y0 = pts[:, 0].mean(), pts[:, 1].mean()     # 큰 좌표 → 정밀도 위해 옮김
        self.tri = mtri.Triangulation(pts[:, 0] - self.x0, pts[:, 1] - self.y0)
        self.interp = mtri.LinearTriInterpolator(self.tri, pts[:, 2])
        self.n = len(pts)
        self.bbox = (pts[:, 0].min(), pts[:, 1].min(), pts[:, 0].max(), pts[:, 1].max())

    def __call__(self, x, y):
        v = self.interp(np.asarray(x, float) - self.x0, np.asarray(y, float) - self.y0)
        return np.ma.filled(v.astype(float), np.nan)


def lines_to_points(lines, step=2.0):
    """등고선 선 → 점 (step m 간격으로 촘촘히 — 삼각형이 등고선을 가로지르지 않게)."""
    out = []
    for a in lines:
        out.append(a[:1])
        for p, q in zip(a[:-1], a[1:]):
            L = float(np.hypot(*(q[:2] - p[:2])))
            n = max(1, int(L // step))
            t = (np.arange(1, n + 1) / n)[:, None]
            out.append(p + (q - p) * t)
    return np.concatenate(out) if out else np.zeros((0, 3))


def dem_tiles(dem, shot_en, origin, cell=4.0, tile=128, radius=200.0):
    """보기 화면용 높이 타일 → dict (RV_DEM). 촬영점 radius(m) 안쪽 칸만 값, 나머지 NaN.

    타일 (ix,iy) 는 (tile+1)² 값: 행 r = 남→북, 열 c = 서→동, 한 줄씩 겹침 (이웃 타일 안 봐도 겹선형).
    좌표는 원점(origin) 기준 상대값.
    """
    import base64
    S = np.asarray(shot_en, float)
    if len(S) == 0:
        return None
    E0, N0 = origin
    x0 = math.floor((S[:, 0].min() - radius - E0) / cell) * cell
    y0 = math.floor((S[:, 1].min() - radius - N0) / cell) * cell
    nx = int(math.ceil((S[:, 0].max() + radius - E0 - x0) / cell)) + 1
    ny = int(math.ceil((S[:, 1].max() + radius - N0 - y0) / cell)) + 1
    keep = np.zeros((ny, nx), bool)
    rc = int(math.ceil(radius / cell))
    yy, xx = np.mgrid[-rc:rc + 1, -rc:rc + 1]
    disk = (xx * xx + yy * yy) * cell * cell <= radius * radius
    for e, n in S:
        cx = int(round((e - E0 - x0) / cell)); cy = int(round((n - N0 - y0) / cell))
        a0, a1 = max(0, cy - rc), min(ny, cy + rc + 1)
        b0, b1 = max(0, cx - rc), min(nx, cx + rc + 1)
        keep[a0:a1, b0:b1] |= disk[a0 - (cy - rc):a1 - (cy - rc), b0 - (cx - rc):b1 - (cx - rc)]
    tiles = {}
    for iy in range(0, (ny - 1) // tile + 1):
        for ix in range(0, (nx - 1) // tile + 1):
            r0, c0 = iy * tile, ix * tile
            sub = keep[r0:r0 + tile + 1, c0:c0 + tile + 1]
            if not sub.any():
                continue
            rr = np.arange(tile + 1); cc = np.arange(tile + 1)
            gx = E0 + x0 + (c0 + cc)[None, :] * cell + 0 * rr[:, None]
            gy = N0 + y0 + (r0 + rr)[:, None] * cell + 0 * cc[None, :]
            z = dem(gx, gy).astype(np.float32)
            k = np.zeros((tile + 1, tile + 1), bool)
            k[:sub.shape[0], :sub.shape[1]] = sub
            # 값 있는 칸 둘레 한 칸까지는 살림 (겹선형이 가장자리에서 NaN 에 안 걸리게)
            k2 = k.copy()
            k2[1:, :] |= k[:-1, :]; k2[:-1, :] |= k[1:, :]; k2[:, 1:] |= k[:, :-1]; k2[:, :-1] |= k[:, 1:]
            z = np.where(k2, z, np.nan).astype('<f4')
            if np.isfinite(z).any():
                tiles['%d_%d' % (ix, iy)] = base64.b64encode(z.tobytes()).decode('ascii')
    return {'cell': cell, 'tile': tile, 'x0': x0, 'y0': y0, 'tiles': tiles}


def grid(dem, bbox, cell, keep=None):
    """보기 화면용 격자 (cell m). keep(x,y)→bool 로 경로 근처만 남김 (나머지 NaN)."""
    xs = np.arange(bbox[0], bbox[2] + cell, cell)
    ys = np.arange(bbox[1], bbox[3] + cell, cell)
    gx, gy = np.meshgrid(xs, ys)
    z = dem(gx, gy)
    if keep is not None:
        z = np.where(keep(gx, gy), z, np.nan)
    return xs[0], ys[0], cell, z.astype(np.float32)
