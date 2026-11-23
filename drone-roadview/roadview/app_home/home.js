/* 드론 로드뷰 — 첫 화면 (프로젝트 목록 · 새로 만들기). 파이썬 쪽 = window.pywebview.api */
(function () {
  'use strict';
  const $ = (id) => document.getElementById(id);
  let api = null, files = [], job = null, timer = 0;

  function toast(t) { const e = $('toast'); e.textContent = t; e.classList.add('show'); clearTimeout(e._t); e._t = setTimeout(() => e.classList.remove('show'), 2600); }
  function ready(fn) {
    if (window.pywebview && window.pywebview.api) { fn(); return; }
    window.addEventListener('pywebviewready', fn, { once: true });
  }

  async function loadList() {
    const L = await api.list_projects();
    const box = $('list');
    box.textContent = '';
    $('empty').classList.toggle('hide', L.length > 0);
    for (const x of L) {
      const d = document.createElement('div');
      d.className = 'item' + (x.exists ? '' : ' gone');
      d.innerHTML = '<div class="t"></div><div class="p"></div><div class="m"></div><button class="rm" title="목록에서 빼기">×</button>';
      d.querySelector('.t').textContent = x.title || '(이름 없음)';
      d.querySelector('.p').textContent = x.path;
      d.querySelector('.m').textContent = (x.exists ? '' : '폴더 없음 · ') + (x.shots ? '촬영점 ' + x.shots : '') + (x.videos ? ' · 영상 ' + x.videos : '') + ' · ' + (x.updated || '');
      d.addEventListener('click', async (e) => {
        if (e.target.classList.contains('rm')) return;
        if (!x.exists) { toast('폴더 없음'); return; }
        const r = await api.open_project(x.path);
        if (r && !r.ok) toast(r.msg);
      });
      d.querySelector('.rm').addEventListener('click', async (e) => { e.stopPropagation(); await api.remove_project(x.path); loadList(); });
      box.appendChild(d);
    }
  }

  function setBusy(b) {
    $('bBuild').disabled = b; $('bStop').disabled = !b; $('bPick').disabled = b;
  }

  async function poll() {
    if (!job) return;
    const j = await api.job(job);
    $('bar').style.width = (j.pct || 0) + '%';
    $('status').textContent = j.line || '';
    const log = $('log');
    log.classList.remove('hide');
    log.textContent = (j.log || []).join('\n');
    log.scrollTop = log.scrollHeight;
    if (j.state === 'run') { timer = setTimeout(poll, 700); return; }
    setBusy(false);
    if (j.state === 'done') {
      $('status').textContent = '끝 · 여는 중…';
      const r = await api.open_project(j.out);
      if (r && !r.ok) toast(r.msg);
    }
    job = null;
  }

  ready(async () => {
    api = window.pywebview.api;
    const info = await api.app_info();
    $('ver').textContent = 'v' + info.version;
    loadList();

    $('bNew').addEventListener('click', () => $('newPane').classList.remove('hide'));
    $('bNewClose').addEventListener('click', () => { if (!job) $('newPane').classList.add('hide'); });
    $('bOpen').addEventListener('click', async () => { const r = await api.pick_open(); if (r && !r.ok) toast(r.msg); });
    $('bPick').addEventListener('click', async () => {
      const r = await api.pick_inputs();
      if (!r) return;
      files = r.files;
      $('pickInfo').textContent = (r.osv ? '영상 ' + r.osv + '개' : '') + (r.osv && r.jpg ? ' · ' : '') + (r.jpg ? '사진 ' + r.jpg + '장' : '') + ' · ' + r.size_gb + ' GB · ' + r.dir;
      if (!$('out').value) $('out').value = r.out;
      if (!$('title').value) $('title').value = r.dir.split(/[\\/]/).pop();
      $('status').textContent = '간격을 넣고 [만들기]';
    });
    $('bOut').addEventListener('click', async () => { const d = await api.pick_folder(); if (d) $('out').value = d; });
    $('bBuild').addEventListener('click', async () => {
      if (!files.length) { toast('영상을 고르세요'); return; }
      const r = await api.start_build({ files, out: $('out').value, interval: Number($('gap').value), title: $('title').value,
        width: Number($('width').value), dem: $('dem').checked, sat: $('sat').checked });
      if (!r.ok) { toast(r.msg); return; }
      job = r.job; setBusy(true); $('bar').style.width = '0%'; poll();
    });
    $('bStop').addEventListener('click', async () => { if (job) await api.stop_job(job); });
  });
})();
