// ★[JACK 1124 «3D 뷰에서 빗물 기능을 이용해서 측구 설계»] 측구 집수 구역 — C# DitchCatch.Strips 띠를 세모로(three 없이 · 시험 가능)
//   띠 = [x0, y0, 칸, n, 아래 가장자리 z × (n + 1), 위 가장자리 z × (n + 1)] 되풀이 · 구역 색(#rrggbb) · faceOf = 세모마다 구역 차례(누르면 풍선)
export function catchMesh(zones) {
  const pos = [], col = [], idx = [], faceOf = [];
  (zones || []).forEach((z, zi) => {
    const s = z.strips || [];
    const hex = /^#?([0-9a-f]{6})$/i.exec(z.color || '') ? parseInt(/^#?([0-9a-f]{6})$/i.exec(z.color)[1], 16) : 0x2e86de;
    const r = ((hex >> 16) & 255) / 255, g = ((hex >> 8) & 255) / 255, b = (hex & 255) / 255;
    let i = 0;
    while (i + 3 < s.length) {
      const x0 = s[i], y0 = s[i + 1], st = s[i + 2], n = Math.round(s[i + 3]);
      const zb = i + 4, zt = zb + n + 1, end = zt + n + 1;
      if (!(n >= 1) || !(st > 0) || end > s.length) break;
      const v0 = pos.length / 3;
      for (let q = 0; q <= n; q++) { pos.push(x0 + q * st, y0, s[zb + q]); col.push(r, g, b); }
      for (let q = 0; q <= n; q++) { pos.push(x0 + q * st, y0 + st, s[zt + q]); col.push(r, g, b); }
      for (let q = 0; q < n; q++) {
        const a = v0 + q, c = v0 + n + 1 + q;
        idx.push(a, a + 1, c + 1, a, c + 1, c);
        faceOf.push(zi, zi);
      }
      i = end;
    }
  });
  return { pos, col, idx, faceOf };
}

// 풍선 줄 — 집수면적(자동 · 입력) · C · 직접 넓이 · 위 측구
export function catchRows(z, fmt) {
  const rows = [];
  rows.push(['집수면적', fmt(z.area, 4) + ' ha' + (z.src ? ` (${z.src})` : '')]);
  if (z.c > 0) rows.push(['유출계수 C', fmt(z.c, 2)]);
  rows.push(['직접 구역', fmt(z.direct, 0) + ' ㎡']);
  if (z.up && z.up.length) rows.push(['위 측구', z.up.join(' · ') + ` (전체 ${fmt(z.total, 0)} ㎡)`]);
  return rows;
}
