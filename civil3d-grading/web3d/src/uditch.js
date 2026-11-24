// ★[배수지 U형 측구 1단계 · JACK 1111 «U형 측구 결과물은 3D 보기에도 반영»] U형 측구 층 — 측구마다 메시 하나(본체 + 기초 · 안쪽 물길이 보이는 U자 · C# UDitch.SceneMesh) ·
//   콘크리트 질감(구조물과 같은 재료 틀) · 수리 판정 기운(부족 = 붉은 기운 · 주의 = 노란 기운 · 그 밖 = 콘크리트) · 층 «U형 측구»(기본 켬 · 없는 장면은 안 생김) ·
//   누르면 이름 · 규격 · 경사 · 수리 판정(C#이 만든 [이름, 값] 줄 그대로 — 셈은 뷰어에 없음).
// ★[v117.16 · JACK 1112 «3D 뷰에서는 좀 보여야 하는데 … 측구가 지표면이랑 겹쳐서 안 보이네» · 열린 U형 · «civil3d 지표면엔 영향이 없게»]
//   측구 자리 지형을 3D에서만 파낸다(uhole.js — 그릴 세모만 새로 · head.terrain · Civil 값은 그대로) · 안쪽(벽 안면 · 바닥)은 어두운 콘크리트 ·
//   바닥 가운데 물 자국 · 양 끝 마구리 벽 · 층 «U형 측구»를 끄면 구멍도 닫힘(원래 지형).
// ★[JACK 1124 «측구를 그렸더니 진입도로 모델링이 깨져 · 3D 뷰에서는 실제 모양으로 지표면엔 영향을 주지 않게 보이게만»]
//   지형 세모를 더는 자르지 않는다(옛 CPU 구멍은 테두리 높이를 측구 윗면으로 내려 묻힌 측구 옆 도로 면이 꺼져 보였다).
//   대신 그리기만 — 측구 · 집수정 윗면 자리(옛 구멍과 같은 평면 테두리)를 스텐실 한 비트에 먼저 적고(색 · 깊이 안 씀),
//   지형 · 차선 · 줄눈은 그 비트가 선 화소만 안 그린다. 지형 메시 · 장면 값 · 누르기 · 자르기 · 빗물 셈은 원래 지형 그대로.
// ★[v117.16 · JACK 1112 «측구에 스틸그라우팅도 선택되게 해줘»] head.uditch[*].grating = true → 윗면에 스틸 그레이팅(어두운 쇠 · 가는 살) · 구멍은 그대로.
// ★[v117.16] 앞으로(C#이 넣으면 · 없으면 안 그림 · 같은 층) — 집수정 head.uditch[*].basins = [{ x, y, zTop, zBottom, w, l, rot? }](네모 상자 + 윗면 그레이팅 · 지형 구멍) ·
//   관로 head.uditch[*].pipes = [{ x1, y1, z1, x2, y2, z2, d }](두 점 사이 원통 · z = 관 중심 · d = 바깥 지름 m) · 좌표 = 측구 메시와 같은 장면 좌표.
import * as THREE from 'three';
import { ditchFrames, chainFrames, mergeChain, holeShapes } from './uhole.js';

export const UDITCH_COLOR = '#c9c5bc';
const TINT = { '부족': '#d79a92', '주의': '#d9cc8c' };
const INNER = 0.8;          // 안쪽 콘크리트 밝기 곱(선형 · 조금만) — 선 벽은 빛을 덜 받아 저절로 어두우니 겹쳐 새까매지지 않게
const WATER = '#3d5866';    // 바닥 물 자국
const PIPE = '#a8a49c';     // 관로(흄관 빛)
const TILE = 0.1;           // 그레이팅 그림 한 장 = 0.1 m × 0.1 m(주부재 33 mm 간격 셋 · 가로대 하나)
const LIFT = 0.006;         // 그레이팅을 윗면 위로(로그 깊이라 polygonOffset이 안 먹음 — 높이로 띄움)

