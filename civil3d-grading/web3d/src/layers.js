// 층 만들기 — 지형(구분 색 · 위성 · 절성토 색) · 원지반 · 터파기 · 옹벽 몸통 · 무늬(빗물 · 지적 · 측점은 extras.js).
// ★[계획 검토 중간 9] 법선은 가볍게 — 지형은 C#가 구분마다 점을 나눠 보내므로 색인 그대로 computeVertexNormals(구분 경계에서 꺾임) ·
//   옹벽 · 무늬는 flatShading(법선 없이 면마다).
import * as THREE from 'three';
import grassUrl from '../tex/grass.jpg';
import soilUrl from '../tex/soil.jpg';
import gravelUrl from '../tex/gravel.jpg';
import asphaltUrl from '../tex/asphalt.jpg';
import concreteUrl from '../tex/concrete.jpg';
// ★[JACK 1110 «만화스럽다»] 표면 지도(R 높이 · G 그늘 · Poly Haven CC0) — 법선 · 그늘 · 거칠기(groundtex.js)
import grassSUrl from '../tex/grass_s.jpg';
import soilSUrl from '../tex/soil_s.jpg';
import gravelSUrl from '../tex/gravel_s.jpg';
import asphaltSUrl from '../tex/asphalt_s.jpg';
import concreteSUrl from '../tex/concrete_s.jpg';
import { SURFACE, surfaceFromPixels, patchGround } from './groundtex.js';
import { faceStyle, pieceAttrs, faceUniforms, FACE, patchWallFace } from './wallface.js';
import { buildUDitch } from './uditch.js';

const srgb = (hex) => new THREE.Color(hex);
const rgb = (a) => new THREE.Color().setRGB(a[0] / 255, a[1] / 255, a[2] / 255, THREE.SRGBColorSpace);

// 지형 구분 색(정지된 자리) — 원지반 그대로인 자리는 위성 · ★[JACK 1027 «잔디 텍스처 · 계획부지 바닥면 텍스처»] 사면 = 잔디 · 계획면 = 다진 흙 · 소단 = 쇄석(질감 · tile = 한 장이 덮는 m)
export const CLASS_STYLE = {
  ground: { color: '#9aa77f', label: '원지반' },
  plan: { color: '#ffffff', label: '계획면', tex: 'soil', tile: 6 },
  bench: { color: '#ffffff', label: '소단', tex: 'gravel', tile: 2.5 },
  slopeCut: { color: '#f2f6ea', label: '사면(절토)', tex: 'grass', tile: 4 },
  slopeFill: { color: '#ffffff', label: '사면(성토)', tex: 'grass', tile: 4 },
  // ★[JACK 1111 «옹벽이 밋밋»] 3D 몸통이 없는 옹벽 자리 · 면(지형 세모)도 3D 옹벽과 같은 콘크리트 질감(상자 투영 · 한 장 3 m)
  wallZone: { color: '#b9b7af', label: '옹벽 자리', tex: 'concrete', tile: 3, box: true },
  wallFace: { color: '#b9b7af', label: '옹벽 면', tex: 'concrete', tile: 3, box: true },
  other: { color: '#d8c9b0', label: '정지', tex: 'soil', tile: 6 },
  // ★[계획_v111_8 6장 · JACK 1102] 진입도로 — 차로 = 아스팔트 · 길어깨 = 콘크리트(도로 사면 · 소단 · 옹벽 자리는 위 구분 그대로)
  road: { color: '#ffffff', label: '진입도로', tex: 'asphalt', tile: 4 },
  shoulder: { color: '#ffffff', label: '길어깨', tex: 'concrete', tile: 3 },
  // ★[v116.8 · JACK 1110 포장 종류] 콘크리트 포장 차로 — 콘크리트 질감 + 가로 줄눈(head.joints)
  roadConc: { color: '#ffffff', label: '진입도로(콘크리트)', tex: 'concrete', tile: 4 },
};
const TEX_URL = { grass: grassUrl, soil: soilUrl, gravel: gravelUrl, asphalt: asphaltUrl, concrete: concreteUrl };
const MEADOW_TILE = 5;   // ★[1110] 위성 없는 원지반 풀밭 — 잔디 그림 한 장이 덮는 m(사면 4 m보다 조금 크게)

