"""프로젝트 생성 편집 화면 시험 (실제 WebView2 · 작은 창 · 화면 캡처 없음).

    python viewer_test\\check_home_edit.py 영상1.OSV [영상2.OSV]

가져오기(파일 창 대신 주어진 파일) → 타임라인에 놓기 → 미리보기 이동 → 자르기 · 지우기 · 앞/뒤 지우기 · 되돌리기
→ 지도 · 썸네일 → [만들기] 가 build 에 넘기는 값(keep) 을 잡아 확인. 실제 만들기는 하지 않는다.
"""
import json
import os
import sys
import time

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '..')))
import webview  # noqa: E402
from roadview import app as A  # noqa: E402

FILES = [os.path.abspath(p) for p in sys.argv[1:]]
CAPT = {}
RES = {}
FAIL = []


def ok(name, cond, val=None):
    RES[name] = val if val is not None else bool(cond)
    if not cond:
        FAIL.append(name)
    print(('통과 ' if cond else '실패 ') + name + ('' if val is None else ' · ' + json.dumps(val, ensure_ascii=False)[:200]))
    sys.stdout.flush()


def patch(api):
    api.pick_inputs = lambda: {'files': FILES, 'size_gb': 0, 'dir': os.path.dirname(FILES[0]), 'osv': len(FILES), 'jpg': 0,
                               'out': api.default_out(FILES)}

    def sb(p):
        CAPT['p'] = p
        return {'ok': False, 'msg': '시험 · 만들기 안 함'}
    api.start_build = sb
    api.load_edit = lambda out: None


