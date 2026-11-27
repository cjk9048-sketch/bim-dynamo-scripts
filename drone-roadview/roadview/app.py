"""드론 로드뷰 — 설치형 프로그램 (pywebview · WebView2 창).

첫 화면(app_home) : 프로젝트 목록 · 새로 만들기(OSV/JPG → 간격 m) · 열기
프로젝트 화면    : 만들어진 폴더의 index.html (보기 화면) — window.pywebview.api 로 아래 Api 를 부른다
                   관로 가져오기 · 맞춤 저장 · 웹(NAS)/폴더로 내보내기 · 처음 화면
무거운 일(만들기 · NAS 올리기)은 따로 프로세스로 돌리고 진행 줄을 읽는다 (창이 안 멈추게).
"""
from __future__ import annotations

import json
import math
import os
import re
import shutil
import subprocess
import sys
import threading
import time
import uuid
from pathlib import Path

APP_NAME = 'DH 에어로사이트'   # JACK 1127 (드론 로드뷰 → 360 · 3D 매핑 · 노선검토 · 실행 파일 · 설정 폴더 이름은 그대로)
APP_VERSION = '1.0.25'
FROZEN = getattr(sys, 'frozen', False)
BASE = Path(sys._MEIPASS) if FROZEN else Path(__file__).resolve().parent.parent      # noqa: SLF001
HOME_HTML = (BASE / 'roadview' / 'app_home' / 'index.html') if not FROZEN else (BASE / 'app_home' / 'index.html')
TUT_USER = Path(os.environ.get('LOCALAPPDATA', str(Path.home()))) / 'DH.Roadview' / 'tutorial'   # 튜토리얼을 열 때 복사해 두는 곳


def _tut_dir():
    """튜토리얼 원본 폴더 (설치본 = exe 옆 tutorial · 개발 = drone-roadview/tutorial · 저장소엔 안 올림)."""
    cands = [Path(sys.executable).parent / 'tutorial', BASE / 'tutorial'] if FROZEN else [BASE / 'tutorial']
    return next((c for c in cands if c.is_dir()), None)


def _tut_meta(d):
    try:
        return json.loads((Path(d) / 'tutorial.json').read_text(encoding='utf-8'))
    except (OSError, ValueError):
        return {}


def _docs_dir():
    """문서 폴더 (OneDrive 로 옮긴 것도 · Windows 셸 폴더) → 못 읽으면 home\\Documents."""
    if os.name == 'nt':
        try:
            import ctypes
            buf = ctypes.create_unicode_buffer(1024)
            if ctypes.windll.shell32.SHGetFolderPathW(None, 5, None, 0, buf) == 0 and buf.value:   # 5 = CSIDL_PERSONAL (문서)
                return Path(buf.value)
        except Exception:                                # noqa: BLE001
            pass
    return Path.home() / 'Documents'


def _is_tut(path):
    """튜토리얼 작업 복사본 (TUT_USER\\<이름>) 인가 — 최근 목록에 안 넣음 · [저장] = 일반 프로젝트로 저장 (JACK 1127)."""
    try:
        return bool(path) and os.path.normcase(str(Path(path).resolve().parent)) == os.path.normcase(str(TUT_USER.resolve()))
    except OSError:
        return False


def _mirror(src, dst):
    """dst 를 src 와 똑같이 (튜토리얼을 열 때마다 처음 상태로 · JACK 1127 «처음으로 돌아가지지가 않아»).
    크기 · 시각이 다른 파일만 다시 복사 · src 에 없는 파일 · 폴더 (튜토리얼에서 새로 생긴 것) 는 지움 → 400 MB 를 통째 복사하지 않음."""
    src, dst = Path(src), Path(dst)
    for root, dirs, files in os.walk(src):
        r = Path(root)
        d = dst / r.relative_to(src)
        d.mkdir(parents=True, exist_ok=True)
        for fn in files:
            a, b = r / fn, d / fn
            sa = a.stat()
            try:
                sb = b.stat()
                same = sb.st_size == sa.st_size and abs(sb.st_mtime - sa.st_mtime) < 0.01
            except OSError:
                same = False
            if not same:
                shutil.copy2(a, b)
    for root, dirs, files in os.walk(dst, topdown=False):
        r = Path(root)
        s = src / r.relative_to(dst)
        for fn in files:
            if not (s / fn).is_file():
                (r / fn).unlink()
        if not s.is_dir():
            r.rmdir()
CFG_DIR = Path(os.environ.get('APPDATA', str(Path.home()))) / 'DH.Roadview'
PROJ_LIST = CFG_DIR / 'projects.json'
CLUSTER_CFG = CFG_DIR / 'cluster.json'     # 함께 처리 (공유 폴더 · 내 매핑에 쓰기 · 도우미 모드)
TPL_DIR = CFG_DIR / 'map_templates'        # 매핑 화면 «매개변수를 템플릿으로 저장» (이름.json · Terra 꼴 · JACK 1126)
NO_WINDOW = 0x08000000 if os.name == 'nt' else 0
# 파일 창 종류 이름은 글자 · 숫자 · 빈칸만 (pywebview 검사 [\w ]+ — «·» 하나로 창이 안 열렸음 · 1123)
FILTER_INPUTS = ('DJI 360 영상 사진 (*.osv;*.OSV;*.jpg;*.JPG)', '모든 파일 (*.*)')
FILTER_PIPES = ('관로 SHP DXF (*.shp;*.SHP;*.dxf;*.DXF)', '모든 파일 (*.*)')
FILTER_OBST = ('지장물 SHP (*.shp;*.SHP)', '모든 파일 (*.*)')
FILTER_SHP_SAVE = ('SHP (*.shp)',)
FILTER_GCP = ('GCP CSV TXT (*.csv;*.CSV;*.txt;*.TXT)', '모든 파일 (*.*)')
FILTER_PNG_SAVE = ('PNG (*.png)',)
FILTER_MAPPHOTO = ('매핑 사진 JPG (*.jpg;*.JPG;*.jpeg;*.JPEG)', '모든 파일 (*.*)')
MAP_OUTS = ('mesh', 'gs', 'ortho', 'dsm', 'dtm', 'las', 'contour')    # 매핑 결과물 열쇠 (gs = 가우시안 스플래팅 · gsplat.py)
MAP_OPT_KEYS = ('feat', 'ortho_res', 'ortho_fill', 'mesh_ratio', 'mesh_formats', 'las_density', 'las_classify', 'dem_cell',
                'contour_base', 'contour_label', 'contour_clean', 'tin_step')   # 매핑 세부 설정 (화면 · args.json · _opt_args)


def _cli(cmd, args):
    """하위 일 (만들기 · 올리기) 명령줄."""
    if FROZEN:
        exe = Path(sys.executable).with_name('rv_cli.exe')
        return [str(exe), cmd] + args, None
    py = sys.executable
    if py.lower().endswith('pythonw.exe'):
        cand = py[:-len('pythonw.exe')] + 'python.exe'
        if os.path.exists(cand):
            py = cand
    mod = {'build': 'roadview.build', 'share': 'roadview.share', 'map': 'roadview.mapping', 'helper': 'roadview.cluster',
           'ai': 'roadview.ai_budae'}[cmd]
    return [py, '-u', '-m', mod] + args, str(BASE)


def _page_url(folder):
    for fn in ('index.html', '로드뷰.html'):
        p = Path(folder) / fn
        if p.exists():
            return p.resolve().as_uri()
    return None


class Job:
    def __init__(self, kind, args, cwd, out=None):
        self.id = uuid.uuid4().hex[:10]
        self.kind = kind
        self.out = out
        self.state = 'run'
        self.pct = 0.0
        self.line = '시작…'
        self.url = ''
        self.lines = []
        self.total = 0
        self.t0 = time.time()
        self.label = ''          # 만들기가 알려 준 지금 단계 (@진행 줄)
        self.eta_at = None       # 끝날 시각 (time.time 기준) · 모르면 None
        self.stage = None        # «@단계 <시작 %> <끝 %> <어림 초>» — 엔진 진행 글이 없는 동안 막대를 시간으로 밀어 줌 (JACK 1126 «한꺼번에 쑥쑥»)
        self.inner = False       # 이 단계 안에서 엔진 진행(@진행)을 받았으면 그 값을 씀
        env = dict(os.environ, PYTHONIOENCODING='utf-8')
        self.proc = subprocess.Popen(args, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                     creationflags=NO_WINDOW, env=env)
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self):
        for raw in iter(self.proc.stdout.readline, b''):
            s = raw.decode('utf-8', errors='replace').rstrip()
            if s.startswith('@진행 '):
                self._progress(s)
                continue
            if s.startswith('@단계 '):
                try:
                    _, a0, a1, sec = s.split(' ', 3)
                    self.stage = (float(a0), float(a1), max(1.0, float(sec)), time.time())
                    self.inner = False
                except ValueError:
                    self.stage = None
                continue
            self.lines.append(s)
            if len(self.lines) > 400:
                self.lines = self.lines[-300:]
            self._parse(s)
        code = self.proc.wait()
        if self.state == 'run':
            self.state = 'done' if code == 0 else 'fail'
            if code == 0:
                self.pct = 100.0
            elif not self.line.startswith('멈춤'):
                self.line = '실패 · ' + (self.lines[-1] if self.lines else '')

    def _progress(self, s):
        """«@진행 <퍼센트> <남은 초 | -1> <글>» — 만들기가 단계마다 잰 빠르기로 셈한 값."""
        try:
            _, pct, left, label = s.split(' ', 3)
            self.inner = self.stage is not None and label.strip() == self.label and float(pct) > self.stage[0] + 1e-6
            self.pct = max(self.pct, float(pct))
            left = int(left)
            # 끝 시각을 바로 바꾸지 않고 천천히 따라감 (JACK 1127 «남은 시간이 올라갔다 내려갔다 신뢰성 떨어져»):
            # 새 값이 이르면 25 % · 늦으면 8 % 만 옮김 · 처음 · 크게 벗어나면 (지금보다 앞 · 2 배 넘게 늦음) 바로 씀
            if left >= 0:
                now, new = time.time(), time.time() + left
                old = self.eta_at
                if old is None or old <= now or new - now > 2 * (old - now) + 120:
                    self.eta_at = new
                else:
                    self.eta_at = old + (new - old) * (0.25 if new < old else 0.08)
            self.label = label.strip()
        except ValueError:
            pass

    def _left_text(self):
        if self.eta_at is None:
            return '남은 시간 재는 중'
        r = self.eta_at - time.time()
        if r < 60:
            return '곧 끝'
        m = int(math.ceil(r / 60.0))
        hm = ('%d시간 %d분' % divmod(m, 60)) if m >= 60 else ('%d분' % m)
        return '남은 %s · %s 끝' % (hm, time.strftime('%H:%M', time.localtime(self.eta_at)))

    def _shown_pct(self):
        """막대 = 받은 퍼센트 · 엔진 진행 글이 없는 단계는 단계 어림 시간에 따라 단계 끝 9할까지 천천히."""
        p = self.pct
        if self.state == 'run' and self.stage and not self.inner:
            a0, a1, sec, t = self.stage
            p = max(p, a0 + (a1 - a0) * min(0.9, (time.time() - t) / sec))
        return min(99.5, p) if self.state == 'run' else p

    def _parse(self, s):
        el = time.time() - self.t0
        if self.kind in ('map', 'ai'):              # 진행 · 남은 시간은 «@진행» 줄 (mapping.Eta · ai_budae)
            self.line = s.split(' ', 1)[-1].strip()[:100]
            return
        if self.kind == 'share':
            m = re.search(r'(\d+)\s*/\s*(\d+)', s)
            if m and int(m.group(2)) > 0:
                self.pct = 100.0 * int(m.group(1)) / int(m.group(2))
            if s.startswith('주소:'):
                self.url = s.split(':', 1)[1].strip()
            self.line = s.strip()[:100]
            return
        m = re.search(r'360 사진 (\d+)장', s)
        if m:
            self.total = int(m.group(1))
            self.line = '360 사진 만드는 중'
        m = re.match(r'^\S+\s+(\d+) / (\d+)\s+\(', s)
        if m and self.total:
            done, tot = int(m.group(1)), int(m.group(2))
            if not self.label:                 # 옛 만들기 (@진행 줄 없음)
                self.pct = 5 + 95.0 * done / max(1, tot)
            self.line = '360 사진 %d / %d' % (done, tot)
        elif '높이 ' in s and '초:' in s:
            self.line = '땅 높이 재는 중 · %d초' % el
        elif '읽는 중' in s:
            self.line = s.split(' ', 1)[1][:80]
        elif '수치지도' in s:
            self.line = s.split(' ', 1)[1][:80]

    def stop(self):
        if self.proc.poll() is None:
            subprocess.run(['taskkill', '/PID', str(self.proc.pid), '/T', '/F'], capture_output=True,
                           creationflags=NO_WINDOW)
            self.state = 'fail'
            self.line = ('멈춤 · 다시 [3D 매핑] 누르면 끝난 단계는 그대로 쓰고 이어 함' if self.kind == 'map'
                         else '멈춤 · 다시 [만들기] 누르면 만든 사진은 그대로 쓰고 이어 함')

    def info(self):
        line, pill = self.line, ''
        if self.state == 'run' and self.label:
            pill = self._left_text()
            line = self.label + ' · ' + pill
        left = int(max(0, self.eta_at - time.time())) if (self.state == 'run' and self.eta_at) else -1
        return {'id': self.id, 'state': self.state, 'pct': round(self._shown_pct(), 1), 'line': line, 'pill': pill,
                'url': self.url, 'out': self.out, 'log': self.lines[-12:],
                'label': self.label, 'left': left, 'elapsed': int(time.time() - self.t0),
                'eta': time.strftime('%H:%M', time.localtime(self.eta_at)) if left >= 0 else ''}


