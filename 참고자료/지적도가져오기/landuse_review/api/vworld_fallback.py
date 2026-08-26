# -*- coding: utf-8 -*-
"""V-World OpenAPI 보강(fallback) — 1차 소스(PROD DB) 미스 시에만 호출.

원칙: V-World·SEE:REAL은 보강용. V-World 서버 점검·장애 시에도 PROD DB 캐시로 검토 가능해야 한다
(기획서 §5.3 / 보고서 §7). API 키는 .env(VWORLD_API_KEY) 또는 환경변수 — zip에 하드코딩 금지.

이중 캐시 정책 (기획서 §5.3):
  - 세션 캐시(메모리, dict): 1회성 민원 조회 결과 — 다이얼로그(프로세스) 살아있는 동안
  - 7일 캐시(로컬 SQLite): 면적 산출 reference용 필지 geometry — TTL 7일
  - 목표 hit ratio ≥ 80%

설계 메모:
  - lookup_parcel(pnu): V-World data API(LP_PA_CBND_BUBUN, 연속지적) → {pnu, jibun, jimok, area_sqm, geom_wkt, source}
  - lookup_owner(pnu) : V-World 공개 OpenAPI는 소유자명을 제공하지 않음(개인정보) → SEE:REAL/정부24 수동확인 안내 sentinel 반환.
                        평문 소유자명을 API로 들고오지 않는다(PII 정책 R4와 정합).
  - 네트워크 실패·키 부재·파싱 실패 시 절대 예외를 올리지 않고 None/sentinel 반환 — 호출 측이 placeholder 처리.
  - 의존성: 표준 라이브러리만(urllib, json, sqlite3). requests/lxml 불필요.
"""
import json
import os
import sqlite3
import time
import urllib.parse
import urllib.request

VWORLD_DATA_URL = "https://api.vworld.kr/req/data"
VWORLD_SEARCH_URL = "https://api.vworld.kr/req/search"
# 연속지적도(필지) 데이터 ID — V-World 데이터 API
CADASTRAL_DATA_ID = "LP_PA_CBND_BUBUN"
HTTP_TIMEOUT = 8  # seconds
CACHE_TTL_SEC = 7 * 24 * 3600  # 7일

# 진단용 — 마지막 호출들에서 발생한 오류 메시지 (UI/로그에서 참조)
last_errors: list = []

# ── 세션 캐시 (메모리) ───────────────────────────────────────────────
_session_cache: dict = {}


def clear_session_cache() -> None:
    """다이얼로그 닫힐 때 controller가 호출 — 세션 캐시 비우기."""
    _session_cache.clear()


# ── API 키 / 도메인 ─────────────────────────────────────────────────

