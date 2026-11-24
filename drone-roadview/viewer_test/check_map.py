"""지도 칸 시험 (JACK 1124): 사진 점 솎기 · 사진점 지우기. make_synth 폴더의 project.js 를 잠깐 «사진 많은 비행» (300 장 · 600 m ·
영상 둘 · id 가 0051 부터 · 사이 빈 번호) 으로 바꿔 화면 없는 Edge (cdp.py, 실제 시간 · 진짜 마우스) 로 다룬다 (끝나면 파일 그대로 되돌림).

    python -I check_map.py --out <make_synth 폴더> [--shots <폴더>]

  d  사진 점 솎기: 전체 보기 (낮은 줌) 에서 그린 점 < 사진 수 · 숨긴 점 있음 · 화면의 흰 점 덩어리 (따로 셈) 가 서로 9 px 넘게 떨어짐 ·
     영상마다 처음 · 끝 점은 그림 · 가장 큰 줌에서는 화면 안 점을 하나도 안 숨김 · 숨긴 점을 눌러도 가장 가까운 사진으로 감
  t  사진점 지우기: 프로그램 없으면 [가위] 없음 · 프로그램 (hostmock) 에서 [가위] → 점 둘 (같은 영상) → 사이 (둘 포함) 수 · 빨간 점 ·
     다른 영상 점 → 안 받음 · Esc → 취소 (trim_shots 안 부름) · [N개 지우기] → trim_shots(ids) (따로 셈과 같음) → 목록 · 지도에서 빠짐 ·
     지금 사진을 지우면 가장 가까운 남은 사진 · 프로그램이 거절하면 그대로
  모든 쪽 JS 오류 0
"""
import argparse
import json
import math
import os
import pathlib
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import check_edit as ce  # noqa: E402
import check_interact as ci  # noqa: E402
import check_view as cv  # noqa: E402
import make_synth as ms  # noqa: E402
from cdp import Edge  # noqa: E402

N = 300
R = {'ok': True, 'errs': []}


def report(name, ok, text=''):
    print('   %-46s %s  %s' % (name, text, '통과' if ok else '실패'))
    R['ok'] &= bool(ok)
    return bool(ok)


def errs(ed, where):
    e = cv.errors(ed)
    for x in e:
        R['errs'].append((where, str(x)[:200]))
    return report('JS 오류 (%s)' % where, not e, '%d 줄' % len(e))


def js_json(ed, expr):
    return json.loads(ed.js('JSON.stringify(%s)' % expr))


def many_shots():
    """600 m · 2 m 간격 · 영상 둘 (앞 150 · 뒤 150) · id 0051 부터 (0100 은 빔)."""
    out, nid = [], 51
    for i in range(N):
        if nid == 100:
            nid += 1
        e = 2.0 * i
        lat, lon = ms.en2ll(e, 0.0)
        out.append({'id': '%04d' % nid, 'seq': i + 1, 'video': '0001' if i < N // 2 else '0002', 'time': 10.0 + i * 0.5,
                    'lat': lat, 'lon': lon, 'e': e, 'n': 0.0, 'z': ms.GZ + ms.HEIGHT, 'agl': ms.HEIGHT, 'groundZ': ms.GZ, 'yaw': 90.0})
        nid += 1
    return out


def lon2x(lon):
    return (lon + 180.0) / 360.0


def lat2y(lat):
    s = math.sin(math.radians(lat))
    return 0.5 - math.log((1 + s) / (1 - s)) / (4 * math.pi)


class MapView:
    """지금 지도 (가운데 · 줌 = map.dataset.view) 에서 사진 점 화면 자리 (client) · 메르카토르는 따로 짬."""

    def __init__(self, ed):
        r = js_json(ed, "document.getElementById('map').getBoundingClientRect()")
        self.l, self.t, self.w, self.h = r['left'], r['top'], r['width'], r['height']
        self.cx, self.cy, self.z = [float(v) for v in ed.js("document.getElementById('map').dataset.view").split(',')]
        self.k = 256 * 2 ** self.z

    def scr(self, s):
        return (self.l + (lon2x(s['lon']) - self.cx) * self.k + self.w / 2, self.t + (lat2y(s['lat']) - self.cy) * self.k + self.h / 2)


