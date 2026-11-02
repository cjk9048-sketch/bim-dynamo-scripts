// ★[v111.7.2 · JACK 1102 «건물이 너무 회색 민짜야 — 랜덤하게 건물 표면도 렌더해줘, 건물처럼»] 건물 겉면 · 지붕
//   겉면 = 그림 파일 없이 캔버스로 그린 무늬(창 모듈 넷 × 한 층 = 512 × 128 px) · 형식마다 «보통 층» + «1층» → 2D 배열 질감 한 장
//   (한 칸씩 따로 반복 · 밉맵 · 이방성 — 아틀라스 이음매 · 번짐 없음) · RGB = 겉 색 · A = 유리(1)
//   셰이더 — u = 벽 길이 ÷ 모듈(벽마다 정수 개 · 모서리에 창이 안 잘림) · v = 높이 ÷ 층(3.5 m) · 창마다 커튼 · 블라인드 · 어둔 방 · 불 켠 방
//   (창이 화면에서 작아지면 고르게 — 먼 곳 반짝임 막기) · 유리만 매끈하게(하늘이 비침) · 땅 가까이 살짝 어둡게
import * as THREE from 'three';

const TW = 512, TH = 128, MODS = 4, MX = TW / MODS;
export const FLOOR_H = 3.5;

