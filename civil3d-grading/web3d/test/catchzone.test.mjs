// ★[JACK 1124 «3D 뷰에서 빗물 기능을 이용해서 측구 설계»] 측구 집수 구역 띠 → 세모 시험 — node --test test/ (화면 없이 · 지어낸 값)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { catchMesh, catchRows } from '../src/catchzone.js';

test('strips become quads per cell with zone colour and faceOf', () => {
  // 구역 0: 띠 둘(3칸 · 1칸) · 구역 1: 띠 하나(2칸)
  const s0 = [0, 0, 1, 3, 10, 10.1, 10.2, 10.3, 11, 11.1, 11.2, 11.3, 5, 0, 2, 1, 9, 9, 9.5, 9.5];
  const s1 = new Float32Array([0, 1, 1, 2, 11, 11.1, 11.2, 12, 12.1, 12.2]);
  const m = catchMesh([{ name: 'D-001', color: '#ff0000', strips: s0 }, { name: 'D-002', color: '#0000ff', strips: s1 }]);
  assert.equal(m.faceOf.length, (3 + 1 + 2) * 2);
  assert.equal(m.idx.length, m.faceOf.length * 3);
  assert.deepEqual([...new Set(m.faceOf)], [0, 1]);
  assert.equal(m.pos.length / 3, (4 + 4) + (2 + 2) + (3 + 3));
  // 첫 띠 위 가장자리 첫 점 = (0, 1, 11) · 둘째 띠 칸 2 m → 끝 x = 7
  assert.deepEqual(m.pos.slice(12, 15), [0, 1, 11]);
  assert.equal(Math.max(...m.pos.filter((_, i) => i % 3 === 0)), 7);
  // 색 — 구역 0 빨강 · 구역 1 파랑
  assert.deepEqual(m.col.slice(0, 3), [1, 0, 0]);
  assert.deepEqual(m.col.slice(-3), [0, 0, 1]);
  // 넓이 = 3 + 2×2 + 2 ㎡(평면)
  let area = 0;
  for (let t = 0; t < m.idx.length; t += 3) {
    const [a, b, c] = [m.idx[t], m.idx[t + 1], m.idx[t + 2]].map((i) => [m.pos[3 * i], m.pos[3 * i + 1]]);
    area += Math.abs((b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])) / 2;
  }
  assert.ok(Math.abs(area - 9) < 1e-9, `area ${area}`);
});

test('broken or empty strips are skipped', () => {
  assert.equal(catchMesh([{ strips: [0, 0, 1, 5, 1, 2] }]).idx.length, 0);
  assert.equal(catchMesh([]).idx.length, 0);
  assert.equal(catchMesh(undefined).idx.length, 0);
});

test('bubble rows show area source, C and upstream', () => {
  const fmt = (v, d) => Number(v).toFixed(d);
  const rows = catchRows({ area: 0.1234, src: '자동', c: 0.62, direct: 800, total: 1234, up: ['D-001'] }, fmt);
  assert.deepEqual(rows[0], ['집수면적', '0.1234 ha (자동)']);
  assert.deepEqual(rows[1], ['유출계수 C', '0.62']);
  assert.deepEqual(rows[3], ['위 측구', 'D-001 (전체 1234 ㎡)']);
});
