// 드론 로드뷰 처음 화면 큰 카드용 3D 드론 (직접 만든 모델 · 상표 · 로고 · 제품 그림 없음)
// three.js (MIT · https://threejs.org) + RoundedBoxGeometry · RoomEnvironment 를 esbuild 로 한 파일(IIFE)로 묶은 것이 drone3d.js.
// 다시 묶는 법: npm i three esbuild → npx esbuild drone3d.src.js --bundle --minify --format=iife --legal-comments=inline --outfile=drone3d.js
//   window.Drone3D.create(canvas, 'mapping' | '360', { pose: {yaw, pitch, roll} }) → { resize(w, h, dpr), render(t초), dispose() }
//   pose 를 주면 그 자세로 고정 (모델 확인용) · WebGL 이 안 되면 null
//   mapping = 접이식 소형 기업용 매핑 기체 (앞팔 옆으로 · 뒷팔 아래 뒤로 · 3축 짐벌 + 여러 렌즈 블록)
//   360     = 양옆 덕트 네 개 + 가운데 세로 360 카메라 블록의 납작한 기체
import * as THREE from 'three';
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js';
import { RoundedBoxGeometry } from 'three/examples/jsm/geometries/RoundedBoxGeometry.js';

const V3 = THREE.Vector3;

function materials() {
  const std = (c, r, m, extra) => new THREE.MeshStandardMaterial(Object.assign({ color: c, roughness: r, metalness: m }, extra || {}));
  return {
    body: std(0x30353a, 0.62, 0.1),              // 짙은 회색 무광
    cover: std(0x3d434a, 0.5, 0.15),
    arm: std(0x2a2f34, 0.5, 0.2),
    groove: std(0x111315, 0.8, 0.1),
    plate: std(0x1f2327, 0.7, 0.1),
    metal: std(0x1a1d21, 0.35, 0.85),
    steel: std(0x9aa3ab, 0.3, 0.9),
    light: std(0x8e949a, 0.5, 0.12),              // 밝은 회색 (덕트 · 몸체 윗면)
    lightEdge: std(0xc9cdd1, 0.38, 0.12),
    duct: std(0x9aa0a6, 0.46, 0.16, { side: THREE.DoubleSide }),
    black: std(0x14161a, 0.45, 0.2),
    glass: new THREE.MeshPhysicalMaterial({ color: 0x04060a, roughness: 0.04, metalness: 0, clearcoat: 1, clearcoatRoughness: 0.03, envMapIntensity: 1.8 }),
    tint: new THREE.MeshPhysicalMaterial({ color: 0x12294a, roughness: 0.05, metalness: 0.35, clearcoat: 1, clearcoatRoughness: 0.02, envMapIntensity: 2.0 }),
    laser: new THREE.MeshPhysicalMaterial({ color: 0x3a0707, roughness: 0.08, metalness: 0.2, clearcoat: 1, envMapIntensity: 1.5 }),
    bladeGray: std(0x7d848a, 0.55, 0.15, { side: THREE.DoubleSide, transparent: true, opacity: 0.55 }),
    bladeDark: std(0x15181b, 0.6, 0.1, { side: THREE.DoubleSide, transparent: true, opacity: 0.45 }),
    disc: new THREE.MeshBasicMaterial({ color: 0xcfd9df, transparent: true, opacity: 0.09, side: THREE.DoubleSide, depthWrite: false }),
    red: new THREE.MeshBasicMaterial({ color: 0xff3b3b }),
    green: new THREE.MeshBasicMaterial({ color: 0x38ff8a }),
    white: new THREE.MeshBasicMaterial({ color: 0xe8f4ff }),
  };
}

function shadowed(o) { o.traverse((m) => { if (m.isMesh && !m.material.isMeshBasicMaterial) { m.castShadow = true; m.receiveShadow = true; } }); return o; }

