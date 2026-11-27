"""360 로드뷰 화면 · 하천 추진 · 횡단 시험 (JACK 1126 «관로 부가 기능은 360 에도 같게 · 360 은 불러온 관로 기준»).
make_synth --trench 폴더를 복사해 화면 없는 Edge (cdp.py) 로 열고 진짜 마우스 · 키로 몬다. 셈 (viewer/river.js) 은 node 시험
(check_river_calc.js) 이 손셈과 견주고, 여기서는 360 화면에서 같은 셈이 나오는지 (화면 안 따로 셈) · 그림 · 저장 · 다시 열기를 본다.

    python -X utf8 check_river360.py --src <make_synth --trench 폴더> --work <빈 시험 폴더> --shots <그림 폴더>

  j  추진: [추진] → 360 에서 T1 위 시점 → 지도에서 종점 → 구간 (측점 · 시점 = 발진구) · 안내 «시점 (발진구)» → «종점 (도달구)» ·
     하상 = 360 땅 높이 (groundAt) 가장 낮은 곳 (따로 셈) · 관 윗면 = 하상 − 2 · 바닥 = 관 바닥 − 0.5 · 깊이 · 추진 길이 · 굴착 · H-파일 · 토류판 = 손셈 ·
     추진 구간 = 터파기 없음 (토공 줄어듦) · 터파기 보기 3D 그물 · 수직구 구멍 · 그림 바뀜 · 토피 3 → 바닥 1 m · 발진 길이 8 → 추진 1 m 줄어듦 ·
     원형 → 라이너 = 깊이 합 · 발진 ⇄ · 종단 창 (수직구 둘 · 깊이 · 추진 · 표) · Esc = 찍던 것 취소 · 창 닫기
  o  횡단: [횡단] → 지도 두 점 → 기본값 · 관 윗면 = 하상 − 1.5 · 천단 = 하상 + 1.5 · 둑 길이 · 톤마대 = 따로 셈 · 개착 = b − a ·
     높이 2 → 천단 0.5 · 톤마대 늘어남 · 2단계 그림 다름 · 시트파일 m² = 따로 셈 · 근입 5 → 바뀜 · 종단 창 (단계 · 높이) · 영역 표현 땅 위 테두리
  p  3D 매핑 화면에서 만든 구간 (pipes.js 에 river · 3D 가 쓰는 꼴) → 360 에서 읽힘 · 목록 · 셈 · 3D · 수량
  d  지우기 (목록 ×) · 저장 (save_pipes 글에 river) → 그 글로 다시 열면 남아 있음
  b  브라우저 (프로그램 밖 · 웹 공유): 저장된 구간 보임 · 3D · 종단 창
  360 칸 1019×820 · 모든 쪽 JS 오류 0
"""
import argparse
import json
import math
import os
import pathlib
import shutil
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import check_edit as ce  # noqa: E402
import check_interact as ci  # noqa: E402
import check_view as cv  # noqa: E402
import make_synth as ms  # noqa: E402
from cdp import Edge  # noqa: E402

GZ = ms.GZ
T1 = [(-40.0, 3.0), (14.0, 3.0), (40.0, 16.0)]      # make_synth TR_LINES[0] (D300)
T1_D = 0.3
V_J = ('j', '0001', 275, -52, 75)                   # T1 서쪽 (추진 구간 e −16 ~ 0)
V_O = ('o', '0003', 55, -45, 85)                    # T1 둘째 마디 (횡단 구간)
R = {'ok': True, 'fails': [], 'errs': []}
NL = chr(10)

# 화면 안에서 따로 셈 (river.js 를 쓰지 않음 · 땅 = 화면과 같은 땅 __rv360.rv.ground · 관 줄 = RV_PIPES 원래 점)
CALC_JS = r"""
window.__rc = {
  at(src, s) {
    const P = window.RV_PIPES.lines[src].pts; let acc = 0;
    for (let i = 1; i < P.length; i++) {
      const dx = P[i][0] - P[i - 1][0], dy = P[i][1] - P[i - 1][1], l = Math.hypot(dx, dy);
      if (s <= acc + l + 1e-9 || i === P.length - 1) { const t = l > 0 ? Math.min(1, Math.max(0, (s - acc) / l)) : 0; return [P[i - 1][0] + dx * t, P[i - 1][1] + dy * t, dx / (l || 1), dy / (l || 1)]; }
      acc += l;
    }
  },
  g(x, y) { return __rv360.rv.ground(x, y); },
  gmin(src, a, b) { let m = Infinity; for (let s = a; s <= b + 1e-9; s += 0.05) { const p = this.at(src, s), g = this.g(p[0], p[1]); if (g < m) m = g; } return m; },
  gs(src, s) { const p = this.at(src, s); return this.g(p[0], p[1]); },
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
  dam(src, a, b, w, st, top, emb) {
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
  plans(src) { return __rv360.rv.plans(src).map((p) => ({ k: p.k, a: p.a, b: p.b, q: p.q, top: p.top, zc: p.zc, zb: p.zb, gMin: p.gMin, damTop: p.damTop, jackLen: p.jackLen,
    shafts: p.shafts ? p.shafts.map((s) => ({ s: s.s, g: s.g, depth: s.depth, w: s.w, l: s.l, shape: s.shape })) : null })); },
  qv(k) { const e = document.getElementById('q_rv_' + k); return e ? Number(e.dataset.v) : NaN; },
  setIn(id, v) { const e = document.getElementById(id); e.value = String(v); e.dispatchEvent(new Event('input')); e.dispatchEvent(new Event('change')); return true; },
};
"""


