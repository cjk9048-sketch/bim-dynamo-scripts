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
PIPELINE_VERSION = '4'      # 2 = 3D 면 세모 한도 · 3 = 사진 많으면 전역 카메라 풀기 · 4 = 조각 질감      # 차례 · 옵션을 바꾸면 올림 → 지난번 중간 자료를 다시 쓰지 않음
# 품질: 질감용 사진 크기 · 특징점 사진 크기 (점구름은 질감용의 절반 · normal = 1125 80장 시험 값)
QUALITY = {'fast': (1320, 2000), 'normal': (2640, 3200), 'fine': (5280, 4000)}
QUALITY_NAME = {'fast': '빠르게', 'normal': '보통', 'fine': '정밀'}
# 3D 보기 면 세모 수 한도 (보기 전용 · 정사 · DSM · 지표면은 점구름 그대로라 정확도는 안 바뀜 — 설계서 10.4)
TARGET_FACES = {'fast': 1000000, 'normal': 3000000, 'fine': 6000000}
DTM_CELL = 0.5             # DTM 격자 m
MAX_TEX = 8192             # 조각 질감 그림 한 변 최대 (보기 전용 · 대부분 그래픽 16384 · 시험 렌더러 8192)
GLOBAL_MIN = 50            # 이 장수 이상이면 전역 카메라 풀기 (1125: 394장 26분 → 10분 · 80장 209 → 53초 · RTK 어긋남 같음)
ENGINE_DIRS = [Path(os.environ.get('LOCALAPPDATA', str(Path.home()))) / 'DH.Roadview' / 'engines', Path(r'D:\매핑시험\engines')]
if os.environ.get('RV_ENGINE_DIRS'):          # 시험 · 다른 PC 흉내 (처음 내려받기) — ; 로 나눈 폴더들
    ENGINE_DIRS = [Path(x) for x in os.environ['RV_ENGINE_DIRS'].split(';') if x]
# 좌표계 고르기 (설계서 4.2 · 6.5) — 기본 = 중부원점 (회사 도면 · 그레이딩 · 로드뷰와 같게)
CRS_CHOICES = {'EPSG:5186': '중부원점 (Korea 2000 / Central Belt 2010)', 'EPSG:5185': '서부원점 (West Belt 2010)',
               'EPSG:5187': '동부원점 (East Belt 2010)', 'EPSG:5188': '동해(울릉)원점 (East Sea Belt 2010)', 'EPSG:5179': 'UTM-K (Unified CS)'}


def set_crs(crs):
    global CRS
    if crs not in CRS_CHOICES:
        raise ValueError('좌표계 %s 는 고를 수 없음 (%s)' % (crs, ', '.join(CRS_CHOICES)))
    CRS = crs
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
        key = {'colmap': 'colmap.exe', 'openmvs': 'DensifyPointCloud.exe'}[name]
        done = (tgt / 'bin' / key) if name == 'colmap' else (tgt / key)
        if done.exists():                          # 실행 파일이 있어야 다 된 것 (풀다 끊긴 폴더는 다시 · 1125 검토)
            continue
        z = dst / (name + '.zip')
        tmp = dst / (name + '.tmp')
        shutil.rmtree(tmp, ignore_errors=True)
        progress('엔진 내려받기 · %s' % name)
        with urllib.request.urlopen(url, timeout=60) as r, open(z, 'wb') as fp:
            shutil.copyfileobj(r, fp, 1 << 20)
        progress('엔진 풀기 · %s' % name)
        with zipfile.ZipFile(z) as zf:
            zf.extractall(tmp)
        z.unlink()
        # 실행 파일이 든 폴더를 끌어올림 (OpenMVS zip = vc17\x64\Release\*.exe · COLMAP zip = bin\colmap.exe 가 바로)
        hit = next(iter(tmp.rglob(key)), None)
        if hit is None:
            raise RuntimeError('엔진 zip 안에 %s 없음' % key)
        src = hit.parent.parent if name == 'colmap' else hit.parent     # colmap 은 bin 의 위
        shutil.rmtree(tgt, ignore_errors=True)
        shutil.move(str(src), str(tgt))            # 다 푼 뒤 한 번에 제자리로
        shutil.rmtree(tmp, ignore_errors=True)
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
        p = l.split(None, 9)                       # 사진 이름에 빈칸이 있어도 (예: «DJI_0001 - 복사본.JPG»)
        if len(p) >= 10:
            cams[p[9].rstrip('\r\n')] = -_qrot(*map(float, p[1:5])).T @ np.array(list(map(float, p[5:8])))
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
    with open(os.path.join(out, '지표면_%s.xml' % name), 'w', encoding='utf-8') as fp:
        fp.write('\n'.join(lines))
    return int(ok.sum()), int(m1.sum() + m2.sum())


