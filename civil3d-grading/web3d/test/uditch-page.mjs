// ★[v117.16 · JACK 1112 «측구가 지표면이랑 겹쳐서 안 보이네»] U형 측구 시험 페이지 — 기운 물결 지형(세모 약 10만 · 구분 셋) 위에
//   열린 U형 측구(꺾임 · 굽음) · 그레이팅 측구(T자로 만남) · 집수정 둘 · 관로 하나를 C#와 같은 «DH3D» 묶음으로 싸서 page.html + 뷰어에 넣는다.
//   측구 메시는 C# SceneMesh 그대로 옮긴 것(uditch-mesh.mjs) · 바닥고 = 양 끝 지표 − 안높이(한 경사) → 가운데는 지형과 몇 cm씩 어긋남(실제와 같음).
//   node test/uditch-page.mjs <나갈 html> [viewer.js(기본 dist/viewer.js)] · 찍기 = node tools/shot.mjs <html> <폴더> <보기 JSON>
//   (이름에 .test 없음 — npm test가 돌리지 않는다 · 좌표 · 이름은 모두 지어낸 것)
import { readFileSync, writeFileSync } from 'node:fs';
import { gzipSync } from 'node:zlib';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { sceneMesh, gridTerrain, joinMeshes } from './uditch-mesh.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const [, , out, viewerArg] = process.argv;
if (!out) { console.error('쓰는 법: node test/uditch-page.mjs <out.html> [viewer.js]'); process.exit(2); }
const viewerPath = viewerArg || join(root, 'dist', 'viewer.js');

const zAt = (x, y) => 50 + 0.03 * x + 0.06 * y + 0.25 * Math.sin(x / 7) * Math.cos(y / 9);
const cls = (x, y) => (Math.abs(x) < 35 && Math.abs(y) < 25 ? 0 : Math.abs(x) < 50 && Math.abs(y) < 38 ? 1 : 2);
const T = gridTerrain(267, 200, 0.45, -60, -45, zAt, cls);
const classes = [{ key: 'plan', name: '계획면' }, { key: 'slopeCut', name: '사면(절토)' }, { key: 'ground', name: '원지반' }];

