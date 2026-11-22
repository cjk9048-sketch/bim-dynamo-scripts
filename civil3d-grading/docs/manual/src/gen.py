"""DH Grading 사용자 매뉴얼 생성기 — 가벼운 표기 → HTML + 삽도 목록.
표기:
  # 제목            → 장(h1) (CH 목록에서 자동)
  ## 4.1 제목       → 절(h2, 목차에 들어감)
  ### 제목          → 소제목(h3)
  #### 목적         → 항목 꼬리표(작은 제목)
  FIG 캡션 :: 힌트 [:: 높이cm]
  |a|b|c|           → 표(첫 줄 = 머리)
  1. …              → 번호 단계
  - …               → 글머리표
  !주의 … / !팁 … / !참고 …  → 상자
  빈 줄 = 문단 구분
인라인: **굵게** · `코드/명령` · [단추]
"""
import re, base64, html as H, sys, os

OUT_HTML = sys.argv[1]
OUT_LIST = sys.argv[2]
LOGO = r"C:\Users\19990\Desktop\AI\civil3d-grading\src\DH.Grading.Civil\Resources\DohwaLogo.png"
VERSION = "v117.23.0"

from content import CHAPTERS, APPENDIX

figs = []  # (num, caption, hint, chapter title)


def inline(s):
    codes = []

    def keep(m):
        codes.append(m.group(1))
        return "\x00%d\x00" % (len(codes) - 1)
    s = re.sub(r"`([^`]+)`", keep, s)
    s = H.escape(s, quote=False)
    s = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", s)
    s = re.sub(r"\[([^\[\]\n]{1,40})\]", r'<span class="btn">\1</span>', s)
    s = re.sub(r"\x00(\d+)\x00", lambda m: "<code>%s</code>" % H.escape(codes[int(m.group(1))], quote=False), s)
    return s


def render(body, chno, chtitle, toc):
    out = []
    lines = body.strip("\n").split("\n")
    i = 0
    fign = 0
    para = []

    def flush():
        if para:
            out.append("<p>%s</p>" % inline(" ".join(para)))
            para.clear()
    while i < len(lines):
        ln = lines[i].rstrip()
        st = ln.strip()
        if not st:
            flush(); i += 1; continue
        if st.startswith("<"):
            flush(); out.append(st)
        elif st.startswith("## "):
            flush()
            t = st[3:].strip()
            key = "s%s_%d" % (chno, len(toc))
            toc.append((2, key, t))
            out.append('<h2 data-key="%s">%s</h2>' % (key, inline(t)))
        elif st.startswith("### "):
            flush(); out.append("<h3>%s</h3>" % inline(st[4:]))
        elif st.startswith("#### "):
            flush(); out.append('<h4>%s</h4>' % inline(st[5:]))
        elif st.startswith("FIG "):
            flush()
            parts = [p.strip() for p in st[4:].split("::")]
            cap, hint = parts[0], parts[1] if len(parts) > 1 else ""
            hcm = parts[2] if len(parts) > 2 else "8"
            fign += 1
            num = "%s-%d" % (chno, fign)
            figs.append((num, cap, hint, chtitle, hcm))
            out.append('<figure class="fig"><div class="frame" style="height:%scm"><div class="ph">삽도 자리 · 그림 %s<br><span>%s</span></div></div>'
                       '<figcaption>그림 %s. %s</figcaption></figure>' % (hcm, num, inline(hint), num, inline(cap)))
        elif st.startswith("|"):
            flush()
            rows = []
            while i < len(lines) and lines[i].strip().startswith("|"):
                rows.append([c.strip() for c in lines[i].strip().strip("|").split("|")])
                i += 1
            t = ['<table>', '<thead><tr>' + "".join("<th>%s</th>" % inline(c) for c in rows[0]) + '</tr></thead><tbody>']
            for r in rows[1:]:
                t.append("<tr>" + "".join("<td>%s</td>" % inline(c) for c in r) + "</tr>")
            t.append("</tbody></table>")
            out.append("".join(t))
            continue
        elif re.match(r"^\d+\. ", st):
            flush()
            items = []
            while i < len(lines) and re.match(r"^\s*\d+\. ", lines[i]):
                txt = re.sub(r"^\s*\d+\. ", "", lines[i].strip())
                i += 1
                subs = []
                while i < len(lines) and re.match(r"^\s{2,}- ", lines[i]):
                    subs.append(lines[i].strip()[2:]); i += 1
                li = inline(txt)
                if subs:
                    li += "<ul>" + "".join("<li>%s</li>" % inline(s) for s in subs) + "</ul>"
                items.append("<li>%s</li>" % li)
            out.append('<ol class="steps">%s</ol>' % "".join(items))
            continue
        elif st.startswith("- "):
            flush()
            items = []
            while i < len(lines) and lines[i].strip().startswith("- "):
                items.append("<li>%s</li>" % inline(lines[i].strip()[2:])); i += 1
            out.append("<ul>%s</ul>" % "".join(items))
            continue
        elif re.match(r"^!(주의|팁|참고) ", st):
            flush()
            kind, txt = st[1:].split(" ", 1)
            cls = {"주의": "warn", "팁": "tip", "참고": "note"}[kind]
            out.append('<div class="box %s"><b class="tag">%s</b> %s</div>' % (cls, kind, inline(txt)))
        else:
            para.append(st)
        i += 1
    flush()
    return "\n".join(out)


