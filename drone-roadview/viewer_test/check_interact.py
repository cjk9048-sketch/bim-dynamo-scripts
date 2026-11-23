"""조작 시험: 시험 폴더에 selftest.js 를 넣은 복사본 html 을 만들어 화면 없는 Edge(cdp.py, 실제 시간)로 돌린다.

    python -I check_interact.py --out <make_synth 폴더> [--out-missing <--no-pipes --drop 0003 폴더>] [--nav <make_synth --nav 폴더>] [--shots <폴더>]

selftest.js 가 남긴 'SELFTEST PASS/FAIL' 줄과 JS 오류(잡히지 않은 오류 · console.error)를 모은다.
'ui' 시험은 가짜 VWorld 키로 바꾼다 (청은 막아 둠 → 실패 → 기본 바탕으로 돌아오는지).
모든 경우에 네트워크 청을 기록해 file:// 에서 OSM 타일을 청하지 않는지 본다 (OSM · VWorld 주소는 막아 밖으로 안 나감).
끌기 시험은 끝난 화면에서 N 글자 자리를 계산 값과 견준다 (휠 줌 + 200 px 끌기).
  arrows · arrows40  화살표 = 보는 방향 기준 이동 (--nav 자료, 없으면 <out>_nav 에 작게 만든다) · PageUp / PageDown = 찍은 순서
  host · host_late · host_http  프로그램 (pywebview) 흉내 hostmock.js 를 app.js 앞에 넣은 쪽:
      단추 · 관로 넣기 (import_pipes 때 이 스크립트가 data/pipes.js 를 새 자료로 바꿔 씀 → 다시 읽어 그리는지) ·
      맞춤 자동 저장 (save_calib) · 내보내기 (job 진행 → 주소 → 복사) · 처음. 끝나면 pipes.js 를 되돌린다.
  names · names2  지도 [이름]: VWorld 청을 CDP Fetch 로 가짜 타일로 답해 (밖으로 안 나감) 이름 겹 (Hybrid) 청을 MARK 앞뒤로 센다.
"""
import argparse
import functools
import http.server
import io
import json
import math
import os
import pathlib
import shutil
import subprocess
import sys
import threading
import time
import urllib.parse

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import check_view as cv  # noqa: E402
from cdp import Edge  # noqa: E402

PAGE = '로드뷰_selftest.html'
PAGE_HOST = '로드뷰_host.html'
BLOCK = ['*openstreetmap.org*', '*vworld.kr*']
TAG = '<script src="viewer/app.js"></script>'


def prepare(out_dir, host=False):
    """복사본 html (selftest.js 를 app.js 뒤에 · host 면 hostmock.js 를 app.js 앞에) 을 쓰고 그 이름을 돌려준다."""
    src = os.path.join(out_dir, '로드뷰.html')
    with open(src, encoding='utf-8') as f:
        html = f.read()
    assert TAG in html
    page = PAGE_HOST if host else PAGE
    html = html.replace(TAG, ('<script src="hostmock.js"></script>\n' if host else '') + TAG + '\n<script src="selftest.js"></script>')
    with open(os.path.join(out_dir, page), 'w', encoding='utf-8', newline='\n') as f:
        f.write(html)
    shutil.copyfile(os.path.join(HERE, 'selftest.js'), os.path.join(out_dir, 'selftest.js'))
    if host:
        shutil.copyfile(os.path.join(HERE, 'hostmock.js'), os.path.join(out_dir, 'hostmock.js'))
    return page


def cleanup(folder):
    for f in (PAGE, PAGE_HOST, 'selftest.js', 'hostmock.js'):
        p = os.path.join(folder, f)
        if os.path.exists(p):
            os.remove(p)


