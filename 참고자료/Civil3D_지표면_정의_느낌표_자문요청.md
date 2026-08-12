# Civil 3D 2026 — 붙여넣기(Paste)로 만든 지표면의 정의 탭 느낌표(⚠)가 API로는 안 지워짐

> **자문 요청 문서** · 2026-08-12 · DH.Grading 애드인 (Civil 3D 2026 / .NET 8)
> 작성 목적: 아래 증상의 원인과 해결책에 대한 **외부 전문가 의견**을 구하기 위함.

---

## 1. 한 줄 요약

**`PasteSurface`로 합성하고 `CreateSnapshot`으로 굳힌 TIN 지표면**의 **지표면 특성 → 정의 탭** 작업 목록에서
**모든 작업 항목에 노란 느낌표(⚠)**가 뜬다.
**API(`Rebuild()` + `RebuildSnapshot()`)로는 안 지워지고, Prospector에서 마우스 오른쪽 → `스냅샷 재작성`을 눌러야만 사라진다.**

---

## 2. 환경

| | |
|---|---|
| 제품 | Autodesk Civil 3D **2026** (R25.1) |
| 애드인 | .NET 8 / C# · `AeccDbMgd.dll` 참조 |
| 대상 객체 | `TinSurface` (붙여넣기로 만든 합성면) |
| 도면 크기 | 합성면 삼각형 약 65,000개 |

---

## 3. 문제의 지표면이 만들어지는 방식

부지 정지(절·성토) 플러그인이다. 세 지표면을 **순서대로 붙여넣어** 하나의 합성면을 만든다.

```
정지면_DH  =  원지반(Surface1)  ⊕  가상성토_DH  ⊕  가상절토_DH
```

### 실제 코드 (핵심만)

```csharp
public static ObjectId Composite(Database db, Transaction tr, string name,
    IReadOnlyList<(ObjectId id, string label)> pasteOrder, out string log,
    bool freezeEach = false, ObjectId protect = default)
{
    EraseSurfacesByBaseName(tr, name, protect);
    ObjectId id = TinSurface.Create(db, UniqueName(db, tr, name));
    var final = (TinSurface)tr.GetObject(id, OpenMode.ForWrite);

    foreach (var (sid, label) in pasteOrder)
    {
        if (sid.IsNull) continue;
        try
        {
            final.PasteSurface(sid);
            if (freezeEach) Freeze(final);
            else { try { final.Rebuild(); } catch { } }   // ← 현재 이 경로
        }
        catch (Exception ex) { /* 로그 */ }
    }
    try { Freeze(final); } catch { }        // ← 다 붙인 뒤 한 번만 굳힌다
    return id;
}

private static void Freeze(TinSurface s)
{
    try { s.Rebuild(); } catch { }
    try { if (s.HasSnapshot) s.RebuildSnapshot(); else s.CreateSnapshot(); } catch { }
}
```

### 그 결과 만들어지는 정의 (특성 → 정의 탭, 실제 스샷)

```
⚠ 붙여넣기     Surface1 지표면 추가
⚠ 붙여넣기     가상성토_DH 지표면 추가
⚠ 붙여넣기     가상절토_DH 지표면 추가
⚠ 스냅샷 작성   사용자 작성
```

**네 항목 모두 ⚠.** Prospector 트리에서도 해당 지표면의 **`정의`** 노드에 ⚠가 붙는다.

> **참고**: 초기 판에서는 붙여넣기마다 `Freeze()`를 불러 **스냅샷이 정의의 두 번째 줄**에 박혀 있었다.
> 그러면 *"빌드는 스냅샷 작업에서 시작하고 그 이전 작업은 무시된다"* 는 규칙 때문에
> **원지반만 스냅샷에 구워지고 나머지 붙여넣기는 소스에 매달린 채** 남는다.
> 그것을 고쳐 스냅샷을 **맨 끝**으로 옮겼다(위 코드). 그래도 ⚠는 그대로다.

