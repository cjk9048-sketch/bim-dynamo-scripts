"""DJI Avata 360 .OSV 파일 읽기.

OSV = MP4 그릇. 흐름(track):
  영상 2개 (hvc1, 3840x3840 어안: 0번 = 위(하늘) 렌즈, 1번 = 아래(땅) 렌즈)
  djmd 2개 (렌즈마다 한 개, 장면마다 한 칸 — protobuf «dvtm_AVATA360.proto»)
  dbgi 2개 (진단), camd 1개 (보정 자료)

djmd 한 칸에서 쓰는 값 (2026-11-23 0014 영상으로 확인):
  .3.1.1            장면 번호
  .3.4.4.1.2 / .3   위도 / 경도 (double)
  .3.4.4.2          GPS 높이 (0.1 m 단위로 보임 — 확인 중)
  .3.4.5.1          이륙점 기준 높이 (mm)
  .3.4.14.3.1~3     속도 NED (m/s)
  .3.4.14.4.1~4     드론 자세 사원수 (w, x, y, z) — NED 기준
  .3.3.2.1.3.1~4    카메라 IMU 사원수 (장면 안에 여러 개)
"""
from __future__ import annotations

import os
import struct
from dataclasses import dataclass, field


# ---------------------------------------------------------------- MP4 상자

class Mp4:
    def __init__(self, path: str):
        self.path = path
        self.f = open(path, 'rb')
        self.size = os.path.getsize(path)

    def close(self):
        self.f.close()

    def _boxes(self, start, end):
        f = self.f
        pos = start
        while pos < end - 8:
            f.seek(pos)
            size, typ = struct.unpack('>I4s', f.read(8))
            hdr = 8
            if size == 1:
                size = struct.unpack('>Q', f.read(8))[0]
                hdr = 16
            elif size == 0:
                size = end - pos
            if size < hdr:
                break
            yield typ, pos + hdr, pos + size
            pos += size

    def find(self, start, end, path):
        for t, a, b in self._boxes(start, end):
            if t == path[0]:
                if len(path) == 1:
                    yield a, b
                else:
                    yield from self.find(a, b, path[1:])

    def tracks(self):
        """[(handler_name, fmt, timescale, sample_offsets, sample_sizes, sample_times)]"""
        f = self.f
        moov = next(self.find(0, self.size, [b'moov']))
        out = []
        for a, b in self.find(moov[0], moov[1], [b'trak']):
            h = next(self.find(a, b, [b'mdia', b'hdlr']))
            f.seek(h[0])
            d = f.read(h[1] - h[0])
            name = d[24:].split(b'\0')[0].decode('latin1')
            md = next(self.find(a, b, [b'mdia', b'mdhd']))
            f.seek(md[0])
            d = f.read(32)
            ts = struct.unpack('>I', d[12:16])[0] if d[0] == 0 else struct.unpack('>I', d[20:24])[0]
            st = next(self.find(a, b, [b'mdia', b'minf', b'stbl']))
            f.seek(next(self.find(st[0], st[1], [b'stsd']))[0] + 12)
            fmt = f.read(4).decode('latin1')
            # 크기
            sz = next(self.find(st[0], st[1], [b'stsz']))
            f.seek(sz[0] + 4)
            ss, cnt = struct.unpack('>II', f.read(8))
            sizes = [ss] * cnt if ss else list(struct.unpack('>%dI' % cnt, f.read(4 * cnt)))
            # 덩어리 위치
            co = list(self.find(st[0], st[1], [b'co64']))
            if co:
                f.seek(co[0][0] + 4)
                c = struct.unpack('>I', f.read(4))[0]
                chunk_offs = list(struct.unpack('>%dQ' % c, f.read(8 * c)))
            else:
                co = next(self.find(st[0], st[1], [b'stco']))
                f.seek(co[0] + 4)
                c = struct.unpack('>I', f.read(4))[0]
                chunk_offs = list(struct.unpack('>%dI' % c, f.read(4 * c)))
            # 덩어리당 칸 수
            sc = next(self.find(st[0], st[1], [b'stsc']))
            f.seek(sc[0] + 4)
            n = struct.unpack('>I', f.read(4))[0]
            stsc = [struct.unpack('>III', f.read(12)) for _ in range(n)]
            offs = []
            si = 0
            for ci in range(len(chunk_offs)):
                per = 1
                for first, spc, _ in stsc:
                    if ci + 1 >= first:
                        per = spc
                o = chunk_offs[ci]
                for _ in range(per):
                    if si >= cnt:
                        break
                    offs.append(o)
                    o += sizes[si]
                    si += 1
            # 시각
            tt = next(self.find(st[0], st[1], [b'stts']))
            f.seek(tt[0] + 4)
            n = struct.unpack('>I', f.read(4))[0]
            times = []
            t = 0
            for _ in range(n):
                c, dlt = struct.unpack('>II', f.read(8))
                for _ in range(c):
                    times.append(t / ts)
                    t += dlt
            out.append((name, fmt, ts, offs, sizes[:len(offs)], times[:len(offs)]))
        return out

    def read(self, off, size):
        self.f.seek(off)
        return self.f.read(size)


