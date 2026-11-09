// 수목 그림 시험 페이지 — 가짜 지형(1.6 km 언덕 · 잔디 · 가운데 계획면) + 가짜 숲(임상도 모양 0~8 · 침엽 · 활엽 · 혼효 · 대나무)을
//   C#와 같은 «DH3D» 묶음으로 싸서 page.html + 뷰어에 넣는다. Civil 없이 나무 모양 · 질감 · LOD를 눈으로 보려고(tools/shot.mjs로 찍음).
//   node test/forest-page.mjs <나갈 html> [viewer.js(기본 dist/viewer.js)] [나무 간격 배율(기본 1)]
//   (이름에 .test 없음 — npm test가 돌리지 않는다)
import { readFileSync, writeFileSync } from 'node:fs';
import { gzipSync } from 'node:zlib';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const [, , out, viewerArg, spacingArg] = process.argv;
if (!out) { console.error('쓰는 법: node test/forest-page.mjs <out.html> [viewer.js] [간격 배율]'); process.exit(2); }
const viewerPath = viewerArg || join(root, 'dist', 'viewer.js');
const SP = Number(spacingArg) || 1;

// 정해진 난수(같은 입력 = 같은 숲)
let seed = 12345;
const rnd = () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; };
const hash2 = (i, j, s) => { let h = (i * 374761393 + j * 668265263 + s * 2147483647) >>> 0; h = Math.imul(h ^ (h >>> 13), 1274126177) >>> 0; return ((h ^ (h >>> 16)) >>> 0) / 4294967296; };

const HALF = 800, STEP = 10;
const ground = (x, y) => 60 + 28 * Math.sin(x / 210) * Math.cos(y / 260) + 12 * Math.sin((x + 0.6 * y) / 110) + 4 * Math.cos((x - y) / 47);
const PAD = [-130, -90, 130, 90];   // 계획면(나무 없음)
const inPad = (x, y) => x > PAD[0] && x < PAD[2] && y > PAD[1] && y < PAD[3];

// ── 지형 ──
const nx = 2 * HALF / STEP + 1;
const pos = new Float32Array(nx * nx * 3);
let zMin = Infinity, zMax = -Infinity;
for (let j = 0; j < nx; j++) for (let i = 0; i < nx; i++) {
  const x = -HALF + i * STEP, y = -HALF + j * STEP, z = inPad(x, y) ? ground(0, 0) : ground(x, y);
  const k = (j * nx + i) * 3; pos[k] = x; pos[k + 1] = y; pos[k + 2] = z;
  zMin = Math.min(zMin, z); zMax = Math.max(zMax, z);
}
const triG = [], triP = [];
for (let j = 0; j < nx - 1; j++) for (let i = 0; i < nx - 1; i++) {
  const a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
  const cx = -HALF + (i + 0.5) * STEP, cy = -HALF + (j + 0.5) * STEP;
  (inPad(cx, cy) ? triP : triG).push(a, b, d, a, d, c);
}
const idx = new Uint32Array([...triG, ...triP]);
const zAt = (x, y) => (inPad(x, y) ? ground(0, 0) : ground(x, y));

