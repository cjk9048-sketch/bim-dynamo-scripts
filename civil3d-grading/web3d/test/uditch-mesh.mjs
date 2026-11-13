// ★[v117.16] 시험용 — C# UDitch.Meshes · SceneMesh(본체 + 기초 · 뚜껑 자리 없음)를 그대로 옮김(측점마다 단면 m점 · 옆 = 이등분 · 1/cos 늘림 · 마구리 귀 자르기 · 바깥 감김)
//   (이름에 .test 없음 — npm test가 직접 돌리지 않는다 · uhole.test.mjs · uditch-page.mjs가 씀)
export function sceneMesh(pts, inv0, inv1, d = {}) {
  const B = d.B ?? 0.3, H = d.H ?? 0.3, Tw = d.Tw ?? 0.15, Tb = d.Tb ?? 0.15, Tf = d.Tf ?? 0.1, Pf = d.Pf ?? 0.1;
  const n = pts.length, W = B + 2 * Tw, bot = -(H + Tb);
  const sta = [0];
  for (let k = 1; k < n; k++) sta.push(sta[k - 1] + Math.hypot(pts[k][0] - pts[k - 1][0], pts[k][1] - pts[k - 1][1]));
  const inv = (k) => inv0 + ((inv1 - inv0) * sta[k]) / sta[n - 1];
  const nrm = (a, b) => { const dx = pts[b][0] - pts[a][0], dy = pts[b][1] - pts[a][1], l = Math.hypot(dx, dy); return [-dy / l, dx / l]; };
  const side = pts.map((_, k) => {
    if (k === 0) return nrm(0, 1);
    if (k === n - 1) return nrm(n - 2, n - 1);
    const a = nrm(k - 1, k), b = nrm(k, k + 1);
    let mx = a[0] + b[0], my = a[1] + b[1];
    const ml = Math.hypot(mx, my); mx /= ml; my /= ml;
    const c = mx * a[0] + my * a[1];
    return [mx / c, my / c];
  });
  const body = [[0, 0], [0, bot], [W, bot], [W, 0], [Tw + B, 0], [Tw + B, -H], [Tw, -H], [Tw, 0]];
  const foot = [[-Pf, bot], [-Pf, bot - Tf], [W + Pf, bot - Tf], [W + Pf, bot]];
  const pos = [], idx = [], parts = [];
  for (const [p0, part] of Tf > 0 ? [[body, '본체'], [foot, '기초']] : [[body, '본체']]) {
    let poly = p0.map(([o, z]) => [o - W / 2, z + H]);
    let sa = 0;
    for (let i = 0; i < poly.length; i++) { const a = poly[i], b = poly[(i + 1) % poly.length]; sa += a[0] * b[1] - b[0] * a[1]; }
    if (sa < 0) poly = poly.reverse();
    const m = poly.length, V = [], F = [];
    for (let k = 0; k < n; k++) for (const q of poly) V.push([pts[k][0] + side[k][0] * q[0], pts[k][1] + side[k][1] * q[0], inv(k) + q[1]]);
    for (let k = 0; k + 1 < n; k++) for (let i = 0; i < m; i++) {
      const j = (i + 1) % m, a = k * m + i, b = k * m + j, c = (k + 1) * m + j, e = (k + 1) * m + i;
      if (poly[i][0] < poly[j][0]) F.push([a, b, c], [a, c, e]); else F.push([a, b, e], [b, c, e]);
    }
    for (const [a, b, c] of earClip(poly)) { F.push([a, c, b]); F.push([(n - 1) * m + a, (n - 1) * m + b, (n - 1) * m + c]); }
    let vol = 0;
    const o = V[0];
    for (const [a, b, c] of F) {
      const A = V[a].map((v, i) => v - o[i]), Bv = V[b].map((v, i) => v - o[i]), Cv = V[c].map((v, i) => v - o[i]);
      vol += A[0] * (Bv[1] * Cv[2] - Bv[2] * Cv[1]) - A[1] * (Bv[0] * Cv[2] - Bv[2] * Cv[0]) + A[2] * (Bv[0] * Cv[1] - Bv[1] * Cv[0]);
    }
    const base = pos.length / 3;
    parts.push({ part, seg: 1, v0: base, n: V.length });
    for (const v of V) pos.push(...v);
    for (const f of F) { const t = vol < 0 ? [f[0], f[2], f[1]] : f; idx.push(base + t[0], base + t[1], base + t[2]); }
  }
  return { pos: new Float32Array(pos), idx: new Uint32Array(idx), W, parts };
}

