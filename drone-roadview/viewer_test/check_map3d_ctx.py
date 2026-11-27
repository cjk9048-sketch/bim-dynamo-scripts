"""3D 매핑 보기 · 오른쪽 메뉴 시험 (JACK 1126): 화면 없는 Edge (cdp.py) 에 진짜 마우스 오른쪽 눌렀다 떼기 → 메뉴 항목마다 한 번씩.
프로그램 쪽은 hostmock.js 흉내 (photos_at · photo_view · save_image · crop_* · add_outputs · export_web · job …).

    python -I check_map3d_ctx.py --out <3D 매핑 결과 폴더 (check_map3d_ug 와 같은 것)> --shots <그림 폴더>

  c  간섭 고리 (JACK 1127): 간섭 카테고리 접으면 없음 · 펴면 있음 · 화면 자리 = toScr(e, n, z 관 · 지장물 가장 가까운 두 점 가운데 높이) · 목록 누름 → 그 높이
  z  꼭짓점 높이: 점 넣기 → 위아래 화살표 (ns-resize) · 끌기 (0.05 m · hasZ · 이웃 보간) · 간섭 풀림 · PageUp · Alt+↓ · Ctrl+Z/Y ·
     한계 (관 윗면 ≤ 땅 − 0.3 · 알림) · [저장] → save_pipes (hasZ · z) · [토피로 되돌리기]
  x  관 자르기: 메뉴 «여기서 관 자르기» (두 줄 · 길이 합 · -1 · -2 · 가시설 둘로) · Ctrl+Z 한 번 · 고치기 [자르기] 두 곳 → 사이 구간 → Delete 바로
  s  새 관 스냅: 관 위 = «분기» (Alt = 끔 · 점 = 관 위 1 cm) · 다른 관 끝점 = «이어 그리기» → 같은 관이 늘어남 (관 개수 그대로 · 끝점 일치) · Ctrl+Z
  m  메뉴: 빈 땅 = 보기 · 재기 · 새 관 · 기타 (관 항목 없음 · JACK 1127 뺀 것 없음) · 관 위 = 관 항목 (자르기 · 새 관 없음) · 맨 위 좌표 = 누른 자리 ·
     Esc · 바깥 누르기 · 휠로 닫힘 · 오른쪽 아래 구석에서 열어도 화면 안 · 관로 고치기 켜진 동안에도 열림 · 오른쪽 끌기 = 메뉴 없음
  v  보기: 여기 가운데로 · 좌표 · 표고 복사 (X, Y, Z) · 화면 그림 저장 (PNG) · 처음 시점 · 위에서 / 비스듬히 · 북쪽 보기
  d  재기: 거리 (점 셋 · Enter · 평면 / 경사 합 = 따로 셈) · 면적 (두 번 누르기로 닫기 · 평면 면적 = 따로 셈) ·
     높이 차 (두 점 · 표고 차 · 기울기) · 지형 단면 (땅 = DTM 따로 셈 · 3D 모델 면 = 위에서 고르기와 같음 · SVG) · [지우기]
  n  새 관: 점 셋 · Enter → 창 (관경 · 관종 · 이름) → RV_PIPES 새 줄 (꼭짓점 = 찍은 자리) · save_pipes
  p  관: 관 정보 · 단면 / 이 관 고치기 / 가시설 (종류 → 시작점 = 누른 자리 → 끝점) / 지우기 (확인 → 빈 줄 → 되돌리기)
  o  기타: 원본 사진 (photos_at 좌표 · 그림 · 표시 자리 · 다음 · 확대 · ← · Esc) · 투시 · 관로 · 결과물 폴더 · 검사점 찍기
  k  잘라내기: 메뉴 → 위에서 보기 · 네모 (구석 누르기) · 꼭짓점 끌기 · 다각형 (첫 점 누르기로 닫기) · 적용 → crop_apply → 진행 → 다시 엶
  t  결과물 다듬기 칸: crop_get · 웹 공유 (진행 → 주소 · 복사) · DEM (add_outputs → 다시 엶) · 잘라내기 없앰 (확인 → crop_clear)
  b  브라우저 (프로그램 밖): 프로그램 항목 없음 · 화면 그림 = 내려받기 PNG · 거리 재기
  모든 쪽 JS 오류 0
"""
import argparse
import glob
import json
import math
import os
import shutil
import sys
import time

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
sys.path.insert(0, REPO)
from cdp import Edge  # noqa: E402
import check_edit as ce  # noqa: E402
import check_map3d_ug as ug  # noqa: E402
from check_map3d_ug import J, errs, report, serve, setup_page, shot  # noqa: E402

WIN = ug.WIN
ALT = 1                                                                  # CDP 누름쇠 (Alt 1 · Ctrl 2 · Shift 8)
GONE = ['copy', 'home', 'top', 'north', 'dtm', 'files', 'ck', 'crop', 'ngHere']   # JACK 1127 «빨간 줄 = 오른쪽 메뉴에서 빼 · 옵션창에서»


def rbtn(ed, typ, x, y, buttons):
    ed.call('Input.dispatchMouseEvent', {'type': typ, 'x': float(x), 'y': float(y), 'button': 'right', 'buttons': buttons, 'clickCount': 1})


def rclick(ed, x, y):
    """진짜 오른쪽 눌렀다 떼기."""
    ed.call('Input.dispatchMouseEvent', {'type': 'mouseMoved', 'x': float(x), 'y': float(y), 'button': 'none', 'buttons': 0})
    rbtn(ed, 'mousePressed', x, y, 2)
    rbtn(ed, 'mouseReleased', x, y, 0)
    ed.frames(2)


def item_xy(ed, iid):
    return J(ed, "(() => { const b = document.querySelector('#ctxMenu [data-id=\"%s\"]'); if (!b || b.offsetParent === null) return null; "
                 "const r = b.getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; })()" % iid)


def menu(ed, inp, x, y, iid):
    """(x, y) 에서 메뉴 → 항목 iid 누르기 (진짜 마우스). → 누름 여부."""
    rclick(ed, x, y)
    if not J(ed, '__map3d.ctx().open'):
        return False
    q = item_xy(ed, iid)
    if not q:
        return False
    inp.click(*q)
    ed.frames(2)
    return True


def calls(ed, name):
    return J(ed, "__host.calls.filter((c) => c.name === '%s').map((c) => c.arg === undefined ? null : c.arg)" % name)


def pick(ed, x, y):
    """화면 (x, y) 의 3D 모델 점 (세계) · 없으면 None."""
    return J(ed, '(() => { __map3d.view(); const h = __map3d.pickAt(%f, %f); return h ? h.p : null; })()' % (x, y))


def toast_btn(ed, inp, wait=3.0):
    ok = ed.wait_js("!!document.querySelector('#toast.show button')", wait)
    if not ok:
        return False
    r = J(ed, "(() => { const r = document.querySelector('#toast button').getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; })()")
    inp.click(*r)
    return True


def empty_spots(ed, n=4):
    """관에서 70 px 넘게 떨어진 · 3D 모델이 있는 화면 자리 (칸 · 윗줄 밖)."""
    pl = J(ed, '__t.line(0, false).concat(__t.line(1, false))') or []
    out = []
    for y in range(160, 780, 60):
        for x in range(120, 1240, 70):
            if any(math.hypot(x - q[0], y - q[1]) < 70 for q in pl):
                continue
            if pick(ed, x, y) is None:
                continue
            out.append((x, y))
    # 서로 떨어진 것 n 개 (가운데 쪽부터)
    out.sort(key=lambda q: math.hypot(q[0] - 680, q[1] - 470))
    sel = []
    for q in out:
        if all(math.hypot(q[0] - s[0], q[1] - s[1]) > 110 for s in sel):
            sel.append(q)
        if len(sel) >= n:
            break
    return sel


def copy_site(out):
    os.makedirs(os.path.join(out, 'viewer'), exist_ok=True)
    shutil.copy2(os.path.join(REPO, 'viewer', 'map3d.html'), os.path.join(out, 'index.html'))
    shutil.copy2(os.path.join(REPO, 'viewer', 'map3d.js'), os.path.join(out, 'viewer', 'map3d.js'))
    shutil.copy2(os.path.join(REPO, 'viewer', 'river.js'), os.path.join(out, 'viewer', 'river.js'))   # 하천 추진 · 횡단 셈
    shutil.copy2(os.path.join(REPO, 'viewer', 'cost.js'), os.path.join(out, 'viewer', 'cost.js'))     # 개략공사비 셈 (1127)
    shutil.copy2(os.path.join(HERE, 'hostmock.js'), os.path.join(out, 'viewer', 'hostmock.js'))
    html = open(os.path.join(out, 'index.html'), encoding='utf-8').read()
    open(os.path.join(out, 'index_host.html'), 'w', encoding='utf-8').write(
        html.replace('<script src="viewer/map3d.js"></script>', '<script src="viewer/hostmock.js"></script>\n<script src="viewer/map3d.js"></script>'))


def cat_set(ed, inp, hid, on):
    """오른쪽 칸 카테고리 머리줄을 진짜 마우스로 펴거나 접음."""
    ed.js("(() => { if (document.getElementById('side').hidden) document.getElementById('tgSide').click(); document.getElementById('%s').scrollIntoView({ block: 'nearest' }); })()" % hid)
    ed.frames(2)
    if (J(ed, "document.getElementById('%s').getAttribute('aria-expanded')" % hid) == 'true') != on:
        inp.el_click(hid)
    ed.frames(3)


