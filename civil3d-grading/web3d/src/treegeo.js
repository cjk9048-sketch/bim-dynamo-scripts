// ★[v115 · JACK 1105 임상도] 나무 모양 — 코드로 만든 저폴리(GLB 아님 · 순수 함수 · three 없이 시험됨).
// ★[v116 · JACK 1109 «조감도 품질»] 잎 카드 뭉치 — 수관 = 사진기를 늘 마주 보는 네모(뭉치) 20~30장(잎 · 바늘 그림 · 알파로 오림) · 줄기 · 굵은 가지 = 껍질 그림 기둥.
//   뭉치 네모는 점 4개가 같은 가운데(position)에 있고 bb(모서리 쏠림 · 수관 지름 단위)만 다르다 — 셰이더(forest.js)가 화면 오른쪽 · 위 방향으로 벌린다.
//   법선 = 수관 겉면 방향(타원체 · 원뿔 기울기) → 해 · 하늘빛으로 둥글게 · 정점색 = 모양 색 × 그늘(속 · 아래 어둡게).
//   정규화: 밑 z = 0 · 수관 지름 ≈ 1(인스턴스가 (W, W, H)로 늘린다) · 꼭대기 뭉치 가운데 + 0.65 × 뭉치 반지름 = 1.
import { TILES, tileRect } from './treetex.js';

// 색표(여름 · 선형) — 맨 위 한 곳. [수관 짙은, 수관 밝은] · 잎 그림(밝은 회녹색)에 곱해진다
export const PALETTE = {
  trunk: [0.80, 0.76, 0.72],   // 껍질 그림에 곱함(그림이 색을 가짐)
  kinds: [
    [[0.11, 0.21, 0.10], [0.20, 0.33, 0.15]],   // 0 소나무형
    [[0.18, 0.31, 0.12], [0.30, 0.45, 0.18]],   // 1 원뿔형(낙엽송 · 전나무)
    [[0.07, 0.17, 0.09], [0.13, 0.27, 0.13]],   // 2 짙은원뿔형(잣 · 편백 · 삼나무)
    [[0.20, 0.33, 0.11], [0.33, 0.48, 0.17]],   // 3 참나무형
    [[0.22, 0.37, 0.12], [0.36, 0.52, 0.18]],   // 4 달걀형
    [[0.27, 0.41, 0.14], [0.42, 0.57, 0.21]],   // 5 길쭉형(포플러 · 자작 · 아까시)
    [[0.21, 0.35, 0.11], [0.35, 0.50, 0.17]],   // 6 넓은형(밤 · 벚 · 느티)
    [[0.08, 0.21, 0.09], [0.15, 0.31, 0.13]],   // 7 상록활엽형
    [[0.25, 0.40, 0.13], [0.40, 0.56, 0.20]],   // 8 대나무
  ],
};
export const KIND_COUNT = 9;
// 먼 대리 모양 — 0 원뿔(침엽 0~2) · 1 둥근(나머지)
export const proxyOf = (kind) => (kind <= 2 ? 0 : 1);
// 대리 모양에 칠할 수관 색(인스턴스 색 — 모양마다 다름)
export const proxyColor = (kind) => { const [a, b] = PALETTE.kinds[kind] || PALETTE.kinds[4]; return [a[0] * 0.35 + b[0] * 0.65, a[1] * 0.35 + b[1] * 0.65, a[2] * 0.35 + b[2] * 0.65]; };

// 정해진 난수(모양마다 씨앗 고정 — 같은 모양 = 같은 그림)
function rng(seed) {
  let s = (seed * 2654435761) >>> 0 || 1;
  return () => { s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0; return s / 4294967296; };
}
const lerp3 = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
const norm3 = (v) => { const l = Math.hypot(v[0], v[1], v[2]) || 1; return [v[0] / l, v[1] / l, v[2] / l]; };
const clamp01 = (v) => Math.max(0, Math.min(1, v));

