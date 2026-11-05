// ★[v115 · JACK 1105 임상도] 수목 층 — C#가 심은 나무(H.forest)를 InstancedMesh로.
//   LOD0 = 덩이(250 m) × 모양마다 InstancedMesh(그림자 냄) · LOD1 = 1 km 묶음 × 대리 모양마다 InstancedMesh 하나(호출 수를 줄임 · 재검토 M3).
//   고르기 = view.onDraw(그리기 바로 앞 · 사진 · 영상의 사진기도 따름): 가까운 덩이부터 세모 예산 1.5백만까지 자세히 · 나머지 대리 · max(3 km, 1.5 × 사진기–목표 거리) 밖 숨김.
//   밀도 막대(25~100 %) = 무리마다 해시 순위 앞 ⌈f·n⌉(C#가 순위 차례로 보냄) · 대리 묶음은 원래 무리에서 다시 채움(재검토 N3).
import * as THREE from 'three';
import { treeGeo, proxyGeo, proxyOf, proxyColor, pickLod, hideDistance, keepCount, KIND_COUNT } from './treegeo.js';

export const TRI_BUDGET = 1_500_000;
export const LOD0_MAX = 800;   // m — 이 밖 덩이는 늘 대리 모양
const BLOCK = 1000;

function toGeometry(g) {
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(g.pos, 3));
  geo.setAttribute('color', new THREE.BufferAttribute(g.col, 3));
  geo.computeVertexNormals();
  return geo;
}

// 머리 → 무리 · 덩이 · 행렬(순수 — 시험이 부른다)
export function forestModel(F) {
  const n = F.n, xy = F.xy, z = F.z, hw = F.hw, k = F.k, r = F.r, t = F.t, grp = F.grp, cbox = F.cbox, czr = F.czr;
  const groups = [], chunks = [];
  const m4 = new THREE.Matrix4(), q = new THREE.Quaternion(), s = new THREE.Vector3(), p = new THREE.Vector3(), up = new THREE.Vector3(0, 0, 1);
  const nChunk = cbox.length / 4;
  for (let c = 0; c < nChunk; c++) {
    const zr = czr ? [czr[2 * c], czr[2 * c + 1]] : [0, 0];
    chunks.push({ i: c, box: [cbox[4 * c], cbox[4 * c + 1], zr[0], cbox[4 * c + 2], cbox[4 * c + 3], zr[1]], groups: [], tris0: 0, n: 0 });
  }
  for (let g = 0; g < grp.length / 3; g++) {
    const start = grp[3 * g], count = grp[3 * g + 1], ch = grp[3 * g + 2], kind = k[start];
    const mat = new Float32Array(16 * count), col = new Float32Array(3 * count), pcol = new Float32Array(3 * count);
    const pc = proxyColor(kind);
    for (let j = 0; j < count; j++) {
      const i = start + j;
      const H = hw[2 * i] * 0.2, W = hw[2 * i + 1] * 0.2;
      p.set(xy[2 * i], xy[2 * i + 1], z[i]);
      q.setFromAxisAngle(up, (r[i] / 256) * Math.PI * 2);
      s.set(W, W, H);
      m4.compose(p, q, s);
      m4.toArray(mat, 16 * j);
      const b = 0.92 + 0.16 * (t[i] / 255);   // 밝기 ±8 %
      col[3 * j] = col[3 * j + 1] = col[3 * j + 2] = b;
      pcol[3 * j] = pc[0] * b; pcol[3 * j + 1] = pc[1] * b; pcol[3 * j + 2] = pc[2] * b;
    }
    const G = { start, count, chunk: ch, kind, proxy: proxyOf(kind), mat, col, pcol };
    groups.push(G);
    if (chunks[ch]) { chunks[ch].groups.push(G); chunks[ch].n += count; }
  }
  return { n, groups, chunks };
}

