"""어안 두 장 → 360 사진(등장방형) 꿰매기 + 회전 도구.

좌표 약속
  렌즈 틀: z = 렌즈가 보는 쪽, x = 그림 오른쪽, y = 그림 아래쪽.
  카메라 틀 C = 아래 렌즈(1번) 틀.  위 렌즈(0번) 방향 d0 = R01 · d1.
  몸체 틀 B = FRD (x 앞, y 오른쪽, z 아래).  d_B = R_bc · d_C.
  세계 = ENU(동, 북, 위) — 도면 격자 북 기준.
360 사진 약속 (보기 화면과 같음)
  열 x: 방위 λ = (x+0.5)/W·360° − 180° (북 = 가운데, 시계 방향 +)
  행 y: 고도 φ = 90° − (y+0.5)/H·180°
  방향 (E,N,U) = (cosφ·sinλ, cosφ·cosλ, sinφ)
"""
from __future__ import annotations

import json
import math
import os
from dataclasses import dataclass, asdict

import numpy as np


# ---------------------------------------------------------------- 회전

def Rx(deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


def Ry(deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def Rz(deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def quat_to_R(q):
    """(w,x,y,z) → 회전 행렬 (몸체 → NED)."""
    w, x, y, z = q
    n = math.sqrt(w * w + x * x + y * y + z * z)
    w, x, y, z = w / n, x / n, y / n, z / n
    return np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y)],
        [2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x)],
        [2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y)],
    ])


def quat_yaw(q):
    w, x, y, z = q
    return math.degrees(math.atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z)))


# NED → ENU
NED_TO_ENU = np.array([[0, 1, 0], [1, 0, 0], [0, 0, -1]], float)


# ---------------------------------------------------------------- 렌즈 보정값

@dataclass
class Rig:
    f0: float = 1146.74          # 위 렌즈 px/rad (3840 기준)
    f1: float = 1110.54          # 아래 렌즈
    c0x: float = 1915.8
    c0y: float = 1916.8
    c1x: float = 1913.6
    c1y: float = 1910.3
    psi: float = -0.212          # 위 렌즈 비틀림 (도)
    ex: float = -0.823
    ey: float = 0.223
    rmax: float = 1906.0         # 쓸 수 있는 반지름 (네모 테두리에 잘림)
    # 카메라 → 몸체 (아래 렌즈 틀 → FRD)
    bc_yaw: float = 0.0
    bc_x: float = 0.0
    bc_y: float = 0.0
    size: int = 3840

    def R01(self):
        return Rz(self.psi) @ Rx(self.ex) @ Ry(self.ey) @ Ry(180.0)

    def R_bc(self):
        return Rz(self.bc_yaw) @ Rx(self.bc_x) @ Ry(self.bc_y)

    @staticmethod
    def load(path):
        with open(path, encoding='utf-8') as fp:
            d = json.load(fp)
        r = Rig()
        for k, v in d.items():
            if hasattr(r, k):
                setattr(r, k, v)
        return r

    def save(self, path):
        with open(path, 'w', encoding='utf-8') as fp:
            json.dump(asdict(self), fp, indent=1, ensure_ascii=False)


DEFAULT_RIG_PATH = os.path.join(os.path.dirname(__file__), 'rig_avata360.json')


def default_rig() -> Rig:
    if os.path.isfile(DEFAULT_RIG_PATH):
        return Rig.load(DEFAULT_RIG_PATH)
    return Rig()


# ---------------------------------------------------------------- 표본 뽑기

def _bilinear(img, u, v):
    """img (H,W,C) uint8, u/v float 배열 → (…,C) float32."""
    H, W = img.shape[:2]
    u = np.clip(u, 0, W - 1.001)
    v = np.clip(v, 0, H - 1.001)
    u0 = u.astype(np.int32); v0 = v.astype(np.int32)
    du = (u - u0)[..., None].astype(np.float32); dv = (v - v0)[..., None].astype(np.float32)
    flat = img.reshape(-1, img.shape[2])
    i00 = v0 * W + u0
    a = flat[i00].astype(np.float32)
    b = flat[i00 + 1].astype(np.float32)
    c = flat[i00 + W].astype(np.float32)
    d = flat[i00 + W + 1].astype(np.float32)
    top = a + (b - a) * du
    bot = c + (d - c) * du
    return top + (bot - top) * dv


def _lens_uv(d, f, cx, cy, scale):
    """렌즈 틀 방향 d(…,3) → 그림 좌표 (u, v, 반지름 r[3840 기준])."""
    t = np.arccos(np.clip(d[..., 2], -1.0, 1.0))
    a = np.arctan2(d[..., 1], d[..., 0])
    r = f * t
    # 화소 가운데 = 정수 좌표. 줄인 그림(scale<1)에서는 (u+0.5)·s − 0.5
    return (cx + r * np.cos(a) + 0.5) * scale - 0.5, (cy + r * np.sin(a) + 0.5) * scale - 0.5, r


def equirect_dirs(W, H, step=1):
    """360 사진 화소 중심 방향 (ENU) — step 간격 성긴 격자."""
    xs = (np.arange(0, W, step) + 0.5) / W * 360.0 - 180.0
    ys = 90.0 - (np.arange(0, H, step) + 0.5) / H * 180.0
    lam = np.radians(xs)[None, :]
    phi = np.radians(ys)[:, None]
    cp = np.cos(phi)
    return np.stack(np.broadcast_arrays(cp * np.sin(lam), cp * np.cos(lam), np.sin(phi) + 0 * lam), -1)


