# =============================================================================
# Dynamo for Revit - Python 노드 템플릿  (대상: Revit 2026 / Dynamo CPython3)
# 역할: Revit 부재에서 치수를 추출(m 단위) -> takeoff 모듈로 엑셀 수량산출서 생성
#
# [Revit 2026 메모]
#  - Revit 2026 의 Dynamo 는 Python 엔진으로 'CPython3'(Python 3.x)만 사용합니다
#    (IronPython2 미지원). 아래 코드는 CPython3 기준입니다.
#  - 일부 BuiltInParameter(특히 슬래브 두께/높이)는 버전에 따라 다를 수 있어,
#    값이 None 으로 나오면 해당 패밀리의 파라미터 이름을 직접 지정하세요.
#
# [사용 전 준비]
#  1) Dynamo Python 노드 엔진을 'CPython3' 로 설정.
#  2) takeoff 패키지가 openpyxl 을 쓰므로, Dynamo CPython3 환경에 openpyxl 설치:
#       - 노드에서 1회:  import subprocess, sys
#                        subprocess.check_call([sys.executable,'-m','pip','install','openpyxl'])
#       - 또는 pip install --target=<경로> openpyxl 후 그 경로를 sys.path 에 추가.
#
# [노드 입력 포트] (IList 로 받음)
#   IN[0] : 기둥 요소 리스트       (Columns)
#   IN[1] : 보/거더 요소 리스트     (StructuralFraming)
#   IN[2] : 기초 요소 리스트       (Foundations)
#   IN[3] : 바닥/슬래브 요소 리스트  (Floors)
#   IN[4] : 벽 요소 리스트         (Walls)
#   IN[5] : 출력 엑셀 경로 (문자열)  예: r"C:\\Temp\\수량산출서.xlsx"
#
#   각 카테고리 입력은 Dynamo 노드 "All Elements of Category" 등으로 만들어 연결.
# =============================================================================
import sys

import clr
clr.AddReference("RevitAPI")
clr.AddReference("RevitServices")
from Autodesk.Revit.DB import BuiltInParameter, StorageType  # noqa: E402
from RevitServices.Persistence import DocumentManager  # noqa: E402

# --- takeoff 모듈 경로 추가 (본인 환경에 맞게 수정) -------------------------
REPO_SCRIPTS = r"C:\Users\user\Desktop\AI\revit-quantity-takeoff\scripts"
if REPO_SCRIPTS not in sys.path:
    sys.path.append(REPO_SCRIPTS)
from takeoff import runner  # noqa: E402

doc = DocumentManager.Instance.CurrentDBDocument
FT_TO_M = 0.3048
SQFT_TO_SQM = FT_TO_M * FT_TO_M


def unwrap(items):
    """Dynamo 래핑 요소 -> Revit Element. 단일/리스트 모두 허용."""
    if items is None:
        return []
    if not isinstance(items, (list, tuple)):
        items = [items]
    out = []
    for it in items:
        out.append(it.InternalElement if hasattr(it, "InternalElement") else it)
    return out


def length_param_m(el, name):
    """인스턴스/타입에서 길이 파라미터를 m 로 읽음 (없으면 None)."""
    p = el.LookupParameter(name)
    if p and p.HasValue and p.StorageType == StorageType.Double:
        return round(p.AsDouble() * FT_TO_M, 4)
    # 타입 파라미터도 조회
    tp = doc.GetElement(el.GetTypeId()) if el.GetTypeId() else None
    if tp is not None:
        p = tp.LookupParameter(name)
        if p and p.HasValue and p.StorageType == StorageType.Double:
            return round(p.AsDouble() * FT_TO_M, 4)
    return None


def builtin_length_m(el, bip):
    p = el.get_Parameter(bip)
    if p and p.HasValue:
        return round(p.AsDouble() * FT_TO_M, 4)
    return None


def get_mark(el):
    p = el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)
    return p.AsString() if (p and p.HasValue) else ""


def get_type_name(el):
    tp = doc.GetElement(el.GetTypeId()) if el.GetTypeId() else None
    if tp is None:
        return ""
    p = tp.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_NAME)
    return p.AsString() if (p and p.HasValue) else (tp.Name if hasattr(tp, "Name") else "")


def get_level(el):
    p = el.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM) or \
        el.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM)
    if p and p.HasValue:
        lvl = doc.GetElement(p.AsElementId())
        return lvl.Name if lvl else ""
    return ""

