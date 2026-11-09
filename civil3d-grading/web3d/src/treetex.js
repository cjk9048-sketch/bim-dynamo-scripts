// ★[v116 · JACK 1109 «조감도 품질»] 나무 질감 — 코드로 그린 잎 · 껍질 묶음 그림(아틀라스 1024² · 4 × 4 칸 · 칸 256 px).
//   외부 그림 파일 없음(뷰어 묶음 크기 0 · 출처 걱정 없음) · 같은 입력 = 같은 그림(난수는 씨앗 고정) · three 없이 시험됨(순수 함수).
//   칸: 0~3 활엽 잎 뭉치 · 4~6 침엽(전나무 · 낙엽송) 바늘 가지 · 7~9 소나무 바늘 다발 · 10~11 대나무 잎 · 12 껍질(일반) · 13 소나무 껍질(붉은 위) · 14 대나무 줄기 · 15 밝은 껍질(자작 · 포플러).
//   밉맵은 칸마다 따로 줄인다(칸끼리 번지지 않음) · 알파는 단계마다 «0.5 넘는 화소 비율»이 원래 그림과 같도록 늘린다(멀리서 잎이 말라 사라지지 않게 — 알파 투 커버리지).
//   데이터 첫 줄 = v 0(아래) — 칸 안에서 +y가 위.

export const TEX_SIZE = 1024, TILE = 256, GRID = 4;
export const TILES = { broad: [0, 1, 2, 3], conifer: [4, 5, 6], pine: [7, 8, 9], bamboo: [10, 11], bark: 12, barkPine: 13, culm: 14, barkLight: 15 };
// 칸 → uv 사각형 [u0, v0, u1, v1](가장자리 2 px 안쪽)
export function tileRect(t) {
  const c = t % GRID, r = Math.floor(t / GRID), e = 2 / TEX_SIZE, s = 1 / GRID;
  return [c * s + e, r * s + e, (c + 1) * s - e, (r + 1) * s - e];
}

// ── 정해진 난수 · 값 잡음 ──
function rng(seed) {
  let s = seed >>> 0 || 1;
  return () => { s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0; return s / 4294967296; };
}
const hash = (x, y, s) => { let h = Math.imul(x, 374761393) + Math.imul(y, 668265263) + Math.imul(s, 1442695041); h = Math.imul(h ^ (h >>> 13), 1274126177); return ((h ^ (h >>> 16)) >>> 0) / 4294967296; };
function vnoise(x, y, s, px) {   // px = 주기(가로로 이어지게 — 줄기를 한 바퀴 감으면 이음매 없음)
  const x0 = Math.floor(x), y0 = Math.floor(y), fx = x - x0, fy = y - y0;
  const u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy);
  const w = (i) => ((i % px) + px) % px;
  const a = hash(w(x0), y0, s), b = hash(w(x0 + 1), y0, s), c = hash(w(x0), y0 + 1, s), d = hash(w(x0 + 1), y0 + 1, s);
  return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
}

