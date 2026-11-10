// ★★[v108 · 계획_v108.md 2.2 · 계획 검토 높음 4] 시공 순서 재생 — 원지반 → 절토 → 옹벽 → 성토 → 녹화(막대 0~4 · ▶ 단계마다 3초).
//   땅 = C#가 1 m 격자로 뜬 정지면(zf) · 원지반(zg) 높이 — 정점 셰이더로 z = mix(zg, zf, k)(절토 점은 0→1 · 성토 점은 2→3)
//     · 지형 점만 내리면 부지 밑 봉우리가 사라진다(격자라 원지반 모양 그대로) · 법선은 화면 미분(flatShading — 셰이더가 옮긴 높이로)
//   위성 → 손댄 자리부터 맨흙 · 옹벽 · 무늬 = 수평 자르는 면이 1→2에서 아래에서 위로 · 녹화(3→4) = 진짜 지형(잔디 · 쇄석)이 서서히.
// ★★[v111.8.1 · JACK 1103 «시공순서도 현실성있게 · 보통 공사를 하려면 진입도로부터 하지 않아?»] 도면에 진입도로가 있으면(build.road — 진입도로 자리 마디)
//   원지반 → 진입도로 → 절토 → 옹벽 → 성토 → 포장·녹화(막대 0~5). 실제 공사는 장비가 들어갈 길(진입도로 토공)을 먼저 열고,
//   포장(아스팔트 · 콘크리트 · 흰 차선)은 무거운 장비 일(부지 절 · 성토 · 옹벽)이 끝난 뒤 마지막에 깐다(포장이 장비에 깨지지 않게).
//   · 진입도로(0→1) = 도로 자리 마디가 절 · 성토 모두 원지반 → 정지면(맨흙 — 포장은 아직) · 부지 절 · 성토는 그 뒤 옛 차례 그대로(1→2 · 3→4).
//   · 도로 끝이 아직 손 안 댄 부지에 닿는 자리는 임시 비탈(1:1.5 — zr)로 내려가 · 올라가 맞춘다 — 1 m 칸 하나에 몇 m 낭떠러지(벽 · 바늘)가 서지 않게.
//     부지 마디는 진입도로 단계에서 zg → zr(그 마디 정지면을 넘지 않음 · 대부분 zr = zg) · 자기 단계에서 zr → zf.
//   · 포장·녹화(4→5) = 진짜 지형(차로 · 길어깨 · 잔디 · 쇄석) + 흰 차선이 서서히. 도로 없는 도면은 옛 다섯 단계 · 같은 셰이더 그대로.
import * as THREE from 'three';
import { groundTex, uvFor } from './layers.js';

const STAGES_OLD = ['원지반', '절토', '옹벽', '성토', '녹화'];
const STAGES_ROAD = ['원지반', '진입도로', '절토', '옹벽', '성토', '포장·녹화'];
const STAGE_MS = 3000;           // 단계마다(옛 12초 = 넷 × 3초)
const TEMP_SLOPE = 1 / 1.5;      // 진입도로 끝 임시 비탈(높이/수평)

