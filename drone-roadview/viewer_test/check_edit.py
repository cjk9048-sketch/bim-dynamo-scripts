"""관로 고치기 시험: make_synth --edit 폴더를 화면 없는 Edge (cdp.py, 실제 시간) 에서 진짜 마우스 · 키 (CDP Input) 로 고치고,
보기 화면 수식과 따로 짠 계산 (지도 = 메르카토르 + make_synth 의 en2ll · 360 = 카메라 투영 · 땅 = 시험 높이 타일 · 사진 땅 높이) 으로 잰다.

    python -I check_edit.py --out <make_synth --edit 폴더> [--legacy <make_synth --legacy-pipes 폴더>] [--shots <폴더>]

  file   (프로그램 없음 · file://)
    a  [관로 고치기] 켜자마자 (고르기 전) 지도에 모든 관 원래 꼭짓점 손잡이가 보임 ·
       지도에서 관 누름 → 고름 · 손잡이 끌기 → 꼭짓점 (e, n) = 마우스 만큼 (±5 cm) · 5 m 마다 다시 나눔 · g = 비탈 땅 · ll ↔ en (±0.2 m)
       끄는 동안 그때그때 바뀜 (놓기 전) · 끄는 동안 다시 올린 WebGL 그물 = 고른 관 것 (관 · 구덩이) 뿐
    b  360 에서 손잡이를 칠한 네모 (사진에서 찾은 빨간 네모 가운데) 로 → 꼭짓점 = 네모 자리 (±10 cm)
    c  Shift + 끌기 (지도) → 꼭짓점 모두 같은 만큼 · d  두 번 누르기 → 그 마디 위에 꼭짓점 · e  손잡이 누르고 Delete → 넣기 전과 같음
    f  Ctrl+Z 다섯 번 → 한 단계씩 정확히 (JSON) · Ctrl+Y 다섯 번 → 다시
    g  노면 띠 가장자리 · 깊이 방식 터파기 자른 선 ↔ 고친 선의 따로 계산한 투영 (중앙 ≤ 1.5 px)
    h  다시 열기 → «고친 관로 불러옴» + [버리기] · 고친 그대로 → 버리기 → 파일 그대로 · 다시 열면 알림 없음
    i  360: 켜자마자 (고르기 전) 모든 관 꼭짓점 손잡이가 보임 · 마우스 올리면 move · 고르지 않은 관 꼭짓점 바로 끌기 → 그 관만 바뀜 ·
       원판 눌러도 안 옮겨 감 · 화살표는 옮겨 감 · 점 찍어 맞추기와 하나만
  host   (hostmock · save_pipes · export_pipes_shp)
       끝난 고침마다 0.8 초 모아 save_pipes 한 번 (글 = 'window.RV_PIPES = ' + JSON + ';\\n', ctrl · edited) · 두 번 빨리 → 한 번 ·
       되돌리기도 저장 · [SHP로 내보내기] = 저장 먼저 → export_pipes_shp · 취소 · 못 씀 · «관로 넣기» 창 단추 · [처음] 전에 저장 · 이 브라우저 저장 안 함
  http   http 로 열어도 고치기 · 다시 열기 되살림
  legacy ctrl 없는 옛 pipes.js (make_synth --legacy-pipes): 모든 점이 손잡이 · 고치면 ctrl 생김 · 5 m 마다 나눔 · g · ll
  모든 쪽 JS 오류 0
"""
import argparse
import base64
import functools
import http.server
import json
import math
import os
import pathlib
import sys
import threading
import time
import urllib.parse

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import check_interact as ci  # noqa: E402
import check_trench as ct  # noqa: E402
import check_view as cv  # noqa: E402
import make_synth as ms  # noqa: E402
from cdp import Edge  # noqa: E402

VIEW = ('edit', '0002', 54.2, -53.4, 60)                 # 이름, 사진, yaw, pitch, fov (고친 꼭짓점 · 빨간 네모가 보임)
VIEW_O = ('pick', '0003', 67.0, -58.0, 90)               # E1 · E2 꼭짓점이 둘 다 보임
VIEW_M = ('mark', '0002', 90.0, -70.0, 90)               # 3 번 사진 원판이 보임
LM_RED = 1                                               # ms.LANDMARKS[1] = (13, 8) 빨강
TR_DEF = dict(tp=0.2, n=0.3, margin=0.3, bed=0.1)
SHIFT, CTRL = 8, 2
R = {'ok': True, 'errs': []}


def report(name, ok, text=''):
    print('   %-44s %s  %s' % (name, text, '통과' if ok else '실패'))
    R['ok'] &= bool(ok)
    return bool(ok)


def q_of(view, extra=''):
    _, shot, yaw, pitch, fov = view
    return 'shot=%s&yaw=%s&pitch=%s&fov=%s%s' % (shot, yaw, pitch, fov, extra)


# ---------------------------------------------------------------- 진짜 마우스 · 키 (CDP Input)
class Inp:
    def __init__(self, ed):
        self.ed = ed

    def _m(self, typ, x, y, buttons=0, mods=0, count=0):
        self.ed.call('Input.dispatchMouseEvent', {'type': typ, 'x': float(x), 'y': float(y), 'modifiers': mods,
                                                  'button': 'left' if (typ != 'mouseMoved' or buttons) else 'none',
                                                  'buttons': buttons, 'clickCount': count})

    def move(self, x, y):
        self._m('mouseMoved', x, y)
        self.ed.frames(2)

    def click(self, x, y, mods=0):
        self._m('mouseMoved', x, y, mods=mods)
        self._m('mousePressed', x, y, 1, mods, 1)
        self._m('mouseReleased', x, y, 0, mods, 1)
        self.ed.frames(2)

    def dblclick(self, x, y):
        self._m('mouseMoved', x, y)
        for c in (1, 2):
            self._m('mousePressed', x, y, 1, 0, c)
            self._m('mouseReleased', x, y, 0, 0, c)
        self.ed.frames(3)

    def press(self, x, y, mods=0):
        self._m('mouseMoved', x, y, mods=mods)
        self._m('mousePressed', x, y, 1, mods, 1)

    def drag_to(self, a, b, mods=0, steps=12):
        for i in range(1, steps + 1):
            t = i / steps
            self._m('mouseMoved', a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, 1, mods)
            time.sleep(0.02)

    def release(self, x, y, mods=0):
        self._m('mouseReleased', x, y, 0, mods, 1)
        self.ed.frames(3)

    def drag(self, a, b, mods=0, steps=12):
        self.press(a[0], a[1], mods)
        self.drag_to(a, b, mods, steps)
        self.release(b[0], b[1], mods)

    def key(self, key, code, vk, mods=0):
        for typ in ('rawKeyDown', 'keyUp'):
            self.ed.call('Input.dispatchKeyEvent', {'type': typ, 'key': key, 'code': code, 'windowsVirtualKeyCode': vk,
                                                    'nativeVirtualKeyCode': vk, 'modifiers': mods})
        self.ed.frames(3)

    def el_click(self, eid):
        # 오른쪽 칸 카테고리 (#opts · 굴림) 안 단추는 먼저 보이게 굴림 (1126 배치)
        self.ed.js("(function (e) { if (e && e.closest('#opts')) e.scrollIntoView({block: 'nearest'}); })(document.getElementById('%s'))" % eid)
        r = rect(self.ed, eid)
        self.click(r['left'] + r['width'] / 2, r['top'] + r['height'] / 2)


def rect(ed, eid):
    return json.loads(ed.js("JSON.stringify(document.getElementById('%s').getBoundingClientRect())" % eid))


def js_json(ed, expr):
    return json.loads(ed.js('JSON.stringify(%s)' % expr))


