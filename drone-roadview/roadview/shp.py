"""SHP(선) + DBF(속성) 읽기 — 꾸러미 없이.

관로 SHP 는 보통 PolyLine(3) · PolyLineZ(13) · PolyLineM(23). 속성 글자는 .cpg 가 있으면 그 인코딩,
없으면 cp949(한국 GIS 자료 대부분) → 안 되면 utf-8.
"""
from __future__ import annotations

import os
import struct


def _sidecar(path, ext):
    base = os.path.splitext(path)[0]
    for e in (ext, ext.upper()):
        p = base + e
        if os.path.exists(p):
            return p
    return None


def _encoding(path):
    cpg = _sidecar(path, '.cpg')
    if cpg:
        try:
            enc = open(cpg, encoding='ascii', errors='ignore').read().strip().lower()
            enc = {'utf8': 'utf-8', 'euc-kr': 'cp949', 'euckr': 'cp949', '949': 'cp949', 'ansi 949': 'cp949'}.get(enc, enc)
            ''.encode(enc)
            return enc
        except Exception:
            pass
    return 'cp949'


def _dec(b: bytes, enc: str) -> str:
    b = b.rstrip(b'\x00 ').lstrip(b' ')
    for e in (enc, 'cp949', 'utf-8'):
        try:
            return b.decode(e)
        except Exception:
            continue
    return b.decode('latin1')


def read_dbf(path):
    """→ (fields[(name, type, len, dec)], records[dict])."""
    dbf = _sidecar(path, '.dbf')
    if not dbf:
        return [], []
    enc = _encoding(path)
    with open(dbf, 'rb') as f:
        data = f.read()
    n = struct.unpack_from('<I', data, 4)[0]
    hlen, rlen = struct.unpack_from('<HH', data, 8)
    fields = []
    pos = 32
    while pos < hlen - 1 and data[pos] != 0x0D:
        name = _dec(data[pos:pos + 11].split(b'\x00')[0], enc)
        ftype = chr(data[pos + 11])
        flen = data[pos + 16]
        fdec = data[pos + 17]
        fields.append((name, ftype, flen, fdec))
        pos += 32
    recs = []
    off = hlen
    for _ in range(n):
        if off + rlen > len(data):
            break
        row = data[off:off + rlen]
        off += rlen
        if row[:1] == b'*':          # 지운 줄
            recs.append(None)
            continue
        p = 1
        d = {}
        for name, ftype, flen, fdec in fields:
            raw = row[p:p + flen]
            p += flen
            s = _dec(raw, enc).strip()
            if ftype in 'NFO' or (ftype == 'B' and flen != 8):
                try:
                    d[name] = float(s) if s not in ('', '*' * len(s)) else None
                except ValueError:
                    d[name] = None
            elif ftype == 'I' and flen == 4:
                d[name] = struct.unpack('<i', raw)[0]
            elif ftype == 'B' and flen == 8:
                d[name] = struct.unpack('<d', raw)[0]
            else:
                d[name] = s
        recs.append(d)
    return fields, recs


def read_shp_lines(path):
    """→ [(record_index, [[x, y, z|None], ...]), ...]  (여러 조각 선은 조각마다 한 줄)."""
    with open(path, 'rb') as f:
        data = f.read()
    if struct.unpack_from('>i', data, 0)[0] != 9994:
        raise ValueError('SHP 파일이 아님')
    out = []
    pos = 100
    rec = 0
    while pos + 8 <= len(data):
        _, clen = struct.unpack_from('>ii', data, pos)
        pos += 8
        body = data[pos:pos + clen * 2]
        pos += clen * 2
        idx = rec
        rec += 1
        if len(body) < 4:
            continue
        st = struct.unpack_from('<i', body, 0)[0]
        if st not in (3, 13, 23, 5, 15, 25):
            continue
        nparts, npts = struct.unpack_from('<ii', body, 36)
        parts = list(struct.unpack_from('<%di' % nparts, body, 44))
        p0 = 44 + 4 * nparts
        xy = struct.unpack_from('<%dd' % (2 * npts), body, p0)
        zs = None
        if st in (13, 15):
            zoff = p0 + 16 * npts + 16
            if zoff + 8 * npts <= len(body):
                zs = struct.unpack_from('<%dd' % npts, body, zoff)
        parts.append(npts)
        for a, b in zip(parts[:-1], parts[1:]):
            pts = [[xy[2 * i], xy[2 * i + 1], (zs[i] if zs else None)] for i in range(a, b)]
            if len(pts) >= 2:
                out.append((idx, pts))
    return out


def prj_crs(path):
    """.prj → pyproj CRS 또는 None."""
    prj = _sidecar(path, '.prj')
    if not prj:
        return None
    try:
        from pyproj import CRS
        wkt = open(prj, encoding='utf-8', errors='ignore').read()
        crs = CRS.from_wkt(wkt)
        try:
            epsg = crs.to_epsg(min_confidence=60)
            if epsg:
                return CRS.from_epsg(epsg)
        except Exception:
            pass
        return crs
    except Exception:
        return None


DIA_HINTS = ('관경', '구경', '호칭', 'DIA', 'DIP', 'DN', 'PIPE_D', 'PIP_DIP', 'SIZE', 'STD_DIP', '직경')


def guess_dia_field(fields, recs):
    """관경(mm) 같은 숫자 항목 짐작 → 이름 또는 None."""
    best = None
    for name, ftype, flen, fdec in fields:
        vals = [r[name] for r in recs[:500] if r and isinstance(r.get(name), (int, float))]
        if not vals:
            continue
        inside = sum(1 for v in vals if 15 <= v <= 4000) / len(vals)
        score = inside + (1.0 if any(h.lower() in name.lower() for h in DIA_HINTS) else 0.0)
        if inside > 0.6 and (best is None or score > best[0]):
            best = (score, name)
    return best[1] if best else None


def read_pipes(path, dia_field=None, label_field=None, dia_scale=0.001):
    """SHP 관로 → [{'pts': [[x,y,z|None]...], 'dia': m|None, 'label': str, 'hasZ': bool}], crs."""
    fields, recs = read_dbf(path)
    lines = []
    for idx, pts in read_shp_lines(path):
        r = recs[idx] if idx < len(recs) else None
        if r is None and recs:
            continue                        # 지운 줄
        dia = None
        if r and dia_field:
            v = r.get(dia_field)
            if isinstance(v, str):
                try:
                    v = float(''.join(ch for ch in v if ch.isdigit() or ch == '.'))
                except ValueError:
                    v = None
            if isinstance(v, (int, float)) and v > 0:
                dia = float(v) * dia_scale
        label = str(r.get(label_field, '')) if (r and label_field) else ''
        # SHP 의 Z 는 땅 높이 · 0 · 관 높이가 섞여 있어 믿지 않는다 → 늘 «땅 − 평균매설깊이» (JACK 1123)
        lines.append({'pts': [[p[0], p[1], None] for p in pts], 'dia': dia, 'label': label, 'hasZ': False})
    return lines, prj_crs(path)
