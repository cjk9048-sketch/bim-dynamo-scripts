# 드론 360 로드뷰

DJI Avata 360 영상(.OSV) · 360 사진(JPG)으로 상수도 설계용 로드뷰를 만들고, 관로(SHP · Civil 3D DXF)를 겹쳐 본다.
설치형 프로그램 «드론 로드뷰» 안에서 만들고 · 보고 · 웹(NAS)으로 내보낸다.

## 프로그램

설치: `packaging\Output\드론로드뷰_설치_<판>.exe` (관리자 권한 없이 사용자 폴더 · ffmpeg 포함)

1. **새로 만들기** — 영상 · 사진 고르기 → **간격 (m)** → 만들기 (수치지도 표고 · 위성 지도 켜고 끄기)
2. **프로젝트 화면**
   - 360: 끌기 · 휠 · **↑↓←→ = 보는 방향 기준 옆 촬영점** · PgUp/PgDn = 찍은 순서 · 땅 위 작은 원 누르기
   - **관로 넣기**: SHP(속성표에서 관경 항목 mm) · DXF(3D 폴리선) → 몇 초 · 평균매설깊이 (땅 → 관 윗면)
   - 노면 = 관경 두께의 띠 · 깊이 = 3D 관
   - **맞춤 → 점 찍어 맞추기**: 360 사진 한 점 + 위성지도 같은 점 → 영상 위치 · 방향 · 높이 (지도 [이름] 끄면 위성만)
   - **내보내기**: 웹 주소(NAS, Civil 3D 3D 웹 공유 설정 그대로) · 폴더
3. 맞춤 값은 프로젝트 폴더(`data/calib.js`)에 바로 저장된다.

명령줄 (개발):

```
python -m roadview.build 영상폴더 --out 결과폴더 --interval 5
python -m roadview.pipes_update 결과폴더 관로.shp --dia-field 관경 --depth 1.2
python -m roadview.share 결과폴더            # NAS 올리기 (이어 올리기) · --test · --new · --remove
python rv_app.py                            # 프로그램 (개발 상태)
cd packaging && pyinstaller --noconfirm roadview.spec && ISCC roadview.iss   # 설치 파일
```

## 하는 일

1. OSV 안 기록(djmd)에서 장면마다 GPS · 이륙점 기준 높이 · 드론 자세 · 참 북 방향을 읽는다 (`osv.py`).
2. 진행 거리 간격마다 장면을 고른다.
3. 땅 위 높이를 영상으로 잰다 — 두 장면의 «위에서 본 땅 그림»이 겹치는 높이 (`height.py`).
4. 어안 두 장을 꿰매 수평 · 격자 북 기준 360 사진으로 (`camera.py`, 렌즈 값 `rig_avata360.json`).
5. 관로를 읽어(`shp.py` · `pipes.py` · `pipeload.py`) 경로 200 m 안만 남기고, 꼭짓점마다 땅 높이를 채운다.
6. NAS 올리기는 Civil 3D 3D 웹 공유와 같은 설정 · 비밀번호(%APPDATA%\DH.Grading)를 쓴다 (`share.py`).

## 정확도 (0014 영상 · 시험 관로)

- 촬영점에서 40 m 안: 0.3~0.5 m
- 50~65 m: 1~2 m (낮은 각도라 높이 오차가 늘어남)
- 절대 위치는 드론 GPS 오차(보통 수 m)만큼 통째로 밀릴 수 있다 → «맞춤»으로 영상마다 보정.
