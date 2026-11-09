// ★[v115 · JACK 1105 임상도] 수목 뷰어 시험 — node --test test/ (화면 없이 · three는 그리기 없이 물체만)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import { treeGeo, proxyGeo, pickLod, hideDistance, keepCount, KIND_COUNT, proxyOf } from '../src/treegeo.js';
import { forestModel, buildForest, treeMaterials, TRI_BUDGET } from '../src/forest.js';
import { makeTreeAtlas, tileRect, TILES, TEX_SIZE, TILE } from '../src/treetex.js';

// ★[v116] 잎 카드 뭉치 — 점 · 법선 · uv · bb(뭉치 벌림) · 색이 다 있고 뭉치 네모(점 6개)는 한 가운데를 나눈다
function checkCards(g, name) {
  const n = g.pos.length / 3;
  assert.equal(g.nrm.length, 3 * n); assert.equal(g.uv.length, 2 * n); assert.equal(g.bb.length, 2 * n); assert.equal(g.col.length, 3 * n);
  assert.equal(n % 3, 0, `${name} 세모 단위`);
  let cards = 0, bbMax = 0, zTop = -Infinity;
  for (let i = 0; i < n; i++) {
    for (let c = 0; c < 3; c++) assert.ok(Number.isFinite(g.pos[3 * i + c]) && Number.isFinite(g.nrm[3 * i + c]), `${name} NaN`);
    assert.ok(Math.abs(Math.hypot(g.nrm[3 * i], g.nrm[3 * i + 1], g.nrm[3 * i + 2]) - 1) < 1e-4, `${name} 법선 길이`);
    assert.ok(g.uv[2 * i] >= 0 && g.uv[2 * i] <= 1 && g.uv[2 * i + 1] >= 0 && g.uv[2 * i + 1] <= 1, `${name} uv`);
    for (let c = 0; c < 3; c++) assert.ok(g.col[3 * i + c] >= 0 && g.col[3 * i + c] <= 1, `${name} 색`);
    bbMax = Math.max(bbMax, Math.hypot(g.bb[2 * i], g.bb[2 * i + 1]));
  }
  for (let q = 0; q < n; q += 6) {
    const isCard = Math.hypot(g.bb[2 * q], g.bb[2 * q + 1]) > 0;
    if (!isCard) continue;
    cards++;
    for (let k = 1; k < 6; k++) for (let c = 0; c < 3; c++) assert.equal(g.pos[3 * (q + k) + c], g.pos[3 * q + c], `${name} 뭉치 ${q / 6} 가운데가 하나`);
    zTop = Math.max(zTop, g.pos[3 * q + 2]);
  }
  assert.ok(Math.abs(bbMax - g.bbMax) < 1e-6, `${name} bbMax`);
  return { cards, zTop };
}

test('나무 모양 — 잎 카드 뭉치 · 세모 예산 · 밑 0 · 키 1 이하 · NaN 없음', () => {
  for (let k = 0; k < KIND_COUNT; k++) {
    const g = treeGeo(k);
    assert.ok(g.tris >= 50 && g.tris <= 160, `모양 ${k} 세모 ${g.tris}`);
    const { cards, zTop } = checkCards(g, `모양 ${k}`);
    assert.ok(cards >= 14 && cards <= 40, `모양 ${k} 뭉치 ${cards}`);
    let zMin = Infinity, zMax = -Infinity, rMax = 0;
    for (let i = 0; i < g.pos.length; i += 3) {
      zMin = Math.min(zMin, g.pos[i + 2]); zMax = Math.max(zMax, g.pos[i + 2]);
      rMax = Math.max(rMax, Math.hypot(g.pos[i], g.pos[i + 1]) + Math.hypot(g.bb[(i / 3) * 2], g.bb[(i / 3) * 2 + 1]));
    }
    assert.equal(zMin, 0, `모양 ${k} 밑 ${zMin}`);
    assert.ok(zMax <= 1 + 1e-6 && zTop > 0.75, `모양 ${k} 키 ${zMax} · 꼭대기 뭉치 ${zTop}`);
    assert.ok(rMax > 0.3 && rMax < 1.0, `모양 ${k} 수관 반지름(뭉치 끝까지) ${rMax}`);
  }
  for (const p of [0, 1]) { const g = proxyGeo(p); assert.ok(g.tris >= 8 && g.tris <= 12, `대리 ${p} ${g.tris}`); checkCards(g, `대리 ${p}`); }
  assert.deepEqual([0, 1, 2, 3, 8].map(proxyOf), [0, 0, 0, 1, 1]);
});

