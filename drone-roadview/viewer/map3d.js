/* 드론 로드뷰 — 3D 매핑 보기 (JACK 1125 · Terra 대체)
 * 자료: data/mapping.js (RV_MAPPING · 원점 · 범위) · data/mesh.json + mesh.bin (세모마다 꼭짓점 셋: 위치 float32×3 · 질감 float32×2, 원점 기준 m)
 *       data/tex.jpg · data/pipes.js (RV_PIPES · 보기 화면과 같은 꼴, 땅 높이 g = DSM)
 * 좌표: x = 동, y = 북, z = 표고 (EPSG:5186 + EGM2008, 원점 뺌).  관 중심 = z(있으면) 또는 땅 − 평균매설깊이 − 관경/2.
 */
(function () {
  'use strict';
  const $ = (id) => document.getElementById(id);
  const P = window.RV_MAPPING || {}, PIPES = window.RV_PIPES || null;
  const host = () => (window.pywebview && window.pywebview.api) || null;
  function toast(t) { const e = $('toast'); e.textContent = t; e.classList.add('show'); clearTimeout(e._t); e._t = setTimeout(() => e.classList.remove('show'), 2600); }
  async function call(n, ...a) { const h = host(); if (!h || !h[n]) { toast('프로그램 안에서만'); return null; } try { return await h[n](...a); } catch (e) { toast('오류 · ' + String(e.message || e).slice(0, 100)); return null; } }
  $('title').textContent = P.title || '3D 매핑';
  const cv = $('c'), gl = cv.getContext('webgl2', { antialias: true, preserveDrawingBuffer: true });
  if (!gl) { $('load').textContent = 'WebGL2 없음'; return; }
  const fext = gl.getExtension('EXT_color_buffer_float');

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
  const MESH = prog('in vec3 aP; in vec2 aT; uniform mat4 M; out vec2 t; void main(){ t = aT; gl_Position = M * vec4(aP, 1.0); }',
    'in vec2 t; uniform sampler2D tex; out vec4 o; void main(){ o = vec4(texture(tex, t).rgb, 1.0); }');
  const PIPE = prog('in vec3 aP; in vec3 aN; uniform mat4 M; out vec3 n; void main(){ n = aN; gl_Position = M * vec4(aP, 1.0); }',
    'in vec3 n; uniform vec3 col; uniform float alpha; out vec4 o; void main(){ float l = 0.45 + 0.55 * max(0.0, dot(normalize(n), normalize(vec3(0.3, -0.4, 0.85)))); o = vec4(col * l, alpha); }');
  const FLAT = prog('in vec3 aP; uniform mat4 M; void main(){ gl_Position = M * vec4(aP, 1.0); }',
    'uniform vec4 col; out vec4 o; void main(){ o = col; }');
  const PICK = prog('in vec3 aP; uniform mat4 M; out vec3 w; void main(){ w = aP; gl_Position = M * vec4(aP, 1.0); }',
    'in vec3 w; uniform float id; out vec4 o; void main(){ o = vec4(w, id); }');

  // ---------------------------------------------------------------- 카메라 (궤도 · z 위)
  const V = { az: 25, el: 38, d: 200, tgt: [0, 0, 0], fov: 50 };
  const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]], dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
  const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
  const norm = (a) => { const l = Math.hypot(a[0], a[1], a[2]) || 1; return [a[0] / l, a[1] / l, a[2] / l]; };
  function mul(A, B) { const C = new Array(16).fill(0); for (let i = 0; i < 4; i++) for (let j = 0; j < 4; j++) for (let k = 0; k < 4; k++) C[j * 4 + i] += A[k * 4 + i] * B[j * 4 + k]; return C; }
  function eye() { const a = V.az * Math.PI / 180, e = V.el * Math.PI / 180; return [V.tgt[0] + V.d * Math.cos(e) * Math.sin(a), V.tgt[1] - V.d * Math.cos(e) * Math.cos(a), V.tgt[2] + V.d * Math.sin(e)]; }
  function matrix(W, H) {
    const E = eye(), f = norm(sub(V.tgt, E)), r = norm(cross(f, [0, 0, 1])), u = cross(r, f);
    const view = [r[0], u[0], -f[0], 0, r[1], u[1], -f[1], 0, r[2], u[2], -f[2], 0, -dot(r, E), -dot(u, E), dot(f, E), 1];
    const n = Math.max(0.05, V.d * 0.005), fa = Math.max(2000, V.d * 30), fy = 1 / Math.tan(V.fov * Math.PI / 360), as = W / H;
    return mul([fy / as, 0, 0, 0, 0, fy, 0, 0, 0, 0, (fa + n) / (n - fa), -1, 0, 0, 2 * fa * n / (n - fa), 0], view);
  }

  // ---------------------------------------------------------------- 자료
  let mesh = null, tex = null, pipeVao = null, pipeN = 0, bandVao = null, bandN = 0, pipeInfo = [];
  function buf(attrs) {
    const vao = gl.createVertexArray(); gl.bindVertexArray(vao);
    for (const [loc, data, size, stride, off] of attrs) {
      const b = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, b); gl.bufferData(gl.ARRAY_BUFFER, data, gl.STATIC_DRAW);
      gl.enableVertexAttribArray(loc); gl.vertexAttribPointer(loc, size, gl.FLOAT, false, stride || 0, off || 0);
    }
    gl.bindVertexArray(null); return vao;
  }
  async function texOf(url) {
    const img = new Image(); img.src = url; await img.decode();
    const mx = gl.getParameter(gl.MAX_TEXTURE_SIZE);
    let src = img;
    if (img.width > mx || img.height > mx) {                // 그래픽 한도보다 크면 줄여서
      const k = mx / Math.max(img.width, img.height), c2 = document.createElement('canvas');
      c2.width = Math.floor(img.width * k); c2.height = Math.floor(img.height * k); c2.getContext('2d').drawImage(img, 0, 0, c2.width, c2.height); src = c2;
    }
    const t = gl.createTexture(); gl.bindTexture(gl.TEXTURE_2D, t);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGB, gl.RGB, gl.UNSIGNED_BYTE, src); gl.generateMipmap(gl.TEXTURE_2D);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    return { t, size: [img.width, img.height], up: [src.width, src.height] };
  }
  async function loadMesh() {
    const meta = await (await fetch('data/mesh.json')).json();
    // 조각(tiles) — 조각마다 질감 (1.0.15 · 그래픽 한도 16384 안) · 옛 자료는 한 덩어리(mesh.bin · tex.jpg)
    const list = meta.tiles || [{ bin: 'mesh.bin', tex: 'tex.jpg', verts: meta.verts }];
    const parts = [];
    for (let i = 0; i < list.length; i++) {
      $('load').textContent = '불러오는 중… ' + (i + 1) + ' / ' + list.length;
      const L = list[i], b = await (await fetch('data/' + L.bin)).arrayBuffer(), n = L.verts;
      const tx = await texOf('data/' + L.tex);
      parts.push({ n, vao: buf([[0, new Float32Array(b, 0, n * 3), 3], [1, new Float32Array(b, n * 12, n * 2), 2]]), tex: tx.t, size: tx.size, up: tx.up });
      dirty = true;
      if (i === 0) {
        const mn = meta.min, mxp = meta.max;
        V.tgt = [(mn[0] + mxp[0]) / 2, (mn[1] + mxp[1]) / 2, mn[2] + (mxp[2] - mn[2]) * 0.3];
        V.d = Math.hypot(mxp[0] - mn[0], mxp[1] - mn[1]) * 0.85;
        mesh = { meta, parts };
      }
    }
    mesh = { meta, parts };
    window.__mesh = { tris: meta.tris, tiles: parts.length, tex: parts.map((p) => p.size), up: parts.map((p) => p.up) };
  }
  const MAT_RGB = { ci: [0.35, 0.35, 0.37], steel: [0.0, 0.85, 1.0], plastic: [0.93, 0.93, 0.91] };
  function buildPipes() {
    if (!PIPES || !PIPES.lines || !PIPES.lines.length || !mesh) return;
    const cover = Number(P.cover != null ? P.cover : 1.2);
    const o = PIPES.origin || { E: P.origin[0], N: P.origin[1] };
    const dx = o.E - P.origin[0], dy = o.N - P.origin[1], h0 = P.origin[2] || 0;
    const tv = [], tn = [], bv = [];
    pipeInfo = [];
    PIPES.lines.forEach((L, li) => {
      const D = Math.max(0.05, Number(L.dia) || 0.2), R = D / 2, n = L.pts.length;
      const C = L.pts.map((p, i) => {
        const g = L.g && L.g[i] != null ? L.g[i] : null;
        const z = p[2] != null ? p[2] : (g != null ? g - cover - R : NaN);
        return [p[0] + dx, p[1] + dy, z - h0, g != null ? g - h0 : NaN];
      });
      pipeInfo.push({ L, C });
      const SIDES = 10;
      for (let i = 0; i < n - 1; i++) {
        const a = C[i], b = C[i + 1];
        if (!Number.isFinite(a[2]) || !Number.isFinite(b[2])) continue;
        const t = norm(sub(b, a)), up = Math.abs(t[2]) > 0.95 ? [1, 0, 0] : [0, 0, 1];
        const s1 = norm(cross(t, up)), s2 = cross(s1, t);
        for (let k = 0; k < SIDES; k++) {
          const q0 = 2 * Math.PI * k / SIDES, q1 = 2 * Math.PI * (k + 1) / SIDES;
          const n0 = [s1[0] * Math.cos(q0) + s2[0] * Math.sin(q0), s1[1] * Math.cos(q0) + s2[1] * Math.sin(q0), s1[2] * Math.cos(q0) + s2[2] * Math.sin(q0)];
          const n1 = [s1[0] * Math.cos(q1) + s2[0] * Math.sin(q1), s1[1] * Math.cos(q1) + s2[1] * Math.sin(q1), s1[2] * Math.cos(q1) + s2[2] * Math.sin(q1)];
          const A0 = [a[0] + R * n0[0], a[1] + R * n0[1], a[2] + R * n0[2]], A1 = [a[0] + R * n1[0], a[1] + R * n1[1], a[2] + R * n1[2]];
          const B0 = [b[0] + R * n0[0], b[1] + R * n0[1], b[2] + R * n0[2]], B1 = [b[0] + R * n1[0], b[1] + R * n1[1], b[2] + R * n1[2]];
          tv.push(...A0, ...B0, ...B1, ...A0, ...B1, ...A1); tn.push(...n0, ...n0, ...n1, ...n0, ...n1, ...n1);
        }
        // 땅 위 띠 (관경 폭 · 땅 + 3 cm)
        if (Number.isFinite(a[3]) && Number.isFinite(b[3])) {
          const w = Math.max(0.15, D) / 2, s = norm([-(b[1] - a[1]), b[0] - a[0], 0]);
          const p0 = [a[0] + w * s[0], a[1] + w * s[1], a[3] + 0.03], p1 = [a[0] - w * s[0], a[1] - w * s[1], a[3] + 0.03];
          const p2 = [b[0] + w * s[0], b[1] + w * s[1], b[3] + 0.03], p3 = [b[0] - w * s[0], b[1] - w * s[1], b[3] + 0.03];
          bv.push(...p0, ...p2, ...p3, ...p0, ...p3, ...p1);
        }
      }
    });
    pipeN = tv.length / 3; bandN = bv.length / 3;
    if (pipeN) pipeVao = buf([[0, new Float32Array(tv), 3], [2, new Float32Array(tn), 3]]);
    if (bandN) bandVao = buf([[0, new Float32Array(bv), 3]]);
    window.__pipes = { lines: PIPES.lines.length, tris: pipeN / 3 };
  }

  // ---------------------------------------------------------------- 그리기
  let xray = true, dirty = true;
  function draw() {
    const dpr = Math.min(2, devicePixelRatio || 1), W = Math.round(cv.clientWidth * dpr), H = Math.round(cv.clientHeight * dpr);
    if (cv.width !== W || cv.height !== H) { cv.width = W; cv.height = H; }
    gl.viewport(0, 0, W, H);
    gl.clearColor(0.106, 0.122, 0.149, 1); gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    if (!mesh) return;
    const M = matrix(W, H);
    gl.enable(gl.DEPTH_TEST); gl.disable(gl.BLEND);
    gl.useProgram(MESH.p); gl.uniformMatrix4fv(MESH.u.M, false, M);
    gl.activeTexture(gl.TEXTURE0); gl.uniform1i(MESH.u.tex, 0);
    for (const p of mesh.parts) { gl.bindTexture(gl.TEXTURE_2D, p.tex); gl.bindVertexArray(p.vao); gl.drawArrays(gl.TRIANGLES, 0, p.n); }
    drawChecks(M);
    if (bandVao) {                                         // 땅 위 관 띠
      gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
      gl.enable(gl.POLYGON_OFFSET_FILL); gl.polygonOffset(-2, -2);
      gl.useProgram(FLAT.p); gl.uniformMatrix4fv(FLAT.u.M, false, M); gl.uniform4f(FLAT.u.col, 0.13, 0.83, 0.93, 0.55);
      gl.bindVertexArray(bandVao); gl.drawArrays(gl.TRIANGLES, 0, bandN);
      gl.disable(gl.POLYGON_OFFSET_FILL);
    }
    if (pipeVao) {
      gl.useProgram(PIPE.p); gl.uniformMatrix4fv(PIPE.u.M, false, M); gl.uniform3f(PIPE.u.col, 0.13, 0.83, 0.93);
      gl.bindVertexArray(pipeVao);
      gl.disable(gl.BLEND); gl.uniform1f(PIPE.u.alpha, 1.0); gl.drawArrays(gl.TRIANGLES, 0, pipeN);   // 드러난 곳
      if (xray) {                                           // 땅 너머 (옅게)
        gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
        gl.depthFunc(gl.GREATER); gl.depthMask(false); gl.uniform1f(PIPE.u.alpha, 0.55); gl.drawArrays(gl.TRIANGLES, 0, pipeN);
        gl.depthFunc(gl.LESS); gl.depthMask(true);
      }
    }
    gl.bindVertexArray(null);
    window.__drawn = (window.__drawn || 0) + 1;
  }
  function loop() { if (dirty) { dirty = false; draw(); } requestAnimationFrame(loop); }
  const redraw = () => { dirty = true; };

  // ---------------------------------------------------------------- 고르기 (화면 한 점 → 위치 · 관 번호)
  let pfb = null, ptex = null, pdep = null, pW = 0, pH = 0;
  function pickAt(x, y) {
    if (!mesh || !fext) return null;
    const dpr = Math.min(2, devicePixelRatio || 1), W = cv.width, H = cv.height;
    if (!pfb || pW !== W || pH !== H) {
      pfb = gl.createFramebuffer(); ptex = gl.createTexture(); pdep = gl.createRenderbuffer(); pW = W; pH = H;
      gl.bindTexture(gl.TEXTURE_2D, ptex); gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA32F, W, H, 0, gl.RGBA, gl.FLOAT, null);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
      gl.bindRenderbuffer(gl.RENDERBUFFER, pdep); gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH_COMPONENT24, W, H);
      gl.bindFramebuffer(gl.FRAMEBUFFER, pfb); gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, ptex, 0);
      gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, pdep);
    }
    gl.bindFramebuffer(gl.FRAMEBUFFER, pfb); gl.viewport(0, 0, W, H);
    gl.clearBufferfv(gl.COLOR, 0, [0, 0, 0, -1]); gl.clear(gl.DEPTH_BUFFER_BIT); gl.enable(gl.DEPTH_TEST); gl.disable(gl.BLEND);
    const M = matrix(W, H);
    gl.useProgram(PICK.p); gl.uniformMatrix4fv(PICK.u.M, false, M);
    gl.uniform1f(PICK.u.id, 0);
    for (const p of mesh.parts) { gl.bindVertexArray(p.vao); gl.drawArrays(gl.TRIANGLES, 0, p.n); }
    if (pipeVao) { gl.uniform1f(PICK.u.id, 1); gl.disable(gl.DEPTH_TEST); gl.bindVertexArray(pipeVao); gl.drawArrays(gl.TRIANGLES, 0, pipeN); gl.enable(gl.DEPTH_TEST); }
    const px = new Float32Array(4);
    gl.readPixels(Math.round(x * dpr), Math.round(H - y * dpr), 1, 1, gl.RGBA, gl.FLOAT, px);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null); gl.bindVertexArray(null);
    redraw();
    return px[3] < 0 ? null : { p: [px[0], px[1], px[2]], pipe: px[3] > 0.5 };
  }
  function pipeNear(p) {                                  // 누른 곳에서 가장 가까운 관 (평면)
    let best = null;
    pipeInfo.forEach(({ L, C }) => {
      for (let i = 0; i < C.length - 1; i++) {
        const a = C[i], b = C[i + 1], ab = [b[0] - a[0], b[1] - a[1]], l2 = ab[0] * ab[0] + ab[1] * ab[1] || 1;
        const u = Math.max(0, Math.min(1, ((p[0] - a[0]) * ab[0] + (p[1] - a[1]) * ab[1]) / l2));
        const q = [a[0] + u * ab[0], a[1] + u * ab[1]], d = Math.hypot(p[0] - q[0], p[1] - q[1]);
        if (!best || d < best.d) best = { d, L, top: a[2] + u * (b[2] - a[2]) + (Number(L.dia) || 0.2) / 2, g: a[3] + u * (b[3] - a[3]) };
      }
    });
    return best;
  }

  // ---------------------------------------------------------------- 검사점 (설계서 6.4 · 매핑에 쓰지 않은 독립 점)
  // pts = [{name, E, N, Z(측량 표고 | null), m: [E, N, Z] (3D 에서 찍은 곳) | null}] · 표시 = 팔면체 + 세로 막대 (모델 너머로도 보임)
  const CK = { pts: [], active: -1, vao: null, ranges: [] };
  const H0 = () => (P.origin && P.origin[2]) || 0;
  function ckRel(p) { return [p.E - P.origin[0], p.N - P.origin[1], (p.Z != null ? p.Z : H0()) - H0()]; }
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
    gl.useProgram(MESH.p);
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
  function ckMeasure(h) {                                 // 모델에서 누른 곳 = 찍은 자리
    const p = CK.pts[CK.active]; if (!p) return;
    p.m = [h.p[0] + P.origin[0], h.p[1] + P.origin[1], h.p[2] + H0()];
    const nx = CK.pts.findIndex((q, j) => j > CK.active && !q.m);
    if (nx >= 0) { CK.active = nx; const c = ckRel(CK.pts[nx]); V.tgt = c; }
    ckRender(); redraw();
  }
  $('bCheck').addEventListener('click', async () => {
    if ($('ck').style.display !== 'none') { $('ck').style.display = 'none'; redraw(); return; }
    if (!CK.pts.length) {
      const r = await call('pick_checkpoints'); if (!r) return;
      if (!r.ok) { toast(r.msg); return; }
      CK.pts = r.points.map((q) => ({ name: q.name, E: q.E, N: q.N, Z: q.Z, m: null })); CK.active = 0;
      ckBuild();
      if (r.note) toast(r.note);
    }
    $('ck').style.display = 'flex'; ckRender(); redraw();
  });
  $('ckClose').addEventListener('click', () => { $('ck').style.display = 'none'; CK.active = -1; redraw(); });
  $('ckSave').addEventListener('click', async () => {
    const q = await call('save_checks', CK.pts.map((p) => ({ name: p.name, E: p.E, N: p.N, Z: p.Z, mE: p.m ? p.m[0] : null, mN: p.m ? p.m[1] : null, mZ: p.m ? p.m[2] : null })));
    if (q && q.ok) toast('보고서 저장 · ' + q.msg); else if (q) toast(q.msg);
  });
  window.__ckApi = { set: (pts) => { CK.pts = pts.map((q) => ({ name: q.name, E: q.E, N: q.N, Z: q.Z, m: null })); CK.active = 0; ckBuild(); $('ck').style.display = 'flex'; ckRender(); redraw(); }, measure: (p) => ckMeasure({ p }), CK };

  // ---------------------------------------------------------------- 조작
  let dn = null;
  cv.addEventListener('pointerdown', (e) => { dn = { x: e.clientX, y: e.clientY, b: e.button, az: V.az, el: V.el, t: V.tgt.slice(), moved: false }; cv.setPointerCapture(e.pointerId); });
  cv.addEventListener('pointermove', (e) => {
    if (!dn) return;
    const dx = e.clientX - dn.x, dy = e.clientY - dn.y;
    if (!dn.moved && dx * dx + dy * dy < 9) return;
    dn.moved = true; cv.classList.add('drag');
    if (dn.b === 2 || e.shiftKey) {
      const a = V.az * Math.PI / 180, k = V.d * 2 * Math.tan(V.fov * Math.PI / 360) / cv.clientHeight;
      V.tgt = [dn.t[0] - (dx * Math.cos(a) + dy * Math.sin(a) * Math.sin(V.el * Math.PI / 180)) * k,
               dn.t[1] - (dx * Math.sin(a) - dy * Math.cos(a) * Math.sin(V.el * Math.PI / 180)) * k, dn.t[2]];
    } else { V.az = dn.az - dx * 0.3; V.el = Math.max(3, Math.min(89.5, dn.el + dy * 0.3)); }
    redraw();
  });
  cv.addEventListener('pointerup', (e) => {
    cv.classList.remove('drag');
    if (dn && !dn.moved && dn.b === 0) {
      const r = cv.getBoundingClientRect(), h = pickAt(e.clientX - r.left, e.clientY - r.top), box = $('pick');
      if (h && CK.active >= 0 && $('ck').style.display !== 'none') { ckMeasure(h); dn = null; return; }   // 검사점 찍기
      if (h && h.pipe && pipeInfo.length) {
        const q = pipeNear(h.p);
        if (q && q.d < 3) {
          box.innerHTML = '';
          const line = (t) => { const d = document.createElement('div'); d.textContent = t; box.appendChild(d); };
          line((q.L.label || q.L.layer || '관로') + ' · Ø' + Math.round((Number(q.L.dia) || 0) * 1000) + ' mm');
          if (Number.isFinite(q.g)) line('지반고 ' + (q.g + (P.origin[2] || 0)).toFixed(2) + ' · 관 상단고 ' + (q.top + (P.origin[2] || 0)).toFixed(2) + ' · 토피 ' + (q.g - q.top).toFixed(2) + ' m');
          box.style.left = (e.clientX + 12) + 'px'; box.style.top = (e.clientY + 12) + 'px'; box.style.display = 'block';
        }
      } else box.style.display = 'none';
    }
    dn = null;
  });
  cv.addEventListener('contextmenu', (e) => e.preventDefault());
  cv.addEventListener('wheel', (e) => {
    e.preventDefault();
    const k = e.deltaY > 0 ? 1.15 : 1 / 1.15;
    const r = cv.getBoundingClientRect(), h = k < 1 ? pickAt(e.clientX - r.left, e.clientY - r.top) : null;
    if (h) V.tgt = V.tgt.map((v, i) => v + (h.p[i] - v) * (1 - k));       // 마우스 쪽으로 다가감
    V.d = Math.max(2, V.d * k); redraw();
  }, { passive: false });
  cv.addEventListener('dblclick', (e) => { const r = cv.getBoundingClientRect(), h = pickAt(e.clientX - r.left, e.clientY - r.top); if (h) { V.tgt = h.p; redraw(); } });
  window.addEventListener('resize', redraw);
  $('bXray').addEventListener('click', () => { xray = !xray; $('bXray').classList.toggle('on', xray); redraw(); });
  $('bTop').addEventListener('click', () => { V.el = V.el > 80 ? 38 : 89.5; redraw(); });
  $('bHome').addEventListener('click', () => call('go_home'));
  $('bFiles').addEventListener('click', () => call('open_results'));
  $('bPipes').addEventListener('click', async () => {
    const r = await call('pick_pipes'); if (!r) return;
    if (r.error) { toast(r.error); return; }
    const fill = (sel, list, pick) => { sel.textContent = ''; ['(없음)'].concat(list).forEach((n) => { const o = document.createElement('option'); o.textContent = n; if (n === pick) o.selected = true; sel.appendChild(o); }); };
    const names = (r.fields || []).map((f) => f.name);
    $('dName').textContent = r.name || '관로';
    fill($('dDia'), names, r.guess_dia); fill($('dLabel'), names, null);
    $('dDepth').value = P.cover != null ? P.cover : 1.2;
    $('dlg').style.display = 'block';
    $('dOk').onclick = async () => {
      const v = (s) => (s.value === '(없음)' ? null : s.value);
      const q = await call('import_pipes', { path: r.path, dia_field: v($('dDia')), label_field: v($('dLabel')), depth: Number($('dDepth').value) });
      if (q && q.ok) { toast(q.note || '관로 넣음'); setTimeout(() => location.reload(), 400); } else if (q) toast(q.msg);
    };
  });
  $('dCancel').addEventListener('click', () => { $('dlg').style.display = 'none'; });

  // 3D 를 안 만든 프로젝트 (결과물 고르기 · 1.0.15) → 위에서 본 미리보기 그림 + 만든 것 · [결과물]
  if (Array.isArray(P.outputs) && !P.outputs.includes('mesh')) {
    const NM = { ortho: '정사영상', dsm: 'DSM', dtm: 'DTM', las: '점구름 LAS' };
    ['bPipes', 'bXray', 'bCheck', 'bTop'].forEach((id) => { $(id).style.display = 'none'; });
    cv.style.display = 'none'; $('load').style.display = 'none';
    const f = document.createElement('div'); f.id = 'flat';
    f.innerHTML = '<img src="data/미리보기.png" alt=""><div class="fl">3D 모델 없음 · 만든 것: ' + P.outputs.map((k) => NM[k] || k).join(' · ') + '</div>';
    document.body.appendChild(f);
    $('info').textContent = '사진 ' + (P.registered || '?') + '/' + (P.photos || '?') + '장 · RTK 어긋남 ' + (P.rtk_rms != null ? P.rtk_rms.toFixed(2) + ' m' : '-') + ' · ' + (P.crs || 'EPSG:5186') + ' + ' + (P.height || 'EGM2008');
    window.__map3d = { flat: true };
    return;
  }
  loadMesh().then(() => {
    buildPipes();
    $('load').style.display = 'none';
    $('info').textContent = '세모 ' + mesh.meta.tris.toLocaleString() + ' · 사진 ' + (P.registered || '?') + '/' + (P.photos || '?') + '장 · RTK 어긋남 ' +
      (P.rtk_rms != null ? P.rtk_rms.toFixed(2) + ' m' : '-') + ' · ' + (P.crs || 'EPSG:5186') + ' + ' + (P.height || 'EGM2008') + (pipeN ? ' · 관로 ' + PIPES.lines.length + '줄' : '');
    redraw(); loop();
  }).catch((e) => { $('load').textContent = '못 불러옴 · ' + e; window.__err = String(e); });
  window.__map3d = { V, redraw, pickAt };
})();
