"""3D 매핑 보기 · 오른쪽 옵션 칸 배치 시험 (JACK 1126 «윗줄 단추 → 옆 옵션 창 · 접이식 카테고리 · 웹 공유 따로 · 수량 · 간섭 갈래 단추»).
결과 폴더를 작은 HTTP 서버로 열고 화면 없는 Edge (cdp.py) 를 진짜 마우스 · 키로 몬다. 그림은 --shots 폴더에 남김.

    python -X utf8 -I check_map3d_ui.py --out <3D 매핑 결과 폴더 (check_map3d_ug 와 같은 것)> --shots <그림 폴더>

  t  윗줄 = 처음 · 이름 · 조작법 · 오른쪽 칸 만 · 조작법 열고 닫기 · 오른쪽 칸 접기
  c  카테고리: 처음 = 보기 · 관로 · 터파기 펴짐 · 머리줄 누르기 = 펴고 접기 · 다시 열어도 기억
  v  보기: 위에서 · 처음 시점 · 북쪽 · 투시 · 그림 저장 · 레이어 (관로 · 이름표 · 지장물)
  p  관로: SHP 불러오기 창 · 직접 그리기 (새 관 → 창) · 고치기 · SHP 내보내기 · 지장물 가져오기 창
  r  터파기 · 점용: 영역 · 터파기 · 단면 창 · 점용 · 터파기 형상 · 기본값
  h  가시설: 머리줄 = 칸 · 형식 고르기 · Esc
  q  수량: 갈래 단추마다 표 줄 · 같은 단추 = 닫기 · 가시설 연장 = 구간 합
  i  간섭: 수 · 머리줄 이름표 · 지장물 종류 단추 → 목록 거르기
  m  재기 · 검사: 거리 · 면적 · 높이 차 · 지형 단면 (점 찍기 → 결과) · 검사점 (파일 고르기 → 칸)
  o  결과물: 머리줄 → 잘림 상태 읽기 · 폴더 · 잘라내기 · 땅 아님
  w  웹 공유: 올리기 → 진행 (웹 카테고리 안) → 주소 · 복사 · 열기 · 머리줄 «올림» · 올린 기록 (list_shares · file://) 읽기
  b  브라우저 (프로그램 밖): 프로그램 전용 단추 · 결과물 · 웹 공유 숨김 · 직접 그리기 · 고치기는 보임
  모든 쪽 JS 오류 0
"""
import argparse
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from cdp import Edge  # noqa: E402
import check_edit as ce  # noqa: E402
import check_map3d_ug as ug  # noqa: E402
from check_map3d_ug import J, errs, report, serve, setup_page, shot  # noqa: E402
from check_map3d_ctx import calls, copy_site, empty_spots  # noqa: E402

WIN = (1600, 900)
CATS = ['view', 'pipe', 'trench', 'shore', 'river', 'qty', 'cf', 'meas', 'out', 'web']
HEAD = {'view': 'hd_view', 'pipe': 'hd_pipe', 'trench': 'hd_trench', 'shore': 'tgShore', 'river': 'hd_river', 'qty': 'hd_qty', 'cf': 'hd_cf', 'meas': 'hd_meas', 'out': 'bTrim', 'web': 'hd_web'}


def opened(ed):
    return J(ed, "[...document.querySelectorAll('#side .cat')].filter((c) => !c.hidden && getComputedStyle(c).display !== 'none' && !c.querySelector('.catB').hidden).map((c) => c.dataset.cat)")


def vis(ed, eid):
    return J(ed, "(() => { const e = document.getElementById('%s'); if (!e) return false; const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0 && e.offsetParent !== null; })()" % eid)


def on(ed, eid):
    return J(ed, "document.getElementById('%s').classList.contains('on')" % eid)


# 개략공사비 손셈 (JACK 1127 · viewer/cost.js): 주철관 D500 m당 = 자재비 246,898 × 1.10 + 공사비 (아스팔트 476,740 · 콘크리트 399,122) × 1.50 (2016.01 환경부 표)
CI500 = {'asphalt': 246898 * 1.10 + 476740 * 1.50, 'concrete': 246898 * 1.10 + 399122 * 1.50}
HP, LAG, SP, CURB, WALK = 115883 + 58284, 40212 + 32031, 100112 + 59996, 16723, 11011   # 표준시장단가 (항타 + 항발 · 설치 + 철거) · × 1.50


