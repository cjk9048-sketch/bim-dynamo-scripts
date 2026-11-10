// 바닥 질감 시험 페이지 — 가짜 현장(계획면 · 절토 사면 + 소단 · 성토 사면 + 소단 · 진입도로[아스팔트 · 길어깨 · 측구 · 흰 차선] ·
//   콘크리트 옹벽 하나 · 둘레 숲 · 가짜 위성)을 C#와 같은 «DH3D» 묶음으로 싸서 page.html + 뷰어에 넣는다. Civil 없이 아스팔트 · 잔디 · 콘크리트 · 흙 · 쇄석을
//   가까이(5~10 m) · 중간(50 m) · 하늘(300 m)에서 눈으로 견주려고(tools/shot.mjs로 찍음). 좌표 · 이름은 모두 지어낸 것.
//   node test/ground-page.mjs <나갈 html> [viewer.js(기본 dist/viewer.js)]
//   (이름에 .test 없음 — npm test가 돌리지 않는다)
//
// 모양 — 원지반 = 북쪽으로 20 % 오르는 기운 평면(현장 둘레) + 멀리 물결 · 계획면 120 × 80 m(EL 61) ·
//   사면은 체비쇼프 거리(네모 둘레 같은 거리 = 모서리에 곧은 능선)로 절토 1:1 · 성토 1:1.5 · 5 m마다 소단 2 m ·
//   진입도로 = 계획면 서쪽 끝에서 서쪽으로 240 m(4.6 % 내림) · 아스팔트 6 m · 길어깨 0.5 m(북쪽은 측구 자리까지 1.3 m) · 비탈 절토 1:1 · 성토 1:1.5.
//   땅 높이 = min(max(원지반, 성토면들), 절토면들) — 조각마다 평면이라 칸을 조각 경계(두 평면이 만나는 곧은 선)로 잘라 경계가 톱니 없이 곧다.
import { readFileSync, writeFileSync } from 'node:fs';
import { gzipSync, deflateSync } from 'node:zlib';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const [, , out, viewerArg] = process.argv;
if (!out) { console.error('쓰는 법: node test/ground-page.mjs <out.html> [viewer.js]'); process.exit(2); }
const viewerPath = viewerArg || join(root, 'dist', 'viewer.js');

// ── 정해진 난수 · 값 잡음 ──
const hash2 = (i, j, s) => { let h = (Math.imul(i, 374761393) + Math.imul(j, 668265263) + Math.imul(s, 1442695041)) >>> 0; h = Math.imul(h ^ (h >>> 13), 1274126177) >>> 0; return ((h ^ (h >>> 16)) >>> 0) / 4294967296; };
function vnoise(x, y, s) {
  const x0 = Math.floor(x), y0 = Math.floor(y), fx = x - x0, fy = y - y0, u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy);
  const a = hash2(x0, y0, s), b = hash2(x0 + 1, y0, s), c = hash2(x0, y0 + 1, s), d = hash2(x0 + 1, y0 + 1, s);
  return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
}
const fbm = (x, y, s, oct = 4) => { let t = 0, a = 0.5, f = 1, n = 0; for (let k = 0; k < oct; k++) { t += a * vnoise(x * f, y * f, s + k); n += a; a *= 0.5; f *= 2.03; } return t / n; };

// ── 원지반 ──
const HALF = 450;
const G0 = 60, GX = 0.02, GY = 0.2;
const SITE = [-330, -90, 100, 80];           // 원지반이 평면인 자리(이 밖 60 m에서 물결이 서서히)
const distSite = (x, y) => Math.hypot(Math.max(0, SITE[0] - x, x - SITE[2]), Math.max(0, SITE[1] - y, y - SITE[3]));
const smooth01 = (a, b, t) => { const k = Math.max(0, Math.min(1, (t - a) / (b - a))); return k * k * (3 - 2 * k); };
const wave = (x, y) => 9 * Math.sin(x / 130 + 0.7) * Math.cos(y / 170 - 0.3) + 5 * Math.sin((x + 0.7 * y) / 75) + 2 * Math.cos((x - y) / 31);
const groundZ = (x, y) => G0 + GX * x + GY * y + wave(x, y) * smooth01(0, 60, distSite(x, y));