class Builder {
  constructor() { this.pos = []; this.nrm = []; this.uv = []; this.bb = []; this.col = []; this.top = 0; }
  vert(p, n, uv, bb, c) { this.pos.push(p[0], p[1], p[2]); this.nrm.push(n[0], n[1], n[2]); this.uv.push(uv[0], uv[1]); this.bb.push(bb[0], bb[1]); this.col.push(c[0], c[1], c[2]); }
  // 뭉치 하나 — 가운데 p · 보이는 반지름 vr(수관 지름 단위) · 그림 칸 · 화면 안 돌림 rot · 좌우 뒤집기 · 법선 n · 색 c
  card(p, vr, tile, rot, flip, n, c) {
    const h = vr / 0.88;   // 그림 속 잎 뭉치는 칸의 0.88
    const [u0, v0, u1, v1] = tileRect(tile);
    const cs = Math.cos(rot) * h, sn = Math.sin(rot) * h;
    const k = [[-1, -1], [1, -1], [1, 1], [-1, 1]].map(([x, y]) => [x * cs - y * sn, x * sn + y * cs]);
    const t = [[flip ? u1 : u0, v0], [flip ? u0 : u1, v0], [flip ? u0 : u1, v1], [flip ? u1 : u0, v1]];
    for (const i of [0, 1, 2, 0, 2, 3]) this.vert(p, n, t[i], k[i], c);
    this.top = Math.max(this.top, p[2] + 0.65 * vr);
  }
  // 기둥(줄기 · 가지) — a → b · 반지름 ra → rb · n각 · 껍질 칸 · 색
  limb(a, b, ra, rb, n, tile, c, c2 = c) {
    const d = norm3([b[0] - a[0], b[1] - a[1], b[2] - a[2]]);
    const ref = Math.abs(d[2]) > 0.9 ? [1, 0, 0] : [0, 0, 1];
    let e1 = norm3([d[1] * ref[2] - d[2] * ref[1], d[2] * ref[0] - d[0] * ref[2], d[0] * ref[1] - d[1] * ref[0]]);
    const e2 = [d[1] * e1[2] - d[2] * e1[1], d[2] * e1[0] - d[0] * e1[2], d[0] * e1[1] - d[1] * e1[0]];
    const [u0, v0, u1, v1] = tileRect(tile);
    const ring = (i) => { const an = (i / n) * Math.PI * 2; return [Math.cos(an), Math.sin(an)]; };
    for (let i = 0; i < n; i++) {
      const [ca, sa] = ring(i), [cb, sb] = ring(i + 1);
      const na = [e1[0] * ca + e2[0] * sa, e1[1] * ca + e2[1] * sa, e1[2] * ca + e2[2] * sa];
      const nb = [e1[0] * cb + e2[0] * sb, e1[1] * cb + e2[1] * sb, e1[2] * cb + e2[2] * sb];
      const P = (cen, nn, r) => [cen[0] + nn[0] * r, cen[1] + nn[1] * r, cen[2] <= 0 ? 0 : cen[2] + nn[2] * r];   // 땅에 닿는 고리는 z = 0(기운 줄기도 땅 밑으로 안 감)
      const ua = u0 + (u1 - u0) * (i / n), ub = u0 + (u1 - u0) * ((i + 1) / n);
      const A = P(a, na, ra), B = P(a, nb, ra), Cc = P(b, nb, rb), D = P(b, na, rb);
      const z = [0, 0];
      this.vert(A, na, [ua, v0], z, c); this.vert(B, nb, [ub, v0], z, c); this.vert(Cc, nb, [ub, v1], z, c2);
      this.vert(A, na, [ua, v0], z, c); this.vert(Cc, nb, [ub, v1], z, c2); this.vert(D, na, [ua, v1], z, c2);
    }
    this.top = Math.max(this.top, a[2], b[2]);
  }
  // 키 1로 맞춤(z만 나눔 — 밑 0은 그대로)
  done() {
    const k = this.top > 0 ? 1 / this.top : 1;
    const pos = new Float32Array(this.pos);
    for (let i = 2; i < pos.length; i += 3) pos[i] *= k;
    // 법선은 z를 늘린 만큼 거꾸로(기울기 보존)
    const nrm = new Float32Array(this.nrm);
    for (let i = 0; i < nrm.length; i += 3) { nrm[i + 2] /= k; const l = Math.hypot(nrm[i], nrm[i + 1], nrm[i + 2]) || 1; nrm[i] /= l; nrm[i + 1] /= l; nrm[i + 2] /= l; }
    let bbMax = 0;
    for (let i = 0; i < this.bb.length; i += 2) bbMax = Math.max(bbMax, Math.hypot(this.bb[i], this.bb[i + 1]));
    return { pos, nrm, uv: new Float32Array(this.uv), bb: new Float32Array(this.bb), col: new Float32Array(this.col), tris: this.pos.length / 9, bbMax };
  }
}

