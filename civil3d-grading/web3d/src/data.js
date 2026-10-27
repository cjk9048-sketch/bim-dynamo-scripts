// 장면 자료 읽기 — 페이지 안 <script id="dh3d-data">(base64) → gzip 풀기 → «DH3D» 묶음.
// 묶음 꼴(C# Web3DScene.Pack과 짝): "DH3D" · u32 판 · u32 머리 길이 · 머리(JSON UTF-8) · 4바이트 맞춤 = 덩어리 시작 · 덩어리들.
// 머리 안 덩어리 표시 {o: 덩어리 시작부터(4의 배수), n: 바이트 수, t: 'f32'|'u32'|'u8'|'img', m: 그림 꼴}.

const CHUNK = 4 << 20;   // base64 4 MB씩 — 큰 문자열 하나를 통째로 atob 하지 않는다

function base64ToBytes(b64) {
  const clean = b64.replace(/\s+/g, '');
  const pad = clean.endsWith('==') ? 2 : clean.endsWith('=') ? 1 : 0;
  const out = new Uint8Array((clean.length / 4) * 3 - pad);
  let w = 0;
  for (let i = 0; i < clean.length; i += CHUNK) {
    const bin = atob(clean.slice(i, i + CHUNK));
    for (let k = 0; k < bin.length; k++) out[w++] = bin.charCodeAt(k);
  }
  return out;
}

async function gunzip(bytes) {
  const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream('gzip'));
  return await new Response(stream).arrayBuffer();
}

export async function loadScene() {
  const el = document.getElementById('dh3d-data');
  if (!el || !el.textContent.trim()) throw new Error('장면 자료가 없습니다');
  const buf = await gunzip(base64ToBytes(el.textContent));
  el.textContent = '';                                   // 큰 문자열은 놓아 준다
  const dv = new DataView(buf);
  const magic = String.fromCharCode(dv.getUint8(0), dv.getUint8(1), dv.getUint8(2), dv.getUint8(3));
  if (magic !== 'DH3D') throw new Error('장면 자료 꼴이 다릅니다');
  const ver = dv.getUint32(4, true);
  if (ver !== 1) throw new Error('장면 자료 판이 다릅니다(' + ver + ')');
  const hLen = dv.getUint32(8, true);
  const head = JSON.parse(new TextDecoder().decode(new Uint8Array(buf, 12, hLen)));
  const base = (12 + hLen + 3) & ~3;                     // 덩어리 시작(4바이트 맞춤) — 표시 o는 여기서부터

  // 덩어리 표시 {o,n,t} → 형식 배열(복사 없이 보기) · 그림은 Blob
  const take = (b) => {
    if (!b) return null;
    const o = base + b.o;
    switch (b.t) {
      case 'f32': return new Float32Array(buf, o, b.n / 4);
      case 'u32': return new Uint32Array(buf, o, b.n / 4);
      case 'u8': return new Uint8Array(buf, o, b.n);
      case 'img': return new Blob([new Uint8Array(buf, o, b.n)], { type: b.m || 'image/jpeg' });
      default: throw new Error('모르는 덩어리 꼴 ' + b.t);
    }
  };
  const isRef = (v) => v && typeof v === 'object' && typeof v.o === 'number' && typeof v.n === 'number' && typeof v.t === 'string';
  const resolve = (v) => {
    if (Array.isArray(v)) return v.map(resolve);
    if (isRef(v)) return take(v);
    if (v && typeof v === 'object') {
      const o = {};
      for (const k of Object.keys(v)) o[k] = resolve(v[k]);
      return o;
    }
    return v;
  };
  return { head: resolve(head), bytes: buf.byteLength };
}
