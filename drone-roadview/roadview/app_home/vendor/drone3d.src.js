// 드론 로드뷰 처음 화면 큰 카드용 3D 드론 (직접 만든 모델 · 상표 · 로고 · 제품 그림 없음)
// three.js (MIT · https://threejs.org) + RoundedBoxGeometry · RoomEnvironment 를 esbuild 로 한 파일(IIFE)로 묶은 것이 drone3d.js.
// 다시 묶는 법: npm i three esbuild → npx esbuild drone3d.src.js --bundle --minify --format=iife --legal-comments=inline --outfile=drone3d.js
//   window.Drone3D.create(canvas, 'mapping' | '360') → { resize(w, h, dpr), render(t초), dispose() }  (WebGL 이 안 되면 null)
import * as THREE from 'three';
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js';
import { RoundedBoxGeometry } from 'three/examples/jsm/geometries/RoundedBoxGeometry.js';

const V3 = THREE.Vector3;

function materials() {
  const std = (c, r, m, extra) => new THREE.MeshStandardMaterial(Object.assign({ color: c, roughness: r, metalness: m }, extra || {}));
  return {
    dark: std(0x3a4046, 0.55, 0.12),            // 어두운 회색 무광 플라스틱
    mid: std(0x4b535a, 0.5, 0.15),
    plate: std(0x1f2327, 0.7, 0.1),
    metal: std(0x1a1d21, 0.35, 0.85),            // 모터 · 경첩 (검은 금속)
    steel: std(0x9aa3ab, 0.3, 0.9),
    light: std(0xaeb4ba, 0.45, 0.1),            // 밝은 회색 몸체
    lightRing: std(0x9aa1a8, 0.4, 0.35),
    spoke: std(0x2c3136, 0.6, 0.2),
    glass: new THREE.MeshPhysicalMaterial({ color: 0x04060a, roughness: 0.04, metalness: 0, clearcoat: 1, clearcoatRoughness: 0.03, envMapIntensity: 1.8 }),
    tint: new THREE.MeshPhysicalMaterial({ color: 0x12294a, roughness: 0.05, metalness: 0.35, clearcoat: 1, clearcoatRoughness: 0.02, envMapIntensity: 2.0 }),
    blade: std(0x14171a, 0.6, 0.1, { side: THREE.DoubleSide, transparent: true, opacity: 0.4 }),
    disc: new THREE.MeshBasicMaterial({ color: 0xcfd9df, transparent: true, opacity: 0.10, side: THREE.DoubleSide, depthWrite: false }),
    red: new THREE.MeshBasicMaterial({ color: 0xff3b3b }),
    green: new THREE.MeshBasicMaterial({ color: 0x38ff8a }),
    white: new THREE.MeshBasicMaterial({ color: 0xe8f4ff }),
  };
}

function shadowed(o) { o.traverse((m) => { if (m.isMesh && !m.material.isMeshBasicMaterial) { m.castShadow = true; m.receiveShadow = true; } }); return o; }

function limb(a, b, r, mat) {
  const d = b.clone().sub(a), len = d.length();
  const m = new THREE.Mesh(new THREE.CylinderGeometry(r, r, len, 12), mat);
  m.position.copy(a).addScaledVector(d, 0.5);
  m.quaternion.setFromUnitVectors(new V3(0, 1, 0), d.normalize());
  return m;
}

