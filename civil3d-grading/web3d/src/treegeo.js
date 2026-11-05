// ★[v115 · JACK 1105 임상도] 나무 모양 — 코드로 만든 저폴리(GLB 아님 · 순수 함수 · three 없이 시험됨).
//   모양 9 × 자세히(LOD0 · 60~160 세모 · 줄기 6각 기둥 + 수관 겹친 정이십면체/원뿔 단) + 먼 대리 모양 둘(LOD1 · 원뿔 5면 / 늘인 팔면체 · 8 세모).
//   정규화: 키 1 · 밑 z = 0 · 수관 지름 ≈ 1(인스턴스가 (W, W, H)로 늘린다) · 면마다 따로 점(평평한 칠) · 정점색.

// 색표(여름) — 맨 위 한 곳. [줄기, 수관 짙은, 수관 밝은]
export const PALETTE = {
  trunk: [0.32, 0.24, 0.17],
  bamboo: [0.55, 0.62, 0.30],
  kinds: [
    [[0.13, 0.25, 0.12], [0.20, 0.34, 0.16]],   // 0 소나무형
    [[0.22, 0.36, 0.14], [0.32, 0.47, 0.20]],   // 1 원뿔형(낙엽송 · 전나무)
    [[0.09, 0.21, 0.11], [0.14, 0.29, 0.14]],   // 2 짙은원뿔형(잣 · 편백 · 삼나무)
    [[0.22, 0.38, 0.13], [0.33, 0.50, 0.18]],   // 3 참나무형
    [[0.25, 0.42, 0.15], [0.36, 0.54, 0.20]],   // 4 달걀형
    [[0.30, 0.46, 0.17], [0.42, 0.58, 0.23]],   // 5 길쭉형(포플러 · 자작 · 아까시)
    [[0.24, 0.40, 0.14], [0.35, 0.52, 0.19]],   // 6 넓은형(밤 · 벚 · 느티)
    [[0.10, 0.26, 0.12], [0.16, 0.34, 0.15]],   // 7 상록활엽형
    [[0.30, 0.46, 0.16], [0.42, 0.58, 0.22]],   // 8 대나무
  ],
};
export const KIND_COUNT = 9;
// 먼 대리 모양 — 0 원뿔(침엽 0~2) · 1 늘인 팔면체(나머지)
export const proxyOf = (kind) => (kind <= 2 ? 0 : 1);
// 대리 모양에 칠할 수관 색(인스턴스 색 — 모양마다 다름)
export const proxyColor = (kind) => { const [a, b] = PALETTE.kinds[kind] || PALETTE.kinds[4]; return [a[0] * 0.3 + b[0] * 0.7, a[1] * 0.3 + b[1] * 0.7, a[2] * 0.3 + b[2] * 0.7]; };

class Builder {
  constructor() { this.pos = []; this.col = []; }
  tri(a, b, c, ca, cb = ca, cc = ca) {
    this.pos.push(a[0], a[1], a[2], b[0], b[1], b[2], c[0], c[1], c[2]);
    this.col.push(...ca, ...cb, ...cc);
  }
  // 키 1로 맞춤(덩이 흔들기로 꼭대기가 1을 조금 넘을 수 있어 z만 나눔 — 밑 0은 그대로)
  done() {
    let zMax = 0;
    for (let i = 2; i < this.pos.length; i += 3) zMax = Math.max(zMax, this.pos[i]);
    const pos = new Float32Array(this.pos);
    if (zMax > 0) for (let i = 2; i < pos.length; i += 3) pos[i] /= zMax;
    return { pos, col: new Float32Array(this.col), tris: this.pos.length / 9 };
  }
}

const lerp3 = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];

// 줄기 — n각 기둥(옆면만 · 2n 세모)
function trunk(B, r, z0, z1, n = 6, color = PALETTE.trunk, rot = 0) {
  for (let i = 0; i < n; i++) {
    const a0 = rot + (i / n) * Math.PI * 2, a1 = rot + ((i + 1) / n) * Math.PI * 2;
    const p0 = [Math.cos(a0) * r, Math.sin(a0) * r], p1 = [Math.cos(a1) * r, Math.sin(a1) * r];
    const dark = color.map(v => v * 0.8);
    B.tri([p0[0], p0[1], z0], [p1[0], p1[1], z0], [p1[0], p1[1], z1], dark, dark, color);
    B.tri([p0[0], p0[1], z0], [p1[0], p1[1], z1], [p0[0], p0[1], z1], dark, color, color);
  }
}