class HelperProc:
    """도우미 모드 (rv_cli helper · cluster.py) — «@도우미 {상태}» 줄을 읽음 · 앱이 꺼지면 도우미도 끝 (--parent-pid)."""

    def __init__(self, args, cwd):
        self.st = {'state': 'idle', 'job': '', 'pct': 0, 'done': 0, 'msg': '시작'}
        self.lines = []
        env = dict(os.environ, PYTHONIOENCODING='utf-8')
        self.proc = subprocess.Popen(args, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                     creationflags=NO_WINDOW, env=env)
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self):
        for raw in iter(self.proc.stdout.readline, b''):
            s = raw.decode('utf-8', errors='replace').rstrip()
            if s.startswith('@도우미 '):
                try:
                    self.st.update(json.loads(s.split(' ', 1)[1]))
                except ValueError:
                    pass
                continue
            self.lines = (self.lines + [s])[-40:]
        self.proc.wait()

    def alive(self):
        return self.proc.poll() is None

    def stop(self):
        if self.alive():
            subprocess.run(['taskkill', '/PID', str(self.proc.pid), '/T', '/F'], capture_output=True, creationflags=NO_WINDOW)

    def info(self):
        d = dict(self.st)
        if not self.alive():
            d.update(state='off', msg=(self.lines[-1] if self.lines else '꺼짐')[:100])
        d['log'] = self.lines[-8:]
        return d


