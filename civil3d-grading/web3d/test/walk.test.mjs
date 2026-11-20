// [시야 검토] 땅 높이 따라가기 — 순수 함수(buildGrid · heightAt · stepOn). 기운 판(북쪽 20 % 오름) 위를 10 m 걸어도 눈높이 = 땅 + 1.70 m.
import test from 'node:test';
import assert from 'node:assert/strict';
import { buildGrid, heightAt, stepOn, EYE } from '../src/walk.js';

// 100 × 100 m 판을 10 m 칸 두 세모씩 · z = 50 + 0.2·y + 0.02·x
function plate() {
  const pos = [], idx = [], n = 10;
  for (let j = 0; j <= n; j++) for (let i = 0; i <= n; i++) { const x = i * 10, y = j * 10; pos.push(x, y, 50 + 0.2 * y + 0.02 * x); }
  for (let j = 0; j < n; j++) for (let i = 0; i < n; i++) { const a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1; idx.push(a, b, d, a, d, c); }
  return { pos: new Float32Array(pos), idx: new Uint32Array(idx) };
}

test('heightAt = 판 높이 · 밖은 null', () => {
  const { pos, idx } = plate(), g = buildGrid(pos, idx, 8);
  assert.ok(Math.abs(heightAt(g, 33, 47) - (50 + 0.2 * 47 + 0.02 * 33)) < 1e-4);
  assert.equal(heightAt(g, -1, 10), null);
  assert.equal(heightAt(g, 50, 101), null);
});

test('기운 땅 위 북쪽으로 10 m 걷기 — 눈높이 = 땅 + 1.70 ± 0.02', () => {
  const { pos, idx } = plate(), g = buildGrid(pos, idx, 8);
  const st = { x: 50, y: 20, yaw: 0, gz: heightAt(g, 50, 20) };
  for (let i = 0; i < 100; i++) assert.ok(stepOn(g, st, 0.1));
  assert.ok(Math.abs(st.y - 30) < 1e-9);
  const truth = 50 + 0.2 * st.y + 0.02 * st.x;
  assert.ok(Math.abs(st.gz + EYE - (truth + 1.7)) < 0.02);
});

test('옆걸음 · 지형 끝에서 멈춤', () => {
  const { pos, idx } = plate(), g = buildGrid(pos, idx, 8);
  const st = { x: 50, y: 50, yaw: Math.PI / 2, gz: heightAt(g, 50, 50) };   // 동쪽 보기
  stepOn(g, st, 0, 5);                                                     // 오른쪽 = 남쪽
  assert.ok(Math.abs(st.y - 45) < 1e-9 && Math.abs(st.x - 50) < 1e-9);
  let n = 0; while (stepOn(g, st, 5) && n < 100) n++;
  assert.ok(st.x <= 100 && st.x > 94);
});

test('여러 겹(옹벽 면 위 · 아래)이면 앞선 높이에 가까운 쪽', () => {
  const pos = new Float32Array([0, 0, 0, 10, 0, 0, 0, 10, 0, 0, 0, 20, 10, 0, 20, 0, 10, 20]);
  const g = buildGrid(pos, null, 8);
  assert.equal(heightAt(g, 2, 2, 19), 20);
  assert.equal(heightAt(g, 2, 2, 1), 0);
  assert.equal(heightAt(g, 2, 2), 20);
});
