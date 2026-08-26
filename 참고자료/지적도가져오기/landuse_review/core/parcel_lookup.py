# -*- coding: utf-8 -*-
"""M1: 지번 조회 + 캔버스 레이어 추가.

자연어 지번 또는 PNU(19자리) 입력을 받아 필지를 조회하고 QGIS 캔버스에 "선택필지"
메모리 레이어를 추가한다. 옵션(radius_m>0): 반경 내 인접 필지를 보조 시각화로 추가.

데이터 출처(`config.DATA_SOURCE`):
  · "DB"(기본, 키 전): PROD `lsmd_cont_ldreg` 조회 → 미스 시 V-World 보강(키 있으면).
  · "API"(키 후): V-World 연속지적 우선 → 미스 시 DB 폴백.

지번 텍스트 → PNU(1단계):
  1. 입력이 19자리 숫자 → PNU 직접.
  2. 그 외(지번 텍스트):
     a. **키 있으면 V-World search API 1순위** — `/req/search&type=address&category=parcel&query=지번`
        → 응답 `id` = 19자리 PNU (주소 정규화로 가장 견고).
     b. 키 없으면 사내 DB 폴백 — bnd_li_bjd/bnd_emd_bjd 이름 LIKE 로 prefix 확보
        → 시군구5+읍면동/리 코드 + 산여부 + 본번4 + 부번4 = PNU 19자리 LIKE.
     산(임야) 필지는 PNU 11번째 자리 '2' (도로 사업 임야 편입 빈번 — §2-⑥).
  (search 는 텍스트→PNU 변환만 담당. 필지 도형·속성은 아래 출처[DB/data API]에서 조회.)

소유자 "이름"은 어떤 자동 경로로도 못 가져온다(v2 §6.2) → '선택필지' 레이어에
편집 가능한 빈 `owner_name` 필드를 두고, 설계자가 **QGIS 속성 테이블에서 직접 입력**한다.
"""
import math
import re
from typing import List, Dict, Tuple, Optional

from .. import db_env
from ..db import queries
from ..api import vworld_fallback
from . import config
from . import symbology
from . import layers

try:
    from qgis.core import (
        QgsVectorLayer, QgsField, QgsFeature, QgsGeometry, QgsProject,
        QgsMessageLog, Qgis,
    )
    from qgis.PyQt.QtCore import QVariant
    _HAS_QGIS = True
except ImportError:
    _HAS_QGIS = False


def _log(msg: str):
    """QGIS 로그창에 남긴다. 조용히 삼키는 것보다는 낫지만, 사용자에게 보여야 하는
    실패는 호출측이 messageBar/report_step_result 로 따로 띄운다."""
    if not _HAS_QGIS:
        return
    try:
        QgsMessageLog.logMessage(msg, "landuse_review", Qgis.Warning)
    except Exception:
        pass


# 한글 필드 alias
PARCEL_FIELD_ALIASES = {
    "pnu": "필지고유번호(PNU)",
    "jibun": "지번",
    "jimok": "지목",
    "area_sqm": "공부면적(㎡)",
    "owner_type": "소유구분",
    "owner_name": "소유자(직접입력)",
    "is_mountain": "산(임야)",
    "is_neighbor": "인접필지여부",
}

PNU_RE = re.compile(r"^\d{19}$")
# "산45-1" 의 '산' 도 인식 — 임야 필지
JIBUN_RE = re.compile(
    r"([가-힣A-Za-z0-9]+(?:리|동|읍|면))\s*(산)?\s*(\d+)(?:[-\s](\d+))?\s*번?지?",
    re.UNICODE,
)
# 지번 앞의 행정 지명 토큰 전부("어상천면 대전리 645-1" → ['어상천면', '대전리']).
# JIBUN_RE 는 숫자 바로 앞 한 토큰만 잡아 상위 읍·면·동을 버린다 → 동명 리 구분 불가.
ADMIN_TOKEN_RE = re.compile(r"[가-힣]+(?:읍|면|동|리)", re.UNICODE)


