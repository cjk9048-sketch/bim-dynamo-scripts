using System.Linq;
using Autodesk.Revit.DB;

namespace DH.Takeoff.Revit;

/// <summary>
/// 자동 겹침 공제 — 부재 교차를 우선순위로 '한 번만' 계산한다.
/// 각 부재의 솔리드를 '이기는(우선순위 높은) 부재들'로 잘라내(Difference) 남는 순(net) 체적을 구하고,
/// 그 순체적에 맞게 L1(기둥은 H)을 보정한다. 보정은 기하(net체적)+단면(W1·H)로만 계산하므로
/// 재실행해도 같은 결과(멱등). 우선순위: 기초>기둥>벽>보>슬래브>일반모델, 동순위는 ElementId 작은 쪽이 소유.
/// </summary>
public static class OverlapResolver
{
    private const double FtToM = 0.3048;
    private const double Ft3ToM3 = FtToM * FtToM * FtToM;

    private static readonly (BuiltInCategory cat, int prio)[] Cats =
    {
        (BuiltInCategory.OST_StructuralFoundation, 0),
        (BuiltInCategory.OST_StructuralColumns,    1),
        (BuiltInCategory.OST_Walls,                2),
        (BuiltInCategory.OST_StructuralFraming,    3),
        (BuiltInCategory.OST_Floors,               4),
        (BuiltInCategory.OST_GenericModel,         5),
    };

    private sealed class Item
    {
        public required Element El;
        public required Solid Solid;
        public required BoundingBoxXYZ Bb;
        public int Prio;
        public long Id;
    }

    public static string Resolve(Document doc)
    {
        // 1) 대상 수집(솔리드·경계상자·우선순위)
        var items = new List<Item>();
        foreach (var (cat, prio) in Cats)
        {
            foreach (var el in new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType())
            {
                var s = DimensionExtractor.LargestSolid(el);
                var bb = el.get_BoundingBox(null);
                if (s == null || bb == null || s.Volume < 1e-9) continue;
                items.Add(new Item { El = el, Solid = s, Bb = bb, Prio = prio, Id = el.Id.Value });
            }
        }

        // 2) 각 부재를 '이기는' 부재들로 잘라내 순체적 계산(겹침 1회만)
        var nets = new List<(Item it, double netFt3)>();
        foreach (var e in items)
        {
            Solid net = e.Solid;
            foreach (var w in items)
            {
                if (ReferenceEquals(w, e) || !Beats(w, e) || !BbOverlap(e.Bb, w.Bb)) continue;
                try
                {
                    var diff = BooleanOperationsUtils.ExecuteBooleanOperation(net, w.Solid, BooleanOperationsType.Difference);
                    if (diff != null) net = diff;
                }
                catch { /* 일부 교차는 불리언 실패 가능 → 그 부재만 건너뜀(과소공제 방지 위해 무시) */ }
            }
            nets.Add((e, net.Volume));
        }

        // 3) 순체적에 맞게 치수 보정(트랜잭션)
        int adjusted = 0;
        double totalDedFt3 = 0;
        using (var tx = new Transaction(doc, "DH 겹침 공제"))
        {
            tx.Start();
            foreach (var (it, netFt3) in nets)
            {
                double gross = it.Solid.Volume;
                double ded = gross - netFt3;          // '부재 겹침'량(개구부는 양쪽 솔리드에 다 있어 상쇄 → 개구부 무관)
                if (ded <= gross * 0.001) continue;   // 겹침이 거의 없으면 건드리지 않음

                double w1 = DimensionExtractor.ReadMeters(it.El, "W1");
                double h = DimensionExtractor.ReadMeters(it.El, "H");
                double l1 = DimensionExtractor.ReadMeters(it.El, "L1");

                // ★ 개구부 체적을 net에 '되더해' L1/H에는 개구부가 녹지 않게 한다.
                //   (개구부는 E/Y 칸의 별도 마이너스 항이 빼므로, net으로 덮어쓰면 개구부가 이중 공제됨)
                //   기하만으로 절대 재계산하므로 여러 번 실행해도 같은 값(멱등).
                double openM3 = OpeningFinder.OpeningVolumeM3(it.El, w1, h);
                double targetM3 = netFt3 * Ft3ToM3 + openM3;

                bool isCol = it.El.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;
                if (isCol)
                {
                    double area = l1 * w1;            // 기둥: 높이에서 공제
                    if (area <= 1e-9) continue;
                    DimensionExtractor.WriteMeters(it.El, "H", Math.Round(targetM3 / area, 4));
                }
                else
                {
                    double area = w1 * h;            // 보·벽·기타: 길이에서 공제
                    if (area <= 1e-9) continue;
                    DimensionExtractor.WriteMeters(it.El, "L1", Math.Round(targetM3 / area, 4));
                }
                adjusted++;
                totalDedFt3 += ded;
            }
            tx.Commit();
        }

        return $"겹침 공제 완료.\n" +
               $"  • 검토 부재: {items.Count}개\n" +
               $"  • 공제 적용: {adjusted}개\n" +
               $"  • 총 공제 체적: {Math.Round(totalDedFt3 * Ft3ToM3, 3)} m³\n" +
               $"  • 우선순위: 기초>기둥>벽>보>슬래브>일반모델 (동순위는 먼저 만든 부재 소유)\n\n" +
               "※ 공제는 L1(기둥은 H)에 반영됩니다. 단면(W1·H 등)을 먼저 채운 부재만 보정됩니다.";
    }

    // w가 e를 이김(소유) = 우선순위 높음, 동순위면 Id 작은 쪽
    private static bool Beats(Item w, Item e) => w.Prio < e.Prio || (w.Prio == e.Prio && w.Id < e.Id);

    private static bool BbOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b) =>
        a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
        a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y &&
        a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
}
