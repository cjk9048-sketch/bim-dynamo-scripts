"""지장물 · 수량 · 간섭 시험: make_synth --trench 폴더에 시험 지장물 (data/obstacles.js) · 땅 높이가 바뀌는 관 T4 를 더해
화면 없는 Edge (cdp.py, 실제 시간) 로 찍고, 보기 화면 수식과 따로 짠 계산으로 잰다.

    python -I check_obst.py --out <make_synth --trench 폴더> [--shots <폴더>]

  지장물 (OBST): 가스 D200 깊이 0.4 m = T1 터파기를 가로지름 (관 윗면과 0.40 m) ·
                 통신 (관경 · 깊이 모름 → defaultDepth 1.075) = T1 옆 평행 (이격 0.20 m)
  a  노면 띠 가장자리 (켬 − 끔 차이 절반) ↔ 지표 ±max(관경, 0.15)/2 투영 (중앙 ≤ 1.5 px) · 지도 점선
  b  깊이 방식 (WebGL 만): 구멍 밖 땅 속 = 반투명 (켬 ↔ 끔 기울기 ≈ 1 − 0.45) · 구멍 안 보이는 곳 = 불투명
     (반투명 그림에서 푼 관 색과 같음) · 구멍 안 벽 뒤 = 안 그림 (화소 그대로)
  c  [지장물] 켜고 끄기 · 이 브라우저 기억 · 지장물 파일 없음 = 단추 숨김
  d  수량 (이 관 · 전체) ↔ 따로 짠 적분 (≤ 0.5 %) · 관로 고치기 끄는 동안 · 단면 바꿈 · 평균매설깊이 · «가시화 기본값 기준»
  e  간섭: 이격 0.3 → 1곳 (T1 ↔ 통신 0.20 m · 구간 가운데) · 0.45 → 2곳 (T1 ↔ 가스 0.40 m · 가로지르는 곳) ↔ 따로 잰 값 ·
     360 · 지도 빨간 고리 자리 · 목록 누름 → 그쪽을 봄 · 관 옮기면 (Shift 끌기) 다시 셈 · 되돌리면 다시
  f  프로그램 (hostmock): [지장물 넣기] → 파일 고르기 (못 읽음 · 짐작 항목) → 넣기 (못 넣음 · 넣음 → data/obstacles.js 다시 읽기)
  g  http 로 열어도 file:// 과 같은 그림
  모든 쪽 JS 오류 0
"""
import argparse
import functools
import http.server
import json
import math
import os
import pathlib
import shutil
import sys
import threading
import time
import urllib.parse

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
TR_DEF = dict(tp=0.2, n=0.3, margin=0.3, bed=0.1)
CFG = dict(tp=0.9, n=0.5, margin=0.4, bed=0.15)     # 단면 창에서 바꿈 (T4 얕은 곳은 벽 전체 직각)
OB_ALPHA = 0.45
# 시험 지장물: (꼭짓점, 층, 종류, 이름, 관경 m | None, 깊이 m | None)
OB_DEF_DEPTH = 1.075
OBST = [
    ([(2.0, -2.0), (2.0, 9.0)], 0, '가스', 'G-1', 0.2, 0.4),
    ([(-30.0, 3.425), (-10.0, 3.425)], 1, '통신', '통신', None, None),
]
OB_LAYERS = [{'name': '가스', 'color': '#ffd400'}, {'name': '통신', 'color': '#3ddc84'}]
# 땅 높이가 바뀌는 관 (수량 적분 시험): 꼭짓점 · 지표 g
T4 = {'layer': 1, 'label': 'T4', 'dia': 0.4, 'v': [(-22.0, -20.0), (-2.0, -26.0), (12.0, -22.0)], 'g': [50.0, 52.0, 49.6]}
V_SURF_A = ('sa', '0002', 315, -62, 60)       # 노면: 가스 띠
V_SURF_B = ('sb', '0001', 270, -38, 60)       # 노면: 통신 띠
V_PIT = ('pit', '0004', 345, -54, 50)         # 깊이: T1 구덩이를 가로지르는 가스 (남쪽에서)
V_QTY = ('q', '0002', 0, -50, 70)             # 수량: 가운데 = T1
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


# ---------------------------------------------------------------- 시험 자료
def ob_center(ob):
    """지장물 관 중심 표고 (보기 화면 약속: g − (깊이 ?? 기본) − (관경 ?? 0.15)/2)."""
    _, _, _, _, dia, dep = ob
    return GZ - (dep if dep is not None else OB_DEF_DEPTH) - (dia if dia is not None else 0.15) / 2


def obst_js():
    lines = []
    for verts, layer, kind, label, dia, dep in OBST:
        lines.append({'layer': layer, 'kind': kind, 'label': label, 'dia': dia, 'depth': dep,
                      'pts': [[x, y, None] for x, y in verts], 'g': [GZ] * len(verts), 'll': [ms.en2ll(x, y) for x, y in verts]})
    return 'window.RV_OBST = ' + json.dumps({'layers': OB_LAYERS, 'lines': lines, 'origin': ms.ORIGIN, 'defaultDepth': OB_DEF_DEPTH,
                                            'source': '시험'}, ensure_ascii=False) + ';\n'


def write_data(out_dir, obst=True):
    """pipes.js 에 T4 를 더하고 (없을 때만) · obstacles.js 를 쓰거나 지운다."""
    pj = os.path.join(out_dir, 'data', 'pipes.js')
    with open(pj, encoding='utf-8') as f:
        t = f.read()
    P = json.loads(t[t.index('{'):t.rindex('}') + 1])
    if not any(l.get('label') == 'T4' for l in P['lines']):
        P['lines'].append({'layer': T4['layer'], 'label': T4['label'], 'dia': T4['dia'], 'pts': [[x, y, None] for x, y in T4['v']],
                           'g': T4['g'], 'll': [ms.en2ll(x, y) for x, y in T4['v']]})
        with open(pj, 'w', encoding='utf-8', newline='\n') as f:
            f.write('window.RV_PIPES = ' + json.dumps(P, ensure_ascii=False, indent=1) + ';\n')
    oj = os.path.join(out_dir, 'data', 'obstacles.js')
    if obst:
        with open(oj, 'w', encoding='utf-8', newline='\n') as f:
            f.write(obst_js())
    elif os.path.exists(oj):
        os.remove(oj)
    for f in ('calib.js',):
        p = os.path.join(out_dir, 'data', f)
        if os.path.exists(p):
            os.remove(p)


def page_url(out_dir, view, mode='', extra='', page='로드뷰.html', base=None):
    _, shot, yaw, pitch, fov = view
    q = 'shot=%s&yaw=%s&pitch=%s&fov=%s' % (shot, yaw, pitch, fov) + ('&mode=' + mode if mode else '') + extra
    return (base or pathlib.Path(out_dir, page).resolve().as_uri()) + '?' + q


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
    return a[y0:y1 + 1, x0:x1 + 1].astype(float), (x0, y0)


