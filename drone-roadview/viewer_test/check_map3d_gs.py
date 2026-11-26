"""3D 매핑 보기 · 가우시안 보기 시험 (JACK 1126): 화면 없는 Edge (cdp.py).

    python -I check_map3d_gs.py --out <가우시안이 든 3D 매핑 결과 폴더> --shots <그림 폴더> [--gpu]

  h  http (웹 공유와 같음 · swiftshader): [가우시안] 단추 보임 · 켜기 → 불러옴 · 정렬 · 그림이 바탕이 아님 · 모델 보기와 다름 ·
     시점을 돌리면 다시 정렬 · 고르기 (모델 그물) · 오른쪽 메뉴 «가우시안 보기 / 모델 보기» · 맨땅과 하나만 · 다시 열면 그대로 (세션)
  f  file:// (앱과 같음 · --allow-file-access-from-files): 불러옴 · Worker · 그림
  g  --gpu: 실제 그래픽카드 (d3d11) 로 같은 시점 모델 · 가우시안 그림 (화질 견줌) · 그리기 시간
  u  (--only u · 관로가 든 프로젝트) 실제 그래픽카드: 가우시안 위 터파기 영역 띠 · 구덩이 · 관 · 고르기
  o  (--only o · 가우시안만 만든 프로젝트) 가우시안 자동 · 고르기 = 맨땅 · 맨땅과 오가기
  모든 쪽 JS 오류 0
"""
import argparse
import os
import sys
import time

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
sys.path.insert(0, REPO)
from cdp import Edge  # noqa: E402
import check_map3d_ug as ug  # noqa: E402
from check_map3d_ug import J, errs, report, serve, shot  # noqa: E402

WIN = (1600, 900)
NEW = "document.readyState === 'complete'"


def wait_ready(ed, timeout=240):
    return ed.wait_js("window.__map3d && document.getElementById('load').style.display === 'none' && window.__drawn > 0", timeout)


def wait_gs(ed, timeout=300):
    return ed.wait_js("window.__map3d && __map3d.gs().ready && __map3d.gs().sorted > 0", timeout)


def look(ed, az, el, dfrac):
    J(ed, "(() => { const M = __map3d, m = M.mesh().meta; M.V.tgt = [(m.min[0] + m.max[0]) / 2, (m.min[1] + m.max[1]) / 2, m.min[2] + (m.max[2] - m.min[2]) * 0.3];"
          " M.V.d = Math.hypot(m.max[0] - m.min[0], m.max[1] - m.min[1]) * %f; M.V.az = %f; M.V.el = %f; M.redraw(); return 1; })()" % (dfrac, az, el))


def settle(ed, n=30):
    """정렬이 지금 시점을 따라잡을 때까지 (정렬 수가 더 안 늚)."""
    last = -1
    for _ in range(n):
        ed.frames(4)
        s = J(ed, '__map3d.gs().sorts')
        if s == last:
            break
        last = s
        time.sleep(0.3)


def bg_frac(img):
    return float((np.abs(img - ug.BG).sum(2) < 12).mean())


def run_http(out, shots, prof):
    srv, base = serve(out)
    try:
        with Edge(prof, WIN) as ed:
            ed.goto(base + 'index.html')
            report('h 화면 열림', wait_ready(ed))
            report('h [가우시안] 단추 보임', J(ed, "!document.getElementById('bGs').hidden"))
            look(ed, 25, 38, 0.6)
            a = shot(ed, shots, 'h_모델.png')
            t0 = time.time()
            J(ed, "(document.getElementById('bGs').click(), 1)")
            ok = wait_gs(ed)
            st = J(ed, '__map3d.gs()')
            report('h 가우시안 켜기 → 불러옴 · 정렬', ok, '%s개 · 정렬 %s · Worker %s · %.0f초' % (st['n'], st['sorted'], st['worker'], time.time() - t0))
            settle(ed)
            b = shot(ed, shots, 'h_가우시안.png')
            report('h 가우시안 그림이 바탕이 아님', bg_frac(b[60:]) < 0.5, '바탕 %.0f %%' % (100 * bg_frac(b[60:])))
            dif = float(np.abs(a.astype(int) - b.astype(int)).mean())
            report('h 모델 보기와 다름', dif > 3, '평균 차 %.1f' % dif)
            s0 = J(ed, '__map3d.gs().sorts')
            look(ed, 120, 30, 0.5)
            settle(ed)
            s1 = J(ed, '__map3d.gs().sorts')
            report('h 시점을 돌리면 다시 정렬', s1 > s0, '%d → %d' % (s0, s1))
            shot(ed, shots, 'h_가우시안_돌림.png')
            pk = J(ed, '__map3d.pickAt(innerWidth / 2, innerHeight / 2)')
            report('h 고르기 (모델 그물)', bool(pk and pk.get('p')), str(pk)[:80])
            import check_map3d_ctx as cx
            cx.rclick(ed, 800, 450)
            items = J(ed, '__map3d.ctx().items')
            report('h 오른쪽 메뉴 «모델 보기» (가우시안 켜짐)', 'gs' in items and J(ed, "document.querySelector('#ctxMenu [data-id=\"gs\"] span').textContent") == '모델 보기',
                   ' · '.join(items[:8]))
            J(ed, "(document.querySelector('#ctxMenu [data-id=\"gs\"]').click(), 1)")
            ed.frames(4)
            report('h 메뉴로 끄기', not J(ed, '__map3d.gs().on'))
            J(ed, '(__map3d.gsSet(true), 1)'); ed.frames(4)
            if J(ed, "!document.getElementById('bDtm').hidden"):
                J(ed, "(document.getElementById('bDtm').click(), 1)"); ed.frames(4)
                report('h 맨땅 켜면 가우시안 꺼짐 (하나만)', J(ed, '__map3d.dt().on') and not J(ed, '__map3d.gs().on'))
                J(ed, "(document.getElementById('bGs').click(), 1)"); ed.frames(4)
                report('h 가우시안 켜면 맨땅 꺼짐', J(ed, '__map3d.gs().on') and not J(ed, '__map3d.dt().on'))
            ed.goto(base + 'index.html')
            wait_ready(ed)
            report('h 다시 열면 가우시안 그대로 (세션)', wait_gs(ed, 120) and J(ed, '__map3d.gs().on'))
            errs(ed, 'http')
    finally:
        srv.shutdown()