SEG_JS = r"""(src, e, n) => {                     // 관 src 위 (e, n) 에 가장 가까운 곳 → {s (측점), d (평면 거리), zc (관 중심 보간)}
  const M = __map3d, L = M.lines().find((q) => q.src === src), P = L.pts; let best = null;
  for (let k = 0; k + 1 < P.length / 3; k++) {
    const ax = P[k * 3], ay = P[k * 3 + 1], dx = P[k * 3 + 3] - ax, dy = P[k * 3 + 4] - ay, l2 = dx * dx + dy * dy;
    const t = l2 > 0 ? Math.max(0, Math.min(1, ((e - ax) * dx + (n - ay) * dy) / l2)) : 0, d = Math.hypot(e - ax - t * dx, n - ay - t * dy);
    if (!best || d < best.d) best = { s: L.cum[k] + (L.cum[k + 1] - L.cum[k]) * t, d: d, zc: M.zcAt(L, k) + (M.zcAt(L, k + 1) - M.zcAt(L, k)) * t };
  }
  return best;
}"""


def zcut(ed, inp, SD, s0, A):
    """JACK 1127: 간섭 고리 (접기 · 진짜 높이) · 꼭짓점 높이 (위아래 화살표) · 관 자르기 · 새 관 스냅 (분기 · 끝점 이어 그리기 · Alt)."""
    seg = lambda src, e, n: J(ed, '(%s)(%d, %f, %f)' % (SEG_JS, src, e, n))   # noqa: E731
    ed.js('__map3d.edMode(false)')
    print('== c 간섭 고리 (간섭 카테고리 펼 때만 · 자리 = 관 · 지장물 가장 가까운 두 점 가운데 높이)')
    if not J(ed, "document.getElementById('tgObst').classList.contains('on')"):
        ug.ui_click(ed, inp, 'tgObst')
    okc = ed.wait_js('__map3d.cf().length > 0', 5)
    sp = J(ed, '__map3d.cf()[0]')
    report('c 간섭 자리 있음', okc and sp is not None, str(sp and {k: round(sp[k], 3) for k in ('e', 'n', 'z', 'g', 'c')}))
    if not sp:
        return
    ed.js('(() => { const M = __map3d, T = M.T(); M.V.tgt = [%f + T[0], %f + T[1], %f + T[2]]; M.V.d = 16; M.V.el = 40; M.V.az = 30; M.redraw(); })()' % (sp['e'], sp['n'], sp['g']))
    ed.js("__map3d.setMode('trench')")
    cat_set(ed, inp, 'hd_cf', False)
    r0 = J(ed, '__map3d.cfr()')
    shot(ed, SD, '40_간섭_접음.png')
    cat_set(ed, inp, 'hd_cf', True)
    r1 = J(ed, '__map3d.cfr()')
    shot(ed, SD, '41_간섭_폄.png')
    exp = J(ed, '(() => { const M = __map3d; M.view(); return M.cf().map((q) => [M.toScr(q.e, q.n, q.z), M.toScr(q.e, q.n, q.g)]).filter((q) => q[0]); })()')
    report('c 접으면 고리 없음 · 펴면 고리', len(r0) == 0 and len(r1) == len(exp) > 0, '접음 %d · 폄 %d' % (len(r0), len(r1)))
    dz = max((math.hypot(a[0] - b[0][0], a[1] - b[0][1]) for a, b in zip(r1, exp)), default=99)
    dg = min((math.hypot(a[0] - b[1][0], a[1] - b[1][1]) for a, b in zip(r1, exp) if b[1]), default=0)
    report('c 고리 화면 자리 = toScr(e, n, z 관 높이) · 땅 g 아님', dz < 0.5 and dg > 5, '높이 자리 어긋남 %.2f px · 땅 자리와 %.0f px' % (dz, dg))
    pz = seg(sp['src'], sp['e'], sp['n'])
    oz = J(ed, '(() => { const O = __map3d.obst()[%d], P = O.pts; let best = null; for (let k = 0; k + 1 < P.length / 3; k++) { const ax = P[k * 3], ay = P[k * 3 + 1], dx = P[k * 3 + 3] - ax, dy = P[k * 3 + 4] - ay, l2 = dx * dx + dy * dy; '
                'const t = l2 > 0 ? Math.max(0, Math.min(1, ((%f - ax) * dx + (%f - ay) * dy) / l2)) : 0, d = Math.hypot(%f - ax - t * dx, %f - ay - t * dy); if (!best || d < best[0]) best = [d, P[k * 3 + 2] + (P[k * 3 + 5] - P[k * 3 + 2]) * t]; } return best[1]; })()'
                % (sp['oi'], sp['e'], sp['n'], sp['e'], sp['n']))
    report('c 고리 높이 = 관 중심 ↔ 지장물 중심 사이 (땅 아래)', min(pz['zc'], oz) - 0.02 <= sp['z'] <= max(pz['zc'], oz) + 0.02 and sp['z'] < sp['g'] - 0.3,
           'z %.3f · 관 %.3f · 지장물 %.3f · 땅 %.3f' % (sp['z'], pz['zc'], oz, sp['g']))
    ed.js("document.querySelector('#qList button').click()"); ed.frames(2)
    t = J(ed, '__map3d.V.tgt'); T = J(ed, '__map3d.T()')
    report('c 목록 누름 → 그 자리 (높이 z)', abs(t[0] - sp['e'] - T[0]) < 1e-6 and abs(t[1] - sp['n'] - T[1]) < 1e-6 and abs(t[2] - sp['z'] - T[2]) < 1e-6, str([round(v, 3) for v in t]))
    errs(ed, '간섭 고리')

    print('== z 꼭짓점 높이 (위아래 화살표 · 0.05 m · Shift 0.01 · PageUp/PageDown · Alt+↓ · 한계 · Ctrl+Z · 저장 · 토피로)')
    src = sp['src']
    n_cf0 = J(ed, '__map3d.cf().length')
    ed.js('__t.look(%d, %f, 14, 30, 120)' % (src, pz['s'])); ed.frames(3)
    ed.js('__map3d.edMode(true); __map3d.ED.sel = %d; __map3d.redraw()' % src); ed.frames(3)
    nc0 = J(ed, '__t.L(%d).ctrl.length' % src)
    q = J(ed, '__t.at(%d, %f, true)' % (src, pz['s']))
    inp.dblclick(q[0], q[1])                                   # 간섭 자리에 점 넣기 → 그 점 고름 → 화살표
    ed.frames(3)
    hi = J(ed, '__map3d.ED.hi')
    zg = J(ed, '__map3d.zg()')
    report('z 점 넣기 → 그 점 고름 → 화살표 보임', J(ed, '__t.L(%d).ctrl.length' % src) == nc0 + 1 and hi > 0 and zg['on'], 'hi %s · %s' % (hi, zg))
    L = J(ed, '(() => { const L = __t.L(%d); return { ctrl: L.ctrl, cum: Array.from(L.cum), zc: Array.from({ length: L.pts.length / 3 }, (_, k) => __map3d.zcAt(L, k)), g: Array.from({ length: L.pts.length / 3 }, (_, k) => __map3d.gAt(L, k)), D: L.dia }; })()' % src)
    k = L['ctrl'][hi]; k0 = L['ctrl'][hi - 1]; k1 = L['ctrl'][hi + 1]
    s0z, u = zg['s0'], zg['u']
    dn = (s0z[0] - u[0] * 24, s0z[1] - u[1] * 24)             # 아래 화살표 가운데
    inp.move(*dn)
    cur = J(ed, "getComputedStyle(document.getElementById('c')).cursor")
    report('z 화살표 위 커서 = ns-resize', cur == 'ns-resize', cur)
    inp.press(dn[0], dn[1])
    inp.drag_to(dn, (dn[0] - u[0] * 30, dn[1] - u[1] * 30), steps=10)   # 아래로 30 px
    shot(ed, SD, '42_높이_끄는중.png')
    inp.release(dn[0] - u[0] * 30, dn[1] - u[1] * 30)
    raw = J(ed, 'window.RV_PIPES.lines[%d]' % src)
    zs = [p[2] for p in raw['pts']]
    znew, dzv = zs[k], zs[k] - L['zc'][k]
    okw = raw.get('hasZ') is True and dzv < -0.1 and abs(znew * 20 - round(znew * 20)) < 1e-6
    errn = 0.0
    for b in range(len(zs)):                                   # 이웃 = 바뀐 만큼을 직선으로 나눔 · 나머지 그대로
        w = 1.0 if b == k else (L['cum'][b] - L['cum'][k0]) / (L['cum'][k] - L['cum'][k0]) if k0 < b < k else (L['cum'][k1] - L['cum'][b]) / (L['cum'][k1] - L['cum'][k]) if k < b < k1 else 0.0
        errn = max(errn, abs(zs[b] - (L['zc'][b] + dzv * w)))
    report('z 끌기 → 그 점 z 바뀜 (0.05 m 단위) · hasZ · 이웃 보간', okw and errn < 0.002, 'Δ %+.3f m · 표고 %.3f · 이웃 어긋남 %.4f m' % (dzv, znew, errn))
    ok = ed.wait_js('__map3d.cf().length < %d' % n_cf0, 3)
    report('z 내리면 간섭 풀림 → 고리 줄어듦', ok, '%d → %d' % (n_cf0, J(ed, '__map3d.cf().length')))
    shot(ed, SD, '43_높이_내림.png')
    nu = J(ed, '__map3d.ED.undo.length')
    inp.key('PageUp', 'PageUp', 33)
    z2 = J(ed, 'window.RV_PIPES.lines[%d].pts[%d][2]' % (src, k))
    inp.key('ArrowDown', 'ArrowDown', 40, ALT)
    z3 = J(ed, 'window.RV_PIPES.lines[%d].pts[%d][2]' % (src, k))
    report('z PageUp +0.1 · Alt+↓ −0.1 (기록 2개)', abs(z2 - znew - 0.1) < 1e-6 and abs(z3 - znew) < 1e-6 and J(ed, '__map3d.ED.undo.length') == nu + 2, '%.3f → %.3f → %.3f' % (znew, z2, z3))
    inp.key('z', 'KeyZ', 90, ce.CTRL)
    report('z Ctrl+Z → 앞 높이', abs(J(ed, 'window.RV_PIPES.lines[%d].pts[%d][2]' % (src, k)) - z2) < 1e-6)
    inp.key('y', 'KeyY', 89, ce.CTRL)
    ed.js('__map3d.ED.hi = %d; __map3d.redraw()' % hi)
    for _ in range(40):                                        # 한계: 관 윗면 ≤ 땅 − 0.3
        inp.key('PageUp', 'PageUp', 33)
    zt = J(ed, 'window.RV_PIPES.lines[%d].pts[%d][2]' % (src, k))
    tt = J(ed, "document.getElementById('toast').textContent")
    zs = [p[2] for p in J(ed, 'window.RV_PIPES.lines[%d].pts' % src)]
    over = [zs[b] + L['D'] / 2 - (L['g'][b] - 0.3) for b in range(len(zs))]     # 0 이하 = 관 윗면이 땅 0.3 m 아래
    report('z 한계 = 관 윗면 땅 0.3 m 아래 (닿은 곳 0) · 알림', max(over) <= 0.002 and max(over) > -0.002 and zt + L['D'] / 2 <= L['g'][k] - 0.3 + 0.002 and '0.3 m 아래' in tt,
           '가장 높은 곳 %.4f m · 고른 점 윗면 %.3f / 땅 %.3f · %s' % (max(over), zt + L['D'] / 2, L['g'][k], tt))
    # 흰 점 누름 → 절점 표고 넣기 칸 → Enter (JACK 1127)
    ed.js('__map3d.ED.hi = %d; __map3d.redraw()' % hi); ed.frames(3)
    zg = J(ed, '__map3d.zg()')
    inp.click(zg['s0'][0], zg['s0'][1]); ed.frames(3)
    vis_ask = J(ed, "(() => { const b = document.getElementById('zAsk'); return !!b && b.style.display === 'block' && !!document.activeElement && document.activeElement.id === 'zAskIn'; })()")
    want = round(L['g'][k] - L['D'] / 2 - 1.5, 2)                # 토피 1.5 m 자리
    nu2 = J(ed, '__map3d.ED.undo.length')
    ed.js("document.getElementById('zAskIn').value = '%.2f'" % want)
    inp.key('Enter', 'Enter', 13)
    ed.frames(2)
    z4 = J(ed, 'window.RV_PIPES.lines[%d].pts[%d][2]' % (src, k))
    report('z 흰 점 누름 → 표고 칸 → Enter = 그 표고 (기록 1개)', vis_ask and abs(z4 - want) < 1e-6 and J(ed, '__map3d.ED.undo.length') == nu2 + 1,
           '칸 %s · %.3f (원함 %.2f)' % (vis_ask, z4, want))
    shot(ed, SD, '44b_표고_넣기.png')
    # 바로 위에서 봄 → 기즈모를 옆으로 비켜 그림 · 화면 위로 끌면 올라감
    el0 = J(ed, '__map3d.V.el')
    ed.js('__map3d.V.el = 89.5; __map3d.redraw()'); ed.frames(4)
    zg = J(ed, '__map3d.zg()')
    up = (zg['s0'][0], zg['s0'][1] - 24)                         # 위 화살표 가운데
    z5 = J(ed, 'window.RV_PIPES.lines[%d].pts[%d][2]' % (src, k))
    inp.press(up[0], up[1])
    inp.drag_to(up, (up[0], up[1] - 40), steps=10)
    inp.release(up[0], up[1] - 40)
    z6 = J(ed, 'window.RV_PIPES.lines[%d].pts[%d][2]' % (src, k))
    report('z 위에서 봄 → 기즈모 옆으로 (겹침 없음) · 위로 끌면 올라감', zg['off'] and z6 > z5 + 0.04, 'off %s · %.3f → %.3f' % (zg['off'], z5, z6))
    shot(ed, SD, '44c_위에서_기즈모.png')
    ed.js('__map3d.V.el = %f; __map3d.redraw()' % el0); ed.frames(2)
    inp.key('z', 'KeyZ', 90, ce.CTRL); inp.key('z', 'KeyZ', 90, ce.CTRL)   # 위 두 고침 되돌림 → 뒤 시험 (저장 · 자르기) 은 앞 상태 그대로
    report('z 되돌리기 두 번 → 표고 넣기 앞 높이', abs(J(ed, 'window.RV_PIPES.lines[%d].pts[%d][2]' % (src, k)) - zt) < 1e-6)
    nsv = len(calls(ed, 'save_pipes'))
    time.sleep(1.0)                                            # 고친 것 모음 (0.8 초) 뒤 [저장]
    ed.js("document.getElementById('bSave').click()")
    ed.wait_js("__host.calls.filter((c) => c.name === 'save_pipes').length > %d" % nsv, 5)
    time.sleep(0.3)
    sv = calls(ed, 'save_pipes')
    got = json.loads(sv[-1].split('=', 1)[1].strip().rstrip(';'))['lines'][src] if sv else {}
    report('z [저장] → save_pipes 에 hasZ · z', got.get('hasZ') is True and abs(got['pts'][k][2] - zt) < 1e-6 and all(p[2] is not None for p in got['pts']), '%d 번' % len(sv))
    shot(ed, SD, '44_높이_고침.png')
    ok0 = J(ed, "!document.getElementById('edZ0').hidden")
    inp.el_click('edZ0')
    raw = J(ed, 'window.RV_PIPES.lines[%d]' % src)
    zz = J(ed, '(() => { const L = __t.L(%d); return [__map3d.zcAt(L, %d), __map3d.gAt(L, %d) - __map3d.S.cover - L.dia / 2]; })()' % (src, k, k))
    report('z [토피로 되돌리기] → hasZ 끔 · 평균매설깊이', ok0 and raw.get('hasZ') is False and abs(zz[0] - zz[1]) < 1e-9 and J(ed, "document.getElementById('edZ0').hidden"), str([round(v, 3) for v in zz]))
    inp.key('z', 'KeyZ', 90, ce.CTRL)
    report('z Ctrl+Z → 높이 다시', J(ed, 'window.RV_PIPES.lines[%d].hasZ' % src) is True)
    errs(ed, '높이')

    print('== x 관 자르기 (메뉴 «여기서 관 자르기» · 고치기 [자르기] 두 곳 → 사이 구간 → Delete · Ctrl+Z)')
    ed.js('__map3d.edMode(false)')
    orig0 = J(ed, 'JSON.stringify(window.RV_PIPES.lines[0])')
    n0 = J(ed, 'window.RV_PIPES.lines.length')
    len0 = J(ed, '__t.L(0).len')
    sc = s0 + 4
    ed.js('__t.look(0, %f, 40, 50, 60)' % sc); ed.frames(3)
    pt = J(ed, '__t.at(0, %f, true)' % sc)
    okm = menu(ed, inp, pt[0], pt[1], 'pCut')
    ed.frames(2)
    a_, b_ = J(ed, 'window.RV_PIPES.lines[0]'), J(ed, 'window.RV_PIPES.lines[%d]' % n0)
    la, lb = J(ed, '__t.L(0).len'), J(ed, '__t.L(%d).len' % n0)
    sa = J(ed, 'window.RV_PIPES.lines[0].shore || []'); sb = J(ed, 'window.RV_PIPES.lines[%d].shore || []' % n0)
    ea, eb = a_['pts'][-1], b_['pts'][0]
    okx = okm and J(ed, 'window.RV_PIPES.lines.length') == n0 + 1 and abs(la + lb - len0) < 0.01 and abs(la - sc) < 0.6 and math.hypot(ea[0] - eb[0], ea[1] - eb[1]) < 1e-6
    okx = okx and a_['label'].endswith('-1') and b_['label'].endswith('-2') and a_['dia'] == b_['dia'] and a_.get('mat') == b_.get('mat')
    report('x 메뉴 자르기 → 두 줄 (길이 합 · 끝 = 시작 · 관경 · 관종 · 이름 -1 · -2)', okx, '%.2f + %.2f = %.2f / %.2f · %s · %s' % (la, lb, la + lb, len0, a_['label'], b_['label']))
    oks = any(abs(q['b'] - la) < 0.02 for q in sa) and any(abs(q['a']) < 0.02 for q in sb)
    report('x 가시설 걸친 것 = 자른 자리에서 둘로', oks, '%s | %s' % ([(q['a'], q['b']) for q in sa], [(q['a'], q['b']) for q in sb]))
    shot(ed, SD, '45_관자름.png')
    inp.key('z', 'KeyZ', 90, ce.CTRL)
    report('x Ctrl+Z → 한 번에 두 줄 되돌림', J(ed, 'JSON.stringify(window.RV_PIPES.lines[0])') == orig0 and J(ed, 'window.RV_PIPES.lines[%d].pts.length' % n0) == 0)
    p1, p2 = J(ed, '__t.at(0, %f, true)' % (sc - 6)), J(ed, '__t.at(0, %f, true)' % (sc + 6))
    ed.js('__map3d.edMode(true)'); ed.frames(2)
    inp.el_click('edCut')
    cur = J(ed, "getComputedStyle(document.getElementById('c')).cursor")
    inp.click(p1[0], p1[1])
    inp.click(p2[0], p2[1])
    sel = J(ed, '__map3d.ED.sel')
    ls = J(ed, '__t.L(%d) ? __t.L(%d).len : -1' % (sel, sel)) if sel >= 0 else -1
    report('x [자르기] 켬 (십자) → 두 곳 → 사이 구간 고름 (12 m)', cur == 'crosshair' and sel >= 0 and abs(ls - 12) < 0.6 and not J(ed, '__map3d.ED.cut'), '%s · 고른 %s · %.2f m' % (cur, sel, ls))
    shot(ed, SD, '46_구간고름.png')
    nv = J(ed, '__map3d.lines().length')
    inp.key('Delete', 'Delete', 46)
    report('x Delete → 그 구간 바로 지움 (알림 단추 없음)', J(ed, '__map3d.lines().length') == nv - 1 and J(ed, 'window.RV_PIPES.lines[%d].pts.length' % sel) == 0 and not J(ed, "!!document.querySelector('#toast.show button')"))
    shot(ed, SD, '47_구간지움.png')
    tl = J(ed, '__map3d.lines().reduce((s, L) => s + L.len, 0)')
    ed.js('__map3d.edMode(false)')
    for _ in range(6):
        if J(ed, 'JSON.stringify(window.RV_PIPES.lines[0])') == orig0:
            break
        inp.key('z', 'KeyZ', 90, ce.CTRL)
    report('x Ctrl+Z 여러 번 → 처음 관 그대로', J(ed, 'JSON.stringify(window.RV_PIPES.lines[0])') == orig0 and J(ed, '__map3d.lines().reduce((s, L) => s + L.len, 0)') > tl + 11)
    errs(ed, '자르기')

    print('== s 새 관 스냅 (관 위 = 분기 · 끝점 = 이어 그리기 · Alt = 끔)')
    ed.js('__t.look(0, %f, 40, 50, 60)' % s0); ed.frames(3)
    ug.ui_click(ed, inp, 'pNew')
    q = J(ed, '__t.at(0, %f, true)' % (s0 + 3))
    h = (q[0] + 3, q[1] + 4)
    inp.move(*h)
    sn = J(ed, '__map3d.MS.snap && { kind: __map3d.MS.snap.kind, src: __map3d.MS.snap.src, name: __map3d.MS.snap.name }')
    report('s 관 가까이 → 스냅 표시 «분기 · 이름»', J(ed, '__map3d.MS.kind') == 'pipe' and sn and sn['kind'] == 'mid' and sn['src'] == 0, str(sn))
    shot(ed, SD, '48_스냅_분기.png')
    inp._m('mouseMoved', h[0], h[1], mods=ALT); ed.frames(2)
    report('s Alt → 스냅 없음', J(ed, '__map3d.MS.snap') is None)
    inp.click(*h)
    p0 = J(ed, '__map3d.MS.pts[0]'); T = J(ed, '__map3d.T()')
    on = seg(0, p0[0] - T[0], p0[1] - T[1])
    report('s 누르기 → 점 = 관 위 (평면 1 cm · 관 중심 높이)', on['d'] < 0.01 and abs(p0[2] - T[2] - on['zc']) < 0.01, '%.4f m · 높이 차 %.4f m' % (on['d'], p0[2] - T[2] - on['zc']))
    inp.click(*A)
    inp.key('Enter', 'Enter', 13)
    inp.el_click('npGo')
    time.sleep(0.5)
    nr = J(ed, 'window.RV_PIPES.lines[window.RV_PIPES.lines.length - 1]')
    fp = nr['pts'][0]
    report('s 새 관 첫 점 = 기존 관 위 (분기 · 1 cm)', seg(0, fp[0], fp[1])['d'] < 0.01, '%.4f m' % seg(0, fp[0], fp[1])['d'])
    inp.key('z', 'KeyZ', 90, ce.CTRL)
    L1 = J(ed, '(() => { const L = __t.L(1), c = L.pts.length / 3; return { n: window.RV_PIPES.lines[1].pts.length, e: L.pts[c * 3 - 3], nn: L.pts[c * 3 - 2], len: L.len }; })()')
    nl = J(ed, 'window.RV_PIPES.lines.length')
    ed.js('__t.look(1, %f, 30, 55, 20)' % L1['len']); ed.frames(3)
    se = J(ed, '(() => { const M = __map3d, L = __t.L(1), c = L.pts.length / 3 - 1; M.view(); return M.toScr(L.pts[c * 3], L.pts[c * 3 + 1], M.gAt(L, c)); })()')
    ug.ui_click(ed, inp, 'pNew')
    inp.move(se[0] + 5, se[1] + 4)
    sn = J(ed, '__map3d.MS.snap && { kind: __map3d.MS.snap.kind, src: __map3d.MS.snap.src, end: __map3d.MS.snap.end }')
    report('s 다른 관 끝점 가까이 → «이어 그리기» 스냅 (끝점 먼저)', sn and sn['kind'] == 'end' and sn['src'] == 1 and sn['end'] == 1, str(sn))
    shot(ed, SD, '49_스냅_끝점.png')
    inp.click(se[0] + 5, se[1] + 4)
    far = None
    for dx, dy in ((150, 70), (-150, 70), (150, -70), (-150, -70)):
        if pick(ed, se[0] + dx, se[1] + dy) is not None and J(ed, '__map3d.snapAt(%f, %f)' % (se[0] + dx, se[1] + dy)) is None:
            far = (se[0] + dx, se[1] + dy)
            break
    wf = pick(ed, *far) if far else None
    if far:
        inp.click(*far)
    inp.key('Enter', 'Enter', 13)
    time.sleep(0.3)
    r1 = J(ed, 'window.RV_PIPES.lines[1]')
    cps = [r1['pts'][i] for i in r1['ctrl']]
    okj = any(math.hypot(c[0] - L1['e'], c[1] - L1['nn']) < 1e-3 for c in cps)
    okl = wf is not None and math.hypot(cps[-1][0] - (wf[0] - T[0]), cps[-1][1] - (wf[1] - T[1])) < 0.002
    report('s 끝점에서 이어 그리기 → 같은 관 점 늘어남 · 관 개수 그대로 · 끝점 일치', far and len(r1['pts']) > L1['n'] and J(ed, 'window.RV_PIPES.lines.length') == nl and okj and okl,
           '점 %d → %d · 관 %d · %s' % (L1['n'], len(r1['pts']), J(ed, 'window.RV_PIPES.lines.length'), J(ed, "document.getElementById('toast').textContent")))
    shot(ed, SD, '50_이어그리기.png')
    inp.key('z', 'KeyZ', 90, ce.CTRL)
    report('s Ctrl+Z → 이어 그린 것 되돌림', len(J(ed, 'window.RV_PIPES.lines[1].pts')) == L1['n'])
    errs(ed, '스냅')


