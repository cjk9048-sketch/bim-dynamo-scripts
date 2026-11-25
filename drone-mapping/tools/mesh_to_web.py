"""OpenMVS 질감 OBJ → 보기 화면용 이진 (도면 좌표 EPSG:5186 + EGM2008 표고, 원점 빼고 float32).

사용: python mesh_to_web.py 작업폴더 [obj 이름=scene_cpu_tex]
출력: 작업폴더/out/web/{mesh.bin, mesh.json, tex.jpg}
  mesh.bin = 꼭짓점 위치 (E, N, H − 원점) float32 ×3 · 질감 좌표 float32 ×2 · 세모 번호 uint32 ×3
"""
import json, math, os, shutil, sys, time, warnings
import numpy as np

warnings.simplefilter('ignore')
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
ws = sys.argv[1]
name = sys.argv[2] if len(sys.argv) > 2 else 'scene_cpu_tex'
T0 = time.time()
import importlib.util
spec = importlib.util.spec_from_file_location('mo', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'map_outputs_lib.py'))

raw = open(os.path.join(ws, 'dense', name + '.obj'), 'rb').read()
lines = raw.split(b'\n')
vs = np.array(b' '.join(l[2:] for l in lines if l.startswith(b'v ')).split(), np.float64).reshape(-1, 3)
vts = np.array(b' '.join(l[3:] for l in lines if l.startswith(b'vt ')).split(), np.float32).reshape(-1, 2)
fl = b' '.join(l[2:] for l in lines if l.startswith(b'f ')).replace(b'/', b' ').split()
f = np.array(fl, np.int64).reshape(-1, 6) - 1               # v/vt v/vt v/vt
print('OBJ: 꼭짓점 %d · 질감점 %d · 세모 %d (%.1f초)' % (len(vs), len(vts), len(f), time.time() - T0))
# (꼭짓점, 질감점) 짝마다 하나
pairs = np.stack([f[:, 0::2].reshape(-1), f[:, 1::2].reshape(-1)], 1)
key = pairs[:, 0] * (len(vts) + 1) + pairs[:, 1]
uk, inv = np.unique(key, return_inverse=True)
vi = uk // (len(vts) + 1); ti = uk % (len(vts) + 1)
idx = inv.reshape(-1, 3).astype(np.uint32)

# 모델 → ECEF → 5186 + EGM2008
from map_outputs_lib import model_to_ecef, to_5186_h
s, R, t = model_to_ecef(ws)
ecef = s * (vs[vi] @ R.T) + t
E, N, H = to_5186_h(ecef)
o = [round(float(np.median(E)), 1), round(float(np.median(N)), 1), round(float(np.percentile(H, 5)), 1)]
pos = np.stack([E - o[0], N - o[1], H - o[2]], 1).astype(np.float32)
uv = vts[ti].copy(); uv[:, 1] = 1.0 - uv[:, 1]                # 그림 위쪽 = 0
out = os.path.join(ws, 'out', 'web'); os.makedirs(out, exist_ok=True)
with open(os.path.join(out, 'mesh.bin'), 'wb') as fp:
    fp.write(pos.tobytes()); fp.write(uv.astype(np.float32).tobytes()); fp.write(idx.tobytes())
shutil.copy2(os.path.join(ws, 'dense', name + '_material_00_map_Kd.jpg'), os.path.join(out, 'tex.jpg'))
meta = {'crs': 'EPSG:5186', 'height': 'EGM2008', 'origin': o, 'verts': int(len(pos)), 'tris': int(len(idx)),
        'min': pos.min(0).tolist(), 'max': pos.max(0).tolist()}
json.dump(meta, open(os.path.join(out, 'mesh.json'), 'w'), indent=1)
print('보기용: 꼭짓점 %d · 세모 %d · 원점 %s · %.1f MB (%.1f초)' % (len(pos), len(idx), o, os.path.getsize(os.path.join(out, 'mesh.bin')) / 1e6, time.time() - T0))
