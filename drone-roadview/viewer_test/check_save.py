"""저장 시험 (JACK 1127 «작업 끝나고 [저장] 해야 저장 · 튜토리얼은 [저장] = 일반 프로젝트로 · 3D · 360 같게»).

    python -X utf8 -I check_save.py [--shots <그림 폴더>] [--only w3,w360,p]

  자료 = 저장소 tutorial\\3D_매핑_튜토리얼 · 360_로드뷰_튜토리얼 의 복사본 (임시 폴더 · 원본 안 바꿈) + hostmock.js + 저장 흉내 (아래 SAVE_MOCK).
  w3   3D: 연 뒤 [저장] = 깨끗 · save_* 0 · 터파기 설정 · 부지 → 1.6 초 뒤에도 save_* 0 · 이 브라우저 기억 안 함 · 단추 = 파랑 (저장 안 함)
       · [처음] → 묻는 창 · Esc = 안 나감 · Ctrl+S → save_calib · save_grade 한 번씩 → mark_saved · 깨끗
       · [처음] → [저장] → 저장 뒤 go_home · [처음] → [저장 안 함] → 안 쓰고 go_home · 창 × (__rvClose) = 깨끗하면 바로 close_app · 고쳤으면 묻고 저장 뒤
       · SHP 내보내기 (프로그램이 읽는 일) = 모은 것 먼저 · 튜토리얼 = [저장] → 이름 · 위치 창 → [위치 바꾸기] → save_as {name, parent, open}
  w360 360: 맞춤 값 → 1.6 초 뒤에도 save_calib 0 · 이 브라우저 기억 안 함 · [위치 보정] = 전부 쓰기 · [처음] → [저장 안 함] · 옛 기억 남아 있으면 지우고 다시 엶
  p    app.py (가짜 창 · 임시 LOCALAPPDATA · APPDATA · 작은 가짜 튜토리얼): open_tutorial = 열 때마다 처음 상태 (고친 파일 되돌림 · 새 파일 지움) · 짝 ·
       최근 목록 안 넣음 · project_info · save_as (짝 · tutorial.json 빼고 · AI 짝 · src360 · 최근 목록 맨 위 · 같은 이름 _2) · 옛 기록 숨김 · 창 × 물음
  모든 쪽 JS 오류 0
"""
import argparse
import json
import os
import shutil
import sys
import tempfile
import threading
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from cdp import Edge  # noqa: E402

R = {'ok': True, 'fails': [], 'errs': []}

# hostmock.js 뒤 · 보기 화면 앞: 저장 쪽 API 흉내 (?tut=1 = 튜토리얼)
SAVE_MOCK = r"""(function () {
  const H = window.__host, api = window.pywebview.api, wait = (ms) => new Promise((r) => setTimeout(r, ms));
  const rec = (n, a) => H.calls.push({ name: n, arg: a, t: performance.now() });
  const tut = new URLSearchParams(location.search).get('tut') === '1';
  H.info = { path: 'C:\\시험\\p', tutorial: tut, title: 'p', opened: tut ? 2 : 1, name: 'p_20261127_1200', parent: 'C:\\문서\\DH 에어로사이트' };
  api.project_info = () => { rec('project_info'); return wait(5).then(() => H.info); };
  api.mark_saved = () => { rec('mark_saved'); return wait(5).then(() => ({ ok: true })); };
  api.save_as = (o) => { rec('save_as', o); return wait(20).then(() => ({ ok: true, path: o.parent + '\\' + o.name, pair: '' })); };
  api.close_app = () => { rec('close_app'); return wait(5).then(() => ({ ok: true })); };
  api.pick_folder = () => { rec('pick_folder'); return wait(5).then(() => 'D:\\새 위치'); };
  api.save_grade = (t) => { rec('save_grade', t); return wait(10).then(() => ({ ok: true })); };
  api.ai_save = (p, d) => { rec('ai_save', { project: p }); return wait(10).then(() => ({ ok: true })); };
})();
"""
SAVES = "__host.calls.filter((c) => /^(save_pipes|save_grade|save_calib|ai_save)$/.test(c.name))"


