// ★[v117.16 · JACK 1112 «측구가 … LandXML 기반 지표면이라 측구가 지표면이랑 겹쳐서 안 보이네» · «civil3d 지표면엔 영향이 없게»]
//   3D 보기에서만 측구 · 집수정 자리 지형을 파낸다 — Civil 지표면 · 장면 자료(head.terrain) · 보내는 값은 그대로, 그릴 세모만 새로 만든다.
//   ① ditchFrames — C# UDitch.SceneMesh 메시(구간마다 본체 + 기초 · 낙차벽 · parts 목록)에서 측점마다 바깥 · 안 벽 자리 · 윗면 · 안바닥 높이를 읽는다
//   ② holeShapes — 구멍 테두리(측구 = 바깥 벽 선 · 집수정 = 네모) + 안쪽 판정용 조각(측구 마디 네모 · 집수정 네모)
//   ③ cutTerrain — 지형 세모마다 테두리 선분으로 나눠(평면 배치 → 면 찾기 → 귀 자르기) 구멍 밖 면만 남긴다.
//      새 점은 늘 테두리 위 → 높이 = min(지형, 측구 윗면)(지형이 높으면 윗면까지 내려 틈 없음 · 낮으면 측구 바깥 벽이 메움).
//      세모 변 위 새 점은 변 양끝(자리 순) · 선분 번호로 같은 셈 → 이웃 세모와 같은 자리(T자 이음 없음 · 갈라짐 없음).
//   왜 CPU 자르기인가(스텐실 아님): 구멍 가장자리 높이를 측구 윗면에 맞출 수 있다(스텐실은 지형을 못 움직여 틈 · 겹침이 남음) ·
//   누르기(광선) · 3D 자르기 · 그림자 · 위성 · 절성토 색이 지형 재료 그대로 · 그리기 비용 0(불러올 때 한 번 셈).
import { ShapeUtils, Vector2 } from 'three';

const TOL = 2e-3;   // 메시 읽기 허용(m) — C# 꼭짓점은 float32(대좌표를 뺀 장면 좌표)

// ── ① 측구 메시 → 측점 틀 ──
// C# UDitch.Meshes · Sweep: 덩어리(본체 · 기초)마다 측점 k의 단면 m점을 차례로(꼭짓점 = k·m + i) · 단면 점은 측점 연직면 위(평면에서 한 직선).
// 틀 = { st: [{ c, L, R, iL, iR, top, floor }] (평면 [x, y] · 왼쪽 = 진행 방향 왼쪽), seg, innerV: 안 벽 줄 꼭짓점 번호 } · 못 읽으면 null
function ringFrame(pos, lo, cnt) {
  if (cnt < 6) return null;
  const X = (v) => pos[(lo + v) * 3], Y = (v) => pos[(lo + v) * 3 + 1];
  // 단면 점 m개가 한 직선(측점 연직면) · 다음 측점은 다른 자리 — 가장 작은 m
  const lineOf = (a, m) => {
    let far = -1, fd = 0;
    for (let i = 1; i < m; i++) { const d = (X(a + i) - X(a)) ** 2 + (Y(a + i) - Y(a)) ** 2; if (d > fd) { fd = d; far = i; } }
    if (fd < 4 * TOL * TOL) return null;
    const l = Math.sqrt(fd), dx = (X(a + far) - X(a)) / l, dy = (Y(a + far) - Y(a)) / l;
    for (let i = 1; i < m; i++) if (Math.abs((X(a + i) - X(a)) * dy - (Y(a + i) - Y(a)) * dx) > TOL) return null;
    return { ox: X(a), oy: Y(a), dx, dy };
  };
  let m = 0, lines = null;
  for (let k = 3; k <= Math.min(64, cnt >> 1) && !m; k++) {
    if (cnt % k) continue;
    const ls = [];
    let ok = true;
    for (let r = 0; r < cnt / k && ok; r++) {
      const l = lineOf(r * k, k);
      if (!l) { ok = false; break; }
      if (r > 0) {
        let cx = 0, cy = 0;
        for (let i = 0; i < k; i++) { cx += X(r * k + i); cy += Y(r * k + i); }
        const p = ls[r - 1];
        if (Math.abs((cx / k - p.ox) * p.dy - (cy / k - p.oy) * p.dx) < TOL) ok = false;
      }
      ls.push(l);
    }
    if (ok) { m = k; lines = ls; }
  }
  if (!m) return null;
  const R = cnt / m, rings = [];
  for (let r = 0; r < R; r++) {
    const cols = [];
    for (let i = 0; i < m; i++) {
      const v = lo + r * m + i, x = pos[v * 3], y = pos[v * 3 + 1], z = pos[v * 3 + 2];
      let c = cols.find(q => Math.abs(q.x - x) < TOL && Math.abs(q.y - y) < TOL);
      if (!c) cols.push(c = { x, y, zmax: -Infinity, zmin: Infinity, vs: [] });
      c.zmax = Math.max(c.zmax, z); c.zmin = Math.min(c.zmin, z); c.vs.push(v);
    }
    const l = lines[r];
    let s0 = Infinity, s1 = -Infinity;
    for (const c of cols) { const s = (c.x - l.ox) * l.dx + (c.y - l.oy) * l.dy; s0 = Math.min(s0, s); s1 = Math.max(s1, s); }
    const sm = (s0 + s1) / 2;
    rings.push({ cols, l, c: [l.ox + l.dx * sm, l.oy + l.dy * sm] });
  }
  const innerV = [];
  let hasInner = true;
  const st = rings.map((S, r) => {
    const a = rings[Math.max(0, r - 1)].c, b = rings[Math.min(R - 1, r + 1)].c;
    const ax = b[0] - a[0], ay = b[1] - a[1], l = S.l;
    const sg = (-ay * l.dx + ax * l.dy) >= 0 ? 1 : -1;   // 단면 직선 방향을 진행 방향 왼쪽(+)으로
    for (const c of S.cols) c.s = sg * ((c.x - S.c[0]) * l.dx + (c.y - S.c[1]) * l.dy);
    const cs = S.cols.slice().sort((p, q) => q.s - p.s);
    const oL = cs[0], oR = cs[cs.length - 1];
    let iL = null, iR = null;
    for (const c of cs) { if (c.s > TOL && (!iL || c.s < iL.s)) iL = c; if (c.s < -TOL && (!iR || c.s > iR.s)) iR = c; }
    if (!iL || !iR || iL === oL || iR === oR) { hasInner = false; iL = iR = null; }
    let top = -Infinity;
    for (const c of S.cols) top = Math.max(top, c.zmax);
    if (iL) for (const c of [iL, iR]) innerV.push(...c.vs);
    return { c: S.c, L: [oL.x, oL.y], R: [oR.x, oR.y], iL: iL && [iL.x, iL.y], iR: iR && [iR.x, iR.y], top, floor: iL ? Math.min(iL.zmin, iR.zmin) : NaN };
  });
  if (!hasInner) for (const s of st) { s.iL = s.iR = null; s.floor = NaN; }
  return { st, seg: 1, innerV: hasInner ? innerV : [] };
}