// mk = { material(color) · geom(pos, idx) · flat(g) } — layers.js의 틀을 빌려 쓴다(질감 · 그림자가 옹벽 · 구조물과 같게)
export function buildUDitch(head, scene, L, mk) {
  const U = head.uditch;
  if (!U || !U.length) return;
  const grp = new THREE.Group();
  const mats = new Map();
  const matOf = (c) => {
    if (!mats.has(c)) mats.set(c, mk.material(c));
    return mats.get(c);
  };
  const darker = (c) => '#' + new THREE.Color(c).multiplyScalar(INNER).getHexString();
  const frames = [], basins = [];
  const stats = { holes: 0, tris: 0, dropped: 0, failed: 0, newV: 0, segs: 0, grating: 0, basins: 0, pipes: 0, joints: 0 };
  let n = 0;
  for (const u of U) {
    const info = { layer: 'uditch', name: u.name, title: u.title || u.name, rows: Array.isArray(u.rows) ? u.rows : [] };
    const color = TINT[u.status] || UDITCH_COLOR;
    if (u.pos && u.idx && u.idx.length) {
      // ★[v117.16] 구간이 여럿이면 덩어리가 여럿(u.parts) — 본체마다 틀 · 없으면 옛 한 덩어리
      const D = ditchFrames(u.pos, u.idx, u.parts);
      let mesh;
      if (D && D.inner) {
        // 안쪽(세 점 모두 안 벽 줄 = 벽 안면 · 바닥 · 낙차벽) 세모를 뒤로 모아 어두운 재료
        const outT = [], inT = [];
        for (let i = 0; i + 2 < u.idx.length; i += 3) {
          const a = u.idx[i], b = u.idx[i + 1], c = u.idx[i + 2];
          (D.inner[a] && D.inner[b] && D.inner[c] ? inT : outT).push(a, b, c);
        }
        const g = mk.geom(u.pos, Uint32Array.from([...outT, ...inT]));
        g.addGroup(0, outT.length, 0);
        g.addGroup(outT.length, inT.length, 1);
        mesh = new THREE.Mesh(mk.flat(g), [matOf(color), matOf(darker(color))]);
      } else mesh = new THREE.Mesh(mk.flat(mk.geom(u.pos, u.idx)), matOf(color));
      if (D) {
        const chains = chainFrames(D.frames);
        let grated = false;
        for (const ch of chains) {
          grated = decorate(mesh, ch, info, matOf(darker(color)), u.grating === true) || grated;
          frames.push(mergeChain(ch));
        }
        if (grated) stats.grating++;
        stats.segs += D.frames.length;
      }
      mesh.castShadow = true; mesh.receiveShadow = true;
      mesh.userData = info;
      grp.add(mesh);
      n++;
    }
    // 집수정 — 네모 상자(콘크리트) + 윗면 그레이팅 · 지형 구멍
    for (const b of Array.isArray(u.basins) ? u.basins : []) {
      const x = +b.x, y = +b.y, zt = +b.zTop, zb = +b.zBottom, w = +b.w, l = +b.l, rot = +b.rot || 0;
      if (![x, y, zt, zb, w, l].every(Number.isFinite) || !(w > 0) || !(l > 0) || !(zt > zb)) continue;
      const box = new THREE.Mesh(mk.flat(new THREE.BoxGeometry(w, l, zt - zb)), matOf(UDITCH_COLOR));
      box.position.set(x, y, (zt + zb) / 2);
      box.rotation.z = rot;
      box.castShadow = true; box.receiveShadow = true;
      box.userData = {
        layer: 'uditch', name: b.name || '집수정', title: b.title || b.name || '집수정',
        rows: Array.isArray(b.rows) ? b.rows : [['크기', `${fx(w)} × ${fx(l)} m`], ['깊이', fx(zt - zb) + ' m']],
      };
      // 덮개 = 안치수(C# basins[*].inner — 정사각 · 있으면) · 없으면 벽 0.15(작은 집수정은 짧은 변의 20 %)
      const inner = +b.inner, mn = Math.min(w, l);
      const t = inner > 0 && inner < mn ? (mn - inner) / 2 : Math.min(0.15, 0.2 * mn), gw = w - 2 * t, gl = l - 2 * t;
      if (gw > 0.05 && gl > 0.05) {
        const pg = new THREE.PlaneGeometry(gw, gl);
        const uv = pg.attributes.uv;
        for (let i = 0; i < uv.count; i++) uv.setXY(i, uv.getX(i) * gw / TILE, uv.getY(i) * gl / TILE);
        const gm = new THREE.Mesh(pg, grateMaterial());
        gm.position.z = (zt - zb) / 2 + LIFT;
        gm.receiveShadow = true;
        gm.userData = { ...box.userData, deco: 'grating' };
        box.add(gm);
      }
      grp.add(box);
      basins.push({ x, y, w, l, rot, zTop: zt });
      stats.basins++;
    }
    // 관로 — 두 점(관 중심) 사이 원통 · ★[v117.16] 단차마다 곧은 토막 여럿(pipes 한 칸 = 한 토막) → 토막 끝이 맞닿는 자리에 이음 공(관 바깥 지름) — 꺾인 자리 틈 없음
    const ends = [];
    for (const p of Array.isArray(u.pipes) ? u.pipes : []) {
      const a = new THREE.Vector3(+p.x1, +p.y1, +p.z1), b = new THREE.Vector3(+p.x2, +p.y2, +p.z2), d = +p.d;
      if (![a.x, a.y, a.z, b.x, b.y, b.z, d].every(Number.isFinite) || !(d > 0)) continue;
      const len = a.distanceTo(b);
      if (len < 1e-3) continue;
      const m = new THREE.Mesh(new THREE.CylinderGeometry(d / 2, d / 2, len, 20, 1, false), pipeMaterial());
      m.position.copy(a).add(b).multiplyScalar(0.5);
      m.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), b.clone().sub(a).normalize());
      m.castShadow = true; m.receiveShadow = true;
      const slope = Math.hypot(b.x - a.x, b.y - a.y) > 1e-6 ? Math.abs(b.z - a.z) / Math.hypot(b.x - a.x, b.y - a.y) * 100 : NaN;
      m.userData = {
        layer: 'uditch', name: p.name || '관로', title: p.title || p.name || '관로',
        rows: Array.isArray(p.rows) ? p.rows : [['지름', Math.round(d * 1000) + ' mm'], ['길이', fx(len) + ' m'], ...(Number.isFinite(slope) ? [['경사', slope.toFixed(2) + ' %']] : [])],
      };
      grp.add(m);
      stats.pipes++;
      ends.push({ p: a, d, m }, { p: b, d, m });
    }
    // 맞닿은 토막 끝(5 mm 안) — 자리마다 공 하나(두 토막 중 굵은 지름 · 누르면 그 관로)
    const done = new Set();
    for (let i = 0; i < ends.length; i++) {
      if (done.has(i)) continue;
      let r = 0, hit = false;
      for (let j = i + 1; j < ends.length; j++) {
        if (ends[j].m === ends[i].m || done.has(j) || ends[i].p.distanceTo(ends[j].p) > 0.005) continue;
        done.add(j); hit = true; r = Math.max(r, ends[j].d / 2);
      }
      if (!hit) continue;
      const s = new THREE.Mesh(jointGeometry(), pipeMaterial());
      s.scale.setScalar(Math.max(r, ends[i].d / 2));
      s.position.copy(ends[i].p);
      s.castShadow = true; s.receiveShadow = true;
      s.userData = { ...ends[i].m.userData, deco: 'joint' };
      grp.add(s);
      stats.joints++;
    }
  }
  if (!grp.children.length) return;
  scene.add(grp);
  L.uditch = { obj: grp, label: 'U형 측구', on: true, n, stats };
  // 윗면 자리 — 측구 바깥 벽 선 · 집수정 네모(관로는 땅속이라 없음) → 스텐실 가림(지형은 안 자름)
  const shapes = holeShapes(frames, basins);
  stats.holes = shapes.length;
  const mask = shapes.length ? openingMask(shapes) : null;
  if (mask) {
    grp.add(mask);                       // 층을 끄면(원지반 보기 포함) 가림도 같이 꺼짐 → 원래 지형
    L.uditch.mask = mask;
    if (L.terrain) maskTerrain(L.terrain);
  }
}

