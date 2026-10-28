// ★★[v108 · 계획_v108.md 2.7] 3D에서 자르기 — 측점 가로선을 지나는 세로 면으로 자르고(보는 쪽 반을 숨김) 옹벽 잘린 면을 채운다.
//   옹벽 · 무늬 잘린 면 = 스텐실(뒷면 +1 · 앞면 −1 → 0이 아닌 자리에만 뚜껑 — 몸통 · 모서리 메움이 겹쳐도 0이 아님)
//   지형 · 원지반 = 잘린 선(노랑 · 하늘색) · 면을 측점 따라 앞뒤로 옮김(막대 · ◀ ▶) · 아래 칸에 범례.
import * as THREE from 'three';
import { fatLines } from './lines.js';
import { alignAt, stationName } from './extras.js';

const fmt = (v, d = 2) => Number.isFinite(v) ? v.toLocaleString('ko-KR', { minimumFractionDigits: d, maximumFractionDigits: d }) : '—';
const PLAN_LINE = 0xffd400, GROUND_LINE = 0x3ee6ff, CAP = 0x8b877f;   // 정지면 선 · 원지형 선 · 옹벽 잘린 면
const hex = (c) => '#' + c.toString(16).padStart(6, '0');

export class Section {
  constructor(view, L, head, ui) {
    this.view = view; this.L = L; this.head = head; this.ui = ui;
    this.plane = new THREE.Plane(new THREE.Vector3(1, 0, 0), 0);
    this.planes = [this.plane];
    this.on = false;
    this.s = 0;
    this.lines = new THREE.Group();
    this.lines.userData.noClip = true;
    view.scene.add(this.lines);
    this.onEnd = null;     // 자르기 끝 — 아래 칸을 닫는 쪽에 알림
    // 보는 쪽 반을 숨긴다 — 사진기가 남는 쪽으로 넘어가면 면을 뒤집는다(돌아가 보면 반대쪽 단면 · 흙 채움도 그쪽으로 다시)
    view.onDraw(() => {
      if (!this.on) return;
      if (this.plane.distanceToPoint(view.camera.position) > 0) { this.plane.negate(); this._cutLines(this.p, this.plane.normal); }
    });
  }

  get available() { return !!(this.L.align && this.L.align.st.length); }

  // 잘리는 재료 — 지형 구분 · 절성토 · 원지반 · 터파기 · 옹벽 · 무늬 · 빗물 · 지적 · 경사(측점 · 자른 선 · 뚜껑은 안 잘림)
  _materials() {
    const set = new Set();
    const add = (m) => { if (!m) return; for (const x of (Array.isArray(m) ? m : [m])) set.add(x); };
    const L = this.L;
    if (L.terrain) { L.terrain.mats.forEach(add); add(L.terrain.cfMat); }
    for (const id of ['ground', 'excav', 'wall', 'pattern', 'drain', 'cad', 'slope']) {
      const l = L[id];
      if (!l || !l.obj) continue;
      l.obj.traverse(o => { if (o.material && !o.userData.noClip && !o.userData.stencil) add(o.material); });
    }
    return [...set];
  }

  _setClip(on) {
    for (const m of this._materials()) {
      m.clippingPlanes = on ? this.planes : null;
      m.clipShadows = on;
      m.needsUpdate = true;
    }
  }

