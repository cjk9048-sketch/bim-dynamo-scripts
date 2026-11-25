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
      if (changed) { tryRestore(); refresh(true); if (S.curM < 0) showAt(S.T); }
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
    $('lens').className = 'lens ' + l;
    document.querySelectorAll('#lensSel button').forEach((b) => b.classList.toggle('on', b.dataset.l === l));
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
    let svg = '';
    const used = new Set(S.clips.map((c) => c.m));
    S.media.forEach((md, i) => {
      if (!md.track || !md.track.length) return;
      svg += line(md.track.map((p) => P(p[1], p[2])), used.has(i) ? '#ffffff55' : '#ffffff30', 2, !used.has(i));
    });
    S.clips.forEach((c, k) => {
      const md = S.media[c.m];
      if (!md.track || !md.track.length) return;
      const pts = [];
      const A = trackAt(md, c.a), B = trackAt(md, c.b);
      if (A) pts.push(P(A[0], A[1]));
      for (const p of md.track) if (p[0] > c.a && p[0] < c.b) pts.push(P(p[1], p[2]));
      if (B) pts.push(P(B[0], B[1]));
      if (k === S.sel) svg += line(pts, '#ffffff', 6);
      svg += line(pts, md.color, 3.5);
    });
    const L = locate(S.T);
    if (L) {
      const q = trackAt(S.media[L.m], L.t);
      if (q) { const [x, y] = P(q[0], q[1]).split(','); svg += '<circle cx="' + x + '" cy="' + y + '" r="6" fill="#ff4d5e" stroke="#fff" stroke-width="2"/>'; }
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
      if (!down.moved) mapPick(e.clientX - r.left, e.clientY - r.top);
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
    const edit = { version: 1, clips: S.clips.map((c) => ({ path: S.media[c.m].path, a: c.a, b: c.b })), interval: Number($('gap').value) };
    const r = await call('start_build', {
      files: used.map((i) => S.media[i].path).concat(S.jpgs), out: $('out').value, interval: Number($('gap').value),
      title: $('title').value, width: Number($('width').value), dem: $('dem').checked, sat: $('sat').checked, keep, edit,
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
    document.querySelectorAll('#lensSel button').forEach((b) => b.addEventListener('click', () => setLens(b.dataset.l)));
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
    setLens('down');
  }

  window.RVEdit = { init, open, close, _S: S, _map: MAP, _api: { addClip, split, delSel, delSide, undo, redo, locate, total, build, showAt, mergeSegs } };
})();
