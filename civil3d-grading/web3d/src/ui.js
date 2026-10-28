// 화면 위 칸들 — 층 · 정보 풍선 · 재기 · 토공량 · 자가진단 · 저장 메뉴 · 바쁨/오류 · 아래 가운데 칸(해 · 시공 · 자르기 · 영상).
import { CLASS_STYLE } from './layers.js';
import { SLOPE_STOPS } from './extras.js';

// 경사 범례 눈금 — 로그 눈금 자리(0~100 %) · 글
const _lg = (g) => Math.log10(g);
const SLOPE_POS = SLOPE_STOPS.map(([g]) => (_lg(g) - _lg(SLOPE_STOPS[0][0])) / (_lg(SLOPE_STOPS[SLOPE_STOPS.length - 1][0]) - _lg(SLOPE_STOPS[0][0])) * 100);
const SLOPE_TXT = ['0.5%', '2%', '5%', '10%', '30%', '1:1.5'];
const SLOPE_GRAD = `linear-gradient(90deg,${SLOPE_STOPS.map(([, c], i) => `${c} ${SLOPE_POS[i].toFixed(1)}%`).join(',')})`;

const $ = (id) => document.getElementById(id);
const esc = (s) => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const n0 = (v) => Number.isFinite(v) ? Math.round(v).toLocaleString('ko-KR') : '—';

export class Ui {
  constructor() {
    this.onLayer = null;    // (id, on) => void
    this.onGoto = null;     // (go) => void
    this._go = null;
    $('infoX').onclick = () => this.hideInfo();
    $('warnX').onclick = () => { $('warnList').style.display = 'none'; };
    $('warnBtn').onclick = () => { const s = $('warnList').style; s.display = s.display === 'block' ? 'none' : 'block'; };
    $('sideBtn').onclick = () => this.toggleSide();
    $('sideClose').onclick = () => this.toggleSide(false);
    if (window.innerWidth < 700) this.toggleSide(false);
  }

  busy(text) {
    const b = $('busy');
    if (text) { $('busyT').textContent = text; b.style.display = 'flex'; }
    else b.style.display = 'none';
  }

  error(text) {
    this.busy(null);
    $('errT').textContent = text;
    $('err').style.display = 'flex';
  }

  clearError() { $('err').style.display = 'none'; }

  toggleSide(on) {
    const s = $('side');
    const open = on === undefined ? s.classList.contains('closed') : on;
    s.classList.toggle('closed', !open);
    $('sideBtn').classList.toggle('on', open);
  }

  stamp(head) {
    const t = [head.dwg, head.made].filter(Boolean).join(' · ');
    $('stamp').textContent = t;
    $('stamp').title = `${head.app || ''}${head.epsg ? ' · EPSG:' + head.epsg : ''}`;
  }

  // 층 목록 — 순서 고정 · 없는 층은 안 보임
  layers(L, order) {
    const box = $('layers');
    box.innerHTML = '';
    for (const id of order) {
      const l = L[id];
      if (!l || l.fixed) continue;     // 늘 보이는 층(옹벽)은 단추 없음
      const row = document.createElement('label');
      const sw = SWATCH[id];
      if (l.missing) {
        // 자료가 없는 층 — 회색 + ⚠(까닭은 풍선 글)
        row.className = 'lay miss';
        row.title = l.missing;
        row.innerHTML = `<input type="checkbox" disabled>${sw ? `<span class="sw" style="background:${sw}"></span>` : ''}<span>${esc(l.label)}</span><span class="wn">⚠</span>`;
        box.appendChild(row);
        continue;
      }
      row.className = 'lay' + (l.on ? '' : ' off');
      row.innerHTML = `<input type="checkbox" ${l.on ? 'checked' : ''}>${sw ? `<span class="sw" style="background:${sw}"></span>` : ''}<span>${esc(l.label)}</span>`;
      const cb = row.querySelector('input');
      cb.onchange = () => { row.classList.toggle('off', !cb.checked); if (this.onLayer) this.onLayer(id, cb.checked); };
      box.appendChild(row);
      l.row = row; l.cb = cb;
    }
  }

