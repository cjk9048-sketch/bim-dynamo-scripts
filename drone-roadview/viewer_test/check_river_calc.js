/* 하천 추진 · 횡단 셈 (viewer/river.js) 화면 없이 시험 — node viewer_test/check_river_calc.js
 * 곧은 관 (0,0) → (100,0) · 땅 = 10 m · 45 ≤ x ≤ 55 만 7 m (물길) · 관 바깥지름 0.5 m 로 손셈과 견줌.
 */
'use strict';
const path = require('path');
const R = require(path.join(__dirname, '..', 'viewer', 'river.js'));

let ok = true, n = 0;
function eq(name, got, want, tol) {
  n++;
  const pass = typeof want === 'number' ? Math.abs(got - want) <= (tol == null ? 1e-6 : tol) : got === want;
  if (!pass) ok = false;
  console.log('   %s  %s  (값 %s · 손셈 %s)', pass ? '통과' : '실패', name, typeof got === 'number' ? +got.toFixed(4) : got, typeof want === 'number' ? +want.toFixed(4) : want);
}
const P = R.path([[0, 0], [40, 0], [100, 0]]);
const chan = (x) => (x >= 45 && x <= 55 ? 7 : 10);
const ground = (x) => chan(x);
const flat = () => 10;
const OD = 0.5;

console.log('== 관 줄');
eq('길이', P.len, 100);
eq('at(70).x', P.at(70).x, 70);
eq('at(70).tx', P.at(70).tx, 1);

console.log('== 기록 다듬기');
eq('모르는 종류 = null', R.clean({ k: 'x', a: 0, b: 5 }), null);
eq('너무 짧음 = null', R.clean({ k: 'jack', a: 1, b: 1.2 }), null);
const c0 = R.clean({ k: 'jack', a: 70, b: 30, cover: 99, shape: 'tri' }, 100);
eq('a < b', c0.a, 30); eq('b', c0.b, 70);
eq('토피 한도 30', c0.cover, 30); eq('모양 기본 = 사각', c0.shape, 'rect');
eq('관 길이로 자름', R.clean({ k: 'open', a: 90, b: 140 }, 100).b, 100);
eq('새 구간 = 이전 값', R.fresh('jack', 1, 9, { cover: 3.2 }).cover, 3.2);

console.log('== 추진 · 사각 (발진 3×6 · 도달 2.5×3 · 토피 2 · 여유 0.5)');
let pl = R.plan({ k: 'jack', a: 30, b: 70 }, P, ground, OD);
eq('하상 (가장 낮은 땅)', pl.gMin, 7);
eq('케이싱 윗면 = 7 − 2 (토피 = 케이싱 기준)', pl.casTop, 5, 1e-9);
eq('관 윗면 = 5 − 0.1', pl.top, 4.9, 1e-9);
eq('수직구 바닥 = 케이싱 바닥 4.3 − 0.5', pl.zb, 3.8, 1e-9);
eq('발진구 깊이 = 10 − 3.8', pl.shafts[0].depth, 6.2, 1e-9);
eq('발진구 = 시점 (30)', pl.shafts[0].s, 30);
eq('추진 길이 = 40 − 3 − 1.5', pl.jackLen, 35.5);
eq('케이싱 = 관 + 0.2', pl.cas, 0.7);
let Z = R.qty(pl);
eq('수직구 수', Z.shaft, 2);
eq('굴착 = (18 + 7.5) × 6.2', Z.exc, 25.5 * 6.2, 1e-6);
eq('H-파일 = (8+4) + (4+4)', Z.hp, 20);
eq('H-파일 m = 20 × (6.2 + 1.5)', Z.hpm, 154, 1e-6);
eq('토류판 = (18 + 11) × 6.2', Z.lag, 29 * 6.2, 1e-6);
eq('라이너 = 0', Z.liner, 0);

pl = R.plan({ k: 'jack', a: 30, b: 70, cover: 3, base: 0.8 }, P, ground, OD);
eq('토피 3 · 여유 0.8 → 바닥 = (7 − 3) − 0.7 − 0.8', pl.zb, 2.5, 1e-9);
eq('깊이 = 10 − 2.5', pl.shafts[1].depth, 7.5, 1e-9);
eq('굴착 = (18 + 7.5) × 7.5', R.qty(pl).exc, 25.5 * 7.5, 1e-6);

pl = R.plan({ k: 'jack', a: 30, b: 70, rev: true }, P, ground, OD);
eq('바꾸기 → 발진 = 종점 (70)', pl.shafts[0].s, 70);
eq('바꾸기 → 케이싱 시작 = 70 − 3', pl.c0, 67);
eq('바꾸기 → 추진 길이 같음', pl.jackLen, 35.5);

console.log('== 추진 · 원형 (발진 Ø6 · 도달 Ø4)');
pl = R.plan({ k: 'jack', a: 30, b: 70, shape: 'circ' }, P, ground, OD);
Z = R.qty(pl);
eq('추진 길이 = 40 − 3 − 2', pl.jackLen, 35);
eq('굴착 = π(9 + 4)·6.2 (케이싱 기준 깊이)', Z.exc, Math.PI * 13 * 6.2, 1e-6);
eq('라이너 m = 6.2 + 6.2', Z.liner, 12.4, 1e-9);
eq('라이너 m² = π(6 + 4)·6.2', Z.linerA, Math.PI * 10 * 6.2, 1e-6);
eq('H-파일 = 0', Z.hp, 0);
pl = R.plan({ k: 'jack', a: 30, b: 70, shape: 'circ', ld: 8, cas: 1.2 }, P, ground, OD);
eq('발진 Ø8 → 추진 길이 = 40 − 4 − 2', pl.jackLen, 34);
eq('케이싱 Ø 고침', pl.cas, 1.2);
pl = R.plan({ k: 'jack', a: 30, b: 33 }, P, ground, OD);
eq('수직구보다 짧은 구간 = 확인 글', pl.ok, false);

