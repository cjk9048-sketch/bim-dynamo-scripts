"""만들어 둔 로드뷰 폴더에 관로만 새로 넣기 (360 사진 · 영상은 안 건드림 · 몇 초).

    python -m roadview.pipes_update 결과폴더 관로.shp [--dia-field 관경] [--label-field 이름] [--depth 1.2]
"""
from __future__ import annotations

import argparse
import json
import os
import sys

from . import pipeload
from .geo import Geo


def _read_js(path, var):
    s = open(path, encoding='utf-8').read()
    return json.loads(s.split('=', 1)[1].strip().rstrip(';'))


def _write_atomic(path, text):
    tmp = path + '.tmp'
    with open(tmp, 'w', encoding='utf-8') as f:
        f.write(text)
    os.replace(tmp, path)


def fields_of(path):
    """관로 파일 고르기 창용: SHP 면 속성 항목 · 예시 값 · 관경 짐작."""
    ext = os.path.splitext(path)[1].lower()
    if ext == '.shp':
        from .shp import read_dbf, read_shp_lines, guess_dia_field, prj_crs
        fields, recs = read_dbf(path)
        recs = [r for r in recs if r]
        out = []
        for name, *_ in fields:
            vals = []
            for r in recs:
                v = r.get(name)
                if v not in (None, '') and v not in vals:
                    vals.append(v)
                if len(vals) >= 6:
                    break
            out.append({'name': name, 'samples': [('%g' % v) if isinstance(v, float) else str(v) for v in vals]})
        crs = prj_crs(path)
        return {'path': path, 'kind': 'shp', 'name': os.path.basename(path), 'fields': out,
                'guess_dia': guess_dia_field(fields, recs), 'lines': len(read_shp_lines(path)),
                'note': ('좌표계 ' + crs.to_string()) if crs else '.prj 없음 → 프로젝트 좌표계로 봄'}
    if ext == '.dxf':
        return {'path': path, 'kind': 'dxf', 'name': os.path.basename(path), 'fields': [], 'guess_dia': None,
                'note': '3D 폴리선 높이 씀 · 도면층 이름의 D300 같은 글자로 관경'}
    raise ValueError('관로 파일은 .shp 또는 .dxf')


def update(out, path, dia_field=None, label_field=None, depth=None, layers=None, default_dia=200.0, log=print):
    """→ (선 조각 수, 알림)."""
    P = _read_js(os.path.join(out, 'data', 'project.js'), 'RV_PROJECT')
    crs = P.get('crs', 'EPSG:5186')
    E0, N0 = float(P['origin']['E']), float(P['origin']['N'])
    shots = P['shots']
    sen = [(s['e'] + E0, s['n'] + N0) for s in shots]
    sg = [s['groundZ'] for s in shots]
    pats = [x.strip() for x in layers.split(',')] if layers else None
    lay, lines, note = pipeload.load(path, crs, dia_field, label_field, pats, default_dia)
    n_raw = len(lines)
    lines = pipeload.densify(lines, 5.0)
    lines = pipeload.clip_near(lines, sen, 200.0)
    lines = pipeload.ground_for(lines, sen, sg)
    geo = Geo(crs)
    pl = []
    for ln in lines:
        pts = [[round(p[0] - E0, 3), round(p[1] - N0, 3), (round(p[2], 3) if (ln['hasZ'] and p[2] is not None) else None)]
               for p in ln['pts']]
        ll = [[round(v, 8) for v in geo.en_to_ll(p[0], p[1])] for p in ln['pts']]
        pl.append(dict(layer=ln['layer'], label=ln['label'], dia=round(ln['dia'], 4), hasZ=bool(ln['hasZ']),
                       pts=pts, g=[round(v, 3) if v is not None else None for v in ln['g']], ll=ll,
                       ctrl=ln.get('ctrl') or list(range(len(pts)))))
    _write_atomic(os.path.join(out, 'data', 'pipes.js'),
                  'window.RV_PIPES = ' + json.dumps({'layers': lay, 'lines': pl, 'origin': {'E': E0, 'N': N0},
                                                     'source': os.path.basename(path)}, ensure_ascii=False) + ';\n')
    if depth is not None:
        P['depth'] = P['cover'] = float(depth)
        _write_atomic(os.path.join(out, 'data', 'project.js'),
                      'window.RV_PROJECT = ' + json.dumps(P, ensure_ascii=False, indent=1) + ';\n')
    msg = '관로 %d개 → 경로 200 m 안 조각 %d개%s' % (n_raw, len(pl), (' · ' + note) if note else '')
    log(msg)
    return len(pl), msg


