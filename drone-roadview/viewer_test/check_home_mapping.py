"""3D 매핑 만들기 시험 (실제 WebView2 · 작은 창 · 화면 캡처 없음 · 실제로 매핑을 돌림).

    python viewer_test\\check_home_mapping.py 사진폴더 결과폴더 [품질=fast]

매핑 화면 열기 → 사진 폴더(파일 창 대신 주어진 폴더) → 범위(가운데 절반 네모) → 만들기 → 진행 · 남은 시간 →
끝나면 프로젝트가 3D 보기로 열림 → 세모 · 질감 · 정보 줄 확인. 결과 폴더는 지우고 새로 만든다.
"""
import json
import os
import shutil
import sys
import time

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '..')))
import webview  # noqa: E402
from roadview import app as A  # noqa: E402

PHOTOS, OUT = os.path.abspath(sys.argv[1]), os.path.abspath(sys.argv[2])
Q = sys.argv[3] if len(sys.argv) > 3 else 'fast'
FAIL = []


def ok(name, cond, val=None):
    if not cond:
        FAIL.append(name)
    print(('통과 ' if cond else '실패 ') + name + ('' if val is None else ' · ' + json.dumps(val, ensure_ascii=False)[:220]))
    sys.stdout.flush()


def run(w, api):
    J = w.evaluate_js

    def wait(expr, t=60, step=0.5):
        t0 = time.time()
        while time.time() - t0 < t:
            try:
                if J(expr):
                    return True
            except Exception:
                pass
            time.sleep(step)
        return False
    time.sleep(3)
    J('RVMap.open()')
    ok('매핑 화면', wait('!document.getElementById("mp").classList.contains("hide")', 10))
    r = api.scan_mapping(PHOTOS)
    ok('사진 폴더 읽기', r['ok'] and len(r['photos']) >= 3, len(r['photos']))
    J('RVMap._test.setPhotos(%s, %s, %s)' % (json.dumps(PHOTOS), json.dumps(r['photos']), json.dumps(OUT)))
    J('document.querySelector("#mQ button[data-q=%s]").click()' % Q)
    n_all = J('RVMap._S.sel.size')
    ok('전체 고름', n_all == len(r['photos']), n_all)
    # 범위 그리기: 지도 가운데 절반 네모를 끌기
    J('document.getElementById("mArea").click()')
    J('''(() => { const el = document.getElementById("mMap"), b = el.getBoundingClientRect();
          const ev = (t, x, y) => el.dispatchEvent(new PointerEvent(t, { clientX: x, clientY: y, bubbles: true, pointerId: 1 }));
          ev("pointerdown", b.left + b.width * 0.25, b.top + b.height * 0.25); ev("pointermove", b.left + b.width * 0.75, b.top + b.height * 0.75);
          ev("pointerup", b.left + b.width * 0.75, b.top + b.height * 0.75); })()''')
    n_sel = J('RVMap._S.sel.size')
    ok('범위로 고름 (전체보다 적고 3장 이상)', 3 <= n_sel <= n_all, [n_sel, n_all])
    ok('예상 시간 글', '분' in (J('document.getElementById("mEst").textContent') or ''), J('document.getElementById("mEst").textContent'))
    # 만들 것 고르기 (1.0.15): 3D 만 → 예상 시간 짧음 · 시험은 3D + 정사 + DSM
    setc = lambda o, on: J('(() => { const b = document.querySelector("#mOuts button[data-o=%s]"); if (b.classList.contains("on") !== %s) b.click(); return b.classList.contains("on"); })()' % (o, 'true' if on else 'false'))   # noqa: E731
    for o in ('ortho', 'dsm', 'dtm', 'las'):
        setc(o, False)
    setc('mesh', True)
    J('RVMap._S.sel = new Set(RVMap._S.photos.map(p => p.name)); RVMap._test.draw();')
    J('RVMap._S.sel = new Set(RVMap._S.photos.concat(RVMap._S.photos, RVMap._S.photos, RVMap._S.photos, RVMap._S.photos).map((p, i) => p.name + "#" + i)); RVMap._test.draw();')
    J('document.querySelector("#mQ button[data-q=normal]").click()')     # 견줌은 보통 100장 (빠르게는 점구름이 같아 거의 같음) · 누르면 예상 다시 셈
    e1 = J('document.getElementById("mEst").textContent')
    setc('ortho', True); setc('dsm', True); setc('dtm', True)
    e2 = J('document.getElementById("mEst").textContent')
    J('document.querySelector("#mQ button[data-q=%s]").click()' % Q)
    num = lambda t: float(__import__('re').search(r'약 ([\d.]+)', t).group(1)) * (60 if '시간' in t else 1)   # noqa: E731
    ok('3D 만 예상이 더 짧음', num(e1) < num(e2), [e1, e2])
    setc('dtm', False)
    ok('고른 것', J('JSON.stringify([...RVMap._S.outs].sort())') == '["dsm","mesh","ortho"]', J('JSON.stringify([...RVMap._S.outs])'))
    J('RVMap._S.sel = new Set(RVMap._S.photos.map(p => p.name)); RVMap._test.draw();')     # 시험은 전체로 (작은 폴더)
    J('document.getElementById("mBuild").click()')
    ok('만들기 시작', wait('!!RVMap._S.job', 15))
    t0 = time.time()
    seen_pct = 0
    while time.time() - t0 < 1800:
        try:          # 끝나면 프로그램이 3D 보기로 넘어가 매핑 화면(RVMap)이 없어짐
            st = json.loads(J('window.RVMap ? JSON.stringify([document.getElementById("mBar").style.width, document.getElementById("mStatus").textContent, !!RVMap._S.job]) : "[\\"\\", \\"\\", false]"'))
        except Exception:
            st = ['', '', False]
        try:
            seen_pct = max(seen_pct, float(st[0].rstrip('%') or 0))
        except ValueError:
            pass
        if not st[2]:
            break
        time.sleep(2)
    ok('진행 막대가 움직임', seen_pct > 0, seen_pct)
    ok('끝나고 3D 보기로 열림', wait('location.href.indexOf("index.html") >= 0 && !!window.__map3d', 60), J('location.href'))
    ok('3D 그려짐', wait('window.__drawn > 0 || !!window.__err', 120), J('JSON.stringify([window.__mesh, window.__err || null])'))
    ok('정보 줄', '세모' in (J('document.getElementById("info").textContent') or ''), J('document.getElementById("info").textContent'))
    ok('결과물 파일', all(os.path.exists(os.path.join(OUT, '결과물', f)) for f in ('정사영상.tif', 'DSM.tif', '지표면_DSM.xml')))
    ok('안 고른 것은 안 만듦 (DTM · LAS)', not any(os.path.exists(os.path.join(OUT, '결과물', f)) for f in ('DTM.tif', '점구름.las')))
    ok('땅 높이 · 미리보기', all(os.path.exists(os.path.join(OUT, 'data', f)) for f in ('지면.tif', '미리보기.png')))
    w.destroy()


def main():
    if os.path.isdir(OUT):
        shutil.rmtree(OUT)
    api = A.Api()
    w = webview.create_window('시험', A.HOME_HTML.resolve().as_uri(), js_api=api, width=1500, height=920, minimized=True)
    api._window = w
    webview.start(run, (w, api), gui='edgechromium', private_mode=True)
    print('== %d 실패' % len(FAIL), FAIL)
    return 1 if FAIL else 0


if __name__ == '__main__':
    sys.exit(main())
