// 누르면 정보 · 재기 · 두 번 누르면 가운데로.
import * as THREE from 'three';

const fmt = (v, d = 2) => Number.isFinite(v) ? v.toLocaleString('ko-KR', { minimumFractionDigits: d, maximumFractionDigits: d }) : '—';
const slopeText = (g) => {   // g = 높이/수평(무차원)
  if (!Number.isFinite(g)) return '—';
  const pct = g * 100;
  const n = g > 1e-6 ? 1 / g : Infinity;
  return `${fmt(pct, 1)}% · 1:${Number.isFinite(n) ? (n >= 100 ? '∞' : fmt(n, 2)) : '∞'}`;
};

export class Picker {
  constructor(view, L, head, ui) {
    this.view = view; this.L = L; this.head = head; this.ui = ui;
    this.ray = new THREE.Raycaster();
    this.mode = 'info';
    this.pts = [];
    this.origin = head.origin || [0, 0];
    this.walls = new Map((head.walls || []).map(w => [w.id, w]));

    // 표시 — 찍은 점(작은 공 · 화면 크기 고정) · 재기 선
    this.marks = new THREE.Group();
    this.marks.renderOrder = 10;
    this.marks.userData = { mark: true, noClip: true };   // 사진 · 영상에서 숨김 · 자르기에 안 잘림
    this.enabled = true;          // 시공 재생 중에는 끔(계획 검토 중간 3)
    this.clip = () => null;       // 3D 자르기 면 — 숨은 쪽 맞음은 건너뜀
    this.stationActs = null;      // (측점) => [{ text, fn }] — 측점 풍선 단추(main이 채움)
    view.scene.add(this.marks);
    this.dotGeo = new THREE.SphereGeometry(1, 16, 12);
    this.dotMat = new THREE.MeshBasicMaterial({ color: 0xff7a00, depthTest: false, transparent: true });
    this.lineMat = new THREE.LineBasicMaterial({ color: 0xff7a00, depthTest: false, transparent: true });
    view.onDraw(() => this._scaleDots());

    const el = view.renderer.domElement;
    let down = null, rdown = null;
    el.addEventListener('pointerdown', e => {
      if (e.button === 0) down = { x: e.clientX, y: e.clientY, t: performance.now() };
      if (e.button === 2) rdown = { x: e.clientX, y: e.clientY };
    });
    el.addEventListener('pointerup', e => {
      if (e.button !== 0 || !down) return;
      const moved = Math.hypot(e.clientX - down.x, e.clientY - down.y);
      const quick = performance.now() - down.t < 600;
      down = null;
      if (moved < 5 && quick) this.click(e.clientX, e.clientY);
    });
    el.addEventListener('dblclick', e => {
      if (!this.enabled) return;
      const h = this.hit(e.clientX, e.clientY);
      if (h) view.centerOn(h.point);
    });
    // 오른쪽 누름(끌지 않고)만 재기 끝 — 오른쪽 끌기는 화면 옮기기라 재던 것을 지우면 안 된다
    el.addEventListener('contextmenu', e => {
      e.preventDefault();
      const still = rdown && Math.hypot(e.clientX - rdown.x, e.clientY - rdown.y) < 5;
      rdown = null;
      if (still && this.mode === 'measure' && this.pts.length) this.endMeasure();
    });
  }

  // 화면 점 → 보이는 물체 첫 맞음(지형의 숨긴 옹벽 면은 건너뜀)
  hit(cx, cy) {
    const rect = this.view.renderer.domElement.getBoundingClientRect();
    const ndc = new THREE.Vector2(((cx - rect.left) / rect.width) * 2 - 1, -((cy - rect.top) / rect.height) * 2 + 1);
    this.ray.setFromCamera(ndc, this.view.camera);
    const objs = [];
    for (const k of ['drain', 'terrain', 'ground', 'excav', 'wall', 'pattern']) {
      const l = this.L[k];
      if (l && l.obj && l.obj.visible) objs.push(l.obj);
    }
    const hits = this.ray.intersectObjects(objs, true);
    const cp = this.clip();
    for (const h of hits) {
      if (!h.object.visible) continue;
      if (cp && cp.distanceToPoint(h.point) < -1e-3) continue;          // 잘라 숨긴 쪽
      const u = h.object.userData;
      if (u.layer === 'drain' && u.kind === 'stream') continue;          // 물길은 화면 거리로 따로(굵기 몇 px라 광선이 잘 못 맞힘)
      if (u.layer === 'terrain') {
        // 숨긴 구분(옹벽 면 · 원지반 보기의 설계 자리)은 건너뛴다 — 광선은 재료가 숨었는지 모른다
        const ci = this._classIndex(h.faceIndex);
        const mat = Array.isArray(h.object.material) ? h.object.material[ci] : h.object.material;
        if (ci < 0 || (mat && !mat.visible)) continue;
        h.cls = this.L.terrain.classOf[ci];
      }
      return h;
    }
    return null;
  }

