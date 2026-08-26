# -*- coding: utf-8 -*-
"""M2: 토지소유 **구분**(개인/국유/공유) 수집 — 성명은 자동 불가(수동).

v2 §6.2 / 1~2단계 설계검토 부록 B 결정:
  소유자 **성명·주소·지분은 V-World API·공공데이터·사내 DB(al_d160) 전부 미제공**
  (개인정보) → "소유구분"까지만 자동. 성명은 설계자가 토지대장/등기에서 확인 후
  '선택필지' 레이어 `owner_name` 칸에 **직접 입력**한다.

따라서 본 모듈은 `al_d160` 에서 **소유구분(a8)만** 가져온다. 라벨(a21)에서 이름을
추출·마스킹하던 v0.2 로직은 **제거**(PII 위생 + 정책 정합).
"""
from typing import List, Dict, Optional

from .. import db_env
from ..db import queries


# 소유구분 코드/문자열 정규화(표시용) — al_d160 a8 값이 코드일 때 대비
_OWNER_TYPE_NORMALIZE = {
    "1": "국유", "2": "공유", "3": "사유", "4": "법인", "5": "비법인",
    "6": "기타", "8": "외국인", "9": "종중",
}


def normalize_owner_type(v: Optional[str]) -> str:
    """소유구분 표시 문자열. 이미 한글이면 그대로, 코드면 매핑."""
    if v is None:
        return ""
    s = str(v).strip()
    if not s:
        return ""
    return _OWNER_TYPE_NORMALIZE.get(s, s)


def collect(pnu_list: List[Optional[str]]) -> List[Dict]:
    """PNU 목록에 대한 소유구분 정보를 반환한다(성명 없음).

    Returns:
        [{pnu, owner_type, jimok, area_sqm, source}, ...]
        al_d160 에 없는 PNU 는 빈 owner_type sentinel(수동 확인 안내).
    """
    pnus = [p for p in (pnu_list or []) if p]
    if not pnus:
        return []
    conn = queries.connect()
    try:
        with conn.cursor() as cur:
            cur.execute(queries.OWNERS_BY_PNU.format(schema=db_env.DB_SCHEMA), (pnus,))
            cols = [d[0] for d in cur.description]
            out = []
            for row in cur.fetchall():
                rec = dict(zip(cols, row))
                out.append({
                    "pnu": rec.get("pnu"),
                    "owner_type": normalize_owner_type(rec.get("owner_type")),
                    "area_sqm": float(rec.get("area_sqm") or 0),
                    "source": "al_d160",
                })
    finally:
        conn.close()
    # al_d160 미수록 PNU → 빈 sentinel (소유구분 미상 — 설계자 확인)
    seen = {o.get("pnu") for o in out}
    for pnu in pnus:
        if pnu not in seen:
            out.append({
                "pnu": pnu, "owner_type": "", "jimok": "", "area_sqm": 0.0,
                "source": "manual",
            })
    return out


def mask_name(name: Optional[str]) -> str:
    """소유자명 마스킹(결재 첨부 직전 토글용): '홍길동'→'홍**', '김철'→'김*', 빈값→''.

    성명은 수동 입력값(owner_name)에만 적용 — 자동 수집 데이터엔 이름이 없다.
    """
    if not name:
        return ""
    s = str(name).strip()
    if len(s) <= 1:
        return s
    return s[0] + "*" * (len(s) - 1)
