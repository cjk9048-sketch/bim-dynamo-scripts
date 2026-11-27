"""3D 매핑 보기 · AI 부대공 시험 (JACK 1126): 결과 폴더를 작은 HTTP 서버로 열고 화면 없는 Edge (cdp.py) 를 진짜 마우스로 몬다.
AI 엔진은 쓰지 않음 — 시험용 가짜 결과 data/ai_budae_test.js (엔진 도우미와 정한 꼴) · 가짜 프로그램 API (viewer/hostmock_ai.js · 이 시험이 씀).
원래 data/pipes.js 는 건드리지 않음 — 곧은 관 둘 (A = 60 m · D500 / B = 40 m · D300 · A 오른쪽 15 m) 시험 관로 data/pipes_ai.js · 페이지 index_ai*.html.

    python -X utf8 -I check_map3d_ai.py --out <3D 매핑 결과 폴더 (check_map3d_ug 와 같은 것)> --shots <그림 폴더>

  k  카테고리: 종류 단추 여섯 (개수 = 아님 뺀 수) · 머리줄 «미확인 n» · 연석 끄기 → 목록에서 빠짐 · 땅 위 그림 바뀜 · 다시 켜기
  o  목록 차례 = 미확인 → 360 다름 → 신뢰도 낮은 것 (손으로 정한 차례와 같음) · 줄 누르기 → 그 자리로 + 고름 · 석축 높이 면 그물
  q  수량 «부대공» (이 관 / 전체) = 손셈: 임시점용 반폭 wv = D/2 + 여유 + 경사 × (매설깊이 + D + 기초 − 포장) · 끝 여유 · 겹친 넓이 · 안 길이 · 아님 뺌
  m  맞음 · 아님 · 종류 바꿈 → ai_save (0.8 초 모아) 로 넘긴 자료 · 수량 바로 바뀜 · 누르는 동안 차례 그대로 · 다시 펴면 새 차례
  r  AI 인식 (결과 없는 프로젝트): «AI 인식 안 함» → 360 고르기 → [AI 인식] → 엔진 받기 묻기 → ai_setup → ai_run (360 넘김) → 결과 다시 읽음
  b  브라우저 (웹 공유): 보기만 (인식 · 360 · 맞음/아님 단추 없음) · 종류 단추 · 수량 같음
  p  360 사진 (file: 로 연 쪽 · 가짜 360 폴더 data/pano/<이름>.js) → 원본 사진 창에 뜸 · 없는 사진 → 사진 단추 숨김
  모든 쪽 JS 오류 0
"""
import argparse
import base64
import io
import json
import math
import os
import shutil
import sys
import time

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
sys.path.insert(0, REPO)
from cdp import Edge  # noqa: E402
import check_edit as ce  # noqa: E402
from check_map3d_ug import J, R, cat_open, errs, report, serve, setup_page, shot, ui_click  # noqa: E402
from check_map3d_ctx import calls  # noqa: E402

WIN = (1600, 900)
AI_OFF = (10.0, -5.0)            # 결과 파일 origin = 매핑 원점 + 이것 (pts 는 그만큼 빼서 씀 · 화면이 다시 더해야 맞음)
LA, LB, BOFF = 60.0, 40.0, 15.0  # 관 A 길이 · 관 B 길이 · B 는 A 오른쪽 15 m (A 측점 10 에서 시작)

# 가짜 프로그램 API (hostmock.js 뒤 · map3d.js 앞) — ai_status · ai_setup · ai_run · ai_save · pick_folder · job 'AR' = __host.arDone 까지 «하는 중»
MOCK_JS = r"""(function () {
  const H = window.__host, api = window.pywebview && window.pywebview.api;
  if (!H || !api) return;
  const rec = (name, arg) => { H.calls.push({ name: name, arg: arg, t: performance.now() }); };
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  const job0 = api.job;
  H.aiEngine = false; H.arDone = false;
  api.ai_status = () => { rec('ai_status'); return wait(10).then(() => ({ ready: !!H.aiEngine, gpu: 'GPU', dir: 'AI' })); };   // 실제 프로그램 꼴 (ai_budae.status = ready · 1127)
  api.ai_setup = () => { rec('ai_setup'); return wait(10).then(() => ({ ok: true, job: 'AS' })); };
  api.ai_run = (o) => { rec('ai_run', o); return wait(10).then(() => ({ ok: true, job: 'AR' })); };
  api.ai_save = (project, data) => { rec('ai_save', { project: project, data: data }); return wait(10).then(() => ({ ok: true })); };
  api.pick_folder = () => { rec('pick_folder'); return wait(10).then(() => 'D:\\시험\\360_구간'); };
  api.job = (id) => {
    if (id === 'AR' && !H.arDone) { rec('job', id); return wait(10).then(() => ({ state: 'run', pct: 50, line: 'AI 인식 중 50%' })); }
    if (id === 'AS') H.aiEngine = true;
    return job0(id);
  };
})();
"""

