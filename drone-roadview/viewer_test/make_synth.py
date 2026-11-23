"""드론 로드뷰 보기 화면 시험용 가짜 자료 만들기 (현장 값 없음).

    python -I make_synth.py --out <폴더> [--width 4096]

평평한 땅(표고 50 m) 위 15 m 높이에서 찍은 사진 4장 (영상 0001: 5 m 간격 3장, 영상 0002: 옆 길 1장)을
보기 화면과 똑같은 등장방형 약속으로 그린다.
  열 x → λ = (x+0.5)/W·360° − 180° (격자 북에서 시계 방향), 행 y → φ = 90° − (y+0.5)/H·180°
  세계 방향 (E,N,U) = (cosφ·sinλ, cosφ·cosλ, sinφ)
땅에는 알려진 선 위에 관경 폭 띠(자홍 0.3 m · 주황 0.8 m)를 칠하고, 같은 선을 관로로 pipes.js 에 넣는다.
  자홍 = g(지표 표고) 있음, 깊이 = 평균매설깊이(project.depth 1.0) · 주황 = hasZ (관 중심 표고 48.1) + g.
  0004 사진은 agl 을 15.6 으로 적어 groundZ 가 0.6 m 틀리게 한다 → 띠가 g 를 써야 맞는다.
  --legacy-pipes: 옛 형식 (dia · g · hasZ 없음, project 에 depth 없이 cover 만).
지평선 위 6° 에 N/E/S/W 글자를 쓴다.
"""
import argparse
import base64
import io
import json
import math
import os
import shutil
import tempfile

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
VIEWER = os.path.normpath(os.path.join(HERE, '..', 'viewer'))

ORIGIN = {'E': 200000.0, 'N': 500000.0}   # 가짜 원점
LAT0, LON0 = 37.0, 127.0                  # 가짜 위경도 (원점 자리)
GZ = 50.0                                 # 땅 표고
HEIGHT = 15.0                             # 땅 위 사진 높이 (agl)
COVER = 1.2                               # 옛 키 (cover)
DEPTH = 1.0                               # 평균매설깊이 (지표 → 관 윗면), project.depth
SHOTS = [  # e, n, yaw(정보), time, video
    (0.0, 0.0, 90.0, 10.0, '0001'),
    (5.0, 0.0, 85.0, 11.0, '0001'),
    (10.0, 0.0, 95.0, 12.0, '0001'),
    (5.0, -8.0, 270.0, 30.0, '0002'),
]
VIDEOS = [{'id': '0001', 'name': 'SYNTH_0001'}, {'id': '0002', 'name': 'SYNTH_0002'}]
# 띠 = 관로: (꼭짓점들, 반폭 m (= 관경/2), 띠 색, 관로 층)
STRIPES = [
    ([(-30.0, 5.0), (15.0, 5.0), (45.0, 20.0)], 0.15, (255, 0, 255), 0),   # 꺾임 하나
    ([(15.0, -40.0), (15.0, 0.0), (15.0, 40.0)], 0.40, (255, 150, 0), 1),
]
# 띠 없는 먼 관 (관경 0.2 m, 120 m 북쪽): 화면에서 2 px 보다 얇아지는 곳 → 최소 폭 2 px 시험
FAR_LINE = {'verts': [(-200.0, 120.0), (200.0, 120.0)], 'dia': 0.2, 'layer': 0, 'label': '먼 관'}
HASZ_Z = GZ - 1.5 - 0.40  # 주황 관 중심 표고 (hasZ): 윗면 깊이 1.5 m
AGL_WRONG = {'0004': 15.6}  # 사진 자료의 agl 을 틀리게 (groundZ = z − agl 이 0.6 m 낮음)
LAYERS = [
    {'name': '상수관', 'color': '#00e5ff'},
    {'name': '하수관', 'color': '#7cfc00'},
]
LABELS = ['상수', '하수']
# --calib: 사진을 영상마다 이만큼 틀리게 찍은 것으로 그리고, 같은 값을 calib.js 로 준다 (보기 화면이 바로잡아야 함)
CALIB_ERR = {
    '0001': {'dx': 1.5, 'dy': -2.0, 'dz': 0.8, 'dyaw': 7.0, 'dpitch': 2.0, 'droll': -3.0},
    '0002': {'dx': -1.0, 'dy': 1.2, 'dz': -0.5, 'dyaw': -5.0, 'dpitch': -1.5, 'droll': 2.5},
}


def en2ll(e, n):
    lat = LAT0 + n / 111320.0
    lon = LON0 + e / (111320.0 * math.cos(math.radians(LAT0)))
    return [round(lat, 9), round(lon, 9)]


