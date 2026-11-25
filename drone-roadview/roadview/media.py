"""프로젝트 생성 편집 화면용 — 영상 한 개의 미리보기 자료 (JACK 1125: DJI Studio 처럼 불러와서 잘라 지우기).

  미리보기 영상 = 같은 이름 .LRF (DJI 저화질 사본 · 1920x960 HEVC · 왼쪽 위 렌즈 | 오른쪽 아래 렌즈 · OSV 와 장면 시각 같음)
  비행 경로     = djmd 기록 (LRF 에도 있음 · 없으면 OSV) → 0.5 초마다 [t, 위도, 경도, 진행 m]
  썸네일        = 아래 렌즈 동그란 그림 · 2 초마다 (긴 영상은 400장 안쪽) · 열쇠 장면만 풀어 빠르게

한 번 만든 것은 %APPDATA%\\DH.Roadview\\media\\<열쇠> 에 두고 다시 쓴다 (파일 경로 · 크기 · 고친 시각이 같으면).
"""
from __future__ import annotations

import hashlib
import json
import math
import os
import subprocess
import threading
from pathlib import Path

NO_WINDOW = 0x08000000 if os.name == 'nt' else 0
CACHE_VERSION = 1


def lrf_of(path: str) -> str | None:
    """OSV 옆 같은 이름 LRF (대소문자 무관)."""
    p = Path(path)
    for ext in ('.LRF', '.lrf', '.Lrf'):
        c = p.with_suffix(ext)
        if c.exists():
            return str(c)
    return None


def cache_dir(root: Path, path: str) -> Path:
    st = os.stat(path)
    k = '%s|%d|%d|%d' % (os.path.normcase(os.path.abspath(path)), st.st_size, int(st.st_mtime), CACHE_VERSION)
    return root / (Path(path).stem[:40] + '_' + hashlib.md5(k.encode('utf-8')).hexdigest()[:10])


def quick_info(path: str) -> dict:
    """고르자마자 보일 것 (1초 안): 이름 · 길이 · 장면 수 · 미리보기 영상."""
    from .osv import video_info
    lrf = lrf_of(path)
    src = lrf or path
    n, fps, dur = video_info(src)
    return {'path': os.path.abspath(path), 'name': Path(path).stem, 'lrf': lrf, 'frames': n, 'fps': fps,
            'dur': dur, 'size_gb': round(os.path.getsize(path) / 1e9, 2)}


def _track(src: str) -> list:
    """[[t, 위도, 경도, 진행 m, 이륙점 기준 높이]] 0.5 초마다 (GPS 없는 장면은 뺌)."""
    from .osv import read_meta
    frames = read_meta(src)
    out = []
    last_t = -1e9
    prev = None
    dist = 0.0
    for f in frames:
        if f.lat is None or f.lon is None:
            continue
        if prev is not None:
            dy = (f.lat - prev.lat) * 111320.0
            dx = (f.lon - prev.lon) * 111320.0 * math.cos(math.radians(f.lat))
            step = math.hypot(dx, dy)
            if step < 30 or f.time - prev.time > 2.0:        # build.select_shots 와 같은 셈
                dist += step
        prev = f
        if f.time - last_t >= 0.5:
            out.append([round(f.time, 3), round(f.lat, 7), round(f.lon, 7), round(dist, 1),
                        round(f.rel_alt, 1) if f.rel_alt is not None else None])
            last_t = f.time
    if prev is not None and out and out[-1][0] != round(prev.time, 3):
        out.append([round(prev.time, 3), round(prev.lat, 7), round(prev.lon, 7), round(dist, 1),
                    round(prev.rel_alt, 1) if prev.rel_alt is not None else None])
    return out


def _thumbs(ffmpeg: str, src: str, is_lrf: bool, dur: float, out: Path) -> float:
    """아래 렌즈 동그란 썸네일 t_0001.jpg … → 간격(초). k 번째 = 대략 (k-1)·간격 초."""
    step = max(2.0, dur / 400.0)
    out.mkdir(parents=True, exist_ok=True)
    if is_lrf:              # 1920x960: 오른쪽 반 = 아래 렌즈
        cmd = [ffmpeg, '-hide_banner', '-loglevel', 'error', '-y', '-skip_frame', 'nokey', '-i', src,
               '-map', '0:v:0', '-vf', 'fps=1/%g,crop=ih:ih:iw-ih:0,scale=96:96' % step]
    else:                   # OSV: 1번 영상 = 아래 렌즈
        cmd = [ffmpeg, '-hide_banner', '-loglevel', 'error', '-y', '-skip_frame', 'nokey', '-i', src,
               '-map', '0:v:1', '-vf', 'fps=1/%g,scale=96:96' % step]
    cmd += ['-q:v', '6', str(out / 't_%04d.jpg')]
    subprocess.run(cmd, capture_output=True, creationflags=NO_WINDOW, timeout=1800)
    return step


