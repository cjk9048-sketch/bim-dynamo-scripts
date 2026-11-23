"""DJI 360 사진(JPG · 카메라가 이미 꿰맨 등장방형) 읽기 — 사진 모드 촬영용.

위치 · 방향은 사진 안 XMP / EXIF:
  방향: GPano:PoseHeadingDegrees → drone-dji:GimbalYawDegree → drone-dji:FlightYawDegree
        (사진 가운데 열이 바라보는 참 방위로 봄 — 블로그 뷰어와 같은 약속)
  위치: drone-dji:GpsLatitude/GpsLongitude → EXIF GPS
  높이: drone-dji:RelativeAltitude (이륙점 기준)
★ Avata 360 실물 사진으로 아직 확인 못 함 (1123) — 첫 사진으로 방향 약속을 확인할 것.
"""
from __future__ import annotations

import os
import re
from datetime import datetime

import numpy as np

_ATTR = re.compile(rb'([A-Za-z\-]+):([A-Za-z]+)\s*=\s*"([^"]*)"')
_ELEM = re.compile(rb'<([A-Za-z\-]+):([A-Za-z]+)>([^<]*)</')


def _xmp(path, limit=4 << 20):
    with open(path, 'rb') as f:
        head = f.read(limit)
    a = head.find(b'<x:xmpmeta')
    if a < 0:
        return {}
    b = head.find(b'</x:xmpmeta>', a)
    pkt = head[a:b if b > 0 else len(head)]
    out = {}
    for ns, k, v in _ATTR.findall(pkt) + _ELEM.findall(pkt):
        out['%s:%s' % (ns.decode('latin1'), k.decode('latin1'))] = v.decode('utf-8', 'replace').strip()
    return out


def _num(s):
    try:
        return float(str(s).replace('+', ''))
    except (TypeError, ValueError):
        return None


def _exif_gps(im):
    try:
        ex = im.getexif()
        g = ex.get_ifd(0x8825)
        if not g:
            return None, None, None

        def dms(v):
            d, m, s = [float(x) for x in v]
            return d + m / 60 + s / 3600
        lat = dms(g[2]) * (-1 if g.get(1) == 'S' else 1)
        lon = dms(g[4]) * (-1 if g.get(3) == 'W' else 1)
        t = ex.get_ifd(0x8769).get(0x9003) or ex.get(0x0132)
        ts = datetime.strptime(t, '%Y:%m:%d %H:%M:%S').timestamp() if t else None
        return lat, lon, ts
    except Exception:
        return None, None, None


def read_photo(path):
    """→ dict(lat, lon, rel, heading, time, w, h) 또는 None (360 사진 아님 · 위치 없음)."""
    from PIL import Image
    try:
        im = Image.open(path)
        w, h = im.size
    except Exception:
        return None
    if abs(w - 2 * h) > 2:                      # 등장방형(2:1)만
        return None
    x = _xmp(path)
    lat = _num(x.get('drone-dji:GpsLatitude') or x.get('drone-dji:GpsLatitudE'))
    lon = _num(x.get('drone-dji:GpsLongitude') or x.get('drone-dji:GpsLongtitude'))
    elat, elon, ts = _exif_gps(im)
    if lat is None or lon is None:
        lat, lon = elat, elon
    if lat is None or lon is None:
        return None
    heading = None
    for k in ('GPano:PoseHeadingDegrees', 'drone-dji:GimbalYawDegree', 'drone-dji:FlightYawDegree'):
        heading = _num(x.get(k))
        if heading is not None:
            src = k
            break
    else:
        src = '없음'
    rel = _num(x.get('drone-dji:RelativeAltitude'))
    return dict(path=path, lat=lat, lon=lon, rel=rel if rel is not None else 0.0, heading=heading or 0.0,
                heading_src=src, time=ts or os.path.getmtime(path), w=w, h=h)


def to_grid_north(path, heading_true, gamma, width):
    """사진 가운데 = 참 방위 heading → 우리 약속(가운데 = 격자 북)으로 옆으로 돌리고 width 로 맞춤 → uint8."""
    from PIL import Image
    im = Image.open(path).convert('RGB')
    if im.size[0] != width:
        im = im.resize((width, width // 2), Image.LANCZOS)
    a = np.asarray(im)
    # 우리 열 x 의 격자 방위 λ → 참 방위 λ+γ → 사진 열 = x + (γ − heading)/360·W
    shift = int(round((gamma - heading_true) / 360.0 * width))
    return np.roll(a, -shift, axis=1)
