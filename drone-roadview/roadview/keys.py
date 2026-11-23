"""DH.Grading 과 같은 VWorld 키 쓰기 (키는 찍지 않는다).

순서 (그레이딩 VWorldKey.Current 와 같음):
  1) 내 키   — %APPDATA%\\DH.Grading\\vworld.json «key» (Windows DPAPI · 이 사용자만 풂)
  2) 설치 키 — DH.Grading 설치 폴더 dh.keys.json «vworld»
"""
from __future__ import annotations

import base64
import json
import os
import re

_ENTROPY = b'DH.Grading.VWorldKey.v1'
_KEY_RE = re.compile(r'^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$')


def _unprotect(b64: str, entropy: bytes) -> str:
    import ctypes
    from ctypes import wintypes

    class BLOB(ctypes.Structure):
        _fields_ = [('cbData', wintypes.DWORD), ('pbData', ctypes.POINTER(ctypes.c_char))]

    data = base64.b64decode(b64)
    bin_ = ctypes.create_string_buffer(data, len(data))
    bent = ctypes.create_string_buffer(entropy, len(entropy))
    bi = BLOB(len(data), ctypes.cast(bin_, ctypes.POINTER(ctypes.c_char)))
    be = BLOB(len(entropy), ctypes.cast(bent, ctypes.POINTER(ctypes.c_char)))
    bo = BLOB()
    f = ctypes.windll.crypt32.CryptUnprotectData
    f.argtypes = [ctypes.POINTER(BLOB), ctypes.c_void_p, ctypes.POINTER(BLOB), ctypes.c_void_p, ctypes.c_void_p,
                  wintypes.DWORD, ctypes.POINTER(BLOB)]
    f.restype = wintypes.BOOL
    if not f(ctypes.byref(bi), None, ctypes.byref(be), None, None, 0, ctypes.byref(bo)):
        raise OSError('DPAPI')
    try:
        return ctypes.string_at(bo.pbData, bo.cbData).decode('utf-8').strip()
    finally:
        ctypes.windll.kernel32.LocalFree(bo.pbData)


def vworld_key():
    """→ (키, 출처) — 없으면 ('', '키 없음')."""
    appd = os.environ.get('APPDATA', '')
    p = os.path.join(appd, 'DH.Grading', 'vworld.json')
    if os.path.exists(p):
        try:
            enc = json.load(open(p, encoding='utf-8-sig')).get('key', '')
            if enc:
                k = _unprotect(enc, _ENTROPY)
                if _KEY_RE.match(k):
                    return k, '내 키'
        except Exception:
            pass
    p = os.path.join(appd, 'Autodesk', 'ApplicationPlugins', 'DH.Grading.bundle', 'Contents', 'dh.keys.json')
    if os.path.exists(p):
        try:
            k = (json.load(open(p, encoding='utf-8-sig')).get('vworld') or '').strip()
            if _KEY_RE.match(k):
                return k, '설치 키'
        except Exception:
            pass
    return '', '키 없음'