const pick = (r, list) => list[Math.floor(r() * list.length) % list.length];

// 수관 색 — 높이 h(0 아래 ~ 1 위) · 속 깊이 e(0 가운데 ~ 1 겉) → 짙은 ↔ 밝은 × 그늘
function crownColor(dk, lt, h, e, r) {
  const ao = (0.58 + 0.42 * clamp01((e - 0.15) / 0.8)) * (0.80 + 0.20 * clamp01(h));
  const c = lerp3(dk, lt, clamp01(0.25 + 0.75 * h * e + (r() - 0.5) * 0.25));
  return [clamp01(c[0] * ao), clamp01(c[1] * ao), clamp01(c[2] * ao)];
}

// 활엽 수관 — 타원체(가운데 c · 반지름 R) 겉면 근처 n장 + 속 몇 장 · 겉 울퉁불퉁(낮은 주파수 혹)
function crownBlob(B, r, c, R, n, vr, tiles, dk, lt) {
  const lumps = [r() * 6.28, r() * 6.28, r() * 6.28];
  const ga = Math.PI * (3 - Math.sqrt(5));
  for (let i = 0; i < n; i++) {
    const inner = i < Math.round(n * 0.18);
    // 피보나치 구(고르게) + 흔들림 · 아래쪽은 조금 덜(밑에서 올려다볼 일은 적다)
    const t = (i + 0.5) / n, zc = 1 - 2 * Math.pow(t, 0.85), rr = Math.sqrt(Math.max(0, 1 - zc * zc)), an = i * ga + r() * 0.6;
    let d = [Math.cos(an) * rr, Math.sin(an) * rr, zc];
    d = norm3([d[0] + (r() - 0.5) * 0.25, d[1] + (r() - 0.5) * 0.25, d[2] + (r() - 0.5) * 0.25]);
    const lump = 1 + 0.10 * Math.sin(3 * Math.atan2(d[1], d[0]) + lumps[0]) * Math.cos(2 * d[2] + lumps[1]) + 0.06 * Math.sin(5 * d[2] + lumps[2]);
    const f = inner ? 0.25 + 0.3 * r() : (0.62 + 0.22 * r()) * lump;
    const p = [c[0] + d[0] * R[0] * f, c[1] + d[1] * R[1] * f, c[2] + d[2] * R[2] * f];
    const nn = norm3([(p[0] - c[0]) / (R[0] * R[0]), (p[1] - c[1]) / (R[1] * R[1]), (p[2] - c[2]) / (R[2] * R[2])]);
    const h = clamp01((p[2] - (c[2] - R[2])) / (2 * R[2]));
    const s = inner ? vr[1] * 1.15 : vr[0] + (vr[1] - vr[0]) * r();
    B.card(p, s, pick(r, tiles), r() * Math.PI * 2, r() < 0.5, nn, crownColor(dk, lt, h, inner ? 0.2 : f, r));
  }
}

// 침엽 수관 — 원뿔(z0 밑 반지름 r0 → z1 꼭대기) 층마다 둘레 뭉치 + 가운데 뭉치 · 위로 갈수록 작게
function crownCone(B, r, z0, z1, r0, tiers, tiles, dk, lt, ringMax = 5) {
  const dz = z1 - z0, slope = r0 / dz;
  for (let i = 0; i < tiers; i++) {
    const t = i / (tiers - 1), z = z0 + dz * Math.pow(t, 0.92), rr = r0 * Math.pow(1 - t, 0.95) + 0.025;
    const m = Math.max(1, Math.round(ringMax * (1 - t) + 0.4)), off = r() * 6.28;
    if (i < tiers - 1) {
      for (let j = 0; j < m; j++) {
        const an = off + (j / m) * Math.PI * 2 + (r() - 0.5) * 0.5, rad = rr * (0.52 + 0.12 * r());
        const p = [Math.cos(an) * rad, Math.sin(an) * rad, z + (r() - 0.5) * dz * 0.04];
        B.card(p, rr * (0.55 + 0.12 * r()) + 0.02, pick(r, tiles), r() * 6.28, r() < 0.5, norm3([Math.cos(an), Math.sin(an), slope]), crownColor(dk, lt, t, 0.75 + 0.25 * r(), r));
      }
    }
    // 가운데(속 · 틈 메움) — 법선은 바깥 + 위
    const an = r() * 6.28;
    B.card([0, 0, z + dz * 0.03], i === tiers - 1 ? Math.max(0.05, rr * 0.9) : rr * 0.62, pick(r, tiles), r() * 6.28, r() < 0.5, norm3([Math.cos(an) * 0.4, Math.sin(an) * 0.4, 1]), crownColor(dk, lt, t, i === tiers - 1 ? 1 : 0.35, r));
  }
}