// 두 점 사이 둥근 막대 / 납작한 팔
function limb(a, b, r, mat) {
  const d = b.clone().sub(a), len = d.length();
  const m = new THREE.Mesh(new THREE.CylinderGeometry(r, r, len, 12), mat);
  m.position.copy(a).addScaledVector(d, 0.5);
  m.quaternion.setFromUnitVectors(new V3(0, 1, 0), d.normalize());
  return m;
}
function boxLimb(a, b, w, h, mat, rad) {
  const d = b.clone().sub(a), len = d.length();
  const m = new THREE.Mesh(new RoundedBoxGeometry(len, h, w, 3, rad || 0.025), mat);
  m.position.copy(a).addScaledVector(d, 0.5);
  m.quaternion.setFromUnitVectors(new V3(1, 0, 0), d.normalize());
  return m;
}
// 위에서 본 모양(x,z 점들)을 높이 h 로 뽑은 몸체 (모서리만 살짝 둥글게)
function planBody(pts, h, bevel, mat) {
  const sh = new THREE.Shape();
  pts.forEach(([x, z], i) => { if (i) sh.lineTo(x, -z); else sh.moveTo(x, -z); });
  const g = new THREE.ExtrudeGeometry(sh, { depth: h - 2 * bevel, bevelEnabled: true, bevelThickness: bevel, bevelSize: bevel, bevelSegments: 3, curveSegments: 8 });
  g.rotateX(-Math.PI / 2);
  g.translate(0, -(h / 2 - bevel), 0);
  return new THREE.Mesh(g, mat);
}
// z 방향을 보는 원통 (렌즈)
function lensZ(r, len, mat) { const g = new THREE.CylinderGeometry(r, r, len, 28); g.rotateX(Math.PI / 2); return new THREE.Mesh(g, mat); }
function dot(r, mat) { return new THREE.Mesh(new THREE.SphereGeometry(r, 10, 8), mat); }

// 프로펠러 : 얇은 날개 둘 + 도는 반투명 원판 (빠르게 돌 때의 흐림)
function makeProp(R, m, bladeMat) {
  const g = new THREE.Group();
  const sh = new THREE.Shape();
  sh.moveTo(0.05, -0.035);
  sh.quadraticCurveTo(R * 0.5, -R * 0.13, R, -R * 0.05);
  sh.quadraticCurveTo(R * 1.03, 0, R, R * 0.055);
  sh.quadraticCurveTo(R * 0.5, R * 0.12, 0.05, 0.045);
  sh.closePath();
  const geo = new THREE.ExtrudeGeometry(sh, { depth: 0.01, bevelEnabled: false });
  geo.rotateX(-Math.PI / 2);
  const spin = new THREE.Group();
  const b1 = new THREE.Mesh(geo, bladeMat), b2 = new THREE.Mesh(geo, bladeMat);
  b2.rotation.y = Math.PI;
  spin.add(b1, b2);
  const disc = new THREE.Mesh(new THREE.CircleGeometry(R, 40).rotateX(-Math.PI / 2), m.disc);
  disc.position.y = 0.004;
  g.add(spin, disc);
  const hub = new THREE.Mesh(new THREE.CylinderGeometry(0.045, 0.055, 0.05, 20), m.metal);
  hub.position.y = 0.015; g.add(hub);
  g.userData.spin = spin;
  return g;
}

