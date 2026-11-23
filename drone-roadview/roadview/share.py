"""드론 로드뷰 폴더를 시놀로지 NAS(File Station)에 올린다 — civil3d-grading Web3DShare.cs 와 같은 방식.

python -m roadview.share <폴더> [--name 이름] [--new] [--remove] [--test]
설정은 애드인과 같은 %APPDATA%\\DH.Grading\\share3d.json (비밀번호 = DPAPI). 비밀번호 · 세션 id 는 어디에도 찍지 않는다.
"""
import argparse
import base64
import ctypes
import json
import os
import secrets
import ssl
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from concurrent.futures import ThreadPoolExecutor, as_completed
from ctypes import wintypes

JUNK_EXT = (".tmp",)
JUNK_NAMES = {"만들기_기록.txt", "pano_index.json", "terrain.json"}
ENTROPY = b"DH.Grading.Web3DShare.v1"
THREADS = 4
RETRIES = 3
CTX = ssl.create_default_context()


def _appdata():
    return os.path.join(os.environ.get("APPDATA", os.path.expanduser("~")), "DH.Grading")


CFG_PATH = os.path.join(_appdata(), "share3d.json")
MANIFEST_PATH = os.path.join(_appdata(), "roadview_share.json")


# ---------- 오류 · 설정 ----------
class NasError(Exception):
    def __init__(self, msg, code=-1):
        super().__init__(msg)
        self.code = code


def _common(c):
    if c == 105:
        return "이 계정에 권한이 없습니다(File Station 허용 · 폴더 권한)"
    if c in (106, 107, 119):
        return "로그인이 끊겼습니다 — 다시 해 보세요"
    if c in (101, 102, 103, 104):
        return "NAS가 요청을 이해하지 못했습니다(DSM 판 확인)"
    return "NAS가 거절했습니다"


def why_auth(c):
    return {
        400: "계정 또는 비밀번호가 틀립니다",
        401: "계정이 꺼져 있습니다",
        402: "이 계정은 쓸 수 없습니다(권한)",
        403: "2단계 인증 계정입니다 — 3D 전용 계정(2단계 인증 없음)을 쓰세요",
        404: "2단계 인증 계정입니다 — 3D 전용 계정(2단계 인증 없음)을 쓰세요",
        406: "2단계 인증 계정입니다 — 3D 전용 계정(2단계 인증 없음)을 쓰세요",
        407: "NAS가 이 PC 주소를 막았습니다(로그인 여러 번 실패 — DSM 보안에서 풀기)",
        408: "비밀번호가 만료됐거나 바꿔야 합니다 — DSM에서 그 계정으로 한 번 들어가 바꾸세요",
        409: "비밀번호가 만료됐거나 바꿔야 합니다 — DSM에서 그 계정으로 한 번 들어가 바꾸세요",
        410: "비밀번호가 만료됐거나 바꿔야 합니다 — DSM에서 그 계정으로 한 번 들어가 바꾸세요",
    }.get(c) or _common(c)


def why_file(c):
    return {
        408: "파일 · 폴더가 없습니다",
        409: "그 폴더에 쓸 권한이 없습니다",
        410: "그 폴더에 쓸 권한이 없습니다",
        411: "그 폴더에 쓸 권한이 없습니다",
        414: "같은 이름이 있습니다",
        416: "NAS 저장 공간이 모자랍니다",
    }.get(c) or _common(c)


class _Blob(ctypes.Structure):
    _fields_ = [("cbData", wintypes.DWORD), ("pbData", ctypes.POINTER(ctypes.c_char))]


def unprotect(b64):
    if not b64:
        return ""
    raw = base64.b64decode(b64)
    crypt32 = ctypes.WinDLL("crypt32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    crypt32.CryptUnprotectData.argtypes = [
        ctypes.POINTER(_Blob), ctypes.c_void_p, ctypes.POINTER(_Blob), ctypes.c_void_p,
        ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(_Blob)]
    crypt32.CryptUnprotectData.restype = wintypes.BOOL
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    kernel32.LocalFree.restype = ctypes.c_void_p
    inb = _Blob(len(raw), ctypes.cast(ctypes.create_string_buffer(raw, len(raw)), ctypes.POINTER(ctypes.c_char)))
    ent = _Blob(len(ENTROPY), ctypes.cast(ctypes.create_string_buffer(ENTROPY, len(ENTROPY)), ctypes.POINTER(ctypes.c_char)))
    out = _Blob()
    if not crypt32.CryptUnprotectData(ctypes.byref(inb), None, ctypes.byref(ent), None, None, 0, ctypes.byref(out)):
        raise NasError("저장된 비밀번호를 풀지 못했습니다 — Civil 3D 공유 설정에서 다시 저장하세요")
    try:
        return ctypes.string_at(out.pbData, out.cbData).decode("utf-8")
    finally:
        kernel32.LocalFree(ctypes.cast(out.pbData, ctypes.c_void_p))


