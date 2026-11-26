"""가시설 (흙막이) · 터파기 관 불투명 · 벽 / 바닥 결 · 관 두 번 누르기 시험: make_synth --trench 폴더를 화면 없는 Edge
(cdp.py, 실제 시간 · 진짜 마우스 · 키) 로 다루고, 보기 화면 수식과 따로 짠 계산으로 잰다.

    python -I check_shore.py --out <make_synth --trench 폴더> [--shots <폴더>]

  o  터파기: 관 = 불투명 (관 뒤가 구덩이 · 사진이어도 관 화소가 같음)
  p  관종 · 이음 · 곡관: T1 이음 = 측점 6 m 마다 (관 위 1 cm) · 꺾인 곳 (14, 3) 곡관 (꺾임 각) · 곡관 둘레 이음 없음 ·
     관 정보 칸 관종 (주철 · 강관 · 비금속) → RV_PIPES mat · 이 브라우저 저장 · 세 색 다름 ·
     관로 고치기: 꼭짓점 끌면 곡관이 따라감 · 점 넣고 끌면 새 곡관 · Ctrl+Z 셋 → 제자리
  f  터파기 면 두 결: 바닥 (보이는 점) 이 벽 (먼 벽 흙 · 보이는 점) 보다 밝음 (밝기 차 ≥ 40) · 표시 항목에 층 단추 없음 ·
     옛 이 브라우저 표시 (기초 · 보호 끔 · 되메우기 켬) 를 넣어도 화소 그대로
  c  [가시설] → 토류벽 → 360 에서 T1 위 두 점 → shore [{a, b, t}] (측점 = 따로 셈 · 5 cm) · 표현 그대로 (영역) ·
     목록 한 줄 (길이) · 이 브라우저 저장 (고친 줄 · shore) · 시작점 뒤 Esc → 취소 (shore 그대로)
  s  기울기 0: 영역 임시점용 점선 = 구간 안 ±wb · 밖 ±wv · 터파기 자른 선 = ±wb (구간 안) ·
     관 정보 [이 자리 단면] = 구간 안 폭 2·wb + 가시설 그림 · 밖 2·wv · 기울기 0.5 에서 구간 안 wb ~ wv 띠는 안 판 (화소 그대로) · 밖은 판 ·
     수량 터파기 = 줄어든 만큼 (따로 셈 · 1 L)
  r  세 종류 그림이 서로 다름 (구간 구멍 둘레 화소) · 가시설 없을 때와도 다름 · 단면 그림도 다름 (열린 창이 바로 바뀜) · 종류 바꾸기 → t
  m  지도에서 두 점 → 시트파일 구간 (측점 5 cm 대신 0.3 m · 지도 한 칸 0.24 m) · 관로 고치기로 끝 꼭짓점을 당겨 줄이면 구간 끝이 새 길이로
  x  × → 그 구간 지움 (저장)
  d  관로 고치기가 아닐 때 360 관 두 번 누르기 → 관로 고치기 · 그 관 고름 · 관 정보 칸 닫힘 · 원판 위는 안 켬
  h  프로그램 (hostmock): 두 점 → save_pipes 글에 shore (0.8 초 모아 한 번) · 이 브라우저 저장 안 함 · 관종 → save_pipes 글에 mat
  모든 쪽 JS 오류 0
"""
import argparse
import json
import math
import os
import pathlib
import sys
import time

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import check_edit as ce  # noqa: E402
import check_interact as ci  # noqa: E402
import check_trench as ct  # noqa: E402
import check_under as cu  # noqa: E402
import check_view as cv  # noqa: E402
import make_synth as ms  # noqa: E402
from cdp import Edge  # noqa: E402

GZ = ms.GZ
TR_DEF = dict(tp=0.2, n=0.3, margin=0.3, bed=0.1, prot=0.3)
T1 = [(-40.0, 3.0), (14.0, 3.0), (40.0, 16.0)]   # make_synth TR_LINES[0] (D300 · 평균매설깊이)
T1_D = 0.3
V_TOP = ('top', '0002', 0, -82, 60)               # T1 거의 바로 위 (관이 벽에 안 가림)
V_PIT = ('pit', '0002', 0, -60, 70)               # check_trench v1_down_n (먼 벽 · 바닥이 보임)
V_C = ('c', '0001', 285, -50, 40)                 # T1 서쪽 (e −17 ~ −8)
A_E, B_E = -16.0, -9.0                            # 360 에서 찍을 두 점 (T1 첫 마디 · 측점 24 · 31)
R = {'ok': True, 'errs': []}


def report(name, ok, text=''):
    print('   %-48s %s  %s' % (name, text, '통과' if ok else '실패'))
    R['ok'] &= bool(ok)
    return bool(ok)


def errs(ed, where):
    e = cv.errors(ed)
    for x in e:
        R['errs'].append((where, str(x)[:200]))
    ed.exceptions.clear()
    ed.console.clear()
    return report('JS 오류 (%s)' % where, not e, '%d 줄' % len(e))


def js_json(ed, expr):
    return json.loads(ed.js('JSON.stringify(%s)' % expr))


def url(out_dir, view, mode='', page='로드뷰.html'):
    return pathlib.Path(out_dir, page).resolve().as_uri() + '?' + ce.q_of(view, ('&mode=' + mode) if mode else '')


def load(ed, u):
    ed.goto(u)
    ok = ed.wait_js(cv.READY, timeout=60)
    ed.frames(4)
    return ok


