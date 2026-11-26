/* 시험 전용: check_interact.py 가 시험 폴더의 복사본 html 에만 넣는다 (배포 화면에는 없음).
 * 마우스 · 키 입력을 흉내 내고 결과를 console.log('SELFTEST ...') 로 남긴다. */
(async function () {
  'use strict';
  const T = new URLSearchParams(location.search).get('t') || '';
  const $ = (id) => document.getElementById(id);
  const log = (...a) => console.log('SELFTEST', ...a);
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
  async function until(fn, ms) { // cdp.py 가 실제 시간으로 돌리므로 그냥 기다리면 된다
    const t0 = performance.now();
    while (performance.now() - t0 < (ms || 10000)) { if (fn()) return true; await sleep(40); }
    return !!fn();
  }
  let fails = 0;
  function check(name, ok, detail) { if (!ok) fails++; log(ok ? 'PASS' : 'FAIL', name, detail === undefined ? '' : String(detail)); }
  function ptr(el, type, x, y, button) {
    el.dispatchEvent(new PointerEvent(type, {
      bubbles: true, cancelable: true, pointerId: 1, pointerType: 'mouse', isPrimary: true,
      button: type === 'pointermove' ? -1 : (button || 0), buttons: type === 'pointerup' ? 0 : 1, clientX: x, clientY: y,
    }));
  }
  function tap(el, x, y) { ptr(el, 'pointerdown', x, y); ptr(el, 'pointerup', x, y); }
  const shown = (el) => !!el && el.getClientRects().length > 0; // display:none (자기 · 조상) 이면 false
  const no = () => $('shotNo').textContent;
  const loaded = () => !$('loading').classList.contains('show');

  await until(() => no() !== '-' && loaded(), 10000);
  check('first shot shown', no() !== '-' && loaded(), no());
  await sleep(100);

  if (T === 'nav') {
    const N = window.RV_PROJECT.shots.length;
    const is = (k) => no() === k + ' / ' + N;
    const key = async (k, want) => {
      document.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true }));
      await until(() => is(want));
      check(k + ' → ' + want, is(want), no());
    };
    for (let k = 3; k <= N; k++) await key('PageDown', k);   // 찍은 순서 = PageUp / PageDown (화살표는 보는 방향 기준)
    check('next disabled at end', $('next').disabled === true);
    $('prev').click();
    await until(() => is(N - 1));
    check('prev button → ' + (N - 1), is(N - 1), no());
    for (let k = N - 2; k >= 1; k--) await key('PageUp', k);
    check('prev disabled at start', $('prev').disabled === true);
    $('next').click();
    await until(() => is(2));
    // 지도에서 3번 점 누르기: 지도 가운데 = 사진 전체 네모의 가운데, 줌 19 (점들이 가까워 최대 줌)
    const s = window.RV_PROJECT.shots;
    const mxf = (lon) => (lon + 180) / 360;
    const myf = (lat) => { const q = Math.sin(lat * Math.PI / 180); return 0.5 - Math.log((1 + q) / (1 - q)) / (4 * Math.PI); };
    const xs = s.map((q) => mxf(q.lon)), ys = s.map((q) => myf(q.lat));
    const cxm = (Math.min(...xs) + Math.max(...xs)) / 2, cym = (Math.min(...ys) + Math.max(...ys)) / 2;
    const k = 256 * Math.pow(2, 19);
    const r = $('map').getBoundingClientRect();
    const x = r.left + r.width / 2 + (xs[2] - cxm) * k, y = r.top + r.height / 2 + (ys[2] - cym) * k;
    tap($('map'), x, y);
    await until(() => is(3));
    check('map tap on shot 3 → 3', is(3), no());
    if (new URLSearchParams(location.search).get('missing')) {
      // 3번 사진 파일이 없는 폴더: 알림이 뜨고 화면은 3번으로 넘어가야 한다
      await until(() => $('toast').textContent.indexOf('사진 없음') >= 0);
      check('missing pano toast', $('toast').textContent.indexOf('사진 없음 · 0003') >= 0, $('toast').textContent);
    } else {
      check('no error toast', $('toast').textContent.indexOf('없음') < 0, $('toast').textContent);
    }
  }

  async function markerNav() {
    // URL 이 2번 사진 땅점을 화면 가운데에 두도록 맞춰져 있다.
    const r = $('ov').getBoundingClientRect();
    // 원판에 번호가 없어야 한다: 360° 겹쳐 그림 캔버스에 쓰는 글자를 모은다 (관로 이름표만 있어야 함)
    const texts = [];
    const fill0 = CanvasRenderingContext2D.prototype.fillText;
    CanvasRenderingContext2D.prototype.fillText = function (t, ...rest) { if (this.canvas && this.canvas.id === 'ov') texts.push(String(t)); return fill0.call(this, t, ...rest); };
    ptr($('ov'), 'pointermove', r.left + r.width / 2, r.top + r.height / 2); // 마우스 올림 → 다시 그림
    await sleep(200);
    CanvasRenderingContext2D.prototype.fillText = fill0;
    check('원판에 번호 없음', !texts.some((t) => /^\d+$/.test(t.trim())), JSON.stringify(texts));
    check('마우스 올림 → 손가락', $('ov').classList.contains('point'));
    const px = $('ov').getContext('2d').getImageData(Math.round($('ov').width / 2), Math.round($('ov').height / 2), 1, 1).data;
    check('원판 가운데 밝음', px[3] === 255 && px[0] > 200 && px[1] > 200 && px[2] > 200, [...px].join(','));
    tap($('ov'), r.left + r.width / 2, r.top + r.height / 2);
    await until(() => no().indexOf('2 /') === 0);
    check('tap ground marker → 2', no().indexOf('2 /') === 0, no());
    // 끌기 뒤에는 누름으로 치지 않는다
    const before = no();
    ptr($('ov'), 'pointerdown', r.left + 200, r.top + 200);
    ptr($('ov'), 'pointermove', r.left + 260, r.top + 200);
    ptr($('ov'), 'pointerup', r.left + 260, r.top + 200);
    await sleep(300);
    check('drag is not a tap', no() === before, no());
  }
  if (T === 'marker') await markerNav();

  // ---------------------------------------------------------------- 점 찍어 맞추기 (make_synth --align 자료)
  // data/align_truth.js = 답 (영상 0001 을 틀리게 찍은 값 · 땅 기울기 · 칠한 네모 자리). 사진 · 지도 누를 자리는 이 답으로 계산한다.
  const Q = new URLSearchParams(location.search);
  const LOOSE = Q.get('loose') === '1'; // 값 맞음은 재기만 (높이 타일 없는 대조 시험)
  const P = window.RV_PROJECT;
  const D2R = Math.PI / 180;
  const VIEW = { yaw: Number(Q.get('yaw') || 0), pitch: Number(Q.get('pitch') || -45), fov: Number(Q.get('fov') || 90) };
  const CALK = ['dx', 'dy', 'dz', 'dyaw', 'dpitch', 'droll'];
  const mul3 = (A, B) => [0, 1, 2].flatMap((i) => [0, 1, 2].map((j) => A[i * 3] * B[j] + A[i * 3 + 1] * B[3 + j] + A[i * 3 + 2] * B[6 + j]));
  function rotM(c) { // 보기 화면과 같은 R = Rz(dyaw)·Rx(dpitch)·Ry(droll)
    const a = c.dyaw * D2R, b = c.dpitch * D2R, g = c.droll * D2R;
    return mul3(mul3([Math.cos(a), Math.sin(a), 0, -Math.sin(a), Math.cos(a), 0, 0, 0, 1], [1, 0, 0, 0, Math.cos(b), -Math.sin(b), 0, Math.sin(b), Math.cos(b)]),
      [Math.cos(g), 0, Math.sin(g), 0, 1, 0, -Math.sin(g), 0, Math.cos(g)]);
  }
  const mulv = (R, v) => [0, 1, 2].map((i) => R[i * 3] * v[0] + R[i * 3 + 1] * v[1] + R[i * 3 + 2] * v[2]);
  const mulTv = (R, v) => [0, 1, 2].map((i) => R[i] * v[0] + R[3 + i] * v[1] + R[6 + i] * v[2]);
  const unit = (v) => { const l = Math.hypot(v[0], v[1], v[2]); return v.map((x) => x / l); };
  const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
  function loadTruth() {
    return new Promise((res) => {
      const s = document.createElement('script');
      s.src = 'data/align_truth.js';
      s.onload = () => res(window.RV_ALIGN_TRUTH || null);
      s.onerror = () => res(null);
      document.head.appendChild(s);
    });
  }
  function calNow(vid) { // 화면이 지금 쓰는 맞춤 (이 브라우저 → 파일 → 0)
    let st = null;
    try { st = JSON.parse(localStorage.getItem('rv_calib:' + P.title)); } catch (e) { st = null; }
    const l = (st && st.videos && st.videos[vid]) || {}, f = (window.RV_CALIB && window.RV_CALIB.videos && window.RV_CALIB.videos[vid]) || {};
    const c = {};
    CALK.forEach((k) => { c[k] = Number.isFinite(l[k]) ? l[k] : Number.isFinite(f[k]) ? f[k] : 0; });
    return c;
  }
  function viewXY(w) { // 세계 방향 → 360° 화면 (client 좌표) · 보기 = URL 의 yaw · pitch · fov
    const ov = $('ov'), dpr = Math.min(devicePixelRatio || 1, 2), W = ov.width / dpr, H = ov.height / dpr;
    const t = Math.tan(VIEW.fov * D2R / 2), tanH = W >= H ? t : t * W / H, tanV = W >= H ? t * H / W : t;
    const y = VIEW.yaw * D2R, p = VIEW.pitch * D2R;
    const f = [Math.cos(p) * Math.sin(y), Math.cos(p) * Math.cos(y), Math.sin(p)];
    const r = [Math.cos(y), -Math.sin(y), 0], u = [-Math.sin(y) * Math.sin(p), -Math.cos(y) * Math.sin(p), Math.cos(p)];
    const z = dot(w, f), rc = ov.getBoundingClientRect();
    if (!(z > 0.05)) return null; // 등 뒤
    return [rc.left + W / 2 + dot(w, r) / z * (W / 2) / tanH, rc.top + H / 2 - dot(w, u) / z * (H / 2) / tanV];
  }
  const shotOf = (id) => P.shots.find((s) => s.id === id);
  function truthP(TR, sid, k) { // 사진 sid 에서 네모 k 가 찍힌 사진 속 방향 (참 자리 · 참 R)
    const s = shotOf(sid), e = TR.err, L = TR.landmarks[k];
    const X = [L[0], L[1], TR.gz + TR.slope[0] * L[0] + TR.slope[1] * L[1]];
    return mulTv(rotM(e), unit([X[0] - s.e - e.dx, X[1] - s.n - e.dy, X[2] - s.z - e.dz]));
  }
  const photoXY = (TR, sid, k) => viewXY(mulv(rotM(calNow(TR.video)), truthP(TR, sid, k)));
  const mxf = (lon) => (lon + 180) / 360;
  const myf = (lat) => { const q = Math.sin(lat * D2R); return 0.5 - Math.log((1 + q) / (1 - q)) / (4 * Math.PI); };
  function mapXY(TR, en) { // 지도 (처음 자리: 사진 점 네모 가운데 · 줌 19) 에서 격자 (e, n) 의 client 좌표
    // 지도의 사진 점은 그 영상 맞춤(dx, dy)만큼 옮겨 그려지고 [전체] 도 그 자리로 맞춘다 (1123)
    const sh = P.shots.map((q) => { const c = calNow(q.video) || {}; return [q.lon + (c.dx || 0) / (111320 * Math.cos(q.lat * D2R)), q.lat + (c.dy || 0) / 111320]; });
    const xs = sh.map((q) => mxf(q[0])), ys = sh.map((q) => myf(q[1]));
    const cx = (Math.min(...xs) + Math.max(...xs)) / 2, cy = (Math.min(...ys) + Math.max(...ys)) / 2;
    const lat = TR.lat0 + en[1] / 111320, lon = TR.lon0 + en[0] / (111320 * Math.cos(TR.lat0 * D2R));
    const k = 256 * Math.pow(2, 19), mc = $('map'), dpr = Math.min(devicePixelRatio || 1, 2), rc = mc.getBoundingClientRect();
    return [rc.left + mc.width / dpr / 2 + (mxf(lon) - cx) * k, rc.top + mc.height / dpr / 2 + (myf(lat) - cy) * k];
  }
  const wheel = (el, x, y, dy) => el.dispatchEvent(new WheelEvent('wheel', { bubbles: true, cancelable: true, deltaY: dy, deltaMode: 0, clientX: x, clientY: y }));
  const step = () => $('alStep').textContent;
  const rows = () => [...$('alList').querySelectorAll('.alRow')];
  const rowText = () => rows().map((r) => r.firstChild.textContent).join(' | ');
  const rowRes = () => rows().map((r) => Number((r.firstChild.textContent.split(' · ')[1] || '').replace(' m', '')));
  const esc = () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
  async function snap(name) { log('SNAP', name); await until(() => window.__snapAck === name, 30000); }
  async function gotoShot(sid) {
    const seq = shotOf(sid).seq, cur = () => Number(no().split(' ')[0]);
    for (let g = 0; g < 10 && cur() !== seq; g++) {
      const want = cur() + (cur() < seq ? 1 : -1);
      document.dispatchEvent(new KeyboardEvent('keydown', { key: cur() < seq ? 'PageDown' : 'PageUp', bubbles: true }));
      await until(() => cur() === want && loaded());
    }
    await sleep(80);
  }
  // 짝 하나: 사진 sid 에서 네모 k 를 누르고, 지도에서 같은 네모를 줌 22 로 키워 누른 뒤 19 로 되돌림. mapEn = 지도에서 누를 격자 자리 (기본 = 참 자리)
  async function addPair(TR, sid, k, opt) {
    opt = opt || {};
    await gotoShot(sid);
    const n0 = rows().length;
    const [px, py] = photoXY(TR, sid, k);
    tap($('ov'), px, py);
    await sleep(60);
    const okPhoto = step() === '② 지도에서 같은 점';
    if (opt.snapPending) await snap(opt.snapPending);
    // 줌 19 → 22 (휠 자리는 정수 px 로 바뀌므로 정수에서 돌리고, 누를 자리는 8 배로 옮김) → 누름 → [전체] 로 처음 자리
    const [fx, fy] = mapXY(TR, opt.mapEn || TR.landmarks[k]), ix = Math.round(fx), iy = Math.round(fy);
    wheel($('map'), ix, iy, -1200);
    await sleep(60);
    tap($('map'), ix + 8 * (fx - ix), iy + 8 * (fy - iy));
    await until(() => rows().length === n0 + 1, 5000);
    await sleep(60);
    if (opt.snapZoom) await snap(opt.snapZoom);
    $('zFit').click();
    await sleep(60);
    check('짝 ' + (n0 + 1) + ' (사진 ' + sid + ' · 네모 ' + (k + 1) + ')', okPhoto && rows().length === n0 + 1 && step() === '① 사진에서 점',
      rowText() + ' / ' + $('alSum').textContent);
    log('CAL', 'after ' + (n0 + 1), JSON.stringify(calNow(TR.video)));
    log('PAIRS', localStorage.getItem('rv_pairs:' + P.title));
  }
  function calCheck(TR, label) {
    const c = calNow(TR.video), e = TR.err, d = {};
    ['dx', 'dy', 'dz', 'dyaw'].forEach((k) => { d[k] = Math.round((c[k] - e[k]) * 1e4) / 1e4; });
    const ok = Math.abs(d.dx) <= 0.15 && Math.abs(d.dy) <= 0.15 && Math.abs(d.dz) <= 0.3 && Math.abs(d.dyaw) <= 0.3 && c.dpitch === 0 && c.droll === 0;
    const txt = 'got ' + JSON.stringify(c) + ' · minus truth ' + JSON.stringify(d);
    if (LOOSE) log('INFO', label, txt); else check(label, ok, txt);
  }
  async function enterPick() {
    if (!$('calPane').classList.contains('open')) $('calHead').click();
    $('alGo').click();
    await sleep(30);
  }
  async function exportCalib() { // 파일로 저장 → 내려받는 calib.js 글
    let blob = null;
    const oc = URL.createObjectURL;
    URL.createObjectURL = function (b) { blob = b; return oc.call(URL, b); };
    try { $('calSave').click(); } finally { URL.createObjectURL = oc; }
    return blob ? blob.text() : '';
  }

  if (T === 'al3') {
    const TR = await loadTruth();
    check('답 파일', !!TR);
    await enterPick();
    check('점 찍기 켬', $('ov').classList.contains('pick') && $('map').classList.contains('pick') && $('alChip').classList.contains('show')
      && step() === '① 사진에서 점' && $('alGo').textContent === '끝' && $('viewPane').classList.contains('pickHere'), step());
    // 하늘은 안 받음 · 사진 찍음 → ② · Esc → 취소
    const rc = $('ov').getBoundingClientRect();
    const [px, py] = photoXY(TR, '0001', 0);
    tap($('ov'), px, py);
    await sleep(40);
    check('사진 찍음 → ②', step() === '② 지도에서 같은 점' && $('mapPane').classList.contains('pickHere'), step());
    esc();
    await sleep(40);
    check('Esc → 짝 취소 (모드는 그대로)', step() === '① 사진에서 점' && rows().length === 0 && $('ov').classList.contains('pick'), step());

    await addPair(TR, '0001', 0, { snapPending: 'pending' });
    await addPair(TR, '0001', 1);
    await addPair(TR, '0003', 2, { snapZoom: 'map22' });
    await addPair(TR, '0003', 3);
    calCheck(TR, '4 짝 → 맞춤 값 (dx/dy ±0.15 m · dz ±0.3 m · dyaw ±0.3°)');
    const r4 = rowRes();
    log('RES', '4 pairs', JSON.stringify(r4), $('alSum').textContent);
    if (!LOOSE) check('4 짝 어긋남 모두 < 0.05 m', r4.length === 4 && r4.every((v) => v < 0.05), JSON.stringify(r4));
    check('요약 글', /^맞춤 오차 \d+\.\d\d m · 점 4개$/.test($('alSum').textContent) && $('alSum2').textContent === $('alSum').textContent, $('alSum').textContent);
    await snap('solved');

    // 4번 지우기 → 3 짝으로 다시 풂
    rows()[3].querySelector('.alDel').click();
    await until(() => rows().length === 3);
    check('지우기 → 3 짝 · 번호 1,2,3', rows().length === 3 && rows().map((r) => r.firstChild.textContent.split(' · ')[0]).join(',') === '1,2,3', rowText());
    calCheck(TR, '지운 뒤 3 짝 → 맞춤 값');
    log('RES', '3 pairs', JSON.stringify(rowRes()), $('alSum').textContent);

    // 파일로 저장 → calib.js 에 짝
    const text = await exportCalib();
    log('EXPORT', JSON.stringify(text));
    let ex = null;
    try { ex = JSON.parse(text.slice(text.indexOf('{'), text.lastIndexOf('}') + 1)); } catch (e) { ex = null; }
    const v = ex && ex.videos && ex.videos[TR.video], c = calNow(TR.video);
    check('calib.js: 영상 0001 짝 3개 + 맞춤 값', !!v && Array.isArray(v.pairs) && v.pairs.length === 3
      && v.pairs.every((q) => typeof q.shot === 'string' && q.p.length === 3 && Number.isFinite(q.e) && Number.isFinite(q.n))
      && CALK.every((k) => Math.abs(v[k] - c[k]) < 1e-9), text.length + ' 글자');
    check('calib.js: 영상 0002 짝 없음', !!ex && !!ex.videos['0002'] && !('pairs' in ex.videos['0002']));

    // 끌기는 찍기가 아님 (360 · 지도)
    ptr($('ov'), 'pointerdown', rc.left + 300, rc.top + 300);
    for (let i = 1; i <= 6; i++) ptr($('ov'), 'pointermove', rc.left + 300 + 12 * i, rc.top + 300);
    ptr($('ov'), 'pointerup', rc.left + 372, rc.top + 300);
    await sleep(60);
    check('360 끌기 → 안 찍힘', step() === '① 사진에서 점' && rows().length === 3, step());
    tap($('ov'), rc.left + rc.width / 2, rc.top + rc.height * 0.6);
    await sleep(40);
    const mr = $('map').getBoundingClientRect();
    ptr($('map'), 'pointerdown', mr.left + 150, mr.top + 150);
    for (let i = 1; i <= 6; i++) ptr($('map'), 'pointermove', mr.left + 150 + 10 * i, mr.top + 150);
    ptr($('map'), 'pointerup', mr.left + 210, mr.top + 150);
    await sleep(60);
    check('지도 끌기 → 안 찍힘', step() === '② 지도에서 같은 점' && rows().length === 3, step() + ' / ' + rows().length);
    esc();
    await sleep(40);
    $('alEnd').click();
    await sleep(40);
    check('끝 → 꺼짐', !$('ov').classList.contains('pick') && !$('map').classList.contains('pick') && !$('alChip').classList.contains('show')
      && $('alGo').textContent === '점 찍어 맞추기' && rows().length === 3);
  }

  if (T === 'al_reload') { // al3 다음, 같은 브라우저 저장소로 다시 열기
    const TR = await loadTruth();
    $('calHead').click();
    await sleep(50);
    check('다시 열어도 짝 3개', rows().length === 3 && /점 3개$/.test($('alSum').textContent), rowText() + ' / ' + $('alSum').textContent);
    calCheck(TR, '다시 열어도 맞춤 값');
    const texts = [];
    const fill0 = CanvasRenderingContext2D.prototype.fillText;
    CanvasRenderingContext2D.prototype.fillText = function (t, ...rest) { if (this.canvas && this.canvas.id === 'ov') texts.push(String(t)); return fill0.call(this, t, ...rest); };
    window.dispatchEvent(new Event('resize'));
    await sleep(250);
    CanvasRenderingContext2D.prototype.fillText = fill0;
    check('360 핀 1 · 2 · 3 다시 그림', ['1', '2', '3'].every((t) => texts.includes(t)), JSON.stringify(texts));
    check('점 찍기는 꺼진 채', !$('ov').classList.contains('pick') && !$('alChip').classList.contains('show'));
  }

  if (T === 'al_file') { // 저장소 비어 있음 + calib.js 에 짝 3개 (al3 에서 내려받은 것)
    const TR = await loadTruth();
    $('calHead').click();
    await sleep(50);
    check('파일 짝 3개 보임', rows().length === 3 && localStorage.getItem('rv_pairs:' + P.title) === null, rowText());
    calCheck(TR, '파일 맞춤 값');
    await enterPick();
    $('zFit').click();             // 맞춤 창을 연 뒤 지도 칸 크기로 다시 [전체] (처음 맞춤은 창 열기 전 크기)
    await sleep(60);
    await addPair(TR, '0001', 4);
    let st = null;
    try { st = JSON.parse(localStorage.getItem('rv_pairs:' + P.title)); } catch (e) { st = null; }
    check('파일 짝 + 1 → 이 브라우저에 4개', !!st && st.videos && st.videos[TR.video] && st.videos[TR.video].length === 4, JSON.stringify(st && st.videos && Object.keys(st.videos)));
    calCheck(TR, '네모 5 더한 뒤 맞춤 값');
  }

  if (T === 'al1') { // URL 고개 −45° · 화면 위쪽에 하늘
    const TR = await loadTruth();
    await enterPick();
    const sky = viewXY(unit([0, 1, 0.08]));
    tap($('ov'), sky[0], sky[1]);
    await sleep(40);
    check('하늘 → 안 받음', step() === '① 사진에서 점' && $('toast').textContent === '땅을 찍기', step() + ' / ' + $('toast').textContent);
    await addPair(TR, '0001', 0);
    const c = calNow(TR.video);
    check('1 짝 → 동 · 북만 바뀜', c.dz === 0 && c.dyaw === 0 && c.dpitch === 0 && c.droll === 0 && (c.dx !== 0 || c.dy !== 0), JSON.stringify(c));
    check('1 짝 어긋남 0.00 m', rowRes()[0] === 0 && $('alSum').textContent === '맞춤 오차 0.00 m · 점 1개', rowText() + ' / ' + $('alSum').textContent);
  }

  if (T === 'al2') {
    const TR = await loadTruth();
    await enterPick();
    await addPair(TR, '0001', 0);
    await addPair(TR, '0001', 1);
    const c = calNow(TR.video);
    check('2 짝 → 동 · 북 · 방향 (높이 그대로)', c.dz === 0 && c.dpitch === 0 && c.droll === 0 && c.dyaw !== 0, JSON.stringify(c));
    check('2 짝 요약', /^맞춤 오차 \d+\.\d\d m · 점 2개$/.test($('alSum').textContent), $('alSum').textContent);
    log('RES', '2 pairs', JSON.stringify(rowRes()), $('alSum').textContent);
    await snap('two');
  }

  if (T === 'al_bad') { // 지도 점을 사진 자리 둘레로 60° 돌려 찍음 → 방향 60° 넘게 나옴 → '점 확인' · 값 그대로
    const TR = await loadTruth();
    await enterPick();
    const s = shotOf('0001');
    const turn = (L) => { const a = 60 * D2R, x = L[0] - s.e, y = L[1] - s.n; return [s.e + x * Math.cos(a) + y * Math.sin(a), s.n - x * Math.sin(a) + y * Math.cos(a)]; };
    await addPair(TR, '0001', 0, { mapEn: turn(TR.landmarks[0]) });
    const c1 = calNow(TR.video);
    await addPair(TR, '0001', 1, { mapEn: turn(TR.landmarks[1]) });
    const c2 = calNow(TR.video);
    check('터무니없는 방향 → 점 확인 · 값 그대로', $('toast').textContent === '점 확인' && CALK.every((k) => c1[k] === c2[k]), $('toast').textContent + ' ' + JSON.stringify(c2));
  }

  if (T === 'al_off') { // 켰다 끈 뒤 원판 마우스 올림 · 누름 · 끌기가 그대로인지
    await enterPick();
    check('점 찍기 켬', $('ov').classList.contains('pick') && $('alChip').classList.contains('show'));
    $('alEnd').click();
    await sleep(30);
    check('끝 → 꺼짐', !$('ov').classList.contains('pick') && !$('alChip').classList.contains('show') && $('alGo').textContent === '점 찍어 맞추기');
    await markerNav();
  }

  if (T === 'ui') {
    // 프로그램 (pywebview) 없음 → 프로그램 단추 · 창 숨김
    // 1126 배치: 윗줄 [처음] · 관로 카테고리 프로그램 단추 · 결과물 · 웹 공유 카테고리 (옛 윗줄 묶음 · [내보내기] 대신)
    const catEl = (c) => document.querySelector('#opts .cat[data-cat="' + c + '"]');
    check('프로그램 단추 숨김 (pywebview 없음)', !window.pywebview && !shown($('hPipes')) && !shown($('hObst')) && !shown($('bShpOut')) && !shown($('hHome'))
      && !shown(catEl('out')) && !shown(catEl('web')) && !shown($('tgTrim')) && shown($('tgEdit')) && !shown($('dlgBack')));
    check('이름 단추 숨김 (VWorld 아님)', !shown($('mNames')));
    $('mDepth').click();
    check('깊이 on', $('mDepth').classList.contains('on') && !$('mSurf').classList.contains('on'));
    $('mSurf').click();
    check('노면 on', $('mSurf').classList.contains('on'));
    $('tgPipes').click();
    check('관로 off', !$('tgPipes').classList.contains('on'));
    $('tgPipes').click();
    check('관로 on', $('tgPipes').classList.contains('on'));
    $('tgPath').click();
    check('경로 off', !$('tgPath').classList.contains('on'));
    $('tgPath').click();
    const cv = $('cover');
    const P = window.RV_PROJECT;
    const def = P.depth != null ? P.depth : P.cover != null ? P.cover : 1.2;
    check('평균매설깊이 이름 · 기본값', cv.parentNode.textContent.indexOf('평균매설깊이') >= 0 && Number(cv.value) === Number(def), cv.value + ' / ' + def);
    cv.value = '2.5';
    cv.dispatchEvent(new Event('input', { bubbles: true }));
    cv.dispatchEvent(new Event('change', { bubbles: true }));
    check('평균매설깊이 2.5', cv.value === '2.5', cv.value);
    cv.value = String(def);
    cv.dispatchEvent(new Event('input', { bubbles: true }));

    $('calHead').click();
    check('맞춤 열림', $('calPane').classList.contains('open'));
    const rows = $('calRows');
    const inputs = rows.querySelectorAll('input');
    const btns = rows.querySelectorAll('button');
    check('맞춤 6 줄', inputs.length === 6 && btns.length === 12, inputs.length + '/' + btns.length);
    // 방향(4번째 줄) + 세 번
    const plusYaw = btns[7];
    for (let i = 0; i < 3; i++) { ptr(plusYaw, 'pointerdown', 0, 0); ptr(plusYaw, 'pointerup', 0, 0); }
    check('방향 +0.3', inputs[3].value === '0.3', inputs[3].value);
    let st = null;
    try { st = JSON.parse(localStorage.getItem('rv_calib:' + window.RV_PROJECT.title)); } catch (e) { st = null; }
    check('localStorage 저장', !!st && st.videos && st.videos['0001'] && Math.abs(st.videos['0001'].dyaw - 0.3) < 1e-9, JSON.stringify(st));
    check('바뀐 줄 표시', rows.querySelectorAll('.cl.mod').length >= 1);
    // 동(첫 줄) 직접 입력
    inputs[0].value = '-1.25';
    inputs[0].dispatchEvent(new Event('input', { bubbles: true }));
    try { st = JSON.parse(localStorage.getItem('rv_calib:' + window.RV_PROJECT.title)); } catch (e) { st = null; }
    check('동 직접 입력 저장', st && Math.abs(st.videos['0001'].dx + 1.25) < 1e-9, st && st.videos['0001'].dx);
    $('calSave').click();
    await sleep(50);
    check('저장 알림', $('toast').textContent.indexOf('calib.js') >= 0, $('toast').textContent);
    $('calReset').click();
    const allZero = [...inputs].every((i) => Number(i.value) === 0);
    check('초기화 → 0', allZero, [...inputs].map((i) => i.value).join(','));

    // 지도 오른쪽 누름 → 위경도 알림
    const mr = $('map').getBoundingClientRect();
    $('map').dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: mr.left + mr.width / 2, clientY: mr.top + mr.height / 2, button: 2 }));
    await sleep(50);
    const ll = $('toast').textContent.split(/[ ,]+/).map(Number);
    check('오른쪽 누름 위경도', /^\d+\.\d{6}, \d+\.\d{6}/.test($('toast').textContent) && Math.abs(ll[0] - 37) < 0.001 && Math.abs(ll[1] - 127) < 0.001, $('toast').textContent);

    // 지도 접기 → 360° 칸이 넓어짐
    const w0 = $('ov').getBoundingClientRect().width;
    $('tgMap').click();
    await sleep(200);
    const w1 = $('ov').getBoundingClientRect().width;
    check('지도 접기 → 보기 넓어짐', $('side').classList.contains('hidden') && w1 > w0 + 300 && $('ov').width === Math.round(w1 * Math.min(devicePixelRatio || 1, 2)), w0 + ' → ' + w1 + ' (canvas ' + $('ov').width + ')');
    $('tgMap').click();
    await sleep(200);
    check('지도 다시 펼침', !$('side').classList.contains('hidden'));

    // 지도 키: 가짜 키 → VWorld 로 바뀌고, 타일이 거절되면 기본 바탕(웹 = OSM · file:// = 바탕 없음 + 안내)으로 돌아오며 '지도 키 확인'
    const attr = () => $('attrib').textContent;
    const FILE = location.protocol === 'file:';
    const note = () => $('mapNote').classList.contains('show') && getComputedStyle($('mapNote')).display !== 'none';
    const isDefault = () => (FILE ? note() && attr() === '' : attr().indexOf('OpenStreetMap') >= 0 && !note());
    check('처음 바탕 = 기본', isDefault(), attr() + ' / 안내 ' + note());
    check('안내 글', $('mapNote').textContent === '지도: 웹 공유 주소에서 보임 · 또는 지도 키', $('mapNote').textContent);
    $('keyBtn').click();
    check('키 상자 열림', $('keyBox').classList.contains('show'));
    $('keyIn').value = '  TESTKEY-NOT-REAL  ';
    $('keyOk').click();
    check('키 저장 (앞뒤 빈칸 뺌)', localStorage.getItem('rv_vworld_key') === 'TESTKEY-NOT-REAL', localStorage.getItem('rv_vworld_key'));
    check('VWorld 바탕', attr().indexOf('VWorld') >= 0 && $('keyBtn').classList.contains('on') && !$('keyBox').classList.contains('show') && !note(), attr());
    check('VWorld → 이름 단추 보임 (꺼짐)', shown($('mNames')) && !$('mNames').classList.contains('on'));
    await until(() => $('toast').textContent.indexOf('지도 키 확인') >= 0, 15000);
    check('키 거절 → 기본 바탕 + 알림', $('toast').textContent.indexOf('지도 키 확인') >= 0 && isDefault() && !$('keyBtn').classList.contains('on'),
      $('toast').textContent + ' / ' + attr() + ' / 안내 ' + note());
    check('기본 바탕 → 이름 단추 숨김', !shown($('mNames')));
    check('키는 남아 있음', localStorage.getItem('rv_vworld_key') === 'TESTKEY-NOT-REAL');
    $('keyBtn').click();
    check('상자에 저장된 키', $('keyIn').value === 'TESTKEY-NOT-REAL', $('keyIn').value);
    $('keyClear').click();
    check('키 지움', localStorage.getItem('rv_vworld_key') === null && isDefault(), String(localStorage.getItem('rv_vworld_key')) + ' / ' + attr());
  }

  if (T === 'drag') {
    // 휠(줌) 한 번 + 오른쪽으로 200 px 끌기. 결과 화면은 check_interact.py 가 N 글자 자리로 잰다.
    const r = $('ov').getBoundingClientRect();
    $('ov').dispatchEvent(new WheelEvent('wheel', { bubbles: true, cancelable: true, deltaY: -100, deltaMode: 0, clientX: r.left + 300, clientY: r.top + 300 }));
    const x0 = r.left + 300, y0 = r.top + 300;
    ptr($('ov'), 'pointerdown', x0, y0);
    for (let i = 1; i <= 10; i++) ptr($('ov'), 'pointermove', x0 + 20 * i, y0);
    ptr($('ov'), 'pointerup', x0 + 200, y0);
    await sleep(300);
    // 화면 없는 Edge 는 찍기 직전에 창 크기를 바꾼다 → 끌 때의 캔버스 폭을 남긴다
    log('DRAGW', r.width);
    check('drag done', true);
  }

  // ---------------------------------------------------------------- 화살표 = 보는 방향 기준 이동 (make_synth --nav 자료)
  // 자리: 1 (0,0) · 2 (10,0) · 3 (0,10) · 4 (10,10) · 5 (40,0) · 6 (130,0) · 7 = 영상 0002 적힌 자리 (−12,3) → 맞춘 자리 (0,−12)
  const seqNow = () => Number(no().split(' ')[0]);
  const keyDown = (k, opt) => document.dispatchEvent(new KeyboardEvent('keydown', Object.assign({ key: k, bubbles: true, cancelable: true }, opt || {})));
  async function arrow(k, want, label, opt) {
    const before = seqNow();
    $('toast').textContent = '';
    keyDown(k, opt);
    if (want === null || want === 'stay') {
      await sleep(400);
      const t = $('toast').textContent;
      if (want === null) check(label + ' → 그대로 · «그쪽 사진 없음»', seqNow() === before && t === '그쪽 사진 없음', no() + ' / ' + t);
      else check(label + ' → 그대로', seqNow() === before && t === '', no() + ' / ' + t);
    } else {
      await until(() => seqNow() === want && loaded(), 8000);
      check(label + ' → ' + want, seqNow() === want && loaded(), no());
      await sleep(60);
    }
  }
  if (T === 'arrows') { // 보기 = 북 (yaw 0)
    await arrow('ArrowUp', 3, '1 에서 ↑ (북 · 찍은 순서 다음은 2)');
    await arrow('ArrowDown', 1, '3 에서 ↓');
    await arrow('ArrowDown', 7, '1 에서 ↓ (7 의 맞춘 자리가 남쪽)');
    await arrow('ArrowUp', 1, '7 에서 ↑');
    await arrow('ArrowLeft', null, '1 에서 ← (7 의 적힌 자리는 서쪽이지만 맞춘 자리로 봄)');
    await arrow('ArrowRight', 2, '1 에서 →');
    await arrow('ArrowRight', 5, '2 에서 → (30 m)');
    await snap('arrows_at5');
    await arrow('ArrowRight', null, '5 에서 → (6 은 90 m · 60 m 밖)');
    await arrow('ArrowLeft', 2, '5 에서 ←');
    await arrow('PageDown', 3, 'PageDown (찍은 순서)');
    await arrow('PageUp', 2, 'PageUp');
    await arrow('PageUp', 1, 'PageUp');
    await arrow('PageUp', 'stay', '1 에서 PageUp (처음)');
    await arrow('ArrowLeft', 'stay', 'Alt+← (브라우저 뒤로 몫)', { altKey: true });
    $('cover').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true, cancelable: true }));
    await sleep(300);
    check('입력 칸 안 ↑ → 그대로', seqNow() === 1, no());
    // 점 찍어 맞추기 중에도 화살표 · Esc 는 예전대로
    await enterPick();
    await arrow('ArrowUp', 3, '점 찍기 중 1 에서 ↑');
    check('점 찍기 켜진 채', $('ov').classList.contains('pick') && $('alChip').classList.contains('show'));
    const rc = $('ov').getBoundingClientRect();
    tap($('ov'), rc.left + rc.width / 2, rc.top + rc.height * 0.75);
    await sleep(60);
    check('사진 점 찍음 → ②', $('alStep').textContent === '② 지도에서 같은 점', $('alStep').textContent);
    await arrow('ArrowDown', 1, '반쯤 만든 짝이 있어도 ↓');
    esc();
    await sleep(60);
    check('Esc → 짝 취소 · 모드 그대로', $('alStep').textContent === '① 사진에서 점' && $('ov').classList.contains('pick'), $('alStep').textContent);
    esc();
    await sleep(60);
    check('Esc → 점 찍기 끝', !$('ov').classList.contains('pick') && !$('alChip').classList.contains('show'));
  }
  if (T === 'arrows40') { // 보기 yaw 40°: 3 (북 · 각 40° · 10 m → 25.8) 보다 4 (북동 · 각 5° · 14.1 m → 14.5)
    await arrow('ArrowUp', 4, '1 에서 ↑ (yaw 40°)');
    await arrow('ArrowDown', 1, '4 에서 ↓ (남서)');
  }

  // ---------------------------------------------------------------- 지도 [이름] (VWorld 이름 겹). 타일은 check_interact.py 가 가짜로 답함
  async function mark(name) { log('MARK', name); await until(() => window.__markAck === name, 30000); }
  if (T === 'names') {
    const nb = $('mNames');
    check('키 없음 → 이름 단추 숨김', !shown(nb));
    $('keyBtn').click();
    $('keyIn').value = 'TESTKEY-FAKE-TILES';
    $('keyOk').click();
    check('VWorld → 이름 단추 보임 · 기본 꺼짐', shown(nb) && !nb.classList.contains('on') && localStorage.getItem('rv_ui:mapNames') === null);
    await sleep(2000);
    check('가짜 타일 받음 (키 거절 아님)', $('keyBtn').classList.contains('on') && $('toast').textContent.indexOf('지도 키 확인') < 0, $('toast').textContent);
    await mark('off');
    nb.click();
    check('이름 켬 · 기억', nb.classList.contains('on') && localStorage.getItem('rv_ui:mapNames') === 'true', localStorage.getItem('rv_ui:mapNames'));
    await sleep(2000);
    await mark('on');
  }
  if (T === 'names2') { // 같은 브라우저로 다시 열기
    const nb = $('mNames');
    check('다시 열어도 VWorld · 이름 켜짐', shown(nb) && nb.classList.contains('on') && $('keyBtn').classList.contains('on'));
    await sleep(2000);
    await mark('start');
    nb.click();
    check('이름 끔 · 기억', !nb.classList.contains('on') && localStorage.getItem('rv_ui:mapNames') === 'false');
    $('zOut').click(); // 새 줌 → 새 타일: 위성만 청해야 함
    await sleep(2000);
    await mark('off');
  }

  // ---------------------------------------------------------------- 프로그램 (pywebview) 흉내 = hostmock.js
  const hcalls = (name) => window.__host.calls.filter((c) => c.name === name);
  async function drawnTexts() { // 360 겹쳐 그림 캔버스에 쓰는 글자 (다시 그리게 해서 모음)
    const texts = [];
    const fill0 = CanvasRenderingContext2D.prototype.fillText;
    CanvasRenderingContext2D.prototype.fillText = function (t, ...rest) { if (this.canvas && this.canvas.id === 'ov') texts.push(String(t)); return fill0.call(this, t, ...rest); };
    window.dispatchEvent(new Event('resize'));
    await sleep(250);
    CanvasRenderingContext2D.prototype.fillText = fill0;
    return texts;
  }
  function colorCount(id, rgb) { // 캔버스에서 그 색 (±25) 화소 수
    const c = $(id), d = c.getContext('2d').getImageData(0, 0, c.width, c.height).data;
    let n = 0;
    for (let i = 0; i < d.length; i += 4) {
      if (d[i + 3] > 100 && Math.abs(d[i] - rgb[0]) < 25 && Math.abs(d[i + 1] - rgb[1]) < 25 && Math.abs(d[i + 2] - rgb[2]) < 25) n++;
    }
    return n;
  }
  if (T === 'host' || T === 'host_late') {
    // 1126 배치: 윗줄 [처음] · 관로 카테고리 [SHP 불러오기 · 지장물 · SHP 내보내기] · 결과물 · 웹 공유 카테고리 (옛 윗줄 묶음 hostTools · [내보내기] 대신)
    const catEl = (c) => document.querySelector('#opts .cat[data-cat="' + c + '"]');
    if (T === 'host_late') {
      check('준비 전 (api 없음) → 프로그램 단추 숨김', !!window.pywebview && !window.pywebview.api && !shown($('hHome')) && !shown($('hPipes')) && !shown(catEl('web')));
      await until(() => shown($('hHome')), 6000);
    }
    const bName = (b) => b.getAttribute('aria-label') || b.textContent; // 그림 단추 = 이름은 aria-label
    check('프로그램 단추 보임 (처음 · 관로 넣기 · 지장물 넣기 · SHP 내보내기 · 결과물 · 웹 공유)', [['hPipes', '관로 넣기'], ['hObst', '지장물 넣기'], ['hHome', '처음']].every(([id, t]) => shown($(id)) && bName($(id)) === t)
      && shown($('bShpOut')) && shown(catEl('out')) && shown(catEl('web')) && shown($('tgTrim')),
      ['hPipes', 'hObst', 'hHome'].map((id) => bName($(id))).join(','));
  }
  if (T === 'host_late') { // 늦게 붙은 api 로도 맞춤 자동 저장
    $('calHead').click();
    await sleep(50);
    tap($('calRows').querySelectorAll('button')[1], 0, 0); // 동 +
    await sleep(1300);
    check('늦게 붙어도 맞춤 저장 한 번', hcalls('save_calib').length === 1, hcalls('save_calib').length);
  }
  if (T === 'host') {
    const H = window.__host, NEW = [255, 59, 48], OLD = [0, 229, 255];
    const counts = () => ({ map: [colorCount('map', OLD), colorCount('map', NEW)], ov: [colorCount('ov', OLD), colorCount('ov', NEW)] });
    // 1) 넣기 전: 옛 관 (청록 · 상수 / 하수)
    await sleep(300);
    const t0 = await drawnTexts(), c0 = counts();
    check('넣기 전: 옛 관 그림', c0.map[0] > 30 && c0.map[1] === 0 && c0.ov[0] > 30 && c0.ov[1] === 0 && t0.some((t) => /상수|하수/.test(t)), JSON.stringify(c0) + ' ' + JSON.stringify(t0));
    check('처음엔 맞춤 저장 안 부름', hcalls('save_calib').length === 0);
    await snap('host_pipes_old');
    // 2) 창 · 고르기 취소
    $('hPipes').click();
    check('관로 창 열림 · 넣기 막힘 · 깊이 = 지금 값', shown($('pipeDlg')) && !shown($('expDlg')) && $('pmGo').disabled && $('pmDepth').value === '1' && !shown($('pmShp')) && !shown($('pmDxf')), $('pmDepth').value);
    H.picks.push(null);
    $('pmPick').click();
    await until(() => hcalls('pick_pipes').length === 1 && !$('pmPick').disabled);
    check('고르기 취소 → 그대로', $('pmFile').textContent === '' && $('pmGo').disabled);
    H.picks.push({ path: 'C:\\시험\\깨진.shp', kind: '', name: '깨진.shp', fields: [], error: 'dbf 없음' }); // 프로그램이 못 읽은 파일
    $('pmPick').click();
    await until(() => hcalls('pick_pipes').length === 2 && !$('pmPick').disabled);
    check('못 읽은 파일 → 까닭 · 넣기 막힘', $('pmMsg').textContent === '못 읽음 · 깨진.shp · dbf 없음' && $('pmGo').disabled && !shown($('pmShp')) && !shown($('pmDxf')), $('pmMsg').textContent);
    // 3) SHP → 넣기 → data/pipes.js 다시 읽기
    H.picks.push({ path: 'C:\\시험\\관로.shp', kind: 'shp', name: '관로.shp', note: '선 2개', guess_dia: 'DIA',
      fields: [{ name: 'ID', samples: [1, 2, 3] }, { name: 'DIA', samples: [300, 200, 150, 100] }, { name: 'NAME', samples: ['상수', '배수'] }] });
    $('pmPick').click();
    await until(() => $('pmFile').textContent.indexOf('관로.shp') >= 0);
    const opts = (id) => [...$(id).options].map((o) => o.textContent).join(',');
    check('SHP: 항목 · 관경 추정 · 예시', shown($('pmShp')) && !shown($('pmDxf')) && $('pmFile').textContent === '관로.shp · SHP'
      && opts('pmDia') === '(없음),ID,DIA,NAME' && $('pmDia').value === 'DIA' && $('pmDiaEx').textContent === '예: 300, 200, 150'
      && opts('pmLabel') === '(없음),ID,DIA,NAME' && $('pmLabel').value === '' && $('pmNote').textContent === '선 2개' && !$('pmGo').disabled,
      [$('pmFile').textContent, opts('pmDia'), $('pmDia').value, $('pmDiaEx').textContent, $('pmNote').textContent].join(' / '));
    $('pmDia').value = 'ID';
    $('pmDia').dispatchEvent(new Event('change'));
    check('관경 항목 바꿈 → 예시', $('pmDiaEx').textContent === '예: 1, 2, 3', $('pmDiaEx').textContent);
    $('pmDia').value = 'DIA';
    $('pmDia').dispatchEvent(new Event('change'));
    $('pmLabel').value = 'NAME';
    $('pmDepth').value = '1.5';
    await snap('host_pipes_dlg');
    $('pmGo').click();
    check('넣는 중 표시', $('pmGo').disabled && $('pmPick').disabled && $('pmGo').textContent === '넣는 중', $('pmGo').textContent);
    await until(() => !shown($('dlgBack')), 20000);
    await sleep(300);
    const imp = hcalls('import_pipes')[0];
    check('import_pipes 인자', !!imp && JSON.stringify(imp.arg) === JSON.stringify({ path: 'C:\\시험\\관로.shp', dia_field: 'DIA', label_field: 'NAME', depth: 1.5, layers: null }), JSON.stringify(imp && imp.arg));
    check('넣음 → 창 닫힘 · «관로 2개» · 깊이 1.5', $('toast').textContent === '관로 2개' && $('cover').value === '1.5', $('toast').textContent + ' / ' + $('cover').value);
    const t1 = await drawnTexts(), c1 = counts();
    check('새 pipes.js 로 다시 그림 (지도 · 360 · 이름표)', c1.map[0] === 0 && c1.map[1] > 30 && c1.ov[0] === 0 && c1.ov[1] > 30
      && t1.some((t) => t.indexOf('새관') >= 0) && !t1.some((t) => /상수|하수/.test(t)) && window.RV_PIPES.layers[0].name === '새관', JSON.stringify(c1) + ' ' + JSON.stringify(t1));
    check('다시 읽은 script 태그 치움', !document.querySelector('script[src*="pipes.js?v="]'));
    await snap('host_pipes_new');
    $('mDepth').click();
    await sleep(300);
    const cd = counts();
    check('깊이 방식도 새 관', cd.ov[0] === 0 && cd.ov[1] > 0, JSON.stringify(cd));
    await snap('host_pipes_depth');
    $('mSurf').click();
    // 4) DXF · 못 넣음 · 창 열린 동안 화살표 · Esc
    H.picks.push({ path: 'C:\\시험\\도면.dxf', kind: 'dxf', name: '도면.dxf', fields: [], guess_dia: null, note: '' });
    $('hPipes').click();
    check('창 다시 열기 → 고른 파일 · 항목 그대로 · 깊이 = 지금 값', $('pmFile').textContent === '관로.shp · SHP' && $('pmDia').value === 'DIA' && $('pmLabel').value === 'NAME' && $('pmDepth').value === '1.5',
      [$('pmFile').textContent, $('pmDia').value, $('pmLabel').value, $('pmDepth').value].join(' / '));
    $('pmPick').click();
    await until(() => $('pmFile').textContent.indexOf('도면.dxf') >= 0);
    check('DXF: 도면층 칸', shown($('pmDxf')) && !shown($('pmShp')) && $('pmLayers').placeholder === '예: W-*' && $('pmFile').textContent === '도면.dxf · DXF');
    $('pmLayers').value = 'FAIL';
    $('pmGo').click();
    await until(() => $('pmMsg').textContent !== '');
    check('못 넣음 → 창에 까닭 · 관 그대로', $('pmMsg').textContent === '도면층 없음' && shown($('pipeDlg')) && !$('pmGo').disabled && colorCount('map', NEW) > 30
      && hcalls('import_pipes')[1].arg.layers === 'FAIL' && hcalls('import_pipes')[1].arg.dia_field === null, $('pmMsg').textContent);
    const s0 = no();
    keyDown('ArrowUp');
    keyDown('PageDown');
    await sleep(300);
    check('창 열린 동안 화살표 · PgDn 안 먹음', no() === s0 && shown($('pipeDlg')), no());
    keyDown('Escape');
    await sleep(50);
    check('Esc → 창 닫힘', !shown($('dlgBack')));
    // 5) 맞춤 자동 저장: 바뀐 뒤 0.8 초 모아 한 번 · «파일로 저장» 내려받기와 같은 글
    $('calHead').click();
    await sleep(50);
    const plusYaw = $('calRows').querySelectorAll('button')[7];
    for (let i = 0; i < 3; i++) { if (i) await sleep(100); tap(plusYaw, 0, 0); }
    const tLast = performance.now();
    await sleep(400);
    check('0.4 초 뒤엔 아직 안 저장', hcalls('save_calib').length === 0, hcalls('save_calib').length);
    await sleep(900);
    const sv = hcalls('save_calib');
    check('마지막으로 바꾼 지 0.8 초 뒤 한 번 저장', sv.length === 1 && sv[0].t - tLast >= 780 && sv[0].t - tLast < 1100, sv.length + ' · ' + (sv[0] ? Math.round(sv[0].t - tLast) + ' ms' : '-'));
    const pw = window.pywebview;
    window.pywebview = undefined; // 잠깐 프로그램 없는 척 → «파일로 저장» = 내려받기
    const dl = await exportCalib();
    window.pywebview = pw;
    check('저장한 글 = 내려받는 calib.js 글', !!sv[0] && sv[0].arg === dl && dl.indexOf('window.RV_CALIB = ') === 0 && /"dyaw": 0\.3\b/.test(dl), dl.length + ' 글자');
    log('CALIB', JSON.stringify(sv[0] ? sv[0].arg : ''));
    let blobbed = false;
    const oc = URL.createObjectURL;
    URL.createObjectURL = function (b) { blobbed = true; return oc.call(URL, b); };
    $('calSave').click();
    await sleep(200);
    URL.createObjectURL = oc;
    check('프로그램 안 «파일로 저장» → 바로 저장 (내려받기 안 함)', hcalls('save_calib').length === 2 && !blobbed && $('toast').textContent === '맞춤 저장됨', hcalls('save_calib').length + ' / ' + $('toast').textContent);
    // 점 짝이 바뀌어도 한 번 저장
    await enterPick();
    const rc = $('ov').getBoundingClientRect(), mr = $('map').getBoundingClientRect();
    tap($('ov'), rc.left + rc.width / 2, rc.top + rc.height * 0.6);
    await sleep(60);
    tap($('map'), mr.left + mr.width / 2 + 20, mr.top + mr.height / 2 + 10);
    await until(() => $('alList').querySelectorAll('.alRow').length === 1, 3000);
    await sleep(1300);
    const sv3 = hcalls('save_calib');
    check('점 짝 → 한 번 저장 (짝 포함)', sv3.length === 3 && sv3[2].arg.indexOf('"pairs"') >= 0, sv3.length);
    $('alClear').click();
    await sleep(1300);
    const sv4 = hcalls('save_calib');
    check('짝 모두 지움 → 한 번 저장 (짝 없음)', sv4.length === 4 && sv4[3].arg.indexOf('"pairs"') < 0, sv4.length);
    $('alEnd').click();
    // 6) 웹 공유 카테고리: 웹 주소 (진행 → 주소 → 복사 · 열기) · 결과물 카테고리: 폴더
    $('hd_web').click();
    check('웹 공유 카테고리 펼침 (창 없음 · 진행 · 주소 숨김 · 복사 · 열기 막힘)', shown($('exWeb')) && !shown($('dlgBack')) && !shown($('exProg')) && !shown($('exDone'))
      && $('exCopy').disabled && $('exOpen').disabled && $('webState').textContent === '올리기 전', $('webState').textContent);
    const widths = [];
    $('exWeb').click();
    check('올리는 중 → 단추 막힘', $('exWeb').disabled);
    await until(() => { const w = $('exBar').style.width; if (widths[widths.length - 1] !== w) widths.push(w); return shown($('exDone')); }, 8000);
    const jobs = hcalls('job'), gaps = jobs.slice(1).map((c, i) => Math.round(c.t - jobs[i].t));
    check('진행 막대 → 끝 → 주소', ['30%', '60%', '90%'].every((w) => widths.includes(w)) && widths[widths.length - 1] === '100%'
      && $('exUrl').value === 'https://nas.example.invalid/rv/test/' && $('exUrl').readOnly && !$('exWeb').disabled && hcalls('export_web').length === 1,
      widths.join(' → ') + ' / ' + $('exUrl').value);
    check('job 0.5 초마다 · 4 번', jobs.length === 4 && jobs.every((c) => c.arg === 'J1') && gaps.every((g) => g >= 480 && g <= 800), JSON.stringify(gaps));
    await snap('host_export');
    $('exCopy').click();
    await until(() => hcalls('copy_text').length === 1);
    await sleep(50);
    check('주소 복사 → copy_text', hcalls('copy_text')[0].arg === 'https://nas.example.invalid/rv/test/' && $('toast').textContent === '주소 복사됨', $('toast').textContent);
    check('올린 뒤 상태 · 머리줄 «올림» · [다시 올리기] · 열기 됨', $('webState').textContent === '올림' && $('webTag').textContent === '올림'
      && $('exWeb').textContent === '다시 올리기' && !$('exOpen').disabled && !$('exCopy').disabled, $('webState').textContent + ' / ' + $('exWeb').textContent);
    $('hd_out').click();
    check('결과물 카테고리 → [폴더로 내보내기] 보임', shown($('exFolder')));
    $('exFolder').click();
    check('폴더로 내보내는 중', $('exFolder').disabled && $('exFolder').textContent === '내보내는 중');
    await until(() => $('toast').textContent.indexOf('내보냄') === 0, 5000);
    check('폴더로 내보내기 → 알림에 경로', hcalls('export_folder').length === 1 && $('toast').textContent === '내보냄 · C:\\시험\\내보내기' && !$('exFolder').disabled, $('toast').textContent);
    await snap('host_folder');
    $('hd_web').click();
    $('hd_out').click();
    check('머리줄 → 웹 공유 · 결과물 접힘 (창 없음)', !shown($('exWeb')) && !shown($('exFolder')) && !shown($('dlgBack')));
    // 7) [처음]: 아직 안 쓴 맞춤을 먼저 저장 → go_home (두 번 저장 안 함)
    const nSave = hcalls('save_calib').length;
    tap(plusYaw, 0, 0);
    $('hHome').click();
    await until(() => hcalls('go_home').length === 1, 3000);
    const last = H.calls.slice(-2).map((c) => c.name).join(',');
    await sleep(1200);
    check('처음 → 맞춤 먼저 저장 → go_home', last === 'save_calib,go_home' && hcalls('save_calib').length === nSave + 1, last + ' / ' + hcalls('save_calib').length);
  }

  if (T === 'trench_host') { // 프로그램 안: 터파기 값이 바뀌면 0.8 초 뒤 save_calib 한 번 (trench 포함) · 단면 창 칸
    const trOf = (t) => { try { return JSON.parse(t.slice(t.indexOf('{'), t.lastIndexOf('}') + 1)).trench; } catch (e) { return null; } };
    await sleep(300);
    check('처음엔 저장 안 부름', hcalls('save_calib').length === 0);
    $('tgSec').click();
    check('단면 창 열림 · 기본값', shown($('secDlg')) && $('tgSec').classList.contains('on') && $('sec_tp').value === '0.2' && $('sec_n').value === '0.3'
      && $('sec_margin').value === '0.3' && $('sec_bed').value === '0.1' && $('secOn').checked && $('sec_cover').value === $('cover').value,
      ['tp', 'n', 'margin', 'bed', 'cover'].map((k) => $('sec_' + k).value).join(','));
    const n = $('sec_n');
    n.value = '0.5';
    n.dispatchEvent(new Event('input', { bubbles: true }));
    n.dispatchEvent(new Event('change', { bubbles: true }));
    await sleep(400);
    check('0.4 초 뒤엔 아직 안 저장', hcalls('save_calib').length === 0, hcalls('save_calib').length);
    await sleep(900);
    let sv = hcalls('save_calib');
    const t1 = sv[0] && trOf(sv[0].arg);
    check('기울기 0.5 → 저장 한 번 (trench 포함)', sv.length === 1 && !!t1 && t1.n === 0.5 && t1.tp === 0.2 && t1.margin === 0.3 && t1.bed === 0.1 && t1.on === true, sv.length + ' ' + JSON.stringify(t1));
    const pw = window.pywebview;
    window.pywebview = undefined; // 잠깐 프로그램 없는 척 → «파일로 저장» = 내려받기
    const dl = await exportCalib();
    window.pywebview = pw;
    check('저장한 글 = 내려받는 calib.js 글', !!sv[0] && sv[0].arg === dl, dl.length + ' 글자');
    $('secOn').click();
    await sleep(1300);
    sv = hcalls('save_calib');
    check('터파기 끔 → 저장 (on false)', sv.length === 2 && trOf(sv[1].arg).on === false && $('secDlg').classList.contains('off'), sv.length + ' ' + JSON.stringify(sv[1] && trOf(sv[1].arg)));
    $('secDef').click();
    await sleep(1300);
    sv = hcalls('save_calib');
    check('기본값 (SAC) → 저장', sv.length === 3 && JSON.stringify(trOf(sv[2].arg)) === JSON.stringify({ tp: 0.2, n: 0.3, margin: 0.3, bed: 0.1, prot: 0.3, on: true }) && $('sec_n').value === '0.3' && $('secOn').checked,
      JSON.stringify(sv[2] && trOf(sv[2].arg)));
    // 평균매설깊이: 단면 칸 ↔ 윗줄 칸 (깊이는 맞춤 파일에 안 씀)
    const ci = $('sec_cover');
    ci.value = '1.7';
    ci.dispatchEvent(new Event('input', { bubbles: true }));
    check('단면 평균매설깊이 → 윗줄 같이', $('cover').value === '1.7', $('cover').value);
    $('cover').value = '1.1';
    $('cover').dispatchEvent(new Event('input', { bubbles: true }));
    check('윗줄 → 단면 칸 · 그림 같이', ci.value === '1.1' && $('secSvg').innerHTML.indexOf('토피 1.10') >= 0, ci.value);
    tap($('secRows').querySelectorAll('button')[3], 0, 0); // 포장 +
    check('± 단추 (포장 0.2 → 0.25)', $('sec_tp').value === '0.25', $('sec_tp').value);
    await sleep(1300);
    check('± 도 저장', hcalls('save_calib').length === 4 && trOf(hcalls('save_calib')[3].arg).tp === 0.25, hcalls('save_calib').length);
    keyDown('Escape');
    await sleep(50);
    check('Esc → 단면 창 닫힘', !shown($('secDlg')) && !$('tgSec').classList.contains('on'));
    await snap('trench_host_end');
  }

  log('DONE', fails === 0 ? 'ALL-PASS' : fails + ' FAIL');
})();
