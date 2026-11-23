// ★[JACK 1123 «키보드 방향키로 장면 이동이 되면 좋겠어 · 보고 있는 방향을 인식해서 가야겠지»]
// 3D 보기(궤도 보기)에서 ↑ ↓ = 보는 방향 앞뒤 · ← → = 옆으로 · Shift = 빠르게 — 사진기와 궤도 중심을 함께 수평으로 옮긴다(높이 · 보는 각도는 그대로).
// 시야 검토(걷기) 중에는 walk.js가 방향키를 먼저 받아 막으므로 여기 오지 않는다.
import * as THREE from 'three';

/** 한 걸음 — 순수 셈(시험용). cam · target · screenUp = {x,y,z} · f = 앞(+1)/뒤(−1) · s = 오른쪽(+1)/왼쪽(−1) · step = 거리(m).
 *  앞 = 사진기 → 궤도 중심의 수평 방향. 거의 수직으로 내려다볼 때는 화면 위쪽(screenUp)의 수평 방향. 돌려줌 = {x, y} 옮길 양. */
export function panStep(cam, target, screenUp, f, s, step) {
  let fx = target.x - cam.x, fy = target.y - cam.y;
  let n = Math.hypot(fx, fy);
  const dz = Math.abs(target.z - cam.z);
  if (n < 1e-6 || n < dz * 1e-3) { fx = screenUp.x; fy = screenUp.y; n = Math.hypot(fx, fy); }
  if (n < 1e-9 || (!f && !s)) return { x: 0, y: 0 };
  fx /= n; fy /= n;
  const rx = fy, ry = -fx;                       // Z 위 — 앞을 시계 방향 90° = 오른쪽
  let mx = f * fx + s * rx, my = f * fy + s * ry;
  const m = Math.hypot(mx, my);
  if (m > 1e-9) { mx /= m; my /= m; }            // 대각선도 같은 빠르기
  return { x: mx * step, y: my * step };
}

const BASE = 0.55;    // 1초에 «보는 거리 × 0.55»만큼
const FAST = 3;       // Shift

export class KeyMove {
  constructor(view, onMove) {
    this.view = view;
    this.onMove = onMove || (() => {});
    this.keys = new Set();
    this.enabled = true;
    this._loop = 0;
    const mine = new Set(['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight']);
    const typing = (el) => el && (el.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(el.tagName || ''));
    window.addEventListener('keydown', (e) => {
      if (!this.enabled || !mine.has(e.code) || e.ctrlKey || e.metaKey || e.altKey || typing(document.activeElement)) return;
      this.keys.add(e.code);
      this.shift = e.shiftKey;
      e.preventDefault();
      this._start();
    });
    window.addEventListener('keyup', (e) => {
      this.shift = e.shiftKey;
      if (mine.has(e.code)) { this.keys.delete(e.code); e.preventDefault(); }
    });
    window.addEventListener('blur', () => this.keys.clear());
    document.addEventListener('visibilitychange', () => { if (document.hidden) this.keys.clear(); });
  }

  _start() {
    if (this._loop) return;
    let last = performance.now();
    const v = new THREE.Vector3();
    const tick = (now) => {
      const K = this.keys;
      if (!K.size || !this.enabled) { this._loop = 0; return; }
      this._loop = requestAnimationFrame(tick);
      const dt = Math.min(0.1, (now - last) / 1000); last = now;
      const f = (K.has('ArrowUp') ? 1 : 0) - (K.has('ArrowDown') ? 1 : 0);
      const s = (K.has('ArrowRight') ? 1 : 0) - (K.has('ArrowLeft') ? 1 : 0);
      if (!f && !s) return;
      const cam = this.view.camera, ctl = this.view.controls;
      const dist = Math.max(1, cam.position.distanceTo(ctl.target));
      const up = v.set(0, 1, 0).applyQuaternion(cam.quaternion);
      const d = panStep(cam.position, ctl.target, up, f, s, dist * BASE * (this.shift ? FAST : 1) * dt);
      if (!d.x && !d.y) return;
      cam.position.x += d.x; cam.position.y += d.y;
      ctl.target.x += d.x; ctl.target.y += d.y;
      ctl.update();
      this.view.request();
      this.onMove();
    };
    this._loop = requestAnimationFrame(tick);
  }
}