  setLayerChecked(L, id, on) {
    const l = L[id];
    if (!l || !l.cb) return;
    l.cb.checked = on;
    l.row.classList.toggle('off', !on);
  }

  legend(show, lim) {
    $('legend').style.display = show ? 'block' : 'none';
    if (show) { $('lgCut').textContent = `절토 ${lim.toFixed(1)} m`; $('lgFill').textContent = `성토 ${lim.toFixed(1)} m`; }
  }

  // 빗물 범례 — 층이 켜졌을 때만 · 왼쪽 아래 따로 한 칸(JACK 1028) · ↻ = 고임 다시 채우기
  drainLegend(show, summary) {
    $('drainLg').style.display = show ? 'block' : 'none';
    if (show) $('drainSum').textContent = summary || '';
    $('drainRe').onclick = () => { if (this.onDrainReplay) this.onDrainReplay(); };
  }

  // 경사 범례 — 층이 켜졌을 때만 · 왼쪽 아래 따로 한 칸(빗물 범례와 같은 자리 줄) · 색 막대 = 화살표 색과 같은 로그 눈금
  slopeLegend(show) {
    const el = $('slopeLg');
    if (!el.dataset.made) {
      el.dataset.made = '1';
      $('slopeBar').style.background = SLOPE_GRAD;
      $('slopeTick').innerHTML = SLOPE_TXT.map((t, i) => `<span style="left:${SLOPE_POS[i].toFixed(1)}%">${t}</span>`).join('');
    }
    el.style.display = show ? 'block' : 'none';
  }

  // ★[JACK 1028] 웹 주소로 공유 — 올리기 전에 한 번 묻는다([계속] · [닫기])
  confirmShare(go) {
    const el = $('shareAsk');
    el.style.display = 'block';
    const close = () => { el.style.display = 'none'; };
    $('askGo').onclick = () => { close(); go(); };
    $('askX').onclick = close;
    $('askNo').onclick = close;
  }

  earth(e) {
    if (!e) return;
    const net = e.fill - e.cut;
    $('earthT').innerHTML =
      `<tr><td class="k">절토</td><td class="cut">${n0(e.cut)} ㎥</td></tr>` +
      `<tr><td class="k">성토</td><td class="fill">${n0(e.fill)} ㎥</td></tr>` +
      `<tr><td class="k">순토량</td><td>${n0(Math.abs(net))} ㎥ ${net >= 0 ? '반입' : '반출'}</td></tr>` +
      (Number.isFinite(e.area) && e.area > 0 ? `<tr><td class="k">계획면적</td><td>${n0(e.area)} ㎡</td></tr>` : '');
    $('earth').style.display = 'block';
    $('earth').title = e.ground ? `원지반: ${e.ground}` : '';
  }

  warnings(list) {
    if (!list || !list.length) { $('warnBtn').style.display = 'none'; return; }
    $('warnN').textContent = String(list.length);
    $('warnBtn').style.display = 'inline-flex';
    $('warnUl').innerHTML = list.map(w => `<li>${esc(w)}</li>`).join('');
  }

  showInfo(cx, cy, d) {
    const el = $('info');
    $('infoT').textContent = d.title || '';
    $('infoTb').innerHTML = d.rows.map(([k, v]) => `<tr><td>${esc(k)}</td><td>${esc(v)}</td></tr>`).join('');
    this._go = d.go;
    // 풍선 단추 — [도면에서 보기](Civil 안 · 자리가 있을 때) + 그 물체 단추(측점: [도면 횡단 보기] · [3D에서 자르기])
    const acts = $('infoActs');
    acts.innerHTML = '';
    const btns = [];
    if (document.body.classList.contains('inhost') && d.go) btns.push({ text: '도면에서 보기', fn: () => { if (this.onGoto) this.onGoto(d.go); } });
    for (const a of d.acts || []) btns.push(a);
    for (const b of btns) { const e = document.createElement('button'); e.textContent = b.text; e.onclick = b.fn; acts.appendChild(e); }
    acts.style.display = btns.length ? 'flex' : 'none';
    el.style.display = 'block';
    const W = window.innerWidth, H = window.innerHeight, r = el.getBoundingClientRect();
    let x = cx + 14, y = cy + 14;
    if (x + r.width > W - 8) x = cx - r.width - 14;
    if (y + r.height > H - 8) y = cy - r.height - 14;
    el.style.left = Math.max(8, x) + 'px';
    el.style.top = Math.max(8, y) + 'px';
  }

