"""가우시안 스플래팅 (3D Gaussian Splatting) 결과물 — JACK 1126 «DJI Terra 처럼 가우시안 스플래팅도».

엔진 = Brush 0.3.0 (Apache-2.0 · github.com/ArthurBrussee/brush · Rust + WebGPU(wgpu → Vulkan/DX12) · 실행 파일 하나 ·
  CUDA · 파이썬 없이 · RTX 50 포함 NVIDIA / AMD / Intel). 1126 견줌에서 뺀 것: OpenSplat (AGPL · GitHub 배포에 Windows 실행
  파일 없음 → libtorch + CUDA 빌드) · gsplat / nerfstudio (파이썬 3.10 · CUDA 12.4 휠만 → 파이썬 3.14 · RTX 50 은 CUDA 빌드) ·
  Inria 원본 (학습 코드 비상업 라이선스) · LichtFeld Studio (GPL-3 · GitHub 배포에 실행 파일 없음).

차례: 왜곡 편 사진 (work/dense · COLMAP PINHOLE · 모델 틀 = RTK 로 맞춘 동 · 북 · 위 m) → Brush 학습 (work/gs/brush.ply)
  → 도면 좌표 (위치 = 모델 → ECEF → CRS + EGM2008 표고 · 회전 · 크기 · 색 SH = 그 자리 선형 부분으로 함께 돌림)
  → 결과물/가우시안.ply (표준 3DGS PLY · SH 3차 · 다른 보기 프로그램이 읽는 꼴)
  + data/gs/splats.bin (보기용 · 개마다 32 B · antimatter15 .splat 꼴 · 원점 기준 · 바탕 색만) + data/gs/gs.json.
가우시안.ply 좌표: float32 는 도면 좌표 (북 40만 m 대) 를 3 cm 칸으로밖에 못 담아 x = E − 원점E · y = N − 원점N · z = 표고 (m) ·
  원점은 머리말 comment 와 정보.txt 에 (LAS 의 offset 과 같은 뜻).
"""
from __future__ import annotations

import hashlib
import json
import math
import os
import re
import shutil
import subprocess
import time
from pathlib import Path

ENGINE = 'brush'
ENGINE_VER = '0.3.0'
ENGINE_URL = 'https://github.com/ArthurBrussee/brush/releases/download/v0.3.0/brush-app-x86_64-pc-windows-msvc.zip'
ENGINE_SHA256 = 'b68e3e9cf052d51bf3ee30776fa5a364de7f2ba13b58443128ff797bb7bcfcd6'      # 배포 쪽 .sha256 (1126 받은 것과 같음)
ENGINE_MB = 151
GS_VERSION = '1'            # 변환 · 보기 자료 꼴을 바꾸면 올림 (학습은 그대로 · 변환만 다시)
NO_WINDOW = 0x08000000 if os.name == 'nt' else 0
# 품질 → (학습 걸음 · 사진 긴 변 px · 가우시안 최대 개수) — 1126 80장 RTX 5070 Ti 실측 (8장마다 1장 평가):
#   낮음 75초 · 18만 개 · SSIM 0.745 / 보통 280초 · 74만 개 · SSIM 0.802 / 높음 1130초 · 293만 개 · SSIM 0.815 /
#   매우 높음 19400걸음까지 20분 · 586만 개 (그래픽 메모리 12 GB → 16 GB 카드는 _vram_mb 로 약 578만 개에서 멈춤 · 끝까지 약 66분 어림)
QUALITY = {'low': (7000, 1280, 1500000), 'normal': (15000, 1600, 3000000),
           'high': (30000, 1920, 5000000), 'ultra': (50000, 2640, 8000000)}
QUALITY_NAME = {'low': '낮음', 'normal': '보통', 'high': '높음', 'ultra': '매우 높음'}
QUALITY_SEC = {'low': 75, 'normal': 280, 'high': 1130, 'ultra': 4000}     # 80장 학습 초 (매우 높음 = 19400걸음 20분 실측으로 어림 · 진행 시간 무게)
WEB_MAX = 6000000           # 보기 자료 최대 개수 (중요한 것부터 · 32 B 씩 ≈ 190 MB)
WEB_MIN_ALPHA = 4 / 255.0   # 보기 자료: 거의 투명한 것은 뺌 (가우시안.ply 는 1/255 아래만 뺌)
SH_C0 = 0.28209479177387814

_GPU = None


def _engine_dirs():
    from .mapping import ENGINE_DIRS          # 엔진 자리는 매핑과 하나로 (%LOCALAPPDATA%\DH.Roadview\engines · RV_ENGINE_DIRS)
    return ENGINE_DIRS


def find_engine():
    """brush_app.exe 경로 · 없으면 None."""
    for d in _engine_dirs():
        p = Path(d) / ENGINE / 'brush_app.exe'
        if p.exists():
            return str(p)
    return None


def _gpus():
    """그래픽카드 이름 (Windows 화면 장치 목록 · 바로 · 하위 프로세스 없이)."""
    global _GPU
    if _GPU is None:
        names = []
        if os.name == 'nt':
            try:
                import ctypes
                from ctypes import wintypes

                class DD(ctypes.Structure):
                    _fields_ = [('cb', wintypes.DWORD), ('DeviceName', wintypes.WCHAR * 32), ('DeviceString', wintypes.WCHAR * 128),
                                ('StateFlags', wintypes.DWORD), ('DeviceID', wintypes.WCHAR * 128), ('DeviceKey', wintypes.WCHAR * 128)]
                i = 0
                while i < 16:
                    d = DD(); d.cb = ctypes.sizeof(d)
                    if not ctypes.windll.user32.EnumDisplayDevicesW(None, i, ctypes.byref(d), 0):
                        break
                    if d.DeviceString and d.DeviceString not in names:
                        names.append(d.DeviceString)
                    i += 1
            except Exception:                  # noqa: BLE001
                pass
        _GPU = names
    return _GPU