// 덩어리 목록이 없는 옛 메시(측구 하나 = 본체 + 기초 한 벌) — 이어진 덩어리 중 가장 높은 것 = 본체(기초는 본체 밑)
// 돌려줌 = { st, seg, inner: Uint8Array(안 벽 줄 꼭짓점 = 1) | null } · 못 읽으면 null
export function ditchFrame(pos, idx) {
  const V = pos ? (pos.length / 3) | 0 : 0;
  if (V < 6 || !idx || idx.length < 3) return null;
  const par = new Int32Array(V);
  for (let i = 0; i < V; i++) par[i] = i;
  const find = (a) => { while (par[a] !== a) { par[a] = par[par[a]]; a = par[a]; } return a; };
  const join = (a, b) => { a = find(a); b = find(b); if (a !== b) par[b] = a; };
  for (let i = 0; i + 2 < idx.length; i += 3) {
    if (idx[i] >= V || idx[i + 1] >= V || idx[i + 2] >= V) return null;
    join(idx[i], idx[i + 1]); join(idx[i], idx[i + 2]);
  }
  const comp = new Map();
  for (let v = 0; v < V; v++) {
    const r = find(v);
    let c = comp.get(r);
    if (!c) comp.set(r, c = { lo: v, hi: v, n: 0, top: -Infinity });
    c.hi = v; c.n++; c.top = Math.max(c.top, pos[v * 3 + 2]);
  }
  let body = null;
  for (const c of comp.values()) if (c.n >= 6 && (!body || c.top > body.top)) body = c;
  if (!body || body.hi - body.lo + 1 !== body.n) return null;
  const f = ringFrame(pos, body.lo, body.n);
  if (!f) return null;
  let inner = null;
  if (f.innerV.length) { inner = new Uint8Array(V); for (const v of f.innerV) inner[v] = 1; }
  return { st: f.st, seg: 1, inner };
}

// ★[v117.16 · C# UDitchStore.Web3DHead «parts»] 구간이 여럿이면 덩어리가 여럿 — parts = [{ part: «본체» | «기초» | «낙차벽», seg(1부터), v0, n }]
//   본체마다 틀(구간 차례) · 안쪽 재료 = 본체 안 벽 줄 + 낙차벽(물길 안 계단) · parts가 없거나 범위가 틀리면 옛 한 덩어리 읽기
// 돌려줌 = { frames: [틀 …](seg 차례), inner: Uint8Array | null } · 못 읽으면 null
export function ditchFrames(pos, idx, parts) {
  const V = pos ? (pos.length / 3) | 0 : 0;
  const ok = Array.isArray(parts) && parts.length > 0 && parts.every(p => p && Number.isInteger(p.v0) && Number.isInteger(p.n) && p.v0 >= 0 && p.n > 0 && p.v0 + p.n <= V);
  if (!ok) {
    const F = ditchFrame(pos, idx);
    return F ? { frames: [F], inner: F.inner } : null;
  }
  const frames = [], inner = new Uint8Array(V);
  let any = false;
  for (const p of parts) {
    if (p.part === '본체') {
      const f = ringFrame(pos, p.v0, p.n);
      if (!f) continue;
      f.seg = Number.isFinite(+p.seg) ? +p.seg : frames.length + 1;
      frames.push(f);
      for (const v of f.innerV) { inner[v] = 1; any = true; }
    } else if (p.part === '낙차벽') for (let v = p.v0; v < p.v0 + p.n; v++) { inner[v] = 1; any = true; }
  }
  if (!frames.length) return null;
  frames.sort((a, b) => a.seg - b.seg);
  return { frames, inner: any ? inner : null };
}

