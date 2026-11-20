// [시야 검토] — 사람 눈높이(지표면 + 1.70 m)로 걷듯이 둘러보기.
//   단추 → 서 있을 자리 클릭 → 그 자리로 날아가(0.6 s) 지금 보던 방향 · 수평으로 → ↑↓ · W S 걷기(1.4 m/s · Shift ×3) · ←→ · A D 돌기(60°/s) · Q E 또는 Alt+←→ 옆걸음 ·
//   마우스 끌기 = 둘러보기 · Esc 또는 단추 다시 = 처음 궤도 보기로 복귀.
//   땅 높이 = 지형 세모를 가로 격자에 담아 (x, y) 위의 세모 높이를 직접 구한다(광선 없음 · 한 장에 한 번 · 세모 수와 무관하게 가볍다).
import * as THREE from 'three';

export const EYE = 1.70;          // 눈높이(m)
export const WALK_SPEED = 1.4;    // m/s
export const RUN_K = 3;
export const TURN_DEG = 60;       // °/s
export const WALK_FOV = 65;
const AIM = 100;                  // 보는 점까지 거리 — 그림자 상자 · 안개가 «멀리 보는» 값으로 잡히게(실제 보는 방향과는 무관)

// ── 순수 부분(three 없음 · 시험이 직접 부른다) ──

/** 세모(pos = x,y,z 차례 · idx = 세모 점 번호)를 칸(cell m)에 담는다. 꼴: { x0, y0, cell, nx, ny, start(Int32Array), list(Int32Array), pos, idx } */
export function buildGrid(pos, idx, cell = 8) {
  const nt = (idx ? idx.length : pos.length / 3) / 3 | 0;
  const vi = (k) => (idx ? idx[k] : k);
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
  for (let k = 0; k < nt * 3; k++) {
    const v = vi(k), x = pos[v * 3], y = pos[v * 3 + 1];
    if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
  }
  if (!(x1 >= x0)) return { x0: 0, y0: 0, cell, nx: 0, ny: 0, start: new Int32Array(1), list: new Int32Array(0), pos, idx, nt: 0 };
  const nx = Math.max(1, Math.ceil((x1 - x0) / cell) + 1), ny = Math.max(1, Math.ceil((y1 - y0) / cell) + 1);
  const cnt = new Int32Array(nx * ny + 1);
  const span = (t) => {
    let ax = Infinity, ay = Infinity, bx = -Infinity, by = -Infinity;
    for (let j = 0; j < 3; j++) {
      const v = vi(t * 3 + j), x = pos[v * 3], y = pos[v * 3 + 1];
      if (x < ax) ax = x; if (x > bx) bx = x; if (y < ay) ay = y; if (y > by) by = y;
    }
    return [Math.floor((ax - x0) / cell), Math.floor((ay - y0) / cell), Math.floor((bx - x0) / cell), Math.floor((by - y0) / cell)];
  };
  for (let t = 0; t < nt; t++) { const [i0, j0, i1, j1] = span(t); for (let j = j0; j <= j1; j++) for (let i = i0; i <= i1; i++) cnt[j * nx + i + 1]++; }
  for (let c = 0; c < nx * ny; c++) cnt[c + 1] += cnt[c];
  const fill = cnt.slice(0, nx * ny), list = new Int32Array(cnt[nx * ny]);
  for (let t = 0; t < nt; t++) { const [i0, j0, i1, j1] = span(t); for (let j = j0; j <= j1; j++) for (let i = i0; i <= i1; i++) list[fill[j * nx + i]++] = t; }
  return { x0, y0, cell, nx, ny, start: cnt, list, pos, idx, nt };
}