def zoom_at(ed, shot, z):
    """그 사진 점 자리에서 마우스 휠로 줌 z 까지 (휠은 마우스 아래 자리를 그대로 둠)."""
    for _ in range(12):
        mv = MapView(ed)
        if mv.z >= z - 1e-9:
            break
        x, y = mv.scr(shot)
        ed.call('Input.dispatchMouseEvent', {'type': 'mouseWheel', 'x': float(x), 'y': float(y), 'deltaX': 0, 'deltaY': -400})
        ed.frames(2)


def zoom_to(ed, z):
    for _ in range(12):
        if MapView(ed).z >= z - 1e-9:
            break
        ed.js("document.getElementById('zIn').click()")
        ed.frames(2)


def blobs(img, thr=235):
    """흰 화소 덩어리 (4 이웃) 가운데들."""
    m = (img >= thr).all(axis=2)
    seen = np.zeros_like(m)
    out = []
    H, W = m.shape
    for y0, x0 in zip(*np.nonzero(m)):
        if seen[y0, x0]:
            continue
        st, px = [(y0, x0)], []
        seen[y0, x0] = True
        while st:
            y, x = st.pop()
            px.append((y, x))
            for yy, xx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                if 0 <= yy < H and 0 <= xx < W and m[yy, xx] and not seen[yy, xx]:
                    seen[yy, xx] = True
                    st.append((yy, xx))
        if len(px) >= 4:
            a = np.array(px, float)
            out.append(a.mean(axis=0)[::-1])
    return np.array(out).reshape(-1, 2)


def grab_map(ed, png):
    ed.frames(3)
    ed.screenshot(png)
    r = ce.rect(ed, 'map')
    a = np.asarray(Image.open(png).convert('RGB')).astype(float)
    l, t = int(round(r['left'])), int(round(r['top']))
    return a[t + 2:t + int(round(r['height'])) - 2, l + 2:l + int(round(r['width'])) - 2]


def url(out_dir, page='로드뷰.html', extra=''):
    return pathlib.Path(out_dir, page).resolve().as_uri() + '?shot=0051&yaw=90&pitch=-45&fov=90' + extra


def load(ed, u):
    ed.goto(u)
    ok = ed.wait_js(cv.READY, timeout=60)
    ed.frames(4)
    return ok


def cur_shot(ed):
    """(지금 사진 seq, 사진 수) · #shotNo «seq / N»."""
    t = ed.js("document.getElementById('shotNo').textContent")
    a, b = t.split('/')
    return int(a), int(b)


