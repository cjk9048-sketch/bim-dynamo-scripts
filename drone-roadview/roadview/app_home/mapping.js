/* 드론 로드뷰 — 3D 매핑 만들기 (JACK 1125: 경사촬영 사진 → 3D 모델 · 정사 · DSM · 지표면)
 * 사진 폴더 → 위성지도에 촬영점 · 바라보는 곳 → 범위 그리기(바라보는 곳이 네모 안인 사진) → 품질 → [만들기] (rv_cli map)
 */
(function () {
  'use strict';
  const $ = (id) => document.getElementById(id);
  const call = (...a) => window.RV.call(...a);
  const toast = (t) => window.RV.toast(t);
  // 사진 한 장 처리 시간 어림 (초) — 1125 실측: 빠르게 80장 266초(3.3) · 보통 394장 약 12초/장(전역 카메라 · 면 한도 반영) · 정밀은 어림
  const SEC = { fast: 4, normal: 12, fine: 35 };
  const S = { info: {}, folder: '', photos: [], sel: new Set(), q: 'normal', area: false, job: null, outTouched: false };
  const MAP = { z: 15, cx: 0, cy: 0, imgs: new Map(), fitted: false, key: '' };
  const wpx = (lat, lon, z) => { const s = 256 * Math.pow(2, z), r = lat * Math.PI / 180; return [(lon + 180) / 360 * s, (1 - Math.log(Math.tan(r) + 1 / Math.cos(r)) / Math.PI) / 2 * s]; };
  const mapEl = () => $('mMap');
  const size = () => [mapEl().clientWidth, mapEl().clientHeight];

  function fit() {
    if (!S.photos.length) return;
    let la0 = 90, la1 = -90, lo0 = 180, lo1 = -180;
    for (const p of S.photos) for (const [la, lo] of [[p.lat, p.lon], [p.look_lat, p.look_lon]]) { la0 = Math.min(la0, la); la1 = Math.max(la1, la); lo0 = Math.min(lo0, lo); lo1 = Math.max(lo1, lo); }
    const [w, h] = size();
    let z = 20; for (; z > 6; z--) { const a = wpx(la1, lo0, z), b = wpx(la0, lo1, z); if (b[0] - a[0] < w - 80 && b[1] - a[1] < h - 80) break; }
    const c = wpx((la0 + la1) / 2, (lo0 + lo1) / 2, z); MAP.z = Math.min(z, 19); const k = Math.pow(2, MAP.z - z);
    MAP.cx = c[0] * k; MAP.cy = c[1] * k; MAP.fitted = true;
  }
  function tiles() {
    const box = mapEl().querySelector('.tiles');
    if (!MAP.key) { box.textContent = ''; return; }
    const [w, h] = size(), z = MAP.z, x0 = MAP.cx - w / 2, y0 = MAP.cy - h / 2, n = Math.pow(2, z), want = new Set();
    for (let ty = Math.floor(y0 / 256); ty <= Math.floor((y0 + h) / 256); ty++) for (let tx = Math.floor(x0 / 256); tx <= Math.floor((x0 + w) / 256); tx++) {
      if (ty < 0 || ty >= n) continue;
      const k = z + '/' + tx + '/' + ty; want.add(k);
      let im = MAP.imgs.get(k);
      if (!im) { im = document.createElement('img'); im.draggable = false; im.onerror = () => { im.style.visibility = 'hidden'; };
        im.src = 'https://api.vworld.kr/req/wmts/1.0.0/' + MAP.key + '/Satellite/' + z + '/' + ty + '/' + (((tx % n) + n) % n) + '.jpeg'; MAP.imgs.set(k, im); box.appendChild(im); }
      im.style.left = (tx * 256 - x0) + 'px'; im.style.top = (ty * 256 - y0) + 'px';
    }
    for (const [k, im] of MAP.imgs) if (!want.has(k)) { im.remove(); MAP.imgs.delete(k); }
  }
  function draw() {
    if (!MAP.fitted) fit();
    tiles();
    const [w, h] = size(), x0 = MAP.cx - w / 2, y0 = MAP.cy - h / 2;
    const P = (la, lo) => { const p = wpx(la, lo, MAP.z); return [(p[0] - x0).toFixed(1), (p[1] - y0).toFixed(1)]; };
    let s = '';
    for (const p of S.photos) {                       // 바라보는 곳 (고른 것만 · 주황 작은 점)
      if (!S.sel.has(p.name)) continue;
      const [x, y] = P(p.look_lat, p.look_lon); s += '<circle cx="' + x + '" cy="' + y + '" r="2" fill="#ffb020" fill-opacity=".75"/>';
    }
    for (const p of S.photos) {                       // 촬영점 (고름 = 파랑 · 안 고름 = 회색 · RTK 아님 = 빨간 테)
      const [x, y] = P(p.lat, p.lon), on = S.sel.has(p.name);
      s += '<circle cx="' + x + '" cy="' + y + '" r="' + (on ? 3.5 : 2.5) + '" fill="' + (on ? '#2f81f7' : '#9aa0a8') + '" stroke="' + (p.rtk === 50 ? '#0008' : '#ff4d5e') + '" stroke-width="1"/>';
    }
    mapEl().querySelector('.ov').innerHTML = s;
  }
  function est() {
    const n = S.sel.size;
    $('mSel').textContent = S.photos.length ? '고름 ' + n + ' / ' + S.photos.length + '장' : '';
    $('mEst').textContent = n ? '사진 ' + n + '장 · 약 ' + Math.max(1, Math.round(n * SEC[S.q] / 60)) + '분 (' + { fast: '빠르게', normal: '보통', fine: '정밀' }[S.q] + ')' : '';
  }
  async function pick() {
    const r = await call('pick_mapping_folder');
    if (!r) return;
    if (!r.ok) { toast(r.msg); return; }
    if (r.photos.length < 3) { toast('위치 있는 매핑 사진 부족 (' + r.photos.length + '장)'); return; }
    S.folder = r.folder; S.photos = r.photos; S.sel = new Set(r.photos.map((p) => p.name));
    const rtk = r.photos.filter((p) => p.rtk === 50).length;
    $('mInfo').textContent = r.photos.length + '장 · RTK 고정 ' + rtk + '장 · ' + r.folder;
    if (!S.outTouched) $('mOut').value = r.out;
    if (!$('mTitle').value) $('mTitle').value = r.folder.split(/[\\/]/).filter(Boolean).pop();
    MAP.fitted = false; draw(); est();
  }
  // 지도: 끌기 = 옮기기 · 휠 = 확대 · «범위 그리기» 켜면 끌기 = 네모 (바라보는 곳이 안에 든 사진을 고름 · Shift = 더하기)
  function mapInit() {
    const el = mapEl(); let dn = null;
    el.addEventListener('pointerdown', (e) => { dn = { x: e.clientX, y: e.clientY, cx: MAP.cx, cy: MAP.cy, area: S.area, add: e.shiftKey }; el.setPointerCapture(e.pointerId); });
    el.addEventListener('pointermove', (e) => {
      if (!dn) return; const dx = e.clientX - dn.x, dy = e.clientY - dn.y, r = el.getBoundingClientRect();
      if (dn.area) { const b = $('mBox'); b.classList.remove('hide'); b.style.left = (Math.min(e.clientX, dn.x) - r.left) + 'px'; b.style.top = (Math.min(e.clientY, dn.y) - r.top) + 'px'; b.style.width = Math.abs(dx) + 'px'; b.style.height = Math.abs(dy) + 'px'; return; }
      MAP.cx = dn.cx - dx; MAP.cy = dn.cy - dy; MAP.fitted = true; draw();
    });
    el.addEventListener('pointerup', (e) => {
      if (dn && dn.area) {
        $('mBox').classList.add('hide');
        const r = el.getBoundingClientRect(), [w, h] = size(), x0 = MAP.cx - w / 2, y0 = MAP.cy - h / 2;
        const ax = Math.min(e.clientX, dn.x) - r.left, bx = Math.max(e.clientX, dn.x) - r.left, ay = Math.min(e.clientY, dn.y) - r.top, by = Math.max(e.clientY, dn.y) - r.top;
        if (bx - ax > 4 && by - ay > 4) {
          if (!dn.add) S.sel.clear();
          for (const p of S.photos) { const q = wpx(p.look_lat, p.look_lon, MAP.z), x = q[0] - x0, y = q[1] - y0; if (x >= ax && x <= bx && y >= ay && y <= by) S.sel.add(p.name); }
          draw(); est();
        }
      }
      dn = null;
    });
    el.addEventListener('wheel', (e) => {
      e.preventDefault(); const nz = Math.max(6, Math.min(20, MAP.z + (e.deltaY < 0 ? 1 : -1))); if (nz === MAP.z) return;
      const r = el.getBoundingClientRect(), mx = e.clientX - r.left, my = e.clientY - r.top, [w, h] = size(), f = Math.pow(2, nz - MAP.z);
      const wx = (MAP.cx - w / 2 + mx) * f, wy = (MAP.cy - h / 2 + my) * f; MAP.z = nz; MAP.cx = wx - mx + w / 2; MAP.cy = wy - my + h / 2; MAP.fitted = true; draw();
    }, { passive: false });
    window.addEventListener('resize', () => { if (!$('mp').classList.contains('hide')) draw(); });
  }
  async function build() {
    if (S.sel.size < 3) { toast('사진 3장 이상 고르기'); return; }
    const r = await call('start_mapping', { folder: S.folder, out: $('mOut').value, names: [...S.sel], quality: S.q, title: $('mTitle').value, crs: $('mCrs').value });
    if (!r || !r.ok) { if (r) toast(r.msg); return; }
    S.job = r.job; $('mBuild').disabled = true; $('mStop').disabled = false; $('mBar').style.width = '0%'; poll();
  }
  async function poll() {
    if (!S.job) return;
    const j = (await call('job', S.job)) || { state: 'fail', line: '작업 상태를 못 읽음' };
    $('mBar').style.width = (j.pct || 0) + '%'; $('mStatus').textContent = j.line || '';
    $('jobPillText').textContent = Math.round(j.pct || 0) + '% · ' + (j.pill || j.line || '매핑 중').slice(0, 40);
    const lg = $('mLog'); lg.classList.remove('hide'); lg.textContent = (j.log || []).join('\n'); lg.scrollTop = lg.scrollHeight;
    if (j.state === 'run') { setTimeout(poll, 1000); return; }
    $('mBuild').disabled = false; $('mStop').disabled = true; $('jobPill').classList.add('hide');
    const out = j.out; S.job = null;
    if (j.state === 'done') { $('mStatus').textContent = '끝 · 여는 중…'; const q = await call('open_project', out); if (q && !q.ok) toast(q.msg); }
  }
  function open() { $('mp').classList.remove('hide'); $('jobPill').classList.add('hide'); draw(); est(); if (!S.photos.length && !S.job) pick(); }
  function close() { $('mp').classList.add('hide'); if (S.job) $('jobPill').classList.remove('hide'); }
  function init(info) {
    S.info = info || {}; MAP.key = S.info.vworld || '';
    $('mBack').addEventListener('click', close);
    $('mPick').addEventListener('click', pick);
    $('mBuild').addEventListener('click', build);
    $('mStop').addEventListener('click', async () => { if (S.job) await call('stop_job', S.job); });
    $('mAll').addEventListener('click', () => { S.sel = new Set(S.photos.map((p) => p.name)); draw(); est(); });
    $('mNone').addEventListener('click', () => { S.sel.clear(); draw(); est(); });
    $('mArea').addEventListener('click', () => { S.area = !S.area; $('mArea').classList.toggle('on', S.area); mapEl().classList.toggle('area', S.area); if (S.area) toast('지도에서 끌어 범위 · Shift = 더하기'); });
    $('mOutB').addEventListener('click', async () => { const d = await call('pick_folder'); if (d) { $('mOut').value = d; S.outTouched = true; } });
    $('mOut').addEventListener('input', () => { S.outTouched = true; });
    document.querySelectorAll('#mQ button').forEach((b) => b.addEventListener('click', () => { S.q = b.dataset.q; document.querySelectorAll('#mQ button').forEach((x) => x.classList.toggle('on', x === b)); est(); }));
    mapInit();
  }
  window.RVMap = { init, open, close, _S: S, _test: { setPhotos: (folder, photos, out) => { S.folder = folder; S.photos = photos; S.sel = new Set(photos.map((p) => p.name)); $('mOut').value = out || ''; MAP.fitted = false; draw(); est(); }, build, draw } };
})();
