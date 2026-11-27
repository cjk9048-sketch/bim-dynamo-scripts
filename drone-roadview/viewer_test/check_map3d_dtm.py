"""3D 매핑 보기 · 맨땅 보기 (DTM) · 땅 아님 그리기 시험 (JACK 1126): 화면 없는 Edge (cdp.py · 그래픽 카드) 에 진짜 마우스 · 키.
프로그램 쪽은 hostmock.js 흉내 (nonground_get · nonground_set · job …). 메운 칸 표시 (mtiles) 는 이 시험이 결과물/DTM_땅칸.tif 에서
따로 만들어 viewer/dem_mt.js 로 dem.js 뒤에 붙임 (조정자 프로그램이 dem.js 에 넣는 것과 같은 꼴 · 1 = 땅 · 0 = 메움 · 255 = 없음).

    python -I check_map3d_dtm.py --out <3D 매핑 결과 폴더 (check_map3d_ug 와 같은 것)> --shots <그림 폴더> [--flat <3D 없는 프로젝트 폴더>]

  a  단추: dem.js 있으면 윗줄 [맨땅] (프로그램 · 브라우저) · 오른쪽 메뉴 «맨땅 보기» · dem.js 없으면 둘 다 없음 · 3D 없는 프로젝트도 없음
  b  맨땅 켜기: 그물 = 타일마다 · 범례 «땅으로 잰 곳 · 메운 곳 · 등고선» · 모델 화면과 다름
  z  높이: 고르기 (pickAt) = dem.js 겹선형 (따로 셈 · 세모 나눔 차이만) · 나무 · 건물 자리는 모델보다 낮음
  m  메운 칸: 메운 칸 표시를 끄면 메운 칸 (mtiles 0) 화소만 바뀜 · 땅으로 잰 칸 그대로
  c  등고선: 끄면 바뀌는 화소 = 표고가 간격 배수 가까이 (따로 셈) · 5배 줄이 더 진함
  u  맨땅 위 관로 · 터파기 (구덩이 · 빈 화소 없음) · 관 누르기 · 오른쪽 메뉴 좌표 = 땅 · 거리 재기 점 = 땅
  o  모델 보기로 돌아옴 (처음 화면과 같음)
  n  땅 아님: 다듬기 칸 줄 · [그리기] → 위에서 · 다각형 둘 (두 번 누르기 · Enter) · 꼭짓점 끌기 · Backspace = 다시 열기 ·
     [적용] → nonground_set (지금 것 + 새 것 · 도면 좌표) → 진행 → 다시 엶 → 붉은 점선 · 누르기 → 지우기 · 메뉴 «여기 땅 아님 지정» ·
     «이 땅 아님 지우기» · [모두 없앰] → 빈 목록
  w  브라우저 (웹 공유): 맨땅 됨 · 땅 아님 항목 없음
  x  mtiles 없는 dem.js = 범례에 메운 곳 없음 · dem.js 없음 = 단추 · 메뉴 없음
  모든 쪽 JS 오류 0
"""
import argparse
import json
import math
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
sys.path.insert(0, REPO)
from cdp import Edge  # noqa: E402
import check_edit as ce  # noqa: E402
import check_map3d_ug as ug  # noqa: E402
from check_map3d_ug import J, errs, report, serve, setup_page, shot  # noqa: E402
from check_map3d_ctx import calls, copy_site, empty_spots, item_xy, pick, rclick, toast_btn, wait_reload  # noqa: E402

WIN = ug.WIN
LINE_RGB = round((0.2 + 0.13 + 0.08) * 255)          # 등고선 줄 색 (map3d.js DTP · vec3(0.2, 0.13, 0.08)) 세 값 합
D_JS = r"""
window.__d = {
  scr(w) { const M = __map3d, T = M.T(); M.view(); return M.toScr(w[0] - T[0], w[1] - T[1], w[2] - T[2]); },   // 세계 점 → 화면
  top(x, y, z, d) { const V = __map3d.V; V.tgt = [x, y, z]; V.d = d; V.el = 89.9; V.az = 0; __map3d.redraw(); return true; },
};
"""


# ---------------------------------------------------------------- dem.js 따로 읽기 · 메운 칸 (mtiles) 따로 만들기
def read_dem(out):
    s = open(os.path.join(out, 'data', 'dem.js'), encoding='utf-8').read()
    d = json.loads(s[s.index('{'):s.rindex('}') + 1])
    T = int(d['tile']); W = T + 1
    Z = {}
    for k, b in d['tiles'].items():
        import base64
        Z[k] = np.frombuffer(base64.b64decode(b), '<f4')[:W * W].reshape(W, W).astype(float)
    return d, Z