// 땅 질감 한 장(m 기준 · uv2) — 시공 재생 격자도 같이 쓴다
export function groundTex(view, key, tile) {
  const t = new THREE.TextureLoader().load(TEX_URL[key], () => view.request());
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.repeat.set(1 / tile, 1 / tile);
  t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = view.renderer.capabilities.getMaxAnisotropy();
  t.channel = 2;
  return t;
}

// 표면 지도(질감마다 하나 · 같은 질감 재료끼리 함께) — 그림을 풀어 법선을 세는 동안은 평평 · 트인 1 화소, 다 되면 바꿔 끼운다(셰이더는 그대로)
const SURF_URL = { grass: grassSUrl, soil: soilSUrl, gravel: gravelSUrl, asphalt: asphaltSUrl, concrete: concreteSUrl };
const surfCache = new Map();
function surfaceTex(view, key) {
  if (surfCache.has(key)) return surfCache.get(key);
  const flat = new THREE.DataTexture(new Uint8Array([128, 128, 255, 128]), 1, 1);
  flat.needsUpdate = true;
  const uni = { value: flat };
  surfCache.set(key, uni);
  new THREE.ImageLoader().load(SURF_URL[key], (img) => {
    try {
      const w = img.naturalWidth || img.width, h = img.naturalHeight || img.height;
      const cv = document.createElement('canvas');
      cv.width = w; cv.height = h;
      const g = cv.getContext('2d', { willReadFrequently: true });
      g.drawImage(img, 0, 0);
      const t = new THREE.DataTexture(surfaceFromPixels(g.getImageData(0, 0, w, h).data, w, h, SURFACE[key].bump), w, h);
      t.wrapS = t.wrapT = THREE.RepeatWrapping;
      t.magFilter = THREE.LinearFilter;
      t.minFilter = THREE.LinearMipmapLinearFilter;
      t.generateMipmaps = true;
      t.anisotropy = view.renderer.capabilities.getMaxAnisotropy();
      t.needsUpdate = true;
      uni.value = t;
      view.request();
    } catch { /* 못 풀면 평평한 채(색 · 얼룩은 그대로) */ }
  });
  return uni;
}

// 땅 질감 — 실제 m 기준(uv2 = 로컬 x, y). ★[JACK 1110] 같은 그림 겹치기(대비가 줄고 지푸라기가 두 겹) 대신 잡음 띠로 자리를 옮겨 읽기 ·
//   m 단위 큰 얼룩(밝기 · 색) · 표면 지도로 법선 · 그늘 · 거칠기(groundtex.js — 재료 값은 SURFACE)
//   box = 옹벽 · 측구 콘크리트(uv 없음 · 평면 셰이딩) — 면 방향으로 세계 좌표를 눕혀 깐다 · 밝기는 재료 색 그대로
//   key = SURFACE 이름(그림은 P.src — 위성 없는 원지반 'meadow'는 잔디 그림) · extra = 재료 덤(양면 등)
function surfMaterial(view, key, tile, color, box = false, extra = {}) {
  const P = SURFACE[key], tk = P.src || key;
  const t = groundTex(view, tk, tile);
  const m = new THREE.MeshStandardMaterial(box ? { color: srgb(color), flatShading: true, ...extra } : { map: t, color: srgb(color), ...extra });
  m.defines = box ? { DH_GROUND: '', DH_BOX: '' } : { DH_GROUND: '' };
  const uni = {
    dhSurf: surfaceTex(view, tk), dhTile: { value: tile },
    dhMacro: { value: new THREE.Vector4(...P.macro, box ? 0.5 : 1) },   // w = 법선 세기(옹벽은 흙손 자국이 덜 도드라지게 반)
    dhTintA: { value: new THREE.Vector3(...P.a) }, dhTintB: { value: new THREE.Vector3(...P.b) },
    dhSurfP: { value: new THREE.Vector4(P.ao, P.rough[0], P.rough[1], P.detail) },
    dhSat: { value: P.sat ?? 1 },
  };
  if (box) uni.dhMap = { value: t };
  m.onBeforeCompile = (sh) => {
    Object.assign(sh.uniforms, uni);
    patchGround(sh);
  };
  m.customProgramCacheKey = () => (box ? 'dh3d-ground-box' : 'dh3d-ground');
  return m;
}