  _classIndex(face) {
    const cs = this.L.terrain.classOf;
    for (let i = 0; i < cs.length; i++) if (face >= cs[i].start && face < cs[i].start + cs[i].count) return i;
    return -1;
  }

  // 화면 좌표(px) — 사진기 뒤면 null
  _scr(x, y, z) {
    const v = this._v || (this._v = new THREE.Vector3());
    const cam = this.view.camera;
    v.set(x, y, z).applyMatrix4(cam.matrixWorldInverse);
    if (v.z > -cam.near) return null;
    v.applyMatrix4(cam.projectionMatrix);
    const r = this._rect;
    return [(v.x + 1) / 2 * r.width + r.left, (1 - v.y) / 2 * r.height + r.top];
  }

  // 선분 묶음 중 화면에서 (cx, cy)에 가장 가까운 것(tol px 안) — 순번
  _nearSeg(seg, cx, cy, tol) {
    let best = -1, bd = tol * tol;
    const cp = this.clip(), mid = new THREE.Vector3();
    for (let i = 0, k = 0; i + 5 < seg.length; i += 6, k++) {
      if (cp && cp.distanceToPoint(mid.set((seg[i] + seg[i + 3]) / 2, (seg[i + 1] + seg[i + 4]) / 2, (seg[i + 2] + seg[i + 5]) / 2)) < 0) continue;
      const a = this._scr(seg[i], seg[i + 1], seg[i + 2]), b = a && this._scr(seg[i + 3], seg[i + 4], seg[i + 5]);
      if (!a || !b) continue;
      const dx = b[0] - a[0], dy = b[1] - a[1], L2 = dx * dx + dy * dy;
      const t = L2 > 0 ? Math.max(0, Math.min(1, ((cx - a[0]) * dx + (cy - a[1]) * dy) / L2)) : 0;
      const ex = a[0] + dx * t - cx, ey = a[1] + dy * t - cy, d2 = ex * ex + ey * ey;
      if (d2 < bd) { bd = d2; best = k; }
    }
    return best;
  }

  // 측점 가로선 · 물길 — 화면 거리로(10 px · 8 px 안)
  _nearLine(cx, cy) {
    this._rect = this.view.renderer.domElement.getBoundingClientRect();
    this.view.camera.updateMatrixWorld();
    const A = this.L.align;
    if (A && A.obj.visible) {
      for (const st of A.st) if (this._nearSeg(st.seg, cx, cy, 10) >= 0) return { station: st };
    }
    const D = this.L.drain;
    if (D && D.obj.visible && D.pickSeg.length) {
      const k = this._nearSeg(D.pickSeg, cx, cy, 8);
      if (k >= 0) return { stream: D.pickAcc[k] };
    }
    return null;
  }

  click(cx, cy) {
    if (!this.enabled) return;
    if (this.mode === 'info') {
      const near = this._nearLine(cx, cy);
      if (near) {
        this.clearMarks();
        if (near.station) this.ui.showInfo(cx, cy, this.describeStation(near.station));
        else this.ui.showInfo(cx, cy, { title: '물길', rows: [['모인 넓이', fmt(near.stream, 0) + ' ㎡']] });
        this.view.request();
        return;
      }
    }
    const h = this.hit(cx, cy);
    if (this.mode === 'measure') {
      if (!h) return;
      const last = this.pts[this.pts.length - 1];
      if (last && last.distanceTo(h.point) < 1e-3) return;   // 두 번 누르기 — 같은 점을 또 넣지 않는다
      this.pts.push(h.point.clone());
      this._drawMeasure();
      return;
    }
    this.clearMarks();
    if (!h) { this.ui.hideInfo(); return; }
    this._dot(h.point);
    this.ui.showInfo(cx, cy, this.describe(h));
    this.view.request();
  }

