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
                       pts=pts, g=[round(v, 3) if v is not None else None for v in ln['g']], ll=ll))
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
