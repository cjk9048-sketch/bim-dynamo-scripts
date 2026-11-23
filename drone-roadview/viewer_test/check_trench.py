"""터파기 (깊이 방식 구덩이) 시험: make_synth --trench 폴더를 화면 없는 Edge (cdp.py, 실제 시간) 로 찍고,
보기 화면 수식과 따로 짠 투영 · 단면 계산으로 잰다.

    python -I check_trench.py --out <make_synth --trench 폴더> [--shots <폴더>]

  (a) 구멍 가장자리 = 겹쳐 그린 밝은 자른 선 ↔ 지표 ±wv 선 투영 (중앙 1 px 안)
  (b) 스텐실: 깊이 방식 WebGL 만 (겹쳐 그림 숨김) − 관로 끔 = 구덩이 + 관 → 구멍 다각형 (2 px 넓힘) 밖 0 화소 ·
      안 (2 px 좁힘) 은 90 % 이상 바뀜
  (c) 보이는 바닥 (모래 윗면) 가장자리 ±ws · 벽의 포장/흙 경계 (±wv, g − tp) ↔ 투영 (중앙 1.5 px 안).
      보이는 점만 잰다: 카메라로 가는 광선이 그 구간 단면 안 (벽 · 관에 안 가림) 으로 지표 구멍에 닿는 점.
  (d) 단면 창에서 기울기 · 여유폭 · 포장 · 기초를 바꿈 → 구멍 폭이 식대로 (a 를 다시) · 단면 그림 '폭' 글 ·
      포장이 바닥보다 깊으면 벽 전체 직각 (wv = wb) · 다시 열어도 (이 브라우저) 그 값
  (e) calib.js: trench 없음 → 기본값으로 그림 · 내려받는 글에 기본 trench · 바꾼 뒤 바뀐 trench ·
      파일 trench 를 읽어 그대로 그림 (단면 칸도) · 파일 on false → 구덩이 없음 (관만) ·
      프로그램 (hostmock): selftest 'trench_host' (save_calib 에 trench · 0.8 초 모아 한 번 · 평균매설깊이 칸 같이)
  (f) 노면 방식은 구덩이 켬 / 끔이 화소까지 같음 · http 로 열어도 file:// 과 같은 그림
  모든 쪽 JS 오류 0
"""
import argparse
import functools
import http.server
import json
import math
import os
import pathlib
import sys
import threading
import urllib.parse

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import check_interact as ci  # noqa: E402
import check_view as cv  # noqa: E402
import make_synth as ms  # noqa: E402
from cdp import Edge  # noqa: E402

TR_DEF = dict(tp=0.2, n=0.3, margin=0.3, bed=0.1)
CFG_B = dict(tp=0.3, n=0.5, margin=0.4, bed=0.2)          # 단면 창에서 바꿈
CFG_C = dict(tp=2.0, n=0.5, margin=0.4, bed=0.2)          # 포장이 바닥보다 깊음 → 벽 전체 직각
CFG_FILE = dict(tp=0.25, n=0.4, margin=0.35, bed=0.15)    # calib.js 에 적은 값
VIEWS = [  # 이름, 사진, yaw, pitch, fov
    ('v1_down_n', '0002', 0, -60, 70),
    ('v2_oblique', '0001', 60, -30, 90),
    ('v3_chain', '0004', 250, -40, 80),
    ('v4_end', '0003', 120, -45, 80),
    ('v5_top', '0002', 0, -89, 100),
    ('v6_deep_zoom', '0003', 130, -47, 40),   # 깊은 D600 (hasZ) 구덩이 · 빈 땅 쪽 끝벽
]
NEAR = 0.05


# ---------------------------------------------------------------- 단면 (보기 화면과 따로 짠 계산)
def chains():
    """TR_LINES 를 끝끼리 (5 cm) 같은 관경이면 잇는다 (시험 자료는 셋 이상 만나는 곳 없음)."""
    raw = [dict(v=[np.array(p, float) for p in v], D=d, zc=zc) for v, d, _, _, zc in ms.TR_LINES]
    used = [False] * len(raw)
    out = []
    for i, l in enumerate(raw):
        if used[i]:
            continue
        used[i] = True
        v = list(l['v'])
        grow = True
        while grow:
            grow = False
            for j, m in enumerate(raw):
                if used[j] or abs(m['D'] - l['D']) > 1e-3 or (m['zc'] is None) != (l['zc'] is None):
                    continue
                a, b = m['v'], m['v'][::-1]
                for seq, at_end in ((a, True), (b, True), (b, False), (a, False)):
                    if at_end and np.hypot(*(v[-1] - seq[0])) <= 0.05:
                        v = v + seq[1:]
                    elif not at_end and np.hypot(*(v[0] - seq[-1])) <= 0.05:
                        v = seq[:-1] + v
                    else:
                        continue
                    used[j] = grow = True
                    break
        out.append(dict(v=v, D=l['D'], zc=l['zc']))
    return out


