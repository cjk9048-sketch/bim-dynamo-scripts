"""360 로드뷰 화면 · AI 부대공 시험 (JACK 1127 «AI 결과 360 화면에도» · «관로를 불러왔을 때 · 임시점용 영역에 걸린 것만»).
make_synth --trench 폴더를 복사해 가짜 AI 결과 (data/ai_budae.js · 3D 매핑 원점 = 이 로드뷰 원점 + (10, −5) · 엔진과 정한 꼴) 를 넣고 화면 없는 Edge 로 연다.

    python -X utf8 check_ai360.py --src <make_synth --trench 폴더> --work <빈 시험 폴더> --shots <그림 폴더>

  z  원점 차이 맞춤 (pts + origin − 로드뷰 원점) · 임시점용 안 = c1 · as1 · w1t · w1b (짝) · no1 / 밖 = far1 숨김
  k  카테고리: 머리줄 «미확인 4» · 종류 단추 개수 (아님 뺌) · 목록 5 줄 · 연석 끄기 → 4 줄 · 360 그림 바뀜
  q  수량 «부대공»: 연석 = 20 m (임시점용 안 길이 · 손셈) · 아스팔트 > 0 · 콘크리트 0 (밖) · 보도블록 0 (아님) · «확인 안 함 4개 포함»
  m  맞음 · 아님 → ai_save (0.8 초 모아 · 고친 것) · 수량 바로 (아스팔트 아님 → 0)
  g  줄 누르기 → 그 후보를 잘 보는 사진 (attrs.photo) 으로 옮김 · 고른 줄
  b  브라우저 (웹 공유): 보기만 (맞음 · 아님 단추 없음) · 목록 · 그림 같음
  모든 쪽 JS 오류 0
"""
import argparse
import json
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import check_river360 as cr  # noqa: E402
from check_river360 import J, report, errs, near, cat_open, click, view_png, shot, R, ce, ci, cv, ms  # noqa: E402,F401
from cdp import Edge  # noqa: E402

V_A = ('a', '0001', 275, -55, 80)                    # T1 서쪽 (후보 둘레)
OFF = (10.0, -5.0)                                   # AI 결과 원점 = 로드뷰 원점 + 이것
SAVE_JS = r"""(function () { const api = window.pywebview && window.pywebview.api; if (!api) return;
  window.__aiSaves = []; api.ai_save = (p, d) => { window.__aiSaves.push({ p: p, d: JSON.parse(JSON.stringify(d)) }); return Promise.resolve({ ok: true }); }; })()"""