  describe(h) {
    const p = h.point, X = p.x + this.origin[0], Y = p.y + this.origin[1];
    const u = h.object.userData;
    const rows = [];
    let title = '', go = null;
    if (u.layer === 'drain') {
      if (u.kind === 'pond') {
        const pd = this.L.drain.ponds[u.faceOf[h.faceIndex]];
        title = '고임';
        if (pd) {
          rows.push(['고인 깊이', fmt(pd.depth, 2) + ' m']);
          rows.push(['넓이', fmt(pd.area, 0) + ' ㎡']);
          rows.push(['물 부피', fmt(pd.vol, 0) + ' ㎥']);
          rows.push(['물 높이', fmt(pd.level, 3) + ' m']);
        }
      } else {
        title = '평지';
        rows.push(['경사', '0.5 % 미만']);
        rows.push(['평지 전체', fmt(this.L.drain.flatArea, 0) + ' ㎡']);
      }
      go = { x: X, y: Y, r: 15 };
    } else if (u.layer === 'terrain' || u.layer === 'ground' || u.layer === 'excav') {
      title = u.layer === 'terrain' ? (h.cls ? h.cls.name : '지형') : (u.layer === 'ground' ? '원지반' : '터파기');
      rows.push(['표고', fmt(p.z + (u.zShift || 0), 3) + ' m']);
      const n = h.face.normal.clone().transformDirection(h.object.matrixWorld);
      const nz = Math.abs(n.z);
      rows.push(['경사', nz > 1e-6 ? slopeText(Math.sqrt(Math.max(0, 1 - nz * nz)) / nz) : '수직']);
      if (u.layer === 'terrain') {
        const d = this._cutFillAt(h);
        if (Number.isFinite(d) && Math.abs(d) >= 0.01) rows.push([d < 0 ? '절토 깊이' : '성토 높이', fmt(Math.abs(d), 2) + ' m']);
      }
      go = { x: X, y: Y, r: 15 };
    } else if (u.layer === 'wall' || u.layer === 'pattern') {
      const w = this.walls.get(u.wall);
      title = w ? w.name : '옹벽';
      if (w) {
        rows.push(['형식', `${w.kind}${w.up === true ? ' · 절토' : w.up === false ? ' · 성토' : ''}`]);
        const s = this._station(w, p);
        if (s) {
          rows.push(['높이', fmt(s.h, 2) + ' m']);
          rows.push(['측점', `시점에서 ${fmt(s.s, 1)} m`]);
          if (s.row >= 0) rows.push(['표 줄', String(s.row + 1)]);
        }
        if (u.layer === 'pattern') rows.push(['무늬', u.pat.replace('옹벽_무늬_', '')]);
        else if (u.mat) rows.push(['재료', u.mat]);
        const b = w.box;
        go = b ? { x0: b[0] + this.origin[0], y0: b[1] + this.origin[1], x1: b[2] + this.origin[0], y1: b[3] + this.origin[1], wall: w.id } : { x: X, y: Y, r: 20 };
      }
      rows.push(['표고', fmt(p.z, 3) + ' m']);
    }
    rows.push(['좌표', `${fmt(X, 3)}, ${fmt(Y, 3)}`]);
    return { title, rows, go };
  }

  describeStation(st) {
    const rows = [['측점', fmt(st.s, 3) + ' m'], ['횡단도', st.views > 0 ? `${st.views}장` : '없음']];
    return { title: st.label, rows, acts: this.stationActs ? this.stationActs(st) : [] };
  }