def frame_at(root: Path, path: str, t: float) -> str:
    """미리보기 영상을 못 트는 PC 용 — 그 시각 한 장면(위 | 아래 렌즈 나란히, 1280x640 JPG) → 파일 경로."""
    from .frames import find_ffmpeg
    d = cache_dir(root, path) / 'fr'
    d.mkdir(parents=True, exist_ok=True)
    out = d / ('f_%09.3f.jpg' % max(0.0, t))
    if out.exists():
        return str(out)
    lrf = lrf_of(path)
    ff = find_ffmpeg()
    if lrf:
        cmd = [ff, '-hide_banner', '-loglevel', 'error', '-y', '-ss', '%.3f' % max(0.0, t), '-i', lrf,
               '-map', '0:v:0', '-frames:v', '1', '-vf', 'scale=1280:640']
    else:
        cmd = [ff, '-hide_banner', '-loglevel', 'error', '-y', '-ss', '%.3f' % max(0.0, t), '-i', path,
               '-filter_complex', '[0:v:0]scale=640:640[a];[0:v:1]scale=640:640[b];[a][b]hstack', '-frames:v', '1']
    subprocess.run(cmd + ['-q:v', '4', str(out)], capture_output=True, creationflags=NO_WINDOW, timeout=60)
    return str(out) if out.exists() else ''


class MediaLoad:
    """뒤에서 경로 · 썸네일 만들기. state = run | done | fail."""

    def __init__(self, root: Path, path: str):
        self.path = path
        self.state = 'run'
        self.line = '읽는 중'
        self.result = None
        self.partial = None
        self.dir = cache_dir(root, path)
        threading.Thread(target=self._run, daemon=True).start()

    def _run(self):
        try:
            info = quick_info(self.path)
            self.partial = dict(info, ready=False,
                                lrf_url=Path(info['lrf']).resolve().as_uri() if info['lrf'] else '')
            meta = self.dir / 'media.json'
            try:
                m = json.loads(meta.read_text(encoding='utf-8'))
                if m.get('version') == CACHE_VERSION and m.get('track') and m.get('thumbs'):
                    self.result = self._finish(info, m)
                    self.state = 'done'
                    return
            except (OSError, ValueError):
                pass
            from .frames import find_ffmpeg
            ffmpeg = find_ffmpeg()
            src = info['lrf'] or self.path
            got = {}

            def th():
                got['step'] = _thumbs(ffmpeg, src, bool(info['lrf']), info['dur'], self.dir / 'th')
            t = threading.Thread(target=th, daemon=True)
            t.start()
            self.line = '경로 읽는 중'
            track = _track(src)
            self.line = '썸네일 만드는 중'
            t.join()
            n = len(list((self.dir / 'th').glob('t_*.jpg')))
            m = {'version': CACHE_VERSION, 'track': track, 'thumbs': n, 'thumb_step': got.get('step', 2.0)}
            self.dir.mkdir(parents=True, exist_ok=True)
            tmp = meta.with_suffix('.tmp')
            tmp.write_text(json.dumps(m), encoding='utf-8')
            os.replace(tmp, meta)
            self.result = self._finish(info, m)
            self.state = 'done'
        except Exception as ex:          # noqa: BLE001 — 화면에 알림으로
            self.line = '실패 · ' + str(ex)[:120]
            self.state = 'fail'

    def _finish(self, info, m):
        r = dict(info)
        r['track'] = m['track']
        r['thumb_step'] = m['thumb_step']
        base = (self.dir / 'th').resolve().as_uri()
        r['thumb_base'] = base + '/t_'
        r['thumbs'] = m['thumbs']
        r['lrf_url'] = Path(info['lrf']).resolve().as_uri() if info['lrf'] else ''
        r['dist'] = m['track'][-1][3] if m['track'] else 0.0
        r['ready'] = True
        return r

    def info(self):
        return {'state': self.state, 'line': self.line, 'media': self.result or self.partial}
