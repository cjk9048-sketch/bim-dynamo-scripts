/* 개략공사비 셈 (viewer/cost.js) 화면 없이 시험 — node viewer_test/check_cost_calc.js
 * 관로 m당 = 자재비 × 1.10 + 공사비 × 1.50 (2016.01 환경부 표) · 그 밖 = 표준시장단가 × 1.50 · 손셈과 견줌.
 */
'use strict';
const path = require('path');
const C = require(path.join(__dirname, '..', 'viewer', 'cost.js'));
const R = require(path.join(__dirname, '..', 'viewer', 'river.js'));

let ok = true, n = 0;
function eq(name, got, want, tol) {
  n++;
  const pass = typeof want === 'number' ? Math.abs(got - want) <= (tol == null ? 1e-6 : tol) : got === want;
  if (!pass) ok = false;
  console.log('   %s  %s  (값 %s · 손셈 %s)', pass ? '통과' : '실패', name, typeof got === 'number' ? +got.toFixed(4) : got, typeof want === 'number' ? +want.toFixed(4) : want);
}

console.log('== 관로 m당 (표 값 그대로)');
let r = C.pipeRate('ci', 300, 'asphalt');
eq('주철 D300 아스팔트 = 124,441 × 1.10 + 361,041 × 1.50', r.rate, 124441 * 1.10 + 361041 * 1.50);
eq('  표 밖 아님', r.clip, '');
eq('주철 D300 토사', C.pipeRate('ci', 300, 'soil').rate, 124441 * 1.10 + 112793 * 1.50);
eq('주철 D300 콘크리트', C.pipeRate('ci', 300, 'concrete').rate, 124441 * 1.10 + 294663 * 1.50);
eq('강관 D800 아스팔트', C.pipeRate('steel', 800, 'asphalt').rate, 604266 * 1.10 + 806578 * 1.50);
eq('비금속 = PE관 D200 아스팔트', C.pipeRate('plastic', 200, 'asphalt').rate, 63895 * 1.10 + 250844 * 1.50);
eq('  관종 이름 = PE관', C.pipeRate('plastic', 200).name, 'PE관');
eq('모르는 포장 = 아스팔트', C.pipeRate('ci', 300, 'xx').rate, 124441 * 1.10 + 361041 * 1.50);
eq('GRP D1100 콘크리트 (표 끝)', C.pipeRate('grp', 1100, 'concrete').rate, 688592 * 1.10 + 561046 * 1.50);

console.log('== 보간 · 표 밖');
r = C.pipeRate('ci', 325, 'asphalt');
eq('주철 D325 자재비 = 300 · 350 가운데', r.mat, (124441 + 152509) / 2);
eq('주철 D325 공사비', r.work, (361041 + 381303) / 2);
eq('주철 D325 m당', r.rate, (124441 + 152509) / 2 * 1.10 + (361041 + 381303) / 2 * 1.50);
r = C.pipeRate('ci', 1200, 'asphalt');
eq('주철 D1200 = 표 밖 (위)', r.clip, 'hi');
eq('  D1000 값', r.rate, 938450 * 1.10 + 812962 * 1.50);
eq('  쓴 관경 = 1000', r.Dt, 1000);
r = C.pipeRate('steel', 200, 'soil');
eq('강관 D200 = 표 밖 (아래)', r.clip, 'lo');
eq('  D300 값', r.rate, 224952 * 1.10 + 158176 * 1.50);
eq('0.3 m 부동소수 (299.99999) = 표 안', C.pipeRate('ci', 0.3 * 1000, 'asphalt').clip, '');

console.log('== 금액 (관로 · 보정 계수)');
const tot = { len: 120, tlen: 100, sh_hpile: 9, sh_hpN: 14, sh_hpA: 40, sh_sheet: 3.5, sh_spN: 18, sh_panel: 5, gd_cut: 1000, gd_fill: 500, bd_curb: 12, rv_hp: 10, rv_lag: 30, rv_spN: 0, rv_jack: 20 };
const pipes = [{ mat: 'ci', dia: 0.3, tlen: 60 }, { mat: 'steel', dia: 0.8, tlen: 40 }];
const KD = C.K_DEF;   // 기본 보정 계수 (건설공사비지수 2026.7 ÷ 2015 평균 · 1127)
eq('기본 보정 계수 = 1.64', KD, 1.64);
const prB = (124441 * 1.10 + 361041 * 1.50) * 60 + (604266 * 1.10 + 806578 * 1.50) * 40, pr = prB * KD;
let c = C.calc(tot, pipes, {});
eq('관로 본공사 = Σ m당 × 개착 길이', c.pipe.amt, pr, 1e-6);
eq('개착 길이 합', c.pipe.len, 100);
eq('  연장 줄 금액 = 관로', c.rows.len.amt, pr, 1e-6);
eq('  연장 줄 단가 = 평균 m당', c.rows.len.unit, pr / 100, 1e-6);
let c2 = C.calc(tot, pipes, { k: 1.37 });
eq('보정 계수 1.37 → 관로 × 1.37', c2.pipe.amt, prB * 1.37, 1e-6);
eq('보정 계수는 부대 공종에 안 곱함', c2.extra, c.extra, 1e-6);
eq('보정 계수 한도 밖 (50) = 기본 1.64', C.calc(tot, pipes, { k: 50 }).opt.k, KD);
const pc = (124441 * 1.10 + 294663 * 1.50) * 60 + (604266 * 1.10 + 712100 * 1.50) * 40;
eq('포장 콘크리트', C.calc(tot, pipes, { pave: 'concrete' }).pipe.amt, pc * KD, 1e-6);

