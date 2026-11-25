"""시험용 사진 고르기 — 사진이 바라보는 땅 지점(짐벌 방향 · 높이로 셈)이 한 구역에 모이는 N 장.
사용: python pick_subset.py 사진폴더 출력폴더 [장수=80]"""
import glob, math, os, re, shutil, sys

src, dst = sys.argv[1], sys.argv[2]
N = int(sys.argv[3]) if len(sys.argv) > 3 else 80


def xmp(path):
    raw = open(path, 'rb').read(200000)
    i = raw.find(b'<x:xmpmeta')
    s = raw[i:raw.find(b'</x:xmpmeta>', i)].decode('utf-8', 'replace') if i >= 0 else ''
    g = lambda k: float(re.search(r'drone-dji:%s="([^"]*)"' % k, s).group(1))
    return g('GpsLatitude'), g('GpsLongitude'), g('RelativeAltitude'), g('GimbalYawDegree'), g('GimbalPitchDegree')


rows = []
for f in sorted(glob.glob(os.path.join(src, '*.JPG'))):
    try:
        lat, lon, h, yaw, pitch = xmp(f)
    except Exception:
        continue
    rows.append((f, lat, lon, h, yaw, pitch))
lat0 = sum(r[1] for r in rows) / len(rows)
lon0 = sum(r[2] for r in rows) / len(rows)
k = math.cos(math.radians(lat0)) * 111320.0
pts = []
for f, lat, lon, h, yaw, pitch in rows:
    x, y = (lon - lon0) * k, (lat - lat0) * 111320.0
    d = h / math.tan(math.radians(max(5.0, -pitch)))          # 바라보는 땅까지 수평 거리 (평평한 땅)
    a = math.radians(yaw)
    pts.append((f, x + d * math.sin(a), y + d * math.cos(a), yaw))
cx = sorted(p[1] for p in pts)[len(pts) // 2]
cy = sorted(p[2] for p in pts)[len(pts) // 2]
pick = sorted(pts, key=lambda p: math.hypot(p[1] - cx, p[2] - cy))[:N]
r = max(math.hypot(p[1] - cx, p[2] - cy) for p in pick)
dirs = {}
for p in pick:
    q = int(((p[3] + 360 + 45) % 360) // 90)          # 북 · 동 · 남 · 서
    dirs['북동남서'[q]] = dirs.get('북동남서'[q], 0) + 1
os.makedirs(dst, exist_ok=True)
for p in pick:
    shutil.copy2(p[0], os.path.join(dst, os.path.basename(p[0])))
print('전체 %d장 → %d장 · 바라보는 곳 반경 %.0f m · 방향 %s' % (len(rows), len(pick), r, dirs))
