"""경사촬영 사진 → 3D 매핑 (JACK 1125 · DJI Terra 대체 · 드론 로드뷰 안).

차례 (1125 80장 시험으로 정함 · AI\\drone-mapping\\README.md):
  COLMAP 4.2.1  특징점(GPU) → GPS 이웃 맞추기(GPU) → RTK 위치 카메라 풀기(pose_prior_mapper) → 왜곡 펴기
  OpenMVS 2.4.0 촘촘한 점구름 → 3D 면 (CPU — CUDA 판은 RTX 50 커널 없음)
  COLMAP        질감 (mesh_texturer — OpenMVS 질감은 이 판에서 좌표가 그림과 어긋남)
  → 도면 좌표 (EPSG:5186 + EGM2008 표고) → 3D 보기 자료 · 정사영상 · DSM (GeoTIFF + tfw + prj) · Civil 3D 지표면 (LandXML)

    python -m roadview.mapping 사진폴더 --out 결과폴더 [--quality fast|normal|fine] [--names 목록.txt]
"""
from __future__ import annotations

import argparse
import glob
import json
import math
import os
import re
import shutil
import sqlite3
import struct
import subprocess
import sys
import time
import warnings
from pathlib import Path

import numpy as np

NO_WINDOW = 0x08000000 if os.name == 'nt' else 0
CRS = 'EPSG:5186'
PIPELINE_VERSION = '2'      # 2 = 3D 면 세모 한도      # 차례 · 옵션을 바꾸면 올림 → 지난번 중간 자료를 다시 쓰지 않음
# 품질: 질감용 사진 크기 · 특징점 사진 크기 (점구름은 질감용의 절반 · normal = 1125 80장 시험 값)
QUALITY = {'fast': (1320, 2000), 'normal': (2640, 3200), 'fine': (5280, 4000)}
QUALITY_NAME = {'fast': '빠르게', 'normal': '보통', 'fine': '정밀'}
# 3D 보기 면 세모 수 한도 (보기 전용 · 정사 · DSM · 지표면은 점구름 그대로라 정확도는 안 바뀜 — 설계서 10.4)
TARGET_FACES = {'fast': 1000000, 'normal': 3000000, 'fine': 6000000}
ENGINE_DIRS = [Path(os.environ.get('LOCALAPPDATA', str(Path.home()))) / 'DH.Roadview' / 'engines', Path(r'D:\매핑시험\engines')]
ENGINE_URLS = {
    'colmap': 'https://github.com/colmap/colmap/releases/download/4.2.1/colmap-x64-windows-cuda.zip',
    'openmvs': 'https://github.com/cdcseacave/openMVS/releases/download/v2.4.0/OpenMVS_Windows_x64.zip',
}


def log(msg, fp=None):
    line = time.strftime('%H:%M:%S ') + msg
    print(line, flush=True)
    if fp:
        fp.write(line + '\n'); fp.flush()


# ---------------------------------------------------------------- 엔진
def find_engines():
    """(colmap.exe, openmvs 폴더) · 없으면 (None, None)."""
    for d in ENGINE_DIRS:
        c = d / 'colmap' / 'bin' / 'colmap.exe'
        m = d / 'openmvs'
        if c.exists() and (m / 'DensifyPointCloud.exe').exists():
            return str(c), str(m)
    return None, None


def install_engines(progress=print):
    """처음 한 번 엔진 내려받기 (%LOCALAPPDATA%\\DH.Roadview\\engines) · zip 만 (7z 없이)."""
    import urllib.request
    import zipfile
    dst = ENGINE_DIRS[0]
    dst.mkdir(parents=True, exist_ok=True)
    for name, url in ENGINE_URLS.items():
        tgt = dst / name
        if tgt.exists() and any(tgt.iterdir()):
            continue
        z = dst / (name + '.zip')
        progress('엔진 내려받기 · %s' % name)
        with urllib.request.urlopen(url, timeout=60) as r, open(z, 'wb') as fp:
            shutil.copyfileobj(r, fp, 1 << 20)
        progress('엔진 풀기 · %s' % name)
        with zipfile.ZipFile(z) as zf:
            zf.extractall(tgt)
        z.unlink()
        # 실행 파일이 든 폴더를 끌어올림 (OpenMVS zip = vc17\x64\Release\*.exe · COLMAP zip = bin\colmap.exe 가 바로)
        key = {'colmap': 'colmap.exe', 'openmvs': 'DensifyPointCloud.exe'}[name]
        hit = next(iter(tgt.rglob(key)), None)
        if hit is not None:
            src = hit.parent.parent if name == 'colmap' else hit.parent     # colmap 은 bin 의 위
            if src != tgt:
                for p in list(src.iterdir()):
                    shutil.move(str(p), str(tgt / p.name))
    return find_engines()


# ---------------------------------------------------------------- 사진
_XMP_KEYS = ('GpsLatitude', 'GpsLongitude', 'AbsoluteAltitude', 'RelativeAltitude', 'GimbalYawDegree', 'GimbalPitchDegree', 'RtkFlag')


