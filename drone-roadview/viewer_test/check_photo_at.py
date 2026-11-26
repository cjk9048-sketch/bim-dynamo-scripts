"""원본 사진 보기 (roadview.photo_at) 시험 — 3D 매핑 결과 폴더 (work/sparse_txt · db.db · 사진 폴더) 로.

    python -I check_photo_at.py --out <3D 매핑 결과 폴더> --shots <그림 폴더>

  1 좌표 왕복: 모델 점 → ECEF → (E, N, 표고) (mapping.to_5186_h) → photo_at.enh_to_ecef → 같은 ECEF (1 mm 안)
  2 다시 비추기: COLMAP 3D 점 (궤적 긴 것 몇 개) → (E, N, 표고) → photos_at 의 (u, v) 가 그 점을 찍은 사진의 특징점 자리와 같음
    (가운데값 2 화소 · 최대 6 화소 안) · 궤적 사진 대부분이 목록에 들어옴
  3 그림: 한 점 → 좋은 순서 사진 6장의 그 자리 둘레 (빨간 고리) + 정사영상 그 자리 → <shots>/원본사진_자리.png
     photo_view (전체 · 확대) 그림 위 표시 자리 → <shots>/원본사진_보기_전체.png · _확대.png
"""
import argparse
import base64
import os
import sys
import time

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from roadview import mapping, photo_at  # noqa: E402

R = {'ok': True, 'fails': []}


def report(name, ok, text=''):
    print('   %-46s %s  %s' % (name, text, '통과' if ok else '실패'))
    R['ok'] &= bool(ok)
    if not ok:
        R['fails'].append(name)
    return bool(ok)


def tracks(ws, want=6, scan=40000):
    """궤적이 긴 3D 점 몇 개 (모델 X · [(image_id, point2d_idx)])."""
    pts = []
    with open(os.path.join(ws, 'sparse_txt', 'points3D.txt'), encoding='utf-8') as fp:
        for i, line in enumerate(fp):
            if line.startswith('#'):
                continue
            if i > scan:
                break
            p = line.split()
            tr = list(map(int, p[8:]))
            if len(tr) >= 16 and float(p[7]) < 0.8:
                pts.append((np.array(list(map(float, p[1:4]))), list(zip(tr[0::2], tr[1::2]))))
    rng = np.random.default_rng(3)
    return [pts[k] for k in rng.choice(len(pts), size=min(want, len(pts)), replace=False)] if pts else []


def obs2d(ws, need):
    """{image_id: (이름, [(x, y) …])} — images.txt 의 특징점 줄 (필요한 사진만)."""
    out = {}
    with open(os.path.join(ws, 'sparse_txt', 'images.txt'), encoding='utf-8') as fp:
        head = None
        for line in fp:
            if line.startswith('#'):
                continue
            if head is None:
                p = line.split(None, 9)
                head = (int(p[0]), p[9].rstrip('\r\n'))
                continue
            if head[0] in need:
                v = np.array(line.split(), float).reshape(-1, 3)
                out[head[0]] = (head[1], v[:, :2])
            head = None
    return out


def decode(url):
    import cv2
    b = base64.b64decode(url.split(',', 1)[1])
    return cv2.imdecode(np.frombuffer(b, np.uint8), cv2.IMREAD_COLOR)


