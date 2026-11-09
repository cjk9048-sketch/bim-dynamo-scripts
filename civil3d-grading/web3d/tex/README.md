# 바닥 질감 — [3D 보기]

| 파일 | 쓰는 곳 | 원본 | 고침 |
|---|---|---|---|
| `grass.jpg` | 사면(잔디) | Poly Haven `leafy_grass` | 1024 px · 초록 쪽으로 색 맞춤 |
| `soil.jpg` | 계획면(다진 흙) · 정지 | Poly Haven `raked_dirt` | 1024 px · 밝고 누렇게 |
| `gravel.jpg` | 소단(쇄석) | Poly Haven `gravel_floor_02` | 1024 px |
| `asphalt.jpg` | 진입도로 차로(아스팔트) | Poly Haven `clean_asphalt` | 1024 px · 채도 0.3 · 진회색(평균 82, 82, 84) |
| `concrete.jpg` | 길어깨 · 측구(콘크리트) | Poly Haven `brushed_concrete` | 1024 px · 채도 0.15 · 대비 0.6 · 밝은 회색(평균 176, 174, 168) |

원본은 모두 **CC0**(Poly Haven — https://polyhaven.com/license) — 고쳐 쓰고 다시 나눠도 된다.
뷰어 묶음(`dist/viewer.js`)에 data URL로 들어간다(`tools/build.mjs` loader).

## 나무 질감(수목 층) — 파일 없음

| 그림 | 쓰는 곳 | 원본 | 크기 |
|---|---|---|---|
| 잎 · 껍질 아틀라스(4 × 4 칸) | 수목 잎 카드 뭉치 · 줄기 · 가지(`src/forest.js`) | **외부 자료 없음** — `src/treetex.js`가 뷰어를 열 때 코드로 그린다(잎 · 바늘 · 대나무 잎 모양 · 값 잡음 껍질 · 씨앗 고정) | GPU 1024² RGBA + 밉맵 ≈ 5.6 MB · 묶음 0 바이트 |

칸: 활엽 잎 뭉치 4 · 침엽(전나무 · 낙엽송) 바늘 가지 3 · 소나무 바늘 다발 3 · 대나무 잎 2 · 껍질(일반 · 소나무 붉은 · 대나무 마디 · 밝은 자작) 4.
밉맵은 칸마다 따로 줄이고 «알파 ≥ 0.5» 비율을 원래 그림과 같게 맞춘다(멀리서 잎이 말라 사라지지 않게) — `test/forest.test.mjs`가 검사.
그림을 눈으로 보려면 `node test/forest-page.mjs <out.html>` → `node tools/shot.mjs <out.html> <폴더> [보기.json]`.