def grab(ed, png, gl_only=False):
    """360 칸만 (CSS 자리로 자름) · gl_only = WebGL 만."""
    vis = lambda v: ed.js("document.querySelectorAll('#viewPane > :not(#gl)').forEach((e) => { e.style.visibility = '%s'; })" % v)  # noqa: E731
    if gl_only:
        vis('hidden')
    ed.frames(3)
    ed.screenshot(png)
    if gl_only:
        vis('')
    r = ce.rect(ed, 'ov')
    a = np.asarray(Image.open(png).convert('RGB')).astype(float)
    l, t = int(round(r['left'])), int(round(r['top']))
    return a[t:t + int(round(r['height'])), l:l + int(round(r['width']))]


def lum(a):
    return a @ np.array([0.299, 0.587, 0.114])


def poly_mask(cam, quads):
    im = Image.new('L', (cam.w, cam.h), 0)
    dr = ImageDraw.Draw(im)
    for q in quads:
        pg = cam.poly(q)
        if pg:
            dr.polygon([(x - 0.5, y - 0.5) for x, y in pg], fill=255)
    return np.asarray(im) > 0


def band_quads(e0, e1, w0, w1, z):
    """T1 첫 마디 (n = 3, 동쪽) 위 e0 ~ e1 · 가로 w0 ~ w1 (양쪽) 네모들."""
    return [[np.array([e0, 3 + sg * w0, z]), np.array([e1, 3 + sg * w0, z]), np.array([e1, 3 + sg * w1, z]), np.array([e0, 3 + sg * w1, z])] for sg in (1, -1)]


def sect_area(D, g, zc, tr, n):
    zs = min(zc - D / 2, g - 0.01)
    zb = zs - tr['bed']
    wb = D / 2 + tr['margin']
    zt = g - tr['tp']
    wv = wb + n * max(0.0, zt - zb)
    return 2 * wv * (g - max(zt, zb)) + (wb + wv) * max(0.0, zt - zb), wb, wv


def shore_of(ed, i=0):
    return js_json(ed, 'window.RV_PIPES.lines[%d].shore || []' % i)


def stored_shore(ed, i=0):
    """이 브라우저 저장 (rv_pipes:제목 → {sig, lines: {번호: 줄}}) 의 shore."""
    st = js_json(ed, "JSON.parse(localStorage.getItem('rv_pipes:' + window.RV_PROJECT.title) || 'null')")
    if not st:
        return None
    ln = (st.get('lines') or {}).get(str(i))
    return None if ln is None else ln.get('shore', [])


def chip(ed):
    return js_json(ed, "{show: document.getElementById('shChip').classList.contains('show'), tip: document.getElementById('shTip').textContent,"
                       " a: document.getElementById('shChip').dataset.a, mode: document.getElementById('app').dataset.mode,"
                       " on: [...document.querySelectorAll('#shTypes button.on')].map((b) => b.dataset.t)}")


def aria(ed, label):
    """aria-label 로 단추 자리 (가운데 client) · 오른쪽 칸 카테고리 (#opts) 안이면 먼저 보이게 굴림."""
    r = js_json(ed, "(function (e) { if (e.closest('#opts')) e.scrollIntoView({block: 'nearest'}); return e.getBoundingClientRect(); })(document.querySelector('[aria-label=\"%s\"]'))" % label)
    return r['left'] + r['width'] / 2, r['top'] + r['height'] / 2


def chain_s(e, n):
    """T1 위 (e, n) 의 측점 (첫 점부터 평면 길이)."""
    acc, best = 0.0, None
    for a, b in zip(T1[:-1], T1[1:]):
        a, b = np.array(a), np.array(b)
        d = b - a
        L = float(np.linalg.norm(d))
        t = float(np.clip(((np.array([e, n]) - a) @ d) / (L * L), 0, 1))
        dist = float(np.linalg.norm(a + d * t - np.array([e, n])))
        if best is None or dist < best[0]:
            best = (dist, acc + t * L)
        acc += L
    return best[1]


def t1_point(s):
    acc = 0.0
    for a, b in zip(T1[:-1], T1[1:]):
        a, b = np.array(a), np.array(b)
        L = float(np.linalg.norm(b - a))
        if s <= acc + L:
            return a + (b - a) * ((s - acc) / L)
        acc += L
    return np.array(T1[-1])


# ---------------------------------------------------------------- o  터파기 관 불투명
def test_opaque(ed, out_dir, shots_dir):
    print('== o  터파기: 관 = 불투명 (뒤가 구덩이든 사진이든 관 화소가 같음)')
    ed.js('localStorage.clear()')
    load(ed, url(out_dir, V_TOP, 'trench'))
    Tr = grab(ed, os.path.join(shots_dir, 'o_trench_gl.png'), True)      # 관 뒤 = 구덩이 바닥 · 벽
    load(ed, url(out_dir, V_TOP, 'trench') + '&trench=0')
    Ph = grab(ed, os.path.join(shots_dir, 'o_nopit_gl.png'), True)       # 관 뒤 = 사진 (구덩이 끔)
    ed.js("document.getElementById('tgPipes').click()")
    P = grab(ed, os.path.join(shots_dir, 'o_off_gl.png'), True)          # 관 없음
    h, w = Tr.shape[:2]
    cam = ct.Cam(V_TOP, w, h)
    zc = GZ - ms.DEPTH - T1_D / 2
    pipe = poly_mask(cam, [[np.array([-6.0, 3 + T1_D / 2, zc]), np.array([12.0, 3 + T1_D / 2, zc]), np.array([12.0, 3 - T1_D / 2, zc]), np.array([-6.0, 3 - T1_D / 2, zc])]])
    m = ~ct.dilate(~(pipe & (np.abs(Ph - P).max(axis=2) > 25)), 2)
    if m.sum() < 200:
        return report('관 화소', False, '%d px' % m.sum())
    d_same = np.median(np.abs(Tr[m] - Ph[m]).max(axis=1))
    d_bg = np.median(np.abs(Tr[m] - P[m]).max(axis=1))
    report('관 화소: 구덩이 위 = 사진 위 (불투명)', d_same <= 3 and d_bg >= 40, '표본 %d px · 둘 차이 %.1f · 사진과 %.1f' % (m.sum(), d_same, d_bg))
    errs(ed, 'o')


