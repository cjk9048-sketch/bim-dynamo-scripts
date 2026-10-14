using Autodesk.Revit.DB;

namespace DH.Takeoff.Revit;

/// <summary>
/// 부재의 위치(DH_Zone)에 얽힌 Revit 쪽 일 — 지금 적힌 값 읽기, 확인용 3D 뷰에 위치 색 입히기.
/// 위치를 정하는 일 자체는 「위치·부위 정하기」 3D 팝업(PickEditor)이 한다(JACK 2026-10-13: 구역 네모 방식을 없앴다).
/// </summary>
public static class ZoneAssigner
{
    public const string ParamName = "DH_Zone";
    public const string CheckViewName = "DH 구역 확인";   // 10/12 부터 쓰던 뷰 이름 그대로(모델에 뷰가 또 생기지 않게)

    /// <summary>지금 적혀 있는 위치. 없으면 "".</summary>
    public static string Current(Element el) =>
        el.GetParameters(ParamName)
            .Where(p => p.StorageType == StorageType.String)
            .Select(p => p.AsString())
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim() ?? "";

    /// <summary>
    /// 확인용 3D 뷰를 찾아(없으면 만들어) 부재를 위치별 색으로 칠한다. 사용자가 쓰던 뷰는 건드리지 않는다.
    /// zoneOf = 부재 → 지금 위치("" = 위치 없음). order = 위치 이름의 차례(색을 이 차례로 준다 — 3D 팝업과 같은 색).
    /// legend = 위치 → 색 이름. 뷰를 못 만들면 null(problem 에 까닭).
    /// </summary>
    public static View3D? Paint(Document doc, IReadOnlyDictionary<ElementId, string> zoneOf, IReadOnlyList<string> order,
                                out Dictionary<string, string> legend, out string problem)
    {
        problem = "";
        legend = new Dictionary<string, string>();
        var colorOf = new Dictionary<string, Color>();
        // 차례에 든 이름이 먼저, 차례에 없는데 부재에 적혀 있는 이름(속성 창에서 넣은 것)은 그 뒤에 이름순.
        var zones = order.Where(z => z.Length > 0).Distinct().ToList();
        zones.AddRange(zoneOf.Values.Where(z => z.Length > 0 && !zones.Contains(z)).Distinct().OrderBy(z => z, StringComparer.Ordinal));
        for (int i = 0; i < zones.Count; i++)
        {
            var p = ZonePalette.Of(i);
            colorOf[zones[i]] = new Color(p.r, p.g, p.b);
            legend[zones[i]] = p.name;
        }
        var none = new Color(ZonePalette.None.r, ZonePalette.None.g, ZonePalette.None.b);

        try
        {
            using var tx = new Transaction(doc, "DH 위치 색 입히기");
            tx.Start();
            var view = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name == CheckViewName);
            if (view == null)
            {
                var type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
                if (type == null)
                {
                    tx.RollBack();
                    problem = "3D 뷰 유형이 없어 확인용 뷰를 만들지 못했습니다.";
                    return null;
                }
                view = View3D.CreateIsometric(doc, type.Id);
                view.Name = CheckViewName;
                try { view.DisplayStyle = DisplayStyle.Shading; } catch { }
                try { view.DetailLevel = ViewDetailLevel.Fine; } catch { }
            }

            var solidFill = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
            foreach (var (id, zone) in zoneOf)
            {
                var color = zone.Length > 0 ? colorOf[zone] : none;
                var ogs = new OverrideGraphicSettings();
                if (solidFill != null)
                {
                    ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                    ogs.SetSurfaceForegroundPatternColor(color);
                    ogs.SetSurfaceForegroundPatternVisible(true);
                }
                ogs.SetProjectionLineColor(new Color((byte)(color.Red / 2), (byte)(color.Green / 2), (byte)(color.Blue / 2)));
                try { view.SetElementOverrides(id, ogs); } catch { /* 이 뷰에서 덮어쓸 수 없는 요소는 건너뛴다 */ }
            }
            if (tx.Commit() != TransactionStatus.Committed)
            {
                problem = "색을 입히지 못했습니다(Revit 이 변경을 되돌림).";
                return null;
            }
            return view;
        }
        catch (Exception ex)
        {
            problem = "색을 입히지 못했습니다: " + ex.Message;
            return null;
        }
    }

    /// <summary>위치가 없는 부재를 칠하는 색의 이름.</summary>
    public static string NoZoneColorName => ZonePalette.None.name;
}
