"""새 판 알림 매니페스트(배포/latest.json)를 만든다 — 설치한 사람의 Civil 3D가 이것을 읽고 «새 판 있음»을 띄운다.

★숫자를 손으로 적지 않는다(relnote.py와 같은 규칙) — 판 번호 · 날짜는 GradingSettings.Version,
  sha256은 실제 설치 exe에서 재고, notes는 src/DH.Grading.Civil/Resources/ReleaseNotes.txt 의 그 판 칸에서 읽는다.

  python tools/setup/mklatest.py                    # installer = exe 파일 이름(latest.json 옆에 두는 경우 — 공유 폴더)
  python tools/setup/mklatest.py --installer https://…/DH정지플러그인_설치_v117.19.0.exe
  python tools/setup/mklatest.py --mandatory        # 꼭 설치할 판(«이 판 알리지 않기»를 눌러도 다시 알림)

latest.json 꼴:
  { "version": "117.19.0", "date": "2026-11-24", "mandatory": false,
    "notes": ["…", "…"], "installer": "DH정지플러그인_설치_v117.19.0.exe", "sha256": "<64자 16진>" }
  installer = 상대 경로(latest.json 자리 기준) · 절대 경로/UNC · https 주소(sha256 필수 · 받아서 지문을 맞춘다).
"""
import argparse
import hashlib
import io
import json
import os
import re
import sys

ap = argparse.ArgumentParser()
ap.add_argument("--installer", default="", help="latest.json에 적을 설치 파일 위치(기본 = exe 파일 이름)")
ap.add_argument("--mandatory", action="store_true", help="꼭 설치할 판")
ap.add_argument("--out", default="배포/latest.json")
a = ap.parse_args()
try:
    sys.stdout.reconfigure(errors="replace")   # cp949 창에서 «—» 때문에 죽지 않게
except Exception:
    pass


def die(msg):
    print("멈춤 — " + msg)
    sys.exit(1)


line = re.search(r'Version = "(v([0-9]+(?:\.[0-9]+){1,3})(?: \(([0-9]{4}-[0-9]{2}-[0-9]{2})\))?)"',
                 io.open("src/DH.Grading.Civil/GradingSettings.cs", encoding="utf-8-sig").read())
if not line:
    die("GradingSettings.Version 을 못 읽었습니다")
ver, num, date = line.group(1).split(" ")[0], line.group(2), line.group(3) or ""

app = re.search(r'AppVersion="([0-9.]+)"',
                io.open("src/DH.Grading.Civil/DH.Grading.bundle/PackageContents.xml", encoding="utf-8-sig").read()).group(1)


def parts(v):
    p = [int(x) for x in v.split(".")]
    return p + [0] * (4 - len(p))


if parts(app) != parts(num):
    die(f"PackageContents AppVersion {app} ≠ 판 {num} — pkg.py 를 먼저 돌리세요")

exe = f"배포/DH정지플러그인_설치_{ver}.exe"
if not os.path.exists(exe):
    die(f"{exe} 가 없습니다 — 설치 exe를 먼저 만드세요(dotnet publish … → 배포/ 로 복사)")
sha = hashlib.sha256(io.open(exe, "rb").read()).hexdigest()

# 이번 판 바뀐 점 — ReleaseNotes.txt 의 «# v117.19.0 (…)» 칸
notes, on = [], False
for raw in io.open("src/DH.Grading.Civil/Resources/ReleaseNotes.txt", encoding="utf-8-sig").read().splitlines():
    t = raw.strip()
    if t.startswith("#"):
        if on:
            break
        m = re.match(r"#+\s*v?([0-9]+(?:\.[0-9]+){1,3})", t)
        on = bool(m) and parts(m.group(1)) == parts(num)
        continue
    if on and t:
        notes.append(t[2:] if t.startswith("- ") else t)
if not notes:
    die(f"ReleaseNotes.txt 에 «# {ver} ({date})» 칸이 없습니다 — 이번 판 바뀐 점을 먼저 적으세요")

inst = a.installer.strip() or os.path.basename(exe)
if inst.lower().startswith("http://"):
    die("installer 주소는 https 만 됩니다")

doc = {"version": num, "date": date, "mandatory": bool(a.mandatory), "notes": notes, "installer": inst, "sha256": sha}
os.makedirs(os.path.dirname(a.out) or ".", exist_ok=True)
io.open(a.out, "w", encoding="utf-8", newline="\n").write(json.dumps(doc, ensure_ascii=False, indent=2) + "\n")
print(f"{a.out} — v{num} ({date}) · 꼭 설치 {'예' if a.mandatory else '아니오'} · 노트 {len(notes)}줄 · installer {inst}")
print(f"sha256 {sha}")
