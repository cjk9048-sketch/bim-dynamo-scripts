// ★★[v108 · 계획_v108.md 2.3 · 계획 검토 중간 7] 해 그림자 — NOAA 해 위치(날짜 · 한국 시각 UTC+9 · 현장 위경도) → 해 방향 · 높이.
//   동지로 시작 · [동지 9–15시 ▶] [하지] [지금] · 그림자 상자는 현장 + 그림자 길이를 늘 덮고 지형도 그림자를 드리운다(낮은 해 · 뒷산).
import * as THREE from 'three';

const rad = THREE.MathUtils.degToRad, deg = THREE.MathUtils.radToDeg;

// NOAA 해 계산(일반 해 계산 스프레드시트와 같은 식) — 방위(북 = 0 · 시계 방향) · 높이(도 · 대기 굴절 보정)
export function sunPosition(utcMs, lat, lon) {
  const jd = utcMs / 86400000 + 2440587.5;
  const jc = (jd - 2451545) / 36525;
  const L0 = ((280.46646 + jc * (36000.76983 + jc * 0.0003032)) % 360 + 360) % 360;
  const M = 357.52911 + jc * (35999.05029 - 0.0001537 * jc);
  const e = 0.016708634 - jc * (0.000042037 + 0.0000001267 * jc);
  const C = Math.sin(rad(M)) * (1.914602 - jc * (0.004817 + 0.000014 * jc)) + Math.sin(rad(2 * M)) * (0.019993 - 0.000101 * jc) + Math.sin(rad(3 * M)) * 0.000289;
  const omega = 125.04 - 1934.136 * jc;
  const lambda = L0 + C - 0.00569 - 0.00478 * Math.sin(rad(omega));
  const eps0 = 23 + (26 + (21.448 - jc * (46.815 + jc * (0.00059 - jc * 0.001813))) / 60) / 60;
  const eps = eps0 + 0.00256 * Math.cos(rad(omega));
  const decl = Math.asin(Math.sin(rad(eps)) * Math.sin(rad(lambda)));
  const y = Math.tan(rad(eps / 2)) ** 2;
  const eqTime = 4 * deg(y * Math.sin(2 * rad(L0)) - 2 * e * Math.sin(rad(M)) + 4 * e * y * Math.sin(rad(M)) * Math.cos(2 * rad(L0))
    - 0.5 * y * y * Math.sin(4 * rad(L0)) - 1.25 * e * e * Math.sin(2 * rad(M)));
  const d = new Date(utcMs);
  const utcMin = d.getUTCHours() * 60 + d.getUTCMinutes() + d.getUTCSeconds() / 60;
  const tst = ((utcMin + eqTime + 4 * lon) % 1440 + 1440) % 1440;
  const ha = tst / 4 < 0 ? tst / 4 + 180 : tst / 4 - 180;
  const phi = rad(lat);
  const cosZ = Math.min(1, Math.max(-1, Math.sin(phi) * Math.sin(decl) + Math.cos(phi) * Math.cos(decl) * Math.cos(rad(ha))));
  const zen = Math.acos(cosZ);
  let el = 90 - deg(zen);
  // 대기 굴절(NOAA)
  const te = Math.tan(rad(el));
  let refr = 0;
  if (el > 85) refr = 0;
  else if (el > 5) refr = 58.1 / te - 0.07 / te ** 3 + 0.000086 / te ** 5;
  else if (el > -0.575) refr = 1735 + el * (-518.2 + el * (103.4 + el * (-12.79 + el * 0.711)));
  else refr = -20.772 / te;
  el += refr / 3600;
  const azA = deg(Math.acos(Math.min(1, Math.max(-1, (Math.sin(phi) * Math.cos(zen) - Math.sin(decl)) / (Math.cos(phi) * Math.sin(zen))))));
  const az = ha > 0 ? (azA + 180) % 360 : (540 - azA) % 360;
  return { az, el };
}

// 한국 날짜(YYYY-MM-DD) · 시각(분) → UTC 밀리초
const kstMs = (date, min) => { const [Y, M, D] = date.split('-').map(Number); return Date.UTC(Y, M - 1, D, 0, 0) + (min - 9 * 60) * 60000; };
const pad = (n) => String(n).padStart(2, '0');
const hm = (min) => `${pad(Math.floor(min / 60))}:${pad(Math.round(min % 60))}`;

export class Sun {
  constructor(view, L, head, focus) {
    this.view = view; this.L = L; this.head = head;
    this.geo = head.geo || null;
    this.focus = focus;
    const y = new Date().getFullYear();
    this.date = `${y}-12-22`;
    this.min = 12 * 60;
    this.on = false;
    this._play = 0;
  }

  get available() { return !!this.geo; }

