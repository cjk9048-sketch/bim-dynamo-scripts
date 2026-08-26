# -*- coding: utf-8 -*-
"""V-World OpenAPI 키 — 코드에 박지 않고 **사내 키 엔드포인트에서 런타임 취득**.

1~2단계 설계검토 §2-④ / 절대 룰 #4 정합 (2026-06-11 개선):
  - 키를 코드/zip 에 평문으로 넣지 않는다. 사내 키 발급 API 에서 받아온다.
      GET https://pgm.dohwa.co.kr/api/key/DHMapDownloader → JSON 의 `apiKey` 필드.
  - 우선순위: QSettings(수동 override) → 프로젝트 .env(개발) → **사내 엔드포인트**.
  - 사내망/VPN 에서만 엔드포인트 도달. 미도달 시 빈 키 반환 → 호출 측이 DB 경로로 폴백.
  - 키 회전·만료(dueDate)는 엔드포인트가 SoT — 플러그인 재배포 없이 키 갱신 반영.

키가 없을 때는 절대 예외를 올리지 않고 빈 문자열을 반환 → 호출 측이 DB 경로로 폴백.
"""
import json
import os
import time
import urllib.request

_QSETTINGS_GROUP = "minwon_review"
_KEY_NAME = "vworld_key"
_DOMAIN_NAME = "vworld_domain"


# ── QSettings (배포 환경 1순위) ─────────────────────────────────────

def _qsettings():
    try:
        from qgis.PyQt.QtCore import QSettings
        return QSettings()
    except Exception:
        return None


def _qget(name: str) -> str:
    s = _qsettings()
    if s is None:
        return ""
    v = s.value(f"{_QSETTINGS_GROUP}/{name}", "")
    return str(v).strip() if v else ""


def _qset(name: str, value: str) -> None:
    s = _qsettings()
    if s is None:
        return
    s.setValue(f"{_QSETTINGS_GROUP}/{name}", value or "")


# ── .env 폴백 (개발 환경) ───────────────────────────────────────────

def _env_value(key: str) -> str:
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


# ── 사내 키 엔드포인트 (코드/zip 에 키 미포함 — 런타임 취득) ─────────
# GET → {"apiNm":"DHMapDownloader","apiKey":"...","dueDate":"2026-09-30","url":"www.vworld.kr", ...}
_KEY_ENDPOINT = "https://pgm.dohwa.co.kr/api/key/DHMapDownloader"
_HTTP_TIMEOUT = 8
_FAIL_COOLDOWN = 60.0                       # 실패 후 재시도까지(매 동작 8s 행 방지)
_endpoint_cache = {"key": "", "last_fail": 0.0}   # 세션 메모(QGIS 재시작 시 재취득)


def _fetch_key_from_endpoint() -> str:
    """사내 키 엔드포인트에서 V-World apiKey 취득(세션 캐시). 실패 시 ''(쿨다운 적용)."""
    if _endpoint_cache["key"]:
        return _endpoint_cache["key"]
    if (time.time() - _endpoint_cache["last_fail"]) < _FAIL_COOLDOWN:
        return ""                            # 직전 실패 쿨다운 내 — 재시도 생략
    try:
        req = urllib.request.Request(_KEY_ENDPOINT, headers={"User-Agent": "landuse_review"})
        with urllib.request.urlopen(req, timeout=_HTTP_TIMEOUT) as resp:
            data = json.loads(resp.read().decode("utf-8", "replace"))
        key = str(data.get("apiKey") or "").strip()
        if key:
            _endpoint_cache["key"] = key
            return key
        _endpoint_cache["last_fail"] = time.time()
        return ""
    except Exception:
        _endpoint_cache["last_fail"] = time.time()   # 사내망 밖·장애 → DB 폴백
        return ""


def clear_key_cache() -> None:
    """세션 키 캐시 비우기(키 회전 후 강제 재취득용)."""
    _endpoint_cache["key"] = ""
    _endpoint_cache["last_fail"] = 0.0


# ── Public API ──────────────────────────────────────────────────────

def get_api_key() -> str:
    """V-World API 키 — QSettings 1순위 → .env → **사내 키 엔드포인트**(pgm.dohwa.co.kr)."""
    return _qget(_KEY_NAME) or _env_value("VWORLD_API_KEY") or _fetch_key_from_endpoint()


def set_api_key(value: str) -> None:
    """수동 키를 QSettings 에 저장(엔드포인트 override — 특수 상황용)."""
    _qset(_KEY_NAME, (value or "").strip())


def get_domain() -> str:
    """V-World 도메인 — QSettings → .env → 빈값(이 키는 도메인 비움으로 동작 확인)."""
    return _qget(_DOMAIN_NAME) or _env_value("VWORLD_DOMAIN") or ""


def set_domain(value: str) -> None:
    _qset(_DOMAIN_NAME, (value or "").strip())


def has_key() -> bool:
    """API 보강 사용 가능 여부(= 키 보유)."""
    return bool(get_api_key())
