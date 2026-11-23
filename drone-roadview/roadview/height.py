"""땅 위 높이(H) 자동 찾기.

이륙점 기준 높이(rel_alt)는 이륙한 곳과 도로 높이가 다르면 틀린다 (0014: 24.2 m 기록 → 실제 약 14.6 m).
가까운 두 장면에서 «바로 위에서 본 땅 그림»을 가정 높이 H 로 만들면, H 가 틀린 만큼 두 그림이 밀린다:
    밀림 = (1 − H / H참) × (두 카메라 사이 거리)      (진행 방향으로)
밀림을 위상 상관으로 재서 H참을 바로 푼다. 두 번 되풀이해 다듬는다.
"""
from __future__ import annotations

import math

import numpy as np

from .camera import sample_dirs


def _hp(g, k):
    """고역 통과 (조명 · 그늘 차이 줄이기)."""
    pad = np.pad(g, k, mode='edge')
    c = np.cumsum(np.cumsum(pad, 0), 1)
    c = np.pad(c, ((1, 0), (1, 0)))
    n = 2 * k + 1
    box = (c[n:, n:] - c[:-n, n:] - c[n:, :-n] + c[:-n, :-n]) / (n * n)
    return g - box[:g.shape[0], :g.shape[1]]


def ortho(pair, rig, R, cam_en, H, center_en, half, res):
    """세계 격자(북이 위) 땅 그림 (밝기)."""
    xs = np.arange(-half, half, res) + res / 2
    gx, gy = np.meshgrid(xs, -xs)
    d = np.stack([center_en[0] + gx - cam_en[0], center_en[1] + gy - cam_en[1], np.full_like(gx, -H)], -1)
    d /= np.linalg.norm(d, axis=-1, keepdims=True)
    return sample_dirs(pair[0], pair[1], rig, R, d).mean(-1)


def phase_shift(a, b):
    """b 가 a 에 비해 얼마나 밀렸나 (열 +, 행 +) 화소 · 봉우리 세기."""
    n = a.shape[0]
    w = np.outer(np.hanning(n), np.hanning(a.shape[1]))
    A = np.fft.fft2((a - a.mean()) * w)
    B = np.fft.fft2((b - b.mean()) * w)
    Rr = B * np.conj(A)
    Rr /= np.abs(Rr) + 1e-9
    r = np.real(np.fft.ifft2(Rr))
    k = np.unravel_index(int(np.argmax(r)), r.shape)
    peak = float(r[k])

    def sub(i, axis):
        m = r.shape[axis]
        im1 = (i - 1) % m; ip1 = (i + 1) % m
        if axis == 0:
            y0, y1, y2 = r[im1, k[1]], r[i, k[1]], r[ip1, k[1]]
        else:
            y0, y1, y2 = r[k[0], im1], r[k[0], i], r[k[0], ip1]
        den = y0 - 2 * y1 + y2
        off = 0.5 * (y0 - y2) / den if abs(den) > 1e-12 else 0.0
        v = i + off
        return v - m if v > m / 2 else v

    return sub(k[1], 1), sub(k[0], 0), peak


def estimate_agl(pair_a, pair_b, rig, Ra, Rb, en_a, en_b, rel_a, rel_b, H0, iters=3):
    """두 장면 → dict(H=a 장면 땅 위 높이, peak=믿음, base=거리, resid=옆 방향 밀림 m)."""
    dx = en_b[0] - en_a[0]; dy = en_b[1] - en_a[1]
    base = math.hypot(dx, dy)
    if base < 1.0:
        return None
    ux, uy = dx / base, dy / base
    center = ((en_a[0] + en_b[0]) / 2, (en_a[1] + en_b[1]) / 2)
    dz = rel_b - rel_a

    def measure(H):
        half = min(max(1.0 * H, 8.0), 40.0)
        res = half / 160.0
        oa = ortho(pair_a, rig, Ra, en_a, H, center, half, res)
        ob = ortho(pair_b, rig, Rb, en_b, H + dz, center, half, res)
        k = max(2, int(round(0.6 / res)))
        sx, sy, peak = phase_shift(_hp(oa, k), _hp(ob, k))
        se, sn = sx * res, -sy * res          # 열 + = 동, 행 + = 남
        along = se * ux + sn * uy
        cross = -se * uy + sn * ux
        return along, cross, peak

    # 여러 가정 높이에서 각각 H참을 풀고, 가장 많이 겹치는 값을 고른다.
    # (봉우리 세기로 고르면 가짜 봉우리에 속는다 — 0014 300:370 에서 확인)
    cands = []
    for f in np.exp(np.linspace(math.log(0.3), math.log(1.6), 11)):
        H = max(2.0, H0 * f)
        if H + dz < 1.0:                 # b 장면이 그 높이보다 더 내려감 → 땅을 못 봄
            continue
        along, cross, peak = measure(H)
        ratio = 1.0 - along / base
        if ratio > 0.05:
            cands.append((H / ratio, H, along, cross, peak))
    if len(cands) < 3:
        return None
    hs = np.array([c[0] for c in cands])
    best_n, best_set = 0, None
    for h in hs:
        s = np.abs(hs - h) < 0.06 * h
        if s.sum() > best_n:
            best_n, best_set = int(s.sum()), s
    Ht = float(np.median(hs[best_set]))
    # 그 높이에서 한 번 더 재서 남은 밀림 확인
    if Ht + dz < 1.0:
        return None
    along, cross, peak = measure(Ht)
    ratio = 1.0 - along / base
    if ratio > 0.05:
        Ht = Ht / ratio
    return dict(H=Ht, votes=best_n, peak=peak, base=base, resid=cross, along=along)