class AmbiguousRegion(Exception):
    """동명 법정리/읍면동이 여러 곳이라 필지를 특정할 수 없다.

    임의로 하나를 고르면 **다른 시군구의 필지를 조용히 가져온다**('대전리'는 전국 17곳).
    그래서 예외로 올려 사용자에게 되묻는다.
    """


# ─────────────────────────────────────────────────────────────────────────
# 입력 파싱
# ─────────────────────────────────────────────────────────────────────────

def parcel_key(p: Dict):
    """필지 식별키 — 누적 병합(controller)과 목록 삭제(step1 UI)가 **반드시 공유**해야 한다.

    pnu 가 있으면 pnu, 없으면(미발견 행) 사용자가 입력한 원본 지번으로 구분한다.
    두 곳에 따로 정의하면 미발견 행 삭제가 조용히 어긋난다.
    """
    pnu = p.get("pnu")
    return ("pnu", pnu) if pnu else ("miss", p.get("jibun", ""))


def _finite_pos(v) -> Optional[float]:
    """유한하고 양수인 면적이면 float, 아니면 None.

    `a22 = 0`(실재)·NULL·nan·음수를 **명시적으로** 걸러낸다. `float(x) or ...` 로
    거르면 `0`·NULL 은 falsy 라 조용히 폴백하고(정상), nan 은 truthy 라 그대로 새어
    보고서에 `nan` 이 찍히며, 음수도 그대로 흘러 편입면적이 음수로 절삭된다(F6).
    """
    try:
        f = float(v)
    except (TypeError, ValueError):
        return None
    return f if (math.isfinite(f) and f > 0.0) else None


def book_area_info(p: Dict) -> Tuple[float, str]:
    """공부면적(㎡)과 그 **출처**.

    분모(대장면적)와 표시값이 어디서 왔는지 호출측이 알아야 폴백을 사용자에게 알릴 수
    있다(F5). source:
      · `"ledger"`   — 토지대장 면적(`al_d160.a22`). 공식 검토서 값. 정답.
      · `"geometry"` — 대장값이 없거나 무효(0·NULL·nan·음수)라 도형 면적으로 되돌림.
      · `"none"`     — 대장·도형 면적 둘 다 무효(면적 0).

    연속지적도는 도엽을 이어 붙인 도면이라 `ST_Area(geom)` 가 대장 면적과 어긋난다
    (645-1: 도형 4,670.7 vs 대장 4,669.5). 공식 검토서는 대장 면적을 쓴다.
    `book_area`(al_d160.a22)는 controller 가 소유구분 조회 결과로 채운다.

    ⚠ 정의를 여기 한 곳에만 둔다. 레이어 속성·편입률 분모·검토서 표가 서로 다른
      면적을 쓰면 사용자는 어느 것이 맞는지 알 수 없다.
    """
    ledger = _finite_pos(p.get("book_area"))
    if ledger is not None:
        return ledger, "ledger"
    geom = _finite_pos(p.get("area_sqm"))
    if geom is not None:
        return geom, "geometry"
    return 0.0, "none"


def book_area(p: Dict) -> float:
    """공부면적(㎡) — 대장면적 우선, 없으면 도형면적. 출처가 필요하면 `book_area_info`."""
    return book_area_info(p)[0]


def _parse_input(jibun_text: str) -> List[Dict]:
    """입력 텍스트(여러 줄 가능)를 PNU 또는 (이름, 산여부, 본번, 부번) dict 목록으로."""
    out = []
    for raw_line in (jibun_text or "").splitlines():
        line = raw_line.strip()
        if not line:
            continue
        clean = line.replace(" ", "")
        if PNU_RE.match(clean):
            out.append({"pnu": clean, "raw": line})
            continue
        m = JIBUN_RE.search(line)
        if m:
            name = m.group(1)
            is_mt = bool(m.group(2))            # '산' 이면 임야
            bon = int(m.group(3))
            bu = int(m.group(4) or 0)
            # 숫자 앞부분의 지명 토큰 전부 — 동명 리 구분(읍면동)에 쓴다.
            tokens = ADMIN_TOKEN_RE.findall(line[:m.start(3)])
            out.append({"name": name, "tokens": tokens, "mountain": is_mt,
                        "bon": bon, "bu": bu, "raw": line})
        else:
            out.append({"raw": line})           # 파싱 실패 — placeholder
    return out


