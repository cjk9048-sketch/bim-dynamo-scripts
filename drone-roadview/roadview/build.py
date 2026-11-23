"""드론 360 영상(OSV) → 로드뷰 폴더 만들기.

쓰는 법 (예):
  python -m roadview.build "C:\\...\\드론촬영" --out "C:\\...\\드론촬영\\로드뷰" --interval 5
  python -m roadview.build 영상.OSV --out 폴더 --interval 10 --pipes 관로.shp --dia-field 관경 --depth 1.2

결과 폴더의 «로드뷰.html» 을 두 번 누르면 열린다 (서버 없이).
관로는 돌릴 때마다 --pipes 로 준다 (SHP 또는 DXF · 안 주면 관로 없음). 맞춤 값(data/calib.js)은 그대로.
"""
from __future__ import annotations

import argparse
import base64
import glob
import hashlib
import io
import json
import math
import os
import shutil
import sys
import time
from concurrent.futures import ProcessPoolExecutor, as_completed

import numpy as np

from .camera import default_rig
from .frames import extract_pair, find_ffmpeg
from .geo import Geo
from .osv import read_meta
from .pose import R_world_cam, yaw_offsets

HERE = os.path.dirname(os.path.abspath(__file__))
VIEWER_DIR = os.path.join(os.path.dirname(HERE), 'viewer')
# ffmpeg -ss 는 그 시각 «이후» 첫 장면을 준다. 기록 시각 그대로(또는 +0.001) 주면 다음 장면이 나온다
# (1123 확인). 그래서 장면 간격의 1/4 앞을 준다.
SEEK_FRACTION = 0.25
PANO_VERSION = 3     # 360 사진 만드는 법이 바뀌면 올림 → 옛 사진 다시 만듦 (3: 성긴 격자 반 칸 치우침 고침)
TERRAIN_VERSION = 2  # 땅 높이 재는 법이 바뀌면 올림


def _low_priority():
    """이 프로그램을 «낮음» 우선순위로. 일꾼 · ffmpeg 도 물려받는다 (Windows 규칙)."""
    if os.name != 'nt':
        return
    try:
        import ctypes
        from ctypes import wintypes
        k32 = ctypes.WinDLL('kernel32', use_last_error=True)
        k32.GetCurrentProcess.restype = wintypes.HANDLE
        k32.SetPriorityClass.argtypes = [wintypes.HANDLE, wintypes.DWORD]
        k32.SetPriorityClass.restype = wintypes.BOOL
        BELOW_NORMAL = 0x00004000
        if not k32.SetPriorityClass(k32.GetCurrentProcess(), BELOW_NORMAL):
            print('우선순위 낮추기 실패 (%d)' % ctypes.get_last_error())
    except Exception as ex:
        print('우선순위 낮추기 실패: %s' % ex)


def log(msg, fp=None):
    line = time.strftime('%H:%M:%S ') + msg
    print(line, flush=True)
    if fp:
        fp.write(line + '\n')
        fp.flush()


def _write_atomic(path, data: bytes):
    tmp = path + '.tmp'
    with open(tmp, 'wb') as f:
        f.write(data)
    os.replace(tmp, path)


# ---------------------------------------------------------------- 장면 고르기

def _has_gps(f):
    return f.lat is not None and f.lon is not None


def track_en(frames, geo):
    return np.array([geo.ll_to_en(f.lat, f.lon) if _has_gps(f) else (np.nan, np.nan) for f in frames])


def select_shots(frames, en, interval, min_rel=2.0):
    """진행 거리 interval(m) 마다 한 장면. 이륙 · 착륙 근처(낮음) · GPS · 자세 없음은 뺀다.
    진행 거리는 GPS 가 있는 앞뒤 장면 사이마다 더한다 (낮음 · 자세 없음 구간도 거리는 셈)."""
    ok = np.array([_has_gps(f) and f.q_body is not None and f.rel_alt is not None and f.rel_alt >= min_rel
                   for f in frames])
    picks = []
    dist = 0.0
    last = None
    prev = None                       # GPS 있는 바로 앞 장면
    s = np.zeros(len(frames))
    for i, f in enumerate(frames):
        if _has_gps(f):
            if prev is not None:
                step = float(np.hypot(*(en[i] - en[prev])))
                dt = f.time - frames[prev].time
                if step < 30 or dt > 2.0:        # 튐은 버리되, GPS 가 끊겼다 돌아온 것은 셈
                    dist += step
            prev = i
        s[i] = dist
        if not ok[i]:
            continue
        if last is None or dist - s[last] >= interval:
            picks.append(i)
            last = i
    return picks, s


