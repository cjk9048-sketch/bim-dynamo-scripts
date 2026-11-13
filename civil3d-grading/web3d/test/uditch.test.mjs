// ★[배수지 U형 측구] 3D 층 시험 — node --test test/ (화면 없이 · 지어낸 값)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import { buildUDitch, describeUDitch, UDITCH_COLOR } from '../src/uditch.js';

const mk = {
  material: (c) => new THREE.MeshStandardMaterial({ color: new THREE.Color(c) }),
  geom: (pos, idx) => { const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.BufferAttribute(pos, 3)); g.setIndex(new THREE.BufferAttribute(idx, 1)); return g; },
  flat: (g) => { g.computeBoundingBox(); return g; },
};
const tri = () => ({ pos: new Float32Array([0, 0, 0, 1, 0, 0, 0, 1, 0]), idx: new Uint32Array([0, 1, 2]) });

test('U형 측구 — 없는 장면은 층이 안 생김(옛 장면 그대로)', () => {
  const scene = new THREE.Scene(), L = {};
  buildUDitch({}, scene, L, mk);
  buildUDitch({ uditch: [] }, scene, L, mk);
  assert.equal(L.uditch, undefined);
  assert.equal(scene.children.length, 0);
});

test('U형 측구 — 측구마다 메시 · 층 «U형 측구» 기본 켬 · 판정 기운 · 빈 메시 건너뜀', () => {
  const scene = new THREE.Scene(), L = {};
  const head = { uditch: [
    { name: 'D-001', title: 'D-001 U형 측구', status: 'OK', rows: [['규격', 'U형 300×300'], ['판정', 'OK']], ...tri() },
    { name: 'D-002', status: '부족', rows: [['판정', '부족']], ...tri() },
    { name: 'D-003', status: 'OK', pos: new Float32Array(0), idx: new Uint32Array(0) },
  ] };
  buildUDitch(head, scene, L, mk);
  assert.ok(L.uditch);
  assert.equal(L.uditch.label, 'U형 측구');
  assert.equal(L.uditch.on, true);
  assert.equal(L.uditch.n, 2);
  const [a, b] = L.uditch.obj.children;
  assert.equal(a.userData.layer, 'uditch');
  assert.equal(a.material.color.getHexString(), new THREE.Color(UDITCH_COLOR).getHexString());
  assert.notEqual(b.material.color.getHexString(), a.material.color.getHexString(), '부족 = 다른 기운');
  assert.ok(a.castShadow && a.receiveShadow);
});

test('U형 측구 — 풍선 = C# 줄 그대로 · 제목 없으면 이름', () => {
  const d = describeUDitch({ name: 'D-002', rows: [['판정', '부족 — 설계유량 > 통수능'], ['bad']] });
  assert.equal(d.title, 'D-002');
  assert.deepEqual(d.rows, [['판정', '부족 — 설계유량 > 통수능']]);
  assert.equal(describeUDitch({ title: 'D-001 U형 측구', rows: [] }).title, 'D-001 U형 측구');
});

// ── ★[v117.16 · JACK 1112 «측구가 지표면이랑 겹쳐서 안 보이네» · «스틸그라우팅도 선택되게»] 지형 구멍 · 안쪽 재료 · 그레이팅 · 집수정 · 관로 ──
import { applyUDitchHole } from '../src/uditch.js';
import { sceneMesh, gridTerrain } from './uditch-mesh.mjs';

function fakeTerrain() {
  const T = gridTerrain(30, 20, 1, 0, 0, (x, y) => 10 + 0.02 * x, (x) => (x < 15 ? 0 : 1));
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(T.pos, 3));
  const uv2 = new Float32Array((T.pos.length / 3) * 2);
  for (let i = 0; i < uv2.length / 2; i++) { uv2[2 * i] = T.pos[3 * i]; uv2[2 * i + 1] = T.pos[3 * i + 1]; }
  g.setAttribute('uv2', new THREE.BufferAttribute(uv2, 2));
  g.setIndex(new THREE.BufferAttribute(T.idx, 1));
  for (const [c, s, n] of T.ranges) g.addGroup(s * 3, n * 3, c);
  g.computeVertexNormals();
  const mats = [new THREE.MeshStandardMaterial(), new THREE.MeshStandardMaterial()];
  return { obj: new THREE.Mesh(g, mats), mats, T };
}