def pipes(ed):
    return js_json(ed, 'window.RV_PIPES')


def lines_json(P):
    return [json.dumps(l, sort_keys=True) for l in P['lines']]


# ---------------------------------------------------------------- 지도 (보기 화면 닮음 변환과 따로: 메르카토르 + make_synth en2ll 의 거꾸로)
def lon2x(lon):
    return (lon + 180.0) / 360.0


def lat2y(lat):
    s = math.sin(math.radians(lat))
    return 0.5 - math.log((1 + s) / (1 - s)) / (4 * math.pi)


def x2lon(x):
    return x * 360.0 - 180.0


def y2lat(y):
    return math.degrees(math.atan(math.sinh(math.pi * (1 - 2 * y))))


def ll2en(lat, lon):
    return ((lon - ms.LON0) * 111320.0 * math.cos(math.radians(ms.LAT0)), (lat - ms.LAT0) * 111320.0)


class MapTf:
    """처음 자리 지도 (사진 점 네모 가운데 · 줌 19 · 시험 사진은 가까워 전체 = 19)."""

    def __init__(self, ed):
        st = js_json(ed, "{r: document.getElementById('map').getBoundingClientRect(), s: window.RV_PROJECT.shots.map((s) => [s.lat, s.lon])}")
        r = st['r']
        xs = [lon2x(s[1]) for s in st['s']]
        ys = [lat2y(s[0]) for s in st['s']]
        self.cx, self.cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
        self.k = 256 * 2 ** 19
        self.l, self.t, self.w, self.h = r['left'], r['top'], round(r['width']), round(r['height'])

    def ll_scr(self, lat, lon):
        return (self.l + (lon2x(lon) - self.cx) * self.k + self.w / 2, self.t + (lat2y(lat) - self.cy) * self.k + self.h / 2)

    def en_scr(self, e, n):
        return self.ll_scr(*ms.en2ll(e, n))

    def scr_en(self, x, y):
        X = (x - self.l - self.w / 2) / self.k + self.cx
        Y = (y - self.t - self.h / 2) / self.k + self.cy
        return ll2en(y2lat(Y), x2lon(X))


# ---------------------------------------------------------------- 360 (보기 화면 약속: x/z·kx, y/z·ky)
class Cam:
    def __init__(self, ed, view):
        _, shot, yaw, pitch, fov = view
        s = next(q for q in js_json(ed, 'window.RV_PROJECT.shots') if q['id'] == shot)
        self.c = np.array([s['e'], s['n'], s['z']], float)
        self.f, self.r, self.u = cv.basis(yaw, pitch)
        r = rect(ed, 'ov')
        self.l, self.t, self.w, self.h = r['left'], r['top'], round(r['width']), round(r['height'])
        t = math.tan(math.radians(fov) / 2)
        self.tanh, self.tanv = (t, t * self.h / self.w) if self.w >= self.h else (t * self.w / self.h, t)
        self.kx, self.ky = (self.w / 2) / self.tanh, (self.h / 2) / self.tanv

    def proj(self, P, client=True):
        d = np.asarray(P, float) - self.c
        z = d @ self.f
        if z < 0.05:
            return None
        x, y = self.w / 2 + (d @ self.r) / z * self.kx, self.h / 2 - (d @ self.u) / z * self.ky
        return np.array([x + self.l, y + self.t]) if client else np.array([x, y])


