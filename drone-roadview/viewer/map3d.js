/* 드론 로드뷰 — 3D 매핑 보기 (JACK 1125 · Terra 대체) + 지하 시설물 (JACK 1126 · 360 보기 viewer/app.js 에서 옮김)
 * 자료: data/mapping.js (RV_MAPPING · 원점 · 범위) · data/mesh.json + 조각 (세모마다 꼭짓점 셋: 위치 float32×3 · 질감 float32×2, 원점 기준 m)
 *       data/pipes.js (RV_PIPES · 360 보기와 같은 꼴) · data/obstacles.js (RV_OBST) · data/calib.js (RV_CALIB.trench = 터파기 설정) ·
 *       data/dem.js (RV_DEM · 땅만 1 m · x0/y0 = 매핑 원점 기준 · 표고 절대값)
 * 좌표: 세계 = 매핑 원점 기준 (x = 동, y = 북, z = 표고 − 원점 표고 · EPSG:5186 + EGM2008).
 *       관로 자료 = RV_PIPES.origin 기준 (e, n) · 표고 절대 (z, g) → 세계 = (e + TX, n + TY, z + TZ).
 * 관 중심 = z (있으면) 또는 땅 − 평균매설깊이 − 관경/2. 땅 g = 관 줄을 따라 1 m 마다 땅 높이 (dem.js · 없으면 파일 g) 를 ±5 m 이동평균
 * (3D 모델 = DSM 이라 풀 · 연석 · 도랑 사면에 관이 출렁임 · JACK 1126).
 * 터파기 구덩이: 3D 모델을 그린 뒤 구덩이 기둥 (지표 띠 ±wv · 바닥 아래 ~ 땅 위 3 m) 을 스텐실 z-fail 로 그려 «모델 면이 기둥 안» 인 화소 = 1 →
 * 그 자리 깊이를 지움 → 안쪽 면 · 관을 스텐실 1 안에서만. 모델 면이 매끈한 땅보다 높은 곳은 벽을 땅 위 WALL_UP 까지 늘려 빈틈이 없게.
 */