def click(ed, eid):
    ed.js("document.getElementById('%s').click()" % eid)
    ed.frames(3)


def js_json(ed, expr):
    return json.loads(ed.js('JSON.stringify(%s)' % expr))


# ---------------------------------------------------------------- a  노면 띠
def band_measure(on, off, cam, verts, hw, col, skip=0.35, step=0.1):
    """띠 가장자리 (켬 − 끔 차이가 안쪽 값의 절반이 되는 곳) − 투영한 ±hw 선 (px, 안쪽 +) · 띠 가운데 색 · 점선 어두운 칸 비율."""
    err, fill_err, dark = [], [], []
    col = np.array(col, float)
    for A, B in zip(verts[:-1], verts[1:]):
        A, B = np.array(A, float), np.array(B, float)
        L = float(np.linalg.norm(B - A))
        d = (B - A) / L
        nl = np.array([-d[1], d[0]])
        for a in np.arange(skip, L - skip, step):
            c2 = A + d * a
            pc = cam.proj(np.array([*c2, GZ]))
            if pc is not None and 12 < pc[0] < cam.w - 12 and 12 < pc[1] < cam.h - 12:
                v_on, v_off = cv.bilinear(on, pc[None, :])[0], cv.bilinear(off, pc[None, :])[0]
                if np.isfinite(v_on).all():
                    fill_err.append(float(np.abs(v_on - (0.55 * col + 0.45 * v_off)).max()))
            for sg in (1, -1):
                E = np.array([*(c2 + sg * nl * hw), GZ])
                p0, q = cam.proj(E), cam.proj(E - np.array([*(sg * nl * 0.02), 0.0]))
                if p0 is None or q is None or not (12 < p0[0] < cam.w - 12 and 12 < p0[1] < cam.h - 12):
                    continue
                ind = (q - p0) / np.linalg.norm(q - p0)  # 화면에서 띠 안쪽
                s = np.arange(-6, 6.001, 0.25)
                pts = p0[None, :] + s[:, None] * ind[None, :]
                a_on, a_off = cv.bilinear(on, pts), cv.bilinear(off, pts)
                if not (np.isfinite(a_on).all() and np.isfinite(a_off).all()):
                    continue
                D = np.abs(a_on - a_off).max(axis=1)
                if np.median(D[(s >= -5) & (s <= -2)]) > 6:
                    continue  # 바깥이 바뀜 (이름표 등)
                lev = np.median(D[(s >= 0.75) & (s <= 1.5)])
                if lev < 25:
                    continue
                k = int(np.argmax(D >= lev / 2))
                if k == 0:
                    continue
                x = s[k - 1] + (s[k] - s[k - 1]) * (lev / 2 - D[k - 1]) / (D[k] - D[k - 1])
                err.append(float(x))
                # 가장자리 안쪽 0.75 px: 어두운 점선 칸 (어두운 색에 더 가까움) 인지
                v = cv.bilinear(on, (p0 + 0.75 * ind)[None, :])[0]
                vo = cv.bilinear(off, (p0 + 0.75 * ind)[None, :])[0]
                fill = 0.55 * col + 0.45 * vo
                dark.append(float(np.abs(v - col * 0.45).sum() < np.abs(v - fill).sum()))
    return np.array(err), np.array(fill_err), np.array(dark)


def stat(v):
    v = np.abs(np.asarray(v, float))
    return (float(np.median(v)), float(np.percentile(v, 90))) if v.size else (math.nan, math.nan)


def hexrgb(h):
    return [int(h[i:i + 2], 16) for i in (1, 3, 5)]


def test_surface(ed, out_dir, shots_dir):
    print('== a  노면 띠 · c 켜고 끄기')
    for view, oi in ((V_SURF_A, 0), (V_SURF_B, 1)):
        name = view[0]
        report('불러옴 ' + name, load(ed, page_url(out_dir, view)))
        click(ed, 'tgPipes')  # 관로 띠는 끄고 잰다
        on, _ = grab(ed, os.path.join(shots_dir, name + '_on.png'))
        click(ed, 'tgObst')
        off, _ = grab(ed, os.path.join(shots_dir, name + '_off.png'))
        click(ed, 'tgObst')
        click(ed, 'tgPipes')
        cam = ct.Cam(view, on.shape[1], on.shape[0])
        verts, layer, kind, _, dia, _ = OBST[oi]
        hw = max(dia or 0.15, 0.15) / 2
        e, fe, dk = band_measure(on, off, cam, verts, hw, hexrgb(OB_LAYERS[layer]['color']))
        m, p = stat(e)
        report('a %s 띠 가장자리 ↔ ±%.3f m 투영' % (kind, hw), e.size >= 40 and m <= 1.5,
               '표본 %d · 중앙 %.2f px · 90%% %.2f px · 평균 %+.2f' % (e.size, m, p, float(np.mean(e)) if e.size else 0))
        report('a %s 띠 가운데 = 층 색 55 %% 겹침' % kind, fe.size >= 20 and np.median(fe) <= 12, '중앙 차이 %.1f (표본 %d)' % (np.median(fe) if fe.size else -1, fe.size))
        report('a %s 테두리 = 어두운 점선' % kind, dk.size >= 20 and 0.3 <= dk.mean() <= 0.8, '어두운 칸 %.0f %%' % (dk.mean() * 100 if dk.size else -1))
        errs(ed, name)
    # 지도: 지장물 점선 (가스 층 색) 이 지도에 그려짐
    mp = ce.MapTf(ed)
    ed.screenshot(os.path.join(shots_dir, 'map.png'))
    a = np.asarray(Image.open(os.path.join(shots_dir, 'map.png')).convert('RGB')).astype(int)
    col = np.array(hexrgb(OB_LAYERS[0]['color']))
    hit, n = 0, 0
    for nn in np.arange(-1.0, 8.0, 0.05):  # 선 가운데 화소 (0.2 px 마다)
        x, y = mp.en_scr(2.0, nn)
        if math.hypot(2.0 - 2.0, nn - 3.0) < 1.5:
            continue  # T1 이 위에 그려지는 곳
        n += 1
        hit += int(np.abs(a[int(y), int(x)] - col).max() < 60)
    report('a 지도 가스 선 = 층 색 점선 (칠한 곳 6 : 빈 곳 5)', 0.35 <= hit / n <= 0.75, '칠한 곳 %.0f %% (%d 점)' % (hit / n * 100, n))

    # ---- c 켜고 끄기 · 기억
    v = V_SURF_A
    load(ed, page_url(out_dir, v))
    on, _ = grab(ed, os.path.join(shots_dir, 'c_on.png'))
    click(ed, 'tgObst')
    off, _ = grab(ed, os.path.join(shots_dir, 'c_off.png'))
    st = js_json(ed, "{on: document.getElementById('tgObst').classList.contains('on'), ls: localStorage.getItem('rv_ui:obst')}")
    report('c [지장물] 끔 → 단추 꺼짐 · 이 브라우저 기억', not st['on'] and st['ls'] == 'false', json.dumps(st))
    report('c 끔 → 그림 바뀜', (np.abs(on - off).max(axis=2) > 20).sum() > 500, '%d px' % (np.abs(on - off).max(axis=2) > 20).sum())
    load(ed, page_url(out_dir, v))
    off2, _ = grab(ed, os.path.join(shots_dir, 'c_reopen_off.png'))
    st = js_json(ed, "{on: document.getElementById('tgObst').classList.contains('on'), hid: document.getElementById('tgObst').hidden}")
    report('c 다시 열기 → 꺼진 채 (그림 = 끔)', not st['on'] and not st['hid'] and np.abs(off2 - off).max() <= 2,
           '최대 차이 %d' % np.abs(off2 - off).max())
    click(ed, 'tgObst')
    on2, _ = grab(ed, os.path.join(shots_dir, 'c_on2.png'))
    report('c 다시 켬 → 그림 = 켬', np.abs(on2 - on).max() <= 2 and ed.js("localStorage.getItem('rv_ui:obst')") == 'true',
           '최대 차이 %d' % np.abs(on2 - on).max())
    errs(ed, 'c')
    # 지장물 파일 없음 → 단추 숨김 · 오류 없음
    write_data(out_dir, obst=False)
    load(ed, page_url(out_dir, v))
    none, _ = grab(ed, os.path.join(shots_dir, 'c_nofile.png'))
    st = js_json(ed, "{hid: document.getElementById('tgObst').hidden, host: document.getElementById('hHome').hidden && document.getElementById('hObst').hidden, obst: typeof window.RV_OBST}")
    report('c 파일 없음 → [지장물] 숨김 · 그림 = 끔', st['hid'] and st['host'] and st['obst'] == 'undefined' and np.abs(none - off).max() <= 2,
           json.dumps(st) + ' · 최대 차이 %d' % np.abs(none - off).max())
    errs(ed, 'c 파일 없음')
    write_data(out_dir)


