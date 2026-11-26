"""3D 매핑 보기 · 오른쪽 메뉴 시험 (JACK 1126): 화면 없는 Edge (cdp.py) 에 진짜 마우스 오른쪽 눌렀다 떼기 → 메뉴 항목마다 한 번씩.
프로그램 쪽은 hostmock.js 흉내 (photos_at · photo_view · save_image · crop_* · add_outputs · export_web · job …).

    python -I check_map3d_ctx.py --out <3D 매핑 결과 폴더 (check_map3d_ug 와 같은 것)> --shots <그림 폴더>

  m  메뉴: 빈 땅 = 보기 · 재기 · 새 관 · 기타 (관 항목 없음) · 관 위 = 관 항목 (새 관 없음) · 맨 위 좌표 = 누른 자리 ·
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
    shutil.copy2(os.path.join(HERE, 'hostmock.js'), os.path.join(out, 'viewer', 'hostmock.js'))
    html = open(os.path.join(out, 'index.html'), encoding='utf-8').read()
    open(os.path.join(out, 'index_host.html'), 'w', encoding='utf-8').write(
        html.replace('<script src="viewer/map3d.js"></script>', '<script src="viewer/hostmock.js"></script>\n<script src="viewer/map3d.js"></script>'))


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
        want = ['center', 'copy', 'shot', 'home', 'top', 'north', 'mDist', 'mArea', 'mHeight', 'mProf', 'pNew', 'photo', 'xray', 'pipes', 'files', 'ck', 'crop']
        report('m 빈 땅 항목', it == want, str(it))
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
        rclick(ed, WIN[0] - 6, WIN[1] - 6)
        mr = J(ed, "(() => { const r = document.getElementById('ctxMenu').getBoundingClientRect(); return [r.left, r.top, r.right, r.bottom]; })()")
        report('m 오른쪽 아래 구석 → 화면 안', J(ed, '__map3d.ctx().open') and mr[0] >= 0 and mr[1] >= 0 and mr[2] <= WIN[0] and mr[3] <= WIN[1], str([round(v) for v in mr]))
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
        report('m 관 위 = 관 항목 · 새 관 없음', all(k in it for k in ('pInfo', 'pEdit', 'pShore', 'pDel')) and 'pNew' not in it, str(it))
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
        w = pick(ed, *A)
        menu(ed, inp, A[0], A[1], 'copy')
        cp = calls(ed, 'copy_text')
        val = [float(v) for v in cp[-1].split(',')] if cp else []
        report('v 좌표 · 표고 복사 «X, Y, Z»', len(val) == 3 and max(abs(val[i] - (w[i] + O[i])) for i in range(3)) < 0.0011, cp[-1] if cp else '-')
        menu(ed, inp, A[0], A[1], 'shot')
        time.sleep(0.3)
        si = calls(ed, 'save_image')
        report('v 화면 그림 저장 (PNG)', si and si[-1]['head'] == 'data:image/png;base64,' and si[-1]['len'] > 20000 and si[-1]['name'].endswith('.png'), str(si[-1] if si else '-'))
        menu(ed, inp, A[0], A[1], 'top')
        el1 = J(ed, '__map3d.V.el')
        rclick(ed, 640, 450)
        lab = J(ed, "document.querySelector('#ctxMenu [data-id=\"top\"] span').textContent")
        q = item_xy(ed, 'top'); inp.click(*q)
        report('v 위에서 보기 → 비스듬히', el1 > 80 and lab == '비스듬히 보기' and J(ed, '__map3d.V.el') < 80, '%.1f · %s' % (el1, lab))
        menu(ed, inp, 640, 450, 'north')
        report('v 북쪽 보기', J(ed, '__map3d.V.az') == 0)
        ed.js('(() => { const V = __map3d.V; V.az = 133; V.el = 70; V.d = 11; V.tgt = [5, 5, 0]; __map3d.redraw(); })()'); ed.frames(2)
        menu(ed, inp, 640, 450, 'home')
        hv = J(ed, '[__map3d.V.az, __map3d.V.el, __map3d.V.d]')
        report('v 처음 시점', hv[0] == 25 and hv[1] == 38 and hv[2] > 50, str([round(v, 1) for v in hv]))
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
        sv = calls(ed, 'save_pipes')
        oks = bool(sv) and len(json.loads(sv[-1].split('=', 1)[1].strip().rstrip(';'))['lines']) == n0 + 1
        report('n save_pipes (새 줄 포함)', oks, '%d 번' % len(sv))
        report('n 보기에 새 관', any(L == n0 for L in J(ed, '__map3d.lines().map((L) => L.src)')) and J(ed, '__map3d.MS.kind') == '')
        shot(ed, SD, '25b_새관.png')
        errs(ed, '새 관')

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
            okm = menu(ed, inp, q1[0], q1[1], 'pDel')
            okc = okm and toast_btn(ed, inp)
            ed.frames(2)
            gone = J(ed, 'window.RV_PIPES.lines[1].pts.length') == 0 and 1 not in J(ed, '__map3d.lines().map((L) => L.src)')
            report('p 지우기 (확인 → 빈 줄 · 보기에서 빠짐)', okc and gone)
            shot(ed, SD, '27_관지움.png')
            oku = toast_btn(ed, inp)
            report('p 되돌리기', oku and J(ed, 'JSON.stringify(window.RV_PIPES.lines[1])') == b1 and 1 in J(ed, '__map3d.lines().map((L) => L.src)'))
        else:
            report('p 가지 관 화면에 보임', False)
        errs(ed, '관')

        print('== o 기타')
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
        menu(ed, inp, A[0], A[1], 'files')
        time.sleep(0.2)
        report('o 결과물 폴더', len(calls(ed, 'open_results')) == 1)
        w = pick(ed, *B)
        ed.js("__host.ckpicks = [{ ok: true, points: [{ name: 'K1', E: %f, N: %f, Z: %f }, { name: 'K2', E: %f, N: %f, Z: null }], note: '' }]"
              % (w[0] + O[0] + 0.3, w[1] + O[1] - 0.2, w[2] + O[2], w[0] + O[0] + 80, w[1] + O[1] + 80))
        menu(ed, inp, B[0], B[1], 'ck')
        ok = ed.wait_js('window.__ck && window.__ck.measured === 1', 5)
        m = J(ed, '__map3d.CK.pts[0].m')
        report('o 검사점 찍기 (파일 고르기 → 가까운 K1 = 이 자리)', ok and len(calls(ed, 'pick_checkpoints')) == 1 and m and math.hypot(m[0] - w[0] - O[0], m[1] - w[1] - O[1]) < 1e-3, str(J(ed, 'window.__ck')))
        shot(ed, SD, '29_검사점.png')
        errs(ed, '기타')

        print('== k 잘라내기')
        ed.js("document.getElementById('ckClose').click()")
        ed.js('__t.look(0, %f, 60, 50, 60)' % s0); ed.frames(3)
        w = pick(ed, *A)
        menu(ed, inp, A[0], A[1], 'crop')
        st = J(ed, '{ k: __map3d.MS.kind, rect: __map3d.MS.rect, n: __map3d.MS.pts.length, el: __map3d.V.el, tb: !document.getElementById("trimBox").hidden }')
        report('k 위에서 보기 · 네모 · 첫 구석 = 누른 자리 · 다듬기 칸', st['k'] == 'crop' and st['rect'] and st['n'] == 1 and st['el'] > 80 and st['tb'] and len(calls(ed, 'crop_get')) >= 1, str(st))
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
        inp.el_click('bTrim')
        ok = ed.wait_js("!document.getElementById('tbClear').hidden", 5)
        tb = J(ed, "[document.getElementById('tbState').textContent, document.querySelector('#tbDem span').textContent]")
        report('t 칸 · 잘림 상태 · DEM 글', ok and tb[0].startswith('잘림 · 점 4개') and tb[1] == ('DEM 다시 만들기' if 'dtm' in J(ed, 'window.RV_MAPPING.outputs || []') else 'DEM 만들기'), str(tb))
        shot(ed, SD, '32_결과물다듬기.png')
        inp.el_click('tbWeb')
        okw = ed.wait_js("!document.getElementById('tbUrlRow').hidden", 10)
        url = J(ed, "document.getElementById('tbUrl').value")
        inp.el_click('tbCopy')
        time.sleep(0.2)
        report('t 웹 공유 → 진행 → 주소 · 복사', okw and url.startswith('https://') and calls(ed, 'copy_text')[-1] == url, url)
        shot(ed, SD, '33_웹공유.png')
        ed.js('window.__mark = 1')
        inp.el_click('tbDem')
        okr = wait_reload(ed)
        report('t DEM 만들기 → add_outputs(["dtm"]) → 다시 엶', okr and J(ed, "sessionStorage.getItem('__add_outputs')") == '["dtm"]')
        ed.js("__host.crop = %s" % json.dumps(poly))
        inp.el_click('bTrim')
        ed.wait_js("!document.getElementById('tbClear').hidden", 5)
        ed.js('window.__mark = 1')
        inp.el_click('tbClear')
        okc = toast_btn(ed, inp)
        okr = wait_reload(ed)
        report('t 잘라내기 없앰 (확인 → crop_clear → 다시 엶)', okc and okr and J(ed, "sessionStorage.getItem('__crop_clear')") == '1')
        errs(ed, '다듬기')

        print('== b 브라우저 (프로그램 밖)')
        setup_page(ed, base + 'index.html')
        ed.js('__t.look(0, %f, 40, 50, 60)' % s0); ed.frames(3)
        rclick(ed, *A)
        it = J(ed, '__map3d.ctx().items')
        report('b 프로그램 항목 없음 · 윗줄 프로그램 단추 숨김', not any(k in it for k in ('photo', 'files', 'crop', 'ck')) and J(ed, "document.getElementById('hostTools').hidden"), str(it))
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
            inp.el_click('bTrim')
            st = J(ed, "[!document.getElementById('trimBox').hidden, document.getElementById('tbCut').hidden, document.querySelector('#tbDem span').textContent]")
            report('f 다듬기 칸 · 잘라내기 없음 · DEM 만들기', st == [True, True, 'DEM 만들기'], str(st))
            shot(ed, SD, '34_3D없음_다듬기.png')
            ed.js('window.__mark = 1')
            inp.el_click('tbDem')
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
