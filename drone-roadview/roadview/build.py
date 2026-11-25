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
PANO_VERSION = 4     # 360 사진 만드는 법이 바뀌면 올림 → 옛 사진 다시 만듦 (3: 반 칸 치우침 · 4: 약한 선명화)
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


def Rz_heading(deg):
    """사진 촬영점의 «자세» 자리 채우기 (재사용 표시 지문용)."""
    c, s_ = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return np.array([[c, -s_, 0.0], [s_, c, 0.0], [0.0, 0.0, 1.0]])


def log(msg, fp=None):
    line = time.strftime('%H:%M:%S ') + msg
    print(line, flush=True)
    if fp:
        fp.write(line + '\n')
        fp.flush()


class Eta:
    """남은 시간 — 앱이 읽는 줄 «@진행 <퍼센트> <남은 초 | -1> <글>» (기록 파일에는 안 씀).

    단계마다 잰 빠르기(한 곳 · 한 장에 걸린 벽시계 초)로 지금 단계를 셈하고,
    아직 안 한 뒤 단계(360 사진 · 안 읽은 영상)는 잰 값에서 미루어 셈한다 (1124 JACK).
    """
    PANO_PER_HEIGHT = 0.95    # 360 사진 한 장 ≈ 땅 높이 한 곳 × 0.95 (1124 0012 실측 0.87 / 0.90 초 · 동시 6)
    PANO_PRIOR = 5.2          # 아무것도 못 쟀을 때 한 장 · 일꾼 하나 (초 · 8192)
    PHOTO_PRIOR = 1.5         # 사진 모드 JPG 한 장 · 일꾼 하나
    TAIL = 5.0                # 마무리 (project.js · 높이 타일 · 보기 화면)

    def __init__(self, workers, width, osv_bytes, n_jpg, h_per_pick):
        self.t0 = time.time()
        self.last = 0.0
        self.workers = max(1, workers)
        self.wscale = 0.5 + 0.5 * (width / 8192.0) ** 2   # 반은 풀기(크기 무관) · 반은 꿰매기(넓이 비례)
        self.bytes_total = float(osv_bytes)
        self.bytes_done = 0.0
        self.read_s = 0.0          # 영상 기록 읽기에 쓴 초
        self.picks = 0             # 지금까지 고른 촬영점 (영상)
        self.hjobs = 0             # 지금까지 잰 땅 높이 곳 수
        self.h_per_pick = h_per_pick
        self.n_jpg = n_jpg
        self.h_rate = None         # 땅 높이 한 곳 (벽시계 초)
        self.p_rate = None         # 360 사진 한 장 (벽시계 초)
        self._phase, self._marks = None, []

    def pano_prior(self):
        if self.p_rate:
            return self.p_rate
        if self.h_rate:
            return self.PANO_PER_HEIGHT * self.h_rate * self.wscale
        return self.PANO_PRIOR / self.workers * self.wscale

    def video_read(self, nbytes, read_s, picks):
        self.bytes_done += nbytes
        self.read_s += read_s
        self.picks += picks

    def later(self, pano_left=None, unread=True):
        """지금 단계 뒤에 남은 초. pano_left = 남은 360 사진 수 (모르면 고른 촬영점 + 미룬 값)."""
        s = self.TAIL
        left_b = max(0.0, self.bytes_total - self.bytes_done) if unread else 0.0
        est_picks = 0.0
        if left_b > 0 and self.bytes_done > 0:
            est_picks = self.picks / self.bytes_done * left_b
            s += self.read_s / self.bytes_done * left_b
            hpp = self.hjobs / float(self.picks) if (self.hjobs and self.picks) else self.h_per_pick
            s += est_picks * hpp * (self.h_rate or self.pano_prior() / self.PANO_PER_HEIGHT)
        if pano_left is None:
            pano_left = self.picks + est_picks
            s += self.n_jpg * self.PHOTO_PRIOR / self.workers
        return s + pano_left * self.pano_prior()

    def known(self):
        return self.bytes_done > 0 or self.bytes_total == 0

    def show(self, label, remain, force=False):
        now = time.time()
        if not force and now - self.last < 2.0:
            return
        self.last = now
        el = now - self.t0
        if remain is None or not self.known():
            pct, r = 1.0, -1
        else:
            pct, r = 100.0 * el / max(1e-6, el + remain), int(remain)
        print('@진행 %.1f %d %s' % (min(pct, 99.0), r, label), flush=True)

    def step(self, t_phase, done, prior):
        """단계 안 빠르기 (하나 끝날 때마다 부름).

        처음 (일꾼 수)개는 일꾼 띄우기 · 첫 읽기가 섞여 느리게 보인다 (1124 실측: 0.9 초 → 2.1 초로 보임)
        → 그동안은 미룬 값, 그 뒤 최근 (일꾼 × 4)개 사이 간격으로 잰다 (첫 묶음은 빼고).
        """
        now = time.time()
        if self._phase != t_phase:
            self._phase, self._marks = t_phase, []
        self._marks.append(now)
        W = self.workers
        n = len(self._marks)
        if n <= 2 * W:
            return prior
        ref = max(W - 1, n - 1 - 4 * W)          # 기준 = 첫 묶음 끝 (또는 창 시작)
        return (now - self._marks[ref]) / float(n - 1 - ref)


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