console.log('== 부대 공종 (단가 × 1.50)');
const hp = (115883 + 58284) * 1.5 * 14 + (40212 + 32031) * 1.5 * 40;
eq('토류벽 = H파일 (항타 + 항발) × 본 + 흙막이판 (설치 + 철거) × m²', c.rows.sh_hpile.amt, hp, 1e-6);
eq('  토류벽 단가 = 금액 ÷ 연장', c.rows.sh_hpile.unit, hp / 9, 1e-6);
eq('시트파일 = (항타 + 항발) × 본', c.rows.sh_sheet.amt, (100112 + 59996) * 1.5 * 18, 1e-6);
eq('간이흙막이 = 공인 단가 없음', c.rows.sh_panel.st, 'none');
eq('  금액 = 없음 (NaN)', Number.isNaN(c.rows.sh_panel.amt), true);
eq('추진 = 공인 단가 없음', c.rows.rv_jack.st, 'none');
eq('하천 H-파일', c.rows.rv_hp.amt, (115883 + 58284) * 1.5 * 10, 1e-6);
eq('하천 토류판', c.rows.rv_lag.amt, (40212 + 32031) * 1.5 * 30, 1e-6);
eq('연석 = 경계블록 B-Type 500 미만', c.rows.bd_curb.amt, 16723 * 1.5 * 12, 1e-6);
eq('절토 = 흙깎기 보통토사 중규모', c.rows.gd_cut.amt, 1798 * 1.5 * 1000, 1e-6);
eq('성토 = 흙쌓기 토사 30cm', c.rows.gd_fill.amt, 2400 * 1.5 * 500, 1e-6);
eq('터파기 = 관로에 포함', c.rows.cut.st, 'incl');
eq('포장 깨기 = 관로에 포함', c.rows.pav.st, 'incl');
eq('되메우기 = 관로에 포함', c.rows.fill.st, 'incl');
eq('아스팔트 (AI) = 관로에 포함', c.rows.bd_asphalt.st, 'incl');
eq('점용 = 금액 줄 없음', c.rows.tmp, undefined);
const ex = hp + (100112 + 59996) * 1.5 * 18 + (115883 + 58284) * 1.5 * 10 + (40212 + 32031) * 1.5 * 30 + 16723 * 1.5 * 12 + 1798 * 1.5 * 1000 + 2400 * 1.5 * 500;
eq('부대 공종 합', c.extra, ex, 1e-6);
eq('개략공사비 합계 = 관로 + 부대', c.total, pr + ex, 1e-6);

console.log('== 단가 고치기');
let c3 = C.calc(tot, pipes, { price: { curb: 20000, panel: 50000 } });
eq('연석 단가 20,000 → 금액', c3.rows.bd_curb.amt, 20000 * 1.5 * 12, 1e-6);
eq('간이흙막이 직접 넣기 50,000 → 금액', c3.rows.sh_panel.amt, 50000 * 1.5 * 5, 1e-6);
eq('  상태 = 셈', c3.rows.sh_panel.st, 'ok');
eq('  합계 바뀜', c3.total, pr + ex - 16723 * 1.5 * 12 + 20000 * 1.5 * 12 + 50000 * 1.5 * 5, 1e-6);
let c4 = C.calc(tot, pipes, { price: { hp_in: null } });
eq('H파일 항타 지움 → 토류벽 = 나머지만 · 일부 없음', c4.rows.sh_hpile.amt, hp - 115883 * 1.5 * 14, 1e-6);
eq('  빠진 수 1', c4.rows.sh_hpile.miss, 1);
eq('기본과 같은 값 = 고친 것 아님', Object.keys(C.optClean({ price: { curb: 16723 } }).price).length, 0);
eq('모르는 단가 열쇠 버림', Object.keys(C.optClean({ price: { zz: 1 } }).price).length, 0);
eq('음수 단가 버림', Object.keys(C.optClean({ price: { curb: -5 } }).price).length, 0);

