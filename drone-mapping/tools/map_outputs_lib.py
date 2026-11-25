"""매핑 좌표 공용: 모델 → ECEF (카메라 ↔ RTK 닮음 맞춤) · ECEF → EPSG:5186 + EGM2008 표고."""
import math, os, sqlite3, struct, warnings
import numpy as np

warnings.simplefilter('ignore')


def _qrot(w, x, y, z):
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y)],
                     [2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x)],
                     [2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y)]])


def model_to_ecef(ws, quiet=False):
    """(배율 s, 회전 R, 이동 t): ECEF = s·R·X + t."""
    import pyproj
    cams = {}
    L = [l for l in open(os.path.join(ws, 'sparse_txt', 'images.txt'), encoding='utf-8') if not l.startswith('#')]
    for l in L[0::2]:
        p = l.split()
        if len(p) >= 10:
            cams[p[9]] = -_qrot(*map(float, p[1:5])).T @ np.array(list(map(float, p[5:8])))
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
    if not quiet:
        print('모델 → ECEF: 사진 %d · 배율 %.6f · 카메라 어긋남 RMS %.3f m' % (len(ks), s, math.sqrt((res ** 2).sum(1).mean())))
    return s, R, t


def to_5186_h(ecef):
    """ECEF (n,3) → (E, N, 표고 EGM2008). 한 번에(4978 → 5186+3855) 하면 지오이드를 건너뛴다 (1125 실측 24.45 m)."""
    import pyproj
    pyproj.network.set_network_enabled(True)
    lon, lat, hel = pyproj.Transformer.from_crs('EPSG:4978', 'EPSG:4979', always_xy=True).transform(ecef[:, 0], ecef[:, 1], ecef[:, 2])
    E, N = pyproj.Transformer.from_crs('EPSG:4326', 'EPSG:5186', always_xy=True).transform(lon, lat)
    _, _, H = pyproj.Transformer.from_crs('EPSG:4979', 'EPSG:4326+3855', always_xy=True).transform(lon, lat, hel)
    return np.asarray(E), np.asarray(N), np.asarray(H)