/** (x, y) 위 땅 높이 — 세모 안이면 그 높이 · 여러 겹이면 ref(앞서 서 있던 땅 높이)에 가장 가까운 것(ref 없으면 가장 높은 것) · 밖이면 null. */
export function heightAt(g, x, y, ref = NaN) {
  if (!g || !g.nx) return null;
  const i = Math.floor((x - g.x0) / g.cell), j = Math.floor((y - g.y0) / g.cell);
  if (i < 0 || j < 0 || i >= g.nx || j >= g.ny) return null;
  const { pos, idx, list, start } = g;
  const vi = (k) => (idx ? idx[k] : k);
  const useRef = Number.isFinite(ref);
  let best = null;
  for (let q = start[j * g.nx + i]; q < start[j * g.nx + i + 1]; q++) {
    const t = list[q], a = vi(t * 3) * 3, b = vi(t * 3 + 1) * 3, c = vi(t * 3 + 2) * 3;
    const ax = pos[a], ay = pos[a + 1], bx = pos[b], by = pos[b + 1], cx = pos[c], cy = pos[c + 1];
    const d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
    if (Math.abs(d) < 1e-12) continue;                      // 위에서 보면 선(수직 면)
    const u = ((by - cy) * (x - cx) + (cx - bx) * (y - cy)) / d, v = ((cy - ay) * (x - cx) + (ax - cx) * (y - cy)) / d, w = 1 - u - v;
    const e = -1e-9;
    if (u < e || v < e || w < e) continue;
    const z = u * pos[a + 2] + v * pos[b + 2] + w * pos[c + 2];
    if (best === null || (useRef ? Math.abs(z - ref) < Math.abs(best - ref) : z > best)) best = z;
  }
  return best;
}

/** 한 걸음 — st = { x, y, yaw(rad · 북 = 0 · 시계), gz(앞선 땅 높이) } · fwd · side(m, 앞 · 오른쪽이 +) → 땅이 있으면 st를 옮기고 true · 지형 끝이면 그대로 false. 눈높이 = st.gz + EYE. */
export function stepOn(g, st, fwd, side = 0) {
  const sy = Math.sin(st.yaw), cy = Math.cos(st.yaw);
  const nx = st.x + sy * fwd + cy * side, ny = st.y + cy * fwd - sy * side;
  const z = heightAt(g, nx, ny, st.gz);
  if (z === null) return false;
  st.x = nx; st.y = ny; st.gz = z;
  return true;
}

// ── 화면 부분 ──

export class Walk {
  constructor(view, L, picker, btn) {
    this.view = view; this.L = L; this.picker = picker; this.btn = btn;
    this.mode = 'off';            // off · pick · fly · walk
    this.keys = new Set();
    this.alt = false;
    this.st ={ x: 0, y: 0, yaw: 0, gz: 0 };
    this.pitch = 0;
    this._grid = null; this._gridKey = null;
    this.onChange = () => { };
    const el = view.renderer.domElement;
    this.el = el;
    if (!el.hasAttribute('tabindex')) el.tabIndex = 0;

    this.hint = document.createElement('div');
    this.hint.id = 'walkHint';
    this.hint.className = 'pnl';
    this.hint.style.display = 'none';
    document.body.appendChild(this.hint);

    btn.onclick = () => (this.mode === 'off' ? this.start() : this.stop());

    // 키 — 활동 중에는 여기서 다 받고 Civil · 다른 단축키로 새지 않게 막는다
    window.addEventListener('keydown', (e) => this._key(e, true), true);
    window.addEventListener('keyup', (e) => this._key(e, false), true);
    window.addEventListener('blur', () => { this.keys.clear(); this.alt = false; });
    document.addEventListener('visibilitychange', () => { if (document.hidden) { this.keys.clear(); this.alt = false; } });

    // 서 있을 자리 클릭 · 끌어 둘러보기
    let down = null;
    window.addEventListener('pointerdown', (e) => {
      if (this.mode === 'off' || e.target !== el) return;
      down = { x: e.clientX, y: e.clientY, t: performance.now(), px: e.clientX, py: e.clientY };
      el.focus({ preventScroll: true });
      if (this.mode === 'walk') { try { el.setPointerCapture(e.pointerId); } catch { /* 없어도 됨 */ } }
    }, true);
    window.addEventListener('pointermove', (e) => {
      if (this.mode !== 'walk' || !down || !e.buttons) return;
      this.st.yaw += (e.clientX - down.px) * 0.2 * Math.PI / 180;
      this.pitch = Math.max(-80, Math.min(80, this.pitch * 180 / Math.PI - (e.clientY - down.py) * 0.2)) * Math.PI / 180;   // 도 단위로 자른 뒤 라디안
      down.px = e.clientX; down.py = e.clientY;
      this._apply();
    }, true);
    window.addEventListener('pointerup', (e) => {
      if (this.mode === 'off' || !down) return;
      const moved = Math.hypot(e.clientX - down.x, e.clientY - down.y), quick = performance.now() - down.t < 600;
      down = null;
      if (this.mode === 'pick' && e.button === 0 && moved < 5 && quick) this._pickAt(e.clientX, e.clientY);
    }, true);
  }