# ---------------------------------------------------------------- DTM (땅 분류 · 설계서 6.1)
def fill_pull_push(Z, mask):
    """빈칸(mask False) 메우기 — 거친 단계로 평균 내려가며(pull) 다시 올라오며 채움(push). 넓은 빈칸도 부드럽게."""
    import cv2
    if not mask.any():
        return Z.astype(np.float32)
    levels = [(np.where(mask, Z, 0).astype(np.float32), mask.astype(np.float32))]
    # 1×1 까지 줄여야 맨 위 칸 = 전체 평균 (2×N 에서 멈추면 빈 칸이 NaN 으로 남아 번짐 — 1125 검토: 비스듬한 회랑에서 나무 23 % 가 땅으로)
    while max(levels[-1][0].shape) > 1:
        v, w = levels[-1]
        h2, w2 = (v.shape[0] + 1) // 2, (v.shape[1] + 1) // 2
        vp = np.zeros((h2 * 2, w2 * 2), np.float32); wp = np.zeros_like(vp)
        vp[:v.shape[0], :v.shape[1]] = v; wp[:w.shape[0], :w.shape[1]] = w
        vs = vp.reshape(h2, 2, w2, 2).sum((1, 3)); ws_ = wp.reshape(h2, 2, w2, 2).sum((1, 3))
        levels.append((vs, ws_))
    est = None
    for v, w in reversed(levels):
        cur = np.where(w > 0, v / np.maximum(w, 1e-9), np.nan).astype(np.float32)
        if est is not None:                      # 정확히 2배로 키운 뒤 잘라 맞춤 (홀수 크기에서 반 칸 밀림 막기)
            up = cv2.resize(est, (est.shape[1] * 2, est.shape[0] * 2), interpolation=cv2.INTER_LINEAR)[:cur.shape[0], :cur.shape[1]]
            cur = np.where(np.isfinite(cur), cur, up)
        est = cur
    return np.where(mask, Z, est).astype(np.float32)


def dtm_from_points(E, N, H, cell=0.5, max_win_m=33.0, slope=0.25, dh0=0.3, dhmax=2.5):
    """점진형 형태학 필터(PMF · Zhang 2003): 칸마다 가장 낮은 점 → 창을 키우며 열림(opening) → 창 크기에 맞는 높이 차보다
    솟은 칸 = 땅 아님(건물 · 비닐하우스 · 나무). 땅 칸만 남겨 빈칸을 메운 것 = DTM. → (dtm, 땅, 자료 있음, x0, y1)."""
    import cv2
    x0 = math.floor(E.min() / cell) * cell; y1 = math.ceil(N.max() / cell) * cell
    W = int(math.ceil((E.max() - x0) / cell)) + 1; Hh = int(math.ceil((y1 - N.min()) / cell)) + 1
    idx = ((y1 - N) / cell).astype(np.int64) * W + ((E - x0) / cell).astype(np.int64)
    order = np.lexsort((H, idx))
    first = order[np.r_[True, idx[order][1:] != idx[order][:-1]]]
    zmin = np.full(W * Hh, np.nan, np.float32); zmin[idx[first]] = H[first]
    zmin = zmin.reshape(Hh, W)
    valid = np.isfinite(zmin)
    # 땅 밑 잡음 점(점구름 잡음)이 «땅» 으로 잡히지 않게: 이웃 가운데값보다 0.5 m 넘게 낮은 칸은 뺌 (1125 394장: DEM 보다 2 m 낮은 곳)
    Zf = fill_pull_push(np.nan_to_num(zmin), valid)
    med = cv2.medianBlur(Zf, 5)
    valid &= ~(zmin < med - 0.5)
    Z0 = fill_pull_push(np.nan_to_num(zmin), valid)
    surf = Z0.copy(); flag = np.zeros(Z0.shape, bool); w_prev = 1
    k = 1
    while True:
        w = 2 * (2 ** k) + 1                                   # 5, 9, 17, 33, 65 칸 (0.5 m 격자 = 최대 32.5 m)
        if w * cell > max_win_m + cell:                        # 넓은 성토 도로 · 둑(> 창)이 깎이지 않게 창 상한 (1125 검토)
            break
        opened = cv2.morphologyEx(surf, cv2.MORPH_OPEN, np.ones((w, w), np.uint8))
        dh = min(dhmax, dh0 + slope * (w - w_prev) * cell)
        flag |= (surf - opened) > dh
        surf = opened; w_prev = w; k += 1
    ground = valid & ~flag
    dtm = fill_pull_push(np.nan_to_num(Z0), ground)
    # 땅이 안 보인 곳(숲 · 건물 밑)은 둘레 땅에 맞춘 막 면(조화 보간)으로 매끄럽게 — 땅 칸은 고정 (1125: 메운 곳이 네모 덩어리)
    hole = ~ground
    if hole.any():
        for _ in range(400):
            sm = cv2.blur(dtm, (3, 3))
            dtm[hole] = sm[hole]
    cover = cv2.dilate(np.isfinite(zmin).astype(np.uint8), np.ones((5, 5), np.uint8)) > 0   # 자료 끝 밖은 비움
    return dtm, ground, cover, x0, y1


