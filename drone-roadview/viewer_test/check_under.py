"""지하 시설물 (표현 넷 · 표시 항목 · 부분 개방 · 단면 · 관 정보) 시험: make_synth --trench 폴더에 땅 높이 · 관 표고가
바뀌는 관 U4 (hasZ) 를 더해 화면 없는 Edge (cdp.py, 실제 시간 · 진짜 마우스) 로 찍고, 보기 화면 수식과 따로 짠 계산으로 잰다.

    python -I check_under.py --out <make_synth --trench 폴더> [--shots <폴더>]

  m  표현 넷 (매설 · 터파기 · 부분 개방 · 단면): 단추 하나만 켜짐 · 주소 ?mode= · 바꾸면 열린 곳 · 단면 칸 지움 ·
     표시 항목 이 브라우저 기억 (관로 본체는 안 기억) · «가시화 기본값» ↔ «직접 입력» · calib.js trench 에 prot (옛 파일 = 기본값)
  c  관 정보 (매설): T1 누름 → 관경 · 토피 · 관 중심고 · 지반고 = 따로 셈 · Esc 닫기 ·
     매설 = 땅 속 관이 비침 (WebGL 만 관로 켬 ↔ 끔 바뀜)
  p  부분 개방: 고르자마자 화면 가운데 가까운 관을 엶 (매설과 다른 그림) · 길이 바 → 바로 바뀜 · 사진 바뀜 → 다시 고름 ·
     T1 누름 → 측점 s · 길이 10 m → [s − 5, s + 5] · WebGL 만 (겹쳐 그림 숨김) 매설 ↔ 연 뒤 바뀐 화소 =
     그 구간 구멍 (지표 ±wv · 2 px 넓힘) 안만 · 안 (2 px 좁힘) 은 90 % 이상 · 먼 끝 자른 면 층 경계
     (포장 복구 / 되메우기 = g − tp · 되메우기 / 보호층 = 관 윗면 + prot) ↔ 투영 (중앙 ≤ 1.5 px) · 폭 + 0.3 m → 넓힌 구멍 ·
     닫기 → 매설 · 화소 그대로 · 지도에서 관 누름 → 엶 · Esc → 매설
  s  단면: 고르자마자 가까운 관 단면 칸 (매설과 다른 그림) · U4 (비탈 · hasZ) · T1 누름 → 칸 숫자 (지반고 · 관 상단 · 중심 · 하단 · 토피 · 깊이 · 폭 위 / 아래 · 기초 · 보호층 · 측점)
     = 자료에서 따로 셈 (1 cm) · 설정 바꿈 → 다시 · 360 노란 선 ↔ 그 측점 가로선 투영 (중앙 ≤ 1.5 px)
  l  층 표시 (터파기): 기초 · 보호 / 되메우기 켜고 끄면 바뀐 화소가 구멍 안만 · 실제로 바뀜
  h  http 로 열어도 file:// 과 같은 그림 (부분 개방) · 프로그램 (hostmock): 그림 단추 · save_calib 에 prot
  z  수치지형도 프로젝트 (project.terrain 에 «수치지도»): 맞춤 «높이» = «수치지형도로 맞춰짐» · 파일 dz 무시 (0) ·
     [점 찍어 맞추기] 켜면 있던 짝으로 한 번 다시 맞춤 · 점 짝 3 개로 풀어도 dz 0 (동 · 북은 풀림) · 수치지도 아닌 프로젝트는 높이 칸 그대로
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
import time
import urllib.parse

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import check_edit as ce  # noqa: E402
import check_interact as ci  # noqa: E402
import check_trench as ct  # noqa: E402
import check_view as cv  # noqa: E402
import make_synth as ms  # noqa: E402
from cdp import Edge  # noqa: E402

GZ = ms.GZ
TR_DEF = dict(tp=0.2, n=0.3, margin=0.3, bed=0.1, prot=0.3)
CFG = dict(tp=0.3, n=0.5, margin=0.4, bed=0.15, prot=0.2)
T1 = [(-40.0, 3.0), (14.0, 3.0), (40.0, 16.0)]                 # make_synth TR_LINES[0] (D300 · 평균매설깊이)
T1_D = 0.3
# 땅 높이 · 관 중심 표고가 꼭짓점마다 다른 관 (hasZ)
U4 = {'layer': 1, 'label': 'U4', 'dia': 0.4, 'v': [(-22.0, -20.0), (-2.0, -26.0), (12.0, -22.0)],
      'g': [50.0, 52.0, 49.6], 'z': [48.0, 49.5, 47.6]}
V_OPEN = ('o', '0001', 285, -50, 40)          # T1 서쪽 · 먼 끝 자른 면이 보임
V_BURY = ('b', '0001', 285, -50, 40)
V_SEC = ('s', '0004', 180, -42, 50)           # U4 (남쪽)
V_PIT = ('v1_down_n', '0002', 0, -60, 70)     # check_trench 와 같은 자리 (T1 · T3 구덩이)
CLICK_T1 = (-8.0, 3.0)
CLICK_U4 = (5.0, -24.0)
R = {'ok': True, 'errs': []}


def report(name, ok, text=''):
    print('   %-46s %s  %s' % (name, text, '통과' if ok else '실패'))
    R['ok'] &= bool(ok)
    return bool(ok)


def errs(ed, where):
    e = cv.errors(ed)
    for x in e:
        R['errs'].append((where, str(x)[:200]))
    return report('JS 오류 (%s)' % where, not e, '%d 줄' % len(e))


def js_json(ed, expr):
    return json.loads(ed.js('JSON.stringify(%s)' % expr))


def add_u4(out_dir):
    """data/pipes.js 에 U4 를 (없으면) 더한다."""
    p = os.path.join(out_dir, 'data', 'pipes.js')
    t = open(p, encoding='utf-8').read()
    P = json.loads(t[t.index('{'):t.rindex('}') + 1])
    if not any(l.get('label') == 'U4' for l in P['lines']):
        P['lines'].append({'layer': U4['layer'], 'label': U4['label'], 'dia': U4['dia'], 'hasZ': True,
                           'pts': [[x, y, z] for (x, y), z in zip(U4['v'], U4['z'])], 'g': U4['g']})
        with open(p, 'w', encoding='utf-8', newline='\n') as f:
            f.write('window.RV_PIPES = ' + json.dumps(P, ensure_ascii=False) + ';\n')


def url(out_dir, view, mode='', extra='', base=None):
    q = ce.q_of(view, ('&mode=' + mode if mode else '') + extra)
    return (base + '?' + q) if base else pathlib.Path(out_dir, '로드뷰.html').resolve().as_uri() + '?' + q


def load(ed, u):
    ed.goto(u)
    ok = ed.wait_js(cv.READY, timeout=60)
    ed.frames(4)
    return ok


def grab(ed, png, gl_only=False):
    """360 칸만 (CSS 자리로 자름) · gl_only = WebGL 만 (겹쳐 그림 · 안내 줄 · 칸 · 단추 숨김)."""
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


def cams(ed, view):
    """진짜 마우스용 (client) · 자른 그림용 (0,0 = 360 왼쪽 위)."""
    c = ce.Cam(ed, view)
    return c, ct.Cam(view, c.w, c.h)


# ---------------------------------------------------------------- 따로 짠 단면 계산 (보기 화면 수식과 따로)
def sect(D, g, zc, tr, ex=0.0):
    zs = min(zc - D / 2, g - 0.01)
    zb = zs - tr['bed']
    wb = D / 2 + tr['margin'] + ex
    zt = g - tr['tp']
    wv = wb + tr['n'] * max(0.0, zt - zb)
    top = zc + D / 2
    zp = max(zs, min(top + tr['prot'], zt))
    return dict(zs=zs, zb=zb, wb=wb, zt=zt, wv=wv, top=top, zp=zp, g=g, zc=zc, D=D, n=tr['n'])


def w_at(X, z):
    return X['wv'] if z >= X['zt'] else X['wb'] + X['n'] * max(0.0, z - X['zb'])


def on_line(verts, e, n):
    """(e, n) 에 가장 가까운 마디 k · 매개 t · 측점."""
    best = None
    acc = 0.0
    for k, (a, b) in enumerate(zip(verts[:-1], verts[1:])):
        a, b = np.array(a), np.array(b)
        d = b - a
        L = float(np.hypot(*d))
        t = float(np.clip(((np.array([e, n]) - a) @ d) / (L * L), 0, 1))
        dist = float(np.hypot(*(a + d * t - np.array([e, n]))))
        if best is None or dist < best[0]:
            best = (dist, k, t, acc + t * L)
        acc += L
    return best


def expect_sec(line, e, n, tr, cover):
    if line == 'U4':
        _, k, t, s = on_line(U4['v'], e, n)
        g = U4['g'][k] + (U4['g'][k + 1] - U4['g'][k]) * t
        zc = U4['z'][k] + (U4['z'][k + 1] - U4['z'][k]) * t
        D = U4['dia']
    else:
        _, k, t, s = on_line(T1, e, n)
        g, D = GZ, T1_D
        zc = g - cover - D / 2
    X = sect(D, g, zc, tr)
    return dict(g=g, top=X['top'], zc=zc, bot=zc - D / 2, cover=g - X['top'], D=D, depth=g - X['zb'],
                wtop=2 * X['wv'], wbot=2 * X['wb'], bed=tr['bed'], prot=max(0.0, X['zp'] - X['top'])), s


def num_of(txt):
    t = txt.replace('EL', '').replace('m', '').strip()
    if t.startswith('D'):
        return int(t[1:]) / 1000
    return float(t)


# ---------------------------------------------------------------- m  표현 · 표시 항목 · 저장
EXPORT_JS = ct.EXPORT_JS


def test_modes(ed, out_dir, shots_dir):
    print('== m  표현 · 표시 항목 · 가시화 기본값 · 저장')
    ed.js('localStorage.clear()')
    report('불러옴 (매설)', load(ed, url(out_dir, V_BURY)))
    on = lambda: js_json(ed, "{m: document.getElementById('app').dataset.mode, on: ['mSurf','mDepth','mOpen','mSec'].filter((i) => document.getElementById(i).classList.contains('on'))}")  # noqa: E731
    st = on()
    report('처음 = 매설 (단추 하나)', st == {'m': 'bury', 'on': ['mSurf']}, json.dumps(st))
    for bid, m in (('mDepth', 'trench'), ('mOpen', 'open'), ('mSec', 'sec'), ('mSurf', 'bury')):
        ed.js("document.getElementById('%s').click()" % bid)
        ed.frames(2)
        st = on()
        report('%s → %s' % (bid, m), st == {'m': m, 'on': [bid]}, json.dumps(st))
    for q, m in (('depth', 'trench'), ('trench', 'trench'), ('open', 'open'), ('sec', 'sec'), ('xx', 'bury')):
        load(ed, url(out_dir, V_BURY, q))
        report('?mode=%s → %s' % (q, m), on()['m'] == m)
    # 표시 항목: 기억 (관로 본체 빼고)
    load(ed, url(out_dir, V_BURY))
    st0 = js_json(ed, "['tgPipes','tgPit','tgBed','tgFill','tgLbl'].map((i) => document.getElementById(i).classList.contains('on'))")
    report('표시 처음 (관로 · 터파기 · 기초 · 보호 · 이름표 켬 · 되메우기 끔)', st0 == [True, True, True, False, True], json.dumps(st0))
    for i in ('tgPipes', 'tgBed', 'tgFill', 'tgLbl'):
        ed.js("document.getElementById('%s').click()" % i)
    load(ed, url(out_dir, V_BURY))
    st1 = js_json(ed, "['tgPipes','tgPit','tgBed','tgFill','tgLbl'].map((i) => document.getElementById(i).classList.contains('on'))")
    report('다시 열기 → 표시 기억 (관로 본체는 켬)', st1 == [True, True, False, True, False], json.dumps(st1))
    ed.js('localStorage.clear()')
    # 가시화 기본값 ↔ 직접 입력
    load(ed, url(out_dir, V_BURY))
    tag = lambda: js_json(ed, "[document.getElementById('ugTag').textContent, document.getElementById('ugTag').classList.contains('user')]")  # noqa: E731
    report('기본값 → «가시화 기본값»', tag() == ['가시화 기본값', False], json.dumps(tag()))
    ct.set_cfg(ed, {'prot': 0.4})
    report('보호층 0.4 → «직접 입력»', tag() == ['직접 입력', True], json.dumps(tag()))
    tr, _ = ct.exported_trench(ed)
    report('내려받는 trench 에 prot', tr == dict(TR_DEF, prot=0.4, on=True), json.dumps(tr))
    ed.js("document.getElementById('secDef').click()")
    ed.frames(2)
    report('기본값 단추 → «가시화 기본값»', tag() == ['가시화 기본값', False], json.dumps(tag()))
    ed.js("document.getElementById('secClose').click()")
    # 옛 calib.js (prot 없음) → 기본 보호층
    cj = os.path.join(out_dir, 'data', 'calib.js')
    with open(cj, 'w', encoding='utf-8', newline='\n') as f:
        f.write('window.RV_CALIB = ' + json.dumps({'videos': {}, 'trench': dict(tp=0.25, n=0.4, margin=0.35, bed=0.15, on=True)}) + ';\n')
    try:
        ed.js('localStorage.clear()')
        load(ed, url(out_dir, V_BURY))
        tr, _ = ct.exported_trench(ed)
        report('옛 trench (prot 없음) → 보호층 기본 0.3', tr == dict(tp=0.25, n=0.4, margin=0.35, bed=0.15, prot=0.3, on=True), json.dumps(tr))
    finally:
        os.remove(cj)
    ed.js('localStorage.clear()')
    errs(ed, 'm')


# ---------------------------------------------------------------- c  관 정보
def test_card(ed, out_dir, shots_dir):
    print('== c  관 정보 (매설)')
    ed.js('localStorage.clear()')
    load(ed, url(out_dir, V_BURY))
    inp = ce.Inp(ed)
    cc, _ = cams(ed, V_BURY)
    p = cc.proj([CLICK_T1[0], CLICK_T1[1], GZ])
    inp.click(p[0], p[1])
    st = js_json(ed, "{shown: !document.getElementById('pipeCard').hidden, d: Object.assign({}, document.getElementById('pipeCard').dataset),"
                     " v: Object.fromEntries([...document.querySelectorAll('#pcRows .v[id]')].map((e) => [e.id, [Number(e.dataset.v), e.textContent]]))}")
    ed.screenshot(os.path.join(shots_dir, 'c_card.png'))
    ok = st['shown'] and st['d'].get('src') == '0'
    report('T1 누름 → 관 정보', ok, json.dumps(st['d']))
    if ok:
        e, n = float(st['d']['e']), float(st['d']['n'])
        report('누른 자리 (관 위 · 5 cm 안)', abs(n - 3.0) < 1e-6 and abs(e - CLICK_T1[0]) < 0.05, '%.3f, %.3f' % (e, n))
        want, _ = expect_sec('T1', e, n, TR_DEF, ms.DEPTH)
        rows = (('pc_dia', 'D', 'D300'), ('pc_cover', 'cover', '1.00 m'), ('pc_zc', 'zc', 'EL 48.85'), ('pc_g', 'g', 'EL 50.00'))
        bad = [(k, st['v'].get(k), want[w], txt) for k, w, txt in rows
               if k not in st['v'] or abs(st['v'][k][0] - want[w]) > 1e-6 or st['v'][k][1] != txt]
        report('관경 · 토피 · 관 중심고 · 지반고 = 따로 셈', not bad, json.dumps(bad, ensure_ascii=False) if bad else 'D300 · 1.00 m · EL 48.85 · EL 50.00')
    inp.key('Escape', 'Escape', 27)
    report('Esc → 닫힘', ed.js("document.getElementById('pipeCard').hidden"))
    q = cc.proj([CLICK_T1[0], CLICK_T1[1] + 4.0, GZ])  # 관 없는 땅
    inp.click(p[0], p[1])
    inp.click(q[0], q[1])
    report('빈 땅 누름 → 닫힘', ed.js("document.getElementById('pipeCard').hidden"))
    on = grab(ed, os.path.join(shots_dir, 'c_bury_gl.png'), True)
    ed.js("document.getElementById('tgPipes').click()")
    off = grab(ed, os.path.join(shots_dir, 'c_bury_gl_off.png'), True)
    ed.js("document.getElementById('tgPipes').click()")
    d = (np.abs(on - off).max(axis=2) > 12).sum()
    report('매설: 땅 속 관 비침 (WebGL 만 관로 켬 ↔ 끔)', d > 300, '바뀐 %d px' % d)
    errs(ed, 'c')


# ---------------------------------------------------------------- p  부분 개방
def hole_mask(cam, s0, s1, wv, g=GZ):
    """T1 첫 마디 (곧음) 위 [s0, s1] 구멍 (지표 ±wv) → 자른 그림 마스크."""
    im = Image.new('L', (cam.w, cam.h), 0)
    dr = ImageDraw.Draw(im)
    a, b = T1[0][0] + s0, T1[0][0] + s1
    pg = cam.poly([np.array([a, 3 + wv, g]), np.array([b, 3 + wv, g]), np.array([b, 3 - wv, g]), np.array([a, 3 - wv, g])])
    if pg:
        dr.polygon([(x - 0.5, y - 0.5) for x, y in pg], fill=255)
    return np.asarray(im) > 0


def face_visible(cam, X, ef, P):
    """먼 끝 자른 면 (e = ef, 안쪽 +e) 위 점 P 에서 카메라로 가는 광선이 열린 곳 안을 지나 지표 구멍에 닿는가."""
    c = cam.c
    for u in np.linspace(0.004, 1.0, 400):
        Q = P + (c - P) * u
        if Q[2] >= X['g']:
            return True
        if Q[0] < ef or abs(Q[1] - 3.0) > w_at(X, Q[2]) - 0.004:
            return False
    return True


def face_edges(img, cam, X, ef):
    """먼 끝 자른 면 층 경계: 포장 복구 / 되메우기 (z = g − tp · R − B) · 되메우기 / 보호층 (z = 관 윗면 + prot · 밝기)."""
    out = {'pav': [], 'prot': []}
    for kind, zb in (('pav', X['zt']), ('prot', X['zp'])):
        wl = w_at(X, zb) - 0.06
        for w in np.linspace(-wl, wl, 25):
            if kind == 'prot' and abs(w) < X['D'] / 2 + 0.1:
                continue
            P = np.array([ef, 3.0 + w, zb])
            if not all(face_visible(cam, X, ef, P + np.array([0, 0, dz])) for dz in (-0.06, 0.0, 0.06)):
                continue
            p0, p1 = cam.proj(P), cam.proj(P + np.array([0.0, 0.0, 0.05]))
            if p0 is None or p1 is None or not (8 < p0[0] < cam.w - 8 and 8 < p0[1] < cam.h - 8):
                continue
            sd = (p1 - p0) / np.hypot(*(p1 - p0))
            s = np.arange(-5, 5.001, 0.25)
            rgb = cv.bilinear(img, p0[None, :] + s[:, None] * sd[None, :])
            if not np.isfinite(rgb).all():
                continue
            pr = rgb[:, 0] - rgb[:, 2] if kind == 'pav' else ct.lum(rgb)
            lo, hi = np.median(pr[(s >= -3.5) & (s <= -1.5)]), np.median(pr[(s >= 1.5) & (s <= 3.5)])  # 아래 (−) · 위 (+)
            if abs(hi - lo) < 15:
                continue
            mid = (lo + hi) / 2
            best = None
            for i in range(s.size - 1):
                if (pr[i] - mid) * (pr[i + 1] - mid) <= 0 and pr[i] != pr[i + 1]:
                    x = s[i] + (s[i + 1] - s[i]) * (mid - pr[i]) / (pr[i + 1] - pr[i])
                    if best is None or abs(x) < abs(best):
                        best = x
            if best is not None:
                out[kind].append(best)
    return out


def op_state(ed):
    d = js_json(ed, "Object.assign({}, document.getElementById('opChip').dataset)")
    return {k: float(v) for k, v in d.items()}


def test_open(ed, out_dir, shots_dir, http_base=None):
    print('== p  부분 개방')
    ed.js('localStorage.clear()')
    report('불러옴 (매설 · 같은 자리)', load(ed, url(out_dir, V_OPEN)))
    inp = ce.Inp(ed)
    cc, cam = cams(ed, V_OPEN)
    A = grab(ed, os.path.join(shots_dir, 'p_before_gl.png'), True)  # 열기 전 = 매설
    ed.js("document.getElementById('mOpen').click()")
    ed.frames(4)
    st = op_state(ed)
    ui = js_json(ed, "{chip: document.getElementById('opChip').classList.contains('show'), tip: document.getElementById('opTip').textContent}")
    report('부분 개방 고름 → 바로 열림 (기본 20 m · «관 누르기» 없음)', st.get('src') is not None and abs(st.get('s1', 0) - st.get('s0', 0) - 20) < 1e-9
           and ui == {'chip': True, 'tip': ''}, json.dumps(st) + ' ' + json.dumps(ui, ensure_ascii=False))
    A1 = grab(ed, os.path.join(shots_dir, 'p_auto_gl.png'), True)
    d = (np.abs(A1 - A).max(axis=2) > 20).sum()
    report('매설 ↔ 부분 개방 그림 다름', d > 2000, '바뀐 %d px' % d)
    ed.js("(function(){ const i = document.getElementById('opLen'); i.value = '40'; i.dispatchEvent(new Event('input', {bubbles: true})); })()")
    ed.frames(4)
    st2 = op_state(ed)
    A2 = grab(ed, os.path.join(shots_dir, 'p_auto_len40_gl.png'), True)
    d = (np.abs(A2 - A1).max(axis=2) > 20).sum()
    report('길이 바 20 → 40 m → 열린 곳 바로 바뀜', st2.get('s1', 0) - st2.get('s0', 0) > 20 + 1e-6 and d > 500,
           '%.1f m · 바뀐 %d px' % (st2.get('s1', 0) - st2.get('s0', 0), d))
    ed.js("(function(){ const i = document.getElementById('opLen'); i.value = '20'; i.dispatchEvent(new Event('input', {bubbles: true})); })()")
    ed.frames(2)
    p = cc.proj([CLICK_T1[0], CLICK_T1[1], GZ])
    inp.click(p[0], p[1])
    st = op_state(ed)
    report('T1 누름 → 열림 (기본 20 m)', st.get('src') == 0 and abs(st['s1'] - st['s0'] - 20) < 1e-9, json.dumps(st))
    ed.js("(function(){ const i = document.getElementById('opLen'); i.value = '10'; i.dispatchEvent(new Event('input', {bubbles: true})); })()")
    ed.frames(4)
    st = op_state(ed)
    s = st.get('s', math.nan)
    want_s = CLICK_T1[0] - T1[0][0]
    report('측점 = 누른 자리 (5 cm 안)', abs(s - want_s) < 0.05 and abs(st['n'] - 3.0) < 1e-6, 's %.3f (누른 곳 %.1f)' % (s, want_s))
    report('길이 10 m → [s − 5, s + 5]', abs(st['s0'] - (s - 5)) < 1e-9 and abs(st['s1'] - (s + 5)) < 1e-9, '%.3f ~ %.3f' % (st['s0'], st['s1']))
    full = grab(ed, os.path.join(shots_dir, 'p_open_full.png'))
    B = grab(ed, os.path.join(shots_dir, 'p_open_gl.png'), True)
    X = sect(T1_D, GZ, GZ - ms.DEPTH - T1_D / 2, TR_DEF)
    mask = hole_mask(cam, st['s0'], st['s1'], X['wv'])
    diff = np.abs(B - A).max(axis=2) > 20
    outside = diff & ~ct.dilate(mask, 2)
    inner = ~ct.dilate(~mask, 2)
    cover = diff[inner].mean() if inner.any() else 0.0
    report('구간 밖 구덩이 화소 0', outside.sum() == 0 and diff.sum() > 2000, '밖 %d px · 바뀐 %d px · 구멍 %d px' % (outside.sum(), diff.sum(), mask.sum()))
    report('구간 안 덮임 ≥ 90 %', cover >= 0.9, '%.1f %%' % (cover * 100))
    if outside.any():
        Image.fromarray((np.stack([outside, diff & ~outside, mask], axis=2) * 255).astype(np.uint8)).save(os.path.join(shots_dir, 'p_dbg.png'))
    # 노면 띠 (청록 #00e5ff) 는 열린 곳 위에 안 그림: 겹쳐 그림에만 있는 청록 화소가 구멍 안 (6 px 좁힘) 에 거의 없음
    cyan = lambda im: (im[..., 0] < 110) & (im[..., 1] > 150) & (im[..., 2] > 180)  # noqa: E731
    inner6 = ~ct.dilate(~mask, 6)
    band = (cyan(full) & ~cyan(B))[inner6]
    report('구멍 위 노면 띠 없음', band.mean() < 0.02, '구멍 안 겹쳐 그린 청록 %.2f %%' % (band.mean() * 100))
    ef = T1[0][0] + st['s0']
    E = face_edges(B, cam, X, ef)
    for kind, lab in (('pav', '먼 자른 면: 포장 복구 / 되메우기 (g − tp)'), ('prot', '먼 자른 면: 되메우기 / 보호층 (관 위 + prot)')):
        v = np.abs(E[kind])
        report(lab, v.size >= 6 and np.median(v) <= 1.5, '표본 %d · 중앙 %.2f px · 90%% %.2f px' % (v.size, np.median(v) if v.size else -1, np.percentile(v, 90) if v.size else -1))
    # 폭 + 0.3
    ed.js("(function(){ const i = document.getElementById('opEx'); i.value = '0.3'; i.dispatchEvent(new Event('input', {bubbles: true})); })()")
    ed.frames(4)
    C = grab(ed, os.path.join(shots_dir, 'p_ex_gl.png'), True)
    X2 = sect(T1_D, GZ, GZ - ms.DEPTH - T1_D / 2, TR_DEF, ex=0.3)
    m2 = hole_mask(cam, st['s0'], st['s1'], X2['wv'])
    d2 = np.abs(C - A).max(axis=2) > 20
    ring = ~ct.dilate(~m2, 2) & ~ct.dilate(mask, 2)
    report('폭 + 0.3 m → 넓힌 구멍 (밖 0 · 늘어난 띠 덮임)', (d2 & ~ct.dilate(m2, 2)).sum() == 0 and ring.any() and d2[ring].mean() >= 0.9,
           '밖 %d px · 늘어난 띠 %.1f %%' % ((d2 & ~ct.dilate(m2, 2)).sum(), d2[ring].mean() * 100 if ring.any() else -1))
    if http_base:  # 다른 주소 = 다른 이 브라우저 저장 → 길이 · 폭을 같게 친다
        load(ed, url(out_dir, V_OPEN, 'open', base=http_base))
        p = cc.proj([CLICK_T1[0], CLICK_T1[1], GZ])
        inp.click(p[0], p[1])
        ed.js("(function(){ for (const [id, v] of [['opLen', '10'], ['opEx', '0.3']]) { const i = document.getElementById(id); i.value = v; i.dispatchEvent(new Event('input', {bubbles: true})); } })()")
        ed.frames(4)
        Hh = grab(ed, os.path.join(shots_dir, 'p_http_gl.png'), True)
        same = Hh.shape == C.shape
        dd = (np.abs(Hh - C).max(axis=2) > 3).mean() if same else 1
        report('http = file:// (WebGL)', same and dd < 0.001 and op_state(ed).get('s1', 0) - op_state(ed).get('s0', 0) == 10,
               '다른 화소 %.4f %%' % (dd * 100))
        errs(ed, 'p http')
        load(ed, url(out_dir, V_OPEN, 'open'))
        inp.click(p[0], p[1])
    ed.js("document.getElementById('opClose').click()")
    ed.frames(3)
    D0 = grab(ed, os.path.join(shots_dir, 'p_closed_gl.png'), True)
    st = js_json(ed, "{m: document.getElementById('app').dataset.mode, chip: document.getElementById('opChip').classList.contains('show')}")
    report('닫기 → 매설 · 화소 그대로', st == {'m': 'bury', 'chip': False} and np.abs(D0 - A).max() <= 3, json.dumps(st) + ' 최대 차이 %d' % np.abs(D0 - A).max())
    # 사진이 바뀌면 저절로 고른 곳은 새 자리 가운데로 다시 고름
    ed.js("document.getElementById('mOpen').click()")
    ed.frames(3)
    s_a = op_state(ed)
    ed.js("document.getElementById('next').click()")
    ed.wait_js(cv.READY, timeout=30)
    ed.frames(4)
    s_b = op_state(ed)
    report('사진 바뀜 → 저절로 고른 곳 다시 고름', s_b.get('src') is not None and (s_b.get('e'), s_b.get('n')) != (s_a.get('e'), s_a.get('n')),
           '%s → %s' % (json.dumps(s_a), json.dumps(s_b)))
    ed.js("document.getElementById('prev').click()")
    ed.wait_js(cv.READY, timeout=30)
    ed.frames(3)
    # 지도에서 관 누름 → 엶 · Esc → 매설
    mt = ce.MapTf(ed)
    q = mt.en_scr(-20.0, 3.0)
    inp.click(q[0], q[1])
    ed.frames(6)
    st = op_state(ed)
    report('지도에서 T1 누름 → 엶 (측점 20)', st.get('src') == 0 and abs(st.get('s', -1) - 20.0) < 0.3, json.dumps(st))
    inp.key('Escape', 'Escape', 27)
    report('Esc → 매설', ed.js("document.getElementById('app').dataset.mode") == 'bury')
    errs(ed, 'p')


# ---------------------------------------------------------------- s  단면
def sec_read(ed):
    return js_json(ed, "{shown: !document.getElementById('secCard').hidden, d: Object.assign({}, document.getElementById('secCard').dataset),"
                       " v: Object.fromEntries([...document.querySelectorAll('#scRows .v[id]')].map((e) => [e.id.slice(3), [Number(e.dataset.v), e.textContent]])),"
                       " sta: document.getElementById('scSta').textContent}")


def sec_check(name, st, line, tr, cover, near):
    ok = st['shown'] and st['d'].get('src') is not None
    if not report('%s 누름 → 단면 칸' % name, ok, json.dumps(st['d'])):
        return None
    e, n = float(st['d']['e']), float(st['d']['n'])
    report('%s 누른 자리 (10 cm 안)' % name, math.hypot(e - near[0], n - near[1]) < 0.1, '%.3f, %.3f' % (e, n))
    want, s = expect_sec(line, e, n, tr, cover)
    bad = []
    for k, w in want.items():
        got = st['v'].get(k)
        if got is None or abs(got[0] - w) > 1e-6 or abs(num_of(got[1]) - w) > 0.005 + 1e-9:
            bad.append((k, got, round(w, 4)))
    report('%s 숫자 11 개 = 따로 셈 (1 cm)' % name, not bad and len(st['v']) == 11,
           json.dumps(bad, ensure_ascii=False) if bad else ' · '.join('%s %s' % (k, st['v'][k][1]) for k in ('g', 'top', 'zc', 'bot', 'cover', 'depth')))
    report('%s 측점' % name, abs(float(st['d']['s']) - s) < 1e-6 and st['sta'] == '측점 %.1f m' % s, '%s (따로 %.3f)' % (st['sta'], s))
    return e, n


def yellow_line(img, cam, C, X, half):
    """360 노란 선 (그 측점 가로선 · 지표) 화소의 선에서 떨어진 거리 (부호 있는 px)."""
    nx, ny = C
    A = cam.proj(np.array([X['e'] + nx * half, X['n'] + ny * half, X['g']]))
    B = cam.proj(np.array([X['e'] - nx * half, X['n'] - ny * half, X['g']]))
    if A is None or B is None:
        return np.array([])
    d = B - A
    L = np.hypot(*d)
    u, nr = d / L, np.array([-d[1], d[0]]) / L
    ys, xs = np.mgrid[0:img.shape[0], 0:img.shape[1]]
    P = np.stack([xs + 0.5, ys + 0.5], axis=-1) - A
    al, pe = P @ u, P @ nr
    yel = (img[..., 0] > 200) & (img[..., 1] > 150) & (img[..., 2] < 90)
    sel = yel & (al > 0.1 * L) & (al < 0.9 * L) & (np.abs(pe) < 6)
    return pe[sel]


def test_sec(ed, out_dir, shots_dir):
    print('== s  단면')
    ed.js('localStorage.clear()')
    load(ed, url(out_dir, V_SEC))
    bury = grab(ed, os.path.join(shots_dir, 's_bury_full.png'))
    report('불러옴 (단면)', load(ed, url(out_dir, V_SEC, 'sec')))
    inp = ce.Inp(ed)
    cc, cam = cams(ed, V_SEC)
    st = sec_read(ed)
    auto = grab(ed, os.path.join(shots_dir, 's_auto_full.png'))
    d = (np.abs(auto - bury).max(axis=2) > 20).sum()
    report('단면 고름 → 바로 단면 칸 (가까운 관) · 매설과 다른 그림', st['shown'] and st['d'].get('src') is not None and d > 1500,
           '%s · 바뀐 %d px' % (json.dumps(st['d']), d))
    g4 = U4['g'][1] + (U4['g'][2] - U4['g'][1]) * 0.5
    p = cc.proj([CLICK_U4[0], CLICK_U4[1], g4])
    inp.click(p[0], p[1])
    ed.frames(3)
    st = sec_read(ed)
    full = grab(ed, os.path.join(shots_dir, 's_u4_full.png'))
    en = sec_check('U4', st, 'U4', TR_DEF, ms.DEPTH, CLICK_U4)
    if en:
        e, n = en
        dx, dy = U4['v'][2][0] - U4['v'][1][0], U4['v'][2][1] - U4['v'][1][1]
        l = math.hypot(dx, dy)
        want, _ = expect_sec('U4', e, n, TR_DEF, ms.DEPTH)
        X = sect(U4['dia'], want['g'], want['zc'], TR_DEF)
        pe = yellow_line(full, cam, (-dy / l, dx / l), dict(e=e, n=n, g=want['g']), X['wv'] + 0.6)
        report('360 노란 선 ↔ 측점 가로선 투영', pe.size >= 30 and abs(np.median(pe)) <= 1.5, '화소 %d · 가운데 %+.2f px' % (pe.size, np.median(pe) if pe.size else 0))
    ct.set_cfg(ed, CFG)
    ed.frames(3)
    st = sec_read(ed)
    sec_check('U4 (설정 바꿈)', st, 'U4', CFG, ms.DEPTH, CLICK_U4)
    ed.js("document.getElementById('secClose').click()")
    ed.js('localStorage.clear()')
    load(ed, url(out_dir, V_BURY, 'sec'))
    cc, _ = cams(ed, V_BURY)
    p = cc.proj([CLICK_T1[0], CLICK_T1[1], GZ])
    inp.click(p[0], p[1])
    ed.frames(3)
    sec_check('T1 (평균매설깊이)', sec_read(ed), 'T1', TR_DEF, ms.DEPTH, CLICK_T1)
    tag = ed.js("document.getElementById('scTag').textContent")
    report('칸 표시: 가시화 기본값 · 관 표고 = 평균매설깊이', '가시화 기본값' in tag and '평균매설깊이' in tag, tag)
    ed.screenshot(os.path.join(shots_dir, 's_t1.png'))
    inp.key('Escape', 'Escape', 27)
    st = js_json(ed, "{card: document.getElementById('secCard').hidden, m: document.getElementById('app').dataset.mode, tip: document.getElementById('opChip').classList.contains('show')}")
    report('Esc → 단면 칸 닫힘 (단면 방식 그대로 · «관 누르기»)', st == {'card': True, 'm': 'sec', 'tip': True}, json.dumps(st))
    # 관 정보 칸 [단면] → 그 자리 단면
    ed.js("document.getElementById('mSurf').click()")
    inp.click(p[0], p[1])
    ed.js("document.getElementById('pcSec').click()")
    ed.frames(3)
    st = sec_read(ed)
    report('관 정보 [단면] → 단면 방식 · 같은 자리', st['shown'] and ed.js("document.getElementById('app').dataset.mode") == 'sec'
           and abs(float(st['d'].get('e', 0)) - CLICK_T1[0]) < 0.1, json.dumps(st['d']))
    errs(ed, 's')


# ---------------------------------------------------------------- l  층 표시 (터파기)
def test_layers(ed, out_dir, shots_dir):
    print('== l  층 표시 (터파기 방식)')
    ed.js('localStorage.clear()')
    report('불러옴 (터파기)', load(ed, url(out_dir, V_PIT, 'trench')))
    _, cam = cams(ed, V_PIT)
    mask = ct.opening_mask(cam, ct.all_sections(TR_DEF))
    imgs = [grab(ed, os.path.join(shots_dir, 'l0_default_gl.png'), True)]
    steps = (('tgBed', '기초 · 보호 끔'), ('tgFill', '되메우기 켬'), ('tgBed', '기초 · 보호 켬'))
    for i, (bid, lab) in enumerate(steps):
        ed.js("document.getElementById('%s').click()" % bid)
        ed.frames(3)
        imgs.append(grab(ed, os.path.join(shots_dir, 'l%d_gl.png' % (i + 1)), True))
        d = np.abs(imgs[-1] - imgs[-2]).max(axis=2) > 6
        out = d & ~ct.dilate(mask, 2)
        report('%s: 바뀐 화소 = 구멍 안만' % lab, out.sum() == 0 and d.sum() > 1500, '밖 %d px · 바뀐 %d px' % (out.sum(), d.sum()))
    st = js_json(ed, "JSON.parse(localStorage.getItem('rv_ui:show'))")
    report('표시 항목 이 브라우저 기억', st == {'bed': True, 'fill': True, 'lbl': True}, json.dumps(st))
    ed.js('localStorage.clear()')
    errs(ed, 'l')


# ---------------------------------------------------------------- z  수치지형도 프로젝트 → 높이 (dz) 잠금
def test_dem(ed, out_dir, shots_dir):
    print('== z  수치지형도 프로젝트: 맞춤 높이 잠금')
    pj, cj = os.path.join(out_dir, 'data', 'project.js'), os.path.join(out_dir, 'data', 'calib.js')
    orig = open(pj, encoding='utf-8').read()
    P = json.loads(orig[orig.index('{'):orig.rindex('}') + 1])
    s0 = P['shots'][0]
    vid, cam = s0['video'], np.array([s0['e'], s0['n'], s0['z']])
    # 짝 4 개: 사진 방향 = 땅 점 X 를 보는 방향 · 지도 점 = X 에서 (1.0, −0.5) m 민 곳 → 풀면 동 · 북이 바뀜 (높이는 잠금이면 0)
    pairs = []
    for de, dn in ((6, 0), (0, 6), (-6, 0), (0, -6)):
        X = np.array([s0['e'] + de, s0['n'] + dn, s0['groundZ']])
        p = (X - cam) / np.linalg.norm(X - cam)
        pairs.append({'shot': s0['id'], 'p': [round(float(v), 7) for v in p], 'e': round(float(X[0] + 1.0), 3), 'n': round(float(X[1] - 0.5), 3)})
    cal = {'videos': {vid: {'dx': 0, 'dy': 0, 'dz': 0.8, 'dyaw': 0, 'dpitch': 0, 'droll': 0, 'pairs': pairs}}}

    def exported():
        t = ed.js(EXPORT_JS, await_promise=True) or ''
        try:
            return json.loads(t[t.index('{'):t.rindex('}') + 1])['videos'][vid]
        except (ValueError, KeyError):
            return {}

    def cal_open():
        ed.js("if (!document.getElementById('calPane').classList.contains('open')) document.getElementById('calHead').click()")
        ed.frames(3)
    try:
        P2 = dict(P, terrain='사내 수치지도 DB 등고선 3개')
        with open(pj, 'w', encoding='utf-8', newline='\n') as f:
            f.write('window.RV_PROJECT = ' + json.dumps(P2, ensure_ascii=False) + ';\n')
        with open(cj, 'w', encoding='utf-8', newline='\n') as f:
            f.write('window.RV_CALIB = ' + json.dumps(cal) + ';\n')
        ed.js('localStorage.clear()')
        report('불러옴 (수치지형도 프로젝트)', load(ed, url(out_dir, V_BURY)))
        cal_open()
        st = js_json(ed, "{t: (document.getElementById('calDzLock') || {}).textContent || '', w: (document.getElementById('calDzLock') || {offsetWidth: 0}).offsetWidth,"
                         " n: document.querySelectorAll('#calRows input').length}")
        ed.screenshot(os.path.join(shots_dir, 'z_dem_cal.png'))
        report('높이 = «수치지형도로 맞춰짐» (칸 없음)', st['t'] == '수치지형도로 맞춰짐' and st['w'] > 0 and st['n'] == 5, json.dumps(st, ensure_ascii=False))
        c0 = exported()
        report('파일 dz 0.8 → 0 (수치지형도)', c0.get('dz') == 0, json.dumps({k: c0.get(k) for k in ('dx', 'dy', 'dz', 'dyaw')}))
        ed.js("document.getElementById('alGo').click()")
        ed.frames(3)
        ce_ = exported()
        al_on = ed.js("document.getElementById('alChip').classList.contains('show')")
        report('점 찍어 맞추기 켬 → 있던 짝 4 개로 한 번 다시 맞춤 (dz 0)', al_on and ce_.get('dz') == 0 and abs(ce_.get('dx', 0) - 1.0) < 0.1 and abs(ce_.get('dy', 0) + 0.5) < 0.1,
               json.dumps({k: ce_.get(k) for k in ('dx', 'dy', 'dz', 'dyaw')}))
        ed.js("document.getElementById('alGo').click()")
        ed.frames(2)
        ed.js("document.querySelector('#alList .alRow .alDel').click()")
        ed.frames(4)
        c1 = exported()
        report('짝 3 개로 풂 → dz 그대로 0 · 동 · 북 풀림', c1.get('dz') == 0 and abs(c1.get('dx', 0) - 1.0) < 0.2 and abs(c1.get('dy', 0) + 0.5) < 0.2
               and len(c1.get('pairs', [])) == 3, json.dumps({k: c1.get(k) for k in ('dx', 'dy', 'dz', 'dyaw')}) + ' · 짝 %d' % len(c1.get('pairs', [])))
        errs(ed, 'z')
    finally:
        with open(pj, 'w', encoding='utf-8', newline='\n') as f:
            f.write(orig)
        if os.path.exists(cj):
            os.remove(cj)
        ed.js('localStorage.clear()')
    load(ed, url(out_dir, V_BURY))
    cal_open()
    st = js_json(ed, "{lock: !!document.getElementById('calDzLock'), n: document.querySelectorAll('#calRows input').length}")
    report('수치지도 아닌 프로젝트 → 높이 칸 그대로', st == {'lock': False, 'n': 6}, json.dumps(st))
    ed.js('localStorage.clear()')
    errs(ed, 'z2')


# ---------------------------------------------------------------- h  프로그램 (hostmock)
def test_host(ed, out_dir, shots_dir):
    print('== h  프로그램 (hostmock)')
    page = ci.prepare(out_dir, host=True)
    try:
        ed.js('localStorage.clear()')
        u = pathlib.Path(out_dir, page).resolve().as_uri() + '?t=none&' + ce.q_of(V_OPEN, '&mode=open')
        report('불러옴 (프로그램)', load(ed, u))
        ed.frames(4)
        st = js_json(ed, "{tools: !document.getElementById('hostTools').hidden, icons: ['hPipes','hObst','hExport','hHome'].map((i) => !!document.getElementById(i).querySelector('svg') && document.getElementById(i).getBoundingClientRect().width > 20),"
                         " tips: ['hPipes','hObst','hExport','hHome','tgEdit','tgQty','tgCal','tgMap'].map((i) => document.getElementById(i).title.length > 0)}")
        report('그림 단추 (넣기 · 내보내기 · 처음) + 짧은 설명', st['tools'] and all(st['icons']) and all(st['tips']), json.dumps(st))
        cc, _ = cams(ed, V_OPEN)
        p = cc.proj([CLICK_T1[0], CLICK_T1[1], GZ])
        ce.Inp(ed).click(p[0], p[1])
        report('프로그램 안 부분 개방', op_state(ed).get('src') == 0)
        ct.set_cfg(ed, {'prot': 0.45})
        time.sleep(1.4)
        calls = js_json(ed, "window.__host.calls.filter((c) => c.name === 'save_calib').map((c) => c.arg)")
        tr = None
        if calls:
            t = calls[-1]
            tr = json.loads(t[t.index('{'):t.rindex('}') + 1]).get('trench')
        report('보호층 0.45 → save_calib (0.8 초 모아) 에 prot', len(calls) == 1 and tr == dict(TR_DEF, prot=0.45, on=True), '%d 번 · %s' % (len(calls), json.dumps(tr)))
        ed.screenshot(os.path.join(shots_dir, 'h_host.png'))
        errs(ed, 'h')
    finally:
        ci.cleanup(out_dir)
        ed.js('localStorage.clear()')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--shots', default=None)
    ap.add_argument('--only', default='', help='m,c,p,s,l,h,z 중 일부만')
    args = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    out_dir = os.path.abspath(args.out)
    shots_dir = os.path.abspath(args.shots or os.path.join(out_dir, '_shots_under'))
    os.makedirs(shots_dir, exist_ok=True)
    cj = os.path.join(out_dir, 'data', 'calib.js')
    if os.path.exists(cj):
        os.remove(cj)
    add_u4(out_dir)
    only = set(args.only.split(',')) if args.only else set('mcpslhz')
    srv = http.server.ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(ci._Quiet, directory=out_dir))
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    base = 'http://127.0.0.1:%d/%s' % (srv.server_address[1], urllib.parse.quote('로드뷰.html'))
    ed = Edge(os.path.join(shots_dir, '_prof_u'), cv.WIN).__enter__()
    try:
        ed.net(block=ci.BLOCK)
        load(ed, url(out_dir, V_BURY))
        if 'm' in only:
            test_modes(ed, out_dir, shots_dir)
        if 'c' in only:
            test_card(ed, out_dir, shots_dir)
        if 'p' in only:
            test_open(ed, out_dir, shots_dir, base)
        if 's' in only:
            test_sec(ed, out_dir, shots_dir)
        if 'l' in only:
            test_layers(ed, out_dir, shots_dir)
        if 'h' in only:
            test_host(ed, out_dir, shots_dir)
        if 'z' in only:
            test_dem(ed, out_dir, shots_dir)
    finally:
        ed.__exit__(None, None, None)
        srv.shutdown()
    for where, e in R['errs']:
        print('   오류 [%s]: %s' % (where, e))
    R['ok'] &= not R['errs']
    print('사진:', shots_dir)
    print('전체:', '통과' if R['ok'] else '실패')
    return 0 if R['ok'] else 1


if __name__ == '__main__':
    sys.exit(main())
