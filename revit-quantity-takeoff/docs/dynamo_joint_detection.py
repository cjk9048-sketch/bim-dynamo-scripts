# =============================================================================
# Dynamo for Revit - 조인트 자동탐지 노드  (Revit 2026 / CPython3)
#
# 역할: 보 중심선이 기둥 솔리드 안에 물리는 '겹침길이'를 자동 계산하여,
#       takeoff.overlap 의 우선순위 귀속(겹침 1회 계산)으로 공제 후 엑셀 생성.
#
# 핵심: 겹침은 '정해진 우선순위'(기초>기둥>벽>보>슬래브)로 한 부재에만 귀속.
#       기둥-보 접합부는 기둥이 소유(체적 포함), 보는 그만큼 길이 공제.
#       → 산출식에는 순(net) 길이만 나오고, 겹침은 한 번만 계산됨.
#
# [입력 포트]
#   IN[0] : 기둥 요소 리스트 (Columns)
#   IN[1] : 보/거더 요소 리스트 (StructuralFraming)
#   IN[2] : 출력 엑셀 경로 (문자열)
#   (기초/슬래브/벽도 동일 패턴으로 확장 가능 — 아래 build_members 참고)
# =============================================================================
import sys

import clr
clr.AddReference("RevitAPI")
clr.AddReference("RevitServices")
from Autodesk.Revit.DB import (  # noqa: E402
    BuiltInParameter, GeometryInstance, Options, Solid,
    SolidCurveIntersectionOptions, StorageType,
)
from RevitServices.Persistence import DocumentManager  # noqa: E402

REPO_SCRIPTS = r"C:\Users\user\Desktop\AI\revit-quantity-takeoff\scripts"
if REPO_SCRIPTS not in sys.path:
    sys.path.append(REPO_SCRIPTS)
from takeoff import runner  # noqa: E402

doc = DocumentManager.Instance.CurrentDBDocument
FT_TO_M = 0.3048


# ---- 공통 추출 헬퍼 (dynamo_node.py 와 동일 규칙) ---------------------------
def unwrap(items):
    if items is None:
        return []
    if not isinstance(items, (list, tuple)):
        items = [items]
    return [it.InternalElement if hasattr(it, "InternalElement") else it for it in items]


def p_len_m(el, name):
    p = el.LookupParameter(name)
    if p and p.HasValue and p.StorageType == StorageType.Double:
        return round(p.AsDouble() * FT_TO_M, 4)
    tp = doc.GetElement(el.GetTypeId()) if el.GetTypeId() else None
    if tp is not None:
        p = tp.LookupParameter(name)
        if p and p.HasValue and p.StorageType == StorageType.Double:
            return round(p.AsDouble() * FT_TO_M, 4)
    return None


def bip_len_m(el, bip):
    p = el.get_Parameter(bip)
    return round(p.AsDouble() * FT_TO_M, 4) if (p and p.HasValue) else None


def get_mark(el):
    p = el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)
    return p.AsString() if (p and p.HasValue) else ""


def get_type_name(el):
    tp = doc.GetElement(el.GetTypeId()) if el.GetTypeId() else None
    if tp is None:
        return ""
    p = tp.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_NAME)
    return p.AsString() if (p and p.HasValue) else getattr(tp, "Name", "")


# ---- 기하: 보 중심선이 기둥 솔리드에 물리는 길이 ----------------------------
def get_solids(el):
    opt = Options()
    opt.ComputeReferences = False
    opt.IncludeNonVisibleObjects = False
    solids = []
    geo = el.get_Geometry(opt)
    if geo is None:
        return solids
    for g in geo:
        if isinstance(g, Solid) and g.Volume > 0:
            solids.append(g)
        elif isinstance(g, GeometryInstance):
            for ig in g.GetInstanceGeometry():
                if isinstance(ig, Solid) and ig.Volume > 0:
                    solids.append(ig)
    return solids


def beam_curve(el):
    loc = el.Location
    return loc.Curve if (loc is not None and hasattr(loc, "Curve")) else None


def overlap_length_m(curve, solids):
    """curve 가 solids 내부에 포함되는 총 길이(m)."""
    total = 0.0
    opts = SolidCurveIntersectionOptions()  # 기본: 솔리드 내부 구간 반환
    for s in solids:
        try:
            res = s.IntersectWithCurve(curve, opts)
        except Exception:
            continue
        if res:
            for i in range(res.SegmentCount):
                total += res.GetCurveSegment(i).Length
    return round(total * FT_TO_M, 4)


def bbox_overlap(a, b):
    """BoundingBox 교차 여부 (느린 솔리드 연산 전 1차 필터)."""
    ba, bb = a.get_BoundingBox(None), b.get_BoundingBox(None)
    if ba is None or bb is None:
        return True
    return (ba.Min.X <= bb.Max.X and ba.Max.X >= bb.Min.X and
            ba.Min.Y <= bb.Max.Y and ba.Max.Y >= bb.Min.Y and
            ba.Min.Z <= bb.Max.Z and ba.Max.Z >= bb.Min.Z)


# ---- 부재 dict 구성 ----------------------------------------------------------
def extract_column(el):
    return {"category": "Columns", "mark": get_mark(el), "type_name": get_type_name(el),
            "count": 1, "b": p_len_m(el, "b") or p_len_m(el, "단면폭"),
            "h": p_len_m(el, "h") or p_len_m(el, "단면춤"),
            "height": bip_len_m(el, BuiltInParameter.INSTANCE_LENGTH_PARAM) or p_len_m(el, "높이")}


def extract_framing(el):
    return {"category": "StructuralFraming", "mark": get_mark(el), "type_name": get_type_name(el),
            "count": 1, "b": p_len_m(el, "b") or p_len_m(el, "단면폭"),
            "h": p_len_m(el, "h") or p_len_m(el, "단면춤"),
            "length": bip_len_m(el, BuiltInParameter.INSTANCE_LENGTH_PARAM)}


# ---- 메인 -------------------------------------------------------------------
col_els = unwrap(IN[0])
beam_els = unwrap(IN[1])
out_path = IN[2]

# members 리스트와 인덱스(기둥 먼저, 그다음 보)
members = [extract_column(e) for e in col_els]
col_index = list(range(len(members)))           # 기둥의 members 인덱스
beam_start = len(members)
members += [extract_framing(e) for e in beam_els]
beam_index = list(range(beam_start, len(members)))

# 기둥 솔리드/커브 사전 계산
col_solids = [get_solids(e) for e in col_els]
beam_curves = [beam_curve(e) for e in beam_els]

# 보↔기둥 겹침 탐지
overlaps = []
for bi, beam_el in enumerate(beam_els):
    crv = beam_curves[bi]
    if crv is None:
        continue
    for ci, col_el in enumerate(col_els):
        if not bbox_overlap(beam_el, col_el):
            continue
        L = overlap_length_m(crv, col_solids[ci])
        if L > 1e-4:
            overlaps.append({"a": col_index[ci], "b": beam_index[bi], "length": L})

# 엑셀 생성 (overlap.resolve_overlaps 가 우선순위로 보에 공제 귀속)
project_info = {"공사명": doc.Title, "작성": "Dynamo 자동 조인트탐지 (단위 m)"}
try:
    result = runner.run(members, out_path, project_info=project_info, overlaps=overlaps)
    OUT = {"result": result, "overlaps_found": len(overlaps)}
except Exception as exc:
    import traceback
    log = out_path + ".error.log"
    with open(log, "w", encoding="utf-8") as f:
        f.write(traceback.format_exc())
    OUT = {"error": str(exc), "log": log, "overlaps_found": len(overlaps)}