def cost_check(ed, ui, SD):
    """가시설 갈래가 열린 채 (관 0 에 토류벽 5~14 · 시트파일 20~23.5) 부름 → 끝나면 다시 가시설 갈래."""
    if not J(ed, '!!window.RVCost'):
        report('$ cost.js 읽힘', False)
        return
    close = lambda a, b, t=0.5: abs(a - b) <= t     # noqa: E731
    ck = lambda k: J(ed, "(() => { const e = document.querySelector('#qSum [data-k=%s]'); return e ? Number(e.dataset.v) : NaN; })()" % k)   # noqa: E731
    C = lambda e: J(ed, '__map3d.QT.cost.' + e)    # noqa: E731
    n_hp, a_hp, n_sp = C('rows.sh_hpile.parts[0].q'), C('rows.sh_hpile.parts[2].q'), C('rows.sh_sheet.parts[0].q')
    want_sh = n_hp * HP * 1.5 + a_hp * LAG * 1.5 + n_sp * SP * 1.5
    report('$ 가시설 소계 = 손셈 (H파일 2 × (9 ÷ 1.8 + 1) = 12 본 · 흙막이판 2 × ∫깊이 · 시트파일 2 × ⌈3.5 ÷ 0.4⌉ = 18 본)',
           n_hp == 12 and n_sp == 18 and 2 * 9 * 0.8 < a_hp < 2 * 9 * 6 and close(ck('sub'), want_sh), '소계 %.0f · 손셈 %.0f · 흙막이판 %.2f m²' % (ck('sub'), want_sh, a_hp))
    rows = J(ed, "[...document.querySelectorAll('#qTab .qc')].filter((e) => !e.hidden && getComputedStyle(e).display !== 'none').map((e) => e.id)")
    report('$ 가시설 표 밑줄 = 토류벽 · 시트파일 (간이흙막이 0 m = 숨김)', rows == ['qc_sh_hpile', 'qc_sh_sheet'], str(rows))

    ui('tgQty')
    Ls = J(ed, "__map3d.lines().map((L) => [L.src, L.mat, Math.round(L.dia * 1000), __map3d.rv.lineQty(L.src).tlen])")
    bd = J(ed, "Object.fromEntries(['bd_curb', 'bd_sidewalk', 'bd_wallA'].map((k) => [k, Number(document.getElementById('qt_' + k).dataset.v) || 0]))")
    KD = J(ed, 'RVCost.K_DEF')   # 기본 보정 계수 (2026 · 건설공사비지수 · 1127)
    def want_tot(pave, k=None, hp_in=115883):
        k = KD if k is None else k
        pipe = 0.0
        for src, mat, D, tl in Ls:
            r = CI500[pave] if (mat, D) == ('ci', 500) else J(ed, "RVCost.pipeRate('%s', %d, '%s').rate" % (mat, D, pave))
            pipe += r * k * tl
        ex = n_hp * (HP - 115883 + hp_in) * 1.5 + a_hp * LAG * 1.5 + n_sp * SP * 1.5 + bd['bd_curb'] * CURB * 1.5 + bd['bd_sidewalk'] * WALK * 1.5
        return pipe, pipe + ex
    wp, wt = want_tot('asphalt')
    report('$ 전체 = 관로 본공사 + 부대 공종 = 합계 (손셈 · 주철 D500 아스팔트 m당 %.1f)' % CI500['asphalt'],
           close(ck('pipe'), wp) and close(ck('total'), wt) and close(ck('pipe') + ck('extra'), ck('total')),
           '관로 %.0f (손셈 %.0f) · 합계 %.0f (손셈 %.0f) · 관 %d 개' % (ck('pipe'), wp, ck('total'), wt, len(Ls)))
    ko = J(ed, "document.querySelector('#qSum .ko') ? document.querySelector('#qSum .ko').textContent : ''")
    report('$ 합계 밑 «억 · 만» 글', ('억' in ko or '만' in ko) if wt >= 1e4 else ko == '', ko)
    # 연장 밑줄: m당 단가가 하나 = «단가 …원/m» · 관마다 다름 = «관 N개 · 평균 …원/m» (평균 = 관로 본공사 ÷ Σ 개착 길이)
    ql = J(ed, "document.getElementById('qc_len').firstChild.textContent")
    rates = {round(CI500['asphalt'] * KD) if (m, D) == ('ci', 500) else round(J(ed, "RVCost.pipeRate('%s', %d, 'asphalt').rate" % (m, D)) * KD) for _, m, D, _ in Ls}
    avg = J(ed, "RVCost.won(%r)" % (wp / sum(t for *_, t in Ls)))
    want_l = ('단가 ' if len(rates) == 1 else '관 %d개 · 평균 ' % len(Ls)) + avg + '원/m'
    report('$ 연장 밑줄 = %s · 금액' % ('단가 원/m' if len(rates) == 1 else '관 %d개 · 평균 원/m' % len(Ls)), ql == want_l, '%s · 기대 %s' % (ql, want_l))
    report('$ 터파기 밑줄 = 관로에 포함', J(ed, "document.getElementById('qc_cut').textContent") == '관로 본공사에 포함')
    shot(ed, SD, '30_개략공사비_전체.png')

    # (i) 설명
    ui('qInfo')
    info = J(ed, "document.getElementById('qInfoBox').hidden ? '' : document.getElementById('qInfoBox').textContent")
    report('$ (i) → 단가 출처 설명 (개략산정기준 · 표준시장단가 · × 1.50)', '개략산정기준' in info and '표준시장단가' in info and '제2026-652호' in info and '1.50' in info, info[:60])
    shot(ed, SD, '31_개략공사비_i설명.png')
    ui('qInfo')
    report('$ (i) 다시 → 닫힘', J(ed, "document.getElementById('qInfoBox').hidden"))

    # 단가표 창 · 단가 고치기 → 금액 바뀜 · 기본값
    ui('qPrice')
    dlg_vis = lambda: J(ed, "(() => { const e = document.getElementById('cpDlg'), r = e.getBoundingClientRect(); return !e.hidden && r.width > 0 && r.height > 0; })()")   # noqa: E731  (fixed 창 = offsetParent 없음)
    report('$ [단가표] → 창 (그 밖 공종 %d 줄 · 관로 줄)' % J(ed, 'RVCost.ITEMS.length'),
           dlg_vis() and J(ed, "document.querySelectorAll('#cpBody .cpItems input').length") == J(ed, 'RVCost.ITEMS.length') and J(ed, "document.querySelectorAll('#cpBody .cpPipe .nm').length") >= 1)
    report('$ 공인 단가 없음 = 빈 칸 · «직접 넣기»', J(ed, "document.getElementById('cp_panel').value === '' && document.getElementById('cp_panel').placeholder === '직접 넣기'"))
    shot(ed, SD, '32_단가표_창.png')
    ed.js("(() => { const e = document.getElementById('cp_hp_in'); e.value = '200000'; e.dispatchEvent(new Event('change', { bubbles: true })); })()"); ed.frames(2)
    _, wt2 = want_tot('asphalt', hp_in=200000)
    report('$ H파일 항타 단가 200,000 → 합계 = 손셈 · 고친 칸 굵게', close(ck('total'), wt2) and J(ed, "document.getElementById('cp_hp_in').classList.contains('mod')"), '%.0f · 손셈 %.0f' % (ck('total'), wt2))
    report('$ 고친 값 = 이 PC 에 기억', J(ed, "JSON.parse(localStorage.getItem('rv_cost:v2')).price.hp_in") == 200000)
    # 터파기 설정 창 «포장» = 개략공사비 «포장» 같은 값 (JACK 1127)
    pv = lambda: J(ed, "[document.getElementById('secPave').value, document.getElementById('qPave').value, JSON.parse(localStorage.getItem('rv_cost:v2')).pave]")   # noqa: E731
    ed.js("(() => { const s = document.getElementById('secPave'); s.value = 'concrete'; s.dispatchEvent(new Event('change')); })()"); ed.frames(2)
    a1 = pv()
    ed.js("(() => { const s = document.getElementById('qPave'); s.value = 'asphalt'; s.dispatchEvent(new Event('change')); })()"); ed.frames(2)
    a2 = pv()
    report('$ 터파기 설정 «포장» ↔ 개략공사비 «포장» 같이 바뀜 (기억 포함)', a1 == ['concrete'] * 3 and a2 == ['asphalt'] * 3, json.dumps([a1, a2]))
    ed.js("(() => { const e = document.getElementById('cpK'); e.value = '1.2'; e.dispatchEvent(new Event('change', { bubbles: true })); })()"); ed.frames(2)
    ed.js("(() => { const e = document.getElementById('qPave'); e.value = 'concrete'; e.dispatchEvent(new Event('change', { bubbles: true })); })()"); ed.frames(2)
    wp3, wt3 = want_tot('concrete', k=1.2, hp_in=200000)
    report('$ 보정 계수 1.2 · 포장 콘크리트 → 관로 = 손셈 × 1.2', close(ck('pipe'), wp3) and close(ck('total'), wt3) and J(ed, "document.getElementById('cpPave').value") == 'concrete', '%.0f · 손셈 %.0f' % (ck('pipe'), wp3))
    ui('cpDef'); ed.frames(2)
    ed.js("(() => { const e = document.getElementById('qPave'); e.value = 'asphalt'; e.dispatchEvent(new Event('change', { bubbles: true })); })()"); ed.frames(2)
    report('$ [기본값] → 합계 처음대로', close(ck('total'), wt) and J(ed, "document.getElementById('cp_hp_in').value") == '115883', '%.0f' % ck('total'))
    ui('cpX')
    report('$ × → 창 닫힘', not dlg_vis() and not on(ed, 'qPrice'))
    errs(ed, '개략공사비')
    ui('qkShore')


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
    copy_site(out)
    srv, base = serve(out)
    ed = Edge(os.path.join(SD, '_prof'), WIN, gl=False).__enter__()
    inp = ce.Inp(ed)
    ed.net(block=['*openstreetmap.org*', '*vworld.kr*'])      # 지도 타일은 밖으로 안 나감 (캔버스 오염 없이 박스 색 읽기)
    ui = lambda eid: ug.ui_click(ed, inp, eid)        # noqa: E731
    esc = lambda: inp.key('Escape', 'Escape', 27)      # noqa: E731
    try:
        print('== 열기 (프로그램 흉내)')
        report('열림', setup_page(ed, base + 'index_host.html'))
        ed.js('localStorage.clear(); sessionStorage.clear()')
        report('다시 열림 (기억 지움)', setup_page(ed, base + 'index_host.html'))
        errs(ed, '열기')

        print('== t 윗줄')
        top = J(ed, "[...document.querySelectorAll('#top button')].filter((b) => b.offsetParent !== null).map((b) => b.id)")
        report('t 윗줄 단추 = 처음 · 저장 · 조작법 · 오른쪽 칸', top == ['bHome', 'bSave', 'bHelp', 'tgSide'], str(top))
        report('t 이름', J(ed, "document.getElementById('title').textContent") == J(ed, "window.RV_MAPPING.title || '3D 매핑'"))
        inp.el_click('bHelp')
        report('t 조작법 열림', vis(ed, 'helpPop'))
        shot(ed, SD, '03_조작법.png')
        esc()
        report('t 조작법 Esc 닫힘', not vis(ed, 'helpPop'))
        inp.el_click('tgSide')
        report('t 오른쪽 칸 접기', J(ed, "document.getElementById('side').hidden") and not J(ed, "document.body.classList.contains('sideOn')") and not on(ed, 'tgSide'))
        inp.el_click('tgSide')
        report('t 오른쪽 칸 펴기', not J(ed, "document.getElementById('side').hidden") and on(ed, 'tgSide'))
        errs(ed, '윗줄')

        print('== c 카테고리 펴고 접기 · 기억')
        o0 = opened(ed)
        report('c 처음 = 보기 만 펼침 (JACK 1127)', o0 == ['view'], str(o0))
        shot(ed, SD, '01_처음.png')
        for c in ('pipe', 'trench'):
            ug.cat_open(ed, inp, HEAD[c])
        for c in ('shore', 'river', 'qty', 'cf', 'meas', 'out', 'web'):
            ug.cat_open(ed, inp, HEAD[c])
        o1 = opened(ed)
        report('c 머리줄 누르기 → 모두 펴짐', o1 == CATS, str(o1))
        for c in ('view', 'pipe', 'trench'):
            ed.js("document.getElementById('%s').scrollIntoView({ block: 'nearest' })" % HEAD[c]); ed.frames(1)
            inp.el_click(HEAD[c])
        o2 = opened(ed)
        report('c 머리줄 다시 → 접힘 (aria-expanded)', o2 == ['shore', 'river', 'qty', 'cf', 'meas', 'out', 'web'] and J(ed, "document.getElementById('hd_view').getAttribute('aria-expanded')") == 'false', str(o2))
        setup_page(ed, base + 'index_host.html')
        o3 = opened(ed)
        report('c 다시 열면 보기만 펼침 (기억한 펼침 안 씀 · JACK 1127)', o3 == ['view'], str(o3))
        for c in ('pipe', 'trench'):
            ug.cat_open(ed, inp, HEAD[c])
        report('c 처음 모양으로 (관로 · 터파기 펴 둠 · 아래 시험이 기대)', opened(ed) == ['view', 'pipe', 'trench'])
        errs(ed, '카테고리')

        print('== g 위성 지도 칸 · 폭 · 높이 손잡이')
        mp = lambda: J(ed, 'window.__map3dMap.st()')      # noqa: E731
        rc = lambda eid: ce.rect(ed, eid)                  # noqa: E731
        first = J(ed, "document.getElementById('side').firstElementChild.id")
        report('g 지도 칸 = 오른쪽 칸 맨 위 · 보임', first == 'mapPane' and vis(ed, 'mapPane') and vis(ed, 'map'), first)
        from pyproj import Transformer
        tr = Transformer.from_crs('EPSG:5186', 'EPSG:4326', always_xy=True)
        bb = J(ed, 'window.__map3dMap.bbox')
        worst = 0.0
        for e, n in ((bb[0], bb[1]), (bb[2], bb[3]), (bb[0], bb[3]), (bb[2], bb[1])):
            lo, la = tr.transform(e, n)
            m = J(ed, 'window.__map3dMap.ll(%r, %r)' % (e, n))
            worst = max(worst, abs(m[0] - lo), abs(m[1] - la))
        report('g 격자 → 위경도 = pyproj 와 같음 (오차 %.2e 도 · 1 cm = 1e-7)' % worst, worst < 2e-8, '%.3e' % worst)
        st0 = mp()
        box = st0['box']
        mr = rc('map')
        inside = all(-2 <= q[0] <= mr['width'] + 2 and -2 <= q[1] <= mr['height'] + 2 for q in box)
        px = J(ed, "(() => { const c = document.getElementById('map'), x = c.getContext('2d'), s = c.width / c.getBoundingClientRect().width, b = window.__map3dMap.st().box;"
                   " const d = x.getImageData(Math.round(((b[0][0] + b[1][0]) / 2) * s), Math.round(((b[0][1] + b[1][1]) / 2) * s) - 1, 1, 1).data; return [d[0], d[1], d[2]]; })()")   # 아래 변 가운데 (파랑 테두리)
        report('g 매핑 범위 박스 = bbox 4 구석 (지도 칸 안 · 파랑 테두리)', inside and len(box) == 4 and abs(box[0][0] - box[1][0]) > 10 and px[2] > px[0] + 60, json.dumps([box, px]))
        v0 = st0['view']
        report('g 지금 보는 곳 = 사각 + 가운데 점', bool(v0) and len(v0) == 4 and bool(st0['c']), json.dumps([v0, st0['c']]))
        shot(ed, SD, '40_지도칸.png')
        cr = rc('c')
        inp.drag((cr['width'] * 0.3, cr['height'] * 0.5), (cr['width'] * 0.3 + 160, cr['height'] * 0.5 - 40))
        ed.frames(6)
        st1 = mp()
        report('g 3D 돌리기 → 보는 곳 표시가 바뀜', json.dumps(st1['view']) != json.dumps(v0) or json.dumps(st1['c']) != json.dumps(st0['c']), json.dumps([st1['view'], st1['c']]))
        s0 = st1['c']
        mrr = rc('map')
        inp.click(mrr['left'] + mrr['width'] * 0.25, mrr['top'] + mrr['height'] * 0.3)
        ed.frames(6)
        st2 = mp()
        want = [mrr['width'] * 0.25, mrr['height'] * 0.3]
        report('g 지도 누르기 → 3D 중심이 그 자리 (오차 %.1f px)' % max(abs(st2['c'][0] - want[0]), abs(st2['c'][1] - want[1])), abs(st2['c'][0] - want[0]) < 3 and abs(st2['c'][1] - want[1]) < 3, json.dumps([s0, st2['c'], want]))
        cg = J(ed, '__map3dMap.cg()')   # 누른 자리 땅이 3D 화면 한가운데 (JACK 1127 «누른 부분이 화면 중앙으로»)
        report('g 지도 누르기 → 그 자리 땅이 3D 화면 가운데 (오차 %s px)' % ('%.1f' % max(abs(cg[0] - cg[2]), abs(cg[1] - cg[3])) if cg else '-'),
               bool(cg) and abs(cg[0] - cg[2]) < 4 and abs(cg[1] - cg[3]) < 4, json.dumps(cg))
        # 높이 손잡이
        h0 = rc('mapPane')['height']
        gr = rc('mapGrip')
        inp.drag((gr['left'] + 60, gr['top'] + 4), (gr['left'] + 60, gr['top'] + 4 + 90))
        ed.frames(6)
        h1 = rc('mapPane')['height']
        st3 = mp()
        report('g 높이 손잡이 끌기 → 지도 높이 +90 · 그림 칸 크기 맞춤', abs(h1 - h0 - 90) < 3 and abs(st3['h'] - rc('map')['height']) < 1.5, '%.0f → %.0f · 그림 %.0f' % (h0, h1, st3['h']))
        gr = rc('mapGrip')
        inp.drag((gr['left'] + 60, gr['top'] + 4), (gr['left'] + 60, gr['top'] - 600))
        ed.frames(4)
        report('g 높이 최소 120 px', abs(rc('mapPane')['height'] - 120) < 2, str(rc('mapPane')['height']))
        gr = rc('mapGrip')
        inp.drag((gr['left'] + 60, gr['top'] + 4), (gr['left'] + 60, gr['top'] + 1500))
        ed.frames(4)
        sh = J(ed, "document.getElementById('side').clientHeight")
        report('g 높이 최대 = 옵션 칸의 75 %', abs(rc('mapPane')['height'] - sh * 0.75) < 6, '%.0f / %.0f' % (rc('mapPane')['height'], sh))
        gr = rc('mapGrip')
        inp.drag((gr['left'] + 60, gr['top'] + 4), (gr['left'] + 60, gr['top'] + 4 - 100))
        ed.frames(4)
        hk = rc('mapPane')['height']
        setup_page(ed, base + 'index_host.html')
        report('g 다시 열면 그 높이 기억', abs(rc('mapPane')['height'] - hk) < 2, '%.0f → %.0f' % (hk, rc('mapPane')['height']))
        gr = rc('mapGrip')
        inp.dblclick(gr['left'] + 60, gr['top'] + 4)
        ed.frames(4)
        report('g 두 번 누르기 → 기본 높이 220', abs(rc('mapPane')['height'] - 220) < 2, str(rc('mapPane')['height']))
        # 폭 손잡이
        w0 = rc('side')['width']
        sg = rc('sideGrip')
        inp.drag((sg['left'] + 4, 500), (sg['left'] + 4 - 100, 500))
        ed.frames(6)
        w1 = rc('side')['width']
        report('g 폭 손잡이 끌기 → 옵션 칸 +100', abs(w1 - w0 - 100) < 3, '%.0f → %.0f' % (w0, w1))
        sg = rc('sideGrip')
        inp.drag((sg['left'] + 4, 500), (sg['left'] + 4 + 900, 500))
        ed.frames(4)
        report('g 폭 최소 260 px', abs(rc('side')['width'] - 260) < 2, str(rc('side')['width']))
        sg = rc('sideGrip')
        inp.drag((sg['left'] + 4, 500), (sg['left'] + 4 - 150, 500))
        ed.frames(4)
        wk = rc('side')['width']
        setup_page(ed, base + 'index_host.html')
        report('g 다시 열면 그 폭 기억 · 지도 그림 칸도 맞음', abs(rc('side')['width'] - wk) < 2 and abs(mp()['w'] - rc('map')['width']) < 1.5, '%.0f → %.0f' % (wk, rc('side')['width']))
        shot(ed, SD, '41_폭_높이_기억.png')
        sg = rc('sideGrip')
        inp.dblclick(sg['left'] + 4, 500)
        ed.frames(4)
        report('g 두 번 누르기 → 기본 폭 300', abs(rc('side')['width'] - 300) < 2, str(rc('side')['width']))
        for c in ('pipe', 'trench'):
            ug.cat_open(ed, inp, HEAD[c])      # 아래 시험이 기대하는 처음 모양
        ed.js("document.getElementById('vHome').click()"); ed.frames(4)      # 3D 시점 처음대로 (아래 시험이 화면 자리를 찍음)
        errs(ed, '지도 칸')

        print('== v 보기')
        ug.T_JS and ed.js(ug.T_JS)
        ed.js('__t.look(0, 20, 60, 45, 30)'); ed.frames(2)
        ui('bTop')
        report('v 위에서 → 켜짐', J(ed, '__map3d.V.el') > 80 and on(ed, 'bTop'))
        ui('vHome')
        report('v 처음 시점 → 비스듬히', abs(J(ed, '__map3d.V.el') - 38) < 1e-6 and not on(ed, 'bTop'))
        ed.js('__map3d.V.az = 123; __map3d.redraw()')
        ui('vNorth')
        report('v 북쪽', abs(J(ed, '__map3d.V.az')) < 1e-9)
        x0 = on(ed, 'bXray')
        ui('bXray')
        report('v 투시 켜고 끄기', on(ed, 'bXray') != x0)
        ui('bXray')
        ui('vShot')
        time.sleep(0.3)
        sv = calls(ed, 'save_image')
        report('v 그림 저장 (save_image · png)', len(sv) == 1 and sv[0]['head'].startswith('data:image/png'), str(sv[:1]))
        ui('tgPipes')
        p1 = J(ed, '__map3d.S.pipes')
        ui('tgPipes')
        report('v 레이어 관로 끄고 켜기', p1 is False and J(ed, '__map3d.S.pipes') is True and on(ed, 'tgPipes'))
        l0 = J(ed, '__map3d.SHOW.lbl')
        ui('tgLbl')
        report('v 레이어 이름표', J(ed, '__map3d.SHOW.lbl') != l0 and on(ed, 'tgLbl') != l0)
        ui('tgLbl')
        if vis(ed, 'tgObst'):
            o_ = on(ed, 'tgObst'); ui('tgObst')
            report('v 레이어 지장물', on(ed, 'tgObst') != o_)
            ui('tgObst')
        errs(ed, '보기')

        print('== p 관로')
        ui('bPipes')
        report('p SHP 불러오기 → 관로 넣기 창', not J(ed, "document.getElementById('pipeDlg').hidden") and not J(ed, "document.getElementById('dlgBack').hidden"))
        esc()
        ui('hObst')
        report('p 지장물 가져오기 → 지장물 넣기 창', not J(ed, "document.getElementById('obstDlg').hidden"))
        esc()
        ui('pNew')
        report('p 직접 그리기 → 새 관 그리기 · 단추 켜짐', J(ed, '__map3d.MS.kind') == 'pipe' and on(ed, 'pNew'))
        s0 = round(J(ed, '__t.L(0).len') * 0.3, 1)
        ed.js('__t.look(0, %f, 40, 50, 60)' % s0); ed.frames(3)
        SP = empty_spots(ed)
        for q in SP[:2]:
            inp.click(*q)
        inp.dblclick(*SP[2])
        report('p 점 셋 → 두 번 누르기 → 새 관 창', not J(ed, "document.getElementById('npDlg').hidden") and J(ed, '__map3d.MS.pts.length') >= 2, str(J(ed, '__map3d.MS.pts.length')))
        shot(ed, SD, '05_직접그리기.png')
        esc()
        report('p 창 닫기 → 그리기 끝 · 단추 꺼짐', J(ed, '__map3d.MS.kind') == '' and not on(ed, 'pNew'))
        ui('pNew'); ui('pNew')
        report('p 직접 그리기 다시 누르기 = 취소', J(ed, '__map3d.MS.kind') == '')
        ui('tgEdit')
        report('p 고치기 켜짐', J(ed, '__map3d.ED.on') is True and on(ed, 'tgEdit'))
        ui('tgEdit')
        report('p 고치기 꺼짐', J(ed, '__map3d.ED.on') is False and not on(ed, 'tgEdit'))
        ui('bShpOut')
        time.sleep(0.4)
        report('p SHP 내보내기 (export_pipes_shp)', len(calls(ed, 'export_pipes_shp')) == 1)
        errs(ed, '관로')

        print('== r 터파기 · 점용')
        ui('mDepth')
        report('r 터파기 · 터파기 형상 단추 보임 · 점용 숨김', J(ed, '__map3d.S.mode') == 'trench' and vis(ed, 'tgPit') and not vis(ed, 'lgTmp'))
        t0 = J(ed, '__map3d.tr().on')
        ui('tgPit')
        report('r 터파기 형상 끄고 켜기', J(ed, '__map3d.tr().on') != t0)
        ui('tgPit')
        ui('mSurf')
        report('r 영역 · 점용 보임', J(ed, '__map3d.S.mode') == 'area' and vis(ed, 'lgTmp') and not vis(ed, 'tgPit'))
        tmp0 = J(ed, '__map3d.SHOW.tmp')
        ui('lgTmp')
        report('r 임시점용 끄고 켜기', J(ed, '__map3d.SHOW.tmp') != tmp0)
        ui('lgTmp')
        ui('tgSec')
        report('r 단면 → 터파기 설정 창 · 단추 켜짐', not J(ed, "document.getElementById('secDlg').hidden") and on(ed, 'tgSec'))
        shot(ed, SD, '06_단면창.png')
        ui('tgSec')
        ed.js('__map3d.trSet({ margin: 0.55 })')
        tg1 = J(ed, "document.getElementById('ugTag').textContent")
        ui('bTrDef')
        report('r 기본값 → 가시화 기본값', tg1 == '직접 입력' and J(ed, "document.getElementById('ugTag').textContent") == '가시화 기본값' and abs(J(ed, '__map3d.tr().margin') - 0.55) > 1e-6, tg1)
        errs(ed, '터파기')

        print('== h 가시설')
        ug.cat_open(ed, inp, 'tgShore')
        report('h 머리줄 → 형식 셋', vis(ed, 'shPanel') and vis(ed, 'shHpile') and vis(ed, 'shSheet'))
        ui('shPanel')
        report('h 간이흙막이 고름 → 켜짐 · 안내 줄', J(ed, '__map3d.SH.arm') == 'panel' and on(ed, 'shPanel') and J(ed, "document.getElementById('shChip').classList.contains('show')"))
        esc()
        report('h Esc → 취소', J(ed, '__map3d.SH.arm') is None and not on(ed, 'shPanel'))
        errs(ed, '가시설')

        print('== q 수량')
        L0 = J(ed, '__t.L(0).len')
        ed.js("window.RV_PIPES.lines[0].shore = [{ a: 5, b: 14, t: 'hpile' }, { a: 20, b: 23.5, t: 'sheet' }]; __map3d.rebuild()")
        rows = lambda: J(ed, "Object.keys(__map3d.QT.rowEl).filter((k) => !__map3d.QT.rowEl[k][0].hidden)")    # noqa: E731
        want = {'tgQty': None, 'qkPipe': ['len'], 'qkEarth': ['cut', 'sand', 'fill'], 'qkPave': ['pav'], 'qkOcc': ['tmp', 'perm'], 'qkRiver': ['rv_jack', 'rv_shaft', 'rv_exc', 'rv_hp', 'rv_lag', 'rv_ssp', 'rv_liner', 'rv_open', 'rv_dam', 'rv_bag', 'rv_sheet'],
            'qkShore': ['sh_panel', 'sh_hpile', 'sh_sheet']}
        okq = True
        # 앞 단계에서 수량 표가 열려 있으면 먼저 닫음 («같은 단추 = 닫기» 라 «전체» 를 누르면 닫혀 버림 · 1126)
        ed.js("(() => { const b = document.querySelector('[data-q].on'); if (b && !document.getElementById('qtyBox').hidden) b.click(); })()")
        for b, ks in want.items():
            ui(b)
            r = rows()
            # «전체» = 기본 10줄 전부 + 하천 · 부대공 줄은 그 자료가 있을 때만 (1126 하천 · AI 부대공 더함)
            base10 = ['len', 'cut', 'pav', 'sand', 'fill', 'tmp', 'perm', 'sh_panel', 'sh_hpile', 'sh_sheet']
            ext = [k for k in r if k not in base10]                       # 하천 · 부대공 줄은 자료가 있으면 그 갈래 줄 전부가 함께 (부분만 나오면 틀림)
            rv_all = [k for k in J(ed, "Object.keys(__map3d.QT.rowEl)") if k.startswith('rv_')]
            bd_all = [k for k in J(ed, "Object.keys(__map3d.QT.rowEl)") if k.startswith('bd_')]
            g_rv, g_bd = [k for k in ext if k.startswith('rv_')], [k for k in ext if k.startswith('bd_')]
            extra_ok = len(g_rv) + len(g_bd) == len(ext) and g_rv in ([], rv_all) and g_bd in ([], bd_all)
            good = vis(ed, 'qtyBox') and on(ed, b) and ((set(base10) <= set(r) and extra_ok) if ks is None else r == ks)
            okq &= good
            if not good:
                print('    ', b, r, 'vis', vis(ed, 'qtyBox'), 'on', on(ed, b), 'ext_ok', extra_ok if ks is None else '-')
        report('q 갈래 단추마다 그 줄만 · 단추 켜짐', okq)
        shot(ed, SD, '07_수량_가시설.png')
        sh = J(ed, "[Number(document.getElementById('qt_sh_hpile').dataset.v), Number(document.getElementById('qt_sh_sheet').dataset.v), Number(document.getElementById('qt_sh_panel').dataset.v)]")
        report('q 가시설 연장 = 구간 합 (토류벽 9 · 시트파일 3.5)', abs(sh[0] - 9) < 1e-6 and abs(sh[1] - 3.5) < 1e-6 and sh[2] == 0 and L0 > 24, str(sh))
        cost_check(ed, ui, SD)
        ui('qkShore')
        report('q 같은 단추 → 닫힘', not vis(ed, 'qtyBox') and not on(ed, 'qkShore'))
        ui('qkEarth')
        ui('qX')
        report('q × → 닫힘', not vis(ed, 'qtyBox') and not on(ed, 'qkEarth'))
        ed.js("delete window.RV_PIPES.lines[0].shore; __map3d.rebuild()")
        errs(ed, '수량')

        print('== i 간섭')
        ug.cat_open(ed, inp, 'hd_cf')
        ok = ed.wait_js("__map3d.cf().length > 0 && document.getElementById('qList').children.length === __map3d.cf().length", 5)
        n = J(ed, '__map3d.cf().length')
        report('i 간섭 수 · 목록 · 머리줄 이름표', ok and J(ed, "document.getElementById('qHit').textContent") == '간섭 %d곳' % n and J(ed, "document.getElementById('cfTag').textContent") == '%d곳' % n, '%d 곳' % n)
        kinds = J(ed, "[...document.querySelectorAll('#cfKinds button')].map((b) => [b.dataset.k, Number(b.querySelector('i').textContent)])")
        report('i 지장물 종류 단추 (전체 + 종류)', len(kinds) >= 2 and kinds[0] == ['', n] and sum(c for k, c in kinds[1:]) == n, str(kinds))
        if len(kinds) >= 2:
            k1, c1 = kinds[1]
            ed.js("document.querySelector('#cfKinds button[data-k=\"%s\"]').scrollIntoView({ block: 'nearest' })" % k1)
            r = J(ed, "(() => { const r = document.querySelector('#cfKinds button[data-k=\"%s\"]').getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; })()" % k1)
            inp.click(*r)
            report('i 종류 누르기 → 그 종류만', J(ed, "document.getElementById('qList').children.length") == c1 and J(ed, "document.querySelector('#cfKinds button.on').dataset.k") == k1, '%s %d' % (k1, c1))
            shot(ed, SD, '08_간섭_종류.png')
            ed.js("document.querySelector('#qList button').scrollIntoView({ block: 'nearest' })"); ed.frames(1)
            q0 = J(ed, "(() => { const r = document.querySelector('#qList button').getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; })()")
            t0 = J(ed, '__map3d.V.tgt')
            inp.click(*q0)
            report('i 목록 누르기 → 그쪽 보기', J(ed, '__map3d.V.tgt') != t0)
            ed.js("document.querySelector('#cfKinds button[data-k=\"\"]').scrollIntoView({ block: 'nearest' })"); ed.frames(1)
            r = J(ed, "(() => { const r = document.querySelector('#cfKinds button[data-k=\"\"]').getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; })()")
            inp.click(*r)
            report('i 전체 → 모두', J(ed, "document.getElementById('qList').children.length") == n)
        errs(ed, '간섭')

        print('== m 재기 · 검사')
        ed.js('__t.look(0, %f, 40, 50, 60)' % s0); ed.frames(3)
        SP = empty_spots(ed)
        for b, k, need in (('msDist', 'dist', 2), ('msArea', 'area', 3), ('msHeight', 'height', 2), ('msProf', 'prof', 2)):
            ui(b)
            st = J(ed, '__map3d.MS.kind') == k and on(ed, b)
            for q in SP[:need]:
                inp.click(*q)
            if k in ('dist', 'area'):
                inp.key('Enter', 'Enter', 13)
            res = ed.wait_js("!document.getElementById('msBox').hidden && document.getElementById('msBox').dataset.kind === '%s'" % k, 5)
            report('m %s → 켜짐 · 점 %d → 결과 칸' % (b, need), st and res, str(J(ed, '{ k: __map3d.MS.kind, n: __map3d.MS.pts.length, d: __map3d.MS.done }')))
            if k == 'prof':
                shot(ed, SD, '09_재기_단면.png')
            ui(b)
            report('m %s 다시 → 끝' % b, J(ed, '__map3d.MS.kind') == '' and not on(ed, b))
        ed.js("__host.ckpicks = [{ ok: true, points: [{ name: 'K1', E: %f, N: %f, Z: null }], note: '' }]" % tuple(J(ed, '[window.RV_MAPPING.origin[0] + 5, window.RV_MAPPING.origin[1] + 5]')))
        ui('bCheck')
        ok = ed.wait_js("document.getElementById('ck').style.display === 'flex'", 5)
        report('m 검사점 → 파일 고르기 → 칸 (재기 · 검사 안)', ok and len(calls(ed, 'pick_checkpoints')) == 1 and on(ed, 'bCheck') and J(ed, "document.getElementById('ck').closest('.cat').dataset.cat") == 'meas')
        ui('bCheck')
        report('m 검사점 끄기', J(ed, "document.getElementById('ck').style.display") == 'none' and not on(ed, 'bCheck'))
        errs(ed, '재기')

        print('== o 결과물')
        ed.js("__host.crop = [[%f, %f], [%f, %f], [%f, %f]]" % tuple(J(ed, '(() => { const o = window.RV_MAPPING.origin; return [o[0], o[1], o[0] + 30, o[1], o[0] + 30, o[1] + 20]; })()')))
        ug.cat_open(ed, inp, 'bTrim', fresh=True)
        ok = ed.wait_js("!document.getElementById('tbClear').hidden", 5)
        report('o 머리줄 → 잘림 상태 읽기 (crop_get)', ok and J(ed, "document.getElementById('tbState').textContent").startswith('잘림 · 점 3개') and len(calls(ed, 'crop_get')) >= 1)
        ui('bFiles')
        time.sleep(0.2)
        report('o 결과물 폴더 (open_results)', len(calls(ed, 'open_results')) == 1)
        ui('tbCut')
        report('o 잘라내기 → 위에서 · 단추 켜짐', J(ed, '__map3d.MS.kind') == 'crop' and J(ed, '__map3d.V.el') > 80 and on(ed, 'tbCut'))
        esc()
        if vis(ed, 'ngDraw'):
            ui('ngDraw')
            report('o 땅 아님 → 그리기 · 단추 켜짐', J(ed, '__map3d.MS.kind') == 'ng' and on(ed, 'ngDraw'))
            esc()
        report('o Esc → 끝', J(ed, '__map3d.MS.kind') == '' and not on(ed, 'tbCut'))
        errs(ed, '결과물')

        print('== w 웹 공유')
        ug.cat_open(ed, inp, 'hd_web')
        report('w 처음 = 복사 · 열기 꺼짐 · 주소 숨김', J(ed, "document.getElementById('tbCopy').disabled && document.getElementById('tbOpen').disabled") and not vis(ed, 'tbUrlRow'))
        ui('tbWeb')
        ok1 = ed.wait_js("!document.getElementById('tbProg').hidden && document.getElementById('tbProg').closest('.cat').dataset.cat === 'web'", 5)
        okw = ed.wait_js("!document.getElementById('tbUrlRow').hidden", 10)
        url = J(ed, "document.getElementById('tbUrl').value")
        report('w 올리기 → 진행 (웹 공유 안) → 주소', ok1 and okw and url.startswith('https://') and len(calls(ed, 'export_web')) == 1, url)
        report('w 머리줄 «올림» · 상태 · 다시 올리기', J(ed, "document.getElementById('webTag').textContent") == '올림' and J(ed, "document.getElementById('webState').textContent").startswith('올림')
               and J(ed, "document.querySelector('#tbWeb span').textContent") == '다시 올리기')
        ui('tbCopy')
        time.sleep(0.2)
        report('w 주소 복사 (copy_text)', calls(ed, 'copy_text')[-1:] == [url])
        ed.js("window.__open = []; window.pywebview.api.open_url = (u) => { window.__open.push(u); return Promise.resolve({ ok: true }); }")
        ui('tbOpen')
        time.sleep(0.2)
        report('w 열기 (open_url)', J(ed, 'window.__open') == [url])
        report('w 결과물 칸 진행 막대 안 건드림', J(ed, "document.getElementById('trimBox').contains(document.getElementById('tbProg'))") is False)
        shot(ed, SD, '02_카테고리_펼침.png')
        ed.js("document.getElementById('side').scrollTop = 1e6"); ed.frames(2)
        shot(ed, SD, '02b_카테고리_아래.png')
        errs(ed, '웹 공유')

        print('== b 브라우저 (프로그램 밖)')
        ed.js('localStorage.clear()')
        setup_page(ed, base + 'index.html')
        report('b 프로그램 전용 숨김 (처음 · SHP · 지장물 · 검사점 · 결과물 · 웹 공유)', J(ed, ug.HOST_HIDDEN) and not vis(ed, 'bHome') and not vis(ed, 'bPipes') and not vis(ed, 'bCheck') and not vis(ed, 'bTrim') and not vis(ed, 'hd_web'))
        ed.js("(() => { const s = document.querySelector('#side .cat[data-cat=pipe]'); if (s && s.querySelector('.catB').hidden) s.querySelector('.catH').click(); })()")   # 처음엔 보기만 펼침 (1127) → 관로 칸 폄
        ed.frames(2)
        report('b 직접 그리기 · 고치기 · 보기 보임', vis(ed, 'pNew') and vis(ed, 'tgEdit') and vis(ed, 'bTop'))
        ui('pNew')
        report('b 직접 그리기 됨', J(ed, '__map3d.MS.kind') == 'pipe')
        esc()
        shot(ed, SD, '04_브라우저.png')
        errs(ed, '브라우저')
    finally:
        ed.__exit__(None, None, None)
        srv.shutdown()

    print('== w 올린 기록 읽기 (file:// · list_shares)')
    url = 'file:///' + os.path.join(out, 'index_host.html').replace('\\', '/')
    with Edge(os.path.join(SD, '_prof2'), WIN, gl=False, extra=['--allow-file-access-from-files']) as ed:
        inp = ce.Inp(ed)
        ed.goto(url)
        ed.wait_js(Edge.NEW + " && window.__map3d && document.readyState === 'complete'", 30)
        ed.js("window.pywebview.api.list_shares = () => Promise.resolve([{ path: 'C:\\\\딴곳\\\\x', url: 'https://x.invalid/a/', files: 3 }, { path: %s, url: 'https://x.invalid/rv_t/', files: 12 }])"
              % json.dumps(out.replace('/', '\\')))
        ug.cat_open(ed, inp, 'hd_web')
        ok = ed.wait_js("document.getElementById('webState').textContent === '올림 · 파일 12개'", 5)
        report('w 이 폴더 기록 → 올림 · 주소 · 복사 켜짐', ok and J(ed, "document.getElementById('tbUrl').value") == 'https://x.invalid/rv_t/' and not J(ed, "document.getElementById('tbCopy').disabled"),
               J(ed, "document.getElementById('webState').textContent"))
        ed.exceptions.clear(); ed.console.clear()      # file:// 은 모델 (fetch) 을 못 읽음 → 오류는 이 시험 밖

    for where, e in ug.R['errs']:
        print('  오류', where, e)
    print('== %s · 실패 %d %s' % ('통과' if ug.R['ok'] else '실패', len(ug.R['fails']), ug.R['fails']))
    return 0 if ug.R['ok'] else 1


if __name__ == '__main__':
    sys.exit(main())