// 낙차로 맞닿은 구간(앞 구간 끝 단면 = 뒤 구간 첫 단면 — C#은 같은 측점 · 같은 옆 방향)을 한 줄로 — 구멍 테두리가 겹친 선 없이 하나가 되게 ·
//   마구리 벽은 줄의 양 끝에만(낙차 자리는 물이 넘어가는 곳)
export function chainFrames(frames) {
  const out = [];
  const same = (p, q) => Math.abs(p[0] - q[0]) < TOL && Math.abs(p[1] - q[1]) < TOL;
  for (const f of frames) {
    const ch = out[out.length - 1], last = ch && ch[ch.length - 1].st;
    if (last && same(last[last.length - 1].L, f.st[0].L) && same(last[last.length - 1].R, f.st[0].R)) ch.push(f);
    else out.push([f]);
  }
  return out;
}

// 한 줄 → 구멍용 틀 하나(맞닿은 측점은 하나 · 윗면 = 둘 중 낮은 것 — 높은 쪽 벽은 그 위로 보이고 틈은 없음)
export function mergeChain(ch) {
  const st = ch[0].st.map(s => ({ ...s }));
  for (let i = 1; i < ch.length; i++) {
    st[st.length - 1].top = Math.min(st[st.length - 1].top, ch[i].st[0].top);
    st.push(...ch[i].st.slice(1).map(s => ({ ...s })));
  }
  return { st };
}

// ── ② 구멍 모양 ──
// shape = { pts: [x0, y0, x1, y1, …], zt: [윗면 z …], segs: [[a, b] …](잘라 넣는 선분 — 테두리 + 측구 마디 사이 맞댄 선), polys: [[i …] …](안쪽 판정 조각) }
// 집수정 = { x, y, zTop, w, l, rot? }(w = x 방향 · l = y 방향 · rot = 반시계 라디안 · 없으면 0)
export function holeShapes(frames, basins = []) {
  const shapes = [];
  for (const F of frames) {
    if (!F || !F.st || F.st.length < 2) continue;
    const n = F.st.length, pts = [], zt = [];
    for (let k = 0; k < n; k++) { pts.push(F.st[k].L[0], F.st[k].L[1]); zt.push(F.st[k].top); }
    for (let k = n - 1; k >= 0; k--) { pts.push(F.st[k].R[0], F.st[k].R[1]); zt.push(F.st[k].top); }
    const segs = [], polys = [];
    for (let i = 0; i < 2 * n; i++) segs.push([i, (i + 1) % (2 * n)]);
    for (let k = 1; k + 1 < n; k++) segs.push([k, 2 * n - 1 - k]);
    for (let k = 0; k + 1 < n; k++) polys.push([k, k + 1, 2 * n - 2 - k, 2 * n - 1 - k]);
    shapes.push({ pts, zt, segs, polys });
  }
  // 집수정 구멍은 1 mm 넓게 — 측구 마구리가 집수정 바깥면에 딱 붙어(C# 측점 S0) 두 테두리가 한 선에 겹치지 않게(상자 벽이 그 1 mm를 가림)
  for (const b of basins) {
    const x = +b.x, y = +b.y, w = +b.w + 2e-3, l = +b.l + 2e-3, z = +b.zTop, r = +b.rot || 0;
    if (![x, y, w, l, z].every(Number.isFinite) || !(w > 0) || !(l > 0)) continue;
    const c = Math.cos(r), s = Math.sin(r), pts = [];
    for (const [u, v] of [[-w / 2, -l / 2], [w / 2, -l / 2], [w / 2, l / 2], [-w / 2, l / 2]]) pts.push(x + u * c - v * s, y + u * s + v * c);
    shapes.push({ pts, zt: [z, z, z, z], segs: [[0, 1], [1, 2], [2, 3], [3, 0]], polys: [[0, 1, 2, 3]] });
  }
  // 아주 작은 흔들기(±0.05 mm · 정해진 값) — 테두리 점이 지형 점 · 변 위에 딱 놓이거나 두 테두리가 한 직선에 겹치는 드문 경우를 피한다
  //   (모든 세모가 같은 흔든 값을 쓰므로 이음은 그대로 · 측구 벽과는 0.05 mm 차이라 안 보임)
  shapes.forEach((sh, si) => {
    for (let i = 0; i < sh.pts.length; i++) {
      const h = (Math.imul(si + 1, 73856093) ^ Math.imul(i + 1, 19349663) ^ Math.imul(i + 7, 83492791)) >>> 0;
      sh.pts[i] += ((Math.imul(h ^ (h >>> 15), 2246822519) >>> 0) / 4294967296 - 0.5) * 1e-4;
    }
  });
  return shapes;
}