def write_raster(out, name, Z, mask, x0, y1, cell):
    from PIL import Image
    Image.fromarray(np.where(mask, Z, -9999).astype(np.float32), 'F').save(os.path.join(out, name + '.tif'))
    _world(out, name, x0, y1, cell)


def grid_sampler(Z, mask, x0, y1, cell):
    def f(E, N):
        E = np.asarray(E, float); N = np.asarray(N, float)
        c = np.floor((E - x0) / cell).astype(int); r = np.floor((y1 - N) / cell).astype(int)
        ok = (r >= 0) & (r < Z.shape[0]) & (c >= 0) & (c < Z.shape[1])
        z = np.full(E.shape, np.nan)
        z[ok] = np.where(mask[r[ok], c[ok]], Z[r[ok], c[ok]], np.nan)
        return z
    return f


def compare_company_dem(E, N, Z, log=print):
    """사내 수치지도 DEM(등고선 · 표고점 5 m)과 견줌 → (가운데값, 사분위 25, 75, 곳 수) 또는 None (접속 없음)."""
    try:
        from . import terrain
        if not terrain.db_available():
            return None
        bbox = (np.min(E) - 200, np.min(N) - 200, np.max(E) + 200, np.max(N) + 200)
        cl = terrain.contours_from_db(bbox, int(CRS.split(':')[1])); sp = terrain.spot_heights_from_db(bbox, int(CRS.split(':')[1]))
        P = terrain.lines_to_points(cl, 2.0)
        if len(sp):
            P = np.vstack([P, sp])
        if len(P) < 3:
            return None
        dem = terrain.GridDem(P, cell=5.0, sigma=15.0)
        d = np.asarray(Z) - np.asarray(dem(E, N))
        d = d[np.isfinite(d)]
        if not len(d):
            return None
        return float(np.median(d)), float(np.percentile(d, 25)), float(np.percentile(d, 75)), int(len(d))
    except Exception as ex:                       # noqa: BLE001
        log('  사내 DEM 견줌 못 함: %s' % str(ex)[:120])
        return None


# ---------------------------------------------------------------- 조각 질감 (그래픽 한도 16384 안으로 · 조각마다 질감)
TILE_FACES = 400000


def split_mesh_tiles(mesh_ply, out_dir, max_faces=TILE_FACES):
    """OpenMVS 면 PLY (xyz float32 · 면 uint8 3 + uint32 ×3) → 세모 가운데 기준 격자 조각 PLY 들 → [(경로, 세모 수)]."""
    b = open(mesh_ply, 'rb').read()
    i = b.find(b'end_header'); h = b.index(b'\n', i) + 1
    head = [l.strip() for l in b[:h].decode('latin1').splitlines()]
    nv = int([l for l in head if l.startswith('element vertex')][0].split()[2])
    nf = int([l for l in head if l.startswith('element face')][0].split()[2])
    V = np.frombuffer(b, np.float32, nv * 3, h).reshape(nv, 3)
    Fb = np.frombuffer(b, np.uint8, nf * 13, h + nv * 12).reshape(nf, 13)
    if not (Fb[:, 0] == 3).all():
        raise RuntimeError('면 형식이 다름 (세모만)')
    F = Fb[:, 1:13].copy().view(np.uint32).reshape(nf, 3)
    C = V[F].mean(1)
    k = max(1, int(math.ceil(nf / max_faces)))
    w = float(C[:, 0].max() - C[:, 0].min()) or 1.0; hh = float(C[:, 1].max() - C[:, 1].min()) or 1.0
    nx = max(1, int(round(math.sqrt(k * w / hh)))); ny = max(1, int(math.ceil(k / nx)))
    ix = np.minimum(nx - 1, ((C[:, 0] - C[:, 0].min()) / w * nx).astype(int))
    iy = np.minimum(ny - 1, ((C[:, 1] - C[:, 1].min()) / hh * ny).astype(int))
    tid = iy * nx + ix
    os.makedirs(out_dir, exist_ok=True)
    tiles = []
    for t in range(nx * ny):
        sel = np.nonzero(tid == t)[0]
        if not len(sel):                               # 작은 조각도 버리지 않음 (3D 보기 구멍 · 면 전체가 작을 때 0 조각 막기)
            continue
        f = F[sel]
        u, inv = np.unique(f.reshape(-1), return_inverse=True)
        p = os.path.join(out_dir, 't%02d.ply' % len(tiles))
        with open(p, 'wb') as fp:
            fp.write(('ply\nformat binary_little_endian 1.0\nelement vertex %d\nproperty float x\nproperty float y\nproperty float z\n'
                      'element face %d\nproperty list uchar uint vertex_indices\nend_header\n' % (len(u), len(sel))).encode('ascii'))
            fp.write(V[u].astype(np.float32).tobytes())
            rec = np.empty((len(sel), 13), np.uint8); rec[:, 0] = 3
            rec[:, 1:13] = inv.reshape(-1, 3).astype(np.uint32).view(np.uint8).reshape(-1, 12)
            fp.write(rec.tobytes())
        tiles.append((p, int(len(sel))))
    return tiles


