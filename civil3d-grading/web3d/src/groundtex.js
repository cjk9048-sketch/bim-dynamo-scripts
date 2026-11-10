// ★[JACK 1110 «아스팔트 · 잔디 · 콘크리트도 수목처럼 현실감 있게 · 너무 만화스럽다»] 바닥 · 옹벽 질감 — 표면 지도 · 셰이더 조각(three 없이 시험되는 순수 함수).
//   왜 만화 같았나(1110 실측 · tex/README.md): ① 색 그림만 — 법선 · 거칠기 없음(땅이 인쇄한 종이처럼 빛을 받음) ② 멀리서 한 색 — 질감 그림의 큰 얼룩이
//   거의 없어(16 칸 평균 밝기 표준편차 0.4~3/255) 50 m 밖은 구분마다 한 색 페인트 ③ 되풀이 감추기로 같은 그림을 45 % 겹쳐 대비가 줄고 지푸라기가 두 겹(유령)으로 보임
//   · 하늘에서는 계획면 흙 무늬가 격자로 되풀이 ④ 옹벽 · 측구 콘크리트는 질감 없는 한 색.
//   고침: 표면 지도(tex/*_s.jpg = R 높이 · G 그늘 · Poly Haven CC0)로 법선 · 그늘 · 거칠기 / 겹치기 대신 잡음 띠로 두 자리를 옮겨 가며 읽기(대비 그대로) /
//   m 단위 큰 얼룩 두 크기(밝기 · 색 — 잔디는 푸른 · 마른 · 아스팔트는 새 · 닳은) / 옹벽은 면 방향으로 세계 좌표를 눕혀 깐 콘크리트(상자 투영) /
//   위성이 없을 때(못 받음 · 시험 키트) 원지반 = 옛 한 색 대신 채도 낮춘 잔디 풀밭(meadow) — 위성이 있으면 위성 그대로(안 바뀜).

// 표면 지도 그림(RGBA · 첫 줄 = 그림 맨 위) → GPU용 RGBA(첫 줄 = v 0 = 그림 맨 아래 — 색 그림(flipY)과 같은 자리)
//   R, G = 법선 x, y(0.5 = 0 · x = u+ 쪽 · y = v+ 쪽) · B = 그늘(1 = 트임) · A = 높이(0.5 = 평균)
//   bump = 법선 기울기 제곱평균(tan) — 그림마다 높이 폭이 달라 기울기를 이 값으로 맞춘다 · 가장자리는 이어 붙여 셈(이음매 없음)
export function surfaceFromPixels(src, w, h, bump) {
  const H = new Float32Array(w * h), out = new Uint8Array(w * h * 4);
  for (let r = 0; r < h; r++) for (let x = 0; x < w; x++) H[r * w + x] = src[((h - 1 - r) * w + x) * 4] / 255;
  const at = (x, r) => H[((r + h) % h) * w + ((x + w) % w)];
  const gx = new Float32Array(w * h), gy = new Float32Array(w * h);
  let ss = 0;
  for (let r = 0; r < h; r++) for (let x = 0; x < w; x++) {
    // 소벨 — 위(v+) · 오른쪽(u+)
    const a = (at(x + 1, r - 1) + 2 * at(x + 1, r) + at(x + 1, r + 1)) - (at(x - 1, r - 1) + 2 * at(x - 1, r) + at(x - 1, r + 1));
    const b = (at(x - 1, r + 1) + 2 * at(x, r + 1) + at(x + 1, r + 1)) - (at(x - 1, r - 1) + 2 * at(x, r - 1) + at(x + 1, r - 1));
    const k = r * w + x;
    gx[k] = a / 8; gy[k] = b / 8;
    ss += gx[k] * gx[k] + gy[k] * gy[k];
  }
  const s = bump / Math.max(1e-9, Math.sqrt(ss / (w * h)));
  for (let r = 0; r < h; r++) for (let x = 0; x < w; x++) {
    const k = r * w + x, i = ((h - 1 - r) * w + x) * 4;
    const nx = -gx[k] * s, ny = -gy[k] * s, l = 1 / Math.sqrt(nx * nx + ny * ny + 1);
    out[k * 4] = Math.round((nx * l * 0.5 + 0.5) * 255);
    out[k * 4 + 1] = Math.round((ny * l * 0.5 + 0.5) * 255);
    out[k * 4 + 2] = src[i + 1];
    out[k * 4 + 3] = src[i];
  }
  return out;
}

