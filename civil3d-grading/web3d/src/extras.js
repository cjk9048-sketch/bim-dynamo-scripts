// ★★[v108 · 계획_v108.md 2.1 · 2.6 · 2.7] 덧 층 — 빗물(물길 · 고임 · 평지) · 지적(선 · 지번) · 측점(중심선 · 가로선 · 이름).
import * as THREE from 'three';
import { fatLines, polySeg, textSprite, nearLabels, screenDash } from './lines.js';

// 물길 굵기 칸 — [모인 넓이 하한 ㎡, 굵기 CSS px]
const STREAM_W = [[0, 1.8], [1000, 2.8], [3000, 4.0], [10000, 5.5]];
const FILL_S = 4;          // 고임이 차오르는 시간(초)

// 잔물결 법선 지도(이어 붙여도 이음매 없음 — 정수 파수 사인 합) — 고인 물 표면이 움직이게
function rippleNormals() {
  const N = 128, h = new Float32Array(N * N);
  const waves = [[1, 2, 0.0], [3, 1, 1.3], [2, -3, 2.1], [5, 2, 0.7], [-4, 5, 2.9], [7, -3, 1.1], [-6, -7, 0.4]];
  for (let y = 0; y < N; y++)
    for (let x = 0; x < N; x++) {
      let v = 0;
      for (const [kx, ky, ph] of waves) v += Math.sin(2 * Math.PI * (kx * x + ky * y) / N + ph) / Math.hypot(kx, ky);
      h[y * N + x] = v;
    }
  const cv = document.createElement('canvas');
  cv.width = cv.height = N;
  const g = cv.getContext('2d'), img = g.createImageData(N, N);
  const H = (x, y) => h[((y + N) % N) * N + ((x + N) % N)];
  for (let y = 0; y < N; y++)
    for (let x = 0; x < N; x++) {
      const dx = (H(x + 1, y) - H(x - 1, y)) * 2, dy = (H(x, y + 1) - H(x, y - 1)) * 2, l = Math.hypot(dx, dy, 1);
      const k = (y * N + x) * 4;
      img.data[k] = (-dx / l * 0.5 + 0.5) * 255; img.data[k + 1] = (-dy / l * 0.5 + 0.5) * 255; img.data[k + 2] = (1 / l * 0.5 + 0.5) * 255; img.data[k + 3] = 255;
    }
  g.putImageData(img, 0, 0);
  const t = new THREE.CanvasTexture(cv);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  return t;
}

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
  // ★[JACK 1028 «빗물이 고여 움직이는 것처럼»] 물길 위에 밝은 마디가 내려가는 쪽으로 흐른다(물길 점 = 위 → 아래 · 큰 물길일수록 빠르게)
  const flows = [];
  byW.forEach((sg, c) => {
    if (!sg.length) return;
    const l = fatLines(view, sg, 0x1e7bff, STREAM_W[c][1]);
    l.userData = { layer: 'drain', kind: 'stream' };
    grp.add(l);
    const f = fatLines(view, sg, 0xe2f6ff, Math.max(1.6, STREAM_W[c][1] * 0.8), { dash: [8, 16], renderOrder: 3 });
    f.userData = { layer: 'drain', kind: 'stream' };
    f.raycast = () => {};
    grp.add(f);
    flows.push({ m: f.material, v: 22 + 9 * c });
  });
  screenDash(view, flows.map(f => f.m), () => grp.visible);
  // 고임 — 칸 줄(x0, y0, x1, y1)마다 네모 · 물 높이 + 2 cm · 켜면 바닥부터 차오르고(aDepth만큼 내렸다 올림) 잔물결이 움직인다
  const ponds = D.ponds || [];
  const pPos = [], pIdx = [], pDep = [], faceOf = [];
  ponds.forEach((pd, pi) => {
    const r = pd.runs, z = pd.level + 0.02, dep = Math.max(0.02, pd.depth || 0) + 0.02;
    for (let i = 0; i + 3 < r.length; i += 4) {
      const v = pPos.length / 3;
      pPos.push(r[i], r[i + 1], z, r[i + 2], r[i + 1], z, r[i + 2], r[i + 3], z, r[i], r[i + 3], z);
      pDep.push(dep, dep, dep, dep);
      pIdx.push(v, v + 1, v + 2, v, v + 2, v + 3);
      faceOf.push(pi, pi);
    }
  });
  const water = { uFill: { value: 1 }, uFlow: { value: new THREE.Vector4() } };
  if (pIdx.length) {
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(new Float32Array(pPos), 3));
    g.setAttribute('aDepth', new THREE.BufferAttribute(new Float32Array(pDep), 1));
    // 물결 무늬 좌표 = 도면 x, y ÷ 8 m
    const uv = new Float32Array((pPos.length / 3) * 2);
    for (let i = 0, j = 0; i < pPos.length; i += 3, j += 2) { uv[j] = pPos[i] / 8; uv[j + 1] = pPos[i + 1] / 8; }
    g.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
    g.setIndex(pIdx);
    g.computeVertexNormals();
    const m = new THREE.MeshStandardMaterial({ color: 0x2f86d6, transparent: true, opacity: 0.66, roughness: 0.08, metalness: 0.0, depthWrite: false,
      normalMap: rippleNormals(), normalScale: new THREE.Vector2(0.45, 0.45) });
    // 법선 지도를 서로 다른 방향 · 크기로 두 번 흘려 섞는다(한 장만 밀면 무늬가 통째로 미끄러져 보임)
    m.onBeforeCompile = (sh) => {
      Object.assign(sh.uniforms, water);
      sh.vertexShader = 'attribute float aDepth;\nuniform float uFill;\n' + sh.vertexShader.replace('#include <begin_vertex>', '#include <begin_vertex>\n\ttransformed.z -= (1.0 - uFill) * aDepth;');
      sh.fragmentShader = 'uniform vec4 uFlow;\n' + sh.fragmentShader.replace('#include <normal_fragment_maps>', `
	vec3 n1 = texture2D( normalMap, vNormalMapUv + uFlow.xy ).xyz * 2.0 - 1.0;
	vec3 n2 = texture2D( normalMap, vNormalMapUv * 1.37 + uFlow.zw ).xyz * 2.0 - 1.0;
	vec3 mapN = normalize( vec3( n1.xy + n2.xy, n1.z * n2.z ) );
	mapN.xy *= normalScale;
	normal = normalize( tbn * mapN );`);
    };
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
  // 움직임 — 층이 보일 때만(안 보이면 다시 그리지 않음) · replay() = 고임을 바닥부터 다시 채움
  let t0 = -1;
  view.addAnim((t) => {
    if (!grp.visible) return false;
    if (t0 < 0) t0 = t;
    for (const f of flows) f.m.dashOffset = -f.v * t;
    water.uFlow.value.set(t * 0.021, t * 0.013, -t * 0.016, t * 0.024);
    const k = Math.min(1, (t - t0) / FILL_S);
    water.uFill.value = 1 - (1 - k) * (1 - k);
    return true;
  });
  const nS = (D.streams || []).length;
  L.drain = {
    obj: grp, label: '빗물', on: false, ponds, flatArea: D.flatArea || 0, nStreams: nS,
    pickSeg: new Float32Array(pickSeg), pickAcc,
    summary: `물길 ${nS}줄 · 고임 ${ponds.length}곳 · 평지 ${Math.round(D.flatArea || 0).toLocaleString('ko-KR')} ㎡`,
    replay: () => { t0 = -1; water.uFill.value = 0; view.request(); },
  };
}

