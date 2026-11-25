"""배포 번들 조립 — src의 .bundle 폴더를 그대로 zip 으로 굳힌다.

★[JACK 0915] 넣는 것과 빼는 것을 <b>여기 한 곳</b>에 적어 둔다.
  넣는다: Contents 전부(DLL·LSP·좌표계) + PackageContents.xml + PDB(진단 로그에 줄번호가 찍힌다)
  뺀다  : template/(애드인 코드가 안 쓴다 — 24MB 중 4.7MB가 .git 찌꺼기다), 빌드 잔재
"""
import io
import os
import re
import zipfile
import hashlib
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import updloc  # noqa: E402  ★새 판 확인 위치(--update) — 묶음에만 넣는다

LOCS = updloc.locations()

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
        if rel == "Contents/dh.keys.json":
            data = updloc.merged_keys(full, LOCS)   # 원본 파일은 안 고친다
            z.writestr(rel, data)
            updloc.report(data)
        else:
            z.write(full, rel)

total = sum(os.path.getsize(f) for f, _ in files)
print("bundle.zip = %d files · raw %.1f MB · zip %.1f MB"
      % (len(files), total / 1e6, os.path.getsize(OUT) / 1e6))
for full, rel in files:
    print("   %-58s %8d" % (rel, os.path.getsize(full)))

# 필수 파일이 정말 들어갔는지 확인 — 빠뜨리면 설치는 되는데 안 뜬다
must = ["PackageContents.xml", "Contents/DH.Grading.Civil.dll", "Contents/DH.Grading.Core.dll",
        "Contents/NetTopologySuite.dll", "Contents/3D_VIEW_LOCATION.LSP",
        "Contents/coordsys/CSLibrary.xml",
        # ★[v109 · 설치 전 검토 중간 ②] 내장 VWorld 키 · 사내 DB 접속 정보(설치 전용 · git에 없음) — 빠지면 받은 사람은 위성 · 등고선이 «키 없음».
        #   ★이 묶음(EXE · ZIP)에는 비밀값이 들어간다 — 사내 배포만(공개 저장소 릴리스 · 공개 NAS 금지).
        "Contents/dh.keys.json"]
have = {r for _, r in files}
# ★[1125 · Civil 3D 2027] 매니페스트가 2027 묶음(Contents/2027/)을 가리키면 그 DLL도 꼭 있어야 한다(빌드: dotnet build -c Release -p:CivilYear=2027)
_pc = io.open(os.path.join(SRC, "PackageContents.xml"), encoding="utf-8-sig").read()
has27 = "./Contents/2027/DH.Grading.Civil.dll" in _pc
if has27:
    must += ["Contents/2027/DH.Grading.Civil.dll", "Contents/2027/DH.Grading.Core.dll", "Contents/2027/NetTopologySuite.dll"]
missing = [m for m in must if m not in have]
print("MISSING:", missing if missing else "none")
if missing:
    raise SystemExit("필수 파일이 빠졌습니다 — 묶음을 만들지 않습니다: %s" % missing)

d = io.open(os.path.join(SRC, "Contents", "DH.Grading.Civil.dll"), "rb").read()
print("dll sha256 =", hashlib.sha256(d).hexdigest()[:16])

# ★[1125] 2027 DLL이 옛것이면 멈춘다 — 2026만 다시 빌드하고 2027을 잊으면 2027 사용자에게 옛 판이 깔린다.
#   ① Core는 두 판이 같은 net8.0 빌드 → 바이트가 같아야 한다  ② 두 Civil DLL 모두 지금 판 문자열(GradingSettings.Version)을 품어야 한다
if has27:
    d27 = io.open(os.path.join(SRC, "Contents", "2027", "DH.Grading.Civil.dll"), "rb").read()
    print("dll 2027 sha256 =", hashlib.sha256(d27).hexdigest()[:16])
    core = [io.open(os.path.join(SRC, "Contents", *sub, "DH.Grading.Core.dll"), "rb").read() for sub in ((), ("2027",))]
    _v = re.search(r'Version = "(v[0-9.]+)', io.open("src/DH.Grading.Civil/GradingSettings.cs", encoding="utf-8-sig").read()).group(1)
    stale = []
    if core[0] != core[1]:
        stale.append("Core.dll 두 판이 다름")
    for nm, blob in (("2026", d), ("2027", d27)):
        if _v.encode("utf-16-le") not in blob:
            stale.append(f"{nm} Civil DLL에 {_v} 없음")
    if stale:
        raise SystemExit("2026 · 2027 DLL이 같은 판이 아닙니다(%s) — 두 판을 다 빌드하세요: "
                         "dotnet build -c Release  ·  dotnet build -c Release -p:CivilYear=2027" % " · ".join(stale))
    print("2026 · 2027 같은 판:", _v)
