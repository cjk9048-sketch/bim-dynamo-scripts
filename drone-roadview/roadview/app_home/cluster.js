/* 드론 로드뷰 — 함께 처리 (JACK 1126 · 옆 PC 와 3D 매핑 점구름을 나눠 처리 · 공유 폴더 = 작업 게시판 · cluster.py)
 * 왼쪽 «함께 처리» 화면: 공유 폴더 · [함께 처리] (내 매핑에 씀) · [도우미 모드] (이 PC 가 일감을 받음) · 도우미 목록
 * 처음 화면 [도우미 모드] 단추 · 매핑 화면 «함께 처리» 줄 (도우미 몇 대)
 */
(function () {
  'use strict';
  const $ = (id) => document.getElementById(id);
  const call = (...a) => window.RV.call(...a);
  const toast = (t) => window.RV.toast(t);
  const C = { cfg: null, me: { state: 'off' } };
  const shown = (id) => { const e = $(id); return !!(e && !e.closest('.hide')); };

  function meText(m) {
    if (!m || m.state === 'off') return '';
    if (m.state === 'busy') return '일감 ' + (m.job || '') + ' · ' + Math.round(m.pct || 0) + '% · 처리 ' + (m.done || 0);
    if (m.state === 'install') return m.msg || '엔진 준비';
    return '기다리는 중 · 처리 ' + (m.done || 0);
  }
  function paintMe(m) {
    C.me = m || { state: 'off' };
    const on = C.me.state !== 'off';
    for (const id of ['bHelper', 'cHelp']) { const b = $(id); if (b) b.classList.toggle('on', on); }
    const b = $('bHelper');
    if (b) b.textContent = on ? '도우미 · ' + (C.me.state === 'busy' ? Math.round(C.me.pct || 0) + '%' : '처리 ' + (C.me.done || 0)) : '도우미 모드';
    const s = $('cMe'); if (s) s.textContent = meText(C.me);
  }
  function paint(c) {
    if (!c) return;
    C.cfg = c; paintMe(c.me);
    $('cBoard').value = c.board || '';
    $('cUse').classList.toggle('on', !!c.use);
    $('cUseSub').textContent = !c.board ? '공유 폴더 먼저' : (c.board_ok ? '' : (c.why || '공유 폴더 안 됨'));
    const L = (c.helpers || []);
    const alive = L.filter((h) => h.alive);
    $('cCnt').textContent = '(' + alive.length + ')';
    const box = $('cList'); box.textContent = '';
    $('cEmpty').classList.toggle('hide', alive.length > 0 || !c.board_ok);
    for (const h of L) {
      const d = document.createElement('div');
      d.className = 'share' + (h.alive ? '' : ' gone');
      d.innerHTML = '<div class="t"></div><div class="u"></div>';
      d.querySelector('.t').textContent = h.host || '?';
      d.querySelector('.u').textContent = (h.cores || '?') + '코어 · ' + (h.ram_gb || '?') + ' GB · ' + (h.alive ? meText(h) : '꺼짐');
      box.appendChild(d);
    }
    mapRow();
  }
  // 매핑 화면 «함께 처리» 줄: 켜짐 · 도우미 몇 대 (내 PC 와 같은 PC 도우미는 안 셈)
  function mapRow() {
    const b = $('mClu'), s = $('mCluSub'), c = C.cfg;
    if (!b || !c) return;
    const on = !!(c.use && c.board);
    b.classList.toggle('on', on); b.textContent = on ? '켜짐' : '꺼짐';
    s.textContent = !c.board ? '공유 폴더 없음 · 처음 화면 «함께 처리»' : (on ? (c.board_ok ? '도우미 ' + (c.workers || 0) + '대' + (c.workers ? '' : ' · 혼자 처리') : '공유 폴더 안 됨 · 혼자 처리') : '');
    if (window.RVMap && window.RVMap._est) window.RVMap._est();      // 도우미 수가 바뀌면 예상 시간도
  }
  async function refresh() { paint(await call('cluster_get')); }
  async function tick() {
    if (shown('pageCluster') || shown('mp')) await refresh();
    else if (shown('pageHome')) paintMe(await call('helper_status'));
  }
  async function toggleHelper() {
    const on = C.me.state === 'off';
    const r = await call('helper_toggle', on);
    if (r && !r.ok) { toast(r.msg); if (!shown('pageCluster') && window.RV.page) window.RV.page('cluster'); return; }
    if (r) paintMe(r.me);
    toast(on ? '도우미 모드 켬 · 이 PC 가 일감을 받음' : '도우미 모드 끔');
  }
  function init() {
    $('cPick').addEventListener('click', async () => { const c = await call('cluster_pick'); if (c) paint(c); });
    $('cUse').addEventListener('click', async () => {
      if (!(C.cfg && C.cfg.board)) { toast('공유 폴더 먼저'); return; }
      paint(await call('cluster_set', { use: !C.cfg.use }));
    });
    $('cHelp').addEventListener('click', toggleHelper);
    $('bHelper').addEventListener('click', toggleHelper);
    const mb = $('mClu');
    if (mb) mb.addEventListener('click', async () => {
      if (!(C.cfg && C.cfg.board)) { toast('처음 화면 왼쪽 «함께 처리» 에서 공유 폴더 고르기'); return; }
      paint(await call('cluster_set', { use: !C.cfg.use }));
    });
    refresh();
    let busy = false;                                                    // 공유 폴더가 느리거나 끊겨도 앞 호출이 끝나기 전엔 다음을 안 부름 (1126 검토)
    setInterval(async () => { if (busy) return; busy = true; try { await tick(); } catch (e) { /* 다음에 다시 */ } finally { busy = false; } }, 4000);
  }
  // 매핑 시간 어림용: 점구름을 나눌 PC 수 (내 PC + 도우미 · 꺼져 있으면 1)
  const workers = () => (C.cfg && C.cfg.use && C.cfg.board_ok ? 1 + (C.cfg.workers || 0) : 1);
  window.RVCluster = { init, show: refresh, workers };
})();