test('U형 측구 — 지형 구멍(켜면 구멍 지형 · 끄면 · 원지반 보기면 원래) · 안쪽 어두운 재료 · 물 자국 · 마구리 · 그레이팅은 고른 측구만', () => {
  const scene = new THREE.Scene(), L = { terrain: fakeTerrain() };
  const full = L.terrain.obj.geometry;
  const a = sceneMesh([[-1, 5.2], [12, 5.2], [20, 9.1]], 9.75, 9.9), b = sceneMesh([[8.1, 15], [8.1, 5.2]], 9.85, 9.8, { B: 0.4, H: 0.4, Tw: 0.15 });
  buildUDitch({ uditch: [
    { name: 'D-001', status: 'OK', pos: a.pos, idx: a.idx },
    { name: 'D-002', status: '주의', pos: b.pos, idx: b.idx, grating: true },
  ] }, scene, L, mk);
  const U = L.uditch, T = L.terrain;
  assert.equal(U.n, 2);
  assert.equal(U.stats.holes, 2);
  assert.equal(U.stats.failed, 0);
  assert.equal(U.stats.grating, 1);
  assert.ok(T.holeGeom && T.fullGeom === full, '구멍 지형은 따로 · 원래는 그대로');
  assert.ok(T.holeGeom.userData.faceSrc.length === T.holeGeom.index.count / 3);
  assert.deepEqual(T.holeGeom.groups.map(g => g.materialIndex), [0, 1]);
  assert.ok(T.holeGeom.attributes.uv2 && T.holeGeom.attributes.normal, '지형 값(uv2 · 법선) 함께 늘림');
  assert.equal(T.obj.userData.geomFull, full);
  // 켬/끔
  applyUDitchHole(L, false); assert.equal(T.obj.geometry, T.holeGeom);
  U.on = false; applyUDitchHole(L, false); assert.equal(T.obj.geometry, full);
  U.on = true; applyUDitchHole(L, true); assert.equal(T.obj.geometry, full, '원지반 보기');
  // 측구 메시 — 바깥 · 안쪽(어두운) 두 재료 · 꾸밈 자식(물 자국 · 마구리 · 그레이팅)
  const [ma, mb] = U.obj.children;
  assert.ok(Array.isArray(ma.material) && ma.material.length === 2);
  assert.ok(ma.material[1].color.r < ma.material[0].color.r, '안쪽이 더 어둡다');
  assert.equal(ma.geometry.groups.length, 2);
  const kinds = (m) => m.children.map(c => c.userData.deco).sort().join(',');
  assert.equal(kinds(ma), 'ends,water', '열린 U = 물 자국 + 마구리');
  assert.equal(kinds(mb), 'ends,grating,water', '그레이팅 측구 = + 그레이팅');
  const grate = mb.children.find(c => c.userData.deco === 'grating');
  assert.equal(grate.userData.layer, 'uditch');
  assert.equal(grate.userData.name, 'D-002', '그레이팅을 누르면 그 측구');
  assert.ok(grate.userData.deco && ma.children[0].userData.deco, '꾸밈은 세모 수 보고에서 뺀다');
  const gb = grate.geometry.boundingBox || (grate.geometry.computeBoundingBox(), grate.geometry.boundingBox);
  // 측구 윗면 = 바닥고 + 0.4(9.85 → 9.80 경사) · 그레이팅 = 그 위 6 mm
  assert.ok(Math.abs(gb.max.z - (10.25 + 0.006)) < 1e-4 && Math.abs(gb.min.z - (10.2 + 0.006)) < 1e-4, `그레이팅 = 윗면 바로 위 (${gb.min.z} ~ ${gb.max.z})`);
  assert.ok(gb.max.x - gb.min.x > 0.4 && gb.max.x - gb.min.x < 0.7, `그레이팅 폭 = 안폭 + 받침 (${gb.max.x - gb.min.x})`);
});

