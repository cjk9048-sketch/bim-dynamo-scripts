// ★[v115 · JACK 1105 임상도] 수목 뷰어 시험 — node --test test/ (화면 없이 · three는 그리기 없이 물체만)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import { treeGeo, proxyGeo, pickLod, hideDistance, keepCount, KIND_COUNT, proxyOf } from '../src/treegeo.js';
import { forestModel, buildForest, TRI_BUDGET } from '../src/forest.js';

test('나무 모양 — 세모 예산 · 키 1 · 밑 0 · NaN 없음', () => {
  for (let k = 0; k < KIND_COUNT; k++) {
    const g = treeGeo(k);
    assert.ok(g.tris >= 60 && g.tris <= 160, `모양 ${k} 세모 ${g.tris}`);
    let zMin = Infinity, zMax = -Infinity, rMax = 0;
    for (let i = 0; i < g.pos.length; i += 3) {
      assert.ok(Number.isFinite(g.pos[i]) && Number.isFinite(g.pos[i + 1]) && Number.isFinite(g.pos[i + 2]), `모양 ${k} NaN`);
      zMin = Math.min(zMin, g.pos[i + 2]); zMax = Math.max(zMax, g.pos[i + 2]); rMax = Math.max(rMax, Math.hypot(g.pos[i], g.pos[i + 1]));
    }
    assert.equal(zMin, 0, `모양 ${k} 밑 ${zMin}`);
    assert.ok(Math.abs(zMax - 1) < 1e-6, `모양 ${k} 키 ${zMax}`);
    assert.ok(rMax > 0.2 && rMax < 0.75, `모양 ${k} 수관 반지름 ${rMax}`);
    assert.equal(g.col.length, g.pos.length);
    for (const c of g.col) assert.ok(c >= 0 && c <= 1);
  }
  for (const p of [0, 1]) { const g = proxyGeo(p); assert.ok(g.tris >= 8 && g.tris <= 12, `대리 ${p} ${g.tris}`); }
  assert.deepEqual([0, 1, 2, 3, 8].map(proxyOf), [0, 0, 0, 1, 1]);
});

test('LOD 고르기 — 가까운 덩이부터 예산까지 자세히 · 나머지 대리 · 숨김 거리', () => {
  const ch = [{ d: 500, tris0: 400 }, { d: 10, tris0: 400 }, { d: 100, tris0: 400 }, { d: 5000, tris0: 400 }, { d: 200, tris0: 400 }];
  const st = pickLod(ch, 1000, 3000);
  assert.deepEqual(st, ['lod1', 'lod0', 'lod0', 'hidden', 'lod1']);   // 400 + 400 = 800 · 다음 400 → 1200 > 1100(예산 + 10 %)
  assert.deepEqual(pickLod([{ d: 1, tris0: 1050 }, { d: 2, tris0: 10 }], 1000, 3000), ['lod0', 'lod1']);   // ±10 % 안 → 넣고 꽉 참
  assert.deepEqual(pickLod([{ d: 100, tris0: 10 }, { d: 900, tris0: 10 }], 1000, 3000, 800), ['lod0', 'lod1']);   // 800 m 밖 = 대리
  assert.equal(hideDistance(100), 3000);
  assert.equal(hideDistance(4000), 6000);
});

test('밀도 → 수', () => {
  assert.equal(keepCount(100, 1), 100);
  assert.equal(keepCount(100, 0.25), 25);
  assert.equal(keepCount(7, 0.5), 4);
  assert.equal(keepCount(0, 0.5), 0);
});

// 가짜 머리 — 덩이 셋(가까움 · 가운데 · 멂) × 모양 둘
function fakeHead(per = 50) {
  const xs = [], zs = [], hw = [], k = [], r = [], t = [], grp = [], cbox = [], czr = [];
  const chunks = [[0, 0], [1000, 0], [6000, 0]];
  let i = 0;
  chunks.forEach(([cx, cy], c) => {
    cbox.push(cx, cy, cx + 250, cy + 250); czr.push(10, 30);
    for (const kind of [0, 3]) {
      grp.push(i, per, c);
      for (let j = 0; j < per; j++, i++) { xs.push(cx + 5 + j, cy + 5 + (kind ? 100 : 0)); zs.push(10); hw.push(75, 25); k.push(kind); r.push(j * 5 % 256); t.push(128); }
    }
  });
  return { n: i, xy: new Float32Array(xs), z: new Float32Array(zs), hw: new Uint8Array(hw), k: new Uint8Array(k), r: new Uint8Array(r), t: new Uint8Array(t), grp: new Uint32Array(grp), cbox: new Float32Array(cbox), czr: new Float32Array(czr), note: '임상도 © 산림청', legend: [] };
}