---

## 4. 관찰된 사실 (전부 실측)

### 4-1. API 플래그는 전부 깨끗한데 UI에는 ⚠가 뜬다

정지면 생성이 끝난 뒤, **그리고 트랜잭션을 커밋한 뒤 새 트랜잭션에서 다시 읽어도**:

```
'정지면_DH'   삼각형=64978  보임=True   구식(IsOutOfDate)=False
                                        스냅샷구식(IsSnapshotOutOfDate)=False
                                        스냅샷있음(HasSnapshot)=True
                                        자동재작성(AutoRebuild)=False
'정지순수_DH' 삼각형=1768   보임=False  구식=False · 스냅샷구식=False · 스냅샷있음=True
```

**즉 `Surface.IsOutOfDate`와 `Surface.IsSnapshotOutOfDate`가 모두 `False`인 상태에서 UI에는 ⚠가 표시된다.**

### 4-2. 실험 A — '지표면 재작성'만 눌러도 **하나도** 안 사라진다

정지면 생성 직후 스샷 → Prospector에서 **'지표면 재작성'만** 클릭 → 다시 스샷.
**두 스샷이 픽셀 단위로 동일**했다. 네 항목 모두 ⚠ 유지.

### 4-3. 실험 B — `스냅샷 재작성`을 누르면 **전부** 사라진다

마우스 오른쪽 → **`스냅샷 재작성`**. 이것만이 ⚠를 지운다.
사용자 표현: *"무조건 마우스 오른쪽 버튼으로 스냅샷 재작성을 눌러줘야만 없어져."*

> 이 자체는 문서와 앞뒤가 맞는다 — 스냅샷이 있으면 `Rebuild()`는 스냅샷에서 시작하므로
> **그 앞의 붙여넣기 항목을 다시 밟지 않는다.** 앞 항목을 다시 훑는 것은 `RebuildSnapshot()`뿐이다.
> **문제는, 우리 코드도 `RebuildSnapshot()`을 부르는데 지워지지 않는다는 것이다.**

### 4-4. 실험 C — 저장·재오픈해도 남는다

**다른 이름으로 저장 → 닫기 → 다시 열기.** ⚠ 그대로.
→ **화면/트리 갱신 문제가 아니라 도면에 저장된 실제 상태**다.

### 4-5. `SurfaceOperation`에는 상태를 읽을 공개 속성이 없다

설치된 `C:\Program Files\Autodesk\AutoCAD 2026\C3D\AeccDbMgd.dll`을 직접 조사한 결과,
모든 정의 작업의 부모 클래스 `SurfaceOperation`의 **공개 멤버는 다음이 전부**다:

```
Guid (읽기전용) · Enabled (체크박스) · MoveUp / MoveDown / MoveToTop / MoveToBottom · Dispose
```

**작업 한 줄의 '구식/수정됨' 여부를 읽는 공개 속성이 존재하지 않는다.**
(`SurfaceOperationCollection.GetOperationStatus()`는 이름이 비슷하나
`Enabled` 체크박스의 3상태 집계 `None/AllFalse/AllTrue/Varies`일 뿐이다.)

→ 그래서 코드로는 **⚠의 유무를 판정할 방법 자체가 없다.** 판정은 사람이 대화상자를 열어 보는 수밖에 없다.

### 4-6. 공식 문서가 말하는 ⚠의 종류

Autodesk 공식 문서에 따르면 정의 탭 목록의 표시는 세 가지다:

| 표시 | 뜻 |
|---|---|
| `Item modified` | 항목이 **추가된 뒤에 수정됨** |
| `Item not found` | **대화상자를 열 때** 항목을 못 찾음 |
| `Item not OK` | 표면 변경 탓에 항목 추가가 **실패**함 |

우리 증상이 이 중 무엇인지 **아직 확정하지 못했다.**

---

