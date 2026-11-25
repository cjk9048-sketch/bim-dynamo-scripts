"""COLMAP mesh_texturer 결과(mesh.ply + texture.png) → 보기 화면용 이진 (EPSG:5186 + EGM2008).
사용: python colmap_tex_to_web.py 작업폴더 [질감폴더=tex_colmap]"""
import json, os, sys, time
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from map_outputs_lib import model_to_ecef, to_5186_h

Image.MAX_IMAGE_PIXELS = None
ws = sys.argv[1]; td = os.path.join(ws, sys.argv[2] if len(sys.argv) > 2 else 'tex_colmap')
T0 = time.time()
b = open(os.path.join(td, 'mesh.ply'), 'rb').read()
i = b.find(b'end_header')
h = b.index(b'\n', i) + 1                       # 줄 끝이 \r\n 일 수도
head = [l.strip() for l in b[:h].decode('latin1').splitlines()]
nv = int([l for l in head if l.startswith('element vertex')][0].split()[2])
nf = int([l for l in head if l.startswith('element face')][0].split()[2])
V = np.frombuffer(b, np.float32, nv * 3, h).reshape(nv, 3).astype(np.float64)
F = np.frombuffer(b, np.uint8, nf * 38, h + nv * 12).reshape(nf, 38)
assert (F[:, 0] == 3).all() and (F[:, 13] == 6).all(), '세모 · 질감점 6개가 아닌 면이 있음'
fi = F[:, 1:13].copy().view(np.int32).reshape(nf, 3)
tc = F[:, 14:38].copy().view(np.float32).reshape(nf, 3, 2)
im = np.asarray(Image.open(os.path.join(td, 'texture.png')))
Hh, W = im.shape[:2]
# 질감 좌표 검사: 꼭짓점 셋이 사진 위에 (검지 않은 곳) 떨어지는 비율 — 두 방향
rng = np.random.default_rng(0); sel = rng.choice(nf, 20000, replace=False)
for nm, vv in (('v 그대로', tc[sel, :, 1]), ('1-v', 1 - tc[sel, :, 1])):
    px = np.clip((tc[sel, :, 0] * W).astype(int), 0, W - 1); py = np.clip((vv * Hh).astype(int), 0, Hh - 1)
    print('%-8s 사진 위 꼭짓점 %.1f%%' % (nm, 100 * (im[py, px].astype(int).sum(-1) >= 30).mean()))
# 면마다 꼭짓점 따로 (질감 좌표가 면마다라서)
s, R, t = model_to_ecef(ws)
P = V[fi.reshape(-1)]
E, N, H = to_5186_h(s * (P @ R.T) + t)
o = [round(float(np.median(E)), 1), round(float(np.median(N)), 1), round(float(np.percentile(H, 5)), 1)]
pos = np.stack([E - o[0], N - o[1], H - o[2]], 1).astype(np.float32)
uv = tc.reshape(-1, 2).astype(np.float32)
idx = np.arange(nf * 3, dtype=np.uint32)
out = os.path.join(ws, 'out', 'web_colmap'); os.makedirs(out, exist_ok=True)
with open(os.path.join(out, 'mesh.bin'), 'wb') as fp:
    fp.write(pos.tobytes()); fp.write(uv.tobytes()); fp.write(idx.tobytes())
Image.fromarray(im).save(os.path.join(out, 'tex.jpg'), quality=90)
json.dump({'crs': 'EPSG:5186', 'height': 'EGM2008', 'origin': o, 'verts': int(len(pos)), 'tris': int(nf),
           'min': pos.min(0).tolist(), 'max': pos.max(0).tolist(), 'tex': [W, Hh]}, open(os.path.join(out, 'mesh.json'), 'w'), indent=1)
print('보기용: 세모 %d · 질감 %d×%d · %.1f MB (%.1f초)' % (nf, W, Hh, os.path.getsize(os.path.join(out, 'mesh.bin')) / 1e6, time.time() - T0))
