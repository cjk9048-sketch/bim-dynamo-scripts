using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★★[v110.4 · 계획_v110_4.md 1 · 2.A] 도곽(<see cref="SheetCommand.Build"/>)을 <b>누구 것으로</b> 그리나.
/// <para><c>null</c> = 부지 [종단도] — 지금 식 그대로(<see cref="ProfileCommand"/>의 static을 읽는다) ·
/// 값 = 진입도로 — 이 인자만 읽고 static은 안 읽는다(부지 옹벽 · 측점 · 계획고가 도로 뷰에 섞이지 않게 — 계획_v110_2b 1).</para></summary>
public sealed class SheetContext
{
    internal ObjectId AlignId;
    /// <summary>실측 지반(도로 «-바탕» — 면에서 딴 것) · 계획(도로 «-계획») · 그린 지반 꺾은선(도로 «-지반» — 밴드 종단1).</summary>
    internal ObjectId PidGround, PidPad, PidGroundLine;
    internal ObjectId ChainId, GroupId;
    internal double Scale;
    internal List<(ObjectId Id, double St, Point2d L, Point2d R)> SampleLines = new();
    internal List<StationMarks.Mark> Marks = new();
    /// <summary>도로는 옹벽 막대 · 수직부 두 값을 안 그린다(계획 6 — 계단 옹벽은 중심 종단과 안 만남) — 빈 목록.</summary>
    internal List<StationMarks.WallSpan> WallSpans = new();
    internal List<StationMarks.VertBar> VertBars = new();
    internal string RoadHandle = "";
    /// <summary>★[계획 검토 M1 · 시험 D] 부지 종단도가 있으면 도로는 부지 축척을 따랐다 — 도면 주석 축척(Cannoscale)을 다시 걸지 않는다(같은 값이라도 손대지 않음).</summary>
    internal bool KeepDrawingScale;
}

/// <summary>★★[v110.4 · 계획 2.A · 1] 부지 <see cref="ProfileCommand.BuildSampleLines"/> 등을 도로에 쓸 때의 대상 — <c>null</c> = 부지(지금 식).
/// <para>★[계획 검토 L4] 폭은 <see cref="Width"/>로 받는다 — 부지의 <c>XsecWidth.Resolve</c>는 횡단 폭 설정을 덮으므로 도로 길에서 안 부른다.</para></summary>
internal sealed class ProfileTarget
{
    /// <summary>부지 출처(정지 경계 · 번들 사면 · 옹벽면 · 터파기 · 벽 자리 정리) — 도로는 거짓(절성 경계 · 원지반 꺾임 · 수동 · 지운 자리만).</summary>
    internal bool SiteSources = true;
    /// <summary>ProfileCommand의 Last* static에 쓸까 — 도로는 거짓(횡단도 · 도곽 · 측점 찍기가 부지 값을 계속 읽게).</summary>
    internal bool WriteStatics = true;
    /// <summary>횡단용 숨긴 검토선 그룹을 만들까 — 도로는 거짓.</summary>
    internal bool XsecGroup = true;
    internal string GroupBase = SectionCommand.GroupBase;
    internal string ChainLayer = ProfileCommand.LayerChain;
    /// <summary>검토선 레이어(빈 값 = 그대로).</summary>
    internal string SampleLineLayer = "";
    internal (double L, double R) Width;
    internal string GroundName = SectionCommand.ProfGroundName;
    internal string GroundTempName = "";
    /// <summary>실측 종단을 «DH_지반실측(숨김)»으로 이름 바꾸고 숨길까 — 도로는 거짓(«-바탕»은 이름 그대로 · 뷰마다 재정의로 숨김 — 검토 M5).</summary>
    internal bool RenameRaw = true;
    // ── 나오는 값
    internal List<StationMarks.Mark> Marks = new();
    internal List<(ObjectId Id, double St, Point2d L, Point2d R)> Made = new();
    internal List<StationMarks.WallSpan> Spans = new();
    internal List<StationMarks.VertBar> Bars = new();
    internal ObjectId ChainId = ObjectId.Null;
}

