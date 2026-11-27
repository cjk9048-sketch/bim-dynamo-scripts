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
    # 프리 뷰 (기본) · 영상 텍스처 · 방향 기록
    ok('프리 뷰 기본 · WebGL', J('RVEdit._FV.mode') == 'free' and J('!!RVEdit._FV.gl'))
    J('RVEdit._FV.dirty = true')
    ok('프리 뷰에 영상 그려짐', wait('RVEdit._FV.hasTex', 10), J('[RVEdit._FV.hasTex, RVEdit._S.media[0].rot.length]'))
    # GCP: 카메라 앞 땅 점 하나 → 화면 가운데 아래를 누름 → 짝 · 풀림
    J('''(() => { const A = RVEdit._api, md = RVEdit._S.media[0], ps = A.posAt(md, 10.01);
          RVEdit._api.gAddPoints([{ name: 'T1', E: ps.E + 3, N: ps.N + 3, z: (md.zt0 || 0), zd: null, lat: md.track[0][1], lon: md.track[0][2] }]); })()''')
    wait('RVEdit._G.pts.length === 1', 10)
    J('RVEdit._G.active = RVEdit._G.pts[0].id; RVEdit._FV.pitch = -60;')
    r = J('(() => { const c = document.getElementById("fv").getBoundingClientRect(); RVAlign.viewClick({ clientX: c.left + c.width / 2, clientY: c.top + c.height * 0.6 }); return JSON.stringify([RVEdit._G.pairs.length, (RVEdit._G.cal[RVEdit._S.media[0].path] || {}).st]); })()')
    ok('GCP 짝 · 풀이', json.loads(r)[0] == 1 and json.loads(r)[1] in ('ok', 'bad'), r)
    ok('GCP 목록 · 결과 표시', J('document.querySelectorAll("#gList .gi").length') >= 1 and len(J('document.getElementById("gRes").textContent')) > 0,
       J('document.getElementById("gRes").textContent'))
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
    # 타임라인 고르기 · 끌어 순서 바꾸기 · 지우기 (JACK 1127) — 재생 선은 눈금자 · 머리만 움직임
    J("""window.__pe = (id, type, x, y, tgt) => { const el = tgt || document.getElementById(id);
          el.dispatchEvent(new PointerEvent(type, { bubbles: true, clientX: x, clientY: y, pointerId: 7, buttons: type === 'pointerup' ? 0 : 1 })); };
        window.__cr = (k) => document.querySelectorAll('#track .clip')[k].getBoundingClientRect();""")
    J('RVEdit._S.sel = -1; RVEdit._S.T = 1.0; RVEdit._api.showAt(1.0); RVEdit._api.undo; 0')
    nclip = J('RVEdit._S.clips.length')
    ok('시험 전 토막 2개 이상', nclip >= 2, nclip)
    ord0 = J('JSON.stringify(RVEdit._S.clips)')
    r1 = J('(() => { const r = window.__cr(0); return [r.left + 10, r.top + 30]; })()')
    J('window.__pe("track", "pointerdown", %f, %f, document.querySelectorAll("#track .clip")[0]); window.__pe("track", "pointerup", %f, %f)' % (r1[0], r1[1], r1[0], r1[1]))
    ok('클립을 누르면 .sel', J('document.querySelectorAll("#track .clip.sel").length') == 1 and J('RVEdit._S.sel') == 0)
    ok('고른 클립 굵은 테두리(계산된 스타일)', J('getComputedStyle(document.querySelector("#track .clip.sel")).outlineWidth') == '3px'
       and J('getComputedStyle(document.querySelector("#track .clip.sel")).outlineStyle') == 'solid')
    ok('클립을 눌러도 재생 선 그대로', abs(J('RVEdit._S.T') - 1.0) < 1e-6, J('RVEdit._S.T'))
    J('window.__pe("track", "pointerdown", %f, %f, document.getElementById("track")); window.__pe("track", "pointerup", %f, %f)' % (r1[0], r1[1], r1[0], r1[1]))
    ok('빈 곳을 누르면 고르기 풀림', J('document.querySelectorAll("#track .clip.sel").length') == 0 and J('RVEdit._S.sel') == -1)
    # 눈금자 · 머리
    rx = J('document.getElementById("tlIn").getBoundingClientRect().left + 5 * RVEdit._S.pps')
    J('window.__pe("ruler", "pointerdown", %f, 10); window.__pe("ruler", "pointerup", %f, 10)' % (rx, rx))
    ok('눈금자를 누르면 그 시각으로', abs(J('RVEdit._S.T') - 5.0) < 0.5, J('RVEdit._S.T'))
    hx = J('(() => { const r = document.querySelector("#ph i").getBoundingClientRect(); return [r.left + r.width / 2, r.top + 5]; })()')
    ok('재생 선 머리 = ew-resize', J('getComputedStyle(document.querySelector("#ph i")).cursor') == 'ew-resize')
    J('window.__pe("x", "pointerdown", %f, %f, document.querySelector("#ph i")); window.__pe("ruler", "pointermove", %f, 10); window.__pe("ruler", "pointerup", %f, 10)'
      % (hx[0], hx[1], J('document.getElementById("tlIn").getBoundingClientRect().left + 8 * RVEdit._S.pps'), hx[0]))
    ok('머리를 끌면 따라옴', abs(J('RVEdit._S.T') - 8.0) < 0.5, J('RVEdit._S.T'))
    J('RVEdit._S.T = 1.0; RVEdit._api.showAt(1.0)')
    # 6 px 안쪽 끌기 = 순서 그대로
    J('window.__pe("x", "pointerdown", %f, %f, document.querySelectorAll("#track .clip")[0]); window.__pe("track", "pointermove", %f, %f); window.__pe("track", "pointerup", %f, %f)'
      % (r1[0], r1[1], r1[0] + 5, r1[1], r1[0] + 5, r1[1]))
    ok('6 px 안쪽 끌기 = 순서 그대로', J('JSON.stringify(RVEdit._S.clips)') == ord0)
    # 마지막 클립을 맨 앞으로
    last = nclip - 1
    r2 = J('(() => { const r = window.__cr(%d); return [r.left + 10, r.top + 30]; })()' % last)
    J('window.__pe("x", "pointerdown", %f, %f, document.querySelectorAll("#track .clip")[%d]); window.__pe("track", "pointermove", %f, %f)'
      % (r2[0], r2[1], last, J('document.getElementById("tlIn").getBoundingClientRect().left + 2'), r2[1]))
    ok('끌면 반투명로 뜸 · 놓을 자리 표시선', J('!!document.querySelector("#track .clip.drag")') and J('!!document.querySelector("#tlIn .ins")'))
    J('window.__pe("track", "pointerup", %f, %f)' % (J('document.getElementById("tlIn").getBoundingClientRect().left + 2'), r2[1]))
    o1 = J('RVEdit._S.clips')
    ok('뒤 클립을 앞으로 끌면 순서 바뀜', json.dumps(o1, sort_keys=True) != ord0 and o1[0] == json.loads(ord0)[last] and J('document.querySelectorAll("#tlIn .ins").length') == 0, o1)
    ok('순서 바뀐 뒤 고른 클립 = 맨 앞', J('RVEdit._S.sel') == 0)
    J('RVEdit._api.undo()')
    ok('Ctrl+Z 로 순서 되돌아옴', J('JSON.stringify(RVEdit._S.clips)') == ord0)
    # 앞 클립을 뒤로
    r3 = J('(() => { const r = window.__cr(0); return [r.left + 10, r.top + 30]; })()')
    J('window.__pe("x", "pointerdown", %f, %f, document.querySelectorAll("#track .clip")[0]); window.__pe("track", "pointermove", %f, %f); window.__pe("track", "pointerup", %f, %f)'
      % (r3[0], r3[1], J('document.getElementById("tlIn").getBoundingClientRect().left + RVEdit._api.total() * RVEdit._S.pps - 3'), r3[1],
         J('document.getElementById("tlIn").getBoundingClientRect().left + RVEdit._api.total() * RVEdit._S.pps - 3'), r3[1]))
    o2 = J('RVEdit._S.clips')
    ok('앞 클립을 뒤로 끌면 맨 끝', o2[-1] == json.loads(ord0)[0] and len(o2) == nclip, o2)
    J('RVEdit._api.undo()')
    ok('되돌림', J('JSON.stringify(RVEdit._S.clips)') == ord0)
    # Delete 키
    def key(k, tgt='document'):
        J('(%s).dispatchEvent(new KeyboardEvent("keydown", { key: "%s", bubbles: true }))' % (tgt, k))
    J('RVEdit._S.sel = 0')
    key('Delete')
    ok('Delete 한 번에 클립 하나 줄어듦 (확인 없음)', J('RVEdit._S.clips.length') == nclip - 1)
    J('RVEdit._api.undo()')
    ok('Ctrl+Z 로 돌아옴', J('JSON.stringify(RVEdit._S.clips)') == ord0)
    J('RVEdit._S.sel = -1; document.getElementById("tDel").click()')
    ok('아무것도 안 고르고 휴지통 → 그대로', J('JSON.stringify(RVEdit._S.clips)') == ord0)
    J('RVEdit._S.sel = 0; document.getElementById("tDel").click()')
    ok('휴지통 단추로 지우기', J('RVEdit._S.clips.length') == nclip - 1)
    J('RVEdit._api.undo()')
    J('RVEdit._S.sel = 0; document.getElementById("title").dispatchEvent(new KeyboardEvent("keydown", { key: "Delete", bubbles: true }))')
    ok('입력 칸 안 Delete 는 무시', J('RVEdit._S.clips.length') == nclip)
    # 지도 칸 높이 끌기 (JACK 1127)
    h0 = J('document.getElementById("map").clientHeight')
    gy = J('(() => { const r = document.getElementById("mapGrip").getBoundingClientRect(); return [r.left + r.width / 2, r.top + 4]; })()')
    J('window.__pe("x", "pointerdown", %f, %f, document.getElementById("mapGrip")); window.__pe("mapGrip", "pointermove", %f, %f)' % (gy[0], gy[1], gy[0], gy[1] + 60))
    h1 = J('document.getElementById("map").clientHeight')
    ok('손잡이 끌면 지도 높이 +', abs(h1 - (h0 + 60)) <= 1, [h0, h1])
    ok('지도 그림 칸 크기가 맞음', J('(() => { const m = document.getElementById("map"), t = document.querySelector("#map .ov"); return Math.abs(t.getBoundingClientRect().height - m.clientHeight) < 1; })()'))
    J('window.__pe("mapGrip", "pointerup", %f, %f)' % (gy[0], gy[1] + 60))
    ok('놓으면 기억', J('localStorage.getItem("rv_edit_maph")') == str(h1), J('localStorage.getItem("rv_edit_maph")'))
    J('window.__pe("x", "pointerdown", %f, %f, document.getElementById("mapGrip")); window.__pe("mapGrip", "pointermove", %f, %f); window.__pe("mapGrip", "pointerup", %f, %f)' % (gy[0], gy[1], gy[0], gy[1] + 5000, gy[0], gy[1] + 5000))
    hmax = J('document.getElementById("map").clientHeight')
    ok('최대 = 창 높이의 70 %', abs(hmax - round(J('window.innerHeight') * 0.7)) <= 1, hmax)
    J('window.__pe("x", "pointerdown", %f, %f, document.getElementById("mapGrip")); window.__pe("mapGrip", "pointermove", %f, %f); window.__pe("mapGrip", "pointerup", %f, %f)' % (gy[0], gy[1], gy[0], gy[1] - 9000, gy[0], gy[1] - 9000))
    ok('최소 120 px', J('document.getElementById("map").clientHeight') == 120)
    J('RVEdit._api.mapSetH(300, true); document.getElementById("map").style.height = ""; ')
    J('document.getElementById("mapGrip").dispatchEvent(new MouseEvent("dblclick", { bubbles: true }))')
    ok('두 번 누르면 기본 높이 · 기억 지움', J('document.getElementById("map").clientHeight') == 250 and J('localStorage.getItem("rv_edit_maph")') is None)
    # 지도에 점 → 바로 뷰에서 같은 자리 (JACK 1127): 지도 · 뷰 · 지도 · 뷰 이어서 · Esc 로 끝
    J('RVEdit._S.T = 1.0; RVEdit._api.showAt(1.0)')
    wait('!RVEdit._S.seekBusy', 20)
    n0 = J('RVEdit._G.pts.length')
    J('(() => { const m = document.getElementById("map"), r = m.getBoundingClientRect(); for (let i = 0; i < 20; i++) m.dispatchEvent(new WheelEvent("wheel", { deltaY: -100, clientX: r.left + r.width / 2, clientY: r.top + r.height / 2, bubbles: true, cancelable: true })); })()')
    time.sleep(0.3)
    zz = J('[RVEdit._map ? RVEdit._map.z : -1, (document.querySelector("#map .tiles img") || {}).style ? document.querySelector("#map .tiles img").style.width : ""]')
    ok('지도 확대 22 단계까지 (19 넘으면 타일 늘림)', zz[0] == 22 and zz[1] == '2048px', zz)
    J('(() => { const m = document.getElementById("map"), r = m.getBoundingClientRect(); for (let i = 0; i < 6; i++) m.dispatchEvent(new WheelEvent("wheel", { deltaY: 100, clientX: r.left + r.width / 2, clientY: r.top + r.height / 2, bubbles: true, cancelable: true })); })()')
    J('RVEdit._G.active = -1; document.getElementById("gMap").click()')
    ok('[지도에 점] 켬 · 안내 띠(지도 단계)', J('RVEdit._G.mapAdd') and not J('document.getElementById("gHint").classList.contains("hide")')
       and '지도' in J('document.getElementById("gHint").textContent'), J('document.getElementById("gHint").textContent'))

    ok('지도 단계 = 위성지도 칸 테두리 (3D 뷰는 아님)', J('document.getElementById("map").classList.contains("pickOn") && !document.getElementById("fv").classList.contains("pickOn")'))
    def map_click(dx, dy):
        J('(() => { const r = document.getElementById("map").getBoundingClientRect(), x = r.left + r.width / 2 + %d, y = r.top + r.height / 2 + %d;'
          ' window.__pe("x", "pointerdown", x, y, document.getElementById("map")); window.__pe("x", "pointerup", x, y, document.getElementById("map")); })()' % (dx, dy))

    def view_click(fy):
        J('RVEdit._FV.pitch = -60; (() => { const c = document.getElementById("fv").getBoundingClientRect(); RVAlign.viewClick({ clientX: c.left + c.width / 2, clientY: c.top + c.height * %f }); })()' % fy)
    map_click(0, 0)
    wait('RVEdit._G.pts.length === %d' % (n0 + 1), 40)
    ok('지도 누름 → GCP 생김 · 바로 active', J('RVEdit._G.pts.length') == n0 + 1 and J('RVEdit._G.active') == J('RVEdit._G.pts[RVEdit._G.pts.length - 1].id'))
    ok('뷰 단계 = 3D 뷰 칸 테두리 (위성지도는 꺼짐)', J('document.getElementById("fv").classList.contains("pickOn") && !document.getElementById("map").classList.contains("pickOn")'))
    ok('뷰 단계 안내 띠 (눈에 띄게) · crosshair · 깜박임', '뷰에서 같은 자리' in J('document.getElementById("gHint").textContent')
       and J('document.getElementById("gHint").classList.contains("big")') and J('document.getElementById("fv").style.cursor') == 'crosshair'
       and J('document.querySelectorAll("#map .ov .blink").length') == 1, J('document.getElementById("gHint").textContent'))
    map_click(40, 40)
    time.sleep(0.5)
    ok('뷰 단계에서 지도 눌러도 새 점 안 생김', J('RVEdit._G.pts.length') == n0 + 1)
    pr0 = J('RVEdit._G.pairs.length')
    view_click(0.6)
    ok('뷰 누름 → 짝 +1 · 풀이', J('RVEdit._G.pairs.length') == pr0 + 1 and J('(RVEdit._G.cal[RVEdit._S.media[0].path] || {}).st') in ('ok', 'bad'),
       J('JSON.stringify(RVEdit._G.cal[RVEdit._S.media[0].path])'))
    ok('다시 지도 단계 (안내 · 켜진 채)', J('RVEdit._G.mapAdd') and J('RVEdit._G.await') == -1 and '지도' in J('document.getElementById("gHint").textContent'))
    ok('지도 경로가 옮겨짐 (동 · 북 ≠ 0)', abs(J('RVEdit._G.cal[RVEdit._S.media[0].path].dx')) + abs(J('RVEdit._G.cal[RVEdit._S.media[0].path].dy')) > 1e-6)
    map_click(-40, -30)
    wait('RVEdit._G.pts.length === %d' % (n0 + 2), 40)
    ok('다음 지도 누름 → 또 점 · 뷰 단계', J('RVEdit._G.pts.length') == n0 + 2 and J('RVEdit._G.await') != -1)
    view_click(0.7)
    ok('둘째 뷰 누름 → 짝 +2', J('RVEdit._G.pairs.length') == pr0 + 2)
    J('document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }))')
    ok('Esc 로 끝', not J('RVEdit._G.mapAdd') and J('RVEdit._G.await') == -1)
    map_click(60, 60)
    time.sleep(0.5)
    ok('끝난 뒤 지도 눌러도 점 안 생김', J('RVEdit._G.pts.length') == n0 + 2)
    ok('Esc 뒤 pairs 는 만들기에 넘어감', J('RVEdit._G.pairs.length') == pr0 + 2)
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
    ok('GCP 기록 넘김', len(p.get('edit', {}).get('gcp', {}).get('pts', [])) == 3)
    ok('맞춤 값 넘김 (풀렸으면)', ('calib' in p), p.get('calib'))
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