class Api:
    def __init__(self):
        self._window = None
        self._project = None
        self._jobs = {}
        self._media = {}
        self._helper = None      # 도우미 모드 (HelperProc)
        self._opened = 0         # 프로젝트를 연 시각 (화면이 «튜토리얼에서 고친 것» 기억을 이번에 연 것만 쓰게)
        self._close_ok = False   # 창 닫기 허락 (화면이 저장 · 저장 안 함을 고른 뒤)
        self._ask_t, self._ask_ans = 0.0, True

    # ------------------------------------------------------------ 공통
    def app_info(self):
        key = ''
        try:
            from .keys import vworld_key
            key = vworld_key()[0] or ''
        except Exception:
            pass
        return {'name': APP_NAME, 'version': APP_VERSION, 'vworld': key}

    def copy_text(self, text):
        try:
            subprocess.run(['clip'], input=str(text).encode('utf-16le'), creationflags=NO_WINDOW, check=True)
            return {'ok': True}
        except Exception:
            return {'ok': False}

    def job(self, jid):
        j = self._jobs.get(jid)
        return j.info() if j else {'state': 'fail', 'line': '작업 없음'}

    def stop_job(self, jid):
        j = self._jobs.get(jid)
        if j:
            j.stop()
        return {'ok': True}

    # ------------------------------------------------------------ 프로젝트 목록
    def _load_list(self):
        try:
            return json.loads(PROJ_LIST.read_text(encoding='utf-8'))
        except Exception:
            return []

    def _save_list(self, lst):
        CFG_DIR.mkdir(parents=True, exist_ok=True)
        PROJ_LIST.write_text(json.dumps(lst, ensure_ascii=False, indent=1), encoding='utf-8')

    def _remember(self, folder):
        folder = str(Path(folder).resolve())
        if _is_tut(folder):                             # 튜토리얼 작업 복사본 = 최근 목록에 안 넣음 ([저장] = 일반 프로젝트로 저장 · JACK 1127)
            return
        info = {'path': folder, 'title': Path(folder).name, 'shots': 0, 'updated': time.strftime('%Y-%m-%d %H:%M')}
        mp = Path(folder) / 'data' / 'mapping.js'
        if mp.exists():                                 # 3D 매핑 프로젝트: 썸네일 = 정사영상 미리보기
            try:
                M = json.loads(mp.read_text(encoding='utf-8').split('=', 1)[1].strip().rstrip(';'))
                info.update(title=M.get('title') or info['title'], shots=M.get('photos', 0), kind='mapping')
                for t in (Path(folder) / 'data' / '미리보기.png', Path(folder) / '결과물' / '정사영상_미리보기.png'):
                    if t.exists():
                        info['thumb'] = str(t)
                        break
            except Exception:
                pass
        try:
            s = (Path(folder) / 'data' / 'project.js').read_text(encoding='utf-8')
            P = json.loads(s.split('=', 1)[1].strip().rstrip(';'))
            info['title'] = P.get('title') or info['title']
            info.setdefault('kind', '360')
            shots = P.get('shots', [])
            info['shots'] = len(shots)
            info['videos'] = len(P.get('videos', []))
            if shots:                                   # 썸네일 = 가운데쯤 촬영점 · 컷 = 고르게 6장
                t = Path(folder) / shots[len(shots) // 2].get('thumb', '')
                if t.exists():
                    info['thumb'] = str(t)
                k = max(1, len(shots) // 6)
                info['cuts'] = [str(Path(folder) / s_['thumb']) for s_ in shots[::k][:6]
                                if (Path(folder) / s_.get('thumb', '')).exists()]
        except Exception:
            pass
        try:
            size = 0
            for root, _, fs in os.walk(Path(folder) / 'data'):
                for fn in fs:
                    size += os.path.getsize(os.path.join(root, fn))
            info['size_gb'] = round(size / 1e9, 2)
        except OSError:
            pass
        lst = [x for x in self._load_list() if os.path.normcase(x['path']) != os.path.normcase(folder)]
        lst.insert(0, info)
        self._save_list(lst[:50])

    def list_projects(self):
        out = []
        for x in self._load_list():
            x = dict(x)
            if _is_tut(x.get('path')):                  # 옛 판이 넣어 둔 튜토리얼 복사본 = 안 보임 (JACK 1127)
                continue
            x['exists'] = bool(_page_url(x['path']))
            t = x.get('thumb')
            x['thumb_url'] = Path(t).as_uri() if t and os.path.exists(t) else ''
            x['cut_urls'] = [Path(c).as_uri() for c in x.get('cuts', []) if os.path.exists(c)]
            if x.get('kind') not in ('mapping', '360'):       # 옛 기록 = 폴더를 보고 (3D 매핑 = data/mapping.js)
                x['kind'] = 'mapping' if (Path(x['path']) / 'data' / 'mapping.js').exists() else '360'
            out.append(x)
        return out

    # ------------------------------------------------------------ 튜토리얼 (JACK 1127 «매핑된 결과는 튜토리얼로 프로그램에 이미 담겨있으면 · 3d · 360 둘 다»)
    # 설치본 = 프로그램 폴더 tutorial\<이름>\ (보기 자료만 · tutorial.json = 제목 · 설명 · 판) · 열 때마다 사용자 폴더로 처음 상태 복사 (설치 폴더는 쓰기 안 됨)
    # 고친 것은 [저장] = «일반 프로젝트로 저장» (save_as · 문서\DH 에어로사이트\이름) 해야 남음 · 저장 안 하고 나가면 다음에 다시 처음 상태 (JACK 1127)
    def tutorials(self):
        out = []
        d0 = _tut_dir()
        if not d0:
            return out
        for d in sorted(d0.iterdir()):
            if not d.is_dir() or not (d / 'index.html').exists():
                continue
            meta = _tut_meta(d)
            kind = 'mapping' if (d / 'data' / 'mapping.js').exists() else '360'
            th = d / meta.get('thumb', '')
            out.append({'key': d.name, 'kind': kind, 'title': meta.get('title') or d.name, 'desc': meta.get('desc', ''),
                        'thumb_url': th.as_uri() if meta.get('thumb') and th.is_file() else ''})
        out.sort(key=lambda x: x['kind'] != 'mapping')     # 3D 매핑 먼저 (처음 화면 카드 차례와 같게)
        return out

    def open_tutorial(self, key):
        d0 = _tut_dir()
        names = {d.name for d in d0.iterdir() if d.is_dir()} if d0 else set()
        if not key or key not in names:                  # 이름만 받음 (경로 넘기기 막음)
            return {'ok': False, 'msg': '튜토리얼 없음'}
        def put(k):                                      # 원본 → 사용자 폴더 (열 때마다 처음 상태 · 바뀐 파일만 다시 복사 · 새로 생긴 파일은 지움)
            src, dst = d0 / k, TUT_USER / k
            _mirror(src, dst)
            return dst
        try:
            dst = put(key)
            pair = _tut_meta(d0 / key).get('pair')
            if pair in names:                            # 같은 구간 3D ↔ 360 짝: 같이 복사 · AI 확인을 같이 저장 · 3D 화면 «사진» = 360 튜토리얼 사진
                pd = put(pair)
                try:
                    from . import ai_budae
                    ai_budae.link(str(dst), str(pd))
                    for a, b in ((dst, pd), (pd, dst)):
                        dd = ai_budae.load(str(a))
                        if dd and (a / 'data' / 'mapping.js').exists() and dd.get('src360') != str(b):
                            dd['src360'] = str(b)
                            ai_budae.save(str(a), dd)
                except Exception:                        # noqa: BLE001 — 짝이 안 돼도 튜토리얼은 열림
                    pass
        except OSError as ex:
            return {'ok': False, 'msg': '튜토리얼 복사 못 함 · %s' % str(ex)[:80]}
        return self.open_project(str(dst))

    # ------------------------------------------------------------ 저장 (JACK 1127 «작업 끝나고 [저장] 해야 저장 · 튜토리얼은 저장 = 일반 프로젝트로»)
    # 보기 화면이 고친 것을 모았다가 [저장] 때 save_pipes · save_grade · save_calib · ai_save 를 부르고 mark_saved · 튜토리얼이면 save_as.
    def project_info(self):
        """{path, tutorial, title, opened (연 시각 · 화면 기억 구분), name · parent (튜토리얼 기본 저장 이름 · 위치)}."""
        p = self._project
        if not p:
            return {'path': '', 'tutorial': False, 'title': '', 'opened': 0}
        tut = _is_tut(p)
        return {'path': p, 'tutorial': tut, 'title': Path(p).name, 'opened': self._opened,
                'name': Path(p).name + time.strftime('_%Y%m%d_%H%M') if tut else '',
                'parent': str(_docs_dir() / APP_NAME) if tut else ''}

    def mark_saved(self):
        """[저장] 으로 파일을 다 쓴 뒤 → 최근 목록 맨 위 · 시각 (튜토리얼 복사본은 안 넣음)."""
        if self._project:
            self._remember(self._project)
        return {'ok': True}

    def save_as(self, opts=None):
        """튜토리얼 [저장] = 일반 프로젝트로: 작업 복사본 (화면이 모은 것을 먼저 씀) → 부모\\이름 통째 복사 · 짝 (3D ↔ 360) 도 같은 부모에
        · AI 짝 새로 · 3D AI 의 src360 = 새 360 · 최근 목록 → 그 프로젝트로 다시 엶 (open = False 면 안 엶 · [처음] · 창 닫기 때)."""
        o = opts if isinstance(opts, dict) else {}
        if not self._project or not _is_tut(self._project):
            return {'ok': False, 'msg': '튜토리얼이 아님'}
        cur = Path(self._project)
        name = re.sub(r'[\\/:*?"<>|]+', '_', str(o.get('name') or '')).strip().strip('.')[:80] or cur.name + time.strftime('_%Y%m%d_%H%M')
        parent = Path(str(o.get('parent') or '').strip() or str(_docs_dir() / APP_NAME))
        tut_root = os.path.normcase(str(TUT_USER.resolve()))
        if os.path.normcase(str(parent.resolve())).startswith(tut_root):
            return {'ok': False, 'msg': '튜토리얼 폴더 안에는 저장 못 함'}

        def free(p):                                     # 같은 이름이 있으면 _2 · _3 …
            q, k = p, 2
            while q.exists():
                q, k = p.with_name('%s_%d' % (p.name, k)), k + 1
            return q
        pk = _tut_meta(cur).get('pair')
        pair = TUT_USER / pk if pk and (TUT_USER / pk / 'index.html').exists() else None
        skip = shutil.ignore_patterns('.tutorial_ver', 'tutorial.json', '*.tmp')
        pd = None
        try:
            parent.mkdir(parents=True, exist_ok=True)
            dst = free(parent / name)
            shutil.copytree(cur, dst, ignore=skip)
            if pair:
                pd = free(parent / (dst.name + ('_3D' if (pair / 'data' / 'mapping.js').exists() else '_360')))
                shutil.copytree(pair, pd, ignore=skip)
        except OSError as ex:
            return {'ok': False, 'msg': '저장 못 함 · %s' % str(ex)[:100]}
        if pd:
            try:
                from . import ai_budae
                ai_budae.link(str(dst), str(pd))
                for a, b in ((dst, pd), (pd, dst)):
                    dd = ai_budae.load(str(a))
                    if dd and (a / 'data' / 'mapping.js').exists():
                        dd['src360'] = str(b)
                        ai_budae.save(str(a), dd)
            except Exception:                            # noqa: BLE001 — 짝이 안 돼도 저장은 됨
                pass
            self._remember(str(pd))
        self._remember(str(dst))                         # 최근 목록 맨 위 = 방금 저장한 것
        out = {'ok': True, 'path': str(dst), 'pair': str(pd) if pd else ''}
        if o.get('open', True):
            r = self.open_project(str(dst))
            if not r.get('ok'):
                out.update(ok=False, msg=r.get('msg', '열지 못함'))
        return out

    def close_app(self):
        """화면이 «나가도 됨» (저장했거나 저장 안 함) → 창 닫기."""
        self._close_ok = True
        threading.Timer(0.05, self._window.destroy).start()
        return {'ok': True}

    def _on_closing(self):
        """창 닫기 (×): 보기 화면이면 저장 안 한 것을 화면에 물음 (__rvClose → close_app) · 화면이 5 초 넘게 답이 없으면 다음 × 에 닫음."""
        if self._close_ok or not self._project:
            return True
        now = time.time()
        if not self._ask_ans and now - self._ask_t > 5:
            return True
        self._ask_t, self._ask_ans = now, False
        threading.Thread(target=self._ask_close, daemon=True).start()   # 화면 스레드에서 evaluate_js 를 기다리면 멈춤 → 따로
        return False

    def _ask_close(self):
        try:
            r = self._window.evaluate_js('window.__rvClose ? window.__rvClose() : "none"')
        except Exception:                                # noqa: BLE001
            r = 'none'
        self._ask_ans = True
        if r != 'wait':
            self.close_app()

    def list_shares(self):
        """NAS 에 올린 로드뷰 (share.py 기록)."""
        p = Path(os.environ.get('APPDATA', '')) / 'DH.Grading' / 'roadview_share.json'
        try:
            m = json.loads(p.read_text(encoding='utf-8'))
        except Exception:
            return []
        return [{'path': k, 'title': Path(k).name, 'url': v.get('url', ''), 'files': len(v.get('files', {}))}
                for k, v in m.items() if v.get('url')]

    def open_url(self, url):
        import webbrowser
        if str(url).startswith('https://'):
            webbrowser.open(url)
        return {'ok': True}

    def remove_project(self, path):
        self._save_list([x for x in self._load_list() if os.path.normcase(x['path']) != os.path.normcase(path)])
        return {'ok': True}

    def _refresh_viewer(self, folder):
        """프로젝트 폴더의 보기 화면을 이 프로그램 판으로 (옛 판으로 만든 폴더도 새 기능이 보이게 · 1123)."""
        src = BASE / 'viewer'
        if not (src / 'app.js').exists():
            return
        dst = Path(folder)
        pairs = [(src / 'app.js', dst / 'viewer' / 'app.js'), (src / 'style.css', dst / 'viewer' / 'style.css'),
                 (src / 'river.js', dst / 'viewer' / 'river.js'),     # 하천 추진 · 횡단 셈 (360 도 같은 셈 · 1126)
                 (src / 'cost.js', dst / 'viewer' / 'cost.js'),       # 개략공사비 셈 (3D 와 같은 셈 · 1127)
                 (src / 'index.html', dst / 'index.html'), (src / 'index.html', dst / '로드뷰.html')]
        if (dst / 'data' / 'mapping.js').exists():      # 3D 매핑 프로젝트 = 3D 보기 화면
            pairs = [(src / 'map3d.js', dst / 'viewer' / 'map3d.js'), (src / 'river.js', dst / 'viewer' / 'river.js'),   # river.js = 하천 추진 · 횡단 셈 (1126)
                     (src / 'cost.js', dst / 'viewer' / 'cost.js'),   # 개략공사비 셈 (1127)
                     (src / 'map3d.html', dst / 'index.html')]
            pairs += [(src / 'textures' / fn, dst / 'viewer' / 'textures' / fn) for fn in ('gd_color.jpg', 'gd_normal.jpg', 'LICENSE.txt')]   # 부지정지 질감 (1127)
            if not (dst / 'data' / 'dem.js').exists():  # 1.0.15 까지 만든 프로젝트: 관로 땅 높이(매끈한 땅) 를 지금 만듦 (몇 초)
                try:
                    from .mapping import write_dem_js
                    write_dem_js(str(dst), log=lambda m: None)
                except Exception:                       # noqa: BLE001
                    pass
        for a, b in pairs:
            try:
                if not b.exists() or a.read_bytes() != b.read_bytes():
                    b.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(a, b)
            except OSError:
                pass

    def open_project(self, path):
        url = _page_url(path)
        if not url:
            return {'ok': False, 'msg': '로드뷰 폴더가 아님 (index.html 없음)'}
        self._refresh_viewer(path)
        url = _page_url(path)
        self._project = str(Path(path).resolve())
        self._opened = round(time.time(), 3)
        self._remember(self._project)                    # 튜토리얼 복사본은 안 넣음 (_remember)
        self._window.load_url(url)
        return {'ok': True}

    def pick_open(self):
        import webview
        r = self._window.create_file_dialog(webview.FileDialog.FOLDER)
        if not r:
            return None
        return self.open_project(r[0] if isinstance(r, (list, tuple)) else r)

    def go_home(self):
        self._project = None
        self._window.load_url(HOME_HTML.resolve().as_uri())

    # ------------------------------------------------------------ 새로 만들기
    def pick_inputs(self):
        import webview
        r = self._window.create_file_dialog(webview.FileDialog.OPEN, allow_multiple=True, file_types=FILTER_INPUTS)
        if not r:
            return None
        files = list(r)
        size = sum(os.path.getsize(f) for f in files if os.path.exists(f)) / 1e9
        return {'files': files, 'size_gb': round(size, 1), 'dir': os.path.dirname(files[0]),
                'osv': sum(f.lower().endswith('.osv') for f in files),
                'jpg': sum(f.lower().endswith(('.jpg', '.jpeg')) for f in files),
                'out': self.default_out(files)}

    def default_out(self, files):
        """기본 결과 폴더 = 로드뷰_<영상 번호…> (다른 영상으로 만든 «로드뷰» 폴더를 덮지 않게 · 1123)."""
        files = [f for f in (files or []) if f]
        if not files:
            return ''
        nums = []
        for f in files:
            parts = Path(f).stem.split('_')
            nums.append(parts[2] if len(parts) >= 3 and parts[2].isdigit() else Path(f).stem[:12])
        nums = sorted(set(nums))
        tag = nums[0] if len(nums) == 1 else ('%s-%s' % (min(nums), max(nums)) if all(n.isdigit() for n in nums) else nums[0] + '외')
        return os.path.join(os.path.dirname(files[0]), '로드뷰_' + tag)

    # 편집 화면 (JACK 1125: DJI Studio 처럼 불러와 타임라인에서 잘라 지우기)
    def media_load(self, path):
        """영상 한 개 미리보기 자료(경로 · 썸네일) 만들기 시작 → id. 결과는 media_state(id)."""
        from .media import MediaLoad
        if not path or not os.path.isfile(path):
            return {'ok': False, 'msg': '파일 없음'}
        m = MediaLoad(CFG_DIR / 'media', path)
        mid = uuid.uuid4().hex[:10]
        self._media[mid] = m
        return {'ok': True, 'id': mid}

    def media_state(self, mid):
        m = self._media.get(mid)
        return m.info() if m else {'state': 'fail', 'line': '없음'}

    def frame_at(self, path, t):
        """미리보기 영상을 못 트는 PC 용 한 장면 (위 | 아래 렌즈) → file:// 주소."""
        from .media import frame_at
        try:
            p = frame_at(CFG_DIR / 'media', path, float(t))
            return Path(p).resolve().as_uri() if p else ''
        except Exception:
            return ''

    # 좌표 보정 (GCP · JACK 1125)
    def gcp_pick(self):
        """GCP 파일 고르기 → 점 목록 (이름 · 표고 · 좌표 후보)."""
        import webview
        r = self._window.create_file_dialog(webview.FileDialog.OPEN, file_types=FILTER_GCP)
        if not r:
            return None
        path = r[0] if isinstance(r, (list, tuple)) else r
        from .media import parse_gcp
        try:
            pts = parse_gcp(path)
        except Exception as ex:
            return {'ok': False, 'msg': 'GCP 파일 못 읽음 · ' + str(ex)[:120]}
        if not pts:
            return {'ok': False, 'msg': 'GCP 없음 · 한 줄 = 이름, X, Y, 표고'}
        return {'ok': True, 'name': os.path.basename(path), 'points': pts}

    def gcp_en(self, lat, lon):
        """지도에서 찍은 점 위경도 → 도면 좌표 (EPSG:5186)."""
        from .geo import Geo
        from .media import CRS
        e, n = Geo(CRS).ll_to_en(float(lat), float(lon))
        return [e, n]

    def ground_z(self, pts):
        """[[E, N], …] → 땅 표고 (사내 수치지도 등고선 · 표고점 5 m DEM, 만들기와 같은 셈) · 못 쓰면 None."""
        try:
            pts = [(float(p[0]), float(p[1])) for p in pts or []]
        except (TypeError, ValueError, IndexError):
            return None
        if not pts:
            return []
        es = [p[0] for p in pts]; ns = [p[1] for p in pts]
        box = getattr(self, '_dem_box', None)
        if not (box and min(es) >= box[0] and min(ns) >= box[1] and max(es) <= box[2] and max(ns) <= box[3]):
            try:
                import numpy as np
                from . import terrain
                if not terrain.db_available():
                    return None
                bbox = (min(es) - 1500, min(ns) - 1500, max(es) + 1500, max(ns) + 1500)
                cl = terrain.contours_from_db(bbox, 5186)
                sp = terrain.spot_heights_from_db(bbox, 5186)
                P = terrain.lines_to_points(cl, 2.0)
                if len(sp):
                    P = np.vstack([P, sp])
                if len(P) < 3:
                    return None
                self._dem = terrain.GridDem(P, cell=5.0, sigma=15.0)
                self._dem_box = (bbox[0] + 300, bbox[1] + 300, bbox[2] - 300, bbox[3] - 300)
            except Exception:
                return None
        z = self._dem(es, ns)
        return [float(v) if v == v else None for v in z]

    def load_edit(self, out):
        """그 결과 폴더에 지난번 편집(타임라인)이 있으면 돌려줌."""
        try:
            return json.loads((Path(out) / 'data' / 'edit.json').read_text(encoding='utf-8'))
        except Exception:
            return None

    def pick_folder(self):
        import webview
        r = self._window.create_file_dialog(webview.FileDialog.FOLDER)
        if not r:
            return None
        return r[0] if isinstance(r, (list, tuple)) else r

    def start_build(self, p):
        files = p.get('files') or []
        out = (p.get('out') or '').strip()
        if not files or not out:
            return {'ok': False, 'msg': '영상을 고르세요'}
        try:
            gap = float(p.get('interval', 5))
            if not 0.5 <= gap <= 500:
                raise ValueError
        except (TypeError, ValueError):
            return {'ok': False, 'msg': '간격(m) 숫자 확인 (0.5 ~ 500)'}
        args = list(files) + ['--out', out, '--interval', '%g' % gap,
                              '--width', str(int(p.get('width', 8192))), '--title', p.get('title') or Path(out).name]
        # 편집 화면에서 남긴 토막 {영상 이름: [[시작초, 끝초], …]}
        for name, segs in (p.get('keep') or {}).items():
            for s in segs or []:
                try:
                    t0, t1 = float(s[0]), float(s[1])
                except (TypeError, ValueError, IndexError):
                    continue
                if t1 > t0:
                    args += ['--keep', '%s=%.3f:%.3f' % (name, t0, t1)]
        try:                                       # 다른 영상으로 만든 폴더면 여기서 막음 (build 도 막지만 edit.json 을 덮기 전에)
            s = (Path(out) / 'data' / 'project.js').read_text(encoding='utf-8')
            old = {v.get('name') for v in json.loads(s.split('=', 1)[1].strip().rstrip(';')).get('videos', [])}
            new = {Path(f).stem for f in files if f.lower().endswith('.osv')}
            if old and new and not (old & new):
                return {'ok': False, 'msg': '다른 영상으로 만든 폴더 → 결과 폴더 바꾸기'}
        except (OSError, ValueError, IndexError, AttributeError):
            pass
        if p.get('edit') is not None:              # 다음에 같은 폴더로 열면 타임라인 되살림
            try:
                d = Path(out) / 'data'
                d.mkdir(parents=True, exist_ok=True)
                (d / 'edit.json').write_text(json.dumps(p['edit'], ensure_ascii=False), encoding='utf-8')
            except OSError:
                pass
        cal = p.get('calib') or {}
        if cal:                                    # 편집 화면 GCP 보정 → 맞춤 값 (build 는 calib.js 를 안 덮음)
            try:
                self._merge_calib(out, cal)
            except Exception as ex:
                return {'ok': False, 'msg': '맞춤 값 저장 못 함 · ' + str(ex)[:120]}
        if not p.get('dem', True):
            args += ['--terrain', 'none']
        if not p.get('sat', True):
            args += ['--no-vworld']
        cmd, cwd = _cli('build', args)
        j = Job('build', cmd, cwd, out=out)
        self._jobs[j.id] = j
        return {'ok': True, 'job': j.id}

    @staticmethod
    def _merge_calib(out, cal):
        """data/calib.js 의 videos[영상] 에 dx · dy · dyaw · dpitch · droll 을 넣는다 (나머지 · 터파기 설정 · 다른 영상은 그대로).
        높이(dz)는 넣지 않음 — 수치지도 프로젝트는 보기 화면이 0 으로 묶고, 편집 화면의 높이는 뜻이 다르다."""
        d = Path(out) / 'data'
        d.mkdir(parents=True, exist_ok=True)
        p = d / 'calib.js'
        C = {'videos': {}}
        if p.exists():
            s = p.read_text(encoding='utf-8')
            C = json.loads(s.split('=', 1)[1].strip().rstrip(';'))
            C.setdefault('videos', {})
        for vid, v in cal.items():
            e = C['videos'].setdefault(str(vid), {})
            for k in ('dx', 'dy', 'dyaw', 'dpitch', 'droll'):
                if isinstance(v.get(k), (int, float)) and math.isfinite(v[k]):
                    e[k] = round(float(v[k]), 4)
            e['src'] = 'gcp'
        tmp = p.with_suffix('.js.tmp')
        tmp.write_text('window.RV_CALIB = ' + json.dumps(C, ensure_ascii=False, indent=2) + ';\n', encoding='utf-8')
        os.replace(tmp, p)

    # ------------------------------------------------------------ 프로젝트 안 (보기 화면에서 부름)
    def pick_pipes(self):
        import webview
        r = self._window.create_file_dialog(webview.FileDialog.OPEN, file_types=FILTER_PIPES)
        if not r:
            return None
        path = r[0] if isinstance(r, (list, tuple)) else r
        from .pipes_update import fields_of
        try:
            return fields_of(path)
        except Exception as ex:
            return {'path': path, 'kind': '', 'name': os.path.basename(path), 'fields': [], 'error': str(ex)}

    def import_pipes(self, p):
        if not self._project:
            return {'ok': False, 'msg': '열린 프로젝트 없음'}
        from .pipes_update import update
        if (Path(self._project) / 'data' / 'mapping.js').exists():     # 3D 매핑 프로젝트: 땅 높이 = DSM
            from .mapping import import_pipes as update
        try:
            depth = p.get('depth')
            n, msg = update(self._project, p['path'], p.get('dia_field') or None,
                            (p.get('label_field') if p.get('label_field') not in (None, '', '(없음)') else None),
                            float(depth) if depth not in (None, '') else None, p.get('layers') or None,
                            log=lambda s: None)
            return {'ok': True, 'lines': n, 'note': msg}
        except Exception as ex:
            return {'ok': False, 'msg': str(ex)[:200]}

    def open_results(self):
        """3D 매핑 결과물 폴더 (정사영상 · DSM · 지표면) 를 탐색기로."""
        if not self._project:
            return {'ok': False}
        d = Path(self._project) / '결과물'
        if d.exists():
            os.startfile(str(d))       # noqa: S606 — 사용자 PC 탐색기
        return {'ok': d.exists()}

    def pick_checkpoints(self):
        """검사점 파일(이름, X, Y, 표고 · CSV/TXT) → 매핑 좌표계 · 범위에 가까운 좌표 순서 (X=북/X=동)."""
        if not self._project:
            return {'ok': False, 'msg': '열린 프로젝트 없음'}
        import webview
        r = self._window.create_file_dialog(webview.FileDialog.OPEN, file_types=FILTER_GCP)
        if not r:
            return None
        path = r[0] if isinstance(r, (list, tuple)) else r
        from .media import parse_gcp
        try:
            M = json.loads((Path(self._project) / 'data' / 'mapping.js').read_text(encoding='utf-8').split('=', 1)[1].strip().rstrip(';'))
            pts = parse_gcp(path, M.get('crs', 'EPSG:5186'))
        except Exception as ex:
            return {'ok': False, 'msg': '검사점 파일 못 읽음 · ' + str(ex)[:120]}
        if not pts:
            return {'ok': False, 'msg': '검사점 없음 · 한 줄 = 이름, X, Y, 표고'}
        x0, y0, x1, y1 = M['bbox']; cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        dist = lambda c: math.hypot(c['E'] - cx, c['N'] - cy)        # noqa: E731
        k = 0
        if len(pts[0]['cand']) > 1:                     # 두 순서 중 범위 가운데에 가까운 쪽 (가운데값)
            d0 = sorted(dist(p['cand'][0]) for p in pts); d1 = sorted(dist(p['cand'][-1]) for p in pts)
            k = 0 if d0[len(d0) // 2] <= d1[len(d1) // 2] else 1
        out = []
        for p in pts:
            c = p['cand'][min(k, len(p['cand']) - 1)]
            out.append({'name': p['name'], 'E': c['E'], 'N': c['N'], 'Z': p['z']})
        far = sum(1 for p in out if not (x0 - 50 <= p['E'] <= x1 + 50 and y0 - 50 <= p['N'] <= y1 + 50))
        note = '검사점 %d개' % len(out) + (' · 매핑 범위 밖 %d개' % far if far else '') + (' · X = 북' if k == 1 else '')
        return {'ok': True, 'points': out, 'note': note}

    def save_checks(self, items):
        """검사점 잔차 → 결과물/정확도_검사점.csv · .txt (RMSE · DSM · DTM 표고 견줌)."""
        if not self._project:
            return {'ok': False, 'msg': '열린 프로젝트 없음'}
        from .mapping import check_accuracy, set_crs
        try:
            M = json.loads((Path(self._project) / 'data' / 'mapping.js').read_text(encoding='utf-8').split('=', 1)[1].strip().rstrip(';'))
            set_crs(M.get('crs', 'EPSG:5186'))
            r = check_accuracy(self._project, list(items or []))
        except Exception as ex:
            return {'ok': False, 'msg': str(ex)[:200]}
        f = lambda v: '-' if v is None else '%.3f m' % v         # noqa: E731
        return {'ok': True, 'msg': '평면 RMSE %s · 높이 %s · %s' % (f(r['h']), f(r['v']), os.path.basename(r['csv'])), 'report': r}

    # ---------------------------------------------------------------- 3D 매핑 만들기 (JACK 1125)
    def scan_mapping(self, folder):
        """폴더의 매핑 사진(위치 XMP · 360 아님) → 지도 · 범위 고르기용 목록."""
        from .mapping import scan_folder
        try:
            ps = scan_folder(folder)
        except Exception as ex:
            return {'ok': False, 'msg': str(ex)[:160]}
        return {'ok': True, 'folder': folder, 'photos': ps,
                'out': os.path.join(os.path.dirname(folder.rstrip('\\/')), '매핑_' + os.path.basename(folder.rstrip('\\/')))}

    def pick_mapping_folder(self):
        d = self.pick_folder()
        return self.scan_mapping(d) if d else None

    def scan_mapping_files(self, files):
        """고른 사진 파일들만 (매핑 화면 «사진 선택» · Terra 꼴) — 폴더 = 첫 파일의 폴더 · 다른 폴더 파일은 뺌."""
        files = [str(f) for f in (files or []) if f]
        if not files:
            return {'ok': False, 'msg': '고른 사진 없음'}
        folder = os.path.dirname(files[0])
        same = [f for f in files if os.path.normcase(os.path.dirname(f)) == os.path.normcase(folder)]
        from .mapping import scan_folder
        try:
            ps = scan_folder(folder, names=[os.path.basename(f) for f in same])
        except Exception as ex:                          # noqa: BLE001
            return {'ok': False, 'msg': str(ex)[:160]}
        out = os.path.join(os.path.dirname(folder.rstrip('\\/')), '매핑_' + os.path.basename(folder.rstrip('\\/')))
        return {'ok': True, 'folder': folder, 'photos': ps, 'out': out, 'picked': len(same), 'other_dir': len(files) - len(same)}

    def pick_mapping_files(self):
        import webview
        r = self._window.create_file_dialog(webview.FileDialog.OPEN, allow_multiple=True, file_types=FILTER_MAPPHOTO)
        return self.scan_mapping_files(list(r)) if r else None

    # ---- 템플릿 (매핑 세부 설정 묶음 · 앱 설정 폴더/map_templates/이름.json)
    @staticmethod
    def _tpl_name(name):
        n = re.sub(r'[\\/:*?"<>|\x00-\x1f]', '', str(name or '')).strip().strip('.')[:40].strip()
        return n

    def templates_list(self):
        try:
            return sorted(p.stem for p in TPL_DIR.glob('*.json'))
        except OSError:
            return []

    def template_save(self, name, values):
        n = self._tpl_name(name)
        if not n:
            return {'ok': False, 'msg': '이름 넣기'}
        if n == '일반':
            return {'ok': False, 'msg': '«일반» = 기본값 · 다른 이름'}
        if not isinstance(values, dict):
            return {'ok': False, 'msg': '설정 값 없음'}
        TPL_DIR.mkdir(parents=True, exist_ok=True)
        (TPL_DIR / (n + '.json')).write_text(json.dumps({'name': n, 'made': time.strftime('%Y-%m-%d %H:%M'), 'values': values},
                                                        ensure_ascii=False, indent=1), encoding='utf-8')
        return {'ok': True, 'name': n}

    def template_load(self, name):
        n = self._tpl_name(name)
        try:
            d = json.loads((TPL_DIR / (n + '.json')).read_text(encoding='utf-8'))
        except (OSError, ValueError):
            return {'ok': False, 'msg': '템플릿 못 읽음 · ' + n}
        v = d.get('values')
        return {'ok': True, 'name': n, 'values': v if isinstance(v, dict) else {}}

    # ---- 장치 성능 (매핑 화면 아래 줄 · Terra 꼴): CPU · RAM · 그래픽카드 · 엔진 · 가우시안 스플래팅
    def sysinfo(self):
        if getattr(self, '_sys', None) is None:                # 바뀌지 않는 것은 한 번만 (nvidia-smi 가 1초 남짓)
            d = {'cores': os.cpu_count() or 0, 'cpu': '', 'ram_gb': 0.0, 'gpu': '', 'vram_gb': 0.0, 'gpu_nvidia': False}
            try:
                import winreg
                with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r'HARDWARE\DESCRIPTION\System\CentralProcessor\0') as k:
                    d['cpu'] = ' '.join(str(winreg.QueryValueEx(k, 'ProcessorNameString')[0]).split())
            except (OSError, ImportError):
                pass
            try:
                r = subprocess.run(['nvidia-smi', '--query-gpu=name,memory.total', '--format=csv,noheader,nounits'], capture_output=True,
                                   text=True, encoding='utf-8', errors='replace', timeout=10, creationflags=NO_WINDOW)
                ln = (r.stdout or '').strip().splitlines()
                if r.returncode == 0 and ln:
                    nm, _, mem = ln[0].rpartition(',')
                    d.update(gpu=nm.strip(), vram_gb=round(float(mem) / 1024.0, 1), gpu_nvidia=True)
            except (OSError, ValueError, subprocess.SubprocessError):
                pass
            self._sys = d
        d = dict(self._sys)
        try:
            import ctypes

            class MS(ctypes.Structure):
                _fields_ = [('dwLength', ctypes.c_ulong), ('dwMemoryLoad', ctypes.c_ulong), ('ullTotalPhys', ctypes.c_ulonglong),
                            ('ullAvailPhys', ctypes.c_ulonglong), ('ullTotalPageFile', ctypes.c_ulonglong), ('ullAvailPageFile', ctypes.c_ulonglong),
                            ('ullTotalVirtual', ctypes.c_ulonglong), ('ullAvailVirtual', ctypes.c_ulonglong), ('ullAvailExtendedVirtual', ctypes.c_ulonglong)]
            m = MS(); m.dwLength = ctypes.sizeof(MS)
            if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m)):
                d.update(ram_gb=round(m.ullTotalPhys / 2 ** 30, 1), ram_free_gb=round(m.ullAvailPhys / 2 ** 30, 1))
        except Exception:                                    # noqa: BLE001
            pass
        try:
            from .mapping import find_engines
            d['engines'] = bool(find_engines()[0])
        except Exception:                                    # noqa: BLE001
            d['engines'] = False
        d['gs_ok'], d['gs_why'] = False, '이 판에 가우시안 스플래팅 엔진 없음'
        try:                                                 # gsplat.available() → bool · (ok, why) · {'ok', 'why'}
            from . import gsplat
            g = gsplat.available()
            if isinstance(g, dict):
                d['gs_ok'], d['gs_why'] = bool(g.get('ok')), str(g.get('why') or '')
            elif isinstance(g, (tuple, list)):
                d['gs_ok'], d['gs_why'] = bool(g[0]), str(g[1] if len(g) > 1 else '')
            else:
                d['gs_ok'], d['gs_why'] = bool(g), ''
            if not d['gs_ok'] and not d['gs_why']:
                d['gs_why'] = '가우시안 스플래팅 엔진을 쓸 수 없음'
        except ImportError:
            pass
        except Exception as ex:                              # noqa: BLE001 — 엔진 확인이 화면을 멈추지 않게
            d['gs_why'] = '가우시안 스플래팅 확인 실패 · ' + str(ex)[:100]
        return d

    # ---- 결과물 다듬기 (JACK 1126: 다 만든 결과물에서 잘라내기 · DEM 더하기 · 웹 공유) — 같은 설정으로 결과물 단계만 다시
    def _map_args(self):
        """열린 3D 매핑 프로젝트를 다시 돌릴 설정 (work/args.json · 없으면 mapping.js · 정보.txt · 사진 목록으로)."""
        pj = Path(self._project or '')
        M = json.loads((pj / 'data' / 'mapping.js').read_text(encoding='utf-8').split('=', 1)[1].strip().rstrip(';'))
        try:
            A = json.loads((pj / 'work' / 'args.json').read_text(encoding='utf-8'))
        except (OSError, ValueError):
            A = {}
        photos = A.get('photos') or M.get('photos_dir')
        if not photos:
            for ln in (pj / '결과물' / '정보.txt').read_text(encoding='utf-8').splitlines():
                if ln.startswith('사진 폴더:'):
                    photos = ln.split(':', 1)[1].strip()
        if not photos or not Path(photos).is_dir():
            raise RuntimeError('사진 폴더 없음 · ' + str(photos))
        names = A.get('names')
        if not names or not Path(names).exists():
            names = next((str(q) for q in (pj / 'work' / 'pick.txt', pj / 'work' / 'names.txt') if q.exists()), None)
        D = {'photos': photos, 'quality': A.get('quality') or M.get('quality') or 'normal', 'names': names,
             'title': A.get('title') or M.get('title') or pj.name, 'crs': A.get('crs') or M.get('crs') or 'EPSG:5186',
             'outputs': list(M.get('outputs') or A.get('outputs') or ['mesh']), 'contour': A.get('contour') or (M.get('contour') or {}).get('minor') or 1.0,
             'cell': A.get('cell') or 0}
        for k in MAP_OPT_KEYS + ('gs_quality', 'gs_dom'):        # 매핑 화면 세부 설정 (없으면 기본 = 1.0.17 까지)
            if A.get(k) is not None:
                D[k] = A[k]
        return D

    @staticmethod
    def _opt_args(d):
        """매핑 세부 설정 (매핑 화면 · work/args.json 같은 열쇠) → rv_cli map 옵션. 없거나 틀린 값은 빼서 기본(= 1.0.17 까지 동작)."""
        d = d or {}
        a = []

        def num(k, lo, hi):
            try:
                v = float(d.get(k))
            except (TypeError, ValueError):
                return None
            return min(hi, max(lo, v)) if math.isfinite(v) else None

        def on(k):
            v = d.get(k)
            return None if v is None or v == 'auto' else ('1' if v in (True, 1, '1', 'true') else '0')
        if d.get('feat') in ('high', 'normal', 'low'):
            a += ['--feat', d['feat']]
        if d.get('ortho_res') in ('auto', 'high', 'mid', 'low'):
            a += ['--ortho-res', d['ortho_res']]
        if on('ortho_fill') is not None:
            a += ['--ortho-fill', on('ortho_fill')]
        if num('mesh_ratio', 1, 100) is not None:
            a += ['--mesh-ratio', '%g' % num('mesh_ratio', 1, 100)]
        fm = d.get('mesh_formats') or []
        fm = [f for f in (fm.split(',') if isinstance(fm, str) else fm) if f in ('ply', 'obj')]
        if fm:
            a += ['--mesh-formats', ','.join(fm)]
        if num('las_density', 0, 100) in (100, 50, 25, 10):
            a += ['--las-density', '%d' % num('las_density', 0, 100)]
        if on('las_classify') is not None:
            a += ['--las-classify', on('las_classify')]
        if num('dem_cell', 0, 10) in (0.25, 0.5, 1.0, 2.0):
            a += ['--dem-cell', '%g' % num('dem_cell', 0, 10)]
        if num('contour_base', -1000, 9000) is not None:
            a += ['--contour-base', '%g' % num('contour_base', -1000, 9000)]
        if num('contour_label', -1, 1000) is not None:
            a += ['--contour-label', '%g' % num('contour_label', -1, 1000)]
        if on('contour_clean') is not None:
            a += ['--contour-clean', on('contour_clean')]
        if num('tin_step', 0.1, 50) is not None:
            a += ['--tin-step', '%g' % num('tin_step', 0.1, 50)]
        return a

    @staticmethod
    def _gs_args(d):
        """가우시안 스플래팅 (결과물 gs 를 고를 때만 · gsplat.py 쪽 옵션)."""
        q = d.get('gs_quality') if d.get('gs_quality') in ('low', 'normal', 'high', 'ultra') else 'normal'
        return ['--gs-quality', q, '--gs-dom', '1' if d.get('gs_dom') in (True, 1, '1', 'true') else '0']

    def _busy(self, folder):
        """같은 결과 폴더에 매핑 작업이 돌고 있으면 True (stages · tiles 가 엉키지 않게 · 1126 검토)."""
        f = os.path.normcase(os.path.abspath(folder))
        return any(j.kind == 'map' and j.state == 'run' and os.path.normcase(os.path.abspath(j.out or '')) == f for j in self._jobs.values())

    def _rerun(self, add=(), A=None, only=None):
        if self._busy(self._project):
            return {'ok': False, 'msg': '이 결과물을 만드는 작업이 이미 돌고 있음'}
        try:
            A = A or self._map_args()
        except Exception as ex:                          # noqa: BLE001
            return {'ok': False, 'msg': str(ex)[:160]}
        want = set(only) if only else set(A['outputs']) | set(add)       # only = 이것만 다시 (나머지 결과물은 그대로 둠)
        outs = [o for o in MAP_OUTS if o in want]
        args = [A['photos'], '--out', self._project, '--quality', A['quality'], '--title', A['title'], '--crs', A['crs'],
                '--outputs', ','.join(outs or ['mesh']), '--contour', str(min(50.0, max(0.1, float(A['contour'])))), '--reuse-only']
        if A['names']:
            args += ['--names', A['names']]
        if A['cell']:
            args += ['--cell', str(A['cell'])]
        args += self._opt_args(A) + (self._gs_args(A) if 'gs' in outs else [])
        cmd, cwd = _cli('map', args + self._cluster_args())
        j = Job('map', cmd, cwd, out=self._project)
        self._jobs[j.id] = j
        return {'ok': True, 'job': j.id}

    def crop_get(self):
        if not self._project:
            return {'polygon': None}
        try:
            d = json.loads((Path(self._project) / 'data' / 'crop.json').read_text(encoding='utf-8'))
            return {'polygon': d.get('polygon')}
        except (OSError, ValueError):
            return {'polygon': None}

    def crop_apply(self, p):
        if not self._project:
            return {'ok': False, 'msg': '열린 프로젝트 없음'}
        poly = [[float(q[0]), float(q[1])] for q in ((p or {}).get('polygon') or []) if len(q) >= 2]
        if len(poly) < 3:
            return {'ok': False, 'msg': '점 3개 이상'}
        area = 0.5 * abs(sum(poly[i][0] * poly[i - 1][1] - poly[i - 1][0] * poly[i][1] for i in range(len(poly))))
        if area < 25:
            return {'ok': False, 'msg': '잘라낼 넓이가 너무 작음 (25 m² 이상)'}
        try:                                             # 설정 · 사진 폴더를 먼저 확인 (안 되면 crop.json 을 바꾸지 않음)
            A = self._map_args()
            M = json.loads((Path(self._project) / 'data' / 'mapping.js').read_text(encoding='utf-8').split('=', 1)[1].strip().rstrip(';'))
        except Exception as ex:                          # noqa: BLE001
            return {'ok': False, 'msg': str(ex)[:160]}
        bb = M.get('bbox') or []
        if len(bb) == 4 and (max(q[0] for q in poly) < bb[0] or min(q[0] for q in poly) > bb[2]
                             or max(q[1] for q in poly) < bb[1] or min(q[1] for q in poly) > bb[3]):
            return {'ok': False, 'msg': '결과물 범위 밖'}
        if self._busy(self._project):
            return {'ok': False, 'msg': '이 결과물을 만드는 작업이 이미 돌고 있음'}
        f = Path(self._project) / 'data' / 'crop.json'
        f.write_text(json.dumps({'polygon': poly, 'made': time.strftime('%Y-%m-%d %H:%M'), 'crs': A.get('crs')}, ensure_ascii=False), encoding='utf-8')
        return self._rerun(A=A)

    def crop_clear(self):
        if not self._project:
            return {'ok': False, 'msg': '열린 프로젝트 없음'}
        try:
            A = self._map_args()
        except Exception as ex:                          # noqa: BLE001
            return {'ok': False, 'msg': str(ex)[:160]}
        if self._busy(self._project):
            return {'ok': False, 'msg': '이 결과물을 만드는 작업이 이미 돌고 있음'}
        f = Path(self._project) / 'data' / 'crop.json'
        if f.exists():
            f.unlink()
        return self._rerun(A=A)

    def nonground_get(self):
        """«땅 아님» 다각형 (DTM 필터가 놓친 큰 건물 등 · JACK 1126) → {polygons: [[[E, N], …], …]}."""
        if not self._project:
            return {'polygons': []}
        try:
            d = json.loads((Path(self._project) / 'data' / 'nonground.json').read_text(encoding='utf-8'))
            return {'polygons': d.get('polygons') or []}
        except (OSError, ValueError):
            return {'polygons': []}

    def nonground_set(self, p):
        """«땅 아님» 다각형을 바꾸고 DTM · 등고선 · 땅 높이를 다시 (빈 목록 = 모두 없앰)."""
        if not self._project:
            return {'ok': False, 'msg': '열린 프로젝트 없음'}
        polys = []
        for pg in ((p or {}).get('polygons') or []):
            q = [[float(a[0]), float(a[1])] for a in pg if len(a) >= 2]
            if len(q) >= 3:
                area = 0.5 * abs(sum(q[i][0] * q[i - 1][1] - q[i - 1][0] * q[i][1] for i in range(len(q))))
                if area >= 1:
                    polys.append(q)
        try:                                             # 설정 · 사진 폴더를 먼저 확인 (안 되면 파일을 바꾸지 않음)
            A = self._map_args()
        except Exception as ex:                          # noqa: BLE001
            return {'ok': False, 'msg': str(ex)[:160]}
        if self._busy(self._project):
            return {'ok': False, 'msg': '이 결과물을 만드는 작업이 이미 돌고 있음'}
        f = Path(self._project) / 'data' / 'nonground.json'
        if polys:
            f.write_text(json.dumps({'polygons': polys, 'made': time.strftime('%Y-%m-%d %H:%M'), 'crs': A.get('crs')}, ensure_ascii=False), encoding='utf-8')
        elif f.exists():
            f.unlink()
        # 땅 아님이 바꾸는 것만 다시: DTM + (있으면) 등고선 · LAS 땅 분류 — 3D · 정사는 그대로 (1126 검토 · 몇 분 줄임)
        return self._rerun(A=A, only=['dtm'] + [o for o in ('contour', 'las') if o in A['outputs']])

    def add_outputs(self, add):
        if not self._project:
            return {'ok': False, 'msg': '열린 프로젝트 없음'}
        add = [o for o in (add or []) if o in MAP_OUTS]
        return self._rerun(add)

    # ---- 함께 처리 (JACK 1126 · cluster.py: 공유 폴더 = 작업 게시판 · 옆 PC 도우미와 점구름 깊이 지도를 나눠 만듦)
    def _cluster_cfg(self):
        try:
            c = json.loads(CLUSTER_CFG.read_text(encoding='utf-8'))
        except (OSError, ValueError):
            c = {}
        return {'board': str(c.get('board') or ''), 'use': bool(c.get('use')), 'helper': bool(c.get('helper'))}

    def _cluster_save(self, c):
        CFG_DIR.mkdir(parents=True, exist_ok=True)
        CLUSTER_CFG.write_text(json.dumps({k: c[k] for k in ('board', 'use', 'helper')}, ensure_ascii=False), encoding='utf-8')

    def _cluster_args(self):
        c = self._cluster_cfg()
        return ['--cluster', c['board']] if c['use'] and c['board'] else []

    def helper_status(self):
        """이 PC 도우미 (공유 폴더는 안 읽음 · 가벼움)."""
        h = self._helper
        return h.info() if h else {'state': 'off', 'job': '', 'pct': 0, 'done': 0, 'msg': ''}

    def cluster_get(self):
        """설정 · 공유 폴더의 도우미 목록 (살아 있음 = 45초 안 심장박동) · 이 PC 도우미."""
        c = self._cluster_cfg()
        c.update(me=self.helper_status(), helpers=[], board_ok=False, why='')
        if c['board']:
            from . import cluster
            c['board_ok'], c['why'] = cluster.board_ok(c['board'])
            if c['board_ok']:
                c['helpers'] = [{k: x.get(k) for k in ('id', 'host', 'cores', 'ram_gb', 'state', 'job', 'pct', 'done', 'alive')}
                                for x in cluster.list_helpers(c['board'])]
                c['workers'] = sum(1 for x in c['helpers'] if x['alive'] and (x['host'] != cluster.HOST or cluster.SAME_HOST))
        return c

    def cluster_set(self, p):
        c = self._cluster_cfg()
        if 'use' in (p or {}):
            c['use'] = bool(p['use'])
        if 'board' in (p or {}):
            c['board'] = str(p['board'] or '').strip()
        self._cluster_save(c)
        if self._helper and 'board' in (p or {}):    # 공유 폴더를 바꾸면 도우미도 새 폴더로
            self.helper_toggle(c['helper'])
        return self.cluster_get()

    def cluster_pick(self):
        d = self.pick_folder()
        return self.cluster_set({'board': d}) if d else None

    def helper_toggle(self, on):
        """도우미 모드 = 이 PC 가 공유 폴더의 일감을 받음 (rv_cli helper · 엔진은 낮은 우선순위 · 앱을 끄면 같이 끝)."""
        c = self._cluster_cfg()
        if self._helper:
            self._helper.stop()
            self._helper = None
        if on:
            if not c['board']:
                return {'ok': False, 'msg': '공유 폴더 먼저'}
            cmd, cwd = _cli('helper', ['--board', c['board'], '--parent-pid', str(os.getpid())])
            self._helper = HelperProc(cmd, cwd)
        c['helper'] = bool(on)
        self._cluster_save(c)
        return {'ok': True, 'me': self.helper_status()}

    def start_mapping(self, p):
        folder, out = p.get('folder'), (p.get('out') or '').strip()
        names = [n for n in (p.get('names') or []) if n]
        if not folder or not out:
            return {'ok': False, 'msg': '사진 폴더 · 결과 폴더 확인'}
        if len(names) < 3:
            return {'ok': False, 'msg': '사진 3장 이상 고르기'}
        q = p.get('quality') if p.get('quality') in ('fast', 'normal', 'fine') else 'normal'
        if (Path(out) / 'data' / 'project.js').exists():
            return {'ok': False, 'msg': '로드뷰 폴더 → 다른 결과 폴더'}
        if self._busy(out):
            return {'ok': False, 'msg': '이 결과 폴더에 작업이 이미 돌고 있음'}
        # 매핑 화면에서 그린 범위 (위경도 네 구석) → 결과물도 그 안만 = data/crop.json (결과물 다듬기 잘라내기와 같은 파일 · JACK 1126)
        #   범위를 안 그렸으면 옛 잘라내기를 지움 (이번 매핑은 고른 사진 전체)
        crop_f = Path(out) / 'data' / 'crop.json'
        rng = p.get('range')
        if rng and len(rng) >= 3:
            try:
                import pyproj
                crs = p.get('crs') if p.get('crs') in ('EPSG:5186', 'EPSG:5185', 'EPSG:5187', 'EPSG:5188', 'EPSG:5179') else 'EPSG:5186'
                tr = pyproj.Transformer.from_crs('EPSG:4326', crs, always_xy=True)
                poly = [[round(v, 3) for v in tr.transform(float(q[1]), float(q[0]))] for q in rng]
                crop_f.parent.mkdir(parents=True, exist_ok=True)
                crop_f.write_text(json.dumps({'polygon': poly, 'made': time.strftime('%Y-%m-%d %H:%M'), 'from': 'range', 'crs': crs}, ensure_ascii=False), encoding='utf-8')
            except Exception as ex:                      # noqa: BLE001
                return {'ok': False, 'msg': '범위 좌표 바꾸기 실패 · ' + str(ex)[:80]}
        elif crop_f.exists():
            crop_f.unlink()
        work = Path(out) / 'work'
        work.mkdir(parents=True, exist_ok=True)
        lst = work / 'pick.txt'
        lst.write_text('\n'.join(names) + '\n', encoding='utf-8')
        args = [folder, '--out', out, '--quality', q, '--names', str(lst), '--title', p.get('title') or Path(out).name]
        if p.get('crs') in ('EPSG:5186', 'EPSG:5185', 'EPSG:5187', 'EPSG:5188', 'EPSG:5179'):
            args += ['--crs', p['crs']]
        sel = p.get('outputs')
        outs = [o for o in (['mesh'] if sel is None else sel) if o in MAP_OUTS]
        if not outs:
            return {'ok': False, 'msg': '만들 결과물 켜기'}
        args += ['--outputs', ','.join(outs)]
        try:                                             # 등고선 주곡선 간격 (m · 계곡선 = 5배)
            ct = float(p.get('contour') or 1.0)
        except (TypeError, ValueError):
            ct = 1.0
        args += ['--contour', str(min(50.0, max(0.1, ct)))]
        args += self._opt_args(p) + (self._gs_args(p) if 'gs' in outs else [])     # 세부 설정 (Terra 꼴 칸 · JACK 1126)
        cmd, cwd = _cli('map', args + self._cluster_args())
        j = Job('map', cmd, cwd, out=out)
        self._jobs[j.id] = j
        return {'ok': True, 'job': j.id}

    def save_pipes(self, text):
        """보기 화면에서 고친 관로 (window.RV_PIPES = ...;) → data/pipes.js · 처음 한 번 원본을 pipes_orig.js 로 남김."""
        if not self._project:
            return {'ok': False}
        t = str(text)
        if not t.lstrip().startswith('window.RV_PIPES'):
            return {'ok': False}
        p = Path(self._project) / 'data' / 'pipes.js'
        orig = p.with_name('pipes_orig.js')
        if p.exists() and not orig.exists():
            shutil.copy2(p, orig)
        tmp = p.with_suffix('.js.tmp')
        tmp.write_text(t if t.endswith('\n') else t + '\n', encoding='utf-8')
        os.replace(tmp, p)
        return {'ok': True}

    def export_pipes_shp(self):
        """고친 관로 중심선(원래 꼭짓점 + 고친 점) → SHP 저장 창."""
        if not self._project:
            return {'ok': False, 'msg': '열린 프로젝트 없음'}
        import webview
        r = self._window.create_file_dialog(webview.FileDialog.SAVE, save_filename='관로_수정.shp',
                                            file_types=FILTER_SHP_SAVE)
        if not r:
            return None
        path = r[0] if isinstance(r, (list, tuple)) else r
        if not path.lower().endswith('.shp'):
            path += '.shp'
        from .pipes_update import export_pipes_shp
        try:
            n = export_pipes_shp(self._project, path)
            return {'ok': True, 'path': path, 'lines': n}
        except Exception as ex:
            return {'ok': False, 'msg': str(ex)[:200]}

    def pick_obstacles(self):
        import webview
        r = self._window.create_file_dialog(webview.FileDialog.OPEN, file_types=FILTER_OBST)
        if not r:
            return None
        path = r[0] if isinstance(r, (list, tuple)) else r
        from .pipes_update import fields_of
        try:
            info = fields_of(path)
        except Exception as ex:
            return {'path': path, 'kind': '', 'name': os.path.basename(path), 'fields': [], 'error': str(ex)}

        def guess(words):
            for f in info.get('fields', []):
                if any(w.lower() in f['name'].lower() for w in words):
                    return f['name']
            return None
        info['guess_type'] = guess(('종류', '시설', '구분', '관종', 'TYPE', 'KIND', 'FTR', 'CDE'))
        info['guess_depth'] = guess(('심도', '깊이', '토피', 'DEP', 'DPT'))
        info['guess_label'] = guess(('명칭', '이름', 'NAME', 'NAM'))
        return info

    def import_obstacles(self, p):
        if not self._project:
            return {'ok': False, 'msg': '열린 프로젝트 없음'}
        from .pipes_update import update_obstacles
        nz = lambda v: v if v not in (None, '', '(없음)') else None   # noqa: E731
        try:
            n, msg = update_obstacles(self._project, p['path'], nz(p.get('type_field')), nz(p.get('dia_field')),
                                      nz(p.get('depth_field')), nz(p.get('label_field')),
                                      float(p.get('default_depth') or 1.0), log=lambda s: None)
            return {'ok': True, 'lines': n, 'note': msg}
        except Exception as ex:
            return {'ok': False, 'msg': str(ex)[:200]}

    def trim_shots(self, ids):
        """위성지도에서 고른 구간의 사진점 지우기 (보기 화면 «사진점 지우기»)."""
        if not self._project:
            return {'ok': False, 'msg': '열린 프로젝트 없음'}
        from .pipes_update import trim_shots
        try:
            n, msg = trim_shots(self._project, list(ids or []))
            return {'ok': n > 0, 'removed': n, 'msg': msg}
        except Exception as ex:
            return {'ok': False, 'msg': str(ex)[:200]}

    def save_calib(self, text):
        if not self._project:
            return {'ok': False}
        t = str(text)
        if not t.lstrip().startswith('window.RV_CALIB'):
            return {'ok': False}
        p = Path(self._project) / 'data' / 'calib.js'
        tmp = p.with_suffix('.js.tmp')
        tmp.write_text(t if t.endswith('\n') else t + '\n', encoding='utf-8')
        os.replace(tmp, p)
        return {'ok': True}

    def export_web(self):
        if not self._project:
            return {'ok': False, 'msg': '열린 프로젝트 없음'}
        cmd, cwd = _cli('share', [self._project])
        j = Job('share', cmd, cwd, out=self._project)
        self._jobs[j.id] = j
        return {'ok': True, 'job': j.id}

    # ------------------------------------------------------------ AI 부대공 인식 (ai_budae.py · AI 엔진은 따로 · 처음 쓸 때 내려받기)
    def ai_status(self):
        """{ready, gpu, torch, cuda, msg, dir} — 가볍게 (내려받지 않음)."""
        try:
            from . import ai_budae
            return ai_budae.status()
        except Exception as ex:                          # noqa: BLE001
            return {'ready': False, 'msg': str(ex)[:160]}

    def ai_setup(self):
        """AI 엔진 내려받기 · 설치 (Job · 약 6 GB)."""
        if any(j.kind == 'ai' and j.state == 'run' for j in self._jobs.values()):
            return {'ok': False, 'msg': 'AI 작업이 이미 돌고 있음'}
        cmd, cwd = _cli('ai', ['setup'])
        j = Job('ai', cmd, cwd, out=self._project)
        self._jobs[j.id] = j
        return {'ok': True, 'job': j.id}

    def ai_run(self, opts=None):
        """{project, project360} → 인식 (Job · 엔진 없으면 먼저 설치) → data/ai_budae.js · json."""
        o = opts if isinstance(opts, dict) else {}
        proj = o.get('project') or self._project
        if not proj or not os.path.isdir(proj):
            return {'ok': False, 'msg': '매핑 프로젝트 없음'}
        if any(j.kind == 'ai' and j.state == 'run' for j in self._jobs.values()):
            return {'ok': False, 'msg': 'AI 작업이 이미 돌고 있음'}
        args = ['run', proj]
        if o.get('project360'):
            args += ['--p360', str(o['project360'])]
        zone = o.get('zone')                            # 임시점용 영역 (도면 좌표 다각형들) → 그 둘레만 인식 (JACK 1127)
        if isinstance(zone, list) and zone:
            try:
                zp = os.path.join(proj, 'work', 'ai', 'zone.json')
                os.makedirs(os.path.dirname(zp), exist_ok=True)
                polys = [[[float(q[0]), float(q[1])] for q in p] for p in zone if isinstance(p, list) and len(p) >= 3]
                with open(zp, 'w', encoding='utf-8') as fp:
                    json.dump({'polys': polys}, fp)
                args += ['--zone', zp]
            except (OSError, TypeError, ValueError, IndexError) as ex:
                return {'ok': False, 'msg': '임시점용 영역 못 씀 · %s' % str(ex)[:80]}
        cmd, cwd = _cli('ai', args)
        j = Job('ai', cmd, cwd, out=proj)
        self._jobs[j.id] = j
        return {'ok': True, 'job': j.id}

    def ai_save(self, project, data):
        """화면이 확인한 결과 (status ok · no) 저장 — ai_budae.json + ai_budae.js 둘 다."""
        proj = project or self._project
        if not proj or not isinstance(data, dict) or not isinstance(data.get('features'), list):
            return {'ok': False, 'msg': '저장할 자료가 이상함'}
        try:
            from . import ai_budae
            ai_budae.save(proj, data)
            return {'ok': True}
        except Exception as ex:                          # noqa: BLE001
            return {'ok': False, 'msg': str(ex)[:160]}

    def export_folder(self):
        if not self._project:
            return None
        dst = self.pick_folder()
        if not dst:
            return None
        target = Path(dst) / Path(self._project).name
        skip = shutil.ignore_patterns('*.tmp', '만들기_기록.txt', 'pano_index.json', 'terrain.json')
        if target.exists():
            target = Path(dst) / (Path(self._project).name + time.strftime('_%m%d_%H%M'))
        shutil.copytree(self._project, target, ignore=skip)
        return {'ok': True, 'path': str(target)}

    # ---------------------------------------------------------------- 3D 보기 오른쪽 메뉴 (JACK 1126)
    def photos_at(self, E, N, H):
        """«이 자리 원본 사진 보기»: 땅 자리 (E, N, 표고) 를 본 드론 원본 사진 (좋은 순서) · 사진 안 자리 (u, v)."""
        if not self._project:
            return {'ok': False, 'msg': '열린 프로젝트 없음'}
        from . import photo_at
        try:
            return photo_at.photos_at(self._project, float(E), float(N), float(H))
        except Exception as ex:                         # noqa: BLE001
            return {'ok': False, 'msg': str(ex)[:160]}

    def photo_view(self, name, u, v, zoom=False):
        """사진 한 장 → 화면 그림 (data URL · 줄인 전체 또는 그 자리 둘레 원본 화소) + 표시 자리."""
        if not self._project:
            return {'ok': False, 'msg': '열린 프로젝트 없음'}
        from . import photo_at
        try:
            return photo_at.photo_view(self._project, str(name), float(u), float(v), bool(zoom))
        except Exception as ex:                         # noqa: BLE001
            return {'ok': False, 'msg': str(ex)[:160]}

    def save_image(self, p):
        """«화면 그림 저장»: PNG (data URL) → 저장 창 (처음 폴더 = 결과물)."""
        import base64
        import webview
        data = str((p or {}).get('data') or '')
        if not data.startswith('data:image/png;base64,'):
            return {'ok': False, 'msg': '그림 없음'}
        name = re.sub(r'[\\/:*?"<>|]+', '_', str((p or {}).get('name') or '화면.png')).strip() or '화면.png'
        d = Path(self._project) / '결과물' if self._project else None
        r = self._window.create_file_dialog(webview.FileDialog.SAVE, directory=str(d) if d and d.exists() else '',
                                            save_filename=name, file_types=FILTER_PNG_SAVE)
        if not r:
            return None
        path = r[0] if isinstance(r, (list, tuple)) else r
        if not path.lower().endswith('.png'):
            path += '.png'
        try:
            Path(path).write_bytes(base64.b64decode(data.split(',', 1)[1]))
        except (OSError, ValueError) as ex:
            return {'ok': False, 'msg': str(ex)[:160]}
        return {'ok': True, 'path': path}

    # ---------------------------------------------------------------- 3D 보기 간단 부지정지 (JACK 1127 · 다각형 SHP · 저장 = data/grade.js)
    def grade_shp(self):
        """부지 다각형 SHP 고르기 → {ok, polys: [{name, el?, pts: [[E, N], …]}], note} (좌표 = 프로젝트 좌표계)."""
        import webview
        r = self._window.create_file_dialog(webview.FileDialog.OPEN, file_types=('부지 다각형 SHP (*.shp;*.SHP)', '모든 파일 (*.*)'))
        if not r:
            return None
        return self._grade_read(r[0] if isinstance(r, (list, tuple)) else r)

    def _grade_read(self, path):
        """다각형 SHP (5 · 15 · 25) → 바깥 고리마다 한 부지 (구멍 뺌) · .prj 가 다르면 프로젝트 좌표계로 · 매핑 범위 (+100 m) 밖은 뺌."""
        import struct
        from .shp import prj_crs, read_dbf, read_shp_lines
        try:
            with open(path, 'rb') as f:
                head = f.read(100)
            if len(head) < 100 or struct.unpack_from('>i', head, 0)[0] != 9994:
                return {'ok': False, 'msg': 'SHP 파일이 아님'}
            if struct.unpack_from('<i', head, 32)[0] not in (5, 15, 25):
                return {'ok': False, 'msg': '다각형 SHP 아님'}
            rings = read_shp_lines(path)
            fields, recs = read_dbf(path)
        except (OSError, ValueError, struct.error) as ex:
            return {'ok': False, 'msg': 'SHP 못 읽음 · %s' % str(ex)[:120]}
        M = {}
        try:
            if self._project:
                M = json.loads((Path(self._project) / 'data' / 'mapping.js').read_text(encoding='utf-8').split('=', 1)[1].strip().rstrip(';'))
        except (OSError, ValueError, IndexError):
            M = {}
        dst = M.get('crs') or 'EPSG:5186'
        note = []
        tr = None
        src = prj_crs(path)
        if src is not None:
            try:
                from pyproj import CRS, Transformer
                if not src.equals(CRS.from_user_input(dst)):
                    tr = Transformer.from_crs(src, CRS.from_user_input(dst), always_xy=True)
                    note.append('좌표계 → %s' % dst)
            except Exception:                           # noqa: BLE001
                tr = None
        else:
            note.append('.prj 없음 → %s 로 봄' % dst)
        names = [f[0] for f in fields]
        pick = lambda words: next((n for n in names if any(w.lower() in n.lower() for w in words)), None)   # noqa: E731
        f_name = pick(('이름', '명칭', '부지', 'NAME', 'NAM'))
        f_el = next((n for n in names if '계획고' in n or n.upper() in ('EL', 'FH', 'FL', 'ELEV', 'ELEVATION')), None)
        area = lambda q: 0.5 * sum(q[i - 1][0] * q[i][1] - q[i][0] * q[i - 1][1] for i in range(len(q)))   # noqa: E731
        by_rec = {}
        for idx, pts in rings:
            q = [[float(p[0]), float(p[1])] for p in pts]
            if len(q) > 3 and abs(q[0][0] - q[-1][0]) + abs(q[0][1] - q[-1][1]) < 1e-6:
                q.pop()
            if len(q) >= 3:
                by_rec.setdefault(idx, []).append(q)
        bb = M.get('bbox') or []
        polys, out_n = [], 0
        for idx, qs in sorted(by_rec.items()):
            outer = [q for q in qs if area(q) < 0] or [max(qs, key=lambda q: abs(area(q)))]   # SHP 바깥 고리 = 시계 방향 (구멍 = 반시계 · 뺌)
            rec = (recs[idx] if idx < len(recs) else None) or {}             # 지운 줄 = None
            for k, q in enumerate(outer):
                if tr is not None:
                    q = [list(tr.transform(p[0], p[1])) for p in q]
                if abs(area(q)) < 1:
                    continue
                if len(bb) == 4 and (max(p[0] for p in q) < bb[0] - 100 or min(p[0] for p in q) > bb[2] + 100
                                     or max(p[1] for p in q) < bb[1] - 100 or min(p[1] for p in q) > bb[3] + 100):
                    out_n += 1
                    continue
                d = {'pts': [[round(p[0], 3), round(p[1], 3)] for p in q]}
                nm = str(rec.get(f_name) or '').strip() if f_name else ''
                if nm:
                    d['name'] = nm if len(outer) == 1 else '%s %d' % (nm, k + 1)
                try:
                    el = float(rec.get(f_el)) if f_el and rec.get(f_el) not in (None, '') else None
                except (TypeError, ValueError):
                    el = None
                if el is not None and math.isfinite(el) and -1000 < el < 9000:
                    d['el'] = el
                polys.append(d)
                if len(polys) >= 100:
                    break
            if len(polys) >= 100:
                note.append('100곳까지만')
                break
        if out_n:
            note.append('범위 밖 %d곳 뺌' % out_n)
        return {'ok': True, 'polys': polys, 'note': ' · '.join(note)}

    def save_grade(self, text):
        """보기 화면의 부지정지 목록 (window.RV_GRADE = …;) → data/grade.js."""
        if not self._project:
            return {'ok': False, 'msg': '열린 프로젝트 없음'}
        t = str(text)
        if not t.lstrip().startswith('window.RV_GRADE'):
            return {'ok': False, 'msg': '저장할 자료가 이상함'}
        p = Path(self._project) / 'data' / 'grade.js'
        tmp = p.with_suffix('.js.tmp')
        tmp.write_text(t if t.endswith('\n') else t + '\n', encoding='utf-8')
        os.replace(tmp, p)
        return {'ok': True}