// 결정적 난수 — 같은 건물은 늘 같은 모습(다시 만들어도 · 다시 열어도)
function hash2(x, y) {
  let h = Math.imul(x | 0, 0x27d4eb2d) ^ Math.imul((y | 0) + 0x9e3779b9, 0x165667b1);
  h = Math.imul(h ^ (h >>> 15), 0x85ebca6b);
  h = Math.imul(h ^ (h >>> 13), 0xc2b2ae35);
  return (h ^ (h >>> 16)) >>> 0;
}
function rng(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// ── 그리기 도구 — x = 모듈(0~4) · y = 층 안 높이(0 = 바닥 · 1 = 위) · 무늬 판(c)과 유리 판(m)을 같이 칠한다 ──
class Painter {
  constructor(c, m, rnd) { this.c = c; this.m = m; this.rnd = rnd; }
  _rect(g, x0, y0, x1, y1) {
    // 무늬 끝을 넘으면 반대쪽에도(가로로 이어 붙여도 이음매 없게)
    for (const s of [0, -MODS, MODS]) {
      const a = Math.max(0, x0 + s), b = Math.min(MODS, x1 + s);
      if (b > a) g.fillRect(a * MX, (1 - y1) * TH, (b - a) * MX, (y1 - y0) * TH);
    }
  }
  fill(col, x0, y0, x1, y1, glass = 0) {
    this.c.fillStyle = col; this._rect(this.c, x0, y0, x1, y1);
    this.m.fillStyle = glass ? '#fff' : '#000'; this._rect(this.m, x0, y0, x1, y1);
  }
  // 유리 — 위가 밝게(하늘이 비침)
  glass(x0, y0, x1, y1, top, bot) {
    const gr = this.c.createLinearGradient(0, (1 - y1) * TH, 0, (1 - y0) * TH);
    gr.addColorStop(0, top); gr.addColorStop(1, bot);
    this.c.fillStyle = gr; this._rect(this.c, x0, y0, x1, y1);
    this.m.fillStyle = '#fff'; this._rect(this.m, x0, y0, x1, y1);
  }
  px(n) { return n / MX; }
  py(n) { return n / TH; }
  // 창 — 유리 + 틀(px 굵기) + 가운데 살 · 위에 그늘 한 줄(창이 벽 안으로 들어가 보이게)
  win(x0, y0, x1, y1, o = {}) {
    const fw = o.fw ?? 4, fx = this.px(fw), fy = this.py(fw), frame = o.frame || '#f2f1ed';
    this.c.fillStyle = 'rgba(0,0,0,0.10)'; this._rect(this.c, x0, y1, x1, Math.min(1, y1 + this.py(3)));
    this.glass(x0, y0, x1, y1, o.top || '#62707a', o.bot || '#2c3842');
    this.fill(frame, x0, y0, x1, y0 + fy); this.fill(frame, x0, y1 - fy, x1, y1);
    this.fill(frame, x0, y0, x0 + fx, y1); this.fill(frame, x1 - fx, y0, x1, y1);
    for (const mx of o.mull || []) this.fill(frame, mx - fx * 0.4, y0, mx + fx * 0.4, y1);
    for (const ty of o.tran || []) this.fill(frame, x0, ty - fy * 0.4, x1, ty + fy * 0.4);
    if (o.sill) this.fill(o.sill, x0 - this.px(3), y0 - this.py(4), x1 + this.px(3), y0);
  }
  // 낱칸 무늬(판석 · 패널) — 칸마다 밝기를 조금씩
  panels(col, nx, ny, jit, joint) {
    const base = new THREE.Color(col);
    for (let j = 0; j < ny; j++)
      for (let i = 0; i < nx * MODS; i++) {
        const k = 1 + (this.rnd() - 0.5) * jit;
        this.fill('#' + base.clone().multiplyScalar(k).getHexString(), i / nx, j / ny, (i + 1) / nx, (j + 1) / ny);
      }
    if (!joint) return;
    for (let i = 0; i <= nx * MODS; i++) this.fill(joint, i / nx - this.px(0.5), 0, i / nx + this.px(0.5), 1);
    for (let j = 0; j <= ny; j++) this.fill(joint, 0, j / ny - this.py(0.5), MODS, j / ny + this.py(0.5));
  }
  // 벽돌(길이 = 무늬 폭 ÷ 40 · 한 켜 4 px) — 켜마다 반 장 엇갈림 · 장마다 색을 조금씩
  bricks(base, mortar) {
    const b = new THREE.Color(base);
    this.c.fillStyle = mortar; this.c.fillRect(0, 0, TW, TH);
    const n = 40, bw = TW / n;
    for (let r = 0; r < TH / 4; r++) {
      const off = (r % 2) * bw / 2;
      for (let i = -1; i < n; i++) {
        const x0 = Math.round(off + i * bw), x1 = Math.round(off + (i + 1) * bw) - 1;
        const k = 0.86 + this.rnd() * 0.26 - (this.rnd() < 0.08 ? 0.18 : 0);
        this.c.fillStyle = '#' + b.clone().multiplyScalar(k).getHexString();
        // 무늬 끝을 넘는 장은 반대쪽에 이어 그림
        for (const s of [0, TW]) this.c.fillRect(x0 - s, r * 4, x1 - x0, 3);
      }
    }
    this.m.fillStyle = '#000'; this.m.fillRect(0, 0, TW, TH);
  }
  // 1층 가게 — 통유리 + 간판 띠(칸마다 차분한 색 · 글자 자리)
  shops(wall, frame, signs) {
    this.fill(wall, 0, 0, MODS, 1);
    for (let i = 0; i < MODS; i++) {
      this.win(i + 0.04, 0.05, i + 0.96, 0.74, { frame, fw: 3, mull: [i + 0.5], top: '#5d6a73', bot: '#232c33' });
      const sc = signs[Math.floor(this.rnd() * signs.length)];
      this.fill(sc, i + 0.02, 0.79, i + 0.98, 0.95);
      const dark = new THREE.Color(sc).getHSL({}).l < 0.5;     // 어두운 간판 = 밝은 글자
      const n = 2 + Math.floor(this.rnd() * 4), w = 0.07 + this.rnd() * 0.05, x = i + 0.5 - (n * w * 1.25) / 2;
      for (let t = 0; t < n; t++) this.fill(dark ? '#e9e6dd' : '#30343a', x + t * w * 1.25, 0.83, x + t * w * 1.25 + w, 0.91);
    }
  }
}

const SIGNS = ['#2f4a6d', '#7b3430', '#2f5d48', '#b08a35', '#e7e4dc', '#3a3d42', '#8a5a2b', '#e7e4dc'];

// 아파트 발코니 — 아래 난간 판(불투명 · 옅은 유리색) + 난간 손잡이 + 위 미닫이 통창 · 층마다 가로 띠로 읽힌다
function balcony(p, x0, x1, wall, frame) {
  p.c.fillStyle = 'rgba(0,0,0,0.09)'; p._rect(p.c, x0, 0.86, x1, 0.89);
  p.fill('#b7c0c5', x0, 0.1, x1, 0.35);
  p.fill('#a3adb3', x0, 0.1, x1, 0.13);
  p.win(x0, 0.37, x1, 0.86, { frame, mull: [(x0 + x1) / 2, x0 + (x1 - x0) * 0.25, x0 + (x1 - x0) * 0.75].slice(0, x1 - x0 > 0.7 ? 3 : 1), top: '#9aa8b1', bot: '#4c5963' });
  p.fill(frame, x0 - p.px(2), 0.34, x1 + p.px(2), 0.37);
  p.fill(wall, x0 - p.px(4), 0.1 - p.py(2), x0, 0.88); p.fill(wall, x1, 0.1 - p.py(2), x1 + p.px(4), 0.88);
}
// 실외기실 갤러리(가로 살)
function louver(p, x0, y0, x1, y1) {
  p.fill('#cfd0cd', x0, y0, x1, y1);
  for (let y = y0 + p.py(3); y < y1 - p.py(1); y += p.py(5)) p.fill('#aeb0ad', x0, y, x1, y + p.py(1.5));
}

// ── 형식 — mod = 창 모듈(m) · roof = 지붕 색 후보 · typ / gnd = 보통 층 · 1층 그리기 ──
const V = [
  { // 0 흰 아파트 — 발코니 셋 + 방 창 · 실외기실
    mod: 3.0, roof: ['#a6a8a5', '#9ea19e', '#b0b1ad', '#8f9a8c'],
    typ(p) {
      p.fill('#eeede8', 0, 0, 4, 1);
      p.fill('#e1dfd9', 0, 0, 4, 0.08);
      p.fill('#d3d1ca', 0, 0.985, 4, 1);
      for (const i of [0, 1, 2]) balcony(p, i + 0.06, i + 0.94, '#eeede8', '#f6f5f2');
      p.win(3.3, 0.34, 3.82, 0.84, { frame: '#f6f5f2', mull: [3.56], sill: '#dddbd4', top: '#9aa8b1', bot: '#4c5963' });
      louver(p, 3.06, 0.12, 3.22, 0.62);
    },
    gnd(p) {
      p.fill('#eeede8', 0, 0, 4, 1);
      p.fill('#a39e94', 0, 0, 4, 0.13);
      p.fill('#c9c7c0', 0.06, 0.8, 0.94, 0.86);
      p.win(0.2, 0.13, 0.8, 0.78, { frame: '#5c5f61', fw: 3, mull: [0.5], top: '#5d6b75', bot: '#20282e' });
      for (const i of [1, 2]) p.win(i + 0.08, 0.38, i + 0.92, 0.86, { frame: '#f6f5f2', mull: [i + 0.5], top: '#9aa8b1', bot: '#4c5963' });
      p.win(3.3, 0.38, 3.82, 0.84, { frame: '#f6f5f2', mull: [3.56], top: '#9aa8b1', bot: '#4c5963' });
    },
  },
  { // 1 베이지 아파트 — 세대 경계 띠(차분한 갈색)
    mod: 3.0, roof: ['#a9aaa6', '#9c9f9b', '#b2b2ad', '#8f9a8c'],
    typ(p) {
      p.fill('#e5e0d6', 0, 0, 4, 1);
      p.fill('#d8d2c6', 0, 0, 4, 0.08);
      p.fill('#cbc5b9', 0, 0.985, 4, 1);
      balcony(p, 0.2, 0.94, '#e5e0d6', '#f2eee7');
      p.win(1.24, 0.34, 1.76, 0.84, { frame: '#f2eee7', mull: [1.5], sill: '#d6d0c4', top: '#9aa8b1', bot: '#4c5963' });
      balcony(p, 2.06, 2.94, '#e5e0d6', '#f2eee7');
      balcony(p, 3.06, 3.8, '#e5e0d6', '#f2eee7');
      p.fill('#a08e7f', -0.1, 0, 0.1, 1);
    },
    gnd(p) {
      p.fill('#e5e0d6', 0, 0, 4, 1);
      p.fill('#9c9487', 0, 0, 4, 0.13);
      p.fill('#a08e7f', -0.1, 0, 0.1, 1);
      p.win(1.2, 0.13, 1.8, 0.78, { frame: '#5f5a52', fw: 3, mull: [1.5], top: '#5b6770', bot: '#20272c' });
      for (const [a, b] of [[0.2, 0.94], [2.06, 2.94], [3.06, 3.8]]) p.win(a, 0.38, b, 0.86, { frame: '#f2eee7', mull: [(a + b) / 2], top: '#9aa8b1', bot: '#4c5963' });
    },
  },
  { // 2 석재 판(상가 · 업무) — 낱창
    mod: 3.2, roof: ['#8b8c89', '#777875', '#999a96'],
    typ(p) {
      p.panels('#cbbda6', 2, 3, 0.07, '#b3a68f');
      for (let i = 0; i < 4; i++) p.win(i + 0.2, 0.28, i + 0.8, 0.8, { frame: '#4b4b49', fw: 3, mull: [i + 0.5], sill: '#ddd2bf', top: '#5f6c75', bot: '#2b353d' });
    },
    gnd(p) { p.shops('#bfb29b', '#3a3c3e', SIGNS); },
  },
  { // 3 붉은 벽돌 빌라 — 흰 창틀 · 1층 필로티 주차
    mod: 2.8, roof: ['#748f73', '#6b8a70', '#8b8c89', '#7d967a', '#999a96'],
    typ(p) {
      p.bricks('#8b4a3a', '#b8a796');
      for (const i of [0, 1, 3]) {
        p.fill('#74392c', i + 0.2, 0.78, i + 0.8, 0.83);
        p.win(i + 0.22, 0.3, i + 0.78, 0.78, { frame: '#ebe8e2', mull: [i + 0.5], sill: '#cfc8bd', top: '#66727b', bot: '#2e3840' });
      }
      p.win(2.34, 0.52, 2.66, 0.78, { frame: '#ebe8e2', sill: '#cfc8bd', top: '#a3abb0', bot: '#7d878d' });
    },
    gnd(p) {
      p.bricks('#8b4a3a', '#b8a796');
      p.fill('#38393a', 0.1, 0, 2.9, 0.8);
      p.fill('#2a2b2c', 0.1, 0.62, 2.9, 0.8);
      p.fill('#5b5b58', 0.1, 0, 2.9, 0.05);
      p.fill('#9a9690', 0.1, 0.8, 2.9, 0.84);
      p.fill('#7f4436', 1.44, 0, 1.56, 0.8);
      p.win(3.22, 0.0, 3.78, 0.72, { frame: '#4a4642', fw: 3, mull: [3.5], top: '#56636c', bot: '#1f262b' });
      p.fill('#c9c3b8', 3.12, 0.74, 3.88, 0.79);
    },
  },
  { // 4 유리 업무 — 커튼월(세로 살 1.5 m · 허리 띠)
    mod: 3.0, roof: ['#6a6b69', '#5f605f', '#777876'],
    typ(p) {
      p.glass(0, 0.24, 4, 1, '#7d92a2', '#4b5e70');
      p.glass(0, 0, 4, 0.24, '#3d4a56', '#35414c');
      for (let i = 0; i <= 8; i++) p.fill('#9aa2a8', i / 2 - p.px(1.6), 0, i / 2 + p.px(1.6), 1);
      p.fill('#9aa2a8', 0, 0.24 - p.py(2), 4, 0.24 + p.py(2));
      p.fill('#9aa2a8', 0, 0, 4, p.py(2.5));
      p.fill('#9aa2a8', 0, 1 - p.py(1.5), 4, 1);
    },
    gnd(p) {
      p.glass(0, 0.05, 4, 1, '#56697a', '#26313a');
      for (let i = 0; i <= 8; i++) p.fill('#8e969c', i / 2 - p.px(2), 0, i / 2 + p.px(2), 1);
      p.fill('#8e969c', 0, 0.78, 4, 0.78 + p.py(3));
      p.fill('#8f8a82', 0, 0, 4, 0.05);
      p.fill('#8e969c', 1.24, 0.05, 1.26, 0.7); p.fill('#8e969c', 1.0, 0.7 - p.py(2), 1.5, 0.7);
    },
  },
  { // 5 따뜻한 회색 콘크리트 — 띠창
    mod: 3.2, roof: ['#8b8c89', '#999a96', '#a3a4a0', '#748f73'],
    typ(p) {
      p.fill('#aaa59c', 0, 0, 4, 1);
      p.fill('#b8b3aa', 0, 0.84, 4, 1);
      p.fill('#b4afa6', 0, 0, 4, 0.06);
      p.glass(0, 0.3, 4, 0.74, '#5e6b74', '#323d46');
      p.fill('#3c3f42', 0, 0.3, 4, 0.3 + p.py(3)); p.fill('#3c3f42', 0, 0.74 - p.py(3), 4, 0.74);
      for (let i = 0; i < 8; i++) p.fill('#3c3f42', i / 2 - p.px(1.5), 0.3, i / 2 + p.px(1.5), 0.74);
      p.c.fillStyle = 'rgba(0,0,0,0.08)'; p._rect(p.c, 0, 0.74, 4, 0.77);
    },
    gnd(p) { p.shops('#a39e95', '#2f3133', SIGNS); p.fill('#b8b3aa', 0, 0.96, 4, 1); },
  },
  { // 6 흰 미장 낮은 집 — 작은 창 · 1층 문
    mod: 3.0, roof: ['#748f73', '#7d967a', '#999a96', '#c4c4bf', '#8b8c89'],
    typ(p) {
      p.fill('#dcd6ca', 0, 0, 4, 1);
      p.fill('#cfc8bb', 0, 0.95, 4, 1);
      const o = { frame: '#6b6158', fw: 3, sill: '#c6bfb2', top: '#66727a', bot: '#2f3940' };
      p.win(0.25, 0.35, 0.75, 0.78, o);
      p.win(1.15, 0.32, 1.85, 0.78, { ...o, mull: [1.5] });
      p.win(2.38, 0.55, 2.62, 0.78, { ...o, top: '#a2a9ad', bot: '#808a90' });
      p.win(3.25, 0.35, 3.75, 0.78, o);
    },
    gnd(p) {
      p.fill('#dcd6ca', 0, 0, 4, 1);
      p.fill('#a59d90', 0, 0, 4, 0.1);
      const o = { frame: '#6b6158', fw: 3, sill: '#c6bfb2', top: '#66727a', bot: '#2f3940' };
      p.win(0.25, 0.35, 0.75, 0.78, o);
      p.fill('#5d4a3a', 1.33, 0.1, 1.67, 0.72);
      p.fill('#4a3b2e', 1.33, 0.1, 1.35, 0.72); p.fill('#4a3b2e', 1.65, 0.1, 1.67, 0.72);
      p.fill('#bdb6a9', 1.25, 0.74, 1.75, 0.79);
      p.win(2.15, 0.32, 2.85, 0.78, { ...o, mull: [2.5] });
      p.win(3.25, 0.35, 3.75, 0.78, o);
    },
  },
  { // 7 짙은 금속 패널 — 세로 틈창
    mod: 2.6, roof: ['#5f605f', '#6a6b6a'],
    typ(p) {
      p.panels('#57595c', 4, 2, 0.05, '#46484b');
      const o = { frame: '#2f3133', fw: 3, top: '#6a7882', bot: '#2f3a43' };
      p.win(0.36, 0.1, 0.64, 0.92, o);
      p.win(1.12, 0.28, 1.88, 0.84, { ...o, mull: [1.5] });
      p.win(2.36, 0.1, 2.64, 0.92, o);
      p.win(3.4, 0.1, 3.6, 0.92, o);
    },
    gnd(p) { p.shops('#4d4f52', '#232527', ['#2c2e30', '#e7e4dc', '#2f4a6d', '#2c2e30']); },
  },
  { // 8 공장 · 창고 — 골 강판 · 높은 띠창 · 1층 셔터
    mod: 4.0, roof: ['#5d7489', '#6a7f92', '#8b8c89', '#999a96'],
    typ(p) {
      p.fill('#b9bfc3', 0, 0, 4, 1);
      for (let x = 0; x < TW; x += 6) { p.c.fillStyle = '#c9ced2'; p.c.fillRect(x, 0, 2, TH); p.c.fillStyle = '#a2a8ad'; p.c.fillRect(x + 4, 0, 1, TH); }
      p.glass(0, 0.7, 4, 0.86, '#93a2ac', '#6d7c87');
      for (let i = 0; i < 8; i++) p.fill('#7b8187', i / 2 - p.px(1.5), 0.7, i / 2 + p.px(1.5), 0.86);
      p.fill('#7b8187', 0, 0.7, 4, 0.7 + p.py(2)); p.fill('#7b8187', 0, 0.86 - p.py(2), 4, 0.86);
    },
    gnd(p) {
      this.typ(p);
      p.fill('#9d9a94', 0, 0, 4, 0.08);
      p.fill('#7d8388', 1.12, 0.08, 2.88, 0.84);
      p.fill('#a3a9ad', 1.16, 0.08, 2.84, 0.8);
      for (let y = 0.1; y < 0.8; y += p.py(4)) p.fill('#8d9397', 1.16, y, 2.84, y + p.py(1));
      p.fill('#6f7a83', 0.4, 0.08, 0.66, 0.66);
    },
  },
];
// 아파트 뒷면 · 옆벽(긴 벽의 반이 안 되는 끝 벽) — 벽이 많고 작은 창(부엌 · 방 · 계단) · 0 흰 · 1 베이지
const END = [
  (p) => {
    const o = { frame: '#f6f5f2', sill: '#dddbd4', top: '#9aa8b1', bot: '#5f6b73' };
    p.fill('#eeede8', 0, 0, 4, 1);
    p.fill('#e5e3dd', 0, 0, 4, 0.05);
    p.fill('#dfddd6', 1.98, 0, 2.02, 1);
    p.win(0.34, 0.48, 0.66, 0.8, o);
    p.win(1.3, 0.48, 1.62, 0.8, o);
    p.win(2.5, 0.4, 3.1, 0.82, { ...o, mull: [2.8] });
    louver(p, 3.3, 0.14, 3.46, 0.6);
  },
  (p) => {
    const o = { frame: '#f2eee7', sill: '#d6d0c4', top: '#9aa8b1', bot: '#5f6b73' };
    p.fill('#e5e0d6', 0, 0, 4, 1);
    p.fill('#dbd5ca', 0, 0, 4, 0.05);
    p.fill('#a08e7f', -0.14, 0, 0.14, 1);
    p.win(0.9, 0.4, 1.5, 0.82, { ...o, mull: [1.2] });
    p.win(1.84, 0.48, 2.16, 0.8, o);
    p.win(2.84, 0.48, 3.16, 0.8, o);
  },
];
const LAYERS = V.length * 2 + END.length;
const endLayer = (v) => V.length * 2 + v;
const CW_LAYER = 4 * 2;      // 4 유리 업무의 보통 층 칸

// 무늬 묶음 — 층마다 무늬(RGB) + 유리(A) · 행은 뒤집어 넣는다(자료 첫 행 = 층 바닥 = v 0)
function facadeTexture(view) {
  const cv = document.createElement('canvas'), mv = document.createElement('canvas');
  cv.width = mv.width = TW; cv.height = mv.height = TH;
  const c = cv.getContext('2d', { willReadFrequently: true }), m = mv.getContext('2d', { willReadFrequently: true });
  const data = new Uint8Array(TW * TH * 4 * LAYERS);
  const out = new Uint8ClampedArray(data.buffer);     // 잡티를 더해도 0~255에서 멈춤(Uint8Array는 257 → 1로 돌아감)
  const rnd = rng(0x5eed1102);
  const p = new Painter(c, m, rnd);
  const draws = [];
  for (const v of V) { draws.push((q) => v.typ(q)); draws.push((q) => v.gnd.call(v, q)); }
  for (const e of END) draws.push(e);
  draws.forEach((draw, L) => {
    c.clearRect(0, 0, TW, TH); m.fillStyle = '#000'; m.fillRect(0, 0, TW, TH);
    draw(p);
    const a = c.getImageData(0, 0, TW, TH).data, b = m.getImageData(0, 0, TW, TH).data;
    const base = L * TW * TH * 4;
    for (let y = 0; y < TH; y++) {
      const src = y * TW * 4, dst = base + (TH - 1 - y) * TW * 4;
      for (let x = 0; x < TW * 4; x += 4) {
        // 벽에 아주 옅은 잡티(콘크리트 · 미장 결) — 유리는 그대로
        const n = b[src + x] > 127 ? 0 : (rnd() - 0.5) * 7;
        out[dst + x] = a[src + x] + n; out[dst + x + 1] = a[src + x + 1] + n; out[dst + x + 2] = a[src + x + 2] + n;
        out[dst + x + 3] = b[src + x];
      }
    }
  });
  const t = new THREE.DataArrayTexture(data, TW, TH, LAYERS);
  t.format = THREE.RGBAFormat;
  t.type = THREE.UnsignedByteType;
  t.colorSpace = THREE.SRGBColorSpace;
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.magFilter = THREE.LinearFilter;
  t.minFilter = THREE.LinearMipmapLinearFilter;
  t.generateMipmaps = true;
  t.anisotropy = Math.min(8, view.renderer.capabilities.getMaxAnisotropy());
  t.needsUpdate = true;
  return t;
}

// 겉면 재료 — 점마다 facUv(u = 모듈 · v = 층) · facInfo(보통 층 칸 · 1층 칸 · 건물 씨앗 · 층수) · facTint(벽 색 보정 ÷ 2)
const FRAG_HEAD = `uniform sampler2DArray facadeMap;
varying vec2 vFacUv;
varying vec4 vFacInfo;
varying vec3 vFacTint;
float facHash( vec2 p ) { vec3 p3 = fract( vec3( p.xyx ) * 0.1031 ); p3 += dot( p3, p3.yzx + 33.33 ); return fract( ( p3.x + p3.y ) * p3.z ); }
`;
const FRAG_MAP = `
	float facFl = floor( vFacUv.y );
	vec4 facC = texture( facadeMap, vec3( vFacUv.x * 0.25, vFacUv.y, floor( ( facFl < 0.5 ? vFacInfo.y : vFacInfo.x ) + 0.5 ) ) );
	float facGlass = facC.a;
	// 창마다(반 모듈 × 층) — 커튼 · 블라인드(위에서 내려옴) · 어둔 방 · 불 켠 방 · 밝기 조금씩 / 창이 몇 px 안 되면 고르게
	//   씨앗은 정수로 — 보간된 값의 1e-7 차이도 해시가 키워 창 안이 점점이 얼룩진다(1102 화면에서 봄)
	float facSeed = floor( vFacInfo.z * 4093.0 + 0.5 );
	vec2 facCell = vec2( floor( vFacUv.x * 2.0 ) + facSeed * 3.0, facFl + facSeed * 5.0 );
	float facR = facHash( facCell ), facR2 = facHash( facCell + 17.0 );
	float facNear = 1.0 - smoothstep( 0.1, 0.35, max( fwidth( vFacUv.x ), fwidth( vFacUv.y ) ) );
	facNear *= abs( vFacInfo.x - ${CW_LAYER}.0 ) < 0.5 ? 0.35 : 1.0;     // 유리 업무(커튼월)는 창마다 덜 다르게 — 바둑판처럼 얼룩지지 않게
	float facY = fract( vFacUv.y );
	vec3 facV = facR < 0.13 ? mix( facC.rgb, vec3( 0.58, 0.52, 0.42 ), 0.85 * step( 0.4 + 0.38 * facR2, facY ) )
		: facR < 0.23 ? mix( facC.rgb, vec3( 0.66, 0.67, 0.66 ), 0.7 * step( 0.42 + 0.36 * facR2, facY ) )
		: facR < 0.35 ? facC.rgb * 0.5
		: facR < 0.39 ? facC.rgb * vec3( 1.65, 1.42, 1.08 )
		: facC.rgb * ( 0.82 + 0.36 * facR2 );
	vec3 facCol = mix( facC.rgb * vFacTint, mix( facC.rgb, facV, facNear ), facGlass );
	// 땅 가까이 살짝 어둡게(바닥 ~1.5 m) · 지붕 끝 두겁(~15 cm)
	facCol *= mix( 0.8, 1.0, smoothstep( 0.0, 0.45, vFacUv.y ) );
	float facTop = vFacInfo.w - vFacUv.y, facAa = fwidth( vFacUv.y );
	facCol *= 1.0 + 0.08 * ( 1.0 - smoothstep( 0.045 - facAa, 0.045 + facAa, facTop ) );
	diffuseColor.rgb *= facCol;
`;
export function facadeMaterial(view) {
  const tex = facadeTexture(view);
  const m = new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.86, metalness: 0, flatShading: true });
  m.onBeforeCompile = (sh) => {
    sh.uniforms.facadeMap = { value: tex };
    sh.vertexShader = 'attribute vec2 facUv;\nattribute vec4 facInfo;\nattribute vec4 facTint;\nvarying vec2 vFacUv;\nvarying vec4 vFacInfo;\nvarying vec3 vFacTint;\n'
      + sh.vertexShader.replace('#include <begin_vertex>', '#include <begin_vertex>\n\tvFacUv = facUv;\n\tvFacInfo = facInfo;\n\tvFacTint = facTint.rgb * 2.0;');
    // 유리만 매끈하게 — 하늘 · 해가 비친다(멀면 무늬가 섞여 반쯤)
    sh.fragmentShader = FRAG_HEAD + sh.fragmentShader
      .replace('#include <map_fragment>', FRAG_MAP)
      .replace('#include <roughnessmap_fragment>', 'float roughnessFactor = mix( roughness, 0.16, facGlass );');
  };
  m.customProgramCacheKey = () => 'dh3d-facade';
  m.userData.tex = tex;
  return m;
}