def run_file(out, shots, prof):
    url = 'file:///' + os.path.abspath(os.path.join(out, 'index.html')).replace('\\', '/')
    with Edge(prof, WIN, extra=['--allow-file-access-from-files']) as ed:
        ed.goto(url)
        report('f file:// 화면 열림', wait_ready(ed))
        look(ed, 25, 38, 0.6)
        J(ed, '(__map3d.gsSet(true), 1)')
        ok = wait_gs(ed)
        st = J(ed, '__map3d.gs()')
        report('f file:// 가우시안 불러옴', ok, '%s개 · Worker %s · %s' % (st['n'], st['worker'], st['err']))
        settle(ed)
        b = shot(ed, shots, 'f_가우시안.png')
        report('f 그림이 바탕이 아님', bg_frac(b[60:]) < 0.5)
        errs(ed, 'file')


def run_gpu(out, shots, prof, views):
    srv, base = serve(out)
    try:
        with Edge(prof, WIN, gl=False, extra=['--use-angle=d3d11', '--ignore-gpu-blocklist', '--enable-gpu']) as ed:
            ed.goto(base + 'index.html')
            report('g 화면 열림 (그래픽카드)', wait_ready(ed))
            rnd = J(ed, "(() => { const g = document.getElementById('c').getContext('webgl2'), e = g.getExtension('WEBGL_debug_renderer_info'); return e ? g.getParameter(e.UNMASKED_RENDERER_WEBGL) : '?'; })()")
            report('g 그래픽 장치', 'SwiftShader' not in str(rnd), str(rnd)[:90])
            J(ed, '(__map3d.gsSet(true), 1)')
            wait_gs(ed)
            for name, (az, el, d) in views.items():
                J(ed, '(__map3d.gsSet(false), 1)'); look(ed, az, el, d); ed.frames(6); time.sleep(1.0)
                shot(ed, shots, 'g_%s_모델.png' % name)
                J(ed, '(__map3d.gsSet(true), 1)'); settle(ed); time.sleep(0.5)
                shot(ed, shots, 'g_%s_가우시안.png' % name)
            ms = ed.js("new Promise((res) => { const M = __map3d; let k = 0, t0 = performance.now(); function f() { M.V.az += 1; M.redraw(); if (++k < 60) requestAnimationFrame(f); else res((performance.now() - t0) / 60); } requestAnimationFrame(f); })", True)
            report('g 돌리는 동안 한 화면 시간', ms is not None and ms < 60, '%.1f ms' % (ms or -1))
            errs(ed, 'gpu')
    finally:
        srv.shutdown()