// 진입도로 끝 임시 비탈 — 도로 마디(zf)에서 1:1.5로 퍼지는 원뿔의 위 · 아래 감싸기(8방 거리 두 번 쓸기) → 부지 마디의 진입도로 단계 높이 zr
//   성토 마디: zr = min(zf, max(zg, 도로 마디 zf − 거리 × 기울기 중 가장 높은 것)) · 절토 마디: zr = max(zf, min(zg, 도로 zf + 거리 × 기울기 중 가장 낮은 것))
function tempSlope(B) {
  const w = B.w + 1, h = B.h + 1, n = w * h, c = B.cell, zf = B.zf, zg = B.zg, road = B.road;
  const up = new Float32Array(n), dn = new Float32Array(n);
  for (let k = 0; k < n; k++) {
    const r = road[k] && Number.isFinite(zf[k]);
    up[k] = r ? zf[k] : -Infinity;
    dn[k] = r ? zf[k] : Infinity;
  }
  const s1 = c * TEMP_SLOPE, s2 = c * Math.SQRT2 * TEMP_SLOPE;
  const relax = (k, q, s) => { if (up[q] - s > up[k]) up[k] = up[q] - s; if (dn[q] + s < dn[k]) dn[k] = dn[q] + s; };
  for (let j = 0; j < h; j++)
    for (let i = 0; i < w; i++) {
      const k = j * w + i;
      if (i > 0) relax(k, k - 1, s1);
      if (j > 0) { relax(k, k - w, s1); if (i > 0) relax(k, k - w - 1, s2); if (i < w - 1) relax(k, k - w + 1, s2); }
    }
  for (let j = h - 1; j >= 0; j--)
    for (let i = w - 1; i >= 0; i--) {
      const k = j * w + i;
      if (i < w - 1) relax(k, k + 1, s1);
      if (j < h - 1) { relax(k, k + w, s1); if (i < w - 1) relax(k, k + w + 1, s2); if (i > 0) relax(k, k + w - 1, s2); }
    }
  const zr = new Float32Array(n);
  for (let k = 0; k < n; k++) {
    const f = zf[k], g = Number.isFinite(zg[k]) ? zg[k] : f;
    if (road[k] || !Number.isFinite(f)) zr[k] = Number.isFinite(f) ? f : g;
    else if (f > g) zr[k] = Math.min(f, Math.max(g, up[k]));
    else if (f < g) zr[k] = Math.max(f, Math.min(g, dn[k]));
    else zr[k] = g;
  }
  return zr;
}

export class Build {
  constructor(view, L, head) {
    this.view = view; this.L = L; this.head = head;
    this.k = 0;
    this.on = false;
    this.mesh = null;
    this.rise = new THREE.Plane(new THREE.Vector3(0, 0, -1), 0);
    this.onExit = null;     // 끝 — 층을 되돌리는 쪽(main)
    this._play = 0;
    const B = head.build;
    this.road = !!(B && B.road && B.road.length === (B.w + 1) * (B.h + 1));
    this.stages = this.road ? STAGES_ROAD : STAGES_OLD;
    this.N = this.stages.length - 1;        // 막대 끝(옛 4 · 도로 5)
    this.off = this.road ? 1 : 0;           // 부지 단계가 밀리는 칸 수
  }

  get available() { return !!(this.head.build && this.L.terrain); }

