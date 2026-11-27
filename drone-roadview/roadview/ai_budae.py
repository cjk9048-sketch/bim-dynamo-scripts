"""AI 부대공 인식 (JACK 1126 · 초안) — 관로 매설로 생길 부대공(연석 · 석축 · 보도 · 포장) 후보 찾기.

    3D 매핑 결과(정사 · 3D 면) = 위치 · 수량 후보 / 360 사진 = 재질 · 상태 (후보를 잘 보는 사진을 평면으로 잘라 판정)
    AI 는 이 PC 안에서만 (클라우드 없음): Grounding DINO + SAM (영역) · CLIP (종류 · 상태)

AI 엔진 환경은 프로그램과 따로 (처음 쓸 때 한 번 내려받기 · 약 4~5 GB):
    %LOCALAPPDATA%\\DH.Roadview\\ai\\  (RV_AI_DIR 로 바꿈)
      uv\\uv.exe           — Astral uv (Python · 패키지 설치기 · 단일 exe)
      python\\             — uv 가 받은 Python 3.12
      venv\\               — torch (그래픽카드면 CUDA) · transformers · opencv · shapely
      models\\             — 모델 파일 (모델마다 폴더 · 링크 없이 파일 그대로 — HF 캐시 링크는 D: 에서 권한 오류) · HF_HOME = models\\_hf
      ready.json          — 다 된 표시 (torch 판 · CUDA · 그래픽카드)
작업은 그 환경의 python.exe 로 ai_worker.py 를 돌림 («@진행 퍼센트 남은초 글» 줄 그대로 전달 → app.Job).

결과: <프로젝트>/data/ai_budae.json + ai_budae.js (window.RV_AI = …) — 화면에서 사람이 ok/no 를 고르면 ai_save 로 저장.
다시 인식할 때 사람이 고른 것(ok · no)은 남기고, 그 자리와 겹치는 새 AI 후보는 버림.

    python -m roadview.ai_budae setup
    python -m roadview.ai_budae run <매핑 프로젝트 폴더> [--p360 <360 프로젝트 폴더>]
    python -m roadview.ai_budae status
"""
from __future__ import annotations

import argparse
import json
import math
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

NO_WINDOW = 0x08000000 if os.name == 'nt' else 0
AI_DIR = Path(os.environ.get('RV_AI_DIR') or (Path(os.environ.get('LOCALAPPDATA', str(Path.home()))) / 'DH.Roadview' / 'ai'))
UV_URL = 'https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-pc-windows-msvc.zip'
PY_VER = '3.12'
# transformers 4.x 로 묶음 (5.x 는 Grounding DINO 뒤처리 이름이 바뀔 수 있음 · 바꿀 때 시험 다시)
PKGS = ['torch', 'transformers>=4.51,<5', 'numpy', 'pillow', 'opencv-python-headless', 'shapely', 'scipy', 'safetensors', 'hf_xet']
# 모델 (모두 Hugging Face · 라이선스: Grounding DINO · SAM = Apache-2.0 · CLIP = MIT (openai/CLIP 저장소))
# CLIP 은 large-patch14 (재질 · 균열 같은 잔 무늬 판정이 base-patch32 보다 나음 · safetensors 있음 — base 는 .bin 만)
MODELS = {'det': 'IDEA-Research/grounding-dino-base', 'sam': 'facebook/sam-vit-base', 'clip': 'openai/clip-vit-large-patch14'}
CLASSES = {'curb': '연석', 'wall': '석축', 'sidewalk': '보도블록', 'asphalt': '아스팔트', 'concrete': '콘크리트', 'unpaved': '비포장'}
WORKER = 'ai_worker.py'


def _base():
    if getattr(sys, 'frozen', False):
        return Path(getattr(sys, '_MEIPASS', '')) / 'roadview'
    return Path(__file__).resolve().parent


def _py():
    return AI_DIR / 'venv' / 'Scripts' / 'python.exe'


