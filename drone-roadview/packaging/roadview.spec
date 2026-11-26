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
for fn in ('index.html', 'app.js', 'style.css', 'map3d.html', 'map3d.js'):
    data = subprocess.run(['git', 'show', 'HEAD:drone-roadview/viewer/' + fn], cwd=ROOT, capture_output=True, check=True).stdout
    open(os.path.join(SNAP, fn), 'wb').write(data)
datas = [
    (os.path.join(SNAP, 'index.html'), 'viewer'),
    (os.path.join(SNAP, 'app.js'), 'viewer'),
    (os.path.join(SNAP, 'style.css'), 'viewer'),
    (os.path.join(SNAP, 'map3d.html'), 'viewer'),
    (os.path.join(SNAP, 'map3d.js'), 'viewer'),
    (os.path.join(ROOT, 'roadview', 'app_home'), 'app_home'),
    (os.path.join(ROOT, 'roadview', 'rig_avata360.json'), 'roadview'),
    (FF, 'ffmpeg'),
]
hidden = ['roadview.build', 'roadview.share', 'roadview.pipes_update', 'roadview.terrain', 'roadview.photos',
          'roadview.keys', 'roadview.media', 'roadview.mapping', 'roadview.ortho', 'roadview.photo_at', 'roadview.shp', 'roadview.pipeload', 'roadview.pipes', 'roadview.height',
          'roadview.cluster', 'roadview.gsplat', 'matplotlib.tri', 'pg8000', 'pg8000.native', 'ezdxf', 'pyproj', 'webview', 'clr', 'cv2']
excludes = ['tkinter', 'roadview.gui', 'IPython', 'pytest']

a = Analysis([os.path.join(ROOT, 'rv_app.py'), os.path.join(ROOT, 'rv_cli.py')],
             pathex=[ROOT], datas=datas, hiddenimports=hidden, excludes=excludes, noarchive=False)
pyz = PYZ(a.pure)

boot = [s for s in a.scripts if s[0] not in ('rv_app', 'rv_cli')]          # 시작 스크립트 (pyiboot · pyi_rth_*)

# 판 정보 (회사 · 제품 · 판) — 판 정보 없는 콘솔 exe 를 회사 보안 프로그램이 바로 지웠다 (1124 · 1.0.7 rv_cli.exe)
import re as _re
_ver = _re.search(r"APP_VERSION = '([\d.]+)'", open(os.path.join(ROOT, 'roadview', 'app.py'), encoding='utf-8').read()).group(1)
_v4 = tuple((list(map(int, _ver.split('.'))) + [0, 0, 0, 0])[:4])


def _verfile(name, desc):
    p = os.path.join(WORK, 'ver_%s.txt' % name)
    s = """VSVersionInfo(
  ffi=FixedFileInfo(filevers=%(v)r, prodvers=%(v)r, mask=0x3f, flags=0x0, OS=0x40004, fileType=0x1, subtype=0x0, date=(0, 0)),
  kids=[StringFileInfo([StringTable('041204B0', [
      StringStruct('CompanyName', 'DOHWA Digital Design Team'),
      StringStruct('FileDescription', %(d)r),
      StringStruct('FileVersion', %(s)r),
      StringStruct('InternalName', %(n)r),
      StringStruct('OriginalFilename', %(n)r + '.exe'),
      StringStruct('ProductName', 'DH Drone Roadview'),
      StringStruct('ProductVersion', %(s)r)])]),
    VarFileInfo([VarStruct('Translation', [0x0412, 1200])])])
""" % {'v': _v4, 's': _ver, 'd': desc, 'n': name}
    open(p, 'w', encoding='utf-8').write(s)
    return p


app_exe = EXE(pyz, boot + [s for s in a.scripts if s[0] == 'rv_app'], [], exclude_binaries=True,
              name='드론로드뷰', icon=os.path.join(SPECPATH, 'roadview.ico'), console=False, upx=False,
              version=_verfile('DroneRoadview', 'DH Drone Roadview'))
cli_exe = EXE(pyz, boot + [s for s in a.scripts if s[0] == 'rv_cli'], [], exclude_binaries=True,
              name='rv_cli', icon=os.path.join(SPECPATH, 'roadview.ico'), console=True, upx=False,
              version=_verfile('rv_cli', 'DH Drone Roadview worker (build / share)'))
coll = COLLECT(app_exe, cli_exe, a.binaries, a.datas, strip=False, upx=False, name='드론로드뷰')