test('head.forest 풀기 — 행렬(이동 · 늘임) · 무리 · 덩이', () => {
  const F = fakeHead();
  const m = forestModel(F);
  assert.equal(m.groups.length, 6);
  assert.equal(m.chunks.length, 3);
  const G = m.groups[0];
  const mat = new THREE.Matrix4().fromArray(G.mat, 0);
  const p = new THREE.Vector3(), q = new THREE.Quaternion(), s = new THREE.Vector3();
  mat.decompose(p, q, s);
  assert.ok(Math.abs(p.x - 5) < 1e-4 && Math.abs(p.z - 10) < 1e-4);
  assert.ok(Math.abs(s.x - 5) < 1e-4 && Math.abs(s.z - 15) < 1e-4, `늘임 ${s.toArray()}`);   // W = 25 × 0.2 · H = 75 × 0.2
});

function fakeView() {
  const camera = new THREE.PerspectiveCamera(45, 1.6, 0.1, 10000);
  camera.position.set(100, -200, 150);
  return { scene: new THREE.Scene(), camera, controls: { target: new THREE.Vector3(100, 100, 10) }, _f: [], onDraw(fn) { this._f.push(fn); }, request() {}, draw() { for (const f of this._f) f(); } };
}

test('층 — 덩이 상태 · 대리 묶음 다시 채우기(N3) · 밀도', () => {
  const view = fakeView(), L = {};
  buildForest(view, { forest: fakeHead(50) }, L);
  assert.ok(L.tree && L.tree.obj && L.tree.n === 300);
  view.draw();
  let s = L.tree.stats();
  assert.equal(s.lod0 + s.lod1 + s.hidden, 3);
  assert.equal(s.hidden, 1, '6 km 덩이는 숨김');
  assert.ok(s.treeTris <= TRI_BUDGET * 1.1);
  // 가까운 덩이 = 자세히 · 1 km 덩이(사진기에서 800 m 밖) = 대리
  assert.deepEqual([s.lod0, s.lod1], [1, 1]);
  // 사진기를 멀리 → 숨김 거리가 늘어 다 보임 · 모두 800 m 밖이라 대리
  view.camera.position.set(100, -5000, 3000); view.controls.target.set(100, 100, 10);
  view.draw();
  s = L.tree.stats();
  assert.deepEqual([s.lod0, s.lod1, s.hidden], [0, 3, 0]);
  // 밀도 50 % — 자세히 무리 수 = ⌈0.5 n⌉
  L.tree.setDensity(0.5);
  view.draw();
  s = L.tree.stats();
  assert.equal(s.lod0Trees + s.lod1Trees, 6 * 25);
  // 대리 묶음 — 강제로 대리 상태를 만들려면 예산을 넘겨야 함: 무리를 크게(덩이마다 2만 그루)
  //   사진기 = 가까운 덩이 300 m 앞 → 가까운 덩이 자세히(1.64백만 ≤ 예산 + 10 %) · 1 km 덩이(800 m 밖) 대리 · 6 km 덩이 숨김
  const v2 = fakeView(), L2 = {};
  buildForest(v2, { forest: fakeHead(10000) }, L2);
  v2.camera.position.set(100, -300, 200);
  v2.draw();
  let s2 = L2.tree.stats();
  assert.deepEqual([s2.lod0, s2.lod1, s2.hidden], [1, 1, 1], `덩이 ${JSON.stringify(s2)}`);
  const lod1Meshes = () => L2.tree.obj.children.filter(o => o.userData.lod === 1 && o.visible);
  const sumLod1 = () => lod1Meshes().reduce((a, o) => a + o.count, 0);
  assert.equal(sumLod1(), s2.lod1Trees);
  // 대리 행렬 = 원래 무리 앞부분 그대로(첫 그루 행렬이 어느 무리의 첫 그루와 같음)
  const m = forestModel(fakeHead(10000));
  const first = lod1Meshes()[0].instanceMatrix.array.subarray(0, 16);
  assert.ok(m.groups.some(G => G.mat.subarray(0, 16).every((v, i) => v === first[i])));
  // 밀도 60 % — 대리 묶음을 원래 무리에서 다시 채움(무리마다 ⌈0.6 n⌉) · 자세히 무리도 같은 수
  L2.tree.setDensity(0.6);
  v2.draw();
  s2 = L2.tree.stats();
  assert.equal(sumLod1(), s2.lod1Trees);
  assert.equal(s2.lod1Trees, s2.lod1 * 2 * Math.ceil(0.6 * 10000), '대리 = 원래 무리마다 앞 ⌈f·n⌉');
  assert.equal(s2.lod0Trees, s2.lod0 * 2 * Math.ceil(0.6 * 10000));
  // 25 % — 같은 덩이 상태 · 무리마다 2,500
  L2.tree.setDensity(0.25);
  v2.draw();
  s2 = L2.tree.stats();
  assert.deepEqual([s2.lod0Trees, s2.lod1Trees, sumLod1()], [2 * 2500, 2 * 2500, 2 * 2500]);
});

test('없음 · quiet 머리 → 회색 층', () => {
  const L = {};
  buildForest(fakeView(), { forest: { missing: '임상도 타일 없음', quiet: true } }, L);
  assert.deepEqual([L.tree.missing, L.tree.quiet, L.tree.on], ['임상도 타일 없음', true, false]);
  const L2 = {};
  buildForest(fakeView(), {}, L2);
  assert.equal(L2.tree, undefined);
});
