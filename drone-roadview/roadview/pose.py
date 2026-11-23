"""장면마다 카메라 자세(세계 방향)와 위치 만들기.

드론 자세 사원수(.3.4.14.4)는 영상마다 북쪽이 제멋대로 돈 틀에 있다.
참 북 기준 방향(.3.4.3.3, 0.1° 단위)과의 차이를 ±2초 중앙값으로 메워 바로잡는다.
"""
from __future__ import annotations

import math

import numpy as np

from .camera import NED_TO_ENU, Rz, quat_to_R, quat_yaw


def yaw_offsets(frames, half_window_s=2.0):
    """장면마다 (참 방향 − 사원수 방향) 도. 없으면 None."""
    t = np.array([f.time for f in frames])
    raw = np.full(len(frames), np.nan)
    for i, f in enumerate(frames):
        if f.q_body is not None and 'a3' in f.raw_extra:
            raw[i] = (f.raw_extra['a3'] / 10.0 - quat_yaw(f.q_body) + 540.0) % 360.0 - 180.0
    good = ~np.isnan(raw)
    if not good.any():
        return None
    ref = np.median(raw[good])
    # 기준 주변으로 감아서 ±180 넘나듦 막기
    raw = (raw - ref + 540.0) % 360.0 - 180.0 + ref
    out = np.empty(len(frames))
    j0 = 0
    j1 = 0
    n = len(frames)
    for i in range(n):
        while t[j0] < t[i] - half_window_s:
            j0 += 1
        while j1 < n and t[j1] <= t[i] + half_window_s:
            j1 += 1
        w = raw[j0:j1]
        w = w[~np.isnan(w)]
        out[i] = np.median(w) if len(w) else ref
    return out


def grid_convergence_deg(transformer, lat, lon):
    """참 북 → 격자 북 차이 γ (격자 방위 = 참 방위 − γ). 수치로 잰다."""
    e0, n0 = transformer.transform(lon, lat)
    e1, n1 = transformer.transform(lon, lat + 1e-4)
    az_true_north_in_grid = math.degrees(math.atan2(e1 - e0, n1 - n0))   # 참 북이 격자에서 보이는 방위
    return -az_true_north_in_grid


def R_world_cam(frame, yaw_off_deg, rig, gamma_deg):
    """카메라 틀(아래 렌즈) → 격자 ENU."""
    R_nb = Rz(yaw_off_deg) @ quat_to_R(frame.q_body)     # 몸체 → 참 NED
    R_enu = NED_TO_ENU @ R_nb @ rig.R_bc()
    return Rz(gamma_deg) @ R_enu                          # 참 ENU → 격자 ENU (방위 −γ)