# ---------------------------------------------------------------- b  깊이 방식 (반투명 · 구멍 안 불투명 · 벽 뒤 안 보임)
def test_depth(ed, out_dir, shots_dir):
    print('== b  깊이 방식: 땅 속 반투명 · 구덩이 안 불투명')
    ed.js('localStorage.clear()')
    v = V_PIT
    report('불러옴', load(ed, page_url(out_dir, v, 'depth')))
    ct.set_ov(ed, False)
    on_p, _ = grab(ed, os.path.join(shots_dir, 'b_pit_on.png'))
    click(ed, 'tgObst')
    off_p, _ = grab(ed, os.path.join(shots_dir, 'b_pit_off.png'))
    load(ed, page_url(out_dir, v, 'depth', '&trench=0'))  # 지장물 꺼진 채 (기억)
    ct.set_ov(ed, False)
    off_x, _ = grab(ed, os.path.join(shots_dir, 'b_x_off.png'))
    click(ed, 'tgObst')
    on_x, _ = grab(ed, os.path.join(shots_dir, 'b_x_on.png'))
    ct.set_ov(ed, True)
    errs(ed, 'b')
    h, w = on_p.shape[:2]
    cam = ct.Cam(v, w, h)
    t4 = [cam.proj(np.array([x, y, GZ])) for x, y in T4['v']]
    report('T4 는 이 보기 밖 (구멍 그림 = T1 · T2 · T3 만)', all(p is None or not (0 <= p[0] < w and 0 <= p[1] < h) for p in t4))
    secs = ct.all_sections(TR_DEF)
    mask = ct.opening_mask(cam, secs)
    near_out = ~ct.dilate(mask, 3)
    inner = ~ct.dilate(~mask, 3)
    verts, _, _, _, dia, _ = OBST[0]
    zc, r = ob_center(OBST[0]), dia / 2
    ax = np.array([0.0, 1.0, 0.0])
    cls = {'out': [], 'vis': [], 'hid': []}
    for n in np.arange(verts[0][1] + 0.3, verts[1][1] - 0.3, 0.01):
        c = np.array([verts[0][0], n, zc])
        p = cam.proj(c)
        if p is None or not (10 < p[0] < w - 10 and 10 < p[1] < h - 10):
            continue
        ix, iy = int(p[0]), int(p[1])
        u = (cam.c - c) / np.linalg.norm(cam.c - c)
        Xf = c + u * r / np.linalg.norm(np.cross(u, ax))  # 카메라에서 c 로 가는 광선이 관 겉면에 닿는 곳
        if near_out[iy, ix]:
            cls['out'].append(p)
        elif inner[iy, ix]:
            vis = ct.visible(cam, secs[0], 0, Xf)
            if vis is True:
                cls['vis'].append(p)
            elif vis is False:
                cls['hid'].append(p)
    bl = lambda img, P: cv.bilinear(img, np.array(P))  # noqa: E731
    k_out = math.nan
    if cls['out']:
        P = cls['out']
        a, b = bl(on_x, P), bl(off_x, P)
        a, b = a - a.mean(axis=0), b - b.mean(axis=0)
        k_out = float((a * b).sum() / (b * b).sum())  # 켬 = α·관 색 + (1 − α)·바탕 → 기울기 1 − α
        same = np.abs(bl(on_p, P) - bl(on_x, P)).max(axis=1)
        report('b 구멍 밖: 켬 ↔ 바탕 기울기 = 1 − 0.45', len(P) >= 100 and abs(k_out - (1 - OB_ALPHA)) <= 0.08,
               '표본 %d · 기울기 %.3f (바탕 표준편차 %.0f)' % (len(P), k_out, float(b.std())))
        report('b 구멍 밖: 터파기 켬 = 끔 (같은 반투명)', np.median(same) <= 3, '중앙 차이 %.1f' % np.median(same))
    else:
        report('b 구멍 밖 표본', False, '0')
    if cls['vis']:
        P = cls['vis']
        on, off, a_on, a_off = bl(on_p, P), bl(off_p, P), bl(on_x, P), bl(off_x, P)
        C = (a_on - (1 - OB_ALPHA) * a_off) / OB_ALPHA  # 반투명 그림에서 푼 관 색
        dC = np.abs(on - C).max(axis=1)
        ch = np.argmax(np.abs(C - off), axis=1)
        idx = np.arange(len(P))
        alpha = (on[idx, ch] - off[idx, ch]) / (C[idx, ch] - off[idx, ch])
        tr_hyp = np.abs(on - (OB_ALPHA * C + (1 - OB_ALPHA) * off)).max(axis=1)
        report('b 구덩이 안 보이는 곳 = 불투명 (α ≈ 1)', len(P) >= 20 and 0.85 <= np.median(alpha) <= 1.15 and np.median(dC) <= 10,
               '표본 %d · α 중앙 %.2f · 관 색과 차이 %.1f (반투명이면 %.1f)' % (len(P), np.median(alpha), np.median(dC), np.median(tr_hyp)))
    else:
        report('b 구덩이 안 보이는 표본', False, '0')
    if cls['hid']:
        P = cls['hid']
        d = np.abs(bl(on_p, P) - bl(off_p, P)).max(axis=1)
        report('b 구덩이 안 벽 뒤 = 안 보임 (화소 그대로)', len(P) >= 5 and np.median(d) <= 3 and np.percentile(d, 90) <= 8,
               '표본 %d · 차이 중앙 %.1f · 90%% %.1f' % (len(P), np.median(d), np.percentile(d, 90)))
    else:
        report('b 벽 뒤 표본', False, '0')
    print('   (구멍 밖 %d · 안 보임 %d · 벽 뒤 %d)' % (len(cls['out']), len(cls['vis']), len(cls['hid'])))
    return on_p