def report(name, ok, text=''):
    print('   %-50s %s  %s' % (name, text, '통과' if ok else '실패'))
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


def names(ed):
    return J(ed, "__host.calls.map((c) => c.name).filter((n) => !/^(job|crop_get|nonground_get|list_shares|project_info|ai_status)$/.test(n))")


def n_of(ed, name):
    return J(ed, "__host.calls.filter((c) => c.name === '%s').length" % name)


def key(ed, k, code, ctrl=False):
    ed.js("window.dispatchEvent(new KeyboardEvent('keydown', { key: '%s', code: '%s', ctrlKey: %s, bubbles: true }))" % (k, code, 'true' if ctrl else 'false'))


def dlg_open(ed):
    return ed.wait_js("!document.getElementById('svBack').hidden", 5)


def page_dir(src, dst, html, js, extra):
    """튜토리얼 복사본 + 이 저장소 보기 화면 + hostmock + 저장 흉내 → index_host.html."""
    shutil.copytree(src, dst)
    os.makedirs(os.path.join(dst, 'viewer'), exist_ok=True)
    shutil.copy2(os.path.join(REPO, 'viewer', html), os.path.join(dst, 'index.html'))
    for f in [js, 'river.js'] + extra:
        shutil.copy2(os.path.join(REPO, 'viewer', f), os.path.join(dst, 'viewer', f))
    shutil.copy2(os.path.join(HERE, 'hostmock.js'), os.path.join(dst, 'viewer', 'hostmock.js'))
    open(os.path.join(dst, 'viewer', 'save_mock.js'), 'w', encoding='utf-8').write(SAVE_MOCK)
    h = open(os.path.join(dst, 'index.html'), encoding='utf-8').read()
    tag = '<script src="viewer/%s"></script>' % js
    assert tag in h
    open(os.path.join(dst, 'index_host.html'), 'w', encoding='utf-8').write(
        h.replace(tag, '<script src="viewer/hostmock.js"></script>\n<script src="viewer/save_mock.js"></script>\n' + tag))


