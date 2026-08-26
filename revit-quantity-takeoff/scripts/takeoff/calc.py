"""카테고리별 수량 산출식과 계산.

각 부재에 대해 산출식(산출근거) 문자열과 계산값을 함께 만든다.
거푸집 산정 규칙은 회사/표준품셈 기준에 따라 달라질 수 있으므로
`docs/formwork_rules.md` 를 참고해 조정한다.
"""
from __future__ import annotations

from .models import Member, LineItem

# quantity 키 -> (한글 라벨, 단위)
QUANTITY_LABELS = {
    "concrete": ("콘크리트", "m³"),
    "formwork": ("거푸집", "m²"),
    "count": ("수량", "EA"),
    "length": ("길이", "m"),
}


class CalcError(Exception):
    """필수 치수 누락 등 산출 입력 오류."""


def _n(x: float) -> str:
    """산출식에 쓰기 좋은 간결한 숫자 표기 (불필요한 0 제거)."""
    return f"{round(float(x), 4):g}"


def _req(dims: dict, key: str, ctx: str) -> float:
    if key not in dims or dims[key] is None:
        raise CalcError(f"{ctx}: 필수 치수 '{key}' 가 없습니다.")
    try:
        return float(dims[key])
    except (TypeError, ValueError) as exc:
        raise CalcError(f"{ctx}: 치수 '{key}' 값이 숫자가 아닙니다 ({dims[key]!r}).") from exc


def _opt(dims: dict, key: str) -> float:
    """선택 치수(공제 등). 없으면 0."""
    v = dims.get(key)
    if v in (None, ""):
        return 0.0
    try:
        return float(v)
    except (TypeError, ValueError) as exc:
        raise CalcError(f"치수 '{key}' 값이 숫자가 아닙니다 ({v!r}).") from exc


def _net(base: float, deduct: float, ctx: str, label: str) -> float:
    """겹침 공제를 반영한 유효치수(순값). 공제 후 0 이하면 오류."""
    net = base - deduct
    if net <= 0:
        raise CalcError(f"{ctx}: 공제 후 {label}가 0 이하입니다 ({label}={_n(base)}, 공제={_n(deduct)}).")
    return net


def _deduct_note(deduct: float, base: float, net: float) -> str:
    """산출식에는 순값만 쓰고, 공제 내역은 비고로만 남긴다."""
    return f"교차부 겹침 {_n(deduct)}m 공제 ({_n(base)}→{_n(net)})" if deduct else ""


def _join_notes(*notes: str) -> str:
    return " / ".join(n for n in notes if n)


def _columns(dims: dict, n: int, ctx: str) -> list[dict]:
    b = _req(dims, "b", ctx)        # 단면 폭
    h = _req(dims, "h", ctx)        # 단면 춤
    H = _req(dims, "height", ctx)   # 높이
    dH = _opt(dims, "deduct_height")  # 보춤 등 겹침 공제
    Hv = _net(H, dH, ctx, "높이")
    dn = _deduct_note(dH, H, Hv)
    return [
        {"quantity": "concrete", "formula": f"{_n(b)}×{_n(h)}×{_n(Hv)}×{n}", "value": b * h * Hv * n,
         "note": dn},
        {"quantity": "formwork", "formula": f"2×({_n(b)}+{_n(h)})×{_n(Hv)}×{n}", "value": 2 * (b + h) * Hv * n,
         "note": _join_notes("4면", dn)},
        {"quantity": "count", "formula": f"{n}", "value": n},
        {"quantity": "length", "formula": f"{_n(Hv)}×{n}", "value": Hv * n, "note": dn},
    ]


def _framing(dims: dict, n: int, ctx: str) -> list[dict]:
    b = _req(dims, "b", ctx)
    h = _req(dims, "h", ctx)
    L = _req(dims, "length", ctx)
    dL = _opt(dims, "deduct_length")  # 기둥/벽에 물리는 양단 겹침 합
    Lv = _net(L, dL, ctx, "길이")
    dn = _deduct_note(dL, L, Lv)
    return [
        {"quantity": "concrete", "formula": f"{_n(b)}×{_n(h)}×{_n(Lv)}×{n}", "value": b * h * Lv * n,
         "note": dn},
        {"quantity": "formwork", "formula": f"(2×{_n(h)}+{_n(b)})×{_n(Lv)}×{n}", "value": (2 * h + b) * Lv * n,
         "note": _join_notes("양 측면+바닥 (상부 슬래브면 제외)", dn)},
        {"quantity": "count", "formula": f"{n}", "value": n},
        {"quantity": "length", "formula": f"{_n(Lv)}×{n}", "value": Lv * n, "note": dn},
    ]