## 5. 시도한 것과 결과 (전부 실패)

| 판 | 시도 | 결과 |
|---|---|---|
| 1 | `Rebuild()` → 스냅샷 → **`Rebuild()`** (트레일링 재작성) | 실패 |
| 2 | 도면의 **모든** 지표면을 구식이 없어질 때까지 **반복** 재작성 | 실패 |
| 3 | **소스 먼저 → 합성면 나중** 순서로 재작성 | **더 나빠짐** |
| 4 | `Composite`가 붙여넣기마다 굳히던 것을 **맨 끝 한 번**으로 (스냅샷을 정의 끝으로 이동) | **부분 성공** — 정의 순서는 고쳐졌으나 ⚠는 남음 |
| 5 | 트레일링 `Rebuild()` 제거 (짓고 → 찍고 끝) | 실패 |
| 6 | **표면 하나마다 트랜잭션을 열고·하고·커밋** (수동 클릭과 같은 모양) | 실패 |

### 6번(트랜잭션 분리)의 실제 코드

수동 클릭은 클릭마다 작업이 끝나고 커밋된다. 그것을 흉내 냈으나 **효과 없음**:

```csharp
// ①소스 전부 → ②합성면 전부 → ③스냅샷 전부, 각 단계를 표면 하나마다 커밋
private static int StageOne(Database db, List<ObjectId> ids, bool snapshot)
{
    int n = 0;
    foreach (var sid in ids)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var w = (Surface)tr.GetObject(sid, OpenMode.ForWrite);
            if (snapshot) { if (w.HasSnapshot) { w.RebuildSnapshot(); n++; } }
            else          { w.Rebuild(); n++; }
            tr.Commit();
        }
        catch { }
    }
    return n;
}
```

로그상 호출은 전부 성공(예외 없음)한다. 그런데도 ⚠는 남는다.

---

## 6. 참고 — 관련될 수 있는 다른 코드 경로

### 6-1. 합성 이후 소스 지표면의 가시성을 끈다

```csharp
public static void IsolateSurfaces(Transaction tr, string? keepBaseName)
{
    foreach (ObjectId sid in civilDoc.GetSurfaceIds())
    {
        bool keep = /* keepBaseName과 이름 비교 */;
        var eRead = (Entity)tr.GetObject(sid, OpenMode.ForRead);
        if (eRead.Visible == keep) continue;              // 같은 값이면 안 쓴다
        var e = (Entity)tr.GetObject(sid, OpenMode.ForWrite);
        e.Visible = keep;
    }
}
```

> Autodesk 커뮤니티에 *"객체를 숨기면 지표면이 구식이 되고 숨김을 해제해도 구식으로 남는다"* 는
> 결함 보고가 있어, 이 경로를 의심해 값이 같으면 쓰지 않도록 고쳤다. 그래도 ⚠는 남는다.

### 6-2. 토량 임시 체적표면을 만들었다 지운다

`TinVolumeSurface.Create(db, "_DH토량임시", 원지반, 정지면_DH)` → 체적 읽기 → 삭제.
**이 작업은 마지막 재작성보다 앞에서 일어난다**(코드로 확인).

---

## 7. 묻고 싶은 것

1. **정의 탭 목록의 ⚠는 정확히 어떤 상태를 나타내는가?**
   `Item modified` / `Item not found` / `Item not OK` 중 무엇인지 **코드로 판별할 방법**이 있는가?
   (`SurfaceOperation`에 공개 속성이 없음을 확인했다. 비공개/COM/다른 경로가 있는가?)

2. **`Surface.RebuildSnapshot()` (API)와 Prospector의 `스냅샷 재작성`(UI)은 같은 일을 하는가?**
   다르다면 UI가 추가로 무엇을 하는가? UI와 동등한 API 호출은 무엇인가?

3. **`PasteSurface` + `CreateSnapshot` 조합에서 붙여넣기 항목의 ⚠를 API로 지우는 방법**이 있는가?