def sections(ch, tr, cover):
    """꼭짓점 자리 (끝은 여유폭만큼 더) · 가로 (이등분 법선 × 1/cos) · 단면 값 (시험 자료는 줄마다 g · zc 가 같음)."""
    v, D = ch['v'], ch['D']
    m = len(v)
    t = [(v[j + 1] - v[j]) / np.linalg.norm(v[j + 1] - v[j]) for j in range(m - 1)]
    nl = [np.array([-q[1], q[0]]) for q in t]
    lat = []
    for j in range(m):
        a, b = max(0, j - 1), min(m - 2, j)
        q = nl[a] + nl[b]
        q = q / np.linalg.norm(q)
        lat.append(q / max(q @ nl[b], 0.3))
    ext = max(tr['margin'], 0.05)
    pos = [p.copy() for p in v]
    pos[0] = pos[0] - t[0] * ext
    pos[-1] = pos[-1] + t[-1] * ext
    g = ms.GZ
    zc = ch['zc'] if ch['zc'] is not None else g - cover - D / 2
    zs = min(zc - D / 2, g - 0.01)
    zb = zs - tr['bed']
    wb = D / 2 + tr['margin']
    zt = g - tr['tp']
    wv = wb + tr['n'] * max(0.0, zt - zb)
    zm, ws = (zt, wb + tr['n'] * (zs - zb)) if zt > zs else (zs, wv)
    return dict(pos=pos, lat=lat, t=t, nl=nl, m=m, D=D, g=g, zc=zc, zs=zs, zb=zb, wb=wb, zt=zt, wv=wv, zm=zm, ws=ws, n=tr['n'])


def w_at(S, z):
    """높이 z 의 벽 반폭."""
    return S['wv'] if z >= S['zt'] else S['wb'] + S['n'] * max(0.0, z - S['zb'])


def all_sections(tr, cover=ms.DEPTH):
    return [sections(c, tr, cover) for c in chains()]


# ---------------------------------------------------------------- 카메라 (보기 화면 약속: x/z·kx, y/z·ky)
class Cam:
    def __init__(self, view, w, h):
        _, shot, yaw, pitch, fov = view
        self.c = cv.cam_of(shot, {})[0]
        self.f, self.r, self.u = cv.basis(yaw, pitch)
        t = math.tan(math.radians(fov) / 2)
        self.tanh, self.tanv = (t, t * h / w) if w >= h else (t * w / h, t)
        self.kx, self.ky = (w / 2) / self.tanh, (h / 2) / self.tanv
        self.w, self.h = w, h

    def cam(self, P):
        d = np.asarray(P, float) - self.c
        return np.array([d @ self.r, d @ self.u, d @ self.f])

    def scr(self, q):
        return np.array([self.w / 2 + q[0] / q[2] * self.kx, self.h / 2 - q[1] / q[2] * self.ky])

    def proj(self, P):
        q = self.cam(P)
        return self.scr(q) if q[2] > NEAR else None

    def poly(self, Ps):
        """가까운 면으로 자른 화면 다각형 + 화면 둘레 넓은 네모로 자름."""
        Q = [self.cam(P) for P in Ps]
        o = []
        for i in range(len(Q)):
            a, b = Q[i - 1], Q[i]
            if (a[2] >= NEAR) != (b[2] >= NEAR):
                o.append(a + (b - a) * (NEAR - a[2]) / (b[2] - a[2]))
            if b[2] >= NEAR:
                o.append(b)
        if len(o) < 3:
            return None
        s = [self.scr(q) for q in o]
        W, H = self.w, self.h
        for ax, sgn, lim in ((0, 1, -W), (0, -1, 2 * W), (1, 1, -H), (1, -1, 2 * H)):
            q = []
            for i in range(len(s)):
                a, b = s[i - 1], s[i]
                da, db = sgn * (a[ax] - lim), sgn * (b[ax] - lim)
                if (da >= 0) != (db >= 0):
                    q.append(a + (b - a) * da / (da - db))
                if db >= 0:
                    q.append(b)
            s = q
            if len(s) < 3:
                return None
        return s


