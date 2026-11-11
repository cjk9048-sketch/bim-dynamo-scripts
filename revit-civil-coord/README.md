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
5. **미리보기**(창 오른쪽 · 창 크기 조절 · 최대화 · 크기 기억) — **[평면 | 3D]**:
   - **평면**(북 = 위): Civil 개략 평면 선(기준점 파일의 `sketch`) · Civil 두 점 · 맞춘 자리의 Revit 부재 윤곽.
   - **3D**: Civil 3D 영역(최종지표면 · 옹벽 · 소단 · 사면 · 경계 3D 선) + Revit 구조물 입체. [지표면 반투명]으로 묻힌 구조물 보기.
     오른쪽 끌기 · Shift+휠 눌러 끌기 = 돌려 보기 · 휠 눌러 끌기 = 화면 이동 · 휠 = 확대/축소 · 두 번 누르기 = 전체 보기.
   - **손 맞춤**(두 화면 같은 값): 구조물 끌기 = 옮기기 · 둥근 손잡이 = 돌리기(Shift = 1° 단위) · 3D 위아래 손잡이 = 높이(«더할 값» 칸 · 1 cm) ·
     방향키 = 1 cm(Shift 10 cm) · Ctrl+←/→ = 0.01° · PageUp/PageDown = 높이 1 cm · 끄는 동안 Esc = 취소.
     두 점 맞춤 위에 더해져 IFC · 공유 좌표 · 모델에 넣기 모두 그 자리. 크기(비율)는 두 점 그대로. 창의 «손 맞춤» 줄 [되돌리기] = 두 점 맞춤 자리.
     Revit 점을 다시 찍거나 Civil 두 점이 바뀌면 손 맞춤은 지움(알림).
6. **높이**: Revit 레벨에 보이는 높이 그대로(EL 50.00 → 50.00 m). 필요하면 더할 값(m).
7. **[IFC 내보내기]**: 지금 3D 뷰에 보이는 부재 → Civil 좌표 IFC(3D 뷰가 아니면 기본 3D 뷰).
8. **[공유 좌표에 적용]**(선택): Revit 공유 좌표를 같은 Civil 좌표로(옮김 · 돌림만 — Revit은 비율을 못 담음) →
   Revit IFC · DWG · NWC를 «공유 좌표»로 내보내면 맞음. 두 거리가 다르면 끝점이 차의 반씩 어긋난다고 알림.
   쓴 뒤 두 점을 다시 재서 맞는지 확인하고, 안 맞으면 바꾸지 않음. Ctrl+Z로 되돌림.
9. **[모델에 넣기]**(Civil 3D 영역이 있을 때): Civil 최종지표면을 **정확한 면**(DirectShape — Civil 세모 그대로) ·
   **지형 솔리드**(Toposolid — Revit이 점으로 삼각망을 다시 만듦 · 옹벽 수직 단차는 경사로 바뀜) 가운데 고르거나 둘 다,
   3D 선도 고름 → 미리보기와 같은 자리(구조물 맞춤의 거꾸로 — 비율 · 손 맞춤 · 높이 포함)에 넣음. 다시 넣으면 지난 것을 바꿈. Ctrl+Z로 되돌림.

**Civil 3D 영역 파일**(`{이름}_3D영역.json` · `DH.CivilRegion3D` v1 — Civil 터미널과 1111 합의): 기준점 파일의 `region3d` 칸으로 같은 폴더에서 찾음.
3D 파일을 [Civil 파일 불러오기]로 직접 골라도 됨(짝 기준점 파일 `pointsFile`을 같이 읽음). 세모는 30만 개까지(Civil이 막음).

넣은 값은 rvt 안(프로젝트 정보)에 저장돼, 다음에는 [IFC 내보내기]만 누르면 됩니다.

## IFC 꼴

- IFC4 ReferenceView · 길이 m · 형상 = 세모 면(IfcTriangulatedFaceSet) — Revit이 그리는 입체 그대로(구멍 · 잘림 반영).
- Civil 좌표(옮김 · 돌림 · 높이)는 **IfcSite 배치** 하나에 · 부재 좌표는 Revit 내부 좌표(m) × 평면 비율(X · Y만).
- IfcSite 속성 묶음 `DH_CivilFit`: Civil 좌표 출처 · 두 점 거리 · 평면 비율 · 회전 · 높이 더함 · 손 맞춤(있을 때).
- 부재 종류 → IFC 종류(벽 · 슬래브 · 기초 · 기둥 · 보 · 계단 · 난간 · 문 · 창 · 지형 …) — Civil이 레이어 · 해치를 이것으로 가름.
- GUID = Revit IFC 내보내기와 같은 값 · 속성 묶음 `DH_Revit`(Revit ID · 범주 · 패밀리 · 유형 · 레벨 · 마크).
- 빠지는 것: Revit 링크 · 가져온 CAD · 형상 없는 부재(창 아래에 개수).

## 구성

| 경로 | 설명 |
|---|---|
| `src/DH.CivilCoord.Core` | 두 점 맞춤 + 손 맞춤(`Fit2D`) · IFC GUID · IFC 쓰기(`IfcWriter`) · Civil 파일 읽기(`CivilPoints` · `Region3D`) · 성긴 지표면(`TerrainLod`) — Revit 없이 시험 |
| `src/DH.CivilCoord.Revit` | 리본 · 창 · 평면/3D 미리보기(`PlanPreview` · `Preview3D`) · 점 찍기 · 형상 모으기(`Exporter`) · 레벨 높이 · 공유 좌표 · 모델에 넣기(`CivilSurface`) · 값 저장 |
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
