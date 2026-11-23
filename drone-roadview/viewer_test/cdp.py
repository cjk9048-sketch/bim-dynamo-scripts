"""화면 없는 Edge 를 DevTools 프로토콜(CDP)로 실제 시간에 몬다. 표준 라이브러리만 쓴다.

가상 시간(--virtual-time-budget)은 그림 풀기 · 네트워크를 기다리지 않아 시험이 흔들린다.
여기서는 창 크기를 고정하고, 조건이 맞을 때까지 실제로 기다린 뒤 화면을 찍는다.

    with Edge(profile_dir) as ed:
        ed.goto(url)
        ed.wait_js("조건식", timeout=30)
        ed.screenshot(path)
        ed.console   # [(종류, 글)]  · ed.exceptions  # [글]
        ed.net(block=['*openstreetmap.org*'])  # 그 뒤 ed.requests = [청한 주소] (막은 주소도 남는다)
        ed.fake([('*vworld.kr*/Satellite/*', 'image/jpeg', jpg)], catch=['*vworld.kr*'])
            # 맞는 청은 밖으로 안 나가고 이 내용으로 바로 답 (Fetch) · catch 무늬인데 규칙이 없으면 막음
        ed.timeline  # [('con', 콘솔 글) | ('req', 청한 주소)] 들어온 차례대로 (MARK 앞뒤로 청을 나눌 때)
"""
import base64
import fnmatch
import json
import os
import shutil
import socket
import struct
import subprocess
import time
import urllib.request

EDGE = r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'


class _WS:
    """아주 작은 웹소켓 클라이언트 (글자 틀만)."""

    def __init__(self, url, timeout=60):
        assert url.startswith('ws://')
        hostport, path = url[5:].split('/', 1)
        host, port = hostport.rsplit(':', 1)
        self.s = socket.create_connection((host, int(port)), timeout=timeout)
        key = base64.b64encode(os.urandom(16)).decode()
        req = ('GET /%s HTTP/1.1\r\nHost: %s\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n'
               'Sec-WebSocket-Key: %s\r\nSec-WebSocket-Version: 13\r\n\r\n') % (path, hostport, key)
        self.s.sendall(req.encode())
        self.buf = bytearray()
        while b'\r\n\r\n' not in self.buf:
            chunk = self.s.recv(4096)
            if not chunk:
                raise ConnectionError('웹소켓 연결 실패')
            self.buf += chunk
        i = self.buf.index(b'\r\n\r\n')
        head = bytes(self.buf[:i])
        del self.buf[:i + 4]
        if b' 101 ' not in head.split(b'\r\n')[0]:
            raise ConnectionError(head.decode('latin1'))

    def _frame(self, op, data):
        hdr = bytearray([0x80 | op])
        n = len(data)
        if n < 126:
            hdr.append(0x80 | n)
        elif n < 65536:
            hdr.append(0x80 | 126)
            hdr += struct.pack('>H', n)
        else:
            hdr.append(0x80 | 127)
            hdr += struct.pack('>Q', n)
        mask = os.urandom(4)
        body = bytearray(data)
        for i in range(n):
            body[i] ^= mask[i & 3]
        self.s.sendall(bytes(hdr) + mask + bytes(body))

    def send(self, obj):
        self._frame(0x1, json.dumps(obj).encode())

    def _read(self, n):
        while len(self.buf) < n:
            chunk = self.s.recv(1 << 20)
            if not chunk:
                raise ConnectionError('웹소켓 끊김')
            self.buf += chunk
        out = bytes(self.buf[:n])
        del self.buf[:n]
        return out

    def recv(self):
        msg = bytearray()
        while True:
            b0, b1 = self._read(2)
            op, n = b0 & 0x0F, b1 & 0x7F
            if n == 126:
                n = struct.unpack('>H', self._read(2))[0]
            elif n == 127:
                n = struct.unpack('>Q', self._read(8))[0]
            mask = self._read(4) if b1 & 0x80 else None
            data = bytearray(self._read(n))
            if mask:
                for i in range(n):
                    data[i] ^= mask[i & 3]
            if op == 0x9:          # ping → pong
                self._frame(0xA, bytes(data))
                continue
            if op == 0x8:
                raise ConnectionError('웹소켓 닫힘')
            msg += data
            if b0 & 0x80:
                return json.loads(msg.decode('utf-8'))

    def close(self):
        try:
            self.s.close()
        except OSError:
            pass