HELP_JS = r"""
window.__ai = {
  wscr(x, y) { const M = __map3d, T = M.T(); M.view(); const g = M.demAt(x - T[0], y - T[1]); return M.toScr(x - T[0], y - T[1], Number.isFinite(g) ? g : 0); },
  order() { return [...document.querySelectorAll('#aiList .aiRow')].map((r) => r.dataset.id); },
  row(id) { return [...document.querySelectorAll('#aiList .aiRow')].find((r) => r.dataset.id === id) || null; },
  rect(id, sel) {
    const r = this.row(id); if (!r) return null; const e = r.querySelector(sel); if (!e) return null;
    e.scrollIntoView({ block: 'nearest' }); const b = e.getBoundingClientRect(); return [b.left + b.width / 2, b.top + b.height / 2];
  },
  q(k, all) { const e = document.getElementById((all ? 'qt_' : 'q_') + k); return e ? Number(e.dataset.v) : NaN; },
  rows() { return Object.keys(__map3d.QT.rowEl).filter((k) => !__map3d.QT.rowEl[k][0].hidden); },
  saves() { return __host.calls.filter((c) => c.name === 'ai_save').map((c) => c.arg); },
  setSel(id, v) { const r = this.row(id), s = r && r.querySelector('select'); if (!s) return false; s.value = v; s.dispatchEvent(new Event('change')); return true; },
};
"""


def read_js(path):
    t = open(path, encoding='utf-8').read()
    return json.loads(t[t.index('{'):t.rstrip().rstrip(';').rindex('}') + 1])


def frame(p0, u):
    """관 A 둘레 (s 측점 · t 왼쪽) → 관로 좌표 (매핑 원점과 같음)."""
    v = (-u[1], u[0])
    return lambda s, t: (p0[0] + u[0] * s + v[0] * t, p0[1] + u[1] * s + v[1] * t)


def make_data(out):
    """시험 관로 (곧은 관 둘) + 가짜 AI 결과 · 손셈에 쓸 꼴 (관 좌표 (s, t))."""
    P = read_js(os.path.join(out, 'data', 'pipes.js'))
    M = read_js(os.path.join(out, 'data', 'mapping.js'))
    org = M['origin']
    po = P.get('origin') or {'E': org[0], 'N': org[1]}
    TX, TY = po['E'] - org[0], po['N'] - org[1]
    pts = P['lines'][0]['pts']
    a, b = pts[0], pts[25]
    d = math.hypot(b[0] - a[0], b[1] - a[1])
    u = ((b[0] - a[0]) / d, (b[1] - a[1]) / d)
    p0 = (pts[6][0], pts[6][1])
    A = frame(p0, u)
    B0 = A(10.0, -BOFF)
    Bf = frame(B0, u)
    pipes = {'layers': P['layers'], 'origin': po, 'source': 'test_ai', 'lines': [
        {'layer': 0, 'label': 'A', 'dia': 0.5, 'hasZ': False, 'pts': [[*A(0, 0), None], [*A(LA, 0), None]]},
        {'layer': 1, 'label': 'B', 'dia': 0.3, 'hasZ': False, 'pts': [[*Bf(0, 0), None], [*Bf(LB, 0), None]]}]}
    open(os.path.join(out, 'data', 'pipes_ai.js'), 'w', encoding='utf-8').write('window.RV_PIPES = ' + json.dumps(pipes, ensure_ascii=False) + ';\n')
    # 후보 (관 A 둘레 s, t) — id, 종류, 꼴, 점, 높이, 신뢰도, 본 것, 상태, 사진
    rect = lambda s0, s1, t0, t1: [(s0, t0), (s1, t0), (s1, t1), (s0, t1)]   # noqa: E731
    F = [
        ('c1', 'curb', 'line', [(10, 0.6), (30, 0.6)], 0.15, 0.9, ['ortho', 'dsm', '360'], 'ai', None, A),
        ('c2', 'curb', 'line', [(10, 0.9), (30, 0.9)], 0.15, 0.4, ['ortho'], 'ai', None, A),
        ('c3', 'curb', 'line', [(50, -0.6), (70, -0.6)], 0.15, 0.7, ['ortho', '360'], 'ai', None, A),
        ('w1', 'wall', 'line', [(40, -5), (40, 5)], 2.0, 0.55, ['dsm'], 'ai', None, A),
        ('sw1', 'sidewalk', 'area', rect(5, 25, 0.2, 4), None, 0.8, ['ortho', '360'], 'ai', None, A),
        ('as1', 'asphalt', 'area', [(30, -3), (45, -3), (45, 3), (40, 3), (40, 0), (30, 0)], None, 0.65, ['ortho', 'dsm', '360'], 'ai', None, A),
        ('as2', 'asphalt', 'area', rect(20, 22, -2, 2), None, 0.3, ['ortho'], 'no', None, A),
        ('co1', 'concrete', 'area', rect(10, 20, 3, 6), None, 0.95, ['ortho', '360'], 'ai', None, A),
        ('un1', 'unpaved', 'area', rect(10, 20, -1.5, 1.5), None, 0.5, ['ortho', '360'], 'ai', 'P0001', Bf),
        ('ok1', 'sidewalk', 'area', rect(45, 55, -4, -0.5), None, 0.85, ['ortho', '360'], 'ok', None, A),
    ]
    feats = []
    for fid, cls, kind, st, h, conf, src, status, photo, fr in F:
        q = [[round(fr(s, t)[0] + TX - AI_OFF[0], 4), round(fr(s, t)[1] + TY - AI_OFF[1], 4), None] for s, t in st]
        feats.append({'id': fid, 'cls': cls, 'kind': kind, 'pts': q, 'height': h, 'conf': conf, 'src': src,
                      'attrs': {'state': '균열' if fid == 'as1' else '모름', 'material': '', 'photo': photo}, 'status': status, 'note': ''})
    ai = {'version': 1, 'made': '시험', 'crs': 'EPSG:5186', 'origin': [org[0] + AI_OFF[0], org[1] + AI_OFF[1], org[2]], 'src360': 'D:/시험/360_구간',
          'classes': {'curb': '연석', 'wall': '석축', 'sidewalk': '보도블록', 'asphalt': '아스팔트', 'concrete': '콘크리트', 'unpaved': '비포장'}, 'features': feats}
    txt = 'window.RV_AI = ' + json.dumps(ai, ensure_ascii=False) + ';\n'
    open(os.path.join(out, 'data', 'ai_budae_test.js'), 'w', encoding='utf-8').write(txt)
    rp = os.path.join(out, 'data', 'ai_budae_run.js')
    if os.path.exists(rp):
        os.remove(rp)
    return ai, txt, (A, Bf, TX, TY)


