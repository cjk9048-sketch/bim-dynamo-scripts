/* 시험 전용: 데스크톱 프로그램 (pywebview · window.pywebview.api) 흉내.
 * check_interact.py 가 시험 폴더의 복사본 html 에만 app.js 앞에 넣는다 (배포 화면에는 없음).
 * window.__host.calls = [{ name, arg, t }] 부른 차례 · window.__host.picks = pick_pipes 가 차례로 돌려줄 답.
 * save_pipes (관로 고치기 저장) · export_pipes_shp: window.__host.shps 가 차례로 돌려줄 답 (없으면 저장함 · 경로).
 * trim_shots (사진점 지우기): window.__host.trims 가 차례로 돌려줄 답 (없으면 {ok, removed, msg}).
 * import_pipes 는 파이썬 시험기에 'SELFTEST HOSTCMD write_pipes' 를 보내 data/pipes.js 를 새 자료로 바꿔 쓰게 하고,
 * 다 썼다는 답 (window.__hostAck.write_pipes) 을 기다린다 (진짜 프로그램처럼 파일을 쓴 뒤 돌아옴).
 * pick_obstacles: window.__host.opicks 가 차례로 돌려줄 답 · import_obstacles: type_field 'FAIL' → 못 넣음,
 * 아니면 'SELFTEST HOSTCMD write_obstacles' 로 시험기가 data/obstacles.js 를 쓰게 하고 답을 기다린다.
 * ?late=1 → 처음엔 api 없음, 1.5 초 뒤 api 를 채우고 'pywebviewready'. */