def reground(out, fn, var, dem=None):
    """만들기를 다시 할 때 — 있던 관로 · 지장물(고친 것 포함)은 그대로 두고 땅 높이(g)만 새 촬영점 땅 높이로 (1124).
    dem = 점마다 수치지도 높이 함수(절대 좌표 · 지형 그대로 모드) 또는 None.
    → 줄 수 · 파일 없으면 0 · 원점이 달라 못 쓰면 None (그대로 두면 엉뚱한 곳에 그려진다)."""
    import numpy as np
    path = os.path.join(out, 'data', fn)
    if not os.path.exists(path):
        return 0
    try:
        P = _read_js(os.path.join(out, 'data', 'project.js'), 'RV_PROJECT')
        D = _read_js(path, var)
        E0, N0 = float(P['origin']['E']), float(P['origin']['N'])
        o = D.get('origin') or {}
        if abs(float(o['E']) - E0) > 1e-3 or abs(float(o['N']) - N0) > 1e-3:
            return None
    except (OSError, ValueError, KeyError, TypeError, IndexError):
        return None
    shots = P['shots']
    sen = [(s['e'], s['n']) for s in shots]
    sg = [s['groundZ'] for s in shots]
    tmp = pipeload.ground_for([{'pts': ln['pts']} for ln in D.get('lines', [])], sen, sg)
    for ln, t in zip(D.get('lines', []), tmp):
        gs = t['g']
        if dem is not None:
            dz = dem([p[0] + E0 for p in ln['pts']], [p[1] + N0 for p in ln['pts']])
            gs = [float(v) if np.isfinite(v) else x for v, x in zip(dz, gs)]
        ln['g'] = [round(v, 3) if v is not None else None for v in gs]
    _write_atomic(path, 'window.%s = ' % var + json.dumps(D, ensure_ascii=False) + ';\n')
    return len(D.get('lines', []))


TRIM_FILE = 'trim.json'


def load_trims(out):
    """data/trim.json → [{video: 영상 이름, t0, t1}] (지운 사진점 시간 구간 · 다시 만들기에서 뺌)."""
    try:
        with open(os.path.join(out, 'data', TRIM_FILE), encoding='utf-8') as f:
            v = json.load(f)
        return [r for r in v if isinstance(r, dict) and r.get('video') and
                isinstance(r.get('t0'), (int, float)) and isinstance(r.get('t1'), (int, float))]
    except (OSError, ValueError):
        return []


