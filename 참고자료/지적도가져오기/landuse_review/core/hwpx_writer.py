"""순수 Python HWPX(OWPML) 렌더러 — 한컴오피스/COM 불필요(lxml only).

`gis_cn` 플러그인의 검증된 hwpx 생성 엔진(2026-06-04 한글 실개봉 확인)을 **도메인
무관 제네릭 엔진**으로 차용했다. CN(유출곡선지수) 전용 로직(다단계·세로병합·표분할)은
제거하고, 민원검토(편입검토)에 필요한 핵심만 남겼다:

  · `_Doc`               : hwpx(zip) 로드 → section0.xml lxml 파싱 → 누름틀/표 처리
  · `fill_field`         : 누름틀(CLICK_HERE) 사이에 텍스트 주입
  · `render_dynamic_table`: 프로토타입 1행 deepcopy × N → id 재배정 → 값 주입
  · `_validate`          : 출하 전 자체검증(rowAddr 연속·그리드 타일링·field 짝) — 한글 크래시 예방
  · `replace_first_image`: 삽도 PNG 삽입(net-new) — 템플릿의 플레이스홀더 이미지 바이트 교체

삽도 PNG 삽입 방식(무설치/순수 Python 결정):
  빈 양식 제작 시 설계자가 한글에서 삽도 위치에 **더미 PNG 이미지 1개**를 넣어 두면,
  한글이 manifest(content.hpf)·header.xml·section0 의 `<hp:pic>`/binItem 을 모두 올바르게
  등록한다. 본 엔진은 그 등록 구조를 건드리지 않고 `BinData/` 의 이미지 **바이트만 교체**해
  버전·구조에 견고하게 삽도를 갈아끼운다(from-scratch `<hp:pic>` 주입의 크래시 위험 회피).

한글 크래시 회피 규칙(gis_cn PoC 규명):
  · 행 재구성 후 모든 cellAddr rowAddr 를 위치순 0..rowCnt-1 재번호 + rowCnt 갱신
  · 복제 행 fieldBegin id 유니크 재배정(짝 fieldEnd beginIDRef 동기화)
  · zip 재패킹 시 mimetype 을 STORED(비압축)로 먼저 기록
"""
from __future__ import annotations

import logging
import zipfile
from copy import deepcopy
from pathlib import Path
from typing import Optional

logger = logging.getLogger(__name__)

SECTION = "Contents/section0.xml"

# 원본 보고서 쪽 여백 (HWPUNIT = 1/7200 inch) — 차용 기본값
ORIG_MARGIN = {"top": "4252", "bottom": "4252", "left": "4252", "right": "4252",
               "header": "3543", "footer": "3543", "gutter": "0"}

_IMAGE_EXTS = (".png", ".jpg", ".jpeg", ".bmp", ".gif")


class HwpxRenderError(RuntimeError):
    """HWPX 렌더링 실패 (lxml 미설치, 템플릿 오류, 자체검증 실패 포함)."""


def _import_lxml():
    try:
        from lxml import etree  # type: ignore
    except ImportError as e:
        raise HwpxRenderError(
            "lxml 이 설치되어 있지 않습니다. QGIS 번들 Python 에 lxml 이 필요합니다. "
            "한글 보고서 출력에 한컴오피스는 불필요합니다."
        ) from e
    return etree


# ─────────────────────────────────────────────────────────────────────────────
# 저수준 OWPML 헬퍼
# ─────────────────────────────────────────────────────────────────────────────

