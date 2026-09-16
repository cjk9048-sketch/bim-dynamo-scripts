import io
import re
import subprocess

p = "src/DH.Grading.Civil/DH.Grading.bundle/PackageContents.xml"
s = io.open(p, encoding='utf-8-sig').read()

# ① 버전 — 애드인 버전과 맞춘다
ver = io.open("src/DH.Grading.Civil/GradingSettings.cs", encoding='utf-8-sig').read()
m = re.search(r'Version = "v([0-9]+)\.([0-9]+)', ver)
app = f'{m.group(1)}.{m.group(2)}.0'
s = re.sub(r'AppVersion="[^"]*"', f'AppVersion="{app}"', s)

# ② 코드에 있는 명령을 전부 싣는다
code = subprocess.run(
    ['grep', '-rhoa', 'CommandMethod("[A-Z0-9]*"', '--include=*.cs', 'src/'],
    capture_output=True, text=True).stdout
cmds = sorted({x.split('"')[1] for x in code.splitlines() if '"' in x})
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