def trim_shots(out, ids):
    """위성지도에서 고른 사진점 지우기 (처음 · 끝 쓸데없는 구간 · JACK 1124) → (지운 수, 알림).

    project.js 에서 빼고 그 사진 파일(영상에서 다시 만들 수 있는 것)은 지운다. 영상 사진은 영상 이름 + 시간 구간을
    data/trim.json 에 남겨 다시 만들어도 안 돌아오게 한다. 맞춤 · 관로 · 원점은 그대로."""
    ids = {str(i) for i in (ids or [])}
    d = os.path.join(out, 'data')
    P = _read_js(os.path.join(d, 'project.js'), 'RV_PROJECT')
    shots = P.get('shots', [])
    gone = [s for s in shots if s.get('id') in ids]
    keep = [s for s in shots if s.get('id') not in ids]
    if not gone:
        return 0, '고른 사진점 없음'
    if len(keep) < 2:
        return 0, '사진점이 2개 이상 남아야 함'
    vname = {v.get('id'): v.get('name') for v in P.get('videos', [])}
    trims = load_trims(out)
    for vid in sorted({s.get('video') for s in gone}):
        name = vname.get(vid)
        if not name or str(name).startswith('360 사진'):   # 사진 모드는 시간 기준이 없어 기록 안 함
            continue
        ts = sorted(float(s['time']) for s in gone if s.get('video') == vid)
        kt = sorted(float(s['time']) for s in keep if s.get('video') == vid)
        # 이어진 덩어리마다 한 구간 (사이에 남긴 사진이 끼면 나눔)
        run = [ts[0]]
        for t in ts[1:]:
            if any(run[-1] < k < t for k in kt):
                trims.append({'video': name, 't0': round(run[0] - 0.01, 3), 't1': round(run[-1] + 0.01, 3)})
                run = [t]
            else:
                run.append(t)
        trims.append({'video': name, 't0': round(run[0] - 0.01, 3), 't1': round(run[-1] + 0.01, 3)})
    _write_atomic(os.path.join(d, TRIM_FILE), json.dumps(trims, ensure_ascii=False, indent=1) + '\n')
    P['shots'] = keep
    _write_atomic(os.path.join(d, 'project.js'),
                  'window.RV_PROJECT = ' + json.dumps(P, ensure_ascii=False, indent=1) + ';\n')
    try:
        with open(os.path.join(d, 'pano_index.json'), encoding='utf-8') as f:
            idx = json.load(f)
        _write_atomic(os.path.join(d, 'pano_index.json'), json.dumps({k: v for k, v in idx.items() if k not in ids}))
    except (OSError, ValueError):
        pass
    for s in gone:
        for p in (os.path.join(d, 'pano', s['id'] + '.js'), os.path.join(d, 'thumb', s['id'] + '.jpg')):
            try:
                os.remove(p)
            except OSError:
                pass
    return len(gone), '사진점 %d개 지움 · 남은 %d개' % (len(gone), len(keep))


# ---------------------------------------------------------------- 지장물

OBST_COLORS = [  # (낱말들, 색) — 지하시설물 관례 색
    (('가스', 'GAS', '도시가스'), '#ffd400'),
    (('전기', '전력', '한전', 'KEPCO', 'ELEC', 'POWER'), '#ff4d4d'),
    (('통신', 'KT', 'TEL', 'COMM', '광케이블', 'SK', 'LG'), '#3ddc84'),
    (('하수', '우수', '오수', 'SEWER', 'DRAIN'), '#b5651d'),
    (('상수', '급수', '배수관', 'WATER'), '#3b82f6'),
    (('난방', '열수송', 'HEAT'), '#ff9f1a'),
]
_OBST_PAL = ['#c084fc', '#f472b6', '#22d3ee', '#a3e635', '#fb7185']


def _obst_color(kind, k):
    u = str(kind).upper()
    for words, c in OBST_COLORS:
        if any(w.upper() in u for w in words):
            return c
    return _OBST_PAL[k % len(_OBST_PAL)] if kind else '#9aa3b2'


