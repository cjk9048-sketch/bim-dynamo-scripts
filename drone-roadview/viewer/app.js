/* 드론 로드뷰 — 360° 사진 보기 · 지도 · 관로 겹쳐 보기.
 * 외부 라이브러리 없음 (WebGL2 + canvas 2D).
 * file:// 로 열어도 되도록 fetch · 모듈 · worker 를 쓰지 않는다. 자료는 모두 <script> 로 받는다.
 *
 * 좌표: e, n, z = 원점 기준 m (격자 동 · 격자 북 · 표고). 방위는 격자 북에서 시계 방향.
 * 사진(등장방형): 열 x → λ = (x+0.5)/W·360° − 180°, 행 y → φ = 90° − (y+0.5)/H·180°.
 * 세계 방향 (E,N,U) = (cosφ·sinλ, cosφ·cosλ, sinφ).
 * 맞춤 R = Rz(dyaw)·Rx(dpitch)·Ry(droll): 사진 방향 p 의 실제 방향 w = R·p. 화면은 p = Rᵀ·w 로 읽는다.
 */
(function () {
  'use strict';

  // ---------------------------------------------------------------- 상수
  const D2R = Math.PI / 180, R2D = 180 / Math.PI;
  const NEAR = 0.05;               // m, 가까운 자르기 면
  const FADE0 = 200, FADE1 = 300;  // m, 관로 흐려짐 시작 · 끝
  const NEIGH_R = 60;              // m, 이웃 사진 표시 반경
  const MARK_R = 0.135;            // m, 이웃 사진 원판 반지름 (JACK 1123: 땅을 덜 가리게 30 %)
  const MARK_HOVER = 1 / 0.3;      // 마우스 올린 것만 이만큼 키움 (= 예전 0.45 m)
  const FOV_MIN = 30, FOV_MAX = 120;
  const TILE = 256, MAP_ZMIN = 1, MAP_ZMAX = 22;
  const CACHE_PANOS = 5, CACHE_TILES = 400;
  const STEP = 0.1;                // 맞춤 ± 한 번
  const OB_DIA = 0.15;             // m, 지장물 관경을 모를 때 (관 굵기 · 노면 띠 최소 폭)
  const OB_ALPHA = 0.45;           // 지장물 관 (깊이 방식) 땅 속 = 반투명 · 터파기 구멍 안 = 불투명
  const CF_RED = '#ef4444';        // 지장물 간섭 고리
  const FILL_A = 0.42;             // 터파기 방식 층 채움 (보호층 · 되메우기) 비침
  const OP_LEN = [5, 100];         // m, 부분 개방 길이

  const $ = (id) => document.getElementById(id);
  const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
  const isNum = (v) => v !== null && v !== undefined && v !== '' && Number.isFinite(Number(v));
  const num = (v, d) => (isNum(v) ? Number(v) : d);
  const round = (v, k) => { const m = Math.pow(10, k); return Math.round(v * m) / m; };
  // CSS 색 → [r, g, b] 0~1 (WebGL 관 색). 캔버스가 '#rrggbb' 또는 'rgba(…)' 로 바꿔 준다.
  const colorCtx = document.createElement('canvas').getContext('2d');
  function hexRgb(c) {
    colorCtx.fillStyle = '#00e5ff';
    colorCtx.fillStyle = c;
    const s = String(colorCtx.fillStyle);
    if (s[0] === '#') return [1, 3, 5].map((i) => parseInt(s.substr(i, 2), 16) / 255);
    const m = s.match(/[\d.]+/g) || [0, 229, 255];
    return [0, 1, 2].map((i) => Number(m[i]) / 255);
  }

  // ---------------------------------------------------------------- 사진 스크립트 받는 곳
  // data/pano/XXXX.js 가 RV_PANO_LOADED("XXXX", "data:image/jpeg;base64,...") 를 부른다.
  const panoWait = new Map();
  let panoStray = null;
  window.RV_PANO_LOADED = function (id, uri) {
    const w = panoWait.get(String(id));
    if (w) w.got(uri); else panoStray = uri;
  };

  // ---------------------------------------------------------------- 알림 · 저장
  let toastTimer = 0;
  // btn = 알림 안 단추 글 (그 단추만 onClick) · 없으면 알림 전체가 onClick
  function toast(text, onClick, btn) {
    const t = $('toast');
    t.textContent = text;
    t.onclick = btn ? null : onClick || null;
    if (btn && onClick) {
      const b = document.createElement('button');
      b.type = 'button';
      b.textContent = btn;
      b.addEventListener('click', (e) => { e.stopPropagation(); t.classList.remove('show'); onClick(); });
      t.appendChild(b);
    }
    t.classList.toggle('click', !!onClick && !btn);
    t.classList.add('show');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => t.classList.remove('show'), btn ? 10000 : onClick ? 6000 : 3000);
  }
  function viewMsg(text) {
    const m = $('viewMsg');
    m.textContent = text || '';
    m.classList.toggle('show', !!text);
  }
  function lsGet(k) {
    try { const v = localStorage.getItem(k); return v ? JSON.parse(v) : null; } catch (e) { return null; }
  }
  function lsSet(k, v) {
    try { localStorage.setItem(k, JSON.stringify(v)); } catch (e) { /* 저장 못 해도 계속 */ }
  }
  function copyText(text) {
    const done = () => toast('복사됨 · ' + text);
    const fallback = () => {
      const ta = document.createElement('textarea');
      ta.value = text;
      ta.style.position = 'fixed'; ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.select();
      try { document.execCommand('copy'); done(); } catch (e) { toast(text); }
      ta.remove();
    };
    if (navigator.clipboard && window.isSecureContext) navigator.clipboard.writeText(text).then(done, fallback);
    else fallback();
  }

  // ---------------------------------------------------------------- 프로그램 연결 (pywebview · WebView2)
  // 프로그램 안에서 열면 window.pywebview.api 가 있다 → 윗줄에 [관로 넣기] · [내보내기] · [처음]. 웹 공유 · 브라우저에서는 숨김.
  const HOST = () => (window.pywebview && window.pywebview.api) || null;
  function hostCall(name, arg) {
    const h = HOST();
    if (!h || typeof h[name] !== 'function') return Promise.reject(new Error('프로그램 연결 없음'));
    return Promise.resolve().then(() => (arg === undefined ? h[name]() : h[name](arg)));
  }
  const errText = (e) => String((e && e.message) || e || '오류');
  function hostShow() { $('hostTools').hidden = !HOST(); }
  let beforeHome = () => Promise.resolve(); // [처음] 전에 할 일 (맞춤 저장)
  $('hHome').addEventListener('click', () => {
    beforeHome().catch(() => {}).then(() => hostCall('go_home')).catch((e) => toast(errText(e)));
  });
  window.addEventListener('pywebviewready', hostShow);

  // ---------------------------------------------------------------- 자료
  const PRJ = window.RV_PROJECT;
  function noData(text) { // 자료가 없어도 프로그램에서는 [처음] 으로 돌아갈 수 있게
    viewMsg(text);
    $('hPipes').hidden = true;
    $('hObst').hidden = true;
    $('hExport').hidden = true;
    hostShow();
  }
  if (!PRJ || !Array.isArray(PRJ.shots) || !PRJ.shots.length) {
    noData('자료 없음 · data/project.js');
    return;
  }
  const shots = PRJ.shots
    .filter((s) => s && isNum(s.e) && isNum(s.n))
    .map((s, i) => {
      const z = num(s.z, 0);
      const id = String(s.id != null ? s.id : i + 1);
      return {
        id: id,
        seq: num(s.seq, i + 1),
        video: s.video != null ? String(s.video) : '',
        time: num(s.time, NaN),
        lat: num(s.lat, NaN), lon: num(s.lon, NaN),
        e: Number(s.e), n: Number(s.n), z: z,
        gz: num(s.groundZ, z - num(s.agl, num(s.relAlt, 15))), // groundZ = z − agl
        yaw: num(s.yaw, 0),
        file: s.file || ('data/pano/' + id + '.js'),
        mx: NaN, my: NaN,
        i: 0,
      };
    })
    .sort((a, b) => a.seq - b.seq);
  if (!shots.length) { noData('자료 없음'); return; }
  shots.forEach((s, i) => { s.i = i; });

  const videoName = {};
  (Array.isArray(PRJ.videos) ? PRJ.videos : []).forEach((v) => { if (v && v.id != null) videoName[String(v.id)] = String(v.name || v.id); });

  // 관로 자료 (RV_PIPES) → 그리기 꼴. 처음 한 번 + [관로 넣기] 뒤 data/pipes.js 를 다시 읽을 때 (reloadPipes).
  // 관 한 줄: pts = [e, n, z] (z 는 hasZ 일 때만 관 중심 표고), g = 꼭짓점마다 지표 표고, dia = 관경 m.
  // ctrl = 원래 꼭짓점 번호 (pts 는 5 m 마다 늘린 점 · 없으면 모든 점) · src = RV_PIPES.lines 의 번호 (관로 고치기).
  let pipeLayers = [];
  function buildLines(PIPES) {
    PIPES = PIPES || {};
    pipeLayers = (Array.isArray(PIPES.layers) ? PIPES.layers : [])
      .map((l) => ({ name: String((l && l.name) || ''), color: String((l && l.color) || '#00e5ff') }));
    const out = [];
    (Array.isArray(PIPES.lines) ? PIPES.lines : []).forEach((l, i) => { const L = buildLine(l, i); if (L) out.push(L); });
    return out;
  }
  const ptOk = (q) => Array.isArray(q) && isNum(q[0]) && isNum(q[1]);
  // 원래 꼭짓점 번호 (오름차순 · 처음 · 끝 포함). ctrl 이 없거나 틀리면 모든 점.
  function ctrlOf(l) {
    const P = l && Array.isArray(l.pts) ? l.pts : [], all = [];
    P.forEach((q, k) => { if (ptOk(q)) all.push(k); });
    if (!Array.isArray(l.ctrl)) return all;
    const c = [...new Set(l.ctrl.filter((k) => Number.isInteger(k) && k >= 0 && k < P.length && ptOk(P[k])))].sort((a, b) => a - b);
    if (c.length < 2 || !all.length) return all;
    if (c[0] !== all[0]) c.unshift(all[0]);
    if (c[c.length - 1] !== all[all.length - 1]) c.push(all[all.length - 1]);
    return c;
  }
  function buildLine(l, src) {
    if (!l || !Array.isArray(l.pts)) return null;
    const hasZ = l.hasZ === true;
    const gIn = Array.isArray(l.g) && l.g.length === l.pts.length ? l.g : null;
    const p = [], g = [], at = new Int32Array(l.pts.length).fill(-1);
    l.pts.forEach((q, k) => {
      if (!ptOk(q)) return;
      at[k] = p.length / 3;
      p.push(Number(q[0]), Number(q[1]), hasZ ? num(q[2], NaN) : NaN);
      g.push(gIn ? num(gIn[k], NaN) : NaN);
    });
    if (p.length < 6) return null;
    const lay = pipeLayers[num(l.layer, 0) | 0] || { name: '', color: '#00e5ff' };
    let e0 = Infinity, e1 = -Infinity, n0 = Infinity, n1 = -Infinity;
    for (let k = 0; k < p.length; k += 3) {
      e0 = Math.min(e0, p[k]); e1 = Math.max(e1, p[k]);
      n0 = Math.min(n0, p[k + 1]); n1 = Math.max(n1, p[k + 1]);
    }
    const ll = Array.isArray(l.ll) && l.ll.length * 3 === p.length && l.ll.every((q) => Array.isArray(q) && isNum(q[0]) && isNum(q[1])) ? l.ll : null;
    const diaKnown = isNum(l.dia) && Number(l.dia) > 0;
    const dia = diaKnown ? Number(l.dia) : 0.2;
    const base = String(l.label || lay.name || '');
    const dTxt = 'D' + Math.round(dia * 1000);
    const kind = String(lay.name || '');
    const label = !diaKnown ? base : !base || base === dTxt ? dTxt : base.indexOf(dTxt + ' ') === 0 ? base : dTxt + ' · ' + base;
    const bi = [0]; // 평면에서 겹치지 않는 꼭짓점 번호 (노면 띠)
    for (let k = 3; k < p.length; k += 3) {
      const j = bi[bi.length - 1] * 3;
      if (Math.hypot(p[k] - p[j], p[k + 1] - p[j + 1]) > 1e-6) bi.push(k / 3);
    }
    const rgb = hexRgb(lay.color), rgbHi = rgb.map((v) => v + (1 - v) * 0.4); // 고른 관 (관로 고치기) = 밝게
    return {
      src: src, color: lay.color, rgb: rgb, rgbHi: rgbHi, colorHi: 'rgb(' + rgbHi.map((v) => Math.round(v * 255)).join(',') + ')',
      label: label, base: base, kind: kind, diaKnown: diaKnown, dia: dia, hasZ: hasZ, pts: Float64Array.from(p), g: Float64Array.from(g), bi: bi, ll: ll,
      ctrl: ctrlOf(l).map((k) => at[k]).filter((k) => k >= 0), e0, e1, n0, n1, mx: null, my: null,
    };
  }
  let lines = buildLines(window.RV_PIPES);

  // ---------------------------------------------------------------- 맞춤 (영상마다)
  const CAL_F = ['dx', 'dy', 'dz', 'dyaw', 'dpitch', 'droll'];
  const CAL_KEY = 'rv_calib:' + String(PRJ.title || 'project');
  const fileCal = (window.RV_CALIB && window.RV_CALIB.videos) || {};
  let localCal = (lsGet(CAL_KEY) || {}).videos || {};
  const calMemo = new Map();
  function calibFor(vid) {
    let c = calMemo.get(vid);
    if (c) return c;
    const f = fileCal[vid] || {}, l = localCal[vid] || {};
    c = {};
    CAL_F.forEach((k) => { c[k] = isNum(l[k]) ? Number(l[k]) : isNum(f[k]) ? Number(f[k]) : 0; });
    calMemo.set(vid, c);
    return c;
  }
  function calFileVal(vid, k) { const f = fileCal[vid] || {}; return isNum(f[k]) ? Number(f[k]) : 0; }
  function setCal(vid, k, v) {
    const c = Object.assign({}, calibFor(vid));
    c[k] = round(v, 4);
    localCal[vid] = c;
    calMemo.delete(vid);
    lsSet(CAL_KEY, { videos: localCal });
    hostCalSoon();
  }
  // 프로그램 안: 맞춤 값 · 점 짝이 바뀌면 0.8 초 뒤 한 번 프로젝트 폴더에 저장 (save_calib, «파일로 저장» 과 같은 글)
  let hostCalT = 0;
  function hostCalSoon() {
    if (!HOST()) return;
    clearTimeout(hostCalT);
    hostCalT = setTimeout(hostCalFlush, 800);
  }
  function hostCalFlush() {
    clearTimeout(hostCalT);
    hostCalT = 0;
    return hostCall('save_calib', calibText()).then((r) => {
      if (!r || r.ok === false) toast('맞춤 저장 안 됨');
      return r;
    }, () => { toast('맞춤 저장 안 됨'); return null; });
  }
  beforeHome = () => (hostCalT ? hostCalFlush() : Promise.resolve());
  window.addEventListener('pagehide', () => { if (hostCalT) hostCalFlush(); });
  function mul3(A, B) {
    const C = new Array(9);
    for (let i = 0; i < 3; i++) {
      for (let j = 0; j < 3; j++) C[i * 3 + j] = A[i * 3] * B[j] + A[i * 3 + 1] * B[3 + j] + A[i * 3 + 2] * B[6 + j];
    }
    return C;
  }
  // R = Rz(dyaw)·Rx(dpitch)·Ry(droll), 행 우선. dyaw + = 위에서 보아 시계 방향.
  // dpitch 는 격자 동 축, droll 은 격자 북 축 둘레 (사진이 북쪽 정렬이라 기체 방향과 무관).
  function rot64(c) {
    const a = c.dyaw * D2R, b = c.dpitch * D2R, g = c.droll * D2R;
    const ca = Math.cos(a), sa = Math.sin(a), cb = Math.cos(b), sb = Math.sin(b), cg = Math.cos(g), sg = Math.sin(g);
    const Rz = [ca, sa, 0, -sa, ca, 0, 0, 0, 1];
    const Rx = [1, 0, 0, 0, cb, -sb, 0, sb, cb];
    const Ry = [cg, 0, sg, 0, 1, 0, -sg, 0, cg];
    return mul3(mul3(Rz, Rx), Ry);
  }
  function rotOf(c) { return Float32Array.from(rot64(c)); }

  // ---------------------------------------------------------------- 땅 높이 (data/dem.js, 없어도 됨)
  // RV_DEM = { cell, tile, x0, y0 (원점 기준), tiles: { "ix_iy": base64 Float32 LE } }
  // 타일 하나 = (tile+1)² 값 · 행 r 은 남→북 (n = y0 + (iy·tile + r)·cell) · 열 c 는 서→동 · NaN = 없음 · 이웃 타일과 한 줄 겹침.
  const DEM = (function () {
    const d = window.RV_DEM;
    if (!d || !(num(d.cell, 0) > 0) || !(num(d.tile, 0) >= 1) || !isNum(d.x0) || !isNum(d.y0) || !d.tiles || typeof d.tiles !== 'object') return null;
    return { cell: Number(d.cell), tile: Math.round(Number(d.tile)), x0: Number(d.x0), y0: Number(d.y0), tiles: d.tiles, got: new Map() };
  })();
  function demTile(ix, iy) {
    const k = ix + '_' + iy;
    if (DEM.got.has(k)) return DEM.got.get(k);
    const s = DEM.tiles[k], n = (DEM.tile + 1) * (DEM.tile + 1);
    let a = null;
    if (typeof s === 'string') {
      try {
        const bin = atob(s);
        if (bin.length >= n * 4) {
          const dv = new DataView(new ArrayBuffer(n * 4));
          for (let i = 0; i < n * 4; i++) dv.setUint8(i, bin.charCodeAt(i));
          a = new Float32Array(n);
          for (let i = 0; i < n; i++) a[i] = dv.getFloat32(i * 4, true);
        }
      } catch (e) { a = null; }
    }
    DEM.got.set(k, a);
    return a;
  }
  // 격자 (e, n) 의 땅 표고 (타일 안 겹선형). 자료 밖 · 빈 칸이면 NaN.
  function demAt(e, n) {
    if (!DEM) return NaN;
    const T = DEM.tile, fx = (e - DEM.x0) / DEM.cell, fy = (n - DEM.y0) / DEM.cell;
    if (!(fx >= 0 && fy >= 0)) return NaN;
    const ix = Math.floor(fx / T), iy = Math.floor(fy / T);
    const a = demTile(ix, iy);
    if (!a) return NaN;
    const c = fx - ix * T, r = fy - iy * T;
    const c0 = Math.min(Math.floor(c), T - 1), r0 = Math.min(Math.floor(r), T - 1);
    const tx = c - c0, ty = r - r0, W = T + 1, i = r0 * W + c0;
    const z = (a[i] * (1 - tx) + a[i + 1] * tx) * (1 - ty) + (a[i + W] * (1 - tx) + a[i + W + 1] * tx) * ty;
    return Number.isFinite(z) ? z : NaN;
  }

  // ---------------------------------------------------------------- 점 찍어 맞추기: 점 짝 (영상마다)
  // 짝 = { shot: 사진 id, p: 사진 속 방향 (맞춤 전, 단위 벡터), e, n: 지도에서 찍은 같은 점 (격자) }
  // 이 브라우저 (localStorage) 에 있으면 그것, 없으면 calib.js 의 videos[영상].pairs.
  const PAIR_KEY = 'rv_pairs:' + String(PRJ.title || 'project');
  const localPairs = (lsGet(PAIR_KEY) || {}).videos || {};
  const shotById = new Map(shots.map((s) => [s.id, s]));
  const AL = { on: false, pend: null }; // pend = 사진만 찍은 짝 { vid, shot, p }
  const alClear = $('alClear'); // 목록 끝에 다시 붙여 쓰는 «모두 지우기»
  function cleanPairs(a) {
    return (Array.isArray(a) ? a : [])
      .filter((q) => q && q.shot != null && Array.isArray(q.p) && q.p.length === 3 && q.p.every(isNum) && isNum(q.e) && isNum(q.n))
      .map((q) => ({ shot: String(q.shot), p: q.p.map((v) => round(Number(v), 7)), e: round(Number(q.e), 3), n: round(Number(q.n), 3) }));
  }
  function pairsOf(vid) {
    return cleanPairs(Array.isArray(localPairs[vid]) ? localPairs[vid] : (fileCal[vid] || {}).pairs);
  }
  function setPairs(vid, list) {
    localPairs[vid] = list;
    lsSet(PAIR_KEY, { videos: localPairs });
    hostCalSoon();
  }
  // 지도 점의 땅 표고: 높이 타일, 없으면 찍은 사진의 groundZ
  function pairGround(q, s) { const z = demAt(q.e, q.n); return Number.isFinite(z) ? z : s.gz; }
  // 사진 s 에서 방향 p 가 표고 zg 땅에 닿는 곳 [e, n] (맞춤 c, R = rot64(c)). 땅을 안 보면 null.
  function rayHit(s, c, R, p, zg) {
    const wx = R[0] * p[0] + R[1] * p[1] + R[2] * p[2];
    const wy = R[3] * p[0] + R[4] * p[1] + R[5] * p[2];
    const wz = R[6] * p[0] + R[7] * p[1] + R[8] * p[2];
    if (wz > -0.02) return null;
    const t = (zg - (s.z + c.dz)) / wz;
    if (!(t > 0)) return null;
    return [s.e + c.dx + t * wx, s.n + c.dy + t * wy];
  }
  // 영상의 짝마다 지금 맞춤으로 사진 점이 닿는 곳 · 지도 점과 어긋남 r (m, 못 쓰면 NaN)
  function pairRes(vid) {
    const c = calibFor(vid), R = rot64(c);
    return pairsOf(vid).map((q) => {
      const s = shotById.get(q.shot);
      const zg = s ? pairGround(q, s) : NaN;
      const h = s && s.video === vid ? rayHit(s, c, R, q.p, zg) : null;
      return { q: q, s: s, zg: zg, h: h, r: h ? Math.hypot(h[0] - q.e, h[1] - q.n) : NaN };
    });
  }
  function solveLin(A, b) { // 작은 연립방정식 (부분 피벗 가우스 소거)
    const n = b.length, M = A.map((row, i) => row.concat([b[i]]));
    for (let i = 0; i < n; i++) {
      let p = i;
      for (let k = i + 1; k < n; k++) if (Math.abs(M[k][i]) > Math.abs(M[p][i])) p = k;
      if (!(Math.abs(M[p][i]) > 1e-300)) return null;
      [M[i], M[p]] = [M[p], M[i]];
      for (let k = i + 1; k < n; k++) {
        const f = M[k][i] / M[i][i];
        for (let j = i; j <= n; j++) M[k][j] -= f * M[i][j];
      }
    }
    const x = new Array(n);
    for (let i = n - 1; i >= 0; i--) {
      let s = M[i][n];
      for (let j = i + 1; j < n; j++) s -= M[i][j] * x[j];
      x[i] = s / M[i][i];
    }
    return x.every(Number.isFinite) ? x : null;
  }
  // 영상 vid 의 짝으로 맞춤 풀기 (Levenberg–Marquardt, 지금 값에서 시작). 1 짝 → 동 · 북, 2 → + 방향, 3 이상 → + 높이.
  // 남북 · 동서 기울기는 그대로. 결과: 'ok' (적용함) · 'bad' (못 풂 · 값이 터무니없음 → 그대로 둠) · 'none' (짝 없음)
  function alSolve(vid) {
    const c0 = calibFor(vid), R0 = rot64(c0), use = [];
    pairRes(vid).forEach((o) => { if (o.h) use.push(o); });
    if (!use.length) return pairsOf(vid).length ? 'bad' : 'none';
    const keys = use.length === 1 ? ['dx', 'dy'] : use.length === 2 ? ['dx', 'dy', 'dyaw'] : ['dx', 'dy', 'dz', 'dyaw'];
    const calOf = (x) => { const c = Object.assign({}, c0); keys.forEach((k, i) => { c[k] = x[i]; }); return c; };
    const res = (x) => {
      const c = calOf(x), R = keys.length > 2 ? rot64(c) : R0, out = [];
      for (const o of use) {
        const h = rayHit(o.s, c, R, o.q.p, o.zg);
        if (!h) return null;
        out.push(h[0] - o.q.e, h[1] - o.q.n);
      }
      return out;
    };
    const cost = (r) => r.reduce((a, v) => a + v * v, 0);
    let x = keys.map((k) => c0[k]), r = res(x), lam = 1e-3;
    if (!r) return 'bad';
    let f = cost(r);
    for (let it = 0; it < 30; it++) {
      const m = keys.length, J = [];
      for (let j = 0; j < m; j++) { // 수치 미분 (가운데 차분)
        const xp = x.slice(), xm = x.slice();
        xp[j] += 1e-4; xm[j] -= 1e-4;
        const rp = res(xp), rm = res(xm);
        if (!rp || !rm) return 'bad';
        J.push(rp.map((v, i) => (v - rm[i]) / 2e-4));
      }
      const A = J.map((a) => J.map((b) => a.reduce((s, v, i) => s + v * b[i], 0)));
      const g = J.map((a) => -a.reduce((s, v, i) => s + v * r[i], 0));
      let step = -1;
      for (let k = 0; k < 10 && step < 0; k++) {
        const d = solveLin(A.map((row, i) => row.map((v, j) => (i === j ? v * (1 + lam) + 1e-12 : v))), g);
        const xn = d && x.map((v, i) => v + d[i]), rn = xn && res(xn);
        if (rn && cost(rn) < f) {
          step = Math.max(...d.map(Math.abs));
          x = xn; r = rn; f = cost(rn);
          lam = Math.max(lam / 10, 1e-9);
        } else lam *= 10;
      }
      if (step < 1e-7) break; // 더 줄지 않음 · 다 옴
    }
    const c = calOf(x);
    if (!x.every(Number.isFinite) || Math.abs(c.dz) > 15 || Math.abs(c.dyaw) > 30) return 'bad';
    keys.forEach((k) => setCal(vid, k, c[k]));
    return 'ok';
  }

  // ---------------------------------------------------------------- 상태
  const Q = new URLSearchParams(location.search);
  const qnum = (k) => (isNum(Q.get(k)) ? Number(Q.get(k)) : null);
  let startIdx = 0;
  const qShot = Q.get('shot');
  if (qShot) {
    let k = shots.findIndex((s) => s.id === qShot);
    if (k < 0 && isNum(qShot)) k = shots.findIndex((s) => s.seq === Number(qShot));
    if (k >= 0) startIdx = k;
  }
  // 지하 시설물 표현 (하나만): bury = 매설 (노면 띠 · 땅이 관을 가림) · trench = 터파기 (관로 따라 구덩이) ·
  // open = 부분 개방 (누른 곳만 구덩이) · sec = 단면 (누른 곳 횡단면). 주소 ?mode=depth 는 옛 이름 (= trench).
  const MODES = ['bury', 'trench', 'open', 'sec'];
  const qMode = String(Q.get('mode') || '').toLowerCase();
  const mode0 = qMode === 'depth' || qMode === 'trench' ? 'trench' : MODES.indexOf(qMode) > 0 ? qMode : 'bury';
  const S = {
    cur: -1, want: -1,
    yaw: qnum('yaw') ?? shots[startIdx].yaw,
    pitch: clamp(qnum('pitch') ?? -45, -89, 89),
    fov: clamp(qnum('fov') ?? 90, FOV_MIN, FOV_MAX),
    pipes: true, mode: mode0, depth: mode0 === 'trench', path: true,
    cover: Math.max(0, num(PRJ.depth, num(PRJ.cover, 1.2))), // 평균매설깊이: 지표 → 관 윗면 (m)
    cam: [0, 0, 0], gz: 0, R: rotOf({ dyaw: 0, dpitch: 0, droll: 0 }),
    marks: [], hover: -1,
  };
  const V = { W: 1, H: 1, w: 1, h: 1, dpr: 1, tanH: 1, tanV: 1, hfov: 90, f: [0, 1, 0], r: [1, 0, 0], u: [0, 0, 1] };
  // 관로 고치기: sel = 고른 관 (RV_PIPES.lines 번호) · hi = 고른 손잡이 · hov = 마우스 올린 손잡이 (원래 꼭짓점 차례)
  // down = 누른 채 끄는 것 · want = 다음 그림에서 옮길 마우스 자리 · undo / redo = [{ src, before, after }] (줄 JSON)
  const ED = {
    on: false, sel: -1, hi: -1, hov: -1, down: null, want: null, undo: [], redo: [],
    dirty: new Set(), orig: new Map(), origSig: '', origEdited: false, shpBusy: false, Lof: null, Lsel: -2, L: null,
  };
  let pipeGen = 0, selGen = 0; // 관 그물 다시: pipeGen = 고른 관 말고 바뀜 · selGen = 고른 관이 바뀜
  S.hnd = []; // 360 손잡이 (화면 자리)

  // ---------------------------------------------------------------- 터파기 설정 (관중심 터파기 · 회사 SAC 기준)
  // tp = 포장 직각 절단 두께 · n = 기울기 1:n (수직 1 에 수평 n) · margin = 관 양옆 여유폭 · bed = 모래 기초 ·
  // prot = 관 위 보호층 (모래, 관 윗면 → 위) · on = 터파기 방식에서 구덩이로 (터파기 형상).
  // 프로젝트 설정 = calib.js 의 trench (맞춤과 같은 저장: 프로그램 save_calib · 이 브라우저 · 파일로 저장). 없으면 기본값 (옛 파일 = prot 없음 → 기본).
  // 이 값들은 보기용 (가시화) 값이다 — 기본값 그대로면 화면에 «가시화 기본값» 을 붙인다.
  const TR_DEF = { tp: 0.2, n: 0.3, margin: 0.3, bed: 0.1, prot: 0.3, on: true };
  const TR_LIM = { tp: [0, 3], n: [0, 3], margin: [0, 3], bed: [0, 1], prot: [0, 3] };
  const TR_KEY = 'rv_trench:' + String(PRJ.title || 'project');
  function trClean(o, base) {
    o = o || {};
    const t = {};
    Object.keys(TR_LIM).forEach((k) => { t[k] = isNum(o[k]) ? round(clamp(Number(o[k]), TR_LIM[k][0], TR_LIM[k][1]), 3) : base[k]; });
    t.on = typeof o.on === 'boolean' ? o.on : base.on;
    return t;
  }
  let TR = trClean(lsGet(TR_KEY), trClean(window.RV_CALIB && window.RV_CALIB.trench, TR_DEF));
  let trOff = Q.get('trench') === '0'; // 주소 ?trench=0 = 이번만 구덩이 끔 (저장 안 함)
  const trOn = () => TR.on && !trOff;
  const trDefault = () => Object.keys(TR_LIM).every((k) => TR[k] === TR_DEF[k]);
  // 표시 항목 (모드와 따로 · 이 브라우저에 기억): bed = 기초 · 보호층 · fill = 되메우기 층 · lbl = 이름표.
  // 관로 본체 (S.pipes) 는 기억하지 않는다 (꺼 둔 채 다시 열면 관이 안 보여 헷갈림) · 지장물은 rv_ui:obst · 터파기 형상 = TR.on.
  const SHOW_KEY = 'rv_ui:show';
  const SHOW = (function () { const v = lsGet(SHOW_KEY) || {}; return { bed: v.bed !== false, fill: v.fill === true, lbl: v.lbl !== false }; })();
  // 부분 개방: OP = 누른 관 (RV_PIPES 번호 · 누른 땅 자리) · OPT = 길이 · 터파기 폭에 더할 폭 (이 브라우저에 기억)
  let OP = null, OPR = null;
  const OPT = Object.assign({ len: 20, ex: 0 }, lsGet('rv_ui:open') || {});
  OPT.len = clamp(num(OPT.len, 20), OP_LEN[0], OP_LEN[1]);
  OPT.ex = clamp(num(OPT.ex, 0), 0, 3);
  // 단면: SC = 누른 관 · 땅 자리 (값은 그때그때 다시 셈)
  let SC = null, SCR = null;

  function updView() {
    const t = Math.tan(S.fov * D2R / 2);
    if (V.W >= V.H) { V.tanH = t; V.tanV = t * V.H / V.W; } else { V.tanV = t; V.tanH = t * V.W / V.H; }
    V.hfov = 2 * Math.atan(V.tanH) * R2D;
    const y = S.yaw * D2R, p = S.pitch * D2R;
    const cy = Math.cos(y), sy = Math.sin(y), cp = Math.cos(p), sp = Math.sin(p);
    V.f = [cp * sy, cp * cy, sp];
    V.r = [cy, -sy, 0];
    V.u = [-sy * sp, -cy * sp, cp];
  }
  function updCam() {
    if (S.cur < 0) return;
    const s = shots[S.cur], c = calibFor(s.video);
    S.cam = [s.e + c.dx, s.n + c.dy, s.z + c.dz];
    S.gz = s.gz;
    S.R = rotOf(c);
  }

  // ---------------------------------------------------------------- 그리기 순서
  let rafId = 0, needView = true, needMap = true;
  function kick() { if (!rafId) rafId = requestAnimationFrame(frame); }
  function dirty() { needView = true; needMap = true; kick(); }
  function mapDirty() { needMap = true; kick(); }
  function flushView() {
    if (needView) { needView = false; updView(); drawGL(); drawOverlay(); }
  }
  function frame() {
    rafId = 0;
    if (ED.want) edApply(); // 관로 고치기: 마우스 움직임은 그림 한 장에 한 번만 반영
    flushView();
    if (needMap) { needMap = false; drawMap(); }
    cfCheck(); // 지장물 간섭: 관로 · 깊이 · 이격이 바뀌었으면 (0.2 초 모아) 다시
    if (QT.on) qtyUI();
    if (S.mode === 'sec') scUI(); // 단면 칸: 설정 · 관로가 바뀌면 다시
    else if (S.mode === 'open') opUI();
  }

  // ---------------------------------------------------------------- WebGL 360°
  const glc = $('gl'), ovc = $('ov'), ov = ovc.getContext('2d');
  let gl = null, prog = null, tex = null, vao = null, U = {}, maxTex = 4096, glImg = null;
  // 관 · 터파기 그물은 둘씩: [0] = 고치지 않는 관 (그대로 둠) · [1] = 고른 관 (관로 고치기 · 끄는 동안 이것만 다시)
  // n = 삼각형 번호 수 · no = 앞쪽 구멍 번호 수 (터파기) · o = 만든 자리 (카메라) · key = 다시 만들지 정하는 열쇠
  let pprog = null, PU = {}, tprog = null, TU = {};
  // nf = 끝쪽 채움 번호 수 (터파기 방식 층 채움 · 반투명)
  const meshNew = () => ({ vao: null, vbo: null, ibo: null, n: 0, no: 0, nf: 0, key: '', o: [0, 0, 0] });
  const tubeM = [meshNew(), meshNew()], pitM = [meshNew(), meshNew()], obM = meshNew(); // obM = 지장물 관
  const opM = meshNew(); // 부분 개방 구덩이
  const NO_MESH = { o: [0, 0, 0], v: new Float32Array(0), i: new Uint32Array(0), no: 0, nf: 0 };

  const VS = '#version 300 es\n' +
    'in vec2 a_pos; out vec2 v_ndc;\n' +
    'void main(){ v_ndc = a_pos; gl_Position = vec4(a_pos, 0.0, 1.0); }\n';
  const FS = '#version 300 es\n' +
    'precision highp float; precision highp sampler2D;\n' +
    'in vec2 v_ndc; out vec4 o;\n' +
    'uniform sampler2D u_tex; uniform vec3 u_f, u_r, u_u; uniform vec2 u_tan; uniform mat3 u_rt;\n' +
    'const float PI = 3.141592653589793;\n' +
    'void main(){\n' +
    '  vec3 w = normalize(u_f + v_ndc.x * u_tan.x * u_r + v_ndc.y * u_tan.y * u_u);\n' +
    '  vec3 p = u_rt * w;\n' +
    '  float lam = atan(p.x, p.y);\n' +
    '  float phi = atan(p.z, length(p.xy));\n' +
    '  vec2 uv = vec2(lam / (2.0 * PI) + 0.5, 0.5 - phi / PI);\n' +
    // 경도 ±180° 이음매: 끊기지 않는 두 좌표의 미분 중 작은 쪽을 쓴다.
    '  float u2 = fract(uv.x + 0.5);\n' +
    '  vec2 dx = dFdx(uv), dy = dFdy(uv);\n' +
    '  float dx2 = dFdx(u2), dy2 = dFdy(u2);\n' +
    '  if (abs(dx2) < abs(dx.x)) dx.x = dx2;\n' +
    '  if (abs(dy2) < abs(dy.x)) dy.x = dy2;\n' +
    '  o = vec4(textureGrad(u_tex, uv, dx, dy).rgb, 1.0);\n' +
    '}\n';
  // 관 (깊이 방식): 카메라 기준 좌표의 관 그물을 사진 위에 그린다. 사진엔 깊이가 없어 깊이 시험은 관끼리만.
  // 화면 투영은 겹쳐 그리기(drawOverlay)와 같다: x/z·kx, y/z·ky.
  const PVS = '#version 300 es\n' +
    'in vec3 a_pos; in vec3 a_nrm; in vec3 a_col;\n' +
    'uniform vec3 u_off, u_f, u_r, u_u; uniform vec2 u_tan, u_nf;\n' +
    'out vec3 v_rel, v_nrm, v_col;\n' +
    'invariant gl_Position;\n' +
    'void main(){\n' +
    '  vec3 rel = a_pos + u_off;\n' +
    '  v_rel = rel; v_nrm = a_nrm; v_col = a_col;\n' +
    '  float z = dot(rel, u_f), n = u_nf.x, f = u_nf.y;\n' +
    '  gl_Position = vec4(dot(rel, u_r) / u_tan.x, dot(rel, u_u) / u_tan.y, (f + n) / (f - n) * z - 2.0 * f * n / (f - n), z);\n' +
    '}\n';
  const PFS = '#version 300 es\n' +
    'precision highp float;\n' +
    'in vec3 v_rel, v_nrm, v_col; out vec4 o;\n' +
    'uniform vec3 u_f, u_r; uniform vec2 u_fade; uniform float u_alpha;\n' +
    'void main(){\n' +
    '  float a = u_alpha * clamp(1.0 - (length(v_rel.xy) - u_fade.x) / (u_fade.y - u_fade.x), 0.0, 1.0);\n' +
    '  if (a < 0.004) discard;\n' +
    '  vec3 N = normalize(v_nrm), V = normalize(-v_rel);\n' +
    '  vec3 L = normalize(vec3(0.0, 0.0, 0.8) - 0.4 * u_r - 0.35 * u_f);\n' + // 위 · 왼쪽 · 보는 사람 쪽 빛
    '  float lam = max(dot(N, L), 0.0);\n' +
    '  float sp = pow(max(dot(N, normalize(L + V)), 0.0), 40.0);\n' +
    '  float rim = pow(1.0 - abs(dot(N, V)), 3.0);\n' +
    '  vec3 c = v_col * (0.36 + 0.64 * lam) + 0.28 * sp + 0.18 * rim * mix(v_col, vec3(1.0), 0.5);\n' +
    '  o = vec4(min(c, vec3(1.0)), a);\n' +
    '}\n';
  // 터파기 안쪽 면 (꼭짓점 셰이더는 관과 같음). 색에 빛 · 깊이를 구워 둠. 안쪽 법선이 보는 사람 쪽을 볼 때만 그린다
  // (볼록한 구덩이 안에서 시선이 빠져나가는 면 = 보이는 면). 구멍 (지표 띠) 도 이 셰이더로 스텐실에만 쓴다 (법선 위).
  const TFS = '#version 300 es\n' +
    'precision highp float;\n' +
    'in vec3 v_rel, v_nrm, v_col; out vec4 o;\n' +
    'uniform vec2 u_fade; uniform float u_alpha;\n' +
    'void main(){\n' +
    '  if (dot(v_nrm, v_rel) > 0.0) discard;\n' +
    '  float a = clamp(1.0 - (length(v_rel.xy) - u_fade.x) / (u_fade.y - u_fade.x), 0.0, 1.0);\n' +
    '  if (a < 0.004) discard;\n' +
    '  o = vec4(v_col, a * u_alpha);\n' +
    '}\n';

  function compile(type, src) {
    const sh = gl.createShader(type);
    gl.shaderSource(sh, src);
    gl.compileShader(sh);
    if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(sh) || 'shader');
    return sh;
  }
  function initGLResources() {
    prog = gl.createProgram();
    gl.attachShader(prog, compile(gl.VERTEX_SHADER, VS));
    gl.attachShader(prog, compile(gl.FRAGMENT_SHADER, FS));
    gl.bindAttribLocation(prog, 0, 'a_pos');
    gl.linkProgram(prog);
    if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(prog) || 'link');
    ['u_tex', 'u_f', 'u_r', 'u_u', 'u_tan', 'u_rt'].forEach((n) => { U[n] = gl.getUniformLocation(prog, n); });
    vao = gl.createVertexArray();
    gl.bindVertexArray(vao);
    const buf = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, buf);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
    tex = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    const an = gl.getExtension('EXT_texture_filter_anisotropic');
    if (an) gl.texParameterf(gl.TEXTURE_2D, an.TEXTURE_MAX_ANISOTROPY_EXT, Math.min(8, gl.getParameter(an.MAX_TEXTURE_MAX_ANISOTROPY_EXT)));
    maxTex = gl.getParameter(gl.MAX_TEXTURE_SIZE) || 4096;
    setBlank();
    initPipeGL();
  }
  // 그물 그릇 (꼭짓점: 자리 3 + 법선 3 + 색 3)
  function meshInit(m) {
    m.key = ''; m.n = 0; m.no = 0; m.nf = 0;
    m.vao = gl.createVertexArray();
    gl.bindVertexArray(m.vao);
    m.vbo = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, m.vbo);
    for (let i = 0; i < 3; i++) { gl.enableVertexAttribArray(i); gl.vertexAttribPointer(i, 3, gl.FLOAT, false, 36, i * 12); }
    m.ibo = gl.createBuffer();
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, m.ibo);
    gl.bindVertexArray(null);
  }
  // 열쇠가 바뀌었을 때만 다시 만들어 올린다
  function meshPut(m, key, build) {
    if (key === m.key) return;
    m.key = key;
    const b = build();
    gl.bindVertexArray(m.vao);
    gl.bindBuffer(gl.ARRAY_BUFFER, m.vbo);
    gl.bufferData(gl.ARRAY_BUFFER, b.v, gl.DYNAMIC_DRAW);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, b.i, gl.DYNAMIC_DRAW);
    gl.bindVertexArray(null);
    m.n = b.i.length; m.no = b.no || 0; m.nf = b.nf || 0; m.o = b.o;
  }
  // 관 그리기 준비. 안 되면 pprog = null → 겹쳐 그리기에서 선으로 대신한다.
  function initPipeGL() {
    pprog = null;
    try {
      const p = gl.createProgram();
      gl.attachShader(p, compile(gl.VERTEX_SHADER, PVS));
      gl.attachShader(p, compile(gl.FRAGMENT_SHADER, PFS));
      ['a_pos', 'a_nrm', 'a_col'].forEach((n, i) => gl.bindAttribLocation(p, i, n));
      gl.linkProgram(p);
      if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p) || 'link');
      ['u_off', 'u_f', 'u_r', 'u_u', 'u_tan', 'u_nf', 'u_fade', 'u_alpha'].forEach((n) => { PU[n] = gl.getUniformLocation(p, n); });
      tubeM.forEach(meshInit);
      meshInit(obM);
      pprog = p;
    } catch (e) { pprog = null; }
    initTrenchGL();
  }
  // 터파기 준비. 스텐실이 없거나 안 되면 tprog = null → 깊이 방식은 예전처럼 관만.
  function initTrenchGL() {
    tprog = null;
    const ca = gl.getContextAttributes && gl.getContextAttributes();
    if (!pprog || !ca || !ca.stencil) return;
    try {
      const p = gl.createProgram();
      gl.attachShader(p, compile(gl.VERTEX_SHADER, PVS));
      gl.attachShader(p, compile(gl.FRAGMENT_SHADER, TFS));
      ['a_pos', 'a_nrm', 'a_col'].forEach((n, i) => gl.bindAttribLocation(p, i, n));
      gl.linkProgram(p);
      if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p) || 'link');
      ['u_off', 'u_f', 'u_r', 'u_u', 'u_tan', 'u_nf', 'u_fade', 'u_alpha'].forEach((n) => { TU[n] = gl.getUniformLocation(p, n); });
      pitM.forEach(meshInit);
      meshInit(opM);
      tprog = p;
    } catch (e) { tprog = null; }
  }
  function setBlank() {
    if (!gl || !tex) return;
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array([30, 34, 44, 255]));
    gl.generateMipmap(gl.TEXTURE_2D);
  }
  function setTexture(img) {
    if (!gl || !tex) return;
    let src = img;
    const w = img.naturalWidth || img.width, h = img.naturalHeight || img.height;
    const lim = Math.min(8192, maxTex);
    if (w > lim || h > lim) {
      // 그래픽 카드 한도보다 크면 줄여서 올린다.
      const k = Math.min(lim / w, lim / h);
      const c = document.createElement('canvas');
      c.width = Math.max(1, Math.floor(w * k));
      c.height = Math.max(1, Math.floor(h * k));
      const cx = c.getContext('2d');
      cx.imageSmoothingQuality = 'high';
      cx.drawImage(img, 0, 0, c.width, c.height);
      src = c;
    }
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, gl.RGBA, gl.UNSIGNED_BYTE, src);
    gl.generateMipmap(gl.TEXTURE_2D);
  }
  function initGL() {
    try {
      // depth · antialias = 깊이 방식의 관 (사진은 깊이를 쓰지 않는다) · stencil = 터파기 구멍 안에서만 그리기
      gl = glc.getContext('webgl2', { alpha: false, antialias: true, depth: true, stencil: true, premultipliedAlpha: false });
    } catch (e) { gl = null; }
    if (!gl) { viewMsg('WebGL2 안 됨'); return; }
    try { initGLResources(); } catch (e) { gl = null; viewMsg('WebGL2 오류'); return; }
    glc.addEventListener('webglcontextlost', (e) => { e.preventDefault(); prog = null; tex = null; pprog = null; tprog = null; });
    glc.addEventListener('webglcontextrestored', () => {
      try { initGLResources(); if (glImg) setTexture(glImg); } catch (e) { viewMsg('WebGL2 오류'); }
      dirty();
    });
  }
  function drawGL() {
    if (!gl || !prog || gl.isContextLost()) return;
    gl.viewport(0, 0, glc.width, glc.height);
    gl.disable(gl.DEPTH_TEST);
    gl.disable(gl.BLEND);
    gl.depthMask(true);
    gl.clear(gl.DEPTH_BUFFER_BIT);
    gl.useProgram(prog);
    gl.uniform3fv(U.u_f, V.f);
    gl.uniform3fv(U.u_r, V.r);
    gl.uniform3fv(U.u_u, V.u);
    gl.uniform2f(U.u_tan, V.tanH, V.tanV);
    gl.uniformMatrix3fv(U.u_rt, false, S.R); // 행 우선 R 을 열 우선으로 넘기면 Rᵀ
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.uniform1i(U.u_tex, 0);
    gl.bindVertexArray(vao);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    if (S.cur < 0 || !pprog) return;
    if (S.depth && (S.pipes || obShow())) drawTubes();
    else if (S.mode === 'open' && tprog && opRes()) drawOpening();
  }

  // ---------------------------------------------------------------- 관 (깊이 방식, WebGL)
  const TUBE_SIDES = 20;
  // 꼭짓점 k 의 지표 표고: 자료 g, 없으면 지금 사진의 groundZ
  function gAt(L, k) { const g = L.g[k]; return Number.isFinite(g) ? g : S.gz; }
  // 관 중심 표고: hasZ 면 z, 아니면 지표 − 평균매설깊이 − 관경/2
  function zcAt(L, k) { const z = L.pts[k * 3 + 2]; return L.hasZ && Number.isFinite(z) ? z : gAt(L, k) - S.cover - L.dia / 2; }
  // 관로 고치기에서 고른 관 (없으면 −1): 그 관 그물만 따로 (끄는 동안 그것만 다시 만든다)
  const edSel = () => (ED.on ? ED.sel : -1);
  const camBase = () => S.cur + '|' + S.cam.join(',') + '|' + S.gz + '|' + S.cover;
  function tubesPut(sel, base) {
    meshPut(tubeM[0], base + '|' + pipeGen + '|' + sel, () => buildTubes((L) => L.src !== sel, false));
    meshPut(tubeM[1], sel < 0 ? '' : base + '|' + pipeGen + '|' + selGen + '|' + sel, () => (sel < 0 ? NO_MESH : buildTubes((L) => L.src === sel, true)));
  }
  function obPut() { meshPut(obM, S.cur + '|' + S.cam.join(',') + '|' + obGen, () => buildTubes(() => true, false, obst, obZ)); }
  function drawTubes() {
    const sel = edSel(), base = camBase();
    if (S.pipes) tubesPut(sel, base);
    // 터파기: 구멍을 스텐실에 쓰고 안쪽 면을 그린 뒤, 관도 구멍 안에서만 (스텐실 시험은 관 · 층 채움을 그린 뒤 끈다)
    const pit = trGL() && drawTrench(base, sel);
    // 지장물: 구멍 안 = 불투명 (구덩이 벽 · 바닥 · 관과 깊이 견줌 → 드러난 것처럼) · 구멍 밖 땅 속 = 반투명 (X선)
    if (obShow()) obPut();
    const ob = obShow() && obM.n > 0;
    if (ob && pit) drawTubeMesh([obM], 1);
    if (S.pipes && (tubeM[0].n || tubeM[1].n)) drawTubeMesh(tubeM);
    if (pit) { pitFill(pitM); gl.disable(gl.STENCIL_TEST); }
    if (ob) {
      if (pit) { // 구멍 밖만 (구멍 안은 위에서 불투명으로 그림)
        gl.enable(gl.STENCIL_TEST);
        gl.stencilFunc(gl.NOTEQUAL, 1, 0xff);
        gl.stencilOp(gl.KEEP, gl.KEEP, gl.KEEP);
      }
      drawTubeMesh([obM], OB_ALPHA);
      if (pit) gl.disable(gl.STENCIL_TEST);
    }
  }
  // 관 그물 그리기 · alpha < 1 = 맨 앞 면만 반투명 · 1 = 불투명 (깊이 씀 · 뒤에 그리는 것이 이 깊이와 견줌)
  function drawTubeMesh(list, alpha) {
    const ms = list.filter((m) => m.n);
    if (!ms.length) return;
    alpha = alpha || 0.9;
    const each = (m) => {
      gl.bindVertexArray(m.vao);
      gl.uniform3f(PU.u_off, m.o[0] - S.cam[0], m.o[1] - S.cam[1], m.o[2] - S.cam[2]);
      gl.drawElements(gl.TRIANGLES, m.n, gl.UNSIGNED_INT, 0);
    };
    gl.useProgram(pprog);
    gl.uniform3fv(PU.u_f, V.f);
    gl.uniform3fv(PU.u_r, V.r);
    gl.uniform3fv(PU.u_u, V.u);
    gl.uniform2f(PU.u_tan, V.tanH, V.tanV);
    gl.uniform2f(PU.u_nf, 0.1, 2000);
    gl.uniform2f(PU.u_fade, FADE0, FADE1);
    gl.uniform1f(PU.u_alpha, alpha);
    gl.enable(gl.DEPTH_TEST);
    // 1) 깊이만 → 2) 맨 앞 면만 색칠 (반투명이어도 뒷면이 비치지 않게) · 그물 둘을 한 몸처럼
    gl.depthMask(true);
    gl.depthFunc(gl.LESS);
    gl.colorMask(false, false, false, false);
    ms.forEach(each);
    gl.colorMask(true, true, true, true);
    gl.depthMask(false);
    gl.depthFunc(gl.LEQUAL);
    gl.enable(gl.BLEND);
    gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ZERO, gl.ONE);
    ms.forEach(each);
    gl.disable(gl.BLEND);
    gl.disable(gl.DEPTH_TEST);
    gl.depthMask(true);
    gl.bindVertexArray(null);
  }
  // 지금 자리에서 FADE1 안의 관 (use(L) 인 것만) 을 원통 그물로 (좌표 = 카메라 기준 m). hi = 밝은 색 (고른 관).
  // list · zf = 다른 줄 묶음 (지장물) 과 그 관 중심 표고
  function buildTubes(use, hi, list, zf) {
    const o = S.cam.slice(), VB = [], IB = [];
    zf = zf || zcAt;
    for (const L of list || lines) {
      if (!use(L)) continue;
      const bx = Math.max(L.e0 - o[0], 0, o[0] - L.e1), by = Math.max(L.n0 - o[1], 0, o[1] - L.n1);
      if (bx * bx + by * by > FADE1 * FADE1) continue;
      const P = L.pts, cnt = P.length / 3, r = L.dia / 2;
      let run = [];
      const flush = () => { if (run.length >= 6) addTube(run, r, hi ? L.rgbHi : L.rgb, VB, IB); run = []; };
      const push = (k) => {
        const x = P[k * 3] - o[0], y = P[k * 3 + 1] - o[1], z = zf(L, k) - o[2], n = run.length;
        if (n && Math.abs(run[n - 3] - x) + Math.abs(run[n - 2] - y) + Math.abs(run[n - 1] - z) < 1e-6) return;
        run.push(x, y, z);
      };
      for (let k = 0; k + 1 < cnt; k++) {
        if (distPtSeg(o[0], o[1], P[k * 3], P[k * 3 + 1], P[k * 3 + 3], P[k * 3 + 4]) > FADE1) { flush(); continue; }
        if (!run.length) push(k);
        push(k + 1);
      }
      flush();
    }
    return { o: o, v: Float32Array.from(VB), i: Uint32Array.from(IB) };
  }
  const dot3 = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
  const cross3 = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
  const unit3 = (a) => { const l = Math.hypot(a[0], a[1], a[2]) || 1; return [a[0] / l, a[1] / l, a[2] / l]; };
  // 꺾인 곳은 이등분 면에서 이어 붙인다 (두 원통이 그 면에서 같은 타원으로 만나 틈 · 겹침이 없다).
  function addTube(c, r, col, VB, IB) {
    const m = c.length / 3 - 1, N = TUBE_SIDES;
    const C = (j) => [c[j * 3], c[j * 3 + 1], c[j * 3 + 2]];
    const t = [];
    for (let i = 0; i < m; i++) t.push(unit3([c[i * 3 + 3] - c[i * 3], c[i * 3 + 4] - c[i * 3 + 1], c[i * 3 + 5] - c[i * 3 + 2]]));
    for (let j = 1; j < m; j++) {
      if (dot3(t[j - 1], t[j]) < -0.6) { // 거의 되돌아가는 꺾임은 둘로 나눠 마구리를 붙인다
        addTube(c.slice(0, (j + 1) * 3), r, col, VB, IB);
        addTube(c.slice(j * 3), r, col, VB, IB);
        return;
      }
    }
    let A = cross3(t[0], [0, 0, 1]);
    if (Math.hypot(A[0], A[1], A[2]) < 1e-6) A = cross3(t[0], [1, 0, 0]);
    A = unit3(A);
    let B = cross3(t[0], A);
    const dirs = (A, B) => {
      const d = [];
      for (let s = 0; s < N; s++) {
        const a = s / N * 2 * Math.PI, ca = Math.cos(a), sa = Math.sin(a);
        d.push([ca * A[0] + sa * B[0], ca * A[1] + sa * B[1], ca * A[2] + sa * B[2]]);
      }
      return d;
    };
    const vert = (p, n) => { VB.push(p[0], p[1], p[2], n[0], n[1], n[2], col[0], col[1], col[2]); };
    const cap = (cen, ring, n, flip) => {
      const b = VB.length / 9;
      vert(cen, n);
      ring.forEach((p) => vert(p, n));
      for (let s = 0; s < N; s++) {
        const s1 = (s + 1) % N;
        if (flip) IB.push(b, b + 1 + s1, b + 1 + s); else IB.push(b, b + 1 + s, b + 1 + s1);
      }
    };
    let d = dirs(A, B);
    const c0 = C(0);
    let ring = d.map((q) => [c0[0] + r * q[0], c0[1] + r * q[1], c0[2] + r * q[2]]);
    cap(c0, ring, [-t[0][0], -t[0][1], -t[0][2]], true);
    for (let i = 0; i < m; i++) {
      const cj = C(i + 1);
      let next, nA = A, nB = B;
      if (i + 1 < m) {
        const T = unit3([t[i][0] + t[i + 1][0], t[i][1] + t[i + 1][1], t[i][2] + t[i + 1][2]]);
        const kt = dot3(t[i], T);
        next = d.map((q) => {
          const s = -r * dot3(q, T) / kt;
          return [cj[0] + r * q[0] + s * t[i][0], cj[1] + r * q[1] + s * t[i][1], cj[2] + r * q[2] + s * t[i][2]];
        });
        // 틀을 최소 회전으로 다음 마디로 옮긴다 (로드리게스)
        const ax = cross3(t[i], t[i + 1]), sn = Math.hypot(ax[0], ax[1], ax[2]), cs = dot3(t[i], t[i + 1]);
        if (sn > 1e-9) {
          const k = [ax[0] / sn, ax[1] / sn, ax[2] / sn];
          const rot = (v) => {
            const kv = cross3(k, v), kd = dot3(k, v) * (1 - cs);
            return [v[0] * cs + kv[0] * sn + k[0] * kd, v[1] * cs + kv[1] * sn + k[1] * kd, v[2] * cs + kv[2] * sn + k[2] * kd];
          };
          nA = rot(A); nB = rot(B);
        }
      } else {
        next = d.map((q) => [cj[0] + r * q[0], cj[1] + r * q[1], cj[2] + r * q[2]]);
      }
      const b = VB.length / 9;
      ring.forEach((p, s) => vert(p, d[s]));
      next.forEach((p, s) => vert(p, d[s]));
      for (let s = 0; s < N; s++) {
        const s1 = (s + 1) % N;
        IB.push(b + s, b + s1, b + N + s1, b + s, b + N + s1, b + N + s);
      }
      ring = next;
      A = nA; B = nB;
      d = dirs(A, B);
    }
    cap(C(m), ring, t[m - 1], false);
  }

  // ---------------------------------------------------------------- 터파기 구덩이 (깊이 방식, WebGL)
  // 관 길 방향에 수직인 단면 (관중심 터파기):
  //   D = 관경 · g = 지표 · zc = 관 중심 (zcAt) · 모래 윗면 zs = 관 밑 = zc − D/2 · 바닥 zb = zs − bed
  //   바닥 반폭 wb = D/2 + margin · 높이 z 의 반폭 = wb + n·(z − zb) (zb ≤ z ≤ g − tp)
  //   포장 (g − tp ~ g) 은 직각: 반폭 wv = wb + n·(g − tp − zb) · g − tp < zb 이면 벽 전체 직각 wb
  // 모래는 관 밑까지 채운 면으로 그린다 (관이 모래 위에 놓임). 보이는 바닥 = zs, 그 반폭 ws.
  // 이어진 관 (같은 관경, 끝이 TR_TOL 안에서 만남) 은 한 줄로 이어 꺾인 곳을 이등분 면으로 붙인다 (구덩이가 끊기지 않게).
  // 끝: 아무것도 안 닿으면 여유폭만큼 더 파고 끝벽 · 다른 관에 닿으면 더 파지 않고, 닿은 관보다 넓을 때만 끝벽.
  const TR_TOL = 0.05;
  const TR_PAV = [0.40, 0.40, 0.42], TR_SOIL = [0.60, 0.42, 0.27], TR_SAND = [0.95, 0.82, 0.54];
  // 다 지은 층 (부분 개방 자른 면 · 단면 · 터파기 방식 채움): 포장 복구 · 되메우기 · 보호층 (모래)
  const TR_REST = [0.27, 0.28, 0.30], TR_BACK = [0.50, 0.34, 0.20], TR_PROT = [0.96, 0.89, 0.68];
  const LAY_CSS = { pav: '#45484f', fill: '#7d5634', prot: '#f0dfae', bed: '#d8b56a' }; // 같은 층 (그림 · 360 덧그림)
  // 단면 값 (관 길 방향에 수직): 관경 D · 지표 g · 관 중심 zc · 더 넓힘 ex (부분 개방) → { zs, zb, wb, zt, wv, zm, ws }
  function sectAt(D, g, zc, ex) {
    const zs = Math.min(zc - D / 2, g - 0.01), zb = zs - TR.bed, wb = D / 2 + TR.margin + (ex || 0), zt = g - TR.tp;
    const wv = wb + TR.n * Math.max(0, zt - zb);
    return zt > zs ? { zs, zb, wb, zt, wv, zm: zt, ws: wb + TR.n * (zs - zb) } : { zs, zb, wb, zt, wv, zm: zs, ws: wv };
  }
  // 보호층 윗면: 관 윗면 + prot (포장 밑 · 모래 윗면 사이로)
  const protTop = (x, zc, D) => Math.max(x.zs, Math.min(zc + D / 2 + TR.prot, x.zt));
  // 층 다각형 (단면 좌표 [w, z] · 볼록 · 오른쪽 아래 → 위 → 왼쪽 위 → 아래). X = { g, zt, zb, zs, wb, wv, zp } · zlo 아래는 자름
  function layerPolys(X, zlo) {
    const wAt = (z) => (z >= X.zt ? X.wv : X.wb + TR.n * Math.max(0, z - X.zb));
    const band = (z0, z1) => {
      z0 = Math.max(z0, zlo);
      if (!(z1 > z0 + 1e-4)) return null;
      const r = [[wAt(z0), z0]];
      if (z0 < X.zt && X.zt < z1) r.push([wAt(X.zt), X.zt]);
      r.push([wAt(z1), z1]);
      return r.concat(r.slice().reverse().map((q) => [-q[0], q[1]]));
    };
    return [['pav', band(Math.max(X.zt, X.zb), X.g)], ['fill', band(Math.max(X.zp, X.zb), X.zt)],
      ['prot', band(X.zs, X.zp)], ['bed', band(X.zb, Math.min(X.zs, X.zt))]].filter((q) => q[1]);
  }
  const TR_LIGHT = unit3([-0.25, 0.3, 1.0]), TR_UP = [0, 0, 1]; // 거의 위에서 오는 빛 (북서쪽으로 조금)
  let trChains = null, trChainsOf = null, trSec = null, trSecKey = '';
  const trGL = () => S.pipes && S.depth && trOn() && !!pprog && !!tprog;
  function trChainsGet() {
    if (trChains && trChainsOf === lines) return trChains;
    trChainsOf = lines;
    const ends = []; // 줄마다 [처음, 끝] 차례 → 끝 k 의 반대쪽 = k ^ 1 · d = 끝에서 줄 안쪽 방향
    lines.forEach((L, i) => {
      if (L.bi.length < 2) return;
      const P = L.pts, b = L.bi, z = b.length - 1;
      for (const [s, v, w] of [[0, b[0], b[1]], [1, b[z], b[z - 1]]]) {
        const dx = P[w * 3] - P[v * 3], dy = P[w * 3 + 1] - P[v * 3 + 1], dl = Math.hypot(dx, dy) || 1;
        ends.push({ i: i, s: s, e: P[v * 3], n: P[v * 3 + 1], dx: dx / dl, dy: dy / dl });
      }
    });
    // 가까운 끝끼리 묶기 (TR_TOL 칸 격자 + 이웃 칸)
    const grid = new Map(), gk = (x, y) => x + ',' + y, cellOf = (p) => [Math.floor(p.e / TR_TOL), Math.floor(p.n / TR_TOL)];
    ends.forEach((p, k) => { const c = gk(...cellOf(p)); if (!grid.has(c)) grid.set(c, []); grid.get(c).push(k); });
    const par = ends.map((p, k) => k);
    const root = (k) => { while (par[k] !== k) { par[k] = par[par[k]]; k = par[k]; } return k; };
    ends.forEach((p, k) => {
      const [cx, cy] = cellOf(p);
      for (let a = -1; a <= 1; a++) {
        for (let b = -1; b <= 1; b++) {
          for (const j of grid.get(gk(cx + a, cy + b)) || []) {
            if (j !== k && Math.hypot(ends[j].e - p.e, ends[j].n - p.n) <= TR_TOL) par[root(j)] = root(k);
          }
        }
      }
    });
    const groups = new Map();
    ends.forEach((p, k) => { const r = root(k); if (!groups.has(r)) groups.set(r, []); groups.get(r).push(k); });
    // 묶음마다 같은 관경 · 가장 곧게 이어지는 짝부터 잇는다 (둘뿐이면 꺾여도 · 셋 이상이면 45° 안만)
    const link = new Int32Array(ends.length).fill(-1);
    for (const g of groups.values()) {
      const free = g.slice();
      while (free.length >= 2) {
        let best = null, bc = Infinity;
        for (let x = 0; x < free.length; x++) {
          for (let y = x + 1; y < free.length; y++) {
            const p = ends[free[x]], q = ends[free[y]], A = lines[p.i], B = lines[q.i];
            if (p.i === q.i || Math.abs(A.dia - B.dia) > 1e-3 || A.hasZ !== B.hasZ) continue;
            const c = p.dx * q.dx + p.dy * q.dy; // −1 = 곧게 이어짐
            if (c < bc) { bc = c; best = [x, y]; }
          }
        }
        if (!best || (g.length > 2 && bc > -0.7071)) break;
        const a = free[best[0]], b = free[best[1]];
        link[a] = b; link[b] = a;
        free.splice(best[1], 1);
        free.splice(best[0], 1);
      }
    }
    const used = new Uint8Array(ends.length >> 1), raw = [];
    const walk = (k) => { // 끝 k 로 들어가 이어진 줄을 따라간다 → [L, 꼭짓점, L, 꼭짓점, …]
      const nd = [];
      while (k >= 0 && !used[k >> 1]) {
        used[k >> 1] = 1;
        const p = ends[k], L = lines[p.i], b = p.s ? L.bi.slice().reverse() : L.bi;
        for (let j = nd.length ? 1 : 0; j < b.length; j++) nd.push(L, b[j]); // 이음 꼭짓점은 한 번만
        k = link[k ^ 1];
      }
      return nd;
    };
    for (let k = 0; k < ends.length; k++) if (link[k] < 0 && !used[k >> 1]) raw.push(walk(k));
    for (let k = 0; k < ends.length; k++) if (!used[k >> 1]) raw.push(walk(k)); // 고리
    const chains = raw.filter((nd) => nd.length >= 4).map((nd) => {
      const m = nd.length / 2, P = new Float64Array(m * 2);
      let e0 = Infinity, e1 = -Infinity, n0 = Infinity, n1 = -Infinity;
      for (let j = 0; j < m; j++) {
        const L = nd[j * 2], k = nd[j * 2 + 1];
        P[j * 2] = L.pts[k * 3]; P[j * 2 + 1] = L.pts[k * 3 + 1];
        e0 = Math.min(e0, P[j * 2]); e1 = Math.max(e1, P[j * 2]); n0 = Math.min(n0, P[j * 2 + 1]); n1 = Math.max(n1, P[j * 2 + 1]);
      }
      const ids = [...new Set(nd.filter((x, j) => !(j & 1)).map((L) => L.src))]; // 든 관 (RV_PIPES 번호)
      return { nd: nd, m: m, P: P, D: nd[0].dia, ids: ids, e0, e1, n0, n1, ext: [true, true], wall: [true, true] };
    });
    // 끝마다 다른 관에 닿는지
    const dPoly = (x, y, c) => {
      let d = Infinity;
      for (let j = 0; j + 1 < c.m; j++) d = Math.min(d, distPtSeg(x, y, c.P[j * 2], c.P[j * 2 + 1], c.P[j * 2 + 2], c.P[j * 2 + 3]));
      return d;
    };
    for (const c of chains) {
      for (const sd of [0, 1]) {
        const j = sd ? c.m - 1 : 0, x = c.P[j * 2], y = c.P[j * 2 + 1];
        let touch = false, wider = true;
        for (const o of chains) {
          if (x < o.e0 - TR_TOL || x > o.e1 + TR_TOL || y < o.n0 - TR_TOL || y > o.n1 + TR_TOL) continue;
          if (o === c) { // 고리: 제 반대쪽 끝
            const jj = sd ? 0 : c.m - 1;
            if (c.m > 2 && Math.hypot(c.P[jj * 2] - x, c.P[jj * 2 + 1] - y) <= TR_TOL) { touch = true; wider = false; }
            continue;
          }
          if (dPoly(x, y, o) <= TR_TOL) { touch = true; if (o.D >= c.D - 1e-3) wider = false; }
        }
        c.ext[sd] = !touch;
        c.wall[sd] = !touch || wider;
      }
    }
    trChains = chains;
    return chains;
  }
  // 이은 줄마다 꼭짓점 단면: 자리 (E, N · 끝은 더 판 만큼 밀림) · 가로 방향 (LX, LY = 왼쪽 법선의 이등분 · 길이 1/cos) · 표고 · 반폭
  function trSecGet() {
    const ch = trChainsGet();
    const key = [TR.tp, TR.n, TR.margin, TR.bed, TR.prot, S.cover, S.gz].join('|');
    if (trSec && trSecKey === key && trSec.of === ch) return trSec;
    trSecKey = key;
    const out = [], ext = Math.max(TR.margin, 0.05); // 여유폭 0 이어도 끝벽이 관 마구리와 겹치지 않게
    for (const c of ch) {
      const m = c.m, P = c.P, F = () => new Float64Array(m);
      const E = F(), N = F(), LX = F(), LY = F(), G = F(), ZM = F(), ZS = F(), WV = F(), WS = F();
      const ZB = F(), ZT = F(), WB = F(), ZP = F(); // 층 채움 (보호층 윗면 ZP · 되메우기 윗면 ZT)
      const tx = new Float64Array(m - 1), ty = new Float64Array(m - 1);
      for (let j = 0; j + 1 < m; j++) {
        const dx = P[j * 2 + 2] - P[j * 2], dy = P[j * 2 + 3] - P[j * 2 + 1], l = Math.hypot(dx, dy) || 1;
        tx[j] = dx / l; ty[j] = dy / l;
      }
      for (let j = 0; j < m; j++) {
        const a = Math.max(0, j - 1), b = Math.min(m - 2, j);
        let qx = -ty[a] - ty[b], qy = tx[a] + tx[b];
        const ql = Math.hypot(qx, qy);
        if (ql < 1e-6) { qx = -ty[b]; qy = tx[b]; } else {
          qx /= ql; qy /= ql;
          const k = 1 / Math.max(-qx * ty[b] + qy * tx[b], 0.3);
          qx *= k; qy *= k;
        }
        LX[j] = qx; LY[j] = qy;
        E[j] = P[j * 2]; N[j] = P[j * 2 + 1];
        const L = c.nd[j * 2], k = c.nd[j * 2 + 1], D = L.dia, g = gAt(L, k), zc = zcAt(L, k);
        const x = sectAt(D, g, zc, 0);
        G[j] = g; ZS[j] = x.zs; WV[j] = x.wv; ZM[j] = x.zm; WS[j] = x.ws;
        ZB[j] = x.zb; ZT[j] = x.zt; WB[j] = x.wb; ZP[j] = protTop(x, zc, D);
      }
      if (c.ext[0]) { E[0] -= tx[0] * ext; N[0] -= ty[0] * ext; }
      if (c.ext[1]) { E[m - 1] += tx[m - 2] * ext; N[m - 1] += ty[m - 2] * ext; }
      const pad = ext + 10;
      out.push({ c, m, E, N, LX, LY, G, ZM, ZS, WV, WS, ZB, ZT, WB, ZP, e0: c.e0 - pad, e1: c.e1 + pad, n0: c.n0 - pad, n1: c.n1 + pad });
    }
    out.of = ch;
    trSec = out;
    return out;
  }
  // 고른 관이 없는 이은 줄의 모양 (끝이 닿는지 바뀌면 그대로 둔 그물도 다시 만들게)
  function trStaticSig(sel) {
    let s = '';
    for (const c of trChainsGet()) {
      if (c.ids.indexOf(sel) < 0) s += c.ids.join(',') + ':' + (+c.ext[0]) + (+c.ext[1]) + (+c.wall[0]) + (+c.wall[1]) + ';';
    }
    return s;
  }
  // 구덩이 그물 짓기 (좌표 = o 기준 m): 구멍 (스텐실) OI · 안쪽 면 II · 반투명 층 채움 FI → 한 그물 (차례대로).
  // T = 단면 묶음 (trSecGet 하나 또는 부분 개방 조각): E, N, LX, LY (w + 왼쪽) · G, ZM, ZS, WV, WS (+ 채움: ZB, ZT, WB, ZP · 자른 면: X)
  function pitMesh(o) {
    const VB = [], OI = [], II = [], FI = [];
    const vtx = (p, nr, c) => { VB.push(p[0], p[1], p[2], nr[0], nr[1], nr[2], c[0], c[1], c[2]); };
    const pt = (T, j, w, z) => [T.E[j] + T.LX[j] * w - o[0], T.N[j] + T.LY[j] * w - o[1], z - o[2]]; // w + 왼쪽 · − 오른쪽
    const dk = (T, j, z) => 1 - 0.35 * clamp((T.G[j] - z) / Math.max(T.G[j] - T.ZS[j], 1e-3), 0, 1); // 깊을수록 어둡게
    // 볼록 면 (네 점 = 두 대각선의 곱 · 더 많으면 Newell): 법선을 hint 쪽으로 · 빛 × 꼭짓점 어둡기 sh 를 색에 굽는다 · to = 번호 묶음
    const face = (ps, hint, base, sh, to) => {
      let nr;
      if (ps.length === 4) {
        const d1 = [ps[2][0] - ps[0][0], ps[2][1] - ps[0][1], ps[2][2] - ps[0][2]];
        const d2 = [ps[3][0] - ps[1][0], ps[3][1] - ps[1][1], ps[3][2] - ps[1][2]];
        nr = cross3(d1, d2);
      } else {
        nr = [0, 0, 0];
        for (let i = 0; i < ps.length; i++) {
          const a = ps[i], b = ps[(i + 1) % ps.length];
          nr[0] += (a[1] - b[1]) * (a[2] + b[2]); nr[1] += (a[2] - b[2]) * (a[0] + b[0]); nr[2] += (a[0] - b[0]) * (a[1] + b[1]);
        }
      }
      const l = Math.hypot(nr[0], nr[1], nr[2]);
      if (l < 1e-9) return;
      const sg = dot3(nr, hint) < 0 ? -1 / l : 1 / l;
      nr = [nr[0] * sg, nr[1] * sg, nr[2] * sg];
      const lam = 0.62 + 0.38 * Math.max(0, dot3(nr, TR_LIGHT));
      const b = VB.length / 9, out = to || II;
      ps.forEach((p, i) => { const s = lam * sh[i]; vtx(p, nr, [base[0] * s, base[1] * s, base[2] * s]); });
      for (let i = 1; i + 1 < ps.length; i++) out.push(b, b + i, b + i + 1);
    };
    const seg = (T, a, b, bot) => {
      // 구멍 (지표 ±wv 띠) → 스텐실
      const ob = VB.length / 9;
      [pt(T, a, T.WV[a], T.G[a]), pt(T, b, T.WV[b], T.G[b]), pt(T, b, -T.WV[b], T.G[b]), pt(T, a, -T.WV[a], T.G[a])].forEach((p) => vtx(p, TR_UP, TR_UP));
      OI.push(ob, ob + 1, ob + 2, ob, ob + 2, ob + 3);
      for (const sg of [1, -1]) { // 벽: 포장 (직각) · 흙 (기울기)
        const hint = [-sg * T.LX[a], -sg * T.LY[a], 0];
        const ta = pt(T, a, sg * T.WV[a], T.G[a]), tb = pt(T, b, sg * T.WV[b], T.G[b]);
        const ma = pt(T, a, sg * T.WV[a], T.ZM[a]), mb = pt(T, b, sg * T.WV[b], T.ZM[b]);
        const ba = pt(T, a, sg * T.WS[a], T.ZS[a]), bb = pt(T, b, sg * T.WS[b], T.ZS[b]);
        face([ta, tb, mb, ma], hint, TR_PAV, [dk(T, a, T.G[a]), dk(T, b, T.G[b]), dk(T, b, T.ZM[b]), dk(T, a, T.ZM[a])]);
        face([ma, mb, bb, ba], hint, TR_SOIL, [dk(T, a, T.ZM[a]), dk(T, b, T.ZM[b]), dk(T, b, T.ZS[b]), dk(T, a, T.ZS[a])]);
      }
      // 바닥 (모래 윗면): 벽 쪽 가장자리를 조금 어둡게 (세 띠)
      const lat = (j) => { const w = T.WS[j], i = w - Math.min(0.12, 0.3 * w); return [w, i, -i, -w]; };
      const la = lat(a), lb = lat(b), ao = [0.66, 0.8, 0.8, 0.66];
      for (let q = 0; q < 3; q++) {
        face([pt(T, a, la[q], T.ZS[a]), pt(T, b, lb[q], T.ZS[b]), pt(T, b, lb[q + 1], T.ZS[b]), pt(T, a, la[q + 1], T.ZS[a])],
          TR_UP, bot, [ao[q], ao[q], ao[q + 1], ao[q + 1]]);
      }
    };
    const endWall = (T, j, hint) => {
      const P = (w, z) => pt(T, j, w, z);
      face([P(T.WV[j], T.G[j]), P(T.WV[j], T.ZM[j]), P(-T.WV[j], T.ZM[j]), P(-T.WV[j], T.G[j])], hint, TR_PAV,
        [dk(T, j, T.G[j]), dk(T, j, T.ZM[j]), dk(T, j, T.ZM[j]), dk(T, j, T.G[j])]);
      face([P(T.WV[j], T.ZM[j]), P(T.WS[j], T.ZS[j]), P(-T.WS[j], T.ZS[j]), P(-T.WV[j], T.ZM[j])], hint, TR_SOIL,
        [dk(T, j, T.ZM[j]), dk(T, j, T.ZS[j]), dk(T, j, T.ZS[j]), dk(T, j, T.ZM[j])]);
    };
    // 층 채움 윗면 (반투명 · FI): 높이 Z 에서 벽에서 벽까지
    const wAt = (T, j, z) => (z >= T.ZT[j] ? T.WV[j] : T.WB[j] + TR.n * Math.max(0, z - T.ZB[j]));
    const ribbon = (T, a, b, Z, col) => {
      const wa = wAt(T, a, Z[a]), wb = wAt(T, b, Z[b]);
      face([pt(T, a, wa, Z[a]), pt(T, b, wb, Z[b]), pt(T, b, -wb, Z[b]), pt(T, a, -wa, Z[a])], TR_UP, col, [1, 1, 1, 1], FI);
    };
    // 자른 면 (부분 개방 끝): 다 지은 층 (포장 복구 · 되메우기 · 보호층 · 바닥 = 기초 윗면) + 관 (5 mm 안쪽) · 더 넓힌 곳 = 원래 땅
    const cut = (T, j, hint, pipeRgb) => {
      const X = T.X[j], P = (w, z, k) => { const q = pt(T, j, w, z); q[0] += hint[0] * k; q[1] += hint[1] * k; return q; };
      const col = { pav: TR_REST, fill: TR_BACK, prot: TR_PROT, bed: TR_SAND };
      for (const [k, pg] of layerPolys(X, X.zs)) face(pg.map((q) => P(q[0], q[1], 0)), hint, col[k], pg.map(() => 1.25)); // 층이 잘 보이게 조금 밝게
      const ex = T.WV[j] - X.wv; // 더 넓힌 폭: 터파기 밖 = 포장 · 흙
      if (ex > 1e-4) {
        const wIn = (z) => (z >= X.zt ? X.wv : X.wb + TR.n * Math.max(0, z - X.zb));
        const zs = [X.zs, X.zt, X.g].filter((z, i, a) => z >= X.zs && (i === 0 || z > a[i - 1] + 1e-4));
        for (let i = 0; i + 1 < zs.length; i++) {
          const z0 = zs[i], z1 = zs[i + 1], c = z0 >= X.zt - 1e-6 ? TR_PAV : TR_SOIL;
          for (const sg of [1, -1]) {
            face([P(sg * wIn(z0), z0, 0), P(sg * wIn(z1), z1, 0), P(sg * (wIn(z1) + ex), z1, 0), P(sg * (wIn(z0) + ex), z0, 0)], hint, c, [0.9, 0.9, 0.9, 0.9]);
          }
        }
      }
      const r = X.D / 2, ring = [], pc = pipeRgb.map((v) => v * 0.85);
      for (let i = 0; i < 24; i++) { const a = i / 24 * 2 * Math.PI; ring.push(P(r * Math.cos(a), X.zc + r * Math.sin(a), 0.005)); }
      face(ring, hint, pc, ring.map(() => 1.25));
    };
    return {
      seg, endWall, ribbon, cut,
      done: () => ({ o: o, v: Float32Array.from(VB), i: Uint32Array.from(OI.concat(II, FI)), no: OI.length, nf: FI.length }),
    };
  }
  // 지금 자리에서 FADE1 안의 구덩이 그물 (이은 줄 use(c) 인 것만 · 카메라 기준 m): 구멍 삼각형 no 개 뒤에 안쪽 면 · 끝에 층 채움 nf 개.
  // 바닥 색 = 기초 · 보호층 켬이면 모래 · 채움 = 보호층 윗면 (기초 · 보호층) · 되메우기 윗면 (되메우기 층) — 깊은 것부터 (위에서 보는 빛 차례)
  function buildTrench(use) {
    const o = S.cam.slice(), B = pitMesh(o);
    const bot = TR.bed > 0 && SHOW.bed ? TR_SAND : TR_SOIL;
    const near = [];
    for (const T of trSecGet()) {
      if (!use(T.c)) continue;
      const bx = Math.max(T.e0 - o[0], 0, o[0] - T.e1), by = Math.max(T.n0 - o[1], 0, o[1] - T.n1);
      if (bx * bx + by * by > FADE1 * FADE1) continue;
      const m = T.m;
      for (let j = 0; j + 1 < m; j++) {
        if (distPtSeg(o[0], o[1], T.E[j], T.N[j], T.E[j + 1], T.N[j + 1]) > FADE1) continue;
        near.push([T, j]);
        B.seg(T, j, j + 1, bot);
        if (j === 0 && T.c.wall[0]) B.endWall(T, 0, unit3([T.E[1] - T.E[0], T.N[1] - T.N[0], 0]));
        if (j === m - 2 && T.c.wall[1]) B.endWall(T, m - 1, unit3([T.E[m - 2] - T.E[m - 1], T.N[m - 2] - T.N[m - 1], 0]));
      }
    }
    if (SHOW.bed && TR.prot > 0) {
      for (const [T, j] of near) if (T.ZP[j] > T.ZS[j] + 1e-3 && T.ZP[j + 1] > T.ZS[j + 1] + 1e-3) B.ribbon(T, j, j + 1, T.ZP, TR_PROT);
    }
    if (SHOW.fill) {
      for (const [T, j] of near) if (T.ZT[j] > T.ZP[j] + 1e-3 && T.ZT[j + 1] > T.ZP[j + 1] + 1e-3) B.ribbon(T, j, j + 1, T.ZT, TR_BACK);
    }
    return B.done();
  }
  // 구덩이 그리기. 그린 것이 있으면 스텐실 시험 (구멍 안 = 1) 을 켠 채로 true → 관도 구멍 안에서만.
  // 그물 둘: 고른 관이 든 이은 줄 (끄는 동안 이것만 다시) · 나머지 (이은 모양이 바뀔 때만 다시)
  function drawTrench(base, sel) {
    const tb = base + '|' + [TR.tp, TR.n, TR.margin, TR.bed, TR.prot, +SHOW.bed, +SHOW.fill].join(',') + '|' + pipeGen + '|' + sel;
    meshPut(pitM[0], tb + '|' + (sel >= 0 ? trStaticSig(sel) : ''), () => buildTrench((c) => c.ids.indexOf(sel) < 0));
    meshPut(pitM[1], sel < 0 ? '' : tb + '|' + selGen, () => (sel < 0 ? NO_MESH : buildTrench((c) => c.ids.indexOf(sel) >= 0)));
    return pitDraw(pitM);
  }
  const tOff = (m) => {
    gl.bindVertexArray(m.vao);
    gl.uniform3f(TU.u_off, m.o[0] - S.cam[0], m.o[1] - S.cam[1], m.o[2] - S.cam[2]);
  };
  function tUse(alpha) {
    gl.useProgram(tprog);
    gl.uniform3fv(TU.u_f, V.f);
    gl.uniform3fv(TU.u_r, V.r);
    gl.uniform3fv(TU.u_u, V.u);
    gl.uniform2f(TU.u_tan, V.tanH, V.tanV);
    gl.uniform2f(TU.u_nf, 0.1, 2000);
    gl.uniform2f(TU.u_fade, FADE0, FADE1);
    gl.uniform1f(TU.u_alpha, alpha);
  }
  // 구멍 → 스텐실 1 · 안쪽 면 (구멍 안 · 깊이 씀). 그린 것이 없으면 false.
  function pitDraw(list) {
    const ms = list.filter((m) => m.no);
    if (!ms.length) return false;
    tUse(1);
    gl.enable(gl.STENCIL_TEST);
    gl.stencilMask(0xff);
    gl.clearStencil(0);
    gl.clear(gl.STENCIL_BUFFER_BIT);
    // 1) 구멍 → 스텐실 1 (색 · 깊이는 안 씀)
    gl.disable(gl.DEPTH_TEST);
    gl.depthMask(false);
    gl.colorMask(false, false, false, false);
    gl.stencilFunc(gl.ALWAYS, 1, 0xff);
    gl.stencilOp(gl.KEEP, gl.KEEP, gl.REPLACE);
    ms.forEach((m) => { tOff(m); gl.drawElements(gl.TRIANGLES, m.no, gl.UNSIGNED_INT, 0); });
    // 2) 안쪽 면: 구멍 안에서만 · 깊이 씀 (관이 이 깊이와 견줌)
    gl.colorMask(true, true, true, true);
    gl.depthMask(true);
    gl.enable(gl.DEPTH_TEST);
    gl.depthFunc(gl.LESS);
    gl.stencilFunc(gl.EQUAL, 1, 0xff);
    gl.stencilOp(gl.KEEP, gl.KEEP, gl.KEEP);
    gl.enable(gl.BLEND);
    gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ZERO, gl.ONE);
    ms.forEach((m) => { const k = m.n - m.no - m.nf; if (k > 0) { tOff(m); gl.drawElements(gl.TRIANGLES, k, gl.UNSIGNED_INT, m.no * 4); } });
    gl.disable(gl.BLEND);
    gl.disable(gl.DEPTH_TEST);
    gl.bindVertexArray(null);
    return true;
  }
  // 층 채움 (반투명): 구멍 안 · 관 · 벽 깊이와 견줌 (안 씀). 스텐실은 켠 채로 부른다.
  function pitFill(list) {
    const ms = list.filter((m) => m.nf);
    if (!ms.length) return;
    tUse(FILL_A);
    gl.enable(gl.DEPTH_TEST);
    gl.depthFunc(gl.LEQUAL);
    gl.depthMask(false);
    gl.enable(gl.BLEND);
    gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ZERO, gl.ONE);
    ms.forEach((m) => { tOff(m); gl.drawElements(gl.TRIANGLES, m.nf, gl.UNSIGNED_INT, (m.n - m.nf) * 4); });
    gl.disable(gl.BLEND);
    gl.disable(gl.DEPTH_TEST);
    gl.depthMask(true);
    gl.bindVertexArray(null);
  }

  // ---------------------------------------------------------------- 부분 개방 (누른 관 자리만 땅을 엶)
  // 누른 자리 (OP) → 그 관이 든 이은 줄 (trChainsGet) 위 측점 s (꼭짓점 c.P 따라 평면 길이) → [s − 길이/2, s + 길이/2] (줄 안으로 자름).
  // 조각 단면: 두 자른 곳 (그 마디 수직 · g · zc 는 꼭짓점 사이 선형) + 사이 꼭짓점 (이은 줄 단면 그대로) · 폭 = 터파기 + OPT.ex.
  // 안 = 판 모습 (벽 · 모래 바닥 · 관) · 두 끝 자른 면 = 다 지은 층. 닫기 / Esc = 매설로.
  function opRes() {
    if (S.mode !== 'open' || !OP) return null;
    const key = [OP.src, OP.e, OP.n, OPT.len, OPT.ex, TR.tp, TR.n, TR.margin, TR.bed, TR.prot, S.cover, S.gz, pipeGen, selGen].join('|');
    if (OPR && OPR.key === key && OPR.of === lines) return OPR.r;
    OPR = { key: key, of: lines, r: null };
    let best = null;
    for (const T of trSecGet()) {
      if (T.c.ids.indexOf(OP.src) < 0) continue;
      const P = T.c.P;
      let acc = 0;
      for (let j = 0; j + 1 < T.m; j++) {
        const ax = P[j * 2], ay = P[j * 2 + 1], dx = P[j * 2 + 2] - ax, dy = P[j * 2 + 3] - ay, l = Math.hypot(dx, dy);
        const t = l > 0 ? clamp(((OP.e - ax) * dx + (OP.n - ay) * dy) / (l * l), 0, 1) : 0;
        const d = Math.hypot(OP.e - ax - t * dx, OP.n - ay - t * dy);
        if (!best || d < best.d) best = { T: T, d: d, s: acc + t * l };
        acc += l;
      }
      if (best && best.T === T) best.tot = acc;
    }
    if (!best) return null;
    const T = best.T, c = T.c, P = c.P, m = T.m, tot = best.tot;
    const s0 = clamp(best.s - OPT.len / 2, 0, tot), s1 = clamp(best.s + OPT.len / 2, 0, tot);
    if (!(s1 - s0 > 0.05)) return null;
    const cum = [0];
    for (let j = 0; j + 1 < m; j++) cum.push(cum[j] + Math.hypot(P[j * 2 + 2] - P[j * 2], P[j * 2 + 3] - P[j * 2 + 1]));
    const U = { m: 0, E: [], N: [], LX: [], LY: [], G: [], ZM: [], ZS: [], WV: [], WS: [], X: [], TD: [] }; // TD = 자른 곳 관 길 방향
    const add = (e, n, lx, ly, g, zc, D) => {
      const x = sectAt(D, g, zc, OPT.ex), x0 = sectAt(D, g, zc, 0);
      U.E.push(e); U.N.push(n); U.LX.push(lx); U.LY.push(ly); U.G.push(g);
      U.ZM.push(x.zm); U.ZS.push(x.zs); U.WV.push(x.wv); U.WS.push(x.ws);
      U.X.push(Object.assign(x0, { g: g, zc: zc, D: D, zp: protTop(x0, zc, D) }));
      U.m++;
    };
    const at = (s) => { // 측점 s 의 자리 · 마디 수직 · g · zc
      let j = 0;
      while (j + 2 < m && cum[j + 1] < s) j++;
      const l = cum[j + 1] - cum[j], t = l > 0 ? clamp((s - cum[j]) / l, 0, 1) : 0;
      const ax = P[j * 2], ay = P[j * 2 + 1], dx = P[j * 2 + 2] - ax, dy = P[j * 2 + 3] - ay, dl = Math.hypot(dx, dy) || 1;
      const La = c.nd[j * 2], ka = c.nd[j * 2 + 1], Lb = c.nd[j * 2 + 2], kb = c.nd[j * 2 + 3];
      U.TD.push([dx / dl, dy / dl, 0]);
      add(ax + dx * t, ay + dy * t, -dy / dl, dx / dl, gAt(La, ka) + (gAt(Lb, kb) - gAt(La, ka)) * t, zcAt(La, ka) + (zcAt(Lb, kb) - zcAt(La, ka)) * t, c.D);
    };
    at(s0);
    for (let j = 1; j + 1 < m; j++) {
      if (cum[j] > s0 + 1e-6 && cum[j] < s1 - 1e-6) { const L = c.nd[j * 2], k = c.nd[j * 2 + 1]; add(P[j * 2], P[j * 2 + 1], T.LX[j], T.LY[j], gAt(L, k), zcAt(L, k), c.D); }
    }
    at(s1);
    U.e0 = Math.min(...U.E) - 10; U.e1 = Math.max(...U.E) + 10; U.n0 = Math.min(...U.N) - 10; U.n1 = Math.max(...U.N) + 10;
    const L0 = lines.find((q) => q.src === OP.src);
    OPR.r = { key: key, T: U, s: best.s, s0: s0, s1: s1, tot: tot, rgb: L0 ? L0.rgb : [0, 0.9, 1] };
    return OPR.r;
  }
  function buildOpening(R) {
    const o = S.cam.slice(), B = pitMesh(o), U = R.T, m = U.m;
    for (let j = 0; j + 1 < m; j++) B.seg(U, j, j + 1, TR.bed > 0 ? TR_SAND : TR_SOIL);
    const a = U.TD[0], b = U.TD[1]; // 자른 면 안쪽 = 열린 곳 쪽
    B.cut(U, 0, a, R.rgb);
    B.cut(U, m - 1, [-b[0], -b[1], 0], R.rgb);
    return B.done();
  }
  // 열린 곳만: 구멍 → 안쪽 면 · 자른 면 → 지장물 (불투명) · 관 → 스텐실 끔
  function drawOpening() {
    const R = opRes(), sel = edSel(), base = camBase();
    meshPut(opM, base + '|' + R.key, () => buildOpening(R));
    if (!pitDraw([opM])) return;
    if (obShow()) { obPut(); if (obM.n) drawTubeMesh([obM], 1); }
    if (S.pipes) { tubesPut(sel, base); if (tubeM[0].n || tubeM[1].n) drawTubeMesh(tubeM); }
    gl.disable(gl.STENCIL_TEST);
  }

  // ---------------------------------------------------------------- 사진 불러오기
  const panoCache = new Map(); // id → Image (오래된 것부터 버림)
  function cachePut(id, img) {
    panoCache.delete(id);
    panoCache.set(id, img);
    while (panoCache.size > CACHE_PANOS) panoCache.delete(panoCache.keys().next().value);
  }
  function loadPano(s) {
    const id = s.id;
    if (panoCache.has(id)) { const img = panoCache.get(id); cachePut(id, img); return Promise.resolve(img); }
    const old = panoWait.get(id);
    if (old) return old.p;
    const w = {};
    w.p = new Promise((resolve, reject) => {
      let done = false;
      const sc = document.createElement('script');
      const fail = (why) => {
        if (done) return;
        done = true;
        panoWait.delete(id);
        sc.remove();
        reject(new Error(why));
      };
      w.got = (uri) => {
        if (done) return;
        done = true;
        panoWait.delete(id);
        const img = new Image();
        img.onload = () => {
          const ok = () => { cachePut(id, img); resolve(img); };
          if (img.decode) img.decode().then(ok, ok); else ok();
        };
        img.onerror = () => reject(new Error('decode'));
        img.src = uri;
      };
      sc.onload = () => {
        sc.remove();
        if (!done && panoStray) { const u = panoStray; panoStray = null; w.got(u); }
        if (!done) fail('empty');
      };
      sc.onerror = () => fail('missing');
      sc.async = true;
      sc.src = s.file;
      document.head.appendChild(sc);
    });
    panoWait.set(id, w);
    return w.p;
  }
  function preload(i) {
    const list = [shots[i + 1], shots[i - 1]].filter(Boolean);
    list.reduce((p, s) => p.then(() => loadPano(s).then(() => {}, () => {})), Promise.resolve());
  }
  function showLoading(on) { $('loading').classList.toggle('show', !!on); }

  function openShot(i) {
    i = clamp(i | 0, 0, shots.length - 1);
    if (i === S.want) return;
    S.want = i;
    const s = shots[i];
    if (!panoCache.has(s.id)) showLoading(true);
    loadPano(s).then((img) => {
      if (S.want !== i) return;
      try { setTexture(img); glImg = img; } catch (e) { setBlank(); glImg = null; toast('사진 오류 · ' + s.id); }
      applyShot(i);
    }, () => {
      if (S.want !== i) return;
      setBlank();
      glImg = null;
      applyShot(i);
      toast('사진 없음 · ' + s.id);
    });
  }
  function applyShot(i) {
    S.cur = i;
    S.hover = -1;
    showLoading(false);
    updCam();
    if (S.look) { lookAt(S.look); S.look = null; } // 간섭 목록에서 누른 곳 (가까운 사진으로 옮긴 뒤)
    cardHide(); // 관 정보 칸은 화면 자리 → 사진이 바뀌면 닫음 (단면 · 부분 개방은 땅 자리라 그대로)
    updInfo();
    updCalUI();
    followMap();
    dirty();
    preload(i);
  }
  function stepShot(d) {
    const base = S.want >= 0 ? S.want : S.cur;
    const j = base + d;
    if (j >= 0 && j < shots.length) openShot(j);
  }
  // 보는 방향 기준으로 옆 사진에 (로드뷰처럼): off = 0 앞 · 180 뒤 · −90 왼쪽 · 90 오른쪽.
  // 이웃 원판과 같은 맞춘 자리 (e, n + 그 영상 dx, dy) · NEIGH_R 안 · 그 방향 ±45° 안에서
  // 거리 · (1 + 2·(각/45°)²) 가 가장 작은 사진. 보는 방향은 그대로.
  function moveDir(off) {
    const bi = S.want >= 0 ? S.want : S.cur;
    if (bi < 0) return;
    const b = shots[bi], cb = calibFor(b.video), be = b.e + cb.dx, bn = b.n + cb.dy;
    const want = S.yaw + off;
    let best = -1, bs = Infinity;
    for (const s of shots) {
      if (s.i === bi) continue;
      const c = calibFor(s.video), de = s.e + c.dx - be, dn = s.n + c.dy - bn, d = Math.hypot(de, dn);
      if (d > NEIGH_R || d < 1e-3) continue;
      const ang = Math.abs(((Math.atan2(de, dn) * R2D - want) % 360 + 540) % 360 - 180);
      if (ang > 45) continue;
      const sc = d * (1 + 2 * (ang / 45) * (ang / 45));
      if (sc < bs) { bs = sc; best = s.i; }
    }
    if (best >= 0) openShot(best); else toast('그쪽 사진 없음');
  }

  // ---------------------------------------------------------------- 겹쳐 그리기 (관로 · 이웃 사진)
  const A3 = [0, 0, 0], B3 = [0, 0, 0], SEG = [0, 0, 0, 0];

  // 2D 선분을 네모 안으로 자른다 (Liang–Barsky).
  function clip2(x1, y1, x2, y2, x0, y0, xm, ym, out) {
    const dx = x2 - x1, dy = y2 - y1;
    let t0 = 0, t1 = 1;
    function edge(p, q) {
      if (p === 0) return q >= 0;
      const t = q / p;
      if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; } else { if (t < t0) return false; if (t < t1) t1 = t; }
      return true;
    }
    if (!edge(-dx, x1 - x0) || !edge(dx, xm - x1) || !edge(-dy, y1 - y0) || !edge(dy, ym - y1)) return false;
    out[0] = x1 + t0 * dx; out[1] = y1 + t0 * dy; out[2] = x1 + t1 * dx; out[3] = y1 + t1 * dy;
    return true;
  }
  function distPtSeg(px, py, ax, ay, bx, by) {
    const dx = bx - ax, dy = by - ay, L = dx * dx + dy * dy;
    const t = L > 0 ? clamp(((px - ax) * dx + (py - ay) * dy) / L, 0, 1) : 0;
    return Math.hypot(px - ax - t * dx, py - ay - t * dy);
  }
  function roundRect(c, x, y, w, h, r) {
    c.beginPath();
    if (c.roundRect) { c.roundRect(x, y, w, h, r); return; }
    c.moveTo(x + r, y); c.arcTo(x + w, y, x + w, y + h, r); c.arcTo(x + w, y + h, x, y + h, r);
    c.arcTo(x, y + h, x, y, r); c.arcTo(x, y, x + w, y, r); c.closePath();
  }

  function drawOverlay() {
    const w = V.w, h = V.h;
    ov.setTransform(V.dpr, 0, 0, V.dpr, 0, 0);
    ov.clearRect(0, 0, w, h);
    S.marks = [];
    if (S.cur < 0) return;
    const cx = w / 2, cy = h / 2, kx = cx / V.tanH, ky = cy / V.tanV;
    const Cx = S.cam[0], Cy = S.cam[1], Cz = S.cam[2];
    const f = V.f, r = V.r, u = V.u;
    const mg = 2 * Math.max(w, h);
    const vc = (px, py, pz, o) => {
      const dx = px - Cx, dy = py - Cy, dz = pz - Cz;
      o[0] = dx * r[0] + dy * r[1];
      o[1] = dx * u[0] + dy * u[1] + dz * u[2];
      o[2] = dx * f[0] + dy * f[1] + dz * f[2];
    };
    // 3D 에서 가까운 면으로 자른 뒤 투영, 화면 둘레로 한 번 더 자른다.
    const projSeg = (a, b, out) => {
      let ax = a[0], ay = a[1], az = a[2], bx = b[0], by = b[1], bz = b[2];
      if (az < NEAR && bz < NEAR) return false;
      if (az < NEAR) { const t = (NEAR - az) / (bz - az); ax += (bx - ax) * t; ay += (by - ay) * t; az = NEAR; }
      else if (bz < NEAR) { const t = (NEAR - bz) / (az - bz); bx += (ax - bx) * t; by += (ay - by) * t; bz = NEAR; }
      return clip2(cx + ax / az * kx, cy - ay / az * ky, cx + bx / bz * kx, cy - by / bz * ky, -mg, -mg, w + mg, h + mg, out);
    };

    // 볼록 다각형 (카메라 좌표 x,y,z 를 잇달아) → 가까운 면 · 화면 둘레로 자른 화면 다각형 [x,y,…] (Sutherland–Hodgman)
    const SIDES = [[1, 0, mg], [-1, 0, w + mg], [0, 1, mg], [0, -1, h + mg]]; // a·x + b·y + c ≥ 0
    const projPoly = (Q) => {
      const o = [], n = Q.length / 3;
      for (let i = 0; i < n; i++) {
        const j = (i + n - 1) % n, az = Q[j * 3 + 2], bz = Q[i * 3 + 2];
        if ((az >= NEAR) !== (bz >= NEAR)) {
          const t = (NEAR - az) / (bz - az);
          o.push(Q[j * 3] + (Q[i * 3] - Q[j * 3]) * t, Q[j * 3 + 1] + (Q[i * 3 + 1] - Q[j * 3 + 1]) * t, NEAR);
        }
        if (bz >= NEAR) o.push(Q[i * 3], Q[i * 3 + 1], bz);
      }
      if (o.length < 9) return null;
      let s = [];
      for (let i = 0; i < o.length; i += 3) s.push(cx + o[i] / o[i + 2] * kx, cy - o[i + 1] / o[i + 2] * ky);
      for (const [a, b, c] of SIDES) {
        const q = [], m = s.length / 2;
        for (let i = 0; i < m; i++) {
          const j = (i + m - 1) % m;
          const da = a * s[j * 2] + b * s[j * 2 + 1] + c, db = a * s[i * 2] + b * s[i * 2 + 1] + c;
          if ((da >= 0) !== (db >= 0)) { const t = da / (da - db); q.push(s[j * 2] + (s[i * 2] - s[j * 2]) * t, s[j * 2 + 1] + (s[i * 2 + 1] - s[j * 2 + 1]) * t); }
          if (db >= 0) q.push(s[i * 2], s[i * 2 + 1]);
        }
        s = q;
        if (s.length < 6) return null;
      }
      // 돌림 방향을 하나로 (nonzero 채우기에서 겹친 곳이 구멍이 되지 않게)
      let ar = 0;
      for (let i = 0, m = s.length / 2; i < m; i++) { const j = (i + 1) % m; ar += s[i * 2] * s[j * 2 + 1] - s[j * 2] * s[i * 2 + 1]; }
      if (ar < 0) { const t = []; for (let i = s.length - 2; i >= 0; i -= 2) t.push(s[i], s[i + 1]); s = t; }
      return s;
    };

    const addEdge = (bk, p, q) => { vc(p[0], p[1], p[2], A3); vc(q[0], q[1], q[2], B3); if (projSeg(A3, B3, SEG)) bk.edge.push(SEG[0], SEG[1], SEG[2], SEG[3]); };
    // 땅 위 점에서 가로 방향 (nx, ny) 으로 화면 폭 2 px 가 되는 반폭 (m)
    const H3 = [0, 0, 0], J1 = [0, 0], J2 = [0, 0];
    const jac = (dx, dy, o) => {
      const z = H3[2], vx = dx * r[0] + dy * r[1], vy = dx * u[0] + dy * u[1], vz = dx * f[0] + dy * f[1];
      o[0] = kx * (vx * z - H3[0] * vz) / (z * z);
      o[1] = -ky * (vy * z - H3[1] * vz) / (z * z);
    };
    const hwMin = (px, py, pz, nx, ny) => {
      vc(px, py, pz, H3);
      if (H3[2] < NEAR) return 0;
      jac(nx, ny, J1);
      jac(ny, -nx, J2);
      const tl = Math.hypot(J2[0], J2[1]);
      const per = tl > 1e-9 ? Math.abs(J1[0] * J2[1] - J1[1] * J2[0]) / tl : Math.hypot(J1[0], J1[1]);
      return per > 1e-9 ? 1 / per : Infinity;
    };
    const Q = new Array(12);
    const put = (i, p) => { vc(p[0], p[1], p[2], A3); Q[i * 3] = A3[0]; Q[i * 3 + 1] = A3[1]; Q[i * 3 + 2] = A3[2]; };
    const path = (s) => {
      ov.beginPath();
      for (let k = 0; k < s.length; k += 4) { ov.moveTo(s[k], s[k + 1]); ov.lineTo(s[k + 2], s[k + 3]); }
    };
    const labels = [];
    // 세계 점 [[e, n, z], …] (볼록) → 화면 다각형
    const poly3 = (ps) => { const q = []; for (const p of ps) { vc(p[0], p[1], p[2], A3); q.push(A3[0], A3[1], A3[2]); } return projPoly(q); };
    const tracePg = (pg) => { ov.moveTo(pg[0], pg[1]); for (let k = 2; k < pg.length; k += 2) ov.lineTo(pg[k], pg[k + 1]); ov.closePath(); };
    // 부분 개방: 열린 곳 (지표 ±wv) 위에는 노면 띠를 그리지 않는다 (큰 네모 − 구멍 = 자르기, 감는 방향이 반대)
    const OPN = S.mode === 'open' ? opRes() : null, holeP = [];
    if (OPN) {
      const U = OPN.T, top = (j, sg) => [U.E[j] + U.LX[j] * U.WV[j] * sg, U.N[j] + U.LY[j] * U.WV[j] * sg, U.G[j]];
      for (let j = 0; j + 1 < U.m; j++) { const pg = poly3([top(j, 1), top(j + 1, 1), top(j + 1, -1), top(j, -1)]); if (pg) holeP.push(pg); }
    }
    const clipHole = () => {
      if (!holeP.length) return false;
      ov.save();
      ov.beginPath();
      ov.moveTo(-mg, -mg); ov.lineTo(-mg, h + mg); ov.lineTo(w + mg, h + mg); ov.lineTo(w + mg, -mg); ov.closePath();
      holeP.forEach(tracePg);
      ov.clip();
      return true;
    };

    // ---- 지장물 (고치지 않음 · 관로 밑에): 노면 = 땅 위 띠 (폭 max(관경, 0.15 m) · 어두운 점선 테두리) ·
    //      깊이 = 땅 위 옅은 점선 (관은 WebGL) · 이름표 «종류 · D100»
    if (obShow()) {
      const depth = S.depth, obk = new Map(); // 색 · 흐림 단계별
      const okey = (L, a) => {
        const key = L.color + '|' + a;
        let b = obk.get(key);
        if (!b) { b = { color: L.color, dark: L.dark, a: a / 10, poly: [], edge: [], line: [], tube: [] }; obk.set(key, b); }
        return b;
      };
      for (const L of obst) {
        const bx = Math.max(L.e0 - Cx, 0, Cx - L.e1), by = Math.max(L.n0 - Cy, 0, Cy - L.n1);
        if (bx * bx + by * by > FADE1 * FADE1) continue;
        const P = L.pts, G = L.g, bi = L.bi, m = bi.length - 1, hw0 = L.hw, hwMax = Math.max(hw0, 3);
        if (m < 1) continue;
        let best = Infinity, lx = 0, ly = 0;
        const cand = () => { // 이름표 자리: 화면 가운데에서 가장 가까운 점
          const sdx = SEG[2] - SEG[0], sdy = SEG[3] - SEG[1], sl = sdx * sdx + sdy * sdy;
          const st = sl > 0 ? clamp(((cx - SEG[0]) * sdx + (cy - SEG[1]) * sdy) / sl, 0, 1) : 0;
          const qx = SEG[0] + st * sdx, qy = SEG[1] + st * sdy, qd = Math.hypot(qx - cx, qy - cy);
          if (qd < best && qx > 10 && qx < w - 10 && qy > 10 && qy < h - 10) { best = qd; lx = qx; ly = qy; }
        };
        // 마디 왼쪽 법선 · 꼭짓점 이음 방향 (관로 띠와 같은 이등분 · 길이 1/cos)
        const nx = new Float64Array(m), ny = new Float64Array(m), jx = new Float64Array(m + 1), jy = new Float64Array(m + 1);
        for (let j = 0; j < m; j++) {
          const dx = P[bi[j + 1] * 3] - P[bi[j] * 3], dy = P[bi[j + 1] * 3 + 1] - P[bi[j] * 3 + 1], l = Math.hypot(dx, dy);
          nx[j] = -dy / l; ny[j] = dx / l;
        }
        for (let j = 0; j <= m; j++) {
          const a = Math.max(0, j - 1), b = Math.min(m - 1, j);
          let qx = nx[a] + nx[b], qy = ny[a] + ny[b];
          const ql = Math.hypot(qx, qy);
          if (ql < 1e-6) { jx[j] = nx[b]; jy[j] = ny[b]; continue; }
          qx /= ql; qy /= ql;
          const k = 1 / Math.max(qx * nx[b] + qy * ny[b], 0.3);
          jx[j] = qx * k; jy[j] = qy * k;
        }
        let open = null;
        const close = () => { if (open) { addEdge(open.b, open.l, open.r); open = null; } };
        for (let j = 0; j < m; j++) {
          const ia = bi[j], ib = bi[j + 1];
          const e1 = P[ia * 3], n1 = P[ia * 3 + 1], e2 = P[ib * 3], n2 = P[ib * 3 + 1];
          if (distPtSeg(Cx, Cy, e1, n1, e2, n2) > FADE1) { close(); continue; }
          const g1 = G[ia], g2 = G[ib], z1 = P[ia * 3 + 2], z2 = P[ib * 3 + 2];
          const far = Math.max(Math.hypot(e1 - Cx, n1 - Cy), Math.hypot(e2 - Cx, n2 - Cy)) > FADE0;
          const pcs = Math.max(1, Math.ceil(Math.hypot(e2 - e1, n2 - n1) / (far ? 10 : 20)));
          for (let q = 0; q < pcs; q++) {
            const t0 = q / pcs, t1 = (q + 1) / pcs;
            const ea = e1 + (e2 - e1) * t0, na = n1 + (n2 - n1) * t0, ga = g1 + (g2 - g1) * t0;
            const eb = e1 + (e2 - e1) * t1, nb = n1 + (n2 - n1) * t1, gb = g1 + (g2 - g1) * t1;
            let a = 10;
            if (far) {
              const dm = Math.hypot((ea + eb) / 2 - Cx, (na + nb) / 2 - Cy);
              a = Math.round(clamp(1 - (dm - FADE0) / (FADE1 - FADE0), 0, 1) * 10);
              if (a <= 0) { close(); continue; }
            }
            const b = okey(L, a);
            if (depth) {
              vc(ea, na, ga, A3); vc(eb, nb, gb, B3);
              if (projSeg(A3, B3, SEG)) b.line.push(SEG[0], SEG[1], SEG[2], SEG[3]);
              vc(ea, na, z1 + (z2 - z1) * t0, A3); vc(eb, nb, z1 + (z2 - z1) * t1, B3);
              if (projSeg(A3, B3, SEG)) { if (!pprog) b.tube.push(SEG[0], SEG[1], SEG[2], SEG[3]); cand(); }
              continue;
            }
            const oax = t0 === 0 ? jx[j] : nx[j], oay = t0 === 0 ? jy[j] : ny[j];
            const obx = t1 === 1 ? jx[j + 1] : nx[j], oby = t1 === 1 ? jy[j + 1] : ny[j];
            const la = Math.hypot(oax, oay), lb = Math.hypot(obx, oby);
            const ha = clamp(hwMin(ea, na, ga, oax / la, oay / la), hw0, hwMax);
            const hb = clamp(hwMin(eb, nb, gb, obx / lb, oby / lb), hw0, hwMax);
            const AL = [ea + oax * ha, na + oay * ha, ga], AR = [ea - oax * ha, na - oay * ha, ga];
            const BL = [eb + obx * hb, nb + oby * hb, gb], BR = [eb - obx * hb, nb - oby * hb, gb];
            put(0, AL); put(1, BL); put(2, BR); put(3, AR);
            const pg = projPoly(Q);
            if (pg) b.poly.push(pg);
            if (!open) addEdge(b, AL, AR);
            addEdge(b, AL, BL);
            addEdge(b, AR, BR);
            open = { b: b, l: BL, r: BR };
            vc(ea, na, ga, A3); vc(eb, nb, gb, B3);
            if (projSeg(A3, B3, SEG)) cand();
          }
        }
        close();
        if (best < Infinity && L.text && SHOW.lbl) labels.push({ x: lx, y: ly, text: L.text, color: L.color, ob: true });
      }
      ov.lineCap = 'butt'; // 둥근 끝이면 점선 틈이 메워짐
      const obClip = !depth && clipHole();
      for (const b of obk.values()) {
        if (!depth && b.poly.length) {
          ov.beginPath();
          for (const pg of b.poly) {
            ov.moveTo(pg[0], pg[1]);
            for (let k = 2; k < pg.length; k += 2) ov.lineTo(pg[k], pg[k + 1]);
            ov.closePath();
          }
          ov.globalAlpha = 0.55 * b.a; // 관로 (0.85) 보다 옅게
          ov.fillStyle = b.color;
          ov.fill();
          ov.save(); // 띠 안쪽 1.5 px 어두운 점선 (띠 폭 = 관경 그대로)
          ov.clip();
          path(b.edge);
          ov.globalAlpha = b.a;
          ov.setLineDash([5, 4]);
          ov.strokeStyle = b.dark;
          ov.lineWidth = 3;
          ov.stroke();
          ov.restore();
        } else if (depth) {
          ov.setLineDash([3, 5]);
          ov.lineWidth = 1.5;
          ov.globalAlpha = 0.5 * b.a;
          ov.strokeStyle = b.color;
          path(b.line);
          ov.stroke();
          if (b.tube.length) { // WebGL 관을 못 그리면 관 중심선으로
            ov.setLineDash([]);
            ov.lineWidth = 4;
            ov.globalAlpha = 0.6 * b.a;
            path(b.tube);
            ov.stroke();
          }
          ov.setLineDash([]);
        }
      }
      if (obClip) ov.restore();
      ov.globalAlpha = 1;
    }

    // ---- 관로: 노면 = 땅 위 관경 폭 띠 · 깊이 = WebGL 관 (여기서는 땅 위 옅은 점선 + 이름표)
    if (S.pipes && lines.length) {
      const depth = S.depth, pit = trGL(), selSrc = edSel();
      const buckets = new Map(); // 색 · 흐림 단계별 (고른 관은 따로: 밝게 · 흰 테두리)
      const bucket = (L, a) => {
        const sel = L.src === selSrc, key = L.color + '|' + a + (sel ? '|s' : '');
        let bk = buckets.get(key);
        if (!bk) { bk = { color: sel ? L.colorHi : L.color, sel: sel, a: a / 10, poly: [], edge: [], line: [], tube: [] }; buckets.set(key, bk); }
        return bk;
      };
      for (const L of lines) {
        const bx = Math.max(L.e0 - Cx, 0, Cx - L.e1), by = Math.max(L.n0 - Cy, 0, Cy - L.n1);
        if (bx * bx + by * by > FADE1 * FADE1) continue;
        const P = L.pts, bi = L.bi, m = bi.length - 1, hw0 = L.dia / 2, hwMax = Math.max(hw0, 3);
        if (m < 1) continue;
        let best = Infinity, lx = 0, ly = 0, ltp = NaN;
        const cand = (tp) => { // 이름표 자리: 화면 가운데에서 가장 가까운 점 · tp = 그 조각 가운데 토피 (지표 − 관 윗면)
          const sdx = SEG[2] - SEG[0], sdy = SEG[3] - SEG[1], sl = sdx * sdx + sdy * sdy;
          const st = sl > 0 ? clamp(((cx - SEG[0]) * sdx + (cy - SEG[1]) * sdy) / sl, 0, 1) : 0;
          const qx = SEG[0] + st * sdx, qy = SEG[1] + st * sdy, qd = Math.hypot(qx - cx, qy - cy);
          if (qd < best && qx > 10 && qx < w - 10 && qy > 10 && qy < h - 10 && !holeP.some((pg) => inPoly(pg, qx, qy))) { best = qd; lx = qx; ly = qy; ltp = tp; }
        };
        // 마디 왼쪽 법선 · 꼭짓점 이음 방향 (두 법선의 이등분, 길이 1/cos — 띠 폭이 꺾인 곳에서도 같게)
        const nx = new Float64Array(m), ny = new Float64Array(m), jx = new Float64Array(m + 1), jy = new Float64Array(m + 1);
        for (let j = 0; j < m; j++) {
          const dx = P[bi[j + 1] * 3] - P[bi[j] * 3], dy = P[bi[j + 1] * 3 + 1] - P[bi[j] * 3 + 1], l = Math.hypot(dx, dy);
          nx[j] = -dy / l; ny[j] = dx / l;
        }
        for (let j = 0; j <= m; j++) {
          const a = Math.max(0, j - 1), b = Math.min(m - 1, j);
          let qx = nx[a] + nx[b], qy = ny[a] + ny[b];
          const ql = Math.hypot(qx, qy);
          if (ql < 1e-6) { jx[j] = nx[b]; jy[j] = ny[b]; continue; }
          qx /= ql; qy /= ql;
          const k = 1 / Math.max(qx * nx[b] + qy * ny[b], 0.3);
          jx[j] = qx * k; jy[j] = qy * k;
        }
        let open = null; // 이어지는 띠의 끝 (마구리 선을 그을 자리)
        const close = () => { if (open) { addEdge(open.bk, open.l, open.r); open = null; } };
        for (let j = 0; j < m; j++) {
          const ia = bi[j], ib = bi[j + 1];
          const e1 = P[ia * 3], n1 = P[ia * 3 + 1], e2 = P[ib * 3], n2 = P[ib * 3 + 1];
          if (distPtSeg(Cx, Cy, e1, n1, e2, n2) > FADE1) { close(); continue; }
          const g1 = gAt(L, ia), g2 = gAt(L, ib);
          const z1 = zcAt(L, ia), z2 = zcAt(L, ib);
          const far = Math.max(Math.hypot(e1 - Cx, n1 - Cy), Math.hypot(e2 - Cx, n2 - Cy)) > FADE0;
          const pcs = Math.max(1, Math.ceil(Math.hypot(e2 - e1, n2 - n1) / (far ? 10 : 20)));
          for (let q = 0; q < pcs; q++) {
            const t0 = q / pcs, t1 = (q + 1) / pcs;
            const ea = e1 + (e2 - e1) * t0, na = n1 + (n2 - n1) * t0, ga = g1 + (g2 - g1) * t0;
            const eb = e1 + (e2 - e1) * t1, nb = n1 + (n2 - n1) * t1, gb = g1 + (g2 - g1) * t1;
            let a = 10;
            if (far) {
              const dm = Math.hypot((ea + eb) / 2 - Cx, (na + nb) / 2 - Cy);
              a = Math.round(clamp(1 - (dm - FADE0) / (FADE1 - FADE0), 0, 1) * 10);
              if (a <= 0) { close(); continue; }
            }
            const bk = bucket(L, a);
            if (depth) {
              vc(ea, na, ga, A3); vc(eb, nb, gb, B3);
              if (projSeg(A3, B3, SEG)) bk.line.push(SEG[0], SEG[1], SEG[2], SEG[3]);
              vc(ea, na, z1 + (z2 - z1) * t0, A3); vc(eb, nb, z1 + (z2 - z1) * t1, B3);
              if (projSeg(A3, B3, SEG)) { if (!pprog) bk.tube.push(SEG[0], SEG[1], SEG[2], SEG[3]); cand((ga + gb) / 2 - (z1 + (z2 - z1) * (t0 + t1) / 2) - L.dia / 2); }
              continue;
            }
            const oax = t0 === 0 ? jx[j] : nx[j], oay = t0 === 0 ? jy[j] : ny[j];
            const obx = t1 === 1 ? jx[j + 1] : nx[j], oby = t1 === 1 ? jy[j + 1] : ny[j];
            const la = Math.hypot(oax, oay), lb = Math.hypot(obx, oby);
            const ha = clamp(hwMin(ea, na, ga, oax / la, oay / la), hw0, hwMax);
            const hb = clamp(hwMin(eb, nb, gb, obx / lb, oby / lb), hw0, hwMax);
            const AL = [ea + oax * ha, na + oay * ha, ga], AR = [ea - oax * ha, na - oay * ha, ga];
            const BL = [eb + obx * hb, nb + oby * hb, gb], BR = [eb - obx * hb, nb - oby * hb, gb];
            put(0, AL); put(1, BL); put(2, BR); put(3, AR);
            const pg = projPoly(Q);
            if (pg) bk.poly.push(pg);
            if (!open) addEdge(bk, AL, AR); // 시작 마구리
            addEdge(bk, AL, BL);
            addEdge(bk, AR, BR);
            open = { bk: bk, l: BL, r: BR };
            vc(ea, na, ga, A3); vc(eb, nb, gb, B3);
            if (projSeg(A3, B3, SEG)) cand((ga + gb) / 2 - (z1 + (z2 - z1) * (t0 + t1) / 2) - L.dia / 2);
          }
        }
        close();
        if (best < Infinity && L.label && SHOW.lbl) labels.push({ x: lx, y: ly, text: L.label + (Number.isFinite(ltp) ? ' · 토피 ' + ltp.toFixed(1) + ' m' : ''), color: L.color });
      }
      ov.lineCap = 'round';
      ov.lineJoin = 'round';
      const bks = [...buckets.values()].sort((p, q) => p.sel - q.sel); // 고른 관은 맨 위
      if (!depth) {
        const pc = clipHole(); // 부분 개방: 열린 곳 위는 비움
        for (const bk of bks) {
          if (!bk.poly.length) continue;
          ov.beginPath();
          for (const pg of bk.poly) {
            ov.moveTo(pg[0], pg[1]);
            for (let k = 2; k < pg.length; k += 2) ov.lineTo(pg[k], pg[k + 1]);
            ov.closePath();
          }
          ov.globalAlpha = (bk.sel ? 0.95 : 0.85) * bk.a;
          ov.fillStyle = bk.color;
          ov.fill();
          // 1 px 어두운 테두리 (고른 관은 흰색): 띠 모양으로 자른 2 px 선 → 띠 안쪽 1 px 만 남는다 (띠 폭 = 관경 그대로)
          ov.save();
          ov.clip();
          path(bk.edge);
          ov.globalAlpha = bk.a;
          ov.strokeStyle = bk.sel ? '#ffffff' : 'rgba(0, 0, 0, 0.45)';
          ov.lineWidth = 2;
          ov.stroke();
          ov.restore();
        }
        if (pc) ov.restore();
      } else {
        ov.setLineDash([6, 6]);
        ov.lineWidth = 1.5;
        if (!pit) for (const bk of bks) { if (bk.sel) continue; ov.globalAlpha = 0.6 * bk.a; ov.strokeStyle = bk.color; path(bk.line); ov.stroke(); }
        ov.setLineDash([]);
        for (const bk of bks) { // 고른 관: 땅 위 흰 선
          if (!bk.sel) continue;
          path(bk.line);
          ov.globalAlpha = bk.a;
          ov.strokeStyle = 'rgba(8, 10, 14, 0.8)'; ov.lineWidth = 4; ov.stroke();
          ov.strokeStyle = '#ffffff'; ov.lineWidth = 2; ov.stroke();
        }
        if (!pprog) { // WebGL 관을 못 그리면 관 중심선으로
          ov.strokeStyle = 'rgba(8, 10, 14, 0.9)';
          ov.lineWidth = 7;
          for (const bk of buckets.values()) { ov.globalAlpha = bk.a; path(bk.tube); ov.stroke(); }
          ov.lineWidth = 4;
          for (const bk of buckets.values()) { ov.globalAlpha = bk.a; ov.strokeStyle = bk.color; path(bk.tube); ov.stroke(); }
        }
      }
      ov.globalAlpha = 1;
    }

    // ---- 터파기 구멍 · 부분 개방: 안쪽 가장자리 그늘 + 밝은 자른 선 (WebGL 구멍과 같은 점 · 같은 투영)
    const rims = []; // [단면 묶음, 처음 끝벽, 끝 끝벽]
    if (S.pipes && lines.length && trGL()) for (const T of trSecGet()) rims.push([T, T.c.wall[0], T.c.wall[1]]);
    if (OPN) rims.push([OPN.T, true, true]);
    if (rims.length) {
      ov.lineCap = 'round';
      ov.lineJoin = 'round';
      {
        const tg = new Map(); // 흐림 단계 → { poly, edge }
        const tgb = (a) => { let g = tg.get(a); if (!g) { g = { a: a / 10, poly: [], edge: [] }; tg.set(a, g); } return g; };
        const rim = (T, j0, j1, t, w0, w1) => { // 꼭짓점 j0 → j1 의 t 자리 지표 가장자리 (w + 왼쪽 · − 오른쪽)
          const ax = T.E[j0] + T.LX[j0] * w0, ay = T.N[j0] + T.LY[j0] * w0, bx = T.E[j1] + T.LX[j1] * w1, by = T.N[j1] + T.LY[j1] * w1;
          return [ax + (bx - ax) * t, ay + (by - ay) * t, T.G[j0] + (T.G[j1] - T.G[j0]) * t];
        };
        for (const [T, w0, w1] of rims) {
          const bx = Math.max(T.e0 - Cx, 0, Cx - T.e1), by = Math.max(T.n0 - Cy, 0, Cy - T.n1);
          if (bx * bx + by * by > FADE1 * FADE1) continue;
          for (let j = 0; j + 1 < T.m; j++) {
            const e1 = T.E[j], n1 = T.N[j], e2 = T.E[j + 1], n2 = T.N[j + 1];
            if (distPtSeg(Cx, Cy, e1, n1, e2, n2) > FADE1) continue;
            const far = Math.max(Math.hypot(e1 - Cx, n1 - Cy), Math.hypot(e2 - Cx, n2 - Cy)) > FADE0;
            const pcs = Math.max(1, Math.ceil(Math.hypot(e2 - e1, n2 - n1) / (far ? 10 : 20)));
            const wl = T.WV[j], wr = T.WV[j + 1];
            for (let q = 0; q < pcs; q++) {
              const t0 = q / pcs, t1 = (q + 1) / pcs;
              let a = 10;
              if (far) {
                const dm = Math.hypot(e1 + (e2 - e1) * (t0 + t1) / 2 - Cx, n1 + (n2 - n1) * (t0 + t1) / 2 - Cy);
                a = Math.round(clamp(1 - (dm - FADE0) / (FADE1 - FADE0), 0, 1) * 10);
                if (a <= 0) continue;
              }
              const g = tgb(a);
              const AL = rim(T, j, j + 1, t0, wl, wr), BL = rim(T, j, j + 1, t1, wl, wr);
              const AR = rim(T, j, j + 1, t0, -wl, -wr), BR = rim(T, j, j + 1, t1, -wl, -wr);
              put(0, AL); put(1, BL); put(2, BR); put(3, AR);
              const pg = projPoly(Q);
              if (pg) g.poly.push(pg);
              addEdge(g, AL, BL);
              addEdge(g, AR, BR);
              if (j === 0 && q === 0 && w0) addEdge(g, AL, AR);
              if (j === T.m - 2 && q === pcs - 1 && w1) addEdge(g, BL, BR);
            }
          }
        }
        for (const g of tg.values()) {
          if (g.poly.length) { // 구멍 안쪽으로만 번지는 그늘 (가장자리 굵은 선을 구멍 모양으로 자름)
            ov.save();
            ov.beginPath();
            for (const pg of g.poly) {
              ov.moveTo(pg[0], pg[1]);
              for (let k = 2; k < pg.length; k += 2) ov.lineTo(pg[k], pg[k + 1]);
              ov.closePath();
            }
            ov.clip();
            path(g.edge);
            ov.globalAlpha = g.a;
            ov.strokeStyle = 'rgba(0, 0, 0, 0.18)'; ov.lineWidth = 14; ov.stroke();
            ov.strokeStyle = 'rgba(0, 0, 0, 0.2)'; ov.lineWidth = 6; ov.stroke();
            ov.restore();
          }
          path(g.edge);
          ov.globalAlpha = g.a;
          ov.strokeStyle = 'rgba(0, 0, 0, 0.5)'; ov.lineWidth = 3.2; ov.stroke();
          ov.strokeStyle = '#fff4dc'; ov.lineWidth = 1.6; ov.stroke();
        }
      }
      ov.globalAlpha = 1;
    }

    // ---- 단면: 누른 자리에 관 길을 가로지르는 세운 면 (땅 속 · 반투명) + 층 · 관 · 터파기 윤곽 + 지표 위 노란 선
    const SCV = S.mode === 'sec' ? scGet() : null;
    if (SCV) {
      const C = SCV, X = C.X, W3 = (q) => [C.e + C.nx * q[0], C.n + C.ny * q[0], q[1]];
      const half = X.wv + 0.6, zlo = X.zb - 0.3;
      const fillPg = (pg, style, a) => { ov.beginPath(); tracePg(pg); ov.globalAlpha = a; ov.fillStyle = style; ov.fill(); };
      const pl = poly3([[half, X.g], [-half, X.g], [-half, zlo], [half, zlo]].map(W3));
      if (pl) {
        fillPg(pl, '#ffffff', 0.12);
        ov.globalAlpha = 0.55; ov.strokeStyle = '#ffffff'; ov.lineWidth = 1; ov.stroke();
      }
      for (const [k, pg] of layerPolys(X, X.zb)) { const sp = poly3(pg.map(W3)); if (sp) fillPg(sp, LAY_CSS[k], 0.8); }
      const ring = [];
      for (let i = 0; i < 32; i++) { const a = i / 32 * 2 * Math.PI; ring.push(W3([X.D / 2 * Math.cos(a), X.zc + X.D / 2 * Math.sin(a)])); }
      const rp = poly3(ring);
      if (rp) { fillPg(rp, C.color, 0.95); ov.globalAlpha = 1; ov.strokeStyle = '#ffffff'; ov.lineWidth = 1.2; ov.stroke(); }
      const wAt = (z) => (z >= X.zt ? X.wv : X.wb + TR.n * Math.max(0, z - X.zb)), zk = Math.max(X.zt, X.zb);
      const ol = poly3([[-X.wb, X.zb], [X.wb, X.zb], [wAt(zk), zk], [X.wv, X.g], [-X.wv, X.g], [-wAt(zk), zk]].map(W3));
      if (ol) { ov.beginPath(); tracePg(ol); ov.globalAlpha = 1; ov.strokeStyle = '#fff4dc'; ov.lineWidth = 1.4; ov.stroke(); }
      vc(...W3([half, X.g]), A3); vc(...W3([-half, X.g]), B3);
      if (projSeg(A3, B3, SEG)) { // 지표 위 자리 (사진 땅과 맞닿는 선)
        ov.beginPath(); ov.moveTo(SEG[0], SEG[1]); ov.lineTo(SEG[2], SEG[3]);
        ov.lineCap = 'round';
        ov.strokeStyle = 'rgba(8, 10, 14, 0.85)'; ov.lineWidth = 5; ov.stroke();
        ov.strokeStyle = '#ffd400'; ov.lineWidth = 2.6; ov.stroke();
      }
      ov.globalAlpha = 1;
    }

    // ---- 이웃 사진: 땅에 누운 원판 (반지름 MARK_R m, 멀어도 화면 1.8 px 아래로는 줄이지 않음 · 마우스 올린 것만 크게)
    const marks = [];
    for (const s of shots) {
      if (s.i === S.cur) continue;
      const c = calibFor(s.video);
      const ge = s.e + c.dx, gn = s.n + c.dy;
      if (Math.hypot(ge - Cx, gn - Cy) > NEIGH_R) continue;
      vc(ge, gn, s.gz, A3);
      if (A3[2] < 1) continue;
      const hov = s.i === S.hover;
      const rad = hov ? Math.max(MARK_R * MARK_HOVER, 6 * A3[2] / kx) : Math.max(MARK_R, 1.8 * A3[2] / kx);
      const sx = cx + A3[0] / A3[2] * kx, sy = cy - A3[1] / A3[2] * ky;
      const poly = [];
      let ok = true, x0 = Infinity, x1 = -Infinity, y0 = Infinity, y1 = -Infinity;
      for (let k = 0; k < 32; k++) {
        const t = k / 32 * 2 * Math.PI;
        vc(ge + rad * Math.cos(t), gn + rad * Math.sin(t), s.gz, B3);
        if (B3[2] < NEAR) { ok = false; break; }
        const px = cx + B3[0] / B3[2] * kx, py = cy - B3[1] / B3[2] * ky;
        poly.push(px, py);
        x0 = Math.min(x0, px); x1 = Math.max(x1, px); y0 = Math.min(y0, py); y1 = Math.max(y1, py);
      }
      if (!ok || x1 < 0 || x0 > w || y1 < 0 || y0 > h) continue;
      // 원판 틀 (동 · 북 반지름 → 화면): 그라데이션을 타원에 맞추는 데 쓴다
      vc(ge + rad, gn, s.gz, B3);
      const ex = [cx + B3[0] / B3[2] * kx - sx, cy - B3[1] / B3[2] * ky - sy];
      vc(ge, gn + rad, s.gz, B3);
      const ny_ = [cx + B3[0] / B3[2] * kx - sx, cy - B3[1] / B3[2] * ky - sy];
      marks.push({ i: s.i, d: A3[2], poly, sx, sy, ex, ny: ny_, hov, rx: (x1 - x0) / 2, ry: (y1 - y0) / 2 });
    }
    marks.sort((a, b) => (a.hov - b.hov) || (b.d - a.d));   // 먼 것부터 · 마우스 올린 것은 맨 위
    const dpr = V.dpr;
    for (const m of marks) {
      const p = m.poly;
      const trace = () => {
        ov.beginPath();
        ov.moveTo(p[0], p[1]);
        for (let k = 2; k < p.length; k += 2) ov.lineTo(p[k], p[k + 1]);
        ov.closePath();
      };
      const mn = Math.min(m.rx, m.ry);
      // 그림자
      ov.save();
      trace();
      ov.shadowColor = 'rgba(0, 0, 0, 0.5)';
      ov.shadowBlur = clamp(mn * 0.8, 1, 10) * dpr;
      ov.shadowOffsetY = clamp(mn * 0.3, 0.5, 4) * dpr;
      ov.fillStyle = '#c3c9d3';
      ov.fill();
      ov.restore();
      // 몸통: 왼쪽 위가 밝은 둥근 그라데이션 · 가장자리는 조금 어둡게
      trace();
      const det = m.ex[0] * m.ny[1] - m.ex[1] * m.ny[0];
      if (Math.abs(det) > 1e-6) {
        const hx = -0.35 * m.rx, hy = -0.45 * m.ry;
        let lx = (m.ny[1] * hx - m.ny[0] * hy) / det, ly = (-m.ex[1] * hx + m.ex[0] * hy) / det;
        const ll = Math.hypot(lx, ly);
        if (ll > 0.6) { lx *= 0.6 / ll; ly *= 0.6 / ll; }
        const gr = ov.createRadialGradient(lx, ly, 0, 0, 0, 1);
        gr.addColorStop(0, '#ffffff');
        gr.addColorStop(0.5, '#eceff4');
        gr.addColorStop(0.85, '#c6ccd6');
        gr.addColorStop(1, '#9aa3b2');
        ov.setTransform(dpr * m.ex[0], dpr * m.ex[1], dpr * m.ny[0], dpr * m.ny[1], dpr * m.sx, dpr * m.sy);
        ov.fillStyle = gr;
        ov.fill();
        ov.setTransform(dpr, 0, 0, dpr, 0, 0);
      } else {
        ov.fillStyle = '#eceff4';
        ov.fill();
      }
      if (m.hov) {
        ov.strokeStyle = 'rgba(10, 12, 16, 0.85)';
        ov.lineWidth = 3.6;
        ov.stroke();
        ov.strokeStyle = '#22d3ee';
        ov.lineWidth = 2.2;
        ov.stroke();
      } else {
        ov.strokeStyle = 'rgba(10, 12, 16, 0.85)';
        ov.lineWidth = 1;
        ov.stroke();
      }
    }
    S.marks = marks;

    // ---- 관로 · 지장물 이름표 (지장물은 속 빈 점)
    if (labels.length) {
      ov.font = '600 12px Malgun Gothic, Segoe UI, sans-serif';
      ov.textAlign = 'left';
      ov.textBaseline = 'middle';
      for (const lb of labels) {
        const tw = ov.measureText(lb.text).width, pw = tw + 22, ph = 22;
        const bx = clamp(lb.x - pw / 2, 4, w - pw - 4), by = clamp(lb.ob ? lb.y + 12 : lb.y - ph - 12, 4, h - ph - 4); // 지장물은 선 아래 (관로 이름표와 덜 겹치게)
        roundRect(ov, bx, by, pw, ph, 11);
        ov.fillStyle = 'rgba(12, 14, 20, 0.85)';
        ov.fill();
        ov.beginPath();
        if (lb.ob) {
          ov.arc(bx + 11, by + ph / 2, 3.2, 0, 2 * Math.PI);
          ov.strokeStyle = lb.color;
          ov.lineWidth = 2;
          ov.stroke();
        } else {
          ov.arc(bx + 11, by + ph / 2, 4, 0, 2 * Math.PI);
          ov.fillStyle = lb.color;
          ov.fill();
        }
        ov.fillStyle = '#fff';
        ov.fillText(lb.text, bx + 19, by + ph / 2 + 0.5);
      }
    }

    // ---- 지장물 간섭 자리: 땅 위 빨간 고리 (반지름 0.6 m · 화면 9 px 아래로는 줄이지 않음 · 이름표 위)
    if (S.pipes && obShow()) {
      for (const sp of CF.list) {
        if (Math.hypot(sp.e - Cx, sp.n - Cy) > FADE1) continue;
        vc(sp.e, sp.n, sp.g, A3);
        if (A3[2] < 1) continue;
        const rad = Math.max(0.6, 9 * A3[2] / kx), poly = [];
        for (let k = 0; k < 32; k++) {
          const t = k / 32 * 2 * Math.PI;
          vc(sp.e + rad * Math.cos(t), sp.n + rad * Math.sin(t), sp.g, B3);
          if (B3[2] < NEAR) break;
          poly.push(cx + B3[0] / B3[2] * kx, cy - B3[1] / B3[2] * ky);
        }
        if (poly.length < 64) continue;
        ov.beginPath();
        ov.moveTo(poly[0], poly[1]);
        for (let k = 2; k < poly.length; k += 2) ov.lineTo(poly[k], poly[k + 1]);
        ov.closePath();
        ov.strokeStyle = 'rgba(8, 10, 14, 0.85)'; ov.lineWidth = 5; ov.stroke();
        ov.strokeStyle = CF_RED; ov.lineWidth = 2.6; ov.stroke();
      }
    }

    // ---- 관로 고치기 손잡이: 고른 관의 원래 꼭짓점마다 땅 위 청록 원판 (고른 것 노랑 · 마우스 올린 것 크게)
    S.hnd = [];
    const EL = edLine();
    if (EL) {
      const P = EL.pts;
      EL.ctrl.forEach((k, i) => {
        const e = P[k * 3], n = P[k * 3 + 1], g = gAt(EL, k);
        vc(e, n, g, A3);
        if (A3[2] < 0.3) return;
        const hov = i === ED.hov, rad = Math.max(MARK_R * (hov ? 1.7 : 1), (hov ? 8 : 5) * A3[2] / kx); // 올리면 조금만 (끄는 자리를 가리지 않게)
        const sx = cx + A3[0] / A3[2] * kx, sy = cy - A3[1] / A3[2] * ky, poly = [];
        for (let q = 0; q < 24; q++) {
          const t = q / 24 * 2 * Math.PI;
          vc(e + rad * Math.cos(t), n + rad * Math.sin(t), g, B3);
          if (B3[2] < NEAR) return;
          poly.push(cx + B3[0] / B3[2] * kx, cy - B3[1] / B3[2] * ky);
        }
        if (sx < -20 || sy < -20 || sx > w + 20 || sy > h + 20) return;
        S.hnd.push({ i: i, sx: sx, sy: sy, poly: poly });
      });
      for (const hd of S.hnd) {
        const p = hd.poly;
        ov.beginPath();
        ov.moveTo(p[0], p[1]);
        for (let q = 2; q < p.length; q += 2) ov.lineTo(p[q], p[q + 1]);
        ov.closePath();
        ov.fillStyle = hd.i === ED.hi ? PIN : PIN2;
        ov.fill();
        ov.strokeStyle = PIN_DARK;
        ov.lineWidth = hd.i === ED.hov ? 2.5 : 1.5;
        ov.stroke();
      }
    }

    // ---- 점 찍어 맞추기 핀 (맨 위)
    drawPins(w, h, kx, ky);
  }
  // 핀: 찍은 점 = 노란 고리 + 번호 풍선 · 다른 쪽에서 온 같은 점 = 청록 고리 (맞으면 둘이 겹친다)
  const PIN = '#facc15', PIN2 = '#22d3ee', PIN_DARK = 'rgba(8, 10, 14, 0.9)';
  function pinMain(c, x, y, no) {
    c.beginPath();
    c.arc(x, y, 5, 0, 2 * Math.PI);
    c.lineWidth = 3.4; c.strokeStyle = PIN_DARK; c.stroke();
    c.lineWidth = 1.8; c.strokeStyle = PIN; c.stroke();
    c.beginPath();
    c.arc(x, y, 1.4, 0, 2 * Math.PI);
    c.fillStyle = PIN; c.fill();
    const bx = x + 12, by = y - 16;
    c.beginPath();
    c.moveTo(x + 3.6, y - 3.6); c.lineTo(bx - 5, by + 5);
    c.lineWidth = 3; c.strokeStyle = PIN_DARK; c.stroke();
    c.lineWidth = 1.4; c.strokeStyle = PIN; c.stroke();
    c.beginPath();
    c.arc(bx, by, 9, 0, 2 * Math.PI);
    c.fillStyle = PIN; c.fill();
    c.lineWidth = 1.5; c.strokeStyle = PIN_DARK; c.stroke();
    c.font = '700 11px Malgun Gothic, Segoe UI, sans-serif';
    c.textAlign = 'center'; c.textBaseline = 'middle';
    c.fillStyle = '#11141a';
    c.fillText(String(no), bx, by + 0.5);
  }
  function pinOther(c, x, y) {
    c.beginPath();
    c.arc(x, y, 8, 0, 2 * Math.PI);
    c.lineWidth = 3.6; c.strokeStyle = PIN_DARK; c.stroke();
    c.lineWidth = 2; c.strokeStyle = PIN2; c.stroke();
  }
  function pinLink(c, a, b) {
    if (Math.hypot(a[0] - b[0], a[1] - b[1]) < 6) return;
    c.beginPath();
    c.moveTo(a[0], a[1]); c.lineTo(b[0], b[1]);
    c.setLineDash([4, 4]);
    c.lineWidth = 1.5; c.strokeStyle = 'rgba(255, 255, 255, 0.85)'; c.stroke();
    c.setLineDash([]);
  }
  // 360°: 사진에서 찍은 점 (이 사진이면 그 방향, 다른 사진이면 그 사진에서 땅에 닿는 곳) · 지도 점 (땅 위 자리)
  function drawPins(w, h, kx, ky) {
    const cur = shots[S.cur], vid = cur.video;
    const items = pairRes(vid).map((o, i) => ({ o: o, no: i + 1 }));
    const pd = AL.pend && AL.pend.shot === cur.id ? AL.pend : null;
    if (!items.length && !pd) return;
    const c = calibFor(vid), R = rot64(c), cx = w / 2, cy = h / 2, f = V.f, r = V.r, u = V.u;
    const proj = (dx, dy, dz) => {
      const z = dx * f[0] + dy * f[1] + dz * f[2];
      if (z < NEAR) return null;
      const x = cx + (dx * r[0] + dy * r[1]) / z * kx, y = cy - (dx * u[0] + dy * u[1] + dz * u[2]) / z * ky;
      return x > -50 && x < w + 50 && y > -50 && y < h + 50 ? [x, y] : null;
    };
    const dirPos = (p) => proj(R[0] * p[0] + R[1] * p[1] + R[2] * p[2], R[3] * p[0] + R[4] * p[1] + R[5] * p[2], R[6] * p[0] + R[7] * p[1] + R[8] * p[2]);
    const [Cx, Cy, Cz] = S.cam;
    ov.save();
    for (const it of items) {
      const o = it.o;
      const a = o.q.shot === cur.id ? dirPos(o.q.p) : o.h ? proj(o.h[0] - Cx, o.h[1] - Cy, o.zg - Cz) : null;
      const b = Number.isFinite(o.zg) ? proj(o.q.e - Cx, o.q.n - Cy, o.zg - Cz) : null;
      if (a && b) pinLink(ov, a, b);
      if (b) pinOther(ov, b[0], b[1]);
      if (a) pinMain(ov, a[0], a[1], it.no);
    }
    if (pd) { const a = dirPos(pd.p); if (a) pinMain(ov, a[0], a[1], items.length + 1); }
    ov.restore();
  }
  function markAt(x, y) {
    for (let j = S.marks.length - 1; j >= 0; j--) {
      const m = S.marks[j], p = m.poly;
      let inside = false;
      for (let a = 0, b = p.length - 2; a < p.length; b = a, a += 2) {
        if ((p[a + 1] > y) !== (p[b + 1] > y) && x < (p[b] - p[a]) * (y - p[a + 1]) / (p[b + 1] - p[a + 1]) + p[a]) inside = !inside;
      }
      if (inside || Math.hypot(x - m.sx, y - m.sy) < 10) return m.i;
    }
    return -1;
  }

  // ---------------------------------------------------------------- 끌기 · 집기 (마우스 · 손가락)
  function gestures(el, h) {
    const pts = new Map();
    let g = null;
    const pos = (e) => { const r = el.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; };
    el.addEventListener('pointerdown', (e) => {
      if (e.pointerType === 'mouse' && e.button !== 0) return;
      try { el.setPointerCapture(e.pointerId); } catch (err) { /* 무시 */ }
      const p = pos(e);
      pts.set(e.pointerId, p);
      if (pts.size === 1) {
        g = { mode: 'drag', x0: p.x, y0: p.y, lx: p.x, ly: p.y, moved: false };
        if (h.start) h.start(p.x, p.y, e);
      } else if (pts.size === 2) {
        const [a, b] = [...pts.values()];
        g = { mode: 'pinch', d0: Math.hypot(a.x - b.x, a.y - b.y) || 1 };
        if (h.pinchStart) h.pinchStart((a.x + b.x) / 2, (a.y + b.y) / 2);
      }
    });
    el.addEventListener('pointermove', (e) => {
      const p = pos(e);
      if (!pts.has(e.pointerId)) { if (h.hover && e.pointerType === 'mouse') h.hover(p.x, p.y); return; }
      pts.set(e.pointerId, p);
      if (!g) return;
      if (g.mode === 'drag') {
        const dx = p.x - g.x0, dy = p.y - g.y0;
        if (!g.moved && Math.hypot(dx, dy) > 4) { g.moved = true; el.classList.add('grabbing'); }
        if (g.moved && h.drag) h.drag(dx, dy, p.x - g.lx, p.y - g.ly);
        g.lx = p.x; g.ly = p.y;
      } else if (g.mode === 'pinch' && pts.size >= 2 && h.pinch) {
        const [a, b] = [...pts.values()];
        h.pinch((Math.hypot(a.x - b.x, a.y - b.y) || 1) / g.d0, (a.x + b.x) / 2, (a.y + b.y) / 2);
      }
    });
    const up = (e) => {
      if (!pts.has(e.pointerId)) return;
      const p = pos(e);
      pts.delete(e.pointerId);
      if (g && g.mode === 'drag' && !g.moved && e.type === 'pointerup' && h.tap) h.tap(p.x, p.y);
      if (g && g.mode === 'drag' && h.end) h.end(g.moved);
      if (pts.size === 0 || (g && g.mode === 'pinch')) { g = null; el.classList.remove('grabbing'); }
    };
    el.addEventListener('pointerup', up);
    el.addEventListener('pointercancel', up);
  }

  // 360° 보기 조작
  let vStart = null;
  gestures(ovc, {
    start: (x, y, e) => { vStart = { yaw: S.yaw, pitch: S.pitch, fov: S.fov }; edStart('v', x, y, e); },
    end: (moved) => edEnd(moved),
    drag: (dx, dy) => {
      if (ED.down) { edMove(dx, dy); return; } // 관로 고치기: 손잡이 · 관 끌기
      if (!vStart) return;
      if (PC.src >= 0) cardHide(); // 관 정보 칸은 누른 화면 자리에 붙어 있음 → 돌리면 닫음
      updView(); // 휠 직후라도 지금 시야각으로 잰다
      const k = R2D * V.tanH / (V.w / 2); // 화면 가운데에서 1 px 당 각도
      S.yaw = ((vStart.yaw - dx * k) % 360 + 540) % 360 - 180;
      S.pitch = clamp(vStart.pitch + dy * k, -89, 89);
      dirty();
    },
    pinchStart: () => { edEnd(true); vStart = { yaw: S.yaw, pitch: S.pitch, fov: S.fov }; },
    pinch: (sc) => { if (vStart) { S.fov = clamp(vStart.fov / sc, FOV_MIN, FOV_MAX); dirty(); } },
    tap: (x, y) => {
      flushView();
      if (ED.on) { edTap('v', x, y); return; } // 관로 고치기 동안은 원판 눌러 옮겨 가지 않음
      if (AL.on) { alPhoto(x, y); return; }
      const i = markAt(x, y);
      if (i >= 0) { openShot(i); return; }
      if (!pipeTap('v', x, y)) cardHide(); // 관 누름: 매설 · 터파기 = 관 정보 · 부분 개방 = 엶 · 단면 = 단면
    },
    hover: (x, y) => {
      flushView();
      if (ED.on) { edHover('v', x, y); return; }
      const i = AL.on ? -1 : markAt(x, y); // 점 찍는 동안은 원판을 키우지 않음 (십자 표시)
      ovc.classList.toggle('point', i >= 0 || (!AL.on && pickMode() && lineAt('v', x, y) >= 0));
      if (i !== S.hover) { S.hover = i; needView = true; kick(); }
    },
  });
  ovc.addEventListener('pointerleave', () => {
    if (ED.on) { edHover('v', -1e9, -1e9); return; }
    if (S.hover >= 0) { S.hover = -1; ovc.classList.remove('point'); needView = true; kick(); }
  });
  ovc.addEventListener('dblclick', (e) => {
    if (!ED.on) return;
    const r = ovc.getBoundingClientRect();
    flushView();
    edInsert('v', e.clientX - r.left, e.clientY - r.top);
  });
  ovc.addEventListener('wheel', (e) => {
    e.preventDefault();
    const d = e.deltaMode === 1 ? e.deltaY * 33 : e.deltaY;
    S.fov = clamp(S.fov * Math.exp(d * 0.0012), FOV_MIN, FOV_MAX);
    dirty();
  }, { passive: false });
  $('prev').addEventListener('click', () => stepShot(-1));
  $('next').addEventListener('click', () => stepShot(1));
  // 화살표 = 보는 방향 기준 옆 사진 · PageUp / PageDown = 찍은 순서 앞 / 뒤
  const ARROW = { ArrowUp: 0, ArrowRight: 90, ArrowDown: 180, ArrowLeft: -90 };
  document.addEventListener('keydown', (e) => {
    const t = e.target;
    if (dlgOpen()) { if (e.key === 'Escape') { e.preventDefault(); dlgClose(); } return; }
    if (e.key === 'Escape' && !secDlg.hidden) { e.preventDefault(); secShow(false); return; }
    if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT' || t.isContentEditable)) return;
    if (ED.on && edKey(e)) { e.preventDefault(); return; } // 관로 고치기: Esc · Delete · Ctrl+Z / Y (화살표는 그대로 이동)
    const nav = !(e.altKey || e.ctrlKey || e.metaKey), dir = Object.prototype.hasOwnProperty.call(ARROW, e.key) ? ARROW[e.key] : null;
    if (nav && dir !== null) { e.preventDefault(); moveDir(dir); }
    else if (nav && e.key === 'PageUp') { e.preventDefault(); stepShot(-1); }
    else if (nav && e.key === 'PageDown') { e.preventDefault(); stepShot(1); }
    else if (e.key === 'Escape' && AL.on) { // 반쯤 만든 짝 취소 · 없으면 끝
      e.preventDefault();
      if (AL.pend) { AL.pend = null; alUI(); dirty(); } else alMode(false);
    } else if (e.key === 'Escape' && ugEsc()) e.preventDefault(); // 단면 · 부분 개방 · 관 정보 닫기
  });

  // ---------------------------------------------------------------- 윗줄
  function fmtTime(t) {
    if (!Number.isFinite(t)) return '-';
    const m = Math.floor(t / 60), s = t - m * 60;
    return m + ':' + (s < 10 ? '0' : '') + s.toFixed(1);
  }
  function updInfo() {
    const s = shots[S.cur];
    $('shotNo').textContent = s.seq + ' / ' + shots.length;
    const vid = $('shotVid');
    vid.textContent = s.video || '-';
    vid.title = videoName[s.video] || '';
    $('shotTime').textContent = fmtTime(s.time);
    $('prev').disabled = S.cur <= 0;
    $('next').disabled = S.cur >= shots.length - 1;
  }
  function setOn(el, on) { el.classList.toggle('on', !!on); }
  $('title').textContent = String(PRJ.title || '드론 로드뷰');
  document.title = (PRJ.title ? String(PRJ.title) + ' · ' : '') + '드론 로드뷰';
  function setPipes(on) {
    S.pipes = !!on;
    if (!S.pipes && ED.on) edMode(false); // 관로를 끄면 고치기도 끝
    setOn($('tgPipes'), S.pipes);
    $('ugMode').classList.toggle('off', !S.pipes);
    if (!S.pipes) cardHide();
    dirty();
  }
  $('tgPipes').addEventListener('click', () => setPipes(!S.pipes));

  // ---------------------------------------------------------------- 지하 시설물 칸: 표현 (하나만) · 표시 항목 (따로) · 평균매설깊이
  const MODE_BTN = { bury: 'mSurf', trench: 'mDepth', open: 'mOpen', sec: 'mSec' };
  function setMode(m) {
    if (MODES.indexOf(m) < 0) m = 'bury';
    S.mode = m;
    S.depth = m === 'trench';
    if (m !== 'open') { OP = null; OPR = null; }
    if (m !== 'sec') { SC = null; SCR = null; }
    cardHide();
    modeUI();
    dirty();
  }
  function modeUI() {
    Object.keys(MODE_BTN).forEach((k) => setOn($(MODE_BTN[k]), S.mode === k));
    $('app').dataset.mode = S.mode;
    scUI();
    opUI();
  }
  Object.keys(MODE_BTN).forEach((k) => $(MODE_BTN[k]).addEventListener('click', () => setMode(k)));
  const pickMode = () => S.pipes && (S.mode === 'open' || S.mode === 'sec'); // 관을 눌러 쓰는 표현 (손가락 표시)
  function showSet(k, on) {
    SHOW[k] = !!on;
    lsSet(SHOW_KEY, SHOW);
    ugUI();
    dirty();
  }
  function ugUI() {
    setOn($('tgPit'), trOn());
    setOn($('tgBed'), SHOW.bed);
    setOn($('tgFill'), SHOW.fill);
    setOn($('tgLbl'), SHOW.lbl);
    const d = trDefault();
    ['ugTag', 'secTag'].forEach((id) => { const t = $(id); t.textContent = d ? '가시화 기본값' : '직접 입력'; t.classList.toggle('user', !d); });
  }
  $('tgPit').addEventListener('click', () => trSet({ on: !trOn() }));
  $('tgBed').addEventListener('click', () => showSet('bed', !SHOW.bed));
  $('tgFill').addEventListener('click', () => showSet('fill', !SHOW.fill));
  $('tgLbl').addEventListener('click', () => showSet('lbl', !SHOW.lbl));
  const coverEl = $('cover');
  coverEl.value = String(round(S.cover, 2));
  coverEl.addEventListener('input', () => { if (isNum(coverEl.value)) setCover(Number(coverEl.value), coverEl); });
  coverEl.addEventListener('change', () => { coverEl.value = String(round(S.cover, 2)); });
  // 평균매설깊이: 지하 시설물 칸과 터파기 설정 창 칸이 같은 값 (from = 지금 치고 있는 칸은 건드리지 않음)
  function setCover(v, from) {
    S.cover = clamp(v, 0, 20);
    if (from !== coverEl) coverEl.value = String(round(S.cover, 2));
    secUI();
    dirty();
  }
  $('tgPath').addEventListener('click', () => { S.path = !S.path; setOn($('tgPath'), S.path); mapDirty(); });
  const calBtnUI = () => setOn($('tgCal'), $('calPane').classList.contains('open') && !$('side').classList.contains('hidden'));
  function calOpen(on) {
    $('calPane').classList.toggle('open', !!on);
    lsSet('rv_ui:calOpen', !!on);
    calBtnUI();
    resizeAll();
  }
  function sideShow(on) {
    $('side').classList.toggle('hidden', !on);
    setOn($('tgMap'), on);
    calBtnUI();
    resizeAll();
  }
  $('tgMap').addEventListener('click', () => sideShow($('side').classList.contains('hidden')));
  $('tgCal').addEventListener('click', () => {
    if ($('side').classList.contains('hidden')) { sideShow(true); calOpen(true); return; }
    calOpen(!$('calPane').classList.contains('open'));
  });

  // ---------------------------------------------------------------- 터파기 설정 창
  // 고칠 때마다 단면 그림과 360 (터파기 구덩이 · 부분 개방 · 단면) 을 바로 다시 그린다. 값은 프로젝트 설정 (맞춤과 같이 저장).
  const secDlg = $('secDlg'), secDia = $('secDia'), secOn = $('secOn'), secSvg = $('secSvg');
  const SEC_UI = [ // 키 · 이름 · 단위 · ± 한 번
    ['cover', '평균매설깊이', 'm', 0.1],
    ['tp', '포장 직각 두께', 'm', 0.05],
    ['n', '기울기 1:n', '', 0.1],
    ['margin', '관 양옆 여유폭', 'm', 0.05],
    ['bed', '모래 기초', 'm', 0.05],
    ['prot', '보호층 (관 위)', 'm', 0.05],
  ];
  const secIn = {};
  const secVal = (k) => (k === 'cover' ? S.cover : TR[k]);
  function trSet(patch) {
    TR = trClean(Object.assign({}, TR, patch), TR);
    trOff = false;
    lsSet(TR_KEY, TR);
    hostCalSoon();
    secUI();
    ugUI();
    dirty();
  }
  function secSetVal(k, v, from) {
    if (k === 'cover') setCover(v, from); else trSet({ [k]: v });
  }
  function secOptions() { // 자료에 있는 관경 (큰 것부터) · 고른 것은 그대로
    const cur = secDia.value;
    const ds = [...new Set(lines.map((L) => Math.round(L.dia * 1000)))].sort((a, b) => b - a);
    if (!ds.length) ds.push(300);
    secDia.textContent = '';
    ds.forEach((d) => { const o = document.createElement('option'); o.value = String(d); o.textContent = 'D' + d; secDia.appendChild(o); });
    secDia.value = ds.indexOf(Number(cur)) >= 0 ? cur : String(ds[0]);
  }
  function secUI() {
    if (secDlg.hidden) return;
    SEC_UI.forEach(([k]) => { if (document.activeElement !== secIn[k]) secIn[k].value = String(round(secVal(k), 3)); });
    secOn.checked = trOn();
    secDlg.classList.toggle('off', !trOn());
    secDraw();
  }
  function secShow(on) {
    secDlg.hidden = !on;
    setOn($('tgSec'), on);
    if (on) { secOptions(); secUI(); }
  }
  function secDraw() {
    const mm = Number(secDia.value) || 300, D = mm / 1000;
    const L0 = lines.find((L) => Math.round(L.dia * 1000) === mm);
    secPic(secSvg, { D: D, col: L0 ? L0.color : '#00e5ff', zc: -S.cover - D / 2, el: null, W: 380 });
  }
  // 단면 그림 (실제 비율, SVG): 지표 = 0 · 다 지은 층 (포장 복구 · 되메우기 · 보호층 · 기초) · 관 · 터파기 윤곽 · 치수.
  // o = { D, col, zc (지표 기준 관 중심, 아래 −), el (지반고 EL → 오른쪽에 EL 글 · 없으면 null), W (그림 폭) }
  function secPic(svg, o) {
    const D = o.D, mm = Math.round(D * 1000), t = TR, zc = o.zc, top = zc + D / 2, c = -top, W = o.W || 380, el = o.el;
    const x = sectAt(D, 0, zc, 0), pb = zc - D / 2, zb = x.zb, wb = x.wb, zt = x.zt, wv = x.wv, zm = Math.max(zt, zb);
    const X = Object.assign({}, x, { g: 0, zc: zc, D: D, zp: protTop(x, zc, D) });
    const wAt = (z) => (z >= zt ? wv : wb + t.n * Math.max(0, z - zb));
    const pl = 72, pr = el != null ? 104 : 80, ptop = 40, pbot = 38, half = wv + 0.45, zl = zb - 0.25;
    const sc = Math.min((W - pl - pr) / (2 * half), (300 - ptop - pbot) / -zl); // 높이는 그림에 맞춤 (300 까지)
    const H = Math.max(150, Math.ceil(ptop + pbot - zl * sc));
    svg.setAttribute('viewBox', '0 0 ' + W + ' ' + H);
    const cx = pl + (W - pl - pr) / 2, Xs = (v) => cx + v * sc, Y = (z) => ptop - z * sc;
    const n1 = (v) => v.toFixed(1), f2 = (v) => v.toFixed(2);
    const P = (pts) => pts.map(([a, z]) => n1(Xs(a)) + ',' + n1(Y(z))).join(' ');
    const txt = (a, b, s, an) => '<text x="' + n1(a) + '" y="' + n1(b) + '" text-anchor="' + (an || 'middle') + '" fill="#e9e9e9" stroke="#141414" stroke-width="3" paint-order="stroke" font-size="11" font-weight="600">' + s + '</text>';
    const ln = (x1, y1, x2, y2, st, da) => '<line x1="' + n1(x1) + '" y1="' + n1(y1) + '" x2="' + n1(x2) + '" y2="' + n1(y2) + '" stroke="' + (st || '#8b8b8b') + '" stroke-width="1"' + (da ? ' stroke-dasharray="' + da + '"' : '') + '/>';
    const dimH = (x1, x2, y, s, dy) => ln(x1, y, x2, y) + ln(x1, y - 4, x1, y + 4) + ln(x2, y - 4, x2, y + 4) + txt((x1 + x2) / 2, y + dy, s);
    const dimV = (a, y1, y2, s, an, dx) => ln(a, y1, a, y2) + ln(a - 4, y1, a + 4, y1) + ln(a - 4, y2, a + 4, y2) + txt(a + dx, (y1 + y2) / 2 + 4, s, an);
    const side = (z) => (z > zm ? [[wb, zb], [wv, zm], [wv, z]] : [[wb, zb], [wAt(z), z]]); // 오른쪽 벽: 바닥 → 높이 z
    const mirror = (pts) => pts.slice().reverse().map(([a, z]) => [-a, z]);
    const pitR = side(0);
    let s = '';
    s += '<rect x="' + n1(Xs(-half)) + '" y="' + n1(Y(0)) + '" width="' + n1(2 * half * sc) + '" height="' + n1(-zl * sc) + '" fill="#5a4330" stroke="none"/>'; // 흙
    if (t.tp > 0) s += '<rect x="' + n1(Xs(-half)) + '" y="' + n1(Y(0)) + '" width="' + n1(2 * half * sc) + '" height="' + n1(t.tp * sc) + '" fill="#5c6068" stroke="none"/>'; // 포장
    for (const [k, pg] of layerPolys(X, zb)) s += '<polygon points="' + P(pg) + '" fill="' + LAY_CSS[k] + '" stroke="none"/>'; // 층
    s += '<circle cx="' + n1(Xs(0)) + '" cy="' + n1(Y(zc)) + '" r="' + n1(D / 2 * sc) + '" fill="' + o.col + '" fill-opacity="0.9" stroke="#ffffff" stroke-width="1.2"/>';
    s += '<path d="M' + n1(Xs(-half)) + ' ' + n1(Y(0)) + 'H' + n1(Xs(-wv)) + 'M' + n1(Xs(wv)) + ' ' + n1(Y(0)) + 'H' + n1(Xs(half)) + '" fill="none" stroke="#c9d0dc" stroke-width="2"/>'; // 도로 면
    s += '<polyline points="' + P(mirror(pitR).concat(pitR)) + '" fill="none" stroke="#fff4dc" stroke-width="1.5"/>'; // 터파기 윤곽
    // 치수
    s += dimH(Xs(-wv), Xs(wv), Y(0) - 16, '폭 ' + f2(2 * wv), -6);
    s += dimV(14, Y(0), Y(zb), '깊이 ' + f2(-zb), 'start', 6);
    if (t.tp > 0) s += dimV(Xs(-wv) - 10, Y(0), Y(-t.tp), '포장 ' + f2(t.tp), 'end', -6);
    if (X.zp > top + 1e-6) s += dimV(Xs(-wAt(X.zp)) - 10, Y(X.zp), Y(top), '보호 ' + f2(X.zp - top), 'end', -6);
    if (t.bed > 0) s += dimV(Xs(-wAt(pb)) - 10, Y(zb), Y(pb), '기초 ' + f2(t.bed), 'end', -6);
    if (c > 0) s += dimV(Xs(wv) + (el != null ? 14 : 26), Y(0), Y(-c), '토피 ' + f2(c), 'start', 6);
    s += ln(Xs(D / 2), Y(zc), Xs(D / 2), Y(zb) + 18, '#5d5d5d') + dimH(Xs(D / 2), Xs(wb), Y(zb) + 14, '여유 ' + f2(t.margin), 14);
    if (zm > zb + 1e-6 && t.n > 0) s += txt(Xs(wAt((zm + zb) / 2)) - 6, Y((zm + zb) / 2) + 4, '1:' + round(t.n, 3), 'end');
    s += D / 2 * sc > 16 ? txt(Xs(0), Y(zc) + 4, 'D' + mm) : txt(Xs(D / 2) + 6, Y(zc) + 4, 'D' + mm, 'start');
    if (el != null) { // 표고 (EL): 오른쪽 줄 · 겹치면 아래로 밀고 꺾은 지시선
      const tags = [['지반', 0, wv], ['상단', top, 0], ['중심', zc, D / 2], ['하단', pb, 0], ['바닥', zb, wb]];
      let last = -Infinity;
      for (const [name, z, at] of tags) {
        const y0 = Y(z), y = Math.max(y0, last + 12);
        last = y;
        s += '<polyline points="' + n1(Xs(at)) + ',' + n1(y0) + ' ' + n1(W - 80) + ',' + n1(y0) + ' ' + n1(W - 74) + ',' + n1(y) + '" fill="none" stroke="#8b8b8b" stroke-width="1" stroke-dasharray="2 2"/>';
        s += '<text x="' + (W - 4) + '" y="' + n1(y + 4) + '" text-anchor="end" fill="#e9e9e9" font-size="10.5" font-weight="600"><tspan fill="#8b8b8b">' + name + ' </tspan>' + f2(el + z) + '</text>';
      }
    }
    svg.innerHTML = s;
  }
  (function buildSec() {
    const box = $('secRows');
    SEC_UI.forEach(([k, name, unit, step]) => {
      const lab = document.createElement('span');
      lab.className = 'cl'; lab.textContent = name;
      const minus = document.createElement('button');
      minus.className = 'cb'; minus.type = 'button'; minus.textContent = '−';
      const inp = document.createElement('input');
      inp.type = 'number'; inp.step = String(step); inp.min = '0'; inp.inputMode = 'decimal'; inp.id = 'sec_' + k;
      const plus = document.createElement('button');
      plus.className = 'cb'; plus.type = 'button'; plus.textContent = '+';
      const un = document.createElement('span');
      un.className = 'cu'; un.textContent = unit;
      box.append(lab, minus, inp, plus, un);
      secIn[k] = inp;
      const bump = (d) => secSetVal(k, round(secVal(k) + d * step, 3));
      holdRepeat(minus, () => bump(-1));
      holdRepeat(plus, () => bump(1));
      inp.addEventListener('input', () => { if (isNum(inp.value)) secSetVal(k, Number(inp.value), inp); });
      inp.addEventListener('change', secUI);
    });
    secDia.addEventListener('change', secDraw);
    secOn.addEventListener('change', () => trSet({ on: secOn.checked }));
    $('secDef').addEventListener('click', () => { trSet(Object.assign({}, TR_DEF)); toast('가시화 기본값'); });
    $('secClose').addEventListener('click', () => secShow(false));
    $('secX').addEventListener('click', () => secShow(false));
    $('tgSec').addEventListener('click', () => secShow(secDlg.hidden));
  })();

  // ---------------------------------------------------------------- 관 누르기: 관 정보 (매설 · 터파기) · 부분 개방 · 단면
  // 값은 사진과 무관하게 자료로 (지표 = g, 없으면 groundAt · 관 중심 = hasZ 면 z, 아니면 지표 − 평균매설깊이 − 관경/2 · 꼭짓점 사이 선형).
  // 토피 = 지표 − 관 윗면 · 관 상단고 = 중심 + D/2 · 관 하단고 = 중심 − D/2 (관 바깥 기준) · 터파기 = 터파기 구덩이와 같은 식 (sectAt).
  function secAt(L, k, t) {
    const P = L.pts, ga = gQ(L, k), gb = gQ(L, k + 1), za = zcQ(L, k), zb = zcQ(L, k + 1);
    const g = ga + (gb - ga) * t, zc = za + (zb - za) * t, D = L.dia;
    let s = 0;
    for (let j = 0; j < k; j++) s += Math.hypot(P[j * 3 + 3] - P[j * 3], P[j * 3 + 4] - P[j * 3 + 1]);
    const dx = P[k * 3 + 3] - P[k * 3], dy = P[k * 3 + 4] - P[k * 3 + 1], dl = Math.hypot(dx, dy) || 1;
    s += t * dl;
    const x = sectAt(D, g, zc, 0), zp = protTop(x, zc, D), top = zc + D / 2;
    return {
      L: L, k: k, t: t, s: s, e: P[k * 3] + dx * t, n: P[k * 3 + 1] + dy * t, nx: -dy / dl, ny: dx / dl, color: L.color,
      g: g, zc: zc, top: top, bot: zc - D / 2, cover: g - top, D: D, depth: g - x.zb, wtop: 2 * x.wv, wbot: 2 * x.wb,
      bed: TR.bed, prot: Math.max(0, zp - top), X: Object.assign({}, x, { g: g, zc: zc, D: D, zp: zp }),
    };
  }
  const f2 = (v) => v.toFixed(2);
  function kvRows(box, rows, pre) { // [[키, 이름, 값, 글]] → 이름 · 값 (data-v = 반올림 전)
    box.textContent = '';
    for (const r of rows) {
      const k = document.createElement('span'), v = document.createElement('span');
      k.className = 'k'; v.className = 'v';
      k.textContent = r[1];
      if (r[0]) { v.id = pre + r[0]; v.dataset.v = String(r[2]); }
      v.textContent = r[3];
      box.append(k, v);
    }
  }
  const elTxt = (v) => 'EL ' + f2(v), mTxt = (v) => f2(v) + ' m', dTxt = (D) => 'D' + Math.round(D * 1000);
  // 관 정보 (작은 칸): 관경 · 관종 · 토피 · 관 중심고 · 지반고
  const PC = { src: -1, e: 0, n: 0 };
  function cardHide() { $('pipeCard').hidden = true; PC.src = -1; }
  function cardShow(L, pk, at) {
    const C = secAt(L, pk.k, pk.t), card = $('pipeCard');
    PC.src = L.src; PC.e = C.e; PC.n = C.n;
    $('pcName').textContent = L.base || L.label || dTxt(L.dia);
    kvRows($('pcRows'), [
      ['dia', '관경', L.dia, dTxt(L.dia) + (L.diaKnown ? '' : ' ?')],
      ['kind', '관종', L.kind || L.base || '', L.kind || L.base || '-'],
      ['cover', '토피', C.cover, mTxt(C.cover)],
      ['zc', '관 중심고', C.zc, elTxt(C.zc)],
      ['g', '지반고', C.g, elTxt(C.g)],
    ].concat(L.hasZ ? [] : [['', '관 표고', '', '평균매설깊이']]), 'pc_');
    Object.assign(card.dataset, { src: L.src, e: C.e, n: C.n });
    card.hidden = false;
    if (at) {
      card.style.bottom = '';
      card.style.left = clamp(at[0] + 16, 8, Math.max(8, V.w - card.offsetWidth - 8)) + 'px';
      card.style.top = clamp(at[1] - card.offsetHeight / 2, 8, Math.max(8, V.h - card.offsetHeight - 8)) + 'px';
    } else { card.style.left = '12px'; card.style.top = ''; card.style.bottom = '64px'; }
  }
  $('pcX').addEventListener('click', cardHide);
  $('pcSec').addEventListener('click', () => { const p = Object.assign({}, PC); setMode('sec'); scSet(p.src, p.e, p.n); });
  // 단면 (띄운 칸): 누른 자리 횡단면 (실제 표고) · 360 에는 그 자리 세운 면
  function scSet(src, e, n) { SC = { src: src, e: round(e, 4), n: round(n, 4) }; SCR = null; scUI(); dirty(); }
  function scGet() {
    if (S.mode !== 'sec' || !SC) return null;
    const key = [SC.src, SC.e, SC.n, TR.tp, TR.n, TR.margin, TR.bed, TR.prot, S.cover, pipeGen, selGen].join('|');
    if (SCR && SCR.key === key && SCR.of === lines) return SCR.r;
    SCR = { key: key, of: lines, r: null };
    const L = lines.find((q) => q.src === SC.src);
    if (!L) return null;
    const P = L.pts;
    let best = null;
    for (let k = 0; k + 3 < P.length; k += 3) {
      const dx = P[k + 3] - P[k], dy = P[k + 4] - P[k + 1], l2 = dx * dx + dy * dy;
      const t = l2 > 0 ? clamp(((SC.e - P[k]) * dx + (SC.n - P[k + 1]) * dy) / l2, 0, 1) : 0, d = Math.hypot(SC.e - P[k] - t * dx, SC.n - P[k + 1] - t * dy);
      if (!best || d < best.d) best = { k: k / 3, t: t, d: d };
    }
    SCR.r = best ? secAt(L, best.k, best.t) : null;
    return SCR.r;
  }
  let scShown = '';
  const SC_ROWS = [ // 두 줄씩 (왼쪽 표고 · 오른쪽 길이)
    ['g', '지반고', elTxt], ['cover', '토피', mTxt],
    ['top', '관 상단고', elTxt], ['D', '관경', dTxt],
    ['zc', '관 중심고', elTxt], ['depth', '터파기 깊이', mTxt],
    ['bot', '관 하단고', elTxt], ['wtop', '터파기 폭 (위)', mTxt],
    ['bed', '기초', mTxt], ['wbot', '터파기 폭 (아래)', mTxt],
    ['prot', '보호층', mTxt],
  ];
  function scUI() {
    const C = scGet(), card = $('secCard');
    if (!C) { card.hidden = true; scShown = ''; opUI(); return; }
    if (SCR.key === scShown && !card.hidden) return;
    scShown = SCR.key;
    const L = C.L;
    $('scName').textContent = L.base || L.label || dTxt(L.dia);
    $('scSta').textContent = '측점 ' + C.s.toFixed(1) + ' m';
    kvRows($('scRows'), SC_ROWS.map(([k, name, f]) => [k, name, C[k], f(C[k])]), 'sc_');
    Object.assign(card.dataset, { src: L.src, e: C.e, n: C.n, s: C.s, k: C.k, t: C.t });
    secPic($('scSvg'), { D: C.D, col: L.color, zc: C.zc - C.g, el: C.g, W: 360 });
    $('scTag').textContent = (trDefault() ? '터파기 = 가시화 기본값' : '터파기 = 직접 입력') + (L.hasZ ? '' : ' · 관 표고 = 평균매설깊이');
    card.hidden = false;
    opUI();
  }
  $('scX').addEventListener('click', () => { SC = null; SCR = null; scUI(); dirty(); });
  // 부분 개방 안내 줄: 관 누르기 · 길이 · 폭 + · 닫기 (단면 방식에서 아직 안 누르면 «관 누르기» 만)
  function opSet(src, e, n) {
    OP = { src: src, e: round(e, 4), n: round(n, 4) };
    OPR = null;
    if (!opRes()) { OP = null; toast('열 수 없음'); }
    opUI();
    dirty();
  }
  function opClose() { OP = null; OPR = null; setMode('bury'); }
  function opUI() {
    const chip = $('opChip'), on = S.mode === 'open' || (S.mode === 'sec' && !SC);
    chip.classList.toggle('show', on);
    $('app').classList.toggle('opOn', on);
    const R = S.mode === 'open' ? opRes() : null, d = chip.dataset;
    chip.classList.toggle('sec', S.mode === 'sec');
    chip.classList.toggle('idle', !R);
    $('opTip').textContent = R ? '' : '관 누르기';
    $('opLenV').textContent = OPT.len + ' m';
    $('opLen').value = String(OPT.len);
    if (document.activeElement !== $('opEx')) $('opEx').value = String(OPT.ex);
    if (R) Object.assign(d, { src: OP.src, e: OP.e, n: OP.n, s: R.s, s0: R.s0, s1: R.s1 }); // 반올림 전 (확인 · 시험)
    else ['src', 'e', 'n', 's', 's0', 's1'].forEach((k) => { delete d[k]; });
  }
  function optSet(patch) {
    Object.assign(OPT, patch);
    OPT.len = clamp(Math.round(OPT.len), OP_LEN[0], OP_LEN[1]);
    OPT.ex = clamp(OPT.ex, 0, 3);
    lsSet('rv_ui:open', OPT);
    opUI();
    dirty();
  }
  $('opLen').addEventListener('input', () => { if (isNum($('opLen').value)) optSet({ len: Number($('opLen').value) }); });
  $('opEx').addEventListener('input', () => { if (isNum($('opEx').value)) optSet({ ex: Number($('opEx').value) }); });
  $('opEx').addEventListener('change', () => { $('opEx').value = String(OPT.ex); });
  $('opClose').addEventListener('click', opClose);
  // 관 누름 (360 · 지도): 관이 있으면 true. 지도에서 누르면 그쪽을 본다 (멀면 가까운 사진으로).
  function pipeTap(view, x, y) {
    if (!S.pipes || !lines.length || S.cur < 0) return false;
    const src = lineAt(view, x, y);
    if (src < 0) return false;
    const L = lines.find((q) => q.src === src), pk = L && pickOf(view, L, x, y);
    if (!pk) return false;
    const p = pickPoint(L, pk);
    if (S.mode === 'open') opSet(src, p[0], p[1]);
    else if (S.mode === 'sec') scSet(src, p[0], p[1]);
    else { cardShow(L, pk, view === 'v' ? [x, y] : null); return true; }
    if (view === 'm') cfGo({ e: p[0], n: p[1], g: p[3] });
    return true;
  }
  // Esc: 단면 칸 → 부분 개방 (매설로) → 관 정보 차례로 닫음
  function ugEsc() {
    if (S.mode === 'sec' && SC) { SC = null; SCR = null; scUI(); dirty(); return true; }
    if (S.mode === 'open') { opClose(); return true; }
    if (!$('pipeCard').hidden) { cardHide(); return true; }
    return false;
  }
  modeUI();
  ugUI();

  // ---------------------------------------------------------------- 맞춤 창
  const CAL_UI = [
    ['dx', '동', 'm'], ['dy', '북', 'm'], ['dz', '높이', 'm'],
    ['dyaw', '방향', '°'], ['dpitch', '남북 기울기', '°'], ['droll', '동서 기울기', '°'],
  ];
  const calInputs = {}, calLabels = {};
  function curVid() { return S.cur >= 0 ? shots[S.cur].video : ''; }
  function calChanged() {
    calMemo.clear();
    updCam();
    dirty();
  }
  function holdRepeat(btn, fn) {
    let t1 = 0, t2 = 0;
    const stop = () => { clearTimeout(t1); clearInterval(t2); };
    btn.addEventListener('pointerdown', (e) => {
      if (e.pointerType === 'mouse' && e.button !== 0) return;
      e.preventDefault();
      fn();
      stop();
      t1 = setTimeout(() => { t2 = setInterval(fn, 70); }, 420);
    });
    ['pointerup', 'pointerleave', 'pointercancel'].forEach((ev) => btn.addEventListener(ev, stop));
    btn.addEventListener('click', (e) => { if (e.detail === 0) fn(); }); // 키보드
  }
  function buildCal() {
    const box = $('calRows');
    CAL_UI.forEach(([k, name, unit]) => {
      const lab = document.createElement('span');
      lab.className = 'cl';
      lab.textContent = name;
      const minus = document.createElement('button');
      minus.className = 'cb'; minus.type = 'button'; minus.textContent = '−';
      const inp = document.createElement('input');
      inp.type = 'number'; inp.step = String(STEP); inp.inputMode = 'decimal';
      const plus = document.createElement('button');
      plus.className = 'cb'; plus.type = 'button'; plus.textContent = '+';
      const un = document.createElement('span');
      un.className = 'cu'; un.textContent = unit;
      box.append(lab, minus, inp, plus, un);
      calInputs[k] = inp;
      calLabels[k] = lab;
      const bump = (d) => {
        const vid = curVid();
        setCal(vid, k, round(calibFor(vid)[k] + d * STEP, 4));
        calChanged();
        updCalUI();
      };
      holdRepeat(minus, () => bump(-1));
      holdRepeat(plus, () => bump(1));
      inp.addEventListener('input', () => {
        if (!isNum(inp.value)) return;
        setCal(curVid(), k, Number(inp.value));
        calChanged();
        updCalMarks();
      });
      inp.addEventListener('change', updCalUI);
    });
    $('calHead').addEventListener('click', () => calOpen(!$('calPane').classList.contains('open')));
    if (lsGet('rv_ui:calOpen')) $('calPane').classList.add('open');
    calBtnUI();
    $('calReset').addEventListener('click', () => {
      const vid = curVid();
      CAL_F.forEach((k) => setCal(vid, k, 0));
      calChanged();
      updCalUI();
      toast('초기화 · 영상 ' + (vid || '-'));
    });
    $('calSave').addEventListener('click', saveCalFile);
  }
  function updCalMarks() {
    // 파일 값과 다르면 이름을 노랗게
    const vid = curVid(), c = calibFor(vid);
    CAL_F.forEach((k) => calLabels[k].classList.toggle('mod', Math.abs(c[k] - calFileVal(vid, k)) > 1e-9));
    alUI(); // 점 짝 어긋남도 지금 값으로
  }
  function updCalUI() {
    const vid = curVid(), c = calibFor(vid);
    $('calVid').textContent = '영상 ' + (vid || '-');
    CAL_F.forEach((k) => {
      if (document.activeElement !== calInputs[k]) calInputs[k].value = String(round(c[k], 3));
    });
    updCalMarks();
  }
  // calib.js 글 (내려받기 · 프로그램 저장이 같은 글을 쓴다)
  function calibText() {
    const ids = new Set();
    (Array.isArray(PRJ.videos) ? PRJ.videos : []).forEach((v) => { if (v && v.id != null) ids.add(String(v.id)); });
    shots.forEach((s) => ids.add(s.video));
    Object.keys(fileCal).forEach((k) => ids.add(k));
    Object.keys(localCal).forEach((k) => ids.add(k));
    Object.keys(localPairs).forEach((k) => ids.add(k));
    const out = { videos: {} };
    [...ids].sort().forEach((id) => {
      const c = calibFor(id), o = {};
      CAL_F.forEach((k) => { o[k] = round(c[k], 4); });
      const pl = pairsOf(id);
      if (pl.length) o.pairs = pl; // 점 찍어 맞추기 짝 (다른 PC 에서도 이어서 고치게)
      out.videos[id] = o;
    });
    out.trench = { tp: TR.tp, n: TR.n, margin: TR.margin, bed: TR.bed, prot: TR.prot, on: TR.on }; // 터파기 설정 (프로젝트 설정)
    return 'window.RV_CALIB = ' + JSON.stringify(out, null, 2) + ';\n';
  }
  function saveCalFile() {
    if (HOST()) { // 프로그램 안: 내려받기 대신 프로젝트 폴더에 바로
      hostCalFlush().then((r) => { if (r && r.ok !== false) toast('맞춤 저장됨'); });
      return;
    }
    const text = calibText();
    const url = URL.createObjectURL(new Blob([text], { type: 'text/javascript;charset=utf-8' }));
    const a = document.createElement('a');
    a.href = url;
    a.download = 'calib.js';
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 5000);
    toast('calib.js → data 폴더에 넣기');
  }

  // ---------------------------------------------------------------- 지도
  const mc = $('map'), mx = mc.getContext('2d');
  const M = { cx: 0.5, cy: 0.5, z: 3, w: 0, h: 0, dpr: 1, fitted: false };
  const lon2x = (lon) => (lon + 180) / 360;
  const lat2y = (lat) => { const s = Math.sin(clamp(lat, -85.0511, 85.0511) * D2R); return 0.5 - Math.log((1 + s) / (1 - s)) / (4 * Math.PI); };
  const x2lon = (x) => x * 360 - 180;
  const y2lat = (y) => R2D * Math.atan(Math.sinh(Math.PI * (1 - 2 * y)));

  shots.forEach((s) => {
    if (Number.isFinite(s.lat) && Number.isFinite(s.lon)) { s.mx = lon2x(s.lon); s.my = lat2y(s.lat); s.gx = s.mx; s.gy = s.my; }
  });
  // 격자(e,n) → 지도 방향: 사진 점들로 닮음 변환을 맞춘다 (자오선 수렴각까지 반영).
  const SIM = (function () {
    const pts = shots.filter((s) => Number.isFinite(s.mx));
    if (!pts.length) return null;
    let me = 0, mn = 0, wx = 0, wy = 0;
    pts.forEach((s) => { me += s.e; mn += s.n; wx += s.mx; wy -= s.my; });
    me /= pts.length; mn /= pts.length; wx /= pts.length; wy /= pts.length;
    let nr = 0, ni = 0, den = 0;
    pts.forEach((s) => {
      const ze = s.e - me, zn = s.n - mn, we = s.mx - wx, wn = -s.my - wy;
      nr += ze * we + zn * wn;
      ni += ze * wn - zn * we;
      den += ze * ze + zn * zn;
    });
    let ar, ai;
    if (den > 25) { ar = nr / den; ai = ni / den; } else { ar = 1 / (40075016.686 * Math.cos(pts[0].lat * D2R)); ai = 0; }
    return { me, mn, wx, wy, ar, ai };
  })();
  function en2m(e, n) {
    const ze = e - SIM.me, zn = n - SIM.mn;
    return [SIM.wx + SIM.ar * ze - SIM.ai * zn, -(SIM.wy + SIM.ai * ze + SIM.ar * zn)];
  }
  function mer2en(X, Y) { // en2m 의 거꾸로
    const d2 = SIM.ar * SIM.ar + SIM.ai * SIM.ai, a = X - SIM.wx, b = -Y - SIM.wy;
    return [SIM.me + (a * SIM.ar + b * SIM.ai) / d2, SIM.mn + (b * SIM.ar - a * SIM.ai) / d2];
  }
  // 지도 위 사진 점 = GPS 자리(gx, gy) + 그 영상 맞춤(dx, dy) 만큼 (점 찍어 맞추기 뒤 경로가 도로 위로 옮겨 가게 · JACK 1123)
  function refreshShotMap() {
    if (!SIM) return;
    for (const s of shots) {
      if (!Number.isFinite(s.gx)) continue;
      const c = calibFor(s.video);
      if (c && (c.dx || c.dy)) {
        const a = en2m(s.e + c.dx, s.n + c.dy), b = en2m(s.e, s.n);
        s.mx = s.gx + a[0] - b[0]; s.my = s.gy + a[1] - b[1];
      } else { s.mx = s.gx; s.my = s.gy; }
    }
  }
  // 관로 지도 자리 (위경도 ll 이 있으면 그것, 없으면 격자 → 닮음 변환)
  function lineToMap(L) {
    const cnt = L.pts.length / 3;
    if (L.ll) {
      L.mx = new Float64Array(cnt); L.my = new Float64Array(cnt);
      L.ll.forEach((q, k) => { L.mx[k] = lon2x(Number(q[1])); L.my[k] = lat2y(Number(q[0])); });
    } else if (SIM) {
      L.mx = new Float64Array(cnt); L.my = new Float64Array(cnt);
      for (let k = 0; k < cnt; k++) { const m = en2m(L.pts[k * 3], L.pts[k * 3 + 1]); L.mx[k] = m[0]; L.my[k] = m[1]; }
    }
  }
  function linesToMap() { lines.forEach(lineToMap); }
  linesToMap();
  // data/pipes.js 다시 읽기 (프로그램이 [관로 넣기] 로 새로 쓴 뒤). 같은 주소는 캐시에서 올 수 있어 ?v= 를 붙인다.
  // 그 뒤 관 · 깊이 방식 관 그물 · 지도 선 · 이름표를 새 자료로 다시 만든다. 못 읽으면 옛 자료 그대로.
  function reloadPipes() {
    const old = window.RV_PIPES;
    return new Promise((resolve, reject) => {
      window.RV_PIPES = undefined;
      const sc = document.createElement('script');
      sc.onload = () => { sc.remove(); resolve(); };
      sc.onerror = () => { sc.remove(); window.RV_PIPES = old; reject(new Error('관로 파일 못 읽음')); };
      sc.src = 'data/pipes.js?v=' + Date.now();
      document.head.appendChild(sc);
    }).then(() => {
      edReset(); // 고치던 것 · 되돌리기는 옛 자료 것 → 비움
      lines = buildLines(window.RV_PIPES);
      linesToMap();
      pipeGen++; // 깊이 방식 관 · 터파기 그물 다시 (이은 줄 · 단면은 lines 가 바뀌면 저절로 다시)
      if (!secDlg.hidden) { secOptions(); secUI(); }
      cardHide();
      dirty();
      return lines.length;
    });
  }
  // 비디오별 비행 경로
  const paths = (function () {
    const by = new Map();
    shots.forEach((s) => {
      if (!Number.isFinite(s.mx)) return;
      if (!by.has(s.video)) by.set(s.video, []);
      by.get(s.video).push(s);
    });
    return [...by.values()];
  })();

  // 바탕 지도: 이 브라우저에 VWorld 키가 있으면 위성+이름, 없으면 project.basemap (기본 osm).
  // 키는 자료 파일에 두지 않고 localStorage 에만 둔다.
  const KEY_LS = 'rv_vworld_key';
  function keyGet() { try { return (localStorage.getItem(KEY_LS) || '').trim(); } catch (e) { return ''; } }
  function keySet(v) { try { if (v) localStorage.setItem(KEY_LS, v); else localStorage.removeItem(KEY_LS); } catch (e) { /* 저장 못 해도 계속 */ } }
  function vworldBase(key) {
    const k = encodeURIComponent(key);
    return {
      layers: [
        { id: 'vs', vw: true, tpl: 'https://api.vworld.kr/req/wmts/1.0.0/' + k + '/Satellite/{z}/{y}/{x}.jpeg', min: 6, max: 19 },
        { id: 'vh', names: true, tpl: 'https://api.vworld.kr/req/wmts/1.0.0/' + k + '/Hybrid/{z}/{y}/{x}.png', min: 6, max: 19 }, // 이름 · 도로 글자
      ],
      attrib: '© VWorld',
      vworld: true,
    };
  }
  // file:// 로 열면 Referer 가 없어 OSM 이 «Access blocked» 그림(403)을 준다 (onerror 도 안 남) → OSM 은 아예 청하지 않는다.
  const FILE = location.protocol === 'file:';
  function blankBase() { return { layers: [], attrib: '', blank: true }; }
  function osmBase() {
    if (FILE) return blankBase();
    return { layers: [{ id: 'o', tpl: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png', min: 0, max: 19 }], attrib: '© OpenStreetMap', link: 'https://www.openstreetmap.org/copyright' };
  }
  function projectBase() {
    const b = PRJ.basemap || {};
    const t = String(b.type || 'osm').toLowerCase();
    if (t === 'vworld' && b.key) return vworldBase(String(b.key).trim());
    if (t === 'xyz' && b.url) {
      if (FILE && /openstreetmap\.org/i.test(String(b.url))) return blankBase();
      return { layers: [{ id: 'x', tpl: String(b.url), min: num(b.minZoom, 0), max: num(b.maxZoom, 19) }], attrib: String(b.attribution || '') };
    }
    return osmBase();
  }
  function pickBase() { const k = keyGet(); return k ? vworldBase(k) : projectBase(); }
  let BASE = pickBase();
  let vwStat = { ok: 0, bad: 0 }; // VWorld 위성 타일 성공 · 실패 수 (키 거절 알아내기)
  // [이름]: VWorld 이름 · 도로 글자 겹 (Hybrid) 켜고 끄기. 점 찍을 때 땅을 가리지 않게 기본은 끔. 이 브라우저에 기억.
  let NAMES = lsGet('rv_ui:mapNames') === true;
  function updNames() {
    const b = $('mNames');
    b.hidden = !BASE.vworld;
    setOn(b, NAMES);
  }
  $('mNames').addEventListener('click', () => {
    NAMES = !NAMES;
    lsSet('rv_ui:mapNames', NAMES);
    updNames();
    mapDirty();
  });

  function updAttrib() {
    $('mapNote').classList.toggle('show', !!BASE.blank);
    const a = $('attrib');
    a.textContent = '';
    if (!BASE.attrib) return;
    if (BASE.link) {
      const l = document.createElement('a');
      l.href = BASE.link; l.target = '_blank'; l.rel = 'noopener'; l.textContent = BASE.attrib;
      a.appendChild(l);
    } else a.textContent = BASE.attrib;
  }
  function setBase(b) {
    BASE = b;
    tiles.forEach((t) => { if (!t.ok) t.img.src = ''; });
    tiles.clear();
    vwStat = { ok: 0, bad: 0 };
    updAttrib();
    $('keyBtn').classList.toggle('on', !!BASE.vworld);
    updNames();
    mapDirty();
  }
  function vworldFailed() {
    // 위성 타일이 하나도 안 오고 계속 실패 → 키 거절로 보고 OSM 으로
    setBase(osmBase());
    toast('지도 키 확인');
  }

  const tiles = new Map();
  function tileUrl(tpl, z, x, y) {
    return tpl.replace(/\{z\}/g, z).replace(/\{x\}/g, x).replace(/\{-y\}/g, (1 << z) - 1 - y).replace(/\{y\}/g, y);
  }
  function tileGet(L, z, x, y, create) {
    const key = L.id + '/' + z + '/' + x + '/' + y;
    let t = tiles.get(key);
    if (t) { tiles.delete(key); tiles.set(key, t); return t; }
    if (!create) return null;
    t = { ok: false, bad: false, img: new Image() };
    const st = vwStat;
    t.img.decoding = 'async';
    t.img.onload = () => { t.ok = true; if (L.vw && st === vwStat) st.ok++; mapDirty(); };
    t.img.onerror = () => {
      t.bad = true;
      if (L.vw && st === vwStat) { st.bad++; if (!st.ok && st.bad >= 4) vworldFailed(); }
    };
    t.img.src = tileUrl(L.tpl, z, x, y);
    tiles.set(key, t);
    while (tiles.size > CACHE_TILES) {
      const old = tiles.keys().next().value, ot = tiles.get(old);
      tiles.delete(old);
      if (ot && !ot.ok) ot.img.src = '';
    }
    return t;
  }
  function drawLayer(L) {
    if (M.z < L.min - 2) return;
    const zi = clamp(Math.round(M.z), L.min, L.max);
    const n = 1 << zi, sc = Math.pow(2, M.z - zi), ts = TILE * sc;
    const wx = M.cx * n * TILE, wy = M.cy * n * TILE;
    // 화면 둘레 한 칸까지 미리 받아 둔다 (끌 때 빈 곳이 덜 보이게).
    const x0 = Math.floor((wx - M.w / 2 / sc) / TILE) - 1, x1 = Math.floor((wx + M.w / 2 / sc) / TILE) + 1;
    const y0 = Math.max(0, Math.floor((wy - M.h / 2 / sc) / TILE) - 1), y1 = Math.min(n - 1, Math.floor((wy + M.h / 2 / sc) / TILE) + 1);
    if ((x1 - x0 + 1) * (y1 - y0 + 1) > 300) return;
    for (let ty = y0; ty <= y1; ty++) {
      for (let tx = x0; tx <= x1; tx++) {
        const sx = (tx * TILE - wx) * sc + M.w / 2, sy = (ty * TILE - wy) * sc + M.h / 2;
        const xx = ((tx % n) + n) % n;
        const t = tileGet(L, zi, xx, ty, true);
        if (t.ok) { mx.drawImage(t.img, sx, sy, ts + 0.5, ts + 0.5); continue; }
        // 아직 없으면 윗단계 타일을 늘려 그린다.
        for (let k = 1; k <= 6 && zi - k >= L.min; k++) {
          const p = tileGet(L, zi - k, xx >> k, ty >> k, false);
          if (p && p.ok) {
            const sub = TILE / (1 << k);
            mx.drawImage(p.img, (xx - ((xx >> k) << k)) * sub, (ty - ((ty >> k) << k)) * sub, sub, sub, sx, sy, ts + 0.5, ts + 0.5);
            break;
          }
        }
      }
    }
  }
  function mk() { return TILE * Math.pow(2, M.z); }
  function m2s(X, Y) { const k = mk(); return [(X - M.cx) * k + M.w / 2, (Y - M.cy) * k + M.h / 2]; }
  function s2m(x, y) { const k = mk(); return [(x - M.w / 2) / k + M.cx, (y - M.h / 2) / k + M.cy]; }
  // 바탕 타일이 없을 때: 격자 좌표(e, n) 100 m 격자. 칸이 8 px 보다 좁으면 1 km, 그래도 좁으면 안 그림.
  function drawGrid() {
    if (!SIM) return;
    const d2 = SIM.ar * SIM.ar + SIM.ai * SIM.ai;
    const pxm = Math.sqrt(d2) * mk(); // 1 m 당 px
    let step = 100;
    if (step * pxm < 8) step = 1000;
    if (step * pxm < 8) return;
    const m2en = (x, y) => { const [X, Y] = s2m(x, y); return mer2en(X, Y); };
    const cs = [m2en(0, 0), m2en(M.w, 0), m2en(0, M.h), m2en(M.w, M.h)];
    const e0 = Math.min(...cs.map((c) => c[0])), e1 = Math.max(...cs.map((c) => c[0]));
    const n0 = Math.min(...cs.map((c) => c[1])), n1 = Math.max(...cs.map((c) => c[1]));
    const seg = (ea, na, eb, nb) => {
      const a = en2m(ea, na), b = en2m(eb, nb), p = m2s(a[0], a[1]), q = m2s(b[0], b[1]);
      mx.moveTo(p[0], p[1]); mx.lineTo(q[0], q[1]);
    };
    mx.beginPath();
    for (let e = Math.ceil(e0 / step) * step; e <= e1; e += step) seg(e, n0, e, n1);
    for (let n = Math.ceil(n0 / step) * step; n <= n1; n += step) seg(e0, n, e1, n);
    mx.strokeStyle = 'rgba(255, 255, 255, 0.08)';
    mx.lineWidth = 1;
    mx.stroke();
  }
  // 격자 방위(°) → 지도 화면 방향 각 (canvas 각, 시계 방향)
  function mapAngle(yawDeg) {
    const y = yawDeg * D2R, ze = Math.sin(y), zn = Math.cos(y);
    if (!SIM) return Math.atan2(-zn, ze);
    const we = SIM.ar * ze - SIM.ai * zn, wn = SIM.ai * ze + SIM.ar * zn;
    return Math.atan2(-wn, we);
  }

  function drawMap() {
    const w = M.w, h = M.h;
    if (w < 2 || h < 2) return;
    refreshShotMap();
    mx.setTransform(M.dpr, 0, 0, M.dpr, 0, 0);
    mx.fillStyle = '#1a1a1a';
    mx.fillRect(0, 0, w, h);
    mx.imageSmoothingEnabled = true;
    if (BASE.blank) drawGrid(); else BASE.layers.forEach((L) => { if (NAMES || !L.names) drawLayer(L); });
    const k = mk(), ox = w / 2 - M.cx * k, oy = h / 2 - M.cy * k;
    mx.lineCap = 'round';
    mx.lineJoin = 'round';

    // 지장물 (관로 밑 · 점선)
    if (obShow()) {
      mx.setLineDash([6, 5]);
      mx.lineCap = 'butt'; // 둥근 끝이면 점선 틈이 메워짐
      for (const L of obst) {
        if (!L.mx) continue;
        mx.beginPath();
        for (let j = 0; j < L.mx.length; j++) {
          const x = L.mx[j] * k + ox, y = L.my[j] * k + oy;
          if (j) mx.lineTo(x, y); else mx.moveTo(x, y);
        }
        mx.strokeStyle = 'rgba(8, 10, 14, 0.6)';
        mx.lineWidth = 4.5;
        mx.stroke();
        mx.strokeStyle = L.color;
        mx.lineWidth = 2.5;
        mx.stroke();
      }
      mx.setLineDash([]);
      mx.lineCap = 'round';
    }
    // 관로 (관로 고치기에서 고른 관은 맨 위 · 흰 테두리 · 밝게)
    const EL = S.pipes ? edLine() : null;
    if (S.pipes) {
      for (const L of lines) {
        if (!L.mx || L === EL) continue;
        mx.beginPath();
        for (let j = 0; j < L.mx.length; j++) {
          const x = L.mx[j] * k + ox, y = L.my[j] * k + oy;
          if (j) mx.lineTo(x, y); else mx.moveTo(x, y);
        }
        const lw = clamp(L.dia * 10, 2, 8); // 관경 0.2 m → 2 px … 0.8 m 이상 → 8 px
        mx.strokeStyle = 'rgba(8, 10, 14, 0.65)';
        mx.lineWidth = lw + 2.5;
        mx.stroke();
        mx.strokeStyle = L.color;
        mx.lineWidth = lw;
        mx.stroke();
      }
    }
    // 부분 개방 구간 (흰 테두리 굵은 선) · 단면 자리 (노란 가로선)
    if (SIM) {
      const R = S.mode === 'open' ? opRes() : null, C = S.mode === 'sec' ? scGet() : null;
      const at = (e, n) => { const m = en2m(e, n); return [m[0] * k + ox, m[1] * k + oy]; };
      if (R) {
        mx.beginPath();
        for (let j = 0; j < R.T.m; j++) { const p = at(R.T.E[j], R.T.N[j]); if (j) mx.lineTo(p[0], p[1]); else mx.moveTo(p[0], p[1]); }
        mx.lineCap = 'butt';
        mx.strokeStyle = 'rgba(8, 10, 14, 0.85)'; mx.lineWidth = 12; mx.stroke();
        mx.strokeStyle = '#fff4dc'; mx.lineWidth = 8; mx.stroke();
        mx.lineCap = 'round';
      }
      if (C) {
        const a = at(C.e + C.nx * 2, C.n + C.ny * 2), b = at(C.e - C.nx * 2, C.n - C.ny * 2), c = at(C.e, C.n);
        const dx = b[0] - a[0], dy = b[1] - a[1], l = Math.hypot(dx, dy) || 1, r = 9;
        mx.beginPath(); mx.moveTo(c[0] - dx / l * r, c[1] - dy / l * r); mx.lineTo(c[0] + dx / l * r, c[1] + dy / l * r);
        mx.strokeStyle = 'rgba(8, 10, 14, 0.85)'; mx.lineWidth = 6; mx.stroke();
        mx.strokeStyle = '#ffd400'; mx.lineWidth = 3; mx.stroke();
      }
    }
    // 지장물 간섭 자리 (빨간 고리)
    if (S.pipes && obShow() && SIM) {
      for (const sp of CF.list) {
        const m = en2m(sp.e, sp.n), x = m[0] * k + ox, y = m[1] * k + oy;
        if (x < -20 || y < -20 || x > w + 20 || y > h + 20) continue;
        mx.beginPath();
        mx.arc(x, y, 9, 0, 2 * Math.PI);
        mx.strokeStyle = 'rgba(8, 10, 14, 0.85)'; mx.lineWidth = 5; mx.stroke();
        mx.strokeStyle = CF_RED; mx.lineWidth = 2.6; mx.stroke();
      }
    }
    // 비행 경로
    if (S.path) {
      for (const p of paths) {
        if (p.length < 2) continue;
        mx.beginPath();
        p.forEach((s, j) => { const x = s.mx * k + ox, y = s.my * k + oy; if (j) mx.lineTo(x, y); else mx.moveTo(x, y); });
        mx.strokeStyle = 'rgba(8, 10, 14, 0.55)';
        mx.lineWidth = 4;
        mx.stroke();
        mx.strokeStyle = 'rgba(255, 255, 255, 0.85)';
        mx.lineWidth = 2;
        mx.stroke();
      }
    }
    // 사진 점
    const showNo = M.z >= 19.5; // 사진 간격 약 5 m: 줌 20 쯤부터 번호가 안 겹친다
    const dot = M.z >= 17 ? 4 : 3;
    mx.font = '600 11px Malgun Gothic, Segoe UI, sans-serif';
    mx.textAlign = 'left';
    mx.textBaseline = 'middle';
    for (const s of shots) {
      if (!Number.isFinite(s.mx) || s.i === S.cur) continue;
      const x = s.mx * k + ox, y = s.my * k + oy;
      if (x < -20 || y < -20 || x > w + 20 || y > h + 20) continue;
      mx.beginPath();
      mx.arc(x, y, dot, 0, 2 * Math.PI);
      mx.fillStyle = '#fff';
      mx.fill();
      mx.strokeStyle = 'rgba(8, 10, 14, 0.8)';
      mx.lineWidth = 1.5;
      mx.stroke();
      if (showNo) {
        mx.lineWidth = 3;
        mx.strokeStyle = 'rgba(8, 10, 14, 0.8)';
        mx.strokeText(String(s.seq), x + 7, y);
        mx.fillStyle = '#fff';
        mx.fillText(String(s.seq), x + 7, y);
      }
    }
    // 지금 사진 + 보는 방향 부채꼴
    const c = shots[S.cur];
    if (c && Number.isFinite(c.mx)) {
      const x = c.mx * k + ox, y = c.my * k + oy;
      const a = mapAngle(S.yaw), half = V.hfov * D2R / 2;
      mx.beginPath();
      mx.moveTo(x, y);
      mx.arc(x, y, 60, a - half, a + half);
      mx.closePath();
      mx.fillStyle = 'rgba(250, 204, 21, 0.28)';
      mx.fill();
      mx.strokeStyle = 'rgba(250, 204, 21, 0.9)';
      mx.lineWidth = 1.5;
      mx.stroke();
      mx.beginPath();
      mx.arc(x, y, 7, 0, 2 * Math.PI);
      mx.fillStyle = '#3b82f6';
      mx.fill();
      mx.strokeStyle = '#facc15';
      mx.lineWidth = 2.5;
      mx.stroke();
      if (showNo) {
        mx.lineWidth = 3;
        mx.strokeStyle = 'rgba(8, 10, 14, 0.85)';
        mx.strokeText(String(c.seq), x + 10, y);
        mx.fillStyle = '#facc15';
        mx.fillText(String(c.seq), x + 10, y);
      }
    }
    if (SIM && S.cur >= 0) drawMapPins();
    // 관로 고치기: 고른 관 + 원래 꼭짓점 네모 손잡이 (사진 점보다 위)
    if (EL && EL.mx) {
      mx.beginPath();
      for (let j = 0; j < EL.mx.length; j++) {
        const x = EL.mx[j] * k + ox, y = EL.my[j] * k + oy;
        if (j) mx.lineTo(x, y); else mx.moveTo(x, y);
      }
      const lw = clamp(EL.dia * 10, 2, 8);
      mx.strokeStyle = 'rgba(8, 10, 14, 0.75)';
      mx.lineWidth = lw + 7;
      mx.stroke();
      mx.strokeStyle = '#ffffff';
      mx.lineWidth = lw + 4;
      mx.stroke();
      mx.strokeStyle = EL.colorHi;
      mx.lineWidth = lw;
      mx.stroke();
      EL.ctrl.forEach((q, i) => {
        const x = EL.mx[q] * k + ox, y = EL.my[q] * k + oy, s = i === ED.hov ? 6 : 4;
        if (x < -20 || y < -20 || x > w + 20 || y > h + 20) return;
        mx.fillStyle = i === ED.hi ? PIN : PIN2;
        mx.fillRect(x - s, y - s, 2 * s, 2 * s);
        mx.strokeStyle = PIN_DARK;
        mx.lineWidth = i === ED.hov ? 2 : 1.5;
        mx.strokeRect(x - s, y - s, 2 * s, 2 * s);
      });
    }
  }
  // 지도: 찍은 점 = 노란 핀 · 사진 점이 지금 맞춤으로 닿는 곳 = 청록 고리
  function drawMapPins() {
    const vid = shots[S.cur].video, on = (e, n) => { const m = en2m(e, n); return m2s(m[0], m[1]); };
    mx.save();
    pairRes(vid).forEach((o, i) => {
      const a = on(o.q.e, o.q.n), b = o.h ? on(o.h[0], o.h[1]) : null;
      if (b) { pinLink(mx, a, b); pinOther(mx, b[0], b[1]); }
      pinMain(mx, a[0], a[1], i + 1);
    });
    const ph = pendHit();
    if (ph) { const b = on(ph[0], ph[1]); pinOther(mx, b[0], b[1]); }
    mx.restore();
  }
  function shotAtMap(x, y) {
    const k = mk(), ox = M.w / 2 - M.cx * k, oy = M.h / 2 - M.cy * k;
    let best = -1, bd = 10;
    for (const s of shots) {
      if (!Number.isFinite(s.mx)) continue;
      const d = Math.hypot(s.mx * k + ox - x, s.my * k + oy - y);
      if (d < bd) { bd = d; best = s.i; }
    }
    return best;
  }
  function fitAll() {
    refreshShotMap();                       // 맞춤 값만큼 옮긴 자리로 맞춘다 (파일에서 읽은 맞춤도)
    const pts = shots.filter((s) => Number.isFinite(s.mx));
    if (!pts.length || M.w < 2) return;
    let x0 = Infinity, x1 = -Infinity, y0 = Infinity, y1 = -Infinity;
    pts.forEach((s) => { x0 = Math.min(x0, s.mx); x1 = Math.max(x1, s.mx); y0 = Math.min(y0, s.my); y1 = Math.max(y1, s.my); });
    const pad = 50;
    const dx = Math.max(x1 - x0, 1e-12), dy = Math.max(y1 - y0, 1e-12);
    const z = Math.log2(Math.min(Math.max(M.w - 2 * pad, 50) / (dx * TILE), Math.max(M.h - 2 * pad, 50) / (dy * TILE)));
    M.z = clamp(z, MAP_ZMIN, 19);
    M.cx = (x0 + x1) / 2;
    M.cy = (y0 + y1) / 2;
    mapDirty();
  }
  function followMap() {
    const s = shots[S.cur];
    if (!s || !Number.isFinite(s.mx) || M.w < 2) return;
    const [x, y] = m2s(s.mx, s.my);
    const mgn = 40;
    if (x < mgn || y < mgn || x > M.w - mgn || y > M.h - mgn) { M.cx = s.mx; M.cy = s.my; }
    mapDirty();
  }
  function zoomAt(px, py, z) {
    const [X, Y] = s2m(px, py);
    M.z = clamp(z, MAP_ZMIN, MAP_ZMAX);
    const k = mk();
    M.cx = X - (px - M.w / 2) / k;
    M.cy = Y - (py - M.h / 2) / k;
    mapDirty();
  }
  let mStart = null;
  gestures(mc, {
    start: (x, y, e) => { mStart = null; edStart('m', x, y, e); },
    end: (moved) => edEnd(moved),
    drag: (dx, dy, ddx, ddy) => {
      if (ED.down) { edMove(dx, dy); return; } // 관로 고치기: 손잡이 · 관 끌기 (지도는 그대로)
      const k = mk(); M.cx -= ddx / k; M.cy -= ddy / k; mapDirty();
    },
    pinchStart: (x, y) => { edEnd(true); mStart = { z: M.z, X: s2m(x, y) }; },
    pinch: (sc, x, y) => {
      if (!mStart) return;
      M.z = clamp(mStart.z + Math.log2(sc), MAP_ZMIN, MAP_ZMAX);
      const k = mk();
      M.cx = mStart.X[0] - (x - M.w / 2) / k;
      M.cy = mStart.X[1] - (y - M.h / 2) / k;
      mapDirty();
    },
    tap: (x, y) => {
      if (ED.on) { edTap('m', x, y); return; } // 관로 고치기 동안은 사진 점 눌러 옮겨 가지 않음
      if (AL.on && AL.pend) { alMap(x, y); return; }
      const i = shotAtMap(x, y);
      if (i >= 0) openShot(i);
      else if (AL.on) toast('① 사진에서 먼저');
      else pipeTap('m', x, y);
    },
    hover: (x, y) => {
      if (ED.on) { edHover('m', x, y); return; }
      mc.classList.toggle('point', !(AL.on && AL.pend) && (shotAtMap(x, y) >= 0 || (!AL.on && pickMode() && lineAt('m', x, y) >= 0)));
    },
  });
  mc.addEventListener('pointerleave', () => { if (ED.on) edHover('m', -1e9, -1e9); });
  mc.addEventListener('dblclick', (e) => {
    if (!ED.on) return;
    const r = mc.getBoundingClientRect();
    edInsert('m', e.clientX - r.left, e.clientY - r.top);
  });
  mc.addEventListener('wheel', (e) => {
    e.preventDefault();
    const r = mc.getBoundingClientRect();
    const d = e.deltaMode === 1 ? e.deltaY * 33 : e.deltaY;
    zoomAt(e.clientX - r.left, e.clientY - r.top, M.z - d * 0.0025);
  }, { passive: false });
  mc.addEventListener('contextmenu', (e) => {
    e.preventDefault();
    const r = mc.getBoundingClientRect();
    const [X, Y] = s2m(e.clientX - r.left, e.clientY - r.top);
    const t = y2lat(Y).toFixed(6) + ', ' + x2lon(X).toFixed(6);
    toast(t + '  · 복사', () => copyText(t));
  });
  $('zIn').addEventListener('click', () => zoomAt(M.w / 2, M.h / 2, Math.round(M.z) + 1));
  $('zOut').addEventListener('click', () => zoomAt(M.w / 2, M.h / 2, Math.round(M.z) - 1));
  $('zFit').addEventListener('click', fitAll);

  // 지도 키 (VWorld)
  const keyBox = $('keyBox'), keyIn = $('keyIn');
  function keyOpen(on) {
    keyBox.classList.toggle('show', on);
    if (on) { keyIn.value = keyGet(); keyIn.focus(); keyIn.select(); }
  }
  function keyApply() {
    const k = keyIn.value.trim();
    keySet(k);
    keyOpen(false);
    setBase(pickBase());
    toast(k ? '지도 키 저장' : '지도 키 지움');
  }
  $('keyBtn').addEventListener('click', () => keyOpen(!keyBox.classList.contains('show')));
  $('keyOk').addEventListener('click', keyApply);
  $('keyClear').addEventListener('click', () => { keyIn.value = ''; keyApply(); });
  keyIn.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { e.preventDefault(); keyApply(); }
    else if (e.key === 'Escape') keyOpen(false);
  });
  document.addEventListener('pointerdown', (e) => {
    if (keyBox.classList.contains('show') && !keyBox.contains(e.target) && e.target !== $('keyBtn')) keyOpen(false);
  });
  updAttrib();
  $('keyBtn').classList.toggle('on', !!BASE.vworld);
  updNames();

  // ---------------------------------------------------------------- 점 찍어 맞추기 (조작)
  // ① 사진에서 점 → ② 지도에서 같은 점 → 짝 하나. 짝이 생기거나 지워질 때마다 그 영상 맞춤을 다시 푼다.
  function alMode(on) {
    if (on && !SIM) { toast('지도 없음'); return; }
    if (on && ED.on) edMode(false); // 점 찍어 맞추기 ↔ 관로 고치기 는 하나만
    AL.on = !!on;
    AL.pend = null;
    ovc.classList.toggle('pick', AL.on);
    mc.classList.toggle('pick', AL.on);
    ovc.classList.remove('point');
    mc.classList.remove('point');
    S.hover = -1;
    alUI();
    dirty();
  }
  // 사진만 찍은 짝이 지금 맞춤으로 땅에 닿는 곳 (지도 표시용)
  function pendHit() {
    const pd = AL.pend, s = pd && shotById.get(pd.shot);
    if (!s) return null;
    const c = calibFor(pd.vid), R = rot64(c);
    const h = rayHit(s, c, R, pd.p, s.gz);
    const z = h ? demAt(h[0], h[1]) : NaN;
    return Number.isFinite(z) ? rayHit(s, c, R, pd.p, z) || h : h;
  }
  function alPhoto(x, y) {
    if (S.cur < 0) return;
    const nx = (x - V.w / 2) / (V.w / 2), ny = (V.h / 2 - y) / (V.h / 2);
    const w = unit3([0, 1, 2].map((i) => V.f[i] + nx * V.tanH * V.r[i] + ny * V.tanV * V.u[i]));
    if (w[2] > -0.02) { toast('땅을 찍기'); return; }
    const R = S.R; // 사진 속 방향 p = Rᵀ·w (지금 맞춤)
    const p = unit3([0, 1, 2].map((i) => R[i] * w[0] + R[3 + i] * w[1] + R[6 + i] * w[2]));
    const s = shots[S.cur];
    AL.pend = { vid: s.video, shot: s.id, p: p };
    const h = pendHit();
    if (h && M.w > 1) { // 지도에서 안 보이면 그쪽으로 옮김 (줌은 그대로)
      const m = en2m(h[0], h[1]), q = m2s(m[0], m[1]);
      if (q[0] < 30 || q[1] < 30 || q[0] > M.w - 30 || q[1] > M.h - 30) { M.cx = m[0]; M.cy = m[1]; }
    }
    alUI();
    dirty();
  }
  function alMap(x, y) {
    const pd = AL.pend;
    if (!pd) return;
    const m = s2m(x, y), en = mer2en(m[0], m[1]);
    const list = pairsOf(pd.vid);
    list.push({ shot: pd.shot, p: pd.p.map((v) => round(v, 7)), e: round(en[0], 3), n: round(en[1], 3) });
    setPairs(pd.vid, list);
    AL.pend = null;
    alRun(pd.vid);
  }
  function alRun(vid) {
    if (alSolve(vid) === 'bad') toast('점 확인');
    calChanged();
    updCalUI();
  }
  function alDelete(vid, i) {
    const list = pairsOf(vid);
    list.splice(i, 1);
    setPairs(vid, list);
    alRun(vid);
  }
  function alSummary(vid) {
    const all = pairRes(vid), ok = all.filter((o) => Number.isFinite(o.r));
    if (!all.length) return '';
    if (!ok.length) return '점 확인 · 점 ' + all.length + '개';
    const rms = Math.sqrt(ok.reduce((a, o) => a + o.r * o.r, 0) / ok.length);
    return '맞춤 오차 ' + rms.toFixed(2) + ' m · 점 ' + all.length + '개';
  }
  function alUI() {
    const vid = curVid(), res = pairRes(vid), sum = alSummary(vid);
    $('alChip').classList.toggle('show', AL.on);
    $('alStep').textContent = AL.pend ? '② 지도에서 같은 점' : '① 사진에서 점';
    $('alSum2').textContent = sum;
    $('alSum').textContent = sum;
    const go = $('alGo');
    go.textContent = AL.on ? '끝' : '점 찍어 맞추기';
    go.classList.toggle('on', AL.on);
    $('viewPane').classList.toggle('pickHere', AL.on && !AL.pend);
    $('mapPane').classList.toggle('pickHere', AL.on && !!AL.pend);
    $('alBox').classList.toggle('on', AL.on); // 켠 동안 목록 줄을 비워 둠 → 첫 짝에 지도가 줄어 움직이지 않게
    const box = $('alList');
    box.textContent = '';
    res.forEach((o, i) => {
      const row = document.createElement('div');
      row.className = 'alRow' + (Number.isFinite(o.r) ? '' : ' bad');
      const t = document.createElement('span');
      t.textContent = (i + 1) + ' · ' + (Number.isFinite(o.r) ? o.r.toFixed(2) + ' m' : '제외');
      const del = document.createElement('button');
      del.type = 'button'; del.className = 'alDel'; del.textContent = '×'; del.title = '지우기';
      del.addEventListener('click', () => alDelete(vid, i));
      row.append(t, del);
      box.appendChild(row);
    });
    if (res.length) box.appendChild(alClear);
  }
  alUI();
  $('alGo').addEventListener('click', () => alMode(!AL.on));
  $('alEnd').addEventListener('click', () => alMode(false));
  alClear.addEventListener('click', () => {
    setPairs(curVid(), []);
    AL.pend = null;
    alUI();
    dirty();
    toast('점 모두 지움');
  });

  // ---------------------------------------------------------------- 프로그램 창: [관로 넣기] · [내보내기]
  const dlgBack = $('dlgBack');
  function dlgOpen() { return !dlgBack.hidden; }
  function dlgShow(id) {
    dlgBack.hidden = false;
    ['pipeDlg', 'obstDlg', 'expDlg'].forEach((k) => { $(k).hidden = k !== id; });
  }
  function dlgClose() { dlgBack.hidden = true; }
  dlgBack.addEventListener('click', (e) => { if (e.target === dlgBack) dlgClose(); });
  dlgBack.querySelectorAll('.dlgX').forEach((b) => b.addEventListener('click', dlgClose));

  // 관로 넣기: 파일 고르기 (pick_pipes) → 항목 고르기 → 넣기 (import_pipes) → data/pipes.js 다시 읽기
  const PM = { pick: null, busy: false };
  function pmMsg(t) { $('pmMsg').textContent = t || ''; }
  function pmBusy(on, text) {
    PM.busy = on;
    $('pmPick').disabled = on;
    $('pmGo').disabled = on || !PM.pick;
    $('pmGo').textContent = on && text ? text : '넣기';
    $('pipeDlg').classList.toggle('busy', on);
  }
  function pmFill(sel, fields, pick) {
    sel.textContent = '';
    const add = (v, t) => { const o = document.createElement('option'); o.value = v; o.textContent = t; sel.appendChild(o); };
    add('', '(없음)');
    fields.forEach((f) => add(f, f));
    sel.value = pick != null && fields.indexOf(String(pick)) >= 0 ? String(pick) : '';
  }
  function pmDiaEx() {
    const p = PM.pick, f = p && (Array.isArray(p.fields) ? p.fields : []).find((q) => q && String(q.name) === $('pmDia').value);
    const s = f && Array.isArray(f.samples) ? f.samples.filter((v) => v !== null && v !== undefined && String(v).trim() !== '').slice(0, 3) : [];
    $('pmDiaEx').textContent = s.length ? '예: ' + s.join(', ') : '';
  }
  function pmShow(fresh) { // fresh = 새로 고른 파일 → 항목 칸을 다시 채움 (창만 다시 열면 고른 값 그대로)
    const p = PM.pick, shp = !!p && p.kind === 'shp', dxf = !!p && p.kind === 'dxf';
    $('pmFile').textContent = p ? String(p.name || p.path) + ' · ' + String(p.kind || '').toUpperCase() : '';
    $('pmShp').hidden = !shp;
    $('pmDxf').hidden = !dxf;
    if (shp && fresh) {
      const fields = (Array.isArray(p.fields) ? p.fields : []).filter((f) => f && f.name != null).map((f) => String(f.name));
      pmFill($('pmDia'), fields, p.guess_dia);
      pmFill($('pmLabel'), fields, null);
      pmDiaEx();
    }
    $('pmNote').textContent = p && p.note ? String(p.note) : '';
    $('pmGo').disabled = !p || PM.busy;
  }
  $('pmDia').addEventListener('change', pmDiaEx);
  $('hPipes').addEventListener('click', () => {
    pmMsg('');
    $('pmDepth').value = String(round(S.cover, 2));
    pmShow();
    dlgShow('pipeDlg');
  });
  $('pmPick').addEventListener('click', () => {
    if (PM.busy) return;
    pmMsg('');
    pmBusy(true);
    hostCall('pick_pipes').then((r) => {
      pmBusy(false);
      if (!r || !r.path) return; // 취소
      const ok = r.kind === 'shp' || r.kind === 'dxf';
      PM.pick = ok ? r : null;
      pmShow(true);
      if (!ok) pmMsg('못 읽음 · ' + String(r.name || r.path) + (r.error ? ' · ' + String(r.error).slice(0, 80) : ''));
    }, (e) => { pmBusy(false); pmMsg(errText(e)); });
  });
  $('pmGo').addEventListener('click', () => {
    const p = PM.pick;
    if (!p || PM.busy) return;
    const dv = $('pmDepth').value, depth = isNum(dv) ? clamp(Number(dv), 0, 20) : S.cover;
    const shp = p.kind === 'shp';
    const arg = {
      path: p.path,
      dia_field: shp ? $('pmDia').value || null : null,
      label_field: shp ? $('pmLabel').value || null : null,
      depth: depth,
      layers: shp ? null : $('pmLayers').value.trim() || null,
    };
    pmMsg('');
    pmBusy(true, '넣는 중');
    pipesFlush().then(() => hostCall('import_pipes', arg)).then((r) => { // 고친 관로를 먼저 써 두고 넣는다
      if (!r || !r.ok) { pmBusy(false); pmMsg((r && r.msg) || '못 넣음'); return null; }
      return reloadPipes().then((n) => {
        pmBusy(false);
        setCover(depth);
        setPipes(true);
        dlgClose();
        toast('관로 ' + (isNum(r.lines) ? Number(r.lines) : n) + '개');
      });
    }).catch((e) => { pmBusy(false); pmMsg(errText(e)); });
  });

  // 내보내기: 웹 주소 (export_web → job 을 0.5 초마다) · 폴더 (export_folder)
  const EX = { job: null, folder: false };
  function exState(st, pct, line, url) {
    $('exProg').hidden = false;
    $('exBar').style.width = clamp(num(pct, 0), 0, 100) + '%';
    $('exLine').textContent = line || '';
    $('exProg').classList.toggle('fail', st === 'fail');
    $('exProg').classList.toggle('done', st === 'done');
    $('exWeb').disabled = st === 'run';
    $('exDone').hidden = st !== 'done' || !url;
    if (st === 'done' && url) $('exUrl').value = String(url);
  }
  function exPoll() {
    setTimeout(() => {
      hostCall('job', EX.job).then((r) => {
        r = r || {};
        if (r.state === 'done') { EX.job = null; exState('done', 100, r.line || '끝', r.url); }
        else if (r.state === 'fail') { EX.job = null; exState('fail', r.pct, r.line || '못 올림'); }
        else { exState('run', r.pct, r.line); exPoll(); }
      }, (e) => { EX.job = null; exState('fail', 0, errText(e)); });
    }, 500);
  }
  $('hExport').addEventListener('click', () => dlgShow('expDlg'));
  $('exWeb').addEventListener('click', () => {
    if (EX.job != null) return;
    $('exDone').hidden = true;
    exState('run', 0, '준비');
    pipesFlush().then(() => hostCall('export_web')).then((r) => {
      if (!r || r.job == null) { exState('fail', 0, (r && r.msg) || '못 올림'); return; }
      EX.job = r.job;
      exPoll();
    }, (e) => exState('fail', 0, errText(e)));
  });
  $('exUrl').addEventListener('focus', () => $('exUrl').select());
  $('exCopy').addEventListener('click', () => {
    const u = $('exUrl').value;
    if (!u) return;
    hostCall('copy_text', u).then((r) => { if (r && r.ok !== false) toast('주소 복사됨'); else copyText(u); }, () => copyText(u));
  });
  $('exFolder').addEventListener('click', () => {
    if (EX.folder) return;
    const b = $('exFolder'), done = () => { EX.folder = false; b.disabled = false; b.textContent = '폴더로 내보내기'; };
    EX.folder = true;
    b.disabled = true;
    b.textContent = '내보내는 중';
    pipesFlush().then(() => hostCall('export_folder')).then((r) => {
      done();
      if (!r) return; // 취소
      const path = String(r.path || '');
      if (r.ok) toast('내보냄 · ' + path, () => hostCall('copy_text', path).then(() => toast('복사됨'), () => copyText(path)));
      else toast(String(r.msg || '못 내보냄'));
    }, (e) => { done(); toast(errText(e)); });
  });

  // ---------------------------------------------------------------- 관로 고치기
  // 원래 꼭짓점 (ctrl) 만 옮긴다. 고칠 때마다 그 줄을 다시 만든다: 꼭짓점 사이 곧은 선을 5 m 이하로 나눈 점 (pts) ·
  // z (hasZ 면 꼭짓점 사이 선형 · 아니면 null) · ctrl · g (groundAt) · ll (격자 → 지도 닮음 변환의 거꾸로).
  // 프로그램 안: 끝난 고침마다 0.8 초 모아 save_pipes (data/pipes.js 와 같은 글) · 밖: 이 브라우저 (localStorage) 에 고친 줄만.
  const ED_STEP = 5;     // m, 꼭짓점 사이 (빌더 densify 와 같음)
  const ED_R = 40, ED_K = 6; // 땅 높이: 사진 땅 높이 거리 가중 (빌더 ground_for 와 같음)
  const PIPE_KEY = 'rv_pipes:' + String(PRJ.title || 'project');
  const shotCell = new Map(); // 40 m 칸 → 사진
  shots.forEach((s) => {
    const k = Math.floor(s.e / ED_R) + ',' + Math.floor(s.n / ED_R);
    if (!shotCell.has(k)) shotCell.set(k, []);
    shotCell.get(k).push(s);
  });
  // 땅 높이: 높이 타일 → 없으면 40 m 안 사진 (가까운 6 개, 무게 1/max(d,1)²) → 없으면 가장 가까운 사진
  function groundAt(e, n) {
    const z = demAt(e, n);
    if (Number.isFinite(z)) return z;
    const cx = Math.floor(e / ED_R), cy = Math.floor(n / ED_R), near = [];
    for (let a = -1; a <= 1; a++) {
      for (let b = -1; b <= 1; b++) {
        for (const s of shotCell.get((cx + a) + ',' + (cy + b)) || []) {
          const d = Math.hypot(s.e - e, s.n - n);
          if (d <= ED_R) near.push([d, s.gz]);
        }
      }
    }
    if (near.length) {
      near.sort((p, q) => p[0] - q[0]);
      let sw = 0, sz = 0;
      near.slice(0, ED_K).forEach(([d, g]) => { const w = 1 / Math.pow(Math.max(d, 1), 2); sw += w; sz += w * g; });
      return sz / sw;
    }
    let bd = Infinity, bz = NaN;
    for (const s of shots) { const d = Math.hypot(s.e - e, s.n - n); if (d < bd) { bd = d; bz = s.gz; } }
    return bz;
  }
  const en2ll = (e, n) => { const m = en2m(e, n); return [round(y2lat(m[1]), 9), round(x2lon(m[0]), 9)]; };
  // 원래 꼭짓점 [[e, n, z|null], …]
  function ctrlPts(l) {
    return ctrlOf(l).map((k) => { const q = l.pts[k]; return [Number(q[0]), Number(q[1]), isNum(q[2]) ? Number(q[2]) : null]; });
  }
  // 원래 꼭짓점 cp 로 줄 l 을 다시 만든다 (빌더 densify · ground_for 와 같은 규칙)
  function edRebuild(l, cp) {
    const hasZ = l.hasZ === true, r3 = (v) => round(v, 3);
    const c = cp.map((q) => [r3(q[0]), r3(q[1]), hasZ && isNum(q[2]) ? r3(Number(q[2])) : null]);
    const pts = [c[0].slice()], ctrl = [0];
    for (let i = 1; i < c.length; i++) {
      const a = c[i - 1], b = c[i], m = Math.max(1, Math.ceil(Math.hypot(b[0] - a[0], b[1] - a[1]) / ED_STEP));
      for (let k = 1; k < m; k++) {
        const t = k / m;
        pts.push([r3(a[0] + t * (b[0] - a[0])), r3(a[1] + t * (b[1] - a[1])), a[2] !== null && b[2] !== null ? r3(a[2] + t * (b[2] - a[2])) : null]);
      }
      pts.push(b.slice());
      ctrl.push(pts.length - 1);
    }
    l.pts = pts;
    l.ctrl = ctrl;
    l.g = pts.map((q) => { const g = groundAt(q[0], q[1]); return Number.isFinite(g) ? round(g, 3) : null; });
    if (SIM) l.ll = pts.map((q) => en2ll(q[0], q[1])); else delete l.ll;
  }
  // 고른 그리기 줄 (관로 고치기 · 없으면 null)
  function edLine() {
    if (!ED.on || ED.sel < 0) return null;
    if (ED.Lof !== lines || ED.Lsel !== ED.sel) { ED.Lof = lines; ED.Lsel = ED.sel; ED.L = lines.find((L) => L.src === ED.sel) || null; }
    return ED.L;
  }
  const edRaw = () => window.RV_PIPES.lines[ED.sel];
  // RV_PIPES.lines[src] 가 바뀜 → 그 줄만 다시 (지도 자리 · 360 띠 · 관 · 구덩이 그물은 고른 것만)
  function edPut(src) {
    const L = buildLine(window.RV_PIPES.lines[src], src);
    if (L) lineToMap(L);
    const nl = lines.slice(), i = nl.findIndex((q) => q.src === src);
    if (i >= 0) { if (L) nl[i] = L; else nl.splice(i, 1); } else if (L) {
      let j = 0;
      while (j < nl.length && nl[j].src < src) j++;
      nl.splice(j, 0, L);
    }
    lines = nl;
    if (src === edSel()) selGen++; else pipeGen++;
    dirty();
  }

  // ---- 화면 ↔ 땅
  function camc(e, n, z) { // 격자 → 360 카메라 좌표 (겹쳐 그리기와 같은 약속)
    const dx = e - S.cam[0], dy = n - S.cam[1], dz = z - S.cam[2];
    return [dx * V.r[0] + dy * V.r[1], dx * V.u[0] + dy * V.u[1] + dz * V.u[2], dx * V.f[0] + dy * V.f[1] + dz * V.f[2]];
  }
  function scr(q) { return [V.w / 2 + q[0] / q[2] * (V.w / 2) / V.tanH, V.h / 2 - q[1] / q[2] * (V.h / 2) / V.tanV]; }
  // 화면 점 → 땅 [e, n, z]: z0 높이 면과 만난 뒤 그 자리 땅 높이로 세 번 더. 땅을 안 보면 null.
  function groundHit(x, y, z0) {
    const nx = (x - V.w / 2) / (V.w / 2), ny = (V.h / 2 - y) / (V.h / 2);
    const w = unit3([0, 1, 2].map((i) => V.f[i] + nx * V.tanH * V.r[i] + ny * V.tanV * V.u[i]));
    if (w[2] > -0.02) return null;
    const C = S.cam;
    let z = Number.isFinite(z0) ? z0 : S.gz, h = null;
    for (let it = 0; it < 4; it++) {
      const t = (z - C[2]) / w[2];
      if (!(t > 0)) return null;
      h = [C[0] + t * w[0], C[1] + t * w[1], z];
      if (it === 3) break;
      const g = groundAt(h[0], h[1]);
      if (!Number.isFinite(g)) break;
      z = g;
    }
    return h;
  }
  // 화면 (x, y) 에서 카메라 좌표 선분 a → b 까지 화면 거리 d · 그 점의 선분 매개 t (가까운 면으로 자름)
  function segPick(a, b, x, y) {
    if (a[2] < NEAR && b[2] < NEAR) return null;
    let ta = 0, tb = 1;
    if (a[2] < NEAR) ta = (NEAR - a[2]) / (b[2] - a[2]); else if (b[2] < NEAR) tb = (NEAR - a[2]) / (b[2] - a[2]);
    const at = (t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
    const qa = at(ta), qb = at(tb), sa = scr(qa), sb = scr(qb);
    const dx = sb[0] - sa[0], dy = sb[1] - sa[1], l2 = dx * dx + dy * dy;
    const s = l2 > 0 ? clamp(((x - sa[0]) * dx + (y - sa[1]) * dy) / l2, 0, 1) : 0;
    const u = s / qb[2] / ((1 - s) / qa[2] + s / qb[2]); // 화면에서는 1/z 가 선형
    return { d: Math.hypot(x - sa[0] - s * dx, y - sa[1] - s * dy), t: ta + (tb - ta) * u };
  }
  // 360: 관 L 에서 화면 (x, y) 에 가장 가까운 곳 { k (나눈 점 마디), t, d (px), de (띠 · 관 가장자리에서 px) }
  function viewPick(L, x, y) {
    const P = L.pts, cnt = P.length / 3, Cx = S.cam[0], Cy = S.cam[1], kx = (V.w / 2) / V.tanH;
    const bx = Math.max(L.e0 - Cx, 0, Cx - L.e1), by = Math.max(L.n0 - Cy, 0, Cy - L.n1);
    if (bx * bx + by * by > FADE1 * FADE1) return null;
    const zf = S.depth ? [gAt, zcAt] : [gAt]; // 깊이 방식은 관 (중심) 도
    let best = null;
    for (let k = 0; k + 1 < cnt; k++) {
      if (distPtSeg(Cx, Cy, P[k * 3], P[k * 3 + 1], P[k * 3 + 3], P[k * 3 + 4]) > FADE1) continue;
      for (const f of zf) {
        const a = camc(P[k * 3], P[k * 3 + 1], f(L, k)), b = camc(P[k * 3 + 3], P[k * 3 + 4], f(L, k + 1));
        const r = segPick(a, b, x, y);
        if (!r) continue;
        const z = a[2] + (b[2] - a[2]) * r.t, de = r.d - (z > NEAR ? L.dia / 2 * kx / z : 0);
        if (!best || de < best.de) best = { k: k, t: r.t, d: r.d, de: de };
      }
    }
    return best;
  }
  // 지도: 관 L 에서 화면 (x, y) 에 가장 가까운 곳
  function mapPick(L, x, y) {
    if (!L.mx) return null;
    const k = mk(), ox = M.w / 2 - M.cx * k, oy = M.h / 2 - M.cy * k;
    let best = null;
    for (let j = 0; j + 1 < L.mx.length; j++) {
      const ax = L.mx[j] * k + ox, ay = L.my[j] * k + oy, dx = L.mx[j + 1] * k + ox - ax, dy = L.my[j + 1] * k + oy - ay, l2 = dx * dx + dy * dy;
      const t = l2 > 0 ? clamp(((x - ax) * dx + (y - ay) * dy) / l2, 0, 1) : 0, d = Math.hypot(x - ax - t * dx, y - ay - t * dy);
      if (!best || d < best.d) best = { k: j, t: t, d: d, de: d - clamp(L.dia * 10, 2, 8) / 2 };
    }
    return best;
  }
  const PICK_DE = 6; // px, 띠 · 선 가장자리에서 이만큼 안이면 그 관
  const pickOf = (view, L, x, y) => (view === 'v' ? viewPick(L, x, y) : mapPick(L, x, y));
  function lineAt(view, x, y) {
    let best = -1, bd = PICK_DE;
    for (const L of lines) {
      const r = pickOf(view, L, x, y);
      if (r && r.de <= bd) { bd = r.de; best = L.src; }
    }
    return best;
  }
  function inPoly(p, x, y) {
    let inside = false;
    for (let a = 0, b = p.length - 2; a < p.length; b = a, a += 2) {
      if ((p[a + 1] > y) !== (p[b + 1] > y) && x < (p[b] - p[a]) * (y - p[a + 1]) / (p[b + 1] - p[a + 1]) + p[a]) inside = !inside;
    }
    return inside;
  }
  // 손잡이 (원래 꼭짓점 차례) · 없으면 −1
  function handleAt(view, x, y) {
    const L = edLine();
    if (!L) return -1;
    let best = -1, bd = Infinity;
    if (view === 'v') {
      for (const hd of S.hnd) {
        const d = Math.hypot(x - hd.sx, y - hd.sy);
        if ((d < 10 || inPoly(hd.poly, x, y)) && d < bd) { bd = d; best = hd.i; }
      }
    } else if (L.mx) {
      const k = mk(), ox = M.w / 2 - M.cx * k, oy = M.h / 2 - M.cy * k;
      L.ctrl.forEach((q, i) => {
        const d = Math.max(Math.abs(L.mx[q] * k + ox - x), Math.abs(L.my[q] * k + oy - y));
        if (d <= 8 && d < bd) { bd = d; best = i; }
      });
    }
    return best;
  }
  // 고른 관 위 점 (나눈 점 마디 k · t) 의 [e, n, z|null, g]
  function pickPoint(L, pk) {
    const P = L.pts, k = pk.k, t = pk.t, za = P[k * 3 + 2], zb = P[k * 3 + 5];
    return [P[k * 3] + (P[k * 3 + 3] - P[k * 3]) * t, P[k * 3 + 1] + (P[k * 3 + 4] - P[k * 3 + 1]) * t,
      L.hasZ && Number.isFinite(za) && Number.isFinite(zb) ? za + (zb - za) * t : null, gAt(L, k) + (gAt(L, k + 1) - gAt(L, k)) * t];
  }

  // ---- 조작
  function edStart(view, x, y, e) {
    ED.down = null;
    if (!ED.on) return;
    if (view === 'v') flushView();
    const L = edLine();
    if (!L) return;
    const hi = handleAt(view, x, y);
    const on = hi < 0 && e && e.shiftKey ? pickOf(view, L, x, y) : null;
    if (hi >= 0) ED.down = { view: view, kind: e && e.shiftKey ? 'move' : 'pt', i: hi, x0: x, y0: y, started: false };
    else if (on && on.de <= PICK_DE) ED.down = { view: view, kind: 'move', i: -1, pk: on, x0: x, y0: y, started: false };
  }
  function edMove(dx, dy) {
    const d = ED.down;
    if (!d) return;
    if (!d.started && !edBegin(d)) { ED.down = null; return; }
    ED.want = [d.x0 + dx, d.y0 + dy];
    needView = true; needMap = true; kick();
  }
  // 끌기 시작: 처음 모양 · 손잡이와 누른 곳의 화면 차이 (손잡이가 마우스로 튀지 않게) · 통째 옮기기의 기준 땅점
  function edBegin(d) {
    const L = edLine(), raw = edRaw();
    if (!L || !raw) return false;
    d.before = JSON.stringify(raw);
    d.cp0 = ctrlPts(raw);
    if (d.cp0.length !== L.ctrl.length) return false;
    if (d.kind === 'pt') {
      const q = L.ctrl[d.i];
      let hs = null;
      if (d.view === 'v') { const c = camc(L.pts[q * 3], L.pts[q * 3 + 1], gAt(L, q)); hs = c[2] > NEAR ? scr(c) : null; } else if (L.mx) hs = m2s(L.mx[q], L.my[q]);
      d.off = hs ? [hs[0] - d.x0, hs[1] - d.y0] : [0, 0];
      ED.hi = d.i;
    } else if (d.view === 'v') {
      const z0 = d.i >= 0 ? gAt(L, L.ctrl[d.i]) : pickPoint(L, d.pk)[3];
      d.ref = groundHit(d.x0, d.y0, z0);
      if (!d.ref) return false;
    } else {
      const m = s2m(d.x0, d.y0);
      d.ref = mer2en(m[0], m[1]);
    }
    d.started = true;
    return true;
  }
  // 다음 그림 전에: 마지막 마우스 자리로 그 줄을 다시 만든다
  function edApply() {
    const d = ED.down, at = ED.want;
    ED.want = null;
    if (!d || !d.started || !at) return;
    const L = edLine(), raw = edRaw();
    if (!L || !raw) return;
    const cp = d.cp0.map((q) => q.slice());
    if (d.kind === 'pt') {
      const tx = at[0] + d.off[0], ty = at[1] + d.off[1];
      let en;
      if (d.view === 'v') en = groundHit(tx, ty, gAt(L, L.ctrl[d.i]));
      else { const m = s2m(tx, ty); en = mer2en(m[0], m[1]); }
      if (!en) return;
      cp[d.i][0] = en[0]; cp[d.i][1] = en[1]; // hasZ 관 중심 표고는 그대로
    } else {
      let en;
      if (d.view === 'v') en = groundHit(at[0], at[1], d.ref[2]);
      else { const m = s2m(at[0], at[1]); en = mer2en(m[0], m[1]); }
      if (!en) return;
      const de = en[0] - d.ref[0], dn = en[1] - d.ref[1];
      cp.forEach((q) => { q[0] += de; q[1] += dn; });
    }
    edRebuild(raw, cp);
    edPut(ED.sel);
  }
  function edEnd(moved) {
    const d = ED.down;
    if (!d) return;
    if (d.started && ED.want) edApply();
    ED.down = null;
    ED.want = null;
    if (!d.started || !moved) return;
    const after = JSON.stringify(edRaw());
    if (after !== d.before) edCommit(ED.sel, d.before, after);
  }
  // 끝난 고침 하나 → 되돌리기 · 저장
  function edCommit(src, before, after) {
    if (!ED.orig.has(src)) ED.orig.set(src, JSON.parse(before)); // [고친 것 버리기] 로 돌아갈 파일 모양
    ED.undo.push({ src: src, before: before, after: after });
    if (ED.undo.length > 300) ED.undo.shift();
    ED.redo = [];
    ED.dirty.add(src);
    pipesSaveSoon();
  }
  function edTap(view, x, y) {
    const hi = handleAt(view, x, y);
    if (hi >= 0) { ED.hi = hi; dirty(); return; }
    const src = lineAt(view, x, y);
    if (src >= 0) {
      if (src !== ED.sel) { ED.sel = src; ED.hi = -1; ED.hov = -1; }
    } else if (ED.hi >= 0) ED.hi = -1; else ED.sel = -1;
    dirty();
  }
  function edHover(view, x, y) {
    const el = view === 'v' ? ovc : mc, hi = handleAt(view, x, y);
    el.classList.toggle('move', hi >= 0);
    el.classList.toggle('point', hi < 0 && lineAt(view, x, y) >= 0);
    if (hi !== ED.hov) { ED.hov = hi; dirty(); }
  }
  // 두 번 누르기: 고른 관의 그 마디에 꼭짓점 넣기
  function edInsert(view, x, y) {
    const L = edLine(), raw = edRaw();
    if (!L || !raw || ED.down || handleAt(view, x, y) >= 0) return;
    const pk = pickOf(view, L, x, y);
    if (!pk || pk.de > PICK_DE) return;
    const p = pickPoint(L, pk), cp = ctrlPts(raw);
    if (cp.length !== L.ctrl.length) return;
    let j = 0;
    while (j + 2 < L.ctrl.length && L.ctrl[j + 1] <= pk.k) j++;
    if (Math.hypot(p[0] - cp[j][0], p[1] - cp[j][1]) < 0.05 || Math.hypot(p[0] - cp[j + 1][0], p[1] - cp[j + 1][1]) < 0.05) return;
    const before = JSON.stringify(raw);
    cp.splice(j + 1, 0, [p[0], p[1], p[2]]);
    edRebuild(raw, cp);
    edPut(ED.sel);
    ED.hi = j + 1;
    edCommit(ED.sel, before, JSON.stringify(raw));
  }
  function edDelete() {
    const raw = ED.sel >= 0 ? edRaw() : null;
    if (!raw || ED.hi < 0) { toast('점 먼저 누르기'); return; }
    const cp = ctrlPts(raw);
    if (cp.length <= 2) { toast('점 2개는 남김'); return; }
    if (ED.hi >= cp.length) return;
    const before = JSON.stringify(raw);
    cp.splice(ED.hi, 1);
    edRebuild(raw, cp);
    edPut(ED.sel);
    ED.hi = -1;
    ED.hov = -1;
    edCommit(ED.sel, before, JSON.stringify(raw));
  }
  function edUndo(redo) {
    const o = (redo ? ED.redo : ED.undo).pop();
    if (!o) { toast(redo ? '다시 할 것 없음' : '되돌릴 것 없음'); return; }
    (redo ? ED.undo : ED.redo).push(o);
    window.RV_PIPES.lines[o.src] = JSON.parse(redo ? o.after : o.before);
    ED.sel = o.src; // 바뀐 관을 보여 줌
    ED.hi = -1;
    ED.hov = -1;
    edPut(o.src);
    ED.dirty.add(o.src);
    pipesSaveSoon();
  }
  function edKey(e) {
    const k = e.key || '', lk = k.toLowerCase(), mod = e.ctrlKey || e.metaKey;
    if (k === 'Escape') { edMode(false); return true; }
    if (k === 'Delete' || k === 'Backspace') { edDelete(); return true; }
    if (mod && (e.code === 'KeyZ' || lk === 'z')) { edUndo(e.shiftKey); return true; }
    if (mod && (e.code === 'KeyY' || lk === 'y')) { edUndo(true); return true; }
    return false;
  }
  function edUI() {
    setOn($('tgEdit'), ED.on);
    $('edChip').classList.toggle('show', ED.on);
    $('app').classList.toggle('edOn', ED.on); // 알림을 안내 줄 위로
    $('edShp').hidden = !HOST();
    $('edDrop').hidden = !!HOST() || !ED.dirty.size;
  }
  function edMode(on) {
    on = !!on;
    if (on && !lines.length) { toast('관로 없음'); return; }
    if (on && AL.on) alMode(false);
    if (ED.down) edEnd(true);
    ED.on = on;
    ED.sel = -1; ED.hi = -1; ED.hov = -1; ED.down = null; ED.want = null;
    if (on && !S.pipes) setPipes(true);
    S.hover = -1;
    [ovc, mc].forEach((el) => el.classList.remove('point', 'move'));
    edUI();
    dirty();
  }
  // 관로 파일을 새로 읽음 ([관로 넣기]): 고치던 것 · 되돌리기 비움
  function edReset() {
    if (ED.on) edMode(false);
    clearTimeout(pipeSaveT);
    pipeSaveT = 0;
    ED.undo = []; ED.redo = []; ED.dirty.clear(); ED.orig.clear();
    ED.origSig = pipesSig(window.RV_PIPES);
    ED.origEdited = !!(window.RV_PIPES && window.RV_PIPES.edited);
    edUI();
  }

  // ---- 저장: 프로그램 = save_pipes (0.8 초 모아) · 밖 = 이 브라우저 (고친 줄만, 파일이 같을 때만 되살림)
  let pipeSaveT = 0, pipeSaving = Promise.resolve(null);
  const pipesText = () => 'window.RV_PIPES = ' + JSON.stringify(window.RV_PIPES) + ';\n';
  function pipesSig(P) { // 파일 관로 지문 (줄 수 · 줄마다 점 수 · 처음 · 끝 점)
    let h = 2166136261;
    const add = (s) => { for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; } };
    const ls = P && Array.isArray(P.lines) ? P.lines : [];
    add(String(ls.length));
    ls.forEach((l) => { const p = l && Array.isArray(l.pts) ? l.pts : []; add('|' + p.length + JSON.stringify(p[0] || null) + JSON.stringify(p[p.length - 1] || null)); });
    return h.toString(16);
  }
  function pipesSaveSoon() {
    window.RV_PIPES.edited = true;
    if (HOST()) {
      clearTimeout(pipeSaveT);
      pipeSaveT = setTimeout(pipesFlush, 800);
    } else {
      const out = {};
      ED.dirty.forEach((i) => { out[i] = window.RV_PIPES.lines[i]; });
      try { localStorage.setItem(PIPE_KEY, JSON.stringify({ sig: ED.origSig, lines: out })); } catch (e) { toast('이 브라우저에 저장 못 함'); }
    }
    edUI();
  }
  function pipesFlush() {
    if (pipeSaveT) {
      clearTimeout(pipeSaveT);
      pipeSaveT = 0;
      pipeSaving = pipeSaving.then(() => hostCall('save_pipes', pipesText())).then((r) => {
        if (!r || r.ok === false) toast('관로 저장 안 됨');
        return r;
      }, () => { toast('관로 저장 안 됨'); return null; });
    }
    return pipeSaving;
  }
  // [고친 것 버리기]: 파일 관로로 (이 브라우저 것 지움)
  function edDiscard() {
    const P = window.RV_PIPES;
    if (P && Array.isArray(P.lines)) {
      ED.orig.forEach((l, i) => { P.lines[i] = l; });
      if (ED.origEdited) P.edited = true; else delete P.edited;
    }
    ED.orig.clear(); ED.dirty.clear(); ED.undo = []; ED.redo = [];
    try { localStorage.removeItem(PIPE_KEY); } catch (e) { /* 없어도 됨 */ }
    ED.sel = -1; ED.hi = -1; ED.hov = -1;
    lines = buildLines(P);
    linesToMap();
    pipeGen++;
    edUI();
    dirty();
    toast('고친 것 버림');
  }
  // SHP 로 내보내기 (프로그램만): 아직 안 쓴 고침을 먼저 저장 → export_pipes_shp
  function edShp() {
    if (!HOST() || ED.shpBusy) return;
    const bs = [$('edShp'), $('pmShpOut')];
    const busy = (on) => { ED.shpBusy = on; bs.forEach((b) => { b.disabled = on; }); };
    busy(true);
    pipesFlush().then(() => hostCall('export_pipes_shp')).then((r) => {
      busy(false);
      if (!r) return; // 취소
      const p = String(r.path || '');
      if (r.ok) toast('SHP 저장 · ' + p, () => hostCall('copy_text', p).then(() => toast('복사됨'), () => copyText(p)));
      else toast(String(r.msg || 'SHP 못 씀'));
    }, (e) => { busy(false); toast(errText(e)); });
  }
  $('tgEdit').addEventListener('click', () => edMode(!ED.on));
  $('edEnd').addEventListener('click', () => edMode(false));
  $('edShp').addEventListener('click', edShp);
  $('pmShpOut').addEventListener('click', edShp);
  $('edDrop').addEventListener('click', () => toast('고친 관로 모두 버리기', edDiscard, '버리기'));
  const homeCal = beforeHome;
  beforeHome = () => Promise.all([homeCal(), pipesFlush()]); // [처음] 전에 안 쓴 고침도 저장
  window.addEventListener('pagehide', () => { if (pipeSaveT) pipesFlush(); });
  window.addEventListener('pywebviewready', edUI);
  // 시작: 이 브라우저에 고친 관로가 있으면 (같은 파일일 때만) 되살림
  ED.origSig = pipesSig(window.RV_PIPES);
  ED.origEdited = !!(window.RV_PIPES && window.RV_PIPES.edited);
  (function restore() {
    const st = lsGet(PIPE_KEY), P = window.RV_PIPES;
    // 다른 pipes.js (이름만 같은 프로젝트 · 다시 넣은 관로) 면 쓰지 않음 (지우지도 않음)
    if (!st || HOST() || !P || !Array.isArray(P.lines) || st.sig !== ED.origSig || !st.lines || typeof st.lines !== 'object') return;
    let n = 0;
    Object.keys(st.lines).forEach((k) => {
      const i = Number(k), l = st.lines[k];
      if (!Number.isInteger(i) || i < 0 || i >= P.lines.length || !l || !Array.isArray(l.pts)) return;
      ED.orig.set(i, P.lines[i]);
      P.lines[i] = l;
      ED.dirty.add(i);
      n++;
    });
    if (!n) return;
    P.edited = true;
    lines = buildLines(P);
    linesToMap();
    toast('고친 관로 불러옴', edDiscard, '버리기');
  })();
  edUI();

  // ---------------------------------------------------------------- 지장물 (data/obstacles.js, 없어도 됨 · 고치지 않음 · 터파기 없음)
  // RV_OBST = { layers: [{ name, color }], lines: [{ layer, kind, label, dia (m | null), depth (m | null = 지표 → 윗면), pts: [[e, n, null]], g, ll }], defaultDepth }
  // 관 중심 = g − (depth ?? defaultDepth ?? 1.0) − (dia ?? 0.15)/2 · 노면 띠 폭 = max(dia, 0.15 m).
  // 줄 꼴: pts = [e, n, 관 중심 표고] · g = 지표 (없으면 groundAt) · dia = 그리는 관경 · hw = 띠 반폭 · text = 이름표.
  let obst = [], obGen = 0, obOn = lsGet('rv_ui:obst') !== false;
  const obShow = () => obOn && obst.length > 0;
  const obZ = (L, k) => L.pts[k * 3 + 2];
  function buildObst(O) {
    O = O || {};
    const lays = (Array.isArray(O.layers) ? O.layers : [])
      .map((l) => ({ name: String((l && l.name) || ''), color: String((l && l.color) || '#9aa3b2') }));
    const dd = isNum(O.defaultDepth) && Number(O.defaultDepth) >= 0 ? Number(O.defaultDepth) : 1.0;
    const out = [];
    (Array.isArray(O.lines) ? O.lines : []).forEach((l) => {
      if (!l || !Array.isArray(l.pts)) return;
      const lay = lays[num(l.layer, 0) | 0] || { name: '', color: '#9aa3b2' };
      const diaK = isNum(l.dia) && Number(l.dia) > 0, dia = diaK ? Number(l.dia) : OB_DIA;
      const dep = isNum(l.depth) && Number(l.depth) >= 0 ? Number(l.depth) : dd;
      const gIn = Array.isArray(l.g) && l.g.length === l.pts.length ? l.g : null;
      const llIn = Array.isArray(l.ll) && l.ll.length === l.pts.length ? l.ll : null;
      const p = [], g = [], ll = [];
      let llOk = !!llIn;
      l.pts.forEach((q, k) => {
        if (!ptOk(q)) return;
        const e = Number(q[0]), n = Number(q[1]), gk = gIn && isNum(gIn[k]) ? Number(gIn[k]) : groundAt(e, n);
        p.push(e, n, gk - dep - dia / 2);
        g.push(gk);
        const a = llIn && llIn[k];
        if (Array.isArray(a) && isNum(a[0]) && isNum(a[1])) ll.push(a); else llOk = false;
      });
      if (p.length < 6) return;
      let e0 = Infinity, e1 = -Infinity, n0 = Infinity, n1 = -Infinity;
      for (let k = 0; k < p.length; k += 3) {
        e0 = Math.min(e0, p[k]); e1 = Math.max(e1, p[k]);
        n0 = Math.min(n0, p[k + 1]); n1 = Math.max(n1, p[k + 1]);
      }
      const bi = [0]; // 평면에서 겹치지 않는 꼭짓점 번호 (노면 띠)
      for (let k = 3; k < p.length; k += 3) {
        const j = bi[bi.length - 1] * 3;
        if (Math.hypot(p[k] - p[j], p[k + 1] - p[j + 1]) > 1e-6) bi.push(k / 3);
      }
      const rgb = hexRgb(lay.color), kind = String(l.kind || l.label || lay.name || '지장물');
      out.push({
        color: lay.color, rgb: rgb, dark: 'rgb(' + rgb.map((v) => Math.round(v * 255 * 0.45)).join(',') + ')',
        kind: kind, text: diaK ? kind + ' · D' + Math.round(dia * 1000) : kind, dia: dia, hw: Math.max(dia, OB_DIA) / 2,
        pts: Float64Array.from(p), g: Float64Array.from(g), bi: bi, ll: llOk ? ll : null, e0, e1, n0, n1, mx: null, my: null,
      });
    });
    return out;
  }
  function obSet(O) {
    obst = buildObst(O);
    obst.forEach(lineToMap);
    obGen++; // 관 그물 · 간섭 다시
    $('tgObst').hidden = !obst.length;
    setOn($('tgObst'), obOn);
    dirty();
  }
  obSet(window.RV_OBST);
  $('tgObst').addEventListener('click', () => {
    obOn = !obOn;
    lsSet('rv_ui:obst', obOn);
    setOn($('tgObst'), obOn);
    dirty();
  });
  // data/obstacles.js 다시 읽기 ([지장물 넣기] 뒤 · 캐시 피하려 ?v=). 못 읽으면 옛 자료 그대로.
  function reloadObst() {
    const old = window.RV_OBST;
    return new Promise((resolve, reject) => {
      window.RV_OBST = undefined;
      const sc = document.createElement('script');
      sc.onload = () => { sc.remove(); resolve(); };
      sc.onerror = () => { sc.remove(); window.RV_OBST = old; reject(new Error('지장물 파일 못 읽음')); };
      sc.src = 'data/obstacles.js?v=' + Date.now();
      document.head.appendChild(sc);
    }).then(() => { obSet(window.RV_OBST); return obst.length; });
  }

  // ---- 지장물 넣기 (프로그램만): 파일 고르기 (pick_obstacles) → 항목 고르기 → 넣기 (import_obstacles) → data/obstacles.js 다시 읽기
  const OM = { pick: null, busy: false };
  function omMsg(t) { $('omMsg').textContent = t || ''; }
  function omBusy(on, text) {
    OM.busy = on;
    $('omPick').disabled = on;
    $('omGo').disabled = on || !OM.pick;
    $('omGo').textContent = on && text ? text : '넣기';
    $('obstDlg').classList.toggle('busy', on);
  }
  function omShow(fresh) { // fresh = 새로 고른 파일 → 항목 칸을 다시 채움 (프로그램이 짐작한 항목을 미리 고름)
    const p = OM.pick;
    $('omFile').textContent = p ? String(p.name || p.path) + ' · SHP' : '';
    $('omShp').hidden = !p;
    if (p && fresh) {
      const fields = (Array.isArray(p.fields) ? p.fields : []).filter((f) => f && f.name != null).map((f) => String(f.name));
      pmFill($('omType'), fields, p.guess_type);
      pmFill($('omDia'), fields, p.guess_dia); // 관경처럼 보이는 항목이 없으면 (없음)
      pmFill($('omDep'), fields, p.guess_depth);
      pmFill($('omLabel'), fields, p.guess_label);
    }
    $('omNote').textContent = p && p.note ? String(p.note) : '';
    $('omGo').disabled = !p || OM.busy;
  }
  $('hObst').addEventListener('click', () => {
    omMsg('');
    const O = window.RV_OBST;
    $('omDepth').value = String(round(O && isNum(O.defaultDepth) ? Number(O.defaultDepth) : 1.0, 2));
    omShow();
    dlgShow('obstDlg');
  });
  $('omPick').addEventListener('click', () => {
    if (OM.busy) return;
    omMsg('');
    omBusy(true);
    hostCall('pick_obstacles').then((r) => {
      omBusy(false);
      if (!r || (!r.path && !r.error)) return; // 취소
      const ok = r.kind === 'shp' && !r.error;
      OM.pick = ok ? r : null;
      omShow(true);
      if (!ok) omMsg('못 읽음 · ' + String(r.name || r.path || '') + (r.error ? ' · ' + String(r.error).slice(0, 80) : ''));
    }, (e) => { omBusy(false); omMsg(errText(e)); });
  });
  $('omGo').addEventListener('click', () => {
    const p = OM.pick;
    if (!p || OM.busy) return;
    const dv = $('omDepth').value;
    const arg = {
      path: p.path,
      type_field: $('omType').value || null,
      dia_field: $('omDia').value || null,
      depth_field: $('omDep').value || null,
      label_field: $('omLabel').value || null,
      default_depth: isNum(dv) ? clamp(Number(dv), 0, 20) : 1.0,
    };
    omMsg('');
    omBusy(true, '넣는 중');
    hostCall('import_obstacles', arg).then((r) => {
      if (!r || !r.ok) { omBusy(false); omMsg((r && r.msg) || '못 넣음'); return null; }
      return reloadObst().then((n) => {
        omBusy(false);
        obOn = true;
        lsSet('rv_ui:obst', true);
        setOn($('tgObst'), true);
        dlgClose();
        toast('지장물 ' + (isNum(r.lines) ? Number(r.lines) : n) + '개');
      });
    }).catch((e) => { omBusy(false); omMsg(errText(e)); });
  });

  // ---------------------------------------------------------------- 수량 (보기만 · 그때그때)
  // 고른 관 (관로 고치기) · 없으면 화면 가운데 땅에서 가장 가까운 관 + 모든 관 합. 단면은 터파기 구덩이와 같은 식 (trSecGet):
  // 연장 = 평면 길이 · 터파기 = 단면적 (포장 직각 2·wv·tp + 기울기 사다리꼴 wb ~ wv · 포장이 바닥보다 깊으면 2·wb·깊이) 을
  // 꼭짓점 사이 심프슨으로 적분 · 포장 깨기 = ∫ 2·wv · 모래 기초 = 2·wb·bed · 연장 · 되메우기 = 터파기 − 관 부피 − 모래.
  // 땅 높이는 사진과 무관하게 (g, 없으면 groundAt) · 관 끝 더 판 곳 · 꺾인 곳 겹침은 넣지 않음.
  const QT = { on: false, memo: new WeakMap(), row: {}, txt: new Map(), cfv: -1 };
  // 지장물 간섭 상태 (아래 «지장물 간섭»): gap = 이격 기준 m (이 브라우저에 기억) · list = 간섭 자리
  const CF_KEY = 'rv_gap:' + String(PRJ.title || 'project'), CF_CELL = 10, CF_STEP = 0.5;
  const CF = { gap: 0.3, list: [], of: null, key: '', t: 0, ver: 0, memo: new WeakMap(), grid: null, gridOf: -1 };
  if (isNum(lsGet(CF_KEY))) CF.gap = clamp(Number(lsGet(CF_KEY)), 0, 5);
  const Q_ROWS = [['len', '연장', 'm'], ['cut', '터파기', 'm³'], ['pav', '포장 깨기', 'm²'], ['sand', '모래 기초', 'm³'], ['fill', '되메우기', 'm³']];
  function gQ(L, k) { const g = L.g[k]; return Number.isFinite(g) ? g : groundAt(L.pts[k * 3], L.pts[k * 3 + 1]); }
  function zcQ(L, k) { const z = L.pts[k * 3 + 2]; return L.hasZ && Number.isFinite(z) ? z : gQ(L, k) - S.cover - L.dia / 2; }
  function secQ(D, g, zc) { // → [단면적 m², 지표 반폭 wv]
    const zs = Math.min(zc - D / 2, g - 0.01), zb = zs - TR.bed, wb = D / 2 + TR.margin, zt = g - TR.tp;
    const wv = wb + TR.n * Math.max(0, zt - zb);
    return [2 * wv * (g - Math.max(zt, zb)) + (wb + wv) * Math.max(0, zt - zb), wv];
  }
  function lineQty(L) {
    const key = [TR.tp, TR.n, TR.margin, TR.bed, S.cover].join('|');
    const m = QT.memo.get(L);
    if (m && m.key === key) return m.q;
    const P = L.pts, D = L.dia, cnt = P.length / 3;
    let len = 0, cut = 0, pav = 0, g0 = gQ(L, 0), z0 = zcQ(L, 0), s0 = secQ(D, g0, z0);
    for (let k = 1; k < cnt; k++) {
      const g1 = gQ(L, k), z1 = zcQ(L, k), s1 = secQ(D, g1, z1);
      const l = Math.hypot(P[k * 3] - P[k * 3 - 3], P[k * 3 + 1] - P[k * 3 - 2]);
      if (l > 0) {
        const sm = secQ(D, (g0 + g1) / 2, (z0 + z1) / 2);
        len += l;
        cut += l / 6 * (s0[0] + 4 * sm[0] + s1[0]);
        pav += l / 3 * (s0[1] + 4 * sm[1] + s1[1]);
      }
      g0 = g1; z0 = z1; s0 = s1;
    }
    const sand = 2 * (D / 2 + TR.margin) * TR.bed * len, pipe = Math.PI * D * D / 4 * len;
    const q = { len: len, cut: cut, pav: pav, sand: sand, fill: cut - pipe - sand };
    QT.memo.set(L, { key: key, q: q });
    return q;
  }
  function qtyLine() { // 고른 관 · 없으면 화면 가운데가 닿는 땅 (하늘이면 사진 자리) 에서 가장 가까운 관
    const EL = edLine();
    if (EL) return EL;
    if (S.cur < 0) return null;
    const c = groundHit(V.w / 2, V.h / 2, S.gz) || S.cam;
    let best = null, bd = Infinity;
    for (const L of lines) {
      if (Math.max(L.e0 - c[0], 0, c[0] - L.e1) > bd || Math.max(L.n0 - c[1], 0, c[1] - L.n1) > bd) continue;
      const P = L.pts;
      for (let k = 0; k + 3 < P.length; k += 3) {
        const d = distPtSeg(c[0], c[1], P[k], P[k + 1], P[k + 3], P[k + 4]);
        if (d < bd) { bd = d; best = L; }
      }
    }
    return best;
  }
  function qTxt(el, t) { if (QT.txt.get(el) !== t) { QT.txt.set(el, t); el.textContent = t; } }
  const qFmt = (v) => (Number.isFinite(v) ? v.toLocaleString('ko-KR', { minimumFractionDigits: 1, maximumFractionDigits: 1 }) : '-');
  function qtyUI() {
    const L = qtyLine(), one = L ? lineQty(L) : null, tot = { len: 0, cut: 0, pav: 0, sand: 0, fill: 0 };
    lines.forEach((M) => { const q = lineQty(M); Q_ROWS.forEach(([k]) => { tot[k] += q[k]; }); });
    qTxt($('qLine'), L ? L.label || 'D' + Math.round(L.dia * 1000) : '관로 없음');
    Q_ROWS.forEach(([k]) => {
      const [a, b] = QT.row[k], va = String(one ? one[k] : NaN), vb = String(lines.length ? tot[k] : NaN);
      if (a.dataset.v !== va) { a.dataset.v = va; a.textContent = qFmt(Number(va)); } // data-v = 반올림 전 값
      if (b.dataset.v !== vb) { b.dataset.v = vb; b.textContent = qFmt(Number(vb)); }
    });
    qTxt($('qNote'), ['tp', 'n', 'margin', 'bed'].every((k) => TR[k] === TR_DEF[k]) ? '가시화 기본값 기준' : '');
    $('qClr').hidden = !obst.length;
    $('qList').hidden = !obst.length;
    if (QT.cfv === CF.ver) return;
    QT.cfv = CF.ver;
    const n = CF.list.length, hit = $('qHit'), box = $('qList');
    hit.textContent = n ? '간섭 ' + n + '곳' : '간섭 없음';
    hit.classList.toggle('bad', n > 0);
    box.textContent = '';
    CF.list.forEach((sp, i) => {
      const b = document.createElement('button');
      b.type = 'button';
      b.textContent = (i + 1) + ' · ' + sp.p + ' ↔ ' + sp.o + ' · ' + sp.c.toFixed(2) + ' m';
      b.title = '그쪽 보기';
      b.dataset.at = [sp.e, sp.n, sp.c].map((v) => round(v, 4)).join(','); // 자리 · 이격 (반올림 전)
      b.addEventListener('click', () => cfGo(sp));
      box.appendChild(b);
    });
  }
  (function buildQty() {
    const tab = $('qTab');
    ['', '이 관', '전체'].forEach((t) => { const s = document.createElement('span'); s.className = 'qh'; s.textContent = t; tab.appendChild(s); });
    Q_ROWS.forEach(([k, name, unit]) => {
      const lab = document.createElement('span');
      lab.className = 'ql'; lab.textContent = name + ' ' + unit;
      const a = document.createElement('span'), b = document.createElement('span');
      a.className = 'qv'; b.className = 'qv'; a.id = 'q_' + k; b.id = 'qt_' + k;
      tab.append(lab, a, b);
      QT.row[k] = [a, b];
    });
    const qShow = (on) => {
      QT.on = !!on;
      $('qtyBox').hidden = !QT.on;
      setOn($('tgQty'), QT.on);
      QT.cfv = -1;
      dirty();
    };
    $('tgQty').addEventListener('click', () => qShow(!QT.on));
    $('qX').addEventListener('click', () => qShow(false));
    const gap = $('qGap');
    gap.value = String(CF.gap);
    gap.addEventListener('input', () => {
      if (!isNum(gap.value)) return;
      CF.gap = clamp(Number(gap.value), 0, 5);
      lsSet(CF_KEY, CF.gap);
      dirty();
    });
    gap.addEventListener('change', () => { gap.value = String(round(CF.gap, 3)); });
  })();

  // ---------------------------------------------------------------- 지장물 간섭
  // 관마다 0.5 m 조각 (3D 선분 · 관 중심 = zcQ) ↔ 지장물 선분 (10 m 칸 격자): 두 선분의 가장 가까운 거리 − 두 반지름 = 이격.
  // 이격 < 기준인 조각이 이어지는 구간마다 한 곳 (가장 가까운 조각 · 같은 거리면 구간 가운데). 자리 = 두 가장 가까운 점의 평면 가운데 땅.
  // 관이 바뀌면 (관로 고치기 · 되돌리기 · 넣기) 그 관만 다시 · 0.2 초 모아 (끄는 동안은 0.2 초마다).
  const SS = [0, 0, 0, 0, 0, 0]; // 가장 가까운 두 점 (관 쪽 · 지장물 쪽)
  // 3D 선분 a→b · c→d 의 가장 가까운 거리 (Ericson, Real-Time Collision Detection 5.1.9)
  function segSeg(ax, ay, az, bx, by, bz, cx, cy, cz, dx, dy, dz) {
    const ux = bx - ax, uy = by - ay, uz = bz - az, vx = dx - cx, vy = dy - cy, vz = dz - cz, wx = ax - cx, wy = ay - cy, wz = az - cz;
    const a = ux * ux + uy * uy + uz * uz, e = vx * vx + vy * vy + vz * vz, f = vx * wx + vy * wy + vz * wz;
    let s = 0, t = 0;
    if (a < 1e-12 && e >= 1e-12) t = clamp(f / e, 0, 1);
    else if (a >= 1e-12) {
      const c = ux * wx + uy * wy + uz * wz;
      if (e < 1e-12) s = clamp(-c / a, 0, 1);
      else {
        const b = ux * vx + uy * vy + uz * vz, den = a * e - b * b;
        s = den > 1e-12 * a * e ? clamp((b * f - c * e) / den, 0, 1) : 0.5; // 나란하면 조각 가운데
        t = (b * s + f) / e;
        if (t < 0) { t = 0; s = clamp(-c / a, 0, 1); } else if (t > 1) { t = 1; s = clamp((b - c) / a, 0, 1); }
      }
    }
    SS[0] = ax + ux * s; SS[1] = ay + uy * s; SS[2] = az + uz * s;
    SS[3] = cx + vx * t; SS[4] = cy + vy * t; SS[5] = cz + vz * t;
    return Math.hypot(SS[0] - SS[3], SS[1] - SS[4], SS[2] - SS[5]);
  }
  function cfGrid() { // 지장물 선분 → 10 m 칸
    if (CF.grid && CF.gridOf === obGen) return CF.grid;
    const cells = new Map(), segs = [];
    let rmax = 0;
    obst.forEach((O, oi) => {
      const P = O.pts, r = O.dia / 2;
      rmax = Math.max(rmax, r);
      for (let k = 0; k + 3 < P.length; k += 3) {
        const id = segs.push({ oi: oi, r: r, p: P, k: k }) - 1;
        const x0 = Math.floor(Math.min(P[k], P[k + 3]) / CF_CELL), x1 = Math.floor(Math.max(P[k], P[k + 3]) / CF_CELL);
        const y0 = Math.floor(Math.min(P[k + 1], P[k + 4]) / CF_CELL), y1 = Math.floor(Math.max(P[k + 1], P[k + 4]) / CF_CELL);
        for (let x = x0; x <= x1; x++) {
          for (let y = y0; y <= y1; y++) {
            const c = x + ',' + y;
            if (!cells.has(c)) cells.set(c, []);
            cells.get(c).push(id);
          }
        }
      }
    });
    CF.grid = { cells: cells, segs: segs, rmax: rmax };
    CF.gridOf = obGen;
    return CF.grid;
  }
  function cfLine(L) { // 관 L 의 간섭 자리들
    const G = cfGrid(), out = [];
    if (!G.segs.length) return out;
    const P = L.pts, cnt = P.length / 3, rp = L.dia / 2, reach = CF.gap + rp + G.rmax;
    const stamp = new Int32Array(G.segs.length).fill(-1), runs = new Map(); // 지장물 줄 → 이어지는 구간 { last, it }
    const flush = (oi) => {
      const r = runs.get(oi);
      runs.delete(oi);
      let mn = Infinity;
      r.it.forEach((b) => { mn = Math.min(mn, b.c); });
      const near = r.it.filter((b) => b.c <= mn + 0.01), a = near[(near.length - 1) >> 1], b = near[near.length >> 1]; // 가운데 (짝수면 둘의 가운데)
      const e = (a.e + b.e) / 2, n = (a.n + b.n) / 2;
      out.push({ src: L.src, oi: oi, c: mn, e: e, n: n, g: groundAt(e, n), p: L.label || 'D' + Math.round(L.dia * 1000), o: obst[oi].text });
    };
    let pi = 0;
    for (let k = 0; k + 1 < cnt; k++) {
      const ax = P[k * 3], ay = P[k * 3 + 1], az = zcQ(L, k), bx = P[k * 3 + 3], by = P[k * 3 + 4], bz = zcQ(L, k + 1);
      const np = Math.max(1, Math.ceil(Math.hypot(bx - ax, by - ay) / CF_STEP));
      for (let q = 0; q < np; q++, pi++) {
        const t0 = q / np, t1 = (q + 1) / np;
        const x0 = ax + (bx - ax) * t0, y0 = ay + (by - ay) * t0, z0 = az + (bz - az) * t0;
        const x1 = ax + (bx - ax) * t1, y1 = ay + (by - ay) * t1, z1 = az + (bz - az) * t1;
        const best = new Map();
        const cxa = Math.floor((Math.min(x0, x1) - reach) / CF_CELL), cxb = Math.floor((Math.max(x0, x1) + reach) / CF_CELL);
        const cya = Math.floor((Math.min(y0, y1) - reach) / CF_CELL), cyb = Math.floor((Math.max(y0, y1) + reach) / CF_CELL);
        for (let x = cxa; x <= cxb; x++) {
          for (let y = cya; y <= cyb; y++) {
            for (const id of G.cells.get(x + ',' + y) || []) {
              if (stamp[id] === pi) continue;
              stamp[id] = pi;
              const s = G.segs[id], Q = s.p, j = s.k;
              const c = segSeg(x0, y0, z0, x1, y1, z1, Q[j], Q[j + 1], Q[j + 2], Q[j + 3], Q[j + 4], Q[j + 5]) - rp - s.r;
              if (c >= CF.gap) continue;
              const o = best.get(s.oi);
              if (!o || c < o.c) best.set(s.oi, { c: c, e: (SS[0] + SS[3]) / 2, n: (SS[1] + SS[4]) / 2 });
            }
          }
        }
        best.forEach((b, oi) => {
          let r = runs.get(oi);
          if (!r) { r = { last: pi, it: [] }; runs.set(oi, r); }
          r.it.push(b);
          r.last = pi;
        });
        [...runs.keys()].forEach((oi) => { if (runs.get(oi).last !== pi) flush(oi); });
      }
    }
    [...runs.keys()].forEach(flush);
    return out;
  }
  const cfKey = () => S.cover + '|' + CF.gap + '|' + obGen;
  function cfCheck() {
    if (CF.of === lines && CF.key === cfKey()) return;
    if (!CF.t) CF.t = setTimeout(cfRun, 200);
  }
  function cfRun() {
    CF.t = 0;
    const key = cfKey(), out = [];
    CF.of = lines;
    CF.key = key;
    if (obst.length) {
      for (const L of lines) {
        let m = CF.memo.get(L);
        if (!m || m.key !== key) { m = { key: key, s: cfLine(L) }; CF.memo.set(L, m); }
        out.push(...m.s);
      }
    }
    CF.list = out;
    CF.ver++;
    dirty();
  }
  function lookAt(p) { // 지금 사진에서 (e, n, z) 쪽으로 돌림
    const de = p[0] - S.cam[0], dn = p[1] - S.cam[1], dz = p[2] - S.cam[2];
    S.yaw = ((Math.atan2(de, dn) * R2D) % 360 + 540) % 360 - 180;
    S.pitch = clamp(Math.atan2(dz, Math.hypot(de, dn)) * R2D, -89, 89);
    dirty();
  }
  // 간섭 목록에서 누름: 그쪽을 봄 (30 m 보다 멀면 가장 가까운 사진으로 옮긴 뒤) · 지도도 그 자리로
  function cfGo(sp) {
    const p = [sp.e, sp.n, sp.g];
    if (SIM) { const m = en2m(sp.e, sp.n); M.cx = m[0]; M.cy = m[1]; mapDirty(); }
    let best = -1, bd = Infinity;
    for (const s of shots) {
      const c = calibFor(s.video), d = Math.hypot(s.e + c.dx - sp.e, s.n + c.dy - sp.n);
      if (d < bd) { bd = d; best = s.i; }
    }
    const cur = S.want >= 0 ? S.want : S.cur;
    if (best >= 0 && best !== cur && Math.hypot(S.cam[0] - sp.e, S.cam[1] - sp.n) > 30) { S.look = p; openShot(best); return; }
    lookAt(p);
  }

  // ---------------------------------------------------------------- 크기 맞춤
  function resizeAll() {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const vr = ovc.getBoundingClientRect(); // 칸 테두리 안쪽 = 캔버스 실제 크기
    const W = Math.max(1, Math.round(vr.width * dpr)), H = Math.max(1, Math.round(vr.height * dpr));
    if (glc.width !== W || glc.height !== H) { glc.width = W; glc.height = H; ovc.width = W; ovc.height = H; }
    V.W = W; V.H = H; V.dpr = dpr; V.w = W / dpr; V.h = H / dpr;
    const mr = mc.getBoundingClientRect();
    const mw = Math.round(mr.width * dpr), mh = Math.round(mr.height * dpr);
    if (mw > 0 && mh > 0) {
      if (mc.width !== mw || mc.height !== mh) { mc.width = mw; mc.height = mh; }
      M.w = mw / dpr; M.h = mh / dpr; M.dpr = dpr;
      if (!M.fitted) { M.fitted = true; fitAll(); }
    } else { M.w = 0; M.h = 0; }
    dirty();
  }
  if (window.ResizeObserver) {
    const ro = new ResizeObserver(resizeAll);
    ro.observe($('viewPane'));
    ro.observe($('mapPane'));
  }
  window.addEventListener('resize', resizeAll);

  // ---------------------------------------------------------------- 시작
  initGL();
  buildCal();
  hostShow();
  resizeAll();
  openShot(startIdx);
})();