// ── 설계 ──
const PZ = 61, PX = 60, PY = 40;                       // 계획면 반 폭
const R0 = -300, R1 = -60, RS = (PZ - 50) / (R1 - R0);   // 진입도로 x 범위 · 기울기(EL 61 → 50 · 성토 → 비탈길 → 끝은 절토)
const AS = 3.0, WS = 3.5, WN = 4.3;                    // 아스팔트 반 폭 · 남쪽 길어깨 끝 · 북쪽(측구 자리까지) 끝
const DITCH = [3.5, 4.3];                              // 북쪽 측구(U형 그레이팅) 자리
// 사면 단면(거리 d → 높이 차 · 기울기 · 조각 번호) — 높이 5 m마다 소단 2 m
function cutProf(d, bench) {
  if (!bench || d <= 5) return [d, 1, 0];
  if (d <= 7) return [5, 0, 1];
  return [5 + (d - 7), 1, 2];
}
function fillProf(d, bench) {
  if (!bench || d <= 7.5) return [d / 1.5, 1 / 1.5, 0];
  if (d <= 9.5) return [5, 0, 1];
  return [5 + (d - 9.5) / 1.5, 1 / 1.5, 2];
}
// 네모 둘레 체비쇼프 거리 + 그 기울기(어느 축이 이겼는지)
function cheb(terms) {
  let best = 0, gx = 0, gy = 0, tag = '0';
  for (const [v, ax, ay, t] of terms) if (v > best) { best = v; gx = ax; gy = ay; tag = t; }
  return { d: best, gx, gy, tag };
}
// 설계 면들 — 각각 { z, a, b(평면 기울기), id, cls }
// 면 하나 — 바탕 높이 base(기울기 ba, bb) · 거리 D(cheb) · 단면 prof · 절토 +1 / 성토 −1
function face(base, ba, bb, D, prof, sign, idp, cTop, cSlope) {
  if (D.d <= 0) return { z: base, a: ba, b: bb, id: idp + 't', cls: cTop };
  const [dz, k, seg] = prof;
  return { z: base + sign * dz, a: ba + sign * k * D.gx, b: bb + sign * k * D.gy, id: idp + D.tag + 's' + seg, cls: seg === 1 ? 'bench' : cSlope };
}
function surfaces(x, y) {
  const g = { z: G0 + GX * x + GY * y, a: GX, b: GY, id: 'g', cls: 'ground' };
  const sx = x >= 0 ? 1 : -1, sy = y >= 0 ? 1 : -1;
  const P = cheb([[Math.abs(x) - PX, sx, 0, 'x' + sx], [Math.abs(y) - PY, 0, sy, 'y' + sy]]);
  const Cp = face(PZ, 0, 0, P, cutProf(P.d, true), 1, 'pc', 'plan', 'slopeCut');
  const Fp = face(PZ, 0, 0, P, fillProf(P.d, true), -1, 'pf', 'plan', 'slopeFill');
  if (P.d <= 0) Cp.id = Fp.id = 'pt';
  // 진입도로 — 길 방향 x · 끝(동 R1 · 서 R0) 너머는 끝 높이 그대로 사면이 둘러싼다
  const xr = Math.max(R0, Math.min(R1, x)), zr = PZ + RS * (xr - R1), ar = x > R0 && x < R1 ? RS : 0;
  const R = cheb([[y >= 0 ? y - WN : -y - WS, 0, sy, 'y' + sy], [x - R1, 1, 0, 'xe'], [R0 - x, -1, 0, 'xw']]);
  const rc = Math.abs(y) <= AS ? 'road' : 'shoulder';
  const Cr = face(zr, ar, 0, R, cutProf(R.d, false), 1, 'rc', rc, 'slopeCut');
  const Fr = face(zr, ar, 0, R, fillProf(R.d, false), -1, 'rf', rc, 'slopeFill');
  if (R.d <= 0) Cr.id = Fr.id = 'rt';
  return { g, Cp, Fp, Cr, Fr };
}
// 땅 = min(max(원지반, 성토면들), 절토면들)
function design(x, y) {
  const { g, Cp, Fp, Cr, Fr } = surfaces(x, y);
  let lo = g;
  if (Fp.z > lo.z) lo = Fp;
  if (Fr.z > lo.z) lo = Fr;
  let hi = Cp.z < Cr.z ? Cp : Cr;
  const w = hi.z < lo.z ? hi : lo;
  // 계획면 · 도로 위(위 · 아래 면이 같음)는 그 이름
  return w;
}
const zAt = (x, y) => { const w = design(x, y); return w.id === 'g' ? groundZ(x, y) : w.z; };
const disturbed = (x, y) => design(x, y).id !== 'g';