def run_ug(out, shots, prof):
    """u: 관로가 든 프로젝트 (pipes.js) · 실제 그래픽카드 — 가우시안 위 터파기 영역 띠 · 터파기 구덩이 · 관 · 고르기 · 같은 시점 모델과 견줌."""
    srv, base = serve(out)
    try:
        with Edge(prof, WIN, gl=False, extra=['--use-angle=d3d11', '--ignore-gpu-blocklist', '--enable-gpu']) as ed:
            report('u 화면 열림 (관로)', ug.setup_page(ed, base + 'index.html'))
            J(ed, '(__map3d.gsSet(true), 1)')
            report('u 가우시안 불러옴', wait_gs(ed))
            sm = J(ed, 'Math.round(__t.L(0).len / 2)')                # 관 가운데 (모델 안)
            for mode, name in (('area', '영역'), ('trench', '터파기')):
                J(ed, "(__map3d.setMode('%s'), __t.along(0, %d, 18, 35), 1)" % (mode, sm))
                settle(ed); time.sleep(0.5)
                b = shot(ed, shots, 'u_%s_가우시안.png' % name)
                p = J(ed, 'window.__pipes || null')
                report('u %s · 가우시안 위 관 그림' % name, bool(p and p['tris'] > 0 and (mode != 'trench' or p['pit'])), str(p))
                J(ed, '(__map3d.gsSet(false), 1)'); ed.frames(6); time.sleep(0.5)
                a = shot(ed, shots, 'u_%s_모델.png' % name)
                J(ed, '(__map3d.gsSet(true), 1)'); settle(ed)
                q = J(ed, '__t.at(0, %d, false)' % sm)
                if q:
                    x, y = int(q[0]), int(q[1])
                    dif = float(np.abs(a[y - 30:y + 30, x - 30:x + 30].astype(int) - b[y - 30:y + 30, x - 30:x + 30].astype(int)).mean())
                    report('u %s · 관 자리 둘레가 모델 보기와 비슷 (같은 깊이 · 같은 자리)' % name, dif < 60, '평균 차 %.1f' % dif)
            look(ed, 25, 38, 0.6); settle(ed)                       # 관 25 m 지점은 모델 가장자리 밖일 수 있음 → 모델 가운데에서 고름
            pk = J(ed, '__map3d.pickAt(innerWidth / 2, innerHeight / 2)')
            report('u 고르기 (가우시안 켜고 · 모델 그물)', bool(pk and pk.get('p')), str(pk)[:70])
            errs(ed, 'ug')
    finally:
        srv.shutdown()


def run_gsonly(out, shots, prof):
    """o: 가우시안만 만든 프로젝트 (outputs 에 mesh 없음 · dem.js 있음) — 3D 화면 · 가우시안 자동 · 고르기 = 맨땅 · 맨땅과 오가기."""
    srv, base = serve(out)
    try:
        with Edge(prof, WIN) as ed:
            ed.goto(base + 'index.html')
            report('o 가우시안만 · 화면 열림 (평면 그림 아님)', wait_ready(ed) and not J(ed, "!!document.getElementById('flat')"))
            report('o 가우시안 자동 켜짐 · 정렬', wait_gs(ed) and J(ed, '__map3d.gs().on'))
            report('o [가우시안] 단추 숨김 (끌 수 없음)', J(ed, "document.getElementById('bGs').hidden"))
            settle(ed)
            b = shot(ed, shots, 'o_가우시안만.png')
            report('o 그림이 바탕이 아님', bg_frac(b[60:]) < 0.7, '바탕 %.0f %%' % (100 * bg_frac(b[60:])))
            pk = J(ed, '__map3d.pickAt(innerWidth / 2, innerHeight / 2)')
            report('o 고르기 = 맨땅 그물', bool(pk and pk.get('p')))
            J(ed, "(document.getElementById('bDtm').click(), 1)"); ed.frames(4)
            report('o 맨땅 켜면 가우시안 꺼짐', J(ed, '__map3d.dt().on') and not J(ed, '__map3d.gs().on'))
            J(ed, "(document.getElementById('bDtm').click(), 1)"); ed.frames(4)
            report('o 맨땅 끄면 가우시안 다시', J(ed, '__map3d.gs().on') and not J(ed, '__map3d.dt().on'))
            errs(ed, 'gs only')
    finally:
        srv.shutdown()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--shots', required=True)
    ap.add_argument('--gpu', action='store_true')
    ap.add_argument('--only', default='hfg')
    a = ap.parse_args()
    if 'u' in a.only:
        run_ug(a.out, a.shots, os.path.join(a.shots, '_edge'))
    if 'o' in a.only:
        run_gsonly(a.out, a.shots, os.path.join(a.shots, '_edge'))
    os.makedirs(a.shots, exist_ok=True)
    prof = os.path.join(a.shots, '_edge')
    if 'h' in a.only:
        run_http(a.out, a.shots, prof)
    if 'f' in a.only:
        run_file(a.out, a.shots, prof)
    if a.gpu and 'g' in a.only:
        run_gpu(a.out, a.shots, prof, {'전체': (25, 38, 0.6), '가까이': (40, 30, 0.12), '위': (0, 89.5, 0.5)})
    print('결과:', '통과' if ug.R['ok'] else '실패 ' + ' · '.join(ug.R['fails']))
    return 0 if ug.R['ok'] else 1


if __name__ == '__main__':
    sys.exit(main())
