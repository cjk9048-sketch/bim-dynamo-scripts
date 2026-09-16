"""배포 번들 조립 — src의 .bundle 폴더를 그대로 zip 으로 굳힌다.

★[JACK 0915] 넣는 것과 빼는 것을 <b>여기 한 곳</b>에 적어 둔다.
  넣는다: Contents 전부(DLL·LSP·좌표계) + PackageContents.xml + PDB(진단 로그에 줄번호가 찍힌다)
  뺀다  : template/(애드인 코드가 안 쓴다 — 24MB 중 4.7MB가 .git 찌꺼기다), 빌드 잔재
"""
import io
import os
import zipfile
import hashlib

SRC = "src/DH.Grading.Civil/DH.Grading.bundle"
OUT = "tools/setup/bundle.zip"

SKIP_DIR = {"template", "obj", "bin", ".git", ".vs"}
SKIP_EXT = {".zip", ".log", ".tmp"}

files = []
for root, dirs, names in os.walk(SRC):
    dirs[:] = [d for d in dirs if d not in SKIP_DIR]
    for n in names:
        if os.path.splitext(n)[1].lower() in SKIP_EXT:
            continue
        full = os.path.join(root, n)
        rel = os.path.relpath(full, SRC).replace(os.sep, "/")
        files.append((full, rel))
files.sort(key=lambda x: x[1])

if os.path.exists(OUT):
    os.remove(OUT)
with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for full, rel in files:
        z.write(full, rel)

total = sum(os.path.getsize(f) for f, _ in files)
print("bundle.zip = %d files · raw %.1f MB · zip %.1f MB"
      % (len(files), total / 1e6, os.path.getsize(OUT) / 1e6))
for full, rel in files:
    print("   %-58s %8d" % (rel, os.path.getsize(full)))

# 필수 파일이 정말 들어갔는지 확인 — 빠뜨리면 설치는 되는데 안 뜬다
must = ["PackageContents.xml", "Contents/DH.Grading.Civil.dll", "Contents/DH.Grading.Core.dll",
        "Contents/NetTopologySuite.dll", "Contents/3D_VIEW_LOCATION.LSP",
        "Contents/coordsys/CSLibrary.xml"]
have = {r for _, r in files}
missing = [m for m in must if m not in have]
print("MISSING:", missing if missing else "none")

d = io.open(os.path.join(SRC, "Contents", "DH.Grading.Civil.dll"), "rb").read()
print("dll sha256 =", hashlib.sha256(d).hexdigest()[:16])