// ── 숲 — 무리(보로노이 씨앗)마다 모양 · 키 구간 ──
const STANDS = [
  { kind: 0, h: [11, 17] }, { kind: 0, h: [14, 20] }, { kind: 1, h: [15, 22] }, { kind: 2, h: [13, 20] },
  { kind: 3, h: [10, 17] }, { kind: 3, h: [12, 19] }, { kind: 4, h: [9, 15] }, { kind: 5, h: [14, 22] },
  { kind: 6, h: [10, 16] }, { kind: 7, h: [7, 12] }, { kind: 8, h: [8, 14] }, { kind: -1, h: [11, 18] },
  { kind: -1, h: [10, 16] }, { kind: 3, h: [11, 18] }, { kind: 0, h: [12, 18] }, { kind: 4, h: [10, 16] },
];
const seeds = STANDS.map((s, i) => ({ ...s, x: -HALF + 100 + rnd() * (2 * HALF - 200), y: -HALF + 100 + rnd() * (2 * HALF - 200) }));
// 계획면 둘레에 침엽 · 활엽 · 대나무가 다 보이게 몇 무리를 가까이 둔다
Object.assign(seeds[0], { x: -260, y: -150 }); Object.assign(seeds[4], { x: 230, y: -170 }); Object.assign(seeds[2], { x: 0, y: -260 });
Object.assign(seeds[10], { x: 200, y: 140 }); Object.assign(seeds[11], { x: -230, y: 150 }); Object.assign(seeds[7], { x: 40, y: 230 });
Object.assign(seeds[3], { x: -360, y: 10 }); Object.assign(seeds[8], { x: 360, y: 0 }); Object.assign(seeds[9], { x: 170, y: 330 });
const crown = (kind, H, j) => (kind === 8 ? 1.5 : kind <= 2 ? 0.28 * H + 0.8 : 0.45 * H + 1.0) * j;
const trees = [];
// 대나무 무리는 씨앗 둘레 80 m 안만(실제 죽림은 작다)
const standAt = (x, y) => { let b = 0, bd = Infinity; for (let i = 0; i < seeds.length; i++) { const d = (seeds[i].x - x) ** 2 + (seeds[i].y - y) ** 2; if (seeds[i].kind === 8 && d > 80 * 80) continue; if (d < bd) { bd = d; b = i; } } return b; };
seeds.forEach((st, si) => {
  const hm = (st.h[0] + st.h[1]) / 2, wAvg = st.kind === -1 ? (crown(0, hm, 1) + crown(3, hm, 1)) / 2 : crown(st.kind, hm, 1);
  const s = Math.max(2.5, Math.min(14, wAvg / Math.sqrt(0.85))) * SP;   // C# GridNo와 같은 간격(덮임 0.85)
  for (let cj = Math.floor(-HALF / s); cj <= Math.ceil(HALF / s); cj++) for (let ci = Math.floor(-HALF / s); ci <= Math.ceil(HALF / s); ci++) {
    const x = (ci + 0.5 + (hash2(ci, cj, 1 + si * 17) - 0.5) * 0.9) * s, y = (cj + 0.5 + (hash2(ci, cj, 2 + si * 17) - 0.5) * 0.9) * s;
    if (Math.abs(x) > HALF - 5 || Math.abs(y) > HALF - 5 || standAt(x, y) !== si) continue;
    if (x > PAD[0] - 12 && x < PAD[2] + 12 && y > PAD[1] - 12 && y < PAD[3] + 12) continue;
    // 빈터(풀밭) — 큰 물결 무늬가 높은 곳
    if (Math.sin(x / 97 + 1.3) * Math.cos(y / 83 - 0.4) + 0.5 * Math.sin((x - y) / 61) > 0.95) continue;
    const kind = st.kind === -1 ? (hash2(ci, cj, 3 + si) < 0.5 ? 0 : 3) : st.kind;
    const H = st.h[0] + hash2(ci, cj, 4 + si) * (st.h[1] - st.h[0]);
    const W = Math.min(25, crown(kind, H, 0.8 + 0.4 * hash2(ci, cj, 5 + si)));
    trees.push({ x, y, z: zAt(x, y) - 0.3, H, W, kind, rot: Math.floor(hash2(ci, cj, 7 + si) * 256), tint: Math.floor(hash2(ci, cj, 11 + si) * 256), u: hash2(ci, cj, 6 + si), h: hash2(ci, cj, 9 + si) });
  }
});

// ── C# ForestScatter.Pack와 같은 차례 · 배열 ──
const CH = 250;
const ord = trees.map(t => ({ t, cx: Math.floor(t.x / CH), cy: Math.floor(t.y / CH) }))
  .sort((a, b) => a.cx - b.cx || a.cy - b.cy || a.t.kind - b.t.kind || a.t.u - b.t.u || a.t.h - b.t.h);