def _resolve_prefix(cur, name: str, tokens: List[str] = None) -> Optional[str]:
    """지명으로 법정리(10자리) 우선, 없으면 읍면동(8자리+'00') prefix 를 반환한다.

    tokens 에 상위 읍·면·동이 함께 있으면 그 코드로 동명 리를 좁힌다.
    좁힌 뒤에도 여러 곳이면 **임의 선택 대신 `AmbiguousRegion` 을 올린다**.

    Raises:
        AmbiguousRegion: 동명 지역이 여럿이라 특정 불가.
    """
    tokens = tokens or [name]
    ri_names = [t for t in tokens if t.endswith("리")]
    emd_names = [t for t in tokens if t.endswith(("읍", "면", "동"))]

    def _emd_codes(nm: str) -> List[str]:
        cur.execute(queries.EMD_BY_NAME.format(schema=db_env.DB_SCHEMA), (nm,))
        return [str(r[0]) for r in cur.fetchall()]

    if ri_names:
        cur.execute(queries.LI_BY_NAME.format(schema=db_env.DB_SCHEMA), (ri_names[-1],))
        cands = [str(r[0]) for r in cur.fetchall()]
        if emd_names and len(cands) > 1:
            parents = tuple(_emd_codes(emd_names[-1]))
            if parents:
                cands = [c for c in cands if c.startswith(parents)]
        if len(cands) == 1:
            return cands[0]                       # 시군구5+읍면동3+리2 = 10자리
        if len(cands) > 1:
            raise AmbiguousRegion(
                f"'{ri_names[-1]}' 은(는) 전국 {len(cands)}곳입니다. "
                f"'어상천면 {ri_names[-1]} 645-1' 처럼 읍·면·동을 함께 입력하거나 "
                f"PNU 19자리를 입력하세요.")

    if emd_names:
        cands = _emd_codes(emd_names[-1])
        if len(cands) == 1:
            return cands[0] + "00"                # 읍면동 8자리 + 리없음 '00'
        if len(cands) > 1:
            raise AmbiguousRegion(
                f"'{emd_names[-1]}' 은(는) 전국 {len(cands)}곳입니다. "
                f"PNU 19자리를 입력하거나 시·군·구를 확인하세요.")
    return None


def _bnd_key(pnu: str):
    """PNU 에서 소속 법정경계를 역산 — ('ri', 10자리) 또는 ('emd', 8자리).

    PNU = 시군구5 + 읍면동3 + 리2 + 산여부1 + 본번4 + 부번4.
    리 자리가 '00' 이면 리가 없는 지역(도시 동 등)이라 읍면동 경계를 쓴다.
    `lsmd_cont_ldreg` 에 pnu 인덱스가 없어, 이 경계 도형으로 GiST 를 먼저 태운다.
    """
    if not pnu or len(pnu) < 10:
        return None
    return ("emd", pnu[:8]) if pnu[8:10] == "00" else ("ri", pnu[:10])


def _pnu_candidates(prefix10: str, bon: int, bu: int, mountain: bool) -> List[str]:
    """PNU 19자리 후보 — 산여부 우선순위(일반 '1'/임야 '2')를 입력에 맞춰 정렬.

    PNU = prefix10 + 산여부1 + 본번4(zero-pad) + 부번4(zero-pad).
    """
    body = f"{bon:04d}{bu:04d}"
    if mountain:
        return [f"{prefix10}2{body}", f"{prefix10}1{body}"]
    return [f"{prefix10}1{body}", f"{prefix10}2{body}"]


def _split_jibun_jimok(jibun_raw):
    """'645-1전' → ('645-1', '전'). lsmd_cont_ldreg 는 지목 컬럼이 없고 jibun 끝에 한글로 붙는다."""
    s = (jibun_raw or "").strip()
    if not s:
        return "", ""
    i = len(s)
    while i > 0 and "가" <= s[i - 1] <= "힣":
        i -= 1
    return (s[:i].strip(), s[i:].strip()) if i < len(s) else (s, "")