# ---------------------------------------------------------------- protobuf (틀 없이)

def _varint(b, i):
    r = 0
    sh = 0
    while True:
        x = b[i]
        i += 1
        r |= (x & 0x7f) << sh
        sh += 7
        if x < 0x80:
            return r, i


def pb_parse(b: bytes, path: str = '', out: list | None = None):
    """틀 없이 protobuf 풀기 → [(경로, 종류, 값)]. 실패하면 None."""
    if out is None:
        out = []
    i = 0
    n = len(b)
    try:
        while i < n:
            key, i = _varint(b, i)
            fn = key >> 3
            wt = key & 7
            if fn == 0 or fn > 100000:
                return None
            p = path + '.' + str(fn)
            if wt == 0:
                v, i = _varint(b, i)
                out.append((p, 'v', v))
            elif wt == 1:
                if i + 8 > n:
                    return None
                out.append((p, 'd', struct.unpack_from('<d', b, i)[0]))
                i += 8
            elif wt == 5:
                if i + 4 > n:
                    return None
                out.append((p, 'f', struct.unpack_from('<f', b, i)[0]))
                i += 4
            elif wt == 2:
                L, i = _varint(b, i)
                if i + L > n:
                    return None
                sub = b[i:i + L]
                i += L
                tmp = []
                if L > 0 and pb_parse(sub, p, tmp) is not None:
                    out.extend(tmp)
                else:
                    out.append((p, 'b', sub))
            else:
                return None
        return out if i == n else None
    except IndexError:
        return None


def _signed(v):
    return v - (1 << 64) if v >= (1 << 63) else v


# ---------------------------------------------------------------- 장면 기록

@dataclass
class FrameMeta:
    index: int
    time: float
    lat: float | None = None
    lon: float | None = None
    gps_alt: float | None = None      # .3.4.4.2 (원값)
    rel_alt: float | None = None      # m
    vel_ned: tuple | None = None
    q_body: tuple | None = None       # (w,x,y,z) NED
    q_cam: tuple | None = None        # 카메라 IMU 첫 값 (w,x,y,z)
    raw_extra: dict = field(default_factory=dict)


def read_meta(path: str, track: int = 0) -> list[FrameMeta]:
    """OSV 한 파일의 장면별 기록. track = djmd 몇 번째(0 = 위 렌즈 쪽)."""
    mp = Mp4(path)
    try:
        tr = [t for t in mp.tracks() if t[1] == 'djmd']
        name, fmt, ts, offs, sizes, times = tr[track]
        frames = []
        for k in range(len(offs)):
            s = mp.read(offs[k], sizes[k])
            fields = pb_parse(s)
            fm = FrameMeta(index=k, time=times[k])
            if fields is None:
                frames.append(fm)
                continue
            q = {}
            v = {}
            qc = {}
            for p, t, val in fields:
                if t == 'b':                     # 글자 · 덩어리는 안 씀
                    continue
                if t == 'v':
                    val = _signed(val)
                head, _, last = p.rpartition('.')
                if p == '.3.4.4.1.2' and t == 'd':
                    fm.lat = val
                elif p == '.3.4.4.1.3' and t == 'd':
                    fm.lon = val
                elif p == '.3.4.4.2':
                    fm.gps_alt = val
                elif p == '.3.4.5.1':
                    fm.rel_alt = val / 1000.0    # 0014: f32 (mm)
                elif head == '.3.4.14.4' and last in '1234':
                    q[last] = val
                elif head == '.3.4.14.3' and last in '123':
                    v[last] = val
                elif head == '.3.3.2.1.3' and last in '1234':
                    qc.setdefault(last, val)     # 장면 안 여러 개 중 첫 값
                elif head == '.3.4.3' and last in ('1', '2', '3'):
                    fm.raw_extra['a' + last] = val
                elif head == '.3.4.2' and last in ('1', '2', '3'):
                    fm.raw_extra['b' + last] = val
                elif head == '.3.4.14.2' and last in ('1', '2', '3'):
                    fm.raw_extra['p' + last] = val
            if len(q) == 4:
                fm.q_body = (q['1'], q['2'], q['3'], q['4'])
            if len(v) == 3:
                fm.vel_ned = (v['1'], v['2'], v['3'])
            if len(qc) == 4:
                fm.q_cam = (qc['1'], qc['2'], qc['3'], qc['4'])
            # 위경도가 (0, 0) 이면 GPS 없음으로
            if fm.lat is not None and fm.lon is not None and abs(fm.lat) < 1e-6 and abs(fm.lon) < 1e-6:
                fm.lat = fm.lon = None
            frames.append(fm)
        return frames
    finally:
        mp.close()


def video_info(path: str):
    """(장면 수, 초당 장면, 길이 초)"""
    mp = Mp4(path)
    try:
        v = [t for t in mp.tracks() if t[1] == 'hvc1'][0]
        n = len(v[3])
        dur = v[5][-1] + (v[5][-1] - v[5][-2] if n > 1 else 0)
        return n, n / dur if dur else 30.0, dur
    finally:
        mp.close()