  // 지금 날짜 · 시각의 해 → 빛 · 그림자 상자
  apply() {
    const { az, el } = sunPosition(kstMs(this.date, this.min), this.geo.lat, this.geo.lon);
    const ca = Math.cos(rad(el));
    const dir = new THREE.Vector3(Math.sin(rad(az)) * ca, Math.cos(rad(az)) * ca, Math.sin(rad(el)));
    // 해가 지평선 아래면 빛은 0(하늘빛만 어둡게) · 낮게 뜨면 약하고 붉게
    const k = THREE.MathUtils.smoothstep(el, -1, 8);
    if (dir.z < 0.02) dir.z = 0.02;           // 그림자 사진기가 땅 밑으로 가지 않게(빛은 0)
    this.view.setSun(dir.normalize(), k);
    // 그림자 상자 — 현장 + 그림자 길이(높이 차 ÷ tan 해 높이 · 600 m까지)
    const f = this.focus;
    const c = new THREE.Vector3((f[0] + f[3]) / 2, (f[1] + f[4]) / 2, (f[2] + f[5]) / 2);
    const R = Math.hypot(f[3] - f[0], f[4] - f[1]) / 2;
    const len = Math.min(600, Math.max(10, f[5] - f[2]) / Math.tan(rad(Math.max(4, el))));
    this.view.shadowFocus = { c, r: Math.min(2000, R * 1.1 + len) };
    this.az = az; this.el = el;
    this._label();
    this.view.request();
    return { az, el };
  }

  enter() {
    if (this.on || !this.available) return;
    this.on = true;
    this._shadow0 = this.view.sun.castShadow;
    this.view.sun.castShadow = true;
    // 지형도 그림자를 드리운다(낮은 해 · 뒷산 · 깎은 사면)
    for (const id of ['terrain', 'ground']) if (this.L[id] && this.L[id].obj) this.L[id].obj.castShadow = true;
    this.apply();
  }

  exit() {
    if (!this.on) return;
    this.stop();
    this.on = false;
    for (const id of ['terrain', 'ground']) if (this.L[id] && this.L[id].obj) this.L[id].obj.castShadow = false;
    this.view.sun.castShadow = this._shadow0;
    this.view.shadowFocus = null;
    this.view.setSun(null);
  }

  set(date, min) {
    if (date) this.date = date;
    if (Number.isFinite(min)) this.min = Math.max(0, Math.min(24 * 60 - 1, min));
    if (this.on) this.apply();
    this._sync();
  }

  // 동지 9–15시 — 10초에 걸쳐
  playWinter() {
    this.stop();
    this.date = `${new Date().getFullYear()}-12-22`;
    const t0 = performance.now(), dur = 10000;
    const step = () => {
      const t = Math.min(1, (performance.now() - t0) / dur);
      this.set(null, 9 * 60 + t * 6 * 60);
      if (t < 1 && this.on) this._play = requestAnimationFrame(step); else { this._play = 0; this._btn(); }
    };
    this._play = requestAnimationFrame(step);
    this._btn();
  }

  stop() { if (this._play) cancelAnimationFrame(this._play); this._play = 0; this._btn(); }
  _btn() { const b = document.getElementById('sunPlay'); if (b) b.textContent = this._play ? '❚❚ 멈춤' : '동지 9–15시 ▶'; }

  _sync() {
    const d = document.getElementById('sunDate'), h = document.getElementById('sunH');
    if (d && d.value !== this.date) d.value = this.date;
    if (h) h.value = String(Math.round(this.min));
  }

  _label() {
    const el = document.getElementById('sunT');
    if (!el) return;
    const [, M, D] = this.date.split('-').map(Number);
    el.textContent = `${M}월 ${D}일 ${hm(this.min)} · 해 높이 ${this.el < 0 ? '해 진 뒤' : Math.round(this.el) + '°'} · 방위 ${Math.round(this.az)}°`;
  }

  panel(dock) {
    dock.open('sun', `
      <div class="row"><span class="t">해 그림자</span><span class="v" id="sunT"></span><span class="grow"></span><button class="x" id="sunX" title="닫기">✕</button></div>
      <div class="row"><input type="date" id="sunDate"><input type="range" id="sunH" min="300" max="1260" step="5"></div>
      <div class="row"><button class="b" id="sunPlay">동지 9–15시 ▶</button><button class="b" id="sunSummer">하지</button><button class="b" id="sunNow">지금</button></div>`,
    () => this.exit());
    const $ = (id) => document.getElementById(id);
    $('sunDate').onchange = () => { if ($('sunDate').value) { this.stop(); this.set($('sunDate').value); } };
    $('sunH').oninput = () => { this.stop(); this.set(null, +$('sunH').value); };
    $('sunPlay').onclick = () => (this._play ? this.stop() : this.playWinter());
    $('sunSummer').onclick = () => { this.stop(); this.set(`${new Date().getFullYear()}-06-21`); };
    $('sunNow').onclick = () => {
      this.stop();
      const n = new Date(Date.now() + 9 * 3600000);   // 한국 시각
      this.set(`${n.getUTCFullYear()}-${pad(n.getUTCMonth() + 1)}-${pad(n.getUTCDate())}`, n.getUTCHours() * 60 + n.getUTCMinutes());
    };
    $('sunX').onclick = () => dock.close();
    this.enter();
    this._sync();
    this._label();
  }
}