const TRUNK = (k = 1) => [PALETTE.trunk[0] * k, PALETTE.trunk[1] * k, PALETTE.trunk[2] * k];

// 자세한 모양(LOD0) — kind 0~8
export function treeGeo(kind) {
  const B = new Builder(), r = rng(1000 + kind * 7919);
  const [dk, lt] = PALETTE.kinds[kind] || PALETTE.kinds[4];
  const T = TILES;
  switch (kind) {
    case 0: { // 소나무형 — 붉은 줄기 높이 · 굵은 가지 셋 · 위에 납작한 바늘 구름 셋(우산 꼴)
      B.limb([0, 0, 0], [0.02, -0.01, 0.80], 0.036, 0.022, 5, T.barkPine, TRUNK(0.75), TRUNK(1.0));
      B.limb([0.01, 0, 0.62], [0.18, -0.12, 0.74], 0.016, 0.009, 3, T.barkPine, TRUNK(0.9));
      B.limb([0.01, 0, 0.58], [-0.20, 0.14, 0.70], 0.016, 0.009, 3, T.barkPine, TRUNK(0.9));
      crownBlob(B, r, [0.02, -0.01, 0.86], [0.40, 0.38, 0.11], 11, [0.16, 0.22], T.pine, dk, lt);
      crownBlob(B, r, [0.18, -0.12, 0.75], [0.27, 0.25, 0.08], 6, [0.14, 0.19], T.pine, dk, lt);
      crownBlob(B, r, [-0.20, 0.14, 0.71], [0.26, 0.24, 0.08], 6, [0.14, 0.19], T.pine, dk, lt);
      break;
    }
    case 1: // 원뿔형(낙엽송 · 전나무) — 줄기 조금 보이고 원뿔 층 일곱
      B.limb([0, 0, 0], [0, 0, 0.55], 0.04, 0.02, 6, T.bark, TRUNK(0.8));
      crownCone(B, r, 0.14, 0.97, 0.50, 7, T.conifer, dk, lt);
      break;
    case 2: // 짙은원뿔형 — 좁고 높은 원뿔 층 여덟
      B.limb([0, 0, 0], [0, 0, 0.4], 0.04, 0.025, 6, T.bark, TRUNK(0.7));
      crownCone(B, r, 0.08, 0.98, 0.42, 8, T.conifer, dk, lt, 4);
      break;
    case 3: // 참나무형 — 줄기 + 굵은 가지 둘 · 울퉁불퉁 넓은 덩이
      B.limb([0, 0, 0], [0.01, 0, 0.50], 0.036, 0.026, 6, T.bark, TRUNK(0.8));
      B.limb([0.01, 0, 0.46], [0.20, 0.08, 0.66], 0.020, 0.010, 3, T.bark, TRUNK(0.85));
      B.limb([0.01, 0, 0.44], [-0.17, -0.10, 0.64], 0.020, 0.010, 3, T.bark, TRUNK(0.85));
      crownBlob(B, r, [0, 0, 0.70], [0.50, 0.48, 0.29], 28, [0.17, 0.23], T.broad, dk, lt);
      break;
    case 4: // 달걀형 — 위로 긴 덩이
      B.limb([0, 0, 0], [0, 0, 0.42], 0.034, 0.024, 6, T.bark, TRUNK(0.8));
      B.limb([0, 0, 0.38], [0.12, -0.06, 0.58], 0.016, 0.008, 3, T.bark, TRUNK(0.85));
      crownBlob(B, r, [0, 0, 0.64], [0.46, 0.46, 0.36], 28, [0.16, 0.22], T.broad, dk, lt);
      break;
    case 5: // 길쭉형(포플러 · 자작 · 아까시) — 밝은 껍질 · 좁고 긴 덩이
      B.limb([0, 0, 0], [0, 0, 0.55], 0.034, 0.022, 6, T.barkLight, TRUNK(0.95));
      crownBlob(B, r, [0, 0, 0.62], [0.32, 0.32, 0.40], 26, [0.12, 0.17], T.broad, dk, lt);
      break;
    case 6: // 넓은형(밤 · 벚 · 느티) — 갈라진 굵은 가지 셋 · 낮고 넓은 덩이
      B.limb([0, 0, 0], [0, 0, 0.42], 0.038, 0.028, 6, T.bark, TRUNK(0.8));
      B.limb([0, 0, 0.40], [0.24, 0.04, 0.66], 0.020, 0.010, 3, T.bark, TRUNK(0.85));
      B.limb([0, 0, 0.40], [-0.20, 0.14, 0.66], 0.020, 0.010, 3, T.bark, TRUNK(0.85));
      crownBlob(B, r, [0, 0, 0.74], [0.52, 0.50, 0.25], 28, [0.16, 0.22], T.broad, dk, lt);
      break;
    case 7: // 상록활엽형 — 빽빽한 둥근 덩이(짧은 줄기)
      B.limb([0, 0, 0], [0, 0, 0.32], 0.034, 0.026, 6, T.bark, TRUNK(0.7));
      crownBlob(B, r, [0, 0, 0.62], [0.48, 0.48, 0.36], 30, [0.18, 0.24], T.broad, dk, lt);
      break;
    case 8: { // 대나무 — 가는 줄기 다섯(마디 그림) · 위쪽 따라 잎 뭉치
      // 수관 폭 W(1.5 m)가 좁아 잎 뭉치는 W보다 크게(대숲은 위가 빽빽이 덮임) — 줄기 끝은 바깥으로 휘고 잎은 위 45 %에 몰림
      const cul = [[0, 0, 1.0], [0.20, 0.06, 0.92], [-0.15, 0.16, 0.95], [0.05, -0.20, 0.88], [-0.18, -0.11, 0.90]];
      for (const [x, y, h] of cul) {
        const tip = [x * 2.2, y * 2.2, h * 0.9];
        B.limb([x * 0.3, y * 0.3, 0], tip, 0.03, 0.016, 4, T.culm, [0.85, 0.9, 0.8]);
        for (let j = 0; j < 3; j++) {
          const t = 0.58 + 0.17 * j, p = [x * 0.3 + (tip[0] - x * 0.3) * t, y * 0.3 + (tip[1] - y * 0.3) * t, tip[2] * t + 0.05];
          B.card(p, 0.42 - 0.07 * j, pick(r, T.bamboo), r() * 6.28, r() < 0.5, norm3([x + 0.01, y + 0.01, 0.7]), crownColor(dk, lt, t, 0.85, r));
        }
      }
      break;
    }
    default:
      B.limb([0, 0, 0], [0, 0, 0.42], 0.034, 0.024, 6, T.bark, TRUNK(0.8));
      crownBlob(B, r, [0, 0, 0.64], [0.46, 0.46, 0.36], 28, [0.16, 0.22], T.broad, dk, lt);
  }
  return B.done();
}

