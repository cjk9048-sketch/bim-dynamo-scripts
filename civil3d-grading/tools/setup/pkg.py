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

io.open(p, 'w', encoding='utf-8-sig', newline='').write(s)
print(f"AppVersion={app} · 명령 {len(cmds)}개(추가 {len(add)}개)")