# ---------------------------------------------------------------- p  관종 · 이음 · 곡관 (터파기)
def fit_of(ed):
    return js_json(ed, "document.getElementById('gl').__fit()")


def on_t1(p, tol=0.01):
    """(e, n) 이 T1 위 (tol 안) 면 그 측점 · 아니면 None."""
    acc = 0.0
    for a, b in zip(T1[:-1], T1[1:]):
        a, b = np.array(a), np.array(b)
        L = float(np.linalg.norm(b - a))
        t = float(np.clip(((np.array(p[:2]) - a) @ (b - a)) / (L * L), 0, 1))
        if np.linalg.norm(a + (b - a) * t - np.array(p[:2])) <= tol:
            return acc + t * L
        acc += L
    return None


def pipe_rgb(img, cam, zc, e0=-4.0, e1=12.0):
    """T1 첫 마디 관 가운데 (±0.06 m) 화소 중앙값 (이음 자리 ±0.5 m 빼고)."""
    pts = []
    for e in np.arange(e0, e1, 0.1):
        if abs(((e - T1[0][0]) / 6.0) - round((e - T1[0][0]) / 6.0)) * 6.0 < 0.5:
            continue
        for dn in (-0.06, 0.0, 0.06):
            p = cam.proj(np.array([e, 3.0 + dn, zc + T1_D / 2]))
            if p is not None and 4 < p[0] < cam.w - 4 and 4 < p[1] < cam.h - 4:
                pts.append(p)
    return np.median(cv.bilinear(img, np.array(pts)), axis=0) if pts else np.array([np.nan] * 3)


def test_pipes(ed, out_dir, shots_dir):
    print('== p  관종 · 6 m 이음 · 곡관 · 관로 고치기에서 바로 바뀜')
    ed.js('localStorage.clear()')
    load(ed, url(out_dir, V_TOP, 'trench'))
    zc = GZ - ms.DEPTH - T1_D / 2
    f = fit_of(ed)
    js = sorted((q for q in f['joints'] if on_t1(q) is not None), key=lambda q: q[3])
    ss = [q[3] for q in js]
    pos_err = max((abs(on_t1(q) - q[3]) for q in js), default=1)
    z_err = max((abs(q[2] - zc) for q in js), default=1)
    report('T1 이음 = 측점 6 m 마다 · 관 위 (1 cm)', len(js) >= 5 and all(abs(s - 6 * round(s / 6)) < 1e-6 for s in ss)
           and all(abs((b - a) - 6.0) < 1e-6 for a, b in zip(ss[:-1], ss[1:]) if b - a < 6.5) and pos_err < 0.01 and z_err < 0.01,
           '%d 개 · 측점 %s · 자리 %.4f m · 높이 %.4f m' % (len(js), [round(s) for s in ss][:12], pos_err, z_err))
    bd = [q for q in f['bends'] if abs(q[0] - 14.0) < 0.01 and abs(q[1] - 3.0) < 0.01]
    ang = math.degrees(math.atan2(13.0, 26.0))
    report('T1 꺾인 곳 (14, 3) 곡관 (꺾임 %.1f°)' % ang, len(bd) == 1 and abs(bd[0][3] - ang) < 0.1 and abs(bd[0][2] - zc) < 0.01, json.dumps(bd))
    report('곡관 둘레 1 m 안 이음 없음', all(not (50.5 < q[3] < 57.5) for q in js), json.dumps(ss))
    # 관종 세 가지: 관 정보 칸에서 바꿈 → 색 · 저장
    inp = ce.Inp(ed)
    cc = ce.Cam(ed, V_TOP)
    rgb = {}
    h = w = None
    for m, name in (('ci', None), ('steel', '강관'), ('plastic', '비금속관')):
        if name:
            inp.click(*cc.proj([2.0, 3.0, GZ]))
            ed.frames(2)
            inp.click(*aria(ed, name))
            ed.frames(3)
            ed.js("document.getElementById('pcX').click()")
        g = grab(ed, os.path.join(shots_dir, 'p_mat_%s_gl.png' % m), True)
        h, w = g.shape[:2]
        rgb[m] = pipe_rgb(g, ct.Cam(V_TOP, w, h), zc)
        st = js_json(ed, "[window.RV_PIPES.lines[0].mat || '', ((JSON.parse(localStorage.getItem('rv_pipes:' + window.RV_PROJECT.title) || '{}').lines || {})['0'] || {}).mat || '']")
        report('관종 %s (RV_PIPES · 이 브라우저 저장)' % m, (m == 'ci' and st == ['', '']) or st == [m, m], json.dumps(st))
    c, s_, p = rgb['ci'], rgb['steel'], rgb['plastic']
    lum = lambda v: float(v @ np.array([0.299, 0.587, 0.114]))  # noqa: E731
    ok = lum(c) < 90 and s_[1] - s_[0] > 80 and s_[2] - s_[0] > 80 and lum(p) > 165 and min(np.abs(c - s_).max(), np.abs(c - p).max(), np.abs(s_ - p).max()) > 60
    report('세 관종 색이 다름 (주철 짙은 회색 · 강관 하늘색 · 비금속 흰색)', ok, '주철 %s · 강관 %s · 비금속 %s' % (np.round(c).tolist(), np.round(s_).tolist(), np.round(p).tolist()))
    # 관로 고치기: 꼭짓점 끌기 → 곡관이 따라감 · 점 넣고 끌면 새 곡관 · Ctrl+Z → 제자리
    inp.el_click('tgEdit')
    mp = ce.MapTf(ed)
    inp.click(*mp.en_scr(-20.0, 3.0))
    h0, h1 = mp.en_scr(14.0, 3.0), mp.en_scr(18.0, 9.0)
    inp.drag((round(h0[0]), round(h0[1])), (round(h1[0]), round(h1[1])), steps=12)
    ln = js_json(ed, 'window.RV_PIPES.lines[0]')
    v1 = ln['pts'][(ln.get('ctrl') or list(range(len(ln['pts']))))[1]]
    f = fit_of(ed)
    near = lambda q, p: math.hypot(q[0] - p[0], q[1] - p[1]) < 0.02  # noqa: E731
    report('꼭짓점 끌기 → 곡관이 새 자리 · 옛 자리엔 없음', any(near(q, v1) for q in f['bends']) and not any(near(q, (14.0, 3.0)) for q in f['bends']),
           '새 꼭짓점 (%.2f, %.2f) · 곡관 %s' % (v1[0], v1[1], [[round(q[0], 2), round(q[1], 2)] for q in f['bends']]))
    nb = len(f['bends'])
    mid = mp.en_scr((T1[0][0] + v1[0]) / 2, (T1[0][1] + v1[1]) / 2)  # 끈 뒤 첫 마디 가운데
    inp.dblclick(round(mid[0]), round(mid[1]))
    ln = js_json(ed, 'window.RV_PIPES.lines[0]')
    cps = [ln['pts'][k] for k in ln['ctrl']]
    report('두 번 누르기 → 꼭짓점 하나 더', len(cps) == 4, json.dumps([[round(c[0], 2), round(c[1], 2)] for c in cps]))
    q0, q1 = mp.en_scr(cps[1][0], cps[1][1]), mp.en_scr(cps[1][0], cps[1][1] + 3.0)
    inp.drag((round(q0[0]), round(q0[1])), (round(q1[0]), round(q1[1])), steps=10)
    ln = js_json(ed, 'window.RV_PIPES.lines[0]')
    vn = ln['pts'][ln['ctrl'][1]]
    f2 = fit_of(ed)
    report('넣은 꼭짓점 끌기 → 거기 새 곡관', any(near(q, vn) for q in f2['bends']) and len(f2['bends']) > nb,
           '꼭짓점 (%.2f, %.2f) · 곡관 %d → %d' % (vn[0], vn[1], nb, len(f2['bends'])))
    for _ in range(3):
        inp.key('z', 'KeyZ', 90, ce.CTRL)
    f3 = fit_of(ed)
    report('Ctrl+Z 셋 → 곡관 제자리 (14, 3)', any(near(q, (14.0, 3.0)) for q in f3['bends']) and len(js_json(ed, 'window.RV_PIPES.lines[0].pts')) == 3,
           json.dumps([[round(q[0], 2), round(q[1], 2)] for q in f3['bends']]))
    inp.key('Escape', 'Escape', 27)
    ed.js('localStorage.clear()')
    errs(ed, 'p')