// ── 칸 하나를 그리는 판(미리 곱한 RGBA · 실수) ──
class Tile {
  constructor() { this.px = new Float32Array(TILE * TILE * 4); }
  // 잎(양끝이 뾰족한 렌즈) — 가운데 (cx, cy) · 반 길이 a · 반 폭 b · 각 ang · 색 rgb(0~1) · 불투명 op · 잎맥 쪽 밝게
  leaf(cx, cy, a, b, ang, rgb, op = 1, base = 0.35) {
    const ca = Math.cos(ang), sa = Math.sin(ang), A = a + 1.5, Bw = b + 1.5;
    const ex = Math.abs(ca) * A + Math.abs(sa) * Bw, ey = Math.abs(sa) * A + Math.abs(ca) * Bw;   // 돌린 잎을 감싸는 상자
    const x0 = Math.max(0, Math.floor(cx - ex)), x1 = Math.min(TILE - 1, Math.ceil(cx + ex)), y0 = Math.max(0, Math.floor(cy - ey)), y1 = Math.min(TILE - 1, Math.ceil(cy + ey));
    const P = this.px;
    for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) {
      const dx = x + 0.5 - cx, dy = y + 0.5 - cy;
      const u = dx * ca + dy * sa, v = -dx * sa + dy * ca;
      const t = u / a;
      if (t <= -1.05 || t >= 1.05) continue;
      const tt = Math.min(1, Math.abs(t));
      // 밑(t < 0)은 통통하게 · 끝은 뾰족하게
      const q = Math.max(0, 1 - tt * tt), sq = Math.sqrt(q);
      const hw = b * (t < 0 ? sq : sq * Math.sqrt(sq));   // q^0.5(밑 · 통통) · q^0.75(끝 · 뾰족)
      const cov = Math.max(0, Math.min(1, hw - Math.abs(v) + 0.5)) * op;
      if (cov <= 0) continue;
      const shade = (1 - base) + base * (1 - Math.min(1, Math.abs(v) / Math.max(0.5, hw))) - (v > 0 ? 0.06 : 0);
      const k = (y * TILE + x) * 4, inv = 1 - cov;
      P[k] = rgb[0] * shade * cov + P[k] * inv;
      P[k + 1] = rgb[1] * shade * cov + P[k + 1] * inv;
      P[k + 2] = rgb[2] * shade * cov + P[k + 2] * inv;
      P[k + 3] = cov + P[k + 3] * inv;
    }
  }
  // 둥근 얼룩(뭉치 속 어두운 그늘 · 틈 메우기)
  disc(cx, cy, r, rgb, op) {
    const P = this.px, x0 = Math.max(0, Math.floor(cx - r - 1)), x1 = Math.min(TILE - 1, Math.ceil(cx + r + 1)), y0 = Math.max(0, Math.floor(cy - r - 1)), y1 = Math.min(TILE - 1, Math.ceil(cy + r + 1));
    for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) {
      const d = Math.hypot(x + 0.5 - cx, y + 0.5 - cy);
      const cov = Math.max(0, Math.min(1, r - d + 0.5)) * op * (1 - 0.5 * Math.min(1, d / r) ** 2);
      if (cov <= 0) continue;
      const k = (y * TILE + x) * 4, inv = 1 - cov;
      for (let c = 0; c < 3; c++) P[k + c] = rgb[c] * cov + P[k + c] * inv;
      P[k + 3] = cov + P[k + 3] * inv;
    }
  }
  // 가는 선(가지 · 잔가지)
  stroke(x0, y0, x1, y1, w, rgb, op = 1) {
    const L = Math.hypot(x1 - x0, y1 - y0), n = Math.max(2, Math.ceil(L / 1.5));
    for (let i = 0; i <= n; i++) { const t = i / n; this.disc(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, w * (1 - 0.5 * t), rgb, op); }
  }
}

const C = TILE / 2;
const tint = (r, k, base) => [base[0] * k * (0.94 + 0.12 * r()), base[1] * k * (0.96 + 0.08 * r()), base[2] * k * (0.9 + 0.2 * r())];

// 활엽 잎 뭉치 — 들쭉날쭉한 둥근 덩이 · 안쪽은 앞(밝게) · 가장자리 · 뒤는 어둡게
function paintBroad(T, seed) {
  const r = rng(seed), R0 = TILE * 0.44, lob = [r() * 6.28, r() * 6.28, r() * 6.28];
  const edge = (a) => R0 * (0.80 + 0.10 * Math.sin(3 * a + lob[0]) + 0.07 * Math.sin(5 * a + lob[1]) + 0.05 * Math.sin(7 * a + lob[2]));
  T.disc(C, C, R0 * 0.62, [0.42, 0.48, 0.34], 0.95);   // 속 그늘(틈 사이로 보이는 어두운 안쪽)
  const L = [];
  for (let i = 0; i < 620; i++) {
    const a = r() * Math.PI * 2, e = edge(a), rr = e * Math.pow(r(), 0.55);
    const x = C + Math.cos(a) * rr, y = C + Math.sin(a) * rr;
    L.push({ x, y, a, f: rr / e, z: r() * 0.6 + (1 - rr / e) * 0.8 });
  }
  L.sort((p, q) => p.z - q.z);   // 뒤 → 앞
  for (const l of L) {
    const len = 6.5 + r() * 4.5, k = 0.60 + 0.40 * Math.min(1, l.z) + (r() - 0.5) * 0.16;
    const ang = l.a + (r() - 0.5) * 1.9;
    T.leaf(l.x, l.y, len, len * (0.42 + r() * 0.12), ang, tint(r, Math.min(1.05, k), [0.80, 0.95, 0.70]));
  }
}