// ── 경사 ── ★[JACK 1028 «경사도 · 작은 화살표 · 그라데이션» · 인터뷰: 완만~사면 한 줄 · 정지한 자리만]
//   정지한 자리(계획면 · 소단 · 사면 · 정지 — 원지반 · 옹벽 자리 · 옹벽 면 빼고)의 고른 간격 점마다 그 자리 세모의 기울기:
//   화살표 = 내리막(물 흐르는 쪽) · 면에 눕힘 · 색 = 경사(로그 눈금 이음) · 0.5 % 미만 = 파란 점(방향 없음).
export const SLOPE_STOPS = [[0.005, '#3d7bff'], [0.02, '#2ecc71'], [0.05, '#f2e23a'], [0.10, '#ffc93a'], [0.30, '#ff8a1f'], [0.667, '#e8392e']];
const SLOPE_KEEP = new Set(['plan', 'bench', 'slopeCut', 'slopeFill', 'other']);
const _stopCol = SLOPE_STOPS.map(([g, c]) => [Math.log10(g), new THREE.Color(c)]);
export function slopeColor(g, out) {
  const lg = Math.log10(Math.max(1e-6, g));
  if (lg <= _stopCol[0][0]) return out.copy(_stopCol[0][1]);
  for (let k = 1; k < _stopCol.length; k++)
    if (lg <= _stopCol[k][0]) return out.copy(_stopCol[k - 1][1]).lerp(_stopCol[k][1], (lg - _stopCol[k - 1][0]) / (_stopCol[k][0] - _stopCol[k - 1][0]));
  return out.copy(_stopCol[_stopCol.length - 1][1]);
}

