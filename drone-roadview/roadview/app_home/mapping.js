/* 드론 로드뷰 — 3D 매핑 만들기 (JACK 1125: 경사촬영 사진 → 3D 모델 · 정사 · DSM · 지표면)
 * 1127 «단계 카드형» (JACK «매핑 창 배치 · 이름을 우리 꼴로 → 이름 · 위치 · 분위기 대폭»):
 *   위 = 단계 띠 ① 사진 ─ ② 작업 영역 ─ ③ 받을 결과 ─ ④ 만들기 (끝낸 단계 체크 · 누르면 그 자리 깜박) · 오른쪽 끝 = 이름 · 실행 기록 · 카드 줄 접기 · 도움말
 *   가운데 = 위성지도 (찍은 자리 → 바라본 곳 · 작업 영역) · 지도 위 도구 (모든 사진 · 작업 영역 지정 · 영역 지우기) · 사진 없으면 가운데 큰 단추
 *   아래 = 받을 결과 그림 카드 (누르면 켜고 끔 · ⋯ = 세부 옵션 떠 있는 칸 · 끈 카드 옵션은 흐리게 · 누를 수 없음 · 값은 그대로)
 *          + 처리 수준 ▾ (빠른 확인 · 표준 · 정밀) · 저장 위치 · 병렬 처리 참여 · 작업 설정 (저장한 설정 · 특징점 밀도 · 좌표계) · 이 설정 저장
 *   맨 아래 = 상태 줄 (예상 시간 · 사진 수 · 처리 수준 · 장치 · 좌표계 · [정지] [3D 매핑])
 *   만드는 동안 = 카드 줄 자리에 단계 시간표 (지금 단계 강조 · 끝난 단계 체크 · 걸린 시간) + 남은 시간 · 끝 예상 · 지난 시간 크게 · 실행 기록은 접어 둠
 *   고른 값 · 카드 줄 접힘 = localStorage (rv.map.ui) · 저장한 설정 = 앱 설정 폴더 (app.template_*) · 화면 안 이름만 바꿈 (안쪽 열쇠 data-o · data-q · data-k 그대로)
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
  // 화면 이름 (JACK 1127 · 상용 프로그램 낱말 피함) — 안쪽 열쇠는 그대로
  const OUT_NAME = { mesh: '3D 모델', gs: '가우시안', ortho: '정사 지도', dsm: '지표 높이', dtm: '맨땅 높이', las: '점구름', contour: '등고선' };
  const QNAME = { fast: '빠른 확인', normal: '표준', fine: '정밀' };
  const QSUB = { fast: '사진 1/4 · 가장 빠름', normal: '사진 1/2 · 대부분', fine: '원본 크기 · 3~4배 느림' };
  const GS_SEC = { low: 75, normal: 280, high: 1130, ultra: 4000 };   // 가우시안 학습 초 (gsplat.QUALITY_SEC · 80장 RTX 5070 Ti 실측 · 사진 수와 거의 무관)
  // 시간 글: «1시간 54분» · «8분» (전에는 «1.9시간» 처럼 소수 · JACK 1126 «1.9시간인지 7분인지 모르겠어»)
  const fmtDur = (sec) => { const m = Math.max(1, Math.round(sec / 60)); return m < 60 ? m + '분' : Math.floor(m / 60) + '시간' + (m % 60 ? ' ' + (m % 60) + '분' : ''); };
  const fmtShort = (sec) => (sec < 60 ? Math.max(1, Math.round(sec)) + '초' : sec < 3600 ? Math.floor(sec / 60) + '분 ' + String(Math.round(sec % 60)).padStart(2, '0') + '초' : fmtDur(sec));
  const hhmm = (t) => { const d = new Date(t); return String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0'); };
  // 상태 줄 예상 시간 (글 차례 = «예상 시간» → 큰 숫자 → «사진 …» · check_home_cluster.py 가 이 차례로 읽음)
  const clock = (k, big, sub, cls) => { const e = $('mEst'); e.className = 'mest ' + (cls || ''); e.innerHTML = '<span class="tk">' + k + '</span><b class="tbig">' + big + '</b><span class="tsm">' + sub + '</span>'; };
  // 세부 설정 기본값 = 1.0.17 까지 만들던 그대로
  const DEF = { outs: ['mesh'], q: 'normal', crs: 'EPSG:5186', ct: 1, feat: 'normal', orthoRes: 'auto', orthoFill: true, meshRatio: 100,
    meshFmt: [], gsQ: 'normal', gsDom: false, lasDensity: 100, lasClassify: true, demCell: 0.5, ctBase: 0, ctLabel: 100, ctClean: false, tinStep: 100 };
  const LIM = { meshRatio: [5, 100], ctBase: [-1000, 9000], ctLabel: [0, 1000], tinStep: [25, 5000], ct: [0.5, 50] };
  const VKEYS = ['feat', 'orthoRes', 'orthoFill', 'meshRatio', 'meshFmt', 'gsQ', 'gsDom', 'lasDensity', 'lasClassify', 'demCell', 'ctBase', 'ctLabel', 'ctClean', 'tinStep'];
  const S = { info: {}, folder: '', photos: [], sel: new Set(), q: 'normal', area: false, job: null, outTouched: false,
    outs: new Set(DEF.outs), v: {}, fold: {}, sys: null, run: null, pop: null };
  const clamp = (k, x) => { const L = LIM[k], n = Number(x); if (x === '' || x == null || !isFinite(n)) return DEF[k]; /* 빈 칸 = 기본값 (0 → 하한 아님 · 1126 검토) */ const r = L ? Math.min(L[1], Math.max(L[0], n)) : n; return k === 'ctLabel' && r > 0 && r < 5 ? 5 : r; };   /* 주석 간격 0 = 없음 · 아니면 5 m 이상 */
  // 함께 처리 (cluster.js): 점구름의 9할(깊이 지도)을 내 PC + 도우미가 나눔 · 합치기 1할은 내 PC
  const workers = () => (window.RVCluster ? window.RVCluster.workers() : 1);
  const perPhoto = () => {
    const k = SEC[S.q], pts = ['dsm', 'dtm', 'las', 'contour'].some((o) => S.outs.has(o))   /* 정사는 원본 사진 색 → 가벼운 점구름 (1126) */, mesh = S.outs.has('mesh');
    const w = workers(), share = w > 1 ? 0.1 + 0.9 / w : 1;
    return k.base + (pts ? k.pts : k.mesh) * share + (mesh ? (pts ? k.mP : k.mM) * (S.v.meshRatio || 100) / 100 + k.tex : 0) + (pts ? k.out : 0.05);
  };
  const gsOk = () => !!(S.sys && S.sys.gs_ok);
  const gsWhy = () => (S.sys ? (S.sys.gs_why || '사진 같은 3D(가우시안)를 쓸 수 없음') : '장치 확인 중');
  const esc = (t) => String(t).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

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
      b.classList.toggle('on', on); b.setAttribute('aria-checked', on ? 'true' : 'false');
      card.classList.toggle('off', !on);
      card.querySelectorAll('.tcb select, .tcb input, .tcb button').forEach((e) => { e.disabled = !on || !!S.job; });   // 끈 카드 = 옵션 누를 수 없음 (값은 그대로) · 만드는 중도 잠금
      const st = card.querySelector('.mpst');
      if (st) { st.textContent = on ? '켜짐 · 이 결과를 만듦' : '꺼짐 · 카드를 눌러 켜야 바뀜'; st.classList.toggle('off', !on); }
    });
    $$('#mp [data-k]').forEach((e) => {
      const k = e.dataset.k;
      if (e.classList.contains('sw')) e.classList.toggle('on', !!S.v[k]);
      else if (String(e.value) !== String(S.v[k])) e.value = S.v[k];
    });
    $$('#mFmt button[data-f]').forEach((b) => b.classList.toggle('on', S.v.meshFmt.includes(b.dataset.f)));
    $$('#mQ button').forEach((b) => b.classList.toggle('on', b.dataset.q === S.q));
    $('mQName').textContent = QNAME[S.q]; $('mQSub').textContent = QSUB[S.q];
    const o = $('mCrs').selectedOptions[0]; $('mCrsBar').textContent = o ? o.textContent : $('mCrs').value;
    const gc = $('mGsCard'), na = !!S.sys && !gsOk();               // 가우시안: 엔진 · 그래픽카드가 안 되면 흐리게 + 이유 한 줄
    gc.classList.toggle('na', na);
    gc.querySelector('.sw[data-o=gs]').title = na ? gsWhy() : '사진 같은 3D 받기';
    $('mGsWhy').textContent = na ? gsWhy() : ''; $('mGsWhy').classList.toggle('hide', !na);
    const outs = [...S.outs];
    $('mOutsSum').textContent = outs.length ? outs.length + '개 켬 · ' + outs.map((x) => OUT_NAME[x]).join(' · ') + (S.fold.__cards ? ' · 처리 수준 ' + QNAME[S.q] : '') : '카드를 눌러 켜기 · 켠 것만 만듦';
    est();
  }
  function saveUi() { try { localStorage.setItem(UIK, JSON.stringify({ fold: S.fold, vals: values() })); } catch (e) { /* 저장소 없음 */ } }
  function changed() { paint(); saveUi(); }
  function loadUi() {
    let u = null;
    try { u = JSON.parse(localStorage.getItem(UIK) || 'null'); } catch (e) { u = null; }
    if (u && typeof u === 'object') { S.fold = u.fold || {}; apply(u.vals); }
    else { let o = 'mesh'; try { o = localStorage.getItem(OUTK) || 'mesh'; } catch (e) { /* 없음 */ } apply({ outs: o.split(',').filter(Boolean) }); }     // 1.0.17 까지 고른 결과물
    $('mp').classList.toggle('cardsMin', !!S.fold.__cards);
    $('mHdrSet').classList.toggle('on', !!S.fold.__cards);
  }

  // ------------------------------------------------------------ 단계 띠 (① 사진 · ② 작업 영역 · ③ 받을 결과 · ④ 만들기)
  function steps() {
    const n = S.sel.size, has = S.photos.length > 0, outs = S.outs.size, run = !!S.job;
    const st = {
      photo: { done: has, sub: has ? S.photos.length + '장' : '폴더 · 파일' },
      area: { done: has && n >= 3, sub: !has ? '지도에서' : S.range ? '지정 영역 · ' + n + '장' : n === S.photos.length ? '모든 사진' : '고름 ' + n + '장' },
      outs: { done: outs > 0, sub: outs ? outs + '개' : '카드 켜기' },
      go: { done: false, sub: run ? '만드는 중' : '' },
    };
    if (!run && has && n >= 3 && outs) { const sec = n * perPhoto() + (S.outs.has('gs') ? GS_SEC[S.v.gsQ] || 0 : 0); st.go.sub = '약 ' + fmtDur(sec); }
    let cur = run ? 'go' : (['photo', 'area', 'outs', 'go'].find((k) => !st[k].done) || 'go');
    $$('#mSteps .mstep').forEach((b) => {
      const k = b.dataset.step;
      b.classList.toggle('done', st[k].done); b.classList.toggle('cur', k === cur); b.classList.toggle('run', k === 'go' && run);
      b.querySelector('em').textContent = st[k].sub;
      b.setAttribute('aria-current', k === cur ? 'step' : 'false');
    });
    $$('#mSteps .mlink').forEach((l) => l.classList.toggle('done', !!st[l.dataset.after].done));
  }
  function flash(el) { if (!el) return; el.classList.remove('mflash'); void el.offsetWidth; el.classList.add('mflash'); setTimeout(() => el.classList.remove('mflash'), 1200); }
  function stepGo(k) {
    popClose();
    if (k === 'photo') { flash(S.photos.length ? $('mPhotoBox') : $('mPick')); if (!S.photos.length) $('mPick').focus(); }
    else if (k === 'area') { if (!S.photos.length) { toast('사진 먼저'); flash($('mPick')); return; } flash($('mTools')); }
    else if (k === 'outs') { if (S.job) return; if (S.fold.__cards) cardsToggle(); const c = document.querySelector('#mCards .tcard:not(.off)') || document.querySelector('#mCards .tcard'); if (c) c.scrollIntoView({ block: 'nearest', inline: 'nearest' }); flash($('mCards')); }
    else if (k === 'go') { flash(S.job ? $('mRun') : $('mBuild')); if (!$('mBuild').disabled) $('mBuild').focus(); }
  }

  // ------------------------------------------------------------ 떠 있는 칸 (카드 세부 옵션 · 처리 수준 · 작업 설정)
  function popClose() {
    if (!S.pop) return;
    S.pop.el.classList.remove('open'); if (S.pop.btn) { S.pop.btn.classList.remove('on'); S.pop.btn.setAttribute('aria-expanded', 'false'); }
    S.pop = null;
  }
  // 칸 자리: 기준(카드 · 단추) 위쪽 (아래 줄이라 위로 띄움) · 화면 밖으로 안 나가게
  function popOpen(el, anchor, btn) {
    if (S.pop && S.pop.el === el) { popClose(); return; }
    popClose();
    el.classList.add('open'); S.pop = { el, btn };
    if (btn) { btn.classList.add('on'); btn.setAttribute('aria-expanded', 'true'); }
    const r = anchor.getBoundingClientRect(), w = el.offsetWidth, h = el.offsetHeight, W = window.innerWidth, H = window.innerHeight;
    let x = Math.min(W - w - 8, Math.max(8, r.left + r.width / 2 - w / 2)), y = r.top - h - 10;
    if (y < 8) y = Math.min(H - h - 8, r.bottom + 10);
    el.style.left = Math.round(x) + 'px'; el.style.top = Math.round(Math.max(8, y)) + 'px';
  }
  function popInit() {
    $$('#mCards .cmore').forEach((b) => b.addEventListener('click', (e) => {
      e.stopPropagation();
      const card = b.closest('.tcard');
      popOpen(card.querySelector('.mpop'), card, b);
    }));
    $('mQBtn').addEventListener('click', (e) => { e.stopPropagation(); popOpen($('mQPop'), $('mQBtn'), $('mQBtn')); });
    $('mSetBtn').addEventListener('click', (e) => { e.stopPropagation(); popOpen($('mSetPop'), $('mSetBtn'), $('mSetBtn')); });
    // 바깥 누르면 닫힘 (칸 안 · 여는 단추 · i 설명 칸은 그대로)
    document.addEventListener('pointerdown', (e) => {
      if (!S.pop) return;
      const t = e.target;
      if (S.pop.el.contains(t) || (S.pop.btn && S.pop.btn.contains(t)) || (t.closest && t.closest('.qpop'))) return;
      popClose();
    }, true);
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape') { popClose(); tipsShow(false); } });
    window.addEventListener('resize', popClose);
    $('mCards').addEventListener('scroll', popClose);
    // 좁은 창 = 카드 줄 가로로 넘침 → 마우스 휠로도 옆으로 (노트북 1366 px)
    $('mCards').addEventListener('wheel', (e) => {
      const c = $('mCards');
      if (c.scrollWidth <= c.clientWidth + 1 || Math.abs(e.deltaX) > Math.abs(e.deltaY)) return;
      e.preventDefault(); c.scrollLeft += e.deltaY;
    }, { passive: false });
  }
  function tipsShow(on) { $('mTips').classList.toggle('hide', !on); $('mHelp').classList.toggle('on', on); }

  // ------------------------------------------------------------ 위성지도
  const MAP = { z: 15, cx: 0, cy: 0, imgs: new Map(), fitted: false, key: '' };
  const TILE_MAX = 19, ZOOM_MAX = 21;                                  // 위성 그림 단계 한도 · 확대 한도 (19 넘으면 그림을 키움)
  const wpx = (lat, lon, z) => { const s = 256 * Math.pow(2, z), r = lat * Math.PI / 180; return [(lon + 180) / 360 * s, (1 - Math.log(Math.tan(r) + 1 / Math.cos(r)) / Math.PI) / 2 * s]; };
  const mapEl = () => $('mMap');
  const size = () => [mapEl().clientWidth, mapEl().clientHeight];

  function fit() {
    if (!S.photos.length) {                         // 사진 없음 = 우리나라 전체 (큰 단추 뒤 바탕 · 사진이 오면 사진에 맞춤)
      if (!MAP.home) { const c = wpx(36.2, 127.8, 7); MAP.z = 7; MAP.cx = c[0]; MAP.cy = c[1]; MAP.home = true; }
      return;
    }
    let la0 = 90, la1 = -90, lo0 = 180, lo1 = -180;
    for (const p of S.photos) for (const [la, lo] of [[p.lat, p.lon], [p.look_lat, p.look_lon]]) { la0 = Math.min(la0, la); la1 = Math.max(la1, la); lo0 = Math.min(lo0, lo); lo1 = Math.max(lo1, lo); }
    const [w, h] = size();
    if (!w || !h) return;                             // 화면이 숨어 있으면 (크기 0) 보일 때 맞춤
    let z = 20; for (; z > 6; z--) { const a = wpx(la1, lo0, z), b = wpx(la0, lo1, z); if (b[0] - a[0] < w - 80 && b[1] - a[1] < h - 80) break; }
    const c = wpx((la0 + la1) / 2, (lo0 + lo1) / 2, z); MAP.z = Math.min(z, 19); const k = Math.pow(2, MAP.z - z);
    MAP.cx = c[0] * k; MAP.cy = c[1] * k; MAP.fitted = true; MAP.home = false;
  }
  function tiles() {
    const box = mapEl().querySelector('.tiles');
    if (!MAP.key) { box.textContent = ''; return; }
    // 브이월드 위성 = 19단계까지 · 더 확대하면 19단계 그림을 키움 (전에는 20단계 그림이 없어 검게 나왔음 · JACK 1126)
    const [w, h] = size(), z = Math.min(MAP.z, TILE_MAX), T = 256 * Math.pow(2, MAP.z - z), x0 = MAP.cx - w / 2, y0 = MAP.cy - h / 2, n = Math.pow(2, z), want = new Set();
    for (let ty = Math.floor(y0 / T); ty <= Math.floor((y0 + h) / T); ty++) for (let tx = Math.floor(x0 / T); tx <= Math.floor((x0 + w) / T); tx++) {
      if (ty < 0 || ty >= n) continue;
      const k = z + '/' + tx + '/' + ty; want.add(k);
      let im = MAP.imgs.get(k);
      if (!im) { im = document.createElement('img'); im.draggable = false; im.onerror = () => { im.style.visibility = 'hidden'; };
        im.src = 'https://api.vworld.kr/req/wmts/1.0.0/' + MAP.key + '/Satellite/' + z + '/' + ty + '/' + (((tx % n) + n) % n) + '.jpeg'; MAP.imgs.set(k, im); box.appendChild(im); }
      im.style.left = (tx * T - x0) + 'px'; im.style.top = (ty * T - y0) + 'px'; im.style.width = im.style.height = T + 'px';
    }
    for (const [k, im] of MAP.imgs) if (!want.has(k)) { im.remove(); MAP.imgs.delete(k); }
  }
  function draw() {
    $('mp').classList.toggle('hasph', S.photos.length > 0);       // 사진 칸 꼴 (없음 = 가운데 큰 단추 · 있음 = 왼쪽 아래 작은 칸)
    if (!MAP.fitted) fit();
    tiles();
    const [w, h] = size(), x0 = MAP.cx - w / 2, y0 = MAP.cy - h / 2;
    const P = (la, lo) => { const p = wpx(la, lo, MAP.z); return [(p[0] - x0).toFixed(1), (p[1] - y0).toFixed(1)]; };
    let s = '';
    // 찍은 자리 ● → 바라본 곳 ◦ 를 선으로 이음 (어느 사진이 어디를 찍었는지 한눈에 · JACK 1126 «파란점 노란점 구려»)
    let off = '', ray = '', shot = '';
    for (const p of S.photos) {
      const [x, y] = P(p.lat, p.lon), nm = esc(p.name), on = S.sel.has(p.name);
      if (!on) { off += '<circle cx="' + x + '" cy="' + y + '" r="2.6" class="dOff"><title>' + nm + ' · 안 고름</title></circle>'; continue; }
      const [lx, ly] = P(p.look_lat, p.look_lon);
      ray += '<line x1="' + x + '" y1="' + y + '" x2="' + lx + '" y2="' + ly + '" class="dRay"/><circle cx="' + lx + '" cy="' + ly + '" r="2.4" class="dLook"/>';
      shot += '<circle cx="' + x + '" cy="' + y + '" r="4.3" class="dShot' + (p.rtk === 50 ? '' : ' noRtk') + '"><title>' + nm + (p.rtk === 50 ? '' : ' · RTK 아님') + '</title></circle>';
    }
    s += off + ray + shot;
    const lg = $('mLegend'); if (lg) lg.classList.toggle('hide', !S.photos.length);
    if (lg) lg.querySelector('.lgNoRtk').classList.toggle('hide', !S.photos.some((p) => p.rtk !== 50));
    if (S.range) {                                    // 작업 영역 (주황 점선) — 이 안을 보는 사진만 · 결과물도 이 안만
      const pts = S.range.map((q) => P(q[0], q[1]).join(',')).join(' ');
      s = '<polygon points="' + pts + '" fill="#ffb020" fill-opacity=".10" stroke="#ffb020" stroke-width="2" stroke-dasharray="6 4"/>' + s;
    }
    mapEl().querySelector('.ov').innerHTML = s;
  }
  // 예상 시간 · 켠 결과물 수 (카드를 켜고 끄면 바로)
  function est() {
    const n = S.sel.size;
    $('mSel').textContent = S.photos.length ? '고름 ' + n + ' / ' + S.photos.length + '장' + (S.range ? ' · 영역 안만' : '') : '';
    $$('#mp .roi').forEach((e) => { e.textContent = S.range ? '지정 영역' : '모든 사진'; });
    steps();
    if (S.job) return;                                                  // 만드는 중 = poll 이 단계 시간표 · 남은 시간을 그림 (예상 시간과 섞이지 않게)
    const outs = [...S.outs], sec = n * perPhoto() + (S.outs.has('gs') ? GS_SEC[S.v.gsQ] || 0 : 0);
    const head = '결과물 ' + outs.length + '개' + (outs.length ? ' (' + outs.map((o) => OUT_NAME[o]).join(' · ') + ')' : '');
    const qn = '처리 수준 ' + QNAME[S.q] + (workers() > 1 ? ' · PC ' + workers() + '대' : '');
    if (!n) clock('예상 시간', '–', '사진을 넣으면 나옴 · ' + qn + '<br>' + esc(head), 'idle');
    else if (!outs.length) clock('예상 시간', '–', '사진 ' + n + '장 · ' + qn + '<br>받을 결과 카드 켜기', 'idle');
    else clock('예상 시간', fmtDur(sec), '사진 ' + n + '장 · ' + qn + ' · ' + esc(head) + '<br>지금 누르면 <b>' + hhmm(Date.now() + sec * 1000) + '</b> 쯤 끝');
  }
  function take(r) {
    if (!r) return;
    if (!r.ok) { toast(r.msg); return; }
    if (r.photos.length < 3) { toast('위치 있는 매핑 사진 부족 (' + r.photos.length + '장)'); return; }
    S.folder = r.folder; S.photos = r.photos; S.sel = new Set(r.photos.map((p) => p.name)); S.range = null;
    const rtk = r.photos.filter((p) => p.rtk === 50).length;
    $('mInfo').textContent = (r.picked != null ? '고른 사진 ' : '') + r.photos.length + '장 · RTK 고정 ' + rtk + '장 · ' + r.folder +
      (r.other_dir ? ' · 다른 폴더 ' + r.other_dir + '장 뺌' : '');
    $('mInfo').title = r.folder;
    if (!S.outTouched) $('mOut').value = r.out;
    $('mOut').title = $('mOut').value;
    if (!$('mTitle').value) $('mTitle').value = r.folder.split(/[\\/]/).filter(Boolean).pop();
    MAP.fitted = false; draw(); est();
  }
  const pick = async () => take(await call('pick_mapping_folder'));
  const pickFiles = async () => take(await call('pick_mapping_files'));
  // 지도: 끌기 = 옮기기 · 휠 = 확대 · «작업 영역 지정» 켜면 끌기 = 네모 (바라보는 곳이 안에 든 사진을 고름 · Shift = 더하기)
  function mapInit() {
    const el = mapEl(); let dn = null;
    el.addEventListener('pointerdown', (e) => { dn = { x: e.clientX, y: e.clientY, cx: MAP.cx, cy: MAP.cy, area: S.area, add: e.shiftKey }; el.setPointerCapture(e.pointerId); });
    el.addEventListener('pointermove', (e) => {
      if (!dn) return; const dx = e.clientX - dn.x, dy = e.clientY - dn.y, r = el.getBoundingClientRect();
      if (dn.area) { const b = $('mBox'); b.classList.remove('hide'); b.style.left = (Math.min(e.clientX, dn.x) - r.left) + 'px'; b.style.top = (Math.min(e.clientY, dn.y) - r.top) + 'px'; b.style.width = Math.abs(dx) + 'px'; b.style.height = Math.abs(dy) + 'px'; return; }
      MAP.cx = dn.cx - dx; MAP.cy = dn.cy - dy; if (S.photos.length) MAP.fitted = true; draw();
    });
    el.addEventListener('pointerup', (e) => {
      if (dn && dn.area) {
        $('mBox').classList.add('hide');
        const r = el.getBoundingClientRect(), [w, h] = size(), x0 = MAP.cx - w / 2, y0 = MAP.cy - h / 2;
        const ax = Math.min(e.clientX, dn.x) - r.left, bx = Math.max(e.clientX, dn.x) - r.left, ay = Math.min(e.clientY, dn.y) - r.top, by = Math.max(e.clientY, dn.y) - r.top;
        if (bx - ax > 4 && by - ay > 4) {
          if (!dn.add) S.sel.clear();
          for (const p of S.photos) { const q = wpx(p.look_lat, p.look_lon, MAP.z), x = q[0] - x0, y = q[1] - y0; if (x >= ax && x <= bx && y >= ay && y <= by) S.sel.add(p.name); }
          // 작업 영역을 남김 (위경도 네 구석 · Shift = 앞 영역과 합친 네모) → 지도에 보이고 · 만들기 때 결과물도 이 안만 (JACK 1126 «범위가 안 나와»)
          const ll = (sx, sy) => { const s2 = 256 * Math.pow(2, MAP.z), gx = x0 + sx, gy = y0 + sy;
            return [Math.atan(Math.sinh(Math.PI * (1 - 2 * gy / s2))) * 180 / Math.PI, gx / s2 * 360 - 180]; };
          let la0 = ll(ax, by)[0], la1 = ll(ax, ay)[0], lo0 = ll(ax, ay)[1], lo1 = ll(bx, ay)[1];
          if (dn.add && !S.range) {
            // Shift = 넓히기인데 앞 영역이 없음(= 모든 사진) → 영역 없음 그대로 (작은 네모로 줄어들지 않게 · 1126 검토)
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
      e.preventDefault(); const nz = Math.max(6, Math.min(ZOOM_MAX, MAP.z + (e.deltaY < 0 ? 1 : -1))); if (nz === MAP.z) return;
      const r = el.getBoundingClientRect(), mx = e.clientX - r.left, my = e.clientY - r.top, [w, h] = size(), f = Math.pow(2, nz - MAP.z);
      const wx = (MAP.cx - w / 2 + mx) * f, wy = (MAP.cy - h / 2 + my) * f; MAP.z = nz; MAP.cx = wx - mx + w / 2; MAP.cy = wy - my + h / 2; if (S.photos.length) MAP.fitted = true; draw();
    }, { passive: false });
    window.addEventListener('resize', () => { if (!$('mp').classList.contains('hide')) draw(); });
  }
  function areaMode(on) { S.area = on; $('mArea').classList.toggle('on', on); mapEl().classList.toggle('area', on); if (!on) $('mBox').classList.add('hide'); }

  // ------------------------------------------------------------ 만들기 · 단계 시간표
  // 단계 = 엔진 진행 글 (mapping.STAGE_LABEL · @진행) 의 앞머리로 맞춤 · 결과물마다 안 하는 단계는 뺌
  const RUN_ST = [
    { k: 'align', name: '사진 맞추기', m: ['특징점', '맞추기', '카메라 풀기', '왜곡 펴기'] },
    { k: 'cloud', name: '점구름', m: ['모양 계산', '측량', '점구름'] },   // «측량 점구름 · 함께 처리» 도
    { k: 'mesh', name: '면 만들기', m: ['3D 면'], need: 'mesh' },
    { k: 'tex', name: '질감 입히기', m: ['질감'], need: 'mesh' },
    { k: 'out', name: '정사 · 지형', m: ['결과물'] },
    { k: 'gs', name: '사진 같은 3D', m: ['가우시안', '엔진 내려받기', '엔진 풀기'], need: 'gs' },
    { k: 'end', name: '끝', m: ['끝'] },
  ];
  function runStart() {
    const outs = S.outs, pts = ['ortho', 'dsm', 'dtm', 'las', 'contour'].some((o) => outs.has(o));
    const plan = RUN_ST.filter((s) => !s.need || outs.has(s.need)).map((s) => Object.assign({}, s, { name: s.k === 'out' && !pts ? '결과물 정리' : s.name }));
    S.run = { plan, idx: 0, ts: [Date.now()], te: [], sub: '준비' };
    $('mRunSum').textContent = '사진 ' + S.sel.size + '장 · 처리 수준 ' + QNAME[S.q] + ' · ' + [...outs].map((o) => OUT_NAME[o]).join(' · ');
    $('mLeft').textContent = '재는 중'; $('mEta').textContent = '–'; $('mGone').textContent = '–';
    timeline();
  }
  function stageOf(label) {
    const L = String(label || '').trim();
    if (!L || !S.run) return -1;
    return S.run.plan.findIndex((s) => s.m.some((m) => L === m || L.indexOf(m) === 0));
  }
  function timeline() {
    const R = S.run; if (!R) return;
    const now = Date.now();
    $('mTime').innerHTML = R.plan.map((s, i) => {
      const cls = i < R.idx || (s.k === 'end' && i === R.idx) ? 'done' : i === R.idx ? 'cur' : '';
      let em = '';
      if (i < R.idx) em = R.ts[i] && R.te[i] ? fmtShort((R.te[i] - R.ts[i]) / 1000) : '바로 끝';   // 지난번 것 다시 씀 · 아주 짧은 단계
      else if (i === R.idx && s.k !== 'end') em = esc(R.sub || '준비') + ' · ' + fmtShort((now - (R.ts[i] || now)) / 1000) + '째';
      return '<li class="ts ' + cls + '" data-k="' + s.k + '"><i class="tn"></i><b>' + s.name + '</b><em>' + em + '</em></li>';
    }).join('');
  }
  async function build() {
    popClose();
    if (S.sel.size < 3) { toast('사진 3장 이상 고르기'); stepGo(S.photos.length ? 'area' : 'photo'); return; }
    if (S.outs.has('gs') && !gsOk()) { toast('사진 같은 3D 못 함 · ' + gsWhy()); return; }
    if (!S.outs.size) { toast('받을 결과 카드 켜기'); stepGo('outs'); return; }
    const v = S.v;
    $('mBuild').disabled = true;                                        // 시작 요청 기다리는 동안 두 번 누르기 막음
    const r = await call('start_mapping', { folder: S.folder, out: $('mOut').value, names: [...S.sel], quality: S.q, title: $('mTitle').value, crs: $('mCrs').value,
      outputs: [...S.outs], contour: clamp('ct', $('mCt').value), range: S.range,
      feat: v.feat, ortho_res: v.orthoRes, ortho_fill: v.orthoFill, mesh_ratio: v.meshRatio, mesh_formats: v.meshFmt, gs_quality: v.gsQ, gs_dom: v.gsDom,
      las_density: v.lasDensity, las_classify: v.lasClassify, dem_cell: v.demCell, contour_base: v.ctBase, contour_label: v.ctLabel,
      contour_clean: v.ctClean, tin_step: v.tinStep / 100 });
    if (!r || !r.ok) { $('mBuild').disabled = false; if (r) toast(r.msg); return; }
    S.job = r.job; $('mBuild').disabled = true; $('mStop').disabled = false; $('mBar').style.width = '0%'; $('mStatus').textContent = '';
    runStart(); logShow(false); lockUi(true); poll();                     // 실행 기록은 접어 둠 (단계 시간표가 먼저 · JACK 1127)
  }
  async function poll() {
    if (!S.job) return;
    const j0 = await call('job', S.job);
    if (!j0 && (S.miss = (S.miss || 0) + 1) < 5) { setTimeout(poll, 1500); return; }   // 한두 번 못 읽어도 작업은 뒤에서 도는 중 (1127 검토)
    S.miss = 0;
    const j = j0 || { state: 'fail', line: '작업 상태를 못 읽음' };
    $('mBar').style.width = (j.pct || 0) + '%';
    $('jobPillText').textContent = Math.round(j.pct || 0) + '% · ' + (j.pill || j.line || '매핑 중').slice(0, 40);
    if (j.state === 'run') {                                             // 만드는 중 = 단계 시간표 · 남은 시간 · 끝 예상 · 지난 시간
      const R = S.run, i = stageOf(j.label), now = Date.now();
      if (R && i > R.idx) { R.te[R.idx] = now; for (let k = R.idx + 1; k < i; k++) R.ts[k] = R.te[k] = 0; R.ts[i] = now; R.idx = i; }   // 뒤로 안 감 · 건너뛴 단계 = 바로 끝
      if (R && j.label) R.sub = j.label;
      timeline();
      const left = j.left == null ? -1 : j.left;
      $('mLeft').textContent = left < 0 ? '재는 중' : left < 60 ? '1분 안' : fmtDur(left);
      $('mEta').textContent = j.eta || '–';
      $('mGone').textContent = (j.elapsed || 0) < 60 ? Math.max(0, j.elapsed || 0) + '초' : fmtDur(j.elapsed || 0);
      clock('만드는 중', '', '지금 <b>' + esc(j.label || '준비') + '</b>' + (j.eta ? ' · <b>' + esc(j.eta) + '</b> 쯤 끝' : '') + '<br>창을 닫아도 계속 · 처음 화면 알약에서 다시 보기', 'run');
      $('mStatus').textContent = '';
    } else $('mStatus').textContent = j.line || '';
    $('mStatus').title = $('mStatus').textContent;
    const lg = $('mLog'); lg.textContent = (j.log || []).join('\n'); lg.scrollTop = lg.scrollHeight;
    if (j.state === 'run') { setTimeout(poll, 1000); return; }
    if (j.state === 'done' && S.run) { const R = S.run, now = Date.now(); R.te[R.idx] = now; R.idx = R.plan.length - 1; R.ts[R.idx] = now; timeline(); }
    $('mBuild').disabled = false; $('mStop').disabled = true; $('jobPill').classList.add('hide');
    const out = j.out; S.job = null; lockUi(false); est();
    if (j.state === 'done') { $('mStatus').textContent = '끝 · 여는 중…'; const q = await call('open_project', out); if (q && !q.ok) toast(q.msg); }
  }
  // 만드는 중 = 사진 · 작업 영역 · 설정 잠금 (JACK 1126 «시작 누르고 나서도 범위가 그려져» · 바꿔도 이미 도는 처리엔 안 들어감)
  const LOCK_IDS = ['mAll', 'mArea', 'mNone', 'mPick', 'mPickFiles', 'mOutB', 'mOut', 'mTpl', 'mTplSave', 'mTitle', 'mRen', 'mQBtn', 'mSetBtn', 'mClu'];
  function lockUi(on) {
    $('mp').classList.toggle('running', on);
    if (on) { popClose(); if (S.area) areaMode(false); }
    LOCK_IDS.forEach((id) => { const e = $(id); if (e) e.disabled = on; });
    $$('#mp [data-lock] select, #mp [data-lock] input, #mp [data-lock] button').forEach((e) => { if (!LOCK_IDS.includes(e.id)) e.disabled = on; });
    if (!on) paint();                                                    // 끈 카드 옵션은 다시 잠긴 채로
    else steps();
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
    $('mCards').addEventListener('scroll', hide);
    window.addEventListener('resize', hide);
  }
  // ------------------------------------------------------------ 실행 기록 · 카드 줄 접기 · 장치 성능
  function logShow(on) { $('mLogBox').classList.toggle('hide', !on); ['mLogBtn', 'mHdrLog', 'mRunLog'].forEach((id) => $(id).classList.toggle('on', on)); }
  // 실행 기록 칸 높이 = 위 가장자리 끌기 · 기억 · 두 번 누르면 처음 높이 (JACK 1126 «오른쪽 칸처럼 조절»)
  function logGripInit() {
    const box = $('mLogBox'), g = document.createElement('div'), KEY = 'rv_ui:logH';
    g.className = 'logGrip'; g.title = '끌어서 높이 바꾸기 · 두 번 누르면 처음 높이'; box.prepend(g);
    const maxH = () => Math.max(120, box.parentElement.getBoundingClientRect().height - 60);
    const apply = (h) => { box.style.height = Math.round(Math.max(90, Math.min(h, maxH()))) + 'px'; };
    let saved = 0; try { saved = Number(localStorage.getItem(KEY)) || 0; } catch (e) { /* 저장소 없음 */ }
    if (saved > 0) apply(saved);
    let dn = null;
    g.addEventListener('pointerdown', (e) => { if (e.button !== 0) return; dn = { y: e.clientY, h: box.getBoundingClientRect().height }; g.setPointerCapture(e.pointerId); g.classList.add('on'); e.preventDefault(); });
    g.addEventListener('pointermove', (e) => { if (dn) apply(dn.h + (dn.y - e.clientY)); });
    const up = () => { if (!dn) return; dn = null; g.classList.remove('on'); try { localStorage.setItem(KEY, String(Math.round(box.getBoundingClientRect().height))); } catch (e) { /* 저장 못 해도 됨 */ } };
    g.addEventListener('pointerup', up); g.addEventListener('pointercancel', up);
    g.addEventListener('dblclick', () => { box.style.height = ''; try { localStorage.removeItem(KEY); } catch (e) { /* 없어도 됨 */ } });
  }
  const logToggle = () => logShow($('mLogBox').classList.contains('hide'));
  // 카드 줄 접기 = 지도를 크게 (작업 영역 그릴 때) · 머리줄만 남음 (켠 결과 요약)
  function cardsToggle() {
    const c = !$('mp').classList.contains('cardsMin');
    popClose();
    $('mp').classList.toggle('cardsMin', c); $('mHdrSet').classList.toggle('on', c); S.fold.__cards = c; paint(); saveUi();
    window.dispatchEvent(new Event('resize'));
  }
  async function sysinfo() {
    const d = await call('sysinfo');
    if (!d) return;
    S.sys = d;
    const good = d.engines && d.gpu_nvidia;
    $('mDevDot').className = 'dot ' + (good ? 'ok' : 'warn');
    $('mDevName').textContent = d.gpu_nvidia ? String(d.gpu || 'NVIDIA').replace(/^NVIDIA\s+(GeForce\s+)?/i, '') : 'CPU 로 (느림)';
    $('mDev').title = '장치 성능 · ' + (good ? '준비됨' : (!d.gpu_nvidia ? 'NVIDIA 그래픽카드 없음 → CPU 로 (느림)' : '엔진 = 처음 만들 때 내려받음'));
    if (S.outs.has('gs') && !gsOk()) S.outs.delete('gs');       // 이 PC 에서 못 하는 결과물은 꺼 둠
    paint();
  }
  function devPop() {
    const p = $('mDevPop');
    if (!p.classList.contains('hide')) { p.classList.add('hide'); $('mDev').classList.remove('on'); return; }
    const d = S.sys;
    const L = !d ? ['장치 확인 중'] : [
      '<b>CPU</b>' + esc(d.cpu || '?') + ' · ' + d.cores + '코어',
      '<b>메모리</b>' + d.ram_gb + ' GB' + (d.ram_free_gb != null ? ' (남은 ' + d.ram_free_gb + ' GB)' : ''),
      '<b>그래픽카드</b>' + (d.gpu_nvidia ? esc(d.gpu) + ' · ' + d.vram_gb + ' GB' : 'NVIDIA 없음 → CPU 로 (느림)'),
      '<b>엔진</b>' + (d.engines ? 'COLMAP · OpenMVS 준비됨' : '처음 만들 때 내려받음'),
      '<b>가우시안</b>' + (d.gs_ok ? '됨' : '안 됨') + (d.gs_why ? ' · ' + esc(d.gs_why) : ''),
    ];
    p.innerHTML = L.join('<br>');
    p.classList.remove('hide'); $('mDev').classList.add('on');
  }
  // ------------------------------------------------------------ 저장한 설정 (기본 = 기본값 · 저장한 것 = 앱 설정 폴더)
  async function tplList(sel) {
    const L = (await call('templates_list')) || [];
    const s = $('mTpl'); s.textContent = '';
    s.add(new Option('기본', ''));
    for (const n of L) s.add(new Option(n, n));
    s.value = sel && L.includes(sel) ? sel : '';
  }
  async function tplPick() {
    const n = $('mTpl').value;
    if (!n) { apply(DEF); saveUi(); toast('기본 설정'); return; }
    const r = await call('template_load', n);
    if (!r || !r.ok) { toast(r ? r.msg : '저장한 설정 못 읽음'); return; }
    apply(r.values); saveUi(); toast('저장한 설정 «' + n + '»');
  }
  async function tplSave() {
    const n = $('mTplName').value.trim();
    if (!n) { toast('설정 이름 넣기'); $('mTplName').focus(); return; }
    const r = await call('template_save', n, values());
    if (!r || !r.ok) { if (r) toast(r.msg); return; }
    await tplList(r.name);
    $('mTplRow').classList.add('hide'); $('mTplBtns').classList.remove('hide');
    toast('설정 저장 · ' + r.name);
  }

  function open() { $('mp').classList.remove('hide'); $('jobPill').classList.add('hide'); draw(); est(); }   // 사진은 «사진 폴더 열기 · 사진 파일 고르기» 로 (열 때 저절로 창 띄우지 않음 · JACK 1126)
  function close() { popClose(); tipsShow(false); $('mp').classList.add('hide'); if (S.job) $('jobPill').classList.remove('hide'); }
  function init(info) {
    S.info = info || {}; MAP.key = S.info.vworld || '';
    $('mBack').addEventListener('click', close);
    $('mPick').addEventListener('click', pick);
    $('mPickFiles').addEventListener('click', pickFiles);
    tipInit(); logGripInit(); popInit();
    $('mBuild').addEventListener('click', build);
    $('mStop').addEventListener('click', async () => { if (S.job) await call('stop_job', S.job); });
    $('mAll').addEventListener('click', () => { S.sel = new Set(S.photos.map((p) => p.name)); S.range = null; draw(); est(); });
    $('mNone').addEventListener('click', () => { S.sel.clear(); S.range = null; draw(); est(); });
    $('mArea').title = '지도에서 끌어 네모 → 그 안을 찍은 사진만 쓰고 결과물도 그 안만 · Shift = 넓히기';
    $('mArea').addEventListener('click', () => { areaMode(!S.area); if (S.area) toast('지도에서 끌어 네모 → 그 안만 만듦 · Shift = 넓히기'); });
    $$('#mp [data-act=area]').forEach((b) => b.addEventListener('click', () => { popClose(); if (!S.photos.length) { toast('사진 먼저'); stepGo('photo'); return; } if (!S.area) $('mArea').click(); }));   // 세부 옵션 «작업 영역 ›» = 작업 영역 지정
    $('mOutB').addEventListener('click', async () => { const d = await call('pick_folder'); if (d) { $('mOut').value = d; $('mOut').title = d; S.outTouched = true; } });
    $('mOut').addEventListener('input', () => { S.outTouched = true; $('mOut').title = $('mOut').value; });
    $('mRen').addEventListener('click', (e) => { e.preventDefault(); $('mTitle').focus(); $('mTitle').select(); });
    $('mTitle').addEventListener('keydown', (e) => { if (e.key === 'Enter') $('mTitle').blur(); });
    // 단계 띠
    $$('#mSteps .mstep').forEach((b) => b.addEventListener('click', () => stepGo(b.dataset.step)));
    // 결과 카드: 카드 아무 데나 = 켜고 끄기 (⋯ · 떠 있는 칸 · 스위치는 따로)
    $$('#mCards .tcard').forEach((c) => c.addEventListener('click', (e) => {
      if (e.target.closest('.cmore, .mpop, .sw')) return;
      const b = c.querySelector('.sw[data-o]'); if (b && !b.disabled) b.click();
    }));
    $$('#mp .sw[data-o]').forEach((b) => b.addEventListener('click', (e) => {
      e.stopPropagation();
      const o = b.dataset.o;
      if (o === 'gs' && !S.outs.has(o) && !gsOk()) { toast(gsWhy()); return; }
      if (S.outs.has(o)) S.outs.delete(o); else S.outs.add(o);
      changed();
    }));
    // 세부 옵션
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
    $$('#mQ button').forEach((b) => b.addEventListener('click', () => { S.q = b.dataset.q; changed(); popClose(); }));
    $('mCrs').addEventListener('change', changed);
    $('mCt').addEventListener('change', () => { $('mCt').value = clamp('ct', $('mCt').value); changed(); });
    // 저장한 설정
    $('mTpl').addEventListener('change', tplPick);
    $('mTplSave').addEventListener('click', () => { $('mTplBtns').classList.add('hide'); $('mTplRow').classList.remove('hide'); $('mTplName').value = $('mTpl').value; $('mTplName').focus(); });
    $('mTplOk').addEventListener('click', tplSave);
    $('mTplName').addEventListener('keydown', (e) => { if (e.key === 'Enter') tplSave(); if (e.key === 'Escape') $('mTplX').click(); });
    $('mTplX').addEventListener('click', () => { $('mTplRow').classList.add('hide'); $('mTplBtns').classList.remove('hide'); });
    // 머리 단추 · 상태 줄
    $('mLogBtn').addEventListener('click', logToggle);
    $('mHdrLog').addEventListener('click', logToggle);
    $('mRunLog').addEventListener('click', logToggle);
    $('mFold').addEventListener('click', cardsToggle);
    $('mHdrSet').addEventListener('click', cardsToggle);
    $('mHelp').addEventListener('click', (e) => { e.stopPropagation(); tipsShow($('mTips').classList.contains('hide')); });
    document.addEventListener('pointerdown', (e) => {                    // 도움말 · 장치 성능 = 바깥 누르면 닫힘
      if (!$('mTips').classList.contains('hide') && !$('mTips').contains(e.target) && !$('mHelp').contains(e.target)) tipsShow(false);
      if (!$('mDevPop').classList.contains('hide') && !$('mDevPop').contains(e.target) && !$('mDev').contains(e.target)) { $('mDevPop').classList.add('hide'); $('mDev').classList.remove('on'); }
    }, true);
    $('mDev').addEventListener('click', devPop);
    loadUi();
    tplList();
    sysinfo();
    mapInit();
  }
  window.RVMap = { init, open, close, _S: S, _est: est, _test: { lockUi: (on) => lockUi(on), poll: () => poll(), est: () => est(), setPhotos: (folder, photos, out) => { S.folder = folder; S.photos = photos; S.sel = new Set(photos.map((p) => p.name)); $('mOut').value = out || ''; MAP.fitted = false; draw(); est(); },
    take, build, draw, values, apply, runStart, stageOf, timeline, popClose } };
})();