/// <summary>★★[v111.5 · 계획_v111_4.md 3 · 검토 M2] [횡단도](<see cref="XsecViewCommand"/> Build)를 <b>누구 것으로</b> 그리나 — <c>null</c> = 부지(지금 식 · static 읽고 씀) ·
/// 값 = 진입도로(이 인자만 읽는다 — LastAt · 검토선 그룹 · 벽 자리 · 측점 · 측점 간격 · 같은 도면 · «마지막 DH선형» · 횡단 폭 · 옹벽 단면은 안 읽고 안 씀).
/// <para>만드는 쪽 = <see cref="RoadXsec.Build"/>.</para></summary>
internal sealed class XsecTarget
{
    internal ObjectId AlignId;
    internal string AlignName = "", RoadHandle = "";
    /// <summary>진입도로 종단도 검토선 그룹(«{노선}-검토»)의 측점 · 이름 꼬리 — 측점 차례.</summary>
    internal List<(double St, string Label)> Stations = new();
    /// <summary>원지반 · 정지면(부지 — 선만) · 진입도로 · 지층 · 지하수위.</summary>
    internal List<SectionCommand.SurfPick> Surfs = new();
    /// <summary>계획면(수량 · FGL) = FirstOf(진입도로, 도로 바탕면) — 바탕이 정지면_DH면 <see cref="BaseIsPad"/>.</summary>
    internal ObjectId RoadSurfId, BaseSurfId;
    internal bool BaseIsPad;
    /// <summary>★[v112.1] 가지 도로면 본 도로 면들(가까운 본 도로 먼저 — 계획면 FirstOf 차례 · 없으면 빈 목록).</summary>
    internal List<ObjectId> MainSurfIds = new();
    internal (double L, double R) Width;
    /// <summary>숨긴 횡단 검토선 그룹 이름 앞머리 «{노선}-횡단»(부지 IsXsecGroupName에 안 걸림).</summary>
    internal string GroupBase = "";
    internal string Title = "진입도로 횡단면도";
    /// <summary>도면설정 «진입도로 횡단도 축척»(0 = 자동 — 부지와 같은 PickScale).</summary>
    internal double Pin;
    /// <summary>지울 옛 횡단(이 도로 기록 — 그룹 핸들 · 뷰 핸들).</summary>
    internal string OldGroup = "";
    internal List<string> OldViews = new();
    internal bool OwnAlign = true;
    // ── 나오는 값
    internal ObjectId GroupId = ObjectId.Null;
    internal List<ObjectId> Views = new();
    internal double Scale, Anno;
    internal string Msg = "";
    internal int Tagged;
    /// <summary>복사본 밴드 스타일인데 라벨 스타일이 원본과 같은 객체(얕은 복사) — 글자 크기를 안 씀(부지 라벨이 바뀌지 않게).</summary>
    internal HashSet<ObjectId> SharedLabels = new();
    /// <summary>★[v111.5 · 설치 전 검토 L1] 진입도로 수량표 스타일 — 복사본을 못 만들어도 부지 «DH-수량표»를 덮지 않게 처음부터 진입도로 이름(최종 복사본이면 축척 짝 이름으로 바뀜).</summary>
    internal string TableStyle = "DH-수량표(진입도로)";
}

/// <summary>★★[v110.4 · 계획 2.A · 4 · 검토 M2] 진입도로 종단도가 그린 <b>평범한 도면 객체</b>(도곽 · 장식 · 세로선 · 검토선 글씨)의 표식 —
/// XData «DH_ROADSHEET» [1000 노선 핸들][1000 뷰 핸들][1000 종류]. 부지 지우기(레이어 · X 범위)는 표식 있는 것을 건너뛰고,
/// 도로 [종단도 다시]는 «이 노선 표식»만 지운다(<see cref="WallSectionDraw"/> 표식과 같은 꼴).</summary>
internal static class SheetTag
{
    internal const string App = "DH_ROADSHEET";
    /// <summary>★[v111.5 · 계획 3 · 검토 H4] 진입도로 횡단도 장식의 종류 칸 — 종단도 장식은 레이어 이름을 적는다(이것과 안 겹침).</summary>
    internal const string KindXsec = "XSEC";

