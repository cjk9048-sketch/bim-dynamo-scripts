// 굵은 선(화면 px 굵기) · 글자 표 — 빗물 물길 · 측점 · 지적 · 자른 선이 같이 쓴다.
// ★[v108 계획 검토 중간 6] 웹GL 선은 굵기가 늘 1 px — 물길 굵기(모인 양)를 보이려면 LineSegments2.
//   굵기 = CSS px 그대로(LineSegments2가 해상도를 CSS 칸으로 넣는다 — 배율을 곱하면 두 번 커진다 · 설치 전 검토 중간 2) — 사진(배율 2) · 영상(1080)에서도 화면과 같은 비율.
import * as THREE from 'three';
import { LineSegments2 } from 'three/addons/lines/LineSegments2.js';
import { LineSegmentsGeometry } from 'three/addons/lines/LineSegmentsGeometry.js';
import { LineMaterial } from 'three/addons/lines/LineMaterial.js';

// 선분 묶음(x0,y0,z0,x1,y1,z1 …) → 굵은 선 하나. w = CSS px · opts.dash = [긋기, 띄기] 화면 px(screenDash로 맞춤)
export function fatLines(view, seg, color, w, opts = {}) {
  const g = new LineSegmentsGeometry();
  g.setPositions(seg instanceof Float32Array ? seg : new Float32Array(seg));
  const m = new LineMaterial({ color, linewidth: w, worldUnits: false, depthTest: opts.depthTest ?? true, depthWrite: opts.depthWrite ?? true, transparent: opts.opacity !== undefined, opacity: opts.opacity ?? 1 });
  if (opts.dash) { m.dashed = true; m.dashSize = opts.dash[0]; m.gapSize = opts.dash[1]; }
  const L = new LineSegments2(g, m);
  if (opts.dash) L.computeLineDistances();
  L.renderOrder = opts.renderOrder ?? 2;
  return L;
}

// ★[JACK 1028 «측점 선은 점선»] 점선 마디를 화면 px로 — 보는 자리(돌리는 중심) 거리에서 1 m가 몇 px인지를 dashScale로
//   (가까이 가도 · 멀어져도 마디 꼴이 같다 · 멀리 있는 선은 원근대로 촘촘)
export function screenDash(view, mats, visible) {
  view.onDraw(() => {
    if (!visible()) return;
    const cam = view.camera, d = Math.max(1, cam.position.distanceTo(view.controls.target));
    const H = view.renderer.domElement.height / view.renderer.getPixelRatio();
    const k = H / (2 * d * Math.tan(THREE.MathUtils.degToRad(cam.fov / 2)));
    for (const m of mats) m.dashScale = k;
  });
}

// 꺾은선(x,y,z …) → 선분 묶음 — 높이가 없는 점(NaN · −99999)에서 끊는다
export function polySeg(pts, out = []) {
  const ok = (i) => Number.isFinite(pts[i + 2]) && pts[i + 2] > -9999;
  for (let i = 0; i + 5 < pts.length; i += 3) {
    if (!ok(i) || !ok(i + 3)) continue;
    out.push(pts[i], pts[i + 1], pts[i + 2], pts[i + 3], pts[i + 4], pts[i + 5]);
  }
  return out;
}

// 글자 표(화면 크기 고정) — 지번 · 측점 이름
export function textSprite(text, x, y, z, color = '#ffe14d', size = 0.028) {
  const fs = 44, pad = 10;
  const cv = document.createElement('canvas');
  const ctx = cv.getContext('2d');
  ctx.font = `600 ${fs}px "Malgun Gothic", sans-serif`;
  const w = Math.ceil(ctx.measureText(text).width) + pad * 2, h = fs + pad * 2;
  cv.width = w; cv.height = h;
  ctx.font = `600 ${fs}px "Malgun Gothic", sans-serif`;
  ctx.fillStyle = 'rgba(0,0,0,0.55)'; ctx.fillRect(0, 0, w, h);
  ctx.fillStyle = color; ctx.textBaseline = 'middle'; ctx.fillText(text, pad, h / 2);
  const tex = new THREE.CanvasTexture(cv);
  tex.colorSpace = THREE.SRGBColorSpace;
  const sp = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, depthTest: false, sizeAttenuation: false }));
  sp.scale.set(w / h * size, size, 1);
  sp.position.set(x, y, z);
  sp.renderOrder = 4;
  return sp;
}

// 글자 표는 가까운 것만 — 사진기에서 maxDist 안 · 먼저 놓을 것(userData.pri 큰 것 · 가까운 것)부터 max개 ·
// 화면에서 이미 놓은 글자와 겹치면 건너뜀(촘촘한 측점 이름이 한 덩어리로 뭉개지지 않게) · userData.force(마우스 올린 측점)는 거리 · 개수와 상관없이 맨 먼저
const _v = new THREE.Vector3();
export function nearLabels(sprites, camera, maxDist, max, H) {
  const p = camera.position;
  const near = [];
  for (const s of sprites) {
    const d = s.position.distanceTo(p);
    s.visible = false;
    if ((d < maxDist || s.userData.force) && !s.userData.hide) near.push([d, s]);
  }
  near.sort((a, b) => ((b[1].userData.force ? 1 : 0) - (a[1].userData.force ? 1 : 0)) || ((b[1].userData.pri || 0) - (a[1].userData.pri || 0)) || a[0] - b[0]);
  // 글자 크기(px) = 크기 × 초점 배율 ÷ 2 × 화면 높이(sizeAttenuation 끈 글자 — three 스프라이트 셰이더와 같은 셈)
  const f = camera.projectionMatrix.elements[5], kH = f / 2 * (H || 1000);
  const placed = [];
  for (const [, s] of near) {
    if (placed.length >= max) break;
    _v.copy(s.position).project(camera);
    if (_v.z > 1 || _v.z < -1) continue;
    const x = _v.x * kH / f * (f / (camera.projectionMatrix.elements[0] || f)), y = _v.y * kH / f;   // 화면 가운데 기준 px(가로는 화면비)
    const w = s.scale.x * kH / 2 + 2, h = s.scale.y * kH / 2 + 2;
    if (placed.some(q => Math.abs(q[0] - x) < q[2] + w && Math.abs(q[1] - y) < q[3] + h)) continue;
    placed.push([x, y, w, h]);
    s.visible = true;
  }
}
