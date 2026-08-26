"""수량산출 데이터 구조.

길이/치수 단위는 모두 **미터(m)** 로 가정한다. Revit 내부 단위(피트)는
Dynamo 노드에서 m 로 변환해 넘기는 것을 전제로 한다.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

# Dynamo/Revit 의 BuiltInCategory 와 매칭되는 정규 카테고리 키
CATEGORIES = ("Columns", "StructuralFraming", "Foundations", "Floors", "Walls")

CATEGORY_LABELS = {
    "Columns": "기둥",
    "StructuralFraming": "보·거더",
    "Foundations": "기초",
    "Floors": "슬래브",
    "Walls": "벽",
}


@dataclass
class Member:
    """Revit 인스턴스 하나(또는 동일 그룹)를 표현하는 입력 데이터."""

    category: str
    mark: str = ""          # 부재기호 (예: C1, G1, F1)
    type_name: str = ""     # Revit 패밀리 타입명 (규격)
    material: str = ""      # 콘크리트 강도 등 (예: 24-21-15)
    level: str = ""         # 층 (예: 2F)
    count: int = 1          # 동일 부재 개수
    dims: dict[str, float] = field(default_factory=dict)  # 치수 (m, m²)

    def __post_init__(self) -> None:
        if self.category not in CATEGORIES:
            raise ValueError(
                f"알 수 없는 카테고리 '{self.category}'. 허용: {', '.join(CATEGORIES)}"
            )
        if self.count <= 0:
            raise ValueError(f"count 는 1 이상이어야 합니다 (mark={self.mark!r}).")

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "Member":
        known = {"category", "mark", "type_name", "material", "level", "count", "dims"}
        kwargs = {k: data[k] for k in known if k in data}
        # dims 를 따로 주지 않고 평면적으로 넘긴 경우도 허용
        if "dims" not in kwargs:
            kwargs["dims"] = {k: v for k, v in data.items() if k not in known}
        kwargs.setdefault("category", data.get("category"))
        return cls(**kwargs)

    def group_key(self) -> tuple:
        """동일 규격 부재를 집계하기 위한 키."""
        dim_sig = tuple(sorted((k, round(float(v), 6)) for k, v in self.dims.items()))
        return (self.category, self.mark, self.type_name, self.material, self.level, dim_sig)


@dataclass
class LineItem:
    """수량산출서의 한 행(산출식 + 수량)."""

    category: str
    mark: str
    type_name: str
    quantity: str       # 'concrete' | 'formwork' | 'count' | 'length'
    unit: str           # 'm³' | 'm²' | 'EA' | 'm'
    formula: str        # 산출식 (산출근거)
    value: float        # 계산값
    count: int = 1
    level: str = ""
    material: str = ""
    note: str = ""
