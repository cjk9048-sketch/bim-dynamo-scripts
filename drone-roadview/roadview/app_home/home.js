/* 드론 로드뷰 — 첫 화면 (홈 · 웹 공유 · 프로젝트 생성). 파이썬 쪽 = window.pywebview.api */
(function () {
  'use strict';
  const $ = (id) => document.getElementById(id);
  let api = null, files = [], job = null, timer = 0;

  function toast(t) { const e = $('toast'); e.textContent = t; e.classList.add('show'); clearTimeout(e._t); e._t = setTimeout(() => e.classList.remove('show'), 2600); }
  // 파이썬 쪽에서 오류가 나면 조용히 멈추지 않고 알림으로 (1123: 파일 창이 «아무 반응 없음»이었음)
  async function call(name, ...args) {
    try { return await api[name](...args); } catch (e) { toast('오류 · ' + String((e && e.message) || e).slice(0, 120)); return null; }
  }
  function ready(fn) {
    if (window.pywebview && window.pywebview.api) { fn(); return; }
    window.addEventListener('pywebviewready', fn, { once: true });
  }
  const fmtDate = (s) => (s || '').replace(/^\d{4}-/, '').replace('-', '.');

  // ------------------------------------------------------------ 메뉴
  function showPage(p) {
    document.querySelectorAll('.nav').forEach((a) => a.classList.toggle('on', a.dataset.page === p));
    $('pageHome').classList.toggle('hide', p !== 'home');
    $('pageShare').classList.toggle('hide', p !== 'share');
    if (p === 'home') loadList(); else loadShares();
  }

  // ------------------------------------------------------------ 최근 프로젝트
  async function loadList() {
    const L = (await call('list_projects')) || [];
    const box = $('grid');
    box.textContent = '';
    $('cnt').textContent = '(' + L.length + ')';
    $('empty').classList.toggle('hide', L.length > 0);
    for (const x of L) {
      const d = document.createElement('div');
      d.className = 'card' + (x.exists ? '' : ' gone');
      d.innerHTML = '<div class="thumb"></div><div class="t"></div><div class="m"></div><button class="rm" title="목록에서 빼기">×</button>';
      const th = d.querySelector('.thumb');
      const bg = (u) => 'url("' + String(u).replace(/"/g, '%22') + '")';
      if (x.thumb_url) th.style.backgroundImage = bg(x.thumb_url);
      // 컷: 마우스를 올리면 그 프로젝트의 여러 장면이 넘어감
      const cuts = (x.cut_urls || []).filter(Boolean);
      if (cuts.length > 1) {
        let k = 0, iv = 0;
        d.addEventListener('mouseenter', () => { iv = setInterval(() => { k = (k + 1) % cuts.length; th.style.backgroundImage = bg(cuts[k]); }, 650); });
        d.addEventListener('mouseleave', () => { clearInterval(iv); k = 0; if (x.thumb_url) th.style.backgroundImage = bg(x.thumb_url); });
        cuts.forEach((u) => { const im = new Image(); im.src = u; });
      }
      d.querySelector('.t').textContent = x.title || '(이름 없음)';
      d.querySelector('.m').textContent = (x.exists ? '' : '폴더 없음 · ') + (x.size_gb != null ? x.size_gb + ' GB · ' : '') + (x.shots ? x.shots + '곳 · ' : '') + fmtDate(x.updated);
      d.title = x.path;
      d.addEventListener('click', async (e) => {
        if (e.target.classList.contains('rm')) return;
        if (!x.exists) { toast('폴더 없음'); return; }
        const r = await call('open_project', x.path);
        if (r && !r.ok) toast(r.msg);
      });
      d.querySelector('.rm').addEventListener('click', async (e) => { e.stopPropagation(); await call('remove_project', x.path); loadList(); });
      box.appendChild(d);
    }
  }

  // ------------------------------------------------------------ 웹 공유
  async function loadShares() {
    const L = (await call('list_shares')) || [];
    const box = $('shares');
    box.textContent = '';
    $('scnt').textContent = '(' + L.length + ')';
    $('sempty').classList.toggle('hide', L.length > 0);
    for (const x of L) {
      const d = document.createElement('div');
      d.className = 'share';
      d.innerHTML = '<div class="t"></div><div class="u"></div><button class="mini c">주소 복사</button><button class="mini o">열기</button>';
      d.querySelector('.t').textContent = x.title;
      d.querySelector('.u').textContent = x.url;
      d.querySelector('.c').addEventListener('click', async () => { await call('copy_text', x.url); toast('주소 복사함'); });
      d.querySelector('.o').addEventListener('click', () => call('open_url', x.url));
      box.appendChild(d);
    }
  }

  // ------------------------------------------------------------ 프로젝트 생성
  function openDlg() { $('dlg').classList.remove('hide'); }
  function setBusy(b) {
    $('bBuild').disabled = b; $('bStop').disabled = !b; $('bPick').style.pointerEvents = b ? 'none' : '';
    $('bHide').classList.toggle('hide', !b);
  }
  async function poll() {
    if (!job) return;
    const j = (await call('job', job)) || { state: 'fail', line: '작업 상태를 못 읽음' };
    $('bar').style.width = (j.pct || 0) + '%';
    $('status').textContent = j.line || '';
    $('jobPillText').textContent = Math.round(j.pct || 0) + '% · ' + (j.pill || j.line || '만드는 중').slice(0, 40);
    const log = $('log');
    log.classList.remove('hide');
    log.textContent = (j.log || []).join('\n');
    log.scrollTop = log.scrollHeight;
    if (j.state === 'run') { timer = setTimeout(poll, 700); return; }
    setBusy(false);
    $('jobPill').classList.add('hide');
    if (j.state === 'done') {
      $('status').textContent = '끝 · 여는 중…';
      const r = await call('open_project', j.out);
      if (r && !r.ok) toast(r.msg);
    }
    job = null;
  }

  ready(async () => {
    api = window.pywebview.api;
    const info = (await call('app_info')) || {};
    $('ver').textContent = 'v' + (info.version || '');
    document.querySelectorAll('.nav').forEach((a) => a.addEventListener('click', () => showPage(a.dataset.page)));
    showPage('home');

    $('bCreate').addEventListener('click', openDlg);
    $('bClose').addEventListener('click', () => { $('dlg').classList.add('hide'); if (job) $('jobPill').classList.remove('hide'); });
    $('bHide').addEventListener('click', () => { $('dlg').classList.add('hide'); $('jobPill').classList.remove('hide'); });
    $('jobPill').addEventListener('click', () => { $('jobPill').classList.add('hide'); openDlg(); });
    $('bOpen').addEventListener('click', async () => { const r = await call('pick_open'); if (r && !r.ok) toast(r.msg); });
    $('bPick').addEventListener('click', async () => {
      const r = await call('pick_inputs');
      if (!r) return;
      files = r.files;
      $('pickInfo').textContent = (r.osv ? '영상 ' + r.osv + '개' : '') + (r.osv && r.jpg ? ' · ' : '') + (r.jpg ? '사진 ' + r.jpg + '장' : '') + ' · ' + r.size_gb + ' GB · ' + r.dir;
      $('out').value = r.out;
      if (!$('title').value) $('title').value = r.out.split(/[\\/]/).pop().replace(/^로드뷰_/, '');
      $('status').textContent = '간격을 넣고 [만들기]';
    });
    $('bOut').addEventListener('click', async () => { const d = await call('pick_folder'); if (d) $('out').value = d; });
    $('bBuild').addEventListener('click', async () => {
      if (!files.length) { toast('영상을 고르세요'); return; }
      const r = await call('start_build', { files, out: $('out').value, interval: Number($('gap').value), title: $('title').value,
        width: Number($('width').value), dem: $('dem').checked, sat: $('sat').checked });
      if (!r || !r.ok) { if (r) toast(r.msg); return; }
      job = r.job; setBusy(true); $('bar').style.width = '0%'; poll();
    });
    $('bStop').addEventListener('click', async () => { if (job) await call('stop_job', job); });
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && !$('dlg').classList.contains('hide') && !job) $('dlg').classList.add('hide'); });
  });
})();