// ── 격자(설계 자리 2 m · 밖 8 m · 축에 나란한 경계는 격자선) ──
function axisLines(lo, hi, fineLo, fineHi, fine, coarse, extra) {
  const s = new Set();
  for (let v = lo; v < fineLo; v += coarse) s.add(+v.toFixed(4));
  for (let v = fineLo; v <= fineHi; v += fine) s.add(+v.toFixed(4));
  for (let v = fineHi + coarse; v <= hi; v += coarse) s.add(+v.toFixed(4));
  s.add(hi); for (const e of extra) s.add(e);
  return [...s].sort((a, b) => a - b);
}
const XL = axisLines(-HALF, HALF, -340, 110, 2, 8, [R0, R1, -PX, PX]);
const YL = axisLines(-HALF, HALF, -100, 90, 2, 8, [-PY, PY, -AS, AS, -WS, WN, ...DITCH]);

// 볼록 다각형을 선 a x + b y + c = 0 으로 자름
function splitPoly(poly, a, b, c) {
  const L = [], R = [];
  const f = poly.map(([x, y]) => a * x + b * y + c);
  for (let i = 0; i < poly.length; i++) {
    const p = poly[i], q = poly[(i + 1) % poly.length], fp = f[i], fq = f[(i + 1) % poly.length];
    if (fp <= 0) L.push(p); if (fp >= 0) R.push(p);
    if ((fp < 0 && fq > 0) || (fp > 0 && fq < 0)) { const t = fp / (fp - fq); const m = [p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t]; L.push(m); R.push(m); }
  }
  const area = (P) => { let s = 0; for (let i = 0; i < P.length; i++) { const p = P[i], q = P[(i + 1) % P.length]; s += p[0] * q[1] - q[0] * p[1]; } return Math.abs(s) / 2; };
  return area(L) > 1e-6 && area(R) > 1e-6 ? [L, R] : null;
}
const tris = new Map();   // cls → [[x,y] × 3, …]
function emit(poly, depth) {
  const n = poly.length;
  const cx = poly.reduce((s, p) => s + p[0], 0) / n, cy = poly.reduce((s, p) => s + p[1], 0) / n;
  // 표본은 다각형 안쪽으로 조금 당긴다 — 경계선 바로 위 점이 옆 조각으로 읽혀 끝없이 가르지 않게
  const pts = [[cx, cy]];
  const inward = (x, y) => [x + (cx - x) * 1e-3, y + (cy - y) * 1e-3];
  for (let i = 0; i < n; i++) { const p = poly[i], q = poly[(i + 1) % n]; pts.push(inward(p[0], p[1]), inward((p[0] + q[0]) / 2, (p[1] + q[1]) / 2), inward((3 * p[0] + q[0]) / 4, (3 * p[1] + q[1]) / 4)); }
  // 조각 = 구분 + 평면(z = a x + b y + c) — 같은 평면 · 같은 구분이면 이름(체비쇼프 쪽)이 달라도 한 조각
  const inf = pts.map(([x, y]) => { const w = design(x, y); return { w, x, y, k: `${w.cls}|${w.a.toFixed(6)}|${w.b.toFixed(6)}|${(w.z - w.a * x - w.b * y).toFixed(4)}` }; });
  const keys = [...new Map(inf.map(s => [s.k, s])).values()];
  if (keys.length > 1 && depth < 12) {
    // 두 조각의 평면이 만나는 선 중 다각형을 가르며 두 표본을 갈라놓는 것
    let parts = null;
    for (let i = 0; i < keys.length && !parts; i++) for (let j = i + 1; j < keys.length && !parts; j++) {
      const p = keys[i], q = keys[j];
      const a = p.w.a - q.w.a, b = p.w.b - q.w.b;
      const c = (p.w.z - p.w.a * p.x - p.w.b * p.y) - (q.w.z - q.w.a * q.x - q.w.b * q.y);
      if (Math.hypot(a, b) < 1e-9) continue;
      const sp = a * p.x + b * p.y + c, sq = a * q.x + b * q.y + c;
      if (sp * sq >= 0) continue;
      parts = splitPoly(poly, a, b, c);
    }
    if (!parts) { // 못 찾음 — 긴 쪽으로 반 가름
      let bx0 = Infinity, bx1 = -Infinity, by0 = Infinity, by1 = -Infinity;
      for (const [x, y] of poly) { bx0 = Math.min(bx0, x); bx1 = Math.max(bx1, x); by0 = Math.min(by0, y); by1 = Math.max(by1, y); }
      parts = bx1 - bx0 > by1 - by0 ? splitPoly(poly, 1, 0, -(bx0 + bx1) / 2) : splitPoly(poly, 0, 1, -(by0 + by1) / 2);
    }
    if (parts) { emit(parts[0], depth + 1); emit(parts[1], depth + 1); return; }
  }
  const cls = design(cx, cy).cls;
  if (!tris.has(cls)) tris.set(cls, []);
  const T = tris.get(cls);
  for (let i = 1; i < n - 1; i++) T.push([poly[0], poly[i], poly[i + 1]]);
}
for (let j = 0; j < YL.length - 1; j++)
  for (let i = 0; i < XL.length - 1; i++)
    emit([[XL[i], YL[j]], [XL[i + 1], YL[j]], [XL[i + 1], YL[j + 1]], [XL[i], YL[j + 1]]], 0);

