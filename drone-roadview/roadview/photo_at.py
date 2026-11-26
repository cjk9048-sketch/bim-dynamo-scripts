"""원본 사진 보기 (JACK 1126 · 3D 보기 오른쪽 메뉴 «이 자리 원본 사진 보기»).

땅 한 자리 (E, N, 표고 EGM2008) 를 본 드론 원본 사진을 좋은 순서로 찾고, 그 사진에서 그 자리 (u, v 화소) 를 셈.
  모델 → ECEF = s·R·X + t (mapping.model_to_ecef) 를 거꾸로: (E, N, 표고) → 경위도 · 타원체고 → ECEF → X = Rᵀ(ECEF − t) / s
  → 사진마다 카메라 좌표 → OPENCV 왜곡 (ortho.py 와 같은 식 · 사진 테두리 밖 되접힘 막기도 같음).
  점수 (낮을수록 좋음 · ortho.py 와 같음) = 땅 → 카메라 선 기울기 (수평 거리 / 높이) + 사진 가운데에서 거리 + 경사 사진 벌점.
  가림은 대충 (카메라 앞 · 사진 안) 만.
자료: 결과 폴더 work/sparse_txt (cameras.txt · images.txt) · work/db.db (RTK 위치) · data/mapping.js (crs · photos_dir).
사진 폴더 = data/mapping.js 의 photos_dir · 없으면 결과물/정보.txt 의 «사진 폴더: …» 줄 (옛 프로젝트).
"""
from __future__ import annotations

import base64
import json
import math
import os
import threading

import numpy as np

EDGE_W = 0.5            # 점수: 사진 가운데에서 거리 (0 = 가운데 · 1 = 테두리) 무게
OBL_W = 0.5             #       카메라 축 기울기 (tan) 무게 — 경사 사진은 뒤로
MAX_N = 12              # 돌려줄 사진 수
PAD = 4                 # 사진 테두리 여유 (화소)
VIEW_PX = 1600          # 전체 보기 그림 긴 변
ZOOM_W, ZOOM_H = 1400, 900   # 확대 보기 = 원본 화소 그대로 그 자리 둘레를 자름

_CACHE = {}
_LOCK = threading.Lock()


def _read_js(path, var):
    s = open(path, encoding='utf-8').read()
    return json.loads(s.split('=', 1)[1].strip().rstrip(';'))


def photos_dir(out):
    """사진 폴더 (없으면 None)."""
    for get in (lambda: json.load(open(os.path.join(out, 'work', 'args.json'), encoding='utf-8')).get('photos'),
                lambda: _read_js(os.path.join(out, 'data', 'mapping.js'), 'RV_MAPPING').get('photos_dir')):
        try:
            d = get()
            if d and os.path.isdir(d):
                return d
        except (OSError, ValueError, IndexError, AttributeError):
            pass
    try:
        for line in open(os.path.join(out, '결과물', '정보.txt'), encoding='utf-8'):
            if line.startswith('사진 폴더:'):
                d = line.split(':', 1)[1].strip()
                if d and os.path.isdir(d):
                    return d
    except OSError:
        pass
    return None


def _model(out):
    """카메라 · 사진 · 모델 → ECEF (결과 폴더마다 한 번 · images.txt 가 바뀌면 다시)."""
    from .mapping import model_to_ecef
    from .ortho import _r2max, _read_model
    ws = os.path.join(out, 'work')
    it = os.path.join(ws, 'sparse_txt', 'images.txt')
    if not os.path.exists(it) or not os.path.exists(os.path.join(ws, 'db.db')):
        raise FileNotFoundError('카메라 자료 없음 (work 폴더)')
    st = os.stat(it)
    key = (os.path.normcase(os.path.abspath(out)), st.st_mtime_ns, st.st_size)
    with _LOCK:
        m = _CACHE.get(key[0])
        if m and m['key'] == key:
            return m
        cams, ims = _read_model(ws)
        s, R, t, _rms, _n = model_to_ecef(ws)
        ims = [im for im in ims if im[3] in cams]
        Rq = np.array([im[1] for im in ims]); Tq = np.array([im[2] for im in ims])
        Cm = -np.einsum('nji,nj->ni', Rq, Tq)                         # 카메라 중심 (모델) = −Rᵀ T
        Am = Rq[:, 2, :]                                               # 카메라 축 (모델) = Rᵀ [0, 0, 1]
        try:
            crs = _read_js(os.path.join(out, 'data', 'mapping.js'), 'RV_MAPPING').get('crs') or 'EPSG:5186'
        except (OSError, ValueError, IndexError):
            crs = 'EPSG:5186'
        m = {'key': key, 'cams': cams, 'names': [im[0] for im in ims], 'cid': np.array([im[3] for im in ims]),
             'Rq': Rq, 'Tq': Tq, 's': float(s), 'R': R, 't': t, 'Ce': s * (Cm @ R.T) + t, 'Ae': Am @ R.T,
             'r2m': {c: _r2max(*cams[c]) for c in cams}, 'crs': crs}
        _CACHE[key[0]] = m
        return m


def enh_to_ecef(E, N, H, crs):
    """(E, N 도면 좌표 · 표고 EGM2008) → (ECEF xyz, 위도 rad, 경도 rad) — mapping.to_5186_h 의 거꾸로."""
    import pyproj
    pyproj.network.set_network_enabled(True)
    lon, lat = pyproj.Transformer.from_crs(crs, 'EPSG:4326', always_xy=True).transform(E, N)
    _, _, hel = pyproj.Transformer.from_crs('EPSG:4326+3855', 'EPSG:4979', always_xy=True).transform(lon, lat, H)
    if not math.isfinite(hel):
        raise RuntimeError('표고 변환 실패 (EGM2008 지오이드 파일 · 인터넷 확인)')
    x, y, z = pyproj.Transformer.from_crs('EPSG:4979', 'EPSG:4978', always_xy=True).transform(lon, lat, hel)
    return np.array([x, y, z], float), math.radians(lat), math.radians(lon)


