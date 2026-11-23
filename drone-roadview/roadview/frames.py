"""OSV에서 한 장면의 두 어안 그림을 꺼낸다 (ffmpeg 사용)."""
from __future__ import annotations

import os
import shutil
import subprocess
import tempfile

import numpy as np

# 따로 지정이 없으면 PC 안에서 HEVC를 풀 수 있는 ffmpeg를 찾는다.
_CANDIDATES = [
    os.environ.get('RV_FFMPEG', ''),
    shutil.which('ffmpeg') or '',
]


def _capcut_ffmpegs():
    base = os.path.join(os.environ.get('LOCALAPPDATA', ''), 'CapCut', 'Apps')
    if not os.path.isdir(base):
        return []
    out = []
    for d in sorted(os.listdir(base), reverse=True):
        p = os.path.join(base, d, 'ffmpeg.exe')
        if os.path.isfile(p):
            out.append(p)
    return out


def _bundled_ffmpegs():
    """설치본에 같이 넣은 ffmpeg · 파이썬 꾸러미(imageio-ffmpeg) 안 ffmpeg."""
    import sys
    out = []
    if getattr(sys, 'frozen', False):
        here = os.path.dirname(sys.executable)
        out += [os.path.join(here, 'ffmpeg', 'ffmpeg.exe'), os.path.join(here, '_internal', 'ffmpeg', 'ffmpeg.exe')]
    try:
        import imageio_ffmpeg
        out.append(imageio_ffmpeg.get_ffmpeg_exe())
    except Exception:
        pass
    return out


def find_ffmpeg(explicit: str | None = None) -> str:
    cands = [explicit or ''] + _bundled_ffmpegs() + _CANDIDATES + _capcut_ffmpegs()
    for c in cands:
        if not c or not os.path.isfile(c):
            continue
        try:
            r = subprocess.run([c, '-hide_banner', '-decoders'], capture_output=True, text=True, timeout=30,
                               encoding='utf-8', errors='replace')
            if ' hevc ' in r.stdout:
                return c
        except Exception:
            continue
    raise RuntimeError('ffmpeg 없음 → --ffmpeg 로 HEVC 를 푸는 ffmpeg.exe 경로를 주세요')


def extract_pair(ffmpeg: str, osv: str, t: float, size: int = 3840) -> tuple[np.ndarray, np.ndarray]:
    """시각 t(초)의 장면 → (위 렌즈, 아래 렌즈) RGB uint8 배열 (size x size)."""
    tmp = tempfile.mkdtemp(prefix='rv_')
    try:
        p0 = os.path.join(tmp, 'a.rgb')
        p1 = os.path.join(tmp, 'b.rgb')
        cmd = [ffmpeg, '-hide_banner', '-loglevel', 'error', '-y', '-threads', '3',
               '-ss', '%.4f' % max(0.0, t), '-i', osv]
        sc = [] if size == 3840 else ['-vf', 'scale=%d:%d:flags=area' % (size, size)]
        cmd += ['-map', '0:v:0', '-frames:v', '1'] + sc + ['-f', 'rawvideo', '-pix_fmt', 'rgb24', p0]
        cmd += ['-map', '0:v:1', '-frames:v', '1'] + sc + ['-f', 'rawvideo', '-pix_fmt', 'rgb24', p1]
        r = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace')
        if r.returncode != 0:
            raise RuntimeError('ffmpeg 실패: ' + r.stderr[-400:])
        a = np.fromfile(p0, dtype=np.uint8)
        b = np.fromfile(p1, dtype=np.uint8)
        n = size * size * 3
        if a.size != n or b.size != n:
            raise RuntimeError('장면 크기 다름 (%d, %d)' % (a.size, b.size))
        return a.reshape(size, size, 3), b.reshape(size, size, 3)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
