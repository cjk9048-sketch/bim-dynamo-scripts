# -*- coding: utf-8 -*-
"""DB 접속 설정.

개발 시 프로젝트 루트 .env 또는 ~/.qgis_axteam.env 에서 읽고,
배포(zip) 시 pack 스크립트가 아래 마커 사이를 하드코딩 버전으로 교체합니다.
플러그인은 읽기전용 계정(waterviewer)만 사용합니다 — 절대 룰 #5.
운영 DB(PROD)는 EPSG:5186, port 6432(PgBouncer, 가벼운 조회 전용)입니다.
"""
import os


def _from_env():
    """개발 환경: .env 로딩 시도 (없으면 None 반환)."""
    candidates = []
    here = os.path.dirname(__file__)
    # src/landuse_review/../../../ 쯤에 repo 루트가 있을 수 있음
    for up in range(1, 6):
        candidates.append(os.path.join(here, *([".."] * up), ".env"))
    candidates.append(os.path.expanduser("~/.qgis_axteam.env"))
    for path in candidates:
        if os.path.isfile(path):
            env = {}
            with open(path, "r", encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if not line or line.startswith("#") or "=" not in line:
                        continue
                    k, v = line.split("=", 1)
                    env[k.strip()] = v.strip().strip('"').strip("'")
            if env.get("DB_HOST"):
                return env
    return None


# === DB_ENV_START ===
DB_HOST = "geo-spatial-hub-prod.postgres.database.azure.com"
DB_PORT = "6432"
DB_NAME = "dde-water"
DB_SCHEMA = "public"
DB_USER = "waterviewer"
DB_PASSWORD = "water123!@#"
DB_GEOM_COLUMN = "geom"
DB_SRID = 5186
# === DB_ENV_END ===

# 개발 환경에서 .env가 있으면 덮어쓰기 (배포 zip에는 .env 없음 → 위 하드코딩 사용)
_dev = _from_env()
if _dev:
    DB_HOST = _dev.get("DB_HOST", DB_HOST)
    DB_PORT = _dev.get("DB_PORT", DB_PORT)
    DB_NAME = _dev.get("DB_NAME", DB_NAME)
    DB_SCHEMA = _dev.get("DB_SCHEMA", DB_SCHEMA)
    # 개발 시에도 플러그인 권한 검증용으로 waterviewer 우선 — 명시 지정 시만 변경
    DB_USER = _dev.get("DB_USER_PLUGIN", DB_USER)
    DB_PASSWORD = _dev.get("DB_PASSWORD_PLUGIN", DB_PASSWORD)