class Edge:
    def __init__(self, profile, size=(1600, 900), gl=True):
        self.profile = profile
        self.size = size
        self.gl = gl
        self.console = []     # (종류, 글)
        self.exceptions = []  # 글
        self.requests = []    # 청한 주소 (net() 뒤)
        self.timeline = []    # ('con', 글) · ('req', 주소) 차례대로
        self.faked = []       # fake() 로 답한 주소 · ('fail', 주소) = 규칙 없어 막음
        self._fake = []
        self._id = 0

    def __enter__(self):
        shutil.rmtree(self.profile, ignore_errors=True)
        os.makedirs(self.profile, exist_ok=True)
        args = [EDGE, '--headless=new', '--no-first-run', '--no-default-browser-check', '--disable-extensions',
                '--user-data-dir=' + self.profile, '--hide-scrollbars', '--remote-debugging-port=0',
                '--window-size=%d,%d' % self.size]
        if self.gl:
            args += ['--disable-gpu-sandbox', '--use-angle=swiftshader', '--enable-unsafe-swiftshader']
        self.proc = subprocess.Popen(args + ['about:blank'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        port_file = os.path.join(self.profile, 'DevToolsActivePort')
        t0 = time.time()
        port = None
        while time.time() - t0 < 30:
            try:
                with open(port_file) as f:
                    port = int(f.readline().strip())
                break
            except (OSError, ValueError):
                time.sleep(0.1)
        if not port:
            self.__exit__(None, None, None)
            raise RuntimeError('Edge DevTools 포트를 못 찾음')
        page = None
        while time.time() - t0 < 30 and not page:
            with urllib.request.urlopen('http://127.0.0.1:%d/json/list' % port, timeout=10) as r:
                page = next((t for t in json.load(r) if t.get('type') == 'page'), None)
            if not page:
                time.sleep(0.1)
        self.ws = _WS(page['webSocketDebuggerUrl'])
        self.call('Runtime.enable')
        self.call('Page.enable')
        self.call('Emulation.setDeviceMetricsOverride',
                  {'width': self.size[0], 'height': self.size[1], 'deviceScaleFactor': 1, 'mobile': False})
        return self

    def __exit__(self, *exc):
        try:
            self.ws.close()
        except AttributeError:
            pass
        self.proc.terminate()
        try:
            self.proc.wait(10)
        except subprocess.TimeoutExpired:
            self.proc.kill()
        for _ in range(20):
            shutil.rmtree(self.profile, ignore_errors=True)
            if not os.path.exists(self.profile):
                break
            time.sleep(0.2)

    def _event(self, m):
        meth = m.get('method')
        p = m.get('params', {})
        if meth == 'Runtime.consoleAPICalled':
            text = ' '.join(str(a.get('value', a.get('description', ''))) for a in p.get('args', []))
            self.console.append((p.get('type'), text))
            self.timeline.append(('con', text))
        elif meth == 'Network.requestWillBeSent':
            self.requests.append(p.get('request', {}).get('url', ''))
            self.timeline.append(('req', self.requests[-1]))
        elif meth == 'Fetch.requestPaused':
            url, rid = p.get('request', {}).get('url', ''), p.get('requestId')
            for pat, mime, body in self._fake:
                if fnmatch.fnmatchcase(url, pat):
                    self._send('Fetch.fulfillRequest', {
                        'requestId': rid, 'responseCode': 200, 'body': body,
                        'responseHeaders': [{'name': 'Content-Type', 'value': mime}, {'name': 'Access-Control-Allow-Origin', 'value': '*'}]})
                    self.faked.append(url)
                    break
            else:
                self._send('Fetch.failRequest', {'requestId': rid, 'errorReason': 'BlockedByClient'})
                self.faked.append(('fail', url))
        elif meth == 'Runtime.exceptionThrown':
            d = p.get('exceptionDetails', {})
            self.exceptions.append((d.get('exception') or {}).get('description') or d.get('text', ''))

    def _send(self, method, params):
        """답을 기다리지 않고 보냄 (소식 처리 중에). 답은 call() 이 모르는 번호로 받아 버린다."""
        self._id += 1
        self.ws.send({'id': self._id, 'method': method, 'params': params})

    def call(self, method, params=None):
        self._id += 1
        mid = self._id
        self.ws.send({'id': mid, 'method': method, 'params': params or {}})
        while True:
            m = self.ws.recv()
            if m.get('id') == mid:
                if 'error' in m:
                    raise RuntimeError('%s: %s' % (method, m['error']))
                return m.get('result', {})
            self._event(m)

    def goto(self, url):
        """새 쪽으로. 옛 쪽에 표시를 남겨 두어 wait_js 가 옛 쪽을 새 쪽으로 착각하지 않게 한다 (NEW 참고)."""
        try:
            self.js('window.__cdpOld = 1')
        except RuntimeError:
            pass
        self.console.clear()
        self.exceptions.clear()
        self.requests.clear()
        self.timeline.clear()
        self.faked.clear()
        self.call('Page.navigate', {'url': url})

    def net(self, block=None):
        """네트워크 청을 기록한다. block = 막을 주소 무늬 (밖으로 나가지 않게)."""
        self.call('Network.enable')
        if block:
            self.call('Network.setBlockedURLs', {'urls': list(block)})

    def fake(self, rules, catch=()):
        """rules = [(fnmatch 무늬, mime, bytes)]: 맞는 청은 밖으로 안 나가고 이 내용으로 바로 답한다.
        catch = 더 잡을 무늬 (규칙에 안 맞으면 막음). 청 기록(net)은 그대로 남는다."""
        self._fake = [(pat, mime, base64.b64encode(body).decode('ascii')) for pat, mime, body in rules]
        pats = [pat for pat, _, _ in rules] + list(catch)
        self.call('Fetch.enable', {'patterns': [{'urlPattern': pat, 'requestStage': 'Request'} for pat in pats]})

    def mouse(self, x, y, kind='mouseMoved'):
        """진짜 마우스 움직임 (hover 시험)."""
        self.call('Input.dispatchMouseEvent', {'type': kind, 'x': x, 'y': y, 'button': 'none', 'buttons': 0})

    NEW = '!window.__cdpOld'  # wait_js 조건 앞에 붙여 쓴다

    def pump(self, seconds):
        """그동안 들어온 콘솔 · 오류 소식을 받는다."""
        t0 = time.time()
        while time.time() - t0 < seconds:
            self.js('1')
            time.sleep(0.1)

    def js(self, expr, await_promise=False):
        r = self.call('Runtime.evaluate', {'expression': expr, 'returnByValue': True, 'awaitPromise': await_promise})
        if 'exceptionDetails' in r:
            raise RuntimeError(r['exceptionDetails'].get('text', 'js'))
        return r.get('result', {}).get('value')

    def wait_js(self, expr, timeout=30.0, step=0.1):
        t0 = time.time()
        while time.time() - t0 < timeout:
            try:
                if self.js(expr):
                    return True
            except RuntimeError:
                pass
            time.sleep(step)
        return False

    def frames(self, n=2):
        """그림이 실제로 화면에 나갈 때까지 몇 장 기다림."""
        self.js('new Promise(r => { let k = %d; const f = () => (--k > 0 ? requestAnimationFrame(f) : r(1)); requestAnimationFrame(f); })' % n,
                await_promise=True)

    def screenshot(self, path):
        r = self.call('Page.captureScreenshot', {'format': 'png'})
        with open(path, 'wb') as f:
            f.write(base64.b64decode(r['data']))