# ---------------------------------------------------------------- f  벽 / 바닥 두 결 · 층 단추 없음
def test_faces(ed, out_dir, shots_dir):
    print('== f  터파기 면: 벽 · 바닥 두 결 (층 띠 없음)')
    ed.js('localStorage.clear()')
    load(ed, url(out_dir, V_PIT, 'trench'))
    G = grab(ed, os.path.join(shots_dir, 'f_pit_gl.png'), True)
    h, w = G.shape[:2]
    cam = ct.Cam(V_PIT, w, h)
    S = ct.all_sections(dict(TR_DEF))[0]
    j = 0
    A, Bp = S['pos'][j], S['pos'][j + 1]
    L = float(np.linalg.norm(Bp - A))
    fl, wl = [], []
    for a in np.arange(2.5, L - 2.5, 0.25):
        c2 = A + (Bp - A) * (a / L)
        for wq in np.linspace(-S['ws'] + 0.1, S['ws'] - 0.1, 9):
            if abs(wq) < S['D'] / 2 + 0.05:
                continue  # 관 빼고
            X = np.array([*(c2 + S['nl'][j] * wq), S['zs']])
            if ct.visible(cam, S, j, X) is True:
                p = cam.proj(X)
                if p is not None and 4 < p[0] < w - 4 and 4 < p[1] < h - 4:
                    fl.append(p)
        for z in np.linspace(S['zs'] + 0.15, S['zt'] - 0.1, 8):
            X = np.array([*(c2 + S['nl'][j] * (ct.w_at(S, z) - 0.01)), z])  # 먼 벽 (북쪽 · 카메라 반대쪽)
            if ct.visible(cam, S, j, X) is True:
                p = cam.proj(X)
                if p is not None and 4 < p[0] < w - 4 and 4 < p[1] < h - 4:
                    wl.append(p)
    fv, wv = lum(cv.bilinear(G, np.array(fl))) if fl else np.array([]), lum(cv.bilinear(G, np.array(wl))) if wl else np.array([])
    ok = fv.size >= 30 and wv.size >= 30 and np.median(fv) - np.median(wv) >= 40
    report('바닥이 벽보다 밝음 (두 결)', ok, '바닥 %d 점 밝기 %.0f · 벽 %d 점 밝기 %.0f' % (fv.size, np.median(fv) if fv.size else -1, wv.size, np.median(wv) if wv.size else -1))
    st = js_json(ed, "['tgBed', 'tgFill'].map((i) => !!document.getElementById(i))")
    report('표시 항목에 층 단추 없음 (기초·보호 · 되메우기)', st == [False, False], json.dumps(st))
    ed.js("localStorage.setItem('rv_ui:show', JSON.stringify({bed: false, fill: true, lbl: true}))")
    load(ed, url(out_dir, V_PIT, 'trench'))
    G2 = grab(ed, os.path.join(shots_dir, 'f_pit_oldshow_gl.png'), True)
    d = np.abs(G2 - G).max()
    report('옛 층 표시 기억을 넣어도 그림 그대로', G2.shape == G.shape and d <= 3, '최대 차이 %d' % d)
    ed.js('localStorage.clear()')
    errs(ed, 'f')


