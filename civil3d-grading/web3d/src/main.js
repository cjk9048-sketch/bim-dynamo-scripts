// [3D 보기] 뷰어 — 장면 읽기 → 층 → 둘러보기 · 정보 · 재기 → (Civil 안이면) 새로고침 · 저장 · 연동.
// v108 — 빗물 · 지적 · 측점 층 · 해 그림자 · 시공 순서 · 드론 영상 · 사진 · 3D 자르기 · 측점 → CAD 횡단.
import * as THREE from 'three';
import { loadScene } from './data.js';
import { View } from './view.js';
import { buildLayers, applyTerrainMode } from './layers.js';
import { buildDrain, buildCad, buildStations } from './extras.js';
import { Picker } from './pick.js';
import { Ui, Dock } from './ui.js';
import { Section } from './section.js';
import { Build } from './build.js';
import { Sun, sunPosition } from './sun.js';
import { photo, video, download, fileName } from './capture.js';
import { inHost, post, onHost } from './host.js';

const ORDER = ['terrain', 'sat', 'cutfill', 'ground', 'wall', 'pattern', 'excav', 'drain', 'cad', 'align'];
const KEEP = 'dh3d.layers.v1';

function loadKeep() { try { return JSON.parse(localStorage.getItem(KEEP) || '{}'); } catch { return {}; } }
function saveKeep(L) {
  try {
    const o = {};
    for (const id of ORDER) if (L[id] && !L[id].missing) o[id] = !!L[id].on;
    localStorage.setItem(KEEP, JSON.stringify(o));
  } catch { /* 저장 못 해도 화면은 그대로 */ }
}

