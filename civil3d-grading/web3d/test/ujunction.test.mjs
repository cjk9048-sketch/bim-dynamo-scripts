// ★[JACK 1123 «닫힌 선 → 두 갈래(출구 집수정)» · «합류 · 교차 자리에 집수정»] 3D — C#이 넣는 꼴 그대로(지어낸 값):
//   두 갈래 메시는 집수정 바깥면에서 끝남 · 집수정은 첫 측구(owner)의 basins에 · 붙일 측구가 못 지어졌으면 집수정만 든 칸(pos 없음)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import { buildUDitch } from '../src/uditch.js';
import { holeShapes, ditchFrames, chainFrames, mergeChain } from '../src/uhole.js';
import { sceneMesh, gridTerrain } from './uditch-mesh.mjs';

const mk = {
  material: (c) => new THREE.MeshStandardMaterial({ color: new THREE.Color(c) }),
  geom: (pos, idx) => { const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.BufferAttribute(pos, 3)); g.setIndex(new THREE.BufferAttribute(idx, 1)); return g; },
  flat: (g) => { g.computeBoundingBox(); return g; },
};

function terrain() {
  const T = gridTerrain(40, 30, 1, -5, -5, (x, y) => 10 - 0.01 * x + 0.01 * y, () => 0);
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(T.pos, 3));
  g.setIndex(new THREE.BufferAttribute(T.idx, 1));
  for (const [c, s, n] of T.ranges) g.addGroup(s * 3, n * 3, c);
  return { obj: new THREE.Mesh(g, new THREE.MeshBasicMaterial()) };
}

// 닫힌 사각형 (0,0)-(20,0)-(20,15)-(0,15) · 출구 = (20,0) 모서리(집수정 바깥 0.9) · 시작 = (0,15)
const h = 0.45;
const a = sceneMesh([[0, 15], [0, 0], [20 - h, 0]], 9.7, 9.5);        // 갈래 1 — 끝 = 집수정 서쪽 면
const b = sceneMesh([[0, 15], [20, 15], [20, h]], 9.7, 9.5);         // 갈래 2 — 끝 = 집수정 북쪽 면
const basin = { x: 20, y: 0, zTop: 9.8, zBottom: 9.05, w: 0.9, l: 0.9, rot: 0, inner: 0.6, name: '집수정 J1(출구 · D-001-1 + D-001-2)', rows: [['종류', '출구 · D-001-1 + D-001-2']] };

test('닫힌 선 두 갈래 — 집수정은 첫 갈래 칸에 · 구멍 = 갈래 둘 + 집수정 하나', () => {
  const scene = new THREE.Scene(), L = { terrain: terrain() };
  const head = { uditch: [
    { name: 'D-001-1', status: '', rows: [], ...a, parts: a.parts.map(q => ({ ...q, seg: 1 })), basins: [basin], pipes: [] },
    { name: 'D-001-2', status: '', rows: [], ...b, parts: b.parts.map(q => ({ ...q, seg: 1 })), basins: [], pipes: [] },
  ] };
  buildUDitch(head, scene, L, mk);
  assert.equal(L.uditch.n, 2);
  assert.equal(L.uditch.stats.basins, 1);
  assert.equal(L.uditch.stats.holes, 3);
  const box = L.uditch.obj.children.find(c => c.userData.name && c.userData.name.startsWith('집수정 J1'));
  assert.ok(box, '집수정 상자');
  assert.equal(box.userData.rows[0][1], '출구 · D-001-1 + D-001-2');
  assert.ok(L.terrain.holeGeom, '지형 구멍');
});

test('갈래 끝은 집수정 바깥면에 딱 붙음(구멍 테두리 = 집수정 네모 + 1 mm 안)', () => {
  const fa = ditchFrames(a.pos, a.idx, a.parts.map(q => ({ ...q, seg: 1 })));
  const st = mergeChain(chainFrames(fa.frames)[0]).st;
  const end = st[st.length - 1].c;
  assert.ok(Math.abs(end[0] - (20 - h)) < 2e-3 && Math.abs(end[1]) < 2e-3, `끝 ${end}`);
  const sh = holeShapes([], [basin]);
  const xs = sh[0].pts.filter((_, i) => i % 2 === 0);
  assert.ok(Math.abs(Math.min(...xs) - (20 - h - 1e-3)) < 2e-4, '집수정 구멍 서쪽 = 갈래 끝 − 1 mm');
});

test('붙일 측구가 못 지어졌으면 집수정만 든 칸도 그림', () => {
  const scene = new THREE.Scene(), L = {};
  buildUDitch({ uditch: [{ name: '집수정 J2(합류 · D-002 → D-001)', title: '집수정 J2', status: '', rows: [], basins: [{ ...basin, x: 5, name: '집수정 J2' }], pipes: [], grating: false }] }, scene, L, mk);
  assert.equal(L.uditch.n, 0);
  assert.equal(L.uditch.stats.basins, 1);
});
