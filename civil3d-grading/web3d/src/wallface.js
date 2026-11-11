// ★[JACK 1111 «3D 뷰에서 옹벽 형태가 너무 밋밋 — 보강토는 블록, 앵커식은 앵커부 … 옹벽처럼 보이게 꾸며 줘»] 옹벽 무늬 조각 겉모습.
//   무늬 조각(C# WallFacePattern — 보강토 블록 · 앵커식 패널 + 앵커 머리 · 콘크리트 자연석 · 석축 돌)의 모양 · 수는 그대로 두고,
//   점마다 무늬 좌표(fuv = u 줄 앞면 길이 · Z 절대 높이 · w 겉 1 / 몸통 면 0)로 조각마다 색 얼룩 · 모서리 깎임(빛 · 그늘) · 겉 거칠기 · 줄눈 속 그늘을
//   셰이더에서 그린다(그림 파일 없음 · 멀면 화소 크기만큼 넓혀 줄이 끓지 않게). three 없이 시험되는 순수 함수.

// 무늬 층 이름(C# WallFacePattern) → 겉모습 이름 · 모르는 층은 null(옛 한 색 그대로)
const LAYER_STYLE = {
  '옹벽_무늬_블록': 'block', '옹벽_무늬_패널': 'panel', '옹벽_무늬_자연석': 'stone', '옹벽_무늬_석축': 'rubble', '옹벽_앵커머리': 'anchor',
};
export function faceStyle(layer) { return LAYER_STYLE[layer] || null; }

// 겉모습 값(m) — bevel 깎임 폭 · depth 깎임 깊이(빛) · round 0 네모 조각 / 1 둥근 돌 · edge 모서리 어둡기 · side 줄눈 속(옆면 몸통 쪽) 어둡기 ·
//   grain 거칠기 알갱이 · amp 거칠기 높이 · mottle 알갱이 얼룩 · tint 조각마다 밝기 ± · warm 조각마다 누런/푸른 ± · rough 거칠기(빛 번짐)
export const FACE = {
  block: { bevel: 0.022, depth: 0.009, round: 0, edge: 0.66, side: 0.40, grain: 0.022, amp: 0.004, mottle: 0.12, tint: 0.11, warm: 0.05, rough: 0.94 },
  panel: { bevel: 0.016, depth: 0.005, round: 0, edge: 0.74, side: 0.42, grain: 0.03, amp: 0.0008, mottle: 0.06, tint: 0.04, warm: 0.012, rough: 0.86 },
  stone: { bevel: 0.045, depth: 0.016, round: 0.25, edge: 0.70, side: 0.42, grain: 0.05, amp: 0.004, mottle: 0.10, tint: 0.10, warm: 0.05, rough: 0.93 },
  rubble: { bevel: 0.055, depth: 0.022, round: 0.3, edge: 0.60, side: 0.32, grain: 0.04, amp: 0.006, mottle: 0.14, tint: 0.16, warm: 0.08, rough: 0.96 },
  anchor: { bevel: 0.012, depth: 0.004, round: 0, edge: 0.80, side: 0.60, grain: 0.02, amp: 0.0006, mottle: 0.04, tint: 0, warm: 0, rough: 0.72 },
};

// 정해진 난수 [0, 1) — 같은 자리는 늘 같은 값
function hash3(a, b, s) {
  let h = (Math.imul(a | 0, 374761393) + Math.imul(b | 0, 668265263) + Math.imul(s | 0, 1442695041)) >>> 0;
  h = Math.imul(h ^ (h >>> 13), 1274126177) >>> 0;
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}

// 조각 하나의 색 곱(rgb) — 조각 가운데(cu, cz) · 반 크기(hu, hz) · 갈래(옹벽 번호)
//   앵커 머리 셋(도넛 0.56 □ · 0.36 □ · 정착판 0.15 □ — C# 한 층 한 색)은 크기로 가름: 정착판 = 녹슨 강판 · 도넛 = 몸통보다 밝은 콘크리트
export function pieceTint(style, cu, cz, hu, hz, seed = 0) {
  const F = FACE[style];
  if (!F) return [1, 1, 1];
  if (style === 'anchor') {
    const s = Math.min(hu, hz);
    return s < 0.1 ? [0.62, 0.55, 0.49] : s < 0.2 ? [1.12, 1.12, 1.1] : [1.3, 1.29, 1.27];
  }
  const i = Math.round(cu * 100), j = Math.round(cz * 100);
  const b = 1 + F.tint * (hash3(i, j, seed * 2 + 1) - 0.5) * 2, w = F.warm * (hash3(i, j, seed * 2 + 2) - 0.5) * 2;
  return [b * (1 + w), b, b * (1 - w)];
}