  get active() { return this.mode !== 'off'; }

  // ── 땅 ──
  _terrainObj() {
    const g = this.L.ground && this.L.ground.on && this.L.ground.obj.visible ? this.L.ground : this.L.terrain;
    return g && g.obj ? g.obj : null;
  }
  _gridNow() {
    const o = this._terrainObj();
    if (!o) return null;
    const geo = o.geometry;
    if (this._gridKey !== geo) {
      const p = geo.getAttribute('position');
      this._grid = buildGrid(p.array, geo.index ? geo.index.array : null, 8);
      this._gridKey = geo;
    }
    return this._grid;
  }

  // ── 시작 · 끝 ──
  start() {
    if (!this._terrainObj()) return false;
    this._setMode('pick');
    this.picker.hover && this.picker.hover(null);
    this._picker0 = this.picker.enabled; this.picker.enabled = false;
    this.el.style.cursor = 'crosshair';
    this.el.focus({ preventScroll: true });
    this._hint('서 있을 자리를 클릭 (Esc=취소)');
    return true;
  }

  _pickAt(cx, cy) {
    const o = this._terrainObj();
    if (!o) return;
    const r = this.el.getBoundingClientRect();
    const ray = new THREE.Raycaster();
    ray.setFromCamera(new THREE.Vector2(((cx - r.left) / r.width) * 2 - 1, -((cy - r.top) / r.height) * 2 + 1), this.view.camera);
    const hit = ray.intersectObject(o, false).find(h => h.object.visible);
    if (!hit) return;
    this.enterAt(hit.point.x, hit.point.y, false);
  }

  /** (x, y)에 서기 — instant면 날아가지 않고 바로(시험) · 땅이 없으면 false. */
  enterAt(x, y, instant) {
    const g = this._gridNow();
    const z = heightAt(g, x, y);
    if (z === null) return false;
    const v = this.view, cam = v.camera;
    if (this.mode === 'off') { this._picker0 = this.picker.enabled; this.picker.enabled = false; }
    if (this.mode !== 'walk' && this.mode !== 'fly') {
      this.saved = { p: cam.position.clone(), t: v.controls.target.clone(), fov: cam.fov, ctl: v.controls.enabled };
    }
    // 보던 방향 그대로(방위) · 수평
    const hd = v.heading() * Math.PI / 180;
    this.st = { x, y, yaw: hd, gz: z };
    this.pitch = 0;
    v.controls.enabled = false;
    this.el.style.cursor = '';
    this._hint('시야 검토 · 방향키 이동 · Shift 빠르게 · Esc 끝');
    if (instant) { this._setMode('walk'); cam.fov = WALK_FOV; this._apply(); this._startLoop(); return true; }
    this._setMode('fly');
    const p0 = cam.position.clone(), t0 = v.controls.target.clone(), fov0 = cam.fov, T0 = performance.now(), dur = 600;
    const p1 = this._eye(), t1 = this._aim();
    const step = () => {
      if (this.mode !== 'fly') return;
      const k = Math.min(1, (performance.now() - T0) / dur), e = k * k * (3 - 2 * k);
      cam.position.lerpVectors(p0, p1, e);
      v.controls.target.lerpVectors(t0, t1, e);
      cam.lookAt(v.controls.target);
      cam.fov = fov0 + (WALK_FOV - fov0) * e; cam.updateProjectionMatrix();
      v.request();
      if (k < 1) requestAnimationFrame(step);
      else { this._setMode('walk'); this._apply(); this._startLoop(); }
    };
    step();
    return true;
  }