def _rows_to_parcels(cur) -> List[Dict]:
    cols = [d[0] for d in cur.description]
    out = []
    for row in cur.fetchall():
        rec = dict(zip(cols, row))
        pnu = rec.get("pnu") or ""
        # lsmd_cont_ldreg 에 jimok 컬럼 없음 → jibun('645-1전')에서 지목 분리
        jibun, jimok = _split_jibun_jimok(rec.get("jibun"))
        out.append({
            "pnu": pnu,
            "jibun": jibun,
            "jimok": jimok,
            "area_sqm": float(rec.get("area_sqm") or 0),
            "geom_wkt": rec.get("geom_wkt") or "",
            "is_mountain": (len(pnu) == 19 and pnu[10] == "2"),
            "is_neighbor": False,
            "source": rec.get("source") or "lsmd_cont_ldreg",
        })
    return out


def _bnd_exists(cur, kind: str, code: str) -> bool:
    sql = queries.RI_EXISTS if kind == "ri" else queries.EMD_EXISTS
    cur.execute(sql.format(schema=db_env.DB_SCHEMA), (code,))
    return cur.fetchone() is not None


def _query_db_by_pnus(cur, pnus: List[str]) -> List[Dict]:
    """PNU 목록(모두 19자리)으로 필지를 조회한다 — 소속 법정경계별로 묶어 GiST 가속.

    `lsmd_cont_ldreg` 에 pnu 인덱스가 없어(gid PK + geom GiST 뿐) `WHERE pnu = ...` 는
    7,780만 행 전수 스캔이다. 경계 도형 bbox 로 먼저 좁히면 같은 결과를 1초 안에 얻는다.

    ⚠ **경계가 존재하는데 0행**이면 그 필지는 없는 것이다 — 전수 스캔으로 재확인하지
      않는다. 그러지 않으면 `_pnu_candidates` 가 만드는 임야('산') 후보처럼 **애초에
      없는 후보 하나 때문에 매 조회가 수십 초씩 걸린다.**
      전수 스캔은 경계 자체를 못 찾았을 때(코드 역산 실패·bnd 미수록)만 쓴다.
    """
    pnus = [p for p in (pnus or []) if p]
    if not pnus:
        return []

    by_bnd = {}
    slow: List[str] = []
    for p in pnus:
        key = _bnd_key(p)
        if key is None:
            slow.append(p)
        else:
            by_bnd.setdefault(key, []).append(p)

    out: List[Dict] = []
    if by_bnd:
        # 가속 경로(경계 bbox → GiST)에도 트랜잭션 + SET LOCAL statement_timeout 을 두른다.
        # 경계 존재를 확인해도 그 경계 도형(bnd_*) 품질 문제가 있으면 ST_Union 단계에서
        # 멈출 수 있다(Codex R6) — 느린 경로에만 상한이 있던 것을 맞춘다.
        try:
            cur.execute("BEGIN")
            cur.execute("SET LOCAL statement_timeout = '20s'")
            for (kind, code), group in by_bnd.items():
                if not _bnd_exists(cur, kind, code):
                    slow.extend(group)          # 법정경계 미수록 — 도형으로 좁힐 수가 없다
                    continue
                sql = queries.PARCEL_BY_PNU_IN_RI if kind == "ri" else queries.PARCEL_BY_PNU_IN_EMD
                cur.execute(sql.format(schema=db_env.DB_SCHEMA), (code, group))
                out.extend(_rows_to_parcels(cur))
            cur.execute("COMMIT")
        except Exception as e:
            try:
                cur.execute("ROLLBACK")
            except Exception:
                pass
            _log(f"필지 조회(가속 경로) 실패·시간초과({len(by_bnd)}개 경계): {e}")

    if slow:
        # 느린 경로(전수 스캔). 상한을 걸어 UI 무한 멈춤을 막고, 실패는 로그에 남긴다.
        try:
            cur.execute("BEGIN")
            cur.execute("SET LOCAL statement_timeout = '20s'")
            cur.execute(queries.PARCEL_BY_PNU.format(schema=db_env.DB_SCHEMA), (slow,))
            out.extend(_rows_to_parcels(cur))
            cur.execute("COMMIT")
        except Exception as e:
            try:
                cur.execute("ROLLBACK")
            except Exception:
                pass
            _log(f"필지 조회 실패·시간초과({len(slow)}건, 법정경계 미수록 → 전수 스캔): {e}")
    return out


