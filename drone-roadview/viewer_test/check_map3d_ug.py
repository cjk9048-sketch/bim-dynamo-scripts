"""3D 매핑 보기 · 지하 시설물 시험 (JACK 1126): 결과 폴더를 작은 HTTP 서버로 열고 화면 없는 Edge (cdp.py · 그래픽 카드) 를
진짜 마우스 · 키로 몬다. 그림은 --shots 폴더에 남김.

    python -I check_map3d_ug.py --out <3D 매핑 결과 폴더 (data/mapping.js · mesh.json · pipes.js · dem.js)> --shots <그림 폴더>
         [--old <옛 map3d 보기 폴더 (map3d.html · map3d.js) · 울퉁불퉁 견줌 그림>] [--flat <빈 폴더 · 3D 없는 프로젝트 시험>]

  s  땅 높이: 관 꼭짓점 g = 1 m 마다 땅 (data/dem.js · 따로 셈 = 결과물 DTM) 을 ±5 m 이동평균 (3 cm 안) · 파일 g (DSM) 보다 매끈 (2차 차분 절반 아래)
  a  영역: 땅 위 그림 (임시점용 · 영구점용) 이 모델에 덮임 · 영구점용 끄면 관 둘레 화소가 바뀜
  t  터파기: 구덩이 그림 (스텐실) · 모델 뚫림 → 관 둘레 화소가 영역과 다름 · 빈 화소 (배경색) 없음 · 관 (구멍 안) 화소
  c  관 누르기 → 관 정보 칸 (토피 = 평균매설깊이) · [이 자리 단면] → 터파기 설정 창 · 측점 · 단면 그림
  r  터파기 설정 + → 값 · 이 브라우저 저장 · 구덩이 그물 다시
  h  가시설: 토류벽 → 관 위 두 점 → shore [{a, b, t}] (측점 0.3 m 안) · 목록 한 줄 · 세 종류 그림 다름
  e  관로 고치기: 관 고름 → 손잡이 · 끌기 → 꼭짓점 = 그 자리 3D 모델 (고르기 버퍼) · g = 그 자리 땅 · Ctrl+Z · Ctrl+Y ·
     두 번 누르기 = 점 추가 · Delete · Shift 끌기 = 통째로 · 위에서 보기에서 끌기
  p  프로그램 (hostmock): 끌기 → save_pipes 한 번 (0.8 초 모아) · 설정 → save_calib (trench) · SHP 내보내기
  q  지장물 · 수량 · 간섭 자리
  l  질감 고해상도 (tex_hi): 가까이 → 바꿔 읽음 · 멀리 → 내림
  f  3D 없는 프로젝트 → 평면 미리보기 그대로 · 검사점 칸
  모든 쪽 JS 오류 0
"""
import argparse
import functools
import http.server
import json
import math
import os
import shutil
import sys
import threading
import time

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
sys.path.insert(0, REPO)
from cdp import Edge  # noqa: E402
import check_edit as ce  # noqa: E402

R = {'ok': True, 'errs': [], 'fails': []}
WIN = (1600, 900)
BG = np.array([27, 31, 38])


def report(name, ok, text=''):
    print('   %-46s %s  %s' % (name, text, '통과' if ok else '실패'))
    R['ok'] &= bool(ok)
    if not ok:
        R['fails'].append(name)
    return bool(ok)


def errs(ed, where):
    e = list(ed.exceptions) + [t for k, t in ed.console if k == 'error']
    for x in e:
        R['errs'].append((where, str(x)[:300]))
    ed.exceptions.clear()
    ed.console.clear()
    return report('JS 오류 (%s)' % where, not e, '%d 줄' % len(e) + ((' · ' + str(e[0])[:160]) if e else ''))


def J(ed, expr):
    return json.loads(ed.js('JSON.stringify(%s)' % expr))


# 오른쪽 옵션 칸 = 접는 카테고리 (JACK 1126 배치) — 단추가 접힌 카테고리 안이거나 칸 아래로 굴러 있으면 진짜 마우스가 못 누름
HOST_HIDDEN = "[...document.querySelectorAll('[data-host]')].every((e) => e.hidden)"      # 프로그램 전용 단추 · 카테고리 모두 숨김
HOST_SHOWN = "[...document.querySelectorAll('[data-host]')].every((e) => !e.hidden)"


def ui_click(ed, inp, eid):
    """오른쪽 칸 기능 단추: 그 단추가 든 카테고리를 펴고 (__map3d.reveal = 머리줄과 같은 catSet) 보이게 굴린 뒤 진짜 마우스로 누름."""
    ed.js("window.__map3d && window.__map3d.reveal && window.__map3d.reveal('%s')" % eid)
    ed.frames(2)
    inp.el_click(eid)


def cat_open(ed, inp, hid, fresh=False):
    """카테고리 머리줄을 진짜 마우스로 눌러 폄 (접혀 있을 때만 · fresh = 펴져 있으면 접었다 다시 펴서 상태를 새로 읽게)."""
    ed.js("(() => { if (document.getElementById('side').hidden) document.getElementById('tgSide').click(); document.getElementById('%s').scrollIntoView({ block: 'nearest' }); })()" % hid)
    ed.frames(2)
    if J(ed, "document.getElementById('%s').getAttribute('aria-expanded')" % hid) == 'true':
        if not fresh:
            return
        inp.el_click(hid)
    inp.el_click(hid)


