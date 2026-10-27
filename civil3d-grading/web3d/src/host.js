// Civil 안(WebView2)일 때만 — 새로고침 · 저장 · 도면에서 보기 · 도면에서 고른 것 따라가기.
export const inHost = !!(window.chrome && window.chrome.webview);

export function post(msg) {
  if (!inHost) return;
  try { window.chrome.webview.postMessage(JSON.stringify(msg)); } catch (e) { /* 창이 닫히는 중 */ }
}

export function onHost(fn) {
  if (!inHost) return;
  window.chrome.webview.addEventListener('message', (e) => {
    let m = e.data;
    if (typeof m === 'string') { try { m = JSON.parse(m); } catch { return; } }
    if (m && typeof m === 'object') fn(m);
  });
}
