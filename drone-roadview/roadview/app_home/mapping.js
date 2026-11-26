/* 드론 로드뷰 — 3D 매핑 만들기 (JACK 1125: 경사촬영 사진 → 3D 모델 · 정사 · DSM · 지표면)
 * 사진 폴더 (또는 사진 고르기) → 위성지도에 촬영점 · 바라보는 곳 → 관심 지역 (범위 그리기) → [재구성 시작] (rv_cli map)
 * 1126 DJI Terra 꼴 (JACK «Terra 메뉴처럼»): 오른쪽 «설정» 칸 = 데이터 세트 · 처리 (템플릿 · 전처리 · 결과물 칸) · 애플리케이션 · 팁
 *   칸 머리줄 = ▾ 접기 (따로) + 오른쪽 켜고 끄기 (켠 칸만 결과물 · 끈 칸은 흐리게 · 누를 수 없음 · 값은 그대로)
 *   아래 줄 = 장치 성능 · 실행 기록 · 출력 좌표 참조계 · 접힘 · 고른 값 = localStorage (rv.map.ui) · 템플릿 = 앱 설정 폴더 (app.template_*)
 */
(function () {
  'use strict';
  const $ = (id) => document.getElementById(id);
  const $$ = (sel) => Array.from(document.querySelectorAll(sel));
  const call = (...a) => window.RV.call(...a);
  const toast = (t) => window.RV.toast(t);
  // 사진 한 장 처리 시간 어림 (초) — 단계별 · 1125 실측 (보통 394장 · 보통 80장 3D만 / 전부 · 정밀 80장 · 빠르게 80장)
  //   base = 특징점 · 맞추기 · 카메라 · 왜곡 펴기 (많을수록 늘어 394장 기준) · pts / mesh = 점구름 (측량용 / 3D 만)
  //   mP / mM = 3D 면 (측량용 점구름 / 3D 만 점구름) · tex = 질감 · out = 정사 · DSM · DTM (3D 만이면 거의 0)
  const SEC = {
    fast: { base: 1.5, pts: 1.6, mesh: 1.6, mP: 0.4, mM: 0.4, tex: 1.0, out: 0.1 },
    normal: { base: 2.8, pts: 7.1, mesh: 1.7, mP: 3.4, mM: 1.0, tex: 3.3, out: 0.5 },
    fine: { base: 3.0, pts: 29.7, mesh: 7.1, mP: 15.6, mM: 3.6, tex: 6.6, out: 1.5 },
  };
  const UIK = 'rv.map.ui', OUTK = 'rv.map.outputs';
  const OUT_NAME = { mesh: '3D', gs: '가우시안', ortho: '정사', dsm: 'DSM', dtm: 'DEM', las: 'LAS', contour: '등고선' };
  // 세부 설정 기본값 = 1.0.17 까지 만들던 그대로 (고도 주석 간격 · 지면 분류만 Terra 처럼 값이 있음)
  const DEF = { outs: ['mesh'], q: 'normal', crs: 'EPSG:5186', ct: 1, feat: 'normal', orthoRes: 'auto', orthoFill: true, meshRatio: 100,
    meshFmt: [], gsQ: 'normal', gsDom: false, lasDensity: 100, lasClassify: true, demCell: 0.5, ctBase: 0, ctLabel: 100, ctClean: false, tinStep: 100 };
  const LIM = { meshRatio: [5, 100], ctBase: [-1000, 9000], ctLabel: [0, 1000], tinStep: [25, 5000], ct: [0.5, 50] };
  const VKEYS = ['feat', 'orthoRes', 'orthoFill', 'meshRatio', 'meshFmt', 'gsQ', 'gsDom', 'lasDensity', 'lasClassify', 'demCell', 'ctBase', 'ctLabel', 'ctClean', 'tinStep'];
  const S = { info: {}, folder: '', photos: [], sel: new Set(), q: 'normal', area: false, job: null, outTouched: false,
    outs: new Set(DEF.outs), v: {}, fold: {}, sys: null };
  const clamp = (k, x) => { const L = LIM[k], n = Number(x); if (x === '' || x == null || !isFinite(n)) return DEF[k]; /* 빈 칸 = 기본값 (0 → 하한 아님 · 1126 검토) */ const r = L ? Math.min(L[1], Math.max(L[0], n)) : n; return k === 'ctLabel' && r > 0 && r < 5 ? 5 : r; };   /* 주석 간격 0 = 없음 · 아니면 5 m 이상 */
  // 함께 처리 (cluster.js): 점구름의 9할(깊이 지도)을 내 PC + 도우미가 나눔 · 합치기 1할은 내 PC
  const workers = () => (window.RVCluster ? window.RVCluster.workers() : 1);
  const perPhoto = () => {
    const k = SEC[S.q], pts = ['dsm', 'dtm', 'las', 'contour'].some((o) => S.outs.has(o))   /* 정사는 원본 사진 색 → 가벼운 점구름 (1126) */, mesh = S.outs.has('mesh');
    const w = workers(), share = w > 1 ? 0.1 + 0.9 / w : 1;
    return k.base + (pts ? k.pts : k.mesh) * share + (mesh ? (pts ? k.mP : k.mM) * (S.v.meshRatio || 100) / 100 + k.tex : 0) + (pts ? k.out : 0.05);
  };
  const gsOk = () => !!(S.sys && S.sys.gs_ok);
  const gsWhy = () => (S.sys ? (S.sys.gs_why || '가우시안 스플래팅을 쓸 수 없음') : '장치 확인 중');

  // ------------------------------------------------------------ 설정 값 ↔ 칸
  function values() {
    const v = { outs: [...S.outs], q: S.q, crs: $('mCrs').value, ct: clamp('ct', $('mCt').value) };
    for (const k of VKEYS) v[k] = Array.isArray(S.v[k]) ? [...S.v[k]] : S.v[k];
    return v;
  }
  function apply(vals) {
    const v = Object.assign({}, DEF, vals || {});
    S.outs = new Set((Array.isArray(v.outs) ? v.outs : []).filter((o) => o in OUT_NAME));
    S.q = ['fast', 'normal', 'fine'].includes(v.q) ? v.q : 'normal';
    if ([...$('mCrs').options].some((o) => o.value === v.crs)) $('mCrs').value = v.crs;
    $('mCt').value = clamp('ct', v.ct);
    for (const k of VKEYS) {
      const d = DEF[k];
      S.v[k] = Array.isArray(d) ? (Array.isArray(v[k]) ? v[k].filter((f) => f === 'ply' || f === 'obj') : [])
        : typeof d === 'boolean' ? !!v[k] : typeof d === 'number' ? clamp(k, v[k]) : String(v[k]);
    }
    paint();
  }
  function paint() {
    $$('#mp .sw[data-o]').forEach((b) => {
      const o = b.dataset.o, on = S.outs.has(o), card = b.closest('.tcard');
      b.classList.toggle('on', on);
      card.classList.toggle('off', !on);
      card.querySelectorAll('.tcb select, .tcb input, .tcb button').forEach((e) => { e.disabled = !on; });   // 끈 칸 = 누를 수 없음 (값은 그대로)
    });
    $$('#mp [data-k]').forEach((e) => {
      const k = e.dataset.k;
      if (e.classList.contains('sw')) e.classList.toggle('on', !!S.v[k]);
      else if (String(e.value) !== String(S.v[k])) e.value = S.v[k];
    });
    $$('#mFmt button[data-f]').forEach((b) => b.classList.toggle('on', S.v.meshFmt.includes(b.dataset.f)));
    $$('#mQ button').forEach((b) => b.classList.toggle('on', b.dataset.q === S.q));
    const o = $('mCrs').selectedOptions[0]; $('mCrsBar').textContent = o ? o.textContent : $('mCrs').value;
    const gc = $('mGsCard'), na = !!S.sys && !gsOk();               // 가우시안: 엔진 · 그래픽카드가 안 되면 흐리게 + 이유 한 줄
    gc.classList.toggle('na', na);
    gc.querySelector('.sw[data-o=gs]').title = na ? gsWhy() : '가우시안 스플래팅 만들기';
    $('mGsWhy').textContent = na ? gsWhy() : ''; $('mGsWhy').classList.toggle('hide', !na);
    est();
  }
  function saveUi() { localStorage.setItem(UIK, JSON.stringify({ fold: S.fold, vals: values() })); }
  function changed() { paint(); saveUi(); }
  function loadUi() {
    let u = null;
    try { u = JSON.parse(localStorage.getItem(UIK) || 'null'); } catch (e) { u = null; }
    if (u && typeof u === 'object') { S.fold = u.fold || {}; apply(u.vals); }
    else apply({ outs: (localStorage.getItem(OUTK) || 'mesh').split(',').filter(Boolean) });     // 1.0.17 까지 고른 결과물
    $$('#mp [data-sec]').forEach((b) => b.classList.toggle('fold', !!S.fold[b.dataset.sec]));
    $('mp').classList.toggle('sidecol', !!S.fold.__side);
  }

  const MAP = { z: 15, cx: 0, cy: 0, imgs: new Map(), fitted: false, key: '' };
  const wpx = (lat, lon, z) => { const s = 256 * Math.pow(2, z), r = lat * Math.PI / 180; return [(lon + 180) / 360 * s, (1 - Math.log(Math.tan(r) + 1 / Math.cos(r)) / Math.PI) / 2 * s]; };
  const mapEl = () => $('mMap');
  const size = () => [mapEl().clientWidth, mapEl().clientHeight];

  function fit() {
    if (!S.photos.length) return;
    let la0 = 90, la1 = -90, lo0 = 180, lo1 = -180;
    for (const p of S.photos) for (const [la, lo] of [[p.lat, p.lon], [p.look_lat, p.look_lon]]) { la0 = Math.min(la0, la); la1 = Math.max(la1, la); lo0 = Math.min(lo0, lo); lo1 = Math.max(lo1, lo); }
    const [w, h] = size();
    if (!w || !h) return;                             // 화면이 숨어 있으면 (크기 0) 보일 때 맞춤
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
    if (S.range) {                                    // 그린 범위 (주황 점선) — 이 안을 보는 사진만 · 결과물도 이 안만
      const pts = S.range.map((q) => P(q[0], q[1]).join(',')).join(' ');
      s = '<polygon points="' + pts + '" fill="#ffb020" fill-opacity=".10" stroke="#ffb020" stroke-width="2" stroke-dasharray="6 4"/>' + s;
    }
    mapEl().querySelector('.ov').innerHTML = s;
  }
  // 예상 시간 · 켠 결과물 수 (칸을 켜고 끄면 바로)
  function est() {
    const n = S.sel.size;
    $('mSel').textContent = S.photos.length ? '고름 ' + n + ' / ' + S.photos.length + '장' + (S.range ? ' · 범위 안만' : '') : '';
    $$('#mp .roi').forEach((e) => { e.textContent = S.range ? '그린 범위' : '전체'; });
    const outs = [...S.outs], m = Math.max(1, Math.round(n * perPhoto() / 60));
    const head = '결과물 ' + outs.length + '개' + (outs.length ? ' (' + outs.map((o) => OUT_NAME[o]).join(' · ') + ')' : '');
    $('mEst').textContent = head + (n && outs.length ? ' · 사진 ' + n + '장 · 약 ' + (m >= 90 ? (m / 60).toFixed(1) + '시간' : m + '분') + ' (' +
      { fast: '단순', normal: '보통', fine: '정밀' }[S.q] + (workers() > 1 ? ' · PC ' + workers() + '대' : '') + (S.outs.has('gs') ? ' · 가우시안 시간 빼고' : '') + ')' : '');
  }
  function take(r) {
    if (!r) return;
    if (!r.ok) { toast(r.msg); return; }
    if (r.photos.length < 3) { toast('위치 있는 매핑 사진 부족 (' + r.photos.length + '장)'); return; }
    S.folder = r.folder; S.photos = r.photos; S.sel = new Set(r.photos.map((p) => p.name)); S.range = null;
    const rtk = r.photos.filter((p) => p.rtk === 50).length;
    $('mInfo').textContent = (r.picked != null ? '고른 사진 ' : '') + r.photos.length + '장 · RTK 고정 ' + rtk + '장 · ' + r.folder +
      (r.other_dir ? ' · 다른 폴더 ' + r.other_dir + '장 뺌' : '');
    if (!S.outTouched) $('mOut').value = r.out;
    if (!$('mTitle').value) $('mTitle').value = r.folder.split(/[\\/]/).filter(Boolean).pop();
    MAP.fitted = false; draw(); est();
  }
  const pick = async () => take(await call('pick_mapping_folder'));
  const pickFiles = async () => take(await call('pick_mapping_files'));
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
          // 그린 범위를 남김 (위경도 네 구석 · Shift = 앞 범위와 합친 네모) → 지도에 보이고 · 만들기 때 결과물도 이 안만 (JACK 1126 «범위가 안 나와»)
          const ll = (sx, sy) => { const s2 = 256 * Math.pow(2, MAP.z), gx = x0 + sx, gy = y0 + sy;
            return [Math.atan(Math.sinh(Math.PI * (1 - 2 * gy / s2))) * 180 / Math.PI, gx / s2 * 360 - 180]; };
          let la0 = ll(ax, by)[0], la1 = ll(ax, ay)[0], lo0 = ll(ax, ay)[1], lo1 = ll(bx, ay)[1];
          if (dn.add && !S.range) {
            // Shift = 넓히기인데 앞 범위가 없음(= 전체) → 범위 없음 그대로 (작은 네모로 줄어들지 않게 · 1126 검토)
          } else {
            if (dn.add) { la0 = Math.min(la0, ...S.range.map((q) => q[0])); la1 = Math.max(la1, ...S.range.map((q) => q[0])); lo0 = Math.min(lo0, ...S.range.map((q) => q[1])); lo1 = Math.max(lo1, ...S.range.map((q) => q[1])); }
            S.range = [[la1, lo0], [la1, lo1], [la0, lo1], [la0, lo0]];
          }
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
    if (S.outs.has('gs') && !gsOk()) { toast('가우시안 스플래팅 못 함 · ' + gsWhy()); return; }
    if (!S.outs.size) { toast('만들 결과물 칸 켜기'); return; }
    const v = S.v;
    const r = await call('start_mapping', { folder: S.folder, out: $('mOut').value, names: [...S.sel], quality: S.q, title: $('mTitle').value, crs: $('mCrs').value,
      outputs: [...S.outs], contour: clamp('ct', $('mCt').value), range: S.range,
      feat: v.feat, ortho_res: v.orthoRes, ortho_fill: v.orthoFill, mesh_ratio: v.meshRatio, mesh_formats: v.meshFmt, gs_quality: v.gsQ, gs_dom: v.gsDom,
      las_density: v.lasDensity, las_classify: v.lasClassify, dem_cell: v.demCell, contour_base: v.ctBase, contour_label: v.ctLabel,
      contour_clean: v.ctClean, tin_step: v.tinStep / 100 });
    if (!r || !r.ok) { if (r) toast(r.msg); return; }
    S.job = r.job; $('mBuild').disabled = true; $('mStop').disabled = false; $('mBar').style.width = '0%'; logShow(true); lockUi(true); poll();
  }
  async function poll() {
    if (!S.job) return;
    const j = (await call('job', S.job)) || { state: 'fail', line: '작업 상태를 못 읽음' };
    $('mBar').style.width = (j.pct || 0) + '%'; $('mStatus').textContent = j.line || '';
    $('jobPillText').textContent = Math.round(j.pct || 0) + '% · ' + (j.pill || j.line || '매핑 중').slice(0, 40);
    const lg = $('mLog'); lg.textContent = (j.log || []).join('\n'); lg.scrollTop = lg.scrollHeight;
    if (j.state === 'run') { setTimeout(poll, 1000); return; }
    $('mBuild').disabled = false; $('mStop').disabled = true; $('jobPill').classList.add('hide');
    const out = j.out; S.job = null; lockUi(false);
    if (j.state === 'done') { $('mStatus').textContent = '끝 · 여는 중…'; const q = await call('open_project', out); if (q && !q.ok) toast(q.msg); }
  }
  // 재구성 중 = 사진 · 범위 · 설정 잠금 (JACK 1126 «재구성 시작 누르고 나서도 범위가 그려져» · 바꿔도 이미 도는 처리엔 안 들어감)
  const LOCK_IDS = ['mAll', 'mArea', 'mNone', 'mPick', 'mPickFiles', 'mOutB', 'mOut', 'mTpl', 'mTplSave', 'mTitle', 'mRen'];
  function lockUi(on) {
    $('mp').classList.toggle('running', on);
    if (on && S.area) { S.area = false; $('mArea').classList.remove('on'); mapEl().classList.remove('area'); $('mBox').classList.add('hide'); }
    LOCK_IDS.forEach((id) => { const e = $(id); if (e) e.disabled = on; });
    $$('#mp .tsbody select, #mp .tsbody input, #mp .tsbody button').forEach((e) => { if (!LOCK_IDS.includes(e.id)) e.disabled = on; });
    if (!on) paint();                                                    // 끈 칸은 다시 잠긴 채로
  }
  // i 표시 = 누르면 설명 칸 (JACK 1126 «i 를 눌러도 아무것도 안 나와» · 전에는 마우스를 올려 두어야만 떴음)
  function tipInit() {
    const pop = document.createElement('div'); pop.className = 'qpop hide'; document.body.appendChild(pop);
    let cur = null;
    const hide = () => { pop.classList.add('hide'); cur = null; };
    $$('#mp .qi').forEach((q) => { q.dataset.tip = q.title; q.removeAttribute('title'); });
    document.addEventListener('click', (e) => {
      const q = e.target.closest && e.target.closest('#mp .qi');
      if (!q) { if (!pop.contains(e.target)) hide(); return; }
      e.preventDefault(); e.stopPropagation();
      if (cur === q) { hide(); return; }
      cur = q; pop.textContent = q.dataset.tip || ''; pop.classList.remove('hide');
      const r = q.getBoundingClientRect(), w = pop.offsetWidth, h = pop.offsetHeight;
      pop.style.left = Math.max(8, Math.min(window.innerWidth - w - 8, r.left + r.width / 2 - w / 2)) + 'px';
      pop.style.top = (r.bottom + 6 + h > window.innerHeight ? r.top - h - 6 : r.bottom + 6) + 'px';
    }, true);
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape') hide(); });
    const body = document.querySelector('#mp .tsbody'); if (body) body.addEventListener('scroll', hide);
    window.addEventListener('resize', hide);
  }
  // ------------------------------------------------------------ 아래 줄 · 윗줄 작은 단추
  function logShow(on) { $('mLogBox').classList.toggle('hide', !on); $('mLogBtn').classList.toggle('on', on); }
  const logToggle = () => logShow($('mLogBox').classList.contains('hide'));
  function sideToggle() {
    const c = !$('mp').classList.contains('sidecol');
    $('mp').classList.toggle('sidecol', c); S.fold.__side = c; saveUi();
    window.dispatchEvent(new Event('resize'));
  }
  async function sysinfo() {
    const d = await call('sysinfo');
    if (!d) return;
    S.sys = d;
    const good = d.engines && d.gpu_nvidia;
    $('mDevDot').className = 'dot ' + (good ? 'ok' : 'warn');
    $('mDev').title = good ? '장치 준비됨' : (!d.gpu_nvidia ? 'NVIDIA 그래픽카드 없음 → CPU 로 (느림)' : '엔진 = 처음 재구성 때 내려받음');
    if (S.outs.has('gs') && !gsOk()) S.outs.delete('gs');       // 이 PC 에서 못 하는 결과물은 꺼 둠
    paint();
  }
  function devPop() {
    const p = $('mDevPop');
    if (!p.classList.contains('hide')) { p.classList.add('hide'); return; }
    const d = S.sys;
    const L = !d ? ['장치 확인 중'] : [
      '<b>CPU</b>' + esc(d.cpu || '?') + ' · ' + d.cores + '코어',
      '<b>메모리</b>' + d.ram_gb + ' GB' + (d.ram_free_gb != null ? ' (남은 ' + d.ram_free_gb + ' GB)' : ''),
      '<b>그래픽카드</b>' + (d.gpu_nvidia ? esc(d.gpu) + ' · ' + d.vram_gb + ' GB' : 'NVIDIA 없음 → CPU 로 (느림)'),
      '<b>엔진</b>' + (d.engines ? 'COLMAP · OpenMVS 준비됨' : '처음 재구성 때 내려받음'),
      '<b>가우시안</b>' + (d.gs_ok ? '됨' : '안 됨') + (d.gs_why ? ' · ' + esc(d.gs_why) : ''),
    ];
    p.innerHTML = L.join('<br>');
    p.classList.remove('hide');
  }
  const esc = (t) => String(t).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  // ------------------------------------------------------------ 템플릿 (일반 = 기본값 · 저장한 것 = 앱 설정 폴더)
  async function tplList(sel) {
    const L = (await call('templates_list')) || [];
    const s = $('mTpl'); s.textContent = '';
    s.add(new Option('일반', ''));
    for (const n of L) s.add(new Option(n, n));
    s.value = sel && L.includes(sel) ? sel : '';
  }
  async function tplPick() {
    const n = $('mTpl').value;
    if (!n) { apply(DEF); saveUi(); toast('일반 (기본값)'); return; }
    const r = await call('template_load', n);
    if (!r || !r.ok) { toast(r ? r.msg : '템플릿 못 읽음'); return; }
    apply(r.values); saveUi(); toast('템플릿 «' + n + '»');
  }
  async function tplSave() {
    const n = $('mTplName').value.trim();
    if (!n) { toast('템플릿 이름 넣기'); $('mTplName').focus(); return; }
    const r = await call('template_save', n, values());
    if (!r || !r.ok) { if (r) toast(r.msg); return; }
    await tplList(r.name);
    $('mTplRow').classList.add('hide'); $('mTplSave').classList.remove('hide');
    toast('템플릿 저장 · ' + r.name);
  }

  function open() { $('mp').classList.remove('hide'); $('jobPill').classList.add('hide'); draw(); est(); }   // 사진은 오른쪽 «사진 폴더 선택 · 사진 선택» 으로 (열 때 저절로 창 띄우지 않음 · JACK 1126)
  function close() { $('mp').classList.add('hide'); if (S.job) $('jobPill').classList.remove('hide'); }
  function init(info) {
    S.info = info || {}; MAP.key = S.info.vworld || '';
    $('mBack').addEventListener('click', close);
    $('mPick').addEventListener('click', pick);
    $('mPickFiles').addEventListener('click', pickFiles);
    tipInit();
    $('mBuild').addEventListener('click', build);
    $('mStop').addEventListener('click', async () => { if (S.job) await call('stop_job', S.job); });
    $('mAll').addEventListener('click', () => { S.sel = new Set(S.photos.map((p) => p.name)); S.range = null; draw(); est(); });
    $('mNone').addEventListener('click', () => { S.sel.clear(); S.range = null; draw(); est(); });
    $('mArea').title = '지도에서 끌어 네모 → 그 안을 찍은 사진만 쓰고 결과물도 그 안만 · Shift = 넓히기';
    $('mArea').addEventListener('click', () => { S.area = !S.area; $('mArea').classList.toggle('on', S.area); mapEl().classList.toggle('area', S.area); if (S.area) toast('지도에서 끌어 네모 → 그 안만 만듦 · Shift = 넓히기'); });
    $$('#mp [data-act=area]').forEach((b) => b.addEventListener('click', () => $('mArea').click()));   // 관심 지역 › = 범위 그리기
    $('mOutB').addEventListener('click', async () => { const d = await call('pick_folder'); if (d) { $('mOut').value = d; S.outTouched = true; } });
    $('mOut').addEventListener('input', () => { S.outTouched = true; });
    $('mRen').addEventListener('click', () => { $('mTitle').focus(); $('mTitle').select(); });
    $('mTitle').addEventListener('keydown', (e) => { if (e.key === 'Enter') $('mTitle').blur(); });
    // 칸 접고 펴기 (머리줄 · 켜고 끄기 단추는 따로)
    $$('#mp .tsh, #mp .tch').forEach((h) => h.addEventListener('click', (e) => {
      if (e.target.closest('.sw')) return;
      const box = h.parentElement; box.classList.toggle('fold'); S.fold[box.dataset.sec] = box.classList.contains('fold'); saveUi();
    }));
    // 결과물 칸 켜고 끄기 (켠 칸만 만듦)
    $$('#mp .sw[data-o]').forEach((b) => b.addEventListener('click', (e) => {
      e.stopPropagation();
      const o = b.dataset.o;
      if (o === 'gs' && !S.outs.has(o) && !gsOk()) { toast(gsWhy()); return; }
      if (S.outs.has(o)) S.outs.delete(o); else S.outs.add(o);
      changed();
    }));
    // 칸 안 세부 설정
    $$('#mp [data-k]').forEach((e) => {
      const k = e.dataset.k;
      if (e.classList.contains('sw')) { e.addEventListener('click', () => { S.v[k] = !S.v[k]; changed(); }); return; }
      e.addEventListener('change', () => {
        const d = DEF[k];
        S.v[k] = typeof d === 'number' ? clamp(k, e.value) : e.value;
        e.value = S.v[k]; changed();
      });
    });
    $$('#mFmt button[data-f]').forEach((b) => b.addEventListener('click', () => {
      const f = b.dataset.f, L = S.v.meshFmt;
      S.v.meshFmt = L.includes(f) ? L.filter((x) => x !== f) : L.concat([f]); changed();
    }));
    $$('#mQ button').forEach((b) => b.addEventListener('click', () => { S.q = b.dataset.q; changed(); }));
    $('mCrs').addEventListener('change', changed);
    $('mCt').addEventListener('change', () => { $('mCt').value = clamp('ct', $('mCt').value); changed(); });
    // 템플릿
    $('mTpl').addEventListener('change', tplPick);
    $('mTplSave').addEventListener('click', () => { $('mTplSave').classList.add('hide'); $('mTplRow').classList.remove('hide'); $('mTplName').value = $('mTpl').value; $('mTplName').focus(); });
    $('mTplOk').addEventListener('click', tplSave);
    $('mTplName').addEventListener('keydown', (e) => { if (e.key === 'Enter') tplSave(); });
    $('mTplX').addEventListener('click', () => { $('mTplRow').classList.add('hide'); $('mTplSave').classList.remove('hide'); });
    // 윗줄 · 아래 줄
    $('mLogBtn').addEventListener('click', logToggle);
    $('mHdrLog').addEventListener('click', logToggle);
    $('mFold').addEventListener('click', sideToggle);
    $('mHdrSet').addEventListener('click', sideToggle);
    $('mHelp').addEventListener('click', () => {
      if ($('mp').classList.contains('sidecol')) sideToggle();
      $('mTips').classList.remove('fold'); S.fold.tips = false; saveUi(); $('mTips').scrollIntoView({ block: 'nearest' });
    });
    $('mDev').addEventListener('click', devPop);
    loadUi();
    tplList();
    sysinfo();
    mapInit();
  }
  window.RVMap = { init, open, close, _S: S, _est: est, _test: { lockUi: (on) => lockUi(on), setPhotos: (folder, photos, out) => { S.folder = folder; S.photos = photos; S.sel = new Set(photos.map((p) => p.name)); $('mOut').value = out || ''; MAP.fitted = false; draw(); est(); },
    take, build, draw, values, apply } };
})();