def photos_at(out, E, N, H, n=MAX_N):
    """→ {ok, photos: [{name, u, v, w, h, score, dist}] (좋은 순서), dir} · 못 찾으면 {ok: False, msg}."""
    from .ortho import _project
    pdir = photos_dir(out)
    if not pdir:
        return {'ok': False, 'msg': '사진 폴더 없음'}
    m = _model(out)
    Pe, lat, lon = enh_to_ecef(E, N, H, m['crs'])
    up = np.array([math.cos(lat) * math.cos(lon), math.cos(lat) * math.sin(lon), math.sin(lat)])
    X = (Pe - m['t']) @ m['R'] / m['s']                                # 모델 좌표 (Rᵀ(P − t) / s)
    xc = np.einsum('nij,j->ni', m['Rq'], X) + m['Tq']                  # 사진마다 카메라 좌표
    u = np.full(len(xc), np.nan); v = u.copy(); r2 = u.copy(); ok = np.zeros(len(xc), bool)
    for c, P in m['cams'].items():
        k = m['cid'] == c
        if k.any():
            u[k], v[k], r2[k], ok[k] = _project(xc[k, 0], xc[k, 1], xc[k, 2], P, m['r2m'][c], PAD)
    d = m['Ce'] - Pe
    vert = d @ up
    horiz = np.linalg.norm(d - vert[:, None] * up, axis=1)
    ok &= vert > 0.5                                                  # 카메라가 그 자리보다 위
    cosz = np.clip(-(m['Ae'] @ up), -1, 1)
    with np.errstate(divide='ignore', invalid='ignore'):
        slope = horiz / vert
        rc = np.sqrt(r2 / np.array([m['r2m'][c] for c in m['cid']]))
        score = slope + EDGE_W * rc + OBL_W * np.sqrt(1 - cosz ** 2) / np.maximum(cosz, 0.05)
    idx = [i for i in np.argsort(np.where(ok, score, np.inf)) if ok[i]]
    out_l = []
    for i in idx:
        if len(out_l) >= n:
            break
        nm = m['names'][i]
        if not os.path.exists(os.path.join(pdir, nm)):
            continue
        w, h = m['cams'][int(m['cid'][i])][:2]
        out_l.append({'name': nm, 'u': round(float(u[i]), 2), 'v': round(float(v[i]), 2), 'w': int(w), 'h': int(h),
                      'score': round(float(score[i]), 4), 'dist': round(float(np.linalg.norm(d[i])), 2)})
    if not out_l:
        return {'ok': False, 'msg': '이 자리를 본 사진 없음' if not idx else '사진 파일 없음 · ' + pdir}
    return {'ok': True, 'photos': out_l, 'dir': pdir}


def photo_view(out, name, u, v, zoom=False):
    """사진 한 장 → 화면에 띄울 그림 (data URL · JPEG) + 그 자리 (그림 화소).
    zoom = False → 긴 변 VIEW_PX 로 줄인 전체 · True → 원본 화소 그대로 그 자리 둘레 ZOOM_W × ZOOM_H."""
    import cv2
    m = _model(out)
    if name not in m['names']:
        return {'ok': False, 'msg': '모델에 없는 사진'}
    pdir = photos_dir(out)
    path = os.path.join(pdir or '', name)
    if not pdir or not os.path.exists(path):
        return {'ok': False, 'msg': '사진 파일 없음'}
    i = m['names'].index(name)
    W, Hh = m['cams'][int(m['cid'][i])][:2]
    u, v = float(u), float(v)
    flag = cv2.IMREAD_IGNORE_ORIENTATION | (cv2.IMREAD_COLOR if zoom else cv2.IMREAD_REDUCED_COLOR_2)   # 카메라 자료와 같은 화소 (돌림 표시 무시)
    img = cv2.imdecode(np.fromfile(path, np.uint8), flag)
    if img is None:
        return {'ok': False, 'msg': '사진 못 읽음'}
    if zoom:
        zw, zh = min(ZOOM_W, img.shape[1]), min(ZOOM_H, img.shape[0])
        x0 = int(min(max(0, round(u - zw / 2)), img.shape[1] - zw)); y0 = int(min(max(0, round(v - zh / 2)), img.shape[0] - zh))
        img = img[y0:y0 + zh, x0:x0 + zw]
        x, y = u - x0, v - y0
    else:
        k = VIEW_PX / max(W, Hh)
        tw, th = max(1, int(round(W * k))), max(1, int(round(Hh * k)))
        img = cv2.resize(img, (tw, th), interpolation=cv2.INTER_AREA)
        x, y = u * tw / W, v * th / Hh
    okj, buf = cv2.imencode('.jpg', img, [cv2.IMWRITE_JPEG_QUALITY, 86])
    if not okj:
        return {'ok': False, 'msg': '그림 못 만듦'}
    return {'ok': True, 'name': name, 'url': 'data:image/jpeg;base64,' + base64.b64encode(buf.tobytes()).decode('ascii'),
            'w': int(img.shape[1]), 'h': int(img.shape[0]), 'x': round(x, 1), 'y': round(y, 1), 'zoom': bool(zoom)}
