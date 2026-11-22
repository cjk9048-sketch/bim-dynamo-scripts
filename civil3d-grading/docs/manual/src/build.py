"""매뉴얼 빌드: HTML -> PDF(Edge headless) -> 목차 쪽번호 채우기 -> 다시 PDF -> 쪽번호 확인.
목차 항목은 <a class="toc-item" data-key="K">…<span class="pg" data-key="K"></span></a>
본문 제목에는 보이지 않는 표식 «⟦K⟧» 대신, 제목 텍스트 자체로 찾는다(data-find).
"""
import re, subprocess, sys, os, shutil
import pymupdf

SRC = sys.argv[1]           # source html (template with empty page numbers)
OUT_PDF = sys.argv[2]
EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
TMP = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_build.html")


def render(html_path, pdf_path):
    if os.path.exists(pdf_path):
        os.remove(pdf_path)
    url = "file:///" + os.path.abspath(html_path).replace("\\", "/")
    subprocess.run([EDGE, "--headless", "--disable-gpu", "--no-pdf-header-footer",
                    "--user-data-dir=" + os.path.join(os.path.dirname(TMP), "_edgeprof"),
                    "--print-to-pdf=" + os.path.abspath(pdf_path), url],
                   check=True, timeout=180, capture_output=True)


def norm(s):
    return re.sub(r"\s+", "", s)


html = open(SRC, encoding="utf-8").read()
# 찾을 제목: <hN ... data-key="K">text</hN>
heads = re.findall(r'<h([12])[^>]*data-key="([^"]+)"[^>]*>(.*?)</h\1>', html, re.S)
keys = [(k, norm(re.sub(r"<[^>]+>", "", t))) for _, k, t in heads]

pages = {}
for it in range(3):
    filled = html
    for k, p in pages.items():
        filled = re.sub(r'(<span class="pg" data-key="%s">)[^<]*(</span>)' % re.escape(k), r"\g<1>%d\g<2>" % p, filled)
    open(TMP, "w", encoding="utf-8").write(filled)
    render(TMP, OUT_PDF)
    doc = pymupdf.open(OUT_PDF)
    ptext = [norm(pg.get_text()) for pg in doc]
    new = {}
    start = 0
    for i, t in enumerate(ptext):
        if "TOCENDMARK" in t:
            start = i + 1
    for k, needle in keys:
        for i in range(start, len(ptext)):
            if needle[:40] in ptext[i]:
                new[k] = i + 1
                start = i
                break
        else:
            print("NOT FOUND", k, needle[:40])
    n = doc.page_count
    doc.close()
    if new == pages:
        break
    pages = new
print("pages", n)
print("filled html at", TMP)
