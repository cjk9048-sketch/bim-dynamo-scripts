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
  function toast(text, onClick) {
    const t = $('toast');
    t.textContent = text;
    t.onclick = onClick || null;
    t.classList.toggle('click', !!onClick);
    t.classList.add('show');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => t.classList.remove('show'), onClick ? 6000 : 3000);
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
  function buildLines(PIPES) {
    PIPES = PIPES || {};
    const layers = (Array.isArray(PIPES.layers) ? PIPES.layers : [])
      .map((l) => ({ name: String((l && l.name) || ''), color: String((l && l.color) || '#00e5ff') }));
    const lines = [];
    (Array.isArray(PIPES.lines) ? PIPES.lines : []).forEach((l) => {
      if (!l || !Array.isArray(l.pts)) return;
      const hasZ = l.hasZ === true;
      const gIn = Array.isArray(l.g) && l.g.length === l.pts.length ? l.g : null;
      const p = [], g = [];
      l.pts.forEach((q, k) => {
        if (!Array.isArray(q) || !isNum(q[0]) || !isNum(q[1])) return;
        p.push(Number(q[0]), Number(q[1]), hasZ ? num(q[2], NaN) : NaN);
        g.push(gIn ? num(gIn[k], NaN) : NaN);
      });
      if (p.length < 6) return;
      const lay = layers[num(l.layer, 0) | 0] || { name: '', color: '#00e5ff' };
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
      const label = !diaKnown ? base : !base || base === dTxt ? dTxt : base.indexOf(dTxt + ' ') === 0 ? base : dTxt + ' · ' + base;
      const bi = [0]; // 평면에서 겹치지 않는 꼭짓점 번호 (노면 띠)
      for (let k = 3; k < p.length; k += 3) {
        const j = bi[bi.length - 1] * 3;
        if (Math.hypot(p[k] - p[j], p[k + 1] - p[j + 1]) > 1e-6) bi.push(k / 3);
      }
      lines.push({ color: lay.color, rgb: hexRgb(lay.color), label: label, dia: dia, hasZ: hasZ, pts: Float64Array.from(p), g: Float64Array.from(g), bi: bi, ll: ll, e0, e1, n0, n1, mx: null, my: null });
    });
    return lines;
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
  const S = {
    cur: -1, want: -1,
    yaw: qnum('yaw') ?? shots[startIdx].yaw,
    pitch: clamp(qnum('pitch') ?? -45, -89, 89),
    fov: clamp(qnum('fov') ?? 90, FOV_MIN, FOV_MAX),
    pipes: true, depth: Q.get('mode') === 'depth', path: true,
    cover: Math.max(0, num(PRJ.depth, num(PRJ.cover, 1.2))), // 평균매설깊이: 지표 → 관 윗면 (m)
    cam: [0, 0, 0], gz: 0, R: rotOf({ dyaw: 0, dpitch: 0, droll: 0 }),
    marks: [], hover: -1,
  };
  const V = { W: 1, H: 1, w: 1, h: 1, dpr: 1, tanH: 1, tanV: 1, hfov: 90, f: [0, 1, 0], r: [1, 0, 0], u: [0, 0, 1] };

  // ---------------------------------------------------------------- 터파기 설정 (관중심 터파기 · 회사 SAC 기준)
  // tp = 포장 직각 절단 두께 · n = 기울기 1:n (수직 1 에 수평 n) · margin = 관 양옆 여유폭 · bed = 모래 기초 · on = 깊이 방식에서 구덩이로.
  // 프로젝트 설정 = calib.js 의 trench (맞춤과 같은 저장: 프로그램 save_calib · 이 브라우저 · 파일로 저장). 없으면 기본값.
  const TR_DEF = { tp: 0.2, n: 0.3, margin: 0.3, bed: 0.1, on: true };
  const TR_LIM = { tp: [0, 3], n: [0, 3], margin: [0, 3], bed: [0, 1] };
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
    flushView();
    if (needMap) { needMap = false; drawMap(); }
  }

  // ---------------------------------------------------------------- WebGL 360°
  const glc = $('gl'), ovc = $('ov'), ov = ovc.getContext('2d');
  let gl = null, prog = null, tex = null, vao = null, U = {}, maxTex = 4096, glImg = null;
  let pprog = null, pvao = null, pvbo = null, pibo = null, PU = {}, pCount = 0, pKey = '', pOrg = [0, 0, 0];
  let tprog = null, tvao = null, tvbo = null, tibo = null, TU = {}, tN = 0, tNo = 0, tKey = '', tOrg = [0, 0, 0];

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
    'uniform vec2 u_fade;\n' +
    'void main(){\n' +
    '  if (dot(v_nrm, v_rel) > 0.0) discard;\n' +
    '  float a = clamp(1.0 - (length(v_rel.xy) - u_fade.x) / (u_fade.y - u_fade.x), 0.0, 1.0);\n' +
    '  if (a < 0.004) discard;\n' +
    '  o = vec4(v_col, a);\n' +
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
  // 관 그리기 준비. 안 되면 pprog = null → 겹쳐 그리기에서 선으로 대신한다.
  function initPipeGL() {
    pprog = null; pKey = ''; pCount = 0;
    try {
      const p = gl.createProgram();
      gl.attachShader(p, compile(gl.VERTEX_SHADER, PVS));
      gl.attachShader(p, compile(gl.FRAGMENT_SHADER, PFS));
      ['a_pos', 'a_nrm', 'a_col'].forEach((n, i) => gl.bindAttribLocation(p, i, n));
      gl.linkProgram(p);
      if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p) || 'link');
      ['u_off', 'u_f', 'u_r', 'u_u', 'u_tan', 'u_nf', 'u_fade', 'u_alpha'].forEach((n) => { PU[n] = gl.getUniformLocation(p, n); });
      pvao = gl.createVertexArray();
      gl.bindVertexArray(pvao);
      pvbo = gl.createBuffer();
      gl.bindBuffer(gl.ARRAY_BUFFER, pvbo);
      for (let i = 0; i < 3; i++) { gl.enableVertexAttribArray(i); gl.vertexAttribPointer(i, 3, gl.FLOAT, false, 36, i * 12); }
      pibo = gl.createBuffer();
      gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, pibo);
      gl.bindVertexArray(null);
      pprog = p;
    } catch (e) { pprog = null; }
    initTrenchGL();
  }
  // 터파기 준비. 스텐실이 없거나 안 되면 tprog = null → 깊이 방식은 예전처럼 관만.
  function initTrenchGL() {
    tprog = null; tKey = ''; tN = 0; tNo = 0;
    const ca = gl.getContextAttributes && gl.getContextAttributes();
    if (!pprog || !ca || !ca.stencil) return;
    try {
      const p = gl.createProgram();
      gl.attachShader(p, compile(gl.VERTEX_SHADER, PVS));
      gl.attachShader(p, compile(gl.FRAGMENT_SHADER, TFS));
      ['a_pos', 'a_nrm', 'a_col'].forEach((n, i) => gl.bindAttribLocation(p, i, n));
      gl.linkProgram(p);
      if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p) || 'link');
      ['u_off', 'u_f', 'u_r', 'u_u', 'u_tan', 'u_nf', 'u_fade'].forEach((n) => { TU[n] = gl.getUniformLocation(p, n); });
      tvao = gl.createVertexArray();
      gl.bindVertexArray(tvao);
      tvbo = gl.createBuffer();
      gl.bindBuffer(gl.ARRAY_BUFFER, tvbo);
      for (let i = 0; i < 3; i++) { gl.enableVertexAttribArray(i); gl.vertexAttribPointer(i, 3, gl.FLOAT, false, 36, i * 12); }
      tibo = gl.createBuffer();
      gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, tibo);
      gl.bindVertexArray(null);
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
    if (S.pipes && S.depth && S.cur >= 0 && pprog) drawTubes();
  }

  // ---------------------------------------------------------------- 관 (깊이 방식, WebGL)
  const TUBE_SIDES = 20;
  // 꼭짓점 k 의 지표 표고: 자료 g, 없으면 지금 사진의 groundZ
  function gAt(L, k) { const g = L.g[k]; return Number.isFinite(g) ? g : S.gz; }
  // 관 중심 표고: hasZ 면 z, 아니면 지표 − 평균매설깊이 − 관경/2
  function zcAt(L, k) { const z = L.pts[k * 3 + 2]; return L.hasZ && Number.isFinite(z) ? z : gAt(L, k) - S.cover - L.dia / 2; }
  function drawTubes() {
    const key = S.cur + '|' + S.cam.join(',') + '|' + S.gz + '|' + S.cover;
    if (key !== pKey) {
      pKey = key;
      const m = buildTubes();
      gl.bindVertexArray(pvao);
      gl.bindBuffer(gl.ARRAY_BUFFER, pvbo);
      gl.bufferData(gl.ARRAY_BUFFER, m.v, gl.DYNAMIC_DRAW);
      gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, m.i, gl.DYNAMIC_DRAW);
      gl.bindVertexArray(null);
      pCount = m.i.length;
      pOrg = m.o;
    }
    // 터파기: 구멍을 스텐실에 쓰고 안쪽 면을 그린 뒤, 관도 구멍 안에서만 (스텐실 시험은 관을 그린 뒤 끈다)
    const pit = trGL() && drawTrench();
    if (pCount) drawTubeMesh();
    if (pit) gl.disable(gl.STENCIL_TEST);
  }
  function drawTubeMesh() {
    gl.useProgram(pprog);
    gl.bindVertexArray(pvao);
    gl.uniform3f(PU.u_off, pOrg[0] - S.cam[0], pOrg[1] - S.cam[1], pOrg[2] - S.cam[2]);
    gl.uniform3fv(PU.u_f, V.f);
    gl.uniform3fv(PU.u_r, V.r);
    gl.uniform3fv(PU.u_u, V.u);
    gl.uniform2f(PU.u_tan, V.tanH, V.tanV);
    gl.uniform2f(PU.u_nf, 0.1, 2000);
    gl.uniform2f(PU.u_fade, FADE0, FADE1);
    gl.uniform1f(PU.u_alpha, 0.9);
    gl.enable(gl.DEPTH_TEST);
    // 1) 깊이만 → 2) 맨 앞 면만 색칠 (반투명이어도 뒷면이 비치지 않게)
    gl.depthFunc(gl.LESS);
    gl.colorMask(false, false, false, false);
    gl.drawElements(gl.TRIANGLES, pCount, gl.UNSIGNED_INT, 0);
    gl.colorMask(true, true, true, true);
    gl.depthMask(false);
    gl.depthFunc(gl.LEQUAL);
    gl.enable(gl.BLEND);
    gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ZERO, gl.ONE);
    gl.drawElements(gl.TRIANGLES, pCount, gl.UNSIGNED_INT, 0);
    gl.disable(gl.BLEND);
    gl.disable(gl.DEPTH_TEST);
    gl.depthMask(true);
    gl.bindVertexArray(null);
  }
  // 지금 자리에서 FADE1 안의 관을 원통 그물로 (좌표 = 카메라 기준 m). 꼭짓점: 자리 3 + 법선 3 + 색 3.
  function buildTubes() {
    const o = S.cam.slice(), VB = [], IB = [];
    for (const L of lines) {
      const bx = Math.max(L.e0 - o[0], 0, o[0] - L.e1), by = Math.max(L.n0 - o[1], 0, o[1] - L.n1);
      if (bx * bx + by * by > FADE1 * FADE1) continue;
      const P = L.pts, cnt = P.length / 3, r = L.dia / 2;
      let run = [];
      const flush = () => { if (run.length >= 6) addTube(run, r, L.rgb, VB, IB); run = []; };
      const push = (k) => {
        const x = P[k * 3] - o[0], y = P[k * 3 + 1] - o[1], z = zcAt(L, k) - o[2], n = run.length;
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
      return { nd: nd, m: m, P: P, D: nd[0].dia, e0, e1, n0, n1, ext: [true, true], wall: [true, true] };
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
    const key = [TR.tp, TR.n, TR.margin, TR.bed, S.cover, S.gz].join('|');
    if (trSec && trSecKey === key && trSec.of === ch) return trSec;
    trSecKey = key;
    const out = [], ext = Math.max(TR.margin, 0.05); // 여유폭 0 이어도 끝벽이 관 마구리와 겹치지 않게
    for (const c of ch) {
      const m = c.m, P = c.P, F = () => new Float64Array(m);
      const E = F(), N = F(), LX = F(), LY = F(), G = F(), ZM = F(), ZS = F(), WV = F(), WS = F();
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
        const L = c.nd[j * 2], k = c.nd[j * 2 + 1], D = L.dia, g = gAt(L, k);
        const zs = Math.min(zcAt(L, k) - D / 2, g - 0.01), zb = zs - TR.bed, wb = D / 2 + TR.margin, zt = g - TR.tp;
        const wv = wb + TR.n * Math.max(0, zt - zb);
        G[j] = g; ZS[j] = zs; WV[j] = wv;
        if (zt > zs) { ZM[j] = zt; WS[j] = wb + TR.n * (zs - zb); } else { ZM[j] = zs; WS[j] = wv; }
      }
      if (c.ext[0]) { E[0] -= tx[0] * ext; N[0] -= ty[0] * ext; }
      if (c.ext[1]) { E[m - 1] += tx[m - 2] * ext; N[m - 1] += ty[m - 2] * ext; }
      const pad = ext + 10;
      out.push({ c, m, E, N, LX, LY, G, ZM, ZS, WV, WS, e0: c.e0 - pad, e1: c.e1 + pad, n0: c.n0 - pad, n1: c.n1 + pad });
    }
    out.of = ch;
    trSec = out;
    return out;
  }
  // 지금 자리에서 FADE1 안의 구덩이 그물 (카메라 기준 m): 구멍 삼각형 no 개 뒤에 안쪽 면.
  function buildTrench() {
    const o = S.cam.slice(), VB = [], OI = [], II = [];
    const vtx = (p, nr, c) => { VB.push(p[0], p[1], p[2], nr[0], nr[1], nr[2], c[0], c[1], c[2]); };
    const pt = (T, j, w, z) => [T.E[j] + T.LX[j] * w - o[0], T.N[j] + T.LY[j] * w - o[1], z - o[2]]; // w + 왼쪽 · − 오른쪽
    const dk = (T, j, z) => 1 - 0.35 * clamp((T.G[j] - z) / Math.max(T.G[j] - T.ZS[j], 1e-3), 0, 1); // 깊을수록 어둡게
    // 네 점 면 (p0→p1→p2→p3): 법선 = 두 대각선의 곱을 hint 쪽으로 · 빛 × 꼭짓점 어둡기 sh 를 색에 굽는다
    const face = (ps, hint, base, sh) => {
      const d1 = [ps[2][0] - ps[0][0], ps[2][1] - ps[0][1], ps[2][2] - ps[0][2]];
      const d2 = [ps[3][0] - ps[1][0], ps[3][1] - ps[1][1], ps[3][2] - ps[1][2]];
      let nr = cross3(d1, d2);
      const l = Math.hypot(nr[0], nr[1], nr[2]);
      if (l < 1e-9) return;
      const sg = dot3(nr, hint) < 0 ? -1 / l : 1 / l;
      nr = [nr[0] * sg, nr[1] * sg, nr[2] * sg];
      const lam = 0.62 + 0.38 * Math.max(0, dot3(nr, TR_LIGHT));
      const b = VB.length / 9;
      ps.forEach((p, i) => { const s = lam * sh[i]; vtx(p, nr, [base[0] * s, base[1] * s, base[2] * s]); });
      II.push(b, b + 1, b + 2, b, b + 2, b + 3);
    };
    const bot = TR.bed > 0 ? TR_SAND : TR_SOIL;
    const seg = (T, a, b) => {
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
    for (const T of trSecGet()) {
      const bx = Math.max(T.e0 - o[0], 0, o[0] - T.e1), by = Math.max(T.n0 - o[1], 0, o[1] - T.n1);
      if (bx * bx + by * by > FADE1 * FADE1) continue;
      const m = T.m;
      for (let j = 0; j + 1 < m; j++) {
        if (distPtSeg(o[0], o[1], T.E[j], T.N[j], T.E[j + 1], T.N[j + 1]) > FADE1) continue;
        seg(T, j, j + 1);
        if (j === 0 && T.c.wall[0]) endWall(T, 0, unit3([T.E[1] - T.E[0], T.N[1] - T.N[0], 0]));
        if (j === m - 2 && T.c.wall[1]) endWall(T, m - 1, unit3([T.E[m - 2] - T.E[m - 1], T.N[m - 2] - T.N[m - 1], 0]));
      }
    }
    return { o: o, v: Float32Array.from(VB), i: Uint32Array.from(OI.concat(II)), no: OI.length };
  }
  // 구덩이 그리기. 그린 것이 있으면 스텐실 시험 (구멍 안 = 1) 을 켠 채로 true → 관도 구멍 안에서만.
  function drawTrench() {
    const key = S.cur + '|' + S.cam.join(',') + '|' + S.gz + '|' + S.cover + '|' + [TR.tp, TR.n, TR.margin, TR.bed].join(',');
    if (key !== tKey) {
      tKey = key;
      const m = buildTrench();
      gl.bindVertexArray(tvao);
      gl.bindBuffer(gl.ARRAY_BUFFER, tvbo);
      gl.bufferData(gl.ARRAY_BUFFER, m.v, gl.DYNAMIC_DRAW);
      gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, m.i, gl.DYNAMIC_DRAW);
      gl.bindVertexArray(null);
      tN = m.i.length; tNo = m.no; tOrg = m.o;
    }
    if (!tNo) return false;
    gl.useProgram(tprog);
    gl.bindVertexArray(tvao);
    gl.uniform3f(TU.u_off, tOrg[0] - S.cam[0], tOrg[1] - S.cam[1], tOrg[2] - S.cam[2]);
    gl.uniform3fv(TU.u_f, V.f);
    gl.uniform3fv(TU.u_r, V.r);
    gl.uniform3fv(TU.u_u, V.u);
    gl.uniform2f(TU.u_tan, V.tanH, V.tanV);
    gl.uniform2f(TU.u_nf, 0.1, 2000);
    gl.uniform2f(TU.u_fade, FADE0, FADE1);
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
    gl.drawElements(gl.TRIANGLES, tNo, gl.UNSIGNED_INT, 0);
    // 2) 안쪽 면: 구멍 안에서만 · 깊이 씀 (관이 이 깊이와 견줌)
    gl.colorMask(true, true, true, true);
    gl.depthMask(true);
    gl.enable(gl.DEPTH_TEST);
    gl.depthFunc(gl.LESS);
    gl.stencilFunc(gl.EQUAL, 1, 0xff);
    gl.stencilOp(gl.KEEP, gl.KEEP, gl.KEEP);
    gl.enable(gl.BLEND);
    gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ZERO, gl.ONE);
    if (tN > tNo) gl.drawElements(gl.TRIANGLES, tN - tNo, gl.UNSIGNED_INT, tNo * 4);
    gl.disable(gl.BLEND);
    gl.disable(gl.DEPTH_TEST);
    gl.bindVertexArray(null);
    return true;
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

    // ---- 관로: 노면 = 땅 위 관경 폭 띠 · 깊이 = WebGL 관 (여기서는 땅 위 옅은 점선 + 이름표)
    const labels = [];
    if (S.pipes && lines.length) {
      const depth = S.depth, pit = trGL();
      const buckets = new Map(); // 색 · 흐림 단계별
      const bucket = (L, a) => {
        const key = L.color + '|' + a;
        let bk = buckets.get(key);
        if (!bk) { bk = { color: L.color, a: a / 10, poly: [], edge: [], line: [], tube: [] }; buckets.set(key, bk); }
        return bk;
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
      for (const L of lines) {
        const bx = Math.max(L.e0 - Cx, 0, Cx - L.e1), by = Math.max(L.n0 - Cy, 0, Cy - L.n1);
        if (bx * bx + by * by > FADE1 * FADE1) continue;
        const P = L.pts, bi = L.bi, m = bi.length - 1, hw0 = L.dia / 2, hwMax = Math.max(hw0, 3);
        if (m < 1) continue;
        let best = Infinity, lx = 0, ly = 0;
        const cand = () => { // 이름표 자리: 화면 가운데에서 가장 가까운 점
          const sdx = SEG[2] - SEG[0], sdy = SEG[3] - SEG[1], sl = sdx * sdx + sdy * sdy;
          const st = sl > 0 ? clamp(((cx - SEG[0]) * sdx + (cy - SEG[1]) * sdy) / sl, 0, 1) : 0;
          const qx = SEG[0] + st * sdx, qy = SEG[1] + st * sdy, qd = Math.hypot(qx - cx, qy - cy);
          if (qd < best && qx > 10 && qx < w - 10 && qy > 10 && qy < h - 10) { best = qd; lx = qx; ly = qy; }
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
              if (projSeg(A3, B3, SEG)) { if (!pprog) bk.tube.push(SEG[0], SEG[1], SEG[2], SEG[3]); cand(); }
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
            if (projSeg(A3, B3, SEG)) cand();
          }
        }
        close();
        if (best < Infinity && L.label) labels.push({ x: lx, y: ly, text: L.label, color: L.color });
      }
      const path = (s) => {
        ov.beginPath();
        for (let k = 0; k < s.length; k += 4) { ov.moveTo(s[k], s[k + 1]); ov.lineTo(s[k + 2], s[k + 3]); }
      };
      ov.lineCap = 'round';
      ov.lineJoin = 'round';
      if (!depth) {
        for (const bk of buckets.values()) {
          if (!bk.poly.length) continue;
          ov.beginPath();
          for (const pg of bk.poly) {
            ov.moveTo(pg[0], pg[1]);
            for (let k = 2; k < pg.length; k += 2) ov.lineTo(pg[k], pg[k + 1]);
            ov.closePath();
          }
          ov.globalAlpha = 0.85 * bk.a;
          ov.fillStyle = bk.color;
          ov.fill();
          // 1 px 어두운 테두리: 띠 모양으로 자른 2 px 선 → 띠 안쪽 1 px 만 남는다 (띠 폭 = 관경 그대로)
          ov.save();
          ov.clip();
          path(bk.edge);
          ov.globalAlpha = bk.a;
          ov.strokeStyle = 'rgba(0, 0, 0, 0.45)';
          ov.lineWidth = 2;
          ov.stroke();
          ov.restore();
        }
      } else {
        ov.setLineDash([6, 6]);
        ov.lineWidth = 1.5;
        if (!pit) for (const bk of buckets.values()) { ov.globalAlpha = 0.6 * bk.a; ov.strokeStyle = bk.color; path(bk.line); ov.stroke(); }
        ov.setLineDash([]);
        if (!pprog) { // WebGL 관을 못 그리면 관 중심선으로
          ov.strokeStyle = 'rgba(8, 10, 14, 0.9)';
          ov.lineWidth = 7;
          for (const bk of buckets.values()) { ov.globalAlpha = bk.a; path(bk.tube); ov.stroke(); }
          ov.lineWidth = 4;
          for (const bk of buckets.values()) { ov.globalAlpha = bk.a; ov.strokeStyle = bk.color; path(bk.tube); ov.stroke(); }
        }
      }
      if (pit) { // 터파기 구멍: 안쪽 가장자리 그늘 + 밝은 자른 선 (WebGL 구멍과 같은 점 · 같은 투영)
        const tg = new Map(); // 흐림 단계 → { poly, edge }
        const tgb = (a) => { let g = tg.get(a); if (!g) { g = { a: a / 10, poly: [], edge: [] }; tg.set(a, g); } return g; };
        const rim = (T, j0, j1, t, w0, w1) => { // 꼭짓점 j0 → j1 의 t 자리 지표 가장자리 (w + 왼쪽 · − 오른쪽)
          const ax = T.E[j0] + T.LX[j0] * w0, ay = T.N[j0] + T.LY[j0] * w0, bx = T.E[j1] + T.LX[j1] * w1, by = T.N[j1] + T.LY[j1] * w1;
          return [ax + (bx - ax) * t, ay + (by - ay) * t, T.G[j0] + (T.G[j1] - T.G[j0]) * t];
        };
        for (const T of trSecGet()) {
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
              if (j === 0 && q === 0 && T.c.wall[0]) addEdge(g, AL, AR);
              if (j === T.m - 2 && q === pcs - 1 && T.c.wall[1]) addEdge(g, BL, BR);
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

    // ---- 관로 이름표 (맨 위)
    if (labels.length) {
      ov.font = '600 12px Malgun Gothic, Segoe UI, sans-serif';
      ov.textAlign = 'left';
      ov.textBaseline = 'middle';
      for (const lb of labels) {
        const tw = ov.measureText(lb.text).width, pw = tw + 22, ph = 22;
        const bx = clamp(lb.x - pw / 2, 4, w - pw - 4), by = clamp(lb.y - ph - 12, 4, h - ph - 4);
        roundRect(ov, bx, by, pw, ph, 11);
        ov.fillStyle = 'rgba(12, 14, 20, 0.85)';
        ov.fill();
        ov.beginPath();
        ov.arc(bx + 11, by + ph / 2, 4, 0, 2 * Math.PI);
        ov.fillStyle = lb.color;
        ov.fill();
        ov.fillStyle = '#fff';
        ov.fillText(lb.text, bx + 19, by + ph / 2 + 0.5);
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
        if (h.start) h.start(p.x, p.y);
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
      if (pts.size === 0 || (g && g.mode === 'pinch')) { g = null; el.classList.remove('grabbing'); }
    };
    el.addEventListener('pointerup', up);
    el.addEventListener('pointercancel', up);
  }

  // 360° 보기 조작
  let vStart = null;
  gestures(ovc, {
    start: () => { vStart = { yaw: S.yaw, pitch: S.pitch, fov: S.fov }; },
    drag: (dx, dy) => {
      if (!vStart) return;
      updView(); // 휠 직후라도 지금 시야각으로 잰다
      const k = R2D * V.tanH / (V.w / 2); // 화면 가운데에서 1 px 당 각도
      S.yaw = ((vStart.yaw - dx * k) % 360 + 540) % 360 - 180;
      S.pitch = clamp(vStart.pitch + dy * k, -89, 89);
      dirty();
    },
    pinchStart: () => { vStart = { yaw: S.yaw, pitch: S.pitch, fov: S.fov }; },
    pinch: (sc) => { if (vStart) { S.fov = clamp(vStart.fov / sc, FOV_MIN, FOV_MAX); dirty(); } },
    tap: (x, y) => {
      flushView();
      if (AL.on) { alPhoto(x, y); return; }
      const i = markAt(x, y);
      if (i >= 0) openShot(i);
    },
    hover: (x, y) => {
      flushView();
      const i = AL.on ? -1 : markAt(x, y); // 점 찍는 동안은 원판을 키우지 않음 (십자 표시)
      ovc.classList.toggle('point', i >= 0);
      if (i !== S.hover) { S.hover = i; needView = true; kick(); }
    },
  });
  ovc.addEventListener('pointerleave', () => { if (S.hover >= 0) { S.hover = -1; ovc.classList.remove('point'); needView = true; kick(); } });
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
    const nav = !(e.altKey || e.ctrlKey || e.metaKey), dir = Object.prototype.hasOwnProperty.call(ARROW, e.key) ? ARROW[e.key] : null;
    if (nav && dir !== null) { e.preventDefault(); moveDir(dir); }
    else if (nav && e.key === 'PageUp') { e.preventDefault(); stepShot(-1); }
    else if (nav && e.key === 'PageDown') { e.preventDefault(); stepShot(1); }
    else if (e.key === 'Escape' && AL.on) { // 반쯤 만든 짝 취소 · 없으면 끝
      e.preventDefault();
      if (AL.pend) { AL.pend = null; alUI(); dirty(); } else alMode(false);
    }
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
    setOn($('tgPipes'), S.pipes);
    $('mSurf').parentNode.classList.toggle('off', !S.pipes);
    dirty();
  }
  $('tgPipes').addEventListener('click', () => setPipes(!S.pipes));
  const setMode = (depth) => { S.depth = depth; setOn($('mSurf'), !depth); setOn($('mDepth'), depth); dirty(); };
  setOn($('mSurf'), !S.depth);
  setOn($('mDepth'), S.depth);
  $('mSurf').addEventListener('click', () => setMode(false));
  $('mDepth').addEventListener('click', () => setMode(true));
  const coverEl = $('cover');
  coverEl.value = String(round(S.cover, 2));
  coverEl.addEventListener('input', () => { if (isNum(coverEl.value)) setCover(Number(coverEl.value), coverEl); });
  coverEl.addEventListener('change', () => { coverEl.value = String(round(S.cover, 2)); });
  // 평균매설깊이: 윗줄 칸과 단면 창 칸이 같은 값 (from = 지금 치고 있는 칸은 건드리지 않음)
  function setCover(v, from) {
    S.cover = clamp(v, 0, 20);
    if (from !== coverEl) coverEl.value = String(round(S.cover, 2));
    secUI();
    dirty();
  }
  $('tgPath').addEventListener('click', () => { S.path = !S.path; setOn($('tgPath'), S.path); mapDirty(); });
  $('tgMap').addEventListener('click', () => {
    const side = $('side');
    side.classList.toggle('hidden');
    setOn($('tgMap'), !side.classList.contains('hidden'));
    resizeAll();
  });

  // ---------------------------------------------------------------- 터파기 단면 창
  // 고칠 때마다 단면 그림과 360 (깊이 방식 구덩이) 을 바로 다시 그린다. 값은 프로젝트 설정 (맞춤과 같이 저장).
  const secDlg = $('secDlg'), secDia = $('secDia'), secOn = $('secOn'), secSvg = $('secSvg');
  const SEC_UI = [ // 키 · 이름 · 단위 · ± 한 번
    ['cover', '평균매설깊이', 'm', 0.1],
    ['tp', '포장 직각 두께', 'm', 0.05],
    ['n', '기울기 1:n', '', 0.1],
    ['margin', '관 양옆 여유폭', 'm', 0.05],
    ['bed', '모래 기초', 'm', 0.05],
  ];
  const secIn = {};
  const secVal = (k) => (k === 'cover' ? S.cover : TR[k]);
  function trSet(patch) {
    TR = trClean(Object.assign({}, TR, patch), TR);
    trOff = false;
    lsSet(TR_KEY, TR);
    hostCalSoon();
    secUI();
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
  // 단면 그림 (실제 비율, SVG). 지표 = 0, 아래 −.
  function secDraw() {
    const mm = Number(secDia.value) || 300, D = mm / 1000, t = TR, c = S.cover;
    const L0 = lines.find((L) => Math.round(L.dia * 1000) === mm);
    const col = L0 ? L0.color : '#00e5ff';
    const zc = -c - D / 2, pb = -c - D, zb = pb - t.bed, wb = D / 2 + t.margin, zt = -t.tp;
    const wv = wb + t.n * Math.max(0, zt - zb), zm = Math.max(zt, zb);
    const wAt = (z) => (z >= zt ? wv : wb + t.n * Math.max(0, z - zb));
    const W = 380, pl = 72, pr = 80, ptop = 40, pbot = 38, half = wv + 0.45, zl = zb - 0.25;
    const sc = Math.min((W - pl - pr) / (2 * half), (300 - ptop - pbot) / -zl); // 높이는 그림에 맞춤 (300 까지)
    const H = Math.max(150, Math.ceil(ptop + pbot - zl * sc));
    secSvg.setAttribute('viewBox', '0 0 ' + W + ' ' + H);
    const cx = pl + (W - pl - pr) / 2, X = (x) => cx + x * sc, Y = (z) => ptop - z * sc;
    const n1 = (v) => v.toFixed(1), f2 = (v) => v.toFixed(2);
    const P = (pts) => pts.map(([x, z]) => n1(X(x)) + ',' + n1(Y(z))).join(' ');
    const txt = (x, y, s, an) => '<text x="' + n1(x) + '" y="' + n1(y) + '" text-anchor="' + (an || 'middle') + '" fill="#e7eaf0" stroke="#0f131a" stroke-width="3" paint-order="stroke" font-size="11" font-weight="600">' + s + '</text>';
    const ln = (x1, y1, x2, y2, st) => '<line x1="' + n1(x1) + '" y1="' + n1(y1) + '" x2="' + n1(x2) + '" y2="' + n1(y2) + '" stroke="' + (st || '#8d95a8') + '" stroke-width="1"/>';
    const dimH = (x1, x2, y, s, dy) => ln(x1, y, x2, y) + ln(x1, y - 4, x1, y + 4) + ln(x2, y - 4, x2, y + 4) + txt((x1 + x2) / 2, y + dy, s);
    const dimV = (x, y1, y2, s, an, dx) => ln(x, y1, x, y2) + ln(x - 4, y1, x + 4, y1) + ln(x - 4, y2, x + 4, y2) + txt(x + dx, (y1 + y2) / 2 + 4, s, an);
    const side = (z) => (z > zm ? [[wb, zb], [wv, zm], [wv, z]] : [[wb, zb], [wAt(z), z]]); // 오른쪽 벽: 바닥 → 높이 z
    const mirror = (pts) => pts.slice().reverse().map(([x, z]) => [-x, z]);
    const pitR = side(0), sandR = side(pb);
    let s = '';
    s += '<rect x="' + n1(X(-half)) + '" y="' + n1(Y(0)) + '" width="' + n1(2 * half * sc) + '" height="' + n1(-zl * sc) + '" fill="#5a4330" stroke="none"/>'; // 흙
    if (t.tp > 0) s += '<rect x="' + n1(X(-half)) + '" y="' + n1(Y(0)) + '" width="' + n1(2 * half * sc) + '" height="' + n1(t.tp * sc) + '" fill="#474b54" stroke="none"/>'; // 포장
    s += '<polygon points="' + P(mirror(pitR).concat(pitR)) + '" fill="#0f131a" stroke="none"/>'; // 판 곳
    if (t.bed > 0) s += '<polygon points="' + P(sandR.concat(mirror(sandR))) + '" fill="#d8b56a" stroke="none"/>'; // 모래 기초
    s += '<circle cx="' + n1(X(0)) + '" cy="' + n1(Y(zc)) + '" r="' + n1(D / 2 * sc) + '" fill="' + col + '" fill-opacity="0.85" stroke="#ffffff" stroke-width="1.2"/>';
    s += '<path d="M' + n1(X(-half)) + ' ' + n1(Y(0)) + 'H' + n1(X(-wv)) + 'M' + n1(X(wv)) + ' ' + n1(Y(0)) + 'H' + n1(X(half)) + '" fill="none" stroke="#c9d0dc" stroke-width="2"/>'; // 도로 면
    s += '<polyline points="' + P(mirror(pitR).concat(pitR)) + '" fill="none" stroke="#fff4dc" stroke-width="1.5"/>';
    // 치수
    s += dimH(X(-wv), X(wv), Y(0) - 16, '폭 ' + f2(2 * wv), -6);
    s += dimV(14, Y(0), Y(zb), '깊이 ' + f2(-zb), 'start', 6);
    if (t.tp > 0) s += dimV(X(-wv) - 10, Y(0), Y(-t.tp), '포장 ' + f2(t.tp), 'end', -6);
    if (t.bed > 0) s += dimV(X(-wAt(pb)) - 10, Y(zb), Y(pb), '기초 ' + f2(t.bed), 'end', -6);
    if (c > 0) s += dimV(X(wv) + 26, Y(0), Y(-c), '토피 ' + f2(c), 'start', 6);
    s += ln(X(D / 2), Y(zc), X(D / 2), Y(zb) + 18, '#5d6577') + dimH(X(D / 2), X(wb), Y(zb) + 14, '여유 ' + f2(t.margin), 14);
    if (zm > zb + 1e-6 && t.n > 0) s += txt(X(wAt((zm + zb) / 2)) - 6, Y((zm + zb) / 2) + 4, '1:' + round(t.n, 3), 'end');
    s += D / 2 * sc > 16 ? txt(X(0), Y(zc) + 4, 'D' + mm) : txt(X(D / 2) + 6, Y(zc) + 4, 'D' + mm, 'start');
    secSvg.innerHTML = s;
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
    $('secDef').addEventListener('click', () => { trSet(Object.assign({}, TR_DEF)); toast('기본값 (SAC)'); });
    $('secClose').addEventListener('click', () => secShow(false));
    $('secX').addEventListener('click', () => secShow(false));
    $('tgSec').addEventListener('click', () => secShow(secDlg.hidden));
  })();

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
    $('calHead').addEventListener('click', () => {
      const p = $('calPane');
      p.classList.toggle('open');
      lsSet('rv_ui:calOpen', p.classList.contains('open'));
      resizeAll();
    });
    if (lsGet('rv_ui:calOpen')) $('calPane').classList.add('open');
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
    out.trench = { tp: TR.tp, n: TR.n, margin: TR.margin, bed: TR.bed, on: TR.on }; // 터파기 단면 (프로젝트 설정)
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
  function linesToMap() {
    lines.forEach((L) => {
      const cnt = L.pts.length / 3;
      if (L.ll) {
        L.mx = new Float64Array(cnt); L.my = new Float64Array(cnt);
        L.ll.forEach((q, k) => { L.mx[k] = lon2x(Number(q[1])); L.my[k] = lat2y(Number(q[0])); });
      } else if (SIM) {
        L.mx = new Float64Array(cnt); L.my = new Float64Array(cnt);
        for (let k = 0; k < cnt; k++) { const m = en2m(L.pts[k * 3], L.pts[k * 3 + 1]); L.mx[k] = m[0]; L.my[k] = m[1]; }
      }
    });
  }
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
      lines = buildLines(window.RV_PIPES);
      linesToMap();
      pKey = ''; // 깊이 방식 관 그물 다시
      tKey = ''; // 터파기 그물도 (이은 줄 · 단면은 lines 가 바뀌면 저절로 다시)
      if (!secDlg.hidden) { secOptions(); secUI(); }
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
    mx.fillStyle = '#0f131a';
    mx.fillRect(0, 0, w, h);
    mx.imageSmoothingEnabled = true;
    if (BASE.blank) drawGrid(); else BASE.layers.forEach((L) => { if (NAMES || !L.names) drawLayer(L); });
    const k = mk(), ox = w / 2 - M.cx * k, oy = h / 2 - M.cy * k;
    mx.lineCap = 'round';
    mx.lineJoin = 'round';

    // 관로
    if (S.pipes) {
      for (const L of lines) {
        if (!L.mx) continue;
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
    start: () => { mStart = null; },
    drag: (dx, dy, ddx, ddy) => { const k = mk(); M.cx -= ddx / k; M.cy -= ddy / k; mapDirty(); },
    pinchStart: (x, y) => { mStart = { z: M.z, X: s2m(x, y) }; },
    pinch: (sc, x, y) => {
      if (!mStart) return;
      M.z = clamp(mStart.z + Math.log2(sc), MAP_ZMIN, MAP_ZMAX);
      const k = mk();
      M.cx = mStart.X[0] - (x - M.w / 2) / k;
      M.cy = mStart.X[1] - (y - M.h / 2) / k;
      mapDirty();
    },
    tap: (x, y) => {
      if (AL.on && AL.pend) { alMap(x, y); return; }
      const i = shotAtMap(x, y);
      if (i >= 0) openShot(i);
      else if (AL.on) toast('① 사진에서 먼저');
    },
    hover: (x, y) => { mc.classList.toggle('point', !(AL.on && AL.pend) && shotAtMap(x, y) >= 0); },
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
    ['pipeDlg', 'expDlg'].forEach((k) => { $(k).hidden = k !== id; });
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
    hostCall('import_pipes', arg).then((r) => {
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
    hostCall('export_web').then((r) => {
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
    hostCall('export_folder').then((r) => {
      done();
      if (!r) return; // 취소
      const path = String(r.path || '');
      if (r.ok) toast('내보냄 · ' + path, () => hostCall('copy_text', path).then(() => toast('복사됨'), () => copyText(path)));
      else toast(String(r.msg || '못 내보냄'));
    }, (e) => { done(); toast(errText(e)); });
  });

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