  hideInfo() { $('info').style.display = 'none'; this._go = null; }

  showMeasure(d) {
    $('measTb').innerHTML = d.rows.map(([k, v]) => `<tr><td>${esc(k)}</td><td>${esc(v)}</td></tr>`).join('');
    $('measure').style.display = 'block';
  }

  hideMeasure() { $('measure').style.display = 'none'; }

  compass(deg) { $('needle').setAttribute('transform', `rotate(${-deg} 16 16)`); }

  // 웹 공유 주소 — 복사 · 브라우저로 열기(Civil 안에서는 C#가 한다 — 웹뷰 안 새 창은 막혀 있다)
  showShare(url, note, act) {
    $('shareUrl').value = url;
    $('shareNote').textContent = note;
    $('share').style.display = 'block';
    $('shareCopy').onclick = async () => {
      try { await navigator.clipboard.writeText(url); this.toast('주소를 복사했습니다'); }
      catch { act('copyurl'); }
    };
    $('shareOpen').onclick = () => act('openurl');
    $('shareX').onclick = () => { $('share').style.display = 'none'; };
    $('shareUrl').onfocus = () => $('shareUrl').select();
  }

  // 짧은 알림 — 몇 초 뒤 사라진다
  toast(text) {
    const el = $('toast');
    el.textContent = text;
    el.style.display = text ? 'block' : 'none';
    clearTimeout(this._toastT);
    if (text) this._toastT = setTimeout(() => { el.style.display = 'none'; }, 4500);
  }

  // 저장 메뉴 — 단추 아래에 띄움
  saveMenu(onPick) {
    const menu = $('menu');
    $('saveBtn').onclick = (e) => {
      e.stopPropagation();
      const r = $('saveBtn').getBoundingClientRect();
      menu.style.display = menu.style.display === 'flex' ? 'none' : 'flex';
      menu.style.left = Math.max(8, Math.min(window.innerWidth - 180, r.right - 170)) + 'px';
      menu.style.top = (r.bottom + 6) + 'px';
    };
    for (const b of menu.querySelectorAll('button')) b.onclick = () => { menu.style.display = 'none'; onPick(b.dataset.save); };
    document.addEventListener('pointerdown', (e) => { if (!menu.contains(e.target) && e.target !== $('saveBtn')) menu.style.display = 'none'; });
  }
}

const SWATCH = {
  terrain: CLASS_STYLE.plan.color, sat: '#4f6b3a', cutfill: 'linear-gradient(90deg,#cc2a1f,#f4f4f0,#265ccc)',
  ground: '#5f7a45', excav: '#9b7b52', wall: '#c2bfb8', pattern: '#a8a090', drain: '#1e7bff', cad: '#ffd400', align: '#ff4d4d',
  slope: SLOPE_GRAD,
};

// 아래 가운데 칸 — 해 · 시공 · 자르기 · 영상 중 하나만 열림. 닫으면 연 쪽의 끝내기를 부른다
export class Dock {
  constructor() { this.el = $('dock'); this.kind = null; this._onClose = null; this.onChange = null; }
  open(kind, html, onClose) {
    if (this.kind) this.close();
    this.kind = kind; this._onClose = onClose;
    this.el.innerHTML = html;
    this.el.style.display = 'block';
    document.body.classList.add('docked');
    if (this.onChange) this.onChange(kind);
  }
  close() {
    if (!this.kind) return;
    const f = this._onClose;
    this.kind = null; this._onClose = null;
    this.el.style.display = 'none';
    this.el.innerHTML = '';
    document.body.classList.remove('docked');
    if (f) f();
    if (this.onChange) this.onChange(null);
  }
}
