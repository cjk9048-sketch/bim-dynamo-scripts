"""점 찍어 맞추기: 먼 곳 짝이 있으면 남북 · 동서 기울기도 푸는지 (1124 JACK «먼 곳 안 맞음»).

알려진 맞춤(정답)으로 짝(사진 방향 p · 지도 점)을 계산해 calib.js 에 넣고, 보기 화면에서 [점 찍어 맞추기] 를 켜
(켤 때 다시 풂) 정답을 다시 찾는지 본다. 높이 타일은 빼서 짝의 땅 높이 = 사진의 groundZ.
    python check_tilt.py --src <syn_align 폴더> --work <빈 시험 폴더>
"""
import argparse
import json
import math
import os
import pathlib
import shutil
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cdp import Edge  # noqa: E402

R_ = {'ok': True}


def report(name, ok, text=''):
    R_['ok'] &= bool(ok)
    print('   %-46s %s  %s' % (name, text, '통과' if ok else '실패'))


def rot64(c):  # 보기 화면과 같은 R = Rz(dyaw)·Rx(dpitch)·Ry(droll) (행 우선)
    a, b, g = (math.radians(c[k]) for k in ('dyaw', 'dpitch', 'droll'))
    ca, sa, cb, sb, cg, sg = math.cos(a), math.sin(a), math.cos(b), math.sin(b), math.cos(g), math.sin(g)
    Rz = np.array([[ca, sa, 0], [-sa, ca, 0], [0, 0, 1]])
    Rx = np.array([[1, 0, 0], [0, cb, -sb], [0, sb, cb]])
    Ry = np.array([[cg, 0, sg], [0, 1, 0], [-sg, 0, cg]])
    return Rz @ Rx @ Ry


def read_js(p):
    t = open(p, encoding='utf-8').read()
    return json.loads(t[t.index('{'):t.rindex('}') + 1])


def make_case(src, work, truth, pts):
    shutil.rmtree(work, ignore_errors=True)
    shutil.copytree(src, work, ignore=shutil.ignore_patterns('dem.js', 'calib.js', '_*'))
    P = read_js(os.path.join(work, 'data', 'project.js'))
    s = P['shots'][0]
    R = rot64(truth)
    pairs = []
    for d, ang in pts:
        ge, gn = s['e'] + d * math.sin(math.radians(ang)), s['n'] + d * math.cos(math.radians(ang))
        v = np.array([ge - s['e'] - truth['dx'], gn - s['n'] - truth['dy'], s['groundZ'] - (s['z'] + truth['dz'])])
        p = R.T @ (v / np.linalg.norm(v))
        pairs.append({'shot': s['id'], 'p': [round(float(x), 7) for x in p], 'e': round(ge, 3), 'n': round(gn, 3)})
    cal = {'videos': {s['video']: {'dx': 0, 'dy': 0, 'dz': 0, 'dyaw': 0, 'dpitch': 0, 'droll': 0, 'pairs': pairs}}}
    with open(os.path.join(work, 'data', 'calib.js'), 'w', encoding='utf-8') as f:
        f.write('window.RV_CALIB = ' + json.dumps(cal) + ';\n')
    return s


def solve_in_viewer(ed, work, s):
    url = pathlib.Path(work, '로드뷰.html').resolve().as_uri() + '?shot=%s' % s['id']
    ed.goto(url)
    ed.wait_js("document.querySelectorAll('#calRows input').length >= 5", 60)
    ed.js('localStorage.clear()')
    ed.goto(url)
    ed.wait_js("document.querySelectorAll('#calRows input').length >= 5", 60)
    ed.pump(0.5)
    ed.js("if (!document.getElementById('calPane').classList.contains('open')) document.getElementById('calHead').click()")
    ed.js("document.getElementById('alGo').click()")  # 켤 때 짝으로 다시 풂
    ed.pump(1.0)
    vals = ed.js("JSON.stringify([...document.querySelectorAll('#calRows input')].map((i) => Number(i.value)))")
    ed.js("document.getElementById('alGo').click()")
    v = json.loads(vals)
    keys = ['dx', 'dy', 'dz', 'dyaw', 'dpitch', 'droll'][:len(v)]
    return dict(zip(keys, v))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--src', required=True)
    ap.add_argument('--work', required=True)
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except (AttributeError, ValueError):
        pass
    truth = {'dx': 1.2, 'dy': -0.8, 'dz': 0.0, 'dyaw': 3.0, 'dpitch': 0.8, 'droll': -0.6}
    far_pts = [(15, 30), (25, 150), (60, 260), (110, 340)]
    near_pts = [(12, 30), (20, 150), (18, 260), (25, 340)]
    with Edge(os.path.join(a.work + '_prof'), (1600, 900)) as ed:
        print('== 먼 곳 짝 포함 (15 · 25 · 60 · 110 m) → 기울기까지')
        s = make_case(a.src, a.work, truth, far_pts)
        got = solve_in_viewer(ed, a.work, s)
        d = {k: round(got[k] - truth[k], 3) for k in got}
        report('동 · 북 · 방향 맞음', abs(d['dx']) <= 0.05 and abs(d['dy']) <= 0.05 and abs(d['dyaw']) <= 0.05, json.dumps(d))
        report('남북 · 동서 기울기 찾음 (±0.05°)', abs(d.get('dpitch', 9)) <= 0.05 and abs(d.get('droll', 9)) <= 0.05,
               'got %s' % json.dumps({k: got.get(k) for k in ('dpitch', 'droll')}))
        print('== 가까운 짝만 (25 m 안) → 기울기는 그대로 0')
        flat = dict(truth, dpitch=0.0, droll=0.0)
        s = make_case(a.src, a.work, flat, near_pts)
        got2 = solve_in_viewer(ed, a.work, s)
        report('기울기 안 품 (0 그대로)', got2.get('dpitch') == 0 and got2.get('droll') == 0, json.dumps(got2))
        errs = [e for e in ed.exceptions]
        report('JS 오류', not errs, '%d 줄' % len(errs))
    print('전체:', '통과' if R_['ok'] else '실패')
    return 0 if R_['ok'] else 1


if __name__ == '__main__':
    sys.exit(main())
