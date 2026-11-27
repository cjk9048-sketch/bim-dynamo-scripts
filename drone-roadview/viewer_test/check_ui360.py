"""360 로드뷰 화면 · 오른쪽 옵션 칸 배치 시험 (JACK 1126 «3D 매핑 화면과 같은 배치 · 같은 꼴 · 관로 부가 기능은 360 에도 · 360 은 그리기 없음»).
make_synth --trench 폴더를 복사해 (가시설 구간 · 지장물을 더함) 화면 없는 Edge (cdp.py) 로 열고 진짜 마우스 · 키로 몬다. 그림은 --shots 폴더에.

    python -X utf8 check_ui360.py --src <make_synth --trench 폴더> --work <빈 시험 폴더> --shots <그림 폴더>

  t  윗줄 = 처음 (프로그램) · 로고 · 이름 · 사진 번호 · 조작법 · 오른쪽 칸 만 · 조작법 열고 닫기 (Esc) · 오른쪽 칸 접기 → 360 넓어짐 · 360 칸 1019×820
  c  카테고리: 차례 = 3D 와 같음 (재기 · 검사 자리 = 맞춤) · 처음 = 보기 · 관로 · 터파기 펴짐 · 머리줄 누르기 = 펴고 접기 · 다시 열어도 기억 ·
     «가시설» 다음에 «하천 추진 · 횡단» 카테고리 (자세한 시험 = check_river360.py)
  v  보기: 이전 · 다음 사진 · 바로 아래 · 북쪽 · 처음 시점 (주소 값으로 연 그림과 같음) · 그림 저장 (내려받기 PNG · 프로그램 = save_image) · 레이어
  p  관로: 직접 그리기 없음 · 브라우저 = 고치기만 · 프로그램 = SHP 불러오기 창 · 지장물 가져오기 창 · SHP 내보내기 (export_pipes_shp) · 고치기 (Esc)
  r  터파기 · 점용: 영역 · 터파기 · 단면 창 · 점용 범례 · 터파기 형상 · 기본값 단추
  h  가시설: 머리줄 = 칸 · 형식 고르기 → 안내 줄 · Esc 취소 · 접으면 닫힘
  q  수량: 갈래 단추마다 표 줄 (그 갈래만) · 같은 단추 = 닫기 · 가시설 연장 = 구간 합 (따로 셈)
  i  간섭: 이격 · 수 · 머리줄 이름표 · 지장물 종류 단추 → 목록 거르기 · 목록 누르기
  m  맞춤: 머리줄 = 칸 (calPane.open) · 점 찍어 맞추기
  w  웹 공유: 올린 기록 (list_shares) 읽기 · 올리기 → 진행 (웹 카테고리 안) → 주소 · 복사 · 열기 · 머리줄 «올림»
  o  결과물: 폴더로 내보내기 (export_folder)
  b  브라우저 (프로그램 밖): 프로그램 전용 단추 · 결과물 · 웹 공유 · 사진점 지우기 숨김 · 고치기는 보임
  모든 쪽 JS 오류 0
"""
import argparse
import json
import os
import pathlib
import shutil
import sys
import time

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import check_edit as ce  # noqa: E402
import check_interact as ci  # noqa: E402
import check_obst as co  # noqa: E402
import check_view as cv  # noqa: E402
from cdp import Edge  # noqa: E402

CATS = ['view', 'pipe', 'trench', 'shore', 'river', 'ai', 'qty', 'cf', 'cal', 'out', 'web']   # ai = AI 부대공 (1127)
HEAD = {'view': 'hd_view', 'pipe': 'hd_pipe', 'trench': 'hd_trench', 'shore': 'tgShore', 'river': 'hd_river', 'qty': 'hd_qty', 'cf': 'hd_cf', 'cal': 'calHead', 'out': 'hd_out', 'web': 'hd_web'}
SHORE = [{'a': 2.0, 'b': 7.0, 't': 'hpile'}, {'a': 10.0, 'b': 13.5, 't': 'sheet'}]   # T1 가시설 (시험용)
R = {'ok': True, 'fails': [], 'errs': []}


def report(name, ok, text=''):
    print('   %-50s %s  %s' % (name, text, '통과' if ok else '실패'))
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
    return report('JS 오류 (%s)' % where, not e, '%d 줄' % len(e))


