"""함께 처리 화면 시험 (실제 WebView2 · 작은 창 · 실제 Api · 실제 도우미 프로세스).

    python viewer_test\\check_home_cluster.py [시험폴더]

설정 파일(cluster.json)은 시험 폴더로 돌림 → 쓰는 사람 설정은 안 건드림. 파일 창은 안 띄움(고르기 = 시험 폴더).
처음 화면 [도우미 모드] (공유 폴더 없음 → «함께 처리» 화면으로) → 공유 폴더 고르기 → 내 매핑에 쓰기 → 도우미 모드 켬 →
도우미 목록에 이 PC → 매핑 화면 «함께 처리 · 도우미 1대» · 예상 시간 줄어듦 → 끔 → 도우미 프로세스 끝.
"""
import json
import os
import shutil
import sys
import tempfile
import time
from pathlib import Path

os.environ['RV_CLUSTER_SAMEHOST'] = '1'          # 같은 PC 도우미도 셈 (시험)
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '..')))
import webview  # noqa: E402
from roadview import app as A  # noqa: E402

ROOT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else tempfile.mkdtemp(prefix='rv_clu_ui_')
BOARD = os.path.join(ROOT, 'board')
FAIL = []


def ok(name, cond, val=None):
    if not cond:
        FAIL.append(name)
    print(('통과 ' if cond else '실패 ') + name + ('' if val is None else ' · ' + json.dumps(val, ensure_ascii=False)[:220]))
    sys.stdout.flush()


def cfg():
    try:
        return json.loads(A.CLUSTER_CFG.read_text(encoding='utf-8'))
    except (OSError, ValueError):
        return {}