const orient = (ax, ay, bx, by, cx, cy) => (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);

// 꺾은선 고리(선분 = 두 점)가 지나는 칸 — 칸 줄(y)마다 변을 그 띠로 잘라 x 범위 · 띠 경계에 닿는 칸도 넣음(빠뜨리지 않게)
function cover(xs, ys, cell, fn) {
  const n = xs.length;
  let ylo = Infinity, yhi = -Infinity;
  for (const y of ys) { if (y < ylo) ylo = y; if (y > yhi) yhi = y; }
  for (let j = Math.floor(ylo / cell), je = Math.floor(yhi / cell); j <= je; j++) {
    const ya = j * cell, yb = ya + cell;
    let xlo = Infinity, xhi = -Infinity;
    for (let k = 0; k < n; k++) {
      const x1 = xs[k], y1 = ys[k], x2 = xs[(k + 1) % n], y2 = ys[(k + 1) % n], dy = y2 - y1;
      let t0 = 0, t1 = 1;
      if (dy === 0) { if (y1 < ya || y1 > yb) continue; }
      else {
        let ta = (ya - y1) / dy, tb = (yb - y1) / dy;
        if (ta > tb) { const t = ta; ta = tb; tb = t; }
        t0 = Math.max(0, ta); t1 = Math.min(1, tb);
        if (t0 > t1) continue;
      }
      const xa = x1 + (x2 - x1) * t0, xb = x1 + (x2 - x1) * t1;
      xlo = Math.min(xlo, xa, xb); xhi = Math.max(xhi, xa, xb);
    }
    if (xlo > xhi) continue;
    for (let i = Math.floor(xlo / cell), ie = Math.floor(xhi / cell); i <= ie; i++) fn(i, j);
  }
}

// 선분이 세모에 닿을 수 있나(가르는 축: 상자 · 선분 직선 · 세모 세 변) — 닿으면 참(경계에 닿음 포함 · 넉넉히)
function segTouchesTri(s, xa, ya, xb, yb, xc, yc, x0, y0, x1, y1) {
  if ((s.ax > s.bx ? s.ax : s.bx) < x0 || (s.ax < s.bx ? s.ax : s.bx) > x1 || (s.ay > s.by ? s.ay : s.by) < y0 || (s.ay < s.by ? s.ay : s.by) > y1) return false;
  const o1 = orient(s.ax, s.ay, s.bx, s.by, xa, ya), o2 = orient(s.ax, s.ay, s.bx, s.by, xb, yb), o3 = orient(s.ax, s.ay, s.bx, s.by, xc, yc);
  if ((o1 > 0 && o2 > 0 && o3 > 0) || (o1 < 0 && o2 < 0 && o3 < 0)) return false;
  const sg = orient(xa, ya, xb, yb, xc, yc) >= 0 ? 1 : -1;
  if (sg * orient(xa, ya, xb, yb, s.ax, s.ay) < 0 && sg * orient(xa, ya, xb, yb, s.bx, s.by) < 0) return false;
  if (sg * orient(xb, yb, xc, yc, s.ax, s.ay) < 0 && sg * orient(xb, yb, xc, yc, s.bx, s.by) < 0) return false;
  if (sg * orient(xc, yc, xa, ya, s.ax, s.ay) < 0 && sg * orient(xc, yc, xa, ya, s.bx, s.by) < 0) return false;
  return true;
}

function inPoly(p, x, y) {
  const xs = p.xs, ys = p.ys;
  let c = false;
  for (let i = 0, j = xs.length - 1; i < xs.length; j = i++)
    if ((ys[i] > y) !== (ys[j] > y) && x < ((xs[j] - xs[i]) * (y - ys[i])) / (ys[j] - ys[i]) + xs[i]) c = !c;
  return c;
}