(function () {
  'use strict';
  const D2R = Math.PI / 180, R2D = 180 / Math.PI;
  const NEAR = 0.05;                 // m, 화면 투영 자르기 (고르기 · 이름표)
  const OB_DIA = 0.15, OB_ALPHA = 0.45, XR_ALPHA = 0.55;
  const CF_RED = '#ef4444', AREA_TMP = '#ff9a1f';
  const WALL_UP = 0.5;               // m, 구덩이 벽 · 끝벽을 매끈한 땅 위로 더 (모델 높이 지도가 없을 때) · 있으면 그 자리 모델 면 − 땅 + 0.1 (0 ~ 3)
  const VOL_UP = 3, VOL_DN = 0.5;    // m, 구덩이 기둥: 땅 위 · 바닥 아래
  const G_STEP = 1, G_HALF = 5;      // m, 관 땅 높이: 1 m 마다 · ±5 m 이동평균
  const SHORE_T = ['panel', 'hpile', 'sheet'];
  const SHORE_NAME = { panel: '조립식 간이흙막이', hpile: '토류벽 흙막이', sheet: '시트파일' };
  const SHORE_COL = { panel: '#f2b33d', hpile: '#b07a45', sheet: '#9aa6b4' };
  const MAT_T = ['ci', 'steel', 'plastic'];
  const MAT_NAME = { ci: '주철관', steel: '강관', plastic: '비금속관' }, MAT_SHORT = { ci: '주철', steel: '강관', plastic: '비금속' };
  const MAT_RGB = { ci: [0.25, 0.25, 0.26], steel: [0, 0.898, 1], plastic: [0.93, 0.93, 0.91] };
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
  const colorCtx = document.createElement('canvas').getContext('2d');
  function hexRgb(c) {
    colorCtx.fillStyle = '#00e5ff';
    colorCtx.fillStyle = c;
    const s = String(colorCtx.fillStyle);
    if (s[0] === '#') return [1, 3, 5].map((i) => parseInt(s.substr(i, 2), 16) / 255);
    const m = s.match(/[\d.]+/g) || [0, 229, 255];
    return [0, 1, 2].map((i) => Number(m[i]) / 255);
  }
  let toastTimer = 0;
  function toast(text, onClick, btn) {
    const t = $('toast');
    t.textContent = text;
    t.onclick = btn ? null : onClick || null;
    if (btn && onClick) {
      const b = document.createElement('button');
      b.type = 'button'; b.textContent = btn;
      b.addEventListener('click', (e) => { e.stopPropagation(); t.classList.remove('show'); onClick(); });
      t.appendChild(b);
    }
    t.classList.toggle('click', !!onClick && !btn);
    t.classList.add('show');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => t.classList.remove('show'), btn ? 10000 : onClick ? 6000 : 3000);
  }
  function lsGet(k) { try { const v = localStorage.getItem(k); return v ? JSON.parse(v) : null; } catch (e) { return null; } }
  function lsSet(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); } catch (e) { /* 저장 못 해도 계속 */ } }
  function copyText(text) {
    const ta = document.createElement('textarea');
    ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
    document.body.appendChild(ta); ta.select();
    try { document.execCommand('copy'); toast('복사됨 · ' + text); } catch (e) { toast(text); }
    ta.remove();
  }
  const HOST = () => (window.pywebview && window.pywebview.api) || null;
  function hostCall(name, arg) {
    const h = HOST();
    if (!h || typeof h[name] !== 'function') return Promise.reject(new Error('프로그램 연결 없음'));
    return Promise.resolve().then(() => (arg === undefined ? h[name]() : h[name](arg)));
  }
  async function call(n, ...a) { const h = HOST(); if (!h || !h[n]) { toast('프로그램 안에서만'); return null; } try { return await h[n](...a); } catch (e) { toast('오류 · ' + String(e.message || e).slice(0, 100)); return null; } }
  const errText = (e) => String((e && e.message) || e || '오류');
  function hostShow() { $('hostTools').hidden = !HOST(); }
  window.addEventListener('pywebviewready', hostShow);
  hostShow();

  const P = window.RV_MAPPING || {};
  const ORG = Array.isArray(P.origin) ? P.origin.map(Number) : [0, 0, 0];
  const H0 = ORG[2] || 0, TZ = -H0;
  $('title').textContent = P.title || '3D 매핑';
  let beforeHome = () => Promise.resolve();
  $('bHome').addEventListener('click', () => { beforeHome().catch(() => {}).then(() => call('go_home')); });
  $('bFiles').addEventListener('click', () => call('open_results'));

  // ---------------------------------------------------------------- 결과물 다듬기 (JACK 1126 · 프로그램 안에서만): 잘라내기 · DEM 만들기 · 웹 공유
  // 프로그램 일 (crop_apply · crop_clear · add_outputs · export_web) → {ok, job} → job(id) 을 0.5 초마다 → 진행 막대.
  // 잘라내기 · DEM = 결과물을 다시 만듦 → 끝나면 다시 엶 · 웹 공유 = 끝나면 주소 + [주소 복사]. 3D 없는 프로젝트도 됨 (잘라내기 빼고).
  // ng = «땅 아님» 그리기 시작 (3D 화면만 · 아래에서 채움) · ngGet = 땅 아님 목록 다시 읽기
  const TB = { id: null, kind: '', crop: null, flush: () => Promise.resolve(), cut: null, onCrop: () => {}, ng: null, ngGet: () => Promise.resolve() };
  const tbBox = $('trimBox');
  const ngHost = () => { const h = HOST(); return !!h && typeof h.nonground_set === 'function'; };
  function tbShow(on) {
    tbBox.hidden = !on;
    if (on && tbBox.parentElement && tbBox.parentElement.id === 'side' && $('side').hidden) { $('side').hidden = false; $('tgSide').classList.add('on'); }
    $('bTrim').classList.toggle('on', on);
    $('tbCut').hidden = !TB.cut;
    $('ngRow').hidden = !TB.ng || !ngHost();
    $('tbDem').querySelector('span').textContent = (Array.isArray(P.outputs) && P.outputs.includes('dtm')) ? 'DEM 다시 만들기' : 'DEM 만들기';
    if (on) { tbCropGet(); if (TB.ng) TB.ngGet(); }
    TB.onCrop();
  }
  function tbCropGet() {
    return call('crop_get').then((r) => {
      const pg = r && Array.isArray(r.polygon) && r.polygon.length >= 3 ? r.polygon.map((q) => [Number(q[0]), Number(q[1])]) : null;
      TB.crop = pg;
      let a = 0;
      if (pg) for (let i = 0, j = pg.length - 1; i < pg.length; j = i++) a += (pg[j][0] + pg[i][0]) * (pg[j][1] - pg[i][1]);
      $('tbState').textContent = pg ? '잘림 · 점 ' + pg.length + '개 · ' + Math.abs(a / 2).toLocaleString('ko-KR', { maximumFractionDigits: 0 }) + ' m²' : '잘린 곳 없음';
      $('tbClear').hidden = !pg;
      TB.onCrop();
      return pg;
    });
  }
  function tbJobUI(st, pct, line) {
    const pr = $('tbProg');
    pr.hidden = false;
    $('tbBar').style.width = clamp(num(pct, 0), 0, 100) + '%';
    $('tbLine').textContent = line || '';
    pr.classList.toggle('fail', st === 'fail');
    pr.classList.toggle('done', st === 'done');
    pr.dataset.st = st;
    const busy = st === 'run';
    ['tbCut', 'tbDem', 'tbWeb', 'tbClear', 'ngDraw', 'ngClear'].forEach((id) => { $(id).disabled = busy; });
  }
  // kind = 'crop' · 'clear' · 'dem' · 'web' · 'ng' (땅 아님) · start = 프로그램 일 시작 (→ {ok, job})
  function tbJob(kind, start) {
    if (TB.id != null) { toast('다른 일 하는 중'); return; }
    if (tbBox.hidden) tbShow(true);
    TB.kind = kind;
    $('tbUrlRow').hidden = true;
    tbJobUI('run', 0, '준비');
    TB.flush().then(start).then((r) => {
      if (!r || r.ok === false || r.job == null) { tbJobUI('fail', 0, (r && r.msg) || '못 함'); return; }
      TB.id = r.job;
      tbPoll();
    }, (e) => tbJobUI('fail', 0, errText(e)));
  }
  function tbPoll() {
    setTimeout(() => {
      call('job', TB.id).then((r) => {
        if (!r) { TB.id = null; tbJobUI('fail', 0, '연결 끊김'); return; }
        if (r.state === 'done') {
          TB.id = null;
          if (TB.kind === 'web') {
            tbJobUI('done', 100, r.line || '끝');
            if (r.url) { $('tbUrl').value = String(r.url); $('tbUrlRow').hidden = false; }
          } else {
            tbJobUI('done', 100, '다 만듦 · 다시 여는 중');
            setTimeout(() => location.reload(), 700);
          }
        } else if (r.state === 'fail') { TB.id = null; tbJobUI('fail', r.pct, r.line || '못 함'); }
        else { tbJobUI('run', r.pct, r.line); tbPoll(); }
      });
    }, 500);
  }
  $('bTrim').addEventListener('click', () => tbShow(tbBox.hidden));
  $('tbX').addEventListener('click', () => tbShow(false));
  $('tbCut').addEventListener('click', () => { if (TB.cut) TB.cut(null); });
  $('tbDem').addEventListener('click', () => tbJob('dem', () => call('add_outputs', ['dtm'])));
  $('tbWeb').addEventListener('click', () => tbJob('web', () => call('export_web')));
  $('tbClear').addEventListener('click', () => toast('잘라내기 없애고 결과물 다시 만들기', () => tbJob('clear', () => call('crop_clear')), '없애기'));
  $('tbUrl').addEventListener('focus', () => $('tbUrl').select());
  $('tbCopy').addEventListener('click', () => {
    const u = $('tbUrl').value;
    if (u) hostCall('copy_text', u).then((r) => { if (r && r.ok !== false) toast('주소 복사됨'); else copyText(u); }, () => copyText(u));
  });

  // 3D 를 안 만든 프로젝트 (결과물 고르기 · 1.0.15) → 위에서 본 미리보기 그림 + 만든 것 · [결과물]
  if (Array.isArray(P.outputs) && !P.outputs.includes('mesh') && !P.outputs.includes('gs')) {     // 가우시안만 = 3D 화면 (아래 loadMesh)
    const NM = { ortho: '정사영상', dsm: 'DSM', dtm: 'DTM', las: '점구름 LAS', contour: '등고선' };
    ['bPipes', 'hObst', 'tgEdit', 'tgQty', 'bXray', 'bCheck', 'bDtm', 'bTop', 'tgSide', 'side', 'c', 'ov', 'load'].forEach((id) => { $(id).style.display = 'none'; });
    const f = document.createElement('div'); f.id = 'flat';
    f.innerHTML = '<img src="data/미리보기.png" alt=""><div class="fl">3D 모델 없음 · 만든 것: ' + P.outputs.map((k) => NM[k] || k).join(' · ') + '</div>';
    document.body.appendChild(f);
    $('info').textContent = '사진 ' + (P.registered || '?') + '/' + (P.photos || '?') + '장 · RTK 어긋남 ' + (P.rtk_rms != null ? P.rtk_rms.toFixed(2) + ' m' : '-') + ' · ' + (P.crs || 'EPSG:5186') + ' + ' + (P.height || 'EGM2008');
    window.__map3d = { flat: true };
    return;
  }

  $('side').insertBefore(tbBox, $('side').firstChild);                 // 결과물 다듬기 = 오른쪽 칸 맨 위 (3D 화면)
  const cv = $('c'), ovc = $('ov'), ov = ovc.getContext('2d');
  const gl = cv.getContext('webgl2', { antialias: true, preserveDrawingBuffer: true, stencil: true });
  if (!gl) { $('load').textContent = 'WebGL2 없음'; return; }
  const fext = gl.getExtension('EXT_color_buffer_float');
  cv.addEventListener('webglcontextlost', (e) => {                    // 그래픽 메모리 부족 등으로 끊기면 알리고 다시 엶 (1126 검토)
    e.preventDefault(); const t = $('toast'); if (t) { t.textContent = '그래픽 메모리 부족 · 다시 여는 중'; t.classList.add('show'); }
    setTimeout(() => location.reload(), 1500);
  });
  const maxTex = gl.getParameter(gl.MAX_TEXTURE_SIZE) || 4096;

  // ---------------------------------------------------------------- 셰이더
  function prog(vs, fs) {
    const sh = (t, s) => { const o = gl.createShader(t); gl.shaderSource(o, '#version 300 es\nprecision highp float;\n' + s); gl.compileShader(o);
      if (!gl.getShaderParameter(o, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(o)); return o; };
    const p = gl.createProgram(); gl.attachShader(p, sh(gl.VERTEX_SHADER, vs)); gl.attachShader(p, sh(gl.FRAGMENT_SHADER, fs));
    gl.bindAttribLocation(p, 0, 'aP'); gl.bindAttribLocation(p, 1, 'aT'); gl.bindAttribLocation(p, 2, 'aN');
    gl.linkProgram(p); if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p));
    const u = {}; for (let i = 0, n = gl.getProgramParameter(p, gl.ACTIVE_UNIFORMS); i < n; i++) { const a = gl.getActiveUniform(p, i); u[a.name] = gl.getUniformLocation(p, a.name); }
    return { p, u };
  }
  // 모델: 질감 + 땅 위 그림 (터파기 영역 · 관 띠 · 자른 선 = 위에서 본 그림 두 장 · 가까이 dl · 전체 dg · 미리 곱한 알파)
  const MESH = prog('in vec3 aP; in vec2 aT; uniform mat4 M; out vec2 t; out vec2 w; void main(){ t = aT; w = aP.xy; gl_Position = M * vec4(aP, 1.0); }',
    'in vec2 t; in vec2 w; uniform sampler2D tex, dg, dl; uniform vec4 rg, rl; uniform float dOn; out vec4 o;\n' +
    'void main(){ vec3 c = texture(tex, t).rgb;\n' +
    '  if (dOn > 0.5) {\n' +
    '    vec2 ug = (w - rg.xy) / rg.zw, ul = (w - rl.xy) / rl.zw; vec4 a = texture(dg, ug), b = texture(dl, ul);\n' +
    '    bool il = all(greaterThanEqual(ul, vec2(0.0))) && all(lessThanEqual(ul, vec2(1.0)));\n' +
    '    bool ig = all(greaterThanEqual(ug, vec2(0.0))) && all(lessThanEqual(ug, vec2(1.0)));\n' +
    '    vec4 d = il ? b : ig ? a : vec4(0.0); c = c * (1.0 - d.a) + d.rgb; }\n' +
    '  o = vec4(c, 1.0); }');
  const FLAT = prog('in vec3 aP; uniform mat4 M; void main(){ gl_Position = M * vec4(aP, 1.0); }',
    'uniform vec4 col; out vec4 o; void main(){ o = col; }');
  const PICK = prog('in vec3 aP; uniform mat4 M; out vec3 w; void main(){ w = aP; gl_Position = M * vec4(aP, 1.0); }',
    'in vec3 w; uniform float id; out vec4 o; void main(){ o = vec4(w, id); }');
  // 화면 전체 세모 (깊이 = 가장 멀리): 구덩이 자리 깊이 지우기
  const FULL = prog('void main(){ vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2)); gl_Position = vec4(p * 2.0 - 1.0, 1.0, 1.0); }',
    'out vec4 o; void main(){ o = vec4(0.0); }');
  const emptyVao = gl.createVertexArray();

  // 관 · 터파기 (360 보기의 PFS · TFS 를 그대로 · 꼭짓점 셰이더만 3D 시점 · 투영: v_rel = 세계 − 눈)
  const PVS = '#version 300 es\n' +
    'in vec3 a_pos; in vec3 a_nrm; in vec3 a_col; in vec4 a_aux;\n' +
    'uniform mat4 u_vp; uniform vec3 u_off;\n' +
    'out vec3 v_rel, v_nrm, v_col; out vec4 v_aux;\n' +
    'invariant gl_Position;\n' +
    'void main(){ vec3 rel = a_pos + u_off; v_rel = rel; v_nrm = a_nrm; v_col = a_col; v_aux = a_aux; gl_Position = u_vp * vec4(rel, 1.0); }\n';
  const SUN_GL = 'const vec3 SUN = vec3(-0.36682, 0.44833, 0.81514);\n';
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
    'float nz3(vec3 W, vec3 l, float s){ float k = fk(min(min(l.x, l.y), l.z)); return k > 0.0 ? mix(0.5, vn(W / l + s), k) : 0.5; }\n' +
    'vec3 wall(vec3 W, vec3 N, vec3 b){\n' +
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
    'vec3 flr(vec3 W, vec3 b){\n' +
    '  float m1 = nz(W, 0.4, 8.8), m2 = nz(W, 0.012, 2.2), m3 = nz(W, 0.005, 4.4);\n' +
    '  vec3 c = b * (1.0 + 0.1 * (m1 - 0.5) + 0.12 * (m2 - 0.5) + 0.1 * (m3 - 0.5));\n' +
    '  return mix(c, vec3(0.6, 0.57, 0.52) * (0.85 + 0.3 * m3), smoothstep(0.86, 0.9, nz(W, 0.03, 13.0)) * 0.5); }\n' +
    'vec3 asph(vec3 W, vec3 b){\n' +
    '  vec3 c = b * (1.15 + 0.34 * (nz(W, 0.018, 1.7) - 0.5) + 0.26 * (nz(W, 0.008, 3.9) - 0.5) + 0.1 * (nz(W, 0.12, 2.9) - 0.5));\n' +
    '  c = mix(c, b * 1.55, smoothstep(0.7, 0.76, nz(W, 0.03, 5.3)) * 0.6);\n' +
    '  return mix(c, b * 0.6, smoothstep(0.74, 0.8, nz(W, 0.022, 7.1)) * 0.5); }\n' +
    'vec3 steel(vec3 W, vec3 b, float r){\n' +
    '  vec3 c = b * (1.0 + 0.1 * (nz(W, 0.05, 1.9) - 0.5) + 0.08 * (nz3(W, vec3(0.6, 0.6, 0.03), 3.3) - 0.5));\n' +
    '  float rs = smoothstep(0.5, 0.72, nz(W, 0.3, 6.1) * 0.6 + nz(W, 0.06, 2.4) * 0.4) * r;\n' +
    '  float st = smoothstep(0.55, 0.8, nz3(W, vec3(0.05, 0.05, 1.2), 9.4)) * r;\n' +
    '  return mix(c, vec3(0.4, 0.22, 0.11) * (0.75 + 0.5 * nz(W, 0.02, 8.3)), clamp(rs + 0.5 * st, 0.0, 0.85)); }\n' +
    'vec3 wood(vec3 W, vec3 b){\n' +
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
    '  float li = m == 0 || m == 7 ? 1.0 : 0.52 + 0.5 * max(dot(N, SUN), 0.0);\n' +
    '  float sh = 1.0;\n' +
    '  if (v_aux.z < 50.0) sh = (1.0 - 0.5 * (1.0 - smoothstep(0.8, 2.2, abs(v_aux.w)))) * (1.0 - 0.3 * (1.0 - smoothstep(1.0, 1.8, abs(v_aux.z))));\n' +
    '  vec3 col = c * li * v_aux.y * sh;\n' +
    '  if (m >= 8 && m != 9) col += (m == 11 ? 0.1 : 0.2) * v_aux.y * pow(max(dot(N, normalize(SUN - normalize(v_rel))), 0.0), 28.0);\n' +
    '  o = vec4(col, a * u_alpha);\n' +
    '}\n';
  const VOLF = '#version 300 es\nprecision highp float;\nout vec4 o;\nvoid main(){ o = vec4(1.0); }\n';
  function progRaw(vs, fs) {
    const sh = (t, s) => { const o = gl.createShader(t); gl.shaderSource(o, s); gl.compileShader(o);
      if (!gl.getShaderParameter(o, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(o) || 'shader'); return o; };
    const p = gl.createProgram(); gl.attachShader(p, sh(gl.VERTEX_SHADER, vs)); gl.attachShader(p, sh(gl.FRAGMENT_SHADER, fs));
    ['a_pos', 'a_nrm', 'a_col', 'a_aux'].forEach((n, i) => gl.bindAttribLocation(p, i, n));
    gl.linkProgram(p); if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p) || 'link');
    const u = {}; for (let i = 0, n = gl.getProgramParameter(p, gl.ACTIVE_UNIFORMS); i < n; i++) { const a = gl.getActiveUniform(p, i); u[a.name] = gl.getUniformLocation(p, a.name); }
    return { p, u };
  }
  let TP = null, TT = null, TV = null; // 관 · 터파기 면 · 구덩이 기둥 (스텐실)
  try { TP = progRaw(PVS, PFS); TT = progRaw(PVS, TFS); TV = progRaw(PVS, VOLF); } catch (e) { TP = TT = TV = null; window.__ugErr = String(e); }

  // ---------------------------------------------------------------- 카메라 (궤도 · z 위)
  const V = { az: 25, el: 38, d: 200, tgt: [0, 0, 0], fov: 50 };
  const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]], dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
  const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
  const norm = (a) => { const l = Math.hypot(a[0], a[1], a[2]) || 1; return [a[0] / l, a[1] / l, a[2] / l]; };
  function mul(A, B) { const C = new Array(16).fill(0); for (let i = 0; i < 4; i++) for (let j = 0; j < 4; j++) for (let k = 0; k < 4; k++) C[j * 4 + i] += A[k * 4 + i] * B[j * 4 + k]; return C; }
  function eye() { const a = V.az * Math.PI / 180, e = V.el * Math.PI / 180; return [V.tgt[0] + V.d * Math.cos(e) * Math.sin(a), V.tgt[1] - V.d * Math.cos(e) * Math.cos(a), V.tgt[2] + V.d * Math.sin(e)]; }
  // 시점: M = 투영·보기 (세계) · VPr = 투영·돌림 (눈 기준 좌표 · 관 그물) · f r u = 앞 · 오른쪽 · 위 · EP = 눈 (관로 좌표)
  function view(W, H, w, h) {
    const E = eye(), f = norm(sub(V.tgt, E)), r = norm(cross(f, [0, 0, 1])), u = cross(r, f);
    const vr = [r[0], u[0], -f[0], 0, r[1], u[1], -f[1], 0, r[2], u[2], -f[2], 0, 0, 0, 0, 1];
    const vt = vr.slice(0, 12).concat([-dot(r, E), -dot(u, E), dot(f, E), 1]);
    const n = Math.max(0.05, V.d * 0.005), fa = Math.max(2000, V.d * 30), fy = 1 / Math.tan(V.fov * Math.PI / 360), as = W / H;
    const pm = [fy / as, 0, 0, 0, 0, fy, 0, 0, 0, 0, (fa + n) / (n - fa), -1, 0, 0, 2 * fa * n / (n - fa), 0];
    return { E, f, r, u, M: mul(pm, vt), VPr: mul(pm, vr), tanV: 1 / fy, tanH: as / fy, w: w || W, h: h || H, W, H, EP: [E[0] - TX, E[1] - TY, E[2] - TZ] };
  }
  let VW = null;                       // 지금 시점 (고르기 · 겹쳐 그리기)
  function vwNow() {
    const dpr = Math.min(2, devicePixelRatio || 1), w = Math.max(1, cv.clientWidth), h = Math.max(1, cv.clientHeight);
    VW = view(Math.round(w * dpr), Math.round(h * dpr), w, h);
    return VW;
  }

  // ---------------------------------------------------------------- 자료 · 모델
  let mesh = null;
  function buf(attrs) {
    const vao = gl.createVertexArray(); gl.bindVertexArray(vao);
    for (const [loc, data, size, stride, off] of attrs) {
      const b = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, b); gl.bufferData(gl.ARRAY_BUFFER, data, gl.STATIC_DRAW);
      gl.enableVertexAttribArray(loc); gl.vertexAttribPointer(loc, size, gl.FLOAT, false, stride || 0, off || 0);
    }
    gl.bindVertexArray(null); return vao;
  }
  function texUp(src) {
    const t = gl.createTexture(); gl.bindTexture(gl.TEXTURE_2D, t);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGB, gl.RGB, gl.UNSIGNED_BYTE, src);
    if (gl.getError() === gl.OUT_OF_MEMORY) { gl.deleteTexture(t); throw new Error('그래픽 메모리 모자람'); }
    gl.generateMipmap(gl.TEXTURE_2D);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    return t;
  }
  async function texOf(url) {
    const img = new Image(); img.src = url; await img.decode();
    let src = img;
    if (img.width > maxTex || img.height > maxTex) {                // 그래픽 한도보다 크면 줄여서
      const k = maxTex / Math.max(img.width, img.height), c2 = document.createElement('canvas');
      c2.width = Math.floor(img.width * k); c2.height = Math.floor(img.height * k); c2.getContext('2d').drawImage(img, 0, 0, c2.width, c2.height); src = c2;
    }
    return { t: texUp(src), size: [img.width, img.height], up: [src.width, src.height] };
  }
  async function loadMesh() {
    if (Array.isArray(P.outputs) && !P.outputs.includes('mesh') && GS.ok) {      // 가우시안만 만든 프로젝트: 모델 그물 없이 (깊이 · 고르기 = 맨땅 그물)
      await gsLoad();
      const g = GS.meta, mn = g.min, mxp = g.max;
      mesh = { meta: { min: mn, max: mxp, tris: 0 }, parts: [], done: true };
      V.tgt = [(mn[0] + mxp[0]) / 2, (mn[1] + mxp[1]) / 2, mn[2] + (mxp[2] - mn[2]) * 0.3];
      V.d = Math.hypot(mxp[0] - mn[0], mxp[1] - mn[1]) * 0.85;
      $('bGs').hidden = true;                                           // 끌 수 없음 (맨땅 보기로만 바꿈)
      if (!DT.on) gsSet(true);                                           // 되살린 맨땅 보기를 풀지 않게 (1126 검토)
      window.__mesh = { tris: 0, tiles: 0, gsOnly: true };
      return;
    }
    const meta = await (await fetch('data/mesh.json')).json();
    // 조각(tiles) — 조각마다 질감 (1.0.15 · 그래픽 한도 16384 안) · 옛 자료는 한 덩어리(mesh.bin · tex.jpg)
    // tex_hi · texsize_hi (있으면) = 고해상도 질감: 가까이 · 화면에 큰 조각만 바꿔 읽음 (LOD)
    const list = meta.tiles || [{ bin: 'mesh.bin', tex: 'tex.jpg', verts: meta.verts }];
    const parts = [];
    for (let i = 0; i < list.length; i++) {
      $('load').textContent = '불러오는 중… ' + (i + 1) + ' / ' + list.length;
      const L = list[i], b = await (await fetch('data/' + L.bin)).arrayBuffer(), n = L.verts;
      const tx = await texOf('data/' + L.tex), pos = new Float32Array(b, 0, n * 3);
      const lo = [Infinity, Infinity, Infinity], hi = [-Infinity, -Infinity, -Infinity];
      for (let k = 0; k < n * 3; k += 3) for (let a = 0; a < 3; a++) { const v = pos[k + a]; if (v < lo[a]) lo[a] = v; if (v > hi[a]) hi[a] = v; }
      parts.push({ n, vao: buf([[0, pos, 3], [1, new Float32Array(b, n * 12, n * 2), 2]]), tex: tx.t, size: tx.size, up: tx.up, lo: tx, hi: null,
        hiUrl: L.tex_hi ? 'data/' + L.tex_hi : null, hiSize: Array.isArray(L.texsize_hi) ? L.texsize_hi : null, hiFail: false, bb: [lo, hi], score: 0 });
      redraw();
      if (i === 0) {
        const mn = meta.min, mxp = meta.max;
        V.tgt = [(mn[0] + mxp[0]) / 2, (mn[1] + mxp[1]) / 2, mn[2] + (mxp[2] - mn[2]) * 0.3];
        V.d = Math.hypot(mxp[0] - mn[0], mxp[1] - mn[1]) * 0.85;
        mesh = { meta, parts };
      }
    }
    mesh = { meta, parts, done: true };
    window.__mesh = { tris: meta.tris, tiles: parts.length, tex: parts.map((p) => p.size), up: parts.map((p) => p.up) };
  }

  // ---------------------------------------------------------------- 질감 고해상도 (LOD): 가까이 · 화면에 큰 조각만 tex_hi · 멀어지면 내림
  const LOD = { max: 3, budget: 320e6, busy: false, t: 0, timer: 0, loads: 0, drops: 0, fails: 0 };
  const hiCap = () => Math.min(maxTex, 16384);
  async function texHi(url, sz) {
    const cap = hiCap(), blob = await (await fetch(url)).blob();
    let opt = {};
    if (sz && Math.max(sz[0], sz[1]) > cap) { const k = cap / Math.max(sz[0], sz[1]); opt = { resizeWidth: Math.floor(sz[0] * k), resizeHeight: Math.floor(sz[1] * k), resizeQuality: 'high' }; }
    let bmp = await createImageBitmap(blob, opt);
    if (Math.max(bmp.width, bmp.height) > cap) {                    // 크기를 몰랐던 큰 그림 → 줄여서 (실패해도 첫 그림은 닫음 · 1126 검토)
      const k = cap / Math.max(bmp.width, bmp.height);
      try { const b2 = await createImageBitmap(bmp, { resizeWidth: Math.floor(bmp.width * k), resizeHeight: Math.floor(bmp.height * k), resizeQuality: 'high' }); bmp.close(); bmp = b2; } catch (e) { bmp.close(); throw e; }
    }
    try { return { t: texUp(bmp), size: sz || [bmp.width, bmp.height], up: [bmp.width, bmp.height] }; } finally { bmp.close(); }
  }
  function lodSoon() { clearTimeout(LOD.timer); LOD.timer = setTimeout(lodTick, 300); }
  function lodTick() {
    if (!mesh || !VW || LOD.busy || DT.on || GS.on) return;           // 맨땅 · 가우시안 보기 = 질감 안 그림
    // 실패한 조각은 30초 뒤 다시 (세 번까지 · 그래픽 메모리가 잠깐 모자랐던 것까지 영영 흐리지 않게 · 1126 검토)
    const ps = mesh.parts.filter((p) => p.hiUrl && !(p.hiFailN >= 3) && !(p.hiFailT && performance.now() - p.hiFailT < 30000));
    if (!ps.length) return;
    for (const p of ps) p.score = lodScore(p);
    const need = (p) => Math.max(p.lo.up[0], p.lo.up[1]) * 0.8;
    const want = ps.filter((p) => p.score > need(p)).sort((a, b) => b.score - a.score).slice(0, LOD.max);
    let texels = 0;
    for (const p of ps) {
      if (!p.hi) continue;
      if (want.indexOf(p) < 0 && (p.score < need(p) * 0.75 || ps.filter((q) => q.hi).length > LOD.max)) {    // 내림
        gl.deleteTexture(p.hi.t); p.hi = null; p.tex = p.lo.t; p.size = p.lo.size; p.up = p.lo.up; LOD.drops++; redraw();
      } else texels += p.hi.up[0] * p.hi.up[1];
    }
    const nx = want.find((p) => !p.hi);
    if (!nx) return;
    const est = nx.hiSize ? Math.min(1, hiCap() / Math.max(nx.hiSize[0], nx.hiSize[1])) ** 2 * nx.hiSize[0] * nx.hiSize[1] : hiCap() * hiCap();
    if (texels + est > LOD.budget) return;
    LOD.busy = true;
    texHi(nx.hiUrl, nx.hiSize).then((tx) => {
      nx.score = lodScore(nx);                                         // 읽는 동안 멀어졌으면 붙이지 않음 (지금 시점으로 다시 잼)
      if (nx.score > need(nx) * 0.75) { nx.hi = tx; nx.tex = tx.t; nx.size = tx.size; nx.up = tx.up; LOD.loads++; redraw(); } else gl.deleteTexture(tx.t);
    }, () => { nx.hiFailN = (nx.hiFailN || 0) + 1; nx.hiFailT = performance.now(); LOD.fails++; }).then(() => { LOD.busy = false; lodSoon(); });
  }
  function lodScore(p) {                                               // 화면에 차지하는 크기 (장치 화소 · 대략) · 안 보이면 0
    if (!VW) return 0;
    const kpx = (VW.H / 2) / VW.tanV, E = VW.E;
    const c = [0, 1, 2].map((a) => (p.bb[0][a] + p.bb[1][a]) / 2), rad = Math.hypot(...[0, 1, 2].map((a) => p.bb[1][a] - p.bb[0][a])) / 2;
    const dv = sub(c, E), dist = Math.max(0.5, Math.hypot(dv[0], dv[1], dv[2]) - rad), fz = dot(dv, VW.f);
    let vis = fz > -rad;
    if (vis && fz > rad) { const x = dot(dv, VW.r) / fz / VW.tanH, y = dot(dv, VW.u) / fz / VW.tanV, m = 1 + rad / fz / VW.tanV; vis = Math.abs(x) < m + 0.2 && Math.abs(y) < m + 0.2; }
    return vis ? 2 * rad / dist * kpx : 0;
  }

  // ---------------------------------------------------------------- 땅 높이 (data/dem.js · 없어도 됨 · 360 보기와 같은 꼴 · x0/y0 = 매핑 원점 기준)
  function demInit(d) {
    if (!d || !(num(d.cell, 0) > 0) || !(num(d.tile, 0) >= 1) || !isNum(d.x0) || !isNum(d.y0) || !d.tiles || typeof d.tiles !== 'object') return null;
    return { cell: Number(d.cell), tile: Math.round(Number(d.tile)), x0: Number(d.x0), y0: Number(d.y0), tiles: d.tiles, got: new Map() };
  }
  let DEM = demInit(window.RV_DEM);
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
  // 세계 (x, y) 의 땅 표고 (절대 · 겹선형). 자료 밖 · 빈 칸이면 NaN.
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

  // ---------------------------------------------------------------- 맨땅 보기 (JACK 1126 · DTM): RV_DEM 격자 → 땅 그물 (타일마다 · 빈칸 빼고)
  // 높이 색 (낮음 → 높음 띠) · 해 그림자 · 등고선 (P.contour.minor · 없으면 1 m · 5배마다 굵게) · 메운 칸 = 옅은 색 + 빗금.
  // mtiles (있으면 · 같은 타일 열쇠 · (tile+1)² uint8): 1 = 땅으로 잰 칸 · 0 = 메운 칸 (건물 밑 · 숲 밑 · 땅 아님) · 255 = 자료 없음.
  const DT_KEY = 'rv_map3d:dtm:' + location.pathname;                 // 프로젝트(폴더)마다 따로 (1126 검토)
  const CT = (function () {
    const c = P.contour || {}, mi = num(c.minor, 0) > 0 ? Number(c.minor) : 1, ma = num(c.major, 0) > mi ? Number(c.major) : mi * 5;
    return { minor: mi, every: Math.max(1, Math.round(ma / mi)) };
  })();
  const DT = { on: false, parts: null, mt: false, zr: [0, 1], el: [0, 1], tris: 0, mtOn: true, ctOn: true };
  let DTP = null;                                                      // 못 만들면 맨땅 단추만 숨김 (다른 보기는 그대로)
  try {
    DTP = prog('in vec3 aP; in vec2 aT; in vec3 aN; uniform mat4 M; out vec3 w; out vec3 nv; out float mk;\n' +
    'void main(){ w = aP; nv = aN; mk = aT.x; gl_Position = M * vec4(aP, 1.0); }',
    'in vec3 w; in vec3 nv; in float mk; uniform sampler2D dg, dl; uniform vec4 rg, rl; uniform vec2 zr; uniform float dOn, h0, ci, cj, mtOn; out vec4 o;\n' +
    'vec3 ramp(float t){ vec3 a = vec3(0.17, 0.42, 0.32), b = vec3(0.42, 0.62, 0.36), c = vec3(0.85, 0.80, 0.50), d = vec3(0.72, 0.50, 0.30), e = vec3(0.93, 0.91, 0.88);\n' +
    '  float s = t * 4.0; return s < 1.0 ? mix(a, b, s) : s < 2.0 ? mix(b, c, s - 1.0) : s < 3.0 ? mix(c, d, s - 2.0) : mix(d, e, s - 3.0); }\n' +
    'void main(){\n' +
    '  vec3 c = ramp(clamp((w.z - zr.x) / max(zr.y - zr.x, 0.01), 0.0, 1.0));\n' +
    '  float hp = (w.x - w.y) * 0.5, hw = fwidth(hp);\n' +                  // 메운 칸: 옅게 + 2 m 빗금 (멀면 빗금 없이 옅게만 · 미분은 갈래 밖에서)
    '  if (mtOn > 0.5 && mk < 0.5) {\n' +
    '    float st = smoothstep(0.3 - hw, 0.3 + hw, abs(fract(hp) - 0.5)) * (1.0 - smoothstep(0.2, 0.45, hw));\n' +
    '    c = mix(mix(c, vec3(0.9, 0.88, 0.86), 0.6), vec3(0.42, 0.36, 0.4), st * 0.65); }\n' +
    '  c *= 0.38 + 0.72 * max(dot(normalize(nv), vec3(-0.5, 0.5, 0.70711)), 0.0);\n' +   // 해 = 북서 45°
    '  float f = (w.z + h0) / max(ci, 1e-3), fw = max(fwidth(f), 1e-5), g = f / cj, gw = fw / cj;\n' +
    '  if (ci > 0.0) {\n' +
    '    float lm = (1.0 - smoothstep(0.6, 1.4, abs(fract(f + 0.5) - 0.5) / fw)) * (1.0 - smoothstep(0.3, 0.6, fw));\n' +
    '    float lM = (1.0 - smoothstep(1.0, 2.0, abs(fract(g + 0.5) - 0.5) / gw)) * (1.0 - smoothstep(0.3, 0.6, gw));\n' +
    '    c = mix(c, vec3(0.2, 0.13, 0.08), max(lm * 0.45, lM * 0.8)); }\n' +
    '  if (dOn > 0.5) {\n' +
    '    vec2 ug = (w.xy - rg.xy) / rg.zw, ul = (w.xy - rl.xy) / rl.zw; vec4 a = texture(dg, ug), b = texture(dl, ul);\n' +
    '    bool il = all(greaterThanEqual(ul, vec2(0.0))) && all(lessThanEqual(ul, vec2(1.0)));\n' +
    '    bool ig = all(greaterThanEqual(ug, vec2(0.0))) && all(lessThanEqual(ug, vec2(1.0)));\n' +
    '    vec4 d = il ? b : ig ? a : vec4(0.0); c = c * (1.0 - d.a) + d.rgb; }\n' +
    '  o = vec4(c, 1.0); }');
  } catch (e) { DTP = null; window.__dtErr = String(e); }
  function dtMask(k) {
    const m = window.RV_DEM && window.RV_DEM.mtiles, s = m && m[k], n = (DEM.tile + 1) * (DEM.tile + 1);
    if (typeof s !== 'string') return null;
    try {
      const b = atob(s);
      if (b.length < n) return null;
      const a = new Uint8Array(n);
      for (let i = 0; i < n; i++) a[i] = b.charCodeAt(i);
      return a;
    } catch (e) { return null; }
  }
  // 격자 마디 (gx, gy) 표고 (타일 경계 = 한 줄 겹침 → 옆 타일도 봄)
  function dtNode(gx, gy) {
    const T = DEM.tile, W = T + 1, ix = Math.floor(gx / T), iy = Math.floor(gy / T), c = gx - ix * T, r = gy - iy * T;
    const at = (a, b, cc, rr) => { if (a < 0 || b < 0) return NaN; const A = demTile(a, b); return A ? A[rr * W + cc] : NaN; };
    let z = at(ix, iy, c, r);
    if (!Number.isFinite(z) && c === 0) z = at(ix - 1, iy, T, r);
    if (!Number.isFinite(z) && r === 0) z = at(ix, iy - 1, c, T);
    if (!Number.isFinite(z) && c === 0 && r === 0) z = at(ix - 1, iy - 1, T, T);
    return Number.isFinite(z) ? z : NaN;
  }
  // 그물: 마디마다 꼭짓점 (자리 · 메운 칸 표시 · 법선 = 이웃 마디 차분 · 그림자만 높이 2배) · 네 마디 다 있으면 세모 둘 · 셋이면 하나
  function dtBuild() {
    if (DT.parts || !DEM) return DT.parts || [];
    const T = DEM.tile, W = T + 1, cell = DEM.cell, ZF = 2, parts = [], zs = [];
    let tris = 0, mt = 0;
    for (const k of Object.keys(DEM.tiles)) {
      const ij = k.split('_').map(Number);
      if (ij.length !== 2 || !ij.every(Number.isInteger)) continue;
      const ix = ij[0], iy = ij[1], a = demTile(ix, iy);
      if (!a) continue;
      const m = dtMask(k), gx0 = ix * T, gy0 = iy * T;
      if (m) mt++;
      const zl = (c, r) => (c >= 0 && c <= T && r >= 0 && r <= T ? a[r * W + c] : dtNode(gx0 + c, gy0 + r));
      const dif = (z0, z, z1) => (Number.isFinite(z0) && Number.isFinite(z1) ? (z1 - z0) / 2 : Number.isFinite(z1) ? z1 - z : Number.isFinite(z0) ? z - z0 : 0);
      const vb = new Float32Array(W * W * 8), ok = new Uint8Array(W * W);
      for (let r = 0; r <= T; r++) {
        for (let c = 0; c <= T; c++) {
          const i = r * W + c, z = a[i];
          if (!Number.isFinite(z)) continue;
          ok[i] = 1;
          if ((i & 7) === 0) zs.push(z);
          const nx = -dif(zl(c - 1, r), z, zl(c + 1, r)) / cell * ZF, ny = -dif(zl(c, r - 1), z, zl(c, r + 1)) / cell * ZF, nl = Math.hypot(nx, ny, 1), o = i * 8;
          vb[o] = DEM.x0 + (gx0 + c) * cell; vb[o + 1] = DEM.y0 + (gy0 + r) * cell; vb[o + 2] = z - H0;
          vb[o + 3] = m && m[i] === 0 ? 0 : 1;
          vb[o + 5] = nx / nl; vb[o + 6] = ny / nl; vb[o + 7] = 1 / nl;
        }
      }
      const ib = [];
      for (let r = 0; r < T; r++) {
        for (let c = 0; c < T; c++) {
          const i00 = r * W + c, i10 = i00 + 1, i01 = i00 + W, i11 = i01 + 1, q = ok[i00] + ok[i10] + ok[i01] + ok[i11];
          if (q === 4) ib.push(i00, i10, i11, i00, i11, i01);
          else if (q === 3) for (const v of [i00, i10, i11, i01]) if (ok[v]) ib.push(v);
        }
      }
      if (!ib.length) continue;
      const I = W * W <= 65535 ? new Uint16Array(ib) : new Uint32Array(ib);
      const vao = gl.createVertexArray(); gl.bindVertexArray(vao);
      const vbo = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, vbo); gl.bufferData(gl.ARRAY_BUFFER, vb, gl.STATIC_DRAW);
      gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 32, 0);
      gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 2, gl.FLOAT, false, 32, 12);
      gl.enableVertexAttribArray(2); gl.vertexAttribPointer(2, 3, gl.FLOAT, false, 32, 20);
      const ibo = gl.createBuffer(); gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ibo); gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, I, gl.STATIC_DRAW);
      gl.bindVertexArray(null);
      parts.push({ key: k, vao: vao, n: I.length, it: I.BYTES_PER_ELEMENT === 2 ? gl.UNSIGNED_SHORT : gl.UNSIGNED_INT });
      tris += I.length / 3;
    }
    zs.sort((p, q) => p - q);
    const lo = zs.length ? zs[Math.floor(zs.length * 0.02)] : H0, hi = zs.length ? zs[Math.min(zs.length - 1, Math.floor(zs.length * 0.98))] : H0 + 1;
    DT.el = [lo, Math.max(hi, lo + 0.5)]; DT.zr = [DT.el[0] - H0, DT.el[1] - H0];
    DT.parts = parts; DT.tris = tris; DT.mt = mt > 0;
    return parts;
  }
  function dtDraw(M) {
    gl.uniform2f(DTP.u.zr, DT.zr[0], DT.zr[1]); gl.uniform1f(DTP.u.h0, H0);
    gl.uniform1f(DTP.u.ci, DT.ctOn ? CT.minor : 0); gl.uniform1f(DTP.u.cj, CT.every); gl.uniform1f(DTP.u.mtOn, DT.mtOn ? 1 : 0);
    for (const p of DT.parts) { gl.bindVertexArray(p.vao); gl.drawElements(gl.TRIANGLES, p.n, p.it, 0); }
  }
  // 켜고 끄기 (윗줄 [맨땅] · 오른쪽 메뉴) · 이 창에서는 다시 열어도 그대로 (땅 아님 적용 뒤 맨땅으로 돌아옴)
  function dtSet(on) {
    on = !!on && !!DEM && !!DTP;
    if (on && !dtBuild().length) { toast('땅 높이 자료 없음'); on = false; }
    if (on && GS.on) gsSet(false);                                      // 가우시안 보기와 하나만
    DT.on = on;
    try { sessionStorage.setItem(DT_KEY, on ? '1' : '0'); } catch (e) { /* 저장 못 해도 계속 */ }
    setOn($('bDtm'), on);
    $('bDtm').title = on ? '모델 보기 (질감 3D)' : '맨땅 보기 (나무 · 건물 뺀 땅 · DTM)';
    $('dtLeg').hidden = !on;
    if (on) {
      const f1 = (v) => v.toFixed(1), ci = String(+CT.minor.toFixed(2));
      $('dtMt').hidden = !DT.mt;
      $('dtTx').textContent = (DT.mt ? '· ' : '') + 'EL ' + f1(DT.el[0]) + ' ~ ' + f1(DT.el[1]) + ' · 등고선 ' + ci + ' m';
      TB.ngGet();
    }
    if (!on && mesh && !mesh.parts.length && GS.ok && !GS.on) gsSet(true);   // 가우시안만 만든 프로젝트 = 맨땅을 끄면 가우시안
    redraw();
  }

  // ---------------------------------------------------------------- 가우시안 보기 (JACK 1126 · 가우시안 스플래팅 · data/gs/gs.json + splats.bin)
  // 그리기는 antimatter15/splat 의 WebGL2 방식을 옮겨 고침 (MIT License · Copyright (c) 2023 Kevin Kwok · https://github.com/antimatter15/splat):
  //   자료 텍스처 RGBA32UI (가우시안마다 2 텍셀: 위치 float×3 · 3D 공분산 반정밀 6개 · 색 RGBA8) · 화면 타원 → 사각형 4점 (인스턴스) ·
  //   조각 exp(−r²) · 정렬 = 눈과 깊이 16 bit 계수 정렬 (Worker · 시점이 바뀌면 다시 · 뒤 → 앞) · 미리 곱한 알파로 섞기.
  // 고친 것: 이 화면의 시점 (오른쪽 r · 위 u · 앞 f · 원점 기준 세계) · 화소 0.3 저역 통과 · 깊이 = 모델 (없으면 맨땅) 그물을 먼저 깊이만 그림
  //   → 관 · 터파기 · 재기 · 고르기 · 땅 위 그림은 모델 그물 위에서 그대로 (가우시안 = 보이는 색만).
  // splats.bin = 가우시안마다 32 B (위치 float32×3 · 크기 float32×3 m · 색 RGBA8 · 회전 uint8×4 (w x y z · ×128 + 128)) · gsplat.py write_web
  const GS_KEY = 'rv_map3d:gs:' + location.pathname;
  const GS = { on: false, ok: Array.isArray(P.outputs) && P.outputs.includes('gs'), meta: null, n: 0, tex: null, vao: null, ibuf: null,
    ready: false, loading: null, wk: null, key: '', want: '', busy: false, sorted: 0, sorts: 0, err: '', prog: null };
  try {                                                                 // progRaw 자리: a_pos (0) = 사각형 귀퉁이 · a_nrm (1) = 가우시안 번호 (정렬 차례 · 인스턴스)
    GS.prog = progRaw('#version 300 es\nprecision highp float; precision highp int;\n' +
      'uniform highp usampler2D u_tex; uniform vec3 u_eye, u_r, u_u, u_f; uniform vec2 u_tan, u_vp; uniform float u_near;\n' +
      'in vec2 a_pos; in int a_nrm; out vec4 vC; out vec2 vP;\n' +
      'void main(){\n' +
      '  ivec2 tc = ivec2((uint(a_nrm) & 0x3ffu) << 1, uint(a_nrm) >> 10);\n' +
      '  uvec4 cen = texelFetch(u_tex, tc, 0);\n' +
      '  vec3 d = uintBitsToFloat(cen.xyz) - u_eye, c = vec3(dot(d, u_r), dot(d, u_u), dot(d, u_f));\n' +
      '  gl_Position = vec4(0.0, 0.0, 2.0, 1.0);\n' +
      '  if (c.z < u_near) return;\n' +
      '  vec2 nd = vec2(c.x / (c.z * u_tan.x), c.y / (c.z * u_tan.y));\n' +
      '  if (abs(nd.x) > 1.3 || abs(nd.y) > 1.3) return;\n' +
      '  uvec4 cv = texelFetch(u_tex, tc + ivec2(1, 0), 0);\n' +
      '  vec2 a1 = unpackHalf2x16(cv.x), a2 = unpackHalf2x16(cv.y), a3 = unpackHalf2x16(cv.z);\n' +
      '  mat3 S = mat3(a1.x, a1.y, a2.x, a1.y, a2.y, a3.x, a2.x, a3.x, a3.y);\n' +
      '  vec2 fp = 0.5 * u_vp / u_tan;\n' +
      '  mat3 J = mat3(fp.x / c.z, 0.0, 0.0, 0.0, fp.y / c.z, 0.0, -fp.x * c.x / (c.z * c.z), -fp.y * c.y / (c.z * c.z), 0.0);\n' +
      '  mat3 T = J * transpose(mat3(u_r, u_u, u_f));\n' +
      '  mat3 C = T * S * transpose(T);\n' +
      '  float a = C[0][0] + 0.3, b = C[0][1], e = C[1][1] + 0.3;\n' +
      '  float mid = 0.5 * (a + e), rd = length(vec2(0.5 * (a - e), b)), l1 = mid + rd, l2 = mid - rd;\n' +
      '  if (l2 <= 0.0) return;\n' +
      '  vec2 dv = vec2(b, l1 - a); dv = dot(dv, dv) > 1e-12 ? normalize(dv) : (a >= e ? vec2(1.0, 0.0) : vec2(0.0, 1.0));\n' +
      '  vec2 mj = min(sqrt(2.0 * l1), 1024.0) * dv, mn = min(sqrt(2.0 * l2), 1024.0) * vec2(dv.y, -dv.x);\n' +
      '  vC = vec4(float(cv.w & 0xffu), float((cv.w >> 8) & 0xffu), float((cv.w >> 16) & 0xffu), float(cv.w >> 24)) / 255.0;\n' +
      '  vP = a_pos;\n' +
      '  gl_Position = vec4(nd + (a_pos.x * mj + a_pos.y * mn) * 2.0 / u_vp, 0.0, 1.0);\n' +
      '}\n',
      '#version 300 es\nprecision highp float;\nin vec4 vC; in vec2 vP; out vec4 o;\n' +
      'void main(){ float A = -dot(vP, vP); if (A < -4.0) discard; float B = exp(A) * vC.a; if (B < 0.004) discard; o = vec4(B * vC.rgb, B); }\n');
  } catch (e) { GS.prog = null; GS.err = String(e); }
  // 가우시안 위 땅 그림 (터파기 영역 · 관 띠 · 자른 선): 모델 그물에 그 그림만 (미리 곱한 알파) · 깊이 = 같은 그물
  let DCO = null;
  try {
    DCO = prog('in vec3 aP; uniform mat4 M; out vec2 w; void main(){ w = aP.xy; gl_Position = M * vec4(aP, 1.0); }',
      'in vec2 w; uniform sampler2D dg, dl; uniform vec4 rg, rl; out vec4 o;\n' +
      'void main(){ vec2 ug = (w - rg.xy) / rg.zw, ul = (w - rl.xy) / rl.zw; vec4 a = texture(dg, ug), b = texture(dl, ul);\n' +
      '  bool il = all(greaterThanEqual(ul, vec2(0.0))) && all(lessThanEqual(ul, vec2(1.0)));\n' +
      '  bool ig = all(greaterThanEqual(ug, vec2(0.0))) && all(lessThanEqual(ug, vec2(1.0)));\n' +
      '  vec4 d = il ? b : ig ? a : vec4(0.0); if (d.a < 0.004) discard; o = d; }');
  } catch (e) { DCO = null; }
  // 정렬 · 자료 텍스처 (Worker 안 · 안 되면 이 창에서): gsCore(post) → 받은 글 처리 함수
  function gsCore(post) {
    let pos = null, n = 0;
    const f1 = new Float32Array(1), i1 = new Int32Array(f1.buffer);
    function half(v) {                                                  // float → 반정밀 (antimatter15 floatToHalf)
      f1[0] = v; const f = i1[0], sign = (f >> 31) & 0x0001, exp = (f >> 23) & 0x00ff;
      let frac = f & 0x007fffff, ne;
      if (exp === 0) ne = 0;
      else if (exp < 113) { ne = 0; frac |= 0x00800000; frac = frac >> (113 - exp); if (frac & 0x01000000) { ne = 1; frac = 0; } }
      else if (exp < 142) ne = exp - 112;
      else { ne = 31; frac = 0; }
      return (sign << 15) | (ne << 10) | (frac >> 13);
    }
    const pack = (a, b) => (half(a) | (half(b) << 16)) >>> 0;
    function build(buf, cnt) {
      const fb = new Float32Array(buf), ub = new Uint8Array(buf), tw = 2048, th = Math.max(1, Math.ceil(2 * cnt / tw));
      const td = new Uint32Array(tw * th * 4), tf = new Float32Array(td.buffer), tc = new Uint8Array(td.buffer);
      pos = new Float32Array(cnt * 3); n = cnt;
      for (let i = 0; i < cnt; i++) {
        const x = fb[8 * i], y = fb[8 * i + 1], z = fb[8 * i + 2];
        tf[8 * i] = x; tf[8 * i + 1] = y; tf[8 * i + 2] = z; pos[3 * i] = x; pos[3 * i + 1] = y; pos[3 * i + 2] = z;
        for (let k = 0; k < 4; k++) tc[4 * (8 * i + 7) + k] = ub[32 * i + 24 + k];
        const sx = fb[8 * i + 3], sy = fb[8 * i + 4], sz = fb[8 * i + 5];
        const w = (ub[32 * i + 28] - 128) / 128, qx = (ub[32 * i + 29] - 128) / 128, qy = (ub[32 * i + 30] - 128) / 128, qz = (ub[32 * i + 31] - 128) / 128;
        const M = [1 - 2 * (qy * qy + qz * qz), 2 * (qx * qy + w * qz), 2 * (qx * qz - w * qy),
          2 * (qx * qy - w * qz), 1 - 2 * (qx * qx + qz * qz), 2 * (qy * qz + w * qx),
          2 * (qx * qz + w * qy), 2 * (qy * qz - w * qx), 1 - 2 * (qx * qx + qy * qy)];
        for (let k = 0; k < 3; k++) { M[k] *= sx; M[3 + k] *= sy; M[6 + k] *= sz; }     // 줄 k = 회전 축 k × 크기 k
        const s00 = M[0] * M[0] + M[3] * M[3] + M[6] * M[6], s01 = M[0] * M[1] + M[3] * M[4] + M[6] * M[7], s02 = M[0] * M[2] + M[3] * M[5] + M[6] * M[8];
        const s11 = M[1] * M[1] + M[4] * M[4] + M[7] * M[7], s12 = M[1] * M[2] + M[4] * M[5] + M[7] * M[8], s22 = M[2] * M[2] + M[5] * M[5] + M[8] * M[8];
        td[8 * i + 4] = pack(s00, s01); td[8 * i + 5] = pack(s02, s11); td[8 * i + 6] = pack(s12, s22);
      }
      return { td: td, tw: tw, th: th };
    }
    let dep = null, cnt16 = null;
    function sort(e, f) {                                               // 뒤 → 앞 (먼 것부터) · 16 bit 계수 정렬
      if (!dep || dep.length !== n) { dep = new Int32Array(n); cnt16 = new Uint32Array(65536); }
      let lo = Infinity, hi = -Infinity;
      for (let i = 0; i < n; i++) {
        const d = (pos[3 * i] - e[0]) * f[0] + (pos[3 * i + 1] - e[1]) * f[1] + (pos[3 * i + 2] - e[2]) * f[2];
        const q = (d * 1024) | 0; dep[i] = q; if (q < lo) lo = q; if (q > hi) hi = q;
      }
      const k = 65535 / Math.max(1, hi - lo);
      cnt16.fill(0);
      for (let i = 0; i < n; i++) { const b = 65535 - (((dep[i] - lo) * k) | 0); dep[i] = b; cnt16[b]++; }
      for (let i = 1; i < 65536; i++) cnt16[i] += cnt16[i - 1];
      const idx = new Uint32Array(n);
      for (let i = n - 1; i >= 0; i--) idx[--cnt16[dep[i]]] = i;
      return idx;
    }
    return function (m) {
      if (m.buf) { const r = build(m.buf, m.n); post({ tex: r.td, tw: r.tw, th: r.th }, [r.td.buffer]); }
      else if (m.e) { const idx = sort(m.e, m.f); post({ idx: idx, key: m.key }, [idx.buffer]); }
    };
  }
  function gsMsg(d) {
    if (d.tex) {
      const t = gl.createTexture(); gl.bindTexture(gl.TEXTURE_2D, t);
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA32UI, d.tw, d.th, 0, gl.RGBA_INTEGER, gl.UNSIGNED_INT, d.tex);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
      if (gl.getError() === gl.OUT_OF_MEMORY) { gl.deleteTexture(t); gsFail('그래픽 메모리 모자람'); return; }
      GS.tex = t;
      GS.vao = gl.createVertexArray(); gl.bindVertexArray(GS.vao);
      const qb = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, qb); gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-2, -2, 2, -2, 2, 2, -2, 2]), gl.STATIC_DRAW);
      gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
      GS.ibuf = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, GS.ibuf); gl.bufferData(gl.ARRAY_BUFFER, 4, gl.DYNAMIC_DRAW);
      gl.enableVertexAttribArray(1); gl.vertexAttribIPointer(1, 1, gl.INT, 0, 0); gl.vertexAttribDivisor(1, 1);
      gl.bindVertexArray(null);
      GS.ready = true; GS.busy = false; GS.key = '';
      if (typeof Worker !== 'undefined' && GS.wk instanceof Worker) GS.wk.onerror = (e) => { e.preventDefault(); gsFail('Worker · ' + (e.message || '')); };   // 받은 자료 (수백 MB) 를 놓아줌
      if (GS.res) { GS.res(); GS.res = null; }
      redraw();
    } else if (d.idx) {
      gl.bindBuffer(gl.ARRAY_BUFFER, GS.ibuf); gl.bufferData(gl.ARRAY_BUFFER, new Int32Array(d.idx.buffer), gl.DYNAMIC_DRAW);
      GS.sorted = d.idx.length; GS.sorts++; GS.key = d.key; GS.busy = false;
      redraw();
    }
  }
  function gsFail(msg) {
    GS.err = msg; GS.loading = null; GS.busy = false;
    if (GS.rej) { GS.rej(new Error(msg)); GS.rej = null; }
    if (GS.on) gsSet(false);
    toast('가우시안 못 불러옴 · ' + msg);
  }
  function gsLoad() {
    if (GS.loading) return GS.loading;
    GS.loading = new Promise((res, rej) => { GS.res = res; GS.rej = rej; });
    $('load').style.display = ''; $('load').textContent = '가우시안 불러오는 중…';
    (async () => {
      const meta = await (await fetch('data/gs/gs.json')).json();
      const r = await fetch('data/gs/' + (meta.file || 'splats.bin'));
      if (!r.ok) throw new Error('splats.bin ' + r.status);
      const all = await r.arrayBuffer(), cap = 1024 * Math.min(maxTex, 16384), n = Math.min(Math.floor(all.byteLength / 32), cap);   // 텍스처 줄 한도 (1024개 / 줄)
      if (!n) throw new Error('자료 없음');
      const buf = n * 32 < all.byteLength ? all.slice(0, n * 32) : all;          // 파일 = 중요한 것부터 → 한도면 앞에서부터
      if (n < all.byteLength / 32) toast('가우시안 ' + n.toLocaleString() + '개만 (그래픽 한도)');
      GS.meta = meta; GS.n = n;
      const local = () => {                                             // Worker 를 못 쓰는 곳 (보안 설정) → 이 창에서 (정렬 동안 잠깐 멈춤)
        const h = gsCore((m) => setTimeout(() => gsMsg(m), 0));
        GS.wk = { postMessage: (m) => h(m) };
        GS.wk.postMessage({ buf: buf, n: n });
      };
      GS.busy = true;
      try {
        const url = URL.createObjectURL(new Blob(['const h = (' + gsCore.toString() + ')((m, t) => postMessage(m, t)); onmessage = (e) => h(e.data);'], { type: 'text/javascript' }));
        GS.wk = new Worker(url);
        GS.wk.onmessage = (e) => gsMsg(e.data);
        GS.wk.onerror = (e) => { e.preventDefault(); if (GS.ready) gsFail('Worker · ' + (e.message || '')); else { GS.wk.terminate(); local(); } };
        GS.wk.postMessage({ buf: buf, n: n });                          // 복사로 넘김 (Worker 가 안 뜨면 이 창에서 같은 자료로)
      } catch (e) { local(); }
    })().catch((e) => gsFail(errText(e))).finally(() => { if (mesh || !GS.on) $('load').style.display = 'none'; });
    return GS.loading;
  }
  // 정렬을 다시 할지 (눈 · 앞 방향이 조금이라도 바뀌면 · 정렬 하나가 도는 동안은 다음 하나만 기다림)
  function gsSortSoon(vw) {
    const k = vw.E.map((v) => v.toFixed(2)).join(',') + '|' + vw.f.map((v) => v.toFixed(3)).join(',');
    GS.want = k;
    if (GS.busy || k === GS.key) return;
    GS.busy = true;
    GS.wk.postMessage({ e: vw.E, f: vw.f, key: k });
  }
  function gsDraw(vw) {
    if (!GS.ready || !GS.sorted) { gsSortSoon(vw); return false; }
    gl.useProgram(GS.prog.p);
    gl.activeTexture(gl.TEXTURE3); gl.bindTexture(gl.TEXTURE_2D, GS.tex); gl.uniform1i(GS.prog.u.u_tex, 3); gl.activeTexture(gl.TEXTURE0);
    gl.uniform3fv(GS.prog.u.u_eye, vw.E); gl.uniform3fv(GS.prog.u.u_r, vw.r); gl.uniform3fv(GS.prog.u.u_u, vw.u); gl.uniform3fv(GS.prog.u.u_f, vw.f);
    gl.uniform2f(GS.prog.u.u_tan, vw.tanH, vw.tanV); gl.uniform2f(GS.prog.u.u_vp, vw.W, vw.H); gl.uniform1f(GS.prog.u.u_near, Math.max(0.05, V.d * 0.005));
    gl.disable(gl.DEPTH_TEST); gl.depthMask(false);
    gl.enable(gl.BLEND); gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
    gl.bindVertexArray(GS.vao);
    gl.drawArraysInstanced(gl.TRIANGLE_FAN, 0, 4, GS.sorted);
    gl.bindVertexArray(null);
    gl.disable(gl.BLEND); gl.depthMask(true); gl.enable(gl.DEPTH_TEST);
    gsSortSoon(vw);                                                     // 지금 시점이 정렬한 시점과 다르면 다시 (정렬이 오면 다시 그림)
    return true;
  }
  // 깊이 · 고르기 그물: 모델 (없으면 맨땅)
  const gsSurfDT = () => !!DT.parts && !!mesh && !mesh.parts.length;
  function gsSet(on) {
    on = !!on && GS.ok && !!GS.prog;
    GS.on = on;
    if (on && DT.on) dtSet(false);                                      // 맨땅 보기와 하나만
    try { sessionStorage.setItem(GS_KEY, on ? '1' : '0'); } catch (e) { /* 저장 못 해도 계속 */ }
    setOn($('bGs'), on);
    $('bGs').title = on ? '모델 보기 (질감 3D)' : '가우시안 보기 (가우시안 스플래팅)';
    if (on) { if (DEM && DTP && mesh && !mesh.parts.length) dtBuild(); gsLoad().then(() => redraw(), () => {}); }
    redraw();
  }

  // ---------------------------------------------------------------- 관로 자료 (360 보기와 같음)
  let PO = { E: ORG[0], N: ORG[1] }, TX = 0, TY = 0;
  function poSet(D) {
    const o = D && D.origin;
    PO = o && isNum(o.E) && isNum(o.N) ? { E: Number(o.E), N: Number(o.N) } : { E: ORG[0], N: ORG[1] };
    TX = PO.E - ORG[0]; TY = PO.N - ORG[1];
  }
  poSet(window.RV_PIPES);
  const demP = (e, n) => demAt(e + TX, n + TY);       // 관로 좌표 (e, n) 의 땅
  // dem.js 가 없을 때 땅: 파일에 있던 관 땅 높이 (40 m 칸 · 가까운 6 개 거리 가중 · 360 의 사진 땅 높이처럼)
  const ED_R = 40, ED_K = 6, gCell = new Map();
  function gSampBuild() {
    gCell.clear();
    const add = (D) => (D && Array.isArray(D.lines) ? D.lines : []).forEach((l) => {
      if (!l || !Array.isArray(l.pts) || !Array.isArray(l.g)) return;
      l.pts.forEach((q, k) => {
        if (!Array.isArray(q) || !isNum(q[0]) || !isNum(q[1]) || !isNum(l.g[k])) return;
        const c = Math.floor(q[0] / ED_R) + ',' + Math.floor(q[1] / ED_R);
        if (!gCell.has(c)) gCell.set(c, []);
        gCell.get(c).push([Number(q[0]), Number(q[1]), Number(l.g[k])]);
      });
    });
    add(window.RV_PIPES);
  }
  gSampBuild();
  // 땅 높이 (관로 좌표): dem.js → 없으면 파일 땅 높이 거리 가중 → 없으면 NaN
  function groundAt(e, n) {
    const z = demP(e, n);
    if (Number.isFinite(z)) return z;
    const cx = Math.floor(e / ED_R), cy = Math.floor(n / ED_R), near = [];
    for (let a = -1; a <= 1; a++) for (let b = -1; b <= 1; b++) for (const s of gCell.get((cx + a) + ',' + (cy + b)) || []) { const d = Math.hypot(s[0] - e, s[1] - n); if (d <= ED_R) near.push([d, s[2]]); }
    if (!near.length) return NaN;
    near.sort((p, q) => p[0] - q[0]);
    let sw = 0, sz = 0;
    near.slice(0, ED_K).forEach(([d, g]) => { const w = 1 / Math.pow(Math.max(d, 1), 2); sw += w; sz += w * g; });
    return sz / sw;
  }
  // 관 줄 땅 높이 (JACK 1126 «관로가 울퉁불퉁»): 측점 1 m 마다 땅 (dem.js · 없으면 gIn 을 측점 따라 선형) → ±5 m 이동평균 → 꼭짓점마다.
  function smoothG(p, cum, gIn) {
    const cnt = cum.length, len = cum[cnt - 1], out = new Float64Array(cnt).fill(NaN);
    const gs = [], gv = [];
    for (let k = 0; k < cnt; k++) if (Number.isFinite(gIn[k])) { gs.push(cum[k]); gv.push(gIn[k]); }
    let fj = 0;
    const fileAt = (s) => {
      if (!gs.length) return NaN;
      if (s <= gs[0]) return gv[0];
      if (s >= gs[gs.length - 1]) return gv[gv.length - 1];
      while (fj > 0 && gs[fj] > s) fj--;
      while (fj + 2 < gs.length && gs[fj + 1] < s) fj++;
      const l = gs[fj + 1] - gs[fj];
      return l > 0 ? gv[fj] + (gv[fj + 1] - gv[fj]) * (s - gs[fj]) / l : gv[fj];
    };
    if (!(len > 1e-6)) { for (let k = 0; k < cnt; k++) { const z = demP(p[k * 3], p[k * 3 + 1]); out[k] = Number.isFinite(z) ? z : gIn[k]; } return out; }
    const n = Math.max(1, Math.ceil(len / G_STEP)), ds = len / n, R = new Float64Array(n + 1).fill(NaN);
    let j = 0, dem = 0;
    for (let i = 0; DEM && i <= n; i++) {
      const s = i * ds;
      while (j + 2 < cnt && cum[j + 1] < s) j++;
      const l = cum[j + 1] - cum[j], t = l > 0 ? clamp((s - cum[j]) / l, 0, 1) : 0;
      R[i] = demP(p[j * 3] + (p[j * 3 + 3] - p[j * 3]) * t, p[j * 3 + 1] + (p[j * 3 + 4] - p[j * 3 + 1]) * t);
      if (Number.isFinite(R[i])) dem++;
    }
    if (!dem) for (let i = 0; i <= n; i++) R[i] = fileAt(i * ds);   // dem.js 가 이 줄에 없음 → 파일 g (DSM) 를 같은 식으로
    // dem.js 밖 · 빈 칸은 이동평균 뒤 가까운 값 (DSM 을 섞지 않음 · 프로그램 import_pipes 와 같음)
    const h = Math.max(1, Math.round(G_HALF / ds)), cs = new Float64Array(n + 2), cn = new Float64Array(n + 2), S2 = new Float64Array(n + 1);
    for (let i = 0; i <= n; i++) { const ok = Number.isFinite(R[i]); cs[i + 1] = cs[i] + (ok ? R[i] : 0); cn[i + 1] = cn[i] + (ok ? 1 : 0); }
    let any = false;
    for (let i = 0; i <= n; i++) { const a = Math.max(0, i - h), b = Math.min(n, i + h), c = cn[b + 1] - cn[a]; S2[i] = c > 0 ? (cs[b + 1] - cs[a]) / c : NaN; any = any || c > 0; }
    if (!any) return out;
    for (let i = 1; i <= n; i++) if (!Number.isFinite(S2[i]) && Number.isFinite(S2[i - 1])) S2[i] = S2[i - 1];   // 빈 곳 = 가까운 값
    for (let i = n - 1; i >= 0; i--) if (!Number.isFinite(S2[i]) && Number.isFinite(S2[i + 1])) S2[i] = S2[i + 1];
    for (let k = 0; k < cnt; k++) {
      const f = cum[k] / ds, i0 = Math.min(n - 1, Math.max(0, Math.floor(f))), t = clamp(f - i0, 0, 1);
      out[k] = S2[i0] * (1 - t) + S2[i0 + 1] * t;
    }
    return out;
  }
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
  function ctrlOf(l) {
    const Pp = l && Array.isArray(l.pts) ? l.pts : [], all = [];
    Pp.forEach((q, k) => { if (ptOk(q)) all.push(k); });
    if (!Array.isArray(l.ctrl)) return all;
    const c = [...new Set(l.ctrl.filter((k) => Number.isInteger(k) && k >= 0 && k < Pp.length && ptOk(Pp[k])))].sort((a, b) => a - b);
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
    const diaKnown = isNum(l.dia) && Number(l.dia) > 0;
    const dia = diaKnown ? Number(l.dia) : 0.2;
    const base = String(l.label || lay.name || '');
    const dTxt0 = 'D' + Math.round(dia * 1000);
    const kind = String(lay.name || '');
    const label = !diaKnown ? base : !base || base === dTxt0 ? dTxt0 : base.indexOf(dTxt0 + ' ') === 0 ? base : dTxt0 + ' · ' + base;
    const bi = [0];
    for (let k = 3; k < p.length; k += 3) {
      const j = bi[bi.length - 1] * 3;
      if (Math.hypot(p[k] - p[j], p[k + 1] - p[j + 1]) > 1e-6) bi.push(k / 3);
    }
    const rgb = hexRgb(lay.color), rgbHi = rgb.map((v) => v + (1 - v) * 0.4);
    const layName = Number.isInteger(l.layer) && l.layer >= 0 && l.layer < pipeLayers.length ? pipeLayers[l.layer].name : '';
    const mat = MAT_T.indexOf(l.mat) >= 0 ? l.mat : guessMat(String(l.label || '') + ' ' + layName);
    const cum = new Float64Array(p.length / 3);
    for (let k = 3; k < p.length; k += 3) cum[k / 3] = cum[k / 3 - 1] + Math.hypot(p[k] - p[k - 3], p[k + 1] - p[k - 2]);
    const len = cum[cum.length - 1];
    const shore = (Array.isArray(l.shore) ? l.shore : []).map((q, i) => (q && isNum(q.a) && isNum(q.b) && SHORE_T.indexOf(q.t) >= 0
      ? { a: clamp(Math.min(+q.a, +q.b), 0, len), b: clamp(Math.max(+q.a, +q.b), 0, len), t: q.t, i: i } : null))
      .filter((q) => q && q.b - q.a > 1e-3).sort((x, y) => x.a - y.a);
    const gs = smoothG(p, cum, g);                                   // 매끈한 땅 (보기 · 터파기 · 수량)
    return {
      src: src, color: lay.color, rgb: rgb, rgbHi: rgbHi, colorHi: 'rgb(' + rgbHi.map((v) => Math.round(v * 255)).join(',') + ')',
      label: label, base: base, kind: kind, diaKnown: diaKnown, dia: dia, hasZ: hasZ, pts: Float64Array.from(p), g: gs, gRaw: Float64Array.from(g), bi: bi,
      ctrl: ctrlOf(l).map((k) => at[k]).filter((k) => k >= 0), e0, e1, n0, n1, cum: cum, len: len, shore: shore, mat: mat,
    };
  }
  function shoreAt(L, s) {
    for (const q of L.shore) if (s > q.a && s < q.b) return q;
    return null;
  }
  let lines = buildLines(window.RV_PIPES);

  // ---------------------------------------------------------------- 상태
  const Q = new URLSearchParams(location.search);
  const MODES = ['area', 'trench'];
  const qMode = String(Q.get('mode') || '').toLowerCase();
  const mode0 = ['trench', 'depth', 'open', 'sec'].indexOf(qMode) >= 0 ? 'trench' : 'area';
  const S = { pipes: true, mode: mode0, depth: mode0 === 'trench', cover: Math.max(0, num(P.cover, num(P.depth, 1.2))), hnd: [], vtx: [] };
  const ED = { on: false, sel: -1, hi: -1, hov: -1, down: null, want: null, undo: [], redo: [], dirty: new Set(), orig: new Map(), origSig: '', origEdited: false, shpBusy: false, Lof: null, Lsel: -2, L: null };
  let pipeGen = 0, selGen = 0;
  const KEY0 = String(P.title || 'project') + ':3d';
  const TR_DEF = { tp: 0.2, n: 0.3, margin: 0.3, bed: 0.1, prot: 0.3, on: true };
  const TR_LIM = { tp: [0, 3], n: [0, 3], margin: [0, 3], bed: [0, 1], prot: [0, 3] };
  const TR_KEY = 'rv_trench:' + KEY0;
  function trClean(o, base) {
    o = o || {};
    const t = {};
    Object.keys(TR_LIM).forEach((k) => { t[k] = isNum(o[k]) ? round(clamp(Number(o[k]), TR_LIM[k][0], TR_LIM[k][1]), 3) : base[k]; });
    t.on = typeof o.on === 'boolean' ? o.on : base.on;
    return t;
  }
  let TR = trClean(lsGet(TR_KEY), trClean(window.RV_CALIB && window.RV_CALIB.trench, TR_DEF));
  let trOff = Q.get('trench') === '0';
  const trOn = () => TR.on && !trOff;
  const trDefault = () => Object.keys(TR_LIM).every((k) => TR[k] === TR_DEF[k]);
  const SHOW_KEY = 'rv_ui:show';
  const SHOW = (function () { const v = lsGet(SHOW_KEY) || {}; return { lbl: v.lbl !== false, tmp: v.tmp !== false, perm: v.perm !== false }; })();
  const SH = { arm: null, a: null, sig: '' };
  let xray = true, dirty = true;
  const redraw = () => { dirty = true; };

  // ---------------------------------------------------------------- 설정 저장 (360 과 같은 길: save_calib · calib.js 의 trench · 이 브라우저)
  let hostCalT = 0;
  function calibText() {
    const C = window.RV_CALIB && typeof window.RV_CALIB === 'object' ? JSON.parse(JSON.stringify(window.RV_CALIB)) : {};
    C.trench = { tp: TR.tp, n: TR.n, margin: TR.margin, bed: TR.bed, prot: TR.prot, on: TR.on };
    window.RV_CALIB = JSON.parse(JSON.stringify(C));
    return 'window.RV_CALIB = ' + JSON.stringify(C, null, 2) + ';\n';
  }
  function hostCalSoon() { if (!HOST()) return; clearTimeout(hostCalT); hostCalT = setTimeout(hostCalFlush, 800); }
  function hostCalFlush() {
    clearTimeout(hostCalT); hostCalT = 0;
    return hostCall('save_calib', calibText()).then((r) => { if (!r || r.ok === false) toast('설정 저장 안 됨'); return r; }, () => { toast('설정 저장 안 됨'); return null; });
  }
  beforeHome = () => (hostCalT ? hostCalFlush() : Promise.resolve());
  window.addEventListener('pagehide', () => { if (hostCalT) hostCalFlush(); });

  // ---------------------------------------------------------------- 그물 그릇 (꼭짓점 VF 개: 자리 3 + 법선 3 + 색 3 + 덧값 4)
  const VF = 13;
  const meshNew = () => ({ vao: null, vbo: null, ibo: null, n: 0, no: 0, ns: 0, key: '', o: [0, 0, 0] });
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
    return m;
  }
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
  const tubeM = [meshInit(meshNew()), meshInit(meshNew())], pitM = [meshInit(meshNew()), meshInit(meshNew())], volM = [meshInit(meshNew()), meshInit(meshNew())], obM = meshInit(meshNew());
  const MO = [0, 0, H0];                // 관 · 구덩이 그물 원점 (관로 좌표 · 표고 절대)
  const NO_MESH = { o: MO, v: new Float32Array(0), i: new Uint32Array(0), no: 0, ns: 0 };

  // ---------------------------------------------------------------- 관 (360 보기와 같은 그물: 관종 색 · 6 m 이음 · 곡관)
  const TUBE_SIDES = 20;
  function gAt(L, k) { const g = L.g[k]; if (Number.isFinite(g)) return g; const q = groundAt(L.pts[k * 3], L.pts[k * 3 + 1]); return Number.isFinite(q) ? q : H0; }
  function zcAt(L, k) { const z = L.pts[k * 3 + 2]; return L.hasZ && Number.isFinite(z) ? z : gAt(L, k) - S.cover - L.dia / 2; }
  const edSel = () => (ED.on ? ED.sel : -1);
  function tubesPut(sel, base) {
    meshPut(tubeM[0], base + '|' + pipeGen + '|' + sel, () => buildTubes((L) => L.src !== sel, false));
    meshPut(tubeM[1], sel < 0 ? '' : base + '|' + pipeGen + '|' + selGen + '|' + sel, () => (sel < 0 ? NO_MESH : buildTubes((L) => L.src === sel, true)));
  }
  function obPut() { meshPut(obM, 'o|' + obGen + '|' + S.cover, () => buildTubes(() => true, false, obst, obZ)); }
  function buildTubes(use, hi, list, zf) {
    const o = MO.slice(), VB = [], IB = [], fit = { joints: [], bends: [] };
    zf = zf || zcAt;
    for (const L of list || lines) {
      if (!use(L)) continue;
      const Pp = L.pts, cnt = Pp.length / 3, r = L.dia / 2, mat = L.mat;
      let run = [], rs = [];
      const flush = () => {
        if (run.length >= 6) {
          if (mat) pipeRun(run, rs, r, mat, hi, o, VB, IB, fit);
          else addTube(run, r, hi ? L.rgbHi : L.rgb, VB, IB);
        }
        run = []; rs = [];
      };
      const push = (k) => {
        const x = Pp[k * 3] - o[0], y = Pp[k * 3 + 1] - o[1], z = zf(L, k) - o[2], n = run.length;
        if (n && Math.abs(run[n - 3] - x) + Math.abs(run[n - 2] - y) + Math.abs(run[n - 1] - z) < 1e-6) return;
        run.push(x, y, z);
        rs.push(L.cum ? L.cum[k] : 0);
      };
      for (let k = 0; k + 1 < cnt; k++) {
        if (!run.length) push(k);
        push(k + 1);
      }
      flush();
    }
    return { o: o, v: Float32Array.from(VB), i: Uint32Array.from(IB), fit: fit };
  }
  const FIT_R = Infinity;     // 360 은 사진에서 120 m 안만 · 3D 는 모두
  const PIPE_LEN = 6;
  const BEND_DEG = 2;
  const HI_TINT = [1, 0.86, 0.3];
  function pipeRun(c, rs, r, mat, hi, o, VB, IB, fit) {
    const tint = (k) => MAT_RGB[mat].map((v, i) => { const q = clamp(v * k, 0, 1); return hi ? q + (HI_TINT[i] - q) * 0.35 : q; });
    const body = tint(1), m = c.length / 3;
    const C = (j) => [c[j * 3], c[j * 3 + 1], c[j * 3 + 2]];
    const near = (p) => Math.hypot(p[0], p[1], p[2]) < FIT_R;
    const dirs = [];
    for (let j = 0; j + 1 < m; j++) { const a = C(j), b = C(j + 1); dirs.push(unit3([b[0] - a[0], b[1] - a[1], b[2] - a[2]])); }
    const len = (j) => { const a = C(j), b = C(j + 1); return Math.hypot(b[0] - a[0], b[1] - a[1], b[2] - a[2]); };
    const bend = new Map();
    for (let j = 1; j + 1 < m; j++) {
      const t1 = dirs[j - 1], t2 = dirs[j], ang = Math.acos(clamp(dot3(t1, t2), -1, 1)) * R2D;
      if (ang <= BEND_DEG || ang > 120 || !near(C(j))) continue;
      const Lf = Math.min(Math.max(0.25, 1.1 * 2 * r), 0.45 * len(j - 1), 0.45 * len(j));
      if (Lf < 0.05) continue;
      const Vv = C(j), A = Vv.map((v, i) => v - t1[i] * Lf), B = Vv.map((v, i) => v + t2[i] * Lf);
      const ns = clamp(Math.ceil(ang / 8), 3, 12), arc = [];
      for (let q = 0; q <= ns; q++) {
        const u = q / ns, a = (1 - u) * (1 - u), b = 2 * (1 - u) * u, d = u * u;
        arc.push([A[0] * a + Vv[0] * b + B[0] * d, A[1] * a + Vv[1] * b + B[1] * d, A[2] * a + Vv[2] * b + B[2] * d]);
      }
      bend.set(j, { A: A, B: B, t1: t1, t2: t2, arc: arc, Lf: Lf });
      fit.bends.push([Vv[0] + o[0], Vv[1] + o[1], Vv[2] + o[2], ang]);
    }
    const pts = [];
    for (let j = 0; j < m; j++) {
      const bd = bend.get(j);
      if (bd) bd.arc.forEach((p) => pts.push(p[0], p[1], p[2])); else pts.push(...C(j));
    }
    addTube(pts, r, body, VB, IB);
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
    for (const bd of bend.values()) {
      const pa = bd.A.map((v, i) => v - bd.t1[i] * 0.004), pb = bd.B.map((v, i) => v + bd.t2[i] * 0.004);
      const path = [pa].concat(bd.arc, [pb]), flat = [];
      path.forEach((p) => flat.push(p[0], p[1], p[2]));
      addTube(flat, r * (mat === 'plastic' ? 1.08 : 1.06), tint(mat === 'ci' ? 1.08 : mat === 'steel' ? 0.9 : 0.95), VB, IB);
      fitEnd(bd.A, bd.t1.map((v) => -v), r, mat, tint, VB, IB);
      fitEnd(bd.B, bd.t2, r, mat, tint, VB, IB);
    }
  }
  function frustum(Pp, t, u0, u1, r0, r1, col, VB, IB, cap0, cap1, ri) {
    const N = TUBE_SIDES;
    let A = cross3(t, [0, 0, 1]);
    if (Math.hypot(A[0], A[1], A[2]) < 1e-6) A = cross3(t, [1, 0, 0]);
    A = unit3(A);
    const B = cross3(t, A), k = (r0 - r1) / Math.max(u1 - u0, 1e-6);
    const vert = (p, n) => { VB.push(p[0], p[1], p[2], n[0], n[1], n[2], col[0], col[1], col[2], 0, 1, 99, 99); };
    const ring = (u, rr) => { const out = []; for (let s = 0; s < N; s++) { const a = s / N * 2 * Math.PI, d = [Math.cos(a) * A[0] + Math.sin(a) * B[0], Math.cos(a) * A[1] + Math.sin(a) * B[1], Math.cos(a) * A[2] + Math.sin(a) * B[2]]; out.push([Pp[0] + t[0] * u + d[0] * rr, Pp[1] + t[1] * u + d[1] * rr, Pp[2] + t[2] * u + d[2] * rr, d]); } return out; };
    const R0 = ring(u0, r0), R1 = ring(u1, r1), b = VB.length / VF;
    for (const R of [R0, R1]) R.forEach((q) => vert(q, unit3([q[3][0] + t[0] * k, q[3][1] + t[1] * k, q[3][2] + t[2] * k])));
    for (let s = 0; s < N; s++) { const s1 = (s + 1) % N; IB.push(b + s, b + s1, b + N + s1, b + s, b + N + s1, b + N + s); }
    const annulus = (R, u, rIn, sg) => {
      const I = ring(u, rIn), n = [t[0] * sg, t[1] * sg, t[2] * sg], c = VB.length / VF;
      R.forEach((q) => vert(q, n)); I.forEach((q) => vert(q, n));
      for (let s = 0; s < N; s++) { const s1 = (s + 1) % N; if (sg > 0) IB.push(c + s, c + s1, c + N + s1, c + s, c + N + s1, c + N + s); else IB.push(c + s, c + N + s1, c + s1, c + s, c + N + s, c + N + s1); }
    };
    if (cap0) annulus(R0, u0, ri, -1);
    if (cap1) annulus(R1, u1, ri, 1);
  }
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
  function addTube(c, r, col, VB, IB) {
    const m = c.length / 3 - 1, N = TUBE_SIDES;
    const C = (j) => [c[j * 3], c[j * 3 + 1], c[j * 3 + 2]];
    const t = [];
    for (let i = 0; i < m; i++) t.push(unit3([c[i * 3 + 3] - c[i * 3], c[i * 3 + 4] - c[i * 3 + 1], c[i * 3 + 5] - c[i * 3 + 2]]));
    for (let j = 1; j < m; j++) {
      if (dot3(t[j - 1], t[j]) < -0.6) {
        addTube(c.slice(0, (j + 1) * 3), r, col, VB, IB);
        addTube(c.slice(j * 3), r, col, VB, IB);
        return;
      }
    }
    let A = cross3(t[0], [0, 0, 1]);
    if (Math.hypot(A[0], A[1], A[2]) < 1e-6) A = cross3(t[0], [1, 0, 0]);
    A = unit3(A);
    let B = cross3(t[0], A);
    const dirs = (A2, B2) => {
      const d = [];
      for (let s = 0; s < N; s++) {
        const a = s / N * 2 * Math.PI, ca = Math.cos(a), sa = Math.sin(a);
        d.push([ca * A2[0] + sa * B2[0], ca * A2[1] + sa * B2[1], ca * A2[2] + sa * B2[2]]);
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
  function distPtSeg(px, py, ax, ay, bx, by) {
    const dx = bx - ax, dy = by - ay, L = dx * dx + dy * dy;
    const t = L > 0 ? clamp(((px - ax) * dx + (py - ay) * dy) / L, 0, 1) : 0;
    return Math.hypot(px - ax - t * dx, py - ay - t * dy);
  }

  // ---------------------------------------------------------------- 터파기 구덩이 (360 보기와 같은 식 · 관중심 터파기 · 회사 SAC 기준)
  const TR_TOL = 0.05;
  const TR_PAV = [0.40, 0.40, 0.42], TR_SOIL = [0.47, 0.33, 0.21], TR_FLOOR = [0.80, 0.72, 0.56];
  const LAY_CSS = { pav: '#45484f', fill: '#7d5634', prot: '#f0dfae', bed: '#d8b56a' };
  function sectAt(D, g, zc, ex, n) {
    n = n === undefined ? TR.n : n;
    const zs = Math.min(zc - D / 2, g - 0.01), zb = zs - TR.bed, wb = D / 2 + TR.margin + (ex || 0), zt = g - TR.tp;
    const wv = wb + n * Math.max(0, zt - zb);
    return zt > zs ? { zs, zb, wb, zt, wv, zm: zt, ws: wb + n * (zs - zb), n } : { zs, zb, wb, zt, wv, zm: zs, ws: wv, n };
  }
  const protTop = (x, zc, D) => Math.max(x.zs, Math.min(zc + D / 2 + TR.prot, x.zt));
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
  const TR_MAT = new Map([[TR_PAV, 1], [TR_SOIL, 2], [TR_FLOOR, 3]]);
  const SUN_SH = [0.45, -0.55];
  let trChains = null, trChainsOf = null, trSec = null, trSecKey = '';
  const trGL = () => S.pipes && S.depth && trOn() && !!TT && lines.length > 0;
  function trChainsGet() {
    if (trChains && trChainsOf === lines) return trChains;
    trChainsOf = lines;
    const ends = [];
    lines.forEach((L, i) => {
      if (L.bi.length < 2) return;
      const Pp = L.pts, b = L.bi, z = b.length - 1;
      for (const [s, v, w] of [[0, b[0], b[1]], [1, b[z], b[z - 1]]]) {
        const dx = Pp[w * 3] - Pp[v * 3], dy = Pp[w * 3 + 1] - Pp[v * 3 + 1], dl = Math.hypot(dx, dy) || 1;
        ends.push({ i: i, s: s, e: Pp[v * 3], n: Pp[v * 3 + 1], dx: dx / dl, dy: dy / dl });
      }
    });
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
    const link = new Int32Array(ends.length).fill(-1);
    for (const g of groups.values()) {
      const free = g.slice();
      while (free.length >= 2) {
        let best = null, bc = Infinity;
        for (let x = 0; x < free.length; x++) {
          for (let y = x + 1; y < free.length; y++) {
            const p = ends[free[x]], q = ends[free[y]], A = lines[p.i], B = lines[q.i];
            if (p.i === q.i || Math.abs(A.dia - B.dia) > 1e-3 || A.hasZ !== B.hasZ) continue;
            const c = p.dx * q.dx + p.dy * q.dy;
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
    const walk = (k) => {
      const nd = [], k0 = [];
      while (k >= 0 && !used[k >> 1]) {
        used[k >> 1] = 1;
        const p = ends[k], L = lines[p.i], b = p.s ? L.bi.slice().reverse() : L.bi;
        for (let j = nd.length ? 1 : 0; j < b.length; j++) { nd.push(L, b[j]); k0.push(j ? b[j - 1] : -1); }
        k = link[k ^ 1];
      }
      nd.k0 = k0;
      return nd;
    };
    for (let k = 0; k < ends.length; k++) if (link[k] < 0 && !used[k >> 1]) raw.push(walk(k));
    for (let k = 0; k < ends.length; k++) if (!used[k >> 1]) raw.push(walk(k));
    const chains = raw.filter((nd) => nd.length >= 4).map((nd) => {
      const m = nd.length / 2, Pp = new Float64Array(m * 2);
      let e0 = Infinity, e1 = -Infinity, n0 = Infinity, n1 = -Infinity;
      for (let j = 0; j < m; j++) {
        const L = nd[j * 2], k = nd[j * 2 + 1];
        Pp[j * 2] = L.pts[k * 3]; Pp[j * 2 + 1] = L.pts[k * 3 + 1];
        e0 = Math.min(e0, Pp[j * 2]); e1 = Math.max(e1, Pp[j * 2]); n0 = Math.min(n0, Pp[j * 2 + 1]); n1 = Math.max(n1, Pp[j * 2 + 1]);
      }
      const ids = [...new Set(nd.filter((x, j) => !(j & 1)).map((L) => L.src))];
      return { nd: nd, k0: nd.k0, m: m, P: Pp, D: nd[0].dia, ids: ids, e0, e1, n0, n1, ext: [true, true], wall: [true, true] };
    });
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
          if (o === c) {
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
  function trSecGet() {
    const ch = trChainsGet();
    const key = [TR.tp, TR.n, TR.margin, TR.bed, TR.prot, S.cover].join('|');
    if (trSec && trSecKey === key && trSec.of === ch) return trSec;
    trSecKey = key;
    const out = [], ext = Math.max(TR.margin, 0.05);
    for (const c of ch) {
      const m = c.m, Pp = c.P, D = c.D;
      const tx = new Float64Array(m - 1), ty = new Float64Array(m - 1);
      for (let j = 0; j + 1 < m; j++) {
        const dx = Pp[j * 2 + 2] - Pp[j * 2], dy = Pp[j * 2 + 3] - Pp[j * 2 + 1], l = Math.hypot(dx, dy) || 1;
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
      const Vv = { PE: [], PN: [], LX: [], LY: [], TX: [], TY: [], G: [], ZC: [], ST: [] }, pc = [];
      const put = (q, dx, dy, st) => {
        Vv.PE.push(q[0]); Vv.PN.push(q[1]); Vv.LX.push(q[2]); Vv.LY.push(q[3]); Vv.G.push(q[4]); Vv.ZC.push(q[5]);
        Vv.TX.push(dx); Vv.TY.push(dy); Vv.ST.push(st);
      };
      let last = null;
      for (let j = 1; j < m; j++) {
        const L = c.nd[j * 2], sa = L.cum[c.k0[j]], sb = L.cum[c.nd[j * 2 + 1]], dx = tx[j - 1], dy = ty[j - 1];
        const ts = [0, 1];
        if (L.shore.length && Math.abs(sb - sa) > 1e-9) {
          const lo = Math.min(sa, sb), hi = Math.max(sa, sb);
          for (const q of L.shore) for (const s of [q.a, q.b]) if (s > lo + 1e-6 && s < hi - 1e-6) ts.push((s - sa) / (sb - sa));
          ts.sort((x, y) => x - y);
        }
        const at = (t) => (t <= 0 ? [Pp[j * 2 - 2], Pp[j * 2 - 1], lxj[j - 1], lyj[j - 1], gj[j - 1], zj[j - 1]]
          : t >= 1 ? [Pp[j * 2], Pp[j * 2 + 1], lxj[j], lyj[j], gj[j], zj[j]]
            : [Pp[j * 2 - 2] + (Pp[j * 2] - Pp[j * 2 - 2]) * t, Pp[j * 2 - 1] + (Pp[j * 2 + 1] - Pp[j * 2 - 1]) * t, -dy, dx,
              gj[j - 1] + (gj[j] - gj[j - 1]) * t, zj[j - 1] + (zj[j] - zj[j - 1]) * t]);
        for (let i = 0; i + 1 < ts.length; i++) {
          const t0 = ts[i], t1 = ts[i + 1];
          if (t1 - t0 < 1e-9) continue;
          const q = L.shore.length ? shoreAt(L, sa + (sb - sa) * (t0 + t1) / 2) : null, st = q ? q.t : '';
          if (!last) put(at(t0), dx, dy, st);
          else if (last.st !== st) put(last.q, dx, dy, st);
          const e = at(t1);
          put(e, dx, dy, st);
          last = { q: e, st: st };
          if (q) pc.push({ va: Vv.PE.length - 2, ls0: sa + (sb - sa) * t0, ls1: sa + (sb - sa) * t1, a: q.a, b: q.b, t: st });
        }
      }
      const n = Vv.PE.length, F = () => new Float64Array(n);
      const E = Float64Array.from(Vv.PE), N = Float64Array.from(Vv.PN), ZM = F(), ZS = F(), WV = F(), WS = F(), WB = F();
      for (let v = 0; v < n; v++) {
        const x = sectAt(D, Vv.G[v], Vv.ZC[v], 0, Vv.ST[v] ? 0 : undefined);
        ZS[v] = x.zs; WV[v] = x.wv; ZM[v] = x.zm; WS[v] = x.ws; WB[v] = x.wb;
      }
      if (c.ext[0]) { E[0] -= tx[0] * ext; N[0] -= ty[0] * ext; }
      if (c.ext[1]) { E[n - 1] += tx[m - 2] * ext; N[n - 1] += ty[m - 2] * ext; }
      const pad = ext + 10;
      out.push({ c, m: n, D: D, E, N, LX: Vv.LX, LY: Vv.LY, TX: Vv.TX, TY: Vv.TY, G: Vv.G, ST: Vv.ST,
        ZM, ZS, WV, WS, WB, pc, e0: c.e0 - pad, e1: c.e1 + pad, n0: c.n0 - pad, n1: c.n1 + pad });
    }
    out.of = ch;
    trSec = out;
    return out;
  }
  function trStaticSig(sel) {
    let s = '';
    for (const c of trChainsGet()) {
      if (c.ids.indexOf(sel) < 0) s += c.ids.join(',') + ':' + (+c.ext[0]) + (+c.ext[1]) + (+c.wall[0]) + (+c.wall[1]) + ';';
    }
    return s;
  }
  const SH_PANEL = [0.56, 0.6, 0.64], SH_YEL = [0.88, 0.66, 0.13], SH_HP = [0.34, 0.34, 0.35], SH_SHEET = [0.36, 0.35, 0.33], SH_WOOD = [0.52, 0.37, 0.22];
  const BOX_F = [[0, 1, 2, 3], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]];
  const SHORE_IN = { panel: 0.1, hpile: 0.03, sheet: 0.16 };
  const waleFits = (wb, D, t) => t !== 'panel' && wb - SHORE_IN[t] - 0.2 >= D / 2 + 0.15;
  function strutZ(g, zs, D) {
    const lo = zs + D + 0.25, z1 = Math.max(g - 0.6, lo), out = [];
    if (z1 > g - 0.15) return out;
    out.push(z1);
    if (g - zs > 2.5) { const z2 = Math.max(lo, zs + 0.9); if (z1 - z2 >= 0.8) out.push(z2); }
    return out;
  }
  // 3D 모델 높이 지도 (위에서 본 가장 높은 면 · 관로 범위 · 모델을 다 읽은 뒤 한 번): 구덩이 벽을 그 자리 모델 면까지만 올림
  const HM = { ver: 0, z: null, x0: 0, y0: 0, cell: 1, n: 0 };
  function hmSync() {
    if (!mesh || !mesh.done || !fext) return;
    let bb = dcBox(true);
    if (!bb) return;
    if (HM.z && HM.of === mesh && bb[0] + 15 >= HM.x0 && bb[1] + 15 >= HM.y0 && bb[2] - 15 <= HM.x0 + HM.n * HM.cell && bb[3] - 15 <= HM.y0 + HM.n * HM.cell) return;
    HM.of = mesh;
    bb = [bb[0] - 30, bb[1] - 30, bb[2] + 30, bb[3] + 30];        // 끌어 옮겨도 자주 다시 안 만들게
    const W = bb[2] - bb[0], cell = Math.max(0.25, W / 1536), n = Math.ceil(W / cell), S2 = n * cell;
    const zlo = mesh.meta.min[2] - 5, zhi = mesh.meta.max[2] + 5, zr = zhi - zlo, cx = (bb[0] + bb[2]) / 2, cy = (bb[1] + bb[3]) / 2;
    const fb = gl.createFramebuffer(), tx = gl.createTexture(), dp = gl.createRenderbuffer();
    gl.bindTexture(gl.TEXTURE_2D, tx); gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA32F, n, n, 0, gl.RGBA, gl.FLOAT, null);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    gl.bindRenderbuffer(gl.RENDERBUFFER, dp); gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH_COMPONENT24, n, n);
    gl.bindFramebuffer(gl.FRAMEBUFFER, fb); gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tx, 0);
    gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, dp);
    gl.viewport(0, 0, n, n);
    gl.colorMask(true, true, true, true); gl.depthMask(true);
    gl.clearBufferfv(gl.COLOR, 0, [0, 0, 0, -1]); gl.clear(gl.DEPTH_BUFFER_BIT);
    gl.enable(gl.DEPTH_TEST); gl.depthFunc(gl.LESS); gl.disable(gl.BLEND); gl.disable(gl.STENCIL_TEST); gl.disable(gl.CULL_FACE);
    const M = [2 / S2, 0, 0, 0, 0, 2 / S2, 0, 0, 0, 0, -2 / zr, 0, -cx * 2 / S2, -cy * 2 / S2, (zlo + zhi) / zr, 1];   // 위에서 · 높은 면이 앞
    gl.useProgram(PICK.p); gl.uniformMatrix4fv(PICK.u.M, false, M); gl.uniform1f(PICK.u.id, 0);
    for (const p of mesh.parts) { gl.bindVertexArray(p.vao); gl.drawArrays(gl.TRIANGLES, 0, p.n); }
    const px = new Float32Array(n * n * 4);
    gl.readPixels(0, 0, n, n, gl.RGBA, gl.FLOAT, px);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null); gl.bindVertexArray(null);
    gl.deleteFramebuffer(fb); gl.deleteTexture(tx); gl.deleteRenderbuffer(dp);
    const z = new Float32Array(n * n);
    for (let i = 0; i < n * n; i++) z[i] = px[i * 4 + 3] < 0 ? NaN : px[i * 4 + 2];
    Object.assign(HM, { z: z, x0: cx - S2 / 2, y0: cy - S2 / 2, cell: cell, n: n });
    HM.ver++;
  }
  function hmMax(x, y, r) {                    // 세계 (x, y) 둘레 r m 안 모델 면 가장 높은 곳 (세계 z · 없으면 NaN)
    if (!HM.z) return NaN;
    const c = HM.cell, n = HM.n;
    const i0 = Math.max(0, Math.floor((x - r - HM.x0) / c)), i1 = Math.min(n - 1, Math.floor((x + r - HM.x0) / c));
    const j0 = Math.max(0, Math.floor((y - r - HM.y0) / c)), j1 = Math.min(n - 1, Math.floor((y + r - HM.y0) / c));
    let m = -Infinity;
    for (let j = j0; j <= j1; j++) for (let i = i0; i <= i1; i++) { const v = HM.z[j * n + i]; if (v > m) m = v; }
    return m > -Infinity ? m : NaN;
  }
  // 벽 꼭대기를 땅 g 위로 올릴 만큼 (관로 좌표 · 그 자리 모델 면 − g + 0.1 · 0 ~ 3 m · 높이 지도 없으면 WALL_UP)
  function wallUp(e, n, g) {
    if (DT.on) { const z = demP(e, n); return Number.isFinite(z) ? clamp(z - g + 0.1, 0, 3) : WALL_UP; }   // 맨땅 보기: 보이는 면 = 땅 그물
    const m = hmMax(e + TX, n + TY, 0.35);
    return Number.isFinite(m) ? clamp(m - TZ - g + 0.1, 0, 3) : WALL_UP;
  }
  const upAt = (T, j, w) => wallUp(T.E[j] + T.LX[j] * w, T.N[j] + T.LY[j] * w, T.G[j]);
  // 구덩이 그물 (360 pitMesh · 벽 · 끝벽은 그 자리 3D 모델 면까지 더 = 모델 면이 매끈한 땅보다 높은 곳에서 벽 윗부분이 비지 않게)
  function pitMesh(o) {
    const VB = [], OI = [], II = [], SI = [];
    const vtx = (p, nr, c, x) => { VB.push(p[0], p[1], p[2], nr[0], nr[1], nr[2], c[0], c[1], c[2], x[0], x[1], x[2], x[3]); };
    const pt = (T, j, w, z) => [T.E[j] + T.LX[j] * w - o[0], T.N[j] + T.LY[j] * w - o[1], z - o[2]];
    const dk = (T, j, z) => 1 - 0.35 * clamp((T.G[j] - z) / Math.max(T.G[j] - T.ZS[j], 1e-3), 0, 1);
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
      const zl = Math.abs(T.E[b] - T.E[a]) + Math.abs(T.N[b] - T.N[a]) < 1e-9;
      const sd = T.WV[a] > T.WV[b] ? -1 : 1, step = zl ? [sd * T.TX[b], sd * T.TY[b], 0] : null;
      if (!zl) {
        const ob = VB.length / VF;
        [pt(T, a, T.WV[a], T.G[a]), pt(T, b, T.WV[b], T.G[b]), pt(T, b, -T.WV[b], T.G[b]), pt(T, a, -T.WV[a], T.G[a])].forEach((p) => vtx(p, TR_UP, TR_UP, TR_AUX0));
        OI.push(ob, ob + 1, ob + 2, ob, ob + 2, ob + 3);
      }
      for (const sg of [1, -1]) {
        const hint = step || [-sg * T.LX[a], -sg * T.LY[a], 0];
        const ta = pt(T, a, sg * T.WV[a], T.G[a]), tb = pt(T, b, sg * T.WV[b], T.G[b]);
        const ua = pt(T, a, sg * T.WV[a], T.G[a] + upAt(T, a, sg * T.WV[a])), ub = pt(T, b, sg * T.WV[b], T.G[b] + upAt(T, b, sg * T.WV[b]));
        const ma = pt(T, a, sg * T.WV[a], T.ZM[a]), mb = pt(T, b, sg * T.WV[b], T.ZM[b]);
        const ba = pt(T, a, sg * T.WS[a], T.ZS[a]), bb = pt(T, b, sg * T.WS[b], T.ZS[b]);
        face([ua, ub, tb, ta], hint, TR_PAV, [1, 1, 1, 1]);
        face([ta, tb, mb, ma], hint, TR_PAV, [dk(T, a, T.G[a]), dk(T, b, T.G[b]), dk(T, b, T.ZM[b]), dk(T, a, T.ZM[a])]);
        face([ma, mb, bb, ba], hint, TR_SOIL, [dk(T, a, T.ZM[a]), dk(T, b, T.ZM[b]), dk(T, b, T.ZS[b]), dk(T, a, T.ZS[a])]);
      }
      if (zl) return;
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
      const Pw = (w, z) => pt(T, j, w, z);
      face([Pw(T.WV[j], T.G[j] + upAt(T, j, T.WV[j])), Pw(T.WV[j], T.G[j]), Pw(-T.WV[j], T.G[j]), Pw(-T.WV[j], T.G[j] + upAt(T, j, -T.WV[j]))], hint, TR_PAV, [1, 1, 1, 1]);
      face([Pw(T.WV[j], T.G[j]), Pw(T.WV[j], T.ZM[j]), Pw(-T.WV[j], T.ZM[j]), Pw(-T.WV[j], T.G[j])], hint, TR_PAV,
        [dk(T, j, T.G[j]), dk(T, j, T.ZM[j]), dk(T, j, T.ZM[j]), dk(T, j, T.G[j])]);
      face([Pw(T.WV[j], T.ZM[j]), Pw(T.WS[j], T.ZS[j]), Pw(-T.WS[j], T.ZS[j]), Pw(-T.WV[j], T.ZM[j])], hint, TR_SOIL,
        [dk(T, j, T.ZM[j]), dk(T, j, T.ZS[j]), dk(T, j, T.ZS[j]), dk(T, j, T.ZM[j])]);
    };
    const shore = (T, j, p) => {
      const ax = T.E[j], ay = T.N[j], dx = T.E[j + 1] - ax, dy = T.N[j + 1] - ay, len = Math.hypot(dx, dy);
      if (len < 1e-6 || Math.abs(p.ls1 - p.ls0) < 1e-6) return;
      const nx = -dy / len, ny = dx / len, lo = Math.min(p.ls0, p.ls1), hi = Math.max(p.ls0, p.ls1), D = T.D;
      const fOf = (s) => (s - p.ls0) / (p.ls1 - p.ls0), du = (m) => m / len;
      const lin = (A, f) => A[j] + (A[j + 1] - A[j]) * f, gf = (f) => lin(T.G, f), zsf = (f) => lin(T.ZS, f);
      const zv = (v, f) => (typeof v === 'function' ? v(f) : v);
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
      const wbox = (f0, f1, sg, d0, d1, z0, z1, col, mat) => box([
        [f0, sg, d0, zv(z0, f0)], [f1, sg, d0, zv(z0, f1)], [f1, sg, d1, zv(z0, f1)], [f0, sg, d1, zv(z0, f0)],
        [f0, sg, d0, zv(z1, f0)], [f1, sg, d0, zv(z1, f1)], [f1, sg, d1, zv(z1, f1)], [f0, sg, d1, zv(z1, f0)]], col, mat);
      const xbox = (f0, f1, d, z0, z1, col, mat) => box([
        [f0, 1, d, z0], [f1, 1, d, z0], [f1, -1, d, z0], [f0, -1, d, z0],
        [f0, 1, d, z1], [f1, 1, d, z1], [f1, -1, d, z1], [f0, -1, d, z1]], col, mat);
      const inS = (s, last) => s >= lo - 1e-6 && (s < hi - 1e-6 || (last && s <= hi + 1e-6));
      const rnd = (k) => { const x = Math.sin(k * 12.9898 + p.a * 78.233) * 43758.5453; return x - Math.floor(x); };
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
        const M = 2.5, TH = SHORE_IN.panel, top = (f) => gf(f) + 0.1;
        for (let k = Math.floor((lo - p.a) / M + 1e-9); p.a + k * M < hi - 1e-6; k++) {
          const m0 = p.a + k * M, m1 = Math.min(m0 + M, p.b), s0 = Math.max(m0, lo), s1 = Math.min(m1, hi);
          if (s1 - s0 < 1e-4) continue;
          const j0 = Math.abs(s0 - m0) < 1e-6, j1 = Math.abs(s1 - m1) < 1e-6;
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
  function buildTrench(use) {
    const o = MO.slice(), B = pitMesh(o);
    for (const T of trSecGet()) {
      if (!use(T.c)) continue;
      const m = T.m;
      for (let j = 0; j + 1 < m; j++) {
        B.seg(T, j, j + 1);
        if (j === 0 && T.c.wall[0]) B.endWall(T, 0, unit3([T.E[1] - T.E[0], T.N[1] - T.N[0], 0]));
        if (j === m - 2 && T.c.wall[1]) B.endWall(T, m - 1, unit3([T.E[m - 2] - T.E[m - 1], T.N[m - 2] - T.N[m - 1], 0]));
      }
      for (const p of T.pc) B.shore(T, p.va, p);
    }
    return B.done();
  }
  // 구덩이 기둥 (스텐실 z-fail): 마디마다 지표 띠 (±wv) 네모를 바닥 아래 VOL_DN ~ 땅 위 VOL_UP 로 세운 닫힌 기둥 (바깥 법선 · 반시계)
  function volMesh(use) {
    const o = MO.slice(), VB = [], IB = [];
    const vtx = (q, z) => { VB.push(q[0] - o[0], q[1] - o[1], z - o[2], 0, 0, 1, 0, 0, 0, 0, 0, 0, 0); return VB.length / VF - 1; };
    for (const T of trSecGet()) {
      if (!use(T.c)) continue;
      for (let j = 0; j + 1 < T.m; j++) {
        if (Math.abs(T.E[j + 1] - T.E[j]) + Math.abs(T.N[j + 1] - T.N[j]) < 1e-9) continue;
        const c = (k, w) => [T.E[k] + T.LX[k] * w, T.N[k] + T.LY[k] * w];
        const q = [c(j, T.WV[j]), c(j + 1, T.WV[j + 1]), c(j + 1, -T.WV[j + 1]), c(j, -T.WV[j])];
        let ar = 0;
        for (let i = 0; i < 4; i++) { const a = q[i], b = q[(i + 1) % 4]; ar += a[0] * b[1] - b[0] * a[1]; }
        if (Math.abs(ar) < 1e-6) continue;
        if (ar < 0) q.reverse();
        const cr = q.map((b, i) => { const a = q[(i + 3) % 4], d = q[(i + 1) % 4]; return (b[0] - a[0]) * (d[1] - b[1]) - (b[1] - a[1]) * (d[0] - b[0]); });
        const neg = cr.filter((v) => v < 0).length;
        if (neg > 1) continue;                                   // 꼬인 네모 (급한 굽이의 짧은 마디) 는 뺌
        const r = neg ? cr.findIndex((v) => v < 0) : 0;           // 오목하면 그 꼭짓점에서 나눔
        const zlo = Math.min(T.ZS[j], T.ZS[j + 1]) - TR.bed - VOL_DN, zhi = Math.max(T.G[j], T.G[j + 1]) + VOL_UP;
        const b = q.map((p) => vtx(p, zlo)), t = q.map((p) => vtx(p, zhi)), I = (i) => (r + i) % 4;
        IB.push(t[I(0)], t[I(1)], t[I(2)], t[I(0)], t[I(2)], t[I(3)]);
        IB.push(b[I(0)], b[I(2)], b[I(1)], b[I(0)], b[I(3)], b[I(2)]);
        for (let i = 0; i < 4; i++) { const i1 = (i + 1) % 4; IB.push(b[i], b[i1], t[i1], b[i], t[i1], t[i]); }
      }
    }
    return { o: o, v: Float32Array.from(VB), i: Uint32Array.from(IB) };
  }
  function pitPut(base, sel) {
    const tb = base + '|' + [TR.tp, TR.n, TR.margin, TR.bed, TR.prot].join(',') + '|' + pipeGen + '|' + sel;
    const k0 = tb + '|' + (sel >= 0 ? trStaticSig(sel) : ''), k1 = sel < 0 ? '' : tb + '|' + selGen;
    meshPut(pitM[0], k0, () => buildTrench((c) => c.ids.indexOf(sel) < 0));
    meshPut(pitM[1], k1, () => (sel < 0 ? NO_MESH : buildTrench((c) => c.ids.indexOf(sel) >= 0)));
    meshPut(volM[0], k0, () => volMesh((c) => c.ids.indexOf(sel) < 0));
    meshPut(volM[1], k1, () => (sel < 0 ? NO_MESH : volMesh((c) => c.ids.indexOf(sel) >= 0)));
    return pitM.some((m) => m.n > m.no) && volM.some((m) => m.n > 0);
  }

  // ---------------------------------------------------------------- 지장물 (data/obstacles.js · 고치지 않음 · 터파기 없음 · 360 과 같음)
  let obst = [], obGen = 0, obOn = lsGet('rv_ui:obst') !== false;
  const obShow = () => obOn && obst.length > 0;
  const obZ = (L, k) => L.pts[k * 3 + 2];
  function buildObst(O) {
    O = O || {};
    const lays = (Array.isArray(O.layers) ? O.layers : []).map((l) => ({ name: String((l && l.name) || ''), color: String((l && l.color) || '#9aa3b2') }));
    const dd = isNum(O.defaultDepth) && Number(O.defaultDepth) >= 0 ? Number(O.defaultDepth) : 1.0;
    const ox = O.origin && isNum(O.origin.E) && isNum(O.origin.N) ? Number(O.origin.E) - PO.E : 0, oy = O.origin && isNum(O.origin.E) && isNum(O.origin.N) ? Number(O.origin.N) - PO.N : 0;
    const out = [];
    (Array.isArray(O.lines) ? O.lines : []).forEach((l) => {
      if (!l || !Array.isArray(l.pts)) return;
      const lay = lays[num(l.layer, 0) | 0] || { name: '', color: '#9aa3b2' };
      const diaK = isNum(l.dia) && Number(l.dia) > 0, dia = diaK ? Number(l.dia) : OB_DIA;
      const dep = isNum(l.depth) && Number(l.depth) >= 0 ? Number(l.depth) : dd;
      const gIn = Array.isArray(l.g) && l.g.length === l.pts.length ? l.g : null;
      const p = [], g = [];
      l.pts.forEach((q, k) => {
        if (!ptOk(q)) return;
        const e = Number(q[0]) + ox, n = Number(q[1]) + oy, dz = demP(e, n), gk = Number.isFinite(dz) ? dz : gIn && isNum(gIn[k]) ? Number(gIn[k]) : groundAt(e, n);
        const gg = Number.isFinite(gk) ? gk : H0;
        p.push(e, n, gg - dep - dia / 2);
        g.push(gg);
      });
      if (p.length < 6) return;
      let e0 = Infinity, e1 = -Infinity, n0 = Infinity, n1 = -Infinity;
      for (let k = 0; k < p.length; k += 3) { e0 = Math.min(e0, p[k]); e1 = Math.max(e1, p[k]); n0 = Math.min(n0, p[k + 1]); n1 = Math.max(n1, p[k + 1]); }
      const bi = [0];
      for (let k = 3; k < p.length; k += 3) { const j = bi[bi.length - 1] * 3; if (Math.hypot(p[k] - p[j], p[k + 1] - p[j + 1]) > 1e-6) bi.push(k / 3); }
      const rgb = hexRgb(lay.color), kind = String(l.kind || l.label || lay.name || '지장물');
      out.push({
        color: lay.color, rgb: rgb, dark: 'rgb(' + rgb.map((v) => Math.round(v * 255 * 0.45)).join(',') + ')',
        kind: kind, text: diaK ? kind + ' · D' + Math.round(dia * 1000) : kind, dia: dia, hw: Math.max(dia, OB_DIA) / 2,
        pts: Float64Array.from(p), g: Float64Array.from(g), bi: bi, e0, e1, n0, n1,
      });
    });
    return out;
  }
  function obSet(O) {
    obst = buildObst(O);
    obGen++;
    $('tgObst').hidden = !obst.length;
    setOn($('tgObst'), obOn);
    redraw();
  }

  // ---------------------------------------------------------------- 땅 위 그림 (위에서 본 그림을 3D 모델에 덮음 · 가까이 = 화면 가운데 둘레 · 전체 = 관로 범위)
  const DC = { N: Math.min(2048, maxTex), g: null, l: null, gk: '', lk: '', paints: 0 };
  function dcMake() {
    const c = document.createElement('canvas'); c.width = c.height = DC.N;
    const t = gl.createTexture(); gl.bindTexture(gl.TEXTURE_2D, t);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array(4));
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    return { c, x: c.getContext('2d'), t, r: [0, 0, 1, 1], on: false };
  }
  function dcBox(all) {
    let e0 = Infinity, e1 = -Infinity, n0 = Infinity, n1 = -Infinity;
    for (const L of S.pipes || all ? lines : []) { e0 = Math.min(e0, L.e0); e1 = Math.max(e1, L.e1); n0 = Math.min(n0, L.n0); n1 = Math.max(n1, L.n1); }
    if (obShow() || all) for (const L of obst) { e0 = Math.min(e0, L.e0); e1 = Math.max(e1, L.e1); n0 = Math.min(n0, L.n0); n1 = Math.max(n1, L.n1); }
    if (!(e1 >= e0)) return null;
    const cx = (e0 + e1) / 2 + TX, cy = (n0 + n1) / 2 + TY, h = Math.max(e1 - e0, n1 - n0) / 2 + 20;
    return [cx - h, cy - h, cx + h, cy + h];
  }
  function dcKey() {
    return [S.mode, S.pipes, SHOW.tmp, SHOW.perm, TR.tp, TR.n, TR.margin, TR.bed, TR.prot, trOn(), S.cover, pipeGen, selGen, edSel(), obGen, obOn, lines.length, TX, TY].join('|');
  }
  function decalSync() {
    if (!DC.g) { DC.g = dcMake(); DC.l = dcMake(); }
    const bb = dcBox();
    if (!bb) { DC.g.on = DC.l.on = false; return; }
    const key = dcKey(), mpp = 2 * V.d * Math.tan(V.fov * D2R / 2) / Math.max(1, cv.clientHeight);
    const gq = Math.round(Math.log2(Math.max(mpp, 1e-3)) * 2), px = Math.pow(2, gq / 2);
    const gk = key + '|' + gq + '|' + bb.join(',');
    if (gk !== DC.gk) { DC.gk = gk; dcPaint(DC.g, bb, px); }
    const half = clamp(V.d * 1.1, 12, 400), hq = Math.pow(1.25, Math.round(Math.log(half) / Math.log(1.25))), step = hq * 0.25;
    const cx = Math.round(V.tgt[0] / step) * step, cy = Math.round(V.tgt[1] / step) * step;
    const lb = [cx - hq, cy - hq, cx + hq, cy + hq], lk = gk + '|' + hq.toFixed(3) + '|' + cx.toFixed(2) + ',' + cy.toFixed(2);
    if (lk !== DC.lk) {
      DC.lk = lk;
      if (lb[2] < bb[0] || lb[0] > bb[2] || lb[3] < bb[1] || lb[1] > bb[3]) DC.l.on = false; else dcPaint(DC.l, lb, px);
    }
  }
  function dcPaint(d, b, px) {
    const c = d.x, N = DC.N, W = b[2] - b[0], H = b[3] - b[1];
    c.setTransform(1, 0, 0, 1, 0, 0); c.clearRect(0, 0, N, N);
    c.setTransform(N / W, 0, 0, -N / H, -b[0] * N / W, b[3] * N / H);
    c.globalAlpha = 1; c.setLineDash([]);
    d.on = dcContent(c, Math.max(px, W / N * 0.8));
    d.r = [b[0], b[3], W, -H];
    DC.paints++;
    if (!d.on) return;
    gl.bindTexture(gl.TEXTURE_2D, d.t);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, true);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, d.c);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
    gl.generateMipmap(gl.TEXTURE_2D);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
  }
  // 띠 다각형 (왼쪽 앞으로 → 오른쪽 거꾸로 · 꼭짓점 이음 = 두 법선의 이등분 · 길이 1/cos · 360 띠와 같음)
  function bandPoly(Pp, bi, hw) {
    const m = bi.length - 1;
    if (m < 1) return null;
    const nx = new Float64Array(m), ny = new Float64Array(m), L = [], R = [];
    for (let j = 0; j < m; j++) {
      const dx = Pp[bi[j + 1] * 3] - Pp[bi[j] * 3], dy = Pp[bi[j + 1] * 3 + 1] - Pp[bi[j] * 3 + 1], l = Math.hypot(dx, dy) || 1;
      nx[j] = -dy / l; ny[j] = dx / l;
    }
    for (let j = 0; j <= m; j++) {
      const a = Math.max(0, j - 1), b = Math.min(m - 1, j);
      let qx = nx[a] + nx[b], qy = ny[a] + ny[b];
      const ql = Math.hypot(qx, qy);
      if (ql < 1e-6) { qx = nx[b]; qy = ny[b]; } else { qx /= ql; qy /= ql; const k = 1 / Math.max(qx * nx[b] + qy * ny[b], 0.3); qx *= k; qy *= k; }
      const e = Pp[bi[j] * 3] + TX, n = Pp[bi[j] * 3 + 1] + TY;
      L.push([e + qx * hw, n + qy * hw]); R.push([e - qx * hw, n - qy * hw]);
    }
    return L.concat(R.reverse());
  }
  // 그리기 (세계 좌표 · px = 화면 한 화소 m)
  function dcContent(c, px) {
    const X = (e) => e + TX, Y = (n) => n + TY;
    const ring = (q) => { c.moveTo(q[0][0], q[0][1]); for (let i = 1; i < q.length; i++) c.lineTo(q[i][0], q[i][1]); c.closePath(); };
    const area = (q) => { let a = 0; for (let i = 0; i < q.length; i++) { const p = q[i], r = q[(i + 1) % q.length]; a += p[0] * r[1] - r[0] * p[1]; } return a; };
    const linePath = (L, P2) => { c.moveTo(X(P2[L.bi[0] * 3]), Y(P2[L.bi[0] * 3 + 1])); for (let j = 1; j < L.bi.length; j++) c.lineTo(X(P2[L.bi[j] * 3]), Y(P2[L.bi[j] * 3 + 1])); };
    let any = false;
    const sel = edSel();
    if (S.pipes && lines.length) {
      const T3 = trSecGet(), top = (T, j, sg) => [X(T.E[j] + T.LX[j] * T.WV[j] * sg), Y(T.N[j] + T.LY[j] * T.WV[j] * sg)];
      const edges = [];
      for (const T of T3) {
        for (let j = 0; j + 1 < T.m; j++) {
          const L1 = top(T, j, 1), R1 = top(T, j, -1), L2 = top(T, j + 1, 1), R2 = top(T, j + 1, -1);
          edges.push([L1, L2], [R1, R2]);
          if (j === 0 && T.c.wall[0]) edges.push([L1, R1]);
          if (j === T.m - 2 && T.c.wall[1]) edges.push([L2, R2]);
        }
      }
      const edgePath = () => { c.beginPath(); for (const [a, b] of edges) { c.moveTo(a[0], a[1]); c.lineTo(b[0], b[1]); } };
      if (!S.depth) {
        // 영역: 임시점용 = 지표의 터파기 테두리 (옅은 주황 + 주황 점선) · 영구점용 = 관 바깥지름 띠 (관로 색)
        if (SHOW.tmp) {
          c.beginPath();
          for (const T of T3) {
            for (let j = 0; j + 1 < T.m; j++) {
              if (Math.abs(T.E[j + 1] - T.E[j]) + Math.abs(T.N[j + 1] - T.N[j]) < 1e-9) continue;
              const q = [top(T, j, 1), top(T, j + 1, 1), top(T, j + 1, -1), top(T, j, -1)];
              if (area(q) < 0) q.reverse();
              ring(q);
            }
          }
          c.globalAlpha = 0.16; c.fillStyle = AREA_TMP; c.fill('nonzero');
          edgePath();
          c.lineCap = 'butt'; c.setLineDash([7 * px, 5 * px]); c.globalAlpha = 0.95;
          c.strokeStyle = 'rgba(8, 10, 14, 0.55)'; c.lineWidth = 3.4 * px; c.stroke();
          c.strokeStyle = AREA_TMP; c.lineWidth = 1.8 * px; c.stroke();
          c.setLineDash([]); c.globalAlpha = 1;
          any = true;
        }
        if (SHOW.perm) {
          for (const L of lines.slice().sort((a, b) => (a.src === sel) - (b.src === sel))) {
            const pg = bandPoly(L.pts, L.bi, Math.max(L.dia / 2, px));
            if (!pg) continue;
            const hl = L.src === sel;
            c.beginPath(); ring(pg);
            c.globalAlpha = hl ? 0.95 : 0.85; c.fillStyle = hl ? L.colorHi : L.color; c.fill();
            c.save(); c.clip();
            c.globalAlpha = 1; c.strokeStyle = hl ? '#ffffff' : 'rgba(0, 0, 0, 0.45)'; c.lineWidth = 2 * px; c.lineJoin = 'miter'; c.stroke();
            c.restore();
            any = true;
          }
          c.globalAlpha = 1;
        }
      } else {
        // 터파기: 구덩이가 있으면 자른 선 (가는 어두운 선) · 없으면 땅 위 옅은 점선 · 고른 관 = 땅 위 흰 선
        if (trGL()) {
          edgePath();
          c.lineCap = 'round'; c.lineJoin = 'round'; c.strokeStyle = 'rgba(14, 11, 8, 0.6)'; c.lineWidth = 1.3 * px; c.stroke();
          any = true;
        } else {
          c.setLineDash([6 * px, 6 * px]); c.lineWidth = 1.5 * px; c.globalAlpha = 0.6;
          for (const L of lines) { if (L.src === sel) continue; c.beginPath(); linePath(L, L.pts); c.strokeStyle = L.color; c.stroke(); any = true; }
          c.setLineDash([]); c.globalAlpha = 1;
        }
        const SL = sel >= 0 ? lines.find((q) => q.src === sel) : null;
        if (SL) {
          c.beginPath(); linePath(SL, SL.pts);
          c.lineCap = 'round'; c.lineJoin = 'round';
          c.strokeStyle = 'rgba(8, 10, 14, 0.8)'; c.lineWidth = 4 * px; c.stroke();
          c.strokeStyle = '#ffffff'; c.lineWidth = 2 * px; c.stroke();
          any = true;
        }
      }
    }
    if (obShow()) {
      for (const L of obst) {
        if (!S.depth) {
          const pg = bandPoly(L.pts, L.bi, Math.max(L.hw, px));
          if (!pg) continue;
          c.beginPath(); ring(pg);
          c.globalAlpha = 0.55; c.fillStyle = L.color; c.fill();
          c.save(); c.clip(); c.globalAlpha = 1; c.setLineDash([5 * px, 4 * px]); c.strokeStyle = L.dark; c.lineWidth = 3 * px; c.stroke(); c.restore();
          c.setLineDash([]);
        } else {
          c.beginPath(); linePath(L, L.pts);
          c.setLineDash([3 * px, 5 * px]); c.lineWidth = 1.5 * px; c.globalAlpha = 0.5; c.strokeStyle = L.color; c.stroke(); c.setLineDash([]);
        }
        any = true;
      }
      c.globalAlpha = 1;
    }
    return any;
  }

  // ---------------------------------------------------------------- 그리기
  function tubeDraw(list, alpha, xr) {
    const ms = list.filter((m) => m.n);
    if (!ms.length || !TP) return;
    gl.useProgram(TP.p);
    gl.uniformMatrix4fv(TP.u.u_vp, false, VW.VPr);
    gl.uniform2f(TP.u.u_fade, 1e9, 2e9);
    gl.uniform1f(TP.u.u_alpha, alpha);
    gl.enable(gl.DEPTH_TEST);
    if (xr) {                                   // 땅 너머 (모델 뒤) 만 · 반투명 · 앞면만
      gl.depthFunc(gl.GREATER); gl.depthMask(false);
      gl.enable(gl.BLEND); gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ZERO, gl.ONE);
      gl.enable(gl.CULL_FACE); gl.cullFace(gl.BACK);
    } else { gl.depthFunc(gl.LESS); gl.depthMask(true); gl.disable(gl.BLEND); }
    for (const m of ms) {
      gl.bindVertexArray(m.vao);
      gl.uniform3f(TP.u.u_off, m.o[0] - VW.EP[0], m.o[1] - VW.EP[1], m.o[2] - VW.EP[2]);
      gl.drawElements(gl.TRIANGLES, m.n, gl.UNSIGNED_INT, 0);
    }
    gl.depthFunc(gl.LESS); gl.depthMask(true); gl.disable(gl.BLEND); gl.disable(gl.CULL_FACE);
  }
  function tUse(alpha) {
    gl.useProgram(TT.p);
    gl.uniformMatrix4fv(TT.u.u_vp, false, VW.VPr);
    gl.uniform2f(TT.u.u_fade, 1e9, 2e9);
    gl.uniform1f(TT.u.u_alpha, alpha);
    gl.uniform1f(TT.u.u_above, 0);
    gl.uniform3f(TT.u.u_cam, VW.EP[0], VW.EP[1], VW.EP[2]);           // 결 = 세계 좌표 (관로 좌표)
  }
  const tOff = (prg, m) => { gl.bindVertexArray(m.vao); gl.uniform3f(prg.u.u_off, m.o[0] - VW.EP[0], m.o[1] - VW.EP[1], m.o[2] - VW.EP[2]); };
  // 구덩이: 1) 기둥 → 스텐실 (모델 면이 기둥 안 = ≠ 0) 2) 그 자리 = 1 · 깊이 지움 3) 안쪽 면 · 가시설 (스텐실 1 · 깊이 씀)
  function pitDraw() {
    const vs = volM.filter((m) => m.n), ps = pitM.filter((m) => m.n > m.no);
    if (!vs.length || !ps.length) return false;
    gl.enable(gl.STENCIL_TEST); gl.stencilMask(0xff);
    gl.useProgram(TV.p); gl.uniformMatrix4fv(TV.u.u_vp, false, VW.VPr);
    gl.colorMask(false, false, false, false); gl.depthMask(false);
    gl.enable(gl.DEPTH_TEST); gl.depthFunc(gl.LESS); gl.disable(gl.CULL_FACE);
    gl.stencilFunc(gl.ALWAYS, 0, 0xff);
    gl.stencilOpSeparate(gl.BACK, gl.KEEP, gl.INCR_WRAP, gl.KEEP);
    gl.stencilOpSeparate(gl.FRONT, gl.KEEP, gl.DECR_WRAP, gl.KEEP);
    vs.forEach((m) => { tOff(TV, m); gl.drawElements(gl.TRIANGLES, m.n, gl.UNSIGNED_INT, 0); });
    gl.useProgram(FULL.p); gl.bindVertexArray(emptyVao);
    gl.depthMask(true); gl.depthFunc(gl.ALWAYS);
    gl.stencilFunc(gl.LEQUAL, 1, 0xff); gl.stencilOp(gl.KEEP, gl.KEEP, gl.REPLACE);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    gl.colorMask(true, true, true, true); gl.depthFunc(gl.LESS);
    gl.stencilFunc(gl.EQUAL, 1, 0xff); gl.stencilOp(gl.KEEP, gl.KEEP, gl.KEEP);
    tUse(1); gl.disable(gl.BLEND);
    ps.forEach((m) => { tOff(TT, m); gl.drawElements(gl.TRIANGLES, m.n - m.no, gl.UNSIGNED_INT, m.no * 4); });
    return true;
  }
  function shoreAbove() {                     // 가시설이 땅 위로 나온 곳 (구멍 밖 · 지표 아래는 버림 · 모델과 깊이 견줌)
    const sm = pitM.filter((m) => m.ns);
    if (!sm.length) return;
    tUse(1); gl.uniform1f(TT.u.u_above, 1);
    gl.stencilFunc(gl.NOTEQUAL, 1, 0xff);
    sm.forEach((m) => { tOff(TT, m); gl.drawElements(gl.TRIANGLES, m.ns, gl.UNSIGNED_INT, (m.n - m.ns) * 4); });
    gl.uniform1f(TT.u.u_above, 0);
  }
  function ug3d() {
    if (!TP) return;
    const sel = edSel(), base = S.cover + '|' + HM.ver + '|' + (DT.on ? 'd' : 'm');
    if (S.pipes) tubesPut(sel, base);
    if (obShow()) obPut();
    const tubes = S.pipes ? tubeM.filter((m) => m.n) : [], obs = obShow() && obM.n ? [obM] : [];
    const pit = trGL() && pitPut(base, sel) && pitDraw();
    if (pit) tubeDraw(obs.concat(tubes), 1, false);        // 구멍 안 = 불투명 (벽 · 바닥과 깊이 견줌)
    if (pit) gl.stencilFunc(gl.NOTEQUAL, 1, 0xff);         // 아래는 구멍 밖
    tubeDraw(obs.concat(tubes), 1, false);                 // 모델 위로 드러난 곳
    // 투시 (땅 너머 반투명): 구덩이가 있으면 관은 구멍 안만 (360 과 같음 · 구멍 가장자리 너머 관이 길 위에 놓인 듯 보이지 않게) · 지장물은 늘
    if (xray && !pit) tubeDraw(tubes, XR_ALPHA, true);
    if (xray || pit) tubeDraw(obs, OB_ALPHA, true);
    if (pit) { shoreAbove(); gl.disable(gl.STENCIL_TEST); }
    window.__pipes = { lines: lines.length, tris: (tubeM[0].n + tubeM[1].n) / 3, pit: !!pit };
  }
  function draw() {
    const dpr = Math.min(2, devicePixelRatio || 1), w = Math.max(1, cv.clientWidth), h = Math.max(1, cv.clientHeight), W = Math.round(w * dpr), H = Math.round(h * dpr);
    if (cv.width !== W || cv.height !== H) { cv.width = W; cv.height = H; }
    if (ovc.width !== W || ovc.height !== H) { ovc.width = W; ovc.height = H; }
    if (S.pipes && S.depth) hmSync();                     // 구덩이 벽 높이용 (처음 · 관로 범위가 바뀔 때만)
    gl.viewport(0, 0, W, H);
    gl.colorMask(true, true, true, true); gl.depthMask(true); gl.stencilMask(0xff);
    gl.clearColor(0.106, 0.122, 0.149, 1); gl.clearStencil(0); gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT | gl.STENCIL_BUFFER_BIT);
    ov.setTransform(1, 0, 0, 1, 0, 0); ov.clearRect(0, 0, W, H);
    if (!mesh) return;
    VW = view(W, H, w, h);
    const M = VW.M;
    decalSync();
    gl.enable(gl.DEPTH_TEST); gl.depthFunc(gl.LESS); gl.disable(gl.BLEND);
    const gsOn = GS.on && GS.ready && !!GS.prog;                       // 가우시안 보기: 그물은 깊이만 → 가우시안 → 땅 위 그림 → 관 · 터파기
    const dtm = (DT.on || (gsOn && gsSurfDT())) && !!DT.parts, SP = dtm ? DTP : MESH;   // 맨땅 보기 = 질감 모델 대신 땅 그물 (땅 위 그림은 같이)
    gl.useProgram(SP.p); gl.uniformMatrix4fv(SP.u.M, false, M);
    if (!dtm) gl.uniform1i(MESH.u.tex, 0);
    gl.uniform1i(SP.u.dg, 1); gl.uniform1i(SP.u.dl, 2);
    const dOn = DC.g && (DC.g.on || DC.l.on);
    gl.uniform1f(SP.u.dOn, dOn ? 1 : 0);
    if (DC.g) {
      const OFF = [1e9, 1e9, 1, 1];                       // 그림 없음 = 늘 범위 밖
      gl.activeTexture(gl.TEXTURE1); gl.bindTexture(gl.TEXTURE_2D, DC.g.t); gl.uniform4fv(SP.u.rg, DC.g.on ? DC.g.r : OFF);
      gl.activeTexture(gl.TEXTURE2); gl.bindTexture(gl.TEXTURE_2D, DC.l.t); gl.uniform4fv(SP.u.rl, DC.l.on ? DC.l.r : OFF);
    }
    gl.activeTexture(gl.TEXTURE0);
    if (gsOn) gl.colorMask(false, false, false, false);
    if (dtm) dtDraw(M);
    else for (const p of mesh.parts) { gl.bindTexture(gl.TEXTURE_2D, p.tex); gl.bindVertexArray(p.vao); gl.drawArrays(gl.TRIANGLES, 0, p.n); }
    if (gsOn) {
      gl.colorMask(true, true, true, true);
      gsDraw(VW);
      if (dOn && DCO && DC.g) {                                       // 땅 위 그림 (터파기 영역 · 관 띠) = 같은 그물 깊이에 덧칠
        gl.useProgram(DCO.p); gl.uniformMatrix4fv(DCO.u.M, false, M); gl.uniform1i(DCO.u.dg, 1); gl.uniform1i(DCO.u.dl, 2);
        gl.uniform4fv(DCO.u.rg, DC.g.on ? DC.g.r : [1e9, 1e9, 1, 1]); gl.uniform4fv(DCO.u.rl, DC.l.on ? DC.l.r : [1e9, 1e9, 1, 1]);
        gl.depthFunc(gl.LEQUAL); gl.depthMask(false); gl.enable(gl.BLEND); gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
        if (dtm) for (const p of DT.parts) { gl.bindVertexArray(p.vao); gl.drawElements(gl.TRIANGLES, p.n, p.it, 0); }
        else for (const p of mesh.parts) { gl.bindVertexArray(p.vao); gl.drawArrays(gl.TRIANGLES, 0, p.n); }
        gl.disable(gl.BLEND); gl.depthMask(true); gl.depthFunc(gl.LESS);
      }
    }
    ug3d();
    gl.bindVertexArray(null);
    drawChecks(M);
    overlay();
    lodSoon();
    window.__drawn = (window.__drawn || 0) + 1;
  }
  function loop() {
    if (ED.want) edApply();
    if (dirty) { dirty = false; draw(); }
    cfCheck();
    if (QT.on) qtyUI();
    shUI();
    if (ED.on) $('edChip').dataset.sel = String(ED.sel);
    requestAnimationFrame(loop);
  }

  // ---------------------------------------------------------------- 화면 ↔ 관로 좌표 (360 의 camc · scr 와 같은 약속: x 오른쪽 · y 위 · z 앞)
  function camc(e, n, z) {
    const rel = [e + TX - VW.E[0], n + TY - VW.E[1], z + TZ - VW.E[2]];
    return [dot(rel, VW.r), dot(rel, VW.u), dot(rel, VW.f)];
  }
  function scr(q) { return [VW.w / 2 + q[0] / q[2] * (VW.w / 2) / VW.tanH, VW.h / 2 - q[1] / q[2] * (VW.h / 2) / VW.tanV]; }
  const toScr = (e, n, z) => { const q = camc(e, n, z); return q[2] > NEAR ? scr(q) : null; };
  function segPick(a, b, x, y) {
    if (a[2] < NEAR && b[2] < NEAR) return null;
    let ta = 0, tb = 1;
    if (a[2] < NEAR) ta = (NEAR - a[2]) / (b[2] - a[2]); else if (b[2] < NEAR) tb = (NEAR - a[2]) / (b[2] - a[2]);
    const at = (t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
    const qa = at(ta), qb = at(tb), sa = scr(qa), sb = scr(qb);
    const dx = sb[0] - sa[0], dy = sb[1] - sa[1], l2 = dx * dx + dy * dy;
    const s = l2 > 0 ? clamp(((x - sa[0]) * dx + (y - sa[1]) * dy) / l2, 0, 1) : 0;
    const u = s / qb[2] / ((1 - s) / qa[2] + s / qb[2]);
    return { d: Math.hypot(x - sa[0] - s * dx, y - sa[1] - s * dy), t: ta + (tb - ta) * u, q: [sa[0] + s * dx, sa[1] + s * dy] };
  }
  // 관 L 에서 화면 (x, y) 에 가장 가까운 곳 { k, t, d (px), de (관 가장자리에서 px) } · 땅 위 선 + (터파기 · 투시) 관 중심
  function viewPick(L, x, y) {
    const Pp = L.pts, cnt = Pp.length / 3, kx = (VW.w / 2) / VW.tanH;
    const zf = S.depth || xray ? [gAt, zcAt] : [gAt];
    let best = null;
    for (let k = 0; k + 1 < cnt; k++) {
      for (const f of zf) {
        const a = camc(Pp[k * 3], Pp[k * 3 + 1], f(L, k)), b = camc(Pp[k * 3 + 3], Pp[k * 3 + 4], f(L, k + 1));
        const r = segPick(a, b, x, y);
        if (!r) continue;
        const z = a[2] + (b[2] - a[2]) * r.t, de = r.d - (z > NEAR ? L.dia / 2 * kx / z : 0);
        if (!best || de < best.de) best = { k: k, t: r.t, d: r.d, de: de };
      }
    }
    return best;
  }
  const PICK_DE = 6;
  const pickOf = (view, L, x, y) => viewPick(L, x, y);
  function lineAt(view, x, y) {
    let best = -1, bd = PICK_DE;
    for (const L of lines) { const r = viewPick(L, x, y); if (r && r.de <= bd) { bd = r.de; best = L.src; } }
    return best;
  }
  function inPoly(p, x, y) {
    let inside = false;
    for (let a = 0, b = p.length - 2; a < p.length; b = a, a += 2) {
      if ((p[a + 1] > y) !== (p[b + 1] > y) && x < (p[b] - p[a]) * (y - p[a + 1]) / (p[b + 1] - p[a + 1]) + p[a]) inside = !inside;
    }
    return inside;
  }
  function handleAt(view, x, y) {
    const L = edLine();
    if (!L) return -1;
    let best = -1, bd = Infinity;
    for (const hd of S.hnd) { const d = Math.hypot(x - hd.sx, y - hd.sy); if ((d < 10 || inPoly(hd.poly, x, y)) && d < bd) { bd = d; best = hd.i; } }
    return best;
  }
  function vtxAt(view, x, y) {
    if (!ED.on) return null;
    let best = null, bd = Infinity;
    for (const hd of S.vtx) { const d = Math.hypot(x - hd.sx, y - hd.sy); if ((d < 9 || inPoly(hd.poly, x, y)) && d < bd) { bd = d; best = { src: hd.src, i: hd.i }; } }
    return best;
  }
  function pickPoint(L, pk) {
    const Pp = L.pts, k = pk.k, t = pk.t, za = Pp[k * 3 + 2], zb = Pp[k * 3 + 5];
    return [Pp[k * 3] + (Pp[k * 3 + 3] - Pp[k * 3]) * t, Pp[k * 3 + 1] + (Pp[k * 3 + 4] - Pp[k * 3 + 1]) * t,
      L.hasZ && Number.isFinite(za) && Number.isFinite(zb) ? za + (zb - za) * t : null, gAt(L, k) + (gAt(L, k + 1) - gAt(L, k)) * t];
  }
  // 고르기 그물 (3D 모델만 · 세계 위치 · 시점이 같으면 다시 안 그림 → 끄는 동안 1 화소만 읽음)
  const PB = { fb: null, tex: null, dep: null, W: 0, H: 0, key: '', renders: 0 };
  function pickAt(x, y) {
    if (!mesh || !fext) return null;
    const dpr = Math.min(2, devicePixelRatio || 1), W = cv.width, H = cv.height;
    if (!PB.fb || PB.W !== W || PB.H !== H) {
      if (PB.fb) { gl.deleteFramebuffer(PB.fb); gl.deleteTexture(PB.tex); gl.deleteRenderbuffer(PB.dep); }
      PB.fb = gl.createFramebuffer(); PB.tex = gl.createTexture(); PB.dep = gl.createRenderbuffer(); PB.W = W; PB.H = H; PB.key = '';
      gl.bindTexture(gl.TEXTURE_2D, PB.tex); gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA32F, W, H, 0, gl.RGBA, gl.FLOAT, null);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
      gl.bindRenderbuffer(gl.RENDERBUFFER, PB.dep); gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH_COMPONENT24, W, H);
      gl.bindFramebuffer(gl.FRAMEBUFFER, PB.fb); gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, PB.tex, 0);
      gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, PB.dep);
    }
    gl.bindFramebuffer(gl.FRAMEBUFFER, PB.fb);
    const dtm = (DT.on || gsSurfDT()) && !!DT.parts, M = view(W, H).M, key = M.join(',') + '|' + mesh.parts.length + '|' + (dtm ? 'd' : 'm');   // 가우시안만 = 맨땅 그물에서
    if (key !== PB.key) {
      PB.key = key; PB.renders++;
      gl.viewport(0, 0, W, H);
      gl.colorMask(true, true, true, true); gl.depthMask(true);
      gl.clearBufferfv(gl.COLOR, 0, [0, 0, 0, -1]); gl.clear(gl.DEPTH_BUFFER_BIT); gl.enable(gl.DEPTH_TEST); gl.depthFunc(gl.LESS); gl.disable(gl.BLEND); gl.disable(gl.STENCIL_TEST);
      gl.useProgram(PICK.p); gl.uniformMatrix4fv(PICK.u.M, false, M); gl.uniform1f(PICK.u.id, 0);
      if (dtm) for (const p of DT.parts) { gl.bindVertexArray(p.vao); gl.drawElements(gl.TRIANGLES, p.n, p.it, 0); }    // 맨땅 보기 = 땅 그물에서 고름
      else for (const p of mesh.parts) { gl.bindVertexArray(p.vao); gl.drawArrays(gl.TRIANGLES, 0, p.n); }
    }
    const px = new Float32Array(4);
    gl.readPixels(clamp(Math.round(x * dpr), 0, W - 1), clamp(Math.round(H - y * dpr), 0, H - 1), 1, 1, gl.RGBA, gl.FLOAT, px);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null); gl.bindVertexArray(null);
    return px[3] < 0 ? null : { p: [px[0], px[1], px[2]] };
  }
  // 화면 점 → 땅 [e, n, z] (관로 좌표): 3D 모델 면 · 하늘이면 z0 높이 면과 만남
  // 화면 한 점 → 땅 위 자리 (관로 좌표). plane = 3D 모델 면을 건너뛰고 «매끈한 땅 높이 수평면» 과 만나는 점을 땅 높이로 두세 번 다시 맞춤
  // (꼭짓점 끌기 · 통째로 옮기기: 모델 면(차 · 나무 · 연석)을 집으면 손잡이가 커서에서 튐 · 360 과 같은 수평면 방식 · 1126 검토)
  function groundHit(x, y, z0, plane) {
    if (!plane) {
      const h = pickAt(x, y);
      if (h) return [h.p[0] - TX, h.p[1] - TY, h.p[2] - TZ];
    }
    const vw = vwNow(), nx = (x - vw.w / 2) / (vw.w / 2), ny = (vw.h / 2 - y) / (vw.h / 2);
    const d = unit3([0, 1, 2].map((i) => vw.f[i] + nx * vw.tanH * vw.r[i] + ny * vw.tanV * vw.u[i]));
    const E = vw.EP;
    if (Math.abs(d[2]) < 1e-6) return null;
    let z = Number.isFinite(z0) ? z0 : H0;
    for (let it = 0; it < 3; it++) {
      const t = (z - E[2]) / d[2];
      if (!(t > 0)) return null;
      const p = [E[0] + t * d[0], E[1] + t * d[1], z];
      if (!plane || it === 2) return p;
      const g = groundAt(p[0], p[1]);
      if (!Number.isFinite(g) || Math.abs(g - z) < 0.01) return p;
      z = g;
    }
    return null;
  }
  function goTo(e, n, g) {
    V.tgt = [e + TX, n + TY, (Number.isFinite(g) ? g : H0) + TZ];
    V.d = Math.min(V.d, 30);
    redraw();
  }

  // ---------------------------------------------------------------- 겹쳐 그리기 (화면 위: 이름표 · 손잡이 · 가시설 시작점 · 간섭 고리)
  const PIN = '#facc15', PIN2 = '#22d3ee', PIN_DARK = 'rgba(8, 10, 14, 0.9)';
  function roundRect(c, x, y, w, h, r) {
    c.beginPath();
    if (c.roundRect) { c.roundRect(x, y, w, h, r); return; }
    c.moveTo(x + r, y); c.arcTo(x + w, y, x + w, y + h, r); c.arcTo(x + w, y + h, x, y + h, r); c.arcTo(x, y + h, x, y, r); c.arcTo(x, y, x + w, y, r); c.closePath();
  }
  const circ = (sx, sy, r) => { const p = []; for (let q = 0; q < 16; q++) { const t = q / 16 * 2 * Math.PI; p.push(sx + r * Math.cos(t), sy + r * Math.sin(t)); } return p; };
  function overlay() {
    const dpr = VW.W / VW.w, w = VW.w, h = VW.h, cx = w / 2, cy = h / 2;
    ov.setTransform(dpr, 0, 0, dpr, 0, 0);
    S.hnd = []; S.vtx = [];
    // ---- 이름표 (관로 · 지장물): 화면 가운데에서 가장 가까운 관 위 점 · «관경 · 토피»
    const labels = [];
    const near = (L, zf, cand) => {
      const Pp = L.pts, cnt = Pp.length / 3;
      let best = Infinity, out = null;
      for (let k = 0; k + 1 < cnt; k++) {
        const a = camc(Pp[k * 3], Pp[k * 3 + 1], zf(L, k)), b = camc(Pp[k * 3 + 3], Pp[k * 3 + 4], zf(L, k + 1));
        const r = segPick(a, b, cx, cy);
        if (!r) continue;
        const qx = r.q[0], qy = r.q[1], qd = Math.hypot(qx - cx, qy - cy);
        if (qd < best && qx > 10 && qx < w - 10 && qy > 10 && qy < h - 10) { best = qd; out = { x: qx, y: qy, k: k, t: r.t }; }
      }
      return out && cand(out);
    };
    if (SHOW.lbl && S.pipes) {
      for (const L of lines) {
        if (!L.label) continue;
        const r = near(L, S.depth || xray ? zcAt : gAt, (o) => {
          const g = gAt(L, o.k) + (gAt(L, o.k + 1) - gAt(L, o.k)) * o.t, z = zcAt(L, o.k) + (zcAt(L, o.k + 1) - zcAt(L, o.k)) * o.t;
          return { x: o.x, y: o.y, text: L.label + ' · 토피 ' + (g - z - L.dia / 2).toFixed(1) + ' m', color: L.color };
        });
        if (r) labels.push(r);
      }
      if (obShow()) for (const L of obst) { const r = near(L, S.depth || xray ? obZ : (Lq, k) => Lq.g[k], (o) => ({ x: o.x, y: o.y, text: L.text, color: L.color, ob: true })); if (r) labels.push(r); }
    }
    if (labels.length) {
      ov.font = '600 12px Malgun Gothic, Segoe UI, sans-serif'; ov.textAlign = 'left'; ov.textBaseline = 'middle';
      for (const lb of labels) {
        const tw = ov.measureText(lb.text).width, pw = tw + 22, ph = 22;
        const bx = clamp(lb.x - pw / 2, 4, w - pw - 4), by = clamp(lb.ob ? lb.y + 12 : lb.y - ph - 12, 4, h - ph - 4);
        roundRect(ov, bx, by, pw, ph, 11); ov.fillStyle = 'rgba(12, 14, 20, 0.85)'; ov.fill();
        ov.beginPath();
        if (lb.ob) { ov.arc(bx + 11, by + ph / 2, 3.2, 0, 2 * Math.PI); ov.strokeStyle = lb.color; ov.lineWidth = 2; ov.stroke(); } else { ov.arc(bx + 11, by + ph / 2, 4, 0, 2 * Math.PI); ov.fillStyle = lb.color; ov.fill(); }
        ov.fillStyle = '#fff'; ov.fillText(lb.text, bx + 19, by + ph / 2 + 0.5);
      }
    }
    // ---- 지장물 간섭 자리: 빨간 고리
    if (S.pipes && obShow()) {
      for (const sp of CF.list) {
        const s = toScr(sp.e, sp.n, sp.g);
        if (!s) continue;
        ov.beginPath(); ov.arc(s[0], s[1], 9, 0, 2 * Math.PI);
        ov.strokeStyle = 'rgba(8, 10, 14, 0.85)'; ov.lineWidth = 5; ov.stroke();
        ov.strokeStyle = CF_RED; ov.lineWidth = 2.6; ov.stroke();
      }
    }
    // ---- 관로 고치기: 다른 관 원래 꼭짓점 (작은 청록 원 · 끌면 그 관을 고르고 옮김) · 고른 관 손잡이 (고른 점 노랑 · 마우스 올린 것 크게)
    if (ED.on && S.pipes) {
      for (const L of lines) {
        if (L.src === ED.sel) continue;
        L.ctrl.forEach((k, i) => {
          const s = toScr(L.pts[k * 3], L.pts[k * 3 + 1], gAt(L, k));
          if (!s || s[0] < -20 || s[1] < -20 || s[0] > w + 20 || s[1] > h + 20) return;
          S.vtx.push({ sx: s[0], sy: s[1], poly: circ(s[0], s[1], 4.5), src: L.src, i: i });
        });
      }
      ov.beginPath();
      for (const hd of S.vtx) { ov.moveTo(hd.sx + 4.5, hd.sy); ov.arc(hd.sx, hd.sy, 4.5, 0, 2 * Math.PI); }
      ov.fillStyle = PIN2; ov.fill(); ov.strokeStyle = PIN_DARK; ov.lineWidth = 1.5; ov.stroke();
      const EL = edLine();
      if (EL) {
        EL.ctrl.forEach((k, i) => {
          const s = toScr(EL.pts[k * 3], EL.pts[k * 3 + 1], gAt(EL, k));
          if (!s || s[0] < -20 || s[1] < -20 || s[0] > w + 20 || s[1] > h + 20) return;
          const r = i === ED.hov ? 8 : 6;
          S.hnd.push({ i: i, sx: s[0], sy: s[1], poly: circ(s[0], s[1], r), r: r });
        });
        for (const hd of S.hnd) {
          ov.beginPath(); ov.arc(hd.sx, hd.sy, hd.r, 0, 2 * Math.PI);
          ov.fillStyle = hd.i === ED.hi ? PIN : PIN2; ov.fill();
          ov.strokeStyle = PIN_DARK; ov.lineWidth = hd.i === ED.hov ? 2.5 : 1.5; ov.stroke();
        }
      }
    }
    // ---- 가시설 시작점 (찍는 중): 노란 고리
    if (SH.a) {
      const s = toScr(SH.a.e, SH.a.n, SH.a.g);
      if (s) {
        ov.beginPath(); ov.arc(s[0], s[1], 6, 0, 2 * Math.PI);
        ov.lineWidth = 4; ov.strokeStyle = PIN_DARK; ov.stroke();
        ov.lineWidth = 2.2; ov.strokeStyle = PIN; ov.stroke();
      }
    }
    msOverlay();                                  // 재기 · 새 관 · 잘라내기 · 원본 사진 자리 (오른쪽 메뉴)
  }

  // ---------------------------------------------------------------- 지하 시설물 칸: 표현 (영역 · 터파기) · [단면] · 표시 항목 · 평균매설깊이
  function setOn(el, on) { el.classList.toggle('on', !!on); }
  function setPipes(on) {
    S.pipes = !!on;
    if (!S.pipes && ED.on) edMode(false);
    setOn($('tgPipes'), S.pipes);
    $('ugMode').classList.toggle('off', !S.pipes);
    if (!S.pipes) cardHide();
    redraw();
  }
  $('tgPipes').addEventListener('click', () => setPipes(!S.pipes));
  const MODE_BTN = { area: 'mSurf', trench: 'mDepth' };
  function setMode(m) {
    if (MODES.indexOf(m) < 0) m = 'area';
    S.mode = m;
    S.depth = m === 'trench';
    if (m === 'area' && !S.pipes) setPipes(true);
    cardHide();
    modeUI();
    redraw();
  }
  function modeUI() {
    Object.keys(MODE_BTN).forEach((k) => setOn($(MODE_BTN[k]), S.mode === k));
    document.body.dataset.mode = S.mode;
    $('ugShow').hidden = S.mode !== 'trench';
    $('ugLegend').hidden = S.mode !== 'area';
  }
  Object.keys(MODE_BTN).forEach((k) => $(MODE_BTN[k]).addEventListener('click', () => setMode(k)));
  function showSet(k, on) { SHOW[k] = !!on; lsSet(SHOW_KEY, SHOW); ugUI(); redraw(); }
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
  function setCover(v, from) {
    S.cover = clamp(v, 0, 20);
    if (from !== coverEl) coverEl.value = String(round(S.cover, 2));
    secUI();
    redraw();
  }
  $('tgSide').addEventListener('click', () => { const s = $('side'); s.hidden = !s.hidden; setOn($('tgSide'), !s.hidden); });
  // 오른쪽 칸 폭 끌어 바꾸기 (JACK 1126 «오른쪽 옵션 창을 좌우로 크기 조정») — 칸 왼쪽 가장자리 손잡이 · 이 PC 에 기억 · 두 번 누르면 처음 폭
  (function sideGripInit() {
    const side = $('side'), KEY = 'rv_map3d:sideW', MIN = 240, root = document.documentElement;
    if (!side || !window.ResizeObserver) return;
    const maxW = () => Math.max(MIN, Math.round(window.innerWidth * 0.6));
    const apply = (w) => root.style.setProperty('--sideW', Math.round(Math.max(MIN, Math.min(w, maxW()))) + 'px');
    const sv = Number(lsGet(KEY)); if (sv > 0) apply(sv);               // 저장소가 막힌 브라우저에서도 화면이 멈추지 않게 (1126 검토)
    const g = document.createElement('div'); g.id = 'sideGrip'; g.title = '끌어서 폭 바꾸기 · 두 번 누르면 처음 폭';
    document.body.appendChild(g);
    const place = () => {                                           // 손잡이 = 칸 왼쪽 가장자리 (칸이 숨거나 좁은 화면 배치면 숨김)
      const r = side.getBoundingClientRect();
      g.hidden = side.hidden || r.width < 1 || window.innerWidth <= 860;
      if (!g.hidden) { g.style.left = (r.left - 5) + 'px'; g.style.top = r.top + 'px'; g.style.height = r.height + 'px'; }
    };
    new ResizeObserver(place).observe(side);
    new MutationObserver(place).observe(side, { attributes: true, attributeFilter: ['hidden'] });
    window.addEventListener('resize', () => { const c = parseFloat(root.style.getPropertyValue('--sideW')); if (c > maxW()) apply(maxW()); place(); });
    let dn = null;
    g.addEventListener('pointerdown', (e) => { if (e.button !== 0) return; dn = { x: e.clientX, w: side.getBoundingClientRect().width }; g.setPointerCapture(e.pointerId); g.classList.add('on'); e.preventDefault(); });
    g.addEventListener('pointermove', (e) => { if (!dn) return; apply(dn.w + (dn.x - e.clientX)); place(); });
    const up = () => { if (!dn) return; dn = null; g.classList.remove('on'); lsSet(KEY, Math.round(side.getBoundingClientRect().width)); };
    g.addEventListener('pointerup', up); g.addEventListener('pointercancel', up);
    g.addEventListener('dblclick', () => { root.style.removeProperty('--sideW'); try { localStorage.removeItem(KEY); } catch (e) { /* 없어도 됨 */ } place(); });
    place();
  })();

  // ---------------------------------------------------------------- 터파기 설정 창 (360 과 같음)
  const secDlg = $('secDlg'), secDia = $('secDia'), secOn = $('secOn'), secSvg = $('secSvg');
  const SEC_UI = [['cover', '평균매설깊이', 'm', 0.1], ['tp', '포장 직각 두께', 'm', 0.05], ['n', '기울기 1:n', '', 0.1],
    ['margin', '관 양옆 여유폭', 'm', 0.05], ['bed', '모래 기초', 'm', 0.05], ['prot', '보호층 (관 위)', 'm', 0.05]];
  const secIn = {};
  const secVal = (k) => (k === 'cover' ? S.cover : TR[k]);
  function trSet(patch) {
    TR = trClean(Object.assign({}, TR, patch), TR);
    trOff = false;
    lsSet(TR_KEY, TR);
    hostCalSoon();
    secUI();
    ugUI();
    redraw();
  }
  function secSetVal(k, v, from) { if (k === 'cover') setCover(v, from); else trSet({ [k]: v }); }
  function secOptions() {
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
  let secCtx = null;
  function secShow(on, ctx) {
    secDlg.hidden = !on;
    setOn($('tgSec'), on);
    secCtx = on && ctx ? ctx : null;
    if (on) { secOptions(); if (secCtx) { const L = lines.find((q) => q.src === secCtx.src); if (L) secDia.value = String(Math.round(L.dia * 1000)); } secUI(); }
  }
  function secDraw() {
    const L = secCtx && lines.find((q) => q.src === secCtx.src), C = L && secCtx.k < L.pts.length / 3 - 1 ? secAt(L, secCtx.k, secCtx.t) : null;
    secDlg.dataset.at = C ? String(round(C.s, 2)) : '';
    if (C) { secPic(secSvg, { D: C.D, col: L.color, zc: C.zc - C.g, el: C.g, W: 380, shore: C.shore }); return; }
    const mm = Number(secDia.value) || 300, D = mm / 1000;
    const L0 = lines.find((q) => Math.round(q.dia * 1000) === mm);
    secPic(secSvg, { D: D, col: L0 ? L0.color : '#00e5ff', zc: -S.cover - D / 2, el: null, W: 380 });
  }
  function secPic(svg, o) {
    const D = o.D, mm = Math.round(D * 1000), t = TR, zc = o.zc, top = zc + D / 2, c = -top, W = o.W || 380, el = o.el, sh = o.shore || null;
    const x = sectAt(D, 0, zc, 0, sh ? 0 : undefined), pb = zc - D / 2, zb = x.zb, wb = x.wb, zt = x.zt, wv = x.wv, zm = Math.max(zt, zb);
    const X = Object.assign({}, x, { g: 0, zc: zc, D: D, zp: protTop(x, zc, D) });
    const wAt = (z) => (z >= zt ? wv : wb + x.n * Math.max(0, z - zb));
    const pl = 72, pr = el != null ? 104 : 80, ptop = 40, pbot = 38, half = wv + 0.45, zl = zb - 0.25;
    const sc = Math.min((W - pl - pr) / (2 * half), (300 - ptop - pbot) / -zl);
    const H = Math.max(150, Math.ceil(ptop + pbot - zl * sc));
    svg.setAttribute('viewBox', '0 0 ' + W + ' ' + H);
    const cx = pl + (W - pl - pr) / 2, Xs = (v) => cx + v * sc, Y = (z) => ptop - z * sc;
    const n1 = (v) => v.toFixed(1), f2b = (v) => v.toFixed(2);
    const Pt = (pts) => pts.map(([a, z]) => n1(Xs(a)) + ',' + n1(Y(z))).join(' ');
    const txt = (a, b, s, an) => '<text x="' + n1(a) + '" y="' + n1(b) + '" text-anchor="' + (an || 'middle') + '" fill="#e9e9e9" stroke="#141414" stroke-width="3" paint-order="stroke" font-size="11" font-weight="600">' + s + '</text>';
    const ln = (x1, y1, x2, y2, st, da) => '<line x1="' + n1(x1) + '" y1="' + n1(y1) + '" x2="' + n1(x2) + '" y2="' + n1(y2) + '" stroke="' + (st || '#8b8b8b') + '" stroke-width="1"' + (da ? ' stroke-dasharray="' + da + '"' : '') + '/>';
    const dimH = (x1, x2, y, s, dy) => ln(x1, y, x2, y) + ln(x1, y - 4, x1, y + 4) + ln(x2, y - 4, x2, y + 4) + txt((x1 + x2) / 2, y + dy, s);
    const dimV = (a, y1, y2, s, an, dx) => ln(a, y1, a, y2) + ln(a - 4, y1, a + 4, y1) + ln(a - 4, y2, a + 4, y2) + txt(a + dx, (y1 + y2) / 2 + 4, s, an);
    const side = (z) => (z > zm ? [[wb, zb], [wv, zm], [wv, z]] : [[wb, zb], [wAt(z), z]]);
    const mirror = (pts) => pts.slice().reverse().map(([a, z]) => [-a, z]);
    const pitR = side(0);
    let s = '';
    s += '<rect x="' + n1(Xs(-half)) + '" y="' + n1(Y(0)) + '" width="' + n1(2 * half * sc) + '" height="' + n1(-zl * sc) + '" fill="#5a4330" stroke="none"/>';
    if (t.tp > 0) s += '<rect x="' + n1(Xs(-half)) + '" y="' + n1(Y(0)) + '" width="' + n1(2 * half * sc) + '" height="' + n1(t.tp * sc) + '" fill="#5c6068" stroke="none"/>';
    for (const [k, pg] of layerPolys(X, zb)) s += '<polygon points="' + Pt(pg) + '" fill="' + LAY_CSS[k] + '" stroke="none"/>';
    s += '<circle cx="' + n1(Xs(0)) + '" cy="' + n1(Y(zc)) + '" r="' + n1(D / 2 * sc) + '" fill="' + o.col + '" fill-opacity="0.9" stroke="#ffffff" stroke-width="1.2"/>';
    s += '<path d="M' + n1(Xs(-half)) + ' ' + n1(Y(0)) + 'H' + n1(Xs(-wv)) + 'M' + n1(Xs(wv)) + ' ' + n1(Y(0)) + 'H' + n1(Xs(half)) + '" fill="none" stroke="#c9d0dc" stroke-width="2"/>';
    s += '<polyline points="' + Pt(mirror(pitR).concat(pitR)) + '" fill="none" stroke="#fff4dc" stroke-width="1.5"/>';
    if (sh) {
      const R = (a0, a1, z0, z1, f) => '<rect class="sh" x="' + n1(Xs(Math.min(a0, a1))) + '" y="' + n1(Y(Math.max(z0, z1))) + '" width="' + n1(Math.abs(a1 - a0) * sc) +
        '" height="' + n1(Math.abs(z1 - z0) * sc) + '" fill="' + f + '" stroke="#141414" stroke-width="0.6"/>';
      const din = SHORE_IN[sh], wf = waleFits(wb, D, sh), lv = strutZ(0, x.zs, D), th = sh === 'panel' ? 0.12 : 0.2, d = wf ? din + 0.2 : din;
      for (const sg of [1, -1]) {
        const at = (dd) => sg * (wb - dd);
        if (sh === 'panel') s += R(at(0), at(din), x.zs, 0.1, '#8f99a3');
        else if (sh === 'hpile') s += R(at(-0.27), at(0.03), zb - 0.2, 0.2, '#4b4f55') + R(at(-0.07), at(0.01), x.zs, 0, '#9a6b3f');
        else s += R(at(0.01), at(din), zb - 0.2, 0.3, '#3b3f45');
        if (wf) for (const z of lv) s += R(at(din), at(din + 0.2), z - 0.1, z + 0.1, '#6b7077');
      }
      for (const z of lv) s += R(-(wb - d), wb - d, z - th / 2, z + th / 2, sh === 'panel' ? '#e0a526' : '#6b7077');
    }
    s += dimH(Xs(-wv), Xs(wv), Y(0) - 16, '폭 ' + f2b(2 * wv), -6);
    s += dimV(14, Y(0), Y(zb), '깊이 ' + f2b(-zb), 'start', 6);
    if (t.tp > 0) s += dimV(Xs(-wv) - 10, Y(0), Y(-t.tp), '포장 ' + f2b(t.tp), 'end', -6);
    if (X.zp > top + 1e-6) s += dimV(Xs(-wAt(X.zp)) - 10, Y(X.zp), Y(top), '보호 ' + f2b(X.zp - top), 'end', -6);
    if (t.bed > 0) s += dimV(Xs(-wAt(pb)) - 10, Y(zb), Y(pb), '기초 ' + f2b(t.bed), 'end', -6);
    if (c > 0) s += dimV(Xs(wv) + (el != null ? 14 : 26), Y(0), Y(-c), '토피 ' + f2b(c), 'start', 6);
    s += ln(Xs(D / 2), Y(zc), Xs(D / 2), Y(zb) + 18, '#5d5d5d') + dimH(Xs(D / 2), Xs(wb), Y(zb) + 14, '여유 ' + f2b(t.margin), 14);
    if (zm > zb + 1e-6 && x.n > 0) s += txt(Xs(wAt((zm + zb) / 2)) - 6, Y((zm + zb) / 2) + 4, '1:' + round(x.n, 3), 'end');
    s += D / 2 * sc > 16 ? txt(Xs(0), Y(zc) + 4, 'D' + mm) : txt(Xs(D / 2) + 6, Y(zc) + 4, 'D' + mm, 'start');
    if (el != null) {
      const tags = [['지반', 0, wv], ['상단', top, 0], ['중심', zc, D / 2], ['하단', pb, 0], ['바닥', zb, wb]];
      let last = -Infinity;
      for (const [name, z, at] of tags) {
        const y0 = Y(z), y = Math.max(y0, last + 12);
        last = y;
        s += '<polyline points="' + n1(Xs(at)) + ',' + n1(y0) + ' ' + n1(W - 80) + ',' + n1(y0) + ' ' + n1(W - 74) + ',' + n1(y) + '" fill="none" stroke="#8b8b8b" stroke-width="1" stroke-dasharray="2 2"/>';
        s += '<text x="' + (W - 4) + '" y="' + n1(y + 4) + '" text-anchor="end" fill="#e9e9e9" font-size="10.5" font-weight="600"><tspan fill="#8b8b8b">' + name + ' </tspan>' + f2b(el + z) + '</text>';
      }
    }
    svg.innerHTML = s;
  }
  function holdRepeat(btn, fn) {
    let t1 = 0, t2 = 0;
    const stop = () => { clearTimeout(t1); clearInterval(t2); };
    btn.addEventListener('pointerdown', (e) => {
      if (e.pointerType === 'mouse' && e.button !== 0) return;
      e.preventDefault();
      fn(); stop();
      t1 = setTimeout(() => { t2 = setInterval(fn, 70); }, 420);
    });
    ['pointerup', 'pointerleave', 'pointercancel'].forEach((ev) => btn.addEventListener(ev, stop));
    btn.addEventListener('click', (e) => { if (e.detail === 0) fn(); });
  }
  (function buildSec() {
    const box = $('secRows');
    SEC_UI.forEach(([k, name, unit, step]) => {
      const lab = document.createElement('span'); lab.className = 'cl'; lab.textContent = name;
      const minus = document.createElement('button'); minus.className = 'cb'; minus.type = 'button'; minus.textContent = '−';
      const inp = document.createElement('input'); inp.type = 'number'; inp.step = String(step); inp.min = '0'; inp.inputMode = 'decimal'; inp.id = 'sec_' + k;
      const plus = document.createElement('button'); plus.className = 'cb'; plus.type = 'button'; plus.textContent = '+';
      const un = document.createElement('span'); un.className = 'cu'; un.textContent = unit;
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

  // ---------------------------------------------------------------- 관 누르기: 관 정보 · 그 자리 단면 (360 과 같은 글)
  function gQ(L, k) { return gAt(L, k); }
  function zcQ(L, k) { const z = L.pts[k * 3 + 2]; return L.hasZ && Number.isFinite(z) ? z : gQ(L, k) - S.cover - L.dia / 2; }
  function secAt(L, k, t) {
    const Pp = L.pts, ga = gQ(L, k), gb = gQ(L, k + 1), za = zcQ(L, k), zb = zcQ(L, k + 1);
    const g = ga + (gb - ga) * t, zc = za + (zb - za) * t, D = L.dia;
    let s = 0;
    for (let j = 0; j < k; j++) s += Math.hypot(Pp[j * 3 + 3] - Pp[j * 3], Pp[j * 3 + 4] - Pp[j * 3 + 1]);
    const dx = Pp[k * 3 + 3] - Pp[k * 3], dy = Pp[k * 3 + 4] - Pp[k * 3 + 1], dl = Math.hypot(dx, dy) || 1;
    s += t * dl;
    const sq = shoreAt(L, s), x = sectAt(D, g, zc, 0, sq ? 0 : undefined), zp = protTop(x, zc, D), top = zc + D / 2;
    return {
      L: L, k: k, t: t, s: s, e: Pp[k * 3] + dx * t, n: Pp[k * 3 + 1] + dy * t, nx: -dy / dl, ny: dx / dl, color: L.color,
      g: g, zc: zc, top: top, bot: zc - D / 2, cover: g - top, D: D, depth: g - x.zb, wtop: 2 * x.wv, wbot: 2 * x.wb,
      bed: TR.bed, prot: Math.max(0, zp - top), X: Object.assign({}, x, { g: g, zc: zc, D: D, zp: zp }), shore: sq ? sq.t : null,
    };
  }
  const f2 = (v) => v.toFixed(2);
  function kvRows(box, rows, pre) {
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
    const w = cv.clientWidth, h = cv.clientHeight;
    card.style.left = clamp(at[0] + 16, 8, Math.max(8, w - card.offsetWidth - 8)) + 'px';
    card.style.top = clamp(at[1] - card.offsetHeight / 2, 64, Math.max(64, h - card.offsetHeight - 8)) + 'px';
  }
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
  function pipeTap(view, x, y) {
    if (!S.pipes || !lines.length) return false;
    const src = lineAt(view, x, y);
    if (src < 0) return false;
    const L = lines.find((q) => q.src === src), pk = L && pickOf(view, L, x, y);
    if (!pk) return false;
    cardShow(L, pk, [x, y]);
    return true;
  }
  function ugEsc() { if (!$('pipeCard').hidden) { cardHide(); return true; } return false; }
  modeUI();
  ugUI();

  // ---------------------------------------------------------------- 창: [관로 넣기] · [지장물 넣기] (360 과 같음)
  const dlgBack = $('dlgBack');
  function dlgShow(id) { dlgBack.hidden = false; ['pipeDlg', 'obstDlg', 'npDlg'].forEach((k) => { $(k).hidden = k !== id; }); }
  function dlgClose() { dlgBack.hidden = true; if (MS.kind === 'pipe') msClear(); }   // 새 관 창을 넣지 않고 닫으면 그리던 것도 버림
  dlgBack.addEventListener('click', (e) => { if (e.target === dlgBack) dlgClose(); });
  dlgBack.querySelectorAll('.dlgX').forEach((b) => b.addEventListener('click', dlgClose));
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
  function pmShow(fresh) {
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
  $('bPipes').addEventListener('click', () => { pmMsg(''); $('pmDepth').value = String(round(S.cover, 2)); pmShow(); dlgShow('pipeDlg'); });
  $('pmPick').addEventListener('click', () => {
    if (PM.busy) return;
    pmMsg(''); pmBusy(true);
    hostCall('pick_pipes').then((r) => {
      pmBusy(false);
      if (!r || !r.path) return;
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
    const arg = { path: p.path, dia_field: shp ? $('pmDia').value || null : null, label_field: shp ? $('pmLabel').value || null : null,
      depth: depth, layers: shp ? null : $('pmLayers').value.trim() || null };
    pmMsg(''); pmBusy(true, '넣는 중');
    pipesFlush().then(() => hostCall('import_pipes', arg)).then((r) => {
      if (!r || !r.ok) { pmBusy(false); pmMsg((r && r.msg) || '못 넣음'); return null; }
      return reloadPipes().then((n) => {
        pmBusy(false); setCover(depth); setPipes(true); dlgClose();
        toast('관로 ' + (isNum(r.lines) ? Number(r.lines) : n) + '개');
      });
    }).catch((e) => { pmBusy(false); pmMsg(errText(e)); });
  });
  function loadScript(src, name) {
    const old = window[name];
    return new Promise((resolve, reject) => {
      window[name] = undefined;
      const sc = document.createElement('script');
      sc.onload = () => { sc.remove(); resolve(window[name]); };
      sc.onerror = () => { sc.remove(); window[name] = old; reject(new Error(src + ' 못 읽음')); };
      sc.src = src + '?v=' + Date.now();
      document.head.appendChild(sc);
    });
  }
  // data/pipes.js 다시 읽기 (프로그램이 [관로 넣기] 로 새로 쓴 뒤 · 땅 높이 dem.js 도 처음 만들어졌을 수 있음)
  function reloadPipes() {
    return (DEM ? Promise.resolve() : loadScript('data/dem.js', 'RV_DEM').then((d) => { DEM = demInit(d); const b = $('bDtm'); if (b) b.hidden = !DEM || !DTP; }, () => {}))
      .then(() => loadScript('data/pipes.js', 'RV_PIPES'))
      .then(() => {
        poSet(window.RV_PIPES);
        gSampBuild();
        edReset();
        lines = buildLines(window.RV_PIPES);
        pipeGen++;
        obSet(window.RV_OBST);                                         // 땅 높이(dem.js)가 새로 생겼으면 지장물 높이도 다시 (1126 검토)
        if (!secDlg.hidden) { secOptions(); secUI(); }
        cardHide();
        infoUpd();
        redraw();
        return lines.length;
      });
  }
  // 지장물 넣기 (프로그램만): pick_obstacles → 항목 → import_obstacles → data/obstacles.js 다시 읽기
  const OM = { pick: null, busy: false };
  function omMsg(t) { $('omMsg').textContent = t || ''; }
  function omBusy(on, text) { OM.busy = on; $('omPick').disabled = on; $('omGo').disabled = on || !OM.pick; $('omGo').textContent = on && text ? text : '넣기'; }
  function omShow(fresh) {
    const p = OM.pick;
    $('omFile').textContent = p ? String(p.name || p.path) + ' · SHP' : '';
    $('omShp').hidden = !p;
    if (p && fresh) {
      const fields = (Array.isArray(p.fields) ? p.fields : []).filter((f) => f && f.name != null).map((f) => String(f.name));
      pmFill($('omType'), fields, p.guess_type); pmFill($('omDia'), fields, p.guess_dia); pmFill($('omDep'), fields, p.guess_depth); pmFill($('omLabel'), fields, p.guess_label);
    }
    $('omNote').textContent = p && p.note ? String(p.note) : '';
    $('omGo').disabled = !p || OM.busy;
  }
  $('hObst').addEventListener('click', () => {
    omMsg('');
    const O = window.RV_OBST;
    $('omDepth').value = String(round(O && isNum(O.defaultDepth) ? Number(O.defaultDepth) : 1.0, 2));
    omShow(); dlgShow('obstDlg');
  });
  $('omPick').addEventListener('click', () => {
    if (OM.busy) return;
    omMsg(''); omBusy(true);
    hostCall('pick_obstacles').then((r) => {
      omBusy(false);
      if (!r || (!r.path && !r.error)) return;
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
    const arg = { path: p.path, type_field: $('omType').value || null, dia_field: $('omDia').value || null, depth_field: $('omDep').value || null,
      label_field: $('omLabel').value || null, default_depth: isNum(dv) ? clamp(Number(dv), 0, 20) : 1.0 };
    omMsg(''); omBusy(true, '넣는 중');
    hostCall('import_obstacles', arg).then((r) => {
      if (!r || !r.ok) { omBusy(false); omMsg((r && r.msg) || '못 넣음'); return null; }
      return loadScript('data/obstacles.js', 'RV_OBST').then(() => {
        obSet(window.RV_OBST);
        omBusy(false); obOn = true; lsSet('rv_ui:obst', true); setOn($('tgObst'), true); dlgClose();
        toast('지장물 ' + (isNum(r.lines) ? Number(r.lines) : obst.length) + '개');
      });
    }).catch((e) => { omBusy(false); omMsg(errText(e)); });
  });
  $('tgObst').addEventListener('click', () => { obOn = !obOn; lsSet('rv_ui:obst', obOn); setOn($('tgObst'), obOn); redraw(); });

  // ---------------------------------------------------------------- 관로 고치기 (360 과 같음 · 꼭짓점 손잡이를 3D 모델 위로 끌기)
  const ED_STEP = 5;
  const PIPE_KEY = 'rv_pipes:' + KEY0;
  function ctrlPts(l) { return ctrlOf(l).map((k) => { const q = l.pts[k]; return [Number(q[0]), Number(q[1]), isNum(q[2]) ? Number(q[2]) : null]; }); }
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
    l.g = pts.map((q) => { const g = groundAt(q[0], q[1]); return Number.isFinite(g) ? round(g, 3) : null; });   // 보기 화면이 이 값을 ±5 m 로 다시 매끈하게
    delete l.ll;
    if (Array.isArray(l.shore)) {
      let len = 0;
      for (let i = 1; i < pts.length; i++) len += Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]);
      l.shore = l.shore.filter((q) => q && isNum(q.a) && isNum(q.b)).map((q) => ({ a: round(clamp(Math.min(+q.a, +q.b), 0, len), 2), b: round(clamp(Math.max(+q.a, +q.b), 0, len), 2), t: q.t }))
        .filter((q) => q.b - q.a >= 0.01);
      if (!l.shore.length) delete l.shore;
    }
  }
  function edLine() {
    if (!ED.on || ED.sel < 0) return null;
    if (ED.Lof !== lines || ED.Lsel !== ED.sel) { ED.Lof = lines; ED.Lsel = ED.sel; ED.L = lines.find((L) => L.src === ED.sel) || null; }
    return ED.L;
  }
  const edRaw = () => window.RV_PIPES.lines[ED.sel];
  function edPut(src) {
    const L = buildLine(window.RV_PIPES.lines[src], src);
    const nl = lines.slice(), i = nl.findIndex((q) => q.src === src);
    if (i >= 0) { if (L) nl[i] = L; else nl.splice(i, 1); } else if (L) {
      let j = 0;
      while (j < nl.length && nl[j].src < src) j++;
      nl.splice(j, 0, L);
    }
    lines = nl;
    if (src === edSel()) selGen++; else pipeGen++;
    if (secCtx && !secDlg.hidden) secDraw();
    redraw();
  }
  function flushView() { if (dirty) { dirty = false; draw(); } }
  function edStart(view, x, y, e) {
    ED.down = null;
    if (!ED.on) return;
    flushView();
    let hi = handleAt(view, x, y);
    if (hi < 0) {
      const v = vtxAt(view, x, y);
      if (v) { ED.sel = v.src; ED.hov = -1; ED.hi = hi = v.i; redraw(); }
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
    redraw();
  }
  function edBegin(d) {
    const L = edLine(), raw = edRaw();
    if (!L || !raw) return false;
    d.before = JSON.stringify(raw);
    d.cp0 = ctrlPts(raw);
    d.sh0 = Array.isArray(raw.shore) ? JSON.stringify(raw.shore) : null;
    if (d.cp0.length !== L.ctrl.length) return false;
    vwNow();
    if (d.kind === 'pt') {
      const q = L.ctrl[d.i], c = camc(L.pts[q * 3], L.pts[q * 3 + 1], gAt(L, q)), hs = c[2] > NEAR ? scr(c) : null;
      d.off = hs ? [hs[0] - d.x0, hs[1] - d.y0] : [0, 0];
      ED.hi = d.i;
    } else {
      const z0 = d.i >= 0 ? gAt(L, L.ctrl[d.i]) : pickPoint(L, d.pk)[3];
      d.ref = groundHit(d.x0, d.y0, z0, true);
      if (!d.ref) return false;
    }
    d.started = true;
    return true;
  }
  function edApply() {
    const d = ED.down, at = ED.want;
    ED.want = null;
    if (!d || !d.started || !at) return;
    const L = edLine(), raw = edRaw();
    if (!L || !raw) return;
    const cp = d.cp0.map((q) => q.slice());
    if (d.kind === 'pt') {
      const en = groundHit(at[0] + d.off[0], at[1] + d.off[1], gAt(L, L.ctrl[d.i]), true);
      if (!en) return;
      cp[d.i][0] = en[0]; cp[d.i][1] = en[1];
    } else {
      const en = groundHit(at[0], at[1], d.ref[2], true);
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
  function edCommit(src, before, after) {
    if (!ED.orig.has(src)) ED.orig.set(src, JSON.parse(before));
    ED.undo.push({ src: src, before: before, after: after });
    if (ED.undo.length > 300) ED.undo.shift();
    ED.redo = [];
    ED.dirty.add(src);
    pipesSaveSoon();
  }
  function edTap(view, x, y) {
    const hi = handleAt(view, x, y);
    if (hi >= 0) { ED.hi = hi; redraw(); return; }
    const v = vtxAt(view, x, y);
    if (v) { ED.sel = v.src; ED.hi = v.i; ED.hov = -1; redraw(); return; }
    const src = lineAt(view, x, y);
    if (src >= 0) {
      if (src !== ED.sel) { ED.sel = src; ED.hi = -1; ED.hov = -1; }
    } else if (ED.hi >= 0) ED.hi = -1; else ED.sel = -1;
    redraw();
  }
  function edHover(view, x, y) {
    const hi = handleAt(view, x, y), v = hi < 0 && vtxAt(view, x, y);
    cv.classList.toggle('move', hi >= 0 || !!v);
    cv.classList.toggle('point', hi < 0 && !v && lineAt(view, x, y) >= 0);
    if (hi !== ED.hov) { ED.hov = hi; redraw(); }
  }
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
    ED.hi = -1; ED.hov = -1;
    edCommit(ED.sel, before, JSON.stringify(raw));
  }
  function edUndo(redo) {
    const o = (redo ? ED.redo : ED.undo).pop();
    if (!o) { toast(redo ? '다시 할 것 없음' : '되돌릴 것 없음'); return; }
    (redo ? ED.undo : ED.redo).push(o);
    window.RV_PIPES.lines[o.src] = JSON.parse(redo ? o.after : o.before);
    ED.sel = o.src; ED.hi = -1; ED.hov = -1;
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
    document.body.classList.toggle('edOn', ED.on);
    $('edShp').hidden = !HOST();
    $('edDrop').hidden = !!HOST() || !ED.dirty.size;
  }
  function edMode(on) {
    on = !!on;
    if (on && !lines.length) { toast('관로 없음'); return; }
    if (on) shCancel();
    if (ED.down) edEnd(true);
    ED.on = on;
    ED.sel = -1; ED.hi = -1; ED.hov = -1; ED.down = null; ED.want = null;
    if (on && !S.pipes) setPipes(true);
    cv.classList.remove('point', 'move');
    edUI();
    redraw();
  }
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
  let pipeSaveT = 0, pipeSaving = Promise.resolve(null);
  const pipesText = () => 'window.RV_PIPES = ' + JSON.stringify(window.RV_PIPES) + ';\n';
  function pipesSig(Pp) {
    let h = 2166136261;
    const add = (s) => { for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; } };
    const ls = Pp && Array.isArray(Pp.lines) ? Pp.lines : [];
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
      try { localStorage.setItem(PIPE_KEY, JSON.stringify({ sig: ED.origSig, lines: out, layers: window.RV_PIPES.layers })); } catch (e) { toast('이 브라우저에 저장 못 함'); }
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
  function edDiscard() {
    const Pp = window.RV_PIPES;
    if (Pp && Array.isArray(Pp.lines)) {
      ED.orig.forEach((l, i) => { Pp.lines[i] = l; });
      if (ED.origEdited) Pp.edited = true; else delete Pp.edited;
    }
    ED.orig.clear(); ED.dirty.clear(); ED.undo = []; ED.redo = [];
    try { localStorage.removeItem(PIPE_KEY); } catch (e) { /* 없어도 됨 */ }
    ED.sel = -1; ED.hi = -1; ED.hov = -1;
    lines = buildLines(Pp);
    pipeGen++;
    edUI();
    redraw();
    toast('고친 것 버림');
  }
  function edShp() {
    if (!HOST() || ED.shpBusy) return;
    const bs = [$('edShp'), $('pmShpOut')];
    const busy = (on) => { ED.shpBusy = on; bs.forEach((b) => { b.disabled = on; }); };
    busy(true);
    pipesFlush().then(() => hostCall('export_pipes_shp')).then((r) => {
      busy(false);
      if (!r) return;
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
  beforeHome = () => Promise.all([homeCal(), pipesFlush()]);
  window.addEventListener('pagehide', () => { if (pipeSaveT) pipesFlush(); });
  window.addEventListener('pywebviewready', edUI);
  ED.origSig = pipesSig(window.RV_PIPES);
  ED.origEdited = !!(window.RV_PIPES && window.RV_PIPES.edited);
  (function restore() {
    const st = lsGet(PIPE_KEY);
    if (!st || HOST() || st.sig !== ED.origSig || !st.lines || typeof st.lines !== 'object') return;
    if (!window.RV_PIPES && Object.keys(st.lines).length) { window.RV_PIPES = { layers: [], lines: [], origin: { E: ORG[0], N: ORG[1] } }; poSet(window.RV_PIPES); }  // 관로 없던 프로젝트에 그린 새 관
    const Pp = window.RV_PIPES;
    if (!Pp || !Array.isArray(Pp.lines)) return;
    let n = 0;
    Object.keys(st.lines).map(Number).sort((a, b) => a - b).forEach((i) => {
      const l = st.lines[i];
      if (i === Pp.lines.length && l && Array.isArray(l.pts)) Pp.lines.push({ pts: [] });   // 이 브라우저에서 그린 새 관 (오른쪽 메뉴) = 끝에 자리 만들고
      if (!Number.isInteger(i) || i < 0 || i >= Pp.lines.length || !l || !Array.isArray(l.pts)) return;
      ED.orig.set(i, Pp.lines[i]);
      Pp.lines[i] = l;
      ED.dirty.add(i);
      n++;
    });
    if (!n) return;
    if (Array.isArray(st.layers) && st.layers.length > (Array.isArray(Pp.layers) ? Pp.layers.length : 0)) Pp.layers = st.layers;   // 새 관 층
    Pp.edited = true;
    lines = buildLines(Pp);
    toast('고친 관로 불러옴', edDiscard, '버리기');
  })();
  edUI();

  // ---------------------------------------------------------------- 가시설 (흙막이 · 관 위 두 점 = 구간 · RV_PIPES.lines[i].shore · 360 과 같음)
  const shBox = $('shBox'), shChip = $('shChip');
  function shOpen(on) { shBox.hidden = !on; setOn($('tgShore'), on); if (!on) shCancel(); SH.sig = ''; shUI(); }
  function shArm(t) {
    if (SHORE_T.indexOf(t) < 0) return;
    if (!lines.length) { toast('관로 없음'); return; }
    if (ED.on) edMode(false);
    if (!S.pipes) setPipes(true);
    SH.arm = t; SH.a = null;
    cardHide(); shUI(); redraw();
  }
  function shCancel() {
    if (!SH.arm && !SH.a) return;
    SH.arm = null; SH.a = null;
    cv.classList.remove('point');
    shUI(); redraw();
  }
  function shTap(view, x, y) {
    const src = S.pipes ? lineAt(view, x, y) : -1, L = src >= 0 ? lines.find((q) => q.src === src) : null, pk = L && pickOf(view, L, x, y);
    if (!pk) { toast('관 위 누르기'); return; }
    const s = round(L.cum[pk.k] + (L.cum[pk.k + 1] - L.cum[pk.k]) * pk.t, 2), p = pickPoint(L, pk);
    if (!SH.a) { SH.a = { src: src, s: s, e: p[0], n: p[1], g: p[3] }; shUI(); redraw(); return; }
    if (src !== SH.a.src) { toast('같은 관 누르기'); return; }
    if (Math.abs(s - SH.a.s) < 0.1) { toast('너무 짧음'); return; }
    const a = Math.min(s, SH.a.s), b = Math.max(s, SH.a.s), t = SH.arm;
    SH.arm = null; SH.a = null;
    cv.classList.remove('point');
    shEdit(src, (list) => {
      const out = [];
      for (const q of list) {
        if (!(q.b > a && q.a < b)) { out.push(q); continue; }
        if (q.a < a) out.push({ a: q.a, b: a, t: q.t });
        if (q.b > b) out.push({ a: b, b: q.b, t: q.t });
      }
      return out.concat([{ a: a, b: b, t: t }]);
    });
    toast(SHORE_NAME[t] + ' · ' + (b - a).toFixed(2) + ' m');
  }
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
  function lineAtS(L, s) {
    const C = L.cum;
    let k = 0;
    while (k + 2 < C.length && C[k + 1] < s) k++;
    const l = C[k + 1] - C[k];
    return { k: k, t: l > 0 ? clamp((s - C[k]) / l, 0, 1) : 0 };
  }
  function shGo(L, q) { const p = pickPoint(L, lineAtS(L, (q.a + q.b) / 2)); goTo(p[0], p[1], p[3]); }
  function shUI() {
    const arm = SH.arm;
    shChip.classList.toggle('show', !!arm);
    document.body.classList.toggle('shOn', !!arm);
    if (arm) { $('shTip').textContent = SH.a ? '끝점' : '시작점'; $('shName').textContent = SHORE_NAME[arm]; }
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
      sel.title = '종류'; sel.setAttribute('aria-label', '가시설 종류');
      SHORE_T.forEach((t) => { const o = document.createElement('option'); o.value = t; o.textContent = SHORE_NAME[t]; sel.appendChild(o); });
      sel.value = q.t;
      sel.addEventListener('change', () => shEdit(L.src, (list) => list.map((r, j) => (j === q.i ? Object.assign(r, { t: sel.value }) : r))));
      const go = document.createElement('button');
      go.type = 'button'; go.className = 'shGo'; go.title = '그쪽 보기';
      go.textContent = (L.label || dTxt(L.dia)) + ' · ' + (q.b - q.a).toFixed(2) + ' m';
      go.addEventListener('click', () => shGo(L, q));
      const del = document.createElement('button');
      del.type = 'button'; del.className = 'shDel'; del.title = '지우기'; del.setAttribute('aria-label', '가시설 지우기'); del.textContent = '×';
      del.addEventListener('click', () => shEdit(L.src, (list) => list.filter((r, j) => j !== q.i)));
      row.append(sel, go, del);
      box.appendChild(row);
    }
  }
  $('tgShore').addEventListener('click', () => shOpen(shBox.hidden));
  document.querySelectorAll('#shTypes button').forEach((b) => b.addEventListener('click', () => (SH.arm === b.dataset.t ? shCancel() : shArm(b.dataset.t))));
  $('shCancel').addEventListener('click', shCancel);

  // ---------------------------------------------------------------- 수량 · 지장물 간섭 (360 과 같음 · 보기만)
  const QT = { on: false, memo: new WeakMap(), row: {}, txt: new Map(), cfv: -1 };
  const CF_KEY = 'rv_gap:' + KEY0, CF_CELL = 10, CF_STEP = 0.5;
  const CF = { gap: 0.3, list: [], of: null, key: '', t: 0, ver: 0, memo: new WeakMap(), grid: null, gridOf: -1 };
  if (isNum(lsGet(CF_KEY))) CF.gap = clamp(Number(lsGet(CF_KEY)), 0, 5);
  const Q_ROWS = [['len', '연장', 'm'], ['cut', '터파기', 'm³'], ['pav', '포장 깨기', 'm²'], ['sand', '모래 기초', 'm³'], ['fill', '되메우기', 'm³'],
    ['tmp', '임시점용', 'm²'], ['perm', '영구점용', 'm²']];
  function secQ(D, g, zc, n) {
    const zs = Math.min(zc - D / 2, g - 0.01), zb = zs - TR.bed, wb = D / 2 + TR.margin, zt = g - TR.tp;
    const wv = wb + n * Math.max(0, zt - zb);
    return [2 * wv * (g - Math.max(zt, zb)) + (wb + wv) * Math.max(0, zt - zb), wv];
  }
  function lineQty(L) {
    const key = [TR.tp, TR.n, TR.margin, TR.bed, S.cover].join('|');
    const m = QT.memo.get(L);
    if (m && m.key === key) return m.q;
    const Pp = L.pts, D = L.dia, cnt = Pp.length / 3, C = L.cum;
    let len = 0, cut = 0, pav = 0, g0 = gQ(L, 0), z0 = zcQ(L, 0);
    for (let k = 1; k < cnt; k++) {
      const g1 = gQ(L, k), z1 = zcQ(L, k), l = Math.hypot(Pp[k * 3] - Pp[k * 3 - 3], Pp[k * 3 + 1] - Pp[k * 3 - 2]);
      if (l > 0) {
        const ts = [0, 1];
        for (const q of L.shore) for (const s of [q.a, q.b]) if (s > C[k - 1] + 1e-9 && s < C[k] - 1e-9) ts.push((s - C[k - 1]) / l);
        ts.sort((a, b) => a - b);
        for (let i = 0; i + 1 < ts.length; i++) {
          const ta = ts[i], tb = ts[i + 1], tm = (ta + tb) / 2, h = (tb - ta) * l;
          const n = shoreAt(L, C[k - 1] + tm * l) ? 0 : TR.n, sq = (t) => secQ(D, g0 + (g1 - g0) * t, z0 + (z1 - z0) * t, n);
          const A = sq(ta), Mm = sq(tm), B = sq(tb);
          cut += h / 6 * (A[0] + 4 * Mm[0] + B[0]);
          pav += h / 3 * (A[1] + 4 * Mm[1] + B[1]);
        }
        len += l;
      }
      g0 = g1; z0 = z1;
    }
    const sand = 2 * (D / 2 + TR.margin) * TR.bed * len, pipe = Math.PI * D * D / 4 * len;
    const q = { len: len, cut: cut, pav: pav, sand: sand, fill: cut - pipe - sand, tmp: pav, perm: D * len };
    QT.memo.set(L, { key: key, q: q });
    return q;
  }
  function qtyLine() {
    const EL = edLine();
    if (EL) return EL;
    const c = [V.tgt[0] - TX, V.tgt[1] - TY];
    let best = null, bd = Infinity;
    for (const L of lines) {
      if (Math.max(L.e0 - c[0], 0, c[0] - L.e1) > bd || Math.max(L.n0 - c[1], 0, c[1] - L.n1) > bd) continue;
      const Pp = L.pts;
      for (let k = 0; k + 3 < Pp.length; k += 3) { const d = distPtSeg(c[0], c[1], Pp[k], Pp[k + 1], Pp[k + 3], Pp[k + 4]); if (d < bd) { bd = d; best = L; } }
    }
    return best;
  }
  function qTxt(el, t) { if (QT.txt.get(el) !== t) { QT.txt.set(el, t); el.textContent = t; } }
  const qFmt = (v) => (Number.isFinite(v) ? v.toLocaleString('ko-KR', { minimumFractionDigits: 1, maximumFractionDigits: 1 }) : '-');
  function qtyUI() {
    const L = qtyLine(), one = L ? lineQty(L) : null, tot = { len: 0, cut: 0, pav: 0, sand: 0, fill: 0, tmp: 0, perm: 0 };
    lines.forEach((Mq) => { const q = lineQty(Mq); Q_ROWS.forEach(([k]) => { tot[k] += q[k]; }); });
    qTxt($('qLine'), L ? L.label || 'D' + Math.round(L.dia * 1000) : '관로 없음');
    Q_ROWS.forEach(([k]) => {
      const [a, b] = QT.row[k], va = String(one ? one[k] : NaN), vb = String(lines.length ? tot[k] : NaN);
      if (a.dataset.v !== va) { a.dataset.v = va; a.textContent = qFmt(Number(va)); }
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
      b.dataset.at = [sp.e, sp.n, sp.c].map((v) => round(v, 4)).join(',');
      b.addEventListener('click', () => goTo(sp.e, sp.n, sp.g));
      box.appendChild(b);
    });
  }
  (function buildQty() {
    const tab = $('qTab');
    ['', '이 관', '전체'].forEach((t) => { const s = document.createElement('span'); s.className = 'qh'; s.textContent = t; tab.appendChild(s); });
    Q_ROWS.forEach(([k, name, unit]) => {
      const lab = document.createElement('span'); lab.className = 'ql'; lab.textContent = name + ' ' + unit;
      const a = document.createElement('span'), b = document.createElement('span');
      a.className = 'qv'; b.className = 'qv'; a.id = 'q_' + k; b.id = 'qt_' + k;
      tab.append(lab, a, b);
      QT.row[k] = [a, b];
    });
    const qShow = (on) => { QT.on = !!on; $('qtyBox').hidden = !QT.on; setOn($('tgQty'), QT.on); QT.cfv = -1; redraw(); };
    $('tgQty').addEventListener('click', () => qShow(!QT.on));
    $('qX').addEventListener('click', () => qShow(false));
    const gap = $('qGap');
    gap.value = String(CF.gap);
    gap.addEventListener('input', () => { if (!isNum(gap.value)) return; CF.gap = clamp(Number(gap.value), 0, 5); lsSet(CF_KEY, CF.gap); redraw(); });
    gap.addEventListener('change', () => { gap.value = String(round(CF.gap, 3)); });
  })();
  const SS = [0, 0, 0, 0, 0, 0];
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
        s = den > 1e-12 * a * e ? clamp((b * f - c * e) / den, 0, 1) : 0.5;
        t = (b * s + f) / e;
        if (t < 0) { t = 0; s = clamp(-c / a, 0, 1); } else if (t > 1) { t = 1; s = clamp((b - c) / a, 0, 1); }
      }
    }
    SS[0] = ax + ux * s; SS[1] = ay + uy * s; SS[2] = az + uz * s;
    SS[3] = cx + vx * t; SS[4] = cy + vy * t; SS[5] = cz + vz * t;
    return Math.hypot(SS[0] - SS[3], SS[1] - SS[4], SS[2] - SS[5]);
  }
  function cfGrid() {
    if (CF.grid && CF.gridOf === obGen) return CF.grid;
    const cells = new Map(), segs = [];
    let rmax = 0;
    obst.forEach((O, oi) => {
      const Pp = O.pts, r = O.dia / 2;
      rmax = Math.max(rmax, r);
      for (let k = 0; k + 3 < Pp.length; k += 3) {
        const id = segs.push({ oi: oi, r: r, p: Pp, k: k }) - 1;
        const x0 = Math.floor(Math.min(Pp[k], Pp[k + 3]) / CF_CELL), x1 = Math.floor(Math.max(Pp[k], Pp[k + 3]) / CF_CELL);
        const y0 = Math.floor(Math.min(Pp[k + 1], Pp[k + 4]) / CF_CELL), y1 = Math.floor(Math.max(Pp[k + 1], Pp[k + 4]) / CF_CELL);
        for (let x = x0; x <= x1; x++) for (let y = y0; y <= y1; y++) { const c = x + ',' + y; if (!cells.has(c)) cells.set(c, []); cells.get(c).push(id); }
      }
    });
    CF.grid = { cells: cells, segs: segs, rmax: rmax };
    CF.gridOf = obGen;
    return CF.grid;
  }
  function cfLine(L) {
    const G = cfGrid(), out = [];
    if (!G.segs.length) return out;
    const Pp = L.pts, cnt = Pp.length / 3, rp = L.dia / 2, reach = CF.gap + rp + G.rmax;
    const stamp = new Int32Array(G.segs.length).fill(-1), runs = new Map();
    const flush = (oi) => {
      const r = runs.get(oi);
      runs.delete(oi);
      let mn = Infinity;
      r.it.forEach((b) => { mn = Math.min(mn, b.c); });
      const nr = r.it.filter((b) => b.c <= mn + 0.01), a = nr[(nr.length - 1) >> 1], b = nr[nr.length >> 1];
      const e = (a.e + b.e) / 2, n = (a.n + b.n) / 2, g = groundAt(e, n);
      out.push({ src: L.src, oi: oi, c: mn, e: e, n: n, g: Number.isFinite(g) ? g : H0, p: L.label || 'D' + Math.round(L.dia * 1000), o: obst[oi].text });
    };
    let pi = 0;
    for (let k = 0; k + 1 < cnt; k++) {
      const ax = Pp[k * 3], ay = Pp[k * 3 + 1], az = zcQ(L, k), bx = Pp[k * 3 + 3], by = Pp[k * 3 + 4], bz = zcQ(L, k + 1);
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
              const s = G.segs[id], Qp = s.p, j = s.k;
              const c = segSeg(x0, y0, z0, x1, y1, z1, Qp[j], Qp[j + 1], Qp[j + 2], Qp[j + 3], Qp[j + 4], Qp[j + 5]) - rp - s.r;
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
    redraw();
  }

  // ---------------------------------------------------------------- 검사점 (설계서 6.4 · 매핑에 쓰지 않은 독립 점)
  // pts = [{name, E, N, Z(측량 표고 | null), m: [E, N, Z] (3D 에서 찍은 곳) | null}] · 표시 = 팔면체 + 세로 막대 (모델 너머로도 보임)
  const CK = { pts: [], active: -1, vao: null, ranges: [] };
  const H0f = () => H0;
  function ckRel(p) { return [p.E - ORG[0], p.N - ORG[1], (p.Z != null ? p.Z : H0f()) - H0f()]; }
  function ckBuild() {
    const v = []; CK.ranges = [];
    const O = [[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]];
    const Fc = [[0, 2, 4], [2, 1, 4], [1, 3, 4], [3, 0, 4], [2, 0, 5], [1, 2, 5], [3, 1, 5], [0, 3, 5]];
    for (const p of CK.pts) {
      const c = ckRel(p), r = 0.25, h = 2.5, w = 0.04, s0 = v.length / 3;
      for (const f of Fc) for (const k of f) v.push(c[0] + r * O[k][0], c[1] + r * O[k][1], c[2] + r * O[k][2]);
      v.push(c[0] - w, c[1], c[2], c[0] + w, c[1], c[2], c[0] + w, c[1], c[2] + h, c[0] - w, c[1], c[2], c[0] + w, c[1], c[2] + h, c[0] - w, c[1], c[2] + h);
      v.push(c[0], c[1] - w, c[2], c[0], c[1] + w, c[2], c[0], c[1] + w, c[2] + h, c[0], c[1] - w, c[2], c[0], c[1] + w, c[2] + h, c[0], c[1] - w, c[2] + h);
      CK.ranges.push([s0, v.length / 3 - s0]);
    }
    CK.vao = v.length ? buf([[0, new Float32Array(v), 3]]) : null;
  }
  function drawChecks(M) {
    if (!CK.vao || $('ck').style.display === 'none') return;
    gl.disable(gl.DEPTH_TEST); gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.useProgram(FLAT.p); gl.uniformMatrix4fv(FLAT.u.M, false, M); gl.bindVertexArray(CK.vao);
    CK.ranges.forEach(([s, n], i) => {
      const p = CK.pts[i], c = i === CK.active ? [1, 0.6, 0.12, 0.95] : p.m ? [0.35, 0.9, 0.45, 0.9] : [1, 0.85, 0.4, 0.85];
      gl.uniform4f(FLAT.u.col, c[0], c[1], c[2], c[3]); gl.drawArrays(gl.TRIANGLES, s, n);
    });
    gl.enable(gl.DEPTH_TEST); gl.disable(gl.BLEND);
  }
  function ckRes(p) { return p.m ? { h: Math.hypot(p.m[0] - p.E, p.m[1] - p.N), z: p.Z != null ? p.m[2] - p.Z : null } : null; }
  function ckRender() {
    const box = $('ckList'); box.textContent = '';
    CK.pts.forEach((p, i) => {
      const r = ckRes(p), d = document.createElement('div');
      d.className = 'ci' + (i === CK.active ? ' on' : '');
      d.innerHTML = '<span class="nm"></span><span class="r"></span>';
      d.querySelector('.nm').textContent = p.name + (p.Z != null ? ' · Z ' + p.Z.toFixed(2) : '');
      d.querySelector('.r').textContent = r ? '평면 ' + r.h.toFixed(3) + (r.z != null ? ' · 높이 ' + (r.z >= 0 ? '+' : '') + r.z.toFixed(3) : '') : '찍기 전';
      d.addEventListener('click', () => { CK.active = i; const c = ckRel(p); V.tgt = c; V.d = Math.min(V.d, 30); ckRender(); redraw(); });
      box.appendChild(d);
    });
    const ms = CK.pts.map(ckRes).filter(Boolean), hz = ms.map((r) => r.h), vz = ms.map((r) => r.z).filter((v) => v != null);
    const rm = (a) => (a.length ? Math.sqrt(a.reduce((s, v) => s + v * v, 0) / a.length) : null);
    $('ckRms').textContent = ms.length ? '평면 RMSE ' + rm(hz).toFixed(3) + (vz.length ? ' · 높이 ' + rm(vz).toFixed(3) : '') + ' m (' + ms.length + '/' + CK.pts.length + ')' : CK.pts.length + '점';
    $('ckHint').textContent = CK.active >= 0 ? '«' + CK.pts[CK.active].name + '» 을 모델에서 누르기' : '점을 고르고 모델에서 누르기';
    window.__ck = { n: CK.pts.length, measured: ms.length, h: rm(hz), v: rm(vz) };
  }
  function ckMeasure(hh) {
    const p = CK.pts[CK.active]; if (!p) return;
    p.m = [hh.p[0] + ORG[0], hh.p[1] + ORG[1], hh.p[2] + H0f()];
    const nx = CK.pts.findIndex((q, j) => j > CK.active && !q.m);
    if (nx >= 0) { CK.active = nx; const c = ckRel(CK.pts[nx]); V.tgt = c; }
    ckRender(); redraw();
  }
  async function ckLoad() {                     // 검사점 파일 고르기 (프로그램) → true = 읽음
    const r = await call('pick_checkpoints'); if (!r) return false;
    if (!r.ok) { toast(r.msg); return false; }
    CK.pts = r.points.map((q) => ({ name: q.name, E: q.E, N: q.N, Z: q.Z, m: null })); CK.active = 0;
    ckBuild();
    if (r.note) toast(r.note);
    return true;
  }
  function ckShow() {
    $('side').hidden = false; setOn($('tgSide'), true);
    $('ck').style.display = 'flex'; setOn($('bCheck'), true); ckRender(); redraw();
  }
  $('bCheck').addEventListener('click', async () => {
    if ($('ck').style.display !== 'none') { $('ck').style.display = 'none'; setOn($('bCheck'), false); redraw(); return; }
    if (!CK.pts.length && !(await ckLoad())) return;
    ckShow();
  });
  $('ckClose').addEventListener('click', () => { $('ck').style.display = 'none'; setOn($('bCheck'), false); CK.active = -1; redraw(); });
  $('ckSave').addEventListener('click', async () => {
    const q = await call('save_checks', CK.pts.map((p) => ({ name: p.name, E: p.E, N: p.N, Z: p.Z, mE: p.m ? p.m[0] : null, mN: p.m ? p.m[1] : null, mZ: p.m ? p.m[2] : null })));
    if (q && q.ok) toast('보고서 저장 · ' + q.msg); else if (q) toast(q.msg);
  });
  window.__ckApi = { set: (pts) => { CK.pts = pts.map((q) => ({ name: q.name, E: q.E, N: q.N, Z: q.Z, m: null })); CK.active = 0; ckBuild(); $('ck').style.display = 'flex'; ckRender(); redraw(); }, measure: (p) => ckMeasure({ p }), CK };

  // ---------------------------------------------------------------- 재기 · 새 관 그리기 · 잘라내기 경계 (JACK 1126 · 오른쪽 메뉴에서 시작)
  // MS.pts = 세계 점 (매핑 원점 기준 · z = 표고 − 원점 표고). dist = 거리 (평면 · 경사) · area = 면적 (평면) · height = 높이 차 ·
  // prof = 지형 단면 (땅 = dem.js · 3D 모델 면 = 위에서 본 띠 그리기) · pipe = 새 관 (끝나면 관경 · 관종 · 이름 창) · crop = 잘라내기 경계.
  // 점 찍기 = 누르기 · 끝 = 두 번 누르기 · Enter · Esc = 취소 · Backspace = 마지막 점 빼기. 결과 칸은 [지우기] 까지 남음.
  // ng = 땅 아님 다각형 (여러 개 · 닫으면 NG.draft 로 · [적용] = 지금 것 + 새 것 → nonground_set)
  const MS = { kind: '', pts: [], hov: null, done: false, drag: -1, dg: null, rect: false, rNew: false, res: null, prof: null };
  // 땅 아님 (JACK 1126 · 프로그램 안에서만): polys = 지정된 것 (도면 좌표 [[E, N], …]) · draft = 새로 그린 것 (세계 점) · sel = 지우려고 고른 것
  const NG = { polys: [], got: false, draft: [], sel: -1, selT: 0 };
  const MS_TIP = { dist: '거리 · 점 찍기 · 두 번 누르기 · Enter = 끝', area: '면적 · 점 찍기 · 두 번 누르기 · Enter = 닫기',
    height: '높이 차 · 두 점', prof: '지형 단면 · 두 점', pipe: '새 관 · 점 찍기 · 두 번 누르기 · Enter = 끝' };
  const MS_COL = { dist: '#facc15', area: '#facc15', height: '#facc15', prof: '#facc15', pipe: '#a3e635', crop: '#f87171', ng: '#f87171' };
  const fM = (v) => v.toFixed(2) + ' m';
  const fA = (v) => v.toLocaleString('ko-KR', { minimumFractionDigits: 1, maximumFractionDigits: 1 }) + ' m²';
  const msBox = $('msBox'), msChip = $('msChip');
  function msStart(kind, w) {
    msClear();
    if (ED.on) edMode(false);
    shCancel(); cardHide();
    MS.kind = kind; MS.pts = w ? [w.slice()] : []; MS.done = false; MS.hov = null; MS.drag = -1; MS.rect = false;
    cv.classList.add('pick');
    msUI(); redraw();
  }
  function msClear() {
    if (!MS.kind) return;
    MS.kind = ''; MS.pts = []; MS.hov = null; MS.done = false; MS.drag = -1; MS.dg = null; MS.res = null; MS.prof = null;
    NG.draft = [];
    cv.classList.remove('pick');
    msUI(); redraw();
  }
  // 화면 → 세계 점: 3D 모델 면 · 잘라내기는 모델 밖이면 지금 높이 수평면
  function rayZ(x, y, z) {
    const vw = vwNow(), nx = (x - vw.w / 2) / (vw.w / 2), ny = (vw.h / 2 - y) / (vw.h / 2);
    const d = [0, 1, 2].map((i) => vw.f[i] + nx * vw.tanH * vw.r[i] + ny * vw.tanV * vw.u[i]);
    if (Math.abs(d[2]) < 1e-9) return null;
    const t = (z - vw.E[2]) / d[2];
    return t > 0 ? [vw.E[0] + t * d[0], vw.E[1] + t * d[1], z] : null;
  }
  function msPoint(x, y) {
    const h = pickAt(x, y);
    if (h) return h.p.slice();
    return MS.kind === 'crop' || MS.kind === 'ng' ? rayZ(x, y, MS.pts.length ? MS.pts[MS.pts.length - 1][2] : V.tgt[2]) : null;
  }
  const wScr = (p) => toScr(p[0] - TX, p[1] - TY, p[2] - TZ);
  function msTap(x, y) {
    if (!MS.kind) return false;
    if (MS.done) return MS.kind === 'crop';
    if (MS.kind === 'crop' && MS.rect) return true;                      // 네모 = 누르고 끌기 (msDown · msUp)
    const p = msPoint(x, y);
    if (!p) { toast('모델 위 누르기'); return true; }
    const last = MS.pts[MS.pts.length - 1];
    if (last && Math.hypot(p[0] - last[0], p[1] - last[1], p[2] - last[2]) < 0.01) return true;   // 두 번 누르기의 둘째
    MS.pts.push(p);
    if ((MS.kind === 'height' || MS.kind === 'prof') && MS.pts.length >= 2) { msFinish(); return true; }
    msUI(); redraw();
    return true;
  }
  function msFinish() {
    const k = MS.kind, need = k === 'area' || k === 'crop' ? 3 : 2;
    if (!k || MS.done) return;
    if (k === 'ng') { ngClose(); return; }                                // 땅 아님: 닫고 다음 다각형
    if (MS.pts.length < need) { toast('점 ' + need + '개 이상'); return; }
    if (k === 'pipe') { npShow(); return; }
    MS.done = true; MS.hov = null;
    if (k !== 'crop') cv.classList.remove('pick');
    if (k === 'prof') MS.prof = profGet(MS.pts[0], MS.pts[1]);
    msUI(); redraw();
  }
  function msBack() {
    if (!MS.kind || MS.done) return;
    if (MS.kind === 'ng' && !MS.pts.length && NG.draft.length) { MS.pts = NG.draft.pop(); msUI(); redraw(); return; }   // 닫은 것 다시 열기
    if (!MS.pts.length) return;
    MS.pts.pop(); msUI(); redraw();
  }
  function msCalc() {
    const Pp = MS.pts, n = Pp.length, r = { segs: [], plan: 0, slope: 0, area: 0, perim: 0, n: n };
    for (let i = 1; i < n; i++) {
      const a = Pp[i - 1], b = Pp[i], pl = Math.hypot(b[0] - a[0], b[1] - a[1]), dz = b[2] - a[2];
      r.segs.push([pl, Math.hypot(pl, dz), dz]); r.plan += pl; r.slope += Math.hypot(pl, dz);
    }
    if (n >= 3) {
      let a = 0;
      for (let i = 0, j = n - 1; i < n; j = i++) a += (Pp[j][0] + Pp[i][0]) * (Pp[j][1] - Pp[i][1]);
      r.area = Math.abs(a / 2);
      r.perim = r.plan + Math.hypot(Pp[0][0] - Pp[n - 1][0], Pp[0][1] - Pp[n - 1][1]);
    }
    return r;
  }
  function msUI() {
    const k = MS.kind, crop = k === 'crop', ng = k === 'ng', red = crop || ng;
    document.body.classList.toggle('msOn', !!k && !MS.done || crop);
    msChip.classList.toggle('show', !!k && (!MS.done || crop));
    msChip.classList.toggle('crop', red);
    $('msSeg').hidden = !crop;
    setOn($('msRect'), crop && MS.rect); setOn($('msPoly'), crop && !MS.rect);
    const n = MS.pts.length, nd = NG.draft.length;
    let tip = MS_TIP[k] || '';
    if (k === 'height' || k === 'prof') tip += n ? ' · 둘째 점' : ' · 첫 점';
    if (crop) tip = MS.done ? '점 끌어 고치기 · 적용 = 결과물 다시 만듦 (394장 3~5분 · 1.0.15 이전 프로젝트는 질감도 다시라 더 걸림)' : MS.rect ? (n ? '맞은 구석 누르기 · 끌기' : '두 구석 끌기') : '점 찍기 · 두 번 누르기 · Enter = 닫기 · 점 끌어 고치기';
    if (ng) tip = n ? '땅 아님 · 점 찍기 · 두 번 누르기 · Enter = 닫기' : nd ? '새 땅 아님 ' + nd + '곳 · 또 그리기 · 점 끌어 고치기 · 적용 = DTM 다시 만듦 (약 3분)' : '땅 아님 · 건물 둘레 점 찍기 · 두 번 누르기 · Enter = 닫기';
    $('msTip').textContent = tip;
    const end = $('msEnd');
    end.hidden = k === 'height' || k === 'prof';
    end.textContent = red ? '적용' : '끝';
    end.disabled = crop ? !MS.done : ng ? !(nd || n >= 3) : n < (k === 'area' ? 3 : 2);
    msChip.dataset.kind = k; msChip.dataset.n = String(n); msChip.dataset.nd = String(nd);
    // 결과 칸
    const show = !!k && k !== 'pipe' && !red && n >= (k === 'area' ? 3 : 2);
    msBox.hidden = !show;
    msBox.dataset.kind = show ? k : '';
    if (!show) { MS.res = null; return; }
    const r = MS.res = msCalc();
    msBox.classList.toggle('wide', k === 'prof');
    $('msTitle').textContent = { dist: '거리', area: '면적', height: '높이 차', prof: '지형 단면' }[k];
    const segs = $('msSegs'), svg = $('msSvg');
    segs.textContent = ''; svg.toggleAttribute('hidden', true);                 // SVG 는 .hidden 속성이 없음 → 글자 속성으로
    let rows = [];
    if (k === 'dist') {
      rows = [['plan', '평면 길이', r.plan, fM(r.plan)], ['slope', '경사 길이', r.slope, fM(r.slope)], ['n', '마디', r.segs.length, r.segs.length + '개']];
      if (r.segs.length > 1) {
        ['', '평면', '경사'].forEach((t) => { const s = document.createElement('span'); s.className = 'h'; s.textContent = t; segs.appendChild(s); });
        r.segs.forEach((g, i) => [String(i + 1), fM(g[0]), fM(g[1])].forEach((t, j) => { const s = document.createElement('span'); if (!j) s.className = 'h'; s.textContent = t; segs.appendChild(s); }));
      }
    } else if (k === 'area') {
      rows = [['area', '평면 면적', r.area, fA(r.area)], ['perim', '둘레', r.perim, fM(r.perim)], ['n', '점', n, n + '개']];
    } else {
      const a = MS.pts[0], b = MS.pts[1], g = r.segs[0], gr = g[0] > 0.01 ? g[2] / g[0] * 100 : NaN;
      rows = [['dz', '표고 차', g[2], (g[2] >= 0 ? '+' : '') + fM(g[2])], ['plan', '평면 거리', g[0], fM(g[0])],
        ['grade', '기울기', gr, Number.isFinite(gr) ? (gr >= 0 ? '+' : '') + gr.toFixed(1) + ' %' : '-'],
        ['za', 'A 표고', a[2] + H0, elTxt(a[2] + H0)], ['zb', 'B 표고', b[2] + H0, elTxt(b[2] + H0)]];
      if (k === 'prof') { rows = [rows[1], rows[0], rows[3], rows[4]]; rows[0] = ['len', '길이', g[0], fM(g[0])]; if (MS.prof) { svg.toggleAttribute('hidden', false); profSvg(svg, MS.prof); } }
    }
    kvRows($('msRows'), rows, 'ms_');
  }
  $('msEnd').addEventListener('click', () => { if (MS.kind === 'crop') crApply(); else if (MS.kind === 'ng') ngApply(); else msFinish(); });
  $('msX').addEventListener('click', msClear);
  $('msClr').addEventListener('click', msClear);
  // 지형 단면: 땅 (dem.js · 매끈한 땅 DTM) + 3D 모델 면 (위에서 본 가장 높은 면 · 두 점 사이 띠 1 화소 줄 그리기)
  function profModel(a, b, n) {
    if (!mesh || !fext) return null;
    const L = Math.hypot(b[0] - a[0], b[1] - a[1]);
    if (!(L > 0.01)) return null;
    const ux = (b[0] - a[0]) / L, uy = (b[1] - a[1]) / L, vx = -uy, vy = ux, hw = 0.05;
    const zlo = mesh.meta.min[2] - 5, zhi = mesh.meta.max[2] + 5, zr = zhi - zlo;
    const fb = gl.createFramebuffer(), tx = gl.createTexture(), dp = gl.createRenderbuffer();
    gl.bindTexture(gl.TEXTURE_2D, tx); gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA32F, n, 1, 0, gl.RGBA, gl.FLOAT, null);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    gl.bindRenderbuffer(gl.RENDERBUFFER, dp); gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH_COMPONENT24, n, 1);
    gl.bindFramebuffer(gl.FRAMEBUFFER, fb); gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tx, 0);
    gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, dp);
    gl.viewport(0, 0, n, 1);
    gl.colorMask(true, true, true, true); gl.depthMask(true);
    gl.clearBufferfv(gl.COLOR, 0, [0, 0, 0, -1]); gl.clear(gl.DEPTH_BUFFER_BIT);
    gl.enable(gl.DEPTH_TEST); gl.depthFunc(gl.LESS); gl.disable(gl.BLEND); gl.disable(gl.STENCIL_TEST); gl.disable(gl.CULL_FACE);
    // x = 측점 (0 ~ L → −1 ~ 1) · y = 선에서 옆 (±hw) · 깊이 = 높은 면이 앞 (hmSync 와 같음)
    const M = [2 * ux / L, vx / hw, 0, 0, 2 * uy / L, vy / hw, 0, 0, 0, 0, -2 / zr, 0,
      -2 * (ux * a[0] + uy * a[1]) / L - 1, -(vx * a[0] + vy * a[1]) / hw, (zlo + zhi) / zr, 1];
    gl.useProgram(PICK.p); gl.uniformMatrix4fv(PICK.u.M, false, M); gl.uniform1f(PICK.u.id, 0);
    for (const p of mesh.parts) { gl.bindVertexArray(p.vao); gl.drawArrays(gl.TRIANGLES, 0, p.n); }
    const px = new Float32Array(n * 4);
    gl.readPixels(0, 0, n, 1, gl.RGBA, gl.FLOAT, px);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null); gl.bindVertexArray(null);
    gl.deleteFramebuffer(fb); gl.deleteTexture(tx); gl.deleteRenderbuffer(dp);
    const out = new Array(n);
    for (let i = 0; i < n; i++) out[i] = px[i * 4 + 3] < 0 ? NaN : px[i * 4 + 2] + H0;
    redraw();
    return out;
  }
  function profGet(a, b) {
    const L = Math.hypot(b[0] - a[0], b[1] - a[1]), n = clamp(Math.ceil(L / 0.1), 2, 1024), s = [], dtm = [], e = [], nn = [];
    for (let i = 0; i < n; i++) {
      const t = (i + 0.5) / n, x = a[0] + (b[0] - a[0]) * t, y = a[1] + (b[1] - a[1]) * t;
      s.push(t * L); e.push(x + ORG[0]); nn.push(y + ORG[1]); dtm.push(demAt(x, y));
    }
    return { L: L, s: s, e: e, n: nn, dtm: dtm, mdl: profModel(a, b, n) || dtm.map(() => NaN), za: a[2] + H0, zb: b[2] + H0 };
  }
  function profSvg(svg, pr) {
    const W = 400, H = 210, pl = 50, prr = 12, pt = 24, pb = 24;
    let z0 = Infinity, z1 = -Infinity;
    for (const v of pr.dtm.concat(pr.mdl)) if (Number.isFinite(v)) { if (v < z0) z0 = v; if (v > z1) z1 = v; }
    svg.dataset.n = String(pr.s.length);
    if (!(z1 >= z0)) { svg.innerHTML = '<text x="200" y="105" text-anchor="middle" fill="#8b8b8b" font-size="12">높이 자료 없음</text>'; return; }
    const pad = Math.max(0.4, (z1 - z0) * 0.12); z0 -= pad; z1 += pad;
    const X = (s) => pl + s / pr.L * (W - pl - prr), Y = (z) => pt + (z1 - z) / (z1 - z0) * (H - pt - pb);
    const n1 = (v) => v.toFixed(1);
    const nice = (span, k) => { const raw = span / k, p = Math.pow(10, Math.floor(Math.log10(raw))), m = raw / p; return (m < 1.5 ? 1 : m < 3.5 ? 2 : m < 7.5 ? 5 : 10) * p; };
    const txt = (x, y, t, an, col) => '<text x="' + n1(x) + '" y="' + n1(y) + '" text-anchor="' + (an || 'middle') + '" fill="' + (col || '#8b8b8b') + '" font-size="10.5" font-weight="600">' + t + '</text>';
    let s = '';
    const dz = nice(z1 - z0, 4), dsx = nice(pr.L, 5);
    for (let z = Math.ceil(z0 / dz) * dz; z <= z1; z += dz) s += '<line x1="' + pl + '" x2="' + (W - prr) + '" y1="' + n1(Y(z)) + '" y2="' + n1(Y(z)) + '" stroke="#2c2c2c" stroke-width="1"/>' + txt(pl - 5, Y(z) + 3.5, z.toFixed(dz < 1 ? 1 : 0), 'end');
    for (let d = 0; d <= pr.L + 1e-6; d += dsx) s += '<line x1="' + n1(X(d)) + '" x2="' + n1(X(d)) + '" y1="' + pt + '" y2="' + (H - pb) + '" stroke="#2c2c2c" stroke-width="1"/>' + txt(X(d), H - pb + 14, d.toFixed(dsx < 1 ? 1 : 0));
    const runs = (arr) => { const out = []; let cur = []; arr.forEach((v, i) => { if (Number.isFinite(v)) cur.push([X(pr.s[i]), Y(v)]); else if (cur.length) { out.push(cur); cur = []; } }); if (cur.length) out.push(cur); return out; };
    const path = (rs) => rs.map((r) => 'M' + r.map((q) => n1(q[0]) + ' ' + n1(q[1])).join('L')).join('');
    const gr = runs(pr.dtm), md = runs(pr.mdl);
    for (const r of gr) s += '<path d="M' + n1(r[0][0]) + ' ' + (H - pb) + 'L' + r.map((q) => n1(q[0]) + ' ' + n1(q[1])).join('L') + 'L' + n1(r[r.length - 1][0]) + ' ' + (H - pb) + 'Z" fill="#5a4330" fill-opacity="0.85"/>';
    if (md.length) s += '<path d="' + path(md) + '" fill="none" stroke="#5fb3ff" stroke-width="1.3" stroke-linejoin="round"/>';
    if (gr.length) s += '<path d="' + path(gr) + '" fill="none" stroke="#f0dfae" stroke-width="1.7" stroke-linejoin="round"/>';
    s += txt(X(0) + 2, pt - 8, 'A', 'start', '#e9e9e9') + txt(X(pr.L) - 2, pt - 8, 'B', 'end', '#e9e9e9');
    s += '<rect x="' + (pl + 22) + '" y="' + (pt - 14) + '" width="12" height="3" fill="#f0dfae"/>' + txt(pl + 38, pt - 9.5, '땅 (DTM)', 'start') +
      '<rect x="' + (pl + 110) + '" y="' + (pt - 14) + '" width="12" height="3" fill="#5fb3ff"/>' + txt(pl + 126, pt - 9.5, '3D 모델 면', 'start') + txt(pl - 5, pt - 9.5, 'EL', 'end') + txt(W - prr, H - 6, 'm', 'end');
    svg.setAttribute('viewBox', '0 0 ' + W + ' ' + H);
    svg.innerHTML = s;
  }
  // 겹쳐 그리기 (overlay 끝에서): 선 · 점 · 글 · 잘라내기 경계 (바깥 어둡게) · 원본 사진 자리
  function wClip(a, b) {
    const ca = camc(a[0] - TX, a[1] - TY, a[2] - TZ), cb = camc(b[0] - TX, b[1] - TY, b[2] - TZ);
    if (ca[2] < NEAR && cb[2] < NEAR) return null;
    const at = (t) => ca.map((v, i) => v + (cb[i] - v) * t);
    const A = ca[2] < NEAR ? at((NEAR - ca[2]) / (cb[2] - ca[2])) : ca, B = cb[2] < NEAR ? at((NEAR - ca[2]) / (cb[2] - ca[2])) : cb;
    return [scr(A), scr(B)];
  }
  function msTag(text, x, y, col) {
    ov.font = '600 12px Malgun Gothic, Segoe UI, sans-serif'; ov.textAlign = 'center'; ov.textBaseline = 'middle';
    const tw = ov.measureText(text).width + 16, th = 20, bx = clamp(x - tw / 2, 4, VW.w - tw - 4), by = clamp(y - th / 2, 4, VW.h - th - 4);
    roundRect(ov, bx, by, tw, th, 10); ov.fillStyle = 'rgba(12, 14, 20, 0.86)'; ov.fill();
    if (col) { ov.strokeStyle = col; ov.lineWidth = 1.2; ov.stroke(); }
    ov.fillStyle = '#fff'; ov.fillText(text, bx + tw / 2, by + th / 2 + 0.5);
  }
  function msLine(a, b, col, dash) {
    const s = wClip(a, b);
    if (!s) return null;
    ov.setLineDash(dash ? [6, 5] : []);
    ov.beginPath(); ov.moveTo(s[0][0], s[0][1]); ov.lineTo(s[1][0], s[1][1]);
    ov.strokeStyle = PIN_DARK; ov.lineWidth = 5; ov.stroke();
    ov.strokeStyle = col; ov.lineWidth = 2.2; ov.stroke();
    ov.setLineDash([]);
    return s;
  }
  function msDot(p, col, r) {
    const s = wScr(p);
    if (!s) return;
    ov.beginPath(); ov.arc(s[0], s[1], r || 4.5, 0, 2 * Math.PI);
    ov.fillStyle = col; ov.fill(); ov.strokeStyle = PIN_DARK; ov.lineWidth = 1.6; ov.stroke();
  }
  function cropWorld(pg) { return pg.map((q) => { const x = q[0] - ORG[0], y = q[1] - ORG[1], g = demAt(x, y); return [x, y, Number.isFinite(g) ? g - H0 : V.tgt[2]]; }); }
  function msOverlay() {
    const w = VW.w, h = VW.h;
    if (TB.crop && (MS.kind === 'crop' || !tbBox.hidden)) {             // 지금 잘린 경계 (옅게)
      const c = cropWorld(TB.crop);
      ov.setLineDash([5, 5]); ov.strokeStyle = 'rgba(255, 255, 255, 0.55)'; ov.lineWidth = 1.5;
      c.forEach((p, i) => { const s = wClip(p, c[(i + 1) % c.length]); if (s) { ov.beginPath(); ov.moveTo(s[0][0], s[0][1]); ov.lineTo(s[1][0], s[1][1]); ov.stroke(); } });
      ov.setLineDash([]);
    }
    if (ngShown()) NG.polys.forEach((pg, i) => {                          // 지정된 땅 아님 (붉은 점선 · 고른 것은 진하게)
      const c = cropWorld(pg), sc = c.map(wScr), on = i === NG.sel;
      if (sc.every(Boolean)) {
        ov.beginPath(); sc.forEach((q, j) => (j ? ov.lineTo(q[0], q[1]) : ov.moveTo(q[0], q[1]))); ov.closePath();
        ov.fillStyle = on ? 'rgba(248, 113, 113, 0.34)' : 'rgba(248, 113, 113, 0.1)'; ov.fill();
      }
      ov.setLineDash([6, 4]);
      c.forEach((p, j) => {
        const s = wClip(p, c[(j + 1) % c.length]);
        if (!s) return;
        ov.beginPath(); ov.moveTo(s[0][0], s[0][1]); ov.lineTo(s[1][0], s[1][1]);
        ov.strokeStyle = PIN_DARK; ov.lineWidth = on ? 4.5 : 3.6; ov.stroke();
        ov.strokeStyle = '#f87171'; ov.lineWidth = on ? 2.6 : 1.8; ov.stroke();
      });
      ov.setLineDash([]);
    });
    if (MS.kind === 'ng') NG.draft.forEach((pg) => {                      // 새로 그린 땅 아님 (닫힘 · 아직 적용 전)
      const sc = pg.map(wScr);
      if (sc.every(Boolean)) {
        ov.beginPath(); sc.forEach((q, j) => (j ? ov.lineTo(q[0], q[1]) : ov.moveTo(q[0], q[1]))); ov.closePath();
        ov.fillStyle = 'rgba(248, 113, 113, 0.24)'; ov.fill();
      }
      pg.forEach((p, j) => msLine(p, pg[(j + 1) % pg.length], MS_COL.ng, false));
      pg.forEach((p) => msDot(p, '#ffffff', 5.5));
    });
    if (PH.at && !phBox.hidden) {                                         // 원본 사진 자리
      const s = wScr(PH.at);
      if (s) { ov.beginPath(); ov.arc(s[0], s[1], 9, 0, 2 * Math.PI); ov.strokeStyle = PIN_DARK; ov.lineWidth = 5; ov.stroke(); ov.strokeStyle = CF_RED; ov.lineWidth = 2.5; ov.stroke(); }
    }
    const k = MS.kind;
    if (!k) return;
    const col = MS_COL[k];
    let Pp = MS.pts.slice();
    if (k === 'crop' && MS.rect && !MS.done && Pp.length === 1 && MS.hov) Pp = crRect(Pp[0], MS.hov);
    else if (!MS.done && MS.hov && Pp.length) Pp.push(MS.hov);
    const closed = (k === 'area' || k === 'crop' || k === 'ng') && Pp.length >= 3;
    if (closed) {
      const sc = Pp.map(wScr);
      if (sc.every(Boolean)) {
        ov.beginPath();
        if (k === 'crop') ov.rect(0, 0, w, h);
        sc.forEach((q, i) => (i ? ov.lineTo(q[0], q[1]) : ov.moveTo(q[0], q[1])));
        ov.closePath();
        ov.fillStyle = k === 'crop' ? 'rgba(0, 0, 0, 0.5)' : k === 'ng' ? 'rgba(248, 113, 113, 0.16)' : 'rgba(250, 204, 21, 0.16)';
        ov.fill('evenodd');
      }
    }
    const nSeg = closed ? Pp.length : Pp.length - 1;
    for (let i = 0; i < nSeg; i++) {
      const a = Pp[i], b = Pp[(i + 1) % Pp.length], s = msLine(a, b, col, !MS.done && (i >= MS.pts.length - 1));
      if (s && k === 'dist') msTag(fM(Math.hypot(b[0] - a[0], b[1] - a[1])), (s[0][0] + s[1][0]) / 2, (s[0][1] + s[1][1]) / 2 - 14);
    }
    if (k === 'height' && Pp.length >= 2) {                               // 세로 · 가로 보조선 + 표고 차 · 기울기
      const a = Pp[0], b = Pp[1], lo = a[2] <= b[2] ? a : b, hi = lo === a ? b : a, c = [lo[0], lo[1], hi[2]];
      msLine(lo, c, '#ffffff', true); msLine(c, hi, '#ffffff', true);
      const s = wClip(a, b), pl = Math.hypot(b[0] - a[0], b[1] - a[1]), dz = b[2] - a[2];
      if (s) msTag('Δ ' + (dz >= 0 ? '+' : '') + fM(dz) + (pl > 0.01 ? ' · ' + (dz / pl * 100).toFixed(1) + ' %' : ''), (s[0][0] + s[1][0]) / 2, (s[0][1] + s[1][1]) / 2 - 16, col);
    }
    if (k === 'prof' && Pp.length >= 2) {
      const s = wClip(Pp[0], Pp[1]);
      if (s) msTag(fM(Math.hypot(Pp[1][0] - Pp[0][0], Pp[1][1] - Pp[0][1])), (s[0][0] + s[1][0]) / 2, (s[0][1] + s[1][1]) / 2 - 16, col);
      const sa = wScr(Pp[0]), sb = wScr(Pp[1]);
      if (sa) msTag('A', sa[0], sa[1] - 18); if (sb) msTag('B', sb[0], sb[1] - 18);
    }
    if (k === 'area' && closed) {
      const sc = Pp.map(wScr).filter(Boolean);
      let A = 0;
      for (let i = 0, j = Pp.length - 1; i < Pp.length; j = i++) A += (Pp[j][0] + Pp[i][0]) * (Pp[j][1] - Pp[i][1]);
      if (sc.length) msTag(fA(Math.abs(A / 2)), sc.reduce((s2, q) => s2 + q[0], 0) / sc.length, sc.reduce((s2, q) => s2 + q[1], 0) / sc.length, col);
    }
    if (k === 'dist' && MS.pts.length >= 3) {
      const r = msCalc(), s = wScr(MS.pts[MS.pts.length - 1]);
      if (s) msTag('합계 ' + fM(r.plan), s[0], s[1] + 22, col);
    }
    MS.pts.forEach((p, i) => msDot(p, i === 0 && closed && !MS.done ? col : '#ffffff', k === 'crop' || k === 'ng' ? 5.5 : 4.5));
  }

  // ---------------------------------------------------------------- 잘라내기 (결과물 다듬기 · 오른쪽 메뉴 «여기서부터 잘라내기»): 위에서 보기 · 네모 = 두 구석 끌기 · 다각형 = 점 찍기
  // [적용] → crop_apply({polygon: [[E, N] 도면 좌표 절대값]}) → 진행 → 다시 엶
  function crStart(w) {
    if (!HOST()) { toast('프로그램 안에서만'); return; }
    msStart('crop', null);
    if (V.el <= 80) { V.el = 89.5; setOn($('bTop'), true); }
    if (w) V.tgt = w.slice();
    MS.rect = true;
    MS.pts = w ? [w.slice()] : [];
    if (tbBox.hidden) tbShow(true); else tbCropGet();
    msUI(); redraw();
  }
  function crRect(a, b) {                                              // 화면에 나란한 네모 (지금 방위)
    const az = V.az * D2R, r = [Math.cos(az), Math.sin(az)], u = [-r[1], r[0]];
    const du = (b[0] - a[0]) * r[0] + (b[1] - a[1]) * r[1], dv = (b[0] - a[0]) * u[0] + (b[1] - a[1]) * u[1], z = (a[2] + b[2]) / 2;
    return [[a[0], a[1], z], [a[0] + r[0] * du, a[1] + r[1] * du, z], [b[0], b[1], z], [a[0] + u[0] * dv, a[1] + u[1] * dv, z]];
  }
  function crMode(rect) {
    if (MS.kind !== 'crop') return;
    MS.rect = rect; MS.done = false; MS.hov = null;
    MS.pts = MS.pts.slice(0, 1);
    cv.classList.add('pick');
    msUI(); redraw();
  }
  $('msRect').addEventListener('click', () => crMode(true));
  $('msPoly').addEventListener('click', () => crMode(false));
  function crCross(P) {                                                // 다각형 변끼리 엇갈리면 true (이웃 변은 빼고)
    const n = P.length, cr = (a, b, c) => (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
    for (let i = 0; i < n; i++) for (let j = i + 2; j < n; j++) {
      if (i === 0 && j === n - 1) continue;
      const a = P[i], b = P[(i + 1) % n], c = P[j], d = P[(j + 1) % n];
      if (cr(a, b, c) * cr(a, b, d) < 0 && cr(c, d, a) * cr(c, d, b) < 0) return true;
    }
    return false;
  }
  function crApply() {
    if (MS.kind !== 'crop' || !MS.done || MS.pts.length < 3) { toast('경계 먼저'); return; }
    if (crCross(MS.pts)) { toast('경계선이 서로 엇갈림 · 점을 끌어 고치기'); return; }
    const poly = MS.pts.map((p) => [round(p[0] + ORG[0], 3), round(p[1] + ORG[1], 3)]);
    msClear();
    tbJob('crop', () => call('crop_apply', { polygon: poly }));
  }
  // 누르기 (왼쪽): 네모 그리기 · 꼭짓점 끌기 (잘라내기) → true 면 이 누르기는 재기 몫 (돌리기 · 옮기기 안 함)
  function msDown(x, y) {
    if (MS.kind === 'ng') {                                              // 땅 아님: 그리는 것 · 닫은 것 꼭짓점 끌기
      let best = null, bd = 10;
      const chk = (p, o) => { const s = wScr(p); if (s) { const d = Math.hypot(s[0] - x, s[1] - y); if (d < bd) { bd = d; best = o; } } };
      MS.pts.forEach((p, i) => chk(p, { i: i }));
      NG.draft.forEach((pg, d) => pg.forEach((p, i) => chk(p, { d: d, i: i })));
      if (!best) return false;
      if (best.d == null) MS.drag = best.i; else MS.dg = [best.d, best.i];
      return true;
    }
    if (MS.kind !== 'crop') return false;
    if (MS.rect && !MS.done) {
      MS.rNew = !MS.pts.length;
      if (MS.rNew) { const p = msPoint(x, y); if (!p) return false; MS.pts = [p]; MS.hov = null; redraw(); }
      return true;
    }
    let best = -1, bd = 10;
    for (let i = 0; i < MS.pts.length; i++) { const s = wScr(MS.pts[i]); if (s) { const d = Math.hypot(s[0] - x, s[1] - y); if (d < bd) { bd = d; best = i; } } }
    if (best < 0) return false;
    MS.drag = best;
    return true;
  }
  function msMove(x, y) {
    if (MS.dg) { const p = msPoint(x, y); if (p) { NG.draft[MS.dg[0]][MS.dg[1]] = p; redraw(); } return; }
    if (MS.drag >= 0) { const p = msPoint(x, y); if (p) { MS.pts[MS.drag] = p; redraw(); } return; }
    if (MS.kind === 'crop' && MS.rect && !MS.done) { MS.hov = msPoint(x, y); redraw(); }
  }
  function msUp(x, y, moved) {
    if (MS.dg) {                                                         // 닫은 땅 아님 꼭짓점: 안 끌고 누름 = 그 자리에 새 점
      MS.dg = null;
      if (!moved) msTap(x, y);
      msUI(); redraw();
      return;
    }
    if (MS.drag >= 0) {
      const i = MS.drag;
      MS.drag = -1;
      if (!moved && !MS.done) {
        if (i === 0 && MS.pts.length >= 3) msFinish();                  // 첫 점 누르기 = 닫기
        else if (i !== MS.pts.length - 1) msTap(x, y);
      }
      msUI(); redraw();
      return;
    }
    if (MS.kind === 'crop' && MS.rect && !MS.done && MS.pts.length === 1 && (moved || !MS.rNew)) {
      const b = msPoint(x, y), a = MS.pts[0];
      if (b && Math.hypot(b[0] - a[0], b[1] - a[1]) > 0.05) { MS.pts = crRect(a, b); MS.hov = null; MS.done = true; }
    }
    MS.rNew = false;
    msUI(); redraw();
  }
  TB.cut = (w) => crStart(w);
  TB.flush = () => pipesFlush();
  TB.onCrop = redraw;

  // ---------------------------------------------------------------- 땅 아님 (JACK 1126 · 결과물 다듬기 «땅 아님» · 오른쪽 메뉴 «여기 땅 아님 지정»)
  // DTM 필터가 놓친 큰 건물 등을 다각형으로 → nonground_set({polygons: 지금 것 + 새 것}) → DTM · 등고선 · dem.js 다시 → 다시 엶.
  // 지정된 것 = 맨땅 보기 · 다듬기 칸 · 그리는 동안 붉은 점선 · 누르기 (오른쪽 메뉴) = 그것만 지우기. 프로그램 안에서만.
  const ngShown = () => NG.got && ngHost() && (DT.on || !tbBox.hidden || MS.kind === 'ng');
  function ngUI() {
    const n = NG.polys.length, st = $('ngState');
    st.textContent = n ? '땅 아님 ' + n + '곳' : '땅 아님 없음';
    st.classList.toggle('on', n > 0);
    $('ngClear').hidden = !n;
  }
  function ngGet() {
    const h = HOST();
    if (!h || typeof h.nonground_get !== 'function') return Promise.resolve(NG.polys);
    return Promise.resolve().then(() => h.nonground_get()).then((r) => {
      const ok = (q) => Array.isArray(q) && isNum(q[0]) && isNum(q[1]);
      NG.polys = (r && Array.isArray(r.polygons) ? r.polygons : []).filter((pg) => Array.isArray(pg) && pg.length >= 3 && pg.every(ok)).map((pg) => pg.map((q) => [Number(q[0]), Number(q[1])]));
      NG.got = true;
      if (NG.sel >= NG.polys.length) NG.sel = -1;
      ngUI(); redraw();
      return NG.polys;
    }, () => NG.polys);
  }
  TB.ngGet = ngGet;
  // 세계 점 w 가 든 지정된 다각형 차례 (없으면 -1)
  function ngAt(w) {
    const x = w[0] + ORG[0], y = w[1] + ORG[1];
    for (let k = NG.polys.length - 1; k >= 0; k--) {
      const p = NG.polys[k];
      let inside = false;
      for (let i = 0, j = p.length - 1; i < p.length; j = i++) {
        if ((p[i][1] > y) !== (p[j][1] > y) && x < (p[j][0] - p[i][0]) * (y - p[i][1]) / (p[j][1] - p[i][1]) + p[i][0]) inside = !inside;
      }
      if (inside) return k;
    }
    return -1;
  }
  function ngStart(w) {
    if (!ngHost()) { toast('프로그램 안에서만'); return; }
    if (MS.kind === 'ng') {                                              // 이미 그리는 중: 그리던 것은 닫아 두고 새로 (지우지 않음)
      if (MS.pts.length >= 3) ngClose(); else { MS.pts = []; MS.hov = null; MS.drag = -1; }
    } else msStart('ng', null);
    if (V.el <= 80) { V.el = 89.5; setOn($('bTop'), true); }
    if (w) { V.tgt = w.slice(); MS.pts = [w.slice()]; }
    if (tbBox.hidden) tbShow(true); else ngGet();
    msUI(); redraw();
  }
  TB.ng = ngStart;
  // 그리던 다각형 닫기 → NG.draft (점 셋 이상 · 변 안 엇갈림 · 1 m² 이상 = 프로그램이 받는 것)
  function ngClose() {
    const pg = MS.pts;
    if (pg.length < 3) { if (pg.length) toast('점 3개 이상'); return false; }
    if (crCross(pg)) { toast('경계선이 서로 엇갈림 · 점을 끌어 고치기'); return false; }
    let a = 0;
    for (let i = 0, j = pg.length - 1; i < pg.length; j = i++) a += (pg[j][0] + pg[i][0]) * (pg[j][1] - pg[i][1]);
    if (Math.abs(a / 2) < 1) { toast('너무 작음 · 1 m² 이상'); return false; }
    NG.draft.push(pg.slice());
    MS.pts = []; MS.hov = null; MS.drag = -1;
    msUI(); redraw();
    return true;
  }
  function ngSet(polys) { tbJob('ng', () => call('nonground_set', { polygons: polys })); }
  // [적용]: 그리던 것도 닫고 → 목록을 새로 읽어 (다른 데서 바뀌었어도) 지금 것 + 새 것
  function ngApply() {
    if (MS.kind !== 'ng') return;
    if (MS.pts.length && !ngClose()) return;
    if (!NG.draft.length) { toast('다각형 먼저'); return; }
    for (const pg of NG.draft) {                                         // 닫은 뒤 꼭짓점을 끌어 엇갈리거나 작아졌으면 막음 (1126 검토)
      let ar = 0;
      for (let i = 0, j = pg.length - 1; i < pg.length; j = i++) ar += (pg[j][0] + pg[i][0]) * (pg[j][1] - pg[i][1]);
      if (crCross(pg)) { toast('경계선이 서로 엇갈린 다각형이 있음 · 점을 끌어 고치기'); return; }
      if (Math.abs(ar / 2) < 1) { toast('너무 작은 다각형이 있음 · 1 m² 이상'); return; }
    }
    if (TB.id != null) { toast('다른 일 하는 중 · 끝난 뒤 적용'); return; }    // 그린 것을 지우기 전에 (1126 검토)
    const add = NG.draft.map((pg) => pg.map((p) => [round(p[0] + ORG[0], 3), round(p[1] + ORG[1], 3)]));
    const keep = NG.draft.map((pg) => pg.map((p) => p.slice()));
    msClear();
    const back = (r) => {                                                // 거절 · 실패 → 그리던 상태로 되돌림
      if (!r || r.ok === false || r.job == null) { msStart('ng', null); NG.draft = keep; msUI(); redraw(); }
      return r;
    };
    tbJob('ng', () => ngGet().then(() => (NG.got ? call('nonground_set', { polygons: NG.polys.concat(add) }) : { ok: false, msg: '땅 아님 목록 못 읽음' })).then(back, (e) => { back(null); throw e; }));
  }
  function ngAsk(i) {
    NG.sel = i; redraw();
    clearTimeout(NG.selT); NG.selT = setTimeout(() => { NG.sel = -1; redraw(); }, 10000);
    toast('땅 아님 ' + (i + 1) + ' 지우기 · DTM 다시 만듦', () => { const rest = NG.polys.filter((q, j) => j !== i); NG.sel = -1; ngSet(rest); }, '지우기');
  }
  // 누르기 (재기 · 고치기 · 관이 아닐 때): 지정된 다각형 안이면 지우기 묻기
  function ngTap(x, y) {
    if (!ngShown() || !NG.polys.length) return false;
    const h = pickAt(x, y), w = h ? h.p : rayZ(x, y, V.tgt[2]), i = w ? ngAt(w) : -1;
    if (i < 0) { if (NG.sel >= 0) { NG.sel = -1; redraw(); } return false; }
    ngAsk(i);
    return true;
  }
  $('ngDraw').addEventListener('click', () => ngStart(null));
  $('ngClear').addEventListener('click', () => toast('땅 아님 모두 없애고 DTM 다시 만들기', () => ngSet([]), '없애기'));
  window.addEventListener('pywebviewready', () => { if (DT.on || !tbBox.hidden) ngGet(); });

  // ---------------------------------------------------------------- 새 관 (오른쪽 메뉴 «여기서 새 관 그리기»): 점 찍기 → 관경 · 관종 · 이름 창 → RV_PIPES 새 줄
  // 저장 = 관로 고치기와 같은 길 (edCommit → save_pipes · 이 브라우저 · 되돌리기). 깊이 = 평균매설깊이 (관 표고 없음).
  const NP = { mat: 'ci', dia: 0 };
  function npMatUI() { $('npMat').querySelectorAll('button').forEach((b) => setOn(b, b.dataset.m === NP.mat)); }
  MAT_T.forEach((m) => {
    const b = document.createElement('button');
    b.type = 'button'; b.dataset.m = m; b.textContent = MAT_SHORT[m]; b.title = MAT_NAME[m];
    b.addEventListener('click', () => { NP.mat = m; npMatUI(); });
    $('npMat').appendChild(b);
  });
  function npShow() {
    const cnt = new Map();
    lines.forEach((L) => { const d = Math.round(L.dia * 1000); cnt.set(d, (cnt.get(d) || 0) + 1); });
    const top = [...cnt.entries()].sort((a, b) => b[1] - a[1])[0];
    $('npDia').value = String(NP.dia || (top ? top[0] : 300));
    npMatUI();
    $('npName').value = '';
    $('npMsg').textContent = '';
    $('npNote').textContent = '점 ' + MS.pts.length + '개 · ' + msCalc().plan.toFixed(1) + ' m · 깊이 = 평균매설깊이 ' + round(S.cover, 2) + ' m';
    dlgShow('npDlg');
  }
  function npGo() {
    const mm = Number($('npDia').value);
    if (!(mm >= 20 && mm <= 5000)) { $('npMsg').textContent = '관경 20 ~ 5000 mm'; return; }
    if (MS.kind !== 'pipe' || MS.pts.length < 2) { dlgClose(); return; }
    if (!window.RV_PIPES || !Array.isArray(window.RV_PIPES.lines)) {
      window.RV_PIPES = { layers: [], lines: [], origin: { E: ORG[0], N: ORG[1] } };
      poSet(window.RV_PIPES);
    }
    const Pp = window.RV_PIPES;
    if (!Array.isArray(Pp.layers)) Pp.layers = [];
    let li = Pp.layers.findIndex((l) => l && l.name === '새 관');
    if (li < 0) { Pp.layers.push({ name: '새 관', color: '#a3e635' }); li = Pp.layers.length - 1; }
    const raw = { layer: li, label: $('npName').value.trim(), dia: round(mm / 1000, 4), hasZ: false, mat: NP.mat };
    edRebuild(raw, MS.pts.map((p) => [p[0] - TX, p[1] - TY, null]));
    const src = Pp.lines.length;
    Pp.lines.push({ pts: [] });
    const before = JSON.stringify(Pp.lines[src]);
    Pp.lines[src] = raw;
    lines = buildLines(Pp);
    pipeGen++;
    NP.dia = mm;
    edCommit(src, before, JSON.stringify(raw));
    const L = lines.find((q) => q.src === src);
    msClear();
    dlgClose();
    if (!S.pipes) setPipes(true);
    infoUpd(); redraw();
    toast('새 관 · D' + Math.round(mm) + ' · ' + (L ? L.len.toFixed(1) : '?') + ' m');
  }
  $('npGo').addEventListener('click', npGo);
  ['npDia', 'npName'].forEach((id) => $(id).addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); npGo(); } else if (e.key === 'Escape') { e.preventDefault(); dlgClose(); } }));
  // 관 지우기 (확인 뒤) — 자리는 남기고 빈 줄 ({pts: []}) · 되돌리기 = 알림 단추 · 관로 고치기의 Ctrl+Z
  function pipeDel(src) {
    const Pp = window.RV_PIPES, raw = Pp && Array.isArray(Pp.lines) ? Pp.lines[src] : null;
    if (!raw) return;
    const before = JSON.stringify(raw);
    Pp.lines[src] = { pts: [] };
    const after = JSON.stringify(Pp.lines[src]);
    if (ED.sel === src) { ED.sel = -1; ED.hi = -1; ED.hov = -1; }
    if (PC.src === src) cardHide();
    shCancel();
    edPut(src);
    edCommit(src, before, after);
    infoUpd();
    toast('관 지움', () => {
      if (JSON.stringify(Pp.lines[src]) !== after) return;
      Pp.lines[src] = JSON.parse(before);
      edPut(src); edCommit(src, after, before); infoUpd();
      toast('되돌림');
    }, '되돌리기');
  }

  // ---------------------------------------------------------------- 원본 사진 (프로그램 안에서만): photos_at(E, N, H) → 좋은 순서 사진 · photo_view → 그림 + 그 자리
  const PH = { list: [], i: 0, zoom: false, at: null, seq: 0 };
  const phBox = $('phBox'), phImg = $('phImg'), phMark = $('phMark');
  function phMsg(t) { $('phMsg').textContent = t || ''; }
  function phClose() { phBox.hidden = true; PH.at = null; PH.seq++; redraw(); }
  function phOpen(E, N, Z, w) {
    PH.at = w; PH.list = []; PH.i = 0; PH.zoom = false; PH.seq++;
    phBox.hidden = false; phImg.removeAttribute('src'); phMark.hidden = true;
    $('phName').textContent = ''; $('phNum').textContent = ''; phBox.dataset.i = '';
    setOn($('phZoom'), false);
    phMsg('사진 찾는 중…'); redraw();
    const seq = PH.seq;
    call('photos_at', E, N, Z).then((r) => {
      if (seq !== PH.seq) return;
      if (!r) { phClose(); return; }
      if (!r.ok || !Array.isArray(r.photos) || !r.photos.length) { phMsg((r && r.msg) || '사진 없음'); return; }
      PH.list = r.photos;
      phShow();
    });
  }
  function phShow() {
    const q = PH.list[PH.i];
    if (!q) return;
    const seq = ++PH.seq;
    $('phName').textContent = q.name;
    $('phNum').textContent = (PH.i + 1) + ' / ' + PH.list.length;
    $('phPrev').disabled = PH.i <= 0; $('phNext').disabled = PH.i >= PH.list.length - 1;
    phMsg('불러오는 중…');
    call('photo_view', q.name, q.u, q.v, PH.zoom).then((v) => {
      if (seq !== PH.seq || phBox.hidden) return;
      if (!v || !v.ok) { phMsg((v && v.msg) || '사진 못 읽음'); return; }
      phImg.onload = () => {
        if (seq !== PH.seq) return;
        phMark.style.left = (v.x / v.w * 100) + '%'; phMark.style.top = (v.y / v.h * 100) + '%';
        phMark.hidden = false; phMsg('');
        Object.assign(phBox.dataset, { i: String(PH.i), name: q.name, x: String(v.x), y: String(v.y), w: String(v.w), h: String(v.h) });
      };
      phImg.src = v.url;
    });
  }
  function phStep(d) { const i = clamp(PH.i + d, 0, PH.list.length - 1); if (i !== PH.i) { PH.i = i; phShow(); } }
  $('phPrev').addEventListener('click', () => phStep(-1));
  $('phNext').addEventListener('click', () => phStep(1));
  $('phZoom').addEventListener('click', () => { PH.zoom = !PH.zoom; setOn($('phZoom'), PH.zoom); phShow(); });
  $('phX').addEventListener('click', phClose);

  // ---------------------------------------------------------------- 오른쪽 메뉴 (JACK 1126 · 오른쪽 눌렀다 떼기 · 누른 자리에 맞는 항목만 · 묶음 사이 구분선)
  const IC = {
    center: '<circle cx="12" cy="12" r="3"/><path d="M12 2v5M12 17v5M2 12h5M17 12h5"/>',
    copy: '<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2"/>',
    shot: '<path d="M4 8h3l2-3h6l2 3h3v11H4z"/><circle cx="12" cy="13" r="3.5"/>',
    home: '<path d="M3 11l9-7 9 7"/><path d="M5.5 9.5V20h13V9.5"/>',
    top: '<path d="M12 3 4 8l8 5 8-5zM4 12l8 5 8-5M4 16l8 5 8-5"/>',
    north: '<path d="M12 3l5 17-5-4-5 4z"/>',
    dist: '<path d="M4 18 20 6"/><circle cx="4" cy="18" r="1.8"/><circle cx="20" cy="6" r="1.8"/><circle cx="12" cy="12" r="1.8"/>',
    area: '<path d="M5 6l13 2 2 10-14 1z"/>',
    height: '<path d="M7 4v16M4 7l3-3 3 3M4 17l3 3 3-3"/><path d="M13 18l7-9"/>',
    prof: '<path d="M3 17l4-6 4 3 4-7 6 8"/><path d="M3 21h18"/>',
    pipe: '<path d="M3 8h9M3 16h9"/><path d="M12 8a2.2 4 0 0 1 0 8"/><path d="M18 8v8M14 12h8"/>',
    edit: '<path d="M4 20h4L19 9l-4-4L4 16z"/><path d="M13 7l4 4"/>',
    del: '<path d="M5 7h14M10 7V4h4v3M7 7l1 13h8l1-13"/>',
    shore: '<path d="M2 5h4M18 5h4"/><path d="M6 5v15M18 5v15"/><path d="M6 10h12"/>',
    info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v6M12 7.5v.5"/>',
    photo: '<rect x="3" y="5" width="18" height="14" rx="2"/><circle cx="9" cy="10" r="2"/><path d="M21 16l-5-5-8 8"/>',
    xray: '<path d="M2 9h20"/><path d="M4 15h16" stroke-dasharray="2.5 2.5"/><path d="M2 9c3-4.5 17-4.5 20 0" opacity=".5"/>',
    eye: '<path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>',
    folder: '<path d="M4 6h5l2 2h9v10a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z"/>',
    ck: '<path d="M12 3 4 21h16z"/><path d="M12 10v5"/>',
    crop: '<path d="M6 2v14a2 2 0 0 0 2 2h14"/><path d="M18 22V8a2 2 0 0 0-2-2H2"/>',
    dtm: '<path d="M2 19l6-9 4 5 3-4 7 8z"/><path d="M6.5 15h3.2M13.5 15.5h3.5" opacity=".55"/>',
    model: '<path d="M12 3l8 4.5v9L12 21l-8-4.5v-9z"/><path d="M4 7.5l8 4.5 8-4.5M12 12v9"/>',
    gs: '<ellipse cx="9.5" cy="10" rx="6" ry="3.4" transform="rotate(-28 9.5 10)"/><ellipse cx="16" cy="15" rx="4.2" ry="2.4" transform="rotate(18 16 15)" opacity=".7"/><circle cx="7" cy="17.5" r="2" opacity=".5"/>',
    ng: '<path d="M4 7l9-3 7 6-3 10-12-2z" stroke-dasharray="3 2.2"/><path d="M9.5 10.5l5 5M14.5 10.5l-5 5"/>',
  };
  const ctxEl = $('ctxMenu');
  const CTX = { open: false, items: [], at: null };
  function ctxClose() {
    if (!CTX.open) return;
    CTX.open = false; CTX.items = []; CTX.at = null;
    ctxEl.hidden = true; ctxEl.textContent = '';
  }
  function ctxPlace(cx, cy) {
    const mw = ctxEl.offsetWidth, mh = Math.min(ctxEl.scrollHeight, innerHeight - 16);
    ctxEl.style.maxHeight = (innerHeight - 16) + 'px';
    let l = cx + 2, t = cy + 2;
    if (l + mw > innerWidth - 8) l = cx - mw - 2;
    if (t + mh > innerHeight - 8) t = Math.max(8, innerHeight - 8 - mh);
    ctxEl.style.left = Math.max(8, Math.min(l, innerWidth - mw - 8)) + 'px';
    ctxEl.style.top = Math.max(8, t) + 'px';
  }
  function viewHome() {
    if (!mesh) return;
    const mn = mesh.meta.min, mx = mesh.meta.max;
    V.tgt = [(mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, mn[2] + (mx[2] - mn[2]) * 0.3];
    V.d = Math.hypot(mx[0] - mn[0], mx[1] - mn[1]) * 0.85; V.az = 25; V.el = 38;
    setOn($('bTop'), false); redraw();
  }
  function copyXYZ(E, N, Z) {
    const t = E.toFixed(3) + ', ' + N.toFixed(3) + ', ' + Z.toFixed(3);
    const ok = () => toast('복사됨 · X ' + E.toFixed(3) + ' · Y ' + N.toFixed(3) + ' · Z ' + Z.toFixed(3) + ' · ' + (P.crs || 'EPSG:5186'));
    if (HOST()) hostCall('copy_text', t).then((r) => (r && r.ok !== false ? ok() : copyText(t)), () => copyText(t));
    else copyText(t);
  }
  function shotSave() {
    if (dirty) { dirty = false; draw(); }
    const c = document.createElement('canvas');
    c.width = cv.width; c.height = cv.height;
    const g = c.getContext('2d');
    g.fillStyle = '#1b1f26'; g.fillRect(0, 0, c.width, c.height);
    g.drawImage(cv, 0, 0); g.drawImage(ovc, 0, 0);
    const url = c.toDataURL('image/png'), d = new Date(), z2 = (v) => String(v).padStart(2, '0');
    const name = String(P.title || '3D').replace(/[\\/:*?"<>|]+/g, '_') + '_' + d.getFullYear() + z2(d.getMonth() + 1) + z2(d.getDate()) + '_' + z2(d.getHours()) + z2(d.getMinutes()) + '.png';
    if (HOST()) {
      call('save_image', { name: name, data: url }).then((r) => {
        if (r && r.ok) toast('저장 · ' + r.path, () => hostCall('copy_text', String(r.path)).then(() => toast('복사됨'), () => copyText(String(r.path))));
        else if (r) toast(r.msg || '저장 안 됨');
      });
    } else {
      const a = document.createElement('a');
      a.href = url; a.download = name; document.body.appendChild(a); a.click(); a.remove();
      toast('내려받기 · ' + name);
    }
  }
  // 검사점 찍기: 파일이 열려 있으면 지금 고른 검사점 (없으면 10 m 안 가까운 것) 을 이 자리로 · 없으면 파일 고르기부터 (고른 뒤 가까운 검사점)
  async function ckHere(w) {
    const fresh = !CK.pts.length;
    if (fresh && !(await ckLoad())) return;
    ckShow();
    if (fresh || CK.active < 0) {
      let bi = -1, bd = 10;
      CK.pts.forEach((p, i) => { const d = Math.hypot(p.E - ORG[0] - w[0], p.N - ORG[1] - w[1]); if (d < bd) { bd = d; bi = i; } });
      if (bi < 0) { toast('10 m 안 검사점 없음 · 점을 고르고 모델에서 누르기'); return; }
      CK.active = bi;
    }
    const nm = CK.pts[CK.active].name;
    ckMeasure({ p: w });
    toast('검사점 «' + nm + '» 찍음');
  }
  function ctxOpen(cx, cy, x, y) {
    ctxClose();
    if (!mesh || !dlgBack.hidden) return;
    vwNow();
    const h = pickAt(x, y), w = h ? h.p.slice() : null, host = !!HOST();
    const E = w ? w[0] + ORG[0] : 0, N = w ? w[1] + ORG[1] : 0, Z = w ? w[2] + H0 : 0;
    const src = S.pipes && lines.length ? lineAt('v', x, y) : -1, L = src >= 0 ? lines.find((q) => q.src === src) : null, pk = L ? pickOf('v', L, x, y) : null;
    const G = [[], [], [], []];                                          // 보기 · 재기 · 관로 · 기타
    const CLR = new Set(['pInfo', 'pEdit', 'pShore', 'pDel', 'photo', 'ck', 'center']);    // 재기 · 잘라내기를 끝내고 할 것
    const wrap = (id, fn) => (fn && CLR.has(id) ? (...q) => { msClear(); return fn(...q); } : fn);
    const it = (g, id, ic, t, fn, o) => {
      const e = Object.assign({ id: id, ic: ic, t: t, fn: wrap(id, fn) }, o || {});
      if (e.sub && CLR.has(id)) e.sub = e.sub.map((q) => [q[0], q[1], wrap(id, q[2])]);
      G[g].push(e);
    };
    if (w) {
      it(0, 'center', 'center', '여기 가운데로', () => { V.tgt = w.slice(); redraw(); });
      it(0, 'copy', 'copy', '좌표 · 표고 복사', () => copyXYZ(E, N, Z));
    }
    it(0, 'shot', 'shot', '화면 그림 저장', shotSave);
    it(0, 'home', 'home', '처음 시점', viewHome);
    it(0, 'top', 'top', V.el > 80 ? '비스듬히 보기' : '위에서 보기', () => $('bTop').click());
    it(0, 'north', 'north', '북쪽 보기', () => { V.az = 0; redraw(); });
    if (DEM && DTP) it(0, 'dtm', DT.on ? 'model' : 'dtm', DT.on ? '모델 보기' : '맨땅 보기', () => dtSet(!DT.on));
    if (GS.ok && GS.prog && mesh.parts.length) it(0, 'gs', GS.on ? 'model' : 'gs', GS.on ? '모델 보기' : '가우시안 보기', () => gsSet(!GS.on));
    else if (GS.ok && GS.prog && DT.on) it(0, 'gs', 'gs', '가우시안 보기', () => gsSet(true));
    it(1, 'mDist', 'dist', '거리 재기', () => msStart('dist', w));
    it(1, 'mArea', 'area', '면적 재기', () => msStart('area', w));
    it(1, 'mHeight', 'height', '높이 차 (두 점)', () => msStart('height', w));
    it(1, 'mProf', 'prof', '지형 단면 (두 점)', () => msStart('prof', w));
    if (L && pk) {
      it(2, 'pInfo', 'info', '관 정보 · 그 자리 단면', () => { cardShow(L, pk, [x, y]); secShow(true, { src: L.src, k: pk.k, t: pk.t }); });
      it(2, 'pEdit', 'edit', '이 관 고치기', () => { cardHide(); edMode(true); ED.sel = L.src; ED.hi = -1; ED.hov = -1; redraw(); });
      it(2, 'pShore', 'shore', '이 관에 가시설 넣기', null, { sub: SHORE_T.map((t) => [t, { panel: '간이흙막이', hpile: '토류벽', sheet: '시트파일' }[t], () => shoreFrom(L, pk, t)]) });
      it(2, 'pDel', 'del', '이 관 지우기', () => toast('«' + (L.base || L.label || dTxt(L.dia)) + '» 지우기', () => pipeDel(L.src), '지우기'), { warn: true });
    } else if (w) it(2, 'pNew', 'pipe', '여기서 새 관 그리기', () => msStart('pipe', w));
    if (w && host) it(3, 'photo', 'photo', '이 자리 원본 사진 보기', () => phOpen(E, N, Z, w));
    it(3, 'xray', 'xray', xray ? '투시 끄기' : '투시 켜기', () => $('bXray').click());
    if (lines.length) it(3, 'pipes', 'eye', S.pipes ? '관로 숨기기' : '관로 보이기', () => setPipes(!S.pipes));
    if (host) it(3, 'files', 'folder', '결과물 폴더', () => call('open_results'));
    if (w && (host || CK.pts.length)) it(3, 'ck', 'ck', CK.pts.length && CK.active >= 0 ? '검사점 «' + CK.pts[CK.active].name + '» 여기로' : '검사점 찍기', () => ckHere(w));
    if (host) it(3, 'crop', 'crop', '여기서부터 잘라내기', () => crStart(w));
    if (w && ngHost()) it(3, 'ngHere', 'ng', '여기 땅 아님 지정', () => ngStart(w));
    const ngi = w && ngShown() ? ngAt(w) : -1;
    if (ngi >= 0) it(3, 'ngDel', 'del', '이 땅 아님 지우기', () => ngAsk(ngi), { warn: true });
    if (w) {
      const hd = document.createElement('div');
      hd.className = 'cHd'; hd.title = (P.crs || 'EPSG:5186') + ' · X = 동 · Y = 북 · Z = 표고 (' + (P.height || 'EGM2008') + ')';
      hd.textContent = 'X ' + E.toFixed(2) + ' · Y ' + N.toFixed(2) + ' · Z ' + Z.toFixed(2);
      ctxEl.appendChild(hd);
    }
    G.filter((g) => g.length).forEach((g, gi) => {
      if (gi) { const s = document.createElement('div'); s.className = 'sep'; ctxEl.appendChild(s); }
      for (const r of g) {
        const b = document.createElement('button');
        b.type = 'button'; b.className = 'ci' + (r.warn ? ' warn' : ''); b.dataset.id = r.id; b.setAttribute('role', 'menuitem');
        b.innerHTML = '<svg viewBox="0 0 24 24">' + IC[r.ic] + '</svg><span></span>';
        b.querySelector('span').textContent = r.t;
        ctxEl.appendChild(b);
        CTX.items.push(r.id);
        if (r.sub) {
          const sub = document.createElement('div');
          sub.className = 'sub'; sub.hidden = true; sub.dataset.of = r.id;
          r.sub.forEach(([sid, st, fn]) => {
            const sb = document.createElement('button');
            sb.type = 'button'; sb.dataset.id = r.id + ':' + sid; sb.textContent = st;
            sb.addEventListener('click', (e) => { e.stopPropagation(); ctxClose(); fn(); });
            sub.appendChild(sb);
          });
          ctxEl.appendChild(sub);
          b.addEventListener('click', (e) => { e.stopPropagation(); sub.hidden = !sub.hidden; ctxPlace(cx, cy); });
        } else b.addEventListener('click', (e) => { e.stopPropagation(); ctxClose(); r.fn(); });
      }
    });
    CTX.open = true; CTX.at = w ? { x: x, y: y, E: E, N: N, Z: Z, src: L ? L.src : -1 } : { x: x, y: y, src: L ? L.src : -1 };
    ctxEl.hidden = false;
    ctxPlace(cx, cy);
  }
  function shoreFrom(L, pk, t) {
    if ($('side').hidden) { $('side').hidden = false; setOn($('tgSide'), true); }
    if (shBox.hidden) shOpen(true);
    shArm(t);
    const s = round(L.cum[pk.k] + (L.cum[pk.k + 1] - L.cum[pk.k]) * pk.t, 2), p = pickPoint(L, pk);
    SH.a = { src: L.src, s: s, e: p[0], n: p[1], g: p[3] };
    shUI(); redraw();
  }
  window.addEventListener('pointerdown', (e) => { if (CTX.open && !ctxEl.contains(e.target)) ctxClose(); }, true);
  window.addEventListener('blur', ctxClose);
  window.addEventListener('resize', ctxClose);

  // ---------------------------------------------------------------- 조작 (왼쪽 끌기 = 돌기 · 오른쪽 / Shift = 옮기기 · 위에서 보기면 왼쪽 = 옮기기 · 휠 = 마우스 쪽으로)
  let dn = null;
  const evXY = (e) => { const r = cv.getBoundingClientRect(); return [e.clientX - r.left, e.clientY - r.top]; };
  function tap(x, y) {
    vwNow();
    if (MS.kind && msTap(x, y)) return;                                  // 재기 · 새 관 · 잘라내기 점 찍기
    if (CK.active >= 0 && $('ck').style.display !== 'none') {
      if (DT.on) { dtSet(false); toast('검사점은 3D 모델 위에서 · 모델 보기로 바꿈 · 다시 누르기'); return; }   // 맨땅 = 다듬고 메운 땅 (1126 검토)
      const h = pickAt(x, y); if (h) { ckMeasure(h); return; }
    }
    if (SH.arm) { shTap('v', x, y); return; }
    if (ED.on) { edTap('v', x, y); return; }
    if (pipeTap('v', x, y)) return;
    cardHide();
    ngTap(x, y);                                                         // 지정된 땅 아님 안 → 지우기 묻기
  }
  function hover(x, y) {
    if (!mesh) return;
    vwNow();
    if (MS.kind && !MS.done) { MS.hov = msPoint(x, y); redraw(); return; }     // 재는 중: 마지막 점 → 마우스 (네모는 첫 구석 → 마우스)
    if (ED.on) { edHover('v', x, y); return; }
    cv.classList.toggle('point', S.pipes && lines.length > 0 && lineAt('v', x, y) >= 0);
  }
  cv.addEventListener('pointerdown', (e) => {
    if (e.button === 1) e.preventDefault();                              // 휠 누르기 = 자동 스크롤 막기
    ctxClose();
    const [x, y] = evXY(e);
    dn = { x: e.clientX, y: e.clientY, b: e.button, az: V.az, el: V.el, t: V.tgt.slice(), moved: false, edit: false, ms: false };
    if (e.button === 0 && MS.kind && mesh) { vwNow(); dn.ms = msDown(x, y); }
    if (dn.ms) { /* 잘라내기 네모 · 꼭짓점 끌기 */ } else if (e.button === 0 && ED.on && mesh) { vwNow(); edStart('v', x, y, e); dn.edit = !!ED.down; }
    cv.setPointerCapture(e.pointerId);
  });
  cv.addEventListener('pointermove', (e) => {
    if (!dn) { const [x, y] = evXY(e); hover(x, y); return; }
    const dx = e.clientX - dn.x, dy = e.clientY - dn.y;
    if (!dn.moved && dx * dx + dy * dy < 9) return;
    dn.moved = true; cv.classList.add('drag');
    if (dn.ms) { const [x, y] = evXY(e); msMove(x, y); return; }
    if (dn.edit) { edMove(dx, dy); return; }
    // JACK 1126: 오른쪽이 하던 일을 휠 버튼으로 · 오른쪽은 눌렀다 떼면 메뉴 (끌어도 아무것도 안 함)
    //   기본 = 왼쪽 돌리기 · 휠 옮기기 / 위에서 보기 = 왼쪽 옮기기 · 휠 돌리기 · Shift + 왼쪽 = 옮기기
    if (dn.b === 2) return;
    const top = V.el > 80, pan = top ? dn.b === 0 || e.shiftKey : dn.b === 1 || e.shiftKey;
    if (pan) {
      const a = V.az * Math.PI / 180, k = V.d * 2 * Math.tan(V.fov * Math.PI / 360) / cv.clientHeight;
      V.tgt = [dn.t[0] - (dx * Math.cos(a) + dy * Math.sin(a) * Math.sin(V.el * Math.PI / 180)) * k,
               dn.t[1] - (dx * Math.sin(a) - dy * Math.cos(a) * Math.sin(V.el * Math.PI / 180)) * k, dn.t[2]];
    } else { V.az = dn.az - dx * 0.3; V.el = Math.max(3, Math.min(89.5, dn.el + dy * 0.3)); }
    redraw();
  });
  cv.addEventListener('pointerup', (e) => {
    cv.classList.remove('drag');
    const d = dn; dn = null;
    if (!d) return;
    const [x, y] = evXY(e);
    if (d.ms) { vwNow(); msUp(x, y, d.moved); return; }
    if (d.edit) { edEnd(d.moved); if (!d.moved) tap(x, y); return; }
    if (!d.moved && d.b === 0) tap(x, y);
    if (!d.moved && d.b === 2) ctxOpen(e.clientX, e.clientY, x, y);   // 오른쪽 눌렀다 떼기 = 메뉴
  });
  cv.addEventListener('pointercancel', () => { cv.classList.remove('drag'); if (dn && dn.edit) edEnd(dn.moved); if (dn && dn.ms) { MS.drag = -1; MS.dg = null; MS.rNew = false; } dn = null; });
  cv.addEventListener('contextmenu', (e) => e.preventDefault());
  cv.addEventListener('wheel', (e) => {
    e.preventDefault();
    ctxClose();
    const k = e.deltaY > 0 ? 1.15 : 1 / 1.15;
    const [x, y] = evXY(e), h = k < 1 ? pickAt(x, y) : null;
    if (h) V.tgt = V.tgt.map((v, i) => v + (h.p[i] - v) * (1 - k));
    V.d = Math.max(2, V.d * k); redraw();
  }, { passive: false });
  cv.addEventListener('dblclick', (e) => {
    const [x, y] = evXY(e);
    vwNow();
    if (MS.kind) { if (!MS.done && !(MS.kind === 'crop' && MS.rect)) msFinish(); return; }   // 두 번 누르기 = 끝 (닫기)
    if (ED.on) { edInsert('v', x, y); return; }
    if (S.pipes && !SH.arm && lines.length) {                          // 관 두 번 누르기 → 관로 고치기 · 그 관 고름 (360 과 같음)
      const src = lineAt('v', x, y);
      if (src >= 0) { cardHide(); edMode(true); ED.sel = src; redraw(); return; }
    }
    const h = pickAt(x, y); if (h) { V.tgt = h.p; redraw(); }
  });
  window.addEventListener('keydown', (e) => {
    const tg = (e.target && e.target.tagName) || '';
    if (/^(INPUT|SELECT|TEXTAREA)$/.test(tg)) return;
    if (CTX.open && e.key === 'Escape') { ctxClose(); return; }
    if (e.key === 'Escape' && !dlgBack.hidden) { dlgClose(); return; }
    if (!phBox.hidden && (e.key === 'Escape' || e.key === 'ArrowLeft' || e.key === 'ArrowRight')) {
      if (e.key === 'Escape') phClose(); else phStep(e.key === 'ArrowLeft' ? -1 : 1);
      e.preventDefault(); return;
    }
    if (MS.kind && (!MS.done || MS.kind === 'crop')) {                   // 재기 · 새 관 · 잘라내기: Esc = 취소 · Enter = 끝 · Backspace = 마지막 점 빼기
      if (e.key === 'Escape') { msClear(); return; }
      if (e.key === 'Enter') { e.preventDefault(); if (MS.kind === 'crop' && MS.done) crApply(); else if (MS.kind === 'ng' && !MS.pts.length) ngApply(); else msFinish(); return; }
      if (e.key === 'Backspace' && !MS.done) { e.preventDefault(); msBack(); return; }
    }
    if (e.key === 'Escape' && (SH.arm || SH.a)) { shCancel(); return; }
    if (ED.on && edKey(e)) { e.preventDefault(); return; }
    if (e.key === 'Escape') { if (!secDlg.hidden) secShow(false); else ugEsc(); }
  });
  window.addEventListener('resize', redraw);
  $('bXray').addEventListener('click', () => { xray = !xray; setOn($('bXray'), xray); redraw(); });
  $('bTop').addEventListener('click', () => { V.el = V.el > 80 ? 38 : 89.5; setOn($('bTop'), V.el > 80); redraw(); });
  $('bDtm').hidden = !DEM || !DTP;                                       // dem.js 없으면 맨땅 단추 숨김
  $('bDtm').addEventListener('click', () => dtSet(!DT.on));
  $('bGs').hidden = !GS.ok || !GS.prog;                                  // 결과물에 가우시안이 있을 때만
  $('bGs').addEventListener('click', () => gsSet(!GS.on));

  // ---------------------------------------------------------------- 시작
  function infoUpd() {
    if (!mesh) return;
    const gn = P.gs && P.gs.web ? ' · 가우시안 ' + Number(P.gs.web).toLocaleString() : '';
    $('info').textContent = (mesh.parts.length ? '세모 ' + mesh.meta.tris.toLocaleString() : '3D 모델 없음') + gn + ' · 사진 ' + (P.registered || '?') + '/' + (P.photos || '?') + '장 · RTK 어긋남 ' +
      (P.rtk_rms != null ? P.rtk_rms.toFixed(2) + ' m' : '-') + ' · ' + (P.crs || 'EPSG:5186') + ' + ' + (P.height || 'EGM2008') + (lines.length ? ' · 관로 ' + lines.length + '줄' : '');
  }
  obSet(window.RV_OBST);
  if (!TP) toast('지하 시설물 그리기 안 됨 (그래픽)');
  try { if (DEM && sessionStorage.getItem(DT_KEY) === '1') dtSet(true); } catch (e) { /* 저장소 못 씀 = 모델 보기 */ }
  try { if (GS.ok && !DT.on && sessionStorage.getItem(GS_KEY) === '1') gsSet(true); } catch (e) { /* 저장소 못 씀 = 모델 보기 */ }
  requestAnimationFrame(loop);
  loadMesh().then(() => {
    $('load').style.display = 'none';
    infoUpd();
    redraw();
  }).catch((e) => { $('load').textContent = '못 불러옴 · ' + e; window.__err = String(e); });
  window.__map3d = {
    V, redraw, pickAt, S, ED, TR, SH, SHOW, CK, DC, LOD, PB, HM, hmMax,
    tr: () => TR, lines: () => lines, obst: () => obst, view: vwNow, camc, toScr, gAt, zcAt, groundAt, demAt: demP,
    fit: () => { const f = { joints: [], bends: [] }; tubeM.forEach((m) => { if (m.fit && m.n) { f.joints.push(...m.fit.joints); f.bends.push(...m.fit.bends); } }); return f; },
    hnd: () => S.hnd, vtx: () => S.vtx, cf: () => CF.list, setMode, setPipes, trSet, secShow, edMode, shArm, mesh: () => mesh, T: () => [TX, TY, TZ],
    xray: (on) => { xray = !!on; setOn($('bXray'), xray); redraw(); },
    rebuild: () => { lines = buildLines(window.RV_PIPES); pipeGen++; redraw(); },
    ctx: () => ({ open: CTX.open, items: CTX.items.slice(), at: CTX.at }), MS, PH, TB, NP, profGet,
    dtSet, DT, CT, NG, ngGet, ngShown, gsSet,
    gs: () => ({ ok: GS.ok, on: GS.on, ready: GS.ready, n: GS.n, sorted: GS.sorted, sorts: GS.sorts, err: GS.err, worker: !!(GS.wk && GS.wk instanceof Worker) }),
    dt: () => ({ on: DT.on, parts: DT.parts ? DT.parts.length : 0, tris: DT.tris, mt: DT.mt, el: DT.el, ci: CT.minor, every: CT.every, keys: DT.parts ? DT.parts.map((p) => p.key) : [] }),
  };
})();
