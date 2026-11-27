/* 드론 로드뷰 — 첫 화면 (홈). 프로젝트 생성 = 편집 화면(edit.js). 파이썬 쪽 = window.pywebview.api */
(function () {
  'use strict';
  const $ = (id) => document.getElementById(id);
  let api = null;

  function toast(t) { const e = $('toast'); e.textContent = t; e.classList.add('show'); clearTimeout(e._t); e._t = setTimeout(() => e.classList.remove('show'), 2600); }
  // 파이썬 쪽에서 오류가 나면 조용히 멈추지 않고 알림으로 (1123: 파일 창이 «아무 반응 없음»이었음)
  async function call(name, ...args) {
    try { return await api[name](...args); } catch (e) { toast('오류 · ' + String((e && e.message) || e).slice(0, 120)); return null; }
  }
  function ready(fn) {
    if (window.pywebview && window.pywebview.api) { fn(); return; }
    window.addEventListener('pywebviewready', fn, { once: true });
  }
  const fmtDate = (s) => (s || '').replace(/^\d{4}-/, '').replace('-', '.');
  window.RV = { call, toast, $, page: showPage };

  // ------------------------------------------------------------ 메뉴
  function showPage(p) {
    document.querySelectorAll('.nav').forEach((a) => a.classList.toggle('on', a.dataset.page === p));
    $('pageHome').classList.toggle('hide', p !== 'home');
    $('pageCluster').classList.toggle('hide', p !== 'cluster');
    if (p === 'home') { loadList(); loadTutorials(); } else if (window.RVCluster) window.RVCluster.show();
  }

  // ------------------------------------------------------------ 최근 프로젝트
  // 종류 마커 (썸네일 왼쪽 위 알약) — 3D 매핑 = 하늘색 · 360 로드뷰 = 주황
  function kindPill(kd) {
    return kd === 'mapping'
      ? '<span class="mk"><svg viewBox="0 0 24 24"><path d="M12 2 3 7v10l9 5 9-5V7zm0 2.3 6.6 3.7L12 11.7 5.4 8zM5 9.7l6 3.4v6.6l-6-3.4zm8 10V13.1l6-3.4v6.6z"/></svg>3D 매핑</span>'
      : '<span class="mk"><svg viewBox="0 0 24 24"><path d="M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zm0 2c1.7 0 3.4 2.6 3.9 6H8.1C8.6 7.6 10.3 5 12 5zM5.1 11a7 7 0 0 1 3-5 11 11 0 0 0-1 5zm0 2h2a11 11 0 0 0 1 5 7 7 0 0 1-3-5zm3 0h7.8c-.5 3.4-2.2 6-3.9 6s-3.4-2.6-3.9-6zm9.8 0h2a7 7 0 0 1-3 5 11 11 0 0 0 1-5zm0-2a11 11 0 0 0-1-5 7 7 0 0 1 3 5z"/></svg>360</span>';
  }

  // 튜토리얼 (프로그램에 담긴 매핑 결과 · 3D · 360 둘 다) — 함수가 없거나 목록이 비면 줄 숨김
  async function loadTutorials() {
    const box = $('tutBox');
    let L = [];
    if (api && typeof api.tutorials === 'function') { try { L = (await api.tutorials()) || []; } catch (e) { L = []; } }
    box.classList.toggle('hide', !L.length);
    const g = $('tuts'); g.textContent = '';
    for (const x of L) {
      const kd = x.kind === 'mapping' ? 'mapping' : '360';
      const d = document.createElement('div');
      d.className = 'card tut k-' + kd;
      d.tabIndex = 0;
      d.innerHTML = '<div class="thumb"></div>' + kindPill(kd) + '<span class="tb">튜토리얼</span><div class="t"></div><div class="m"></div>';
      if (x.thumb_url) d.querySelector('.thumb').style.backgroundImage = 'url("' + String(x.thumb_url).replace(/"/g, '%22') + '")';
      d.querySelector('.t').textContent = x.title || '';
      d.querySelector('.m').textContent = x.desc || '';
      d.title = x.desc || x.title || '';
      let busy = false;
      const go = async () => {
        if (busy) return; busy = true; d.classList.add('busy'); d.querySelector('.m').textContent = '여는 중…';
        const r = await call('open_tutorial', x.key);
        busy = false; d.classList.remove('busy'); d.querySelector('.m').textContent = x.desc || '';
        if (r && !r.ok) toast(r.msg || '열지 못함');
      };
      d.addEventListener('click', go);
      d.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); go(); } });
      g.appendChild(d);
    }
  }
  async function loadList() {
    const L = (await call('list_projects')) || [];
    const box = $('grid');
    box.textContent = '';
    $('cnt').textContent = '(' + L.length + ')';
    $('empty').classList.toggle('hide', L.length > 0);
    for (const x of L) {
      const d = document.createElement('div');
      const kd = x.kind === 'mapping' ? 'mapping' : '360';           // kind 가 없으면 360 (옛 기록)
      d.className = 'card k-' + kd + (x.exists ? '' : ' gone');
      d.innerHTML = '<div class="thumb"></div>' + kindPill(kd) + '<div class="t"></div><div class="m"></div><button class="rm" title="목록에서 빼기">×</button>';
      const th = d.querySelector('.thumb');
      const bg = (u) => 'url("' + String(u).replace(/"/g, '%22') + '")';
      if (x.thumb_url) th.style.backgroundImage = bg(x.thumb_url);
      // 컷: 마우스를 올리면 그 프로젝트의 여러 장면이 넘어감
      const cuts = (x.cut_urls || []).filter(Boolean);
      if (cuts.length > 1) {
        let k = 0, iv = 0;
        d.addEventListener('mouseenter', () => { iv = setInterval(() => { k = (k + 1) % cuts.length; th.style.backgroundImage = bg(cuts[k]); }, 650); });
        d.addEventListener('mouseleave', () => { clearInterval(iv); k = 0; if (x.thumb_url) th.style.backgroundImage = bg(x.thumb_url); });
        cuts.forEach((u) => { const im = new Image(); im.src = u; });
      }
      d.querySelector('.t').textContent = x.title || '(이름 없음)';
      d.querySelector('.m').textContent = (x.exists ? '' : '폴더 없음 · ') + (x.size_gb != null ? x.size_gb + ' GB · ' : '') + (x.shots ? x.shots + '곳 · ' : '') + fmtDate(x.updated);
      d.title = x.path;
      d.addEventListener('click', async (e) => {
        if (e.target.classList.contains('rm')) return;
        if (!x.exists) { toast('폴더 없음'); return; }
        const r = await call('open_project', x.path);
        if (r && !r.ok) toast(r.msg);
      });
      d.querySelector('.rm').addEventListener('click', async (e) => { e.stopPropagation(); await call('remove_project', x.path); loadList(); });
      box.appendChild(d);
    }
  }

  // ------------------------------------------------------------ 큰 카드 그림 (직접 그린 canvas · 사진 · 영상 · 로고 없음)
  // 왼쪽 = 하늘에 뜬 360 드론 (둥실) · 오른쪽 = 어두운 공간의 레이저 격자 속을 나는 매핑 드론.
  // 가볍게: 30fps 이하 · 창이 숨거나 다른 화면이 덮으면 멈춤 · 움직임 줄이기 설정이면 멈춘 그림 한 장.
  const TAU = Math.PI * 2;
  // 윈도우 «애니메이션 효과» 가 꺼진 PC 도 많아(이 PC 도 그렇게 잡힘) 설정을 따르면 그림이 늘 멈춰 보임 → 따르지 않고 가볍게(30fps 이하) 움직임
  const reduce = false;

  function rr(c, x, y, w, h, r) {
    c.beginPath(); c.moveTo(x + r, y); c.arcTo(x + w, y, x + w, y + h, r); c.arcTo(x + w, y + h, x, y + h, r);
    c.arcTo(x, y + h, x, y, r); c.arcTo(x, y, x + w, y, r); c.closePath();
  }
  function ell(c, x, y, rx, ry, rot) { c.beginPath(); c.ellipse(x, y, Math.max(rx, 0.01), Math.max(ry, 0.01), rot || 0, 0, TAU); }

  // 구름 한 장 (미리 그려 두고 크기만 바꿔 씀)
  let cloudImg = null;
  function cloudSprite() {
    if (cloudImg) return cloudImg;
    const k = document.createElement('canvas'); k.width = 300; k.height = 130;
    const c = k.getContext('2d');
    const bl = [[60, 80, 46], [110, 62, 58], [170, 66, 56], [225, 82, 44], [140, 92, 50], [90, 96, 38], [200, 98, 36]];
    for (const [x, y, r] of bl) {
      const g = c.createRadialGradient(x, y, 0, x, y, r);
      g.addColorStop(0, 'rgba(255,255,255,.75)'); g.addColorStop(1, 'rgba(255,255,255,0)');
      c.fillStyle = g; c.fillRect(x - r, y - r, r * 2, r * 2);
    }
    return (cloudImg = k);
  }
  const CLOUDS = [[.05, .62, 1.5, .010, .9], [.35, .78, 1.9, .007, .8], [.62, .58, 1.3, .013, .7], [.85, .86, 2.1, .008, .85], [.2, .93, 2.4, .006, .9], [.5, .40, 0.9, .016, .45], [.9, .35, 0.8, .012, .4]];

  // ---- 360 드론 (정면) : 프로펠러 가드 · 가운데 검은 몸통 · 위아래 둥근 360 렌즈
  function drone360(c, cx, cy, s, t) {
    c.save();
    c.translate(cx + Math.sin(t * 0.6) * 0.12 * s, cy + Math.sin(t * 1.4) * 0.16 * s);
    c.rotate(Math.sin(t * 0.9) * 0.03);
    // 가드 (둥근 고리) 와 돌아가는 날개 흐림
    for (const sd of [-1, 1]) {
      const gx = sd * 2.35 * s, gy = -0.12 * s;
      ell(c, gx, gy, 1.4 * s, 0.34 * s); c.fillStyle = 'rgba(255,255,255,' + (0.10 + 0.05 * Math.sin(t * 30 + sd)) + ')'; c.fill();
      ell(c, gx, gy, 1.4 * s, 0.34 * s); c.lineWidth = 0.15 * s; c.strokeStyle = '#d3d9df'; c.stroke();
      ell(c, gx, gy + 0.04 * s, 1.4 * s, 0.34 * s); c.lineWidth = 0.04 * s; c.strokeStyle = 'rgba(70,80,92,.55)'; c.stroke();
      c.save(); c.translate(gx, gy); c.scale(1, 0.24); c.rotate(t * 18 * sd);
      c.strokeStyle = 'rgba(40,46,54,.3)'; c.lineWidth = 0.2 * s; c.beginPath(); c.moveTo(-1.2 * s, 0); c.lineTo(1.2 * s, 0); c.stroke(); c.restore();
      // 팔
      rr(c, (sd > 0 ? 0.9 : -1.45) * s, -0.3 * s, 0.55 * s, 0.4 * s, 0.12 * s); c.fillStyle = '#8f979f'; c.fill();
    }
    // 가로 몸통 (은회색)
    let g = c.createLinearGradient(0, -0.5 * s, 0, 0.7 * s); g.addColorStop(0, '#b9c0c7'); g.addColorStop(1, '#6f7780');
    rr(c, -1.7 * s, -0.42 * s, 3.4 * s, 0.95 * s, 0.34 * s); c.fillStyle = g; c.fill();
    // 앞 센서 (좌우)
    for (const sd of [-1, 1]) {
      ell(c, sd * 1.22 * s, 0.1 * s, 0.2 * s, 0.2 * s); c.fillStyle = '#1b2026'; c.fill();
      ell(c, sd * 1.22 * s, 0.1 * s, 0.09 * s, 0.09 * s); c.fillStyle = '#5b86c7'; c.fill();
      ell(c, sd * 1.7 * s, -0.06 * s, 0.07 * s, 0.07 * s);
      c.fillStyle = sd < 0 ? 'rgba(255,90,90,' + (0.5 + 0.5 * Math.sin(t * 5)) + ')' : 'rgba(90,255,150,' + (0.5 + 0.5 * Math.sin(t * 5 + 1)) + ')'; c.fill();
    }
    // 가운데 검은 몸통 + 위아래 렌즈 돔
    g = c.createLinearGradient(-0.8 * s, 0, 0.8 * s, 0); g.addColorStop(0, '#15181c'); g.addColorStop(.5, '#2c3138'); g.addColorStop(1, '#15181c');
    rr(c, -0.78 * s, -0.7 * s, 1.56 * s, 1.75 * s, 0.26 * s); c.fillStyle = g; c.fill();
    for (let i = 0; i < 5; i++) { c.fillStyle = 'rgba(255,255,255,.05)'; c.fillRect(-0.62 * s, (-0.3 + i * 0.28) * s, 1.24 * s, 0.05 * s); }
    for (const [dy, up] of [[-0.78, 1], [1.13, -1]]) {
      g = c.createRadialGradient(-0.15 * s, (dy - up * 0.1) * s, 0, 0, dy * s, 0.6 * s);
      g.addColorStop(0, '#9cc0e6'); g.addColorStop(.35, '#2c4a6e'); g.addColorStop(1, '#0c1219');
      ell(c, 0, dy * s, 0.6 * s, 0.4 * s); c.fillStyle = g; c.fill();
      ell(c, -0.2 * s, (dy - up * 0.1) * s, 0.16 * s, 0.07 * s); c.fillStyle = 'rgba(255,255,255,.45)'; c.fill();
    }
    // 가운데 둥근 360 렌즈 (정면)
    g = c.createRadialGradient(-0.12 * s, 0.1 * s, 0, 0, 0.2 * s, 0.5 * s);
    g.addColorStop(0, '#6d96c9'); g.addColorStop(.4, '#1d2f49'); g.addColorStop(1, '#07090d');
    ell(c, 0, 0.2 * s, 0.46 * s, 0.46 * s); c.fillStyle = g; c.fill();
    c.lineWidth = 0.05 * s; c.strokeStyle = '#4a525c'; c.stroke();
    ell(c, -0.14 * s, 0.05 * s, 0.1 * s, 0.06 * s, -0.5); c.fillStyle = 'rgba(255,255,255,.5)'; c.fill();
    c.restore();
  }

  function frame360(c, w, h, t) {
    const sky = c.createLinearGradient(0, 0, 0, h);
    sky.addColorStop(0, '#3c6c9e'); sky.addColorStop(.55, '#7ea5c8'); sky.addColorStop(1, '#c4d8e8');
    c.fillStyle = sky; c.fillRect(0, 0, w, h);
    const sp = cloudSprite();
    for (const [x, y, k, v, a] of CLOUDS) {
      const cw = sp.width * k * (h / 480), ch = sp.height * k * (h / 480);
      const px = (((x + t * v) % 1.3) + 1.3) % 1.3 - 0.15;
      c.globalAlpha = a; c.drawImage(sp, px * w - cw / 2, y * h - ch / 2, cw, ch);
    }
    c.globalAlpha = 1;
    drone360(c, w * 0.5, h * 0.60, Math.min(w / 8.6, h / 6.4), t);
  }

  // ---- 3D 매핑 : 레이저 격자 방 + 매핑 드론
  function droneMap(c, cx, cy, s, t) {
    c.save(); c.translate(cx, cy); c.rotate(Math.sin(t * 0.8) * 0.05);
    // 스캔 빛 (아래로 퍼짐)
    let g = c.createLinearGradient(0, 0, 0, s * 5);
    g.addColorStop(0, 'rgba(34,211,238,.30)'); g.addColorStop(1, 'rgba(34,211,238,0)');
    c.beginPath(); c.moveTo(-0.1 * s, 0.7 * s); c.lineTo(-2.2 * s, 5 * s); c.lineTo(2.2 * s, 5 * s); c.lineTo(0.1 * s, 0.7 * s); c.closePath(); c.fillStyle = g; c.fill();
    // 팔 + 날개 (뒤 둘 작고 위 · 앞 둘 크고 아래)
    const R = [[-1.9, -0.55, .8], [1.9, -0.55, .8], [-2.1, 0.35, 1], [2.1, 0.35, 1]];
    c.strokeStyle = '#3a4650'; c.lineWidth = 0.18 * s; c.lineCap = 'round';
    for (const [x, y] of R) { c.beginPath(); c.moveTo(0, 0); c.lineTo(x * s * 0.85, (y - 0.1) * s); c.stroke(); }
    for (const [x, y, k] of R) {
      ell(c, x * s, (y - 0.12) * s, 0.95 * s * k, 0.2 * s * k); c.fillStyle = 'rgba(190,235,240,' + (0.14 + 0.06 * Math.sin(t * 40 + x)) + ')'; c.fill();
      c.save(); c.translate(x * s, (y - 0.12) * s); c.scale(1, 0.22); c.rotate(t * 25 * (x > 0 ? 1 : -1));
      c.strokeStyle = 'rgba(200,225,230,.5)'; c.lineWidth = 0.18 * s; c.beginPath(); c.moveTo(-0.9 * s * k, 0); c.lineTo(0.9 * s * k, 0); c.stroke(); c.restore();
      ell(c, x * s, (y - 0.04) * s, 0.16 * s, 0.12 * s); c.fillStyle = '#222b33'; c.fill();
    }
    // 몸통
    g = c.createLinearGradient(0, -0.6 * s, 0, 0.7 * s); g.addColorStop(0, '#59656f'); g.addColorStop(1, '#1d252c');
    rr(c, -1.1 * s, -0.55 * s, 2.2 * s, 1.0 * s, 0.4 * s); c.fillStyle = g; c.fill();
    c.fillStyle = 'rgba(255,255,255,.12)'; rr(c, -0.85 * s, -0.45 * s, 1.7 * s, 0.14 * s, 0.07 * s); c.fill();
    ell(c, -1.0 * s, 0.05 * s, 0.07 * s, 0.07 * s); c.fillStyle = 'rgba(255,80,80,' + (0.5 + 0.5 * Math.sin(t * 6)) + ')'; c.fill();
    ell(c, 1.0 * s, 0.05 * s, 0.07 * s, 0.07 * s); c.fillStyle = 'rgba(80,255,160,' + (0.5 + 0.5 * Math.sin(t * 6 + 2)) + ')'; c.fill();
    // 아래 짐벌 카메라 (밝은 하늘색 렌즈)
    rr(c, -0.5 * s, 0.35 * s, 1.0 * s, 0.4 * s, 0.18 * s); c.fillStyle = '#262f37'; c.fill();
    ell(c, 0, 0.78 * s, 0.34 * s, 0.34 * s); c.fillStyle = '#10161b'; c.fill();
    ell(c, 0, 0.78 * s, 0.17 * s, 0.17 * s); c.fillStyle = '#22d3ee'; c.shadowColor = '#22d3ee'; c.shadowBlur = 12; c.fill(); c.shadowBlur = 0;
    c.restore();
  }

  function frameMap(c, w, h, t) {
    const bg = c.createLinearGradient(0, 0, 0, h); bg.addColorStop(0, '#05090b'); bg.addColorStop(1, '#0d1c20');
    c.fillStyle = bg; c.fillRect(0, 0, w, h);
    const f = h * 0.95, cx = w / 2, cy = h * 0.46, FY = 1.25, ZN = 0.9, ZF = 15, XW = 4;
    const P = (x, y, z) => [cx + x / z * f, cy + y / z * f];
    const off = (t * 0.45) % 1;                              // 격자가 천천히 다가옴
    // 드론 자리 (월드)
    const dX = Math.sin(t * 0.5) * 1.4, dY = -0.25 + Math.sin(t * 0.9) * 0.18, dZ = 5.6;
    const hot = (x, z) => Math.max(0, 1 - Math.hypot(x - dX, (z - dZ - 1.4) * 0.8) / 2.6);
    const col = (a) => 'rgba(47,129,247,' + a + ')';
    c.lineWidth = 1;
    // 바닥 선 (앞뒤로 · 가로로)
    for (let x = -XW; x <= XW; x++) {
      const a = P(x, FY, ZN), b = P(x, FY, ZF);
      const g = c.createLinearGradient(0, a[1], 0, b[1]); g.addColorStop(0, col(0.55)); g.addColorStop(1, col(0));
      c.strokeStyle = g; c.beginPath(); c.moveTo(a[0], a[1]); c.lineTo(b[0], b[1]); c.stroke();
    }
    for (let k = 0; k < ZF; k++) {
      const z = k + 1 - off; if (z < ZN) continue;
      const al = Math.max(0, 1 - z / ZF);
      const a = P(-XW, FY, z), b = P(XW, FY, z);
      c.strokeStyle = col(0.5 * al); c.beginPath(); c.moveTo(a[0], a[1]); c.lineTo(b[0], b[1]); c.stroke();
      for (let x = -XW; x <= XW; x++) {            // 교차점 = 빛나는 점 (드론 아래가 더 밝음)
        const p = P(x, FY, z), b2 = hot(x, z), r = (1.1 + b2 * 2.2) * (1.3 - z / ZF * 0.9);
        c.fillStyle = b2 > 0.05 ? 'rgba(170,235,255,' + Math.min(1, al * 0.9 + b2) + ')' : 'rgba(215,235,240,' + (al * 0.85) + ')';
        c.fillRect(p[0] - r, p[1] - r, r * 2, r * 2);
      }
    }
    // 양 옆 벽 (세로 · 가로 격자)
    for (const sd of [-1, 1]) {
      const X = sd * XW;
      for (let k = 0; k < ZF; k++) {
        const z = k + 1 - off; if (z < ZN) continue;
        const al = Math.max(0, 1 - z / ZF);
        const a = P(X, FY, z), b = P(X, -2.4, z);
        c.strokeStyle = col(0.4 * al); c.beginPath(); c.moveTo(a[0], a[1]); c.lineTo(b[0], b[1]); c.stroke();
        for (let y = FY; y >= -2.4; y -= 0.95) {
          const p = P(X, y, z), r = 1.0 * (1.3 - z / ZF * 0.9);
          c.fillStyle = 'rgba(215,235,240,' + (al * 0.8) + ')'; c.fillRect(p[0] - r, p[1] - r, r * 2, r * 2);
        }
      }
      for (let y = FY; y >= -2.4; y -= 0.95) {
        const a = P(X, y, ZN), b = P(X, y, ZF);
        const g = c.createLinearGradient(a[0], a[1], b[0], b[1]); g.addColorStop(0, col(0.4)); g.addColorStop(1, col(0));
        c.strokeStyle = g; c.beginPath(); c.moveTo(a[0], a[1]); c.lineTo(b[0], b[1]); c.stroke();
      }
    }
    // 드론 아래 바닥 스캔 고리 (퍼지며 사라짐)
    const fp = P(dX, FY, dZ + 1.4);
    for (let i = 0; i < 2; i++) {
      const ph = ((t * 0.5 + i * 0.5) % 1), rx = (0.4 + ph * 2.3) / (dZ + 1.4) * f;
      ell(c, fp[0], fp[1], rx, rx * 0.22); c.strokeStyle = 'rgba(120,225,250,' + (0.7 * (1 - ph)) + ')'; c.lineWidth = 1.5; c.stroke();
    }
    c.lineWidth = 1;
    const dp = P(dX, dY, dZ);
    droneMap(c, dp[0], dp[1], f / dZ * 0.42, t);
  }

  const HERO = [];
  // 큰 카드 = 실제 드론 영상(있으면) 바탕 + 3D 드론 (three.js) · WebGL 이 안 되면 위에서 그린 canvas 그림
  const MEDIA = '../../tutorial/_media/';
  // 카드 사진 (JACK 1127 · tutorial/_media/card_*.jpg · 설치 파일에만 · 저장소엔 안 올림): 평소 = 멈춘 사진 · 마우스 올리면 움직임 (home.css .phon)
  // 사진이 없으면 (개발 · 사진 빠진 설치) 예전 그대로 = 드론 영상 + 3D 드론 (three.js) · WebGL 도 안 되면 canvas 그림
  function heroSetup(cid, draw, g3id, kind, vid, media, photo) {
    const cv = $(cid); if (!cv) return;
    const o = { cv, c: cv.getContext('2d'), draw, w: 0, h: 0, dpr: 1, g3: null, v: null, ph: false };
    const art = cv.parentNode;
    const g3init = () => {
      const g3 = window.Drone3D && $(g3id) ? window.Drone3D.create($(g3id), kind) : null;
      if (!g3) return;
      o.g3 = g3; art.classList.add('g3on');
      const v = $(vid);
      if (v) {
        o.v = v;
        v.poster = MEDIA + media + '.jpg';
        v.addEventListener('loadeddata', () => v.classList.add('on'));
        v.addEventListener('error', () => { v.classList.remove('on'); v.removeAttribute('src'); o.v = null; });
        v.src = MEDIA + media + '.mp4';
      }
    };
    if (photo) {
      const im = new Image();
      im.className = 'photo'; im.alt = ''; im.draggable = false; im.decoding = 'async';
      im.addEventListener('load', () => { o.ph = true; art.classList.add('phon'); });
      im.addEventListener('error', () => { im.remove(); g3init(); fit(); });
      im.src = MEDIA + photo;
      art.insertBefore(im, art.firstChild);
    } else g3init();
    const fit = () => {
      const r = cv.parentNode.getBoundingClientRect();
      if (r.width < 2 || r.height < 2) return;
      o.dpr = Math.min(window.devicePixelRatio || 1, 1.25);
      o.w = r.width; o.h = r.height;
      if (o.g3) o.g3.resize(r.width, r.height, o.dpr);
      else { cv.width = Math.round(r.width * o.dpr); cv.height = Math.round(r.height * o.dpr); }
      if (reduce) paint(o, 3.2);
    };
    if (window.ResizeObserver) new ResizeObserver(fit).observe(cv.parentNode); else window.addEventListener('resize', fit);
    fit();
    HERO.push(o);
  }
  function paint(o, t) {
    if (!o.w || o.ph) return;                                           // 사진 카드 = 그리지 않음 (움직임은 CSS)
    if (o.g3) { o.g3.render(t); return; }
    o.c.setTransform(o.dpr, 0, 0, o.dpr, 0, 0);
    o.draw(o.c, o.w, o.h, t);
  }
  function heroVideos(on) {
    for (const o of HERO) {
      if (!o.v) continue;
      if (on && !reduce) { if (o.v.paused) { const p = o.v.play(); if (p && p.catch) p.catch(() => {}); } }
      else if (!o.v.paused) o.v.pause();
    }
  }
  let hRaf = 0, hTimer = 0, hLast = 0;
  function heroActive() {
    return !document.hidden && !$('pageHome').classList.contains('hide') && $('ed').classList.contains('hide') && $('mp').classList.contains('hide');
  }
  function heroTick(now) {
    hRaf = 0;
    if (!heroActive()) { heroVideos(false); hTimer = setTimeout(() => { hTimer = 0; heroKick(); }, 400); return; }
    heroVideos(true);
    if (now - hLast >= 33) { hLast = now; for (const o of HERO) paint(o, now / 1000); }
    hRaf = requestAnimationFrame(heroTick);
  }
  function heroKick() { if (!hRaf && !hTimer) hRaf = requestAnimationFrame(heroTick); }
  function heroStart() {
    // 카드 = 움직이는 3D 드론 + 우리 영상 바탕 (JACK 1127 «실제 드론 사진 쓴 거 그냥 애니메이션으로» · 사진 인자를 안 넘김 = 사진 안 씀)
    heroSetup('hCvMap', frameMap, 'hG3Map', 'mapping', 'hVidMap', 'home_3d');
    heroSetup('hCv360', frame360, 'hG3360', '360', 'hVid360', 'home_360');
    if (reduce) return;
    document.addEventListener('visibilitychange', heroKick);
    heroKick();
  }
  ready(async () => {
    api = window.pywebview.api;
    const info = (await call('app_info')) || {};
    $('ver').textContent = 'v' + (info.version || '');
    document.querySelectorAll('.nav').forEach((a) => a.addEventListener('click', () => showPage(a.dataset.page)));
    showPage('home');
    $('bOpen').addEventListener('click', async () => { const r = await call('pick_open'); if (r && !r.ok) toast(r.msg); });
    $('bCreate').addEventListener('click', () => window.RVEdit && window.RVEdit.open());
    $('jobPill').addEventListener('click', () => {
      if (window.RVMap && window.RVMap._S.job) window.RVMap.open(); else if (window.RVEdit) window.RVEdit.open();
    });
    $('bMapping').addEventListener('click', () => window.RVMap && window.RVMap.open());
    if (window.RVEdit) window.RVEdit.init(info);
    if (window.RVMap) window.RVMap.init(info);
    if (window.RVCluster) window.RVCluster.init();
  });
  heroStart();           // 그림은 파이썬 연결과 무관 (먼저 움직이기 시작)
})();