// 지붕 얼룩(16 m 반복 · 밝기 0.84~1) — 평지붕 콘크리트 · 방수층 결
function roofNoise(view) {
  const N = 256, cv = document.createElement('canvas');
  cv.width = cv.height = N;
  const g = cv.getContext('2d'), img = g.createImageData(N, N), rnd = rng(0x700f);
  const val = new Float32Array(N * N);
  for (const [cells, amp] of [[4, 0.5], [8, 0.3], [32, 0.14], [128, 0.06]]) {
    const grid = Array.from({ length: cells * cells }, () => rnd());
    const at = (i, j) => grid[((j + cells) % cells) * cells + ((i + cells) % cells)];
    for (let y = 0; y < N; y++)
      for (let x = 0; x < N; x++) {
        const fx = x / N * cells, fy = y / N * cells, i = Math.floor(fx), j = Math.floor(fy);
        let tx = fx - i, ty = fy - j; tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
        val[y * N + x] += amp * ((at(i, j) * (1 - tx) + at(i + 1, j) * tx) * (1 - ty) + (at(i, j + 1) * (1 - tx) + at(i + 1, j + 1) * tx) * ty);
      }
  }
  for (let k = 0; k < N * N; k++) {
    const v = Math.round(255 * (0.84 + 0.16 * Math.min(1, Math.max(0, (val[k] - 0.25) / 0.6))));
    img.data[k * 4] = img.data[k * 4 + 1] = img.data[k * 4 + 2] = v; img.data[k * 4 + 3] = 255;
  }
  g.putImageData(img, 0, 0);
  const t = new THREE.CanvasTexture(cv);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.anisotropy = Math.min(8, view.renderer.capabilities.getMaxAnisotropy());
  return t;
}

