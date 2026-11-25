/* 드론 로드뷰 — 첫 화면 (홈 · 웹 공유). 프로젝트 생성 = 편집 화면(edit.js). 파이썬 쪽 = window.pywebview.api */
(function () {
  'use strict';
  const $ = (id) => document.getElementById(id);
  let api = null;

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
  window.RV = { call, toast, $ };

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

  ready(async () => {
    api = window.pywebview.api;
    const info = (await call('app_info')) || {};
    $('ver').textContent = 'v' + (info.version || '');
    document.querySelectorAll('.nav').forEach((a) => a.addEventListener('click', () => showPage(a.dataset.page)));
    showPage('home');
    $('bOpen').addEventListener('click', async () => { const r = await call('pick_open'); if (r && !r.ok) toast(r.msg); });
    $('bCreate').addEventListener('click', () => window.RVEdit && window.RVEdit.open());
    $('jobPill').addEventListener('click', () => {
      if (window.RVMap && window.RVMap._S.job) window.RVMap.open(); else if (window.RVEdit) window.RVEdit.open();
    });
    $('bMapping').addEventListener('click', () => window.RVMap && window.RVMap.open());
    if (window.RVEdit) window.RVEdit.init(info);
    if (window.RVMap) window.RVMap.init(info);
  });
})();