// 프로펠러 : 얇은 날개 둘 + 도는 반투명 원판 (빠르게 돌 때의 흐림)
function makeProp(R, m) {
  const g = new THREE.Group();
  const sh = new THREE.Shape();
  sh.moveTo(0.06, -0.04);
  sh.quadraticCurveTo(R * 0.5, -R * 0.13, R, -R * 0.045);
  sh.quadraticCurveTo(R * 1.03, 0, R, R * 0.05);
  sh.quadraticCurveTo(R * 0.5, R * 0.11, 0.06, 0.05);
  sh.closePath();
  const geo = new THREE.ExtrudeGeometry(sh, { depth: 0.012, bevelEnabled: false });
  geo.rotateX(-Math.PI / 2);
  const spin = new THREE.Group();
  const b1 = new THREE.Mesh(geo, m.blade), b2 = new THREE.Mesh(geo, m.blade);
  b2.rotation.y = Math.PI;
  spin.add(b1, b2);
  const disc = new THREE.Mesh(new THREE.CircleGeometry(R, 40).rotateX(-Math.PI / 2), m.disc);
  disc.position.y = 0.004;
  g.add(spin, disc);
  const hub = new THREE.Mesh(new THREE.CylinderGeometry(0.07, 0.085, 0.07, 20), m.metal);
  hub.position.y = 0.02; g.add(hub);
  g.userData.spin = spin;
  return g;
}

// ---- 산업용 매핑 드론 : 어두운 회색 몸체 · X 자 접이식 팔 넷 · 큰 프로펠러 · 앞 아래 짐벌 · 착륙 다리 · 팔 끝 LED
function buildMapping(m) {
  const root = new THREE.Group(), props = [];
  const hull = new THREE.Mesh(new RoundedBoxGeometry(1.25, 0.44, 1.75, 6, 0.14), m.dark); root.add(hull);
  const cap = new THREE.Mesh(new RoundedBoxGeometry(0.92, 0.16, 1.22, 5, 0.07), m.mid); cap.position.set(0, 0.26, -0.05); root.add(cap);
  const bat = new THREE.Mesh(new RoundedBoxGeometry(1.3, 0.2, 0.62, 4, 0.05), m.plate); bat.position.set(0, 0.0, -0.45); root.add(bat);
  const nose = new THREE.Mesh(new RoundedBoxGeometry(0.98, 0.16, 0.08, 4, 0.03), m.glass); nose.position.set(0, 0.04, 0.88); root.add(nose);
  for (const sx of [-1, 1]) {
    const l = new THREE.Mesh(new THREE.SphereGeometry(0.06, 20, 14), m.tint); l.position.set(sx * 0.34, 0.04, 0.91); root.add(l);
    const an = limb(new V3(sx * 0.4, 0.3, -0.7), new V3(sx * 0.46, 0.7, -0.78), 0.022, m.plate); root.add(an);
  }
  // 앞 아래 짐벌 카메라 (렌즈 유리 반사)
  const mount = new THREE.Mesh(new RoundedBoxGeometry(0.5, 0.16, 0.4, 4, 0.05), m.plate); mount.position.set(0, -0.3, 0.6); root.add(mount);
  const gim = new THREE.Mesh(new THREE.SphereGeometry(0.2, 28, 20), m.dark); gim.position.set(0, -0.46, 0.66); root.add(gim);
  const lens = new THREE.Mesh(new THREE.SphereGeometry(0.115, 28, 20), m.glass); lens.position.set(0, -0.46, 0.84); root.add(lens);
  const ring = new THREE.Mesh(new THREE.TorusGeometry(0.125, 0.02, 12, 32), m.steel); ring.position.set(0, -0.46, 0.8); root.add(ring);
  const iris = new THREE.Mesh(new THREE.SphereGeometry(0.055, 20, 14), m.tint); iris.position.set(0.0, -0.46, 0.93); root.add(iris);
  // 팔 · 모터 · 프로펠러 · LED
  for (const [sx, sz] of [[1, 1], [-1, 1], [1, -1], [-1, -1]]) {
    const dir = new V3(sx, 0, sz).normalize();
    const arm = new THREE.Mesh(new RoundedBoxGeometry(1.2, 0.13, 0.2, 4, 0.05), m.mid);
    arm.rotation.y = -Math.atan2(sz, sx); arm.position.copy(dir).multiplyScalar(0.98); arm.position.y = 0.04; root.add(arm);
    const hinge = new THREE.Mesh(new THREE.CylinderGeometry(0.13, 0.13, 0.2, 20), m.metal); hinge.position.copy(dir).multiplyScalar(0.42); hinge.position.y = 0.05; root.add(hinge);
    const mp = dir.clone().multiplyScalar(1.6);
    const motor = new THREE.Mesh(new THREE.CylinderGeometry(0.14, 0.15, 0.2, 28), m.metal); motor.position.set(mp.x, 0.1, mp.z); root.add(motor);
    const bell = new THREE.Mesh(new THREE.CylinderGeometry(0.115, 0.13, 0.08, 28), m.steel); bell.position.set(mp.x, 0.24, mp.z); root.add(bell);
    const p = makeProp(0.88, m); p.position.set(mp.x, 0.3, mp.z); root.add(p); props.push([p, (sx * sz) > 0 ? 1 : -1]);
    const led = new THREE.Mesh(new THREE.SphereGeometry(0.04, 12, 10), sz > 0 ? m.red : m.green); led.position.set(mp.x + dir.x * 0.16, -0.02, mp.z + dir.z * 0.16); root.add(led);
  }
  // 착륙 다리
  for (const [sx, sz] of [[1, 1], [-1, 1], [1, -1], [-1, -1]]) {
    const a = new V3(sx * 0.5, -0.2, sz * 0.6), b = new V3(sx * 0.95, -0.82, sz * 0.8);
    root.add(limb(a, b, 0.035, m.plate));
    const f = new THREE.Mesh(new THREE.SphereGeometry(0.06, 14, 10), m.metal); f.position.copy(b); root.add(f);
  }
  shadowed(root);
  return { root, props, kind: 'mapping' };
}