// 지붕 재료 — 점 색(건물마다) × 얼룩(uv = 도면 x, y ÷ 16 m)
export function roofMaterial(view) {
  return new THREE.MeshStandardMaterial({ vertexColors: true, map: roofNoise(view), roughness: 0.92, metalness: 0, flatShading: true });
}

// 건물 하나의 모습 — 도면 좌표 가운데(cx, cy) · 층수 · 바닥 넓이로 결정적으로 고른다
//   높은 건물(10층 이상)은 220 m 칸마다 같은 형식(한 단지 = 한 모습) · 낮은 건물은 건물마다
//   변형 차례: 0 흰 아파트 · 1 크림 아파트 · 2 석재 · 3 벽돌 빌라 · 4 유리 업무 · 5 회색 띠창 · 6 흰 미장 · 7 짙은 패널 · 8 공장
const PICK = [
  [15, [55, 37, 0, 0, 4, 4, 0, 0, 0]],
  [10, [34, 24, 10, 0, 14, 10, 0, 8, 0]],
  [6, [12, 10, 22, 6, 18, 16, 4, 12, 0]],
  [3, [0, 4, 18, 34, 4, 14, 16, 10, 0]],
  [1, [0, 0, 12, 24, 0, 22, 32, 6, 4]],
];
const PICK_WIDE = [0, 0, 8, 0, 0, 28, 22, 4, 38];     // 3층 이하 · 바닥 1,500 ㎡ 이상 = 공장 · 창고 · 학교
function weighted(w, r) {
  let s = 0;
  for (const x of w) s += x;
  let t = r * s;
  for (let i = 0; i < w.length; i++) { t -= w[i]; if (t < 0) return i; }
  return w.length - 1;
}
export function bldgStyle(cx, cy, floors, area) {
  const own = rng(hash2(Math.round(cx * 10), Math.round(cy * 10)));
  const zone = rng(hash2(Math.floor(cx / 220), Math.floor(cy / 220)) ^ 0xa5a5);
  const tall = floors >= 10;
  const w = floors <= 3 && area >= 1500 ? PICK_WIDE : PICK.find(([f]) => floors >= f)[1];
  const variant = weighted(w, tall ? zone() : own());
  const v = V[variant];
  // 벽 색 보정 — 밝기 ±6 % · 따뜻함 ±1.5 %(한 단지는 밝기 ±3 %만)
  const b = 1 + (own() - 0.5) * (tall ? 0.06 : 0.12), warm = (own() - 0.5) * 0.03;
  const roof = new THREE.Color(v.roof[Math.floor(own() * v.roof.length)]).multiplyScalar(0.94 + own() * 0.12);
  // 벽돌 빌라 1층 필로티(주차)는 위에 층이 있을 때만 — 1 · 2층 벽돌집은 1층도 벽돌 + 창
  return {
    variant, typ: variant * 2, gnd: variant === 3 && floors < 3 ? variant * 2 : variant * 2 + 1,
    end: variant <= 1 && floors >= 8 ? endLayer(variant) : -1,
    mod: v.mod * (0.92 + own() * 0.16),
    tint: [b * (1 + warm), b, b * (1 - warm)],
    roof, seed: own(), rnd: own,
  };
}