    internal static void Put(Database db, Transaction tr, Entity e, string road, string view, string kind)
    {
        GradingBuilder.EnsureRegApp(db, tr, App);
        using var rb = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, App),
                                        new TypedValue((int)DxfCode.ExtendedDataAsciiString, road ?? ""),
                                        new TypedValue((int)DxfCode.ExtendedDataAsciiString, view ?? ""),
                                        new TypedValue((int)DxfCode.ExtendedDataAsciiString, kind ?? ""));
        e.XData = rb;
    }

    /// <summary>표식이 있으면 참 — 노선 · 뷰 · 종류를 돌려준다.</summary>
    internal static bool Of(Entity e, out string road, out string view, out string kind)
    {
        road = view = kind = "";
        try
        {
            using var rb = e.GetXDataForApplication(App);
            if (rb == null) return false;
            var a = rb.AsArray();
            if (a.Length > 1) road = a[1].Value as string ?? "";
            if (a.Length > 2) view = a[2].Value as string ?? "";
            if (a.Length > 3) kind = a[3].Value as string ?? "";
            return true;
        }
        catch { return false; }
    }

    /// <summary>진입도로 종단도 것인가(표식 있음) — 부지 지우기가 건너뛴다(<see cref="ProfileSheetRules.ShouldErase"/>).</summary>
    internal static bool IsRoad(Entity e) => Of(e, out _, out _, out _);

    /// <summary>이 노선 표식이 있는 모형 공간 객체를 지운다(모아 두고 밖에서 지움 · 잠긴 레이어여도).
    /// <para>★[v111.5 · 계획 3 · 검토 H4] 종류로 거른다 — <paramref name="xsec"/> 거짓 = 종단도 장식만([종단도 새로 그리기]) · 참 = 횡단도 장식(«XSEC»)만.</para></summary>
    internal static int EraseRoad(Database db, Transaction tr, string road, bool xsec = false)
    {
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        var kill = new List<ObjectId>();
        foreach (ObjectId id in ms)
        {
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is Entity e && Of(e, out string r, out _, out string k)
                    && ProfileSheetRules.ShouldErase(true, r, road) && string.Equals(k, KindXsec, StringComparison.Ordinal) == xsec) kill.Add(id);
            }
            catch { }
        }
        int n = 0;
        foreach (var id in kill) try { tr.GetObject(id, OpenMode.ForWrite, false, true).Erase(); n++; } catch { }
        return n;
    }

    /// <summary>★[계획 2.C-11] 진입도로 종단뷰인가 — 그 노선에 진입도로 설계(XRecord «DH_ROAD»)가 있으면 참(v110.0~110.3 단순 뷰 포함).</summary>
    internal static bool IsRoadView(Transaction tr, CivilDb.ProfileView pv)
    {
        try { return RoadDesign.Has(tr, pv.AlignmentId); } catch { return false; }
    }

    /// <summary>★[계획 검토 H1] 도로 도곽 사각형들 — 표식 있는 닫힌 폴리선(모형 도곽 레이어) · 부지 밴드 묶음 주인 판정이 쓴다.</summary>
    internal static List<ProfileSheetRules.Rect> RoadFrames(Database db, Transaction tr)
    {
        var list = new List<ProfileSheetRules.Rect>();
        try
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Polyline pl || !pl.Closed) continue;
                    if (!string.Equals(pl.Layer, SheetCommand.LayFrameModel, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!IsRoad(pl)) continue;
                    var ex = pl.GeometricExtents;
                    list.Add(new ProfileSheetRules.Rect(ex.MinPoint.X, ex.MinPoint.Y, ex.MaxPoint.X, ex.MaxPoint.Y));
                }
                catch { }
            }
        }
        catch { }
        return list;
    }
}