class _Doc:
    """로드된 hwpx 1개분 (zip 원본 + section0 lxml 트리)."""

    def __init__(self, etree, template: Path):
        self.etree = etree
        zin = zipfile.ZipFile(template)
        self.names = zin.namelist()
        self.raw = {n: zin.read(n) for n in self.names}
        zin.close()
        if SECTION not in self.raw:
            raise HwpxRenderError(f"템플릿에 {SECTION} 없음: {template}")
        self.root = etree.fromstring(self.raw[SECTION], etree.XMLParser(huge_tree=True))
        self.ns = self.root.nsmap.get("hp")
        if not self.ns:
            raise HwpxRenderError("템플릿 네임스페이스(hp) 누락 — OWPML 형식 아님")
        self._uid = 900_000_000
        self._bsmap = None

    def hp(self, tag: str) -> str:
        return f"{{{self.ns}}}{tag}"

    def fresh_id(self) -> str:
        self._uid += 1
        return str(self._uid)

    def tables(self):
        return list(self.root.iter(self.hp("tbl")))

    def find_table_with_field(self, prefix: str):
        """누름틀 name 이 `prefix.` 로 시작하는 첫 표."""
        for tbl in self.root.iter(self.hp("tbl")):
            if any((fb.get("name") or "").startswith(prefix + ".")
                   for fb in tbl.iter(self.hp("fieldBegin"))):
                return tbl
        return None

    def set_margins(self, margins: dict) -> int:
        n = 0
        for mg in self.root.iter(self.hp("margin")):
            if mg.getparent().tag == self.hp("pagePr"):
                for k, v in margins.items():
                    mg.set(k, v)
                n += 1
        return n

    def border_solid_map(self) -> "dict[str, str]":
        """header.xml borderFill 중 윗변 이중선(DOUBLE_SLIM)을 동일 속성 실선(SOLID) id 로 매핑.
        데이터 행간 중복 이중선 제거용. 결과 캐시."""
        if self._bsmap is not None:
            return self._bsmap
        hdr = self.raw.get("Contents/header.xml")
        if not hdr:
            self._bsmap = {}
            return self._bsmap
        root = self.etree.fromstring(hdr, self.etree.XMLParser(huge_tree=True))
        ql = lambda e: self.etree.QName(e).localname
        defs: dict[str, dict] = {}
        for bf in root.iter():
            if ql(bf) != "borderFill":
                continue
            d = {}
            for ch in bf:
                ln = ql(ch)
                if ln in ("leftBorder", "rightBorder", "topBorder", "bottomBorder"):
                    d[ln] = (ch.get("type"), ch.get("width"))
                elif ln == "fillBrush":
                    d["fill"] = self.etree.tostring(ch)
            defs[bf.get("id")] = d
        kf = lambda d: (d.get("leftBorder"), d.get("rightBorder"), d.get("bottomBorder"), d.get("fill"))
        solid_idx: dict[tuple, str] = {}
        for bid, d in defs.items():
            t = d.get("topBorder")
            if t and t[0] == "SOLID":
                solid_idx.setdefault(kf(d), bid)
        mapping: dict[str, str] = {}
        for bid, d in defs.items():
            t = d.get("topBorder")
            if t and t[0] == "DOUBLE_SLIM" and kf(d) in solid_idx:
                mapping[bid] = solid_idx[kf(d)]
        self._bsmap = mapping
        return mapping

    def fill_field(self, scope, name: str, value: str) -> bool:
        """scope(표/행/문서) 안에서 CLICK_HERE 누름틀(name) 사이에 <hp:t>value</hp:t> 주입."""
        for fb in scope.iter(self.hp("fieldBegin")):
            if fb.get("name") != name:
                continue
            run = fb.getparent().getparent()      # ctrl -> run
            end_ctrl = None
            for ctrl in run.findall(self.hp("ctrl")):
                fe = ctrl.find(self.hp("fieldEnd"))
                if fe is not None and fe.get("beginIDRef") == fb.get("id"):
                    end_ctrl = ctrl
                    break
            for tt in run.findall(self.hp("t")):
                run.remove(tt)
            t = self.etree.SubElement(run, self.hp("t"))
            t.text = value
            if end_ctrl is not None:
                run.remove(t)
                end_ctrl.addprevious(t)
            return True
        return False

    def reassign_field_ids(self, tr) -> None:
        for fb in tr.iter(self.hp("fieldBegin")):
            old = fb.get("id")
            nid, nfid = self.fresh_id(), self.fresh_id()
            fb.set("id", nid)
            fb.set("fieldid", nfid)
            run = fb.getparent().getparent()
            for fe in run.iter(self.hp("fieldEnd")):
                if fe.get("beginIDRef") == old:
                    fe.set("beginIDRef", nid)
                    fe.set("fieldid", nfid)

    def replace_first_image(self, image_path) -> bool:
        """템플릿 삽도 플레이스홀더(BinData/*.png)의 바이트를 새 PNG 로 교체.

        한글이 양식 제작 시 등록한 manifest/pic/binItem 구조를 그대로 두고 바이트만
        바꾼다(가장 견고한 무설치 이미지 삽입). 템플릿에 이미지가 없으면 False.

        타깃 선정: **삽도 슬롯은 PNG** 로 만들어 두므로 BinData 의 첫 `.png` 를 교체한다.
        레터헤드/로고 등 장식 이미지는 `.jpg`/`.jpeg` 로 두어 보존한다(과거엔 정렬상
        첫 이미지를 교체해 레터헤드를 덮어쓰는 잠복 버그가 있었음 — pyeonib 양식 대응).
        PNG 가 하나도 없으면(구양식 호환) 정렬상 첫 이미지로 폴백한다.
        """
        image_path = Path(image_path)
        if not image_path.exists():
            return False
        imgs = sorted(
            n for n in self.names
            if n.startswith("BinData/") and n.lower().endswith(_IMAGE_EXTS)
        )
        if not imgs:
            return False
        pngs = [n for n in imgs if n.lower().endswith(".png")]
        target = pngs[0] if pngs else imgs[0]
        self.raw[target] = image_path.read_bytes()
        return True

    def serialize(self) -> None:
        self.raw[SECTION] = self.etree.tostring(
            self.root, xml_declaration=True, encoding="UTF-8", standalone=True)

    def save(self, out: Path) -> None:
        self.serialize()
        out = Path(out)
        if out.exists():
            out.unlink()
        with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zout:
            zi = zipfile.ZipInfo("mimetype")
            zi.compress_type = zipfile.ZIP_STORED
            zout.writestr(zi, self.raw.get("mimetype", b"application/hwp+zip"))
            for n in self.names:
                if n != "mimetype":
                    zout.writestr(n, self.raw[n])