def main():
    import cv2
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    ap.add_argument('--shots', required=True)
    ap.add_argument('--at', default=None, help='그림에 쓸 자리 «E,N» (표고 = DSM) · 없으면 궤적 가장 긴 3D 점')
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    out, SD = os.path.abspath(a.out), os.path.abspath(a.shots)
    os.makedirs(SD, exist_ok=True)
    ws = os.path.join(out, 'work')
    M = mapping._read_js(os.path.join(out, 'data', 'mapping.js'), 'RV_MAPPING')
    mapping.set_crs(M.get('crs', 'EPSG:5186'))
    report('사진 폴더', bool(photo_at.photos_dir(out)), str(photo_at.photos_dir(out)))
    t0 = time.time()
    m = photo_at._model(out)
    print('   카메라 자료 %.1f 초 · 사진 %d' % (time.time() - t0, len(m['names'])))

    print('== 1 좌표 왕복')
    TP = tracks(ws)
    X = np.array([p[0] for p in TP])
    ecef = m['s'] * (X @ m['R'].T) + m['t']
    E, N, H = mapping.to_5186_h(ecef)
    back = np.array([photo_at.enh_to_ecef(E[i], N[i], H[i], m['crs'])[0] for i in range(len(E))])
    err = np.abs(back - ecef).max()
    report('1 (E, N, 표고) → ECEF 왕복', err < 1e-3, '최대 %.2f mm' % (err * 1000))

    print('== 2 다시 비추기 (COLMAP 특징점)')
    need = {iid for _x, tr in TP for iid, _k in tr}
    ob = obs2d(ws, need)
    errs, hit, tot = [], 0, 0
    t1 = time.time()
    for i, (_x, tr) in enumerate(TP):
        r = photo_at.photos_at(out, float(E[i]), float(N[i]), float(H[i]), n=1000)
        got = {q['name']: (q['u'], q['v']) for q in (r.get('photos') or [])}
        for iid, k in tr:
            if iid not in ob:
                continue
            nm, xy = ob[iid]
            tot += 1
            if nm in got:
                hit += 1
                errs.append(float(np.hypot(*(np.array(got[nm]) - xy[k]))))
    dt = (time.time() - t1) / max(1, len(TP))
    e = np.array(errs) if errs else np.array([np.inf])
    report('2 사진 위 자리 = 특징점 자리', np.median(e) < 2.0 and e.max() < 6.0,
           '가운데값 %.2f · 최대 %.2f 화소 · %d 곳' % (np.median(e), e.max(), len(errs)))
    report('2 그 점을 찍은 사진이 목록에', tot > 0 and hit / tot > 0.9, '%d / %d · 한 번 %.2f 초' % (hit, tot, dt))

    print('== 3 그림')
    i0 = int(np.argmax([len(tr) for _x, tr in TP]))
    E0, N0, H0 = float(E[i0]), float(N[i0]), float(H[i0])
    if a.at:
        E0, N0 = map(float, a.at.split(','))
        H0 = float(mapping.dsm_sampler(out)(np.array([E0]), np.array([N0]))[0])
    r = photo_at.photos_at(out, E0, N0, H0)
    report('3 사진 목록 (좋은 순서)', r['ok'] and len(r['photos']) >= 4 and all(r['photos'][j]['score'] <= r['photos'][j + 1]['score'] for j in range(len(r['photos']) - 1)),
           '%d 장 · 첫 점수 %.3f' % (len(r.get('photos', [])), r['photos'][0]['score'] if r.get('photos') else -1))
    S = 360
    tiles = []
    for q in r['photos'][:6]:
        img = cv2.imdecode(np.fromfile(os.path.join(r['dir'], q['name']), np.uint8), cv2.IMREAD_COLOR | cv2.IMREAD_IGNORE_ORIENTATION)
        u, v = q['u'], q['v']
        x0, y0 = int(round(u - S / 2)), int(round(v - S / 2))
        tile = np.zeros((S, S, 3), np.uint8)
        sx0, sy0 = max(0, x0), max(0, y0); sx1, sy1 = min(img.shape[1], x0 + S), min(img.shape[0], y0 + S)
        tile[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0] = img[sy0:sy1, sx0:sx1]
        cv2.circle(tile, (S // 2, S // 2), 14, (0, 0, 255), 2, cv2.LINE_AA)
        cv2.putText(tile, '%s  %.2f' % (q['name'][-12:], q['score']), (6, S - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1, cv2.LINE_AA)
        tiles.append(tile)
    # 정사영상 그 자리 (같은 넓이쯤 · 사진 GSD 와 비슷하게)
    try:
        from PIL import Image
        Image.MAX_IMAGE_PIXELS = None
        tfw = [float(x) for x in open(os.path.join(out, '결과물', '정사영상.tfw')).read().split()]
        c, xw, yn = tfw[0], tfw[4] - tfw[0] / 2, tfw[5] + tfw[0] / 2
        with Image.open(os.path.join(out, '결과물', '정사영상.tif')) as im:
            col, row = (E0 - xw) / c, (yn - N0) / c
            half = S / 2 * (0.0163 / c)
            box = (int(col - half), int(row - half), int(col + half), int(row + half))
            o = np.asarray(im.crop(box).convert('RGB').resize((S, S)))[:, :, ::-1].copy()
        cv2.circle(o, (S // 2, S // 2), 14, (0, 0, 255), 2, cv2.LINE_AA)
        cv2.putText(o, 'ortho', (6, S - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1, cv2.LINE_AA)
        tiles.append(o)
    except Exception as ex:  # noqa: BLE001
        print('   정사영상 못 읽음', ex)
    while len(tiles) % 4:
        tiles.append(np.zeros((S, S, 3), np.uint8))
    rows = [np.hstack(tiles[j:j + 4]) for j in range(0, len(tiles), 4)]
    p = os.path.join(SD, '원본사진_자리.png')
    cv2.imencode('.png', np.vstack(rows))[1].tofile(p)
    print('   그림', p, '· 점 E %.2f N %.2f H %.2f' % (E0, N0, H0))
    q = r['photos'][0]
    for zoom, fn in ((False, '원본사진_보기_전체.png'), (True, '원본사진_보기_확대.png')):
        v = photo_at.photo_view(out, q['name'], q['u'], q['v'], zoom)
        img = decode(v['url'])
        ok = v['ok'] and img is not None and img.shape[1] == v['w'] and img.shape[0] == v['h'] and (max(v['w'], v['h']) <= 1600)
        cv2.circle(img, (int(round(v['x'])), int(round(v['y']))), 16, (0, 0, 255), 2, cv2.LINE_AA)
        cv2.imencode('.png', img)[1].tofile(os.path.join(SD, fn))
        if zoom:                                            # 확대 그림의 표시 자리 = 원본 화소 그대로 (원본 둘레와 같은 화소)
            full = cv2.imdecode(np.fromfile(os.path.join(r['dir'], q['name']), np.uint8), cv2.IMREAD_COLOR | cv2.IMREAD_IGNORE_ORIENTATION)
            x0, y0 = int(round(q['u'] - v['x'])), int(round(q['v'] - v['y']))
            img2 = decode(v['url'])
            same = np.abs(img2[:50, :50].astype(int) - full[y0:y0 + 50, x0:x0 + 50].astype(int)).mean()
            ok = ok and same < 6
        report('3 photo_view %s' % ('확대' if zoom else '전체'), ok, '%d×%d · 자리 %.0f, %.0f' % (v['w'], v['h'], v['x'], v['y']))
    print('== %s · 실패 %d %s' % ('통과' if R['ok'] else '실패', len(R['fails']), R['fails']))
    return 0 if R['ok'] else 1


if __name__ == '__main__':
    sys.exit(main())
