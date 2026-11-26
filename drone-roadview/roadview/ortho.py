"""정사영상 = 원본 사진 재투영 (true ortho · JACK 1126 «정사 화질이 탁함 · Terra 는 확대해도 깨끗»).

칸마다 땅 위치 (E, N, DSM 표고) → 사진에 투영 (COLMAP 카메라 · OPENCV 왜곡) → 원본 JPG 에서 색.
  사진 고르기: 칸마다 점수가 가장 낮은 사진 = 땅 → 카메라 선의 기울기(수평 거리 / 높이) + 사진 가운데에서 거리
    (선이 연직에 가까울수록 건물 · 나무 가림과 기울어짐이 적음 · 같은 자리 경사 사진보다 수직 사진).
  이음: 두 사진 점수가 비슷한 곳만 짧게 섞음 — 이음은 사진 사이 경계라 타일 경계에 네모가 안 생김.
  좌표: (E, N, 표고) → 모델 좌표는 표본 점으로 맞춘 다항식 (pyproj 를 칸마다 안 돌림 · 왕복 오차 확인).
mapping.main 이 부름 (to_enh = mapping.to_5186_h · world = mapping._world — 같은 좌표계 전역을 쓰게 넘겨받음).
"""
from __future__ import annotations

import math
import os
import threading
import time
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor

import numpy as np

TILE = 512                  # 타일 한 변 (칸)
EDGE_W = 0.5                # 점수 = 기울기 + EDGE_W × 사진 가운데에서 거리 (정규 좌표)
OBL_W = 0.5                 #       + OBL_W × 카메라 축 기울기(tan) — 경사 사진은 수직 사진이 못 보는 곳만 (풀밭 등 보는 각도로 색이 달라 이음이 보임)
HOLE_M2 = 2500.0            # DSM 안쪽 빈칸(점이 없는 눈 지붕 · 물 등)을 이 넓이까지 둘레 높이로 메워 사진 색을 넣음
FEATHER_M = 3.0             # 가장 좋은 사진의 테두리 이만큼 안에서 둘째 사진으로 서서히 넘김 (찍은 범위 끝 · 경사 사진 끝 이음)
OCC_R = 6.0                 # 가림 검사: 땅 → 카메라 선을 이 수평 거리까지 DSM 과 견줌 (m)
OCC_TOL = 0.3               #   DSM 이 선보다 이만큼 높으면 가림 (DSM 잡음 여유)
OCC_STEP = 0.1              #   선 따라 재는 간격 m
OCC_PEN = 10.0              #   가린 사진 점수 벌점 (모든 사진이 가리면 덜 나쁜 것)
BLEND_M = 0.5               # 이음 섞기 반폭 m (노출 차이를 감춤 · 길면 DSM 이 틀린 곳에서 겹쳐 보임)
CELL_MIN = {'fast': 0.06, 'normal': 0.03, 'fine': 0.015}   # 정밀 = 사진 해상도 그대로 (GSD ≈ 1.7 cm · JACK 1126 «정밀이면 확실히 좋아져야»)     # 품질별 정사 칸 하한 m (사진 GSD 보다 잘게는 안 함)
KEEP = 1.2                  # 사진을 칸에 맞춰 줄일 때 남기는 여유 (줄인 사진 화소 = 칸 / KEEP)
MAX_PX = 400e6              # 정사 칸 수 한도 (RGBA 1.6 GB · 넘으면 칸을 키움 — 16 GB PC · TIFF 4 GB)
FIT_TOL = 0.001             # 좌표 다항식 왕복 오차 한도 m


def _avail_ram():
    try:
        import ctypes

        class _MS(ctypes.Structure):
            _fields_ = [('len', ctypes.c_ulong), ('load', ctypes.c_ulong)] + [(k, ctypes.c_ulonglong) for k in (
                'tot', 'avail', 'tpage', 'apage', 'tvirt', 'avirt', 'aext')]
        m = _MS(); m.len = ctypes.sizeof(_MS)
        if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m)):
            return float(m.avail)
    except Exception:                              # noqa: BLE001
        pass
    return 8e9