def prep(out):
    os.makedirs(os.path.join(out, 'viewer'), exist_ok=True)
    for fn in ('map3d.js', 'river.js', 'cost.js'):     # cost.js = 개략공사비 셈 (1127)
        shutil.copy2(os.path.join(REPO, 'viewer', fn), os.path.join(out, 'viewer', fn))
    shutil.copy2(os.path.join(HERE, 'hostmock.js'), os.path.join(out, 'viewer', 'hostmock.js'))
    open(os.path.join(out, 'viewer', 'hostmock_ai.js'), 'w', encoding='utf-8').write(MOCK_JS)
    html = open(os.path.join(REPO, 'viewer', 'map3d.html'), encoding='utf-8').read().replace('data/pipes.js', 'data/pipes_ai.js')
    host = lambda h: h.replace('<script src="viewer/map3d.js"></script>',   # noqa: E731
                               '<script src="viewer/hostmock.js"></script>\n<script src="viewer/hostmock_ai.js"></script>\n<script src="viewer/map3d.js"></script>')
    test = html.replace('data/ai_budae.js', 'data/ai_budae_test.js')
    run = html.replace('data/ai_budae.js', 'data/ai_budae_run.js')
    for name, h in (('index_ai.html', test), ('index_ai_host.html', host(test)), ('index_ai_run_host.html', host(run))):
        open(os.path.join(out, name), 'w', encoding='utf-8').write(h)


def page(ed, url):
    ok = setup_page(ed, url)
    ed.js(HELP_JS)
    return ok


OUTZ = {'co1'}                   # 임시점용 영역 밖 (관 A 옆 3 ~ 6 m · 반폭 약 1.03 m) = 안 보임 (JACK 1127 «임시점용 영역에 걸렸을 경우만»)


