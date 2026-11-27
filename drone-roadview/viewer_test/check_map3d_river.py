"""3D 매핑 보기 · 하천 추진 · 횡단 시험 (JACK 1126): 결과 폴더를 작은 HTTP 서버로 열고 화면 없는 Edge (cdp.py) 를 진짜 마우스로 몬다.
셈 (viewer/river.js) 은 node 시험 (check_river_calc.js) 이 손셈과 견주고, 여기서는 화면에서 같은 셈이 나오는지 · 그림 · 저장을 본다.
원래 data/pipes.js 는 건드리지 않음 — 시험용 관로 파일 data/pipes_river.js · 페이지 index_river*.html 을 더해 씀.

    python -X utf8 -I check_map3d_river.py --out <3D 매핑 결과 폴더 (check_map3d_ug 와 같은 것)> --shots <그림 폴더>

  j  추진: [추진] → 관 위 두 점 → 구간 (측점 · 시점 = 발진구) · 하상 = dem.js 따로 셈 · 관 윗면 = 하상 − 2 · 바닥 = 관 바닥 − 0.5 ·
     깊이 · 추진 길이 · 굴착 · H-파일 · 토류판 = 손셈 · 3D 그물 · 토피 3 → 바닥 1 m 내려감 · 발진 길이 8 → 추진 길이 2 m 줄어듦 ·
     원형 → 라이너 = 깊이 합 · 바꾸기 → 발진 = 종점 · 수량 카테고리 «하천» 표 = 손셈 · 종단 창 (그림 · 표)
  o  횡단: [횡단] → 두 점 → 둑 길이 = 2 (b − a) + 4 w 근처 · 톤마대 개수 = 따로 셈 (dem.js 로 다리마다) · 높이 2 → 천단 0.5 올라감 ·
     2단계 그림 다름 · 시트파일 m² = 따로 셈 · 근입 바꾸면 m² 바뀜
  m  오른쪽 메뉴: 관 위 → «여기서 하천 추진 · 횡단» → 추진 → 시점 = 누른 자리 · Esc 취소
  d  지우기 (목록 ×) · 저장 (save_pipes 글에 river) → 그 글로 다시 열면 남아 있음
  b  브라우저 (프로그램 밖 · 웹 공유): 저장된 구간 보임 · 3D · 종단 창
  모든 쪽 JS 오류 0
"""
import argparse
import json
import math
import os
import re
import shutil
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
sys.path.insert(0, REPO)
from cdp import Edge  # noqa: E402
import check_edit as ce  # noqa: E402
from check_map3d_ug import J, R, cat_open, errs, report, serve, setup_page, shot, ui_click  # noqa: E402
from check_map3d_ctx import calls, item_xy, rclick  # noqa: E402

WIN = (1600, 900)

