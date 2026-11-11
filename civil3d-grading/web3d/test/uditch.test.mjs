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