// 옹벽 몸통 재료 색
const MAT_COLOR = {
  '콘크리트': '#c2bfb8', '버림': '#a09e97', '활동방지벽': '#b7b4ac', '블록': '#ada391', '기초패드': '#a09e97',
  '패널': '#cbc8c0', '기초': '#a09e97', '찰쌓기': '#918878', '메쌓기': '#918878', '뒤채움 콘크리트': '#aeaba4',
};
// ★[JACK 1110] 콘크리트 질감을 입히는 재료(석축 찰쌓기 · 메쌓기는 돌이라 한 색 그대로) — 한 장 3 m(길어깨와 같음)
const CONCRETE_MATS = new Set(['콘크리트', '버림', '활동방지벽', '블록', '기초패드', '패널', '기초', '뒤채움 콘크리트']);

function geom(pos, idx) {
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  if (idx) g.setIndex(new THREE.BufferAttribute(idx, 1));
  return g;
}

function smooth(g) {
  g.computeVertexNormals();
  g.computeBoundingSphere();
  g.computeBoundingBox();
  return g;
}

function flat(g) {
  g.computeBoundingSphere();
  g.computeBoundingBox();
  return g;
}

// 위성 그림(Blob) → 질감(그래픽 카드 한도에 맞춰 줄임)
async function texture(view, blob) {
  const maxTex = Math.min(view.renderer.capabilities.maxTextureSize, 8192);
  let bmp = await createImageBitmap(blob);
  if (bmp.width > maxTex || bmp.height > maxTex) {
    const s = maxTex / Math.max(bmp.width, bmp.height);
    const w = Math.floor(bmp.width * s), h = Math.floor(bmp.height * s);
    bmp.close();
    bmp = await createImageBitmap(blob, { resizeWidth: w, resizeHeight: h, resizeQuality: 'high' });
  }
  const t = new THREE.Texture(bmp);
  t.flipY = false;                       // 그림 첫 줄 = 북쪽 끝 → v = (maxY − y)/h
  t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = view.renderer.capabilities.getMaxAnisotropy();
  t.wrapS = t.wrapT = THREE.ClampToEdgeWrapping;
  t.needsUpdate = true;
  return t;
}

export function uvFor(pos, ext) {
  const n = pos.length / 3, uv = new Float32Array(n * 2);
  const w = ext[2] - ext[0], h = ext[3] - ext[1];
  for (let i = 0; i < n; i++) { uv[i * 2] = (pos[i * 3] - ext[0]) / w; uv[i * 2 + 1] = (ext[3] - pos[i * 3 + 1]) / h; }
  return uv;
}