# 화면 안에서 따로 셈 (river.js 를 쓰지 않음 · dem.js 땅 = __map3d.demAt)
CALC_JS = r"""
window.__rc = {
  P(src) { const L = __map3d.lines().find((q) => q.src === src); return L; },
  at(src, s) {                         // 관 줄 측점 s 의 [x, y, tx, ty]
    const L = this.P(src), C = L.cum, P = L.pts; let k = 0;
    while (k + 2 < C.length && C[k + 1] < s) k++;
    const l = C[k + 1] - C[k], t = l > 0 ? Math.min(1, Math.max(0, (s - C[k]) / l)) : 0, dx = P[k * 3 + 3] - P[k * 3], dy = P[k * 3 + 4] - P[k * 3 + 1], d = Math.hypot(dx, dy) || 1;
    return [P[k * 3] + dx * t, P[k * 3 + 1] + dy * t, dx / d, dy / d];
  },
  g(x, y) { return __map3d.rv.ground(x, y); },   // 화면과 같은 땅 (dem.js → 없으면 3D 모델 면 → 파일 땅) · 셈 (칸 · 단 · 피라미드) 은 여기서 따로
  gmin(src, a, b) { let m = Infinity; for (let s = a; s <= b + 1e-9; s += 0.05) { const p = this.at(src, s), g = this.g(p[0], p[1]); if (g < m) m = g; } return m; },
  gs(src, s) { const p = this.at(src, s); return this.g(p[0], p[1]); },
  // 횡단 둑 (단계 st · 간격 w · 천단 top) → 다리 셋 [[x, y]…] 0.05 m 마다
  legs(src, a, b, w, st) {
    const sm = (a + b) / 2, s0 = st === 1 ? a : b, out = [];
    const side = (s1, s2, o) => { const r = [], n = Math.ceil(Math.abs(s2 - s1) / 0.05); for (let i = 0; i <= n; i++) { const p = this.at(src, s1 + (s2 - s1) * i / n); r.push([p[0] - p[3] * o, p[1] + p[2] * o]); } return r; };
    out.push(side(s0, sm, w));
    const p = this.at(src, sm), cr = [], n = Math.ceil(2 * w / 0.05);
    for (let i = 0; i <= n; i++) { const u = w - 2 * w * i / n; cr.push([p[0] - p[3] * u, p[1] + p[2] * u]); }
    out.push(cr);
    out.push(side(sm, s0, -w));
    return out;
  },
  dam(src, a, b, w, st, top, emb) {    // 둑 길이 · 톤마대 (1.1 m 칸 · 0.8 m 단 · 피라미드) · 시트파일 m²
    let len = 0, bags = 0, sheet = 0;
    for (const L of this.legs(src, a, b, w, st)) {
      const cum = [0];
      for (let i = 1; i < L.length; i++) cum.push(cum[i - 1] + Math.hypot(L[i][0] - L[i - 1][0], L[i][1] - L[i - 1][1]));
      const ll = cum[cum.length - 1]; len += ll;
      const nc = Math.max(1, Math.ceil(ll / 1.1 - 1e-9));
      for (let i = 0; i < nc; i++) {
        const u = (i + 0.5) * ll / nc; let j = 1; while (j + 1 < L.length && cum[j] < u) j++;
        const t = (u - cum[j - 1]) / (cum[j] - cum[j - 1] || 1), x = L[j - 1][0] + (L[j][0] - L[j - 1][0]) * t, y = L[j - 1][1] + (L[j][1] - L[j - 1][1]) * t;
        const h = top - this.g(x, y), k = h > 0.05 ? Math.ceil(h / 0.8 - 1e-9) : 0; bags += k * (k + 1) / 2;
      }
      for (let i = 1; i < L.length; i++) {
        const ha = Math.max(0, top - this.g(L[i - 1][0], L[i - 1][1])), hb = Math.max(0, top - this.g(L[i][0], L[i][1]));
        if (ha > 0 || hb > 0) sheet += (cum[i] - cum[i - 1]) * ((ha + hb) / 2 + emb);
      }
    }
    return { len, bags, sheet };
  },
  plan(src) { const p = __map3d.rv.plans(src)[0]; return p ? { k: p.k, a: p.a, b: p.b, gMin: p.gMin, top: p.top, zc: p.zc, zb: p.zb, jackLen: p.jackLen, cas: p.cas, damTop: p.damTop,
    shafts: p.shafts ? p.shafts.map((s) => ({ s: s.s, g: s.g, depth: s.depth, w: s.w, l: s.l, shape: s.shape })) : null, q: p.q } : null; },
  plans(src) { return __map3d.rv.plans(src).map((p) => ({ k: p.k, a: p.a, b: p.b, q: p.q, top: p.top, damTop: p.damTop, gMin: p.gMin })); },
  qv(k) { const e = document.getElementById('q_rv_' + k); return e ? Number(e.dataset.v) : NaN; },
  setIn(id, v) { const e = document.getElementById(id); e.value = String(v); e.dispatchEvent(new Event('input')); e.dispatchEvent(new Event('change')); return true; },
};
"""


def prep(out):
    """시험용 페이지 · 관로 파일 (원래 파일은 그대로)."""
    os.makedirs(os.path.join(out, 'viewer'), exist_ok=True)
    for fn in ('map3d.js', 'river.js', 'cost.js'):     # cost.js = 개략공사비 셈 (1127)
        shutil.copy2(os.path.join(REPO, 'viewer', fn), os.path.join(out, 'viewer', fn))
    shutil.copy2(os.path.join(HERE, 'hostmock.js'), os.path.join(out, 'viewer', 'hostmock.js'))
    html = open(os.path.join(REPO, 'viewer', 'map3d.html'), encoding='utf-8').read().replace('data/pipes.js', 'data/pipes_river.js')
    open(os.path.join(out, 'index_river.html'), 'w', encoding='utf-8').write(html)
    open(os.path.join(out, 'index_river_host.html'), 'w', encoding='utf-8').write(
        html.replace('<script src="viewer/map3d.js"></script>', '<script src="viewer/hostmock.js"></script>\n<script src="viewer/map3d.js"></script>'))
    shutil.copy2(os.path.join(out, 'data', 'pipes.js'), os.path.join(out, 'data', 'pipes_river.js'))


