# -*- mode: python ; coding: utf-8 -*-
# 드론 로드뷰 설치본 — PyInstaller 6 (onedir)
#   cd packaging && pyinstaller --noconfirm roadview.spec
# 결과: dist\드론로드뷰\ (드론로드뷰.exe = 창 · rv_cli.exe = 뒤에서 일하는 명령)
import os
import shutil
import imageio_ffmpeg

ROOT = os.path.abspath(os.path.join(SPECPATH, '..'))
WORK = os.path.join(SPECPATH, 'build_tmp')
os.makedirs(os.path.join(WORK, 'ffmpeg'), exist_ok=True)
FF = os.path.join(WORK, 'ffmpeg', 'ffmpeg.exe')
shutil.copy2(imageio_ffmpeg.get_ffmpeg_exe(), FF)          # 이름을 ffmpeg.exe 로 (frames.find_ffmpeg 가 찾는 이름)

# 보기 화면은 «마지막으로 커밋된 것»을 넣는다 — 다른 일이 viewer/ 를 고치는 중이어도 설치본이 섞이지 않게 (1123)
import subprocess
SNAP = os.path.join(WORK, 'viewer')
os.makedirs(SNAP, exist_ok=True)
for fn in ('index.html', 'app.js', 'style.css'):
    data = subprocess.run(['git', 'show', 'HEAD:drone-roadview/viewer/' + fn], cwd=ROOT, capture_output=True, check=True).stdout
    open(os.path.join(SNAP, fn), 'wb').write(data)
datas = [
    (os.path.join(SNAP, 'index.html'), 'viewer'),
    (os.path.join(SNAP, 'app.js'), 'viewer'),
    (os.path.join(SNAP, 'style.css'), 'viewer'),
    (os.path.join(ROOT, 'roadview', 'app_home'), 'app_home'),
    (os.path.join(ROOT, 'roadview', 'rig_avata360.json'), 'roadview'),
    (FF, 'ffmpeg'),
]
hidden = ['roadview.build', 'roadview.share', 'roadview.pipes_update', 'roadview.terrain', 'roadview.photos',
          'roadview.keys', 'roadview.shp', 'roadview.pipeload', 'roadview.pipes', 'roadview.height',
          'matplotlib.tri', 'pg8000', 'pg8000.native', 'ezdxf', 'pyproj', 'webview', 'clr', 'cv2']
excludes = ['tkinter', 'roadview.gui', 'IPython', 'pytest']

a = Analysis([os.path.join(ROOT, 'rv_app.py'), os.path.join(ROOT, 'rv_cli.py')],
             pathex=[ROOT], datas=datas, hiddenimports=hidden, excludes=excludes, noarchive=False)
pyz = PYZ(a.pure)

boot = [s for s in a.scripts if s[0] not in ('rv_app', 'rv_cli')]          # 시작 스크립트 (pyiboot · pyi_rth_*)
app_exe = EXE(pyz, boot + [s for s in a.scripts if s[0] == 'rv_app'], [], exclude_binaries=True,
              name='드론로드뷰', icon=os.path.join(SPECPATH, 'roadview.ico'), console=False, upx=False)
cli_exe = EXE(pyz, boot + [s for s in a.scripts if s[0] == 'rv_cli'], [], exclude_binaries=True,
              name='rv_cli', icon=os.path.join(SPECPATH, 'roadview.ico'), console=True, upx=False)
coll = COLLECT(app_exe, cli_exe, a.binaries, a.datas, strip=False, upx=False, name='드론로드뷰')