(function () {
  'use strict';
  const Q = new URLSearchParams(location.search);
  const H = window.__host = { calls: [], picks: [], opicks: [], shps: [], trims: [], jobs: 0 };
  const rec = (name, arg) => { H.calls.push({ name: name, arg: arg, t: performance.now() }); };
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  function ask(cmd) {
    window.__hostAck = window.__hostAck || {};
    console.log('SELFTEST HOSTCMD ' + cmd);
    return new Promise((res) => {
      const t = setInterval(() => { if (window.__hostAck[cmd]) { clearInterval(t); res(window.__hostAck[cmd]); } }, 40);
    });
  }
  const api = {
    pick_pipes: () => { rec('pick_pipes'); return wait(30).then(() => (H.picks.length ? H.picks.shift() : null)); },
    import_pipes: (o) => {
      rec('import_pipes', o);
      if (o && o.layers === 'FAIL') return wait(30).then(() => ({ ok: false, msg: '도면층 없음' }));
      return ask('write_pipes').then(() => ({ ok: true, lines: 2, note: '' }));
    },
    pick_obstacles: () => { rec('pick_obstacles'); return wait(30).then(() => (H.opicks.length ? H.opicks.shift() : null)); },
    import_obstacles: (o) => {
      rec('import_obstacles', o);
      if (o && o.type_field === 'FAIL') return wait(30).then(() => ({ ok: false, msg: '종류 항목 없음' }));
      return ask('write_obstacles').then(() => ({ ok: true, lines: 2, note: '' }));
    },
    save_calib: (text) => { rec('save_calib', text); return wait(10).then(() => ({ ok: true })); },
    save_pipes: (text) => { rec('save_pipes', text); return wait(10).then(() => ({ ok: true })); },
    trim_shots: (ids) => {
      rec('trim_shots', ids);
      return wait(20).then(() => (H.trims.length ? H.trims.shift() : { ok: true, removed: (ids || []).length, msg: '사진 ' + (ids || []).length + '개 지움' }));
    },
    export_pipes_shp: () => {
      rec('export_pipes_shp');
      return wait(30).then(() => (H.shps.length ? H.shps.shift() : { ok: true, path: 'C:\\시험\\관로.shp', lines: 3 }));
    },
    export_web: () => { rec('export_web'); H.jobs = 0; return wait(20).then(() => ({ job: 'J1' })); },
    // 3D 보기 (오른쪽 메뉴 · 결과물 다듬기): 잘라내기 · DEM 은 다시 만든 뒤 화면을 다시 여니 부른 값을 sessionStorage 에 남김
    crop_get: () => { rec('crop_get'); return wait(10).then(() => ({ polygon: H.crop || null })); },
    crop_apply: (p) => { rec('crop_apply', p); sessionStorage.setItem('__crop_apply', JSON.stringify(p)); return wait(20).then(() => ({ ok: true, job: 'C1' })); },
    crop_clear: () => { rec('crop_clear'); sessionStorage.setItem('__crop_clear', '1'); return wait(20).then(() => ({ ok: true, job: 'C2' })); },
    add_outputs: (a) => { rec('add_outputs', a); sessionStorage.setItem('__add_outputs', JSON.stringify(a)); return wait(20).then(() => ({ ok: true, job: 'D1' })); },
    // 땅 아님 (맨땅 · DTM): 목록 = sessionStorage '__ng' (다시 열어도 남게 · 진짜 프로그램은 data/nonground.json) · 바꾸면 다시 만듦 (job N1)
    nonground_get: () => { rec('nonground_get'); return wait(10).then(() => ({ polygons: JSON.parse(sessionStorage.getItem('__ng') || '[]') })); },
    nonground_set: (p) => {
      rec('nonground_set', p);
      sessionStorage.setItem('__nonground_set', JSON.stringify(p));
      sessionStorage.setItem('__ng', JSON.stringify((p && p.polygons) || []));
      return wait(20).then(() => ({ ok: true, job: 'N1' }));
    },
    open_results: () => { rec('open_results'); return wait(10).then(() => ({ ok: true })); },
    pick_checkpoints: () => { rec('pick_checkpoints'); return wait(20).then(() => (H.ckpicks && H.ckpicks.length ? H.ckpicks.shift() : null)); },
    save_image: (p) => { rec('save_image', { name: p && p.name, head: String((p && p.data) || '').slice(0, 22), len: String((p && p.data) || '').length }); return wait(20).then(() => ({ ok: true, path: 'C:\\시험\\' + (p && p.name) })); },
    // 원본 사진: H.photos (없으면 세 장) · photo_view = 그 이름 색 그림 (800×600 · 확대면 400×300 둘레) + 표시 자리
    photos_at: (E, N, Z) => {
      rec('photos_at', [E, N, Z]);
      return wait(20).then(() => (H.photos === null ? { ok: false, msg: '사진 폴더 없음' }
        : { ok: true, photos: H.photos || [['P1.JPG', 400, 300], ['P2.JPG', 120, 500], ['P3.JPG', 700, 80]].map(([n, u, v], i) => ({ name: n, u: u, v: v, w: 800, h: 600, score: 0.2 + i * 0.1 })) }));
    },
    photo_view: (name, u, v, zoom) => {
      rec('photo_view', [name, u, v, !!zoom]);
      const c = document.createElement('canvas'), W = zoom ? 400 : 800, Hh = zoom ? 300 : 600, g = c.getContext('2d');
      c.width = W; c.height = Hh;
      g.fillStyle = { 'P1.JPG': '#7f1d1d', 'P2.JPG': '#14532d', 'P3.JPG': '#1e3a8a' }[name] || '#444'; g.fillRect(0, 0, W, Hh);
      const x = zoom ? 200 : u, y = zoom ? 150 : v;
      return wait(20).then(() => ({ ok: true, name: name, url: c.toDataURL('image/png'), w: W, h: Hh, x: x, y: y, zoom: !!zoom }));
    },
    job: (id) => {
      rec('job', id);
      if (id !== 'J1') {                          // 잘라내기 · DEM: 두 번 뒤 끝 (주소 없음)
        H.jp = H.jp || {};
        H.jp[id] = (H.jp[id] || 0) + 1;
        return wait(10).then(() => (H.jp[id] >= 3 ? { state: 'done', pct: 100, line: '끝' } : { state: 'run', pct: H.jp[id] * 40, line: '다시 만드는 중 ' + H.jp[id] * 40 + '%' }));
      }
      const pct = Math.min(100, ++H.jobs * 30);
      return wait(10).then(() => (pct >= 100
        ? { state: 'done', pct: 100, line: '끝', url: 'https://nas.example.invalid/rv/test/' }
        : { state: 'run', pct: pct, line: '올리는 중 ' + pct + '%' }));
    },
    export_folder: () => { rec('export_folder'); return wait(150).then(() => ({ ok: true, path: 'C:\\시험\\내보내기' })); },
    copy_text: (text) => { rec('copy_text', text); return wait(10).then(() => ({ ok: true })); },
    go_home: () => { rec('go_home'); return wait(10).then(() => null); },
  };
  if (Q.get('late') === '1') {
    window.pywebview = {}; // pywebview 는 준비가 끝난 뒤 api 를 채우고 'pywebviewready' 를 쏜다
    setTimeout(() => { window.pywebview.api = api; window.dispatchEvent(new Event('pywebviewready')); }, 1500);
  } else {
    window.pywebview = { api: api };
  }
})();
