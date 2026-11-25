"""카메라 풀기 결과 좌표계 확인 — 풀린 카메라 중심 vs RTK 위치(위경도 · 높이) 닮음 맞춤 (Umeyama).
사용: python check_frame.py 작업폴더  (sparse_txt/images.txt · db.db 필요)"""
import math, sqlite3, struct, sys
import numpy as np

ws = sys.argv[1]
def qrot(w, x, y, z):
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y)],
                     [2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x)],
                     [2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y)]])
cams = {}
lines = [l for l in open(ws + r'\sparse_txt\images.txt', encoding='utf-8') if not l.startswith('#')]
for l in lines[0::2]:
    p = l.split()
    if len(p) < 10:
        continue
    q = list(map(float, p[1:5])); t = np.array(list(map(float, p[5:8])))
    cams[p[9]] = -qrot(*q).T @ t
db = sqlite3.connect(ws + r'\db.db')
names = {r[0]: r[1] for r in db.execute('select image_id, name from images')}
pri = {}
for iid, pos in db.execute('select corr_data_id, position from pose_priors'):
    pri[names[iid]] = struct.unpack('<3d', pos)
# WGS84 → ECEF → ENU (가운데 기준)
a, f = 6378137.0, 1 / 298.257223563; e2 = f * (2 - f)
def ecef(lat, lon, h):
    la, lo = math.radians(lat), math.radians(lon); N = a / math.sqrt(1 - e2 * math.sin(la) ** 2)
    return np.array([(N + h) * math.cos(la) * math.cos(lo), (N + h) * math.cos(la) * math.sin(lo), (N * (1 - e2) + h) * math.sin(la)])
ks = [k for k in cams if k in pri]
lat0 = np.mean([pri[k][0] for k in ks]); lon0 = np.mean([pri[k][1] for k in ks]); h0 = np.mean([pri[k][2] for k in ks])
la, lo = math.radians(lat0), math.radians(lon0)
Rn = np.array([[-math.sin(lo), math.cos(lo), 0], [-math.sin(la) * math.cos(lo), -math.sin(la) * math.sin(lo), math.cos(la)],
               [math.cos(la) * math.cos(lo), math.cos(la) * math.sin(lo), math.sin(la)]])
o = ecef(lat0, lon0, h0)
P = np.array([Rn @ (ecef(*pri[k]) - o) for k in ks]); X = np.array([cams[k] for k in ks])
mx, mp = X.mean(0), P.mean(0); Xc, Pc = X - mx, P - mp
U, S, Vt = np.linalg.svd(Pc.T @ Xc); D = np.eye(3); D[2, 2] = np.sign(np.linalg.det(U @ Vt))
R = U @ D @ Vt; s = (S * np.diag(D)).sum() / (Xc ** 2).sum(); tvec = mp - s * R @ mx
res = P - (s * (R @ X.T).T + tvec)
print('사진 %d · 닮음 배율 %.6f · 회전(도) %.3f' % (len(ks), s, math.degrees(math.acos(max(-1, min(1, (np.trace(R) - 1) / 2))))))
print('맞춘 뒤 어긋남 m: 평면 RMS %.3f · 높이 RMS %.3f · 최대 %.3f' % (math.sqrt((res[:, :2] ** 2).sum(1).mean()), math.sqrt((res[:, 2] ** 2).mean()), np.abs(res).max()))
print('모델 원점의 ENU 위치(가운데 기준) m:', np.round(tvec, 3), ' / 모델 카메라 중심 평균:', np.round(mx, 2))
