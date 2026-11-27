"""3D 매핑 보기 · 간단 부지정지 시험 (JACK 1127): 결과 폴더를 작은 HTTP 서버로 열고 화면 없는 Edge (cdp.py) 를 진짜 마우스로 몬다.
원래 data 파일은 건드리지 않음 — 시험용 data/grade_saved.js · data/dem_flat.js (평평한 땅) · 페이지 index_grade*.html ·
가짜 프로그램 API viewer/hostmock_grade.js (save_grade · grade_shp). data/grade.js 는 만들지 않음 (다른 시험 페이지가 읽음).

    python -X utf8 -I check_map3d_grade.py --out <3D 매핑 결과 폴더 (check_map3d_ug 와 같은 것)> --shots <그림 폴더>

  a  평평한 땅 손셈: 20 m 네모 · 성토 2 m (1:1.5 · 소단 없음) = 400×2 + 둘레×h²·n/2 + π·h³·n²/3 ·
     절토 8 m (1:1 · 5 m 마다 소단 1 m) = 1차원 적분 · 마디 갈래 (평탄면 · 절토 · 소단 · 그대로) · 높이
  d  그리기: [다각형 그리기] → 위에서 네 점 → Enter → 새 부지 (점 = 누른 자리 · 계획고 = 땅 평균) · 목록 한 줄 · 설정 칸
  q  실제 땅 (dem.js) 수량 = 파이썬 따로 셈 (0.25 m 격자) 과 3 % 안 · 계획고 올리면 성토 ↑ 절토 ↓ · 수량 «정지» 갈래 · 전체 갈래
  g  그림: 위에서 · 비스듬히 · 부지 안 배경색 화소 없음 · 평탄면 = 회색 · 절토 = 흙색 · 성토 = 풀색 · [보이기] 끄면 원래 모델 · 맨땅 보기
  s  저장 (save_grade · 0.8 초 모아) · 다시 열면 그대로 · SHP (grade_shp 흉내 · 못 읽음 알림) · 부지 누르기 = 고름 · 지우기 (×)
  b  브라우저 (웹 공유) = 보기만 (그리기 · SHP · × 없음 · 칸 잠금) · 그림 · 수량
  p  app.py _grade_read: 다각형 SHP (구멍 뺌 · 이름 · 계획고 항목 · .prj 바꿈 · 선 SHP 거절) · save_grade
  모든 쪽 JS 오류 0
"""
import argparse
import base64
import collections
import json
import math
import os
import shutil
import struct
import sys
import tempfile
import time

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
sys.path.insert(0, REPO)
from cdp import Edge  # noqa: E402
import check_edit as ce  # noqa: E402
from check_map3d_ug import BG, J, R, cat_open, errs, report, serve, setup_page, shot, ui_click  # noqa: E402
from check_map3d_ctx import calls  # noqa: E402

WIN = (1600, 900)
ZF = 34.0                         # 평평한 땅 표고 (data/dem_flat.js)
GK = ['gd_cut', 'gd_fill', 'gd_pad', 'gd_area']

MOCK_JS = r"""(function () {
  const H = window.__host, api = window.pywebview && window.pywebview.api;
  if (!H || !api) return;
  const rec = (name, arg) => { H.calls.push({ name: name, arg: arg, t: performance.now() }); };
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  H.gshp = [];
  api.save_grade = (text) => { rec('save_grade', text); return wait(10).then(() => ({ ok: true })); };
  api.grade_shp = () => { rec('grade_shp'); return wait(20).then(() => (H.gshp.length ? H.gshp.shift() : null)); };
})();
"""

HELP_JS = r"""
window.__g = {
  scr(x, y, z) { const M = __map3d, T = M.T(); M.view(); return M.toScr(x - T[0], y - T[1], z); },        // 세계 (x, y) · 표고 절대 → 화면
  ground(x, y) { return __map3d.gd.ground(x, y); },
  look(x, y, d, el, az) { const M = __map3d, g = this.ground(x, y); M.V.tgt = [x, y, (Number.isFinite(g) ? g : 35) - window.RV_MAPPING.origin[2]]; M.V.d = d; M.V.el = el; M.V.az = az; M.redraw(); return true; },
  find(i, k) {                              // 갈래 k 마디 (둘레 1.5 m 도 같은 갈래) → [x, y, z]
    const r = __map3d.gd.res(i);
    for (let j = 4; j < r.ny - 4; j += 2) for (let ii = 4; ii < r.nx - 4; ii += 2) {
      const x = r.X0 + (ii + 0.5) * r.c, y = r.Y0 + (j + 0.5) * r.c, a = __map3d.gd.at(i, x, y);
      if (!a || a.cls !== k) continue;
      let ok = true;
      for (const [dx, dy] of [[1.5, 0], [-1.5, 0], [0, 1.5], [0, -1.5], [1, 1], [-1, -1]]) { const b = __map3d.gd.at(i, x + dx, y + dy); if (!b || b.cls !== k) { ok = false; break; } }
      if (ok) return [x, y, a.z];
    }
    return null;
  },
  q(k, all) { const e = document.getElementById((all ? 'qt_' : 'q_') + k); return e ? Number(e.dataset.v) : NaN; },
  rows() { return Object.keys(__map3d.QT.rowEl).filter((k) => !__map3d.QT.rowEl[k][0].hidden); },
  saves() { return __host.calls.filter((c) => c.name === 'save_grade').map((c) => c.arg); },
  setIn(k, v) { const i = document.getElementById('gd_' + k); if (!i) return false; i.value = String(v); i.dispatchEvent(new Event('input')); i.dispatchEvent(new Event('change')); return true; },
};
"""