// ---- 소형 360 드론 : 밝은 회색 몸체 · 프로펠러 가드(덕트) 넷 · 위 · 아래 어안 렌즈 · 짧은 다리
function buildOsv(m) {
  const root = new THREE.Group(), props = [];
  const body = new THREE.Mesh(new RoundedBoxGeometry(0.98, 0.82, 1.3, 6, 0.2), m.light); root.add(body);
  const band = new THREE.Mesh(new RoundedBoxGeometry(1.0, 0.2, 1.32, 4, 0.06), m.plate); band.position.y = -0.08; root.add(band);
  for (const up of [1, -1]) {                         // 어안 렌즈 둘 (위 · 아래)
    const dome = new THREE.Mesh(new THREE.SphereGeometry(0.31, 36, 18, 0, Math.PI * 2, 0, Math.PI / 2), m.glass);
    dome.position.y = up * 0.4; if (up < 0) dome.rotation.x = Math.PI; root.add(dome);
    const bez = new THREE.Mesh(new THREE.TorusGeometry(0.31, 0.035, 14, 40), m.steel); bez.rotation.x = Math.PI / 2; bez.position.y = up * 0.4; root.add(bez);
    const core = new THREE.Mesh(new THREE.SphereGeometry(0.12, 20, 14), m.tint); core.position.y = up * 0.5; root.add(core);
  }
  for (const sx of [-1, 1]) {                         // 앞 센서
    const e = new THREE.Mesh(new THREE.SphereGeometry(0.075, 20, 14), m.glass); e.position.set(sx * 0.28, 0.0, 0.64); root.add(e);
    const r = new THREE.Mesh(new THREE.TorusGeometry(0.08, 0.015, 10, 24), m.steel); r.position.set(sx * 0.28, 0.0, 0.62); root.add(r);
  }
  // 가드 (덕트) 넷
  for (const [sx, sz] of [[1, 1], [-1, 1], [1, -1], [-1, -1]]) {
    const cx = sx * 1.28, cz = sz * 0.98, R = 0.72;
    const g = new THREE.Group(); g.position.set(cx, 0.02, cz);
    const wall = new THREE.Mesh(new THREE.CylinderGeometry(R, R, 0.3, 48, 1, true), m.lightRing); wall.material = m.lightRing.clone(); wall.material.side = THREE.DoubleSide; g.add(wall);
    for (const y of [0.15, -0.15]) { const t = new THREE.Mesh(new THREE.TorusGeometry(R, 0.045, 12, 48), m.light); t.rotation.x = Math.PI / 2; t.position.y = y; g.add(t); }
    const s1 = new THREE.Mesh(new RoundedBoxGeometry(2 * R, 0.04, 0.07, 2, 0.015), m.spoke); s1.position.y = 0.11; g.add(s1);
    const s2 = s1.clone(); s2.rotation.y = Math.PI / 2; g.add(s2);
    const p = makeProp(0.6, m); p.position.y = 0.1; g.add(p); props.push([p, (sx * sz) > 0 ? 1 : -1]);
    // 몸체와 잇는 팔
    const arm = new THREE.Mesh(new RoundedBoxGeometry(0.5, 0.2, 0.34, 4, 0.06), m.lightRing);
    arm.position.set(-sx * 0.58, 0, -sz * 0.22); g.add(arm);
    // 짧은 다리
    const leg = new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.065, 0.14, 12), m.plate); leg.position.set(0, -0.22, sz * 0.5); g.add(leg);
    const led = new THREE.Mesh(new THREE.SphereGeometry(0.035, 12, 10), sz > 0 ? m.white : m.red); led.position.set(sx * 0.6, 0.0, sz * (R + 0.02)); g.add(led);
    root.add(g);
  }
  shadowed(root);
  return { root, props, kind: '360' };
}

