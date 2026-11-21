"""새 판 확인 위치(dh.keys.json «update»)를 설치 묶음에만 넣는다 — mkbundle.py(설치 exe) · mkzip.py(수동 설치 zip)가 같이 쓴다.

★위치 값은 저장소에 안 남는다: 원본 src/…/Contents/dh.keys.json 은 그대로 두고, 묶음에 들어가는 사본만 고친다.

  python tools/setup/mkbundle.py --update \\\\JACK-PC\\DH정지\\latest.json [--update https://…/latest.json]
  (또는 이 PC에 한 번만: 환경 변수 DH_GRADING_UPDATE = "\\\\JACK-PC\\DH정지\\latest.json;https://…" — 세미콜론으로 여럿)
  --update 를 주면 원본에 있던 update 를 <b>바꾼다</b>(덧붙이지 않음). 아무것도 안 주면 원본 그대로.
"""
import argparse
import io
import json
import os
import re
import sys


def valid(s):
    s = s.strip()
    return bool(re.match(r"^https?://", s, re.I)) or s.startswith("\\\\") or bool(re.match(r"^[A-Za-z]:[\\/]", s))


def locations(argv=None):
    """--update 값(여러 번) → 목록. 없으면 환경 변수 DH_GRADING_UPDATE. 둘 다 없으면 None(원본 그대로)."""
    ap = argparse.ArgumentParser(add_help=False)
    ap.add_argument("--update", action="append", default=None)
    a, _ = ap.parse_known_args(argv)
    raw = a.update
    if raw is None:
        env = os.environ.get("DH_GRADING_UPDATE", "").strip()
        raw = [x for x in env.split(";")] if env else None
    if raw is None:
        return None
    locs = []
    for x in raw:
        x = x.strip()
        if not x:
            continue
        if not valid(x):
            raise SystemExit("업데이트 위치가 아닙니다(\\\\서버\\공유\\latest.json · C:\\… · https://…만): %s" % x)
        if x.lower() not in [y.lower() for y in locs]:
            locs.append(x)
    return locs


def merged_keys(src_path, locs):
    """원본 dh.keys.json + update=locs → 묶음에 넣을 바이트(UTF-8). locs가 None이면 원본 그대로."""
    raw = io.open(src_path, "rb").read()
    if locs is None:
        return raw
    d = json.loads(raw.decode("utf-8-sig"))
    if not isinstance(d, dict):
        raise SystemExit("dh.keys.json 맨 위가 { } 가 아닙니다")
    d["update"] = locs
    return (json.dumps(d, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def report(data):
    """묶음에 들어간 update 개수를 알린다(값은 안 찍음)."""
    try:
        n = json.loads(data.decode("utf-8-sig")).get("update")
        n = 1 if isinstance(n, str) and n.strip() else len(n) if isinstance(n, list) else 0
    except Exception:
        n = 0
    try:
        sys.stdout.reconfigure(errors="replace")
    except Exception:
        pass
    if n:
        print("업데이트 위치 %d곳 — 설치 묶음에만(원본 dh.keys.json · 저장소는 그대로)" % n)
    else:
        print("! 업데이트 위치 없음 — 받은 사람에게 새 판 알림이 안 뜹니다(--update 또는 DH_GRADING_UPDATE)")
    return n