// 침엽(전나무 · 낙엽송 · 잣) 가지 — 짙은 속 + 가운데서 뻗은 가지 두 겹마다 짧은 바늘 · 끝으로 갈수록 처지고 밝게
function paintConifer(T, seed) {
  const r = rng(seed), R0 = TILE * 0.45;
  T.disc(C, C + 3, R0 * 0.58, [0.36, 0.44, 0.34], 0.9);
  for (const [nb, l0, l1] of [[15, 0.80, 1.0], [11, 0.45, 0.7], [8, 0.22, 0.4]]) {
    for (let b = 0; b < nb; b++) {
      const a0 = (b / nb) * Math.PI * 2 + r() * 0.4, len = R0 * (l0 + r() * (l1 - l0));
      const droop = 0.15 + r() * 0.2, steps = 10;
      let px = C, py = C;
      for (let s = 1; s <= steps; s++) {
        const t = s / steps;
        const nx = C + Math.cos(a0) * len * t, ny = C + Math.sin(a0) * len * t - droop * len * t * t * 0.35;   // 끝으로 갈수록 처짐
        const dir = Math.atan2(ny - py, nx - px), k = 0.5 + 0.5 * t + (r() - 0.5) * 0.18;
        for (let n = 0; n < 8; n++) {   // 바늘 — 양쪽 · 앞으로 비스듬히
          const u = r(), qx = px + (nx - px) * u, qy = py + (ny - py) * u, side = n % 2 ? 1 : -1;
          const nl = (5 + r() * 4) * (1.15 - 0.3 * t);
          const na = dir + side * (0.6 + r() * 0.6);
          T.leaf(qx + Math.cos(na) * nl * 0.9, qy + Math.sin(na) * nl * 0.9, nl, 1.25, na, tint(r, Math.min(1.05, k), [0.80, 0.94, 0.78]), 1, 0.2);
        }
        px = nx; py = ny;
      }
    }
  }
}

// 소나무 바늘 다발 — 짙은 속 + 다발마다 긴 바늘이 부채꼴로(밖으로)
function paintPine(T, seed) {
  const r = rng(seed), R0 = TILE * 0.43;
  T.disc(C, C, R0 * 0.6, [0.38, 0.44, 0.31], 0.9);
  const tufts = [];
  for (let i = 0; i < 44; i++) { const a = r() * Math.PI * 2, rr = R0 * Math.pow(r(), 0.6) * 0.8; tufts.push({ x: C + Math.cos(a) * rr, y: C + Math.sin(a) * rr, a, z: r() * 0.5 + (1 - rr / R0) * 0.5, sp: 3.0 + 3.3 * Math.max(0, 1 - rr / (R0 * 0.45)) }); }
  tufts.sort((p, q) => p.z - q.z);
  for (const t of tufts) {
    const k0 = 0.55 + 0.5 * t.z;
    for (let i = 0; i < 24; i++) {
      const na = t.a + (r() - 0.5) * t.sp, nl = 9 + r() * 7;   // 가운데 다발은 사방으로(속이 비어 보이지 않게)
      T.leaf(t.x + Math.cos(na) * nl * 0.9, t.y + Math.sin(na) * nl * 0.9, nl, 1.1, na, tint(r, Math.min(1.05, k0 + (r() - 0.5) * 0.2), [0.84, 0.95, 0.72]), 1, 0.15);
    }
  }
}