def seg_dist(px, py, a, b):
    ax, ay = a
    bx, by = b
    dx, dy = bx - ax, by - ay
    t = np.clip(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy), 0.0, 1.0)
    return np.hypot(px - (ax + t * dx), py - (ay + t * dy))


def rot(cal):
    """보기 화면과 같은 R = Rz(dyaw)·Rx(dpitch)·Ry(droll). 사진 방향 p 의 실제 방향 w = R·p."""
    a, b, g = (math.radians(cal.get(k, 0.0)) for k in ('dyaw', 'dpitch', 'droll'))
    Rz = np.array([[math.cos(a), math.sin(a), 0], [-math.sin(a), math.cos(a), 0], [0, 0, 1]])
    Rx = np.array([[1, 0, 0], [0, math.cos(b), -math.sin(b)], [0, math.sin(b), math.cos(b)]])
    Ry = np.array([[math.cos(g), 0, math.sin(g)], [0, 1, 0], [-math.sin(g), 0, math.cos(g)]])
    return Rz @ Rx @ Ry


def render_pano(cam, W, H, R=None):
    """cam = 실제 사진 자리. R 이 있으면 사진이 R 만큼 틀어진 것처럼 그린다 (w = R·p)."""
    out = np.empty((H, W, 3), np.uint8)
    lam = ((np.arange(W) + 0.5) / W * 2.0 - 1.0) * math.pi
    sinl, cosl = np.sin(lam), np.cos(lam)
    sky_top = np.array([38.0, 78.0, 160.0])
    sky_hor = np.array([178.0, 206.0, 236.0])
    for r0 in range(0, H, 128):
        r1 = min(H, r0 + 128)
        phi = math.pi / 2 - (np.arange(r0, r1) + 0.5) / H * math.pi
        cp, sp = np.cos(phi)[:, None], np.sin(phi)[:, None]
        E = cp * sinl[None, :]
        N = cp * cosl[None, :]
        U = np.broadcast_to(sp, E.shape)
        if R is not None:
            E, N, U = (R[i, 0] * E + R[i, 1] * N + R[i, 2] * U for i in range(3))
        blk = np.empty(E.shape + (3,), np.float64)
        # 하늘
        t = np.sqrt(np.clip(U, 0.0, 1.0))[..., None]
        sky = sky_hor * (1.0 - t) + sky_top * t
        # 땅
        ground = U < 0
        with np.errstate(divide='ignore', invalid='ignore'):
            tt = np.where(ground, (GZ - cam[2]) / np.where(ground, U, -1.0), 0.0)
        ge = cam[0] + tt * E
        gn = cam[1] + tt * N
        g = np.full(E.shape, 120.0)
        # 10 m 격자선
        fe = np.abs((ge + 5.0) % 10.0 - 5.0)
        fn = np.abs((gn + 5.0) % 10.0 - 5.0)
        g = np.where(((fe < 0.06) | (fn < 0.06)) & (tt < 150.0), 88.0, g)  # 먼 곳 격자는 빼서 얼룩 줄임
        gc = np.repeat(g[..., None], 3, axis=2)
        foot = np.maximum(tt * math.pi / H, 1e-3)  # 화소 하나의 땅 크기(대략)
        for verts, hw, col, _ in STRIPES:
            d = np.full(E.shape, np.inf)
            for a, b in zip(verts[:-1], verts[1:]):
                d = np.minimum(d, seg_dist(ge, gn, a, b))
            cov = np.clip((hw - d) / foot + 0.5, 0.0, 1.0)[..., None]
            gc = gc * (1.0 - cov) + np.array(col, np.float64) * cov
        haze = (1.0 - np.exp(-tt / 600.0))[..., None]
        gc = gc * (1.0 - haze) + sky_hor * haze
        blk[:] = np.where(ground[..., None], gc, sky)
        out[r0:r1] = np.clip(blk + 0.5, 0, 255).astype(np.uint8)
    img = Image.fromarray(out, 'RGB')
    # 지평선 위 6° 에 방위 글자
    dr = ImageDraw.Draw(img)
    size = int(H * 7.0 / 180.0)
    try:
        font = ImageFont.truetype(os.path.join(os.environ.get('WINDIR', r'C:\Windows'), 'Fonts', 'arialbd.ttf'), size)
    except OSError:
        font = ImageFont.load_default(size)
    yc = (90.0 - 6.0) / 180.0 * H - 0.5
    for letter, az in (('N', 0.0), ('E', 90.0), ('S', 180.0), ('W', -90.0)):
        xc = (az + 180.0) / 360.0 * W - 0.5
        for xx in (xc, xc - W) if az == 180.0 else (xc,):
            dr.text((xx, yc), letter, font=font, fill=(16, 16, 22), anchor='mm')
    return img