// ---- 접이식 소형 기업용 매핑 기체 : 길쭉한 짙은 회색 몸체 · 앞팔 옆으로 · 뒷팔 아래 뒤로 · 3축 짐벌 + 렌즈 블록
function buildMapping(m) {
  const root = new THREE.Group(), props = [];
  // 몸체 : 앞이 좁고 뒤가 넓은 유선형
  const hull = planBody([[-0.17, 0.82], [0.17, 0.82], [0.27, 0.62], [0.31, 0.25], [0.31, -0.55], [0.25, -0.74], [-0.25, -0.74], [-0.31, -0.55], [-0.31, 0.25], [-0.27, 0.62]], 0.34, 0.07, m.body);
  root.add(hull);
  const dome = new THREE.Mesh(new THREE.SphereGeometry(1, 32, 16), m.cover); dome.scale.set(0.27, 0.13, 0.66); dome.position.set(0, 0.17, -0.06); root.add(dome);
  const nose = new THREE.Mesh(new RoundedBoxGeometry(0.3, 0.12, 0.05, 2, 0.02), m.groove); nose.position.set(0, 0.03, 0.87); root.add(nose);
  for (const sx of [-1, 1]) {
    for (let i = 0; i < 5; i++) { const s = new THREE.Mesh(new THREE.BoxGeometry(0.012, 0.03, 0.14), m.groove); s.position.set(sx * 0.385, 0.04 - (i % 2) * 0.0, -0.12 - i * 0.12 + 0.12); root.add(s); }   // 옆 냉각 홈
    const eye = dot(0.026, m.glass); eye.position.set(sx * 0.16, 0.03, 0.88); root.add(eye);           // 앞 감지 카메라
    const sideEye = dot(0.026, m.glass); sideEye.position.set(sx * 0.385, 0.03, 0.42); root.add(sideEye);   // 옆
    const back = dot(0.026, m.glass); back.position.set(sx * 0.14, 0.03, -0.8); root.add(back);        // 뒤
    const up = dot(0.024, m.glass); up.position.set(sx * 0.12, 0.27, 0.34); root.add(up);              // 위
    const dn = dot(0.024, m.glass); dn.position.set(sx * 0.14, -0.2, 0.3); root.add(dn);               // 아래
    // 몸체 아래 짧은 받침
    const pad = new THREE.Mesh(new RoundedBoxGeometry(0.1, 0.06, 0.24, 2, 0.02), m.plate); pad.position.set(sx * 0.22, -0.2, -0.38); root.add(pad);
    const padF = new THREE.Mesh(new RoundedBoxGeometry(0.08, 0.05, 0.16, 2, 0.02), m.plate); padF.position.set(sx * 0.2, -0.19, 0.1); root.add(padF);
  }
  // 3축 짐벌 + 네모 카메라 블록
  const bracket = new THREE.Mesh(new RoundedBoxGeometry(0.24, 0.1, 0.22, 2, 0.03), m.plate); bracket.position.set(0, -0.2, 0.56); root.add(bracket);
  const yaw = new THREE.Mesh(new THREE.CylinderGeometry(0.13, 0.13, 0.05, 24), m.metal); yaw.position.set(0, -0.27, 0.58); root.add(yaw);
  const roll = new THREE.Mesh(new THREE.TorusGeometry(0.17, 0.018, 10, 36), m.plate); roll.position.set(0, -0.4, 0.74); roll.rotation.y = Math.PI / 2; root.add(roll);
  const blk = new THREE.Mesh(new RoundedBoxGeometry(0.46, 0.27, 0.3, 3, 0.04), m.black); blk.position.set(0, -0.4, 0.74); root.add(blk);
  const front = 0.74 + 0.15;
  const wide = lensZ(0.082, 0.05, m.glass); wide.position.set(-0.1, -0.4, front); root.add(wide);                       // 광각 (큰 렌즈)
  const wideRing = new THREE.Mesh(new THREE.TorusGeometry(0.086, 0.012, 10, 28), m.steel); wideRing.position.set(-0.1, -0.4, front + 0.025); root.add(wideRing);
  const wideCore = dot(0.035, m.tint); wideCore.position.set(-0.1, -0.4, front + 0.03); root.add(wideCore);
  for (const y of [-0.34, -0.46]) {                                                                                       // 망원 둘
    const t = lensZ(0.04, 0.05, m.glass); t.position.set(0.1, y, front); root.add(t);
    const tr = new THREE.Mesh(new THREE.TorusGeometry(0.043, 0.008, 8, 22), m.steel); tr.position.set(0.1, y, front + 0.025); root.add(tr);
  }
  const rf = new THREE.Mesh(new RoundedBoxGeometry(0.075, 0.032, 0.02, 1, 0.008), m.laser); rf.position.set(0.19, -0.4, front + 0.005); root.add(rf);   // 레이저 거리계 창
  // 팔 : 앞팔 = 옆으로 펴짐(높음) · 뒷팔 = 아래 뒤로 내려 펴짐(낮음)
  const spec = [
    [1, 1, new V3(0.3, 0.1, 0.46), new V3(0.84, 0.2, 0.58), 0.5],
    [-1, 1, new V3(-0.3, 0.1, 0.46), new V3(-0.84, 0.2, 0.58), 0.5],
    [1, -1, new V3(0.3, 0.02, -0.42), new V3(0.8, -0.14, -0.78), 0.5],
    [-1, -1, new V3(-0.3, 0.02, -0.42), new V3(-0.8, -0.14, -0.78), 0.5],
  ];
  for (const [sx, sz, a, b, R] of spec) {
    root.add(boxLimb(a, b, sz > 0 ? 0.13 : 0.15, 0.1, m.arm, 0.03));
    const hinge = new THREE.Mesh(new THREE.CylinderGeometry(0.07, 0.07, 0.14, 16), m.metal); hinge.position.copy(a); root.add(hinge);
    const motor = new THREE.Mesh(new THREE.CylinderGeometry(0.085, 0.095, 0.14, 24), m.metal); motor.position.set(b.x, b.y + 0.07, b.z); root.add(motor);
    const bell = new THREE.Mesh(new THREE.CylinderGeometry(0.07, 0.085, 0.05, 24), m.steel); bell.position.set(b.x, b.y + 0.16, b.z); root.add(bell);
    const p = makeProp(R, m, m.bladeGray); p.position.set(b.x, b.y + 0.2, b.z); root.add(p); props.push([p, (sx * sz) > 0 ? 1 : -1]);
    const led = dot(0.03, sz > 0 ? m.red : m.green); led.position.set(b.x + sx * 0.06, b.y - 0.07, b.z + sz * 0.05); root.add(led);
  }
  shadowed(root);
  return { root, props };
}

