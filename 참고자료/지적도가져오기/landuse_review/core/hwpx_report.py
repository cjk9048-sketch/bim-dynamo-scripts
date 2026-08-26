# -*- coding: utf-8 -*-
"""편입검토 도메인 → hwpx 누름틀/표 데이터 변환 (gis_cn 엔진 경로).

`hwpx_writer`(도메인 무관 엔진)에 먹일 메타 누름틀 dict 와 편입면적표 행 dict 를
편입검토 결과(parcels·intersections·소유자명)로부터 만든다.

빈 양식 템플릿 규약(차용_가이드 §4):
  · 누름틀: meta.project_name / meta.site_name / meta.author / meta.organization / meta.analysis_date
  · 편입면적표 셀필드 prefix `pyeonib.`: jibun jimok owner book_area enc_area enc_ratio (1행 — 렌더러가 복제)
  · 삽도 위치: 플레이스홀더 PNG 이미지(설계자가 한글 양식에 1개 삽입)
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import List, Dict, Optional

from . import hwpx_writer
from .parcel_lookup import book_area

TABLE_PREFIX = "pyeonib"


@dataclass
class PyeonibRow:
    """편입면적표 1행."""
    jibun: str                  # 지번 (예: "대전리 645-1")
    jimok: str                  # 지목 (전/답/도 …)
    owner: str                  # 소유자 (수동 입력 — API 미제공, 마스킹 가능)
    book_area: Optional[float]  # 공부상 면적(㎡)
    enc_area: Optional[float]   # 편입면적(㎡) — 미편입이면 None → "-"
    enc_ratio: Optional[float]  # 편입률(%) — 미편입이면 None → "-"


def _fmt_area(v: Optional[float]) -> str:
    if v is None:
        return "-"
    try:
        return f"{float(v):,.1f}"
    except (TypeError, ValueError):
        return "-"


def _fmt_ratio(v: Optional[float]) -> str:
    if v is None:
        return "-"
    try:
        return f"{float(v):.1f}%"
    except (TypeError, ValueError):
        return "-"


def _row_to_cells(row: PyeonibRow) -> Dict[str, str]:
    p = lambda local: f"{TABLE_PREFIX}.{local}"
    return {
        p("jibun"): row.jibun,
        p("jimok"): row.jimok,
        p("owner"): row.owner or "",
        p("book_area"): _fmt_area(row.book_area),
        p("enc_area"): _fmt_area(row.enc_area),
        p("enc_ratio"): _fmt_ratio(row.enc_ratio),
    }


def build_rows(parcels: List[Dict], intersections: List[Dict],
               owner_names: Dict[str, str]) -> List[PyeonibRow]:
    """parcels(대상 필지) + intersections(편입 산출) + owner_names(PNU→수동입력 이름) → 표 행 리스트.

    편입 산출에 있는 필지는 편입면적/편입률을 채우고, 없으면(미편입) "-".
    """
    incl_by_pnu = {r.get("pnu"): r for r in (intersections or [])}
    rows: List[PyeonibRow] = []
    for p in (parcels or []):
        if p.get("is_neighbor"):
            continue  # 인접 보조 시각화는 보고서 제외
        pnu = p.get("pnu")
        incl = incl_by_pnu.get(pnu)
        # 공부면적 = 토지대장 면적 우선(정의는 parcel_lookup.book_area 한 곳).
        book = book_area(p) or (float(incl.get("total_area")) if incl else None)
        if incl and float(incl.get("incl_area") or 0) > 0:
            enc_area = float(incl.get("incl_area"))
            enc_ratio = float(incl.get("incl_ratio") or 0)
        else:
            enc_area = None
            enc_ratio = None
        rows.append(PyeonibRow(
            jibun=p.get("jibun", "") or "",
            jimok=p.get("jimok", "") or "",
            owner=owner_names.get(pnu, "") if owner_names else "",
            book_area=book,
            enc_area=enc_area,
            enc_ratio=enc_ratio,
        ))
    return rows


def render_report(out_path, template_path, *, meta: Dict[str, str],
                  rows: List[PyeonibRow], inset_png: Optional[str] = None):
    """빈 양식(template_path)을 채워 .hwpx(out_path) 생성. hwpx_writer 엔진 사용.

    meta keys(채워질 때만): project_name, site_name, author, organization, analysis_date
    """
    meta_fields = {f"meta.{k}": str(v) for k, v in (meta or {}).items() if v is not None}
    data_rows = [_row_to_cells(r) for r in rows]
    return hwpx_writer.render(
        meta_fields, TABLE_PREFIX, data_rows,
        template_path, out_path, inset_png=inset_png,
    )