def _foundations(dims: dict, n: int, ctx: str) -> list[dict]:
    B = _req(dims, "b", ctx)
    L = _req(dims, "l", ctx)
    H = _req(dims, "h", ctx)
    return [
        {"quantity": "concrete", "formula": f"{_n(B)}×{_n(L)}×{_n(H)}×{n}", "value": B * L * H * n},
        {"quantity": "formwork", "formula": f"2×({_n(B)}+{_n(L)})×{_n(H)}×{n}", "value": 2 * (B + L) * H * n,
         "note": "측면 4면 (상·하부 거푸집 제외)"},
        {"quantity": "count", "formula": f"{n}", "value": n},
    ]


def _floors(dims: dict, n: int, ctx: str) -> list[dict]:
    A = _req(dims, "area", ctx)          # 평면 면적
    t = _req(dims, "thickness", ctx)     # 두께
    perim = dims.get("perimeter")
    items = [
        {"quantity": "concrete", "formula": f"{_n(A)}×{_n(t)}×{n}", "value": A * t * n},
    ]
    if perim:
        P = float(perim)
        items.append({"quantity": "formwork",
                      "formula": f"{_n(A)}×{n} + {_n(P)}×{_n(t)}×{n}",
                      "value": A * n + P * t * n,
                      "note": "하부면+측면(둘레×두께)"})
    else:
        items.append({"quantity": "formwork", "formula": f"{_n(A)}×{n}", "value": A * n,
                      "note": "하부면만 (둘레 미입력으로 측면 제외)"})
    items.append({"quantity": "count", "formula": f"{n}", "value": n})
    return items


def _walls(dims: dict, n: int, ctx: str) -> list[dict]:
    L = _req(dims, "length", ctx)
    H = _req(dims, "height", ctx)
    t = _req(dims, "thickness", ctx)
    dL = _opt(dims, "deduct_length")  # 교차 기둥 등에 물리는 겹침 합
    Lv = _net(L, dL, ctx, "길이")
    dn = _deduct_note(dL, L, Lv)
    return [
        {"quantity": "concrete", "formula": f"{_n(Lv)}×{_n(H)}×{_n(t)}×{n}", "value": Lv * H * t * n,
         "note": dn},
        {"quantity": "formwork", "formula": f"2×{_n(Lv)}×{_n(H)}×{n}", "value": 2 * Lv * H * n,
         "note": _join_notes("양면", dn)},
        {"quantity": "count", "formula": f"{n}", "value": n},
        {"quantity": "length", "formula": f"{_n(Lv)}×{n}", "value": Lv * n, "note": dn},
    ]


_BUILDERS = {
    "Columns": _columns,
    "StructuralFraming": _framing,
    "Foundations": _foundations,
    "Floors": _floors,
    "Walls": _walls,
}


def compute(member: Member, quantities: set[str] | None = None) -> list[LineItem]:
    """한 부재(그룹)에 대한 LineItem 리스트를 만든다.

    quantities 가 주어지면 해당 산출항목만 반환한다 (기본: 전부).
    """
    builder = _BUILDERS.get(member.category)
    if builder is None:  # pragma: no cover - Member 가 이미 카테고리 검증
        raise CalcError(f"카테고리 '{member.category}' 에 대한 산출식이 없습니다.")

    ctx = f"[{member.category}] {member.mark or member.type_name or '(무명)'}"
    raw = builder(member.dims, member.count, ctx)

    items: list[LineItem] = []
    for row in raw:
        q = row["quantity"]
        if quantities is not None and q not in quantities:
            continue
        label, unit = QUANTITY_LABELS[q]
        items.append(LineItem(
            category=member.category,
            mark=member.mark,
            type_name=member.type_name,
            quantity=q,
            unit=unit,
            formula=row["formula"],
            value=round(row["value"], 3),
            count=member.count,
            level=member.level,
            material=member.material,
            note=row.get("note", ""),
        ))
    return items