def vis(ed, eid):
    return J(ed, "(() => { const e = document.getElementById('%s'); return !!e && e.getClientRects().length > 0; })()" % eid)


def on(ed, eid):
    return J(ed, "document.getElementById('%s').classList.contains('on')" % eid)


def opened(ed):
    return J(ed, "[...document.querySelectorAll('#opts .cat')].filter((c) => c.getClientRects().length && !c.querySelector('.catB').hidden).map((c) => c.dataset.cat)")


def setup(src, work):
    """시험 폴더: --trench 복사 + 저장소 보기 화면 + T1 가시설 + 지장물 (check_obst 의 가스 · 통신)."""
    shutil.rmtree(work, ignore_errors=True)
    shutil.copytree(src, work)
    for dst in ('로드뷰.html', 'index.html'):
        shutil.copyfile(os.path.join(REPO, 'viewer', 'index.html'), os.path.join(work, dst))
    for f in ('app.js', 'style.css', 'river.js'):
        shutil.copyfile(os.path.join(REPO, 'viewer', f), os.path.join(work, 'viewer', f))
    co.write_data(work)                                     # T4 + 지장물
    pj = os.path.join(work, 'data', 'pipes.js')
    t = open(pj, encoding='utf-8').read()
    P = json.loads(t[t.index('{'):t.rindex('}') + 1])
    next(l for l in P['lines'] if l.get('label') == 'T1')['shore'] = SHORE
    with open(pj, 'w', encoding='utf-8', newline='\n') as f:
        f.write('window.RV_PIPES = ' + json.dumps(P, ensure_ascii=False, indent=1) + ';\n')
    return ci.prepare(work, host=True)


class Page:
    def __init__(self, ed, work, host_page):
        self.ed, self.work, self.host_page = ed, work, host_page

    def url(self, host, q=''):
        page = self.host_page if host else '로드뷰.html'
        return pathlib.Path(self.work, page).resolve().as_uri() + '?t=none&shot=0002' + q

    def load(self, host, q='', clear=True):
        if clear:
            self.ed.goto(self.url(host, q))
            self.ed.wait_js(cv.READY, timeout=60)
            self.ed.js('localStorage.clear()')
        self.ed.goto(self.url(host, q))
        ok = self.ed.wait_js(cv.READY, timeout=60)
        self.ed.frames(5)
        return ok


def click(ed, inp, eid):
    """진짜 마우스로 누름 (오른쪽 칸 카테고리 안이면 먼저 보이게 굴림)."""
    inp.el_click(eid)
    ed.frames(3)


def view_png(ed, png):
    """360 칸만 (WebGL + 겹쳐 그림) · 단추 · 안내 숨김."""
    ed.js("document.querySelectorAll('#viewPane > :not(#gl):not(#ov)').forEach((e) => { e.style.visibility = 'hidden'; })")
    ed.frames(3)
    ed.screenshot(png)
    ed.js("document.querySelectorAll('#viewPane > :not(#gl):not(#ov)').forEach((e) => { e.style.visibility = ''; })")
    r = ce.rect(ed, 'ov')
    a = np.asarray(Image.open(png).convert('RGB')).astype(int)
    return a[int(r['top']):int(r['top'] + r['height']), int(r['left']):int(r['left'] + r['width'])]