# ---------------------------------------------------------------- c · s · r · x  가시설 (360)
def click_ground(inp, cc, e, n):
    p = cc.proj([e, n, GZ])
    inp.click(p[0], p[1])
    return p


def qty_total(ed):
    return float(ed.js("document.getElementById('qt_cut').dataset.v"))


def test_shore_view(ed, out_dir, shots_dir):
    print('== c  가시설 만들기 (360) · Esc')
    ed.js('localStorage.clear()')
    report('불러옴 (매설)', load(ed, url(out_dir, V_C)))
    inp = ce.Inp(ed)
    cc = ce.Cam(ed, V_C)
    ed.js("document.getElementById('tgQty').click()")
    ed.frames(3)
    q0 = qty_total(ed)
    inp.click(*aria(ed, '가시설'))
    st = js_json(ed, "{box: !document.getElementById('shBox').hidden, n: document.querySelectorAll('#shTypes button').length, on: document.getElementById('tgShore').classList.contains('on')}")
    report('[가시설] → 종류 셋', st == {'box': True, 'n': 3, 'on': True}, json.dumps(st))
    # Esc: 시작점만 찍고 취소
    inp.click(*aria(ed, '시트파일'))
    c0 = chip(ed)
    report('종류 고름 → 안내 «시작점» · 표현 그대로 (영역)', c0['show'] and c0['tip'] == '시작점' and c0['mode'] == 'area' and c0['on'] == ['sheet'], json.dumps(c0, ensure_ascii=False))
    click_ground(inp, cc, A_E, 3.0)
    c1 = chip(ed)
    report('시작점 누름 → «끝점»', c1['tip'] == '끝점' and c1['a'] != '', json.dumps(c1, ensure_ascii=False))
    inp.key('Escape', 'Escape', 27)
    c2 = chip(ed)
    report('Esc → 취소 (안내 닫힘 · 구간 없음)', not c2['show'] and c2['on'] == [] and shore_of(ed) == [], json.dumps(c2, ensure_ascii=False))
    # 만들기: 토류벽 · 두 점
    inp.click(*aria(ed, '토류벽 흙막이'))
    click_ground(inp, cc, A_E, 3.0)
    click_ground(inp, cc, B_E, 3.0)
    sh = shore_of(ed)
    sa, sb = chain_s(A_E, 3.0), chain_s(B_E, 3.0)
    ok = len(sh) == 1 and sh[0]['t'] == 'hpile' and abs(sh[0]['a'] - sa) <= 0.05 and abs(sh[0]['b'] - sb) <= 0.05
    report('두 점 → shore [{a, b, t: hpile}] (측점 5 cm)', ok, '%s · 따로 %.2f ~ %.2f' % (json.dumps(sh), sa, sb))
    report('측점 0.01 m 로 반올림', all(abs(q[k] * 100 - round(q[k] * 100)) < 1e-6 for q in sh for k in ('a', 'b')), json.dumps(sh))
    c3 = chip(ed)
    report('만든 뒤 안내 닫힘', not c3['show'], json.dumps(c3, ensure_ascii=False))
    rows = js_json(ed, "[...document.querySelectorAll('#shList .shRow')].map((r) => [r.dataset.t, r.querySelector('.shGo').textContent, r.querySelector('select').value])")
    report('목록 한 줄 (종류 · 길이)', len(rows) == 1 and rows[0][0] == 'hpile' and rows[0][2] == 'hpile' and ('%.2f m' % (sh[0]['b'] - sh[0]['a']) if sh else '?') in rows[0][1],
           json.dumps(rows, ensure_ascii=False))
    stv = stored_shore(ed)
    report('이 브라우저 저장 (고친 줄에 shore)', stv == sh, json.dumps(stv))
    ed.screenshot(os.path.join(shots_dir, 'c_made.png'))
    errs(ed, 'c')

    print('== s  기울기 0 (영역 테두리 · 자른 선 · 단면 · 판 곳 · 수량)')
    a, b = sh[0]['a'], sh[0]['b']
    D, g, zc = T1_D, GZ, GZ - ms.DEPTH - T1_D / 2
    A_n, wb, wv = sect_area(D, g, zc, TR_DEF, TR_DEF['n'])
    A_0, _, _ = sect_area(D, g, zc, TR_DEF, 0.0)
    q1 = qty_total(ed)
    want = (A_n - A_0) * (b - a)
    report('수량 터파기 = 줄어든 만큼 (따로 셈)', abs((q0 - q1) - want) <= 1e-3 and q1 < q0, '%.4f → %.4f m³ · 줄어듦 %.4f · 따로 %.4f' % (q0, q1, q0 - q1, want))
    ed.js("document.getElementById('tgQty').click()")  # 수량 칸 닫음 (자른 선을 가리지 않게)
    ed.frames(2)
    # 영역 (지금 표현): 임시점용 주황 점선 = 구간 안 ±wb (곧은 벽 폭) · 밖 ±wv
    fa = grab(ed, os.path.join(shots_dir, 's_area_full.png'))
    h, w = fa.shape[:2]
    cam = ct.Cam(V_C, w, h)
    o_in = cu.outline_offsets(fa, cam, wb, np.arange(A_E + 0.6, B_E - 0.6, 0.2))
    o_out = cu.outline_offsets(fa, cam, wv, np.arange(A_E - 6.0, A_E - 0.6, 0.2))
    report('영역: 임시점용 = 구간 안 ±wb · 밖 ±wv', o_in.size >= 15 and np.median(np.abs(o_in)) <= 1.5 and o_out.size >= 15 and np.median(np.abs(o_out)) <= 1.5,
           '안 %d 점 %.2f px · 밖 %d 점 %.2f px' % (o_in.size, np.median(np.abs(o_in)) if o_in.size else -1, o_out.size, np.median(np.abs(o_out)) if o_out.size else -1))
    # 터파기: 자른 선 (겹쳐 그린 어두운 선) = ±wb (구간 안)
    ed.js("document.getElementById('mDepth').click()")
    ed.frames(3)
    full = grab(ed, os.path.join(shots_dir, 's_trench_full.png'))
    gl = grab(ed, os.path.join(shots_dir, 's_trench_gl.png'), True)
    offs = []
    for e in np.arange(A_E + 0.6, B_E - 0.6, 0.2):
        for sg in (1, -1):
            P0 = np.array([e, 3 + sg * wb, GZ])
            p0, sd = ct.screen_dir(cam, P0, P0 + np.array([0.3, 0.0, 0.0]))
            if p0 is None or not (12 < p0[0] < w - 12 and 12 < p0[1] < h - 12):
                continue
            x = ct.dark_line_at(full, gl, p0, np.array([-sd[1], sd[0]]))
            if x is not None:
                offs.append(x)
    v = np.abs(np.array(offs))
    report('360 자른 선 = ±wb (구간 안 · 곧은 벽)', v.size >= 20 and np.median(v) <= 1.0, '표본 %d · 중앙 %.2f px' % (v.size, np.median(v) if v.size else -1))
    # 관 정보 → [이 자리 단면] = 설정 창이 그 자리 단면 (구간 안 · 밖)
    def sec_at(e):
        inp.click(*cc.proj([e, 3.0, GZ]))
        ed.frames(2)
        inp.click(*aria(ed, '이 자리 단면'))
        ed.frames(3)
        r = js_json(ed, "{at: document.getElementById('secDlg').dataset.at, svg: document.getElementById('secSvg').innerHTML, rects: document.querySelectorAll('#secSvg rect.sh').length}")
        return r
    sc = sec_at((A_E + B_E) / 2)
    report('단면 (구간 안): 폭 2·wb · 가시설 그림', ('폭 %.2f' % (2 * wb)) in sc['svg'] and sc['rects'] >= 4, '측점 %s · 가시설 %d' % (sc['at'], sc['rects']))
    ed.screenshot(os.path.join(shots_dir, 's_sec_in.png'))
    ed.js("document.getElementById('secClose').click()")
    sc2 = sec_at(A_E - 2.0)
    report('단면 (구간 밖): 폭 2·wv · 가시설 없음', ('폭 %.2f' % (2 * wv)) in sc2['svg'] and sc2['rects'] == 0, '측점 %s' % sc2['at'])
    ed.js("document.getElementById('secClose').click()")

    print('== r  세 종류 그림 · 종류 바꾸기')
    set_t = lambda t: ed.js("(function(){ const s = document.querySelector('#shList .shRow select'); s.value = '%s'; s.dispatchEvent(new Event('change', {bubbles: true})); })()" % t)  # noqa: E731
    sec_at((A_E + B_E) / 2)  # 설정 창 = 구간 안 단면 (종류 바꾸면 바로 다시)
    svgs = {}
    for t in ('panel', 'sheet', 'hpile'):
        set_t(t)
        ed.frames(4)
        svgs[t] = ed.js("document.getElementById('secSvg').innerHTML")
        report('종류 바꾸기 → t = %s (저장)' % t, [q['t'] for q in shore_of(ed)] == [t] and [q['t'] for q in (stored_shore(ed) or [])] == [t])
        ed.screenshot(os.path.join(shots_dir, 'r_sec_%s.png' % t))
    report('단면 그림이 종류마다 다름', len(set(svgs.values())) == 3)
    ed.js("document.getElementById('secClose').click()")
    ed.frames(2)
    imgs = {}
    for t in ('panel', 'sheet', 'hpile'):
        set_t(t)
        ed.frames(4)
        imgs[t] = grab(ed, os.path.join(shots_dir, 'r_%s_gl.png' % t), True)
        if t == 'panel':  # 기울기 0.5 로: 구간 안 wb ~ wv 띠 = 안 판 · 밖 = 판 (조립식 판은 굴착면 안쪽 · 땅 위 0.1 m 라 띠에 안 걸림)
            ct.set_cfg(ed, {'n': 0.5})
            ed.js("document.getElementById('secClose').click()")
            ed.frames(3)
            on = grab(ed, os.path.join(shots_dir, 's_n05_gl.png'), True)
            ed.js("document.getElementById('tgPipes').click()")
            off = grab(ed, os.path.join(shots_dir, 's_n05_off_gl.png'), True)
            ed.js("document.getElementById('tgPipes').click()")
            _, _, wv5 = sect_area(D, g, zc, dict(TR_DEF, n=0.5), 0.5)
            dm = np.abs(on - off).max(axis=2) > 20
            inside = poly_mask(cam, band_quads(A_E + 0.5, B_E - 0.5, wb + 0.35, wv5 - 0.06, GZ)) & ~ct.dilate(poly_mask(cam, band_quads(A_E, B_E, 0, wb + 0.3, GZ)), 2)
            outside = ~ct.dilate(~poly_mask(cam, band_quads(A_E - 4.0, A_E - 1.0, wb + 0.35, wv5 - 0.06, GZ)), 2)
            fi, fo = dm[inside].mean() if inside.any() else 1, dm[outside].mean() if outside.any() else 0
            report('기울기 0.5: 구간 안 wb ~ wv 띠 안 판 · 밖은 판', inside.sum() > 150 and fi < 0.02 and outside.sum() > 150 and fo > 0.8,
                   '안 %d px 중 %.1f %% 바뀜 · 밖 %d px 중 %.1f %%' % (inside.sum(), fi * 100, outside.sum(), fo * 100))
            ct.set_cfg(ed, {'n': TR_DEF['n']})
            ed.js("document.getElementById('secClose').click()")
            ed.frames(3)
            imgs[t] = grab(ed, os.path.join(shots_dir, 'r_%s_gl.png' % t), True)
    ed.js("document.querySelector('#shList .shDel').click()")
    ed.frames(4)
    imgs['none'] = grab(ed, os.path.join(shots_dir, 'r_none_gl.png'), True)
    report('× → 구간 지움 (저장)', shore_of(ed) == [] and not (stored_shore(ed) or []) and not js_json(ed, "document.querySelectorAll('#shList .shRow').length"))
    reg = ct.dilate(poly_mask(cam, band_quads(A_E, B_E, -wb, wb + 0.3, GZ)), 6)
    keys = ['panel', 'hpile', 'sheet', 'none']
    pairs = []
    for i in range(4):
        for k in range(i + 1, 4):
            d = (np.abs(imgs[keys[i]] - imgs[keys[k]]).max(axis=2) > 20)[reg].sum()
            pairs.append((keys[i], keys[k], int(d)))
    report('세 종류 · 없음 그림이 서로 다름 (구간 둘레 바뀐 화소 ≥ 300)', all(d >= 300 for _, _, d in pairs), ' · '.join('%s/%s %d' % p for p in pairs))
    errs(ed, 's r x')


