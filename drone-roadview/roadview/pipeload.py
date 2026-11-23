"""관로 파일(SHP · DXF) → 보기 화면용 선.

선마다: layer(이름) · label · dia(m) · hasZ(설계 높이 있음) · pts[[E,N,z|None]] (도면 좌표계 = 프로젝트 좌표계).
땅 높이 g 는 촬영점 땅 높이로 꼭짓점마다 채운다 (ground_for).
"""
from __future__ import annotations

import math
import os
import re

import numpy as np

_PALETTE = ['#00e5ff', '#ffd400', '#ff4fd8', '#7CFF4F', '#ff8a3d', '#b28cff', '#ff5a5a', '#4fa3ff',
            '#00ffa6', '#ffa6e1']
_DIA_RE = re.compile(r'(?:^|[^A-Z0-9])(?:D|DN|Φ|φ|Ø|ø)\s*(\d{2,4})', re.IGNORECASE)


def dia_from_text(s):
    m = _DIA_RE.search(s or '')
    return float(m.group(1)) / 1000.0 if m else None


def load(path, crs='EPSG:5186', dia_field=None, label_field=None, layer_patterns=None, default_dia_mm=200.0):
    """→ (layers[{name,color}], lines[{layer, label, dia, hasZ, pts}]), 알림 글."""
    ext = os.path.splitext(path)[1].lower()
    note = ''
    raw = []      # (그룹 이름, label, dia, hasZ, pts)
    if ext == '.shp':
        from .shp import read_pipes
        lines, src = read_pipes(path, dia_field, label_field)
        if src is not None:
            from pyproj import CRS, Transformer
            dst = CRS.from_user_input(crs)
            if not src.equals(dst):
                tr = Transformer.from_crs(src, dst, always_xy=True)
                for ln in lines:
                    for p in ln['pts']:
                        p[0], p[1] = tr.transform(p[0], p[1])
                note = '좌표계 %s → %s' % (src.to_string(), crs)
        else:
            note = '.prj 없음 → %s 로 봄' % crs
        for ln in lines:
            dia = ln['dia']
            grp = ('D%d' % round(dia * 1000)) if dia else '관경 없음'
            raw.append((grp, ln['label'], dia, False, ln['pts']))
    elif ext == '.dxf':
        from .pipes import read_dxf
        layers, lines = read_dxf(path, layer_patterns)
        for ln in lines:
            name = layers[ln['layer']]['name']
            dia = dia_from_text(name)
            hasz = any(abs(p[2]) > 1e-9 for p in ln['pts'])
            raw.append((name, ln['label'], dia, hasz, [list(p) for p in ln['pts']]))
    else:
        raise ValueError('관로 파일은 .shp 또는 .dxf')
    # 관경 없으면 기본값
    out_lines = []
    groups = []
    for grp, label, dia, hasz, pts in raw:
        if grp not in groups:
            groups.append(grp)
        out_lines.append({'layer': grp, 'label': label, 'dia': dia if dia else default_dia_mm / 1000.0,
                          'diaKnown': bool(dia), 'hasZ': hasz, 'pts': pts})
    # 그룹 순서: 관경 작은 것부터
    def gkey(g):
        m = re.match(r'D(\d+)$', g)
        return (0, int(m.group(1))) if m else (1, g)
    groups.sort(key=gkey)
    layers = [{'name': g, 'color': _PALETTE[i % len(_PALETTE)]} for i, g in enumerate(groups)]
    for ln in out_lines:
        ln['layer'] = groups.index(ln['layer'])
    return layers, out_lines, note


def densify(lines, step=5.0):
    """꼭짓점 사이를 step(m) 이하로 — 땅 높이가 경사를 따라가게.
    원래 꼭짓점의 새 번호를 ln['ctrl'] 에 남긴다 (보기 화면 «관로 고치기» 손잡이 · SHP 내보내기 = 원래 꼭짓점)."""
    for ln in lines:
        p = ln['pts']
        q = [p[0]]
        ctrl = [0]
        for a, b in zip(p[:-1], p[1:]):
            L = math.hypot(b[0] - a[0], b[1] - a[1])
            n = max(1, int(math.ceil(L / step)))
            for k in range(1, n + 1):
                t = k / n
                z = None if (a[2] is None or b[2] is None) else a[2] + t * (b[2] - a[2])
                q.append([a[0] + t * (b[0] - a[0]), a[1] + t * (b[1] - a[1]), z])
            ctrl.append(len(q) - 1)
        ln['pts'] = q
        ln['ctrl'] = ctrl
    return lines


