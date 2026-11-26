/* 오른쪽 칸 폭 끌어 바꾸기 (JACK 1126 «오른쪽 옵션 창을 좌우로 크기 조정») — 편집 화면(#ed) · 매핑 화면(#mp)
 * aside.eside 의 왼쪽 가장자리 손잡이를 끌면 폭이 바뀜 · 화면마다 기억 · 두 번 누르면 처음 폭(330 px)
 * 틀(grid)의 오른쪽 칸 폭 = CSS 변수 --sideW (edit.css)
 */
(function () {
  'use strict';
  const MIN = 260, KEY = 'rv_ui:sideW:';
  const maxW = () => Math.max(MIN, Math.round(window.innerWidth * 0.6));
  const ls = (f) => { try { return f(); } catch (e) { return null; } };   // 저장소가 막혀도 화면은 그대로 (1126 검토)
  function setup(box) {
    const aside = box.querySelector(':scope > aside.eside');
    if (!aside) return;
    const key = KEY + box.id;
    const apply = (w) => box.style.setProperty('--sideW', Math.round(Math.max(MIN, Math.min(w, maxW()))) + 'px');
    const saved = Number(ls(() => localStorage.getItem(key)));
    if (saved > 0) apply(saved);
    const g = document.createElement('div');
    g.className = 'sideGrip';
    g.title = '끌어서 폭 바꾸기 · 두 번 누르면 처음 폭';
    box.appendChild(g);
    const kick = () => window.dispatchEvent(new Event('resize'));   // 지도 · 시간줄이 새 폭으로 다시 그림
    let dn = null;
    g.addEventListener('pointerdown', (e) => {
      if (e.button !== 0) return;
      dn = { x: e.clientX, w: aside.getBoundingClientRect().width };
      g.setPointerCapture(e.pointerId); g.classList.add('on'); e.preventDefault();
    });
    g.addEventListener('pointermove', (e) => { if (!dn) return; apply(dn.w + (dn.x - e.clientX)); kick(); });
    const up = () => {
      if (!dn) return;
      dn = null; g.classList.remove('on');
      ls(() => localStorage.setItem(key, String(Math.round(aside.getBoundingClientRect().width))));
      kick();
    };
    g.addEventListener('pointerup', up);
    g.addEventListener('pointercancel', up);
    g.addEventListener('dblclick', () => { box.style.removeProperty('--sideW'); ls(() => localStorage.removeItem(key)); kick(); });
    window.addEventListener('resize', () => {                       // 창이 줄면 칸이 화면을 다 먹지 않게
      const cur = parseFloat(box.style.getPropertyValue('--sideW'));
      if (cur > maxW()) apply(maxW());
    });
  }
  function init() { ['ed', 'mp'].forEach((id) => { const b = document.getElementById(id); if (b) setup(b); }); }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
  window.RVResize = { init };
})();