def drive(ed, url, png, shots_dir, prefix, on_cmd=None, timeout=180):
    """selftest 를 끝까지 돌린다. SELFTEST SNAP / MARK 이름 → 찍고 window.__snapAck / __markAck 로 알림 ·
    SELFTEST HOSTCMD 이름 → on_cmd(이름) 뒤 window.__hostAck[이름] = 1. MARK 는 ed.timeline 의 자리를 남긴다."""
    ed.goto(url)
    t0 = time.time()
    seen, marks, pngs = set(), {}, []
    while time.time() - t0 < timeout:
        ed.pump(0.2)
        for k, (kind, t) in enumerate(list(ed.timeline)):
            if kind != 'con' or k in seen or not t.startswith('SELFTEST '):
                continue
            cmd = t[len('SELFTEST '):].strip()
            if cmd.startswith(('SNAP ', 'MARK ')):
                seen.add(k)
                what, name = cmd.split(' ', 1)
                name = name.strip()
                if what == 'MARK':
                    marks[name] = k
                ed.frames(3)
                p = os.path.join(shots_dir, '%s_%s.png' % (prefix, name))
                ed.screenshot(p)
                pngs.append(p)
                ed.js('window.__%sAck = %s' % (what.lower(), json.dumps(name)))
            elif cmd.startswith('HOSTCMD '):
                seen.add(k)
                name = cmd.split(' ', 1)[1].strip()
                if on_cmd:
                    on_cmd(name)
                ed.js('window.__hostAck = window.__hostAck || {}; window.__hostAck[%s] = 1' % json.dumps(name))
        if any(kind == 'con' and t.startswith('SELFTEST DONE') for kind, t in ed.timeline):
            break
    ed.pump(0.3)
    ed.frames(3)
    ed.screenshot(png)
    st = [t[len('SELFTEST '):] for _, t in ed.console if t.startswith('SELFTEST ')]
    other = [str(x)[:220] for x in ed.exceptions] + [t[:220] for k, t in ed.console if k == 'error']
    return st, other, list(ed.requests), list(ed.timeline), marks, pngs


# ---------------------------------------------------------------- 프로그램 흉내: import_pipes 때 쓸 새 관로
NEW_PIPES = {
    'layers': [{'name': '새관', 'color': '#ff3b30'}],
    'lines': [([(-20.0, -3.0), (40.0, -3.0)], 0.5, '새관'), ([(12.0, -20.0), (12.0, 20.0)], 0.3, '새관2')],
}


def new_pipes_js():
    lines = [{'layer': 0, 'label': label, 'dia': dia, 'pts': [[x, y, None] for x, y in verts],
              'g': [cv.ms.GZ] * len(verts), 'll': [cv.ms.en2ll(x, y) for x, y in verts]}
             for verts, dia, label in NEW_PIPES['lines']]
    return 'window.RV_PIPES = ' + json.dumps({'layers': NEW_PIPES['layers'], 'lines': lines}, ensure_ascii=False) + ';\n'


# ---------------------------------------------------------------- 지도 [이름]: 가짜 VWorld 타일
def fake_tiles():
    sat = Image.new('RGB', (256, 256), (52, 78, 56))
    d = ImageDraw.Draw(sat)
    for i, c in enumerate([(92, 112, 70), (120, 104, 78), (70, 96, 64)]):
        d.rectangle([20 + i * 75, 30 + (i % 2) * 90, 80 + i * 75, 110 + (i % 2) * 90], fill=c)
    d.line([(0, 200), (256, 170)], fill=(150, 150, 150), width=10)
    buf = io.BytesIO()
    sat.save(buf, 'JPEG', quality=85)
    jpg = buf.getvalue()
    hyb = Image.new('RGBA', (256, 256), (0, 0, 0, 0))
    d = ImageDraw.Draw(hyb)
    d.line([(0, 200), (256, 170)], fill=(255, 214, 0, 255), width=4)
    try:
        font = ImageFont.truetype(os.path.join(os.environ.get('WINDIR', r'C:\Windows'), 'Fonts', 'arialbd.ttf'), 22)
    except OSError:
        font = ImageFont.load_default()
    d.text((128, 120), 'NAME', font=font, fill=(255, 255, 255, 255), anchor='mm', stroke_width=3, stroke_fill=(0, 0, 0, 255))
    buf = io.BytesIO()
    hyb.save(buf, 'PNG')
    return [('*vworld.kr*/Satellite/*', 'image/jpeg', jpg), ('*vworld.kr*/Hybrid/*', 'image/png', buf.getvalue())]


def names_check(name, timeline, marks):
    vw = [(i, u) for i, (k, u) in enumerate(timeline) if k == 'req' and 'vworld.kr' in u]

    def cnt(a, b, layer):
        return sum(1 for i, u in vw if a <= i < b and '/%s/' % layer in u)
    end = len(timeline)
    if name == 'names':
        a, b = marks.get('off', -1), marks.get('on', -1)
        if a < 0 or b < 0:
            return False, 'MARK 없음'
        s0, h0, h1 = cnt(0, a, 'Satellite'), cnt(0, a, 'Hybrid'), cnt(a, b, 'Hybrid')
        return s0 > 0 and h0 == 0 and h1 > 0, '이름 끔: 위성 %d · 이름 %d  →  이름 켬 뒤: 이름 %d' % (s0, h0, h1)
    a, b = marks.get('start', -1), marks.get('off', -1)
    if a < 0 or b < 0:
        return False, 'MARK 없음'
    s0, h0, s1, h1 = cnt(0, a, 'Satellite'), cnt(0, a, 'Hybrid'), cnt(a, b, 'Satellite'), cnt(a, end, 'Hybrid')
    return s0 > 0 and h0 > 0 and s1 > 0 and h1 == 0, '다시 열기 (켜짐 기억): 위성 %d · 이름 %d  →  끄고 줌 바꾼 뒤: 위성 %d · 이름 %d' % (s0, h0, s1, h1)