def update_obstacles(out, path, type_field=None, dia_field=None, depth_field=None, label_field=None,
                     default_depth=1.0, log=print):
    """지장물 SHP → data/obstacles.js (터파기 없음 · 고치기 없음). → (조각 수, 알림)."""
    from .shp import read_dbf, read_shp_lines, prj_crs
    M = _mapping(out)
    if M:                                        # 3D 매핑 프로젝트: 원점 · 좌표계 = mapping.js (땅 · 범위는 아래)
        crs = M.get('crs', 'EPSG:5186')
        E0, N0 = float(M['origin'][0]), float(M['origin'][1])
    else:
        P = _read_js(os.path.join(out, 'data', 'project.js'), 'RV_PROJECT')
        crs = P.get('crs', 'EPSG:5186')
        E0, N0 = float(P['origin']['E']), float(P['origin']['N'])
        shots = P['shots']
        sen = [(s['e'] + E0, s['n'] + N0) for s in shots]
        sg = [s['groundZ'] for s in shots]
    fields, recs = read_dbf(path)
    src = prj_crs(path)
    tr = None
    note = ''
    if src is not None:
        from pyproj import CRS, Transformer
        if not src.equals(CRS.from_user_input(crs)):
            tr = Transformer.from_crs(src, CRS.from_user_input(crs), always_xy=True)
            note = '좌표계 %s → %s' % (src.to_string(), crs)
    else:
        note = '.prj 없음 → %s 로 봄' % crs

    def num(v, scale=1.0):
        if isinstance(v, str):
            v = ''.join(ch for ch in v if ch.isdigit() or ch == '.')
            try:
                v = float(v)
            except ValueError:
                return None
        return float(v) * scale if isinstance(v, (int, float)) and v > 0 else None

    lines = []
    kinds = []
    for idx, pts in read_shp_lines(path):
        r = recs[idx] if idx < len(recs) else None
        if r is None and recs:
            continue
        kind = str(r.get(type_field, '')).strip() if (r and type_field) else ''
        kind = kind or '지장물'
        if kind not in kinds:
            kinds.append(kind)
        if tr:
            pts = [[*tr.transform(p[0], p[1]), None] for p in pts]
        else:
            pts = [[p[0], p[1], None] for p in pts]
        lines.append({'layer': kinds.index(kind), 'kind': kind,
                      'label': (str(r.get(label_field, '')).strip() if (r and label_field) else '') or kind,
                      'dia': num(r.get(dia_field), 0.001) if (r and dia_field) else None,
                      'depth': num(r.get(depth_field)) if (r and depth_field) else None,
                      'hasZ': False, 'pts': pts})
    n_raw = len(lines)
    lines = pipeload.densify(lines, 5.0)
    if M:
        lines = _mapping_clip_ground(out, M, lines)
    else:
        lines = pipeload.clip_near(lines, sen, 200.0)
        lines = pipeload.ground_for(lines, sen, sg)
    geo = Geo(crs)
    layers = [{'name': k, 'color': _obst_color(k, i)} for i, k in enumerate(kinds)]
    ol = []
    for ln in lines:
        ol.append(dict(layer=ln['layer'], kind=ln['kind'], label=ln['label'],
                       dia=round(ln['dia'], 4) if ln['dia'] else None,
                       depth=round(ln['depth'], 3) if ln['depth'] else None,
                       pts=[[round(p[0] - E0, 3), round(p[1] - N0, 3), None] for p in ln['pts']],
                       g=[round(v, 3) if v is not None else None for v in ln['g']],
                       ll=[[round(v, 8) for v in geo.en_to_ll(p[0], p[1])] for p in ln['pts']]))
    _write_atomic(os.path.join(out, 'data', 'obstacles.js'),
                  'window.RV_OBST = ' + json.dumps({'layers': layers, 'lines': ol, 'origin': {'E': E0, 'N': N0},
                                                    'defaultDepth': float(default_depth),
                                                    'source': os.path.basename(path)}, ensure_ascii=False) + ';\n')
    msg = '지장물 %d개 → %s 조각 %d개 · 종류 %d%s' % (n_raw, '매핑 범위 안' if M else '경로 200 m 안', len(ol), len(kinds), (' · ' + note) if note else '')
    log(msg)
    return len(ol), msg


def _mapping(out):
    """3D 매핑 프로젝트 (data/project.js 없음 · data/mapping.js 있음) 면 RV_MAPPING · 아니면 None."""
    if os.path.exists(os.path.join(out, 'data', 'project.js')):
        return None
    p = os.path.join(out, 'data', 'mapping.js')
    return _read_js(p, 'RV_MAPPING') if os.path.exists(p) else None