test('나무 질감 아틀라스 — 단계 크기 · 같은 입력 같은 그림 · 껍질 불투명 · 멀리서도 잎 덮임 유지 · 투명 화소 색 채움', () => {
  const A = makeTreeAtlas(), B = makeTreeAtlas();
  assert.equal(A.size, TEX_SIZE);
  assert.deepEqual(A.levels.map(l => l.width), [1024, 512, 256, 128, 64, 32, 16, 8, 4, 2, 1]);
  for (const l of A.levels) assert.equal(l.data.length, l.width * l.height * 4);
  assert.ok(A.levels.every((l, i) => Buffer.compare(Buffer.from(l.data), Buffer.from(B.levels[i].data)) === 0), '같은 입력 = 같은 그림');
  const tileCov = (L, t) => {   // 단계 L에서 칸 t의 «알파 ≥ 0.5» 비율
    const lv = A.levels[L], w = TILE >> L, c = t % 4, r = Math.floor(t / 4);
    let n = 0;
    for (let y = 0; y < w; y++) for (let x = 0; x < w; x++) if (lv.data[((r * w + y) * lv.width + c * w + x) * 4 + 3] >= 128) n++;
    return n / (w * w);
  };
  for (const t of [TILES.bark, TILES.barkPine, TILES.culm, TILES.barkLight]) assert.equal(tileCov(0, t), 1, `껍질 칸 ${t} 불투명`);
  for (const t of [...TILES.broad, ...TILES.conifer, ...TILES.pine, ...TILES.bamboo]) {
    const c0 = tileCov(0, t);
    assert.ok(c0 > 0.25 && c0 < 0.85, `잎 칸 ${t} 덮임 ${c0}`);
    for (const L of [2, 4, 5]) { const c = tileCov(L, t); assert.ok(Math.abs(c - c0) < 0.12, `잎 칸 ${t} 단계 ${L} 덮임 ${c.toFixed(2)} ↔ ${c0.toFixed(2)}`); }
  }
  // 알파 0 화소도 색은 잎 평균(검은 테두리 방지)
  const l0 = A.levels[0].data;
  let black = 0, clear = 0;
  for (let i = 0; i < l0.length; i += 4) if (l0[i + 3] === 0) { clear++; if (l0[i] + l0[i + 1] + l0[i + 2] < 30) black++; }
  assert.ok(clear > 0 && black === 0, `투명 화소 ${clear} 중 검은 것 ${black}`);
  const [u0, v0, u1, v1] = tileRect(TILES.broad[0]);
  assert.ok(u0 > 0 && v0 > 0 && u1 < 0.25 && v1 < 0.25);
});