# ---------------------------------------------------------------- w3: 3D 보기
def check_3d(ed, base, shots):
    print('== w3  3D 보기 [저장]')
    ed.goto(base + 'index_host.html')
    ok = ed.wait_js(Edge.NEW + " && window.__map3d && window.__map3d.trSet && window.__map3d.gd", 120)
    report('열림', ok)
    time.sleep(1.6)
    report('연 뒤 save_* 0 · [저장] 보임 · 깨끗', J(ed, SAVES + '.length') == 0 and J(ed, "!document.getElementById('bSave').hidden && !document.getElementById('bSave').classList.contains('dirty')"),
           '%d' % J(ed, SAVES + '.length'))
    ed.js("__map3d.trSet({ tp: 0.35 })")
    ed.js("(() => { const o = RV_MAPPING.origin; return __map3d.gd.add({ name: '시험', pts: [[o[0] - 6, o[1] - 6], [o[0] + 6, o[1] - 6], [o[0] + 6, o[1] + 6], [o[0] - 6, o[1] + 6]] }); })()")
    time.sleep(1.6)
    tk = "'rv_trench:' + String(RV_MAPPING.title || 'project') + ':3d'"
    report('고침 → 1.6 초 뒤에도 save_* 0 · 단추 = 저장 안 함', J(ed, SAVES + '.length') == 0 and J(ed, "document.getElementById('bSave').classList.contains('dirty')"),
           '%d' % J(ed, SAVES + '.length'))
    report('이 브라우저 기억 안 함 (터파기 설정)', J(ed, 'localStorage.getItem(%s)' % tk) is None)
    if shots:
        ed.frames(2)
        ed.screenshot(os.path.join(shots, 'save_3d_topbar_dirty.png'))
    ed.js("document.getElementById('bHome').click()")
    ok = dlg_open(ed)
    if shots and ok:
        ed.frames(2)
        ed.screenshot(os.path.join(shots, 'save_3d_ask.png'))
    report('[처음] → 묻는 창 (저장 안 한 변경 · 저장 안 함 · 저장)', ok and J(ed, "document.getElementById('svHead').textContent") == '저장 안 한 변경'
           and J(ed, "!document.getElementById('svNo').hidden && document.getElementById('svAs').hidden"))
    key(ed, 'Escape', 'Escape')
    time.sleep(0.3)
    report('Esc = 취소 (안 나감 · 안 씀)', J(ed, "document.getElementById('svBack').hidden") and n_of(ed, 'go_home') == 0 and J(ed, SAVES + '.length') == 0)
    key(ed, 's', 'KeyS', ctrl=True)
    ok = ed.wait_js("__host.calls.some((c) => c.name === 'mark_saved')", 5)
    cal = J(ed, "__host.calls.filter((c) => c.name === 'save_calib').map((c) => c.arg)")
    report('Ctrl+S → save_calib · save_grade 한 번씩 → mark_saved', ok and len(cal) == 1 and n_of(ed, 'save_grade') == 1 and '"tp": 0.35' in cal[0],
           json.dumps(names(ed), ensure_ascii=False))
    report('저장 뒤 깨끗 · 알림 «저장됨»', J(ed, "!document.getElementById('bSave').classList.contains('dirty')") and '저장됨' in J(ed, "document.getElementById('toast').textContent"))
    # [처음] → [저장]
    ed.js("__map3d.trSet({ tp: 0.4 })")
    ed.js("document.getElementById('bHome').click()")
    dlg_open(ed)
    ed.js("document.getElementById('svOk').click()")
    ok = ed.wait_js("__host.calls.some((c) => c.name === 'go_home')", 5)
    nm = names(ed)
    report('[처음] → [저장] → save_calib 뒤 go_home', ok and n_of(ed, 'save_calib') == 2 and nm.index('go_home') > len(nm) - 1 - nm[::-1].index('save_calib'), json.dumps(nm[-4:], ensure_ascii=False))
    # [처음] → [저장 안 함]
    ed.js("__map3d.trSet({ tp: 0.45 })")
    ed.js("document.getElementById('bHome').click()")
    dlg_open(ed)
    ed.js("document.getElementById('svNo').click()")
    ok = ed.wait_js("__host.calls.filter((c) => c.name === 'go_home').length === 2", 5)
    report('[처음] → [저장 안 함] → 안 쓰고 go_home · 깨끗', ok and n_of(ed, 'save_calib') == 2 and J(ed, "!document.getElementById('bSave').classList.contains('dirty')"))
    # 창 × (app.py closing → __rvClose)
    r = ed.js("window.__rvClose()")
    ok = ed.wait_js("__host.calls.some((c) => c.name === 'close_app')", 5)
    report('창 × · 고친 것 없음 → 바로 close_app', r == 'wait' and ok and J(ed, "document.getElementById('svBack').hidden"))
    ed.js("__map3d.trSet({ tp: 0.5 })")
    ed.js("window.__rvClose()")
    ok = dlg_open(ed)
    r2 = ed.js("window.__rvClose()")                                # × 를 또 누름 = 같은 창 그대로
    ed.js("document.getElementById('svOk').click()")
    ok2 = ed.wait_js("__host.calls.filter((c) => c.name === 'close_app').length === 2", 5)
    report('창 × · 고침 → 묻기 → [저장] → save_calib → close_app', ok and ok2 and r2 == 'wait' and n_of(ed, 'save_calib') == 3)
    # 프로그램이 읽는 일 전 = 모은 것 먼저
    ed.js("__map3d.trSet({ tp: 0.55 })")
    time.sleep(1.2)
    ed.js("document.getElementById('bShpOut').click()")
    ok = ed.wait_js("__host.calls.some((c) => c.name === 'export_pipes_shp')", 5)
    nm = names(ed)
    last_cal = len(nm) - 1 - nm[::-1].index('save_calib') if 'save_calib' in nm else -1
    report('SHP 내보내기 전 모은 것 먼저 (save_calib → export_pipes_shp)', ok and n_of(ed, 'save_calib') == 4 and 0 <= last_cal < nm.index('export_pipes_shp'),
           json.dumps(nm[-3:], ensure_ascii=False))
    errs(ed, '3D')
    # 튜토리얼
    print('== w3  3D 튜토리얼 [저장] = 일반 프로젝트로')
    ed.goto(base + 'index_host.html?tut=1')
    ed.wait_js(Edge.NEW + " && window.__map3d && window.__map3d.trSet && window.__host.calls.some((c) => c.name === 'project_info')", 120)
    time.sleep(0.5)
    report('튜토리얼 · 연 뒤 깨끗 · 단추 글 «일반 프로젝트로 저장»', J(ed, "!document.getElementById('bSave').classList.contains('dirty')") and '일반 프로젝트로' in J(ed, "document.getElementById('bSave').title"))
    ed.js("__map3d.trSet({ tp: 0.6 })")
    ed.js("document.getElementById('bSave').click()")
    ok = dlg_open(ed)
    if shots and ok:
        ed.frames(2)
        ed.screenshot(os.path.join(shots, 'save_3d_tutorial_saveas.png'))
    report('[저장] → 이름 · 위치 창 (기본 이름 · 문서\\DH 에어로사이트)', ok and J(ed, "!document.getElementById('svAs').hidden && document.getElementById('svNo').hidden")
           and J(ed, "document.getElementById('svName').value") == 'p_20261127_1200' and J(ed, "document.getElementById('svDir').textContent").endswith('DH 에어로사이트'))
    ed.js("document.getElementById('svPick').click()")
    ed.wait_js("document.getElementById('svDir').textContent === 'D:\\\\새 위치'", 3)
    ed.js("document.getElementById('svName').value = '내 시험'")
    ed.js("document.getElementById('svOk').click()")
    ok = ed.wait_js("__host.calls.some((c) => c.name === 'save_as')", 5)
    sa = J(ed, "__host.calls.filter((c) => c.name === 'save_as').map((c) => c.arg)")
    nm = names(ed)
    report('save_as {name, parent = 고른 위치, open} · 그 전에 save_calib', ok and sa and sa[0] == {'name': '내 시험', 'parent': 'D:\\새 위치', 'open': True}
           and nm.index('save_calib') < nm.index('save_as') and 'mark_saved' not in nm, json.dumps(sa, ensure_ascii=False))
    errs(ed, '3D 튜토리얼')