test('U형 측구 — 집수정(상자 + 그레이팅 · 지형 구멍) · 관로(원통) · 값이 틀리면 건너뜀 · 없으면 그대로', () => {
  const scene = new THREE.Scene(), L = { terrain: fakeTerrain() };
  buildUDitch({ uditch: [{ name: 'D-009', basins: [
    { x: 20, y: 10, zTop: 10.4, zBottom: 9.2, w: 1, l: 1.2 },
    { x: 1, y: 1, zTop: 9, zBottom: 10, w: 1, l: 1 },          // 윗면이 바닥보다 낮음 → 건너뜀
  ], pipes: [
    { x1: 20, y1: 10, z1: 9.5, x2: 28, y2: 10, z2: 9.3, d: 0.3 },
    { x1: 0, y1: 0, z1: 0, x2: 0, y2: 0, z2: 0, d: 0.3 },       // 길이 0 → 건너뜀
    { x1: 0, y1: 0, z1: 0, x2: 1, y2: 0, z2: 0 },                // 지름 없음 → 건너뜀
  ] }] }, scene, L, mk);
  const U = L.uditch;
  assert.equal(U.n, 0, '측구 메시 없음');
  assert.equal(U.stats.basins, 1);
  assert.equal(U.stats.pipes, 1);
  assert.equal(U.stats.holes, 1, '집수정 구멍 하나(관로는 땅속)');
  assert.ok(L.terrain.holeGeom);
  const [box, pipe] = U.obj.children;
  assert.equal(box.userData.layer, 'uditch');
  assert.equal(box.userData.title, '집수정');
  assert.equal(box.children.length, 1, '윗면 그레이팅');
  assert.ok(Math.abs(box.position.z - 9.8) < 1e-9);
  assert.equal(pipe.geometry.type, 'CylinderGeometry');
  assert.ok(Math.abs(pipe.geometry.parameters.height - Math.hypot(8, 0.2)) < 1e-9);
  assert.ok(pipe.userData.rows.some(r => r[0] === '지름' && r[1] === '300 mm'));
  // 집수정 · 관로 · 그레이팅 없는 옛 장면 = 옛 그대로(꾸밈만 · 구멍은 측구)
  const L2 = {}, s2 = new THREE.Scene(), m = sceneMesh([[0, 0], [5, 0]], 1, 0.9);
  buildUDitch({ uditch: [{ name: 'D-1', pos: m.pos, idx: m.idx }] }, s2, L2, mk);
  assert.equal(L2.uditch.stats.basins + L2.uditch.stats.pipes + L2.uditch.stats.grating, 0);
  assert.equal(L2.uditch.stats.holes, 1, '지형이 없으면 모양만 · 자르기 없음');
  assert.equal(L2.terrain, undefined);
});

test('U형 측구 — 구간 여럿(parts) · 낙차벽 · 집수정 사이: 구멍 = 낙차로 이은 줄마다 하나 + 집수정 · 물 자국 · 그레이팅은 구간마다 · 마구리는 줄 끝에만', async () => {
  const { joinMeshes } = await import('./uditch-mesh.mjs');
  const s1 = sceneMesh([[0, 5], [10, 5]], 10, 9.95), s2 = sceneMesh([[10, 5], [15, 5], [20, 5]], 9.75, 9.7), s3 = sceneMesh([[24, 5], [29, 5.4]], 9.4, 9.35);
  const M = joinMeshes([{ m: s1, seg: 1 }, { m: s2, seg: 2 }, { m: s3, seg: 3 }, { box: [[10, 4.85], [10.15, 4.85], [10.15, 5.15], [10, 5.15]], zb: 9.75, zt: 9.95, seg: 2 }]);
  const scene = new THREE.Scene(), L = { terrain: fakeTerrain() };
  buildUDitch({ uditch: [{
    name: 'D-010', status: 'OK', pos: M.pos, idx: M.idx, parts: M.parts, grating: true,
    basins: [{ x: 20.6, y: 5, zTop: 10.2, zBottom: 9, w: 1.2, l: 1.2, rot: 0, name: 'D-010 집수정 1', title: 'D-010 집수정 1', rows: [['깊이', '1.20 m']] }],
    pipes: [{ x1: 21.2, y1: 5, z1: 9.6, x2: 24, y2: 5, z2: 9.55, d: 0.4, name: 'D-010 관로 1', title: 'D-010 관로 1', rows: [['지름', 'D300 (바깥 0.40 m)']] }],
  }] }, scene, L, mk);
  const U = L.uditch;
  assert.equal(U.n, 1);
  assert.equal(U.stats.segs, 3);
  assert.equal(U.stats.holes, 3, '구간 1+2(낙차로 이음) · 구간 3 · 집수정');
  assert.equal(U.stats.failed, 0);
  assert.equal(U.stats.grating, 1);
  const [ditch, box, pipe] = U.obj.children;
  const count = (k) => ditch.children.filter(c => c.userData.deco === k).length;
  assert.equal(count('water'), 3);
  assert.equal(count('grating'), 3);
  assert.equal(count('ends'), 2, '줄 둘');
  for (const e of ditch.children.filter(c => c.userData.deco === 'ends')) assert.equal(e.geometry.index.count, 12, '줄마다 양 끝 마구리(낙차 자리는 안 막음)');
  // 낙차벽 = 안쪽 재료 묶음
  const g = ditch.geometry, inner = g.groups[1], wall = M.parts.find(p => p.part === '낙차벽');
  const innerV = new Set(Array.from(g.index.array.slice(inner.start, inner.start + inner.count)));
  for (let v = wall.v0; v < wall.v0 + wall.n; v++) assert.ok(innerV.has(v), '낙차벽 꼭짓점이 안쪽 묶음');
  assert.equal(box.userData.title, 'D-010 집수정 1');
  assert.deepEqual(box.userData.rows, [['깊이', '1.20 m']], 'C# 풍선 줄 그대로');
  assert.ok(Math.abs(pipe.geometry.parameters.radiusTop - 0.2) < 1e-9, 'd = 바깥 지름');
  assert.deepEqual(pipe.userData.rows, [['지름', 'D300 (바깥 0.40 m)']]);
});

