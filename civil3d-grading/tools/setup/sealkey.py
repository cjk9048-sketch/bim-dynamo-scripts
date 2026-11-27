"""설치 키 파일(dh.keys.json)의 한 칸에 값을 암호문으로 넣는다 — DH.Grading.Core.SealedText 와 같은 꼴(enc1: · AES-256-GCM · 용도 = 칸 이름).

  python tools/setup/sealkey.py kakao <값이 든 파일>   # 파일 첫 줄을 암호문으로 넣고 · 되풀어 같은지 알림

★값은 화면 · 로그에 안 찍는다(길이만). 원본 dh.keys.json 은 저장소 밖(.gitignore) — 이 도구도 그 파일만 고친다.
★한계: 푸는 열쇠는 DLL 안에 있다(공개 소스) — 파일을 열어도 바로 안 보이게 하는 것까지.
"""
import base64
import hashlib
import io
import json
import os
import sys

from cryptography.hazmat.primitives.ciphers.aead import AESGCM

PREFIX = "enc1:"
KEY = hashlib.sha256("DH.Grading.SealedText.v1/builtin".encode("utf-8")).digest()   # SealedText.cs 와 같은 글
KEYS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                    "src", "DH.Grading.Civil", "DH.Grading.bundle", "Contents", "dh.keys.json")


def seal(plain, purpose):
    nonce = os.urandom(12)
    ct_tag = AESGCM(KEY).encrypt(nonce, plain.encode("utf-8"), purpose.encode("utf-8"))   # 암호문 + 태그 16
    return PREFIX + base64.b64encode(nonce + ct_tag).decode("ascii")


def open_(sealed, purpose):
    raw = base64.b64decode(sealed[len(PREFIX):])
    return AESGCM(KEY).decrypt(raw[:12], raw[12:], purpose.encode("utf-8")).decode("utf-8")


def main():
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    field, src = sys.argv[1], sys.argv[2]
    lines = io.open(src, encoding="utf-8-sig").read().strip().splitlines()
    plain = lines[0].strip() if lines else ""
    if not plain:
        raise SystemExit("값이 비었습니다")
    path = os.path.normpath(KEYS)
    raw = io.open(path, encoding="utf-8-sig").read()
    d = json.loads(raw)
    d[field] = seal(plain, field)
    tmp = path[:-len(".json")] + ".tmp.json"   # 다 쓴 뒤 바꿔 끼움 — 쓰다 끊겨도 원본이 안 깨짐 · 이름이 .gitignore «dh.keys*.json»에 걸리게
    with io.open(tmp, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(d, ensure_ascii=False, indent=2) + "\n")
    os.replace(tmp, path)
    back = json.loads(io.open(path, encoding="utf-8").read())[field]
    ok = open_(back, field) == plain
    print("%s 칸 = 암호문(%d자) · 값 길이 %d · 되풀기 %s · 글자 그대로 남음 %s"
          % (field, len(back), len(plain), "같음" if ok else "다름!", plain in io.open(path, encoding="utf-8").read()))
    if not ok:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