def run(w, api):
    J = w.evaluate_js

    def wait(expr, t=30, step=0.4):
        t0 = time.time()
        while time.time() - t0 < t:
            try:
                if J(expr):
                    return True
            except Exception:
                pass
            time.sleep(step)
        return False
    txt = lambda i: J('(document.getElementById("%s") || {}).textContent || ""' % i)          # noqa: E731
    vis = lambda i: J('(() => { const e = document.getElementById("%s"); return !!(e && !e.closest(".hide") && e.offsetParent); })()' % i)   # noqa: E731
    on = lambda i: J('document.getElementById("%s").classList.contains("on")' % i)           # noqa: E731
    click = lambda i: J('document.getElementById("%s").click()' % i)                          # noqa: E731
    try:
        ok('화면 준비', wait('!!window.RVCluster && !!window.RV', 20))
        time.sleep(1.5)
        ok('처음 화면 [병렬 처리 참여] 보임 · 꺼짐', vis('bHelper') and not on('bHelper') and txt('bHelper') == '병렬 처리 참여', txt('bHelper'))
        ok('왼쪽 메뉴 «함께 처리»', J('!!document.querySelector(".nav[data-page=cluster]")'))
        # 큰 카드 = 움직이는 3D 드론 + 영상 바탕 (사진 안 씀 · JACK 1127 «애니메이션으로»)
        wait("[...document.querySelectorAll('.hero .art')].every((a) => a.classList.contains('g3on'))", 10)
        r = J("[...document.querySelectorAll('.hero .art')].map((a) => [a.classList.contains('g3on'), !!a.querySelector('img.photo'), a.classList.contains('phon')])")
        ok('큰 카드 둘 = 움직이는 3D 드론 (사진 없음)', len(r) == 2 and all(x[0] and not x[1] and not x[2] for x in r), r)
        # 공유 폴더 없이 도우미 모드 → 알림 + «함께 처리» 화면으로
        click('bHelper')
        ok('공유 폴더 없음 → «함께 처리» 화면', wait('!document.getElementById("pageCluster").classList.contains("hide")', 5))
        ok('도우미 안 켜짐', api._helper is None and not on('cHelp'))
        ok('«공유 폴더 먼저»', wait('document.getElementById("cUseSub").textContent.indexOf("공유 폴더") >= 0', 5), txt('cUseSub'))
        # 공유 폴더 고르기 (파일 창 대신 시험 폴더)
        click('cPick')
        ok('공유 폴더 칸', wait('document.getElementById("cBoard").value === %s' % json.dumps(BOARD), 10), J('document.getElementById("cBoard").value'))
        ok('설정 저장 · 공유 폴더', cfg().get('board') == BOARD, cfg())
        ok('게시판 폴더 만듦', os.path.isdir(os.path.join(BOARD, 'helpers')) and os.path.isdir(os.path.join(BOARD, 'runs')))
        ok('켜 둔 도우미 없음 글', vis('cEmpty'), txt('cEmpty'))
        click('cUse')
        ok('내 매핑에 쓰기 켬', wait('document.getElementById("cUse").classList.contains("on")', 5) and cfg().get('use') is True, cfg())
        # 도우미 모드 켬 → 실제 도우미 프로세스 · 게시판 심장박동 → 목록
        click('cHelp')
        ok('도우미 켬 (단추)', wait('document.getElementById("cHelp").classList.contains("on")', 5))
        ok('설정 저장 · 도우미', cfg().get('helper') is True, cfg())
        hp = api._helper.proc.pid if api._helper else None
        ok('도우미 프로세스', bool(hp) and api._helper.alive(), hp)
        ok('도우미 목록 (1)', wait('document.getElementById("cCnt").textContent === "(1)" && !!document.querySelector("#cList .share:not(.gone)")', 60),
           [txt('cCnt'), txt('cList')])
        ok('이 PC 상태 글', wait('document.getElementById("cMe").textContent.indexOf("기다리는 중") >= 0', 20), txt('cMe'))
        ok('목록 글 (코어 · GB)', '코어' in txt('cList') and 'GB' in txt('cList'), txt('cList'))
        ok('빈 글 숨김', not vis('cEmpty'))
        # 처음 화면 단추도 켜짐
        J('RV.page("home")')
        ok('처음 화면 단추 켜짐', wait('document.getElementById("bHelper").classList.contains("on")', 10), txt('bHelper'))
        ok('처음 화면 단추 글', wait('document.getElementById("bHelper").textContent.indexOf("병렬 처리 · ") === 0', 10), txt('bHelper'))
        # 매핑 화면 «함께 처리» 줄 · 예상 시간
        J('RVMap.open()')
        ok('매핑 화면 «함께 처리» 줄', wait('document.getElementById("mClu").textContent === "켜짐"', 10) and vis('mClu'), [txt('mClu'), txt('mCluSub')])
        ok('«도우미 1대»', wait('document.getElementById("mCluSub").textContent === "도우미 1대"', 10), txt('mCluSub'))
        ph = [{'name': 'P%03d.JPG' % i, 'lat': 37.0 + i * 1e-5, 'lon': 127.0 + i * 1e-5, 'look_lat': 37.0 + i * 1e-5, 'look_lon': 127.0 + i * 1e-5, 'rtk': 50}
              for i in range(100)]
        J('RVMap._test.setPhotos("C:/시험", %s, "C:/시험_결과")' % json.dumps(ph))
        J('document.querySelector("#mQ button[data-q=normal]").click()')
        e_on = txt('mEst')
        ok('예상 시간에 PC 2대', 'PC 2대' in e_on, e_on)
        click('mClu')
        ok('매핑 화면에서 끔', wait('document.getElementById("mClu").textContent === "꺼짐"', 5) and cfg().get('use') is False, cfg())
        e_off = txt('mEst')
        def num(t):                                  # 예상 시간 «1시간 54분» · «8분» → 분
            m = __import__('re').search(r'(?:(\d+)시간)?\s*(?:(\d+)분)?', t.split('사진')[0].replace('예상 시간', ''))
            return int(m.group(1) or 0) * 60 + int(m.group(2) or 0)
        ok('함께 처리가 예상 시간을 줄임', num(e_on) < num(e_off) and 'PC' not in e_off, [e_on, e_off])
        click('mClu')
        ok('다시 켬', wait('document.getElementById("mClu").textContent === "켜짐"', 5) and cfg().get('use') is True)
        J('RVMap.close()')
        # 도우미 모드 끔 → 프로세스 끝 · 게시판에서 빠짐
        J('RV.page("cluster")')
        click('cHelp')
        ok('도우미 끔 (단추)', wait('!document.getElementById("cHelp").classList.contains("on")', 10))
        ok('설정 저장 · 도우미 끔', cfg().get('helper') is False, cfg())
        ok('도우미 프로세스 끝', api._helper is None and not A_alive(hp), hp)
        time.sleep(2)
        ok('이 PC 상태 글 비움', txt('cMe') == '', txt('cMe'))
        # 다시 켰다 끄기 (빠르게 두 번 · 남는 프로세스 없음)
        click('cHelp'); time.sleep(1.0)
        hp2 = api._helper.proc.pid if api._helper else None
        click('cHelp'); time.sleep(1.5)
        ok('다시 켰다 끄기', hp2 is not None and api._helper is None, hp2)
        for pid in (hp, hp2):
            if pid:
                ok('프로세스 %d 없음' % pid, not A_alive(pid))
    finally:
        if api._helper:
            api._helper.stop()
            api._helper = None
        w.destroy()


def A_alive(pid):
    from roadview.cluster import _pid_alive
    time.sleep(0.5)
    return _pid_alive(pid)


def main():
    shutil.rmtree(ROOT, ignore_errors=True)
    os.makedirs(ROOT, exist_ok=True)
    A.CLUSTER_CFG = Path(ROOT) / 'cluster.json'       # 쓰는 사람 설정 대신
    api = A.Api()
    api.pick_folder = lambda: BOARD                     # 파일 창 대신
    api.pick_mapping_folder = lambda: None
    w = webview.create_window('시험', A.HOME_HTML.resolve().as_uri(), js_api=api, width=1500, height=920, minimized=True)
    api._window = w
    webview.start(run, (w, api), gui='edgechromium', private_mode=True)
    print('== %d 실패' % len(FAIL), FAIL)
    return 1 if FAIL else 0


if __name__ == '__main__':
    sys.exit(main())