  _makeMesh() {
    const B = this.head.build, w = B.w, h = B.h, c = B.cell, n = (w + 1) * (h + 1);
    const pos = new Float32Array(n * 3), uv2 = new Float32Array(n * 2);
    for (let j = 0; j <= h; j++)
      for (let i = 0; i <= w; i++) {
        const k = j * (w + 1) + i, x = B.x0 + i * c, y = B.y0 + j * c;
        pos[k * 3] = x; pos[k * 3 + 1] = y; pos[k * 3 + 2] = Number.isFinite(B.zg[k]) ? B.zg[k] : 0;
        uv2[k * 2] = x; uv2[k * 2 + 1] = y;
      }
    // 정지면이 없는 점(NaN)이 든 세모는 뺀다 — 그 자리는 지형의 원지반 그대로 자리가 덮는다
    const ok = (k) => Number.isFinite(B.zf[k]);
    const idx = [];
    for (let j = 0; j < h; j++)
      for (let i = 0; i < w; i++) {
        const a = j * (w + 1) + i, b = a + 1, cc = a + w + 1, d = cc + 1;
        if (ok(a) && ok(b) && ok(d)) idx.push(a, b, d);
        if (ok(a) && ok(d) && ok(cc)) idx.push(a, d, cc);
      }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    g.setAttribute('zf', new THREE.BufferAttribute(B.zf, 1));
    g.setAttribute('zg', new THREE.BufferAttribute(B.zg, 1));
    if (this.road) g.setAttribute('zr', new THREE.BufferAttribute(tempSlope(B), 1));
    g.setAttribute('uv2', new THREE.BufferAttribute(uv2, 2));
    const S = this.L.$sat || {};
    if (S.base) g.setAttribute('uvb', new THREE.BufferAttribute(uvFor(pos, S.baseExt), 2));
    if (S.detail) g.setAttribute('uvd', new THREE.BufferAttribute(uvFor(pos, S.detailExt), 2));
    g.setIndex(new THREE.BufferAttribute(new Uint32Array(idx), 1));
    g.computeBoundingSphere();   // 높이는 셰이더가 옮긴다 — 화면 밖 걸러내기는 끈다(아래 frustumCulled)

    const m = new THREE.MeshStandardMaterial({ map: groundTex(this.view, 'soil', 6), flatShading: true, roughness: 0.95 });
    m.defines = {};
    if (S.base) m.defines.DH_SATB = '';
    if (S.detail) m.defines.DH_SATD = '';
    if (this.road) m.defines.DH_ROAD = '';
    const uni = { uK: { value: 0 }, satBase: { value: S.base || null }, satDetail: { value: S.detail || null }, satColor: { value: new THREE.Color('#9aa77f') } };
    m.onBeforeCompile = (sh) => {
      Object.assign(sh.uniforms, uni);
      sh.vertexShader = [
        'attribute float zf;', 'attribute float zg;', 'uniform float uK;', 'varying float vTouch;',
        '#ifdef DH_ROAD', 'attribute float zr;', '#endif',
        '#ifdef DH_SATB', 'attribute vec2 uvb;', 'varying vec2 vUvB;', '#endif',
        '#ifdef DH_SATD', 'attribute vec2 uvd;', 'varying vec2 vUvD;', '#endif',
        sh.vertexShader.replace('#include <begin_vertex>', [
          '#include <begin_vertex>',
          '#ifdef DH_ROAD',
          // 진입도로 단계(0→1) = 도로 마디 zg → zf(= zr) · 부지 마디 zg → zr(임시 비탈) · 그 뒤 부지 절토 1→2 · 성토 3→4에서 zr → zf
          '\tfloat kr = clamp( uK, 0.0, 1.0 );',
          '\tfloat kk = zf < zg ? clamp( uK - 1.0, 0.0, 1.0 ) : clamp( uK - 3.0, 0.0, 1.0 );',
          '\ttransformed.z = mix( mix( zg, zr, kr ), zf, kk ) - 0.03;',
          '\tvTouch = step( 0.02, abs( zf - zg ) ) * smoothstep( 0.0, 0.12, max( kk, kr * step( 0.02, abs( zr - zg ) ) ) );',
          '#else',
          // 절토 점(정지면이 아래)은 0→1 · 성토 점은 2→3 · 3 cm 아래(지형의 원지반 그대로 자리가 이긴다)
          '\tfloat kk = zf < zg ? clamp( uK, 0.0, 1.0 ) : clamp( uK - 2.0, 0.0, 1.0 );',
          '\ttransformed.z = mix( zg, zf, kk ) - 0.03;',
          '\tvTouch = step( 0.02, abs( zf - zg ) ) * smoothstep( 0.0, 0.12, kk );',
          '#endif',
          '#ifdef DH_SATB', '\tvUvB = uvb;', '#endif',
          '#ifdef DH_SATD', '\tvUvD = uvd;', '#endif',
        ].join('\n')),
      ].join('\n');
      sh.fragmentShader = [
        'uniform sampler2D satBase;', 'uniform sampler2D satDetail;', 'uniform vec3 satColor;', 'varying float vTouch;',
        '#ifdef DH_SATB', 'varying vec2 vUvB;', '#endif',
        '#ifdef DH_SATD', 'varying vec2 vUvD;', '#endif',
        sh.fragmentShader.replace('#include <map_fragment>', [
          '#ifdef USE_MAP',
          '\tvec4 soilC = texture2D( map, vMapUv );',
          '\tvec4 satC = vec4( satColor, 1.0 );',
          '#ifdef DH_SATB', '\tsatC = texture2D( satBase, vUvB );', '#endif',
          '#ifdef DH_SATD',
          '\tfloat insideD = step( 0.0, vUvD.x ) * step( vUvD.x, 1.0 ) * step( 0.0, vUvD.y ) * step( vUvD.y, 1.0 );',
          '\tsatC = mix( satC, texture2D( satDetail, clamp( vUvD, 0.0, 1.0 ) ), insideD );',
          '#endif',
          '\tdiffuseColor *= mix( satC, soilC, vTouch );',
          '#endif',
        ].join('\n')),
      ].join('\n');
    };
    m.customProgramCacheKey = () => 'dh3d-build' + (S.base ? 'b' : '') + (S.detail ? 'd' : '') + (this.road ? 'r' : '');
    this.uni = uni;
    const mesh = new THREE.Mesh(g, m);
    mesh.receiveShadow = true;
    mesh.frustumCulled = false;
    mesh.visible = false;
    mesh.userData = { layer: 'build' };
    mesh.raycast = () => {};
    this.view.scene.add(mesh);
    this.mesh = mesh;
    // 옹벽 · 무늬 높이 범위 — 자르는 면이 이 사이를 올라간다
    let z0 = Infinity, z1 = -Infinity;
    for (const id of ['wall', 'pattern']) {
      const l = this.L[id];
      if (!l) continue;
      for (const o of l.obj.children) if (o.geometry && o.geometry.boundingBox) { z0 = Math.min(z0, o.geometry.boundingBox.min.z); z1 = Math.max(z1, o.geometry.boundingBox.max.z); }
    }
    this.wz = Number.isFinite(z0) ? [z0 - 0.05, z1 + 0.05] : null;
  }