def read_textured_ply(td):
    """COLMAP mesh_texturer 결과 → (꼭짓점 float64 n×3, 면 번호 m×3, 질감 좌표 m×3×2 (그림 위 = 0), 질감 그림 경로)."""
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
    return V, fi, tc, os.path.join(td, 'texture.png')


def tiles_to_web(tex_dirs, s, R, t, out, max_tex=8192):
    """조각마다 질감 면 → data/tiles/tNN.bin (위치 float32×3 · 질감 float32×2, 세모마다 셋) + tNN.jpg · data/mesh.json.
    질감 그림이 max_tex 보다 크면 줄여 저장 (보기 전용 · 대부분 그래픽 한도 16384 · 시험용 8192)."""
    from PIL import Image
    Image.MAX_IMAGE_PIXELS = None
    parts = [read_textured_ply(d) for d in tex_dirs]
    allE = []
    conv = []
    for V, fi, tc, tex in parts:
        E, N, H = to_5186_h(s * (V @ R.T) + t)
        conv.append((E, N, H)); allE.append((np.median(E), np.median(N), np.percentile(H, 5), len(E)))
    w = np.array([a[3] for a in allE], float)
    o = [round(float(np.average([a[0] for a in allE], weights=w)), 1), round(float(np.average([a[1] for a in allE], weights=w)), 1),
         round(float(min(a[2] for a in allE)), 1)]
    tdir = os.path.join(out, 'tiles'); os.makedirs(tdir, exist_ok=True)
    for f in glob.glob(os.path.join(tdir, '*')):
        os.remove(f)
    tiles = []; mn = np.full(3, np.inf); mx = np.full(3, -np.inf); tris = 0
    for k, ((V, fi, tc, tex), (E, N, H)) in enumerate(zip(parts, conv)):
        vpos = np.stack([E - o[0], N - o[1], H - o[2]], 1).astype(np.float32)
        pos = vpos[fi.reshape(-1)]
        with open(os.path.join(tdir, 't%02d.bin' % k), 'wb') as fp:
            fp.write(pos.tobytes()); fp.write(tc.reshape(-1, 2).astype(np.float32).tobytes())
        im = Image.open(tex).convert('RGB')
        if max(im.size) > max_tex:
            r = max_tex / max(im.size); im = im.resize((max(1, int(im.size[0] * r)), max(1, int(im.size[1] * r))), Image.LANCZOS)
        im.save(os.path.join(tdir, 't%02d.jpg' % k), quality=90)
        tiles.append({'bin': 'tiles/t%02d.bin' % k, 'tex': 'tiles/t%02d.jpg' % k, 'verts': int(len(pos)), 'tris': int(len(fi)), 'texsize': list(im.size)})
        mn = np.minimum(mn, pos.min(0)); mx = np.maximum(mx, pos.max(0)); tris += len(fi)
    for f in ('mesh.bin', 'tex.jpg'):                   # 옛 한 덩어리 자료는 지움
        if os.path.exists(os.path.join(out, f)):
            os.remove(os.path.join(out, f))
    meta = {'crs': CRS, 'height': 'EGM2008', 'origin': o, 'tris': int(tris), 'tiles': tiles, 'min': mn.tolist(), 'max': mx.tolist()}
    with open(os.path.join(out, 'mesh.json'), 'w') as fp:
        json.dump(meta, fp, indent=1)
    return meta