def _env():
    """AI 환경 하위 프로세스 환경 — 모델 캐시 · uv 자리를 AI_DIR 안으로 (사용자 전역 설정을 안 건드림)."""
    e = dict(os.environ)
    for k in list(e):
        if k.startswith(('PYTHON', 'VIRTUAL_ENV', 'CONDA')):
            e.pop(k)
    e.update(PYTHONIOENCODING='utf-8', PYTHONUTF8='1', HF_HOME=str(AI_DIR / 'models' / '_hf'), RV_AI_MODELS=str(AI_DIR / 'models'), HF_HUB_DISABLE_TELEMETRY='1', HF_HUB_DISABLE_PROGRESS_BARS='1', HF_HUB_DISABLE_SYMLINKS_WARNING='1',
             UV_PYTHON_INSTALL_DIR=str(AI_DIR / 'python'), UV_CACHE_DIR=str(AI_DIR / 'cache'), UV_LINK_MODE='copy',
             UV_NO_CONFIG='1', UV_PYTHON_PREFERENCE='only-managed', TOKENIZERS_PARALLELISM='false')
    return e


_SPAN = [0.0, 100.0]        # 진행 막대 구간 (실행 중 엔진 설치가 끼면 설치 0~40 · 인식 40~100 — app.Job 은 퍼센트가 뒤로 안 감)


def _say(pct, label, left=-1):
    pct = _SPAN[0] + (_SPAN[1] - _SPAN[0]) * max(0.0, min(100.0, pct)) / 100.0
    print('@진행 %.1f %d %s' % (pct, left, label), flush=True)


def _relay(s, p0=0.0, p1=100.0):
    """하위 프로세스 줄 → 그대로 보임 · «@진행» 은 [p0, p1] 구간으로 옮겨 다시 냄 · «@단계» 는 버림."""
    if s.startswith('@진행 '):
        try:
            _, pct, left, label = s.split(' ', 3)
            _say(p0 + (p1 - p0) * float(pct) / 100.0, label, int(left))
            return
        except ValueError:
            pass
    if s.startswith('@'):
        return
    print(s, flush=True)


def gpu_names():
    try:
        from .gsplat import _gpus
        return [g for g in _gpus() if not re.search(r'Basic Display|Basic Render|Remote|Virtual|Parsec|Citrix|Mirage|DisplayLink', g, re.I)]
    except Exception:                          # noqa: BLE001
        return []


def status():
    """화면이 부름 (가볍게 · 내려받지 않음) → {ready, dir, gpu, torch, cuda, msg}."""
    g = gpu_names()
    d = {'ready': False, 'dir': str(AI_DIR), 'gpu': g[0] if g else '', 'torch': '', 'cuda': False, 'models': MODELS}
    rj = AI_DIR / 'ready.json'
    if rj.exists() and _py().exists():
        try:
            d.update(json.load(open(rj, encoding='utf-8')))
            d['ready'] = d.get('models') == MODELS and all(   # 모델을 바꾼 판이면 새 모델을 받아야 (작업은 인터넷 없이 돎)
                (AI_DIR / 'models' / m.replace('/', '__') / 'config.json').exists() for m in MODELS.values())
        except (OSError, ValueError):
            pass
    if d['ready']:
        d['msg'] = 'AI 엔진 있음 · %s' % ('그래픽카드 %s' % d.get('gpu_used') if d.get('cuda') else 'CPU (느림)')
    else:
        d['msg'] = '처음 쓸 때 AI 엔진 내려받기 (약 6 GB)' + ('' if g else ' · 그래픽카드 없음 → CPU 로 (느림)')
    return d


def _run_lines(cmd, label, p0, p1, sec, env=None):
    """하위 명령을 돌리며 줄을 그대로 보이고 막대는 어림 초로 밀어 줌 (@단계)."""
    a0 = _SPAN[0] + (_SPAN[1] - _SPAN[0]) * p0 / 100.0; a1 = _SPAN[0] + (_SPAN[1] - _SPAN[0]) * p1 / 100.0
    print('@단계 %.1f %.1f %d' % (a0, a1, sec), flush=True)
    _say(p0, label)
    pr = subprocess.Popen([str(c) for c in cmd], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=env or _env(),
                          creationflags=NO_WINDOW)
    tail = []
    for raw in iter(pr.stdout.readline, b''):
        s = raw.decode('utf-8', errors='replace').rstrip()
        if s:
            _relay(s if s.startswith('@') else '  ' + s[:200], p0, p1)
            if not s.startswith('@'):
                tail = (tail + [s])[-6:]
    if pr.wait() != 0:
        raise RuntimeError('%s 실패 · %s' % (label, ' / '.join(tail[-3:])))