def load_config():
    try:
        with open(CFG_PATH, "r", encoding="utf-8-sig") as f:
            c = json.load(f)
    except (OSError, ValueError):
        return None
    if not (c.get("Dsm") and c.get("Web") and c.get("User") and c.get("Pass") and str(c.get("Folder", "")).startswith("/")):
        return None
    return c


# ---------- DSM ----------
def _http(url, data=None, headers=None, timeout=900):
    req = urllib.request.Request(url, data=data, headers=headers or {})
    try:
        with urllib.request.urlopen(req, timeout=timeout, context=CTX) as r:
            return r.status, r.read(), r.headers
    except urllib.error.HTTPError as e:
        return e.code, e.read(), e.headers


def _call(res, what, auth=False):
    status, body, _ = res
    if status < 200 or status >= 300:
        raise NasError(f"{what} — NAS 응답 {status}")
    try:
        root = json.loads(body.decode("utf-8"))
    except ValueError:
        raise NasError(f"{what} — NAS가 알 수 없는 답을 했습니다(DSM 주소 확인)")
    if root.get("success"):
        return root.get("data") or {}
    code = (root.get("error") or {}).get("code", -1)
    raise NasError(f"{what} — {why_auth(code) if auth else why_file(code)}(코드 {code})", code)


class Session:
    def __init__(self, cfg):
        self.cfg = cfg
        dsm = cfg["Dsm"]
        if not dsm.lower().startswith("https://"):
            raise NasError("DSM 주소가 https가 아닙니다 — 웹 공유 설정에서 고치세요")
        self.base = dsm.rstrip("/")
        self.auth_path = self.up_path = self.del_path = "entry.cgi"
        self.up_ver = self.del_ver = 2
        self.sid = ""
        self._lock = threading.Lock()
        try:
            q = _call(_http(f"{self.base}/webapi/query.cgi?api=SYNO.API.Info&version=1&method=query"
                            "&query=SYNO.API.Auth,SYNO.FileStation.Upload,SYNO.FileStation.Delete"), "NAS 연결")
        except (urllib.error.URLError, OSError) as ex:
            raise NasError(f"NAS에 연결하지 못했습니다(DSM 주소 · 인터넷 확인) — {ex}")
        if "SYNO.API.Auth" in q:
            self.auth_path = q["SYNO.API.Auth"].get("path", self.auth_path)
        if "SYNO.FileStation.Upload" in q:
            self.up_path = q["SYNO.FileStation.Upload"].get("path", self.up_path)
            self.up_ver = min(2, q["SYNO.FileStation.Upload"].get("maxVersion", 2))
        if "SYNO.FileStation.Delete" in q:
            self.del_path = q["SYNO.FileStation.Delete"].get("path", self.del_path)
            self.del_ver = min(2, q["SYNO.FileStation.Delete"].get("maxVersion", 2))
        self.login()

    def login(self):
        form = urllib.parse.urlencode({
            "api": "SYNO.API.Auth", "version": "6", "method": "login", "session": "FileStation", "format": "sid",
            "account": self.cfg["User"], "passwd": unprotect(self.cfg["Pass"])}).encode()
        data = _call(_http(f"{self.base}/webapi/{self.auth_path}", form,
                           {"Content-Type": "application/x-www-form-urlencoded"}), "NAS 로그인", auth=True)
        self.sid = data.get("sid", "")

    def relogin(self, old_sid):
        with self._lock:
            if self.sid == old_sid:
                self.login()

    def logout(self):
        try:
            _http(f"{self.base}/webapi/{self.auth_path}?api=SYNO.API.Auth&version=6&method=logout"
                  f"&session=FileStation&_sid={self.sid}", timeout=30)
        except Exception:
            pass

    def upload(self, folder, name, data):
        b = "----dh3d" + uuid.uuid4().hex
        parts = []
        for k, v in (("path", folder), ("create_parents", "true"), ("overwrite", "true")):
            parts.append(f'--{b}\r\nContent-Disposition: form-data; name="{k}"\r\n\r\n{v}\r\n'.encode("utf-8"))
        parts.append(f'--{b}\r\nContent-Disposition: form-data; name="file"; filename="{name}"\r\n'
                     "Content-Type: application/octet-stream\r\n\r\n".encode("utf-8"))
        parts.append(data)
        parts.append(f"\r\n--{b}--\r\n".encode())
        body = b"".join(parts)
        for attempt in range(RETRIES):
            sid = self.sid
            try:
                _call(_http(f"{self.base}/webapi/{self.up_path}?api=SYNO.FileStation.Upload&version={self.up_ver}"
                            f"&method=upload&_sid={sid}", body,
                            {"Content-Type": "multipart/form-data; boundary=" + b}), "올리기")
                return
            except NasError as e:
                if e.code in (106, 107, 119):
                    self.relogin(sid)
                elif e.code in (105, 408, 409, 410, 411, 416) or attempt == RETRIES - 1:
                    raise
            except (urllib.error.URLError, OSError, TimeoutError):
                if attempt == RETRIES - 1:
                    raise
            time.sleep(2 * (attempt + 1))

    def delete(self, remote):
        path = urllib.parse.quote(json.dumps([remote], ensure_ascii=False), safe="")
        _call(_http(f"{self.base}/webapi/{self.del_path}?api=SYNO.FileStation.Delete&version={self.del_ver}"
                    f"&method=delete&path={path}&_sid={self.sid}"), "지우기")


