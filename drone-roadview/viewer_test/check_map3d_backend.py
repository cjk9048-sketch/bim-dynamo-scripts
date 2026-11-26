"""3D 매핑 프로젝트에서 관로 SHP 내보내기 · 지장물 넣기 (roadview.pipes_update · 프로그램 Api 가 부르는 길) 시험.

    python -I check_map3d_backend.py --out <3D 매핑 결과 폴더 (data/mapping.js · pipes.js)>

  관로 SHP: 원래 꼭짓점 (ctrl) + 매핑 원점 = SHP 좌표 (1 mm) · 가시설 구간에서 끊김 · 관종 · .prj = 매핑 좌표계
  지장물: SHP (종류 · 관경 · 깊이) → data/obstacles.js 원점 = 매핑 원점 · 매핑 범위 안만 · 땅 g = 매끈한 땅 (DTM)
  (시험이 바꾼 data 파일은 끝나면 되돌림)
"""
import argparse
import json
import math
import os
import shutil
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from roadview import pipes_update as pu  # noqa: E402
from roadview import mapping  # noqa: E402
from roadview.shp import read_shp_lines, read_dbf, write_lines  # noqa: E402

OK = []


def report(name, ok, text=''):
    print('   %-40s %s  %s' % (name, text, '통과' if ok else '실패'))
    OK.append(bool(ok))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', required=True)
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    out = os.path.abspath(a.out)
    d = os.path.join(out, 'data')
    keep = {f: open(os.path.join(d, f), encoding='utf-8').read() for f in ('pipes.js', 'obstacles.js') if os.path.exists(os.path.join(d, f))}
    tmp = tempfile.mkdtemp(prefix='ug3d_be_')
    try:
        M = pu._read_js(os.path.join(d, 'mapping.js'), 'RV_MAPPING')
        D = pu._read_js(os.path.join(d, 'pipes.js'), 'RV_PIPES')
        D['lines'][0]['shore'] = [{'a': 10.0, 'b': 20.0, 't': 'sheet'}]
        D['lines'][0]['mat'] = 'steel'
        open(os.path.join(d, 'pipes.js'), 'w', encoding='utf-8').write('window.RV_PIPES = ' + json.dumps(D, ensure_ascii=False) + ';\n')
        print('== 관로 SHP')
        shp = os.path.join(tmp, '관로.shp')
        n = pu.export_pipes_shp(out, shp)
        got = read_shp_lines(shp)
        fields, recs = read_dbf(shp)
        o = D['origin']
        want0 = [[D['lines'][0]['pts'][k][0] + o['E'], D['lines'][0]['pts'][k][1] + o['N']] for k in D['lines'][0]['ctrl']]
        first = [p for i, p in got if recs[i] and recs[i].get('NAME') == D['lines'][0]['label']]
        allp = [q for part in first for q in part]
        err = max(min(math.hypot(w[0] - q[0], w[1] - q[1]) for q in allp) for w in want0)
        report('원래 꼭짓점 = SHP 점 (매핑 원점)', err < 0.002, '%.4f m · 선 %d' % (err, n))
        sh = [r for r in recs if r and r.get('SHORING')]
        report('가시설 구간에서 끊김 · 이름', len(sh) == 1 and abs(float(sh[0]['LEN_M']) - 10.0) < 0.01, str([(r['SHORING'], r['LEN_M']) for r in sh]))
        report('관종 (강관)', any(r and r.get('PIPE_MAT') == '강관' for r in recs))
        prj = open(shp[:-4] + '.prj', encoding='utf-8', errors='ignore').read()
        report('.prj = 매핑 좌표계', 'Korea_2000' in prj or '5186' in prj or 'Central' in prj, prj[:60])
        print('== 지장물 넣기')
        x0, y0, x1, y1 = M['bbox']
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        ol = [{'pts': [[cx - 20, cy - 5], [cx + 20, cy + 5]], 'rec': {'TYPE': '가스', 'DIA': 100, 'DEP': 1.0}},
              {'pts': [[cx - 10, cy + 10], [cx + 500, cy + 10]], 'rec': {'TYPE': '통신', 'DIA': 0, 'DEP': 0.8}}]   # 둘째는 범위 밖으로 길게
        oshp = os.path.join(tmp, '지장물.shp')
        from pyproj import CRS
        write_lines(oshp, ol, [('TYPE', 'C', 20, 0), ('DIA', 'N', 6, 0), ('DEP', 'N', 8, 2)], CRS.from_user_input(M.get('crs', 'EPSG:5186')).to_wkt('WKT1_ESRI'), 'cp949')
        n2, msg = pu.update_obstacles(out, oshp, 'TYPE', 'DIA', 'DEP', None, 1.0, log=lambda s: None)
        O = pu._read_js(os.path.join(d, 'obstacles.js'), 'RV_OBST')
        report('obstacles.js 원점 = 매핑 원점', abs(O['origin']['E'] - M['origin'][0]) < 1e-6 and abs(O['origin']['N'] - M['origin'][1]) < 1e-6, msg)
        pad = 30
        inside = all(x0 - pad <= p[0] + O['origin']['E'] <= x1 + pad and y0 - pad <= p[1] + O['origin']['N'] <= y1 + pad for l in O['lines'] for p in l['pts'])
        report('매핑 범위 안만 · 줄 2', inside and n2 == 2, '%d 줄' % n2)
        gs = mapping.ground_sampler(out)
        l0 = O['lines'][0]
        gz = gs([p[0] + O['origin']['E'] for p in l0['pts']], [p[1] + O['origin']['N'] for p in l0['pts']])
        dg = max(abs(a_ - b_) for a_, b_ in zip(l0['g'], gz))
        report('땅 g = 매끈한 땅 (DTM)', dg < 0.002, '%.4f m' % dg)
        report('관경 · 깊이 · 종류', l0['dia'] == 0.1 and l0['depth'] == 1.0 and O['layers'][0]['name'] == '가스')
    finally:
        for f, t in keep.items():
            open(os.path.join(d, f), 'w', encoding='utf-8').write(t)
        if 'obstacles.js' not in keep and os.path.exists(os.path.join(d, 'obstacles.js')):
            os.remove(os.path.join(d, 'obstacles.js'))
        shutil.rmtree(tmp, ignore_errors=True)
    print('== %s' % ('통과' if all(OK) else '실패 %d' % OK.count(False)))
    return 0 if all(OK) else 1


if __name__ == '__main__':
    sys.exit(main())