def read_js(path):
    t = open(path, encoding='utf-8').read()
    return json.loads(t[t.index('{'):t.rstrip().rstrip(';').rindex('}') + 1])


def prep(out):
    os.makedirs(os.path.join(out, 'viewer'), exist_ok=True)
    for fn in ('map3d.js', 'river.js'):
        shutil.copy2(os.path.join(REPO, 'viewer', fn), os.path.join(out, 'viewer', fn))
    shutil.copy2(os.path.join(HERE, 'hostmock.js'), os.path.join(out, 'viewer', 'hostmock.js'))
    open(os.path.join(out, 'viewer', 'hostmock_grade.js'), 'w', encoding='utf-8').write(MOCK_JS)
    html = open(os.path.join(REPO, 'viewer', 'map3d.html'), encoding='utf-8').read()
    for a, b in (('data/pipes.js', 'data/pipes_none.js'), ('data/obstacles.js', 'data/obst_none.js'), ('data/ai_budae.js', 'data/ai_none.js')):
        html = html.replace(a, b)                                     # 관로 · 지장물 · AI 없이 (누르기가 부지로 가게)
    host = lambda h: h.replace('<script src="viewer/map3d.js"></script>',   # noqa: E731
                               '<script src="viewer/hostmock.js"></script>\n<script src="viewer/hostmock_grade.js"></script>\n<script src="viewer/map3d.js"></script>')
    g = lambda f: html.replace('data/grade.js', 'data/' + f)          # noqa: E731
    pages = {'index_grade_flat_host.html': host(g('grade_none.js').replace('data/dem.js', 'data/dem_flat.js')),
             'index_grade_host.html': host(g('grade_none.js')),
             'index_grade_saved_host.html': host(g('grade_saved.js')),
             'index_grade_saved.html': g('grade_saved.js')}
    for name, h in pages.items():
        open(os.path.join(out, name), 'w', encoding='utf-8').write(h)
    D = read_js(os.path.join(out, 'data', 'dem.js'))
    n = (int(D['tile']) + 1) ** 2
    flat = base64.b64encode(np.full(n, ZF, '<f4').tobytes()).decode()
    D['tiles'] = {k: flat for k in D['tiles']}
    open(os.path.join(out, 'data', 'dem_flat.js'), 'w', encoding='utf-8').write('window.RV_DEM = ' + json.dumps(D) + ';\n')
    for f in ('grade_saved.js', 'grade_none.js'):
        p = os.path.join(out, 'data', f)
        if os.path.exists(p):
            os.remove(p)
    if os.path.exists(os.path.join(out, 'data', 'grade.js')):
        raise SystemExit('시험 폴더에 data/grade.js 가 있음 (다른 시험 페이지가 읽음) — 지우고 다시')


# ---------------------------------------------------------------- 손셈 · 따로 셈
def prof(d, n, bh, bw):
    d = np.asarray(d, float)
    if not (bh > 0 and bw > 0):
        return d / n, np.zeros(d.shape, bool)
    s = bh * n
    k = np.floor(d / (s + bw))
    r = d - k * (s + bw)
    return np.where(r > s, (k + 1) * bh, k * bh + r / n), r > s


def ring_vol(P, area, h, n, bh, bw):
    """볼록 다각형 (둘레 P · 넓이) · 평평한 땅 · 높이 차 h 의 정지 부피 = 넓이 × h + ∫ (h − 사면(d)) (P + 2πd) dd."""
    dd = np.linspace(0, 400, 400001)
    r, _ = prof(dd, n, bh, bw)
    f = np.clip(h - r, 0, None) * (P + 2 * math.pi * dd)
    return area * h + float(np.sum((f[1:] + f[:-1]) / 2 * np.diff(dd)))


def dem_sampler(out):
    D = read_js(os.path.join(out, 'data', 'dem.js'))
    T, x0, y0, cell = int(D['tile']), float(D['x0']), float(D['y0']), float(D['cell'])
    tiles = {k: np.frombuffer(base64.b64decode(v), '<f4').astype(float).reshape(T + 1, T + 1) for k, v in D['tiles'].items() if isinstance(v, str)}

    def at(x, y):
        fx, fy = (x - x0) / cell, (y - y0) / cell
        ix, iy = np.floor(fx / T).astype(int), np.floor(fy / T).astype(int)
        c, r = fx - ix * T, fy - iy * T
        c0, r0 = np.minimum(np.floor(c), T - 1).astype(int), np.minimum(np.floor(r), T - 1).astype(int)
        tx, ty = c - c0, r - r0
        z = np.full(x.shape, np.nan)
        ok = (fx >= 0) & (fy >= 0)
        for key, a in tiles.items():
            kx, ky = map(int, key.split('_'))
            m = ok & (ix == kx) & (iy == ky)
            if m.any():
                cc, rr, ux, uy = c0[m], r0[m], tx[m], ty[m]
                z[m] = (a[rr, cc] * (1 - ux) + a[rr, cc + 1] * ux) * (1 - uy) + (a[rr + 1, cc] * (1 - ux) + a[rr + 1, cc + 1] * ux) * uy
        return z
    return at