export function buildForest(view, head, L) {
  const F = head.forest;
  if (!F) return;
  if (F.missing || !F.n || !F.xy) {
    // 못 읽음 = 회색 + ⚠ · 범위에 숲 없음(quiet) = 회색만
    L.tree = { label: '수목', on: false, missing: F.missing || '수목 자료가 없습니다', quiet: !!F.quiet };
    return;
  }
  const model = forestModel(F);
  const geos = [], tris = [];
  for (let kd = 0; kd < KIND_COUNT; kd++) { const g = treeGeo(kd); geos.push(toGeometry(g)); tris.push(g.tris); }
  const pgeos = [toGeometry(proxyGeo(0)), toGeometry(proxyGeo(1))], ptris = [proxyGeo(0).tris, proxyGeo(1).tris];
  const mat0 = new THREE.MeshStandardMaterial({ vertexColors: true, flatShading: true, roughness: 0.9, metalness: 0 });
  const mat1 = new THREE.MeshStandardMaterial({ vertexColors: true, flatShading: true, roughness: 0.95, metalness: 0 });
  const root = new THREE.Group();
  root.userData = { layer: 'tree' };
  // LOD0 — 무리마다 하나(행렬 배열을 그대로 씀)
  for (const G of model.groups) {
    const mesh = new THREE.InstancedMesh(geos[G.kind], mat0, G.count);
    mesh.instanceMatrix = new THREE.InstancedBufferAttribute(G.mat, 16);
    mesh.instanceColor = new THREE.InstancedBufferAttribute(G.col, 3);
    mesh.castShadow = true;
    mesh.receiveShadow = false;
    mesh.visible = false;
    mesh.userData = { layer: 'tree', lod: 0 };
    mesh.computeBoundingSphere();
    G.mesh = mesh;
    root.add(mesh);
  }
  for (const c of model.chunks) c.tris0 = c.groups.reduce((a, G) => a + G.count * tris[G.kind], 0);
  // LOD1 — 1 km 묶음 × 대리 모양
  const blocks = new Map();
  for (const c of model.chunks) {
    const bx = Math.floor((c.box[0] + c.box[3]) / 2 / BLOCK), by = Math.floor((c.box[1] + c.box[4]) / 2 / BLOCK), key = `${bx}_${by}`;
    if (!blocks.has(key)) blocks.set(key, { key, chunks: [], meshes: [null, null], sig: '' });
    const B = blocks.get(key);
    B.chunks.push(c);
    c.block = B;
  }
  for (const B of blocks.values()) {
    for (let pz = 0; pz < 2; pz++) {
      const cap = B.chunks.reduce((a, c) => a + c.groups.filter(G => G.proxy === pz).reduce((s, G) => s + G.count, 0), 0);
      if (!cap) continue;
      const mesh = new THREE.InstancedMesh(pgeos[pz], mat1, cap);
      mesh.instanceColor = new THREE.InstancedBufferAttribute(new Float32Array(3 * cap), 3);
      mesh.count = 0;
      mesh.visible = false;
      mesh.castShadow = false;
      mesh.userData = { layer: 'tree', lod: 1 };
      B.meshes[pz] = mesh;
      root.add(mesh);
    }
  }
  view.scene.add(root);

  let density = 1;
  const state = new Array(model.chunks.length).fill('hidden');
  const stats = { lod0: 0, lod1: 0, hidden: model.chunks.length, lod0Trees: 0, lod1Trees: 0, treeTris: 0 };

  // 묶음 다시 채우기 — 대리 상태인 덩이의 무리마다 앞 ⌈f·n⌉(원래 무리에서 · N3)
  const refill = (B, force) => {
    const sig = B.chunks.map(c => (state[c.i] === 'lod1' ? '1' : '0')).join('') + '|' + density;
    if (!force && sig === B.sig) return;
    B.sig = sig;
    for (let pz = 0; pz < 2; pz++) {
      const mesh = B.meshes[pz];
      if (!mesh) continue;
      const M = mesh.instanceMatrix.array, C = mesh.instanceColor.array;
      let o = 0;
      for (const c of B.chunks) {
        if (state[c.i] !== 'lod1') continue;
        for (const G of c.groups) {
          if (G.proxy !== pz) continue;
          const m = keepCount(G.count, density);
          M.set(G.mat.subarray(0, 16 * m), 16 * o);
          C.set(G.pcol.subarray(0, 3 * m), 3 * o);
          o += m;
        }
      }
      mesh.count = o;
      mesh.visible = o > 0;
      mesh.instanceMatrix.needsUpdate = true;
      mesh.instanceColor.needsUpdate = true;
      if (o > 0) mesh.computeBoundingSphere();
    }
  };

  const cam = new THREE.Vector3(), box = new THREE.Box3(), lo = new THREE.Vector3(), hi = new THREE.Vector3();
  const update = () => {
    if (!root.visible) return;
    cam.copy(view.camera.position);
    const hide = hideDistance(view.camera.position.distanceTo(view.controls.target));
    const cs = model.chunks.map(c => {
      lo.set(c.box[0], c.box[1], c.box[2]); hi.set(c.box[3], c.box[4], c.box[5]);
      box.set(lo, hi);
      return { d: box.distanceToPoint(cam), tris0: Math.ceil(c.tris0 * density) };
    });
    const st = pickLod(cs, TRI_BUDGET, hide, LOD0_MAX);
    let changed = false;
    for (let i = 0; i < st.length; i++) if (st[i] !== state[i]) { state[i] = st[i]; changed = true; }
    if (!changed && stats._density === density) return;
    stats._density = density;
    let l0 = 0, l1 = 0, hd = 0, t0 = 0, t1 = 0, tr = 0;
    for (const c of model.chunks) {
      const s = state[c.i];
      for (const G of c.groups) {
        G.mesh.visible = s === 'lod0';
        G.mesh.count = keepCount(G.count, density);
        if (s === 'lod0') { t0 += G.mesh.count; tr += G.mesh.count * tris[G.kind]; }
        else if (s === 'lod1') { t1 += keepCount(G.count, density); tr += keepCount(G.count, density) * ptris[G.proxy]; }
      }
      if (s === 'lod0') l0++; else if (s === 'lod1') l1++; else hd++;
    }
    for (const B of blocks.values()) refill(B, false);
    Object.assign(stats, { lod0: l0, lod1: l1, hidden: hd, lod0Trees: t0, lod1Trees: t1, treeTris: tr });
  };
  view.onDraw(update);

  // 층 풍선 글 — 출처 · 갱신년도 · 그루 수 + 숲 종류마다 넓이 · 그루
  const lg = (F.legend || []).map(e => `${e.name} ${Number(e.ha).toLocaleString('ko-KR')} ha · ${Number(e.n).toLocaleString('ko-KR')}그루`);
  L.tree = {
    obj: root, label: '수목', on: true, n: model.n, note: F.note || '', title: [F.note || '', ...lg].filter(Boolean).join('\n'),
    legend: F.legend || [], kinds: F.kinds || [],
    stats: () => { const { _density, ...o } = stats; return { ...o, chunks: model.chunks.length, blocks: blocks.size, density }; },
    setDensity: (f) => { density = Math.max(0.25, Math.min(1, f)); for (const B of blocks.values()) refill(B, true); stats._density = -1; view.request(); },
    density: () => density,
  };
  // 층 줄의 밀도 막대(25~100 %)
  L.tree.slider = { min: 25, max: 100, value: () => Math.round(density * 100), set: (pct) => L.tree.setDensity(pct / 100), title: '밀도' };
}