// ---- 납작한 360 기체 : 양옆 덕트 네 개(앞뒤로 이어짐) + 가운데 세로 360 카메라 블록 · 회색/검정 투톤
function buildOsv(m) {
  const root = new THREE.Group(), props = [];
  // 몸체 (투톤) : 아래 검정 · 위 밝은 회색
  const low = new THREE.Mesh(new RoundedBoxGeometry(1.5, 0.3, 0.92, 2, 0.025), m.black); low.position.y = -0.1; root.add(low);
  const up = new THREE.Mesh(new RoundedBoxGeometry(1.46, 0.2, 0.9, 2, 0.025), m.light); up.position.y = 0.08; root.add(up);
  for (const sx of [-1, 1]) {
    const st = new THREE.Mesh(new THREE.BoxGeometry(0.2, 0.012, 0.6), m.groove); st.position.set(sx * 0.45, 0.185, 0); root.add(st);   // 윗면 얇은 패널 선
  }
  // 가운데 세로 360 카메라 블록 (날카로운 모서리 · 옆면 냉각 홈)
  const blk = new THREE.Mesh(new RoundedBoxGeometry(0.54, 0.98, 0.64, 1, 0.02), m.black); root.add(blk);
  for (const sx of [-1, 1]) for (let i = 0; i < 6; i++) {
    const g = new THREE.Mesh(new THREE.BoxGeometry(0.012, 0.022, 0.42), m.groove); g.position.set(sx * 0.272, -0.3 + i * 0.1, 0); root.add(g);
  }
  for (const k of [1, -1]) {                         // 위 · 아래 어안 렌즈
    const d = new THREE.Mesh(new THREE.SphereGeometry(0.2, 36, 18, 0, Math.PI * 2, 0, Math.PI / 2), m.glass);
    d.position.y = k * 0.49; if (k < 0) d.rotation.x = Math.PI; root.add(d);
    const bez = new THREE.Mesh(new THREE.TorusGeometry(0.205, 0.022, 12, 40), m.steel); bez.rotation.x = Math.PI / 2; bez.position.y = k * 0.49; root.add(bez);
    const core = dot(0.07, m.tint); core.position.y = k * 0.58; core.scale.y = 0.6; root.add(core);
  }
  for (const sx of [-1, 1]) {                         // 블록 앞 작은 센서
    const e = dot(0.03, m.glass); e.position.set(sx * 0.14, -0.05, 0.325); root.add(e);
  }
  // 덕트 네 개
  const prof = [[0.43, 0.16], [0.48, 0.185], [0.575, 0.14], [0.575, -0.14], [0.5, -0.18], [0.43, -0.13]].map((p) => new THREE.Vector2(p[0], p[1]));
  prof.push(prof[0].clone());
  const lathe = new THREE.LatheGeometry(prof, 56);
  for (const [sx, sz] of [[1, 1], [-1, 1], [1, -1], [-1, -1]]) {
    const g = new THREE.Group(); g.position.set(sx * 1.14, 0.0, sz * 0.66);
    g.add(new THREE.Mesh(lathe, m.duct));
    const s1 = new THREE.Mesh(new THREE.BoxGeometry(0.9, 0.025, 0.05), m.plate); s1.position.y = -0.03; g.add(s1);
    const s2 = s1.clone(); s2.rotation.y = Math.PI / 2; g.add(s2);
    const mot = new THREE.Mesh(new THREE.CylinderGeometry(0.07, 0.08, 0.1, 20), m.metal); mot.position.y = 0.0; g.add(mot);
    const p = makeProp(0.4, m, m.bladeDark); p.position.y = 0.06; g.add(p); props.push([p, (sx * sz) > 0 ? 1 : -1]);
    const cam = dot(0.035, m.glass); cam.position.set(sx * 0.1, -0.19, sz * 0.5); g.add(cam);                 // 덕트 아래 작은 카메라
    const sen = dot(0.026, m.glass); sen.position.set(0, 0.0, sz * 0.585); g.add(sen);                        // 덕트 앞뒤 센서
    const led = dot(0.028, sz > 0 ? m.white : m.red); led.position.set(sx * 0.585, 0.0, 0); g.add(led);
    root.add(g);
  }
  // 앞뒤 덕트를 잇는 옆 벽
  for (const sx of [-1, 1]) {
    const w = new THREE.Mesh(new RoundedBoxGeometry(0.2, 0.28, 0.5, 2, 0.02), m.duct); w.position.set(sx * 1.14, 0, 0); root.add(w);
    const br = new THREE.Mesh(new RoundedBoxGeometry(0.6, 0.18, 0.5, 2, 0.02), m.light); br.position.set(sx * 0.82, 0.0, 0); root.add(br);
  }
  shadowed(root);
  return { root, props };
}