  // 지형 맞은 자리의 절성토 깊이 — 원래(합친 점) 삼각형 세 점의 값을 무게중심 좌표로
  _cutFillAt(h) {
    const T = this.head.terrain;
    if (!T || !T.cf) return NaN;
    const f = h.faceIndex, i0 = T.idx[f * 3], i1 = T.idx[f * 3 + 1], i2 = T.idx[f * 3 + 2];
    const P = T.pos, v = (i) => new THREE.Vector3(P[i * 3], P[i * 3 + 1], P[i * 3 + 2]);
    const tri = new THREE.Triangle(v(i0), v(i1), v(i2));
    const bc = tri.getBarycoord(h.point, new THREE.Vector3());
    if (!bc) return NaN;
    const a = T.cf[i0], b = T.cf[i1], c = T.cf[i2];
    if (![a, b, c].every(Number.isFinite)) return NaN;
    return a * bc.x + b * bc.y + c * bc.z;
  }

  // 옹벽 정거장(x, y, S, H, 표 줄) 중 가장 가까운 것
  _station(w, p) {
    const s = w.sta;
    if (!s || s.length < 5) return null;
    let best = -1, bd = Infinity;
    for (let i = 0; i < s.length; i += 5) {
      const d = (s[i] - p.x) ** 2 + (s[i + 1] - p.y) ** 2;
      if (d < bd) { bd = d; best = i; }
    }
    return best < 0 ? null : { s: s[best + 2], h: s[best + 3], row: Math.round(s[best + 4]) };
  }

  setMode(m) {
    this.mode = m;
    this.endMeasure(true);
    this.clearMarks();
    this.ui.hideInfo();
    this.view.request();
  }

  endMeasure(silent) {
    this.pts = [];
    if (!silent) { this.clearMarks(); this.ui.hideMeasure(); this.view.request(); }
  }

  clearMarks() {
    for (const c of [...this.marks.children]) { this.marks.remove(c); if (c.geometry && c.geometry !== this.dotGeo) c.geometry.dispose(); }
  }

  _dot(p) {
    const m = new THREE.Mesh(this.dotGeo, this.dotMat);
    m.position.copy(p);
    m.renderOrder = 11;
    this.marks.add(m);
  }

  _scaleDots() {
    const cam = this.view.camera;
    const k = Math.tan(THREE.MathUtils.degToRad(cam.fov / 2)) * 2 / Math.max(1, this.view.host.clientHeight);
    for (const c of this.marks.children) if (c.geometry === this.dotGeo) c.scale.setScalar(Math.max(0.01, c.position.distanceTo(cam.position) * k * 4));
  }

  _drawMeasure() {
    this.clearMarks();
    for (const p of this.pts) this._dot(p);
    if (this.pts.length >= 2) {
      const g = new THREE.BufferGeometry().setFromPoints(this.pts);
      const line = new THREE.Line(g, this.lineMat);
      line.renderOrder = 10;
      this.marks.add(line);
    }
    // 마지막 두 점의 값 + 전체 길이
    const n = this.pts.length;
    if (n >= 2) {
      const a = this.pts[n - 2], b = this.pts[n - 1];
      const dh = Math.hypot(b.x - a.x, b.y - a.y), dz = b.z - a.z, d3 = a.distanceTo(b);
      let total = 0;
      for (let i = 1; i < n; i++) total += this.pts[i - 1].distanceTo(this.pts[i]);
      this.ui.showMeasure({
        rows: [
          ['사거리', fmt(d3, 3) + ' m'],
          ['수평거리', fmt(dh, 3) + ' m'],
          ['높이차', (Math.abs(dz) < 0.0005 ? '' : dz > 0 ? '+' : '−') + fmt(Math.abs(dz), 3) + ' m'],
          ['경사', dh > 1e-6 ? slopeText(Math.abs(dz) / dh) : '수직'],
          ...(n > 2 ? [['누적 사거리', fmt(total, 3) + ' m']] : []),
        ],
      });
    } else this.ui.showMeasure({ rows: [['첫 점', `표고 ${fmt(this.pts[0].z, 3)} m`]] });
    this.view.request();
  }

  // 시험 훅 — 두 점(도면 좌표)을 재어 값만 돌려준다
  measureWorld(a, b) {
    const A = new THREE.Vector3(a[0] - this.origin[0], a[1] - this.origin[1], a[2]);
    const B = new THREE.Vector3(b[0] - this.origin[0], b[1] - this.origin[1], b[2]);
    return { d3: A.distanceTo(B), dh: Math.hypot(B.x - A.x, B.y - A.y), dz: B.z - A.z };
  }
}