// ★[JACK 1124] 스텐실 맨 위 비트(3D 자르기 section.js는 아래 7비트) — 측구 윗면 자리
export const MASK_BIT = 0x80;
const KEEP = THREE.KeepStencilOp, REP = THREE.ReplaceStencilOp;

// 윗면 자리 판(색 · 깊이 안 씀 · 지형보다 먼저 · 깊이 시험 없음) — 이 화소의 스텐실에 MASK_BIT
function openingMask(shapes) {
  const pos = [], idx = [];
  for (const sh of shapes) {
    const b = pos.length / 3;
    for (let i = 0; i < sh.pts.length / 2; i++) pos.push(sh.pts[2 * i], sh.pts[2 * i + 1], (sh.zt[i] ?? 0) + 0.01);
    for (const p of sh.polys) for (let k = 1; k + 1 < p.length; k++) idx.push(b + p[0], b + p[k], b + p[k + 1]);
  }
  if (!idx.length) return null;
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(new Float32Array(pos), 3));
  g.setIndex(idx);
  g.computeBoundingSphere();
  g.computeBoundingBox();
  const m = new THREE.MeshBasicMaterial({
    colorWrite: false, depthWrite: false, depthTest: false, side: THREE.DoubleSide,
    stencilWrite: true, stencilRef: MASK_BIT, stencilWriteMask: MASK_BIT, stencilFunc: THREE.AlwaysStencilFunc,
    stencilFail: REP, stencilZFail: REP, stencilZPass: REP,
  });
  const mesh = new THREE.Mesh(g, m);
  mesh.renderOrder = -10;
  mesh.castShadow = false; mesh.receiveShadow = false;
  mesh.raycast = () => {};
  mesh.userData = { deco: 'mask' };
  return mesh;
}