# ---------------------------------------------------------------- 높이

def _height_job(args):
    osv, ffmpeg, rig_dict, fa, fb, Ra, Rb, ea, eb, rel_a, rel_b, H0 = args
    from .camera import Rig
    from .height import estimate_agl
    rig = Rig(**rig_dict)
    A = extract_pair(ffmpeg, osv, fa, 1920)
    B = extract_pair(ffmpeg, osv, fb, 1920)
    return estimate_agl(A, B, rig, np.array(Ra), np.array(Rb), ea, eb, rel_a, rel_b, H0)


def estimate_terrain(osv, frames, en, s, R_list, rig, ffmpeg, pool, step_m, seek_back, fp):
    """진행 거리 step_m 마다 땅 높이 차 T(= 이륙점 기준 높이 − 땅 위 높이)를 잰다 → [(s, T)]."""
    jobs = []
    next_s = -1e9
    n = len(frames)

    def usable(k):
        return R_list[k] is not None and frames[k].rel_alt is not None and frames[k].rel_alt >= 2.0

    for i in range(n):
        if not usable(i) or s[i] < next_s:
            continue
        H0 = max(3.0, frames[i].rel_alt)
        need = max(8.0, 0.6 * min(H0, 40.0))
        j = None
        k = i + 1
        while k < n and frames[k].time - frames[i].time <= 8.0:
            if usable(k) and math.hypot(*(en[k] - en[i])) >= need:
                j = k
                break
            k += 1
        if j is None:
            continue
        jobs.append((i, (osv, ffmpeg, dict(rig.__dict__), frames[i].time - seek_back, frames[j].time - seek_back,
                         R_list[i].tolist(), R_list[j].tolist(), tuple(en[i]), tuple(en[j]),
                         frames[i].rel_alt, frames[j].rel_alt, H0)))
        next_s = s[i] + step_m
    out = []
    futs = {pool.submit(_height_job, a): i for i, a in jobs}
    for fu in as_completed(futs):
        i = futs[fu]
        try:
            r = fu.result()
        except Exception as ex:
            log('  높이 재기 실패 (%.1f초): %s' % (frames[i].time, ex), fp)
            continue
        if not r:
            continue
        good = r['votes'] >= 4 and abs(r['resid']) < 3.0 and 2.0 < r['H'] < 250
        log('  높이 %.1f초: H %.2f · 표 %d · 옆밀림 %.2f · 거리 %.1f → %s' % (
            frames[i].time, r['H'], r['votes'], r['resid'], r['base'], '씀' if good else '버림'), fp)
        if good:
            out.append((float(s[i]), frames[i].rel_alt - r['H']))
    out.sort()
    log('  땅 높이 잰 곳 %d / %d' % (len(out), len(jobs)), fp)
    return out


def terrain_at(samples, s_query, window=75.0):
    if not samples:
        return None
    ss = np.array([a for a, _ in samples]); ts = np.array([b for _, b in samples])
    sel = np.abs(ss - s_query) <= window
    if sel.sum() >= 2:
        return float(np.median(ts[sel]))
    return float(np.median(ts))


# ---------------------------------------------------------------- 360 사진

def _pano_job(args):
    osv, ffmpeg, rig_dict, t, R, width, quality, pano_js, thumb_jpg, sid = args
    from PIL import Image
    from .camera import Rig, stitch
    rig = Rig(**rig_dict)
    a, b = extract_pair(ffmpeg, osv, t)
    pano = stitch(a, b, rig, np.array(R), W=width)
    im = Image.fromarray(pano)
    buf = io.BytesIO()
    im.save(buf, 'JPEG', quality=quality, optimize=True)
    b64 = base64.b64encode(buf.getvalue()).decode('ascii')
    _write_atomic(pano_js, ('RV_PANO_LOADED("%s","data:image/jpeg;base64,%s");\n' % (sid, b64)).encode('ascii'))
    tb = io.BytesIO()
    im.resize((512, 256), Image.LANCZOS).save(tb, 'JPEG', quality=80)
    _write_atomic(thumb_jpg, tb.getvalue())
    return sid, len(b64)


# ---------------------------------------------------------------- 본 흐름

