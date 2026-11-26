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
  const AREA_TMP = '#ff9a1f';      // 터파기 영역: 임시점용 (주황 점선 · 옅은 채움)
  // 가시설 (흙막이): RV_PIPES.lines[i].shore = [{ a, b = 측점 m (그 줄 첫 점부터 평면 길이), t = 종류 }] · 그 구간은 터파기 기울기 0 (곧은 벽)
  const SHORE_T = ['panel', 'hpile', 'sheet'];
  const SHORE_NAME = { panel: '조립식 간이흙막이', hpile: '토류벽 흙막이', sheet: '시트파일' };
  const SHORE_COL = { panel: '#f2b33d', hpile: '#b07a45', sheet: '#9aa6b4' }; // 지도 · 목록
  // 관종 (JACK 1124): RV_PIPES.lines[i].mat = ci (주철관) · steel (강관) · plastic (비금속관) · 없으면 이름 + 관로 층 글자로 짐작 (프로그램 SHP 와 같은 규칙)
  // 3D 색: 주철 = 짙은 회색 (아스팔트 도장) · 강관 = 하늘색 (지금 색) · 비금속 = 흰색
  const MAT_T = ['ci', 'steel', 'plastic'];
  const MAT_NAME = { ci: '주철관', steel: '강관', plastic: '비금속관' }, MAT_SHORT = { ci: '주철', steel: '강관', plastic: '비금속' };
  const MAT_RGB = { ci: [0.25, 0.25, 0.26], steel: [0, 0.898, 1], plastic: [0.93, 0.93, 0.91] };
  // 하천 추진 · 횡단 (JACK 1126): RV_PIPES.lines[i].river = [{ k: 'jack' | 'open', a, b, … }] · 셈 · 기하 = viewer/river.js (3D 매핑 화면과 같은 셈) ·
  // 360 은 붙이는 코드만 (불러온 관 위 두 점 · 땅 = groundAt · 그물 = 터파기와 같은 셰이더 · 구멍) · river.js 가 없으면 그 카테고리 · 수량 갈래만 숨김
  const RV = window.RVRiver || null;
  function guessMat(text) {
    const u = String(text || '').toUpperCase();
    if (/비금속|폴리|PVC|HDPE|GRP|(?<![A-Z])PE(?![A-Z])/.test(u)) return 'plastic';
    if (/강관|STEEL|STPW|STS|(?<![A-Z])SP(?![A-Z])/.test(u)) return 'steel';
    return 'ci';
  }

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
  // 프로그램 안에서 열면 window.pywebview.api 가 있다 → 윗줄 [처음] · 관로 [SHP 불러오기 · 내보내기 · 지장물] · 결과물 · 웹 공유 · 사진점 지우기.
  // 웹 공유 · 브라우저에서는 숨김 ([data-host] · 3D 매핑 화면 map3d.js hostShow 와 같음).
  const HOST = () => (window.pywebview && window.pywebview.api) || null;
  function hostCall(name, arg) {
    const h = HOST();
    if (!h || typeof h[name] !== 'function') return Promise.reject(new Error('프로그램 연결 없음'));
    return Promise.resolve().then(() => (arg === undefined ? h[name]() : h[name](arg)));
  }
  const errText = (e) => String((e && e.message) || e || '오류');
  function hostShow() {
    const h = !!HOST();
    document.querySelectorAll('[data-host]').forEach((e) => { e.hidden = !h; });
    document.body.classList.toggle('host', h);
  }
  let beforeHome = () => Promise.resolve(); // [처음] 전에 할 일 (맞춤 저장)
  $('hHome').addEventListener('click', () => {
    beforeHome().catch(() => {}).then(() => hostCall('go_home')).catch((e) => toast(errText(e)));
  });
  window.addEventListener('pywebviewready', hostShow);

  // ---------------------------------------------------------------- 자료
  const PRJ = window.RV_PROJECT;
  function noData(text) { // 자료가 없어도 프로그램에서는 [처음] 으로 돌아갈 수 있게 (카테고리는 숨김)
    viewMsg(text);
    hostShow();
    $('opts').hidden = true;
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
    const layName = Number.isInteger(l.layer) && l.layer >= 0 && l.layer < pipeLayers.length ? pipeLayers[l.layer].name : '';
    const mat = MAT_T.indexOf(l.mat) >= 0 ? l.mat : guessMat(String(l.label || '') + ' ' + layName);
    const cum = new Float64Array(p.length / 3); // 측점 (첫 점부터 평면 길이)
    for (let k = 3; k < p.length; k += 3) cum[k / 3] = cum[k / 3 - 1] + Math.hypot(p[k] - p[k - 3], p[k + 1] - p[k - 2]);
    const len = cum[cum.length - 1];
    // 가시설 구간 (줄 안으로 자름 · i = RV_PIPES 의 shore 번호)
    const shore = (Array.isArray(l.shore) ? l.shore : []).map((q, i) => (q && isNum(q.a) && isNum(q.b) && SHORE_T.indexOf(q.t) >= 0
      ? { a: clamp(Math.min(+q.a, +q.b), 0, len), b: clamp(Math.max(+q.a, +q.b), 0, len), t: q.t, i: i } : null))
      .filter((q) => q && q.b - q.a > 1e-3).sort((x, y) => x.a - y.a);
    // 하천 구간 (river.js clean = 줄 안으로 · 모자란 값 = 기본값 · i = RV_PIPES 의 river 번호)
    const river = RV ? (Array.isArray(l.river) ? l.river : []).map((q, i) => { const c = RV.clean(q, len); return c ? Object.assign(c, { i: i }) : null; })
      .filter((q) => q).sort((x, y) => x.a - y.a) : [];
    return {
      src: src, color: lay.color, rgb: rgb, rgbHi: rgbHi, colorHi: 'rgb(' + rgbHi.map((v) => Math.round(v * 255)).join(',') + ')',
      label: label, base: base, kind: kind, diaKnown: diaKnown, dia: dia, hasZ: hasZ, pts: Float64Array.from(p), g: Float64Array.from(g), bi: bi, ll: ll,
      ctrl: ctrlOf(l).map((k) => at[k]).filter((k) => k >= 0), e0, e1, n0, n1, mx: null, my: null, cum: cum, len: len, shore: shore, mat: mat, river: river,
    };
  }
  // 측점 s 가 든 가시설 구간 (없으면 null)
  function shoreAt(L, s) {
    for (const q of L.shore) if (s > q.a && s < q.b) return q;
    return null;
  }
  // 하천 계획 (river.js plan): 땅 = groundAt (높이 타일 RV_DEM → 없으면 사진 땅 높이) · 줄마다 기억 (줄을 고치면 새 줄 = 새로 셈)
  const rvMemo = new WeakMap();
  function rvGround(e, n) { return groundAt(e, n); }
  function rvPlans(L) {
    if (!RV || !L.river || !L.river.length) return [];
    const key = shots.length, m = rvMemo.get(L);
    if (m && m.key === key) return m.p;
    const xy = [];
    for (let k = 0; k < L.pts.length; k += 3) xy.push([L.pts[k], L.pts[k + 1]]);
    const P = RV.path(xy);
    const p = L.river.map((q) => { const pl = RV.plan(q, P, rvGround, L.dia); if (pl) pl.i = q.i; return pl; }).filter((x) => x);
    rvMemo.set(L, { key: key, p: p });
    return p;
  }
  function rvAny() { return !!RV && lines.some((L) => L.river && L.river.length); }
  function rvPipeZ(L) { return rvPlans(L).filter((p) => Number.isFinite(p.zc)).map((p) => ({ a: p.a, b: p.b, zc: p.zc })); }
  // 관 줄 → [x, y, z, 측점] (하천 구간 안 = 그 깊이로 수평 · 구간 끝 = 같은 자리에서 수직으로 꺾임 · 3D 화면과 같음)
  function rvPath(L, secs, zf) {
    const P = L.pts, C = L.cum, cnt = P.length / 3, out = [];
    const inSec = (s) => secs.find((q) => s > q.a && s < q.b);
    for (let k = 0; k + 1 < cnt; k++) {
      const s0 = C[k], l = C[k + 1] - s0, za = zf(L, k), zb = zf(L, k + 1), ts = [0, 1];
      if (l > 1e-9) for (const q of secs) for (const s of [q.a, q.b]) if (s > s0 + 1e-6 && s < s0 + l - 1e-6) ts.push((s - s0) / l);
      ts.sort((x, y) => x - y);
      for (const t of ts) {
        if (k > 0 && t === 0) continue;
        const s = s0 + l * t, x = P[k * 3] + (P[k * 3 + 3] - P[k * 3]) * t, y = P[k * 3 + 1] + (P[k * 3 + 4] - P[k * 3 + 1]) * t, zn = za + (zb - za) * t;
        const before = inSec(s - 1e-3), after = inSec(s + 1e-3), z0 = before ? before.zc : zn, z1 = after ? after.zc : zn;
        out.push([x, y, z0, s]);
        if (Math.abs(z1 - z0) > 1e-6) out.push([x, y, z1, s]);
      }
    }
    return out;
  }
  let lines = buildLines(window.RV_PIPES);

  // ---------------------------------------------------------------- 맞춤 (영상마다)
  const CAL_F = ['dx', 'dy', 'dz', 'dyaw', 'dpitch', 'droll'];
  // 땅 높이가 수치지형도 (사내 DB · DXF) 에서 온 프로젝트: 높이 (dz) 는 수치지형도로 맞춰져 있다 → 0 으로 두고 손 · 점 맞추기 모두 안 바꿈
  const DZ_LOCK = /수치지도/.test(String(PRJ.terrain || ''));
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
    if (DZ_LOCK) c.dz = 0;
    calMemo.set(vid, c);
    return c;
  }
  function calFileVal(vid, k) { const f = fileCal[vid] || {}; return isNum(f[k]) ? Number(f[k]) : 0; }
  function setCal(vid, k, v) {
    const c = Object.assign({}, calibFor(vid));
    c[k] = DZ_LOCK && k === 'dz' ? 0 : round(v, 4);
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
  // 영상 vid 의 짝으로 맞춤 풀기 (Levenberg–Marquardt, 지금 값에서 시작). 1 짝 → 동 · 북, 2 → + 방향, 3 이상 → + 높이 (수치지형도 프로젝트는 높이 빼고).
  // 남북 · 동서 기울기: 먼 곳(사진에서 40 m 넘게)을 찍은 짝이 있고 짝이 넉넉하면(풀 값 수 ≤ 짝 × 2) 같이 푼다 —
  // 비스듬히 보는 먼 곳은 기울기 0.3° 가 150 m 에서 6 m 로 부풀어 어긋난다 (1124 JACK «먼 곳 안 맞음»). 가까운 짝만이면 기울기는 드러나지 않아 그대로.
  // 결과: 'ok' (적용함) · 'bad' (못 풂 · 값이 터무니없음 → 그대로 둠) · 'none' (짝 없음)
  const AL_FAR = 40;       // m · 이보다 먼 곳을 찍은 짝이 있어야 기울기를 푼다
  const AL_TILT_MAX = 5;   // ° · 이보다 크게 풀리면 점이 잘못 찍힌 것으로 봄
  function alSolve(vid) {
    const c0 = calibFor(vid), R0 = rot64(c0), use = [];
    pairRes(vid).forEach((o) => { if (o.h) use.push(o); });
    if (!use.length) return pairsOf(vid).length ? 'bad' : 'none';
    const keys = use.length === 1 ? ['dx', 'dy'] : use.length === 2 || DZ_LOCK ? ['dx', 'dy', 'dyaw'] : ['dx', 'dy', 'dz', 'dyaw'];
    const far = use.some((o) => Math.hypot(o.h[0] - o.s.e, o.h[1] - o.s.n) > AL_FAR);
    if (use.length >= 3 && far && keys.length + 2 <= use.length * 2) keys.push('dpitch', 'droll');
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
    if (!x.every(Number.isFinite) || Math.abs(c.dz) > 15 || Math.abs(c.dyaw) > 30 ||
        Math.abs(c.dpitch || 0) > AL_TILT_MAX || Math.abs(c.droll || 0) > AL_TILT_MAX) return 'bad';
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
  // 지하 시설물 표현 (둘 중 하나 · JACK 1124): area = 터파기 영역 (땅 위 임시점용 · 영구점용 테두리 · 지도도) ·
  // trench = 터파기 (3D 시공 구덩이 · 관 · 가시설). 주소 ?mode= : area · bury → 영역 · trench · depth · open · sec → 터파기 (옛 이름).
  const MODES = ['area', 'trench'];
  const qMode = String(Q.get('mode') || '').toLowerCase();
  const mode0 = ['trench', 'depth', 'open', 'sec'].indexOf(qMode) >= 0 ? 'trench' : 'area';
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
  S.vtx = []; // 360 다른 관 원래 꼭짓점 (관로 고치기 · 화면 자리 · 끌면 그 관을 고르고 옮김)

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
  // 표시 항목 (이 브라우저에 기억): lbl = 이름표 · tmp = 임시점용 · perm = 영구점용 (영역 표현의 범례 단추). (층 띠는 단면 그림에만)
  // 관로 본체 (S.pipes) 는 기억하지 않는다 (꺼 둔 채 다시 열면 관이 안 보여 헷갈림) · 지장물은 rv_ui:obst · 터파기 형상 = TR.on.
  const SHOW_KEY = 'rv_ui:show';
  const SHOW = (function () { const v = lsGet(SHOW_KEY) || {}; return { lbl: v.lbl !== false, tmp: v.tmp !== false, perm: v.perm !== false }; })();
  // 가시설 만들기: arm = 고른 종류 (누르는 중) · a = 찍은 시작점 { src, s, e, n, g } · sig = 목록 지문
  const SH = { arm: null, a: null, sig: '' };
  // 사진점 지우기 (지도 · 프로그램만): on · a = 처음 고른 사진 (shots 번호) · b = 둘째 · ids = 지울 사진 id · busy
  const TM = { on: false, a: -1, b: -1, ids: [], busy: false };

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
    cfUI(); // 간섭 카테고리 (간섭 자리 바뀐 때만 다시)
    shUI(); // 가시설 목록 (바뀐 때만 다시)
    rvUI(); // 하천 목록 · 설정 칸 · 종단 창 (바뀐 때만 다시)
    if (ED.on) $('edChip').dataset.sel = String(ED.sel); // 고른 관 (확인 · 시험)
  }

  // ---------------------------------------------------------------- WebGL 360°
  const glc = $('gl'), ovc = $('ov'), ov = ovc.getContext('2d');
  let gl = null, prog = null, tex = null, vao = null, U = {}, maxTex = 4096, glImg = null;
  // 관 · 터파기 그물은 둘씩: [0] = 고치지 않는 관 (그대로 둠) · [1] = 고른 관 (관로 고치기 · 끄는 동안 이것만 다시)
  // n = 삼각형 번호 수 · no = 앞쪽 구멍 번호 수 (터파기) · o = 만든 자리 (카메라) · key = 다시 만들지 정하는 열쇠
  let pprog = null, PU = {}, tprog = null, TU = {};
  // ns = 끝쪽 가시설 번호 수 (터파기)
  const meshNew = () => ({ vao: null, vbo: null, ibo: null, n: 0, no: 0, ns: 0, key: '', o: [0, 0, 0] });
  const tubeM = [meshNew(), meshNew()], pitM = [meshNew(), meshNew()], obM = meshNew(); // obM = 지장물 관
  const rvM = meshNew(); // 하천 추진 · 횡단 (수직구 · 케이싱 · 가물막이 · 구멍 no = 수직구 입구 · 나머지 = 가시설처럼 ns)
  const NO_MESH = { o: [0, 0, 0], v: new Float32Array(0), i: new Uint32Array(0), no: 0, ns: 0 };

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
    'in vec3 a_pos; in vec3 a_nrm; in vec3 a_col; in vec4 a_aux;\n' +
    'uniform vec3 u_off, u_f, u_r, u_u; uniform vec2 u_tan, u_nf;\n' +
    'out vec3 v_rel, v_nrm, v_col; out vec4 v_aux;\n' +
    'invariant gl_Position;\n' +
    'void main(){\n' +
    '  vec3 rel = a_pos + u_off;\n' +
    '  v_rel = rel; v_nrm = a_nrm; v_col = a_col; v_aux = a_aux;\n' +
    '  float z = dot(rel, u_f), n = u_nf.x, f = u_nf.y;\n' +
    '  gl_Position = vec4(dot(rel, u_r) / u_tan.x, dot(rel, u_u) / u_tan.y, (f + n) / (f - n) * z - 2.0 * f * n / (f - n), z);\n' +
    '}\n';
  // 해 (세계 방향 · 북서쪽 위 54°): 관 · 터파기 면이 같은 빛을 받는다
  const SUN_GL = 'const vec3 SUN = vec3(-0.36682, 0.44833, 0.81514);\n';
  // 관 (원통): 해 (램버트) + 하늘 (위 밝고 밑 어둡게) + 보는 쪽 채움 빛 + 부드러운 반짝임 → 둥근 관으로 보이게
  const PFS = '#version 300 es\n' +
    'precision highp float;\n' +
    'in vec3 v_rel, v_nrm, v_col; out vec4 o;\n' +
    'uniform vec3 u_f, u_r; uniform vec2 u_fade; uniform float u_alpha;\n' + SUN_GL +
    'void main(){\n' +
    '  float a = u_alpha * clamp(1.0 - (length(v_rel.xy) - u_fade.x) / (u_fade.y - u_fade.x), 0.0, 1.0);\n' +
    '  if (a < 0.004) discard;\n' +
    '  vec3 N = normalize(v_nrm), V = normalize(-v_rel);\n' +
    '  float sky = 0.5 + 0.5 * N.z;\n' +
    '  float dif = max(dot(N, SUN), 0.0);\n' +
    '  float fill = max(dot(N, normalize(V + vec3(0.0, 0.0, 0.6))), 0.0);\n' +
    '  float sp = pow(max(dot(N, normalize(SUN + V)), 0.0), 36.0);\n' +
    '  float rim = pow(1.0 - abs(dot(N, V)), 3.0);\n' +
    '  vec3 c = v_col * (0.10 + 0.30 * sky + 0.45 * dif + 0.22 * fill) + 0.32 * sp + 0.10 * rim * mix(v_col, vec3(1.0), 0.5);\n' +
    '  o = vec4(min(c, vec3(1.0)), a);\n' +
    '}\n';
  // 터파기 안쪽 면 (꼭짓점 셰이더는 관과 같음). 안쪽 법선이 보는 사람 쪽을 볼 때만 그린다
  // (볼록한 구덩이 안에서 시선이 빠져나가는 면 = 보이는 면 · 가시설 상자는 뒷면을 버림). 구멍 (지표 띠) 도 이 셰이더로 스텐실에만 쓴다 (법선 위).
  // v_aux = (재질, 어둡기, 관 옆 거리/반지름, 그림자 쪽으로 민 거리/반지름 · 바닥만, 아니면 99) · 가시설 = (재질, 어둡기, 99, 지표 표고).
  // 재질: 1 포장 자른 면 · 2 벽 (굴삭기로 깎은 흙) · 3 바닥 (다진 모래) · 7 관 자른 면 · 0 그대로 ·
  //       가시설 8 강판 (조립식 판) · 9 토류판 (나무) · 10 H형강 · 띠장 · 버팀보 · 11 시트파일 (녹슨 강) · 12 보강대 (칠한 강).
  // 결 = 세계 좌표 (m) 값 잡음 (몇 겹) → 돌려 봐도 그 자리 · 화면 2 px 보다 잘아지는 겹은 평균으로 (자글거림 없게).
  // u_above = 1: 지표 위만 그림 (가시설이 땅 위로 나온 곳 · 구멍 밖).
  const TFS = '#version 300 es\n' +
    'precision highp float; precision highp int;\n' +
    'in vec3 v_rel, v_nrm, v_col; in vec4 v_aux; out vec4 o;\n' +
    'uniform vec2 u_fade; uniform float u_alpha, u_above; uniform vec3 u_cam;\n' + SUN_GL +
    'float FW;\n' +
    'uint hu(uvec3 v){ v = v * 1664525u + 1013904223u; v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;\n' +
    '  v ^= v >> 16u; v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y; return v.x ^ v.y ^ v.z; }\n' +
    'float hf(ivec3 c){ return float(hu(uvec3(c)) & 0xffffffu) / 16777215.0; }\n' +
    'float vn(vec3 x){ vec3 fl = floor(x), f = x - fl; ivec3 i = ivec3(fl); f = f * f * (3.0 - 2.0 * f);\n' +
    '  float a = mix(hf(i), hf(i + ivec3(1, 0, 0)), f.x), b = mix(hf(i + ivec3(0, 1, 0)), hf(i + ivec3(1, 1, 0)), f.x);\n' +
    '  float c = mix(hf(i + ivec3(0, 0, 1)), hf(i + ivec3(1, 0, 1)), f.x), d = mix(hf(i + ivec3(0, 1, 1)), hf(i + ivec3(1, 1, 1)), f.x);\n' +
    '  return mix(mix(a, b, f.y), mix(c, d, f.y), f.z); }\n' +
    'float fk(float l){ return clamp(l / FW * 0.5 - 0.5, 0.0, 1.0); }\n' +
    'float nz(vec3 W, float l, float s){ float k = fk(l); return k > 0.0 ? mix(0.5, vn(W / l + s), k) : 0.5; }\n' +
    // 축마다 다른 크기 (나뭇결 · 녹물 줄): 가장 잘은 축으로 거른다
    'float nz3(vec3 W, vec3 l, float s){ float k = fk(min(min(l.x, l.y), l.z)); return k > 0.0 ? mix(0.5, vn(W / l + s), k) : 0.5; }\n' +
    'vec3 wall(vec3 W, vec3 N, vec3 b){\n' + // 벽: 어두운 흙 · 큰 덩어리 · 굴삭기 이빨 자국 (세로 홈 0.16 m · 버킷 한 번 0.64 m · 0.8 m 마다 어긋남) · 자갈
    '  float c1 = nz(W, 0.22, 3.1), c2 = nz(W, 0.07, 7.7), g1 = nz(W, 0.022, 1.3), g2 = nz(W, 0.008, 5.9);\n' +
    '  float t = 1.0 + 0.6 * (c1 * 0.55 + c2 * 0.45 - 0.5) + 0.3 * (g1 - 0.5) + 0.22 * (g2 - 0.5);\n' +
    '  float hl = length(N.xy);\n' +
    '  if (hl > 0.2) {\n' +
    '    float u = dot(W.xy, vec2(-N.y, N.x) / hl), ps = floor(u / 0.64), off = hf(ivec3(int(ps), int(floor(W.z / 0.8)), 17));\n' +
    '    float q = u / 0.16 + off * 4.0 + 0.3 * sin(W.z * 3.1 + off * 6.283);\n' +
    '    float gr = abs(fract(q) - 0.5) * 2.0, aw = max(0.3, 1.5 * FW / 0.16);\n' +
    '    float k = (1.0 - smoothstep(0.03, 0.07, FW)) * smoothstep(0.3, 0.5, vn(vec3(ps * 1.7 + off, W.z * 2.0, 3.0)));\n' +
    '    t *= 1.0 - k * (0.36 * (1.0 - smoothstep(0.0, aw, gr)) - 0.1 * smoothstep(0.55, 0.95, gr));\n' +
    '  }\n' +
    '  vec3 c = mix(b * t, vec3(0.5, 0.48, 0.45) * (0.8 + 0.4 * g2), smoothstep(0.8, 0.86, nz(W, 0.05, 17.0)) * 0.6);\n' +
    '  return c * (1.0 - 0.3 * (1.0 - smoothstep(0.06, 0.1, nz(W, 0.016, 23.0)))); }\n' +
    'vec3 flr(vec3 W, vec3 b){\n' + // 바닥: 밝은 다진 모래 · 고운 알갱이 · 옅은 얼룩 · 작은 자갈 (벽보다 매끈)
    '  float m1 = nz(W, 0.4, 8.8), m2 = nz(W, 0.012, 2.2), m3 = nz(W, 0.005, 4.4);\n' +
    '  vec3 c = b * (1.0 + 0.1 * (m1 - 0.5) + 0.12 * (m2 - 0.5) + 0.1 * (m3 - 0.5));\n' +
    '  return mix(c, vec3(0.6, 0.57, 0.52) * (0.85 + 0.3 * m3), smoothstep(0.86, 0.9, nz(W, 0.03, 13.0)) * 0.5); }\n' +
    'vec3 asph(vec3 W, vec3 b){\n' + // 아스팔트: 거친 알갱이 · 밝은 골재 · 검은 바인더
    '  vec3 c = b * (1.15 + 0.34 * (nz(W, 0.018, 1.7) - 0.5) + 0.26 * (nz(W, 0.008, 3.9) - 0.5) + 0.1 * (nz(W, 0.12, 2.9) - 0.5));\n' +
    '  c = mix(c, b * 1.55, smoothstep(0.7, 0.76, nz(W, 0.03, 5.3)) * 0.6);\n' +
    '  return mix(c, b * 0.6, smoothstep(0.74, 0.8, nz(W, 0.022, 7.1)) * 0.5); }\n' +
    'vec3 steel(vec3 W, vec3 b, float r){\n' + // 강: 압연 결 · 녹 (r 만큼) · 녹물 세로 줄
    '  vec3 c = b * (1.0 + 0.1 * (nz(W, 0.05, 1.9) - 0.5) + 0.08 * (nz3(W, vec3(0.6, 0.6, 0.03), 3.3) - 0.5));\n' +
    '  float rs = smoothstep(0.5, 0.72, nz(W, 0.3, 6.1) * 0.6 + nz(W, 0.06, 2.4) * 0.4) * r;\n' +
    '  float st = smoothstep(0.55, 0.8, nz3(W, vec3(0.05, 0.05, 1.2), 9.4)) * r;\n' +
    '  return mix(c, vec3(0.4, 0.22, 0.11) * (0.75 + 0.5 * nz(W, 0.02, 8.3)), clamp(rs + 0.5 * st, 0.0, 0.85)); }\n' +
    'vec3 wood(vec3 W, vec3 b){\n' + // 토류판: 판 한 장 높이 0.2 m (틈 · 판마다 밝기) · 가로 나뭇결 · 옹이
    '  float f = fract(W.z / 0.2), tn = 0.84 + 0.3 * hf(ivec3(int(floor(W.z / 0.2)), int(b.g * 4096.0), 5));\n' +
    '  vec3 c = b * tn * (0.86 + 0.28 * nz3(W, vec3(0.9, 0.9, 0.02), 4.1));\n' +
    '  c *= 1.0 - 0.35 * smoothstep(0.86, 0.9, nz(W, 0.05, 12.0));\n' +
    '  float gap = 1.0 - smoothstep(0.0, max(0.05, 1.2 * FW / 0.2), min(f, 1.0 - f));\n' +
    '  return c * (1.0 - 0.6 * gap * (1.0 - smoothstep(0.02, 0.05, FW))); }\n' +
    'void main(){\n' +
    '  vec3 W = v_rel + u_cam;\n' +
    '  FW = max(length(dFdx(W)), length(dFdy(W))) + 1e-5;\n' +
    '  if (dot(v_nrm, v_rel) > 0.0) discard;\n' +
    '  if (u_above > 0.5 && W.z < v_aux.w - 0.003) discard;\n' +
    '  float a = clamp(1.0 - (length(v_rel.xy) - u_fade.x) / (u_fade.y - u_fade.x), 0.0, 1.0);\n' +
    '  if (a < 0.004) discard;\n' +
    '  int m = int(v_aux.x + 0.5);\n' +
    '  vec3 N = normalize(v_nrm), c = v_col;\n' +
    '  if (m == 1) c = asph(W, v_col);\n' +
    '  else if (m == 2) c = wall(W, N, v_col);\n' +
    '  else if (m == 3) c = flr(W, v_col);\n' +
    '  else if (m == 8) c = steel(W, v_col, 0.12);\n' +
    '  else if (m == 9) c = wood(W, v_col);\n' +
    '  else if (m == 10) c = steel(W, v_col, 0.45);\n' +
    '  else if (m == 11) c = steel(W, v_col, 0.7);\n' +
    '  else if (m == 12) c = steel(W, v_col, 0.15);\n' +
    '  float li = m == 0 || m == 7 ? 1.0 : 0.52 + 0.5 * max(dot(N, SUN), 0.0);\n' + // 해 · 하늘
    '  float sh = 1.0;\n' +
    '  if (v_aux.z < 50.0) sh = (1.0 - 0.5 * (1.0 - smoothstep(0.8, 2.2, abs(v_aux.w)))) * (1.0 - 0.3 * (1.0 - smoothstep(1.0, 1.8, abs(v_aux.z))));\n' + // 관 그림자 · 맞닿은 곳
    '  vec3 col = c * li * v_aux.y * sh;\n' +
    '  if (m >= 8 && m != 9) col += (m == 11 ? 0.1 : 0.2) * v_aux.y * pow(max(dot(N, normalize(SUN - normalize(v_rel))), 0.0), 28.0);\n' + // 강 반짝임
    '  o = vec4(col, a * u_alpha);\n' +
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
  // 그물 그릇 (꼭짓점 VF 개: 자리 3 + 법선 3 + 색 3 + 덧값 4 (터파기 재질 · 어둡기 · 관 옆 거리 둘))
  const VF = 13;
  function meshInit(m) {
    m.key = ''; m.n = 0; m.no = 0; m.ns = 0;
    m.vao = gl.createVertexArray();
    gl.bindVertexArray(m.vao);
    m.vbo = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, m.vbo);
    for (let i = 0; i < 4; i++) { gl.enableVertexAttribArray(i); gl.vertexAttribPointer(i, i < 3 ? 3 : 4, gl.FLOAT, false, VF * 4, i * 12); }
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
    m.n = b.i.length; m.no = b.no || 0; m.ns = b.ns || 0; m.o = b.o; m.fit = b.fit || null;
  }
  // 관 그리기 준비. 안 되면 pprog = null → 겹쳐 그리기에서 선으로 대신한다.
  function initPipeGL() {
    pprog = null;
    try {
      const p = gl.createProgram();
      gl.attachShader(p, compile(gl.VERTEX_SHADER, PVS));
      gl.attachShader(p, compile(gl.FRAGMENT_SHADER, PFS));
      ['a_pos', 'a_nrm', 'a_col', 'a_aux'].forEach((n, i) => gl.bindAttribLocation(p, i, n));
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
      ['a_pos', 'a_nrm', 'a_col', 'a_aux'].forEach((n, i) => gl.bindAttribLocation(p, i, n));
      gl.linkProgram(p);
      if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p) || 'link');
      ['u_off', 'u_f', 'u_r', 'u_u', 'u_tan', 'u_nf', 'u_fade', 'u_alpha', 'u_above', 'u_cam'].forEach((n) => { TU[n] = gl.getUniformLocation(p, n); });
      pitM.forEach(meshInit);
      meshInit(rvM);
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
    if (S.depth && (S.pipes || obShow())) drawTubes(); // 영역 표현은 3D 없음 (땅 위 테두리만 · 겹쳐 그리기)
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
  // 지금 그린 관 그물의 이음 · 곡관 자리 (세계 좌표 · 확인 · 시험)
  glc.__fit = () => { const f = { joints: [], bends: [] }; tubeM.forEach((m) => { if (m.fit && m.n) { f.joints.push(...m.fit.joints); f.bends.push(...m.fit.bends); } }); return f; };
  function obPut() { meshPut(obM, S.cur + '|' + S.cam.join(',') + '|' + obGen, () => buildTubes(() => true, false, obst, obZ)); }
  function drawTubes() {
    const sel = edSel(), base = camBase();
    if (S.pipes) tubesPut(sel, base);
    // 터파기: 구멍을 스텐실에 쓰고 안쪽 면 · 가시설을 그린 뒤, 관도 구멍 안에서만 (스텐실 시험은 관을 그린 뒤 끈다)
    // 하천 (수직구 입구 = 구멍 · 수직구 · 가물막이 = 가시설처럼 땅 위만 밖에서) 도 같은 길 · 터파기 형상을 끄면 하천은 관처럼 다 보임
    const pit = trGL() && drawTrench(base, sel);
    if (!trGL() && rvGL()) { rvPut(base); rvDrawAll(); }
    // 지장물: 구멍 안 = 불투명 (구덩이 벽 · 바닥 · 관과 깊이 견줌 → 드러난 것처럼) · 구멍 밖 땅 속 = 반투명 (X선)
    if (obShow()) obPut();
    const ob = obShow() && obM.n > 0;
    if (ob && pit) drawTubeMesh([obM], 1);
    if (S.pipes && (tubeM[0].n || tubeM[1].n)) drawTubeMesh(tubeM, 1); // 판 구덩이 안 관 = 불투명 (JACK 1124)
    if (pit) gl.disable(gl.STENCIL_TEST);
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
  // 지금 자리에서 FADE1 안의 관 (use(L) 인 것만) 을 원통 그물로 (좌표 = 카메라 기준 m). hi = 고른 관 (밝게).
  // list · zf = 다른 줄 묶음 (지장물 · 관종 없음 = 민 원통) 과 그 관 중심 표고.
  // 관로 (관종 있음): 관종 색 · 6 m 마다 이음 · 꺾인 곳 (3D 로 2° 넘게) 곡관 (관 중심선 자료는 그대로 · 그림만 곡관 안에서 둥글게) ·
  // 이음 · 곡관은 카메라에서 FIT_R 안만 (멀면 2 px 아래). 만든 이음 · 곡관 자리는 out.fit (시험 · 확인).
  function buildTubes(use, hi, list, zf) {
    const o = S.cam.slice(), VB = [], IB = [], fit = { joints: [], bends: [] };
    zf = zf || zcAt;
    for (const L of list || lines) {
      if (!use(L)) continue;
      const bx = Math.max(L.e0 - o[0], 0, o[0] - L.e1), by = Math.max(L.n0 - o[1], 0, o[1] - L.n1);
      if (bx * bx + by * by > FADE1 * FADE1) continue;
      const P = L.pts, cnt = P.length / 3, r = L.dia / 2, mat = L.mat;
      let run = [], rs = [];
      const flush = () => {
        if (run.length >= 6) {
          if (mat) pipeRun(run, rs, r, mat, hi, o, VB, IB, fit);
          else addTube(run, r, hi ? L.rgbHi : L.rgb, VB, IB);
        }
        run = []; rs = [];
      };
      const pushP = (X, Y, Z, s) => {
        const x = X - o[0], y = Y - o[1], z = Z - o[2], n = run.length;
        if (n && Math.abs(run[n - 3] - x) + Math.abs(run[n - 2] - y) + Math.abs(run[n - 1] - z) < 1e-6) return;
        run.push(x, y, z);
        rs.push(s);
      };
      const push = (k) => pushP(P[k * 3], P[k * 3 + 1], zf(L, k), L.cum ? L.cum[k] : 0);
      const rz = !list && L.river && L.river.length ? rvPipeZ(L) : null; // 하천 구간 = 그 깊이로 수평 (구간 끝 = 수직구 · 둑에서 꺾여 내려감)
      if (rz && rz.length) {
        const q = rvPath(L, rz, zf);
        for (let i = 0; i + 1 < q.length; i++) {
          if (distPtSeg(o[0], o[1], q[i][0], q[i][1], q[i + 1][0], q[i + 1][1]) > FADE1) { flush(); continue; }
          if (!run.length) pushP(q[i][0], q[i][1], q[i][2], q[i][3]);
          pushP(q[i + 1][0], q[i + 1][1], q[i + 1][2], q[i + 1][3]);
        }
      } else {
        for (let k = 0; k + 1 < cnt; k++) {
          if (distPtSeg(o[0], o[1], P[k * 3], P[k * 3 + 1], P[k * 3 + 3], P[k * 3 + 4]) > FADE1) { flush(); continue; }
          if (!run.length) push(k);
          push(k + 1);
        }
      }
      flush();
    }
    return { o: o, v: Float32Array.from(VB), i: Uint32Array.from(IB), fit: fit };
  }
  const FIT_R = 120;          // m, 이음 · 곡관을 그리는 거리 (더 멀면 화면에서 2 px 아래)
  const PIPE_LEN = 6;         // m, 관 한 본 (이음 간격)
  const BEND_DEG = 2;         // 이보다 크게 꺾이면 곡관
  const HI_TINT = [1, 0.86, 0.3]; // 고른 관 (관로 고치기) = 노랗게 섞음
  // 관 한 줄 (카메라 기준 점 c · 점마다 그 관 측점 rs): 꺾인 곳 곡관 · 곧은 곳 6 m 이음 · 나머지 관 몸통
  function pipeRun(c, rs, r, mat, hi, o, VB, IB, fit) {
    const tint = (k) => MAT_RGB[mat].map((v, i) => { const q = clamp(v * k, 0, 1); return hi ? q + (HI_TINT[i] - q) * 0.35 : q; });
    const body = tint(1), m = c.length / 3;
    const C = (j) => [c[j * 3], c[j * 3 + 1], c[j * 3 + 2]];
    const near = (p) => Math.hypot(p[0], p[1], p[2]) < FIT_R;
    const dirs = [];
    for (let j = 0; j + 1 < m; j++) { const a = C(j), b = C(j + 1); dirs.push(unit3([b[0] - a[0], b[1] - a[1], b[2] - a[2]])); }
    const len = (j) => { const a = C(j), b = C(j + 1); return Math.hypot(b[0] - a[0], b[1] - a[1], b[2] - a[2]); };
    // 곡관: 꺾인 꼭짓점 j 를 A (앞 마디 Lf 앞) → B (뒤 마디 Lf 뒤) 둥근 길 (V 를 조절점으로 한 2차 곡선) 로
    const bend = new Map();
    for (let j = 1; j + 1 < m; j++) {
      const t1 = dirs[j - 1], t2 = dirs[j], ang = Math.acos(clamp(dot3(t1, t2), -1, 1)) * R2D;
      if (ang <= BEND_DEG || ang > 120 || !near(C(j))) continue;
      const Lf = Math.min(Math.max(0.25, 1.1 * 2 * r), 0.45 * len(j - 1), 0.45 * len(j));
      if (Lf < 0.05) continue;
      const V = C(j), A = V.map((v, i) => v - t1[i] * Lf), B = V.map((v, i) => v + t2[i] * Lf);
      const ns = clamp(Math.ceil(ang / 8), 3, 12), arc = [];
      for (let q = 0; q <= ns; q++) {
        const u = q / ns, a = (1 - u) * (1 - u), b = 2 * (1 - u) * u, d = u * u;
        arc.push([A[0] * a + V[0] * b + B[0] * d, A[1] * a + V[1] * b + B[1] * d, A[2] * a + V[2] * b + B[2] * d]);
      }
      bend.set(j, { A: A, B: B, t1: t1, t2: t2, arc: arc, Lf: Lf });
      fit.bends.push([V[0] + o[0], V[1] + o[1], V[2] + o[2], ang]);
    }
    // 관 몸통: 곡관 자리는 둥근 길로
    const pts = [];
    for (let j = 0; j < m; j++) {
      const bd = bend.get(j);
      if (bd) bd.arc.forEach((p) => pts.push(p[0], p[1], p[2])); else pts.push(...C(j));
    }
    addTube(pts, r, body, VB, IB);
    // 이음 (곧은 곳 · 측점 6 m 마다 · 곡관 · 줄 끝 가까이는 뺌)
    const sMin = rs[0], sMax = rs[m - 1];
    for (let s = Math.ceil((Math.min(sMin, sMax) + 1e-6) / PIPE_LEN) * PIPE_LEN; s < Math.max(sMin, sMax) - 1e-6; s += PIPE_LEN) {
      let j = 0;
      while (j + 2 < m && !((rs[j] - s) * (rs[j + 1] - s) <= 0)) j++;
      const ds = rs[j + 1] - rs[j];
      if (!(Math.abs(ds) > 1e-9)) continue;
      const u = (s - rs[j]) / ds, a = C(j), b = C(j + 1), p = [a[0] + (b[0] - a[0]) * u, a[1] + (b[1] - a[1]) * u, a[2] + (b[2] - a[2]) * u];
      if (!near(p)) continue;
      const da = u * len(j), db = (1 - u) * len(j);
      const ba = bend.get(j), bb = bend.get(j + 1);
      if ((ba ? da < ba.Lf + 0.4 : j === 0 && da < 0.3) || (bb ? db < bb.Lf + 0.4 : j + 2 === m && db < 0.3)) continue;
      joint(p, dirs[j], r, mat, tint, VB, IB);
      fit.joints.push([p[0] + o[0], p[1] + o[1], p[2] + o[2], s]);
    }
    // 곡관 몸통 (조금 굵게) + 양 끝 (주철 = 받는 입 · 강관 = 플랜지 · 비금속 = 융착 띠)
    for (const bd of bend.values()) {
      const pa = bd.A.map((v, i) => v - bd.t1[i] * 0.004), pb = bd.B.map((v, i) => v + bd.t2[i] * 0.004);
      const path = [pa].concat(bd.arc, [pb]), flat = [];
      path.forEach((p) => flat.push(p[0], p[1], p[2]));
      addTube(flat, r * (mat === 'plastic' ? 1.08 : 1.06), tint(mat === 'ci' ? 1.08 : mat === 'steel' ? 0.9 : 0.95), VB, IB);
      fitEnd(bd.A, bd.t1.map((v) => -v), r, mat, tint, VB, IB);
      fitEnd(bd.B, bd.t2, r, mat, tint, VB, IB);
    }
  }
  // 원뿔대 (P + t·u0 반지름 r0 → P + t·u1 반지름 r1 · 옆면 법선은 기울기 따라) · cap0 / cap1 = 그 끝 고리 면 (안 반지름 ri)
  function frustum(P, t, u0, u1, r0, r1, col, VB, IB, cap0, cap1, ri) {
    const N = TUBE_SIDES;
    let A = cross3(t, [0, 0, 1]);
    if (Math.hypot(A[0], A[1], A[2]) < 1e-6) A = cross3(t, [1, 0, 0]);
    A = unit3(A);
    const B = cross3(t, A), k = (r0 - r1) / Math.max(u1 - u0, 1e-6); // 옆면 법선의 길 방향 성분
    const vert = (p, n) => { VB.push(p[0], p[1], p[2], n[0], n[1], n[2], col[0], col[1], col[2], 0, 1, 99, 99); };
    const ring = (u, rr) => { const out = []; for (let s = 0; s < N; s++) { const a = s / N * 2 * Math.PI, d = [Math.cos(a) * A[0] + Math.sin(a) * B[0], Math.cos(a) * A[1] + Math.sin(a) * B[1], Math.cos(a) * A[2] + Math.sin(a) * B[2]]; out.push([P[0] + t[0] * u + d[0] * rr, P[1] + t[1] * u + d[1] * rr, P[2] + t[2] * u + d[2] * rr, d]); } return out; };
    const R0 = ring(u0, r0), R1 = ring(u1, r1), b = VB.length / VF;
    for (const R of [R0, R1]) R.forEach((q) => vert(q, unit3([q[3][0] + t[0] * k, q[3][1] + t[1] * k, q[3][2] + t[2] * k])));
    for (let s = 0; s < N; s++) { const s1 = (s + 1) % N; IB.push(b + s, b + s1, b + N + s1, b + s, b + N + s1, b + N + s); }
    const annulus = (R, u, rIn, sg) => { // 고리 면 (rIn ~ 그 고리) · 법선 = sg·t
      const I = ring(u, rIn), n = [t[0] * sg, t[1] * sg, t[2] * sg], c = VB.length / VF;
      R.forEach((q) => vert(q, n)); I.forEach((q) => vert(q, n));
      for (let s = 0; s < N; s++) { const s1 = (s + 1) % N; if (sg > 0) IB.push(c + s, c + s1, c + N + s1, c + s, c + N + s1, c + N + s); else IB.push(c + s, c + N + s1, c + s1, c + s, c + N + s, c + N + s1); }
    };
    if (cap0) annulus(R0, u0, ri, -1);
    if (cap1) annulus(R1, u1, ri, 1);
  }
  // 곧은 관 이음 (자리 p · 길 방향 t): 주철 = 다음 관의 받는 입 (1.18 D · 입 면 + 짧게 줄어듦) · 강관 = 용접 띠 · 비금속 = 융착 띠
  function joint(p, t, r, mat, tint, VB, IB) {
    if (mat === 'ci') {
      const Lb = 0.08 + 0.25 * 2 * r, rb = 1.18 * r, col = tint(1.3);
      frustum(p, t, 0, Lb, rb, rb, col, VB, IB, true, false, r * 0.999);
      frustum(p, t, Lb, Lb + 0.4 * Lb, rb, r * 1.002, col, VB, IB, false, false);
    } else if (mat === 'steel') {
      frustum(p, t, -0.03, 0.03, r * 1.045, r * 1.045, tint(0.7), VB, IB, true, true, r * 0.999);
    } else {
      frustum(p, t, -0.02, 0.02, r * 1.09, r * 1.09, tint(0.86), VB, IB, true, true, r * 0.999);
    }
  }
  // 곡관 끝 (자리 p · 바깥쪽 = 관 쪽 방향 t): 주철 = 받는 입 · 강관 = 플랜지 (1.45 D) · 비금속 = 융착 띠
  function fitEnd(p, t, r, mat, tint, VB, IB) {
    if (mat === 'ci') {
      const Lb = 0.08 + 0.25 * 2 * r, rb = 1.18 * r, col = tint(1.3);
      frustum(p, t, -0.3 * Lb, 0, r * 1.06, rb, col, VB, IB, false, false);
      frustum(p, t, 0, Lb, rb, rb, col, VB, IB, false, true, r * 0.999);
    } else if (mat === 'steel') {
      const tf = Math.max(0.02, 0.08 * 2 * r);
      frustum(p, t, -tf / 2, tf / 2, r * 1.45, r * 1.45, tint(0.7), VB, IB, true, true, r * 0.999);
    } else {
      frustum(p, t, -0.025, 0.025, r * 1.12, r * 1.12, tint(0.86), VB, IB, true, true, r * 0.999);
    }
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
    const vert = (p, n) => { VB.push(p[0], p[1], p[2], n[0], n[1], n[2], col[0], col[1], col[2], 0, 1, 99, 99); };
    const cap = (cen, ring, n, flip) => {
      const b = VB.length / VF;
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
      const b = VB.length / VF;
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
  // 3D 면은 두 결만 (JACK 1124): 벽 · 사면 · 끝벽 = 깎은 흙 (위 포장 두께만 아스팔트) · 바닥 = 밝은 다진 모래. 층 띠는 단면 그림에만.
  // 가시설 구간 (L.shore) 은 기울기 0 (곧은 벽 · 위 폭 = 아래 폭) · 구간 끝은 곧바로 바뀜 (그 자리 계단 면).
  const TR_TOL = 0.05;
  const TR_PAV = [0.40, 0.40, 0.42], TR_SOIL = [0.47, 0.33, 0.21], TR_FLOOR = [0.80, 0.72, 0.56];
  const LAY_CSS = { pav: '#45484f', fill: '#7d5634', prot: '#f0dfae', bed: '#d8b56a' }; // 다 지은 층 (단면 그림 · 360 단면 덧그림)
  // 단면 값 (관 길 방향에 수직): 관경 D · 지표 g · 관 중심 zc · 더 넓힘 ex · 기울기 n (없으면 TR.n · 가시설 0)
  // → { zs, zb, wb, zt, wv, zm, ws, n }
  function sectAt(D, g, zc, ex, n) {
    n = n === undefined ? TR.n : n;
    const zs = Math.min(zc - D / 2, g - 0.01), zb = zs - TR.bed, wb = D / 2 + TR.margin + (ex || 0), zt = g - TR.tp;
    const wv = wb + n * Math.max(0, zt - zb);
    return zt > zs ? { zs, zb, wb, zt, wv, zm: zt, ws: wb + n * (zs - zb), n } : { zs, zb, wb, zt, wv, zm: zs, ws: wv, n };
  }
  // 보호층 윗면: 관 윗면 + prot (포장 밑 · 모래 윗면 사이로)
  const protTop = (x, zc, D) => Math.max(x.zs, Math.min(zc + D / 2 + TR.prot, x.zt));
  // 층 다각형 (단면 좌표 [w, z] · 볼록 · 오른쪽 아래 → 위 → 왼쪽 위 → 아래). X = { g, zt, zb, zs, wb, wv, zp, n } · zlo 아래는 자름
  function layerPolys(X, zlo) {
    const n = X.n === undefined ? TR.n : X.n;
    const wAt = (z) => (z >= X.zt ? X.wv : X.wb + n * Math.max(0, z - X.zb));
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
  const TR_UP = [0, 0, 1], TR_AUX0 = [0, 1, 99, 99];
  // 색 → 재질 번호 (셰이더 결): 1 포장 자른 면 · 2 벽 흙 · 3 바닥 (그 밖 = 7 관 자른 면 · 가시설은 번호를 따로 줌)
  const TR_MAT = new Map([[TR_PAV, 1], [TR_SOIL, 2], [TR_FLOOR, 3]]);
  const SUN_SH = [0.45, -0.55]; // 높이 1 인 점의 그림자가 바닥에서 밀리는 만큼 (= −해 수평 / 해 높이 · SUN_GL 과 같은 해)
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
    // 끝 k 로 들어가 이어진 줄을 따라간다 → [L, 꼭짓점, L, 꼭짓점, …] · nd.k0 = 꼭짓점마다 그 앞 마디의 시작 꼭짓점 (그 줄 번호 · 처음 −1)
    const walk = (k) => {
      const nd = [], k0 = [];
      while (k >= 0 && !used[k >> 1]) {
        used[k >> 1] = 1;
        const p = ends[k], L = lines[p.i], b = p.s ? L.bi.slice().reverse() : L.bi;
        for (let j = nd.length ? 1 : 0; j < b.length; j++) { nd.push(L, b[j]); k0.push(j ? b[j - 1] : -1); } // 이음 꼭짓점은 한 번만
        k = link[k ^ 1];
      }
      nd.k0 = k0;
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
      return { nd: nd, k0: nd.k0, m: m, P: P, D: nd[0].dia, ids: ids, e0, e1, n0, n1, ext: [true, true], wall: [true, true] };
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
  // 이은 줄마다 단면 꼭짓점. 가시설 구간 끝에서 마디를 나누고, 기울기가 바뀌는 곳은 같은 자리에 꼭짓점 둘 (길이 0 마디 = 계단 면).
  // 자리 E, N (끝은 더 판 만큼 밀림) · 가로 LX, LY (왼쪽 법선의 이등분 · 길이 1/cos) · 마디 방향 TX, TY (그 꼭짓점으로 끝나는 마디 ·
  // 같은 자리 둘째 = 다음 마디) · 지표 G · ST = 가시설 종류 ('' = 없음) · 단면 ZM, ZS, WV, WS, WB.
  // pc = 가시설 조각 [{ va (꼭짓점 va → va+1), ls0, ls1 (그 관 측점), a, b (구간), t }]
  function trSecGet() {
    const ch = trChainsGet();
    const key = [TR.tp, TR.n, TR.margin, TR.bed, TR.prot, S.cover, S.gz].join('|');
    if (trSec && trSecKey === key && trSec.of === ch) return trSec;
    trSecKey = key;
    const out = [], ext = Math.max(TR.margin, 0.05); // 여유폭 0 이어도 끝벽이 관 마구리와 겹치지 않게
    for (const c of ch) {
      const m = c.m, P = c.P, D = c.D;
      const tx = new Float64Array(m - 1), ty = new Float64Array(m - 1);
      for (let j = 0; j + 1 < m; j++) {
        const dx = P[j * 2 + 2] - P[j * 2], dy = P[j * 2 + 3] - P[j * 2 + 1], l = Math.hypot(dx, dy) || 1;
        tx[j] = dx / l; ty[j] = dy / l;
      }
      const lxj = new Float64Array(m), lyj = new Float64Array(m), gj = new Float64Array(m), zj = new Float64Array(m);
      for (let j = 0; j < m; j++) {
        const a = Math.max(0, j - 1), b = Math.min(m - 2, j);
        let qx = -ty[a] - ty[b], qy = tx[a] + tx[b];
        const ql = Math.hypot(qx, qy);
        if (ql < 1e-6) { qx = -ty[b]; qy = tx[b]; } else {
          qx /= ql; qy /= ql;
          const k = 1 / Math.max(-qx * ty[b] + qy * tx[b], 0.3);
          qx *= k; qy *= k;
        }
        lxj[j] = qx; lyj[j] = qy;
        const L = c.nd[j * 2], k = c.nd[j * 2 + 1];
        gj[j] = gAt(L, k); zj[j] = zcAt(L, k);
      }
      // 하천 구간 (river.js · 3D 화면과 같음): 추진 = 터파기 없음 (띠를 끊음 · 끊긴 끝 = 끝벽) · 횡단 (개착) = 관 깊이를 하천 깊이로 (구간 끝 = 단차)
      let V, pc;
      const pieces = [], fresh = () => { V = { PE: [], PN: [], LX: [], LY: [], TX: [], TY: [], G: [], ZC: [], ST: [] }; pc = []; };
      fresh();
      const put = (q, dx, dy, st) => {
        V.PE.push(q[0]); V.PN.push(q[1]); V.LX.push(q[2]); V.LY.push(q[3]); V.G.push(q[4]); V.ZC.push(q[5]);
        V.TX.push(dx); V.TY.push(dy); V.ST.push(st);
      };
      const cut = (tail) => { if (V.PE.length >= 2) pieces.push({ V: V, pc: pc, head: !pieces.length && !cut.any, tail: tail }); cut.any = true; fresh(); };
      cut.any = false;
      const withZ = (q, rz) => (rz == null ? q : [q[0], q[1], q[2], q[3], q[4], rz]);
      let last = null;
      for (let j = 1; j < m; j++) {
        const L = c.nd[j * 2], sa = L.cum[c.k0[j]], sb = L.cum[c.nd[j * 2 + 1]], dx = tx[j - 1], dy = ty[j - 1];
        const rp = L.river && L.river.length ? rvPlans(L) : [];
        const ts = [0, 1]; // 이 마디 안 가시설 · 하천 구간 끝 (그 관 측점 → 마디 매개)
        if ((L.shore.length || rp.length) && Math.abs(sb - sa) > 1e-9) {
          const lo = Math.min(sa, sb), hi = Math.max(sa, sb);
          for (const q of L.shore.concat(rp)) for (const s of [q.a, q.b]) if (s > lo + 1e-6 && s < hi - 1e-6) ts.push((s - sa) / (sb - sa));
          ts.sort((x, y) => x - y);
        }
        // 마디 매개 t 자리 [e, n, lx, ly, g, zc] (끝 = 원래 꼭짓점 · 사이 = 마디 수직)
        const at = (t) => (t <= 0 ? [P[j * 2 - 2], P[j * 2 - 1], lxj[j - 1], lyj[j - 1], gj[j - 1], zj[j - 1]]
          : t >= 1 ? [P[j * 2], P[j * 2 + 1], lxj[j], lyj[j], gj[j], zj[j]]
            : [P[j * 2 - 2] + (P[j * 2] - P[j * 2 - 2]) * t, P[j * 2 - 1] + (P[j * 2 + 1] - P[j * 2 - 1]) * t, -dy, dx,
              gj[j - 1] + (gj[j] - gj[j - 1]) * t, zj[j - 1] + (zj[j] - zj[j - 1]) * t]);
        for (let i = 0; i + 1 < ts.length; i++) {
          const t0 = ts[i], t1 = ts[i + 1];
          if (t1 - t0 < 1e-9) continue;
          const sm = sa + (sb - sa) * (t0 + t1) / 2, rq = rp.length ? rp.find((p) => sm > p.a && sm < p.b) : null;
          if (rq && rq.k === 'jack') { cut(false); last = null; continue; } // 추진 = 땅을 안 팜
          const rz = rq && Number.isFinite(rq.zc) ? rq.zc : null;
          const q = L.shore.length ? shoreAt(L, sm) : null, st = q ? q.t : '', key = st + (rz == null ? '' : '|' + rz);
          if (!last) put(withZ(at(t0), rz), dx, dy, st);
          else if (last.key !== key) put(withZ(last.raw, rz), dx, dy, st); // 기울기 · 깊이 바뀜 → 같은 자리 하나 더
          const raw = at(t1), e = withZ(raw, rz);
          put(e, dx, dy, st);
          last = { raw: raw, key: key };
          if (q) pc.push({ va: V.PE.length - 2, ls0: sa + (sb - sa) * t0, ls1: sa + (sb - sa) * t1, a: q.a, b: q.b, t: st });
        }
      }
      cut(true);
      // 조각마다 단면 · 끝 (줄 처음 · 끝 조각만 더 파고 닿음 따름 · 추진에서 끊긴 끝 = 끝벽) · wall = 끝벽 [처음, 끝]
      for (const pz of pieces) {
        const W = pz.V, n = W.PE.length, F = () => new Float64Array(n);
        const E = Float64Array.from(W.PE), N = Float64Array.from(W.PN), ZM = F(), ZS = F(), WV = F(), WS = F(), WB = F();
        for (let v = 0; v < n; v++) {
          const x = sectAt(D, W.G[v], W.ZC[v], 0, W.ST[v] ? 0 : undefined);
          ZS[v] = x.zs; WV[v] = x.wv; ZM[v] = x.zm; WS[v] = x.ws; WB[v] = x.wb;
        }
        if (pz.head && c.ext[0]) { E[0] -= W.TX[0] * ext; N[0] -= W.TY[0] * ext; }
        if (pz.tail && c.ext[1]) { E[n - 1] += W.TX[n - 1] * ext; N[n - 1] += W.TY[n - 1] * ext; }
        const pad = ext + 10;
        out.push({ c, m: n, D: D, E, N, LX: W.LX, LY: W.LY, TX: W.TX, TY: W.TY, G: W.G, ST: W.ST, wall: [pz.head ? c.wall[0] : true, pz.tail ? c.wall[1] : true],
          ZM, ZS, WV, WS, WB, pc: pz.pc, e0: c.e0 - pad, e1: c.e1 + pad, n0: c.n0 - pad, n1: c.n1 + pad });
      }
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
  // 가시설 색 (재질 번호는 따로): 조립식 판 (밝은 강) · 보강대 (노란 칠) · H형강 · 띠장 · 버팀보 (어두운 강) · 시트파일 (녹슨 강) · 토류판 (나무)
  const SH_PANEL = [0.56, 0.6, 0.64], SH_YEL = [0.88, 0.66, 0.13], SH_HP = [0.34, 0.34, 0.35], SH_SHEET = [0.36, 0.35, 0.33], SH_WOOD = [0.52, 0.37, 0.22];
  const BOX_F = [[0, 1, 2, 3], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]; // 상자 여섯 면 (아래 넷 → 위 넷)
  // 가시설이 굴착면 (벽 반폭 wb) 에서 안쪽으로 나온 두께: 조립식 판 0.1 · H형강 앞 날개 0.03 (말뚝은 굴착선에 박음) · 시트파일 골 0.16
  const SHORE_IN = { panel: 0.1, hpile: 0.03, sheet: 0.16 };
  // 띠장 (H형강 0.2 m) 은 관 둘레 0.15 m 를 비울 때만 (좁은 도랑은 버팀만 · 조립식은 띠장 없음)
  const waleFits = (wb, D, t) => t !== 'panel' && wb - SHORE_IN[t] - 0.2 >= D / 2 + 0.15;
  // 버팀 높이 (가운데): 지표 − 0.6 (관 윗면 + 0.25 보다 위) · 깊이 2.5 m 넘으면 아래 하나 더 (0.8 m 넘게 떨어질 때만) · 둘 다 안 되면 없음
  function strutZ(g, zs, D) {
    const lo = zs + D + 0.25, z1 = Math.max(g - 0.6, lo), out = [];
    if (z1 > g - 0.15) return out;
    out.push(z1);
    if (g - zs > 2.5) { const z2 = Math.max(lo, zs + 0.9); if (z1 - z2 >= 0.8) out.push(z2); }
    return out;
  }
  // 구덩이 그물 짓기 (좌표 = o 기준 m): 구멍 (스텐실) OI · 안쪽 면 II · 가시설 SI → 한 그물 (차례대로).
  // T = 단면 묶음 (trSecGet 하나): E, N, LX, LY (w + 왼쪽) · TX, TY · G, ZM, ZS, WV, WS, WB
  function pitMesh(o) {
    const VB = [], OI = [], II = [], SI = [];
    const vtx = (p, nr, c, x) => { VB.push(p[0], p[1], p[2], nr[0], nr[1], nr[2], c[0], c[1], c[2], x[0], x[1], x[2], x[3]); };
    const pt = (T, j, w, z) => [T.E[j] + T.LX[j] * w - o[0], T.N[j] + T.LY[j] * w - o[1], z - o[2]]; // w + 왼쪽 · − 오른쪽
    const dk = (T, j, z) => 1 - 0.35 * clamp((T.G[j] - z) / Math.max(T.G[j] - T.ZS[j], 1e-3), 0, 1); // 깊을수록 어둡게
    // 볼록 면 (네 점 = 두 대각선의 곱 · 더 많으면 Newell): 법선을 hint 쪽으로 · 재질 (mat · 없으면 색으로) · 꼭짓점 어둡기 sh · to = 번호 묶음 ·
    // lat = 꼭짓점마다 덧값 둘 (바닥 = [관 옆 거리/반지름, 해 그림자 쪽으로 민 거리/반지름] · 가시설 = [99, 지표 표고]) (셰이더가 결 · 빛 · 관 그림자를 그린다)
    const face = (ps, hint, base, sh, to, lat, mat) => {
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
      const b = VB.length / VF, out = to || II, mt = mat || TR_MAT.get(base) || 7;
      ps.forEach((p, i) => { vtx(p, nr, base, [mt, sh[i], lat ? lat[i][0] : 99, lat ? lat[i][1] : 99]); });
      for (let i = 1; i + 1 < ps.length; i++) out.push(b, b + i, b + i + 1);
    };
    const seg = (T, a, b) => {
      // 길이 0 마디 (가시설 구간 끝 · 기울기 바뀜) = 계단 면만: 넓은 쪽 (구덩이가 더 넓은 쪽) 을 본다
      const zl = Math.abs(T.E[b] - T.E[a]) + Math.abs(T.N[b] - T.N[a]) < 1e-9;
      const sd = T.WV[a] > T.WV[b] ? -1 : 1, step = zl ? [sd * T.TX[b], sd * T.TY[b], 0] : null;
      if (!zl) { // 구멍 (지표 ±wv 띠) → 스텐실
        const ob = VB.length / VF;
        [pt(T, a, T.WV[a], T.G[a]), pt(T, b, T.WV[b], T.G[b]), pt(T, b, -T.WV[b], T.G[b]), pt(T, a, -T.WV[a], T.G[a])].forEach((p) => vtx(p, TR_UP, TR_UP, TR_AUX0));
        OI.push(ob, ob + 1, ob + 2, ob, ob + 2, ob + 3);
      }
      for (const sg of [1, -1]) { // 벽: 포장 (직각) · 흙 (기울기 · 가시설 구간 = 곧은 벽)
        const hint = step || [-sg * T.LX[a], -sg * T.LY[a], 0];
        const ta = pt(T, a, sg * T.WV[a], T.G[a]), tb = pt(T, b, sg * T.WV[b], T.G[b]);
        const ma = pt(T, a, sg * T.WV[a], T.ZM[a]), mb = pt(T, b, sg * T.WV[b], T.ZM[b]);
        const ba = pt(T, a, sg * T.WS[a], T.ZS[a]), bb = pt(T, b, sg * T.WS[b], T.ZS[b]);
        face([ta, tb, mb, ma], hint, TR_PAV, [dk(T, a, T.G[a]), dk(T, b, T.G[b]), dk(T, b, T.ZM[b]), dk(T, a, T.ZM[a])]);
        face([ma, mb, bb, ba], hint, TR_SOIL, [dk(T, a, T.ZM[a]), dk(T, b, T.ZM[b]), dk(T, b, T.ZS[b]), dk(T, a, T.ZS[a])]);
      }
      if (zl) return;
      // 바닥 (모래 윗면): 벽 쪽 가장자리를 조금 어둡게 (세 띠) · 관 밑 그림자 (관 중심 높이 r 에서 해 반대쪽으로 민 자리)
      const lat = (j) => { const w = T.WS[j], i = w - Math.min(0.12, 0.3 * w); return [w, i, -i, -w]; };
      const la = lat(a), lb = lat(b), ao = [0.7, 0.82, 0.82, 0.7], r = Math.max(T.D / 2, 0.01);
      const sl = (j) => (SUN_SH[0] * T.LX[j] + SUN_SH[1] * T.LY[j]) / (Math.hypot(T.LX[j], T.LY[j]) || 1);
      const qa = (w) => [w / r, w / r - sl(a)], qb = (w) => [w / r, w / r - sl(b)];
      for (let q = 0; q < 3; q++) {
        face([pt(T, a, la[q], T.ZS[a]), pt(T, b, lb[q], T.ZS[b]), pt(T, b, lb[q + 1], T.ZS[b]), pt(T, a, la[q + 1], T.ZS[a])],
          TR_UP, TR_FLOOR, [ao[q], ao[q], ao[q + 1], ao[q + 1]], null, [qa(la[q]), qb(lb[q]), qb(lb[q + 1]), qa(la[q + 1])]);
      }
    };
    const endWall = (T, j, hint) => {
      const P = (w, z) => pt(T, j, w, z);
      face([P(T.WV[j], T.G[j]), P(T.WV[j], T.ZM[j]), P(-T.WV[j], T.ZM[j]), P(-T.WV[j], T.G[j])], hint, TR_PAV,
        [dk(T, j, T.G[j]), dk(T, j, T.ZM[j]), dk(T, j, T.ZM[j]), dk(T, j, T.G[j])]);
      face([P(T.WV[j], T.ZM[j]), P(T.WS[j], T.ZS[j]), P(-T.WS[j], T.ZS[j]), P(-T.WV[j], T.ZM[j])], hint, TR_SOIL,
        [dk(T, j, T.ZM[j]), dk(T, j, T.ZS[j]), dk(T, j, T.ZS[j]), dk(T, j, T.ZM[j])]);
    };
    // 가시설 (조각 p · 꼭짓점 j → j+1 곧은 마디 · 양쪽 벽 · 바닥 zs ~ 지표 위): 지표 · 바닥 · 벽 반폭은 마디 따라 선형.
    // 자리는 그 관 측점 (p.ls0 → p.ls1) 으로 잰다 → 조각이 나뉘어도 (꺾인 곳) 판 이음 · 말뚝 · 버팀이 같은 자리.
    const shore = (T, j, p) => {
      const ax = T.E[j], ay = T.N[j], dx = T.E[j + 1] - ax, dy = T.N[j + 1] - ay, len = Math.hypot(dx, dy);
      if (len < 1e-6 || Math.abs(p.ls1 - p.ls0) < 1e-6) return;
      const nx = -dy / len, ny = dx / len, lo = Math.min(p.ls0, p.ls1), hi = Math.max(p.ls0, p.ls1), D = T.D;
      const fOf = (s) => (s - p.ls0) / (p.ls1 - p.ls0), du = (m) => m / len; // 측점 → 마디 매개 · m 길이 → 매개
      const lin = (A, f) => A[j] + (A[j + 1] - A[j]) * f, gf = (f) => lin(T.G, f), zsf = (f) => lin(T.ZS, f);
      const zv = (v, f) => (typeof v === 'function' ? v(f) : v);
      // 모서리 [f, 쪽 sg, 벽에서 안쪽 d, z] 여덟 (아래 넷 → 위 넷) → 상자 (면마다 바깥 법선 · 어둡기 = 깊이 · 덧값 = 지표 표고)
      const P3 = (q) => { const w = q[1] * (lin(T.WB, q[0]) - q[2]); return [ax + dx * q[0] + nx * w - o[0], ay + dy * q[0] + ny * w - o[1], q[3] - o[2]]; };
      const box = (cs, col, mat) => {
        const ps = cs.map(P3);
        const gz = cs.map((q) => gf(q[0]));
        const sh = cs.map((q, i) => (q[3] >= gz[i] ? 1 : 1 - 0.32 * clamp((gz[i] - q[3]) / Math.max(gz[i] - zsf(q[0]), 1e-3), 0, 1)));
        const cen = [0, 1, 2].map((k) => ps.reduce((s, q) => s + q[k], 0) / 8);
        for (const F of BOX_F) {
          const fp = F.map((i) => ps[i]), fc = [0, 1, 2].map((k) => fp.reduce((s, q) => s + q[k], 0) / 4 - cen[k]);
          face(fp, fc, col, F.map((i) => sh[i]), SI, F.map((i) => [99, gz[i]]), mat);
        }
      };
      // 벽 따라 상자 (f0 ~ f1 · 쪽 sg · 벽에서 d0 ~ d1 · 높이 z0 ~ z1 = 수 또는 f 의 함수)
      const wbox = (f0, f1, sg, d0, d1, z0, z1, col, mat) => box([
        [f0, sg, d0, zv(z0, f0)], [f1, sg, d0, zv(z0, f1)], [f1, sg, d1, zv(z0, f1)], [f0, sg, d1, zv(z0, f0)],
        [f0, sg, d0, zv(z1, f0)], [f1, sg, d0, zv(z1, f1)], [f1, sg, d1, zv(z1, f1)], [f0, sg, d1, zv(z1, f0)]], col, mat);
      // 가로지르는 상자 (두 벽의 d 사이 · 버팀)
      const xbox = (f0, f1, d, z0, z1, col, mat) => box([
        [f0, 1, d, z0], [f1, 1, d, z0], [f1, -1, d, z0], [f0, -1, d, z0],
        [f0, 1, d, z1], [f1, 1, d, z1], [f1, -1, d, z1], [f0, -1, d, z1]], col, mat);
      const inS = (s, last) => s >= lo - 1e-6 && (s < hi - 1e-6 || (last && s <= hi + 1e-6)); // 조각마다 한 번만 (구간 끝 점은 끝 조각)
      const rnd = (k) => { const x = Math.sin(k * 12.9898 + p.a * 78.233) * 43758.5453; return x - Math.floor(x); };
      // 버팀 (높이마다): 띠장이 들어갈 자리면 띠장 (H형강 0.2 m · 조각 길이 전체) 앞에서 · 아니면 벽 (말뚝 · 시트) 에서 받침판과 함께
      const wf = waleFits(Math.min(T.WB[j], T.WB[j + 1]), D, p.t), din = SHORE_IN[p.t];
      const levels = (f) => strutZ(gf(f), zsf(f), D);
      if (wf) for (const z of levels(fOf((lo + hi) / 2))) for (const sg of [1, -1]) wbox(fOf(lo), fOf(hi), sg, din, din + 0.2, z - 0.1, z + 0.1, SH_HP, 10);
      const strut = (s, w, col, mat) => {
        const f = fOf(s), d = wf ? din + 0.2 : din;
        for (const z of levels(f)) {
          xbox(f - du(w / 2), f + du(w / 2), d, z - w / 2, z + w / 2, col, mat);
          if (!wf) for (const sg of [1, -1]) wbox(f - du(0.15), f + du(0.15), sg, d, d + 0.025, z - 0.15, z + 0.15, col, mat);
        }
      };
      if (p.t === 'panel') {
        // 조립식 간이흙막이 (굴착면 안쪽): 판 2.5 m (이음 틈 · 세로 틀 · 가로 보강 띠 · 깊으면 2.4 m 위로 한 단 더) · 판 가운데마다 노란 보강대 (깊으면 두 단)
        const M = 2.5, TH = SHORE_IN.panel, top = (f) => gf(f) + 0.1;
        for (let k = Math.floor((lo - p.a) / M + 1e-9); p.a + k * M < hi - 1e-6; k++) {
          const m0 = p.a + k * M, m1 = Math.min(m0 + M, p.b), s0 = Math.max(m0, lo), s1 = Math.min(m1, hi);
          if (s1 - s0 < 1e-4) continue;
          const j0 = Math.abs(s0 - m0) < 1e-6, j1 = Math.abs(s1 - m1) < 1e-6; // 판 끝 (이음) · 아니면 꺾인 곳에서 나뉜 자리
          const f0 = fOf(s0 + (j0 ? 0.012 : 0)), f1 = fOf(s1 - (j1 ? 0.012 : 0));
          const deep = Math.max(gf(f0) - zsf(f0), gf(f1) - zsf(f1)) > 2.7, mid = (f) => zsf(f) + 2.4;
          for (const sg of [1, -1]) {
            if (deep) { wbox(f0, f1, sg, 0, TH, zsf, mid, SH_PANEL, 8); wbox(f0, f1, sg, 0, TH, (f) => mid(f) + 0.03, top, SH_PANEL, 8); } else wbox(f0, f1, sg, 0, TH, zsf, top, SH_PANEL, 8);
            const r0 = fOf(s0 + (j0 ? 0.09 : 0)), r1 = fOf(s1 - (j1 ? 0.09 : 0));
            for (let z = 0.45; z + 0.07 < Math.min(gf(f0) - zsf(f0), gf(f1) - zsf(f1)); z += 0.6) {
              wbox(r0, r1, sg, TH, TH + 0.035, (f) => zsf(f) + z, (f) => zsf(f) + z + 0.07, SH_PANEL, 8);
            }
            if (j0) wbox(f0, fOf(s0 + 0.07), sg, 0, TH + 0.045, zsf, top, SH_PANEL, 8);
            if (j1) wbox(fOf(s1 - 0.07), f1, sg, 0, TH + 0.045, zsf, top, SH_PANEL, 8);
          }
          const sc = (m0 + m1) / 2;
          if (inS(sc, false)) strut(sc, 0.12, SH_YEL, 12);
        }
      } else if (p.t === 'hpile') {
        // 토류벽 (굴착선에 박은 H형강 300 · 앞 날개만 안으로 3 cm): 1.8 m 쯤 · 지표 + 0.2 · 사이 토류판 (앞 날개 뒤 · 지표까지) · 버팀 (말뚝 둘에 하나)
        const nP = Math.max(1, Math.round((p.b - p.a) / 1.8)), sp = (p.b - p.a) / nP, fw = du(0.15);
        for (let k = 0; k <= nP; k++) {
          const s = p.a + k * sp;
          if (!inS(s, k === nP)) continue;
          const f = fOf(s), z0 = zsf(f), z1 = gf(f) + 0.2;
          for (const sg of [1, -1]) {
            wbox(f - fw, f + fw, sg, 0.01, 0.03, z0, z1, SH_HP, 10);
            wbox(f - fw, f + fw, sg, -0.27, -0.25, z0, z1, SH_HP, 10);
            wbox(f - du(0.0075), f + du(0.0075), sg, -0.25, 0.01, z0, z1, SH_HP, 10);
          }
        }
        for (let k = 0; k < nP; k++) {
          const b0 = Math.max(p.a + k * sp + 0.11, lo), b1 = Math.min(p.a + (k + 1) * sp - 0.11, hi);
          if (b1 - b0 < 1e-3) continue;
          for (const sg of [1, -1]) {
            const t = 0.9 + 0.2 * rnd(k * 2 + (sg > 0 ? 1 : 0));
            wbox(fOf(b0), fOf(b1), sg, -0.07, 0.01, zsf, gf, [SH_WOOD[0] * t, SH_WOOD[1] * t, SH_WOOD[2] * t], 9);
          }
        }
        for (let k = 1; k <= Math.max(1, nP - 1); k += 2) {
          const s = nP === 1 ? (p.a + p.b) / 2 : p.a + k * sp;
          if (inS(s, false)) strut(s, 0.2, SH_HP, 10);
        }
      } else {
        // 시트파일 (굴착선 안쪽 · U 형 골 0.8 m 마다 · 깊이 0.15 m · 판 두께 12 mm) · 지표 + 0.3 · 버팀 3.2 m 쯤
        const PT = 0.8, T2 = 0.006, d0 = 0.01, d1 = SHORE_IN.sheet, prof = [[0, d0], [0.25, d0], [0.4, d1], [0.65, d1], [0.8, d0]];
        for (let k = Math.floor((lo - p.a) / PT + 1e-9); p.a + k * PT < hi - 1e-6; k++) {
          for (let i = 0; i + 1 < prof.length; i++) {
            const u0 = p.a + k * PT + prof[i][0], u1 = p.a + k * PT + prof[i + 1][0], c0 = Math.max(u0, lo, p.a), c1 = Math.min(u1, hi, p.b);
            if (c1 - c0 < 1e-4) continue;
            const dAt = (u) => prof[i][1] + (prof[i + 1][1] - prof[i][1]) * (u - u0) / (u1 - u0), e0 = dAt(c0), e1 = dAt(c1);
            const f0 = fOf(c0), f1 = fOf(c1), z0 = zsf, z1 = (f) => gf(f) + 0.3;
            for (const sg of [1, -1]) {
              box([[f0, sg, e0 - T2, z0(f0)], [f1, sg, e1 - T2, z0(f1)], [f1, sg, e1 + T2, z0(f1)], [f0, sg, e0 + T2, z0(f0)],
                [f0, sg, e0 - T2, z1(f0)], [f1, sg, e1 - T2, z1(f1)], [f1, sg, e1 + T2, z1(f1)], [f0, sg, e0 + T2, z1(f0)]], SH_SHEET, 11);
              // 골 뒤 (굴착면 ~ 시트 뒷면) 는 흙: 지표 높이 윗면만 (포장 결) → 위에서 빈 틈이 안 보이게
              const tq = [[f0, sg, 0, gf(f0)], [f1, sg, 0, gf(f1)], [f1, sg, e1 - T2, gf(f1)], [f0, sg, e0 - T2, gf(f0)]];
              face(tq.map(P3), TR_UP, TR_PAV, [1, 1, 1, 1], SI, tq.map((q) => [99, q[3]]));
            }
          }
        }
        const nS = Math.max(1, Math.round((p.b - p.a) / 3.2));
        for (let k = 0; k < nS; k++) {
          const s = p.a + (k + 0.5) * (p.b - p.a) / nS;
          if (inS(s, false)) strut(s, 0.2, SH_HP, 10);
        }
      }
    };
    return {
      seg, endWall, shore,
      done: () => ({ o: o, v: Float32Array.from(VB), i: Uint32Array.from(OI.concat(II, SI)), no: OI.length, ns: SI.length }),
    };
  }
  // 지금 자리에서 FADE1 안의 구덩이 그물 (이은 줄 use(c) 인 것만 · 카메라 기준 m): 구멍 삼각형 no 개 뒤에 안쪽 면 · 끝에 가시설 ns 개.
  function buildTrench(use) {
    const o = S.cam.slice(), B = pitMesh(o);
    for (const T of trSecGet()) {
      if (!use(T.c)) continue;
      const bx = Math.max(T.e0 - o[0], 0, o[0] - T.e1), by = Math.max(T.n0 - o[1], 0, o[1] - T.n1);
      if (bx * bx + by * by > FADE1 * FADE1) continue;
      const m = T.m;
      for (let j = 0; j + 1 < m; j++) {
        if (distPtSeg(o[0], o[1], T.E[j], T.N[j], T.E[j + 1], T.N[j + 1]) > FADE1) continue;
        B.seg(T, j, j + 1);
        if (j === 0 && T.wall[0]) B.endWall(T, 0, unit3([T.E[1] - T.E[0], T.N[1] - T.N[0], 0]));
        if (j === m - 2 && T.wall[1]) B.endWall(T, m - 1, unit3([T.E[m - 2] - T.E[m - 1], T.N[m - 2] - T.N[m - 1], 0]));
      }
      for (const p of T.pc) if (distPtSeg(o[0], o[1], T.E[p.va], T.N[p.va], T.E[p.va + 1], T.N[p.va + 1]) <= FADE1) B.shore(T, p.va, p);
    }
    return B.done();
  }
  // 구덩이 그리기. 그린 것이 있으면 스텐실 시험 (구멍 안 = 1) 을 켠 채로 true → 관도 구멍 안에서만.
  // 그물 둘: 고른 관이 든 이은 줄 (끄는 동안 이것만 다시) · 나머지 (이은 모양이 바뀔 때만 다시)
  function drawTrench(base, sel) {
    const tb = base + '|' + [TR.tp, TR.n, TR.margin, TR.bed, TR.prot].join(',') + '|' + pipeGen + '|' + sel;
    meshPut(pitM[0], tb + '|' + (sel >= 0 ? trStaticSig(sel) : ''), () => buildTrench((c) => c.ids.indexOf(sel) < 0));
    meshPut(pitM[1], sel < 0 ? '' : tb + '|' + selGen, () => (sel < 0 ? NO_MESH : buildTrench((c) => c.ids.indexOf(sel) >= 0)));
    rvPut(base);
    return pitDraw(pitM.concat([rvM])); // 하천 = 수직구 입구도 구멍 · 수직구 · 가물막이는 가시설처럼
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
    gl.uniform1f(TU.u_above, 0);
    gl.uniform3f(TU.u_cam, S.cam[0], S.cam[1], S.cam[2]); // 결 = 세계 좌표
  }
  // 구멍 → 스텐실 1 · 안쪽 면 · 가시설 (구멍 안 · 깊이 씀) · 가시설이 땅 위로 나온 곳 (구멍 밖 · 지표 위만). 구멍을 그린 것이 없으면 false (스텐실 끔).
  // 구멍 없는 그물 (하천 가물막이만) 은 땅 위만 그림.
  function pitDraw(list) {
    const ms = list.filter((m) => m.n), hs = ms.filter((m) => m.no);
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
    hs.forEach((m) => { tOff(m); gl.drawElements(gl.TRIANGLES, m.no, gl.UNSIGNED_INT, 0); });
    // 2) 안쪽 면 + 가시설: 구멍 안에서만 · 깊이 씀 (관이 이 깊이와 견줌)
    gl.colorMask(true, true, true, true);
    gl.depthMask(true);
    gl.enable(gl.DEPTH_TEST);
    gl.depthFunc(gl.LESS);
    gl.stencilFunc(gl.EQUAL, 1, 0xff);
    gl.stencilOp(gl.KEEP, gl.KEEP, gl.KEEP);
    gl.enable(gl.BLEND);
    gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ZERO, gl.ONE);
    ms.forEach((m) => { const k = m.n - m.no; if (k > 0) { tOff(m); gl.drawElements(gl.TRIANGLES, k, gl.UNSIGNED_INT, m.no * 4); } });
    // 3) 가시설 땅 위 (구멍 밖 · 지표 아래는 버림)
    const sm = ms.filter((m) => m.ns);
    if (sm.length) {
      gl.stencilFunc(gl.NOTEQUAL, 1, 0xff);
      gl.uniform1f(TU.u_above, 1);
      sm.forEach((m) => { tOff(m); gl.drawElements(gl.TRIANGLES, m.ns, gl.UNSIGNED_INT, (m.n - m.ns) * 4); });
      gl.uniform1f(TU.u_above, 0);
      gl.stencilFunc(gl.EQUAL, 1, 0xff);
    }
    gl.disable(gl.BLEND);
    gl.disable(gl.DEPTH_TEST);
    gl.bindVertexArray(null);
    if (!hs.length) { gl.disable(gl.STENCIL_TEST); return false; }
    return true;
  }

  // ---------------------------------------------------------------- 하천 추진 · 횡단 그물 (river.js build3d 면 → 터파기 셰이더 · 가시설과 같은 결 · 3D 화면과 같은 모양)
  // 수직구 입구 (땅 높이 · 내공) = 구멍 (스텐실) → 그 안에서 수직구 · 케이싱 끝 · 관이 보임 · 밖에서는 땅 위로 나온 것만 (흙막이 머리 · 가물막이).
  // 덧값 = (재질, 어둡기, 99, 그 자리 땅 표고) → 셰이더 u_above 가 땅 아래를 버림. 터파기 형상을 끄면 관처럼 다 보임 (rvDrawAll).
  const RV_MAT = { wood: [SH_WOOD, 9], steel: [SH_HP, 10], hpile: [SH_HP, 10], liner: [[0.56, 0.6, 0.63], 11], conc: [[0.66, 0.66, 0.63], 3],
    case: [[0.42, 0.45, 0.5], 12], bag: [[0.86, 0.85, 0.8], 3], sheet: [SH_SHEET, 11] };
  const rvGL = () => S.pipes && S.depth && !!pprog && !!tprog && rvAny();
  function rvPut(base) { meshPut(rvM, rvGL() ? 'r|' + base + '|' + pipeGen + '|' + selGen : '', () => (rvGL() ? rvBuild() : NO_MESH)); }
  function rvBuild() {
    const o = S.cam.slice(), VB = [], OI = [], SI = [], gm = new Map();
    const gAtXY = (x, y) => { const k = Math.round(x * 20) + ',' + Math.round(y * 20); let g = gm.get(k); if (g === undefined) { g = rvGround(x, y); gm.set(k, g); } return g; };
    const vtx = (p, nr, c, x) => { VB.push(p[0] - o[0], p[1] - o[1], p[2] - o[2], nr[0], nr[1], nr[2], c[0], c[1], c[2], x[0], x[1], x[2], x[3]); };
    const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
    const face = (ps, hint, kind) => {
      const m = RV_MAT[kind] || [[0.7, 0.7, 0.7], 7];
      let nr = ps.length === 4 ? cross3(sub(ps[2], ps[0]), sub(ps[3], ps[1])) : cross3(sub(ps[1], ps[0]), sub(ps[2], ps[0]));
      const l = Math.hypot(nr[0], nr[1], nr[2]);
      if (l < 1e-12) return;
      const sg = dot3(nr, hint) < 0 ? -1 / l : 1 / l;
      nr = [nr[0] * sg, nr[1] * sg, nr[2] * sg];
      const b = VB.length / VF;
      ps.forEach((p) => {
        const g = gAtXY(p[0], p[1]), gz = Number.isFinite(g) ? g : p[2];
        vtx(p, nr, m[0], [m[1], p[2] >= gz ? 1 : 1 - 0.32 * clamp((gz - p[2]) / 6, 0, 1), 99, gz]); // 깊을수록 어둡게
      });
      for (let i = 1; i + 1 < ps.length; i++) SI.push(b, b + i, b + i + 1);
    };
    const hole = (xy) => { // 땅 위 볼록 다각형 → 구멍 (부채꼴 · 법선 위)
      const b = VB.length / VF;
      xy.forEach((p) => { const g = gAtXY(p[0], p[1]); vtx([p[0], p[1], Number.isFinite(g) ? g : p[2]], TR_UP, TR_UP, TR_AUX0); });
      for (let i = 1; i + 1 < xy.length; i++) OI.push(b, b + i, b + i + 1);
    };
    for (const L of lines) {
      for (const pl of rvPlans(L)) {
        if (!Number.isFinite(pl.gMin) || !pl.path) continue;
        const ps = pl.path.between(pl.a, pl.b).map((q) => q[1]);
        let d = Infinity;
        for (let i = 0; i + 1 < ps.length; i++) d = Math.min(d, distPtSeg(o[0], o[1], ps[i].x, ps[i].y, ps[i + 1].x, ps[i + 1].y));
        if (d > FADE1 + 30) continue;
        RV.build3d(pl, face);
        if (pl.k !== 'jack') continue;
        for (const sh of pl.shafts) { // 수직구 입구 (내공 · 땅 높이)
          if (sh.shape === 'rect') {
            const hl = sh.l / 2, hw = sh.w / 2, at = (u, v) => [sh.x + sh.tx * u - sh.ty * v, sh.y + sh.ty * u + sh.tx * v, sh.g];
            hole([at(-hl, -hw), at(hl, -hw), at(hl, hw), at(-hl, hw)]);
          } else {
            const r = sh.w / 2, c = [];
            for (let i = 0; i < 36; i++) { const t = i / 36 * 2 * Math.PI; c.push([sh.x + r * Math.cos(t), sh.y + r * Math.sin(t), sh.g]); }
            hole(c);
          }
        }
      }
    }
    return { o: o, v: Float32Array.from(VB), i: Uint32Array.from(OI.concat(SI)), no: OI.length, ns: SI.length };
  }
  // 터파기 형상을 끈 깊이 보기: 하천 그물을 관처럼 다 (불투명 · 깊이 씀)
  function rvDrawAll() {
    const k = rvM.n - rvM.no;
    if (k <= 0) return;
    tUse(1);
    gl.disable(gl.STENCIL_TEST);
    gl.enable(gl.DEPTH_TEST);
    gl.depthFunc(gl.LESS);
    gl.depthMask(true);
    gl.enable(gl.BLEND);
    gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ZERO, gl.ONE);
    tOff(rvM);
    gl.drawElements(gl.TRIANGLES, k, gl.UNSIGNED_INT, rvM.no * 4);
    gl.disable(gl.BLEND);
    gl.disable(gl.DEPTH_TEST);
    gl.bindVertexArray(null);
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
    cardHide(); // 관 정보 칸은 화면 자리 → 사진이 바뀌면 닫음
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
    const tracePg = (pg) => { ov.moveTo(pg[0], pg[1]); for (let k = 2; k < pg.length; k += 2) ov.lineTo(pg[k], pg[k + 1]); ov.closePath(); };
    // ---- 터파기 영역 (영역 표현): 임시점용 = 지표의 터파기 테두리 (±wv · 가시설 구간 = 곧은 벽 폭) · 옅은 주황 채움 + 주황 점선 (맨 밑).
    //      화면에서 10 px 보다 좁은 조각은 그리지 않음 (멀리서 띠가 뭉개지지 않게) · 영구점용 (관 바깥지름 띠) 은 아래 관로 띠.
    if (S.mode === 'area' && S.pipes && SHOW.tmp && lines.length) {
      const fill = [], edge = [];
      const top = (T, j, sg) => [T.E[j] + T.LX[j] * T.WV[j] * sg, T.N[j] + T.LY[j] * T.WV[j] * sg, T.G[j]];
      for (const T of trSecGet()) {
        const bx = Math.max(T.e0 - Cx, 0, Cx - T.e1), by = Math.max(T.n0 - Cy, 0, Cy - T.n1);
        if (bx * bx + by * by > FADE0 * FADE0) continue;
        for (let j = 0; j + 1 < T.m; j++) {
          const e1 = T.E[j], n1 = T.N[j], e2 = T.E[j + 1], n2 = T.N[j + 1];
          if (distPtSeg(Cx, Cy, e1, n1, e2, n2) > FADE0) continue;
          const zl = Math.abs(e2 - e1) + Math.abs(n2 - n1) < 1e-9; // 가시설 구간 끝 (폭이 바뀌는 곳)
          const L1 = top(T, j, 1), R1 = top(T, j, -1), L2 = top(T, j + 1, 1), R2 = top(T, j + 1, -1);
          const mx2 = (e1 + e2) / 2, my2 = (n1 + n2) / 2, mz = (T.G[j] + T.G[j + 1]) / 2;
          const nx = (T.LX[j] + T.LX[j + 1]) / 2, ny = (T.LY[j] + T.LY[j + 1]) / 2, nl = Math.hypot(nx, ny) || 1;
          const per = hwMin(mx2, my2, mz, nx / nl, ny / nl); // 2 px 가 되는 반폭 (m)
          if (!zl && !(per > 0 && Number.isFinite(per) && (T.WV[j] + T.WV[j + 1]) / per >= 10)) continue; // 화면 폭 10 px 아래
          if (!zl) { put(0, L1); put(1, L2); put(2, R2); put(3, R1); const pg = projPoly(Q); if (pg) fill.push(pg); }
          addEdge({ edge: edge }, L1, L2);
          addEdge({ edge: edge }, R1, R2);
          if (j === 0 && T.wall[0]) addEdge({ edge: edge }, L1, R1);
          if (j === T.m - 2 && T.wall[1]) addEdge({ edge: edge }, L2, R2);
        }
      }
      if (fill.length) {
        ov.beginPath();
        fill.forEach(tracePg);
        ov.globalAlpha = 0.16;
        ov.fillStyle = AREA_TMP;
        ov.fill();
      }
      if (edge.length) {
        ov.lineCap = 'butt';
        ov.setLineDash([7, 5]);
        ov.globalAlpha = 0.95;
        path(edge);
        ov.strokeStyle = 'rgba(8, 10, 14, 0.55)'; ov.lineWidth = 3.4; ov.stroke();
        ov.strokeStyle = AREA_TMP; ov.lineWidth = 1.8; ov.stroke();
        ov.setLineDash([]);
      }
      ov.globalAlpha = 1;
    }
    // ---- 하천 추진 · 횡단 (영역 표현 = 땅 위 자리): 수직구 테두리 · 보이는 단계 가물막이 둑 · 공법 색 (터파기 표현 = 3D 그물)
    if (S.mode === 'area' && S.pipes && RV && rvAny()) {
      const ed = { jack: [], open: [] };
      const ring = (bk, ps) => { for (let i = 0; i + 1 < ps.length; i++) addEdge(bk, ps[i], ps[i + 1]); };
      const gp = (x, y, z0) => { const g = rvGround(x, y); return [x, y, Number.isFinite(g) ? g : z0]; };
      for (const L of lines) {
        if (!L.river.length || Math.max(L.e0 - Cx, 0, Cx - L.e1, L.n0 - Cy, Cy - L.n1) > FADE0) continue;
        for (const pl of rvPlans(L)) {
          if (!Number.isFinite(pl.gMin)) continue;
          const bk = { edge: ed[pl.k] };
          if (pl.k === 'jack') {
            for (const sh of pl.shafts) {
              const c = [];
              if (sh.shape === 'rect') {
                const hl = sh.l / 2, hw = sh.w / 2;
                for (const [uu, vv] of [[-hl, -hw], [hl, -hw], [hl, hw], [-hl, hw], [-hl, -hw]]) c.push(gp(sh.x + sh.tx * uu - sh.ty * vv, sh.y + sh.ty * uu + sh.tx * vv, sh.g));
              } else for (let i = 0; i <= 36; i++) { const t = i / 36 * 2 * Math.PI; c.push(gp(sh.x + sh.w / 2 * Math.cos(t), sh.y + sh.w / 2 * Math.sin(t), sh.g)); }
              ring(bk, c);
            }
          } else for (const lg of pl.stages[pl.q.st - 1].legs) ring(bk, lg.pts.map((p) => [p[0], p[1], p[2]]));
        }
      }
      ov.lineCap = 'round';
      for (const kk of ['open', 'jack']) {
        if (!ed[kk].length) continue;
        path(ed[kk]);
        ov.strokeStyle = 'rgba(8, 10, 14, 0.6)'; ov.lineWidth = 4.2; ov.stroke();
        ov.strokeStyle = RV_COL[kk]; ov.lineWidth = 2.2; ov.stroke();
      }
    }

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
      ov.globalAlpha = 1;
    }

    // ---- 관로: 영역 = 땅 위 관경 폭 띠 (영구점용) · 터파기 = WebGL 관 (여기서는 땅 위 옅은 점선 + 이름표)
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
        // 이름표 자리: 화면 가운데에서 가장 가까운 점 · tp = 그 조각 가운데 토피 (지표 − 관 윗면)
        const cand = (tp) => {
          const sdx = SEG[2] - SEG[0], sdy = SEG[3] - SEG[1], sl = sdx * sdx + sdy * sdy;
          const st = sl > 0 ? clamp(((cx - SEG[0]) * sdx + (cy - SEG[1]) * sdy) / sl, 0, 1) : 0;
          const qx = SEG[0] + st * sdx, qy = SEG[1] + st * sdy, qd = Math.hypot(qx - cx, qy - cy);
          if (qd < best && qx > 10 && qx < w - 10 && qy > 10 && qy < h - 10) { best = qd; lx = qx; ly = qy; ltp = tp; }
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
            if (projSeg(A3, B3, SEG)) {
              cand((ga + gb) / 2 - (z1 + (z2 - z1) * (t0 + t1) / 2) - L.dia / 2);
            }
          }
        }
        close();
        if (best < Infinity && L.label && SHOW.lbl) labels.push({ x: lx, y: ly, text: L.label + (Number.isFinite(ltp) ? ' · 토피 ' + ltp.toFixed(1) + ' m' : ''), color: L.color });
      }
      ov.lineCap = 'round';
      ov.lineJoin = 'round';
      const bks = [...buckets.values()].sort((p, q) => p.sel - q.sel); // 고른 관은 맨 위
      if (!depth) { // 영역: 영구점용 = 관 바깥지름 띠 (관로 색 · 범례 단추로 끔)
        for (const bk of bks) {
          if (!bk.poly.length || !SHOW.perm) continue;
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

    // ---- 터파기 구멍: 안쪽 가장자리 그늘 + 밝은 자른 선 (WebGL 구멍과 같은 점 · 같은 투영)
    const rims = []; // [단면 묶음, 처음 끝벽, 끝 끝벽]
    if (S.pipes && lines.length && trGL()) for (const T of trSecGet()) rims.push([T, T.wall[0], T.wall[1]]);
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
        for (const g of tg.values()) { // 자른 곳: 가는 어두운 선 하나 (깨끗한 절단면 · 흰 테두리 없음)
          path(g.edge);
          ov.globalAlpha = g.a;
          ov.strokeStyle = 'rgba(14, 11, 8, 0.6)'; ov.lineWidth = 1.3; ov.stroke();
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

    // ---- 관로 고치기: 켜자마자 모든 관의 원래 꼭짓점 = 작은 청록 원판 (끌면 그 관을 고르고 바로 옮김)
    S.vtx = [];
    if (ED.on && S.pipes) {
      const disk = (e, n, g, rad) => {
        vc(e, n, g, A3);
        if (A3[2] < 0.3) return null;
        const sx = cx + A3[0] / A3[2] * kx, sy = cy - A3[1] / A3[2] * ky, poly = [];
        if (sx < -20 || sy < -20 || sx > w + 20 || sy > h + 20) return null;
        rad = Math.max(rad, 4.5 * A3[2] / kx);
        for (let q = 0; q < 16; q++) {
          const t = q / 16 * 2 * Math.PI;
          vc(e + rad * Math.cos(t), n + rad * Math.sin(t), g, B3);
          if (B3[2] < NEAR) return null;
          poly.push(cx + B3[0] / B3[2] * kx, cy - B3[1] / B3[2] * ky);
        }
        return { sx: sx, sy: sy, poly: poly };
      };
      for (const L of lines) {
        if (L.src === ED.sel) continue;
        const bx = Math.max(L.e0 - Cx, 0, Cx - L.e1), by = Math.max(L.n0 - Cy, 0, Cy - L.n1);
        if (bx * bx + by * by > FADE1 * FADE1) continue;
        L.ctrl.forEach((k, i) => {
          const d = disk(L.pts[k * 3], L.pts[k * 3 + 1], gAt(L, k), MARK_R);
          if (d) S.vtx.push(Object.assign(d, { src: L.src, i: i }));
        });
      }
      ov.beginPath();
      for (const hd of S.vtx) { const p = hd.poly; ov.moveTo(p[0], p[1]); for (let q = 2; q < p.length; q += 2) ov.lineTo(p[q], p[q + 1]); ov.closePath(); }
      ov.fillStyle = PIN2;
      ov.fill();
      ov.strokeStyle = PIN_DARK;
      ov.lineWidth = 1.5;
      ov.stroke();
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

    // ---- 가시설 시작점 (찍는 중): 땅 위 노란 고리
    if (SH.a) {
      vc(SH.a.e, SH.a.n, SH.a.g, A3);
      if (A3[2] > NEAR) {
        ov.beginPath();
        ov.arc(cx + A3[0] / A3[2] * kx, cy - A3[1] / A3[2] * ky, 6, 0, 2 * Math.PI);
        ov.lineWidth = 4; ov.strokeStyle = PIN_DARK; ov.stroke();
        ov.lineWidth = 2.2; ov.strokeStyle = PIN; ov.stroke();
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
      if (ED.on) { // 관로 고치기: 손잡이 · 꼭짓점이 먼저, 그다음 사진 원판(누르면 그 사진으로 · 1124 JACK), 그다음 관 고르기
        if (handleAt('v', x, y) < 0 && !vtxAt('v', x, y)) { const m = markAt(x, y); if (m >= 0) { openShot(m); return; } }
        edTap('v', x, y);
        return;
      }
      if (AL.on) { alPhoto(x, y); return; }
      if (SH.arm) { shTap('v', x, y); return; } // 가시설 찍는 동안은 원판 눌러 옮겨 가지 않음
      const i = markAt(x, y);
      if (i >= 0) { openShot(i); return; }
      if (!pipeTap('v', x, y)) cardHide(); // 관 누름 = 관 정보
    },
    hover: (x, y) => {
      flushView();
      if (ED.on) { edHover('v', x, y); return; }
      const i = AL.on || SH.arm ? -1 : markAt(x, y); // 점 찍는 동안 · 가시설 찍는 동안은 원판을 키우지 않음
      ovc.classList.toggle('point', i >= 0 || (!AL.on && pickMode() && lineAt('v', x, y) >= 0));
      if (i !== S.hover) { S.hover = i; needView = true; kick(); }
    },
  });
  ovc.addEventListener('pointerleave', () => {
    if (ED.on) { edHover('v', -1e9, -1e9); return; }
    if (S.hover >= 0) { S.hover = -1; ovc.classList.remove('point'); needView = true; kick(); }
  });
  // 두 번 누르기: 관로 고치기 동안 = 고른 관에 꼭짓점 넣기 · 아니면 관 위 = 관로 고치기 (그 관 고름)
  ovc.addEventListener('dblclick', (e) => {
    const r = ovc.getBoundingClientRect(), x = e.clientX - r.left, y = e.clientY - r.top;
    flushView();
    if (ED.on) { edInsert('v', x, y); return; }
    if (AL.on || SH.arm || !S.pipes || markAt(x, y) >= 0) return;
    const src = lineAt('v', x, y);
    if (src < 0) return;
    edMode(true);
    if (!ED.on) return;
    ED.sel = src;
    cardHide();
    dirty();
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
    if (e.key === 'Escape' && !$('rvDlg').hidden && !SH.arm) { e.preventDefault(); rvShow(false); return; } // 하천 종단 창 (찍는 중이면 찍기 취소 먼저)
    if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT' || t.isContentEditable)) return;
    if (e.key === 'Escape' && SH.arm) { e.preventDefault(); shCancel(); return; } // 가시설: 찍던 것 취소
    if (e.key === 'Escape' && TM.on) { e.preventDefault(); tmMode(false); return; } // 사진점 지우기 취소
    if (ED.on && edKey(e)) { e.preventDefault(); return; } // 관로 고치기: Esc · Delete · Ctrl+Z / Y (화살표는 그대로 이동)
    const nav = !(e.altKey || e.ctrlKey || e.metaKey), dir = Object.prototype.hasOwnProperty.call(ARROW, e.key) ? ARROW[e.key] : null;
    if (nav && dir !== null) { e.preventDefault(); moveDir(dir); }
    else if (nav && e.key === 'PageUp') { e.preventDefault(); stepShot(-1); }
    else if (nav && e.key === 'PageDown') { e.preventDefault(); stepShot(1); }
    else if (e.key === 'Escape' && AL.on) { // 반쯤 만든 짝 취소 · 없으면 끝
      e.preventDefault();
      if (AL.pend) { AL.pend = null; alUI(); dirty(); } else alMode(false);
    } else if (e.key === 'Escape' && ugEsc()) e.preventDefault(); // 관 정보 닫기
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
    $('prev').disabled = $('vPrev').disabled = S.cur <= 0;
    $('next').disabled = $('vNext').disabled = S.cur >= shots.length - 1;
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

  // ---------------------------------------------------------------- 지하 시설물 칸: 표현 (영역 · 터파기) · [단면] = 터파기 설정 창 · 표시 항목 (터파기만) · 평균매설깊이
  const MODE_BTN = { area: 'mSurf', trench: 'mDepth' };
  function setMode(m) {
    if (MODES.indexOf(m) < 0) m = 'area';
    S.mode = m;
    S.depth = m === 'trench';
    if (m === 'area' && !S.pipes) setPipes(true); // 영역엔 관로 단추가 없음 → 늘 보이게
    cardHide();
    modeUI();
    dirty();
  }
  function modeUI() {
    Object.keys(MODE_BTN).forEach((k) => setOn($(MODE_BTN[k]), S.mode === k));
    $('app').dataset.mode = S.mode;
    $('ugShow').hidden = S.mode !== 'trench'; // 표시 항목은 터파기에서만
    $('ugLegend').hidden = S.mode !== 'area'; // 영역 범례 (임시점용 · 영구점용 · 누르면 켜고 끔)
  }
  Object.keys(MODE_BTN).forEach((k) => $(MODE_BTN[k]).addEventListener('click', () => setMode(k)));
  const pickMode = () => S.pipes && !!SH.arm; // 관을 눌러 쓰는 때 (가시설 찍기 · 손가락 표시)
  function showSet(k, on) {
    SHOW[k] = !!on;
    lsSet(SHOW_KEY, SHOW);
    ugUI();
    dirty();
  }
  function ugUI() {
    setOn($('tgPit'), trOn());
    setOn($('tgLbl'), SHOW.lbl);
    setOn($('lgTmp'), SHOW.tmp);
    setOn($('lgPerm'), SHOW.perm);
    const d = trDefault();
    ['ugTag', 'secTag'].forEach((id) => { const t = $(id); t.textContent = d ? '가시화 기본값' : '직접 입력'; t.classList.toggle('user', !d); });
  }
  $('tgPit').addEventListener('click', () => trSet({ on: !trOn() }));
  $('tgLbl').addEventListener('click', () => showSet('lbl', !SHOW.lbl));
  $('lgTmp').addEventListener('click', () => showSet('tmp', !SHOW.tmp));
  $('lgPerm').addEventListener('click', () => showSet('perm', !SHOW.perm));
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
  $('bTrDef').addEventListener('click', () => { trSet(Object.assign({}, TR_DEF)); toast('가시화 기본값'); });   // 터파기 · 점용 카테고리

  // ---------------------------------------------------------------- 오른쪽 칸 = 지도 + 접는 카테고리 (JACK 1126 «윗줄 단추 → 옆 옵션 창 · 접이식 카테고리 · 360 도 3D 와 같게»)
  // #opts > section.cat[data-cat] > .catH (머리줄 = 펴고 접기) + .catB (몸 · hidden = 접힘) · 펴고 접힌 상태 = 이 PC 에 기억 (rv_360:cats) · 틀은 map3d.js 와 같음.
  // 몸이 예전 칸 그대로인 것: 가시설 (몸 = shBox · 머리줄 = tgShore) · 맞춤 (칸 = calPane · 머리줄 = calHead · 몸 = calBody · 펴짐 = calPane.open)
  // hook(on) = 펴고 접을 때 그 카테고리가 할 일 · 새 카테고리 (하천 추진 · 횡단 등) 는 html 만 넣으면 됨. 펴고 접으면 지도 칸 크기가 바뀜 → resizeAll.
  const CAT_KEY = 'rv_360:cats', CAT_DEF = { view: true, pipe: true, trench: true, cal: !!lsGet('rv_ui:calOpen') };
  const CAT = {};
  const catSaved = (() => { const v = lsGet(CAT_KEY); return v && typeof v === 'object' ? v : {}; })();
  const catIs = (name) => !!CAT[name] && !CAT[name].body.hidden;
  function catUI(c, on) {
    c.body.hidden = !on;
    c.head.setAttribute('aria-expanded', String(on));
    c.sec.classList.toggle('open', on);
  }
  function catSet(name, on) {
    const c = CAT[name];
    if (!c) return;
    on = !!on;
    catUI(c, on);
    catSaved[name] = on;
    lsSet(CAT_KEY, catSaved);
    if (on && $('side').classList.contains('hidden')) sideShow(true);
    if (c.hook) c.hook(on);
    resizeAll();
  }
  document.querySelectorAll('#opts .cat').forEach((sec) => {
    const name = sec.dataset.cat, head = sec.querySelector('.catH'), body = sec.querySelector('.catB');
    if (!name || !head || !body) return;
    const c = CAT[name] = { sec: sec, head: head, body: body, hook: null };
    catUI(c, name in catSaved ? !!catSaved[name] : !!CAT_DEF[name]);
    head.setAttribute('aria-controls', body.id || (body.id = 'cb_' + name));
    head.addEventListener('click', () => { const on = body.hidden; catSet(name, on); if (on) sec.scrollIntoView({ block: 'nearest' }); });   // 편 것이 칸 아래면 보이게
  });
  // 단추 id → 그 카테고리를 펴고 오른쪽 칸을 띄워 보이게 굴림 (시험)
  function reveal(id) {
    const el = typeof id === 'string' ? $(id) : id, sec = el && el.closest('#opts .cat');
    if (!sec) return false;
    if (!catIs(sec.dataset.cat) && el !== CAT[sec.dataset.cat].head) catSet(sec.dataset.cat, true);
    if ($('side').classList.contains('hidden')) sideShow(true);
    el.scrollIntoView({ block: 'nearest' });
    return true;
  }
  CAT.cal.hook = (on) => lsSet('rv_ui:calOpen', on);
  function sideShow(on) {
    $('side').classList.toggle('hidden', !on);
    setOn($('tgMap'), on);
    resizeAll();
  }
  $('tgMap').addEventListener('click', () => sideShow($('side').classList.contains('hidden')));
  // 조작법 (윗줄 ? · 밖 누르기 · Esc = 닫기)
  const helpPop = $('helpPop');
  function helpShow(on) { helpPop.hidden = !on; setOn($('bHelp'), on); }
  $('bHelp').addEventListener('click', () => helpShow(helpPop.hidden));
  window.addEventListener('pointerdown', (e) => { if (!helpPop.hidden && !helpPop.contains(e.target) && !$('bHelp').contains(e.target)) helpShow(false); }, true);
  window.addEventListener('keydown', (e) => { if (e.key === 'Escape' && !helpPop.hidden) { helpShow(false); e.stopImmediatePropagation(); } }, true);
  // 보기 카테고리 (360 에만: 사진 넘기기 · 바로 아래 · 처음 시점 = 찍은 방향 · 45° 아래 · 90° · 북쪽 · 그림 저장)
  $('vPrev').addEventListener('click', () => stepShot(-1));
  $('vNext').addEventListener('click', () => stepShot(1));
  $('vDown').addEventListener('click', () => { S.pitch = -89; dirty(); });
  $('vHome').addEventListener('click', () => { const s = shots[S.want >= 0 ? S.want : S.cur]; if (s) S.yaw = s.yaw; S.pitch = -45; S.fov = 90; dirty(); });
  $('vNorth').addEventListener('click', () => { S.yaw = 0; dirty(); });
  $('vShot').addEventListener('click', shotSave);
  // 화면 그림 저장 (360 · 겹쳐 그린 관로 포함): 프로그램 = save_image (저장 창) · 브라우저 = 내려받기 (3D 화면과 같음)
  function shotSave() {
    needView = false; updView(); drawGL(); drawOverlay();   // 같은 차례에서 읽음 (WebGL 그림이 지워지기 전)
    const c = document.createElement('canvas');
    c.width = glc.width; c.height = glc.height;
    const g = c.getContext('2d');
    g.fillStyle = '#000'; g.fillRect(0, 0, c.width, c.height);
    g.drawImage(glc, 0, 0); g.drawImage(ovc, 0, 0);
    const url = c.toDataURL('image/png'), d = new Date(), z2 = (v) => String(v).padStart(2, '0'), s = shots[S.cur];
    const name = String(PRJ.title || '로드뷰').replace(/[\\/:*?"<>|]+/g, '_') + '_' + (s ? s.id + '_' : '') + d.getFullYear() + z2(d.getMonth() + 1) + z2(d.getDate()) + '_' + z2(d.getHours()) + z2(d.getMinutes()) + '.png';
    if (HOST()) {
      hostCall('save_image', { name: name, data: url }).then((r) => {
        if (r && r.ok) toast('저장 · ' + r.path, () => hostCall('copy_text', String(r.path)).then(() => toast('복사됨'), () => copyText(String(r.path))));
        else if (r) toast(r.msg || '저장 안 됨');
      }, (e) => toast(errText(e)));
    } else {
      const a = document.createElement('a');
      a.href = url; a.download = name; document.body.appendChild(a); a.click(); a.remove();
      toast('내려받기 · ' + name);
    }
  }

  // ---------------------------------------------------------------- 터파기 설정 창
  // 고칠 때마다 단면 그림과 360 (터파기 구덩이 · 영역 테두리) 을 바로 다시 그린다. 값은 프로젝트 설정 (맞춤과 같이 저장).
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
  // ctx = 관 정보 칸에서 연 그 자리 단면 { src, k, t } (실제 표고 · 가시설) · 없으면 고른 관경 · 평균매설깊이 (관경을 바꾸면 이것으로)
  let secCtx = null;
  function secShow(on, ctx) {
    if (on && RV && !$('rvDlg').hidden) rvShow(false); // 같은 자리 창 (하천 종단) 은 닫음
    secDlg.hidden = !on;
    setOn($('tgSec'), on);
    secCtx = on && ctx ? ctx : null;
    if (on) { secOptions(); if (secCtx) { const L = lines.find((q) => q.src === secCtx.src); if (L) secDia.value = String(Math.round(L.dia * 1000)); } secUI(); }
  }
  function secDraw() {
    const L = secCtx && lines.find((q) => q.src === secCtx.src), C = L && secCtx.k < L.pts.length / 3 - 1 ? secAt(L, secCtx.k, secCtx.t) : null;
    secDlg.dataset.at = C ? String(round(C.s, 2)) : ''; // 그 자리 단면 (측점 · 확인 · 시험)
    if (C) { secPic(secSvg, { D: C.D, col: L.color, zc: C.zc - C.g, el: C.g, W: 380, shore: C.shore }); return; }
    const mm = Number(secDia.value) || 300, D = mm / 1000;
    const L0 = lines.find((q) => Math.round(q.dia * 1000) === mm);
    secPic(secSvg, { D: D, col: L0 ? L0.color : '#00e5ff', zc: -S.cover - D / 2, el: null, W: 380 });
  }
  // 단면 그림 (실제 비율, SVG): 지표 = 0 · 다 지은 층 (포장 복구 · 되메우기 · 보호층 · 기초) · 관 · 터파기 윤곽 · 치수.
  // o = { D, col, zc (지표 기준 관 중심, 아래 −), el (지반고 EL → 오른쪽에 EL 글 · 없으면 null), W (그림 폭), shore (가시설 종류 · 곧은 벽 · 없으면 null) }
  function secPic(svg, o) {
    const D = o.D, mm = Math.round(D * 1000), t = TR, zc = o.zc, top = zc + D / 2, c = -top, W = o.W || 380, el = o.el, sh = o.shore || null;
    const x = sectAt(D, 0, zc, 0, sh ? 0 : undefined), pb = zc - D / 2, zb = x.zb, wb = x.wb, zt = x.zt, wv = x.wv, zm = Math.max(zt, zb);
    const X = Object.assign({}, x, { g: 0, zc: zc, D: D, zp: protTop(x, zc, D) });
    const wAt = (z) => (z >= zt ? wv : wb + x.n * Math.max(0, z - zb));
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
    if (sh) { // 가시설 (양쪽 벽 · 3D 와 같은 치수): 조립식 판 · H형강 + 토류판 · 시트파일 → (띠장) · 버팀
      const R = (a0, a1, z0, z1, f) => '<rect class="sh" x="' + n1(Xs(Math.min(a0, a1))) + '" y="' + n1(Y(Math.max(z0, z1))) + '" width="' + n1(Math.abs(a1 - a0) * sc) +
        '" height="' + n1(Math.abs(z1 - z0) * sc) + '" fill="' + f + '" stroke="#141414" stroke-width="0.6"/>';
      const din = SHORE_IN[sh], wf = waleFits(wb, D, sh), lv = strutZ(0, x.zs, D), th = sh === 'panel' ? 0.12 : 0.2, d = wf ? din + 0.2 : din;
      for (const sg of [1, -1]) {
        const at = (dd) => sg * (wb - dd); // 벽에서 안쪽 dd
        if (sh === 'panel') s += R(at(0), at(din), x.zs, 0.1, '#8f99a3');
        else if (sh === 'hpile') s += R(at(-0.27), at(0.03), zb - 0.2, 0.2, '#4b4f55') + R(at(-0.07), at(0.01), x.zs, 0, '#9a6b3f');
        else s += R(at(0.01), at(din), zb - 0.2, 0.3, '#3b3f45');
        if (wf) for (const z of lv) s += R(at(din), at(din + 0.2), z - 0.1, z + 0.1, '#6b7077');
      }
      for (const z of lv) s += R(-(wb - d), wb - d, z - th / 2, z + th / 2, sh === 'panel' ? '#e0a526' : '#6b7077');
    }
    // 치수
    s += dimH(Xs(-wv), Xs(wv), Y(0) - 16, '폭 ' + f2(2 * wv), -6);
    s += dimV(14, Y(0), Y(zb), '깊이 ' + f2(-zb), 'start', 6);
    if (t.tp > 0) s += dimV(Xs(-wv) - 10, Y(0), Y(-t.tp), '포장 ' + f2(t.tp), 'end', -6);
    if (X.zp > top + 1e-6) s += dimV(Xs(-wAt(X.zp)) - 10, Y(X.zp), Y(top), '보호 ' + f2(X.zp - top), 'end', -6);
    if (t.bed > 0) s += dimV(Xs(-wAt(pb)) - 10, Y(zb), Y(pb), '기초 ' + f2(t.bed), 'end', -6);
    if (c > 0) s += dimV(Xs(wv) + (el != null ? 14 : 26), Y(0), Y(-c), '토피 ' + f2(c), 'start', 6);
    s += ln(Xs(D / 2), Y(zc), Xs(D / 2), Y(zb) + 18, '#5d5d5d') + dimH(Xs(D / 2), Xs(wb), Y(zb) + 14, '여유 ' + f2(t.margin), 14);
    if (zm > zb + 1e-6 && x.n > 0) s += txt(Xs(wAt((zm + zb) / 2)) - 6, Y((zm + zb) / 2) + 4, '1:' + round(x.n, 3), 'end');
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
    secDia.addEventListener('change', () => { secCtx = null; secDraw(); });
    secOn.addEventListener('change', () => trSet({ on: secOn.checked }));
    $('secDef').addEventListener('click', () => { trSet(Object.assign({}, TR_DEF)); toast('가시화 기본값'); });
    $('secClose').addEventListener('click', () => secShow(false));
    $('secX').addEventListener('click', () => secShow(false));
    $('tgSec').addEventListener('click', () => secShow(secDlg.hidden));
  })();

  // ---------------------------------------------------------------- 관 누르기: 관 정보 · 그 자리 단면
  // 값은 사진과 무관하게 자료로 (지표 = g, 없으면 groundAt · 관 중심 = hasZ 면 z, 아니면 지표 − 평균매설깊이 − 관경/2 · 꼭짓점 사이 선형).
  // 토피 = 지표 − 관 윗면 · 관 상단고 = 중심 + D/2 · 관 하단고 = 중심 − D/2 (관 바깥 기준) · 터파기 = 터파기 구덩이와 같은 식 (sectAt).
  function secAt(L, k, t) {
    const P = L.pts, ga = gQ(L, k), gb = gQ(L, k + 1), za = zcQ(L, k), zb = zcQ(L, k + 1);
    const g = ga + (gb - ga) * t, zc = za + (zb - za) * t, D = L.dia;
    let s = 0;
    for (let j = 0; j < k; j++) s += Math.hypot(P[j * 3 + 3] - P[j * 3], P[j * 3 + 4] - P[j * 3 + 1]);
    const dx = P[k * 3 + 3] - P[k * 3], dy = P[k * 3 + 4] - P[k * 3 + 1], dl = Math.hypot(dx, dy) || 1;
    s += t * dl;
    const sq = shoreAt(L, s), x = sectAt(D, g, zc, 0, sq ? 0 : undefined), zp = protTop(x, zc, D), top = zc + D / 2; // 가시설 구간 = 곧은 벽
    return {
      L: L, k: k, t: t, s: s, e: P[k * 3] + dx * t, n: P[k * 3 + 1] + dy * t, nx: -dy / dl, ny: dx / dl, color: L.color,
      g: g, zc: zc, top: top, bot: zc - D / 2, cover: g - top, D: D, depth: g - x.zb, wtop: 2 * x.wv, wbot: 2 * x.wb,
      bed: TR.bed, prot: Math.max(0, zp - top), X: Object.assign({}, x, { g: g, zc: zc, D: D, zp: zp }), shore: sq ? sq.t : null,
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
  // 관 정보 (작은 칸): 관경 · 관종 (주철 · 강관 · 비금속 고르기 → RV_PIPES.lines[i].mat · 저장) · 토피 · 관 중심고 · 지반고 · [단면] = 그 자리 터파기 단면 창
  const PC = { src: -1, e: 0, n: 0, k: 0, t: 0 };
  function cardHide() { $('pipeCard').hidden = true; PC.src = -1; }
  function cardShow(L, pk, at) {
    const C = secAt(L, pk.k, pk.t), card = $('pipeCard');
    Object.assign(PC, { src: L.src, e: C.e, n: C.n, k: pk.k, t: pk.t });
    $('pcName').textContent = L.base || L.label || dTxt(L.dia);
    kvRows($('pcRows'), [
      ['dia', '관경', L.dia, dTxt(L.dia) + (L.diaKnown ? '' : ' ?')],
      ['cover', '토피', C.cover, mTxt(C.cover)],
      ['zc', '관 중심고', C.zc, elTxt(C.zc)],
      ['g', '지반고', C.g, elTxt(C.g)],
    ].concat(L.hasZ ? [] : [['', '관 표고', '', '평균매설깊이']]), 'pc_');
    const k = document.createElement('span'), v = document.createElement('span');
    k.className = 'k'; k.textContent = '관종';
    v.className = 'v pcMat'; v.id = 'pc_mat'; v.dataset.v = L.mat;
    MAT_T.forEach((m) => {
      const b = document.createElement('button');
      b.type = 'button'; b.dataset.m = m; b.textContent = MAT_SHORT[m]; b.title = MAT_NAME[m]; b.setAttribute('aria-label', MAT_NAME[m]);
      setOn(b, L.mat === m);
      b.addEventListener('click', () => matSet(L.src, m));
      v.appendChild(b);
    });
    $('pcRows').insertBefore(v, $('pcRows').children[2]);
    $('pcRows').insertBefore(k, v);
    Object.assign(card.dataset, { src: L.src, e: C.e, n: C.n });
    card.hidden = false;
    if (at) {
      card.style.bottom = '';
      card.style.left = clamp(at[0] + 16, 8, Math.max(8, V.w - card.offsetWidth - 8)) + 'px';
      card.style.top = clamp(at[1] - card.offsetHeight / 2, 8, Math.max(8, V.h - card.offsetHeight - 8)) + 'px';
    } else { card.style.left = '12px'; card.style.top = ''; card.style.bottom = '64px'; }
  }
  // 관종 바꾸기 (관로 고치기와 같은 저장 · 되돌리기) → 3D 관 · 이음 · 곡관 다시
  function matSet(src, m) {
    const raw = window.RV_PIPES && Array.isArray(window.RV_PIPES.lines) ? window.RV_PIPES.lines[src] : null;
    if (!raw || MAT_T.indexOf(m) < 0) return;
    const before = JSON.stringify(raw);
    raw.mat = m;
    const after = JSON.stringify(raw);
    if (after === before) return;
    edPut(src);
    edCommit(src, before, after);
    const v = $('pc_mat');
    if (v && PC.src === src) { v.dataset.v = m; v.querySelectorAll('button').forEach((b) => setOn(b, b.dataset.m === m)); }
  }
  $('pcX').addEventListener('click', cardHide);
  $('pcSec').addEventListener('click', () => secShow(true, { src: PC.src, k: PC.k, t: PC.t }));
  // 관 누름 (360 · 지도): 관이 있으면 관 정보 칸 · true
  function pipeTap(view, x, y) {
    if (!S.pipes || !lines.length || S.cur < 0) return false;
    const src = lineAt(view, x, y);
    if (src < 0) return false;
    const L = lines.find((q) => q.src === src), pk = L && pickOf(view, L, x, y);
    if (!pk) return false;
    cardShow(L, pk, view === 'v' ? [x, y] : null);
    return true;
  }
  // Esc: 관 정보 칸 닫기
  function ugEsc() {
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
      calLabels[k] = lab;
      if (DZ_LOCK && k === 'dz') { // 높이 = 수치지형도 (고칠 칸 없음)
        const v = document.createElement('span');
        v.id = 'calDzLock'; v.className = 'clock'; v.textContent = '수치지형도로 맞춰짐';
        box.append(lab, v);
        return;
      }
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
    // 맞춤 칸 펴고 접기 = 오른쪽 칸 카테고리 틀 (머리줄 calHead · 기억 rv_360:cats · 옛 rv_ui:calOpen 도 같이 적음)
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
    CAL_F.forEach((k) => calLabels[k].classList.toggle('mod', !(DZ_LOCK && k === 'dz') && Math.abs(c[k] - calFileVal(vid, k)) > 1e-9));
    alUI(); // 점 짝 어긋남도 지금 값으로
  }
  function updCalUI() {
    const vid = curVid(), c = calibFor(vid);
    $('calVid').textContent = '영상 ' + (vid || '-');
    CAL_F.forEach((k) => {
      if (calInputs[k] && document.activeElement !== calInputs[k]) calInputs[k].value = String(round(c[k], 3));
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
  const M = { cx: 0.5, cy: 0.5, z: 3, w: 0, h: 0, dpr: 1, fitted: false, hov: -1 }; // hov = 지도에서 마우스 올린 사진
  const DOT_GAP = 12; // px, 지도 사진 점 사이 최소 (이보다 가까우면 숨김)
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
  const paths = [];
  function pathsBuild() {
    const by = new Map();
    shots.forEach((s) => {
      if (!Number.isFinite(s.mx)) return;
      if (!by.has(s.video)) by.set(s.video, []);
      by.get(s.video).push(s);
    });
    paths.length = 0;
    paths.push(...by.values());
  }
  pathsBuild();

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
    // 터파기 영역 (영역 표현): 임시점용 테두리 (실제 크기 · 옅은 주황 + 점선) · 폭이 3 px 보다 좁은 줌에서는 안 그림
    if (S.mode === 'area' && S.pipes && SHOW.tmp && SIM) {
      const pm = Math.hypot(SIM.ar, SIM.ai) * k; // 1 m → px
      const at = (e, n) => { const m = en2m(e, n); return [m[0] * k + ox, m[1] * k + oy]; };
      const top = (T, j, sg) => at(T.E[j] + T.LX[j] * T.WV[j] * sg, T.N[j] + T.LY[j] * T.WV[j] * sg);
      const fillP = new Path2D(), edgeP = new Path2D();
      let any = false;
      for (const T of trSecGet()) {
        for (let j = 0; j + 1 < T.m; j++) {
          if ((T.WV[j] + T.WV[j + 1]) * pm < 3) continue;
          const L1 = top(T, j, 1), L2 = top(T, j + 1, 1), R1 = top(T, j, -1), R2 = top(T, j + 1, -1);
          const xs = [L1[0], L2[0], R1[0], R2[0]], ys = [L1[1], L2[1], R1[1], R2[1]];
          if (Math.max(...xs) < -20 || Math.min(...xs) > w + 20 || Math.max(...ys) < -20 || Math.min(...ys) > h + 20) continue;
          // 감는 방향을 하나로 (nonzero 채우기에서 겹친 곳이 구멍이 되지 않게)
          const cw = (L2[0] - L1[0]) * (R1[1] - L1[1]) - (L2[1] - L1[1]) * (R1[0] - L1[0]) > 0;
          const pg = cw ? [L1, L2, R2, R1] : [L1, R1, R2, L2];
          fillP.moveTo(pg[0][0], pg[0][1]); pg.slice(1).forEach((p) => fillP.lineTo(p[0], p[1])); fillP.closePath();
          edgeP.moveTo(L1[0], L1[1]); edgeP.lineTo(L2[0], L2[1]);
          edgeP.moveTo(R1[0], R1[1]); edgeP.lineTo(R2[0], R2[1]);
          if (j === 0 && T.wall[0]) { edgeP.moveTo(L1[0], L1[1]); edgeP.lineTo(R1[0], R1[1]); }
          if (j === T.m - 2 && T.wall[1]) { edgeP.moveTo(L2[0], L2[1]); edgeP.lineTo(R2[0], R2[1]); }
          any = true;
        }
      }
      if (any) {
        mx.globalAlpha = 0.22;
        mx.fillStyle = AREA_TMP;
        mx.fill(fillP);
        mx.globalAlpha = 1;
        mx.lineCap = 'butt';
        mx.setLineDash([5, 4]);
        mx.strokeStyle = AREA_TMP;
        mx.lineWidth = 1.5;
        mx.stroke(edgeP);
        mx.setLineDash([]);
        mx.lineCap = 'round';
      }
    }
    // 가시설 구간 (관 밑 굵은 띠 · 종류 색)
    if (S.pipes) {
      mx.lineCap = 'butt';
      for (const L of lines) {
        if (!L.mx || !L.shore.length) continue;
        const lw = clamp(L.dia * 10, 2, 8) + 7;
        for (const q of L.shore) {
          mx.beginPath();
          mapSub(L, q.a, q.b, k, ox, oy);
          mx.strokeStyle = 'rgba(8, 10, 14, 0.75)'; mx.lineWidth = lw + 2.5; mx.stroke();
          mx.strokeStyle = SHORE_COL[q.t]; mx.lineWidth = lw; mx.stroke();
        }
      }
      mx.lineCap = 'round';
    }
    // 하천 추진 · 횡단 (관 밑 굵은 띠 · 공법 색 · 추진 = 점선) + 수직구 테두리 · 보이는 단계 가물막이 둑 (땅 위 자리)
    if (S.pipes && RV) {
      mx.lineCap = 'butt';
      const mp = (x, y) => { const m = en2m(x, y); return [m[0] * k + ox, m[1] * k + oy]; };
      for (const L of lines) {
        if (!L.mx || !L.river.length) continue;
        const lw = clamp(L.dia * 10, 2, 8) + 7;
        for (const q of L.river) {
          mx.beginPath();
          mapSub(L, q.a, q.b, k, ox, oy);
          mx.setLineDash(q.k === 'jack' ? [7, 4] : []);
          mx.strokeStyle = 'rgba(8, 10, 14, 0.75)'; mx.lineWidth = lw + 2.5; mx.stroke();
          mx.strokeStyle = RV_COL[q.k]; mx.lineWidth = lw; mx.stroke();
        }
        mx.setLineDash([]);
        if (!SIM) continue;
        for (const pl of rvPlans(L)) {
          if (!Number.isFinite(pl.gMin)) continue;
          mx.beginPath();
          const ring = (ps) => { ps.forEach((p, i) => { const s = mp(p[0], p[1]); if (i) mx.lineTo(s[0], s[1]); else mx.moveTo(s[0], s[1]); }); };
          if (pl.k === 'jack') {
            for (const sh of pl.shafts) {
              const c = [];
              if (sh.shape === 'rect') {
                const hl = sh.l / 2, hw = sh.w / 2;
                for (const [u, v] of [[-hl, -hw], [hl, -hw], [hl, hw], [-hl, hw], [-hl, -hw]]) c.push([sh.x + sh.tx * u - sh.ty * v, sh.y + sh.ty * u + sh.tx * v]);
              } else for (let i = 0; i <= 36; i++) { const t = i / 36 * 2 * Math.PI; c.push([sh.x + sh.w / 2 * Math.cos(t), sh.y + sh.w / 2 * Math.sin(t)]); }
              ring(c);
            }
          } else for (const lg of pl.stages[pl.q.st - 1].legs) ring(lg.pts);
          mx.strokeStyle = 'rgba(8, 10, 14, 0.75)'; mx.lineWidth = 3.6; mx.stroke();
          mx.strokeStyle = RV_COL[pl.k]; mx.lineWidth = 1.8; mx.stroke();
        }
      }
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
    // 사진 점: 찍은 차례 따라 바로 앞에 그린 점과 화면에서 DOT_GAP px 넘게 떨어진 것만 (줌이 낮아도 점이 따로 보이게 · JACK 1124).
    // 영상마다 처음 · 끝 · 지금 사진 · 마우스 올린 것 · 사진점 지우기에서 고른 것은 늘 그림. 누르기는 숨긴 점도 (가장 가까운 사진).
    const showNo = M.z >= 19.5; // 사진 간격 약 5 m: 줌 20 쯤부터 번호가 안 겹친다
    const dot = M.z >= 17 ? 4 : 3, trim = TM.on ? new Set(TM.ids) : null;
    mx.font = '600 11px Malgun Gothic, Segoe UI, sans-serif';
    mx.textAlign = 'left';
    mx.textBaseline = 'middle';
    let lx = NaN, ly = NaN, drawn = 0, skip = 0;
    for (let i = 0; i < shots.length; i++) {
      const s = shots[i];
      if (!Number.isFinite(s.mx)) continue;
      const x = s.mx * k + ox, y = s.my * k + oy, cut = !!trim && trim.has(s.id);
      const ends = i === 0 || shots[i - 1].video !== s.video || i + 1 === shots.length || shots[i + 1].video !== s.video;
      const keep = ends || i === S.cur || i === M.hov || i === TM.a || cut;
      const out = x < -20 || y < -20 || x > w + 20 || y > h + 20;
      if (!keep && Math.hypot(x - lx, y - ly) < DOT_GAP) { if (!out) skip++; continue; }
      lx = x; ly = y;
      if (out || i === S.cur) continue;
      drawn++;
      mx.beginPath();
      mx.arc(x, y, i === M.hov ? dot + 1.5 : dot, 0, 2 * Math.PI);
      mx.fillStyle = cut ? CF_RED : '#fff';
      mx.fill();
      mx.strokeStyle = i === TM.a ? CF_RED : 'rgba(8, 10, 14, 0.8)';
      mx.lineWidth = i === TM.a ? 3 : 1.5;
      mx.stroke();
      if (showNo) {
        mx.lineWidth = 3;
        mx.strokeStyle = 'rgba(8, 10, 14, 0.8)';
        mx.strokeText(String(s.seq), x + 7, y);
        mx.fillStyle = '#fff';
        mx.fillText(String(s.seq), x + 7, y);
      }
    }
    mc.dataset.dots = String(drawn); // 그린 점 · 화면 안에서 숨긴 점 (확인 · 시험)
    mc.dataset.view = [M.cx, M.cy, M.z].join(','); // 지도 가운데 (메르카토르 0 ~ 1) · 줌
    mc.dataset.skip = String(skip);
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
    // 가시설 시작점 (찍는 중)
    const SL = SH.a && lines.find((q) => q.src === SH.a.src);
    if (SL && SL.mx) {
      const p = mapAtS(SL, SH.a.s, k, ox, oy);
      mx.beginPath();
      mx.arc(p[0], p[1], 6, 0, 2 * Math.PI);
      mx.lineWidth = 4; mx.strokeStyle = PIN_DARK; mx.stroke();
      mx.lineWidth = 2.2; mx.strokeStyle = PIN; mx.stroke();
    }
    // 관로 고치기: 켜자마자 모든 관의 원래 꼭짓점 = 작은 청록 네모 (끌면 그 관을 고르고 바로 옮김)
    if (ED.on && S.pipes) {
      mx.beginPath();
      for (const L of lines) {
        if (!L.mx || L === EL) continue;
        for (const q of L.ctrl) {
          const x = L.mx[q] * k + ox, y = L.my[q] * k + oy;
          if (x > -10 && y > -10 && x < w + 10 && y < h + 10) mx.rect(x - 3, y - 3, 6, 6);
        }
      }
      mx.fillStyle = PIN2;
      mx.fill();
      mx.strokeStyle = PIN_DARK;
      mx.lineWidth = 1.2;
      mx.stroke();
    }
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
      if (ED.on) { // 관로 고치기: 손잡이 · 꼭짓점이 먼저, 그다음 사진 점(누르면 그 사진으로 · 1124 JACK), 그다음 관 고르기
        if (handleAt('m', x, y) < 0 && !vtxAt('m', x, y)) { const s = shotAtMap(x, y); if (s >= 0) { openShot(s); return; } }
        edTap('m', x, y);
        return;
      }
      if (AL.on && AL.pend) { alMap(x, y); return; }
      if (SH.arm) { shTap('m', x, y); return; }
      if (TM.on) { tmTap(x, y); return; } // 사진점 지우기: 점 둘
      const i = shotAtMap(x, y);
      if (i >= 0) openShot(i);
      else if (AL.on) toast('① 사진에서 먼저');
      else pipeTap('m', x, y);
    },
    hover: (x, y) => {
      if (ED.on) { edHover('m', x, y); return; }
      const hv = SH.arm ? -1 : shotAtMap(x, y);
      if (hv !== M.hov) { M.hov = hv; mapDirty(); } // 마우스 올린 사진 점은 늘 그림 (숨긴 점이어도)
      mc.classList.toggle('point', !(AL.on && AL.pend) && (hv >= 0 || (!AL.on && pickMode() && lineAt('m', x, y) >= 0)));
    },
  });
  mc.addEventListener('pointerleave', () => { if (ED.on) edHover('m', -1e9, -1e9); if (M.hov >= 0) { M.hov = -1; mapDirty(); } });
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

  // ---------------------------------------------------------------- 사진점 지우기 (지도 · 프로그램만 · 쓸모없는 앞뒤 비행 자르기)
  // [가위] → 사진 점 하나 → 같은 영상 다른 점 → 그 사이 (둘 포함 · 찍은 차례) 를 빨갛게 · «N개 지우기» → 프로그램 trim_shots(ids)
  // (project.js · 사진 파일 · trim.json) → 이 목록에서도 뺌 (남은 사진 id 는 그대로 · 사이가 비어도 됨). 2개보다 적게는 안 남김. Esc = 취소.
  function tmMode(on) {
    on = !!on && !!HOST();
    if (on) { if (ED.on) edMode(false); if (AL.on) alMode(false); shCancel(); }
    TM.on = on; TM.a = -1; TM.b = -1; TM.ids = [];
    mc.classList.toggle('pick', on);
    tmUI();
    mapDirty();
  }
  function tmTap(x, y) {
    if (TM.busy) return;
    const i = shotAtMap(x, y);
    if (i < 0) { toast('사진 점 누르기'); return; }
    if (TM.a < 0 || TM.b >= 0) { TM.a = i; TM.b = -1; TM.ids = [shots[i].id]; } else if (shots[i].video !== shots[TM.a].video) { toast('같은 영상 점 누르기'); return; } else {
      const v = shots[TM.a].video, lo = Math.min(TM.a, i), hi = Math.max(TM.a, i);
      TM.b = i;
      TM.ids = shots.slice(lo, hi + 1).filter((s) => s.video === v).map((s) => s.id);
    }
    tmUI();
    mapDirty();
  }
  function tmUI() {
    const chip = $('tmChip'), n = TM.b >= 0 ? TM.ids.length : 0;
    chip.classList.toggle('show', TM.on);
    setOn($('tgTrim'), TM.on);
    $('tmTip').textContent = TM.a < 0 ? '첫 점' : TM.b < 0 ? '끝 점' : '';
    $('tmGo').hidden = !n;
    $('tmGo').textContent = n + '개 지우기';
    $('tmGo').disabled = TM.busy;
    chip.dataset.n = String(n); // 지울 사진 수 (확인 · 시험)
  }
  function tmGo() {
    if (!TM.on || TM.b < 0 || TM.busy) return;
    const ids = TM.ids.slice();
    if (shots.length - ids.length < 2) { toast('사진 2개는 남김'); return; }
    TM.busy = true;
    tmUI();
    hostCall('trim_shots', ids).then((r) => {
      TM.busy = false;
      if (!r || !r.ok) { tmUI(); toast(String((r && r.msg) || '못 지움')); return; }
      shotsRemove(ids);
      tmMode(false);
      toast(String(r.msg || ids.length + '개 지움'));
    }, (e) => { TM.busy = false; tmUI(); toast(errText(e)); });
  }
  // 사진 목록에서 뺌 (번호 다시 · 칸 · 비행 경로) · 지금 사진을 지웠으면 찍은 차례로 가장 가까운 남은 사진으로
  function shotsRemove(ids) {
    const del = new Set(ids.map(String)), cur = S.cur >= 0 ? shots[S.cur] : null;
    const keep = shots.filter((q) => !del.has(q.id));
    if (keep.length === shots.length || keep.length < 1) return;
    let next = cur && !del.has(cur.id) ? cur : null;
    if (!next && cur) { let bd = Infinity; for (const q of keep) { const d = Math.abs(q.i - cur.i); if (d < bd) { bd = d; next = q; } } }
    shots.length = 0;
    keep.forEach((q, i) => { q.i = i; shots.push(q); });
    if (Array.isArray(PRJ.shots)) PRJ.shots = PRJ.shots.filter((q) => !(q && q.id != null && del.has(String(q.id))));
    shotById.clear();
    shots.forEach((q) => shotById.set(q.id, q));
    cellsBuild();
    pathsBuild();
    S.hover = -1; M.hov = -1; S.marks = [];
    if (next === cur) { S.cur = cur.i; S.want = cur.i; updInfo(); dirty(); return; }
    S.cur = -1; S.want = -1;
    dirty();
    if (next) openShot(next.i);
  }
  $('tgTrim').addEventListener('click', () => tmMode(!TM.on));
  $('tmGo').addEventListener('click', tmGo);
  $('tmEnd').addEventListener('click', () => tmMode(false));

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
    if (on) shCancel();
    AL.on = !!on;
    AL.pend = null;
    if (AL.on && pairsOf(curVid()).length) alRun(curVid()); // 이미 찍은 짝 → 지금 자료 (땅 높이 등) 로 한 번 다시 맞춤
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

  // ---------------------------------------------------------------- 프로그램 창: [관로 넣기] · [지장물 넣기] (관로 카테고리)
  const dlgBack = $('dlgBack');
  function dlgOpen() { return !dlgBack.hidden; }
  function dlgShow(id) {
    dlgBack.hidden = false;
    ['pipeDlg', 'obstDlg'].forEach((k) => { $(k).hidden = k !== id; });
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

  // 웹 공유 카테고리 (JACK 1126 «웹 공유 따로» · NAS · 프로그램 안에서만): 올리기 (export_web → job 을 0.5 초마다 → 진행 막대) · 주소 복사 · 열기 · 상태
  // 상태 = 프로그램의 올린 기록 (list_shares · 열쇠 = 프로젝트 폴더) 에서 이 폴더를 찾음 · 없으면 «올리기 전» (3D 화면 map3d.js 와 같음)
  // 결과물 카테고리: 폴더로 내보내기 (export_folder)
  const EX = { job: null, folder: false };
  function exState(st, pct, line, url) {
    $('exProg').hidden = false;
    $('exBar').style.width = clamp(num(pct, 0), 0, 100) + '%';
    $('exLine').textContent = line || '';
    $('exProg').classList.toggle('fail', st === 'fail');
    $('exProg').classList.toggle('done', st === 'done');
    $('exProg').dataset.st = st;
    $('exWeb').disabled = st === 'run';
    if (st === 'done' && url) webSet(String(url), 0);
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
  function projDir() { // 이 로드뷰 폴더 (file:// 일 때만 · 올린 기록의 열쇠)
    try {
      if (location.protocol !== 'file:') return '';
      let p = decodeURIComponent(location.pathname).replace(/\/[^/]*$/, '');
      if (/^\/[A-Za-z]:/.test(p)) p = p.slice(1);
      return (location.host ? '//' + location.host : '') + p;
    } catch (e) { return ''; }
  }
  const normPath = (s) => String(s || '').replace(/[\\/]+/g, '/').replace(/\/$/, '').toLowerCase();
  function webSet(url, n) {
    const u = String(url || ''), st = $('webState');
    $('exUrl').value = u;
    $('exDone').hidden = !u;
    $('exCopy').disabled = !u; $('exOpen').disabled = !u;
    st.textContent = u ? '올림' + (n ? ' · 파일 ' + n + '개' : '') : '올리기 전';
    st.classList.toggle('on', !!u);
    $('webTag').textContent = u ? '올림' : '';
    $('exWeb').querySelector('span').textContent = u ? '다시 올리기' : '올리기';
  }
  function webGet() {
    const h = HOST(), me = normPath(projDir());
    if (!h || typeof h.list_shares !== 'function' || !me || EX.job != null) return Promise.resolve(null);
    return Promise.resolve().then(() => h.list_shares()).then((L) => {
      const hit = (Array.isArray(L) ? L : []).find((x) => x && normPath(x.path) === me);
      if (hit && hit.url) webSet(hit.url, hit.files); else if (!$('exUrl').value) webSet('', 0);
      return hit || null;
    }, () => null);
  }
  CAT.web.hook = (on) => { if (on) webGet(); };
  window.addEventListener('pywebviewready', webGet);
  $('exWeb').addEventListener('click', () => {
    if (EX.job != null) return;
    if (!catIs('web')) catSet('web', true);
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
  $('exOpen').addEventListener('click', () => { const u = $('exUrl').value; if (u) hostCall('open_url', u).catch(() => copyText(u)); });
  webSet('', 0);
  webGet();
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
  function cellsBuild() {
    shotCell.clear();
    shots.forEach((s) => {
      const k = Math.floor(s.e / ED_R) + ',' + Math.floor(s.n / ED_R);
      if (!shotCell.has(k)) shotCell.set(k, []);
      shotCell.get(k).push(s);
    });
  }
  cellsBuild();
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
    // 가시설 구간은 새 길이 안으로 (길이 0 이 되면 지움)
    if (Array.isArray(l.shore)) {
      let len = 0;
      for (let i = 1; i < pts.length; i++) len += Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]);
      l.shore = l.shore.filter((q) => q && isNum(q.a) && isNum(q.b)).map((q) => ({ a: round(clamp(Math.min(+q.a, +q.b), 0, len), 2), b: round(clamp(Math.max(+q.a, +q.b), 0, len), 2), t: q.t }))
        .filter((q) => q.b - q.a >= 0.01);
      if (!l.shore.length) delete l.shore;
    }
    if (Array.isArray(l.river) && RV) { // 하천 구간도 새 관 길이 안으로 (너무 짧아지면 뺌 · 3D 화면과 같음)
      let len = 0;
      for (let i = 1; i < pts.length; i++) len += Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]);
      l.river = l.river.map((q) => RV.clean(q, len)).filter((q) => q);
      if (!l.river.length) delete l.river;
    }
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
    if (secCtx && !secDlg.hidden) secDraw(); // 열린 그 자리 단면 (가시설 · 관 고침) 바로 다시
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
  // 다른 관 (고르지 않은 것) 의 원래 꼭짓점 { src, i } · 없으면 null
  function vtxAt(view, x, y) {
    if (!ED.on) return null;
    let best = null, bd = Infinity;
    if (view === 'v') {
      for (const hd of S.vtx) {
        const d = Math.hypot(x - hd.sx, y - hd.sy);
        if ((d < 9 || inPoly(hd.poly, x, y)) && d < bd) { bd = d; best = { src: hd.src, i: hd.i }; }
      }
    } else {
      const k = mk(), ox = M.w / 2 - M.cx * k, oy = M.h / 2 - M.cy * k;
      for (const L of lines) {
        if (L.src === ED.sel || !L.mx) continue;
        L.ctrl.forEach((q, i) => {
          const d = Math.max(Math.abs(L.mx[q] * k + ox - x), Math.abs(L.my[q] * k + oy - y));
          if (d <= 7 && d < bd) { bd = d; best = { src: L.src, i: i }; }
        });
      }
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
    let hi = handleAt(view, x, y);
    if (hi < 0) { // 다른 관 꼭짓점 → 그 관을 고르고 바로 그 점을 끈다
      const v = vtxAt(view, x, y);
      if (v) { ED.sel = v.src; ED.hov = -1; ED.hi = hi = v.i; dirty(); }
    }
    const L = edLine();
    if (!L) return;
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
    d.sh0 = Array.isArray(raw.shore) ? JSON.stringify(raw.shore) : null; // 끄는 동안 가시설은 처음 값에서 다시 자름
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
    if (d.sh0) raw.shore = JSON.parse(d.sh0);
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
    const v = vtxAt(view, x, y);
    if (v) { ED.sel = v.src; ED.hi = v.i; ED.hov = -1; dirty(); return; }
    const src = lineAt(view, x, y);
    if (src >= 0) {
      if (src !== ED.sel) { ED.sel = src; ED.hi = -1; ED.hov = -1; }
    } else if (ED.hi >= 0) ED.hi = -1; else ED.sel = -1;
    dirty();
  }
  function edHover(view, x, y) {
    const el = view === 'v' ? ovc : mc, hi = handleAt(view, x, y), v = hi < 0 && vtxAt(view, x, y);
    const shot = hi < 0 && !v ? (view === 'v' ? markAt(x, y) : shotAtMap(x, y)) : -1; // 사진 점도 눌러 옮겨 감
    el.classList.toggle('move', hi >= 0 || !!v);
    el.classList.toggle('point', hi < 0 && !v && (shot >= 0 || lineAt(view, x, y) >= 0));
    if (view === 'v' && shot !== S.hover) { S.hover = shot; needView = true; kick(); }
    if (view === 'm' && shot !== M.hov) { M.hov = shot; mapDirty(); }
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
    if (on) shCancel();
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
    shCancel();
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
    const bs = [$('edShp'), $('pmShpOut'), $('bShpOut')];
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
  $('bShpOut').addEventListener('click', edShp);   // 관로 카테고리 [SHP 내보내기]
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

  // ---------------------------------------------------------------- 가시설 (흙막이 · 관 위 두 점 = 구간)
  // [가시설] → 종류 (조립식 간이흙막이 · 토류벽 흙막이 · 시트파일) → 관 위 시작점 → 같은 관 끝점 (360 · 지도) → RV_PIPES.lines[i].shore 에 { a, b, t }.
  // 측점 = 그 줄 첫 점부터 평면 길이 (0.01 m) · 저장 · 되돌리기 = 관로 고치기와 같음 (프로그램 save_pipes · 밖 = 이 브라우저).
  // 겹치면 새 구간이 이김 (옛 구간을 자름) · Esc = 찍던 것 취소.
  // 가시설 카테고리 (머리줄 tgShore · 몸 shBox): 펴고 접기 = 카테고리 틀 · 접으면 찍던 것 취소
  const shBox = $('shBox'), shChip = $('shChip');
  CAT.shore.hook = (on) => {
    setOn($('tgShore'), on);
    if (!on && SH.arm && SHORE_T.indexOf(SH.arm) >= 0) shCancel(); // 하천 찍기는 하천 카테고리가
    SH.sig = '';
    shUI();
  };
  setOn($('tgShore'), !shBox.hidden);
  function shArm(t) {
    if (SHORE_T.indexOf(t) < 0) return;
    if (!lines.length) { toast('관로 없음'); return; }
    if (ED.on) edMode(false);
    if (AL.on) alMode(false);
    if (!S.pipes) setPipes(true);
    SH.arm = t;
    SH.a = null;
    cardHide();
    shUI();
    dirty();
  }
  function shCancel() {
    if (!SH.arm && !SH.a) return;
    SH.arm = null;
    SH.a = null;
    [ovc, mc].forEach((el) => el.classList.remove('point'));
    shUI();
    dirty();
  }
  // 관 위 누름 (360 · 지도): 시작점 → 끝점 (같은 관) → 구간
  function shTap(view, x, y) {
    const src = S.pipes ? lineAt(view, x, y) : -1, L = src >= 0 ? lines.find((q) => q.src === src) : null, pk = L && pickOf(view, L, x, y);
    if (!pk) { toast('관 위 누르기'); return; }
    const s = round(L.cum[pk.k] + (L.cum[pk.k + 1] - L.cum[pk.k]) * pk.t, 2), p = pickPoint(L, pk);
    if (!SH.a) { SH.a = { src: src, s: s, e: p[0], n: p[1], g: p[3] }; shUI(); dirty(); return; }
    if (src !== SH.a.src) { toast('같은 관 누르기'); return; }
    if (Math.abs(s - SH.a.s) < 0.1) { toast('너무 짧음'); return; }
    const a = Math.min(s, SH.a.s), b = Math.max(s, SH.a.s), t = SH.arm, s0 = SH.a.s;
    SH.arm = null;
    SH.a = null;
    [ovc, mc].forEach((el) => el.classList.remove('point'));
    if (t.indexOf('r:') === 0) { rvAdd(src, s0, s, t.slice(2)); shUI(); dirty(); return; } // 하천 추진 · 횡단 (시점 = 처음 누른 점)
    shEdit(src, (list) => {
      const out = [];
      for (const q of list) { // 새 구간과 겹친 곳은 자름
        if (!(q.b > a && q.a < b)) { out.push(q); continue; }
        if (q.a < a) out.push({ a: q.a, b: a, t: q.t });
        if (q.b > b) out.push({ a: b, b: q.b, t: q.t });
      }
      return out.concat([{ a: a, b: b, t: t }]);
    });
    toast(SHORE_NAME[t] + ' · ' + (b - a).toFixed(2) + ' m');
  }
  // 한 줄의 가시설 고치기: f(옛 목록 · RV_PIPES 차례 그대로) → 새 목록 → 다듬어 (a < b · 0.01 m · 측점 차례) 저장 · 되돌리기 · 다시 그림
  function shEdit(src, f) {
    const raw = window.RV_PIPES && Array.isArray(window.RV_PIPES.lines) ? window.RV_PIPES.lines[src] : null;
    if (!raw) return;
    const before = JSON.stringify(raw);
    const next = f((Array.isArray(raw.shore) ? raw.shore : []).map((q) => Object.assign({}, q)))
      .filter((q) => q && isNum(q.a) && isNum(q.b) && SHORE_T.indexOf(q.t) >= 0)
      .map((q) => ({ a: round(Math.min(+q.a, +q.b), 2), b: round(Math.max(+q.a, +q.b), 2), t: q.t }))
      .filter((q) => q.b - q.a >= 0.01).sort((p, q) => p.a - q.a);
    if (next.length) raw.shore = next; else delete raw.shore;
    const after = JSON.stringify(raw);
    if (after === before) return;
    edPut(src);
    edCommit(src, before, after);
    shUI();
  }
  // 줄 L 의 측점 s 자리 (나눈 점 마디 k · t)
  function lineAtS(L, s) {
    const C = L.cum;
    let k = 0;
    while (k + 2 < C.length && C[k + 1] < s) k++;
    const l = C[k + 1] - C[k];
    return { k: k, t: l > 0 ? clamp((s - C[k]) / l, 0, 1) : 0 };
  }
  // 지도 화면 자리 (측점 s) · 측점 a ~ b 부분을 지금 길로 (moveTo · lineTo)
  function mapAtS(L, s, k, ox, oy) {
    const q = lineAtS(L, s);
    return [(L.mx[q.k] + (L.mx[q.k + 1] - L.mx[q.k]) * q.t) * k + ox, (L.my[q.k] + (L.my[q.k + 1] - L.my[q.k]) * q.t) * k + oy];
  }
  function mapSub(L, a, b, k, ox, oy) {
    const p0 = mapAtS(L, a, k, ox, oy), p1 = mapAtS(L, b, k, ox, oy);
    mx.moveTo(p0[0], p0[1]);
    for (let j = 1; j + 1 < L.cum.length; j++) if (L.cum[j] > a && L.cum[j] < b) mx.lineTo(L.mx[j] * k + ox, L.my[j] * k + oy);
    mx.lineTo(p1[0], p1[1]);
  }
  function shGo(L, q) { // 목록에서 누름: 그 구간 가운데를 봄
    const p = pickPoint(L, lineAtS(L, (q.a + q.b) / 2));
    cfGo({ e: p[0], n: p[1], g: p[3] });
  }
  // 안내 줄 · 종류 단추 · 목록 (줄 · 측점 차례: [종류] 관 · 길이 [×] · 바뀐 때만 다시 만듦)
  function shUI() {
    const arm = SH.arm;
    shChip.classList.toggle('show', !!arm);
    $('app').classList.toggle('shOn', !!arm);
    if (arm) {
      const rk = arm.indexOf('r:') === 0 ? arm.slice(2) : ''; // 하천 추진 · 횡단 (3D 화면과 같은 안내)
      $('shTip').textContent = rk === 'jack' ? (SH.a ? '종점 (도달구)' : '시점 (발진구)') : rk ? (SH.a ? '종점' : '시점') : SH.a ? '끝점' : '시작점';
      $('shName').textContent = rk ? RV.NAME[rk] : SHORE_NAME[arm];
    }
    shChip.dataset.a = SH.a ? String(SH.a.s) : '';
    document.querySelectorAll('#shTypes button').forEach((b) => setOn(b, b.dataset.t === arm));
    if (shBox.hidden) return;
    const rows = [];
    for (const L of lines) for (const q of L.shore) rows.push([L, q]);
    const sig = rows.map(([L, q]) => [L.src, q.i, q.a, q.b, q.t].join(':')).join('|');
    if (sig === SH.sig) return;
    SH.sig = sig;
    const box = $('shList');
    box.textContent = '';
    for (const [L, q] of rows) {
      const row = document.createElement('div');
      row.className = 'shRow';
      row.style.setProperty('--c', SHORE_COL[q.t]);
      Object.assign(row.dataset, { src: L.src, i: q.i, a: q.a, b: q.b, t: q.t });
      const sel = document.createElement('select');
      sel.title = '종류';
      sel.setAttribute('aria-label', '가시설 종류');
      SHORE_T.forEach((t) => { const o = document.createElement('option'); o.value = t; o.textContent = SHORE_NAME[t]; sel.appendChild(o); });
      sel.value = q.t;
      sel.addEventListener('change', () => shEdit(L.src, (list) => list.map((r, j) => (j === q.i ? Object.assign(r, { t: sel.value }) : r))));
      const go = document.createElement('button');
      go.type = 'button';
      go.className = 'shGo';
      go.title = '그쪽 보기';
      go.textContent = (L.label || dTxt(L.dia)) + ' · ' + (q.b - q.a).toFixed(2) + ' m';
      go.addEventListener('click', () => shGo(L, q));
      const del = document.createElement('button');
      del.type = 'button';
      del.className = 'shDel';
      del.title = '지우기';
      del.setAttribute('aria-label', '가시설 지우기');
      del.textContent = '×';
      del.addEventListener('click', () => shEdit(L.src, (list) => list.filter((r, j) => j !== q.i)));
      row.append(sel, go, del);
      box.appendChild(row);
    }
  }
  // 좁은 화면(휴대폰)은 오른쪽 칸이 아래로 이어짐 → 편 카테고리가 보이게 내림 (1124 JACK · 카테고리 머리줄 누르기가 scrollIntoView)
  document.querySelectorAll('#shTypes button').forEach((b) => b.addEventListener('click', () => (SH.arm === b.dataset.t ? shCancel() : shArm(b.dataset.t))));
  $('shCancel').addEventListener('click', shCancel);

  // ---------------------------------------------------------------- 하천 추진 · 횡단 (JACK 1126 · 3D 매핑 화면 map3d.js rv* 와 같은 꼴 · 셈 = viewer/river.js)
  // 360 = 그리기 없음 → 불러온 관 위 두 점 (360 관 누르기 · 오른쪽 지도 관 위 누르기 · 가시설과 같은 길: SH.arm = 'r:jack' · 'r:open' · 시점 노란 고리 · Esc)
  // 시점 = 처음 누른 점 (추진 = 발진구) · 목록 줄 누르기 = 그 구간 고름 → 설정 칸 (값 바꾸면 3D · 수량 · 종단 바로) · 새 구간 = 그 종류에 마지막으로 쓴 값 (이 PC 기억 · 3D 화면과 같은 열쇠)
  // 저장 · 되돌리기 = 관로 고치기와 같음 (프로그램 save_pipes · 밖 = 이 브라우저) → 3D 화면에서 만든 구간도 같은 pipes.js 로 읽힘
  const RV_DEF_KEY = 'rv_river:def';
  const RV_COL = { jack: '#22d3ee', open: '#60a5fa' };
  const RV_SHAPE = { rect: '사각', circ: '원형' }, RV_DAM = { bag: '톤마대', sheet: '시트파일' };
  const RVS = { cur: null, sig: '', setK: '', setV: '', dlgSig: '' };
  const rvName = (q) => (q.k === 'jack' ? '추진 · ' + RV_SHAPE[q.shape] : '횡단 · ' + RV_DAM[q.dam]);
  function rvDefs() { const v = lsGet(RV_DEF_KEY); return v && typeof v === 'object' ? v : {}; }
  const rvSame = (q, c) => !!q && Math.abs(q.a - c.a) < 1e-6 && Math.abs(q.b - c.b) < 1e-6;
  function rvCur() {
    const c = RVS.cur;
    if (!c) return null;
    const L = lines.find((x) => x.src === c.src), q = L && L.river.find((r) => rvSame(r, c));
    return q ? { L: L, q: q } : null;
  }
  function rvCurPlan() { const c = rvCur(); return c ? rvPlans(c.L).find((p) => p.i === c.q.i) || null : null; }
  // 한 줄의 하천 구간 고치기: f(옛 목록) → 새 목록 → river.js clean · 측점 차례 → 저장 · 되돌리기 · 다시 그림
  function rvEdit(src, f) {
    const raw = window.RV_PIPES && Array.isArray(window.RV_PIPES.lines) ? window.RV_PIPES.lines[src] : null;
    if (!raw || !RV) return;
    const before = JSON.stringify(raw);
    const next = f((Array.isArray(raw.river) ? raw.river : []).map((q) => Object.assign({}, q))).map((q) => RV.clean(q)).filter((q) => q).sort((p, q) => p.a - q.a);
    if (next.length) raw.river = next; else delete raw.river;
    const after = JSON.stringify(raw);
    if (after === before) return;
    edPut(src);
    edCommit(src, before, after);
    RVS.sig = '';
  }
  function rvArm(k) {
    if (!RV || RV.KINDS.indexOf(k) < 0) return;
    if (!lines.length) { toast('관로 없음'); return; }
    if (ED.on) edMode(false);
    if (AL.on) alMode(false);
    if (!S.pipes) setPipes(true);
    SH.arm = 'r:' + k;
    SH.a = null;
    cardHide();
    shUI();
    rvUI();
    dirty();
  }
  function rvAdd(src, s0, s1, k) {
    const L = lines.find((x) => x.src === src);
    const q = L && RV.fresh(k, Math.min(s0, s1), Math.min(Math.max(s0, s1), L.len), rvDefs()[k]);
    if (!q) { toast('너무 짧음'); return; }
    if (k === 'jack') q.rev = s0 > s1;                                  // 시점 (처음 누른 점) = 발진구
    rvEdit(src, (list) => list.filter((r) => !(Math.max(+r.a, +r.b) > q.a && Math.min(+r.a, +r.b) < q.b)).concat([q]));   // 겹친 옛 구간은 뺌
    RVS.cur = { src: src, a: q.a, b: q.b };
    if (!catIs('river')) catSet('river', true);
    rvUI();
    const pl = rvCurPlan();
    toast(RV.NAME[k] + ' · ' + (q.b - q.a).toFixed(2) + ' m' + (pl && !pl.ok ? ' · ' + pl.msg : ''));
  }
  function rvSet(patch) {
    const c = rvCur();
    if (!c) return;
    rvEdit(c.L.src, (list) => list.map((r) => (rvSame(RV.clean(r, c.L.len), c.q) ? Object.assign(r, patch) : r)));
    const n = rvCur();
    if (n) {                                                             // 이 종류 새 구간 기본값 = 지금 값
      const d = rvDefs(), keep = Object.assign({}, n.q);
      ['a', 'b', 'i', 'k', 'rev'].forEach((x) => { delete keep[x]; });
      d[n.q.k] = keep;
      lsSet(RV_DEF_KEY, d);
    }
    rvUI();
  }
  function rvDel(L, q) {
    rvEdit(L.src, (list) => list.filter((r) => !rvSame(RV.clean(r, L.len), q)));
    if (RVS.cur && RVS.cur.src === L.src && rvSame(q, RVS.cur)) RVS.cur = null;
    rvUI();
  }
  function rvGo(L, q) { // 목록에서 누름: 그 구간 가운데를 봄 (가까운 사진에서 그쪽으로)
    const p = pickPoint(L, lineAtS(L, (q.a + q.b) / 2));
    cfGo({ e: p[0], n: p[1], g: p[3] });
  }
  // 설정 칸 (고른 구간) = [열쇠, 이름, 단계, 단위, 설명] · 사각 = 폭 × 길이 두 칸 (3D 화면과 같음)
  const RV_F = {
    jack: [['cover', '토피', 0.1, 'm', '하상 (구간 가장 낮은 땅) → 관 윗면 · 관은 수평'], ['base', '기초 여유', 0.1, 'm', '관 바닥 → 수직구 바닥'],
      ['cas', '케이싱 Ø', 0.05, 'm', '강관 케이싱 바깥지름 · 0 = 관 바깥지름 + 0.2 m']],
    rect: [[['lw', 'll'], '발진', 0.5, 'm', '발진구 내공 폭 × 길이 (길이 = 관 방향)'], [['aw', 'al'], '도달', 0.5, 'm', '도달구 내공 폭 × 길이 (길이 = 관 방향)']],
    circ: [['ld', '발진 Ø', 0.5, 'm', '발진구 안지름 (라이너 플레이트)'], ['ad', '도달 Ø', 0.5, 'm', '도달구 안지름 (라이너 플레이트)']],
    open: [['cover', '토피', 0.1, 'm', '하상 (구간 가장 낮은 땅) → 관 윗면 · 관은 수평'], ['h', '높이', 0.1, 'm', '가물막이 천단 = 구간 가장 낮은 땅 + 높이 (수평)'],
      ['w', '간격', 0.5, 'm', '관 → 가물막이 둑 (양쪽 · 물 흐름은 관에 직각)']],
    sheet: [['emb', '근입', 0.5, 'm', '시트파일 땅속 길이']],
  };
  const RV_IN = {};
  function rvChips(lab, items, title) {
    const row = document.createElement('div');
    row.className = 'lrow';
    const k = document.createElement('span'); k.className = 'lk'; k.textContent = lab;
    const ch = document.createElement('div'); ch.className = 'chips';
    items.forEach(([id, t, fn, tt]) => {
      const b = document.createElement('button');
      b.type = 'button'; b.className = 'tg'; b.id = id; b.textContent = t; b.title = tt || title || t;
      b.addEventListener('click', fn);
      ch.appendChild(b);
    });
    row.append(k, ch);
    return row;
  }
  function rvNum(spec) {
    const [key, name, step, unit, title] = spec, keys = Array.isArray(key) ? key : [key];
    const lab = document.createElement('label');
    lab.className = 'numbox'; lab.title = title;
    lab.appendChild(document.createTextNode(name));
    keys.forEach((k, i) => {
      if (i) { const x = document.createElement('span'); x.className = 'xx'; x.textContent = '×'; lab.appendChild(x); }
      const inp = document.createElement('input');
      inp.type = 'number'; inp.step = String(step); inp.id = 'rv_' + k; inp.inputMode = 'decimal';
      inp.min = String(RV.LIM[k][0]); inp.max = String(RV.LIM[k][1]);
      inp.addEventListener('input', () => { if (isNum(inp.value)) rvSet({ [k]: Number(inp.value) }); });
      inp.addEventListener('change', () => { RVS.setV = ''; rvUI(); });
      RV_IN[k] = inp;
      lab.appendChild(inp);
    });
    lab.appendChild(document.createTextNode(unit));
    return lab;
  }
  function rvSetUI(cur) {
    const box = $('rvSet');
    if (!cur) { box.hidden = true; RVS.setK = ''; return; }
    const q = cur.q, sk = [cur.L.src, q.a, q.b, q.k, q.shape, q.dam].join(':');
    box.hidden = false;
    if (RVS.setK !== sk) {
      RVS.setK = sk; RVS.setV = '';
      box.textContent = '';
      Object.keys(RV_IN).forEach((k) => { delete RV_IN[k]; });
      if (q.k === 'jack') {
        box.appendChild(rvChips('수직구', [['rvRect', '사각', () => rvSet({ shape: 'rect' }), '사각 = H-파일 + 토류판'], ['rvCirc', '원형', () => rvSet({ shape: 'circ' }), '원형 = 라이너 플레이트'],
          ['rvRev', '발진 ⇄', () => { const c = rvCur(); if (c) rvSet({ rev: !c.q.rev }); }, '발진구 · 도달구 바꾸기']]));
        RV_F.jack.concat(RV_F[q.shape]).forEach((f) => box.appendChild(rvNum(f)));
      } else {
        box.appendChild(rvChips('가물막이', [['rvBag', '톤마대', () => rvSet({ dam: 'bag' })], ['rvSheet', '시트파일', () => rvSet({ dam: 'sheet' })]]));
        box.appendChild(rvChips('단계', [['rvSt1', '1단계', () => rvSet({ st: 1 }), '1단계 = 시점 둑 → 가운데 (반폭)'], ['rvSt2', '2단계', () => rvSet({ st: 2 }), '2단계 = 가운데 → 종점 둑 (반폭)']]));
        RV_F.open.concat(q.dam === 'sheet' ? RV_F.sheet : []).forEach((f) => box.appendChild(rvNum(f)));
      }
    }
    const pl = rvCurPlan(), vs = JSON.stringify(q) + '|' + (pl ? pl.cas : '');
    if (RVS.setV === vs) return;
    RVS.setV = vs;
    Object.keys(RV_IN).forEach((k) => {
      const inp = RV_IN[k];
      if (document.activeElement === inp) return;
      inp.value = String(round(k === 'cas' && pl && pl.cas ? pl.cas : q[k], 3));
    });
    const on = (id, v) => { const b = $(id); if (b) setOn(b, v); };
    on('rvRect', q.shape === 'rect'); on('rvCirc', q.shape === 'circ'); on('rvBag', q.dam === 'bag'); on('rvSheet', q.dam === 'sheet'); on('rvSt1', q.st === 1); on('rvSt2', q.st === 2);
    const rv = $('rvRev');
    if (rv) rv.title = '발진구 · 도달구 바꾸기 (지금 발진 = ' + (q.rev ? '종점' : '시점') + ')';
  }
  // 종류 단추 · 머리줄 수 · 목록 (바뀐 때만 다시) · 설정 칸 · 열린 종단 창 (그림마다 · 지문으로 거름)
  function rvUI() {
    if (!RV) return;
    const arm = SH.arm && SH.arm.indexOf('r:') === 0 ? SH.arm.slice(2) : '';
    document.querySelectorAll('#rvKinds [data-k]').forEach((b) => setOn(b, b.dataset.k === arm));
    const rows = [];
    for (const L of lines) for (const q of L.river) rows.push([L, q]);
    const tg = $('rvTag'), tt = rows.length ? String(rows.length) : '';
    if (tg.textContent !== tt) tg.textContent = tt;
    if (RVS.cur && !rvCur()) RVS.cur = null;
    const cur = rvCur();
    const sig = rows.map(([L, q]) => [L.src, q.a, q.b, q.k, q.shape, q.dam].join(':')).join('|') + '#' + (cur ? [cur.L.src, cur.q.a, cur.q.b].join(':') : '');
    if (sig !== RVS.sig) {
      RVS.sig = sig;
      const box = $('rvList');
      box.textContent = '';
      for (const [L, q] of rows) {
        const row = document.createElement('div');
        row.className = 'shRow' + (cur && cur.q === q ? ' sel' : '');
        row.style.setProperty('--c', RV_COL[q.k]);
        Object.assign(row.dataset, { src: L.src, a: q.a, b: q.b, k: q.k });
        const go = document.createElement('button');
        go.type = 'button'; go.className = 'shGo'; go.title = '고르기 · 그쪽 보기';
        go.textContent = (L.label || dTxt(L.dia)) + ' · ' + rvName(q) + ' · ' + (q.b - q.a).toFixed(2) + ' m';
        go.addEventListener('click', () => { RVS.cur = { src: L.src, a: q.a, b: q.b }; rvUI(); rvGo(L, q); });
        const del = document.createElement('button');
        del.type = 'button'; del.className = 'shDel'; del.title = '지우기'; del.setAttribute('aria-label', '하천 구간 지우기'); del.textContent = '×';
        del.addEventListener('click', () => rvDel(L, q));
        row.append(go, del);
        box.appendChild(row);
      }
    }
    rvSetUI(cur);
    if (!$('rvDlg').hidden) rvDlgDraw();
  }
  function rvShow(on) {
    if (on && !rvCur()) { const L = lines.find((x) => x.river && x.river.length); if (L) RVS.cur = { src: L.src, a: L.river[0].a, b: L.river[0].b }; }
    if (on && !rvCur()) { toast('하천 구간 없음 · 추진 · 횡단 → 관 위 두 점'); on = false; }
    $('rvDlg').hidden = !on;
    setOn($('rvSec'), on);
    if (on) { if (!secDlg.hidden) secShow(false); RVS.dlgSig = ''; rvUI(); }
  }
  // 종단 창 (river.js svg · rows · 터파기 설정 창과 같은 자리)
  function rvDlgDraw() {
    const c = rvCur(), pl = rvCurPlan();
    if (!c || !pl) { rvShow(false); return; }
    const sig = [pipeGen, selGen, c.L.src, c.q.a, c.q.b].join('|');
    if (sig === RVS.dlgSig) return;
    RVS.dlgSig = sig;
    const g = RV.svg(pl, { W: 380, col: c.L.color }), svg = $('rvSvg');
    svg.setAttribute('viewBox', '0 0 ' + g.w + ' ' + g.h);
    svg.innerHTML = g.body;
    $('rvDTag').textContent = rvName(c.q) + ' · ' + (c.q.b - c.q.a).toFixed(2) + ' m';
    const box = $('rvRows');
    box.textContent = '';
    for (const [k, v] of RV.rows(pl)) {
      const a = document.createElement('span'), b = document.createElement('span');
      a.className = 'k'; a.textContent = k; b.className = 'v'; b.textContent = v;
      box.append(a, b);
    }
    Object.assign($('rvDlg').dataset, { k: c.q.k, a: c.q.a, b: c.q.b });
  }
  if (RV) {
    CAT.river.hook = (on) => { if (!on && SH.arm && SH.arm.indexOf('r:') === 0) shCancel(); RVS.sig = ''; RVS.setK = ''; rvUI(); };
    document.querySelectorAll('#rvKinds [data-k]').forEach((b) => b.addEventListener('click', () => (SH.arm === 'r:' + b.dataset.k ? shCancel() : rvArm(b.dataset.k))));
    $('rvSec').addEventListener('click', () => rvShow($('rvDlg').hidden));
    $('rvX').addEventListener('click', () => rvShow(false));
    rvUI();
  } else { CAT.river.sec.hidden = true; $('qkRiver').hidden = true; }   // river.js 못 읽음 = 하천 카테고리 · 수량 갈래 숨김

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
  // 꼭짓점 사이 심프슨으로 적분 (가시설 구간 = 기울기 0 · 구간 끝에서 나눔) · 포장 깨기 = ∫ 2·wv · 모래 기초 = 2·wb·bed · 연장 · 되메우기 = 터파기 − 관 부피 − 모래.
  // 땅 높이는 사진과 무관하게 (g, 없으면 groundAt) · 관 끝 더 판 곳 · 꺾인 곳 겹침은 넣지 않음.
  // 수량 카테고리 = 갈래 단추 (전체 · 관로 · 토공 · 포장 · 점용 · 가시설) → 아래 표 (그 갈래 줄만 · 이 관 / 전체) · 같은 단추 다시 = 닫기 (3D 화면과 같음)
  const QT = { on: false, view: 'all', memo: new WeakMap(), row: {}, rowEl: {}, txt: new Map() };
  // 지장물 간섭 상태 (아래 «지장물 간섭»): gap = 이격 기준 m (이 브라우저에 기억) · list = 간섭 자리
  const CF_KEY = 'rv_gap:' + String(PRJ.title || 'project'), CF_CELL = 10, CF_STEP = 0.5;
  const CF = { gap: 0.3, list: [], of: null, key: '', t: 0, ver: 0, memo: new WeakMap(), grid: null, gridOf: -1 };
  if (isNum(lsGet(CF_KEY))) CF.gap = clamp(Number(lsGet(CF_KEY)), 0, 5);
  const Q_ROWS = [['len', '연장', 'm'], ['cut', '터파기', 'm³'], ['pav', '포장 깨기', 'm²'], ['sand', '모래 기초', 'm³'], ['fill', '되메우기', 'm³'],
    ['tmp', '임시점용', 'm²'], ['perm', '영구점용', 'm²']].concat(SHORE_T.map((t) => ['sh_' + t, { panel: '간이흙막이', hpile: '토류벽', sheet: '시트파일' }[t], 'm']));
  const RV_Q = RV ? RV.QROWS.map(([k, n, u]) => ['rv_' + k, n, u]) : [];          // 하천 추진 · 횡단 (river.js qty · 3D 화면과 같은 줄)
  const RV_QINT = new Set(['rv_shaft', 'rv_hp', 'rv_bag']);                        // 개수 = 소수 없이
  Q_ROWS.push(...RV_Q);
  const Q_KIND = { all: ['수량', null], pipe: ['관로', ['len']], earth: ['토공', ['cut', 'sand', 'fill']], pave: ['포장', ['pav']], occ: ['점용', ['tmp', 'perm']],
    shore: ['가시설', SHORE_T.map((t) => 'sh_' + t)], river: ['하천', RV_Q.map((r) => r[0])] };
  function gQ(L, k) { const g = L.g[k]; return Number.isFinite(g) ? g : groundAt(L.pts[k * 3], L.pts[k * 3 + 1]); }
  function zcQ(L, k) { const z = L.pts[k * 3 + 2]; return L.hasZ && Number.isFinite(z) ? z : gQ(L, k) - S.cover - L.dia / 2; }
  function secQ(D, g, zc, n) { // → [단면적 m², 지표 반폭 wv] · n = 기울기 (가시설 구간 = 0)
    const zs = Math.min(zc - D / 2, g - 0.01), zb = zs - TR.bed, wb = D / 2 + TR.margin, zt = g - TR.tp;
    const wv = wb + n * Math.max(0, zt - zb);
    return [2 * wv * (g - Math.max(zt, zb)) + (wb + wv) * Math.max(0, zt - zb), wv];
  }
  function lineQty(L) {
    const key = [TR.tp, TR.n, TR.margin, TR.bed, S.cover].join('|');
    const m = QT.memo.get(L);
    if (m && m.key === key) return m.q;
    const P = L.pts, D = L.dia, cnt = P.length / 3, C = L.cum;
    let len = 0, tlen = 0, cut = 0, pav = 0, g0 = gQ(L, 0), z0 = zcQ(L, 0);
    const rp = rvPlans(L); // 하천: 추진 = 터파기 없음 · 횡단 = 관 깊이 = 하천 깊이 (3D 화면과 같음)
    for (let k = 1; k < cnt; k++) {
      const g1 = gQ(L, k), z1 = zcQ(L, k), l = Math.hypot(P[k * 3] - P[k * 3 - 3], P[k * 3 + 1] - P[k * 3 - 2]);
      if (l > 0) {
        const ts = [0, 1]; // 가시설 · 하천 구간 끝에서 나눔 (가시설 안 = 기울기 0)
        for (const q of L.shore.concat(rp)) for (const s of [q.a, q.b]) if (s > C[k - 1] + 1e-9 && s < C[k] - 1e-9) ts.push((s - C[k - 1]) / l);
        ts.sort((a, b) => a - b);
        for (let i = 0; i + 1 < ts.length; i++) {
          const ta = ts[i], tb = ts[i + 1], tm = (ta + tb) / 2, h = (tb - ta) * l, sm = C[k - 1] + tm * l;
          const rq = rp.length ? rp.find((p) => sm > p.a && sm < p.b) : null, rz = rq && Number.isFinite(rq.zc) ? rq.zc : null;
          if (rq && rq.k === 'jack') continue;
          const n = shoreAt(L, sm) ? 0 : TR.n, sq = (t) => secQ(D, g0 + (g1 - g0) * t, rz == null ? z0 + (z1 - z0) * t : rz, n);
          const A = sq(ta), M = sq(tm), B = sq(tb);
          cut += h / 6 * (A[0] + 4 * M[0] + B[0]);
          pav += h / 3 * (A[1] + 4 * M[1] + B[1]);
          tlen += h;
        }
        len += l;
      }
      g0 = g1; z0 = z1;
    }
    const sand = 2 * (D / 2 + TR.margin) * TR.bed * tlen, pipe = Math.PI * D * D / 4 * tlen; // 터파기 길이만 (추진 구간 빼고)
    const q = { len: len, cut: cut, pav: pav, sand: sand, fill: cut - pipe - sand, tmp: pav, perm: D * len }; // 임시점용 = 터파기 위 폭 × 길이 · 영구점용 = 관 바깥지름 × 길이
    SHORE_T.forEach((t) => { q['sh_' + t] = 0; });
    for (const s of L.shore) if (q['sh_' + s.t] != null) q['sh_' + s.t] += s.b - s.a;     // 가시설 연장 (관 측점 구간 b − a)
    RV_Q.forEach(([k]) => { q[k] = 0; });
    for (const pl of rp) { const z = RV.qty(pl); RV.QROWS.forEach(([k]) => { q['rv_' + k] += z[k]; }); }   // 하천 (구간마다 river.js)
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
  const qFmt0 = (v) => (Number.isFinite(v) ? Math.round(v).toLocaleString('ko-KR') : '-');
  function qtyUI() {
    const L = qtyLine(), one = L ? lineQty(L) : null, tot = {};
    Q_ROWS.forEach(([k]) => { tot[k] = 0; });
    lines.forEach((M) => { const q = lineQty(M); Q_ROWS.forEach(([k]) => { tot[k] += q[k]; }); });
    qTxt($('qLine'), L ? L.label || 'D' + Math.round(L.dia * 1000) : '관로 없음');
    Q_ROWS.forEach(([k]) => {
      const [a, b] = QT.row[k], va = String(one ? one[k] : NaN), vb = String(lines.length ? tot[k] : NaN), fm = RV_QINT.has(k) ? qFmt0 : qFmt;
      if (a.dataset.v !== va) { a.dataset.v = va; a.textContent = fm(Number(va)); } // data-v = 반올림 전 값
      if (b.dataset.v !== vb) { b.dataset.v = vb; b.textContent = fm(Number(vb)); }
    });
    if (QT.view === 'all') { const h = !rvAny(); RV_Q.forEach(([k]) => QT.rowEl[k].forEach((e) => { if (e.hidden !== h) e.hidden = h; })); } // 전체 = 하천 구간 있을 때만 하천 줄
    qTxt($('qNote'), QT.view === 'river' ? (rvAny() ? '수직구 = 내공 × 깊이 · H-파일 근입 ' + RV.HP_EMB + ' m' : '하천 구간 없음') :
      QT.view !== 'pipe' && QT.view !== 'shore' && ['tp', 'n', 'margin', 'bed'].every((k) => TR[k] === TR_DEF[k]) ? '가시화 기본값 기준' : '');
  }
  // 수량 표 열기 · 닫기 (view = 갈래 · 그 갈래 단추 켜짐 · 표 = 그 갈래 줄만)
  function qShow(on, view) {
    QT.on = !!on;
    if (view && Q_KIND[view]) QT.view = view;
    const ks = Q_KIND[QT.view][1];
    $('qtyBox').hidden = !QT.on;
    $('qtyBox').dataset.view = QT.view;
    document.querySelectorAll('[data-q]').forEach((b) => setOn(b, QT.on && b.dataset.q === QT.view));
    const noRv = !rvAny(); // 전체 = 하천 구간 있을 때만 하천 줄
    Q_ROWS.forEach(([k]) => { QT.rowEl[k].forEach((e) => { e.hidden = ks ? ks.indexOf(k) < 0 : noRv && k.indexOf('rv_') === 0; }); });
    qTxt($('qTitle'), Q_KIND[QT.view][0]);
    if (QT.on) { if (!catIs('qty')) catSet('qty', true); qtyUI(); $('qtyBox').scrollIntoView({ block: 'nearest' }); }
    dirty();
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
      QT.rowEl[k] = [lab, a, b];
    });
    document.querySelectorAll('[data-q]').forEach((b) => b.addEventListener('click', () => (QT.on && QT.view === b.dataset.q ? qShow(false) : qShow(true, b.dataset.q))));
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
  // 간섭 카테고리: 이격 · 간섭 수 (머리줄 이름표도) · 지장물 종류 단추 (전체 · 가스 …· 간섭 있는 종류만) · 목록 (누르면 그쪽 보기 · 간섭 자리 바뀔 때만 다시 그림)
  const CFV = { kind: '', ver: -1, has: null };
  const cfKind = (sp) => (obst[sp.oi] && obst[sp.oi].kind) || '지장물';
  function cfUI() {
    const has = obst.length > 0;
    if (CFV.has !== has) {
      CFV.has = has; CFV.ver = -1;
      $('cfNone').hidden = has; $('qClr').hidden = !has; $('cfKinds').hidden = !has; $('qList').hidden = !has;
    }
    if (CFV.ver === CF.ver) return;
    CFV.ver = CF.ver;
    const n = CF.list.length, hit = $('qHit'), tag = $('cfTag'), kb = $('cfKinds'), box = $('qList');
    hit.textContent = n ? '간섭 ' + n + '곳' : '간섭 없음';
    hit.classList.toggle('bad', n > 0);
    tag.textContent = has ? (n ? n + '곳' : '없음') : '';
    tag.classList.toggle('bad', n > 0);
    tag.classList.toggle('ok', has && !n);
    const cnt = new Map();
    CF.list.forEach((sp) => { const k = cfKind(sp); cnt.set(k, (cnt.get(k) || 0) + 1); });
    if (CFV.kind && !cnt.has(CFV.kind)) CFV.kind = '';
    kb.textContent = '';
    if (cnt.size) {
      [['', '전체', n]].concat([...cnt].map(([k, c]) => [k, k, c])).forEach(([k, t, c]) => {
        const b = document.createElement('button');
        b.type = 'button'; b.className = 'tg' + (CFV.kind === k ? ' on' : ''); b.dataset.k = k; b.title = k ? '관로 ↔ ' + k : '관로 ↔ 지장물 전체';
        b.append(t);
        const ni = document.createElement('i'); ni.className = 'n'; ni.textContent = String(c); b.appendChild(ni);
        b.addEventListener('click', () => { CFV.kind = k; CFV.ver = -1; cfUI(); });
        kb.appendChild(b);
      });
    }
    box.textContent = '';
    CF.list.forEach((sp, i) => {
      if (CFV.kind && cfKind(sp) !== CFV.kind) return;
      const b = document.createElement('button');
      b.type = 'button';
      b.textContent = (i + 1) + ' · ' + sp.p + ' ↔ ' + sp.o + ' · ' + sp.c.toFixed(2) + ' m';
      b.title = '그쪽 보기';
      b.dataset.at = [sp.e, sp.n, sp.c].map((v) => round(v, 4)).join(','); // 자리 · 이격 (반올림 전)
      b.addEventListener('click', () => cfGo(sp));
      box.appendChild(b);
    });
  }

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
  window.__rv360 = { reveal: reveal, catSet: catSet, catIs: catIs,   // 시험 (3D 화면 window.__map3d 와 같은 꼴)
    SH: SH, lineQty: (src) => { const L = lines.find((q) => q.src === src); return L ? lineQty(L) : null; },
    rv: { ok: !!RV, arm: rvArm, set: rvSet, show: rvShow, ground: rvGround, plans: (src) => { const L = lines.find((q) => q.src === src); return L ? rvPlans(L) : []; },
      cur: () => { const c = rvCur(); return c ? { src: c.L.src, q: c.q } : null; }, pick: (src, a, b) => { RVS.cur = { src: src, a: a, b: b }; rvUI(); },
      tris: () => (rvM.n - rvM.no) / 3, holes: () => rvM.no / 3 } };
  initGL();
  buildCal();
  hostShow();
  resizeAll();
  openShot(startIdx);
})();
