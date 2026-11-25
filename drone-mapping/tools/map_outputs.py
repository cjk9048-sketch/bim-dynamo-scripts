"""매핑 결과 → 도면 좌표 (EPSG:5186 + EGM2008 표고) · 정사영상 · DSM (GeoTIFF + tfw + prj, Terra 결과와 같은 꼴).

사용: python map_outputs.py 작업폴더 [격자 m=0.05]
  작업폴더/sparse_txt/images.txt · db.db (카메라 ↔ RTK 위치로 모델 → ECEF 닮음 변환)
  작업폴더/dense/scene_cpu.ply (OpenMVS 점구름: xyz · rgb · 법선 · 보는 사진 목록)
"""
import math, os, sqlite3, struct, sys, time, warnings
import numpy as np

warnings.simplefilter('ignore')
ws = sys.argv[1]
CELL = float(sys.argv[2]) if len(sys.argv) > 2 else 0.05
T0 = time.time()


# ---------------------------------------------------------------- 모델 → ECEF (카메라 중심 ↔ RTK 위치 닮음 맞춤)
def qrot(w, x, y, z):
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y)],
                     [2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x)],
                     [2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y)]])


def model_to_ecef(ws):
    import pyproj
    cams = {}
    L = [l for l in open(os.path.join(ws, 'sparse_txt', 'images.txt'), encoding='utf-8') if not l.startswith('#')]
    for l in L[0::2]:
        p = l.split()
        if len(p) >= 10:
            cams[p[9]] = -qrot(*map(float, p[1:5])).T @ np.array(list(map(float, p[5:8])))
    db = sqlite3.connect(os.path.join(ws, 'db.db'))
    names = {r[0]: r[1] for r in db.execute('select image_id, name from images')}
    tr = pyproj.Transformer.from_crs('EPSG:4979', 'EPSG:4978', always_xy=True)
    pri = {}
    for iid, pos in db.execute('select corr_data_id, position from pose_priors'):
        lat, lon, h = struct.unpack('<3d', pos)
        pri[names[iid]] = np.array(tr.transform(lon, lat, h))
    ks = [k for k in cams if k in pri]
    X = np.array([cams[k] for k in ks]); P = np.array([pri[k] for k in ks])
    mx, mp = X.mean(0), P.mean(0)
    U, S, Vt = np.linalg.svd((P - mp).T @ (X - mx))
    D = np.eye(3); D[2, 2] = np.sign(np.linalg.det(U @ Vt))
    R = U @ D @ Vt; s = (S * np.diag(D)).sum() / ((X - mx) ** 2).sum(); t = mp - s * R @ mx
    res = P - (s * (R @ X.T).T + t)
    print('모델 → ECEF: 사진 %d · 배율 %.6f · 카메라 어긋남 RMS %.3f m' % (len(ks), s, math.sqrt((res ** 2).sum(1).mean())))
    return s, R, t


# ---------------------------------------------------------------- OpenMVS 점구름 PLY (목록 칸 있음)
def read_openmvs_ply(path):
    b = open(path, 'rb').read()
    h = b.find(b'end_header\n') + len(b'end_header\n')
    head = b[:h].decode('latin1')
    n = int([l for l in head.splitlines() if l.startswith('element vertex')][0].split()[2])
    a = np.frombuffer(b, np.uint8, offset=h)
    xyz = np.empty((n, 3), np.float32); rgb = np.empty((n, 3), np.uint8)
    off = np.empty(n, np.int64)
    o = 0
    mv = memoryview(b)[h:]
    for i in range(n):                    # 앞 27 바이트 고정 · 그 뒤 목록 둘 (보는 사진 · 무게)
        off[i] = o
        c1 = mv[o + 27]
        c2 = mv[o + 28 + 4 * c1]
        o += 29 + 4 * c1 + 4 * c2
    idx = off[:, None] + np.arange(12)[None, :]
    xyz[:] = a[idx].reshape(n, 12).copy().view(np.float32).reshape(n, 3)
    rgb[:] = a[off[:, None] + 12 + np.arange(3)[None, :]]
    return xyz, rgb


s, R, t = model_to_ecef(ws)
xyz, rgb = read_openmvs_ply(os.path.join(ws, 'dense', 'scene_cpu.ply'))
print('점 %d개 읽음 (%.1f초)' % (len(xyz), time.time() - T0))
ecef = s * (xyz.astype(np.float64) @ R.T) + t
import pyproj
pyproj.network.set_network_enabled(True)
# 한 번에(4978 → 5186+3855) 하면 지오이드를 건너뛰고 타원체고가 그대로 나온다 (1125 실측: DEM 보다 24.45 m 높음 = 지오이드 높이)
# → ECEF → 위경도 · 타원체고 → (평면) 5186 · (높이) EGM2008 따로
lon, lat, hel = pyproj.Transformer.from_crs('EPSG:4978', 'EPSG:4979', always_xy=True).transform(ecef[:, 0], ecef[:, 1], ecef[:, 2])
E, N = pyproj.Transformer.from_crs('EPSG:4326', 'EPSG:5186', always_xy=True).transform(lon, lat)
_, _, H = pyproj.Transformer.from_crs('EPSG:4979', 'EPSG:4326+3855', always_xy=True).transform(lon, lat, hel)
E, N, H = np.asarray(E), np.asarray(N), np.asarray(H)
print('지오이드 높이 (타원체고 − 표고) 가운데값 %.3f m' % float(np.nanmedian(np.asarray(hel) - H)))
ok = np.isfinite(E) & np.isfinite(H)
E, N, H, rgb = E[ok], N[ok], H[ok], rgb[ok]
print('범위 E %.1f~%.1f · N %.1f~%.1f · 표고 %.2f~%.2f (%.1f초)' % (E.min(), E.max(), N.min(), N.max(), np.percentile(H, 1), np.percentile(H, 99), time.time() - T0))

