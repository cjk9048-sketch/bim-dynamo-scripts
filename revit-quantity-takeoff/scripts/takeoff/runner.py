"""부재 데이터 -> 그룹핑/집계 -> 엑셀 수량산출서 생성 오케스트레이션."""
from __future__ import annotations

import json
from collections import OrderedDict
from typing import Any

from . import calc
from .models import Member


def group_members(members: list[Member]) -> list[Member]:
    """동일 규격(group_key) 부재의 개수를 합쳐 집계용 Member 로 묶는다."""
    grouped: "OrderedDict[tuple, Member]" = OrderedDict()
    for m in members:
        key = m.group_key()
        if key in grouped:
            grouped[key].count += m.count
        else:
            grouped[key] = Member(
                category=m.category, mark=m.mark, type_name=m.type_name,
                material=m.material, level=m.level, count=m.count, dims=dict(m.dims),
            )
    return list(grouped.values())


def build_line_items(members_data: list[dict[str, Any]],
                     quantities: set[str] | None = None,
                     overlaps: list[dict] | None = None,
                     priority: dict | None = None) -> list:
    """입력 dict 리스트 -> LineItem 리스트.

    overlaps 가 주어지면 그룹 집계 '전에' 인스턴스 단위로 겹침 공제를 귀속한다
    (겹침을 한 번만 계산하기 위함).
    """
    members = [Member.from_dict(d) for d in members_data]
    if overlaps:
        from . import overlap
        overlap.resolve_overlaps(members, overlaps, priority=priority)
    line_items = []
    for grouped in group_members(members):
        line_items.extend(calc.compute(grouped, quantities=quantities))
    return line_items


def run(members_data: list[dict[str, Any]], out_path: str,
        project_info: dict | None = None,
        quantities: set[str] | None = None,
        overlaps: list[dict] | None = None,
        priority: dict | None = None) -> dict:
    """부재 데이터로 엑셀 수량산출서를 만들고 요약을 반환한다.

    Dynamo Python 노드에서 부재 dict 리스트(+선택적으로 겹침 overlaps)를 만들어
    이 함수를 호출한다. 엑셀 출력을 위해 openpyxl 이 필요하므로 import 는 호출 시점에 한다.
    """
    from . import excel  # openpyxl 의존성을 호출 시점으로 미룸 (계산만 할 땐 불필요)

    line_items = build_line_items(members_data, quantities=quantities,
                                  overlaps=overlaps, priority=priority)
    if not line_items:
        raise ValueError("산출할 부재가 없습니다. 입력 데이터를 확인하세요.")
    totals = excel.write_takeoff(line_items, out_path, project_info=project_info)
    return {
        "out_path": out_path,
        "member_count": sum(int(d.get("count", 1)) for d in members_data),
        "row_count": len(line_items),
        "totals": totals,
    }


def run_from_json(json_path: str, out_path: str,
                  project_info: dict | None = None,
                  quantities: set[str] | None = None) -> dict:
    """JSON 파일(부재 리스트)로부터 엑셀을 생성한다 (테스트/데모용)."""
    with open(json_path, "r", encoding="utf-8") as f:
        data = json.load(f)
    members_data = data["members"] if isinstance(data, dict) else data
    info = project_info
    overlaps = None
    if isinstance(data, dict):
        if info is None:
            info = data.get("project_info")
        overlaps = data.get("overlaps")
    return run(members_data, out_path, project_info=info,
               quantities=quantities, overlaps=overlaps)
