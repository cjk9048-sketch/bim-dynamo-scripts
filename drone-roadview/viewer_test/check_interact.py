"""조작 시험: 시험 폴더에 selftest.js 를 넣은 복사본 html 을 만들어 화면 없는 Edge(cdp.py, 실제 시간)로 돌린다.

    python -I check_interact.py --out <make_synth 폴더> [--out-missing <--no-pipes --drop 0003 폴더>] [--shots <폴더>]

selftest.js 가 남긴 'SELFTEST PASS/FAIL' 줄과 JS 오류(잡히지 않은 오류 · console.error)를 모은다.
'ui' 시험은 가짜 VWorld 키로 바꾼다 (청은 막아 둠 → 실패 → 기본 바탕으로 돌아오는지).
모든 경우에 네트워크 청을 기록해 file:// 에서 OSM 타일을 청하지 않는지 본다 (OSM · VWorld 주소는 막아 밖으로 안 나감).
끌기 시험은 끝난 화면에서 N 글자 자리를 계산 값과 견준다 (휠 줌 + 200 px 끌기).
"""
import argparse
import math
import os
import pathlib
import shutil
import sys
import time

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import check_view as cv  # noqa: E402
from cdp import Edge  # noqa: E402

PAGE = '로드뷰_selftest.html'


def prepare(out_dir):
    src = os.path.join(out_dir, '로드뷰.html')
    with open(src, encoding='utf-8') as f:
        html = f.read()
    tag = '<script src="viewer/app.js"></script>'
    assert tag in html
    html = html.replace(tag, tag + '\n<script src="selftest.js"></script>')
    with open(os.path.join(out_dir, PAGE), 'w', encoding='utf-8', newline='\n') as f:
        f.write(html)
    shutil.copyfile(os.path.join(HERE, 'selftest.js'), os.path.join(out_dir, 'selftest.js'))


def run(out_dir, query, png, prof):
    """새 Edge 하나로 (저장소가 섞이지 않게) selftest 를 끝까지 돌리고 찍는다."""
    uri = pathlib.Path(out_dir, PAGE).resolve().as_uri() + '?' + query
    with Edge(prof, cv.WIN) as ed:
        ed.net(block=['*openstreetmap.org*', '*vworld.kr*'])
        ed.goto(uri)
        t0 = time.time()
        while time.time() - t0 < 120:
            ed.pump(0.3)
            if any(t.startswith('SELFTEST DONE') for _, t in ed.console):
                break
        ed.frames(3)
        ed.screenshot(png)
        st = [t[len('SELFTEST '):] for _, t in ed.console if t.startswith('SELFTEST ')]
        other = [str(x)[:220] for x in ed.exceptions] + [t[:220] for k, t in ed.console if k == 'error']
        osm = [u for u in ed.requests if 'openstreetmap' in u]
        vw = [u for u in ed.requests if 'vworld.kr' in u]
    return st, other, osm, vw


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--out-missing', default=None)
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

    # 3번 자리에서 2번 땅점을 화면 가운데로 (두 점이 같은 북 좌표에 있다: 서쪽을 내려다봄)
    e3, e2 = cv.ms.SHOTS[2][0], cv.ms.SHOTS[1][0]
    look = (math.degrees(math.atan2(e2 - e3, 0.0)), -math.degrees(math.atan2(cv.ms.HEIGHT, abs(e3 - e2))))
    runs = [
        ('nav', out_dir, 't=nav&shot=0002&yaw=0&pitch=-45'),
        ('marker', out_dir, 't=marker&shot=0003&yaw=%.4f&pitch=%.4f&fov=90' % look),
        ('ui', out_dir, 't=ui&shot=0002'),
        ('drag', out_dir, 't=drag&shot=0002&yaw=0&pitch=0&fov=90'),
    ]
    if args.out_missing:
        runs.append(('nav_missing', os.path.abspath(args.out_missing), 't=nav&missing=1&shot=0002'))

    all_ok = True
    for name, folder, query in runs:
        prepare(folder)
        png = os.path.join(shots_dir, 'i_' + name + '.png')
        st, other, osm, vw = run(folder, query, png, prof)
        print('== %s  (%s)' % (name, query))
        for s in st:
            print('   ', s)
        for o in other:
            print('    JS:', o)
        print('    네트워크: OSM 청 %d · VWorld 청 %d%s' % (len(osm), len(vw), '  실패 (file:// 에서 OSM 금지)' if osm else ''))
        done = [s for s in st if s.startswith('DONE')]
        ok = bool(done) and 'ALL-PASS' in done[0] and not other and not osm
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
        print('   결과:', '통과' if ok else '실패', '· 사진:', png)
        all_ok &= ok
        for f in (PAGE, 'selftest.js'):
            p = os.path.join(folder, f)
            if os.path.exists(p):
                os.remove(p)
    shutil.rmtree(prof, ignore_errors=True)
    print('전체:', '통과' if all_ok else '실패')
    return 0 if all_ok else 1


if __name__ == '__main__':
    sys.exit(main())