CSS = r"""
@page { size: A4; margin: 18mm 17mm 20mm 17mm;
  @bottom-center { content: counter(page); font: 9pt 'Malgun Gothic'; color: #555; }
  @bottom-right { content: "DH Grading for Civil 3D 사용자 매뉴얼 " "v117.23.0"; font: 7.5pt 'Malgun Gothic'; color: #999; } }
@page cover { margin: 0; @bottom-center { content: none; } @bottom-right { content: none; } }
* { box-sizing: border-box; }
html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
body { font-family: 'Malgun Gothic', '맑은 고딕', sans-serif; font-size: 10pt; line-height: 1.62; color: #1d232b; margin: 0; }
.cover { page: cover; height: 297mm; width: 210mm; position: relative; overflow: hidden; background: #fff; }
.cover .band { position: absolute; left: 0; top: 0; width: 100%; height: 118mm; background: linear-gradient(135deg, #0f3d6e 0%, #1c6bb0 100%); }
.cover .band::after { content: ""; position: absolute; left: 0; right: 0; bottom: 0; height: 6mm; background: #f08a24; }
.cover .t1 { position: absolute; left: 22mm; top: 40mm; color: #cfe3f7; font-size: 14pt; letter-spacing: .5pt; }
.cover .t2 { position: absolute; left: 22mm; top: 52mm; color: #fff; font-size: 34pt; font-weight: 700; line-height: 1.15; }
.cover .t3 { position: absolute; left: 22mm; top: 140mm; font-size: 24pt; font-weight: 700; color: #0f3d6e; }
.cover .t4 { position: absolute; left: 22mm; top: 156mm; font-size: 11pt; color: #444; width: 165mm; line-height: 1.7; }
.cover .meta { position: absolute; left: 22mm; top: 205mm; font-size: 11pt; color: #222; }
.cover .meta td { padding: 2mm 8mm 2mm 0; border: none; background: none; }
.cover .meta td:first-child { color: #777; }
.cover .logo { position: absolute; left: 22mm; bottom: 22mm; height: 13mm; }
.cover .org { position: absolute; right: 22mm; bottom: 24mm; font-size: 10pt; color: #666; text-align: right; }
.toc { break-before: page; }
.toc h1 { border: none; }
.toc .row { display: flex; align-items: baseline; font-size: 10pt; margin: 0; }
.toc .row.l1 { font-weight: 700; font-size: 11pt; margin-top: 3.2mm; color: #0f3d6e; }
.toc .row.l2 { padding-left: 7mm; color: #333; }
.toc .row .t { white-space: nowrap; }
.toc .row .dots { flex: 1; border-bottom: 1px dotted #aaa; margin: 0 2mm; transform: translateY(-3px); }
.toc .row .pg { min-width: 8mm; text-align: right; }
.mark { color: #fff; font-size: 1pt; }
section.ch { break-before: page; }
h1 { font-size: 21pt; color: #0f3d6e; margin: 0 0 6mm; padding: 0 0 3mm; border-bottom: 3px solid #f08a24; }
h1 .no { display: block; font-size: 11pt; color: #f08a24; letter-spacing: 1pt; font-weight: 700; margin-bottom: 1mm; }
h2 { font-size: 13.5pt; color: #0f3d6e; margin: 8mm 0 2.5mm; padding-left: 3mm; border-left: 5px solid #1c6bb0; break-after: avoid; }
h3 { font-size: 11pt; color: #1c4f86; margin: 5mm 0 1.5mm; break-after: avoid; }
h4 { display: inline-block; font-size: 9pt; color: #fff; background: #4a6f96; padding: 0.3mm 2.5mm; border-radius: 2mm; margin: 3.5mm 0 1mm; break-after: avoid; }
p { margin: 1.5mm 0 2mm; }
ul, ol { margin: 1mm 0 2.5mm; padding-left: 6mm; }
li { margin: 0.6mm 0; }
ol.steps { counter-reset: st; list-style: none; padding-left: 0; }
ol.steps > li { counter-increment: st; position: relative; padding-left: 8.5mm; margin: 1.3mm 0; }
ol.steps > li::before { content: counter(st); position: absolute; left: 0; top: 0.4mm; width: 5.6mm; height: 5.6mm; border-radius: 50%; background: #1c6bb0; color: #fff; font-size: 8.5pt; font-weight: 700; text-align: center; line-height: 5.6mm; }
ol.steps ul { margin-top: 0.5mm; }
table { border-collapse: collapse; width: 100%; margin: 2mm 0 4mm; font-size: 9pt; break-inside: auto; }
thead { display: table-header-group; }
tr { break-inside: avoid; }
th { white-space: nowrap; background: #e6eef7; color: #0f3d6e; text-align: left; font-weight: 700; }
th, td { border: 1px solid #c3cfdc; padding: 1.3mm 2mm; vertical-align: top; }
tbody tr:nth-child(even) td { background: #f7f9fc; }
code { font-family: Consolas, 'Malgun Gothic', monospace; font-size: 8.8pt; background: #eef1f5; padding: 0 1mm; border-radius: 1mm; color: #8a2d0c; }
.btn { display: inline-block; border: 1px solid #8aa3bd; background: #f3f6fa; border-radius: 1.2mm; padding: 0 1.4mm; font-size: 9pt; line-height: 1.45; color: #12365e; white-space: nowrap; }
.box { border-radius: 1.5mm; padding: 2mm 3mm; margin: 2.5mm 0; break-inside: avoid; font-size: 9.4pt; }
.box .tag { display: inline-block; margin-right: 1.5mm; font-size: 8.5pt; padding: 0 1.6mm; border-radius: 1mm; color: #fff; }
.box.warn { background: #fff4e8; border-left: 4px solid #f08a24; } .box.warn .tag { background: #e0761a; }
.box.tip { background: #eaf6ee; border-left: 4px solid #2f9e5b; } .box.tip .tag { background: #2f9e5b; }
.box.note { background: #eef3fa; border-left: 4px solid #1c6bb0; } .box.note .tag { background: #1c6bb0; }
figure.fig { margin: 3mm 0 4.5mm; break-inside: avoid; }
figure.fig .frame { border: 1.6px dashed #7d93ab; border-radius: 2mm; background: #fafbfd; display: flex; align-items: center; justify-content: center; }
figure.fig .ph { text-align: center; color: #8696a8; font-size: 10pt; font-weight: 700; }
figure.fig .ph span { display: inline-block; margin-top: 1.5mm; font-weight: 400; font-size: 8.8pt; color: #7b8a9b; max-width: 140mm; }
figure.fig figcaption { text-align: center; font-size: 9pt; color: #333; margin-top: 1.5mm; font-weight: 700; }
.lead { font-size: 10.5pt; color: #333; background: #f4f7fb; border-radius: 2mm; padding: 3mm 4mm; margin-bottom: 4mm; }
.flow { display: flex; flex-wrap: wrap; gap: 1.5mm; align-items: center; margin: 3mm 0 4mm; font-size: 9pt; }
.flow span.s { background: #1c6bb0; color: #fff; border-radius: 1.5mm; padding: 1.2mm 2.5mm; font-weight: 700; }
.flow span.a { color: #f08a24; font-weight: 700; }
"""


