// ★★[v108 · 계획_v108.md 2.2 · 계획 검토 높음 4] 시공 순서 재생 — 원지반 → 절토 → 옹벽 → 성토 → 녹화(막대 0~4 · ▶ 12초).
//   땅 = C#가 1 m 격자로 뜬 정지면(zf) · 원지반(zg) 높이 — 정점 셰이더로 z = mix(zg, zf, k)(절토 점은 0→1 · 성토 점은 2→3)
//     · 지형 점만 내리면 부지 밑 봉우리가 사라진다(격자라 원지반 모양 그대로) · 법선은 화면 미분(flatShading — 셰이더가 옮긴 높이로)
//   위성 → 손댄 자리부터 맨흙 · 옹벽 · 무늬 = 수평 자르는 면이 1→2에서 아래에서 위로 · 녹화(3→4) = 진짜 지형(잔디 · 쇄석)이 서서히.
import * as THREE from 'three';
import { groundTex, uvFor } from './layers.js';

const STAGES = ['원지반', '절토', '옹벽', '성토', '녹화'];
const PLAY_MS = 12000;

export class Build {
  constructor(view, L, head) {
    this.view = view; this.L = L; this.head = head;
    this.k = 0;
    this.on = false;
    this.mesh = null;
    this.rise = new THREE.Plane(new THREE.Vector3(0, 0, -1), 0);
    this.onExit = null;     // 끝 — 층을 되돌리는 쪽(main)
    this._play = 0;
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
    const uni = { uK: { value: 0 }, satBase: { value: S.base || null }, satDetail: { value: S.detail || null }, satColor: { value: new THREE.Color('#9aa77f') } };
    m.onBeforeCompile = (sh) => {
      Object.assign(sh.uniforms, uni);
      sh.vertexShader = [
        'attribute float zf;', 'attribute float zg;', 'uniform float uK;', 'varying float vTouch;',
        '#ifdef DH_SATB', 'attribute vec2 uvb;', 'varying vec2 vUvB;', '#endif',
        '#ifdef DH_SATD', 'attribute vec2 uvd;', 'varying vec2 vUvD;', '#endif',
        sh.vertexShader.replace('#include <begin_vertex>', [
          '#include <begin_vertex>',
          // 절토 점(정지면이 아래)은 0→1 · 성토 점은 2→3 · 3 cm 아래(지형의 원지반 그대로 자리가 이긴다)
          '\tfloat kk = zf < zg ? clamp( uK, 0.0, 1.0 ) : clamp( uK - 2.0, 0.0, 1.0 );',
          '\ttransformed.z = mix( zg, zf, kk ) - 0.03;',
          '\tvTouch = step( 0.02, abs( zf - zg ) ) * smoothstep( 0.0, 0.12, kk );',
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
    m.customProgramCacheKey = () => 'dh3d-build' + (S.base ? 'b' : '') + (S.detail ? 'd' : '');
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
    // 설계 자리는 격자가 대신 — 지형은 원지반 그대로 자리만 · 절성토 색 · 원지반 · 터파기 · 빗물 · 지적 · 측점 · 경사는 끔(정지면에 얹은 것이라 공중에 뜬다)
    T.obj.material = T.mats;
    for (const id of ['ground', 'excav', 'drain', 'cad', 'align', 'slope']) if (L[id] && L[id].obj) L[id].obj.visible = false;
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

  set(k) {
    if (!this.on) return;
    this.k = k = Math.max(0, Math.min(4, k));
    this.uni.uK.value = k;
    const T = this.L.terrain, wallOn = !!this.L.wall;
    // 녹화(3→4) — 진짜 지형이 격자 위로 서서히 · 4면 격자를 걷는다
    const green = k > 3 ? k - 3 : 0;
    this.mesh.visible = k < 3.999;
    for (const m of T.mats) {
      const key = m.userData.key;
      m.visible = key === 'ground' ? true : key === 'wallFace' ? !wallOn && green > 0 : green > 0;
    }
    this._fade(green, true);
    // 옹벽 · 무늬 — 1→2에서 아래에서 위로
    const wv = k > 1;
    for (const id of ['wall', 'pattern']) if (this.L[id]) this.L[id].obj.visible = wv && this.L[id].on;
    if (this.wz) this.rise.constant = k >= 2 ? this.wz[1] + 1 : this.wz[0] + (this.wz[1] - this.wz[0]) * Math.max(0, k - 1);
    this._label();
    const sl = document.getElementById('bldS');
    if (sl && document.activeElement !== sl) sl.value = String(k);
    this.view.request();
  }

  play() {
    this.stop();
    const from = this.k >= 3.999 ? 0 : this.k;
    const t0 = performance.now() - (from / 4) * PLAY_MS;
    const step = () => {
      const k = ((performance.now() - t0) / PLAY_MS) * 4;
      this.set(k);
      if (k < 4 && this.on) this._play = requestAnimationFrame(step);
      else { this._play = 0; this._btn(); }
    };
    this._play = requestAnimationFrame(step);
    this._btn();
  }

  stop() { if (this._play) cancelAnimationFrame(this._play); this._play = 0; this._btn(); }

  _btn() { const b = document.getElementById('bldPlay'); if (b) b.textContent = this._play ? '❚❚' : '▶'; }

  stage() { const k = this.k; return k <= 0 ? 0 : Math.min(4, Math.ceil(k - 1e-6)); }

  _label() {
    const el = document.getElementById('bldStages');
    if (!el) return;
    const s = this.stage();
    [...el.children].forEach((c, i) => c.classList.toggle('on', i === s));
  }

  panel(dock) {
    dock.open('build', `
      <div class="row"><span class="t">시공 순서</span><span class="grow"></span><button class="x" id="bldX" title="닫기">✕</button></div>
      <div class="row"><button id="bldPlay" class="b" title="재생">▶</button><input type="range" id="bldS" min="0" max="4" step="0.01" value="${this.k}"></div>
      <div class="stages" id="bldStages">${STAGES.map(s => `<span>${s}</span>`).join('')}</div>`,
    () => this.exit());
    const sl = document.getElementById('bldS');
    sl.oninput = () => { this.stop(); this.set(+sl.value); };
    document.getElementById('bldPlay').onclick = () => (this._play ? this.stop() : this.play());
    document.getElementById('bldX').onclick = () => dock.close();
    this._label();
  }
}