def make_mtiles(out, d):
    """결과물/DTM_땅칸.tif (0.5 m · 1 땅 · 0 메움 · 255 없음) → dem.js 마디 (1 m 칸 가운데) 마다 다수결 → {열쇠: (T+1)² uint8}."""
    Image.MAX_IMAGE_PIXELS = None
    Mg = np.asarray(Image.open(os.path.join(out, '결과물', 'DTM_땅칸.tif')), np.uint8)
    tw = [float(v) for v in open(os.path.join(out, '결과물', 'DTM_땅칸.tfw')).read().split()]
    c0, X0, Y0 = tw[0], tw[4], tw[5]
    M = ug_mapping_origin(out)
    T = int(d['tile']); W = T + 1; cell = float(d['cell']); k = max(1, int(round(cell / c0)))
    out_ = {}
    for key in d['tiles']:
        ix, iy = map(int, key.split('_'))
        E = d['x0'] + (ix * T + np.arange(W)) * cell + M[0]
        N = d['y0'] + (iy * T + np.arange(W)) * cell + M[1]
        cols = np.round((E - cell / 2 + c0 / 2 - X0) / c0).astype(int)[:, None] + np.arange(k)[None, :]       # (W, k)
        rows = np.round((Y0 - (N + cell / 2 - c0 / 2)) / c0).astype(int)[:, None] + np.arange(k)[None, :]     # (W, k)
        R = rows[:, None, :, None]; C = cols[None, :, None, :]
        okr = (R >= 0) & (R < Mg.shape[0]); okc = (C >= 0) & (C < Mg.shape[1])
        B = np.where(okr & okc, Mg[np.clip(R, 0, Mg.shape[0] - 1), np.clip(C, 0, Mg.shape[1] - 1)], 255)    # (W 행, W 열, k, k)
        nv = (B != 255).sum(axis=(2, 3)); ng = (B == 1).sum(axis=(2, 3))
        out_[key] = np.where(nv == 0, 255, np.where(ng * 2 >= nv, 1, 0)).astype(np.uint8)
    return out_


def ug_mapping_origin(out):
    s = open(os.path.join(out, 'data', 'mapping.js'), encoding='utf-8').read()
    return json.loads(s[s.index('{'):s.rindex('}') + 1])['origin']


def write_pages(out, mt):
    import base64
    js = 'window.RV_DEM && (window.RV_DEM.mtiles = ' + json.dumps({k: base64.b64encode(v.tobytes()).decode('ascii') for k, v in mt.items()}) + ');\n'
    open(os.path.join(out, 'viewer', 'dem_mt.js'), 'w', encoding='utf-8').write(js)
    tag = '<script src="data/dem.js" onerror="void 0"></script>'
    for src, dst, rep in (('index_host.html', 'index_host_mt.html', tag + '\n<script src="viewer/dem_mt.js"></script>'),
                          ('index.html', 'index_mt.html', tag + '\n<script src="viewer/dem_mt.js"></script>'),
                          ('index.html', 'index_nodem.html', '')):
        h = open(os.path.join(out, src), encoding='utf-8').read()
        assert tag in h
        open(os.path.join(out, dst), 'w', encoding='utf-8').write(h.replace(tag, rep))


