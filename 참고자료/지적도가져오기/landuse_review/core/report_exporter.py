# -*- coding: utf-8 -*-
"""M5b: HWPX 용지편입검토서 초안 출력 — **하이브리드**.

기획서 v2 §10 결정(하이브리드):
  · (A) 빈 양식 템플릿(`resources/templates/pyeonib_report.hwpx`)이 있으면 → 사내 `gis_cn`
        검증 hwpx 엔진(`hwpx_writer`/`hwpx_report`)으로 채움. 도로부 실제 양식 정합·삽도 PNG
        플레이스홀더 교체. (G1 도로부 양식 입수 시 이 템플릿을 교체)
  · (B) 템플릿이 아직 없으면 → 동봉 python-hwpx 로 프로그래매틱 초안 생성(지금 즉시 동작).

둘 다 한컴오피스 불필요(순수 Python). 설계자가 한글에서 확인 후 결재(자동 출력 ≠ 자동 결재).
소유자 "이름"은 수동 입력값(owner_names)만 사용 — 자동 수집 데이터엔 이름이 없다(v2 §6.2).
"""
import os
from datetime import datetime
from typing import List, Dict, Optional

from . import hwpx_report
from . import hwpx_image
from . import owner_collector


def _templates_dir() -> str:
    return os.path.join(os.path.dirname(os.path.dirname(__file__)), "resources", "templates")


def template_path() -> str:
    return os.path.join(_templates_dir(), "pyeonib_report.hwpx")


def has_template() -> bool:
    return os.path.isfile(template_path())


def export(out_path: str, *, parcels: List[Dict], intersections: List[Dict],
           owner_names: Optional[Dict[str, str]] = None,
           meta: Optional[Dict[str, str]] = None,
           inset_png: Optional[str] = None, mask: bool = False) -> str:
    """용지편입검토서 초안 .hwpx 를 생성한다.

    Args:
        out_path: 저장 경로(.hwpx)
        parcels: parcel_lookup.lookup() 결과(대상 필지; is_neighbor 제외)
        intersections: area_calculator 결과(편입 산출)
        owner_names: {PNU: 소유자명} — 설계자가 '선택필지' 속성테이블에 직접 입력한 값
        meta: {project_name, site_name, author, organization, analysis_date}
        inset_png: 삽도 PNG 경로(있으면 템플릿 플레이스홀더 교체 / 프로그래매틱은 이미지 삽입)
        mask: True 면 소유자명 마스킹(결재 첨부 직전 토글) — default OFF
    Returns:
        out_path
    """
    if not out_path:
        raise ValueError("out_path 가 비었습니다.")
    os.makedirs(os.path.dirname(out_path) or ".", exist_ok=True)

    owner_names = dict(owner_names or {})
    if mask:
        owner_names = {k: owner_collector.mask_name(v) for k, v in owner_names.items()}

    meta = dict(meta or {})
    meta.setdefault("organization", "도화엔지니어링 기술개발연구원")
    meta.setdefault("analysis_date", datetime.now().strftime("%Y-%m-%d"))
    # 과업명(제목) 미입력 시 공백 제목 방지 — 양식 템플릿의 meta.project_name 누름틀에 매핑.
    if not (meta.get("project_name") or "").strip():
        meta["project_name"] = "용지 편입 검토서"

    rows = hwpx_report.build_rows(parcels, intersections, owner_names)

    if has_template():
        # (A) gis_cn 엔진 — 도로부 양식 템플릿 채움
        return hwpx_report.render_report(
            out_path, template_path(), meta=meta, rows=rows, inset_png=inset_png,
        )
    # (B) 템플릿 미수령 — 동봉 python-hwpx 프로그래매틱 초안
    return _export_programmatic(out_path, rows, meta, inset_png, mask)


# ─────────────────────────────────────────────────────────────────────────
# (B) 프로그래매틱 폴백 — python-hwpx (동봉 _vendor)
# ─────────────────────────────────────────────────────────────────────────

def _export_programmatic(out_path: str, rows, meta: Dict[str, str],
                         inset_png: Optional[str], mask: bool) -> str:
    """빈 양식 템플릿이 없을 때: python-hwpx 로 검토서 초안을 직접 조립."""
    from hwpx import HwpxDocument   # 동봉(_vendor), __init__.py 가 sys.path 부트스트랩

    doc = HwpxDocument.new()

    title = meta.get("project_name") or "용지편입검토서(초안)"
    doc.add_paragraph(f"[ {title} ]")
    site = meta.get("site_name") or ""
    author = meta.get("author")
    if not author:
        try:
            author = os.getlogin()
        except Exception:
            author = ""
    line2 = f"사업지구: {site}    작성자: {author}    작성일: {meta.get('analysis_date')}"
    doc.add_paragraph(line2)
    doc.add_paragraph("")

    # 1. 용지 편입현황 (삽도) — 실제 그림은 저장 후 hwpx_image.embed_inset 가 이 제목 문단
    # 뒤에 <hp:pic> 로 삽입한다(python-hwpx add_image 는 바이트만 등록·본문 미표시).
    doc.add_paragraph("1. 용지 편입현황")
    has_png = bool(inset_png and os.path.exists(inset_png))
    if not has_png:
        doc.add_paragraph("  (삽도 PNG 미생성 — 4단계에서 삽도를 먼저 저장하세요)")
    doc.add_paragraph("")

    # 2. 편입면적표
    doc.add_paragraph("2. 편입면적")
    headers = ["지번", "지목", "소유자", "공부면적(㎡)", "편입면적(㎡)", "편입률"]
    n = max(2, len(rows) + 1)
    t = doc.add_table(n, len(headers))
    for c, h in enumerate(headers):
        t.set_cell_text(0, c, h)
    from .hwpx_report import _fmt_area, _fmt_ratio
    for i, r in enumerate(rows, start=1):
        cells = [
            r.jibun, r.jimok, r.owner or "",
            _fmt_area(r.book_area), _fmt_area(r.enc_area), _fmt_ratio(r.enc_ratio),
        ]
        for c, val in enumerate(cells):
            t.set_cell_text(i, c, str(val))
    if not rows:
        for c in range(len(headers)):
            t.set_cell_text(1, c, "-")
    doc.add_paragraph("")

    # 워터마크/작성자 메타(텍스트) + 결재 안내 (v2 §13)
    mask_note = " · 소유자명 마스킹 ON" if mask else ""
    doc.add_paragraph(
        f"※ 자동 생성 초안 · 작성 {author} · {meta.get('analysis_date')}{mask_note}"
    )
    doc.add_paragraph(
        "⚠ 본 문서는 민원검토 도구가 만든 초안입니다. 설계자가 한글에서 내용·삽도를 "
        "확인·수정한 후 결재하십시오. (도로부 공식 양식 입수 시 양식 정합 출력으로 전환)"
    )

    if hasattr(doc, "save_to_path"):
        doc.save_to_path(out_path)
    else:
        doc.save(out_path)
    # 삽도 PNG 를 본문 "1. 용지 편입현황" 제목 뒤에 그림으로 삽입(검증 한컴 <hp:pic> 차용)
    if has_png:
        hwpx_image.embed_inset(out_path, inset_png, anchor_substr="용지 편입현황")
    return out_path
