"""바탕 지도 청 시험: file:// 에서는 OSM 타일을 청하지 않고 안내 글을 띄우는지, http 에서는 OSM 을 그대로 쓰는지.

    python -I check_net.py --out <make_synth 폴더> [--shots <폴더>]

index.html (빌더가 로드뷰.html 옆에 같은 내용으로 씀) 으로 연다 — 쪽 이름에 기대지 않는지도 본다.
OSM · VWorld 주소는 CDP 로 막아 밖으로 나가지 않게 한다 (막은 청도 기록에는 남는다).
http 는 이 스크립트가 127.0.0.1 에 잠깐 띄운 정적 서버.
"""
import argparse
import functools
import http.server
import json
import os
import pathlib
import sys
import threading

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import check_view as cv  # noqa: E402
from cdp import Edge  # noqa: E402

BLOCK = ['*openstreetmap.org*', '*vworld.kr*']
NOTE = '지도: 웹 공유 주소에서 보임 · 또는 지도 키'
STATE = ("JSON.stringify({no: document.getElementById('shotNo').textContent,"
         " note: getComputedStyle(document.getElementById('mapNote')).display !== 'none',"
         " noteText: document.getElementById('mapNote').textContent,"
         " attrib: document.getElementById('attrib').textContent,"
         " toast: document.getElementById('toast').textContent})")


class _Quiet(http.server.SimpleHTTPRequestHandler):
    def log_message(self, *a):
        pass


def visit(ed, url, png, settle=2.5):
    ed.goto(url)
    ok = ed.wait_js(cv.READY, timeout=60)
    ed.pump(settle)  # 타일 청이 나갈 시간
    ed.frames(2)
    ed.screenshot(png)
    return ok, json.loads(ed.js(STATE)), list(ed.requests), cv.errors(ed)


def report(name, ok, st, reqs, errs, png):
    osm = [u for u in reqs if 'openstreetmap' in u]
    vw = [u for u in reqs if 'vworld.kr' in u]
    print('== %s' % name)
    print('   불러옴 %s · 사진 %s · 안내 글 %s (%s) · 출처 "%s"' % (ok, st['no'], st['note'], st['noteText'], st['attrib']))
    print('   청 %d 개 · OSM %d · VWorld %d' % (len(reqs), len(osm), len(vw)))
    for u in osm[:3]:
        print('     OSM:', u)
    print('   JS 오류 %d 줄' % len(errs))
    for e in errs:
        print('   오류:', str(e)[:200])
    print('   사진:', png)
    return osm, vw


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--shots', default=None)
    args = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    out_dir = os.path.abspath(args.out)
    shots_dir = os.path.abspath(args.shots or os.path.join(out_dir, '_shots'))
    os.makedirs(shots_dir, exist_ok=True)
    all_ok = True
    file_url = pathlib.Path(out_dir, 'index.html').resolve().as_uri() + '?shot=0002'

    srv = http.server.ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(_Quiet, directory=out_dir))
    th = threading.Thread(target=srv.serve_forever, daemon=True)
    th.start()
    http_url = 'http://127.0.0.1:%d/index.html?shot=0002' % srv.server_address[1]
    try:
        with Edge(os.path.join(shots_dir, '_prof_n'), cv.WIN) as ed:
            ed.net(block=BLOCK)
            # 1) file:// · 키 없음 → OSM 청 0, 안내 글
            png = os.path.join(shots_dir, 'n_file.png')
            ok, st, reqs, errs = visit(ed, file_url, png)
            osm, vw = report('file:// (키 없음)', ok, st, reqs, errs, png)
            good = ok and not osm and not vw and st['note'] and st['noteText'] == NOTE and st['attrib'] == '' and not errs
            print('   결과:', '통과' if good else '실패')
            all_ok &= good

            # 2) file:// · 가짜 VWorld 키 → VWorld 청 (막힘 → 실패) → 안내 글로 돌아옴, OSM 청 0
            ed.js("localStorage.setItem('rv_vworld_key', 'TESTKEY-NOT-REAL')")
            png = os.path.join(shots_dir, 'n_file_key.png')
            ed.goto(file_url)
            ok = ed.wait_js(cv.READY, timeout=60)
            ok2 = ed.wait_js("document.getElementById('toast').textContent.indexOf('지도 키 확인') >= 0", timeout=20)
            ed.frames(2)
            ed.screenshot(png)
            st = json.loads(ed.js(STATE))
            reqs, errs = list(ed.requests), cv.errors(ed)
            osm, vw = report('file:// (가짜 지도 키)', ok, st, reqs, errs, png)
            good = ok and ok2 and not osm and len(vw) > 0 and st['note'] and not errs
            print('   키 거절 알림 %s · 결과: %s' % (ok2, '통과' if good else '실패'))
            all_ok &= good
            ed.js("localStorage.removeItem('rv_vworld_key')")

            # 3) http · 키 없음 → OSM 타일 청 (막아 둠), 안내 글 없음
            png = os.path.join(shots_dir, 'n_http.png')
            ok, st, reqs, errs = visit(ed, http_url, png)
            osm, vw = report('http (키 없음)', ok, st, reqs, errs, png)
            good = ok and len(osm) > 0 and not st['note'] and 'OpenStreetMap' in st['attrib'] and not errs
            print('   결과:', '통과' if good else '실패')
            all_ok &= good
    finally:
        srv.shutdown()
    print('전체:', '통과' if all_ok else '실패')
    return 0 if all_ok else 1


if __name__ == '__main__':
    sys.exit(main())
