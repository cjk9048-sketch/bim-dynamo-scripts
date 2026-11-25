# 드론 경사촬영 3D 매핑 (DJI Terra 대체 · 시험 단계)

DJI Matrice 4E 영역 경로 경사촬영 원본 사진 → 3D 모델 · 점구름 · (정사영상 · DSM) → 관로 얹기.
Terra 라이선스 만료로 공개 엔진을 쓴다. 지금은 **엔진 시험** 단계 — 프로그램 화면은 아직 없음.

## 엔진 (내려받아 `D:\매핑시험\engines` 에 풂 · 저장소에는 넣지 않음)

| 엔진 | 판 | 라이선스 | 쓰는 곳 |
|---|---|---|---|
| COLMAP (Windows · CUDA) | 4.2.1 | BSD | 특징점 · GPS 이웃 맞추기 · RTK 위치를 쓰는 카메라 풀기(`pose_prior_mapper`) · 왜곡 펴기 |
| OpenMVS (Windows · CUDA) | 2.4.0 | AGPL | 촘촘한 점구름 · 3D 면 · 질감 |

내려받기: `github.com/colmap/colmap/releases` · `github.com/cdcseacave/openMVS/releases` (회사망 GitHub API 는 한도에 걸림 → 릴리스 페이지에서 주소)

## 순서

```
bash run_sparse.sh 작업폴더 사진폴더          # 특징점 → 맞추기 → 카메라 풀기 (시간.txt 에 단계별 초)
bash run_dense.sh  작업폴더 사진폴더 [2640]   # 왜곡 펴기 → 점구름 → 면 → 질감 (OBJ)
```

## 잰 사실 (1125)

- 시험 PC: RTX 5070 Ti 16 GB · Core Ultra 7 265K 20코어 · RAM 63 GB — COLMAP 4.2.1 CUDA 가 뜸.
- M4E 사진: 5280×3956 · 짐벌 −57° 경사 · XMP 에 RTK 위치(RtkFlag 50 = 고정해 · 표준편차 수 mm~1 cm) · AbsoluteAltitude(타원체고로 보임 — 지오이드 보정 필요 확인) · DewarpData · CalibratedFocalLength 3725 px.
  임무 폴더에 MRK · NAV · OBS · RTK 파일도 있음 (PPK 용).
- Terra 결과 형식(비교용): 3D Tiles 점구름(pnts) · FBX/PLY 질감 면 64조각 · LAS · DEM · 정사 GeoTIFF — 좌표계 EPSG:5186 + KNGeoid18 표고.
- OpenMVS 는 도움말 · 진행을 화면이 아니라 실행 폴더의 `*.log` 에 쓴다.

## 다음

1. 394장 전체로 `run_sparse.sh` → 시간 · 등록 사진 수 · 재투영 오차.
2. `run_dense.sh` → 시간 · 모습 · Terra 결과와 견줌.
3. 좌표: 카메라 풀기 결과를 EPSG:5186 + 지오이드 표고로 (Terra 와 같게).
4. 결과가 쓸 만하면 계획 → 프로그램(사진 폴더 → 버튼 하나 → 3D · 정사 · DSM → 관로 얹기).
