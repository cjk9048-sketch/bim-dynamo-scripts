// ★★[v108 · 계획_v108.md 2.4 · 2.5 · 계획 검토 중간 8 · 낮음 11] 깨끗한 사진 · 드론 비행 영상 — 캔버스(3D)만 찍는다(단추 · 범례 · 풍선은 화면 칸이라 안 찍힘).
//   사진 = 화면의 2배(긴 변 3840까지) PNG · 영상 = 현장 가운데를 20초에 한 바퀴(높이 30°) 1920×1080 · 정해진 600장을 한 장씩 그려 H.264(WebCodecs) → MP4
//   (그리기가 느려도 영상은 매끄럽다) · H.264가 안 되면 실시간 녹화(MediaRecorder · WebM).
//   저장 = 브라우저 내려받기 — Civil 안에서는 C#가 내려받기를 가로채 저장 대화상자(조각으로 넘기지 않음).
import * as THREE from 'three';
import { Muxer, ArrayBufferTarget } from 'mp4-muxer';

const pad = (n) => String(n).padStart(2, '0');
const stamp = () => { const d = new Date(); return `${String(d.getFullYear()).slice(2)}${pad(d.getMonth() + 1)}${pad(d.getDate())}_${pad(d.getHours())}${pad(d.getMinutes())}`; };
const safe = (s) => String(s || '3D').replace(/\.[^.]+$/, '').replace(/[\\/:*?"<>|\s]+/g, '_').slice(0, 40) || '3D';
export const fileName = (kind, dwg, ext) => `3D${kind}_${safe(dwg)}_${stamp()}.${ext}`;

let lastUrl = null;
// 내려받기 — 주소는 다음 내려받기 때 놓는다(저장 대화상자가 오래 떠 있어도 끊기지 않게)
export function download(blob, name) {
  if (lastUrl) URL.revokeObjectURL(lastUrl);
  lastUrl = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = lastUrl; a.download = name;
  document.body.appendChild(a); a.click(); a.remove();
}

// 찍는 동안 숨길 것 — 누른 점 · 재기 선(풍선은 화면 칸이라 원래 안 찍힘)
function hideMarks(view) {
  const hidden = [];
  view.scene.traverse(o => { if (o.userData && o.userData.mark && o.visible) { o.visible = false; hidden.push(o); } });
  return () => { for (const o of hidden) o.visible = true; };
}

export async function photo(view) {
  const host = view.host, w = Math.max(1, host.clientWidth), h = Math.max(1, host.clientHeight);
  const gl = view.renderer.getContext();
  const maxRb = Math.min(gl.getParameter(gl.MAX_RENDERBUFFER_SIZE), ...gl.getParameter(gl.MAX_VIEWPORT_DIMS));
  const pr = Math.min(2, 3840 / Math.max(w, h), maxRb / Math.max(w, h));
  const restore = hideMarks(view);
  try {
    return await view.renderAt(w, h, pr, (cv) => new Promise((res, rej) => cv.toBlob(b => (b ? res(b) : rej(new Error('그림을 못 만들었습니다'))), 'image/png')));
  } finally { restore(); }
}

// H.264 — High · Main · Baseline(수준 4.0 = 1080p30)
async function pickCodec(w, h, fps) {
  if (typeof VideoEncoder === 'undefined') return null;
  for (const codec of ['avc1.640028', 'avc1.4d0028', 'avc1.42e028']) {
    const cfg = { codec, width: w, height: h, bitrate: 8_000_000, framerate: fps, avc: { format: 'avc' } };
    try { const s = await VideoEncoder.isConfigSupported(cfg); if (s.supported) return s.config; } catch { /* 다음 */ }
  }
  return null;
}

// 비행 — 현장 가운데 c를 거리 d · 높이 el°로 한 바퀴(t = 0~1 · 시작 방위 az0)
function orbitCam(view, c, d, el, az0, t) {
  const az = az0 + t * Math.PI * 2, e = THREE.MathUtils.degToRad(el);
  view.camera.position.set(c.x - Math.sin(az) * Math.cos(e) * d, c.y - Math.cos(az) * Math.cos(e) * d, c.z + Math.sin(e) * d);
  view.camera.lookAt(c);
}

// 영상 — o: { focus, seconds, fps, w, h, frames(시험), onProgress(0~1), cancelled() } → { blob, ext, codec }
export async function video(view, o) {
  const fps = o.fps || 30, seconds = o.seconds || 20, w = o.w || 1920, h = o.h || 1080;
  const frames = o.frames || seconds * fps;
  const f = o.focus;
  const c = new THREE.Vector3((f[0] + f[3]) / 2, (f[1] + f[4]) / 2, (f[2] + f[5]) / 2);
  const r = Math.max(5, Math.hypot(f[3] - f[0], f[4] - f[1], f[5] - f[2]) / 2);
  const d = r / Math.sin(THREE.MathUtils.degToRad(view.camera.fov / 2)) * 1.05;
  // 시작 방위 = 지금 보는 방향(화면이 튀지 않게)
  const off = view.controls.target.clone().sub(view.camera.position);
  const az0 = Math.atan2(off.x, off.y);
  const pose0 = view.pose();
  const restore = hideMarks(view);
  const cv = view.renderer.domElement;
  cv.style.objectFit = 'contain';
  try {
    const cfg = await pickCodec(w, h, fps);
    if (cfg) {
      const target = new ArrayBufferTarget();
      const muxer = new Muxer({ target, video: { codec: 'avc', width: w, height: h, frameRate: fps }, fastStart: 'in-memory' });
      let err = null;
      const enc = new VideoEncoder({ output: (chunk, meta) => { try { muxer.addVideoChunk(chunk, meta); } catch (e) { err = e; } }, error: (e) => { err = e; } });
      let done = 0;
      try {
        enc.configure(cfg);
        await view.renderAt(w, h, 1, async (canvas) => {
          for (let i = 0; i < frames; i++) {
            if (o.cancelled && o.cancelled()) break;
            if (err) throw err;
            orbitCam(view, c, d, 30, az0, i / (seconds * fps));
            view.draw();
            const vf = new VideoFrame(canvas, { timestamp: Math.round(i * 1e6 / fps), duration: Math.round(1e6 / fps) });
            try { enc.encode(vf, { keyFrame: i % (fps * 2) === 0 }); } finally { vf.close(); }
            done = i + 1;
            while (enc.encodeQueueSize > 6) await new Promise(res => setTimeout(res, 4));
            if (i % 6 === 0) { if (o.onProgress) o.onProgress(i / frames); await new Promise(res => setTimeout(res, 0)); }
          }
        });
        await enc.flush();
      } finally {
        if (enc.state !== 'closed') enc.close();
      }
      if (err) throw err;
      if (done === 0) return null;
      muxer.finalize();
      return { blob: new Blob([target.buffer], { type: 'video/mp4' }), ext: 'mp4', codec: cfg.codec, frames: done };
    }
    // 대체 — 실시간 녹화(WebM) · 20초 걸림
    const mime = ['video/webm;codecs=vp9', 'video/webm;codecs=vp8', 'video/webm'].find(m => window.MediaRecorder && MediaRecorder.isTypeSupported(m));
    if (!mime) throw new Error('이 브라우저는 영상 녹화를 못 합니다');
    return await view.renderAt(w, h, 1, (canvas) => new Promise((res, rej) => {
      const rec = new MediaRecorder(canvas.captureStream(fps), { mimeType: mime, videoBitsPerSecond: 8_000_000 });
      const parts = [];
      rec.ondataavailable = (e) => { if (e.data && e.data.size) parts.push(e.data); };
      rec.onstop = () => res(parts.length ? { blob: new Blob(parts, { type: 'video/webm' }), ext: 'webm', codec: mime, frames: 0 } : null);
      rec.onerror = (e) => rej(e.error || new Error('녹화 실패'));
      const t0 = performance.now(), dur = (frames / fps) * 1000;
      rec.start(500);
      const step = () => {
        const t = (performance.now() - t0) / dur;
        if (t >= 1 || (o.cancelled && o.cancelled())) { rec.stop(); return; }
        orbitCam(view, c, d, 30, az0, t * frames / (seconds * fps));
        view.draw();
        if (o.onProgress) o.onProgress(t);
        requestAnimationFrame(step);
      };
      requestAnimationFrame(step);
    }));
  } finally {
    cv.style.objectFit = '';
    restore();
    view.setPose(pose0);
  }
}
