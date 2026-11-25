import io
import os
import re

p = "src/DH.Grading.Civil/DH.Grading.bundle/PackageContents.xml"
s = io.open(p, encoding='utf-8-sig').read()

# ① 버전 — 애드인 버전과 맞춘다(★1015 셋째 자리까지 — v103.1.12면 103.1.12 · 설치 창이 찍는 판과 애드인 판이 같게)
ver = io.open("src/DH.Grading.Civil/GradingSettings.cs", encoding='utf-8-sig').read()
m = re.search(r'Version = "v([0-9]+)\.([0-9]+)(?:\.([0-9]+))?', ver)
app = f'{m.group(1)}.{m.group(2)}.{m.group(3) or 0}'
s = re.sub(r'AppVersion="[^"]*"', f'AppVersion="{app}"', s)

# ② 코드에 있는 명령을 전부 싣는다
#   ★1015 외부 grep을 안 쓴다 — 이 PC의 파이썬에서 grep이 아무것도 안 돌려줘(«명령 0개») DHWALLCOMP가 빠진 채 지나갔다
cmds = set()
for root, dirs, names in os.walk("src"):
    dirs[:] = [d for d in dirs if d not in {"obj", "bin", ".git", ".vs"}]
    for nm in names:
        if nm.endswith(".cs"):
            t = io.open(os.path.join(root, nm), encoding='utf-8-sig', errors='replace').read()
            cmds.update(re.findall(r'CommandMethod\("([A-Z0-9]*)"', t))
cmds = sorted(cmds)
assert len(cmds) > 0, "코드에서 명령을 하나도 못 찾았다 — 찾는 자리를 확인할 것"
# ★[1125 · Civil 3D 2027] 2027 묶음은 아래 ③에서 2026 묶음을 베껴 다시 만든다 — 먼저 걷어 내고 2026 묶음만 고친다
B27 = re.compile(r'[ \t]*<Components Description="DH Grading 2027">.*?</Components>[ \t]*\r?\n', re.S)
s = B27.sub('', s)
have = set(re.findall(r'Global="([A-Z0-9]*)"', s))
add = [c for c in cmds if c not in have]
if add:
    block = '\n'.join(f'        <Command Global="{c}" Local="{c}" />' for c in add)
    anchor = '      </Commands>'
    assert s.count(anchor) == 1
    s = s.replace(anchor,
                  '        <!-- ★[JACK 0915 배포판] 코드의 [CommandMethod]를 전부 싣는다 —\n'
                  '             자동 로드라 없어도 돌긴 하지만, 매니페스트가 실제와 어긋나 있으면\n'
                  '             "이 명령이 있는지" 확인할 길이 도면 쪽에 남지 않는다. -->\n'
                  + block + '\n' + anchor)

# ③ ★[1125 · Civil 3D 2027] 2027(R26.0) 묶음 = 2026 묶음 그대로(명령 목록 · LSP 같음) — 판과 DLL 자리(Contents/2027/)만 바꾼다
#   공유 자료(LSP · coordsys · Tutorial …)는 Contents/ 한 벌 — 2027 DLL은 BundleContents.Root 로 찾는다
m26 = re.search(r'[ \t]*<Components Description="DH Grading">.*?</Components>[ \t]*\r?\n', s, re.S)
assert m26, "2026 <Components Description=\"DH Grading\"> 묶음을 못 찾았다"
b27 = m26.group(0).replace('Description="DH Grading"', 'Description="DH Grading 2027"', 1)
b27, n1 = re.subn(r'SeriesMin="[^"]*" SeriesMax="[^"]*"', 'SeriesMin="R26.0" SeriesMax="R26.0"', b27)
b27, n2 = re.subn(r'ModuleName="\./Contents/DH\.Grading\.Civil\.dll"', 'ModuleName="./Contents/2027/DH.Grading.Civil.dll"', b27)
assert n1 == 1 and n2 == 1, f"2027 묶음 바꿈 수가 이상하다(판 {n1} · DLL {n2})"
s = s[:m26.end()] + b27 + s[m26.end():]

io.open(p, 'w', encoding='utf-8-sig', newline='').write(s)
print(f"AppVersion={app} · 명령 {len(cmds)}개(추가 {len(add)}개) · 묶음 2026(R25.1) + 2027(R26.0 · Contents/2027)")
