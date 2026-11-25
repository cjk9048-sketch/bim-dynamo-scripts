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

APP_NAME = '드론 로드뷰'
APP_VERSION = '1.0.14'
FROZEN = getattr(sys, 'frozen', False)
BASE = Path(sys._MEIPASS) if FROZEN else Path(__file__).resolve().parent.parent      # noqa: SLF001
HOME_HTML = (BASE / 'roadview' / 'app_home' / 'index.html') if not FROZEN else (BASE / 'app_home' / 'index.html')
CFG_DIR = Path(os.environ.get('APPDATA', str(Path.home()))) / 'DH.Roadview'
PROJ_LIST = CFG_DIR / 'projects.json'
NO_WINDOW = 0x08000000 if os.name == 'nt' else 0
# 파일 창 종류 이름은 글자 · 숫자 · 빈칸만 (pywebview 검사 [\w ]+ — «·» 하나로 창이 안 열렸음 · 1123)
FILTER_INPUTS = ('DJI 360 영상 사진 (*.osv;*.OSV;*.jpg;*.JPG)', '모든 파일 (*.*)')
FILTER_PIPES = ('관로 SHP DXF (*.shp;*.SHP;*.dxf;*.DXF)', '모든 파일 (*.*)')
FILTER_OBST = ('지장물 SHP (*.shp;*.SHP)', '모든 파일 (*.*)')
FILTER_SHP_SAVE = ('SHP (*.shp)',)
FILTER_GCP = ('GCP CSV TXT (*.csv;*.CSV;*.txt;*.TXT)', '모든 파일 (*.*)')


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
    mod = {'build': 'roadview.build', 'share': 'roadview.share', 'map': 'roadview.mapping'}[cmd]
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
            self.pct = max(self.pct, float(pct))
            left = int(left)
            self.eta_at = time.time() + left if left >= 0 else None
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
        return '남은 약 %s · %s 끝' % (hm, time.strftime('%H:%M', time.localtime(self.eta_at)))

    def _parse(self, s):
        el = time.time() - self.t0
        if self.kind == 'map':                      # 진행 · 남은 시간은 «@진행» 줄 (mapping.Eta)
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
            self.line = '멈춤 · 다시 [만들기] 누르면 만든 사진은 그대로 쓰고 이어 함'

    def info(self):
        line, pill = self.line, ''
        if self.state == 'run' and self.label:
            pill = self._left_text()
            line = self.label + ' · ' + pill
        return {'id': self.id, 'state': self.state, 'pct': round(self.pct, 1), 'line': line, 'pill': pill,
                'url': self.url, 'out': self.out, 'log': self.lines[-12:]}


class Api:
    def __init__(self):
        self._window = None
        self._project = None
        self._jobs = {}
        self._media = {}

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
        info = {'path': folder, 'title': Path(folder).name, 'shots': 0, 'updated': time.strftime('%Y-%m-%d %H:%M')}
        mp = Path(folder) / 'data' / 'mapping.js'
        if mp.exists():                                 # 3D 매핑 프로젝트: 썸네일 = 정사영상 미리보기
            try:
                M = json.loads(mp.read_text(encoding='utf-8').split('=', 1)[1].strip().rstrip(';'))
                info.update(title=M.get('title') or info['title'], shots=M.get('photos', 0), kind='mapping')
                t = Path(folder) / '결과물' / '정사영상_미리보기.png'
                if t.exists():
                    info['thumb'] = str(t)
            except Exception:
                pass
        try:
            s = (Path(folder) / 'data' / 'project.js').read_text(encoding='utf-8')
            P = json.loads(s.split('=', 1)[1].strip().rstrip(';'))
            info['title'] = P.get('title') or info['title']
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
            x['exists'] = bool(_page_url(x['path']))
            t = x.get('thumb')
            x['thumb_url'] = Path(t).as_uri() if t and os.path.exists(t) else ''
            x['cut_urls'] = [Path(c).as_uri() for c in x.get('cuts', []) if os.path.exists(c)]
            out.append(x)
        return out

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
                 (src / 'index.html', dst / 'index.html'), (src / 'index.html', dst / '로드뷰.html')]
        if (dst / 'data' / 'mapping.js').exists():      # 3D 매핑 프로젝트 = 3D 보기 화면
            pairs = [(src / 'map3d.js', dst / 'viewer' / 'map3d.js'), (src / 'map3d.html', dst / 'index.html')]
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
        self._remember(self._project)
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
        work = Path(out) / 'work'
        work.mkdir(parents=True, exist_ok=True)
        lst = work / 'pick.txt'
        lst.write_text('\n'.join(names) + '\n', encoding='utf-8')
        args = [folder, '--out', out, '--quality', q, '--names', str(lst), '--title', p.get('title') or Path(out).name]
        if p.get('crs') in ('EPSG:5186', 'EPSG:5185', 'EPSG:5187', 'EPSG:5188', 'EPSG:5179'):
            args += ['--crs', p['crs']]
        cmd, cwd = _cli('map', args)
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
        webview.start(gui='edgechromium', private_mode=False, storage_path=str(CFG_DIR / 'webview'))


if __name__ == '__main__':
    main()