def clip_near(lines, shot_en, radius=200.0):
    """촬영점에서 radius(m) 안쪽 조각만 남긴다 (시 전체 관망 SHP 도 가볍게). 잘린 끝은 한 꼭짓점 더 둠."""
    if len(shot_en) == 0:
        return lines
    cell = radius
    grid = {}
    for x, y in shot_en:
        grid.setdefault((int(x // cell), int(y // cell)), []).append((x, y))
    r2 = radius * radius

    def near(x, y):
        cx, cy = int(x // cell), int(y // cell)
        for i in (cx - 1, cx, cx + 1):
            for j in (cy - 1, cy, cy + 1):
                for sx, sy in grid.get((i, j), ()):
                    if (sx - x) ** 2 + (sy - y) ** 2 <= r2:
                        return True
        return False

    out = []
    for ln in lines:
        p = ln['pts']
        keep = [near(q[0], q[1]) for q in p]
        i = 0
        n = len(p)
        while i < n:
            if not keep[i]:
                i += 1
                continue
            j = i
            while j + 1 < n and keep[j + 1]:
                j += 1
            a, b = max(0, i - 1), min(n - 1, j + 1)
            if b > a:
                seg = dict(ln)
                seg['pts'] = p[a:b + 1]
                if 'ctrl' in ln:            # 원래 꼭짓점 번호 옮기기 · 잘린 두 끝도 손잡이
                    c = sorted({k - a for k in ln['ctrl'] if a <= k <= b} | {0, b - a})
                    seg['ctrl'] = c
                out.append(seg)
            i = j + 1
    return out


class ShotGround:
    """촬영점 땅 높이로 만든 땅 높이 함수 (관로 g 와 같은 규칙) — 보기 화면 높이 타일용.
    가까운 촬영점(40 m 안 · 6개까지) 거리 가중 평균, 없으면 가장 가까운 것 (단 radius_far 밖은 NaN)."""

    def __init__(self, shot_en, shot_g, radius=40.0, k=6, radius_far=250.0):
        self.S = np.asarray(shot_en, float)
        self.G = np.asarray(shot_g, float)
        self.radius, self.k, self.far = radius, k, radius_far

    def __call__(self, x, y):
        x = np.asarray(x, float); y = np.asarray(y, float)
        shp = x.shape
        X = x.ravel(); Y = y.ravel()
        out = np.full(X.shape, np.nan)
        if len(X) == 0:
            return out.reshape(shp)
        m = ((self.S[:, 0] >= X.min() - self.far) & (self.S[:, 0] <= X.max() + self.far) &
             (self.S[:, 1] >= Y.min() - self.far) & (self.S[:, 1] <= Y.max() + self.far))
        S, G = self.S[m], self.G[m]
        if len(S) == 0:
            return out.reshape(shp)
        for a in range(0, len(X), 4096):
            xs, ys = X[a:a + 4096], Y[a:a + 4096]
            d = np.hypot(xs[:, None] - S[None, :, 0], ys[:, None] - S[None, :, 1])
            kk = min(self.k, d.shape[1])
            idx = np.argpartition(d, kk - 1, axis=1)[:, :kk]
            dd = np.take_along_axis(d, idx, 1)
            gg = G[idx]
            w = np.where(dd <= self.radius, 1.0 / np.maximum(dd, 1.0) ** 2, 0.0)
            ws = w.sum(1)
            near = np.argmin(dd, 1)
            v = np.where(ws > 0, (gg * w).sum(1) / np.where(ws > 0, ws, 1), gg[np.arange(len(xs)), near])
            v = np.where(dd.min(1) <= self.far, v, np.nan)
            out[a:a + 4096] = v
        return out.reshape(shp)


def ground_for(lines, shot_en, shot_g, radius=40.0, k=6):
    """꼭짓점마다 땅 높이 = 가까운 촬영점 땅 높이의 거리 가중 평균 (없으면 가장 가까운 것)."""
    if len(shot_en) == 0:
        for ln in lines:
            ln['g'] = [None] * len(ln['pts'])
        return lines
    S = np.asarray(shot_en, float)
    G = np.asarray(shot_g, float)
    for ln in lines:
        P = np.asarray([[p[0], p[1]] for p in ln['pts']], float)
        gs = []
        for x, y in P:
            d = np.hypot(S[:, 0] - x, S[:, 1] - y)
            idx = np.argsort(d)[:k]
            near = idx[d[idx] <= radius]
            if len(near) == 0:
                gs.append(float(G[idx[0]]))
            else:
                w = 1.0 / np.maximum(d[near], 1.0) ** 2
                gs.append(float((G[near] * w).sum() / w.sum()))
        ln['g'] = gs
    return lines
