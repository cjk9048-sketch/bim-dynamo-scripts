"""DH 관로 — 번들 매니페스트(PackageContents.xml)를 다시 쓴다.
판 = src/DH.Pipe.Civil/PipeSettings.cs 의 Version · 명령 = 소스의 [CommandMethod] 전부 · 2026(R25.1) / 2027(R26.0) 두 묶음.
쓰기: python -X utf8 tools/setup/pkg.py
"""
import glob
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "src", "DH.Pipe.Civil")
ver = re.search(r'Version\s*=\s*"v([0-9.]+)', open(os.path.join(SRC, "PipeSettings.cs"), encoding="utf-8").read()).group(1)
cmds = []
for f in glob.glob(os.path.join(SRC, "**", "*.cs"), recursive=True):
    if os.sep + "obj" + os.sep in f or os.sep + "bin" + os.sep in f:
        continue
    for m in re.finditer(r'\[CommandMethod\(\s*"[^"]*"\s*,\s*"([A-Z0-9_]+)"', open(f, encoding="utf-8").read()):
        if m.group(1) not in cmds:
            cmds.append(m.group(1))
cmds.sort()


def comp(desc, series, module):
    lines = [f'  <Components Description="{desc}">',
             f'    <RuntimeRequirements OS="Win64" Platform="AutoCAD*" SeriesMin="{series}" SeriesMax="{series}" />',
             f'    <ComponentEntry AppName="DH.Pipe" ModuleName="{module}" LoadOnAutoCADStartup="True" PerDocument="False">',
             '      <Commands GroupName="DH_PIPE">']
    lines += [f'        <Command Global="{c}" Local="{c}" />' for c in cmds]
    lines += ['      </Commands>', '    </ComponentEntry>', '  </Components>']
    return lines


out = ['<?xml version="1.0" encoding="utf-8"?>',
       '<!-- Civil 3D 자동 로드 매니페스트 — tools/setup/pkg.py 가 다시 쓴다(손으로 고치지 말 것).',
       r'     이 .bundle 폴더를 %APPDATA%\Autodesk\ApplicationPlugins\DH.Pipe.bundle 에 두면 Civil 3D 시작 때 올라온다. -->',
       f'<ApplicationPackage SchemaVersion="1.0" AppVersion="{ver}" ProductType="Application" Name="DH 관로"',
       '                    Description="상수 관로 — 노선 · 종단 · 구간 공법 · 횡단 · 토적표 · 3D">',
       '  <CompanyDetails Name="도화엔지니어링 상하수도부" />']
out += comp("DH Pipe", "R25.1", "./Contents/DH.Pipe.Civil.dll")
out += comp("DH Pipe 2027", "R26.0", "./Contents/2027/DH.Pipe.Civil.dll")
out += ['</ApplicationPackage>', '']
path = os.path.join(SRC, "DH.Pipe.bundle", "PackageContents.xml")
open(path, "w", encoding="utf-8-sig", newline="\r\n").write("\n".join(out))
print(f"매니페스트: 판 {ver} · 명령 {len(cmds)}개 → {path}")
