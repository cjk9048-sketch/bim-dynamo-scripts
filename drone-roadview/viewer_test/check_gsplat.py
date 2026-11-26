"""가우시안 스플래팅 변환 시험 (gsplat.py · 엔진 · 그래픽카드 없이): SH 돌림 · 사원수 · 도면 좌표 변환 · 표준 PLY 왕복 · 보기 자료 꼴 · 정사영상.

    python check_gsplat.py
"""
import math
import os
import sys
import tempfile

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from roadview import gsplat  # noqa: E402

OK = True


def report(name, ok, text=''):
    global OK
    print('   %-40s %s  %s' % (name, text, '통과' if ok else '실패'))
    OK &= bool(ok)


def rot(ax, th):
    ax = np.asarray(ax, float) / np.linalg.norm(ax)
    K = np.array([[0, -ax[2], ax[1]], [ax[2], 0, -ax[0]], [-ax[1], ax[0], 0]])
    return np.eye(3) + math.sin(th) * K + (1 - math.cos(th)) * K @ K


def qmat(q):
    w, x, y, z = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y)],
                     [2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x)],
                     [2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y)]])


def main():
    rng = np.random.default_rng(5)
    # SH 돌림: f'(d) = f(Rᵀ d)
    for th in (0.003, 0.4, 2.0):
        Rg = rot(rng.normal(size=3), th)
        A = gsplat.sh_rotation(Rg)
        c = rng.normal(size=15); D = rng.normal(size=(64, 3)); D /= np.linalg.norm(D, axis=1, keepdims=True)
        e = np.abs(gsplat._sh_basis(D @ Rg) @ c - gsplat._sh_basis(D) @ (A @ c)).max()
        report('SH 돌림 %.3f rad' % th, e < 1e-9, '최대 차 %.1e' % e)
        q = gsplat._quat_of(Rg)
        report('사원수 %.3f rad' % th, np.abs(qmat(q) - Rg).max() < 1e-9)
    # 가짜 Brush PLY (이름 차례 · 모델 틀) → 도면 틀 (닮음 s · R · t 와 가짜 to_enh = 선형) → 공분산 · 색이 맞게 돌았는지
    n = 500
    d = tempfile.mkdtemp(prefix='rv_gs_')
    X = (rng.normal(size=(n, 3)) * 20).astype(np.float32).astype(np.float64)     # PLY 에 들어가는 float32 값 그대로
    cols = {'x': X[:, 0], 'y': X[:, 1], 'z': X[:, 2], 'opacity': rng.normal(size=n) + 2,
            'scale_0': np.log(rng.uniform(0.01, 0.3, n)), 'scale_1': np.log(rng.uniform(0.01, 0.3, n)), 'scale_2': np.log(rng.uniform(0.01, 0.3, n))}
    q = rng.normal(size=(n, 4)); q /= np.linalg.norm(q, axis=1, keepdims=True)
    for i in range(4):
        cols['rot_%d' % i] = q[:, i]
    for i in range(3):
        cols['f_dc_%d' % i] = rng.normal(size=n)
    for i in range(45):
        cols['f_rest_%d' % i] = rng.normal(size=n) * 0.2
    names = sorted(cols)                                  # Brush 처럼 이름 차례
    head = ['ply', 'format binary_little_endian 1.0', 'element vertex %d' % n] + ['property float %s' % k for k in names] + ['end_header']
    src = os.path.join(d, 'brush.ply')
    with open(src, 'wb') as fp:
        fp.write(('\n'.join(head) + '\n').encode())
        np.stack([cols[k] for k in names], 1).astype('<f4').tofile(fp)
    s = 1.37; R = rot([0.2, -0.3, 1.0], 0.7); t = np.array([1000.0, 2000.0, 30.0])
    Rg_true = rot([0, 0, 1], 0.01)
    to_enh = lambda P: tuple((P @ Rg_true.T * 0.9996).T)      # noqa: E731 — 가짜 «ECEF → 도면» (선형 · 돌림 + 축척)
    origin = [1000.0, 2000.0, 30.0]
    out, st = gsplat.to_world(src, s, R, t, to_enh, origin, log=lambda m: None)
    J = 0.9996 * Rg_true @ (s * R)
    report('도면 위치', np.abs(np.stack([out['x'] + origin[0], out['y'] + origin[1], out['z']], 1) - (X @ J.T + 0.9996 * (t @ Rg_true.T))).max() < 1e-6)
    report('배율 (닮음 × 축척)', abs(st['scale'] - 1.37 * 0.9996) < 1e-9, '%.6f' % st['scale'])
    k = 7
    S0 = qmat(q[k]) @ np.diag(np.exp(2 * np.array([cols['scale_%d' % i][k] for i in range(3)]))) @ qmat(q[k]).T
    q2 = np.array([out['rot_%d' % i][k] for i in range(4)]); q2 /= np.linalg.norm(q2)
    S1 = qmat(q2) @ np.diag(np.exp(2 * np.array([out['scale_%d' % i][k] for i in range(3)]))) @ qmat(q2).T
    report('공분산 = J Σ Jᵀ', np.abs(S1 - J @ S0 @ J.T).max() < 1e-6 * np.abs(S1).max())
    Rg = J / st['scale']
    D = rng.normal(size=(32, 3)); D /= np.linalg.norm(D, axis=1, keepdims=True)
    c0 = np.array([cols['f_rest_%d' % j][k] for j in range(15)]); c1 = np.array([out['f_rest_%d' % j][k] for j in range(15)])
    report('보는 방향 색 (SH) 같이 돎', np.abs(gsplat._sh_basis(D) @ c1 - gsplat._sh_basis(D @ Rg) @ c0).max() < 1e-6)
    # 표준 PLY 왕복 · 차례
    dst = os.path.join(d, '가우시안.ply')
    gsplat.write_ply(dst, out, comments=('offset_x 1000',))
    g2, n2 = gsplat.read_ply(dst)
    hd = open(dst, 'rb').read(4000).split(b'end_header')[0].decode().splitlines()
    props = [l.split()[2] for l in hd if l.startswith('property')]
    report('표준 PLY 차례 (x y z nx ny nz f_dc f_rest opacity scale rot)', props == gsplat._std_props(45) and n2 == n)
    report('PLY 왕복 값', all(np.abs(g2[kk] - np.asarray(out[kk], np.float32)).max() == 0 for kk in ('x', 'opacity', 'rot_3', 'f_rest_44')))
    # 보기 자료 32 B
    nw, lo, hi = gsplat.write_web(out, origin[2], os.path.join(d, 'splats.bin'), min_alpha=0)
    b = np.fromfile(os.path.join(d, 'splats.bin'), dtype=[('p', '<f4', 3), ('s', '<f4', 3), ('c', 'u1', 4), ('r', 'u1', 4)])
    report('보기 자료 32 B · 개수', os.path.getsize(os.path.join(d, 'splats.bin')) == 32 * nw and nw == n)
    j = int(np.argmin(np.abs(b['p'][:, 0] - out['x'][k])))
    report('보기 자료 위치 (표고 − 원점)', abs(b['p'][j, 2] - (out['z'][k] - origin[2])) < 1e-3)
    # 정사영상 (작게)
    gsplat.write_dom(out, origin, d, 0.5, 'EPSG:5186', log=lambda m: None)
    from PIL import Image
    im = np.asarray(Image.open(os.path.join(d, '가우시안_정사.tif')))
    report('정사영상 RGBA · 칠한 칸 있음', im.shape[2] == 4 and (im[..., 3] > 0).mean() > 0.01, '%d×%d' % (im.shape[1], im.shape[0]))
    ok, why = gsplat.available()
    report('available() = (bool, str)', isinstance(ok, bool) and isinstance(why, str) and why, why)
    import shutil
    shutil.rmtree(d, ignore_errors=True)
    print('결과:', '통과' if OK else '실패')
    return 0 if OK else 1


if __name__ == '__main__':
    sys.exit(main())