console.log('== 하천 횡단 시트파일 본 수 (river.js 계획)');
const P = R.path([[0, 0], [100, 0]]);
const g = (x) => (x >= 45 && x <= 55 ? 7 : 10);
const pl = R.plan({ k: 'open', a: 40, b: 60, dam: 'sheet' }, P, g, 0.5);
const zq = R.qty(pl);
let nw = 0;
for (const st of pl.stages) if (st.len > 1e-9) nw += Math.ceil(st.len / 0.4 - 1e-9);
eq('본 수 = Σ ⌈둑 길이 ÷ 0.4⌉', C.sheetN(pl), nw);
eq('  둑 길이 = river.js 가물막이', pl.stages.reduce((s, st) => s + st.len, 0), zq.dam, 1e-9);
eq('  본 수 > 0', C.sheetN(pl) > 0, true);
eq('톤마대 둑 = 0 본', C.sheetN(R.plan({ k: 'open', a: 40, b: 60, dam: 'bag' }, P, g, 0.5)), 0);
eq('추진 = 0 본', C.sheetN(R.plan({ k: 'jack', a: 30, b: 70 }, P, g, 0.5)), 0);

console.log('== 글 · 소계');
eq('원 = 반올림 · 쉼표', C.won(1234567.5), '1,234,568');
eq('억 · 만', C.wonKo(1234567890), '12억 3,456만 원');
eq('만만', C.wonKo(56780000), '5,678만 원');
eq('1만 아래 = 빈 글', C.wonKo(9999), '');
eq('억 딱', C.wonKo(300000000), '3억 원');
let s = C.sumRows(c, null);
eq('전체 = 관로 · 부대 · 합계 세 줄', s.map((x) => x[2]).join(','), 'pipe,extra,total');
eq('  합계 값', s[2][1], c.total, 1e-6);
s = C.sumRows(c, ['sh_panel', 'sh_hpile', 'sh_sheet']);
eq('가시설 소계 = 토류벽 + 시트파일 (간이 = 없음)', s[0][1], hp + (100112 + 59996) * 1.5 * 18, 1e-6);
s = C.sumRows(c, ['cut', 'sand', 'fill']);
eq('토공 소계 = 관로에 포함 글', s[0][3], '관로 본공사에 포함');
eq('점용 = 소계 없음', C.sumRows(c, ['tmp', 'perm']).length, 0);
let t = C.rowText('sh_panel', c, 'm');
eq('간이흙막이 글 = 공인 단가 없음', t.l, '공인 단가 없음 · 직접 넣기');
t = C.rowText('bd_curb', c, 'm');
eq('연석 글 = 단가 25,085원/m', t.l, '단가 ' + C.won(16723 * 1.5) + '원/m');
t = C.rowText('len', C.calc(tot, [{ mat: 'ci', dia: 1.2, tlen: 10 }], {}), 'm');
eq('표 밖 관 = 글에 «표 밖» · 경고', t.cls, 'warn');
t = C.rowText('len', C.calc(tot, [{ mat: 'ci', dia: 0.5, tlen: 30 }, { mat: 'ci', dia: 0.5, tlen: 70 }], {}), 'm');
eq('같은 관 둘 = «단가 …원/m»', t.l, '단가 ' + C.won((246898 * 1.10 + 476740 * 1.50) * KD) + '원/m');
t = C.rowText('len', c, 'm');
eq('관경 다른 관 둘 = «관 2개 · 평균 …원/m» (평균 = 금액 ÷ 개착 길이)', t.l, '관 2개 · 평균 ' + C.won(pr / 100) + '원/m');

console.log('== CSV');
const tx = C.csv({ price: { curb: 20000 } }, pipes, ',');
const lines = tx.trim().split('\r\n');
eq('줄 수 = 머리 + 관 2 + 항목 ' + C.ITEMS.length, lines.length, 1 + 2 + C.ITEMS.length);
eq('주철 D300 줄 = m당 반올림', lines[1].split(',')[4], String(Math.round(124441 * 1.10 + 361041 * 1.50)));
eq('고친 연석 = 20000 · 고친 값 표시', /경계블록.*,20000,1\.5,30000,.*고친 값/.test(tx), true);
eq('간이흙막이 = 단가 빈 칸', /조립식 간이흙막이,-,m,,1\.5,,/.test(tx), true);

console.log('\n%s (%d 개)', ok ? '모두 통과' : '실패 있음', n);
process.exit(ok ? 0 : 1);