// ── 구분마다 점 나눔(C#와 같음) · 같은 자리 점은 하나로 ──
const CLASSES = ['ground', 'plan', 'bench', 'slopeCut', 'slopeFill', 'road', 'shoulder'];
const NAMES = { ground: '원지반', plan: '계획면', bench: '소단', slopeCut: '사면(절토)', slopeFill: '사면(성토)', road: '진입도로', shoulder: '길어깨' };
const pos = [], cf = [], idx = [], ranges = [], classes = [];
for (const key of CLASSES) {
  const T = tris.get(key);
  if (!T || !T.length) continue;
  const map = new Map(), start = idx.length / 3;
  const vid = ([x, y]) => {
    const k = Math.round(x * 1e4) + ',' + Math.round(y * 1e4);
    let v = map.get(k);
    if (v === undefined) {
      v = pos.length / 3; map.set(k, v);
      const z = zAt(x, y);
      pos.push(x, y, z);
      cf.push(key === 'ground' ? 0 : z - groundZ(x, y));
    }
    return v;
  };
  for (const t of T) {
    const [a, b, c] = t.map(vid);
    // 위에서 보아 반시계(법선 위)
    const ax = pos[3 * a], ay = pos[3 * a + 1], bx = pos[3 * b], by = pos[3 * b + 1], cx = pos[3 * c], cy = pos[3 * c + 1];
    if ((bx - ax) * (cy - ay) - (by - ay) * (cx - ax) < 0) idx.push(a, c, b); else idx.push(a, b, c);
  }
  ranges.push([classes.length, start, idx.length / 3 - start]);
  classes.push({ key, name: NAMES[key] });
}
const posA = new Float32Array(pos), idxA = new Uint32Array(idx), cfA = new Float32Array(cf);
let zMin = Infinity, zMax = -Infinity;
for (let i = 2; i < posA.length; i += 3) { zMin = Math.min(zMin, posA[i]); zMax = Math.max(zMax, posA[i]); }