def jpeg_bytes(img, q=92):
    buf = io.BytesIO()
    img.save(buf, 'JPEG', quality=q, subsampling=0)
    return buf.getvalue()


def write_js(path, text):
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)


def merc_px(lat, lon, z):
    """위경도 → 줌 z 의 세계 픽셀 (타일 256)."""
    s = math.sin(math.radians(lat))
    k = 256 * 2 ** z
    return (lon + 180.0) / 360.0 * k, (0.5 - math.log((1 + s) / (1 - s)) / (4 * math.pi)) * k


def make_tiles(out, zooms=range(15, 20), margin=400.0):
    """data 옆 tiles/{z}/{x}/{y}.png — 바둑판 + 관로(빨강) · 사진 자리(빨강 동그라미)를 정확한 자리에 굽는다."""
    es = [s[0] for s in SHOTS]
    ns = [s[1] for s in SHOTS]
    lat0, lon0 = en2ll(min(es) - margin, min(ns) - margin)
    lat1, lon1 = en2ll(max(es) + margin, max(ns) + margin)
    try:
        font = ImageFont.truetype(os.path.join(os.environ.get('WINDIR', r'C:\Windows'), 'Fonts', 'arial.ttf'), 14)
    except OSError:
        font = ImageFont.load_default()
    count = 0
    for z in zooms:
        xa, ya = merc_px(lat1, lon0, z)
        xb, yb = merc_px(lat0, lon1, z)
        for ty in range(int(ya // 256), int(yb // 256) + 1):
            for tx in range(int(xa // 256), int(xb // 256) + 1):
                im = Image.new('RGB', (256, 256), (58, 66, 60) if (tx + ty) % 2 else (72, 80, 74))
                dr = ImageDraw.Draw(im)
                dr.rectangle([0, 0, 255, 255], outline=(110, 120, 112))
                dr.text((8, 8), '%d/%d/%d' % (z, tx, ty), font=font, fill=(200, 210, 200))
                for verts, _, _, _ in STRIPES:
                    pts = [merc_px(*en2ll(x, y), z) for x, y in verts]
                    dr.line([(px - tx * 256, py - ty * 256) for px, py in pts], fill=(255, 40, 40), width=3)
                for e, n, *_ in SHOTS:
                    px, py = merc_px(*en2ll(e, n), z)
                    px, py = px - tx * 256, py - ty * 256
                    dr.ellipse([px - 9, py - 9, px + 9, py + 9], outline=(255, 40, 40), width=2)
                d = os.path.join(out, 'tiles', str(z), str(tx))
                os.makedirs(d, exist_ok=True)
                im.save(os.path.join(d, '%d.png' % ty))
                count += 1
    return count


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default=os.path.join(tempfile.gettempdir(), 'rv_synth'))
    ap.add_argument('--width', type=int, default=4096)
    ap.add_argument('--calib', action='store_true', help='사진을 알려진 만큼 틀어 찍고, 바로잡는 calib.js 를 쓴다')
    ap.add_argument('--no-pipes', action='store_true', help='pipes.js 를 쓰지 않는다')
    ap.add_argument('--xyz', action='store_true', help='바탕 지도를 tiles/{z}/{x}/{y}.png (상대 경로) 로')
    ap.add_argument('--drop', nargs='*', default=[], help='사진 파일을 만들지 않을 사진 번호 (예: 0003)')
    ap.add_argument('--legacy-pipes', action='store_true', help='옛 pipes.js 형식 (dia · g · hasZ 없음) + project 에 depth 없음')
    args = ap.parse_args()
    out = os.path.abspath(args.out)
    W = args.width
    H = W // 2

    for sub in ('viewer', 'data', os.path.join('data', 'pano'), os.path.join('data', 'thumb')):
        os.makedirs(os.path.join(out, sub), exist_ok=True)
    for f in ('calib.js', 'pipes.js'):
        p = os.path.join(out, 'data', f)
        if os.path.exists(p):
            os.remove(p)  # 없을 때도 되는지 본다

    shots = []
    for k, (e, n, yaw, tm, vid) in enumerate(SHOTS, start=1):
        sid = '%04d' % k
        cal = CALIB_ERR[vid] if args.calib else {}
        R = rot(cal) if args.calib else None
        cam = (e + cal.get('dx', 0.0), n + cal.get('dy', 0.0), GZ + HEIGHT + cal.get('dz', 0.0))  # 실제 자리
        img = render_pano(cam, W, H, R)
        uri = 'data:image/jpeg;base64,' + base64.b64encode(jpeg_bytes(img)).decode('ascii')
        pano_js = os.path.join(out, 'data', 'pano', sid + '.js')
        if sid in args.drop:
            if os.path.exists(pano_js):
                os.remove(pano_js)  # 사진 파일이 없을 때 시험
        else:
            write_js(pano_js, 'RV_PANO_LOADED("%s", "%s");\n' % (sid, uri))
        img.resize((256, 128), Image.LANCZOS).save(os.path.join(out, 'data', 'thumb', sid + '.jpg'), quality=85)
        lat, lon = en2ll(e, n)
        shot = {
            'id': sid, 'seq': k, 'video': vid, 'time': tm, 'lat': lat, 'lon': lon,
            'e': e, 'n': n, 'z': GZ + HEIGHT, 'groundZ': GZ, 'agl': HEIGHT, 'relAlt': HEIGHT, 'yaw': yaw,
            'file': 'data/pano/%s.js' % sid, 'thumb': 'data/thumb/%s.jpg' % sid,
        }
        if vid == '0002':
            del shot['groundZ'], shot['relAlt']  # groundZ 없이 z − agl 로 되는지 본다
            if not args.legacy_pipes and sid in AGL_WRONG:
                shot['agl'] = AGL_WRONG[sid]
        shots.append(shot)
        print('pano', sid, '%dx%d' % (W, H), '%.1f MB' % (len(uri) / 1e6))

    basemap = {'type': 'osm'}
    if args.xyz:
        print('tiles', make_tiles(out))
        basemap = {'type': 'xyz', 'url': 'tiles/{z}/{x}/{y}.png', 'maxZoom': 19}
    project = {
        'version': 1, 'title': '시험 자료' + (' (맞춤)' if args.calib else ''), 'crs': 'EPSG:5186',
        'origin': ORIGIN, 'cover': COVER,
        **({} if args.legacy_pipes else {'depth': DEPTH}),
        'basemap': basemap,
        'videos': VIDEOS,
        'shots': shots,
    }
    write_js(os.path.join(out, 'data', 'project.js'),
             'window.RV_PROJECT = ' + json.dumps(project, ensure_ascii=False, indent=1) + ';\n')

    if not args.no_pipes:
        pipes = {'layers': LAYERS, 'lines': []}
        for verts, hw, _, layer in STRIPES:
            if args.legacy_pipes:
                pipes['lines'].append({
                    'layer': layer, 'label': LABELS[layer],
                    'pts': [[x, y, GZ - COVER] for x, y in verts],
                    'll': [en2ll(x, y) for x, y in verts],
                })
                continue
            ln = {'layer': layer, 'label': LABELS[layer], 'dia': round(hw * 2, 3),
                  'pts': [[x, y, None] for x, y in verts], 'g': [GZ for _ in verts],
                  'll': [en2ll(x, y) for x, y in verts]}
            if layer == 1:
                ln['hasZ'] = True
                ln['pts'] = [[x, y, HASZ_Z] for x, y in verts]
            pipes['lines'].append(ln)
        if not args.legacy_pipes:
            v = FAR_LINE['verts']
            pipes['lines'].append({'layer': FAR_LINE['layer'], 'label': FAR_LINE['label'], 'dia': FAR_LINE['dia'],
                                   'pts': [[x, y, None] for x, y in v], 'g': [GZ for _ in v], 'll': [en2ll(x, y) for x, y in v]})
        write_js(os.path.join(out, 'data', 'pipes.js'),
                 'window.RV_PIPES = ' + json.dumps(pipes, ensure_ascii=False, indent=1) + ';\n')
    if args.calib:
        write_js(os.path.join(out, 'data', 'calib.js'),
                 'window.RV_CALIB = ' + json.dumps({'videos': CALIB_ERR}, indent=1) + ';\n')

    shutil.copyfile(os.path.join(VIEWER, 'index.html'), os.path.join(out, '로드뷰.html'))
    shutil.copyfile(os.path.join(VIEWER, 'index.html'), os.path.join(out, 'index.html'))  # 빌더처럼 같은 쪽을 둘로
    for f in ('app.js', 'style.css'):
        shutil.copyfile(os.path.join(VIEWER, f), os.path.join(out, 'viewer', f))
    print('out', out)


if __name__ == '__main__':
    main()
