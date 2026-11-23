// [3D 보기 방향키] 보는 방향 기준 한 걸음 — 순수 함수 panStep
import test from 'node:test';
import assert from 'node:assert/strict';
import { panStep } from '../src/keymove.js';

const near = (a, b) => Math.abs(a - b) < 1e-9;

test('↑ = 사진기 → 중심의 수평 방향', () => {
  const d = panStep({ x: 0, y: 0, z: 100 }, { x: 0, y: 100, z: 0 }, { x: 0, y: 1, z: 0 }, 1, 0, 10);
  assert.ok(near(d.x, 0) && near(d.y, 10), JSON.stringify(d));
});

test('→ = 오른쪽(북을 보면 동쪽) · ← = 왼쪽', () => {
  const r = panStep({ x: 0, y: 0, z: 50 }, { x: 0, y: 100, z: 0 }, { x: 0, y: 1, z: 0 }, 0, 1, 5);
  assert.ok(near(r.x, 5) && near(r.y, 0), JSON.stringify(r));
  const l = panStep({ x: 0, y: 0, z: 50 }, { x: 0, y: 100, z: 0 }, { x: 0, y: 1, z: 0 }, 0, -1, 5);
  assert.ok(near(l.x, -5) && near(l.y, 0), JSON.stringify(l));
});

test('동쪽을 보며 ↑ = 동쪽 · ↓ = 서쪽', () => {
  const f = panStep({ x: 0, y: 0, z: 30 }, { x: 80, y: 0, z: 0 }, { x: 0, y: 0, z: 1 }, 1, 0, 2);
  assert.ok(near(f.x, 2) && near(f.y, 0), JSON.stringify(f));
  const b = panStep({ x: 0, y: 0, z: 30 }, { x: 80, y: 0, z: 0 }, { x: 0, y: 0, z: 1 }, -1, 0, 2);
  assert.ok(near(b.x, -2) && near(b.y, 0), JSON.stringify(b));
});

test('수직으로 내려다보면 화면 위쪽이 앞', () => {
  const d = panStep({ x: 10, y: 10, z: 500 }, { x: 10, y: 10, z: 0 }, { x: -1, y: 0, z: 0 }, 1, 0, 3);
  assert.ok(near(d.x, -3) && near(d.y, 0), JSON.stringify(d));
});

test('대각선도 같은 빠르기 · 안 누르면 0', () => {
  const d = panStep({ x: 0, y: 0, z: 10 }, { x: 0, y: 10, z: 0 }, { x: 0, y: 1, z: 0 }, 1, 1, 4);
  assert.ok(near(Math.hypot(d.x, d.y), 4), JSON.stringify(d));
  const z = panStep({ x: 0, y: 0, z: 10 }, { x: 0, y: 10, z: 0 }, { x: 0, y: 1, z: 0 }, 0, 0, 4);
  assert.ok(z.x === 0 && z.y === 0);
});