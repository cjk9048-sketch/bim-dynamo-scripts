"""DH 관로 — 번들 폴더를 이 PC 의 Civil 3D 에 설치한다(복사).

- Civil 3D(acad.exe)가 켜져 있으면 멈춘다(켜진 채 번들을 바꾸면 반쯤 올라간 채로 남는다).
- 이미 깔린 것이 있으면 먼저 백업 폴더로 복사한다.
- 복사한 뒤 DLL 지문(SHA-256)이 빌드와 같은지 확인한다.

쓰기: python -X utf8 tools/setup/install.py [--backup <백업 둘 폴더>] [--check]
  --check  설치하지 않고 깔린 것과 빌드의 지문만 견줌
끝 코드: 0 = 됨 · 1 = 지문 다름 · 2 = 못 함
"""
import argparse
import hashlib
import os
import shutil
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "src", "DH.Pipe.Civil", "DH.Pipe.bundle")
DST = os.path.join(os.environ.get("APPDATA", ""), "Autodesk", "ApplicationPlugins", "DH.Pipe.bundle")
MUST = ["PackageContents.xml", os.path.join("Contents", "DH.Pipe.Civil.dll"), os.path.join("Contents", "DH.Pipe.Core.dll")]

ap = argparse.ArgumentParser()
ap.add_argument("--backup", default=os.path.join(os.path.dirname(ROOT), "설치백업"))
ap.add_argument("--check", action="store_true")
a = ap.parse_args()


def sha(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def files(base):
    res = {}
    for d, _, names in os.walk(base):
        for n in names:
            p = os.path.join(d, n)
            res[os.path.relpath(p, base)] = p
    return res


def compare():
    src, dst = files(SRC), files(DST) if os.path.isdir(DST) else {}
    bad = [r for r in src if r not in dst or sha(src[r]) != sha(dst[r])]
    extra = [r for r in dst if r not in src]
    return src, bad, extra


for m in MUST:
    if not os.path.exists(os.path.join(SRC, m)):
        print(f"원인: 번들에 {m} 없음 → 먼저 Release 빌드 · pkg.py")
        sys.exit(2)

if a.check:
    src, bad, extra = compare()
    print(f"빌드 파일 {len(src)}개 · 다른 것 {len(bad)}개 · 설치본에만 있는 것 {len(extra)}개")
    for r in bad[:20]:
        print("  다름:", r)
    sys.exit(1 if bad else 0)

out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq acad.exe"], capture_output=True, text=True, errors="replace").stdout
if "acad.exe" in out.lower():
    print("원인: Civil 3D(acad.exe)가 켜져 있음 → 닫고 다시")
    sys.exit(2)

if os.path.isdir(DST):
    bk = os.path.join(a.backup, "DH관로백업_" + time.strftime("%Y%m%d_%H%M"))
    os.makedirs(a.backup, exist_ok=True)
    shutil.copytree(DST, bk)
    print("백업:", bk)
    shutil.rmtree(DST)
shutil.copytree(SRC, DST)
src, bad, extra = compare()
dll = os.path.join(DST, "Contents", "DH.Pipe.Civil.dll")
print(f"설치: {DST}")
print(f"파일 {len(src)}개 · 지문 다른 것 {len(bad)}개 · DH.Pipe.Civil.dll {sha(dll)[:8]}")
dll27 = os.path.join(DST, "Contents", "2027", "DH.Pipe.Civil.dll")
print("2027 판:", sha(dll27)[:8] if os.path.exists(dll27) else "없음(2026 만)")
sys.exit(1 if bad else 0)