// 지형(구분 재료 · 절성토 색) · 차선 · 줄눈 — MASK_BIT가 선 화소는 안 그림(쓰지는 않음)
function maskTerrain(T) {
  const set = (m) => {
    if (!m) return;
    Object.assign(m, {
      stencilWrite: true, stencilWriteMask: 0x00, stencilRef: MASK_BIT, stencilFuncMask: MASK_BIT, stencilFunc: THREE.NotEqualStencilFunc,
      stencilFail: KEEP, stencilZFail: KEEP, stencilZPass: KEEP,
    });
    m.userData.uditchMask = true;
  };
  for (const m of T.mats || []) set(m);
  set(T.cfMat); set(T.laneMat); set(T.jointMat);
}

// ★[JACK 1124] 지형은 늘 원래 그대로 — 가림판은 층 묶음 안이라 층을 끄면 · 원지반 보기면 같이 꺼진다(layers.js applyTerrainMode가 부름 · 옛 이름 유지)
export function applyUDitchHole(L, groundMode) {
  const T = L.terrain;
  if (!T || !T.holeGeom) return;
  T.obj.geometry = T.fullGeom;
}

// 측구 꾸밈(낙차로 이어진 구간 한 줄 ch) — 구간마다 바닥 물 자국 · 그레이팅(고른 측구만) · 줄 양 끝 마구리 벽(안쪽 재료 · 낙차 자리는 물이 넘어가니 안 막음)
//   안 벽을 못 읽은 구간은 건너뜀 · 돌려줌 = 그레이팅을 그렸는가
function decorate(mesh, ch, info, innerMat, grating) {
  const lerp = (p, q, t) => [p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t];
  let grated = false;
  for (const F of ch) {
    if (!F.st[0].iL) continue;
    // 물 자국 — 바닥 가운데 안폭의 45 % · 바닥 + 1 cm
    const water = new THREE.Mesh(band(F.st, (s) => [lerp(s.c, s.iL, 0.45), lerp(s.c, s.iR, 0.45), 0, 0], (s) => s.floor + 0.01), waterMaterial());
    water.raycast = () => {};
    water.receiveShadow = true;
    water.userData = { deco: 'water' };
    mesh.add(water);
    if (!grating) continue;
    // 그레이팅 — 안폭 + 양쪽 받침(벽 두께의 40 % · 최대 3 cm) · 윗면 + 6 mm · uv = (길이, 옆) / 0.1 m
    const gm = new THREE.Mesh(band(F.st, (s) => {
      const tw = Math.hypot(s.L[0] - s.iL[0], s.L[1] - s.iL[1]), seat = Math.min(0.03, 0.4 * tw), f = tw > 1e-6 ? seat / tw : 0;
      const l = lerp(s.iL, s.L, f), r = lerp(s.iR, s.R, f);
      return [l, r, Math.hypot(l[0] - s.c[0], l[1] - s.c[1]), -Math.hypot(r[0] - s.c[0], r[1] - s.c[1])];
    }, (s) => s.top + LIFT, TILE), grateMaterial());
    gm.receiveShadow = true;
    gm.userData = { ...info, deco: 'grating' };
    mesh.add(gm);
    grated = true;
  }
  // 마구리 벽 — 줄 시점 · 종점 단면의 안쪽 홈(바닥 ~ 윗면)을 막는다(안 막으면 끝에서 지형 밑이 비쳐 보임)
  const ep = [], ei = [];
  for (const [s, dir] of [[ch[0].st[0], 1], [ch[ch.length - 1].st[ch[ch.length - 1].st.length - 1], -1]]) {
    if (!s.iL) continue;
    const b = ep.length / 3;
    ep.push(s.iL[0], s.iL[1], s.floor, s.iR[0], s.iR[1], s.floor, s.iR[0], s.iR[1], s.top, s.iL[0], s.iL[1], s.top);
    // 물길 안쪽(진행 방향 dir)으로 앞면
    if (dir > 0) ei.push(b, b + 2, b + 1, b, b + 3, b + 2); else ei.push(b, b + 1, b + 2, b, b + 2, b + 3);
  }
  if (ep.length) {
    const eg = new THREE.BufferGeometry();
    eg.setAttribute('position', new THREE.BufferAttribute(new Float32Array(ep), 3));
    eg.setIndex(ei);
    eg.computeBoundingSphere();
    const ends = new THREE.Mesh(eg, innerMat);
    ends.receiveShadow = true;
    ends.userData = { ...info, deco: 'ends' };
    mesh.add(ends);
  }
  return grated;
}