// ── 흰 차선(차로 끝 안 0.1 m · 폭 0.1 m) · 측구(북쪽 U형 그레이팅 + 양 벽) ──
function strip(x0, x1, y0, y1, dz, step = 4) {
  const P = [], I = [];
  for (let x = x0, k = 0; x < x1 - 1e-6; x += step, k++) {
    const xa = x, xb = Math.min(x1, x + step), b = P.length / 3;
    for (const [px, py] of [[xa, y0], [xb, y0], [xb, y1], [xa, y1]]) P.push(px, py, zAt(px, (y0 + y1) / 2) + dz);
    I.push(b, b + 1, b + 2, b, b + 2, b + 3);
  }
  return { P, I };
}
function box(x0, x1, y0, y1, h, step = 4) {   // 도로를 따라 놓인 낮은 상자(위 · 양옆) — 바닥 높이는 도로면
  const P = [], I = [];
  for (let x = x0; x < x1 - 1e-6; x += step) {
    const xa = x, xb = Math.min(x1, x + step), za = zAt(xa, AS), zb = zAt(xb, AS), b = P.length / 3;
    P.push(xa, y0, za - 0.05, xb, y0, zb - 0.05, xb, y0, zb + h, xa, y0, za + h, xa, y1, za - 0.05, xb, y1, zb - 0.05, xb, y1, zb + h, xa, y1, za + h);
    I.push(b, b + 1, b + 2, b, b + 2, b + 3, b + 3, b + 2, b + 6, b + 3, b + 6, b + 7, b + 5, b + 4, b + 7, b + 5, b + 7, b + 6);
  }
  return { P, I };
}
const lanes = [strip(R0, R1, AS - 0.2, AS - 0.1, 0.004), strip(R0, R1, -AS + 0.1, -AS + 0.2, 0.004)];
const laneP = new Float32Array(lanes.flatMap(s => s.P)), laneI = [];
{ let o = 0; for (const s of lanes) { for (const i of s.I) laneI.push(i + o); o += s.P.length / 3; } }
const grate = strip(R0, R1, DITCH[0] + 0.15, DITCH[1] - 0.15, 0.012, 2);
const curbA = box(R0, R1, DITCH[0], DITCH[0] + 0.15, 0.12), curbB = box(R0, R1, DITCH[1] - 0.15, DITCH[1], 0.12);
const curbP = [...curbA.P, ...curbB.P], curbI = [...curbA.I, ...curbB.I.map(i => i + curbA.P.length / 3)];

// ── 콘크리트 옹벽 하나(계획면 북쪽 · 몸통 3 m · 기초는 계획면 아래) — 옹벽 콘크리트 질감 보기용 ──
function cuboid(x0, x1, y0, y1, z0, z1) {
  const P = [x0, y0, z0, x1, y0, z0, x1, y1, z0, x0, y1, z0, x0, y0, z1, x1, y0, z1, x1, y1, z1, x0, y1, z1];
  const I = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7];
  return { P: new Float32Array(P), I: new Uint32Array(I) };
}
const WALL = [25, 58, 36.0, 36.4];
const wBody = cuboid(WALL[0], WALL[1], WALL[2], WALL[3], PZ - 0.1, PZ + 3), wFoot = cuboid(WALL[0] - 0.3, WALL[1] + 0.3, WALL[2] - 0.8, WALL[3] + 0.8, PZ - 0.5, PZ - 0.05);

// ── 시공 순서 격자(1 m · 설계 자리) ──
const B0 = [-320, -80], BW = 420, BH = 160;
const bzf = new Float32Array((BW + 1) * (BH + 1)), bzg = new Float32Array(bzf.length), broad = new Uint8Array(bzf.length);
for (let j = 0; j <= BH; j++) for (let i = 0; i <= BW; i++) {
  const x = B0[0] + i, y = B0[1] + j, k = j * (BW + 1) + i, w = design(x, y);
  bzf[k] = zAt(x, y); bzg[k] = groundZ(x, y); broad[k] = w.id[0] === 'r' ? 1 : 0;
}

