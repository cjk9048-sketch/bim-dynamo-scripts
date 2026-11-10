# revit-civil-coord — DH Civil 좌표 맞춤 (Revit 2026 애드인)

Revit에서 **어떤 좌표로 만들었든**, 구조물을 Civil 3D(DH 부지 애드인 `DHSTRUCT`) · 나비스웍스에
**제자리로** 들어가는 IFC로 내보냅니다. 같은 값으로 Revit 공유 좌표도 바꿀 수 있습니다.

## 쓰는 법

1. 리본 **DH 좌표 → Civil 좌표 맞춤**.
2. **① Civil 좌표** — 탭 하나를 고름(두 탭 값은 따로 보관 · 계산은 고른 탭 값):
   - **[Civil 파일]**: [Civil 파일 불러오기] → Civil 3D(DH 부지)에서 두 점을 찍어 내보낸 기준점 파일(JSON · `DH.CivilStructPoints` v1).
     단위 m · 점 2개 · 손으로 고친 흔적을 검사하고 구조물 이름 · 도면 · EPSG · 지반고를 보여 줌. Civil에서 다시 내보내면 «파일 바뀜» → [다시 읽기].
   - **[직접 입력]**: Civil 도면에서 읽은 X(동) · Y(북)(m)를 넣음.
3. **② Revit 모서리**(두 탭 공통): [Revit에서 찍기] → 평면도에서 Civil 1 · 2번과 같은 모서리를 같은 순서로 찍음(끝점 · 교차점에 붙음).
4. **확인** 줄: 두 점 거리(Revit · Civil · 차) · **비율** · 회전. 두 거리가 다르면 IFC는 그 비율로 평면(X · Y)만 키우거나 줄여 두 점을 꼭 맞춤(높이 그대로).
5. **미리보기**(창 오른쪽 · 북 = 위): Civil 개략 평면 선(기준점 파일의 `sketch`) · Civil 두 점 · 맞춘 자리의 Revit 부재 윤곽.
   점 순서가 바뀌거나 회전이 틀리면 윤곽이 Civil 선에서 벗어나 보임.
6. **높이**: Revit 레벨에 보이는 높이 그대로(EL 50.00 → 50.00 m). 필요하면 더할 값(m).
7. **[IFC 내보내기]**: 지금 3D 뷰에 보이는 부재 → Civil 좌표 IFC(3D 뷰가 아니면 기본 3D 뷰).
8. **[공유 좌표에 적용]**(선택): Revit 공유 좌표를 같은 Civil 좌표로(옮김 · 돌림만 — Revit은 비율을 못 담음) →
   Revit IFC · DWG · NWC를 «공유 좌표»로 내보내면 맞음. 두 거리가 다르면 끝점이 차의 반씩 어긋난다고 알림.
   쓴 뒤 두 점을 다시 재서 맞는지 확인하고, 안 맞으면 바꾸지 않음. Ctrl+Z로 되돌림.

넣은 값은 rvt 안(프로젝트 정보)에 저장돼, 다음에는 [IFC 내보내기]만 누르면 됩니다.

## IFC 꼴

- IFC4 ReferenceView · 길이 m · 형상 = 세모 면(IfcTriangulatedFaceSet) — Revit이 그리는 입체 그대로(구멍 · 잘림 반영).
- Civil 좌표(옮김 · 돌림 · 높이)는 **IfcSite 배치** 하나에 · 부재 좌표는 Revit 내부 좌표(m) × 평면 비율(X · Y만).
- IfcSite 속성 묶음 `DH_CivilFit`: Civil 좌표 출처 · 두 점 거리 · 평면 비율 · 회전 · 높이 더함.
- 부재 종류 → IFC 종류(벽 · 슬래브 · 기초 · 기둥 · 보 · 계단 · 난간 · 문 · 창 · 지형 …) — Civil이 레이어 · 해치를 이것으로 가름.
- GUID = Revit IFC 내보내기와 같은 값 · 속성 묶음 `DH_Revit`(Revit ID · 범주 · 패밀리 · 유형 · 레벨 · 마크).
- 빠지는 것: Revit 링크 · 가져온 CAD · 형상 없는 부재(창 아래에 개수).

## 구성

| 경로 | 설명 |
|---|---|
| `src/DH.CivilCoord.Core` | 두 점 맞춤(`Fit2D`) · IFC GUID · IFC 쓰기(`IfcWriter`) — Revit 없이 시험 |
| `src/DH.CivilCoord.Revit` | 리본 · 창 · 점 찍기 · 형상 모으기(`Exporter`) · 레벨 높이 · 공유 좌표 · 값 저장 |
| `src/DH.CivilCoord.Tests` | 시험 — 쓴 IFC를 Civil 애드인의 IFC 읽기 코드(실물 링크)로 읽어 좌표 비교 |
| `installer/DH.CivilCoord.addin` | 매니페스트 |
| `scripts/install.ps1` | 설치(백업 · 해시 대조) |

## 빌드 · 시험 · 설치

```powershell
cd src
dotnet test DH.CivilCoord.Tests\DH.CivilCoord.Tests.csproj
dotnet build DH.CivilCoord.Revit\DH.CivilCoord.Revit.csproj -c Release
powershell -ExecutionPolicy Bypass -File ..\scripts\install.ps1
```

기록: `%APPDATA%\DH.CivilCoord\log.txt`(단계마다 한 줄).
