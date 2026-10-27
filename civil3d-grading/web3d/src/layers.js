// 층 만들기 — 지형(구분 색 · 위성 · 절성토 색) · 원지반 · 터파기 · 옹벽 몸통 · 무늬(빗물 · 지적 · 측점은 extras.js).
// ★[계획 검토 중간 9] 법선은 가볍게 — 지형은 C#가 구분마다 점을 나눠 보내므로 색인 그대로 computeVertexNormals(구분 경계에서 꺾임) ·
//   옹벽 · 무늬는 flatShading(법선 없이 면마다).
import * as THREE from 'three';
import grassUrl from '../tex/grass.jpg';
import soilUrl from '../tex/soil.jpg';
import gravelUrl from '../tex/gravel.jpg';

const srgb = (hex) => new THREE.Color(hex);
const rgb = (a) => new THREE.Color().setRGB(a[0] / 255, a[1] / 255, a[2] / 255, THREE.SRGBColorSpace);

// 지형 구분 색(정지된 자리) — 원지반 그대로인 자리는 위성 · ★[JACK 1027 «잔디 텍스처 · 계획부지 바닥면 텍스처»] 사면 = 잔디 · 계획면 = 다진 흙 · 소단 = 쇄석(질감 · tile = 한 장이 덮는 m)
export const CLASS_STYLE = {
  ground: { color: '#9aa77f', label: '원지반' },
  plan: { color: '#ffffff', label: '계획면', tex: 'soil', tile: 6 },
  bench: { color: '#ffffff', label: '소단', tex: 'gravel', tile: 2.5 },
  slopeCut: { color: '#f2f6ea', label: '사면(절토)', tex: 'grass', tile: 4 },
  slopeFill: { color: '#ffffff', label: '사면(성토)', tex: 'grass', tile: 4 },
  wallZone: { color: '#b9b7af', label: '옹벽 자리' },
  wallFace: { color: '#b9b7af', label: '옹벽 면' },
  other: { color: '#d8c9b0', label: '정지', tex: 'soil', tile: 6 },
};
const TEX_URL = { grass: grassUrl, soil: soilUrl, gravel: gravelUrl };

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

// 땅 질감 — 실제 m 기준(uv2 = 로컬 x, y)으로 깔고, 같은 무늬가 줄지어 보이지 않게 큰 얼룩(같은 그림을 7배 크게 한 번 더 읽은 밝기)을 곱한다
function groundTexMaterial(view, key, tile, color) {
  const t = groundTex(view, key, tile);
  const m = new THREE.MeshStandardMaterial({ map: t, color: srgb(color) });
  m.onBeforeCompile = (sh) => {
    sh.fragmentShader = sh.fragmentShader.replace('#include <map_fragment>', [
      '#ifdef USE_MAP',
      // 같은 그림을 37° 돌리고 0.61배로 한 번 더 읽어 섞는다 — 한 장 크기마다 되풀이되는 띠가 안 보이게
      '\tvec2 ruv = mat2( 0.7986, 0.6018, -0.6018, 0.7986 ) * vMapUv * 0.61 + vec2( 0.17, 0.83 );',
      '\tvec4 texC = mix( texture2D( map, vMapUv ), texture2D( map, ruv ), 0.45 );',
      '\tvec4 bigC = texture2D( map, vMapUv * 0.137 + vec2( 0.31, 0.57 ) );',
      '\tfloat lum = dot( bigC.rgb, vec3( 0.333 ) );',
      '\tdiffuseColor *= texC * mix( 0.84, 1.14, smoothstep( 0.15, 0.6, lum ) );',
      '#endif',
    ].join('\n'));
  };
  m.customProgramCacheKey = () => 'dh3d-groundtex';
  return m;
}

// 옹벽 몸통 재료 색
const MAT_COLOR = {
  '콘크리트': '#c2bfb8', '버림': '#a09e97', '활동방지벽': '#b7b4ac', '블록': '#ada391', '기초패드': '#a09e97',
  '패널': '#cbc8c0', '기초': '#a09e97', '찰쌓기': '#918878', '메쌓기': '#918878', '뒤채움 콘크리트': '#aeaba4',
};

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
      else if (st.tex) m = groundTexMaterial(view, st.tex, st.tile, st.color);
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
    if (!matCache.has(key)) matCache.set(key, new THREE.MeshStandardMaterial({ color, flatShading: true }));
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
  for (const p of head.patterns || []) {
    if (!p.pos || !p.idx || p.idx.length === 0) continue;
    const g = flat(geom(p.pos, p.idx));
    const mesh = new THREE.Mesh(g, matOf('p:' + p.layer, rgb(p.color)));
    mesh.castShadow = true; mesh.receiveShadow = true;
    mesh.userData = { layer: 'pattern', wall: p.wall, pat: p.layer };
    patGrp.add(mesh);
  }
  if (wallGrp.children.length) { scene.add(wallGrp); L.wall = { obj: wallGrp, label: '옹벽', on: true }; }
  if (patGrp.children.length) { scene.add(patGrp); L.pattern = { obj: patGrp, label: '옹벽 무늬', on: true }; }

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
  // 원지반 보기에서는 옹벽 · 무늬도 없다(그때는 없던 것)
  if (L.wall) L.wall.obj.visible = wallOn;
  if (L.pattern) L.pattern.obj.visible = L.pattern.on && !groundMode;
}