def _read_model(ws):
    """sparse_txt → 카메라 {id: (w, h, fx, fy, cx, cy, k1, k2, p1, p2)} · 사진 [(이름, 회전, 이동, 카메라 id)]."""
    cams = {}
    for line in open(os.path.join(ws, 'sparse_txt', 'cameras.txt'), encoding='utf-8'):
        if line.startswith('#') or not line.strip():
            continue
        p = line.split(); cid, model, w, h = int(p[0]), p[1], int(p[2]), int(p[3]); v = list(map(float, p[4:]))
        if model == 'OPENCV':
            fx, fy, cx, cy, k1, k2, p1, p2 = v[:8]
        elif model in ('PINHOLE', 'SIMPLE_PINHOLE', 'SIMPLE_RADIAL', 'RADIAL'):
            fx, fy, cx, cy = v[:4] if model == 'PINHOLE' else (v[0], v[0], v[1], v[2])
            k1 = v[3] if model in ('SIMPLE_RADIAL', 'RADIAL') else 0.0
            k2 = v[4] if model == 'RADIAL' else 0.0
            p1 = p2 = 0.0
        else:
            raise RuntimeError('카메라 모델 %s 는 정사 재투영 안 됨' % model)
        cams[cid] = (w, h, fx, fy, cx, cy, k1, k2, p1, p2)
    ims = []
    L = [line for line in open(os.path.join(ws, 'sparse_txt', 'images.txt'), encoding='utf-8') if not line.startswith('#')]
    for line in L[0::2]:
        p = line.split(None, 9)                    # 사진 이름에 빈칸이 있어도
        if len(p) < 10:
            continue
        w_, x, y, z = map(float, p[1:5])
        Rq = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - w_ * z), 2 * (x * z + w_ * y)],
                       [2 * (x * y + w_ * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w_ * x)],
                       [2 * (x * z - w_ * y), 2 * (y * z + w_ * x), 1 - 2 * (x * x + y * y)]])
        ims.append((p[9].rstrip('\r\n'), Rq, np.array(list(map(float, p[5:8]))), int(p[8])))
    return cams, ims


def _distort(x, y, k1, k2, p1, p2):
    r2 = x * x + y * y
    rad = 1 + r2 * (k1 + k2 * r2)
    return x * rad + 2 * p1 * x * y + p2 * (r2 + 2 * x * x), y * rad + p1 * (r2 + 2 * y * y) + 2 * p2 * x * y, r2


def _r2max(w, h, fx, fy, cx, cy, k1, k2, p1, p2):
    """사진 테두리를 왜곡 편 정규 좌표로 → 가장 큰 r² (이 밖은 왜곡 다항식이 되접혀 사진 안으로 잘못 들어올 수 있음)."""
    s = np.linspace(0, 1, 65)
    u = np.r_[s * w, s * w, np.zeros(65), np.full(65, float(w))]
    v = np.r_[np.zeros(65), np.full(65, float(h)), s * h, s * h]
    xd, yd = (u - cx) / fx, (v - cy) / fy
    x, y = xd.copy(), yd.copy()
    for _ in range(200):
        dx, dy, _r = _distort(x, y, k1, k2, p1, p2)
        x, y = x + (xd - dx), y + (yd - dy)
    dx, dy, _r = _distort(x, y, k1, k2, p1, p2)
    if np.abs(dx - xd).max() * fx > 0.01 or np.abs(dy - yd).max() * fy > 0.01:
        raise RuntimeError('카메라 왜곡을 되풀 수 없음')
    return float((x * x + y * y).max())


def _phi(e, n, h, deg):
    c = [np.ones_like(e), e, n, h, e * e, e * n, e * h, n * n, n * h, h * h]
    if deg >= 3:
        c += [e * e * e, e * e * n, e * e * h, e * n * n, e * n * h, e * h * h, n * n * n, n * n * h, n * h * h, h * h * h]
    return np.stack(c)