# ─────────────────────────────────────────────────────────────────────────────
# 동적 행 생성 (프로토타입 deepcopy × N)
# ─────────────────────────────────────────────────────────────────────────────

def render_dynamic_table(doc: _Doc, table, field_prefix: str,
                         data_rows: "list[dict[str, str]]") -> None:
    """표의 데이터행을 data_rows(필드명→값 dict) 개수에 맞춰 동적 생성.

    헤더(첫 데이터행 이전)는 보존, 기존 데이터영역은 전부 교체. 각 행은 프로토타입을
    deepcopy → 필드 id 재배정 → 값 주입. 후처리로 전체 rowAddr 재번호 + rowCnt 갱신.
    """
    hp = doc.hp
    trs = table.findall(hp("tr"))

    def is_data(tr):
        return any((fb.get("name") or "").startswith(field_prefix + ".")
                   for fb in tr.iter(hp("fieldBegin")))

    first_data = next((i for i, tr in enumerate(trs) if is_data(tr)), None)
    if first_data is None:
        raise HwpxRenderError(f"템플릿 표에 '{field_prefix}.*' 프로토타입 행 없음")
    header_rows = trs[:first_data]
    prototype = deepcopy(trs[first_data])

    for tr in trs[first_data:]:      # 기존 데이터영역(끝 빈 행 포함) 제거
        table.remove(tr)

    prev = header_rows[-1] if header_rows else None
    for rowdata in (data_rows or []):
        nr = deepcopy(prototype)
        doc.reassign_field_ids(nr)
        for name, value in rowdata.items():
            doc.fill_field(nr, name, value)
        if prev is not None:
            prev.addnext(nr)
        else:
            table.append(nr)
        prev = nr

    all_rows = table.findall(hp("tr"))
    for pos, tr in enumerate(all_rows):
        for ca in tr.iter(hp("cellAddr")):
            ca.set("rowAddr", str(pos))
    table.set("rowCnt", str(len(all_rows)))

    _fix_row_separators(doc, table, len(data_rows or []))


def _fix_row_separators(doc: _Doc, table, n_data: int) -> None:
    """데이터행 2번째부터 윗변 이중선(DOUBLE_SLIM) → 가는 실선(SOLID) 교체(원본 선스타일 재현)."""
    if n_data <= 1:
        return
    bmap = doc.border_solid_map()
    if not bmap:
        return
    hp = doc.hp
    all_trs = table.findall(hp("tr"))
    data_trs = all_trs[len(all_trs) - n_data:]
    for tr in data_trs[1:]:
        for tc in tr.findall(hp("tc")):
            bid = tc.get("borderFillIDRef")
            if bid in bmap:
                tc.set("borderFillIDRef", bmap[bid])