// 정이십면체(단위 구) — detail 1이면 한 번 나눠 80 세모
function icosa(detail = 0) {
  const t = (1 + Math.sqrt(5)) / 2;
  let v = [[-1, t, 0], [1, t, 0], [-1, -t, 0], [1, -t, 0], [0, -1, t], [0, 1, t], [0, -1, -t], [0, 1, -t], [t, 0, -1], [t, 0, 1], [-t, 0, -1], [-t, 0, 1]];
  let f = [[0, 11, 5], [0, 5, 1], [0, 1, 7], [0, 7, 10], [0, 10, 11], [1, 5, 9], [5, 11, 4], [11, 10, 2], [10, 7, 6], [7, 1, 8],
    [3, 9, 4], [3, 4, 2], [3, 2, 6], [3, 6, 8], [3, 8, 9], [4, 9, 5], [2, 4, 11], [6, 2, 10], [8, 6, 7], [9, 8, 1]];
  const norm = (p) => { const l = Math.hypot(p[0], p[1], p[2]); return [p[0] / l, p[1] / l, p[2] / l]; };
  v = v.map(norm);
  for (let d = 0; d < detail; d++) {
    const nf = [], mid = new Map();
    const m = (a, b) => {
      const k = a < b ? `${a}_${b}` : `${b}_${a}`;
      if (!mid.has(k)) { v.push(norm([(v[a][0] + v[b][0]) / 2, (v[a][1] + v[b][1]) / 2, (v[a][2] + v[b][2]) / 2])); mid.set(k, v.length - 1); }
      return mid.get(k);
    };
    for (const [a, b, c] of f) { const ab = m(a, b), bc = m(b, c), ca = m(c, a); nf.push([a, ab, ca], [b, bc, ab], [c, ca, bc], [ab, bc, ca]); }
    f = nf;
  }
  return { v, f };
}

// 덩이(타원체 근사) — 가운데 (cx, cy, cz) · 반지름 (rx, ry, rz) · 아래는 짙게 · 위는 밝게 · 꼭짓점을 해시로 조금 흔듦(덜 매끈하게)
function blob(B, cx, cy, cz, rx, ry, rz, dark, light, detail = 0, seed = 1) {
  const { v, f } = icosa(detail);
  const jit = (i) => { const s = Math.sin((i + 1) * 12.9898 * seed) * 43758.5453; return 1 + 0.12 * ((s - Math.floor(s)) - 0.5); };
  const P = v.map((p, i) => { const j = jit(i); return [cx + p[0] * rx * j, cy + p[1] * ry * j, cz + p[2] * rz * j]; });
  const C = v.map(p => lerp3(dark, light, Math.min(1, Math.max(0, (p[2] + 1) / 2 * 0.9 + 0.1))));
  for (const [a, b, c] of f) B.tri(P[a], P[b], P[c], C[a], C[b], C[c]);
}

// 원뿔 단 — 밑 원(n각) z0 · 꼭대기 z1 · 밑면 포함(아래에서 올려다볼 때 구멍 없게)
function cone(B, r, z0, z1, n, dark, light, rot = 0) {
  const top = [0, 0, z1];
  for (let i = 0; i < n; i++) {
    const a0 = rot + (i / n) * Math.PI * 2, a1 = rot + ((i + 1) / n) * Math.PI * 2;
    const p0 = [Math.cos(a0) * r, Math.sin(a0) * r, z0], p1 = [Math.cos(a1) * r, Math.sin(a1) * r, z0];
    B.tri(p0, p1, top, dark, dark, light);
    B.tri(p1, p0, [0, 0, z0], dark, dark, dark);
  }
}