// ── ③ 지형 자르기 ──
// P = 지형 꼭짓점(x, y, z …) · I = 세모 번호 · groups = BufferGeometry.groups(번호 단위) · shapes = holeShapes
// 돌려줌 = 자른 세모가 없으면 null · 있으면 { index, groups, faceSrc(새 세모 → 원래 세모), nv, src(새 점마다 원래 세 점), w(무게), xyz(자리), stats }
export function cutTerrain(P, I, groups, shapes) {
  const segs = [], polys = [];
  let gOff = 0, bx0 = Infinity, by0 = Infinity, bx1 = -Infinity, by1 = -Infinity;
  for (const sh of shapes) {
    const p = sh.pts;
    for (const [a, b] of sh.segs)
      segs.push({ id: segs.length, ax: p[2 * a], ay: p[2 * a + 1], bx: p[2 * b], by: p[2 * b + 1], za: sh.zt[a], zb: sh.zt[b], ga: gOff + a, gb: gOff + b });
    for (const q of sh.polys) {
      const xs = q.map(i => p[2 * i]), ys = q.map(i => p[2 * i + 1]);
      polys.push({ id: polys.length, xs, ys, x0: Math.min(...xs), y0: Math.min(...ys), x1: Math.max(...xs), y1: Math.max(...ys) });
    }
    for (let i = 0; i < p.length; i += 2) { bx0 = Math.min(bx0, p[i]); bx1 = Math.max(bx1, p[i]); by0 = Math.min(by0, p[i + 1]); by1 = Math.max(by1, p[i + 1]); }
    gOff += sh.zt.length;
  }
  if (!segs.length) return null;
  // 칸 색인(2 m) — ★[v117.16 검토 M] 선분 · 조각은 실제로 지나는 칸에만(상자 전체가 아님 — 두 점짜리 긴 대각선 측구가 상자로는 수십만 칸)
  const CELL = 2, grid = new Map();
  const ci = (v) => Math.floor(v / CELL), key = (i, j) => (i + 524288) * 1048576 + (j + 524288);
  const put = (kind, o, xs, ys) => cover(xs, ys, CELL, (i, j) => {
    const k = key(i, j);
    let c = grid.get(k);
    if (!c) grid.set(k, c = { s: [], p: [] });
    c[kind].push(o);
  });
  for (const s of segs) put('s', s, [s.ax, s.bx], [s.ay, s.by]);
  for (const q of polys) put('p', q, q.xs, q.ys);

  const V0 = P.length / 3, T0 = I.length / 3;
  const weld = new Map();   // 같은 자리 꼭짓점(구분마다 나눈 점) → 하나의 번호 — 변 셈 순서를 정한다
  const wid = (i) => { const k = P[3 * i] + ',' + P[3 * i + 1] + ',' + P[3 * i + 2]; let w = weld.get(k); if (w === undefined) weld.set(k, w = weld.size); return w; };
  const shared = new Map();  // "작은번호:큰번호:선분" → 새 점(같은 구분 이웃 세모와 나눠 씀)
  const nSrc = [], nW = [], nXYZ = [];
  let nv = 0;
  const segMark = new Int32Array(segs.length).fill(-1), polyMark = new Int32Array(polys.length).fill(-1);
  const stats = { tris: 0, dropped: 0, failed: 0, newV: 0 };
  // 결과 번호 · 원래 세모 — 형식 배열(세모 수만큼 + 여유 · 모자라면 1.5배) · JS 배열 수백만 칸을 안 만든다
  let out = new Uint32Array(I.length + Math.max(3072, 3 * Math.ceil(T0 * 0.03))), on = 0;
  let src = new Uint32Array(out.length / 3), sn = 0;
  const emit = (a, b, c, f) => {
    if (on + 3 > out.length) { const o2 = new Uint32Array(Math.ceil(out.length * 1.5 / 3) * 3); o2.set(out); out = o2; const s2 = new Uint32Array(o2.length / 3); s2.set(src); src = s2; }
    out[on++] = a; out[on++] = b; out[on++] = c; src[sn++] = f;
  };
  const outGroups = [];
  const G = groups && groups.length ? groups : [{ start: 0, count: I.length, materialIndex: 0 }];
  let changed = false;
  const cs = [], cp = [];

  for (const g of G) {
    const gStart = on;
    for (let f = g.start / 3, fEnd = (g.start + g.count) / 3; f < fEnd; f++) {
      // 세모 범위는 숫자로만 먼저 — 측구 근처가 아니면 배열을 하나도 안 만든다
      const i0 = I[3 * f], i1 = I[3 * f + 1], i2 = I[3 * f + 2];
      const xa = P[3 * i0], ya = P[3 * i0 + 1], xb = P[3 * i1], yb = P[3 * i1 + 1], xc = P[3 * i2], yc = P[3 * i2 + 1];
      const x0 = xa < xb ? (xa < xc ? xa : xc) : (xb < xc ? xb : xc), x1 = xa > xb ? (xa > xc ? xa : xc) : (xb > xc ? xb : xc);
      const y0 = ya < yb ? (ya < yc ? ya : yc) : (yb < yc ? yb : yc), y1 = ya > yb ? (ya > yc ? ya : yc) : (yb > yc ? yb : yc);
      let res = null;
      if (!(x1 < bx0 || x0 > bx1 || y1 < by0 || y0 > by1)) {
        cs.length = 0; cp.length = 0;
        for (let i = ci(x0), ie = ci(x1); i <= ie; i++) for (let j = ci(y0), je = ci(y1); j <= je; j++) {
          const c = grid.get(key(i, j));
          if (!c) continue;
          for (const s of c.s) if (segMark[s.id] !== f) { segMark[s.id] = f; if (segTouchesTri(s, xa, ya, xb, yb, xc, yc, x0, y0, x1, y1)) cs.push(s); }
          for (const q of c.p) if (polyMark[q.id] !== f) { polyMark[q.id] = f; cp.push(q); }
        }
        if (cp.length) {
          // 닿는 테두리가 없으면 세모 전체가 구멍 안이거나 밖 — 무게중심 하나로(나누기 셈 없음)
          if (!cs.length) res = cp.some(q => inPoly(q, (xa + xb + xc) / 3, (ya + yb + yc) / 3)) ? [] : null;
          else res = clipTri([i0, i1, i2], [xa, xb, xc], [ya, yb, yc], [P[3 * i0 + 2], P[3 * i1 + 2], P[3 * i2 + 2]], cs.slice(), cp.slice());
        }
      }
      if (res === null) { emit(i0, i1, i2, f); continue; }
      changed = true;
      stats.tris++;
      if (res === false) { stats.failed++; emit(i0, i1, i2, f); continue; }
      if (!res.length) stats.dropped++;
      for (const t of res) emit(t[0], t[1], t[2], f);
    }
    outGroups.push({ start: gStart, count: on - gStart, materialIndex: g.materialIndex ?? 0 });
  }
  if (!changed) return null;
  stats.newV = nv;
  return {
    index: out.subarray(0, on), groups: outGroups, faceSrc: src.subarray(0, sn), nv,
    src: new Uint32Array(nSrc), w: new Float64Array(nW), xyz: new Float64Array(nXYZ), stats,
  };

  // 세모 하나 — null = 그대로 · false = 못 셈(그대로 두고 셈) · [] = 통째 구멍 · [[a, b, c] …] = 남는 세모(전역 번호)
  function clipTri(C, X, Y, Z, cs, cp) {
    const area2 = orient(X[0], Y[0], X[1], Y[1], X[2], Y[2]);
    if (Math.abs(area2) < 1e-9) return null;
    const sg = area2 > 0 ? 1 : -1;
    const W = C.map(wid);
    // 세 변 — 정한 방향(용접 번호 작은 쪽 → 큰 쪽) · 안쪽 부호
    const E = [0, 1, 2].map(j => {
      const k = (j + 1) % 3, flip = W[j] > W[k];
      const u = flip ? k : j, v = flip ? j : k;
      return { j, k, u, v, ux: X[u], uy: Y[u], vx: X[v], vy: Y[v], inSign: flip ? -sg : sg, on: [] };
    });
    const inside = (x, y) => {
      for (const e of E) if ((orient(e.ux, e.uy, e.vx, e.vy, x, y) >= 0 ? 1 : -1) !== e.inSign) return false;
      return true;
    };
    const bary = (x, y) => {
      const w0 = orient(X[1], Y[1], X[2], Y[2], x, y) / area2, w1 = orient(X[2], Y[2], X[0], Y[0], x, y) / area2;
      return [w0, w1, 1 - w0 - w1];
    };
    // 지역 점 — x, y · 무게(세 꼭짓점) · z · 전역 번호(꼭짓점 = 원래 번호 · 새 점 = 쓸 때 정함) · 나눠 쓰는 열쇠
    const vx = [X[0], X[1], X[2]], vy = [Y[0], Y[1], Y[2]], vw = [[1, 0, 0], [0, 1, 0], [0, 0, 1]], vz = [Z[0], Z[1], Z[2]], vg = [C[0], C[1], C[2]], vk = [null, null, null];
    const local = new Map();
    const addV = (lk, x, y, w, z, sk) => {
      let i = local.get(lk);
      if (i !== undefined) { vz[i] = Math.min(vz[i], z); return i; }
      i = vx.length;
      vx.push(x); vy.push(y); vw.push(w); vz.push(z); vg.push(-1); vk.push(sk);
      local.set(lk, i);
      return i;
    };
    const segIn = [];
    for (const s of cs) {
      const hits = [];
      for (const e of E) {
        const oU = orient(s.ax, s.ay, s.bx, s.by, e.ux, e.uy), oV = orient(s.ax, s.ay, s.bx, s.by, e.vx, e.vy);
        if ((oU >= 0) === (oV >= 0)) continue;
        const oA = orient(e.ux, e.uy, e.vx, e.vy, s.ax, s.ay), oB = orient(e.ux, e.uy, e.vx, e.vy, s.bx, s.by);
        if ((oA >= 0) === (oB >= 0)) continue;
        const lam = oU / (oU - oV), t = oA / (oA - oB);
        const x = e.ux + lam * (e.vx - e.ux), y = e.uy + lam * (e.vy - e.uy);
        const zt = Z[e.u] + lam * (Z[e.v] - Z[e.u]), zs = s.za + t * (s.zb - s.za);
        const w = [0, 0, 0]; w[e.u] = 1 - lam; w[e.v] = lam;
        const a = C[e.j], b = C[e.k];
        const sk = (a < b ? a + ':' + b : b + ':' + a) + ':' + s.id;
        const lv = addV('e' + sk, x, y, w, Math.min(zt, zs), sk);
        e.on.push({ lam: e.u === e.j ? lam : 1 - lam, v: lv });
        hits.push({ t, v: lv });
      }
      const inA = inside(s.ax, s.ay), inB = inside(s.bx, s.by);
      const list = [];
      const endV = (gi, x, y, z) => { const w = bary(x, y); return addV('p' + gi, x, y, w, Math.min(w[0] * Z[0] + w[1] * Z[1] + w[2] * Z[2], z), null); };
      if (inA) list.push({ t: 0, v: endV(s.ga, s.ax, s.ay, s.za) });
      hits.sort((p, q) => p.t - q.t);
      list.push(...hits);
      if (inB) list.push({ t: 1, v: endV(s.gb, s.bx, s.by, s.zb) });
      if (!list.length) continue;
      if (list.length !== 2) return false;
      segIn.push({ s, t0: list[0].t, t1: list[1].t, v0: list[0].v, v1: list[1].v, cut: [] });
    }
    if (!segIn.length) {
      const mx = (X[0] + X[1] + X[2]) / 3, my = (Y[0] + Y[1] + Y[2]) / 3;
      return cp.some(q => inPoly(q, mx, my)) ? [] : null;
    }
    // 선분끼리 만나는 점(세모 안)
    for (let i = 0; i < segIn.length; i++) for (let j = i + 1; j < segIn.length; j++) {
      let A = segIn[i], B = segIn[j];
      if (A.s.id > B.s.id) [A, B] = [B, A];
      const a = A.s, b = B.s;
      if (a.ga === b.ga || a.ga === b.gb || a.gb === b.ga || a.gb === b.gb) continue;
      const o1 = orient(a.ax, a.ay, a.bx, a.by, b.ax, b.ay), o2 = orient(a.ax, a.ay, a.bx, a.by, b.bx, b.by);
      if ((o1 >= 0) === (o2 >= 0)) continue;
      const o3 = orient(b.ax, b.ay, b.bx, b.by, a.ax, a.ay), o4 = orient(b.ax, b.ay, b.bx, b.by, a.bx, a.by);
      if ((o3 >= 0) === (o4 >= 0)) continue;
      const ta = o3 / (o3 - o4), tb = o1 / (o1 - o2);
      const inA = ta > A.t0 && ta < A.t1, inB = tb > B.t0 && tb < B.t1;
      if (!inA && !inB) continue;
      if (inA !== inB) return false;
      const x = a.ax + ta * (a.bx - a.ax), y = a.ay + ta * (a.by - a.ay), w = bary(x, y);
      const z = Math.min(w[0] * Z[0] + w[1] * Z[1] + w[2] * Z[2], a.za + ta * (a.zb - a.za), b.za + tb * (b.zb - b.za));
      const lv = addV('x' + a.id + ':' + b.id, x, y, w, z, null);
      A.cut.push({ t: ta, v: lv }); B.cut.push({ t: tb, v: lv });
    }
    // 평면 그래프 — 변(세모 변 조각 · 선분 조각)
    const adj = vx.map(() => []);
    const seen = new Set();
    const link = (a, b) => {
      if (a === b) return;
      const k = a < b ? a + ',' + b : b + ',' + a;
      if (seen.has(k)) return;
      seen.add(k); adj[a].push(b); adj[b].push(a);
    };
    for (const S of segIn) {
      const ps = [{ t: S.t0, v: S.v0 }, ...S.cut.sort((p, q) => p.t - q.t), { t: S.t1, v: S.v1 }];
      for (let i = 0; i + 1 < ps.length; i++) link(ps[i].v, ps[i + 1].v);
    }
    for (const e of E) {
      const ps = [{ lam: 0, v: e.j }, ...e.on.sort((p, q) => p.lam - q.lam), { lam: 1, v: e.k }];
      for (let i = 0; i + 1 < ps.length; i++) link(ps[i].v, ps[i + 1].v);
    }
    // 면 찾기 — 점마다 나가는 변을 각도 순(세모 감김 쪽을 반시계로) · (u→v) 다음 = v에서 (v→u) 바로 앞(시계 쪽) 변
    const nV = vx.length;
    const ang = (a, b) => Math.atan2(sg * (vy[b] - vy[a]), vx[b] - vx[a]);
    const nb = adj.map((l, a) => l.slice().sort((p, q) => ang(a, p) - ang(a, q)));
    const used = nb.map(l => new Uint8Array(l.length));
    const cpar = new Int32Array(nV);
    for (let i = 0; i < nV; i++) cpar[i] = i;
    const cf = (a) => { while (cpar[a] !== a) { cpar[a] = cpar[cpar[a]]; a = cpar[a]; } return a; };
    for (let a = 0; a < nV; a++) for (const b of adj[a]) { const ra = cf(a), rb = cf(b); if (ra !== rb) cpar[rb] = ra; }
    const faces = [];
    for (let a0 = 0; a0 < nV; a0++) for (let k0 = 0; k0 < nb[a0].length; k0++) {
      if (used[a0][k0]) continue;
      const vs = [];
      let a = a0, k = k0, ar = 0, guard = 0;
      while (!used[a][k]) {
        if (++guard > 4 * nV + 16) return false;
        used[a][k] = 1;
        const b = nb[a][k];
        vs.push(a);
        ar += vx[a] * vy[b] - vx[b] * vy[a];
        const i = nb[b].indexOf(a);
        k = (i - 1 + nb[b].length) % nb[b].length;
        a = b;
      }
      faces.push({ vs, area: (ar * sg) / 2, comp: cf(vs[0]), holes: [] });
    }
    const main = cf(0);
    const pos = faces.filter(F => F.area > 1e-12);
    const ptIn = (F, x, y) => inPoly({ xs: F.vs.map(i => vx[i]), ys: F.vs.map(i => vy[i]) }, x, y);
    // 섬(세모 변에 안 닿는 테두리 고리) — 그 고리를 품은 가장 작은 면의 구멍으로
    for (const F of faces) {
      if (F.area >= -1e-12 || F.comp === main) continue;
      const x = vx[F.vs[0]], y = vy[F.vs[0]];
      let best = null;
      for (const Q of pos) if (Q.comp !== F.comp && (!best || Q.area < best.area) && ptIn(Q, x, y)) best = Q;
      if (best) best.holes.push(F.vs);
    }
    const tris = [];
    for (const F of pos) {
      const ids = [...F.vs, ...F.holes.flat()];
      const contour = F.vs.map(i => new Vector2(vx[i], vy[i]));
      const holes = F.holes.map(h => h.map(i => new Vector2(vx[i], vy[i])));
      // 끝점 = 첫 점이면 three가 말없이 빼서 번호가 밀린다 — 그런 겹친 고리는 못 셈으로
      if ([contour, ...holes].some(r => r.length > 2 && r[r.length - 1].equals(r[0]))) return false;
      const tr = ShapeUtils.triangulateShape(contour, holes);
      if (!tr.length) { if (F.area > 1e-6) return false; continue; }
      // 안/밖 — 가장 큰 세모의 무게중심
      let best = null, ba = -1;
      for (const t of tr) {
        const a = ids[t[0]], b = ids[t[1]], c = ids[t[2]];
        const s = Math.abs(orient(vx[a], vy[a], vx[b], vy[b], vx[c], vy[c]));
        if (s > ba) { ba = s; best = [a, b, c]; }
      }
      const mx = (vx[best[0]] + vx[best[1]] + vx[best[2]]) / 3, my = (vy[best[0]] + vy[best[1]] + vy[best[2]]) / 3;
      if (cp.some(q => inPoly(q, mx, my))) continue;
      for (const t of tr) {
        let a = ids[t[0]], b = ids[t[1]], c = ids[t[2]];
        const o = orient(vx[a], vy[a], vx[b], vy[b], vx[c], vy[c]);
        if (o === 0) continue;
        if ((o > 0 ? 1 : -1) !== sg) [b, c] = [c, b];
        tris.push([a, b, c]);
      }
    }
    // 전역 번호 — 원래 꼭짓점은 그대로 · 새 점은 쓸 때 만든다(같은 변 · 같은 선분이면 이웃과 하나)
    const gid = (i) => {
      if (vg[i] >= 0) return vg[i];
      if (vk[i] && shared.has(vk[i])) return (vg[i] = shared.get(vk[i]));
      const g = V0 + nv++;
      nSrc.push(C[0], C[1], C[2]); nW.push(vw[i][0], vw[i][1], vw[i][2]); nXYZ.push(vx[i], vy[i], vz[i]);
      if (vk[i]) shared.set(vk[i], g);
      return (vg[i] = g);
    };
    return tris.map(t => [gid(t[0]), gid(t[1]), gid(t[2])]);
  }
}