def want_order(ai):
    """손으로 정한 차례: 미확인 → (360 을 같이 봤으면) 360 다름 → 신뢰도 낮은 것 → 파일 차례 · 맞음 · 아님 은 뒤 · 영역 밖은 뺌."""
    rk = {'ai': 0, 'ok': 1, 'no': 2}
    fs = [p for p in enumerate(ai['features']) if p[1]['id'] not in OUTZ]
    key = lambda p: (rk[p[1]['status']], 0 if '360' not in p[1]['src'] else 1, p[1]['conf'], p[0])   # noqa: E731
    return [f['id'] for _, f in sorted(fs, key=key)]


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
    ai, ai_txt, (A, Bf, TX, TY) = make_data(out)
    srv, base = serve(out)
    ed = Edge(os.path.join(SD, '_prof'), WIN, gl=False).__enter__()
    inp = ce.Inp(ed)
    try:
        print('== 열기 (프로그램 흉내 · 가짜 AI 결과)')
        report('열림', page(ed, base + 'index_ai_host.html'))
        ed.js('localStorage.clear()')
        page(ed, base + 'index_ai_host.html')
        st = J(ed, '__map3d.ai.st()')
        report('AI 결과 읽힘 (후보 10)', st['has'] and st['n'] == 10, str(st['n']))
        f1 = J(ed, "__map3d.ai.feat('c1')")
        x, y = A(10, 0.6)
        report('origin 차이만큼 옮김 (pts + (origin − 매핑 원점))', f1 and near(f1['c'][0], (x + A(30, 0.6)[0]) / 2 + TX, 1e-3) and near(f1['c'][1], (y + A(30, 0.6)[1]) / 2 + TY, 1e-3) and near(f1['meas'], 20, 1e-3),
               str(f1 and [round(v, 3) for v in f1['c'][:2]]))
        errs(ed, '열기')

        print('== k 카테고리 · 종류 단추')
        cat_open(ed, inp, 'hd_ai')
        kd = J(ed, "[...document.querySelectorAll('#aiKinds .tg')].map((b) => [b.dataset.k, Number(b.querySelector('.n').textContent), b.classList.contains('on')])")
        wantn = {'curb': 3, 'wall': 1, 'sidewalk': 2, 'asphalt': 1, 'concrete': 0, 'unpaved': 1}   # 콘크리트 co1 = 영역 밖
        report('k 종류 단추 여섯 · 개수 (아님 뺌) · 모두 켜짐', len(kd) == 6 and all(n == wantn[k] and on for k, n, on in kd), str(kd))
        report('k 머리줄 «미확인 7» (영역 밖 co1 뺌)', J(ed, "document.getElementById('aiTag').textContent") == '미확인 7')
        zs = J(ed, '__map3d.ai.zone()')
        report('z 임시점용 영역 안 = 9 · 밖 (co1) 숨김 · 목록에도 없음', sorted(zs) == sorted(f['id'] for f in ai['features'] if f['id'] not in OUTZ) and
               J(ed, "!__ai.row('co1')"), str(sorted(zs)))
        report('k 프로그램 = [AI 인식] · [360 고르기] 보임 · «AI 인식 안 함» 숨김',
               J(ed, "!document.getElementById('aiRun').hidden && !document.getElementById('ai360').hidden && document.getElementById('aiNone').hidden"))
        ed.js('__t.look(0, 20, 34, 89.5, 0)')
        ed.frames(4)
        img_on = shot(ed, SD, '01_카테고리_위에서.png')
        cx, cy = A(20, 0.6)
        pc = J(ed, '__ai.wscr(%f, %f)' % (cx + TX, cy + TY))
        ui_click(ed, inp, 'aiK_curb')
        ed.frames(4)
        img_off = shot(ed, SD, '02_연석_끔.png')
        b1 = img_on[int(pc[1]) - 3:int(pc[1]) + 4, int(pc[0]) - 3:int(pc[0]) + 4]
        b2 = img_off[int(pc[1]) - 3:int(pc[1]) + 4, int(pc[0]) - 3:int(pc[0]) + 4]
        dif = np.abs(b1 - b2).sum(2)
        report('k 연석 끄기 → 연석 자리 화소 바뀜', (dif > 40).mean() > 0.3, '%.0f %%' % (100 * (dif > 40).mean()))
        od = J(ed, '__ai.order()')
        report('k 연석 끄기 → 목록에서 빠짐', not any(i.startswith('c') and i[1:].isdigit() for i in od) and len(od) == 6, str(od))
        ui_click(ed, inp, 'aiK_curb')
        ed.frames(2)
        report('k 다시 켜기 → 9 줄', len(J(ed, '__ai.order()')) == 9 and J(ed, '__map3d.ai.st().vis.curb') is True)
        errs(ed, '종류')

        print('== o 목록 차례 · 고르기')
        od = J(ed, '__ai.order()')
        wo = want_order(ai)
        report('o 차례 = 미확인 → 360 다름 → 신뢰도 낮은 것 → 맞음 → 아님', od == wo, str(od))
        tags = J(ed, "[...document.querySelectorAll('#aiList .aiRow')].map((r) => [r.dataset.id, [...r.querySelectorAll('.tag')].map((t) => t.textContent).join('/')])")
        tg = dict(tags)
        report('o 상태 표 (미확인 · 맞음 · 아님 · 360 다름)', tg.get('c2') == '360 다름/미확인' and tg.get('c1') == '미확인' and tg.get('ok1') == '맞음' and tg.get('as2') == '아님', str(tags[:4]))
        report('o 줄 글 (종류 · 길이 · 신뢰도 · 상태)', '석축 · 10.0 m · 높이 2.0 · 55%' in J(ed, "__ai.row('w1').querySelector('.aiGo').textContent") and
               '아스팔트' in J(ed, "__ai.row('as1').querySelector('.aiGo').textContent") and '균열' in J(ed, "__ai.row('as1').querySelector('.aiGo').textContent"))
        report('o http 로 연 쪽 = 360 사진 단추 없음 (못 엶)', J(ed, "document.querySelectorAll('#aiList .aiPh').length") == 0)
        xy = J(ed, "__ai.rect('w1', '.aiGo')")
        inp.click(*xy)
        ed.frames(4)
        fw = J(ed, "__map3d.ai.feat('w1')")
        tg = J(ed, '__map3d.V.tgt')
        report('o 줄 누르기 → 고름 · 그 자리 가운데', J(ed, '__map3d.ai.st().sel') == 'w1' and near(tg[0], fw['c'][0], 1e-6) and near(tg[1], fw['c'][1], 1e-6) and
               J(ed, "!!document.querySelector('#aiList .aiRow.sel[data-id=w1]')"))
        report('o 석축 높이 면 그물 (선분 하나 · 양면 = 세모 4)', J(ed, '__map3d.ai.tris()') == 4, str(J(ed, '__map3d.ai.tris()')))
        ed.js('__map3d.V.d = 26; __map3d.V.el = 30; __map3d.V.az = 200; __map3d.redraw()')
        shot(ed, SD, '03_석축_고름.png')
        errs(ed, '고르기')

        print('== q 수량 «부대공» (손셈)')
        tr = J(ed, '__map3d.tr()')
        cover = J(ed, '__map3d.S.cover')
        wv = lambda D: D / 2 + tr['margin'] + tr['n'] * max(0, cover + D + tr['bed'] - tr['tp'])   # noqa: E731
        wa, wb, ext = wv(0.5), wv(0.3), max(tr['margin'], 0.05)
        print('    반폭 A %.3f · B %.3f · 끝 여유 %.2f' % (wa, wb, ext))
        hand_a = {'bd_curb': 20 + 20 + (LA + ext - 50), 'bd_wall': 2 * wa, 'bd_wallA': 2 * wa * 2.0, 'bd_sidewalk': 20 * (wa - 0.2) + 10 * (wa - 0.5),
                  'bd_asphalt': 10 * wa + 5 * 2 * wa, 'bd_concrete': 0, 'bd_unpaved': 0}
        hand_b = {k: 0 for k in hand_a}
        hand_b['bd_unpaved'] = 10 * 2 * wb
        ed.js('__t.look(0, 30, 60, 60, 0)')
        ed.frames(3)
        ui_click(ed, inp, 'qkBudae')
        ed.frames(3)
        rows = J(ed, '__ai.rows()')
        report('q 부대공 갈래 = 그 줄만 (연석 · 석축 m · m² · 보도블록 · 아스팔트 · 콘크리트 · 비포장)', rows == list(hand_a.keys()), str(rows))
        got_a = {k: J(ed, "__ai.q('%s')" % k) for k in hand_a}
        got_t = {k: J(ed, "__ai.q('%s', true)" % k) for k in hand_a}
        report('q 이 관 (A) = 손셈', all(near(got_a[k], hand_a[k], 1e-3) for k in hand_a), str({k: round(v, 3) for k, v in got_a.items()}))
        report('q 전체 = A + B 손셈', all(near(got_t[k], hand_a[k] + hand_b[k], 1e-3) for k in hand_a), str({k: round(v, 3) for k, v in got_t.items()}))
        note = J(ed, "document.getElementById('qNote').textContent")
        report('q «확인 안 함 7개 포함» 표시', '확인 안 함 7개 포함' in note, note)
        report('q 수량 전체 갈래에도 부대공 줄 (AI 결과 있을 때)', (ui_click(ed, inp, 'tgQty') or True) and all(k in J(ed, '__ai.rows()') for k in hand_a))
        ui_click(ed, inp, 'qkBudae')   # AI 칸 [부대공 수량] 단추는 뺌 (JACK 1127 «개략공사비에 부대공이 있으니 AI 구조물에서는 빼») → 개략공사비 «부대공» 갈래
        ed.frames(2)
        if J(ed, "document.getElementById('qtyBox').hidden"):
            ui_click(ed, inp, 'qkBudae')
            ed.frames(2)
        report('q AI 칸 [부대공 수량] 숨김 · 개략공사비 «부대공» 갈래 → 부대공 표', J(ed, "document.getElementById('aiQty').hidden && !document.getElementById('qtyBox').hidden && document.getElementById('qtyBox').dataset.view === 'budae'"))
        shot(ed, SD, '04_수량_부대공.png')
        errs(ed, '수량')

        print('== m 맞음 · 아님 · 종류 바꿈 → ai_save')
        cat_open(ed, inp, 'hd_ai')
        od0 = J(ed, '__ai.order()')
        n0 = len(J(ed, '__ai.saves()'))
        inp.click(*J(ed, "__ai.rect('c2', '.aiNo')"))
        ed.frames(2)
        inp.click(*J(ed, "__ai.rect('as2', '.aiNo')"))
        ed.frames(2)
        inp.click(*J(ed, "__ai.rect('c1', '.aiOk')"))
        ed.frames(2)
        ed.js("__ai.rect('w1', 'select')")
        report('m 종류 바꿈 = 같은 꼴만 (선: 연석 · 석축)', J(ed, "[...__ai.row('w1').querySelectorAll('select option')].map((o) => o.value)") == ['curb', 'wall'])
        ed.js("__ai.setSel('w1', 'curb')")
        ed.frames(2)
        report('m 누르는 동안 차례 그대로', J(ed, '__ai.order()') == od0)
        st = {i: J(ed, "__map3d.ai.feat('%s')" % i) for i in ('c1', 'c2', 'as2', 'w1')}
        report('m 상태 (c1 맞음 · c2 아님 · as2 아님 → 미확인 · w1 → 연석 맞음)', st['c1']['st'] == 'ok' and st['c2']['st'] == 'no' and st['as2']['st'] == 'ai' and st['w1']['cls'] == 'curb' and st['w1']['st'] == 'ok',
               str({k: (v['cls'], v['st']) for k, v in st.items()}))
        time.sleep(1.2)
        held = len(J(ed, '__ai.saves()')) == n0                         # JACK 1127: 저절로 안 씀 · 윗줄 [저장] 을 눌러야
        ed.js("document.getElementById('bSave').click()")
        ok = ed.wait_js('__host.calls.filter((c) => c.name === "ai_save").length > %d' % n0, 5)
        ed.frames(10)
        sv = J(ed, '__ai.saves()')
        last = sv[-1]['data'] if sv else None
        fs = {f['id']: f for f in (last or {}).get('features', [])}
        report('m ai_save = [저장] 에 한 번 (그 전엔 안 씀)', held and ok and len(sv) - n0 == 1, '%d 번' % (len(sv) - n0))
        report('m ai_save 자료 = 고친 것 (상태 · 종류 · 나머지 그대로)', bool(fs) and fs['c1']['status'] == 'ok' and fs['c2']['status'] == 'no' and fs['as2']['status'] == 'ai' and
               fs['w1']['cls'] == 'curb' and fs['w1']['status'] == 'ok' and fs['sw1']['status'] == 'ai' and last['version'] == 1 and len(fs) == 10 and
               fs['w1']['pts'] == ai['features'][3]['pts'], str({k: (fs[k]['cls'], fs[k]['status']) for k in ('c1', 'c2', 'as2', 'w1')} if fs else None))
        report('m 저장 글 «저장됨»', J(ed, "document.getElementById('aiSave').textContent") == '저장됨')
        ui_click(ed, inp, 'qkBudae')
        if J(ed, "document.getElementById('qtyBox').hidden"):
            ui_click(ed, inp, 'qkBudae')
        ed.js('__t.look(0, 30, 60, 60, 0)')
        ed.frames(3)
        got2 = {k: J(ed, "__ai.q('%s')" % k) for k in hand_a}
        want2 = dict(hand_a)
        want2['bd_asphalt'] += 2 * 2 * wa          # as2 아님 → 미확인 = 다시 셈
        want2['bd_curb'] += 2 * wa - 20            # w1 석축 → 연석 · c2 아님 → 뺌
        want2['bd_wall'] = 0
        want2['bd_wallA'] = 0
        report('m 수량 바로 바뀜 (as2 들어감 · w1 연석으로 · c2 빠짐)', all(near(got2[k], want2[k], 1e-3) for k in want2), str({k: round(v, 3) for k, v in got2.items()}))
        report('m 석축 → 연석 = 높이 면 그물 없음', J(ed, '__map3d.ai.tris()') == 0)
        shot(ed, SD, '05_확인뒤_수량.png')
        cat_open(ed, inp, 'hd_ai', fresh=True)
        ai2 = json.loads(json.dumps(ai))
        for f in ai2['features']:
            if f['id'] in fs:
                f['status'], f['cls'] = fs[f['id']]['status'], fs[f['id']]['cls']
                f['src'] = fs[f['id']]['src']
        report('m 다시 펴면 새 차례 (맞음 · 아님 아래로)', J(ed, '__ai.order()') == want_order(ai2), str(J(ed, '__ai.order()')))
        report('m 머리줄 «미확인 5»', J(ed, "document.getElementById('aiTag').textContent") == '미확인 5')
        inp.key('Escape', 'Escape', 27)
        ed.frames(2)
        report('m Esc → 고른 것 풀기', J(ed, '__map3d.ai.st().sel') is None)
        errs(ed, '확인')

        print('== z 관로 없음 = AI 후보 안 보임 (JACK 1127 «관로는 불러왔을 경우만»)')
        ed.js('window.__pz = window.RV_PIPES; window.RV_PIPES = { layers: window.__pz.layers, origin: window.__pz.origin, lines: [] }; __map3d.rebuild()')
        ed.frames(3)
        cat_open(ed, inp, 'hd_ai', fresh=True)
        report('z 관로 없음 → 목록 · 종류 · 머리줄 비고 «관로 없음 …» 안내',
               J(ed, "!document.querySelector('#aiList .aiRow') && document.getElementById('aiKinds').hidden && document.getElementById('aiTag').textContent === '' && "
                     "!document.getElementById('aiNone').hidden && document.getElementById('aiNone').textContent.indexOf('관로 없음') === 0"),
               J(ed, "document.getElementById('aiNone').textContent"))
        ui_click(ed, inp, 'aiRun')
        ed.frames(2)
        report('z 관로 없음 → [AI 인식] = 안내만 (ai_run 안 부름)', '관로 없음' in J(ed, "document.getElementById('toast').textContent") and
               not any(c['name'] == 'ai_status' for c in J(ed, '__host.calls')[-3:]), J(ed, "document.getElementById('toast').textContent"))
        ed.js('window.RV_PIPES = window.__pz; __map3d.rebuild()')
        ed.frames(3)
        report('z 관로 다시 → 9 줄', len(J(ed, '__ai.order()')) == 9 or (cat_open(ed, inp, 'hd_ai', fresh=True) or True) and len(J(ed, '__ai.order()')) == 9)
        errs(ed, '관로 없음')

        print('== r AI 인식 (결과 없는 프로젝트)')
        page(ed, base + 'index_ai_run_host.html')
        cat_open(ed, inp, 'hd_ai')
        report('r «AI 인식 안 함» + [AI 인식] · 목록 · 종류 없음', J(ed, "!document.getElementById('aiNone').hidden && document.getElementById('aiNone').textContent === 'AI 인식 안 함' && "
                                                           "!document.getElementById('aiRun').hidden && document.getElementById('aiKinds').hidden && !document.querySelector('#aiList .aiRow')"))
        ui_click(ed, inp, 'qkBudae')
        report('r 수량 부대공 = «AI 인식 안 함»', J(ed, "document.getElementById('qNote').textContent") == 'AI 인식 안 함')
        ui_click(ed, inp, 'tgQty')
        report('r 수량 전체 = 부대공 줄 숨김 (결과 없음)', not any(k.startswith('bd_') for k in J(ed, '__ai.rows()')))
        ui_click(ed, inp, 'tgQty')
        cat_open(ed, inp, 'hd_ai')
        ui_click(ed, inp, 'ai360')
        ed.wait_js("!document.getElementById('ai360Row').hidden", 5)
        report('r 360 고르기 → 이름 · [빼기]', J(ed, "document.getElementById('ai360Name').textContent") == '360 · 360_구간' and J(ed, "document.getElementById('ai360').classList.contains('on')"))
        ui_click(ed, inp, 'aiRun')
        ok = ed.wait_js("document.getElementById('toast').classList.contains('show') && !!document.querySelector('#toast button')", 5)
        report('r 엔진 없음 → «받기 (약 6 GB)» 묻기', ok and '6 GB' in J(ed, "document.getElementById('toast').textContent"), J(ed, "document.getElementById('toast').textContent"))
        shot(ed, SD, '06_엔진받기_묻기.png')
        ed.js("document.querySelector('#toast button').click()")
        ok = ed.wait_js("__host.calls.some((c) => c.name === 'ai_run')", 10)
        cl = [c['name'] for c in J(ed, '__host.calls') if c['name'].startswith('ai_')]
        run = [c['arg'] for c in J(ed, '__host.calls') if c['name'] == 'ai_run']
        report('r ai_status → ai_setup (일 끝) → ai_run', ok and cl[:3] == ['ai_status', 'ai_setup', 'ai_run'], str(cl))
        report('r ai_run 에 360 넘김', bool(run) and run[0].get('project360') == 'D:\\시험\\360_구간' and 'project' in run[0], str(run and {k: run[0][k] for k in ('project', 'project360')}))
        zp = (run and run[0].get('zone')) or []
        org_ = J(ed, '__map3d.T()')
        okz = bool(zp) and all(len(p) == 4 for p in zp) and all(abs(q[0]) > 1000 and abs(q[1]) > 1000 for p in zp for q in p)
        report('r ai_run 에 임시점용 영역 (도면 좌표 네모들) 넘김', okz, '%d 조각 · 첫 점 %s' % (len(zp), zp[0][0] if zp else None))
        ed.wait_js("document.getElementById('aiLine').textContent.indexOf('50%') >= 0", 5)
        report('r 진행 막대 (이 카테고리 · AI 인식 중)', J(ed, "!document.getElementById('aiProg').hidden && document.getElementById('aiProg').dataset.st === 'run' && document.getElementById('aiRun').disabled"))
        shot(ed, SD, '07_AI인식_진행.png')
        open(os.path.join(out, 'data', 'ai_budae_run.js'), 'w', encoding='utf-8').write(ai_txt)
        ed.js('__host.arDone = true')
        ok = ed.wait_js('__map3d.ai.st().n === 10', 10)
        ed.frames(4)
        report('r 끝 → 결과 다시 읽음 · 목록 9 (영역 안) · 막대 끝', ok and len(J(ed, '__ai.order()')) == 9 and J(ed, "document.getElementById('aiProg').dataset.st") == 'done' and
               J(ed, "document.querySelector('#aiRun span').textContent") == 'AI 다시 인식')
        ed.js('__t.look(0, 30, 75, 55, 20)')
        shot(ed, SD, '08_AI인식_끝.png')
        ed.js('__host.aiEngine = true')
        inp.click(*J(ed, "__ai.rect('c1', '.aiOk')"))
        ed.frames(2)
        ui_click(ed, inp, 'aiRun')
        ok = ed.wait_js("document.getElementById('toast').classList.contains('show') && document.getElementById('toast').textContent.indexOf('확인한 것') >= 0", 5)
        report('r 확인한 것 있음 → 다시 인식 묻기', ok, J(ed, "document.getElementById('toast').textContent"))
        errs(ed, 'AI 인식')
        os.remove(os.path.join(out, 'data', 'ai_budae_run.js'))

        print('== b 브라우저 (웹 공유 · 보기만)')
        page(ed, base + 'index_ai.html')
        report('b 프로그램 없음', J(ed, '!window.pywebview'))
        cat_open(ed, inp, 'hd_ai')
        report('b [AI 인식] · [360 고르기] 숨김 · 목록 9 · 맞음/아님 단추 없음',
               J(ed, "document.getElementById('aiRun').hidden && document.getElementById('ai360').hidden && document.querySelectorAll('#aiList .aiRow').length === 9 && "
                     "!document.querySelector('#aiList .aiL2') && !document.querySelector('#aiList .aiOk')"))
        ui_click(ed, inp, 'aiK_unpaved')
        ed.frames(2)
        report('b 종류 단추 (비포장 끔 → 8 줄)', len(J(ed, '__ai.order()')) == 8)
        ui_click(ed, inp, 'aiK_unpaved')
        ed.js('__t.look(0, 30, 60, 60, 0)')
        ed.frames(3)
        ui_click(ed, inp, 'qkBudae')
        ed.frames(3)
        got_b = {k: J(ed, "__ai.q('%s')" % k) for k in hand_a}
        report('b 수량 부대공 = 손셈 (같은 자료)', all(near(got_b[k], hand_a[k], 1e-3) for k in hand_a))
        inp.click(*J(ed, "__ai.rect('sw1', '.aiGo')"))
        ed.frames(4)
        report('b 줄 누르기 → 고름 (보기는 됨)', J(ed, '__map3d.ai.st().sel') == 'sw1')
        shot(ed, SD, '09_브라우저.png')
        errs(ed, '브라우저')

        print('== p 360 사진 (file: 로 연 쪽 · 가짜 360 폴더)')
        d360 = os.path.join(SD, '_360', 'data', 'pano')
        os.makedirs(d360, exist_ok=True)
        buf = io.BytesIO()
        Image.new('RGB', (64, 32), (200, 40, 160)).save(buf, 'PNG')
        uri = 'data:image/png;base64,' + base64.b64encode(buf.getvalue()).decode()
        open(os.path.join(d360, 'P0001.js'), 'w', encoding='utf-8').write('RV_PANO_LOADED("P0001", "%s");\n' % uri)
        ai3 = json.loads(json.dumps(ai))
        ai3['src360'] = os.path.join(SD, '_360')
        ai3['features'][0]['attrs']['photo'] = 'P9999'                  # 없는 사진 → 못 엶 → 사진 단추 숨김
        open(os.path.join(out, 'data', 'ai_budae_test.js'), 'w', encoding='utf-8').write('window.RV_AI = ' + json.dumps(ai3, ensure_ascii=False) + ';\n')
        furl = 'file:///' + os.path.join(out, 'index_ai_host.html').replace('\\', '/')
        ed.goto(furl)
        okf = ed.wait_js(Edge.NEW + ' && window.__map3d && window.__map3d.ai', 30)
        ed.js(HELP_JS)
        ed.js("(() => { if (document.getElementById('side').hidden) document.getElementById('tgSide').click(); if (!__map3d.catIs('ai')) __map3d.catSet('ai', true); })()")
        ed.frames(3)
        nph = J(ed, "document.querySelectorAll('#aiList .aiPh').length")
        report('p file: 로 열면 사진 단추 (사진 이름 있는 줄 둘)', okf and nph == 2, str(nph))
        ed.js("__ai.row('un1').querySelector('.aiPh').click()")
        ok = ed.wait_js("!document.getElementById('phBox').hidden && (document.getElementById('phImg').getAttribute('src') || '').indexOf('data:image/png') === 0 && document.getElementById('phMsg').textContent === ''", 10)
        report('p [사진] → 원본 사진 창에 360 사진 (data/pano/<이름>.js)', ok, J(ed, "document.getElementById('phName').textContent + ' · ' + document.getElementById('phMsg').textContent"))
        shot(ed, SD, '10_360사진.png')
        ed.js("document.getElementById('phX').click()")
        ed.js("__ai.row('c1').querySelector('.aiPh').click()")
        ok = ed.wait_js("document.getElementById('phMsg').textContent === '360 사진 못 엶'", 10)
        ed.frames(3)
        report('p 없는 사진 → «360 사진 못 엶» · 사진 단추 숨김', ok and J(ed, "document.querySelectorAll('#aiList .aiPh').length") == 0)
        # 엔진 결과의 src360 = 360 프로젝트 «이름» → 고른 360 폴더 (끝 이름이 같을 때) 로 엶
        ai3['src360'] = '_360'
        ai3['features'][0]['attrs']['photo'] = None
        open(os.path.join(out, 'data', 'ai_budae_test.js'), 'w', encoding='utf-8').write('window.RV_AI = ' + json.dumps(ai3, ensure_ascii=False) + ';\n')
        ed.js("localStorage.setItem('rv_ai360:' + (window.RV_MAPPING.title || 'project') + ':3d', JSON.stringify(%s))" % json.dumps(os.path.join(SD, '_360')))
        ed.goto(furl)
        ed.wait_js(Edge.NEW + ' && window.__map3d && window.__map3d.ai', 30)
        ed.js(HELP_JS)
        ed.js("(() => { if (document.getElementById('side').hidden) document.getElementById('tgSide').click(); if (!__map3d.catIs('ai')) __map3d.catSet('ai', true); })()")
        ed.frames(3)
        ed.js("__ai.row('un1') && __ai.row('un1').querySelector('.aiPh') && __ai.row('un1').querySelector('.aiPh').click()")
        ok = ed.wait_js("!document.getElementById('phBox').hidden && (document.getElementById('phImg').getAttribute('src') || '').indexOf('data:image/png') === 0", 10)
        report('p src360 = 이름 → 고른 360 폴더 (같은 이름) 사진', ok, J(ed, '__map3d.ai.st().p360'))
        ed.exceptions.clear()
        ed.console.clear()                                             # file: 쪽은 3D 모델 (fetch) 을 못 읽음 — 사진만 봄
        open(os.path.join(out, 'data', 'ai_budae_test.js'), 'w', encoding='utf-8').write(ai_txt)
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