def photo_info(path):
    """DJI 사진 XMP → {lat, lon, alt, rel, yaw, pitch, rtk} · 매핑 사진이 아니면 None (360 등장방형 · 위치 없음)."""
    raw = open(path, 'rb').read(200000)
    i = raw.find(b'<x:xmpmeta')
    if i < 0:
        return None
    s = raw[i:raw.find(b'</x:xmpmeta>', i)].decode('utf-8', 'replace')
    if 'GPano:ProjectionType' in s and 'equirectangular' in s:
        return None
    v = {}
    for k in _XMP_KEYS:
        m = re.search(r'drone-dji:%s="([^"]*)"' % k, s) or re.search(r'<drone-dji:%s>([^<]*)<' % k, s)
        if m:
            try:
                v[k] = float(m.group(1))
            except ValueError:
                pass
    if 'GpsLatitude' not in v or 'GpsLongitude' not in v:
        return None
    # 카메라 구분 (설계서 4.3 · 5.2: 기체 이름이 아니라 실제 찍은 카메라마다 내부표정) — 광각 · 망원 · 크기
    src = re.search(r'drone-dji:ImageSource="([^"]*)"', s)
    fcal = re.search(r'drone-dji:CalibratedFocalLength="([^"]*)"', s)
    dew = re.search(r'drone-dji:DewarpData="([^"]*)"', s)
    model = re.search(rb'(M4E|M3E|M30T|M300|FC\d{4}|L2|P1|ZH20T|H20)', raw[:4096])
    try:
        from PIL import Image
        with Image.open(path) as im:
            w, h = im.size
    except Exception:
        w = h = 0
    return {'name': os.path.basename(path), 'lat': v['GpsLatitude'], 'lon': v['GpsLongitude'], 'alt': v.get('AbsoluteAltitude'),
            'rel': v.get('RelativeAltitude'), 'yaw': v.get('GimbalYawDegree', 0.0), 'pitch': v.get('GimbalPitchDegree', -90.0),
            'rtk': int(v.get('RtkFlag', 0)), 'w': w, 'h': h, 'src': src.group(1) if src else '',
            'model': model.group(1).decode() if model else '', 'fcal': float(fcal.group(1)) if fcal else None,
            'dewarp': dew.group(1) if dew else ''}


def camera_groups(photos):
    """{(기종, 카메라, 가로, 세로): [사진…]} — 많은 쪽부터."""
    g = {}
    for p in photos:
        g.setdefault((p.get('model', ''), p.get('src', ''), p.get('w', 0), p.get('h', 0)), []).append(p)
    return sorted(g.items(), key=lambda kv: -len(kv[1]))


def camera_params(p):
    """COLMAP OPENCV 처음 값 (fx, fy, cx, cy, k1, k2, p1, p2) = CalibratedFocalLength · 가운데 · 왜곡 0 (1125 80 · 394장 시험 값,
    묶음 조정에서 초점 · 왜곡을 다시 맞춤). 없으면 None (COLMAP 이 EXIF 초점거리로).
    DJI DewarpData(가운데 기준 cx · cy 와 왜곡) 는 뜻을 실물로 아직 확인 못 해 안 씀."""
    w, h = p.get('w') or 0, p.get('h') or 0
    if not (w and h and p.get('fcal')):
        return None
    return '%.3f,%.3f,%.1f,%.1f,0,0,0,0' % (p['fcal'], p['fcal'], w / 2, h / 2)


def scan_folder(folder):
    """폴더의 매핑 사진 목록 + 바라보는 땅 지점(평평한 땅 · 짐벌 방향) — 지도에 범위 고르기용."""
    out = []
    for f in sorted(glob.glob(os.path.join(folder, '*.JPG')) + glob.glob(os.path.join(folder, '*.jpg'))):
        try:
            p = photo_info(f)
        except OSError:
            p = None
        if not p:
            continue
        d = (p['rel'] or 50.0) / math.tan(math.radians(max(5.0, -(p['pitch'] or -90.0))))
        a = math.radians(p['yaw'] or 0.0)
        k = 111320.0 * math.cos(math.radians(p['lat']))
        p['look_lat'] = p['lat'] + d * math.cos(a) / 111320.0
        p['look_lon'] = p['lon'] + d * math.sin(a) / k
        out.append(p)
    seen = set(); uniq = []
    for p in out:                                  # 대소문자만 다른 같은 파일 (*.JPG · *.jpg)
        if p['name'].lower() not in seen:
            seen.add(p['name'].lower()); uniq.append(p)
    return uniq


# ---------------------------------------------------------------- 좌표 (모델 → ECEF → 5186 + EGM2008)
def _qrot(w, x, y, z):
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y)],
                     [2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x)],
                     [2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y)]])


def model_to_ecef(ws):
    """(배율 s, 회전 R, 이동 t, 카메라 어긋남 RMS m): ECEF = s·R·X + t (풀린 카메라 중심 ↔ RTK 위치 닮음 맞춤)."""
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
    db.close()
    ks = [k for k in cams if k in pri]
    X = np.array([cams[k] for k in ks]); P = np.array([pri[k] for k in ks])
    mx, mp = X.mean(0), P.mean(0)
    U, S, Vt = np.linalg.svd((P - mp).T @ (X - mx))
    D = np.eye(3); D[2, 2] = np.sign(np.linalg.det(U @ Vt))
    R = U @ D @ Vt; s = (S * np.diag(D)).sum() / ((X - mx) ** 2).sum(); t = mp - s * R @ mx
    res = P - (s * (R @ X.T).T + t)
    return s, R, t, float(math.sqrt((res ** 2).sum(1).mean())), len(ks)