def estimate_terrain(osv, frames, en, s, R_list, rig, ffmpeg, pool, step_m, seek_back, fp, eta=None):
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
    t_h = time.time()
    if eta:
        eta.hjobs += len(jobs)
        prior = eta.h_rate or eta.pano_prior() / Eta.PANO_PER_HEIGHT
        eta.show('땅 높이 재기 0 / %d' % len(jobs), len(jobs) * prior + eta.later(), force=True)
    for k, fu in enumerate(as_completed(futs), 1):
        i = futs[fu]
        if eta:
            eta.h_rate = eta.step(t_h, k, prior)
            eta.show('땅 높이 재기 %d / %d' % (k, len(jobs)), (len(jobs) - k) * eta.h_rate + eta.later(), k == len(jobs))
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


def terrain_profile(samples, near=150.0, lam=100.0, step=10.0):
    """잰 땅 높이 차 [(s, T)] → 이어진 함수 T(s) · 잰 곳이 없으면 None.

    옛 방식(±75 m 안 중앙값 · 2곳 미만이면 영상 전체 중앙값)은 계단처럼 튀었다 (1124 JACK «관이 뚝 떨어짐»):
    0012 에서 재지 못한 246 m 구간 · 영상 끝이 전체 중앙값(3.8 m)으로 떨어져 땅 위 높이 19 m → 59 m (화면 속 거리 3배),
    튀는 값 둘뿐인 곳은 평균 −61 m. 그래서
      ① 이웃(150 m 안, 적어도 가까운 6곳)과 max(3 m, 3·MAD) 넘게 다른 값은 뺀다 (세 번까지 되풀이)
      ② 10 m 격자 위 부드러운 곡선 (Whittaker: 잰 값과의 차² + λ·굽음² 최소) — 빈 구간은 곧게 이어지고
         오르막 → 평지에서 넘치지 않는다 (국소 직선 맞춤은 0012 끝에서 3 m 넘침 · λ 100 = 5 m 마다 최대 0.6 m)
      ③ 잰 구간 밖은 끝 값 그대로 (멀리 늘이지 않음)
    """
    if not samples:
        return None
    ss = np.array([a for a, _ in samples], float)
    ts = np.array([b for _, b in samples], float)
    o = np.argsort(ss)
    ss, ts = ss[o], ts[o]
    keep = np.isfinite(ts)
    for _ in range(3):
        bad = np.zeros(len(ss), bool)
        for i in np.where(keep)[0]:
            idx = np.where(keep)[0]
            idx = idx[idx != i]
            if len(idx) < 3:
                break
            d = np.abs(ss[idx] - ss[i])
            nb = idx[d <= near]
            if len(nb) < 6:
                nb = idx[np.argsort(d)[:6]]
            v = ts[nb]
            med = float(np.median(v))
            mad = 1.4826 * float(np.median(np.abs(v - med)))
            if abs(ts[i] - med) > max(3.0, 3.0 * mad):
                bad[i] = True
        if not bad.any():
            break
        keep &= ~bad
    sk, tk = ss[keep], ts[keep]
    if len(sk) == 0:
        return None
    lo, hi = float(sk[0]), float(sk[-1])
    n = int(np.ceil((hi - lo) / step)) + 1
    if len(sk) < 3 or n < 3:
        z, g = np.full(2, float(np.median(tk))), np.array([lo, hi + 1e-6])
    else:
        g = lo + np.arange(n) * step
        u = (sk - lo) / step
        i0 = np.clip(np.floor(u).astype(int), 0, n - 2)
        t = u - i0
        A = np.zeros((n, n))
        rhs = np.zeros(n)
        for a, w in ((i0, 1.0 - t), (i0 + 1, t)):      # Bᵀ B · Bᵀ T (겹선형 몫)
            np.add.at(rhs, a, w * tk)
            for b, v in ((i0, 1.0 - t), (i0 + 1, t)):
                np.add.at(A, (a, b), w * v)
        D = np.diff(np.eye(n), 2, axis=0)
        A += lam * (D.T @ D) + 1e-9 * np.eye(n)
        z = np.linalg.solve(A, rhs)

    def f(s):
        return float(np.interp(min(max(float(s), lo), hi), g, z))

    f.kept, f.dropped = int(keep.sum()), int((~keep).sum())
    return f


# ---------------------------------------------------------------- 360 사진