class _Quiet(http.server.SimpleHTTPRequestHandler):
    def log_message(self, *a):
        pass


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--out-missing', default=None)
    ap.add_argument('--nav', default=None, help='make_synth --nav 폴더 (없으면 <out>_nav 에 작게 만든다)')
    ap.add_argument('--shots', default=None)
    args = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    out_dir = os.path.abspath(args.out)
    shots_dir = os.path.abspath(args.shots or os.path.join(out_dir, '_shots'))
    os.makedirs(shots_dir, exist_ok=True)
    prof = os.path.join(shots_dir, '_prof_i')
    nav_dir = os.path.abspath(args.nav) if args.nav else out_dir + '_nav'
    if not args.nav:
        subprocess.run([sys.executable, '-I', os.path.join(HERE, 'make_synth.py'), '--nav', '--width', '1024', '--out', nav_dir],
                       check=True, stdout=subprocess.DEVNULL)

    # 3번 자리에서 2번 땅점을 화면 가운데로 (두 점이 같은 북 좌표에 있다: 서쪽을 내려다봄)
    e3, e2 = cv.ms.SHOTS[2][0], cv.ms.SHOTS[1][0]
    look = (math.degrees(math.atan2(e2 - e3, 0.0)), -math.degrees(math.atan2(cv.ms.HEIGHT, abs(e3 - e2))))
    HOSTQ = 't=host&shot=0002&yaw=80&pitch=-45&fov=90'
    runs = [
        dict(name='nav', folder=out_dir, query='t=nav&shot=0002&yaw=0&pitch=-45'),
        dict(name='marker', folder=out_dir, query='t=marker&shot=0003&yaw=%.4f&pitch=%.4f&fov=90' % look),
        dict(name='ui', folder=out_dir, query='t=ui&shot=0002'),
        dict(name='drag', folder=out_dir, query='t=drag&shot=0002&yaw=0&pitch=0&fov=90'),
    ]
    if args.out_missing:
        runs.append(dict(name='nav_missing', folder=os.path.abspath(args.out_missing), query='t=nav&missing=1&shot=0002'))
    runs += [
        dict(name='arrows', folder=nav_dir, query='t=arrows&shot=0001&yaw=0&pitch=-30&fov=90'),
        dict(name='arrows40', folder=nav_dir, query='t=arrows40&shot=0001&yaw=40&pitch=-30&fov=90'),
        dict(name='host', folder=out_dir, query=HOSTQ, host=True),
        dict(name='host_late', folder=out_dir, query='t=host_late&late=1&shot=0002', host=True),
        dict(name='host_http', folder=out_dir, query=HOSTQ, host=True, http=True),
        dict(name='names', folder=out_dir, query='t=names&shot=0002', names=True, keep=True),
        dict(name='names2', folder=out_dir, query='t=names2&shot=0002', names=True),
    ]

    all_ok = True
    srv = http.server.ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(_Quiet, directory=out_dir))
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    ed = None
    try:
        for r in runs:
            name, folder = r['name'], r['folder']
            page = prepare(folder, host=r.get('host', False))
            png = os.path.join(shots_dir, 'i_' + name + '.png')
            pipes_js = os.path.join(folder, 'data', 'pipes.js')
            pipes_old = open(pipes_js, encoding='utf-8').read() if r.get('host') else None
            wrote = []

            def on_cmd(cmd, _p=pipes_js, _w=wrote):
                if cmd == 'write_pipes':  # 진짜 프로그램처럼 data/pipes.js 를 새로 쓴 뒤 돌아감
                    with open(_p, 'w', encoding='utf-8', newline='\n') as f:
                        f.write(new_pipes_js())
                    _w.append(cmd)
            if r.get('http'):
                url = 'http://127.0.0.1:%d/%s?%s' % (srv.server_address[1], urllib.parse.quote(page), r['query'])
            else:
                url = pathlib.Path(folder, page).resolve().as_uri() + '?' + r['query']
            if ed is None:  # names → names2 는 같은 Edge (저장소 기억)
                ed = Edge(prof, cv.WIN).__enter__()
                if r.get('names'):
                    ed.net(block=['*openstreetmap.org*'])
                    ed.fake(fake_tiles(), catch=['*vworld.kr*'])
                else:
                    ed.net(block=BLOCK)
            try:
                st, other, reqs, timeline, marks, pngs = drive(ed, url, png, shots_dir, 'i_' + name, on_cmd)
                faked = list(ed.faked)
            finally:
                if not r.get('keep'):
                    ed.__exit__(None, None, None)
                    ed = None
                if pipes_old is not None:
                    with open(pipes_js, 'w', encoding='utf-8', newline='\n') as f:
                        f.write(pipes_old)
            print('== %s  (%s%s)' % (name, 'http · ' if r.get('http') else '', r['query']))
            for s in st:
                if not s.startswith(('SNAP ', 'MARK ', 'HOSTCMD ', 'CALIB ')):
                    print('   ', s)
            for o in other:
                print('    JS:', o)
            osm = [u for u in reqs if 'openstreetmap' in u]
            vw = [u for u in reqs if 'vworld.kr' in u]
            bad_osm = bool(osm) and not r.get('http')  # http 는 OSM 이 바탕 (막아 둠 · check_net 과 같음)
            print('    네트워크: OSM 청 %d · VWorld 청 %d%s' % (len(osm), len(vw), '  실패 (file:// 에서 OSM 금지)' if bad_osm else ''))
            done = [s for s in st if s.startswith('DONE')]
            ok = bool(done) and 'ALL-PASS' in done[0] and not other and not bad_osm
            if r.get('host'):
                restored = open(pipes_js, encoding='utf-8').read() == pipes_old
                print('    pipes.js 새로 씀 %d 번 · 되돌림 %s' % (len(wrote), restored))
                ok &= restored and (name == 'host_late' or wrote == ['write_pipes'])
            if r.get('names'):
                good, txt = names_check(name, timeline, marks)
                got = {u for u in faked if isinstance(u, str)}
                fails = [u for u in faked if not isinstance(u, str)]
                out = [u for u in vw if u not in got]
                print('    %s  %s' % (txt, '통과' if good else '실패'))
                print('    VWorld 청 %d 개 모두 가짜 타일로 답함 (밖으로 안 나감): %s · 막음 %d' % (len(vw), not out, len(fails)))
                ok &= good and not out and not fails
            if name == 'drag':
                a = np.asarray(Image.open(png).convert('RGB'))
                x0, y0, x1, y1 = cv.view_rect(a)
                view = a[y0:y1 + 1, x0:x1 + 1]
                w, h = view.shape[1], view.shape[0]
                fov1 = min(120.0, max(30.0, 90.0 * math.exp(-100 * 0.0012)))
                t = math.tan(math.radians(fov1) / 2)
                tanh = t if w >= h else t * w / h
                dw = [s for s in st if s.startswith('DRAGW')]
                wd = float(dw[0].split()[1]) if dw else w  # 끌 때의 캔버스 폭
                dyaw = 200 * math.degrees(tanh) / (wd / 2)  # 앱과 같은 끌기 규칙: 가운데 1 px 당 각도
                proj, proj_dir = cv.projector(('drag', '0002', -dyaw, 0, fov1, ''), w, h, {})
                ex, ey = proj_dir(0.0, 6.0)
                hz = proj_dir(0.0, 0.0)[1]
                ys, xs = np.nonzero(view.max(axis=2) < 70)
                sel = (np.abs(xs - ex) < 150) & (ys > hz - 120) & (ys < hz)
                if sel.sum() < 20:
                    print('    N 글자 못 찾음')
                    ok = False
                else:
                    gx, gy = xs[sel].mean() + 0.5, ys[sel].mean() + 0.5  # 픽셀 번호 → 픽셀 가운데 좌표
                    good = abs(gx - ex) <= 2 and abs(gy - ey) <= 4
                    ok &= good
                    print('    휠 뒤 fov %.2f°, 끌 때 폭 %.1f px, 끌기 뒤 yaw %.2f° → N 글자 (%.1f, %.1f) · 계산 (%.1f, %.1f)  %s'
                          % (fov1, wd, -dyaw, gx, gy, ex, ey, '통과' if good else '실패'))
            print('   결과:', '통과' if ok else '실패', '· 사진:', ' · '.join(pngs + [png]))
            all_ok &= ok
            cleanup(folder)
    finally:
        if ed is not None:
            ed.__exit__(None, None, None)
        srv.shutdown()
    shutil.rmtree(prof, ignore_errors=True)
    print('전체:', '통과' if all_ok else '실패')
    return 0 if all_ok else 1


if __name__ == '__main__':
    sys.exit(main())
