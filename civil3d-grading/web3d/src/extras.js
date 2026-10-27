// ★★[v108 · 계획_v108.md 2.1 · 2.6 · 2.7] 덧 층 — 빗물(물길 · 고임 · 평지) · 지적(선 · 지번) · 측점(중심선 · 가로선 · 이름).
import * as THREE from 'three';
import { fatLines, polySeg, textSprite, nearLabels } from './lines.js';

// 물길 굵기 칸 — [모인 넓이 하한 ㎡, 굵기 CSS px]
const STREAM_W = [[0, 1.8], [1000, 2.8], [3000, 4.0], [10000, 5.5]];

// 측점 이름 — C# StationNaming.Fmt와 같은 꼴(No.5 · No.5+12.34 · 판정은 반올림 · 표기는 내림)
export function stationName(s, index) {
  if (!(index > 1e-6)) return s.toFixed(2);
  const near = Math.round(s / index);
  if (Math.abs(s - near * index) < 0.005) return `No.${near}`;
  const no = Math.floor(s / index + 1e-9);
  const plus = s - no * index;
  return `No.${no}+${plus.toFixed(2).padStart(5, '0')}`;
}

function dotTexture(view) {
  const cv = document.createElement('canvas');
  cv.width = cv.height = 64;
  const g = cv.getContext('2d');
  g.fillStyle = 'rgba(125,215,255,0.95)';
  g.beginPath(); g.arc(32, 32, 10, 0, Math.PI * 2); g.fill();
  const t = new THREE.CanvasTexture(cv);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = view.renderer.capabilities.getMaxAnisotropy();
  return t;
}

// ── 빗물 ── 물길(굵기 = 모인 넓이) · 고임(물 높이 평면 · 반투명) · 평지(하늘색 점무늬)
export function buildDrain(view, head, L) {
  const D = head.drain;
  if (!D) return;
  const grp = new THREE.Group();
  // 물길 — 선분마다 내려간 쪽 점의 모인 넓이로 굵기 칸 · 누르기용으로 선분 · 모인 넓이를 따로 둔다
  const byW = STREAM_W.map(() => []);
  const pickSeg = [], pickAcc = [];
  for (const s of D.streams || []) {
    const p = s.pos, a = s.acc;
    for (let i = 0; i + 1 < a.length; i++) {
      const acc = a[i + 1];
      let c = 0;
      for (let k = STREAM_W.length - 1; k >= 0; k--) if (acc >= STREAM_W[k][0]) { c = k; break; }
      const seg = [p[i * 3], p[i * 3 + 1], p[i * 3 + 2], p[i * 3 + 3], p[i * 3 + 4], p[i * 3 + 5]];
      for (const v of seg) byW[c].push(v);
      for (const v of seg) pickSeg.push(v);
      pickAcc.push(acc);
    }
  }
  byW.forEach((sg, c) => {
    if (!sg.length) return;
    const l = fatLines(view, sg, 0x1e7bff, STREAM_W[c][1]);
    l.userData = { layer: 'drain', kind: 'stream' };
    grp.add(l);
  });
  // 고임 — 칸 줄(x0, y0, x1, y1)마다 네모 · 물 높이 + 2 cm
  const ponds = D.ponds || [];
  const pPos = [], pIdx = [], faceOf = [];
  ponds.forEach((pd, pi) => {
    const r = pd.runs, z = pd.level + 0.02;
    for (let i = 0; i + 3 < r.length; i += 4) {
      const v = pPos.length / 3;
      pPos.push(r[i], r[i + 1], z, r[i + 2], r[i + 1], z, r[i + 2], r[i + 3], z, r[i], r[i + 3], z);
      pIdx.push(v, v + 1, v + 2, v, v + 2, v + 3);
      faceOf.push(pi, pi);
    }
  });
  if (pIdx.length) {
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(new Float32Array(pPos), 3));
    g.setIndex(pIdx);
    g.computeVertexNormals();
    const m = new THREE.MeshStandardMaterial({ color: 0x2f86d6, transparent: true, opacity: 0.62, roughness: 0.12, metalness: 0.0, depthWrite: false });
    const mesh = new THREE.Mesh(g, m);
    mesh.renderOrder = 1;
    mesh.userData = { layer: 'drain', kind: 'pond', faceOf };
    grp.add(mesh);
  }
  // 평지 — 칸 줄(x0, y0, x1, y1, z0, z1) · 점무늬 1.2 m마다 · 땅 + 4 cm
  const F = D.flats;
  if (F && F.length >= 6) {
    const pos = [], uv = [], idx = [];
    for (let i = 0; i + 5 < F.length; i += 6) {
      const x0 = F[i], y0 = F[i + 1], x1 = F[i + 2], y1 = F[i + 3], z0 = F[i + 4] + 0.04, z1 = F[i + 5] + 0.04;
      const v = pos.length / 3;
      pos.push(x0, y0, z0, x1, y0, z1, x1, y1, z1, x0, y1, z0);
      for (const [x, y] of [[x0, y0], [x1, y0], [x1, y1], [x0, y1]]) uv.push(x / 1.2, y / 1.2);
      idx.push(v, v + 1, v + 2, v, v + 2, v + 3);
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(new Float32Array(pos), 3));
    g.setAttribute('uv', new THREE.BufferAttribute(new Float32Array(uv), 2));
    g.setIndex(idx);
    const m = new THREE.MeshBasicMaterial({ map: dotTexture(view), transparent: true, depthWrite: false });
    const mesh = new THREE.Mesh(g, m);
    mesh.renderOrder = 1;
    mesh.userData = { layer: 'drain', kind: 'flat' };
    grp.add(mesh);
  }
  grp.visible = false;
  view.scene.add(grp);
  const nS = (D.streams || []).length;
  L.drain = {
    obj: grp, label: '빗물', on: false, ponds, flatArea: D.flatArea || 0, nStreams: nS,
    pickSeg: new Float32Array(pickSeg), pickAcc,
    summary: `물길 ${nS}줄 · 고임 ${ponds.length}곳 · 평지 ${Math.round(D.flatArea || 0).toLocaleString('ko-KR')} ㎡`,
  };
}