# ---------------------------------------------------------------- 땅 높이 (시험 높이 타일 · 사진 땅 높이: 빌더 규칙을 따로 짬)
class Ground:
    def __init__(self, ed, slope):
        self.tiles = {}
        if slope is not None:  # make_synth 높이 타일 (dem.js 와 같은 내용) 을 따로 풀어 씀
            d = ms.dem_dict(slope)
            self.T, self.cell, self.x0, self.y0 = d['tile'], d['cell'], d['x0'], d['y0']
            self.tiles = {k: np.frombuffer(base64.b64decode(v), '<f4').reshape(self.T + 1, self.T + 1) for k, v in d['tiles'].items()}
        sh = js_json(ed, 'window.RV_PROJECT.shots')
        self.S = np.array([[s['e'], s['n']] for s in sh], float)
        self.G = np.array([s['groundZ'] if s.get('groundZ') is not None else s['z'] - s['agl'] for s in sh], float)
        self.dem = slope is not None

    def dem_at(self, e, n):
        if not self.dem:
            return math.nan
        fx, fy = (e - self.x0) / self.cell, (n - self.y0) / self.cell
        if fx < 0 or fy < 0:
            return math.nan
        ix, iy = int(fx // self.T), int(fy // self.T)
        a = self.tiles.get('%d_%d' % (ix, iy))
        if a is None:
            return math.nan
        c, r = fx - ix * self.T, fy - iy * self.T
        c0, r0 = min(int(c), self.T - 1), min(int(r), self.T - 1)
        tx, ty = c - c0, r - r0
        z = (a[r0, c0] * (1 - tx) + a[r0, c0 + 1] * tx) * (1 - ty) + (a[r0 + 1, c0] * (1 - tx) + a[r0 + 1, c0 + 1] * tx) * ty
        return float(z)

    def idw(self, e, n):
        d = np.hypot(self.S[:, 0] - e, self.S[:, 1] - n)
        idx = np.argsort(d)[:6]
        near = idx[d[idx] <= 40.0]
        if len(near) == 0:
            return float(self.G[idx[0]])
        w = 1.0 / np.maximum(d[near], 1.0) ** 2
        return float((self.G[near] * w).sum() / w.sum())

    def at(self, e, n):
        z = self.dem_at(e, n)
        return z if math.isfinite(z) else self.idw(e, n)


# ---------------------------------------------------------------- 고친 줄 검사 (빌더 규칙과 따로)
def check_line(name, ln, ground, want_cp=None, tol=0.05):
    """ctrl · 5 m 나눔 · 곧은 마디 위 · hasZ z 선형 · g · ll. want_cp = 바라는 꼭짓점 [(e, n)] (None 이면 안 봄)."""
    P = ln['pts']
    c = ln.get('ctrl')
    ok = isinstance(c, list) and len(c) >= 2 and c[0] == 0 and c[-1] == len(P) - 1 and all(b > a for a, b in zip(c, c[1:]))
    if not ok:
        return report(name + ' ctrl', False, json.dumps(c))
    cp = [P[k] for k in c]
    bad_seg, max_off, max_sp = 0, 0.0, 0.0
    for j in range(len(c) - 1):
        A, B = np.array(cp[j][:2]), np.array(cp[j + 1][:2])
        L = float(np.linalg.norm(B - A))
        m = max(1, math.ceil(L / ms.EDIT_STEP))
        if c[j + 1] - c[j] != m:
            bad_seg += 1
        for k in range(c[j], c[j + 1] + 1):
            q = np.array(P[k][:2])
            t = (k - c[j]) / (c[j + 1] - c[j])
            max_off = max(max_off, float(np.linalg.norm(q - (A + (B - A) * t))))
            if ln.get('hasZ') and cp[j][2] is not None and cp[j + 1][2] is not None:
                max_off = max(max_off, abs(P[k][2] - (cp[j][2] + (cp[j + 1][2] - cp[j][2]) * t)))
            elif not ln.get('hasZ') and P[k][2] is not None:
                bad_seg += 1
            if k > c[j]:
                max_sp = max(max_sp, float(np.linalg.norm(q - np.array(P[k - 1][:2]))))
    report(name + ' 5 m 마다 곧게 나눔', bad_seg == 0 and max_off <= 0.002 and max_sp <= ms.EDIT_STEP + 1e-6,
           '점 %d · 꼭짓점 %d · 곧은 선에서 최대 %.4f m · 간격 최대 %.3f m' % (len(P), len(c), max_off, max_sp))
    dg = [abs(g - ground.at(p[0], p[1])) for p, g in zip(P, ln['g'])]
    nd = sum(1 for p in P if math.isfinite(ground.dem_at(p[0], p[1])))
    report(name + ' g = 땅 높이', len(ln['g']) == len(P) and max(dg) <= 0.01,
           '최대 차이 %.4f m (높이 타일 %d 점 · 사진 땅 %d 점)' % (max(dg), nd, len(P) - nd))
    dl = [math.hypot(*(np.array(ll2en(*q)) - np.array(p[:2]))) for p, q in zip(P, ln['ll'])]
    report(name + ' ll ↔ en', len(ln['ll']) == len(P) and max(dl) <= 0.2, '최대 %.4f m' % max(dl))
    if want_cp is not None:
        de = [math.hypot(a[0] - b[0], a[1] - b[1]) for a, b in zip(cp, want_cp)]
        report(name + ' 꼭짓점 자리', len(cp) == len(want_cp) and max(de) <= tol,
               ' · '.join('(%.3f, %.3f)' % (p[0], p[1]) for p in cp) + ' · 최대 어긋남 %.3f m' % max(de))
    return cp


def ctrl_pts(ln):
    return [ln['pts'][k] for k in (ln.get('ctrl') or range(len(ln['pts'])))]


def changed_near(png_off, png_on, pts, box, r=6, thr=25):
    """화면 점 (client) 마다 둘레 ±r px 에서 두 장이 thr 넘게 다른 화소 수 · box (l, t, w, h) 안 (12 px 여유) 의 점만."""
    a = np.asarray(Image.open(png_off).convert('RGB')).astype(int)
    b = np.asarray(Image.open(png_on).convert('RGB')).astype(int)
    l, t, w, h = box
    out = []
    for q in pts:
        if q is None or not (l + 12 < q[0] < l + w - 12 and t + 12 < q[1] < t + h - 12):
            continue
        x, y = int(round(q[0])), int(round(q[1]))
        win = np.abs(a[y - r:y + r + 1, x - r:x + r + 1] - b[y - r:y + r + 1, x - r:x + r + 1]).max(axis=2)
        out.append(int((win > thr).sum()))
    return out


def errs(ed, where):
    e = cv.errors(ed)
    for x in e:
        R['errs'].append((where, str(x)[:200]))
    return report('JS 오류 (%s)' % where, not e, '%d 줄' % len(e))


def load(ed, url):
    ed.goto(url)
    ok = ed.wait_js(cv.READY, timeout=60)
    ed.frames(3)
    return ok


def red_centroid(png, cam, want):
    """사진에서 빨간 네모 (230, 40, 40) 가운데 (client 좌표) · 따로 투영한 자리 근처 60 px 안."""
    a = np.asarray(Image.open(png).convert('RGB')).astype(int)
    col = np.array(ms.LANDMARKS[LM_RED][2])
    m = np.abs(a - col).max(axis=2) < 40
    ys, xs = np.nonzero(m)
    sel = np.hypot(xs + 0.5 - want[0], ys + 0.5 - want[1]) < 60
    if sel.sum() < 30:
        return None, int(sel.sum())
    return np.array([xs[sel].mean() + 0.5, ys[sel].mean() + 0.5]), int(sel.sum())


# ---------------------------------------------------------------- 노면 띠 · 터파기 자른 선 (따로 계산한 투영)
def band_measure(on, off, cam, ln, others, skip=0.4, step=0.25):
    """고친 관 띠 가장자리 (켬 − 끔 차이 절반) ↔ 지표 ±관경/2 투영. [(lo−p_lo, hi−p_hi)] px."""
    P, G, hw = ln['pts'], ln['g'], ln['dia'] / 2
    out = []
    for j in range(len(P) - 1):
        A, B = np.array(P[j][:2]), np.array(P[j + 1][:2])
        L = float(np.linalg.norm(B - A))
        if L < 2 * skip + step:
            continue
        t = (B - A) / L
        nl = np.array([-t[1], t[0]])
        for a in np.arange(skip, L - skip, step):
            c2 = A + t * a
            if any(cv.dist_poly(c2[0], c2[1], v) < 2.0 for v in others):
                continue
            z = G[j] + (G[j + 1] - G[j]) * a / L
            pts = [np.array([*c2, z]), np.array([*(c2 - t * 0.5), z]), np.array([*(c2 + t * 0.5), z]),
                   np.array([*(c2 + nl * hw), z]), np.array([*(c2 + nl * hw + t), z]),
                   np.array([*(c2 - nl * hw), z]), np.array([*(c2 - nl * hw + t), z])]
            q = [cam.proj(p, client=False) for p in pts]
            if any(v is None for v in q):
                continue
            c = q[0]
            d = q[2] - q[1]
            d /= np.hypot(*d)
            nrm = np.array([-d[1], d[0]])
            pa = cv.line_hit(c, nrm, q[3], q[4] - q[3])
            pb = cv.line_hit(c, nrm, q[5], q[6] - q[5])
            p_lo, p_hi = sorted((pa, pb))
            Rr = max(abs(p_lo), abs(p_hi)) + 7
            if not (Rr < 120 and Rr < c[0] < cam.w - Rr and Rr < c[1] < cam.h - Rr):
                continue
            s = np.arange(-Rr, Rr + 0.001, 0.25)
            sp = c[None, :] + s[:, None] * nrm[None, :]
            Aa, Bb = cv.bilinear(on, sp), cv.bilinear(off, sp)
            if not (np.isfinite(Aa).all() and np.isfinite(Bb).all()) or (Aa.max(axis=1) < 60).any():
                continue  # 이름표 · 밖
            dd = np.sqrt(((Aa - Bb) ** 2).sum(axis=1))
            inner = np.abs(s) <= max(0.0, min(abs(p_lo), abs(p_hi)) - 1.5)
            if inner.sum() < 3:
                continue
            level = np.median(dd[inner])
            bd = cv.cross_at(dd, s, 0.5 * level) if level > 40 else None
            if bd is None:
                continue
            out.append((bd[0] - p_lo, bd[1] - p_hi))
    return np.array(out).reshape(-1, 2)


def rim_edges(ln, tr, cover):
    """터파기 지표 구멍 가장자리 (보기 화면 trSecGet 과 따로 짬): 꼭짓점마다 가로 = 두 마디 왼쪽 법선 이등분 · 1/cos ·
    끝은 여유폭 (최소 5 cm) 만큼 더 · 반폭 wv = wb + n·max(0, (g − tp) − 바닥) · 높이 = 그 꼭짓점 g."""
    P = [np.array(p[:2], float) for p in ln['pts']]
    G = ln['g']
    m, D = len(P), ln['dia']
    t = [(P[j + 1] - P[j]) / np.linalg.norm(P[j + 1] - P[j]) for j in range(m - 1)]
    nl = [np.array([-q[1], q[0]]) for q in t]
    lat, wv = [], []
    for j in range(m):
        a, b = max(0, j - 1), min(m - 2, j)
        q = nl[a] + nl[b]
        q = q / np.linalg.norm(q)
        lat.append(q / max(q @ nl[b], 0.3))
        g = G[j]
        zc = g - cover - D / 2
        zs = min(zc - D / 2, g - 0.01)
        zb = zs - tr['bed']
        wv.append(D / 2 + tr['margin'] + tr['n'] * max(0.0, (g - tr['tp']) - zb))
    ext = max(tr['margin'], 0.05)
    pos = [p.copy() for p in P]
    pos[0] = pos[0] - t[0] * ext
    pos[-1] = pos[-1] + t[-1] * ext
    out = []
    for j in range(m - 1):
        for sg in (1, -1):
            out.append((np.array([*(pos[j] + sg * lat[j] * wv[j]), G[j]]), np.array([*(pos[j + 1] + sg * lat[j + 1] * wv[j + 1]), G[j + 1]])))
    return out


def cut_measure(img, gl, cam, edges, others, skip=0.3, step=0.2):
    """어두운 자른 선 가운데 − 투영한 가장자리 (check_trench.measure_cut 과 같은 재기, 높이는 꼭짓점마다 · gl = 겹쳐 그림 숨김)."""
    out = []
    for EA, EB in edges:
        L = float(np.linalg.norm(EB[:2] - EA[:2]))
        for a in np.arange(skip, L - skip, step):
            P = EA + (EB - EA) * (a / L)
            if any(cv.dist_poly(P[0], P[1], v) < 2.5 for v in others):
                continue
            p0, q0 = cam.proj(P, client=False), cam.proj(P + (EB - EA) / L * 0.3, client=False)
            if p0 is None or q0 is None or not (12 < p0[0] < cam.w - 12 and 12 < p0[1] < cam.h - 12):
                continue
            sd = (q0 - p0) / np.hypot(*(q0 - p0))
            x = ct.dark_line_at(img, gl, p0, np.array([-sd[1], sd[0]]))
            if x is not None:
                out.append(x)
    return np.array(out)


def stat(v):
    v = np.abs(np.asarray(v, float)).ravel()
    return (float(np.median(v)), float(np.percentile(v, 90))) if v.size else (math.nan, math.nan)


def view_img(png, cam):
    a = np.asarray(Image.open(png).convert('RGB')).astype(float)
    x0, y0 = int(round(cam.l)), int(round(cam.t))
    return a[y0:y0 + cam.h, x0:x0 + cam.w]


# ---------------------------------------------------------------- 쪽 안 기록: WebGL 그물 올림 (끄는 동안 무엇을 다시 올리나)
GL_HOOK = """(function () {
  if (window.__glh) return;
  const P = WebGL2RenderingContext.prototype, bd = P.bufferData, ids = new Map();
  window.__glh = { log: [] };
  P.bufferData = function (target, data, usage) {
    if (target === this.ARRAY_BUFFER) {
      const b = this.getParameter(this.ARRAY_BUFFER_BINDING);
      if (!ids.has(b)) ids.set(b, ids.size + 1);
      window.__glh.log.push([ids.get(b), data && data.byteLength || 0]);
    }
    return bd.apply(this, arguments);
  };
})()"""


# ---------------------------------------------------------------- 시험
def run_file(ed, out_dir, shots_dir, ground, page='로드뷰.html', tag='file'):
    inp = Inp(ed)
    url = pathlib.Path(out_dir, page).resolve().as_uri() + '?' + q_of(VIEW, '&mode=depth')
    load(ed, url)
    ed.js('localStorage.clear()')
    ed.js(GL_HOOK)
    report('불러옴', load(ed, url))
    ed.js(GL_HOOK)
    P0 = pipes(ed)
    snaps = [lines_json(P0)]
    mp = MapTf(ed)
    print('== a  지도: 고르기 · 손잡이 끌기 (깊이 방식 · 터파기 켬)')
    p_off = os.path.join(shots_dir, tag + '_a_edit_off.png')
    ed.frames(3)
    ed.screenshot(p_off)
    inp.el_click('tgEdit')
    ed.frames(3)
    p_on = os.path.join(shots_dir, tag + '_a_edit_on.png')
    ed.screenshot(p_on)
    vx = [mp.en_scr(q[0], q[1]) for ln in P0['lines'] for q in ctrl_pts(ln)]
    cnt = changed_near(p_off, p_on, vx, (mp.l, mp.t, mp.w, mp.h))
    report('켜자마자 (고르기 전) 지도에 모든 관 꼭짓점 손잡이', len(cnt) >= 5 and min(cnt) >= 8, '꼭짓점 %d 개 · 바뀐 화소 %s' % (len(cnt), cnt))
    st = js_json(ed, "{on: document.getElementById('tgEdit').classList.contains('on'), chip: document.getElementById('edChip').classList.contains('show'),"
                     " tip: document.getElementById('edTip').textContent, shp: document.getElementById('edShp').hidden, drop: document.getElementById('edDrop').hidden}")
    report('[관로 고치기] → 켜짐 · 안내 · SHP 단추 숨김 (프로그램 없음)', st['on'] and st['chip'] and st['shp'] and st['drop']
           and st['tip'] == '관로 고치기 · 점 끌기 · 두 번 누르기 = 점 추가 · Delete = 삭제 · Shift 끌기 = 통째로', st['tip'])
    sel_at = mp.en_scr(-10.0, 5.0)
    ed.js('window.__glh.log = []')
    inp.click(*sel_at)
    sel_log = js_json(ed, 'window.__glh.log')
    h0 = mp.en_scr(15.0, 5.0)
    inp.move(*h0)
    report('지도에서 E1 누름 → 손잡이 (마우스 올리면 move)', 'move' in js_json(ed, "[...document.getElementById('map').classList]"))
    ed.screenshot(os.path.join(shots_dir, tag + '_a0_selected.png'))
    p0 = (round(h0[0]), round(h0[1]))
    p1 = (p0[0] + 20, p0[1] - 12)
    ed.js('window.__glh.log = []')
    inp.press(*p0)
    mid = (p0[0] + 10, p0[1] - 6)
    inp.drag_to(p0, mid, steps=6)
    ed.frames(3)
    live = pipes(ed)['lines'][0]
    want_mid = np.array([15.0, 5.0]) + np.array(mp.scr_en(*mid)) - np.array(mp.scr_en(*p0))
    lv = ctrl_pts(live)[1]
    report('끄는 동안 (놓기 전) 꼭짓점이 따라옴', math.hypot(lv[0] - want_mid[0], lv[1] - want_mid[1]) <= 0.05,
           '(%.3f, %.3f) · 바람 (%.3f, %.3f)' % (lv[0], lv[1], want_mid[0], want_mid[1]))
    ed.screenshot(os.path.join(shots_dir, tag + '_a1_dragging.png'))
    inp.drag_to(mid, p1, steps=6)
    inp.release(*p1)
    drag_log = js_json(ed, 'window.__glh.log')
    sel_ids, drag_ids = {i for i, _ in sel_log}, {i for i, _ in drag_log}
    report('끄는 동안 WebGL 그물 = 고른 관 것만 다시', len(sel_ids) == 4 and len(drag_ids) == 2 and drag_ids <= sel_ids and len(drag_log) >= 6,
           '고를 때 그물 %d 개 (%s) · 끄는 동안 %d 번 올림 · 그물 %d 개 (%s)'
           % (len(sel_ids), sorted(sel_ids), len(drag_log), len(drag_ids), sorted(drag_ids)))
    P1 = pipes(ed)
    d_en = np.array(mp.scr_en(*p1)) - np.array(mp.scr_en(*p0))
    want = [(-30.0, 5.0), (15.0 + d_en[0], 5.0 + d_en[1]), (45.0, 20.0)]
    print('   지도 끌기 +20, −12 px → 바라는 이동 (%.3f, %.3f) m' % tuple(d_en))
    check_line('a E1', P1['lines'][0], ground, want)
    report('a 다른 관 그대로', lines_json(P1)[1:] == snaps[0][1:])
    report('a [고친 것 버리기] 보임 (이 브라우저 저장)', not ed.js("document.getElementById('edDrop').hidden")
           and ed.js("!!localStorage.getItem('rv_pipes:' + window.RV_PROJECT.title)"))
    ed.screenshot(os.path.join(shots_dir, tag + '_a2_map_drag.png'))
    snaps.append(lines_json(P1))

    print('== b  360: 손잡이를 빨간 네모로 (노면 방식)')
    ed.js("document.getElementById('mSurf').click()")  # 깊이 방식 구덩이가 네모를 가리지 않게 · 고치기는 그대로
    cam = Cam(ed, VIEW)
    ed.js("document.getElementById('ov').style.visibility = 'hidden'")  # 사진만 찍어 네모 찾기
    ed.frames(3)
    png = os.path.join(shots_dir, tag + '_b0_photo.png')
    ed.screenshot(png)
    ed.js("document.getElementById('ov').style.visibility = 'visible'")
    ed.frames(3)
    report('노면으로 바꿔도 고치기 그대로', ed.js("document.getElementById('edChip').classList.contains('show')"))
    lm = ms.LANDMARKS[LM_RED]
    lm3 = np.array([lm[0], lm[1], ground.at(lm[0], lm[1])])
    lm_s = cam.proj(lm3)
    red, npx = red_centroid(png, cam, lm_s)
    report('사진의 빨간 네모 ↔ 따로 투영', red is not None and np.hypot(*(red - lm_s)) <= 2.0,
           '화소 %d · 찾음 (%.1f, %.1f) · 투영 (%.1f, %.1f)' % (npx, *(red if red is not None else (0, 0)), *lm_s))
    cp = ctrl_pts(P1['lines'][0])[1]
    k1 = P1['lines'][0]['ctrl'][1]
    hs = cam.proj(np.array([cp[0], cp[1], P1['lines'][0]['g'][k1]]))
    a = (round(hs[0]), round(hs[1]))
    b = (round(red[0]), round(red[1])) if red is not None else (round(lm_s[0]), round(lm_s[1]))
    inp.move(*a)
    report('360 손잡이에 마우스 → move', 'move' in js_json(ed, "[...document.getElementById('ov').classList]"))
    ed.screenshot(os.path.join(shots_dir, tag + '_b1_hover.png'))
    inp.drag(a, b, steps=14)
    P2 = pipes(ed)
    want = [(-30.0, 5.0), (lm[0], lm[1]), (45.0, 20.0)]
    check_line('b E1', P2['lines'][0], ground, want, tol=0.10)
    ed.screenshot(os.path.join(shots_dir, tag + '_b2_view_drag.png'))
    snaps.append(lines_json(P2))

    print('== c  Shift + 끌기 (지도) → 통째로')
    cps = ctrl_pts(P2['lines'][0])
    h0 = mp.en_scr(cps[0][0], cps[0][1])
    a = (round(h0[0]), round(h0[1]))
    b = (a[0] - 15, a[1] + 10)
    inp.drag(a, b, mods=SHIFT)
    P3 = pipes(ed)
    d_en = np.array(mp.scr_en(*b)) - np.array(mp.scr_en(*a))
    cps3 = ctrl_pts(P3['lines'][0])
    dd = np.array([[q[0] - p[0], q[1] - p[1]] for p, q in zip(cps, cps3)])
    report('c 꼭짓점 모두 같은 만큼', len(cps3) == 3 and np.ptp(dd[:, 0]) <= 0.002 and np.ptp(dd[:, 1]) <= 0.002
           and np.hypot(*(dd[0] - d_en)) <= 0.05, '이동 %s · 바람 (%.3f, %.3f)' % (json.dumps(np.round(dd, 3).tolist()), *d_en))
    check_line('c E1', P3['lines'][0], ground)
    snaps.append(lines_json(P3))

    print('== d  두 번 누르기 → 꼭짓점 넣기')
    A, B = np.array(cps3[0][:2]), np.array(cps3[1][:2])
    midp = (A + B) / 2
    ms_ = mp.en_scr(*midp)
    at = (round(ms_[0]), round(ms_[1]))
    inp.dblclick(*at)
    P4 = pipes(ed)
    cps4 = ctrl_pts(P4['lines'][0])
    ok = len(cps4) == 4
    if ok:
        q = np.array(cps4[1][:2])
        u, v = B - A, q - A
        off = abs(float(u[0] * v[1] - u[1] * v[0])) / float(np.linalg.norm(u))
        dclick = float(np.linalg.norm(q - np.array(mp.scr_en(*at))))
        report('d 꼭짓점 하나 더 · 그 마디 위 · 누른 곳', off <= 0.01 and dclick <= 0.35,
               '(%.3f, %.3f) · 마디에서 %.4f m · 누른 곳에서 %.3f m' % (q[0], q[1], off, dclick))
    else:
        report('d 꼭짓점 하나 더', False, '꼭짓점 %d' % len(cps4))
    check_line('d E1', P4['lines'][0], ground)
    snaps.append(lines_json(P4))
    ed.screenshot(os.path.join(shots_dir, tag + '_d_insert.png'))

    print('== e  손잡이 누르고 Delete')
    hq = mp.en_scr(*cps4[1][:2]) if ok else (0, 0)
    inp.click(round(hq[0]), round(hq[1]))
    inp.key('Delete', 'Delete', 46)
    P5 = pipes(ed)
    report('e 지움 → 넣기 전과 같음 (JSON)', lines_json(P5) == snaps[3], '꼭짓점 %d' % len(ctrl_pts(P5['lines'][0])))
    inp.click(round(mp.en_scr(*cps3[0][:2])[0]), round(mp.en_scr(*cps3[0][:2])[1]))
    inp.key('Delete', 'Delete', 46)
    inp.key('Delete', 'Delete', 46)
    inp.click(round(mp.en_scr(*cps3[1][:2])[0]), round(mp.en_scr(*cps3[1][:2])[1]))
    inp.key('Delete', 'Delete', 46)
    P6 = pipes(ed)
    tst = ed.js("document.getElementById('toast').textContent")
    report('e 꼭짓점 2 개는 남김', len(ctrl_pts(P6['lines'][0])) == 2 and tst == '점 2개는 남김', '꼭짓점 %d · 알림 %s' % (len(ctrl_pts(P6['lines'][0])), tst))
    check_line('e E1 (2 점)', P6['lines'][0], ground)
    snaps += [lines_json(P5), lines_json(P6)]  # [5] = 넣은 것 지움 · [6] = 처음 꼭짓점도 지움 (2 점)

    print('== f  Ctrl+Z · Ctrl+Y')
    seq_ok = []
    for i in range(6, 0, -1):
        inp.key('z', 'KeyZ', 90, CTRL)
        seq_ok.append(lines_json(pipes(ed)) == snaps[i - 1])
    inp.key('z', 'KeyZ', 90, CTRL)
    t0 = ed.js("document.getElementById('toast').textContent")
    report('f Ctrl+Z 여섯 번 → 한 단계씩 정확히', all(seq_ok) and t0 == '되돌릴 것 없음', json.dumps(seq_ok) + ' · ' + t0)
    seq_ok = []
    for i in range(1, 7):
        inp.key('y', 'KeyY', 89, CTRL)
        seq_ok.append(lines_json(pipes(ed)) == snaps[i])
    report('f Ctrl+Y 여섯 번 → 다시', all(seq_ok), json.dumps(seq_ok))
    inp.key('z', 'KeyZ', 90, CTRL)  # 하나 덜 지운 (3 꼭짓점) 모양으로
    report('f 하나 되돌림 = [5]', lines_json(pipes(ed)) == snaps[5])
    final = pipes(ed)
    errs(ed, tag + ' a~f')

    print('== g  노면 띠 · 터파기 자른 선 ↔ 따로 계산한 투영')
    inp.key('Escape', 'Escape', 27)
    report('Esc → 고치기 끝', not ed.js("document.getElementById('edChip').classList.contains('show')")
           and not ed.js("document.getElementById('tgEdit').classList.contains('on')"))
    E1 = final['lines'][0]
    others = [[tuple(p[:2]) for p in l['pts']] for l in final['lines'][1:]]
    ed.js("document.getElementById('mSurf').click()")
    ed.frames(3)
    p_on = os.path.join(shots_dir, tag + '_g_surface_on.png')
    ed.screenshot(p_on)
    ed.js("document.getElementById('tgPipes').click()")
    ed.frames(3)
    p_off = os.path.join(shots_dir, tag + '_g_surface_off.png')
    ed.screenshot(p_off)
    ed.js("document.getElementById('tgPipes').click()")
    ed.frames(3)
    cam = Cam(ed, VIEW)
    r = band_measure(view_img(p_on, cam), view_img(p_off, cam), cam, E1, others)
    m, p = stat(r)
    report('g 노면 띠 가장자리 (고친 E1)', len(r) >= 40 and m <= 1.5, '표본 %d · 중앙 %.2f px · 90%% %.2f px' % (len(r), m, p))
    ed.js("document.getElementById('mDepth').click()")
    ed.frames(3)
    p_d = os.path.join(shots_dir, tag + '_g_trench.png')
    ed.screenshot(p_d)
    ed.js("document.getElementById('ov').style.visibility = 'hidden'")
    ed.frames(3)
    p_dg = os.path.join(shots_dir, tag + '_g_trench_gl.png')
    ed.screenshot(p_dg)
    ed.js("document.getElementById('ov').style.visibility = ''")
    ed.frames(2)
    cover = float(ed.js("Number(document.getElementById('cover').value)"))
    e = cut_measure(view_img(p_d, cam), view_img(p_dg, cam), cam, rim_edges(E1, TR_DEF, cover), others)
    m, p = stat(e)
    report('g 터파기 자른 선 (고친 E1)', e.size >= 40 and m <= 1.5, '표본 %d · 중앙 %.2f px · 90%% %.2f px' % (e.size, m, p))
    errs(ed, tag + ' g')
    return P0, final


def run_restore(ed, out_dir, shots_dir, P0, final, page='로드뷰.html', tag='file', base=None):
    inp = Inp(ed)
    print('== h  다시 열기 → 이 브라우저에서 되살림 · 버리기')
    url = (base or pathlib.Path(out_dir, page).resolve().as_uri()) + '?' + q_of(VIEW)
    load(ed, url)
    ed.frames(3)
    st = js_json(ed, "{t: document.getElementById('toast').textContent, show: document.getElementById('toast').classList.contains('show'),"
                     " b: !!document.querySelector('#toast button')}")
    report('h 알림 «고친 관로 불러옴» + [버리기]', st['show'] and st['t'] == '고친 관로 불러옴버리기' and st['b'], st['t'])
    P = pipes(ed)
    report('h 고친 그대로 (JSON) · edited', lines_json(P) == lines_json(final) and P.get('edited') is True)
    ed.screenshot(os.path.join(shots_dir, tag + '_h_restored.png'))
    r = js_json(ed, "document.querySelector('#toast button').getBoundingClientRect()")
    inp.click(r['left'] + r['width'] / 2, r['top'] + r['height'] / 2)
    P = pipes(ed)
    report('h [버리기] → 파일 그대로 · 저장 지움', lines_json(P) == lines_json(P0) and 'edited' not in P
           and not ed.js("!!localStorage.getItem('rv_pipes:' + window.RV_PROJECT.title)")
           and ed.js("document.getElementById('toast').textContent") == '고친 것 버림')
    load(ed, url)
    ed.frames(3)
    t = ed.js("document.getElementById('toast').textContent")
    report('h 다시 열기 → 알림 없음 · 파일 그대로', t != '고친 관로 불러옴버리기' and lines_json(pipes(ed)) == lines_json(P0), repr(t))
    errs(ed, tag + ' h')


def run_pick(ed, out_dir, shots_dir, ground):
    inp = Inp(ed)
    print('== i  360 에서 관 고르기 · 원판 · 화살표 · 점 찍어 맞추기와 하나만')
    url = pathlib.Path(out_dir, '로드뷰.html').resolve().as_uri() + '?' + q_of(VIEW_O)
    load(ed, url)
    ed.js('localStorage.clear()')
    load(ed, url)
    cam = Cam(ed, VIEW_O)
    on_line = lambda e, n: cam.proj(np.array([e, n, ground.at(e, n)]))  # noqa: E731
    is_move = lambda: 'move' in js_json(ed, "[...document.getElementById('ov').classList]")  # noqa: E731
    p_off = os.path.join(shots_dir, 'i_edit_off.png')
    ed.frames(3)
    ed.screenshot(p_off)
    inp.el_click('tgEdit')
    ed.frames(3)
    p_on = os.path.join(shots_dir, 'i_edit_on.png')
    ed.screenshot(p_on)
    P = pipes(ed)
    cnt = changed_near(p_off, p_on, [on_line(q[0], q[1]) for ln in P['lines'] for q in ctrl_pts(ln)], (cam.l, cam.t, cam.w, cam.h))
    report('i 켜자마자 (고르기 전) 360 에 모든 관 꼭짓점 손잡이', len(cnt) >= 3 and min(cnt) >= 8, '보이는 꼭짓점 %d 개 · 바뀐 화소 %s' % (len(cnt), cnt))
    v1 = on_line(15.0, 5.0)
    v2 = on_line(22.0, 0.0)
    inp.move(*np.round(v1))
    m1 = is_move()
    inp.move(*np.round(v2))
    m2 = is_move()
    ed.screenshot(os.path.join(shots_dir, 'i_hover_E2.png'))
    report('i 고르기 전 꼭짓점 마우스 올림 → move (E1 · E2)', m1 and m2, 'E1 꼭짓점 %s · E2 꼭짓점 %s' % (m1, m2))
    a = np.round(v2)
    inp.drag(a, a + np.array([0, -25]))
    P2 = pipes(ed)
    cp = ctrl_pts(P2['lines'][1])
    report('i 고르지 않은 E2 (hasZ) 꼭짓점 바로 끌기 → 관 중심 표고 그대로 · 다른 관 그대로', len(cp) == 3 and [round(q[2], 3) for q in cp] == [47.0, 47.5, 48.5]
           and math.hypot(cp[1][0] - 22.0, cp[1][1]) > 0.5 and lines_json(P2)[0] == lines_json(P)[0] and lines_json(P2)[2] == lines_json(P)[2],
           ' · '.join('(%.2f, %.2f, %.2f)' % tuple(q) for q in cp))
    check_line('i E2', P2['lines'][1], ground)
    # 360: Shift + 관 몸통 (손잡이 아님) 끌기 → 통째로
    seg_pt = lambda A, B, n: np.array(A[:2]) + (np.array(B[:2]) - np.array(A[:2])) * ((n - A[1]) / (B[1] - A[1]))  # noqa: E731
    q = seg_pt(cp[0], cp[1], -2.0)
    a = np.round(on_line(*q))
    inp.drag(a, a + np.array([-20, 0]), mods=SHIFT)
    P3 = pipes(ed)
    cp3 = ctrl_pts(P3['lines'][1])
    dd = np.array([[v[0] - u[0], v[1] - u[1]] for u, v in zip(cp, cp3)])
    report('i 360 Shift + 관 몸통 끌기 → 통째로 (z 그대로)', len(cp3) == 3 and np.ptp(dd[:, 0]) <= 0.002 and np.ptp(dd[:, 1]) <= 0.002
           and np.hypot(*dd[0]) > 0.2 and [round(v[2], 3) for v in cp3] == [47.0, 47.5, 48.5], json.dumps(np.round(dd, 3).tolist()))
    # 360: 두 번 누르기 → 꼭짓점 넣기 (hasZ: z 는 마디를 따라 선형) · Backspace 지움
    q = seg_pt(cp3[1], cp3[2], 3.0)
    at = np.round(on_line(*q))
    inp.dblclick(*at)
    P4 = pipes(ed)
    cp4 = ctrl_pts(P4['lines'][1])
    ok = len(cp4) == 4
    if ok:
        A, B, X = np.array(cp3[1][:2]), np.array(cp3[2][:2]), np.array(cp4[2][:2])
        u, v = B - A, X - A
        off = abs(float(u[0] * v[1] - u[1] * v[0])) / float(np.linalg.norm(u))
        zw = cp3[1][2] + (cp3[2][2] - cp3[1][2]) * float(np.linalg.norm(v)) / float(np.linalg.norm(u))
        report('i 360 두 번 누르기 → 꼭짓점 (마디 위 · z 선형)', off <= 0.01 and abs(cp4[2][2] - zw) <= 0.002 and np.linalg.norm(X - q) <= 0.3,
               '(%.3f, %.3f, %.3f) · 마디에서 %.4f m · z 바람 %.3f · 누른 곳에서 %.3f m' % (*cp4[2], off, zw, np.linalg.norm(X - q)))
    else:
        report('i 360 두 번 누르기 → 꼭짓점', False, '꼭짓점 %d' % len(cp4))
    check_line('i E2 넣음', P4['lines'][1], ground)
    inp.key('Backspace', 'Backspace', 8)
    report('i Backspace → 넣은 꼭짓점 지움 (넣기 전과 같음)', lines_json(pipes(ed)) == lines_json(P3))
    ed.screenshot(os.path.join(shots_dir, 'i_E2_edits.png'))
    # 원판 · 화살표
    url = pathlib.Path(out_dir, '로드뷰.html').resolve().as_uri() + '?' + q_of(VIEW_M)
    load(ed, url)
    cam = Cam(ed, VIEW_M)
    inp.el_click('tgEdit')
    s3 = next(q for q in js_json(ed, 'window.RV_PROJECT.shots') if q['id'] == '0003')
    mk = cam.proj(np.array([s3['e'], s3['n'], s3['groundZ']]))
    inp.click(*np.round(mk))
    # 1124 JACK: 관로 고치기 중에도 사진 원판을 누르면 그 사진으로 (고치기는 그대로)
    moved = ed.wait_js("document.getElementById('shotNo').textContent === '3 / 4'", 10)
    n1 = ed.js("document.getElementById('shotNo').textContent")
    inp.key('PageUp', 'PageUp', 33)  # 다시 2 번으로
    back = ed.wait_js("document.getElementById('shotNo').textContent === '2 / 4'", 10)
    inp.key('ArrowUp', 'ArrowUp', 38)  # 동쪽을 보는 중 → 앞 = 3 번 (10, 0)
    ok = ed.wait_js("document.getElementById('shotNo').textContent === '3 / 4'", 10)
    report('i 고치는 동안 원판 누름 → 그 사진 · 화살표도 옮겨 감 · 고치기 그대로',
           moved and back and ok and ed.js("document.getElementById('edChip').classList.contains('show')"),
           '원판 → %s · 화살표 → %s' % (n1, ed.js("document.getElementById('shotNo').textContent")))
    ed.js("document.getElementById('calHead').click()")
    ed.frames(2)
    inp.el_click('alGo')
    st = js_json(ed, "{ed: document.getElementById('tgEdit').classList.contains('on'), chip: document.getElementById('edChip').classList.contains('show'), al: document.getElementById('alChip').classList.contains('show')}")
    report('i 점 찍어 맞추기 켬 → 고치기 꺼짐', not st['ed'] and not st['chip'] and st['al'], json.dumps(st))
    inp.el_click('tgEdit')
    st = js_json(ed, "{ed: document.getElementById('tgEdit').classList.contains('on'), al: document.getElementById('alChip').classList.contains('show')}")
    report('i 고치기 켬 → 점 찍어 맞추기 꺼짐', st['ed'] and not st['al'], json.dumps(st))
    ed.js("document.getElementById('tgPipes').click()")
    ed.frames(2)
    report('i 관로 끔 → 고치기도 끝', not ed.js("document.getElementById('tgEdit').classList.contains('on')"))
    errs(ed, 'i')


def run_host(ed, out_dir, shots_dir):
    inp = Inp(ed)
    print('== host  save_pipes · export_pipes_shp (hostmock)')
    page = ci.prepare(out_dir, host=True)
    try:
        url = pathlib.Path(out_dir, page).resolve().as_uri() + '?t=edit_host&' + q_of(VIEW)
        load(ed, url)
        ed.js('localStorage.clear()')
        load(ed, url)
        calls = lambda name=None: [c for c in js_json(ed, 'window.__host.calls.map((c) => ({name: c.name, arg: c.arg, t: c.t}))') if name is None or c['name'] == name]  # noqa: E731
        mp = MapTf(ed)
        inp.el_click('tgEdit')
        st = js_json(ed, "{shp: document.getElementById('edShp').hidden, drop: document.getElementById('edDrop').hidden}")
        report('host SHP 단추 보임 · [고친 것 버리기] 숨김', not st['shp'] and st['drop'])
        inp.click(*mp.en_scr(-10.0, 5.0))
        h = mp.en_scr(15.0, 5.0)
        a = (round(h[0]), round(h[1]))
        inp.drag(a, (a[0] + 10, a[1] + 8))
        t_up = time.time()
        while time.time() - t_up < 0.4:
            ed.pump(0.05)
        n04 = len(calls('save_pipes'))
        while time.time() - t_up < 1.4:
            ed.pump(0.05)
        sv = calls('save_pipes')
        text = ed.js("'window.RV_PIPES = ' + JSON.stringify(window.RV_PIPES) + ';\\n'")
        report('host 0.4 초엔 아직 · 1.4 초에 save_pipes 한 번', n04 == 0 and len(sv) == 1, '%d → %d' % (n04, len(sv)))
        report('host 저장 글 = 파일 꼴 전체 (ctrl · edited)', bool(sv) and sv[0]['arg'] == text and '"ctrl":' in text and '"edited":true' in text
               and text.startswith('window.RV_PIPES = {') and text.endswith('};\n'), '%d 글자' % len(text))
        report('host 이 브라우저에는 저장 안 함', not ed.js("!!localStorage.getItem('rv_pipes:' + window.RV_PROJECT.title)"))
        a2 = (a[0] + 10, a[1] + 8)
        inp.drag(a2, (a2[0] + 5, a2[1]))
        inp.drag((a2[0] + 5, a2[1]), (a2[0] + 9, a2[1] + 3))
        ed.pump(1.4)
        report('host 빨리 두 번 고침 → 한 번 저장', len(calls('save_pipes')) == 2, '%d' % len(calls('save_pipes')))
        inp.key('z', 'KeyZ', 90, CTRL)
        ed.pump(1.4)
        sv = calls('save_pipes')
        report('host Ctrl+Z → 저장', len(sv) == 3 and sv[-1]['arg'] == ed.js("'window.RV_PIPES = ' + JSON.stringify(window.RV_PIPES) + ';\\n'"), '%d' % len(sv))
        a3 = (a2[0] + 5, a2[1])
        inp.drag(a3, (a3[0] - 6, a3[1] - 6))
        inp.el_click('edShp')
        ok = ed.wait_js("document.getElementById('toast').textContent.indexOf('SHP 저장') === 0", 5)
        names = [c['name'] for c in calls()][-2:]
        sv = calls('save_pipes')
        report('host [SHP로 내보내기] → 저장 먼저 → export_pipes_shp · 알림', ok and names == ['save_pipes', 'export_pipes_shp'] and len(sv) == 4
               and sv[-1]['arg'] == ed.js("'window.RV_PIPES = ' + JSON.stringify(window.RV_PIPES) + ';\\n'")
               and ed.js("document.getElementById('toast').textContent") == 'SHP 저장 · C:\\시험\\관로.shp', '%s · %s' % (names, ed.js("document.getElementById('toast').textContent")))
        ed.pump(1.2)
        report('host SHP 뒤 저장 또 안 함', len(calls('save_pipes')) == 4)
        ed.screenshot(os.path.join(shots_dir, 'host_shp.png'))
        ed.js("window.__host.shps.push(null); window.__host.shps.push({ok: false, msg: '관로 없음'})")
        ed.js("document.getElementById('toast').textContent = ''")
        inp.el_click('edShp')
        ed.pump(0.4)
        t1 = ed.js("document.getElementById('toast').textContent")
        inp.el_click('edShp')
        ed.wait_js("document.getElementById('toast').textContent === '관로 없음'", 5)
        t2 = ed.js("document.getElementById('toast').textContent")
        report('host SHP 취소 → 알림 없음 · 못 씀 → 까닭', t1 == '' and t2 == '관로 없음' and len(calls('export_pipes_shp')) == 3, '%r · %r' % (t1, t2))
        inp.key('Escape', 'Escape', 27)
        inp.el_click('hPipes')
        report('host «관로 넣기» 창에 [SHP로 내보내기]', ed.js("getComputedStyle(document.getElementById('pmShpOut')).display !== 'none'"))
        ed.screenshot(os.path.join(shots_dir, 'host_pipe_dlg.png'))
        inp.el_click('pmShpOut')
        ok = ed.wait_js("window.__host.calls.filter((c) => c.name === 'export_pipes_shp').length === 4", 5)
        report('host 창 단추 → export_pipes_shp (안 쓴 고침 없어 저장 안 함)', ok and len(calls('save_pipes')) == 4)
        inp.key('Escape', 'Escape', 27)
        inp.el_click('tgEdit')
        inp.click(*mp.en_scr(-10.0, 5.0))
        h = mp.en_scr(*ctrl_pts(pipes(ed)['lines'][0])[1][:2])
        a = (round(h[0]), round(h[1]))
        inp.drag(a, (a[0] + 6, a[1] + 6))
        inp.el_click('hHome')
        ed.wait_js("window.__host.calls.some((c) => c.name === 'go_home')", 5)
        names = [c['name'] for c in calls()][-2:]
        report('host [처음] → 안 쓴 고침 먼저 저장 → go_home', names == ['save_pipes', 'go_home'] and len(calls('save_pipes')) == 5, json.dumps(names))
        errs(ed, 'host')
    finally:
        ci.cleanup(out_dir)


def run_legacy(ed, leg_dir, shots_dir):
    inp = Inp(ed)
    print('== legacy  ctrl 없는 옛 pipes.js')
    url = pathlib.Path(leg_dir, '로드뷰.html').resolve().as_uri() + '?' + q_of(('l', '0002', 60, -50, 70))
    load(ed, url)
    ed.js('localStorage.clear()')
    load(ed, url)
    ground = Ground(ed, None)
    P0 = pipes(ed)
    mp = MapTf(ed)
    inp.el_click('tgEdit')
    inp.click(*mp.en_scr(-10.0, 5.0))
    hs = [mp.en_scr(x, y) for x, y in ms.STRIPES[0][0]]
    moves = []
    for h in hs:
        inp.move(round(h[0]), round(h[1]))
        moves.append('move' in js_json(ed, "[...document.getElementById('map').classList]"))
    report('legacy 모든 점이 손잡이', all(moves) and 'ctrl' not in P0['lines'][0], json.dumps(moves))
    a = (round(hs[1][0]), round(hs[1][1]))
    b = (a[0] + 12, a[1] - 9)
    inp.drag(a, b)
    P1 = pipes(ed)
    d_en = np.array(mp.scr_en(*b)) - np.array(mp.scr_en(*a))
    want = [ms.STRIPES[0][0][0], (15.0 + d_en[0], 5.0 + d_en[1]), ms.STRIPES[0][0][2]]
    check_line('legacy 고친 줄', P1['lines'][0], ground, want)
    report('legacy z = null (hasZ 없음) · 다른 관 그대로', all(p[2] is None for p in P1['lines'][0]['pts']) and lines_json(P1)[1:] == lines_json(P0)[1:])
    ed.screenshot(os.path.join(shots_dir, 'legacy_edit.png'))
    errs(ed, 'legacy')


class _Srv(http.server.ThreadingHTTPServer):
    def handle_error(self, request, client_address):  # 브라우저가 끊은 연결은 조용히
        if not isinstance(sys.exc_info()[1], (ConnectionError, OSError)):
            super().handle_error(request, client_address)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True, help='make_synth --edit 폴더')
    ap.add_argument('--legacy', default=None, help='make_synth --legacy-pipes 폴더')
    ap.add_argument('--shots', default=None)
    args = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    out_dir = os.path.abspath(args.out)
    shots_dir = os.path.abspath(args.shots or os.path.join(out_dir, '_shots_edit'))
    os.makedirs(shots_dir, exist_ok=True)
    srv = _Srv(('127.0.0.1', 0), functools.partial(ci._Quiet, directory=out_dir))
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    ed = Edge(os.path.join(shots_dir, '_prof_e'), cv.WIN).__enter__()
    try:
        ed.net(block=ci.BLOCK)
        load(ed, pathlib.Path(out_dir, '로드뷰.html').resolve().as_uri() + '?' + q_of(VIEW))
        ground = Ground(ed, (ms.SLOPE_E, ms.SLOPE_N))
        P0, final = run_file(ed, out_dir, shots_dir, ground)
        run_restore(ed, out_dir, shots_dir, P0, final)
        run_pick(ed, out_dir, shots_dir, ground)
        print('== http')
        ed.js('localStorage.clear()')
        base = 'http://127.0.0.1:%d/%s' % (srv.server_address[1], urllib.parse.quote('로드뷰.html'))
        inp = Inp(ed)
        load(ed, base + '?' + q_of(VIEW))
        ed.js('localStorage.clear()')
        load(ed, base + '?' + q_of(VIEW))
        P0h = pipes(ed)
        mp = MapTf(ed)
        inp.el_click('tgEdit')
        inp.click(*mp.en_scr(-10.0, 5.0))
        h = mp.en_scr(15.0, 5.0)
        a = (round(h[0]), round(h[1]))
        inp.drag(a, (a[0] + 14, a[1] + 9))
        P1h = pipes(ed)
        d_en = np.array(mp.scr_en(a[0] + 14, a[1] + 9)) - np.array(mp.scr_en(*a))
        check_line('http E1', P1h['lines'][0], ground, [(-30.0, 5.0), (15.0 + d_en[0], 5.0 + d_en[1]), (45.0, 20.0)])
        errs(ed, 'http')
        run_restore(ed, out_dir, shots_dir, P0h, P1h, tag='http', base=base)
        run_host(ed, out_dir, shots_dir)
        if args.legacy:
            run_legacy(ed, os.path.abspath(args.legacy), shots_dir)
    finally:
        ed.__exit__(None, None, None)
        srv.shutdown()
    for where, e in R['errs']:
        print('   오류 [%s]: %s' % (where, e))
    print('사진:', shots_dir)
    print('전체:', '통과' if R['ok'] and not R['errs'] else '실패')
    return 0 if R['ok'] and not R['errs'] else 1


if __name__ == '__main__':
    sys.exit(main())
