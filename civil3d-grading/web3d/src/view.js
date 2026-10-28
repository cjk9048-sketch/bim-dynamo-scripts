// 화면 — 그리는 틀 · 사진기(Z가 위) · 둘러보기 · 빛 · 그림자 · 필요할 때만 그리기.
// ★[JACK 1027 «텍스트와 전체적인 해상도 · 좀 만화같이 나와»] 사실감 — 물리 재질(Standard)과 영화식 색 보정(ACES) · 하늘빛 주변 조명(Sky → PMREM) ·
//   먼 곳 대기 흐림(fog) · 화면보다 크게 그려 매끄럽게(배율 1이면 1.25배).
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { Sky } from 'three/addons/objects/Sky.js';

export class View {
  constructor(host) {
    this.host = host;
    // ★[계획 검토 낮음 13] 로그 깊이 — 1 km 밖에서도 2 cm 무늬 · 1 cm 꼭대기 올림이 지형과 깜빡이지 않게
    //   ★[v108 계획 검토 높음 2] 스텐실 — 3D 자르기에서 옹벽 잘린 면 채움 · 높음 1 배경은 장면 안(사진 · 영상에 하늘이 찍히게 — 투명 캔버스 아님)
    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false, preserveDrawingBuffer: true, logarithmicDepthBuffer: true, stencil: true });
    this.renderer.localClippingEnabled = true;
    this.lost = false;
    this.renderer.domElement.addEventListener('webglcontextlost', (e) => { e.preventDefault(); this.lost = true; if (this.onLost) this.onLost(); });
    this.renderer.domElement.addEventListener('webglcontextrestored', () => { this.lost = false; if (this.onRestored) this.onRestored(); this.request(); });
    this.pixelRatio = Math.min(2, Math.max(window.devicePixelRatio || 1, 1.25));
    this.renderer.setPixelRatio(this.pixelRatio);
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFShadowMap;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.05;
    host.appendChild(this.renderer.domElement);

    this.scene = new THREE.Scene();
    // 하늘빛 주변 조명 — 물리 하늘 그림을 한 번 구워(PMREM) 재질이 하늘을 비추게 · 배경은 CSS 하늘색 그대로(대기 흐림 색과 맞춤)
    try {
      const sky = new Sky();
      sky.scale.setScalar(1000);
      const u = sky.material.uniforms;
      u.turbidity.value = 4; u.rayleigh.value = 1.2; u.mieCoefficient.value = 0.004; u.mieDirectionalG.value = 0.8;
      u.sunPosition.value.set(-0.45, -0.55, 0.70);
      u.up.value.set(0, 0, 1);
      const skyScene = new THREE.Scene();
      skyScene.add(sky);
      const pm = new THREE.PMREMGenerator(this.renderer);
      this.scene.environment = pm.fromScene(skyScene, 0.02).texture;
      this.scene.environmentIntensity = 0.55;
      pm.dispose();
    } catch { /* 하늘빛을 못 구우면 빛만으로 */ }
    this.scene.background = skyGradient();
    this.fogColor = new THREE.Color(0xd9e4ee);
    this.scene.fog = new THREE.Fog(this.fogColor, 1000, 20000);
    THREE.Object3D.DEFAULT_UP.set(0, 0, 1);
    this.camera = new THREE.PerspectiveCamera(45, 1, 0.1, 10000);
    this.camera.up.set(0, 0, 1);

    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.mouseButtons = { LEFT: THREE.MOUSE.ROTATE, MIDDLE: THREE.MOUSE.PAN, RIGHT: THREE.MOUSE.PAN };
    this.controls.zoomToCursor = true;
    this.controls.screenSpacePanning = false;     // 옮기기는 수평면 위에서(InfraWorks처럼)
    this.controls.maxPolarAngle = Math.PI * 0.497;
    this.controls.enableDamping = false;
    this.controls.addEventListener('change', () => this.request());

    // 빛 — 하늘빛 + 해(그림자) + 반대편 보조 빛(해 반대쪽 옹벽 앞면이 검게 죽지 않게). 해 그림자 상자는 보는 자리를 따라간다.
    this.hemi = new THREE.HemisphereLight(0xeef4fb, 0x9c8f78, 1.1);
    this.scene.add(this.hemi);
    this.fill = new THREE.DirectionalLight(0xdfe8f3, 0.55);
    this.fill.position.set(0.5, 0.6, 0.45);
    this.scene.add(this.fill);
    this.sun = new THREE.DirectionalLight(0xfff4e5, 2.6);
    this.sun.castShadow = true;
    this.sun.shadow.mapSize.set(4096, 4096);
    this.sun.shadow.bias = -0.0004;
    this.sun.shadow.normalBias = 0.05;
    this.scene.add(this.sun, this.sun.target);
    this.sunDir = new THREE.Vector3(-0.45, -0.55, 0.70).normalize();   // 남서쪽 높은 해(북쪽 벽도 너무 어둡지 않게)
    this._sun0 = { dir: this.sunDir.clone(), sun: this.sun.intensity, hemi: this.hemi.intensity, fill: this.fill.intensity, env: this.scene.environmentIntensity, color: this.sun.color.clone() };
    this.shadowFocus = null;      // 해 그림자 보기 — { c: Vector3, r } (현장 + 그림자 길이를 늘 덮음) · 없으면 보는 자리를 따라감
    this.locked = false;          // 사진 · 영상 찍는 동안 — 창 크기 바뀜을 따르지 않음

    this._need = true;
    this._listeners = [];
    this._anims = [];
    this._tAnim = 0;
    const ro = new ResizeObserver(() => this.resize());
    ro.observe(host);
    this.resize();
    const loop = (now) => {
      requestAnimationFrame(loop);
      // 움직이는 것(빗물 흐름 · 고임)이 있으면 초당 30장 — 그것 때문에만 그리는 장은 그림자 지도를 다시 굽지 않는다(물은 그림자를 안 만든다)
      let anim = false;
      if (this._anims.length && now - this._tAnim >= 32) {
        for (const f of this._anims) if (f(now / 1000)) anim = true;
        if (anim) this._tAnim = now;
      }
      if (this._need) { this._need = false; this.draw(); }
      else if (anim) this.draw(true);
    };
    requestAnimationFrame(loop);
  }

  onDraw(fn) { this._listeners.push(fn); }
  // fn(초) → 그리는 중이면 true(보이지 않으면 false — 그때는 다시 그리지 않는다)
  addAnim(fn) { this._anims.push(fn); }
  request() { this._need = true; }

  resize() {
    if (this.locked) return;
    const w = Math.max(1, this.host.clientWidth), h = Math.max(1, this.host.clientHeight);
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
    this.request();
  }

  // 가까운 면 · 먼 면을 보는 거리에 맞춘다(로그 깊이와 함께)
  _fitClip() {
    const d = this.camera.position.distanceTo(this.controls.target);
    this.camera.near = Math.max(0.02, d * 0.001);
    this.camera.far = Math.max(2000, d * 40 + (this.siteSize || 1000) * 3);
    this.camera.updateProjectionMatrix();
    // 대기 흐림 — 보는 거리의 몇 배 너머부터 서서히(가까운 현장은 또렷 · 먼 산은 옅게)
    if (this.scene.fog) { this.scene.fog.near = Math.max(300, d * 2.5); this.scene.fog.far = Math.max(3000, d * 14 + (this.siteSize || 1000)); }
  }

  // 사진기 자리 — 도면 좌표 기준으로 꺼내고 되돌린다(새로고침 때 그대로)
  pose() {
    const p = this.camera.position, t = this.controls.target;
    return { p: [p.x, p.y, p.z], t: [t.x, t.y, t.z] };
  }

  setPose(s) {
    this.camera.position.set(s.p[0], s.p[1], s.p[2]);
    this.controls.target.set(s.t[0], s.t[1], s.t[2]);
    this.camera.lookAt(this.controls.target);
    this.controls.update();
    this.request();
  }

  _fitShadow() {
    const F = this.shadowFocus;
    const t = F ? F.c : this.controls.target;
    const d = this.camera.position.distanceTo(this.controls.target);
    const half = F ? F.r : THREE.MathUtils.clamp(d * 0.9, 30, 1500);
    const cam = this.sun.shadow.camera;
    cam.left = -half; cam.right = half; cam.top = half; cam.bottom = -half;
    cam.near = 1; cam.far = half * 6 + 2000;
    cam.updateProjectionMatrix();
    // 그림자 칸 크기에 맞춘 법선 밀기 — 상자가 커지면(해 그림자 · 낮은 해) 칸도 커져 땅에 얼룩(그림자 여드름)이 진다
    this.sun.shadow.normalBias = Math.max(0.05, (2 * half / this.sun.shadow.mapSize.x) * 1.5);
    this.sun.position.copy(t).addScaledVector(this.sunDir, half * 3 + 800);
    this.sun.target.position.copy(t);
    this.sun.target.updateMatrixWorld();
  }

  draw(animOnly = false) {
    this._fitClip();
    if (this.sun.castShadow && !animOnly) this._fitShadow();
    this.renderer.shadowMap.autoUpdate = !animOnly;
    for (const f of this._listeners) f();
    this.renderer.render(this.scene, this.camera);
  }

  // 해 — 방향(북 = +y · 동 = +x · 위 = +z · 단위 벡터)과 세기 0~1(지면 0 · 낮게 뜨면 붉고 약함). null이면 처음 해.
  setSun(dir, k = 1) {
    const s0 = this._sun0;
    if (!dir) {
      this.sunDir.copy(s0.dir);
      this.sun.intensity = s0.sun; this.hemi.intensity = s0.hemi; this.fill.intensity = s0.fill; this.scene.environmentIntensity = s0.env;
      this.sun.color.copy(s0.color);
    } else {
      this.sunDir.copy(dir).normalize();
      // 해 그림자 보기 — 보조 빛을 줄여 그림자가 또렷하게 · 해가 지면 하늘빛만 어둡게
      this.sun.intensity = s0.sun * k;
      this.hemi.intensity = s0.hemi * (0.35 + 0.65 * k);
      this.fill.intensity = s0.fill * 0.35 * k;
      this.scene.environmentIntensity = s0.env * (0.4 + 0.6 * k);
      this.sun.color.copy(s0.color).lerp(new THREE.Color(0xffb27a), 1 - Math.min(1, k * 1.6));
    }
    this.request();
  }

  // 그리는 크기를 잠시 바꿔 한 장 — 사진(배율 2) · 영상(1920×1080) · fn은 그린 뒤 캔버스로 할 일
  async renderAt(w, h, pr, fn) {
    const r = this.renderer, cam = this.camera;
    const pr0 = r.getPixelRatio(), aspect0 = cam.aspect;
    this.locked = true;
    try {
      r.setPixelRatio(pr);
      r.setSize(w, h, false);
      cam.aspect = w / h;
      cam.updateProjectionMatrix();
      this.draw();
      return await fn(r.domElement);
    } finally {
      r.setPixelRatio(pr0);
      cam.aspect = aspect0;
      this.locked = false;
      this.resize();
      this.draw();
    }
  }

  setShadows(on) {
    this.sun.castShadow = on;
    this.request();
  }

  // 보기 — 상자(로컬 좌표 [minx,miny,minz,maxx,maxy,maxz])가 화면에 들어오게
  fit(box, mode = 'iso') {
    const c = new THREE.Vector3((box[0] + box[3]) / 2, (box[1] + box[4]) / 2, (box[2] + box[5]) / 2);
    const r = Math.max(5, Math.hypot(box[3] - box[0], box[4] - box[1], box[5] - box[2]) / 2);
    const dist = r / Math.sin(THREE.MathUtils.degToRad(this.camera.fov / 2)) * 1.05;
    let dir;
    if (mode === 'top') dir = new THREE.Vector3(0, -0.001, 1);
    else dir = new THREE.Vector3(-0.35, -0.75, 0.55);
    dir.normalize();
    this.controls.target.copy(c);
    this.camera.position.copy(c).addScaledVector(dir, dist);
    this.camera.lookAt(c);
    this.controls.update();
    this.request();
  }

  // 북쪽이 위로 — 지금 높이 · 거리는 그대로, 방위만 남쪽에서 보게
  north() {
    const t = this.controls.target, p = this.camera.position;
    const off = p.clone().sub(t);
    const horiz = Math.hypot(off.x, off.y);
    this.camera.position.set(t.x, t.y - Math.max(horiz, 0.001), p.z);
    this.controls.update();
    this.request();
  }

  // 그 점을 가운데로 — 사진기와 목표를 함께 옮긴다(보는 방향 그대로)
  centerOn(pt) {
    const off = this.camera.position.clone().sub(this.controls.target);
    const from = this.controls.target.clone(), to = pt.clone();
    const t0 = performance.now(), dur = 350;
    const step = () => {
      const k = Math.min(1, (performance.now() - t0) / dur);
      const e = k * k * (3 - 2 * k);
      this.controls.target.lerpVectors(from, to, e);
      this.camera.position.copy(this.controls.target).add(off);
      this.controls.update();
      this.request();
      if (k < 1) requestAnimationFrame(step);
    };
    step();
  }

  // 방위각(도 · 북 = 0 · 시계 방향) — 나침반
  heading() {
    const off = this.controls.target.clone().sub(this.camera.position);
    return THREE.MathUtils.radToDeg(Math.atan2(off.x, off.y));
  }
}

// 배경 하늘 — 위 짙은 하늘색 → 지평선 옅은 색(CSS 배경과 같은 색 · 대기 흐림 색과 맞춤) · 화면에 붙은 그림이라 사진 · 영상에도 찍힌다
function skyGradient() {
  const cv = document.createElement('canvas');
  cv.width = 4; cv.height = 512;
  const g = cv.getContext('2d');
  const gr = g.createLinearGradient(0, 0, 0, 512);
  gr.addColorStop(0, '#6f9fd3'); gr.addColorStop(0.38, '#a9c6e6'); gr.addColorStop(0.62, '#d9e4ee'); gr.addColorStop(1, '#e4eaee');
  g.fillStyle = gr; g.fillRect(0, 0, 4, 512);
  const t = new THREE.CanvasTexture(cv);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}
