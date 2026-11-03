// ★★[JACK 1103 «3D 뷰에서 지층을 모델링했다면 자르기 뷰에서 지층도 보이게»] 자른 면 위 지층 — 셈만(three 없음 · 시험이 node로 바로 부른다).
//   장면 머리 strata(C# Web3DStrata.Head): {x0, y0, cell, w, h, layers: [{name, ord, color, z}], water?: {name, z}} — 층 z = 그 층 «상단»(위에서 아래 차례) · 마디 j·(w+1) + i · 없음 NaN
//   층 k 띠 = 위 max(zb, min(지형, 상단 k)) ~ 아래 max(zb, min(지형, 다음 층 상단 · 마지막 층은 zb)) — 최종 지표면 아래만(절토가 층 상단을 파 내려간 자리도 맞게)
//   지형 선분을 칸 반 이하로 잘게 · 지형과 층 상단 · zb가 엇갈리는 자리에서 한 번 더 자른다(그 안에서는 min · max가 곧은 선).

// 마디 높이 쌍선형 — 네 마디 중 하나라도 없으면 NaN · 범위 밖 NaN
export function gridZ(S, z, x, y) {
  const fx = (x - S.x0) / S.cell, fy = (y - S.y0) / S.cell;
  if (!(fx >= 0 && fy >= 0 && fx <= S.w && fy <= S.h)) return NaN;
  const i = Math.min(Math.floor(fx), S.w - 1), j = Math.min(Math.floor(fy), S.h - 1);
  const u = fx - i, v = fy - j, W = S.w + 1, k = j * W + i;
  const a = z[k], b = z[k + 1], c = z[k + W], d = z[k + W + 1];
  if (!(Number.isFinite(a) && Number.isFinite(b) && Number.isFinite(c) && Number.isFinite(d))) return NaN;
  return (a * (1 - u) + b * u) * (1 - v) + (c * (1 - u) + d * u) * v;
}

