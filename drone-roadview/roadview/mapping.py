"""경사촬영 사진 → 3D 매핑 (JACK 1125 · DJI Terra 대체 · 드론 로드뷰 안).

차례 (1125 80장 시험으로 정함 · AI\\drone-mapping\\README.md):
  COLMAP 4.2.1  특징점(GPU) → GPS 이웃 맞추기(GPU) → RTK 위치 카메라 풀기(pose_prior_mapper) → 왜곡 펴기
  OpenMVS 2.4.0 촘촘한 점구름 → 3D 면 (CPU — CUDA 판은 RTX 50 커널 없음)
  OpenMVS       질감 (1126 · 조각마다 · 원본 크기 사진 · 이음 고르기는 끄고 조각 색 맞춤 — 예전 «좌표 어긋남» 은 이음 고르기가
                면 자리를 까맣게 칠한 것) · 안 되는 조각만 COLMAP mesh_texturer
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
# 점구름 깊이 지도 폭(px) — 측량용(정사 · DSM · DTM · LAS) / 3D 만 (모양은 한 단계 덜 촘촘 · 질감은 QUALITY 폭 그대로)
# OpenMVS 는 640 px 아래로 안 줄임 (--min-resolution) → 빠르게는 둘이 같음
DENSE_PX_SURVEY = {'fast': 660, 'normal': 1320, 'fine': 2640}
DENSE_PX_MESH = {'fast': 660, 'normal': 660, 'fine': 1320}
OUTPUTS = ['mesh', 'ortho', 'dsm', 'dtm', 'las', 'contour']
OUTPUT_NAME = {'mesh': '3D 모델', 'ortho': '정사영상', 'dsm': 'DSM', 'dtm': 'DTM', 'las': '점구름(LAS)', 'contour': '등고선 DXF'}
DTM_CELL = 0.5             # DTM 격자 m
MAX_TEX = 4096             # 보기 조각 질감 tex 한 변 최대 (늘 읽음 · 1126 8192 → 4096 · 가까운 조각은 tex_hi 로 바꿔 읽음)
MAX_TEX_HI = 16384         # 고해상도 질감 tex_hi 한 변 최대 (대부분 그래픽 한도 · OpenMVS 질감은 8192 안)
# 질감 사진 폭(px) — 원본 크기 (M4E 5280 · 1126 Terra 견줌: 2640 은 흐림) · 질감 방식이 바뀌면 질감 단계만 다시 (stages params 'tex')
TEX_PX = {'fast': 2640, 'normal': 5280, 'fine': 5280}
TEX_METHOD = 'mvs1'        # mvs1 = 조각마다 OpenMVS TextureMesh (보는 사진만) + 조각 색 맞춤 · 안 되면 그 조각만 COLMAP mesh_texturer
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


def ortho_dsm(E, N, H, rgb, out, cell, write_ortho=True, write_dsm=True, preview=None):
    """가장 높은 점 = DSM · 그 점 색 = 정사. 작은 빈칸은 이웃으로 3번 메움. → (dsm, 채움, x0, y1).
    write_* = 결과물 파일 쓰기 · preview = 미리보기 PNG 경로 (정사를 쓰면 정사영상_미리보기.png 도)."""
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
    if write_ortho:
        Image.fromarray(np.dstack([ort, alpha]), 'RGBA').save(os.path.join(out, '정사영상.tif'), compression='tiff_lzw')
        _world(out, '정사영상', x0, y1, cell)
    if write_dsm:
        Image.fromarray(np.where(filled, dsm, -9999).astype(np.float32), 'F').save(os.path.join(out, 'DSM.tif'))
        _world(out, 'DSM', x0, y1, cell)
    k = max(1, max(W, Hh) // 2048)
    for pv in ([os.path.join(out, '정사영상_미리보기.png')] if write_ortho else []) + ([preview] if preview else []):
        Image.fromarray(np.dstack([ort, alpha]), 'RGBA').resize((max(1, W // k), max(1, Hh // k))).save(pv)
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


def write_las(path, E, N, H, rgb, ground=None, chunk=4000000):
    """점구름 LAS 1.2 · 점 형식 2 (위치 + 색) · 1 mm 단위 · 좌표계 = GeoKey (수평 CRS · 수직 EGM2008 3855).
    ground = 땅 점 표시(bool) 가 있으면 분류 2 = 땅 · 1 = 안 나눔, 없으면 0."""
    import datetime
    n = len(E)
    epsg = int(CRS.split(':')[1])
    keys = [(1024, 0, 1, 1), (1025, 0, 1, 1), (3072, 0, 1, epsg), (3076, 0, 1, 9001), (4096, 0, 1, 3855), (4099, 0, 1, 9001)]
    gk = struct.pack('<4H', 1, 1, 0, len(keys)) + b''.join(struct.pack('<4H', *k) for k in keys)
    vlr = struct.pack('<H16sHH32s', 0, b'LASF_Projection', 34735, len(gk), b'GeoKeyDirectoryTag') + gk
    mn = [float(E.min()), float(N.min()), float(H.min())]; mx = [float(E.max()), float(N.max()), float(H.max())]
    sc = 0.001
    off = [math.floor(v) for v in mn]
    d = datetime.date.today()
    hdr = struct.pack('<4sHH16sBB32s32sHHHIIBHI5I3d3d6d', b'LASF', 0, 0, b'', 1, 2, b'DH.Roadview', b'DH.Roadview 3D mapping',
                      d.timetuple().tm_yday, d.year, 227, 227 + len(vlr), 1, 2, 26, n, n, 0, 0, 0, 0,
                      sc, sc, sc, off[0], off[1], off[2], mx[0], mn[0], mx[1], mn[1], mx[2], mn[2])
    dt = np.dtype([('x', '<i4'), ('y', '<i4'), ('z', '<i4'), ('i', '<u2'), ('rn', 'u1'), ('cls', 'u1'), ('sa', 'i1'),
                   ('ud', 'u1'), ('ps', '<u2'), ('r', '<u2'), ('g', '<u2'), ('b', '<u2')])
    with open(path, 'wb') as fp:
        fp.write(hdr); fp.write(vlr)
        for i in range(0, n, chunk):
            j = min(n, i + chunk)
            rec = np.zeros(j - i, dt)
            rec['x'] = np.round((E[i:j] - off[0]) / sc); rec['y'] = np.round((N[i:j] - off[1]) / sc); rec['z'] = np.round((H[i:j] - off[2]) / sc)
            rec['rn'] = 9                              # 반사 1 / 1
            rec['cls'] = 0 if ground is None else np.where(ground[i:j], 2, 1)
            c = rgb[i:j].astype(np.uint16) * 257
            rec['r'] = c[:, 0]; rec['g'] = c[:, 1]; rec['b'] = c[:, 2]
            fp.write(rec.tobytes())


def read_crop(out):
    """data/crop.json → 잘라내기 다각형 [[E, N], …] (도면 좌표 · 3점 이상) 또는 None (JACK 1126 «경계가 지저분 → 그려서 잘라내기»)."""
    try:
        d = json.load(open(os.path.join(out, 'data', 'crop.json'), encoding='utf-8'))
        poly = [[float(p[0]), float(p[1])] for p in d.get('polygon') or []]
        return poly if len(poly) >= 3 else None
    except (OSError, ValueError, TypeError, IndexError):
        return None


def crop_points(E, N, poly, cell=0.1):
    """점마다 다각형 안인지 (다각형을 cell 격자에 칠해 찾음 · 수천만 점도 빠르게)."""
    import cv2
    P = np.asarray(poly, float)
    E = np.asarray(E); N = np.asarray(N)
    out = np.zeros(len(E), bool)
    if not len(E):
        return out
    # 격자는 다각형과 점 범위가 겹치는 곳만 (화면 밖 먼 꼭짓점이 있어도 격자가 커지지 않게 · 1126 검토)
    ex0, ex1, ny0, ny1 = max(P[:, 0].min(), float(E.min())), min(P[:, 0].max(), float(E.max())),         max(P[:, 1].min(), float(N.min())), min(P[:, 1].max(), float(N.max()))
    if ex0 > ex1 or ny0 > ny1:
        return out
    cell = max(cell, math.sqrt((ex1 - ex0 + 1) * (ny1 - ny0 + 1) / 4e8))   # 격자 4억 칸 안
    x0, y1 = ex0 - cell, ny1 + cell
    W = int(math.ceil((ex1 + cell - x0) / cell)) + 1; Hh = int(math.ceil((y1 - ny0 + cell) / cell)) + 1
    m = np.zeros((Hh, W), np.uint8)
    cv2.fillPoly(m, [np.round(np.c_[(P[:, 0] - x0) / cell, (y1 - P[:, 1]) / cell]).astype(np.int32)], 1)
    for i in range(0, len(E), 5_000_000):              # 나눠서 (색인 메모리)
        e, n = E[i:i + 5_000_000], N[i:i + 5_000_000]
        c = ((e - x0) / cell).astype(np.int32); r = ((y1 - n) / cell).astype(np.int32)
        ok = (r >= 0) & (r < Hh) & (c >= 0) & (c < W)
        o = np.zeros(len(e), bool); o[ok] = m[r[ok], c[ok]] > 0
        out[i:i + len(e)] = o
    return out


def crop_grid(poly, x0, y1, cell, shape):
    """격자(서쪽 끝 x0 · 북쪽 끝 y1 · cell · shape) 칸마다 다각형 안인지."""
    import cv2
    P = np.asarray(poly, float)
    m = np.zeros(shape, np.uint8)
    cv2.fillPoly(m, [np.round(np.c_[(P[:, 0] - x0) / cell - 0.5, (y1 - P[:, 1]) / cell - 0.5]).astype(np.int32)], 1)
    return m > 0


def write_contour_dxf(path, Z, mask, x0, y1, cell, minor=1.0, major=5.0, smooth_m=1.0, log=print):
    """땅(DTM) 격자 → 등고선 DXF (R12 · 한글 도면층 = ANSI_949): 등고선_주곡선 · 등고선_계곡선 (표고 = 높이값 · 2D 폴리선)
    + 등고선_표고 (계곡선 가운데 표고 글). 거친 칸 잡음은 smooth_m 가우시안으로 펴고 점은 2 cm 안으로 솎음. → (주곡선 수, 계곡선 수)."""
    import cv2
    import contourpy
    Zf = fill_pull_push(np.where(mask, Z, 0).astype(np.float32), mask)
    if smooth_m > 0:
        s = smooth_m / cell
        Zf = cv2.GaussianBlur(Zf, (0, 0), s)
    Zm = np.ma.masked_array(Zf, ~mask)
    ny, nx = Z.shape
    xs = x0 + (np.arange(nx) + 0.5) * cell; ys = y1 - (np.arange(ny) + 0.5) * cell
    gen = contourpy.contour_generator(xs, ys, Zm, line_type='Separate')
    zv = Zf[mask]
    if not len(zv):
        return 0, 0
    lo, hi = math.ceil(float(zv.min()) / minor) * minor, float(zv.max())
    L = []                                                        # DXF 줄 (코드 · 값)

    def ent(*kv):
        for k, v in zip(kv[::2], kv[1::2]):
            L.append('%d\n%s\n' % (k, v))
    n_minor = n_major = 0
    lv = lo
    while lv <= hi + 1e-9:
        is_major = abs(lv / major - round(lv / major)) < 1e-6
        lay = '등고선_계곡선' if is_major else '등고선_주곡선'
        for seg in gen.lines(lv):
            if len(seg) < 2:
                continue
            ap = cv2.approxPolyDP(seg.astype(np.float32).reshape(-1, 1, 2), 0.02, False).reshape(-1, 2) if len(seg) > 2 else seg
            if len(ap) < 2:
                continue
            closed = len(seg) > 3 and np.hypot(*(seg[0] - seg[-1])) < cell * 0.5
            ent(0, 'POLYLINE', 8, lay, 66, 1, 10, '0.0', 20, '0.0', 30, '%.3f' % lv, 70, 1 if closed else 0)
            for x, y in ap[:-1] if closed else ap:
                ent(0, 'VERTEX', 8, lay, 10, '%.3f' % x, 20, '%.3f' % y, 30, '%.3f' % lv)
            ent(0, 'SEQEND', 8, lay)
            if is_major:
                n_major += 1
                ln = float(np.sum(np.hypot(*np.diff(ap, axis=0).T)))
                if ln >= 30:                                      # 계곡선 가운데 표고 글
                    k = len(ap) // 2; d = ap[min(k + 1, len(ap) - 1)] - ap[max(k - 1, 0)]
                    ang = math.degrees(math.atan2(d[1], d[0]))
                    ang = ang + 180 if ang > 90 or ang < -90 else ang
                    ent(0, 'TEXT', 8, '등고선_표고', 10, '%.3f' % ap[k][0], 20, '%.3f' % ap[k][1], 30, '%.3f' % lv,
                        40, '0.8', 1, ('%.0f' % lv) if abs(lv - round(lv)) < 1e-6 else ('%.1f' % lv), 50, '%.2f' % ang, 72, 1, 11,
                        '%.3f' % ap[k][0], 21, '%.3f' % ap[k][1], 31, '%.3f' % lv)
            else:
                n_minor += 1
        lv = round(lv + minor, 6)
    head = ['0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1009\n9\n$DWGCODEPAGE\n3\nANSI_949\n',
            '9\n$EXTMIN\n10\n%.3f\n20\n%.3f\n30\n%.3f\n' % (xs[0], ys[-1], lo),
            '9\n$EXTMAX\n10\n%.3f\n20\n%.3f\n30\n%.3f\n' % (xs[-1], ys[0], hi), '0\nENDSEC\n',
            '0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLTYPE\n70\n1\n0\nLTYPE\n2\nCONTINUOUS\n70\n0\n3\nSolid line\n72\n65\n73\n0\n40\n0.0\n0\nENDTAB\n',
            '0\nTABLE\n2\nLAYER\n70\n3\n']
    for nm, col in (('등고선_주곡선', 8), ('등고선_계곡선', 1), ('등고선_표고', 7)):
        head.append('0\nLAYER\n2\n%s\n70\n0\n62\n%d\n6\nCONTINUOUS\n' % (nm, col))
    head.append('0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n')
    with open(path, 'w', encoding='cp949', errors='replace', newline='\r\n') as fp:
        fp.write(''.join(head)); fp.write(''.join(L)); fp.write('0\nENDSEC\n0\nEOF\n')
    return n_minor, n_major


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


_PLY_TY = {'char': 'i1', 'int8': 'i1', 'uchar': 'u1', 'uint8': 'u1', 'short': 'i2', 'int16': 'i2', 'ushort': 'u2', 'uint16': 'u2',
           'int': 'i4', 'int32': 'i4', 'uint': 'u4', 'uint32': 'u4', 'float': 'f4', 'float32': 'f4', 'double': 'f8', 'float64': 'f8'}


def read_tex_ply(path):
    """질감 PLY (COLMAP mesh_texturer · OpenMVS TextureMesh · web.ply) → (꼭짓점 float64 n×3, 면 m×3, 질감 좌표 m×3×2 (그림 위 = 0),
    질감 번호 m (OpenMVS 는 질감이 여러 장일 수 있음 · texnumber), 질감 그림 경로들 (comment TextureFile 차례))."""
    b = open(path, 'rb').read()
    i = b.find(b'end_header'); h = b.index(b'\n', i) + 1
    elems = []; texs = []
    for l in b[:h].decode('latin1').splitlines():
        p = l.split()
        if p[:2] == ['comment', 'TextureFile'] and len(p) > 2:
            texs.append(os.path.join(os.path.dirname(path), ' '.join(p[2:])))
        elif p[:1] == ['element']:
            elems.append((p[1], int(p[2]), []))
        elif p[:1] == ['property']:
            elems[-1][2].append(p[1:])
    off = h; A = {}
    for name, n, props in elems:                       # 목록 길이는 첫 줄 것 (세모 3 · 질감점 6) · 아래에서 모두 같은지 확인
        dt = []; o = off
        for pr in props:
            if pr[0] == 'list':
                ct, it = np.dtype(_PLY_TY[pr[1]]), np.dtype(_PLY_TY[pr[2]])
                cnt = int(np.frombuffer(b, '<' + ct.str[1:], 1, o)[0])
                dt += [(pr[3] + '#', '<' + ct.str[1:]), (pr[3], '<' + it.str[1:], (cnt,))]
                o += ct.itemsize + cnt * it.itemsize
            else:
                dt.append((pr[1], '<' + _PLY_TY[pr[0]])); o += np.dtype(_PLY_TY[pr[0]]).itemsize
        a = np.frombuffer(b, np.dtype(dt), n, off)
        for nm in a.dtype.names:
            if nm.endswith('#') and n and not (a[nm] == a[nm][0]).all():
                raise RuntimeError('질감 면 형식이 다름 (%s 길이가 줄마다 다름)' % nm[:-1])
        A[name] = a; off += n * a.dtype.itemsize
    v, f = A['vertex'], A['face']
    V = np.stack([v['x'], v['y'], v['z']], 1).astype(np.float64)
    F = f['vertex_indices'].astype(np.int64)
    tc = f['texcoord'].reshape(-1, 3, 2).astype(np.float32)
    tc[..., 1] = 1.0 - tc[..., 1]                       # 그림 위쪽 = 0 (COLMAP · OpenMVS 둘 다 OBJ 꼴 · 1125 99.9 % · 1126 80장 확인)
    TN = f['texnumber'].astype(np.int64) if 'texnumber' in f.dtype.names else np.zeros(len(F), np.int64)
    if not texs:
        texs = [os.path.join(os.path.dirname(path), 'texture.png')]
    if len(F) and (TN.min() < 0 or TN.max() >= len(texs)):
        raise RuntimeError('질감 번호가 그림 수(%d)와 안 맞음' % len(texs))
    return V, F, tc, TN, texs


def write_tex_ply(path, V, F, tc, TN, names):
    """read_tex_ply 꼴 → 질감 PLY (질감 좌표는 OBJ 꼴로 되돌려 씀 · texnumber · comment TextureFile)."""
    nf = len(F)
    rec = np.zeros(nf, np.dtype([('n', 'u1'), ('f', '<i4', (3,)), ('m', 'u1'), ('t', '<f4', (6,)), ('k', '<i4')]))
    rec['n'] = 3; rec['f'] = F; rec['m'] = 6; rec['k'] = TN
    t2 = tc.astype(np.float32).copy(); t2[..., 1] = 1.0 - t2[..., 1]; rec['t'] = t2.reshape(nf, 6)
    with open(path, 'wb') as fp:
        fp.write(('ply\nformat binary_little_endian 1.0\n' + ''.join('comment TextureFile %s\n' % n for n in names) +
                  'element vertex %d\nproperty float x\nproperty float y\nproperty float z\nelement face %d\n'
                  'property list uchar int vertex_indices\nproperty list uchar float texcoord\nproperty int texnumber\nend_header\n'
                  % (len(V), nf)).encode('latin1'))
        fp.write(V.astype(np.float32).tobytes()); fp.write(rec.tobytes())


def tile_views(ws, ply, margin=0.1, nsamp=4000, cap=160):
    """조각 꼭짓점이 사진 안(가장자리 여유 margin)에 비치는 사진 이름들 — sparse_txt (왜곡 안 편 카메라 · 가림은 안 봄 → 넉넉히).
    많으면 비치는 점 수 ÷ 거리² (가깝고 많이 보는 사진) 차례로 cap 장까지 — TextureMesh 는 사진을 다 메모리에 들고 있음
    (5280 px 한 장 ≈ 61 MB · 394장 가운데 조각은 373장이 봄 → 23 GB · 1126)."""
    b = open(ply, 'rb').read()
    i = b.find(b'end_header'); h = b.index(b'\n', i) + 1
    nv = int([l for l in b[:h].decode('latin1').splitlines() if l.startswith('element vertex')][0].split()[2])
    V = np.frombuffer(b, np.float32, nv * 3, h).reshape(nv, 3).astype(np.float64)
    if nv > nsamp:
        V = V[np.linspace(0, nv - 1, nsamp).astype(np.int64)]
    cams = {}
    for l in open(os.path.join(ws, 'sparse_txt', 'cameras.txt'), encoding='utf-8'):
        p = l.split()
        if p and not l.startswith('#'):
            cams[p[0]] = (float(p[2]), float(p[3]), [float(x) for x in p[4:8]])
    L = [l for l in open(os.path.join(ws, 'sparse_txt', 'images.txt'), encoding='utf-8') if not l.startswith('#')]
    out = []
    for l in L[0::2]:
        p = l.split(None, 9)
        if len(p) < 10 or p[8] not in cams:
            continue
        X = V @ _qrot(*map(float, p[1:5])).T + np.array(list(map(float, p[5:8])))
        w, hh, (fx, fy, cx, cy) = cams[p[8]]          # 모든 카메라 모형의 앞 넷 = fx fy cx cy (SIMPLE 꼴은 f f cx cy 아님 → 아래 넉넉히)
        ok = X[:, 2] > 1e-6
        u = fx * X[ok, 0] / X[ok, 2] + cx; v = fy * X[ok, 1] / X[ok, 2] + cy
        ins = (u > -margin * w) & (u < (1 + margin) * w) & (v > -margin * hh) & (v < (1 + margin) * hh)
        if ins.any():
            out.append((ins.sum() / float(np.median(X[ok, 2][ins])) ** 2, p[9].rstrip('\r\n')))
    out.sort(key=lambda x: -x[0])
    return [n for _, n in out[:cap]]


def texture_tile_mvs(colmap, mvs, ws, tex_ws, ply, d, logp):
    """조각 하나 → d/tex.ply + d/tex*.png (OpenMVS TextureMesh · 원본 크기 사진 · 그 조각을 보는 사진만 → 사진이 많아도 메모리 작게).
    이음 고르기(전역 · 지역)는 끔 — 2.4.0 은 켜면 면 자리가 까맣게 칠해짐 (1126 80장 · 둘 다 · 대신 level_mvs_tile).
    → 실패 사유 (없으면 None)."""
    os.makedirs(d, exist_ok=True)
    img_dir = os.path.join(tex_ws, 'images')
    allnames = sorted(os.listdir(img_dir))
    try:
        names = set(tile_views(ws, ply))
    except (OSError, ValueError, IndexError, KeyError) as ex:
        names = set(allnames); log('  조각 사진 고르기 못 함 (%s) → 모든 사진' % ex)
    names &= set(allnames)
    if len(names) < 2:
        return '이 조각을 보는 사진 %d장' % len(names)
    try:                                               # 모형의 모든 사진 (폴더에 없는 사진도 빼야 TextureMesh 가 안 멈춤 · 1126)
        L = [l for l in open(os.path.join(ws, 'sparse_txt', 'images.txt'), encoding='utf-8') if not l.startswith('#')]
        model_names = {p[9].rstrip('\r\n') for p in (l.split(None, 9) for l in L[0::2]) if len(p) >= 10}
    except OSError:
        model_names = set()
    with open(os.path.join(d, 'drop.txt'), 'w', encoding='utf-8') as fp:
        fp.write(''.join(n + '\n' for n in sorted((model_names | set(allnames)) - names)))
    sp = os.path.join(d, 'sparse')
    shutil.rmtree(sp, ignore_errors=True); os.makedirs(sp)
    rc = run([colmap, 'image_deleter', '--input_path', os.path.join(tex_ws, 'sparse'), '--output_path', sp,
              '--image_names_path', os.path.join(d, 'drop.txt')], logp + '_a.log')
    if rc != 0:
        return 'image_deleter 종료코드 %d' % rc
    rc = run([os.path.join(mvs, 'InterfaceCOLMAP.exe'), '-i', '.', '-o', 'scene.mvs', '--image-folder', os.path.abspath(img_dir), '-w', '.'],
             logp + '_b.log', cwd=d)
    if rc != 0 or not os.path.exists(os.path.join(d, 'scene.mvs')):
        return 'InterfaceCOLMAP 종료코드 %d' % rc
    rc = run([os.path.join(mvs, 'TextureMesh.exe'), 'scene.mvs', '-m', os.path.abspath(ply), '-o', 'tex.mvs', '--export-type', 'ply',
              '--max-texture-size', '8192', '--global-seam-leveling', '0', '--local-seam-leveling', '0'] + mvs_cuda_opt(mvs) + ['-w', '.'],
             logp + '_c.log', cwd=d)
    for f in ('scene.mvs', 'tex.mvs', 'drop.txt'):
        if os.path.exists(os.path.join(d, f)):
            os.remove(os.path.join(d, f))
    shutil.rmtree(sp, ignore_errors=True)
    if rc != 0 or not os.path.exists(os.path.join(d, 'tex.ply')):
        return 'TextureMesh 종료코드 %d' % rc
    return None


def level_mvs_tile(d, quality=92):
    """OpenMVS 조각 질감 뒷손질 → d/web.ply + d/webN.jpg (tex.ply · tex*.png 는 지움) → 정보 dict.
    ① 조각(patch = 한 사진에서 떼어 온 세모 묶음)마다 RGB 더하기 값 하나로 이음 양쪽 색을 맞춤 (OpenMVS 이음 고르기 대신 ·
       가림 · 어긋남 표본은 Huber 로 약하게) ② 질감 그림에서 안 쓴 곳 잘라냄 (2의 거듭제곱 크기라 절반쯤 빔)
    ③ 빈 칸을 이웃 색으로 메움 (줄인 그림 · 먼 거리에서 주황 테가 비치지 않게)."""
    import cv2
    t0 = time.time()
    V, F, tc, TN, texs = read_tex_ply(os.path.join(d, 'tex.ply'))
    m = len(F)
    # 모서리를 함께 쓰는 세모 짝 → 같은 조각(같은 질감 · 모서리 두 끝 질감 좌표가 같음) 인지
    e = np.concatenate([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]]); fi = np.tile(np.arange(m), 3)
    ej = np.repeat(np.array([[0, 1], [1, 2], [2, 0]]), m, axis=0)       # 세모 안 꼭짓점 자리
    sw = e[:, 0] > e[:, 1]
    e[sw] = e[sw][:, ::-1]; ej[sw] = ej[sw][:, ::-1]
    key = e[:, 0] * (int(F.max()) + 1) + e[:, 1]
    o = np.argsort(key, kind='stable'); k2 = key[o]
    s = np.nonzero(k2[1:] == k2[:-1])[0]
    f1, f2 = fi[o][s], fi[o][s + 1]
    j1, j2 = ej[o][s], ej[o][s + 1]
    ua1, ub1 = tc[f1, j1[:, 0]], tc[f1, j1[:, 1]]; ua2, ub2 = tc[f2, j2[:, 0]], tc[f2, j2[:, 1]]
    same = (TN[f1] == TN[f2]) & (np.abs(ua1 - ua2).max(1) < 1e-6) & (np.abs(ub1 - ub2).max(1) < 1e-6)
    lab = np.arange(m); p, q = f1[same], f2[same]
    while True:                                          # 이어진 묶음 번호 (작은 번호로 모으고 포인터 뛰기)
        lp, lq = lab[p], lab[q]; mn = np.minimum(lp, lq)
        if not ((lp != mn) | (lq != mn)).any():
            break
        np.minimum.at(lab, lp, mn); np.minimum.at(lab, lq, mn); lab = lab[lab]
    _, pid = np.unique(lab, return_inverse=True); npc = int(pid.max()) + 1 if m else 0
    ims = [cv2.imdecode(np.fromfile(x, np.uint8), cv2.IMREAD_COLOR) for x in texs]
    if any(x is None for x in ims):
        raise RuntimeError('질감 그림 못 읽음')
    # 이음 표본: 다른 조각끼리 붙은 모서리의 두 끝 + 가운데 · 양쪽 그림에서 3×3 평균
    sm = ~same & (pid[f1] != pid[f2])
    blur = [cv2.blur(x, (3, 3)).astype(np.float32) for x in ims]

    def samp(tn, uv):
        r = np.zeros((len(uv), 3), np.float32)
        for k, im in enumerate(blur):
            mk = tn == k
            hh, ww = im.shape[:2]
            r[mk] = im[np.clip((uv[mk, 1] * hh).astype(int), 0, hh - 1), np.clip((uv[mk, 0] * ww).astype(int), 0, ww - 1)]
        return r
    P1 = np.tile(pid[f1[sm]], 3); P2 = np.tile(pid[f2[sm]], 3)
    dlt = (samp(np.tile(TN[f1[sm]], 3), np.concatenate([ua1[sm], ub1[sm], (ua1[sm] + ub1[sm]) / 2])) -
           samp(np.tile(TN[f2[sm]], 3), np.concatenate([ua2[sm], ub2[sm], (ua2[sm] + ub2[sm]) / 2]))).astype(np.float64)
    blur.clear()                                         # 큰 그림 메모리 풀기
    # 조각마다 더하기 값 x: Σ w (x1 − x2 + d)² + 0.02·차수·x² 최소 → 대각 전처리 켤레 기울기 (Huber 다시 무게 2번)
    x = np.zeros((npc, 3)); w = np.ones(len(dlt))
    for _ in range(3 if len(dlt) else 0):
        deg = np.bincount(P1, w, npc) + np.bincount(P2, w, npc); dg = deg + 0.02 * np.maximum(deg, 1.0)

        def Ax(z):
            y = dg[:, None] * z
            for c in range(3):
                y[:, c] -= np.bincount(P1, w * z[P2, c], npc) + np.bincount(P2, w * z[P1, c], npc)
            return y
        b_ = np.stack([np.bincount(P2, w * dlt[:, c], npc) - np.bincount(P1, w * dlt[:, c], npc) for c in range(3)], 1)
        r = b_ - Ax(x); z = r / dg[:, None]; pp = z.copy(); rz = (r * z).sum(0)
        for _ in range(400):
            Ap = Ax(pp); al = rz / np.maximum((pp * Ap).sum(0), 1e-12)
            x += al * pp; r -= al * Ap
            if np.abs(r).max() < 1e-3:
                break
            z = r / dg[:, None]; rz2 = (r * z).sum(0); pp = z + rz2 / np.maximum(rz, 1e-12) * pp; rz = rz2
        w = 1.0 / np.maximum(1.0, np.abs(x[P1] - x[P2] + dlt).mean(1) / 12.0)
    names = []; tc2 = tc.copy(); info = {'patches': npc, 'seams': int(sm.sum()), 'shift': round(float(np.abs(x).mean()), 1) if npc else 0.0}
    for k, im in enumerate(ims):
        hh, ww = im.shape[:2]
        fk = np.nonzero(TN == k)[0]
        if not len(fk):
            names.append(None); continue
        uvp = tc[fk] * [ww, hh]
        lo = np.floor(uvp.min(1)).astype(int) - 1; hi = np.ceil(uvp.max(1)).astype(int) + 2
        pk = pid[fk]; od = np.argsort(pk, kind='stable'); pks = pk[od]
        st = np.r_[0, np.nonzero(np.diff(pks))[0] + 1]
        x0 = np.maximum(np.minimum.reduceat(lo[od, 0], st), 0); y0 = np.maximum(np.minimum.reduceat(lo[od, 1], st), 0)
        x1 = np.maximum.reduceat(hi[od, 0], st); y1 = np.maximum.reduceat(hi[od, 1], st)
        imf = im.astype(np.float32)
        for P, a0, b0, a1, b1 in zip(pks[st], x0, y0, x1, y1):       # OpenMVS 는 조각 네모를 겹치지 않게 쌓음 (테두리 여유 안)
            imf[b0:b1, a0:a1] += x[P].astype(np.float32)
        used = np.zeros((hh, ww), np.uint8)
        cv2.fillPoly(used, list(np.round(uvp * 16).astype(np.int32)), 255, lineType=cv2.LINE_8, shift=4)
        used = cv2.dilate(used, np.ones((5, 5), np.uint8)) > 0
        ys, xs = np.nonzero(used.any(1))[0], np.nonzero(used.any(0))[0]
        cy0, cy1 = max(0, ys[0] - 4), min(hh, ys[-1] + 5); cx0, cx1 = max(0, xs[0] - 4), min(ww, xs[-1] + 5)
        imf = imf[cy0:cy1, cx0:cx1]; used = used[cy0:cy1, cx0:cx1]
        tc2[fk, :, 0] = (tc[fk, :, 0] * ww - cx0) / (cx1 - cx0); tc2[fk, :, 1] = (tc[fk, :, 1] * hh - cy0) / (cy1 - cy0)
        acc = imf * used[..., None]; wm = used.astype(np.float32)
        for sg in (2, 6, 18):                              # 빈 칸 메우기 (흐린 평균을 넓혀 가며)
            num = cv2.GaussianBlur(acc, (0, 0), sg); den = cv2.GaussianBlur(wm, (0, 0), sg)
            fill = (den > 1e-3) & ~used
            imf[fill] = num[fill] / den[fill][:, None]
            used = used | fill; wm = used.astype(np.float32); acc = imf * used[..., None]
        imf[~used] = 128
        nm = 'web%d.jpg' % k
        ok, buf = cv2.imencode('.jpg', np.clip(imf + 0.5, 0, 255).astype(np.uint8), [cv2.IMWRITE_JPEG_QUALITY, quality])
        if not ok:
            raise RuntimeError('질감 그림 저장 실패')
        buf.tofile(os.path.join(d, nm)); names.append(nm)
        info.setdefault('size', []).append([int(cx1 - cx0), int(cy1 - cy0)])
    keep = [k for k, n in enumerate(names) if n]          # 안 쓰인 질감은 빼고 번호 다시
    remap = np.full(len(names), -1, np.int64); remap[keep] = np.arange(len(keep))
    write_tex_ply(os.path.join(d, 'web.ply'), V, F, tc2, remap[TN], [names[k] for k in keep])
    for f in [os.path.join(d, 'tex.ply')] + texs:
        if os.path.exists(f):
            os.remove(f)
    info['sec'] = round(time.time() - t0, 1)
    return info


def read_textured_ply(td):
    """조각 질감 폴더 → read_tex_ply 꼴 — web.ply (OpenMVS + 뒷손질 · 1126) 없으면 mesh.ply (COLMAP · 그 앞 판)."""
    p = os.path.join(td, 'web.ply')
    return read_tex_ply(p if os.path.exists(p) else os.path.join(td, 'mesh.ply'))


def tex_ply_exists(td):
    return os.path.exists(os.path.join(td, 'web.ply')) or os.path.exists(os.path.join(td, 'mesh.ply'))


def tiles_to_web(tex_dirs, s, R, t, out, max_tex=MAX_TEX, origin=None, max_hi=MAX_TEX_HI, crop=None):
    """조각마다 질감 면 → data/tiles/tNN.bin (위치 float32×3 · 질감 float32×2, 세모마다 셋) + tNN.jpg · data/mesh.json.
    한 조각에 질감이 여러 장이면 장마다 보기 조각 하나. tex = 한 변 max_tex 이하 (늘 읽음) ·
    tex_hi = 원래 크기 (한 변 max_hi 이하 · tex 보다 클 때만 · 보기 화면이 가까운 조각만 바꿔 읽음) + texsize_hi."""
    from PIL import Image
    Image.MAX_IMAGE_PIXELS = None
    parts = [read_textured_ply(d) for d in tex_dirs]
    allE = []
    conv = []
    for V, fi, tc, tn, texs in parts:
        E, N, H = to_5186_h(s * (V @ R.T) + t)
        conv.append((E, N, H)); allE.append((np.median(E), np.median(N), np.percentile(H, 5), len(E)))
    w = np.array([a[3] for a in allE], float)
    o = [round(float(np.average([a[0] for a in allE], weights=w)), 1), round(float(np.average([a[1] for a in allE], weights=w)), 1),
         round(float(min(a[2] for a in allE)), 1)] if origin is None else [float(v) for v in origin]   # 원점이 정해져 있으면 그대로 (관로 자료와 맞게)
    tdir = os.path.join(out, 'tiles'); os.makedirs(tdir, exist_ok=True)
    for f in glob.glob(os.path.join(tdir, '*')):
        os.remove(f)
    tiles = []; mn = np.full(3, np.inf); mx = np.full(3, -np.inf); tris = 0
    fit = lambda im, cap: im if max(im.size) <= cap else im.resize(           # noqa: E731
        (max(1, int(im.size[0] * cap / max(im.size))), max(1, int(im.size[1] * cap / max(im.size)))), Image.LANCZOS)
    for (V, fi, tc, tn, texs), (E, N, H) in zip(parts, conv):
        vpos = np.stack([E - o[0], N - o[1], H - o[2]], 1).astype(np.float32)
        keep = crop_points(E[fi].mean(1), N[fi].mean(1), crop) if crop else np.ones(len(fi), bool)   # 잘라내기 = 세모 가운데가 안인 것만
        for q in np.unique(tn):
            sel = (tn == q) & keep; k = len(tiles)
            if not sel.any():
                continue
            pos = vpos[fi[sel].reshape(-1)]
            with open(os.path.join(tdir, 't%02d.bin' % k), 'wb') as fp:
                fp.write(pos.tobytes()); fp.write(tc[sel].reshape(-1, 2).astype(np.float32).tobytes())
            src = texs[q]
            im = Image.open(src).convert('RGB')
            ent = {'bin': 'tiles/t%02d.bin' % k, 'tex': 'tiles/t%02d.jpg' % k, 'verts': int(len(pos)), 'tris': int(sel.sum())}
            if max(im.size) > max_tex:                     # 고해상도 = 원래 그림 (jpg 면 다시 누르지 않고 복사)
                hi = fit(im, max_hi)
                if hi is im and src.lower().endswith('.jpg'):
                    shutil.copyfile(src, os.path.join(tdir, 't%02d_hi.jpg' % k))
                else:
                    hi.save(os.path.join(tdir, 't%02d_hi.jpg' % k), quality=90)
                ent.update(tex_hi='tiles/t%02d_hi.jpg' % k, texsize_hi=list(hi.size))
                del hi
            im = fit(im, max_tex)
            im.save(os.path.join(tdir, 't%02d.jpg' % k), quality=90)
            ent['texsize'] = list(im.size)
            tiles.append(ent)
            mn = np.minimum(mn, pos.min(0)); mx = np.maximum(mx, pos.max(0)); tris += int(sel.sum())
    for f in ('mesh.bin', 'tex.jpg'):                   # 옛 한 덩어리 자료는 지움
        if os.path.exists(os.path.join(out, f)):
            os.remove(os.path.join(out, f))
    if not tiles:                                       # 잘라내기 안쪽에 세모가 없음 → 무한대 대신 0 (보기 화면 JSON 이 깨지지 않게)
        mn = np.zeros(3); mx = np.zeros(3)
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


def ground_grid(out, cell=1.0):
    """관로 땅 높이용 매끈한 땅 (1 m · JACK 1126 «관로가 울퉁불퉁») → (Z, mask, x0, y1) 또는 None.
    결과물/DTM.tif 가 있으면 그것을 1 m 로, 없으면 data/지면.tif (옛 프로젝트 = 결과물/DSM.tif) 를 0.25 m 칸 최솟값으로 줄여
    점진형 형태학 필터로 땅만 (풀 · 연석 · 차 · 도랑 사면 꼭대기를 빼서 관이 출렁이지 않게)."""
    from PIL import Image
    Image.MAX_IMAGE_PIXELS = None
    src = None
    for d, nm in ((os.path.join(out, '결과물'), 'DTM'), (os.path.join(out, 'data'), '지면'), (os.path.join(out, '결과물'), 'DSM')):
        if os.path.exists(os.path.join(d, nm + '.tif')) and os.path.exists(os.path.join(d, nm + '.tfw')):
            src = (d, nm); break
    if not src:
        return None
    A = np.asarray(Image.open(os.path.join(src[0], src[1] + '.tif')), np.float32)
    w = [float(x) for x in open(os.path.join(src[0], src[1] + '.tfw')).read().split()]
    c0, xw, yn = w[0], w[4] - w[0] / 2, w[5] + w[0] / 2          # 칸 크기 · 서쪽 끝 · 북쪽 끝
    A = np.where(A <= -9998, np.nan, A)
    k = max(1, int(round((cell if src[1] == 'DTM' else 0.25) / c0)))
    if k > 1:                                                     # 칸 묶기 (DTM = 평균 · 표면 = 최솟값)
        h, wd = (A.shape[0] // k) * k, (A.shape[1] // k) * k
        B = A[:h, :wd].reshape(h // k, k, wd // k, k)
        with warnings.catch_warnings():
            warnings.simplefilter('ignore', RuntimeWarning)
            A = np.nanmean(B, axis=(1, 3)) if src[1] == 'DTM' else np.nanmin(B, axis=(1, 3))
        c0 *= k
    if src[1] == 'DTM':
        return A, np.isfinite(A), xw, yn, c0
    r, c = np.nonzero(np.isfinite(A))
    if len(r) < 10:
        return None
    E = xw + (c + 0.5) * c0; N = yn - (r + 0.5) * c0
    dtm, ground, cover, x0, y1 = dtm_from_points(E, N, A[r, c].astype(np.float64), cell=cell)
    return dtm, cover, x0, y1, cell


def write_dem_js(out, log=print):
    """data/dem.js = 360 보기와 같은 꼴 (window.RV_DEM · 타일 128 · 한 줄 겹침 · 남→북 · x0/y0 = 매핑 원점 기준 · 표고 절대값).
    보기 화면이 관 땅 높이(꼭짓점을 끌어도)를 여기서 읽음. → 칸 수 (못 만들면 0)."""
    import base64
    old = os.path.join(out, 'data', 'dem.js')
    if os.path.exists(old):                                         # 못 만들어도 옛 원점 기준 파일이 남지 않게 (1126 검토)
        os.remove(old)
    try:
        P = _read_js(os.path.join(out, 'data', 'mapping.js'), 'RV_MAPPING')
        G = ground_grid(out)
    except Exception as ex:                                         # noqa: BLE001
        log('  땅 높이(dem.js) 못 만듦 · %s' % ex); return 0
    if G is None:
        return 0
    Z, mask, x0, y1, cell = G
    Zs = np.where(mask, Z, np.nan).astype('<f4')[::-1]              # 행 = 남 → 북
    E0, N0 = float(P['origin'][0]), float(P['origin'][1])
    T = 128
    ny, nx = Zs.shape
    tiles = {}
    for iy in range((ny - 1) // T + 1):
        for ix in range((nx - 1) // T + 1):
            sub = np.full((T + 1, T + 1), np.nan, '<f4')
            blk = Zs[iy * T:iy * T + T + 1, ix * T:ix * T + T + 1]
            sub[:blk.shape[0], :blk.shape[1]] = blk
            if np.isfinite(sub).any():
                tiles['%d_%d' % (ix, iy)] = base64.b64encode(sub.tobytes()).decode('ascii')
    dj = {'cell': cell, 'tile': T, 'x0': round(x0 + cell / 2 - E0, 3), 'y0': round(y1 - ny * cell + cell / 2 - N0, 3), 'tiles': tiles,
          'kind': 'ground'}
    with open(os.path.join(out, 'data', 'dem.js'), 'w', encoding='utf-8') as fp:
        fp.write('window.RV_DEM = ' + json.dumps(dj) + ';\n')
    return int(np.isfinite(Zs).sum())


def ground_sampler(out):
    """매끈한 땅 f(E, N) = 표고 (겹선형 · 빈칸 NaN) — ground_grid · 없으면 dsm_sampler."""
    G = ground_grid(out)
    if G is None:
        return dsm_sampler(out)
    Z, mask, x0, y1, cell = G
    Zm = np.where(mask, Z, np.nan)

    def f(E, N):
        E = np.asarray(E, float); N = np.asarray(N, float)
        fx = (E - x0) / cell - 0.5; fy = (y1 - N) / cell - 0.5
        c = np.floor(fx).astype(int); r = np.floor(fy).astype(int); tx = fx - c; ty = fy - r
        ok = (r >= 0) & (r + 1 < Zm.shape[0]) & (c >= 0) & (c + 1 < Zm.shape[1])
        z = np.full(E.shape, np.nan)
        rr, cc, ax, ay = r[ok], c[ok], tx[ok], ty[ok]
        z[ok] = (Zm[rr, cc] * (1 - ax) + Zm[rr, cc + 1] * ax) * (1 - ay) + (Zm[rr + 1, cc] * (1 - ax) + Zm[rr + 1, cc + 1] * ax) * ay
        return z
    return f


def smooth_along(pts, z, half=5.0):
    """관 줄을 따라 측점 ±half m 이동평균 (빈 값은 빼고 · 다 비면 그대로)."""
    P = np.asarray([[p[0], p[1]] for p in pts], float)
    s = np.r_[0.0, np.cumsum(np.hypot(*np.diff(P, axis=0).T))]
    z = np.asarray(z, float); ok = np.isfinite(z)
    if not ok.any():
        return z
    out = np.empty_like(z)
    for i in range(len(z)):
        m = ok & (np.abs(s - s[i]) <= half)
        out[i] = z[m].mean() if m.any() else np.nan
    return out


def dsm_sampler(out):
    """땅 높이 f(E 배열, N 배열) = 표고 (빈칸 NaN) — data/지면.tif (1.0.15 · 늘 만듦) · 없으면 결과물/DSM.tif."""
    if os.path.exists(os.path.join(out, 'data', '지면.tif')):
        return tif_sampler(os.path.join(out, 'data'), '지면')
    return tif_sampler(os.path.join(out, '결과물'), 'DSM')


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
    for ln in lines:                                   # 범위 안 덩어리마다 한 줄 · 원래 꼭짓점(ctrl)은 잘린 줄에서도 남김
        cset = set(ln.get('ctrl') or range(len(ln['pts'])))   # (없애면 2 m 마다 손잡이 · 1126 관로 도구 도우미)
        run, idx = [], []

        def flush():
            if len(run) >= 2:
                c = [i for i, k in enumerate(idx) if k in cset]
                c = sorted(set([0, len(run) - 1] + c))
                cut.append(dict(ln, pts=list(run), ctrl=c))
        for k, p in enumerate(ln['pts']):
            if inside(p):
                run.append(p); idx.append(k)
            else:
                flush(); run, idx = [], []
        flush()
    g = ground_sampler(out)                           # 매끈한 땅 (DTM 1 m) — DSM 이면 풀 · 연석 · 도랑 사면에 관이 출렁임 (JACK 1126)
    if not os.path.exists(os.path.join(out, 'data', 'dem.js')):
        write_dem_js(out, log=log)                    # 보기 화면도 같은 땅 (꼭짓점을 끌어 옮길 때)
    geo = Geo(CRS)
    pl = []
    for ln in cut:
        pts = ln['pts']
        gz = g([p[0] for p in pts], [p[1] for p in pts])
        if np.isfinite(gz).any():                     # 빈칸은 이웃 값으로 · 줄을 따라 ±5 m 이동평균
            idx = np.arange(len(gz)); ok = np.isfinite(gz)
            gz = smooth_along(pts, np.interp(idx, idx[ok], gz[ok]), 5.0)
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
    """단계 무게(실측 초)로 퍼센트 · 남은 초 → «@진행 퍼센트 남은초 글» (app.Job 이 읽음).
    무게 = 394장 보통 실측(1125) · 품질마다 무거운 단계는 배수 (80장 실측: 정밀 점구름 4.2배 · 면 4.3배 · 질감 3.7배 / 빠르게 0.2배 안팎).
    1126 JACK «정밀인데 남은 33분» — 예전 무게(보통 80장 비율)는 정밀 점구름을 4배 작게 봤음."""
    W0 = {'특징점': 60, '맞추기': 90, '카메라 풀기': 900, '왜곡 펴기': 36, '점구름 준비': 1, '점구름': 2800, '측량 점구름': 2800,
          '3D 면': 1290, '질감': 1300, '결과물': 200}     # 질감 1126: 원본 크기 + OpenMVS 조각 (80장 ≈ 7분 · 394장 ≈ 20~25분)
    QF = {'fast': {'점구름': 0.22, '측량 점구름': 0.22, '3D 면': 0.11, '질감': 0.3, '결과물': 0.3},
          'normal': {},
          'fine': {'점구름': 4.2, '측량 점구름': 4.2, '3D 면': 4.3, '질감': 2.0, '결과물': 2.6}}

    def __init__(self, n, quality='normal'):
        self.t0 = time.time(); self.done = 0.0; self.n = n; self.q = quality
        self.plan()

    def plan(self, skip=(), scale=None):
        """안 할 단계는 무게 0 · 점구름 해상도에 따라 무게 줄임 (결과물 고르기 · 1.0.15) · 품질 배수."""
        self.W = dict(self.W0)
        for k, f in self.QF.get(self.q, {}).items():
            self.W[k] = self.W[k] * f
        for k in skip:
            self.W[k] = 0
        for k, f in (scale or {}).items():
            self.W[k] = self.W[k] * f
        self.total = float(sum(self.W.values()))

    def start(self, name):
        el = time.time() - self.t0
        frac = min(1.0, self.done / max(1e-6, self.total))
        pct = max(getattr(self, 'last', 0.0), min(99.0, 100.0 * frac))     # 다시 계획해도 뒤로 안 감
        self.last = pct
        left = max(0, int(el / max(1e-6, frac) - el)) if self.done > 0 else -1
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
    ap.add_argument('--outputs', default='mesh,ortho,dsm,dtm',
                    help='만들 결과물 (쉼표): mesh = 3D 모델 · ortho = 정사영상 · dsm = DSM + 지표면 · dtm = DTM + 지표면 · las = 점구름')
    ap.add_argument('--contour', type=float, default=1.0, help='등고선 주곡선 간격 m (계곡선 = 5배)')
    ap.add_argument('--reuse-only', action='store_true', help='결과물 다듬기: 앞 단계를 다시 쓸 수 없으면(사진 · 설정이 바뀜) 처음부터 돌지 말고 멈춤')
    a = ap.parse_args(argv)
    if not (0.05 <= a.contour <= 100):
        print('등고선 간격은 0.05 ~ 100 m (%s)' % a.contour); return 2
    set_crs(a.crs)
    outs_sel = {x.strip() for x in a.outputs.split(',') if x.strip() in OUTPUTS}
    if not outs_sel:
        print('만들 결과물 없음 (--outputs %s)' % a.outputs); return 2
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
    with open(os.path.join(ws, 'args.json'), 'w', encoding='utf-8') as f2:    # 결과물 다듬기(잘라내기 · DEM 더하기)에서 같은 설정으로 다시
        json.dump({'photos': os.path.abspath(a.photos), 'quality': a.quality, 'names': a.names, 'title': a.title, 'crs': a.crs,
                   'outputs': sorted(outs_sel), 'contour': a.contour, 'cell': a.cell}, f2, ensure_ascii=False, indent=1)
    with open(os.path.join(ws, 'names.txt'), 'w', encoding='utf-8') as f2:
        f2.write('\n'.join(names) + '\n')
    cam = camera_params(photos[0])
    res_tex, res_feat = QUALITY[a.quality]
    cell = a.cell or {'fast': 0.1, 'normal': 0.05, 'fine': 0.03}[a.quality]
    log('시작 · 사진 %d장 · 품질 %s · RTK 고정 %d장 · 카메라 %s %s %d×%d' % (
        len(photos), QUALITY_NAME[a.quality], sum(p['rtk'] == 50 for p in photos),
        photos[0].get('model') or '?', photos[0].get('src') or '?', photos[0].get('w', 0), photos[0].get('h', 0)), fp)
    eta = Eta(len(photos), a.quality)
    want_pts0 = bool({'dsm', 'dtm', 'las', 'contour'} & outs_sel)     # 정사는 원본 사진 색 → 측량용 점구름이 없어도 됨 (1126)
    eta.plan(skip=(set() if 'mesh' in outs_sel else {'3D 면', '질감'}) | {'측량 점구름'},
             scale={'점구름': 1.0 if want_pts0 else (DENSE_PX_MESH[a.quality] / DENSE_PX_SURVEY[a.quality]) ** 2})
    T0 = time.time()

    # 끝난 단계는 다시 쓰기 (설계서 원칙 6 · 10.5): 같은 사진 · 품질 · 카메라 값 · 차례 판일 때만
    import hashlib
    # '측량 점구름' = 3D 만 만든 프로젝트에 정사 · DSM 등을 더할 때만 (맨 끝: 3D 면 · 질감을 다시 해도 안 지움 · drop_from)
    ORDER = ['특징점', '맞추기', '카메라 풀기', '왜곡 펴기', '점구름 준비', '점구름', '3D 면', '질감', '측량 점구름']
    sigd = {'v': PIPELINE_VERSION, 'q': a.quality, 'names': sorted(n.lower() for n in names), 'cam': cam}
    if a.mapper != 'auto':                       # 카메라 풀기 방식을 굳이 고르면 서명에 (auto 는 옛 서명 그대로)
        sigd['mapper'] = a.mapper
    sig = hashlib.md5(json.dumps(sigd, ensure_ascii=False).encode('utf-8')).hexdigest()
    stf = os.path.join(ws, 'stages.json')
    try:
        stages = json.load(open(stf, encoding='utf-8'))
    except (OSError, ValueError):
        stages = {}
    fresh = stages.get('sig') != sig
    if fresh and a.reuse_only and stages.get('sig'):  # 결과물 다듬기인데 앞 단계를 못 씀 → 몇 시간 처음부터 돌지 않고 멈춤 (1126 검토)
        log('다듬기 멈춤: 사진 · 설정이 처음과 달라 앞 단계를 다시 쓸 수 없음 → 매핑 화면에서 새로 만들기', fp); return 6
    if fresh:                                        # 사진 · 품질이 바뀜 → 처음부터
        for p in ('db.db', 'sparse', 'sparse_txt', 'dense', 'dense_tex', 'tex_colmap'):
            q = os.path.join(ws, p)
            if os.path.isdir(q):
                shutil.rmtree(q, ignore_errors=True)
            elif os.path.exists(q):
                os.remove(q)
        stages = {'sig': sig, 'done': [], 'stale_outputs': True}    # 결과물을 새로 쓸 때까지 옛 결과물은 믿지 않음
    for p in ('sparse', 'sparse_txt', 'dense', 'tex_colmap'):
        os.makedirs(os.path.join(ws, p), exist_ok=True)

    def save_stages():
        with open(stf, 'w', encoding='utf-8') as f4:
            json.dump(stages, f4, ensure_ascii=False)

    def drop_from(name):                         # name 과 뒤 단계는 다시 (측량 점구름은 점구름 · 그 앞이 바뀔 때만)
        i = ORDER.index(name); keep_srv = name != '측량 점구름' and i > ORDER.index('점구름')
        stages['done'] = [d for d in stages['done'] if ORDER.index(d) < i or (keep_srv and d == '측량 점구름')]

    def step(name, cmd, logname, cwd=None, check=None):
        if name in stages['done'] and (check is None or check()):
            log('%s · 지난번 것 씀' % name, fp)
            eta.finish(name)
            return True
        drop_from(name)
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
            drop_from('카메라 풀기'); save_stages()
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
    # 결과물 고르기 (JACK 1125 밤: «할 때마다 모든 걸 다 만들면 느림 · 3D 를 많이 씀 → 매핑은 빠르게»)
    #   3D 만 = 점구름을 덜 촘촘하게(모양 · 질감은 그대로 선명한 사진) · 정사 · DSM · DTM · LAS 를 고르면 측량용 해상도
    #   나중에 더 고르면 사진 맞추기 · 카메라 · (있으면) 점구름 · 3D 는 그대로 쓰고 모자란 것만
    want_mesh = 'mesh' in outs_sel
    want_pts = bool({'dsm', 'dtm', 'las', 'contour'} & outs_sel)       # 정사만이면 가벼운 점구름 (색은 원본 사진 · 높이만 씀 · 1126)
    px_srv, px_mesh = DENSE_PX_SURVEY[a.quality], DENSE_PX_MESH[a.quality]
    if 'params' not in stages:                   # 1.0.14 까지 만든 프로젝트 = 측량용 점구름 · 지금 세모 한도
        stages['params'] = {'dense': px_srv, 'faces': TARGET_FACES[a.quality]} if '점구름' in stages['done'] else {}
    prm = stages['params']
    if prm.get('faces') not in (None, TARGET_FACES[a.quality]):
        drop_from('3D 면')
    cloud_ok = lambda: has('dense', 'scene_cpu.ply')                                       # noqa: E731
    srv_ok = lambda: has('dense', 'scene_srv.ply')                                         # noqa: E731
    reuse = lambda n, chk: n in stages['done'] and chk()                                   # noqa: E731
    dense_px = prm['dense'] if (reuse('점구름', cloud_ok) and prm.get('dense')) else (px_srv if want_pts else px_mesh)
    need_srv = want_pts and dense_px < px_srv     # 3D 만 만들었던 프로젝트에 정사 · DSM 등을 더할 때만
    eta.plan(skip=(set() if want_mesh else {'3D 면', '질감'}) | (set() if need_srv else {'측량 점구름'}),
             scale={'점구름': (dense_px / px_srv) ** 2})
    log('  만들 것: %s · 점구름 %d px%s' % (' · '.join(OUTPUT_NAME[k] for k in OUTPUTS if k in outs_sel), dense_px,
                                            ' + 측량용 %d px' % px_srv if need_srv else ''), fp)

    def ensure_images():                         # 지난번 끝에서 지운 왜곡 편 사진만 다시 (뒤 단계는 안 지움)
        im = os.path.join(dense, 'images')
        if not (os.path.isdir(im) and os.listdir(im)):
            log('  왜곡 편 사진 다시 만들기', fp)
            rc = run([colmap, 'image_undistorter', '--image_path', a.photos, '--input_path', model, '--output_path', dense,
                      '--output_type', 'COLMAP', '--max_image_size', str(res_tex)], os.path.join(ws, '4b_왜곡펴기_다시.log'))
            if rc != 0 or not (os.path.isdir(im) and len(os.listdir(im)) >= nreg):   # 반만 찬 폴더를 다음에 통과시키지 않게
                shutil.rmtree(im, ignore_errors=True)
                log('  왜곡 편 사진 다시 만들기 실패 (4b_왜곡펴기_다시.log)', fp)
                return False
        return True

    def densify(name, px, outname, chk):
        if not reuse(name, chk):
            if not ensure_images():
                return False
            for f in glob.glob(os.path.join(dense, '*.dmap')):   # 다른 해상도 깊이 지도를 다시 읽지 않게
                os.remove(f)
        lvl = max(0, int(round(math.log2(max(1.0, res_tex / px)))))
        return step(name, [os.path.join(mvs, 'DensifyPointCloud.exe'), 'scene.mvs', '-o', outname, '--resolution-level', str(lvl),
                           '--max-resolution', str(res_tex)] + mvs_cuda_opt(mvs) + ['-w', '.'],
                    '5_%s.log' % name.replace(' ', ''), cwd=dense, check=chk)

    if ok and not reuse('점구름 준비', lambda: has('dense', 'scene.mvs')):
        ok = ensure_images()
    ok = ok and step('점구름 준비', [os.path.join(mvs, 'InterfaceCOLMAP.exe'), '-i', '.', '-o', 'scene.mvs', '--image-folder', 'images', '-w', '.'],
                     '5a_넘기기.log', cwd=dense, check=lambda: has('dense', 'scene.mvs'))
    if ok and not reuse('점구름', cloud_ok):
        prm['dense'] = dense_px
    ok = ok and densify('점구름', dense_px, 'scene_cpu.mvs', cloud_ok)
    if ok and need_srv:
        ok = densify('측량 점구름', px_srv, 'scene_srv.mvs', srv_ok)
    tex_dirs = []
    tex_root = os.path.join(ws, 'tex_tiles'); tl_json = os.path.join(tex_root, 'tiles.json')

    def tex_list():                              # tiles.json = 조각 폴더 이름만 (결과 폴더를 옮겨도 · 1125 검토)
        try:
            L = [os.path.join(tex_root, os.path.basename(str(d).rstrip('/\\'))) for d in json.load(open(tl_json, encoding='utf-8'))]
            return L if L and all(tex_ply_exists(d) for d in L) else None
        except (OSError, ValueError):
            return None
    tex_ws = dense if TEX_PX[a.quality] == res_tex else os.path.join(ws, 'dense_tex')

    def ensure_tex_images():                     # 질감용 원본 크기 왜곡 편 사진 (dense_tex · 질감 단계마다 새로 · 끝에 지움)
        if tex_ws == dense:
            return ensure_images()
        shutil.rmtree(tex_ws, ignore_errors=True)
        im = os.path.join(tex_ws, 'images')
        rc = run([colmap, 'image_undistorter', '--image_path', a.photos, '--input_path', model, '--output_path', tex_ws,
                  '--output_type', 'COLMAP', '--max_image_size', str(TEX_PX[a.quality])], os.path.join(ws, '7a_질감사진.log'))
        if rc != 0 or not (os.path.isdir(im) and len(os.listdir(im)) >= nreg):
            shutil.rmtree(tex_ws, ignore_errors=True)
            log('  질감 사진 만들기 실패 (7a_질감사진.log)', fp)
            return False
        return True
    if ok and want_mesh:
        prm['faces'] = TARGET_FACES[a.quality]
        if not reuse('3D 면', lambda: has('dense', 'scene_cpu_mesh.ply')):   # 정사만 만든 뒤 3D 를 더할 때 (사진 머리말을 읽음)
            ok = ensure_images()
        ok = ok and step('3D 면', [os.path.join(mvs, 'ReconstructMesh.exe'), 'scene_cpu.mvs', '-o', 'scene_cpu_mesh.mvs'] + mvs_cuda_opt(mvs) +
                  ['--target-face-num', str(TARGET_FACES[a.quality]), '-w', '.'], '6_면.log', cwd=dense,
                  check=lambda: has('dense', 'scene_cpu_mesh.ply'))
        # 질감 = 조각마다 (한 장이면 32768 폭까지 커져 그래픽 한도 16384 로 줄어 흐려짐 · 1125 394장)
        #   1126 Terra 견줌 (COLMAP 2640 px = 흐림 · 방지턱 끝 · 길 가장자리 깨짐): 원본 크기 사진 (TEX_PX) + OpenMVS TextureMesh
        #   (조각을 보는 사진만 · 가림 검사 · 겉도는 사진 빼기) + 조각 색 맞춤 · 그 조각만 안 되면 COLMAP mesh_texturer (그 앞 판)
        #   질감 방식 · 사진 폭이 다르면 (1.0.15 까지 만든 프로젝트 포함) 질감만 다시
        tex_sig = '%s-%d' % (TEX_METHOD, TEX_PX[a.quality])
        if ok and reuse('질감', tex_list) and prm.get('tex') == tex_sig:
            log('질감 · 지난번 것 씀', fp); eta.finish('질감'); tex_dirs = tex_list()
        elif ok:
            drop_from('질감'); save_stages()
            eta.start('질감'); t2 = time.time()
            shutil.rmtree(tex_root, ignore_errors=True); os.makedirs(tex_root, exist_ok=True)
            tiles = split_mesh_tiles(os.path.join(dense, 'scene_cpu_mesh.ply'), os.path.join(ws, 'mesh_tiles'))
            mvs_ok = ensure_tex_images()
            n_mvs = 0; t_lv = 0.0
            for k, (p, nf) in enumerate(tiles):
                d = os.path.join(tex_root, 't%02d' % k); os.makedirs(d, exist_ok=True)
                err = texture_tile_mvs(colmap, mvs, ws, tex_ws, p, d, os.path.join(ws, '7_질감_t%02d' % k)) if mvs_ok else '질감 사진 없음'
                if not err:
                    try:
                        t_lv += level_mvs_tile(d)['sec']
                    except Exception as ex:           # noqa: BLE001 — 메모리 · 그림 읽기 · 형식
                        err = '색 맞춤 %s' % ex
                if err:
                    log('  조각 %d OpenMVS 질감 안 됨 (%s) → COLMAP 질감' % (k, err), fp)
                    shutil.rmtree(d, ignore_errors=True); os.makedirs(d, exist_ok=True)
                    if not ensure_images():
                        ok = False; break
                    rc = run([colmap, 'mesh_texturer', '--workspace_path', dense, '--input_path', p, '--output_path', d], os.path.join(ws, '7_질감_t%02d.log' % k))
                    if rc != 0 or not os.path.exists(os.path.join(d, 'mesh.ply')):
                        ok = False; log('  조각 %d 질감 실패 (7_질감_t%02d.log)' % (k, k), fp); break
                else:
                    n_mvs += 1
                tex_dirs.append(d)
            if tex_ws != dense and not a.keep_work:   # 원본 크기 사진은 질감에만 씀 (394장 ≈ 5 GB) → 바로 지움
                shutil.rmtree(tex_ws, ignore_errors=True)
            if ok:
                json.dump([os.path.basename(d) for d in tex_dirs], open(tl_json, 'w', encoding='utf-8'))
                stages['done'].append('질감'); prm['tex'] = tex_sig
            save_stages()
            log('질감 %d초 · 조각 %d개 (OpenMVS %d · 사진 %d px · 색 맞춤 %d초)' % (time.time() - t2, len(tex_dirs), n_mvs, TEX_PX[a.quality], t_lv), fp)
            eta.finish('질감')
    save_stages()
    if not ok:
        log('매핑 실패 → work 폴더 기록(*.log) 확인', fp); return 4
    eta.start('결과물')
    t1 = time.time()
    outs = os.path.join(out, '결과물'); os.makedirs(outs, exist_ok=True)
    data_dir = os.path.join(out, 'data'); os.makedirs(data_dir, exist_ok=True)
    try:
        P_old = _read_js(os.path.join(data_dir, 'mapping.js'), 'RV_MAPPING')
    except (OSError, ValueError, IndexError):
        P_old = {}
    # 옛 결과물이 지금 모델 · 좌표계와 다르면 지움 (검사점 보고서 · 정보는 둠) — 처음부터 다시 · 그 판이 결과물 전에 끊김
    #   (stages 의 stale_outputs) · 좌표계를 바꿈 (원점 · 관로 좌표도 옛 좌표계 → 관로는 다시 넣기) — 1125 검토
    crs_changed = P_old.get('crs') not in (None, CRS)
    if fresh or stages.get('stale_outputs') or crs_changed:
        for f in glob.glob(os.path.join(outs, '*')):
            if os.path.basename(f).startswith(('정사영상', 'DSM', 'DTM', '지표면_', '점구름')):
                os.remove(f)
        for f in ('mesh.json', 'mesh.bin', 'tex.jpg', '지면.tif', '지면.tfw', '지면.prj', '미리보기.png') + (('pipes.js', 'pipes_orig.js', 'obstacles.js') if crs_changed else ()) + ('dem.js',):
            if os.path.exists(os.path.join(data_dir, f)):
                os.remove(os.path.join(data_dir, f))
        shutil.rmtree(os.path.join(data_dir, 'tiles'), ignore_errors=True)
        if crs_changed:
            log('  좌표계 %s → %s · 옛 결과물 · 관로 지움 (관로는 다시 넣기)' % (P_old.get('crs'), CRS), fp)
        P_old = {k: v for k, v in P_old.items() if k == 'cover' and not crs_changed}
    use_srv = need_srv or reuse('측량 점구름', srv_ok)     # 측량용 점구름이 있으면 늘 그것으로 (지면 · 미리보기도)
    xyz, rgb = read_openmvs_ply(os.path.join(dense, 'scene_srv.ply' if use_srv else 'scene_cpu.ply'))
    E, N, H = to_5186_h(s * (xyz.astype(np.float64) @ R.T) + t)
    good = np.isfinite(E) & np.isfinite(H)
    E, N, H, rgb = E[good], N[good], H[good], rgb[good]
    crop = read_crop(out)                            # 잘라내기 (data/crop.json · 결과물 다듬기 · JACK 1126)
    if crop:
        k = crop_points(E, N, crop)
        if k.sum() < 1000:                           # 범위 밖 · 너무 작음 → 파일을 쓰기 전에 멈추고 잘라내기를 치움 (다음 다듬기가 계속 실패하지 않게)
            bad = os.path.join(data_dir, 'crop.json')
            if os.path.exists(bad):
                os.replace(bad, os.path.join(data_dir, 'crop_못씀.json'))
            log('잘라내기 멈춤: 그린 안쪽에 자료가 없음 (점 %d) → 결과물 범위 안에 다시 그리기' % int(k.sum()), fp); return 5
        E, N, H, rgb = E[k], N[k], H[k], rgb[k]
        log('  잘라내기 다각형 %d점 · 점 %d 남김' % (len(crop), len(E)), fp)
    origin = P_old.get('origin') or [round(float(np.median(E)), 1), round(float(np.median(N)), 1), round(float(np.percentile(H, 5)), 1)]
    npt = ntri = None
    ortho_meta = None
    if 'ortho' in outs_sel or 'dsm' in outs_sel:
        dsm, filled, x0, y1 = ortho_dsm(E, N, H, rgb, outs, cell, write_ortho=False, write_dsm=False)
        if crop:                                     # 이웃 메움이 경계 밖으로 번지지 않게
            filled &= crop_grid(crop, x0, y1, cell, filled.shape)
        if 'dsm' in outs_sel:
            write_raster(outs, 'DSM', dsm, filled, x0, y1, cell)
        if 'ortho' in outs_sel:
            # 정사 = 원본 사진 재투영 (JACK 1126 «탁함 · Terra 는 확대해도 깨끗» · ortho.py) · 안 되면 옛 점구름 색 정사
            try:
                from .ortho import true_ortho
                oi = true_ortho(ws, a.photos, s, R, t, dsm, filled, x0, y1, cell, outs, a.quality, to_enh=to_5186_h, world=_world,
                                fill=fill_pull_push, cell_out=a.cell, log=lambda m: log(m, fp))
                ortho_meta = {'method': 'photo', 'cell': oi['cell'], 'gsd': round(oi['gsd'], 4)}
                log('  정사영상 원본 사진 재투영 · 칸 %.1f cm · 사진 GSD %.1f cm · %d×%d · 사진 %d / %d장 · 빈칸 %.1f%% · %d초' % (
                    100 * oi['cell'], 100 * oi['gsd'], oi['size'][0], oi['size'][1], oi['used'], oi['photos'], 100 * oi['empty'], oi['sec']), fp)
            except Exception as ex:                  # noqa: BLE001 — 사진 폴더 없음 · 메모리 등
                log('  정사영상 원본 사진 재투영 실패 (%s) → 점구름 색 정사' % ex, fp)
                ortho_dsm(E, N, H, rgb, outs, cell, write_ortho=True, write_dsm=False)
                ortho_meta = {'method': 'points', 'cell': cell, 'why': str(ex)[:200]}
        if 'dsm' in outs_sel:
            npt, ntri = landxml_from_dsm(dsm, filled, x0, y1, cell, outs, step_m=1.0)
        del dsm, filled
    # 땅 높이 (관로 넣기 · 늘 만듦 · 0.25 m 로 가벼움) + 미리보기 그림 (처음 화면 썸네일 · 3D 없을 때 보기)
    gc = max(cell, 0.25)
    gz, gf, gx0, gy1 = ortho_dsm(E, N, H, rgb, data_dir, gc, write_ortho=False, write_dsm=False,
                                 preview=os.path.join(data_dir, '미리보기.png'))
    if crop:
        gf &= crop_grid(crop, gx0, gy1, gc, gf.shape)
    write_raster(data_dir, '지면', gz, gf, gx0, gy1, gc)
    dtm_info = None
    ground_cls = None
    n_ct = None
    if 'dtm' in outs_sel or 'contour' in outs_sel:
        # DTM (땅만 · 설계서 6.1) — 메운 곳(건물 · 비닐하우스 · 숲 밑)은 DTM_땅칸.tif 에 0 으로 따로 표시 · 등고선도 이 땅으로
        dtm, ground, cover, dx0, dy1 = dtm_from_points(E, N, H, cell=DTM_CELL)
        if crop:
            cover &= crop_grid(crop, dx0, dy1, DTM_CELL, cover.shape)
        if 'contour' in outs_sel:                    # 등고선 DXF (주곡선 a.contour · 계곡선 5배 · JACK 1126)
            n_ct = write_contour_dxf(os.path.join(outs, '등고선.dxf'), dtm, cover, dx0, dy1, DTM_CELL,
                                     minor=a.contour, major=a.contour * 5)
            if not sum(n_ct) and os.path.exists(os.path.join(outs, '등고선.dxf')):
                os.remove(os.path.join(outs, '등고선.dxf'))    # 땅 칸이 없어 한 줄도 없음 → 잘리기 전 옛 파일이 남지 않게
            log('  등고선 DXF 주곡선 %g m · 계곡선 %g m · 선 %d + %d' % (a.contour, a.contour * 5, n_ct[0], n_ct[1]), fp)
    if 'dtm' in outs_sel:
        write_raster(outs, 'DTM', dtm, cover, dx0, dy1, DTM_CELL)
        from PIL import Image
        Image.fromarray(np.where(cover, ground.astype(np.uint8), 255).astype(np.uint8), 'L').save(os.path.join(outs, 'DTM_땅칸.tif'))
        _world(outs, 'DTM_땅칸', dx0, dy1, DTM_CELL)
        nptd, ntrid = landxml_from_dsm(dtm, cover, dx0, dy1, DTM_CELL, outs, step_m=1.0, name='DTM')
        ground_frac = float(ground.sum() / max(1, cover.sum()))
        rr, cc = np.nonzero(ground[::4, ::4] & cover[::4, ::4]); rr *= 4; cc *= 4
        dem_cmp = compare_company_dem(dx0 + (cc + 0.5) * DTM_CELL, dy1 - (rr + 0.5) * DTM_CELL, dtm[rr, cc], log=lambda m: log(m, fp)) if len(rr) else None
        log('  DTM %.1f m · 땅으로 잰 칸 %.0f%%%s' % (DTM_CELL, 100 * ground_frac, (' · 사내 DEM 과 가운데값 %.2f m (사분위 %.2f ~ %.2f)' % dem_cmp[:3]) if dem_cmp else ''), fp)
        dtm_info = (nptd, ntrid, ground_frac, dem_cmp)
        if 'las' in outs_sel:                    # 점구름 분류: 땅으로 잰 칸에서 DTM 과 0.3 m 안 = 땅(2)
            r = np.clip(((dy1 - N) / DTM_CELL).astype(np.int64), 0, dtm.shape[0] - 1)
            c = np.clip(((E - dx0) / DTM_CELL).astype(np.int64), 0, dtm.shape[1] - 1)
            ground_cls = ground[r, c] & (np.abs(H - dtm[r, c]) < 0.3)
    if 'las' in outs_sel:
        write_las(os.path.join(outs, '점구름.las'), E, N, H, rgb, ground_cls)
        log('  점구름 LAS %d점%s' % (len(E), ' · 땅 분류 %.0f%%' % (100 * ground_cls.mean()) if ground_cls is not None else ''), fp)
    meta = None
    if want_mesh:
        meta = tiles_to_web(tex_dirs, s, R, t, data_dir, max_tex=MAX_TEX, origin=origin, crop=crop)
    elif os.path.exists(os.path.join(data_dir, 'mesh.json')):   # 지난번에 만든 3D 는 그대로 둠
        try:
            meta = json.load(open(os.path.join(data_dir, 'mesh.json'), encoding='utf-8'))
        except (OSError, ValueError):
            meta = None
    have = {'mesh': bool(meta and meta.get('tris')), 'ortho': os.path.exists(os.path.join(outs, '정사영상.tif')),
            'dsm': os.path.exists(os.path.join(outs, 'DSM.tif')), 'dtm': os.path.exists(os.path.join(outs, 'DTM.tif')),
            'las': os.path.exists(os.path.join(outs, '점구름.las')), 'contour': os.path.exists(os.path.join(outs, '등고선.dxf'))}
    proj = {'version': 1, 'kind': 'mapping', 'title': a.title or P_old.get('title') or Path(out).name, 'crs': CRS, 'height': 'EGM2008',
            'origin': meta['origin'] if meta else origin, 'quality': a.quality, 'photos': len(photos), 'registered': nreg,
            'rtk_rms': round(rms, 3), 'cell': cell, 'bbox': [float(E.min()), float(N.min()), float(E.max()), float(N.max())],
            'outputs': [k for k in OUTPUTS if have[k]], 'dense_px': dense_px, 'made': time.strftime('%Y-%m-%d %H:%M')}
    for k in ('cover',):                         # 관로 넣기에서 정한 값은 남김
        if k in P_old:
            proj[k] = P_old[k]
    if crop:
        proj['crop'] = crop
    if have['contour']:
        proj['contour'] = {'minor': a.contour, 'major': a.contour * 5} if 'contour' in outs_sel else P_old.get('contour')
    if have['ortho'] and (ortho_meta or P_old.get('ortho')):     # 정사 방식 · 칸 (정보.txt) — 이번에 안 만들었으면 지난번 것
        proj['ortho'] = ortho_meta or P_old['ortho']
    with open(os.path.join(data_dir, 'mapping.js'), 'w', encoding='utf-8') as f3:
        f3.write('window.RV_MAPPING = ' + json.dumps(proj, ensure_ascii=False, indent=1) + ';\n')
    stages.pop('stale_outputs', None); save_stages()
    t3 = time.time(); nd = write_dem_js(out, log=lambda m: log(m, fp))   # 관로 땅 높이 (보기 화면 · 1126)
    log('  땅 높이 dem.js %d칸 · %.0f초' % (nd, time.time() - t3), fp)
    write_info(outs, proj, photos, a, cam, rms, s, npt, ntri, colmap, mvs, dtm_info=dtm_info,
               tiles=len(meta['tiles']) if meta and 'tiles' in meta else 0, px=(dense_px, px_srv if need_srv else 0))
    log('결과물 %d초 · 이번에 %s%s' % (time.time() - t1, ' · '.join(OUTPUT_NAME[k] for k in OUTPUTS if k in outs_sel),
                                      (' · 3D 세모 %d' % meta['tris']) if (meta and want_mesh) else ''), fp)
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


def write_info(outs, proj, photos, a, cam, rms, scale, npt, ntri, colmap, mvs, dtm_info=None, tiles=0, px=(0, 0)):
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
        '[결과물] (%s)' % ' · '.join(OUTPUT_NAME[k] for k in proj.get('outputs', [])),
    ]
    ex = lambda f: os.path.exists(os.path.join(outs, f))      # noqa: E731
    if ex('정사영상.tif'):
        om = proj.get('ortho') or {}
        if om.get('method') == 'photo':
            lines.append('정사영상.tif  RGBA · 원본 사진 재투영 (칸마다 가장 수직인 사진 · 가림 검사) · %.1f cm 격자 · 사진 GSD %.1f cm · 투명 = 자료 없음'
                         % (100 * om['cell'], 100 * om['gsd']))
        else:
            lines.append('정사영상.tif  RGBA · 점구름 색 · %.2f m 격자 · 투명 = 자료 없음%s' % (
                om.get('cell', proj['cell']), ' (원본 사진 재투영 실패: %s)' % om['why'] if om.get('why') else ''))
    if ex('DSM.tif'):
        lines.append('DSM.tif       표고(m) float32 · %.2f m 격자 · -9999 = 자료 없음 · 건물 · 나무 포함한 표면 모델 (DTM 아님)' % proj['cell'])
    if ex('지표면_DSM.xml'):
        lines.append('지표면_DSM.xml  LandXML 세모 지표면 (DSM 1 m 솎음%s) — Civil 3D 지표면 가져오기용 · 표면 모델'
                     % (' · 점 %d · 세모 %d' % (npt, ntri) if npt else ''))
    if ex('DTM.tif'):
        lines.append('DTM.tif       표고(m) float32 · %.1f m 격자 · 땅만 (점진형 형태학 필터 · 건물 · 비닐하우스 · 나무 뺌)%s'
                     % (DTM_CELL, ' · 땅으로 잰 칸 %.0f %%' % (100 * dtm_info[2]) if dtm_info else ''))
        lines.append('DTM_땅칸.tif  1 = 땅으로 잰 칸 · 0 = 메운 칸(건물 밑 · 숲 밑 등 — 낮은 신뢰) · 255 = 자료 없음')
    if ex('지표면_DTM.xml'):
        lines.append('지표면_DTM.xml  LandXML 세모 지표면 (DTM 1 m%s) — Civil 3D 원지반용' % (' · 점 %d · 세모 %d' % dtm_info[:2] if dtm_info else ''))
    if dtm_info and dtm_info[3]:
        lines.append('DTM ↔ 사내 수치지도 DEM(5 m 등고선): 가운데값 %.2f m · 사분위 %.2f ~ %.2f m (땅 칸 %d곳 · DEM 이 거칠어 참고용)' % dtm_info[3])
    if ex('등고선.dxf'):
        ct = proj.get('contour') or {}
        lines.append('등고선.dxf    DXF R12 · 땅(DTM) 기준 · 주곡선 %s m (등고선_주곡선) · 계곡선 %s m (등고선_계곡선 · 표고 글 등고선_표고) · 폴리선 높이값 = 표고'
                     % (ct.get('minor', '?'), ct.get('major', '?')))
    if proj.get('crop'):
        lines.append('잘라내기: 다각형 %d점 안만 (data/crop.json · 결과물 다듬기에서 바꾸거나 없앰)' % len(proj['crop']))
    if ex('점구름.las'):
        lines.append('점구름.las    LAS 1.2 · 위치 + 색 · 1 mm 단위 · 분류 2 = 땅 (DTM 을 같이 만들 때) · 좌표 %s + EGM2008' % CRS)
    if tiles:
        lines.append('3D 보기: 조각 %d개 (조각마다 질감 · 보기 전용으로 세모를 줄임 — 측량 계산은 위 래스터 · 지표면)' % tiles)
    lines += [
        '',
        '[처리]',
        '점구름 깊이 지도 %d px%s (3D 만이면 한 단계 덜 촘촘 · 정사 · DSM · DTM · LAS 는 측량용)' % (
            px[0], ' · 측량용 %d px 따로' % px[1] if px[1] else ''),
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