def opening_mask(cam, secs):
    im = Image.new('L', (cam.w, cam.h), 0)
    dr = ImageDraw.Draw(im)
    for S in secs:
        P = lambda j, sg: np.array([*(S['pos'][j] + sg * S['lat'][j] * S['wv']), S['g']])  # noqa: E731
        for j in range(S['m'] - 1):
            pg = cam.poly([P(j, 1), P(j + 1, 1), P(j + 1, -1), P(j, -1)])
            if pg:
                dr.polygon([(x - 0.5, y - 0.5) for x, y in pg], fill=255)
    return np.asarray(im) > 0


def shift(m, dy, dx):
    o = np.zeros_like(m)
    H, W = m.shape
    o[max(0, dy):H + min(0, dy), max(0, dx):W + min(0, dx)] = m[max(0, -dy):H + min(0, -dy), max(0, -dx):W + min(0, -dx)]
    return o


def dilate(m, r):
    o = m.copy()
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dx * dx + dy * dy <= r * r:
                o |= shift(m, dy, dx)
    return o


def visible(cam, S, j, X, eps=0.004):
    """X (구덩이 면 위 점) 에서 카메라로 가는 광선이 구간 j 의 단면 안을 지나 지표 구멍에 닿는가.
    벽 · 관에 가리면 False · 구간 끝 (꺾임 · 끝벽) 1 m 안을 지나면 None (재지 않음)."""
    A, t, nl = S['pos'][j], S['t'][j], S['nl'][j]
    L = np.linalg.norm(S['pos'][j + 1] - A)
    X = np.asarray(X, float)
    dz = cam.c[2] - X[2]
    if dz <= 0:
        return False
    u = np.linspace(0.012 / max(1e-9, np.linalg.norm(cam.c - X)), (S['g'] - X[2]) / dz, 500)
    P = X[None, :] + (cam.c - X)[None, :] * u[:, None]
    s, al, z = (P[:, :2] - A) @ nl, (P[:, :2] - A) @ t, P[:, 2]
    w = np.where(z >= S['zt'], S['wv'], S['wb'] + S['n'] * np.maximum(0.0, z - S['zb']))
    bad = (np.abs(s) > w - eps) | (np.hypot(s, z - S['zc']) < S['D'] / 2 + eps)
    out = (al < 1.0) | (al > L - 1.0)
    k = int(np.argmax(bad | out)) if (bad | out).any() else -1
    if k < 0:
        return True
    return None if out[k] else False


# ---------------------------------------------------------------- 재기
def lum(rgb):
    return rgb @ np.array([0.299, 0.587, 0.114])


def screen_dir(cam, P, Q):
    p, q = cam.proj(P), cam.proj(Q)
    if p is None or q is None:
        return None, None
    d = q - p
    n = np.hypot(*d)
    return (p, d / n) if n > 1e-9 else (None, None)


def measure_cut(img, cam, secs, skip=1.0, step=0.2):
    """밝은 자른 선의 가운데 (밝은 화소 무게중심) − 투영한 ±wv 지표 선. 표본마다 부호 있는 px."""
    out = []
    for S in secs:
        for j in range(S['m'] - 1):
            for sg in (1, -1):
                EA = S['pos'][j] + sg * S['lat'][j] * S['wv']
                EB = S['pos'][j + 1] + sg * S['lat'][j + 1] * S['wv']
                L = np.linalg.norm(EB - EA)
                d = (EB - EA) / L
                for a in np.arange(skip, L - skip, step):
                    P = np.array([*(EA + d * a), S['g']])
                    p0, sd = screen_dir(cam, P, P + np.array([*(d * 0.3), 0.0]))
                    if p0 is None or not (12 < p0[0] < cam.w - 12 and 12 < p0[1] < cam.h - 12):
                        continue
                    nrm = np.array([-sd[1], sd[0]])
                    s = np.arange(-8, 8.001, 0.25)
                    rgb = cv.bilinear(img, p0[None, :] + s[:, None] * nrm[None, :])
                    if not np.isfinite(rgb).all() or (rgb.max(axis=1) < 30).any():
                        continue  # 이름표 바탕이 걸침
                    lv = lum(rgb)
                    k = int(np.argmax(lv))
                    if lv[k] < 200:
                        continue
                    base = lv[k] - 60
                    i0 = k
                    while i0 > 0 and lv[i0 - 1] >= base:
                        i0 -= 1
                    i1 = k
                    while i1 + 1 < s.size and lv[i1 + 1] >= base:
                        i1 += 1
                    if (i1 - i0 + 1) * 0.25 > 3.5:
                        continue  # 선이 아님 (원판 · 글자)
                    wgt = lv[i0:i1 + 1] - base
                    out.append(float((s[i0:i1 + 1] * wgt).sum() / wgt.sum()))
    return np.array(out)


