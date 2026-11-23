"""드론 로드뷰 만들기 창.

OSV 고르기 → 간격(m) → (관로 SHP/DXF · 관경 항목) → 만들기 → 열기 · NAS 올리기.
실제 일은 `python -m roadview.build` / `roadview.share` 를 따로 띄워서 한다 (창이 안 멈추게).
"""
from __future__ import annotations

import os
import queue
import re
import subprocess
import sys
import threading
import time
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

PKG_PARENT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NO_WINDOW = 0x08000000 if os.name == 'nt' else 0

BG = '#f4f6f9'
CARD = '#ffffff'
INK = '#1c2430'
SUB = '#6b7685'
ACCENT = '#1f6feb'

CRS_LIST = ['EPSG:5186 중부원점', 'EPSG:5187 동부원점', 'EPSG:5185 서부원점', 'EPSG:5188 동해원점', 'EPSG:5179 UTM-K']
WIDTHS = ['6144 (보통)', '4096 (가볍게)', '8192 (선명하게)']


def _python_exe():
    exe = sys.executable
    if exe.lower().endswith('pythonw.exe'):
        cand = exe[:-len('pythonw.exe')] + 'python.exe'
        if os.path.exists(cand):
            return cand
    return exe


class App:
    def __init__(self, root):
        self.root = root
        root.title('드론 로드뷰 만들기')
        root.configure(bg=BG)
        root.minsize(640, 560)
        self.proc = None
        self.q = queue.Queue()
        self.osvs = []
        self.fields = []
        self.out_dir = tk.StringVar()
        self.interval = tk.StringVar(value='5')
        self.depth = tk.StringVar(value='1.2')
        self.pipe_path = tk.StringVar()
        self.dia_field = tk.StringVar()
        self.label_field = tk.StringVar(value='(없음)')
        self.layers = tk.StringVar()
        self.crs = tk.StringVar(value=CRS_LIST[0])
        self.width = tk.StringVar(value=WIDTHS[0])
        self.status = tk.StringVar(value='영상을 고르세요')
        self.url = tk.StringVar()
        self._style()
        self._build()
        root.after(150, self._pump)

    # ------------------------------------------------------------ 모양
    def _style(self):
        st = ttk.Style()
        st.theme_use('clam')
        st.configure('.', background=BG, foreground=INK, font=('맑은 고딕', 10))
        st.configure('Card.TFrame', background=CARD)
        st.configure('Card.TLabel', background=CARD, foreground=INK)
        st.configure('Sub.TLabel', background=CARD, foreground=SUB, font=('맑은 고딕', 9))
        st.configure('Head.TLabel', background=BG, foreground=INK, font=('맑은 고딕', 15, 'bold'))
        st.configure('Sec.TLabel', background=CARD, foreground=INK, font=('맑은 고딕', 11, 'bold'))
        st.configure('TButton', padding=(10, 5))
        st.configure('Accent.TButton', background=ACCENT, foreground='white', padding=(16, 7),
                     font=('맑은 고딕', 10, 'bold'))
        st.map('Accent.TButton', background=[('active', '#1559c7'), ('disabled', '#9db8e8')])
        st.configure('TEntry', padding=4)
        st.configure('Horizontal.TProgressbar', thickness=10, background=ACCENT)

    def _card(self, parent, title):
        f = ttk.Frame(parent, style='Card.TFrame', padding=(14, 10))
        f.pack(fill='x', padx=14, pady=(0, 10))
        ttk.Label(f, text=title, style='Sec.TLabel').grid(row=0, column=0, columnspan=4, sticky='w', pady=(0, 6))
        f.columnconfigure(1, weight=1)
        return f

    def _build(self):
        r = self.root
        ttk.Label(r, text='드론 로드뷰 만들기', style='Head.TLabel').pack(anchor='w', padx=16, pady=(14, 8))

        # 1. 영상
        c = self._card(r, '① 영상 (OSV)')
        ttk.Button(c, text='영상 고르기', command=self.pick_osv).grid(row=1, column=0, sticky='w')
        self.osv_info = ttk.Label(c, text='고른 영상 없음', style='Sub.TLabel')
        self.osv_info.grid(row=1, column=1, columnspan=3, sticky='w', padx=10)
        ttk.Label(c, text='간격 (m)', style='Card.TLabel').grid(row=2, column=0, sticky='w', pady=(8, 0))
        ttk.Entry(c, textvariable=self.interval, width=8).grid(row=2, column=1, sticky='w', padx=10, pady=(8, 0))
        ttk.Label(c, text='결과 폴더', style='Card.TLabel').grid(row=3, column=0, sticky='w', pady=(6, 0))
        ttk.Entry(c, textvariable=self.out_dir).grid(row=3, column=1, columnspan=2, sticky='we', padx=10, pady=(6, 0))
        ttk.Button(c, text='바꾸기', command=self.pick_out).grid(row=3, column=3, sticky='e', pady=(6, 0))

        # 2. 관로
        c = self._card(r, '② 관로 (선택)')
        ttk.Button(c, text='SHP · DXF 고르기', command=self.pick_pipe).grid(row=1, column=0, sticky='w')
        self.pipe_info = ttk.Label(c, text='없음', style='Sub.TLabel')
        self.pipe_info.grid(row=1, column=1, columnspan=2, sticky='w', padx=10)
        ttk.Button(c, text='빼기', command=self.clear_pipe).grid(row=1, column=3, sticky='e')
        self.lbl_dia = ttk.Label(c, text='관경 항목 (mm)', style='Card.TLabel')
        self.cb_dia = ttk.Combobox(c, textvariable=self.dia_field, state='readonly', width=22)
        self.cb_dia.bind('<<ComboboxSelected>>', lambda e: self.show_samples())
        self.dia_sample = ttk.Label(c, text='', style='Sub.TLabel')
        self.lbl_name = ttk.Label(c, text='이름 항목', style='Card.TLabel')
        self.cb_name = ttk.Combobox(c, textvariable=self.label_field, state='readonly', width=22)
        self.lbl_layers = ttk.Label(c, text='도면층 (예: W-*)', style='Card.TLabel')
        self.ent_layers = ttk.Entry(c, textvariable=self.layers, width=24)
        ttk.Label(c, text='평균매설깊이 (m)', style='Card.TLabel').grid(row=5, column=0, sticky='w', pady=(8, 0))
        ttk.Entry(c, textvariable=self.depth, width=8).grid(row=5, column=1, sticky='w', padx=10, pady=(8, 0))
        self.pipe_card = c

        # 3. 기타
        c = self._card(r, '③ 설정')
        ttk.Label(c, text='좌표계', style='Card.TLabel').grid(row=1, column=0, sticky='w')
        ttk.Combobox(c, textvariable=self.crs, values=CRS_LIST, state='readonly', width=22).grid(
            row=1, column=1, sticky='w', padx=10)
        ttk.Label(c, text='사진 크기', style='Card.TLabel').grid(row=2, column=0, sticky='w', pady=(6, 0))
        ttk.Combobox(c, textvariable=self.width, values=WIDTHS, state='readonly', width=22).grid(
            row=2, column=1, sticky='w', padx=10, pady=(6, 0))

        # 4. 실행
        c = ttk.Frame(r, padding=(16, 4))
        c.pack(fill='x')
        self.btn_go = ttk.Button(c, text='만들기', style='Accent.TButton', command=self.start)
        self.btn_go.pack(side='left')
        self.btn_stop = ttk.Button(c, text='멈추기', command=self.stop, state='disabled')
        self.btn_stop.pack(side='left', padx=8)
        self.btn_open = ttk.Button(c, text='로드뷰 열기', command=self.open_view, state='disabled')
        self.btn_open.pack(side='right')
        self.btn_share = ttk.Button(c, text='NAS 올리기', command=self.share, state='disabled')
        self.btn_share.pack(side='right', padx=8)
        self.pb = ttk.Progressbar(r, mode='determinate', maximum=100)
        self.pb.pack(fill='x', padx=16, pady=(8, 2))
        ttk.Label(r, textvariable=self.status, foreground=SUB, background=BG).pack(anchor='w', padx=16)
        uf = ttk.Frame(r, padding=(16, 6))
        uf.pack(fill='x')
        self.url_entry = ttk.Entry(uf, textvariable=self.url, state='readonly')
        self.url_btn = ttk.Button(uf, text='주소 복사', command=self.copy_url)
        self.log = tk.Text(r, height=7, bg='#0f1620', fg='#c9d4e3', relief='flat', font=('Consolas', 9),
                           insertbackground='white')
        self.log.pack(fill='both', expand=True, padx=16, pady=(4, 14))
        self.log.configure(state='disabled')
        self._layout_pipe()

    # ------------------------------------------------------------ 고르기
    def pick_osv(self):
        fs = filedialog.askopenfilenames(title='영상 (OSV) 고르기', filetypes=[('DJI 360 영상', '*.osv *.OSV')])
        if not fs:
            return
        self.osvs = list(fs)
        size = sum(os.path.getsize(f) for f in self.osvs) / 1e9
        self.osv_info.configure(text='%d개 · %.1f GB · %s' % (len(self.osvs), size, os.path.dirname(self.osvs[0])))
        if not self.out_dir.get():
            self.out_dir.set(os.path.join(os.path.dirname(self.osvs[0]), '로드뷰'))
        self.status.set('간격을 넣고 [만들기]')
        self._refresh_buttons()

    def pick_out(self):
        d = filedialog.askdirectory(title='결과 폴더')
        if d:
            self.out_dir.set(os.path.normpath(d))
            self._refresh_buttons()

    def pick_pipe(self):
        f = filedialog.askopenfilename(title='관로 파일', filetypes=[('관로', '*.shp *.SHP *.dxf *.DXF')])
        if not f:
            return
        self.pipe_path.set(f)
        self.fields = []
        self.recs = []
        if f.lower().endswith('.shp'):
            try:
                sys.path.insert(0, PKG_PARENT)
                from roadview import shp
                fields, recs = shp.read_dbf(f)
                self.fields, self.recs = fields, [r for r in recs if r]
                names = [x[0] for x in fields]
                self.cb_dia['values'] = names
                self.cb_name['values'] = ['(없음)'] + names
                g = shp.guess_dia_field(fields, self.recs)
                self.dia_field.set(g or (names[0] if names else ''))
                n = len(shp.read_shp_lines(f))
                crs = shp.prj_crs(f)
                self.pipe_info.configure(text='%s · 선 %d개 · %s' % (os.path.basename(f), n,
                                         ('좌표계 ' + crs.to_string()) if crs else '.prj 없음 → ③ 좌표계로 봄'))
            except Exception as ex:
                messagebox.showerror('관로', 'SHP 를 못 읽음\n%s' % ex)
                self.clear_pipe()
                return
        else:
            self.pipe_info.configure(text=os.path.basename(f) + ' · 3D 폴리선 높이 씀')
        self._layout_pipe()
        self.show_samples()

    def clear_pipe(self):
        self.pipe_path.set('')
        self.fields = []
        self.pipe_info.configure(text='없음')
        self._layout_pipe()

    def _layout_pipe(self):
        for w in (self.lbl_dia, self.cb_dia, self.dia_sample, self.lbl_name, self.cb_name, self.lbl_layers, self.ent_layers):
            w.grid_remove()
        p = self.pipe_path.get().lower()
        if p.endswith('.shp'):
            self.lbl_dia.grid(row=2, column=0, sticky='w', pady=(8, 0))
            self.cb_dia.grid(row=2, column=1, sticky='w', padx=10, pady=(8, 0))
            self.dia_sample.grid(row=2, column=2, columnspan=2, sticky='w', pady=(8, 0))
            self.lbl_name.grid(row=3, column=0, sticky='w', pady=(6, 0))
            self.cb_name.grid(row=3, column=1, sticky='w', padx=10, pady=(6, 0))
        elif p.endswith('.dxf'):
            self.lbl_layers.grid(row=2, column=0, sticky='w', pady=(8, 0))
            self.ent_layers.grid(row=2, column=1, sticky='w', padx=10, pady=(8, 0))

    def show_samples(self):
        f = self.dia_field.get()
        if not f or not getattr(self, 'recs', None):
            self.dia_sample.configure(text='')
            return
        vals = []
        for r in self.recs:
            v = r.get(f)
            if v not in (None, '') and v not in vals:
                vals.append(v)
            if len(vals) >= 6:
                break
        txt = ', '.join(('%g' % v) if isinstance(v, float) else str(v) for v in vals)
        self.dia_sample.configure(text=('예: ' + txt) if txt else '값 없음')

    # ------------------------------------------------------------ 실행
    def _args(self):
        try:
            gap = float(self.interval.get())
            if not (0.5 <= gap <= 500):
                raise ValueError
        except ValueError:
            messagebox.showwarning('간격', '간격(m) 숫자 확인 (0.5 ~ 500)')
            return None
        try:
            depth = float(self.depth.get())
        except ValueError:
            messagebox.showwarning('평균매설깊이', '평균매설깊이(m) 숫자 확인')
            return None
        out = self.out_dir.get().strip()
        if not self.osvs or not out:
            messagebox.showwarning('영상', '영상을 고르세요')
            return None
        a = [_python_exe(), '-u', '-m', 'roadview.build'] + self.osvs + [
            '--out', out, '--interval', '%g' % gap, '--depth', '%g' % depth,
            '--crs', self.crs.get().split()[0], '--width', self.width.get().split()[0]]
        p = self.pipe_path.get()
        if p:
            a += ['--pipes', p]
            if p.lower().endswith('.shp'):
                if self.dia_field.get():
                    a += ['--dia-field', self.dia_field.get()]
                if self.label_field.get() and self.label_field.get() != '(없음)':
                    a += ['--label-field', self.label_field.get()]
            elif self.layers.get().strip():
                a += ['--layers', self.layers.get().strip()]
        return a

    def start(self):
        a = self._args()
        if not a:
            return
        self._run(a, 'build')

    def _run(self, args, kind):
        self._log_clear()
        self.kind = kind
        self.total = 0
        self.t0 = time.time()
        self.pb['value'] = 0
        env = dict(os.environ, PYTHONIOENCODING='utf-8')
        self.proc = subprocess.Popen(args, cwd=PKG_PARENT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                     creationflags=NO_WINDOW, env=env)
        threading.Thread(target=self._reader, args=(self.proc,), daemon=True).start()
        self.btn_go.configure(state='disabled')
        self.btn_share.configure(state='disabled')
        self.btn_stop.configure(state='normal')
        self.status.set('시작…' if kind == 'build' else 'NAS 에 올리는 중…')

    def _reader(self, proc):
        for raw in iter(proc.stdout.readline, b''):
            self.q.put(raw.decode('utf-8', errors='replace').rstrip())
        proc.wait()
        self.q.put(('__end__', proc.returncode))

    def stop(self):
        if self.proc and self.proc.poll() is None:
            subprocess.run(['taskkill', '/PID', str(self.proc.pid), '/T', '/F'], capture_output=True,
                           creationflags=NO_WINDOW)
            self.status.set('멈춤 · 다시 [만들기] 누르면 만든 사진은 그대로 쓰고 이어 함')

    def _pump(self):
        try:
            while True:
                m = self.q.get_nowait()
                if isinstance(m, tuple):
                    self._ended(m[1])
                else:
                    self._line(m)
        except queue.Empty:
            pass
        self.root.after(150, self._pump)

    def _line(self, s):
        self._log_add(s)
        el = time.time() - self.t0
        if self.kind == 'share':
            m = re.search(r'(\d+)\s*/\s*(\d+)', s)
            if m and int(m.group(2)) > 0:
                self.pb['value'] = 100.0 * int(m.group(1)) / int(m.group(2))
            if s.startswith('주소:'):
                self.url.set(s.split(':', 1)[1].strip())
                self.url_entry.pack(side='left', fill='x', expand=True)
                self.url_btn.pack(side='left', padx=8)
            self.status.set(s.strip()[:90])
            return
        m = re.search(r'360 사진 (\d+)장', s)
        if m:
            self.total = int(m.group(1))
        m = re.match(r'^\S+\s+(\d+) / (\d+)\s+\(', s)
        if m and self.total:
            done, tot = int(m.group(1)), int(m.group(2))
            self.pb['value'] = 100.0 * done / max(1, tot)
            left = el / max(1, done) * (tot - done)
            self.status.set('360 사진 %d / %d · 남은 시간 약 %d분' % (done, tot, left / 60 + 1))
        elif '높이 ' in s and '초:' in s:
            self.status.set('땅 높이 재는 중 · %d초' % el)
        elif '읽는 중' in s:
            self.status.set(s.split(' ', 1)[1][:80])
        elif s.split(' ', 1)[-1].startswith('끝'):
            self.status.set('끝')

    def _ended(self, code):
        self.btn_go.configure(state='normal')
        self.btn_stop.configure(state='disabled')
        if code == 0:
            self.pb['value'] = 100
            if self.kind == 'build':
                self.status.set('끝 · [로드뷰 열기] 또는 [NAS 올리기]')
            else:
                self.status.set('올리기 끝 · 주소를 복사해 보내면 됨')
        elif self.status.get().startswith('멈춤'):
            pass
        else:
            self.status.set('실패 · 아래 기록 확인')
        self._refresh_buttons()

    def _refresh_buttons(self):
        out = self.out_dir.get().strip()
        ready = bool(out) and os.path.exists(os.path.join(out, 'index.html'))
        busy = self.proc is not None and self.proc.poll() is None
        self.btn_open.configure(state='normal' if ready else 'disabled')
        share_ok = os.path.exists(os.path.join(PKG_PARENT, 'roadview', 'share.py'))
        self.btn_share.configure(state='normal' if (ready and share_ok and not busy) else 'disabled')

    def open_view(self):
        p = os.path.join(self.out_dir.get().strip(), '로드뷰.html')
        if os.path.exists(p):
            os.startfile(p)

    def share(self):
        out = self.out_dir.get().strip()
        if not messagebox.askyesno('NAS 올리기', '이 로드뷰를 NAS 에 올려 웹 주소를 만들까요?\n(주소를 아는 사람은 누구나 볼 수 있음)'):
            return
        self._run([_python_exe(), '-u', '-m', 'roadview.share', out], 'share')

    def copy_url(self):
        self.root.clipboard_clear()
        self.root.clipboard_append(self.url.get())
        self.status.set('주소 복사함')

    # ------------------------------------------------------------ 기록 칸
    def _log_clear(self):
        self.log.configure(state='normal')
        self.log.delete('1.0', 'end')
        self.log.configure(state='disabled')

    def _log_add(self, s):
        self.log.configure(state='normal')
        self.log.insert('end', s + '\n')
        if int(self.log.index('end-1c').split('.')[0]) > 400:
            self.log.delete('1.0', '100.0')
        self.log.see('end')
        self.log.configure(state='disabled')


def main():
    try:
        from ctypes import windll
        windll.shcore.SetProcessDpiAwareness(1)     # 창보다 먼저 (글자 흐림 막기)
    except Exception:
        pass
    root = tk.Tk()
    App(root)
    root.mainloop()


if __name__ == '__main__':
    main()