class Dem:
    """dem.js 겹선형 (보기 화면 demAt 과 같은 약속 · 세계 = 매핑 원점 기준 · 표고 절대)."""

    def __init__(self, d, Z, mt):
        self.d, self.Z, self.mt = d, Z, mt
        self.T = int(d['tile']); self.cell = float(d['cell'])
        self.z0 = float(np.nanmedian(np.concatenate([v.ravel() for v in Z.values()])))

    def near_z(self, zt):
        """표고 zt 가까운 마디 중 자료 가운데에 가장 가까운 것 (세계 x, y)."""
        best, bd = None, float('inf')
        pts = [(k, r, c) for k, a in self.Z.items() for r, c in zip(*np.nonzero(np.abs(a - zt) < 0.05))]
        cx = np.median([self.d['x0'] + (int(k.split('_')[0]) * self.T + c) * self.cell for k, r, c in pts]) if pts else 0
        cy = np.median([self.d['y0'] + (int(k.split('_')[1]) * self.T + r) * self.cell for k, r, c in pts]) if pts else 0
        for k, r, c in pts:
            x = self.d['x0'] + (int(k.split('_')[0]) * self.T + c) * self.cell; y = self.d['y0'] + (int(k.split('_')[1]) * self.T + r) * self.cell
            dd = math.hypot(x - cx, y - cy)
            if dd < bd:
                best, bd = (x, y), dd
        return best or (0.0, 0.0)

    def atz(self, x, y):
        """땅 높이 · 자료 밖이면 가운데값 (화면 점 찾기용)."""
        z = self.at(x, y)
        return z if math.isfinite(z) else self.z0

    def at(self, x, y):
        T, c = self.T, self.cell
        fx, fy = (x - self.d['x0']) / c, (y - self.d['y0']) / c
        if fx < 0 or fy < 0:
            return float('nan')
        ix, iy = int(fx // T), int(fy // T)
        a = self.Z.get('%d_%d' % (ix, iy))
        if a is None:
            return float('nan')
        cc, rr = fx - ix * T, fy - iy * T
        c0, r0 = min(int(cc), T - 1), min(int(rr), T - 1)
        tx, ty = cc - c0, rr - r0
        return (a[r0, c0] * (1 - tx) + a[r0, c0 + 1] * tx) * (1 - ty) + (a[r0 + 1, c0] * (1 - tx) + a[r0 + 1, c0 + 1] * tx) * ty

    def spots(self, val, n, rad=3):
        """mtiles 가 val 이고 둘레 rad 마디도 다 val · 표고 있는 마디 (세계 x, y, z 절대) · 서로 떨어진 n 개."""
        out = []
        for k, m in self.mt.items():
            ix, iy = map(int, k.split('_'))
            a = self.Z[k]
            for r in range(rad, self.T - rad + 1):
                for c in range(rad, self.T - rad + 1):
                    blk = m[r - rad:r + rad + 1, c - rad:c + rad + 1]
                    if (blk == val).all() and np.isfinite(a[r - rad:r + rad + 1, c - rad:c + rad + 1]).all():
                        out.append((self.d['x0'] + (ix * self.T + c) * self.cell, self.d['y0'] + (iy * self.T + r) * self.cell, float(a[r, c])))
        sel = []
        rng = np.random.default_rng(7)
        for i in rng.permutation(len(out)):
            q = out[i]
            if all(math.hypot(q[0] - s[0], q[1] - s[1]) > 15 for s in sel):
                sel.append(q)
            if len(sel) >= n:
                break
        return sel


def img(path):
    return np.asarray(Image.open(path).convert('RGB')).astype(int)


def box(a, x, y, r):
    return a[max(0, int(y) - r):int(y) + r + 1, max(0, int(x) - r):int(x) + r + 1]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--shots', required=True)
    ap.add_argument('--flat', default=None)
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    out, SD = os.path.abspath(a.out), os.path.abspath(a.shots)
    os.makedirs(SD, exist_ok=True)
    copy_site(out)
    d, Z = read_dem(out)
    mt = make_mtiles(out, d)
    write_pages(out, mt)
    DM = Dem(d, Z, mt)
    n0 = sum(int((v == 0).sum()) for v in mt.values()); n1 = sum(int((v == 1).sum()) for v in mt.values())
    print('== 준비 · dem 타일 %d · 메운 마디 %d · 땅 마디 %d' % (len(Z), n0, n1))
    srv, base = serve(out)
    ed = Edge(os.path.join(SD, '_prof'), WIN, gl=False).__enter__()
    inp = ce.Inp(ed)

    def page(url):
        ok = setup_page(ed, base + url)
        ed.js(D_JS)
        return ok

    def after_reload():
        ed.js(ug.T_JS); ed.js(D_JS)
        ed.frames(3)

    try:
        print('== a 단추 (프로그램 흉내)')
        report('열림', page('index_host_mt.html'))
        ed.js('localStorage.clear(); sessionStorage.clear()')
        page('index_host_mt.html')
        O = J(ed, 'window.RV_MAPPING.origin'); H0 = O[2]
        report('a [맨땅] 단추 보임', J(ed, "(() => { const b = document.getElementById('bDtm'); return !b.hidden && b.offsetParent !== null; })()"))
        L0 = J(ed, '__t.L(0).len'); s0 = round(L0 * 0.3, 1)
        ed.js('__t.look(0, %f, 40, 50, 60)' % s0); ed.frames(3)
        sp = empty_spots(ed)
        report('빈 땅 자리', len(sp) >= 3, str(sp))
        A, B = sp[0], sp[1]
        rclick(ed, *A)
        it = J(ed, '__map3d.ctx().items')
        report('a 오른쪽 메뉴 = 맨땅 · 북쪽 · 땅 아님 지정 없음 (JACK 1127 · 옵션 칸에서)', not any(k in it for k in ('dtm', 'north', 'ngHere')), str(it))
        inp.key('Escape', 'Escape', 27)
        errs(ed, '단추')

        print('== b 맨땅 켜기')
        ed.js('__t.look(0, %f, 60, 50, 60)' % s0); ed.frames(3)
        im0 = shot(ed, SD, '40_모델.png')
        ug.ui_click(ed, inp, 'bDtm')
        ed.frames(3)
        dt = J(ed, '__map3d.dt()')
        report('b 켜짐 · 단추 켜짐', dt['on'] and J(ed, "document.getElementById('bDtm').classList.contains('on')"))
        report('b 그물 = dem 타일마다', dt['parts'] == len(Z) and sorted(dt['keys']) == sorted(Z.keys()) and dt['tris'] > 10000, '%d 타일 · 세모 %d' % (dt['parts'], dt['tris']))
        lg = J(ed, "(() => { const l = document.getElementById('dtLeg'); return { vis: !l.hidden && l.offsetParent !== null, mt: !document.getElementById('dtMt').hidden, t: l.textContent }; })()")
        ci = dt['ci']
        report('b 범례 «땅으로 잰 곳 · 메운 곳 · 등고선»', lg['vis'] and lg['mt'] and '땅으로 잰 곳' in lg['t'] and '메운 곳' in lg['t'] and ('등고선 %g m' % ci) in lg['t'], lg['t'])
        im1 = shot(ed, SD, '41_맨땅.png')
        dif = np.abs(im1 - im0).sum(axis=2)
        report('b 모델 화면과 다름', (dif > 40).mean() > 0.3, '바뀐 화소 %.0f %%' % ((dif > 40).mean() * 100))
        errs(ed, '맨땅')

        print('== z 높이 (따로 셈 · dem.js 겹선형)')
        pts = []
        for y in range(200, 760, 70):
            for x in range(150, 1250, 110):
                p = pick(ed, x, y)
                if p:
                    pts.append(p)
        dz = np.array([p[2] + H0 - DM.at(p[0], p[1]) for p in pts])
        dz = dz[np.isfinite(dz)]
        report('z 고른 점 = 땅 (dem.js)', len(dz) >= 30 and np.median(np.abs(dz)) < 0.01 and np.abs(dz).max() < 0.15,
               '%d 점 · 가운데값 %.4f · 최대 %.3f m' % (len(dz), np.median(np.abs(dz)) if len(dz) else 99, np.abs(dz).max() if len(dz) else 99))
        ug.ui_click(ed, inp, 'bDtm')
        mz = [pick(ed, *q[:2]) for q in [(x, y) for y in range(200, 760, 70) for x in range(150, 1250, 110)]]
        ug.ui_click(ed, inp, 'bDtm')
        hi = sum(1 for q in mz if q and q[2] + H0 - DM.at(q[0], q[1]) > 0.5)
        report('z 모델 면은 나무 · 건물 자리에서 더 높음 (맨땅 아님)', hi >= 3, '%d 곳' % hi)
        errs(ed, '높이')

        print('== m 메운 칸 표시')
        ed.js('__map3d.setPipes(false)')
        f_sp, g_sp = DM.spots(0, 3, 1), DM.spots(1, 3)                    # 메운 곳은 작은 덩어리 (건물 · 숲 밑) → 둘레 1 마디
        res = []
        for kind, lst in (('메움', f_sp), ('땅', g_sp)):
            for q in lst:
                ed.js('__d.top(%f, %f, %f, 30)' % (q[0], q[1], q[2] - H0)); ed.frames(3)
                c = J(ed, '__d.scr([%f, %f, %f])' % (q[0], q[1], q[2] - H0))
                ia = shot(ed, SD, '_m.png')
                ed.js('__map3d.DT.mtOn = false; __map3d.redraw()')
                ib = shot(ed, SD, '_m.png')
                ed.js('__map3d.DT.mtOn = true; __map3d.redraw()')
                res.append((kind, float(np.abs(box(ia, c[0], c[1], 12) - box(ib, c[0], c[1], 12)).sum(axis=2).mean())))
        fm = [v for k, v in res if k == '메움']; gm = [v for k, v in res if k == '땅']
        report('m 메운 칸만 표시 (끄면 메운 칸 화소만 바뀜)', len(fm) >= 2 and len(gm) >= 2 and min(fm) > 25 and max(gm) < 2,
               '메움 %s · 땅 %s' % ([round(v) for v in fm], [round(v, 1) for v in gm]))
        q = f_sp[0]
        ed.js('__d.top(%f, %f, %f, 45)' % (q[0], q[1], q[2] - H0))
        shot(ed, SD, '42_메운곳_가까이.png')
        errs(ed, '메운 칸')

        print('== c 등고선')
        cx_, cy_ = DM.near_z(round(DM.z0 / (ci * dt['every'])) * ci * dt['every'])    # 가운데 = 굵은 줄 높이 마디 (굵은 · 가는 줄 다 보이게)
        ed.js('__d.top(%f, %f, %f, 70)' % (cx_, cy_, DM.atz(cx_, cy_) - H0)); ed.frames(3)
        ia = shot(ed, SD, '43_등고선.png')
        ed.js('__map3d.DT.ctOn = false; __map3d.redraw()')
        ib = shot(ed, SD, '_c.png')
        ed.js('__map3d.DT.ctOn = true; __map3d.redraw()'); ed.frames(2)
        dd = (ib - ia).sum(axis=2)
        ys, xs = np.nonzero((dd > 30)[80:WIN[1] - 60, 20:1260])
        ys = ys + 80; xs = xs + 20
        frac = (dd > 30).mean()
        ok = len(xs) > 200
        dist, dmaj, dmin = [], [], []
        if ok:
            rng = np.random.default_rng(3)
            for i in rng.choice(len(xs), min(200, len(xs)), replace=False):
                p = pick(ed, float(xs[i]), float(ys[i]))
                if not p:
                    continue
                z = p[2] + H0
                k = round(z / ci)
                dist.append(abs(z - k * ci) / ci)
                bx = box(dd, xs[i], ys[i], 2)                            # 줄 가운데 = 둘레 5×5 에서 가장 진한 화소
                yy, xx = np.unravel_index(np.argmax(bx), bx.shape)
                yy += max(0, int(ys[i]) - 2); xx += max(0, int(xs[i]) - 2)
                bcol = ib[yy, xx].sum() - LINE_RGB                       # 섞는 비 = 진해진 만큼 ÷ (바탕 − 줄 색) · 낮은 땅 바탕이 어두워도 같은 잣대
                (dmaj if k % dt['every'] == 0 else dmin).append(float(dd[yy, xx]) / max(bcol, 1.0))
        dist = np.array(dist)
        mj, mn = (float(np.median(v)) if v else 0.0 for v in (dmaj, dmin))
        report('c 줄 화소 = 표고가 간격 배수 가까이 (따로 셈)', ok and len(dist) > 30 and np.median(dist) < 0.05 and np.percentile(dist, 80) < 0.1,
               '줄 화소 %.1f %% · 가운데 %.3f · 80%% %.3f (간격 비)' % (frac * 100, np.median(dist) if len(dist) else 9, np.percentile(dist, 80) if len(dist) else 9))
        report('c %d배 줄이 더 진함' % dt['every'], len(dmaj) >= 5 and len(dmin) >= 5 and mj > 1.3 * mn,
               '굵은 줄 %d 곳 %.2f · 가는 줄 %d 곳 %.2f (섞는 비 가운데값)' % (len(dmaj), mj, len(dmin), mn))
        errs(ed, '등고선')

        print('== u 맨땅 위 관로 · 터파기 · 재기')
        ed.js('__map3d.setPipes(true); __map3d.setMode("trench")')
        ed.js('__t.look(0, %f, 25, 45, 60)' % s0); ed.frames(4)
        pit = J(ed, 'window.__pipes')
        shot(ed, SD, '44_맨땅_터파기.png')
        ed.js('__map3d.SHOW.lbl = false; __map3d.redraw()')                # 이름표 (어두운 바탕) 빼고 빈 화소 셈 (check_map3d_ug 와 같은 잣대)
        iu2 = shot(ed, SD, '_u.png')
        ed.js('__map3d.SHOW.lbl = true; __map3d.redraw()')
        bgp, nb = 0, 0
        for q in J(ed, '__t.line(0, false)')[::3]:
            if q[0] > 1280:
                continue
            bgp += int((np.abs(box(iu2, q[0], q[1], 30) - ug.BG).sum(axis=2) <= 6).sum()); nb += 1
        report('u 터파기 구덩이 (맨땅) · 관 둘레 빈 화소 없음', pit['pit'] and pit['tris'] > 0 and nb > 5 and bgp < 10, '%s · 빈 화소 %d · 상자 %d' % (pit, bgp, nb))
        ed.js('__map3d.setMode("area")')
        ed.js('__t.look(0, %f, 40, 50, 60)' % s0); ed.frames(3)
        pt = J(ed, '__t.at(0, %f, true)' % s0)
        inp.click(pt[0], pt[1])
        report('u 관 누르기 → 관 정보', not J(ed, "document.getElementById('pipeCard').hidden"))
        inp.el_click('pcX')
        sp = empty_spots(ed)
        A, B = sp[0], sp[1]
        w = pick(ed, *A)
        rclick(ed, *A)
        hd = J(ed, "document.querySelector('#ctxMenu .cHd').textContent")
        zg = DM.at(w[0], w[1])
        report('u 메뉴 좌표 = 땅 · 맨땅 항목 없음', 'dtm' not in J(ed, '__map3d.ctx().items') and hd.endswith('Z %.2f' % (w[2] + H0)) and abs(w[2] + H0 - zg) < 0.15, '%s · 땅 %.2f' % (hd, zg))
        q = item_xy(ed, 'mDist'); inp.click(*q)
        inp.click(*B)
        inp.key('Enter', 'Enter', 13)
        ms = J(ed, '__map3d.MS.pts')
        okd = J(ed, '__map3d.MS.done') and len(ms) == 2 and all(abs(p[2] + H0 - DM.at(p[0], p[1])) < 0.15 for p in ms)
        report('u 거리 재기 점 = 땅', okd, str([round(p[2] + H0 - DM.at(p[0], p[1]), 3) for p in ms]))
        inp.el_click('msClr')
        errs(ed, '맨땅 위')

        print('== o 모델 보기로')
        ed.js('__t.look(0, %f, 60, 50, 60)' % s0); ed.frames(2)
        ug.ui_click(ed, inp, 'bDtm')
        ed.frames(3)
        im2 = shot(ed, SD, '45_모델로.png')
        md = np.abs(im2 - im0).sum(axis=2)
        report('o 꺼짐 · 범례 숨김 · 처음 화면과 같음', not J(ed, '__map3d.DT.on') and J(ed, "document.getElementById('dtLeg').hidden") and (md > 40).mean() < 0.01,
               '다른 화소 %.2f %%' % ((md > 40).mean() * 100))
        errs(ed, '모델로')

        print('== n 땅 아님 (프로그램)')
        ug.ui_click(ed, inp, 'bDtm')
        ed.js('__map3d.setPipes(false)')
        cxy = (d['x0'] + 128 * DM.cell * 0.85, d['y0'] + 128 * DM.cell * 0.8)
        gz = DM.atz(*cxy)
        ed.js('(() => { const V = __map3d.V; V.tgt = [%f, %f, %f]; V.d = 90; V.el = 50; V.az = 20; __map3d.redraw(); })()' % (cxy[0], cxy[1], gz - H0)); ed.frames(2)
        ug.cat_open(ed, inp, 'bTrim', fresh=True)
        ok = ed.wait_js("document.getElementById('ngState').textContent === '땅 아님 없음'", 5)
        st = J(ed, "[!document.getElementById('ngRow').hidden, document.getElementById('ngClear').hidden]")
        report('n 다듬기 칸 «땅 아님 없음» · [그리기] · 모두 없앰 숨김', ok and st == [True, True] and len(calls(ed, 'nonground_get')) >= 1, str(st))
        ug.ui_click(ed, inp, 'ngDraw')
        s = J(ed, "{ k: __map3d.MS.kind, el: __map3d.V.el, chip: document.getElementById('msChip').classList.contains('show'), end: document.getElementById('msEnd').disabled, t: document.getElementById('msEnd').textContent }")
        report('n [그리기] → 위에서 · 안내 줄 · [적용] 꺼짐', s['k'] == 'ng' and s['el'] > 80 and s['chip'] and s['end'] and s['t'] == '적용', str(s))
        ed.frames(2)
        c = (680, 470)
        P1 = [(c[0] - 220, c[1] - 140), (c[0] - 70, c[1] - 140), (c[0] - 70, c[1] + 10), (c[0] - 220, c[1] + 10)]
        for q in P1[:-1]:
            inp.click(*q)
        inp.dblclick(*P1[-1])
        s = J(ed, '{ nd: __map3d.NG.draft.length, n: __map3d.MS.pts.length, d0: __map3d.NG.draft[0] && __map3d.NG.draft[0].length, end: document.getElementById("msEnd").disabled }')
        report('n 첫 다각형: 두 번 누르기 → 닫힘 (점 넷) · [적용] 켜짐', s == {'nd': 1, 'n': 0, 'd0': 4, 'end': False}, str(s))
        P2 = [(c[0] + 60, c[1] - 120), (c[0] + 230, c[1] - 120), (c[0] + 150, c[1] + 40)]
        for q in P2:
            inp.click(*q)
        inp.key('Enter', 'Enter', 13)
        report('n 둘째 다각형: Enter → 닫힘 (점 셋)', J(ed, '[__map3d.NG.draft.length, __map3d.MS.pts.length, __map3d.NG.draft[1].length]') == [2, 0, 3])
        v0 = J(ed, '__map3d.NG.draft[0][1]')
        s0p = J(ed, '__d.scr(__map3d.NG.draft[0][1])')
        inp.drag((s0p[0], s0p[1]), (s0p[0] + 40, s0p[1] + 30))
        v1 = J(ed, '__map3d.NG.draft[0][1]')
        report('n 닫은 다각형 꼭짓점 끌기', math.hypot(v1[0] - v0[0], v1[1] - v0[1]) > 1.0 and J(ed, '[__map3d.NG.draft.length, __map3d.MS.pts.length]') == [2, 0],
               '%.2f m' % math.hypot(v1[0] - v0[0], v1[1] - v0[1]))
        inp.key('Backspace', 'Backspace', 8)
        r1 = J(ed, '[__map3d.NG.draft.length, __map3d.MS.pts.length]')
        inp.key('Enter', 'Enter', 13)
        report('n Backspace = 닫은 것 다시 열기 · Enter = 다시 닫기', r1 == [1, 3] and J(ed, '[__map3d.NG.draft.length, __map3d.MS.pts.length]') == [2, 0], str(r1))
        shot(ed, SD, '50_땅아님_그리기.png')
        drafts = J(ed, '__map3d.NG.draft')
        ed.js('window.__mark = 1')
        inp.el_click('msEnd')
        okr = wait_reload(ed)
        after_reload()
        ns = json.loads(J(ed, "sessionStorage.getItem('__nonground_set') || 'null'") or 'null')
        okp = bool(ns) and len(ns['polygons']) == 2 and all(len(ns['polygons'][i]) == len(drafts[i]) and max(math.hypot(ns['polygons'][i][j][0] - drafts[i][j][0] - O[0], ns['polygons'][i][j][1] - drafts[i][j][1] - O[1])
                                                                                                         for j in range(len(drafts[i]))) < 0.002 for i in range(2))
        report('n [적용] → nonground_set (새 것 둘 · 도면 좌표) → 진행 → 다시 엶', okr and okp, str(ns and [len(p) for p in ns['polygons']]))
        ok = ed.wait_js('__map3d.NG.polys.length === 2 && __map3d.DT.on', 5)
        report('n 다시 연 뒤: 맨땅 그대로 · 땅 아님 2곳 (붉은 점선)', ok and J(ed, '__map3d.ngShown()') and J(ed, "document.getElementById('ngState').textContent") == '땅 아님 2곳')
        ed.js('__map3d.setPipes(false)')
        pg = J(ed, '__map3d.NG.polys[0]')
        ctr = (sum(q[0] for q in pg) / len(pg) - O[0], sum(q[1] for q in pg) / len(pg) - O[1])
        ed.js('__d.top(%f, %f, %f, 80)' % (ctr[0], ctr[1], DM.atz(*ctr) - H0)); ed.frames(3)
        ia = shot(ed, SD, '51_땅아님_적용후.png')
        red_edges = 0
        for j in range(len(pg)):
            pa, pb = pg[j], pg[(j + 1) % len(pg)]
            mx, my = (pa[0] + pb[0]) / 2 - O[0], (pa[1] + pb[1]) / 2 - O[1]
            sxy = J(ed, '__d.scr([%f, %f, %f])' % (mx, my, DM.atz(mx, my) - H0))
            if sxy:
                bx = box(ia, sxy[0], sxy[1], 7)
                if ((bx[:, :, 0] > 190) & (bx[:, :, 1] < 150) & (bx[:, :, 2] < 150)).sum() >= 3:
                    red_edges += 1
        report('n 붉은 선 (변 가운데)', red_edges >= len(pg) - 1, '%d / %d 변' % (red_edges, len(pg)))
        cs = J(ed, '__d.scr([%f, %f, %f])' % (ctr[0], ctr[1], DM.atz(*ctr) - H0))
        inp.click(*cs)
        oks = J(ed, '__map3d.NG.sel') == 0 and ed.wait_js("!!document.querySelector('#toast.show button')", 3)
        shot(ed, SD, '52_땅아님_지우기.png')
        keep = J(ed, '__map3d.NG.polys[1]')
        ed.js('window.__mark = 1')
        okc = toast_btn(ed, inp)
        okr = wait_reload(ed)
        after_reload()
        ns = json.loads(J(ed, "sessionStorage.getItem('__nonground_set') || 'null'") or 'null')
        report('n 다각형 누르기 → 지우기 → 나머지 하나만 보냄 → 다시 엶', oks and okc and okr and ns and ns['polygons'] == [keep], str(ns and len(ns['polygons'])))
        ed.wait_js('__map3d.NG.polys.length === 1', 5)
        ed.js('__map3d.setPipes(false)')
        pg = J(ed, '__map3d.NG.polys[0]')
        ctr = (sum(q[0] for q in pg) / len(pg) - O[0], sum(q[1] for q in pg) / len(pg) - O[1])
        ed.js('__d.top(%f, %f, %f, 80)' % (ctr[0], ctr[1], DM.atz(*ctr) - H0)); ed.frames(3)
        cs = J(ed, '__d.scr([%f, %f, %f])' % (ctr[0], ctr[1], DM.atz(*ctr) - H0))
        rclick(ed, *cs)
        it_in = J(ed, '__map3d.ctx().items')
        inp.key('Escape', 'Escape', 27)
        far = (cs[0] - 330, cs[1])
        wf = pick(ed, *far)
        rclick(ed, *far)
        it_out = J(ed, '__map3d.ctx().items')
        report('n 메뉴: 안 = «이 땅 아님 지우기» · 밖 = 없음 · «여기 땅 아님 지정» 뺌 (JACK 1127)', 'ngDel' in it_in and 'ngHere' not in it_in and 'ngDel' not in it_out and 'ngHere' not in it_out, '%s / %s' % (it_in[-3:], it_out[-3:]))
        inp.key('Escape', 'Escape', 27)
        ug.ui_click(ed, inp, 'ngDraw')                             # 땅 아님 = 옵션 칸 [땅 아님] → 첫 점 누르기
        ed.frames(3)
        wf = pick(ed, *far)
        inp.click(*far)
        s = J(ed, '{ k: __map3d.MS.kind, p: __map3d.MS.pts[0], el: __map3d.V.el, tb: !document.getElementById("trimBox").hidden }')
        report('n [땅 아님] (옵션 칸) → 첫 점 = 누른 자리 · 위에서 · 다듬기 칸', s['k'] == 'ng' and wf and s['p'] and max(abs(s['p'][i] - wf[i]) for i in range(3)) < 1e-3 and s['el'] > 80 and s['tb'], str(s))
        inp.key('Escape', 'Escape', 27)
        report('n Esc → 취소', J(ed, '__map3d.MS.kind') == '' and J(ed, '__map3d.NG.draft.length') == 0)
        ok = ed.wait_js("!document.getElementById('ngClear').hidden", 5)
        ed.js('window.__mark = 1')
        ug.ui_click(ed, inp, 'ngClear')
        okc = toast_btn(ed, inp)
        okr = wait_reload(ed)
        after_reload()
        ns = json.loads(J(ed, "sessionStorage.getItem('__nonground_set') || 'null'") or 'null')
        okn = ed.wait_js('__map3d.NG.got && __map3d.NG.polys.length === 0', 5)
        report('n [모두 없앰] → 빈 목록 → 다시 엶', ok and okc and okr and ns == {'polygons': []} and okn and J(ed, "document.getElementById('ngState').textContent") == '땅 아님 없음')
        errs(ed, '땅 아님')

        print('== w 브라우저 (웹 공유)')
        page('index_mt.html')
        ed.js('__t.look(0, %f, 40, 50, 60)' % s0); ed.frames(3)
        st = J(ed, "[" + ug.HOST_HIDDEN + ", !document.getElementById('bDtm').hidden, __map3d.DT.on]")
        if st[2]:
            ug.ui_click(ed, inp, 'bDtm')
        ug.ui_click(ed, inp, 'bDtm')
        rclick(ed, 640, 450)
        it = J(ed, '__map3d.ctx().items')
        inp.key('Escape', 'Escape', 27)
        report('w 맨땅 됨 · 땅 아님 항목 없음', st[0] and st[1] and J(ed, '__map3d.DT.on') and 'dtm' not in it and not any(k in it for k in ('ngHere', 'ngDel')) and not J(ed, '__map3d.ngShown()'), str(it))
        shot(ed, SD, '60_브라우저_맨땅.png')
        errs(ed, '브라우저')

        print('== x mtiles 없음 · dem.js 없음')
        page('index_host.html')
        if not J(ed, '__map3d.DT.on'):
            ug.ui_click(ed, inp, 'bDtm')
        lg = J(ed, "[document.getElementById('dtMt').hidden, document.getElementById('dtTx').textContent, __map3d.dt().mt]")
        report('x mtiles 없음 → 범례에 메운 곳 없음', J(ed, '__map3d.DT.on') and lg[0] and not lg[2] and lg[1].startswith('EL '), lg[1])
        shot(ed, SD, '61_메운곳표시없음.png')
        page('index_nodem.html')
        rclick(ed, 640, 450)
        it = J(ed, '__map3d.ctx().items')
        inp.key('Escape', 'Escape', 27)
        report('x dem.js 없음 → 단추 · 메뉴 없음', J(ed, "document.getElementById('bDtm').hidden") and 'dtm' not in it and not J(ed, '__map3d.DT.on'), str(it))
        ed.js('sessionStorage.clear(); localStorage.clear()')
        errs(ed, 'dem 없음')
    finally:
        ed.__exit__(None, None, None)
        srv.shutdown()

    if a.flat and os.path.exists(os.path.join(a.flat, 'data', 'mapping.js')):
        print('== f 3D 없는 프로젝트')
        fd = os.path.abspath(a.flat)
        copy_site(fd)
        srv, base = serve(fd)
        with Edge(os.path.join(SD, '_prof5'), WIN, gl=False) as ed:
            ed.goto(base + 'index_host.html')
            ed.wait_js(Edge.NEW + " && window.__map3d && window.__map3d.flat === true", 30)
            ed.js("document.getElementById('bTrim').click()")
            st = J(ed, "[getComputedStyle(document.getElementById('bDtm')).display, document.getElementById('ngRow').hidden]")
            report('f 맨땅 단추 · 땅 아님 줄 없음', st == ['none', True], str(st))
            errs(ed, '3D 없음')
        srv.shutdown()

    for where, e in ug.R['errs']:
        print('  오류', where, e)
    print('== %s · 실패 %d %s' % ('통과' if ug.R['ok'] else '실패', len(ug.R['fails']), ug.R['fails']))
    return 0 if ug.R['ok'] else 1


if __name__ == '__main__':
    sys.exit(main())