def measure_edges(img, cam, secs, kind, step=0.25):
    """보이는 면 경계: kind 'bottom' = 모래 윗면 가장자리 (±ws, zs) · 밝기 / 'pav' = 벽의 포장 → 흙 (±wv, g − tp) · R − B.
    [(부호 있는 px, 단면 번호, 쪽)]"""
    out = []
    for si, S in enumerate(secs):
        for j in range(S['m'] - 1):
            A, B = S['pos'][j], S['pos'][j + 1]
            L = np.linalg.norm(B - A)
            for sg in (1, -1):
                for a in np.arange(2.5, L - 2.5, step):
                    c2 = A + (B - A) * (a / L)
                    lat = S['nl'][j] * sg  # 곧은 구간: 이등분 가로의 수직 성분 = 같은 반폭
                    wall = lambda z: np.array([*(c2 + lat * w_at(S, z)), z])  # noqa: E731  벽 위 높이 z 점
                    if kind == 'bottom':  # A 쪽 = 모래 (안쪽으로) · B 쪽 = 벽 (위로)
                        z0 = S['zs']
                        X = wall(z0)
                        pathA = [np.array([*(c2 + lat * (S['ws'] - k * 0.01)), z0]) for k in range(1, int(S['ws'] * 100))]
                        pathB = [wall(z0 + k * 0.01) for k in range(1, int((S['g'] - z0) * 100))]
                    else:                 # A 쪽 = 흙 (아래로) · B 쪽 = 포장 (위로)
                        if S['zt'] <= S['zs'] + 0.2:
                            continue
                        z0 = S['zt']
                        X = wall(z0)
                        pathA = [wall(z0 - k * 0.01) for k in range(1, int((z0 - S['zs']) * 100))]
                        pathB = [wall(z0 + k * 0.01) for k in range(1, int((S['g'] - z0) * 100) + 1)]
                    if visible(cam, S, j, X) is not True or not pathA or not pathB:
                        continue
                    p0, sd = screen_dir(cam, X, pathA[0])
                    if p0 is None or not (12 < p0[0] < cam.w - 12 and 12 < p0[1] < cam.h - 12):
                        continue

                    def reach(path):  # 끊기지 않고 보이는 띠가 화면에서 얼마나 (px)
                        d = 0.0
                        for q in path:
                            if visible(cam, S, j, q) is not True:
                                break
                            pq = cam.proj(q)
                            d = max(d, np.hypot(*(pq - p0)))
                            if d >= 4.5:
                                break
                        return d
                    if reach(pathA[::2]) < 4.5 or reach(pathB[::2]) < 4.5:
                        continue  # 경계 두 쪽 띠가 화면에서 너무 좁거나 가림
                    s = np.arange(-6, 6.001, 0.25)
                    rgb = cv.bilinear(img, p0[None, :] + s[:, None] * sd[None, :])
                    if not np.isfinite(rgb).all():
                        continue
                    pr = lum(rgb) if kind == 'bottom' else rgb[:, 0] - rgb[:, 2]
                    lo = np.median(pr[(s >= -4) & (s <= -1.5)])
                    hi = np.median(pr[(s >= 1.5) & (s <= 4)])
                    if hi - lo < (30 if kind == 'bottom' else 15):
                        continue
                    mid = (lo + hi) / 2
                    best = None
                    for i in range(s.size - 1):
                        if (pr[i] - mid) * (pr[i + 1] - mid) <= 0 and pr[i] != pr[i + 1]:
                            x = s[i] + (s[i + 1] - s[i]) * (mid - pr[i]) / (pr[i + 1] - pr[i])
                            if best is None or abs(x) < abs(best):
                                best = x
                    if best is not None:
                        out.append((best, si, sg))
    return out