// 재료마다 — macro: [밝기 폭 · 작은 얼룩 m · 큰 얼룩 m] · a/b: 얼룩 양끝 색 곱(선형) · rough: [바탕 거칠기 · 높낮이로 바뀌는 폭(높은 곳 매끈)] ·
//   ao: 그늘 세기 · bump: 법선 기울기(surfaceFromPixels) · detail: 잔무늬 대비(그림 평균 둘레로 늘림 — 아스팔트 골재가 보이게) ·
//   sat: 채도(없으면 1) · src: 쓰는 그림 이름(없으면 제 이름 — tex/<이름>.jpg · <이름>_s.jpg)
export const SURFACE = {
  grass: { macro: [0.20, 5, 24], a: [0.72, 0.97, 0.60], b: [1.08, 1.04, 0.80], rough: [0.93, 0.06], ao: 0.85, bump: 0.55, detail: 1 },
  soil: { macro: [0.12, 5, 23], a: [0.84, 0.82, 0.82], b: [1.08, 1.06, 1.02], rough: [0.92, 0.08], ao: 0.7, bump: 0.45, detail: 1 },
  gravel: { macro: [0.10, 3, 15], a: [0.92, 0.92, 0.94], b: [1.08, 1.04, 0.96], rough: [0.86, 0.12], ao: 0.9, bump: 0.6, detail: 1 },
  asphalt: { macro: [0.05, 4, 22], a: [0.92, 0.92, 0.93], b: [1.07, 1.06, 1.05], rough: [0.76, 0.16], ao: 0.8, bump: 0.35, detail: 1.7 },
  concrete: { macro: [0.08, 1.6, 8], a: [0.93, 0.91, 0.87], b: [1.05, 1.05, 1.05], rough: [0.80, 0.10], ao: 0.6, bump: 0.3, detail: 1 },
  // ★[1110] 위성이 없을 때 원지반(옛 한 색 #9aa77f) — 잔디 그림을 채도를 낮춰(sat) 풀밭처럼 · 얼룩은 크게(밭 · 풀밭 · 맨땅) · 표면 지도는 잔디 것(src)
  meadow: { src: 'grass', macro: [0.18, 9, 70], a: [0.78, 1.0, 0.88], b: [1.08, 1.05, 0.95], rough: [0.94, 0.05], ao: 0.7, bump: 0.55, detail: 1, sat: 0.8 },
};

// 셰이더 머리 — 재료 값 · 값 잡음(해시 · 텍스처 없음). DH_BOX(옹벽) = 색 그림도 dhMap(재료 map을 안 씀 — uv가 없는 모양)
const HEAD = [
  'uniform sampler2D dhSurf;', 'uniform float dhTile;', 'uniform vec4 dhMacro;', 'uniform vec3 dhTintA;', 'uniform vec3 dhTintB;', 'uniform vec4 dhSurfP;', 'uniform float dhSat;',
  '#ifdef DH_BOX', 'uniform sampler2D dhMap;', 'varying vec3 vDhW;', '#define DH_ALB dhMap', '#else', '#define DH_ALB map', '#endif',
  'float dhHash( vec2 p ) { vec3 q = fract( vec3( p.xyx ) * 0.1031 ); q += dot( q, q.yzx + 33.33 ); return fract( ( q.x + q.y ) * q.z ); }',
  'float dhNoise( vec2 p ) { vec2 i = floor( p ), f = fract( p ), u = f * f * ( 3.0 - 2.0 * f );',
  '\treturn mix( mix( dhHash( i ), dhHash( i + vec2( 1.0, 0.0 ) ), u.x ), mix( dhHash( i + vec2( 0.0, 1.0 ) ), dhHash( i + vec2( 1.0, 1.0 ) ), u.x ), u.y ); }',
  // 화소 하나가 잡음 한 칸에 가까워지면(멀리) 평균 0.5로 — 먼 땅이 자글자글 끓지 않게
  'float dhNoiseAA( vec2 p, float fw ) { return mix( dhNoise( p ), 0.5, smoothstep( 0.3, 0.9, fw ) ); }',
].join('\n');

