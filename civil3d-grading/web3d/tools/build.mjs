// [3D 보기] 뷰어 묶기 — src/*.js + three → dist/viewer.js(IIFE 하나) · three.js MIT 전문을 머리에 · src 지문(source-hash.txt).
//   node tools/build.mjs          묶기
//   node tools/build.mjs --check  dist가 지금 src로 묶은 것인지(지문)만 본다 — 다르면 1로 끝남(설치 전 확인)
import { build } from 'esbuild';
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync, readdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const src = join(root, 'src');

function sourceHash() {
  const h = createHash('sha256');
  for (const f of readdirSync(src).filter(f => f.endsWith('.js')).sort()) { h.update(f); h.update(readFileSync(join(src, f))); }
  for (const f of readdirSync(join(root, 'tex')).filter(f => f.endsWith('.jpg')).sort()) { h.update(f); h.update(readFileSync(join(root, 'tex', f))); }
  h.update(readFileSync(join(root, 'package.json')));
  h.update(readFileSync(join(root, 'node_modules', 'three', 'package.json')));
  h.update(readFileSync(join(root, 'node_modules', 'mp4-muxer', 'package.json')));
  return h.digest('hex');
}

if (process.argv.includes('--check')) {
  let have = '';
  try { have = readFileSync(join(root, 'dist', 'source-hash.txt'), 'utf8').trim(); } catch { /* 없음 */ }
  const want = sourceHash();
  if (have !== want) { console.error('dist/viewer.js가 지금 src와 다릅니다 — npm run build'); process.exit(1); }
  console.log('dist/viewer.js = 지금 src');
  process.exit(0);
}

const lic = readFileSync(join(root, 'node_modules', 'three', 'LICENSE'), 'utf8').trim();
const threeVer = JSON.parse(readFileSync(join(root, 'node_modules', 'three', 'package.json'), 'utf8')).version;
// v108 — 영상 MP4 묶기(mp4-muxer · MIT)
const licMux = readFileSync(join(root, 'node_modules', 'mp4-muxer', 'LICENSE'), 'utf8').trim();
const muxVer = JSON.parse(readFileSync(join(root, 'node_modules', 'mp4-muxer', 'package.json'), 'utf8')).version;
await build({
  entryPoints: [join(src, 'main.js')],
  bundle: true,
  format: 'iife',
  minify: true,
  legalComments: 'none',
  target: 'chrome100',
  loader: { '.jpg': 'dataurl' },   // 바닥 질감(tex/*.jpg · CC0)을 뷰어 안에 — 저장한 파일이 인터넷 없이 열리게
  outfile: join(root, 'dist', 'viewer.js'),
  banner: { js: `/*! DH.Grading [3D 보기] 뷰어 — three.js ${threeVer} · mp4-muxer ${muxVer} 포함.\n\nthree.js:\n${lic.replace(/\*\//g, '* /')}\n\nmp4-muxer:\n${licMux.replace(/\*\//g, '* /')}\n*/` },
});
const out = readFileSync(join(root, 'dist', 'viewer.js'), 'utf8');
if (/<\/script/i.test(out)) { console.error('viewer.js에 </script 글자가 있습니다 — 페이지에 못 넣음'); process.exit(1); }
writeFileSync(join(root, 'dist', 'source-hash.txt'), sourceHash() + '\n');
console.log(`dist/viewer.js ${(out.length / 1024).toFixed(0)} KB · three ${threeVer}`);
