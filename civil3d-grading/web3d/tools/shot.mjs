// [3D 보기] 브라우저 시험 — Edge를 화면 없이 띄워(CDP) 페이지가 다 뜨기를 기다리고(DH3D.ready) 보기마다 그림을 찍는다 · 콘솔 오류 · 첫 그림까지 시간.
//   node tools/shot.mjs <html> <나갈 폴더> [보기 JSON 파일]
//   보기 JSON: [{ "name": "fit", "view": "fit" }, { "name": "w1", "view": { "at": [X, Y, Z], "dist": 40, "az": 200, "el": 25 } }, { "name": "pick", "pick": [800, 500] }]
//   보기 파일이 없으면 전체 · 위에서 · 옹벽마다 하나(앞 다섯)
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync, readFileSync, existsSync, mkdtempSync, rmSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { pathToFileURL } from 'node:url';

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
      const shot = await send('Page.captureScreenshot', { format: 'png' });
      writeFileSync(join(outDir, `${v.name}.png`), Buffer.from(shot.data, 'base64'));
      log.push(`그림 ${v.name}.png`);
    }
  else {
    const shot = await send('Page.captureScreenshot', { format: 'png' });
    writeFileSync(join(outDir, 'fail.png'), Buffer.from(shot.data, 'base64'));
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
