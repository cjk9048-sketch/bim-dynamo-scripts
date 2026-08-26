using Autodesk.Revit.DB;

namespace DH.Takeoff.Revit;

/// <summary>
/// 치수 자동입력 — 부재 치수를 읽어 L1/W1/H(미터)에 채운다.
/// 벽은 실제 길이·두께·높이로 정확히, 그 외는 경계상자.
/// '반듯함/비정형'은 (솔리드 부피 ÷ 경계상자 부피) 비율로 자동 판별(반듯할수록 1에 가까움).
/// 같은 이름의 칸이 여러 개여도(우리 칸 + DH-Water 칸) 모두에 기록한다.
/// </summary>
public static class DimensionExtractor
{
    private const double FtToM = 0.3048;

    /// <summary>대상 카테고리의 모든 인스턴스.</summary>
    public static ICollection<ElementId> CollectApplicable(Document doc)
    {
        var filter = new ElementMulticategoryFilter(new[]
        {
            BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_GenericModel,
        });
        return new FilteredElementCollector(doc).WhereElementIsNotElementType()
            .WherePasses(filter).ToElementIds();
    }

    /// <summary>대상을 '반듯함(simple)'과 '비정형(irregular)'으로 분류.</summary>
    public static (List<ElementId> simple, List<ElementId> irregular) Classify(Document doc, ICollection<ElementId> ids)
    {
        var simple = new List<ElementId>();
        var irregular = new List<ElementId>();
        foreach (var id in ids)
        {
            var el = doc.GetElement(id);
            if (el == null) continue;
            // 정육면체/직육면체(박스)만 '반듯', 그 외(노치·구멍·경사·곡면·부속) 전부 '비정형'.
            // 박스는 비스듬히 놓여도 반듯으로 인정된다.
            (IsCuboid(el) ? simple : irregular).Add(id);
        }
        return (simple, irregular);
    }

    /// <summary>주어진 부재들에 L1/W1/H 기록. 채운 부재 수 반환.</summary>
    public static int Fill(Document doc, ICollection<ElementId> ids)
    {
        int filled = 0;
        using var tx = new Transaction(doc, "DH 치수 자동입력");
        tx.Start();
        foreach (var id in ids)
        {
            var el = doc.GetElement(id);
            if (el == null) continue;
            if (!TryGetDims(el, out double l1, out double w1, out double h)) continue;
            bool any = WriteMeters(el, "L1", l1) | WriteMeters(el, "W1", w1) | WriteMeters(el, "H", h);
            if (any) filled++;
        }
        tx.Commit();
        return filled;
    }

    // --- 치수 추출 ---
    private static bool TryGetDims(Element el, out double l1, out double w1, out double h)
    {
        l1 = w1 = h = 0;

        if (el is Wall wall) // 벽: 두께·높이는 알려진 값, 길이는 '실제 체적'에서 역산(접합으로 잘린 net 길이)
        {
            double wF = wall.Width;                                  // 두께
            var wbb = el.get_BoundingBox(null);
            double hF = wbb != null ? wbb.Max.Z - wbb.Min.Z          // 높이=수직 범위(벽은 수직이라 정확)
                       : wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? 0;
            double volF = SolidVolume(el);                            // 실제 덩어리 체적(접합 반영)
            if (wF > 1e-9 && hF > 1e-9 && volF > 1e-9)
            {
                l1 = M(volF / (wF * hF)); w1 = M(wF); h = M(hF);      // 길이 = 체적 ÷ 두께 ÷ 높이 = net 길이
                return l1 > 0;
            }
            // 폴백: 위치선 길이
            double len = (wall.Location as LocationCurve)?.Curve?.Length ?? 0;
            l1 = M(len); w1 = M(wF); h = M(hF);
            return l1 > 0;
        }

        if (el is Floor floor) // 바닥: 두께=H, 평면 경계상자=L1·W1
        {
            var fbb = el.get_BoundingBox(null);
            double t = floor.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)?.AsDouble()
                       ?? (fbb != null ? fbb.Max.Z - fbb.Min.Z : 0);
            double fx = fbb != null ? fbb.Max.X - fbb.Min.X : 0;
            double fy = fbb != null ? fbb.Max.Y - fbb.Min.Y : 0;
            l1 = M(fy); w1 = M(fx); h = M(t); // L=세로(북/Y), W=가로(X)
            return l1 > 0 || w1 > 0;
        }