def _selftest(api, w, out_path, project):
    """RV_SELFTEST=결과.json [RV_SELFTEST_PROJECT=폴더] — 화면을 찍지 않고 창 안 내용을 읽어 적는다."""
    res = {}
    try:
        from webview.util import parse_file_type
        res['filters_ok'] = all(parse_file_type(f) for f in FILTER_INPUTS + FILTER_PIPES + FILTER_OBST + FILTER_SHP_SAVE + FILTER_GCP)
        time.sleep(4)
        res['home_title'] = w.evaluate_js('document.title')
        res['home_has_api'] = w.evaluate_js('!!(window.pywebview && window.pywebview.api)')
        res['home_items'] = w.evaluate_js('document.querySelectorAll(".card").length')
        res['home_ver'] = w.evaluate_js('document.getElementById("ver").textContent')
        if project:
            res['open'] = api.open_project(project)
            time.sleep(10)
            res['proj_title'] = w.evaluate_js('document.title')
            res['proj_has_api'] = w.evaluate_js('!!(window.pywebview && window.pywebview.api)')
            res['proj_shots'] = w.evaluate_js('(window.RV_PROJECT && RV_PROJECT.shots.length) || 0')
            res['proj_pipes'] = w.evaluate_js('(window.RV_PIPES && RV_PIPES.lines.length) || 0')
            res['proj_webgl'] = w.evaluate_js('!!document.querySelector("canvas") && !!document.querySelector("canvas").getContext')
            res['proj_loading_text'] = w.evaluate_js('(document.getElementById("loading")||{}).className || ""')
            res['proj_buttons'] = w.evaluate_js(
                '[...document.querySelectorAll("button")].filter(b => b.offsetParent).map(b => (b.getAttribute("aria-label") || b.title || b.textContent).trim()).filter(Boolean).join("|")')
            pipes = os.environ.get('RV_SELFTEST_PIPES')
            if pipes:
                res['pick'] = {k: v for k, v in __import__('roadview.pipes_update', fromlist=['x']).fields_of(pipes).items()
                               if k in ('kind', 'guess_dia', 'lines')}
                res['import'] = api.import_pipes({'path': pipes, 'dia_field': res['pick'].get('guess_dia'),
                                                  'label_field': None, 'depth': 1.2})
                res['save_calib'] = api.save_calib('window.RV_CALIB = {"videos": {}};\n')
    except Exception as ex:
        res['error'] = repr(ex)
    Path(out_path).write_text(json.dumps(res, ensure_ascii=False, indent=1), encoding='utf-8')
    api._close_ok = True                                 # noqa: SLF001 — 자가 시험은 묻지 않고 닫음
    w.destroy()


def main():
    import webview
    api = Api()
    CFG_DIR.mkdir(parents=True, exist_ok=True)
    st = os.environ.get('RV_SELFTEST')
    w = webview.create_window(APP_NAME, HOME_HTML.resolve().as_uri(), js_api=api, width=1500, height=920,
                              min_size=(1000, 640), background_color='#0f131a', minimized=bool(st))
    api._window = w
    if st:
        webview.start(_selftest, (api, w, st, os.environ.get('RV_SELFTEST_PROJECT')), gui='edgechromium',
                      private_mode=False, storage_path=str(CFG_DIR / 'webview'))
    else:
        if api._cluster_cfg()['helper']:            # 지난번에 도우미 모드를 켜 두었으면 다시 켬 (옆 PC 는 앱만 켜 두면 됨)
            api.helper_toggle(True)
        w.events.closing += api._on_closing         # 창 × = 저장 안 한 변경이 있으면 화면이 물음 (JACK 1127)
        try:
            webview.start(gui='edgechromium', private_mode=False, storage_path=str(CFG_DIR / 'webview'))
        finally:
            if api._helper:
                api._helper.stop()


if __name__ == '__main__':
    main()