def stat(v):
    v = np.abs(np.asarray(v, float))
    return (np.median(v), np.percentile(v, 90)) if v.size else (np.nan, np.nan)


# ---------------------------------------------------------------- 찍기
def url_of(out_dir, view, mode='depth', extra='', base=None):
    _, shot, yaw, pitch, fov = view
    q = 'shot=%s&yaw=%s&pitch=%s&fov=%s' % (shot, yaw, pitch, fov) + ('&mode=' + mode if mode else '') + extra
    if base:
        return base + '?' + q
    return pathlib.Path(out_dir, '로드뷰.html').resolve().as_uri() + '?' + q


def load(ed, url):
    ed.goto(url)
    ok = ed.wait_js(cv.READY, timeout=60)
    ed.frames(3)
    return ok


def grab(ed, png):
    ed.frames(3)
    ed.screenshot(png)
    a = np.asarray(Image.open(png).convert('RGB'))
    x0, y0, x1, y1 = cv.view_rect(a)
    return a[y0:y1 + 1, x0:x1 + 1].astype(float), (x0, y0, x1, y1)


def set_ov(ed, on):
    ed.js("document.getElementById('ov').style.visibility = '%s'" % ('visible' if on else 'hidden'))


def three(ed, shots_dir, tag):
    """겹쳐 그림 포함 · WebGL 만 · WebGL 만 관로 끔."""
    full, rect = grab(ed, os.path.join(shots_dir, tag + '_full.png'))
    set_ov(ed, False)
    gl, _ = grab(ed, os.path.join(shots_dir, tag + '_gl.png'))
    ed.js("document.getElementById('tgPipes').click()")
    off, _ = grab(ed, os.path.join(shots_dir, tag + '_off.png'))
    ed.js("document.getElementById('tgPipes').click()")
    set_ov(ed, True)
    ed.frames(2)
    return full, gl, off, rect


def set_cfg(ed, cfg):
    """단면 창 칸에 값을 친다 (사용자처럼 input 소식)."""
    ed.js("(function(){ if (document.getElementById('secDlg').hidden) document.getElementById('tgSec').click(); })()")
    for k, v in cfg.items():
        ed.js("(function(){ const i = document.getElementById('sec_%s'); i.focus(); i.value = '%s';"
              " i.dispatchEvent(new Event('input', {bubbles: true})); i.dispatchEvent(new Event('change', {bubbles: true})); i.blur(); })()" % (k, v))
    ed.frames(3)


EXPORT_JS = ("new Promise((res) => { let b = null; const oc = URL.createObjectURL;"
             " URL.createObjectURL = function (x) { b = x; return oc.call(URL, x); };"
             " try { document.getElementById('calSave').click(); } finally { URL.createObjectURL = oc; }"
             " (b ? b.text() : Promise.resolve('')).then(res); })")


def exported_trench(ed):
    t = ed.js(EXPORT_JS, await_promise=True) or ''
    try:
        return json.loads(t[t.index('{'):t.rindex('}') + 1]).get('trench'), t
    except ValueError:
        return None, t


def report(name, ok, text):
    print('   %-34s %s  %s' % (name, text, '통과' if ok else '실패'))
    return bool(ok)


