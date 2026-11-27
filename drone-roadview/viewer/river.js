/* 드론 로드뷰 — 하천 추진 · 횡단 셈 (JACK 1126 «시점 종점 찍으면 설정값에 맞춰 추진구 · 도달구 · 하천 횡단 = 가물막이»)
 * 화면과 떼어 둔 셈만: 기하 (깊이 · 수직구 · 가물막이 둑) · 수량 · 종단 그림 (SVG 글) · 3D 면 (부르는 쪽이 그림 그릇에 담음).
 * 3D 매핑 화면 (viewer/map3d.js) 과 360 화면 (viewer/app.js) 이 같이 씀 · node 로도 시험 (module.exports).
 *
 * 접점 (360 에 붙일 때 이것만 쓰면 됨):
 *   RVRiver.clean(q, len)            → 구간 기록 다듬기 (모자란 값 = 기본값) · 못 쓰면 null
 *   RVRiver.path([[x, y], …])         → 관 줄 { len, at(s) → {x, y, tx, ty} }  (s = 관 처음부터 평면 거리 m)
 *   RVRiver.plan(q, path, ground, od) → 계획 (ground(x, y) = 땅 표고 절대 · 없으면 NaN · od = 관 바깥지름 m)
 *                                       ta · tb = 터파기가 빠지는 측점 구간 (추진 = 두 수직구 바깥 벽 사이 · 횡단 = a · b)
 *                                       foot = 추진 수직구 바닥 자리 [{poly: [[x, y], …] 반시계, area}] (터파기 영역 · 임시점용 · 포장 깨기 · AI 영역)
 *   RVRiver.qty(plan)                 → 수량 {jack, shaft, exc, hp, hpm, lag, ssp (수직구 시트파일 장), sspm (그 길이 합 m), liner, linerA, open, dam, bag, sheet}
 *   RVRiver.rows(plan)                → 이 구간 표 [[이름, 값 글], …]
 *   RVRiver.svg(plan, {W, col})       → 종단 그림 {w, h, body} (svg viewBox = 0 0 w h · innerHTML = body)
 *   RVRiver.build3d(plan, face)       → face(점들 [[x, y, z] ×3~4], 바깥 쪽 [dx, dy, dz], 재질 이름) 을 면마다 부름
 *                                       재질 이름 = wood · steel · hpile · liner · conc · case · bag · sheet
 * 구간 기록 (RV_PIPES.lines[i].river 한 칸 · 관 측점 a < b):
 *   추진 {k:'jack', a, b, shape:'rect'|'circ', wall:'hpile'|'sheet' (사각 흙막이 = 토류벽 · 시트파일 · 없으면 토류벽 · 원형은 안 씀 · JACK 1127),
 *         rev (true = 종점 쪽이 발진구), cover (하상 → 관 윗면), base (관 바닥 → 수직구 바닥),
 *         cas (케이싱 바깥지름 · 0 = 관 + 0.2), lw × ll (발진 사각 내공 폭 × 길이), aw × al (도달), ld · ad (원형 내경)}
 *   횡단 {k:'open', a, b, dam:'bag'|'sheet', st:1|2 (보이는 단계), cover, h (천단 = 구간 가장 낮은 땅 + h), w (관 → 둑 간격), emb (시트파일 근입)}
 * 좌표 = 부르는 쪽 그대로 (x = 동 · y = 북 · z = 표고 절대).
 */