def page(ed, url):
    ok = setup_page(ed, url)
    ed.js(CALC_JS)
    return ok


def two_points(ed, inp, src, a, b, d=70, el=55, az=60):
    ed.js('__t.look(%d, %f, %f, %f, %f)' % (src, (a + b) / 2, d, el, az))
    ed.frames(4)
    for s in (a, b):
        q = J(ed, '__t.at(%d, %f, true)' % (src, s))
        inp.click(q[0], q[1])


def near(a, b, tol):
    return a is not None and b is not None and abs(a - b) <= tol


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--shots', required=True)
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    out, SD = os.path.abspath(a.out), os.path.abspath(a.shots)
    os.makedirs(SD, exist_ok=True)
    prep(out)
    srv, base = serve(out)
    ed = Edge(os.path.join(SD, '_prof'), WIN, gl=False).__enter__()
    inp = ce.Inp(ed)
    try:
        print('== 열기')
        report('열림 (프로그램 흉내)', page(ed, base + 'index_river_host.html'))
        ed.js('localStorage.clear()')
        page(ed, base + 'index_river_host.html')
        report('하천 셈 읽힘 (river.js)', J(ed, '__map3d.rv.ok') is True)
        errs(ed, '열기')
        L0 = J(ed, '__rc.P(0).len')
        cut0 = J(ed, '__map3d.rv.lineQty(0).cut')

        print('== j 추진')
        cat_open(ed, inp, 'hd_river')
        ui_click(ed, inp, 'rvJack')
        report('j 추진 단추 켜짐 · 시점 안내', J(ed, "__map3d.SH.arm === 'r:jack' && document.getElementById('shTip').textContent.indexOf('발진') >= 0"))
        sa, sb = 130.0, 170.0          # 곧은 구간 (121.8 m 에 38° 꺾임 · 추진은 꺾인 구간을 막음 · 1126 검토)
        two_points(ed, inp, 0, sa, sb)
        rv = J(ed, 'window.RV_PIPES.lines[0].river || null')
        okj = bool(rv) and len(rv) == 1 and rv[0]['k'] == 'jack' and abs(rv[0]['a'] - sa) < 0.4 and abs(rv[0]['b'] - sb) < 0.4 and rv[0]['rev'] is False
        report('j 구간 (측점 · 시점 = 발진구)', okj, str(rv and {k: rv[0][k] for k in ('k', 'a', 'b', 'shape', 'rev')}))
        p = J(ed, '__rc.plan(0)')
        A, B = p['a'], p['b']
        gmin = J(ed, '(() => { let m = Infinity; for (let s = %f; s <= %f; s += 0.05) { const p = __rc.at(0, s), g = __map3d.demAt(p[0], p[1]); if (g < m) m = g; } return m; })()' % (A, B))
        report('j 하상 = dem.js (맨땅) 가장 낮은 곳 (따로 셈)', near(p['gMin'], gmin, 0.05), '%.3f / %.3f' % (p['gMin'], gmin))
        report('j 관 윗면 = 하상 − 2 · 바닥 = 관 바닥 − 0.5', near(p['zc'] + p['cas'] / 2, p['gMin'] - 2.0, 1e-6) and near(p['zb'], p['zc'] - p['cas'] / 2 - 0.5, 1e-6), '윗면 %.3f · 바닥 %.3f' % (p['top'], p['zb']))
        gL, gA = J(ed, '__rc.gs(0, %f)' % A), J(ed, '__rc.gs(0, %f)' % B)
        dL, dA = gL - p['zb'], gA - p['zb']
        report('j 발진 · 도달 깊이 = 그 자리 땅 − 바닥', near(p['shafts'][0]['depth'], dL, 0.01) and near(p['shafts'][1]['depth'], dA, 0.01), '%.2f · %.2f' % (dL, dA))
        report('j 추진 길이 = 구간 − 3 − 1.5', near(p['jackLen'], (B - A) - 4.5, 1e-6), '%.3f' % p['jackLen'])
        report('j 3D 그물', J(ed, '__map3d.rv.tris()') > 1000 and J(ed, 'window.__pipes.river') > 0, '세모 %d' % J(ed, '__map3d.rv.tris()'))
        report('j 목록 한 줄 · 고른 줄 · 설정 칸', J(ed, "document.querySelectorAll('#rvList .shRow').length === 1 && !!document.querySelector('#rvList .shRow.sel') && !document.getElementById('rvSet').hidden"))
        ed.js('__t.look(0, %f, 55, 38, 70)' % ((A + B) / 2))
        shot(ed, SD, '01_추진_사각.png')
        ed.js('__t.look(0, %f, 14, 30, 100)' % A)
        shot(ed, SD, '01b_발진구_가까이.png')
        cut1 = J(ed, '__map3d.rv.lineQty(0).cut')
        report('j 추진 구간 = 터파기 없음 (토공 줄어듦)', cut1 < cut0 - 0.5 * (B - A), '%.1f → %.1f m³' % (cut0, cut1))
        # JACK 1127 «터파기와 겹쳐져서 이상»: 터파기 띠 끝 = 수직구 바깥 벽 (사각 = 내공 길이/2 + 0.4 · 발진 6 m · 도달 3 m) · 수직구 가운데엔 끝 없음
        ends = [e for pr in J(ed, '__map3d.rv.ends()') for e in pr]
        dmin = lambda s: min(math.hypot(e[0] - __p[0], e[1] - __p[1]) for e in ends for __p in [J(ed, '__rc.at(0, %f)' % s)])   # noqa: E731
        dl_, da_, cl_, ca_ = dmin(A - 3.4), dmin(B + 1.9), dmin(A), dmin(B)
        report('j 터파기 끝 = 수직구 바깥 벽 (발진 3.4 · 도달 1.9 m 바깥) · 가운데엔 없음', dl_ < 0.05 and da_ < 0.05 and cl_ > 1.0 and ca_ > 1.0,
               '바깥 벽 %.3f · %.3f / 가운데 %.2f · %.2f' % (dl_, da_, cl_, ca_))
        ed.js("__map3d.setMode('trench')")
        ed.js('__t.look(0, %f, 40, 42, 70)' % A)
        shot(ed, SD, '01c_추진_터파기보기.png')
        report('j 터파기 보기 · 구덩이 그림', J(ed, 'window.__pipes.pit') is True)
        ed.js("__map3d.setMode('area')")

        print('== j 수량 (하천 갈래)')
        ui_click(ed, inp, 'qkRiver')
        hp = lambda w, l: 2 * math.ceil(l / 1.8 - 1e-9) + 2 * math.ceil(w / 1.8 - 1e-9)   # noqa: E731
        want = {'jack': (B - A) - 4.5, 'shaft': 2, 'exc': 18 * dL + 7.5 * dA, 'hp': hp(3, 6) + hp(2.5, 3), 'lag': 18 * dL + 11 * dA}
        got = {k: J(ed, "__rc.qv('%s')" % k) for k in want}
        report('j 수량 표 = 손셈 (추진 · 수직구 · 굴착 · H-파일 · 토류판)', all(near(got[k], want[k], 0.05) for k in want), str({k: round(got[k], 2) for k in got}))
        shot(ed, SD, '02_수량_하천.png')
        lq = J(ed, '__map3d.rv.lineQty(0)')
        report('j 이 관 수량 = 표', near(lq['rv_exc'], got['exc'], 1e-6))

        print('== j 설정 바꾸기')
        cat_open(ed, inp, 'hd_river')
        ed.js("__rc.setIn('rv_cover', 3)")
        ed.frames(3)
        p2 = J(ed, '__rc.plan(0)')
        report('j 토피 3 → 바닥 1 m 내려감 · 깊이 1 m 늘어남', near(p2['zb'], p['zb'] - 1.0, 1e-6) and near(p2['shafts'][0]['depth'], p['shafts'][0]['depth'] + 1.0, 1e-6), '%.3f → %.3f' % (p['zb'], p2['zb']))
        want_exc = 18 * (dL + 1) + 7.5 * (dA + 1)
        report('j 굴착 바로 바뀜 (수량 표)', near(J(ed, "__rc.qv('exc')"), want_exc, 0.05), '%.2f / %.2f' % (J(ed, "__rc.qv('exc')"), want_exc))
        report('j 새 구간 기본값 = 지금 값 (이 PC)', J(ed, "JSON.parse(localStorage.getItem('rv_river:def')).jack.cover") == 3)
        ed.js("__rc.setIn('rv_ll', 8)")
        ed.frames(3)
        p3 = J(ed, '__rc.plan(0)')
        report('j 발진 길이 8 → 추진 길이 1 m 줄어듦 (가운데 기준)', near(p3['jackLen'], p2['jackLen'] - 1.0, 1e-6), '%.3f → %.3f' % (p2['jackLen'], p3['jackLen']))
        ed.js("__rc.setIn('rv_ll', 6)")
        ui_click(ed, inp, 'rvCirc')
        ed.frames(3)
        p4 = J(ed, '__rc.plan(0)')
        sd = sum(s['depth'] for s in p4['shafts'])
        report('j 원형 → 라이너 = 깊이 합 · H-파일 0', p4['q']['shape'] == 'circ' and near(J(ed, "__rc.qv('liner')"), sd, 0.05) and J(ed, "__rc.qv('hp')") == 0, '%.2f / %.2f' % (J(ed, "__rc.qv('liner')"), sd))
        report('j 원형 추진 길이 = 구간 − 3 − 2', near(p4['jackLen'], (B - A) - 5.0, 1e-6))
        ed.js('__t.look(0, %f, 55, 38, 70)' % ((A + B) / 2))
        img_c = shot(ed, SD, '03_추진_원형.png')
        ui_click(ed, inp, 'rvRev')
        ed.frames(3)
        p5 = J(ed, '__rc.plan(0)')
        report('j 발진 ⇄ → 발진 = 종점', near(p5['shafts'][0]['s'], B, 1e-6) and J(ed, 'window.RV_PIPES.lines[0].river[0].rev') is True)
        ui_click(ed, inp, 'rvRect')
        ed.frames(2)
        ui_click(ed, inp, 'rvSec')
        dl = J(ed, "(() => { const d = document.getElementById('rvDlg'), s = document.getElementById('rvSvg').innerHTML; return { hid: d.hidden, k: d.dataset.k, shaft: (s.match(/class=\"shaft\"/g) || []).length, deep: s.indexOf('깊이') >= 0, jack: s.indexOf('추진 ') >= 0, rows: document.querySelectorAll('#rvRows .k').length }; })()")
        report('j 종단 창 (수직구 둘 · 깊이 · 추진 · 표)', not dl['hid'] and dl['k'] == 'jack' and dl['shaft'] == 2 and dl['deep'] and dl['jack'] and dl['rows'] >= 7, str(dl))
        shot(ed, SD, '04_종단_추진.png')
        errs(ed, '추진')

        print('== o 횡단')
        oa, ob = round(L0 * 0.12, 1), round(L0 * 0.12 + 36.0, 1)
        ed.js("document.getElementById('rvX').click()")                # 종단 창이 찍을 자리를 가리지 않게
        ui_click(ed, inp, 'rvOpen')
        two_points(ed, inp, 0, oa, ob)
        pls = J(ed, '__rc.plans(0)')
        po = next((x for x in pls if x['k'] == 'open'), None)
        report('o 구간 둘 (추진 + 횡단) · 횡단 = 고른 줄', len(pls) == 2 and po is not None and J(ed, '__map3d.rv.cur().q.k') == 'open', str([(x['k'], x['a'], x['b']) for x in pls]))
        OA, OB = po['a'], po['b']
        q = po['q']
        report('o 기본값 (톤마대 · 1단계 · 토피 1.5 · 높이 1.5 · 간격 5)', q['dam'] == 'bag' and q['st'] == 1 and q['cover'] == 1.5 and q['h'] == 1.5 and q['w'] == 5, str(q))
        gmo = J(ed, '__rc.gmin(0, %f, %f)' % (OA, OB))
        report('o 관 윗면 = 하상 − 1.5 · 천단 = 하상 + 1.5', near(po['top'], gmo - 1.5, 0.05) and near(po['damTop'], gmo + 1.5, 0.05), '%.2f · %.2f' % (po['top'], po['damTop']))
        top = po['damTop']
        d1, d2 = J(ed, '__rc.dam(0, %f, %f, 5, 1, %f, 3)' % (OA, OB, top)), J(ed, '__rc.dam(0, %f, %f, 5, 2, %f, 3)' % (OA, OB, top))
        ui_click(ed, inp, 'qkRiver')
        if not J(ed, "!document.getElementById('qtyBox').hidden && document.getElementById('qtyBox').dataset.view === 'river'"):
            ui_click(ed, inp, 'qkRiver')
        gd, gb = J(ed, "__rc.qv('dam')"), J(ed, "__rc.qv('bag')")
        report('o 가물막이 길이 = 따로 셈 · ≈ 2 (b − a) + 4 w', near(gd, d1['len'] + d2['len'], 0.3) and abs(gd - (2 * (OB - OA) + 20)) < 0.03 * gd, '%.2f / %.2f / %.2f' % (gd, d1['len'] + d2['len'], 2 * (OB - OA) + 20))
        report('o 톤마대 개수 = 따로 셈 (다리마다 1.1 m 칸 · 피라미드)', abs(gb - (d1['bags'] + d2['bags'])) <= max(3, 0.03 * gb), '%d / %d' % (gb, d1['bags'] + d2['bags']))
        report('o 개착 횡단 = b − a', near(J(ed, "__rc.qv('open')"), OB - OA, 1e-6))
        cat_open(ed, inp, 'hd_river')
        ed.js('__t.look(0, %f, 60, 40, 60)' % ((OA + OB) / 2))
        img_s1 = shot(ed, SD, '05_횡단_톤마대_1단계.png')
        ed.js("__rc.setIn('rv_h', 2)")
        ed.frames(3)
        po2 = next(x for x in J(ed, '__rc.plans(0)') if x['k'] == 'open')
        report('o 높이 2 → 천단 0.5 올라감 · 톤마대 늘어남', near(po2['damTop'], top + 0.5, 1e-6) and J(ed, "__rc.qv('bag')") > gb, '%.2f → %.2f · %d → %d' % (top, po2['damTop'], gb, J(ed, "__rc.qv('bag')")))
        ed.js("__rc.setIn('rv_h', 1.5)")
        ui_click(ed, inp, 'rvSt2')
        ed.frames(4)
        img_s2 = shot(ed, SD, '06_횡단_톤마대_2단계.png')
        dd = np.abs(img_s1 - img_s2).sum(2)
        report('o 2단계 = 다른 반폭 (그림 다름)', J(ed, '__map3d.rv.cur().q.st') == 2 and (dd > 40).mean() > 0.005, '%.2f %%' % (100 * (dd > 40).mean()))
        ui_click(ed, inp, 'rvSheet')
        ed.frames(3)
        gs_ = J(ed, "__rc.qv('sheet')")
        report('o 시트파일 m² = 따로 셈 · 톤마대 0', abs(gs_ - (d1['sheet'] + d2['sheet'])) <= 0.02 * max(gs_, 1) and J(ed, "__rc.qv('bag')") == 0, '%.1f / %.1f' % (gs_, d1['sheet'] + d2['sheet']))
        ed.js("__rc.setIn('rv_emb', 5)")
        ed.frames(3)
        d1b, d2b = J(ed, '__rc.dam(0, %f, %f, 5, 1, %f, 5)' % (OA, OB, top)), J(ed, '__rc.dam(0, %f, %f, 5, 2, %f, 5)' % (OA, OB, top))
        report('o 근입 5 → 시트파일 m² 바뀜 (따로 셈)', abs(J(ed, "__rc.qv('sheet')") - (d1b['sheet'] + d2b['sheet'])) <= 0.02 * (d1b['sheet'] + d2b['sheet']), '%.1f' % J(ed, "__rc.qv('sheet')"))
        img_sh = shot(ed, SD, '07_횡단_시트파일_2단계.png')
        report('o 시트파일 그림 ≠ 톤마대', (np.abs(img_sh - img_s2).sum(2) > 40).mean() > 0.003)
        ui_click(ed, inp, 'rvSec')
        if J(ed, "document.getElementById('rvDlg').hidden"):
            ui_click(ed, inp, 'rvSec')
        dl = J(ed, "(() => { const s = document.getElementById('rvSvg').innerHTML; return { k: document.getElementById('rvDlg').dataset.k, st: s.indexOf('2단계') >= 0, h: s.indexOf('높이') >= 0 }; })()")
        report('o 종단 창 (횡단 · 단계 · 높이)', dl['k'] == 'open' and dl['st'] and dl['h'], str(dl))
        shot(ed, SD, '08_종단_횡단.png')
        ed.js("document.getElementById('rvX').click()")
        errs(ed, '횡단')

        print('== m 오른쪽 메뉴')
        sm = round(L0 * 0.75, 1)
        ed.js('__t.look(0, %f, 40, 50, 60)' % sm)
        ed.frames(3)
        q = J(ed, '__t.at(0, %f, true)' % sm)
        rclick(ed, q[0], q[1])
        okm = J(ed, "__map3d.ctx().items.indexOf('pRiver') >= 0")
        if okm:
            inp.click(*item_xy(ed, 'pRiver'))
            xy = item_xy(ed, 'pRiver:jack')
            okm = bool(xy)
            if xy:
                inp.click(*xy)
        sa_ = J(ed, '__map3d.SH.a')
        report('m 메뉴 → 추진 → 시점 = 누른 자리', okm and J(ed, '__map3d.SH.arm') == 'r:jack' and sa_ and abs(sa_['s'] - sm) < 0.6, str(sa_ and sa_['s']))
        inp.key('Escape', 'Escape', 27)
        ed.frames(2)
        report('m Esc = 취소', not J(ed, '__map3d.SH.arm'))
        errs(ed, '메뉴')

        print('== k 객체 누르기 → 카테고리 펼침 · 줄 열림 · 파란 테두리 · Delete 바로 지움 · Ctrl+Z (JACK 1127)')
        ed.js("window.RV_PIPES.lines[0].shore = [{ a: 5, b: 14, t: 'hpile' }, { a: 20, b: 23.5, t: 'sheet' }]; __map3d.rebuild()")
        ed.frames(3)
        TOAST = "(() => { const t = document.getElementById('toast'); return { txt: t.textContent, btn: !!t.querySelector('button') }; })()"
        for kind, cat, listid, nm in (('sh', 'shore', 'shList', '가시설'), ('rv', 'river', 'rvList', '하천')):
            objs = J(ed, "__map3d.obj.list('%s')" % kind)
            report('k %s 객체 있음' % nm, len(objs) >= 1, str(objs))
            if not objs:
                continue
            src, idx = objs[0]
            n0 = len(objs)
            ed.js("__map3d.catSet('shore', false); __map3d.catSet('river', false); document.getElementById('side').hidden = true")
            ed.js('__t.look(%d, %f, 90, 60, 60)' % (src, 16))
            ed.frames(4)
            xy = J(ed, "__map3d.obj.xy('%s', %d, %d)" % (kind, src, idx))
            inp.click(xy[0], xy[1])
            ed.frames(4)
            st = J(ed, '__map3d.obj.st()')
            sel = J(ed, "!!document.querySelector('#%s .shRow.sel')" % listid)
            report('k %s 누르기 → 선택 · 카테고리 펼침 · 오른쪽 칸 띄움 · 줄 파란 테두리' % nm,
                   st['k'] == kind and J(ed, "__map3d.catIs('%s')" % cat) and not J(ed, "document.getElementById('side').hidden") and sel, json.dumps(st))
            shot(ed, SD, '60_객체선택_%s.png' % kind)
            inp.click(200, 860)
            ed.frames(3)
            report('k %s 빈 곳 누르기 → 선택 풀림' % nm, J(ed, '__map3d.obj.st().k') == '' and not J(ed, "!!document.querySelector('#%s .shRow.sel')" % listid))
            inp.click(xy[0], xy[1])
            ed.frames(3)
            inp.key('Escape', 'Escape', 27)
            report('k %s Esc → 선택 풀림' % nm, J(ed, '__map3d.obj.st().k') == '')
            inp.click(xy[0], xy[1])
            ed.frames(3)
            ed.js("(() => { const i = document.createElement('input'); i.id = 'tmpIn'; document.body.appendChild(i); i.focus(); })()")
            inp.key('Delete', 'Delete', 46)
            report('k %s 입력 칸 안 Delete → 무시' % nm, J(ed, "__map3d.obj.count('%s')" % kind) == n0)
            ed.js("document.getElementById('tmpIn').remove()")
            inp.key('Delete', 'Delete', 46)
            ed.frames(3)
            tt = J(ed, TOAST)
            report('k %s Delete 한 번 → 개수 -1 · 확인 단추 없음 · 알림 «Ctrl+Z»' % nm,
                   J(ed, "__map3d.obj.count('%s')" % kind) == n0 - 1 and not tt['btn'] and 'Ctrl+Z' in tt['txt'], json.dumps(tt, ensure_ascii=False))
            inp.key('z', 'KeyZ', 90, 2)
            ed.frames(4)
            report('k %s Ctrl+Z → 되살아남 (목록 · 3D)' % nm, J(ed, "__map3d.obj.count('%s')" % kind) == n0 and J(ed, "document.querySelectorAll('#%s .shRow').length" % listid) == n0)
        ed.js("delete window.RV_PIPES.lines[0].shore; __map3d.catSet('shore', false); __map3d.catSet('river', true); __map3d.rebuild()")
        errs(ed, '객체 누르기')

        print('== d 지우기 · 저장 · 다시 열기')
        n0 = len(calls(ed, 'save_pipes'))
        ed.js("(() => { const r = [...document.querySelectorAll('#rvList .shRow')].find((x) => x.dataset.k === 'jack'); r.querySelector('.shDel').scrollIntoView({ block: 'nearest' }); })()")
        ed.frames(2)
        xy = J(ed, "(() => { const r = [...document.querySelectorAll('#rvList .shRow')].find((x) => x.dataset.k === 'jack').querySelector('.shDel').getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; })()")
        inp.click(*xy)
        ed.frames(2)
        rv = J(ed, 'window.RV_PIPES.lines[0].river')
        report('d 목록 × → 추진 지움 (횡단 남음)', len(rv) == 1 and rv[0]['k'] == 'open', str([x['k'] for x in rv]))
        ed.js("document.getElementById('bSave').click()")              # JACK 1127: 윗줄 [저장] 을 눌러야 씀
        ed.wait_js("__host.calls.filter((c) => c.name === 'save_pipes').length > %d" % n0, 5)
        sv = calls(ed, 'save_pipes')
        txt = sv[-1] if sv else ''
        report('d 저장 (save_pipes 글에 river)', bool(txt) and '"river"' in txt and '"open"' in txt, '%d 번' % len(sv))
        if txt:
            open(os.path.join(out, 'data', 'pipes_river.js'), 'w', encoding='utf-8').write(txt)
        page(ed, base + 'index_river_host.html')
        rv = J(ed, 'window.RV_PIPES.lines[0].river || null')
        report('d 다시 열면 남아 있음 (시트파일 · 2단계 · 근입 5)', bool(rv) and len(rv) == 1 and rv[0]['dam'] == 'sheet' and rv[0]['st'] == 2 and rv[0]['emb'] == 5, str(rv))
        report('d 다시 열기 3D', J(ed, '__map3d.rv.tris()') > 100)
        errs(ed, '다시 열기')

        print('== b 브라우저 (웹 공유 · 프로그램 밖)')
        page(ed, base + 'index_river.html')
        report('b 프로그램 없음', J(ed, '!window.pywebview'))
        cat_open(ed, inp, 'hd_river')
        report('b 하천 카테고리 · 목록 한 줄', J(ed, "!document.querySelector('[data-cat=river]').hidden && document.querySelectorAll('#rvList .shRow').length === 1"))
        ed.js("document.querySelector('#rvList .shGo').click()")
        ed.frames(4)
        report('b 3D 그물', J(ed, '__map3d.rv.tris()') > 100)
        ui_click(ed, inp, 'rvSec')
        report('b 종단 창', J(ed, "!document.getElementById('rvDlg').hidden && document.getElementById('rvSvg').innerHTML.length > 500"))
        shot(ed, SD, '09_브라우저.png')
        errs(ed, '브라우저')
    finally:
        try:
            ed.__exit__(None, None, None)
        finally:
            srv.shutdown()
    print()
    if R['errs']:
        print('JS 오류:')
        for w, e in R['errs'][:10]:
            print('  ', w, e)
    print('모두 통과' if R['ok'] else '실패: ' + ', '.join(R['fails']))
    print('그림:', SD)
    return 0 if R['ok'] else 1


if __name__ == '__main__':
    sys.exit(main())