def _read_env_value(key: str) -> str:
    """환경변수 → 프로젝트 .env / ~/.qgis_axteam.env 순으로 값을 찾는다 (없으면 '')."""
    v = os.environ.get(key)
    if v:
        return v.strip()
    here = os.path.dirname(os.path.dirname(__file__))  # src/landuse_review/
    candidates = [os.path.join(here, *([".."] * up), ".env") for up in range(1, 6)]
    candidates.append(os.path.expanduser("~/.qgis_axteam.env"))
    for path in candidates:
        if not os.path.isfile(path):
            continue
        try:
            with open(path, "r", encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if not line or line.startswith("#") or "=" not in line:
                        continue
                    k, val = line.split("=", 1)
                    if k.strip() == key:
                        return val.strip().strip('"').strip("'")
        except OSError:
            continue
    return ""


def get_api_key() -> str:
    # 단일 소스: core.vworld_key (QSettings 1순위 → .env 폴백). 평문 내장 금지(룰 #4).
    try:
        from ..core import vworld_key
        return vworld_key.get_api_key()
    except Exception:
        return _read_env_value("VWORLD_API_KEY")


def get_domain() -> str:
    # V-World 키 발급 시 등록한 도메인. 미설정 시 'localhost' (개발 키 기본값).
    try:
        from ..core import vworld_key
        return vworld_key.get_domain()
    except Exception:
        return _read_env_value("VWORLD_DOMAIN") or "localhost"


def is_available() -> bool:
    """V-World 보강 사용 가능 여부 (= API 키 보유)."""
    return bool(get_api_key())


# ── 7일 SQLite 캐시 ─────────────────────────────────────────────────

def _cache_db_path() -> str:
    """캐시 SQLite 파일 경로. QGIS 프로필 디렉터리가 있으면 그 아래, 없으면 홈."""
    try:
        from qgis.core import QgsApplication
        base = QgsApplication.qgisSettingsDirPath()
        if base:
            d = os.path.join(base, "landuse_review")
            os.makedirs(d, exist_ok=True)
            return os.path.join(d, "vworld_cache.sqlite")
    except Exception:
        pass
    return os.path.join(os.path.expanduser("~"), ".landuse_review_vworld_cache.sqlite")


def _cache_conn():
    """SQLite 연결 — 호출 측이 반드시 close() 한다 (sqlite3 `with`는 commit만 하고 닫지 않음;
    Windows에서 닫지 않으면 캐시 파일이 잠긴 채 남음)."""
    conn = sqlite3.connect(_cache_db_path(), timeout=5)
    conn.execute(
        "CREATE TABLE IF NOT EXISTS vworld_cache "
        "(key TEXT PRIMARY KEY, payload TEXT, ts REAL)"
    )
    return conn


def _cache_get(key: str):
    """7일 캐시에서 조회 — 없거나 만료면 None."""
    try:
        conn = _cache_conn()
        try:
            row = conn.execute(
                "SELECT payload, ts FROM vworld_cache WHERE key = ?", (key,)
            ).fetchone()
        finally:
            conn.close()
        if not row:
            return None
        payload, ts = row
        if time.time() - float(ts) > CACHE_TTL_SEC:
            return None
        return json.loads(payload)
    except Exception:
        return None


def _cache_put(key: str, value) -> None:
    try:
        conn = _cache_conn()
        try:
            conn.execute(
                "INSERT OR REPLACE INTO vworld_cache(key, payload, ts) VALUES (?, ?, ?)",
                (key, json.dumps(value, ensure_ascii=False), time.time()),
            )
            conn.commit()
        finally:
            conn.close()
    except Exception:
        pass


def cache_hit_ratio() -> float:
    """캐시 적중률 추정 — 만료되지 않은 엔트리 수 / 전체 (모니터링용, 0~1)."""
    try:
        conn = _cache_conn()
        try:
            total = conn.execute("SELECT COUNT(*) FROM vworld_cache").fetchone()[0]
            if not total:
                return 0.0
            cutoff = time.time() - CACHE_TTL_SEC
            fresh = conn.execute(
                "SELECT COUNT(*) FROM vworld_cache WHERE ts >= ?", (cutoff,)
            ).fetchone()[0]
        finally:
            conn.close()
        return fresh / total
    except Exception:
        return 0.0


# ── GeoJSON geometry → WKT (Point/LineString/Polygon/MultiPolygon) ──

def _ring_wkt(ring) -> str:
    return "(" + ", ".join(f"{c[0]} {c[1]}" for c in ring) + ")"


def _geojson_to_wkt(geom) -> str:
    if not geom or "type" not in geom:
        return ""
    gtype = geom.get("type")
    coords = geom.get("coordinates")
    try:
        if gtype == "Point":
            return f"POINT({coords[0]} {coords[1]})"
        if gtype == "LineString":
            return "LINESTRING(" + ", ".join(f"{c[0]} {c[1]}" for c in coords) + ")"
        if gtype == "Polygon":
            return "POLYGON(" + ", ".join(_ring_wkt(r) for r in coords) + ")"
        if gtype == "MultiPolygon":
            return "MULTIPOLYGON(" + ", ".join(
                "(" + ", ".join(_ring_wkt(r) for r in poly) + ")" for poly in coords
            ) + ")"
        if gtype == "MultiLineString":
            return "MULTILINESTRING(" + ", ".join(
                "(" + ", ".join(f"{c[0]} {c[1]}" for c in line) + ")" for line in coords
            ) + ")"
    except (TypeError, IndexError):
        return ""
    return ""


# ── HTTP ────────────────────────────────────────────────────────────

def _http_get_json(url: str):
    req = urllib.request.Request(url, headers={"User-Agent": "landuse_review/0.2"})
    with urllib.request.urlopen(req, timeout=HTTP_TIMEOUT) as resp:
        raw = resp.read().decode("utf-8", errors="replace")
    return json.loads(raw)


# ── 지목(地目) 분리 — V-World jibun 문자열 꼬리의 한글 ──────────────

def _split_jibun_jimok(jibun_raw: str):
    """'123-4답' → ('123-4', '답'). 꼬리가 한글이면 지목으로 분리, 아니면 ('...', '')."""
    s = (jibun_raw or "").strip()
    if not s:
        return "", ""
    i = len(s)
    while i > 0 and "가" <= s[i - 1] <= "힣":
        i -= 1
    if i < len(s):
        return s[:i].strip(), s[i:].strip()
    return s, ""


def _parse_first_feature(data, fallback_pnu: str = ""):
    """data API GetFeature 응답 → 첫 feature 를 표준 필지 dict 로(없으면 None).

    반환: {pnu, jibun, jimok, area_sqm(0.0), geom_wkt, jiga, gosi, addr, source='vworld'}
    (면적은 V-World data API 미제공 → 0.0, 도형 기반 산출은 area_calculator.
     V-World jibun 은 '645-1전' 처럼 지목이 붙어 와 _split_jibun_jimok 으로 분리.)
    """
    resp = data.get("response", {}) or {}
    if resp.get("status") != "OK":
        last_errors.append(f"V-World 응답 status={resp.get('status')}")
        return None
    features = (resp.get("result", {}) or {}).get("featureCollection", {}).get("features", []) or []
    if not features:
        return None
    props = features[0].get("properties", {}) or {}
    geom_wkt = _geojson_to_wkt(features[0].get("geometry"))
    jibun, jimok = _split_jibun_jimok(str(props.get("jibun") or props.get("JIBUN") or ""))
    gy = str(props.get("gosi_year") or "").strip()
    gm = str(props.get("gosi_month") or "").strip()
    return {
        "pnu": str(props.get("pnu") or props.get("PNU") or fallback_pnu),
        "jibun": jibun,
        "jimok": jimok,
        "area_sqm": 0.0,            # data API 면적 미제공 → 도형 기반 산출
        "geom_wkt": geom_wkt,
        "jiga": props.get("jiga"),  # 공시지가(원/㎡)
        "gosi": f"{gy}-{gm}" if (gy or gm) else "",
        "addr": props.get("addr") or "",
        "source": "vworld",
    }


# ── Public API ──────────────────────────────────────────────────────

def lookup_parcel(pnu: str):
    """V-World 연속지적 API로 필지 1건을 조회한다 (이중 캐시 적용).

    Args:
        pnu: 필지고유번호 19자리 문자열.
    Returns:
        {pnu, jibun, jimok, area_sqm, geom_wkt, source='vworld'} 또는 None
        (키 부재 / 네트워크 실패 / 미발견 / 파싱 실패).
    """
    if not pnu:
        return None
    pnu = str(pnu).strip()
    ck = f"parcel:{pnu}"
    if ck in _session_cache:
        return _session_cache[ck]
    cached = _cache_get(ck)
    if cached is not None:
        _session_cache[ck] = cached
        return cached

    key = get_api_key()
    if not key:
        last_errors.append("V-World API 키 없음 (.env VWORLD_API_KEY) — 보강 조회 생략")
        return None

    params = {
        "service": "data",
        "version": "2.0",
        "request": "GetFeature",
        "format": "json",
        "size": "10",
        "page": "1",
        "data": CADASTRAL_DATA_ID,
        "attrFilter": f"pnu:=:{pnu}",
        "geometry": "true",
        "attribute": "true",
        "crs": "EPSG:5186",
        "domain": get_domain(),
        "key": key,
    }
    url = VWORLD_DATA_URL + "?" + urllib.parse.urlencode(params)
    try:
        data = _http_get_json(url)
    except Exception as e:  # urllib error, JSON error, timeout 등
        last_errors.append(f"V-World 호출 실패(pnu={pnu}): {e}")
        return None

    try:
        result = _parse_first_feature(data, fallback_pnu=pnu)
    except Exception as e:
        last_errors.append(f"V-World 파싱 실패(pnu={pnu}): {e}")
        return None
    if result is None:
        return None

    _session_cache[ck] = result
    _cache_put(ck, result)
    return result


def lookup_parcel_by_point(x, y, *, crs: str = "EPSG:5186"):
    """대표점으로 연속지적 1필지 조회 — **geomfilter=POINT** (사용자 2단계 방식).

    search_parcel 이 준 point 를 그대로 geomfilter 로 넣어 그 점을 포함하는 필지를 조회한다.
    crs 는 point 좌표계(= 반환 geom 좌표계). 기본 5186(DB·도면 파이프라인 정합, 무변환).

    Args:
        x, y: 대표점 좌표(crs 기준). search_parcel(crs=5186) 의 point_x/point_y.
    Returns:
        {pnu, jibun, jimok, area_sqm, geom_wkt, jiga, gosi, addr, source='vworld'} 또는 None.
    """
    if x is None or y is None:
        return None
    ck = f"point:{crs}:{float(x):.4f}:{float(y):.4f}"
    if ck in _session_cache:
        return _session_cache[ck]
    cached = _cache_get(ck)
    if cached is not None:
        _session_cache[ck] = cached
        return cached

    key = get_api_key()
    if not key:
        last_errors.append("V-World API 키 없음 — geomfilter 조회 생략")
        return None

    params = {
        "service": "data",
        "version": "2.0",
        "request": "GetFeature",
        "format": "json",
        "size": "10",
        "page": "1",
        "data": CADASTRAL_DATA_ID,
        "geomfilter": f"POINT({x} {y})",
        "geometry": "true",
        "attribute": "true",
        "crs": crs,
        "domain": get_domain(),
        "key": key,
    }
    url = VWORLD_DATA_URL + "?" + urllib.parse.urlencode(params)
    try:
        data = _http_get_json(url)
    except Exception as e:
        last_errors.append(f"V-World geomfilter 호출 실패({x},{y}): {e}")
        return None
    try:
        result = _parse_first_feature(data)
    except Exception as e:
        last_errors.append(f"V-World geomfilter 파싱 실패({x},{y}): {e}")
        return None
    if result is None:
        return None

    _session_cache[ck] = result
    _cache_put(ck, result)
    return result


def _parse_search_item(it):
    """search API item → 후보 dict. id 가 PNU(19자리)가 아니면 None."""
    pnu = str((it or {}).get("id") or "").strip()
    if not pnu.isdigit() or len(pnu) != 19:
        return None
    pt = it.get("point", {}) or {}
    def _f(v):
        try:
            return float(v)
        except (TypeError, ValueError):
            return None
    addr = it.get("address", {}) or {}
    return {
        "pnu": pnu,
        "point_x": _f(pt.get("x")),     # 5186 평면좌표 X
        "point_y": _f(pt.get("y")),     # 5186 평면좌표 Y
        "point_crs": "EPSG:5186",
        "address": addr.get("parcel") or it.get("title") or "",
        "source": "vworld_search",
    }


def search_candidates(query: str):
    """지번 텍스트 → V-World search API → **후보 목록**(동명 주소 대비).

    `type=address&category=parcel` 검색. 각 item `id`=PNU(19자리) + 대표점(5186, crs=5186 요청).
    '대전리 645-1' 처럼 전국에 동명 지번이 여럿이면 모두 반환 → 1단계 UI 에서 사용자가 선택.

    Returns: [{pnu, point_x, point_y, point_crs, address, source}, ...] (없으면 []).
    """
    if not query or not str(query).strip():
        return []
    q = str(query).strip()
    ck = f"searchN:{q}"
    if ck in _session_cache:
        return _session_cache[ck]

    key = get_api_key()
    if not key:
        last_errors.append("V-World API 키 없음 — search 생략")
        return []
    params = {
        "service": "search", "request": "search", "version": "2.0",
        "crs": "EPSG:5186", "size": "30", "page": "1",
        "query": q, "type": "address", "category": "parcel",
        "format": "json", "errorformat": "json", "key": key,
    }
    url = VWORLD_SEARCH_URL + "?" + urllib.parse.urlencode(params)
    try:
        data = _http_get_json(url)
    except Exception as e:
        last_errors.append(f"V-World search 호출 실패(q={q}): {e}")
        return []
    try:
        resp = data.get("response", {}) or {}
        if resp.get("status") != "OK":
            last_errors.append(f"V-World search status={resp.get('status')} (q={q})")
            return []
        items = (resp.get("result", {}) or {}).get("items", []) or []
        out = [c for c in (_parse_search_item(it) for it in items) if c]
    except Exception as e:
        last_errors.append(f"V-World search 파싱 실패(q={q}): {e}")
        return []
    _session_cache[ck] = out
    return out


def search_parcel(query: str):
    """지번 텍스트 → 첫 후보(PNU+대표점). 비대화형 경로(자동 lookup)용. 없으면 None."""
    cands = search_candidates(query)
    return cands[0] if cands else None


def lookup_owner(pnu: str):
    """소유정보 보강 — V-World 공개 OpenAPI는 소유자명을 제공하지 않는다(개인정보).

    SEE:REAL(부동산종합공부)·정부24는 프로그램 자동조회 대상이 아니므로, 여기서는
    네트워크 호출 없이 '수동확인 필요' sentinel을 반환한다. 평문 소유자명을 외부 API로
    들고오지 않는 것이 PII 정책(기획서 §4.3, 리스크 R4)과 정합.

    Returns:
        {pnu, owner_masked, owner_type, source='manual', note} — 항상 sentinel.
    """
    return {
        "pnu": (str(pnu).strip() if pnu else None),
        "owner_masked": "(SEE:REAL 수동확인)",
        "owner_type": "",
        "source": "manual",
        "note": "PROD DB(al_d160) 미수록 — SEE:REAL/정부24에서 설계자가 직접 확인",
    }