// 지형 선분 묶음(seg: ax,ay,az,bx,by,bz … · z = 지형 + dz) → 층마다 세모(x,y,z 아홉 개씩) · 지하수위 선분(지형 아래만)
export function strataCut(S, seg, zb, dz = 0) {
  const n = S.layers.length;
  const tris = S.layers.map(() => []);
  const water = [];
  const step = Math.max(0.05, S.cell / 2);
  const Lz = S.layers.map(l => l.z), Wz = S.water ? S.water.z : null;
  const sk = new Array(n), ek = new Array(n);
  const gx0 = S.x0, gy0 = S.y0, gx1 = S.x0 + S.w * S.cell, gy1 = S.y0 + S.h * S.cell;
  for (let s = 0; s + 5 < seg.length; s += 6) {
    const ax = seg[s], ay = seg[s + 1], az = seg[s + 2] - dz, bx = seg[s + 3], by = seg[s + 4], bz = seg[s + 5] - dz;
    const len = Math.hypot(bx - ax, by - ay);
    if (!(len > 1e-6)) continue;
    if (Math.max(ax, bx) < gx0 || Math.min(ax, bx) > gx1 || Math.max(ay, by) < gy0 || Math.min(ay, by) > gy1) continue;   // 격자 밖 선분(먼 지형)
    const m = Math.max(1, Math.ceil(len / step));
    for (let q = 0; q < m; q++) {
      const u0 = q / m, u1 = (q + 1) / m;
      const x0 = ax + (bx - ax) * u0, y0 = ay + (by - ay) * u0, x1 = ax + (bx - ax) * u1, y1 = ay + (by - ay) * u1;
      const T0 = az + (bz - az) * u0, T1 = az + (bz - az) * u1;
      let any = false;
      for (let k = 0; k < n; k++) {
        sk[k] = gridZ(S, Lz[k], x0, y0); ek[k] = gridZ(S, Lz[k], x1, y1);
        if (!(Number.isFinite(sk[k]) && Number.isFinite(ek[k]))) sk[k] = ek[k] = NaN; else any = true;
      }
      // 지하수위 — 지형 아래 · zb 위만
      if (Wz) {
        const w0 = gridZ(S, Wz, x0, y0), w1 = gridZ(S, Wz, x1, y1);
        if (Number.isFinite(w0) && Number.isFinite(w1)) {
          const cuts = [0, 1];
          for (const [p, r] of [[T0 - w0, T1 - w1], [w0 - zb, w1 - zb]]) if (p * r < 0) cuts.push(p / (p - r));
          cuts.sort((a, b) => a - b);
          for (let c = 0; c + 1 < cuts.length; c++) {
            const t0 = cuts[c], t1 = cuts[c + 1];
            if (t1 - t0 < 1e-9) continue;
            const tm = (t0 + t1) / 2, wm = w0 + (w1 - w0) * tm, Tm = T0 + (T1 - T0) * tm;
            if (wm > Tm || wm < zb) continue;
            water.push(x0 + (x1 - x0) * t0, y0 + (y1 - y0) * t0, w0 + (w1 - w0) * t0, x0 + (x1 - x0) * t1, y0 + (y1 - y0) * t1, w0 + (w1 - w0) * t1);
          }
        }
      }
      if (!any) continue;
      // 지형 · zb와 층 상단이 엇갈리는 자리
      const cuts = [0, 1];
      for (let k = 0; k < n; k++) {
        if (!Number.isFinite(sk[k])) continue;
        for (const [p, r] of [[T0 - sk[k], T1 - ek[k]], [sk[k] - zb, ek[k] - zb]]) if (p * r < 0) cuts.push(p / (p - r));
      }
      cuts.sort((a, b) => a - b);
      for (let c = 0; c + 1 < cuts.length; c++) {
        const t0 = cuts[c], t1 = cuts[c + 1];
        if (t1 - t0 < 1e-9) continue;
        const px = x0 + (x1 - x0) * t0, py = y0 + (y1 - y0) * t0, qx = x0 + (x1 - x0) * t1, qy = y0 + (y1 - y0) * t1;
        const Ta = T0 + (T1 - T0) * t0, Tb = T0 + (T1 - T0) * t1;
        const at = (k, t) => sk[k] + (ek[k] - sk[k]) * t;
        for (let k = 0; k < n; k++) {
          if (!Number.isFinite(sk[k])) continue;
          let nx = -1;
          for (let j = k + 1; j < n; j++) if (Number.isFinite(sk[j])) { nx = j; break; }
          const topA = Math.max(zb, Math.min(Ta, at(k, t0))), topB = Math.max(zb, Math.min(Tb, at(k, t1)));
          const botA = nx < 0 ? zb : Math.max(zb, Math.min(Ta, at(nx, t0))), botB = nx < 0 ? zb : Math.max(zb, Math.min(Tb, at(nx, t1)));
          const ba = Math.min(botA, topA), bb = Math.min(botB, topB);
          if (topA - ba < 1e-4 && topB - bb < 1e-4) continue;
          tris[k].push(px, py, topA, qx, qy, topB, qx, qy, bb, px, py, topA, qx, qy, bb, px, py, ba);
        }
      }
    }
  }
  return { tris, water: alongCut(water) };
}

// 선분을 자른 선 방향 차례로(지형 선분은 세모 차례로 온다) · 한 방향으로 — 점선 마디가 선을 따라 고르게 이어지게
function alongCut(seg) {
  if (seg.length < 6) return seg;
  let dx = 0, dy = 0;
  for (let i = 0; i + 5 < seg.length && Math.hypot(dx, dy) < 1e-9; i += 6) { dx = seg[i + 3] - seg[i]; dy = seg[i + 4] - seg[i + 1]; }
  const k = (i) => seg[i] * dx + seg[i + 1] * dy;
  const out = [];
  for (let i = 0; i + 5 < seg.length; i += 6) out.push(k(i) <= k(i + 3) ? seg.slice(i, i + 6) : [...seg.slice(i + 3, i + 6), ...seg.slice(i, i + 3)]);
  out.sort((a, b) => (a[0] * dx + a[1] * dy) - (b[0] * dx + b[1] * dy));
  return out.flat();
}