function create(canvas, kind, opts) {
  opts = opts || {};
  let renderer;
  try {
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, powerPreference: 'low-power' });
    if (!renderer.getContext()) return null;
  } catch (e) { return null; }
  try {
    renderer.setClearColor(0x000000, 0);
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.05;
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    const scene = new THREE.Scene();
    const pm = new THREE.PMREMGenerator(renderer);
    scene.environment = pm.fromScene(new RoomEnvironment(), 0.04).texture;
    pm.dispose();
    const m = materials();
    const model = kind === 'mapping' ? buildMapping(m) : buildOsv(m);
    const holder = new THREE.Group(); holder.add(model.root); scene.add(holder);
    const key = new THREE.DirectionalLight(0xffffff, 1.7); key.position.set(2.5, 5, 3.5); key.castShadow = true;
    key.shadow.mapSize.set(1024, 1024); key.shadow.radius = 6; key.shadow.bias = -0.0004;
    Object.assign(key.shadow.camera, { left: -3, right: 3, top: 3, bottom: -3, near: 1, far: 14 });
    scene.add(key);
    const rim = new THREE.DirectionalLight(kind === 'mapping' ? 0x38d6f0 : 0xffb25a, 1.1); rim.position.set(-3.5, 2, -3); scene.add(rim);
    const floor = new THREE.Mesh(new THREE.PlaneGeometry(12, 12).rotateX(-Math.PI / 2), new THREE.ShadowMaterial({ opacity: 0.5 }));
    floor.position.y = -1.25; floor.receiveShadow = true; scene.add(floor);
    const cam = new THREE.PerspectiveCamera(30, 1.5, 0.1, 60);
    const place = () => {
      // 드론이 칸 폭의 약 38 % · 위쪽 제목과 안 겹치게 아래로 (lookAt 을 위로)
      const t2 = Math.tan(THREE.MathUtils.degToRad(cam.fov / 2));
      const need = kind === 'mapping' ? [6.9, 4.4] : [8.4, 4.4];
      const d = Math.max(need[0] / (2 * t2 * cam.aspect), need[1] / (2 * t2));
      cam.position.set(0, d * 0.2, d); cam.lookAt(0, 0.72, 0);
    };
    return {
      resize(w, h, dpr) { renderer.setPixelRatio(dpr || 1); renderer.setSize(w, h, false); cam.aspect = w / h; cam.updateProjectionMatrix(); place(); },
      render(t) {
        for (const [p, dirn] of model.props) p.userData.spin.rotation.y = t * 38 * dirn;
        if (opts.pose) {
          holder.position.y = 0; holder.rotation.set(opts.pose.pitch || 0, opts.pose.yaw || 0, opts.pose.roll || 0);
        } else if (kind === 'mapping') {
          holder.position.y = Math.sin(t * 1.2) * 0.07;
          holder.rotation.set(0.1 + Math.sin(t * 0.8) * 0.05, 0.4 + Math.sin(t * 0.35) * 0.6, Math.sin(t * 0.6) * 0.07);
        } else {
          holder.position.y = Math.sin(t * 1.3) * 0.08;
          holder.rotation.set(0.07 + Math.sin(t * 0.7) * 0.04, Math.sin(t * 0.3) * 0.75, Math.sin(t * 0.5) * 0.05);
        }
        renderer.render(scene, cam);
      },
      dispose() { renderer.dispose(); },
    };
  } catch (e) { try { renderer.dispose(); } catch (e2) { /* 무시 */ } return null; }
}

window.Drone3D = { create };