def _pano_job(args):
    osv, ffmpeg, rig_dict, t, R, width, quality, pano_js, thumb_jpg, sid = args
    from PIL import Image
    try:
        import cv2
        cv2.setNumThreads(2)               # 일꾼 여럿이 스레드를 서로 다투지 않게
    except ImportError:
        pass
    from .camera import sharpen
    if isinstance(osv, dict):                 # 사진 모드 JPG (카메라가 이미 꿰맴)
        from .photos import to_grid_north
        pano = np.ascontiguousarray(to_grid_north(osv['path'], osv['heading'], osv['gamma'], width))
    else:
        from .camera import Rig, stitch
        rig = Rig(**rig_dict)
        a, b = extract_pair(ffmpeg, osv, t)
        pano = stitch(a, b, rig, np.array(R), W=width)
    # 약한 선명화 — 겹선형 다시 뽑기로 무뎌진 가장자리만 살린다 (JACK 1123 화질 · 비교 그림으로 확인)
    pano = sharpen(pano, radius=1.2, percent=60, threshold=2)
    im = Image.fromarray(pano)
    buf = io.BytesIO()
    im.save(buf, 'JPEG', quality=quality, optimize=True)
    b64 = base64.b64encode(buf.getvalue()).decode('ascii')
    _write_atomic(pano_js, ('RV_PANO_LOADED("%s","data:image/jpeg;base64,%s");\n' % (sid, b64)).encode('ascii'))
    tb = io.BytesIO()
    try:
        import cv2
        small = Image.fromarray(cv2.resize(pano, (512, 256), interpolation=cv2.INTER_AREA))
    except ImportError:
        small = im.resize((512, 256), Image.LANCZOS)
    small.save(tb, 'JPEG', quality=80)
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
    ap.add_argument('--width', type=int, default=8192, help='360 사진 가로 화소 (기본 8192 ≈ 원본 렌즈 세밀함)')
    ap.add_argument('--quality', type=int, default=92)
    ap.add_argument('--vworld-key', help='VWorld 지도 키 (없으면 DH.Grading 키 — 내 키 → 설치 키)')
    ap.add_argument('--no-vworld', action='store_true', help='위성 지도 키를 넣지 않음 (OSM)')
    ap.add_argument('--ffmpeg', help='ffmpeg.exe 경로')
    ap.add_argument('--title', help='제목')
    ap.add_argument('--workers', type=int, default=0, help='동시 작업 수 (기본 자동)')
    ap.add_argument('--max-shots', type=int, default=0, help='영상마다 최대 촬영점 수 (시험용)')
    ap.add_argument('--from-time', type=float, default=0.0, help='시험용: 이 초부터')
    ap.add_argument('--to-time', type=float, default=1e9, help='시험용: 이 초까지')
    ap.add_argument('--keep', action='append', default=[], metavar='영상이름=시작초:끝초',
                    help='쓸 구간 (여러 번 가능 · 같은 영상에 여러 토막) — 프로그램 편집 화면에서 자르고 남긴 것. 없으면 영상 전체')
    ap.add_argument('--no-terrain', action='store_true', help='땅 높이 자동 재기 끄기 (이륙점 높이 그대로)')
    ap.add_argument('--terrain', choices=['auto', 'db', 'none'], default='auto',
                    help='수치지도 표고: auto = 사내 DB 되면 씀 · db · none')
    ap.add_argument('--ngii', nargs='+', help='수치지도 DXF 파일 (주면 DB 대신 이것)')
    ap.add_argument('--terrain-mode', choices=['datum', 'surface'], default='surface',
                    help='surface = 땅 높이는 점마다 수치지도 DEM (기본 · 1124 JACK: 평면 설계용 — 관 = DEM − 평균매설깊이) · '
                         'datum = 수치지도로 표고 기준만 (촬영점 사이 높낮이는 영상 값)')
    ap.add_argument('--overwrite', action='store_true', help='다른 영상으로 만든 폴더라도 덮어 만듦')
    ap.add_argument('--normal-priority', action='store_true', help='보통 우선순위로 (기본은 낮음 — PC 쓰면서 돌려도 안 버벅이게)')
    a = ap.parse_args(argv)
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass
    if not a.normal_priority:
        _low_priority()
    keep = {}
    for k in a.keep:
        name, _, rng = str(k).rpartition('=')
        try:
            t0, t1 = (float(x) for x in rng.split(':'))
        except ValueError:
            print('--keep 형식: 영상이름=시작초:끝초 (%s)' % k)
            return 2
        if name and t1 > t0:
            keep.setdefault(name, []).append((t0, t1))
    for v in keep.values():
        v.sort()

    osvs = []
    jpgs = []
    for p in a.inputs:
        if os.path.isdir(p):
            osvs += sorted(glob.glob(os.path.join(p, '*.OSV')) + glob.glob(os.path.join(p, '*.osv')))
            jpgs += sorted(glob.glob(os.path.join(p, '*.JPG')) + glob.glob(os.path.join(p, '*.jpg')))
        elif os.path.isfile(p):
            (jpgs if p.lower().endswith(('.jpg', '.jpeg')) else osvs).append(p)
    seen = set()
    uniq = []
    for p in sorted(os.path.abspath(p) for p in osvs):
        k = os.path.normcase(p)
        if k not in seen:
            seen.add(k)
            uniq.append(p)
    osvs = [p for p in uniq if not p.lower().endswith(('.jpg', '.jpeg'))]
    jpgs = sorted({os.path.normcase(os.path.abspath(p)): os.path.abspath(p) for p in jpgs}.values())
    if not osvs and not jpgs:
        print('OSV · 360 사진 없음 → 영상 파일이나 폴더를 주세요')
        return 2

    out = os.path.abspath(a.out)
    # 다른 영상으로 만든 로드뷰 폴더면 덮지 않는다 (옛 사진을 지우게 되므로 · 1123)
    try:
        oldp = json.loads(open(os.path.join(out, 'data', 'project.js'), encoding='utf-8').read()
                          .split('=', 1)[1].strip().rstrip(';'))
        old_names = {v.get('name') for v in oldp.get('videos', [])}
        new_names = {os.path.splitext(os.path.basename(p))[0] for p in osvs}
        if jpgs:
            new_names |= {n for n in old_names if str(n).startswith('360 사진')}
        # 겹치는 영상이 하나라도 있으면 같은 프로젝트 (편집 화면에서 영상을 빼거나 더해 다시 만들기 · 1125)
        if old_names and not (old_names & new_names) and not a.overwrite:
            print('이 폴더에는 다른 영상으로 만든 로드뷰가 있음 → 다른 결과 폴더를 고르세요 (%s)' % out)
            return 3
    except (OSError, ValueError, KeyError, IndexError):
        pass
    os.makedirs(os.path.join(out, 'data', 'pano'), exist_ok=True)
    os.makedirs(os.path.join(out, 'data', 'thumb'), exist_ok=True)
    fp = open(os.path.join(out, '만들기_기록.txt'), 'a', encoding='utf-8')
    log('시작 · 영상 %d개%s · 간격 %.1f m · 폭 %d' % (len(osvs), (' · JPG %d장' % len(jpgs)) if jpgs else '',
                                                 a.interval, a.width), fp)

    ffmpeg = find_ffmpeg(a.ffmpeg)
    geo = Geo(a.crs)
    rig = default_rig()
    rig_sig = json.dumps(rig.__dict__, sort_keys=True)
    workers = a.workers or max(2, min(6, (os.cpu_count() or 4) // 3))
    eta = Eta(workers, a.width, sum(os.path.getsize(p) for p in osvs), len(jpgs),
              0.0 if a.no_terrain else a.interval / 20.0)
    eta.show('시작', None, force=True)

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
    from .pipes_update import load_trims
    trims = load_trims(out)
    with ProcessPoolExecutor(max_workers=workers) as pool:
        for osv in osvs:
            name = os.path.splitext(os.path.basename(osv))[0]
            parts = name.split('_')
            vid = parts[2] if len(parts) >= 3 else name[-4:]
            if vid in used_vids:                       # 다른 날 · 다른 카드의 같은 번호
                vid = (parts[1][-6:] + '_' + parts[2]) if len(parts) >= 3 else name
            used_vids.add(vid)
            log('영상 %s 읽는 중' % name, fp)
            eta.show('영상 읽는 중', eta.later() if eta.known() else None, force=True)
            t_read = time.time()
            nbytes = os.path.getsize(osv)
            frames = read_meta(osv)
            frames = [f for f in frames if a.from_time <= f.time <= a.to_time]
            segs = keep.get(name)
            if segs:                                    # 편집 화면에서 남긴 토막만 (1125)
                frames = [f for f in frames if any(t0 <= f.time <= t1 for t0, t1 in segs)]
                log('  쓸 구간 %d토막 · %.0f초' % (len(segs), sum(t1 - t0 for t0, t1 in segs)), fp)
            if not frames or not any(_has_gps(f) for f in frames):
                log('  GPS 없음 → 건너뜀', fp)
                eta.video_read(nbytes, time.time() - t_read, 0)
                continue
            off = yaw_offsets(frames)
            if off is None:
                log('  방향 기록 없음 → 건너뜀', fp)
                eta.video_read(nbytes, time.time() - t_read, 0)
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
            picks_all = picks                       # 땅 높이 범위는 자르기 전 것 (지난번 잰 값을 그대로 쓰게)
            cut = [r for r in trims if r['video'] == name]
            if cut:                                 # 위성지도에서 지운 구간 (data/trim.json · 1124)
                picks = [i for i in picks if not any(r['t0'] <= frames[i].time <= r['t1'] for r in cut)]
                log('  지운 구간 %d곳 → 촬영점 %d 뺌' % (len(cut), len(picks_all) - len(picks)), fp)
            log('  장면 %d · 진행 %.0f m · 촬영점 %d' % (len(frames), s[-1], len(picks)), fp)
            eta.video_read(nbytes, time.time() - t_read, len(picks))
            if not picks:
                continue
            # 땅 높이
            samples = []
            key = None
            if not a.no_terrain:
                step_m = 20.0
                s_lo, s_hi = s[picks_all[0]] - step_m, s[picks_all[-1]] + step_m
                key = [name, round(a.from_time, 2), min(a.to_time, 1e6), step_m, round(s_lo), round(s_hi),
                       TERRAIN_VERSION, hashlib.md5(rig_sig.encode()).hexdigest()[:10], a.crs]
                if segs:
                    key.append([[round(t0, 2), round(t1, 2)] for t0, t1 in segs])
                hit = next((v for v in old_terrain if v.get('key') == key), None)
                if hit:
                    samples = [tuple(x) for x in hit['terrain']]
                    log('  땅 높이 = 지난번 잰 값 %d곳' % len(samples), fp)
                else:
                    sub = [i for i in range(len(frames)) if s_lo <= s[i] <= s_hi]
                    samples = estimate_terrain(osv, [frames[i] for i in sub], en[sub], s[sub], [R_list[i] for i in sub],
                                               rig, ffmpeg, pool, step_m, seek_back, fp, eta=eta)
            videos.append({'id': vid, 'name': name, 'key': key,
                           'terrain': [[round(x, 1), round(y, 2)] for x, y in samples]})
            if segs:
                videos[-1]['keep'] = [[round(t0, 3), round(t1, 3)] for t0, t1 in segs]
            tprof = terrain_profile(samples)
            if tprof is not None and tprof.dropped:
                log('  땅 높이 튀는 값 %d곳 뺌 · %d곳으로 이음' % (tprof.dropped, tprof.kept), fp)
            for i in picks:
                f = frames[i]
                T = tprof(s[i]) if tprof is not None else None
                agl = f.rel_alt - T if T is not None else f.rel_alt
                if agl < 1.0:
                    agl = f.rel_alt
                shots.append(dict(video=vid, osv=osv, frame=f.index, time=round(f.time, 3), seek=f.time - seek_back,
                                  lat=f.lat, lon=f.lon, E=float(en[i][0]), N=float(en[i][1]), rel=f.rel_alt,
                                  agl=round(agl, 2), T=T, yaw=round(f.raw_extra.get('a3', 0) / 10.0, 1), R=R_list[i]))

        # 사진 모드 JPG — 촬영 시각 순 · 간격 거르기 (카메라가 이미 꿰맨 360 사진)
        if jpgs:
            from .photos import read_photo
            ph = [r for r in (read_photo(p) for p in jpgs) if r]
            ph.sort(key=lambda r: r['time'])
            log('360 사진 %d장 중 위치 있는 등장방형 %d장' % (len(jpgs), len(ph)), fp)
            if ph:
                vid = 'P'
                gam = geo.convergence(ph[0]['lat'], ph[0]['lon'])
                last = None
                kept = 0
                for i, r in enumerate(ph):
                    E, N = geo.ll_to_en(r['lat'], r['lon'])
                    if last is not None and math.hypot(E - last[0], N - last[1]) < a.interval:
                        continue
                    last = (E, N)
                    kept += 1
                    shots.append(dict(video=vid, osv={'path': r['path'], 'heading': r['heading'], 'gamma': gam},
                                      frame=i, time=round(r['time'] - ph[0]['time'], 3), seek=0.0,
                                      lat=r['lat'], lon=r['lon'], E=E, N=N, rel=r['rel'], agl=round(max(r['rel'], 1.0), 2),
                                      T=None, yaw=round(r['heading'], 1),
                                      R=Rz_heading(r['heading'])))
                videos.append({'id': vid, 'name': '360 사진 (%s)' % os.path.basename(os.path.dirname(ph[0]['path'])),
                               'key': None, 'terrain': []})
                log('  사진 촬영점 %d · 방향 출처 %s' % (kept, ph[0]['heading_src']), fp)

        # 땅 높이 기록 (이번에 안 돌린 영상 것도 남김)
        names_now = {v['name'] for v in videos}
        merged = [v for v in old_terrain if v.get('name') not in names_now] + videos
        _write_atomic(terrain_path, json.dumps(merged, ensure_ascii=False).encode('utf-8'))

        if not shots:
            log('촬영점 없음 → 끝', fp)
            return 1

        # 수치지도 땅 높이 (있으면 촬영점 · 관로 땅 높이를 이것으로 — 표고 EL)
        dem = None
        dem_note = ''
        eta.show('수치지도 높이', eta.later(len(shots)), force=True)
        if a.terrain != 'none':
            from . import terrain
            es = np.array([sh['E'] for sh in shots]); ns = np.array([sh['N'] for sh in shots])
            bbox = (es.min() - 300, ns.min() - 300, es.max() + 300, ns.max() + 300)
            try:
                if a.ngii:
                    pts = terrain.points_from_ngii_dxf(a.ngii, bbox)
                    dem_note = '수치지도 DXF %d장' % len(a.ngii)
                elif a.terrain in ('auto', 'db') and terrain.db_available():
                    epsg = int(a.crs.split(':')[1])
                    cl = terrain.contours_from_db(bbox, epsg)
                    sp = terrain.spot_heights_from_db(bbox, epsg)
                    pts = terrain.lines_to_points(cl, 2.0)
                    if len(sp):
                        pts = np.vstack([pts, sp])
                    dem_note = '사내 수치지도 DB 등고선 %d개 · 표고점 %d개' % (len(cl), len(sp))
                else:
                    pts = None
                    if a.terrain == 'db':
                        log('수치지도: 사내 DB 접속 정보 없음 → 안 씀', fp)
                if pts is not None and len(pts) >= 3:
                    # 등고선 삼각망 그대로는 계단 → 5 m 격자 DEM · σ 15 m 로 고르게 (1124 JACK «DEM 으로»)
                    dem = terrain.GridDem(pts, cell=5.0, sigma=15.0)
                    dem_note += ' → 5 m DEM'
                    log('수치지도 땅 높이: %s · 점 %d' % (dem_note, dem.n), fp)
                elif pts is not None:
                    log('수치지도: 이 범위에 높이 자료 없음 → 안 씀', fp)
            except Exception as ex:
                log('수치지도 못 씀 → 영상 높이만: %s' % str(ex)[:160], fp)
                dem = None
        if dem is not None:
            dg = dem([sh['E'] for sh in shots], [sh['N'] for sh in shots])
            n_ok = 0
            for sh, g in zip(shots, dg):
                sh['dem'] = float(g) if np.isfinite(g) else None
                n_ok += sh['dem'] is not None
            log('  촬영점 %d / %d 에 표고' % (n_ok, len(shots)), fp)

        # 높이 기준 (이륙점 땅 높이 Z_home) — 수치지도 표고가 없는 촬영점에만 쓰임
        zhome_v = {}                 # 영상마다 이륙한 곳이 다르다 → 이륙점 높이도 영상마다 (1123)
        if dem is not None and any(sh.get('dem') is not None for sh in shots):
            vals = [sh['dem'] - (sh['T'] or 0.0) for sh in shots if sh.get('dem') is not None]
            zhome = float(np.median(vals))
            for vid in {sh['video'] for sh in shots}:
                vv = [sh['dem'] - (sh['T'] or 0.0) for sh in shots if sh['video'] == vid and sh.get('dem') is not None]
                if vv:
                    zhome_v[vid] = float(np.median(vv))
            log('이륙점 높이 (수치지도에서 추정 · %s): %s' % (
                '표고 기준만' if a.terrain_mode == 'datum' else '지형 그대로',
                ' · '.join('%s %.1f' % (k, v) for k, v in sorted(zhome_v.items()))), fp)
            if a.terrain_mode == 'datum':
                # 5 m 등고선 삼각망은 평평한 도로에도 가짜 경사(0.5 m 쯤)를 만든다 (1123 0014 실측) →
                # 표고 기준(EL)만 수치지도로 맞추고 촬영점 사이 높낮이는 영상으로 잰 값을 쓴다
                for sh in shots:
                    if sh.get('T') is not None:      # 영상으로 땅 위 높이를 잰 촬영점만 (사진은 수치지도 그대로)
                        sh['dem'] = None
        elif a.home_z is not None:
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

        # 원점 — 같은 폴더를 다시 만들면 예전 원점을 그대로 (점 찍어 맞추기 짝 · 맞춤 값이 원점 기준이라)
        E0 = round(shots[0]['E'] / 100.0) * 100.0
        N0 = round(shots[0]['N'] / 100.0) * 100.0
        try:
            old = json.loads(open(os.path.join(out, 'data', 'project.js'), encoding='utf-8').read()
                             .split('=', 1)[1].strip().rstrip(';'))
            oE, oN = float(old['origin']['E']), float(old['origin']['N'])
            if old.get('crs') == a.crs and math.hypot(oE - E0, oN - N0) < 50000:
                E0, N0 = oE, oN
        except Exception:
            pass

        # 360 사진 (같은 장면 · 같은 자세 · 같은 설정으로 이미 만든 것은 다시 안 만듦)
        idx_path = os.path.join(out, 'data', 'pano_index.json')
        try:
            with open(idx_path, encoding='utf-8') as f:
                old_idx = json.load(f)
        except Exception:
            old_idx = {}
        new_idx = {}
        jobs = []
        pdir, tdir = os.path.join(out, 'data', 'pano'), os.path.join(out, 'data', 'thumb')
        # 사진 번호는 바꾸지 않는다 (1124): 같은 장면(sig)이면 옛 번호 그대로 — 앞 구간을 지워도 번호가 밀리지 않아
        # 점 찍어 맞추기 짝(사진 번호로 기억) · 사진 파일이 그대로 맞는다. 새 장면만 비어 있는 번호를 받는다.
        sigs = []
        for sh in shots:
            rhash = hashlib.md5(np.round(sh['R'], 6).tobytes()).hexdigest()[:12]
            src = sh['osv']['path'] if isinstance(sh['osv'], dict) else sh['osv']
            sigs.append([os.path.basename(src), sh['frame'], a.width, a.quality, rig_sig, PANO_VERSION, rhash])
        old_by_sig = {json.dumps(v): k for k, v in old_idx.items()}
        keep_id = [old_by_sig.get(json.dumps(sg)) for sg in sigs]
        taken = {i for i in keep_id if i}
        free = (('%04d' % n) for n in range(1, 10 ** 7) if '%04d' % n not in taken)
        for k, sh in enumerate(shots):
            sid = keep_id[k] or next(free)
            taken.add(sid)
            sh['id'] = sid
            pano_js = os.path.join(pdir, sid + '.js')
            thumb = os.path.join(tdir, sid + '.jpg')
            sig = sigs[k]
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
        t_p = time.time()
        n_photo = sum(1 for j in jobs if isinstance(j[0], dict))
        p_prior = ((len(jobs) - n_photo) * eta.pano_prior() + n_photo * Eta.PHOTO_PRIOR / workers) / max(1, len(jobs))
        eta.show('360 사진 0 / %d' % len(jobs), len(jobs) * p_prior + Eta.TAIL, force=True)
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
            eta.p_rate = eta.step(t_p, done, p_prior)
            eta.show('360 사진 %d / %d' % (done, len(jobs)), (len(jobs) - done) * eta.p_rate + Eta.TAIL,
                     done == len(jobs))
            if done % max(5, len(jobs) // 40) == 0 or done == len(jobs):
                el = time.time() - t_start
                log('  %d / %d  (%.0f MB · %.0f초)' % (done, len(jobs), total_b / 1e6, el), fp)
                _write_atomic(idx_path, json.dumps({k: new_idx[k] for k in ok_ids}).encode('utf-8'))
        _write_atomic(idx_path, json.dumps({k: new_idx[k] for k in ok_ids}).encode('utf-8'))
        # 이번 목록에 없는 옛 파일 지우기
        for fn in os.listdir(os.path.join(out, 'data', 'pano')):
            sid = fn.split('.')[0]
            if fn.endswith(('.tmp', '.mv')):              # 만들다 / 옮기다 만 것 (그 번호 작은 그림은 살림)
                os.remove(os.path.join(out, 'data', 'pano', fn))
                continue
            if sid not in ok_ids:
                os.remove(os.path.join(out, 'data', 'pano', fn))
                tfn = os.path.join(out, 'data', 'thumb', sid + '.jpg')
                if os.path.exists(tfn):
                    os.remove(tfn)
        shots = [sh for sh in shots if sh['id'] not in failed]
        eta.show('마무리', Eta.TAIL, force=True)

    # project.js
    proj_shots = []
    for k, sh in enumerate(shots):
        if sh.get('dem') is not None:          # 수치지도 표고 + 영상으로 잰 땅 위 높이
            g = sh['dem']
            z = g + sh['agl']
        else:
            z = zhome_v.get(sh['video'], zhome) + sh['rel']
            g = z - sh['agl']
        proj_shots.append(dict(id=sh['id'], seq=k + 1, video=sh['video'], frame=sh['frame'], time=sh['time'],
                               lat=round(sh['lat'], 8), lon=round(sh['lon'], 8),
                               e=round(sh['E'] - E0, 3), n=round(sh['N'] - N0, 3),
                               z=round(z, 3), groundZ=round(g, 3), agl=sh['agl'], relAlt=round(sh['rel'], 2),
                               yaw=sh['yaw'], file='data/pano/%s.js' % sh['id'], thumb='data/thumb/%s.jpg' % sh['id']))
    # 위성 지도 키: 그레이딩과 같은 키 (JACK 1123) — 파일로 열어도 위성이 나온다 (출처 없이도 VWorld 응답 확인)
    vkey, vsrc = (a.vworld_key, '지정') if a.vworld_key else ('', '')
    if not vkey and not a.no_vworld:
        from .keys import vworld_key
        vkey, vsrc = vworld_key()
    basemap = {'type': 'vworld', 'key': vkey} if vkey else {'type': 'osm'}
    log('바탕 지도: %s' % ('VWorld 위성 (%s)' % vsrc if vkey else 'OSM (VWorld 키 없음)'), fp)
    project = dict(version=1, title=a.title or os.path.basename(out), crs=a.crs, origin=dict(E=E0, N=N0),
                   depth=a.depth, cover=a.depth, homeZ=zhome, homeZByVideo=zhome_v, interval=a.interval, basemap=basemap,
                   terrain=(dem_note if dem is not None else '영상으로 잰 높이 (표고 아님)'),
                   terrainMode=(a.terrain_mode if dem is not None else 'video'),
                   videos=[{'id': v['id'], 'name': v['name']} for v in videos], shots=proj_shots)
    _write_atomic(os.path.join(out, 'data', 'project.js'),
                  ('window.RV_PROJECT = ' + json.dumps(project, ensure_ascii=False, indent=1) + ';\n').encode('utf-8'))

    # 관로 — 이번에 준 파일. 안 주면 있던 관로(관로 넣기 · 고친 것)를 그대로 두고 땅 높이만 새로 (1124).
    # 원점이 바뀌어 옛 관로가 엉뚱한 곳에 그려질 때만 빈 파일.
    from .pipes_update import reground
    surf = dem if (dem is not None and a.terrain_mode == 'surface') else None
    kept = None if lines else reground(out, 'pipes.js', 'RV_PIPES', surf)
    if kept:
        log('관로: 있던 %d줄 그대로 · 땅 높이만 새로' % kept, fp)
    n_ob = reground(out, 'obstacles.js', 'RV_OBST', surf)
    if n_ob:
        log('지장물: 있던 %d줄 그대로 · 땅 높이만 새로' % n_ob, fp)
    elif n_ob is None:
        os.remove(os.path.join(out, 'data', 'obstacles.js'))
        log('지장물: 원점이 바뀌어 뺌 → 다시 넣기', fp)
    pl = []
    if lines:
        from . import pipeload
        sen = [(sh['E'], sh['N']) for sh in shots]
        sg = [ps['groundZ'] for ps in proj_shots]
        lines = pipeload.densify(lines, 5.0)
        n0 = len(lines)
        lines = pipeload.clip_near(lines, sen, 200.0)
        lines = pipeload.ground_for(lines, sen, sg)
        if dem is not None and a.terrain_mode == 'surface':   # 점마다 수치지도 높이
            for ln in lines:
                dz = dem([p[0] for p in ln['pts']], [p[1] for p in ln['pts']])
                ln['g'] = [float(v) if np.isfinite(v) else g for v, g in zip(dz, ln['g'])]
        log('관로: 경로 200 m 안 조각 %d개 (원래 선 %d개)' % (len(lines), n0), fp)
        for ln in lines:
            pts = [[round(p[0] - E0, 3), round(p[1] - N0, 3), (round(p[2], 3) if (ln['hasZ'] and p[2] is not None) else None)]
                   for p in ln['pts']]
            ll = [[round(v, 8) for v in geo.en_to_ll(p[0], p[1])] for p in ln['pts']]
            pl.append(dict(layer=ln['layer'], label=ln['label'], dia=round(ln['dia'], 4), hasZ=bool(ln['hasZ']),
                           pts=pts, g=[round(v, 3) if v is not None else None for v in ln['g']], ll=ll,
                           ctrl=ln.get('ctrl') or list(range(len(pts)))))
    if not kept:
        _write_atomic(os.path.join(out, 'data', 'pipes.js'),
                      ('window.RV_PIPES = ' + json.dumps({'layers': layers, 'lines': pl, 'origin': {'E': E0, 'N': N0}},
                                                        ensure_ascii=False) + ';\n').encode('utf-8'))
    # 보기 화면용 높이 타일 (점 찍어 맞추기 · 지도 점의 땅 높이)
    dem_path = os.path.join(out, 'data', 'dem.js')
    if dem is not None:
        from . import terrain, pipeload
        sen = [(sh['E'], sh['N']) for sh in shots]
        # datum: 관로 · 촬영점과 같은 규칙의 땅 높이 (촬영점 땅 높이로) — 점 찍어 맞추기가 관로와 어긋나지 않게
        gfun = dem if a.terrain_mode == 'surface' else pipeload.ShotGround(sen, [ps['groundZ'] for ps in proj_shots])
        dj = terrain.dem_tiles(gfun, sen, (E0, N0))
        if dj and dj['tiles']:
            dj['source'] = dem_note + (' · 표고 기준만' if a.terrain_mode == 'datum' else '')
            _write_atomic(dem_path, ('window.RV_DEM = ' + json.dumps(dj, ensure_ascii=False) + ';\n').encode('utf-8'))
            log('높이 타일 %d장 (%.1f MB)' % (len(dj['tiles']), os.path.getsize(dem_path) / 1e6), fp)
    elif os.path.exists(dem_path):
        os.remove(dem_path)                    # 옛 높이 타일이 다른 원점으로 남지 않게
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
