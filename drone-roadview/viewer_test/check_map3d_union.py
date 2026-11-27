"""3D 매핑 보기 · 터파기 합집합 시험 (JACK 1127 «관로 그리기로 분기관 그릴 시 터파기 표면이 공제 안 되고 다 보여서 이상하게 작성돼»):
화면 없는 Edge (cdp.py) · 결과 폴더는 check_map3d_ug 와 같은 것 (관 0 = 본관 D500 · 관 1 = 첫 점이 본관 중심선 위인 D300 가지).

    python -X utf8 -I check_map3d_union.py --out <3D 매핑 결과 폴더> --shots <그림 폴더> [--js <견줄 map3d.js (고치기 전)> --tag 전]

  b  분기 자리 (화소 = 벽 흙색 비율 · 작은 칸):
       A 본관 가지 쪽 벽 (가지가 들어오는 자리 · 가지 바닥 위) = 뚫림 · W 본관 안에 든 가지 옆벽 = 없음 · E 가지 끝 마구리 (본관 중심) = 없음
       S1 분기 밖 본관 벽 · S2 본관 건너편 벽 = 그대로 벽 · 위에서 본 겹친 네모 안 벽 화소 비율
  q  수량: 관마다 셈 합 = 두 번 셈 → 겹침 (따로 셈: 0.05 m 기둥 합집합 · 이 시험 안 numpy) 만큼 뺐는지 (터파기 m³ · 포장 깨기 m²)
  f  빠르기: 관 30 줄 (가로 15 · 세로 15 · 225 곳 겹침 · 마디 약 3000) 그리기 한 장 평균 ms · 합집합 켬 / 끔 · 목록 만드는 시간
  모든 쪽 JS 오류 0
"""
import argparse
import json
import math
import os
import shutil
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
sys.path.insert(0, REPO)
from cdp import Edge  # noqa: E402
import check_map3d_ug as ug  # noqa: E402
from check_map3d_ug import J, errs, report, serve, setup_page, shot  # noqa: E402

U_JS = r"""
window.__u = {
  geo() {                                  // 분기 자리: J = 가지 첫 점 · u = 가지 방향 · n = 본관 수직 (가지 쪽) · 단면 (sectAt 과 같은 식)
    const M = __map3d, A = M.lines().find((q) => q.src === 0), B = M.lines().find((q) => q.src === 1), TR = M.tr(), P = B.pts;
    const jx = P[0], jy = P[1]; let ux = P[3] - jx, uy = P[4] - jy; const ul = Math.hypot(ux, uy); ux /= ul; uy /= ul;
    let bk = 0, bd = Infinity, bt = 0;
    for (let k = 0; k + 1 < A.pts.length / 3; k++) {
      const ax = A.pts[k * 3], ay = A.pts[k * 3 + 1], dx = A.pts[k * 3 + 3] - ax, dy = A.pts[k * 3 + 4] - ay, L = dx * dx + dy * dy;
      const t = Math.max(0, Math.min(1, ((jx - ax) * dx + (jy - ay) * dy) / L)), d = Math.hypot(jx - ax - t * dx, jy - ay - t * dy);
      if (d < bd) { bd = d; bk = k; bt = t; }
    }
    let mx = A.pts[bk * 3 + 3] - A.pts[bk * 3], my = A.pts[bk * 3 + 4] - A.pts[bk * 3 + 1]; const ml = Math.hypot(mx, my); mx /= ml; my /= ml;
    let nx = -my, ny = mx; if (nx * ux + ny * uy < 0) { nx = -nx; ny = -ny; }
    const gm = M.gAt(A, bk) + (M.gAt(A, bk + 1) - M.gAt(A, bk)) * bt, zm = M.zcAt(A, bk) + (M.zcAt(A, bk + 1) - M.zcAt(A, bk)) * bt;
    const sec = (D, g, zc) => { const zs = Math.min(zc - D / 2, g - 0.01), zb = zs - TR.bed, wb = D / 2 + TR.margin, zt = g - TR.tp, wv = wb + TR.n * Math.max(0, zt - zb);
      return zt > zs ? { g, zs, zb, wb, zt, wv, zm: zt, ws: wb + TR.n * (zs - zb) } : { g, zs, zb, wb, zt, wv, zm: zs, ws: wv }; };
    return { J: [jx, jy], u: [ux, uy], m: [mx, my], n: [nx, ny], d: bd, sm: sec(A.dia, gm, zm), sb: sec(B.dia, M.gAt(B, 0), M.zcAt(B, 0)), tr: TR };
  },
  look(x, y, z, d, el, ax, ay) {           // (x, y, z) 를 가운데 · 시선 = (ax, ay) 방향 (그쪽을 봄)
    const M = __map3d, T = M.T(); M.V.tgt = [x + T[0], y + T[1], z + T[2]]; M.V.d = d; M.V.el = el; M.V.az = Math.atan2(-ax, ay) * 180 / Math.PI; M.redraw(); return true;
  },
  scr(x, y, z) { const M = __map3d; M.view(); return M.toScr(x, y, z); },
  qty() { const M = __map3d, o = {}; for (const L of M.lines()) { const q = M.rv.lineQty(L.src); for (const k of ['cut', 'pav', 'fill', 'tmp', 'sand']) o[k] = (o[k] || 0) + q[k]; } return o; },
  lines() { const M = __map3d; return M.lines().map((L) => ({ src: L.src, D: L.dia, p: Array.from(L.pts), g: Array.from({ length: L.pts.length / 3 }, (_, k) => M.gAt(L, k)), zc: Array.from({ length: L.pts.length / 3 }, (_, k) => M.zcAt(L, k)) })); },
};
"""