  _wallMats() {
    const set = new Set();
    for (const id of ['wall', 'pattern']) { const l = this.L[id]; if (l) l.obj.traverse(o => { if (o.isMesh && !o.userData.stencil) set.add(o.material); }); }
    return [...set];
  }

  enter() {
    if (this.on || !this.available) return;
    if (!this.mesh) this._makeMesh();
    this.on = true;
    const L = this.L, T = L.terrain;
    // 설계 자리는 격자가 대신 — 지형은 원지반 그대로 자리만 · 절성토 색 · 원지반 · 터파기 · 빗물 · 지적 · 측점(부지 · 진입도로) · 경사는 끔(정지면에 얹은 것이라 공중에 뜬다)
    T.obj.material = T.mats;
    for (const id of ['ground', 'excav', 'drain', 'cad', 'align', 'ralign', 'slope']) if (L[id] && L[id].obj) L[id].obj.visible = false;
    if (T.lanes) T.lanes.visible = false;            // 차선도(끝나면 applyTerrainMode가 되살림)
    if (T.joints) T.joints.visible = false;          // ★[v116.8 · 검토 M2] 줄눈도
    for (const m of this._wallMats()) { m.clippingPlanes = [this.rise]; m.clipShadows = true; m.side = THREE.DoubleSide; m.needsUpdate = true; }
    this.mesh.visible = true;
    this.set(0);
  }

  exit() {
    if (!this.on) return;
    this.stop();
    this.on = false;
    this.mesh.visible = false;
    for (const m of this._wallMats()) { m.clippingPlanes = null; m.clipShadows = false; m.side = THREE.FrontSide; m.needsUpdate = true; }
    this._fade(1, false);
    this._laneFade(1, false);
    if (this.onExit) this.onExit();
    this.view.request();
  }

  // 녹화 — 진짜 지형 설계 구분이 서서히(투명도) · 끝나면 불투명으로 되돌림
  _fade(a, on) {
    const T = this.L.terrain;
    for (const m of T.mats) {
      const key = m.userData.key;
      if (key === 'ground') continue;
      const tr = on && a < 0.999;
      if (m.transparent !== tr) { m.transparent = tr; m.needsUpdate = true; }
      m.opacity = tr ? a : 1;
    }
  }