NEIGHBOR_LIMIT = 200


def _query_neighbors(cur, exclude_pnus: List[str], target_wkt: str, radius_m: int) -> List[Dict]:
    """대상 도형(WKT, 5186) 반경 내 인접 필지 — ST_DWithin(GiST 인덱스 가속).

    2단계다. ① 반경 안 **PNU 목록**을 거리순으로 받고 ② 그 PNU 로 **전체 도형**을 다시
    가져온다. ①에서 도형을 같이 집계하면 멀티파트 필지의 반경 밖 조각이 탈락해
    면적이 과소 계산된다.

    무거울 수 있어 트랜잭션 내 SET LOCAL statement_timeout(PgBouncer transaction pooling
    호환, 시작 파라미터 options 는 거부됨)으로 상한 → 최악의 경우에도 UI 무한 멈춤 방지.
    인접은 보조 시각화라 실패 시 빈 목록으로 본 흐름을 진행하되, **조용히 넘어가지 않고**
    로그에 남긴다(옛 `except: return []` 는 타임아웃·SQL 오류·연결 끊김을 전부
    '인접 0건'으로 위장했다).
    """
    if not target_wkt or not radius_m:
        return []
    try:
        cur.execute("BEGIN")
        cur.execute("SET LOCAL statement_timeout = '20s'")
        cur.execute(
            queries.NEIGHBOR_PNUS_BY_GEOM.format(schema=db_env.DB_SCHEMA),
            # 상한보다 1건 더 요청한다 — 딱 상한만큼 존재하는 경우를 '잘렸다'고 오보하지 않으려면
            # 초과분이 실제로 있는지 봐야 한다.
            (target_wkt, db_env.DB_SRID, list(exclude_pnus), radius_m, NEIGHBOR_LIMIT + 1),
        )
        hits = cur.fetchall()
        cur.execute("COMMIT")
    except Exception as e:
        try:
            cur.execute("ROLLBACK")
        except Exception:
            pass
        _log(f"인접필지 조회 실패(반경 {radius_m}m): {e} — 인접 없이 진행합니다.")
        return []

    truncated = len(hits) > NEIGHBOR_LIMIT
    if truncated:
        hits = hits[:NEIGHBOR_LIMIT]
        _log(f"인접필지가 {NEIGHBOR_LIMIT}건을 넘어 가까운 순으로 잘렸습니다"
             f"(반경 {radius_m}m). 반경을 줄이면 전부 볼 수 있습니다.")

    dist_by_pnu = {str(r[0]): float(r[1]) for r in hits}
    if not dist_by_pnu:
        return []
    rows = _query_db_by_pnus(cur, list(dist_by_pnu))
    for r in rows:
        r["is_neighbor"] = True
        r["distance_m"] = dist_by_pnu.get(r["pnu"])
        r["neighbor_truncated"] = truncated   # 조용한 절단 금지 — controller 가 화면에 알린다
    rows.sort(key=lambda r: r.get("distance_m") or 0.0)
    return rows


def _norm_api_row(vw: Dict) -> Dict:
    """V-World 응답 dict 표준화 — is_neighbor·is_mountain·면적 보강.

    V-World data API 는 면적 미제공 → 도형(5186, ㎡) 면적으로 채운다(편입률 분모=공부면적 근사).
    """
    vw = dict(vw)
    vw.setdefault("is_neighbor", False)
    p = vw.get("pnu") or ""
    vw["is_mountain"] = (len(p) == 19 and p[10] == "2")
    if _HAS_QGIS and not vw.get("area_sqm") and vw.get("geom_wkt"):
        try:
            g = QgsGeometry.fromWkt(vw["geom_wkt"])
            if g and not g.isEmpty():
                vw["area_sqm"] = float(g.area())
        except Exception:
            pass
    return vw