// 색 — 자리(uv · 접선 축) → 되풀이 감추기(잡음 띠마다 다른 자리를 읽고 띠 사이만 섞음 · 미분은 원래 uv로 — 띠 경계에 밉맵 줄이 안 생기게) + m 단위 큰 얼룩
//   땅: uv = vMapUv(세계 x, y ÷ 한 장 m) · 접선 = 세계 x, y
//   옹벽(DH_BOX): 면(평면 셰이딩 — 세모마다 평면)이 가장 많이 향한 축을 빼고 남은 두 세계 축으로 깐다 · 선 면은 얼룩을 세로로 늘임(빗물 자국처럼) · 밝기는 재료 색 그대로(그림 평균으로 나눔)
const MAP = [
  '#ifdef DH_GROUND',
  '#ifdef DH_BOX',
  '\tvec3 dhFn = abs( cross( dFdx( vDhW ), dFdy( vDhW ) ) );',
  '\tvec3 dhWx = vec3( 1.0, 0.0, 0.0 ), dhWy = vec3( 0.0, 1.0, 0.0 );',
  '\tif ( dhFn.x >= dhFn.y && dhFn.x >= dhFn.z ) { dhWx = vec3( 0.0, 1.0, 0.0 ); dhWy = vec3( 0.0, 0.0, 1.0 ); }',
  '\telse if ( dhFn.y >= dhFn.z ) { dhWx = vec3( 1.0, 0.0, 0.0 ); dhWy = vec3( 0.0, 0.0, 1.0 ); }',
  '\tvec2 dhUv = vec2( dot( vDhW, dhWx ), dot( vDhW, dhWy ) ) / dhTile;',
  '\tvec2 dhDx = vec2( dot( dFdx( vDhW ), dhWx ), dot( dFdx( vDhW ), dhWy ) ) / dhTile, dhDy = vec2( dot( dFdy( vDhW ), dhWx ), dot( dFdy( vDhW ), dhWy ) ) / dhTile;',
  '\tvec2 dhP = dhUv * dhTile * ( dhWy.z > 0.5 ? vec2( 1.0, 0.3 ) : vec2( 1.0 ) );',
  '#else',
  '\tvec3 dhWx = vec3( 1.0, 0.0, 0.0 ), dhWy = vec3( 0.0, 1.0, 0.0 );',
  '\tvec2 dhUv = vMapUv, dhDx = dFdx( dhUv ), dhDy = dFdy( dhUv );',
  '\tvec2 dhP = dhUv * dhTile;',
  '#endif',
  '\tfloat dhK = dhNoise( dhUv * 1.3 ) * 8.0, dhI = floor( dhK ), dhF = dhK - dhI;',
  '\tvec2 dhOa = sin( vec2( 3.0, 7.0 ) * dhI ), dhOb = sin( vec2( 3.0, 7.0 ) * ( dhI + 1.0 ) );',
  '\tvec4 dhCa = textureGrad( DH_ALB, dhUv + dhOa, dhDx, dhDy ), dhCb = textureGrad( DH_ALB, dhUv + dhOb, dhDx, dhDy );',
  '\tfloat dhW = smoothstep( 0.2, 0.8, dhF - 0.1 * dot( dhCa.rgb - dhCb.rgb, vec3( 1.0 ) ) );',
  '\tvec4 texC = mix( dhCa, dhCb, dhW );',
  '\tvec3 dhAvg = textureLod( DH_ALB, vec2( 0.5 ), 16.0 ).rgb;',
  '\ttexC.rgb = max( dhAvg + ( texC.rgb - dhAvg ) * dhSurfP.w, 0.0 );',
  '\ttexC.rgb = mix( vec3( dot( texC.rgb, vec3( 0.2126, 0.7152, 0.0722 ) ) ), texC.rgb, dhSat );',
  '#ifdef DH_BOX',
  '\ttexC.rgb /= max( dot( dhAvg, vec3( 0.2126, 0.7152, 0.0722 ) ), 0.02 );',
  '#endif',
  '\tfloat dhFw = max( length( dFdx( dhP ) ), length( dFdy( dhP ) ) );',
  '\tfloat dhM1 = dhNoiseAA( dhP / dhMacro.y, dhFw / dhMacro.y ) * 0.6 + dhNoiseAA( dhP / dhMacro.y * 2.9 + 17.0, dhFw / dhMacro.y * 2.9 ) * 0.4;',
  '\tfloat dhM2 = dhNoiseAA( dhP / dhMacro.z + 41.0, dhFw / dhMacro.z ) * 0.7 + dhNoiseAA( dhP / dhMacro.z * 2.3 + 5.0, dhFw / dhMacro.z * 2.3 ) * 0.3;',
  '\tvec3 dhTint = mix( dhTintA, dhTintB, smoothstep( 0.3, 0.7, dhM2 * 0.65 + dhM1 * 0.35 ) );',
  '\tdiffuseColor *= vec4( texC.rgb * dhTint * ( 1.0 + dhMacro.x * ( dhM1 - 0.5 ) * 2.0 ), texC.a );',
  '#else',
  '\t#include <map_fragment>',
  '#endif',
].join('\n');