// 먼 대리 모양(LOD1) — 같은 잎 그림 뭉치 4~5장(8~10 세모 · 줄기 없음) · 색은 회색 그늘(인스턴스 색이 모양 색을 준다)
export function proxyGeo(p) {
  const B = new Builder(), r = rng(77 + p);
  const g = (k) => [k, k, k];
  if (p === 0) {
    const tiers = [[0.30, 0.36], [0.52, 0.28], [0.72, 0.19], [0.88, 0.11]];
    tiers.forEach(([z, vr], i) => B.card([0, 0, z], vr, TILES.conifer[i % 3], r() * 6.28, i % 2 === 1, norm3([0.3, 0.2, 1]), g(0.78 + 0.07 * i)));
  } else {
    B.card([0, 0, 0.60], 0.40, TILES.broad[0], r() * 6.28, false, [0, 0, 1], g(0.86));
    for (let i = 0; i < 4; i++) {
      const an = (i / 4) * Math.PI * 2 + 0.4, d = [Math.cos(an), Math.sin(an)];
      B.card([d[0] * 0.2, d[1] * 0.2, 0.52 + 0.08 * (i % 2)], 0.28, TILES.broad[(i + 1) % 4], r() * 6.28, i % 2 === 0, norm3([d[0], d[1], 0.8]), g(0.80 + 0.06 * (i % 2)));
    }
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