// ★[계획 검토 높음 1] 위성 두 장 — 바탕(지형 전체 · 흐림)은 map(uv1) · 자세히(바뀐 자리 + 50 m · 0.3 m/px)는 그 범위 안 화소에서만 덮는다
function satMaterial(base, detail) {
  const m = new THREE.MeshStandardMaterial({ map: base || detail });
  if (base) base.channel = 1;
  m.userData.sat = m.map;
  if (base && detail) {
    m.onBeforeCompile = (sh) => {
      sh.uniforms.detailMap = { value: detail };
      sh.vertexShader = 'attribute vec2 uvd;\nvarying vec2 vDetailUv;\n' + sh.vertexShader.replace('#include <uv_vertex>', '#include <uv_vertex>\n\tvDetailUv = uvd;');
      sh.fragmentShader = 'uniform sampler2D detailMap;\nvarying vec2 vDetailUv;\n' + sh.fragmentShader.replace('#include <map_fragment>', [
        '#ifdef USE_MAP',
        '\tvec4 baseC = texture2D( map, vMapUv );',
        '\tvec4 detC = texture2D( detailMap, clamp( vDetailUv, 0.0, 1.0 ) );',
        '\tfloat inside = step( 0.0, vDetailUv.x ) * step( vDetailUv.x, 1.0 ) * step( 0.0, vDetailUv.y ) * step( vDetailUv.y, 1.0 );',
        '\tdiffuseColor *= mix( baseC, detC, inside );',
        '#endif',
      ].join('\n'));
    };
    m.customProgramCacheKey = () => 'dh3d-sat2';
  }
  return m;
}

// 절성토 깊이 → 색(절토 빨강 · 성토 파랑 · 0 근처 흰색 · 원지반 없음 회색)
function cutFillColor(d, lim, out) {
  if (!Number.isFinite(d)) { out.setRGB(0.55, 0.55, 0.55); return; }
  const k = Math.max(-1, Math.min(1, d / lim));
  const w = new THREE.Color(0.96, 0.96, 0.94);
  const cut = new THREE.Color().setRGB(0.80, 0.16, 0.12, THREE.SRGBColorSpace);
  const fill = new THREE.Color().setRGB(0.15, 0.36, 0.80, THREE.SRGBColorSpace);
  out.copy(w).lerp(k < 0 ? cut : fill, Math.pow(Math.abs(k), 0.8));
}