def ensure_env(force=False):
    """AI 환경 준비 (있으면 바로) → status(). 진행은 «@진행» 줄."""
    import urllib.request
    import zipfile
    if not force and status()['ready']:
        _say(100, 'AI 엔진 있음')
        return status()
    if os.name != 'nt':
        raise RuntimeError('Windows 에서만')
    t0 = time.time()
    AI_DIR.mkdir(parents=True, exist_ok=True)
    (AI_DIR / 'ready.json').unlink(missing_ok=True)
    uv = AI_DIR / 'uv' / 'uv.exe'
    if not uv.exists():
        _say(1, '설치기(uv) 내려받기')
        z = AI_DIR / 'uv.zip'
        try:
            with urllib.request.urlopen(UV_URL, timeout=60) as r, open(z, 'wb') as fp:
                shutil.copyfileobj(r, fp, 1 << 20)
        except OSError as ex:
            raise RuntimeError('AI 엔진 내려받기 실패 → 인터넷 · 회사 방화벽 확인 (%s)' % ex)
        tmp = AI_DIR / 'uv.tmp'
        shutil.rmtree(tmp, ignore_errors=True)
        with zipfile.ZipFile(z) as zf:
            zf.extractall(tmp)
        z.unlink()
        hit = next(iter(tmp.rglob('uv.exe')), None)
        if hit is None:
            raise RuntimeError('uv zip 안에 uv.exe 없음')
        shutil.rmtree(AI_DIR / 'uv', ignore_errors=True)
        shutil.move(str(hit.parent), str(AI_DIR / 'uv'))
        shutil.rmtree(tmp, ignore_errors=True)
    if not _py().exists():
        _run_lines([uv, 'venv', AI_DIR / 'venv', '--python', PY_VER, '--seed'], 'Python %s 준비' % PY_VER, 3, 8, 40)
    gpu = gpu_names()
    nv = any(re.search(r'NVIDIA', g, re.I) for g in gpu)
    backend = 'auto' if nv else 'cpu'           # auto = 그래픽 드라이버 판에 맞는 CUDA torch (RTX 50 = CUDA 12.8 이상)
    try:
        _run_lines([uv, 'pip', 'install', '--python', _py(), '--torch-backend', backend] + PKGS,
                   'AI 패키지 내려받기 (torch %s · 약 3 GB)' % ('CUDA' if nv else 'CPU'), 8, 70, 600)
    except RuntimeError:
        if backend == 'cpu':
            raise
        print('  CUDA torch 실패 → CPU torch 로 다시', flush=True)
        _run_lines([uv, 'pip', 'install', '--python', _py(), '--torch-backend', 'cpu', '--reinstall-package', 'torch'] + PKGS,
                   'AI 패키지 내려받기 (CPU)', 8, 70, 400)
    chk = subprocess.run([str(_py()), '-c', 'import json,torch;c=torch.cuda.is_available();'
                          'print(json.dumps({"torch":torch.__version__,"cuda":bool(c and (torch.ones(2,device="cuda")*2).sum().item()==4),'
                          '"gpu_used":torch.cuda.get_device_name(0) if c else ""}))'],
                         capture_output=True, env=_env(), creationflags=NO_WINDOW)
    try:
        info = json.loads(chk.stdout.decode('utf-8', 'replace').strip().splitlines()[-1])
    except (ValueError, IndexError):
        raise RuntimeError('torch 확인 실패 · ' + chk.stderr.decode('utf-8', 'replace')[-300:])
    print('  torch %s · CUDA %s %s' % (info['torch'], '됨' if info['cuda'] else '안 됨', info.get('gpu_used', '')), flush=True)
    _run_lines([_py(), '-X', 'utf8', '-u', '-s', _base() / WORKER, 'fetch', '--models', json.dumps(MODELS)],
               'AI 모델 내려받기 (약 3 GB)', 70, 97, 240)
    subprocess.run([str(uv), 'cache', 'clean'], capture_output=True, env=_env(), creationflags=NO_WINDOW)   # 받은 wheel 지움 (venv 는 복사본)
    shutil.rmtree(AI_DIR / 'cache', ignore_errors=True)
    shutil.rmtree(AI_DIR / 'models' / 'hub', ignore_errors=True)       # 옛 초안 (HF 캐시 꼴) 모델
    info.update(made=time.strftime('%Y-%m-%d %H:%M'), models=MODELS, setup_sec=int(time.time() - t0))
    with open(AI_DIR / 'ready.json', 'w', encoding='utf-8') as fp:
        json.dump(info, fp, ensure_ascii=False, indent=1)
    _say(100, 'AI 엔진 준비 끝')
    return status()


