"""make_synth.py 로 만든 폴더를 화면 없는 Edge(cdp.py, 실제 시간)로 찍고 관로 · 이웃 사진 표시를 잰다.

    python -I check_view.py --out <make_synth 폴더> [--shots <사진 저장 폴더>] [--legacy]

재는 것 (화면 픽셀, 사진 가운데 좌표 = 번호 + 0.5):
  1) 노면: 띠(사진, 관경 폭) 양쪽 가장자리 ↔ 관로 띠(겹쳐 그림) 양쪽 가장자리.  관로 켜고 / 끄고 두 장의 차이로 띠를 찾는다.
     + 사진 띠 가장자리 ↔ 이 스크립트가 따로 계산한 투영 (보기 화면 수식과 독립 — 재는 자가 맞는지)
  2) 깊이: WebGL 관(겹쳐 그림 숨김, 관로 켜고 / 끄고 차이)의 바깥 가장자리 · 가운데 ↔ 원통을 따로 투영한 윤곽선 · 중심선
  3) 고개 0° 화면에서 N / S 글자 무게중심 ↔ 계산 위치
  3b) 노면 최소 폭: 먼 관(관경 0.2 m, 120 m)은 화면에서 2 px 아래로 얇아지지 않는다
  4) 이웃 사진 원판: 크기 · 자리 ↔ 계산 (평소 반지름 0.135 m · 마우스 올리면 0.45 m + 청록 테두리)
  5) 페이지 JS 오류 (잡히지 않은 오류 · console.error)
--legacy: 옛 pipes.js (make_synth --legacy-pipes) — 관경 0.2 m 기본값 · 지표 = 사진 groundZ · 깊이 = project.cover
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
import make_synth as ms  # noqa: E402
from cdp import Edge  # noqa: E402

WIN = (1600, 900)
# 새 쪽이 뜨고, 사진 하나가 적용되고, 불러오는 중 표시가 꺼짐
READY = (Edge.NEW + " && document.getElementById('shotNo') && document.getElementById('shotNo').textContent !== '-'"
         " && !document.getElementById('loading').classList.contains('show')")
BG = np.array([11, 13, 18])
G0 = np.array([120.0, 120.0, 120.0])  # 사진 땅 색

# (이름, 사진, yaw, pitch, fov, 방식, 꼭 재야 하는 관 번호 (STRIPES))
CASES = [
    ('n_letter', '0002', 0, 0, 90, '', ()),
    ('s_letter_seam', '0002', 180, 0, 90, '', ()),
    ('s2_north_down', '0002', 0, -70, 80, '', (0, 1)),
    ('s2_south_clip', '0002', 180, -50, 90, '', (1,)),
    ('s1_ne', '0001', 60, -50, 70, '', (0, 1)),
    ('s3_ne', '0003', 30, -55, 70, '', (0, 1)),
    ('s2_default', '0002', 80, -45, 90, '', (0, 1)),
    ('s4_other_video', '0004', 0, -45, 80, '', (0, 1)),
    ('d2_north', '0002', 0, -60, 70, 'depth', (0,)),
    ('d3_east', '0003', 90, -55, 70, 'depth', (1,)),
    ('d1_ne', '0001', 60, -50, 70, 'depth', (0, 1)),
    ('d4_other', '0004', 0, -45, 80, 'depth', (0, 1)),
    ('far_min_width', '0002', 0, -4, 60, '', ('far',)),
]
LEGACY_CASES = ('s1_ne', 's2_default', 'd2_north', 'd3_east')
BLANK = np.array([30, 34, 44])  # 사진이 아직 없을 때 보기 화면 색


def shoot(ed, out_dir, case, png, page='로드뷰.html'):
    """찍고 (JS 오류 줄들, 다 불러왔는지) 를 돌려준다."""
    name, shot, yaw, pitch, fov, mode = case[:6]
    uri = pathlib.Path(out_dir, page).resolve().as_uri()
    uri += '?shot=%s&yaw=%s&pitch=%s&fov=%s' % (shot, yaw, pitch, fov)
    if mode:
        uri += '&mode=' + mode
    ed.goto(uri)
    ok = ed.wait_js(READY, timeout=60)
    ed.frames(3)
    ed.screenshot(png)
    return ok


def errors(ed):
    return list(ed.exceptions) + [t for k, t in ed.console if k == 'error']


def _run(gap, at):
    """gap 이 False 인 연속 구간 중 at 을 품은 것."""
    i0 = at
    while i0 > 0 and not gap[i0 - 1]:
        i0 -= 1
    i1 = at
    while i1 + 1 < gap.size and not gap[i1 + 1]:
        i1 += 1
    return i0, i1


def _drop_short(gap, n=6):
    """n 보다 짧은 틈(예: 화면을 가로지르는 관로선의 검은 테두리)은 틈이 아니다."""
    g = gap.copy()
    i = 0
    while i < g.size:
        if g[i]:
            j = i
            while j + 1 < g.size and g[j + 1]:
                j += 1
            if j - i + 1 < n and i > 0 and j < g.size - 1:
                g[i:j + 1] = False
            i = j + 1
        else:
            i += 1
    return g


def view_rect(a):
    """360° 보기 = 왼쪽 첫 칸. 칸 사이 틈(10 px)은 거의 모두 바탕색인 열 · 행."""
    on = np.abs(a[..., :3].astype(int) - BG).max(axis=2) > 25
    colgap = _drop_short((~on[100:850]).mean(axis=0) > 0.97)
    x0, x1 = _run(colgap, int(np.argmax(~colgap)))
    rowgap = _drop_short((~on[:, x0 + 40:x1 - 40]).mean(axis=1) > 0.97)
    y0, y1 = _run(rowgap, a.shape[0] // 2)
    return x0, y0, x1, y1


def basis(yaw, pitch):
    y, p = math.radians(yaw), math.radians(pitch)
    f = np.array([math.cos(p) * math.sin(y), math.cos(p) * math.cos(y), math.sin(p)])
    r = np.array([math.cos(y), -math.sin(y), 0.0])
    u = np.array([-math.sin(y) * math.sin(p), -math.cos(y) * math.sin(p), math.cos(p)])
    return f, r, u


def load_calib(out_dir):
    """data/calib.js 가 있으면 영상별 맞춤 값 {영상: {...}}."""
    p = os.path.join(out_dir, 'data', 'calib.js')
    if not os.path.exists(p):
        return {}
    with open(p, encoding='utf-8') as f:
        t = f.read()
    return json.loads(t[t.index('{'):t.rindex('}') + 1])['videos']


def cam_of(shot, cal_all):
    e, n, _, _, vid = ms.SHOTS[int(shot) - 1]
    cal = cal_all.get(vid, {})
    return np.array([e + cal.get('dx', 0.0), n + cal.get('dy', 0.0), ms.GZ + ms.HEIGHT + cal.get('dz', 0.0)]), cal


def projector(case, w, h, cal_all):
    _, shot, yaw, pitch, fov = case[:5]
    cam, cal = cam_of(shot, cal_all)
    R = ms.rot(cal)
    f, r, u = basis(yaw, pitch)
    t = math.tan(math.radians(fov) / 2)
    tanh, tanv = (t, t * h / w) if w >= h else (t * w / h, t)
    kx, ky = (w / 2) / tanh, (h / 2) / tanv

    def proj(pts):  # pts: (N,3) 세계 좌표 → (N,2) 화면, 뒤쪽은 nan
        d = np.atleast_2d(pts) - cam
        zv = d @ f
        bad = zv < 0.05
        zv = np.where(bad, 1.0, zv)
        xs = w / 2 + (d @ r) / zv * kx
        ys = h / 2 - (d @ u) / zv * ky
        xs[bad] = np.nan
        ys[bad] = np.nan
        return np.stack([xs, ys], axis=1)

    def proj_dir(az, el):  # 사진 속 방향(az, el) 이 화면 어디에 오는가: 세계 방향 = R·p
        a, b = math.radians(az), math.radians(el)
        p = np.array([math.cos(b) * math.sin(a), math.cos(b) * math.cos(a), math.sin(b)])
        return proj((R @ p)[None, :] * 1000.0 + cam)[0]

    return proj, proj_dir


# ---------------------------------------------------------------- 재는 도구
def bilinear(img, pts):
    """img (H,W,C) float, pts (N,2) 화면 좌표 (픽셀 가운데 = i+0.5) → (N,C). 밖은 nan."""
    H, W = img.shape[:2]
    x = pts[:, 0] - 0.5
    y = pts[:, 1] - 0.5
    x0 = np.floor(x).astype(int)
    y0 = np.floor(y).astype(int)
    ok = (x0 >= 0) & (y0 >= 0) & (x0 < W - 1) & (y0 < H - 1)
    x0c, y0c = np.clip(x0, 0, W - 2), np.clip(y0, 0, H - 2)
    fx, fy = (x - x0)[:, None], (y - y0)[:, None]
    v = (img[y0c, x0c] * (1 - fx) * (1 - fy) + img[y0c, x0c + 1] * fx * (1 - fy)
         + img[y0c + 1, x0c] * (1 - fx) * fy + img[y0c + 1, x0c + 1] * fx * fy)
    v[~ok] = np.nan
    return v


def cross_at(prof, s, level):
    """s=0 에서 바깥으로 걸어 prof 가 level 아래로 떨어지는 곳 (선형 보간). (음쪽, 양쪽)."""
    i0 = int(np.argmin(np.abs(s)))
    if not prof[i0] >= level:
        return None
    out = []
    for step in (-1, 1):
        i = i0
        while 0 <= i + step < s.size and prof[i + step] >= level:
            i += step
        j = i + step
        if not 0 <= j < s.size or not np.isfinite(prof[j]):
            return None
        a, b = prof[i], prof[j]
        out.append(s[i] + (s[j] - s[i]) * (a - level) / (a - b))
    return out


def line_hit(c, nrm, a, d):
    """c + s·nrm 와 a + λ·d 가 만나는 s."""
    M = np.array([[nrm[0], -d[0]], [nrm[1], -d[1]]])
    if abs(np.linalg.det(M)) < 1e-9:
        return np.nan
    return np.linalg.solve(M, a - c)[0]


def samples(verts, step=0.05):
    """선 위 점들 (x, y, tx, ty, 끝 · 꺾인 곳까지 거리)."""
    out = []
    ends = [np.array(v) for v in verts]
    for (ax, ay), (bx, by) in zip(verts[:-1], verts[1:]):
        L = math.hypot(bx - ax, by - ay)
        tx, ty = (bx - ax) / L, (by - ay) / L
        for t in np.arange(0, L, step):
            x, y = ax + tx * t, ay + ty * t
            dv = min(math.hypot(x - v[0], y - v[1]) for v in ends)
            out.append((x, y, tx, ty, dv))
    return np.array(out)


def dist_poly(x, y, verts):
    return min(float(ms.seg_dist(np.array(x), np.array(y), a, b)) for a, b in zip(verts[:-1], verts[1:]))


def pipe_geom(si, legacy):
    """(꼭짓점, 관경/2, 관 중심 표고, 띠 반폭)"""
    verts, hw, _, layer = ms.STRIPES[si]
    if legacy:
        return verts, 0.1, ms.GZ - ms.COVER - 0.1, hw
    zc = ms.HASZ_Z if layer == 1 else ms.GZ - ms.DEPTH - hw
    return verts, hw, zc, hw


def far_from_others(si, x, y, pad):
    for sj, (verts, hw, _, _) in enumerate(ms.STRIPES):
        if sj != si and dist_poly(x, y, verts) < hw + pad:
            return False
    return True


def measure_band(si, proj, on, off, w, h, legacy):
    """노면: 사진 띠 가장자리 (끔) · 관로 띠 가장자리 (켬−끔) · 계산 가장자리를 선의 화면 법선 방향으로 잰다."""
    verts, r_pipe, _, hw = pipe_geom(si, legacy)
    col = np.array(ms.STRIPES[si][2], float)
    sc = col - G0
    res = []
    S = samples(verts)
    for x, y, tx, ty, dv in S[::4]:
        if dv < 1.0 or not far_from_others(si, x, y, 1.5):
            continue
        P = np.array([x, y, ms.GZ])
        t3 = np.array([tx, ty, 0.0])
        n3 = np.array([-ty, tx, 0.0])
        q = proj(np.array([P, P - t3 * 0.5, P + t3 * 0.5, P + n3 * hw, P + n3 * hw + t3, P - n3 * hw, P - n3 * hw + t3]))
        if not np.isfinite(q).all():
            continue
        c = q[0]
        d = q[2] - q[1]
        dl = np.hypot(*d)
        if dl < 1e-6:
            continue
        d /= dl
        nrm = np.array([-d[1], d[0]])
        pa = line_hit(c, nrm, q[3], q[4] - q[3])
        pb = line_hit(c, nrm, q[5], q[6] - q[5])
        p_lo, p_hi = sorted((pa, pb))
        R = max(abs(p_lo), abs(p_hi)) + 7
        if not (R < 120 and R < c[0] < w - R and R < c[1] < h - R):
            continue
        s = np.arange(-R, R + 0.001, 0.25)
        pts = c[None, :] + s[:, None] * nrm[None, :]
        A = bilinear(on, pts)
        B = bilinear(off, pts)
        if not (np.isfinite(A).all() and np.isfinite(B).all()):
            continue
        if (A.max(axis=1) < 60).any() or (A.min(axis=1) > 200).any() or (B.min(axis=1) > 200).any():
            continue  # 이름표 · 이웃 사진 원판이 걸침
        cov = ((B - G0) @ sc) / (sc @ sc)
        st = cross_at(cov, s, 0.5)
        dd = np.sqrt(((A - B) ** 2).sum(axis=1))
        inner = np.abs(s) <= max(0.0, min(abs(p_lo), abs(p_hi)) * r_pipe / hw - 1.5)  # 관로 띠 안쪽 (옛 형식은 관경 0.2 m)
        level = np.median(dd[inner]) if inner.sum() >= 3 else dd[np.abs(s) <= 0.5].max()
        bd = cross_at(dd, s, 0.5 * level) if level > 40 else None
        if st is None or bd is None:
            continue
        res.append((st[0], st[1], bd[0], bd[1], p_lo, p_hi))
    return np.array(res).reshape(-1, 6)


def measure_tube(si, proj, cam, on, off, w, h, legacy):
    """깊이: WebGL 관 (켬−끔) 의 바깥 가장자리 · 가운데 ↔ 원통 윤곽선 (따로 계산) · 중심선."""
    verts, r, zc, _ = pipe_geom(si, legacy)
    res = []
    S = samples(verts)
    for x, y, tx, ty, dv in S[::4]:
        if dv < 1.5 or not far_from_others(si, x, y, 2.5):
            continue
        P = np.array([x, y, zc])
        t3 = np.array([tx, ty, 0.0])
        wv = P - cam
        wv = wv - (wv @ t3) * t3
        rho = np.linalg.norm(wv)
        wh = wv / rho
        b = np.cross(t3, wh)
        sil = [P + r * (-(r / rho) * wh + sg * math.sqrt(1 - (r / rho) ** 2) * b) for sg in (-1, 1)]
        q = proj(np.array([P, P - t3 * 0.5, P + t3 * 0.5, sil[0], sil[0] + t3, sil[1], sil[1] + t3]))
        if not np.isfinite(q).all():
            continue
        c = q[0]
        d = q[2] - q[1]
        d /= np.hypot(*d)
        nrm = np.array([-d[1], d[0]])
        pa = line_hit(c, nrm, q[3], q[4] - q[3])
        pb = line_hit(c, nrm, q[5], q[6] - q[5])
        p_lo, p_hi = sorted((pa, pb))
        R = max(abs(p_lo), abs(p_hi)) + 7
        if not (R < 120 and R < c[0] < w - R and R < c[1] < h - R):
            continue
        s = np.arange(-R, R + 0.001, 0.25)
        pts = c[None, :] + s[:, None] * nrm[None, :]
        A = bilinear(on, pts)
        B = bilinear(off, pts)
        if not (np.isfinite(A).all() and np.isfinite(B).all()):
            continue
        dd = np.sqrt(((A - B) ** 2).sum(axis=1))
        coarse = cross_at(dd, s, 25.0)
        if coarse is None:
            continue
        edges = []
        for k, sgn in ((0, 1), (1, -1)):  # 안쪽 1.5~3 px 의 값을 기준으로 절반 높이에서
            e = coarse[k]
            sel = (sgn * (s - e) >= 1.5) & (sgn * (s - e) <= 3.0)
            lev = np.median(dd[sel]) if sel.any() else np.nan
            ins = s[(sgn * (s - e) >= 1.5)]
            if not np.isfinite(lev) or not ins.size:
                break
            s0 = ins[0] if sgn == 1 else ins[-1]
            i = int(np.argmin(np.abs(s - s0)))
            st = -sgn
            while 0 <= i + st < s.size and dd[i + st] >= 0.5 * lev:
                i += st
            j = i + st
            if not 0 <= j < s.size:
                break
            edges.append(s[i] + (s[j] - s[i]) * (dd[i] - 0.5 * lev) / (dd[i] - dd[j]))
        if len(edges) != 2:
            continue
        res.append((edges[0], edges[1], p_lo, p_hi))
    return np.array(res).reshape(-1, 4)


def measure_far(proj, on, off, w, h):
    """먼 관 띠 두께 (열마다 Σ차이 / 최대 차이) · 늘리지 않았을 때 계산 두께."""
    v = ms.FAR_LINE['verts']
    hw = ms.FAR_LINE['dia'] / 2
    es = np.linspace(v[0][0], v[1][0], 4001)
    c = proj(np.stack([es, np.full_like(es, v[0][1]), np.full_like(es, ms.GZ)], axis=1))
    lo = proj(np.stack([es, np.full_like(es, v[0][1] - hw), np.full_like(es, ms.GZ)], axis=1))
    hi = proj(np.stack([es, np.full_like(es, v[0][1] + hw), np.full_like(es, ms.GZ)], axis=1))
    ok = np.isfinite(c).all(axis=1)
    c, lo, hi = c[ok], lo[ok], hi[ok]
    dd = np.sqrt(((on - off) ** 2).sum(axis=2))
    th, raw = [], []
    for x in range(20, w - 20, 3):
        k = int(np.argmin(np.abs(c[:, 0] - (x + 0.5))))
        if abs(c[k, 0] - (x + 0.5)) > 1:
            continue
        y = c[k, 1]
        if not 8 < y < h - 8:
            continue
        col = dd[int(y) - 6:int(y) + 7, x]
        if col.max() < 40:
            continue
        th.append(col.sum() / col.max())
        raw.append(abs(hi[k, 1] - lo[k, 1]))
    return np.array(th), np.array(raw)


def stat(v):
    v = np.abs(np.asarray(v))
    return np.median(v), np.percentile(v, 90)


def letter_check(view, case, proj_dir, w):
    letter = 'N' if case[2] == 0 else 'S'
    ex, ey = proj_dir(case[2], 6.0)
    hz = proj_dir(case[2], 0.0)[1]
    ys, xs = np.nonzero((view.max(axis=2) < 70))
    sel = (np.abs(xs - ex) < 150) & (ys > hz - 120) & (ys < hz)
    if sel.sum() < 20:
        print('   글자 %s 못 찾음  실패' % letter)
        return False
    gx, gy = xs[sel].mean() + 0.5, ys[sel].mean() + 0.5  # 픽셀 번호 → 픽셀 가운데 좌표
    ok = abs(gx - ex) <= 2 and abs(gy - ey) <= 4
    print('   글자 %s 무게중심 (%.1f, %.1f) · 계산 (%.1f, %.1f) · 화면 가운데 x %.1f  %s'
          % (letter, gx, gy, ex, ey, w / 2, '통과' if ok else '실패'))
    return ok


def crop_zoom(png, box, out, k=4):
    im = Image.open(png).convert('RGB').crop(box)
    im.resize((im.width * k, im.height * k), Image.NEAREST).save(out)


def marker_case(ed, out_dir, shots_dir, cal):
    """3번 자리에서 2번 땅점(원판)을 화면 가운데로: 크기 · 자리 · 숫자 없음 · 마우스 올림 (진짜 마우스)."""
    ok_all = True
    e3, e2 = ms.SHOTS[2][0], ms.SHOTS[1][0]
    yaw = math.degrees(math.atan2(e2 - e3, 0.0))
    pitch = -math.degrees(math.atan2(ms.HEIGHT, abs(e3 - e2)))
    case = ('marker', '0003', yaw, pitch, 90, '')
    png = os.path.join(shots_dir, 'mk_plain.png')
    loaded = shoot(ed, out_dir, case, png)
    a = np.asarray(Image.open(png).convert('RGB')).astype(int)
    x0, y0, x1, y1 = view_rect(a)
    view = a[y0:y1 + 1, x0:x1 + 1]
    w, h = view.shape[1], view.shape[0]
    proj, _ = projector(case, w, h, cal)
    cal2 = cal.get(ms.SHOTS[1][4], {})
    ge, gn = ms.SHOTS[1][0] + cal2.get('dx', 0.0), ms.SHOTS[1][1] + cal2.get('dy', 0.0)
    th = np.linspace(0, 2 * math.pi, 361)
    R_PLAIN, R_HOVER = 0.135, 0.45          # app.js MARK_R · MARK_R × MARK_HOVER (JACK 1123: 평소 30 %)
    ring = proj(np.stack([ge + R_PLAIN * np.cos(th), gn + R_PLAIN * np.sin(th), np.full_like(th, ms.GZ)], axis=1))
    ring_h = proj(np.stack([ge + R_HOVER * np.cos(th), gn + R_HOVER * np.sin(th), np.full_like(th, ms.GZ)], axis=1))
    bx0, bx1, by0, by1 = ring[:, 0].min(), ring[:, 0].max(), ring[:, 1].min(), ring[:, 1].max()
    cx, cy = (bx0 + bx1) / 2, (by0 + by1) / 2
    win = view[int(by0) - 10:int(by1) + 11, int(bx0) - 10:int(bx1) + 11]
    lit = win.min(axis=2) > 140  # 밝은 몸통
    ys, xs = np.nonzero(lit)
    if not loaded or xs.size < 4:
        print('== marker  원판 못 찾음  실패')
        return False
    mx0, mx1 = xs.min() + int(bx0) - 10, xs.max() + 1 + int(bx0) - 10
    my0, my1 = ys.min() + int(by0) - 10, ys.max() + 1 + int(by0) - 10
    errs = [mx0 - bx0, mx1 - bx1, my0 - by0, my1 - by1]
    ok = max(abs(e) for e in errs) <= 2.0
    ok_all &= ok
    print('== marker  shot=0003 → 2번 땅점  view=%dx%d' % (w, h))
    print('   원판 계산 네모 x %.1f~%.1f y %.1f~%.1f · 밝은 몸통 x %d~%d y %d~%d · 차이 최대 %.2f px  %s'
          % (bx0, bx1, by0, by1, mx0, mx1, my0, my1, max(abs(e) for e in errs), '통과' if ok else '실패'))
    cyan = lambda v: ((v[..., 0] < 110) & (v[..., 1] > 170) & (v[..., 2] > 200)).sum()  # noqa: E731
    c_plain = cyan(win)
    # 진짜 마우스를 원판 가운데로
    ed.mouse(x0 + cx, y0 + cy)
    ed.frames(3)
    point = ed.js("document.getElementById('ov').classList.contains('point')")
    png2 = os.path.join(shots_dir, 'mk_hover.png')
    ed.screenshot(png2)
    b = np.asarray(Image.open(png2).convert('RGB')).astype(int)[y0:y1 + 1, x0:x1 + 1]
    hx0, hx1, hy0, hy1 = ring_h[:, 0].min(), ring_h[:, 0].max(), ring_h[:, 1].min(), ring_h[:, 1].max()
    win2 = b[int(hy0) - 14:int(hy1) + 15, int(hx0) - 14:int(hx1) + 15]
    ys2, xs2 = np.nonzero(win2.min(axis=2) > 140)
    grow = (xs2.max() - xs2.min() + 1) / max(1, (mx1 - mx0)) if xs2.size else 0
    want = (hx1 - hx0) / max(1e-6, (bx1 - bx0))           # 계산상 커지는 배수 (≈ 3.3)
    c_hover = cyan(win2)
    ok = bool(point) and c_plain == 0 and c_hover >= 20 and 0.75 * want <= grow <= 1.25 * want
    ok_all &= ok
    print('   마우스 올림: 손가락 모양 %s · 청록 테두리 픽셀 %d → %d · 폭 %.2f 배 (계산 %.2f 배)  %s'
          % (point, c_plain, c_hover, grow, want, '통과' if ok else '실패'))
    box = (int(x0 + hx0) - 30, int(y0 + hy0) - 25, int(x0 + hx1) + 30, int(y0 + hy1) + 25)
    crop_zoom(png, box, os.path.join(shots_dir, 'mk_plain_x4.png'))
    crop_zoom(png2, box, os.path.join(shots_dir, 'mk_hover_x4.png'))
    er = errors(ed)
    print('   JS 오류 %d 줄' % len(er))
    for ln in er:
        print('   오류:', str(ln).strip()[:200])
    ok_all &= not er
    print('   사진:', png, '·', png2, '· 확대:', os.path.join(shots_dir, 'mk_plain_x4.png'), os.path.join(shots_dir, 'mk_hover_x4.png'))
    return ok_all


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--shots', default=None)
    ap.add_argument('--legacy', action='store_true')
    args = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    out_dir = os.path.abspath(args.out)
    shots_dir = os.path.abspath(args.shots or os.path.join(out_dir, '_shots'))
    os.makedirs(shots_dir, exist_ok=True)
    cal = load_calib(out_dir)
    print('맞춤(calib.js):', cal or '없음', '· pipes.js:', '있음' if os.path.exists(os.path.join(out_dir, 'data', 'pipes.js')) else '없음',
          '· 옛 형식' if args.legacy else '')
    ed = Edge(os.path.join(shots_dir, '_prof'), WIN).__enter__()  # 한 번 켜서 모든 경우를 찍는다
    try:
        all_ok = run_cases(ed, out_dir, shots_dir, cal, args.legacy)
        if not args.legacy:
            all_ok &= marker_case(ed, out_dir, shots_dir, cal)
    finally:
        ed.__exit__(None, None, None)
    print('전체:', '통과' if all_ok else '실패')
    return 0 if all_ok else 1


def run_cases(ed, out_dir, shots_dir, cal, legacy):
    all_ok = True
    has_pipes = os.path.exists(os.path.join(out_dir, 'data', 'pipes.js'))
    for case in CASES:
        name = case[0]
        if legacy and name not in LEGACY_CASES:
            continue
        depth = case[5] == 'depth'
        png = os.path.join(shots_dir, name + '.png')
        loaded = shoot(ed, out_dir, case, png)
        a = np.asarray(Image.open(png).convert('RGB'))
        x0, y0, x1, y1 = view_rect(a)
        view = a[y0:y1 + 1, x0:x1 + 1]
        blank = (np.abs(view.astype(int) - BLANK).max(axis=2) <= 3).mean()
        w, h = view.shape[1], view.shape[0]
        proj, proj_dir = projector(case, w, h, cal)
        print('== %s  shot=%s yaw=%s pitch=%s fov=%s %s  view=%dx%d at (%d,%d)' % (name, case[1], case[2], case[3], case[4], case[5], w, h, x0, y0))
        if not loaded or blank >= 0.5:
            print('   사진이 안 보임 (불러옴=%s, 빈 화면 %.0f%%)  실패' % (loaded, blank * 100))
            all_ok = False
        shots = [png]
        if case[6] and has_pipes:
            crop = lambda p: np.asarray(Image.open(p).convert('RGB')).astype(float)[y0:y1 + 1, x0:x1 + 1]  # noqa: E731
            if depth:  # 겹쳐 그림(점선 · 이름표 · 원판)을 숨기고 WebGL 만
                ed.js("document.getElementById('ov').style.visibility = 'hidden'")
                ed.frames(3)
                p_on = os.path.join(shots_dir, name + '_gl.png')
                ed.screenshot(p_on)
            else:
                p_on = png
            ed.js("document.getElementById('tgPipes').click()")
            ed.frames(3)
            p_off = os.path.join(shots_dir, name + ('_gl_off.png' if depth else '_off.png'))
            ed.screenshot(p_off)
            shots += [p_on, p_off] if depth else [p_off]
            on, off = crop(p_on), crop(p_off)
            cam = cam_of(case[1], cal)[0]
            for si in case[6]:
                if si == 'far':
                    th, raw = measure_far(proj, on, off, w, h)
                    ok = len(th) >= 50 and 1.6 <= np.median(th) <= 2.8 and np.percentile(th, 10) >= 1.5 and np.median(raw) < 1.0
                    all_ok &= bool(ok)
                    print('   먼 관 띠 (관경 0.2 m, 120 m): 열 %d · 화면 두께 중앙 %.2f px (10%% %.2f) · 늘리지 않으면 %.2f px  %s'
                          % (len(th), np.median(th) if len(th) else 0, np.percentile(th, 10) if len(th) else 0,
                             np.median(raw) if len(raw) else 0, '통과' if ok else '실패'))
                    continue
                cname = ('자홍', '주황')[si]
                if depth:
                    r = measure_tube(si, proj, cam, on, off, w, h, legacy)
                    if len(r) < 20:
                        print('   %s 관: 잴 곳 %d  실패 (보여야 함)' % (cname, len(r)))
                        all_ok = False
                        continue
                    e_edge = np.concatenate([r[:, 0] - r[:, 2], r[:, 1] - r[:, 3]])
                    e_cen = (r[:, 0] + r[:, 1]) / 2  # 중심선 투영 = s 0
                    m1, p1 = stat(e_edge)
                    m2, p2 = stat(e_cen)
                    wid = np.median(r[:, 1] - r[:, 0])
                    ok = m1 <= 1.0 and m2 <= 1.0
                    all_ok &= bool(ok)
                    print('   %s 관 (WebGL): 표본 %4d  관 폭 %5.1f px | 바깥선-원통 윤곽 중앙 %.2f  90%% %.2f px | 가운데-중심선 중앙 %.2f  90%% %.2f px  %s'
                          % (cname, len(r), wid, m1, p1, m2, p2, '통과' if ok else '실패'))
                else:
                    r = measure_band(si, proj, on, off, w, h, legacy)
                    if len(r) < 20:
                        print('   %s 띠: 잴 곳 %d  실패 (보여야 함)' % (cname, len(r)))
                        all_ok = False
                        continue
                    s_lo, s_hi, b_lo, b_hi, p_lo, p_hi = r.T
                    e_sp = np.concatenate([s_lo - p_lo, s_hi - p_hi])
                    if legacy:
                        e_bs = (b_lo + b_hi) / 2 - (s_lo + s_hi) / 2
                        what = '관로 가운데-사진 띠 가운데'
                    else:
                        e_bs = np.concatenate([b_lo - s_lo, b_hi - s_hi])
                        what = '관로 가장자리-사진 띠 가장자리'
                    m1, p1 = stat(e_bs)
                    m2, p2 = stat(e_sp)
                    ok = m1 <= 1.0 and m2 <= 1.0
                    all_ok &= bool(ok)
                    print('   %s 띠: 표본 %4d  사진 띠 폭 %5.1f px · 관로 띠 폭 %5.1f px | %s 중앙 %.2f  90%% %.2f px | 사진 띠-계산 중앙 %.2f px  %s'
                          % (cname, len(r), np.median(s_hi - s_lo), np.median(b_hi - b_lo), what, m1, p1, m2, '통과' if ok else '실패'))
        er = errors(ed)
        print('   JS 오류 %d 줄' % len(er))
        for ln in er:
            print('   오류:', str(ln).strip()[:200])
            all_ok = False
        if case[3] == 0:
            all_ok &= letter_check(view, case, proj_dir, w)
        print('   사진:', ' · '.join(shots))
    return all_ok


if __name__ == '__main__':
    sys.exit(main())