# -----------------------------------------------------------------------------
# 카테고리별 치수 추출.
# ★ 패밀리마다 파라미터 이름이 다르므로 아래 LookupParameter 이름을 프로젝트에
#   맞게 수정한다. (예: 'b','h','단면폭','단면춤','B','D' 등)
# -----------------------------------------------------------------------------
def extract_column(el):
    return {
        "category": "Columns", "mark": get_mark(el), "type_name": get_type_name(el),
        "level": get_level(el), "count": 1,
        "b": length_param_m(el, "b") or length_param_m(el, "단면폭"),
        "h": length_param_m(el, "h") or length_param_m(el, "단면춤"),
        "height": builtin_length_m(el, BuiltInParameter.INSTANCE_LENGTH_PARAM)
                  or length_param_m(el, "높이"),
        # 보춤 등 겹침 공제(선택): 프로젝트 파라미터로 관리하거나 0.
        "deduct_height": length_param_m(el, "겹침공제") or 0,
    }


def extract_framing(el):
    return {
        "category": "StructuralFraming", "mark": get_mark(el), "type_name": get_type_name(el),
        "level": get_level(el), "count": 1,
        "b": length_param_m(el, "b") or length_param_m(el, "단면폭"),
        "h": length_param_m(el, "h") or length_param_m(el, "단면춤"),
        "length": builtin_length_m(el, BuiltInParameter.INSTANCE_LENGTH_PARAM),
        # 양단 기둥 물림 겹침 공제(선택). 정밀 산출 시 아래처럼 계산해 넣는다:
        #   - 보가 프레임되는 기둥을 찾아 보 축방향 치수(폭/춤)를 양단 합산.
        #   - 또는 보 길이를 '안목길이(clear span)'로 직접 모델/파라미터화.
        # 간단히는 프로젝트 파라미터 '겹침공제'(m) 를 읽어 사용.
        "deduct_length": length_param_m(el, "겹침공제") or 0,
    }


def extract_foundation(el):
    return {
        "category": "Foundations", "mark": get_mark(el), "type_name": get_type_name(el),
        "level": get_level(el), "count": 1,
        "b": length_param_m(el, "Width") or length_param_m(el, "폭"),
        "l": length_param_m(el, "Length") or length_param_m(el, "길이"),
        "h": length_param_m(el, "Thickness") or length_param_m(el, "두께") or length_param_m(el, "높이"),
    }


def extract_floor(el):
    area_p = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)
    area = round(area_p.AsDouble() * SQFT_TO_SQM, 4) if (area_p and area_p.HasValue) else None
    return {
        "category": "Floors", "mark": get_mark(el), "type_name": get_type_name(el),
        "level": get_level(el), "count": 1,
        "area": area,
        "thickness": builtin_length_m(el, BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)
                     or length_param_m(el, "두께"),
        # 둘레(perimeter)는 별도 산출이 필요하면 면적 경계에서 계산해 넣는다 (생략 시 측면 제외).
    }


def extract_wall(el):
    return {
        "category": "Walls", "mark": get_mark(el), "type_name": get_type_name(el),
        "level": get_level(el), "count": 1,
        "length": builtin_length_m(el, BuiltInParameter.CURVE_ELEM_LENGTH),
        "height": builtin_length_m(el, BuiltInParameter.WALL_USER_HEIGHT_PARAM),
        "thickness": builtin_length_m(el, BuiltInParameter.WALL_ATTR_WIDTH_PARAM),
        "deduct_length": length_param_m(el, "겹침공제") or 0,  # 교차 기둥 물림 공제(선택)
    }


# --- 입력 수집 ----------------------------------------------------------------
extractors = [
    (IN[0], extract_column),
    (IN[1], extract_framing),
    (IN[2], extract_foundation),
    (IN[3], extract_floor),
    (IN[4], extract_wall),
]
out_path = IN[5]

members = []
skipped = []
for raw_list, fn in extractors:
    for el in unwrap(raw_list):
        try:
            members.append(fn(el))
        except Exception as exc:  # 한 부재 실패가 전체를 막지 않게
            skipped.append("%s: %s" % (getattr(el, "Id", "?"), exc))

# --- 엑셀 생성 ----------------------------------------------------------------
project_info = {"공사명": doc.Title, "작성": "Dynamo 자동 산출 (단위 m)"}
try:
    result = runner.run(members, out_path, project_info=project_info)
    OUT = {"result": result, "skipped": skipped}
except Exception as exc:
    # 실패 시 로그 파일로 남겨 VS Code 의 Claude 가 바로 읽게 함 (스샷 불필요)
    import traceback
    log = out_path + ".error.log"
    with open(log, "w", encoding="utf-8") as f:
        f.write(traceback.format_exc())
    OUT = {"error": str(exc), "log": log, "skipped": skipped}