# ---------------------------------------------------------------- 검사점 정확도 (설계서 6.4 · GCP 와 검사점은 따로)
def check_accuracy(out, items):
    """items = [{name, E, N, Z, mE, mN, mZ}] (m* = 3D 보기에서 찍은 자리 · 없으면 None) → 결과물/정확도_검사점.csv · .txt.
    표고는 DSM · DTM 에서도 읽어 같이 적음. → 요약 dict."""
    P = _read_js(os.path.join(out, 'data', 'mapping.js'), 'RV_MAPPING')
    d = os.path.join(out, '결과물')
    samp = {}
    for nm in ('DSM', 'DTM'):
        if os.path.exists(os.path.join(d, nm + '.tif')):
            samp[nm] = tif_sampler(d, nm)
    rows = []
    for it in items:
        E, N, Z = float(it['E']), float(it['N']), (float(it['Z']) if it.get('Z') not in (None, '') else None)
        r = {'name': it.get('name', ''), 'E': E, 'N': N, 'Z': Z}
        if it.get('mE') is not None:
            r.update(dE=float(it['mE']) - E, dN=float(it['mN']) - N, dZ=(float(it['mZ']) - Z) if Z is not None else None)
        for nm, f in samp.items():
            z = float(f([E], [N])[0])
            r['dZ_' + nm] = (z - Z) if (Z is not None and np.isfinite(z)) else None
        rows.append(r)

    def rmse(vals):
        v = [x for x in vals if x is not None]
        return (math.sqrt(sum(x * x for x in v) / len(v)), len(v)) if v else (None, 0)
    hz = rmse([math.hypot(r['dE'], r['dN']) if 'dE' in r else None for r in rows])
    vz = rmse([r.get('dZ') for r in rows])
    vs = {nm: rmse([r.get('dZ_' + nm) for r in rows]) for nm in samp}
    import csv
    with open(os.path.join(d, '정확도_검사점.csv'), 'w', newline='', encoding='utf-8-sig') as fp:
        w = csv.writer(fp)
        w.writerow(['이름', 'X(동)', 'Y(북)', '표고', 'dX', 'dY', 'd평면', 'dZ(찍은 곳)'] + ['dZ(%s)' % nm for nm in samp])
        f3 = lambda v: '' if v is None else '%.3f' % v      # noqa: E731
        for r in rows:
            w.writerow([r['name'], '%.3f' % r['E'], '%.3f' % r['N'], f3(r['Z']), f3(r.get('dE')), f3(r.get('dN')),
                        f3(math.hypot(r['dE'], r['dN']) if 'dE' in r else None), f3(r.get('dZ'))] + [f3(r.get('dZ_' + nm)) for nm in samp])
    f2 = lambda t: '-' if t[0] is None else '%.3f m (%d점)' % t      # noqa: E731
    lines = ['검사점 정확도 · %s · %s' % (P.get('title', ''), time.strftime('%Y-%m-%d %H:%M')), '',
             '좌표계 %s · 표고 %s · 검사점 %d개' % (P.get('crs', CRS), P.get('height', 'EGM2008'), len(rows)),
             '평면 RMSE (3D 보기에서 찍은 곳): %s' % f2(hz), '표고 RMSE (찍은 곳): %s' % f2(vz)]
    lines += ['표고 RMSE (%s 에서 읽음): %s' % (nm, f2(v)) for nm, v in vs.items()]
    lines += ['', '검사점은 매핑에 쓰지 않은 독립 점이어야 함 (GCP 잔차만으로 절대 정확도를 판단하지 않음 · 설계서 6.4)',
              'DSM 은 건물 · 나무 포함 표면이라 땅 위 검사점은 DTM 값을 볼 것']
    with open(os.path.join(d, '정확도_검사점.txt'), 'w', encoding='utf-8') as fp:
        fp.write('\n'.join(lines) + '\n')
    return {'n': len(rows), 'h': hz[0], 'v': vz[0], 'v_grid': {nm: v[0] for nm, v in vs.items()}, 'csv': os.path.join(d, '정확도_검사점.csv')}


def tif_sampler(d, name):
    from PIL import Image
    Image.MAX_IMAGE_PIXELS = None
    A = np.asarray(Image.open(os.path.join(d, name + '.tif')), np.float32)
    w = [float(x) for x in open(os.path.join(d, name + '.tfw')).read().split()]
    cell, xc, yc = w[0], w[4], w[5]

    def f(E, N):
        E = np.asarray(E, float); N = np.asarray(N, float)
        c = np.round((E - xc) / cell).astype(int); r = np.round((yc - N) / cell).astype(int)
        ok = (r >= 0) & (r < A.shape[0]) & (c >= 0) & (c < A.shape[1])
        z = np.full(E.shape, np.nan); z[ok] = A[r[ok], c[ok]]; z[z <= -9998] = np.nan
        return z
    return f


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
    set_crs(P.get('crs', 'EPSG:5186'))
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


_MVS_CUDA = {}