  // 스텐실 짝(옹벽 · 무늬 몸통마다 뒷면 · 앞면) · 뚜껑 — 처음 자를 때 한 번 만든다
  _ensureStencil() {
    if (this.cap) return;
    const base = { depthWrite: false, depthTest: false, colorWrite: false, stencilWrite: true, stencilFunc: THREE.AlwaysStencilFunc, clippingPlanes: this.planes };
    const inc = THREE.IncrementWrapStencilOp, dec = THREE.DecrementWrapStencilOp;
    const back = new THREE.MeshBasicMaterial({ ...base, side: THREE.BackSide, stencilFail: inc, stencilZFail: inc, stencilZPass: inc });
    const front = new THREE.MeshBasicMaterial({ ...base, side: THREE.FrontSide, stencilFail: dec, stencilZFail: dec, stencilZPass: dec });
    this.stencil = [];
    for (const id of ['wall', 'pattern']) {
      const l = this.L[id];
      if (!l) continue;
      for (const mesh of [...l.obj.children]) {
        if (!mesh.isMesh) continue;
        for (const m of [back, front]) {
          const s = new THREE.Mesh(mesh.geometry, m);
          s.userData = { stencil: true };
          s.renderOrder = 5;
          s.raycast = () => {};
          s.visible = false;
          mesh.add(s);
          this.stencil.push(s);
        }
      }
    }
    const rep = THREE.ReplaceStencilOp;
    const capMat = new THREE.MeshStandardMaterial({
      color: CAP, roughness: 0.95, side: THREE.DoubleSide,
      stencilWrite: true, stencilRef: 0, stencilFunc: THREE.NotEqualStencilFunc, stencilFail: rep, stencilZFail: rep, stencilZPass: rep,
    });
    this.cap = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), capMat);
    this.cap.renderOrder = 6;
    this.cap.userData.noClip = true;
    this.cap.raycast = () => {};
    this.cap.visible = false;
    this.cap.onAfterRender = (r) => r.clearStencil();
    this.view.scene.add(this.cap);
  }

  // 측점 i(가로선 그대로) · 또는 측점 값 s(중심선 위 · 진행 방향에 직각)
  at(where) {
    const A = this.L.align;
    let p, n, s;
    if (typeof where === 'number' && Number.isInteger(where) && A.st[where]) {
      const st = A.st[where];
      const l = st.l, r = st.r;
      const dx = r[0] - l[0], dy = r[1] - l[1], len = Math.hypot(dx, dy) || 1;
      n = new THREE.Vector3(dy / len, -dx / len, 0);
      p = new THREE.Vector3(st.c[0], st.c[1], st.c[2] > -9999 ? st.c[2] : 0);
      s = st.s;
    } else {
      s = Math.max(A.s0, Math.min(A.s1, where.s));
      const a = alignAt(A, s);
      if (!a) return;
      p = a.p; n = a.dir;
    }
    // 이미 자르는 중이면 사진기도 같이 옮긴다(측점 따라 걷듯 — 보던 방향 그대로)
    if (this.on && this.p && !this._facing) {
      const d = p.clone().sub(this.p);
      this.view.camera.position.add(d);
      this.view.controls.target.add(d);
      this.view.controls.update();
    }
    this.s = s;
    this.p = p;
    // 사진기 쪽이 숨는 쪽(면의 음수 쪽)
    this.plane.setFromNormalAndCoplanarPoint(n, p);
    if (this.plane.distanceToPoint(this.view.camera.position) > 0) this.plane.negate();
    const size = Math.max(200, (this.view.siteSize || 500) * 3);
    this.cap.scale.set(size, size, 1);
    this.cap.position.copy(p);
    this.cap.quaternion.setFromUnitVectors(new THREE.Vector3(0, 0, 1), n);
    this.cap.updateMatrixWorld();
    this._cutLines(p, this.plane.normal);
    this._label();
    this.view.request();
  }

  start(where, face = true) {
    this._ensureStencil();
    this._facing = face;
    if (!this.on) {
      this.on = true;
      this._setClip(true);
      for (const s of this.stencil) s.visible = true;
      this.cap.visible = true;
      // 측점 층은 자르는 동안 숨긴다 — 중심선 · 이름이 단면 가운데에 겹겹이 쌓인다(끝나면 체크 상태대로)
      const A = this.L.align;
      if (A && A.obj) A.obj.visible = false;
    }
    this.at(where);
    if (face) { this._face(); this._cutLines(this.p, this.plane.normal); }
    this._facing = false;
  }

  end() {
    if (!this.on) return;
    this.on = false;
    this._setClip(false);
    for (const s of this.stencil || []) s.visible = false;
    if (this.cap) this.cap.visible = false;
    const A = this.L.align;
    if (A && A.obj) A.obj.visible = A.on;
    this._clearLines();
    this.view.request();
    if (this.onEnd) this.onEnd();
  }

  // 단면을 마주 보게 — 측점 가운데를 숨는 쪽에서 20° 내려다봄 · 거리 = 횡단 폭
  _face() {
    const v = this.view, p = this.p;
    // 측점에서 진행 방향 뒤쪽(작은 측점 쪽)에서 본다 — 면은 사진기 쪽이 숨도록 다시 맞춤
    const n = this.plane.normal.clone();
    const st = this.L.align.st[0];
    const w = st ? Math.hypot(st.r[0] - st.l[0], st.r[1] - st.l[1]) : 60;
    const d = Math.max(40, w * 1.15);
    const el = THREE.MathUtils.degToRad(18);
    v.controls.target.copy(p);
    v.camera.position.set(p.x - n.x * Math.cos(el) * d, p.y - n.y * Math.cos(el) * d, p.z + Math.sin(el) * d);
    v.camera.lookAt(p);
    v.controls.update();
    if (this.plane.distanceToPoint(v.camera.position) > 0) this.plane.negate();
    v.request();
  }

  _clearLines() {
    for (const c of [...this.lines.children]) {
      this.lines.remove(c);
      c.geometry.dispose();
      if (c.material.map) c.material.map.dispose();
      c.material.dispose();
    }
  }

  // 세모 묶음을 면으로 자른 선분(점 묶음) — 면 위 높이 + dz
  static slice(pos, idx, n, d0, dz) {
    const out = [];
    const nx = n.x, ny = n.y, nz = n.z;
    const D = (i) => pos[i * 3] * nx + pos[i * 3 + 1] * ny + pos[i * 3 + 2] * nz - d0;
    const P = (i, j, a, b) => { const t = a / (a - b); out.push(pos[i * 3] + (pos[j * 3] - pos[i * 3]) * t, pos[i * 3 + 1] + (pos[j * 3 + 1] - pos[i * 3 + 1]) * t, pos[i * 3 + 2] + (pos[j * 3 + 2] - pos[i * 3 + 2]) * t + dz); };
    for (let f = 0; f + 2 < idx.length; f += 3) {
      const i0 = idx[f], i1 = idx[f + 1], i2 = idx[f + 2];
      const a = D(i0), b = D(i1), c = D(i2);
      if ((a > 0 && b > 0 && c > 0) || (a < 0 && b < 0 && c < 0)) continue;
      const before = out.length;
      if ((a > 0) !== (b > 0)) P(i0, i1, a, b);
      if ((b > 0) !== (c > 0)) P(i1, i2, b, c);
      if ((c > 0) !== (a > 0)) P(i2, i0, c, a);
      if (out.length - before !== 6) out.length = before;   // 꼭짓점이 면 위(0)인 드문 경우 — 버림(이웃 세모가 이어 줌)
    }
    return out;
  }

  _cutLines(p, n) {
    this._clearLines();
    const d0 = n.dot(p);
    const T = this.head.terrain, G = this.head.ground;
    if (T && T.pos) {
      const seg = Section.slice(T.pos, T.idx, n, d0, 0.03);
      if (seg.length) {
        const l = fatLines(this.view, seg, PLAN_LINE, 3.2, { renderOrder: 7 }); l.userData.noClip = true; this.lines.add(l);
        this.lines.add(this._earth(seg, n));
      }
    }
    // 원지반 — 그릴 때 5 cm 내렸다(layers.js) · 되올려 원래 높이
    //   ★[JACK 1028 «원지형선 색이 잘린 단면색과 비슷해 헷갈림»] 갈색(흙 빗금과 같은 계열) → 하늘색 + 어두운 테(흙 · 하늘 · 위성 어디서나 보임)
    //   깊이를 안 따진다 — 노란 선과 같은 자리라 깊이를 다투면 테가 하늘색을 이겨 검은 점 · 노란 점이 박힌다(1028 화면에서 봄).
    //   선은 자르는 면 위 · 사진기는 숨긴 쪽이라 앞을 가릴 것이 없다(남는 쪽 물체는 다 면 뒤).
    if (G && G.pos) {
      const seg = Section.slice(G.pos, G.idx, n, d0, 0.05 + 0.03);
      if (seg.length)
        for (const [c, w, ro] of [[0x0c1a20, 6.5, 8], [GROUND_LINE, 3.2, 9]]) {
          const l = fatLines(this.view, seg, c, w, { renderOrder: ro, depthTest: false, depthWrite: false }); l.userData.noClip = true; this.lines.add(l);
        }
    }
  }

  // 잘린 땅 — 정지면 선 아래를 바닥(지형 가장 낮은 곳 − 5 m)까지 흙색으로 채운 막(횡단도처럼 · 그러지 않으면 땅 밑으로 하늘이 비친다).
  //   면에서 남는 쪽으로 2 cm 물려 옹벽 잘린 면(뚜껑 — 면 위)이 앞에 오게
  _earth(seg, n) {
    const b = this.head.bbox || [0, 0, 0, 0, 0, 0];
    const zb = b[2] - 5, ox = n.x * 0.02, oy = n.y * 0.02;
    const pos = new Float32Array((seg.length / 6) * 18);
    let k = 0;
    for (let i = 0; i + 5 < seg.length; i += 6) {
      const ax = seg[i] + ox, ay = seg[i + 1] + oy, az = seg[i + 2] - 0.03, bx = seg[i + 3] + ox, by = seg[i + 4] + oy, bz = seg[i + 5] - 0.03;
      pos.set([ax, ay, az, bx, by, bz, bx, by, zb, ax, ay, az, bx, by, zb, ax, ay, zb], k);
      k += 18;
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    const m = new THREE.MeshBasicMaterial({ map: this._earthTex(), color: 0xffffff, side: THREE.DoubleSide });
    // 무늬 좌표 = 면 위 가로(수평) · 세로(높이) m
    const uv = new Float32Array((pos.length / 3) * 2), t = new THREE.Vector3(-n.y, n.x, 0);
    for (let i = 0, j = 0; i < pos.length; i += 3, j += 2) { uv[j] = (pos[i] * t.x + pos[i + 1] * t.y) / 3; uv[j + 1] = pos[i + 2] / 3; }
    g.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
    const mesh = new THREE.Mesh(g, m);
    mesh.renderOrder = 5;
    mesh.userData.noClip = true;
    mesh.raycast = () => {};
    return mesh;
  }

  // 흙 빗금(횡단도 흙 표시) — 갈색 바탕 + 45° 옅은 선
  _earthTex() {
    const cv = document.createElement('canvas');
    cv.width = cv.height = 64;
    const g = cv.getContext('2d');
    g.fillStyle = '#7a5a3c'; g.fillRect(0, 0, 64, 64);
    g.strokeStyle = 'rgba(255,230,200,0.22)'; g.lineWidth = 3;
    for (let i = -64; i <= 64; i += 16) { g.beginPath(); g.moveTo(i, 64); g.lineTo(i + 64, 0); g.stroke(); }
    const t = new THREE.CanvasTexture(cv);
    t.wrapS = t.wrapT = THREE.RepeatWrapping;
    t.colorSpace = THREE.SRGBColorSpace;
    return t;
  }

  // 아래 칸 — 자르기 · 측점 · ◀ 막대 ▶ · 범례(★[JACK 1028] 자르는 동안만 — 칸이 닫히면 같이 사라짐) · 끝
  panel(dock) {
    const A = this.L.align;
    const key = [`<span><i class="ln" style="background:${hex(PLAN_LINE)}"></i>정지면</span>`];
    if (this.head.ground && this.head.ground.pos) key.push(`<span><i class="ln" style="background:${hex(GROUND_LINE)};box-shadow:0 0 0 1.5px #0c1a20"></i>원지형</span>`);
    key.push('<span><i style="background:repeating-linear-gradient(-45deg,#7a5a3c 0 4px,#a88a6c 4px 5px)"></i>흙</span>');
    if (this.L.wall) key.push(`<span><i style="background:${hex(CAP)}"></i>옹벽</span>`);
    dock.open('cut', `
      <div class="row"><span class="t">자르기</span><span class="v" id="cutT"></span><span class="grow"></span><button class="x" id="cutX" title="자르기 끝">✕</button></div>
      <div class="row"><button id="cutPrev" title="이전 측점">◀</button><input type="range" id="cutS" min="${A.s0}" max="${A.s1}" step="any"><button id="cutNext" title="다음 측점">▶</button></div>
      <div class="row lg">${key.join('')}</div>`,
    () => this.end());
    const $ = (id) => document.getElementById(id);
    const sl = $('cutS');
    sl.value = String(this.s);
    let raf = 0;
    sl.oninput = () => { cancelAnimationFrame(raf); raf = requestAnimationFrame(() => this.at({ s: +sl.value })); };
    const jump = (dir) => {
      const sts = A.st;
      let k = -1;
      if (dir > 0) k = sts.findIndex(t => t.s > this.s + 0.01);
      else for (let i = sts.length - 1; i >= 0; i--) if (sts[i].s < this.s - 0.01) { k = i; break; }
      if (k >= 0) { this.at(k); sl.value = String(this.s); }
    };
    $('cutPrev').onclick = () => jump(-1);
    $('cutNext').onclick = () => jump(1);
    $('cutX').onclick = () => dock.close();
    this._label();
  }

  _label() {
    const el = document.getElementById('cutT');
    if (!el) return;
    const A = this.L.align;
    const hit = A.st.find(t => Math.abs(t.s - this.s) < 0.01);
    el.textContent = `${hit ? hit.label : stationName(this.s, A.index)} · ${fmt(this.s, 2)} m`;
  }
}
