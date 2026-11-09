// [3D 보기] 브라우저 시험 — Edge를 화면 없이 띄워(CDP) 페이지가 다 뜨기를 기다리고(DH3D.ready) 보기마다 그림을 찍는다 · 콘솔 오류 · 첫 그림까지 시간.
//   node tools/shot.mjs <html> <나갈 폴더> [보기 JSON 파일]
//   보기 JSON: [{ "name": "fit", "view": "fit" }, { "name": "w1", "view": { "at": [X, Y, Z], "dist": 40, "az": 200, "el": 25 } }, { "name": "pick", "pick": [800, 500] }]
//   보기 파일이 없으면 전체 · 위에서 · 옹벽마다 하나(앞 다섯)
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync, readFileSync, existsSync, mkdtempSync, rmSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { pathToFileURL } from 'node:url';
import { inflateSync, deflateSync } from 'node:zlib';

const [, , html, outDir, viewsFile] = process.argv;
if (!html || !outDir) { console.error('쓰는 법: node tools/shot.mjs <html> <out> [views.json]'); process.exit(2); }
mkdirSync(outDir, { recursive: true });
const edge = ['C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', 'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe'].find(existsSync);
const port = 9300 + Math.floor(Math.random() * 500);
const prof = mkdtempSync(join(tmpdir(), 'dh3d-edge-'));
const W = 1600, H = 1000;
const br = spawn(edge, [`--headless=new`, `--remote-debugging-port=${port}`, `--user-data-dir=${prof}`, `--window-size=${W},${H}`,
  '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-first-run', '--no-default-browser-check', 'about:blank'], { stdio: 'ignore' });
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const log = [];
let ws, seq = 0;
const pending = new Map();
function send(method, params = {}) {
  const id = ++seq;
  ws.send(JSON.stringify({ id, method, params }));
  return new Promise((res, rej) => pending.set(id, { res, rej }));
}
async function evaluate(expr) {
  const r = await send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.text + ' ' + (r.exceptionDetails.exception?.description || ''));
  return r.result.value;
}