def to_5186_h(ecef):
    """ECEF (n,3) → (E, N, 표고 EGM2008). 한 번에(4978 → 5186+3855) 하면 지오이드를 건너뛴다 (1125 실측 24.45 m)."""
    import pyproj
    warnings.simplefilter('ignore')
    pyproj.network.set_network_enabled(True)
    lon, lat, hel = pyproj.Transformer.from_crs('EPSG:4978', 'EPSG:4979', always_xy=True).transform(ecef[:, 0], ecef[:, 1], ecef[:, 2])
    E, N = pyproj.Transformer.from_crs('EPSG:4326', CRS, always_xy=True).transform(lon, lat)
    _, _, H = pyproj.Transformer.from_crs('EPSG:4979', 'EPSG:4326+3855', always_xy=True).transform(lon, lat, hel)
    H = np.asarray(H)
    if not np.isfinite(H).any():
        raise RuntimeError('표고 변환 실패 (EGM2008 지오이드 파일 · 인터넷 확인)')
    return np.asarray(E), np.asarray(N), H


# ---------------------------------------------------------------- 결과물
def read_openmvs_ply(path):
    """OpenMVS 점구름 (xyz · rgb · 법선 · 보는 사진 목록 · 무게 목록)."""
    b = open(path, 'rb').read()
    i = b.find(b'end_header'); h = b.index(b'\n', i) + 1
    head = b[:h].decode('latin1')
    n = int([l for l in head.splitlines() if l.startswith('element vertex')][0].split()[2])
    a = np.frombuffer(b, np.uint8, offset=h)
    off = np.empty(n, np.int64)
    mv = memoryview(b)[h:]
    o = 0
    for k in range(n):
        off[k] = o
        c1 = mv[o + 27]
        o += 29 + 4 * c1 + 4 * mv[o + 28 + 4 * c1]
    xyz = a[off[:, None] + np.arange(12)[None, :]].copy().view(np.float32).reshape(n, 3)
    rgb = a[off[:, None] + 12 + np.arange(3)[None, :]]
    return xyz, rgb


def _world(out, name, x0, y1, cell):
    import pyproj
    with open(os.path.join(out, name + '.tfw'), 'w') as fp:
        fp.write('%.4f\n0\n0\n%.4f\n%.4f\n%.4f\n' % (cell, -cell, x0 + cell / 2, y1 - cell / 2))
    with open(os.path.join(out, name + '.prj'), 'w') as fp:
        fp.write(pyproj.CRS(CRS).to_wkt(pyproj.enums.WktVersion.WKT1_ESRI))


