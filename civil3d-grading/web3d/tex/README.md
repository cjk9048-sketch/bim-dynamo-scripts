# 바닥 질감 — [3D 보기]

## 색 그림(그대로 — 1110에 안 바뀜)

| 파일 | 쓰는 곳 | 원본 | 고침 |
|---|---|---|---|
| `grass.jpg` | 사면(잔디) | Poly Haven `leafy_grass` — https://polyhaven.com/a/leafy_grass | 1024 px · 초록 쪽으로 색 맞춤 |
| `soil.jpg` | 계획면(다진 흙) · 정지 | Poly Haven `raked_dirt` — https://polyhaven.com/a/raked_dirt | 1024 px · 밝고 누렇게 |
| `gravel.jpg` | 소단(쇄석) | Poly Haven `gravel_floor_02` — https://polyhaven.com/a/gravel_floor_02 | 1024 px |
| `asphalt.jpg` | 진입도로 차로(아스팔트) | Poly Haven `clean_asphalt` — https://polyhaven.com/a/clean_asphalt | 1024 px · 채도 0.3 · 진회색(평균 82, 82, 84) |
| `concrete.jpg` | 길어깨 · 측구 · ★1110 옹벽 콘크리트 | Poly Haven `brushed_concrete` — https://polyhaven.com/a/brushed_concrete | 1024 px · 채도 0.15 · 대비 0.6 · 밝은 회색(평균 176, 174, 168) |

## 표면 지도 `*_s.jpg`(★JACK 1110 «만화스럽다» — 법선 · 그늘 · 거칠기)

같은 Poly Haven 자료의 짝 그림(1k JPG)에서 만든 512 px 묶음 그림 — **R = 높이 · G = 그늘(AO) · B = 128(안 씀)**.
뷰어가 열 때 R(높이)로 법선을 세고(`src/groundtex.js` `surfaceFromPixels` — 소벨 · 가장자리 이어 셈 · 기울기 크기는 재료마다 `SURFACE.bump`)
GPU용 RGBA(법선 x, y · 그늘 · 높이)로 바꾼다. 거칠기는 높이로(높은 곳이 매끈 — 아스팔트 골재 머리) 재료마다 `SURFACE.rough`.

| 파일 | 높이(R) 원본 | 그늘(G) 원본 | 바이트 |
|---|---|---|---|
| `grass_s.jpg` | `leafy_grass_disp_1k.jpg` | `leafy_grass_ao_1k.jpg` | 176,540 |
| `soil_s.jpg` | `raked_dirt_disp_1k.jpg` | `raked_dirt_ao_1k.jpg` | 91,121 |
| `gravel_s.jpg` | `gravel_floor_02_disp_1k.jpg` | `gravel_floor_02_ao_1k.jpg` | 222,391 |
| `asphalt_s.jpg` | `clean_asphalt_ao_1k.jpg` ※ | `clean_asphalt_ao_1k.jpg` | 155,643 |
| `concrete_s.jpg` | `brushed_concrete_disp_1k.jpg` | `brushed_concrete_ao_1k.jpg` | 75,843 |

내려받은 곳: `https://dl.polyhaven.org/file/ph-assets/Textures/jpg/1k/<자료>/<파일>`(목록 = `https://api.polyhaven.com/files/<자료>` · md5 맞춤 확인 1110).
※ `clean_asphalt`의 높이 그림은 거의 평평(표준편차 2.3/255 · 값 37가지)이라 골재 사이 틈이 드러나는 그늘 그림을 높이로 썼다.

만든 법(1110): 1k 그림 → 512 px(Lanczos) · 높이는 평균 128 · 표준편차 42로 늘리고 0~255로 자름 · 그늘은 그대로 · JPEG 품질 85 · 색 줄임 없음(4:4:4).
(묶음 `dist/viewer.js`가 1110에 2,544,577 → 3,513,902 바이트(+969,325) — 거의 다(962,051) 이 다섯 장이 base64 data URL로 들어간 몫 · 나머지 ≈ 7.3 KB는 셰이더 · 코드)