# ─────────────────────────────────────────────────────────────────────────────
# 자체검증
# ─────────────────────────────────────────────────────────────────────────────

def _validate(doc: _Doc) -> None:
    hp = doc.hp
    problems: list[str] = []
    for ti, tbl in enumerate(doc.tables()):
        rc, cc = int(tbl.get("rowCnt")), int(tbl.get("colCnt"))
        grid = {}
        over = False
        for tr in tbl.findall(hp("tr")):
            for tc in tr.findall(hp("tc")):
                ca, sp = tc.find(hp("cellAddr")), tc.find(hp("cellSpan"))
                r, c = int(ca.get("rowAddr")), int(ca.get("colAddr"))
                rs = int(sp.get("rowSpan")) if sp is not None else 1
                cs = int(sp.get("colSpan")) if sp is not None else 1
                if r >= rc or c >= cc:
                    problems.append(f"표{ti}: cellAddr({r},{c}) 범위초과 {rc}x{cc}")
                    over = True
                for dr in range(rs):
                    for dc in range(cs):
                        key = (r + dr, c + dc)
                        if key in grid:
                            problems.append(f"표{ti}: 셀 중복 {key}")
                        grid[key] = 1
        if not over:
            missing = [(r, c) for r in range(rc) for c in range(cc) if (r, c) not in grid]
            if missing:
                problems.append(f"표{ti}: 그리드 미충족 {len(missing)}칸 예{missing[:3]}")
    begins = {}
    for fb in doc.root.iter(hp("fieldBegin")):
        begins[fb.get("id")] = begins.get(fb.get("id"), 0) + 1
    for fid, cnt in begins.items():
        if cnt > 1:
            problems.append(f"fieldBegin id 중복: {fid} x{cnt}")
    for fe in doc.root.iter(hp("fieldEnd")):
        if fe.get("beginIDRef") not in begins:
            problems.append(f"fieldEnd 고아 참조: {fe.get('beginIDRef')}")
    if problems:
        raise HwpxRenderError("자체검증 실패(한글 크래시 위험): " + "; ".join(problems[:8]))


# ─────────────────────────────────────────────────────────────────────────────
# 메인 엔트리 — 메타 누름틀 + 편입면적표 + 삽도
# ─────────────────────────────────────────────────────────────────────────────

def render(meta: dict, table_prefix: str, data_rows: "list[dict]",
           template, out, *, inset_png: Optional[str] = None,
           apply_orig_margin: bool = True) -> Path:
    """빈 양식 hwpx 템플릿을 채워 .hwpx 저장.

    Args:
        meta: 누름틀 dict {"meta.project_name": ..., ...}
        table_prefix: 동적 표 셀필드 prefix (예: "pyeonib")
        data_rows: 표 데이터행 dict 리스트 ({"pyeonib.jibun": ..., ...})
        template/out: 빈 양식 경로 / 출력 경로
        inset_png: 삽도 PNG 경로(템플릿 플레이스홀더 이미지 교체). None 이면 생략.
    Raises:
        HwpxRenderError: lxml 미설치, 템플릿 오류, 자체검증 실패.
    """
    etree = _import_lxml()
    template, out = Path(template), Path(out)
    if not template.exists():
        raise HwpxRenderError(f"템플릿을 찾을 수 없습니다: {template}")

    doc = _Doc(etree, template)
    if apply_orig_margin:
        doc.set_margins(ORIG_MARGIN)

    # 메타 누름틀(템플릿에 있을 때만 graceful)
    filled = sum(int(doc.fill_field(doc.root, k, v)) for k, v in (meta or {}).items())

    # 편입면적표 동적 행
    tbl = doc.find_table_with_field(table_prefix)
    if tbl is None:
        raise HwpxRenderError(f"템플릿에 '{table_prefix}.*' 표가 없습니다.")
    render_dynamic_table(doc, tbl, table_prefix, data_rows)

    # 삽도 PNG (플레이스홀더 교체)
    img_ok = False
    if inset_png:
        img_ok = doc.replace_first_image(inset_png)

    _validate(doc)
    doc.save(out)
    logger.info("HWPX 저장: %s (행 %d, meta %d필드, 삽도 %s)",
                out, len(data_rows or []), filled, "교체" if img_ok else "없음")
    return out