# ---------------------------------------------------------------- m  지도 · 꼭짓점 끌면 구간 자름
def test_shore_map(ed, out_dir, shots_dir):
    print('== m  지도에서 두 점 · 꼭짓점 끌기 → 구간 자름')
    ed.js('localStorage.clear()')
    report('불러옴', load(ed, url(out_dir, V_C)))
    inp = ce.Inp(ed)
    inp.click(*aria(ed, '가시설'))
    ed.frames(3)
    mp = ce.MapTf(ed)
    s1, s2 = 40.0, 48.0
    inp.click(*aria(ed, '시트파일'))
    for s in (s1, s2):
        p = t1_point(s)
        inp.click(*mp.en_scr(p[0], p[1]))
    sh = shore_of(ed)
    report('지도 두 점 → 시트파일 구간 (측점 0.3 m)', len(sh) == 1 and sh[0]['t'] == 'sheet' and abs(sh[0]['a'] - s1) <= 0.3 and abs(sh[0]['b'] - s2) <= 0.3, json.dumps(sh))
    inp.click(*aria(ed, '조립식 간이흙막이'))
    mp = ce.MapTf(ed)  # 목록이 한 줄 늘어 지도 칸 높이가 바뀜
    for s in (58.0, 80.0):
        p = t1_point(s)
        inp.click(*mp.en_scr(p[0], p[1]))
    sh = shore_of(ed)
    report('두 번째 구간 (조립식 · 58 ~ 80)', len(sh) == 2 and sh[1]['t'] == 'panel' and abs(sh[1]['a'] - 58) <= 0.3 and abs(sh[1]['b'] - 80) <= 0.3, json.dumps(sh))
    ed.screenshot(os.path.join(shots_dir, 'm_map.png'))
    # 관로 고치기: T1 고르고 끝 꼭짓점 (40, 16) → (20, 6) 으로 끌기
    inp.el_click('tgEdit')
    mp = ce.MapTf(ed)
    inp.click(*mp.en_scr(-20.0, 3.0))
    h0 = mp.en_scr(40.0, 16.0)
    h1 = mp.en_scr(20.0, 6.0)
    inp.drag((round(h0[0]), round(h0[1])), (round(h1[0]), round(h1[1])), steps=14)
    ln = js_json(ed, 'window.RV_PIPES.lines[0]')
    P = [np.array(q[:2]) for q in ln['pts']]
    L = sum(float(np.linalg.norm(b - a)) for a, b in zip(P[:-1], P[1:]))
    sh2 = ln.get('shore', [])
    ok = len(sh2) == 2 and sh2[0] == sh[0] and abs(sh2[1]['a'] - sh[1]['a']) < 1e-9 and abs(sh2[1]['b'] - round(L, 2)) <= 0.011 and L < 70
    report('끝 꼭짓점 당김 → 구간 끝 = 새 길이 (앞 구간 그대로)', ok, '새 길이 %.2f · %s' % (L, json.dumps(sh2)))
    inp.key('z', 'KeyZ', 90, ce.CTRL)
    report('Ctrl+Z → 구간도 돌아옴', shore_of(ed) == sh, json.dumps(shore_of(ed)))
    inp.key('Escape', 'Escape', 27)
    errs(ed, 'm')