const n = ord.length;
const fxy = new Float32Array(2 * n), fz = new Float32Array(n), hw = new Uint8Array(2 * n), fk = new Uint8Array(n), fr = new Uint8Array(n), ft = new Uint8Array(n);
const grp = [], cbox = [], czr = [];
let lcx = null, lcy = null, lk = -1, chunk = -1;
for (let i = 0; i < n; i++) {
  const { t, cx, cy } = ord[i];
  if (cx !== lcx || cy !== lcy) { chunk++; lcx = cx; lcy = cy; lk = -1; cbox.push(cx * CH, cy * CH, (cx + 1) * CH, (cy + 1) * CH); czr.push(Infinity, -Infinity); }
  if (t.kind !== lk) { grp.push(i, 0, chunk); lk = t.kind; }
  grp[grp.length - 2]++;
  fxy[2 * i] = t.x; fxy[2 * i + 1] = t.y; fz[i] = t.z;
  hw[2 * i] = Math.max(1, Math.min(255, Math.round(t.H / 0.2))); hw[2 * i + 1] = Math.max(1, Math.min(255, Math.round(t.W / 0.2)));
  fk[i] = t.kind; fr[i] = t.rot; ft[i] = t.tint;
  czr[2 * chunk] = Math.min(czr[2 * chunk], t.z); czr[2 * chunk + 1] = Math.max(czr[2 * chunk + 1], t.z + t.H);
}

// ── «DH3D» 묶음(data.js와 짝) ──
const blobs = [];
let off = 0;
const ref = (arr, t) => {
  const b = Buffer.from(arr.buffer, arr.byteOffset, arr.byteLength);
  const r = { o: off, n: b.length, t };
  blobs.push(b);
  const pad = (4 - (b.length % 4)) % 4;
  if (pad) blobs.push(Buffer.alloc(pad));
  off += b.length + pad;
  return r;
};
const head = {
  dwg: 'forest-test', origin: [0, 0], geo: { lat: 37.5, lon: 127.0 },   // 해 그림자 시험용(서울 근처)
  bbox: [-HALF, -HALF, zMin, HALF, HALF, zMax + 30],
  focus: [PAD[0], PAD[1], ground(0, 0), PAD[2], PAD[3], ground(0, 0) + 5],
  terrain: {
    pos: ref(pos, 'f32'), idx: ref(idx, 'u32'),
    classes: [{ key: 'slopeFill', name: '잔디' }, { key: 'plan', name: '계획면' }],
    ranges: [[0, 0, triG.length / 3], [1, triG.length / 3, triP.length / 3]],
  },
  forest: {
    n, xy: ref(fxy, 'f32'), z: ref(fz, 'f32'), hw: ref(hw, 'u8'), k: ref(fk, 'u8'), r: ref(fr, 'u8'), t: ref(ft, 'u8'),
    grp: ref(new Uint32Array(grp), 'u32'), cbox: ref(new Float32Array(cbox), 'f32'), czr: ref(new Float32Array(czr), 'f32'), chunk: CH,
    kinds: ['소나무형', '원뿔형', '짙은원뿔형', '참나무형', '달걀형', '길쭉형', '넓은형', '상록활엽형', '대나무'],
    legend: [], note: `시험 숲 · 나무 ${n.toLocaleString('ko-KR')}그루`,
  },
};
const hj = Buffer.from(JSON.stringify(head), 'utf8');
const pre = Buffer.alloc(12); pre.write('DH3D', 0, 'ascii'); pre.writeUInt32LE(1, 4); pre.writeUInt32LE(hj.length, 8);
const hp = (4 - ((12 + hj.length) % 4)) % 4;
const pack = Buffer.concat([pre, hj, Buffer.alloc(hp), ...blobs]);
const data = gzipSync(pack).toString('base64');

const tpl = readFileSync(join(root, 'page.html'), 'utf8');
const viewer = readFileSync(viewerPath, 'utf8');
const iT = tpl.indexOf('__TITLE__'), iD = tpl.indexOf('__DATA__'), iV = tpl.indexOf('/*__VIEWER__*/');
const html = tpl.slice(0, iT) + '수목 시험' + tpl.slice(iT + 9, iD) + data + tpl.slice(iD + 8, iV) + viewer + tpl.slice(iV + 14);
writeFileSync(out, html);
const perKind = new Array(9).fill(0); for (const t of trees) perKind[t.kind]++;
console.log(`${out} · 나무 ${n} · 덩이 ${chunk + 1} · 무리 ${grp.length / 3} · 모양별 ${perKind.join('/')} · ${(html.length / 1048576).toFixed(1)} MB`);
