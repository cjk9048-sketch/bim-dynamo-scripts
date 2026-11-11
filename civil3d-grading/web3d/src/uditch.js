// ★[배수지 U형 측구 1단계 · JACK 1111 «U형 측구 결과물은 3D 보기에도 반영»] U형 측구 층 — 측구마다 메시 하나(본체 + 기초 · 안쪽 물길이 보이는 U자 · C# UDitch.SceneMesh) ·
//   콘크리트 질감(구조물과 같은 재료 틀) · 수리 판정 기운(부족 = 붉은 기운 · 주의 = 노란 기운 · 그 밖 = 콘크리트) · 층 «U형 측구»(기본 켬 · 없는 장면은 안 생김) ·
//   누르면 이름 · 규격 · 경사 · 수리 판정(C#이 만든 [이름, 값] 줄 그대로 — 셈은 뷰어에 없음).
import * as THREE from 'three';

export const UDITCH_COLOR = '#c9c5bc';
const TINT = { '부족': '#d79a92', '주의': '#d9cc8c' };

// mk = { material(color) · geom(pos, idx) · flat(g) } — layers.js의 틀을 빌려 쓴다(질감 · 그림자가 옹벽 · 구조물과 같게)
export function buildUDitch(head, scene, L, mk) {
  const U = head.uditch;
  if (!U || !U.length) return;
  const grp = new THREE.Group();
  const mats = new Map();
  const matOf = (st) => {
    const c = TINT[st] || UDITCH_COLOR;
    if (!mats.has(c)) mats.set(c, mk.material(c));
    return mats.get(c);
  };
  for (const u of U) {
    if (!u.pos || !u.idx || u.idx.length === 0) continue;
    const mesh = new THREE.Mesh(mk.flat(mk.geom(u.pos, u.idx)), matOf(u.status));
    mesh.castShadow = true; mesh.receiveShadow = true;
    mesh.userData = { layer: 'uditch', name: u.name, title: u.title || u.name, rows: Array.isArray(u.rows) ? u.rows : [] };
    grp.add(mesh);
  }
  if (grp.children.length) { scene.add(grp); L.uditch = { obj: grp, label: 'U형 측구', on: true, n: grp.children.length }; }
}

// 고르기 풍선 — 제목 · 줄([이름, 값])
export function describeUDitch(u) {
  const rows = [];
  for (const r of u.rows || []) if (Array.isArray(r) && r.length >= 2) rows.push([String(r[0]), String(r[1])]);
  return { title: u.title || u.name || 'U형 측구', rows };
}