def _mapping_clip_ground(out, M, lines, pad=30.0):
    """3D 매핑 프로젝트: 매핑 범위 (+pad) 안 덩어리마다 한 줄 · 땅 높이 g = 매끈한 땅 (DTM · 관로와 같음)."""
    import numpy as np
    from .mapping import ground_sampler
    x0, y0, x1, y1 = M['bbox']
    inside = lambda p: x0 - pad <= p[0] <= x1 + pad and y0 - pad <= p[1] <= y1 + pad   # noqa: E731
    cut = []
    for ln in lines:
        run = []
        for p in ln['pts']:
            if inside(p):
                run.append(p)
            else:
                if len(run) >= 2:
                    cut.append(dict(ln, pts=run))
                run = []
        if len(run) >= 2:
            cut.append(dict(ln, pts=run))
    try:
        g = ground_sampler(out)
    except Exception:                                 # noqa: BLE001 — 땅 자료가 하나도 없는 옛 프로젝트 (1126 검토)
        g = None
    for ln in cut:
        gz = g([p[0] for p in ln['pts']], [p[1] for p in ln['pts']]) if g else [np.nan] * len(ln['pts'])
        ln['g'] = [float(v) if np.isfinite(v) else None for v in gz]
    return cut


# ---------------------------------------------------------------- 고친 관로 → SHP (중심선)

# 가시설 (RV_PIPES.lines[i].shore = [{a: 시작 측점 m, b: 끝 측점 m, t: 종류}] · 측점 = 줄 첫 점부터 평면 길이)
SHORE_NAMES = {'panel': '조립식간이흙막이', 'hpile': '토류벽흙막이', 'sheet': '시트파일'}
MAT_NAMES = {'ci': '주철관', 'steel': '강관', 'plastic': '비금속관'}      # RV_PIPES.lines[i].mat (보기 화면에서 고름)


def guess_mat(text):
    """관 이름 · 묶음 글자로 관종 짐작 (보기 화면과 같은 규칙) · 모르면 주철관."""
    import re
    u = str(text or '').upper()
    if re.search(r'비금속|폴리|PVC|HDPE|GRP|(?<![A-Z])PE(?![A-Z])', u):
        return 'plastic'
    if re.search(r'강관|STEEL|STPW|STS|(?<![A-Z])SP(?![A-Z])', u):
        return 'steel'
    return 'ci'


def _cut_at(xy, s_marks):
    """평면 줄 xy 를 측점들에서 자른 조각들 → [(s0, s1, [[x, y], …])]. 측점은 줄 안 값만."""
    seg = [((xy[k + 1][0] - xy[k][0]) ** 2 + (xy[k + 1][1] - xy[k][1]) ** 2) ** 0.5 for k in range(len(xy) - 1)]
    L = sum(seg)
    cuts = sorted({0.0, L} | {min(max(float(s), 0.0), L) for s in s_marks})

    def at(s):                                   # 측점 s 의 자리 · 그 앞 꼭짓점 번호
        acc = 0.0
        for k, d in enumerate(seg):
            if s <= acc + d or k == len(seg) - 1:
                t = 0.0 if d <= 0 else min(max((s - acc) / d, 0.0), 1.0)
                return [xy[k][0] + (xy[k + 1][0] - xy[k][0]) * t, xy[k][1] + (xy[k + 1][1] - xy[k][1]) * t], k
            acc += d
        return list(xy[-1]), len(xy) - 2

    out = []
    for s0, s1 in zip(cuts[:-1], cuts[1:]):
        if s1 - s0 < 1e-6:
            continue
        p0, k0 = at(s0)
        p1, k1 = at(s1)
        acc, mid = 0.0, []
        for k, d in enumerate(seg):              # 두 측점 사이 꼭짓점
            acc += d
            if s0 + 1e-6 < acc < s1 - 1e-6:
                mid.append(list(xy[k + 1]))
        out.append((s0, s1, [p0] + mid + [p1]))
    return out