# ---------------------------------------------------------------- d  사진 점 솎기
def test_dots(ed, out_dir, shots_dir, shots):
    print('== d  사진 점 솎기 (줌 따라)')
    load(ed, url(out_dir))
    ed.js('localStorage.clear()')
    report('불러옴 (사진 %d 장)' % N, load(ed, url(out_dir)))
    ed.js("document.getElementById('tgPath').click()")  # 비행 경로 끔 (흰 점 덩어리를 따로 세게)
    img = grab_map(ed, os.path.join(shots_dir, 'd_fit.png'))
    st = js_json(ed, "Object.assign({}, document.getElementById('map').dataset)")
    dots, skip = int(st.get('dots', -1)), int(st.get('skip', -1))
    report('낮은 줌: 그린 점 < 사진 수 · 숨긴 점 있음', 0 < dots < 0.35 * N and skip > N * 0.5, '그림 %d · 숨김 %d · 사진 %d' % (dots, skip, N))
    B = blobs(img)
    d = np.sqrt(((B[:, None, :] - B[None, :, :]) ** 2).sum(axis=2)) if len(B) > 1 else np.array([[np.inf]])
    np.fill_diagonal(d, np.inf)
    mf = MapView(ed)
    forced = [np.array(mf.scr(s)) - np.array([mf.l + 2, mf.t + 2]) for s in (shots[0], shots[N // 2 - 1], shots[N // 2], shots[-1])]
    free = np.array([all(np.hypot(*(p - q)) > 3 for q in forced) for p in B]) if len(B) else np.zeros(0, bool)
    dd = d[free][:, free] if free.any() else np.array([[np.inf]])
    report('흰 점 덩어리가 따로 (늘 그리는 끝 점 빼고 서로 9 px 넘게)', len(B) >= 0.7 * dots and dd.min() > 9,
           '덩어리 %d · 가장 가까운 둘 %.1f px (끝 점 포함 %.1f)' % (len(B), dd.min(), d.min()))
    ends = [shots[0], shots[N // 2 - 1], shots[N // 2], shots[-1]]
    ok_end = []
    for s in ends[1:]:  # 처음 (지금 사진) 은 따로 그림
        x, y = mf.scr(s)
        x, y = x - mf.l - 2, y - mf.t - 2
        ok_end.append(bool(len(B)) and float(np.min(np.hypot(B[:, 0] - x, B[:, 1] - y))) < 2.5)
    report('영상마다 끝 점은 그림 (영상 1 끝 · 영상 2 처음 · 끝)', all(ok_end), json.dumps(ok_end))
    # 숨긴 점 누르기 → 가장 가까운 사진
    hidden = shots[N // 4 + 1]
    x, y = mf.scr(hidden)
    inp = ce.Inp(ed)
    inp.click(x, y)
    ed.wait_js(cv.READY, timeout=30)
    ed.frames(3)
    seq, _ = cur_shot(ed)
    near = min(shots, key=lambda s: math.hypot(mf.scr(s)[0] - x, mf.scr(s)[1] - y))
    report('숨긴 점 자리 누름 → 가장 가까운 사진', abs(seq - near['seq']) <= 1, '%d (누른 곳 사진 %d)' % (seq, hidden['seq']))
    # 가장 큰 줌: 화면 안 점 안 숨김
    zoom_to(ed, 22)
    st2 = js_json(ed, "Object.assign({}, document.getElementById('map').dataset)")
    report('큰 줌 (22): 화면 안 점 하나도 안 숨김', int(st2.get('skip', -1)) == 0 and int(st2.get('dots', 0)) >= 3, json.dumps(st2))
    mv = MapView(ed)
    cur = cur_shot(ed)[0]
    inside = [s for s in shots if s['seq'] != cur and -20 <= mv.scr(s)[0] - mv.l <= mv.w + 20 and -20 <= mv.scr(s)[1] - mv.t <= mv.h + 20]
    report('큰 줌: 그린 점 = 화면 안 사진 (따로 셈)', int(st2.get('dots', 0)) == len(inside), '그림 %s · 따로 %d' % (st2.get('dots'), len(inside)))
    grab_map(ed, os.path.join(shots_dir, 'd_zoom.png'))
    ed.js('localStorage.clear()')
    errs(ed, 'd')


# ---------------------------------------------------------------- e  관로 고치기 중에도 사진 점 → 그 사진 (1124 JACK)
def test_edit_dot(ed, out_dir, shots_dir, shots):
    print('== e  관로 고치기 중 사진 점 누름')
    load(ed, url(out_dir))
    ed.js('localStorage.clear()')
    load(ed, url(out_dir))
    ed.js("document.getElementById('tgEdit').click()")
    ed.frames(3)
    on = ed.js("document.getElementById('edChip').classList.contains('show')")
    report('관로 고치기 켜짐', on)
    zoom_to(ed, 20)
    mf = MapView(ed)
    cur = cur_shot(ed)[0]
    vis = [s for s in shots if abs(s['seq'] - cur) >= 3 and mf.l + 20 < mf.scr(s)[0] < mf.l + mf.w - 20 and mf.t + 20 < mf.scr(s)[1] < mf.t + mf.h - 20]
    if not vis:
        report('화면 안 다른 사진 점 있음', False)
        return
    target = vis[len(vis) // 2]
    ce.Inp(ed).click(*mf.scr(target))
    ed.wait_js(cv.READY, timeout=30)
    ed.frames(3)
    seq = cur_shot(ed)[0]
    still = ed.js("document.getElementById('edChip').classList.contains('show')")
    report('고치기 중 지도 사진 점 → 그 사진으로 (고치기는 그대로)', seq == target['seq'] and still,
           '지금 %d · 누른 %d · 고치기 %s' % (seq, target['seq'], still))
    ed.js("document.getElementById('tgEdit').click()")
    ed.js('localStorage.clear()')
    errs(ed, 'e')


# ---------------------------------------------------------------- t  사진점 지우기
def trim_chip(ed):
    return js_json(ed, "{show: document.getElementById('tmChip').classList.contains('show'), n: Number(document.getElementById('tmChip').dataset.n || 0),"
                       " tip: document.getElementById('tmTip').textContent, go: document.getElementById('tmGo').hidden ? '' : document.getElementById('tmGo').textContent}")


def test_trim(ed, out_dir, shots_dir, shots):
    print('== t  사진점 지우기')
    ed.js('localStorage.clear()')
    load(ed, url(out_dir))
    report('프로그램 없으면 [가위] 없음', ed.js("document.getElementById('tgTrim').hidden"))
    page = ci.prepare(out_dir, host=True)
    try:
        report('불러옴 (프로그램)', load(ed, url(out_dir, page)))
        inp = ce.Inp(ed)
        r = js_json(ed, "document.querySelector('[aria-label=\"사진점 지우기\"]').getBoundingClientRect()")
        report('[가위] 보임 (사진점 지우기)', r['width'] > 10)
        zoom_to(ed, 20)  # 점 사이 2 m = 17 px (다 그림)
        mf = MapView(ed)
        vis = [s for s in shots if mf.l + 15 < mf.scr(s)[0] < mf.l + mf.w - 15]
        a, b = vis[3], vis[12]
        inp.click(r['left'] + r['width'] / 2, r['top'] + r['height'] / 2)
        c0 = trim_chip(ed)
        report('[가위] → 안내 «첫 점»', c0['show'] and c0['tip'] == '첫 점', json.dumps(c0, ensure_ascii=False))
        inp.click(*mf.scr(a))
        c1 = trim_chip(ed)
        inp.click(*mf.scr(b))
        c2 = trim_chip(ed)
        want = [s['id'] for s in shots if a['seq'] <= s['seq'] <= b['seq']]
        report('점 둘 → 사이 (둘 포함) 수 · [N개 지우기]', c1['tip'] == '끝 점' and c2['n'] == len(want) and c2['go'] == '%d개 지우기' % len(want),
               '%s → %s (따로 %d)' % (json.dumps(c1, ensure_ascii=False), json.dumps(c2, ensure_ascii=False), len(want)))
        img = grab_map(ed, os.path.join(shots_dir, 't_pick.png'))
        red = ((img[..., 0] > 200) & (img[..., 1] < 100) & (img[..., 2] < 100)).sum()
        report('고른 점 빨갛게', red > 15 * len(want), '빨간 화소 %d' % red)
        inp.key('Escape', 'Escape', 27)
        c3 = trim_chip(ed)
        calls = js_json(ed, "window.__host.calls.filter((c) => c.name === 'trim_shots').length")
        report('Esc → 취소 (trim_shots 안 부름)', not c3['show'] and calls == 0, json.dumps(c3, ensure_ascii=False))
        # 다른 영상 점 → 안 받음
        ed.js("document.getElementById('zFit').click()")
        ed.frames(3)
        mf = MapView(ed)
        inp.click(r['left'] + r['width'] / 2, r['top'] + r['height'] / 2)
        inp.click(*mf.scr(shots[10]))
        inp.click(*mf.scr(shots[N // 2 + 40]))
        c4 = trim_chip(ed)
        report('다른 영상 점 → 안 받음', c4['tip'] == '끝 점' and c4['n'] == 0, json.dumps(c4, ensure_ascii=False))
        inp.key('Escape', 'Escape', 27)
        # 지금 사진 포함 지우기 → 가장 가까운 남은 사진
        load(ed, url(out_dir, page).replace('shot=0051', 'shot=%s' % shots[20]['id']))
        report('지금 사진 = %d' % shots[20]['seq'], cur_shot(ed) == (shots[20]['seq'], N), json.dumps(cur_shot(ed)))
        zoom_at(ed, shots[21], 20)
        mf = MapView(ed)
        at = mf.scr
        ed.js("window.__host.trims.push({ok: false, msg: '못 지움 (시험)'})")
        inp.click(r['left'] + r['width'] / 2, r['top'] + r['height'] / 2)
        inp.click(*at(shots[18]))
        inp.click(*at(shots[24]))
        c5 = trim_chip(ed)
        ed.js("document.getElementById('tmGo').click()")
        ed.frames(6)
        report('프로그램 거절 → 그대로 (사진 수 · 안내 줄)', cur_shot(ed)[1] == N and trim_chip(ed)['show'] and len(ed.js("window.RV_PROJECT.shots.length + ''")) > 0,
               '고른 %d · %s' % (c5['n'], json.dumps(cur_shot(ed))))
        ed.js("document.getElementById('tmGo').click()")
        ed.wait_js("!document.getElementById('tmChip').classList.contains('show')", timeout=10)
        ed.wait_js(cv.READY, timeout=30)
        ed.frames(6)
        ids = js_json(ed, "window.__host.calls.filter((c) => c.name === 'trim_shots').map((c) => c.arg)")
        want = [s['id'] for s in shots[18:25]]
        seq, n = cur_shot(ed)
        left = js_json(ed, "window.RV_PROJECT.shots.map((s) => s.id)")
        report('[N개 지우기] → trim_shots(ids) = 따로 셈', len(ids) == 2 and ids[-1] == want, json.dumps(ids[-1] if ids else None))
        report('목록에서 빠짐 (사진 수 · project)', n == N - len(want) and len(left) == N - len(want) and not set(want) & set(left), '%d 장' % n)
        report('지운 지금 사진 → 가장 가까운 남은 사진 (seq 18 또는 26)', seq in (shots[17]['seq'], shots[25]['seq']), 'seq %d' % seq)
        img = grab_map(ed, os.path.join(shots_dir, 't_after.png'))
        ed.js("document.getElementById('zOut').click()")
        errs(ed, 't')
    finally:
        ci.cleanup(out_dir)
        ed.js('localStorage.clear()')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--shots', default=None)
    ap.add_argument('--only', default='', help='d,t 중 일부만')
    args = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    out_dir = os.path.abspath(args.out)
    shots_dir = os.path.abspath(args.shots or os.path.join(out_dir, '_shots_map'))
    os.makedirs(shots_dir, exist_ok=True)
    pj = os.path.join(out_dir, 'data', 'project.js')
    orig = open(pj, 'rb').read()
    t = orig.decode('utf-8')
    P = json.loads(t[t.index('{'):t.rindex('}') + 1])
    shots = many_shots()
    only = set(args.only.split(',')) if args.only else set('det')
    try:
        with open(pj, 'w', encoding='utf-8', newline='\n') as f:
            f.write('window.RV_PROJECT = ' + json.dumps(dict(P, shots=shots, title='사진 많은 시험'), ensure_ascii=False) + ';\n')
        ed = Edge(os.path.join(shots_dir, '_prof_m'), cv.WIN).__enter__()
        try:
            ed.net(block=ci.BLOCK)
            if 'd' in only:
                test_dots(ed, out_dir, shots_dir, shots)
            if 'e' in only:
                test_edit_dot(ed, out_dir, shots_dir, shots)
            if 't' in only:
                test_trim(ed, out_dir, shots_dir, shots)
        finally:
            ed.__exit__(None, None, None)
    finally:
        with open(pj, 'wb') as f:
            f.write(orig)
    for where, e in R['errs']:
        print('   오류 [%s]: %s' % (where, e))
    R['ok'] &= not R['errs']
    print('사진:', shots_dir)
    print('전체:', '통과' if R['ok'] else '실패')
    return 0 if R['ok'] else 1


if __name__ == '__main__':
    sys.exit(main())