def report(name, ok, text=''):
    print('   %-52s %s  %s' % (name, text, '통과' if ok else '실패'))
    sys.stdout.flush()
    R['ok'] &= bool(ok)
    if not ok:
        R['fails'].append(name)
    return bool(ok)


def J(ed, expr):
    return json.loads(ed.js('JSON.stringify(%s)' % expr))


def errs(ed, where):
    e = cv.errors(ed)
    R['errs'] += [(where, str(x)[:200]) for x in e]
    ed.exceptions.clear()
    ed.console.clear()
    return report('JS 오류 (%s)' % where, not e, '%d 줄' % len(e))


def near(a, b, tol):
    return a is not None and b is not None and abs(a - b) <= tol


def t1_point(s):
    acc = 0.0
    for a, b in zip(T1[:-1], T1[1:]):
        a, b = np.array(a), np.array(b)
        L = float(np.linalg.norm(b - a))
        if s <= acc + L:
            return a + (b - a) * ((s - acc) / L)
        acc += L
    return np.array(T1[-1])


def setup(src, work, extra_river=None):
    """시험 폴더: --trench 복사 + 저장소 보기 화면 (river.js 포함) · extra_river = T1 에 넣을 하천 구간 (3D 가 쓴 꼴)."""
    shutil.rmtree(work, ignore_errors=True)
    shutil.copytree(src, work)
    for dst in ('로드뷰.html', 'index.html'):
        shutil.copyfile(os.path.join(REPO, 'viewer', 'index.html'), os.path.join(work, dst))
    for f in ('app.js', 'style.css', 'river.js', 'cost.js'):     # cost.js = 개략공사비 셈 (1127)
        shutil.copyfile(os.path.join(REPO, 'viewer', f), os.path.join(work, 'viewer', f))
    cj = os.path.join(work, 'data', 'calib.js')
    if os.path.exists(cj):
        os.remove(cj)
    if extra_river is not None:
        pj = os.path.join(work, 'data', 'pipes.js')
        t = open(pj, encoding='utf-8').read()
        P = json.loads(t[t.index('{'):t.rindex('}') + 1])
        next(l for l in P['lines'] if l.get('label') == 'T1')['river'] = extra_river
        with open(pj, 'w', encoding='utf-8', newline='\n') as f:
            f.write('window.RV_PIPES = ' + json.dumps(P, ensure_ascii=False, indent=1) + ';\n')
    return ci.prepare(work, host=True)


class Page:
    def __init__(self, ed, work, host_page):
        self.ed, self.work, self.host_page = ed, work, host_page

    def url(self, host, view, mode='area'):
        page = self.host_page if host else '로드뷰.html'
        return pathlib.Path(self.work, page).resolve().as_uri() + '?t=none&' + ce.q_of(view, '&mode=' + mode)

    def load(self, host, view, mode='area', clear=False):
        if clear:
            self.ed.goto(self.url(host, view, mode))
            self.ed.wait_js(cv.READY, timeout=60)
            self.ed.js('localStorage.clear()')
        self.ed.goto(self.url(host, view, mode))
        ok = self.ed.wait_js(cv.READY, timeout=60)
        self.ed.frames(5)
        self.ed.js(CALC_JS)
        return ok


def cat_open(ed, inp, head):
    sec = J(ed, "document.getElementById('%s').closest('.cat').dataset.cat" % head)
    if not J(ed, "__rv360.catIs('%s')" % sec):
        inp.el_click(head)
        ed.frames(3)


def click(ed, inp, eid):
    inp.el_click(eid)
    ed.frames(3)