// ── 가짜 위성(1024² PNG · 북쪽이 첫 줄) — 밭 · 풀밭 · 숲 얼룩 ──
const forestAt = (x, y) => fbm(x / 140, y / 140, 71) + 0.25 * (y > 60 ? 1 : 0) - 0.1 * (y < -120 ? 1 : 0);
function satPng(N) {
  const rows = [];
  for (let r = 0; r < N; r++) {
    const row = Buffer.alloc(1 + N * 3);
    const y = HALF - (r + 0.5) * (2 * HALF / N);
    for (let c = 0; c < N; c++) {
      const x = -HALF + (c + 0.5) * (2 * HALF / N);
      const f = forestAt(x, y), n1 = fbm(x / 60, y / 60, 11), n2 = fbm(x / 9, y / 9, 13, 3);
      let R = 118 + 40 * (n1 - 0.5), G = 128 + 30 * (n1 - 0.5), Bc = 76 + 20 * (n1 - 0.5);       // 풀밭
      const t = smooth01(0.52, 0.6, f);                                                       // 숲
      R = R * (1 - t) + (48 + 30 * n2) * t; G = G * (1 - t) + (66 + 34 * n2) * t; Bc = Bc * (1 - t) + (38 + 18 * n2) * t;
      const k = 0.85 + 0.3 * n2;
      row[1 + c * 3] = Math.max(0, Math.min(255, R * k)); row[2 + c * 3] = Math.max(0, Math.min(255, G * k)); row[3 + c * 3] = Math.max(0, Math.min(255, Bc * k));
    }
    rows.push(row);
  }
  const tab = new Int32Array(256);
  for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; tab[n] = c; }
  const crc = (b) => { let x = -1; for (let i = 0; i < b.length; i++) x = tab[(x ^ b[i]) & 255] ^ (x >>> 8); return (x ^ -1) >>> 0; };
  const chunk = (t, d) => { const l = Buffer.alloc(4), c = Buffer.alloc(4), td = Buffer.concat([Buffer.from(t, 'ascii'), d]); l.writeUInt32BE(d.length); c.writeUInt32BE(crc(td)); return Buffer.concat([l, td, c]); };
  const ih = Buffer.alloc(13); ih.writeUInt32BE(N, 0); ih.writeUInt32BE(N, 4); ih[8] = 8; ih[9] = 2;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ih), chunk('IDAT', deflateSync(Buffer.concat(rows), { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}
const sat = satPng(1024);

// ── 숲(현장 둘레 · 손댄 자리 + 10 m 밖) — forest-page.mjs와 같은 묶음 꼴 ──
const trees = [];
for (let cj = -60; cj < 60; cj++) for (let ci = -60; ci < 60; ci++) {
  const s = 7.5, x = (ci + 0.5 + (hash2(ci, cj, 1) - 0.5) * 0.9) * s, y = (cj + 0.5 + (hash2(ci, cj, 2) - 0.5) * 0.9) * s;
  if (Math.abs(x) > HALF - 5 || Math.abs(y) > HALF - 5 || forestAt(x, y) < 0.56) continue;
  let near = false;
  for (const [dx, dy] of [[0, 0], [10, 0], [-10, 0], [0, 10], [0, -10], [7, 7], [-7, 7], [7, -7], [-7, -7]]) if (disturbed(x + dx, y + dy)) { near = true; break; }
  if (near) continue;
  const kind = hash2(ci, cj, 3) < 0.55 ? 0 : hash2(ci, cj, 4) < 0.7 ? 3 : 4;
  const H = (kind === 0 ? 12 : 10) + hash2(ci, cj, 5) * 7, W = Math.min(14, (kind === 0 ? 0.28 * H + 0.8 : 0.45 * H + 1) * (0.8 + 0.4 * hash2(ci, cj, 6)));
  trees.push({ x, y, z: zAt(x, y) - 0.3, H, W, kind, rot: Math.floor(hash2(ci, cj, 7) * 256), tint: Math.floor(hash2(ci, cj, 8) * 256), u: hash2(ci, cj, 9), h: hash2(ci, cj, 10) });
}
const CH = 250;
const ord = trees.map(t => ({ t, cx: Math.floor(t.x / CH), cy: Math.floor(t.y / CH) }))
  .sort((a, b) => a.cx - b.cx || a.cy - b.cy || a.t.kind - b.t.kind || a.t.u - b.t.u || a.t.h - b.t.h);
const nT = ord.length;
const fxy = new Float32Array(2 * nT), fz = new Float32Array(nT), hw = new Uint8Array(2 * nT), fk = new Uint8Array(nT), fr = new Uint8Array(nT), ft = new Uint8Array(nT);
const grp = [], cbox = [], czr = [];
{
  let lcx = null, lcy = null, lk = -1, chunk = -1;
  for (let i = 0; i < nT; i++) {
    const { t, cx, cy } = ord[i];
    if (cx !== lcx || cy !== lcy) { chunk++; lcx = cx; lcy = cy; lk = -1; cbox.push(cx * CH, cy * CH, (cx + 1) * CH, (cy + 1) * CH); czr.push(Infinity, -Infinity); }
    if (t.kind !== lk) { grp.push(i, 0, chunk); lk = t.kind; }
    grp[grp.length - 2]++;
    fxy[2 * i] = t.x; fxy[2 * i + 1] = t.y; fz[i] = t.z;
    hw[2 * i] = Math.max(1, Math.min(255, Math.round(t.H / 0.2))); hw[2 * i + 1] = Math.max(1, Math.min(255, Math.round(t.W / 0.2)));
    fk[i] = t.kind; fr[i] = t.rot; ft[i] = t.tint;
    czr[2 * chunk] = Math.min(czr[2 * chunk], t.z); czr[2 * chunk + 1] = Math.max(czr[2 * chunk + 1], t.z + t.H);
  }
}

// ── «DH3D» 묶음(data.js와 짝) ──
const blobs = [];
let off = 0;
const ref = (arr, t, m) => {
  const b = Buffer.isBuffer(arr) ? arr : Buffer.from(arr.buffer, arr.byteOffset, arr.byteLength);
  const r = { o: off, n: b.length, t };
  if (m) r.m = m;
  blobs.push(b);
  const pad = (4 - (b.length % 4)) % 4;
  if (pad) blobs.push(Buffer.alloc(pad));
  off += b.length + pad;
  return r;
};
const head = {
  dwg: 'ground-test', origin: [0, 0], geo: { lat: 37.5, lon: 127.0 },
  bbox: [-HALF, -HALF, zMin, HALF, HALF, zMax + 25],
  focus: [-310, -70, 45, 90, 70, 75],
  terrain: { pos: ref(posA, 'f32'), idx: ref(idxA, 'u32'), cf: ref(cfA, 'f32'), classes, ranges },
  sat: { base: { img: ref(sat, 'img', 'image/png'), ext: [-HALF, -HALF, HALF, HALF] } },
  lanes: { pos: ref(laneP, 'f32'), idx: ref(new Uint32Array(laneI), 'u32'), n: 2 },
  ditches: {
    grate: { pos: ref(new Float32Array(grate.P), 'f32'), idx: ref(new Uint32Array(grate.I), 'u32') },
    curb: { pos: ref(new Float32Array(curbP), 'f32'), idx: ref(new Uint32Array(curbI), 'u32') },
  },
  walls: [{
    id: 'W1', name: '시험 옹벽', kind: '역T형', box: [WALL[0], WALL[2], WALL[1], WALL[3]],
    parts: [{ mat: '콘크리트', pos: ref(wBody.P, 'f32'), idx: ref(wBody.I, 'u32') }, { mat: '기초', pos: ref(wFoot.P, 'f32'), idx: ref(wFoot.I, 'u32') }],
  }],
  build: { x0: B0[0], y0: B0[1], cell: 1, w: BW, h: BH, zf: ref(bzf, 'f32'), zg: ref(bzg, 'f32'), road: ref(broad, 'u8') },
  forest: {
    n: nT, xy: ref(fxy, 'f32'), z: ref(fz, 'f32'), hw: ref(hw, 'u8'), k: ref(fk, 'u8'), r: ref(fr, 'u8'), t: ref(ft, 'u8'),
    grp: ref(new Uint32Array(grp), 'u32'), cbox: ref(new Float32Array(cbox), 'f32'), czr: ref(new Float32Array(czr), 'f32'), chunk: CH,
    kinds: ['소나무형', '원뿔형', '짙은원뿔형', '참나무형', '달걀형', '길쭉형', '넓은형', '상록활엽형', '대나무'],
    legend: [], note: `시험 숲 · 나무 ${nT.toLocaleString('ko-KR')}그루`,
  },
};
const hj = Buffer.from(JSON.stringify(head), 'utf8');
const pre = Buffer.alloc(12); pre.write('DH3D', 0, 'ascii'); pre.writeUInt32LE(1, 4); pre.writeUInt32LE(hj.length, 8);
const hp = (4 - ((12 + hj.length) % 4)) % 4;
const data = gzipSync(Buffer.concat([pre, hj, Buffer.alloc(hp), ...blobs])).toString('base64');

const tpl = readFileSync(join(root, 'page.html'), 'utf8');
const viewer = readFileSync(viewerPath, 'utf8');
const iT = tpl.indexOf('__TITLE__'), iD = tpl.indexOf('__DATA__'), iV = tpl.indexOf('/*__VIEWER__*/');
const html = tpl.slice(0, iT) + '바닥 질감 시험' + tpl.slice(iT + 9, iD) + data + tpl.slice(iD + 8, iV) + viewer + tpl.slice(iV + 14);
writeFileSync(out, html);
console.log(`${out} · 세모 ${idxA.length / 3} (${ranges.map(([c, , n]) => classes[c].key + ' ' + n).join(' · ')}) · 나무 ${nT} · 도로 기울기 ${(RS * 100).toFixed(1)} % · ${(html.length / 1048576).toFixed(1)} MB`);