// ★[v117.16] C# FollowMeshes처럼 구간 덩어리 여럿 + 낙차벽을 한 메시로 — chunks = [{ m: sceneMesh 결과, seg } | { box: [[x,y]×4](반시계), zb, zt, seg }] → { pos, idx, parts }
export function joinMeshes(chunks) {
  const pos = [], idx = [], parts = [];
  for (const ch of chunks) {
    const base = pos.length / 3;
    if (ch.m) {
      pos.push(...ch.m.pos);
      for (const i of ch.m.idx) idx.push(base + i);
      for (const q of ch.m.parts) parts.push({ part: q.part, seg: ch.seg, v0: base + q.v0, n: q.n });
    } else {
      for (const z of [ch.zb, ch.zt]) for (const [x, y] of ch.box) pos.push(x, y, z);
      for (const f of [[3, 2, 1], [3, 1, 0], [4, 5, 6], [4, 6, 7], ...[0, 1, 2, 3].flatMap(i => { const j = (i + 1) % 4; return [[i, j, 4 + j], [i, 4 + j, 4 + i]]; })]) idx.push(base + f[0], base + f[1], base + f[2]);
      parts.push({ part: '낙차벽', seg: ch.seg, v0: base, n: 8 });
    }
  }
  return { pos: new Float32Array(pos), idx: new Uint32Array(idx), parts };
}

function earClip(poly) {
  const idx = poly.map((_, i) => i), o = [];
  const cr = (a, b, c) => (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
  let guard = 0;
  while (idx.length > 3 && guard++ < 10000) {
    let cut = false;
    for (let t = 0; t < idx.length; t++) {
      const ia = idx[(t + idx.length - 1) % idx.length], ib = idx[t], ic = idx[(t + 1) % idx.length];
      const a = poly[ia], b = poly[ib], c = poly[ic];
      if (cr(a, b, c) <= 1e-12) continue;
      let inside = false;
      for (const q of idx) {
        if (q === ia || q === ib || q === ic) continue;
        const x = poly[q];
        if (cr(a, b, x) >= -1e-12 && cr(b, c, x) >= -1e-12 && cr(c, a, x) >= -1e-12) { inside = true; break; }
      }
      if (inside) continue;
      o.push([ia, ib, ic]); idx.splice(t, 1); cut = true; break;
    }
    if (!cut) break;
  }
  if (idx.length === 3) o.push([idx[0], idx[1], idx[2]]);
  return o;
}

// 격자 지형(가로 nx · 세로 ny 칸 · 칸 h m · 왼아래 x0, y0 · z(x, y)) — 구분 cls(x, y) → 번호(구분마다 꼭짓점을 따로 · C#처럼 구분 차례로 정렬)
export function gridTerrain(nx, ny, h, x0, y0, z, cls = () => 0) {
  const tris = [];
  for (let j = 0; j < ny; j++) for (let i = 0; i < nx; i++) {
    const a = [i, j], b = [i + 1, j], c = [i + 1, j + 1], d = [i, j + 1];
    for (const t of (i + j) % 2 ? [[a, b, c], [a, c, d]] : [[a, b, d], [b, c, d]]) {
      const mx = x0 + ((t[0][0] + t[1][0] + t[2][0]) / 3) * h, my = y0 + ((t[0][1] + t[1][1] + t[2][1]) / 3) * h;
      tris.push({ t, c: cls(mx, my) });
    }
  }
  tris.sort((p, q) => p.c - q.c);
  const pos = [], idx = [], maps = new Map(), ranges = [];
  for (const { t, c } of tris) {
    if (!maps.has(c)) { maps.set(c, new Map()); ranges.push([c, idx.length / 3, 0]); }
    const M = maps.get(c);
    for (const [i, j] of t) {
      const k = i * 100003 + j;
      if (!M.has(k)) { const x = x0 + i * h, y = y0 + j * h; M.set(k, pos.length / 3); pos.push(x, y, z(x, y)); }
      idx.push(M.get(k));
    }
    ranges[ranges.length - 1][2]++;
  }
  return { pos: new Float32Array(pos), idx: new Uint32Array(idx), ranges };
}