def view_png(ed, png):
    """360 칸만 (WebGL + 겹쳐 그림) · 단추 · 안내 숨김."""
    ed.js("document.querySelectorAll('#viewPane > :not(#gl):not(#ov)').forEach((e) => { e.style.visibility = 'hidden'; })")
    ed.frames(3)
    ed.screenshot(png)
    ed.js("document.querySelectorAll('#viewPane > :not(#gl):not(#ov)').forEach((e) => { e.style.visibility = ''; })")
    r = ce.rect(ed, 'ov')
    a = np.asarray(Image.open(png).convert('RGB')).astype(float)
    l, t = int(round(r['left'])), int(round(r['top']))
    return a[t:t + int(round(r['height'])), l:l + int(round(r['width']))]


def shot(ed, SD, name):
    p = os.path.join(SD, name)
    ed.frames(3)
    ed.screenshot(p)
    return p


def diff(a, b):
    return float((np.abs(a - b).sum(2) > 40).mean())


def chip(ed):
    return J(ed, "{show: document.getElementById('shChip').classList.contains('show'), tip: document.getElementById('shTip').textContent, name: document.getElementById('shName').textContent,"
                 " arm: __rv360.SH.arm, a: __rv360.SH.a && __rv360.SH.a.s}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--src', required=True)
    ap.add_argument('--work', required=True)
    ap.add_argument('--shots', required=True)
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    src, work, SD = os.path.abspath(a.src), os.path.abspath(a.work), os.path.abspath(a.shots)
    os.makedirs(SD, exist_ok=True)
    host_page = setup(src, work)
    ed = Edge(os.path.join(work + '_prof'), cv.WIN).__enter__()
    ed.net(block=ci.BLOCK)
    pg = Page(ed, work, host_page)
    inp = ce.Inp(ed)
    try:
        # ------------------------------------------------------------ 열기
        print('== 열기 (찍기 · 설정 = 프로그램 밖 쪽 · 고친 것 = 이 브라우저 → 다시 열어도 남음 · 저장 = d 에서 프로그램 흉내)')
        report('불러옴', pg.load(False, V_J, 'trench', clear=True))
        report('하천 셈 읽힘 (river.js) · 카테고리 · 수량 갈래 보임', J(ed, "__rv360.rv.ok && !document.querySelector('[data-cat=river]').hidden && !document.getElementById('qkRiver').hidden"))
        ov = J(ed, "[Math.round(document.getElementById('ov').getBoundingClientRect().width), Math.round(document.getElementById('ov').getBoundingClientRect().height)]")
        report('360 칸 1019×820', ov == [1019, 820], json.dumps(ov))
        src0 = J(ed, "window.RV_PIPES.lines.findIndex((l) => l.label === 'T1')")
        cut0 = J(ed, '__rv360.lineQty(%d).cut' % src0)
        img0 = view_png(ed, os.path.join(SD, '_j0.png'))           # 하천 없음 (터파기 보기)
        errs(ed, '열기')

        # ------------------------------------------------------------ j 추진
        print('== j 추진 (360 시점 · 지도 종점)')
        pg.load(False, V_J, 'area')
        cat_open(ed, inp, 'hd_river')
        click(ed, inp, 'rvJack')
        c0 = chip(ed)
        report('j [추진] → 켜짐 · 안내 «시점 (발진구)»', c0['show'] and c0['arm'] == 'r:jack' and c0['tip'] == '시점 (발진구)' and c0['name'] == '하천 추진'
               and J(ed, "document.getElementById('rvJack').classList.contains('on')"), json.dumps(c0, ensure_ascii=False))
        cc = ce.Cam(ed, V_J)
        p = cc.proj([-16.0, 3.0, GZ])
        inp.click(p[0], p[1])
        c1 = chip(ed)
        report('j 360 관 위 누름 → «종점 (도달구)» (시점 측점 ≈ 24)', c1['tip'] == '종점 (도달구)' and near(c1['a'], 24.0, 0.1), json.dumps(c1, ensure_ascii=False))
        inp.key('Escape', 'Escape', 27)
        c2 = chip(ed)
        report('j Esc → 찍던 것 취소 (구간 없음)', not c2['show'] and not c2['arm'] and not J(ed, 'window.RV_PIPES.lines[%d].river || null' % src0), json.dumps(c2, ensure_ascii=False))
        click(ed, inp, 'rvJack')
        inp.click(p[0], p[1])
        mp = ce.MapTf(ed)
        q = t1_point(40.0)
        inp.click(*mp.en_scr(q[0], q[1]))
        rv = J(ed, 'window.RV_PIPES.lines[%d].river || null' % src0)
        okj = bool(rv) and len(rv) == 1 and rv[0]['k'] == 'jack' and near(rv[0]['a'], 24.0, 0.1) and near(rv[0]['b'], 40.0, 0.4) and rv[0]['rev'] is False
        report('j 360 시점 + 지도 종점 → 구간 (측점 · 시점 = 발진구)', okj, str(rv and {k: rv[0][k] for k in ('k', 'a', 'b', 'shape', 'rev')}))
        report('j 안내 닫힘 · 목록 한 줄 (고른 줄) · 설정 칸', not chip(ed)['show'] and J(ed, "document.querySelectorAll('#rvList .shRow').length === 1 && !!document.querySelector('#rvList .shRow.sel') && !document.getElementById('rvSet').hidden"))
        pl = J(ed, "__rc.plans(%d)[0]" % src0)
        A, B = pl['a'], pl['b']
        gmin = J(ed, '__rc.gmin(%d, %f, %f)' % (src0, A, B))
        report('j 하상 = 360 땅 높이 가장 낮은 곳 (따로 셈)', near(pl['gMin'], gmin, 0.02), '%.3f / %.3f' % (pl['gMin'], gmin))
        report('j 관 윗면 = 하상 − 2 · 바닥 = 관 바닥 − 0.5', near(pl['zc'] + (T1_D + 0.2) / 2, pl['gMin'] - 2.0, 1e-6) and near(pl['zb'], pl['gMin'] - 2.0 - (T1_D + 0.2) - 0.5, 1e-6), '윗면 %.3f · 바닥 %.3f' % (pl['top'], pl['zb']))
        dL, dA = J(ed, '__rc.gs(%d, %f)' % (src0, A)) - pl['zb'], J(ed, '__rc.gs(%d, %f)' % (src0, B)) - pl['zb']
        report('j 발진 · 도달 깊이 = 그 자리 땅 − 바닥', near(pl['shafts'][0]['depth'], dL, 0.01) and near(pl['shafts'][1]['depth'], dA, 0.01), '%.2f · %.2f' % (dL, dA))
        report('j 추진 길이 = 구간 − 3 − 1.5', near(pl['jackLen'], (B - A) - 4.5, 1e-6), '%.3f' % pl['jackLen'])
        cut1 = J(ed, '__rv360.lineQty(%d).cut' % src0)
        report('j 추진 구간 = 터파기 없음 (토공 줄어듦)', cut1 < cut0 - 0.5 * (B - A), '%.1f → %.1f m³' % (cut0, cut1))
        # JACK 1127: 터파기 띠 끝 = 수직구 바깥 벽 (발진 6/2 + 0.4 · 도달 3/2 + 0.4) · 수직구 가운데엔 끝 없음
        ends = [e for pr in J(ed, '__rv360.rv.ends()') for e in pr]
        dmin = lambda s: min(math.hypot(e[0] - __p[0], e[1] - __p[1]) for e in ends for __p in [J(ed, '__rc.at(%d, %f)' % (src0, s))])   # noqa: E731
        dl_, da_, cl_, ca_ = dmin(A - 3.4), dmin(B + 1.9), dmin(A), dmin(B)
        report('j 터파기 끝 = 수직구 바깥 벽 (발진 3.4 · 도달 1.9 m 바깥) · 가운데엔 없음', dl_ < 0.05 and da_ < 0.05 and cl_ > 1.0 and ca_ > 1.0,
               '바깥 벽 %.3f · %.3f / 가운데 %.2f · %.2f' % (dl_, da_, cl_, ca_))
        shot(ed, SD, '01_추진_영역_테두리.png')
        errs(ed, 'j 찍기')

        print('== j 터파기 보기 (3D)')
        pg.load(False, V_J, 'trench')
        tris, holes = J(ed, '__rv360.rv.tris()'), J(ed, '__rv360.rv.holes()')
        img1 = view_png(ed, os.path.join(SD, '02_추진_터파기_사각.png'))
        d1 = diff(img0, img1)
        report('j 3D 그물 · 수직구 구멍 · 그림 바뀜', tris > 1000 and holes >= 4 and d1 > 0.01, '세모 %d · 구멍 %d · 바뀐 화소 %.1f %%' % (tris, holes, 100 * d1))
        # 수량 (하천 갈래)
        cat_open(ed, inp, 'hd_qty')
        click(ed, inp, 'qkRiver')
        hp = lambda w, l: 2 * math.ceil(l / 1.8 - 1e-9) + 2 * math.ceil(w / 1.8 - 1e-9)   # noqa: E731
        want = {'jack': (B - A) - 4.5, 'shaft': 2, 'exc': 18 * dL + 7.5 * dA, 'hp': hp(3, 6) + hp(2.5, 3), 'lag': 18 * dL + 11 * dA}
        got = {k: J(ed, "__rc.qv('%s')" % k) for k in want}
        vis_rows = J(ed, "[...document.querySelectorAll('#qTab .ql')].filter((e) => !e.hidden).map((e) => e.textContent)")
        report('j 수량 «하천» 표 = 손셈 (추진 · 수직구 · 굴착 · H-파일 · 토류판)', all(near(got[k], want[k], 0.05) for k in want) and len(vis_rows) == 10 and vis_rows[0].startswith('추진 길이'),
               str({k: round(got[k], 2) for k in got}))
        shot(ed, SD, '03_수량_하천.png')
        click(ed, inp, 'qkRiver')
        # 설정 바꾸기
        cat_open(ed, inp, 'hd_river')
        ed.js('__rv360.rv.pick(%d, %f, %f)' % (src0, A, B))          # 다시 열면 고른 구간 없음 → 목록에서 고른 것처럼 (사진은 그대로)
        ed.frames(2)
        report('j 고르면 설정 칸 (토피 2 · 사각 켜짐)', J(ed, "!document.getElementById('rvSet').hidden && document.getElementById('rv_cover').value === '2' && document.getElementById('rvRect').classList.contains('on')"))
        ed.js("__rc.setIn('rv_cover', 3)")
        ed.frames(3)
        p2 = J(ed, '__rc.plans(%d)[0]' % src0)
        report('j 토피 3 → 바닥 1 m 내려감 · 깊이 1 m 늘어남', near(p2['zb'], pl['zb'] - 1.0, 1e-6) and near(p2['shafts'][0]['depth'], pl['shafts'][0]['depth'] + 1.0, 1e-6), '%.3f → %.3f' % (pl['zb'], p2['zb']))
        report('j 굴착 바로 바뀜 (이 관 수량)', near(J(ed, '__rv360.lineQty(%d).rv_exc' % src0), 18 * (dL + 1) + 7.5 * (dA + 1), 0.05))
        report('j 새 구간 기본값 = 지금 값 (이 PC · 3D 와 같은 열쇠)', J(ed, "JSON.parse(localStorage.getItem('rv_river:def')).jack.cover") == 3)
        ed.js("__rc.setIn('rv_ll', 8)")
        ed.frames(3)
        p3 = J(ed, '__rc.plans(%d)[0]' % src0)
        report('j 발진 길이 8 → 추진 길이 1 m 줄어듦', near(p3['jackLen'], p2['jackLen'] - 1.0, 1e-6), '%.3f → %.3f' % (p2['jackLen'], p3['jackLen']))
        ed.js("__rc.setIn('rv_ll', 6)")
        click(ed, inp, 'rvCirc')
        p4 = J(ed, '__rc.plans(%d)[0]' % src0)
        lq = J(ed, '__rv360.lineQty(%d)' % src0)
        sd = sum(s['depth'] for s in p4['shafts'])
        report('j 원형 → 라이너 = 깊이 합 · H-파일 0 · 추진 = 구간 − 3 − 2', p4['q']['shape'] == 'circ' and near(lq['rv_liner'], sd, 0.05) and lq['rv_hp'] == 0 and near(p4['jackLen'], (B - A) - 5.0, 1e-6),
               '%.2f / %.2f' % (lq['rv_liner'], sd))
        img_c = view_png(ed, os.path.join(SD, '04_추진_터파기_원형.png'))
        report('j 원형 그림 ≠ 사각', diff(img1, img_c) > 0.003, '%.2f %%' % (100 * diff(img1, img_c)))
        click(ed, inp, 'rvRev')
        p5 = J(ed, '__rc.plans(%d)[0]' % src0)
        report('j 발진 ⇄ → 발진 = 종점', near(p5['shafts'][0]['s'], B, 1e-6) and J(ed, 'window.RV_PIPES.lines[%d].river[0].rev' % src0) is True)
        click(ed, inp, 'rvRect')
        click(ed, inp, 'rvSec')
        dl = J(ed, "(() => { const d = document.getElementById('rvDlg'), s = document.getElementById('rvSvg').innerHTML; return { hid: d.hidden, k: d.dataset.k, shaft: (s.match(/class=\"shaft\"/g) || []).length, deep: s.indexOf('깊이') >= 0, jack: s.indexOf('추진 ') >= 0, rows: document.querySelectorAll('#rvRows .k').length }; })()")
        report('j 종단 창 (수직구 둘 · 깊이 · 추진 · 표)', not dl['hid'] and dl['k'] == 'jack' and dl['shaft'] == 2 and dl['deep'] and dl['jack'] and dl['rows'] >= 7, str(dl))
        shot(ed, SD, '05_종단_추진.png')
        inp.key('Escape', 'Escape', 27)
        report('j Esc → 종단 창 닫힘', J(ed, "document.getElementById('rvDlg').hidden"))
        errs(ed, '추진')

        # ------------------------------------------------------------ o 횡단
        print('== o 횡단 (지도 두 점)')
        pg.load(False, V_O, 'trench')
        cat_open(ed, inp, 'hd_river')
        img_o0 = view_png(ed, os.path.join(SD, '_o0.png'))
        click(ed, inp, 'rvOpen')
        report('o [횡단] → 안내 «시점»', chip(ed)['tip'] == '시점' and chip(ed)['name'] == '하천 횡단')
        mp = ce.MapTf(ed)
        for s in (60.0, 75.0):
            q = t1_point(s)
            inp.click(*mp.en_scr(q[0], q[1]))
        pls = J(ed, '__rc.plans(%d)' % src0)
        po = next((x for x in pls if x['k'] == 'open'), None)
        report('o 구간 둘 (추진 + 횡단) · 횡단 = 고른 줄', len(pls) == 2 and po is not None and J(ed, '__rv360.rv.cur().q.k') == 'open', str([(x['k'], x['a'], x['b']) for x in pls]))
        OA, OB, qo = po['a'], po['b'], po['q']
        report('o 측점 (지도 0.3 m)', near(OA, 60.0, 0.4) and near(OB, 75.0, 0.4), '%.2f ~ %.2f' % (OA, OB))
        report('o 기본값 (톤마대 · 1단계 · 토피 1.5 · 높이 1.5 · 간격 5)', qo['dam'] == 'bag' and qo['st'] == 1 and qo['cover'] == 1.5 and qo['h'] == 1.5 and qo['w'] == 5, str(qo))
        gmo = J(ed, '__rc.gmin(%d, %f, %f)' % (src0, OA, OB))
        report('o 관 윗면 = 하상 − 1.5 · 천단 = 하상 + 1.5', near(po['top'], gmo - 1.5, 0.02) and near(po['damTop'], gmo + 1.5, 0.02), '%.2f · %.2f' % (po['top'], po['damTop']))
        top = po['damTop']
        d1_, d2_ = J(ed, '__rc.dam(%d, %f, %f, 5, 1, %f, 3)' % (src0, OA, OB, top)), J(ed, '__rc.dam(%d, %f, %f, 5, 2, %f, 3)' % (src0, OA, OB, top))
        lq = J(ed, '__rv360.lineQty(%d)' % src0)
        report('o 가물막이 길이 = 따로 셈 ≈ 2 (b − a) + 4 w', near(lq['rv_dam'], d1_['len'] + d2_['len'], 0.3) and abs(lq['rv_dam'] - (2 * (OB - OA) + 20)) < 0.03 * lq['rv_dam'],
               '%.2f / %.2f' % (lq['rv_dam'], d1_['len'] + d2_['len']))
        report('o 톤마대 개수 = 따로 셈 (1.1 m 칸 · 0.8 m 단 · 피라미드)', abs(lq['rv_bag'] - (d1_['bags'] + d2_['bags'])) <= max(3, 0.03 * lq['rv_bag']), '%d / %d' % (lq['rv_bag'], d1_['bags'] + d2_['bags']))
        report('o 개착 횡단 = b − a', near(lq['rv_open'], OB - OA, 1e-6))
        tr = J(ed, '__rv360.rv.tris()')
        img_s1 = view_png(ed, os.path.join(SD, '06_횡단_톤마대_1단계.png'))
        report('o 3D 가물막이 (그림 바뀜)', tr > 2000 and diff(img_o0, img_s1) > 0.005, '세모 %d · %.1f %%' % (tr, 100 * diff(img_o0, img_s1)))
        ed.js("__rc.setIn('rv_h', 2)")
        ed.frames(3)
        po2 = next(x for x in J(ed, '__rc.plans(%d)' % src0) if x['k'] == 'open')
        report('o 높이 2 → 천단 0.5 올라감 · 톤마대 늘어남', near(po2['damTop'], top + 0.5, 1e-6) and J(ed, '__rv360.lineQty(%d).rv_bag' % src0) > lq['rv_bag'])
        ed.js("__rc.setIn('rv_h', 1.5)")
        click(ed, inp, 'rvSt2')
        img_s2 = view_png(ed, os.path.join(SD, '07_횡단_톤마대_2단계.png'))
        report('o 2단계 = 다른 반폭 (그림 다름)', J(ed, '__rv360.rv.cur().q.st') == 2 and diff(img_s1, img_s2) > 0.003, '%.2f %%' % (100 * diff(img_s1, img_s2)))
        click(ed, inp, 'rvSheet')
        gs_ = J(ed, '__rv360.lineQty(%d).rv_sheet' % src0)
        report('o 시트파일 m² = 따로 셈 · 톤마대 0', abs(gs_ - (d1_['sheet'] + d2_['sheet'])) <= 0.02 * max(gs_, 1) and J(ed, '__rv360.lineQty(%d).rv_bag' % src0) == 0, '%.1f / %.1f' % (gs_, d1_['sheet'] + d2_['sheet']))
        ed.js("__rc.setIn('rv_emb', 5)")
        ed.frames(3)
        d1b, d2b = J(ed, '__rc.dam(%d, %f, %f, 5, 1, %f, 5)' % (src0, OA, OB, top)), J(ed, '__rc.dam(%d, %f, %f, 5, 2, %f, 5)' % (src0, OA, OB, top))
        report('o 근입 5 → 시트파일 m² 바뀜 (따로 셈)', abs(J(ed, '__rv360.lineQty(%d).rv_sheet' % src0) - (d1b['sheet'] + d2b['sheet'])) <= 0.02 * (d1b['sheet'] + d2b['sheet']))
        img_sh = view_png(ed, os.path.join(SD, '08_횡단_시트파일_2단계.png'))
        report('o 시트파일 그림 ≠ 톤마대', diff(img_sh, img_s2) > 0.003, '%.2f %%' % (100 * diff(img_sh, img_s2)))
        click(ed, inp, 'rvSec')
        dl = J(ed, "(() => { const s = document.getElementById('rvSvg').innerHTML; return { hid: document.getElementById('rvDlg').hidden, k: document.getElementById('rvDlg').dataset.k, st: s.indexOf('2단계') >= 0, h: s.indexOf('높이') >= 0 }; })()")
        report('o 종단 창 (횡단 · 단계 · 높이)', not dl['hid'] and dl['k'] == 'open' and dl['st'] and dl['h'], str(dl))
        shot(ed, SD, '09_종단_횡단.png')
        click(ed, inp, 'rvX')
        pg.load(False, V_O, 'area')
        shot(ed, SD, '10_횡단_영역_테두리.png')
        errs(ed, '횡단')
        # 지금 관로 (이 브라우저에 고친 것) 를 파일로 = 프로그램이 저장해 둔 것처럼 (d · b 가 그 파일로 염)
        with open(os.path.join(work, 'data', 'pipes.js'), 'w', encoding='utf-8', newline=NL) as f:
            f.write('window.RV_PIPES = ' + ed.js('JSON.stringify(window.RV_PIPES)') + ';' + NL)

        # ------------------------------------------------------------ d 지우기 · 저장 · 다시 열기
        print('== d 지우기 · 저장 · 다시 열기 (프로그램 흉내)')
        pg.load(True, V_J, 'area', clear=True)
        report('d 파일 구간 둘 읽힘', len(J(ed, 'window.RV_PIPES.lines[%d].river || []' % src0)) == 2)
        cat_open(ed, inp, 'hd_river')
        n0 = len(ci_calls(ed))
        ed.js("[...document.querySelectorAll('#rvList .shRow')].find((x) => x.dataset.k === 'jack').querySelector('.shDel').scrollIntoView({ block: 'nearest' })")
        ed.frames(2)
        xy = J(ed, "(() => { const r = [...document.querySelectorAll('#rvList .shRow')].find((x) => x.dataset.k === 'jack').querySelector('.shDel').getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; })()")
        inp.click(*xy)
        rv = J(ed, 'window.RV_PIPES.lines[%d].river' % src0)
        report('d 목록 × → 추진 지움 (횡단 남음)', len(rv) == 1 and rv[0]['k'] == 'open', str([x['k'] for x in rv]))
        ed.js("document.getElementById('hSave').click()")              # JACK 1127: 윗줄 [저장] 을 눌러야 씀
        ed.wait_js("__host.calls.filter((c) => c.name === 'save_pipes').length > %d" % n0, 5)
        sv = ci_calls(ed)
        txt = sv[-1] if sv else ''
        report('d 저장 (save_pipes 글에 river)', bool(txt) and '"river"' in txt and '"open"' in txt, '%d 번' % len(sv))
        if txt:
            open(os.path.join(work, 'data', 'pipes.js'), 'w', encoding='utf-8').write(txt)
        pg.load(True, V_O, 'trench')
        rv = J(ed, 'window.RV_PIPES.lines[%d].river || null' % src0)
        report('d 다시 열면 남아 있음 (시트파일 · 2단계 · 근입 5)', bool(rv) and len(rv) == 1 and rv[0]['dam'] == 'sheet' and rv[0]['st'] == 2 and rv[0]['emb'] == 5, str(rv))
        report('d 다시 열기 3D', J(ed, '__rv360.rv.tris()') > 100)
        errs(ed, '다시 열기')

        # ------------------------------------------------------------ b 브라우저
        print('== b 브라우저 (웹 공유 · 프로그램 밖)')
        pg.load(False, V_O, 'trench', clear=True)
        report('b 프로그램 없음', J(ed, '!window.pywebview'))
        cat_open(ed, inp, 'hd_river')
        report('b 하천 카테고리 · 목록 한 줄', J(ed, "document.querySelectorAll('#rvList .shRow').length === 1"))
        ed.js("document.querySelector('#rvList .shGo').click()")
        ed.wait_js(cv.READY, timeout=30)
        ed.frames(4)
        report('b 목록 누름 → 고름 · 3D 그물', J(ed, '!!__rv360.rv.cur()') and J(ed, '__rv360.rv.tris()') > 100)
        click(ed, inp, 'rvSec')
        report('b 종단 창', J(ed, "!document.getElementById('rvDlg').hidden && document.getElementById('rvSvg').innerHTML.length > 500"))
        shot(ed, SD, '11_브라우저.png')
        errs(ed, '브라우저')

        # ------------------------------------------------------------ p 3D 매핑 화면에서 만든 구간 (pipes.js)
        print('== p 3D 화면에서 만든 구간 읽힘 (pipes.js 에 river)')
        ed.__exit__(None, None, None)
        made3d = [{'k': 'jack', 'a': 20.0, 'b': 42.0, 'shape': 'circ', 'rev': True, 'cover': 2.5, 'base': 0.5, 'cas': 0, 'lw': 3.0, 'll': 6.0, 'aw': 2.5, 'al': 3.0, 'ld': 6.0, 'ad': 4.0},
                  {'k': 'open', 'a': 58.0, 'b': 76.0, 'dam': 'sheet', 'st': 2, 'cover': 1.5, 'h': 1.8, 'w': 4.0, 'emb': 3.0}]
        host_page = setup(src, work, made3d)
        ed = Edge(os.path.join(work + '_prof'), cv.WIN).__enter__()
        ed.net(block=ci.BLOCK)
        pg = Page(ed, work, host_page)
        inp = ce.Inp(ed)
        pg.load(False, V_J, 'trench', clear=True)
        cat_open(ed, inp, 'hd_river')
        rows = J(ed, "[...document.querySelectorAll('#rvList .shRow')].map((r) => [r.dataset.k, r.querySelector('.shGo').textContent])")
        report('p 목록 두 줄 (추진 원형 · 횡단 시트파일)', len(rows) == 2 and rows[0][0] == 'jack' and '원형' in rows[0][1] and rows[1][0] == 'open' and '시트파일' in rows[1][1], json.dumps(rows, ensure_ascii=False))
        pls = J(ed, '__rc.plans(%d)' % src0)
        pj = next(x for x in pls if x['k'] == 'jack')
        gm = J(ed, '__rc.gmin(%d, 20, 42)' % src0)
        report('p 추진 셈 (토피 2.5 · 발진 = 종점 · 원형 Ø6 · Ø4)', near(pj['top'], gm - 2.5 - 0.1, 0.02) and near(pj['shafts'][0]['s'], 42.0, 1e-6) and pj['shafts'][0]['shape'] == 'circ'
               and near(pj['jackLen'], 22.0 - 5.0, 1e-6), '%.2f · %s' % (pj['top'], json.dumps(pj['shafts'])))
        lq = J(ed, '__rv360.lineQty(%d)' % src0)
        report('p 수량 (수직구 2 · 라이너 · 시트파일 · 개착)', lq['rv_shaft'] == 2 and lq['rv_liner'] > 0 and lq['rv_sheet'] > 0 and near(lq['rv_open'], 18.0, 1e-6), json.dumps({k: round(v, 2) for k, v in lq.items() if k.startswith('rv_')}))
        report('p 3D 그물 · 구멍', J(ed, '__rv360.rv.tris()') > 1000 and J(ed, '__rv360.rv.holes()') > 30)
        view_png(ed, os.path.join(SD, '12_3D에서_만든_구간.png'))
        errs(ed, '3D 구간')
    finally:
        ed.__exit__(None, None, None)
        ci.cleanup(work)
    for w, e in R['errs']:
        print('   JS 오류 [%s] %s' % (w, e))
    print('사진: ' + SD)
    print('전체: ' + ('통과' if R['ok'] and not R['errs'] else '실패 · ' + ' · '.join(R['fails'])))
    sys.exit(0 if R['ok'] and not R['errs'] else 1)


def ci_calls(ed):
    return J(ed, "__host.calls.filter((c) => c.name === 'save_pipes').map((c) => c.arg)")


if __name__ == '__main__':
    main()