def _api_fetch(point, cands: List[str]) -> List[Dict]:
    """V-World data API 로 필지 1건 취득(연속지적 도형·속성).

    **사용자 2단계 방식 = geomfilter=POINT**(search 대표점) 우선 → 미스 시 attrFilter=pnu 폴백
    (point 가 오목 폴리곤 밖이거나 미확보 시 — 1~2단계 §2-③ 폴백 설계).
    """
    if point and point[0] is not None and point[1] is not None:
        vw = vworld_fallback.lookup_parcel_by_point(point[0], point[1])
        if vw:
            return [_norm_api_row(vw)]
    for c in cands:
        vw = vworld_fallback.lookup_parcel(c)
        if vw:
            return [_norm_api_row(vw)]
    return []


# ─────────────────────────────────────────────────────────────────────────
# Public API
# ─────────────────────────────────────────────────────────────────────────

def _miss_row(parsed_item: Dict, reason: str = "") -> Dict:
    """찾지 못한 한 줄. `reason` 이 있으면 사용자에게 왜 못 찾았는지 알려 준다."""
    return {
        "pnu": None, "jibun": parsed_item.get("raw", ""), "jimok": "",
        "area_sqm": 0.0, "geom_wkt": "", "is_mountain": False,
        "is_neighbor": False, "miss": True, "reason": reason, "source": None,
    }


def lookup(jibun_text: str, radius_m: int = 0) -> List[Dict]:
    """지번 텍스트를 받아 필지 목록을 반환한다.

    Returns:
        [{pnu, jibun, jimok, area_sqm, geom_wkt, is_mountain, is_neighbor, source}, ...]
    """
    parsed = _parse_input(jibun_text)
    if not parsed:
        return []
    has_key = vworld_fallback.is_available()
    api_first = config.resolve_use_api(has_key)   # 키 있으면 AUTO→API (v2 §6.1)

    conn = queries.connect()
    try:
        with conn.cursor() as cur:
            target_pnus: List[str] = []
            results: List[Dict] = []
            for p in parsed:
                rows: List[Dict] = []
                cands: List[str] = []
                point = None   # search 대표점(5186) — 2단계 geomfilter=POINT 용
                if "pnu" in p:
                    cands = [p["pnu"]]
                else:
                    # 1단계 — 지번 텍스트 → PNU. 키 있으면 V-World **search API** 1순위
                    # (주소 정규화 → id=PNU + 대표점, 가장 견고). 없으면 사내 DB(bnd_*) 본번/부번 폴백.
                    raw = p.get("raw") or p.get("name") or ""
                    if raw and has_key:
                        sr = vworld_fallback.search_parcel(raw)
                        if sr and sr.get("pnu"):
                            cands = [sr["pnu"]]
                            point = (sr.get("point_x"), sr.get("point_y"))   # 5186
                    if not cands and "name" in p:
                        try:
                            prefix = _resolve_prefix(cur, p["name"], p.get("tokens"))
                        except AmbiguousRegion as e:
                            # 이 줄만 미발견으로 남기고 계속한다. 예외를 올리면 여러 줄
                            # 일괄 조회에서 **앞서 성공한 지번까지 통째로 버려진다.**
                            results.append(_miss_row(p, reason=str(e)))
                            continue
                        if prefix:
                            cands = _pnu_candidates(prefix, p["bon"], p["bu"], p.get("mountain", False))

                # 2단계 — 도형·속성 취득. 출처 우선순위(config.DATA_SOURCE / 키 유무).
                # API 경로 = search 대표점으로 geomfilter=POINT(사용자 방식) → attrFilter=pnu 폴백.
                if api_first and has_key:
                    rows = _api_fetch(point, cands)
                    if not rows:
                        rows = _lookup_db(cur, p, cands)
                else:
                    rows = _lookup_db(cur, p, cands)
                    if not rows and has_key:
                        rows = _api_fetch(point, cands)

                if not rows:
                    results.append(_miss_row(p))
                    continue
                for r in rows:
                    if r.get("pnu"):
                        target_pnus.append(r["pnu"])
                results.extend(rows)

            # 반경 옵션 — 각 대상 PNU 인접 필지(보조 시각화, DB 전용, 대상 도형 기준 ST_DWithin).
            # 이미 담긴 필지 전부를 제외 목록으로 넘긴다 — 그래야 다른 **본 필지**가
            # 서로의 인접필지로 잡히지 않는다(옛 코드는 자기 자신 1건만 제외).
            if radius_m and target_pnus:
                geom_by_pnu = {r["pnu"]: r.get("geom_wkt") for r in results if r.get("pnu")}
                seen = set(geom_by_pnu)
                for pnu in target_pnus:
                    for n in _query_neighbors(cur, list(seen), geom_by_pnu.get(pnu), radius_m):
                        if n["pnu"] not in seen:
                            results.append(n)
                            seen.add(n["pnu"])
            return results
    finally:
        conn.close()