def main(argv=None):
    ap = argparse.ArgumentParser(description='드론 360 영상(OSV) → 로드뷰')
    ap.add_argument('inputs', nargs='+', help='OSV 파일 또는 폴더')
    ap.add_argument('--out', required=True, help='만들 폴더')
    ap.add_argument('--interval', type=float, default=5.0, help='촬영점 간격 (m, 기본 5)')
    ap.add_argument('--pipes', help='관로 파일 (.shp 또는 Civil 3D 에서 저장한 .dxf)')
    ap.add_argument('--dxf', help='(옛 이름) = --pipes')
    ap.add_argument('--dia-field', help='SHP 속성 중 관경(mm) 항목 이름')
    ap.add_argument('--label-field', help='SHP 속성 중 이름표로 보일 항목 (선택)')
    ap.add_argument('--default-dia', type=float, default=200.0, help='관경 모를 때 (mm, 기본 200)')
    ap.add_argument('--layers', help='DXF 관로 도면층 (쉼표, * 가능) 예: "상수*,W-*"')
    ap.add_argument('--crs', default='EPSG:5186', help='도면 좌표계 (기본 EPSG:5186 중부원점)')
    ap.add_argument('--depth', '--cover', dest='depth', type=float, default=1.2, help='평균매설깊이 (땅 → 관 윗면, m)')
    ap.add_argument('--home-z', type=float, help='이륙점 땅 높이 (EL, m). 없으면 관로에서 추정')
    ap.add_argument('--width', type=int, default=6144, help='360 사진 가로 화소 (기본 6144)')
    ap.add_argument('--quality', type=int, default=85)
    ap.add_argument('--vworld-key', help='VWorld 지도 키 (넣으면 결과 파일에 저장됨 — 공유 주의)')
    ap.add_argument('--ffmpeg', help='ffmpeg.exe 경로')
    ap.add_argument('--title', help='제목')
    ap.add_argument('--workers', type=int, default=0, help='동시 작업 수 (기본 자동)')
    ap.add_argument('--max-shots', type=int, default=0, help='영상마다 최대 촬영점 수 (시험용)')
    ap.add_argument('--from-time', type=float, default=0.0, help='시험용: 이 초부터')
    ap.add_argument('--to-time', type=float, default=1e9, help='시험용: 이 초까지')
    ap.add_argument('--no-terrain', action='store_true', help='땅 높이 자동 재기 끄기 (이륙점 높이 그대로)')
    ap.add_argument('--normal-priority', action='store_true', help='보통 우선순위로 (기본은 낮음 — PC 쓰면서 돌려도 안 버벅이게)')
    a = ap.parse_args(argv)
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass
    if not a.normal_priority:
        _low_priority()

    osvs = []
    for p in a.inputs:
        if os.path.isdir(p):
            osvs += sorted(glob.glob(os.path.join(p, '*.OSV')) + glob.glob(os.path.join(p, '*.osv')))
        elif os.path.isfile(p):
            osvs.append(p)
    seen = set()
    uniq = []
    for p in sorted(os.path.abspath(p) for p in osvs):
        k = os.path.normcase(p)
        if k not in seen:
            seen.add(k)
            uniq.append(p)
    osvs = uniq
    if not osvs:
        print('OSV 없음 → 영상 파일이나 폴더를 주세요')
        return 2

    out = os.path.abspath(a.out)
    os.makedirs(os.path.join(out, 'data', 'pano'), exist_ok=True)
    os.makedirs(os.path.join(out, 'data', 'thumb'), exist_ok=True)
    fp = open(os.path.join(out, '만들기_기록.txt'), 'a', encoding='utf-8')
    log('시작 · 영상 %d개 · 간격 %.1f m · 폭 %d' % (len(osvs), a.interval, a.width), fp)

    ffmpeg = find_ffmpeg(a.ffmpeg)
    geo = Geo(a.crs)
    rig = default_rig()
    rig_sig = json.dumps(rig.__dict__, sort_keys=True)
    workers = a.workers or max(2, min(6, (os.cpu_count() or 4) // 3))

    # 관로
    layers, lines = [], []
    pipe_path = a.pipes or a.dxf
    if pipe_path:
        from . import pipeload
        pats = [x.strip() for x in a.layers.split(',')] if a.layers else None
        layers, lines, note = pipeload.load(pipe_path, a.crs, a.dia_field, a.label_field, pats, a.default_dia)
        log('관로 선 %d개 · 묶음 %d개%s' % (len(lines), len(layers), (' · ' + note) if note else ''), fp)
        if not lines:
            log('  관로 없음 → 파일 · 도면층 이름 확인', fp)

    shots = []
    videos = []
    used_vids = set()
    t_start = time.time()
    terrain_path = os.path.join(out, 'data', 'terrain.json')
    try:
        with open(terrain_path, encoding='utf-8') as f:
            old_terrain = json.load(f)
    except Exception:
        old_terrain = []
    with ProcessPoolExecutor(max_workers=workers) as pool:
        for osv in osvs:
            name = os.path.splitext(os.path.basename(osv))[0]
            parts = name.split('_')
            vid = parts[2] if len(parts) >= 3 else name[-4:]
            if vid in used_vids:                       # 다른 날 · 다른 카드의 같은 번호
                vid = (parts[1][-6:] + '_' + parts[2]) if len(parts) >= 3 else name
            used_vids.add(vid)
            log('영상 %s 읽는 중' % name, fp)
            frames = read_meta(osv)
            frames = [f for f in frames if a.from_time <= f.time <= a.to_time]
            if not frames or not any(_has_gps(f) for f in frames):
                log('  GPS 없음 → 건너뜀', fp)
                continue
            off = yaw_offsets(frames)
            if off is None:
                log('  방향 기록 없음 → 건너뜀', fp)
                continue
            dt = float(np.median(np.diff([f.time for f in frames[:200]]))) if len(frames) > 2 else 1 / 30.0
            seek_back = SEEK_FRACTION * dt
            f0 = next(f for f in frames if _has_gps(f))
            gam = geo.convergence(f0.lat, f0.lon)
            en = track_en(frames, geo)
            R_list = [R_world_cam(f, off[i], rig, gam) if (f.q_body is not None and _has_gps(f)) else None
                      for i, f in enumerate(frames)]
            picks, s = select_shots(frames, en, a.interval)
            if a.max_shots:
                picks = picks[:a.max_shots]
            log('  장면 %d · 진행 %.0f m · 촬영점 %d' % (len(frames), s[-1], len(picks)), fp)
            if not picks:
                continue
            # 땅 높이
            samples = []
            key = None
            if not a.no_terrain:
                step_m = 20.0
                s_lo, s_hi = s[picks[0]] - step_m, s[picks[-1]] + step_m
                key = [name, round(a.from_time, 2), min(a.to_time, 1e6), step_m, round(s_lo), round(s_hi),
                       TERRAIN_VERSION, hashlib.md5(rig_sig.encode()).hexdigest()[:10], a.crs]
                hit = next((v for v in old_terrain if v.get('key') == key), None)
                if hit:
                    samples = [tuple(x) for x in hit['terrain']]
                    log('  땅 높이 = 지난번 잰 값 %d곳' % len(samples), fp)
                else:
                    sub = [i for i in range(len(frames)) if s_lo <= s[i] <= s_hi]
                    samples = estimate_terrain(osv, [frames[i] for i in sub], en[sub], s[sub], [R_list[i] for i in sub],
                                               rig, ffmpeg, pool, step_m, seek_back, fp)
            videos.append({'id': vid, 'name': name, 'key': key,
                           'terrain': [[round(x, 1), round(y, 2)] for x, y in samples]})
            for i in picks:
                f = frames[i]
                T = terrain_at(samples, s[i])
                agl = f.rel_alt - T if T is not None else f.rel_alt
                if agl < 1.0:
                    agl = f.rel_alt
                shots.append(dict(video=vid, osv=osv, frame=f.index, time=round(f.time, 3), seek=f.time - seek_back,
                                  lat=f.lat, lon=f.lon, E=float(en[i][0]), N=float(en[i][1]), rel=f.rel_alt,
                                  agl=round(agl, 2), T=T, yaw=round(f.raw_extra.get('a3', 0) / 10.0, 1), R=R_list[i]))

        # 땅 높이 기록 (이번에 안 돌린 영상 것도 남김)
        names_now = {v['name'] for v in videos}
        merged = [v for v in old_terrain if v.get('name') not in names_now] + videos
        _write_atomic(terrain_path, json.dumps(merged, ensure_ascii=False).encode('utf-8'))

        if not shots:
            log('촬영점 없음 → 끝', fp)
            return 1

        # 높이 기준 (이륙점 땅 높이 Z_home)
        if a.home_z is not None:
            zhome = a.home_z
            log('이륙점 높이 = %.2f (지정)' % zhome, fp)
        elif any(ln['hasZ'] for ln in lines):
            # 설계 높이가 있는 관(Civil 3D 3D 폴리선) → 땅 = 관 가운데 + 반지름 + 평균매설깊이
            from .pipes import nearest_on_lines
            zl = [dict(pts=[[p[0], p[1], p[2] + ln['dia'] / 2 + a.depth] for p in ln['pts']])
                  for ln in lines if ln['hasZ']]
            vals = []
            for sh in shots:
                d, z = nearest_on_lines(zl, sh['E'], sh['N'])
                if z is not None and d <= 20:
                    vals.append(z - (sh['T'] or 0.0))
            zhome = float(np.median(vals)) if vals else 0.0
            log('이륙점 높이 = %.2f (관로 %d곳에서 추정)' % (zhome, len(vals)) if vals else
                '이륙점 높이 = 0 (관로가 경로에서 멀어 추정 못 함 → --home-z)', fp)
        else:
            zhome = 0.0

        # 원점
        E0 = round(shots[0]['E'] / 100.0) * 100.0
        N0 = round(shots[0]['N'] / 100.0) * 100.0

        # 360 사진 (같은 장면 · 같은 자세 · 같은 설정으로 이미 만든 것은 다시 안 만듦)
        idx_path = os.path.join(out, 'data', 'pano_index.json')
        try:
            with open(idx_path, encoding='utf-8') as f:
                old_idx = json.load(f)
        except Exception:
            old_idx = {}
        new_idx = {}
        jobs = []
        for k, sh in enumerate(shots):
            sid = '%04d' % (k + 1)
            sh['id'] = sid
            pano_js = os.path.join(out, 'data', 'pano', sid + '.js')
            thumb = os.path.join(out, 'data', 'thumb', sid + '.jpg')
            rhash = hashlib.md5(np.round(sh['R'], 6).tobytes()).hexdigest()[:12]
            sig = [os.path.basename(sh['osv']), sh['frame'], a.width, a.quality, rig_sig, PANO_VERSION, rhash]
            new_idx[sid] = sig
            if old_idx.get(sid) == sig and os.path.exists(pano_js) and os.path.exists(thumb):
                continue
            jobs.append((sh['osv'], ffmpeg, dict(rig.__dict__), sh['seek'], sh['R'].tolist(), a.width, a.quality,
                         pano_js, thumb, sid))
        job_ids = {j[-1] for j in jobs}
        ok_ids = set(new_idx) - job_ids                       # 그대로 쓰는 것
        # 시작 전에 «지금 맞는 것만» 적어 둠 — 중간에 멈춰도 옛 표가 다른 장면을 가리키지 않게
        _write_atomic(idx_path, json.dumps({k: new_idx[k] for k in ok_ids}).encode('utf-8'))
        log('360 사진 %d장 만드는 중 (동시 %d · 그대로 씀 %d)' % (len(jobs), workers, len(ok_ids)), fp)
        done = 0
        total_b = 0
        failed = set()
        futs = {pool.submit(_pano_job, j): j[-1] for j in jobs}
        for fu in as_completed(futs):
            sid = futs[fu]
            try:
                _, nb = fu.result()
                ok_ids.add(sid)
                total_b += nb
            except Exception as ex:
                failed.add(sid)
                log('  사진 실패 %s → 뺌: %s' % (sid, str(ex)[:200]), fp)
            done += 1
            if done % max(5, len(jobs) // 40) == 0 or done == len(jobs):
                el = time.time() - t_start
                log('  %d / %d  (%.0f MB · %.0f초)' % (done, len(jobs), total_b / 1e6, el), fp)
                _write_atomic(idx_path, json.dumps({k: new_idx[k] for k in ok_ids}).encode('utf-8'))
        _write_atomic(idx_path, json.dumps({k: new_idx[k] for k in ok_ids}).encode('utf-8'))
        # 이번 목록에 없는 옛 파일 지우기
        for fn in os.listdir(os.path.join(out, 'data', 'pano')):
            sid = fn.split('.')[0]
            if fn.endswith('.tmp') or sid not in ok_ids:
                os.remove(os.path.join(out, 'data', 'pano', fn))
                tfn = os.path.join(out, 'data', 'thumb', sid + '.jpg')
                if os.path.exists(tfn):
                    os.remove(tfn)
        shots = [sh for sh in shots if sh['id'] not in failed]

    # project.js
    proj_shots = []
    for k, sh in enumerate(shots):
        z = zhome + sh['rel']
        g = z - sh['agl']
        proj_shots.append(dict(id=sh['id'], seq=k + 1, video=sh['video'], frame=sh['frame'], time=sh['time'],
                               lat=round(sh['lat'], 8), lon=round(sh['lon'], 8),
                               e=round(sh['E'] - E0, 3), n=round(sh['N'] - N0, 3),
                               z=round(z, 3), groundZ=round(g, 3), agl=sh['agl'], relAlt=round(sh['rel'], 2),
                               yaw=sh['yaw'], file='data/pano/%s.js' % sh['id'], thumb='data/thumb/%s.jpg' % sh['id']))
    basemap = {'type': 'vworld', 'key': a.vworld_key} if a.vworld_key else {'type': 'osm'}
    project = dict(version=1, title=a.title or os.path.basename(out), crs=a.crs, origin=dict(E=E0, N=N0),
                   depth=a.depth, cover=a.depth, homeZ=zhome, interval=a.interval, basemap=basemap,
                   videos=[{'id': v['id'], 'name': v['name']} for v in videos], shots=proj_shots)
    _write_atomic(os.path.join(out, 'data', 'project.js'),
                  ('window.RV_PROJECT = ' + json.dumps(project, ensure_ascii=False, indent=1) + ';\n').encode('utf-8'))

    # 관로 — 이번에 준 파일만 (안 주면 빈 파일: 원점이 바뀐 옛 관로가 엉뚱한 곳에 남지 않게)
    pl = []
    if lines:
        from . import pipeload
        sen = [(sh['E'], sh['N']) for sh in shots]
        sg = [ps['groundZ'] for ps in proj_shots]
        lines = pipeload.densify(lines, 5.0)
        n0 = len(lines)
        lines = pipeload.clip_near(lines, sen, 200.0)
        lines = pipeload.ground_for(lines, sen, sg)
        log('관로: 경로 200 m 안 조각 %d개 (원래 선 %d개)' % (len(lines), n0), fp)
        for ln in lines:
            pts = [[round(p[0] - E0, 3), round(p[1] - N0, 3), (round(p[2], 3) if (ln['hasZ'] and p[2] is not None) else None)]
                   for p in ln['pts']]
            ll = [[round(v, 8) for v in geo.en_to_ll(p[0], p[1])] for p in ln['pts']]
            pl.append(dict(layer=ln['layer'], label=ln['label'], dia=round(ln['dia'], 4), hasZ=bool(ln['hasZ']),
                           pts=pts, g=[round(v, 3) if v is not None else None for v in ln['g']], ll=ll))
    _write_atomic(os.path.join(out, 'data', 'pipes.js'),
                  ('window.RV_PIPES = ' + json.dumps({'layers': layers, 'lines': pl, 'origin': {'E': E0, 'N': N0}},
                                                    ensure_ascii=False) + ';\n').encode('utf-8'))
    calib_path = os.path.join(out, 'data', 'calib.js')
    if not os.path.exists(calib_path):                          # 맞춤 값은 있으면 절대 안 덮음
        _write_atomic(calib_path, 'window.RV_CALIB = {"videos": {}};\n'.encode('utf-8'))

    # 보기 화면 복사 (맞춤 값 calib.js 는 건드리지 않음)
    if os.path.isdir(VIEWER_DIR):
        os.makedirs(os.path.join(out, 'viewer'), exist_ok=True)
        for fn in ('app.js', 'style.css'):
            src = os.path.join(VIEWER_DIR, fn)
            if os.path.exists(src):
                shutil.copy2(src, os.path.join(out, 'viewer', fn))
        src = os.path.join(VIEWER_DIR, 'index.html')
        if os.path.exists(src):
            shutil.copy2(src, os.path.join(out, '로드뷰.html'))
            shutil.copy2(src, os.path.join(out, 'index.html'))      # 웹 공유 주소용
    log('끝 · 촬영점 %d · %.0f초 · %s' % (len(shots), time.time() - t_start, os.path.join(out, '로드뷰.html')), fp)
    fp.close()
    return 0


if __name__ == '__main__':
    sys.exit(main())