// 측점 띠 — pick(측점) = [왼 점, 오른 점, 왼 옆 거리, 오른 옆 거리] · zOf(측점) · uv = (가운데 길이, 옆 거리) / tile · 앞면 위
// ★[JACK 1123 «스틸 그레이팅 무늬 방향이 이상해»] 꺾이는 점의 단면은 비스듬해(두 변 사이 각) 한 구간이 사다리꼴이 된다 —
//   예전엔 왼 · 오른 점에 같은 길이(run)를 줘서 살이 대각선으로 눕고 세모 경계에서 «V»로 꺾였다.
//   이제 구간마다 꼭짓점을 따로 두고 uv = (구간 방향으로 잰 길이, 구간에 수직으로 잰 옆 거리) — 살은 늘 측구 축에 직각 · 꺾이는 자리만 이음매.
function band(st, pick, zOf, tile = 1) {
  const n = st.length, pos = [], uv = [], idx = [];
  const at = st.map((s) => ({ lr: pick(s), z: zOf(s) }));
  let run = 0;
  for (let k = 1; k < n; k++) {
    const a = st[k - 1].c, b = st[k].c;
    const dx = b[0] - a[0], dy = b[1] - a[1], len = Math.hypot(dx, dy);
    if (len < 1e-9) continue;
    const tx = dx / len, ty = dy / len, base = pos.length / 3;
    for (const q of [at[k - 1], at[k]]) {
      const [l, r] = q.lr;
      for (const p of [l, r]) {
        pos.push(p[0], p[1], q.z);
        const ox = p[0] - a[0], oy = p[1] - a[1];
        uv.push((run + ox * tx + oy * ty) / tile, (-ty * ox + tx * oy) / tile);   // 앞 = 길이 · 왼쪽 = +
      }
    }
    // base = 왼(k−1) · +1 = 오른(k−1) · +2 = 왼(k) · +3 = 오른(k) — 예전과 같은 앞면 차례
    idx.push(base + 1, base + 3, base + 2, base + 1, base + 2, base);
    run += len;
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(new Float32Array(pos), 3));
  g.setAttribute('uv', new THREE.BufferAttribute(new Float32Array(uv), 2));
  g.setIndex(idx);
  g.computeVertexNormals();
  g.computeBoundingSphere();
  return g;
}

