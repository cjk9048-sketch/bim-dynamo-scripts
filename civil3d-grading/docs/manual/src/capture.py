"""매뉴얼 삽도용 화면 기록기 — Civil 3D 창을 1초마다 찍어 화면이 바뀐 때만 저장.

쓰는 법:  python capture.py [저장폴더] [초간격]
  끝내기:  저장폴더에 STOP 파일을 만들거나 창을 닫으면(Ctrl+C) 멈춤.
파일 이름 = 시각(HHMMSS_ms).png — 나중에 DH 진단 로그(명령 시각)와 맞춰 삽도를 고른다.
"""
import ctypes, ctypes.wintypes as wt, os, sys, time
from PIL import ImageGrab, ImageChops

out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), '..', '녹화')
step = float(sys.argv[2]) if len(sys.argv) > 2 else 1.0
os.makedirs(out, exist_ok=True)
u32 = ctypes.windll.user32
try: u32.SetProcessDPIAware()
except Exception: pass

def civil_rect():
    """제목이 'Autodesk Civil 3D'로 시작하는 맨 위 창(매뉴얼 PDF 창 등은 빼고)의 화면 사각형(없으면 None)."""
    found = []
    @ctypes.WINFUNCTYPE(ctypes.c_bool, wt.HWND, wt.LPARAM)
    def cb(h, _):
        if u32.IsWindowVisible(h):
            n = u32.GetWindowTextLengthW(h)
            if n:
                b = ctypes.create_unicode_buffer(n + 1); u32.GetWindowTextW(h, b, n + 1)
                if b.value.startswith('Autodesk Civil 3D'): found.append(h)
        return True
    u32.EnumWindows(cb, 0)
    if not found: return None
    r = wt.RECT(); u32.GetWindowRect(found[0], ctypes.byref(r))
    if r.right - r.left < 200: return None
    return (max(r.left, 0), max(r.top, 0), r.right, r.bottom)

prev = None; saved = 0
log = open(os.path.join(out, '_기록.txt'), 'a', encoding='utf-8')
print(f'기록 시작 → {os.path.abspath(out)} (끝: STOP 파일 또는 Ctrl+C)')
try:
    while not os.path.exists(os.path.join(out, 'STOP')):
        t0 = time.time()
        rect = civil_rect()
        if rect:
            img = ImageGrab.grab(bbox=rect, all_screens=True)
            small = img.convert('L').resize((320, 180))
            changed = prev is None or ImageChops.difference(small, prev).getbbox() is not None and \
                      sum(ImageChops.difference(small, prev).point(lambda v: 255 if v > 24 else 0).histogram()[255:]) > 40
            if changed:
                name = time.strftime('%H%M%S') + f'_{int((t0 % 1) * 1000):03d}.png'
                img.save(os.path.join(out, name), optimize=False)
                log.write(f'{name}\t{rect}\n'); log.flush()
                prev = small; saved += 1
        time.sleep(max(0.05, step - (time.time() - t0)))
except KeyboardInterrupt:
    pass
print(f'기록 끝 — {saved}장')