def lookup_by_pnu(pnu: str, point=None, radius_m: int = 0) -> List[Dict]:
    """확정된 PNU(+대표점)로 필지 취득 — 1단계 검색 후보 선택 경로.

    point(5186) 있으면 API geomfilter=POINT 우선(사용자 방식) → attrFilter=pnu → DB 폴백.
    """
    if not pnu:
        return []
    has_key = vworld_fallback.is_available()
    api_first = config.resolve_use_api(has_key)
    conn = queries.connect()
    try:
        with conn.cursor() as cur:
            if api_first and has_key:
                rows = _api_fetch(point, [pnu]) or _query_db_by_pnus(cur, [pnu])
            else:
                rows = _query_db_by_pnus(cur, [pnu])
                if not rows and has_key:
                    rows = _api_fetch(point, [pnu])
            results = list(rows)
            if radius_m and rows and rows[0].get("pnu"):
                tgt_wkt = rows[0].get("geom_wkt")
                seen = {r["pnu"] for r in results if r.get("pnu")}
                for n in _query_neighbors(cur, list(seen), tgt_wkt, radius_m):
                    if n["pnu"] not in seen:
                        results.append(n)
                        seen.add(n["pnu"])
            if not results:
                results = [{
                    "pnu": pnu, "jibun": "", "jimok": "", "area_sqm": 0.0,
                    "geom_wkt": "", "is_mountain": (len(pnu) == 19 and pnu[10] == "2"),
                    "is_neighbor": False, "miss": True, "source": None,
                }]
            return results
    finally:
        conn.close()


def _lookup_db(cur, parsed_item: Dict, cands: List[str]) -> List[Dict]:
    """DB(lsmd_cont_ldreg) 조회 — PNU 후보 전체를 한 번에 묻고 후보 우선순위대로 고른다.

    후보는 `_pnu_candidates` 가 산(임야) 여부 우선순위로 정렬해 준다.
    """
    if "pnu" in parsed_item:
        return _query_db_by_pnus(cur, [parsed_item["pnu"]])
    if not cands:
        return []
    rows = _query_db_by_pnus(cur, cands)
    by_pnu = {r["pnu"]: r for r in rows}
    for c in cands:                    # 산여부 우선순위 보존
        if c in by_pnu:
            return [by_pnu[c]]
    return []