let envCache = null;
function create(canvas, kind) {
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
    const key = new THREE.DirectionalLight(0xffffff, 1.7); key.position.set(3, 6, 4); key.castShadow = true;
    key.shadow.mapSize.set(1024, 1024); key.shadow.radius = 6; key.shadow.bias = -0.0004;
    Object.assign(key.shadow.camera, { left: -4, right: 4, top: 4, bottom: -4, near: 1, far: 16 });
    scene.add(key);
    const rim = new THREE.DirectionalLight(kind === 'mapping' ? 0x38d6f0 : 0xffb25a, 1.1); rim.position.set(-4, 2.5, -3); scene.add(rim);
    const floor = new THREE.Mesh(new THREE.PlaneGeometry(14, 14).rotateX(-Math.PI / 2), new THREE.ShadowMaterial({ opacity: 0.5 }));
    floor.position.y = -2.1; floor.receiveShadow = true; scene.add(floor);
    const cam = new THREE.PerspectiveCamera(30, 1.5, 0.1, 60);
    let W = 1, H = 1;
    const place = () => {
      const t2 = Math.tan(THREE.MathUtils.degToRad(cam.fov / 2));
      const need = kind === 'mapping' ? [6.2, 4.3] : [6.4, 4.1];
      const d = Math.max(need[0] / (2 * t2 * cam.aspect), need[1] / (2 * t2));
      cam.position.set(0, d * 0.2, d); cam.lookAt(0, 0.35, 0);
    };
    return {
      resize(w, h, dpr) { W = w; H = h; renderer.setPixelRatio(dpr || 1); renderer.setSize(w, h, false); cam.aspect = w / h; cam.updateProjectionMatrix(); place(); },
      render(t) {
        for (const [p, dirn] of model.props) p.userData.spin.rotation.y = t * 38 * dirn;
        const r = model.root;
        if (kind === 'mapping') {
          holder.position.y = Math.sin(t * 1.2) * 0.1;
          holder.rotation.set(0.1 + Math.sin(t * 0.8) * 0.05, 0.35 + Math.sin(t * 0.35) * 0.65, Math.sin(t * 0.6) * 0.07);
        } else {
          holder.position.y = Math.sin(t * 1.3) * 0.12;
          holder.rotation.set(0.06 + Math.sin(t * 0.7) * 0.04, t * 0.32, Math.sin(t * 0.5) * 0.05);
        }
        r.rotation.y = 0;
        renderer.render(scene, cam);
      },
      dispose() { renderer.dispose(); },
    };
  } catch (e) { try { renderer.dispose(); } catch (e2) { /* 무시 */ } return null; }
}

window.Drone3D = { create };