# ---------------------------------------------------------------- d  두 번 누르기 → 관로 고치기
def test_dbl(ed, out_dir, shots_dir):
    print('== d  관 두 번 누르기 → 관로 고치기')
    ed.js('localStorage.clear()')
    load(ed, url(out_dir, V_C))
    inp = ce.Inp(ed)
    cc = ce.Cam(ed, V_C)
    p = cc.proj([-12.0, 3.0, GZ])
    inp.dblclick(p[0], p[1])
    ed.frames(3)
    st = js_json(ed, "{on: document.getElementById('tgEdit').classList.contains('on'), chip: document.getElementById('edChip').classList.contains('show'),"
                     " sel: document.getElementById('edChip').dataset.sel, card: document.getElementById('pipeCard').hidden}")
    report('관 위 두 번 → 관로 고치기 · T1 고름 · 관 정보 닫힘', st == {'on': True, 'chip': True, 'sel': '0', 'card': True}, json.dumps(st))
    ed.screenshot(os.path.join(shots_dir, 'd_dbl.png'))
    inp.key('Escape', 'Escape', 27)
    q = cc.proj([-12.0, 6.0, GZ])  # 관 없는 땅
    inp.dblclick(q[0], q[1])
    report('빈 땅 두 번 → 그대로', not ed.js("document.getElementById('tgEdit').classList.contains('on')"))
    errs(ed, 'd')


