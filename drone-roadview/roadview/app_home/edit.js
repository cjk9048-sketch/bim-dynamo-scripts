/* 드론 로드뷰 — 프로젝트 생성 = 편집 화면 (JACK 1125: DJI Studio 처럼)
 *   가져오기 → 영상을 아래 타임라인으로 끌어 놓기 → 미리보기(LRF)로 프레임을 찾아 자르기(S) · 지우기(Delete) · 앞/뒤 지우기(Q/W)
 *   → [만들기] = 남은 토막만 360 사진으로 (build --keep 영상이름=시작초:끝초)
 * 시간: 토막의 a, b = 원본 영상 초 (프레임 경계에 맞춤) · T = 타임라인 초 (토막을 차례로 이은 것)
 */
(function () {
  'use strict';
  const $ = (id) => document.getElementById(id);
  const call = (...a) => window.RV.call(...a);
  const toast = (t) => window.RV.toast(t);
  const COLORS = ['#ffb020', '#22d3ee', '#a3e635', '#f472b6', '#c084fc', '#fb7185', '#60a5fa', '#fbbf24'];
  const TH_W = 44;                 // 타임라인 썸네일 한 칸 (px)
  const SEC_PER_SHOT = 1.7;        // 만들기 시간 어림 (0012: 867곳 1457초)
  const MAX_PPS = 150;             // 가장 크게 = 1초 150 px (한 프레임 5 px)

  const S = {
    info: {}, media: [], jpgs: [], clips: [], sel: -1, T: 0, pps: 10, zoomV: 0,
    undo: [], redo: [], job: null, timer: 0, outTouched: false, pendingEdit: null, restored: false,
    playing: false, curM: -1, seekBusy: false, seekNext: null, resume: false, lastRaf: 0,
  };

  // ------------------------------------------------------------ 시간 · 토막
  const len = (c) => c.b - c.a;
  const total = () => S.clips.reduce((s, c) => s + len(c), 0);
  function starts() { const o = []; let s = 0; for (const c of S.clips) { o.push(s); s += len(c); } return o; }
  const fpsOf = (m) => (S.media[m] && S.media[m].fps) || 29.97;
  const snap = (m, t) => Math.round(t * fpsOf(m)) / fpsOf(m);
  function locate(T) {            // T → {k, m, t(원본 초), start}
    if (!S.clips.length) return null;
    const st = starts();
    const tot = total();
    if (T >= tot - 1e-6) { const k = S.clips.length - 1; const c = S.clips[k]; return { k, m: c.m, t: Math.max(c.a, c.b - 0.5 / fpsOf(c.m)), start: st[k] }; }
    for (let k = S.clips.length - 1; k >= 0; k--) {
      if (T >= st[k] - 1e-6) { const c = S.clips[k]; return { k, m: c.m, t: c.a + Math.max(0, T - st[k]), start: st[k] }; }
    }
    return { k: 0, m: S.clips[0].m, t: S.clips[0].a, start: 0 };
  }
  function tc(T, fps) {
    const s = Math.floor(T + 1e-6), f = Math.min(Math.round(fps) - 1, Math.floor((T - s) * fps + 1e-6));
    const p = (n) => String(n).padStart(2, '0');
    return p(Math.floor(s / 3600)) + ':' + p(Math.floor(s / 60) % 60) + ':' + p(s % 60) + ':' + p(Math.max(0, f));
  }
  const mmss = (t) => Math.floor(t / 60) + ':' + String(Math.floor(t % 60)).padStart(2, '0');

  function push() { S.undo.push(JSON.stringify(S.clips)); if (S.undo.length > 200) S.undo.shift(); S.redo = []; }
  function undo() { if (!S.undo.length) return; S.redo.push(JSON.stringify(S.clips)); S.clips = JSON.parse(S.undo.pop()); after(); }
  function redo() { if (!S.redo.length) return; S.undo.push(JSON.stringify(S.clips)); S.clips = JSON.parse(S.redo.pop()); after(); }
  function after(keepSel) { if (!keepSel) S.sel = -1; S.T = Math.min(S.T, total()); refresh(); showAt(S.T); }

  function addClip(m, idx) {
    const md = S.media[m];
    if (!md || !(md.dur > 0)) { toast('불러오는 중'); return; }
    push();
    if (idx == null || idx > S.clips.length) idx = S.clips.length;
    S.clips.splice(idx, 0, { m, a: 0, b: snap(m, md.dur) });
    S.T = starts()[idx];
    S.sel = -1;
    refresh(true);
    showAt(S.T);
  }
  function split() {
    const L = locate(S.T); if (!L) return;
    const c = S.clips[L.k], f = 1 / fpsOf(c.m), t = snap(c.m, L.t);
    if (t - c.a < f * 0.5 || c.b - t < f * 0.5) { toast('토막 끝 · 자를 곳 없음'); return; }
    push();
    S.clips.splice(L.k, 1, { m: c.m, a: c.a, b: t }, { m: c.m, a: t, b: c.b });
    after();
  }
  function delSel() {
    if (S.sel < 0 || S.sel >= S.clips.length) { toast('지울 토막을 누르세요'); return; }
    push();
    const st = starts()[S.sel];
    S.clips.splice(S.sel, 1);
    S.T = st;
    after();
  }
  function delSide(left) {
    const L = locate(S.T); if (!L) return;
    const c = S.clips[L.k], t = snap(c.m, L.t), f = 1 / fpsOf(c.m);
    if (left ? t - c.a < f * 0.5 : c.b - t < f * 0.5) return;
    push();
    if (left) { c.a = t; S.T = L.start; } else { c.b = t; S.T = L.start + (t - c.a); }
    if (len(c) < f * 0.5) S.clips.splice(L.k, 1);
    after(true);
  }

  // ------------------------------------------------------------ 가져오기
  async function pick() {
    const r = await call('pick_inputs');
    if (!r) return;
    let added = 0;
    for (const f of r.files) {
      if (/\.jpe?g$/i.test(f)) { if (!S.jpgs.includes(f)) S.jpgs.push(f); continue; }
      if (S.media.some((x) => x.path.toLowerCase() === f.toLowerCase())) continue;
      const md = { path: f, name: f.split(/[\\/]/).pop().replace(/\.[^.]+$/, ''), state: 'load', line: '읽는 중',
                   color: COLORS[S.media.length % COLORS.length], track: [], thumbs: 0 };
      S.media.push(md);
      added++;
      const q = await call('media_load', f);
      if (!q || !q.ok) { md.state = 'fail'; md.line = (q && q.msg) || '실패'; continue; }
      md.mid = q.id;
    }
    renderBin();
    pollMedia();
    if (added || S.jpgs.length) await setOut();
  }
  async function setOut() {
    if (S.outTouched) return;
    const paths = S.media.map((x) => x.path).concat(S.jpgs);
    const out = await call('default_out', paths);
    if (!out) return;
    $('out').value = out;
    if (!$('title').value) $('title').value = out.split(/[\\/]/).pop().replace(/^로드뷰_/, '');
    if (!S.restored && !S.clips.length) {
      const e = await call('load_edit', out);
      if (e && e.clips && e.clips.length) { S.pendingEdit = e; tryRestore(); }
    }
  }
  function tryRestore() {
    const e = S.pendingEdit;
    if (!e || S.restored || S.clips.length) return;
    const idx = (p) => S.media.findIndex((x) => x.path.toLowerCase() === String(p).toLowerCase());
    if (!e.clips.every((c) => { const i = idx(c.path); return i >= 0 && S.media[i].dur > 0; })) return;
    S.clips = e.clips.map((c) => ({ m: idx(c.path), a: +c.a, b: +c.b }));
    if (e.interval) $('gap').value = e.interval;
    if (e.gcp && Array.isArray(e.gcp.pts)) {
      G.pts = e.gcp.pts; G.pairs = (e.gcp.pairs || []).filter((q) => idx(q.path) >= 0);
      G.seq = G.pts.reduce((s, g) => Math.max(s, +String(g.id).slice(1) || 0), 0);
      if (S.media.every((m) => m.ready)) gSolveAll();
      gRender();
    }
    S.restored = true;
    S.pendingEdit = null;
    toast('지난번 편집 불러옴 · 토막 ' + S.clips.length);
    after();
  }
  let mediaTimer = 0;
  function pollMedia() {
    clearTimeout(mediaTimer);
    const busy = S.media.filter((x) => x.state === 'load' && x.mid);
    if (!busy.length) return;
    mediaTimer = setTimeout(async () => {
      let changed = false;
      for (const md of busy) {
        const r = await call('media_state', md.mid);
        if (!r) continue;
        if (r.media) {
          const had = md.dur > 0, wasReady = md.ready;
          Object.assign(md, r.media);
          if (!had && md.dur > 0) changed = true;
          if (md.ready && !wasReady) changed = true;
        }
        md.line = r.line;
        if (r.state !== 'run') { md.state = r.state === 'done' ? 'done' : 'fail'; changed = true; if (r.state === 'fail') toast(md.name + ' · ' + r.line); }
      }
      renderBin();
      if (changed) {
        tryRestore(); refresh(true); if (S.curM < 0) showAt(S.T);
        for (const md of busy) if (md.ready) await ensureZt0(md);
        if (G.pairs.length) { gSolveAll(); gRender(); mapDraw(); }
        FV.dirty = true;
      }
      pollMedia();
    }, 400);
  }

  function shortName(n) {             // DJI_20261120110457_0012_D → 0012 · 11.20 11:04
    const m = /^DJI_\d{4}(\d{2})(\d{2})(\d{2})(\d{2})\d{2}_(\d{4})/.exec(n || '');
    return m ? m[5] + ' · ' + m[1] + '.' + m[2] + ' ' + m[3] + ':' + m[4] : n;
  }
  function thumbUrl(md, t) {
    if (!md.thumbs || !md.thumb_base) return '';
    const i = Math.max(1, Math.min(md.thumbs, Math.round(t / (md.thumb_step || 2)) + 1));
    return md.thumb_base + String(i).padStart(4, '0') + '.jpg';
  }
  function renderBin() {
    const box = $('bin');
    box.textContent = '';
    $('binEmpty').classList.toggle('hide', S.media.length + S.jpgs.length > 0);
    S.media.forEach((md, i) => {
      const d = document.createElement('div');
      d.className = 'mc' + (S.clips.some((c) => c.m === i) ? ' used' : '');
      d.draggable = md.dur > 0;
      d.title = md.path + (md.dur > 0 ? '\n끌어 놓기 · 두 번 누르기 = 타임라인 끝에' : '');
      d.innerHTML = '<div class="th"></div><div class="nm"></div><button class="add" title="타임라인 끝에">+</button>';
      const th = d.querySelector('.th');
      const u = thumbUrl(md, (md.dur || 0) / 2);
      if (u) th.style.backgroundImage = 'url("' + u + '")';
      th.style.boxShadow = 'inset 0 -3px 0 ' + md.color;
      if (md.dur > 0) th.insertAdjacentHTML('beforeend', '<span class="du">' + mmss(md.dur) + '</span>');
      if (md.state !== 'done') th.insertAdjacentHTML('beforeend', '<div class="ld">' + (md.state === 'fail' ? '실패' : (md.line || '읽는 중')) + '</div>');
      d.querySelector('.nm').textContent = shortName(md.name);
      d.addEventListener('dragstart', (e) => { e.dataTransfer.setData('text/plain', 'rvm:' + i); e.dataTransfer.effectAllowed = 'copy'; });
      d.addEventListener('dblclick', () => addClip(i));
      d.querySelector('.add').addEventListener('click', (e) => { e.stopPropagation(); addClip(i); });
      box.appendChild(d);
    });
    if (S.jpgs.length) {
      const d = document.createElement('div');
      d.className = 'mc jpg';
      d.innerHTML = '<div class="th"><div class="ld">사진 ' + S.jpgs.length + '장 · 모두 씀</div></div><div class="nm">360 사진</div>';
      box.appendChild(d);
    }
  }

  // ------------------------------------------------------------ 타임라인 그리기
  function minPps() { const w = Math.max(200, $('tl').clientWidth - 60); return Math.max(0.02, w / Math.max(1, total())); }
  function setZoom(v, anchorT) {
    const tl = $('tl');
    const at = anchorT == null ? S.T : anchorT;
    const off = at * S.pps - tl.scrollLeft;
    S.zoomV = Math.max(0, Math.min(100, v));
    $('zoom').value = S.zoomV;
    const lo = minPps(), hi = Math.max(lo, MAX_PPS);
    S.pps = lo * Math.pow(hi / lo, S.zoomV / 100);
    renderTL();
    tl.scrollLeft = Math.max(0, at * S.pps - off);
    renderThumbs();
  }
  function refresh(rezoom) {
    if (rezoom) { const lo = minPps(), hi = Math.max(lo, MAX_PPS); S.pps = lo * Math.pow(hi / lo, S.zoomV / 100); }
    renderTL(); renderBin(); renderInfo(); mapDraw(); renderTC();
    $('noclip').classList.toggle('hide', S.clips.length > 0);
  }
  function renderTL() {
    const tl = $('tl'), tin = $('tlIn');
    const W = Math.max(tl.clientWidth, total() * S.pps + 300);
    tin.style.width = W + 'px';
    // 눈금
    const ru = $('ruler');
    ru.textContent = '';
    const steps = [1 / 30, 0.1, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800];
    const major = steps.find((s) => s * S.pps >= 90) || 1800;
    const minor = major / (major >= 60 ? 6 : 5);
    const n = Math.ceil(W / S.pps / minor);
    const frag = document.createDocumentFragment();
    for (let i = 0; i <= n && i < 4000; i++) {
      const t = i * minor, isMaj = Math.abs(t / major - Math.round(t / major)) < 1e-6;
      const e = document.createElement('div');
      e.className = 'tk' + (isMaj ? '' : ' s');
      e.style.left = (t * S.pps) + 'px';
      if (isMaj) e.textContent = major < 1 ? tc(t, 29.97) : mmss(t);
      frag.appendChild(e);
    }
    ru.appendChild(frag);
    // 토막
    const tr = $('track');
    tr.textContent = '';
    const st = starts();
    S.clips.forEach((c, k) => {
      const md = S.media[c.m];
      const d = document.createElement('div');
      d.className = 'clip' + (k === S.sel ? ' sel' : '') + (md.ready ? '' : ' load');
      d.style.left = (st[k] * S.pps) + 'px';
      d.style.width = Math.max(2, len(c) * S.pps - 1) + 'px';
      d.dataset.k = k;
      d.innerHTML = '<div class="cap"><b style="background:' + md.color + '"></b></div><div class="ths"></div>';
      d.querySelector('.cap').append(shortName(md.name).split(' · ')[0] + ' · ' + tc(len(c), fpsOf(c.m)));
      d.title = md.name + '\n' + tc(c.a, fpsOf(c.m)) + ' ~ ' + tc(c.b, fpsOf(c.m));
      tr.appendChild(d);
    });
    placePH();
    renderThumbs();
  }
  function renderThumbs() {           // 보이는 칸만 그림 (긴 영상 · 크게 볼 때 수천 장 막기)
    const tl = $('tl');
    const vl = tl.scrollLeft - 300, vr = tl.scrollLeft + tl.clientWidth + 300;
    const st = starts();
    document.querySelectorAll('#track .clip').forEach((d) => {
      const k = +d.dataset.k, c = S.clips[k], md = S.media[c.m];
      const box = d.querySelector('.ths');
      if (!md.ready || !md.thumbs) { box.textContent = ''; return; }
      const x0 = st[k] * S.pps, w = len(c) * S.pps;
      const j0 = Math.max(0, Math.floor((vl - x0) / TH_W)), j1 = Math.min(Math.ceil(w / TH_W) - 1, Math.ceil((vr - x0) / TH_W));
      const key = j0 + ':' + j1 + ':' + S.pps.toFixed(4);
      if (box.dataset.key === key) return;
      box.dataset.key = key;
      box.textContent = '';
      for (let j = j0; j <= j1; j++) {
        const im = document.createElement('img');
        im.src = thumbUrl(md, Math.min(c.b, c.a + (j * TH_W + TH_W / 2) / S.pps));
        im.style.left = (j * TH_W) + 'px';
        im.draggable = false;
        box.appendChild(im);
      }
    });
  }
  function placePH(follow) {
    const x = S.T * S.pps;
    $('ph').style.left = x + 'px';
    if (follow) {
      const tl = $('tl');
      if (x < tl.scrollLeft + 20 || x > tl.scrollLeft + tl.clientWidth - 40) tl.scrollLeft = Math.max(0, x - tl.clientWidth * 0.3);
    }
  }
  function renderTC() {
    const L = locate(S.T), f = L ? fpsOf(L.m) : 29.97;
    $('tc').innerHTML = tc(S.T, f) + ' <i>/ ' + tc(total(), f) + '</i>';
  }
  function trackAt(md, t) {           // 원본 초 → [위도, 경도, 진행 m]
    const tr = md.track;
    if (!tr || !tr.length) return null;
    if (t <= tr[0][0]) return tr[0].slice(1, 4);
    if (t >= tr[tr.length - 1][0]) return tr[tr.length - 1].slice(1, 4);
    let lo = 0, hi = tr.length - 1;
    while (hi - lo > 1) { const mid = (lo + hi) >> 1; if (tr[mid][0] <= t) lo = mid; else hi = mid; }
    const A = tr[lo], B = tr[hi], u = (t - A[0]) / Math.max(1e-6, B[0] - A[0]);
    return [A[1] + (B[1] - A[1]) * u, A[2] + (B[2] - A[2]) * u, A[3] + (B[3] - A[3]) * u];
  }
  function renderInfo() {
    const tot = total();
    $('tlInfo').textContent = S.clips.length ? '토막 ' + S.clips.length + ' · ' + mmss(tot) : '';
    const gap = Math.max(0.5, Number($('gap').value) || 5);
    let shots = 0, unknown = false;
    for (const c of S.clips) {
      const md = S.media[c.m];
      if (!md.ready) { unknown = true; continue; }
      const A = trackAt(md, c.a), B = trackAt(md, c.b);
      if (A && B) shots += Math.floor(Math.max(0, B[2] - A[2]) / gap) + 1;
    }
    const n = shots + (S.jpgs.length || 0);
    $('est').textContent = !S.clips.length && !S.jpgs.length ? '' :
      (unknown ? '어림 중…' : '촬영점 약 ' + n + '곳 · 약 ' + Math.max(1, Math.round(n * SEC_PER_SHOT / 60)) + '분');
  }

  // ------------------------------------------------------------ 미리보기
  const vid = () => $('vid');
  function showAt(T) {
    renderTC(); placePH(); mapDraw();
    const L = locate(T);
    if (!L) { vid().removeAttribute('src'); S.curM = -1; $('still').classList.add('hide'); return; }
    const md = S.media[L.m];
    if (md.novid || !md.lrf_url) {        // 영상을 못 트는 PC: 썸네일 먼저 → 멈추면 그 장면 (ffmpeg)
      vid().classList.add('hide');
      const st = $('still'); st.classList.remove('hide');
      const u = thumbUrl(md, L.t);
      if (u && !st.classList.contains('full') && st.getAttribute('src') !== u) st.src = u;
      clearTimeout(S.frameTimer);
      if (S.playing) return;
      const want = L.m + ':' + L.t.toFixed(3);
      S.frameTimer = setTimeout(async () => {
        const url = await call('frame_at', md.path, L.t);
        const N = locate(S.T);
        if (url && N && N.m + ':' + N.t.toFixed(3) === want) { st.classList.add('full'); st.src = url; }
      }, 180);
      if (st.dataset.want !== want) { st.classList.remove('full'); st.dataset.want = want; if (u) st.src = u; }
      return;
    }
    $('still').classList.add('hide'); vid().classList.remove('hide');
    if (S.curM !== L.m) {
      S.curM = L.m;
      S.seekBusy = false;
      S.seekNext = L.t;
      vid().src = md.lrf_url;
      return;                                     // loadedmetadata 에서 seekNext 로 감
    }
    seek(L.t);
  }
  function seek(t) {
    const v = vid();
    if (v.readyState < 1 || S.seekBusy) { S.seekNext = t; return; }
    S.seekBusy = true;
    S.seekNext = null;
    v.currentTime = t + 0.001;
  }
  function onSeeked() {
    S.seekBusy = false;
    if (S.seekNext != null) { seek(S.seekNext); return; }
    if (S.resume && S.playing) { S.resume = false; vid().play().catch(() => {}); }
  }
  function play() {
    if (!S.clips.length) return;
    if (S.T >= total() - 1e-3) S.T = 0;
    S.playing = true;
    $('icPlay').classList.add('hide'); $('icPause').classList.remove('hide');
    S.resume = true;
    showAt(S.T);
    if (!S.seekBusy && S.seekNext == null && vid().readyState >= 1) { S.resume = false; vid().play().catch(() => {}); }
    S.lastRaf = performance.now();
    requestAnimationFrame(tick);
  }
  function pause() {
    S.playing = false; S.resume = false;
    vid().pause();
    $('icPlay').classList.remove('hide'); $('icPause').classList.add('hide');
    renderTC();
  }
  function tick(now) {
    if (!S.playing) return;
    const dt = Math.min(0.25, (now - S.lastRaf) / 1000); S.lastRaf = now;
    const L = locate(S.T);
    if (!L) { pause(); return; }
    const c = S.clips[L.k], md = S.media[c.m];
    const st = starts();
    if (md.novid || !md.lrf_url) {
      S.T += dt;
    } else if (!S.seekBusy && S.seekNext == null && S.curM === c.m && !vid().paused) {
      S.T = st[L.k] + Math.max(0, vid().currentTime - c.a);
    }
    if (S.T >= st[L.k] + len(c) - 0.5 / fpsOf(c.m)) {     // 토막 끝 → 다음 토막
      if (L.k + 1 >= S.clips.length) { S.T = total(); pause(); showAt(S.T); return; }
      const nx = S.clips[L.k + 1];
      S.T = st[L.k + 1];
      if (!(nx.m === c.m && Math.abs(nx.a - c.b) < 1e-3)) { vid().pause(); S.resume = true; showAt(S.T); }
    }
    renderTC(); placePH(true); mapDraw();
    requestAnimationFrame(tick);
  }
  function stepFrames(n) {
    pause();
    const L = locate(S.T);
    const f = L ? fpsOf(L.m) : 29.97;
    S.T = Math.max(0, Math.min(total(), Math.round((S.T + n / f) * f) / f));
    showAt(S.T); placePH(true);
  }
  function setLens(l) {
    FV.mode = l;
    $('lens').className = 'lens ' + l;
    $('stage').classList.toggle('free', l === 'free');
    document.querySelectorAll('#lensSel button').forEach((b) => b.classList.toggle('on', b.dataset.l === l));
    FV.dirty = true;
  }

  // ------------------------------------------------------------ 프리 뷰 (JACK 1125: 마우스로 돌려 보기)
  // LRF 한 장(왼쪽 위 렌즈 | 오른쪽 아래 렌즈)을 셰이더에서 원근 화면으로. 렌즈 값 = 사진 꿰매기와 같음(rig · 3840 기준).
  // 수평은 장면마다 카메라 방향(rot: 아래 렌즈 → ENU)으로 맞추고, 보는 방향은 드론 앞 + 끈 만큼.
  const FV = { mode: 'free', gl: null, prog: null, tex: null, u: {}, yaw: 0, pitch: -25, fov: 90, dirty: true, hasTex: false, texKey: '' };
  const FV_VS = 'attribute vec2 a; varying vec2 p; void main(){ p = a; gl_Position = vec4(a, 0.0, 1.0); }';
  const FV_FS = [
    'precision highp float;',
    'uniform sampler2D tex; uniform mat3 Rcw; uniform mat3 R01; uniform vec3 vf; uniform vec3 vr; uniform vec3 vu;',
    'uniform float th; uniform float asp; uniform vec4 L0; uniform vec4 L1; uniform float seam; uniform float blend;',
    'varying vec2 p;',
    'vec2 lens(vec3 d, float f, float cx, float cy, float s){ float t = acos(clamp(d.z,-1.0,1.0)); float a = atan(d.y, d.x);',
    '  float r = f*t; return vec2((cx + r*cos(a) + 0.5)/s, (cy + r*sin(a) + 0.5)/s); }',
    'void main(){',
    '  vec3 dw = normalize(vf + p.x*asp*th*vr + p.y*th*vu);',
    '  vec3 d1 = Rcw*dw; vec3 d0 = R01*d1;',
    '  float t1 = acos(clamp(d1.z,-1.0,1.0)); float t0 = acos(clamp(d0.z,-1.0,1.0));',
    '  float w1 = clamp((seam + blend - degrees(t1))/(2.0*blend), 0.0, 1.0);',
    '  if (L1.x*t1 >= L0.w) w1 = 0.0; if (L0.x*t0 >= L0.w) w1 = 1.0;',
    '  vec2 u1 = lens(d1, L1.x, L1.y, L1.z, L1.w); u1.x = 0.5 + 0.5*u1.x;',
    '  vec2 u0 = lens(d0, L0.x, L0.y, L0.z, L1.w); u0.x = 0.5*u0.x;',
    '  gl_FragColor = vec4(mix(texture2D(tex,u0).rgb, texture2D(tex,u1).rgb, w1), 1.0);',
    '}'].join('\n');
  function fvInit() {
    const cv = $('fv');
    const gl = cv.getContext('webgl', { antialias: false, preserveDrawingBuffer: true });
    if (!gl) return;
    const sh = (type, src) => { const s = gl.createShader(type); gl.shaderSource(s, src); gl.compileShader(s);
      if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s)); return s; };
    const pr = gl.createProgram();
    gl.attachShader(pr, sh(gl.VERTEX_SHADER, FV_VS)); gl.attachShader(pr, sh(gl.FRAGMENT_SHADER, FV_FS));
    gl.linkProgram(pr); gl.useProgram(pr);
    const buf = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, buf);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]), gl.STATIC_DRAW);
    const loc = gl.getAttribLocation(pr, 'a'); gl.enableVertexAttribArray(loc); gl.vertexAttribPointer(loc, 2, gl.FLOAT, false, 0, 0);
    const tex = gl.createTexture(); gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    for (const n of ['tex', 'Rcw', 'R01', 'vf', 'vr', 'vu', 'th', 'asp', 'L0', 'L1', 'seam', 'blend']) FV.u[n] = gl.getUniformLocation(pr, n);
    Object.assign(FV, { gl, prog: pr, tex });
    // 끌기 = 둘러보기 · 휠 = 확대 · 두 번 = 앞으로
    let down = null;
    cv.addEventListener('pointerdown', (e) => { down = { x: e.clientX, y: e.clientY, yaw: FV.yaw, pitch: FV.pitch, moved: false }; cv.setPointerCapture(e.pointerId); });
    cv.addEventListener('pointermove', (e) => {
      if (!down) return;
      const dx = e.clientX - down.x, dy = e.clientY - down.y;
      if (!down.moved && dx * dx + dy * dy < 16) return;
      down.moved = true; cv.classList.add('drag');
      const k = FV.fov / Math.max(100, cv.clientHeight);
      FV.yaw = down.yaw - dx * k; FV.pitch = Math.max(-89, Math.min(89, down.pitch + dy * k)); FV.dirty = true;
    });
    cv.addEventListener('pointerup', (e) => {
      cv.classList.remove('drag');
      if (down && !down.moved && window.RVAlign) window.RVAlign.viewClick(e);
      down = null;
    });
    cv.addEventListener('wheel', (e) => { e.preventDefault(); FV.fov = Math.max(25, Math.min(120, FV.fov * (e.deltaY > 0 ? 1.1 : 1 / 1.1))); FV.dirty = true; }, { passive: false });
    cv.addEventListener('dblclick', () => { FV.yaw = 0; FV.pitch = -25; FV.fov = 90; FV.dirty = true; });
    const v = vid();
    if (v.requestVideoFrameCallback) { const cb = () => { FV.dirty = true; v.requestVideoFrameCallback(cb); }; v.requestVideoFrameCallback(cb); }
    v.addEventListener('seeked', () => { FV.dirty = true; });
    v.addEventListener('loadeddata', () => { FV.dirty = true; });
    requestAnimationFrame(fvLoop);
  }
  function rotAt(md, t) {               // 원본 초 → {R: 아래 렌즈→ENU 행렬(행 순서 9), yaw: 드론 방향 °}
    const r = md && md.rot;
    if (!r || !r.length) return { R: [1, 0, 0, 0, -1, 0, 0, 0, -1], yaw: 0 };
    let lo = 0, hi = r.length - 1;
    if (t <= r[0][0]) hi = 0; else if (t >= r[hi][0]) lo = hi;
    else while (hi - lo > 1) { const m = (lo + hi) >> 1; if (r[m][0] <= t) lo = m; else hi = m; }
    const A = r[lo], B = r[hi], u = hi === lo ? 0 : (t - A[0]) / Math.max(1e-6, B[0] - A[0]);
    const R = []; for (let i = 0; i < 9; i++) R.push(A[2 + i] + (B[2 + i] - A[2 + i]) * u);
    let dy = ((B[1] - A[1] + 540) % 360) - 180;
    return { R, yaw: A[1] + dy * u };
  }
  function fvView(md, t) {              // 지금 보는 방향 (ENU) — 드론 앞 + 끈 만큼
    const ro = rotAt(md, t);
    const y = (ro.yaw + FV.yaw) * Math.PI / 180, p = FV.pitch * Math.PI / 180;
    const f = [Math.cos(p) * Math.sin(y), Math.cos(p) * Math.cos(y), Math.sin(p)];
    const r = [Math.cos(y), -Math.sin(y), 0];
    const u = [r[1] * f[2] - r[2] * f[1], r[2] * f[0] - r[0] * f[2], r[0] * f[1] - r[1] * f[0]];
    return { f, r, u, R: ro.R, yaw: ro.yaw };
  }
  function fvSrc() {                    // 지금 그릴 장면 → {md, t, el}
    const L = locate(S.T);
    if (!L) return null;
    const md = S.media[L.m];
    if (md.novid || !md.lrf_url) {
      const st = $('still');
      return st.classList.contains('full') && st.complete && st.naturalWidth ? { md, t: L.t, el: st, key: st.src } : { md, t: L.t, el: null };
    }
    const v = vid();
    const t = S.curM === L.m && v.readyState >= 2 ? v.currentTime : L.t;
    return { md, t, el: S.curM === L.m && v.readyState >= 2 ? v : null, key: 'v' };
  }
  function fvLoop() {
    requestAnimationFrame(fvLoop);
    if (FV.mode !== 'free' || !isOpen() || !FV.gl) return;
    const cv = $('fv'), gl = FV.gl;
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    const W = Math.round(cv.clientWidth * dpr), H = Math.round(cv.clientHeight * dpr);
    if (cv.width !== W || cv.height !== H) { cv.width = W; cv.height = H; FV.dirty = true; }
    if (!FV.dirty && !S.playing) return;
    FV.dirty = false;
    const src = fvSrc();
    gl.viewport(0, 0, W, H);
    if (!src || !src.md.rig) { gl.clearColor(0, 0, 0, 1); gl.clear(gl.COLOR_BUFFER_BIT); return; }
    if (src.el && (src.key === 'v' || src.key !== FV.texKey)) {
      try { gl.bindTexture(gl.TEXTURE_2D, FV.tex); gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGB, gl.RGB, gl.UNSIGNED_BYTE, src.el); FV.hasTex = true; FV.texKey = src.key; } catch (e) { FV.hasTex = false; }
    }
    if (!FV.hasTex) { gl.clearColor(0, 0, 0, 1); gl.clear(gl.COLOR_BUFFER_BIT); return; }
    const rg = src.md.rig, V = fvView(src.md, src.t);
    const R01 = rg.R01;            // 행 순서 → GLSL 은 열 순서라 전치해서 넘김
    gl.uniformMatrix3fv(FV.u.Rcw, false, new Float32Array(V.R));        // 행 순서 Rwc 를 열 순서로 = Rcwᵀ… = Rcw
    gl.uniformMatrix3fv(FV.u.R01, false, new Float32Array([R01[0], R01[3], R01[6], R01[1], R01[4], R01[7], R01[2], R01[5], R01[8]]));
    gl.uniform3fv(FV.u.vf, V.f); gl.uniform3fv(FV.u.vr, V.r); gl.uniform3fv(FV.u.vu, V.u);
    gl.uniform1f(FV.u.th, Math.tan(FV.fov * Math.PI / 360)); gl.uniform1f(FV.u.asp, W / Math.max(1, H));
    gl.uniform4f(FV.u.L0, rg.f0, rg.c0x, rg.c0y, rg.rmax); gl.uniform4f(FV.u.L1, rg.f1, rg.c1x, rg.c1y, rg.size);
    const lim1 = rg.rmax / rg.f1 * 180 / Math.PI, lim0 = rg.rmax / rg.f0 * 180 / Math.PI;
    gl.uniform1f(FV.u.seam, 0.5 * (lim1 + (180 - lim0))); gl.uniform1f(FV.u.blend, 3.0);
    gl.uniform1i(FV.u.tex, 0);
    gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
    gcpOverlay(src, V, W, H);
  }

  // ------------------------------------------------------------ 좌표 보정 · GCP (JACK 1125: 이 단계에서 · GCP 점으로)
  // GCP = {id, name, E, N (EPSG:5186), z: 측량 표고 | null, zd: 수치지도 표고 | null, lat, lon}
  // 짝  = {g: GCP id, path: 영상, t: 원본 초, p: 맞춤 전 방향 (격자 ENU 단위 벡터)} — 프리 뷰에서 그 점을 누른 것
  // 맞춤 (영상마다 · 보기 화면 calib 과 같은 뜻): 카메라 = (E + dx, N + dy, zt + 이륙점 기준 높이), 방향 w = R·p,
  //   R = Rz(dyaw)·Rx(dpitch)·Ry(droll). 짝마다 광선이 GCP 표고 면에 닿는 곳 − GCP (m) 를 최소로 (Levenberg–Marquardt).
  //   짝 1 → 동 · 북 · 2 → + 방향 · 3 이상 → + 높이(zt) · 먼 점(40 m 넘게)이 있고 넉넉하면 + 남북 · 동서 기울기.
  //   표고 없는 점(지도에서 찍고 수치지도도 없음)은 이륙점 높이의 평평한 땅으로 본다.
  const G = { pts: [], pairs: [], active: -1, mapAdd: false, cal: {}, res: {}, seq: 0 };
  const GA_FAR = 40, GA_TILT = 5;
  function rot64(c) {                   // 보기 화면 app.js rot64 와 같음 (행 순서)
    const a = (c.dyaw || 0) * Math.PI / 180, b = (c.dpitch || 0) * Math.PI / 180, g = (c.droll || 0) * Math.PI / 180;
    const ca = Math.cos(a), sa = Math.sin(a), cb = Math.cos(b), sb = Math.sin(b), cg = Math.cos(g), sg = Math.sin(g);
    const m = (A, B) => { const C = []; for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) C.push(A[i * 3] * B[j] + A[i * 3 + 1] * B[3 + j] + A[i * 3 + 2] * B[6 + j]); return C; };
    return m(m([ca, sa, 0, -sa, ca, 0, 0, 0, 1], [1, 0, 0, 0, cb, -sb, 0, sb, cb]), [cg, 0, sg, 0, 1, 0, -sg, 0, cg]);
  }
  const mv = (R, p) => [R[0] * p[0] + R[1] * p[1] + R[2] * p[2], R[3] * p[0] + R[4] * p[1] + R[5] * p[2], R[6] * p[0] + R[7] * p[1] + R[8] * p[2]];
  const mtv = (R, p) => [R[0] * p[0] + R[3] * p[1] + R[6] * p[2], R[1] * p[0] + R[4] * p[1] + R[7] * p[2], R[2] * p[0] + R[5] * p[1] + R[8] * p[2]];
  const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
  function posAt(md, t) {               // 원본 초 → {E, N, rel} (경로 0.5 초 사이 직선)
    const tr = md.track;
    if (!tr || !tr.length || tr[0].length < 7) return null;
    let lo = 0, hi = tr.length - 1;
    if (t <= tr[0][0]) hi = 0; else if (t >= tr[hi][0]) lo = hi;
    else while (hi - lo > 1) { const m = (lo + hi) >> 1; if (tr[m][0] <= t) lo = m; else hi = m; }
    const A = tr[lo], B = tr[hi], u = hi === lo ? 0 : (t - A[0]) / Math.max(1e-6, B[0] - A[0]);
    const lerp = (i) => (A[i] == null || B[i] == null ? (A[i] ?? B[i] ?? 0) : A[i] + (B[i] - A[i]) * u);
    return { E: lerp(5), N: lerp(6), rel: lerp(4) };
  }
  const gById = (id) => G.pts.find((g) => g.id === id);
  const mByPath = (p) => S.media.findIndex((m) => m.path.toLowerCase() === String(p).toLowerCase());
  function calOf(md) {
    if (!G.cal[md.path]) G.cal[md.path] = { dx: 0, dy: 0, dyaw: 0, dpitch: 0, droll: 0, zt: md.zt0 ?? 0, zt0: md.zt0 ?? 0, st: 'none' };
    return G.cal[md.path];
  }
  function gZ(g, c) { return g.z != null ? g.z : g.zd != null ? g.zd : c.zt0; }       // 그 점 표고 (없으면 이륙점 높이 평면)
  function gHit(md, c, R, q) {          // 짝 q 의 광선이 GCP 표고 면에 닿는 곳 [E, N] · 못 닿으면 null
    const ps = posAt(md, q.t), g = gById(q.g);
    if (!ps || !g) return null;
    const w = mv(R, q.p);
    if (w[2] > -0.02) return null;
    const cz = c.zt + ps.rel, s = (gZ(g, c) - cz) / w[2];
    if (!(s > 0)) return null;
    return [ps.E + c.dx + s * w[0], ps.N + c.dy + s * w[1], s];
  }
  function gSolve(path) {               // → 'ok' · 'bad' · 'none' (보기 화면 alSolve 와 같은 차례)
    const m = mByPath(path); if (m < 0) return 'none';
    const md = S.media[m], c0 = calOf(md), use = G.pairs.filter((q) => q.path === path && gById(q.g));
    if (md.ztSrc !== 'dem') {            // 수치지도가 없으면 이륙점 높이 = 짝 GCP 표고 가운데값 (처음 값 · 평면 땅 높이)
      const zs = use.map((q) => gById(q.g)).filter((g) => g.z != null || g.zd != null).map((g) => (g.z != null ? g.z : g.zd)).sort((a, b) => a - b);
      if (zs.length) { const zm = zs[zs.length >> 1]; if (c0.zt0 !== zm) { c0.zt0 = zm; if (!(c0.keys || []).includes('zt')) c0.zt = zm; } }
    }
    if (!use.length) { Object.assign(c0, { dx: 0, dy: 0, dyaw: 0, dpitch: 0, droll: 0, zt: c0.zt0, st: 'none' }); G.res[path] = []; return 'none'; }
    const keys = use.length === 1 ? ['dx', 'dy'] : use.length === 2 ? ['dx', 'dy', 'dyaw'] : ['dx', 'dy', 'dyaw', 'zt'];
    const base = Object.assign({}, c0, { dpitch: 0, droll: 0 });
    keys.forEach((k) => { if (!Number.isFinite(base[k])) base[k] = 0; });
    const h0 = use.map((q) => gHit(md, base, rot64(base), q));
    const far = h0.some((h, i) => { const ps = posAt(md, use[i].t); return h && ps && Math.hypot(h[0] - ps.E - base.dx, h[1] - ps.N - base.dy) > GA_FAR; });
    if (use.length >= 3 && far && keys.length + 2 <= use.length * 2) keys.push('dpitch', 'droll');
    const cOf = (x) => { const c = Object.assign({}, base); keys.forEach((k, i) => { c[k] = x[i]; }); return c; };
    const res = (x) => {
      const c = cOf(x), R = rot64(c), out = [];
      for (const q of use) { const h = gHit(md, c, R, q); if (!h) return null; const g = gById(q.g); out.push(h[0] - g.E, h[1] - g.N); }
      return out;
    };
    const cost = (r) => r.reduce((a, v) => a + v * v, 0);
    let x = keys.map((k) => base[k]), r = res(x), lam = 1e-3;
    if (!r) { // 처음 값으로 안 닿음 (먼 짝 · 큰 어긋남) → 0 에서 다시
      x = keys.map((k) => (k === 'zt' ? c0.zt0 : 0)); r = res(x);
      if (!r) { c0.st = 'bad'; return 'bad'; }
    }
    let f = cost(r);
    for (let it = 0; it < 40; it++) {
      const J = [];
      for (let j = 0; j < keys.length; j++) {
        const xp = x.slice(), xm = x.slice(); xp[j] += 1e-4; xm[j] -= 1e-4;
        const rp = res(xp), rm = res(xm); if (!rp || !rm) { c0.st = 'bad'; return 'bad'; }
        J.push(rp.map((v, i) => (v - rm[i]) / 2e-4));
      }
      const A = J.map((a) => J.map((b) => a.reduce((s, v, i) => s + v * b[i], 0)));
      const gr = J.map((a) => -a.reduce((s, v, i) => s + v * r[i], 0));
      let step = -1;
      for (let k = 0; k < 10 && step < 0; k++) {
        const d = solveLin(A.map((row, i) => row.map((v, j) => (i === j ? v * (1 + lam) + 1e-12 : v))), gr);
        const xn = d && x.map((v, i) => v + d[i]), rn = xn && res(xn);
        if (rn && cost(rn) < f) { step = Math.max(...d.map(Math.abs)); x = xn; r = rn; f = cost(rn); lam = Math.max(lam / 10, 1e-9); } else lam *= 10;
      }
      if (step < 1e-7) break;
    }
    const c = cOf(x);
    if (!x.every(Number.isFinite) || Math.abs(c.dyaw) > 30 || Math.abs(c.dpitch) > GA_TILT || Math.abs(c.droll) > GA_TILT ||
        Math.abs(c.zt - c0.zt0) > 40 || Math.hypot(c.dx, c.dy) > 60) { c0.st = 'bad'; return 'bad'; }
    Object.assign(c0, c, { st: 'ok', keys: keys.slice() });
    G.res[path] = use.map((q, i) => ({ q, r: Math.hypot(r[2 * i], r[2 * i + 1]) }));
    return 'ok';
  }
  function solveLin(A, b) {
    const n = b.length, M = A.map((row, i) => row.concat([b[i]]));
    for (let i = 0; i < n; i++) {
      let p = i; for (let k = i + 1; k < n; k++) if (Math.abs(M[k][i]) > Math.abs(M[p][i])) p = k;
      if (!(Math.abs(M[p][i]) > 1e-300)) return null;
      [M[i], M[p]] = [M[p], M[i]];
      for (let k = i + 1; k < n; k++) { const f = M[k][i] / M[i][i]; for (let j = i; j <= n; j++) M[k][j] -= f * M[i][j]; }
    }
    const x = new Array(n);
    for (let i = n - 1; i >= 0; i--) { let s = M[i][n]; for (let j = i + 1; j < n; j++) s -= M[i][j] * x[j]; x[i] = s / M[i][i]; }
    return x.every(Number.isFinite) ? x : null;
  }
  function gSolveAll() { [...new Set(G.pairs.map((q) => q.path))].forEach(gSolve); Object.keys(G.cal).forEach((p) => { if (!G.pairs.some((q) => q.path === p)) gSolve(p); }); }
  async function ensureZt0(md) {         // 이륙점(경로 첫 점) 땅 표고 — 수치지도, 없으면 0 (평평한 땅)
    if (md.zt0 !== undefined || !md.track || !md.track.length || md.track[0].length < 7) return;
    md.zt0 = 0;
    const z = await call('ground_z', [[md.track[0][5], md.track[0][6]]]);
    if (z && z[0] != null) { md.zt0 = z[0]; md.ztSrc = 'dem'; }
    const c = G.cal[md.path]; if (c && c.st === 'none') { c.zt = c.zt0 = md.zt0; }
  }
  async function gAddPoints(list) {      // [{name, E, N, z, lat, lon}] → 수치지도 표고(zd) 채워 넣기
    const need = list.filter((g) => g.z == null);
    if (need.length) {
      const z = await call('ground_z', need.map((g) => [g.E, g.N]));
      if (z) need.forEach((g, i) => { g.zd = z[i]; });
    }
    for (const g of list) { g.id = 'g' + (++G.seq); G.pts.push(g); }
    gRender(); mapDraw(); FV.dirty = true;
  }
  async function gLoad() {
    const r = await call('gcp_pick');
    if (!r) return;
    if (!r.ok) { toast(r.msg); return; }
    // 두 좌표 순서 (X=동 · X=북) 중 비행 경로에 가까운 쪽 (한국 측량 = X 북 · Y 동)
    const tr = []; S.media.forEach((md) => (md.track || []).forEach((p) => { if (p.length >= 7) tr.push([p[5], p[6]]); }));
    const near = (c) => { if (!tr.length) return 0; let b = Infinity; for (let i = 0; i < tr.length; i += 4) b = Math.min(b, Math.hypot(tr[i][0] - c.E, tr[i][1] - c.N)); return b; };
    let pick = 1;
    if (r.points[0].cand.length > 1) {
      const med = (k) => { const d = r.points.map((p) => near(p.cand[k])).sort((a, b) => a - b); return d[d.length >> 1]; };
      pick = tr.length ? (med(0) < med(1) ? 0 : 1) : 1;
    }
    const list = r.points.map((p) => { const c = p.cand[Math.min(pick, p.cand.length - 1)]; return { name: p.name, E: c.E, N: c.N, lat: c.lat, lon: c.lon, z: p.z, zd: null }; });
    const far = list.filter((g) => tr.length && near(g) > 3000).length;
    await gAddPoints(list);
    toast('GCP ' + list.length + '개' + (r.points[0].cand.length > 1 ? ' · ' + (pick ? 'X = 북' : 'X = 동') : '') + (far ? ' · 경로에서 3 km 넘는 점 ' + far : ''));
    MAP.fitted = false; mapDraw();
  }
  async function gMapAdd(lat, lon) {
    const en = await call('gcp_en', lat, lon);
    if (!en) return;
    let k = 1; while (G.pts.some((g) => g.name === 'M' + k)) k++;
    const g = { name: 'M' + k, E: en[0], N: en[1], lat, lon, z: null, zd: null };
    await gAddPoints([g]);
    G.active = g.id; gRender();
  }
  function gDel(id) {
    const paths = new Set(G.pairs.filter((q) => q.g === id).map((q) => q.path));
    G.pts = G.pts.filter((g) => g.id !== id); G.pairs = G.pairs.filter((q) => q.g !== id);
    if (G.active === id) G.active = -1;
    paths.forEach(gSolve); gRender(); mapDraw(); FV.dirty = true;
  }
  function gDelPair(q) { G.pairs = G.pairs.filter((x) => x !== q); gSolve(q.path); gRender(); mapDraw(); FV.dirty = true; }
  function viewClick(e) {                // 프리 뷰를 눌렀다 (끌지 않고)
    if (G.active === -1) { if (G.pts.length) toast('GCP 를 고른 뒤 그 점을 누르세요'); return; }
    const src = fvSrc(); if (!src || !src.md.rot || !src.md.rot.length) { toast('이 영상은 방향 기록이 없음'); return; }
    const cv = $('fv'), rc = cv.getBoundingClientRect();
    const px = (e.clientX - rc.left) / rc.width * 2 - 1, py = 1 - (e.clientY - rc.top) / rc.height * 2;
    const V = fvView(src.md, src.t), th = Math.tan(FV.fov * Math.PI / 360), asp = rc.width / Math.max(1, rc.height);
    const d = [0, 1, 2].map((i) => V.f[i] + px * asp * th * V.r[i] + py * th * V.u[i]);
    const n = Math.hypot(d[0], d[1], d[2]);
    if (d[2] / n > -0.02) { toast('땅을 눌러 주세요 (수평선 위)'); return; }
    G.pairs = G.pairs.filter((q) => !(q.g === G.active && q.path === src.md.path && Math.abs(q.t - src.t) < 0.05));
    G.pairs.push({ g: G.active, path: src.md.path, t: +src.t.toFixed(3), p: d.map((v) => +(v / n).toFixed(7)) });
    const st = gSolve(src.md.path);
    if (st === 'bad') toast('풀리지 않음 · 점을 다시 확인 (× 로 지우기)');
    gRender(); mapDraw(); FV.dirty = true;
  }
  window.RVAlign = { viewClick };
  function gRender() {
    const box = $('gList'); box.textContent = '';
    for (const g of G.pts) {
      const n = G.pairs.filter((q) => q.g === g.id);
      const rs = []; Object.values(G.res).forEach((L) => L.forEach((o) => { if (o.q.g === g.id) rs.push(o.r); }));
      const d = document.createElement('div');
      d.className = 'gi' + (G.active === g.id ? ' on' : '');
      d.innerHTML = '<i class="tri"></i><span class="nm"></span><span class="z"></span><span class="pc"></span><button class="x ib" title="지우기">×</button>';
      d.querySelector('.nm').textContent = g.name;
      d.querySelector('.z').textContent = g.z != null ? 'Z ' + g.z.toFixed(2) : g.zd != null ? 'Z≈' + g.zd.toFixed(1) : 'Z -';
      d.querySelector('.z').title = g.z != null ? '측량 표고' : g.zd != null ? '수치지도 표고' : '표고 없음 · 이륙점 높이 평면';
      d.querySelector('.pc').textContent = n.length ? (rs.length ? Math.max(...rs).toFixed(2) + ' m' : '짝 ' + n.length) : '';
      d.querySelector('.pc').title = n.length ? '짝 ' + n.length + ' · 가장 큰 어긋남' : '';
      d.addEventListener('click', (e) => {
        if (e.target.classList.contains('x')) { gDel(g.id); return; }
        G.active = G.active === g.id ? -1 : g.id; gRender(); mapDraw(); FV.dirty = true;
      });
      box.appendChild(d);
      if (G.active === g.id) {
        for (const q of n) {
          const md = S.media[mByPath(q.path)];
          const o = (G.res[q.path] || []).find((x) => x.q === q);
          const e = document.createElement('div');
          e.className = 'gi'; e.style.paddingLeft = '22px';
          e.innerHTML = '<span class="nm"></span><span class="pc"></span><button class="x ib" title="이 짝 지우기">×</button>';
          e.querySelector('.nm').textContent = (md ? shortName(md.name).split(' · ')[0] : '?') + ' · ' + tc(q.t, md ? md.fps : 29.97);
          e.querySelector('.pc').textContent = o ? o.r.toFixed(2) + ' m' : '';
          e.addEventListener('click', (ev) => {
            if (ev.target.classList.contains('x')) { gDelPair(q); return; }
            const k = S.clips.findIndex((c) => S.media[c.m].path === q.path && q.t >= c.a && q.t <= c.b);
            if (k < 0) { toast('지운 구간'); return; }
            pause(); S.T = starts()[k] + q.t - S.clips[k].a; showAt(S.T); placePH(true);
          });
          box.appendChild(e);
        }
      }
    }
    const hint = $('gHint'), act = gById(G.active);
    hint.classList.toggle('hide', !act || FV.mode !== 'free');
    if (act) hint.textContent = '«' + act.name + '» 을 화면에서 누르기';
    // 결과: 영상마다
    const lines = [];
    for (const md of S.media) {
      const c = G.cal[md.path]; const n = G.pairs.filter((q) => q.path === md.path).length;
      if (!c || !n) continue;
      const nm = shortName(md.name).split(' · ')[0];
      if (c.st !== 'ok') { lines.push('<span class="bad">' + nm + ' · 풀리지 않음 (짝 ' + n + ')</span>'); continue; }
      const L = G.res[md.path] || [], rms = Math.sqrt(L.reduce((s, o) => s + o.r * o.r, 0) / Math.max(1, L.length));
      const sg = (v, d) => (v >= 0 ? '+' : '') + v.toFixed(d);
      let s = '<b>' + nm + '</b> 동 ' + sg(c.dx, 2) + ' · 북 ' + sg(c.dy, 2) + ' m';
      if ((c.keys || []).includes('dyaw')) s += ' · 방향 ' + sg(c.dyaw, 2) + '°';
      if ((c.keys || []).includes('zt')) s += ' · 높이 ' + sg(c.zt - c.zt0, 1) + ' m';
      if ((c.keys || []).includes('dpitch')) s += ' · 기울기 ' + sg(c.dpitch, 2) + '/' + sg(c.droll, 2) + '°';
      s += ' · 어긋남 ' + rms.toFixed(2) + ' m (짝 ' + n + (n < 3 ? ' · 3곳 이상 권장' : '') + ')';
      lines.push(s);
    }
    $('gRes').innerHTML = lines.join('<br>') || (G.pts.length ? 'GCP 를 고르고 화면에서 그 점 누르기' : '');
  }
  function gcpOverlay(src, V, W, H) {   // 프리 뷰 위 GCP 표시 (맞춤 반영 · 맞으면 화면의 그 점 위에 옴)
    const cv = $('fvo');
    if (cv.width !== W || cv.height !== H) { cv.width = W; cv.height = H; }
    const g2 = cv.getContext('2d');
    g2.clearRect(0, 0, W, H);
    if (!G.pts.length || !src) return;
    const md = src.md, c = calOf(md), ps = posAt(md, src.t);
    if (!ps) return;
    const R = rot64(c), th = Math.tan(FV.fov * Math.PI / 360), asp = W / Math.max(1, H);
    const dpr = W / Math.max(1, $('fv').clientWidth);
    const proj = (p) => { const z = dot(p, V.f); if (z <= 0.01) return null; return [((dot(p, V.r) / z) / (asp * th) + 1) / 2 * W, (1 - (dot(p, V.u) / z) / th) / 2 * H]; };
    g2.font = (12 * dpr) + 'px Malgun Gothic, sans-serif'; g2.lineWidth = 2 * dpr;
    for (const g of G.pts) {
      const v = [g.E - (ps.E + c.dx), g.N - (ps.N + c.dy), gZ(g, c) - (c.zt + ps.rel)];
      const dist = Math.hypot(v[0], v[1]);
      if (dist > 400) continue;
      const s = proj(mtv(R, v)); if (!s) continue;
      const on = G.active === g.id, r = (on ? 9 : 6) * dpr;
      g2.strokeStyle = on ? '#ff9a1f' : '#ffd166'; g2.fillStyle = '#000a';
      g2.beginPath(); g2.arc(s[0], s[1], r, 0, Math.PI * 2); g2.stroke();
      g2.beginPath(); g2.moveTo(s[0] - r * 1.8, s[1]); g2.lineTo(s[0] + r * 1.8, s[1]); g2.moveTo(s[0], s[1] - r * 1.8); g2.lineTo(s[0], s[1] + r * 1.8); g2.stroke();
      const label = g.name + ' · ' + dist.toFixed(0) + ' m';
      const tw = g2.measureText(label).width;
      g2.fillRect(s[0] + r + 3 * dpr, s[1] - 9 * dpr, tw + 8 * dpr, 17 * dpr);
      g2.fillStyle = on ? '#ffb86b' : '#ffe2a0'; g2.fillText(label, s[0] + r + 7 * dpr, s[1] + 4 * dpr);
    }
    // 이 장면 근처에서 누른 자리 (하늘색 ×)
    g2.strokeStyle = '#22d3ee';
    for (const q of G.pairs) {
      if (q.path !== md.path || Math.abs(q.t - src.t) > 0.6) continue;
      const s = proj(q.p); if (!s) continue;
      const k = 5 * dpr;
      g2.beginPath(); g2.moveTo(s[0] - k, s[1] - k); g2.lineTo(s[0] + k, s[1] + k); g2.moveTo(s[0] + k, s[1] - k); g2.lineTo(s[0] - k, s[1] + k); g2.stroke();
    }
  }
  function vidOf(paths) {                // 만들기(build)와 같은 영상 번호 규칙 → {경로: 번호}
    const out = {}, used = new Set();
    [...paths].sort((a, b) => a.toLowerCase() < b.toLowerCase() ? -1 : 1).forEach((p) => {
      const name = p.split(/[\\/]/).pop().replace(/\.[^.]+$/, ''), parts = name.split('_');
      let v = parts.length >= 3 ? parts[2] : name.slice(-4);
      if (used.has(v)) v = parts.length >= 3 ? parts[1].slice(-6) + '_' + parts[2] : name;
      used.add(v); out[p] = v;
    });
    return out;
  }

  // ------------------------------------------------------------ 지도 (위성 · 비행 경로 · 지금 위치)
  const MAP = { z: 15, cx: 0, cy: 0, imgs: new Map(), fitted: false, key: '' };
  const wpx = (lat, lon, z) => {
    const s = 256 * Math.pow(2, z), r = lat * Math.PI / 180;
    return [(lon + 180) / 360 * s, (1 - Math.log(Math.tan(r) + 1 / Math.cos(r)) / Math.PI) / 2 * s];
  };
  function mapSize() { const e = $('map'); return [e.clientWidth, e.clientHeight]; }
  function mapFit() {
    const pts = [];
    const used = new Set(S.clips.map((c) => c.m));
    S.media.forEach((md, i) => { if ((used.size ? used.has(i) : true) && md.track) md.track.forEach((p) => pts.push(p)); });
    if (!pts.length) return false;
    let la0 = 90, la1 = -90, lo0 = 180, lo1 = -180;
    for (const p of pts) { la0 = Math.min(la0, p[1]); la1 = Math.max(la1, p[1]); lo0 = Math.min(lo0, p[2]); lo1 = Math.max(lo1, p[2]); }
    const [w, h] = mapSize();
    let z = 19;
    for (; z > 6; z--) { const a = wpx(la1, lo0, z), b = wpx(la0, lo1, z); if (b[0] - a[0] < w - 40 && b[1] - a[1] < h - 40) break; }
    const c = wpx((la0 + la1) / 2, (lo0 + lo1) / 2, z);
    MAP.z = z; MAP.cx = c[0]; MAP.cy = c[1]; MAP.fitted = true;
    return true;
  }
  function mapTiles() {
    const box = document.querySelector('#map .tiles');
    if (!MAP.key) { box.textContent = ''; return; }
    const [w, h] = mapSize(), z = MAP.z, x0 = MAP.cx - w / 2, y0 = MAP.cy - h / 2;
    const want = new Set(), n = Math.pow(2, z);
    for (let ty = Math.floor(y0 / 256); ty <= Math.floor((y0 + h) / 256); ty++) {
      for (let tx = Math.floor(x0 / 256); tx <= Math.floor((x0 + w) / 256); tx++) {
        if (ty < 0 || ty >= n) continue;
        const k = z + '/' + tx + '/' + ty;
        want.add(k);
        let im = MAP.imgs.get(k);
        if (!im) {
          im = document.createElement('img');
          im.draggable = false;
          im.onerror = () => { im.style.visibility = 'hidden'; };
          im.src = 'https://api.vworld.kr/req/wmts/1.0.0/' + MAP.key + '/Satellite/' + z + '/' + ty + '/' + ((tx % n) + n) % n + '.jpeg';
          MAP.imgs.set(k, im); box.appendChild(im);
        }
        im.style.left = (tx * 256 - x0) + 'px'; im.style.top = (ty * 256 - y0) + 'px';
      }
    }
    for (const [k, im] of MAP.imgs) if (!want.has(k)) { im.remove(); MAP.imgs.delete(k); }
  }
  function mapDraw() {
    const ov = document.querySelector('#map .ov');
    if (!MAP.fitted && !mapFit()) { ov.innerHTML = ''; mapTiles(); return; }
    mapTiles();
    const [w, h] = mapSize(), x0 = MAP.cx - w / 2, y0 = MAP.cy - h / 2, z = MAP.z;
    const P = (la, lo) => { const p = wpx(la, lo, z); return (p[0] - x0).toFixed(1) + ',' + (p[1] - y0).toFixed(1); };
    const line = (pts, col, wd, dash) => pts.length > 1 ? '<polyline points="' + pts.join(' ') + '" fill="none" stroke="' + col +
      '" stroke-width="' + wd + '" stroke-linejoin="round" stroke-linecap="round"' + (dash ? ' stroke-dasharray="4 4"' : '') + '/>' : '';
    // 좌표 보정을 푼 영상은 경로를 그만큼 옮겨 그림 (동 · 북 m → 위경도)
    const Pm = (md, la, lo) => {
      const c = G.cal[md.path];
      if (c && c.st === 'ok') { la += c.dy / 111320; lo += c.dx / (111320 * Math.cos(la * Math.PI / 180)); }
      return P(la, lo);
    };
    let svg = '';
    const used = new Set(S.clips.map((c) => c.m));
    S.media.forEach((md, i) => {
      if (!md.track || !md.track.length) return;
      svg += line(md.track.map((p) => Pm(md, p[1], p[2])), used.has(i) ? '#ffffff55' : '#ffffff30', 2, !used.has(i));
    });
    S.clips.forEach((c, k) => {
      const md = S.media[c.m];
      if (!md.track || !md.track.length) return;
      const pts = [];
      const A = trackAt(md, c.a), B = trackAt(md, c.b);
      if (A) pts.push(Pm(md, A[0], A[1]));
      for (const p of md.track) if (p[0] > c.a && p[0] < c.b) pts.push(Pm(md, p[1], p[2]));
      if (B) pts.push(Pm(md, B[0], B[1]));
      if (k === S.sel) svg += line(pts, '#ffffff', 6);
      svg += line(pts, md.color, 3.5);
    });
    for (const g of G.pts) {             // GCP ▲
      const [x, y] = P(g.lat, g.lon).split(',').map(Number), on = G.active === g.id, s = on ? 8 : 5.5;
      svg += '<path d="M' + x + ',' + (y - s) + 'L' + (x + s) + ',' + (y + s * 0.8) + 'L' + (x - s) + ',' + (y + s * 0.8) + 'Z" fill="' + (on ? '#ff9a1f' : '#ffd166') + '" stroke="#000" stroke-width="1"/>';
      if (on || MAP.z >= 17) svg += '<text x="' + (x + s + 2) + '" y="' + (y + 4) + '" fill="#fff" stroke="#000" stroke-width="3" paint-order="stroke" font-size="11">' + g.name.replace(/[<&]/g, '') + '</text>';
    }
    const L = locate(S.T);
    if (L) {
      const md = S.media[L.m], q = trackAt(md, L.t);
      if (q) { const [x, y] = Pm(md, q[0], q[1]).split(','); svg += '<circle cx="' + x + '" cy="' + y + '" r="6" fill="#ff4d5e" stroke="#fff" stroke-width="2"/>'; }
    }
    ov.innerHTML = svg;
  }
  function mapPick(mx, my) {           // 지도에서 누른 곳 → 그 장면 (남긴 토막 안이면)
    const [w, h] = mapSize(), x0 = MAP.cx - w / 2, y0 = MAP.cy - h / 2;
    let best = null, bd = 14 * 14;
    S.media.forEach((md, i) => {
      if (!md.track) return;
      for (const p of md.track) {
        const q = wpx(p[1], p[2], MAP.z), dx = q[0] - x0 - mx, dy = q[1] - y0 - my, d = dx * dx + dy * dy;
        if (d < bd) { bd = d; best = { m: i, t: p[0] }; }
      }
    });
    if (!best) return;
    const st = starts();
    const k = S.clips.findIndex((c) => c.m === best.m && best.t >= c.a && best.t <= c.b);
    if (k < 0) { toast(S.clips.some((c) => c.m === best.m) ? '지운 구간' : '타임라인에 없는 영상'); return; }
    pause();
    S.T = st[k] + snap(best.m, best.t) - S.clips[k].a;
    showAt(S.T); placePH(true);
  }
  function mapInit() {
    const el = $('map');
    let down = null;
    el.addEventListener('pointerdown', (e) => { down = { x: e.clientX, y: e.clientY, cx: MAP.cx, cy: MAP.cy, moved: false }; el.setPointerCapture(e.pointerId); });
    el.addEventListener('pointermove', (e) => {
      if (!down) return;
      const dx = e.clientX - down.x, dy = e.clientY - down.y;
      if (!down.moved && dx * dx + dy * dy < 16) return;
      down.moved = true; el.classList.add('drag');
      MAP.cx = down.cx - dx; MAP.cy = down.cy - dy; MAP.fitted = true; mapDraw();
    });
    el.addEventListener('pointerup', (e) => {
      if (!down) return;
      el.classList.remove('drag');
      const r = el.getBoundingClientRect();
      if (!down.moved) {
        const mx = e.clientX - r.left, my = e.clientY - r.top;
        if (G.mapAdd) {                  // 지도에 점: 누른 곳 → GCP
          const [w, h] = mapSize(), s = 256 * Math.pow(2, MAP.z);
          const wx = MAP.cx - w / 2 + mx, wy = MAP.cy - h / 2 + my;
          const lon = wx / s * 360 - 180, lat = Math.atan(Math.sinh(Math.PI * (1 - 2 * wy / s))) * 180 / Math.PI;
          gMapAdd(lat, lon);
        } else mapPick(mx, my);
      }
      down = null;
    });
    el.addEventListener('wheel', (e) => {
      e.preventDefault();
      const nz = Math.max(6, Math.min(19, MAP.z + (e.deltaY < 0 ? 1 : -1)));
      if (nz === MAP.z) return;
      const r = el.getBoundingClientRect(), mx = e.clientX - r.left, my = e.clientY - r.top;
      const [w, h] = mapSize(), f = Math.pow(2, nz - MAP.z);
      const wx = (MAP.cx - w / 2 + mx) * f, wy = (MAP.cy - h / 2 + my) * f;
      MAP.z = nz; MAP.cx = wx - mx + w / 2; MAP.cy = wy - my + h / 2;
      mapDraw();
    }, { passive: false });
    el.addEventListener('dblclick', () => { MAP.fitted = false; mapDraw(); });
  }

  // ------------------------------------------------------------ 타임라인 조작
  function tlX(e) { return e.clientX - $('tlIn').getBoundingClientRect().left; }
  function tlInit() {
    const tl = $('tl');
    let scrub = false;
    const toT = (e) => { S.T = Math.max(0, Math.min(total(), tlX(e) / S.pps)); const L = locate(S.T); if (L) S.T = L.start + snap(L.m, L.t) - S.clips[L.k].a; showAt(S.T); };
    $('ruler').addEventListener('pointerdown', (e) => { pause(); scrub = true; $('ruler').setPointerCapture(e.pointerId); toT(e); });
    $('ruler').addEventListener('pointermove', (e) => { if (scrub) toT(e); });
    $('ruler').addEventListener('pointerup', () => { scrub = false; });
    $('track').addEventListener('pointerdown', (e) => {
      pause();
      const d = e.target.closest('.clip');
      S.sel = d ? +d.dataset.k : -1;
      document.querySelectorAll('#track .clip').forEach((x) => x.classList.toggle('sel', +x.dataset.k === S.sel));
      toT(e); scrub = true; $('track').setPointerCapture(e.pointerId);
    });
    $('track').addEventListener('pointermove', (e) => { if (scrub && e.buttons) toT(e); });
    $('track').addEventListener('pointerup', () => { scrub = false; });
    let raf = 0;
    tl.addEventListener('scroll', () => { cancelAnimationFrame(raf); raf = requestAnimationFrame(renderThumbs); });
    tl.addEventListener('wheel', (e) => {
      if (e.ctrlKey) { e.preventDefault(); setZoom(S.zoomV + (e.deltaY < 0 ? 6 : -6), (e.clientX - tl.getBoundingClientRect().left + tl.scrollLeft) / S.pps); return; }
      if (Math.abs(e.deltaY) > Math.abs(e.deltaX)) { e.preventDefault(); tl.scrollLeft += e.deltaY; }
    }, { passive: false });
    // 끌어 놓기 (가져온 영상 → 타임라인)
    let ins = null;
    const insAt = (e) => {
      const x = tlX(e), st = starts();
      let k = S.clips.length;
      for (let i = 0; i < S.clips.length; i++) if (x < (st[i] + len(S.clips[i]) / 2) * S.pps) { k = i; break; }
      return k;
    };
    tl.addEventListener('dragover', (e) => {
      e.preventDefault(); e.dataTransfer.dropEffect = 'copy';
      $('drop').classList.toggle('hide', S.clips.length > 0);
      const k = insAt(e), st = starts();
      if (!ins) { ins = document.createElement('div'); ins.className = 'ins'; $('tlIn').appendChild(ins); }
      ins.style.left = ((k < S.clips.length ? st[k] : total()) * S.pps) + 'px';
    });
    const endDrag = () => { $('drop').classList.add('hide'); if (ins) { ins.remove(); ins = null; } };
    tl.addEventListener('dragleave', (e) => { if (!tl.contains(e.relatedTarget)) endDrag(); });
    tl.addEventListener('drop', (e) => {
      e.preventDefault();
      const s = e.dataTransfer.getData('text/plain') || '';
      const k = insAt(e);
      endDrag();
      if (s.startsWith('rvm:')) addClip(+s.slice(4), k);
    });
  }

  // ------------------------------------------------------------ 만들기
  function mergeSegs(segs) {
    segs.sort((p, q) => p[0] - q[0]);
    const o = [];
    for (const s of segs) { if (o.length && s[0] <= o[o.length - 1][1] + 1e-3) o[o.length - 1][1] = Math.max(o[o.length - 1][1], s[1]); else o.push(s.slice()); }
    return o;
  }
  async function build() {
    if (!S.clips.length && !S.jpgs.length) { toast('타임라인에 영상을 놓으세요'); return; }
    if (S.media.some((md, i) => S.clips.some((c) => c.m === i) && !md.ready && md.state !== 'fail')) { toast('불러오는 중 · 잠시 뒤'); return; }
    const used = [...new Set(S.clips.map((c) => c.m))];
    const keep = {};
    for (const i of used) {
      const md = S.media[i];
      const segs = mergeSegs(S.clips.filter((c) => c.m === i).map((c) => [c.a, c.b]));
      const whole = segs.length === 1 && segs[0][0] <= 0.05 && segs[0][1] >= md.dur - 0.1;
      if (!whole) keep[md.name] = segs.map((s) => [+s[0].toFixed(3), +s[1].toFixed(3)]);
    }
    const edit = { version: 1, clips: S.clips.map((c) => ({ path: S.media[c.m].path, a: c.a, b: c.b })), interval: Number($('gap').value),
                   gcp: { pts: G.pts, pairs: G.pairs } };
    // 좌표 보정 → 만든 프로젝트의 맞춤 값 (calib.js · 영상 번호는 build 규칙)
    const vids = vidOf(used.map((i) => S.media[i].path)), calib = {};
    for (const i of used) {
      const c = G.cal[S.media[i].path];
      if (c && c.st === 'ok') calib[vids[S.media[i].path]] = { dx: +c.dx.toFixed(4), dy: +c.dy.toFixed(4), dyaw: +c.dyaw.toFixed(4), dpitch: +c.dpitch.toFixed(4), droll: +c.droll.toFixed(4) };
    }
    if (Object.keys(calib).length) {     // 보기 화면이 이 PC 에 따로 기억한 옛 맞춤 값이 새 값을 가리지 않게
      const ttl = $('title').value || String($('out').value).split(/[\\/]/).pop();
      try {
        const k = 'rv_calib:' + ttl, o = JSON.parse(localStorage.getItem(k) || 'null');
        if (o && o.videos) { Object.keys(calib).forEach((v) => { delete o.videos[v]; }); localStorage.setItem(k, JSON.stringify(o)); }
      } catch (e) { /* 없어도 됨 */ }
    }
    const r = await call('start_build', {
      files: used.map((i) => S.media[i].path).concat(S.jpgs), out: $('out').value, interval: Number($('gap').value),
      title: $('title').value, width: Number($('width').value), dem: $('dem').checked, sat: $('sat').checked, keep, edit, calib,
    });
    if (!r || !r.ok) { if (r) toast(r.msg); return; }
    S.job = r.job; setBusy(true); $('bar').style.width = '0%'; poll();
  }
  function setBusy(b) { $('bBuild').disabled = b; $('bStop').disabled = !b; $('bPick').disabled = b; }
  async function poll() {
    if (!S.job) return;
    const j = (await call('job', S.job)) || { state: 'fail', line: '작업 상태를 못 읽음' };
    $('bar').style.width = (j.pct || 0) + '%';
    $('status').textContent = j.line || '';
    $('jobPillText').textContent = Math.round(j.pct || 0) + '% · ' + (j.pill || j.line || '만드는 중').slice(0, 40);
    const log = $('log');
    log.classList.remove('hide');
    log.textContent = (j.log || []).join('\n');
    log.scrollTop = log.scrollHeight;
    if (j.state === 'run') { S.timer = setTimeout(poll, 700); return; }
    setBusy(false);
    $('jobPill').classList.add('hide');
    const out = j.out;
    S.job = null;
    if (j.state === 'done') {
      $('status').textContent = '끝 · 여는 중…';
      const r = await call('open_project', out);
      if (r && !r.ok) toast(r.msg);
    }
  }

  // ------------------------------------------------------------ 열기 · 닫기 · 단축키
  function isOpen() { return !$('ed').classList.contains('hide'); }
  function open() {
    $('ed').classList.remove('hide');
    $('jobPill').classList.add('hide');
    refresh(true);
    if (!S.media.length && !S.job) pick();
  }
  function close() {
    pause();
    $('ed').classList.add('hide');
    if (S.job) $('jobPill').classList.remove('hide');
  }
  function onKey(e) {
    if (!isOpen()) return;
    const tag = (e.target && e.target.tagName) || '';
    if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA') return;
    const k = e.key;
    if (e.ctrlKey && (k === 'z' || k === 'Z')) { e.preventDefault(); if (e.shiftKey) redo(); else undo(); return; }
    if (e.ctrlKey && (k === 'y' || k === 'Y')) { e.preventDefault(); redo(); return; }
    if (e.ctrlKey || e.altKey || e.metaKey) return;
    if (k === ' ') { e.preventDefault(); if (S.playing) pause(); else play(); }
    else if (k === 'ArrowLeft') { e.preventDefault(); stepFrames(e.shiftKey ? -Math.round(fpsOf(0)) : -1); }
    else if (k === 'ArrowRight') { e.preventDefault(); stepFrames(e.shiftKey ? Math.round(fpsOf(0)) : 1); }
    else if (k === 's' || k === 'S') split();
    else if (k === 'q' || k === 'Q') delSide(true);
    else if (k === 'w' || k === 'W') delSide(false);
    else if (k === 'Delete' || k === 'Backspace') { e.preventDefault(); delSel(); }
    else if (k === 'Home') { pause(); S.T = 0; showAt(0); placePH(true); }
    else if (k === 'End') { pause(); S.T = total(); showAt(S.T); placePH(true); }
    else if (k === '+' || k === '=') setZoom(S.zoomV + 8);
    else if (k === '-') setZoom(S.zoomV - 8);
  }

  function init(info) {
    S.info = info || {};
    MAP.key = S.info.vworld || '';
    $('mapNote').classList.toggle('hide', !!MAP.key);
    $('eBack').addEventListener('click', close);
    $('bPick').addEventListener('click', pick);
    $('bBuild').addEventListener('click', build);
    $('bStop').addEventListener('click', async () => { if (S.job) await call('stop_job', S.job); });
    $('bOut').addEventListener('click', async () => { const d = await call('pick_folder'); if (d) { $('out').value = d; S.outTouched = true; } });
    $('out').addEventListener('input', () => { S.outTouched = true; });
    $('gap').addEventListener('input', renderInfo);
    $('bPlay').addEventListener('click', () => (S.playing ? pause() : play()));
    $('bPrevF').addEventListener('click', () => stepFrames(-1));
    $('bNextF').addEventListener('click', () => stepFrames(1));
    document.querySelectorAll('#lensSel button').forEach((b) => b.addEventListener('click', () => { setLens(b.dataset.l); gRender(); }));
    $('gLoad').addEventListener('click', gLoad);
    $('gMap').addEventListener('click', () => {
      G.mapAdd = !G.mapAdd; $('gMap').classList.toggle('on', G.mapAdd);
      if (G.mapAdd) toast('위성지도에서 GCP 자리 누르기 · 다시 누르면 끝');
    });
    $('tUndo').addEventListener('click', undo);
    $('tRedo').addEventListener('click', redo);
    $('tSplit').addEventListener('click', split);
    $('tDelL').addEventListener('click', () => delSide(true));
    $('tDelR').addEventListener('click', () => delSide(false));
    $('tDel').addEventListener('click', delSel);
    $('zoom').addEventListener('input', () => setZoom(Number($('zoom').value)));
    $('zIn').addEventListener('click', () => setZoom(S.zoomV + 10));
    $('zOut').addEventListener('click', () => setZoom(S.zoomV - 10));
    const v = vid();
    v.addEventListener('loadedmetadata', () => { if (S.seekNext != null) { const t = S.seekNext; S.seekNext = null; seek(t); } });
    v.addEventListener('seeked', onSeeked);
    v.addEventListener('error', () => {
      const md = S.media[S.curM];
      if (md && !md.novid) { md.novid = true; toast('미리보기 영상 재생 안 됨 → 썸네일로'); S.curM = -1; showAt(S.T); }
    });
    document.addEventListener('keydown', onKey);
    window.addEventListener('resize', () => { if (isOpen()) { refresh(true); } });
    mapInit();
    tlInit();
    try { fvInit(); } catch (e) { toast('프리 뷰 못 씀 · ' + String(e.message || e).slice(0, 60)); }
    setLens(FV.gl ? 'free' : 'down');
    gRender();
  }

  window.RVEdit = { init, open, close, _S: S, _map: MAP, _G: G, _FV: FV, _api: { addClip, split, delSel, delSide, undo, redo, locate, total, build, showAt, mergeSegs, gSolve, gAddPoints, viewClick, posAt, rotAt, fvView, rot64, vidOf, ensureZt0 } };
})();