def available():
    """(되나 bool, 이유 str) — 앱 매핑 화면이 부름 (가볍게 · 내려받지 않음)."""
    import platform
    if os.name != 'nt':
        return False, 'Windows 에서만'
    if platform.machine().lower() not in ('amd64', 'x86_64'):
        return False, '64비트 Windows 에서만'
    real = [g for g in _gpus() if not re.search(r'Basic Display|Basic Render|Remote|Virtual|Parsec|Citrix|Mirage|DisplayLink', g, re.I)]
    if not real:
        return False, '그래픽카드 없음 → 가우시안 학습 못 함'
    if find_engine():
        return True, '%s · Brush %s' % (real[0], ENGINE_VER)
    return True, '%s · 처음 쓸 때 엔진 내려받기 (약 %d MB)' % (real[0], ENGINE_MB)


def install_engine(progress=print):
    """처음 한 번 Brush 내려받기 (공식 GitHub Releases · sha256 확인) → engines\\brush\\brush_app.exe."""
    exe = find_engine()
    if exe:
        return exe
    import urllib.request
    import zipfile
    dst = Path(_engine_dirs()[0])
    dst.mkdir(parents=True, exist_ok=True)
    z = dst / 'brush.zip'
    tmp = dst / 'brush.tmp'
    shutil.rmtree(tmp, ignore_errors=True)
    progress('엔진 내려받기 · Brush %s (약 %d MB)' % (ENGINE_VER, ENGINE_MB))
    h = hashlib.sha256()
    with urllib.request.urlopen(ENGINE_URL, timeout=60) as r, open(z, 'wb') as fp:
        while True:
            b = r.read(1 << 20)
            if not b:
                break
            h.update(b); fp.write(b)
    if h.hexdigest() != ENGINE_SHA256:
        z.unlink()
        raise RuntimeError('엔진 파일이 배포본과 다름 (sha256) → 다시 받기')
    progress('엔진 풀기 · Brush')
    with zipfile.ZipFile(z) as zf:
        zf.extractall(tmp)
    z.unlink()
    hit = next(iter(tmp.rglob('brush_app.exe')), None)
    if hit is None:
        raise RuntimeError('엔진 zip 안에 brush_app.exe 없음')
    tgt = dst / ENGINE
    shutil.rmtree(tgt, ignore_errors=True)
    shutil.move(str(hit.parent), str(tgt))        # 실행 파일 · LICENSE · README 를 한 번에 제자리로
    shutil.rmtree(tmp, ignore_errors=True)
    return find_engine()


# ---------------------------------------------------------------- PLY
def read_ply(path):
    """3DGS PLY (binary little endian · float 속성) → ({이름: 배열}, 개수). Brush 는 속성을 이름 차례로 씀 → 이름으로 읽음."""
    import numpy as np
    with open(path, 'rb') as fp:
        head = b''
        while b'end_header' not in head:
            line = fp.readline()
            if not line:
                raise ValueError('PLY 머리말 끝 없음')
            head += line
        off = fp.tell()
    lines = head.decode('latin1').splitlines()
    if 'format binary_little_endian 1.0' not in lines:
        raise ValueError('binary_little_endian PLY 만')
    n = int(next(l for l in lines if l.startswith('element vertex')).split()[2])
    TY = {'float': '<f4', 'float32': '<f4', 'double': '<f8', 'uchar': 'u1', 'uint8': 'u1', 'int': '<i4', 'uint': '<u4'}
    props = [(l.split()[2], TY[l.split()[1]]) for l in lines if l.startswith('property')]
    a = np.fromfile(path, dtype=np.dtype(props), count=n, offset=off)
    return {k: a[k] for k, _ in props}, n


# 3DGS 표준 차례 (Inria 3DGS · SuperSplat · antimatter15 등이 읽는 꼴)
def _std_props(nrest):
    return (['x', 'y', 'z', 'nx', 'ny', 'nz', 'f_dc_0', 'f_dc_1', 'f_dc_2'] + ['f_rest_%d' % i for i in range(nrest)]
            + ['opacity', 'scale_0', 'scale_1', 'scale_2', 'rot_0', 'rot_1', 'rot_2', 'rot_3'])


def write_ply(path, cols, comments=()):
    """{이름: 배열} → 표준 3DGS PLY (float32 · 표준 차례)."""
    import numpy as np
    nrest = sum(1 for k in cols if k.startswith('f_rest_'))
    names = _std_props(nrest)
    n = len(cols['x'])
    A = np.empty((n, len(names)), dtype='<f4')
    for j, k in enumerate(names):
        A[:, j] = cols[k] if k in cols else 0.0
    head = ['ply', 'format binary_little_endian 1.0'] + ['comment ' + c for c in comments] + ['element vertex %d' % n]
    head += ['property float %s' % k for k in names] + ['end_header']
    tmp = path + '.tmp'
    with open(tmp, 'wb') as fp:
        fp.write(('\n'.join(head) + '\n').encode('ascii'))
        A.tofile(fp)
    os.replace(tmp, path)