// 대나무 잎 — 잔가지마다 길고 좁은 잎이 아래 · 밖으로 처짐
function paintBamboo(T, seed) {
  const r = rng(seed), R0 = TILE * 0.44;
  T.disc(C, C, R0 * 0.5, [0.45, 0.52, 0.34], 0.9);
  for (let g = 0; g < 28; g++) {
    const a = r() * Math.PI * 2, rr = R0 * (0.35 + 0.45 * r()), x = C + Math.cos(a) * rr * 0.3, y = C + Math.sin(a) * rr * 0.3;
    const ex = C + Math.cos(a) * rr, ey = C + Math.sin(a) * rr;
    for (let i = 0; i < 12; i++) {
      const u = r(), qx = x + (ex - x) * u, qy = y + (ey - y) * u;
      const na = a + (r() - 0.5) * 1.6 - 0.5 * Math.cos(a) * Math.sign(Math.sin(a) + 1e-3), nl = 13 + r() * 8;
      T.leaf(qx + Math.cos(na) * nl, qy + Math.sin(na) * nl, nl, 2.4 + r() * 1.2, na, tint(r, 0.62 + 0.42 * r(), [0.86, 0.96, 0.68]), 1, 0.3);
    }
  }
}

// 껍질 — 세로 골(잡음을 세로로 늘림) · 가로로 이어짐(줄기 한 바퀴)
function paintBark(T, seed, base, kind) {
  const P = T.px;
  for (let y = 0; y < TILE; y++) for (let x = 0; x < TILE; x++) {
    const u = x / TILE, v = y / TILE;
    let n = 0, amp = 1, f = 1, s = 0;
    for (let o = 0; o < 4; o++) { n += amp * vnoise(u * 6 * f, v * 1.5 * f, seed + o, 6 * f); s += amp; amp *= 0.5; f *= 2; }
    n /= s;
    let k;
    if (kind === 'pine') {   // 붉은 껍질 판 + 검은 틈
      const plate = vnoise(u * 10, v * 5, seed + 9, 10);
      k = 0.65 + 0.45 * n - (plate > 0.62 ? 0.35 * Math.min(1, (plate - 0.62) * 8) : 0);
    } else if (kind === 'culm') {   // 대나무 줄기 — 마디(가로 띠)
      const node = Math.abs(((v * 6) % 1) - 0.5);
      k = 0.85 + 0.2 * n - (node > 0.46 ? 0.35 : 0);
    } else if (kind === 'light') {   // 밝은 껍질(가로 검은 줄)
      const lent = vnoise(u * 3, v * 20, seed + 5, 3);
      k = 0.9 + 0.15 * n - (lent > 0.7 ? 0.5 * Math.min(1, (lent - 0.7) * 6) : 0);
    } else {
      const furrow = Math.abs(vnoise(u * 9, v * 0.8, seed + 7, 9) - 0.5) * 2;
      k = 0.55 + 0.5 * n - 0.3 * (1 - Math.min(1, furrow * 3));
    }
    const i = (y * TILE + x) * 4;
    P[i] = base[0] * k; P[i + 1] = base[1] * k; P[i + 2] = base[2] * k; P[i + 3] = 1;
  }
}

