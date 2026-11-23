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

  if (T === 'marker') {
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
