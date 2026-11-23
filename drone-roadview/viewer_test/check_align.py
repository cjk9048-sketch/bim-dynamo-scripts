"""점 찍어 맞추기 시험: selftest.js 의 al* 시험을 화면 없는 Edge(cdp.py, 실제 시간)로 돌린다.

    python -I check_align.py --align <make_synth --align --xyz 폴더> --out <make_synth 폴더> [--shots <폴더>]

사용자처럼 사진 · 지도를 눌러 짝을 만들고 (누를 자리는 make_synth 의 답으로 계산), 풀린 맞춤이 일부러 틀린 값
(dx 2.0 · dy −1.5 · dz 0.8 · dyaw 4°) 과 맞는지 본다.
  al3        4 짝 (사진 둘) → 값 · 지우기 → 3 짝 · Esc · 끌기 · 끝 · 파일로 저장 (calib.js 에 짝)
  al_reload  같은 브라우저로 다시 열기 → 짝 · 값 · 핀 그대로
  al_file    빈 저장소 + al3 에서 내려받은 calib.js → 파일 짝 · 값 · 하나 더 하면 이 브라우저에 저장
  al1 · al2  짝 1 · 2 개 (풀리는 값 수) · 하늘 누름 거절
  al_bad     지도 점을 60° 돌려 찍음 → '점 확인' · 값 그대로
  al3_http   al3 를 http 로
  al3_nodem  높이 타일(dem.js) 없이 같은 누름 → 값이 얼마나 틀어지는지 (대조, 재기만)
  al_off     (평평한 자료) 켰다 끈 뒤 원판 마우스 올림 · 누름 이동 · 끌기 ≠ 누름
OSM · VWorld 주소는 막아 둔다 (밖으로 안 나감). SNAP 줄이 오면 그때 화면을 찍는다.
"""
import argparse
import functools
import http.server
import json
import os
import pathlib
import shutil
import sys
import threading
import time
import urllib.parse

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import check_interact as ci  # noqa: E402
import check_view as cv  # noqa: E402
from cdp import Edge  # noqa: E402

BLOCK = ['*openstreetmap.org*', '*vworld.kr*']
DOWN = 'shot=0001&yaw=0&pitch=-89&fov=120'  # 바로 아래를 넓게


class _Quiet(http.server.SimpleHTTPRequestHandler):
    def log_message(self, *a):
        pass


def drive(ed, url, prefix, shots_dir, timeout=240):
    """selftest 를 끝까지 돌린다. 'SELFTEST SNAP 이름' 이 오면 찍고 window.__snapAck 로 알린다."""
    ed.goto(url)
    t0 = time.time()
    acked, pngs = set(), []
    while time.time() - t0 < timeout:
        ed.pump(0.2)
        for _, t in list(ed.console):
            if t.startswith('SELFTEST SNAP '):
                name = t[len('SELFTEST SNAP '):].strip()
                if name not in acked:
                    acked.add(name)
                    ed.frames(3)
                    png = os.path.join(shots_dir, '%s_%s.png' % (prefix, name))
                    ed.screenshot(png)
                    pngs.append(png)
                    ed.js('window.__snapAck = %s' % json.dumps(name))
        if any(t.startswith('SELFTEST DONE') for _, t in ed.console):
            break
    ed.frames(3)
    png = os.path.join(shots_dir, prefix + '.png')
    ed.screenshot(png)
    pngs.append(png)
    st = [t[len('SELFTEST '):] for _, t in ed.console if t.startswith('SELFTEST ')]
    errs = [str(x)[:220] for x in ed.exceptions] + [t[:220] for k, t in ed.console if k == 'error']
    reqs = list(ed.requests)
    return st, errs, reqs, pngs


def report(name, query, st, errs, reqs, pngs):
    print('== %s  (%s)' % (name, query))
    export = None
    for s in st:
        if s.startswith('EXPORT '):
            export = json.loads(s[len('EXPORT '):])
            print('    EXPORT calib.js %d 글자' % len(export))
            continue
        if s.startswith('SNAP '):
            continue
        print('   ', s)
    for e in errs:
        print('    JS:', e)
    osm = [u for u in reqs if 'openstreetmap' in u]
    vw = [u for u in reqs if 'vworld.kr' in u]
    print('    네트워크: OSM %d · VWorld %d' % (len(osm), len(vw)))
    done = [s for s in st if s.startswith('DONE')]
    ok = bool(done) and 'ALL-PASS' in done[0] and not errs and not osm and not vw
    print('   결과:', '통과' if ok else '실패', '· 사진:', ' · '.join(pngs))
    return ok, export