# ---------------------------------------------------------------- 돌림 (위치는 점마다 정확히 · 회전 · 크기 · SH 는 그 자리 선형 부분)
def _quat_of(R):
    """회전 행렬 → 사원수 (w, x, y, z)."""
    import numpy as np
    w = math.sqrt(max(0.0, 1 + R[0, 0] + R[1, 1] + R[2, 2])) / 2
    x = math.sqrt(max(0.0, 1 + R[0, 0] - R[1, 1] - R[2, 2])) / 2
    y = math.sqrt(max(0.0, 1 - R[0, 0] + R[1, 1] - R[2, 2])) / 2
    z = math.sqrt(max(0.0, 1 - R[0, 0] - R[1, 1] + R[2, 2])) / 2
    x = math.copysign(x, R[2, 1] - R[1, 2]); y = math.copysign(y, R[0, 2] - R[2, 0]); z = math.copysign(z, R[1, 0] - R[0, 1])
    q = np.array([w, x, y, z]); return q / np.linalg.norm(q)


def _sh_basis(d):
    """3DGS 의 실수 SH 바탕 (sh_utils.eval_sh 와 같은 부호 · 1 ~ 3차 · 15개) — d (K, 3) 단위 방향."""
    import numpy as np
    x, y, z = d[:, 0], d[:, 1], d[:, 2]
    C1 = 0.4886025119029199
    C2 = [1.0925484305920792, -1.0925484305920792, 0.31539156525252005, -1.0925484305920792, 0.5462742152960396]
    C3 = [-0.5900435899266435, 2.890611442640554, -0.4570457994644658, 0.3731763325901154, -0.4570457994644658,
          1.445305721320277, -0.5900435899266435]
    xx, yy, zz = x * x, y * y, z * z
    return np.stack([-C1 * y, C1 * z, -C1 * x,
                     C2[0] * x * y, C2[1] * y * z, C2[2] * (2 * zz - xx - yy), C2[3] * x * z, C2[4] * (xx - yy),
                     C3[0] * y * (3 * xx - yy), C3[1] * x * y * z, C3[2] * y * (4 * zz - xx - yy), C3[3] * z * (2 * zz - 3 * xx - 3 * yy),
                     C3[4] * x * (4 * zz - xx - yy), C3[5] * z * (xx - yy), C3[6] * x * (xx - 3 * yy)], 1)


def sh_rotation(Rg):
    """SH 1 ~ 3차 계수 돌림 (15×15 · 차수마다 덩어리): 새 계수 c' = A c 이면 f'(d) = f(Rgᵀ d) — 수치로 (방향 400개 최소제곱)."""
    import numpy as np
    rng = np.random.default_rng(7)
    D = rng.normal(size=(400, 3)); D /= np.linalg.norm(D, axis=1, keepdims=True)
    B0, B1 = _sh_basis(D), _sh_basis(D @ Rg)          # D @ Rg = (Rgᵀ d) 를 줄마다
    A = np.zeros((15, 15))
    for a, b in ((0, 3), (3, 8), (8, 15)):
        A[a:b, a:b] = np.linalg.lstsq(B0[:, a:b], B1[:, a:b], rcond=None)[0]     # B0 · A = B1
    return A


def to_world(ply, s, R, t, to_enh, origin, crop=None, crop_fn=None, log=print):
    """Brush PLY (모델 틀) → 도면 틀 열 {x(E) y(N) z(H) …} float64 위치 + 돌린 회전 · 크기 · SH. crop = [[E, N], …] 밖은 뺌."""
    import numpy as np
    g, n = read_ply(ply)
    X = np.stack([g['x'], g['y'], g['z']], 1).astype(np.float64)
    keep = np.isfinite(X).all(1) & np.isfinite(g['opacity']) & (g['opacity'] > -5.54)      # 시그모이드 1/255 아래 = 안 보임
    for k in ('scale_0', 'scale_1', 'scale_2', 'rot_0'):
        keep &= np.isfinite(g[k])
    E, N, H = to_enh(s * (X @ R.T) + t)
    keep &= np.isfinite(E) & np.isfinite(N) & np.isfinite(H)
    if crop and crop_fn is not None and keep.any():
        kk = np.zeros(n, bool); kk[keep] = crop_fn(E[keep], N[keep], crop)
        keep &= kk
    # 그 자리 선형 부분 J (가운데에서 ±1 m 차분) = k · Rg (닮음 · 도면 축척 계수 · 자오선 수렴각까지)
    c = np.median(X[keep], 0) if keep.any() else X.mean(0)
    P = np.array([c + d for d in ([1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1])])
    e2, n2, h2 = to_enh(s * (P @ R.T) + t)
    Q = np.stack([e2, n2, h2], 1)
    J = np.stack([(Q[0] - Q[1]) / 2, (Q[2] - Q[3]) / 2, (Q[4] - Q[5]) / 2], 1)
    U, S, Vt = np.linalg.svd(J)
    if np.linalg.det(U @ Vt) < 0:
        raise RuntimeError('모델 → 도면 좌표가 거울상 (좌표 틀 확인)')
    Rg = U @ Vt; k = float(S.mean())
    ang = math.degrees(math.acos(max(-1.0, min(1.0, (np.trace(Rg) - 1) / 2))))
    log('  가우시안 도면 좌표: 배율 %.6f (축마다 %s) · 돌림 %.3f°' % (k, ' · '.join('%.6f' % v for v in S), ang))
    idx = np.nonzero(keep)[0]
    out = {'x': E[idx] - origin[0], 'y': N[idx] - origin[1], 'z': H[idx]}
    q = np.stack([g['rot_0'], g['rot_1'], g['rot_2'], g['rot_3']], 1)[idx].astype(np.float64)
    q /= np.maximum(np.linalg.norm(q, axis=1, keepdims=True), 1e-12)
    a = _quat_of(Rg)                                    # q' = a ⊗ q (가우시안 축 = Rg · 원래 축)
    w0, x0, y0, z0 = a
    w1, x1, y1, z1 = q[:, 0], q[:, 1], q[:, 2], q[:, 3]
    out['rot_0'] = w0 * w1 - x0 * x1 - y0 * y1 - z0 * z1
    out['rot_1'] = w0 * x1 + x0 * w1 + y0 * z1 - z0 * y1
    out['rot_2'] = w0 * y1 - x0 * z1 + y0 * w1 + z0 * x1
    out['rot_3'] = w0 * z1 + x0 * y1 - y0 * x1 + z0 * w1
    lk = math.log(k)
    for i in range(3):
        out['scale_%d' % i] = g['scale_%d' % i][idx] + lk
    out['opacity'] = g['opacity'][idx]
    for i in range(3):
        out['f_dc_%d' % i] = g['f_dc_%d' % i][idx]
    nrest = sum(1 for kk in g if kk.startswith('f_rest_'))
    nb = nrest // 3                                     # 3DGS 꼴: f_rest = [빨강 nb 개 · 초록 nb · 파랑 nb]
    if nb:
        A = sh_rotation(Rg)[:nb, :nb]
        for ch in range(3):
            C = np.stack([g['f_rest_%d' % (ch * nb + j)][idx] for j in range(nb)], 1).astype(np.float64)
            C2 = C @ A.T
            for j in range(nb):
                out['f_rest_%d' % (ch * nb + j)] = C2[:, j]
    return out, {'n_in': int(n), 'n': int(len(idx)), 'scale': k, 'rot_deg': ang}