def wall_frac(img, x, y, r):
    """벽 흙색 (굴삭기로 깎은 흙 · TR_SOIL 결) 화소 비율: 붉은 갈색 · 바닥 모래 (밝고 덜 붉음) · 관 (회색) · 포장 (회색) 아님."""
    b = ug.near_box(img, x, y, r).reshape(-1, 3).astype(float)
    R, G, B = b[:, 0], b[:, 1], b[:, 2]
    m = (R > G) & (G > B) & (R - B > 30) & ((R - B) / (R + 1) > 0.42) & (R < 200)
    return float(m.mean()) if len(m) else 0.0


def poly_frac(img, pts):
    h, w = img.shape[:2]
    mk = Image.new('L', (w, h), 0)
    ImageDraw.Draw(mk).polygon([tuple(p) for p in pts], fill=1)
    sel = np.asarray(mk).astype(bool)
    b = img[sel].astype(float)
    R, G, B = b[:, 0], b[:, 1], b[:, 2]
    m = (R > G) & (G > B) & (R - B > 30) & ((R - B) / (R + 1) > 0.42) & (R < 200)
    return float(m.mean()) if len(m) else 0.0, int(sel.sum())


def union_indep(L, tr, J, half=5.0, h=0.05):
    """따로 셈: J 둘레 네모 (±half) 를 h 기둥으로 · 관마다 [바닥 (모래 아래) ~ 땅] (관 끝 = 반듯이 · 꺾임 = 가장 가까운 마디) → 합 − 합집합."""
    xs = np.arange(J[0] - half + h / 2, J[0] + half, h)
    ys = np.arange(J[1] - half + h / 2, J[1] + half, h)
    X, Y = np.meshgrid(xs, ys)
    lo_all, hi_all = [], []
    for q in L:
        P = np.array(q['p'], float).reshape(-1, 3)[:, :2]
        g, zc, D = np.array(q['g']), np.array(q['zc']), q['D']
        best_d = np.full(X.shape, np.inf)
        LO = np.full(X.shape, np.inf)
        HI = np.full(X.shape, -np.inf)
        for k in range(len(P) - 1):
            a, b = P[k], P[k + 1]
            d = b - a
            l2 = float(d @ d)
            if l2 < 1e-12:
                continue
            t = ((X - a[0]) * d[0] + (Y - a[1]) * d[1]) / l2
            ok = np.ones(X.shape, bool)
            if k == 0:
                ok &= t >= 0
            if k == len(P) - 2:
                ok &= t <= 1
            tc = np.clip(t, 0, 1)
            dist = np.hypot(X - a[0] - d[0] * tc, Y - a[1] - d[1] * tc)
            gg = g[k] + (g[k + 1] - g[k]) * tc
            zz = zc[k] + (zc[k + 1] - zc[k]) * tc
            zs = np.minimum(zz - D / 2, gg - 0.01)
            zb = zs - tr['bed']
            wb = D / 2 + tr['margin']
            zt = gg - tr['tp']
            wv = wb + tr['n'] * np.maximum(0, zt - zb)
            lo = np.where(dist > wb, zb + (dist - wb) / max(tr['n'], 1e-9), zb)
            inside = ok & (dist < wv) & (gg > lo)
            LO = np.where(inside, np.minimum(LO, lo), LO)
            HI = np.where(inside, np.maximum(HI, gg), HI)
        lo_all.append(LO)
        hi_all.append(HI)
    LO, HI = np.array(lo_all), np.array(hi_all)
    has = np.isfinite(LO)
    ln = np.where(has, HI - LO, 0).sum(0)
    # 합집합 (관 둘 · 겹치면 [min, max] · 안 겹치면 합)
    cnt = has.sum(0)
    lo2 = np.where(has, LO, np.inf).min(0)
    hi2 = np.where(has, HI, -np.inf).max(0)
    gap = np.zeros(X.shape)
    if len(L) == 2:
        a0, a1, b0, b1 = LO[0], HI[0], LO[1], HI[1]
        gap = np.where(cnt == 2, np.maximum(0, np.maximum(a0, b0) - np.minimum(a1, b1)), 0)
    uni = np.where(cnt >= 1, hi2 - lo2 - gap, 0)
    return float((ln - uni).sum() * h * h), float(np.maximum(cnt - 1, 0).sum() * h * h)


