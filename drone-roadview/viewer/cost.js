/* DH 에어로사이트 — 개략공사비 셈 (JACK 1127 «수량탭 이름은 개략공사비 · 수량이 나오되 공사비도 같이» · «무조건 공인된 금액으로 · 기존 설계자료 말고»)
 * 화면과 떼어 둔 셈 + 단가표 창 · (i) 설명 칸. 3D 매핑 화면 (viewer/map3d.js) 과 360 화면 (viewer/app.js) 이 같이 씀 · node 로도 시험 (module.exports).
 *
 * 단가 출처 (정부 · 공공기관 공인 자료만):
 *   관로 본공사 = 환경부 «수도시설 운영비 및 공사비 개략산정기준»(2016.01 · 17~20쪽) 관종 × 관경 × 포장 m당 «자재비 + 포장상태별 공사비»
 *                 (토피 1.5 m · 개착 · 터파기 · 되메우기 · 모래 · 포장 깨기/복구는 이 값 안 → 그 줄은 금액을 다시 더하지 않음)
 *                 2026 산정프로그램 단가 DB 는 암호라 못 씀 → 2016 판 값 · 원문에 가격 기준 연도 없음 (2015 년 가격으로 보임) → 보정 계수 (기본 1.64 = 건설공사비지수 2026.7 ÷ 2015 평균)
 *   셈 방법     = 기후에너지환경부 «수도시설 개략공사비 산정 프로그램 세부 사용 매뉴얼»(2026): 신설관로 = 자재비 × 1.10 (부가세) + 공사비 × 1.50 (제비율 50 %)
 *                 그 밖 공종 = 단가 × 1.50
 *   그 밖 공종  = 국토교통부 공고 제2026-652호 «2026년 하반기 건설공사 표준시장단가» (직접공사비 · 2026.5.8 적용 · 간접비 · 이윤 · 부가세 빠짐)
 *   공인 단가 없는 것 (조립식 간이흙막이 · 강관 압입(추진) · 수직구 · 라이너 플레이트 · 톤마대 · 석축) = 값 비움 → 단가표에서 직접 넣음 (지어내지 않음)
 *   빠진 것: 설계비 · 감리비 · 보상비 · 예비비 · 낙찰률
 *
 * 접점:
 *   RVCost.pipeRate(mat, Dmm, pave)  → {kind, name, D, Dt, mat, work, rate (원/m · 보정 계수 전), clip: '' | 'lo' | 'hi'}
 *                                       mat = 화면 관종 (ci · steel · plastic) 또는 표 이름 (ci · steel · pe · pvc · grp · sts) · 표 사이 관경 = 직선 보간 · 표 밖 = 가까운 끝 값
 *   RVCost.ITEMS                      → 그 밖 공종 기본 단가 [{id, name, spec, unit, price (null = 공인 단가 없음), src}]
 *   RVCost.ROWS                       → 수량 줄 열쇠 → 'incl' (관로에 포함) | 'sub' (다른 줄로) | [[단가 id, 수량 열쇠], …] · 없는 열쇠 = 금액 없는 줄 (점용 · 정지 면적 등)
 *   RVCost.EXTRA                      → 표 줄은 아니고 금액에만 쓰는 수량 열쇠 (부르는 쪽 lineQty 가 넣음): tlen · sh_hpN · sh_hpA · sh_spN · rv_spN
 *   RVCost.calc(tot, pipes, opt)      → {rows: {열쇠: {st, amt, unit, parts, miss}}, pipe: {amt, len, list, clip}, extra, total, opt}
 *                                       tot = 전체 수량 {열쇠: 값} · pipes = [{mat, dia (m), tlen (개착 m), label}] · opt = {pave, k, price: {id: 원 | null}}
 *   RVCost.rowText(k, C, unit) · sumRows(C, keys) → 표 밑줄 글 · 소계 / 합계 줄
 *   RVCost.won(v) · wonKo(v) · csv(opt, pipes, sep) · load() · save(opt) · optClean(o)
 *   RVCost.infoFill(el) · dlgFill(body, env)   (화면 · env = {opt(), set(o), pipes(), copy?(text), toast?(t)})
 */