# ---------------------------------------------------------------- 격자: 가장 높은 점 = DSM · 그 점 색 = 정사
x0 = math.floor(E.min() / CELL) * CELL; y1 = math.ceil(N.max() / CELL) * CELL
W = int(math.ceil((E.max() - x0) / CELL)) + 1; Hh = int(math.ceil((y1 - N.min()) / CELL)) + 1
ci = ((E - x0) / CELL).astype(np.int64); ri = ((y1 - N) / CELL).astype(np.int64)
cell = ri * W + ci
order = np.lexsort((H, cell))                           # 칸마다 높은 점이 끝에
last = np.r_[cell[order][1:] != cell[order][:-1], True]
top = order[last]
dsm = np.full(W * Hh, np.nan, np.float32); dsm[cell[top]] = H[top]
ort = np.zeros((W * Hh, 3), np.uint8); ort[cell[top]] = rgb[top]
dsm = dsm.reshape(Hh, W); ort = ort.reshape(Hh, W, 3)
# 작은 빈칸 메우기 (이웃 값 · 3번)
filled = np.isfinite(dsm)
for _ in range(3):
    if filled.all():
        break
    acc = np.zeros_like(dsm); cnt = np.zeros_like(dsm); accc = np.zeros(ort.shape, np.float32)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            m = np.roll(np.roll(filled, dy, 0), dx, 1)
            v = np.roll(np.roll(np.nan_to_num(dsm), dy, 0), dx, 1)
            c = np.roll(np.roll(ort, dy, 0), dx, 1).astype(np.float32)
            acc += v * m; cnt += m; accc += c * m[..., None]
    new = ~filled & (cnt > 0)
    dsm[new] = acc[new] / cnt[new]; ort[new] = (accc[new] / cnt[new][:, None]).astype(np.uint8)
    filled |= new
print('격자 %d×%d · %.2f m · 채움 %.1f%% (%.1f초)' % (W, Hh, CELL, 100 * filled.mean(), time.time() - T0))

# ---------------------------------------------------------------- 쓰기 (TIFF + tfw + prj)
from PIL import Image
out = os.path.join(ws, 'out'); os.makedirs(out, exist_ok=True)
wkt = pyproj.CRS('EPSG:5186').to_wkt(pyproj.enums.WktVersion.WKT1_ESRI)
def world(name):
    open(os.path.join(out, name + '.tfw'), 'w').write('%.4f\n0\n0\n%.4f\n%.4f\n%.4f\n' % (CELL, -CELL, x0 + CELL / 2, y1 - CELL / 2))
    open(os.path.join(out, name + '.prj'), 'w').write(wkt)
alpha = (filled * 255).astype(np.uint8)
Image.fromarray(np.dstack([ort, alpha]), 'RGBA').save(os.path.join(out, 'ortho.tif'), compression='tiff_lzw')
world('ortho')
Image.fromarray(np.where(filled, dsm, -9999).astype(np.float32), 'F').save(os.path.join(out, 'dsm.tif'))
world('dsm')
Image.fromarray(np.dstack([ort, alpha]), 'RGBA').resize((max(1, W // 4), max(1, Hh // 4))).save(os.path.join(out, 'ortho_small.png'))
print('저장: %s (%.1f초)' % (out, time.time() - T0))

# ---------------------------------------------------------------- 표고 점검: 사내 수치지도 DEM 과 견줌 (5 m 칸마다 가장 낮은 곳 ≈ 땅)
sys.path.insert(0, r'C:\Users\user\Desktop\AI\drone-roadview')
try:
    from roadview import terrain
    if terrain.db_available():
        bbox = (E.min() - 200, N.min() - 200, E.max() + 200, N.max() + 200)
        cl = terrain.contours_from_db(bbox, 5186); sp = terrain.spot_heights_from_db(bbox, 5186)
        P = terrain.lines_to_points(cl, 2.0)
        if len(sp):
            P = np.vstack([P, sp])
        dem = terrain.GridDem(P, cell=5.0, sigma=15.0)
        B = 5.0
        bi = ((E - E.min()) // B).astype(int) * 100000 + ((N - N.min()) // B).astype(int)
        o2 = np.lexsort((H, bi)); first = np.r_[True, bi[o2][1:] != bi[o2][:-1]]
        lo = o2[first]
        dz = H[lo] - np.asarray(dem(E[lo], N[lo]))
        dz = dz[np.isfinite(dz)]
        print('사내 DEM 견줌: 5 m 칸 %d곳 · (매핑 가장 낮은 곳 − DEM) 가운데값 %.2f m · 사분위 %.2f ~ %.2f m' %
              (len(dz), np.median(dz), np.percentile(dz, 25), np.percentile(dz, 75)))
    else:
        print('사내 DEM 접속 없음 → 표고 점검 건너뜀')
except Exception as ex:
    print('DEM 견줌 못 함:', ex)