// 법선 · 거칠기 — 표면 지도를 같은 두 자리 · 같은 섞음으로. 접선 = 위 두 세계 축을 면에 눕힌 것(눈 공간)
const NORMAL = [
  '#include <normal_fragment_maps>',
  '#ifdef DH_GROUND',
  '\tvec4 dhS = mix( textureGrad( dhSurf, dhUv + dhOa, dhDx, dhDy ), textureGrad( dhSurf, dhUv + dhOb, dhDx, dhDy ), dhW );',
  '\tvec2 dhNt = ( dhS.xy * 2.0 - 1.0 ) * dhMacro.w;',
  '\tvec3 dhAx = ( viewMatrix * vec4( dhWx, 0.0 ) ).xyz, dhAy = ( viewMatrix * vec4( dhWy, 0.0 ) ).xyz;',
  '\tvec3 dhT = dhAx - normal * dot( dhAx, normal ), dhB = dhAy - normal * dot( dhAy, normal );',
  '\tdhT *= inversesqrt( max( dot( dhT, dhT ), 1e-8 ) ); dhB *= inversesqrt( max( dot( dhB, dhB ), 1e-8 ) );',
  '\tnormal = normalize( normal + dhNt.x * dhT + dhNt.y * dhB );',
  '\tfloat dhAO = mix( 1.0, dhS.z, dhSurfP.x );',
  '\troughnessFactor = clamp( dhSurfP.y - dhSurfP.z * ( dhS.w - 0.5 ) * 2.0, 0.05, 1.0 );',
  '#endif',
].join('\n');

// 그늘 — 하늘빛(간접)에 다 · 해(직접)에 반
const AO = [
  '#include <aomap_fragment>',
  '#ifdef DH_GROUND',
  '\treflectedLight.indirectDiffuse *= dhAO;',
  '\treflectedLight.indirectSpecular *= dhAO;',
  '\treflectedLight.directDiffuse *= mix( 1.0, dhAO, 0.5 );',
  '#endif',
].join('\n');

const swap = (src, pairs) => {
  let s = src;
  for (const [from, to] of pairs) {
    if (!s.includes(from)) throw new Error('바닥 셰이더 자리 없음: ' + from);
    s = s.replace(from, to);
  }
  return s;
};

// three 표준(MeshStandard) 셰이더에 위 조각을 끼운다(onBeforeCompile의 sh) — 바꿀 자리가 없으면(three 판이 바뀜) 던진다.
//   켜기는 재료 defines: DH_GROUND(늘) · DH_BOX(옹벽 — 세계 자리 vDhW를 정점에서 넘김)
export function patchGround(sh) {
  sh.fragmentShader = HEAD + '\n' + swap(sh.fragmentShader, [['#include <map_fragment>', MAP], ['#include <normal_fragment_maps>', NORMAL], ['#include <aomap_fragment>', AO]]);
  sh.vertexShader = '#ifdef DH_BOX\nvarying vec3 vDhW;\n#endif\n' + swap(sh.vertexShader, [['#include <project_vertex>',
    '#include <project_vertex>\n#ifdef DH_BOX\n\tvDhW = ( modelMatrix * vec4( transformed, 1.0 ) ).xyz;\n#endif']]);
  return sh;
}
