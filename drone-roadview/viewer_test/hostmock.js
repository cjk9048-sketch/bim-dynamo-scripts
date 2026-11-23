/* 시험 전용: 데스크톱 프로그램 (pywebview · window.pywebview.api) 흉내.
 * check_interact.py 가 시험 폴더의 복사본 html 에만 app.js 앞에 넣는다 (배포 화면에는 없음).
 * window.__host.calls = [{ name, arg, t }] 부른 차례 · window.__host.picks = pick_pipes 가 차례로 돌려줄 답.
 * save_pipes (관로 고치기 저장) · export_pipes_shp: window.__host.shps 가 차례로 돌려줄 답 (없으면 저장함 · 경로).
 * import_pipes 는 파이썬 시험기에 'SELFTEST HOSTCMD write_pipes' 를 보내 data/pipes.js 를 새 자료로 바꿔 쓰게 하고,
 * 다 썼다는 답 (window.__hostAck.write_pipes) 을 기다린다 (진짜 프로그램처럼 파일을 쓴 뒤 돌아옴).
 * pick_obstacles: window.__host.opicks 가 차례로 돌려줄 답 · import_obstacles: type_field 'FAIL' → 못 넣음,
 * 아니면 'SELFTEST HOSTCMD write_obstacles' 로 시험기가 data/obstacles.js 를 쓰게 하고 답을 기다린다.
 * ?late=1 → 처음엔 api 없음, 1.5 초 뒤 api 를 채우고 'pywebviewready'. */
(function () {
  'use strict';
  const Q = new URLSearchParams(location.search);
  const H = window.__host = { calls: [], picks: [], opicks: [], shps: [], jobs: 0 };
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
    export_pipes_shp: () => {
      rec('export_pipes_shp');
      return wait(30).then(() => (H.shps.length ? H.shps.shift() : { ok: true, path: 'C:\\시험\\관로.shp', lines: 3 }));
    },
    export_web: () => { rec('export_web'); H.jobs = 0; return wait(20).then(() => ({ job: 'J1' })); },
    job: (id) => {
      rec('job', id);
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