  // 흰 차선 — 포장·녹화에서 지형과 함께 서서히(그 전엔 숨김) · 끝나면 불투명으로 되돌림(보이기는 main의 applyTerrainMode가)
  _laneFade(a, on) {
    const T = this.L.terrain;
    // ★[v116.8 · 검토 M2] 콘크리트 줄눈도 차선과 함께
    for (const [obj, m] of [[T.lanes, T.laneMat], [T.joints, T.jointMat]]) {
      if (!obj || !m) continue;
      const tr = on && a < 0.999;
      if (m.transparent !== tr) { m.transparent = tr; m.needsUpdate = true; }
      m.opacity = tr ? a : 1;
      if (on) obj.visible = a > 0;
    }
  }

  set(k) {
    if (!this.on) return;
    const N = this.N, o = this.off;
    this.k = k = Math.max(0, Math.min(N, k));
    this.uni.uK.value = k;
    const T = this.L.terrain, wallOn = !!this.L.wall;
    // 녹화(옛 3→4 · 도로 4→5 포장·녹화) — 진짜 지형이 격자 위로 서서히 · 끝이면 격자를 걷는다
    const green = k > N - 1 ? k - (N - 1) : 0;
    this.mesh.visible = k < N - 0.001;
    for (const m of T.mats) {
      const key = m.userData.key;
      m.visible = key === 'ground' ? true : key === 'wallFace' ? !wallOn && green > 0 : green > 0;
    }
    this._fade(green, true);
    this._laneFade(green, true);
    // 옹벽 · 무늬 — 옛 1→2(도로 2→3)에서 아래에서 위로
    const wv = k > 1 + o;
    for (const id of ['wall', 'pattern']) if (this.L[id]) this.L[id].obj.visible = wv && this.L[id].on;
    if (this.wz) this.rise.constant = k >= 2 + o ? this.wz[1] + 1 : this.wz[0] + (this.wz[1] - this.wz[0]) * Math.max(0, k - 1 - o);
    this._label();
    const sl = document.getElementById('bldS');
    if (sl && document.activeElement !== sl) sl.value = String(k);
    this.view.request();
  }

  play() {
    this.stop();
    const N = this.N;
    const from = this.k >= N - 0.001 ? 0 : this.k;
    const t0 = performance.now() - from * STAGE_MS;
    const step = () => {
      const k = (performance.now() - t0) / STAGE_MS;
      this.set(k);
      if (k < N && this.on) this._play = requestAnimationFrame(step);
      else { this._play = 0; this._btn(); }
    };
    this._play = requestAnimationFrame(step);
    this._btn();
  }

  stop() { if (this._play) cancelAnimationFrame(this._play); this._play = 0; this._btn(); }

  _btn() { const b = document.getElementById('bldPlay'); if (b) b.textContent = this._play ? '❚❚' : '▶'; }

  stage() { const k = this.k; return k <= 0 ? 0 : Math.min(this.N, Math.ceil(k - 1e-6)); }

  _label() {
    const el = document.getElementById('bldStages');
    if (!el) return;
    const s = this.stage();
    [...el.children].forEach((c, i) => c.classList.toggle('on', i === s));
  }

  panel(dock) {
    dock.open('build', `
      <div class="row"><span class="t">시공 순서</span><span class="grow"></span><button class="x" id="bldX" title="닫기">✕</button></div>
      <div class="row"><button id="bldPlay" class="b" title="재생">▶</button><input type="range" id="bldS" min="0" max="${this.N}" step="0.01" value="${this.k}"></div>
      <div class="stages" id="bldStages">${this.stages.map(s => `<span>${s}</span>`).join('')}</div>`,
    () => this.exit());
    const sl = document.getElementById('bldS');
    sl.oninput = () => { this.stop(); this.set(+sl.value); };
    document.getElementById('bldPlay').onclick = () => (this._play ? this.stop() : this.play());
    document.getElementById('bldX').onclick = () => dock.close();
    this._label();
  }
}