// 조각 나누기 — 이어진 세모끼리 한 조각(C#이 조각마다 점을 따로 쌓으므로 조각끼리 점을 안 나눈다) → 점마다 조각 가운데 · 반 크기(fpc) · 색 곱(col)
export function pieceAttrs(idx, fuv, style, seed = 0) {
  const nV = Math.floor(fuv.length / 3);
  const par = new Int32Array(nV);
  for (let i = 0; i < nV; i++) par[i] = i;
  const find = (a) => { while (par[a] !== a) { par[a] = par[par[a]]; a = par[a]; } return a; };
  const join = (a, b) => { a = find(a); b = find(b); if (a !== b) par[b] = a; };
  for (let t = 0; t + 2 < idx.length; t += 3) { join(idx[t], idx[t + 1]); join(idx[t], idx[t + 2]); }
  const lo = new Float64Array(nV * 2).fill(Infinity), hi = new Float64Array(nV * 2).fill(-Infinity);
  for (let v = 0; v < nV; v++) {
    const r = find(v), u = fuv[v * 3], z = fuv[v * 3 + 1];
    if (u < lo[r * 2]) lo[r * 2] = u; if (u > hi[r * 2]) hi[r * 2] = u;
    if (z < lo[r * 2 + 1]) lo[r * 2 + 1] = z; if (z > hi[r * 2 + 1]) hi[r * 2 + 1] = z;
  }
  const fpc = new Float32Array(nV * 4), col = new Float32Array(nV * 3), tint = new Map();
  let pieces = 0;
  for (let v = 0; v < nV; v++) {
    const r = find(v);
    const cu = (lo[r * 2] + hi[r * 2]) / 2, cz = (lo[r * 2 + 1] + hi[r * 2 + 1]) / 2, hu = (hi[r * 2] - lo[r * 2]) / 2, hz = (hi[r * 2 + 1] - lo[r * 2 + 1]) / 2;
    let c = tint.get(r);
    if (!c) { c = pieceTint(style, cu, cz, hu, hz, seed); tint.set(r, c); pieces++; }
    fpc[v * 4] = cu; fpc[v * 4 + 1] = cz; fpc[v * 4 + 2] = hu; fpc[v * 4 + 3] = hz;
    col[v * 3] = c[0]; col[v * 3 + 1] = c[1]; col[v * 3 + 2] = c[2];
  }
  return { fpc, col, pieces };
}

// 재료 값(셰이더 uniform) — wfA = (bevel, depth, round, edge) · wfB = (side, grain, amp, mottle)
export function faceUniforms(style) {
  const F = FACE[style];
  return { wfA: [F.bevel, F.depth, F.round, F.edge], wfB: [F.side, F.grain, F.amp, F.mottle] };
}

const HEAD = [
  'uniform vec4 wfA;', 'uniform vec4 wfB;', 'varying vec3 vWfUv;', 'varying vec4 vWfPc;',
  'float wfHash( vec2 p ) { vec3 q = fract( vec3( p.xyx ) * 0.1031 ); q += dot( q, q.yzx + 33.33 ); return fract( ( q.x + q.y ) * q.z ); }',
  'float wfNoise( vec2 p ) { vec2 i = floor( p ), f = fract( p ), u = f * f * ( 3.0 - 2.0 * f );',
  '\treturn mix( mix( wfHash( i ), wfHash( i + vec2( 1.0, 0.0 ) ), u.x ), mix( wfHash( i + vec2( 0.0, 1.0 ) ), wfHash( i + vec2( 1.0, 1.0 ) ), u.x ), u.y ); }',
  'float wfNoiseAA( vec2 p, float fw ) { return mix( wfNoise( p ), 0.5, smoothstep( 0.3, 0.9, fw ) ); }',
].join('\n');