// ★[v117.16 · C# FollowMeshes] D-001 = 구간 셋(parts) — 구간 1(한 경사 · 끝은 지표 위 0.2) → 낙차 0.2(x = 5 · 아래 구간 안쪽 낙차벽) → 구간 2 → 집수정 1(−18.6) + 관로 → 구간 3 → 집수정 2(−30.6)
const i1a = zAt(31, 15) - 0.3, i1b = zAt(5, -20) - 0.1, i2a = i1b - 0.2, i2b = zAt(-18, -20) - 0.3, i3a = zAt(-24, -20) - 0.35, i3b = zAt(-30, -20) - 0.3;
const d1 = joinMeshes([
  { m: sceneMesh([[31, 15], [31, -4], [30, -11], [27, -16], [22, -19], [15, -20], [5, -20]], i1a, i1b), seg: 1 },
  { m: sceneMesh([[5, -20], [-5, -20], [-18, -20]], i2a, i2b), seg: 2 },
  { m: sceneMesh([[-24, -20], [-30, -20]], i3a, i3b), seg: 3 },
  { box: [[5, -20.15], [5, -19.85], [4.85, -19.85], [4.85, -20.15]], zb: i2a - (i2a - i2b) * 0.15 / 23, zt: i1b, seg: 2 },
]);
const d2m = sceneMesh([[0, 12], [0, -20]], zAt(0, 12) - 0.4, zAt(0, -20) - 0.4, { B: 0.4, H: 0.4, Tw: 0.15, Tb: 0.15 });
const d2 = { ...d2m, parts: d2m.parts.map(q => ({ ...q, seg: 1 })) };
const rows = (k, b) => [['규격', `U형 ${k}`], ['뚜껑', b ? '스틸 그레이팅' : '없음(열린 U형)'], ['판정', 'OK']];
const zb1 = zAt(-30.6, -20), zb2 = zAt(31, 15.5), zb3 = zAt(-18.6, -20), zb4 = zAt(-23.4, -20);

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
let z0 = Infinity, z1 = -Infinity;
for (let i = 2; i < T.pos.length; i += 3) { z0 = Math.min(z0, T.pos[i]); z1 = Math.max(z1, T.pos[i]); }
const head = {
  dwg: 'uditch-test', origin: [0, 0],
  bbox: [-60, -45, z0 - 2, 60.15, 45, z1 + 5],
  focus: [-35, -25, z0, 35, 20, z1],
  terrain: { pos: ref(T.pos, 'f32'), idx: ref(T.idx, 'u32'), classes, ranges: T.ranges },
  uditch: [
    { name: 'D-001', title: 'D-001 U형 측구', status: 'OK', rows: rows('300×300', false), pos: ref(d1.pos, 'f32'), idx: ref(d1.idx, 'u32'), parts: d1.parts, grating: false,
      basins: [
        { x: -18.6, y: -20, zTop: zb3, zBottom: zb3 - 1.4, w: 1.2, l: 1.2, inner: 0.8, rot: Math.PI, name: 'D-001 집수정 1', title: 'D-001 집수정 1', rows: [['깊이', '1.40 m'], ['덮개', '그레이팅']] },
        { x: -23.4, y: -20, zTop: zb4, zBottom: i3a - 0.4, w: 1.2, l: 1.2, inner: 0.8, rot: Math.PI, name: 'D-001 집수정 2(아래)', title: 'D-001 집수정 2(아래)', rows: [['덮개', '그레이팅']] },
        { x: -30.6, y: -20, zTop: zb1, zBottom: zb1 - 1.2, w: 1.2, l: 1.2, rot: Math.PI, name: 'D-001 집수정 2', title: 'D-001 집수정 2', rows: [['덮개', '그레이팅']] },
        { x: 31, y: 15.5, zTop: zb2, zBottom: zb2 - 1.0, w: 0.9, l: 0.9, rot: 0.2 },
      ],
      pipes: [
        // ★[v117.16] 단차마다 곧은 토막 셋(위 집수정 → 수평 → 내림 → 수평 → 아래 집수정)
        { x1: -19.2, y1: -20, z1: zb3 - 1.0, x2: -20, y2: -20, z2: zb3 - 1.0, d: 0.4, name: 'D-001 관로 1', title: 'D-001 관로 1', rows: [['지름', 'D300 (바깥 0.40 m)']] },
        { x1: -20, y1: -20, z1: zb3 - 1.0, x2: -22, y2: -20, z2: i3a + 0.05, d: 0.4, name: 'D-001 관로 1', title: 'D-001 관로 1', rows: [['지름', 'D300 (바깥 0.40 m)']] },
        { x1: -22, y1: -20, z1: i3a + 0.05, x2: -22.8, y2: -20, z2: i3a + 0.05, d: 0.4, name: 'D-001 관로 1', title: 'D-001 관로 1', rows: [['지름', 'D300 (바깥 0.40 m)']] },
        { x1: -31.2, y1: -20, z1: zb1 - 0.9, x2: -48, y2: -22, z2: zb1 - 1.3, d: 0.4 },
      ] },
    { name: 'D-002', title: 'D-002 U형 측구', status: 'OK', rows: rows('400×400', true), pos: ref(d2.pos, 'f32'), idx: ref(d2.idx, 'u32'), parts: d2.parts, grating: true },
  ],
};
const hj = Buffer.from(JSON.stringify(head), 'utf8');
const pre = Buffer.alloc(12); pre.write('DH3D', 0, 'ascii'); pre.writeUInt32LE(1, 4); pre.writeUInt32LE(hj.length, 8);
const hp = (4 - ((12 + hj.length) % 4)) % 4;
const data = gzipSync(Buffer.concat([pre, hj, Buffer.alloc(hp), ...blobs])).toString('base64');

const tpl = readFileSync(join(root, 'page.html'), 'utf8');
const viewer = readFileSync(viewerPath, 'utf8');
const iT = tpl.indexOf('__TITLE__'), iD = tpl.indexOf('__DATA__'), iV = tpl.indexOf('/*__VIEWER__*/');
const html = tpl.slice(0, iT) + 'U형 측구 시험' + tpl.slice(iT + 9, iD) + data + tpl.slice(iD + 8, iV) + viewer + tpl.slice(iV + 14);
writeFileSync(out, html);
console.log(`${out} · 세모 ${T.idx.length / 3} · 측구 2 · ${(html.length / 1048576).toFixed(1)} MB · 보기 자리: D-001 (-12, -20, ${zAt(-12, -20).toFixed(2)}) · D-002 (0, -5, ${zAt(0, -5).toFixed(2)}) · 집수정 (-30.6, -20, ${zb1.toFixed(2)})`);