# ---------------------------------------------------------------- d  수량
def sec_ref(D, g, zc, tr):
    """터파기 단면 (따로 짬): 지표 → 바닥 깊이 h · 포장 tp 는 직각 · 그 아래 1:n 사다리꼴 · tp ≥ h 면 벽 전체 직각."""
    zs = min(zc - D / 2, g - 0.01)
    h = g - (zs - tr['bed'])
    wb = D / 2 + tr['margin']
    if tr['tp'] >= h:
        return 2 * wb * h, wb
    wv = wb + tr['n'] * (h - tr['tp'])
    return 2 * wv * tr['tp'] + (wb + wv) * (h - tr['tp']), wv


def qty_ref(ln, tr, cover, sub=4000):
    P, G, D = ln['pts'], ln['g'], ln.get('dia') or 0.2
    hasz = ln.get('hasZ') is True
    zc = [p[2] if hasz and p[2] is not None else g - cover - D / 2 for p, g in zip(P, G)]
    L = V = pav = 0.0
    t = (np.arange(sub) + 0.5) / sub
    for k in range(len(P) - 1):
        seg = math.hypot(P[k + 1][0] - P[k][0], P[k + 1][1] - P[k][1])
        res = [sec_ref(D, G[k] + (G[k + 1] - G[k]) * s, zc[k] + (zc[k + 1] - zc[k]) * s, tr) for s in t]
        L += seg
        V += float(np.mean([a for a, _ in res])) * seg
        pav += 2 * float(np.mean([w for _, w in res])) * seg
    sand = 2 * (D / 2 + tr['margin']) * tr['bed'] * L
    pipe = math.pi * D * D / 4 * L
    return dict(len=L, cut=V, pav=pav, sand=sand, fill=V - pipe - sand)


QKEYS = ('len', 'cut', 'pav', 'sand', 'fill')


def panel(ed):
    return js_json(ed, "{line: document.getElementById('qLine').textContent, note: document.getElementById('qNote').textContent,"
                       " hit: document.getElementById('qHit').textContent, open: !document.getElementById('qtyBox').hidden,"
                       " v: Object.fromEntries([...document.querySelectorAll('#qTab .qv')].map((e) => [e.id, Number(e.dataset.v)])),"
                       " list: [...document.querySelectorAll('#qList button')].map((b) => [b.textContent, b.dataset.at])}")


def cmp_qty(name, got, want):
    rel = {k: abs(got[k] - want[k]) / max(abs(want[k]), 1e-9) for k in QKEYS}
    worst = max(rel, key=rel.get)
    return report(name, all(v <= 0.005 for v in rel.values()),
                  ' · '.join('%s %.2f/%.2f' % (k, got[k], want[k]) for k in QKEYS) + ' · 최대 %.3f %% (%s)' % (rel[worst] * 100, worst))


def check_panel(ed, name, want_line, tr, cover):
    st = panel(ed)
    lines = js_json(ed, 'window.RV_PIPES.lines')
    ln = next(l for l in lines if l['label'] == want_line)
    one = {k: st['v']['q_' + k] for k in QKEYS}
    tot = {k: st['v']['qt_' + k] for k in QKEYS}
    refs = [qty_ref(l, tr, cover) for l in lines]
    rt = {k: sum(r[k] for r in refs) for k in QKEYS}
    ok = report(name + ' 이 관 = ' + want_line, st['line'].endswith(want_line), st['line'])
    ok &= cmp_qty(name + ' 이 관 수량', one, qty_ref(ln, tr, cover))
    ok &= cmp_qty(name + ' 전체 (%d 줄)' % len(lines), tot, rt)
    return ok, st


def test_qty(ed, out_dir, shots_dir):
    print('== d  수량')
    ed.js('localStorage.clear()')
    inp = ce.Inp(ed)
    v = ('q', '0002', 0, -60, 70)
    load(ed, page_url(out_dir, v))
    click(ed, 'tgQty')
    ed.wait_js("document.getElementById('q_len').dataset.v !== undefined", 5)
    ed.frames(2)
    ok, st = check_panel(ed, 'd 가운데', 'T1', TR_DEF, ms.DEPTH)
    report('d «가시화 기본값 기준» (기본값)', st['note'] == '가시화 기본값 기준', st['note'])
    ed.screenshot(os.path.join(shots_dir, 'd_panel.png'))
    load(ed, page_url(out_dir, ('q4', '0002', 180, -35, 70)))
    click(ed, 'tgQty')
    ed.frames(3)
    check_panel(ed, 'd 남쪽을 봄', 'T4', TR_DEF, ms.DEPTH)
    # 관로 고치기: 지도에서 T2 고름 → 이 관 = T2 · T4 고르고 손잡이 끄는 동안
    mp = ce.MapTf(ed)
    click(ed, 'tgEdit')
    inp.click(*mp.en_scr(22.0, -20.0))
    ed.frames(3)
    check_panel(ed, 'd 고른 관', 'T2', TR_DEF, ms.DEPTH)
    inp.click(*mp.en_scr(-12.0, -23.0))
    ed.frames(3)
    before = panel(ed)['v']
    h0 = mp.en_scr(-2.0, -26.0)
    p0 = (round(h0[0]), round(h0[1]))
    inp.press(*p0)
    mid = (p0[0] + 14, p0[1] - 9)
    inp.drag_to(p0, mid, steps=6)
    ed.frames(4)
    _, st = check_panel(ed, 'd 끄는 동안 (놓기 전)', 'T4', TR_DEF, ms.DEPTH)
    report('d 끄는 동안 값이 바뀜', abs(st['v']['q_cut'] - before['q_cut']) > 0.05, '%.2f → %.2f' % (before['q_cut'], st['v']['q_cut']))
    inp.drag_to(mid, (p0[0] + 24, p0[1] - 14), steps=6)
    inp.release(p0[0] + 24, p0[1] - 14)
    check_panel(ed, 'd 놓은 뒤', 'T4', TR_DEF, ms.DEPTH)
    # 단면 창 · 평균매설깊이
    ct.set_cfg(ed, CFG)
    ed.frames(3)
    _, st = check_panel(ed, 'd 단면 바꿈 (T4 얕은 곳 = 벽 전체 직각)', 'T4', CFG, ms.DEPTH)
    report('d 단면 바꿈 → «가시화 기본값 기준» 없음', st['note'] == '', repr(st['note']))
    ed.js("(function(){ const i = document.getElementById('cover'); i.value = '1.4'; i.dispatchEvent(new Event('input', {bubbles: true})); })()")
    ed.frames(3)
    check_panel(ed, 'd 평균매설깊이 1.4', 'T4', CFG, 1.4)
    click(ed, 'secDef')
    ed.frames(3)
    _, st = check_panel(ed, 'd 기본값 (SAC)', 'T4', TR_DEF, 1.4)
    report('d 기본값 → «가시화 기본값 기준»', st['note'] == '가시화 기본값 기준', st['note'])
    ed.screenshot(os.path.join(shots_dir, 'd_edit_panel.png'))
    errs(ed, 'd')
    ed.js('localStorage.clear()')