test('U형 측구(검토 M) — 원래 · 구멍 지형이 늘린 값 한 벌을 함께 씀 · 관로 토막 이음 공 · 집수정 안치수(inner)로 덮개', () => {
  const scene = new THREE.Scene(), L = { terrain: fakeTerrain() };
  const full = L.terrain.obj.geometry, n0 = full.index.count, V0 = full.attributes.position.count;
  const m = sceneMesh([[2, 5], [12, 5]], 9.75, 9.7);
  buildUDitch({ uditch: [{ name: 'D-020', pos: m.pos, idx: m.idx,
    basins: [{ x: 13, y: 5, zTop: 10.25, zBottom: 9, w: 1.4, l: 1.4, inner: 0.8 }, { x: 20, y: 5, zTop: 10.3, zBottom: 8.8, w: 1, l: 1 }],
    pipes: [
      { x1: 13.7, y1: 5, z1: 9.4, x2: 16, y2: 5, z2: 9.4, d: 0.4 },
      { x1: 16, y1: 5, z1: 9.4, x2: 18, y2: 5, z2: 9.0, d: 0.4 },
      { x1: 18, y1: 5, z1: 9.0, x2: 19.5, y2: 5, z2: 9.0, d: 0.4 },
    ] }] }, scene, L, mk);
  const T = L.terrain, U = L.uditch;
  for (const k of Object.keys(full.attributes)) assert.equal(T.holeGeom.attributes[k], full.attributes[k], `${k} 한 벌`);
  assert.equal(full.index.count, n0, '원래 번호 그대로');
  assert.ok(full.attributes.position.count > V0, '새 점은 원래 번호가 안 가리킴');
  assert.equal(T.obj.userData.geomFull, full);
  assert.equal(U.stats.pipes, 3);
  assert.equal(U.stats.joints, 2, '토막 셋 = 이음 둘(집수정 쪽 끝은 없음)');
  const joints = U.obj.children.filter(c => c.userData.deco === 'joint');
  assert.equal(joints.length, 2);
  assert.ok(Math.abs(joints[0].scale.x - 0.2) < 1e-9, '이음 공 = 관 반지름');
  assert.equal(joints[0].userData.layer, 'uditch', '이음 공을 누르면 관로');
  const box = U.obj.children.find(c => c.geometry.type === 'BoxGeometry');
  const lid = box.children[0].geometry.parameters;
  assert.ok(Math.abs(lid.width - 0.8) < 1e-9 && Math.abs(lid.height - 0.8) < 1e-9, `덮개 = 안치수 0.8 (${lid.width})`);
  const box2 = U.obj.children.filter(c => c.geometry.type === 'BoxGeometry')[1];
  assert.ok(Math.abs(box2.children[0].geometry.parameters.width - 0.7) < 1e-9, 'inner 없으면 벽 0.15');
});