def wait_reload(ed, timeout=20):
    return ed.wait_js("!window.__mark && performance.getEntriesByType('navigation')[0].type === 'reload' && window.__map3d && "
                      "document.getElementById('load').style.display === 'none' && window.__drawn > 0", timeout)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--shots', required=True)
    ap.add_argument('--flat', default=None, help='3D 없는 프로젝트 폴더 (check_map3d_ug --flat 가 만든 것) · 결과물 다듬기 칸')
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    out, SD = os.path.abspath(a.out), os.path.abspath(a.shots)
    os.makedirs(SD, exist_ok=True)
    copy_site(out)
    from roadview import mapping
    gs = mapping.ground_sampler(out)
    srv, base = serve(out)
    ed = Edge(os.path.join(SD, '_prof'), WIN, gl=False).__enter__()
    inp = ce.Inp(ed)
    try:
        print('== 열기 (프로그램 흉내)')
        report('열림', setup_page(ed, base + 'index_host.html'))
        ed.js('localStorage.clear(); sessionStorage.clear()')
        setup_page(ed, base + 'index_host.html')
        O = J(ed, 'window.RV_MAPPING.origin')
        L0 = J(ed, '__t.L(0).len'); s0 = round(L0 * 0.3, 1)
        ed.js('__t.look(0, %f, 40, 50, 60)' % s0)
        ed.frames(3)
        sp = empty_spots(ed)
        report('빈 땅 자리', len(sp) >= 4, str(sp))
        A, B, C, D = sp[:4]
        errs(ed, '열기')

        print('== m 메뉴')
        rclick(ed, *A)
        cx = J(ed, '__map3d.ctx()')
        it = cx['items']
        want = ['center', 'shot', 'mDist', 'mArea', 'mHeight', 'mProf', 'pNew', 'photo', 'xray', 'pipes']
        report('m 빈 땅 항목', it == want, str(it))
        report('m 뺀 항목 없음 (JACK 1127 · 옵션 칸에서)', not any(k in it for k in GONE), str([k for k in GONE if k in it]))
        report('m 묶음 사이 구분선 3', J(ed, "document.querySelectorAll('#ctxMenu .sep').length") == 3)
        w = pick(ed, *A)
        hd = J(ed, "document.querySelector('#ctxMenu .cHd').textContent")
        exp = 'X %.2f · Y %.2f · Z %.2f' % (w[0] + O[0], w[1] + O[1], w[2] + O[2])
        report('m 맨 위 좌표 = 누른 자리', hd == exp, hd)
        shot(ed, SD, '20_메뉴_땅.png')
        inp.key('Escape', 'Escape', 27)
        report('m Esc → 닫힘', not J(ed, '__map3d.ctx().open') and J(ed, "document.getElementById('ctxMenu').hidden"))
        rclick(ed, *A)
        r = ce.rect(ed, 'title')
        inp.click(r['left'] + 5, r['top'] + r['height'] / 2)
        report('m 바깥 누르기 → 닫힘', not J(ed, '__map3d.ctx().open'))
        rclick(ed, *A)
        ed.call('Input.dispatchMouseEvent', {'type': 'mouseWheel', 'x': float(A[0]), 'y': float(A[1]), 'deltaX': 0, 'deltaY': 120})
        ed.frames(2)
        report('m 휠 → 닫힘', not J(ed, '__map3d.ctx().open'))
        ed.js("document.getElementById('tgSide').click()"); ed.frames(2)       # 오른쪽 칸 (붙박이) 을 접어야 화면 구석이 3D
        rclick(ed, WIN[0] - 6, WIN[1] - 6)
        mr = J(ed, "(() => { const r = document.getElementById('ctxMenu').getBoundingClientRect(); return [r.left, r.top, r.right, r.bottom]; })()")
        report('m 오른쪽 아래 구석 → 화면 안', J(ed, '__map3d.ctx().open') and mr[0] >= 0 and mr[1] >= 0 and mr[2] <= WIN[0] and mr[3] <= WIN[1], str([round(v) for v in mr]))
        ed.js("document.getElementById('tgSide').click()"); ed.frames(2)
        inp.key('Escape', 'Escape', 27)
        rbtn(ed, 'mousePressed', A[0], A[1], 2)                    # 오른쪽 끌기 = 아무것도 안 함 (메뉴 없음 · 시점 그대로)
        v0 = J(ed, '[__map3d.V.az, __map3d.V.el, __map3d.V.tgt]')
        for i in range(1, 9):
            ed.call('Input.dispatchMouseEvent', {'type': 'mouseMoved', 'x': float(A[0] + 8 * i), 'y': float(A[1] + 4 * i), 'button': 'right', 'buttons': 2})
        rbtn(ed, 'mouseReleased', A[0] + 64, A[1] + 32, 0)
        ed.frames(2)
        report('m 오른쪽 끌기 → 메뉴 없음 · 시점 그대로', not J(ed, '__map3d.ctx().open') and J(ed, '[__map3d.V.az, __map3d.V.el, __map3d.V.tgt]') == v0)
        pt = J(ed, '__t.at(0, %f, true)' % s0)
        rclick(ed, pt[0], pt[1])
        it = J(ed, '__map3d.ctx().items')
        report('m 관 위 = 관 항목 (자르기 포함) · 새 관 없음', all(k in it for k in ('pInfo', 'pEdit', 'pShore', 'pCut', 'pDel')) and it.index('pCut') == it.index('pDel') - 1 and 'pNew' not in it, str(it))
        shot(ed, SD, '20b_메뉴_관.png')
        inp.key('Escape', 'Escape', 27)
        ed.js('__map3d.edMode(true)')
        rclick(ed, *A)
        report('m 관로 고치기 켜진 동안 열림', J(ed, '__map3d.ctx().open') and J(ed, '__map3d.ED.on') is True)
        inp.key('Escape', 'Escape', 27)
        ed.js('__map3d.edMode(false)')
        errs(ed, '메뉴')

        print('== v 보기')
        w = pick(ed, *B)
        menu(ed, inp, B[0], B[1], 'center')
        t = J(ed, '__map3d.V.tgt')
        report('v 여기 가운데로', max(abs(t[i] - w[i]) for i in range(3)) < 1e-3, str([round(v, 3) for v in t]))
        ed.js('__t.look(0, %f, 40, 50, 60)' % s0); ed.frames(3)
        menu(ed, inp, A[0], A[1], 'shot')
        time.sleep(0.3)
        si = calls(ed, 'save_image')
        report('v 화면 그림 저장 (PNG)', si and si[-1]['head'] == 'data:image/png;base64,' and si[-1]['len'] > 20000 and si[-1]['name'].endswith('.png'), str(si[-1] if si else '-'))
        ug.ui_click(ed, inp, 'bTop')                               # 위에서 · 북쪽 · 처음 시점 = 옵션 칸 «보기» 단추 (JACK 1127 메뉴에서 뺌)
        el1 = J(ed, '__map3d.V.el')
        ug.ui_click(ed, inp, 'bTop')
        report('v [위에서] → 비스듬히 (옵션 칸)', el1 > 80 and J(ed, '__map3d.V.el') < 80, '%.1f' % el1)
        ug.ui_click(ed, inp, 'vNorth')
        report('v [북쪽] (옵션 칸)', J(ed, '__map3d.V.az') == 0)
        ed.js('(() => { const V = __map3d.V; V.az = 133; V.el = 70; V.d = 11; V.tgt = [5, 5, 0]; __map3d.redraw(); })()'); ed.frames(2)
        ug.ui_click(ed, inp, 'vHome')
        hv = J(ed, '[__map3d.V.az, __map3d.V.el, __map3d.V.d]')
        report('v [처음 시점] (옵션 칸)', hv[0] == 25 and hv[1] == 38 and hv[2] > 50, str([round(v, 1) for v in hv]))
        errs(ed, '보기')

        print('== d 재기')
        ed.js('__t.look(0, %f, 40, 50, 60)' % s0); ed.frames(3)
        pa, pb, pc = pick(ed, *A), pick(ed, *B), pick(ed, *C)
        menu(ed, inp, A[0], A[1], 'mDist')
        inp.click(*B); inp.click(*C)
        inp.key('Enter', 'Enter', 13)
        ms = J(ed, '{ k: __map3d.MS.kind, done: __map3d.MS.done, pts: __map3d.MS.pts }')
        P3 = np.array([pa, pb, pc])
        pl = sum(math.hypot(*(P3[i + 1, :2] - P3[i, :2])) for i in range(2)); sl = sum(np.linalg.norm(P3[i + 1] - P3[i]) for i in range(2))
        got = J(ed, "[Number(document.getElementById('ms_plan').dataset.v), Number(document.getElementById('ms_slope').dataset.v)]")
        report('d 거리: 점 셋 = 찍은 자리', ms['k'] == 'dist' and ms['done'] and len(ms['pts']) == 3 and np.abs(np.array(ms['pts']) - P3).max() < 1e-3)
        report('d 거리 평면 · 경사 합 (따로 셈)', abs(got[0] - pl) < 1e-3 and abs(got[1] - sl) < 1e-3, '%.3f · %.3f m' % (got[0], got[1]))
        report('d 결과 칸 · 안내 줄 숨김', not J(ed, "document.getElementById('msBox').hidden") and not J(ed, "document.getElementById('msChip').classList.contains('show')"))
        shot(ed, SD, '21_거리.png')
        inp.el_click('msClr')
        report('d [지우기]', J(ed, '__map3d.MS.kind') == '' and J(ed, "document.getElementById('msBox').hidden"))
        pd = pick(ed, *D)
        menu(ed, inp, A[0], A[1], 'mArea')
        inp.click(*B); inp.click(*C); inp.dblclick(*D)
        ms = J(ed, '{ k: __map3d.MS.kind, done: __map3d.MS.done, pts: __map3d.MS.pts }')
        P4 = np.array([pa, pb, pc, pd])
        ar = 0.5 * abs(sum(P4[i - 1, 0] * P4[i, 1] - P4[i, 0] * P4[i - 1, 1] for i in range(4)))
        got = J(ed, "Number(document.getElementById('ms_area').dataset.v)")
        report('d 면적: 두 번 누르기로 닫기 · 점 넷', ms['done'] and len(ms['pts']) == 4, str(len(ms['pts'])))
        report('d 평면 면적 (따로 셈)', abs(got - ar) < 1e-3, '%.2f m²' % got)
        shot(ed, SD, '22_면적.png')
        inp.key('Escape', 'Escape', 27)
        inp.el_click('msClr')
        menu(ed, inp, A[0], A[1], 'mHeight')
        inp.click(*C)
        dz, plh = pc[2] - pa[2], math.hypot(pc[0] - pa[0], pc[1] - pa[1])
        got = J(ed, "['dz', 'plan', 'grade'].map((k) => Number(document.getElementById('ms_' + k).dataset.v))")
        report('d 높이 차 · 평면 거리 · 기울기', J(ed, '__map3d.MS.done') and abs(got[0] - dz) < 1e-3 and abs(got[1] - plh) < 1e-3 and abs(got[2] - dz / plh * 100) < 1e-2,
               '%+.3f m · %.2f m · %.2f %%' % tuple(got))
        shot(ed, SD, '23_높이차.png')
        inp.el_click('msClr')
        menu(ed, inp, A[0], A[1], 'mProf')
        inp.click(*D)
        pr = J(ed, '__map3d.MS.prof')
        ok = bool(pr) and pr['L'] > 1
        if ok:
            E = np.array(pr['e']); N = np.array(pr['n']); dt = np.array([v if v is not None else np.nan for v in pr['dtm']], float)
            ref = gs(E, N)
            m = np.isfinite(dt) & np.isfinite(ref)
            dif = np.abs(dt[m] - ref[m]).max() if m.any() else 99
            report('d 단면 땅 = DTM (따로 셈)', m.sum() > 0.8 * len(dt) and dif < 0.02, '최대 차 %.4f m · %d 점' % (dif, m.sum()))
            md = np.array([v if v is not None else np.nan for v in pr['mdl']], float)
            idx = [i for i in np.linspace(0, len(md) - 1, 7).astype(int)[1:-1] if np.isfinite(md[i])]
            dd = []
            for i in idx:                                       # 3D 모델 면 = 바로 위에서 본 화면 가운데 고르기
                z = J(ed, '(() => { const M = __map3d, V = M.V; V.tgt = [%f, %f, %f]; V.d = 25; V.el = 89.9; V.az = 0; M.redraw(); M.view(); const h = M.pickAt(innerWidth / 2, innerHeight / 2); return h ? h.p[2] : null; })()'
                      % (E[i] - O[0], N[i] - O[1], md[i] - O[2]))
                if z is not None:
                    dd.append(abs(z + O[2] - md[i]))
            report('d 단면 3D 모델 면 = 위에서 고르기', len(dd) >= 3 and np.median(dd) < 0.1, '가운데값 %.3f m · %d 곳' % (np.median(dd) if dd else 99, len(dd)))
            ed.js('__t.look(0, %f, 40, 50, 60)' % s0); ed.frames(3)
        svg = J(ed, "(() => { const s = document.getElementById('msSvg'), r = s.getBoundingClientRect(); return { h: r.height, w: r.width, n: s.querySelectorAll('path').length, t: s.textContent }; })()")
        report('d 단면 그림 (SVG · 땅 · 모델 · 보임)', ok and svg['h'] > 100 and svg['w'] > 300 and svg['n'] >= 3 and '땅 (DTM)' in svg['t'] and '3D 모델 면' in svg['t'], '%d×%d · 선 %d' % (svg['w'], svg['h'], svg['n']))
        shot(ed, SD, '24_지형단면.png')
        inp.el_click('msClr')
        errs(ed, '재기')

        print('== n 새 관')
        n0 = J(ed, 'window.RV_PIPES.lines.length')
        menu(ed, inp, A[0], A[1], 'pNew')
        inp.click(*B); inp.click(*C)
        pts = J(ed, '__map3d.MS.pts'); T = J(ed, '__map3d.T()')
        inp.key('Enter', 'Enter', 13)
        report('n 창 열림', J(ed, "!document.getElementById('dlgBack').hidden && !document.getElementById('npDlg').hidden"))
        inp.el_click('npDia'); ed.js("document.getElementById('npDia').select()"); ed.call('Input.insertText', {'text': '250'})
        q = J(ed, "(() => { const r = document.querySelector('#npMat button[data-m=\"steel\"]').getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; })()")
        inp.click(*q)
        inp.el_click('npName'); ed.call('Input.insertText', {'text': '시험 관'})
        shot(ed, SD, '25_새관_창.png')
        inp.el_click('npGo')
        time.sleep(1.3)
        raw = J(ed, 'window.RV_PIPES.lines[window.RV_PIPES.lines.length - 1]')
        cps = [raw['pts'][k][:2] for k in raw['ctrl']]
        okn = J(ed, 'window.RV_PIPES.lines.length') == n0 + 1 and abs(raw['dia'] - 0.25) < 1e-9 and raw['mat'] == 'steel' and raw['label'] == '시험 관' and len(cps) == 3
        okn = okn and max(math.hypot(cps[i][0] - (pts[i][0] - T[0]), cps[i][1] - (pts[i][1] - T[1])) for i in range(3)) < 0.002
        report('n 새 줄 (D250 · 강관 · 이름 · 꼭짓점 = 찍은 자리)', okn, str({k: raw.get(k) for k in ('dia', 'mat', 'label', 'layer')}))
        ed.js("document.getElementById('bSave').click()")              # JACK 1127: 윗줄 [저장] 을 눌러야 씀
        ed.wait_js("__host.calls.some((c) => c.name === 'save_pipes')", 5)
        sv = calls(ed, 'save_pipes')
        oks = bool(sv) and len(json.loads(sv[-1].split('=', 1)[1].strip().rstrip(';'))['lines']) == n0 + 1
        report('n save_pipes (새 줄 포함)', oks, '%d 번' % len(sv))
        report('n 보기에 새 관', any(L == n0 for L in J(ed, '__map3d.lines().map((L) => L.src)')) and J(ed, '__map3d.MS.kind') == '')
        shot(ed, SD, '25b_새관.png')
        errs(ed, '새 관')

        print('== nb 새 관 분기 (기존 관 위 누름 = 그 관 중심선에 붙음) · 고치기 Delete = 관 지우기 (JACK 1127)')
        ed.js('__t.look(0, %f, 40, 50, 60)' % (s0 + 4)); ed.frames(3)
        q0 = J(ed, '__t.at(0, %f, true)' % (s0 + 4))
        off = [q0[0] + 140, q0[1] + 60]
        nb0 = J(ed, 'window.RV_PIPES.lines.length')
        menu(ed, inp, off[0], off[1], 'pNew')
        inp.click(q0[0], q0[1])
        ed.frames(2)
        tt = J(ed, "document.getElementById('toast').textContent")
        inp.key('Enter', 'Enter', 13)
        ed.frames(2)
        inp.el_click('npGo')
        time.sleep(1.0)
        rawb = J(ed, 'window.RV_PIPES.lines[window.RV_PIPES.lines.length - 1]')
        endp = rawb['pts'][rawb['ctrl'][-1]][:2] if rawb and rawb.get('ctrl') else None
        LS = J(ed, '__map3d.lines().filter((L) => L.src !== window.RV_PIPES.lines.length - 1).map((L) => Array.from(L.pts))')   # 기존 관 전부 (새로 그린 것 빼고)
        def dseg(p, a, b):
            ax, ay, bx, by = a[0], a[1], b[0], b[1]
            l2 = (bx - ax) ** 2 + (by - ay) ** 2
            u = 0 if l2 == 0 else max(0, min(1, ((p[0] - ax) * (bx - ax) + (p[1] - ay) * (by - ay)) / l2))
            return math.hypot(p[0] - ax - u * (bx - ax), p[1] - ay - u * (by - ay))
        dmin = min(dseg(endp, (L0[k], L0[k + 1]), (L0[k + 3], L0[k + 4])) for L0 in LS for k in range(0, len(L0) - 3, 3)) if endp else 99
        report('nb 끝점 = 누른 기존 관 중심선 위 (1 cm) · 알림 «분기»', J(ed, 'window.RV_PIPES.lines.length') == nb0 + 1 and dmin < 0.01 and '분기' in tt, '%.4f m · %s' % (dmin, tt))
        shot(ed, SD, '25c_분기.png')
        sb = J(ed, 'window.RV_PIPES.lines.length') - 1
        qm = J(ed, '__t.at(%d, 2, true)' % sb)
        okm = menu(ed, inp, qm[0], qm[1], 'pEdit') if qm else False
        ed.frames(2)
        inp.key('Delete', 'Delete', 46)
        ed.frames(2)
        nob = not J(ed, "!!document.querySelector('#toast.show button')")
        report('nb 고치기 · 점 안 고르고 Delete → 바로 지움 (알림 단추 없음 · JACK 1127)', okm and nob and J(ed, 'window.RV_PIPES.lines[%d].pts.length' % sb) == 0 and sb not in J(ed, '__map3d.lines().map((L) => L.src)'),
               str([okm, nob, J(ed, "document.getElementById('toast').textContent")]))
        inp.key('Escape', 'Escape', 27)
        errs(ed, '분기')

        print('== p 관')
        ed.js('__t.look(0, %f, 40, 50, 60)' % s0); ed.frames(3)
        pt = J(ed, '__t.at(0, %f, true)' % s0)
        menu(ed, inp, pt[0], pt[1], 'pInfo')
        sec = J(ed, "(() => ({ card: !document.getElementById('pipeCard').hidden, src: document.getElementById('pipeCard').dataset.src, sec: !document.getElementById('secDlg').hidden, at: document.getElementById('secDlg').dataset.at }))()")
        report('p 관 정보 · 그 자리 단면', sec['card'] and sec['src'] == '0' and sec['sec'] and abs(float(sec['at'] or -99) - s0) < 0.6, str(sec))
        shot(ed, SD, '26_관정보_단면.png')
        inp.el_click('secX'); inp.el_click('pcX')
        okm = menu(ed, inp, pt[0], pt[1], 'pEdit')
        ed.frames(2)
        report('p 이 관 고치기 (그 관 고름)', okm and J(ed, '__map3d.ED.on') is True and J(ed, '__map3d.ED.sel') == 0 and J(ed, "document.getElementById('edChip').classList.contains('show')"),
               str([okm, J(ed, '__map3d.ED.on'), J(ed, '__map3d.ED.sel')]))
        inp.key('Escape', 'Escape', 27)
        sh0 = J(ed, 'window.RV_PIPES.lines[0].shore || []')
        rclick(ed, pt[0], pt[1])
        inp.click(*item_xy(ed, 'pShore'))
        report('p 가시설 → 종류 칸 (메뉴 그대로)', J(ed, '__map3d.ctx().open') and bool(item_xy(ed, 'pShore:hpile')))
        inp.click(*item_xy(ed, 'pShore:hpile'))
        sa = J(ed, '__map3d.SH.a')
        report('p 가시설 시작점 = 누른 자리', J(ed, '__map3d.SH.arm') == 'hpile' and sa and abs(sa['s'] - s0) < 0.6, str(sa and sa['s']))
        q = J(ed, '__t.at(0, %f, true)' % (s0 + 8))
        inp.click(q[0], q[1])
        sh1 = J(ed, 'window.RV_PIPES.lines[0].shore || []')
        new = [x for x in sh1 if x not in sh0]
        report('p 가시설 구간 (끝점)', len(new) == 1 and new[0]['t'] == 'hpile' and abs(new[0]['a'] - s0) < 0.6 and abs(new[0]['b'] - s0 - 8) < 0.6, str(new))
        ed.js('__t.look(1, 9, 30, 50, 20)'); ed.frames(3)
        q1 = J(ed, '__t.at(1, 9, false)')
        b1 = J(ed, 'JSON.stringify(window.RV_PIPES.lines[1])')
        if q1:
            nv0 = J(ed, '__map3d.lines().length')
            okm = menu(ed, inp, q1[0], q1[1], 'pDel')
            ed.frames(2)
            gone = J(ed, 'window.RV_PIPES.lines[1].pts.length') == 0 and 1 not in J(ed, '__map3d.lines().map((L) => L.src)')
            nob = not J(ed, "!!document.querySelector('#toast.show button')")
            report('p 지우기 = 바로 (빈 줄 · 보기에서 빠짐 · 관 −1 · 알림 단추 없음)', okm and gone and nob and J(ed, '__map3d.lines().length') == nv0 - 1, J(ed, "document.getElementById('toast').textContent"))
            shot(ed, SD, '27_관지움.png')
            ok_out = J(ed, '__map3d.ED.on') is False
            inp.key('z', 'KeyZ', 90, ce.CTRL)                      # 고치기 밖에서도 Ctrl+Z (JACK 1127 «상시»)
            back = J(ed, 'JSON.stringify(window.RV_PIPES.lines[1])') == b1 and 1 in J(ed, '__map3d.lines().map((L) => L.src)')
            report('p 고치기 밖 Ctrl+Z → 되살아남', ok_out and back)
            inp.key('y', 'KeyY', 89, ce.CTRL)
            redo = J(ed, 'window.RV_PIPES.lines[1].pts.length') == 0
            inp.key('z', 'KeyZ', 90, ce.CTRL)
            report('p Ctrl+Y → 다시 지움 · Ctrl+Z → 되살아남', redo and J(ed, 'JSON.stringify(window.RV_PIPES.lines[1])') == b1)
            ed.js("__map3d.reveal('qGap')"); ed.frames(2)
            ed.js("document.getElementById('qGap').focus()")
            nu = J(ed, '__map3d.ED.undo.length')
            inp.key('z', 'KeyZ', 90, ce.CTRL)
            report('p 입력 칸 안 Ctrl+Z = 무시', J(ed, '__map3d.ED.undo.length') == nu and J(ed, 'JSON.stringify(window.RV_PIPES.lines[1])') == b1)
            ed.js("document.activeElement && document.activeElement.blur()")
        else:
            report('p 가지 관 화면에 보임', False)
        errs(ed, '관')

        zcut(ed, inp, SD, s0, A)                                   # JACK 1127: 간섭 고리 · 관 높이 · 관 자르기 · 새 관 스냅

        print('== o 기타')
        ed.js("__map3d.setMode('area'); __map3d.edMode(false)")
        ed.js('__t.look(0, %f, 40, 50, 60)' % s0); ed.frames(3)
        w = pick(ed, *A)
        menu(ed, inp, A[0], A[1], 'photo')
        ok = ed.wait_js("document.getElementById('phBox').dataset.name === 'P1.JPG'", 5)
        pa_ = calls(ed, 'photos_at')
        report('o 원본 사진: photos_at 좌표 = 누른 자리', pa_ and max(abs(pa_[-1][i] - (w[i] + O[i])) for i in range(3)) < 1e-3, str(pa_[-1] if pa_ else '-'))
        mk = J(ed, "[document.getElementById('phMark').style.left, document.getElementById('phMark').style.top, document.getElementById('phMark').hidden, document.getElementById('phImg').naturalWidth]")
        report('o 그림 · 표시 자리 (50 % · 50 %)', ok and mk[0] == '50%' and mk[1] == '50%' and not mk[2] and mk[3] == 800, str(mk))
        shot(ed, SD, '28_원본사진.png')
        inp.el_click('phNext')
        ok2 = ed.wait_js("document.getElementById('phBox').dataset.name === 'P2.JPG'", 5)
        inp.el_click('phZoom')
        ok3 = ed.wait_js("document.getElementById('phBox').dataset.w === '400'", 5)
        pv = calls(ed, 'photo_view')
        report('o 다음 · 확대', ok2 and ok3 and pv[-1] == ['P2.JPG', 120, 500, True], str(pv[-1]))
        inp.key('ArrowLeft', 'ArrowLeft', 37)
        ok4 = ed.wait_js("document.getElementById('phBox').dataset.name === 'P1.JPG'", 5)
        inp.key('Escape', 'Escape', 27)
        report('o ← · Esc', ok4 and J(ed, "document.getElementById('phBox').hidden"))
        x0 = J(ed, "document.getElementById('bXray').classList.contains('on')")
        menu(ed, inp, A[0], A[1], 'xray')
        report('o 투시 켜고 끄기', J(ed, "document.getElementById('bXray').classList.contains('on')") != x0)
        menu(ed, inp, A[0], A[1], 'xray')
        menu(ed, inp, A[0], A[1], 'pipes')
        p1 = J(ed, '__map3d.S.pipes')
        menu(ed, inp, A[0], A[1], 'pipes')
        report('o 관로 숨기기 · 보이기', p1 is False and J(ed, '__map3d.S.pipes') is True)
        ug.ui_click(ed, inp, 'bFiles')                             # 결과물 폴더 · 검사점 = 옵션 칸 단추 (JACK 1127 메뉴에서 뺌)
        time.sleep(0.2)
        report('o [결과물 폴더] (옵션 칸)', len(calls(ed, 'open_results')) == 1)
        w = pick(ed, *B)
        ed.js("__host.ckpicks = [{ ok: true, points: [{ name: 'K1', E: %f, N: %f, Z: %f }, { name: 'K2', E: %f, N: %f, Z: null }], note: '' }]"
              % (w[0] + O[0] + 0.3, w[1] + O[1] - 0.2, w[2] + O[2], w[0] + O[0] + 80, w[1] + O[1] + 80))
        ug.ui_click(ed, inp, 'bCheck')
        ed.wait_js("document.getElementById('ck').style.display === 'flex'", 5)
        w = pick(ed, *B)
        inp.click(*B)
        ok = ed.wait_js('window.__ck && window.__ck.measured === 1', 5)
        m = J(ed, '__map3d.CK.pts[0].m')
        report('o [검사점] (파일 고르기) → 모델 누르기 → K1 = 이 자리', ok and len(calls(ed, 'pick_checkpoints')) == 1 and m and w and math.hypot(m[0] - w[0] - O[0], m[1] - w[1] - O[1]) < 1e-3, str(J(ed, 'window.__ck')))
        shot(ed, SD, '29_검사점.png')
        errs(ed, '기타')

        print('== k 잘라내기')
        ed.js("document.getElementById('ckClose').click()")
        ed.js('__t.look(0, %f, 60, 50, 60)' % s0); ed.frames(3)
        ug.ui_click(ed, inp, 'tbCut')                              # 잘라내기 = 옵션 칸 «결과물» [잘라내기] (JACK 1127 메뉴에서 뺌) → 첫 구석 누르기
        ed.frames(3)
        F = (WIN[0] * 0.4 - 130, WIN[1] * 0.5 - 75)
        w = pick(ed, *F)
        inp.click(*F)
        st = J(ed, '{ k: __map3d.MS.kind, rect: __map3d.MS.rect, n: __map3d.MS.pts.length, el: __map3d.V.el, tb: !document.getElementById("trimBox").hidden, p: __map3d.MS.pts[0] }')
        report('k [잘라내기] → 위에서 보기 · 네모 · 첫 구석 = 누른 자리 · 다듬기 칸', st['k'] == 'crop' and st['rect'] and st['n'] == 1 and st['el'] > 80 and st['tb'] and len(calls(ed, 'crop_get')) >= 1
               and w and max(abs(st['p'][i] - w[i]) for i in range(3)) < 1e-3, str({k: st[k] for k in ('k', 'rect', 'n', 'el', 'tb')}))
        ed.frames(3)
        c0 = J(ed, '__map3d.toScr(__map3d.MS.pts[0][0] - __map3d.T()[0], __map3d.MS.pts[0][1] - __map3d.T()[1], __map3d.MS.pts[0][2] - __map3d.T()[2])')
        q2 = (c0[0] + 260, c0[1] + 150)
        inp.click(*q2)
        ms = J(ed, '{ done: __map3d.MS.done, pts: __map3d.MS.pts }')
        report('k 맞은 구석 누르기 → 네모 (꼭짓점 넷)', ms['done'] and len(ms['pts']) == 4 and not J(ed, "document.getElementById('msEnd').disabled"))
        shot(ed, SD, '30_잘라내기_네모.png')
        c2 = J(ed, '__map3d.toScr(...(() => { const p = __map3d.MS.pts[2], T = __map3d.T(); return [p[0] - T[0], p[1] - T[1], p[2] - T[2]]; })())')
        p2a = ms['pts'][2]
        inp.drag((c2[0], c2[1]), (c2[0] + 40, c2[1] + 30))
        p2b = J(ed, '__map3d.MS.pts[2]')
        report('k 꼭짓점 끌기', math.hypot(p2b[0] - p2a[0], p2b[1] - p2a[1]) > 1.0 and J(ed, '__map3d.MS.pts.length') == 4)
        inp.el_click('msPoly')
        report('k 다각형으로 → 첫 점만 남음', J(ed, '__map3d.MS.rect') is False and J(ed, '__map3d.MS.pts.length') == 1 and not J(ed, '__map3d.MS.done'))
        for dx, dy in ((240, 20), (200, 190), (-30, 170)):
            inp.click(c0[0] + dx, c0[1] + dy)
        inp.click(c0[0], c0[1])                                    # 첫 점 누르기 = 닫기
        report('k 다각형 첫 점 누르기 → 닫힘 (점 넷)', J(ed, '__map3d.MS.done') is True and J(ed, '__map3d.MS.pts.length') == 4)
        shot(ed, SD, '31_잘라내기_다각형.png')
        poly = [[p[0] + O[0], p[1] + O[1]] for p in J(ed, '__map3d.MS.pts')]
        ed.js('window.__mark = 1')
        inp.el_click('msEnd')
        okr = wait_reload(ed)
        ca = json.loads(J(ed, "sessionStorage.getItem('__crop_apply') || 'null'") or 'null')
        okp = ca and len(ca['polygon']) == 4 and max(math.hypot(ca['polygon'][i][0] - poly[i][0], ca['polygon'][i][1] - poly[i][1]) for i in range(4)) < 0.002
        report('k 적용 → crop_apply (도면 좌표) → 진행 → 다시 엶', okr and okp, str(ca and ca['polygon'][0]))
        ed.js(ug.T_JS)
        errs(ed, '잘라내기')

        print('== t 결과물 다듬기 칸')
        ed.js("__host.crop = %s" % json.dumps(poly))
        ug.cat_open(ed, inp, 'bTrim', fresh=True)
        ok = ed.wait_js("!document.getElementById('tbClear').hidden", 5)
        tb = J(ed, "[document.getElementById('tbState').textContent, document.querySelector('#tbDem span').textContent]")
        report('t 칸 · 잘림 상태 · DEM 글', ok and tb[0].startswith('잘림 · 점 4개') and tb[1] == ('DEM 다시 만들기' if 'dtm' in J(ed, 'window.RV_MAPPING.outputs || []') else 'DEM 만들기'), str(tb))
        shot(ed, SD, '32_결과물다듬기.png')
        ug.ui_click(ed, inp, 'tbWeb')
        okw = ed.wait_js("!document.getElementById('tbUrlRow').hidden", 10)
        url = J(ed, "document.getElementById('tbUrl').value")
        ug.ui_click(ed, inp, 'tbCopy')
        time.sleep(0.2)
        report('t 웹 공유 → 진행 → 주소 · 복사', okw and url.startswith('https://') and calls(ed, 'copy_text')[-1] == url, url)
        shot(ed, SD, '33_웹공유.png')
        ed.js('window.__mark = 1')
        ug.ui_click(ed, inp, 'tbDem')
        okr = wait_reload(ed)
        report('t DEM 만들기 → add_outputs(["dtm"]) → 다시 엶', okr and J(ed, "sessionStorage.getItem('__add_outputs')") == '["dtm"]')
        ed.js("__host.crop = %s" % json.dumps(poly))
        ug.cat_open(ed, inp, 'bTrim', fresh=True)
        ed.wait_js("!document.getElementById('tbClear').hidden", 5)
        ed.js('window.__mark = 1')
        ug.ui_click(ed, inp, 'tbClear')
        okc = toast_btn(ed, inp)
        okr = wait_reload(ed)
        report('t 잘라내기 없앰 (확인 → crop_clear → 다시 엶)', okc and okr and J(ed, "sessionStorage.getItem('__crop_clear')") == '1')
        errs(ed, '다듬기')

        print('== b 브라우저 (프로그램 밖)')
        setup_page(ed, base + 'index.html')
        ed.js('__t.look(0, %f, 40, 50, 60)' % s0); ed.frames(3)
        rclick(ed, *A)
        it = J(ed, '__map3d.ctx().items')
        report('b 프로그램 항목 없음 · 윗줄 프로그램 단추 숨김', not any(k in it for k in ('photo', 'files', 'crop', 'ck', 'ngHere', 'ngDel')) and J(ed, ug.HOST_HIDDEN), str(it))
        inp.key('Escape', 'Escape', 27)
        dl = os.path.join(SD, '_prof', 'downloads')
        before = set(glob.glob(os.path.join(dl, '*.png')))
        menu(ed, inp, A[0], A[1], 'shot')
        t0 = time.time()
        new = []
        while time.time() - t0 < 5 and not new:
            new = [p for p in set(glob.glob(os.path.join(dl, '*.png'))) - before if os.path.getsize(p) > 20000]
            time.sleep(0.2)
        report('b 화면 그림 = 내려받기 PNG', bool(new), os.path.basename(new[0]) if new else '-')
        menu(ed, inp, A[0], A[1], 'mDist')
        inp.click(*B)
        inp.dblclick(*C)
        report('b 거리 재기 (두 번 누르기로 끝)', J(ed, '__map3d.MS.done') is True and J(ed, '__map3d.MS.pts.length') == 3)
        inp.el_click('msClr')
        n0 = J(ed, 'window.RV_PIPES.lines.length')
        menu(ed, inp, A[0], A[1], 'pNew')
        inp.click(*B)
        inp.key('Enter', 'Enter', 13)
        inp.el_click('npName'); ed.call('Input.insertText', {'text': '브라우저 관'})
        inp.el_click('npGo')
        setup_page(ed, base + 'index.html')                       # 다시 열기 → 이 브라우저에 남은 새 관
        lb = J(ed, "__map3d.lines().map((L) => L.base)")
        report('b 새 관 → 다시 열어도 남음 (이 브라우저)', J(ed, 'window.RV_PIPES.lines.length') == n0 + 1 and '브라우저 관' in lb, str(lb))
        ed.js('localStorage.clear()')
        errs(ed, '브라우저')
    finally:
        ed.__exit__(None, None, None)
        srv.shutdown()

    if a.flat and os.path.exists(os.path.join(a.flat, 'data', 'mapping.js')):     # 3D 없는 프로젝트: 다듬기 칸 (잘라내기 빼고) · DEM
        print('== f 3D 없는 프로젝트 · 결과물 다듬기')
        fd = os.path.abspath(a.flat)
        copy_site(fd)
        srv, base = serve(fd)
        with Edge(os.path.join(SD, '_prof5'), WIN, gl=False) as ed:
            inp = ce.Inp(ed)
            ed.goto(base + 'index_host.html')
            ed.wait_js(Edge.NEW + " && window.__map3d && window.__map3d.flat === true", 30)
            ed.js('sessionStorage.clear()')
            ug.cat_open(ed, inp, 'bTrim', fresh=True)
            st = J(ed, "[!document.getElementById('trimBox').hidden, document.getElementById('tbCut').hidden, document.querySelector('#tbDem span').textContent]")
            report('f 다듬기 칸 · 잘라내기 없음 · DEM 만들기', st == [True, True, 'DEM 만들기'], str(st))
            shot(ed, SD, '34_3D없음_다듬기.png')
            ed.js('window.__mark = 1')
            ug.ui_click(ed, inp, 'tbDem')
            okr = ed.wait_js("!window.__mark && performance.getEntriesByType('navigation')[0].type === 'reload' && window.__map3d && window.__map3d.flat === true", 20)
            report('f DEM 만들기 → add_outputs → 다시 엶', okr and J(ed, "sessionStorage.getItem('__add_outputs')") == '["dtm"]')
            errs(ed, '3D 없음')
        srv.shutdown()

    for where, e in ug.R['errs']:
        print('  오류', where, e)
    print('== %s · 실패 %d %s' % ('통과' if ug.R['ok'] else '실패', len(ug.R['fails']), ug.R['fails']))
    return 0 if ug.R['ok'] else 1


if __name__ == '__main__':
    sys.exit(main())