## 쓰는 법(`src/groundtex.js` · `src/layers.js`)

- 되풀이 감추기 — 옛 «같은 그림을 37° 돌려 45 % 겹치기»는 대비가 줄고 지푸라기가 두 겹(유령)으로 보였다 → 잡음 띠마다 다른 자리를 읽고 띠 사이만 섞는다
  (Inigo Quilez «texture repetition» 셋째 방법 · 색 · 표면 지도 같은 두 자리).
- 큰 얼룩 — m 단위 값 잡음 두 크기로 밝기 · 색을 곱한다(잔디 푸른 ↔ 마른 · 흙 젖은 ↔ 마른 · 아스팔트 새 ↔ 닳은 · 콘크리트 때). 멀면(화소가 잡음 한 칸에 가까우면) 평균으로.
- 옹벽 · 측구 콘크리트(석축 찰쌓기 · 메쌓기 빼고) — uv가 없어 면이 가장 많이 향한 축을 빼고 남은 두 세계 축으로 깐다(상자 투영 · 한 장 3 m) · 밝기는 재료 색 그대로.
- 위성이 없을 때(못 받음 · 시험 키트) 원지반 — 옛 한 색(#9aa77f) 대신 `grass.jpg` · `grass_s.jpg`를 채도 0.8로 낮춘 풀밭(`SURFACE.meadow` · 한 장 5 m · 얼룩 9 m · 70 m) · 원지반 보기 층도 같음.
  위성이 있으면 위성 그대로(이 길은 안 바뀜) · 위성을 [위성] 단추로 끄면 옛 한 색 그대로.
- 노면 표시는 더하지 않았다 — 흰 차선은 전처럼 C#가 보내는 차로 끝선뿐 · 가운데선 등은 설계 결정이라 따로 지시가 있을 때.

원본은 모두 **CC0**(Poly Haven — https://polyhaven.com/license · 1110 다시 확인: «All assets … are licensed as CC0») — 고쳐 쓰고 다시 나눠도 된다.
뷰어 묶음(`dist/viewer.js`)에 data URL로 들어간다(`tools/build.mjs` loader).
눈으로 견주려면 `node test/ground-page.mjs <out.html>`(가짜 현장 — 계획면 · 절토/성토 사면 + 소단 · 진입도로 · 측구 · 옹벽 · 숲) → `node tools/shot.mjs <out.html> <폴더> [보기.json]`.

## 나무 질감(수목 층) — 파일 없음

| 그림 | 쓰는 곳 | 원본 | 크기 |
|---|---|---|---|
| 잎 · 껍질 아틀라스(4 × 4 칸) | 수목 잎 카드 뭉치 · 줄기 · 가지(`src/forest.js`) | **외부 자료 없음** — `src/treetex.js`가 뷰어를 열 때 코드로 그린다(잎 · 바늘 · 대나무 잎 모양 · 값 잡음 껍질 · 씨앗 고정) | GPU 1024² RGBA + 밉맵 ≈ 5.6 MB · 묶음 0 바이트 |

칸: 활엽 잎 뭉치 4 · 침엽(전나무 · 낙엽송) 바늘 가지 3 · 소나무 바늘 다발 3 · 대나무 잎 2 · 껍질(일반 · 소나무 붉은 · 대나무 마디 · 밝은 자작) 4.
밉맵은 칸마다 따로 줄이고 «알파 ≥ 0.5» 비율을 원래 그림과 같게 맞춘다(멀리서 잎이 말라 사라지지 않게) — `test/forest.test.mjs`가 검사.
그림을 눈으로 보려면 `node test/forest-page.mjs <out.html>` → `node tools/shot.mjs <out.html> <폴더> [보기.json]`.
