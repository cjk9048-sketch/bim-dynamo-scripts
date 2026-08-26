"""부재 교차부 겹침의 '단 한 번' 귀속(ownership) 로직.

핵심 원칙: 두 부재가 겹치는 구간은 **정해진 우선순위가 높은 부재 하나에만 귀속**되고,
낮은 부재에서는 그만큼 공제한다. 처리 '순서'가 아니라 '우선순위'로 결정하므로,
입력 순서와 무관하게 겹침이 **정확히 한 번만** 계산된다(중복·누락 없음).

Dynamo(기하)는 부재 쌍의 겹침 길이만 계산해 넘기고, 귀속/공제 판단은 여기서 한다.
이 모듈은 Revit 에 의존하지 않으므로 단독 테스트가 가능하다.
"""
from __future__ import annotations

from .models import Member

# 우선순위(숫자가 작을수록 높음 = 겹침 구간을 '소유'하여 그대로 포함).
# 기초가 기둥-기초 접합부를 소유, 기둥이 보-기둥 접합부를 소유 ... 형태.
DEFAULT_PRIORITY = {
    "Foundations": 0,
    "Columns": 1,
    "Walls": 2,
    "StructuralFraming": 3,
    "Floors": 4,
}

# 우선순위가 낮아 '공제'당하는 부재가, 어느 치수에서 겹침을 빼는지.
_LOSER_DEDUCT_KEY = {
    "Columns": "deduct_height",        # 기둥은 높이에서 공제
    "StructuralFraming": "deduct_length",  # 보는 길이에서 공제
    "Walls": "deduct_length",          # 벽은 길이에서 공제
    # Floors/Foundations 는 선형 공제 개념이 아니므로 제외(향후 deduct_volume).
}


class OverlapWarning(Exception):
    """겹침을 어느 부재에도 공제하지 못한 경우(슬래브 등) — 정보용."""


def _winner_loser(a: Member, b: Member, ia: int, ib: int, priority: dict):
    ra = priority.get(a.category, 99)
    rb = priority.get(b.category, 99)
    if ra < rb:
        return a, b
    if rb < ra:
        return b, a
    # 동일 우선순위 → 인덱스가 작은 쪽이 소유(안정적 결정)
    return (a, b) if ia <= ib else (b, a)


def resolve_overlaps(members: list[Member], overlaps: list[dict],
                     priority: dict | None = None) -> dict:
    """members(인스턴스 리스트)의 dims 에 겹침 공제를 누적 반영한다.

    overlaps 항목: {"a": i, "b": j, "length": m}
      - i, j 는 members 의 인덱스, length 는 두 부재의 겹침 길이(m).
    반환: 적용 요약 {"applied": n, "skipped": [..]}.
    """
    priority = priority or DEFAULT_PRIORITY
    applied = 0
    skipped: list[str] = []
    for ov in overlaps:
        ia, ib = int(ov["a"]), int(ov["b"])
        length = float(ov.get("length", 0) or 0)
        if length <= 0:
            continue
        a, b = members[ia], members[ib]
        _, loser = _winner_loser(a, b, ia, ib, priority)
        key = _LOSER_DEDUCT_KEY.get(loser.category)
        if key is None:
            skipped.append(
                f"{a.mark or a.category}↔{b.mark or b.category}: "
                f"'{loser.category}' 는 선형 공제 대상이 아님(겹침 {length}m 미반영)"
            )
            continue
        loser.dims[key] = round(float(loser.dims.get(key, 0) or 0) + length, 4)
        applied += 1
    return {"applied": applied, "skipped": skipped}
