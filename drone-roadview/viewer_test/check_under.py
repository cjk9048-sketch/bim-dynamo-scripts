"""지하 시설물 (표현 둘 · 영역 테두리 · 표시 항목 · [단면] · 관 정보 · 관종) 시험: make_synth --trench 폴더에 땅 높이 · 관 표고가
바뀌는 관 U4 (hasZ) 를 더해 화면 없는 Edge (cdp.py, 실제 시간 · 진짜 마우스) 로 찍고, 보기 화면 수식과 따로 짠 계산으로 잰다.

    python -I check_under.py --out <make_synth --trench 폴더> [--shots <폴더>]

  m  표현 둘 (터파기 영역 · 터파기 · JACK 1124): 처음 = 영역 · 단추 하나만 켜짐 · 주소 ?mode= (area · bury → 영역 · trench · depth · open · sec → 터파기) ·
     표시 항목은 터파기에서만 (영역 = 범례 임시점용 · 영구점용) · 표시 항목 · 범례 이 브라우저 기억 (관로 본체는 안 기억) ·
     [단면] = 터파기 설정 창 (다시 누르면 닫힘) · «가시화 기본값» ↔ «직접 입력» · calib.js trench 에 prot (옛 파일 = 기본값) ·
     맞춤 칸 안내 «시점 · 종점 포함 2곳 이상 찍기» · 옛 층 단추 (기초·보호 · 되메우기) 없음 · 옛 기억을 넣어도 화소 그대로
  a  영역: 임시점용 주황 점선 = 지표 ±wv 투영 (중앙 ≤ 1.5 px) · 영구점용 띠 (관경 폭) 가 그 안 · 범례 끄면 점선 없음 ·
     터파기 표현엔 점선 없음 · 지도에도 주황 점선 (영역만) · 수량 칸 임시점용 = 포장 깨기 (∫ 2·wv) · 영구점용 = Σ 관경 × 길이 (따로 셈)
  c  관 정보: T1 누름 → 관경 · 토피 · 관 중심고 · 지반고 = 따로 셈 · 관종 = 짐작 (주철) · [강관] → RV_PIPES mat · 이 브라우저 저장 ·
     [이 자리 단면] → 설정 창이 그 자리 단면 (측점 · 폭 글) · Esc 닫기 · 빈 땅 누름 → 닫힘
  h  프로그램 (hostmock): 그림 단추 · save_calib 에 prot
  z  수치지형도 프로젝트 (project.terrain 에 «수치지도»): 맞춤 «높이» = «수치지형도로 맞춰짐» · 파일 dz 무시 (0) ·
     [점 찍어 맞추기] 켜면 있던 짝으로 한 번 다시 맞춤 · 점 짝 3 개로 풀어도 dz 0 (동 · 북은 풀림) · 수치지도 아닌 프로젝트는 높이 칸 그대로
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
from PIL import Image

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
T1 = [(-40.0, 3.0), (14.0, 3.0), (40.0, 16.0)]                 # make_synth TR_LINES[0] (D300 · 평균매설깊이)
T1_D = 0.3
# 땅 높이 · 관 중심 표고가 꼭짓점마다 다른 관 (hasZ)
U4 = {'layer': 1, 'label': 'U4', 'dia': 0.4, 'v': [(-22.0, -20.0), (-2.0, -26.0), (12.0, -22.0)],
      'g': [50.0, 52.0, 49.6], 'z': [48.0, 49.5, 47.6]}
V_AREA = ('a', '0001', 285, -50, 40)          # T1 서쪽 (e −17 ~ −8)
V_PIT = ('v1_down_n', '0002', 0, -60, 70)     # check_trench 와 같은 자리 (T1 · T3 구덩이)
CLICK_T1 = (-8.0, 3.0)
R = {'ok': True, 'errs': []}
EXPORT_JS = ct.EXPORT_JS


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


def grab(ed, png, gl_only=False, eid='ov'):
    """360 칸만 (CSS 자리로 자름) · gl_only = WebGL 만 (겹쳐 그림 · 안내 줄 · 칸 · 단추 숨김) · eid = 'map' 이면 지도 칸."""
    vis = lambda v: ed.js("document.querySelectorAll('#viewPane > :not(#gl)').forEach((e) => { e.style.visibility = '%s'; })" % v)  # noqa: E731
    if gl_only:
        vis('hidden')
    ed.frames(3)
    ed.screenshot(png)
    if gl_only:
        vis('')
    r = ce.rect(ed, eid)
    a = np.asarray(Image.open(png).convert('RGB')).astype(float)
    l, t = int(round(r['left'])), int(round(r['top']))
    return a[t:t + int(round(r['height'])), l:l + int(round(r['width']))]


def cams(ed, view):
    """진짜 마우스용 (client) · 자른 그림용 (0,0 = 360 왼쪽 위)."""
    c = ce.Cam(ed, view)
    return c, ct.Cam(view, c.w, c.h)


# ---------------------------------------------------------------- 따로 짠 단면 계산 (보기 화면 수식과 따로)
def sect(D, g, zc, tr, n=None):
    n = tr['n'] if n is None else n
    zs = min(zc - D / 2, g - 0.01)
    zb = zs - tr['bed']
    wb = D / 2 + tr['margin']
    zt = g - tr['tp']
    wv = wb + n * max(0.0, zt - zb)
    top = zc + D / 2
    return dict(zs=zs, zb=zb, wb=wb, zt=zt, wv=wv, top=top, g=g, zc=zc, D=D, n=n)


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


def orange_mask(img):
    return (img[..., 0] > 200) & (img[..., 1] > 125) & (img[..., 1] < 175) & (img[..., 2] < 90)  # AREA_TMP (255, 154, 31) · 노란 보기 부채꼴 (G 186 ~) 은 빼고


# ---------------------------------------------------------------- m  표현 둘 · 표시 항목 · 범례 · [단면] · 안내
def test_modes(ed, out_dir, shots_dir):
    print('== m  표현 둘 · 표시 항목 · 범례 · [단면] · 안내')
    ed.js('localStorage.clear()')
    report('불러옴 (처음)', load(ed, url(out_dir, V_AREA)))
    st_of = lambda: js_json(ed, "{m: document.getElementById('app').dataset.mode, on: ['mSurf','mDepth'].filter((i) => document.getElementById(i).classList.contains('on')),"  # noqa: E731
                                " show: !document.getElementById('ugShow').hidden && document.getElementById('ugShow').offsetHeight > 0,"
                                " legend: !document.getElementById('ugLegend').hidden && document.getElementById('ugLegend').offsetHeight > 0,"
                                " n: document.querySelectorAll('#ugMode button').length, old: ['mOpen', 'secCard', 'opChip', 'tgBed', 'tgFill'].filter((i) => !!document.getElementById(i))}")
    st = st_of()
    report('처음 = 터파기 영역 (단추 하나 · 범례 · 표시 항목 숨김)', st == {'m': 'area', 'on': ['mSurf'], 'show': False, 'legend': True, 'n': 3, 'old': []}, json.dumps(st))
    ed.js("document.getElementById('mDepth').click()")
    ed.frames(2)
    st = st_of()
    report('[터파기] → 표시 항목 보임 · 범례 숨김', st['m'] == 'trench' and st['on'] == ['mDepth'] and st['show'] and not st['legend'], json.dumps(st))
    ed.js("document.getElementById('mSurf').click()")
    ed.frames(2)
    report('[터파기 영역] → 영역', st_of()['m'] == 'area')
    for q, m in (('area', 'area'), ('bury', 'area'), ('trench', 'trench'), ('depth', 'trench'), ('open', 'trench'), ('sec', 'trench'), ('xx', 'area')):
        load(ed, url(out_dir, V_AREA, q))
        report('?mode=%s → %s' % (q, m), st_of()['m'] == m)
    # [단면] = 설정 창
    inp = ce.Inp(ed)
    inp.el_click('tgSec')
    ed.frames(2)
    d1 = js_json(ed, "{dlg: !document.getElementById('secDlg').hidden, on: document.getElementById('tgSec').classList.contains('on'), lab: document.getElementById('tgSec').getAttribute('aria-label')}")
    inp.el_click('tgSec')
    ed.frames(2)
    d2 = ed.js("document.getElementById('secDlg').hidden")
    report('[단면] → 터파기 설정 창 · 다시 → 닫힘', d1 == {'dlg': True, 'on': True, 'lab': '단면'} and d2, json.dumps(d1, ensure_ascii=False))
    # 표시 항목 · 범례 기억 (관로 본체는 안 기억)
    ed.js('localStorage.clear()')
    load(ed, url(out_dir, V_AREA, 'trench'))
    tg = lambda: js_json(ed, "['tgPipes','tgPit','tgLbl','lgTmp','lgPerm'].map((i) => document.getElementById(i).classList.contains('on'))")  # noqa: E731
    report('표시 처음 (관로 · 터파기 · 이름표 · 임시 · 영구 켬)', tg() == [True, True, True, True, True], json.dumps(tg()))
    for i in ('tgPipes', 'tgLbl'):
        ed.js("document.getElementById('%s').click()" % i)
    ed.js("document.getElementById('mSurf').click()")
    ed.js("document.getElementById('lgTmp').click()")
    load(ed, url(out_dir, V_AREA, 'trench'))
    report('다시 열기 → 표시 · 범례 기억 (관로 본체는 켬)', tg() == [True, True, False, False, True], json.dumps(tg()))
    ed.js('localStorage.clear()')
    # 옛 층 표시 기억 (기초·보호 · 되메우기) 을 넣어도 터파기 그림 그대로
    load(ed, url(out_dir, V_PIT, 'trench'))
    a = grab(ed, os.path.join(shots_dir, 'm_pit_gl.png'), True)
    ed.js("localStorage.setItem('rv_ui:show', JSON.stringify({bed: false, fill: true, lbl: true}))")
    load(ed, url(out_dir, V_PIT, 'trench'))
    b = grab(ed, os.path.join(shots_dir, 'm_pit_oldshow_gl.png'), True)
    report('옛 층 기억 → 터파기 화소 그대로', a.shape == b.shape and np.abs(a - b).max() <= 3, '최대 차이 %d' % np.abs(a - b).max())
    ed.js('localStorage.clear()')
    # 맞춤 칸 안내
    load(ed, url(out_dir, V_AREA))
    ed.js("if (!document.getElementById('calPane').classList.contains('open')) document.getElementById('calHead').click()")
    ed.frames(2)
    hint = js_json(ed, "{t: document.getElementById('alHint').textContent, w: document.getElementById('alHint').offsetWidth}")
    report('맞춤 안내 «시점 · 종점 포함 2곳 이상 찍기»', hint['t'] == '시점 · 종점 포함 2곳 이상 찍기' and hint['w'] > 0, json.dumps(hint, ensure_ascii=False))
    # 가시화 기본값 ↔ 직접 입력
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
        load(ed, url(out_dir, V_AREA))
        tr, _ = ct.exported_trench(ed)
        report('옛 trench (prot 없음) → 보호층 기본 0.3', tr == dict(tp=0.25, n=0.4, margin=0.35, bed=0.15, prot=0.3, on=True), json.dumps(tr))
    finally:
        os.remove(cj)
    ed.js('localStorage.clear()')
    errs(ed, 'm')


# ---------------------------------------------------------------- a  영역 테두리 · 지도 · 수량
def outline_offsets(img, cam, wv, es, n0=3.0):
    """T1 첫 마디 (n = n0) 위 e 마다 ±wv 투영 둘레 ±8 px 의 주황 화소 무게중심 − 투영 (px)."""
    O = orange_mask(img)
    out = []
    for e in es:
        for sg in (1, -1):
            P = np.array([e, n0 + sg * wv, GZ])
            p0, sd = ct.screen_dir(cam, P, P + np.array([0.3, 0.0, 0.0]))
            if p0 is None or not (12 < p0[0] < cam.w - 12 and 12 < p0[1] < cam.h - 12):
                continue
            nrm = np.array([-sd[1], sd[0]])
            s = np.arange(-8, 8.001, 0.5)
            pts = p0[None, :] + s[:, None] * nrm[None, :]
            xi, yi = np.floor(pts[:, 0]).astype(int), np.floor(pts[:, 1]).astype(int)
            ok = (xi >= 0) & (yi >= 0) & (xi < cam.w) & (yi < cam.h)
            hit = np.zeros(s.size, bool)
            hit[ok] = O[yi[ok], xi[ok]]
            if hit.sum() >= 2:
                out.append(float(s[hit].mean()))
    return np.array(out)


def test_area(ed, out_dir, shots_dir):
    print('== a  영역: 임시점용 · 영구점용 · 지도 · 수량')
    ed.js('localStorage.clear()')
    load(ed, url(out_dir, V_AREA))
    _, cam = cams(ed, V_AREA)
    full = grab(ed, os.path.join(shots_dir, 'a_area.png'))
    X = sect(T1_D, GZ, GZ - ms.DEPTH - T1_D / 2, TR_DEF)
    es = np.arange(-20.0, -6.0, 0.25)
    off = outline_offsets(full, cam, X['wv'], es)
    report('임시점용 주황 점선 = 지표 ±wv 투영', off.size >= 20 and np.median(np.abs(off)) <= 1.5,
           '표본 %d · 중앙 %.2f px (wv %.3f m)' % (off.size, np.median(np.abs(off)) if off.size else -1, X['wv']))
    inside = outline_offsets(full, cam, T1_D / 2 + 0.05, es)
    report('영구점용 띠 안에는 주황 점선 없음', inside.size <= 2, '표본 %d' % inside.size)
    # 영구점용 띠 = 관로 색 (관경 폭): 가운데 화소가 청록
    C = cv.bilinear(full, np.array([cam.proj(np.array([e, 3.0, GZ])) for e in es if cam.proj(np.array([e, 3.0, GZ])) is not None]))
    cyan = ((C[:, 0] < 110) & (C[:, 1] > 150) & (C[:, 2] > 170)).mean()
    report('영구점용 띠 (관 가운데 = 관로 색)', cyan > 0.8, '청록 %.0f %%' % (cyan * 100))
    ed.js("document.getElementById('lgTmp').click()")
    ed.frames(3)
    f2 = grab(ed, os.path.join(shots_dir, 'a_area_tmp_off.png'))
    report('범례 [임시점용] 끔 → 주황 점선 없음', outline_offsets(f2, cam, X['wv'], es).size <= 2 and orange_mask(f2).sum() < 50, '주황 %d px' % orange_mask(f2).sum())
    ed.js("document.getElementById('lgTmp').click()")
    ed.js("document.getElementById('mDepth').click()")
    ed.frames(3)
    f3 = grab(ed, os.path.join(shots_dir, 'a_trench.png'))
    report('터파기 표현엔 주황 점선 없음', outline_offsets(f3, cam, X['wv'], es).size <= 2, '')
    m3 = grab(ed, os.path.join(shots_dir, 'a_map_trench.png'), eid='map')
    ed.js("document.getElementById('mSurf').click()")
    ed.frames(3)
    m1 = grab(ed, os.path.join(shots_dir, 'a_map_area.png'), eid='map')
    report('지도: 영역에서만 주황 점선', orange_mask(m1).sum() > 60 and orange_mask(m3).sum() < 10, '영역 %d px · 터파기 %d px' % (orange_mask(m1).sum(), orange_mask(m3).sum()))
    # 수량: 임시점용 = 포장 깨기 · 영구점용 = Σ 관경 × 길이
    ed.js("document.getElementById('tgQty').click()")
    ed.frames(3)
    q = js_json(ed, "Object.fromEntries(['qt_pav','qt_tmp','qt_perm','qt_len'].map((i) => [i, Number(document.getElementById(i).dataset.v)]))")
    perm = sum(d * sum(math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(v[:-1], v[1:])) for v, d, _, _, _ in ms.TR_LINES)
    perm += U4['dia'] * sum(math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(U4['v'][:-1], U4['v'][1:]))
    report('수량: 임시점용 = 포장 깨기 · 영구점용 = Σ 관경 × 길이', abs(q['qt_tmp'] - q['qt_pav']) < 1e-9 and abs(q['qt_perm'] - perm) < 1e-6,
           '임시 %.3f m² · 영구 %.3f m² (따로 %.3f)' % (q['qt_tmp'], q['qt_perm'], perm))
    ed.js("document.getElementById('tgQty').click()")
    ed.js('localStorage.clear()')
    errs(ed, 'a')


# ---------------------------------------------------------------- c  관 정보 · 관종 · 그 자리 단면
def test_card(ed, out_dir, shots_dir):
    print('== c  관 정보 · 관종 · 그 자리 단면')
    ed.js('localStorage.clear()')
    load(ed, url(out_dir, V_AREA))
    inp = ce.Inp(ed)
    cc, _ = cams(ed, V_AREA)
    p = cc.proj([CLICK_T1[0], CLICK_T1[1], GZ])
    inp.click(p[0], p[1])
    st = js_json(ed, "{shown: !document.getElementById('pipeCard').hidden, d: Object.assign({}, document.getElementById('pipeCard').dataset),"
                     " v: Object.fromEntries([...document.querySelectorAll('#pcRows .v[id]')].map((e) => [e.id, [e.dataset.v, e.textContent]]))}")
    ed.screenshot(os.path.join(shots_dir, 'c_card.png'))
    ok = st['shown'] and st['d'].get('src') == '0'
    report('T1 누름 → 관 정보', ok, json.dumps(st['d']))
    if ok:
        e, n = float(st['d']['e']), float(st['d']['n'])
        report('누른 자리 (관 위 · 5 cm 안)', abs(n - 3.0) < 1e-6 and abs(e - CLICK_T1[0]) < 0.05, '%.3f, %.3f' % (e, n))
        g, zc = GZ, GZ - ms.DEPTH - T1_D / 2
        want = {'pc_dia': (T1_D, 'D300'), 'pc_cover': (g - zc - T1_D / 2, '1.00 m'), 'pc_zc': (zc, 'EL 48.85'), 'pc_g': (g, 'EL 50.00')}
        bad = [(k, st['v'].get(k), w) for k, w in want.items() if k not in st['v'] or abs(float(st['v'][k][0]) - w[0]) > 1e-6 or st['v'][k][1] != w[1]]
        report('관경 · 토피 · 관 중심고 · 지반고 = 따로 셈', not bad, json.dumps(bad, ensure_ascii=False) if bad else 'D300 · 1.00 m · EL 48.85 · EL 50.00')
        report('관종 = 짐작 (이름 T1 · 층 상수관 → 주철)', st['v'].get('pc_mat', [''])[0] == 'ci', json.dumps(st['v'].get('pc_mat'), ensure_ascii=False))
    inp.click(*aria(ed, '강관'))
    ed.frames(2)
    m = js_json(ed, "{raw: window.RV_PIPES.lines[0].mat, on: [...document.querySelectorAll('#pc_mat button.on')].map((b) => b.dataset.m),"
                    " st: (JSON.parse(localStorage.getItem('rv_pipes:' + window.RV_PROJECT.title) || '{}').lines || {})['0']}")
    report('[강관] → mat steel · 단추 · 이 브라우저 저장', m['raw'] == 'steel' and m['on'] == ['steel'] and (m['st'] or {}).get('mat') == 'steel', json.dumps({'raw': m['raw'], 'on': m['on']}))
    inp.click(*aria(ed, '이 자리 단면'))
    ed.frames(3)
    X = sect(T1_D, GZ, GZ - ms.DEPTH - T1_D / 2, TR_DEF)
    s_want = on_line(T1, CLICK_T1[0], CLICK_T1[1])[3]
    d = js_json(ed, "{dlg: !document.getElementById('secDlg').hidden, at: document.getElementById('secDlg').dataset.at, svg: document.getElementById('secSvg').innerHTML}")
    report('[이 자리 단면] → 설정 창 = 그 자리 단면 (측점 · 폭 · 지반 EL)', d['dlg'] and abs(float(d['at'] or -1) - s_want) < 0.06
           and ('폭 %.2f' % (2 * X['wv'])) in d['svg'] and '50.00' in d['svg'], '측점 %s (따로 %.2f) · 폭 %.2f' % (d['at'], s_want, 2 * X['wv']))
    ed.js("document.getElementById('secClose').click()")
    inp.click(p[0], p[1])
    inp.key('Escape', 'Escape', 27)
    report('Esc → 닫힘', ed.js("document.getElementById('pipeCard').hidden"))
    q = cc.proj([CLICK_T1[0], CLICK_T1[1] + 4.0, GZ])  # 관 없는 땅
    inp.click(p[0], p[1])
    inp.click(q[0], q[1])
    report('빈 땅 누름 → 닫힘', ed.js("document.getElementById('pipeCard').hidden"))
    ed.js('localStorage.clear()')
    errs(ed, 'c')


def aria(ed, label):
    r = js_json(ed, "document.querySelector('[aria-label=\"%s\"]').getBoundingClientRect()" % label)
    return r['left'] + r['width'] / 2, r['top'] + r['height'] / 2


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
        report('불러옴 (수치지형도 프로젝트)', load(ed, url(out_dir, V_AREA)))
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
    load(ed, url(out_dir, V_AREA))
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
        u = pathlib.Path(out_dir, page).resolve().as_uri() + '?t=none&' + ce.q_of(V_AREA)
        report('불러옴 (프로그램)', load(ed, u))
        ed.frames(4)
        st = js_json(ed, "{tools: !document.getElementById('hostTools').hidden, icons: ['hPipes','hObst','hExport','hHome'].map((i) => !!document.getElementById(i).querySelector('svg') && document.getElementById(i).getBoundingClientRect().width > 20),"
                         " tips: ['hPipes','hObst','hExport','hHome','tgEdit','tgQty','tgCal','tgMap'].map((i) => document.getElementById(i).title.length > 0),"
                         " logo: (function(){ const i = document.getElementById('dohwa'); const r = i.getBoundingClientRect(); return [i.alt, i.naturalWidth, Math.round(r.height), r.left < 40, getComputedStyle(i).pointerEvents]; })(),"
                         " top: document.getElementById('top').getBoundingClientRect().height}")
        report('그림 단추 (넣기 · 내보내기 · 처음) + 짧은 설명', st['tools'] and all(st['icons']) and all(st['tips']), json.dumps({k: st[k] for k in ('tools', 'icons', 'tips')}))
        report('DOHWA 로고 (왼쪽 위 · 18~20 px · 안 누름) · 윗줄 48 px', st['logo'][0] == 'DOHWA' and st['logo'][1] == 240 and 18 <= st['logo'][2] <= 20 and st['logo'][3]
               and st['logo'][4] == 'none' and st['top'] == 48, json.dumps(st['logo']) + ' · 윗줄 %s' % st['top'])
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
    ap.add_argument('--only', default='', help='m,a,c,h,z 중 일부만')
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
    only = set(args.only.split(',')) if args.only else set('machz')
    ed = Edge(os.path.join(shots_dir, '_prof_u'), cv.WIN).__enter__()
    try:
        ed.net(block=ci.BLOCK)
        load(ed, url(out_dir, V_AREA))
        if 'm' in only:
            test_modes(ed, out_dir, shots_dir)
        if 'a' in only:
            test_area(ed, out_dir, shots_dir)
        if 'c' in only:
            test_card(ed, out_dir, shots_dir)
        if 'h' in only:
            test_host(ed, out_dir, shots_dir)
        if 'z' in only:
            test_dem(ed, out_dir, shots_dir)
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