# ---------------------------------------------------------------- w360: 360 보기
def check_360(ed, base, shots):
    print('== w360  360 보기 [저장]')
    ed.goto(base + 'index_host.html')
    ok = ed.wait_js(Edge.NEW + " && window.__rv360 && document.getElementById('calSave') && window.RV_PROJECT", 120)
    report('열림', ok)
    time.sleep(1.6)
    report('연 뒤 save_* 0 · [저장] 보임 · 깨끗', J(ed, SAVES + '.length') == 0 and J(ed, "!document.getElementById('hSave').hidden && !document.getElementById('hSave').classList.contains('dirty')"))
    bump = ("(() => { const i = document.querySelector('#calRows input[type=number]'); if (!i) return false; "
            "i.value = String(Math.round((Number(i.value || 0) + %s) * 1000) / 1000); i.dispatchEvent(new Event('input', { bubbles: true })); return true; })()")
    ck = "'rv_calib:' + String(RV_PROJECT.title || 'project')"
    report('맞춤 값 고침', ed.js(bump % '0.5'))
    time.sleep(1.6)
    report('1.6 초 뒤에도 save_calib 0 · 단추 = 저장 안 함 · 이 브라우저 기억 안 함', n_of(ed, 'save_calib') == 0 and J(ed, "document.getElementById('hSave').classList.contains('dirty')")
           and J(ed, 'localStorage.getItem(%s)' % ck) is None)
    if shots:
        ed.frames(2)
        ed.screenshot(os.path.join(shots, 'save_360_topbar_dirty.png'))
    ed.js("document.getElementById('calSave').click()")
    ok = ed.wait_js("__host.calls.some((c) => c.name === 'mark_saved')", 5)
    report('[위치 보정] = 전부 쓰기 → save_calib 한 번 → mark_saved · 깨끗', ok and n_of(ed, 'save_calib') == 1 and J(ed, "!document.getElementById('hSave').classList.contains('dirty')"))
    ed.js(bump % '0.5')
    ed.js("document.getElementById('hHome').click()")
    ok = dlg_open(ed)
    if shots and ok:
        ed.frames(2)
        ed.screenshot(os.path.join(shots, 'save_360_ask.png'))
    ed.js("document.getElementById('svNo').click()")
    ok2 = ed.wait_js("__host.calls.some((c) => c.name === 'go_home')", 5)
    report('[처음] → 묻기 → [저장 안 함] → 안 쓰고 go_home', ok and ok2 and n_of(ed, 'save_calib') == 1)
    ed.js(bump % '0.5')
    ed.js("document.getElementById('hSave').click()")
    ok = ed.wait_js("__host.calls.filter((c) => c.name === 'save_calib').length === 2", 5)
    report('윗줄 [저장] → save_calib', ok)
    errs(ed, '360')
    # 옛 판이 이 브라우저에 남긴 맞춤 → 지우고 다시 엶 (파일 값)
    ed.js("localStorage.setItem(%s, JSON.stringify({ videos: { x: { dx: 9 } } }))" % ck)
    ed.goto(base + 'index_host.html')
    ok = ed.wait_js(Edge.NEW + " && window.__rv360 && window.RV_PROJECT", 60)
    time.sleep(2.5)
    report('옛 기억 → 지우고 다시 엶 (한 번)', ok and ed.wait_js("window.__rv360 && localStorage.getItem(%s) === null" % ck, 30))
    errs(ed, '360 옛 기억')