def grid_pipes(base_line):
    """관 30 줄: 가로 15 · 세로 15 (8 m 간격 · 220 m · 꼭짓점 100) · D300 · 225 곳 겹침."""
    out = []
    for k in range(30):
        vert = k < 15
        c = -20 + 8 * (k % 15)
        pts = []
        for i in range(100):
            s = -100 + 220 * i / 99
            pts.append([c, s, None] if vert else [s * 0.5 + 40, c * 1.4 - 70, None])
        q = dict(base_line)
        q.update({'label': 'G%d' % k, 'dia': 0.3, 'pts': pts, 'g': [None] * 100, 'ctrl': [0, 99]})
        q.pop('shore', None)
        out.append(q)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--shots', required=True)
    ap.add_argument('--js', default=None)
    ap.add_argument('--tag', default='후')
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    out, SD, tg = os.path.abspath(a.out), os.path.abspath(a.shots), a.tag
    os.makedirs(SD, exist_ok=True)
    os.makedirs(os.path.join(out, 'viewer'), exist_ok=True)
    shutil.copy2(os.path.join(REPO, 'viewer', 'map3d.html'), os.path.join(out, 'index.html'))
    shutil.copy2(os.path.abspath(a.js) if a.js else os.path.join(REPO, 'viewer', 'map3d.js'), os.path.join(out, 'viewer', 'map3d.js'))
    for f in ('river.js', 'cost.js'):
        shutil.copy2(os.path.join(REPO, 'viewer', f), os.path.join(out, 'viewer', f))
    srv, base = serve(out)
    ed = Edge(os.path.join(SD, '_prof_u'), ug.WIN, gl=False).__enter__()
    res = {}
    try:
        print('== 열기 (%s)' % tg)
        setup_page(ed, base + 'index.html')
        ed.js('localStorage.clear()')
        report('열림 · 관로', setup_page(ed, base + 'index.html'), J(ed, 'window.__pipes'))
        ed.js(U_JS)
        ed.js("__map3d.setMode('trench'); __map3d.SHOW.lbl = false; __map3d.xray(false); __map3d.redraw()")
        ed.frames(4)
        G = J(ed, '__u.geo()')
        sm, sb, tr = G['sm'], G['sb'], G['tr']
        report('b 가지 첫 점 = 본관 중심선 위', G['d'] < 0.01, '%.4f m' % G['d'])
        Jx, Jy = G['J']
        ux, uy = G['u']
        nx, ny = G['n']
        mx, my = G['m']
        px, py = -uy, ux
        dot = ux * nx + uy * ny

        def wz(s, z):
            return s['wv'] if z >= s['zm'] else s['ws'] + (s['wv'] - s['ws']) * (z - s['zs']) / max(s['zm'] - s['zs'], 1e-4)

        print('== b 분기 자리 (%s)' % tg)
        zA = (sm['zt'] + max(sb['zs'], sm['zs'])) / 2 + 0.1
        sA = wz(sm, zA) / dot
        A = (Jx + ux * sA, Jy + uy * sA, zA)
        ed.js('__u.look(%f, %f, %f, 7, 32, %f, %f)' % (A[0], A[1], A[2], ux, uy))
        img = shot(ed, SD, '%s_A_본관벽_분기자리.png' % tg)
        q = J(ed, '__u.scr(%f, %f, %f)' % A)
        res['A'] = wall_frac(img, q[0], q[1], 6)
        zS = (sm['zt'] + sm['zs']) / 2
        S1 = (Jx + mx * 3 + nx * wz(sm, zS), Jy + my * 3 + ny * wz(sm, zS), zS)
        q1 = J(ed, '__u.scr(%f, %f, %f)' % S1)
        res['S1'] = wall_frac(img, q1[0], q1[1], 6)
        zW = (sb['zt'] + sb['zs']) / 2 + 0.05
        sW = 0.5 * sm['wv'] / dot
        Wp = (Jx + ux * sW + px * wz(sb, zW), Jy + uy * sW + py * wz(sb, zW), zW)
        ed.js('__u.look(%f, %f, %f, 7, 40, %f, %f)' % (Wp[0], Wp[1], Wp[2], px, py))
        img = shot(ed, SD, '%s_W_가지옆벽_본관안.png' % tg)
        q = J(ed, '__u.scr(%f, %f, %f)' % Wp)
        res['W'] = wall_frac(img, q[0], q[1], 5)
        zE = (sb['zt'] + sb['zs']) / 2
        E = (Jx, Jy, zE)
        ed.js('__u.look(%f, %f, %f, 7, 62, %f, %f)' % (E[0], E[1], E[2], -ux, -uy))
        img = shot(ed, SD, '%s_E_가지끝마구리.png' % tg)
        q = J(ed, '__u.scr(%f, %f, %f)' % E)
        res['E'] = wall_frac(img, q[0], q[1], 5)
        S2 = (Jx - nx * wz(sm, zA), Jy - ny * wz(sm, zA), zA)
        ed.js('__u.look(%f, %f, %f, 7, 32, %f, %f)' % (S2[0], S2[1], S2[2], -nx, -ny))
        img = shot(ed, SD, '%s_S2_본관건너편벽.png' % tg)
        q = J(ed, '__u.scr(%f, %f, %f)' % S2)
        res['S2'] = wall_frac(img, q[0], q[1], 6)
        ed.js('__u.look(%f, %f, %f, 9, 80, %f, %f)' % (Jx + ux * 0.6, Jy + uy * 0.6, sm['g'], ux, uy))
        img = shot(ed, SD, '%s_T_위에서_겹친네모.png' % tg)
        s1 = sm['wv'] / dot
        cs = [(Jx + px * sb['wv'], Jy + py * sb['wv']), (Jx - px * sb['wv'], Jy - py * sb['wv']),
              (Jx + ux * s1 - px * sb['wv'], Jy + uy * s1 - py * sb['wv']), (Jx + ux * s1 + px * sb['wv'], Jy + uy * s1 + py * sb['wv'])]
        poly = [J(ed, '__u.scr(%f, %f, %f)' % (c[0], c[1], sm['g']))[:2] for c in cs]
        res['T'], npx = poly_frac(img, poly)
        ed.js('__u.look(%f, %f, %f, 11, 38, %f, %f)' % (Jx - ux * 1.5, Jy - uy * 1.5, sm['zt'], ux + nx * 0.4, uy + ny * 0.4))
        shot(ed, SD, '%s_분기_비스듬히.png' % tg)
        ed.js('__u.look(%f, %f, %f, 12, 50, %f, %f)' % (Jx + ux * 2, Jy + uy * 2, sm['zt'], -ux + mx * 0.5, -uy + my * 0.5))
        shot(ed, SD, '%s_분기_가지쪽에서.png' % tg)
        print('   벽 흙색 비율: A 본관 가지쪽 벽 %.2f · W 본관 안 가지 옆벽 %.2f · E 가지 끝 마구리 %.2f · S1 분기 밖 본관 벽 %.2f · S2 본관 건너편 벽 %.2f · '
              'T 위에서 겹친 네모 %.2f (%d 화소)' % (res['A'], res['W'], res['E'], res['S1'], res['S2'], res['T'], npx))
        if tg != '전':
            report('b A 본관 옆벽이 가지 쪽으로 뚫림', res['A'] < 0.15, '%.2f' % res['A'])
            report('b W 본관 안에 든 가지 옆벽 없음', res['W'] < 0.15, '%.2f' % res['W'])
            report('b E 가지 끝 마구리 없음', res['E'] < 0.15, '%.2f' % res['E'])
            report('b S1 분기 밖 본관 벽 그대로', res['S1'] > 0.3, '%.2f' % res['S1'])
            report('b S2 본관 건너편 벽 그대로', res['S2'] > 0.3, '%.2f' % res['S2'])
            report('b 합집합 목록 (두 조각 닿는 칸 · 마디 기록)', J(ed, '__map3d.CUT.n') > 0, '기록 %d · %.1f ms' % (J(ed, '__map3d.CUT.n'), J(ed, '__map3d.CUT.ms')))
        errs(ed, '분기')

        print('== q 수량 겹침 (%s)' % tg)
        qt = J(ed, '__u.qty()')
        ov = J(ed, '__map3d.over ? __map3d.over() : []')
        ovc = sum(v[1][0] for v in ov)
        ovp = sum(v[1][1] for v in ov)
        iv, ia = union_indep(J(ed, '__u.lines()'), tr, (Jx, Jy))
        print('   관마다 합: 터파기 %.3f m³ · 포장 깨기 %.3f m² · 되메우기 %.3f m³ · 뺀 겹침 %.3f m³ · %.3f m² (관 %s)'
              % (qt['cut'], qt['pav'], qt['fill'], ovc, ovp, [v[0] for v in ov]))
        print('   따로 셈 겹침 (0.05 m 기둥): %.3f m³ · %.3f m²' % (iv, ia))
        report('q 두 번 셈 있음 (따로 셈 겹침 > 0)', iv > 0.05 and ia > 0.05, '%.3f m³ · %.3f m²' % (iv, ia))
        if tg != '전':
            report('q 뺀 부피 = 따로 셈 겹침 (5 % 안)', abs(ovc - iv) <= 0.05 * iv + 0.01, '%.3f vs %.3f m³' % (ovc, iv))
            report('q 뺀 면적 = 따로 셈 겹침 (5 % 안)', abs(ovp - ia) <= 0.05 * ia + 0.01, '%.3f vs %.3f m²' % (ovp, ia))
            report('q 뺀 쪽 = 가지 관 (관 1)', [v[0] for v in ov] == [1], str([v[0] for v in ov]))
            report('q 되메우기 = 터파기 − 관 − 모래 (뺀 만큼 줄어듦)', abs(qt['tmp'] - qt['pav']) < 1e-6, '')
        errs(ed, '수량')

        print('== f 빠르기 · 관 30 줄 (%s)' % tg)
        bl = J(ed, 'window.RV_PIPES.lines[1]')
        ed.js('window.RV_PIPES.lines = %s; __map3d.rebuild()' % json.dumps(grid_pipes(bl), ensure_ascii=False))
        ed.frames(6)
        ed.js('__u.look(40, 10, %f, 140, 55, 0.3, 1)' % sm['g'])
        ed.frames(6)
        shot(ed, SD, '%s_관30줄.png' % tg)
        # 한 장 시간 = 화면 주사율 (16.7 ms) 에 안 막히게: requestAnimationFrame → setTimeout 0 · 그릴 때마다 readPixels 로 그래픽 카드 끝까지 기다림
        FT = r"""(async () => { const M = __map3d, c = document.getElementById('c'), g = c.getContext('webgl2'), px = new Uint8Array(4), raf = window.requestAnimationFrame;
          window.requestAnimationFrame = (f) => setTimeout(() => f(performance.now()), 0);
          const run = async () => { for (let i = 0; i < 5; i++) { M.redraw(); await new Promise((r) => setTimeout(r, 0)); }
            const t0 = performance.now(), d0 = window.__drawn;
            while (performance.now() - t0 < 1500) { M.redraw(); await new Promise((r) => setTimeout(r, 0)); g.readPixels(0, 0, 1, 1, g.RGBA, g.UNSIGNED_BYTE, px); }
            return (performance.now() - t0) / Math.max(1, window.__drawn - d0); };
          const a = await run(); let b = null;
          if (M.CUT) { M.CUT.on = false; b = await run(); M.CUT.on = true; }
          window.requestAnimationFrame = raf;
          return { on: a, off: b, n: M.CUT ? M.CUT.n : 0, ms: M.CUT ? M.CUT.ms : 0, segs: M.lines().reduce((s, L) => s + L.pts.length / 3 - 1, 0) }; })()"""
        for nm, vw in (('멀리 (관 30 줄 전부)', '40, 10, %f, 140, 55, 0.3, 1' % sm['g']), ('가까이 (겹친 곳이 화면 가득)', '4, -64.4, %f, 7, 60, 0.3, 1' % sm['g'])):
            ed.js('__u.look(%s)' % vw)
            ed.frames(4)
            shot(ed, SD, '%s_관30줄_%s.png' % (tg, nm.split(' ')[0]))
            ft = json.loads(ed.js('(%s).then((o) => JSON.stringify(o))' % FT, await_promise=True))
            print('   %s: 마디 %d · 한 장 평균 %.2f ms (합집합 켬)%s · 목록 기록 %d · 만드는 시간 %.1f ms'
                  % (nm, ft['segs'], ft['on'], (' · %.2f ms (끔)' % ft['off']) if ft['off'] is not None else '', ft['n'], ft['ms']))
            res['ft_' + nm.split(' ')[0]] = ft
            if tg != '전':
                report('f %s 한 장 시간 (끔 대비 +3 ms 안)' % nm.split(' ')[0], ft['on'] - ft['off'] < 3.0, '%.2f vs %.2f ms' % (ft['on'], ft['off']))
        t0 = J(ed, '(() => { __map3d.trSet({ margin: 0.31 }); const t = performance.now(); const o = __map3d.over ? __map3d.over() : []; return performance.now() - t; })()')
        ed.js('__map3d.trSet({ margin: 0.3 })')
        print('   겹침 수량 셈 (225 곳): %.1f ms' % t0)
        errs(ed, '빠르기')
    finally:
        ed.__exit__(None, None, None)
        srv.shutdown()
    open(os.path.join(SD, '%s_결과.json' % tg), 'w', encoding='utf-8').write(json.dumps(res, ensure_ascii=False, indent=1))
    for where, e in ug.R['errs']:
        print('  오류', where, e)
    print('== %s · 실패 %d %s' % ('통과' if ug.R['ok'] else '실패', len(ug.R['fails']), ug.R['fails']))
    return 0 if ug.R['ok'] else 1


if __name__ == '__main__':
    sys.exit(main())