def shot(ed, name, SD):
    p = os.path.join(SD, name)
    ed.frames(3)
    ed.screenshot(p)
    return p


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
    work, SD = os.path.abspath(a.work), os.path.abspath(a.shots)
    os.makedirs(SD, exist_ok=True)
    host_page = setup(os.path.abspath(a.src), work)
    ed = Edge(os.path.join(work + '_prof'), cv.WIN).__enter__()
    pg = Page(ed, work, host_page)
    inp = ce.Inp(ed)
    try:
        # ------------------------------------------------------------ t 윗줄
        print('== t  윗줄 · 조작법 · 오른쪽 칸')
        report('불러옴 (프로그램)', pg.load(True))
        top = J(ed, "[...document.querySelectorAll('#top button')].filter((b) => b.getClientRects().length).map((b) => b.id)")
        report('t 윗줄 단추 = 처음 · 조작법 · 오른쪽 칸 만', top == ['hHome', 'bHelp', 'tgMap'], json.dumps(top))
        st = J(ed, "{logo: !!document.getElementById('dohwa').naturalWidth, title: document.getElementById('title').textContent, no: document.getElementById('shotNo').textContent,"
                   " top: document.getElementById('top').getBoundingClientRect().height, bg: getComputedStyle(document.getElementById('top')).backgroundColor,"
                   " ov: [Math.round(document.getElementById('ov').getBoundingClientRect().width), Math.round(document.getElementById('ov').getBoundingClientRect().height)]}")
        report('t 어두운 윗줄 48 px · 로고 · 이름 · 사진 번호 · 360 칸 1019×820', st['top'] == 48 and st['bg'] == 'rgb(31, 31, 31)' and st['logo'] and st['no'].startswith('2 /') and st['ov'] == [1019, 820],
               json.dumps(st, ensure_ascii=False))
        shot(ed, '01_처음.png', SD)
        click(ed, inp, 'bHelp')
        h1 = vis(ed, 'helpPop') and on(ed, 'bHelp')
        inp.key('Escape', 'Escape', 27)
        h2 = not vis(ed, 'helpPop')
        click(ed, inp, 'bHelp')
        ed.screenshot(os.path.join(SD, '12_조작법.png'))
        inp.click(400, 400)
        ed.frames(2)
        report('t 조작법 열기 · Esc 닫기 · 밖 누르기 닫기', h1 and h2 and not vis(ed, 'helpPop'))
        w0 = J(ed, "document.getElementById('ov').getBoundingClientRect().width")
        click(ed, inp, 'tgMap')
        w1 = J(ed, "document.getElementById('ov').getBoundingClientRect().width")
        s1 = J(ed, "document.getElementById('side').classList.contains('hidden')")
        click(ed, inp, 'tgMap')
        report('t 오른쪽 칸 접기 → 360 넓어짐 · 다시 펼침', s1 and w1 > w0 + 300 and not J(ed, "document.getElementById('side').classList.contains('hidden')"), '%.0f → %.0f' % (w0, w1))
        errs(ed, 't')

        # ------------------------------------------------------------ c 카테고리
        print('== c  카테고리')
        order = J(ed, "[...document.querySelectorAll('#opts .cat')].map((c) => c.dataset.cat)")
        report('c 차례 = 3D 와 같음 (재기 · 검사 자리 = 맞춤)', order == CATS, json.dumps(order))
        names = J(ed, "[...document.querySelectorAll('#opts .cat .catH b')].map((b) => b.textContent)")
        report('c 이름', names == ['보기', '관로', '터파기 · 점용', '가시설', '하천 추진 · 횡단', 'AI 부대공', '수량', '간섭', '맞춤', '결과물', '웹 공유'], json.dumps(names, ensure_ascii=False))
        html = open(os.path.join(REPO, 'viewer', 'index.html'), encoding='utf-8').read()
        i_sh, i_rv, i_q = html.index('data-cat="shore"'), html.find('<section class="cat" data-cat="river">'), html.index('data-cat="qty"')
        report('c «가시설» 다음 «하천 추진 · 횡단» 카테고리 (3D 와 같은 꼴 · 시험 = check_river360)', i_sh < i_rv < i_q and J(ed, "!document.querySelector('[data-cat=river]').hidden"), str(i_rv))
        report('c 처음 = 보기 · 관로 · 터파기 펴짐', opened(ed) == ['view', 'pipe', 'trench'], json.dumps(opened(ed)))
        for c in ('view', 'shore', 'qty', 'cf'):
            click(ed, inp, HEAD[c])
        o1 = opened(ed)
        shot(ed, '02_카테고리_펼침.png', SD)
        report('c 머리줄 누르기 = 펴고 접기', o1 == ['pipe', 'trench', 'shore', 'qty', 'cf'], json.dumps(o1))
        pg.load(True, clear=False)
        report('c 다시 열어도 기억 (rv_360:cats)', opened(ed) == o1, json.dumps(opened(ed)))
        errs(ed, 'c')

        # ------------------------------------------------------------ v 보기
        print('== v  보기')
        pg.load(True)
        n0 = J(ed, "document.getElementById('shotNo').textContent")
        click(ed, inp, 'vNext')
        ed.wait_js("document.getElementById('shotNo').textContent.indexOf('3 /') === 0", 10)
        n1 = J(ed, "document.getElementById('shotNo').textContent")
        click(ed, inp, 'vPrev')
        ed.wait_js("document.getElementById('shotNo').textContent.indexOf('2 /') === 0 && " + cv.READY, 10)
        n2 = J(ed, "document.getElementById('shotNo').textContent")
        report('v 다음 · 이전 사진', n0.startswith('2 /') and n1.startswith('3 /') and n2.startswith('2 /'), ' → '.join([n0, n1, n2]))
        ed.frames(4)
        a0 = view_png(ed, os.path.join(SD, '_v0.png'))
        shots = J(ed, 'window.RV_PROJECT.shots')
        yaw0 = next(s for s in shots if s['id'] == '0002')['yaw']
        click(ed, inp, 'vNorth')
        a1 = view_png(ed, os.path.join(SD, '_v1.png'))
        click(ed, inp, 'vDown')
        a2 = view_png(ed, os.path.join(SD, '_v2.png'))
        click(ed, inp, 'vHome')
        a3 = view_png(ed, os.path.join(SD, '_v3.png'))
        pg.load(True, '&yaw=0&pitch=-45&fov=90', clear=False)
        r1 = view_png(ed, os.path.join(SD, '_r1.png'))
        pg.load(True, '&yaw=0&pitch=-89&fov=90', clear=False)
        r2 = view_png(ed, os.path.join(SD, '_r2.png'))
        d = lambda x, y: int(np.abs(x - y).max()) if x.shape == y.shape else 999  # noqa: E731
        report('v 북쪽 = 주소 yaw 0 그림 · 바로 아래 = pitch −89 그림', d(a1, r1) <= 3 and d(a2, r2) <= 3 and d(a0, a1) > 40, 'yaw %s · 차이 %d · %d · 처음↔북쪽 %d' % (yaw0, d(a1, r1), d(a2, r2), d(a0, a1)))
        report('v 처음 시점 = 처음 연 그림 (찍은 방향 · 45° 아래 · 90°)', d(a3, a0) <= 3, '차이 %d' % d(a3, a0))
        # 그림 저장: 프로그램 = save_image
        pg.load(True, clear=False)
        click(ed, inp, 'vShot')
        ed.wait_js("window.__host.calls.some((c) => c.name === 'save_image')", 5)
        sv = J(ed, "window.__host.calls.filter((c) => c.name === 'save_image').map((c) => c.arg)")
        time.sleep(0.2)
        report('v 그림 저장 (프로그램) → save_image PNG · 알림', len(sv) == 1 and sv[0]['head'] == 'data:image/png;base64,' and sv[0]['len'] > 20000 and sv[0]['name'].endswith('.png')
               and J(ed, "document.getElementById('toast').textContent").startswith('저장 · '), json.dumps(sv, ensure_ascii=False)[:160])
        # 레이어
        b0 = view_png(ed, os.path.join(SD, '_l0.png'))
        click(ed, inp, 'tgPipes')
        b1 = view_png(ed, os.path.join(SD, '_l1.png'))
        p_off = not on(ed, 'tgPipes')
        click(ed, inp, 'tgPipes')
        lb0 = on(ed, 'tgLbl')
        click(ed, inp, 'tgLbl')
        lb1 = on(ed, 'tgLbl')
        click(ed, inp, 'tgLbl')
        report('v 레이어: 관로 끄기 → 그림 바뀜 · 다시 켬 · 이름표 켜고 끄기 · 지장물 단추 보임', d(b0, b1) > 40 and p_off and on(ed, 'tgPipes') and lb0 != lb1 and on(ed, 'tgLbl') == lb0 and vis(ed, 'tgObst'),
               '관로 끔 차이 %d · 이름표 %s → %s · 지장물 %s' % (d(b0, b1), lb0, lb1, vis(ed, 'tgObst')))
        shot(ed, '03_보기.png', SD)
        errs(ed, 'v')

        # ------------------------------------------------------------ p 관로
        print('== p  관로')
        pc = J(ed, "[...document.querySelector('#opts .cat[data-cat=\"pipe\"]').querySelectorAll('.fb')].filter((b) => b.getClientRects().length).map((b) => b.querySelector('span').textContent)")
        draw = J(ed, "!!document.getElementById('pNew') || !!document.querySelector('[data-ms]') || document.body.innerText.indexOf('직접 그리기') >= 0")
        report('p 관로 단추 = SHP 불러오기 · 고치기 · SHP 내보내기 · 지장물 가져오기 · 직접 그리기 없음', pc == ['SHP 불러오기', '고치기', 'SHP 내보내기', '지장물 가져오기'] and not draw, json.dumps(pc, ensure_ascii=False))
        click(ed, inp, 'hPipes')
        p1 = vis(ed, 'pipeDlg')
        shot(ed, '04_관로_불러오기.png', SD)
        inp.key('Escape', 'Escape', 27)
        click(ed, inp, 'hObst')
        p2 = vis(ed, 'obstDlg') and not vis(ed, 'pipeDlg')
        inp.key('Escape', 'Escape', 27)
        report('p SHP 불러오기 → 관로 넣기 창 · 지장물 가져오기 → 지장물 넣기 창 · Esc', p1 and p2 and not vis(ed, 'dlgBack'))
        click(ed, inp, 'bShpOut')
        ed.wait_js("document.getElementById('toast').textContent.indexOf('SHP 저장') === 0", 5)
        report('p SHP 내보내기 → export_pipes_shp · 알림', J(ed, "window.__host.calls.filter((c) => c.name === 'export_pipes_shp').length") == 1, J(ed, "document.getElementById('toast').textContent"))
        click(ed, inp, 'tgEdit')
        e1 = on(ed, 'tgEdit') and J(ed, "document.getElementById('edChip').classList.contains('show')")
        inp.key('Escape', 'Escape', 27)
        report('p 고치기 → 켜짐 · 안내 줄 · Esc 끝', e1 and not on(ed, 'tgEdit'))
        errs(ed, 'p')

        # ------------------------------------------------------------ r 터파기 · 점용
        print('== r  터파기 · 점용')
        r0 = J(ed, "{area: document.getElementById('mSurf').classList.contains('on'), lg: !document.getElementById('ugLegend').hidden, show: !document.getElementById('ugShow').hidden, tag: document.getElementById('ugTag').textContent}")
        click(ed, inp, 'mDepth')
        r1_ = J(ed, "{m: document.getElementById('app').dataset.mode, lg: !document.getElementById('ugLegend').hidden, show: !document.getElementById('ugShow').hidden}")
        pit0 = on(ed, 'tgPit')
        click(ed, inp, 'tgPit')
        pit1 = on(ed, 'tgPit')
        click(ed, inp, 'tgPit')
        click(ed, inp, 'mSurf')
        click(ed, inp, 'lgTmp')
        lg1 = on(ed, 'lgTmp')
        click(ed, inp, 'lgTmp')
        report('r 영역 · 터파기 · 범례 (영역만) · 터파기 형상 (터파기만) · 켜고 끄기',
               r0['area'] and r0['lg'] and not r0['show'] and r1_ == {'m': 'trench', 'lg': False, 'show': True} and pit0 and not pit1 and not lg1 and r0['tag'] == '가시화 기본값',
               json.dumps([r0, r1_], ensure_ascii=False))
        click(ed, inp, 'tgSec')
        s1 = vis(ed, 'secDlg') and on(ed, 'tgSec')
        ed.js("const n = document.getElementById('sec_n'); n.value = '0.6'; n.dispatchEvent(new Event('input', {bubbles: true})); n.dispatchEvent(new Event('change', {bubbles: true}))")
        ed.frames(2)
        tag1 = J(ed, "document.getElementById('ugTag').textContent")
        shot(ed, '05_터파기_단면.png', SD)
        click(ed, inp, 'tgSec')
        click(ed, inp, 'bTrDef')
        tag2 = J(ed, "document.getElementById('ugTag').textContent")
        report('r 단면 창 · 값 바꿈 → «직접 입력» · [기본값] → «가시화 기본값»', s1 and not vis(ed, 'secDlg') and tag1 == '직접 입력' and tag2 == '가시화 기본값'
               and J(ed, "document.getElementById('toast').textContent") == '가시화 기본값', '%s → %s' % (tag1, tag2))
        errs(ed, 'r')

        # ------------------------------------------------------------ h 가시설
        print('== h  가시설')
        click(ed, inp, 'tgShore')
        h1 = J(ed, "{box: !document.getElementById('shBox').hidden, n: document.querySelectorAll('#shTypes button').length, on: document.getElementById('tgShore').classList.contains('on'), rows: document.querySelectorAll('#shList .shRow').length}")
        click(ed, inp, 'shSheet')
        c1 = J(ed, "document.getElementById('shChip').classList.contains('show') && document.getElementById('shName').textContent")
        shot(ed, '06_가시설.png', SD)
        inp.key('Escape', 'Escape', 27)
        c2 = J(ed, "document.getElementById('shChip').classList.contains('show')")
        click(ed, inp, 'shHpile')
        click(ed, inp, 'tgShore')
        c3 = J(ed, "{box: !document.getElementById('shBox').hidden, chip: document.getElementById('shChip').classList.contains('show'), on: document.getElementById('tgShore').classList.contains('on')}")
        report('h 머리줄 → 종류 셋 · 목록 (T1 둘) · 시트파일 → 안내 줄 · Esc · 접으면 찍던 것 취소', h1 == {'box': True, 'n': 3, 'on': True, 'rows': 2} and c1 == '시트파일' and not c2
               and c3 == {'box': False, 'chip': False, 'on': False}, json.dumps([h1, c1, c3], ensure_ascii=False))
        errs(ed, 'h')

        # ------------------------------------------------------------ q 수량
        print('== q  수량')
        want_rows = {'all': None, 'pipe': ['len'], 'earth': ['cut', 'sand', 'fill'], 'pave': ['pav'], 'occ': ['tmp', 'perm'], 'shore': ['sh_panel', 'sh_hpile', 'sh_sheet']}
        btn = {'all': 'tgQty', 'pipe': 'qkPipe', 'earth': 'qkEarth', 'pave': 'qkPave', 'occ': 'qkOcc', 'shore': 'qkShore'}
        allk = ['len', 'cut', 'pav', 'sand', 'fill', 'tmp', 'perm', 'sh_panel', 'sh_hpile', 'sh_sheet']
        ok = True
        info = []
        if 'qty' not in opened(ed):
            click(ed, inp, 'hd_qty')
        for k, ks in want_rows.items():
            click(ed, inp, btn[k])
            st = J(ed, "{open: !document.getElementById('qtyBox').hidden, onb: [...document.querySelectorAll('[data-q]')].filter((b) => b.classList.contains('on')).map((b) => b.id),"
                       " rows: [...document.querySelectorAll('#qTab .qv')].filter((e) => e.id.indexOf('qt_') === 0 && e.getClientRects().length).map((e) => e.id.slice(3)),"
                       " title: document.getElementById('qTitle').textContent}")
            good = st['open'] and st['onb'] == [btn[k]] and st['rows'] == (ks or allk)
            ok &= good
            info.append('%s %s' % (st['title'], 'O' if good else json.dumps(st)))
            if k == 'shore':
                shot(ed, '07_수량_가시설.png', SD)
        report('q 갈래 단추마다 그 갈래 줄만 · 단추 하나 켜짐', ok, ' · '.join(info))
        sh = J(ed, "Object.fromEntries(['panel', 'hpile', 'sheet'].map((t) => [t, Number(document.getElementById('qt_sh_' + t).dataset.v)]))")
        report('q 가시설 연장 = 구간 합 (따로 셈)', abs(sh['hpile'] - 5.0) < 1e-6 and abs(sh['sheet'] - 3.5) < 1e-6 and sh['panel'] == 0, json.dumps(sh))
        click(ed, inp, 'qkShore')
        report('q 같은 단추 = 닫기', not vis(ed, 'qtyBox') and not on(ed, 'qkShore'))
        errs(ed, 'q')

        # ------------------------------------------------------------ i 간섭
        print('== i  간섭')
        if 'cf' not in opened(ed):
            click(ed, inp, 'hd_cf')
        ed.js("const g = document.getElementById('qGap'); g.value = '0.45'; g.dispatchEvent(new Event('input', {bubbles: true})); g.dispatchEvent(new Event('change', {bubbles: true}))")
        ed.wait_js("document.getElementById('qHit').textContent === '간섭 2곳'", 10)
        ed.frames(3)
        st = J(ed, "{hit: document.getElementById('qHit').textContent, tag: document.getElementById('cfTag').textContent, bad: document.getElementById('cfTag').classList.contains('bad'),"
                   " kinds: [...document.querySelectorAll('#cfKinds .tg')].map((b) => b.textContent), n: document.querySelectorAll('#qList button').length}")
        report('i 이격 0.45 → 간섭 2곳 · 머리줄 «2곳» · 종류 단추 (전체 · 가스 · 통신)', st['hit'] == '간섭 2곳' and st['tag'] == '2곳' and st['bad'] and st['n'] == 2
               and st['kinds'][0] == '전체2' and sorted(st['kinds'][1:]) == ['가스1', '통신1'], json.dumps(st, ensure_ascii=False))
        kb = J(ed, "[...document.querySelectorAll('#cfKinds .tg')].findIndex((b) => b.dataset.k === '가스')")
        ed.js("document.querySelectorAll('#cfKinds .tg')[%d].scrollIntoView({block: 'nearest'})" % kb)
        r = J(ed, "document.querySelectorAll('#cfKinds .tg')[%d].getBoundingClientRect()" % kb)
        inp.click(r['left'] + r['width'] / 2, r['top'] + r['height'] / 2)
        ed.frames(3)
        l1 = J(ed, "[...document.querySelectorAll('#qList button')].map((b) => b.textContent)")
        k1 = J(ed, "[...document.querySelectorAll('#cfKinds .tg.on')].map((b) => b.dataset.k)")
        shot(ed, '08_간섭_종류.png', SD)
        no0 = J(ed, "document.getElementById('shotNo').textContent")
        ed.js("document.querySelector('#qList button').scrollIntoView({block: 'nearest'})")
        r = J(ed, "document.querySelector('#qList button').getBoundingClientRect()")
        inp.click(r['left'] + 40, r['top'] + r['height'] / 2)
        ed.frames(4)
        report('i 가스 단추 → 목록 가스만 · 누르기 → 그쪽 보기 (오류 없음)', k1 == ['가스'] and len(l1) == 1 and '가스' in l1[0], json.dumps(l1, ensure_ascii=False) + ' · ' + no0)
        ed.js("document.querySelector('#cfKinds .tg').click()")
        ed.frames(2)
        report('i 전체 → 목록 둘', J(ed, "document.querySelectorAll('#qList button').length") == 2)
        errs(ed, 'i')

        # ------------------------------------------------------------ m 맞춤
        print('== m  맞춤')
        click(ed, inp, 'calHead')
        m1 = J(ed, "{open: document.getElementById('calPane').classList.contains('open'), rows: document.querySelectorAll('#calRows input').length, vid: document.getElementById('calVid').textContent}")
        click(ed, inp, 'alGo')
        m2 = J(ed, "document.getElementById('alChip').classList.contains('show') && document.getElementById('ov').classList.contains('pick')")
        shot(ed, '09_맞춤.png', SD)
        inp.key('Escape', 'Escape', 27)
        click(ed, inp, 'calHead')
        report('m 머리줄 → 맞춤 칸 (6 줄 · 영상) · 점 찍어 맞추기 · 접기', m1['open'] and m1['rows'] == 6 and m1['vid'].startswith('영상') and m2
               and not J(ed, "document.getElementById('calPane').classList.contains('open')"), json.dumps(m1, ensure_ascii=False))
        errs(ed, 'm')

        # ------------------------------------------------------------ w 웹 공유
        print('== w  웹 공유')
        me = os.path.normpath(work)
        ed.js("window.__opened = []; window.pywebview.api.open_url = (u) => { window.__opened.push(u); return Promise.resolve({ok: true}); };"
              " window.pywebview.api.list_shares = () => Promise.resolve([{path: %s, url: 'https://nas.example.invalid/rv/old/', files: 7}, {path: 'C:\\\\다른', url: 'https://x.invalid/', files: 1}]);"
              % json.dumps(me))
        click(ed, inp, 'hd_web')
        ed.wait_js("document.getElementById('webState').textContent.indexOf('올림') === 0", 5)
        w1 = J(ed, "{st: document.getElementById('webState').textContent, tag: document.getElementById('webTag').textContent, url: document.getElementById('exUrl').value, btn: document.getElementById('exWeb').textContent}")
        report('w 올린 기록 읽기 (list_shares · 이 폴더) → «올림 · 파일 7개» · 머리줄 «올림» · [다시 올리기]', w1 == {'st': '올림 · 파일 7개', 'tag': '올림', 'url': 'https://nas.example.invalid/rv/old/', 'btn': '다시 올리기'},
               json.dumps(w1, ensure_ascii=False))
        click(ed, inp, 'exWeb')
        busy = J(ed, "document.getElementById('exWeb').disabled && !document.getElementById('exProg').hidden")
        ed.wait_js("document.getElementById('exProg').dataset.st === 'done'", 10)
        ed.frames(2)
        w2 = J(ed, "{url: document.getElementById('exUrl').value, inWeb: !!document.getElementById('exProg').closest('[data-cat=\"web\"]'), done: !document.getElementById('exDone').hidden}")
        shot(ed, '10_웹공유.png', SD)
        click(ed, inp, 'exCopy')
        ed.wait_js("window.__host.calls.some((c) => c.name === 'copy_text')", 5)
        click(ed, inp, 'exOpen')
        time.sleep(0.2)
        op = J(ed, 'window.__opened')
        report('w 올리기 → 진행 (웹 카테고리 안) → 새 주소 · 복사 · 열기', busy and w2 == {'url': 'https://nas.example.invalid/rv/test/', 'inWeb': True, 'done': True}
               and op == ['https://nas.example.invalid/rv/test/'], json.dumps([w2, op]))
        errs(ed, 'w')

        # ------------------------------------------------------------ o 결과물
        print('== o  결과물')
        click(ed, inp, 'hd_out')
        click(ed, inp, 'exFolder')
        ed.wait_js("document.getElementById('toast').textContent.indexOf('내보냄') === 0", 5)
        report('o 폴더로 내보내기 → export_folder · 알림', J(ed, "window.__host.calls.filter((c) => c.name === 'export_folder').length") == 1, J(ed, "document.getElementById('toast').textContent"))
        shot(ed, '11_결과물_프로그램.png', SD)
        errs(ed, 'o')

        # ------------------------------------------------------------ b 브라우저
        print('== b  브라우저 (프로그램 밖)')
        pg.load(False)
        hid = J(ed, "['hHome', 'hPipes', 'hObst', 'bShpOut', 'tgTrim'].filter((i) => document.getElementById(i).getClientRects().length)")
        cats = J(ed, "[...document.querySelectorAll('#opts .cat')].filter((c) => c.getClientRects().length).map((c) => c.dataset.cat)")
        report('b 프로그램 단추 · 결과물 · 웹 공유 숨김 · 고치기 보임 · 직접 그리기 없음', hid == [] and cats == CATS[:-2] and vis(ed, 'tgEdit') and not J(ed, "document.body.innerText.indexOf('직접 그리기') >= 0"),
               json.dumps([hid, cats]))
        ed.js("window.__dl = []; HTMLAnchorElement.prototype.click = function () { window.__dl.push([this.download, this.href.slice(0, 22), this.href.length]); };")
        click(ed, inp, 'vShot')
        dl = J(ed, 'window.__dl')
        report('b 그림 저장 = 내려받기 PNG', len(dl) == 1 and dl[0][0].endswith('.png') and dl[0][1] == 'data:image/png;base64,' and dl[0][2] > 20000, json.dumps(dl, ensure_ascii=False))
        shot(ed, '13_브라우저.png', SD)
        errs(ed, 'b')
    finally:
        ed.__exit__(None, None, None)
        ci.cleanup(work)
    for w, e in R['errs']:
        print('   JS 오류 [%s] %s' % (w, e))
    print('사진: ' + SD)
    print('전체: ' + ('통과' if R['ok'] else '실패 · ' + ' · '.join(R['fails'])))
    sys.exit(0 if R['ok'] else 1)


if __name__ == '__main__':
    main()