def check_web(url):
    try:
        req = urllib.request.Request(url, headers={"Range": "bytes=0-1023"})
        with urllib.request.urlopen(req, timeout=30, context=CTX) as r:
            ct = r.headers.get_content_type()
            if "html" not in ct:
                return f"웹 주소가 웹페이지가 아니라 «{ct}»로 내보냅니다(내려받기로 열림)"
            return None
    except urllib.error.HTTPError as e:
        if e.code == 404:
            return "웹 주소에서 파일이 안 보입니다(404) — Web Station이 그 폴더를 웹으로 내놓는지 · 웹 주소가 맞는지 확인"
        return f"웹 주소가 {e.code}로 답합니다"
    except Exception as ex:
        return f"웹 주소에 연결하지 못했습니다 — {ex}"


# ---------- 폴더 · 목록 ----------
def scan(folder):
    """[(원격 상대경로, 로컬 경로, 크기, mtime_ns)] — 쓰레기 파일 제외, 로드뷰.html 만 있으면 index.html 로."""
    out = {}
    for root, _, names in os.walk(folder):
        for n in names:
            if n in JUNK_NAMES or n.lower().endswith(JUNK_EXT):
                continue
            p = os.path.join(root, n)
            rel = os.path.relpath(p, folder).replace(os.sep, "/")
            st = os.stat(p)
            out[rel] = (p, st.st_size, st.st_mtime_ns)
    if "index.html" not in out and "로드뷰.html" in out:
        out["index.html"] = out["로드뷰.html"]
    return [(rel, p, sz, mt) for rel, (p, sz, mt) in sorted(out.items())]