// 자세한 모양(LOD0) — kind 0~8
export function treeGeo(kind) {
  const B = new Builder();
  const [dk, lt] = PALETTE.kinds[kind] || PALETTE.kinds[4];
  switch (kind) {
    case 0: // 소나무형 — 줄기 높이 · 위에 납작한 덩이 셋(우산 꼴)
      trunk(B, 0.034, 0, 0.78);
      blob(B, 0.0, 0.0, 0.86, 0.42, 0.40, 0.14, dk, lt, 0, 1);
      blob(B, 0.16, -0.10, 0.74, 0.30, 0.28, 0.11, dk, lt, 0, 2);
      blob(B, -0.18, 0.12, 0.70, 0.28, 0.26, 0.10, dk, lt, 0, 3);
      break;
    case 1: // 원뿔형 — 원뿔 단 셋(8각)
      trunk(B, 0.04, 0, 0.3);
      cone(B, 0.50, 0.12, 0.58, 8, dk, lt, 0.2);
      cone(B, 0.40, 0.38, 0.82, 8, dk, lt, 0.6);
      cone(B, 0.28, 0.62, 1.00, 8, dk, lt, 1.0);
      break;
    case 2: // 짙은원뿔형 — 좁고 높은 원뿔 단 넷
      trunk(B, 0.04, 0, 0.2);
      cone(B, 0.42, 0.08, 0.46, 8, dk, lt, 0.1);
      cone(B, 0.36, 0.30, 0.66, 8, dk, lt, 0.5);
      cone(B, 0.29, 0.50, 0.84, 8, dk, lt, 0.9);
      cone(B, 0.20, 0.70, 1.00, 8, dk, lt, 1.3);
      break;
    case 3: // 참나무형 — 둥근 덩이 넷
      trunk(B, 0.034, 0, 0.45);
      blob(B, 0.0, 0.0, 0.72, 0.42, 0.42, 0.27, dk, lt, 0, 4);
      blob(B, 0.20, 0.10, 0.62, 0.28, 0.28, 0.20, dk, lt, 0, 5);
      blob(B, -0.18, 0.14, 0.64, 0.28, 0.26, 0.20, dk, lt, 0, 6);
      blob(B, 0.02, -0.20, 0.60, 0.27, 0.26, 0.19, dk, lt, 0, 7);
      break;
    case 4: // 달걀형 — 위로 긴 덩이 하나(나눔 1)
      trunk(B, 0.032, 0, 0.36);
      blob(B, 0.0, 0.0, 0.66, 0.46, 0.46, 0.34, dk, lt, 1, 8);
      break;
    case 5: // 길쭉형 — 좁고 긴 덩이(나눔 1)
      trunk(B, 0.036, 0, 0.26);
      blob(B, 0.0, 0.0, 0.62, 0.32, 0.32, 0.38, dk, lt, 1, 9);
      break;
    case 6: // 넓은형 — 낮고 넓은 덩이 셋 + 위 하나
      trunk(B, 0.034, 0, 0.42);
      blob(B, 0.0, 0.0, 0.74, 0.46, 0.44, 0.24, dk, lt, 0, 10);
      blob(B, 0.24, 0.0, 0.62, 0.26, 0.30, 0.18, dk, lt, 0, 11);
      blob(B, -0.24, 0.02, 0.62, 0.26, 0.30, 0.18, dk, lt, 0, 12);
      blob(B, 0.0, 0.0, 0.88, 0.26, 0.26, 0.12, dk, lt, 0, 13);
      break;
    case 7: // 상록활엽형 — 빽빽한 둥근 덩이(나눔 1) + 아래 덩이
      trunk(B, 0.032, 0, 0.3);
      blob(B, 0.0, 0.0, 0.66, 0.48, 0.48, 0.34, dk, lt, 1, 14);
      blob(B, 0.0, 0.0, 0.40, 0.34, 0.34, 0.12, dk, dk, 0, 15);
      break;
    case 8: { // 대나무 — 가는 줄기 다섯(4각) + 끝 잎 뭉치(팔면체)
      const cul = [[0, 0, 1.0], [0.18, 0.06, 0.92], [-0.14, 0.14, 0.95], [0.04, -0.18, 0.88], [-0.16, -0.10, 0.90]];
      for (const [x, y, h] of cul) {
        const r = 0.025;
        for (let i = 0; i < 4; i++) {
          const a0 = (i / 4) * Math.PI * 2 + 0.4, a1 = ((i + 1) / 4) * Math.PI * 2 + 0.4;
          const p0 = [x + Math.cos(a0) * r, y + Math.sin(a0) * r], p1 = [x + Math.cos(a1) * r, y + Math.sin(a1) * r];
          B.tri([p0[0], p0[1], 0], [p1[0], p1[1], 0], [p1[0] + x * 0.15, p1[1] + y * 0.15, h * 0.8], PALETTE.bamboo);
          B.tri([p0[0], p0[1], 0], [p1[0] + x * 0.15, p1[1] + y * 0.15, h * 0.8], [p0[0] + x * 0.15, p0[1] + y * 0.15, h * 0.8], PALETTE.bamboo);
        }
        const tx = x * 1.4, ty = y * 1.4, tz = h * 0.82, s = 0.2;
        const o = [[tx + s, ty, tz], [tx, ty + s, tz], [tx - s, ty, tz], [tx, ty - s, tz]], up = [tx, ty, h], dn = [tx, ty, tz - s * 0.8];
        for (let i = 0; i < 4; i++) { B.tri(o[i], o[(i + 1) % 4], up, dk, dk, lt); B.tri(o[(i + 1) % 4], o[i], dn, dk, dk, dk); }
      }
      break;
    }
    default:
      trunk(B, 0.045, 0, 0.36);
      blob(B, 0, 0, 0.66, 0.46, 0.46, 0.34, dk, lt, 1, 8);
  }
  return B.done();
}