def sample_dirs(img0, img1, rig: Rig, R_world_cam: np.ndarray, dirs: np.ndarray, blend_deg=3.0):
    """세계(ENU) 방향 배열(…,3) → 색(…,3) float32. 덧붙임 없이 바로 뽑기 (정사 그림 · 시험용)."""
    scale = img0.shape[0] / rig.size
    d1 = dirs @ R_world_cam
    d0 = d1 @ rig.R01().T
    u1, v1, r1 = _lens_uv(d1, rig.f1, rig.c1x, rig.c1y, scale)
    u0, v0, r0 = _lens_uv(d0, rig.f0, rig.c0x, rig.c0y, scale)
    th1 = np.degrees(r1 / rig.f1)
    lim1 = np.degrees(rig.rmax / rig.f1)
    lim0 = np.degrees(rig.rmax / rig.f0)
    seam = 0.5 * (lim1 + (180.0 - lim0))
    w1 = np.clip((seam + blend_deg - th1) / (2 * blend_deg), 0, 1)
    w1 = np.where(r1 >= rig.rmax, 0.0, w1)
    w1 = np.where(r0 >= rig.rmax, 1.0, w1)[..., None].astype(np.float32)
    c0 = _bilinear(img0, u0, v0)
    c1 = _bilinear(img1, u1, v1)
    return c1 * w1 + c0 * (1 - w1)


def _stitch_map(rig: Rig, R_world_cam, W, H, step, scale, blend_deg):
    """성긴 격자 (gh, gw, 5) = u0, v0, u1, v1, 아래 렌즈 몫."""
    dirs = equirect_dirs(W, H, step)                      # ENU
    d1 = dirs @ R_world_cam                               # = Rᵀ·w (행 벡터)
    d0 = d1 @ rig.R01().T
    u1, v1, r1 = _lens_uv(d1, rig.f1, rig.c1x, rig.c1y, scale)
    u0, v0, r0 = _lens_uv(d0, rig.f0, rig.c0x, rig.c0y, scale)
    th1 = np.degrees(r1 / rig.f1)
    lim1 = np.degrees(rig.rmax / rig.f1)
    lim0 = np.degrees(rig.rmax / rig.f0)
    seam = 0.5 * (lim1 + (180.0 - lim0))                  # 겹침 띠 가운데
    w1 = np.clip((seam + blend_deg - th1) / (2 * blend_deg), 0, 1)
    w1 = np.where(r1 >= rig.rmax, 0.0, w1)
    w1 = np.where(r0 >= rig.rmax, 1.0, w1)
    m = np.stack([u0, v0, u1, v1, w1], -1).astype(np.float32)
    # 경도 끝(x = W, 곧 λ = +180°)도 이어지게 한 열 더 — 방향이 첫 열(x = 0)과 같다
    return np.concatenate([m, m[:, :1]], axis=1)


def stitch(img0, img1, rig: Rig, R_world_cam: np.ndarray, W=6144, step=4, blend_deg=3.0, gain=True, strip=256):
    """두 어안 그림 → 360 사진 (H=W/2) uint8. R_world_cam: 카메라 틀(아래 렌즈) → ENU.

    방향 계산은 step 간격 성긴 격자에서 하고 화소마다 겹선형으로 메운다 (빠름).
    띠(strip 행)씩 처리해 메모리를 아낀다.
    """
    H = W // 2
    scale = img0.shape[0] / rig.size
    m = _stitch_map(rig, R_world_cam, W, H, step, scale, blend_deg)
    # 노출 맞춤: 겹침 띠에서 두 렌즈 평균 색 비율
    g = np.ones(3, np.float32)
    if gain:
        band = (m[..., 4] > 0.02) & (m[..., 4] < 0.98)
        if band.sum() > 200:
            mb = m[band]
            c0 = _bilinear(img0, mb[:, 0], mb[:, 1])
            c1 = _bilinear(img1, mb[:, 2], mb[:, 3])
            g = np.sqrt((c1.mean(0) + 1.0) / (c0.mean(0) + 1.0)).astype(np.float32)
    out = np.empty((H, W, 3), np.uint8)
    gh, gw = m.shape[:2]
    # 성긴 격자 j 는 화소 j·step 자리 (equirect_dirs) → 화소 x 의 성긴 좌표 = x / step
    xs = np.arange(W) / step
    x0 = np.clip(np.floor(xs).astype(int), 0, gw - 2)
    fx = np.clip(xs - x0, 0, 1).astype(np.float32)[None, :, None]
    for r0 in range(0, H, strip):
        r1 = min(H, r0 + strip)
        ys = np.arange(r0, r1) / step
        y0 = np.clip(np.floor(ys).astype(int), 0, gh - 2)
        fy = np.clip(ys - y0, 0, 1).astype(np.float32)[:, None, None]
        a = m[y0][:, x0]; b = m[y0][:, x0 + 1]; c = m[y0 + 1][:, x0]; d = m[y0 + 1][:, x0 + 1]
        mm = (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy
        col0 = _bilinear(img0, mm[..., 0], mm[..., 1]) * g
        col1 = _bilinear(img1, mm[..., 2], mm[..., 3]) / g
        w = mm[..., 4:5]
        out[r0:r1] = np.clip(col1 * w + col0 * (1 - w) + 0.5, 0, 255).astype(np.uint8)
    return out