// ★[v116 · 1109] 화면 한 장을 PNG로 통째 찍으면 잎이 자잘한 숲 그림(PNG ≈ 3 MB 넘음)에서 화면 없는 Edge가 멈춘다(GPU · CPU 놀고 CDP 답 없음 —
//   JPEG · WebP · 반 높이 PNG는 됨 · 1109 실측). 그래서 가로 띠 넷으로 찍어(PNG · 무손실) 이어 붙인다.
const SHOT_STRIPS = 4;
function pngRows(buf) {   // 8비트 RGB/RGBA · 비월 없음(Edge 출력) → { w, h, bpp, rows: Buffer[] }
  let o = 8, w = 0, h = 0, bpp = 0;
  const idat = [];
  while (o < buf.length) {
    const len = buf.readUInt32BE(o), type = buf.toString('ascii', o + 4, o + 8), d = buf.subarray(o + 8, o + 8 + len);
    if (type === 'IHDR') { w = d.readUInt32BE(0); h = d.readUInt32BE(4); if (d[8] !== 8 || d[12] !== 0 || (d[9] !== 2 && d[9] !== 6)) throw new Error('PNG 꼴이 다름'); bpp = d[9] === 6 ? 4 : 3; }
    else if (type === 'IDAT') idat.push(d);
    else if (type === 'IEND') break;
    o += 12 + len;
  }
  const raw = inflateSync(Buffer.concat(idat)), stride = w * bpp, rows = [];
  let prev = Buffer.alloc(stride);
  for (let y = 0; y < h; y++) {
    const f = raw[y * (stride + 1)], src = raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1)), row = Buffer.alloc(stride);
    for (let x = 0; x < stride; x++) {
      const a = x >= bpp ? row[x - bpp] : 0, b = prev[x], c = x >= bpp ? prev[x - bpp] : 0;
      let p = 0;
      if (f === 1) p = a; else if (f === 2) p = b; else if (f === 3) p = (a + b) >> 1;
      else if (f === 4) { const q = a + b - c, pa = Math.abs(q - a), pb = Math.abs(q - b), pc = Math.abs(q - c); p = pa <= pb && pa <= pc ? a : pb <= pc ? b : c; }
      row[x] = (src[x] + p) & 255;
    }
    rows.push(row); prev = row;
  }
  return { w, h, bpp, rows };
}
function pngEncode(w, bpp, rows) {
  const tab = new Int32Array(256);
  for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; tab[n] = c; }
  const crc = (b) => { let x = -1; for (let i = 0; i < b.length; i++) x = tab[(x ^ b[i]) & 255] ^ (x >>> 8); return (x ^ -1) >>> 0; };
  const chunk = (t, d) => { const l = Buffer.alloc(4), c = Buffer.alloc(4), td = Buffer.concat([Buffer.from(t, 'ascii'), d]); l.writeUInt32BE(d.length); c.writeUInt32BE(crc(td)); return Buffer.concat([l, td, c]); };
  const ih = Buffer.alloc(13); ih.writeUInt32BE(w, 0); ih.writeUInt32BE(rows.length, 4); ih[8] = 8; ih[9] = bpp === 4 ? 6 : 2;
  const raw = Buffer.concat(rows.flatMap(r => [Buffer.from([0]), r]));
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ih), chunk('IDAT', deflateSync(raw, { level: 6 })), chunk('IEND', Buffer.alloc(0))]);
}
async function screenshot() {
  const parts = [];
  for (let i = 0; i < SHOT_STRIPS; i++) {
    const y0 = Math.round((i * H) / SHOT_STRIPS), y1 = Math.round(((i + 1) * H) / SHOT_STRIPS);
    const r = await send('Page.captureScreenshot', { format: 'png', clip: { x: 0, y: y0, width: W, height: y1 - y0, scale: 1 } });
    parts.push(pngRows(Buffer.from(r.data, 'base64')));
  }
  if (parts.some(p => p.w !== parts[0].w || p.bpp !== parts[0].bpp)) throw new Error('띠 그림 크기가 다름');
  return pngEncode(parts[0].w, parts[0].bpp, parts.flatMap(p => p.rows));
}
try {
  let target;
  for (let i = 0; i < 60 && !target; i++) {
    try { const list = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json(); target = list.find(t => t.type === 'page'); } catch { /* 아직 */ }
    if (!target) await sleep(250);
  }
  if (!target) throw new Error('Edge 디버그 포트가 안 열림');
  ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
  ws.onmessage = (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && pending.has(m.id)) { const p = pending.get(m.id); pending.delete(m.id); m.error ? p.rej(new Error(m.error.message)) : p.res(m.result); return; }
    if (m.method === 'Runtime.consoleAPICalled' && (m.params.type === 'error' || m.params.type === 'warning'))
      log.push(`[console.${m.params.type}] ` + m.params.args.map(a => a.value ?? a.description ?? '').join(' '));
    if (m.method === 'Runtime.exceptionThrown') log.push('[exception] ' + (m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text));
  };
  await send('Runtime.enable'); await send('Page.enable');
  // v108 — 사진 · 영상 단추가 내려받는 파일을 받을 폴더(DH_DL)
  if (process.env.DH_DL) { mkdirSync(process.env.DH_DL, { recursive: true }); const dl = { behavior: 'allow', downloadPath: resolve(process.env.DH_DL) }; await send('Browser.setDownloadBehavior', dl).catch(() => send('Page.setDownloadBehavior', dl)).catch(e => log.push('내려받기 폴더 실패 ' + e.message)); }
  await send('Emulation.setDeviceMetricsOverride', { width: W, height: H, deviceScaleFactor: 1, mobile: false });
  const t0 = Date.now();
  await send('Page.navigate', { url: pathToFileURL(resolve(html)).href });
  let st = null;
  for (let i = 0; i < 600; i++) {
    st = await evaluate('window.DH3D ? JSON.stringify({ ready: !!window.DH3D.ready, error: window.DH3D.error || null, ms: window.DH3D.ms || 0 }) : null').catch(() => null);
    if (st) { const s = JSON.parse(st); if (s.ready || s.error) break; }
    await sleep(250);
  }
  const s = st ? JSON.parse(st) : { ready: false };
  const info = s.ready ? await evaluate('JSON.stringify({ ms: DH3D.ms, counts: DH3D.counts, head: DH3D.head, gl: (() => { const c = document.querySelector("canvas"); const g = c && (c.getContext("webgl2") || c.getContext("webgl")); const d = g && g.getExtension("WEBGL_debug_renderer_info"); return d ? g.getParameter(d.UNMASKED_RENDERER_WEBGL) : "?"; })() })') : null;
  log.unshift(`열기 ${Date.now() - t0} ms · 페이지 첫 그림 ${s.ms} ms · ready ${s.ready} · 오류 ${s.error}`);
  if (info) log.push('정보 ' + info);
  let views = [{ name: 'fit', view: 'fit' }, { name: 'top', view: 'top' }];
  if (viewsFile) views = JSON.parse(readFileSync(viewsFile, 'utf8'));
  else if (s.ready) {
    const walls = JSON.parse(await evaluate('JSON.stringify(DH3D.walls ? DH3D.walls() : [])'));
    walls.slice(0, 5).forEach((w, i) => views.push({ name: `wall${i + 1}`, view: { at: [(w.box[0] + w.box[2]) / 2, (w.box[1] + w.box[3]) / 2, w.z ?? 0], dist: Math.max(25, Math.hypot(w.box[2] - w.box[0], w.box[3] - w.box[1]) * 0.8), az: 200, el: 28 } }));
  }
  if (s.ready)
    for (const v of views) {
      if (v.layers) for (const [id, on] of Object.entries(v.layers)) await evaluate(`DH3D.layer(${JSON.stringify(id)}, ${on})`);
      if (v.view) await evaluate(`DH3D.view(${JSON.stringify(v.view)})`);
      if (v.pick) { log.push(`누름 ${v.name}: ` + JSON.stringify(await evaluate(`DH3D.pick(${v.pick[0]}, ${v.pick[1]})`))); }
      if (v.eval) log.push(`스크립트 ${v.name}: ` + JSON.stringify(await evaluate(v.eval)));
      const clicks = [...(v.clicks || [])];
      if (v.clickAt) clicks.push(await evaluate(`DH3D.screen(${JSON.stringify(v.clickAt)})`));   // 도면 좌표 자리를 누름(측점 선 등)
      for (const [x, y] of clicks) {   // 진짜 마우스 누름(눌러 떼기) — 정보 풍선 · 재기 점
        await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y });
        await send('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button: 'left', buttons: 1, clickCount: 1 });
        await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button: 'left', buttons: 0, clickCount: 1 });
        await sleep(150);
      }
      if (v.moveAt) {   // 진짜 마우스 올리기(누르지 않음) — 측점 강조
        const [x, y] = await evaluate(`DH3D.screen(${JSON.stringify(v.moveAt)})`);
        await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: x - 30, y: y - 30 });
        await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y });
        await sleep(150);
      }
      if (v.text) log.push(`글 ${v.name}: ` + JSON.stringify(await evaluate(`[...document.querySelectorAll(${JSON.stringify(v.text)})].filter(e => e.offsetParent).map(e => e.innerText).join(' | ')`)));
      if (v.wait) await sleep(v.wait);
      await sleep(300);
      writeFileSync(join(outDir, `${v.name}.png`), await screenshot());
      log.push(`그림 ${v.name}.png`);
    }
  else {
    writeFileSync(join(outDir, 'fail.png'), await screenshot());
  }
} catch (e) {
  log.push('시험 실패 ' + e.message);
} finally {
  writeFileSync(join(outDir, 'shot.log'), log.join('\n') + '\n');
  console.log(log.join('\n'));
  try { ws?.close(); } catch { }
  br.kill();
  await sleep(500);
  try { rmSync(prof, { recursive: true, force: true }); } catch { }
}