async function main() {
  const t0 = performance.now();
  const ui = new Ui();
  if (inHost) document.body.classList.add('inhost');
  ui.busy('불러오는 중');
  window.DH3D = { ready: false };
  // ★[계획 검토 중간 11] 실패도 C#에 알린다 — «페이지가 떴다»와 «3D가 그려졌다»는 다르다
  const fail = (msg) => { ui.error(msg); window.DH3D.error = msg; post({ t: 'fail', why: msg }); };
  let head;
  try { ({ head } = await loadScene()); }
  catch (e) { fail('3D 자료를 못 읽었습니다 — ' + e.message); return; }

  let view;
  try { view = new View(document.getElementById('view')); }
  catch (e) { fail('이 PC에서 3D(웹GL)를 쓸 수 없습니다 — ' + e.message); return; }
  view.onLost = () => { ui.error('그래픽 장치가 멈췄습니다 — 잠시 뒤 되살아나지 않으면 [새로고침]'); post({ t: 'fail', why: 'webglcontextlost' }); };
  view.onRestored = () => ui.clearError();
  const b = head.bbox || [-100, -100, 0, 100, 100, 10];
  view.siteSize = Math.hypot(b[3] - b[0], b[4] - b[1]);
  let L;
  try {
    L = await buildLayers(view, head);
    buildDrain(view, head, L);
    buildCad(view, head, L);
    buildStations(view, head, L);
  } catch (e) { fail('3D를 못 그렸습니다 — ' + e.message); return; }

  // 지난번 층 켬/끔(이 PC 브라우저 기억) — 없으면 처음 값
  const keep = loadKeep();
  for (const id of ORDER) if (L[id] && !L[id].missing && typeof keep[id] === 'boolean') L[id].on = keep[id];

  const dock = new Dock();
  let section = null;   // 3D 자르기(아래에서 만든다)
  const setLayer = (id, on) => {
    const l = L[id];
    if (!l || l.missing) return;
    l.on = on;
    // 자르는 동안 측점 층은 숨겨 둔다(끝나면 이 체크대로 — 설치 전 검토 낮음)
    if (l.obj) l.obj.visible = on && !(id === 'align' && section && section.on);
    if (id === 'sat' || id === 'cutfill' || id === 'wall' || id === 'ground' || id === 'pattern') applyTerrainMode(L);
    if (id === 'cutfill') ui.legend(on, L.terrain ? L.terrain.cfLim : 1);
    if (id === 'drain') ui.drainLegend(on, L.drain.summary);
    ui.setLayerChecked(L, id, on);
    saveKeep(L);
    view.request();
  };
  // 층을 켬/끔 상태대로 다시(시공 재생이 바꾼 것 되돌리기)
  const refreshLayers = () => {
    for (const id of ORDER) if (L[id] && L[id].obj) L[id].obj.visible = L[id].on;
    applyTerrainMode(L);
    view.request();
  };
  refreshLayers();
  if (L.cutfill && L.cutfill.on) ui.legend(true, L.terrain.cfLim);
  if (L.drain && L.drain.on) ui.drainLegend(true, L.drain.summary);

  ui.stamp(head);
  ui.layers(L, ORDER);
  ui.onLayer = (id, on) => {
    // 시공 재생 중에 층을 바꾸면 재생을 끝낸다(재생이 층을 잠시 바꿔 두므로)
    if (dock.kind === 'build') dock.close();
    setLayer(id, on);
  };
  ui.earth(head.earth);
  ui.warnings(head.warn);

  const picker = new Picker(view, L, head, ui);
  section = new Section(view, L, head, ui);
  picker.clip = () => (section.on ? section.plane : null);
  section.onEnd = () => { if (dock.kind === 'cut') dock.close(); };
  const modeBtns = { info: 'modeInfo', measure: 'modeMeasure' };
  const setMode = (m) => {
    picker.setMode(m);
    for (const [k, id] of Object.entries(modeBtns)) document.getElementById(id).classList.toggle('on', k === m);
    ui.hideMeasure();
  };
  document.getElementById('modeInfo').onclick = () => setMode('info');
  document.getElementById('modeMeasure').onclick = () => setMode('measure');
  document.getElementById('measX').onclick = () => { picker.endMeasure(); };
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') { if (picker.mode === 'measure' && picker.pts.length) picker.endMeasure(); else { setMode('info'); ui.hideInfo(); picker.clearMarks(); view.request(); } }
  });

  // [전체] — 현장(바뀐 자리) · 그대로 한 번 더 누르면 지형 전체
  const focus = head.focus || b;
  let fitState = '';
  const fitTo = (box, mode, tag) => { view.fit(box, mode); fitState = tag; };
  view.controls.addEventListener('start', () => { fitState = ''; });
  document.getElementById('fitBtn').onclick = () => (fitState === 'focus' ? fitTo(b, 'iso', 'all') : fitTo(focus, 'iso', 'focus'));
  document.getElementById('topBtn').onclick = () => (fitState === 'focusTop' ? fitTo(b, 'top', 'allTop') : fitTo(focus, 'top', 'focusTop'));
  document.getElementById('compass').onclick = () => view.north();
  const shBtn = document.getElementById('shadowBtn');
  shBtn.onclick = () => { const on = !shBtn.classList.contains('on'); shBtn.classList.toggle('on', on); view.setShadows(on); };
  view.onDraw(() => ui.compass(view.heading()));

  // ── 해 · 시공 · 자르기 · 영상 — 아래 가운데 칸은 하나만 ──
  const sun = new Sun(view, L, head, focus);
  const build = new Build(view, L, head);
  build.onExit = () => { picker.enabled = true; refreshLayers(); };
  const sunBtn = document.getElementById('sunBtn'), buildBtn = document.getElementById('buildBtn');
  const videoBtn = document.getElementById('videoBtn'), photoBtn = document.getElementById('photoBtn');
  if (!sun.available) { sunBtn.disabled = true; sunBtn.title = '해 그림자 — 도면 좌표계(위경도)를 모릅니다'; }
  if (!build.available) { buildBtn.disabled = true; buildBtn.title = '시공 순서 — 원지반이 없습니다'; }
  dock.onChange = (k) => {
    sunBtn.classList.toggle('on', k === 'sun'); buildBtn.classList.toggle('on', k === 'build'); videoBtn.classList.toggle('on', k === 'rec');
    // 해 그림자 보기에서는 그림자가 늘 켜짐 — 그동안 [그림자] 단추를 막아 끝날 때 단추와 실제가 어긋나지 않게(설치 전 검토 낮음)
    shBtn.disabled = k === 'sun';
  };
  const openSun = () => { if (dock.kind === 'sun') return dock.close(); sun.panel(dock); };
  const openBuild = () => {
    if (dock.kind === 'build') return dock.close();
    ui.hideInfo(); picker.clearMarks();
    build.panel(dock);
    build.enter();
    picker.enabled = false;            // ★[계획 검토 중간 3] 재생 중에는 누르기 끔(보이는 것과 자료가 다르다)
  };
  sunBtn.onclick = openSun;
  buildBtn.onclick = openBuild;
  const cutAt = (where) => {
    // 다른 칸(시공 재생)을 먼저 닫는다 — 닫힐 때 옹벽 재료의 자르는 면을 걷으므로 자르기 뒤에 닫으면 자르기가 풀린다
    if (dock.kind !== 'cut') { dock.close(); section.start(where); section.panel(dock); }
    else section.start(where, false);
  };

  // 측점 풍선 단추 — [도면 횡단 보기](Civil 안만) · [3D에서 자르기]
  picker.stationActs = (st) => {
    const acts = [];
    if (inHost) acts.push({ text: '도면 횡단 보기', fn: () => post({ t: 'xsec', st: st.s, label: st.label }) });
    acts.push({ text: '3D에서 자르기', fn: () => { ui.hideInfo(); cutAt(st.i); } });
    return acts;
  };

  const dwgName = head.dwg || '3D';
  // [사진] — 3D만(단추 · 범례 · 풍선 없이) 화면의 2배
  let capturing = false;
  photoBtn.onclick = async () => {
    if (capturing) return;
    capturing = true;
    try {
      const blob = await photo(view);
      download(blob, fileName('사진', dwgName, 'png'));
      if (!inHost) ui.toast('사진을 내려받았습니다');
    } catch (e) { ui.toast('사진 실패 — ' + e.message); }
    finally { capturing = false; }
  };
  // [영상] — 현장을 20초에 한 바퀴 · 1080p
  videoBtn.onclick = async () => {
    if (capturing) return;
    capturing = true;
    let stop = false;
    if (dock.kind === 'build') dock.close();
    dock.open('rec', `
      <div class="row"><span class="t">드론 영상</span><span class="v" id="recT">준비 중</span><div class="bar"><div id="recBar"></div></div><button class="b" id="recStop">그만</button></div>`,
    () => { stop = true; });
    document.getElementById('recStop').onclick = () => { stop = true; };
    try {
      const r = await video(view, {
        focus, seconds: 20, fps: 30, w: 1920, h: 1080, cancelled: () => stop,
        onProgress: (t) => {
          const e1 = document.getElementById('recT'), e2 = document.getElementById('recBar');
          if (e1) e1.textContent = `녹화 중 ${Math.min(20, Math.floor(t * 20))}/20초`;
          if (e2) e2.style.width = `${Math.round(t * 100)}%`;
        },
      });
      if (r && !stop) {
        download(r.blob, fileName('영상', dwgName, r.ext));
        if (r.ext !== 'mp4') ui.toast('MP4를 못 만들어 WebM으로 저장합니다');
        else if (!inHost) ui.toast('영상을 내려받았습니다');
      } else if (stop) ui.toast('녹화를 그만두었습니다');
    } catch (e) { ui.toast('영상 실패 — ' + e.message); }
    finally {
      capturing = false;
      if (dock.kind === 'rec') { dock._onClose = null; dock.close(); }
    }
  };

  // Civil 안 — 새로고침 · 저장 · 도면에서 보기 · 도면 횡단 보기
  if (inHost) {
    // 바쁨은 새 페이지가 뜨면 사라진다 — 답이 없으면(명령이 못 돌았거나 실패) 3분 뒤 걷는다
    document.getElementById('refreshBtn').onclick = () => {
      ui.busy('다시 만드는 중');
      post({ t: 'refresh' });
      setTimeout(() => { if (document.getElementById('busy').style.display !== 'none') { ui.busy(null); ui.toast('응답이 없습니다 — 명령창을 보세요'); } }, 180000);
    };
    ui.saveMenu((what) => post(what === 'shareset' ? { t: 'shareset' } : { t: 'save', what }));
    ui.onGoto = (go) => post({ t: 'goto', ...go });
    onHost((m) => {
      if (m.t === 'busy') ui.busy(m.text || null);
      else if (m.t === 'toast') ui.toast(m.text || '');
      else if (m.t === 'shared' && m.url) ui.showShare(m.url, m.note || '', (what) => post({ t: what, url: m.url }));
      else if (m.t === 'xsec') {
        // ★[JACK 1027 «횡단을 수행하지않았으면 경고뜨고닫기»] 횡단도가 하나도 없으면 경고 + 풍선 닫음
        ui.toast((m.ok ? '' : '⚠ ') + (m.text || ''));
        if (m.none) ui.hideInfo();
      } else if (m.t === 'focus' && Array.isArray(m.box)) {
        const o = head.origin || [0, 0];
        const z0 = b[2], z1 = b[5];
        view.fit([m.box[0] - o[0], m.box[1] - o[1], z0, m.box[2] - o[0], m.box[3] - o[1], z1]);
      }
    });
  }

  // 사진기 자리 — 같은 도면 · 같은 원점이면 지난 자리로(새로고침해도 보던 곳 그대로)
  const poseKey = 'dh3d.pose.' + (head.dwgKey || head.dwg || '');
  const o0 = head.origin || [0, 0];
  let restored = false;
  try {
    const s = JSON.parse(localStorage.getItem(poseKey) || 'null');
    if (s && s.o && s.o[0] === o0[0] && s.o[1] === o0[1] && Array.isArray(s.p) && Array.isArray(s.t)) { view.setPose(s); restored = true; }
  } catch { /* 처음 값으로 */ }
  if (!restored) fitTo(focus, 'iso', 'focus');
  let poseTimer = 0;
  view.controls.addEventListener('change', () => {
    clearTimeout(poseTimer);
    poseTimer = setTimeout(() => { try { localStorage.setItem(poseKey, JSON.stringify({ o: o0, ...view.pose() })); } catch { /* 기억 못 해도 그만 */ } }, 400);
  });
  if (head.sat && (head.sat.base || head.sat.detail)) document.getElementById('attrib').style.display = 'block';
  ui.busy(null);
  view.draw();
  const ms = Math.round(performance.now() - t0);

  // 시험 훅(화면 없는 Edge 시험이 쓴다) — 화면을 바꾸지 않는 읽기 위주
  const counts = {};
  view.scene.traverse((o) => {
    if (!o.isMesh || !o.userData.layer) return;
    const g = o.geometry, k = o.userData.layer;
    const tris = (g.index ? g.index.count : g.attributes.position.count) / 3;
    counts[k] = (counts[k] || 0) + tris;
  });
  post({ t: 'ready', info: { ms, counts } });
  window.DH3D = {
    ready: true,
    ms,
    counts,
    head: {
      origin: head.origin, bbox: b, walls: (head.walls || []).length, patterns: (head.patterns || []).length, warn: head.warn || [],
      drain: L.drain ? L.drain.summary : null, cad: L.cad ? (L.cad.missing ? 'missing' : (head.cad.labels || []).length) : null,
      align: L.align ? { name: L.align.name, st: L.align.st.length, views: L.align.views } : null, build: !!head.build, geo: head.geo || null,
    },
    layer: (id, on) => setLayer(id, on),
    // 보기: 'fit' | 'top' | { at:[X,Y,Z](도면 좌표), dist, az(북=0 · 시계), el(도) }
    view: (v) => {
      if (v === 'fit') view.fit(focus);
      else if (v === 'top') view.fit(focus, 'top');
      else if (v === 'all') view.fit(b);
      else if (v && v.at) {
        const o = head.origin || [0, 0];
        const t = new THREE.Vector3(v.at[0] - o[0], v.at[1] - o[1], v.at[2]);
        const az = THREE.MathUtils.degToRad(v.az ?? 200), el = THREE.MathUtils.degToRad(v.el ?? 30), d = v.dist ?? 60;
        view.controls.target.copy(t);
        view.camera.position.set(t.x - Math.sin(az) * Math.cos(el) * d, t.y - Math.cos(az) * Math.cos(el) * d, t.z + Math.sin(el) * d);
        view.controls.update();
      }
      view.draw();
      return true;
    },
    pick: (px, py) => { const h = picker.hit(px, py); return h ? { ...picker.describe(h), layer: h.object.userData.layer } : null; },
    // 옹벽 목록(도면 좌표 범위 · 정거장 평균 앞 땅 높이 대신 지형 가운데 높이) — 시험이 옹벽 자리를 찍을 때
    walls: () => (head.walls || []).map(w => {
      const o = head.origin || [0, 0];
      let z = (b[2] + b[5]) / 2;
      const ray = new THREE.Raycaster(new THREE.Vector3((w.box[0] + w.box[2]) / 2, (w.box[1] + w.box[3]) / 2, b[5] + 100), new THREE.Vector3(0, 0, -1));
      const hit = L.terrain ? ray.intersectObject(L.terrain.obj, false)[0] : null;
      if (hit) z = hit.point.z;
      return { id: w.id, name: w.name, kind: w.kind, box: [w.box[0] + o[0], w.box[1] + o[1], w.box[2] + o[0], w.box[3] + o[1]], z };
    }),
    measure: (a, c) => picker.measureWorld(a, c),
    // v108 시험 — 해(날짜 · 한국 시각 h) · 시공 단계 k(null = 끝) · 자르기(측점 순번 · null = 끝) · 사진 · 영상(짧게)
    sun: (date, h) => { if (dock.kind !== 'sun') sun.panel(dock); sun.set(date, h * 60); view.draw(); return { az: sun.az, el: sun.el }; },
    sunAt: (date, h, lat, lon) => { const [Y, M, D] = date.split('-').map(Number); return sunPosition(Date.UTC(Y, M - 1, D) + (h - 9) * 3600000, lat, lon); },
    build: (k) => { if (k === null) { dock.close(); return null; } if (dock.kind !== 'build') openBuild(); build.stop(); build.set(k); view.draw(); return build.stage(); },
    cut: (i) => { if (i === null) { dock.close(); return null; } cutAt(i); view.draw(); return { s: section.s, n: section.plane.normal.toArray() }; },
    stations: () => (L.align ? L.align.st.map(s => ({ i: s.i, label: s.label, s: s.s, views: s.views, c: s.c })) : []),
    photo: async () => { const bl = await photo(view); return { bytes: bl.size, type: bl.type, ...(await (async () => { const bm = await createImageBitmap(bl); const r = { w: bm.width, h: bm.height }; bm.close(); return r; })()) }; },
    video: async (frames) => { const t = performance.now(); const r = await video(view, { focus, frames: frames || 30 }); return r ? { bytes: r.blob.size, ext: r.ext, codec: r.codec, frames: r.frames, ms: Math.round(performance.now() - t) } : null; },
    dock: () => dock.kind,
    // 도면 좌표 → 화면 px(시험이 측점 선을 진짜로 누를 때)
    screen: (at) => {
      const o = head.origin || [0, 0];
      const v = new THREE.Vector3(at[0] - o[0], at[1] - o[1], at[2]).project(view.camera);
      const r = view.renderer.domElement.getBoundingClientRect();
      return [Math.round((v.x + 1) / 2 * r.width + r.left), Math.round((1 - v.y) / 2 * r.height + r.top)];
    },
  };
}

main();