def ai_data(work):
    t = open(os.path.join(work, 'data', 'project.js'), encoding='utf-8').read()
    P = json.loads(t[t.index('{'):t.rindex('}') + 1])
    E0, N0 = float(P['origin']['E']), float(P['origin']['N'])
    o = [E0 + OFF[0], N0 + OFF[1], 0.0]
    loc = lambda pts: [[round(x - OFF[0], 4), round(y - OFF[1], 4), None] for x, y in pts]   # noqa: E731
    rect = lambda x0, x1, y0, y1: [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]   # noqa: E731
    F = [
        ('c1', 'curb', 'line', [(-30, 3.3), (-10, 3.3)], 0.15, 0.8, ['ortho', 'dsm'], 'ai', None, {}),
        ('as1', 'asphalt', 'area', rect(-20, -5, 2.0, 4.0), None, 0.6, ['ortho', '360'], 'ai', '0002', {'state': '균열'}),
        ('far1', 'concrete', 'area', rect(-20, -10, 12, 16), None, 0.9, ['ortho', '360'], 'ai', None, {}),
        ('w1t', 'wall', 'line', [(-28, 3.2), (-22, 3.2)], 1.5, 0.5, ['dsm', '360'], 'ai', None, {'edge': 'top', 'pair': 'w1'}),
        ('w1b', 'wall', 'line', [(-28, 7.0), (-22, 7.0)], 1.5, 0.5, ['dsm', '360'], 'ai', None, {'edge': 'bottom', 'pair': 'w1'}),
        ('no1', 'sidewalk', 'area', rect(0, 5, 2.5, 3.5), None, 0.7, ['ortho', '360'], 'no', None, {}),
    ]
    feats = []
    for fid, cls, kind, pts, h, conf, src, st, photo, at in F:
        a = {'state': '모름', 'material': '', 'photo': photo}
        a.update(at)
        feats.append({'id': fid, 'cls': cls, 'kind': kind, 'pts': loc(pts), 'height': h, 'conf': conf, 'src': src, 'attrs': a, 'status': st, 'note': ''})
    return {'version': 1, 'made': '시험', 'crs': P.get('crs', 'EPSG:5186'), 'origin': o, 'src360': os.path.basename(work), 'src3d': '3D_시험',
            'classes': {'curb': '연석', 'wall': '석축', 'sidewalk': '보도블록', 'asphalt': '아스팔트', 'concrete': '콘크리트', 'unpaved': '비포장'}, 'features': feats}


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
    host_page = cr.setup(src, work)
    ai = ai_data(work)
    open(os.path.join(work, 'data', 'ai_budae.js'), 'w', encoding='utf-8').write('window.RV_AI = ' + json.dumps(ai, ensure_ascii=False) + ';\n')
    ed = Edge(os.path.join(work + '_prof'), cv.WIN).__enter__()
    ed.net(block=ci.BLOCK)
    pg = cr.Page(ed, work, host_page)
    inp = ce.Inp(ed)
    try:
        print('== z 읽기 · 원점 맞춤 · 임시점용 영역')
        report('불러옴 (프로그램 쪽)', pg.load(True, V_A, 'area', clear=True))
        ed.js(SAVE_JS)
        st = J(ed, '__rv360.ai.st()')
        report('z AI 결과 읽힘 (후보 6 · 못 씀 없음)', st['has'] and st['n'] == 6 and not st['bad'], json.dumps(st, ensure_ascii=False))
        f1 = J(ed, "__rv360.ai.feat('c1')")
        report('z 원점 차이 맞춤 (c1 = 로드뷰 격자 (−30, 3.3) ~ (−10, 3.3) · 길이 20)', f1 and near(f1['P'][0][0], -30, 1e-6) and near(f1['P'][0][1], 3.3, 1e-6) and near(f1['meas'], 20, 1e-6),
               str(f1 and f1['P'][0][:2]))
        zs = sorted(J(ed, '__rv360.ai.zone()'))
        report('z 임시점용 안 = c1 · as1 · no1 · w1t + 짝 w1b / 밖 far1 숨김', zs == sorted(['c1', 'as1', 'no1', 'w1t', 'w1b']), str(zs))
        errs(ed, '읽기')

        print('== k 카테고리 · 목록 · 그림')
        cat_open(ed, inp, 'hd_ai')
        ed.frames(3)
        report('k 머리줄 «미확인 4»', J(ed, "document.getElementById('aiTag').textContent") == '미확인 4', J(ed, "document.getElementById('aiTag').textContent"))
        kd = J(ed, "[...document.querySelectorAll('#aiKinds .tg')].map((b) => [b.dataset.k, Number(b.querySelector('.n').textContent), b.classList.contains('on')])")
        want = {'curb': 1, 'wall': 2, 'sidewalk': 0, 'asphalt': 1, 'concrete': 0, 'unpaved': 0}
        report('k 종류 단추 여섯 · 개수 (아님 뺌 · 영역 밖 뺌)', len(kd) == 6 and all(n == want[k] and on for k, n, on in kd), str(kd))
        rows = J(ed, "[...document.querySelectorAll('#aiList .aiRow')].map((r) => r.dataset.id)")
        report('k 목록 5 줄 (미확인 먼저 · 아님 끝) · 프로그램 = 맞음/아님 단추', len(rows) == 5 and rows[-1] == 'no1' and J(ed, "document.querySelectorAll('#aiList .aiOk').length") == 5, str(rows))
        report('k 360 다름 표 (360 이 못 본 c1)', '360 다름' in J(ed, "[...document.querySelectorAll('#aiList .aiRow')].find((r) => r.dataset.id === 'c1').textContent"))
        img_on = view_png(ed, os.path.join(SD, '01_AI_360.png'))
        for k in ('curb', 'wall', 'asphalt'):
            click(ed, inp, 'aiK_' + k)
        ed.frames(3)
        img_off = view_png(ed, os.path.join(SD, '02_AI_끔.png'))
        d = float((np.abs(img_on - img_off).sum(2) > 40).mean())
        report('k 종류 끄기 → 360 그림 바뀜 · 목록 줄어듦', d > 0.002 and J(ed, "document.querySelectorAll('#aiList .aiRow').length") == 1, '바뀐 화소 %.2f %%' % (100 * d))
        for k in ('curb', 'wall', 'asphalt'):
            click(ed, inp, 'aiK_' + k)
        ed.frames(3)
        shot(ed, SD, '03_카테고리.png')
        errs(ed, '카테고리')

        print('== q 수량 «부대공»')
        click(ed, inp, 'aiQty')
        ed.frames(3)
        q = J(ed, '__rv360.ai.qty()')
        tot = {k: 0.0 for k in ('bd_curb', 'bd_wall', 'bd_asphalt', 'bd_concrete', 'bd_sidewalk')}
        for kk, v in q.items():
            if kk != 'un':
                for k in tot:
                    tot[k] += v.get(k, 0)
        report('q 연석 20 m (손셈) · 석축 = 위 선만 6 m · 아스팔트 > 0 · 콘크리트 0 (밖) · 보도블록 0 (아님)',
               near(tot['bd_curb'], 20, 1e-3) and near(tot['bd_wall'], 6, 1e-3) and tot['bd_asphalt'] > 1 and tot['bd_concrete'] == 0 and tot['bd_sidewalk'] == 0, str({k: round(v, 3) for k, v in tot.items()}))
        note = J(ed, "document.getElementById('qNote').textContent")
        report('q 부대공 표 · «확인 안 함 3개 포함» (연석 · 아스팔트 · 석축 위)', J(ed, "!document.getElementById('qtyBox').hidden && document.getElementById('qtyBox').dataset.view === 'budae'") and '확인 안 함 3개 포함' in note, note)
        tv = J(ed, "Number(document.getElementById('qt_bd_curb').dataset.v)")
        report('q 표 전체 연석 = 셈', near(tv, 20, 1e-3), str(tv))
        shot(ed, SD, '04_수량_부대공.png')
        errs(ed, '수량')

        print('== m 맞음 · 아님 → 저장')
        cat_open(ed, inp, 'hd_ai')
        r = J(ed, "(() => { const b = [...document.querySelectorAll('#aiList .aiRow')].find((x) => x.dataset.id === 'as1').querySelector('.aiNo'); b.scrollIntoView({block: 'nearest'}); const q = b.getBoundingClientRect(); return [q.left + q.width / 2, q.top + q.height / 2]; })()")
        inp.click(r[0], r[1])
        ed.frames(2)
        r = J(ed, "(() => { const b = [...document.querySelectorAll('#aiList .aiRow')].find((x) => x.dataset.id === 'c1').querySelector('.aiOk'); b.scrollIntoView({block: 'nearest'}); const q = b.getBoundingClientRect(); return [q.left + q.width / 2, q.top + q.height / 2]; })()")
        inp.click(r[0], r[1])
        ed.js("document.getElementById('hSave').click()")              # JACK 1127: 윗줄 [저장] 을 눌러야 씀
        ok = ed.wait_js('(window.__aiSaves || []).length > 0', 5)
        ed.frames(10)
        sv = J(ed, 'window.__aiSaves || []')
        fs = {f['id']: f for f in (sv[-1]['d']['features'] if sv else [])}
        report('m ai_save 한 번 (0.8 초 모아) · as1 아님 · c1 맞음 · 나머지 그대로', ok and len(sv) == 1 and fs['as1']['status'] == 'no' and fs['c1']['status'] == 'ok' and fs['far1']['status'] == 'ai' and len(fs) == 6,
               '%d 번 · %s' % (len(sv), {k: fs[k]['status'] for k in fs} if fs else None))
        q2 = J(ed, '__rv360.ai.qty()')
        asp = sum(v.get('bd_asphalt', 0) for kk, v in q2.items() if kk != 'un')
        report('m 아스팔트 아님 → 수량 0 · 머리줄 «미확인 2»', asp == 0 and J(ed, "document.getElementById('aiTag').textContent") == '미확인 2', '%.3f · %s' % (asp, J(ed, "document.getElementById('aiTag').textContent")))
        errs(ed, '확인')

        print('== g 줄 누르기 → 그 후보를 잘 보는 사진')
        no0 = J(ed, "document.getElementById('shotNo').textContent")
        r = J(ed, "(() => { const b = [...document.querySelectorAll('#aiList .aiRow')].find((x) => x.dataset.id === 'as1').querySelector('.aiGo'); b.scrollIntoView({block: 'nearest'}); const q = b.getBoundingClientRect(); return [q.left + 30, q.top + q.height / 2]; })()")
        inp.click(r[0], r[1])
        ok = ed.wait_js("document.getElementById('shotNo').textContent.indexOf('2 /') === 0", 15)
        ed.frames(5)
        report('g as1 (사진 0002) → 사진 2 로 · 고른 줄', ok and J(ed, '__rv360.ai.st().sel') == 'as1' and J(ed, "!!document.querySelector('#aiList .aiRow.sel')"),
               '%s → %s' % (no0, J(ed, "document.getElementById('shotNo').textContent")))
        view_png(ed, os.path.join(SD, '05_후보로_이동.png'))
        errs(ed, '이동')

        print('== b 브라우저 (웹 공유 = 보기만)')
        report('b 불러옴', pg.load(False, V_A, 'area'))
        cat_open(ed, inp, 'hd_ai')
        ed.frames(3)
        report('b 목록 5 줄 · 맞음/아님 단추 없음', J(ed, "document.querySelectorAll('#aiList .aiRow').length === 5 && !document.querySelector('#aiList .aiOk')"))
        shot(ed, SD, '06_브라우저.png')
        errs(ed, '브라우저')
    finally:
        ed.__exit__(None, None, None)
    print('그림:', SD)
    print('전체:', '통과' if R['ok'] else '실패 %d' % len(R['fails']))
    for f in R['fails']:
        print('  실패 —', f)
    return 0 if R['ok'] else 1


if __name__ == '__main__':
    sys.exit(main())
