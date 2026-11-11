// ★[JACK 1111 «옹벽 표면을 옹벽처럼»] 옹벽 겉모습 시험 — node --test test/ (화면 없이 · 셰이더는 끼우기만)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import { faceStyle, FACE, pieceAttrs, pieceTint, faceUniforms, patchWallFace } from '../src/wallface.js';

test('무늬 층 → 겉모습 — C# WallFacePattern 층 다섯 · 모르는 층은 null(옛 한 색)', () => {
  assert.equal(faceStyle('옹벽_무늬_블록'), 'block');
  assert.equal(faceStyle('옹벽_무늬_패널'), 'panel');
  assert.equal(faceStyle('옹벽_앵커머리'), 'anchor');
  assert.equal(faceStyle('옹벽_무늬_자연석'), 'stone');
  assert.equal(faceStyle('옹벽_무늬_석축'), 'rubble');
  assert.equal(faceStyle('옹벽_무늬_새것'), null);
  assert.equal(faceStyle(undefined), null);
  for (const k of Object.keys(FACE)) {
    const u = faceUniforms(k);
    assert.equal(u.wfA.length, 4); assert.equal(u.wfB.length, 4);
    assert.ok([...u.wfA, ...u.wfB].every(Number.isFinite), k);
  }
});

// 상자 조각(점 8개 · 세모 12개) — 무늬 좌표 (u0..u1) × (z0..z1) · w = 0(몸통) / 1(겉)
function box(u0, u1, z0, z1, base) {
  const fuv = [], idx = [];
  for (const w of [0, 1]) for (const [u, z] of [[u0, z0], [u1, z0], [u1, z1], [u0, z1]]) fuv.push(u, z, w);
  const f = [[0, 1, 2], [0, 2, 3], [4, 6, 5], [4, 7, 6], [0, 4, 5], [0, 5, 1], [1, 5, 6], [1, 6, 2], [2, 6, 7], [2, 7, 3], [3, 7, 4], [3, 4, 0]];
  for (const t of f) idx.push(...t.map(i => i + base));
  return { fuv, idx };
}

test('조각 나누기 — 이어진 세모끼리 한 조각 · 가운데 · 반 크기 · 같은 조각은 같은 색 · 같은 입력은 같은 값', () => {
  const a = box(0, 0.46, 50, 50.2, 0), b = box(0.47, 0.93, 50, 50.2, 8), c = box(0.2, 0.66, 50.21, 50.41, 16);
  const fuv = new Float32Array([...a.fuv, ...b.fuv, ...c.fuv]), idx = new Uint32Array([...a.idx, ...b.idx, ...c.idx]);
  const r = pieceAttrs(idx, fuv, 'block', 3);
  assert.equal(r.pieces, 3);
  const at = (v) => Array.from(r.fpc.subarray(v * 4, v * 4 + 4));
  const near = (x, y) => x.every((q, i) => Math.abs(q - y[i]) < 1e-5);
  assert.ok(near(at(0), [0.23, 50.1, 0.23, 0.1]), JSON.stringify(at(0)));
  assert.ok(near(at(7), [0.23, 50.1, 0.23, 0.1]));
  assert.ok(near(at(8), [0.7, 50.1, 0.23, 0.1]));
  assert.ok(near(at(20), [0.43, 50.31, 0.23, 0.1]));
  const col = (v) => Array.from(r.col.subarray(v * 3, v * 3 + 3));
  for (let v = 1; v < 8; v++) assert.deepEqual(col(v), col(0), '한 조각 한 색');
  assert.notDeepEqual(col(0), col(8), '옆 블록은 다른 색');
  // 밝기 ± 한도 안
  for (let v = 0; v < 24; v++) assert.ok(Math.abs(r.col[v * 3 + 1] - 1) <= FACE.block.tint + 1e-9);
  assert.deepEqual(Array.from(pieceAttrs(idx, fuv, 'block', 3).col), Array.from(r.col), '같은 입력 = 같은 값');
});

test('앵커 머리 셋 — 정착판(0.15 □)은 어두운 강판 · 도넛 둘은 몸통보다 밝은 콘크리트', () => {
  const plate = pieceTint('anchor', 0, 0, 0.075, 0.075), mid = pieceTint('anchor', 0, 0, 0.18, 0.18), big = pieceTint('anchor', 0, 0, 0.28, 0.28);
  assert.ok(plate.every(x => x < 0.7), JSON.stringify(plate));
  assert.ok(mid.every(x => x > 1) && big.every(x => x > mid[0] - 0.05));
  assert.deepEqual(pieceTint('없음', 0, 0, 1, 1), [1, 1, 1]);
});

test('셰이더 끼우기 — three 표준 재료 조각 둘 · 정점 속성 fuv · fpc · 값 이름', () => {
  const sh = { vertexShader: THREE.ShaderLib.standard.vertexShader, fragmentShader: THREE.ShaderLib.standard.fragmentShader, uniforms: {} };
  patchWallFace(sh);
  assert.match(sh.vertexShader, /attribute vec3 fuv;/);
  assert.match(sh.vertexShader, /vWfUv = fuv;/);
  assert.match(sh.fragmentShader, /uniform vec4 wfA;/);
  assert.match(sh.fragmentShader, /diffuseColor\.rgb \*= wfShade;/);
  assert.match(sh.fragmentShader, /normal = normalize\( abs\( wfDet \) \* normal - wfGrad \);/);
  // 법선 조각은 색 조각(wfH) 뒤
  assert.ok(sh.fragmentShader.indexOf('float wfH') < sh.fragmentShader.indexOf('wfDh'));
  assert.throws(() => patchWallFace({ vertexShader: 'x', fragmentShader: 'y' }), /자리 없음/);
});