// 먼 대리 모양(LOD1) — 0 원뿔 5면(옆 5 + 밑 3 = 8 세모) · 1 늘인 팔면체(8 세모) · 색은 흰 → 회색 음영(인스턴스 색이 모양 색을 준다)
export function proxyGeo(p) {
  const B = new Builder();
  const hi = [1.05, 1.05, 1.05], lo = [0.78, 0.78, 0.78];   // 자세한 모양(줄기 · 사이 땅이 보여 밝아 보임)과 멀리서 밝기가 비슷하게
  if (p === 0) {
    const n = 5, r = 0.45, z0 = 0.08, top = [0, 0, 1];
    const ring = [];
    for (let i = 0; i < n; i++) { const a = (i / n) * Math.PI * 2; ring.push([Math.cos(a) * r, Math.sin(a) * r, z0]); }
    for (let i = 0; i < n; i++) B.tri(ring[i], ring[(i + 1) % n], top, lo, lo, hi);
    B.tri(ring[0], ring[2], ring[1], lo); B.tri(ring[0], ring[3], ring[2], lo); B.tri(ring[0], ring[4], ring[3], lo);
  } else {
    const r = 0.48, zc = 0.62, top = [0, 0, 1], bot = [0, 0, 0.2];
    const o = [[r, 0, zc], [0, r, zc], [-r, 0, zc], [0, -r, zc]];
    for (let i = 0; i < 4; i++) { B.tri(o[i], o[(i + 1) % 4], top, lo, lo, hi); B.tri(o[(i + 1) % 4], o[i], bot, lo, lo, lo); }
  }
  return B.done();
}

// ── LOD 고르기(순수 · 시험됨) ──
// chunks: [{ d(사진기까지 m), tris0(자세히 세모 수) }] → 상태 배열 'lod0' | 'lod1' | 'hidden'
//   가까운 덩이부터 세모 예산까지 자세히(예산 ±10 % — 넘는 첫 덩이가 예산의 10 % 안이면 넣음) · 나머지는 대리 · 숨김 거리 밖은 숨김
//   lod0Max(m) 밖은 예산이 남아도 대리 — 멀리서는 자세한 모양이 몇 화소라 세모만 든다(1105 그림 시험: 4 km 보기 세모 4.7백만 → 대리만)
export function pickLod(chunks, budget, hideDist, lod0Max = Infinity) {
  const order = chunks.map((c, i) => i).sort((a, b) => chunks[a].d - chunks[b].d || a - b);
  const st = new Array(chunks.length).fill('hidden');
  let used = 0, full = false;
  for (const i of order) {
    const c = chunks[i];
    if (c.d > hideDist) { st[i] = 'hidden'; continue; }
    if (!full && c.d <= lod0Max && used + c.tris0 <= budget * 1.1) { st[i] = 'lod0'; used += c.tris0; if (used >= budget) full = true; }
    else { full = true; st[i] = 'lod1'; }
  }
  return st;
}

export const hideDistance = (targetDist) => Math.max(3000, 1.5 * targetDist);

// 밀도 막대 f(0.25~1) → 무리마다 남길 수(해시 순위 차례라 앞에서부터)
export const keepCount = (n, f) => Math.min(n, Math.ceil(Math.max(0, Math.min(1, f)) * n));