console.log('== 횡단 · 톤마대 (토피 1.5 · 높이 1.5 · 간격 5)');
pl = R.plan({ k: 'open', a: 30, b: 70 }, P, flat, OD);
eq('평지 관 윗면 = 10 − 1.5', pl.top, 8.5);
eq('천단 = 10 + 1.5', pl.damTop, 11.5);
eq('1단계 둑 길이 = 20 + 10 + 20', pl.stages[0].len, 50, 1e-6);
Z = R.qty(pl);
eq('가물막이 = 2 단계 × 50', Z.dam, 100, 1e-6);
eq('톤마대 = (19 + 10 + 19) 줄 × 3 개 × 2 단계', Z.bag, 288);
eq('개착 횡단', Z.open, 40);
pl = R.plan({ k: 'open', a: 30, b: 70, h: 2.0 }, P, flat, OD);
eq('높이 2.0 → 한 줄 3 단 (6 개)', R.qty(pl).bag, 48 * 6 * 2);
pl = R.plan({ k: 'open', a: 30, b: 70, w: 8 }, P, flat, OD);
eq('간격 8 → 둑 = 2 × (40 + 16)', R.qty(pl).dam, 112, 1e-6);
pl = R.plan({ k: 'open', a: 30, b: 70 }, P, ground, OD);
eq('물길: 하상 7 → 관 윗면 5.5', pl.top, 5.5);
eq('물길: 톤마대 = 물길 안 줄만 (20 줄 × 3 × 2)', R.qty(pl).bag, 120);

console.log('== 횡단 · 시트파일 (근입 3)');
pl = R.plan({ k: 'open', a: 30, b: 70, dam: 'sheet' }, P, flat, OD);
Z = R.qty(pl);
eq('시트파일 m² = 100 m × (1.5 + 3)', Z.sheet, 450, 1e-6);
eq('톤마대 = 0', Z.bag, 0);
pl = R.plan({ k: 'open', a: 30, b: 70, dam: 'sheet', emb: 5 }, P, flat, OD);
eq('근입 5 → 100 × 6.5', R.qty(pl).sheet, 650, 1e-6);

console.log('== 그림 · 3D');
for (const q of [{ k: 'jack', a: 30, b: 70 }, { k: 'jack', a: 30, b: 70, shape: 'circ' }, { k: 'open', a: 30, b: 70 }, { k: 'open', a: 30, b: 70, dam: 'sheet', st: 2 }]) {
  const p = R.plan(q, P, ground, OD), sv = R.svg(p, { W: 380 });
  let nf = 0, bad = 0;
  R.build3d(p, (ps, hint, kind) => { nf++; if (!ps.every((v) => v.every(Number.isFinite)) || !hint.every(Number.isFinite) || !kind) bad++; });
  const tag = q.k + (q.shape || q.dam || '');
  eq(tag + ' 종단 그림 글 (토피)', sv.body.indexOf('토피') >= 0, true);
  eq(tag + ' 3D 면 있음', nf > 50, true);
  eq(tag + ' 3D 면 숫자 모두 정상', bad, 0);
}
eq('땅 없음 → 그림 = 알림', R.svg(R.plan({ k: 'open', a: 30, b: 70 }, P, () => NaN, OD)).body.indexOf('땅 높이 없음') >= 0, true);
eq('표 줄 (추진)', R.rows(R.plan({ k: 'jack', a: 30, b: 70 }, P, ground, OD)).length >= 7, true);

console.log('== 검토 고침 (1126 밤)');
{
  const Pb = R.path([[0, 0], [50, 0], [50, 50]]), flat = () => 10;
  let q = R.plan({ k: 'jack', a: 10, b: 50 }, Pb, flat, OD);
  eq('꺾인 점 끝 도달구 방향 = 추진 방향 (1, 0)', [Math.round(q.shafts[1].tx), Math.round(q.shafts[1].ty)].join(','), '1,0');
  q = R.plan({ k: 'jack', a: 10, b: 50, rev: true }, Pb, flat, OD);
  eq('바꾸기 → 꺾인 점 발진구 방향 = (−1, 0)', [Math.round(q.shafts[0].tx), Math.round(q.shafts[0].ty)].join(','), '-1,0');
  q = R.plan({ k: 'jack', a: 10, b: 80 }, Pb, flat, OD);
  eq('추진 구간 안 꺾임 = 못 셈', q.ok, false);
  eq('못 셈 구간 수량 = 0', R.qty(q).shaft, 0);
  q = R.plan({ k: 'jack', a: 30, b: 70 }, P, ground, OD);
  eq('곧게 이어진 꼭짓점(40) 은 꺾임 아님', q.ok, true);
  q = R.plan({ k: 'jack', a: 30, b: 34 }, P, ground, OD);
  eq('짧은 구간 수량 = 0', R.qty(q).exc, 0);
  q = R.plan({ k: 'jack', a: 30, b: 70 }, P, (x) => (x < 32 ? NaN : 7), OD);
  eq('수직구 자리 땅 없음 = 못 셈', q.ok, false);
}

console.log('\n%s (%d 개)', ok ? '모두 통과' : '실패 있음', n);
process.exit(ok ? 0 : 1);
