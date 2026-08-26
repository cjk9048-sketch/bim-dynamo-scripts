# -*- coding: utf-8 -*-
"""HWPX 본문에 삽도 PNG를 그림(<hp:pic>)으로 삽입 — post-process.

동봉 python-hwpx 의 `add_image()` 는 이미지 바이트를 zip·manifest 에 **등록만** 하고
본문에 그림 요소(`<hp:pic>`)를 넣지 않는다 → 한글에서 보이지 않는다. 이 모듈이 생성된
.hwpx(프로그래매틱 경로 B 산출물)를 열어 다음을 수행한다:

  1) `BinData/imageN.png` 로 PNG 바이트 추가
  2) `Contents/content.hpf` 의 `<opf:manifest>` 에 `<opf:item ... isEmbeded="1"/>` 등록
  3) `Contents/section0.xml` 의 앵커 문단(예: "1. 용지 편입현황") 뒤에 그림 문단 삽입
  4) mimetype STORED 선기록으로 재패킹

`<hp:pic>` 마크업은 **한컴이 직접 만든 검증 샘플**(gis_cn cn_report_sample.hwpx)의 구조를
그대로 차용하되, 변환(scaMatrix/transMatrix)을 항등·클립을 1:1 로 두어 from-scratch 주입의
크래시/미렌더 위험을 최소화한다. 표시 크기는 PNG 픽셀 비율을 보존해 페이지 폭에 맞춘다.

실패해도 보고서 생성은 막지 않는다(이미지만 누락) — 호출 측에서 graceful.
"""
import os
import struct
import zipfile

SECTION = "Contents/section0.xml"
HPF = "Contents/content.hpf"

HP = "http://www.hancom.co.kr/hwpml/2011/paragraph"
HC = "http://www.hancom.co.kr/hwpml/2011/core"

# 쪽 설정을 못 읽을 때의 폴백 표시 폭(HWPUNIT=1/7200in). 보통은 본문 폭을 계산해 쓴다.
_DISPLAY_WIDTH = 42000


def _page_text_box(sec):
    """section0 의 쪽 폭·여백에서 (본문 폭, 본문 높이) HWPUNIT 산출. 못 읽으면 (폴백, None)."""
    hpn = lambda t: f"{{{HP}}}{t}"
    try:
        pp = sec.find(".//" + hpn("pagePr"))
        mg = sec.find(".//" + hpn("margin"))
        pw, ph = int(pp.get("width")), int(pp.get("height"))
        ml, mr = int(mg.get("left")), int(mg.get("right"))
        mt, mb = int(mg.get("top")), int(mg.get("bottom"))
        tw, th = pw - ml - mr, ph - mt - mb
        if tw > 0 and th > 0:
            return tw, th
    except Exception:
        pass
    return _DISPLAY_WIDTH, None


def _fit_box(w_px, h_px, text_w, max_h):
    """이미지를 본문 폭에 꽉 채우되, 높이가 본문 높이를 넘으면 비율 보존하며 축소."""
    w = text_w
    if w_px and h_px:
        h = round(w * h_px / w_px)
    else:
        h = min(28800, max_h or 28800)
    if max_h and h > max_h:
        h = max_h
        w = round(h * w_px / h_px) if (w_px and h_px) else text_w
    return max(1, int(w)), max(1, int(h))


def _png_size(path):
    """PNG 픽셀 크기(w,h) — IHDR 직접 파싱(PIL 불필요). 실패 시 (0,0)."""
    try:
        with open(path, "rb") as f:
            head = f.read(24)
        if len(head) >= 24 and head[:8] == b"\x89PNG\r\n\x1a\n" and head[12:16] == b"IHDR":
            w, h = struct.unpack(">II", head[16:24])
            return int(w), int(h)
    except Exception:
        pass
    return 0, 0


def _next_image_id(parts):
    """기존 BinData/image*.png 와 충돌하지 않는 (id, 파일명) 반환."""
    n = 1
    while f"BinData/image{n}.png" in parts:
        n += 1
    return f"image{n}", f"image{n}.png"