// 색 — 조각 안 자리(가운데에서 m) → 모서리까지 거리(네모 · 둥근 돌 섞음) → 깎임 · 모서리 어둡기 · 줄눈 속 그늘 · 알갱이 얼룩. 높이(wfH, m)는 법선에서 씀
const COLOR = [
  '#include <color_fragment>',
  '\tvec2 wfQ = vWfUv.xy - vWfPc.xy, wfHalf = max( vWfPc.zw, vec2( 1e-3 ) );',
  '\tvec2 wfE = max( wfHalf - abs( wfQ ), 0.0 );',
  '\tfloat wfEl = max( 1.0 - length( wfQ / wfHalf ), 0.0 ) * min( wfHalf.x, wfHalf.y );',
  '\tfloat wfD = mix( min( wfE.x, wfE.y ), wfEl, wfA.z );',
  '\tfloat wfFw = max( length( fwidth( vWfUv.xy ) ), 1e-5 );',
  '\tfloat wfBw = max( wfA.x, 1.5 * wfFw );',
  '\tfloat wfK = smoothstep( 0.0, wfBw, wfD );',
  '\tfloat wfTop = step( 0.98, vWfUv.z );',
  '\tvec2 wfG = vWfUv.xy / wfB.y;',
  '\tfloat wfGf = wfFw / wfB.y;',
  '\tfloat wfR = wfNoiseAA( wfG, wfGf ) * 0.65 + wfNoiseAA( wfG * 2.7 + 13.0, wfGf * 2.7 ) * 0.35;',
  '\tfloat wfShade = mix( mix( wfA.w, 1.0, wfK ), 1.0, 1.0 - wfTop );',
  '\twfShade *= mix( wfB.x, 1.0, clamp( vWfUv.z, 0.0, 1.0 ) );',
  '\twfShade *= 1.0 + wfB.w * ( wfR - 0.5 ) * 2.0;',
  '\tdiffuseColor.rgb *= wfShade;',
  '\tfloat wfH = ( wfK * wfA.y + wfR * wfB.z ) * wfTop;',
  '\tfloat wfBumpK = wfA.x / wfBw;',
].join('\n');

// 법선 — 높이 wfH의 화면 기울기로 면 법선을 기울임(three perturbNormalArb와 같은 셈 · 멀면 약하게)
const NORMAL = [
  '#include <normal_fragment_maps>',
  '\t{',
  '\t\tvec3 wfSx = dFdx( - vViewPosition ), wfSy = dFdy( - vViewPosition );',
  '\t\tvec3 wfR1 = cross( wfSy, normal ), wfR2 = cross( normal, wfSx );',
  '\t\tfloat wfDet = dot( wfSx, wfR1 ) * faceDirection;',
  '\t\tvec2 wfDh = vec2( dFdx( wfH ), dFdy( wfH ) ) * wfBumpK;',
  '\t\tvec3 wfGrad = sign( wfDet ) * ( wfDh.x * wfR1 + wfDh.y * wfR2 );',
  '\t\tnormal = normalize( abs( wfDet ) * normal - wfGrad );',
  '\t}',
].join('\n');

const swap = (src, pairs) => {
  let s = src;
  for (const [from, to] of pairs) {
    if (!s.includes(from)) throw new Error('옹벽 겉 셰이더 자리 없음: ' + from);
    s = s.replace(from, to);
  }
  return s;
};

// three 표준 재료 셰이더(onBeforeCompile의 sh)에 끼운다 — 점 속성 fuv(vec3) · fpc(vec4) · 바꿀 자리가 없으면(three 판이 바뀜) 던진다
export function patchWallFace(sh) {
  sh.fragmentShader = HEAD + '\n' + swap(sh.fragmentShader, [['#include <color_fragment>', COLOR], ['#include <normal_fragment_maps>', NORMAL]]);
  sh.vertexShader = 'attribute vec3 fuv;\nattribute vec4 fpc;\nvarying vec3 vWfUv;\nvarying vec4 vWfPc;\n'
    + swap(sh.vertexShader, [['#include <begin_vertex>', '#include <begin_vertex>\n\tvWfUv = fuv;\n\tvWfPc = fpc;']]);
  return sh;
}