// 꼭짓점 값 늘이기 — 원래 값 뒤에 새 점(세 점 무게 섞음) · position은 정한 자리(x, y, 맞춘 z) · normal은 길이 1로
export function extendAttribute(arr, k, V0, cut, name) {
  const outA = new arr.constructor((V0 + cut.nv) * k);
  outA.set(arr.subarray(0, V0 * k));
  const round = !(arr instanceof Float32Array || arr instanceof Float64Array);
  for (let n = 0; n < cut.nv; n++) {
    const o = (V0 + n) * k, s = cut.src, w = cut.w, a = s[3 * n] * k, b = s[3 * n + 1] * k, c = s[3 * n + 2] * k;
    if (name === 'position' && k === 3) { outA[o] = cut.xyz[3 * n]; outA[o + 1] = cut.xyz[3 * n + 1]; outA[o + 2] = cut.xyz[3 * n + 2]; continue; }
    let len = 0;
    for (let i = 0; i < k; i++) {
      const v = w[3 * n] * arr[a + i] + w[3 * n + 1] * arr[b + i] + w[3 * n + 2] * arr[c + i];
      outA[o + i] = round ? Math.round(v) : v;
      len += v * v;
    }
    if (name === 'normal' && len > 0) { const r = 1 / Math.sqrt(len); for (let i = 0; i < k; i++) outA[o + i] *= r; }
  }
  return outA;
}