test('나무 재료 — 뭉치 벌림 셰이더 · 그림자 깊이 재료 · 알파 투 커버리지', () => {
  const { mat, depth } = treeMaterials(null);
  assert.ok(mat.map && mat.map.mipmaps.length === 11 && mat.map.generateMipmaps === false);
  assert.equal(mat.alphaToCoverage, true);
  assert.equal(mat.alphaTest, 0.5);
  assert.equal(mat.shadowSide, THREE.DoubleSide);   // 해 쪽으로 벌린 뭉치가 그림자 패스에서 뒷면으로 잘리지 않게
  const sh = { vertexShader: THREE.ShaderLib.standard.vertexShader, fragmentShader: THREE.ShaderLib.standard.fragmentShader, uniforms: {} };
  mat.onBeforeCompile(sh);
  assert.ok(/attribute vec2 bb;/.test(sh.vertexShader) && /dhW\.xyz \+= dhOff/.test(sh.vertexShader) && /worldPosition = dhW/.test(sh.vertexShader) && /normalize\( bb \)/.test(sh.vertexShader));
  assert.ok(!/#include <project_vertex>/.test(sh.vertexShader) && !/#include <worldpos_vertex>/.test(sh.vertexShader));
  const dsh = { vertexShader: THREE.ShaderLib.depth.vertexShader, fragmentShader: THREE.ShaderLib.depth.fragmentShader, uniforms: {} };
  depth.onBeforeCompile(dsh);
  assert.ok(/dhW\.xyz \+= dhOff/.test(dsh.vertexShader) && !/#include <project_vertex>/.test(dsh.vertexShader));
});

test('LOD 고르기 — 가까운 덩이부터 예산까지 자세히 · 나머지 대리 · 숨김 거리', () => {
  const ch = [{ d: 500, tris0: 400 }, { d: 10, tris0: 400 }, { d: 100, tris0: 400 }, { d: 5000, tris0: 400 }, { d: 200, tris0: 400 }];
  const st = pickLod(ch, 1000, 3000);
  assert.deepEqual(st, ['lod1', 'lod0', 'lod0', 'hidden', 'lod1']);   // 400 + 400 = 800 · 다음 400 → 1200 > 1100(예산 + 10 %)
  assert.deepEqual(pickLod([{ d: 1, tris0: 1050 }, { d: 2, tris0: 10 }], 1000, 3000), ['lod0', 'lod1']);   // ±10 % 안 → 넣고 꽉 참
  assert.deepEqual(pickLod([{ d: 100, tris0: 10 }, { d: 900, tris0: 10 }], 1000, 3000, 800), ['lod0', 'lod1']);   // 800 m 밖 = 대리
  assert.equal(hideDistance(100), 3000);
  assert.equal(hideDistance(4000), 6000);
});

test('밀도 → 수', () => {
  assert.equal(keepCount(100, 1), 100);
  assert.equal(keepCount(100, 0.25), 25);
  assert.equal(keepCount(7, 0.5), 4);
  assert.equal(keepCount(0, 0.5), 0);
});

// 가짜 머리 — 덩이 셋(가까움 · 가운데 · 멂) × 모양 둘
function fakeHead(per = 50) {
  const xs = [], zs = [], hw = [], k = [], r = [], t = [], grp = [], cbox = [], czr = [];
  const chunks = [[0, 0], [1000, 0], [6000, 0]];
  let i = 0;
  chunks.forEach(([cx, cy], c) => {
    cbox.push(cx, cy, cx + 250, cy + 250); czr.push(10, 30);
    for (const kind of [0, 3]) {
      grp.push(i, per, c);
      for (let j = 0; j < per; j++, i++) { xs.push(cx + 5 + j, cy + 5 + (kind ? 100 : 0)); zs.push(10); hw.push(75, 25); k.push(kind); r.push(j * 5 % 256); t.push(128); }
    }
  });
  return { n: i, xy: new Float32Array(xs), z: new Float32Array(zs), hw: new Uint8Array(hw), k: new Uint8Array(k), r: new Uint8Array(r), t: new Uint8Array(t), grp: new Uint32Array(grp), cbox: new Float32Array(cbox), czr: new Float32Array(czr), note: '임상도 © 산림청', legend: [] };
}

test('head.forest 풀기 — 행렬(이동 · 늘임) · 무리 · 덩이', () => {
  const F = fakeHead();
  const m = forestModel(F);
  assert.equal(m.groups.length, 6);
  assert.equal(m.chunks.length, 3);
  const G = m.groups[0];
  const mat = new THREE.Matrix4().fromArray(G.mat, 0);
  const p = new THREE.Vector3(), q = new THREE.Quaternion(), s = new THREE.Vector3();
  mat.decompose(p, q, s);
  assert.ok(Math.abs(p.x - 5) < 1e-4 && Math.abs(p.z - 10) < 1e-4);
  assert.ok(Math.abs(s.x - 5) < 1e-4 && Math.abs(s.z - 15) < 1e-4, `늘임 ${s.toArray()}`);   // W = 25 × 0.2 · H = 75 × 0.2
});

function fakeView() {
  const camera = new THREE.PerspectiveCamera(45, 1.6, 0.1, 10000);
  camera.position.set(100, -200, 150);
  return { scene: new THREE.Scene(), camera, controls: { target: new THREE.Vector3(100, 100, 10) }, _f: [], onDraw(fn) { this._f.push(fn); }, request() {}, draw() { for (const f of this._f) f(); } };
}

test('층 — 덩이 상태 · 대리 묶음 다시 채우기(N3) · 밀도', () => {
  const view = fakeView(), L = {};
  buildForest(view, { forest: fakeHead(50) }, L);
  assert.ok(L.tree && L.tree.obj && L.tree.n === 300);
  view.draw();
  let s = L.tree.stats();
  assert.equal(s.lod0 + s.lod1 + s.hidden, 3);
  assert.equal(s.hidden, 1, '6 km 덩이는 숨김');
  assert.ok(s.treeTris <= TRI_BUDGET * 1.1);
  // 가까운 덩이 = 자세히 · 1 km 덩이(사진기에서 800 m 밖) = 대리
  assert.deepEqual([s.lod0, s.lod1], [1, 1]);
  // 사진기를 멀리 → 숨김 거리가 늘어 다 보임 · 모두 800 m 밖이라 대리
  view.camera.position.set(100, -5000, 3000); view.controls.target.set(100, 100, 10);
  view.draw();
  s = L.tree.stats();
  assert.deepEqual([s.lod0, s.lod1, s.hidden], [0, 3, 0]);
  // 밀도 50 % — 자세히 무리 수 = ⌈0.5 n⌉
  L.tree.setDensity(0.5);
  view.draw();
  s = L.tree.stats();
  assert.equal(s.lod0Trees + s.lod1Trees, 6 * 25);
  // 대리 묶음 — 강제로 대리 상태를 만들려면 예산을 넘겨야 함: 무리를 크게(덩이마다 2만 그루)
  //   사진기 = 가까운 덩이 300 m 앞 → 가까운 덩이 자세히(1.64백만 ≤ 예산 + 10 %) · 1 km 덩이(800 m 밖) 대리 · 6 km 덩이 숨김
  const v2 = fakeView(), L2 = {};
  buildForest(v2, { forest: fakeHead(10000) }, L2);
  v2.camera.position.set(100, -300, 200);
  v2.draw();
  let s2 = L2.tree.stats();
  assert.deepEqual([s2.lod0, s2.lod1, s2.hidden], [1, 1, 1], `덩이 ${JSON.stringify(s2)}`);
  const lod1Meshes = () => L2.tree.obj.children.filter(o => o.userData.lod === 1 && o.visible);
  const sumLod1 = () => lod1Meshes().reduce((a, o) => a + o.count, 0);
  assert.equal(sumLod1(), s2.lod1Trees);
  // 대리 행렬 = 원래 무리 앞부분 그대로(첫 그루 행렬이 어느 무리의 첫 그루와 같음)
  const m = forestModel(fakeHead(10000));
  const first = lod1Meshes()[0].instanceMatrix.array.subarray(0, 16);
  assert.ok(m.groups.some(G => G.mat.subarray(0, 16).every((v, i) => v === first[i])));
  // 밀도 60 % — 대리 묶음을 원래 무리에서 다시 채움(무리마다 ⌈0.6 n⌉) · 자세히 무리도 같은 수
  L2.tree.setDensity(0.6);
  v2.draw();
  s2 = L2.tree.stats();
  assert.equal(sumLod1(), s2.lod1Trees);
  assert.equal(s2.lod1Trees, s2.lod1 * 2 * Math.ceil(0.6 * 10000), '대리 = 원래 무리마다 앞 ⌈f·n⌉');
  assert.equal(s2.lod0Trees, s2.lod0 * 2 * Math.ceil(0.6 * 10000));
  // 25 % — 같은 덩이 상태 · 무리마다 2,500
  L2.tree.setDensity(0.25);
  v2.draw();
  s2 = L2.tree.stats();
  assert.deepEqual([s2.lod0Trees, s2.lod1Trees, sumLod1()], [2 * 2500, 2 * 2500, 2 * 2500]);
});

test('그릴 때 고르기에서 오류 → 수목만 숨김 · 층 회색(L16) · 3D는 그대로', () => {
  const view = fakeView(), L = {};
  buildForest(view, { forest: fakeHead(50) }, L);
  const row = { className: 'lay', title: '' }, cb = { checked: true, disabled: false };
  L.tree.row = row; L.tree.cb = cb;
  view.controls.target = null;   // distanceTo(null) → 던짐
  assert.doesNotThrow(() => view.draw());
  assert.equal(L.tree.obj.visible, false);
  assert.equal(L.tree.missing, '수목을 못 그렸습니다');
  assert.deepEqual([row.className, cb.checked, cb.disabled], ['lay miss', false, true]);
  assert.doesNotThrow(() => view.draw());   // 다음 장부터는 건너뜀
});

test('없음 · quiet 머리 → 회색 층', () => {
  const L = {};
  buildForest(fakeView(), { forest: { missing: '임상도 타일 없음', quiet: true } }, L);
  assert.deepEqual([L.tree.missing, L.tree.quiet, L.tree.on], ['임상도 타일 없음', true, false]);
  const L2 = {};
  buildForest(fakeView(), {}, L2);
  assert.equal(L2.tree, undefined);
});