# ---------------------------------------------------------------- e  간섭
def clear_ref(lines, gap, cover, step=0.01):
    """관 중심선을 1 cm 마다 · 지장물 3D 선분까지 정확한 거리 − 두 반지름. 구간 (이격 < gap) 마다
    (관 이름, 지장물, 가장 작은 이격, 자리 = 가장 작은 곳들 (1 cm 안) 가운데 표본의 두 점 평면 가운데)."""
    out = []
    for ln in lines:
        D = ln.get('dia') or 0.2
        hasz = ln.get('hasZ') is True
        P = np.array([[p[0], p[1], p[2] if hasz and p[2] is not None else g - cover - D / 2] for p, g in zip(ln['pts'], ln['g'])])
        S = []
        for k in range(len(P) - 1):
            m = max(1, math.ceil(np.hypot(*(P[k + 1][:2] - P[k][:2])) / step))
            t = np.arange(m + (1 if k == len(P) - 2 else 0)) / m
            S.append(P[k][None, :] + (P[k + 1] - P[k])[None, :] * t[:, None])
        S = np.concatenate(S)
        for oi, ob in enumerate(OBST):
            verts, _, kind, _, dia, _ = ob
            ro, zo = (dia or 0.15) / 2, ob_center(ob)
            O = [np.array([x, y, zo]) for x, y in verts]
            best = np.full(len(S), np.inf)
            Qb = np.zeros((len(S), 3))
            for a, b in zip(O[:-1], O[1:]):
                d = b - a
                t = np.clip(((S - a) @ d) / (d @ d), 0, 1)
                Q = a[None, :] + t[:, None] * d[None, :]
                dist = np.linalg.norm(S - Q, axis=1)
                upd = dist < best
                best[upd], Qb[upd] = dist[upd], Q[upd]
            c = best - D / 2 - ro
            bad = c < gap
            i = 0
            while i < len(S):
                if not bad[i]:
                    i += 1
                    continue
                j = i
                while j + 1 < len(S) and bad[j + 1]:
                    j += 1
                seg = np.arange(i, j + 1)
                mn = c[seg].min()
                near = seg[c[seg] <= mn + 0.01]
                q = near[(len(near) - 1) // 2]
                out.append(dict(pipe=ln['label'], kind=kind, c=float(mn), e=float((S[q][0] + Qb[q][0]) / 2), n=float((S[q][1] + Qb[q][1]) / 2),
                                z=float((S[q][2] + Qb[q][2]) / 2),   # 고리 높이 = 관 중심 · 지장물 중심 가장 가까운 두 점 가운데 (JACK 1127)
                                run=(float(S[i][0]), float(S[j][0]))))
                i = j + 1
    return out


def spots_of(st):
    out = []
    for text, at in st['list']:
        e, n, c = (float(x) for x in at.split(','))
        out.append(dict(text=text, e=e, n=n, c=c))
    return out


def cmp_spots(name, st, ref, tol_par=0.3, tol_x=0.05):
    got = spots_of(st)
    ok = len(got) == len(ref)
    rows = []
    for r in ref:
        g = min(got, key=lambda q: math.hypot(q['e'] - r['e'], q['n'] - r['n'])) if got else None
        if g is None:
            ok = False
            continue
        dpos = math.hypot(g['e'] - r['e'], g['n'] - r['n'])
        tol = tol_x if r['run'][1] - r['run'][0] < 2.0 else tol_par
        good = dpos <= tol and abs(g['c'] - r['c']) <= 0.005 and r['pipe'] in g['text'] and r['kind'] in g['text']
        ok &= good
        rows.append('%s↔%s 이격 %.3f/%.3f · 자리 (%.2f, %.2f)/(%.2f, %.2f) 어긋남 %.3f m' % (r['pipe'], r['kind'], g['c'], r['c'], g['e'], g['n'], r['e'], r['n'], dpos))
    return report(name, ok, '%d곳/%d곳 · ' % (len(got), len(ref)) + ' | '.join(rows))


def red_ring(img, cam, P, z_cam_r=None):
    """(e, n, GZ) 둘레 반지름 max(0.6 m, 9 px) 고리 위 48 점 중 빨간 화소가 2.5 px 안에 있는 비율."""
    q = cam.cam(P)
    if q[2] < 1:
        return 0.0
    rad = max(0.6, 9 * q[2] / cam.kx)
    red = (img[..., 0] > 170) & (img[..., 1] < 120) & (img[..., 2] < 120)
    hit = 0
    for t in np.linspace(0, 2 * math.pi, 48, endpoint=False):
        p = cam.proj(np.array([P[0] + rad * math.cos(t), P[1] + rad * math.sin(t), P[2]]))
        if p is None:
            continue
        x, y = int(round(p[0])), int(round(p[1]))
        win = red[max(0, y - 3):y + 4, max(0, x - 3):x + 4]
        hit += int(win.any())
    return hit / 48


def cf_set(ed, on):
    """간섭 카테고리 펴기 · 접기 (JACK 1127: 고리는 펼 때만)."""
    ed.js("(() => { const h = document.getElementById('hd_cf'); if ((h.getAttribute('aria-expanded') === 'true') !== %s) h.click(); })()" % ('true' if on else 'false'))
    ed.frames(3)


def look_view(shot, e, n, fov=60, name='look', z=None):
    s = ms.SHOTS[int(shot) - 1]
    de, dn, dz = e - s[0], n - s[1], (GZ if z is None else z) - (GZ + ms.HEIGHT)
    return (name, shot, math.degrees(math.atan2(de, dn)) % 360, math.degrees(math.atan2(dz, math.hypot(de, dn))), fov)


def set_gap(ed, v):
    ed.js("(function(){ const i = document.getElementById('qGap'); i.value = '%s'; i.dispatchEvent(new Event('input', {bubbles: true})); })()" % v)


def wait_hit(ed, text, timeout=3.0):
    return ed.wait_js("document.getElementById('qHit').textContent === %s" % json.dumps(text), timeout)


def test_clear(ed, out_dir, shots_dir):
    print('== e  간섭')
    ed.js('localStorage.clear()')
    v = ('q', '0002', 0, -60, 70)
    load(ed, page_url(out_dir, v))
    click(ed, 'tgQty')
    lines = js_json(ed, 'window.RV_PIPES.lines')
    ok = wait_hit(ed, '간섭 1곳')
    st = panel(ed)
    ref = clear_ref(lines, 0.3, ms.DEPTH)
    report('e 이격 0.3 → «간섭 1곳»', ok, st['hit'])
    cmp_spots('e 이격 0.3 ↔ 따로 잰 값', st, ref)
    ref_b = ref[0] if ref else None
    set_gap(ed, 0.45)
    ok = wait_hit(ed, '간섭 2곳')
    st = panel(ed)
    ref2 = clear_ref(lines, 0.45, ms.DEPTH)
    report('e 이격 0.45 → «간섭 2곳»', ok, st['hit'])
    cmp_spots('e 이격 0.45 ↔ 따로 잰 값 (가로지름 = 정확히)', st, ref2)
    report('e 이격 기준 이 브라우저 기억', ed.js("localStorage.getItem('rv_gap:' + window.RV_PROJECT.title)") == '0.45')
    # 360 고리: 통신 간섭 자리를 따로 정한 방향으로 봄 (사진 0001 에서) · 고리 = 간섭 카테고리 펼 때만 · 높이 = 두 관 가장 가까운 곳 가운데 (JACK 1127)
    if ref_b:
        vb = look_view('0001', ref_b['e'], ref_b['n'], 60, 'e_ring', ref_b['z'])
        load(ed, page_url(out_dir, vb))
        click(ed, 'tgQty')
        wait_hit(ed, '간섭 2곳')  # 셈이 끝난 뒤 (창은 왼쪽 위 · 고리는 가운데)
        cf_set(ed, False)
        img, _ = grab(ed, os.path.join(shots_dir, 'e_ring_fold.png'))
        cam = ct.Cam(vb, img.shape[1], img.shape[0])
        fr0 = red_ring(img, cam, np.array([ref_b['e'], ref_b['n'], ref_b['z']]))
        cf_set(ed, True)
        img, _ = grab(ed, os.path.join(shots_dir, 'e_ring.png'))
        fr = red_ring(img, cam, np.array([ref_b['e'], ref_b['n'], ref_b['z']]))
        frg = red_ring(img, cam, np.array([ref_b['e'], ref_b['n'], GZ]))
        report('e 간섭 접으면 고리 없음 · 펴면 고리', fr0 < 0.2 and fr >= 0.6, '접음 %.0f %% · 폄 %.0f %%' % (fr0 * 100, fr * 100))
        report('e 360 빨간 고리 = 간섭 자리 높이 (따로 투영 · 땅 아님)', fr >= 0.6 and frg < 0.3, '고리 위 %.0f %% · 땅 높이 %.0f %% · z %.2f (땅 %.2f)' % (fr * 100, frg * 100, ref_b['z'], GZ))
        # 목록 누름 → 그쪽을 봄 (사진 0002 에서 25 m · 옮기지 않음)
        load(ed, page_url(out_dir, v))
        click(ed, 'tgQty')
        wait_hit(ed, '간섭 2곳')
        cf_set(ed, True)
        btn = ed.js("[...document.querySelectorAll('#qList button')].findIndex((b) => b.textContent.indexOf('통신') >= 0)")
        ed.js("document.querySelectorAll('#qList button')[%d].click()" % btn)
        time.sleep(0.3)
        ed.frames(3)
        img, _ = grab(ed, os.path.join(shots_dir, 'e_go.png'))
        got = spots_of(panel(ed))
        sp = next(q for q in got if '통신' in q['text'])
        vg = look_view('0002', sp['e'], sp['n'], 70, 'e_go', ref_b['z'])
        cam = ct.Cam(vg, img.shape[1], img.shape[0])
        fr = red_ring(img, cam, np.array([sp['e'], sp['n'], ref_b['z']]))
        report('e 목록 누름 → 그쪽을 봄 (고리가 가운데)', fr >= 0.6, '고리 위 %.0f %% · 사진 %s' % (fr * 100, ed.js("document.getElementById('shotNo').textContent")))
        r = ce.rect(ed, 'map')
        ed.screenshot(os.path.join(shots_dir, 'e_go_full.png'))
        a = np.asarray(Image.open(os.path.join(shots_dir, 'e_go_full.png')).convert('RGB')).astype(int)
        cx, cy = r['left'] + r['width'] / 2, r['top'] + r['height'] / 2
        red = (a[..., 0] > 170) & (a[..., 1] < 120) & (a[..., 2] < 120)
        hit = sum(int(red[int(round(cy + 9 * math.sin(t))) - 2:int(round(cy + 9 * math.sin(t))) + 3,
                          int(round(cx + 9 * math.cos(t))) - 2:int(round(cx + 9 * math.cos(t))) + 3].any())
                  for t in np.linspace(0, 2 * math.pi, 32, endpoint=False))
        report('e 지도 빨간 고리 (지도 가운데로 옮김)', hit >= 20, '고리 위 %d / 32' % hit)
        # 멀리 있는 간섭: 사진 0004 (남쪽 8 m) 에서 누르면 30 m 넘음 → 가까운 사진으로 옮긴 뒤 봄
        load(ed, page_url(out_dir, ('far', '0003', 0, -60, 70)))  # 사진 3 (10, 0) 에서 30.4 m
        click(ed, 'tgQty')
        wait_hit(ed, '간섭 2곳')
        cf_set(ed, True)
        ed.js("document.querySelectorAll('#qList button')[%d].click()" % btn)
        moved = ed.wait_js("document.getElementById('shotNo').textContent.indexOf('1 /') === 0", 10)
        time.sleep(0.3)
        img, _ = grab(ed, os.path.join(shots_dir, 'e_go_far.png'))
        vf = look_view('0001', sp['e'], sp['n'], 70, 'e_far', ref_b['z'])
        fr = red_ring(img, ct.Cam(vf, img.shape[1], img.shape[0]), np.array([sp['e'], sp['n'], ref_b['z']]))
        report('e 30 m 넘음 → 가까운 사진 (1) 으로 옮긴 뒤 봄', moved and fr >= 0.6,
               '사진 %s · 고리 위 %.0f %%' % (ed.js("document.getElementById('shotNo').textContent"), fr * 100))
    # 관 옮김 (Shift 끌기) → 다시 셈 (끄는 동안에도) · 되돌림 → 다시
    set_gap(ed, 0.3)
    load(ed, page_url(out_dir, v))
    click(ed, 'tgQty')
    report('e 이격 0.3 (다시)', wait_hit(ed, '간섭 1곳'), panel(ed)['hit'])
    inp = ce.Inp(ed)
    mp = ce.MapTf(ed)
    click(ed, 'tgEdit')
    at = mp.en_scr(-5.0, 3.0)
    inp.click(*at)
    p0 = (round(at[0]), round(at[1]))
    inp.press(p0[0], p0[1], ce.SHIFT)
    inp.drag_to(p0, (p0[0], p0[1] - 10), ce.SHIFT, steps=8)
    t0 = time.time()
    ok = wait_hit(ed, '간섭 없음', 3.0)
    report('e 관 옮기는 동안 (놓기 전) 다시 셈 → 간섭 없음', ok, '%.2f 초 · %s' % (time.time() - t0, panel(ed)['hit']))
    inp.release(p0[0], p0[1] - 10, ce.SHIFT)
    T1 = next(l for l in js_json(ed, 'window.RV_PIPES.lines') if l['label'] == 'T1')
    ref3 = clear_ref([T1], 0.3, ms.DEPTH)
    report('e 옮긴 T1 따로 잼 → 간섭 없음', not ref3 and T1['pts'][0][1] > 4.0, 'T1 북 %.2f m' % T1['pts'][0][1])
    inp.key('z', 'KeyZ', 90, ce.CTRL)
    report('e Ctrl+Z → 다시 «간섭 1곳»', wait_hit(ed, '간섭 1곳', 3.0), panel(ed)['hit'])
    errs(ed, 'e')
    ed.js('localStorage.clear()')


# ---------------------------------------------------------------- f  프로그램 (hostmock): 지장물 넣기
HOST_PAGE = '로드뷰_obhost.html'
PICK_BAD = {'path': 'C:\\시험\\깨짐.shp', 'kind': '', 'name': '깨짐.shp', 'fields': [], 'error': 'dbf 못 읽음'}
FIELDS = [{'name': 'FTR_CDE', 'samples': ['가스', '통신']}, {'name': 'PIPE_DIA', 'samples': [200, None]},
          {'name': 'DEPTH', 'samples': [0.4, None]}, {'name': 'NAME', 'samples': ['G-1', '통신']}, {'name': 'FAIL', 'samples': []}]
PICK_A = {'path': 'C:\\시험\\지장물.shp', 'kind': 'shp', 'name': '지장물.shp', 'fields': FIELDS, 'guess_type': 'FTR_CDE',
          'guess_depth': 'DEPTH', 'guess_label': 'NAME', 'guess_dia': None, 'note': '.prj 없음 → EPSG:5186 로 봄'}
PICK_B = dict(PICK_A, guess_dia='PIPE_DIA', note='')


def host_page(out_dir):
    with open(os.path.join(out_dir, '로드뷰.html'), encoding='utf-8') as f:
        html = f.read()
    assert ci.TAG in html
    with open(os.path.join(out_dir, HOST_PAGE), 'w', encoding='utf-8', newline='\n') as f:
        f.write(html.replace(ci.TAG, '<script src="hostmock.js"></script>\n' + ci.TAG))
    shutil.copyfile(os.path.join(HERE, 'hostmock.js'), os.path.join(out_dir, 'hostmock.js'))


def host_cleanup(out_dir):
    for f in (HOST_PAGE, 'hostmock.js'):
        p = os.path.join(out_dir, f)
        if os.path.exists(p):
            os.remove(p)


def dlg_state(ed):
    return js_json(ed, "{dlg: !document.getElementById('obstDlg').hidden && !document.getElementById('dlgBack').hidden,"
                       " file: document.getElementById('omFile').textContent, shp: !document.getElementById('omShp').hidden,"
                       " type: document.getElementById('omType').value, dia: document.getElementById('omDia').value,"
                       " dep: document.getElementById('omDep').value, label: document.getElementById('omLabel').value,"
                       " depth: document.getElementById('omDepth').value, note: document.getElementById('omNote').textContent,"
                       " msg: document.getElementById('omMsg').textContent, go: document.getElementById('omGo').disabled,"
                       " opts: [...document.getElementById('omDia').options].map((o) => o.textContent)}")


def test_host(ed, out_dir, shots_dir):
    print('== f  프로그램 (hostmock): 지장물 넣기')
    write_data(out_dir, obst=False)
    host_page(out_dir)
    try:
        ed.js('localStorage.clear()')
        ed.net(block=ci.BLOCK)
        v = V_SURF_A
        report('불러옴', load(ed, page_url(out_dir, v, page=HOST_PAGE)))
        st = js_json(ed, "{tools: !document.getElementById('hHome').hidden, ob: !document.getElementById('hObst').hidden,"
                         " tg: document.getElementById('tgObst').hidden}")
        report('f 프로그램 단추 [지장물 넣기] 보임 · 지장물 없음 → [지장물] 숨김', st['tools'] and st['ob'] and st['tg'], json.dumps(st))
        ct.set_ov(ed, True)
        before, _ = grab(ed, os.path.join(shots_dir, 'f_before.png'))
        ed.js('window.__host.opicks = %s' % json.dumps([PICK_BAD, PICK_A, PICK_B], ensure_ascii=False))
        click(ed, 'hObst')
        s = dlg_state(ed)
        report('f 창 열림 · 기본 깊이 1 · [넣기] 막힘', s['dlg'] and s['depth'] == '1' and s['go'] and not s['shp'], json.dumps(s, ensure_ascii=False)[:160])
        click(ed, 'omPick')
        ed.wait_js("document.getElementById('omMsg').textContent !== ''", 3)
        s = dlg_state(ed)
        report('f 못 읽는 파일 → «못 읽음» · [넣기] 막힘', s['msg'].startswith('못 읽음') and '깨짐.shp' in s['msg'] and s['go'] and not s['shp'], s['msg'])
        click(ed, 'omPick')
        ed.wait_js("!document.getElementById('omShp').hidden", 3)
        s = dlg_state(ed)
        report('f 고른 파일 → 짐작 항목 (관경 짐작 없음 = (없음))',
               s['type'] == 'FTR_CDE' and s['dia'] == '' and s['dep'] == 'DEPTH' and s['label'] == 'NAME' and not s['go'] and s['msg'] == ''
               and s['note'] == PICK_A['note'] and s['opts'][0] == '(없음)' and s['file'] == '지장물.shp · SHP',
               '종류 %s · 관경 %r · 깊이 %s · 이름 %s · %s' % (s['type'], s['dia'], s['dep'], s['label'], s['note']))
        click(ed, 'omPick')
        ed.wait_js("document.getElementById('omDia').value === 'PIPE_DIA'", 3)
        s = dlg_state(ed)
        report('f 관경 항목 짐작 → 그 항목', s['dia'] == 'PIPE_DIA', s['dia'])
        # 못 넣음 (프로그램이 거절)
        ed.js("document.getElementById('omType').value = 'FAIL'")
        click(ed, 'omGo')
        ed.wait_js("document.getElementById('omMsg').textContent !== ''", 3)
        s = dlg_state(ed)
        report('f 못 넣음 → 알림 · 창 그대로', s['dlg'] and s['msg'] == '종류 항목 없음' and not s['go'], s['msg'])
        # 넣음 → 시험기가 data/obstacles.js 를 씀 → 다시 읽기
        ed.js("document.getElementById('omType').value = 'FTR_CDE'")
        ed.js("(function(){ const i = document.getElementById('omDepth'); i.value = '%s'; })()" % OB_DEF_DEPTH)
        n_con = len(ed.console)
        click(ed, 'omGo')
        t0 = time.time()
        wrote = False
        while time.time() - t0 < 10:
            ed.pump(0.1)
            if any(t == 'SELFTEST HOSTCMD write_obstacles' for _, t in ed.console[n_con:]):
                write_data(out_dir, obst=True)
                ed.js("window.__hostAck = window.__hostAck || {}; window.__hostAck.write_obstacles = 1")
                wrote = True
                break
        ok = wrote and ed.wait_js("document.getElementById('dlgBack').hidden", 5)
        ed.frames(3)
        calls = js_json(ed, "window.__host.calls.filter((c) => c.name === 'import_obstacles').map((c) => c.arg)")
        want = {'path': PICK_A['path'], 'type_field': 'FTR_CDE', 'dia_field': 'PIPE_DIA', 'depth_field': 'DEPTH', 'label_field': 'NAME',
                'default_depth': OB_DEF_DEPTH}
        report('f import_obstacles 인자', len(calls) == 2 and calls[-1] == want, json.dumps(calls[-1] if calls else None, ensure_ascii=False))
        st = js_json(ed, "{toast: document.getElementById('toast').textContent, tg: !document.getElementById('tgObst').hidden,"
                         " on: document.getElementById('tgObst').classList.contains('on'), n: (window.RV_OBST && window.RV_OBST.lines || []).length}")
        report('f 넣음 → 창 닫힘 · «지장물 2개» · [지장물] 켜짐', ok and st['toast'].startswith('지장물 2개') and st['tg'] and st['on'] and st['n'] == 2,
               json.dumps(st, ensure_ascii=False))
        reqs = [u for u in ed.requests if 'obstacles.js' in u]
        report('f data/obstacles.js 다시 읽음 (?v= 캐시 피함)', any('obstacles.js?v=' in u for u in reqs), ' · '.join(u.rsplit('/', 1)[-1] for u in reqs))
        after, _ = grab(ed, os.path.join(shots_dir, 'f_after.png'))
        ref, _ = None, None
        report('f 넣은 지장물이 그려짐', (np.abs(after - before).max(axis=2) > 20).sum() > 500, '%d px 바뀜' % (np.abs(after - before).max(axis=2) > 20).sum())
        errs(ed, 'f')
    finally:
        host_cleanup(out_dir)
        write_data(out_dir, obst=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--shots', default=None)
    ap.add_argument('--smoke', default=None, help='이 보기만 찍고 끝 (이름,사진,yaw,pitch,fov,mode)')
    args = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    out_dir = os.path.abspath(args.out)
    shots_dir = os.path.abspath(args.shots or os.path.join(out_dir, '_shots_obst'))
    os.makedirs(shots_dir, exist_ok=True)
    write_data(out_dir)
    if args.smoke:
        name, shot, yaw, pitch, fov, mode = args.smoke.split(',')
        with Edge(os.path.join(shots_dir, '_prof_smoke'), cv.WIN) as ed:
            ed.net(block=ci.BLOCK)
            load(ed, page_url(out_dir, (name, shot, yaw, pitch, fov), mode))
            ed.js("document.getElementById('tgQty').click()")
            time.sleep(0.6)
            ed.frames(3)
            ed.screenshot(os.path.join(shots_dir, 'smoke_%s.png' % name))
            print(ed.js("document.getElementById('qtyBox').innerText"))
            print('errors', cv.errors(ed))
        return 0
    srv = http.server.ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(ci._Quiet, directory=out_dir))
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    ed = Edge(os.path.join(shots_dir, '_prof_o'), cv.WIN).__enter__()
    try:
        ed.net(block=ci.BLOCK)
        test_surface(ed, out_dir, shots_dir)
        gl_file = test_depth(ed, out_dir, shots_dir)
        test_qty(ed, out_dir, shots_dir)
        test_clear(ed, out_dir, shots_dir)
        # ---- g http
        print('== g  http')
        ed.js('localStorage.clear()')
        base = 'http://127.0.0.1:%d/%s' % (srv.server_address[1], urllib.parse.quote('로드뷰.html'))
        report('불러옴 (http)', load(ed, page_url(out_dir, V_PIT, 'depth', base=base)))
        ct.set_ov(ed, False)
        glh, _ = grab(ed, os.path.join(shots_dir, 'g_http_gl.png'))
        ct.set_ov(ed, True)
        same = glh.shape == gl_file.shape
        dd = np.abs(glh - gl_file).max(axis=2) if same else None
        report('g http = file:// (WebGL 만 · 지장물 켬)', same and (dd > 3).mean() < 0.001, '다른 화소 %.4f %%' % ((dd > 3).mean() * 100 if same else -1))
        click(ed, 'tgQty')
        report('g http 간섭 셈', wait_hit(ed, '간섭 1곳'), panel(ed)['hit'])
        errs(ed, 'g http')
        test_host(ed, out_dir, shots_dir)
    finally:
        ed.__exit__(None, None, None)
        srv.shutdown()
        host_cleanup(out_dir)
        write_data(out_dir)
    for where, e in R['errs']:
        print('   오류 [%s]: %s' % (where, e))
    print('사진:', shots_dir)
    print('전체:', '통과' if R['ok'] and not R['errs'] else '실패')
    return 0 if R['ok'] and not R['errs'] else 1


if __name__ == '__main__':
    sys.exit(main())