def py_grade(site, org, ext, ground, c=0.25):
    """따로 셈 (JS 와 다른 격자 0.25 m · 같은 약속): 안 = 계획고 · 밖 = 땅을 [계획고 − 내림, 계획고 + 오름] 안으로 · 이어진 것만 · 격자 끝 그대로."""
    X0, Y0, X1, Y1, Rm = ext
    xs, ys = X0 + (np.arange(int((X1 - X0) / c)) + 0.5) * c, Y0 + (np.arange(int((Y1 - Y0) / c)) + 0.5) * c
    X, Y = np.meshgrid(xs, ys)
    P = [(p[0] - org[0], p[1] - org[1]) for p in site['pts']]
    ins = np.zeros(X.shape, bool)
    d = np.full(X.shape, np.inf)
    for i in range(len(P)):
        a, b = P[i], P[(i + 1) % len(P)]
        with np.errstate(divide='ignore', invalid='ignore'):
            ins ^= ((a[1] > Y) != (b[1] > Y)) & (X < (b[0] - a[0]) * (Y - a[1]) / ((b[1] - a[1]) or 1e-12) + a[0])
        dx, dy = b[0] - a[0], b[1] - a[1]
        t = np.clip(((X - a[0]) * dx + (Y - a[1]) * dy) / (dx * dx + dy * dy), 0, 1)
        d = np.minimum(d, np.hypot(a[0] + dx * t - X, a[1] + dy * t - Y))
    g = ground(X, Y)
    zp = site['el']
    rc, _ = prof(d, site['cs'], site['bh'], site['bw'])
    rf, _ = prof(d, site['fs'], site['bh'], site['bw'])
    fin = np.isfinite(g)
    gz = np.where(fin, g, zp)
    cut = fin & ~ins & (d < Rm) & (gz > zp + rc + 0.005)
    fil = fin & ~ins & (d < Rm) & (gz < zp - rf - 0.005)
    z = np.where(ins, zp, np.where(cut, zp + rc, np.where(fil, zp - rf, gz)))
    mod = cut | fil
    keep = ins.copy()
    q = collections.deque(zip(*np.nonzero(ins | (mod & (d <= c)))))
    keep[ins | (mod & (d <= c))] = True
    ny, nx = X.shape
    while q:
        j, i = q.popleft()
        for jj, ii in ((j - 1, i), (j + 1, i), (j, i - 1), (j, i + 1)):
            if 0 <= jj < ny and 0 <= ii < nx and mod[jj, ii] and not keep[jj, ii]:
                keep[jj, ii] = True
                q.append((jj, ii))
    keep[0, :] &= ins[0, :]
    keep[-1, :] &= ins[-1, :]
    keep[:, 0] &= ins[:, 0]
    keep[:, -1] &= ins[:, -1]
    dz = np.where(keep & fin, z - g, 0)
    A = c * c
    return {'cut': float(-dz[dz < 0].sum() * A), 'fill': float(dz[dz > 0].sum() * A), 'area': float(keep.sum() * A)}


def close(a, b, rel, ab=0.0):
    return a is not None and b is not None and abs(a - b) <= max(abs(b) * rel, ab)


def poly_mask(shape, pts):
    m = Image.new('L', (shape[1], shape[0]), 0)
    ImageDraw.Draw(m).polygon([tuple(p) for p in pts], fill=1)
    return np.asarray(m).astype(bool)


def bg_frac(img, mask):
    return float((np.abs(img - BG).sum(2)[mask] < 8).mean()) if mask.any() else 1.0


def color_at(img, s, r=3):
    x, y = int(round(s[0])), int(round(s[1]))
    return img[y - r:y + r + 1, x - r:x + r + 1].reshape(-1, 3).mean(0)