        // 보·기둥: 솔리드의 '방향 경계상자'(회전 무관 정확) + 정확한 한 축은 별도 소스
        long cat = el.Category?.Id.Value ?? 0;
        bool isBeam = cat == (long)BuiltInCategory.OST_StructuralFraming;
        bool isCol = cat == (long)BuiltInCategory.OST_StructuralColumns;
        if ((isBeam || isCol) && OrientedExtents(el) is double[] e) // e: 내림차순(e0≥e1≥e2)
        {
            if (isBeam) // 길이=중심선(정확), 단면=나머지 두 축(큰 것=춤=H, 작은 것=폭=W1)
            {
                double len = (el.Location as LocationCurve)?.Curve?.Length ?? e[0];
                l1 = M(len); h = M(e[1]); w1 = M(e[2]);
                return l1 > 0;
            }
            // 기둥: 높이=수직 범위(정확), 단면=나머지 두 축
            var cbb = el.get_BoundingBox(null);
            double hz = cbb != null ? cbb.Max.Z - cbb.Min.Z : e[0];
            l1 = M(Math.Max(e[1], e[2])); w1 = M(Math.Min(e[1], e[2])); h = M(hz);
            return h > 0 || l1 > 0;
        }

        var bb = el.get_BoundingBox(null); // 그 외(일반모델·시스템): 경계상자
        if (bb == null) return false;
        double dx = bb.Max.X - bb.Min.X, dy = bb.Max.Y - bb.Min.Y, dz = bb.Max.Z - bb.Min.Z;
        h = M(dz); l1 = M(dy); w1 = M(dx); // L=세로(북/Y), W=가로(X), H=높이(Z)
        return l1 > 0 || w1 > 0 || h > 0;
    }

    // --- 직육면체 판정: 박스이면 변 치수(내림차순), 아니면 null ---
    private static bool IsCuboid(Element el) => RectBoxDims(el) != null;
    private static double[]? OrientedExtents(Element el) => RectBoxDims(el);

    /// <summary>
    /// '진짜 직육면체'면 세 변 치수(내림차순 e0≥e1≥e2)를 반환, 아니면 null.
    /// 조건: 솔리드 1개 + 평면 6면 + 면 법선이 '서로 직각인 3축'(평행사변형·마름모 단면 배제).
    /// 회전(비스듬)과 무관하게 정확. 변 치수는 부피÷면적으로 산출.
    /// </summary>
    private static double[]? RectBoxDims(Element el)
    {
        var s = LargestSolid(el);
        if (s == null) return null;
        if (s.Faces.Size != 6) return null;            // 면 6개 아님 → 박스 아님(노치·구멍·곡면)
        if (SolidVolume(el) > s.Volume * 1.01) return null; // 부속 솔리드 있음 → 비정형

        var normals = new List<XYZ>();
        var areas = new List<double>();
        foreach (Face f in s.Faces)
        {
            if (f is not PlanarFace pf) return null;   // 곡면 → 박스 아님
            normals.Add(pf.FaceNormal.Normalize());
            areas.Add(pf.Area);
        }

        // 법선을 '축'으로 묶기(법선과 그 반대는 같은 축). 직육면체면 정확히 3축.
        var axes = new List<XYZ>();
        foreach (var n in normals)
        {
            bool found = false;
            foreach (var a in axes) if (Math.Abs(a.DotProduct(n)) > 0.999) { found = true; break; }
            if (!found) axes.Add(n);
        }
        if (axes.Count != 3) return null;
        // 세 축이 서로 직각이어야 진짜 직육면체(평행사변형/마름모 단면이면 직각 아님 → 배제)
        if (Math.Abs(axes[0].DotProduct(axes[1])) > 1e-3) return null;
        if (Math.Abs(axes[0].DotProduct(axes[2])) > 1e-3) return null;
        if (Math.Abs(axes[1].DotProduct(axes[2])) > 1e-3) return null;

        // 각 축 변 길이 = 부피 ÷ (그 축에 수직인 면의 면적)
        double vol = s.Volume;
        var dims = new double[3];
        for (int k = 0; k < 3; k++)
        {
            double area = 0;
            for (int j = 0; j < normals.Count; j++)
                if (Math.Abs(normals[j].DotProduct(axes[k])) > 0.999) { area = areas[j]; break; }
            if (area <= 1e-9) return null;
            dims[k] = vol / area;
        }
        Array.Sort(dims);
        Array.Reverse(dims);
        return dims;
    }

    public static Solid? LargestSolid(Element el)
    {
        try
        {
            var opt = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false, DetailLevel = ViewDetailLevel.Medium };
            var geo = el.get_Geometry(opt);
            return geo == null ? null : LargestSolidIn(geo);
        }
        catch { return null; }
    }

    private static Solid? LargestSolidIn(GeometryElement geo)
    {
        Solid? best = null;
        double bv = 0;
        foreach (GeometryObject g in geo)
        {
            if (g is Solid s && s.Volume > bv) { best = s; bv = s.Volume; }
            else if (g is GeometryInstance gi)
            {
                var inner = LargestSolidIn(gi.GetInstanceGeometry());
                if (inner != null && inner.Volume > bv) { best = inner; bv = inner.Volume; }
            }
        }
        return best;
    }

    private static double SolidVolume(Element el)
    {
        try
        {
            var opt = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false, DetailLevel = ViewDetailLevel.Medium };
            var geo = el.get_Geometry(opt);
            return geo == null ? 0 : SumSolids(geo);
        }
        catch { return 0; }
    }

    private static double SumSolids(GeometryElement geo)
    {
        double v = 0;
        foreach (GeometryObject g in geo)
        {
            if (g is Solid s && s.Volume > 0) v += s.Volume;
            else if (g is GeometryInstance gi) v += SumSolids(gi.GetInstanceGeometry());
        }
        return v;
    }

    private static double M(double feet) => Math.Round(feet * FtToM, 4);

    /// <summary>같은 이름의 칸 중 값이 있는 것을 미터로 읽어 반환(없으면 0). 길이칸→미터 환산, 숫자칸→그대로.</summary>
    public static double ReadMeters(Element el, string name)
    {
        foreach (Parameter p in el.GetParameters(name))
        {
            if (p.StorageType != StorageType.Double || !p.HasValue) continue;
            double raw = p.AsDouble();
            if (Math.Abs(raw) < 1e-12) continue;
            try
            {
                if (p.Definition.GetDataType().TypeId == SpecTypeId.Length.TypeId)
                    return Math.Round(raw * FtToM, 4);
            }
            catch { }
            return Math.Round(raw, 4);
        }
        return 0;
    }

    /// <summary>같은 이름의 모든 문자칸에 문자열 기록. 트랜잭션 안에서 호출할 것.</summary>
    public static bool WriteString(Element el, string name, string value)
    {
        bool wrote = false;
        foreach (Parameter p in el.GetParameters(name))
        {
            if (p.IsReadOnly || p.StorageType != StorageType.String) continue;
            p.Set(value);
            wrote = true;
        }
        return wrote;
    }

    /// <summary>같은 이름의 모든 칸에 기록(숫자칸=미터, 길이칸=내부 피트). 트랜잭션 안에서 호출할 것.</summary>
    public static bool WriteMeters(Element el, string name, double meters)
    {
        bool wrote = false;
        foreach (Parameter p in el.GetParameters(name))
        {
            if (p.IsReadOnly || p.StorageType != StorageType.Double) continue;
            double store = meters;
            try
            {
                if (p.Definition.GetDataType().TypeId == SpecTypeId.Length.TypeId)
                    store = meters / FtToM;
            }
            catch { }
            p.Set(store);
            wrote = true;
        }
        return wrote;
    }
}