// 화살표(길이 1 · +x 쪽 · 면 = xy) · 점(지름 0.5)
function arrowGeo() {
  const s = new THREE.Shape();
  s.moveTo(-0.5, -0.07); s.lineTo(0.12, -0.07); s.lineTo(0.12, -0.21); s.lineTo(0.5, 0); s.lineTo(0.12, 0.21); s.lineTo(0.12, 0.07); s.lineTo(-0.5, 0.07); s.closePath();
  return new THREE.ShapeGeometry(s);
}

export function buildSlope(view, head, L) {
  const T = head.terrain;
  if (!T || !T.pos || !T.idx || !T.ranges || !T.classes) return;
  const P = T.pos, I = T.idx;
  const tris = [];
  for (const [ci, start, count] of T.ranges) if (SLOPE_KEEP.has(T.classes[ci].key)) for (let f = start; f < start + count; f++) tris.push(f);
  if (!tris.length) return;
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity, area = 0;
  for (const f of tris) {
    const a = I[f * 3] * 3, b = I[f * 3 + 1] * 3, c = I[f * 3 + 2] * 3;
    for (const v of [a, b, c]) { x0 = Math.min(x0, P[v]); x1 = Math.max(x1, P[v]); y0 = Math.min(y0, P[v + 1]); y1 = Math.max(y1, P[v + 1]); }
    area += Math.abs((P[b] - P[a]) * (P[c + 1] - P[a + 1]) - (P[c] - P[a]) * (P[b + 1] - P[a + 1])) / 2;
  }
  // 간격 2 m — 넓은 현장은 화살표가 2만 개를 넘지 않게 넓힌다
  const step = Math.max(2, Math.sqrt(area / 20000));
  const nx = Math.max(1, Math.ceil((x1 - x0) / step)), ny = Math.max(1, Math.ceil((y1 - y0) / step));
  // 칸 색인 — 세모를 그 상자가 덮는 칸마다(점 = 칸 가운데 · 그 칸 것만 본다)
  const bins = new Map();
  for (const f of tris) {
    const a = I[f * 3] * 3, b = I[f * 3 + 1] * 3, c = I[f * 3 + 2] * 3;
    const i0 = Math.floor((Math.min(P[a], P[b], P[c]) - x0) / step), i1 = Math.floor((Math.max(P[a], P[b], P[c]) - x0) / step);
    const j0 = Math.floor((Math.min(P[a + 1], P[b + 1], P[c + 1]) - y0) / step), j1 = Math.floor((Math.max(P[a + 1], P[b + 1], P[c + 1]) - y0) / step);
    for (let j = Math.max(0, j0); j <= Math.min(ny - 1, j1); j++)
      for (let i = Math.max(0, i0); i <= Math.min(nx - 1, i1); i++) {
        const k = j * nx + i;
        let arr = bins.get(k);
        if (!arr) bins.set(k, arr = []);
        arr.push(f);
      }
  }
  const arrows = [], dots = [];
  for (const [k, list] of bins) {
    const px = x0 + (k % nx + 0.5) * step, py = y0 + (Math.floor(k / nx) + 0.5) * step;
    for (const f of list) {
      const a = I[f * 3] * 3, b = I[f * 3 + 1] * 3, c = I[f * 3 + 2] * 3;
      const ux = P[b] - P[a], uy = P[b + 1] - P[a + 1], uz = P[b + 2] - P[a + 2], vx = P[c] - P[a], vy = P[c + 1] - P[a + 1], vz = P[c + 2] - P[a + 2];
      const det = ux * vy - vx * uy;
      if (Math.abs(det) < 1e-9) continue;                     // 세운 세모(위에서 넓이 0)
      const wx = px - P[a], wy = py - P[a + 1];
      const s = (wx * vy - vx * wy) / det, t = (ux * wy - wx * uy) / det;
      if (s < -1e-9 || t < -1e-9 || s + t > 1 + 1e-9) continue;
      let nX = uy * vz - uz * vy, nY = uz * vx - ux * vz, nZ = ux * vy - uy * vx;
      if (nZ < 0) { nX = -nX; nY = -nY; nZ = -nZ; }
      const pz = P[a + 2] + s * uz + t * vz, g = Math.hypot(nX, nY) / nZ;
      (g < 0.005 ? dots : arrows).push([px, py, pz, nX, nY, nZ, g]);
      break;
    }
  }
  const grp = new THREE.Group();
  const size = step * 0.78, col = new THREE.Color(), m4 = new THREE.Matrix4();
  const X = new THREE.Vector3(), Y = new THREE.Vector3(), Z = new THREE.Vector3(), O = new THREE.Vector3();
  // 놓기 — 면에 눕혀(+z = 면 법선) 법선 쪽으로 띄움 · 화살표 +x = 내리막
  const place = (p, sc, lift) => {
    Z.set(p[3], p[4], p[5]).normalize();
    if (p[6] >= 0.005) X.set(p[3], p[4], 0).normalize().setZ(-p[6]).normalize();
    else X.set(1, 0, 0).addScaledVector(Z, -Z.x).normalize();
    Y.crossVectors(Z, X);
    O.set(p[0], p[1], p[2]).addScaledVector(Z, lift);
    return m4.makeBasis(X.multiplyScalar(sc), Y.multiplyScalar(sc), Z).setPosition(O);
  };
  const make = (geo, list, scale, lift, fixedColor, order) => {
    if (!list.length) return;
    const m = new THREE.MeshBasicMaterial({ side: THREE.DoubleSide, toneMapped: false, depthWrite: fixedColor == null });
    if (fixedColor != null) m.color.set(fixedColor);
    const im = new THREE.InstancedMesh(geo, m, list.length);
    list.forEach((p, i) => {
      im.setMatrixAt(i, place(p, size * scale, lift));
      if (fixedColor == null) im.setColorAt(i, slopeColor(p[6], col));
    });
    im.instanceMatrix.needsUpdate = true;
    if (im.instanceColor) im.instanceColor.needsUpdate = true;
    im.computeBoundingSphere();
    im.renderOrder = order;
    im.raycast = () => {};                 // 누르면 아래 지형(경사 · 표고)
    grp.add(im);
  };
  const ag = arrowGeo(), dg = new THREE.CircleGeometry(0.16, 12);
  // 어두운 테(조금 크게 · 깊이 안 씀) 위에 색 — 흙 · 잔디 · 위성 어디서나 또렷이
  make(ag, arrows, 1.18, 0.10, 0x14181c, 2);
  make(ag, arrows, 1.0, 0.12, null, 3);
  make(dg, dots, 1.35, 0.10, 0x14181c, 2);
  make(dg, dots, 1.0, 0.12, null, 3);
  grp.visible = false;
  view.scene.add(grp);
  L.slope = { obj: grp, label: '경사', on: false, n: arrows.length + dots.length, step };
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

// ── 측점 ── 중심선(빨강) · 측점 가로선(흰 점선 · 정측점 굵게 · 땅을 따라) · 이름(가까이 갈 때) · 마우스 올린 측점은 주황 실선
const ST_DASH = [8, 5];    // 측점 점선 긋기 · 띄기(화면 px)
const ST_HOV = 0xffa31a;   // 마우스 올린 측점
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
  // ★[JACK 1028 «측점 선은 점선 · 조금 더 얇게»] 2.2 · 3.6 px 실선 → 1.5 · 2.4 px 점선
  const dashMats = [];
  for (const [seg, color, w] of [[minor, 0xf2f4f7, 1.5], [major, 0xffffff, 2.4]]) {
    if (!seg.length) continue;
    const l = fatLines(view, seg, color, w, { dash: ST_DASH });
    dashMats.push(l.material);
    grp.add(l);
  }
  screenDash(view, dashMats, () => grp.visible);
  for (const s of labels) grp.add(s);
  grp.traverse(o => { o.userData.noClip = true; });
  view.scene.add(grp);
  view.onDraw(() => { if (grp.visible) nearLabels(labels, view.camera, 700, 60, view.renderer.domElement.height / view.renderer.getPixelRatio()); });
  // ★[JACK 1028 «측점 위에 마우스 올리면 하이라이트»] 그 측점만 주황 실선(굵게) + 이름 주황(멀어도 보임) · 사진 · 영상에는 안 찍힘(mark)
  let hov = null, hovSp = null;
  const highlight = (st) => {
    if (hov) { grp.remove(hov); hov.geometry.dispose(); hov.material.dispose(); hov = null; }
    if (hovSp) { hovSp.material.color.set(0xffffff); hovSp.userData.force = false; hovSp = null; }
    if (!st) return;
    if (st.seg.length) {
      hov = fatLines(view, st.seg, ST_HOV, 3.4, { renderOrder: 3 });
      hov.userData = { noClip: true, mark: true };
      hov.raycast = () => {};
      grp.add(hov);
    }
    hovSp = labels.find(s => s.userData.st === st.i) || null;
    if (hovSp) { hovSp.material.color.set(ST_HOV); hovSp.userData.force = true; }
  };
  // 중심선 점의 측점 값 — s0부터 step마다 · 마지막 점 = 끝 측점 s1(마지막 토막은 step보다 짧을 수 있음)
  const n = A.line.length / 3, step = A.step || 2, s0 = A.s0 || 0;
  const s1 = Number.isFinite(A.s1) ? A.s1 : s0 + step * (n - 1);
  L.align = { obj: grp, label: '측점', on: true, name: A.name, st: sts, labels, line: A.line, s0, s1, step, index: A.index || 20, views: A.views || 0, highlight };
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
