// ★[JACK 1110 «만화스럽다»] 바닥 · 옹벽 질감 시험 — node --test test/ (화면 없이 · 셰이더는 끼우기만 · 그림은 node:fs로 머리만)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import * as THREE from 'three';
import { surfaceFromPixels, SURFACE, patchGround } from '../src/groundtex.js';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const N = 64;
const img = (f) => { const px = new Uint8Array(N * N * 4); for (let r = 0; r < N; r++) for (let x = 0; x < N; x++) { const [h, ao] = f(x, r); const i = (r * N + x) * 4; px[i] = h; px[i + 1] = ao; px[i + 2] = 128; px[i + 3] = 255; } return px; };
const at = (out, x, rowFromBottom) => out.subarray((rowFromBottom * N + x) * 4, (rowFromBottom * N + x) * 4 + 4);

test('표면 지도 — 평평하면 법선 가운데 · 그늘 · 높이는 줄을 뒤집어(그림 맨 위 = v 끝) 그대로', () => {
  const src = img((x, r) => [100, r * 3]);
  const out = surfaceFromPixels(src, N, N, 0.5);
  for (let k = 0; k < N * N; k++) { assert.ok(Math.abs(out[4 * k] - 128) <= 1 && Math.abs(out[4 * k + 1] - 128) <= 1, '평평한 법선'); assert.equal(out[4 * k + 3], 100); }
  for (let r = 0; r < N; r++) assert.equal(at(out, 5, r)[2], (N - 1 - r) * 3, `줄 ${r} 그늘(뒤집힘)`);
});

test('표면 지도 — 봉우리 동쪽 비탈 법선은 +x · 북쪽(그림 위쪽) 비탈은 +y · 기울기 제곱평균 = bump', () => {
  const c = N / 2;
  const src = img((x, r) => [Math.round(40 + 180 * Math.exp(-((x - c) ** 2 + (r - c) ** 2) / 60)), 255]);
  const bump = 0.4, out = surfaceFromPixels(src, N, N, bump);
  // 그림 줄 r = 아래에서 N−1−r 줄
  assert.ok(at(out, c + 6, N - 1 - c)[0] > 150, `동쪽 nx ${at(out, c + 6, N - 1 - c)[0]}`);
  assert.ok(at(out, c - 6, N - 1 - c)[0] < 106, `서쪽 nx ${at(out, c - 6, N - 1 - c)[0]}`);
  assert.ok(at(out, c, N - 1 - (c - 6))[1] > 150, `북쪽(그림 위) ny ${at(out, c, N - 1 - (c - 6))[1]}`);
  assert.ok(at(out, c, N - 1 - (c + 6))[1] < 106, `남쪽 ny ${at(out, c, N - 1 - (c + 6))[1]}`);
  // 기울기 제곱평균 — 저장된 법선(n = (−g s, 1)/|…|)에서 되셈: tan = |nxy| / nz
  let ss = 0;
  for (let k = 0; k < N * N; k++) {
    const nx = out[4 * k] / 255 * 2 - 1, ny = out[4 * k + 1] / 255 * 2 - 1, nz = Math.sqrt(Math.max(1e-6, 1 - nx * nx - ny * ny));
    ss += (nx * nx + ny * ny) / (nz * nz);
  }
  assert.ok(Math.abs(Math.sqrt(ss / (N * N)) - bump) < 0.05, `기울기 ${Math.sqrt(ss / (N * N)).toFixed(3)} ≈ ${bump}`);
});

test('표면 지도 — 가장자리는 이어 셈(한 장을 이어 깔아도 이음매 없음)', () => {
  const src = img((x) => [Math.round(128 + 100 * Math.sin((2 * Math.PI * x) / N)), 255]);
  const out = surfaceFromPixels(src, N, N, 0.5);
  // sin 기울기 = cos — x = 0 과 x = N/2 은 크기가 같고 방향이 반대 · x = 0 은 이웃(N−1, 1)에서 바르게 셈
  const a = at(out, 0, 10)[0] - 128, b = at(out, N / 2, 10)[0] - 128;
  assert.ok(a < -20 && b > 20 && Math.abs(a + b) <= 2, `x0 ${a} · xN/2 ${b}`);
});

test('셰이더 끼우기 — three 표준 재료 조각 셋 · 정점(옹벽 세계 자리) · 값 이름', () => {
  const sh = { vertexShader: THREE.ShaderLib.standard.vertexShader, fragmentShader: THREE.ShaderLib.standard.fragmentShader, uniforms: {} };
  patchGround(sh);
  const f = sh.fragmentShader;
  for (const s of ['uniform sampler2D dhSurf', 'uniform float dhSat', 'textureGrad( DH_ALB', 'roughnessFactor = clamp', 'reflectedLight.indirectDiffuse *= dhAO', 'normal = normalize( normal + dhNt.x']) assert.ok(f.includes(s), s);
  // 원래 자리 셋은 재료가 DH_GROUND가 아닐 때만 쓰는 #else 또는 바로 앞에 한 번씩 남는다
  assert.equal(f.split('#include <map_fragment>').length - 1, 1);
  assert.ok(f.indexOf('#include <normal_fragment_maps>') < f.indexOf('dhNt'));
  assert.ok(sh.vertexShader.includes('vDhW = ( modelMatrix * vec4( transformed, 1.0 ) ).xyz'));
  assert.throws(() => patchGround({ vertexShader: 'void main(){}', fragmentShader: 'void main(){}' }), /바닥 셰이더 자리 없음/);
});

test('질감 파일 — 재료마다 색 그림 + 표면 지도(512² JPEG) · 값 범위', () => {
  const jpegSize = (b) => { // SOF0/SOF2 머리에서 크기
    for (let i = 2; i < b.length - 9;) {
      if (b[i] !== 0xff) { i++; continue; }
      const m = b[i + 1], len = b.readUInt16BE(i + 2);
      if (m === 0xc0 || m === 0xc2) return [b.readUInt16BE(i + 7), b.readUInt16BE(i + 5)];
      i += 2 + len;
    }
    return null;
  };
  for (const [k, P] of Object.entries(SURFACE)) {
    const f = P.src || k;
    assert.ok(existsSync(join(root, 'tex', f + '.jpg')), `${f}.jpg`);
    assert.ok(P.sat === undefined || (P.sat > 0 && P.sat <= 1), `${k} 채도`);
    const b = readFileSync(join(root, 'tex', f + '_s.jpg'));
    assert.deepEqual(jpegSize(b), [512, 512], `${k}_s.jpg 크기`);
    assert.ok(P.macro.length === 3 && P.macro[1] > 0 && P.macro[2] > P.macro[1], `${k} 얼룩 크기`);
    assert.ok(P.rough[0] - P.rough[1] >= 0.05 && P.rough[0] + P.rough[1] <= 1.1, `${k} 거칠기`);
    for (const t of [...P.a, ...P.b]) assert.ok(t > 0.5 && t < 1.5, `${k} 색 곱`);
  }
});
