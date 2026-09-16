"""EXE가 막히는 환경용 — 수동 설치 ZIP 을 만든다.

  DH정지플러그인_v93.8_수동설치.zip
    ├ 설치.bat          더블클릭 한 번
    ├ 설치.ps1
    ├ 읽어보세요.txt
    └ DH.Grading.bundle/  (설치.ps1 이 제 옆에서 이 폴더를 찾는다)
"""
import io
import os
import re
import shutil
import zipfile

SRC = "src/DH.Grading.Civil/DH.Grading.bundle"
STAGE = "배포/DH.Grading_설치"
SKIP_DIR = {"template", "obj", "bin", ".git", ".vs"}
SKIP_EXT = {".zip", ".log", ".tmp"}

ver = io.open("src/DH.Grading.Civil/GradingSettings.cs", encoding='utf-8-sig').read()
v = re.search(r'Version = "(v[0-9.]+)', ver).group(1)

# ① 스테이징의 번들을 현재 것으로 갈아 끼운다(옛 파일이 남지 않게 지우고 새로)
dstb = os.path.join(STAGE, "DH.Grading.bundle")
if os.path.isdir(dstb):
    shutil.rmtree(dstb)
n = 0
for root, dirs, names in os.walk(SRC):
    dirs[:] = [d for d in dirs if d not in SKIP_DIR]
    for nm in names:
        if os.path.splitext(nm)[1].lower() in SKIP_EXT:
            continue
        s = os.path.join(root, nm)
        rel = os.path.relpath(s, SRC)
        d = os.path.join(dstb, rel)
        os.makedirs(os.path.dirname(d), exist_ok=True)
        shutil.copy2(s, d)
        n += 1

# ② 읽어보세요.txt
readme = os.path.join(STAGE, "읽어보세요.txt")
io.open(readme, "w", encoding="utf-8-sig", newline="\r\n").write(
    "DH 정지(부지정지) 플러그인 — 수동 설치\r\n"
    "=" * 44 + "\r\n\r\n"
    "버전 : " + v + "\r\n"
    "대상 : AutoCAD Civil 3D 2025 ~ 2026 (R25.0 ~ R25.1) · Windows 64비트\r\n\r\n"
    "설치 방법\r\n"
    "  1) Civil 3D 를 <완전히> 닫습니다. (켜져 있으면 파일이 잠겨 설치가 중단됩니다)\r\n"
    "  2) 이 폴더의 '설치.bat' 을 더블클릭합니다.\r\n"
    "  3) 검은 창에 '설치 완료' 가 뜨면 끝입니다.\r\n"
    "  4) Civil 3D 를 실행하면 자동으로 올라옵니다. 명령창에 DHGRADE 를 쳐 보세요.\r\n\r\n"
    "설치되는 곳\r\n"
    "  %APPDATA%\\Autodesk\\ApplicationPlugins\\DH.Grading.bundle\r\n"
    "  한국 좌표계 9종(사용자 사전이 없을 때만 새로 넣습니다. 이미 있으면 건드리지 않습니다)\r\n\r\n"
    "지우려면\r\n"
    "  위 DH.Grading.bundle 폴더를 통째로 지우면 됩니다.\r\n\r\n"
    "잘 안 될 때\r\n"
    "  · '실행 중입니다' 가 뜨면 작업관리자에서 acad.exe 가 남아 있는지 보세요.\r\n"
    "  · 회사 보안정책으로 exe 가 막히면 이 ZIP 판을 쓰시면 됩니다(같은 내용입니다).\r\n"
    "  · 그래도 안 뜨면 Civil 3D 명령창에 NETLOAD 를 치고 위 폴더의\r\n"
    "    Contents\\DH.Grading.Civil.dll 을 직접 지정해 보세요.\r\n")

# ③ zip
out = "배포/DH정지플러그인_" + v + "_수동설치.zip"
if os.path.exists(out):
    os.remove(out)
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for f in ["설치.bat", "설치.ps1"]:
        z.write(os.path.join("배포", f), f)
    z.write(readme, "읽어보세요.txt")
    for root, dirs, names in os.walk(dstb):
        for nm in names:
            p = os.path.join(root, nm)
            z.write(p, os.path.relpath(p, STAGE).replace(os.sep, "/"))

print("bundle files staged =", n)
print("zip =", out, "%.1f MB" % (os.path.getsize(out) / 1e6))
with zipfile.ZipFile(out) as z:
    for x in sorted(z.namelist()):
        print("   ", x)
