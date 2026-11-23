// [U형 측구 그레이팅] 꺾이는 자리 비스듬한 단면(사다리꼴)에서도 살 방향 = 측구 축에 직각 — uv = (축 방향 길이, 축에 수직 옆 거리)
import test from 'node:test';
import assert from 'node:assert/strict';
import { _bandForTest as band } from '../src/uditch.js';

const near = (a, b) => Math.abs(a - b) < 1e-6;

test('비스듬한 끝 단면 — u = 축 방향(x) · v = 옆(y) 그대로', () => {
  // 축 = x 방향 10 m · 시점 단면은 45° 기움(왼 (-1, 1) · 오른 (1, -1)) · 종점은 직각
  const st = [{ c: [0, 0] }, { c: [10, 0] }];
  const ends = [[[-1, 1], [1, -1]], [[10, 1], [10, -1]]];
  const g = band(st, (s) => (s === st[0] ? [...ends[0], 0, 0] : [...ends[1], 0, 0]), () => 0, 1);
  const pos = g.attributes.position.array, uv = g.attributes.uv.array;
  for (let i = 0; i < pos.length / 3; i++) {
    assert.ok(near(uv[2 * i], pos[3 * i]), `u ${uv[2 * i]} ≠ x ${pos[3 * i]}`);
    assert.ok(near(uv[2 * i + 1], pos[3 * i + 1]), `v ${uv[2 * i + 1]} ≠ y ${pos[3 * i + 1]}`);
  }
});

test('꺾인 선 — 두 번째 구간은 그 구간 방향으로 이어 잼', () => {
  const st = [{ c: [0, 0] }, { c: [10, 0] }, { c: [10, 10] }];
  const pick = (s) => [[s.c[0] - 0.2, s.c[1] + 0.2], [s.c[0] + 0.2, s.c[1] - 0.2], 0, 0];
  const g = band(st, pick, () => 0, 1);
  const uv = g.attributes.uv.array;
  assert.equal(g.attributes.position.count, 8, '구간마다 꼭짓점 넷');
  // 둘째 구간(북쪽으로) 시작 = 길이 10에서 이어짐 · 끝 쪽 u ≈ 20 근처
  const u2 = [uv[8], uv[10], uv[12], uv[14]];
  assert.ok(u2.every((u) => u > 9 && u < 21), JSON.stringify(u2));
});