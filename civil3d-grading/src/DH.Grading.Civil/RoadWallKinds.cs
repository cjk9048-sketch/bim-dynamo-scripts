using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★[JACK 1124 · 진입도로 옹벽 표준단면 2단계] 진입도로 옹벽의 단면 형식(절토 · 성토) — 이 함수 하나에서만 정한다.
/// 도로마다 [옹벽 설정] 팝업 [진입도로] 칸(<see cref="RoadDesign.WallCutKind"/> · <see cref="RoadDesign.WallFillKind"/>)이 먼저 ·
/// 빈 값(«계획부지와 같음») · 못 읽음 · 모르는 이름이면 부지 설정(옹벽 터파기 설정의 절토 · 성토 형식 — <see cref="WallListStore.ExcavOf"/>).</summary>
internal static class RoadWallKinds
{
    internal static (WallSection.Kind Cut, WallSection.Kind Fill) Of(Transaction tr, ObjectId alignId)
    {
        var st = WallListStore.ExcavOf(alignId.IsNull ? null : alignId.Database, out _);
        WallSection.Kind cut = st.CutKind, fill = st.FillKind;
        try
        {
            if (!alignId.IsNull && RoadDesign.Has(tr, alignId) && RoadDesign.Read(tr, alignId) is RoadDesign d)
            {
                if (!string.IsNullOrEmpty(d.WallCutKind) && System.Enum.TryParse(d.WallCutKind, out WallSection.Kind c)) cut = c;
                if (!string.IsNullOrEmpty(d.WallFillKind) && System.Enum.TryParse(d.WallFillKind, out WallSection.Kind f)) fill = f;
            }
        }
        catch { }   // 도로 기록을 못 읽으면 부지 설정 그대로
        return (cut, fill);
    }
}