def _project(xc, yc, zc, P, r2m, pad):
    """카메라 좌표 → (u, v 원본 화소 · COLMAP 기준 = 왼쪽 위 모서리 0) · 정규 r² · 사진 안."""
    w, h, fx, fy, cx, cy, k1, k2, p1, p2 = P
    with np.errstate(divide='ignore', invalid='ignore'):
        z = np.where(zc > 1e-9, zc, np.nan)
        xd, yd, r2 = _distort(xc / z, yc / z, k1, k2, p1, p2)
        u = fx * xd + cx; v = fy * yd + cy
        ok = (r2 < r2m) & (u >= pad) & (u <= w - pad) & (v >= pad) & (v <= h - pad)
    return u, v, r2, ok


def _morton(i, j):
    z = 0
    for b in range(16):
        z |= (((i >> b) & 1) << (2 * b + 1)) | (((j >> b) & 1) << (2 * b))
    return z


def true_ortho(ws, photos, s, R, t, dsm, filled, dx0, dy1, dc, outs, quality, to_enh, world, fill=None, cell_out=0.0, log=print):
    """정사영상.tif (RGBA · LZW · 투명 = 자료 없음) + tfw · prj + 정사영상_미리보기.png.
    dsm · filled · dx0 · dy1 · dc = mapping.ortho_dsm 의 DSM (칸 dc) · fill = mapping.fill_pull_push (안쪽 빈칸 메우기)
    cell_out = 정사 칸 m (0 = 사진 GSD 와 품질로).
    → 요약 dict (칸 · GSD · 크기 · 초 · 쓴 사진 수 · 빈칸 비율 …)."""
    import cv2
    from PIL import Image
    T0 = time.time()
    Image.MAX_IMAGE_PIXELS = None
    cams, ims = _read_model(ws)
    ims = [im for im in ims if im[3] in cams and os.path.exists(os.path.join(photos, im[0]))]
    if not ims:
        raise RuntimeError('사진 폴더에 모델 사진이 없음 (%s)' % photos)
    nc = len(ims)

    # DSM → 칸 높이: 1 m 보다 작은 빈칸을 메우고 (주변 평균) 홀로 튄 점은 가운데값 5×5 로 지움 (높은 점 DSM 의 잡음)
    Hd, Wd = dsm.shape
    Zd = np.where(filled, dsm, 0).astype(np.float32)
    k = max(3, int(round(1.0 / dc)) | 1)
    Vd = cv2.morphologyEx(filled.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((k, k), np.uint8))
    new = (Vd > 0) & ~filled
    if new.any():
        num = cv2.boxFilter(Zd, -1, (k + 2, k + 2), normalize=False)
        den = cv2.boxFilter(filled.astype(np.float32), -1, (k + 2, k + 2), normalize=False)
        Zd[new] = num[new] / np.maximum(den[new], 1.0)
        Vd[new & (den <= 0)] = 0
        del num, den
    del new
    n_hole = 0
    if fill is not None:                           # 둘레가 모두 DSM 인 빈칸 (사진 테두리에 닿지 않음 · HOLE_M2 까지)
        _n, lab, st, _c = cv2.connectedComponentsWithStats((Vd == 0).astype(np.uint8), connectivity=4)
        inner = ((st[:, 0] > 0) & (st[:, 1] > 0) & (st[:, 0] + st[:, 2] < Wd) & (st[:, 1] + st[:, 3] < Hd)
                 & (st[:, 4] * dc * dc <= HOLE_M2))
        inner[0] = False                           # 0 = DSM 있는 칸
        if inner.any():                            # 높이는 둘레 1 m 안 가장 낮은 곳으로 (빈칸은 대개 건물 뒤 가려진 땅 · 물)
            hole = inner[lab]
            low = cv2.erode(np.where(Vd > 0, Zd, 1e9).astype(np.float32), np.ones((k, k), np.uint8))
            Zd = np.where(hole, fill(np.where(Vd > 0, low, 0).astype(np.float32), Vd > 0), Zd).astype(np.float32)
            del low
            Vd[hole] = 1
            n_hole = int(inner.sum())
            del hole
        del lab
    full5 = cv2.erode(Vd, np.ones((5, 5), np.uint8)) > 0
    Zd = np.where(full5, cv2.medianBlur(Zd, 5), Zd)
    del full5
    zv = Zd[Vd > 0]
    if not len(zv):
        raise RuntimeError('DSM 이 비었음')
    # 높이 읽기(겹선형)용: 빈칸을 이웃 값으로 두 칸 넓혀 둠 — 빈칸 0 이 섞여 가장자리 높이가 끌려 내려가던 것 (1126 검토 · 50 m → 35 m)
    Zi = np.where(Vd > 0, Zd, 0).astype(np.float32); Vi = (Vd > 0).astype(np.float32)
    for _ in range(2):
        num = cv2.boxFilter(Zi * Vi, -1, (3, 3), normalize=False); den = cv2.boxFilter(Vi, -1, (3, 3), normalize=False)
        add = (Vi == 0) & (den > 0)
        Zi[add] = num[add] / den[add]; Vi[add] = 1
        del num, den, add
    del Vi
    hlo, hhi = float(np.percentile(zv, 0.01)) - 5.0, float(np.percentile(zv, 99.99)) + 5.0
    del zv
    # 가림 검사용: DSM (없는 칸 = 아주 낮게) · 1 m 칸 최대 높이를 OCC_R 만큼 넓힌 것 (둘레에 높은 것이 없으면 검사 안 함)
    bk = max(1, int(round(1.0 / dc))); hb, wb = -(-Hd // bk), -(-Wd // bk)
    Zo = np.full((hb * bk, wb * bk), -1e9, np.float32); Zo[:Hd, :Wd] = np.where(Vd > 0, Zd, -1e9)
    rk = 2 * int(math.ceil(OCC_R / (bk * dc))) + 1
    Zmax = cv2.dilate(Zo.reshape(hb, bk, wb, bk).max((1, 3)), np.ones((rk, rk), np.uint8))

    # 카메라: 모델 좌표 중심 · 축 → 도면 좌표 (pyproj 한 번)
    Cm = np.array([-Rq.T @ T for _, Rq, T, _c in ims])
    Am = np.array([Rq.T @ np.array([0.0, 0.0, 1.0]) for _, Rq, _T, _c in ims])
    Xm = np.array([Rq.T @ np.array([1.0, 0.0, 0.0]) for _, Rq, _T, _c in ims])   # 가로축 끝도 (일자 비행 한 줄이어도 1차 맞춤이 풀리게 · 1126 검토)
    Ex, Nx, Hx = to_enh(s * (np.vstack([Cm, Cm + Am * (10.0 / s), Cm + Xm * (10.0 / s)]) @ R.T) + t)
    camE, camN, camH = Ex[:nc], Nx[:nc], Hx[:nc]
    cosz = np.clip((Hx[:nc] - Hx[nc:2 * nc]) / 10.0, -1, 1)
    nadir = cosz > math.cos(math.radians(10))     # 축이 연직에서 10° 안
    obl = OBL_W * np.sqrt(1 - cosz ** 2) / np.maximum(cosz, 0.05)     # 경사 사진 벌점

    # (E, N, 표고) → 모델 좌표 다항식: 카메라로 대충 맞춘 1차 식으로 모델 점을 깔고 → 정확한 정변환 → 2차 (안 되면 3차)
    E0, N0 = dx0 + Wd * dc / 2, dy1 - Hd * dc / 2; H0 = (hlo + hhi) / 2; LS = 1000.0
    G = np.c_[(Ex - E0) / LS, (Nx - N0) / LS, (Hx - H0) / LS, np.ones(3 * nc)]
    aff = np.linalg.lstsq(G, np.vstack([Cm, Cm + Am * (10.0 / s), Cm + Xm * (10.0 / s)]), rcond=None)[0]
    pad_m = 10.0

    def grid(nx, off):
        ge = dx0 - pad_m + (np.arange(nx) + off) / nx * (Wd * dc + 2 * pad_m)
        gn = dy1 + pad_m - (np.arange(nx) + off) / nx * (Hd * dc + 2 * pad_m)
        gh = hlo + (np.arange(6) + off) / 6 * (hhi - hlo)
        e, n, h = [a.ravel() for a in np.meshgrid(ge, gn, gh, indexing='ij')]
        X = np.c_[(e - E0) / LS, (n - N0) / LS, (h - H0) / LS, np.ones(len(e))] @ aff
        e2, n2, h2 = to_enh(s * (X @ R.T) + t)
        return X, (np.asarray(e2) - E0) / LS, (np.asarray(n2) - N0) / LS, (np.asarray(h2) - H0) / LS
    Xa, ea, na, ha = grid(16, 0.0)
    Xt, et, nt, ht = grid(15, 0.5)                 # 맞춤에 안 쓴 점으로 왕복 오차
    for deg in (2, 3):
        A = np.linalg.lstsq(_phi(ea, na, ha, deg).T, Xa, rcond=None)[0]    # (K, 3)
        fit_err = float(s * np.sqrt(((_phi(et, nt, ht, deg).T @ A - Xt) ** 2).sum(1)).max())
        if fit_err < FIT_TOL:
            break
    else:
        raise RuntimeError('좌표 다항식 왕복 오차 %.1f mm' % (fit_err * 1000))
    Mc = np.empty((nc, 3, A.shape[0]))
    for i, (_n, Rq, T, _c) in enumerate(ims):
        Mc[i] = Rq @ A.T
        Mc[i, :, 0] += T
    Pc = [cams[c] for _n, _R, _T, c in ims]
    r2m = {cid: _r2max(*cams[cid]) for cid in {c for *_x, c in ims}}
    R2 = np.array([r2m[c] for *_x, c in ims])
    PA = [np.array([p[j] for p in Pc], np.float64)[:, None] for j in range(10)]     # 카메라별 내부표정 (한꺼번에 투영)
    f_mean = np.array([(p[2] + p[3]) / 2 for p in Pc])

    # 사진 GSD = (카메라 높이 − 땅) / 초점 · 수직 사진 가운데값 → 정사 칸
    rr = np.clip(((dy1 - camN) / dc).astype(int), 0, Hd - 1); cc = np.clip(((camE - dx0) / dc).astype(int), 0, Wd - 1)
    sel = (Vd[rr, cc] > 0) & (nadir if nadir.sum() >= 3 else True)
    hgt = (camH - Zd[rr, cc])[sel]
    if not len(hgt) or np.median(hgt) <= 1:
        raise RuntimeError('카메라 아래 땅 높이를 못 잼')
    hmed = float(np.median(hgt))
    gsd = float(np.median(hgt / f_mean[sel]))
    co = cell_out or max(gsd, CELL_MIN.get(quality, 0.03))
    co = math.ceil(co * 1000 - 1e-6) / 1000.0
    x0o = math.floor(dx0 / co) * co; y1o = math.ceil(dy1 / co) * co
    Wo = int(math.ceil((dx0 + Wd * dc - x0o) / co)); Ho = int(math.ceil((y1o - (dy1 - Hd * dc)) / co))
    avail = _avail_ram()
    cap = min(MAX_PX, 0.25 * avail / 4)
    if Wo * Ho > cap:                              # 너무 크면 칸을 키움 (메모리 · TIFF 한도)
        co = math.ceil(co * math.sqrt(Wo * Ho / cap) * 1000) / 1000.0
        x0o = math.floor(dx0 / co) * co; y1o = math.ceil(dy1 / co) * co
        Wo = int(math.ceil((dx0 + Wd * dc - x0o) / co)); Ho = int(math.ceil((y1o - (dy1 - Hd * dc)) / co))
        log('  정사 칸이 너무 많아 %.1f cm 로 키움' % (co * 100))
    scale = KEEP * gsd / co                        # 사진을 칸에 맞게 줄여 읽음 (덜 계단 · 메모리 · 속도) · 거의 같으면 원본 그대로
    scale = 1.0 if scale > 0.9 else scale
    margin = 2 * BLEND_M * (1 + EDGE_W) / hmed     # 점수 차 → 이음에서 거리 (두 사진 점수가 m 마다 (1+EDGE_W)/높이 씩 벌어짐)
    fpx = FEATHER_M / gsd                          # 테두리 섞기 폭 (원본 화소)

    # 풀린 사진 기억 (LRU · 줄인 크기) — 타일은 공간 순서라 이웃 타일이 같은 사진을 씀
    nthr = max(1, min(os.cpu_count() or 4, 16, int(0.15 * avail / 150e6)))
    budget = min(3e9, max(0.5e9, 0.2 * avail))
    lock = threading.Lock(); store = OrderedDict(); loading = {}; held = [0]
    red = 2 if scale <= 0.5 else 1
    flag = (cv2.IMREAD_REDUCED_COLOR_2 if red == 2 else cv2.IMREAD_COLOR) | cv2.IMREAD_IGNORE_ORIENTATION

    def load(c):
        w, h = Pc[c][0], Pc[c][1]
        img = cv2.imdecode(np.fromfile(os.path.join(photos, ims[c][0]), np.uint8), flag)
        if img is None or img.shape[:2] != ((h + red - 1) // red, (w + red - 1) // red):
            raise RuntimeError('사진 %s 크기가 카메라와 다름 (%s)' % (ims[c][0], None if img is None else img.shape[:2]))
        tw, th = max(1, int(round(w * scale))), max(1, int(round(h * scale)))
        if (th, tw) != img.shape[:2]:
            img = cv2.resize(img, (tw, th), interpolation=cv2.INTER_AREA)
        return img, tw / w, th / h

    def photo(c):
        while True:
            with lock:
                if c in store:
                    store.move_to_end(c)
                    return store[c]
                ev = loading.get(c)
                mine = ev is None
                if mine:
                    ev = loading[c] = threading.Event()
            if not mine:
                ev.wait()
                continue
            item = None
            try:
                item = load(c)
                return item
            finally:
                with lock:
                    if item is not None:
                        store[c] = item; held[0] += item[0].nbytes
                        while held[0] > budget and len(store) > 1:
                            held[0] -= store.popitem(last=False)[1][0].nbytes
                    loading.pop(c).set()

    out = np.zeros((Ho, Wo, 4), np.uint8)
    used = set()

    def do_tile(ij):
        i, j = ij
        r0, c0 = i * TILE, j * TILE
        th, tw = min(TILE, Ho - r0), min(TILE, Wo - c0)
        E = x0o + (c0 + np.arange(tw) + 0.5) * co; N = y1o - (r0 + np.arange(th) + 0.5) * co
        fx_ = (E - dx0) / dc - 0.5; fy_ = (dy1 - N) / dc - 0.5
        a0 = max(0, int(math.floor(fx_[0])) - 1); a1 = min(Wd, int(math.ceil(fx_[-1])) + 2)
        b0 = max(0, int(math.floor(fy_[0])) - 1); b1 = min(Hd, int(math.ceil(fy_[-1])) + 2)
        if a1 - a0 < 2 or b1 - b0 < 2:
            return 0, 0, 0
        mx = np.ascontiguousarray(np.broadcast_to((fx_ - a0).astype(np.float32), (th, tw)))
        my = np.ascontiguousarray(np.broadcast_to((fy_ - b0).astype(np.float32)[:, None], (th, tw)))
        idx = np.flatnonzero(cv2.remap(Vd[b0:b1, a0:a1], mx, my, cv2.INTER_NEAREST, borderMode=cv2.BORDER_CONSTANT, borderValue=0))
        if not len(idx):
            return 0, 0, 0
        hz = cv2.remap(Zi[b0:b1, a0:a1], mx, my, cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE).ravel()[idx].astype(np.float64)
        e = E[idx % tw]; n = N[idx // tw]
        Phi = _phi((e - E0) / LS, (n - N0) / LS, (hz - H0) / LS, deg)
        # 후보 사진: 표본 칸마다 점수 앞 3장 (모든 사진을 한꺼번에 · 사진 테두리를 조금 넓혀서)
        q = np.unique(np.linspace(0, len(idx) - 1, min(len(idx), 400)).astype(int))
        X = np.einsum('cik,km->cim', Mc, Phi[:, q])
        u, v, r2, ok = _project(X[:, 0], X[:, 1], X[:, 2], PA, R2[:, None] * 1.1, -0.03 * PA[0])
        dh = camH[:, None] - hz[q]
        sc = np.where(ok & (dh > 0.5), np.hypot(camE[:, None] - e[q], camN[:, None] - n[q]) / np.maximum(dh, 0.5) + EDGE_W * np.sqrt(r2) + obl[:, None], np.inf)
        top = np.argsort(sc, 0)[:3]
        cand = np.unique(top[np.isfinite(np.take_along_axis(sc, top, 0))])
        if not len(cand):
            return len(idx), 0, 0
        # 칸마다: 후보마다 투영 · 점수 → 가장 좋은 사진 + 둘째 (이음 섞기)
        m = len(idx); nk = len(cand); ar = np.arange(m)
        S = np.full((nk, m), np.inf, np.float32); U = np.zeros((nk, m), np.float32); V = np.zeros((nk, m), np.float32)
        B = np.zeros((nk, m), np.float32)          # 사진 테두리까지 원본 화소
        for a, c in enumerate(cand):
            xc, yc, zc = np.einsum('ik,km->im', Mc[c], Phi)
            u, v, r2, ok = _project(xc, yc, zc, Pc[c], R2[c], 1.0)
            dh = camH[c] - hz
            ok &= dh > 0.5
            S[a] = np.where(ok, np.hypot(camE[c] - e, camN[c] - n) / np.maximum(dh, 0.5) + EDGE_W * np.sqrt(r2) + obl[c], np.inf)
            U[a] = u; V[a] = v
            B[a] = np.minimum(np.minimum(u, Pc[c][0] - u), np.minimum(v, Pc[c][1] - v))

        def top2():
            if nk == 1:
                return np.zeros(m, np.int64), np.zeros(m, np.int64), S[0], np.full(m, np.inf, np.float32)
            pp = np.argpartition(S, 1, axis=0)[:2]
            sp = np.take_along_axis(S, pp, 0); sw = sp[0] > sp[1]
            return np.where(sw, pp[1], pp[0]), np.where(sw, pp[0], pp[1]), sp.min(0), sp.max(0)

        def blocked(c, px):                        # 땅 → 카메라 선이 DSM (건물 · 나무) 에 막힘
            ep, nq, hp = e[px], n[px], hz[px]
            dE, dN = camE[c] - ep, camN[c] - nq
            hor = np.maximum(np.hypot(dE, dN), 1e-6); rise = (camH[c] - hp) / hor    # 수평 1 m 마다 선이 오르는 높이
            nst = np.ceil(np.minimum(rel[px] / np.maximum(rise, 1e-6), OCC_R) / OCC_STEP).astype(np.int64)
            ux, uy = dE / hor, dN / hor
            occ = np.zeros(len(px), bool)
            for k in range(1, int(nst.max()) + 1):
                act = np.flatnonzero((nst >= k) & ~occ)
                if not len(act):
                    break
                d = k * OCC_STEP
                ri = np.floor((dy1 - (nq[act] + uy[act] * d)) / dc).astype(np.int64)
                ci = np.floor((ep[act] + ux[act] * d - dx0) / dc).astype(np.int64)
                zz = Zo[np.clip(ri, 0, Hd - 1), np.clip(ci, 0, Wd - 1)]
                zz = np.where((ri >= 0) & (ri < Hd) & (ci >= 0) & (ci < Wd), zz, -1e9)
                occ[act] = zz > hp[act] + rise[act] * d + OCC_TOL
            return occ

        b1i, b2i, s1, s2 = top2()
        # 가림: 둘레 OCC_R 안에 OCC_TOL 보다 높은 것이 있는 칸만 · 가장 좋은 두 장을 재고 막히면 벌점 → 다시 고름 (3번까지)
        rel = Zmax[np.clip(np.floor((dy1 - n) / dc).astype(np.int64) // bk, 0, Zmax.shape[0] - 1),
                   np.clip(np.floor((e - dx0) / dc).astype(np.int64) // bk, 0, Zmax.shape[1] - 1)] - hz
        need = rel > OCC_TOL
        nocc = 0
        if need.any():
            tested = np.zeros((nk, m), bool)
            for _ in range(3):
                more = False
                for bi in (b1i, b2i):
                    for a in range(nk):
                        px = np.flatnonzero((bi == a) & need & ~tested[a] & np.isfinite(S[a]))
                        if len(px):
                            more = True; tested[a, px] = True
                            hit = px[blocked(cand[a], px)]
                            S[a, hit] += OCC_PEN; nocc += len(hit)
                if not more:
                    break
                b1i, b2i, s1, s2 = top2()
        has = np.isfinite(s1)
        # 섞기: 두 점수가 margin 안이면 반반까지 · 가장 좋은 사진 테두리 FEATHER_M 안이면 둘째로 서서히 (둘째가 안 가릴 때)
        ea = np.clip(B[b1i, ar] / fpx, 0, 1); eb = np.clip(B[b2i, ar] / fpx, 0, 1)
        two = has & np.isfinite(s2) & (b2i != b1i)
        with np.errstate(invalid='ignore'):
            d12 = np.where(two, s2 - s1, np.inf)
            w2 = 0.5 * np.clip(1 - d12 / margin, 0, 1)
            wA = np.where(has, ea * (1 - w2), 0)
            wB = np.where(two, eb * np.maximum(w2, np.where(d12 < OCC_PEN / 2, 1 - ea, 0)), 0)
            tot = wA + wB
            wA = np.where(tot > 1e-6, wA / tot, 1.0).astype(np.float32); wB = np.where(tot > 1e-6, wB / tot, 0.0).astype(np.float32)
        acc = np.zeros((m, 3), np.float32)
        for a in np.unique(np.r_[b1i[has], b2i[two & (wB > 0)]]):
            wa = np.where(has & (b1i == a), wA, 0) + np.where(two & (b2i == a), wB, 0)
            pick = np.flatnonzero(wa > 0)
            if not len(pick):
                continue
            img, sx, sy = photo(cand[a])
            kk = len(pick); rows = -(-kk // 1024)       # remap 한 변 32767 한도 → 1024 폭으로 접음
            mxp = np.zeros(rows * 1024, np.float32); myp = np.zeros(rows * 1024, np.float32)
            mxp[:kk] = U[a, pick] * sx - 0.5; myp[:kk] = V[a, pick] * sy - 0.5     # COLMAP 모서리 0 → 화소 가운데 0
            col = cv2.remap(img, mxp.reshape(rows, 1024), myp.reshape(rows, 1024), cv2.INTER_CUBIC,
                            borderMode=cv2.BORDER_REPLICATE).reshape(-1, 3)[:kk]
            acc[pick] += wa[pick, None] * col
            used.add(int(cand[a]))
        tile = np.zeros((th * tw, 4), np.uint8)
        tile[idx[has], :3] = np.clip(acc[has][:, ::-1] + 0.5, 0, 255).astype(np.uint8)     # BGR → RGB
        tile[idx[has], 3] = 255
        out[r0:r0 + th, c0:c0 + tw] = tile.reshape(th, tw, 4)
        return m, int(has.sum()), nocc

    tiles = sorted(((i, j) for i in range(-(-Ho // TILE)) for j in range(-(-Wo // TILE))), key=lambda ij: _morton(*ij))
    nthr0 = cv2.getNumThreads()
    cv2.setNumThreads(1)                           # 타일 병렬 — OpenCV 안쪽 스레드와 겹치지 않게
    try:
        with ThreadPoolExecutor(nthr) as ex:
            res = list(ex.map(do_tile, tiles))
    finally:
        cv2.setNumThreads(nthr0)
    nval, ngot, nocc = (sum(r[k] for r in res) for k in range(3))
    t_proj = time.time() - T0
    im = Image.fromarray(out)
    im.save(os.path.join(outs, '정사영상.tif'), compression='tiff_lzw')
    world(outs, '정사영상', x0o, y1o, co)
    kp = max(1, max(Wo, Ho) // 2048)
    im.resize((max(1, Wo // kp), max(1, Ho // kp)), reducing_gap=3.0).save(os.path.join(outs, '정사영상_미리보기.png'))
    return {'cell': co, 'gsd': gsd, 'height': hmed, 'size': (Wo, Ho), 'sec': time.time() - T0, 'sec_proj': t_proj,
            'used': len(used), 'photos': nc, 'nadir': int(nadir.sum()), 'empty': 1.0 - ngot / max(1, nval), 'holes': n_hole, 'occluded': nocc,
            'fit_mm': fit_err * 1000, 'deg': deg, 'scale': scale, 'threads': nthr, 'cache_mb': budget / 1e6}