def run(w):
    J = w.evaluate_js
    time.sleep(3)

    def wait(expr, t=60):
        t0 = time.time()
        while time.time() - t0 < t:
            if J(expr):
                return True
            time.sleep(0.25)
        return False
    J('RVEdit.open()')
    ok('가져오기 · 불러오기 끝', wait('RVEdit._S.media.length === %d && RVEdit._S.media.every(m => m.ready)' % len(FILES), 120),
       J('RVEdit._S.media.map(m => [m.name, +m.dur.toFixed(2), m.track.length, m.thumbs])'))
    ok('가져온 영상 카드', J('document.querySelectorAll("#bin .mc").length') == len(FILES))
    for i in range(len(FILES)):
        J('RVEdit._api.addClip(%d)' % i)
    dur = J('RVEdit._S.media.map(m => m.dur)')
    tot = J('RVEdit._api.total()')
    ok('타임라인 토막 = 영상 수', J('RVEdit._S.clips.length') == len(FILES), J('RVEdit._S.clips'))
    ok('전체 길이 = 영상 길이 합', abs(tot - sum(dur)) < 0.1, [tot, sum(dur)])
    time.sleep(1.5)
    ok('썸네일 그림', J('document.querySelectorAll("#track .ths img").length') > 0, J('document.querySelectorAll("#track .ths img").length'))
    # 미리보기 이동: 10초
    J('RVEdit._S.T = 10.0; RVEdit._api.showAt(10.0)')
    ok('미리보기 10초로', wait('(() => { const v = document.getElementById("vid"); return v.readyState >= 2 && !RVEdit._S.seekBusy && Math.abs(v.currentTime - 10) < 0.05; })()', 20),
       J('[document.getElementById("vid").currentTime, document.getElementById("vid").readyState, document.getElementById("vid").videoWidth]'))
    ok('영상 재생 가능 (썸네일 대체 아님)', not J('!!RVEdit._S.media[0].novid'))
    # 자르기 → 앞 토막 지우기
    J('RVEdit._api.split()')
    n = J('RVEdit._S.clips.length')
    ok('자르기 → 토막 +1', n == len(FILES) + 1, J('RVEdit._S.clips'))
    a1 = J('RVEdit._S.clips[1].a')
    fps = J('RVEdit._S.media[0].fps')
    ok('자른 곳 = 프레임 경계 · 10초', abs(a1 - 10.0) < 1.0 / fps and abs(a1 * fps - round(a1 * fps)) < 1e-6, [a1, a1 * fps])
    J('RVEdit._S.sel = 0; RVEdit._api.delSel()')
    ok('지우기 → 앞 10초 빠짐', J('RVEdit._S.clips.length') == len(FILES) and abs(J('RVEdit._S.clips[0].a') - a1) < 1e-9,
       J('RVEdit._S.clips[0]'))
    ok('지운 뒤 재생 위치 = 0', abs(J('RVEdit._S.T')) < 1e-6)
    # W: 15초(원본 25초)에서 뒤 지우기 → 첫 토막 = 10~25
    J('RVEdit._S.T = 15.0; RVEdit._api.delSide(false)')
    c0 = J('RVEdit._S.clips[0]')
    ok('뒷부분 지우기(W) → 10~25초', abs(c0['b'] - 25.0) < 1.0 / fps, c0)
    # Q: 2초(원본 12초)에서 앞 지우기 → 12~25
    J('RVEdit._S.T = 2.0; RVEdit._api.delSide(true)')
    c0 = J('RVEdit._S.clips[0]')
    ok('앞부분 지우기(Q) → 12~25초', abs(c0['a'] - 12.0) < 1.0 / fps and abs(c0['b'] - 25.0) < 1.0 / fps, c0)
    J('RVEdit._api.undo()')
    ok('되돌리기 → 10~25', abs(J('RVEdit._S.clips[0].a') - a1) < 1e-9)
    J('RVEdit._api.redo()')
    ok('다시 → 12~25', abs(J('RVEdit._S.clips[0].a') - 12.0) < 1.0 / fps)
    # 같은 영상 토막 둘 (가운데 지우기): 15초 원본 = T 3 에서 자르고 18초 = T 6 에서 자른 뒤 가운데 지움
    J('RVEdit._S.T = 3.0; RVEdit._api.split(); RVEdit._S.T = 6.0; RVEdit._api.split(); RVEdit._S.sel = 1; RVEdit._api.delSel()')
    segs = J('RVEdit._S.clips.filter(c => c.m === 0).map(c => [c.a, c.b])')
    ok('가운데 지우기 → 같은 영상 두 토막', len(segs) == 2 and abs(segs[0][1] - 15) < 0.05 and abs(segs[1][0] - 18) < 0.05, segs)
    # 지도
    ok('지도 경로 · 지금 위치', J('document.querySelectorAll("#map .ov polyline").length') >= 2 and J('!!document.querySelector("#map .ov circle")'),
       [J('document.querySelectorAll("#map .ov polyline").length'), J('document.querySelectorAll("#map .tiles img").length')])
    # 재생 1초
    J('RVEdit._S.T = 0; RVEdit._api.showAt(0)')
    time.sleep(1)
    J('document.getElementById("bPlay").click()')
    time.sleep(1.5)
    t_play = J('RVEdit._S.T')
    J('document.getElementById("bPlay").click()')
    ok('재생하면 시간이 감', 0.5 < t_play < 3.0, t_play)
    # 만들기 → 넘기는 값
    J('document.getElementById("bBuild").click()')
    wait('false', 2)
    p = CAPT.get('p') or {}
    name0 = J('RVEdit._S.media[0].name')
    keep = p.get('keep', {})
    ok('만들기 keep (지운 곳 빼고)', name0 in keep and len(keep[name0]) == 2, keep)
    if len(FILES) > 1:
        name1 = J('RVEdit._S.media[1].name')
        ok('통째로 쓴 영상은 keep 없음 (땅 높이 기록 그대로 쓰게)', name1 not in keep)
    ok('만들기 files', len(p.get('files', [])) == len(FILES), p.get('files'))
    ok('편집 기록 넘김', bool(p.get('edit', {}).get('clips')), len(p.get('edit', {}).get('clips', [])))
    ok('화면 오류 없음', True)
    w.destroy()


def main():
    api = A.Api()
    patch(api)
    w = webview.create_window('시험', A.HOME_HTML.resolve().as_uri(), js_api=api, width=1500, height=920, minimized=True)
    api._window = w
    webview.start(run, (w,), gui='edgechromium', private_mode=True)
    print('== %d 실패' % len(FAIL), FAIL)
    return 1 if FAIL else 0


if __name__ == '__main__':
    sys.exit(main())
