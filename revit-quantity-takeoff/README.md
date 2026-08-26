# revit-quantity-takeoff

Revit 구조 모델의 객체를 순회해 **산출근거(산출식)가 포함된 엑셀 수량산출서**를
자동 생성합니다. 콘크리트 체적(m³)·거푸집 면적(m²)·부재 수량(EA)·길이(m)를 산출합니다.

## 설계 원칙

**Dynamo for Revit 노드는 치수 추출만**, **계산·엑셀 작성은 외부 순수 Python 모듈**이
담당합니다. 덕분에 Revit 없이도 테스트·디버깅이 가능하고, VS Code에서 Claude가
로직(`scripts/takeoff/`)을 직접 수정할 수 있습니다.

```
Revit 모델
  └─ Dynamo Python 노드 (docs/dynamo_node.py): 부재 → dict(치수, m)
       └─ takeoff.runner.run(members, out_path)
            ├─ models.py : 입력 검증·그룹 집계
            ├─ calc.py   : 카테고리별 산출식 + 계산값
            └─ excel.py  : openpyxl 수량산출서 (집계/콘크리트/거푸집/수량/길이 시트)
```

## 구성

| 경로 | 설명 |
|------|------|
| `scripts/takeoff/models.py` | `Member`/`LineItem` 데이터 구조, 그룹 키 |
| `scripts/takeoff/calc.py` | 카테고리별 콘크리트·거푸집·수량·길이 산출식 |
| `scripts/takeoff/excel.py` | openpyxl 수량산출서 생성 |
| `scripts/takeoff/overlap.py` | 교차부 겹침 **우선순위 귀속**(한 번만 계산) |
| `scripts/takeoff/runner.py` | 겹침 귀속 → 그룹 집계 → 엑셀 오케스트레이션 |
| `scripts/make_example.py` | 샘플로 예시 엑셀 생성 (데모) |
| `docs/dynamo_node.py` | Dynamo 노드 템플릿(치수 수동/파라미터 추출) |
| `docs/dynamo_joint_detection.py` | Dynamo 노드(보·기둥 **겹침 자동탐지**) |
| `docs/formwork_rules.md` | 거푸집·체적 산정 기준 + 겹침 귀속 원칙 |
| `docs/PLAN.md` | 전체 기획·설계·결정사항·로드맵 (검토용) |
| `samples/sample_members.json` | 입력 데이터 예시 |
| `tests/` | unittest (Revit 없이 실행) |

## 빠른 시작 (모델 없이 데모)

```powershell
py -m pip install openpyxl
py scripts/make_example.py        # samples/수량산출서_예시.xlsx 생성
```

## 테스트

```powershell
py -m unittest discover -s tests
```

## 입력 데이터 형식 (부재 dict)

치수 단위는 **미터(m)**. 카테고리별 필수 치수:

| 카테고리 | 필수 치수 키 |
|----------|-------------|
| `Columns` | `b`, `h`, `height` |
| `StructuralFraming` | `b`, `h`, `length` |
| `Foundations` | `b`, `l`, `h` |
| `Floors` | `area`, `thickness` (`perimeter` 선택) |
| `Walls` | `length`, `height`, `thickness` |

공통: `mark`(부재기호), `type_name`(규격), `level`(층), `count`(개수). 동일 규격은
자동으로 묶여 개수로 집계됩니다.

**교차부 겹침 공제** (선택): 보·벽에 `deduct_length`(양단 겹침 합, m), 기둥에
`deduct_height`(보춤 등, m)를 주면 유효치수로 계산합니다. **산출식에는 순값만**
나오고(예: `0.4×0.7×5.1×12`) 공제 내역은 비고에 표기됩니다.

또는 부재 쌍의 겹침을 `overlaps`로 넘기면 **우선순위 귀속**으로 겹침을 한 번만
계산합니다 (아래 참고):

```python
runner.run(members, out_path,
           overlaps=[{"a": 0, "b": 3, "length": 0.25}])  # a,b = members 인덱스
```

```json
{"category": "Columns", "mark": "C1", "type_name": "RC 400x600",
 "level": "1F", "count": 8, "b": 0.4, "h": 0.6, "height": 3.6}
```

## Dynamo 연동

1. Dynamo Python 노드 엔진을 **CPython3** 로 설정.
2. Dynamo CPython3 환경에 `openpyxl` 설치 (docs/dynamo_node.py 상단 주석 참고).
3. `docs/dynamo_node.py` 내용을 노드에 붙여넣고, 카테고리별 요소 리스트와 출력 경로를
   입력 포트에 연결. **패밀리별 파라미터 이름**(`b`,`h`,`단면폭` 등)은 프로젝트에 맞게 수정.
4. 실패 시 노드가 `*.error.log` 를 남기므로, 스크린샷 없이 Claude가 로그를 읽어 수정.

## 산정 기준 / 공제(겹침)

기본 산정식과 조정 포인트는 [docs/formwork_rules.md](docs/formwork_rules.md) 참고.
부재 교차부 **겹침길이 공제**는 `deduct_length`/`deduct_height` 입력으로 지원합니다.