  stop() {
    if (this.mode === 'off') return;
    const v = this.view, cam = v.camera;
    if (this.saved) {
      cam.position.copy(this.saved.p);
      v.controls.target.copy(this.saved.t);
      cam.fov = this.saved.fov; cam.updateProjectionMatrix();
      cam.lookAt(v.controls.target);
      v.controls.enabled = this.saved.ctl;
      v.controls.update();
    }
    this.saved = null;
    if (this._picker0 !== undefined) this.picker.enabled = this._picker0;
    this._picker0 = undefined;
    this.el.style.cursor = '';
    this.keys.clear(); this.alt = false;
    this._hint('');
    this._setMode('off');
    v.request();
  }

  _setMode(m) { this.mode = m; this.btn.classList.toggle('on', m !== 'off'); this.onChange(m); }
  _hint(t) { this.hint.textContent = t; this.hint.style.display = t ? 'block' : 'none'; }

  // ── 카메라 ──
  _eye() { return new THREE.Vector3(this.st.x, this.st.y, this.st.gz + EYE); }
  _dir() {
    const cp = Math.cos(this.pitch);
    return new THREE.Vector3(Math.sin(this.st.yaw) * cp, Math.cos(this.st.yaw) * cp, Math.sin(this.pitch));
  }
  _aim() { return this._eye().addScaledVector(this._dir(), AIM); }
  _apply() {
    const v = this.view, cam = v.camera;
    cam.position.copy(this._eye());
    v.controls.target.copy(this._aim());
    cam.lookAt(v.controls.target);
    if (cam.fov !== WALK_FOV) { cam.fov = WALK_FOV; cam.updateProjectionMatrix(); }
    v.request();
  }

  // ── 걷기 ──
  /** 앞(m)으로 걷기 — 시험도 같은 길을 쓴다. 지형 끝이면 false. */
  walkBy(fwd, side = 0) {
    const ok = stepOn(this._gridNow(), this.st, fwd, side);
    if (ok) this._apply();
    return ok;
  }

  _startLoop() {
    if (this._loop) return;
    let last = performance.now();
    const tick = (now) => {
      if (this.mode !== 'walk') { this._loop = 0; return; }
      this._loop = requestAnimationFrame(tick);
      const dt = Math.min(0.1, (now - last) / 1000); last = now;
      const K = this.keys, sh = K.has('ShiftLeft') || K.has('ShiftRight') ? RUN_K : 1;
      const f = (K.has('ArrowUp') || K.has('KeyW') ? 1 : 0) - (K.has('ArrowDown') || K.has('KeyS') ? 1 : 0);
      let turn = (K.has('ArrowRight') || K.has('KeyD') ? 1 : 0) - (K.has('ArrowLeft') || K.has('KeyA') ? 1 : 0);
      let side = (K.has('KeyE') ? 1 : 0) - (K.has('KeyQ') ? 1 : 0);
      if (this.alt) { side += turn; turn = 0; }
      if (!f && !turn && !side) return;
      if (turn) this.st.yaw += turn * TURN_DEG * Math.PI / 180 * dt;
      if (f || side) stepOn(this._gridNow(), this.st, f * WALK_SPEED * sh * dt, side * WALK_SPEED * sh * dt);
      this._apply();
    };
    this._loop = requestAnimationFrame(tick);
  }

  _key(e, down) {
    if (this.mode === 'off') return;
    // 자리(e.code) 기준 — 한글 입력 · Caps Lock · 자판 배열과 상관없이 · Alt는 글쇠마다 e.altKey로(Alt를 뗀 것을 놓쳐도 안 남음)
    const k = e.code;
    this.alt = e.altKey;
    if (k === 'Escape' || e.key === 'Escape') { if (down) this.stop(); e.preventDefault(); e.stopPropagation(); return; }
    if (k === 'AltLeft' || k === 'AltRight') { e.stopPropagation(); return; }
    const mine = ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'KeyW', 'KeyA', 'KeyS', 'KeyD', 'KeyQ', 'KeyE', 'ShiftLeft', 'ShiftRight'];
    if (!mine.includes(k)) { if (down && !e.ctrlKey && !e.metaKey) e.stopPropagation(); return; }   // 다른 글쇠도 Civil 쪽으로 안 새게
    if (down) this.keys.add(k); else this.keys.delete(k);
    if (k !== 'ShiftLeft' && k !== 'ShiftRight') { e.preventDefault(); }
    e.stopPropagation();
  }
}
