# -*- coding: utf-8 -*-
"""
DB 접속 설정 (.env 기반)
개발 시 프로젝트 루트의 .env에서 읽고, 배포(zip) 시 pack_plugin.py가 하드코딩 버전으로 교체합니다.
"""
# === DB_ENV_START ===
DB_HOST = "geo-spatial-hub-prod.postgres.database.azure.com"
DB_PORT = "6432"
DB_NAME = "dde-water"
DB_SCHEMA = "public"
DB_USER = "waterviewer"
DB_PASSWORD = "water123!@#"
DB_GEOM_COLUMN = "geom"
DB_PK_COLUMN = "ufid"
# === DB_ENV_END ===