# ---------------------------------------------------------------- h  프로그램 (hostmock) 저장
def test_host(ed, out_dir, shots_dir):
    print('== h  프로그램: save_pipes 글에 shore')
    page = ci.prepare(out_dir, host=True)
    try:
        ed.js('localStorage.clear()')
        report('불러옴 (프로그램)', load(ed, url(out_dir, V_C, page=page)))
        inp = ce.Inp(ed)
        cc = ce.Cam(ed, V_C)
        inp.click(*aria(ed, '가시설'))
        inp.click(*aria(ed, '조립식 간이흙막이'))
        click_ground(inp, cc, B_E, 3.0)
        click_ground(inp, cc, A_E, 3.0)  # 거꾸로 찍어도 a < b
        time.sleep(1.4)
        calls = js_json(ed, "window.__host.calls.filter((c) => c.name === 'save_pipes').map((c) => c.arg)")
        sh = None
        if calls:
            t = calls[-1]
            sh = json.loads(t[t.index('{'):t.rindex('}') + 1])['lines'][0].get('shore')
            report('글 꼴 window.RV_PIPES = …;', t.startswith('window.RV_PIPES = ') and t.rstrip().endswith(';'))
        sa, sb = chain_s(A_E, 3.0), chain_s(B_E, 3.0)
        report('save_pipes 한 번 · shore = [{a, b, t: panel}]', len(calls) == 1 and sh and len(sh) == 1 and sh[0]['t'] == 'panel'
               and abs(sh[0]['a'] - sa) <= 0.05 and abs(sh[0]['b'] - sb) <= 0.05, '%d 번 · %s' % (len(calls), json.dumps(sh)))
        report('이 브라우저 저장 안 함', stored_shore(ed) is None)
        # 관 정보 칸 관종 → save_pipes 글에 mat
        inp.click(*cc.proj([-12.0, 3.0, GZ]))
        ed.frames(2)
        inp.click(*aria(ed, '비금속관'))
        time.sleep(1.4)
        calls = js_json(ed, "window.__host.calls.filter((c) => c.name === 'save_pipes').map((c) => c.arg)")
        mat = None
        if calls:
            t = calls[-1]
            mat = json.loads(t[t.index('{'):t.rindex('}') + 1])['lines'][0].get('mat')
        report('관 정보 [비금속관] → save_pipes 글에 mat plastic', len(calls) == 2 and mat == 'plastic', '%d 번 · %s' % (len(calls), mat))
        errs(ed, 'h')
    finally:
        ci.cleanup(out_dir)
        ed.js('localStorage.clear()')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--shots', default=None)
    ap.add_argument('--only', default='', help='o,p,f,c,m,d,h 중 일부만')
    args = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    out_dir = os.path.abspath(args.out)
    shots_dir = os.path.abspath(args.shots or os.path.join(out_dir, '_shots_shore'))
    os.makedirs(shots_dir, exist_ok=True)
    cj = os.path.join(out_dir, 'data', 'calib.js')
    if os.path.exists(cj):
        os.remove(cj)
    only = set(args.only.split(',')) if args.only else set('opfcmdh')
    ed = Edge(os.path.join(shots_dir, '_prof_s'), cv.WIN).__enter__()
    try:
        ed.net(block=ci.BLOCK)
        load(ed, url(out_dir, V_C))
        if 'o' in only:
            test_opaque(ed, out_dir, shots_dir)
        if 'p' in only:
            test_pipes(ed, out_dir, shots_dir)
        if 'f' in only:
            test_faces(ed, out_dir, shots_dir)
        if 'c' in only:
            test_shore_view(ed, out_dir, shots_dir)
        if 'm' in only:
            test_shore_map(ed, out_dir, shots_dir)
        if 'd' in only:
            test_dbl(ed, out_dir, shots_dir)
        if 'h' in only:
            test_host(ed, out_dir, shots_dir)
    finally:
        ed.__exit__(None, None, None)
    for where, e in R['errs']:
        print('   오류 [%s]: %s' % (where, e))
    R['ok'] &= not R['errs']
    print('사진:', shots_dir)
    print('전체:', '통과' if R['ok'] else '실패')
    return 0 if R['ok'] else 1


if __name__ == '__main__':
    sys.exit(main())