def mvs_cuda_opt(mvs):
    """['--cuda-device', '-2'] (CPU 로) · 이 옵션이 없는 판(일반 zip · CUDA 없는 판)이면 [] —
    없는 옵션을 주면 바로 종료코드 1 (1125 다른 PC 흉내 시험에서 잼)."""
    if mvs not in _MVS_CUDA:
        import tempfile
        d = tempfile.mkdtemp(prefix='rv_mvs_')
        try:
            r = subprocess.run([os.path.join(mvs, 'DensifyPointCloud.exe'), '--help'], cwd=d, capture_output=True, timeout=60,
                               creationflags=NO_WINDOW, text=True, encoding='utf-8', errors='replace')
            txt = (r.stdout or '') + (r.stderr or '') + ''.join(open(f, encoding='utf-8', errors='replace').read()
                                                              for f in glob.glob(os.path.join(d, '*.log')))
            _MVS_CUDA[mvs] = ['--cuda-device', '-2'] if 'cuda-device' in txt else []
        except Exception:                          # noqa: BLE001
            _MVS_CUDA[mvs] = []
        finally:
            shutil.rmtree(d, ignore_errors=True)
    return _MVS_CUDA[mvs]


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
    ap.add_argument('--crs', default='EPSG:5186', choices=list(CRS_CHOICES), help='결과 좌표계 (기본 중부원점)')
    ap.add_argument('--mapper', choices=['auto', 'global', 'prior'], default='auto',
                    help='카메라 풀기: auto = %d장 이상이면 전역 · global · prior(한 장씩)' % GLOBAL_MIN)
    a = ap.parse_args(argv)
    set_crs(a.crs)
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
    sigd = {'v': PIPELINE_VERSION, 'q': a.quality, 'names': sorted(n.lower() for n in names), 'cam': cam}
    if a.mapper != 'auto':                       # 카메라 풀기 방식을 굳이 고르면 서명에 (auto 는 옛 서명 그대로)
        sigd['mapper'] = a.mapper
    sig = hashlib.md5(json.dumps(sigd, ensure_ascii=False).encode('utf-8')).hexdigest()
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
    feat = lambda g: ([colmap, 'feature_extractor', '--database_path', db, '--image_path', a.photos,        # noqa: E731
                       '--image_list_path', os.path.join(ws, 'names.txt'),
                       '--ImageReader.single_camera', '1', '--ImageReader.camera_model', 'OPENCV']
                      + (['--ImageReader.camera_params', cam] if cam else [])
                      + ['--FeatureExtraction.max_image_size', str(res_feat), '--FeatureExtraction.use_gpu', g])
    match = lambda g: [colmap, 'spatial_matcher', '--database_path', db, '--SpatialMatching.max_num_neighbors', '40',   # noqa: E731
                       '--SpatialMatching.max_distance', '300', '--FeatureMatching.use_gpu', g]
    ok = step('특징점', feat('1'), '1_특징점.log', check=lambda: has('db.db'))
    if not ok:                                   # NVIDIA 그래픽카드가 없거나 CUDA 가 안 되는 PC → CPU 로 다시
        log('  그래픽카드로 안 됨 → CPU 로 다시', fp)
        if os.path.exists(db):
            os.remove(db)
        ok = step('특징점', feat('0'), '1_특징점_cpu.log', check=lambda: has('db.db'))
    ok2 = ok and step('맞추기', match('1'), '2_맞추기.log')
    if ok and not ok2:
        log('  그래픽카드로 안 됨 → CPU 로 다시', fp)
        ok2 = step('맞추기', match('0'), '2_맞추기_cpu.log')
    ok = ok2
    # 카메라 풀기: 사진이 많으면 전역(global_mapper · 394장 26분 → 10분 · RTK 어긋남 같음) → RTK 위치로 동 · 북 · 위(m) 맞춤
    #   (전역 결과는 배율 · 방향이 제멋대로라 뒤 단계가 미터 틀에서 돌게) · 실패하거나 90 % 못 붙이면 한 장씩(pose_prior_mapper)
    use_global = a.mapper == 'global' or (a.mapper == 'auto' and len(photos) >= GLOBAL_MIN)
    prior_cmd = [colmap, 'pose_prior_mapper', '--database_path', db, '--image_path', a.photos,
                 '--output_path', os.path.join(ws, 'sparse'), '--overwrite_priors_covariance', '1',
                 '--prior_position_std_x', '0.05', '--prior_position_std_y', '0.05', '--prior_position_std_z', '0.1',
                 '--use_robust_loss_on_prior_position', '1']
    sparse_ok = lambda: bool(glob.glob(os.path.join(ws, 'sparse', '*', 'images.bin')))     # noqa: E731
    if ok and use_global:
        raw = os.path.join(ws, 'sparse_global')
        aligned = os.path.join(ws, 'sparse', '0')
        if '카메라 풀기' in stages['done'] and sparse_ok():
            log('카메라 풀기 · 지난번 것 씀', fp); eta.finish('카메라 풀기'); ok = True
        else:
            stages['done'] = [d for d in stages['done'] if ORDER.index(d) < ORDER.index('카메라 풀기')]; save_stages()
            shutil.rmtree(raw, ignore_errors=True); os.makedirs(raw, exist_ok=True)
            eta.start('카메라 풀기'); t = time.time()
            rc = run([colmap, 'global_mapper', '--database_path', db, '--image_path', a.photos, '--output_path', raw],
                     os.path.join(ws, '3_카메라풀기_전역.log'))
            gm = sorted(glob.glob(os.path.join(raw, '*', 'images.bin')))
            rc2 = 1
            if rc == 0 and gm:
                shutil.rmtree(os.path.join(ws, 'sparse'), ignore_errors=True); os.makedirs(aligned, exist_ok=True)
                rc2 = run([colmap, 'model_aligner', '--input_path', os.path.dirname(max(gm, key=os.path.getsize)), '--output_path', aligned,
                           '--database_path', db, '--ref_is_gps', '1', '--alignment_type', 'enu', '--alignment_max_error', '3'],
                          os.path.join(ws, '3b_미터맞춤.log'))
            nreg_g = 0
            if rc2 == 0 and sparse_ok():
                r = subprocess.run([colmap, 'model_analyzer', '--path', aligned], capture_output=True, text=True,
                                   encoding='utf-8', errors='replace', creationflags=NO_WINDOW)
                m = re.search(r'Registered images:\s*(\d+)', r.stdout + r.stderr)
                nreg_g = int(m.group(1)) if m else 0
            log('카메라 풀기 (전역) %d초 · 등록 %d / %d장' % (time.time() - t, nreg_g, len(photos)), fp)
            if nreg_g >= 0.9 * len(photos):
                ok = True
            else:
                log('  전역으로 부족 → 한 장씩 풀기로 다시', fp)
                shutil.rmtree(os.path.join(ws, 'sparse'), ignore_errors=True); os.makedirs(os.path.join(ws, 'sparse'), exist_ok=True)
                t = time.time()
                ok = run(prior_cmd, os.path.join(ws, '3_카메라풀기.log')) == 0 and sparse_ok()
                log('카메라 풀기 (한 장씩) %d초%s' % (time.time() - t, '' if ok else ' · 실패'), fp)
            eta.finish('카메라 풀기')
            if ok:
                stages['done'].append('카메라 풀기'); save_stages()
    else:
        if ok and not ('카메라 풀기' in stages['done'] and sparse_ok()):     # 남은 옛 덩어리(sparse/N)가 섞이지 않게
            shutil.rmtree(os.path.join(ws, 'sparse'), ignore_errors=True); os.makedirs(os.path.join(ws, 'sparse'), exist_ok=True)
        ok = ok and step('카메라 풀기', prior_cmd, '3_카메라풀기.log', check=sparse_ok)
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
                       '--max-resolution', str(res_tex)] + mvs_cuda_opt(mvs) + ['-w', '.'], '5_점구름.log', cwd=dense,
             check=lambda: has('dense', 'scene_cpu.ply'))
    ok = ok and step('3D 면', [os.path.join(mvs, 'ReconstructMesh.exe'), 'scene_cpu.mvs', '-o', 'scene_cpu_mesh.mvs'] + mvs_cuda_opt(mvs) +
                     ['--target-face-num', str(TARGET_FACES[a.quality]), '-w', '.'], '6_면.log', cwd=dense,
                     check=lambda: has('dense', 'scene_cpu_mesh.ply'))
    # 질감 = 조각마다 (한 장이면 32768 폭까지 커져 그래픽 한도 16384 로 줄어 흐려짐 · 1125 394장)
    tex_root = os.path.join(ws, 'tex_tiles'); tl_json = os.path.join(tex_root, 'tiles.json')

    def tex_list():                              # tiles.json = 조각 폴더 이름만 (결과 폴더를 옮겨도 · 1125 검토)
        try:
            L = [os.path.join(tex_root, os.path.basename(str(d).rstrip('/\\'))) for d in json.load(open(tl_json, encoding='utf-8'))]
            return L if L and all(os.path.exists(os.path.join(d, 'mesh.ply')) for d in L) else None
        except (OSError, ValueError):
            return None
    tex_dirs = []
    if ok and '질감' in stages['done'] and tex_list():
        log('질감 · 지난번 것 씀', fp); eta.finish('질감'); tex_dirs = tex_list()
    elif ok:
        stages['done'] = [d for d in stages['done'] if ORDER.index(d) < ORDER.index('질감')]; save_stages()
        eta.start('질감'); t2 = time.time()
        if not (os.path.isdir(os.path.join(dense, 'images')) and os.listdir(os.path.join(dense, 'images'))):
            # 지난번 끝에서 지운 왜곡 편 사진만 다시 (점구름 · 면은 그대로 · 뒤 단계 안 지움)
            log('  질감용 사진 다시 펴기', fp)
            run([colmap, 'image_undistorter', '--image_path', a.photos, '--input_path', model, '--output_path', dense,
                 '--output_type', 'COLMAP', '--max_image_size', str(res_tex)], os.path.join(ws, '4b_왜곡펴기_질감.log'))
        shutil.rmtree(tex_root, ignore_errors=True); os.makedirs(tex_root, exist_ok=True)
        tiles = split_mesh_tiles(os.path.join(dense, 'scene_cpu_mesh.ply'), os.path.join(ws, 'mesh_tiles'))
        for k, (p, nf) in enumerate(tiles):
            d = os.path.join(tex_root, 't%02d' % k); os.makedirs(d, exist_ok=True)
            rc = run([colmap, 'mesh_texturer', '--workspace_path', dense, '--input_path', p, '--output_path', d], os.path.join(ws, '7_질감_t%02d.log' % k))
            if rc != 0 or not os.path.exists(os.path.join(d, 'mesh.ply')):
                ok = False; log('  조각 %d 질감 실패 (7_질감_t%02d.log)' % (k, k), fp); break
            tex_dirs.append(d)
        if ok:
            json.dump([os.path.basename(d) for d in tex_dirs], open(tl_json, 'w', encoding='utf-8'))
            stages['done'].append('질감'); save_stages()
        log('질감 %d초 · 조각 %d개' % (time.time() - t2, len(tex_dirs)), fp)
        eta.finish('질감')
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
    # DTM (땅만 · 설계서 6.1) — 메운 곳(건물 · 비닐하우스 · 숲 밑)은 DTM_땅칸.tif 에 0 으로 따로 표시
    dtm, ground, cover, dx0, dy1 = dtm_from_points(E[good], N[good], H[good], cell=DTM_CELL)
    write_raster(outs, 'DTM', dtm, cover, dx0, dy1, DTM_CELL)
    from PIL import Image
    Image.fromarray(np.where(cover, ground.astype(np.uint8), 255).astype(np.uint8), 'L').save(os.path.join(outs, 'DTM_땅칸.tif'))
    _world(outs, 'DTM_땅칸', dx0, dy1, DTM_CELL)
    nptd, ntrid = landxml_from_dsm(dtm, cover, dx0, dy1, DTM_CELL, outs, step_m=1.0, name='DTM')
    ground_frac = float(ground.sum() / max(1, cover.sum()))
    rr, cc = np.nonzero(ground[::4, ::4] & cover[::4, ::4]); rr *= 4; cc *= 4
    dem_cmp = compare_company_dem(dx0 + (cc + 0.5) * DTM_CELL, dy1 - (rr + 0.5) * DTM_CELL, dtm[rr, cc], log=lambda m: log(m, fp)) if len(rr) else None
    log('  DTM %.1f m · 땅으로 잰 칸 %.0f%%%s' % (DTM_CELL, 100 * ground_frac, (' · 사내 DEM 과 가운데값 %.2f m (사분위 %.2f ~ %.2f)' % dem_cmp[:3]) if dem_cmp else ''), fp)
    meta = tiles_to_web(tex_dirs, s, R, t, os.path.join(out, 'data'), max_tex=MAX_TEX)
    proj = {'version': 1, 'kind': 'mapping', 'title': a.title or Path(out).name, 'crs': CRS, 'height': 'EGM2008',
            'origin': meta['origin'], 'quality': a.quality, 'photos': len(photos), 'registered': nreg, 'rtk_rms': round(rms, 3),
            'cell': cell, 'bbox': [float(E[good].min()), float(N[good].min()), float(E[good].max()), float(N[good].max())],
            'made': time.strftime('%Y-%m-%d %H:%M')}
    with open(os.path.join(out, 'data', 'mapping.js'), 'w', encoding='utf-8') as f3:
        f3.write('window.RV_MAPPING = ' + json.dumps(proj, ensure_ascii=False, indent=1) + ';\n')
    write_info(outs, proj, photos, a, cam, rms, s, npt, ntri, colmap, mvs, dtm_info=(nptd, ntrid, ground_frac, dem_cmp), tiles=len(meta['tiles']))
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