(function (root) {
  'use strict';
  const KINDS = ['jack', 'open'];
  const NAME = { jack: '하천 추진', open: '하천 횡단' };
  const DEF = {
    jack: { shape: 'rect', wall: 'hpile', rev: false, cover: 2.0, base: 0.5, cas: 0, lw: 3.0, ll: 6.0, aw: 2.5, al: 3.0, ld: 6.0, ad: 4.0 },
    open: { dam: 'bag', st: 1, cover: 1.5, h: 1.5, w: 5.0, emb: 3.0 },
  };
  const OPTS = { shape: ['rect', 'circ'], wall: ['hpile', 'sheet'], dam: ['bag', 'sheet'] };
  const LIM = { cover: [0, 30], base: [0, 5], cas: [0, 6], lw: [0.5, 30], ll: [0.5, 30], aw: [0.5, 30], al: [0.5, 30], ld: [0.5, 30], ad: [0.5, 30],
    h: [0.2, 10], w: [0.5, 50], emb: [0, 30] };
  const MIN_LEN = 0.5;               // m, 구간 가장 짧은 것
  const STEP = 0.5;                  // m, 땅 높이 보는 간격
  const HP_PITCH = 1.8;              // m, 수직구 H-파일 간격 (가시설 토류벽과 같음)
  const HP_EMB = 1.5;                // m, H-파일 근입 (수직구 바닥 아래)
  const BAG_W = 1.1, BAG_H = 0.8;    // m, 톤마대 한 개 자리 (가로 · 세로 간격) · 한 단 높이 · 쌓기 = 피라미드 (맨 위 1 줄 · 한 단 내려갈 때마다 1 줄 더)
  const SP_PITCH = 0.8;              // m, 시트파일 3D 골 한 주기 (두 장)
  const SP_W = 0.4;                  // m, 시트파일 한 장 유효폭 (Type3 400 · cost.js SP_W 와 같음) · 수직구 장 수 = 둘레 ÷ 이 값 올림
  const SP_EMB = 3.0;                // m, 수직구 시트파일 근입 (수직구 바닥 아래 · 가시설 시트파일 기본 근입과 같음)
  const WALL_C = 0.23;               // m, 사각 수직구 내공 면 → 흙막이 줄 (토류판 0.08 + H-파일 반폭 0.15 · 시트파일도 이 줄)
  // 수직구 바깥 벽 (가운데 → 관 방향 바깥 면): 사각 = 내공 l/2 + 토류판 0.08 + H-파일 중심 0.15 + 플랜지 0.15 + 2 cm · 원형 = 내경/2 + 라이너 0.05 + 2 cm
  // 터파기는 여기서 끝남 (수직구 안으로 안 들어감 · 수직구 자리 = 수직구 굴착으로만 셈 · JACK 1127 «터파기와 겹쳐져서 이상»)
  const SHAFT_OUT_RECT = 0.4, SHAFT_OUT_CIRC = 0.07;
  const shaftOut = (sh) => (sh.shape === 'rect' ? sh.l / 2 + SHAFT_OUT_RECT : sh.w / 2 + SHAFT_OUT_CIRC);

  const isNum = (v) => v !== null && v !== undefined && v !== '' && typeof v !== 'boolean' && Number.isFinite(Number(v));
  const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
  const round = (v, k) => { const m = Math.pow(10, k); return Math.round(v * m) / m; };
  const ceilN = (v) => Math.max(1, Math.ceil(v - 1e-9));
  const f2 = (v) => (Number.isFinite(v) ? v.toFixed(2) : '-');

  // ---------------------------------------------------------------- 구간 기록 다듬기
  function clean(q, len) {
    if (!q || KINDS.indexOf(q.k) < 0 || !isNum(q.a) || !isNum(q.b)) return null;
    let a = Math.min(+q.a, +q.b), b = Math.max(+q.a, +q.b);
    if (Number.isFinite(len)) { a = clamp(a, 0, len); b = clamp(b, 0, len); }
    if (!(b - a >= MIN_LEN - 1e-9)) return null;
    const d = DEF[q.k], o = { k: q.k, a: round(a, 2), b: round(b, 2) };
    Object.keys(d).forEach((key) => {
      const v = q[key], dv = d[key];
      if (typeof dv === 'boolean') o[key] = typeof v === 'boolean' ? v : dv;
      else if (OPTS[key]) o[key] = OPTS[key].indexOf(v) >= 0 ? v : dv;
      else if (key === 'st') o[key] = Number(v) === 2 ? 2 : 1;
      else o[key] = isNum(v) ? round(clamp(Number(v), LIM[key][0], LIM[key][1]), 3) : dv;
    });
    return o;
  }
  // 새 구간 기본값 (kind · 이전에 쓴 값 prev 가 있으면 그것)
  function fresh(k, a, b, prev) {
    const base = Object.assign({}, DEF[k], prev && typeof prev === 'object' ? prev : {}, { k: k, a: a, b: b });
    return clean(base, Infinity);
  }

  // ---------------------------------------------------------------- 관 줄 (평면 · 측점)
  function path(pts) {
    const xy = [];
    (Array.isArray(pts) ? pts : []).forEach((q) => { if (q && isNum(q[0]) && isNum(q[1])) xy.push([Number(q[0]), Number(q[1])]); });
    const cum = [0];
    for (let i = 1; i < xy.length; i++) cum.push(cum[i - 1] + Math.hypot(xy[i][0] - xy[i - 1][0], xy[i][1] - xy[i - 1][1]));
    const len = xy.length ? cum[cum.length - 1] : 0;
    const segAt = (s) => {                       // s 가 든 마디 (길이 0 마디 건너뜀)
      let lo = 0, hi = Math.max(0, xy.length - 2);
      while (lo < hi) { const m = (lo + hi + 1) >> 1; if (cum[m] <= s) lo = m; else hi = m - 1; }
      return lo;
    };
    const dirAt = (k) => {
      for (let j = k; j + 1 < xy.length; j++) { const dx = xy[j + 1][0] - xy[j][0], dy = xy[j + 1][1] - xy[j][1], l = Math.hypot(dx, dy); if (l > 1e-9) return [dx / l, dy / l]; }
      for (let j = k; j > 0; j--) { const dx = xy[j][0] - xy[j - 1][0], dy = xy[j][1] - xy[j - 1][1], l = Math.hypot(dx, dy); if (l > 1e-9) return [dx / l, dy / l]; }
      return [1, 0];
    };
    function at(s) {
      if (xy.length < 2) return xy.length ? { x: xy[0][0], y: xy[0][1], tx: 1, ty: 0 } : { x: 0, y: 0, tx: 1, ty: 0 };
      s = clamp(s, 0, len);
      const k = segAt(s), l = cum[k + 1] - cum[k], t = l > 0 ? clamp((s - cum[k]) / l, 0, 1) : 0, d = dirAt(k);
      return { x: xy[k][0] + (xy[k + 1][0] - xy[k][0]) * t, y: xy[k][1] + (xy[k + 1][1] - xy[k][1]) * t, tx: d[0], ty: d[1] };
    }
    // s0 → s1 사이 관 줄 꺾인 점 (양끝 포함 · 측점 순)
    function between(s0, s1) {
      const lo = Math.min(s0, s1), hi = Math.max(s0, s1), out = [[lo, at(lo)]];
      for (let i = 1; i + 1 < xy.length; i++) if (cum[i] > lo + 1e-6 && cum[i] < hi - 1e-6) out.push([cum[i], { x: xy[i][0], y: xy[i][1] }]);
      out.push([hi, at(hi)]);
      return s0 <= s1 ? out : out.reverse();
    }
    return { xy: xy, cum: cum, len: len, at: at, between: between };
  }

  // ---------------------------------------------------------------- 계획 (깊이 · 수직구 · 둑)
  // 관 깊이 = 구간 안 가장 낮은 땅 (하상) − 토피 = 관 윗면 · 수평. 추진: 수직구 가운데 = 찍은 점 · 바닥 = 관 바닥 − 기초 여유.
  // 횡단: 반폭씩 1단계 (시점 둑 → 가운데) · 2단계 (가운데 → 종점 둑) · 둑 = U 자 (관 양쪽 w 떨어진 두 다리 + 가운데 가로) · 천단 = 하상 + h 수평.
  function plan(q0, P, ground, od) {
    if (!P || !(P.len > 0)) return null;
    const q = clean(q0, P.len);
    if (!q) return null;
    od = od > 0 ? Number(od) : 0.3;
    const G = (x, y) => { const g = ground(x, y); return Number.isFinite(g) ? g : NaN; };
    const out = { q: q, k: q.k, a: q.a, b: q.b, ta: q.a, tb: q.b, od: od, ok: true, msg: '' };   // ta · tb = 터파기 빠지는 구간 (추진 = 두 수직구 바깥 벽 사이)
    const n = Math.max(8, Math.ceil((q.b - q.a) / STEP));
    let gMin = Infinity, sMin = q.a;
    for (let i = 0; i <= n; i++) {
      const s = q.a + (q.b - q.a) * i / n, p = P.at(s), g = G(p.x, p.y);
      if (Number.isFinite(g) && g < gMin) { gMin = g; sMin = s; }
    }
    if (!Number.isFinite(gMin)) { out.ok = false; out.msg = '땅 높이 없음'; return out; }
    // 추진 = 토피를 케이싱 윗면에서 잼 (하천 아래 토피는 바깥 구조물 기준 · 1126 검토) · 개착 = 관 윗면에서
    const cas = q.k === 'jack' ? (q.cas > 0 ? q.cas : round(od + 0.2, 3)) : od;
    const zc = gMin - q.cover - Math.max(cas, od) / 2, top = zc + od / 2, bot = zc - od / 2;
    Object.assign(out, { gMin: gMin, sMin: sMin, top: top, zc: zc, bot: bot, casTop: zc + cas / 2 });
    let ext = 3;
    const bends = (pts) => {                       // 2° 넘게 꺾인 꼭짓점이 있는지 (곧게 이어진 꼭짓점은 괜찮음)
      for (let i = 1; i + 1 < pts.length; i++) {
        const a = pts[i - 1][1], b = pts[i][1], c = pts[i + 1][1];
        const ux = b.x - a.x, uy = b.y - a.y, vx = c.x - b.x, vy = c.y - b.y, lu = Math.hypot(ux, uy), lv = Math.hypot(vx, vy);
        if (lu > 1e-6 && lv > 1e-6 && (ux * vx + uy * vy) / (lu * lv) < Math.cos(2 * Math.PI / 180)) return true;
      }
      return false;
    };
    if (q.k === 'jack') {
      const sL = q.rev ? q.b : q.a, sA = q.rev ? q.a : q.b, dir = sA > sL ? 1 : -1, zb = zc - Math.max(cas, od) / 2 - q.base;
      let noGround = false;
      const mk = (s, launch) => {
        // 방향은 구간 안쪽에서 잼 (끝 수직구가 꺾인 점에 서면 다음 관 방향으로 돌던 것 · 1126 검토)
        const p = P.at(s), pd = P.at(s >= q.b ? s - 0.01 : s + 0.01), rect = q.shape === 'rect';
        const w = rect ? (launch ? q.lw : q.aw) : (launch ? q.ld : q.ad), l = rect ? (launch ? q.ll : q.al) : w;
        let g = G(p.x, p.y);
        if (!Number.isFinite(g)) { g = gMin; noGround = true; }
        // 사각 흙막이: 토류벽 = H-파일 (1.8 m 간격) + 토류판 · 시트파일 = 흙막이 줄 둘레 ÷ 한 장 유효폭 올림 (JACK 1127 «시트파일도 선택»)
        const wall = rect ? q.wall : '', sheet = wall === 'sheet';
        return { s: s, x: p.x, y: p.y, tx: pd.tx * dir, ty: pd.ty * dir, g: g, launch: launch, shape: q.shape, wall: wall, w: w, l: l, zb: zb, depth: g - zb,
          area: rect ? w * l : Math.PI * w * w / 4, per: rect ? 2 * (w + l) : Math.PI * w, hp: rect && !sheet ? 2 * ceilN(l / HP_PITCH) + 2 * ceilN(w / HP_PITCH) : 0,
          sp: sheet ? ceilN((2 * (w + l) + 8 * WALL_C) / SP_W) : 0 };
      };
      const L = mk(sL, true), A = mk(sA, false);
      const c0 = sL + dir * L.l / 2, c1 = sA - dir * A.l / 2;
      Object.assign(out, { shafts: [L, A], zb: zb, dir: dir, c0: c0, c1: c1, jackLen: Math.max(0, (c1 - c0) * dir), cas: cas });
      if (!(out.jackLen > 0)) { out.ok = false; out.msg = '구간이 수직구보다 짧음'; }
      else if (noGround) { out.ok = false; out.msg = '수직구 자리 땅 높이 없음'; }
      else if (bends(P.between(c0, c1))) { out.ok = false; out.msg = '추진 구간 안에서 관이 꺾임 · 곧은 구간으로'; }
      ext = Math.max(L.l, A.l) / 2 + 2;
      const shA = q.rev ? A : L, shB = q.rev ? L : A;                    // 측점 a 쪽 · b 쪽 수직구 (관 끝에서 멈춤)
      out.ta = Math.max(0, q.a - shaftOut(shA)); out.tb = Math.min(P.len, q.b + shaftOut(shB));
      // 수직구 바닥 자리 = 터파기 영역 (임시점용 · 포장 깨기 · AI 부대공 영역 · JACK 1127 «추진도 터파기 영역이 잡혀야») · 바깥 벽 테두리 (반시계)
      out.foot = [L, A].map((sh) => {
        const poly = [];
        if (sh.shape === 'rect') {
          const hl = sh.l / 2 + SHAFT_OUT_RECT, hw = sh.w / 2 + SHAFT_OUT_RECT, nx = -sh.ty, ny = sh.tx;
          for (const [u, v] of [[-hl, -hw], [hl, -hw], [hl, hw], [-hl, hw]]) poly.push([sh.x + sh.tx * u + nx * v, sh.y + sh.ty * u + ny * v]);
          return { poly: poly, area: 4 * hl * hw };
        }
        const r = sh.w / 2 + SHAFT_OUT_CIRC, N = 24;
        for (let i = 0; i < N; i++) { const t = i / N * 2 * Math.PI; poly.push([sh.x + r * Math.cos(t), sh.y + r * Math.sin(t)]); }
        return { poly: poly, area: N / 2 * r * r * Math.sin(2 * Math.PI / N) };
      });
    } else {
      const sm = (q.a + q.b) / 2, damTop = gMin + q.h;
      const leg = (pts) => {                       // [[x, y]…] → 점마다 땅 · 길이
        const o = { pts: [], len: 0 };
        pts.forEach((p, i) => {
          if (i) o.len += Math.hypot(p[0] - pts[i - 1][0], p[1] - pts[i - 1][1]);
          o.pts.push([p[0], p[1], G(p[0], p[1]), o.len]);
        });
        o.pts.forEach((p) => { if (!Number.isFinite(p[2])) p[2] = gMin; });
        return o;
      };
      const side = (s0, s1, w) => {                 // 관에서 w (왼쪽 +) 떨어진 다리 · 0.5 m 마다
        const m = Math.max(1, Math.ceil(Math.abs(s1 - s0) / STEP)), pts = [];
        for (let i = 0; i <= m; i++) { const p = P.at(s0 + (s1 - s0) * i / m); pts.push([p.x - p.ty * w, p.y + p.tx * w]); }
        return leg(pts);
      };
      const cross = (s, w) => {
        const p = P.at(s), m = Math.max(1, Math.ceil(2 * w / STEP)), pts = [];
        for (let i = 0; i <= m; i++) { const u = w - 2 * w * i / m; pts.push([p.x - p.ty * u, p.y + p.tx * u]); }
        return leg(pts);
      };
      const stages = [1, 2].map((st) => {
        const s0 = st === 1 ? q.a : q.b, legs = [side(s0, sm, q.w), cross(sm, q.w), side(sm, s0, -q.w)];
        let len = 0, bags = 0, sheet = 0;
        for (const L of legs) {
          const nc = L.len > 1e-6 ? ceilN(L.len / BAG_W) : 0;
          L.cols = [];
          for (let i = 0; i < nc; i++) {
            const u = (i + 0.5) * L.len / nc, p = legAt(L, u), h = damTop - p.g, k = h > 0.05 ? ceilN(h / BAG_H) : 0;
            L.cols.push({ u: u, x: p.x, y: p.y, dx: p.dx, dy: p.dy, g: p.g, k: k });
            bags += k * (k + 1) / 2;
          }
          for (let i = 1; i < L.pts.length; i++) {
            const a = L.pts[i - 1], b = L.pts[i], dl = b[3] - a[3], ha = Math.max(0, damTop - a[2]), hb = Math.max(0, damTop - b[2]);
            if (ha > 0 || hb > 0) { sheet += dl * ((ha + hb) / 2 + q.emb); len += dl; }   // 마른 둔치는 길이에서 뺌 (1126 검토)
          }
        }
        return { st: st, s0: s0, sm: sm, legs: legs, len: len, bags: bags, sheet: sheet };
      });
      Object.assign(out, { sm: sm, damTop: damTop, stages: stages });
    }
    // 종단 (땅) = 구간 양옆으로 조금 더 · 0.5 m 마다
    const p0 = Math.max(0, q.a - ext), p1 = Math.min(P.len, q.b + ext), m = Math.max(8, Math.ceil((p1 - p0) / STEP)), prof = [];
    for (let i = 0; i <= m; i++) { const s = p0 + (p1 - p0) * i / m, p = P.at(s), g = G(p.x, p.y); if (Number.isFinite(g)) prof.push([s, g]); }
    out.prof = prof;
    return out;
  }
  // 다리 위 u (m) 자리 [x, y, 땅] · 방향
  function legAt(L, u) {
    const P = L.pts;
    let i = 1;
    while (i + 1 < P.length && P[i][3] < u) i++;
    const a = P[i - 1], b = P[i], l = b[3] - a[3], t = l > 0 ? clamp((u - a[3]) / l, 0, 1) : 0;
    const dx = b[0] - a[0], dy = b[1] - a[1], dl = Math.hypot(dx, dy) || 1;
    return { x: a[0] + dx * t, y: a[1] + dy * t, g: a[2] + (b[2] - a[2]) * t, dx: dx / dl, dy: dy / dl };
  }

  // ---------------------------------------------------------------- 수량
  const QK = ['jack', 'shaft', 'exc', 'hp', 'hpm', 'lag', 'ssp', 'sspm', 'liner', 'linerA', 'open', 'dam', 'bag', 'sheet'];
  // 수량 표 줄 (수량 카테고리 «하천» 갈래 · [열쇠, 이름, 단위]) · 수직구 시트파일 길이 합 (sspm) 은 단면 창 표 · 표 아래 글에
  const QROWS = [['jack', '추진 길이', 'm'], ['shaft', '수직구', '개소'], ['exc', '수직구 굴착', 'm³'], ['hp', 'H-파일', '본'], ['lag', '토류판', 'm²'],
    ['ssp', '시트파일 (수직구)', '장'], ['liner', '라이너 플레이트', 'm'], ['open', '개착 횡단', 'm'], ['dam', '가물막이', 'm'], ['bag', '톤마대', '개'], ['sheet', '시트파일', 'm²']];
  function qty(pl) {
    const z = {};
    QK.forEach((k) => { z[k] = 0; });
    if (!pl || !pl.ok || !Number.isFinite(pl.gMin)) return z;      // 못 셈 구간은 합계에 넣지 않음 (1126 검토)
    if (pl.k === 'jack') {
      z.jack = pl.jackLen;
      for (const sh of pl.shafts) {
        z.shaft += 1;
        z.exc += sh.area * sh.depth;
        if (sh.shape !== 'rect') { z.liner += sh.depth; z.linerA += sh.per * sh.depth; }
        else if (sh.wall === 'sheet') { z.ssp += sh.sp; z.sspm += sh.sp * (sh.depth + SP_EMB); }      // 한 장 길이 = 굴착 깊이 + 근입
        else { z.hp += sh.hp; z.hpm += sh.hp * (sh.depth + HP_EMB); z.lag += sh.per * sh.depth; }
      }
    } else {
      z.open = pl.b - pl.a;
      for (const st of pl.stages) { z.dam += st.len; if (pl.q.dam === 'bag') z.bag += st.bags; else z.sheet += st.sheet; }
    }
    return z;
  }
  // 이 구간 표 (단면 창 · [이름, 값])
  function rows(pl) {
    if (!pl) return [];
    if (!Number.isFinite(pl.gMin)) return [['상태', pl.msg || '못 셈']];
    const q = pl.q, Z = qty(pl), r = [['하상 (가장 낮은 땅)', 'EL ' + f2(pl.gMin)], ['관 윗면', 'EL ' + f2(pl.top) + ' · 토피 ' + f2(q.cover)]];
    if (pl.k === 'jack') {
      const [L, A] = pl.shafts, nm = (sh) => (sh.shape === 'rect' ? f2(sh.w) + ' × ' + f2(sh.l) : 'Ø' + f2(sh.w));
      r.push(['수직구 바닥', 'EL ' + f2(pl.zb) + ' · 여유 ' + f2(q.base)]);
      r.push(['발진구 ' + nm(L), '깊이 ' + f2(L.depth) + ' m'], ['도달구 ' + nm(A), '깊이 ' + f2(A.depth) + ' m']);
      r.push(['추진 길이 (케이싱 Ø' + f2(pl.cas) + ')', f2(Z.jack) + ' m'], ['수직구 굴착', f2(Z.exc) + ' m³']);
      if (q.shape === 'rect' && q.wall === 'sheet') r.push(['시트파일 (근입 ' + SP_EMB.toFixed(1) + ' m)', Z.ssp + ' 장 · ' + f2(Z.sspm) + ' m']);
      else if (q.shape === 'rect') r.push(['H-파일 (근입 ' + HP_EMB.toFixed(1) + ' m)', Z.hp + ' 본 · ' + f2(Z.hpm) + ' m'], ['토류판', f2(Z.lag) + ' m²']);
      else r.push(['라이너 플레이트', f2(Z.liner) + ' m · ' + f2(Z.linerA) + ' m²']);
      if (!pl.ok) r.push(['확인', pl.msg]);
    } else {
      r.push(['개착 횡단', f2(Z.open) + ' m'], ['가물막이 천단', 'EL ' + f2(pl.damTop) + ' · 하상 + ' + f2(q.h)]);
      pl.stages.forEach((st) => r.push([st.st + '단계 가물막이', f2(st.len) + ' m' + (q.dam === 'bag' ? ' · 톤마대 ' + st.bags + ' 개' : ' · 시트파일 ' + f2(st.sheet) + ' m²')]));
    }
    return r;
  }

  // ---------------------------------------------------------------- 종단 그림 (SVG 글 · 터파기 단면 창 그림과 같은 꼴: 어두운 바탕 · 흰 글)
  function svg(pl, o) {
    o = o || {};
    const W = o.W || 380, col = o.col || '#00e5ff';
    const n1 = (v) => (Number.isFinite(v) ? v.toFixed(1) : '0');
    const txt = (x, y, s, an, c) => '<text x="' + n1(x) + '" y="' + n1(y) + '" text-anchor="' + (an || 'middle') + '" fill="' + (c || '#e9e9e9') +
      '" stroke="#141414" stroke-width="3" paint-order="stroke" font-size="11" font-weight="600">' + s + '</text>';
    if (!pl || !pl.prof || pl.prof.length < 2 || !Number.isFinite(pl.gMin)) {
      return { w: W, h: 120, body: txt(W / 2, 64, (pl && pl.msg) || '땅 높이 없음') };
    }
    const q = pl.q, prof = pl.prof, s0 = prof[0][0], s1 = prof[prof.length - 1][0];
    let zhi = -Infinity, zlo = Infinity;
    prof.forEach((p) => { zhi = Math.max(zhi, p[1]); zlo = Math.min(zlo, p[1]); });
    if (pl.k === 'jack') {
      zlo = Math.min(zlo, pl.zb - 0.6, pl.shafts.some((sh) => sh.wall === 'sheet') ? pl.zb - SP_EMB - 0.3 : Infinity);   // 시트파일 = 근입 끝까지 보이게
      pl.shafts.forEach((sh) => { zhi = Math.max(zhi, sh.g + 0.3); });
    } else {
      zlo = Math.min(zlo, pl.bot - 0.6, q.dam === 'sheet' ? pl.gMin - q.emb - 0.3 : Infinity);
      zhi = Math.max(zhi, pl.damTop);
    }
    zhi += 0.9; zlo -= 0.4;
    const H = 236, pl0 = 44, pr = 14, pt = 30, pb = 36;
    const sx = (W - pl0 - pr) / Math.max(s1 - s0, 1), sz = (H - pt - pb) / Math.max(zhi - zlo, 1);
    const X = (s) => pl0 + (s - s0) * sx, Y = (z) => pt + (zhi - z) * sz;
    const ln = (x1, y1, x2, y2, st, w, da) => '<line x1="' + n1(x1) + '" y1="' + n1(y1) + '" x2="' + n1(x2) + '" y2="' + n1(y2) + '" stroke="' + (st || '#8b8b8b') +
      '" stroke-width="' + (w || 1) + '"' + (da ? ' stroke-dasharray="' + da + '"' : '') + '/>';
    const rect = (x0, x1, y0, y1, f, st, cls) => '<rect' + (cls ? ' class="' + cls + '"' : '') + ' x="' + n1(Math.min(x0, x1)) + '" y="' + n1(Math.min(y0, y1)) + '" width="' + n1(Math.abs(x1 - x0)) +
      '" height="' + n1(Math.abs(y1 - y0)) + '" fill="' + f + '"' + (st ? ' stroke="' + st + '" stroke-width="1"' : ' stroke="none"') + '/>';
    const dimV = (x, z0, z1, s, an, dx) => ln(x, Y(z0), x, Y(z1)) + ln(x - 4, Y(z0), x + 4, Y(z0)) + ln(x - 4, Y(z1), x + 4, Y(z1)) + txt(x + dx, (Y(z0) + Y(z1)) / 2 + 4, s, an);
    const dimH = (xa, xb, y, s) => ln(xa, y, xb, y) + ln(xa, y - 4, xa, y + 4) + ln(xb, y - 4, xb, y + 4) + txt((xa + xb) / 2, y - 5, s);
    let s = '';
    const gl = prof.map((p) => n1(X(p[0])) + ',' + n1(Y(p[1]))).join(' ');
    s += '<polygon class="gd" points="' + n1(X(s0)) + ',' + n1(H) + ' ' + gl + ' ' + n1(X(s1)) + ',' + n1(H) + '" fill="#5a4330" stroke="none"/>';
    if (pl.k === 'jack') {
      for (const sh of pl.shafts) {                                      // 수직구 = 빈 구덩이 (바탕색) + 흙막이 선
        const xa = X(sh.s - sh.l / 2), xb = X(sh.s + sh.l / 2);
        s += rect(xa, xb, Y(sh.g + 0.3), Y(sh.zb), '#141414', null, 'shaft');
        // 흙막이 선: 토류벽 = H-파일 색 · 근입 HP_EMB / 시트파일 = 가물막이 시트파일과 같은 색 · 굵기 · 근입 SP_EMB / 라이너 = 바닥까지
        const sp = sh.wall === 'sheet', wc = sp ? '#9aa6b4' : sh.shape === 'rect' ? '#b07a45' : '#9aa6b4', zt = sp ? sh.zb - SP_EMB : sh.shape === 'rect' ? sh.zb - HP_EMB : sh.zb;
        s += ln(xa, Y(sh.g + 0.3), xa, Y(zt), wc, sp ? 3 : 2.4) + ln(xb, Y(sh.g + 0.3), xb, Y(zt), wc, sp ? 3 : 2.4);
        if (sp && sh.launch) s += txt(xa > W / 2 ? xb - 4 : xa + 4, Y(sh.zb - SP_EMB / 2) + 4, '시트파일 근입 ' + SP_EMB.toFixed(1), xa > W / 2 ? 'end' : 'start', '#c9d0dc');
        s += rect(xa, xb, Y(sh.zb), Y(sh.zb - 0.2), '#9a9a92');
        const xm = (xa + xb) / 2, an = xm < 70 ? 'start' : xm > W - 70 ? 'end' : 'middle', xt = an === 'start' ? Math.max(4, xa) : an === 'end' ? Math.min(W - 4, xb) : xm;
        s += txt(xt, Y(sh.g + 0.3) - 6, (sh.launch ? '발진' : '도달') + ' 깊이 ' + f2(sh.depth), an);
      }
    }
    s += '<path d="M' + gl.split(' ').join(' L') + '" fill="none" stroke="#c9d0dc" stroke-width="2"/>';
    if (pl.k === 'jack') {
      const ca = Math.min(pl.c0, pl.c1), cb = Math.max(pl.c0, pl.c1);
      if (cb > ca) s += rect(X(ca), X(cb), Y(pl.zc + pl.cas / 2), Y(pl.zc - pl.cas / 2), '#7f8c99', '#c9d0dc', 'cas');
      const [L, A] = pl.shafts;
      s += rect(X(Math.min(L.s, A.s)), X(Math.max(L.s, A.s)), Y(pl.top), Y(pl.bot), col, null, 'pipe');
      const yd = Math.min(Y(pl.zc + pl.cas / 2) - 8, Y(pl.top) - 8);
      if (cb > ca) s += dimH(X(ca), X(cb), Math.max(pt, Math.min(yd, Y(pl.gMin) + 14)), '추진 ' + f2(pl.jackLen) + ' m');
    } else {
      s += rect(X(pl.a), X(pl.b), Y(pl.top), Y(pl.bot), col, null, 'pipe');
      pl.stages.forEach((st) => {
        const cur = st.st === q.st, xa = X(Math.min(st.s0, st.sm)), xb = X(Math.max(st.s0, st.sm));
        s += ln(xa, Y(pl.damTop), xb, Y(pl.damTop), cur ? '#60a5fa' : '#4b5563', cur ? 2 : 1, '5 3');
        s += txt((xa + xb) / 2, Y(pl.damTop) - 6, st.st + '단계', 'middle', cur ? '#e9e9e9' : '#8b8b8b');
      });
      const cur = pl.stages[q.st - 1], c = cur.legs[1].cols, gm = c.length ? c[Math.floor(c.length / 2)].g : pl.gMin, k = Math.max(1, Math.ceil((pl.damTop - gm) / BAG_H - 1e-9));
      if (q.dam === 'bag') {
        const hw = k * BAG_W / 2;
        for (let j = 0; j < k; j++) {
          const m = k - j, z0 = gm + j * BAG_H, z1 = Math.min(z0 + BAG_H, pl.damTop + 0.2);
          for (let i = 0; i < m; i++) { const u = -hw + (j * BAG_W) / 2 + i * BAG_W; s += rect(X(pl.sm + u + 0.04), X(pl.sm + u + BAG_W - 0.04), Y(z1 - 0.03), Y(z0), '#d9d6cc', '#141414', 'bag'); }
        }
      } else s += ln(X(pl.sm), Y(pl.damTop), X(pl.sm), Y(gm - q.emb), '#9aa6b4', 3);
      s += dimV(X(pl.sm) + 16, gm, pl.damTop, '높이 ' + f2(pl.damTop - gm), 'start', 6);
    }
    const cx = X(pl.sMin) + (pl.k === 'open' && Math.abs(pl.sMin - pl.sm) < 3 ? -16 : 0), right = pl.sMin < (pl.a + pl.b) / 2;   // 토피 글 = 구간 가운데 쪽
    s += dimV(cx, pl.gMin, pl.top, '토피 ' + f2(q.cover), right ? 'start' : 'end', right ? 6 : -6);
    const vx = sz / sx;
    s += txt(6, 16, NAME[pl.k] + ' · ' + f2(pl.b - pl.a) + ' m' + (vx > 1.5 ? ' · 세로 ×' + Math.round(vx) : ''), 'start');
    s += txt(6, H - 10, '하상 EL ' + f2(pl.gMin) + ' · 관 윗면 EL ' + f2(pl.top) + (pl.k === 'jack' ? ' · 바닥 EL ' + f2(pl.zb) : ' · 천단 EL ' + f2(pl.damTop)), 'start', '#c9d0dc');
    return { w: W, h: H, body: s };
  }

  // ---------------------------------------------------------------- 3D 면 (face(점들, 바깥 쪽, 재질 이름))
  const BOX_F = [[0, 1, 2, 3], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]];
  const SP_PROF = [[0, -0.075], [0.25, -0.075], [0.4, 0.075], [0.65, 0.075], [0.8, -0.075]], SP_T = 0.006;   // 시트파일 골 (한 주기 [u, 직각 거리] · 강판 반 두께)
  function build3d(pl, face) {
    if (!pl || !Number.isFinite(pl.gMin)) return 0;
    let nf = 0;
    const F = (ps, hint, kind) => { face(ps, hint, kind); nf++; };
    const box = (cs, kind) => {                                          // 8 모서리 (아래 4 · 위 4)
      const c = [0, 1, 2].map((i) => cs.reduce((a, p) => a + p[i], 0) / 8);
      for (const f of BOX_F) {
        const ps = f.map((i) => cs[i]), fc = [0, 1, 2].map((i) => ps.reduce((a, p) => a + p[i], 0) / 4 - c[i]);
        F(ps, fc, kind);
      }
    };
    // 돌린 상자: 가운데 (x, y) · 축 u = (ux, uy) · 반 길이 hu (u 쪽) · hv (u 에 직각) · z0 ~ z1
    const obox = (x, y, ux, uy, hu, hv, z0, z1, kind) => {
      const vx = -uy, vy = ux, c = (a, b, z) => [x + ux * a + vx * b, y + uy * a + vy * b, z];
      box([c(-hu, -hv, z0), c(hu, -hv, z0), c(hu, hv, z0), c(-hu, hv, z0), c(-hu, -hv, z1), c(hu, -hv, z1), c(hu, hv, z1), c(-hu, hv, z1)], kind);
    };
    // 원통 벽 (안 · 밖 면 + 위 · 아래 고리)
    const cyl = (x, y, ri, ro, z0, z1, kind, N) => {
      N = N || 36;
      const P = (r, i, z) => { const a = i / N * 2 * Math.PI; return [x + r * Math.cos(a), y + r * Math.sin(a), z]; };
      for (let i = 0; i < N; i++) {
        const a = (i + 0.5) / N * 2 * Math.PI, d = [Math.cos(a), Math.sin(a), 0];
        F([P(ro, i, z0), P(ro, i + 1, z0), P(ro, i + 1, z1), P(ro, i, z1)], d, kind);
        if (ri > 0) F([P(ri, i, z0), P(ri, i + 1, z0), P(ri, i + 1, z1), P(ri, i, z1)], [-d[0], -d[1], 0], kind);
        if (ri > 0) {
          F([P(ri, i, z1), P(ri, i + 1, z1), P(ro, i + 1, z1), P(ro, i, z1)], [0, 0, 1], kind);
          F([P(ri, i, z0), P(ri, i + 1, z0), P(ro, i + 1, z0), P(ro, i, z0)], [0, 0, -1], kind);
        } else {
          F([[x, y, z1], P(ro, i, z1), P(ro, i + 1, z1)], [0, 0, 1], kind);
        }
      }
    };
    // 곧은 시트파일 벽 (골진 강판): 시작 (ax, ay) · 방향 (ux, uy) · 길이 len · z0 ~ z1 · 골 = 방향에 직각으로 ± (가물막이와 같은 골 SP_PROF)
    const spRun = (ax, ay, ux, uy, len, z0, z1) => {
      const vx = -uy, vy = ux, nP = Math.ceil(len / SP_PITCH - 1e-9);
      const Q = (u, d, z) => [ax + ux * u + vx * d, ay + uy * u + vy * d, z];
      for (let k = 0; k < nP; k++) {
        for (let i = 0; i + 1 < SP_PROF.length; i++) {
          const u0 = k * SP_PITCH + SP_PROF[i][0], u1 = Math.min(k * SP_PITCH + SP_PROF[i + 1][0], len);
          if (u1 - u0 < 1e-4) continue;
          const da = SP_PROF[i][1], db = SP_PROF[i + 1][1];
          box([Q(u0, da - SP_T, z0), Q(u1, db - SP_T, z0), Q(u1, db + SP_T, z0), Q(u0, da + SP_T, z0),
            Q(u0, da - SP_T, z1), Q(u1, db - SP_T, z1), Q(u1, db + SP_T, z1), Q(u0, da + SP_T, z1)], 'sheet');
        }
      }
    };
    if (pl.k === 'jack') {
      for (const sh of pl.shafts) {
        const zt = sh.g + 0.3, zb = sh.zb;
        if (sh.shape === 'rect') {
          const tx = sh.tx, ty = sh.ty, nx = -ty, ny = tx, hl = sh.l / 2, hw = sh.w / 2, T = 0.08;
          const at = (u, v) => [sh.x + tx * u + nx * v, sh.y + ty * u + ny * v];
          if (sh.wall === 'sheet') {
            // 시트파일 (흙막이 줄 = H-파일 줄과 같은 자리 · 네 변 · 바닥 아래 근입 SP_EMB · 가물막이 시트파일과 같은 골 · 재질)
            const HL = hl + WALL_C, HW = hw + WALL_C;
            for (const sg of [1, -1]) {
              let p = at(-HL, sg * HW); spRun(p[0], p[1], tx, ty, 2 * HL, zb - SP_EMB, zt);
              p = at(sg * HL, -HW); spRun(p[0], p[1], nx, ny, 2 * HW, zb - SP_EMB, zt);
            }
          } else {
            // 토류판 (안쪽 면 = 내공)
            for (const sg of [1, -1]) {
              let p = at(0, sg * (hw + T / 2)); obox(p[0], p[1], tx, ty, hl + T, T / 2, zb, zt, 'wood');
              p = at(sg * (hl + T / 2), 0); obox(p[0], p[1], nx, ny, hw, T / 2, zb, zt, 'wood');
            }
            // H-파일 (둘레 1.8 m 안 간격 · 구석 포함 · 바닥 아래 근입)
            const hp = (u, v, ax, ay) => {
              const p = at(u, v), bx = -ay, by = ax;
              for (const sg of [1, -1]) obox(p[0] + bx * sg * 0.14, p[1] + by * sg * 0.14, ax, ay, 0.15, 0.01, zb - HP_EMB, sh.g + 0.6, 'hpile');
              obox(p[0], p[1], bx, by, 0.13, 0.008, zb - HP_EMB, sh.g + 0.6, 'hpile');
            };
            const mL = ceilN(sh.l / HP_PITCH), mW = ceilN(sh.w / HP_PITCH), off = T + 0.15;
            for (const sg of [1, -1]) {
              for (let i = 0; i <= mL; i++) hp(-hl + sh.l * i / mL, sg * (hw + off), tx, ty);
              for (let j = 1; j < mW; j++) hp(sg * (hl + off), -hw + sh.w * j / mW, nx, ny);
            }
          }
          // 띠장 (2 m 마다 · 안쪽)
          for (let z = sh.g - 0.5; z > zb + 0.6; z -= 2.0) {
            for (const sg of [1, -1]) {
              let p = at(0, sg * (hw - 0.15)); obox(p[0], p[1], tx, ty, hl, 0.15, z - 0.15, z + 0.15, 'steel');
              p = at(sg * (hl - 0.15), 0); obox(p[0], p[1], nx, ny, hw - 0.3, 0.15, z - 0.15, z + 0.15, 'steel');
            }
          }
          obox(sh.x, sh.y, tx, ty, hl, hw, zb - 0.2, zb, 'conc');            // 바닥 콘크리트
        } else {
          const r = sh.w / 2;
          cyl(sh.x, sh.y, r, r + 0.05, zb, zt, 'liner');
          for (let z = zb + 1.0; z < zt - 0.2; z += 1.0) cyl(sh.x, sh.y, r - 0.07, r, z - 0.04, z + 0.04, 'liner');   // 보강 링
          cyl(sh.x, sh.y, 0, r, zb - 0.2, zb, 'conc');
        }
      }
      // 케이싱 (강관 · 발진 안쪽 벽 → 도달 안쪽 벽 · 관이 안에 보이게 끝은 고리)
      const P = pl.path;
      if (P && pl.jackLen > 0) {
        const R = pl.cas / 2, ri = Math.min(R - 0.01, pl.od / 2), N = 20, pts = P.between(pl.c0, pl.c1);
        for (let j = 1; j < pts.length; j++) {
          const a = pts[j - 1][1], b = pts[j][1], dx = b.x - a.x, dy = b.y - a.y, l = Math.hypot(dx, dy);
          if (l < 1e-6) continue;
          const tx = dx / l, ty = dy / l, nx = -ty, ny = tx;
          const ring = (p, i) => { const t = i / N * 2 * Math.PI, c = Math.cos(t), sn = Math.sin(t); return [p.x + nx * c * R, p.y + ny * c * R, pl.zc + sn * R]; };
          for (let i = 0; i < N; i++) {
            const t = (i + 0.5) / N * 2 * Math.PI, d = [nx * Math.cos(t), ny * Math.cos(t), Math.sin(t)];
            F([ring(a, i), ring(b, i), ring(b, i + 1), ring(a, i + 1)], d, 'case');
          }
          const capAt = (p, sg) => {
            for (let i = 0; i < N; i++) {
              const t0 = i / N * 2 * Math.PI, t1 = (i + 1) / N * 2 * Math.PI;
              const q = (r, t) => [p.x + nx * Math.cos(t) * r, p.y + ny * Math.cos(t) * r, pl.zc + Math.sin(t) * r];
              F([q(ri, t0), q(R, t0), q(R, t1), q(ri, t1)], [tx * sg, ty * sg, 0], 'case');
            }
          };
          if (j === 1) capAt(a, -1);
          if (j === pts.length - 1) capAt(b, 1);
        }
      }
    } else {
      const q = pl.q, st = pl.stages[q.st - 1], top = pl.damTop;
      for (const L of st.legs) {
        if (q.dam === 'bag') {
          for (const c of L.cols) {
            const ex = -c.dy, ey = c.dx;
            for (let j = 0; j < c.k; j++) {
              const m = c.k - j, z0 = c.g + j * BAG_H, z1 = z0 + BAG_H - 0.05;
              for (let i = 0; i < m; i++) {
                const u = (i - (m - 1) / 2) * BAG_W;
                obox(c.x + ex * u, c.y + ey * u, c.dx, c.dy, BAG_W / 2 - 0.05, BAG_W / 2 - 0.05, z0, z1, 'bag');
              }
            }
          }
        } else {
          const prof = SP_PROF, T2 = SP_T;
          const nP = Math.ceil(L.len / SP_PITCH - 1e-9);
          for (let k = 0; k < nP; k++) {
            for (let i = 0; i + 1 < prof.length; i++) {
              const u0 = k * SP_PITCH + prof[i][0], u1 = Math.min(k * SP_PITCH + prof[i + 1][0], L.len);
              if (u1 - u0 < 1e-4) continue;
              const pa = legAt(L, u0), pb = legAt(L, u1);
              if (top - pa.g <= 0 && top - pb.g <= 0) continue;
              const da = prof[i][1], db = prof[i + 1][1];
              const Q = (p, d, z) => [p.x - p.dy * d, p.y + p.dx * d, z];
              box([Q(pa, da - T2, pa.g - q.emb), Q(pb, db - T2, pb.g - q.emb), Q(pb, db + T2, pb.g - q.emb), Q(pa, da + T2, pa.g - q.emb),
                Q(pa, da - T2, top), Q(pb, db - T2, top), Q(pb, db + T2, top), Q(pa, da + T2, top)], 'sheet');
            }
          }
        }
      }
    }
    return nf;
  }

  // 계획에 관 줄을 붙여 둠 (3D 케이싱이 굽은 줄을 따라가게)
  function planOn(q, P, ground, od) {
    const pl = plan(q, P, ground, od);
    if (pl) pl.path = P;
    return pl;
  }

  const API = { KINDS: KINDS, NAME: NAME, DEF: DEF, LIM: LIM, QK: QK, QROWS: QROWS, HP_EMB: HP_EMB, HP_PITCH: HP_PITCH, BAG_W: BAG_W, BAG_H: BAG_H,
    SP_W: SP_W, SP_EMB: SP_EMB, WALL_C: WALL_C,
    clean: clean, fresh: fresh, path: path, plan: planOn, qty: qty, rows: rows, svg: svg, build3d: build3d };
  root.RVRiver = API;
  if (typeof module !== 'undefined' && module.exports) module.exports = API;
})(typeof window !== 'undefined' ? window : globalThis);
