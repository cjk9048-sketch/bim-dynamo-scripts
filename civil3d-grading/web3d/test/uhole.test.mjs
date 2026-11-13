// ★[v117.16 · JACK 1112 «측구가 지표면이랑 겹쳐서 안 보이네»] 3D 보기 측구 구멍 시험 — node --test test/ (화면 없이 · 지어낸 값)
//   측구 메시 읽기(C# SceneMesh 옮김) · 지형 자르기: 넓이 보존 · 구멍 안 세모 없음 · 감김 · 이음(T자 없음 = 열린 변은 지형 둘레 · 구멍 테두리뿐) ·
//   가장자리 높이 = min(지형, 측구 윗면) · 섬(큰 세모 안 집수정) · 겹친 측구(합집합) · 구분(재료 묶음) · 값 늘이기 · 10만 세모 시간
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ditchFrame, ditchFrames, chainFrames, mergeChain, holeShapes, cutTerrain, extendAttribute } from '../src/uhole.js';
import { sceneMesh, gridTerrain, joinMeshes } from './uditch-mesh.mjs';

const near = (a, b, e = 1e-3) => Math.abs(a - b) <= e;

// 자른 결과의 꼭짓점(원래 + 새 점)
function vtxOf(P, cut) {
  const V0 = P.length / 3;
  return (i) => (i < V0 ? [P[3 * i], P[3 * i + 1], P[3 * i + 2]] : [cut.xyz[3 * (i - V0)], cut.xyz[3 * (i - V0) + 1], cut.xyz[3 * (i - V0) + 2]]);
}
const area2 = (a, b, c) => (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
function stats(P, cut) {
  const v = vtxOf(P, cut);
  let area = 0, neg = 0;
  const edges = new Map(), k = (p) => p[0].toFixed(9) + ',' + p[1].toFixed(9);   // 이웃이 같은 셈으로 만든 점은 똑같은 값 — 9자리로도 같다
  for (let i = 0; i < cut.index.length; i += 3) {
    const a = v(cut.index[i]), b = v(cut.index[i + 1]), c = v(cut.index[i + 2]);
    const s = area2(a, b, c);
    if (s <= 0) neg++;
    area += s / 2;
    for (const [p, q] of [[a, b], [b, c], [c, a]]) { const e = [k(p), k(q)].sort().join('|'); edges.set(e, (edges.get(e) || 0) + 1); }
  }
  const open = [...edges].filter(([, n]) => n === 1).map(([e]) => e.split('|').map(s => s.split(',').map(Number)));
  const over = [...edges].filter(([, n]) => n > 2).length;
  return { area, neg, open, over, v };
}
const terrainGroups = (T) => T.ranges.map(([c, s, n]) => ({ start: s * 3, count: n * 3, materialIndex: c }));

test('측구 메시 읽기 — 곧은 측구: 측점 셋 · 바깥 · 안 벽 · 윗면 · 안바닥 · 안 벽 줄 꼭짓점', () => {
  const M = sceneMesh([[0, 0], [10, 0], [20, 0]], 10, 9.8);
  const F = ditchFrame(M.pos, M.idx);
  assert.ok(F);
  assert.equal(F.st.length, 3);
  for (const [k, s] of F.st.entries()) {
    const inv = 10 - 0.01 * k * 10;
    assert.ok(near(s.L[1], 0.3) && near(s.R[1], -0.3), `바깥 벽 ±0.3 (${s.L} ${s.R})`);
    assert.ok(near(s.iL[1], 0.15) && near(s.iR[1], -0.15), '안 벽 ±0.15');
    assert.ok(near(s.top, inv + 0.3) && near(s.floor, inv), `윗면 ${s.top} 바닥 ${s.floor}`);
  }
  assert.equal(F.inner.reduce((a, b) => a + b, 0), 12, '측점마다 안 벽 두 줄 × 위아래');
});

test('측구 메시 읽기 — 꺾인 측구: 왼쪽 = 진행 방향 왼쪽(꺾임 안쪽) · 기초 없는 규격 · 못 읽는 메시는 null', () => {
  const F = ditchFrame(...Object.values(sceneMesh([[0, 0], [10, 0], [10, 10]], 5, 4.9, { B: 0.4, H: 0.4, Tw: 0.2, Tb: 0.2, Tf: 0 })).slice(0, 2));
  assert.ok(F && F.st.length === 3);
  assert.ok(near(F.st[1].L[0], 9.6) && near(F.st[1].L[1], 0.4), `꺾인 점 왼쪽 ${F.st[1].L}`);
  assert.ok(near(F.st[2].R[0], 10.4), '끝 오른쪽 = +x');
  assert.equal(ditchFrame(new Float32Array([0, 0, 0, 1, 0, 0, 0, 1, 0]), new Uint32Array([0, 1, 2])), null);
  assert.equal(ditchFrame(new Float32Array(0), new Uint32Array(0)), null);
});

test('지형 자르기 — 곧은 측구가 격자를 가로지름: 넓이 = 전체 − 측구 폭 × 길이 · 구멍 안 세모 없음 · 감김 · T자 이음 없음', () => {
  const T = gridTerrain(20, 20, 1, 0, 0, (x) => 10 + 0.1 * x);
  const M = sceneMesh([[-2, 10.37], [22, 10.37]], 10 + 0.1 * -2 + 0.2 - 0.3, 10 + 0.1 * 22 + 0.2 - 0.3);   // 윗면 = 지형 + 0.2
  const cut = cutTerrain(T.pos, T.idx, terrainGroups(T), holeShapes([ditchFrame(M.pos, M.idx)]));
  assert.ok(cut && cut.stats.failed === 0, JSON.stringify(cut && cut.stats));
  const S = stats(T.pos, cut);
  assert.ok(near(S.area, 400 - 20 * 0.6, 5e-3), `넓이 ${S.area}`);
  assert.equal(S.neg, 0, '뒤집힌 세모');
  assert.equal(S.over, 0, '세 번 넘게 쓴 변');
  for (let i = 0; i < cut.index.length; i += 3) {
    const a = S.v(cut.index[i]), b = S.v(cut.index[i + 1]), c = S.v(cut.index[i + 2]);
    const my = (a[1] + b[1] + c[1]) / 3;
    assert.ok(!(my > 10.07 + 1e-3 && my < 10.67 - 1e-3), `구멍 안 세모 ${my}`);
  }
  // 열린 변 = 지형 둘레 또는 구멍 테두리(y = 10.07 · 10.67)뿐 — 다른 데 있으면 T자 이음
  for (const [p, q] of S.open) {
    const onBorder = [0, 20].some(e => (near(p[0], e, 1e-6) && near(q[0], e, 1e-6)) || (near(p[1], e, 1e-6) && near(q[1], e, 1e-6)));
    const onHole = [10.07, 10.67].some(e => near(p[1], e, 2e-4) && near(q[1], e, 2e-4));
    assert.ok(onBorder || onHole, `열린 변 ${p} — ${q}`);
  }
  // 측구 윗면이 지형보다 0.2 높다 → 가장자리 = 지형 높이(측구 바깥 벽이 0.2 m 보임)
  for (let n = 0; n < cut.nv; n++) assert.ok(near(cut.xyz[3 * n + 2], 10 + 0.1 * cut.xyz[3 * n], 1e-6), '가장자리 = 지형');
});

test('지형 자르기 — 측구 윗면이 지형보다 낮으면 가장자리를 윗면까지 내림(틈 없음)', () => {
  const T = gridTerrain(10, 10, 1, 0, 0, () => 20);
  const M = sceneMesh([[-1, 5.2], [11, 5.2]], 19.5 - 0.3, 19.5 - 0.3);   // 윗면 19.5 = 지형 − 0.5
  const cut = cutTerrain(T.pos, T.idx, terrainGroups(T), holeShapes([ditchFrame(M.pos, M.idx)]));
  assert.ok(cut);
  for (let n = 0; n < cut.nv; n++) assert.ok(near(cut.xyz[3 * n + 2], 19.5, 1e-6), `새 점 높이 ${cut.xyz[3 * n + 2]}`);
  const S = stats(T.pos, cut);
  assert.ok(near(S.area, 100 - 10 * 0.6, 5e-3), `넓이 ${S.area}`);
});

test('지형 자르기 — 섬: 큰 세모 하나 안의 집수정(변에 안 닿음) → 구멍 뚫린 세모들', () => {
  const P = new Float32Array([0, 0, 5, 100, 0, 5, 0, 100, 5]), I = new Uint32Array([0, 1, 2]);
  const cut = cutTerrain(P, I, [], holeShapes([], [{ x: 20, y: 20, zTop: 5, w: 1, l: 1.2, rot: 0.3 }]));
  assert.ok(cut && cut.stats.failed === 0);
  const S = stats(P, cut);
  assert.ok(near(S.area, 5000 - 1.002 * 1.202, 1e-3), `넓이 ${S.area}(집수정 구멍 = 1 mm 넓힘)`);
  assert.equal(S.neg, 0);
  assert.equal(S.open.length, 3 + 4, '둘레 세 변 + 집수정 네 변');
  assert.equal(cut.groups.length, 1);
});

test('지형 자르기 — 십자로 겹친 측구 둘: 넓이 = 전체 − 합집합(겹친 자리 한 번)', () => {
  const T = gridTerrain(20, 20, 1, 0, 0, () => 3);
  const A = sceneMesh([[-1, 10.11], [21, 10.11]], 2.7, 2.7), B = sceneMesh([[9.93, -1], [9.93, 21]], 2.7, 2.7);
  const shapes = holeShapes([ditchFrame(A.pos, A.idx), ditchFrame(B.pos, B.idx)]);
  const cut = cutTerrain(T.pos, T.idx, terrainGroups(T), shapes);
  assert.ok(cut && cut.stats.failed === 0, JSON.stringify(cut.stats));
  const S = stats(T.pos, cut);
  assert.ok(near(S.area, 400 - (12 + 12 - 0.36), 5e-3), `넓이 ${S.area}`);
  assert.equal(S.neg, 0);
  assert.equal(S.over, 0);
});

test('지형 자르기 — 구분(재료 묶음) 그대로 · 구분 경계를 넘는 꺾인 측구도 이음 없음 · 원래 세모 번호', () => {
  const T = gridTerrain(16, 16, 1, 0, 0, (x, y) => 1 + 0.02 * y, (x) => (x < 8 ? 0 : 1));
  const M = sceneMesh([[2.13, 3.07], [12.31, 4.29], [13.7, 13.11]], 0.5, 0.45);
  const g = terrainGroups(T);
  const cut = cutTerrain(T.pos, T.idx, g, holeShapes([ditchFrame(M.pos, M.idx)]));
  assert.ok(cut && cut.stats.failed === 0);
  assert.deepEqual(cut.groups.map(x => x.materialIndex), [0, 1]);
  assert.equal(cut.groups[1].start, cut.groups[0].count);
  assert.equal(cut.faceSrc.length, cut.index.length / 3);
  // 새 세모의 원래 세모는 그 구분 안
  for (const [gi, G] of cut.groups.entries()) for (let t = G.start / 3; t < (G.start + G.count) / 3; t++) {
    const f = cut.faceSrc[t];
    assert.ok(f * 3 >= g[gi].start && f * 3 < g[gi].start + g[gi].count, '원래 세모가 같은 구분');
  }
  const S = stats(T.pos, cut);
  assert.equal(S.neg, 0);
  assert.equal(S.over, 0);
  // 열린 변 중 둘레가 아닌 것 = 구멍 테두리 — 측구 둘레 길이와 같다(T자 이음 · 구분 경계 틈이 있으면 더 길어짐)
  const holeLen = S.open.filter(([p, q]) => ![0, 16].some(e => (near(p[0], e, 1e-6) && near(q[0], e, 1e-6)) || (near(p[1], e, 1e-6) && near(q[1], e, 1e-6))))
    .reduce((a, [p, q]) => a + Math.hypot(q[0] - p[0], q[1] - p[1]), 0);
  const F = ditchFrame(M.pos, M.idx), ring = [...F.st.map(s => s.L), ...F.st.map(s => s.R).reverse()];
  const per = ring.reduce((a, p, i) => a + Math.hypot(ring[(i + 1) % ring.length][0] - p[0], ring[(i + 1) % ring.length][1] - p[1]), 0);
  assert.ok(near(holeLen, per, 2e-3), `구멍 테두리 ${holeLen} = 측구 둘레 ${per}`);
});

test('값 늘이기 — 새 점: 자리 = 정한 값 · uv2(= x, y)는 무게로 같은 값 · 법선 길이 1', () => {
  const T = gridTerrain(6, 6, 1, 0, 0, (x, y) => 0.3 * x + 0.1 * y);
  const M = sceneMesh([[-1, 3.03], [7, 3.03]], -1, -1);
  const cut = cutTerrain(T.pos, T.idx, [], holeShapes([ditchFrame(M.pos, M.idx)]));
  const V0 = T.pos.length / 3;
  const uv2 = new Float32Array(V0 * 2), nrm = new Float32Array(V0 * 3);
  for (let i = 0; i < V0; i++) { uv2[2 * i] = T.pos[3 * i]; uv2[2 * i + 1] = T.pos[3 * i + 1]; nrm.set([-0.3, -0.1, 1], 3 * i); }
  const p = extendAttribute(T.pos, 3, V0, cut, 'position'), u = extendAttribute(uv2, 2, V0, cut, 'uv2'), n = extendAttribute(nrm, 3, V0, cut, 'normal');
  assert.equal(p.length, (V0 + cut.nv) * 3);
  for (let k = 0; k < cut.nv; k++) {
    const i = V0 + k;
    assert.ok(near(u[2 * i], p[3 * i], 1e-4) && near(u[2 * i + 1], p[3 * i + 1], 1e-4), 'uv2 = x, y');
    assert.ok(near(Math.hypot(n[3 * i], n[3 * i + 1], n[3 * i + 2]), 1, 1e-5), '법선 길이');
  }
  assert.deepEqual(Array.from(p.subarray(0, V0 * 3)), Array.from(T.pos), '원래 점 그대로');
});

test('지형 자르기 — 닿는 측구가 없으면 null(원래 지형 그대로) · 10만 세모 + 긴 꺾은선 측구 시간', () => {
  const T = gridTerrain(224, 224, 1, 0, 0, (x, y) => 50 + 0.05 * x + 0.3 * Math.sin(y / 7));
  assert.equal(cutTerrain(T.pos, T.idx, [], holeShapes([], [{ x: -50, y: -50, zTop: 0, w: 1, l: 1 }])), null);
  const pts = [];
  for (let k = 0; k <= 40; k++) pts.push([10 + k * 5.1, 20 + 60 * Math.sin(k / 6) + k * 2.3]);
  const M = sceneMesh(pts, 49, 45);
  const t0 = performance.now();
  const cut = cutTerrain(T.pos, T.idx, [], holeShapes([ditchFrame(M.pos, M.idx)], [{ x: 112, y: 112, zTop: 55, w: 1.2, l: 1.2 }]));
  const ms = performance.now() - t0;
  assert.ok(cut && cut.stats.failed === 0, JSON.stringify(cut && cut.stats));
  console.log(`  10만 세모(${T.idx.length / 3}) · 측구 ${pts.length}점 — 자른 세모 ${cut.stats.tris} · 새 점 ${cut.nv} · ${ms.toFixed(0)} ms`);
  assert.ok(ms < 3000, `${ms} ms`);
  const S = stats(T.pos, cut);
  assert.equal(S.neg, 0);
  assert.equal(S.over, 0);
});

// ── ★[v117.16 · C# parts] 구간마다 덩어리 · 낙차벽 · 집수정 사이 ──
function stepped() {
  // 구간 1(x 0→10 · 바닥 10.0→9.95) · 낙차 0.2 · 구간 2(x 10→20 · 9.75→9.7, 시점 안쪽에 낙차벽) · 집수정(20~21.2) · 구간 3(x 24→30)
  const s1 = sceneMesh([[0, 5], [10, 5]], 10, 9.95), s2 = sceneMesh([[10, 5], [15, 5], [20, 5]], 9.75, 9.7), s3 = sceneMesh([[24, 5], [30, 5]], 9.4, 9.35);
  const wall = { box: [[10, 4.85], [10.15, 4.85], [10.15, 5.15], [10, 5.15]], zb: 9.75, zt: 9.95, seg: 2 };
  return joinMeshes([{ m: s1, seg: 1 }, { m: s2, seg: 2 }, { m: s3, seg: 3 }, wall]);
}

test('구간 덩어리 읽기(parts) — 본체마다 틀 · 낙차벽은 안쪽 재료 · 낙차로 맞닿은 구간은 한 줄(윗면 = 낮은 쪽) · 집수정 사이는 따로', () => {
  const M = stepped();
  const D = ditchFrames(M.pos, M.idx, M.parts);
  assert.ok(D);
  assert.deepEqual(D.frames.map(f => f.seg), [1, 2, 3]);
  assert.deepEqual(D.frames.map(f => f.st.length), [2, 3, 2]);
  const wall = M.parts.find(p => p.part === '낙차벽');
  for (let v = wall.v0; v < wall.v0 + wall.n; v++) assert.equal(D.inner[v], 1, '낙차벽 = 안쪽');
  const foot = M.parts.find(p => p.part === '기초');
  for (let v = foot.v0; v < foot.v0 + foot.n; v++) assert.equal(D.inner[v], 0, '기초 = 바깥');
  const ch = chainFrames(D.frames);
  assert.deepEqual(ch.map(c => c.map(f => f.seg)), [[1, 2], [3]]);
  const m = mergeChain(ch[0]);
  assert.equal(m.st.length, 4, '맞닿은 측점은 하나');
  assert.ok(near(m.st[1].top, 9.75 + 0.3, 1e-4), `맞닿은 윗면 = 낮은 쪽 ${m.st[1].top}`);
  assert.ok(near(ch[0][0].st[1].top, 9.95 + 0.3, 1e-4), '원래 틀은 그대로');
});

test('구간 덩어리 — parts가 없거나 범위가 틀리면 옛 한 덩어리 읽기 · 낙차 측구 지형 자르기: 겹친 선 없이 넓이 = 전체 − 구간 셋 − 집수정', () => {
  const one = sceneMesh([[0, 0], [5, 0]], 1, 0.9);
  for (const parts of [undefined, [], [{ part: '본체', seg: 1, v0: 0, n: 99999 }], [{ part: '본체', v0: 'x', n: 8 }]]) {
    const D = ditchFrames(one.pos, one.idx, parts);
    assert.ok(D && D.frames.length === 1 && D.frames[0].st.length === 2, JSON.stringify(parts));
  }
  const T = gridTerrain(32, 10, 1, 0, 0, () => 10.25);
  const M = stepped(), D = ditchFrames(M.pos, M.idx, M.parts);
  const basin = { x: 20.6, y: 5, zTop: 10.2, zBottom: 9, w: 1.2, l: 1.2 };
  const cut = cutTerrain(T.pos, T.idx, [], holeShapes(chainFrames(D.frames).map(mergeChain), [basin]));
  assert.ok(cut && cut.stats.failed === 0, JSON.stringify(cut && cut.stats));
  const S = stats(T.pos, cut);
  // 구간 1 + 2 = 20 m × 0.6 · 구간 3 = 6 m × 0.6 · 집수정 1.202² − 측구와 겹친 1 mm × 0.6
  const want = 320 - 20 * 0.6 - 6 * 0.6 - (1.202 * 1.202 - 0.001 * 0.6);
  assert.ok(near(S.area, want, 5e-3), `넓이 ${S.area} ≈ ${want}`);
  assert.equal(S.neg, 0);
  assert.equal(S.over, 0);
});

test('긴 대각선 두 점 측구(검토 M) — 지나는 칸에만 등록해도 빠짐 없음: 넓이 = 전체 − 길이 × 폭 · 구멍 테두리 = 둘레 · 이음 없음', () => {
  const T = gridTerrain(120, 120, 1, 0, 0, (x, y) => 5 + 0.01 * x + 0.02 * y);
  const M = sceneMesh([[3.3, 4.1], [116.2, 113.7]], 4.9, 6.9);
  const F = ditchFrame(M.pos, M.idx), len = Math.hypot(116.2 - 3.3, 113.7 - 4.1);
  const cut = cutTerrain(T.pos, T.idx, [], holeShapes([F]));
  assert.ok(cut && cut.stats.failed === 0);
  const S = stats(T.pos, cut);
  assert.ok(near(S.area, 14400 - len * 0.6, 1.5e-4 * len), `넓이 ${S.area} ≈ ${14400 - len * 0.6}(흔들기 ±0.05 mm × 길이)`);
  assert.equal(S.neg, 0);
  assert.equal(S.over, 0);
  const holeLen = S.open.filter(([p, q]) => ![0, 120].some(e => (near(p[0], e, 1e-6) && near(q[0], e, 1e-6)) || (near(p[1], e, 1e-6) && near(q[1], e, 1e-6))))
    .reduce((a, [p, q]) => a + Math.hypot(q[0] - p[0], q[1] - p[1]), 0);
  assert.ok(near(holeLen, 2 * len + 2 * 0.6, 2e-3), `구멍 테두리 ${holeLen}`);
});
