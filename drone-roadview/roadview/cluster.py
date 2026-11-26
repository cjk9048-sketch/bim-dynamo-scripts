"""함께 처리 — 옆 PC 들과 3D 매핑 점구름을 나눠 처리 (JACK 1126 «회사라 옆에 컴퓨터가 많다»).

공유 폴더(회사 NAS 등) 하나 = 작업 게시판 · 네트워크 포트 · 방화벽 설정 없이 폴더만 씀.
  내 PC (조정자 · mapping.py 점구름 단계): 사진을 조각으로 나눠 일감을 올리고 · 자기도 조각을 처리 · 다 모이면 합치기만
  옆 PC (도우미 · rv_cli helper --board 공유폴더 · 앱 «도우미 모드»): 일감을 집어 깊이 지도를 만들어 올림

나누는 단위 = 깊이 지도 (점구름 시간의 9할 · 80장 보통 9분 24초 중 8분 40초 · 합치기 45초). 1126 실험 (20 · 80장):
  - OpenMVS 는 깊이 지도 파일(depthNNNN.dmap · NNNN = COLMAP 사진 번호)이 있으면 그 사진을 건너뜀 → 조각 밖 사진에
    0바이트 파일을 깔면 이웃 고르기 · 깊이 범위는 혼자 처리와 똑같고 조각 사진만 계산
    (이웃 목록 텍스트 파일 --view-neighbors-file 은 점수 · 깊이 범위를 잃어 결과가 바뀜 → 안 씀)
  - 깊이 지도를 다 모은 뒤 --geometric-iters 0 이면 합치기만 (깊이 지도가 같으면 점구름이 바이트까지 같음)
  - 조각 이웃 사진(고리)은 사진만 빌리고 계산 안 함 → 덤 일 없음 · 조각 가장자리는 기하 맞춤 이웃이 조금 모자람
    (20장 2조각 극단 시험: 점 98.9 % · 50 cm 칸 높이 차 가운데 1.4 cm — 혼자 처리를 두 번 돌린 차이가 1.1 cm)
  - 필요 없는 사진 = 같은 크기 회색 대역 사진 (이웃 점수가 사진 크기를 씀 · 파일은 몇 KB)
  - 이웃 목록 = 모든 사진에 0바이트 깊이 지도를 깐 1초 실행(--fusion-mode 1)이 남긴 .mvs 에서 읽음
도우미가 없거나 · 죽거나(심장박동 끊김) · 실패하면 그 조각은 내 PC 가 다시 → 결과는 늘 나옴.
  1126 끝까지 시험 (80장 · 조각 4 · 같은 PC 도우미 2 · 같은 카메라 판의 혼자 처리와 견줌): 점 96.4 % (1320 px) · 96.5 % (660 px)
  · DSM 50 cm 칸 높이 차 가운데 1.1 cm (함께 두 번 사이 0.6 cm) · 도우미를 25 % 에서 죽여도 190초 뒤 내 PC 가 그 조각을 다시 해 끝까지
  · 진행률 = 엔진 자체 기록(DensifyPointCloud-*.log) — 표준출력은 엔진이 끝날 때까지 비어 있음

게시판 꼴:
  helpers/<id>.json              도우미 심장박동 (10초마다 · PC 이름 · 코어 · 상태 · 지금 일감 · 처리 수)
  runs/<판>/run.json             조정자 심장박동 (끊기면 도우미가 그 판을 버림)
  runs/<판>/scene.mvs · sizes.json · images/   장면 · 사진 크기 · 원격 조각에 필요한 왜곡 편 사진 (한 번만 올림)
  runs/<판>/jobs/<n>/job.json    일감 (계산할 사진 번호 · 빌릴 사진 이름 · 엔진 옵션 · 엔진 판) — 사진을 다 올린 뒤에 씀
                                 (도우미는 엔진 판이 같을 때만 집음 · 판이 다르면 깊이 지도 꼴 · 값이 다를 수 있음)
                    lock/        집은 PC (mkdir 은 원자적 → 둘이 못 집음) · lock/owner.json
                    hb_<id>.json 일감 심장박동 (진행 · 따로 실이 5초마다 → 큰 복사 중에도 안 끊김)
                    res_<id>/    결과 깊이 지도 + done.json (마지막에 씀) · fail.json
                    collected.json  조정자가 거둠 (늦게 끝난 같은 일감은 멈춤)

    rv_cli helper --board 공유폴더 [--threads N] [--parent-pid 앱]
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import shutil
import socket
import struct
import subprocess
import sys
import threading
import time
import uuid

NO_WINDOW = 0x08000000 if os.name == 'nt' else 0
PROTO = 1                  # 게시판 꼴 판 (다르면 서로 안 씀)
HB_SEC = 10                # 도우미 심장박동 간격
ALIVE_SEC = 45             # 이 안에 심장박동이 있으면 살아 있음 (윈도 공유 폴더 정보 캐시 10초 포함)
STALE_SEC = 150            # 일감 심장박동이 이만큼 끊기면 내 PC 가 다시
RUN_STALE_SEC = 240        # 조정자 판이 이만큼 안 움직이면 도우미는 그 판을 버림
RUN_GONE_SEC = 30          # run.json 이 이만큼 연달아 없으면 도우미는 «판 끝남» (공유 폴더가 잠깐 끊긴 것과 가름)
WAIT_HELPER_SEC = 1800     # 내 PC 가 실패한 조각을 살아 있는 도우미가 하는 중이면 이만큼까지 기다림
BELOW_NORMAL = 0x4000 if os.name == 'nt' else 0   # 도우미 엔진 = 낮은 우선순위 (옆 PC 쓰는 사람 일을 안 막음)
MIN_PHOTOS = 30            # 이보다 적으면 혼자 (나누는 덤이 더 큼)
MIN_PER_CHUNK = 20         # 조각 하나 최소 사진 수 (작으면 가장자리 사진이 많아 기하 맞춤 이웃이 모자람)
VIEWS = 8                  # OpenMVS --number-views 기본 (이웃 앞 8장 = 고리)
HOST = socket.gethostname()
SAME_HOST = os.environ.get('RV_CLUSTER_SAMEHOST') == '1'      # 시험용: 같은 PC 도우미도 셈 (평소엔 내 PC 도우미는 내 판을 안 집음)
LOCAL = os.path.join(os.environ.get('LOCALAPPDATA', os.path.expanduser('~')), 'DH.Roadview', 'helper')


def _log(msg):
    print(time.strftime('%H:%M:%S ') + msg, flush=True)


# ---------------------------------------------------------------- 게시판 공통
def write_json(path, obj):
    tmp = '%s.%s.tmp' % (path, uuid.uuid4().hex[:6])
    try:
        with open(tmp, 'w', encoding='utf-8') as fp:
            json.dump(obj, fp, ensure_ascii=False)
        os.replace(tmp, path)                      # 다른 PC 가 읽는 중이면 공유 위반으로 실패할 수 있음 → 부른 쪽이 다음에 다시
    except OSError:
        try:
            os.remove(tmp)                         # 찌꺼기 안 남김
        except OSError:
            pass
        raise


def read_json(path):
    try:
        with open(path, encoding='utf-8') as fp:
            return json.load(fp)
    except (OSError, ValueError):
        return None


def board_now(folder, tag='now'):
    """공유 폴더 시계 — 파일을 써 보고 그 수정 시각 (PC 마다 시계가 달라도 같은 잣대로 심장박동 나이를 잼).
    이름 = PC 이름 + pid + 실 (같은 PC 의 도우미 · 조정자 · 앱이 한 파일을 같이 쓰면 공유 위반) · 잰 뒤 지움 (찌꺼기 없게)."""
    p = os.path.join(folder, '.%s_%s_%d_%x' % (tag, HOST, os.getpid(), threading.get_ident()))
    with open(p, 'w') as fp:
        fp.write('.')
    t = os.path.getmtime(p)
    try:
        os.remove(p)
    except OSError:
        pass
    return t


def board_ok(board):
    """(쓸 수 있음, 이유)."""
    if not board:
        return False, '공유 폴더 안 정함'
    try:
        os.makedirs(os.path.join(board, 'helpers'), exist_ok=True)
        os.makedirs(os.path.join(board, 'runs'), exist_ok=True)
        board_now(os.path.join(board, 'helpers'))
        return True, ''
    except OSError as ex:
        return False, '공유 폴더 못 씀 (%s)' % ex


def list_helpers(board):
    """[{id, host, cores, state, job, done, alive, age}] — 살아 있음 = 공유 폴더 시계로 ALIVE_SEC 안."""
    d = os.path.join(board, 'helpers')
    try:
        now = board_now(d)
    except OSError:
        return []
    out = []
    for f in glob.glob(os.path.join(d, '*.json')):
        h = read_json(f)
        if not h:
            continue
        try:
            age = now - os.path.getmtime(f)
        except OSError:
            continue
        if age > 3600:                             # 한 시간 넘은 것 = 꺼진 도우미 찌꺼기
            try:
                os.remove(f)
            except OSError:
                pass
            continue
        h['age'] = round(age, 1)
        h['alive'] = age < ALIVE_SEC and h.get('proto') == PROTO and h.get('state') != 'off'
        out.append(h)
    return sorted(out, key=lambda h: (not h['alive'], h.get('host', '')))


def _workers(board):
    return [h for h in list_helpers(board) if h['alive'] and (SAME_HOST or h.get('host') != HOST)]


# ---------------------------------------------------------------- OpenMVS 파일
def mvs_images(path):
    """MVSI(.mvs · OpenMVS 2.4 판 7) → [(사진 이름, COLMAP 번호, 이웃 순번 목록)] 장면 순서."""
    with open(path, 'rb') as fp:
        b = fp.read()
    if b[:4] != b'MVSI':
        raise ValueError('MVSI 아님')
    ver = struct.unpack_from('<I', b, 4)[0]
    if ver != 7:
        raise ValueError('MVSI 판 %d (7 만 읽음)' % ver)
    o = 12

    def u64():
        nonlocal o
        v = struct.unpack_from('<Q', b, o)[0]
        o += 8
        return v

    def u32():
        nonlocal o
        v = struct.unpack_from('<I', b, o)[0]
        o += 4
        return v

    def text():
        nonlocal o
        n = u64()
        v = b[o:o + n].decode('utf-8', 'replace')
        o += n
        return v
    for _ in range(u64()):                         # 플랫폼
        text()
        for _ in range(u64()):                     # 카메라: 이름 · 밴드 · 가로 세로 · K · R · C
            text(); text()
            o += 8 + 8 * (9 + 9 + 3)
        npose = u64()                              # (o += u64() 는 o 를 먼저 읽어 틀림)
        o += npose * 8 * (9 + 3)
    out = []
    for _ in range(u64()):
        name = text(); text()                      # 이름 · 마스크
        u32(); u32(); u32()                        # 플랫폼 · 카메라 · 자세
        iid = u32()
        o += 12                                    # 깊이 값 3개
        nn = u64()
        nbs = [struct.unpack_from('<I', b, o + 24 * j)[0] for j in range(nn)]
        o += 24 * nn                               # 이웃 = 순번 u32 · 점 u32 · scale · angle · area · score
        out.append((name, iid, nbs))
    return out


_MVS_VER = {}


def mvs_version(mvs):
    """'OpenMVS x64 v2.4.0' · 모르면 '' — OpenMVS 는 판을 화면이 아니라 실행 폴더의 *.log 첫 줄에 씀 (--help 도 기록을 남김)."""
    if mvs not in _MVS_VER:
        import re
        import tempfile
        d = tempfile.mkdtemp(prefix='rv_mvsv_')
        ver = ''
        try:
            subprocess.run([os.path.join(mvs, 'DensifyPointCloud.exe'), '--help'], cwd=d, capture_output=True, timeout=60,
                           creationflags=NO_WINDOW)
            for f in glob.glob(os.path.join(d, '*.log')):
                with open(f, encoding='utf-8', errors='replace') as fp:
                    m = re.search(r'OpenMVS x64 v[\d.]+', fp.read(400))
                if m:
                    ver = m.group(0)
        except Exception:                          # noqa: BLE001 — 판을 몰라도 함께 처리는 함 (판 맞춤만 건너뜀)
            pass
        finally:
            shutil.rmtree(d, ignore_errors=True)
        _MVS_VER[mvs] = ver
    return _MVS_VER[mvs]


def dmap_name(iid):
    return 'depth%04d.dmap' % iid


def dmap_ok(path, iid):
    """OpenMVS 깊이 지도 머리말 확인 ('DR' · 기준 사진 번호 · 크기)."""
    try:
        size = os.path.getsize(path)
        with open(path, 'rb') as fp:
            b = fp.read(4096)
        name, typ, _, _, _, dw, dh = struct.unpack_from('<HBBIIII', b, 0)
        if name != 0x5244 or not (typ & 1) or dw * dh <= 0:
            return False
        o = 28
        n = struct.unpack_from('<H', b, o)[0]
        o += 2 + n
        k = struct.unpack_from('<I', b, o)[0]
        if k < 1 or struct.unpack_from('<I', b, o + 4)[0] != iid:
            return False
        o += 4 + 4 * k + 8 * 21
        return size >= o + 4 * dw * dh
    except (OSError, struct.error):
        return False


def split_parts(nbs, k):
    """이웃 그래프(앞 VIEWS 장)를 k 조각으로 — 스펙트럼 반복 이등분 (조각 경계를 지나는 이웃이 가장 적게 · 크기는 고르게).
    1126 80 · 394장: 카메라 위치 k-평균보다 고리가 작음."""
    import numpy as np
    n = len(nbs)
    W = np.zeros((n, n), np.float32)
    for i, L in enumerate(nbs):
        for j in L[:VIEWS]:
            if 0 <= j < n and j != i:
                W[i, j] = W[j, i] = 1.0

    def rec(nodes, k):
        if k <= 1 or len(nodes) < 2:
            return [nodes]
        sub = W[np.ix_(nodes, nodes)].astype(np.float64)
        L = np.diag(sub.sum(1)) - sub
        f = np.linalg.eigh(L)[1][:, 1]
        order = np.argsort(f, kind='stable')
        k1 = k // 2
        cut = int(round(len(nodes) * k1 / k))
        return rec([nodes[i] for i in order[:cut]], k1) + rec([nodes[i] for i in order[cut:]], k - k1)
    return [p for p in rec(list(range(n)), k) if p]


def _gray_jpeg(path, size):
    from PIL import Image
    Image.new('RGB', tuple(size), (128, 128, 128)).save(path, quality=30)


def _link(src, dst):
    try:
        os.link(src, dst)
    except OSError:
        shutil.copyfile(src, dst)


def _fetch(src, dst):
    """공유 폴더 → 로컬 (이미 같은 크기면 안 받음 · 같은 PC 의 다른 도우미와 겹쳐도 안 깨지게 제 이름으로 받아 바꿈) → 받은 바이트."""
    n = os.path.getsize(src)
    if os.path.exists(dst) and os.path.getsize(dst) == n:
        return 0
    part = '%s.%s.part' % (dst, uuid.uuid4().hex[:6])
    shutil.copyfile(src, part)
    try:
        os.replace(part, dst)
    except OSError:                                # 다른 도우미가 먼저 받아 쓰는 중
        os.remove(part)
        if not (os.path.exists(dst) and os.path.getsize(dst) == n):
            raise
    return n


def run_chunk(mvs, scene, wd, imgs, refs, real, img_src, res_args, sizes=None, threads=0, progress=None, abort=None, cache=None,
              low=False):
    """조각 하나 → wd 에 refs(COLMAP 번호) 깊이 지도. imgs = [(이름, 번호, …)] 장면 순서 · real = 진짜 사진 이름
    (img_src 에서 링크 · 안 되면 복사) · 나머지 = sizes 크기 회색 대역 사진 (sizes 없으면 모두 진짜).
    low = 엔진을 낮은 우선순위로 (도우미). → (ok, 이유, 만든 번호 목록)."""
    from .mapping import mvs_cuda_opt
    shutil.rmtree(wd, ignore_errors=True)
    os.makedirs(os.path.join(wd, 'images'), exist_ok=True)
    shutil.copyfile(scene, os.path.join(wd, 'scene.mvs'))
    refs = set(refs); real = set(real)
    gray = {}
    for name, iid, *_ in imgs:
        dst = os.path.join(wd, name)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        base = os.path.basename(name)
        if sizes is None or name in real:
            _link(os.path.join(img_src, base), dst)
        else:
            sz = tuple(sizes[name])
            if sz not in gray:
                g = os.path.join(cache or wd, '_gray_%dx%d.jpg' % sz)
                if not os.path.exists(g):
                    tmp = '%s.%s.jpg' % (g, uuid.uuid4().hex[:6])
                    _gray_jpeg(tmp, sz)
                    try:
                        os.replace(tmp, g)
                    except OSError:
                        os.remove(tmp)
                gray[sz] = g
            _link(gray[sz], dst)
        if iid not in refs:
            open(os.path.join(wd, dmap_name(iid)), 'wb').close()
    cmd = ([os.path.join(mvs, 'DensifyPointCloud.exe'), 'scene.mvs', '-o', 'chunk.mvs'] + list(res_args) + mvs_cuda_opt(mvs)
           + (['--max-threads', str(threads)] if threads else []) + ['--fusion-mode', '1', '-w', '.'])
    lp = os.path.join(wd, 'engine.log')
    with open(lp, 'wb') as fp:
        p = subprocess.Popen(cmd, cwd=wd, stdout=fp, stderr=subprocess.STDOUT, creationflags=NO_WINDOW | (BELOW_NORMAL if low else 0))
        n_est = 0
        while p.poll() is None:
            time.sleep(3)
            if abort is not None and abort():
                subprocess.run(['taskkill', '/PID', str(p.pid), '/T', '/F'], capture_output=True, creationflags=NO_WINDOW)
                p.wait()
                return False, '멈춤', []
            try:                                   # 사진마다 3번 (사진 맞춤 1 + 기하 맞춤 2) · 표준출력(engine.log)은 끝날 때까지 비어 있음 → 엔진 자체 기록
                for f in glob.glob(os.path.join(wd, 'DensifyPointCloud-*.log')) or [lp]:
                    with open(f, 'rb') as f2:
                        n_est = f2.read().count(b'estimated using')
            except OSError:
                pass
            if progress:
                progress(min(0.99, n_est / max(1.0, 3.0 * len(refs))))
    if p.returncode != 0:
        try:
            tail = open(lp, encoding='utf-8', errors='replace').read()[-400:]
        except OSError:
            tail = ''
        return False, '엔진 종료코드 %d · %s' % (p.returncode, tail.strip().splitlines()[-1] if tail.strip() else ''), []
    got = [i for i in refs if dmap_ok(os.path.join(wd, dmap_name(i)), i)]
    if len(got) < 0.8 * len(refs):                 # 못 쓰는 사진(이웃 없음)은 혼자 처리에도 깊이 지도가 없음 · 많이 빠지면 실패
        return False, '깊이 지도 %d / %d' % (len(got), len(refs)), got
    return True, '', got


def scene_neighbors(mvs, dense, res_args, log=_log):
    """dense/scene.mvs 의 사진 · 이웃 — 모든 사진에 0바이트 깊이 지도를 깔고 1초 실행 (OpenMVS 가 고른 이웃 그대로 · 실험 1126)."""
    from .mapping import mvs_cuda_opt
    imgs = mvs_images(os.path.join(dense, 'scene.mvs'))
    made = []
    try:
        for _, iid, _ in imgs:
            f = os.path.join(dense, dmap_name(iid))
            if not os.path.exists(f):
                open(f, 'wb').close()
                made.append(f)
        with open(os.path.join(dense, '_nb.log'), 'wb') as fp:
            r = subprocess.run([os.path.join(mvs, 'DensifyPointCloud.exe'), 'scene.mvs', '-o', '_nb.mvs'] + list(res_args) + mvs_cuda_opt(mvs)
                               + ['--geometric-iters', '0', '--fusion-mode', '1', '-w', '.'], cwd=dense, stdout=fp, stderr=subprocess.STDOUT,
                               creationflags=NO_WINDOW)
        if r.returncode != 0 or not os.path.exists(os.path.join(dense, '_nb.mvs')):
            raise RuntimeError('이웃 고르기 실패 (종료코드 %d · _nb.log)' % r.returncode)
        return mvs_images(os.path.join(dense, '_nb.mvs'))
    finally:
        for f in made:
            try:
                os.remove(f)
            except OSError:
                pass
        for f in ('_nb.mvs', '_nb.ply'):
            try:
                os.remove(os.path.join(dense, f))
            except OSError:
                pass


# ---------------------------------------------------------------- 조정자 (내 PC · mapping.py 점구름 단계)
def densify_shared(board, mvs, dense, res_args, log=_log, progress=None, threads=None):
    """함께 처리로 깊이 지도를 모두 dense 에 둠 → True (그다음 --geometric-iters 0 으로 합치기만).
    도우미가 없거나 쓸 수 없으면 False (부른 쪽이 혼자 처리) — 도중에 도우미가 죽어도 그 조각은 여기서 다시 하므로 True.
    threads = 내 PC 조각 엔진 코어 수 (없으면 RV_CLUSTER_THREADS · 0 = 전부 · 혼자 처리와 같음)."""
    if threads is None:
        threads = int(os.environ.get('RV_CLUSTER_THREADS') or 0)
    ok, why = board_ok(board)
    if not ok:
        log('  함께 처리 안 함: %s → 혼자' % why)
        return False
    _clean_my_runs(board)
    helpers = _workers(board)
    if not helpers:
        log('  함께 처리: 켜 둔 도우미 없음 → 혼자')
        return False
    t0 = time.time()
    try:
        imgs = scene_neighbors(mvs, dense, res_args, log)
    except Exception as ex:                        # noqa: BLE001 — 판이 다른 엔진 · 파일 꼴
        log('  함께 처리 안 함: %s → 혼자' % ex)
        return False
    n = len(imgs)
    if n < MIN_PHOTOS or any(os.path.isabs(x[0]) for x in imgs):
        log('  함께 처리 안 함: 사진 %d장 (%d장 이상일 때) → 혼자' % (n, MIN_PHOTOS))
        return False
    workers = len(helpers) + 1
    k = max(2, min(2 * workers, n // MIN_PER_CHUNK))   # 일꾼의 2배 조각 = 느린 PC 는 적게 · 빠른 PC 는 많이 집음
    parts = split_parts([x[2] for x in imgs], k)
    jobs = []
    for P in parts:
        S = set(P)
        ring = set()
        for i in S:
            ring.update(j for j in imgs[i][2][:VIEWS] if 0 <= j < n)
        jobs.append({'refs': sorted(imgs[i][1] for i in S), 'real': sorted(imgs[i][0] for i in S | ring), 'ring': len(ring - S)})
    log('  함께 처리: 도우미 %d대 (%s) + 내 PC · 사진 %d장 → 조각 %d개 (고리 가운데 %d장 · 이웃 고르기 %.0f초)' % (
        len(helpers), ' · '.join('%s %s코어' % (h.get('host', '?'), h.get('cores', '?')) for h in helpers), n, len(jobs),
        sorted(j['ring'] for j in jobs)[len(jobs) // 2], time.time() - t0))
    C = _Coordinator(board, mvs, dense, res_args, imgs, jobs, log, progress, threads, len(helpers))
    try:
        return C.run()
    finally:
        C.close()


def _clean_my_runs(board):
    """이 PC 가 전에 올렸다가 끊긴 판 (멈추기 · 꺼짐 · 1분 넘게 심장박동 없음) 지우기."""
    try:
        now = board_now(os.path.join(board, 'runs'))
    except OSError:
        return
    for rd in glob.glob(os.path.join(board, 'runs', '*')):
        r = read_json(os.path.join(rd, 'run.json'))
        try:
            age = now - os.path.getmtime(os.path.join(rd, 'run.json'))
        except OSError:
            continue
        if r and r.get('host') == HOST and age > 60:
            shutil.rmtree(rd, ignore_errors=True)


class _Coordinator:
    def __init__(self, board, mvs, dense, res_args, imgs, jobs, log, progress, threads, nhelp):
        self.board, self.mvs, self.dense, self.res, self.imgs, self.jobs = board, mvs, dense, list(res_args), imgs, jobs
        self.log, self.progress, self.threads, self.nhelp = log, progress, threads, nhelp
        self.id = time.strftime('%m%d_%H%M%S_') + uuid.uuid4().hex[:6]
        self.rd = os.path.join(board, 'runs', self.id)
        self.me = '%s-%d' % (HOST, os.getpid())
        self.lock = threading.Lock()
        self.beat = 0
        self.local = None                          # (번호, 실 · 멈춤 깃발 · 진행 [0..1])
        self.local_sec = []                        # 내 PC 조각 하나 사진 한 장 초 (훔치기 판단)
        self.board_bad = 0
        self.t0 = time.time()
        self.stop_post = False
        self.up_mb = 0.0
        self.engine = ''
        self.poster = None                         # 올리기 실 (끝남 = 다 올렸거나 · 실패 · 죽음 → 못 올린 조각은 내 PC 가)
        self.beater = None                         # 심장박동 실 (주 고리가 큰 깊이 지도를 복사하는 동안에도 run.json 이 움직이게)
        self.beat_stop = threading.Event()
        for j, J in enumerate(jobs):
            J.update(n=j, state='new', src='', posted=False, sec=0.0, tried_local=False)

    # ---- 게시판
    def jd(self, j):
        return os.path.join(self.rd, 'jobs', '%02d' % j)

    def _post(self):
        """장면 · 사진 크기 · 일감 올리기 (조각 0 은 내 PC 가 먼저 하므로 그 사진은 안 올림 · 이미 있는 사진은 다시 안 올림).
        폴더는 makedirs 말고 mkdir (판이 지워진 뒤 늦게 돈 올리기가 판 폴더를 되살리지 않게)."""
        from PIL import Image
        os.mkdir(os.path.join(self.rd, 'images'))
        os.mkdir(os.path.join(self.rd, 'jobs'))
        shutil.copyfile(os.path.join(self.dense, 'scene.mvs'), os.path.join(self.rd, 'scene.mvs'))
        sizes = {}
        for name, _, _ in self.imgs:
            with Image.open(os.path.join(self.dense, 'images', os.path.basename(name))) as im:
                sizes[name] = list(im.size)
        write_json(os.path.join(self.rd, 'sizes.json'), sizes)
        self.engine = mvs_version(self.mvs)
        os.mkdir(self.jd(0))                       # 조각 0 = 내 PC
        os.mkdir(os.path.join(self.jd(0), 'lock'))
        write_json(os.path.join(self.jd(0), 'lock', 'owner.json'), {'id': self.me, 'host': HOST})
        up = 0
        for J in self.jobs[1:]:
            for name in J['real']:
                if self.stop_post:
                    return
                dst = os.path.join(self.rd, 'images', os.path.basename(name))
                src = os.path.join(self.dense, 'images', os.path.basename(name))
                if not (os.path.exists(dst) and os.path.getsize(dst) == os.path.getsize(src)):
                    shutil.copyfile(src, dst + '.part')
                    os.replace(dst + '.part', dst)
                    up += os.path.getsize(src)
                    self.up_mb = up / 1e6
            if self.stop_post:
                return
            os.mkdir(self.jd(J['n']))
            write_json(os.path.join(self.jd(J['n']), 'job.json'), {
                'proto': PROTO, 'run': self.id, 'n': J['n'], 'refs': J['refs'], 'real': J['real'], 'res_args': self.res,
                'engine': self.engine, 'host': HOST, 'made': time.strftime('%Y-%m-%d %H:%M:%S')})
            J['posted'] = True

    def _beat(self):
        self.beat += 1
        write_json(os.path.join(self.rd, 'run.json'), {
            'proto': PROTO, 'id': self.id, 'host': HOST, 'pid': os.getpid(), 'beat': self.beat, 'photos': len(self.imgs),
            'jobs': len(self.jobs), 'done': sum(J['state'] == 'done' for J in self.jobs)})

    def _beat_loop(self):
        while not self.beat_stop.wait(5):
            try:
                self._beat(); self.board_bad = 0
            except OSError:
                self.board_bad += 1

    # ---- 내 PC 가 조각 하나
    def _start_local(self, J, why):
        J['tried_local'] = True
        stop = threading.Event()
        st = {'pct': 0.0}
        wd = os.path.join(self.dense, '_cl', 'w%02d' % J['n'])

        def fail(err):
            self.log('  조각 %d 내 PC 실패 (%s)' % (J['n'], err))
            J['fail_t'] = time.time()
            J['local_fail'] = err or '알 수 없음'      # 빈 글이면 끝 조건이 못 셈 → 늘 글을 남김

        def work():
            t = time.time()
            try:
                ok, err, got = run_chunk(self.mvs, os.path.join(self.dense, 'scene.mvs'), wd, self.imgs, J['refs'], [],
                                         os.path.join(self.dense, 'images'), self.res, threads=self.threads,
                                         progress=lambda f: st.__setitem__('pct', f), abort=stop.is_set)
                sec = time.time() - t
                if ok:
                    with self.lock:
                        if J['state'] != 'done':
                            for i in got:
                                os.replace(os.path.join(wd, dmap_name(i)), os.path.join(self.dense, dmap_name(i)))
                            self._done(J, '내 PC', sec)
                            self.local_sec.append(sec / max(1, len(J['refs'])))
                elif not stop.is_set():
                    fail(err)
            except Exception as ex:                # noqa: BLE001 — 복사 · 링크 · 엔진 띄우기 · 디스크 가득: 조용히 죽으면 끝없이 기다림
                if not stop.is_set():
                    fail('%s: %s' % (type(ex).__name__, ex))
            finally:
                shutil.rmtree(wd, ignore_errors=True)
        th = threading.Thread(target=work, daemon=True)
        self.local = (J, th, stop, st)
        th.start()
        if why:
            self.log('  조각 %d (사진 %d) 내 PC 가 함 · %s' % (J['n'], len(J['refs']), why))

    def _done(self, J, who, sec):
        J['state'] = 'done'; J['src'] = who; J['sec'] = sec
        self.log('  조각 %d (사진 %d) 끝 · %s · %.0f초' % (J['n'], len(J['refs']), who, sec))
        try:
            write_json(os.path.join(self.jd(J['n']), 'collected.json'), {'by': who})
        except OSError:
            pass

    # ---- 도우미 결과
    def _collect(self, J):
        jd = self.jd(J['n'])
        for res in glob.glob(os.path.join(jd, 'res_*')):
            d = read_json(os.path.join(res, 'done.json'))
            if not d:
                if os.path.exists(os.path.join(res, 'fail.json')) and not J.get('fail_seen'):
                    J['fail_seen'] = True
                    f = read_json(os.path.join(res, 'fail.json')) or {}
                    self.log('  조각 %d 도우미 %s 실패 (%s)' % (J['n'], f.get('host', '?'), str(f.get('why', ''))[:120]))
                continue
            got = [i for i in d.get('got', []) if i in set(J['refs'])]
            bad = [i for i in got if not dmap_ok(os.path.join(res, dmap_name(i)), i)]
            if bad or len(got) < 0.8 * len(J['refs']):
                self.log('  조각 %d 도우미 결과 못 씀 (깊이 지도 %d / %d · 깨짐 %d) → 다시' % (J['n'], len(got), len(J['refs']), len(bad)))
                os.replace(os.path.join(res, 'done.json'), os.path.join(res, 'bad.json'))
                J['fail_seen'] = True
                continue
            for i in got:                           # 큰 파일 (보통 25 MB · 정밀 100 MB) → 이름 바꿔 한 번에
                dst = os.path.join(self.dense, dmap_name(i))
                shutil.copyfile(os.path.join(res, dmap_name(i)), dst + '.part')
                os.replace(dst + '.part', dst)
            with self.lock:
                if J['state'] != 'done':
                    self._done(J, '도우미 %s' % d.get('host', '?'), float(d.get('sec', 0)))
            return True
        return False

    def _hb(self, J, now):
        """(집은 PC, 진행, 나이 초) — 심장박동 = hb_<id>.json 수정 시각 (없으면 lock 만든 시각)."""
        jd = self.jd(J['n'])
        own = read_json(os.path.join(jd, 'lock', 'owner.json')) or {}
        best = None
        for f in glob.glob(os.path.join(jd, 'hb_*.json')):
            h = read_json(f) or {}
            try:
                age = now - os.path.getmtime(f)
            except OSError:
                continue
            if h.get('id') == own.get('id') or best is None:
                best = (h, age)
        if best:
            return own, float(best[0].get('pct', 0.0)), best[1]
        try:
            return own, 0.0, now - os.path.getmtime(os.path.join(jd, 'lock'))
        except OSError:
            return own, 0.0, None

    def _take(self, J, why, steal=False):
        """아무도 안 집은 조각 (steal=False · mkdir 이 되면) · 끊긴 · 실패한 조각 (steal=True · lock 을 옮기고 새로 = 그 도우미는 멈춤)
        을 내 PC 가 함. 늦게 온 도우미 결과도 먼저 오면 씀."""
        jd = self.jd(J['n'])
        try:
            if steal and os.path.isdir(os.path.join(jd, 'lock')):
                os.replace(os.path.join(jd, 'lock'), os.path.join(jd, 'lock_%s' % uuid.uuid4().hex[:4]))
            os.mkdir(os.path.join(jd, 'lock'))
            write_json(os.path.join(jd, 'lock', 'owner.json'), {'id': self.me, 'host': HOST})
        except OSError:
            if not steal:                          # 그 사이 도우미가 집음
                return False
        self._start_local(J, why)
        return True

    def _lock_age(self, J, now):
        try:
            return now - os.path.getmtime(os.path.join(self.jd(J['n']), 'lock'))
        except OSError:
            return None

    def _pick_local(self, now):
        """내 PC 가 쉴 때 할 조각: 0 → 못 올린 것 → 아무도 안 집은 것 → 끊긴 · 실패한 것 → (도우미가 아주 느리면) 같이."""
        todo = [J for J in self.jobs if J['state'] != 'done' and not J['tried_local']]
        if not todo:
            return
        post_over = self.poster is not None and not self.poster.is_alive()     # 다 올렸거나 · 올리기 실패 · 실이 죽음
        for J in todo:
            if J['n'] == 0 or not self.board_bad < 3:
                return self._start_local(J, '' if J['n'] == 0 else '공유 폴더 안 됨')
            if not J['posted'] and post_over:      # 못 올린 조각 = job.json 이 없어 도우미가 못 집음 → 바로 내 PC
                return self._start_local(J, '일감 못 올림')
        rate = (sum(self.local_sec) / len(self.local_sec)) if self.local_sec else None
        for J in todo:
            if not J['posted']:
                continue
            own, pct, age = self._hb(J, now)
            if not os.path.isdir(os.path.join(self.jd(J['n']), 'lock')):
                if self._take(J, ''):              # 아무도 안 집은 조각 = 내 PC 도 일꾼
                    return
                continue
            if not own:                            # 막 집어 주인 파일을 쓰는 중 · 오래도록 없으면 (도우미가 mkdir 직후 꺼짐) 훔침
                if age is not None and age > STALE_SEC and not os.path.exists(os.path.join(self.jd(J['n']), 'lock', 'owner.json')):
                    return self._take(J, '주인 없는 일감 (%.0f초)' % age, steal=True)
                continue
            if age is not None and age > STALE_SEC:
                return self._take(J, '도우미 %s 끊김 (%.0f초)' % (own.get('host', '?'), age), steal=True)
            if J.get('fail_seen'):
                return self._take(J, '도우미 실패', steal=True)
        if rate:                                   # 도우미가 내 PC 보다 한참 느리면 같이 돌려 먼저 끝난 것을 씀
            for J in todo:
                if not J['posted']:
                    continue
                own, pct, age = self._hb(J, now)
                el = self._lock_age(J, now)
                if el is None:
                    continue
                left = (el / pct - el) if pct > 0.05 else None
                mine = rate * len(J['refs'])
                if (left is not None and left > 1.6 * mine) or (left is None and el > 2.5 * mine):
                    return self._start_local(J, '도우미 %s 가 느림 (남은 %s · 내 PC %.0f초) → 같이' % (
                        own.get('host', '?'), '%.0f초' % left if left else '?', mine))

    def _helper_on(self, J, now):
        """내 PC 가 실패한 조각을 도우미가 아직 하는 중인가 («같이» 돌리던 도우미의 심장박동이 살아 있음 · 실패 안 함 · 너무 오래 안 기다림)."""
        if not J['posted'] or J.get('fail_seen') or time.time() - J.get('fail_t', 0.0) > WAIT_HELPER_SEC:
            return False
        if now is None:                            # 공유 폴더가 잠깐 안 보임 → 아직 모름 (오래 안 되면 끝으로 셈)
            return self.board_bad < 3
        own, _, age = self._hb(J, now)
        return bool(own) and own.get('id') != self.me and age is not None and age < STALE_SEC

    def run(self):
        os.makedirs(self.rd, exist_ok=True)
        self._beat()
        self.beater = threading.Thread(target=self._beat_loop, daemon=True)
        self.beater.start()
        self.poster = threading.Thread(target=self._post_safe, daemon=True)
        self.poster.start()
        n = len(self.imgs)
        while True:
            try:
                bnow = board_now(self.rd)
            except OSError:
                bnow = None; self.board_bad += 1
            if bnow is not None:
                for J in self.jobs:
                    if J['state'] != 'done' and J['posted']:
                        try:
                            self._collect(J)
                        except OSError as ex:
                            self.log('  조각 %d 결과 읽기 실패 (%s)' % (J['n'], ex))
            if self.local and not self.local[1].is_alive():
                self.local = None
            if self.local and self.local[0]['state'] == 'done':   # 같이 돌리던 조각을 도우미가 먼저 끝냄 → 내 PC 는 멈춤
                self.local[2].set()
            # 내 PC 가 실패했고 하는 도우미도 없는 조각 = 함께 처리 결과는 못 씀 → 남은 조각을 기다리지 말고 바로 혼자 처리로
            # (조각 0 이 실패해 «느림» 판단의 내 PC 속도가 없어도 느린 도우미를 끝없이 기다리지 않음)
            if any(J['state'] != 'done' and J.get('local_fail') and not self._helper_on(J, bnow) for J in self.jobs):
                if self.local:
                    self.local[2].set()
                break
            if self.local is None and all(J['state'] == 'done' for J in self.jobs):
                break
            if not self.local and (bnow is not None or self.board_bad >= 3):
                self._pick_local(bnow if bnow is not None else time.time())
            if self.progress:
                done = sum(len(J['refs']) for J in self.jobs if J['state'] == 'done')
                part = 0.0
                if self.local and self.local[0]['state'] != 'done':
                    part = self.local[3]['pct'] * len(self.local[0]['refs'])
                ndone = sum(J['state'] == 'done' for J in self.jobs)
                self.progress(min(0.99, (done + part) / n), '점구름 · 함께 처리 (도우미 %d대) · 조각 %d/%d' % (self.nhelp, ndone, len(self.jobs)))
            time.sleep(2)
        self.stop_post = True
        left = [J for J in self.jobs if J['state'] != 'done']
        if left:
            self.log('  함께 처리: 조각 %d개 못 함 (%s) → 혼자 처리로' % (len(left), ' · '.join(
                '%d %s' % (J['n'], str(J.get('local_fail') or '안 끝남')[:60]) for J in left)))
            return False
        by = {}
        for J in self.jobs:
            k = J['src'] if J['src'] == '내 PC' else '도우미'
            by[k] = by.get(k, 0) + len(J['refs'])
        self.log('  함께 처리 깊이 지도 %.0f초 · 사진 %d장 (%s) · 올린 사진 %.0f MB' % (
            time.time() - self.t0, n, ' · '.join('%s %d' % kv for kv in sorted(by.items())), self.up_mb))
        return True

    def _post_safe(self):
        """실이 끝나면 (실패든 죽음이든) 못 올린 조각은 _pick_local 이 바로 내 PC 로 — board_bad 로 알리면 심장박동 성공이 5초 뒤 지워 끝없이 기다렸음."""
        try:
            self._post()
        except Exception as ex:                    # noqa: BLE001 — NAS 용량 · 권한 · 끊김 · 사진 읽기
            if not self.stop_post:
                self.log('  함께 처리: 일감 올리기 실패 (%s) → 못 올린 조각 %d개는 내 PC 가 함' % (
                    ex, sum(not J['posted'] for J in self.jobs[1:])))

    def close(self):
        self.stop_post = True
        self.beat_stop.set()
        for th in (self.beater, self.poster):      # 판 폴더를 지우기 전에 멈춤 (늦게 쓴 run.json · 일감이 판을 되살리지 않게)
            if th:
                th.join(timeout=30)
        if self.local:
            self.local[2].set()
            self.local[1].join(timeout=30)
        shutil.rmtree(os.path.join(self.dense, '_cl'), ignore_errors=True)
        shutil.rmtree(self.rd, ignore_errors=True)


# ---------------------------------------------------------------- 도우미 (옆 PC · rv_cli helper)
class Helper:
    def __init__(self, board, threads=0, parent=0, once=False, idle_exit=0.0):
        self.board, self.threads, self.parent, self.once, self.idle_exit = board, threads, parent, once, idle_exit
        self.id = '%s-%s' % (HOST, uuid.uuid4().hex[:6])
        self.stop = threading.Event()
        self.st = {'id': self.id, 'host': HOST, 'pid': os.getpid(), 'cores': os.cpu_count() or 0, 'ram_gb': _ram_gb(),
                   'proto': PROTO, 'state': 'idle', 'job': '', 'pct': 0.0, 'done': 0, 'threads': threads, 'msg': ''}
        self._last = ''
        self.mvs = None
        self.engine = ''
        self._skip = set()                         # 엔진 판이 달라 안 집은 일감 (알림 한 번만)

    def _beat(self):
        try:
            write_json(os.path.join(self.board, 'helpers', self.id + '.json'), self.st)
        except OSError:
            pass
        s = json.dumps({k: self.st[k] for k in ('state', 'job', 'pct', 'done', 'msg')}, ensure_ascii=False)
        if s != self._last:                        # 앱 «도우미 모드» 가 읽는 줄
            print('@도우미 ' + s, flush=True)
            self._last = s

    def _beat_loop(self):
        while not self.stop.wait(HB_SEC):
            self._beat()
            if self.parent and not _pid_alive(self.parent):
                _log('앱이 꺼짐 → 도우미 끝')
                self.stop.set()

    def set(self, **kw):
        self.st.update(kw)
        self._beat()

    def run(self):
        ok, why = board_ok(self.board)
        if not ok:
            _log('도우미 시작 못 함: ' + why)
            return 2
        from .mapping import find_engines, install_engines
        self.set(state='install', msg='엔진 확인')
        colmap, mvs = find_engines()
        if not mvs:
            _log('엔진 없음 → 내려받기')
            try:
                colmap, mvs = install_engines(lambda s: (_log(s), self.set(msg=s)))
            except Exception as ex:                # noqa: BLE001
                _log('엔진 설치 실패: %s' % ex)
        if not mvs:
            self.set(state='off', msg='엔진 없음')
            return 3
        self.mvs = mvs
        self.engine = mvs_version(mvs)
        self.set(state='idle', msg='')
        _log('도우미 시작 · %s · 코어 %s · 엔진 %s · 공유 폴더 %s' % (self.id, self.st['cores'], self.engine or '판 모름', self.board))
        threading.Thread(target=self._beat_loop, daemon=True).start()
        idle_t = time.time()
        try:
            while not self.stop.is_set():
                got = self._find()
                if got:
                    self._do(*got)
                    idle_t = time.time()
                    if self.once:
                        break
                    continue
                if self.idle_exit and time.time() - idle_t > self.idle_exit:
                    break
                self.stop.wait(3)
        finally:
            self.st['state'] = 'off'
            try:
                os.remove(os.path.join(self.board, 'helpers', self.id + '.json'))
            except OSError:
                pass
            print('@도우미 ' + json.dumps({'state': 'off', 'job': '', 'pct': 0, 'done': self.st['done'], 'msg': ''}), flush=True)
        return 0

    def _find(self):
        """집을 일감: 오래된 판부터 · 조정자가 살아 있는 판만 · lock 을 mkdir 로 (둘이 못 집음)."""
        runs = os.path.join(self.board, 'runs')
        try:
            now = board_now(runs)
            rds = sorted(glob.glob(os.path.join(runs, '*')))
        except OSError:
            return None
        alive_runs = set()
        for rd in rds:
            r = read_json(os.path.join(rd, 'run.json'))
            try:
                age = now - os.path.getmtime(os.path.join(rd, 'run.json'))
            except OSError:
                age = None
            if not r or age is None:
                continue
            if age > 3 * RUN_STALE_SEC:            # 꺼진 조정자의 판 찌꺼기 (누구든 지움)
                shutil.rmtree(rd, ignore_errors=True)
                continue
            if age > RUN_STALE_SEC or r.get('proto') != PROTO or (r.get('host') == HOST and not SAME_HOST):
                continue
            alive_runs.add(os.path.basename(rd))
            for jd in sorted(glob.glob(os.path.join(rd, 'jobs', '*'))):
                if os.path.exists(os.path.join(jd, 'lock')) or os.path.exists(os.path.join(jd, 'collected.json')):
                    continue
                job = read_json(os.path.join(jd, 'job.json'))     # 없으면 아직 사진을 올리는 중
                if not job:
                    continue
                ev = job.get('engine') or ''
                if ev and self.engine and ev != self.engine:     # 판이 다른 엔진의 깊이 지도는 섞지 않음 → 조정자 · 다른 도우미가 함
                    if jd not in self._skip:
                        self._skip.add(jd)
                        _log('일감 %s/%s 안 집음 · 엔진 판 다름 (일감 %s · 이 PC %s)' % (job.get('run'), job.get('n'), ev, self.engine))
                    continue
                try:
                    os.mkdir(os.path.join(jd, 'lock'))
                except OSError:
                    continue                       # 다른 PC 가 먼저 집음
                try:
                    write_json(os.path.join(jd, 'lock', 'owner.json'), {'id': self.id, 'host': HOST})
                except OSError:                    # 주인을 못 씀 → 놓음 (못 놓으면 조정자가 «주인 없는 일감» 으로 훔침)
                    try:
                        os.rmdir(os.path.join(jd, 'lock'))
                    except OSError:
                        pass
                    continue
                return rd, jd, job
        for d in glob.glob(os.path.join(LOCAL, '*')):   # 끝난 판의 이 PC 사진 사본 지우기
            if os.path.basename(d) not in alive_runs:
                shutil.rmtree(d, ignore_errors=True)
        return None

    def _do(self, rd, jd, job):
        name = '%s/%02d' % (job['run'], job['n'])
        t = time.time()
        res = os.path.join(jd, 'res_' + self.id)
        cache = os.path.join(LOCAL, job['run'])
        self.set(state='busy', job=name, pct=0.0, msg='사진 받는 중')
        _log('일감 %s · 사진 %d장 계산 · 빌릴 사진 %d장' % (name, len(job['refs']), len(job['real'])))
        hbf = os.path.join(jd, 'hb_%s.json' % self.id)
        cur = [0.0]
        jstop = threading.Event()

        def job_beat():                            # 일감 심장박동 = 따로 실 (사진 받기 · 결과 올리기 같은 큰 복사 중에도 끊기지 않게)
            while True:
                try:                               # write_json 은 폴더를 안 만듦 → 판이 지워졌으면 그냥 실패 (되살리지 않음)
                    write_json(hbf, {'id': self.id, 'host': HOST, 'pct': cur[0], 'beat': int(time.time())})
                except OSError:
                    pass
                if jstop.wait(5):
                    return

        chk = [0.0]
        miss = [0.0]

        def run_gone():
            """run.json 이 RUN_GONE_SEC 동안 연달아 없음 = 판 끝남. 없음(FileNotFoundError)은 runs 폴더가 보일 때만 셈 ·
            다른 OSError 와 runs 도 안 보임 = 공유 폴더가 잠깐 끊김 → 기다림 (전에는 3초마다 엔진을 죽였음)."""
            try:
                os.stat(os.path.join(rd, 'run.json'))
                miss[0] = 0.0
            except FileNotFoundError:
                if os.path.isdir(os.path.dirname(rd)):
                    if not miss[0]:
                        miss[0] = time.time()
                    elif time.time() - miss[0] > RUN_GONE_SEC:
                        return True
            except OSError:
                pass
            return False

        def gone():                                # 조정자가 거둠 · 판 없어짐 · 조정자 끊김 · 집은 PC 가 바뀜 → 멈춤
            if self.stop.is_set() or os.path.exists(os.path.join(jd, 'collected.json')) or run_gone():
                return True
            own = read_json(os.path.join(jd, 'lock', 'owner.json'))
            if own is not None and own.get('id') != self.id:
                return True
            if time.time() - chk[0] > 30:
                chk[0] = time.time()
                try:
                    if board_now(rd, 'hb') - os.path.getmtime(os.path.join(rd, 'run.json')) > RUN_STALE_SEC:
                        return True
                except OSError:
                    pass
            return False

        def mkres():                               # 결과 폴더 = 판이 살아 있을 때만 · mkdir (makedirs 는 지워진 판의 jobs 폴더를 되살림)
            try:
                os.mkdir(res)
            except FileExistsError:
                pass
            except FileNotFoundError:              # 일감 폴더가 없음 = 판이 지워짐
                raise _Gone()
        last = [0.0]

        def prog(f):
            cur[0] = f
            self.st['pct'] = round(100 * f, 1)
            if time.time() - last[0] > 5:          # 앱 «도우미 모드» 줄 (일감 심장박동은 job_beat 이 씀)
                last[0] = time.time()
                self._beat()
        jt = threading.Thread(target=job_beat, daemon=True)
        jt.start()
        try:
            prog(0.0)
            os.makedirs(os.path.join(cache, 'images'), exist_ok=True)
            for f in ('scene.mvs', 'sizes.json'):
                _fetch(os.path.join(rd, f), os.path.join(cache, f))
            sizes = read_json(os.path.join(cache, 'sizes.json'))
            mb = 0
            for k, nm in enumerate(job['real']):        # 이 판에서 이미 받은 사진은 다시 안 받음
                b = os.path.basename(nm)
                mb += _fetch(os.path.join(rd, 'images', b), os.path.join(cache, 'images', b))
                if k % 10 == 0:
                    if gone():
                        raise _Gone()
                    prog(0.0)
            self.set(msg='깊이 지도')
            imgs = mvs_images(os.path.join(cache, 'scene.mvs'))
            wd = os.path.join(cache, 'w%02d' % job['n'])
            ok, err, got = run_chunk(self.mvs, os.path.join(cache, 'scene.mvs'), wd, imgs, job['refs'], job['real'],
                                     os.path.join(cache, 'images'), job['res_args'], sizes=sizes, threads=self.threads,
                                     progress=prog, abort=gone, cache=cache, low=True)
            if err == '멈춤' or gone():
                raise _Gone()
            mkres()
            if not ok:
                write_json(os.path.join(res, 'fail.json'), {'host': HOST, 'why': err})
                _log('일감 %s 실패: %s' % (name, err))
            else:
                self.set(msg='결과 올리는 중')
                for i in got:
                    shutil.copyfile(os.path.join(wd, dmap_name(i)), os.path.join(res, dmap_name(i) + '.part'))
                    os.replace(os.path.join(res, dmap_name(i) + '.part'), os.path.join(res, dmap_name(i)))
                    if gone():
                        raise _Gone()
                write_json(os.path.join(res, 'done.json'), {'host': HOST, 'id': self.id, 'got': got, 'sec': round(time.time() - t, 1),
                                                            'down_mb': round(mb / 1e6, 1)})
                self.st['done'] += 1
                _log('일감 %s 끝 · %.0f초 · 깊이 지도 %d · 받은 사진 %.0f MB' % (name, time.time() - t, len(got), mb / 1e6))
            shutil.rmtree(wd, ignore_errors=True)
        except _Gone:
            _log('일감 %s 그만 (조정자가 거둠 · 판 끝남)' % name)
        except Exception as ex:                    # noqa: BLE001 — 공유 폴더 끊김 · 디스크 등: 실패로 알리고 다음 일감
            _log('일감 %s 실패: %s' % (name, ex))
            try:
                mkres()                            # 판이 이미 지워졌으면 폴더를 다시 만들지 않음 (공유 폴더 찌꺼기)
                write_json(os.path.join(res, 'fail.json'), {'host': HOST, 'why': str(ex)[:300]})
            except (OSError, _Gone):
                pass
        finally:
            jstop.set()
            jt.join(timeout=10)
            self.set(state='idle', job='', pct=0.0, msg='')


class _Gone(Exception):
    pass


def _ram_gb():
    try:
        import ctypes

        class MS(ctypes.Structure):
            _fields_ = [('dwLength', ctypes.c_ulong), ('dwMemoryLoad', ctypes.c_ulong), ('ullTotalPhys', ctypes.c_ulonglong),
                        ('ullAvailPhys', ctypes.c_ulonglong), ('ullTotalPageFile', ctypes.c_ulonglong), ('ullAvailPageFile', ctypes.c_ulonglong),
                        ('ullTotalVirtual', ctypes.c_ulonglong), ('ullAvailVirtual', ctypes.c_ulonglong), ('ullAvailExtendedVirtual', ctypes.c_ulonglong)]
        m = MS(); m.dwLength = ctypes.sizeof(MS)
        ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m))
        return round(m.ullTotalPhys / 2 ** 30)
    except Exception:                              # noqa: BLE001
        return 0


def _pid_alive(pid):
    try:
        import ctypes
        h = ctypes.windll.kernel32.OpenProcess(0x1000, False, int(pid))     # PROCESS_QUERY_LIMITED_INFORMATION
        if not h:
            return False
        code = ctypes.c_ulong()
        ctypes.windll.kernel32.GetExitCodeProcess(h, ctypes.byref(code))
        ctypes.windll.kernel32.CloseHandle(h)
        return code.value == 259                   # STILL_ACTIVE
    except Exception:                              # noqa: BLE001
        return True


def helper_main(argv=None):
    ap = argparse.ArgumentParser(description='함께 처리 도우미 — 공유 폴더의 점구름 일감을 받아 처리')
    ap.add_argument('--board', required=True, help='공유 폴더 (작업 게시판)')
    ap.add_argument('--threads', type=int, default=0, help='쓸 코어 수 (0 = 전부 · 엔진은 낮은 우선순위)')
    ap.add_argument('--parent-pid', type=int, default=0, help='이 프로그램이 꺼지면 같이 끝 (앱 «도우미 모드»)')
    ap.add_argument('--once', action='store_true', help='일감 하나만 (시험)')
    ap.add_argument('--idle-exit', type=float, default=0, help='이 초만큼 일감이 없으면 끝 (시험)')
    a = ap.parse_args(argv)
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:                              # noqa: BLE001
        pass
    return Helper(os.path.abspath(a.board), a.threads, a.parent_pid, a.once, a.idle_exit).run()


if __name__ == '__main__':
    sys.exit(helper_main())