def _pic_paragraph_xml(img_id, w, h):
    """검증된 한컴 <hp:pic> 구조(항등 변환·1:1 클립). w/h = 표시 크기(HWPUNIT)."""
    cx, cy = w // 2, h // 2
    # id/instid 는 문서 내 유니크해야 함 — python-hwpx id 는 uuid4 기반 대정수라
    # 아래 고정 대값과 충돌 확률 무시 가능(보고서당 삽도 1개).
    pid, picid, instid = "2142880001", "2142880002", "1069090001"
    return (
        f'<hp:p xmlns:hp="{HP}" xmlns:hc="{HC}" id="{pid}" paraPrIDRef="0" '
        f'styleIDRef="0" pageBreak="0" columnBreak="0" merged="0">'
        f'<hp:run charPrIDRef="0">'
        f'<hp:pic id="{picid}" zOrder="0" numberingType="NONE" textWrap="TOP_AND_BOTTOM" '
        f'textFlow="BOTH_SIDES" lock="0" dropcapstyle="None" href="" groupLevel="0" '
        f'instid="{instid}" reverse="0">'
        f'<hp:offset x="0" y="0"/>'
        f'<hp:orgSz width="{w}" height="{h}"/>'
        f'<hp:curSz width="{w}" height="{h}"/>'
        f'<hp:flip horizontal="0" vertical="0"/>'
        f'<hp:rotationInfo angle="0" centerX="{cx}" centerY="{cy}" rotateimage="1"/>'
        f'<hp:renderingInfo>'
        f'<hc:transMatrix e1="1" e2="0" e3="0" e4="0" e5="1" e6="0"/>'
        f'<hc:scaMatrix e1="1" e2="0" e3="0" e4="0" e5="1" e6="0"/>'
        f'<hc:rotMatrix e1="1" e2="0" e3="0" e4="0" e5="1" e6="0"/>'
        f'</hp:renderingInfo>'
        f'<hc:img binaryItemIDRef="{img_id}" bright="0" contrast="0" effect="REAL_PIC" alpha="0"/>'
        f'<hp:imgRect>'
        f'<hc:pt0 x="0" y="0"/><hc:pt1 x="{w}" y="0"/>'
        f'<hc:pt2 x="{w}" y="{h}"/><hc:pt3 x="0" y="{h}"/>'
        f'</hp:imgRect>'
        f'<hp:imgClip left="0" right="{w}" top="0" bottom="{h}"/>'
        f'<hp:inMargin left="0" right="0" top="0" bottom="0"/>'
        f'<hp:imgDim dimwidth="{w}" dimheight="{h}"/>'
        f'<hp:effects/>'
        f'<hp:sz width="{w}" widthRelTo="ABSOLUTE" height="{h}" heightRelTo="ABSOLUTE" protect="0"/>'
        f'<hp:pos treatAsChar="1" affectLSpacing="0" flowWithText="1" allowOverlap="0" '
        f'holdAnchorAndSO="0" vertRelTo="PARA" horzRelTo="PARA" vertAlign="TOP" '
        f'horzAlign="LEFT" vertOffset="0" horzOffset="0"/>'
        f'<hp:outMargin left="0" right="0" top="0" bottom="0"/>'
        f'</hp:pic><hp:t/></hp:run></hp:p>'
    )


def _repack(out_path, order, parts):
    """mimetype STORED 선기록 후 나머지 DEFLATE 로 재패킹."""
    if os.path.exists(out_path):
        os.remove(out_path)
    with zipfile.ZipFile(out_path, "w", zipfile.ZIP_DEFLATED) as z:
        if "mimetype" in parts:
            zi = zipfile.ZipInfo("mimetype")
            zi.compress_type = zipfile.ZIP_STORED
            z.writestr(zi, parts["mimetype"])
        for n in order:
            if n == "mimetype":
                continue
            z.writestr(n, parts[n])


def embed_inset(hwpx_path, png_path, anchor_substr="용지 편입현황"):
    """생성된 .hwpx 에 삽도 PNG 를 그림으로 삽입. 성공 시 True.

    anchor_substr 텍스트를 포함한 문단 **뒤**에 그림을 넣는다(없으면 본문 끝).
    """
    if not png_path or not os.path.exists(png_path) or not os.path.exists(hwpx_path):
        return False
    try:
        from lxml import etree
    except Exception:
        return False

    w_px, h_px = _png_size(png_path)

    try:
        zin = zipfile.ZipFile(hwpx_path)
        order = list(zin.namelist())
        parts = {n: zin.read(n) for n in order}
        zin.close()
        if SECTION not in parts or HPF not in parts:
            return False
        # 이미 본문에 그림이 있으면(템플릿 플레이스홀더 교체 등) 중복 삽입하지 않는다.
        if b"<hp:pic" in parts[SECTION]:
            return False

        img_id, bin_name = _next_image_id(parts)
        with open(png_path, "rb") as f:
            parts[f"BinData/{bin_name}"] = f.read()
        order.append(f"BinData/{bin_name}")

        # content.hpf 매니페스트 등록
        hpf = parts[HPF].decode("utf-8")
        item = (f'<opf:item id="{img_id}" href="BinData/{bin_name}" '
                f'media-type="image/png" isEmbeded="1"/>')
        if "</opf:manifest>" not in hpf:
            return False
        parts[HPF] = hpf.replace("</opf:manifest>", item + "</opf:manifest>", 1).encode("utf-8")

        # section0 그림 문단 삽입 — 표시 크기는 쪽 여백(본문 폭)에 맞춤
        sec = etree.fromstring(parts[SECTION], etree.XMLParser(huge_tree=True))
        text_w, max_h = _page_text_box(sec)
        disp_w, disp_h = _fit_box(w_px, h_px, text_w, max_h)
        anchor = None
        for p in sec.iter(f"{{{HP}}}p"):
            txt = "".join(t.text or "" for t in p.iter(f"{{{HP}}}t"))
            if anchor_substr in txt:
                anchor = p
        pic_p = etree.fromstring(_pic_paragraph_xml(img_id, disp_w, disp_h).encode("utf-8"))
        if anchor is not None:
            anchor.addnext(pic_p)
        else:
            sec.append(pic_p)
        parts[SECTION] = etree.tostring(sec, xml_declaration=True, encoding="UTF-8", standalone=True)

        _repack(hwpx_path, order, parts)
        return True
    except Exception:
        return False