// ── 지적 ── 노란 선 · 지번(가까이 갈 때만) · 없으면 층을 회색 + 까닭
export function buildCad(view, head, L) {
  const C = head.cad;
  if (!C || !C.seg || !C.seg.length) {
    L.cad = { label: '지적', on: false, missing: '지적이 도면에 없습니다 — [지적도 가져오기] 뒤 [새로고침]' };
    return;
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(C.seg, 3));
  const lines = new THREE.LineSegments(g, new THREE.LineBasicMaterial({ color: 0xffd400 }));
  lines.renderOrder = 3;
  const grp = new THREE.Group();
  grp.add(lines);
  const labels = (C.labels || []).map(lb => textSprite(lb.t, lb.x, lb.y, lb.z + 0.5));
  for (const s of labels) { s.userData.noClip = true; grp.add(s); }
  grp.visible = false;
  view.scene.add(grp);
  view.onDraw(() => { if (grp.visible) nearLabels(labels, view.camera, 350, 150, view.renderer.domElement.height / view.renderer.getPixelRatio()); });
  L.cad = { obj: grp, label: '지적', on: false };
}

// ── 측점 ── 중심선(빨강) · 측점 가로선(흰색 · 정측점 굵게 · 땅을 따라) · 이름(가까이 갈 때)
export function buildStations(view, head, L) {
  const A = head.align;
  if (!A || !A.st || !A.st.length) return;
  const grp = new THREE.Group();
  const center = polySeg(A.line);
  if (center.length) grp.add(fatLines(view, center, 0xff4d4d, 3));
  const minor = [], major = [], labels = [];
  const sts = A.st.map((s, i) => {
    const pts = s.pts && s.pts.length >= 6 ? s.pts : [...s.l, ...s.c, ...s.r];
    const seg = polySeg(pts);
    const into = s.major ? major : minor;
    for (const v of seg) into.push(v);
    const cz = s.c[2] > -9999 ? s.c[2] : NaN;
    if (Number.isFinite(cz)) {
      const sp = textSprite(s.label, s.c[0], s.c[1], cz + 2.0, s.major ? '#ffffff' : '#e6e9ee', s.major ? 0.026 : 0.022);
      sp.userData.st = i;
      sp.userData.pri = s.major ? 1 : 0;      // 정측점 이름부터 놓는다
      labels.push(sp);
    }
    return { ...s, i, seg: new Float32Array(seg) };
  });
  if (minor.length) grp.add(fatLines(view, minor, 0xf2f4f7, 2.2));
  if (major.length) grp.add(fatLines(view, major, 0xffffff, 3.6));
  for (const s of labels) grp.add(s);
  grp.traverse(o => { o.userData.noClip = true; });
  view.scene.add(grp);
  view.onDraw(() => { if (grp.visible) nearLabels(labels, view.camera, 700, 60, view.renderer.domElement.height / view.renderer.getPixelRatio()); });
  // 중심선 점의 측점 값 — s0부터 step마다 · 마지막 점 = 끝 측점 s1(마지막 토막은 step보다 짧을 수 있음)
  const n = A.line.length / 3, step = A.step || 2, s0 = A.s0 || 0;
  const s1 = Number.isFinite(A.s1) ? A.s1 : s0 + step * (n - 1);
  L.align = { obj: grp, label: '측점', on: true, name: A.name, st: sts, labels, line: A.line, s0, s1, step, index: A.index || 20, views: A.views || 0 };
}

// 중심선 위 측점 s의 점 · 진행 방향(수평 단위) — 3D 자르기 면
export function alignAt(A, s) {
  const n = A.line.length / 3;
  if (n < 2) return null;
  // 점 j의 측점 = s0 + step·j · 마지막 점만 s1
  const sj = (j) => (j >= n - 1 ? A.s1 : A.s0 + A.step * j);
  const i = Math.max(0, Math.min(n - 2, Math.floor((s - A.s0) / A.step)));
  const t = Math.max(0, Math.min(1, (s - sj(i)) / Math.max(1e-9, sj(i + 1) - sj(i))));
  const P = (j) => new THREE.Vector3(A.line[j * 3], A.line[j * 3 + 1], A.line[j * 3 + 2]);
  const a = P(i), b = P(i + 1);
  const p = a.clone().lerp(b, t);
  if (!Number.isFinite(p.z)) p.z = Number.isFinite(a.z) ? a.z : b.z;
  const dir = new THREE.Vector3(b.x - a.x, b.y - a.y, 0).normalize();
  return { p, dir };
}