class _Quiet(http.server.SimpleHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def end_headers(self):
        self.send_header('Cache-Control', 'no-store')
        super().end_headers()


class _Srv(http.server.ThreadingHTTPServer):
    def handle_error(self, request, client_address):
        if not isinstance(sys.exc_info()[1], (ConnectionError, OSError)):
            super().handle_error(request, client_address)


def serve(d):
    s = _Srv(('127.0.0.1', 0), functools.partial(_Quiet, directory=d))
    threading.Thread(target=s.serve_forever, daemon=True).start()
    return s, 'http://127.0.0.1:%d/' % s.server_address[1]


T_JS = r"""
window.__t = {
  L(src) { return __map3d.lines().find((q) => q.src === src); },
  at(src, s, top) {                       // 측점 s 의 [화면 x, y, e, n, g, zc] (top = 관 윗면 · 아니면 땅)
    const M = __map3d, L = this.L(src); let k = 0;
    while (k + 2 < L.cum.length && L.cum[k + 1] < s) k++;
    const t = (s - L.cum[k]) / (L.cum[k + 1] - L.cum[k]), P = L.pts;
    const e = P[k * 3] + (P[k * 3 + 3] - P[k * 3]) * t, n = P[k * 3 + 1] + (P[k * 3 + 4] - P[k * 3 + 1]) * t;
    const g = M.gAt(L, k) + (M.gAt(L, k + 1) - M.gAt(L, k)) * t, zc = M.zcAt(L, k) + (M.zcAt(L, k + 1) - M.zcAt(L, k)) * t;
    M.view(); const q = M.toScr(e, n, top ? zc + L.dia / 2 : g);
    return q ? [q[0], q[1], e, n, g, zc] : null;
  },
  look(src, s, d, el, az) {
    const M = __map3d, L = this.L(src), T = M.T(); let k = 0;
    while (k + 2 < L.cum.length && L.cum[k + 1] < s) k++;
    const t = (s - L.cum[k]) / (L.cum[k + 1] - L.cum[k]), P = L.pts;
    const e = P[k * 3] + (P[k * 3 + 3] - P[k * 3]) * t, n = P[k * 3 + 1] + (P[k * 3 + 4] - P[k * 3 + 1]) * t, g = M.gAt(L, k) + (M.gAt(L, k + 1) - M.gAt(L, k)) * t;
    M.V.tgt = [e + T[0], n + T[1], g + T[2]]; M.V.d = d; M.V.el = el; M.V.az = az; M.redraw(); return true;
  },
  along(src, s, d, el) {                  // 관을 따라 보기 (뒤에서 · 낮게)
    const L = this.L(src), P = L.pts; let k = 0;
    while (k + 2 < L.cum.length && L.cum[k + 1] < s) k++;
    const ux = P[k * 3 + 3] - P[k * 3], uy = P[k * 3 + 4] - P[k * 3 + 1];
    return this.look(src, s, d, el, Math.atan2(-ux, uy) * 180 / Math.PI);
  },
  line(src, top) {                        // 관 줄 화면 점들 (측점 1 m 마다 · 화면 안만)
    const L = this.L(src), out = [];
    for (let s = 0; s <= L.len; s += 1) { const q = this.at(src, s, top); if (q && q[0] > 0 && q[0] < innerWidth && q[1] > 60 && q[1] < innerHeight) out.push(q.slice(0, 2)); }
    return out;
  },
};
"""


def setup_page(ed, url, timeout=180):
    ed.goto(url)
    ok = ed.wait_js(Edge.NEW + " && window.__map3d && document.getElementById('load').style.display === 'none' && window.__drawn > 0", timeout)
    ed.js(T_JS)
    ed.frames(3)
    return ok


def shot(ed, d, name):
    ed.frames(3)
    p = os.path.join(d, name)
    ed.screenshot(p)
    return np.asarray(Image.open(p).convert('RGB')).astype(int)


def near_box(img, x, y, r):
    h, w = img.shape[:2]
    x0, x1, y0, y1 = max(0, int(x - r)), min(w, int(x + r) + 1), max(0, int(y - r)), min(h, int(y + r) + 1)
    return img[y0:y1, x0:x1]


# ---------------------------------------------------------------- s: 땅 높이 (따로 셈)
def check_smooth(ed, out):
    from roadview import mapping
    g = mapping.ground_sampler(out)
    M = mapping._read_js(os.path.join(out, 'data', 'mapping.js'), 'RV_MAPPING')
    E0, N0 = M['origin'][0], M['origin'][1]
    for src in (0, 1):
        d = J(ed, "(() => { const L = __t.L(%d), M = __map3d; return { p: Array.from(L.pts), cum: Array.from(L.cum), g: Array.from(L.g), raw: Array.from(L.gRaw), o: window.RV_PIPES.origin }; })()" % src)
        p = np.array([v if v is not None else np.nan for v in d['p']], float).reshape(-1, 3)[:, :2] + [d['o']['E'], d['o']['N']]
        cum = np.array(d['cum']); L = cum[-1]; n = max(1, math.ceil(L / 1.0)); ds = L / n
        st = np.arange(n + 1) * ds
        x = np.interp(st, cum, p[:, 0]); y = np.interp(st, cum, p[:, 1])
        z = g(x, y)
        h = max(1, round(5.0 / ds))
        sm = np.array([np.nanmean(z[max(0, i - h):i + h + 1]) for i in range(n + 1)])
        want = np.interp(cum, st, sm)
        got = np.array(d['g'])
        diff = np.nanmax(np.abs(got - want))
        report('s 관 %d 땅 = DEM 1 m · ±5 m 평균 (따로 셈)' % src, diff < 0.03, '최대 차 %.3f m · 점 %d' % (diff, len(got)))
        raw = np.array([v if v is not None else np.nan for v in d['raw']], float)
        r2 = lambda a: np.nanmean(np.abs(np.diff(a, 2))) if len(a) > 2 else 0.0   # noqa: E731
        if np.isfinite(raw).sum() > 3:
            report('s 관 %d 매끈함 (2차 차분 · 파일 DSM 대비)' % src, r2(got) < 0.5 * r2(raw) + 1e-6, '%.3f → %.3f m' % (r2(raw), r2(got)))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--shots', required=True)
    ap.add_argument('--old', default=None)
    ap.add_argument('--flat', default=None)
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    out, SD = os.path.abspath(a.out), os.path.abspath(a.shots)
    os.makedirs(SD, exist_ok=True)
    os.makedirs(os.path.join(out, 'viewer'), exist_ok=True)
    shutil.copy2(os.path.join(REPO, 'viewer', 'map3d.html'), os.path.join(out, 'index.html'))
    shutil.copy2(os.path.join(REPO, 'viewer', 'map3d.js'), os.path.join(out, 'viewer', 'map3d.js'))
    shutil.copy2(os.path.join(REPO, 'viewer', 'river.js'), os.path.join(out, 'viewer', 'river.js'))   # 하천 추진 · 횡단 셈
    shutil.copy2(os.path.join(REPO, 'viewer', 'cost.js'), os.path.join(out, 'viewer', 'cost.js'))     # 개략공사비 셈 (1127)
    shutil.copy2(os.path.join(HERE, 'hostmock.js'), os.path.join(out, 'viewer', 'hostmock.js'))
    html = open(os.path.join(out, 'index.html'), encoding='utf-8').read()
    open(os.path.join(out, 'index_host.html'), 'w', encoding='utf-8').write(
        html.replace('<script src="viewer/map3d.js"></script>', '<script src="viewer/hostmock.js"></script>\n<script src="viewer/map3d.js"></script>'))
    mj = os.path.join(out, 'data', 'mesh.json')
    meta0 = open(mj, encoding='utf-8').read()
    srv, base = serve(out)
    ed = Edge(os.path.join(SD, '_prof'), WIN, gl=False).__enter__()
    inp = ce.Inp(ed)
    try:
        print('== 열기')
        report('열림 · 모델 · 관로', setup_page(ed, base + 'index.html'), J(ed, 'window.__pipes'))
        ed.js('localStorage.clear()')
        setup_page(ed, base + 'index.html')
        errs(ed, '열기')
        print('== s 땅 높이')
        check_smooth(ed, out)

        print('== a 영역')
        L0 = J(ed, '__t.L(0).len'); s0 = round(L0 * 0.3, 1)
        ed.js('__t.look(0, %f, 24, 42, 60)' % s0)
        img_a = shot(ed, SD, '01_영역.png')
        report('a 땅 위 그림 켜짐', J(ed, '__map3d.DC.g.on || __map3d.DC.l.on'))
        p = J(ed, '__t.at(0, %f, false)' % s0)
        ed.js("__map3d.SHOW.perm = false; __map3d.redraw()")
        ed.frames(3)
        img_np = shot(ed, SD, '_영역_영구끔.png')
        ed.js("__map3d.SHOW.perm = true; __map3d.redraw()")
        dif = np.abs(near_box(img_a, p[0], p[1], 6) - near_box(img_np, p[0], p[1], 6)).sum(2)
        report('a 영구점용 끄면 관 위 화소 바뀜', (dif > 40).mean() > 0.3, '%.0f %%' % (100 * (dif > 40).mean()))

        print('== t 터파기')
        ed.js("__map3d.setMode('trench')")
        ed.frames(4)
        img_t = shot(ed, SD, '02_터파기.png')
        report('t 구덩이 그림', J(ed, 'window.__pipes.pit') is True)
        dif = np.abs(near_box(img_a, p[0], p[1], 12) - near_box(img_t, p[0], p[1], 12)).sum(2)
        report('t 관 둘레 = 영역과 다름 (모델 뚫림)', (dif > 40).mean() > 0.5, '%.0f %%' % (100 * (dif > 40).mean()))
        bgc, nb = 0, 0
        for q in J(ed, '__t.line(0, false)')[::3]:                 # 관 둘레 (구덩이 · 벽 · 바닥) 에 배경색 (뚫린 틈) 이 없어야
            if q[0] > 1280:
                continue
            b = near_box(img_t, q[0], q[1], 30)
            bgc += int((np.abs(b - BG).sum(2) <= 6).sum()); nb += 1
        report('t 관 둘레 빈 화소 (배경색) 없음', nb > 5 and bgc < 10, '%d 화소 · 상자 %d' % (bgc, nb))
        pt = J(ed, '__t.at(0, %f, true)' % s0)
        c = near_box(img_t, pt[0], pt[1], 1).reshape(-1, 3).mean(0)
        report('t 구멍 안 관 (주철 짙은 회색)', c.max() - c.min() < 40 and c.mean() < 140, str(c.round()))
        ed.js("__map3d.trSet({ on: false })"); ed.frames(3)
        img_off = shot(ed, SD, '_터파기_끔.png')
        ed.js("__map3d.trSet({ on: true })"); ed.frames(3)
        dif = np.abs(near_box(img_off, p[0], p[1], 12) - near_box(img_t, p[0], p[1], 12)).sum(2)
        report('t 터파기 형상 끄면 다름', (dif > 40).mean() > 0.3, '%.0f %%' % (100 * (dif > 40).mean()))
        ed.js('__t.look(0, %f, 9, 24, 150)' % s0)
        shot(ed, SD, '02b_터파기_낮게.png')
        ed.js('__t.look(1, 1.5, 16, 50, 200)')
        shot(ed, SD, '02c_터파기_가지.png')
        errs(ed, '터파기')

        print('== c 관 누르기 · 단면')
        ed.js('__t.look(0, %f, 24, 42, 60)' % s0)
        ed.frames(3)
        pt = J(ed, '__t.at(0, %f, true)' % s0)
        inp.click(pt[0], pt[1])
        card = J(ed, "(() => { const c = document.getElementById('pipeCard'); return { hid: c.hidden, cov: Number((document.getElementById('pc_cover') || {}).dataset ? document.getElementById('pc_cover').dataset.v : NaN), src: c.dataset.src }; })()")
        report('c 관 정보 칸 · 토피 = 평균매설깊이', not card['hid'] and abs(card['cov'] - 1.2) < 0.011, str(card))
        shot(ed, SD, '03_관정보.png')
        inp.el_click('pcSec')
        sec = J(ed, "(() => { const d = document.getElementById('secDlg'); return { hid: d.hidden, at: d.dataset.at, svg: document.getElementById('secSvg').innerHTML.length, toh: document.getElementById('secSvg').innerHTML.indexOf('토피') >= 0 }; })()")
        report('c 이 자리 단면 창 · 측점 · 그림', not sec['hid'] and sec['at'] != '' and abs(float(sec['at']) - s0) < 0.6 and sec['toh'], str(sec))
        shot(ed, SD, '04_단면창.png')

        print('== r 터파기 설정')
        k0 = J(ed, "__map3d.tr().margin")
        btn = J(ed, "(() => { const i = document.getElementById('sec_margin'), r = i.nextElementSibling.getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; })()")
        inp.click(*btn)
        k1 = J(ed, "__map3d.tr().margin")
        ls = J(ed, "JSON.parse(localStorage.getItem(Object.keys(localStorage).find((k) => k.indexOf('rv_trench:') === 0)) || 'null')")
        report('r 여유폭 + → 값 · 이 브라우저 저장', abs(k1 - k0 - 0.05) < 1e-6 and ls and abs(ls['margin'] - k1) < 1e-6, '%s → %s' % (k0, k1))
        shot(ed, SD, '04b_단면창_고침.png')
        inp.el_click('secDef')
        inp.el_click('secClose')
        errs(ed, '단면')

        print('== h 가시설')
        cat_open(ed, inp, 'tgShore')
        ui_click(ed, inp, 'shHpile')
        sa, sb = round(L0 * 0.25, 1), round(L0 * 0.25 + 9.0, 1)
        ed.js('__t.look(0, %f, 26, 48, 60)' % ((sa + sb) / 2))
        ed.frames(3)
        for s in (sa, sb):
            q = J(ed, '__t.at(0, %f, true)' % s)
            inp.click(q[0], q[1])
        sh = J(ed, 'window.RV_PIPES.lines[0].shore || null')
        okh = bool(sh) and len(sh) == 1 and sh[0]['t'] == 'hpile' and abs(sh[0]['a'] - sa) < 0.3 and abs(sh[0]['b'] - sb) < 0.3
        report('h 토류벽 구간 (측점)', okh, str(sh))
        report('h 목록 한 줄', J(ed, "document.querySelectorAll('#shList .shRow').length") == 1)
        ed.js('__t.look(0, %f, 11, 32, 120)' % ((sa + sb) / 2))
        imgs = {}
        for t in ('hpile', 'panel', 'sheet'):
            ed.js("(() => { const s = document.querySelector('#shList select'); s.value = '%s'; s.dispatchEvent(new Event('change')); })()" % t)
            ed.frames(4)
            imgs[t] = shot(ed, SD, '05_가시설_%s.png' % {'hpile': '토류벽', 'panel': '간이흙막이', 'sheet': '시트파일'}[t])
        dd = [np.abs(imgs[x] - imgs[y]).sum(2).mean() for x, y in (('hpile', 'panel'), ('panel', 'sheet'), ('hpile', 'sheet'))]
        report('h 세 종류 그림 다름', min(dd) > 0.5, str([round(v, 2) for v in dd]))
        errs(ed, '가시설')

        print('== e 관로 고치기')
        ed.js("__map3d.setMode('area')")
        ed.js('__t.look(1, 15, 34, 55, 20)')
        ui_click(ed, inp, 'tgEdit')
        q = J(ed, '__t.at(1, 9, false)')
        inp.click(q[0], q[1])
        hn = J(ed, '__map3d.hnd().map((h) => [h.i, h.sx, h.sy])')
        report('e 가지 관 고름 · 손잡이 4개', J(ed, '__map3d.ED.sel') == 1 and len(hn) == 4, str(len(hn)))
        shot(ed, SD, '06_고치기_손잡이.png')
        P0 = J(ed, 'window.RV_PIPES.lines[1]')
        h1 = next(h for h in hn if h[0] == 1)
        dst = (h1[1] + 60, h1[2] + 35)
        inp.drag((h1[1], h1[2]), dst)
        P1 = J(ed, 'window.RV_PIPES.lines[1]')
        hit = J(ed, '(() => { const h = __map3d.pickAt(%f, %f), T = __map3d.T(); return h ? [h.p[0] - T[0], h.p[1] - T[1]] : null; })()' % dst)
        c1 = P1['pts'][P1['ctrl'][1]]
        report('e 끌기 → 꼭짓점 = 3D 모델 위 그 자리', hit and math.hypot(c1[0] - hit[0], c1[1] - hit[1]) < 0.1, '%s vs %s' % (c1[:2], hit))
        gd = J(ed, '__map3d.demAt(%f, %f)' % (c1[0], c1[1]))
        report('e 그 자리 땅 (dem.js)', abs(P1['g'][P1['ctrl'][1]] - gd) < 0.01, '%.3f vs %.3f' % (P1['g'][P1['ctrl'][1]], gd))
        shot(ed, SD, '06b_고치기_끈뒤.png')
        inp.key('z', 'KeyZ', 90, 2)
        report('e Ctrl+Z → 처음', J(ed, 'window.RV_PIPES.lines[1].pts') == P0['pts'])
        inp.key('y', 'KeyY', 89, 2)
        report('e Ctrl+Y → 다시', J(ed, 'window.RV_PIPES.lines[1].pts') == P1['pts'])
        n0 = len(J(ed, 'window.RV_PIPES.lines[1].ctrl'))
        q = J(ed, '__t.at(1, 4, false)')
        inp.dblclick(q[0], q[1])
        n1 = len(J(ed, 'window.RV_PIPES.lines[1].ctrl'))
        report('e 두 번 누르기 → 점 추가', n1 == n0 + 1, '%d → %d' % (n0, n1))
        inp.key('Delete', 'Delete', 46)
        n2 = len(J(ed, 'window.RV_PIPES.lines[1].ctrl'))
        report('e Delete → 점 지움', n2 == n0, '%d → %d' % (n1, n2))
        Pa = J(ed, 'window.RV_PIPES.lines[1]')
        q = J(ed, '__t.at(1, 20, false)')
        inp.drag((q[0], q[1]), (q[0] + 40, q[1] - 20), mods=8)
        Pb = J(ed, 'window.RV_PIPES.lines[1]')
        ca = np.array([Pa['pts'][k][:2] for k in Pa['ctrl']]); cb = np.array([Pb['pts'][k][:2] for k in Pb['ctrl']])
        dv = cb - ca
        report('e Shift 끌기 → 통째로 (같은 만큼)', len(ca) == len(cb) and np.ptp(dv, 0).max() < 0.011 and np.abs(dv).max() > 0.3, str(dv[0].round(2)))
        ui_click(ed, inp, 'bTop')
        ed.frames(3)
        hn = [h for h in J(ed, '__map3d.hnd().map((h) => [h.i, h.sx, h.sy])') if 40 < h[1] < 1240 and 120 < h[2] < 820]   # 칸 · 윗줄에 안 가린 손잡이
        h2 = min(hn, key=lambda h: math.hypot(h[1] - 640, h[2] - 450))
        Pc = J(ed, 'window.RV_PIPES.lines[1]')
        inp.drag((h2[1], h2[2]), (h2[1] - 30, h2[2] + 30))
        Pd = J(ed, 'window.RV_PIPES.lines[1]')
        report('e 위에서 보기 · 끌기', J(ed, '__map3d.V.el') > 80 and Pc['pts'][Pc['ctrl'][h2[0]]] != Pd['pts'][Pd['ctrl'][h2[0]]], '손잡이 %d' % h2[0])
        shot(ed, SD, '07_고치기_위에서.png')
        ui_click(ed, inp, 'bTop')
        inp.key('Escape', 'Escape', 27)
        report('e Esc → 고치기 끝', J(ed, '__map3d.ED.on') is False)
        saved = J(ed, "Object.keys(localStorage).some((k) => k.indexOf('rv_pipes:') === 0)")
        report('e 이 브라우저 저장 (프로그램 밖)', saved)
        errs(ed, '고치기')

        print('== q 지장물 · 수량 · 간섭')
        ed.js('localStorage.clear()')
        setup_page(ed, base + 'index.html')
        report('q 지장물 단추', J(ed, "!document.getElementById('tgObst').hidden"))
        ui_click(ed, inp, 'tgQty')
        ed.wait_js('document.getElementById("qHit").textContent !== ""', 5)
        cf = J(ed, '__map3d.cf().length')
        report('q 간섭 자리 (가스 · 주 관)', cf >= 1, '%d 곳' % cf)
        qv = J(ed, "Number(document.getElementById('qt_len').dataset.v)")
        tl = J(ed, '__map3d.lines().reduce((s, L) => s + L.len, 0)')
        report('q 연장 합 = 관 길이', abs(qv - tl) < 0.01, '%.2f · %.2f' % (qv, tl))
        sp = J(ed, '__map3d.cf()[0] || { e: 0, n: 0, g: 0 }')
        ed.js('(() => { const M = __map3d, T = M.T(); M.V.tgt = [%f + T[0], %f + T[1], %f + T[2]]; M.V.d = 20; M.V.el = 45; M.redraw(); })()' % (sp['e'], sp['n'], sp['g']))
        ed.js("__map3d.setMode('trench')")
        shot(ed, SD, '08_지장물_간섭.png')
        ui_click(ed, inp, 'tgQty')
        errs(ed, '지장물')

        print('== p 프로그램 (hostmock)')
        ed.js('localStorage.clear()')
        setup_page(ed, base + 'index_host.html')
        report('p 프로그램 단추 보임', J(ed, HOST_SHOWN))
        ed.js('__t.look(1, 15, 34, 55, 20)')
        ui_click(ed, inp, 'tgEdit')
        q = J(ed, '__t.at(1, 9, false)')
        inp.click(q[0], q[1])
        hn = J(ed, '__map3d.hnd().map((h) => [h.i, h.sx, h.sy])')
        h1 = next(h for h in hn if h[0] == 1)
        inp.drag((h1[1], h1[2]), (h1[1] + 50, h1[2] + 20))
        time.sleep(1.3)
        held = J(ed, "__host.calls.filter((c) => c.name === 'save_pipes').length") == 0   # JACK 1127: 저절로 안 씀 · [저장] 을 눌러야
        ed.js("document.getElementById('bSave').click()")
        ed.wait_js("__host.calls.some((c) => c.name === 'save_pipes')", 5)
        calls = J(ed, "__host.calls.filter((c) => c.name === 'save_pipes').map((c) => c.arg)") if held else []
        P1 = J(ed, 'window.RV_PIPES.lines[1]')
        okp = len(calls) == 1 and calls[0].startswith('window.RV_PIPES') and json.loads(calls[0].split('=', 1)[1].strip().rstrip(';'))['lines'][1]['pts'] == P1['pts']
        report('p save_pipes 한 번 (고친 줄)', okp, '%d 번' % len(calls))
        report('p 이 브라우저 저장 안 함', not J(ed, "Object.keys(localStorage).some((k) => k.indexOf('rv_pipes:') === 0)"))
        inp.key('Escape', 'Escape', 27)
        ed.js("__map3d.trSet({ margin: 0.4 })")
        time.sleep(1.2)
        ed.js("document.getElementById('bSave').click()")
        ed.wait_js("__host.calls.some((c) => c.name === 'save_calib')", 5)
        cal = J(ed, "__host.calls.filter((c) => c.name === 'save_calib').map((c) => c.arg)")
        okc = len(cal) >= 1 and '"trench"' in cal[-1] and json.loads(cal[-1].split('=', 1)[1].strip().rstrip(';'))['trench']['margin'] == 0.4
        report('p save_calib (calib.js 의 trench)', okc, '%d 번' % len(cal))
        ui_click(ed, inp, 'tgEdit')
        inp.el_click('edShp')
        time.sleep(0.3)
        report('p SHP 내보내기 부름', J(ed, "__host.calls.some((c) => c.name === 'export_pipes_shp')"))
        inp.key('Escape', 'Escape', 27)
        ui_click(ed, inp, 'bPipes')
        report('p 관로 넣기 창', J(ed, "!document.getElementById('dlgBack').hidden && !document.getElementById('pipeDlg').hidden"))
        # 넣기: 파일 고르기 (pick_pipes) → 넣기 (import_pipes = 프로그램이 data/pipes.js 를 새로 씀) → 다시 읽음
        pj = os.path.join(out, 'data', 'pipes.js')
        pj0 = open(pj, encoding='utf-8').read()
        try:
            D = json.loads(pj0.split('=', 1)[1].strip().rstrip(';'))
            D['lines'] = D['lines'][:1]
            D['lines'][0]['label'] = 'D500 새로'
            ed.js("__host.picks.push({ path: 'C:/시험/관로.shp', kind: 'shp', name: '관로.shp', fields: [{ name: 'DIA', samples: ['500'] }], guess_dia: 'DIA', note: '' }); "
                  "window.pywebview.api.import_pipes = (o) => { __host.calls.push({ name: 'import_pipes', arg: o }); return new Promise((r) => setTimeout(() => r({ ok: true, lines: 1 }), 50)); }")
            inp.el_click('pmPick')
            ed.wait_js("!document.getElementById('pmGo').disabled", 5)
            shot(ed, SD, '09_관로넣기창.png')
            open(pj, 'w', encoding='utf-8').write('window.RV_PIPES = ' + json.dumps(D, ensure_ascii=False) + ';' + chr(10))
            inp.el_click('pmGo')
            oki = ed.wait_js("__map3d.lines().length === 1 && __map3d.lines()[0].base === 'D500 새로' && document.getElementById('dlgBack').hidden", 10)
            arg = J(ed, "(__host.calls.find((c) => c.name === 'import_pipes') || {}).arg || null")
            report('p 넣기 → 다시 읽음 (관 1줄)', oki and arg and arg['dia_field'] == 'DIA' and abs(arg['depth'] - 1.2) < 1e-9, str(arg))
            report('p 넣은 뒤 아래 줄 · 관로 수', '관로 1줄' in J(ed, "document.getElementById('info').textContent"))
        finally:
            open(pj, 'w', encoding='utf-8').write(pj0)
        errs(ed, '프로그램')
        print('== 관로 없는 프로젝트')
        ed.net(block=['*pipes.js*', '*obstacles.js*'])
        setup_page(ed, base + 'index.html')
        report('관로 없음 · 열림', J(ed, '__map3d.lines().length') == 0 and J(ed, '!window.__pipes || window.__pipes.lines === 0'))
        ui_click(ed, inp, 'tgEdit')
        ui_click(ed, inp, 'mDepth')
        report('관로 없음 · 고치기 안 켜짐 · 터파기 표현', J(ed, '__map3d.ED.on') is False and J(ed, "__map3d.S.mode") == 'trench')
        shot(ed, SD, '_관로없음.png')
        errs(ed, '관로 없음')
        ed.net(block=['*__none__*'])

        print('== l 질감 고해상도')
        meta = json.loads(meta0)
        hi_t = os.path.join(out, 'data', 'tiles', '_hi_t04.jpg')
        if not os.path.exists(hi_t):
            im = Image.open(os.path.join(out, 'data', meta['tiles'][4]['tex'])).convert('RGB')
            im = im.resize((im.width * 2, im.height * 2), Image.BICUBIC)
            arr = np.asarray(im).astype(np.float32); arr[..., 0] = np.minimum(255, arr[..., 0] * 1.25 + 30)   # 붉게 (바뀐 것이 보이게)
            Image.fromarray(arr.astype(np.uint8)).save(hi_t, quality=88)
            im.close()
        with Image.open(hi_t) as im:
            hs = [im.width, im.height]
        meta['tiles'][4]['tex_hi'] = 'tiles/_hi_t04.jpg'; meta['tiles'][4]['texsize_hi'] = hs
        open(mj, 'w', encoding='utf-8').write(json.dumps(meta))
        setup_page(ed, base + 'index.html')
        c4 = J(ed, '(() => { const b = __map3d.mesh().parts[4].bb; return [(b[0][0] + b[1][0]) / 2, (b[0][1] + b[1][1]) / 2, (b[0][2] + b[1][2]) / 2]; })()')
        ed.js('(() => { const M = __map3d; M.V.tgt = %s; M.V.d = 15; M.V.el = 50; M.redraw(); })()' % json.dumps(c4))
        okl = ed.wait_js('__map3d.LOD.loads >= 1 && !!__map3d.mesh().parts[4].hi', 60)
        report('l 가까이 → 고해상도 읽음', okl, J(ed, '[__map3d.LOD.loads, __map3d.mesh().parts[4].up]'))
        shot(ed, SD, '10_질감_가까이.png')
        ed.js('(() => { const M = __map3d; M.V.d = 900; M.V.el = 60; M.redraw(); })()')
        okd = ed.wait_js('__map3d.LOD.drops >= 1 && !__map3d.mesh().parts[4].hi', 30)
        report('l 멀리 → 내림', okd, J(ed, '[__map3d.LOD.drops]'))
        errs(ed, '질감')
    finally:
        try:
            open(mj, 'w', encoding='utf-8').write(meta0)
        except OSError:
            pass
        ed.__exit__(None, None, None)
        srv.shutdown()

    if a.old:                                               # 견줌: 옛 보기 (DSM 땅 · 울퉁불퉁) · 새 보기 같은 시점
        print('== 견줌 (옛 보기 · 새 보기)')
        od = os.path.abspath(a.old)
        shutil.copy2(os.path.join(od, 'map3d.html'), os.path.join(out, 'old.html'))
        shutil.copy2(os.path.join(od, 'map3d.js'), os.path.join(out, 'viewer', 'map3d_old.js'))
        h = open(os.path.join(out, 'old.html'), encoding='utf-8').read().replace('viewer/map3d.js', 'viewer/map3d_old.js')
        open(os.path.join(out, 'old.html'), 'w', encoding='utf-8').write(h)
        srv, base = serve(out)
        with Edge(os.path.join(SD, '_prof2'), WIN, gl=False) as ed:
            setup_page(ed, base + 'index.html')
            ed.js('__t.along(0, %f, 26, 9)' % (J(ed, '__t.L(0).len') * 0.62))
            v = J(ed, '[__map3d.V.tgt, __map3d.V.d, __map3d.V.el, __map3d.V.az]')
            ed.js("window.RV_PIPES.lines.forEach((l) => { l.mat = 'steel'; }); __map3d.rebuild(); __map3d.setMode('area'); __map3d.SHOW.perm = false; __map3d.SHOW.tmp = false; __map3d.redraw()")
            shot(ed, SD, '00_새_관로_매끈.png')
            ed.goto(base + 'old.html')
            ed.wait_js(Edge.NEW + " && window.__map3d && document.getElementById('load').style.display === 'none'", 180)
            ed.js('(() => { const M = __map3d; M.V.tgt = %s; M.V.d = %f; M.V.el = %f; M.V.az = %f; M.redraw(); })()' % (json.dumps(v[0]), v[1], v[2], v[3]))
            shot(ed, SD, '00_옛_관로_울퉁불퉁.png')
        srv.shutdown()

    if a.flat:                                              # 3D 없는 프로젝트 (결과물 고르기 1.0.15): 평면 미리보기 그대로
        print('== f 3D 없는 프로젝트')
        fd = os.path.abspath(a.flat)
        os.makedirs(os.path.join(fd, 'data'), exist_ok=True); os.makedirs(os.path.join(fd, 'viewer'), exist_ok=True)
        shutil.copy2(os.path.join(REPO, 'viewer', 'map3d.html'), os.path.join(fd, 'index.html'))
        shutil.copy2(os.path.join(REPO, 'viewer', 'map3d.js'), os.path.join(fd, 'viewer', 'map3d.js'))
        shutil.copy2(os.path.join(REPO, 'viewer', 'river.js'), os.path.join(fd, 'viewer', 'river.js'))
        shutil.copy2(os.path.join(REPO, 'viewer', 'cost.js'), os.path.join(fd, 'viewer', 'cost.js'))
        shutil.copy2(os.path.join(out, 'data', '미리보기.png'), os.path.join(fd, 'data', '미리보기.png'))
        Mf = json.loads(open(os.path.join(out, 'data', 'mapping.js'), encoding='utf-8').read().split('=', 1)[1].strip().rstrip(';'))
        Mf['outputs'] = ['ortho', 'dsm']
        open(os.path.join(fd, 'data', 'mapping.js'), 'w', encoding='utf-8').write('window.RV_MAPPING = ' + json.dumps(Mf, ensure_ascii=False) + ';\n')
        srv, base = serve(fd)
        with Edge(os.path.join(SD, '_prof3'), WIN, gl=False) as ed:
            ed.goto(base + 'index.html')
            okf = ed.wait_js(Edge.NEW + " && window.__map3d && window.__map3d.flat === true && !!document.getElementById('flat')", 30)
            report('f 평면 미리보기', okf)
            shot(ed, SD, '11_3D없음.png')
            errs(ed, '3D 없음')
        srv.shutdown()

    print('== 검사점 칸 (모델에서 찍기)')
    srv, base = serve(out)
    with Edge(os.path.join(SD, '_prof4'), WIN, gl=False) as ed:
        inp = ce.Inp(ed)
        setup_page(ed, base + 'index.html')
        ed.js('__t.look(0, 60, 40, 50, 30)')
        ed.frames(3)
        q = [640, 450]
        o = J(ed, 'window.RV_MAPPING.origin')
        hh = J(ed, '(() => { const h = __map3d.pickAt(%f, %f); return h && h.p; })()' % (q[0], q[1]))
        E, N, Z = hh[0] + o[0], hh[1] + o[1], hh[2] + o[2]
        ed.js("__ckApi.set([{ name: 'K1', E: %f, N: %f, Z: %f }])" % (E, N, Z))
        ed.frames(3)
        inp.click(q[0], q[1])
        ck = J(ed, 'window.__ck')
        report('검사점 찍기 · 잔차', ck and ck['measured'] == 1 and ck['h'] < 0.05, str(ck))
        shot(ed, SD, '12_검사점.png')
        errs(ed, '검사점')
    srv.shutdown()

    for where, e in R['errs']:
        print('  오류', where, e)
    print('== %s · 실패 %d %s' % ('통과' if R['ok'] else '실패', len(R['fails']), R['fails']))
    return 0 if R['ok'] else 1


if __name__ == '__main__':
    sys.exit(main())
