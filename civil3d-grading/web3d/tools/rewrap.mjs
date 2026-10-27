// 만든 3D 파일의 자료만 꺼내 지금 page.html · dist/viewer.js로 다시 감싼다(뷰어를 고칠 때 Civil을 다시 돌리지 않게).
//   node tools/rewrap.mjs <들어온 html> <나갈 html>
import { readFileSync, writeFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const [, , src, dst] = process.argv;
if (!src || !dst) { console.error('쓰는 법: node tools/rewrap.mjs <in.html> <out.html>'); process.exit(2); }
const html = readFileSync(src, 'utf8');
const open = '<script id="dh3d-data" type="application/octet-stream">';
const i = html.indexOf(open), j = html.indexOf('</script>', i);
if (i < 0 || j < 0) { console.error('자료 자리를 못 찾음'); process.exit(1); }
const data = html.slice(i + open.length, j);
const t = html.match(/<title>([^<]*)<\/title>/);
const tpl = readFileSync(join(root, 'page.html'), 'utf8');
const viewer = readFileSync(join(root, 'dist', 'viewer.js'), 'utf8');
// 자리를 차례로 한 번씩(C# Web3DScene.WritePage와 같은 법)
const iT = tpl.indexOf('__TITLE__'), iD = tpl.indexOf('__DATA__'), iV = tpl.indexOf('/*__VIEWER__*/');
const out = tpl.slice(0, iT) + (t ? t[1] : '3D 보기') + tpl.slice(iT + 9, iD) + data + tpl.slice(iD + 8, iV) + viewer + tpl.slice(iV + 14);
writeFileSync(dst, out);
console.log(`${dst} ${(out.length / 1048576).toFixed(1)} MB`);