// ── 칸 → 아틀라스 단계마다 ──
// 칸 판(미리 곱함 · 실수) → 줄인 단계들 · 각 단계를 RGBA 바이트로(알파 0인 화소의 색 = 칸 평균 잎 색 · 검은 테두리 방지)
function writeTile(levels, t, P0) {
  const c0 = t % GRID, r0 = Math.floor(t / GRID);
  // 칸 평균 색(알파 가중)
  let sr = 0, sg = 0, sb = 0, sa = 0;
  for (let i = 0; i < P0.length; i += 4) { sr += P0[i]; sg += P0[i + 1]; sb += P0[i + 2]; sa += P0[i + 3]; }
  const mean = sa > 0 ? [sr / sa, sg / sa, sb / sa] : [0.5, 0.5, 0.5];
  // 0.5 넘는 화소 비율(처음 그림)
  const cov0 = (() => { let n = 0; for (let i = 3; i < P0.length; i += 4) if (P0[i] >= 0.5) n++; return n / (P0.length / 4); })();
  let P = P0, w = TILE;
  for (let L = 0; L < levels.length && w >= 1; L++) {
    if (L > 0) {   // 2 × 2 평균(미리 곱한 값이라 색이 알파로 가중됨)
      const nw = w >> 1, Q = new Float32Array(nw * nw * 4);
      for (let y = 0; y < nw; y++) for (let x = 0; x < nw; x++) {
        const o = (y * nw + x) * 4, a = ((2 * y) * w + 2 * x) * 4, b = a + 4, c = a + w * 4, d = c + 4;
        for (let k = 0; k < 4; k++) Q[o + k] = (P[a + k] + P[b + k] + P[c + k] + P[d + k]) * 0.25;
      }
      P = Q; w = nw;
    }
    // 알파 늘림 — 0.5 넘는 비율이 cov0이 되게(불투명 칸은 그대로)
    let scale = 1;
    if (cov0 > 0 && cov0 < 0.999 && w >= 2) {
      const hist = new Uint32Array(256);
      for (let i = 3; i < P.length; i += 4) hist[Math.min(255, Math.floor(P[i] * 255.999))]++;
      const want = cov0 * (P.length / 4);
      let acc = 0, q = 255;
      for (; q > 0; q--) { acc += hist[q]; if (acc >= want) break; }
      const aq = Math.max(1 / 255, (q + 0.5) / 255);
      scale = Math.max(1, Math.min(8, 0.5 / aq));
    }
    const lv = levels[L], LW = lv.width, ox = c0 * w, oy = r0 * w, D = lv.data;
    for (let y = 0; y < w; y++) for (let x = 0; x < w; x++) {
      const s = (y * w + x) * 4, a = P[s + 3], o = ((oy + y) * LW + ox + x) * 4;
      const col = a > 1e-4 ? [P[s] / a, P[s + 1] / a, P[s + 2] / a] : mean;
      D[o] = Math.round(Math.min(1, col[0]) * 255); D[o + 1] = Math.round(Math.min(1, col[1]) * 255); D[o + 2] = Math.round(Math.min(1, col[2]) * 255);
      D[o + 3] = Math.round(Math.min(1, a * scale) * 255);
    }
  }
}

// 아틀라스 만들기 → { size, levels: [{ data: Uint8Array RGBA, width, height }] }(단계 0 = 1024 … 1)
export function makeTreeAtlas() {
  const levels = [];
  for (let w = TEX_SIZE; w >= 1; w >>= 1) levels.push({ data: new Uint8Array(w * w * 4), width: w, height: w });
  const paint = [
    (T) => paintBroad(T, 11), (T) => paintBroad(T, 23), (T) => paintBroad(T, 37), (T) => paintBroad(T, 41),
    (T) => paintConifer(T, 53), (T) => paintConifer(T, 67), (T) => paintConifer(T, 71),
    (T) => paintPine(T, 83), (T) => paintPine(T, 97), (T) => paintPine(T, 101),
    (T) => paintBamboo(T, 113), (T) => paintBamboo(T, 127),
    (T) => paintBark(T, 131, [0.62, 0.54, 0.45], 'bark'), (T) => paintBark(T, 137, [0.70, 0.45, 0.32], 'pine'),
    (T) => paintBark(T, 139, [0.62, 0.70, 0.40], 'culm'), (T) => paintBark(T, 149, [0.80, 0.78, 0.72], 'light'),
  ];
  // 칸이 1 px 이 되는 단계(256 → 1: 단계 8)까지 칸마다 · 그 아래(아틀라스 2 · 1 px)는 평균
  for (let t = 0; t < paint.length; t++) {
    const T = new Tile();
    paint[t](T);
    writeTile(levels.slice(0, 9), t, T.px);
  }
  // 단계 9 · 10(2 × 2 · 1 × 1) — 위 단계 평균
  for (let L = 9; L < levels.length; L++) {
    const P = levels[L - 1], Q = levels[L], w = Q.width;
    for (let y = 0; y < w; y++) for (let x = 0; x < w; x++) for (let k = 0; k < 4; k++) {
      const a = ((2 * y) * P.width + 2 * x) * 4 + k;
      Q.data[(y * w + x) * 4 + k] = Math.round((P.data[a] + P.data[a + 4] + P.data[a + P.width * 4] + P.data[a + P.width * 4 + 4]) / 4);
    }
  }
  return { size: TEX_SIZE, levels };
}