def add_to_canvas(parcels: List[Dict], owners: List[Dict], *,
                  project, main_layer_name: str = "선택필지",
                  neighbor_layer_name: str = "인접필지",
                  radius_m: int = 0,
                  manual_names: Dict[str, str] = None) -> Tuple[Optional[object], Optional[object]]:
    """필지 결과를 QGIS 캔버스 메모리 레이어로 추가한다(동명 레이어 교체).

    manual_names: {pnu: 소유자명} — 다중 지번 누적 시 레이어 교체 전 기존 '선택필지'
    레이어에서 수확한 수동 입력값을 새 레이어에 다시 채워 넣는다(R6, 회귀 방지).
    """
    if not _HAS_QGIS or project is None:
        return None, None

    owner_by_pnu = {o.get("pnu"): o for o in (owners or [])}

    main = [p for p in parcels if not p.get("is_neighbor") and p.get("geom_wkt")]
    neigh = [p for p in parcels if p.get("is_neighbor") and p.get("geom_wkt")]

    main_layer = _make_parcel_layer(main, main_layer_name, owner_by_pnu,
                                     editable_owner=True, manual_names=manual_names)
    neighbor_layer = None
    if radius_m and neigh:
        neighbor_layer = _make_parcel_layer(neigh, neighbor_layer_name, owner_by_pnu, editable_owner=False)

    # 소유 마커가 있는 레이어만 교체한다 — 사용자가 만든 동명 레이어 보호(F2).
    layers.replace_owned(project, main_layer_name, main_layer)
    # neighbor_layer 가 None 이어도 항상 교체 호출 — 그래야 이전 호출에서 만들어진
    # '인접필지' 레이어가 이번 재구성(누적 병합 후 반경 옵션이 꺼진 경우 등)에서 안 남는다.
    layers.replace_owned(project, neighbor_layer_name, neighbor_layer)

    return main_layer, neighbor_layer


def _make_parcel_layer(parcels: List[Dict], name: str, owner_by_pnu: Dict,
                       editable_owner: bool = True, manual_names: Dict[str, str] = None):
    """메모리 폴리곤 레이어 + 필드 alias. `owner_name` 은 manual_names 에 값이 있으면
    그 값을, 없으면 빈 칸(설계자가 속성테이블에서 입력)으로 채운다."""
    manual_names = manual_names or {}
    layer = QgsVectorLayer(f"MultiPolygon?crs=EPSG:{db_env.DB_SRID}", name, "memory")
    pr = layer.dataProvider()
    pr.addAttributes([
        QgsField("pnu", QVariant.String),
        QgsField("jibun", QVariant.String),
        QgsField("jimok", QVariant.String),
        QgsField("area_sqm", QVariant.Double),
        QgsField("owner_type", QVariant.String),
        QgsField("owner_name", QVariant.String),   # 수동 입력 슬롯 (v2 §6.2)
        QgsField("is_mountain", QVariant.Bool),
        QgsField("is_neighbor", QVariant.Bool),
    ])
    layer.updateFields()
    feats = []
    for p in parcels:
        # 도형 먼저 검증 — fromWkt 실패/빈 도형이면 피처를 추가하지 않는다(빈 도형 '추가됨' 오보 방지).
        geom = None
        try:
            g = QgsGeometry.fromWkt(p.get("geom_wkt") or "")
            if g and not g.isNull() and not g.isEmpty():
                g.convertToMultiType()   # 단일 POLYGON(API 폴백 등) → MultiPolygon 레이어 타입 정합
                geom = g
        except Exception:
            geom = None
        if geom is None:
            continue
        f = QgsFeature(layer.fields())
        owner = owner_by_pnu.get(p.get("pnu"), {})
        manual_name = manual_names.get(p.get("pnu")) or ""   # 빈 값/None 은 덮어쓰지 않음(R6)
        f.setAttributes([
            p.get("pnu", ""),
            p.get("jibun", ""),
            p.get("jimok", ""),
            book_area(p),
            owner.get("owner_type", ""),
            manual_name,                         # owner_name — 수동 입력 보존 또는 빈칸
            bool(p.get("is_mountain")),
            bool(p.get("is_neighbor")),
        ])
        f.setGeometry(geom)
        feats.append(f)
    if feats:
        pr.addFeatures(feats)
    layer.updateExtents()
    for i, fld in enumerate(layer.fields()):
        a = PARCEL_FIELD_ALIASES.get(fld.name())
        if a:
            layer.setFieldAlias(i, a)
    # 삽도·캔버스에서 구분되도록 명시 심볼(기본 랜덤 채움 대체)
    if editable_owner:
        symbology.style_parcel(layer)
        symbology.label_parcel_canvas(layer)   # 캔버스에 지번 라벨 표시(본 필지만)
    else:
        symbology.style_neighbor(layer)
    return layer