def ortho_dsm(E, N, H, rgb, out, cell):
    """가장 높은 점 = DSM · 그 점 색 = 정사. 작은 빈칸은 이웃으로 3번 메움. → (dsm, 채움, x0, y1)."""
    from PIL import Image
    x0 = math.floor(E.min() / cell) * cell; y1 = math.ceil(N.max() / cell) * cell
    W = int(math.ceil((E.max() - x0) / cell)) + 1; Hh = int(math.ceil((y1 - N.min()) / cell)) + 1
    cidx = ((y1 - N) / cell).astype(np.int64) * W + ((E - x0) / cell).astype(np.int64)
    order = np.lexsort((H, cidx))
    top = order[np.r_[cidx[order][1:] != cidx[order][:-1], True]]
    dsm = np.full(W * Hh, np.nan, np.float32); dsm[cidx[top]] = H[top]
    ort = np.zeros((W * Hh, 3), np.uint8); ort[cidx[top]] = rgb[top]
    dsm = dsm.reshape(Hh, W); ort = ort.reshape(Hh, W, 3)
    filled = np.isfinite(dsm)
    for _ in range(3):
        acc = np.zeros_like(dsm); cnt = np.zeros_like(dsm); accc = np.zeros(ort.shape, np.float32)
        dz = np.nan_to_num(dsm)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                m = np.roll(np.roll(filled, dy, 0), dx, 1)
                acc += np.roll(np.roll(dz, dy, 0), dx, 1) * m; cnt += m
                accc += np.roll(np.roll(ort, dy, 0), dx, 1).astype(np.float32) * m[..., None]
        new = ~filled & (cnt > 0)
        if not new.any():
            break
        dsm[new] = acc[new] / cnt[new]; ort[new] = (accc[new] / cnt[new][:, None]).astype(np.uint8)
        filled |= new
    alpha = (filled * 255).astype(np.uint8)
    Image.fromarray(np.dstack([ort, alpha]), 'RGBA').save(os.path.join(out, '정사영상.tif'), compression='tiff_lzw')
    _world(out, '정사영상', x0, y1, cell)
    Image.fromarray(np.where(filled, dsm, -9999).astype(np.float32), 'F').save(os.path.join(out, 'DSM.tif'))
    _world(out, 'DSM', x0, y1, cell)
    k = max(1, max(W, Hh) // 2048)
    Image.fromarray(np.dstack([ort, alpha]), 'RGBA').resize((max(1, W // k), max(1, Hh // k))).save(os.path.join(out, '정사영상_미리보기.png'))
    return dsm, filled, x0, y1


def landxml_from_dsm(dsm, filled, x0, y1, cell, out, step_m=1.0, name='DSM'):
    """DSM 을 step_m 격자로 솎아 세모 지표면 LandXML (Civil 3D 지표면 가져오기용 · 건물 · 나무 포함 = 표면 모델)."""
    k = max(1, int(round(step_m / cell)))
    Z = dsm[::k, ::k]; F = filled[::k, ::k]
    rows, cols = Z.shape
    ids = -np.ones(Z.shape, np.int64)
    ok = F & np.isfinite(Z)
    ids[ok] = np.arange(1, ok.sum() + 1)
    lines = ['<?xml version="1.0" encoding="UTF-8"?>',
             '<LandXML xmlns="http://www.landxml.org/schema/LandXML-1.2" version="1.2">',
             '<Units><Metric areaUnit="squareMeter" linearUnit="meter" volumeUnit="cubicMeter" temperatureUnit="celsius" pressureUnit="milliBars"/></Units>',
             '<Surfaces><Surface name="%s"><Definition surfType="TIN"><Pnts>' % name]
    rr, cc = np.nonzero(ok)
    for r, c in zip(rr, cc):          # LandXML 점 = 북 동 높이
        lines.append('<P id="%d">%.3f %.3f %.3f</P>' % (ids[r, c], y1 - (r * k + 0.5) * cell, x0 + (c * k + 0.5) * cell, Z[r, c]))
    lines.append('</Pnts><Faces>')
    a = ids[:-1, :-1]; b = ids[:-1, 1:]; c_ = ids[1:, :-1]; d = ids[1:, 1:]
    m1 = (a > 0) & (b > 0) & (c_ > 0); m2 = (b > 0) & (d > 0) & (c_ > 0)
    for p, q, r in zip(a[m1], c_[m1], b[m1]):
        lines.append('<F>%d %d %d</F>' % (p, q, r))
    for p, q, r in zip(b[m2], c_[m2], d[m2]):
        lines.append('<F>%d %d %d</F>' % (p, q, r))
    lines.append('</Faces></Definition></Surface></Surfaces></LandXML>')
    with open(os.path.join(out, '지표면_DSM.xml'), 'w', encoding='utf-8') as fp:
        fp.write('\n'.join(lines))
    return int(ok.sum()), int(m1.sum() + m2.sum())


def textured_mesh_to_web(ws, s, R, t, out, td=None):
    """COLMAP mesh_texturer (mesh.ply + texture.png) → 보기 자료 data/mesh.bin · mesh.json · tex.jpg (원점 빼고 float32)."""
    from PIL import Image
    Image.MAX_IMAGE_PIXELS = None
    td = td or os.path.join(ws, 'tex_colmap')
    b = open(os.path.join(td, 'mesh.ply'), 'rb').read()
    i = b.find(b'end_header'); h = b.index(b'\n', i) + 1
    head = [l.strip() for l in b[:h].decode('latin1').splitlines()]
    nv = int([l for l in head if l.startswith('element vertex')][0].split()[2])
    nf = int([l for l in head if l.startswith('element face')][0].split()[2])
    V = np.frombuffer(b, np.float32, nv * 3, h).reshape(nv, 3).astype(np.float64)
    F = np.frombuffer(b, np.uint8, nf * 38, h + nv * 12).reshape(nf, 38)
    if not ((F[:, 0] == 3).all() and (F[:, 13] == 6).all()):
        raise RuntimeError('질감 면 형식이 다름 (세모 · 질감점 6개)')
    fi = F[:, 1:13].copy().view(np.int32).reshape(nf, 3)
    tc = F[:, 14:38].copy().view(np.float32).reshape(nf, 3, 2).copy()
    tc[..., 1] = 1.0 - tc[..., 1]                       # 그림 위쪽 = 0 (COLMAP = OBJ 꼴 · 1125 99.9 %)
    E, N, H = to_5186_h(s * (V @ R.T) + t)
    o = [round(float(np.median(E)), 1), round(float(np.median(N)), 1), round(float(np.percentile(H, 5)), 1)]
    vpos = np.stack([E - o[0], N - o[1], H - o[2]], 1).astype(np.float32)
    pos = vpos[fi.reshape(-1)]
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, 'mesh.bin'), 'wb') as fp:
        fp.write(pos.tobytes()); fp.write(tc.reshape(-1, 2).astype(np.float32).tobytes())
    im = Image.open(os.path.join(td, 'texture.png')).convert('RGB')
    im.save(os.path.join(out, 'tex.jpg'), quality=90)
    meta = {'crs': CRS, 'height': 'EGM2008', 'origin': o, 'verts': int(len(pos)), 'tris': int(nf), 'indexed': False,
            'min': pos.min(0).tolist(), 'max': pos.max(0).tolist(), 'tex': list(im.size)}
    with open(os.path.join(out, 'mesh.json'), 'w') as fp:
        json.dump(meta, fp, indent=1)
    return meta


# ---------------------------------------------------------------- 관로 넣기 (매핑 프로젝트 · 땅 높이 = DSM)
def _read_js(path, var):
    s = open(path, encoding='utf-8').read()
    return json.loads(s.split('=', 1)[1].strip().rstrip(';'))


def dsm_sampler(out):
    """결과물/DSM.tif + tfw → f(E 배열, N 배열) = 표고 (빈칸 NaN)."""
    from PIL import Image
    Image.MAX_IMAGE_PIXELS = None
    d = os.path.join(out, '결과물')
    A = np.asarray(Image.open(os.path.join(d, 'DSM.tif')), np.float32)
    w = [float(x) for x in open(os.path.join(d, 'DSM.tfw')).read().split()]
    cell, xc, yc = w[0], w[4], w[5]

    def f(E, N):
        E = np.asarray(E, float); N = np.asarray(N, float)
        c = np.round((E - xc) / cell).astype(int); r = np.round((yc - N) / cell).astype(int)
        ok = (r >= 0) & (r < A.shape[0]) & (c >= 0) & (c < A.shape[1])
        z = np.full(E.shape, np.nan)
        z[ok] = A[r[ok], c[ok]]
        z[z <= -9998] = np.nan
        return z
    return f


def import_pipes(out, path, dia_field=None, label_field=None, depth=None, layers=None, default_dia=200.0, log=print):
    """관로 SHP · DXF → data/pipes.js (보기 화면과 같은 꼴 · 원점 = 매핑 원점) · 매핑 범위(+30 m) 밖은 자름. → (조각 수, 알림)."""
    from . import pipeload
    from .geo import Geo
    P = _read_js(os.path.join(out, 'data', 'mapping.js'), 'RV_MAPPING')
    E0, N0 = float(P['origin'][0]), float(P['origin'][1])
    x0, y0, x1, y1 = P['bbox']
    pad = 30.0
    pats = [x.strip() for x in layers.split(',')] if layers else None
    lay, lines, note = pipeload.load(path, CRS, dia_field, label_field, pats, default_dia)
    n_raw = len(lines)
    lines = pipeload.densify(lines, 2.0)
    inside = lambda p: x0 - pad <= p[0] <= x1 + pad and y0 - pad <= p[1] <= y1 + pad   # noqa: E731
    cut = []
    for ln in lines:                                   # 범위 안 덩어리마다 한 줄
        run = []
        for p in ln['pts']:
            if inside(p):
                run.append(p)
            elif len(run) >= 2:
                cut.append(dict(ln, pts=run, ctrl=None)); run = []
            else:
                run = []
        if len(run) >= 2:
            cut.append(dict(ln, pts=run, ctrl=None if len(run) != len(ln['pts']) else ln.get('ctrl')))
    g = dsm_sampler(out)
    geo = Geo(CRS)
    pl = []
    for ln in cut:
        pts = ln['pts']
        gz = g([p[0] for p in pts], [p[1] for p in pts])
        if np.isfinite(gz).any():                     # 빈칸은 이웃 값으로
            idx = np.arange(len(gz)); ok = np.isfinite(gz)
            gz = np.interp(idx, idx[ok], gz[ok])
        pl.append(dict(layer=ln['layer'], label=ln['label'], dia=round(ln['dia'], 4), hasZ=bool(ln['hasZ']),
                       pts=[[round(p[0] - E0, 3), round(p[1] - N0, 3), (round(p[2], 3) if (ln['hasZ'] and p[2] is not None) else None)] for p in pts],
                       g=[round(float(v), 3) if np.isfinite(v) else None for v in gz],
                       ll=[[round(v, 8) for v in geo.en_to_ll(p[0], p[1])] for p in pts],
                       ctrl=ln.get('ctrl') or list(range(len(pts)))))
    with open(os.path.join(out, 'data', 'pipes.js'), 'w', encoding='utf-8') as fp:
        fp.write('window.RV_PIPES = ' + json.dumps({'layers': lay, 'lines': pl, 'origin': {'E': E0, 'N': N0},
                                                    'source': os.path.basename(path)}, ensure_ascii=False) + ';\n')
    if depth is not None:
        P['cover'] = float(depth)
        with open(os.path.join(out, 'data', 'mapping.js'), 'w', encoding='utf-8') as fp:
            fp.write('window.RV_MAPPING = ' + json.dumps(P, ensure_ascii=False, indent=1) + ';\n')
    msg = '관로 %d개 → 매핑 범위 안 조각 %d개%s' % (n_raw, len(pl), (' · ' + note) if note else '')
    log(msg)
    return len(pl), msg


# ---------------------------------------------------------------- 차례
class Eta:
    """단계 무게(80장 시험 시간 비율)로 퍼센트 · 남은 초 → «@진행 퍼센트 남은초 글» (app.Job 이 읽음)."""
    W = {'특징점': 2, '맞추기': 2, '카메라 풀기': 26, '왜곡 펴기': 2, '점구름 준비': 0, '점구름': 29, '3D 면': 11, '질감': 5, '결과물': 3}

    def __init__(self, n):
        self.t0 = time.time(); self.done = 0.0; self.total = float(sum(self.W.values())); self.n = n

    def start(self, name):
        el = time.time() - self.t0
        pct = min(99.0, 100.0 * self.done / self.total)
        left = int(el / max(1e-6, self.done / self.total) - el) if self.done > 0 else -1
        print('@진행 %.1f %d %s' % (pct, left, name), flush=True)

    def finish(self, name):
        self.done += self.W.get(name, 1)


def run(cmd, logpath, cwd=None):
    with open(logpath, 'w', encoding='utf-8', errors='replace') as fp:
        r = subprocess.run(cmd, stdout=fp, stderr=subprocess.STDOUT, cwd=cwd, creationflags=NO_WINDOW)
    return r.returncode


def main(argv=None):
    ap = argparse.ArgumentParser(description='경사촬영 사진 → 3D 매핑')
    ap.add_argument('photos', help='사진 폴더')
    ap.add_argument('--out', required=True, help='결과 폴더')
    ap.add_argument('--quality', choices=list(QUALITY), default='normal')
    ap.add_argument('--names', help='쓸 사진 이름 목록 파일 (한 줄에 하나 · 없으면 폴더 전체)')
    ap.add_argument('--cell', type=float, default=0.0, help='정사 · DSM 격자 m (0 = 품질에 맞춰)')
    ap.add_argument('--title', help='제목')
    ap.add_argument('--keep-work', action='store_true', help='중간 자료(점 깊이 · 왜곡 편 사진) 남기기')
    a = ap.parse_args(argv)
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass
    out = os.path.abspath(a.out); ws = os.path.join(out, 'work')
    os.makedirs(ws, exist_ok=True)
    fp = open(os.path.join(out, '매핑_기록.txt'), 'a', encoding='utf-8')
    colmap, mvs = find_engines()
    if not colmap:
        log('엔진 없음 → 내려받기', fp)
        colmap, mvs = install_engines(lambda s: log(s, fp))
        if not colmap:
            log('엔진 설치 실패', fp); return 2
    env_path = os.pathsep.join([os.path.dirname(colmap), os.path.join(os.path.dirname(os.path.dirname(colmap)), 'lib'), mvs])
    os.environ['PATH'] = env_path + os.pathsep + os.environ.get('PATH', '')
    photos = [p for p in scan_folder(a.photos)]
    if a.names:
        want = {l.strip().lower() for l in open(a.names, encoding='utf-8') if l.strip()}
        photos = [p for p in photos if p['name'].lower() in want]
    groups = camera_groups(photos)
    if groups and len(groups) > 1:                  # 카메라마다 내부표정이 달라 한 카메라로 (설계서 4.3 · 5.2)
        (gm, gs, gw, gh), main_ph = groups[0]
        log('  카메라 %s %s %d×%d 사진 %d장만 씀 · 다른 카메라 %d장 뺌 (%s)' % (
            gm or '?', gs or '?', gw, gh, len(main_ph), sum(len(v) for _, v in groups[1:]),
            ' · '.join('%s %s %d×%d %d장' % (k[0] or '?', k[1] or '?', k[2], k[3], len(v)) for k, v in groups[1:])), fp)
        photos = main_ph
    if len(photos) < 3:
        log('매핑 사진 부족 (%d장) → 위치(XMP) 있는 사진 3장 이상' % len(photos), fp); return 2
    names = [p['name'] for p in photos]
    with open(os.path.join(ws, 'names.txt'), 'w', encoding='utf-8') as f2:
        f2.write('\n'.join(names) + '\n')
    cam = camera_params(photos[0])
    res_tex, res_feat = QUALITY[a.quality]
    cell = a.cell or {'fast': 0.1, 'normal': 0.05, 'fine': 0.03}[a.quality]
    log('시작 · 사진 %d장 · 품질 %s · RTK 고정 %d장 · 카메라 %s %s %d×%d' % (
        len(photos), QUALITY_NAME[a.quality], sum(p['rtk'] == 50 for p in photos),
        photos[0].get('model') or '?', photos[0].get('src') or '?', photos[0].get('w', 0), photos[0].get('h', 0)), fp)
    eta = Eta(len(photos))
    T0 = time.time()

    # 끝난 단계는 다시 쓰기 (설계서 원칙 6 · 10.5): 같은 사진 · 품질 · 카메라 값 · 차례 판일 때만
    import hashlib
    ORDER = ['특징점', '맞추기', '카메라 풀기', '왜곡 펴기', '점구름 준비', '점구름', '3D 면', '질감']
    sig = hashlib.md5(json.dumps({'v': PIPELINE_VERSION, 'q': a.quality, 'names': sorted(n.lower() for n in names), 'cam': cam},
                                 ensure_ascii=False).encode('utf-8')).hexdigest()
    stf = os.path.join(ws, 'stages.json')
    try:
        stages = json.load(open(stf, encoding='utf-8'))
    except (OSError, ValueError):
        stages = {}
    if stages.get('sig') != sig:                     # 사진 · 품질이 바뀜 → 처음부터
        for p in ('db.db', 'sparse', 'sparse_txt', 'dense', 'tex_colmap'):
            q = os.path.join(ws, p)
            if os.path.isdir(q):
                shutil.rmtree(q, ignore_errors=True)
            elif os.path.exists(q):
                os.remove(q)
        stages = {'sig': sig, 'done': []}
    for p in ('sparse', 'sparse_txt', 'dense', 'tex_colmap'):
        os.makedirs(os.path.join(ws, p), exist_ok=True)

    def save_stages():
        with open(stf, 'w', encoding='utf-8') as f4:
            json.dump(stages, f4, ensure_ascii=False)

    def step(name, cmd, logname, cwd=None, check=None):
        if name in stages['done'] and (check is None or check()):
            log('%s · 지난번 것 씀' % name, fp)
            eta.finish(name)
            return True
        stages['done'] = [d for d in stages['done'] if ORDER.index(d) < ORDER.index(name)]   # 뒤 단계는 다시
        save_stages()
        eta.start(name)
        t = time.time()
        rc = run(cmd, os.path.join(ws, logname), cwd)
        good = rc == 0 and (check is None or check())
        log('%s %d초%s' % (name, time.time() - t, '' if good else ' · 실패 (종료코드 %d · %s)' % (rc, logname)), fp)
        eta.finish(name)
        if good:
            stages['done'].append(name); save_stages()
        return good

    db = os.path.join(ws, 'db.db')
    dense = os.path.join(ws, 'dense')
    has = lambda *p: os.path.exists(os.path.join(ws, *p))       # noqa: E731
    ok = step('특징점', [colmap, 'feature_extractor', '--database_path', db, '--image_path', a.photos,
                          '--image_list_path', os.path.join(ws, 'names.txt'),
                          '--ImageReader.single_camera', '1', '--ImageReader.camera_model', 'OPENCV']
              + (['--ImageReader.camera_params', cam] if cam else [])
              + ['--FeatureExtraction.max_image_size', str(res_feat), '--FeatureExtraction.use_gpu', '1'], '1_특징점.log',
              check=lambda: has('db.db'))
    ok = ok and step('맞추기', [colmap, 'spatial_matcher', '--database_path', db, '--SpatialMatching.max_num_neighbors', '40',
                                '--SpatialMatching.max_distance', '300', '--FeatureMatching.use_gpu', '1'], '2_맞추기.log')
    ok = ok and step('카메라 풀기', [colmap, 'pose_prior_mapper', '--database_path', db, '--image_path', a.photos,
                                    '--output_path', os.path.join(ws, 'sparse'), '--overwrite_priors_covariance', '1',
                                    '--prior_position_std_x', '0.05', '--prior_position_std_y', '0.05', '--prior_position_std_z', '0.1',
                                    '--use_robust_loss_on_prior_position', '1'], '3_카메라풀기.log',
                     check=lambda: bool(glob.glob(os.path.join(ws, 'sparse', '*', 'images.bin'))))
    models = sorted(glob.glob(os.path.join(ws, 'sparse', '*', 'images.bin')))
    if not ok or not models:
        log('카메라 풀기 실패 → 사진 겹침 · 위치 확인', fp); return 3
    model = os.path.dirname(max(models, key=os.path.getsize))       # 가장 큰 덩어리
    subprocess.run([colmap, 'model_converter', '--input_path', model, '--output_path', os.path.join(ws, 'sparse_txt'),
                    '--output_type', 'TXT'], capture_output=True, creationflags=NO_WINDOW)
    s, R, t, rms, nreg = model_to_ecef(ws)
    log('  등록 %d / %d장 · RTK 와 어긋남 %.3f m · 배율 %.5f' % (nreg, len(photos), rms, s), fp)
    ok = step('왜곡 펴기', [colmap, 'image_undistorter', '--image_path', a.photos, '--input_path', model, '--output_path', dense,
                            '--output_type', 'COLMAP', '--max_image_size', str(res_tex)], '4_왜곡펴기.log',
              check=lambda: has('dense', 'sparse'))
    ok = ok and step('점구름 준비', [os.path.join(mvs, 'InterfaceCOLMAP.exe'), '-i', '.', '-o', 'scene.mvs', '--image-folder', 'images', '-w', '.'],
                     '5a_넘기기.log', cwd=dense, check=lambda: has('dense', 'scene.mvs')) and \
        step('점구름', [os.path.join(mvs, 'DensifyPointCloud.exe'), 'scene.mvs', '-o', 'scene_cpu.mvs', '--resolution-level', '1',
                       '--max-resolution', str(res_tex), '--cuda-device', '-2', '-w', '.'], '5_점구름.log', cwd=dense,
             check=lambda: has('dense', 'scene_cpu.ply'))
    ok = ok and step('3D 면', [os.path.join(mvs, 'ReconstructMesh.exe'), 'scene_cpu.mvs', '-o', 'scene_cpu_mesh.mvs', '--cuda-device', '-2',
                               '--target-face-num', str(TARGET_FACES[a.quality]), '-w', '.'], '6_면.log', cwd=dense,
                     check=lambda: has('dense', 'scene_cpu_mesh.ply'))
    ok = ok and step('질감', [colmap, 'mesh_texturer', '--workspace_path', dense, '--input_path', os.path.join(dense, 'scene_cpu_mesh.ply'),
                             '--output_path', os.path.join(ws, 'tex_colmap')], '7_질감.log', check=lambda: has('tex_colmap', 'mesh.ply'))
    if not ok:
        log('매핑 실패 → work 폴더 기록(*.log) 확인', fp); return 4
    eta.start('결과물')
    t1 = time.time()
    outs = os.path.join(out, '결과물'); os.makedirs(outs, exist_ok=True)
    xyz, rgb = read_openmvs_ply(os.path.join(dense, 'scene_cpu.ply'))
    E, N, H = to_5186_h(s * (xyz.astype(np.float64) @ R.T) + t)
    good = np.isfinite(E) & np.isfinite(H)
    dsm, filled, x0, y1 = ortho_dsm(E[good], N[good], H[good], rgb[good], outs, cell)
    npt, ntri = landxml_from_dsm(dsm, filled, x0, y1, cell, outs, step_m=1.0)
    meta = textured_mesh_to_web(ws, s, R, t, os.path.join(out, 'data'))
    proj = {'version': 1, 'kind': 'mapping', 'title': a.title or Path(out).name, 'crs': CRS, 'height': 'EGM2008',
            'origin': meta['origin'], 'quality': a.quality, 'photos': len(photos), 'registered': nreg, 'rtk_rms': round(rms, 3),
            'cell': cell, 'bbox': [float(E[good].min()), float(N[good].min()), float(E[good].max()), float(N[good].max())],
            'made': time.strftime('%Y-%m-%d %H:%M')}
    with open(os.path.join(out, 'data', 'mapping.js'), 'w', encoding='utf-8') as f3:
        f3.write('window.RV_MAPPING = ' + json.dumps(proj, ensure_ascii=False, indent=1) + ';\n')
    write_info(outs, proj, photos, a, cam, rms, s, npt, ntri, colmap, mvs)
    log('결과물 %d초 · 정사 · DSM (%.2f m) · 지표면 점 %d · 세모 %d · 3D 세모 %d' % (time.time() - t1, cell, npt, ntri, meta['tris']), fp)
    eta.finish('결과물')
    if not a.keep_work:                              # 무거운 중간 자료 지우기 (점 깊이 · 왜곡 편 사진 · 점구름)
        for p in glob.glob(os.path.join(dense, '*.dmap')):
            os.remove(p)
        shutil.rmtree(os.path.join(dense, 'images'), ignore_errors=True)
        shutil.rmtree(os.path.join(dense, 'stereo'), ignore_errors=True)
    copy_viewer(out)
    log('끝 · %d초 · %s' % (time.time() - T0, out), fp)
    print('@진행 100 0 끝', flush=True)
    return 0


def _openmvs_ver(dense):
    """OpenMVS 는 판을 화면이 아니라 실행 폴더의 *.log 첫 줄에 씀."""
    for f in sorted(glob.glob(os.path.join(dense, 'DensifyPointCloud-*.log')), key=os.path.getmtime, reverse=True)[:1]:
        m = re.search(r'OpenMVS x64 v[\d.]+', open(f, encoding='utf-8', errors='replace').read(400))
        if m:
            return m.group(0)
    return 'OpenMVS ?'


def write_info(outs, proj, photos, a, cam, rms, scale, npt, ntri, colmap, mvs):
    """결과물/정보.txt — 좌표계 · 수직 기준 · 지오이드 · 엔진 판 · 처리 옵션 · 정확도 (설계서 6.5 · 10.3: 산출물마다 기준을 남김)."""
    def ver(cmd):
        try:
            r = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=30, creationflags=NO_WINDOW)
            m = re.search(r'(COLMAP [\d.]+|OpenMVS x64 v[\d.]+)', r.stdout + r.stderr)
            return m.group(1) if m else '?'
        except Exception:
            return '?'
    rtk = sum(p['rtk'] == 50 for p in photos)
    lines = [
        '3D 매핑 결과물 정보 · %s' % proj['made'],
        '',
        '[좌표]',
        '수평: EPSG:5186 Korea 2000 / Central Belt 2010 (정사영상 · DSM 의 .prj · .tfw)',
        '수직: 표고 = 타원체고 − 지오이드 (EGM2008 · PROJ). KNGeoid18(국토지리정보원) 과는 이 지역에서 보통 0.1~0.3 m 다를 수 있음',
        '원점(3D 보기 자료): E %.1f · N %.1f · H %.1f' % tuple(proj['origin']),
        '범위: E %.1f ~ %.1f · N %.1f ~ %.1f' % (proj['bbox'][0], proj['bbox'][2], proj['bbox'][1], proj['bbox'][3]),
        '',
        '[정확도]',
        '사진 %d장 중 등록 %d장 · RTK 고정해 %d장' % (proj['photos'], proj['registered'], rtk),
        '카메라 위치 ↔ RTK 위치 어긋남 RMS %.3f m · 배율 %.5f (GCP 없이 RTK 로만 맞춤 · 검사점 검증은 따로 해야 함)' % (rms, scale),
        '',
        '[결과물]',
        '정사영상.tif  RGBA · %.2f m 격자 · 투명 = 자료 없음' % proj['cell'],
        'DSM.tif       표고(m) float32 · %.2f m 격자 · -9999 = 자료 없음 · 건물 · 나무 포함한 표면 모델 (DTM 아님)' % proj['cell'],
        '지표면_DSM.xml  LandXML 세모 지표면 (DSM 1 m 솎음 · 점 %d · 세모 %d) — Civil 3D 지표면 가져오기용 · 표면 모델' % (npt, ntri),
        '',
        '[처리]',
        '품질 %s · 질감 사진 %d px · 특징점 사진 %d px · 카메라 %s %s %d×%d · 처음 내부표정 %s' % (
            QUALITY_NAME[a.quality], QUALITY[a.quality][0], QUALITY[a.quality][1], photos[0].get('model') or '?', photos[0].get('src') or '?',
            photos[0].get('w', 0), photos[0].get('h', 0), cam or 'EXIF'),
        '엔진: %s · %s (점구름 · 면) · 차례 판 %s' % (ver([colmap, '--version']), _openmvs_ver(os.path.join(os.path.dirname(outs), 'work', 'dense')), PIPELINE_VERSION),
        '사진 폴더: %s' % os.path.abspath(a.photos),
    ]
    with open(os.path.join(outs, '정보.txt'), 'w', encoding='utf-8') as fp:
        fp.write('\n'.join(lines) + '\n')


def copy_viewer(out):
    """3D 보기 화면 (viewer/map3d.html → index.html · map3d.js) 을 결과 폴더에."""
    base = Path(getattr(sys, '_MEIPASS', '')) / 'viewer' if getattr(sys, 'frozen', False) else Path(__file__).resolve().parent.parent / 'viewer'
    os.makedirs(os.path.join(out, 'viewer'), exist_ok=True)
    shutil.copy2(base / 'map3d.html', os.path.join(out, 'index.html'))
    shutil.copy2(base / 'map3d.js', os.path.join(out, 'viewer', 'map3d.js'))


if __name__ == '__main__':
    sys.exit(main())