def write_web(cols, origin_h, path, max_n=WEB_MAX, min_alpha=WEB_MIN_ALPHA):
    """보기 자료 (antimatter15 .splat 꼴 · 개마다 32 B): 위치 float32×3 (원점 기준 · 표고 − 원점 표고) · 크기 float32×3 (m) ·
    색 RGBA uint8 (SH 바탕 색 · 알파 = 불투명도) · 회전 uint8×4 (w x y z · ×128 + 128). 중요한 것(불투명도 × 크기)부터 · 많으면 자름."""
    import numpy as np
    alpha = 1 / (1 + np.exp(-cols['opacity'].astype(np.float64)))
    sc = np.exp(np.stack([cols['scale_0'], cols['scale_1'], cols['scale_2']], 1).astype(np.float64))
    keep = np.nonzero(alpha >= min_alpha)[0]
    imp = alpha[keep] * np.prod(sc[keep], 1) ** (1 / 3)
    order = keep[np.argsort(-imp, kind='stable')][:max_n]
    n = len(order)
    rec = np.zeros(n, dtype=[('p', '<f4', 3), ('s', '<f4', 3), ('c', 'u1', 4), ('r', 'u1', 4)])
    rec['p'] = np.stack([cols['x'][order], cols['y'][order], cols['z'][order] - origin_h], 1)
    rec['s'] = sc[order]
    rgb = 0.5 + SH_C0 * np.stack([cols['f_dc_0'][order], cols['f_dc_1'][order], cols['f_dc_2'][order]], 1)
    rec['c'][:, :3] = np.clip(rgb * 255, 0, 255).astype(np.uint8)
    rec['c'][:, 3] = np.clip(alpha[order] * 255, 0, 255).astype(np.uint8)
    q = np.stack([cols['rot_0'][order], cols['rot_1'][order], cols['rot_2'][order], cols['rot_3'][order]], 1)
    q /= np.maximum(np.linalg.norm(q, axis=1, keepdims=True), 1e-12)
    rec['r'] = np.clip(q * 128 + 128, 0, 255).astype(np.uint8)
    tmp = path + '.tmp'
    rec.tofile(tmp)
    os.replace(tmp, path)
    P = rec['p']                                        # 범위 = 1 ~ 99 % (가우시안만 만든 프로젝트의 처음 시점 · 떠다니는 것 빼고)
    return n, (np.percentile(P, 1, axis=0).tolist() if n else [0, 0, 0]), (np.percentile(P, 99, axis=0).tolist() if n else [0, 0, 0])


