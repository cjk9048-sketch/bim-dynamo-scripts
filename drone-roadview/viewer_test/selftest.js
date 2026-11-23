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
    for (let k = 3; k <= N; k++) await key('ArrowRight', k);
    check('next disabled at end', $('next').disabled === true);
    $('prev').click();
    await until(() => is(N - 1));
    check('prev button → ' + (N - 1), is(N - 1), no());
    for (let k = N - 2; k >= 1; k--) await key('ArrowLeft', k);
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
      document.dispatchEvent(new KeyboardEvent('keydown', { key: cur() < seq ? 'ArrowRight' : 'ArrowLeft', bubbles: true }));
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
    await until(() => $('toast').textContent.indexOf('지도 키 확인') >= 0, 15000);
    check('키 거절 → 기본 바탕 + 알림', $('toast').textContent.indexOf('지도 키 확인') >= 0 && isDefault() && !$('keyBtn').classList.contains('on'),
      $('toast').textContent + ' / ' + attr() + ' / 안내 ' + note());
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

  log('DONE', fails === 0 ? 'ALL-PASS' : fails + ' FAIL');
})();