def write_info(outs, proj, photos, a, cam, rms, scale, npt, ntri, colmap, mvs, dtm_info=None, tiles=0):
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
        '수평: %s %s (정사영상 · DSM · DTM 의 .prj · .tfw · 지표면 LandXML 도 같은 좌표)' % (CRS, CRS_CHOICES.get(CRS, '')),
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
        ('DTM.tif       표고(m) float32 · %.1f m 격자 · 땅만 (점진형 형태학 필터 · 건물 · 비닐하우스 · 나무 뺌) · 땅으로 잰 칸 %.0f %%' % (DTM_CELL, 100 * dtm_info[2])) if dtm_info else '',
        'DTM_땅칸.tif  1 = 땅으로 잰 칸 · 0 = 메운 칸(건물 밑 · 숲 밑 등 — 낮은 신뢰) · 255 = 자료 없음' if dtm_info else '',
        ('지표면_DTM.xml  LandXML 세모 지표면 (DTM 1 m · 점 %d · 세모 %d) — Civil 3D 원지반용' % dtm_info[:2]) if dtm_info else '',
        ('DTM ↔ 사내 수치지도 DEM(5 m 등고선): 가운데값 %.2f m · 사분위 %.2f ~ %.2f m (땅 칸 %d곳 · DEM 이 거칠어 참고용)' % dtm_info[3]) if dtm_info and dtm_info[3] else '',
        '3D 보기: 조각 %d개 (조각마다 질감 · 보기 전용으로 세모를 줄임 — 측량 계산은 위 래스터 · 지표면)' % tiles if tiles else '',
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
