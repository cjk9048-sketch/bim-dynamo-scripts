"""드론 로드뷰 — 설치형 프로그램 (pywebview · WebView2 창).

첫 화면(app_home) : 프로젝트 목록 · 새로 만들기(OSV/JPG → 간격 m) · 열기
프로젝트 화면    : 만들어진 폴더의 index.html (보기 화면) — window.pywebview.api 로 아래 Api 를 부른다
                   관로 가져오기 · 맞춤 저장 · 웹(NAS)/폴더로 내보내기 · 처음 화면
무거운 일(만들기 · NAS 올리기)은 따로 프로세스로 돌리고 진행 줄을 읽는다 (창이 안 멈추게).
"""
from __future__ import annotations

import json
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
APP_VERSION = '1.0.5'
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
    mod = {'build': 'roadview.build', 'share': 'roadview.share'}[cmd]
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
        env = dict(os.environ, PYTHONIOENCODING='utf-8')
        self.proc = subprocess.Popen(args, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                     creationflags=NO_WINDOW, env=env)
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self):
        for raw in iter(self.proc.stdout.readline, b''):
            s = raw.decode('utf-8', errors='replace').rstrip()
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

    def _parse(self, s):
        el = time.time() - self.t0
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
            self.pct = 5 + 95.0 * done / max(1, tot)
            left = el / max(1, done) * (tot - done)
            self.line = '360 사진 %d / %d · 남은 시간 약 %d분' % (done, tot, left / 60 + 1)
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
        return {'id': self.id, 'state': self.state, 'pct': round(self.pct, 1), 'line': self.line,
                'url': self.url, 'out': self.out, 'log': self.lines[-12:]}


class Api:
    def __init__(self):
        self._window = None
        self._project = None
        self._jobs = {}

    # ------------------------------------------------------------ 공통
    def app_info(self):
        return {'name': APP_NAME, 'version': APP_VERSION}

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
        # 기본 결과 폴더 = 로드뷰_<영상 번호…> (다른 영상으로 만든 «로드뷰» 폴더를 덮지 않게 · 1123)
        nums = []
        for f in files:
            parts = Path(f).stem.split('_')
            nums.append(parts[2] if len(parts) >= 3 and parts[2].isdigit() else Path(f).stem[:12])
        tag = nums[0] if len(nums) == 1 else ('%s-%s' % (min(nums), max(nums)) if all(n.isdigit() for n in nums) else nums[0] + '외')
        base = os.path.join(os.path.dirname(files[0]), '로드뷰_' + tag)
        return {'files': files, 'size_gb': round(size, 1), 'dir': os.path.dirname(files[0]),
                'osv': sum(f.lower().endswith('.osv') for f in files),
                'jpg': sum(f.lower().endswith(('.jpg', '.jpeg')) for f in files),
                'out': base}

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
        if not p.get('dem', True):
            args += ['--terrain', 'none']
        if not p.get('sat', True):
            args += ['--no-vworld']
        cmd, cwd = _cli('build', args)
        j = Job('build', cmd, cwd, out=out)
        self._jobs[j.id] = j
        return {'ok': True, 'job': j.id}

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
        try:
            depth = p.get('depth')
            n, msg = update(self._project, p['path'], p.get('dia_field') or None,
                            (p.get('label_field') if p.get('label_field') not in (None, '', '(없음)') else None),
                            float(depth) if depth not in (None, '') else None, p.get('layers') or None,
                            log=lambda s: None)
            return {'ok': True, 'lines': n, 'note': msg}
        except Exception as ex:
            return {'ok': False, 'msg': str(ex)[:200]}

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
        res['filters_ok'] = all(parse_file_type(f) for f in FILTER_INPUTS + FILTER_PIPES + FILTER_OBST + FILTER_SHP_SAVE)
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