# ---------------------------------------------------------------- 결과 파일
def _read_js(path, var):
    t = open(path, encoding='utf-8').read()
    return json.loads(t[t.index('{'):t.rindex('}') + 1]) if var in t else None


def project_origin(project):
    """3D 화면 원점 (data/mapping.js · 없으면 data/mesh.json)."""
    for fn, var in (('mapping.js', 'RV_MAPPING'), ('mesh.json', '')):
        p = os.path.join(project, 'data', fn)
        if os.path.exists(p):
            try:
                d = _read_js(p, var) if var else json.load(open(p, encoding='utf-8'))
                if d and d.get('origin'):
                    return [float(v) for v in d['origin']], d.get('crs', 'EPSG:5186')
            except (OSError, ValueError):
                pass
    raise RuntimeError('매핑 결과(data/mapping.js) 없음 → 3D 매핑을 먼저')


def load(project):
    p = os.path.join(project, 'data', 'ai_budae.json')
    if os.path.exists(p):
        try:
            return json.load(open(p, encoding='utf-8'))
        except (OSError, ValueError):
            return None
    return None


# 3D 매핑 ↔ 360 짝 (같은 구간) — 프로그램 설정 폴더에만 (프로젝트 폴더엔 경로를 안 씀 = 웹 공유에 안 나감)
LINKS = Path(os.environ.get('APPDATA', str(Path.home()))) / 'DH.Roadview' / 'ai_links.json'


def _links():
    try:
        m = json.load(open(LINKS, encoding='utf-8'))
        return m if isinstance(m, dict) else {}
    except (OSError, ValueError):
        return {}


def link(a, b):
    """두 프로젝트를 짝으로 (어느 쪽에서 확인 · 저장해도 다른 쪽에 같이 씀)."""
    m = _links()
    a, b = os.path.abspath(a), os.path.abspath(b)
    m[os.path.normcase(a)] = b
    m[os.path.normcase(b)] = a
    LINKS.parent.mkdir(parents=True, exist_ok=True)
    tmp = str(LINKS) + '.tmp'
    with open(tmp, 'w', encoding='utf-8') as fp:
        json.dump(m, fp, ensure_ascii=False, indent=1)
    os.replace(tmp, LINKS)


def twin(project):
    return _links().get(os.path.normcase(os.path.abspath(project)))


def save(project, data):
    """저장 + 짝 프로젝트도 (같은 인식 결과 = made 가 같을 때만 · 다른 인식이면 덮지 않음)."""
    _write(project, data)
    tw = twin(project)
    if tw and os.path.isdir(os.path.join(tw, 'data')):
        old = load(tw)
        if old is None or old.get('made') == data.get('made'):
            try:
                _write(tw, data)
            except OSError:
                pass


def _write(project, data):
    """ai_budae.json + ai_budae.js (같은 내용) — 화면 확인 결과 · 인식 결과 모두. 반쯤 쓴 파일이 남지 않게 바꿔치기."""
    d = os.path.join(project, 'data')
    os.makedirs(d, exist_ok=True)
    txt = json.dumps(data, ensure_ascii=False, separators=(',', ':'))
    for fn, body in (('ai_budae.json', txt), ('ai_budae.js', 'window.RV_AI = ' + txt + ';\n')):
        tmp = os.path.join(d, fn + '.tmp')
        with open(tmp, 'w', encoding='utf-8') as fp:
            fp.write(body)
        os.replace(tmp, os.path.join(d, fn))


def _centroid(f):
    pts = f.get('pts') or []
    if not pts:
        return None
    return (sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts))