export async function buildLayers(view, head) {
  const L = {};   // id → { obj, label, on, kind, … }
  const scene = view.scene;

  // ── 위성 그림(바탕 · 자세히) ──
  let base = null, detail = null, baseExt = null, detailExt = null;
  const S = head.sat || {};
  for (const [k, v] of [['base', S.base], ['detail', S.detail]]) {
    if (!v || !v.img) continue;
    try {
      const t = await texture(view, v.img);
      if (k === 'base') { base = t; baseExt = v.ext; } else { detail = t; detailExt = v.ext; }
    } catch (e) {
      head.warn = (head.warn || []).concat('위성 그림을 못 읽었습니다 — ' + e.message);
    }
  }
  const satTex = base || detail;
  L.$sat = { base, detail, baseExt, detailExt };   // 층 아님(목록에 안 나옴) — 시공 재생 격자가 같은 위성을 쓴다

  // ── 지형(정지면) ──
  const T = head.terrain;
  if (T && T.pos && T.idx) {
    const g = geom(T.pos, T.idx);
    const nV = T.pos.length / 3;
    // 바탕만 · 자세히만 · 둘 다 — 바탕은 uv1(channel 1) · 자세히는 둘 다면 uvd(셰이더) · 하나뿐이면 그것이 uv
    if (base) g.setAttribute('uv1', new THREE.BufferAttribute(uvFor(T.pos, baseExt), 2));
    if (detail && base) g.setAttribute('uvd', new THREE.BufferAttribute(uvFor(T.pos, detailExt), 2));
    if (detail && !base) g.setAttribute('uv', new THREE.BufferAttribute(uvFor(T.pos, detailExt), 2));
    // 땅 질감용 — 로컬 m(질감마다 repeat로 한 장 크기)
    const uvw = new Float32Array(nV * 2);
    for (let i = 0; i < nV; i++) { uvw[i * 2] = T.pos[i * 3]; uvw[i * 2 + 1] = T.pos[i * 3 + 1]; }
    g.setAttribute('uv2', new THREE.BufferAttribute(uvw, 2));
    // 절성토 깊이 색(원지반이 있을 때)
    let cfLim = 0;
    if (T.cf) {
      const abs = [];
      for (let i = 0; i < T.cf.length; i += Math.max(1, Math.floor(T.cf.length / 20000))) if (Number.isFinite(T.cf[i]) && Math.abs(T.cf[i]) > 0.01) abs.push(Math.abs(T.cf[i]));
      abs.sort((a, b) => a - b);
      cfLim = abs.length ? Math.max(0.5, abs[Math.floor(abs.length * 0.98)]) : 1;
      const col = new Float32Array(nV * 3), c = new THREE.Color();
      for (let i = 0; i < nV; i++) { cutFillColor(T.cf[i], cfLim, c); col[i * 3] = c.r; col[i * 3 + 1] = c.g; col[i * 3 + 2] = c.b; }
      g.setAttribute('color', new THREE.BufferAttribute(col, 3));
    }
    // 구분마다 묶음(C#가 삼각형을 구분 차례로 정렬해 보냄)
    const mats = [];
    const classOf = [];
    for (const [ci, start, count] of T.ranges) {
      const cls = T.classes[ci];
      const st = CLASS_STYLE[cls.key] || CLASS_STYLE.other;
      let m;
      if (cls.key === 'ground' && satTex) m = satMaterial(base, detail);
      else if (cls.key === 'ground') m = surfMaterial(view, 'meadow', MEADOW_TILE, '#ffffff');   // ★[1110] 위성이 없을 때 — 한 색 대신 풀밭(위성 길은 그대로)
      else if (st.tex) m = surfMaterial(view, st.tex, st.tile, st.color, !!st.box);
      else m = new THREE.MeshStandardMaterial({ color: srgb(st.color) });
      m.userData.key = cls.key;
      g.addGroup(start * 3, count * 3, mats.length);
      classOf.push({ start, count, key: cls.key, name: cls.name || st.label });
      mats.push(m);
    }
    const tg = smooth(g);
    const mesh = new THREE.Mesh(tg, mats);
    mesh.receiveShadow = true;
    mesh.userData = { layer: 'terrain', classOf };
    scene.add(mesh);
    const cfMat = T.cf ? new THREE.MeshStandardMaterial({ vertexColors: true }) : null;
    L.terrain = { obj: mesh, label: '지형', on: true, mats, cfMat, cfLim, classOf };
    // ★[계획_v111_8 6장 · JACK Q1 · Q5] 흰 차선(차로 끝 안 0.1 m · 폭 0.1 m — Civil이 설계에서 띠로 보냄) · 지형 층에 붙임(단추 없음) · 누르면 아래 지형
    const N = head.lanes;
    if (N && N.pos && N.idx && N.idx.length) {
      const laneMat = new THREE.MeshStandardMaterial({ color: srgb('#f2f2ee'), roughness: 0.6, side: THREE.DoubleSide, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2 });
      const lanes = new THREE.Mesh(smooth(geom(N.pos, N.idx)), laneMat);
      lanes.receiveShadow = true;
      lanes.raycast = () => {};
      lanes.userData = { layer: 'lanes' };
      mesh.add(lanes);
      L.terrain.lanes = lanes;
      L.terrain.laneMat = laneMat;
    }
    // ★[v116.8 · JACK 1110 «콘크리트 질감 + 가로 줄눈»] 콘크리트 도로 줄눈(폭 3 cm · 지형 + 3 cm — Civil이 설계에서 띠로 보냄) · 차선(−2)이 위 · 누르기 없음 · 옛 장면(joints 없음) = 그대로
    const J = head.joints;
    if (J && J.pos && J.idx && J.idx.length) {
      const jointMat = new THREE.MeshStandardMaterial({ color: srgb('#6e6c67'), roughness: 0.9, side: THREE.DoubleSide, polygonOffset: true, polygonOffsetFactor: -1, polygonOffsetUnits: -1 });
      const joints = new THREE.Mesh(smooth(geom(J.pos, J.idx)), jointMat);
      joints.receiveShadow = true;
      joints.raycast = () => {};
      joints.userData = { layer: 'joints' };
      mesh.add(joints);
      L.terrain.joints = joints;
      L.terrain.jointMat = jointMat;
    }
    if (satTex) L.sat = { label: '위성', on: true, virtual: true };
    if (cfMat) L.cutfill = { label: '절성토 깊이', on: false, virtual: true };
  }

  // ── 원지반 · 터파기 ──
  //   ★[JACK 1027 «원지반을 누르면 위성지도가 덮어진 아예 최초 원지반»] 원지반은 «보기 바꿈» — 켜면 설계(정지 구분 · 옹벽 · 무늬)를 숨기고
  //     바뀐 자리의 원래 땅을 위성으로 덮어 보인다(그 밖은 지형의 원지반 그대로 자리가 그대로 이어진다).
  //   ★원지반은 그릴 때만 5 cm 내린다 — 바뀐 자리 둘레에서 지형과 같은 높이라 겹쳐 얼룩이 진다(누르면 정보는 되더해 원래 높이)
  for (const [id, label, src, color, zShift] of [
    ['ground', '원지반', head.ground, '#9aa77f', 0.05],
    ['excav', '터파기', head.excav, '#9b7b52', 0],
  ]) {
    if (!src || !src.pos || !src.idx) continue;
    if (zShift) for (let i = 2; i < src.pos.length; i += 3) src.pos[i] -= zShift;
    const g = geom(src.pos, src.idx);
    let m;
    if (id === 'ground' && satTex) {
      if (base) g.setAttribute('uv1', new THREE.BufferAttribute(uvFor(src.pos, baseExt), 2));
      if (detail && base) g.setAttribute('uvd', new THREE.BufferAttribute(uvFor(src.pos, detailExt), 2));
      if (detail && !base) g.setAttribute('uv', new THREE.BufferAttribute(uvFor(src.pos, detailExt), 2));
      m = satMaterial(base, detail);
    } else if (id === 'ground') {
      // ★[1110] 위성이 없을 때 원지반 보기도 지형의 원지반과 같은 풀밭(uv2 = 로컬 m)
      const n = src.pos.length / 3, uvw = new Float32Array(n * 2);
      for (let i = 0; i < n; i++) { uvw[i * 2] = src.pos[i * 3]; uvw[i * 2 + 1] = src.pos[i * 3 + 1]; }
      g.setAttribute('uv2', new THREE.BufferAttribute(uvw, 2));
      m = surfMaterial(view, 'meadow', MEADOW_TILE, '#ffffff', false, { side: THREE.DoubleSide });
    } else m = new THREE.MeshStandardMaterial({ color: srgb(color), side: THREE.DoubleSide });
    const mesh = new THREE.Mesh(smooth(g), m);
    mesh.visible = false;
    mesh.receiveShadow = true;
    mesh.userData = { layer: id, zShift };
    scene.add(mesh);
    L[id] = { obj: mesh, label, on: false };
  }

  // ── 옹벽 몸통 · 무늬 ──
  const wallGrp = new THREE.Group(), patGrp = new THREE.Group();
  const matCache = new Map();
  const matOf = (key, color) => {
    if (!matCache.has(key)) {
      // ★[JACK 1110] 옹벽 · 측구 콘크리트는 콘크리트 질감(상자 투영) · 무늬 · 석축은 한 색 그대로
      const conc = key.startsWith('m:') && CONCRETE_MATS.has(key.slice(2));
      matCache.set(key, conc ? surfMaterial(view, 'concrete', 3, '#' + color.getHexString(), true) : new THREE.MeshStandardMaterial({ color, flatShading: true }));
    }
    return matCache.get(key);
  };
  for (const w of head.walls || []) {
    for (const p of w.parts || []) {
      if (!p.pos || !p.idx || p.idx.length === 0) continue;
      const g = flat(geom(p.pos, p.idx));
      const mesh = new THREE.Mesh(g, matOf('m:' + p.mat, srgb(MAT_COLOR[p.mat] || '#b4b4b4')));
      mesh.castShadow = true; mesh.receiveShadow = true;
      mesh.userData = { layer: 'wall', wall: w.id, mat: p.mat };
      wallGrp.add(mesh);
    }
  }
  // ★[JACK 1111 «옹벽 표면을 옹벽처럼»] 무늬 좌표(fuv)가 있으면 조각마다 색 얼룩 · 모서리 깎임 · 줄눈 그늘(wallface.js) — 없으면(옛 장면 · 모르는 층) 옛 한 색
  const faceMats = new Map();
  const faceMat = (style, color) => {
    const key = style + ':' + color.getHexString();
    if (!faceMats.has(key)) {
      const m = new THREE.MeshStandardMaterial({ color, flatShading: true, vertexColors: true, roughness: FACE[style].rough });
      const u = faceUniforms(style);
      const uni = { wfA: { value: new THREE.Vector4(...u.wfA) }, wfB: { value: new THREE.Vector4(...u.wfB) } };
      m.onBeforeCompile = (sh) => { Object.assign(sh.uniforms, uni); patchWallFace(sh); };
      m.customProgramCacheKey = () => 'dh3d-wallface';
      faceMats.set(key, m);
    }
    return faceMats.get(key);
  };
  for (const p of head.patterns || []) {
    if (!p.pos || !p.idx || p.idx.length === 0) continue;
    const g = flat(geom(p.pos, p.idx));
    const style = p.fuv && p.fuv.length === p.pos.length ? faceStyle(p.layer) : null;
    let pm, pieces = 0;
    if (style) {
      const a = pieceAttrs(p.idx, p.fuv, style, p.wall | 0);
      g.setAttribute('fuv', new THREE.BufferAttribute(p.fuv, 3));
      g.setAttribute('fpc', new THREE.BufferAttribute(a.fpc, 4));
      g.setAttribute('color', new THREE.BufferAttribute(a.col, 3));
      pm = faceMat(style, rgb(p.color));
      pieces = a.pieces;
    } else pm = matOf('p:' + p.layer, rgb(p.color));
    const mesh = new THREE.Mesh(g, pm);
    mesh.castShadow = true; mesh.receiveShadow = true;
    mesh.userData = { layer: 'pattern', wall: p.wall, pat: p.layer, face: style, pieces };
    patGrp.add(mesh);
  }
  if (wallGrp.children.length) { scene.add(wallGrp); L.wall = { obj: wallGrp, label: '옹벽', on: true, fixed: true }; }   // ★[JACK 1028] 옹벽은 늘 보임 — 층 목록에 단추 없음
  if (patGrp.children.length) { scene.add(patGrp); L.pattern = { obj: patGrp, label: '옹벽 무늬', on: true }; }

  // ── ★[JACK 1111 «3D 뷰에서 불러온 구조물이 안 보여»] 불러온 구조물 — 구조물마다 메시 하나 · 매스콘크리트(옹벽과 같은 콘크리트 질감 · 밝은 회색) ·
  //   양면(열린 메시도 구멍 없이) · 그림자 · 층 «구조물»(없는 장면은 안 생김)
  if (head.structs && head.structs.length) {
    const grp = new THREE.Group();
    const m = surfMaterial(view, 'concrete', 3, '#b9b5ad', true, { side: THREE.DoubleSide });
    for (const s of head.structs) {
      if (!s.pos || !s.idx || s.idx.length === 0) continue;
      const mesh = new THREE.Mesh(flat(geom(s.pos, s.idx)), m);
      mesh.castShadow = true; mesh.receiveShadow = true;
      mesh.userData = { layer: 'struct', name: s.name };
      grp.add(mesh);
    }
    if (grp.children.length) { scene.add(grp); L.struct = { obj: grp, label: '구조물', on: true, n: grp.children.length }; }
  }

  // ── ★[도로 마무리 ② · 계획 6.6] 진입도로 측구 — U형 그레이팅(어두운 격자 띠) · 물길(뚜껑 없음) · L형 경계석(콘크리트) · 층 «측구»(없는 장면은 안 생김)
  const DT = head.ditches;
  if (DT) {
    const grp = new THREE.Group();
    for (const [key, color, mat] of [['grate', '#3b3d40', null], ['water', '#2d5470', null], ['curb', null, '콘크리트']]) {
      const s = DT[key];
      if (!s || !s.pos || !s.idx || s.idx.length === 0) continue;
      const m = mat ? matOf('m:' + mat, srgb(MAT_COLOR[mat] || '#b4b4b4'))
                    : new THREE.MeshStandardMaterial({ color: srgb(color), roughness: 0.8, side: THREE.DoubleSide, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2 });
      const mesh = new THREE.Mesh(mat ? flat(geom(s.pos, s.idx)) : smooth(geom(s.pos, s.idx)), m);
      mesh.castShadow = !!mat; mesh.receiveShadow = true;
      mesh.userData = { layer: 'ditch', part: key };
      grp.add(mesh);
    }
    if (grp.children.length) { scene.add(grp); L.ditch = { obj: grp, label: '측구', on: true }; }
  }

  // ── ★[배수지 U형 측구] 부지 U형 측구(uditch.js) — 없는 장면은 안 생김
  buildUDitch(head, scene, L, { material: (c) => surfMaterial(view, 'concrete', 3, c, true, { side: THREE.DoubleSide }), geom, flat });

  applyTerrainMode(L);
  return L;
}