def _dom_level(cx, cy, z, a, ia, ib, ic, rad, rank, rgb, W, Hh, cell, band=512):
    """한 단계 칸에 가우시안 그리기 (높은 것부터 앞에서 뒤로) → (P 미리 곱한 색 (H, W, 3) · A 불투명 · Z 불투명 무게 평균 높이)."""
    import numpy as np
    P = np.zeros((Hh, W, 3), np.float32); A = np.zeros((Hh, W), np.float32); Z = np.zeros((Hh, W), np.float32)
    for r0 in range(0, Hh, band):
        r1 = min(Hh, r0 + band)
        sel = np.nonzero((cy + rad >= r0) & (cy - rad < r1))[0]
        if not len(sel):
            continue
        PIX, WT, RK, ID = [], [], [], []
        for rr in np.unique(rad[sel]):
            g = sel[rad[sel] == rr]
            o = np.arange(-rr, rr + 1)
            ox, oy = np.meshgrid(o, o); ox = ox.ravel(); oy = oy.ravel()
            step = max(1, 4000000 // len(ox))
            for c0 in range(0, len(g), step):                 # 덩어리 (메모리)
                gg = g[c0:c0 + step]
                px = np.floor(cx[gg])[:, None].astype(np.int64) + ox[None]
                py = np.floor(cy[gg])[:, None].astype(np.int64) + oy[None]
                dx = (px + 0.5 - cx[gg][:, None]) * cell; dy = -(py + 0.5 - cy[gg][:, None]) * cell
                m2 = ia[gg][:, None] * dx * dx + 2 * ib[gg][:, None] * dx * dy + ic[gg][:, None] * dy * dy
                wt = a[gg][:, None] * np.exp(-0.5 * m2)
                ok = (m2 <= 9) & (wt >= 1 / 255) & (px >= 0) & (px < W) & (py >= r0) & (py < r1)     # 3σ 타원 안만
                PIX.append((py[ok] - r0) * W + px[ok]); WT.append(np.minimum(wt[ok], 0.99).astype(np.float32))
                RK.append(np.broadcast_to(rank[gg][:, None], ok.shape)[ok]); ID.append(np.broadcast_to(gg[:, None], ok.shape)[ok])
        pix = np.concatenate(PIX); wt = np.concatenate(WT); rk = np.concatenate(RK); idx = np.concatenate(ID)
        if not len(pix):
            continue
        o = np.lexsort((rk, pix)); pix, wt, idx = pix[o], wt[o], idx[o]
        L = np.log1p(-wt.astype(np.float64)); cs = np.cumsum(L)
        st = np.r_[0, np.nonzero(np.diff(pix))[0] + 1]
        base = np.repeat(cs[st] - L[st], np.diff(np.r_[st, len(pix)]))     # 화소 앞 누적 (앞 화소까지)
        contrib = wt * np.exp(cs - L - base)                                # α · (그 앞 투과)
        up = pix[st]
        sc = np.add.reduceat(contrib, st)
        Pb = P[r0:r1].reshape(-1, 3); Ab = A[r0:r1].reshape(-1); Zb = Z[r0:r1].reshape(-1)
        for ch in range(3):
            Pb[up, ch] = np.add.reduceat(contrib * rgb[idx, ch], st)
        Ab[up] = 1 - np.exp(np.add.reduceat(L, st))
        Zb[up] = np.add.reduceat(contrib * z[idx], st) / np.maximum(sc, 1e-9)
    return P, A, Z


def write_dom(cols, origin, outs, cell, crs, crop=None, log=print, max_px=40e6, rmax=12, kmax=7):
    """가우시안 정사영상 (위에서 본 정사 투영 · 바탕 색만) → 결과물/가우시안_정사.tif (RGBA) + .tfw · .prj.
    칸마다 높은 가우시안부터 앞에서 뒤로 섞기 (T = Π(1 − α·g)). 큰 가우시안 (길 · 밭 · 지붕처럼 매끈한 곳) 은 크기에 맞는 거친 칸
    (2^k 배 · 도장 반지름 rmax 칸 안) 에 그리고 고운 칸으로 키워 칸마다 높이 (불투명 무게 평균) 로 앞뒤를 정해 합침 (근사 · 보기용)."""
    import numpy as np
    from PIL import Image
    from .mapping import _world
    t0 = time.time()
    a = 1 / (1 + np.exp(-cols['opacity'].astype(np.float64)))
    k = a >= 0.02
    if k.sum() > 1000:                                    # 떠다니는 먼 가우시안 (범위만 키움) 은 뺌 · 0.2 ~ 99.8 %
        for c in ('x', 'y'):
            lo, hi = np.percentile(cols[c][k], [0.2, 99.8])
            k &= (cols[c] >= lo) & (cols[c] <= hi)
    k = np.nonzero(k)[0]
    x, y, z, a = cols['x'][k], cols['y'][k], cols['z'][k].astype(np.float64), a[k]
    q = np.stack([cols['rot_%d' % i][k] for i in range(4)], 1).astype(np.float64)
    q /= np.maximum(np.linalg.norm(q, axis=1, keepdims=True), 1e-12)
    w_, x_, y_, z_ = q.T
    R0 = np.stack([1 - 2 * (y_ * y_ + z_ * z_), 2 * (x_ * y_ - w_ * z_), 2 * (x_ * z_ + w_ * y_)], 1)     # 회전 행렬 첫째 · 둘째 줄
    R1 = np.stack([2 * (x_ * y_ + w_ * z_), 1 - 2 * (x_ * x_ + z_ * z_), 2 * (y_ * z_ - w_ * x_)], 1)
    s2 = np.exp(2 * np.stack([cols['scale_%d' % i][k] for i in range(3)], 1).astype(np.float64))
    sxx0 = (R0 * R0 * s2).sum(1); syy0 = (R1 * R1 * s2).sum(1); sxy0 = (R0 * R1 * s2).sum(1)
    del R0, R1, s2, q, w_, x_, y_, z_
    x0m, x1m, y0m, y1m = float(x.min()), float(x.max()), float(y.min()), float(y.max())
    cell = max(cell, math.sqrt((x1m - x0m) * (y1m - y0m) / max_px))
    W = int(math.ceil((x1m - x0m) / cell)) + 1; Hh = int(math.ceil((y1m - y0m) / cell)) + 1
    lam0 = (sxx0 + syy0) / 2 + np.sqrt(((sxx0 - syy0) / 2) ** 2 + sxy0 * sxy0)
    r_base = 3 * np.sqrt(lam0 + (0.5 * cell) ** 2) / cell
    lev = np.clip(np.ceil(np.log2(np.maximum(r_base, 1) / rmax)), 0, None).astype(np.int32)
    keep = lev <= kmax                                   # 3σ 가 rmax·2^kmax 칸 넘는 것 (수십 m · 떠다니는 것) 만 뺌
    rgb = np.clip(0.5 + SH_C0 * np.stack([cols['f_dc_%d' % i][k] for i in range(3)], 1), 0, 1).astype(np.float32)
    rank = np.empty(len(z), np.int64); rank[np.argsort(-z, kind='stable')] = np.arange(len(z))     # 높은 것 = 앞
    out_P = out_A = out_Z = None
    nlev = []
    for L in sorted(set(lev[keep].tolist()), reverse=True):     # 거친 단계부터 → 고운 단계로 합침
        g = np.nonzero(keep & (lev == L))[0]
        f = 2 ** L; cl = cell * f
        Wl, Hl = int(math.ceil(W / f)), int(math.ceil(Hh / f))
        lp = (0.5 * cl) ** 2                             # 칸보다 작은 가우시안도 칸 하나는 덮게 (빈틈 없이)
        sxx, syy, sxy = sxx0[g] + lp, syy0[g] + lp, sxy0[g]
        det = sxx * syy - sxy * sxy
        lam = (sxx + syy) / 2 + np.sqrt(((sxx - syy) / 2) ** 2 + sxy * sxy)
        rad = np.clip(np.ceil(3 * np.sqrt(lam) / cl), 1, rmax + 1).astype(np.int32)
        Pl, Al, Zl = _dom_level((x[g] - x0m) / cl, (y1m - y[g]) / cl, z[g], a[g], syy / det, -sxy / det, sxx / det, rad, rank[g],
                                rgb[g], Wl, Hl, cl)
        nlev.append((L, len(g)))
        if f > 1:                                        # 고운 칸으로 키움 (선형)
            up = lambda m: np.asarray(Image.fromarray(m, 'F').resize((Wl * f, Hl * f), Image.BILINEAR))[:Hh, :W]   # noqa: E731
            Pl = np.stack([up(Pl[..., c]) for c in range(3)], 2); Al = up(Al); Zl = up(Zl)
        if out_P is None:
            out_P, out_A, out_Z = Pl, Al, Zl
            continue
        front = np.where(out_A < 1e-4, 1, np.where(Al < 1e-4, 0, Zl >= out_Z)).astype(np.float32)[..., None]   # 이 단계가 위 (칸마다 높이 견줌)
        P_new = front * (Pl + (1 - Al)[..., None] * out_P) + (1 - front) * (out_P + (1 - out_A)[..., None] * Pl)
        A_new = Al + out_A - Al * out_A
        out_Z = (Al * Zl + out_A * out_Z) / np.maximum(Al + out_A, 1e-6)
        out_P, out_A = P_new.astype(np.float32), A_new.astype(np.float32)
    img = np.zeros((Hh, W, 4), np.uint8)
    if out_P is not None:
        img[..., :3] = np.clip(out_P / np.maximum(out_A, 1e-6)[..., None] * 255 + 0.5, 0, 255).astype(np.uint8)
        # 사선 사진으로 배운 가우시안은 바로 위에서 조금 비침 (80장 밭 불투명 가운데값 0.9) → 0.6 이상 = 꽉 참 (색은 알파로 나눠 둠)
        img[..., 3] = np.clip(out_A / 0.6 * 255 + 0.5, 0, 255).astype(np.uint8)
    Image.fromarray(img, 'RGBA').save(os.path.join(outs, '가우시안_정사.tif'), compression='tiff_lzw')
    _world(outs, '가우시안_정사', origin[0] + x0m, origin[1] + y1m, cell)
    log('  가우시안 정사영상 %d×%d · 칸 %.1f cm · 단계 %s · %d초' % (
        W, Hh, 100 * cell, ' · '.join('%d배 %s개' % (2 ** L, format(n, ',')) for L, n in sorted(nlev)), time.time() - t0))
    return {'cell': round(cell, 4), 'size': [W, Hh]}


# ---------------------------------------------------------------- 학습
def _md5_files(paths):
    h = hashlib.md5()
    for p in paths:
        with open(p, 'rb') as fp:
            for b in iter(lambda: fp.read(1 << 20), b''):
                h.update(b)
    return h.hexdigest()


def _dataset(dense, d):
    """Brush 가 읽는 COLMAP 폴더 (images · sparse) — 사진은 굳은 연결(같은 디스크 · 복사 없이) · 안 되면 복사.
    work/dense 를 바로 주지 않음: OpenMVS *.ply 가 있으면 Brush 가 그것을 시작 가우시안으로 읽을 수 있음."""
    shutil.rmtree(d, ignore_errors=True)
    os.makedirs(os.path.join(d, 'images')); os.makedirs(os.path.join(d, 'sparse'))
    for f in os.listdir(os.path.join(dense, 'sparse')):
        if f.endswith('.bin'):
            shutil.copy2(os.path.join(dense, 'sparse', f), os.path.join(d, 'sparse', f))
    im = os.path.join(dense, 'images')
    for f in os.listdir(im):
        a, b = os.path.join(im, f), os.path.join(d, 'images', f)
        try:
            os.link(a, b)
        except OSError:
            shutil.copy2(a, b)


def _vram_mb():
    """NVIDIA 그래픽 메모리 (MB) · nvidia-smi 가 없으면 None (AMD · Intel = 한도 그대로)."""
    try:
        r = subprocess.run(['nvidia-smi', '--query-gpu=memory.total', '--format=csv,noheader,nounits'], capture_output=True,
                           text=True, timeout=10, creationflags=NO_WINDOW)
        v = [int(float(x)) for x in r.stdout.split() if x.strip()]
        return max(v) if r.returncode == 0 and v else None
    except (OSError, ValueError, subprocess.SubprocessError):
        return None


def train(exe, dense, wd, quality, res_cap=0, log=print, progress=None, extra=()):
    """Brush 학습 → work/gs/brush.ply · (경로, 초, 가우시안 수, 걸음). progress(비율 0~1, 글)."""
    steps, res, max_splats = QUALITY[quality]
    if res_cap:
        res = min(res, int(res_cap))
    vm = _vram_mb()                                       # 1126 실측: Brush 가 가우시안 하나에 그래픽 메모리 약 2.1 KB (2640 px · 557만 개 11.6 GB)
    if vm:                                                # 바탕 (화면 · 다른 프로그램) 3 GB 를 빼고 → 넘치면 느려지거나 멈춤
        cap = max(500000, int((vm - 3000) * 1e6 / 2300))
        if cap < max_splats:
            log('  그래픽 메모리 %d MB → 가우시안 최대 %s개 (품질 기본 %s개)' % (vm, format(cap, ','), format(max_splats, ',')))
            max_splats = cap
    d = os.path.join(wd, 'data')
    _dataset(dense, d)
    out = os.path.join(wd, 'brush.ply')
    for f in (out, os.path.join(wd, 'brush_train.ply')):
        if os.path.exists(f):
            os.remove(f)
    cmd = [exe, d, '--total-steps', str(steps), '--max-resolution', str(res), '--max-splats', str(max_splats),
           '--growth-stop-iter', str(steps // 2), '--export-every', str(steps), '--eval-every', str(10 ** 9),
           '--export-path', wd, '--export-name', 'brush_train.ply'] + list(extra)
    env = dict(os.environ, RUST_LOG='brush_cli=info,brush_process=warn,brush_train=warn')
    t0 = time.time(); last = ''; nspl = 0
    with open(os.path.join(wd, 'brush.log'), 'w', encoding='utf-8', errors='replace') as lf:
        lf.write(' '.join(cmd) + '\n'); lf.flush()
        cwd = os.path.dirname(exe) if os.access(os.path.dirname(exe), os.W_OK) else wd     # 자동 조정 캐시 (target/autotune) 를 프로젝트끼리 함께
        p = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, cwd=cwd, env=env, creationflags=NO_WINDOW,
                             text=True, encoding='utf-8', errors='replace', bufsize=1)
        tail = []
        for line in p.stdout:
            lf.write(line); lf.flush()
            tail = (tail + [line.strip()])[-12:]
            m = re.search(r'Refine iter (\d+), (\d+) splats', line)
            if m:
                it, nspl = int(m.group(1)), int(m.group(2))
                if progress:
                    progress(min(1.0, it / steps), '가우시안 학습 %d / %d걸음 · %s개' % (it, steps, format(nspl, ',')))
            last = line.strip() or last
        rc = p.wait()
    shutil.rmtree(d, ignore_errors=True)                # 사진 연결은 바로 지움 (왜곡 편 사진을 지워도 디스크가 안 비는 일 없게)
    src = os.path.join(wd, 'brush_train.ply')
    if rc != 0 or not os.path.exists(src):
        why = next((l for l in reversed(tail) if re.search(r'error|panic|fail', l, re.I)), last)
        raise RuntimeError('Brush 종료코드 %d · %s' % (rc, (why or '?')[:200]))
    os.replace(src, out)
    _, n = read_ply(out)
    return out, time.time() - t0, n, steps


def last_quality(ws):
    """지난 학습 품질 (work/gs/train.json) · 없으면 None — 품질을 안 넘긴 다시 돌리기에서 다시 학습하지 않게."""
    try:
        q = json.load(open(os.path.join(ws, 'gs', 'train.json'), encoding='utf-8')).get('quality')
        return q if q in QUALITY else None
    except (OSError, ValueError, AttributeError):
        return None


def last_dom(ws):
    """지난 변환에서 가우시안 정사영상을 만들었는지 (data/gs/gs.json) — --gs-dom 을 안 넘긴 다시 돌리기에서 그대로."""
    try:
        return 1 if json.load(open(os.path.join(os.path.dirname(ws), 'data', 'gs', 'gs.json'), encoding='utf-8')).get('dom') else 0
    except (OSError, ValueError, AttributeError):
        return 0


def _drop_outputs(outs, web):
    for f in ('가우시안.ply', '가우시안_정사.tif', '가우시안_정사.tfw', '가우시안_정사.prj'):
        if os.path.exists(os.path.join(outs, f)):
            os.remove(os.path.join(outs, f))
    shutil.rmtree(web, ignore_errors=True)


def make(ws, out, s, R, t, origin, quality='high', crop=None, crs='', to_enh=None, crop_fn=None,
         ensure_images=None, res_cap=0, do_train=True, dom=False, dom_cell=0.1, log=print, progress=None):
    """매핑 결과물 «가우시안 스플래팅» (mapping.main 이 부름).
    do_train = False: 학습은 하지 않고 지난 학습이 그대로 쓸 만할 때만 변환 (잘라내기 · 좌표계만 바뀐 다시 돌리기 · 앱 다듬기는
    결과물 목록에 gs 를 안 넘김) · 지난 학습을 못 쓰면 옛 가우시안 결과물을 지우고 None (잘리지 않은 옛 것이 남지 않게).
    → {'count', 'web', 'quality', 'steps', 'res', 'train_sec', 'conv_sec', 'reused', 'scale', 'rot_deg', 'dom'}."""
    dense = os.path.join(ws, 'dense'); wd = os.path.join(ws, 'gs')
    os.makedirs(wd, exist_ok=True)
    outs = os.path.join(out, '결과물'); web = os.path.join(out, 'data', 'gs')
    if not do_train and not os.path.exists(os.path.join(dense, 'sparse', 'images.bin')):
        _drop_outputs(outs, web)
        return None
    steps, res, _ = QUALITY[quality]
    if res_cap:
        res = min(res, int(res_cap))
    cams = _md5_files([os.path.join(dense, 'sparse', f) for f in ('cameras.bin', 'images.bin')])
    tsig = hashlib.md5(json.dumps([ENGINE, ENGINE_VER, quality, steps, res, cams]).encode()).hexdigest()
    tj = os.path.join(wd, 'train.json')
    try:
        T = json.load(open(tj, encoding='utf-8'))
    except (OSError, ValueError):
        T = {}
    ply = os.path.join(wd, 'brush.ply')
    reused = T.get('sig') == tsig and os.path.exists(ply)
    if not reused and T.get('cams') == cams and os.path.exists(ply) and not do_train:
        reused = True                                   # 다시 돌리기 (품질을 안 넘긴 다듬기): 같은 카메라면 지난 품질 그대로
        quality, steps, res = T.get('quality', quality), T.get('steps', steps), T.get('res', res)
        tsig = T['sig']
    if reused:
        log('가우시안 학습 · 지난번 것 씀 (%s · %d걸음)' % (QUALITY_NAME.get(quality, quality), steps))
        tsec = T.get('sec', 0)
    elif not do_train:
        log('  가우시안: 지난 학습이 지금 카메라와 다름 → 옛 가우시안 결과물 지움 (가우시안을 골라 다시 만들기)')
        _drop_outputs(outs, web)
        return None
    else:
        exe = find_engine() or install_engine(log)
        if not exe:
            raise RuntimeError('엔진 설치 실패')
        if ensure_images is not None and not ensure_images():
            raise RuntimeError('왜곡 편 사진 없음')
        log('  가우시안 학습 시작 · 품질 %s · %d걸음 · 사진 %d px' % (QUALITY_NAME[quality], steps, res))
        ply, tsec, nraw, steps = train(exe, dense, wd, quality, res_cap=res_cap, log=log, progress=progress)
        T = {'sig': tsig, 'cams': cams, 'quality': quality, 'steps': steps, 'res': res, 'sec': round(tsec), 'n': nraw,
             'engine': '%s %s' % (ENGINE, ENGINE_VER), 'made': time.strftime('%Y-%m-%d %H:%M')}
        json.dump(T, open(tj, 'w', encoding='utf-8'), ensure_ascii=False)
        log('가우시안 학습 %d초 · %s개' % (tsec, format(nraw, ',')))
    csig = hashlib.md5(json.dumps([GS_VERSION, tsig, crop, crs, list(origin), bool(dom), dom_cell if dom else 0]).encode()).hexdigest()
    gj = os.path.join(web, 'gs.json')
    try:
        G = json.load(open(gj, encoding='utf-8'))
    except (OSError, ValueError):
        G = {}
    if G.get('sig') == csig and os.path.exists(os.path.join(outs, '가우시안.ply')) and os.path.exists(os.path.join(web, G.get('file', '-'))):
        log('  가우시안 변환 · 지난번 것 그대로')
        return {'count': G.get('count_ply', 0), 'web': G.get('count', 0), 'quality': quality, 'steps': steps, 'res': res,
                'train_sec': tsec, 'conv_sec': 0, 'reused': True, 'scale': G.get('scale'), 'rot_deg': G.get('rot_deg'), 'dom': G.get('dom')}
    t1 = time.time()
    if progress:
        progress(1.0, '가우시안 도면 좌표로')
    cols, st = to_world(ply, s, R, t, to_enh, origin, crop=crop, crop_fn=crop_fn, log=log)
    if not st['n']:
        raise RuntimeError('남은 가우시안 없음 (잘라내기 범위 확인)')
    os.makedirs(outs, exist_ok=True); os.makedirs(web, exist_ok=True)
    write_ply(os.path.join(outs, '가우시안.ply'), cols, comments=(
        'DH Drone Roadview gaussian splatting (Brush %s)' % ENGINE_VER, 'crs %s + EGM2008 height' % crs,
        'offset_x %.3f' % origin[0], 'offset_y %.3f' % origin[1], 'offset_z 0',
        'E = x + offset_x, N = y + offset_y, H = z (m)', 'Vertical axis: z'))
    nw, lo, hi = write_web(cols, origin[2], os.path.join(web, 'splats.bin'))
    dom_i = None
    for f in ('가우시안_정사.tif', '가우시안_정사.tfw', '가우시안_정사.prj'):
        if os.path.exists(os.path.join(outs, f)):
            os.remove(os.path.join(outs, f))
    if dom:
        if progress:
            progress(1.0, '가우시안 정사영상')
        try:
            dom_i = write_dom(cols, origin, outs, dom_cell, crs, crop=crop, log=log)
        except Exception as ex:                          # noqa: BLE001 — 메모리 등 · 가우시안 PLY · 보기는 그대로
            log('  가우시안 정사영상 실패 (%s)' % str(ex)[:200])
    G = {'version': 1, 'file': 'splats.bin', 'bytes': 32, 'count': nw, 'count_ply': st['n'], 'min': lo, 'max': hi,
         'origin': list(origin), 'crs': crs, 'quality': quality, 'steps': steps, 'res': res, 'engine': '%s %s' % (ENGINE, ENGINE_VER),
         'scale': round(st['scale'], 7), 'rot_deg': round(st['rot_deg'], 4), 'dom': dom_i, 'sig': csig, 'made': time.strftime('%Y-%m-%d %H:%M')}
    with open(gj + '.tmp', 'w', encoding='utf-8') as fp:
        json.dump(G, fp, ensure_ascii=False)
    os.replace(gj + '.tmp', gj)
    csec = time.time() - t1
    log('  가우시안.ply %s개 (모델 %s개 중) · 보기 자료 %s개 · %.0f MB · 변환 %d초' % (
        format(st['n'], ','), format(st['n_in'], ','), format(nw, ','), nw * 32 / 1e6, csec))
    return {'count': st['n'], 'web': nw, 'quality': quality, 'steps': steps, 'res': res, 'train_sec': tsec, 'conv_sec': csec,
            'reused': reused, 'scale': st['scale'], 'rot_deg': st['rot_deg'], 'dom': dom_i}