def check_cut(full, cam, tr, label, need=40):
    e = measure_cut(full, cam, all_sections(tr))
    m, p = stat(e)
    return report(label, e.size >= need and m <= 1.0, '표본 %4d · 중앙 %.2f px · 90%% %.2f px · 평균 %+.2f' % (e.size, m, p, np.mean(e) if e.size else 0))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--shots', default=None)
    args = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    out_dir = os.path.abspath(args.out)
    shots_dir = os.path.abspath(args.shots or os.path.join(out_dir, '_shots_trench'))
    os.makedirs(shots_dir, exist_ok=True)
    calib_js = os.path.join(out_dir, 'data', 'calib.js')
    if os.path.exists(calib_js):
        os.remove(calib_js)
    all_ok = True
    errs_all = []

    def errs(ed, where):
        e = cv.errors(ed)
        for x in e:
            errs_all.append((where, str(x)[:200]))
        return e

    srv = http.server.ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(ci._Quiet, directory=out_dir))
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    ed = Edge(os.path.join(shots_dir, '_prof_t'), cv.WIN).__enter__()
    try:
        ed.net(block=ci.BLOCK)
        defsec = all_sections(TR_DEF)
        print('기본 단면 (D300 T1): wv %.3f · ws %.3f · zs %.3f · zb %.3f  |  D600 hasZ: wv %.3f  |  D200 이음: wv %.3f'
              % (defsec[0]['wv'], defsec[0]['ws'], defsec[0]['zs'] - ms.GZ, defsec[0]['zb'] - ms.GZ, defsec[1]['wv'], defsec[2]['wv']))
        # ---- (a) (b) (c) 기본값 (calib.js 없음)
        gl_v1 = None
        c_all = {'bottom': [], 'pav': []}
        for view in VIEWS:
            name = view[0]
            ok = load(ed, url_of(out_dir, view))
            full, gl, off, rect = three(ed, shots_dir, name)
            h, w = gl.shape[:2]
            cam = Cam(view, w, h)
            print('== %s  shot=%s yaw=%s pitch=%s fov=%s  view=%dx%d' % (*view, w, h))
            all_ok &= report('불러옴', ok, '')
            if name == 'v1_down_n':
                gl_v1 = gl
            e = measure_cut(full, cam, defsec)
            m, p = stat(e)
            all_ok &= report('(a) 자른 선 ↔ ±wv 투영', e.size >= 30 and m <= 1.0, '표본 %4d · 중앙 %.2f px · 90%% %.2f px' % (e.size, m, p))
            mask = opening_mask(cam, defsec)
            diff = np.abs(gl - off).max(axis=2) > 20
            outside = diff & ~dilate(mask, 2)
            inner = ~dilate(~mask, 2)
            cover = diff[inner].mean() if inner.any() else 0.0
            all_ok &= report('(b) 구멍 밖 구덩이 화소', outside.sum() == 0 and diff.sum() > 500,
                             '밖 %d px (바뀐 화소 %d · 구멍 %d px)' % (outside.sum(), diff.sum(), mask.sum()))
            all_ok &= report('(b) 구멍 안 덮임', cover >= 0.9, '%.1f %%' % (cover * 100))
            if outside.any():
                dbg = (np.stack([outside, diff & ~outside, mask], axis=2) * 255).astype(np.uint8)
                Image.fromarray(dbg).save(os.path.join(shots_dir, name + '_b_dbg.png'))
            for kind, lab in (('bottom', '(c) 바닥 가장자리 ±ws'), ('pav', '(c) 벽 포장/흙 경계')):
                r = measure_edges(gl, cam, defsec, kind)
                v = [x[0] for x in r]
                c_all[kind] += v
                m, p = stat(v)
                sides = sorted({(si, sg) for _, si, sg in r})
                okc = len(v) < 5 or m <= 1.5  # 보이는 곳이 거의 없으면 이 자리는 재지 않음 (모두 합쳐 아래에서 셈)
                all_ok &= report(lab, okc, '표본 %4d · 중앙 %.2f px · 90%% %.2f px · (단면, 쪽) %s' % (len(v), m, p, sides))
            er = errs(ed, name)
            all_ok &= report('JS 오류', not er, '%d 줄' % len(er))
        print('== (c) 모든 자리 합')
        for kind, lab, need in (('bottom', '바닥 가장자리 ±ws', 150), ('pav', '벽 포장/흙 경계', 20)):
            m, p = stat(c_all[kind])
            all_ok &= report(lab, len(c_all[kind]) >= need and m <= 1.5, '표본 %4d · 중앙 %.2f px · 90%% %.2f px' % (len(c_all[kind]), m, p))

        # ---- (f) 노면 방식: 구덩이 켬 / 끔 같은 그림
        v1 = VIEWS[0]
        load(ed, url_of(out_dir, v1, mode=''))
        s_on, _ = grab(ed, os.path.join(shots_dir, 'f_surface_on.png'))
        load(ed, url_of(out_dir, v1, mode='', extra='&trench=0'))
        s_off, _ = grab(ed, os.path.join(shots_dir, 'f_surface_trench0.png'))
        print('== (f) 노면 방식 · 깊이 방식 ?trench=0')
        all_ok &= report('노면: 구덩이 켬 = 끔 (화소)', s_on.shape == s_off.shape and np.abs(s_on - s_off).max() == 0,
                         '최대 차이 %d' % (np.abs(s_on - s_off).max() if s_on.shape == s_off.shape else -1))
        load(ed, url_of(out_dir, v1, extra='&trench=0'))
        _, gl0, off0, _ = three(ed, shots_dir, 'f_depth_trench0')
        cam = Cam(v1, gl0.shape[1], gl0.shape[0])
        d0 = np.abs(gl0 - off0).max(axis=2) > 20
        inner = ~dilate(~opening_mask(cam, defsec), 2)
        all_ok &= report('깊이 ?trench=0 → 관만 (구멍 안 덮임 작음)', d0[inner].mean() < 0.45 and d0.sum() > 200,
                         '구멍 안 %.1f %% · 바뀐 화소 %d' % (d0[inner].mean() * 100, d0.sum()))
        all_ok &= report('JS 오류', not errs(ed, 'f'), '')

        # ---- (e1) 내려받는 calib.js: trench 없음 → 기본값
        print('== (e) calib.js')
        load(ed, url_of(out_dir, v1))
        tr, txt = exported_trench(ed)
        all_ok &= report('trench 없음 → 내려받는 글에 기본값', tr == dict(TR_DEF, on=True), json.dumps(tr))

        # ---- (d) 단면 창에서 바꿈 → 구멍 폭
        print('== (d) 단면 창')
        for cfg, tag in ((CFG_B, 'd_cfgB'), (CFG_C, 'd_cfgC')):
            set_cfg(ed, cfg)
            full, rect = grab(ed, os.path.join(shots_dir, tag + '_full.png'))
            cam = Cam(v1, full.shape[1], full.shape[0])
            secs = all_sections(cfg)
            all_ok &= check_cut(full, cam, cfg, '%s 자른 선 (T1 wv %.3f = 기본 %.3f 의 %.2f 배)' % (tag, secs[0]['wv'], defsec[0]['wv'], secs[0]['wv'] / defsec[0]['wv']))
            st = json.loads(ed.js("JSON.stringify({dia: document.getElementById('secDia').value, svg: document.getElementById('secSvg').textContent,"
                                  " ls: localStorage.getItem('rv_trench:' + window.RV_PROJECT.title)})"))
            D = int(st['dia']) / 1000
            sd = sections(dict(v=[np.zeros(2), np.array([1.0, 0.0])], D=D, zc=None), cfg, ms.DEPTH)
            want = '폭 %.2f' % (2 * sd['wv'])
            all_ok &= report('%s 단면 그림 글 (D%d)' % (tag, int(st['dia'])), want in st['svg'], want + ' · ' + st['svg'][:60])
            ls = json.loads(st['ls'] or '{}')
            all_ok &= report('%s 이 브라우저 저장' % tag, all(abs(ls.get(k, -1) - v) < 1e-9 for k, v in cfg.items()), json.dumps(ls))
        if True:
            sC = all_sections(CFG_C)[0]
            all_ok &= report('포장 > 깊이 → 벽 전체 직각', abs(sC['wv'] - sC['wb']) < 1e-12, 'wv %.3f = wb %.3f' % (sC['wv'], sC['wb']))
        crop = Image.open(os.path.join(shots_dir, 'd_cfgB_full.png'))
        crop.save(os.path.join(shots_dir, 'd_cfgB_panel.png'))
        tr, _ = exported_trench(ed)
        all_ok &= report('바꾼 뒤 내려받는 글 = 마지막 값', tr == dict(CFG_C, on=True), json.dumps(tr))
        # 다시 열기 (같은 브라우저) → 저장된 값으로, 다른 자리
        v3 = VIEWS[2]
        load(ed, url_of(out_dir, v3))
        full, _ = grab(ed, os.path.join(shots_dir, 'd_reopen_v3_full.png'))
        all_ok &= check_cut(full, Cam(v3, full.shape[1], full.shape[0]), CFG_C, '다시 열기 v3 (저장 값 C)')
        all_ok &= report('JS 오류', not errs(ed, 'd'), '')

        # ---- (e2) 파일 trench → 그대로 그림 · 단면 칸
        with open(calib_js, 'w', encoding='utf-8', newline='\n') as f:
            f.write('window.RV_CALIB = ' + json.dumps({'videos': {}, 'trench': dict(CFG_FILE, on=True)}) + ';\n')
        ed.js('localStorage.clear()')
        load(ed, url_of(out_dir, v1))
        full, _ = grab(ed, os.path.join(shots_dir, 'e_file_full.png'))
        all_ok &= check_cut(full, Cam(v1, full.shape[1], full.shape[0]), CFG_FILE, '파일 trench 로 그림')
        ed.js("document.getElementById('tgSec').click()")
        vals = json.loads(ed.js("JSON.stringify(['tp','n','margin','bed'].map((k) => document.getElementById('sec_' + k).value).concat([document.getElementById('secOn').checked]))"))
        all_ok &= report('단면 칸 = 파일 값', vals == [str(CFG_FILE[k]) for k in ('tp', 'n', 'margin', 'bed')] + [True], json.dumps(vals))
        with open(calib_js, 'w', encoding='utf-8', newline='\n') as f:
            f.write('window.RV_CALIB = ' + json.dumps({'videos': {}, 'trench': dict(CFG_FILE, on=False)}) + ';\n')
        ed.js('localStorage.clear()')
        load(ed, url_of(out_dir, v1))
        _, gl1, off1, _ = three(ed, shots_dir, 'e_file_off')
        d1 = np.abs(gl1 - off1).max(axis=2) > 20
        cam = Cam(v1, gl1.shape[1], gl1.shape[0])
        inner = ~dilate(~opening_mask(cam, all_sections(CFG_FILE)), 2)
        chk = ed.js("document.getElementById('tgSec').click(); document.getElementById('secOn').checked")
        all_ok &= report('파일 on false → 구덩이 없음 · 칸 꺼짐', d1[inner].mean() < 0.45 and chk is False, '구멍 안 %.1f %% · 칸 %s' % (d1[inner].mean() * 100, chk))
        os.remove(calib_js)
        all_ok &= report('JS 오류', not errs(ed, 'e'), '')

        # ---- (f) http 로 열어도 같은 그림
        ed.js('localStorage.clear()')
        base = 'http://127.0.0.1:%d/%s' % (srv.server_address[1], urllib.parse.quote('로드뷰.html'))
        load(ed, url_of(out_dir, v1, base=base))
        set_ov(ed, False)
        glh, _ = grab(ed, os.path.join(shots_dir, 'f_http_gl.png'))
        same = glh.shape == gl_v1.shape
        dd = np.abs(glh - gl_v1).max(axis=2) if same else None
        all_ok &= report('http = file:// (WebGL 만)', same and (dd > 3).mean() < 0.001, '다른 화소 %.4f %%' % ((dd > 3).mean() * 100 if same else -1))
        all_ok &= report('JS 오류 (http)', not errs(ed, 'http'), '')

        # ---- (e3) 프로그램 (hostmock): save_calib 에 trench
        print('== (e) 프로그램 (hostmock) selftest trench_host')
        ed.goto(pathlib.Path(out_dir, '로드뷰.html').resolve().as_uri())
        ed.wait_js(cv.READY, timeout=60)
        ed.js('localStorage.clear()')
        page = ci.prepare(out_dir, host=True)
        try:
            url = pathlib.Path(out_dir, page).resolve().as_uri() + '?t=trench_host&shot=0002&yaw=0&pitch=-60&fov=70&mode=depth'
            st, other, _, _, _, pngs = ci.drive(ed, url, os.path.join(shots_dir, 'i_trench_host.png'), shots_dir, 'i_trench_host')
        finally:
            ci.cleanup(out_dir)
        for s in st:
            if not s.startswith(('SNAP ', 'MARK ')):
                print('    ', s)
        for o in other:
            print('     JS:', o)
            errs_all.append(('host', o))
        done = [s for s in st if s.startswith('DONE')]
        all_ok &= report('selftest trench_host', bool(done) and 'ALL-PASS' in done[0] and not other, done[0] if done else '끝 못 냄')
    finally:
        ed.__exit__(None, None, None)
        srv.shutdown()
        if os.path.exists(calib_js):
            os.remove(calib_js)
    for where, e in errs_all:
        print('   오류 [%s]: %s' % (where, e))
    all_ok &= not errs_all
    print('사진:', shots_dir)
    print('전체:', '통과' if all_ok else '실패')
    return 0 if all_ok else 1


if __name__ == '__main__':
    sys.exit(main())