(function (root) {
  'use strict';
  const K_MAT = 1.10, K_WORK = 1.50, K_ITEM = 1.50;        // 2026 산정프로그램 매뉴얼: 자재비 × 1.10 (부가세) · 공사비 × 1.50 (제비율) · 그 밖 단가 × 1.50
  const PAVE = ['asphalt', 'concrete', 'soil'];
  const PAVE_NAME = { asphalt: '아스팔트', concrete: '콘크리트', soil: '토사' };
  const PAVE_COL = { soil: 2, asphalt: 3, concrete: 4 };
  const SP_W = 0.4;                  // m, 시트파일 한 장 폭 (표준시장단가 Type3 = 400 × 150 × 13)
  const K_LIM = [0.1, 10];           // 보정 계수 범위
  // 기본 보정 계수 (JACK 1127 «2026년에 맞는 보정계수로») = 건설공사비지수 (한국건설기술연구원 · 2020 = 100) 2026.7 138.59 (잠정)
  //   ÷ 2015 연평균 84.54 (2015 = 100 계열의 100 을 2021.11 에서 접속: 2020 기준 117.19 [KICT 설명자료 2024.11 표 1] ÷ 2015 기준 138.62 [통계정보보고서 2022.1 표 4]) = 1.639 → 1.64
  //   환경부 개략산정기준 (2016.01) 의 가격 = 2015 년 가격으로 봄
  const K_DEF = 1.64, K_SRC = '건설공사비지수 2026.7 138.59 (잠정) ÷ 2015 평균 84.54 (2020 = 100 환산)';
  const STORE = 'rv_cost:v2';   // v2 = 기본 보정 계수 1.64 (v1 에 기억된 1.00 을 안 쓰게 · 1127)        // 이 PC 에 기억 (단가 고친 값 · 포장 · 보정 계수 · 3D 와 360 같이)

  // ---------------------------------------------------------------- 관로 m당 (2016.01 환경부 개략산정기준 17~20쪽 · 숫자 = 원문 그대로)
  // [관경 mm, 자재비, 공사비 토사, 공사비 아스팔트, 공사비 콘크리트] 원/m · 합계 = 자재비 + 그 포장 공사비 (원문 합계 칸과 모두 같음)
  const PIPE = {
    ci: { name: '주철관', rows: [[80, 30340, 77748, 281556, 227542], [100, 36751, 81324, 289172, 234034], [150, 56228, 90675, 308623, 250675], [200, 75270, 97268, 325316, 264558], [250, 98547, 105449, 343597, 280029], [300, 124441, 112793, 361041, 294663], [350, 152509, 122955, 381303, 312114], [400, 181534, 166224, 434672, 362674], [450, 212334, 178105, 456653, 381845], [500, 246898, 188092, 476740, 399122], [600, 313947, 216022, 524869, 441632], [700, 508994, 242505, 571552, 482695], [800, 629946, 326006, 675253, 580775], [900, 785592, 365075, 734521, 634424], [1000, 938450, 423316, 812962, 707246]] },
    steel: { name: '강관', rows: [[300, 224952, 158176, 406424, 340046], [350, 239983, 170653, 429001, 359812], [400, 279971, 213964, 482412, 410414], [450, 313042, 228375, 506923, 432115], [500, 350124, 246573, 535221, 457603], [600, 418741, 290811, 599658, 516421], [700, 483902, 351675, 680722, 591865], [800, 604266, 457331, 806578, 712100], [900, 694971, 514220, 883666, 783569], [1000, 846709, 594059, 983705, 877989], [1100, 992932, 668125, 1077971, 966634], [1200, 1160158, 761843, 1191889, 1074933], [1350, 1386928, 865668, 1326013, 1200627], [1500, 1752115, 976154, 1466799, 1332984], [1650, 2027032, 1163719, 1684663, 1542418], [1800, 2317826, 1278158, 1829403, 1678728], [2000, 2798757, 1469771, 2111914, 1935950], [2200, 3455516, 1680417, 2383159, 2190335], [2400, 4680863, 1875326, 2638668, 2428985]] },   // D2000 이상 토피 2.0 · 2.2 · 2.4
    pe: { name: 'PE관', rows: [[20, 614, 27464, 161179, 126665], [25, 876, 27767, 162088, 127405], [30, 1446, 28057, 163186, 128279], [40, 1446, 28139, 163268, 128361], [50, 3353, 29574, 167128, 131546], [75, 5270, 31423, 170593, 134561], [100, 10386, 37745, 180954, 143799], [125, 16197, 72651, 219901, 181621], [150, 33154, 79590, 234919, 194392], [200, 63895, 87435, 250844, 208069], [250, 96741, 94038, 265527, 220504], [300, 122511, 100445, 280014, 232743], [350, 153146, 107865, 295513, 245994], [400, 194436, 143342, 339070, 287303], [450, 243171, 151431, 355239, 301225], [500, 299259, 158608, 370497, 314234], [600, 468719, 176885, 404933, 344175]] },
    pvc: { name: '내충격PVC관', rows: [[16, 2607, 27022, 160737, 126223], [20, 3163, 27672, 161993, 127310], [25, 4307, 28519, 163648, 128741], [32, 5638, 28822, 163951, 129044], [40, 7406, 30378, 167932, 132350], [50, 6352, 30303, 169473, 133441], [65, 9906, 31655, 173248, 136542], [75, 12998, 33332, 176541, 139386], [100, 21214, 67440, 214690, 176410], [150, 43157, 72689, 228018, 187491], [200, 57997, 78386, 241795, 199020], [250, 86270, 86487, 257976, 212953], [300, 115852, 94091, 273660, 226389], [350, 141233, 100874, 288522, 239003], [400, 165569, 138411, 334139, 282372]] },
    grp: { name: 'GRP관', rows: [[150, 54943, 76620, 231949, 191422], [200, 68650, 83643, 247052, 204277], [250, 89305, 83692, 255181, 210158], [300, 107164, 89148, 268717, 221446], [350, 146094, 95234, 282882, 233363], [400, 179336, 131254, 326982, 275215], [450, 196193, 138968, 342776, 288762], [500, 212131, 147390, 359279, 303016], [600, 256334, 166386, 394434, 333676], [700, 329926, 184560, 428768, 363513], [800, 397058, 252956, 513324, 443574], [900, 488892, 271617, 548145, 473899], [1000, 603783, 312816, 605503, 526761], [1100, 688592, 335436, 644283, 561046]] },
    sts: { name: 'STS관', rows: [[15, 1772, 42566, 233447, 183029], [20, 2947, 36211, 170532, 135849], [25, 3660, 40656, 175785, 140878], [32, 5179, 43778, 178907, 144000], [40, 6320, 46638, 184192, 148610], [50, 7333, 51614, 190784, 154752], [65, 11364, 58571, 200164, 163458], [80, 20932, 94653, 238670, 201290], [100, 26386, 105765, 253015, 214735]] },
  };
  // 화면 관종 → 표: 주철관 · 강관 그대로 · 비금속관 = PE관 (화면은 PE · PVC · GRP 를 한 «비금속» 으로 묶음 → 상수도 비금속 관로에 가장 흔한 PE관으로 · 1127)
  const MAT_MAP = { ci: 'ci', steel: 'steel', plastic: 'pe' };
  const PIPE_SRC = '환경부 수도시설 운영비 및 공사비 개략산정기준(2016.01) 17~20쪽';

  // ---------------------------------------------------------------- 그 밖 공종 (2026 하반기 표준시장단가 · 대표 규격 하나씩 · 고칠 수 있음)
  const SMP = '표준시장단가 2026 하반기(국토교통부 공고 제2026-652호) ';
  const ITEMS = [
    { id: 'hp_in', name: 'H파일 항타 (전동식)', spec: 'H=300×300, ℓ=8m이하', unit: '본', price: 115883, src: SMP + '12쪽(토목-7) · AE110.10800' },
    { id: 'hp_out', name: 'H파일 항발 (전동식)', spec: 'H=300×300, ℓ=8m이하', unit: '본', price: 58284, src: SMP + '12쪽(토목-7) · AE110.20800' },
    { id: 'lag_in', name: '흙막이판 설치', spec: '각재 및 강재', unit: 'm²', price: 40212, src: SMP + '14쪽(토목-9) · AE130.00200' },
    { id: 'lag_out', name: '흙막이판 철거', spec: '각재 및 강재', unit: 'm²', price: 32031, src: SMP + '14쪽(토목-9) · AE130.10200' },
    { id: 'sp_in', name: '강널말뚝(쉬트파일) 항타 (전동식)', spec: 'Type3, ℓ=8m이하', unit: '본', price: 100112, src: SMP + '15쪽(토목-10) · AE141.30080' },
    { id: 'sp_out', name: '강널말뚝(쉬트파일) 항발 (전동식)', spec: 'Type3, ℓ=8m이하', unit: '본', price: 59996, src: SMP + '15쪽(토목-10) · AE146.30080' },
    { id: 'curb', name: '보차도 및 도로 경계블록', spec: 'B-Type / 아래폭+높이 500㎜ 미만', unit: 'm', price: 16723, src: SMP + '85쪽(토목-80) · LF102.10500' },
    { id: 'walk', name: '보도용 블록 포장', spec: '소형고압블록 / B-Type', unit: 'm²', price: 11011, src: SMP + '87쪽(토목-82) · LG802.10080' },
    { id: 'gcut', name: '흙깎기 / 보통토사', spec: '중규모 (10만 m³ 미만)', unit: 'm³', price: 1798, src: SMP + '23쪽(토목-18) · DD100.10000' },
    { id: 'gfill', name: '흙쌓기 / 토사', spec: '두께 30cm', unit: 'm³', price: 2400, src: SMP + '38쪽(토목-33) · DG100.10000' },
    { id: 'panel', name: '조립식 간이흙막이', spec: '-', unit: 'm', price: null, src: '공인 단가 없음 (표준시장단가에 없음)' },
    { id: 'jack', name: '강관 압입 (추진)', spec: '-', unit: 'm', price: null, src: '공인 단가 없음 (표준시장단가에 없음)' },
    { id: 'shaft', name: '수직구', spec: '-', unit: '개소', price: null, src: '공인 단가 없음 (표준시장단가에 없음)' },
    { id: 'liner', name: '라이너 플레이트', spec: '-', unit: 'm', price: null, src: '공인 단가 없음 (표준시장단가에 없음)' },
    { id: 'bag', name: '톤마대', spec: '-', unit: '개', price: null, src: '공인 단가 없음 (표준시장단가에 없음)' },
    { id: 'wall', name: '석축', spec: '-', unit: 'm²', price: null, src: '공인 단가 없음 (표준시장단가에 없음)' },
  ];
  const ITEM = {};
  ITEMS.forEach((it) => { ITEM[it.id] = it; });

  // 수량 줄 → 금액 (가시설 · 하천 H-파일 = 항타 + 항발 · 흙막이판 = 설치 + 철거 · 시트파일 = 본 수로 항타 + 항발)
  const ROWS = {
    cut: 'incl', pav: 'incl', sand: 'incl', fill: 'incl',
    sh_panel: [['panel', 'sh_panel']],
    sh_hpile: [['hp_in', 'sh_hpN'], ['hp_out', 'sh_hpN'], ['lag_in', 'sh_hpA'], ['lag_out', 'sh_hpA']],
    sh_sheet: [['sp_in', 'sh_spN'], ['sp_out', 'sh_spN']],
    rv_jack: [['jack', 'rv_jack']], rv_shaft: [['shaft', 'rv_shaft']], rv_exc: 'sub', rv_hp: [['hp_in', 'rv_hp'], ['hp_out', 'rv_hp']],
    rv_lag: [['lag_in', 'rv_lag'], ['lag_out', 'rv_lag']], rv_liner: [['liner', 'rv_liner']], rv_open: 'incl', rv_dam: 'sub', rv_bag: [['bag', 'rv_bag']],
    rv_sheet: [['sp_in', 'rv_spN'], ['sp_out', 'rv_spN']],
    bd_curb: [['curb', 'bd_curb']], bd_wall: 'sub', bd_wallA: [['wall', 'bd_wallA']], bd_sidewalk: [['walk', 'bd_sidewalk']],
    bd_asphalt: 'incl', bd_concrete: 'incl', bd_unpaved: 'incl',
    gd_cut: [['gcut', 'gd_cut']], gd_fill: [['gfill', 'gd_fill']],
  };
  const SUB_NOTE = { rv_exc: '수직구 줄에 포함', rv_dam: '톤마대 · 시트파일 줄로', bd_wall: '석축 면적 줄로' };
  const EXTRA = ['tlen', 'sh_hpN', 'sh_hpA', 'sh_spN', 'rv_spN'];
  const INFO = [
    '관로 = 환경부 «수도시설 운영비 및 공사비 개략산정기준»(2016.01) 관종 · 관경 · 포장별 m당 공사비 (토피 1.5 m · 개착)',
    '관로 금액 = 자재비 × 1.10 + 공사비 × 1.50 (2026 산정프로그램 방법) · 터파기 · 되메우기 · 포장은 이 값 안',
    '그 밖 = 2026 하반기 건설공사 표준시장단가 (국토교통부 공고 제2026-652호) × 1.50',
    '2016 기준 가격 → 보정 계수 ' + K_DEF.toFixed(2) + ' (' + K_SRC + ') · 공인 단가 없는 항목은 비움',
    '설계비 · 감리비 · 보상비 · 예비비 빠짐',
  ];

  const isNum = (v) => v !== null && v !== undefined && v !== '' && typeof v !== 'boolean' && Number.isFinite(Number(v));

  // ---------------------------------------------------------------- 관로 m당
  function pipeRate(mat, Dmm, pave) {
    const kind = PIPE[mat] ? mat : MAT_MAP[mat] || 'ci', T = PIPE[kind], R = T.rows, c = PAVE_COL[pave] || PAVE_COL.asphalt;
    const D = isNum(Dmm) && Number(Dmm) > 0 ? Number(Dmm) : R[0][0], lo = R[0], hi = R[R.length - 1];
    let m, w, Dt = D, clip = '';
    if (D <= lo[0]) { m = lo[1]; w = lo[c]; Dt = lo[0]; if (D < lo[0] - 0.5) clip = 'lo'; }
    else if (D >= hi[0]) { m = hi[1]; w = hi[c]; Dt = hi[0]; if (D > hi[0] + 0.5) clip = 'hi'; }
    else {
      let i = 0;
      while (i + 2 < R.length && R[i + 1][0] <= D) i++;
      const a = R[i], b = R[i + 1], t = (D - a[0]) / (b[0] - a[0]);
      m = a[1] + (b[1] - a[1]) * t; w = a[c] + (b[c] - a[c]) * t;
    }
    return { kind: kind, name: T.name, D: D, Dt: Dt, mat: m, work: w, rate: m * K_MAT + w * K_WORK, clip: clip };
  }

  // ---------------------------------------------------------------- 고친 값 (이 PC)
  function optClean(o) {
    const r = { pave: 'asphalt', k: K_DEF, price: {} };
    if (!o || typeof o !== 'object') return r;
    if (PAVE.indexOf(o.pave) >= 0) r.pave = o.pave;
    if (isNum(o.k) && +o.k >= K_LIM[0] && +o.k <= K_LIM[1]) r.k = +o.k;
    const p = o.price && typeof o.price === 'object' ? o.price : {};
    Object.keys(p).forEach((id) => {
      if (!ITEM[id]) return;
      if (p[id] === null) { if (ITEM[id].price != null) r.price[id] = null; }       // 공인 단가를 지운 것 = 금액 없음
      else if (isNum(p[id]) && +p[id] >= 0 && +p[id] !== ITEM[id].price) r.price[id] = +p[id];
    });
    return r;
  }
  function load() { try { const v = root.localStorage && root.localStorage.getItem(STORE); return optClean(v ? JSON.parse(v) : null); } catch (e) { return optClean(null); } }
  function save(o) { try { if (root.localStorage) root.localStorage.setItem(STORE, JSON.stringify(optClean(o))); } catch (e) { /* 기억 못 해도 계속 */ } }
  function price(o, id) {
    const p = o && o.price && Object.prototype.hasOwnProperty.call(o.price, id) ? o.price[id] : ITEM[id] ? ITEM[id].price : null;
    return isNum(p) ? Number(p) : NaN;
  }

  // ---------------------------------------------------------------- 금액
  function calc(tot, pipes, opt) {
    const o = optClean(opt), T = tot || {}, rows = {}, list = [];
    let pAmt = 0, pLen = 0, clip = 0;
    (Array.isArray(pipes) ? pipes : []).forEach((p) => {
      const r = pipeRate(p.mat, Number(p.dia) * 1000, o.pave), l = Math.max(0, Number(p.tlen) || 0), a = r.rate * o.k * l;
      pAmt += a; pLen += l; if (r.clip) clip++;
      list.push(Object.assign(r, { len: l, amt: a, label: p.label || '' }));
    });
    rows.len = { st: 'ok', amt: pAmt, unit: pLen > 0 ? pAmt / pLen : list.length ? list[0].rate * o.k : NaN, parts: [], miss: 0, clip: clip };
    let extra = 0;
    Object.keys(ROWS).forEach((k) => {
      const d = ROWS[k];
      if (typeof d === 'string') { rows[k] = { st: d, amt: 0, unit: NaN, parts: [], miss: 0 }; return; }
      let amt = 0, n = 0, miss = 0;
      const parts = d.map(([id, qk]) => {
        const p = price(o, id), q = Number(T[qk]) || 0;
        if (!Number.isFinite(p)) { miss++; return { id: id, q: q, p: NaN, amt: NaN }; }
        const a = p * K_ITEM * q;
        amt += a; n++;
        return { id: id, q: q, p: p * K_ITEM, amt: a };
      });
      const qq = Number(T[k]) || 0;
      rows[k] = { st: n ? 'ok' : 'none', amt: n ? amt : NaN, unit: n && qq > 1e-9 ? amt / qq : d.length === 1 && n ? parts[0].p : NaN, parts: parts, miss: miss };
      if (n) extra += amt;
    });
    return { rows: rows, pipe: { amt: pAmt, len: pLen, list: list, clip: clip }, extra: extra, total: pAmt + extra, opt: o };
  }
  // 하천 횡단 시트파일 본 수 (river.js 계획 · 단계마다 둑 길이 ÷ 한 장 폭 · 못 셈 구간은 0 = river.js qty 와 같음)
  function sheetN(pl) {
    if (!pl || !pl.ok || !Number.isFinite(pl.gMin) || pl.k !== 'open' || !pl.q || pl.q.dam !== 'sheet') return 0;
    let n = 0;
    for (const st of pl.stages || []) if (st.len > 1e-9) n += Math.ceil(st.len / SP_W - 1e-9);
    return n;
  }

  // ---------------------------------------------------------------- 글
  const won = (v) => (Number.isFinite(v) ? Math.round(v).toLocaleString('ko-KR') : '-');
  function wonKo(v) {                                                    // 큰 금액 = 억 · 만 (1만 원 아래 = 빈 글)
    if (!Number.isFinite(v)) return '';
    const n = Math.round(Math.abs(v)), e = Math.floor(n / 1e8), m = Math.floor((n % 1e8) / 1e4);
    const s = (e ? e.toLocaleString('ko-KR') + '억' : '') + (m ? (e ? ' ' : '') + m.toLocaleString('ko-KR') + '만' : '');
    return s ? (v < 0 ? '−' : '') + s + ' 원' : '';
  }
  const fq = (v) => (Number.isFinite(v) ? (Math.abs(v - Math.round(v)) < 1e-9 ? Math.round(v).toLocaleString('ko-KR') : v.toLocaleString('ko-KR', { maximumFractionDigits: 1 })) : '-');
  // 수량 줄 밑 글: {l (왼쪽 · 단가 또는 까닭), r (금액), cls ('' · incl · none · warn), v (금액 값), tip (자세히)}
  function rowText(k, C, unit) {
    const r = C && C.rows[k];
    if (!r) return null;
    if (r.st === 'incl') return { l: '관로 본공사에 포함', r: '', cls: 'incl', v: 0, tip: '관로 m당 공사비 안에 든 일 (따로 안 셈)' };
    if (r.st === 'sub') return { l: SUB_NOTE[k] || '다른 줄로', r: '', cls: 'incl', v: 0, tip: '' };
    if (r.st === 'none') return { l: '공인 단가 없음 · 직접 넣기', r: '-', cls: 'none', v: NaN, tip: '단가표에서 단가를 넣으면 셈' };
    if (k === 'len') {
      const L = C.pipe.list, one = L.length === 1 || L.every((p) => Math.abs(p.rate - L[0].rate) < 0.5);
      const tip = L.map((p) => (p.label ? p.label + ' · ' : '') + p.name + ' D' + Math.round(p.D) + (p.clip ? ' (표 밖 → D' + p.Dt + ' 값)' : '') + ' · ' + PAVE_NAME[C.opt.pave] +
        ' · 개착 ' + fq(p.len) + ' m × ' + won(p.rate * C.opt.k) + ' = ' + won(p.amt)).join('\n');
      // m당 단가가 하나 = «단가 …원/m» · 관마다 다름 (관경 · 관종이 섞임) = «관 N개 · 평균 …원/m» (관마다 값 = 마우스 올리면)
      return { l: (one ? '단가 ' : '관 ' + L.length + '개 · 평균 ') + won(r.unit) + '원/m' + (C.pipe.clip ? ' · 표 밖' : ''), r: won(r.amt) + '원', cls: C.pipe.clip ? 'warn' : '', v: r.amt, tip: tip };
    }
    const tip = r.parts.map((p) => { const it = ITEM[p.id]; return it.name + ' ' + it.spec + ' · ' + fq(p.q) + ' ' + it.unit + ' × ' + (Number.isFinite(p.p) ? won(p.p) : '단가 없음') + ' = ' + won(p.amt); }).join('\n');
    return { l: '단가 ' + won(r.unit) + '원/' + unit + (r.miss ? ' · 일부 단가 없음' : ''), r: won(r.amt) + '원', cls: r.miss ? 'none' : '', v: r.amt, tip: tip };
  }
  // 표 아래 소계 · 합계 줄 [[이름, 값, 열쇠 (pipe · extra · total · sub), 글 (값 대신)]] · keys = 이 갈래 줄 (null = 전체)
  function sumRows(C, keys) {
    if (!C) return [];
    if (!keys) return [['관로 본공사', C.pipe.amt, 'pipe'], ['부대 공종', C.extra, 'extra'], ['개략공사비 합계', C.total, 'total']];
    let s = 0, ok = false, incl = false;
    keys.forEach((k) => {
      const r = k === 'len' ? { st: 'ok', amt: C.pipe.amt } : C.rows[k];
      if (!r) return;
      if (r.st === 'ok') { s += r.amt; ok = true; } else if (r.st === 'incl') incl = true;
    });
    return ok ? [['소계', s, 'sub']] : incl ? [['소계', 0, 'sub', '관로 본공사에 포함']] : [];
  }

  // ---------------------------------------------------------------- CSV (단가표 내보내기)
  function csv(opt, pipes, sep) {
    const o = optClean(opt), S = sep || ',', q = (v) => { const t = String(v == null ? '' : v); return S === ',' && /[",\n]/.test(t) ? '"' + t.replace(/"/g, '""') + '"' : t.replace(/[\t\n]/g, ' '); };
    const out = [['구분', '항목', '규격', '단위', '단가(원)', '곱하는 수', '적용 단가(원)', '출처'].map(q).join(S)];
    pipeKinds(pipes).forEach(({ mat, dia }) => {
      const r = pipeRate(mat, dia * 1000, o.pave);
      out.push(['관로 본공사', r.name + ' D' + Math.round(r.D), PAVE_NAME[o.pave] + ' · 토피 1.5 m · 자재비 ' + Math.round(r.mat) + ' · 공사비 ' + Math.round(r.work) + (r.clip ? ' · 표 밖 (D' + r.Dt + ' 값)' : ''),
        'm', Math.round(r.rate), o.k, Math.round(r.rate * o.k), PIPE_SRC + ' · 자재비 × 1.10 + 공사비 × 1.50'].map(q).join(S));
    });
    ITEMS.forEach((it) => {
      const p = price(o, it.id);
      out.push(['그 밖 공종', it.name, it.spec, it.unit, Number.isFinite(p) ? p : '', K_ITEM, Number.isFinite(p) ? Math.round(p * K_ITEM) : '', it.src + (Object.prototype.hasOwnProperty.call(o.price, it.id) ? ' · 고친 값' : '')].map(q).join(S));
    });
    return out.join('\r\n') + '\r\n';
  }
  function pipeKinds(pipes) {                                            // 프로젝트 관 (관종 · 관경 같은 것 하나로)
    const seen = new Map();
    (Array.isArray(pipes) ? pipes : []).forEach((p) => {
      const kind = PIPE[p.mat] ? p.mat : MAT_MAP[p.mat] || 'ci', D = Math.round(Number(p.dia) * 1000);
      const key = kind + ':' + D;
      if (!seen.has(key)) seen.set(key, { mat: kind, dia: D / 1000 });
    });
    return [...seen.values()].sort((a, b) => (a.mat < b.mat ? -1 : a.mat > b.mat ? 1 : a.dia - b.dia));
  }

  // ---------------------------------------------------------------- 화면 (설명 칸 · 단가표 창)
  function mk(doc, tag, cls, txt) { const e = doc.createElement(tag); if (cls) e.className = cls; if (txt != null) e.textContent = txt; return e; }
  function infoFill(el) {
    const doc = el.ownerDocument, ul = mk(doc, 'ul');
    INFO.forEach((t) => ul.appendChild(mk(doc, 'li', '', t)));
    el.textContent = '';
    el.appendChild(ul);
  }
  // 단가표 창 몸: 포장 · 보정 계수 · 관로 m당 (프로젝트 관) · 그 밖 공종 단가 (고칠 수 있음 · 고친 칸 = 굵게)
  function dlgFill(body, env) {
    const doc = body.ownerDocument, o = optClean(env.opt());
    body.textContent = '';
    const set = (fn) => { const n = optClean(env.opt()); fn(n); env.set(optClean(n)); };
    const top = mk(doc, 'div', 'cpTop');
    const lp = mk(doc, 'label', 'cl', '포장 '), sp = mk(doc, 'select');
    sp.id = 'cpPave';
    PAVE.forEach((k) => { const op = mk(doc, 'option', '', PAVE_NAME[k]); op.value = k; sp.appendChild(op); });
    sp.value = o.pave;
    sp.addEventListener('change', () => { set((n) => { n.pave = sp.value; }); dlgFill(body, env); });
    lp.appendChild(sp);
    const lk = mk(doc, 'label', 'cl', '보정 계수 '), ik = mk(doc, 'input');
    ik.id = 'cpK'; ik.type = 'number'; ik.step = '0.01'; ik.min = String(K_LIM[0]); ik.max = String(K_LIM[1]); ik.value = String(o.k);
    ik.title = '관로 본공사에 곱함 · 2016 기준 → 건설공사비지수 비율 넣기';
    ik.addEventListener('change', () => { if (isNum(ik.value)) set((n) => { n.k = +ik.value; }); dlgFill(body, env); });
    lk.appendChild(ik);
    top.append(lp, lk);
    body.appendChild(top);
    body.appendChild(mk(doc, 'div', 'cpHint', '보정 계수 = 관로 본공사에 곱함 · 기본 ' + K_DEF.toFixed(2) + ' = ' + K_SRC));

    body.appendChild(mk(doc, 'div', 'cpSec', '관로 본공사 · m당 (자재비 × 1.10 + 공사비 × 1.50)'));
    const pt = mk(doc, 'div', 'cpTab cpPipe');
    ['관', '자재비', '공사비', 'm당'].forEach((t) => pt.appendChild(mk(doc, 'span', 'h' + (t === '관' ? '' : ' r'), t)));
    const pk = pipeKinds(env.pipes());
    if (!pk.length) { const e = mk(doc, 'span', 'cpNone', '관로 없음'); pt.appendChild(e); }
    pk.forEach(({ mat, dia }) => {
      const r = pipeRate(mat, dia * 1000, o.pave), nm = mk(doc, 'span', 'nm');
      nm.appendChild(mk(doc, 'b', '', r.name + ' D' + Math.round(r.D)));
      nm.appendChild(mk(doc, 'small', r.clip ? 'w' : '', r.clip ? '표 밖 → D' + r.Dt + ' 값' : PAVE_NAME[o.pave] + ' · 토피 1.5 m'));
      pt.append(nm, mk(doc, 'span', 'r', won(r.mat)), mk(doc, 'span', 'r', won(r.work)), mk(doc, 'span', 'r b', won(r.rate * o.k)));
    });
    body.appendChild(pt);
    body.appendChild(mk(doc, 'div', 'cpSrc', '출처 · ' + PIPE_SRC + ' · 셈 방법 = 2026 산정프로그램 세부 사용 매뉴얼'));

    body.appendChild(mk(doc, 'div', 'cpSec', '그 밖 공종 · 단가 × 1.50'));
    const it = mk(doc, 'div', 'cpTab cpItems');
    ['항목 · 규격 · 출처', '단위', '단가 (원)'].forEach((t, i) => it.appendChild(mk(doc, 'span', 'h' + (i ? ' r' : ''), t)));
    ITEMS.forEach((I) => {
      const p = price(o, I.id), mod = Object.prototype.hasOwnProperty.call(o.price, I.id), nm = mk(doc, 'span', 'nm');
      nm.appendChild(mk(doc, 'b', '', I.name));
      nm.appendChild(mk(doc, 'small', '', I.spec === '-' ? I.src : I.spec));
      if (I.spec !== '-') nm.appendChild(mk(doc, 'small', 'src', I.src));
      const inp = mk(doc, 'input', mod ? 'mod' : '');
      inp.type = 'number'; inp.min = '0'; inp.step = '1'; inp.id = 'cp_' + I.id; inp.dataset.id = I.id;
      inp.value = Number.isFinite(p) ? String(p) : '';
      inp.placeholder = I.price == null ? '직접 넣기' : String(I.price);
      inp.title = I.price == null ? '공인 단가 없음 · 직접 넣기' : '기본 ' + won(I.price) + '원';
      inp.addEventListener('change', () => {
        const v = inp.value.trim();
        set((n) => { if (v === '') { if (I.price == null) delete n.price[I.id]; else n.price[I.id] = null; } else if (isNum(v) && +v >= 0) n.price[I.id] = +v; });
        const n2 = optClean(env.opt());
        inp.classList.toggle('mod', Object.prototype.hasOwnProperty.call(n2.price, I.id));
      });
      it.append(nm, mk(doc, 'span', 'u', I.unit), inp);
    });
    body.appendChild(it);
  }
  function dlgDefault(env) { const o = optClean(env.opt()); env.set({ pave: o.pave, k: K_DEF, price: {} }); }
  function dlgCsv(env) {
    const o = env.opt(), pipes = env.pipes();
    if (env.copy) { env.copy(csv(o, pipes, '\t')); return; }                    // 프로그램 안 = 클립보드 (엑셀에 붙여넣기)
    const doc = root.document, url = URL.createObjectURL(new Blob(['﻿' + csv(o, pipes, ',')], { type: 'text/csv;charset=utf-8' }));
    const a = doc.createElement('a');
    a.href = url; a.download = '단가표.csv'; doc.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 2000);
    if (env.toast) env.toast('내려받기 · 단가표.csv');
  }

  const API = { K_DEF: K_DEF, K_SRC: K_SRC, K_MAT: K_MAT, K_WORK: K_WORK, K_ITEM: K_ITEM, PAVE: PAVE, PAVE_NAME: PAVE_NAME, SP_W: SP_W, PIPE: PIPE, MAT_MAP: MAT_MAP, ITEMS: ITEMS, ITEM: ITEM,
    ROWS: ROWS, EXTRA: EXTRA, INFO: INFO, STORE: STORE, pipeRate: pipeRate, optClean: optClean, load: load, save: save, price: price, calc: calc, sheetN: sheetN,
    won: won, wonKo: wonKo, rowText: rowText, sumRows: sumRows, csv: csv, pipeKinds: pipeKinds, infoFill: infoFill, dlgFill: dlgFill, dlgDefault: dlgDefault, dlgCsv: dlgCsv };
  root.RVCost = API;
  if (typeof module !== 'undefined' && module.exports) module.exports = API;
})(typeof window !== 'undefined' ? window : globalThis);
