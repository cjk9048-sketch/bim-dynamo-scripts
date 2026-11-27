"""DH 관로 — 화면 없는 Civil 3D(accoreconsole) 자가 시험.

빈 Civil 도면(템플릿 사본)을 열어 DLL 을 올리고 DHPIPESELFTEST 를 돌린다. 결과 = <작업 폴더>\\selftest.txt.
JACK 의 Civil(acad.exe) · 설정은 건드리지 않는다(/isolate 로 따로 둔 프로필 · 도면은 사본).

쓰기:  python -X utf8 tools/headless/run_selftest.py [--year 2026|2027] [--plain] [--work <폴더>]
  --plain  Civil 스타일이 없는 맨 도면(acad 템플릿)에서 — 스타일이 없어도 도는지 본다
끝 코드: 0 = 전부 통과 · 1 = 실패 있음 · 2 = 못 돌림
"""
import argparse, os, shutil, subprocess, sys, time, winreg

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ap = argparse.ArgumentParser()
ap.add_argument("--year", default="2026")
ap.add_argument("--plain", action="store_true")
ap.add_argument("--work", default="")
ap.add_argument("--timeout", type=int, default=420)
a = ap.parse_args()

acc = rf"C:\Program Files\Autodesk\AutoCAD {a.year}\accoreconsole.exe"
sub = "\\2027" if a.year == "2027" else ""
dll = os.path.join(ROOT, "src", "DH.Pipe.Civil", "DH.Pipe.bundle", "Contents" + sub, "DH.Pipe.Civil.dll")
local = os.environ.get("LOCALAPPDATA", "")
if a.plain:
    tpl = os.path.join(local, "Autodesk", f"C3D {a.year}", "kor", "Template", "AutoCAD Template", "acadiso.dwt")
else:
    tpl = os.path.join(local, "Autodesk", f"C3D {a.year}", "kor", "Template", "_Autodesk Civil 3D (Metric) NCS.dwt")
for p, what in ((acc, "accoreconsole"), (dll, "DLL(먼저 Release 빌드)"), (tpl, "템플릿")):
    if not os.path.exists(p):
        print(f"원인: {what} 없음 — {p}")
        sys.exit(2)

work = a.work or os.path.join(os.environ.get("TEMP", "."), "dhpipe-headless", time.strftime("%m%d_%H%M%S") + ("_plain" if a.plain else "") + "_" + a.year)
os.makedirs(os.path.join(work, "tmp"), exist_ok=True)
os.makedirs(os.path.join(work, "iso"), exist_ok=True)
dwg = os.path.join(work, "test.dwg")
shutil.copyfile(tpl, dwg)
scr = os.path.join(work, "run.scr")
out_dxf = os.path.join(work, "out.dxf")
with open(scr, "w", encoding="ascii") as f:
    f.write("SECURELOAD\n0\nFILEDIA\n0\nNETLOAD\n\"" + dll + "\"\nDHPIPESELFTEST\n_.DXFOUT\n\"" + out_dxf + "\"\n16\n_.QUIT\n_Y\n")

env = dict(os.environ)
env["DH_PIPE_TEST_OUT"] = work
env["DH_PIPE_LOG_DIR"] = work
env["TEMP"] = env["TMP"] = os.path.join(work, "tmp")

# 2027 을 돌리면 압력관 카탈로그 경로(사용자 설정)가 바뀔 수 있다 — 돌리기 전 값을 적어 두었다가 되돌린다
KEY, VAL = r"Software\Autodesk\UtilitiesContent\1.0", "ContentPath"
def read_content():
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, KEY) as k:
            return winreg.QueryValueEx(k, VAL)[0]
    except OSError:
        return None
before = read_content()

t0 = time.time()
args = [acc, "/i", dwg, "/s", scr, "/isolate", "dhpipe" + a.year, os.path.join(work, "iso"), "/l", "ko-KR"]
rc, timed = None, False
try:
    p = subprocess.run(args, stdin=subprocess.DEVNULL, capture_output=True, timeout=a.timeout, env=env, cwd=work)   # 오류 보고(ErrorReports)가 저장소가 아니라 작업 폴더에 떨어지게
    rc = p.returncode
    log = p.stdout.decode("utf-16-le", "replace")
except subprocess.TimeoutExpired as ex:
    timed = True
    log = (ex.stdout or b"").decode("utf-16-le", "replace")
with open(os.path.join(work, "accore.log"), "w", encoding="utf-8") as f:
    f.write(log)

after = read_content()
if before is not None and after != before:
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, KEY, 0, winreg.KEY_SET_VALUE) as k:
            winreg.SetValueEx(k, VAL, 0, winreg.REG_SZ, before)
        print("압력관 카탈로그 경로 되돌림")
    except OSError as ex:
        print("원인: 압력관 카탈로그 경로를 못 되돌림 —", ex)

res = os.path.join(work, "selftest.txt")
print(f"작업 폴더: {work}")
print(f"Civil {a.year} · {'맨 도면' if a.plain else 'Civil 템플릿'} · {time.time() - t0:.0f}초 · 끝 코드 {rc}{' · 시간 초과' if timed else ''}")
if not os.path.exists(res):
    print("원인: 결과 파일 없음 — 기록 끝부분:")
    print("\n".join(l for l in log.replace("\x00", "").splitlines()[-40:] if l.strip()))
    sys.exit(2)
text = open(res, encoding="utf-8").read()
print(text)
fail = sum(1 for l in text.splitlines() if l.startswith("FAIL"))
print("DXF:", "있음" if os.path.exists(out_dxf) else "없음")
sys.exit(1 if fail or timed else 0)