4. **스냅샷이 정의의 맨 끝에 있을 때, 그 앞의 붙여넣기 항목은 잉여인가?**
   잉여라면 `Surface.Operations`에서 **붙여넣기 항목을 제거**해도 형상이 유지되는가?
   (`SurfaceOperationCollection.RemoveAt(int)`이 어셈블리에 실존함은 확인했다.)
   제거한 표면을 **다음 실행에서 다시 `PasteSurface`의 소스로** 쓸 때 문제가 없는가?

5. **`AutoRebuild = true`로 두면 달라지는가?**
   현재 모든 표면이 `AutoRebuild = False`다. 다만 공식 문서가
   *"`AutoRebuild`와 `IsOutOfDate`가 **둘 다** true일 때"* 라고 적고 있고 실측 `IsOutOfDate = False`라,
   조건이 열리지 않을 것으로 보인다. 맞는가?

6. **이 ⚠가 실제로 해로운가?** 형상·토공량·종단/횡단 결과는 모두 정상으로 보인다.
   무시해도 되는 표시인가, 아니면 나중에 실제 문제(저장 실패·형상 손실 등)로 이어지는가?

---

## 8. English summary (for international forums)

**Environment**: Civil 3D 2026 (R25.1), .NET 8 add-in, `AeccDbMgd.dll`.

**Symptom**: A `TinSurface` built by pasting three surfaces and then snapshotted shows a **yellow warning
icon (⚠) on every operation row** in *Surface Properties → Definition*, and on the `Definition` node in
Prospector. It is **only cleared by right-click → Rebuild Snapshot in Prospector**. Calling
`Surface.Rebuild()` and `Surface.RebuildSnapshot()` from the API does **not** clear it, even though the calls
succeed without exception.

**Definition contents** (snapshot is last):

```
⚠ Paste     Add Surface1
⚠ Paste     Add 가상성토_DH
⚠ Paste     Add 가상절토_DH
⚠ Snapshot  User created
```

**Measured facts**:
- `Surface.IsOutOfDate == false` and `Surface.IsSnapshotOutOfDate == false`, verified **after the transaction
  is committed, in a fresh read transaction** — yet the UI shows ⚠.
- Clicking *Rebuild Surface* alone clears **nothing** (before/after screenshots identical).
- Saving to a new file, closing and reopening: ⚠ **persists** → it is persisted state, not a UI refresh issue.
- `SurfaceOperation` exposes only `Guid`, `Enabled`, `MoveUp/Down/ToTop/ToBottom`, `Dispose` — there is
  **no public API to read per-operation staleness**.

**Things tried, all ineffective**: reordering `Rebuild`/`RebuildSnapshot`; iterating rebuilds to convergence;
rebuilding sources before composites; moving the snapshot to the end of the definition (this *did* fix the
definition ordering but not the ⚠); removing the trailing `Rebuild()`; **committing each surface in its own
transaction** to mimic the manual click sequence.

**Questions**: (1) What exactly does the ⚠ represent and can it be read programmatically?
(2) Does the Prospector *Rebuild Snapshot* command do something beyond `Surface.RebuildSnapshot()`?
(3) Is it safe to delete the Paste operations from `Surface.Operations` when a snapshot is the last operation,
and to later use such a surface as a paste source?
(4) Is this ⚠ actually harmful, or cosmetic?

---

## 9. 이 문서와 함께 보면 좋은 것

- 프로젝트 작업기록: `civil3d-grading/작업과정.md` — §28~§30에 이 문제의 전체 경과
- 진단 로그: `civil3d-grading/DHGRADE_진단.log` — 위 실측값의 원본
- 핵심 소스: `civil3d-grading/src/DH.Grading.Civil/GradingBuilder.cs`
  (`Composite` · `Freeze` · `RebuildSurfacesStaged` · `StripPasteOperations` · `IsolateSurfaces`)