// 지형 칠 — 원지반 보기(설계를 숨기고 원래 땅) · 절성토 색(바뀐 자리만) · 구분 재료(위성 켬/끔 · 옹벽이 보이면 옹벽 면 숨김)
export function applyTerrainMode(L) {
  const T = L.terrain;
  if (!T) return;
  const satOn = !L.sat || L.sat.on;
  const groundMode = !!(L.ground && L.ground.on);
  const wallOn = L.wall && L.wall.on && !groundMode;
  // 절성토 색은 바뀐 자리만 — 원지반 그대로인 자리는 위성(또는 지형 색) 그대로
  const cf = !groundMode && L.cutfill && L.cutfill.on && T.cfMat;
  T.obj.material = cf ? T.mats.map(m => (m.userData.key === 'ground' || m.userData.key === 'wallFace' ? m : T.cfMat)) : T.mats;
  const satToggle = (m) => {
    if (!m || !m.userData.sat) return;
    m.map = satOn ? m.userData.sat : null;
    m.color.set(satOn ? 0xffffff : CLASS_STYLE.ground.color);
    m.needsUpdate = true;
  };
  for (const m of T.mats) {
    const key = m.userData.key;
    if (key === 'ground') satToggle(m);
    m.visible = groundMode ? key === 'ground' : key === 'wallFace' ? !wallOn : true;
  }
  if (L.ground) satToggle(L.ground.obj.material);
  if (T.lanes) T.lanes.visible = !groundMode;                          // 차선은 설계 것 — 원지반 보기에서 숨김
  if (T.joints) T.joints.visible = !groundMode;                        // ★[v116.8] 줄눈도
  if (L.ditch) L.ditch.obj.visible = L.ditch.on && !groundMode;         // ★[도로 마무리 ②] 측구도 설계 것
  if (L.uditch) L.uditch.obj.visible = L.uditch.on && !groundMode;      // ★[배수지 U형 측구] 부지 측구도
  // 원지반 보기에서는 옹벽 · 무늬도 없다(그때는 없던 것)
  if (L.wall) L.wall.obj.visible = wallOn;
  if (L.pattern) L.pattern.obj.visible = L.pattern.on && !groundMode;
  if (L.slope) L.slope.obj.visible = L.slope.on && !groundMode;       // 경사 화살표는 정지면 것
}