# ---------------------------------------------------------------- app.py _grade_read · save_grade
def write_poly_shp(path, recs, stype=5):
    bodies, allp = [], []
    for parts in recs:
        pts = [p for r in parts for p in r]
        allp += pts
        xs, ys = [p[0] for p in pts], [p[1] for p in pts]
        idx, k = [], 0
        for r in parts:
            idx.append(k)
            k += len(r)
        b = struct.pack('<i4d2i', stype, min(xs), min(ys), max(xs), max(ys), len(parts), len(pts)) + struct.pack('<%di' % len(parts), *idx)
        bodies.append(b + b''.join(struct.pack('<2d', *p) for p in pts))
    content = b''.join(struct.pack('>2i', i + 1, len(b) // 2) + b for i, b in enumerate(bodies))
    xs, ys = [p[0] for p in allp], [p[1] for p in allp]
    head = struct.pack('>7i', 9994, 0, 0, 0, 0, 0, (100 + len(content)) // 2) + struct.pack('<2i4d4d', 1000, stype, min(xs), min(ys), max(xs), max(ys), 0, 0, 0, 0)
    open(path, 'wb').write(head + content)


def write_dbf(path, fields, rows):
    hlen, rlen = 32 + 32 * len(fields) + 1, 1 + sum(f[2] for f in fields)
    h = struct.pack('<4BIHH20x', 3, 126, 11, 27, len(rows), hlen, rlen)
    for name, t, ln, dec in fields:
        h += name.encode('ascii').ljust(11, b'\x00') + t.encode() + b'\x00' * 4 + bytes([ln, dec]) + b'\x00' * 14
    h += b'\x0d'
    body = b''
    for r in rows:
        body += b' ' + b''.join((str(v).encode('cp949').ljust(ln)[:ln] if t == 'C' else str(v).encode().rjust(ln)[:ln]) for (n_, t, ln, d_), v in zip(fields, r))
    open(os.path.splitext(path)[0] + '.dbf', 'wb').write(h + body + b'\x1a')


def check_app(out, SD):
    print('== p app.py (다각형 SHP · 저장)')
    from roadview.app import Api
    M = read_js(os.path.join(out, 'data', 'mapping.js'))
    E0, N0 = M['origin'][0] - 10, M['origin'][1] - 10
    tmp = tempfile.mkdtemp(prefix='grade_', dir=SD)
    os.makedirs(os.path.join(tmp, 'proj', 'data'))
    shutil.copy2(os.path.join(out, 'data', 'mapping.js'), os.path.join(tmp, 'proj', 'data', 'mapping.js'))
    api = Api()
    api._project = os.path.join(tmp, 'proj')
    sq = [(E0, N0), (E0, N0 + 20), (E0 + 20, N0 + 20), (E0 + 20, N0), (E0, N0)]                  # 시계 방향 = 바깥
    hole = [(E0 + 5, N0 + 5), (E0 + 15, N0 + 5), (E0 + 15, N0 + 15), (E0 + 5, N0 + 15), (E0 + 5, N0 + 5)]   # 반시계 = 구멍
    tri = [(E0 + 30, N0), (E0 + 35, N0 + 10), (E0 + 40, N0), (E0 + 30, N0)]
    far = [(E0 + 5000, N0), (E0 + 5000, N0 + 10), (E0 + 5010, N0), (E0 + 5000, N0)]
    shp = os.path.join(tmp, '부지.shp')
    write_poly_shp(shp, [[sq, hole], [tri], [far]])
    write_dbf(shp, [('NAME', 'C', 20, 0), ('EL', 'N', 10, 2)], [['A동', '36.50'], ['B동', ''], ['먼곳', '1.00']])
    r = api._grade_read(shp)
    ps = r.get('polys') or []
    report('p 다각형 둘 (구멍 뺌 · 범위 밖 뺌)', r.get('ok') and len(ps) == 2 and len(ps[0]['pts']) == 4 and len(ps[1]['pts']) == 3 and '범위 밖 1곳' in r.get('note', ''), str(r.get('note')))
    report('p 이름 · 계획고 항목 (빈 칸 = 없음)', ps and ps[0].get('name') == 'A동' and abs(ps[0].get('el', 0) - 36.5) < 1e-9 and ps[1].get('name') == 'B동' and 'el' not in ps[1],
           str([(p.get('name'), p.get('el')) for p in ps]))
    report('p 점 = 도면 좌표 그대로 (.prj 없음)', ps and abs(ps[0]['pts'][0][0] - E0) < 1e-6 and abs(ps[0]['pts'][0][1] - N0) < 1e-6 and '.prj 없음' in r['note'])
    from pyproj import CRS, Transformer
    tr = Transformer.from_crs('EPSG:5186', 'EPSG:5179', always_xy=True)
    shp2 = os.path.join(tmp, '부지_utmk.shp')
    write_poly_shp(shp2, [[[tr.transform(*p) for p in sq]]])
    write_dbf(shp2, [('NAME', 'C', 20, 0)], [['C동']])
    open(os.path.splitext(shp2)[0] + '.prj', 'w').write(CRS.from_epsg(5179).to_wkt('WKT1_ESRI'))
    r2 = api._grade_read(shp2)
    p2 = (r2.get('polys') or [{}])[0].get('pts') or []
    report('p .prj 다름 (UTM-K) → 프로젝트 좌표계', r2.get('ok') and len(p2) == 4 and max(abs(a[0] - b[0]) + abs(a[1] - b[1]) for a, b in zip(p2, sq)) < 0.01, str(r2.get('note')))
    shp3 = os.path.join(tmp, '선.shp')
    write_poly_shp(shp3, [[sq]], stype=3)
    r3 = api._grade_read(shp3)
    report('p 선 SHP → 거절 (다각형 SHP 아님)', r3.get('ok') is False and '다각형' in r3.get('msg', ''), str(r3))
    s1 = api.save_grade('window.RV_GRADE = {"version": 1, "sites": []};')
    s2 = api.save_grade('alert(1)')
    gp = os.path.join(api._project, 'data', 'grade.js')
    report('p save_grade → data/grade.js · 이상한 글 거절', s1.get('ok') and s2.get('ok') is False and open(gp, encoding='utf-8').read().startswith('window.RV_GRADE'))
    shutil.rmtree(tmp, ignore_errors=True)


def wait_save(ed, n, timeout=6):
    return ed.wait_js('__g.saves().length >= %d' % n, timeout)


def page(ed, url):
    ok = setup_page(ed, url)
    ed.js(HELP_JS)
    return ok


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--shots', required=True)
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    out, SD = os.path.abspath(a.out), os.path.abspath(a.shots)
    os.makedirs(SD, exist_ok=True)
    check_app(out, SD)
    prep(out)
    M = read_js(os.path.join(out, 'data', 'mapping.js'))
    org = M['origin']
    W = lambda x, y: [round(org[0] + x, 3), round(org[1] + y, 3)]     # noqa: E731 · 세계 → 도면
    srv, base = serve(out)
    ed = Edge(os.path.join(SD, '_prof'), WIN, gl=False).__enter__()
    inp = ce.Inp(ed)
    try:
        print('== a 평평한 땅 손셈 (data/dem_flat.js · 표고 %.1f)' % ZF)
        report('열림', page(ed, base + 'index_grade_flat_host.html'))
        ed.js('localStorage.clear()')
        page(ed, base + 'index_grade_flat_host.html')
        report('a 셰이더 (정지 면) 만들어짐', J(ed, '__map3d.gd.err()') == '', J(ed, '__map3d.gd.err()'))
        sqA = [W(-10, -10), W(10, -10), W(10, 10), W(-10, 10)]
        sqB = [W(30, -10), W(50, -10), W(50, 10), W(30, 10)]
        ed.js('__map3d.gd.add(%s)' % json.dumps({'pts': sqA, 'el': ZF + 2, 'cs': 1.0, 'fs': 1.5, 'bh': 5, 'bw': 1}))
        ed.js('__map3d.gd.add(%s)' % json.dumps({'pts': sqB, 'el': ZF - 8, 'cs': 1.0, 'fs': 1.5, 'bh': 5, 'bw': 1}))
        ed.frames(3)
        rA, rB = J(ed, '__map3d.gd.res(0)'), J(ed, '__map3d.gd.res(1)')
        fA = ring_vol(80, 400, 2, 1.5, 5, 1)
        fA_closed = 800 + 80 * 4 * 1.5 / 2 + math.pi * 8 * 1.5 ** 2 / 3
        cB = ring_vol(80, 400, 8, 1.0, 5, 1)
        print('     손셈 성토 %.1f (식 %.1f) · 절토 %.1f m³ · 화면 성토 %.1f · 절토 %.1f · 칸 %.2f m' % (fA, fA_closed, cB, rA['fill'], rB['cut'], rA['c']))
        report('a 성토 2 m (1:1.5) = 손셈 2 % 안 · 절토 0', close(rA['fill'], fA_closed, 0.02) and rA['cut'] < 1e-6 and abs(rA['pad'] - 400) < 1e-6, '%.1f / %.1f' % (rA['fill'], fA_closed))
        report('a 절토 8 m (1:1 · 소단 5 m 마다 1 m) = 적분 2 % 안 · 성토 0', close(rB['cut'], cB, 0.02) and rB['fill'] < 1e-6, '%.1f / %.1f' % (rB['cut'], cB))
        L = 2 * 1.5
        aA = (20 + 2 * L) ** 2 - (4 - math.pi) * L * L
        report('a 정지 면적 (평탄면 + 사면 · 둥근 모서리) 3 % 안', close(rA['area'], aA, 0.03), '%.1f / %.1f' % (rA['area'], aA))
        pts = {'안': (40, 0.25, 1, ZF - 8), '절토 d 3.25': (40, 13.25, 2, ZF - 8 + 3.25), '소단 d 5.75': (40, 15.75, 4, ZF - 3), '그대로 d 9.75': (40, 19.75, 0, ZF)}
        okc = True
        for nm, (x, y, k, z) in pts.items():
            q = J(ed, '__map3d.gd.at(1, %f, %f)' % (x, y))
            good = q and q['cls'] == k and abs(q['z'] - z) < 0.01
            okc &= bool(good)
            if not good:
                print('     ', nm, q, '기대', k, z)
        report('a 마디 갈래 · 높이 (안 · 절토 · 소단 · 그대로)', okc)
        report('a 그물 · 높이 질감', J(ed, '__map3d.gd.tris()') > 1000 and J(ed, '__map3d.gd.tex()'), str(J(ed, '__map3d.gd.tris()')))
        ed.js('__g.look(20, 0, 110, 89.5, 0)')
        imf = shot(ed, SD, '09_평평_위에서.png')
        cs = {nm: color_at(imf, J(ed, '__g.scr(%f, %f, %f)' % (x, y, z)), 2) for nm, (x, y, z) in
              {'평탄면': (35.25, -4.75, ZF - 8), '절토': (40, -12.75, ZF - 8 + 2.75), '소단': (40, -15.75, ZF - 3), '성토': (0.25, 11.25, ZF + 2 - 1.25 / 1.5)}.items()}   # 남쪽 = 해 받는 사면
        print('     색 (RGB):', {k: [int(x) for x in v] for k, v in cs.items()})
        c1, c2, c3, c4 = cs['평탄면'], cs['절토'], cs['소단'], cs['성토']
        report('g 평탄면 = 밝은 회색 (쇄석)', abs(c1[0] - c1[1]) < 22 and abs(c1[1] - c1[2]) < 30 and c1.mean() > 90)
        report('g 절토 사면 = 흙색 (빨강 > 초록 > 파랑)', c2[0] > c2[1] + 15 and c2[1] > c2[2] + 8)
        report('g 소단 = 절토보다 어두운 회갈색 띠', c3.mean() < c2.mean() and abs(c3[0] - c3[2]) < abs(c2[0] - c2[2]))
        report('g 성토 사면 = 풀색 (초록 > 빨강 · 파랑)', c4[1] > c4[0] + 5 and c4[1] > c4[2] + 15)
        ed.js('__g.look(40, 0, 75, 38, 20)')
        img_f = shot(ed, SD, '04_평평_절토_소단.png')
        ring = [J(ed, '__g.scr(%f, %f, %f)' % (x - org[0], y - org[1], ZF + 1)) for x, y in [W(19, -21), W(61, -21), W(61, 21), W(19, 21)]]
        mkf = poly_mask(img_f.shape, ring) if all(ring) else np.zeros(img_f.shape[:2], bool)
        report('g 맨땅이 모델보다 낮아도 절토 둘레 틈 (배경색) 없음', mkf.any() and bg_frac(img_f, mkf) < 0.002, '%.2f %%' % (100 * bg_frac(img_f, mkf)))
        errs(ed, '평평한 땅')

        print('== d 그리기 (실제 땅 dem.js)')
        report('열림', page(ed, base + 'index_grade_host.html'))
        cat_open(ed, inp, 'hd_grade')
        report('d 카테고리: 부지 없음 · 그리기 · SHP 보임 · 보이기 켜짐', J(ed, "!document.getElementById('gdNone').hidden && !document.getElementById('gdDraw').hidden && !document.getElementById('gdShp').hidden && document.getElementById('gdShow').classList.contains('on')"))
        ed.js('__g.look(0, 0, 95, 89.5, 0)')
        ed.frames(3)
        img0 = shot(ed, SD, '00_정지_전.png')
        ui_click(ed, inp, 'gdDraw')
        report('d [다각형 그리기] → 그리는 중 · 안내 줄', J(ed, "__map3d.MS.kind === 'grade' && document.getElementById('gdDraw').classList.contains('on') && document.getElementById('msChip').classList.contains('show')"))
        corners = [(-15, -10), (15, -10), (15, 10), (-15, 10)]
        want = []
        for x, y in corners:
            s = J(ed, '__g.scr(%f, %f, __g.ground(%f, %f))' % (x, y, x, y))
            h = J(ed, '__map3d.pickAt(%f, %f)' % (s[0], s[1]))
            want.append([h['p'][0] + org[0], h['p'][1] + org[1]])
            inp.click(s[0], s[1])
        inp.key('Enter', 'Enter', 13)
        ed.frames(3)
        st = J(ed, '__map3d.gd.sites()')
        okp = len(st) == 1 and all(abs(p[0] - q[0]) < 0.01 and abs(p[1] - q[1]) < 0.01 for p, q in zip(st[0]['pts'], want))
        report('d Enter → 새 부지 · 점 = 누른 자리 (모델 면)', okp, str(st[0]['pts'] if st else None))
        mean = J(ed, '__map3d.gd.mean(%s)' % json.dumps(st[0]['pts'])) if st else None
        report('d 계획고 = 다각형 안 땅 평균 · 기본 구배 1:1 · 1:1.5 · 소단 5 m · 1 m', st and abs(st[0]['el'] - mean) < 1e-9 and (st[0]['cs'], st[0]['fs'], st[0]['bh'], st[0]['bw']) == (1, 1.5, 5, 1),
               str(st and (st[0]['name'], st[0]['el'])))
        report('d 목록 한 줄 (고름) · 설정 칸 다섯 · 머리줄 1', J(ed, "document.querySelectorAll('#gdList .gdRow.sel').length === 1 && !document.getElementById('gdSet').hidden && document.querySelectorAll('#gdSet input').length === 5 && document.getElementById('gdTag').textContent === '1' && document.getElementById('gdNone').hidden"))
        report('d 그리기 끝 (안내 줄 닫힘)', J(ed, "__map3d.MS.kind === '' && !document.getElementById('gdDraw').classList.contains('on')"))
        errs(ed, '그리기')

        print('== q 수량 (실제 땅 · 따로 셈)')
        site, r0 = st[0], J(ed, '__map3d.gd.res(0)')
        ext = (r0['X0'], r0['Y0'], r0['X0'] + r0['nx'] * r0['c'], r0['Y0'] + r0['ny'] * r0['c'], r0['R'])
        t0 = time.time()
        py = py_grade(site, org, ext, dem_sampler(out))
        print('     화면 절토 %.1f · 성토 %.1f · 면적 %.1f / 따로 %.1f · %.1f · %.1f (%.1f 초) · 칸 %.2f m · 격자 %d×%d · 여유 %.0f m' % (
            r0['cut'], r0['fill'], r0['area'], py['cut'], py['fill'], py['area'], time.time() - t0, r0['c'], r0['nx'], r0['ny'], r0['R']))
        report('q 절토 · 성토 · 정지 면적 = 따로 셈 3 % 안', close(r0['cut'], py['cut'], 0.03, 3) and close(r0['fill'], py['fill'], 0.03, 3) and close(r0['area'], py['area'], 0.03, 5))
        report('q 절토 · 성토 둘 다 있음 (계획고 = 평균)', r0['cut'] > 1 and r0['fill'] > 1)
        el0 = site['el']
        ed.js("__g.setIn('el', %f)" % (el0 + 1))
        ed.frames(2)
        r1 = J(ed, '__map3d.gd.res(0)')
        report('q 계획고 +1 m (칸에 넣기) → 성토 ↑ · 절토 ↓', abs(J(ed, '__map3d.gd.sites()[0].el') - (el0 + 1)) < 1e-9 and r1['fill'] > r0['fill'] and r1['cut'] < r0['cut'],
               '%.0f → %.0f · %.0f → %.0f' % (r0['fill'], r1['fill'], r0['cut'], r1['cut']))
        ed.js("__g.setIn('el', %f)" % el0)
        ed.js("__g.setIn('fs', 3)")
        r2 = J(ed, '__map3d.gd.res(0)')
        report('q 성토 구배 1:3 → 성토 ↑ (사면 넓어짐)', r2['fill'] > r0['fill'] and r2['area'] > r0['area'], '%.0f → %.0f' % (r0['fill'], r2['fill']))
        ed.js("__g.setIn('fs', 1.5)")
        ed.frames(2)
        ui_click(ed, inp, 'qkGrade')
        ed.frames(3)
        rows = J(ed, '__g.rows()')
        rr = J(ed, '__map3d.gd.res(0)')
        okq = rows == GK and all(close(J(ed, "__g.q('%s')" % k), v, 1e-9, 1e-9) and close(J(ed, "__g.q('%s', 1)" % k), v, 1e-9, 1e-9)
                                 for k, v in (('gd_cut', rr['cut']), ('gd_fill', rr['fill']), ('gd_pad', rr['pad']), ('gd_area', rr['area'])))
        report('q 수량 «정지» = 네 줄 · 고른 부지 · 전체 = 셈', okq and J(ed, "document.getElementById('qTab').children[1].textContent") == '고른 부지' and J(ed, "document.getElementById('qLine').textContent") == site['name'], str(rows))
        img_q = shot(ed, SD, '03_정지_수량.png')
        ui_click(ed, inp, 'tgQty')
        rows_all = J(ed, '__g.rows()')
        report('q 전체 갈래에도 정지 줄 · 머리 «이 관» (이 관 칸 = -)', all(k in rows_all for k in GK) and J(ed, "document.getElementById('qTab').children[1].textContent") == '이 관' and
               J(ed, "document.getElementById('q_gd_cut').textContent") == '-' and close(J(ed, "__g.q('gd_cut', 1)"), rr['cut'], 1e-9, 1e-9))
        ui_click(ed, inp, 'tgQty')
        errs(ed, '수량')

        print('== g 그림')
        ed.js('__g.look(0, 0, 95, 89.5, 0)')
        ed.frames(4)
        img1 = shot(ed, SD, '01_정지_위에서.png')
        sp = [J(ed, '__g.scr(%f, %f, %f)' % (p[0] - org[0], p[1] - org[1], site['el'])) for p in site['pts']]
        mk = poly_mask(img1.shape, sp)
        report('g 위에서: 부지 안 배경색 화소 없음', bg_frac(img1, mk) < 0.003, '%.2f %%' % (100 * bg_frac(img1, mk)))
        chg = (np.abs(img1 - img0).sum(2)[mk] > 30).mean()
        report('g 평탄면 = 원래 모델과 다름 (질감)', chg > 0.5, '%.0f %%' % (100 * chg))
        cols = {}
        for k, nm in ((1, '평탄면'), (2, '절토'), (3, '성토')):                 # 실제 땅: 사면 띠가 좁으면 (둘레 1.5 m 같은 갈래 없음) 안 봄
            f = J(ed, '__g.find(0, %d)' % k)
            s = J(ed, '__g.scr(%f, %f, %f)' % tuple(f)) if f else None
            cols[nm] = color_at(img1, s) if s else None
        print('     실제 땅 색 (RGB):', {k: (None if v is None else [int(x) for x in v]) for k, v in cols.items()})
        c1, c2, c3 = cols['평탄면'], cols['절토'], cols['성토']
        report('g 실제 땅 평탄면 = 밝은 회색 · 찾은 사면 색 맞음', c1 is not None and abs(c1[0] - c1[1]) < 22 and c1.mean() > 90 and
               (c2 is None or (c2[0] > c2[1] + 8 and c2[1] > c2[2])) and (c3 is None or (c3[1] > c3[0] + 5 and c3[1] > c3[2] + 15)))
        ed.js('__g.look(0, -6, 70, 32, 25)')
        img2 = shot(ed, SD, '02_정지_비스듬히.png')
        sp2 = [J(ed, '__g.scr(%f, %f, %f)' % (p[0] - org[0], p[1] - org[1], site['el'])) for p in site['pts']]
        mk2 = poly_mask(img2.shape, sp2) if all(sp2) else np.zeros(img2.shape[:2], bool)
        report('g 비스듬히: 부지 안 배경색 화소 없음', mk2.any() and bg_frac(img2, mk2) < 0.003, '%.2f %%' % (100 * bg_frac(img2, mk2)))
        ed.js('__g.look(0, 0, 95, 89.5, 0)')
        ui_click(ed, inp, 'gdShow')
        ed.frames(4)
        img3 = shot(ed, SD, '06_정지_숨김.png')
        same = (np.abs(img3 - img0).sum(2)[mk] <= 30).mean()
        report('g [보이기] 끔 → 원래 모델 (정지 전 그림과 같음)', not J(ed, '__map3d.gd.show()') and J(ed, '__map3d.gd.tris()') == 0 and same > 0.97, '%.1f %%' % (100 * same))
        ui_click(ed, inp, 'gdShow')
        ed.frames(3)
        ed.js('__map3d.dtSet(true)')
        ed.frames(4)
        img4 = shot(ed, SD, '07_정지_맨땅보기.png')
        report('g 맨땅 보기: 부지 안 배경색 화소 없음 · 정지 그물 그대로', J(ed, '__map3d.DT.on') and bg_frac(img4, mk) < 0.003 and J(ed, '__map3d.gd.tris()') > 1000, '%.2f %%' % (100 * bg_frac(img4, mk)))
        ed.js('__map3d.dtSet(false)')
        ed.frames(2)
        errs(ed, '그림')

        print('== s 저장 · SHP · 고르기 · 지우기')
        report('s 저장 (save_grade · 모아서)', wait_save(ed, 1))
        sv = J(ed, '__g.saves()')
        D = read_js_text(sv[-1]) if sv else {}
        report('s 저장 글 = window.RV_GRADE · 부지 하나 · 계획고 · 점', sv and sv[-1].startswith('window.RV_GRADE') and len(D.get('sites', [])) == 1 and abs(D['sites'][0]['el'] - el0) < 1e-9 and D['sites'][0]['pts'] == site['pts'])
        n0 = len(calls(ed, 'grade_shp'))
        ed.js("__host.gshp.push({ ok: false, msg: '다각형 SHP 아님' })")
        ui_click(ed, inp, 'gdShp')
        ok = ed.wait_js("document.getElementById('toast').textContent.indexOf('다각형 SHP 아님') >= 0", 5)
        report('s SHP 못 읽음 → 알림 · 부지 그대로', ok and len(J(ed, '__map3d.gd.sites()')) == 1)
        g30 = J(ed, '__g.ground(40, 0)')
        shp_site = {'name': '시험 부지', 'el': round(g30 + 0.5, 2), 'pts': [W(30, -10), W(50, -10), W(50, 10), W(30, 10)]}
        ed.js('__host.gshp.push(%s)' % json.dumps({'ok': True, 'polys': [shp_site], 'note': '좌표계 그대로'}))
        ui_click(ed, inp, 'gdShp')
        ok = ed.wait_js('__map3d.gd.sites().length === 2', 5)
        s2 = J(ed, '__map3d.gd.sites()')
        report('s SHP 불러오기 → 새 부지 (이름 · 계획고 · 고름) · 알림', ok and s2[1]['name'] == '시험 부지' and abs(s2[1]['el'] - shp_site['el']) < 1e-9 and J(ed, '__map3d.gd.sel()') == 1 and
               len(calls(ed, 'grade_shp')) == n0 + 2 and '부지 1곳' in J(ed, "document.getElementById('toast').textContent"), J(ed, "document.getElementById('toast').textContent"))
        ed.js('__g.look(0, 0, 95, 89.5, 0)')
        ed.frames(3)
        c0 = J(ed, '__g.scr(0, 0, %f)' % site['el'])
        inp.click(c0[0], c0[1])
        report('s 3D 에서 부지 누르기 → 그 부지 고름', J(ed, '__map3d.gd.sel()') == 0 and J(ed, "document.querySelector('#gdList .gdRow.sel').dataset.i") == '0')
        ed.js('__map3d.gd.flush()')
        ok = ed.wait_js('__g.saves().length >= 2 && JSON.parse(__g.saves().slice(-1)[0].slice(__g.saves().slice(-1)[0].indexOf("{"), __g.saves().slice(-1)[0].lastIndexOf("}") + 1)).sites.length === 2', 6)
        last = J(ed, '__g.saves()')[-1]
        open(os.path.join(out, 'data', 'grade_saved.js'), 'w', encoding='utf-8').write(last)
        report('s 두 부지 저장', ok)
        res_before = [J(ed, '__map3d.gd.res(%d)' % i) for i in range(2)]
        errs(ed, '저장')
        page(ed, base + 'index_grade_saved_host.html')
        s3 = J(ed, '__map3d.gd.sites()')
        res_after = [J(ed, '__map3d.gd.res(%d)' % i) for i in range(len(s3))]
        report('s 다시 열면 그대로 (부지 둘 · 값 · 수량)', len(s3) == 2 and [x['name'] for x in s3] == [site['name'], '시험 부지'] and
               all(close(a_[k], b_[k], 1e-9, 1e-6) for a_, b_ in zip(res_before, res_after) for k in ('cut', 'fill', 'area')))
        cat_open(ed, inp, 'hd_grade')
        ed.js("document.querySelectorAll('#gdList .gdRow')[1].querySelector('.shDel').scrollIntoView({ block: 'nearest' })")
        ed.frames(2)
        b = J(ed, "(() => { const r = document.querySelectorAll('#gdList .gdRow')[1].querySelector('.shDel').getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; })()")
        inp.click(b[0], b[1])
        ok = ed.wait_js("document.querySelector('#toast.show button') !== null", 3)
        tb = J(ed, "(() => { const r = document.querySelector('#toast button').getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; })()")
        inp.click(tb[0], tb[1])
        ed.frames(2)
        ok = ok and wait_save(ed, 1)
        D2 = read_js_text(J(ed, '__g.saves()')[-1]) if ok else {}
        report('s × → 묻기 → 지우기 → 하나 남음 · 저장', ok and len(J(ed, '__map3d.gd.sites()')) == 1 and len(D2.get('sites', [])) == 1 and J(ed, "document.querySelectorAll('#gdList .gdRow').length") == 1)
        shot(ed, SD, '08_지운뒤.png')
        errs(ed, '다시 열기 · 지우기')

        print('== b 브라우저 (웹 공유 · 보기만)')
        page(ed, base + 'index_grade_saved.html')
        cat_open(ed, inp, 'hd_grade')
        ed.frames(2)
        report('b 부지 둘 · 그리기 · SHP · × 없음 · 칸 잠금', len(J(ed, '__map3d.gd.sites()')) == 2 and J(ed, "document.getElementById('gdDraw').hidden && document.getElementById('gdShp').hidden && "
               "document.querySelectorAll('#gdList .shDel').length === 0 && [...document.querySelectorAll('#gdSet input')].every((i) => i.disabled) && !document.getElementById('gdMean')"))
        el_b = J(ed, '__map3d.gd.sites()[0].el')
        ed.js("__g.setIn('el', %f)" % (el_b + 3))
        report('b 칸 고쳐도 안 바뀜', abs(J(ed, '__map3d.gd.sites()[0].el') - el_b) < 1e-9)
        report('b 그물 · 높이 질감', J(ed, '__map3d.gd.tris()') > 1000 and J(ed, '__map3d.gd.tex()'))
        ui_click(ed, inp, 'gdQty')
        ed.frames(2)
        report('b [정지 수량] → 수량 «정지» 갈래', J(ed, "__map3d.QT.on && __map3d.QT.view === 'grade'") and J(ed, '__g.rows()') == GK)
        ed.js('__g.look(20, 0, 120, 45, 30)')
        shot(ed, SD, '05_브라우저.png')
        errs(ed, '브라우저')
    finally:
        try:
            ed.__exit__(None, None, None)
        finally:
            srv.shutdown()
    print()
    if R['errs']:
        print('JS 오류:')
        for w, e in R['errs'][:10]:
            print('  ', w, e)
    print('모두 통과' if R['ok'] else '실패: ' + ', '.join(R['fails']))
    print('그림:', SD)
    return 0 if R['ok'] else 1


def read_js_text(t):
    return json.loads(t[t.index('{'):t.rstrip().rstrip(';').rindex('}') + 1])


if __name__ == '__main__':
    sys.exit(main())