def _near(a, b, tol):
    """두 후보가 같은 자리인가 — 같은 종류 · 점들 사이 가장 가까운 거리 ≤ tol (선) / 가운데가 상대 다각형 안 (면)."""
    if a.get('cls') != b.get('cls'):
        return False
    pa, pb = a.get('pts') or [], b.get('pts') or []
    if not pa or not pb:
        return False
    if a.get('kind') == 'area' and b.get('kind') == 'area':
        ca, cb = _centroid(a), _centroid(b)
        return _inside(ca, pb) or _inside(cb, pa)
    step_a = pa[::max(1, len(pa) // 40)]
    step_b = pb[::max(1, len(pb) // 40)]
    hit = sum(1 for p in step_a if min(math.hypot(p[0] - q[0], p[1] - q[1]) for q in step_b) <= tol)
    return hit >= 0.5 * len(step_a)


def _inside(pt, poly):
    x, y = pt
    c = False
    j = len(poly) - 1
    for i in range(len(poly)):
        xi, yi, xj, yj = poly[i][0], poly[i][1], poly[j][0], poly[j][1]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / ((yj - yi) or 1e-12) + xi:
            c = not c
        j = i
    return c


def merge(old, new):
    """사람이 고른 것(ok · no)은 그대로 · 그 자리와 겹치는 새 AI 후보는 버림 · 새 id 는 겹치지 않게."""
    keep = [f for f in (old or {}).get('features', []) if f.get('status') in ('ok', 'no')]
    used = {f.get('id') for f in keep}
    out = list(keep)
    n = 0
    for f in new.get('features', []):
        if any(_near(f, k, 1.0) for k in keep):
            continue
        n += 1
        while 'f%d' % n in used:
            n += 1
        f['id'] = 'f%d' % n
        used.add(f['id'])
        out.append(f)
    new['features'] = out
    return new, len(keep)


def _is360(d):
    return os.path.isfile(os.path.join(d, 'data', 'project.js')) and not os.path.isfile(os.path.join(d, 'data', 'mapping.js'))


def find_360(p360, origin, reach=600.0):
    """고른 폴더가 360 프로젝트면 그대로 · 아니면 안쪽 (두 단계) 360 프로젝트 중 매핑 원점 둘레 (600 m) 사진이 가장 많은 것.
    1127 실측: 원본 OSV 폴더 (AVATA360) 를 골라 360 근거 0 건 → 도랑 흙더미가 석축으로 남음."""
    import glob
    if not p360 or not os.path.isdir(p360):
        return None, '360 폴더 없음'
    if _is360(p360):
        return os.path.abspath(p360), ''
    best, bn = None, 0
    for pj in glob.glob(os.path.join(glob.escape(p360), '*', 'data', 'project.js')) + glob.glob(os.path.join(glob.escape(p360), '*', '*', 'data', 'project.js')):
        d = os.path.dirname(os.path.dirname(pj))
        if not _is360(d):
            continue
        try:
            pr = _read_js(pj, 'RV_PROJECT') or {}
            E0, N0 = float(pr['origin']['E']), float(pr['origin']['N'])
            n = sum(1 for s in pr.get('shots', []) if math.hypot(s['e'] + E0 - origin[0], s['n'] + N0 - origin[1]) <= reach)
        except (OSError, KeyError, TypeError, ValueError):
            continue
        if n > bn:
            best, bn = os.path.abspath(d), n
    if not best:
        return None, '고른 폴더에 360 프로젝트 없음 (로드뷰 폴더를 고르기)'
    return best, '고른 폴더 안 360 프로젝트 «%s» 씀 (매핑 둘레 사진 %d장)' % (os.path.basename(best), bn)


def run(project, p360=None, cell=0.05, dets=None, zone=None):
    """인식 실행 (환경이 없으면 먼저 준비) → 결과 경로. 진행은 «@진행» 줄.
    zone = 임시점용 영역 json 경로 (화면이 만든 다각형 · 그 영역 + 여유만 인식 · JACK 1127) · 없으면 정사 전체."""
    project = os.path.abspath(project)
    origin, crs = project_origin(project)
    if not status()['ready']:                   # 엔진 없음 → 먼저 설치 (막대 0~40 %) · 인식은 40~100 %
        _SPAN[:] = [0.0, 40.0]
        ensure_env()
        _SPAN[:] = [40.0, 100.0]
    ortho = os.path.join(project, '결과물', '정사영상.tif')
    if not os.path.exists(ortho):
        raise RuntimeError('정사영상 없음 → 매핑 결과물에서 «정사영상» 을 골라 다시 만들기')
    if p360:
        p360, why = find_360(p360, origin)
        if why:
            print('  %s%s' % (why, '' if p360 else ' → 360 근거 없이'), flush=True)
    work = os.path.join(project, 'work', 'ai')
    os.makedirs(work, exist_ok=True)
    raw = os.path.join(work, 'result_raw.json')
    if os.path.exists(raw):
        os.remove(raw)
    args = [_py(), '-X', 'utf8', '-u', '-s', _base() / WORKER, 'run', '--project', project, '--origin', ','.join('%.4f' % v for v in origin),
            '--out', raw, '--cell', str(cell), '--models', json.dumps(MODELS)]
    if p360:
        args += ['--p360', os.path.abspath(p360)]
    if dets:
        args += ['--debug', dets]
    if zone:
        if not os.path.exists(zone):
            raise RuntimeError('임시점용 영역 파일 없음')
        args += ['--zone', os.path.abspath(zone)]
    env = _env()
    env['HF_HUB_OFFLINE'] = '1'                 # 모델은 준비 때 받음 (작업 중엔 인터넷 안 씀)
    t0 = time.time()
    pr = subprocess.Popen([str(a) for a in args], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=env, creationflags=NO_WINDOW)
    tail = []
    for b in iter(pr.stdout.readline, b''):
        s = b.decode('utf-8', errors='replace').rstrip()
        if s:
            _relay(s)
            tail = (tail + [s])[-8:]
    if pr.wait() != 0 or not os.path.exists(raw):
        raise RuntimeError('AI 인식 실패 · ' + ' / '.join(tail[-3:]))
    new = json.load(open(raw, encoding='utf-8'))
    new.pop('run', None)                        # 걸린 시간 · 그래픽 메모리 등은 work/ai/result_raw.json 에만 (결과 파일은 정한 꼴 그대로)
    new.update(version=1, made=time.strftime('%Y-%m-%d %H:%M'), crs=crs, origin=origin,
               src360=os.path.basename(os.path.normpath(p360)) if p360 else None, classes=CLASSES)
    data, kept = merge(load(project), new)
    _write(project, data)
    if p360:                                    # 같은 구간 360 에도 (360 화면 «AI 부대공» · 웹 공유에도 보임) · 짝 기억 (확인 저장을 같이)
        try:
            _write(p360, dict(data, src3d=os.path.basename(os.path.normpath(project))))
            link(project, p360)
            print('  360 «%s» 에도 넣음' % os.path.basename(os.path.normpath(p360)), flush=True)
        except OSError as ex:
            print('  360 에 못 넣음 (%s)' % ex, flush=True)
    n = {}
    for f in data['features']:
        n[f['cls']] = n.get(f['cls'], 0) + 1
    print('AI 부대공 후보 %d개 (%s) · 사람이 고른 것 %d개 남김 · %d초' % (
        len(data['features']), ' · '.join('%s %d' % (CLASSES.get(k, k), v) for k, v in n.items()), kept, time.time() - t0), flush=True)
    _say(100, '끝')
    return os.path.join(project, 'data', 'ai_budae.json')


def main(argv=None):
    ap = argparse.ArgumentParser(prog='rv_cli ai')
    sub = ap.add_subparsers(dest='cmd')
    s = sub.add_parser('setup')
    s.add_argument('--force', action='store_true')
    r = sub.add_parser('run')
    r.add_argument('project')
    r.add_argument('--p360', default=None)
    r.add_argument('--cell', type=float, default=0.05)
    r.add_argument('--debug', default=None, help='확인 그림 폴더')
    r.add_argument('--zone', default=None, help='임시점용 영역 json (화면이 만듦) · 없으면 정사 전체')
    sub.add_parser('status')
    a = ap.parse_args(argv)
    try:                                        # 진행 줄 (한글 · 받기 막대 글자) 이 cp949 창에서 끊기지 않게
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass
    try:
        if a.cmd == 'setup':
            print(json.dumps(ensure_env(a.force), ensure_ascii=False), flush=True)
        elif a.cmd == 'run':
            print(run(a.project, a.p360, a.cell, a.debug, a.zone), flush=True)
        elif a.cmd == 'status':
            print(json.dumps(status(), ensure_ascii=False), flush=True)
        else:
            ap.print_help()
            return 2
    except Exception as ex:                     # noqa: BLE001 — 알기 쉬운 글 한 줄
        print('실패: %s' % ex, flush=True)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