# ---------------------------------------------------------------- p: app.py
class FakeWin:
    def __init__(self):
        self.urls, self.js, self.destroyed, self.answer = [], [], 0, 'wait'

    def load_url(self, u):
        self.urls.append(u)

    def evaluate_js(self, s):
        self.js.append(s)
        return self.answer

    def destroy(self):
        self.destroyed += 1

    def create_file_dialog(self, *a, **k):
        return None


def check_py(tmp):
    print('== p  app.py 튜토리얼 · save_as')
    os.environ['LOCALAPPDATA'] = os.path.join(tmp, 'local')
    os.environ['APPDATA'] = os.path.join(tmp, 'roam')
    sys.path.insert(0, REPO)
    from roadview import app, ai_budae
    report('설정 폴더 = 임시', app.TUT_USER.as_posix().startswith(tmp.replace('\\', '/')) and str(ai_budae.LINKS).startswith(tmp))
    src = os.path.join(tmp, 'tut')
    for k, kind, pair in (('T3D', 'mapping', 'T360'), ('T360', '360', 'T3D')):
        d = os.path.join(src, k, 'data')
        os.makedirs(d)
        open(os.path.join(src, k, 'index.html'), 'w', encoding='utf-8').write('<html></html>')
        json.dump({'title': k, 'ver': '1', 'pair': pair}, open(os.path.join(src, k, 'tutorial.json'), 'w', encoding='utf-8'))
        if kind == 'mapping':
            open(os.path.join(d, 'mapping.js'), 'w', encoding='utf-8').write('window.RV_MAPPING = {"title": "3D 시험", "photos": 3};\n')
            open(os.path.join(d, 'dem.js'), 'w', encoding='utf-8').write('window.RV_DEM = null;\n')
        else:
            open(os.path.join(d, 'project.js'), 'w', encoding='utf-8').write('window.RV_PROJECT = {"title": "360 시험", "shots": []};\n')
        open(os.path.join(d, 'pipes.js'), 'w', encoding='utf-8').write('window.RV_PIPES = {"lines": []};\n')
        json.dump({'made': 'm1', 'features': [], 'src360': 'X'}, open(os.path.join(d, 'ai_budae.json'), 'w', encoding='utf-8'))
    app._tut_dir = lambda: __import__('pathlib').Path(src)
    api = app.Api()
    w = api._window = FakeWin()
    r = api.open_tutorial('T3D')
    t3, t36 = app.TUT_USER / 'T3D', app.TUT_USER / 'T360'
    report('open_tutorial → 열림 · 짝도 복사', r.get('ok') and (t3 / 'index.html').exists() and (t36 / 'data' / 'project.js').exists() and w.urls, json.dumps(r, ensure_ascii=False))
    report('최근 목록에 안 넣음', not [x for x in api._load_list() if 'tutorial' in x['path'].replace('\\', '/').lower()], str(len(api._load_list())))
    report('AI 짝 · 3D src360 = 360 작업 복사본', ai_budae.twin(str(t3)) and os.path.normcase(ai_budae.twin(str(t3))) == os.path.normcase(str(t36))
           and ai_budae.load(str(t3)).get('src360') == str(t36))
    # 튜토리얼에서 고침 (파이썬이 바로 쓴 것 · 새 파일) → 다시 열면 처음 상태
    (t3 / 'data' / 'pipes.js').write_text('window.RV_PIPES = {"lines": [1]};\n', encoding='utf-8')
    (t3 / 'data' / 'grade.js').write_text('window.RV_GRADE = {};\n', encoding='utf-8')
    (t36 / 'data' / 'new').mkdir()
    (t36 / 'data' / 'new' / 'x.txt').write_text('x', encoding='utf-8')
    api.go_home()
    r = api.open_tutorial('T3D')
    report('다시 열면 처음 상태 (고친 파일 되돌림 · 새 파일 · 폴더 지움)', r.get('ok') and (t3 / 'data' / 'pipes.js').read_text(encoding='utf-8') == 'window.RV_PIPES = {"lines": []};\n'
           and not (t3 / 'data' / 'grade.js').exists() and not (t36 / 'data' / 'new').exists())
    info = api.project_info()
    report('project_info = 튜토리얼 · 기본 이름 · 위치', info['tutorial'] and info['name'].startswith('T3D_') and info['parent'].endswith(app.APP_NAME) and info['opened'] > 0,
           json.dumps(info, ensure_ascii=False)[:160])
    out = os.path.join(tmp, 'out')
    (t3 / 'data' / 'pipes.js').write_text('window.RV_PIPES = {"lines": [2]};\n', encoding='utf-8')     # 화면이 [저장] 으로 먼저 쓴 것
    (t3 / '.tutorial_ver').write_text('1', encoding='utf-8')
    r = api.save_as({'name': '내 시험', 'parent': out, 'open': True})
    d, pd = os.path.join(out, '내 시험'), os.path.join(out, '내 시험_360')
    report('save_as → 부모\\이름 · 짝 = 이름_360 · 고친 것 들어감', r.get('ok') and r['path'] == d and r['pair'] == pd
           and open(os.path.join(d, 'data', 'pipes.js'), encoding='utf-8').read().endswith('[2]};\n') and os.path.exists(os.path.join(pd, 'data', 'project.js')), json.dumps(r, ensure_ascii=False))
    report('tutorial.json · .tutorial_ver 뺌', not any(os.path.exists(os.path.join(x, f)) for x in (d, pd) for f in ('tutorial.json', '.tutorial_ver')))
    report('AI 짝 새로 · src360 = 새 360', os.path.normcase(ai_budae.twin(d) or '') == os.path.normcase(pd) and ai_budae.load(d).get('src360') == pd)
    L = api._load_list()
    report('최근 목록 맨 위 = 저장한 것 · 다음 = 짝', len(L) >= 2 and os.path.normcase(L[0]['path']) == os.path.normcase(d) and os.path.normcase(L[1]['path']) == os.path.normcase(pd),
           ' · '.join(os.path.basename(x['path']) for x in L[:3]))
    report('그 프로젝트로 다시 엶', os.path.normcase(api._project) == os.path.normcase(d) and w.urls[-1].endswith('index.html') and not api.project_info()['tutorial'])
    r2 = api.save_as({'name': '내 시험', 'parent': out})
    report('일반 프로젝트에서 save_as = 거절', r2.get('ok') is False)
    api.open_tutorial('T3D')
    r3 = api.save_as({'name': '내 시험', 'parent': out, 'open': False})
    report('같은 이름 → _2 · open False = 안 엶 (튜토리얼 그대로)', r3.get('ok') and r3['path'].endswith('내 시험_2') and app._is_tut(api._project), r3.get('path', ''))
    r4 = api.save_as({'parent': str(app.TUT_USER)})
    report('튜토리얼 폴더 안 저장 = 거절', r4.get('ok') is False)
    # 옛 판이 넣은 튜토리얼 기록 → 목록에서 숨김
    lst = api._load_list()
    lst.insert(0, {'path': str(t3), 'title': 'T3D', 'updated': '2026-11-27 10:00'})
    api._save_list(lst)
    report('옛 튜토리얼 기록 숨김 (list_projects)', not [x for x in api.list_projects() if app._is_tut(x['path'])] and 'tutorial' not in api.list_projects()[0])
    api.mark_saved()
    report('mark_saved (튜토리얼) = 목록에 안 넣음', len([x for x in api._load_list() if app._is_tut(x['path'])]) == 1)
    # 창 × : 보기 화면이면 화면에 물음 (wait = 화면이 close_app) · 답 none = 닫음 · 처음 화면 = 바로
    w.answer = 'wait'
    c1 = api._on_closing()
    time.sleep(0.4)
    report('창 × (보기 화면) = 막고 화면에 물음', c1 is False and w.js and '__rvClose' in w.js[-1] and w.destroyed == 0)
    w.answer = 'none'
    c2 = api._on_closing()
    time.sleep(0.4)
    report('화면 답 none → 닫음 · 그 뒤 × = 바로', c2 is False and w.destroyed == 1 and api._on_closing() is True)
    api._close_ok = False
    api._ask_ans, api._ask_t = False, time.time() - 6
    report('화면이 5 초 넘게 답 없음 → 다음 × = 닫음', api._on_closing() is True)
    api._ask_ans = True
    api._project = None
    report('처음 화면 × = 바로 닫음', api._on_closing() is True)
    _ = threading


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--shots', default='')
    ap.add_argument('--only', default='w3,w360,p')
    a = ap.parse_args()
    only = set(a.only.split(','))
    if a.shots:
        os.makedirs(a.shots, exist_ok=True)
    tmp = tempfile.mkdtemp(prefix='rv_save_')
    try:
        if 'p' in only:
            check_py(tmp)
        web = only & {'w3', 'w360'}
        if web:
            import check_map3d_ug as ug
            root = os.path.join(tmp, 'web')
            os.makedirs(root)
            if 'w3' in only:
                page_dir(os.path.join(REPO, 'tutorial', '3D_매핑_튜토리얼'), os.path.join(root, 'p3'), 'map3d.html', 'map3d.js', [])
            if 'w360' in only:
                page_dir(os.path.join(REPO, 'tutorial', '360_로드뷰_튜토리얼'), os.path.join(root, 'p360'), 'index.html', 'app.js', ['style.css'])
            srv, base = ug.serve(root)
            with Edge(os.path.join(tmp, 'edge'), size=(1600, 900)) as ed:
                if 'w3' in only:
                    check_3d(ed, base + 'p3/', a.shots)
                if 'w360' in only:
                    check_360(ed, base + 'p360/', a.shots)
            srv.shutdown()
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    print('\n결과: %s' % ('모두 통과' if R['ok'] else '실패 %d · %s' % (len(R['fails']), ' / '.join(R['fails']))))
    for w, e in R['errs'][:10]:
        print('   JS 오류 [%s] %s' % (w, e))
    sys.exit(0 if R['ok'] else 1)


if __name__ == '__main__':
    main()