let waterMat = null, grateMat = null, pipeMat = null, jointGeo = null;
// 관 이음 공(반지름 1 · 관 반지름으로 늘림 · 원통 20각과 비슷한 촘촘함)
function jointGeometry() {
  return jointGeo || (jointGeo = new THREE.SphereGeometry(1, 20, 12));
}
function waterMaterial() {
  return waterMat || (waterMat = new THREE.MeshStandardMaterial({ color: WATER, roughness: 0.18, metalness: 0 }));
}
function pipeMaterial() {
  return pipeMat || (pipeMat = new THREE.MeshStandardMaterial({ color: PIPE, roughness: 0.85 }));
}
// 스틸 그레이팅 — 그림(128 px = 0.1 m): 어두운 틈 · 주부재 셋(측구 너비 방향 · 33 mm 간격 · 윗면 밝게) · 가로대 하나(흐름 방향)
//   화면 없는 시험(node)은 그림 없이 어두운 쇠 한 색
function grateMaterial() {
  if (grateMat) return grateMat;
  const m = new THREE.MeshStandardMaterial({ color: '#ffffff', metalness: 0.55, roughness: 0.45 });
  if (typeof document !== 'undefined') {
    const N = 128, cv = document.createElement('canvas');
    cv.width = cv.height = N;
    const g = cv.getContext('2d');
    g.fillStyle = '#121416'; g.fillRect(0, 0, N, N);
    g.fillStyle = '#5f6367'; g.fillRect(0, N / 2 - 2, N, 4);
    for (let i = 0; i < 3; i++) {
      const x = Math.round(((i + 0.5) * N) / 3);
      g.fillStyle = '#7d8185'; g.fillRect(x - 3, 0, 6, N);
      g.fillStyle = '#b3b7ba'; g.fillRect(x - 1, 0, 2, N);
    }
    const t = new THREE.CanvasTexture(cv);
    t.wrapS = t.wrapT = THREE.RepeatWrapping;
    t.colorSpace = THREE.SRGBColorSpace;
    t.anisotropy = 8;
    m.map = t;
  } else m.color.set('#55595d');
  return (grateMat = m);
}

const fx = (v) => (Math.round(v * 100) / 100).toString();

// 고르기 풍선 — 제목 · 줄([이름, 값])
export function describeUDitch(u) {
  const rows = [];
  for (const r of u.rows || []) if (Array.isArray(r) && r.length >= 2) rows.push([String(r[0]), String(r[1])]);
  return { title: u.title || u.name || 'U형 측구', rows };
}

// 시험용(node) — 그레이팅 uv 방향
export { band as _bandForTest };