def load_manifest():
    try:
        with open(MANIFEST_PATH, "r", encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def save_manifest(m):
    os.makedirs(os.path.dirname(MANIFEST_PATH), exist_ok=True)
    tmp = MANIFEST_PATH + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(m, f, ensure_ascii=False, indent=1)
    os.replace(tmp, MANIFEST_PATH)


def gb(n):
    return f"{n / 1073741824:.1f} GB" if n >= 1073741824 else f"{n / 1048576:.0f} MB"


def web_url(cfg, name):
    return cfg["Web"].rstrip("/") + "/" + urllib.parse.quote(name) + "/index.html"


# ---------- 동작 ----------
def do_test(cfg):
    s = Session(cfg)
    try:
        print("로그인 됨")
    finally:
        s.logout()
    bad = check_web(cfg["Web"].rstrip("/") + "/")
    # 폴더 주소가 404/403 이어도 웹이 답하는 것 — 연결 자체만 본다
    if bad and "연결하지 못했습니다" in bad:
        print("웹 주소 확인 실패: " + bad)
        return 1
    print("웹 주소 응답 있음" + (f" ({bad})" if bad else ""))
    print("연결 시험 통과")
    return 0


def _safe_name(n):
    """원격 폴더 이름: rv_ 뒤 영문 · 숫자 · _ · - 1~64자 (빈 이름 · 경로 섞임 막기)."""
    import re as _re
    return bool(_re.fullmatch(r"rv_[A-Za-z0-9_-]{1,64}", n or ""))


def do_remove(cfg, folder):
    key = os.path.abspath(folder)
    m = load_manifest()
    ent = m.get(key)
    if not ent:
        print("올린 기록이 없습니다")
        return 0
    # 이름이 비었거나 이상하면 공유 폴더 전체(/web/3d/)를 지울 수 있다 → rv_ 로 시작하는 한 단어만 허용
    if not _safe_name(ent.get("name", "")):
        print("기록된 이름이 이상해서 지우지 않습니다: %r" % ent.get("name"))
        return 2
    s = Session(cfg)
    try:
        try:
            s.delete(cfg["Folder"].rstrip("/") + "/" + ent["name"])
        except NasError as e:
            if e.code != 408:
                raise
    finally:
        s.logout()
    m.pop(key, None)
    save_manifest(m)
    print("NAS에서 지움: " + ent["name"])
    return 0


def do_upload(cfg, folder, name, new):
    key = os.path.abspath(folder)
    items = scan(key)
    if not items:
        print("올릴 파일이 없습니다")
        return 1
    if not any(r == "index.html" for r, *_ in items):
        print("index.html 도 로드뷰.html 도 없습니다 — 로드뷰를 먼저 만드세요")
        return 1
    m = load_manifest()
    ent = m.get(key)
    if name:
        if ent and ent["name"] != name:
            ent = None
        rn = name if name.startswith("rv_") else "rv_" + name
    elif ent and not new:
        rn = ent["name"]
    else:
        rn = "rv_" + secrets.token_hex(16)
    if not _safe_name(rn):
        print("이름에는 영문 · 숫자 · _ · - 만 (예: rv_조치원 대신 rv_jochiwon): %r" % rn)
        return 2
    if not ent or ent["name"] != rn:
        ent = {"name": rn, "url": web_url(cfg, rn), "files": {}}
    ent["url"] = web_url(cfg, rn)
    m[key] = ent
    done_files = ent["files"]
    todo = [(r, p, sz, mt) for r, p, sz, mt in items if done_files.get(r) != [sz, mt]]
    total_bytes = sum(sz for _, _, sz, _ in items)
    print(f"파일 {len(items)}개 ({gb(total_bytes)}) · 올릴 것 {len(todo)}개 · 이름 {rn}", flush=True)
    failed = []
    if todo:
        remote_root = cfg["Folder"].rstrip("/") + "/" + rn
        s = Session(cfg)
        lock = threading.Lock()
        st = {"n": 0, "bytes": 0, "last": -1, "saved": 0}
        todo_bytes = sum(sz for _, _, sz, _ in todo)

        def work(item):
            rel, p, sz, mt = item
            with open(p, "rb") as f:
                data = f.read()
            d, _, base = rel.rpartition("/")
            s.upload(remote_root + ("/" + d if d else ""), base, data)
            with lock:
                done_files[rel] = [sz, mt]
                st["n"] += 1
                st["bytes"] += sz
                pct = st["n"] * 100 // len(todo)
                if pct // 2 != st["last"] // 2 or st["n"] == len(todo):
                    st["last"] = pct
                    print(f"올리는 중 {st['n']} / {len(todo)} · {gb(st['bytes'])}", flush=True)
                if st["n"] - st["saved"] >= 100:
                    st["saved"] = st["n"]
                    save_manifest(m)

        t0 = time.time()
        try:
            with ThreadPoolExecutor(THREADS) as ex:
                futs = {ex.submit(work, it): it for it in todo}
                for fu in as_completed(futs):
                    try:
                        fu.result()
                    except Exception as e:
                        failed.append((futs[fu][0], str(e)))
        finally:
            save_manifest(m)
            s.logout()
        print(f"걸린 시간 {time.time() - t0:.0f}초 · 올림 {st['n']}개 · 실패 {len(failed)}개", flush=True)
    else:
        save_manifest(m)
        print("올릴 것 0개 — 이미 다 올라가 있습니다")
    for r, e in failed[:10]:
        print(f"실패: {r} — {e}")
    if failed:
        print("다시 실행하면 실패한 것만 이어서 올립니다")
        return 1
    bad = check_web(ent["url"])
    if bad:
        print("⚠ " + bad)
    print("주소: " + ent["url"])
    return 0


def main(argv=None):
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass
    ap = argparse.ArgumentParser(prog="roadview.share")
    ap.add_argument("folder", nargs="?", default=".")
    ap.add_argument("--name")
    ap.add_argument("--new", action="store_true", help="새 무작위 이름으로")
    ap.add_argument("--remove", action="store_true", help="NAS에서 지우고 기록도 지움")
    ap.add_argument("--test", action="store_true", help="로그인 · 웹 응답만 확인")
    a = ap.parse_args(argv)
    cfg = load_config()
    if cfg is None:
        print("NAS 공유 설정이 없습니다 — Civil 3D에서 3D 웹 공유 «공유 설정»을 한 번 하세요")
        return 2
    try:
        if a.test:
            return do_test(cfg)
        if not os.path.isdir(a.folder):
            print("폴더가 없습니다: " + a.folder)
            return 1
        if a.remove:
            return do_remove(cfg, a.folder)
        return do_upload(cfg, a.folder, a.name, a.new)
    except NasError as e:
        print("실패: " + str(e))
        return 1


if __name__ == "__main__":
    sys.exit(main())