def export_pipes_shp(out, shp_path):
    """data/pipes.js 의 원래 꼭짓점(ctrl · 고친 점 포함)만으로 중심선 SHP. → 선 수.
    가시설 구간은 그 자리에서 선을 끊고 SHORING 속성에 가시설 이름 (1124 JACK)."""
    from .shp import write_lines
    from pyproj import CRS
    D = _read_js(os.path.join(out, 'data', 'pipes.js'), 'RV_PIPES')
    M = _mapping(out)
    if M:                                        # 3D 매핑 프로젝트: 관로 원점 (없으면 매핑 원점) · 매핑 좌표계
        o = D.get('origin') or {}
        P = {'crs': M.get('crs', 'EPSG:5186')}
        E0, N0 = float(o.get('E', M['origin'][0])), float(o.get('N', M['origin'][1]))
    else:
        P = _read_js(os.path.join(out, 'data', 'project.js'), 'RV_PROJECT')
        E0, N0 = float(P['origin']['E']), float(P['origin']['N'])
    names = [l.get('name', '') for l in D.get('layers', [])]
    lines = []
    for ln in D.get('lines', []):
        pts = ln['pts']
        idx = ln.get('ctrl') or list(range(len(pts)))
        xy = [[pts[i][0] + E0, pts[i][1] + N0] for i in idx if 0 <= i < len(pts)]
        if len(xy) < 2:
            continue
        dia = ln.get('dia')
        shore = [q for q in (ln.get('shore') or [])
                 if isinstance(q, dict) and isinstance(q.get('a'), (int, float)) and isinstance(q.get('b'), (int, float))]
        marks = [v for q in shore for v in (q['a'], q['b'])]
        for s0, s1, part in _cut_at(xy, marks):
            mid = 0.5 * (s0 + s1)
            q = next((q for q in shore if min(q['a'], q['b']) - 1e-6 <= mid <= max(q['a'], q['b']) + 1e-6), None)
            lines.append({'pts': part, 'rec': {
                'DIA_MM': round(dia * 1000) if dia else None,
                'NAME': ln.get('label') or '',
                'GROUP': names[ln['layer']] if isinstance(ln.get('layer'), int) and ln['layer'] < len(names) else '',
                'LEN_M': s1 - s0,
                'PIPE_MAT': MAT_NAMES[ln['mat'] if ln.get('mat') in MAT_NAMES else guess_mat(
                    '%s %s' % (ln.get('label') or '',
                               names[ln['layer']] if isinstance(ln.get('layer'), int) and ln['layer'] < len(names) else ''))],
                'SHORING': SHORE_NAMES.get(q.get('t'), str(q.get('t') or '')) if q else ''}})
    fields = [('DIA_MM', 'N', 6, 0), ('NAME', 'C', 60, 0), ('GROUP', 'C', 30, 0), ('LEN_M', 'N', 12, 2),
              ('PIPE_MAT', 'C', 20, 0), ('SHORING', 'C', 30, 0)]
    wkt = CRS.from_user_input(P.get('crs', 'EPSG:5186')).to_wkt('WKT1_ESRI')
    return write_lines(shp_path, lines, fields, wkt, 'cp949')


def main(argv=None):
    ap = argparse.ArgumentParser(description='로드뷰 폴더에 관로만 새로 넣기')
    ap.add_argument('out')
    ap.add_argument('pipes')
    ap.add_argument('--dia-field')
    ap.add_argument('--label-field')
    ap.add_argument('--depth', type=float)
    ap.add_argument('--layers')
    ap.add_argument('--default-dia', type=float, default=200.0)
    a = ap.parse_args(argv)
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass
    update(a.out, a.pipes, a.dia_field, a.label_field, a.depth, a.layers, a.default_dia)
    return 0


if __name__ == '__main__':
    sys.exit(main())
