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
