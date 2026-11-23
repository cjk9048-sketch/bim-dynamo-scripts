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


def write_lines(path, lines, fields, prj_wkt=None, encoding='cp949'):
    """선 SHP 쓰기 (.shp .shx .dbf .prj .cpg).

    lines  = [{'pts': [[x, y], ...], 'rec': {필드: 값}}]   (2D PolyLine)
    fields = [(이름, 'N'|'C', 길이, 소수)]   이름은 인코딩해서 10바이트 안
    """
    base = os.path.splitext(path)[0]
    recs = []
    for ln in lines:
        pts = [(float(p[0]), float(p[1])) for p in ln['pts']]
        if len(pts) < 2:
            continue
        xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
        body = struct.pack('<i4d', 3, min(xs), min(ys), max(xs), max(ys)) + struct.pack('<ii', 1, len(pts))
        body += struct.pack('<i', 0) + b''.join(struct.pack('<2d', *p) for p in pts)
        recs.append((body, (min(xs), min(ys), max(xs), max(ys)), ln.get('rec', {})))
    if not recs:
        raise ValueError('내보낼 선 없음')
    bb = (min(r[1][0] for r in recs), min(r[1][1] for r in recs), max(r[1][2] for r in recs), max(r[1][3] for r in recs))

    def header(words):
        return struct.pack('>7i', 9994, 0, 0, 0, 0, 0, words) + struct.pack('<2i4d4d', 1000, 3, *bb, 0, 0, 0, 0)

    shp = b''; shx = b''; off = 50
    for i, (body, _, _) in enumerate(recs):
        shx += struct.pack('>2i', off, len(body) // 2)
        shp += struct.pack('>2i', i + 1, len(body) // 2) + body
        off += 4 + len(body) // 2
    with open(base + '.shp', 'wb') as f:
        f.write(header(50 + len(shp) // 2) + shp)
    with open(base + '.shx', 'wb') as f:
        f.write(header(50 + len(shx) // 2) + shx)
    # DBF
    hl = 32 + 32 * len(fields) + 1
    rl = 1 + sum(fl[2] for fl in fields)
    h = struct.pack('<BBBBIHH20x', 3, 126, 11, 23, len(recs), hl, rl)
    for name, typ, ln_, dec in fields:
        nb = name.encode(encoding)[:10]
        h += nb + b'\x00' * (11 - len(nb)) + typ.encode('ascii') + b'\x00' * 4 + bytes([ln_, dec]) + b'\x00' * 14
    h += b'\x0d'
    rows = b''
    for _, _, rec in recs:
        rows += b' '
        for name, typ, ln_, dec in fields:
            v = rec.get(name)
            if typ == 'N':
                s = '' if v is None else (('%.*f' % (dec, float(v))) if dec else str(int(round(float(v)))))
                b = s.encode('ascii')[:ln_].rjust(ln_)
            else:
                b = str('' if v is None else v).encode(encoding, 'replace')[:ln_].ljust(ln_)
            rows += b
    with open(base + '.dbf', 'wb') as f:
        f.write(h + rows + b'\x1a')
    if prj_wkt:
        with open(base + '.prj', 'w', encoding='ascii', errors='replace') as f:
            f.write(prj_wkt)
    with open(base + '.cpg', 'w', encoding='ascii') as f:
        f.write({'cp949': 'CP949', 'utf-8': 'UTF-8'}.get(encoding.lower(), encoding))
    return len(recs)


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