def build():
    toc = []
    secs = []
    for n, (title, body) in enumerate(CHAPTERS, 1):
        key = "c%d" % n
        toc.append((1, key, "제%d장 %s" % (n, title)))
        content = render(body, n, title, toc)
        secs.append('<section class="ch"><h1 data-key="%s"><span class="no">CHAPTER %d</span>제%d장 %s</h1>\n%s\n</section>' % (key, n, n, H.escape(title), content))
    for letter, (title, body) in zip("AB", APPENDIX):
        key = "ap" + letter
        toc.append((1, key, "부록 %s %s" % (letter, title)))
        content = render(body, letter, title, toc)
        secs.append('<section class="ch"><h1 data-key="%s"><span class="no">APPENDIX %s</span>부록 %s %s</h1>\n%s\n</section>' % (key, letter, letter, H.escape(title), content))

    logo = base64.b64encode(open(LOGO, "rb").read()).decode()
    cover = f"""<div class="cover"><div class="band"></div>
<div class="t1">DOHWA Engineering · Civil 3D 2026 Add-in</div>
<div class="t2">DH Grading<br>for Civil 3D</div>
<div class="t3">사용자 매뉴얼</div>
<div class="t4">원지반 작성 · 계획부지 정지 · 옹벽 · 구조물 터파기 · 진입도로 · 측구 ·<br>종단도/횡단도 · 도면생성 · 토공 수량산출서 · 3D 보기</div>
<table class="meta"><tr><td>버전</td><td><b>{VERSION}</b></td></tr><tr><td>발행</td><td>2026-11</td></tr><tr><td>대상</td><td>Autodesk Civil 3D 2026 (Windows 64비트)</td></tr><tr><td>리본 탭</td><td>DH 정지</td></tr></table>
<img class="logo" src="data:image/png;base64,{logo}">
<div class="org">DOHWA<br>도화엔지니어링</div></div>"""
    rows = []
    for lvl, key, t in toc:
        rows.append('<div class="row l%d"><span class="t">%s</span><span class="dots"></span><span class="pg" data-key="%s"></span></div>' % (lvl, inline(t), key))
    tochtml = '<section class="toc"><h1>목차</h1>%s<div class="mark">TOCENDMARK</div></section>' % "\n".join(rows)

    doc = f"""<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><title>DH Grading 사용자 매뉴얼 {VERSION}</title>
<style>{CSS}</style></head><body>
{cover}
{tochtml}
{"".join(secs)}
</body></html>"""
    # h2 도 build.py 가 찾을 수 있게 data-key 를 붙였다(h1/h2 모두)
    open(OUT_HTML, "w", encoding="utf-8").write(doc)

    lst = ["# DH Grading 사용자 매뉴얼 %s — 삽도 목록" % VERSION, "",
           "매뉴얼 PDF의 빈 그림 틀에 넣을 화면입니다. 찍은 뒤 `그림번호.png`(예: `4-3.png`)로 저장하면 나중에 틀에 넣기 쉽습니다.",
           "틀 높이는 기본 8 cm(가로는 본문 폭 약 17.6 cm)입니다 — 화면 비율이 맞지 않으면 여백을 두고 잘라 주세요.", "",
           "| ☐ | 그림 | 장 | 무엇을 찍나 | 어떤 화면·상태로 | 틀 높이 |", "|---|---|---|---|---|---|"]
    for num, cap, hint, ch, hcm in figs:
        lst.append("| ☐ | %s | %s | %s | %s | %s cm |" % (num, ch, cap.replace("|", "/"), hint.replace("|", "/"), hcm))
    lst += ["", "총 %d개" % len(figs)]
    open(OUT_LIST, "w", encoding="utf-8").write("\n".join(lst) + "\n")
    print("figures", len(figs), "toc", len(toc))


build()