def copy_set(src, dst, drop=()):
    shutil.rmtree(dst, ignore_errors=True)
    shutil.copytree(src, dst, ignore=shutil.ignore_patterns('_*', 'tiles'))
    if os.path.isdir(os.path.join(src, 'tiles')):
        shutil.copytree(os.path.join(src, 'tiles'), os.path.join(dst, 'tiles'))
    for f in drop:
        p = os.path.join(dst, f)
        if os.path.exists(p):
            os.remove(p)


def page_uri(folder, query):
    return pathlib.Path(folder, ci.PAGE).resolve().as_uri() + '?' + query


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--align', required=True, help='make_synth --align --xyz 폴더')
    ap.add_argument('--out', required=True, help='make_synth (평평한) 폴더 — al_off')
    ap.add_argument('--shots', default=None)
    args = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    A = os.path.abspath(args.align)
    B = os.path.abspath(args.out)
    shots_dir = os.path.abspath(args.shots or os.path.join(A, '_shots'))
    os.makedirs(shots_dir, exist_ok=True)
    prof = os.path.join(shots_dir, '_prof_a')
    F = A + '_file'   # al3 에서 내려받은 calib.js 를 넣은 복사본
    N = A + '_nodem'  # dem.js 뺀 복사본
    copy_set(A, N, drop=[os.path.join('data', 'dem.js')])
    for d in (A, B, N):
        ci.prepare(d)
    all_ok = True

    def one(name, folder, query, ed=None, url=None):
        nonlocal all_ok
        own = ed is None
        if own:
            ed = Edge(prof, cv.WIN).__enter__()
            ed.net(block=BLOCK)
        try:
            st, errs, reqs, pngs = drive(ed, url or page_uri(folder, query), 'a_' + name, shots_dir)
        finally:
            if own:
                ed.__exit__(None, None, None)
        ok, export = report(name, query, st, errs, reqs, pngs)
        all_ok &= ok
        return ok, export, st

    # al3 → al_reload (같은 브라우저 · 저장소)
    ed = Edge(prof, cv.WIN).__enter__()
    try:
        ed.net(block=BLOCK)
        _, export, _ = one('al3', A, 't=al3&' + DOWN, ed)
        one('al_reload', A, 't=al_reload&' + DOWN, ed)
    finally:
        ed.__exit__(None, None, None)
    if export:
        with open(os.path.join(shots_dir, 'a_al3_export_calib.js'), 'w', encoding='utf-8', newline='\n') as f:
            f.write(export)
        copy_set(A, F)
        with open(os.path.join(F, 'data', 'calib.js'), 'w', encoding='utf-8', newline='\n') as f:
            f.write(export)
        ci.prepare(F)
        one('al_file', F, 't=al_file&' + DOWN)
    else:
        print('== al_file  내려받은 calib.js 없음  실패')
        all_ok = False
    one('al1', A, 't=al1&shot=0001&yaw=0&pitch=-45&fov=120')
    one('al2', A, 't=al2&' + DOWN)
    one('al_bad', A, 't=al_bad&' + DOWN)

    srv = http.server.ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(_Quiet, directory=A))
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    try:
        q = 't=al3&' + DOWN
        one('al3_http', A, q, url='http://127.0.0.1:%d/%s?%s' % (srv.server_address[1], urllib.parse.quote(ci.PAGE), q))
    finally:
        srv.shutdown()

    _, _, st = one('al3_nodem', N, 't=al3&loose=1&' + DOWN)
    info = [s for s in st if s.startswith('INFO')]
    if info:
        print('   (대조) 높이 타일 없이 4 짝:', info[0])

    e3, e2 = cv.ms.SHOTS[2][0], cv.ms.SHOTS[1][0]
    look = (-90.0, -cv.math.degrees(cv.math.atan2(cv.ms.HEIGHT, abs(e3 - e2))))
    one('al_off', B, 't=al_off&shot=0003&yaw=%.4f&pitch=%.4f&fov=90' % look)

    for d in (A, B, N, F):
        for f in (ci.PAGE, 'selftest.js'):
            p = os.path.join(d, f)
            if os.path.exists(p):
                os.remove(p)
    shutil.rmtree(prof, ignore_errors=True)
    print('전체:', '통과' if all_ok else '실패')
    return 0 if all_ok else 1


if __name__ == '__main__':
    sys.exit(main())
