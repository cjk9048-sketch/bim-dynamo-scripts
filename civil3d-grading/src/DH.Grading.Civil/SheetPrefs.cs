using Autodesk.AutoCAD.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★[v112.1 · JACK 1102 «도면설정 도면마다 저장»] 도면설정 중 세션 값뿐이던 것 — <b>도면에 저장</b>(도면 사전 XRecord «DH_SHEETPREFS»).
/// <para>측점 간격 · 횡단 폭(좌 · 우 · 자동) · 원지반 굴곡 · 종단뷰 · 검토선 · 횡단뷰 축척 · 배경지도 화질. 진입도로 축척(<see cref="RoadDrawingSettings"/>) · 횡단도 배치 · 등고선 간격(<see cref="ContourInterval"/>)은 이미 도면에 있음.</para>
/// <para>쓰는 때 = 도면설정 [저장] · 종단도 · 횡단도를 그릴 때(그 도면을 그린 값을 붙잡음). 읽는 때 = 도면이 바뀔 때(<see cref="GradingSettings.SyncToDocument"/>).
/// 기록이 없는 도면(새 도면 · 옛 도면)은 지금 세션 값 그대로 — 정지 값(<see cref="GradingBundle"/>)과 같은 법(새 도면은 직전 값으로 시작).</para></summary>
internal static class SheetPrefs
{
    const string Key = "DH_SHEETPREFS";
    /// <summary>★[v113.1] 판 2 — 끝에 부지 종단 V 축척 · 옛 규칙 표지 두 칸(판 1 기록 = V «H와 같게» + 옛 규칙).</summary>
    const int Ver = 2;

    /// <summary>값 한 벌(차례 = 기록 차례).</summary>
    internal readonly record struct Values(double XsecInterval, double XsecLeft, double XsecRight, bool XsecWidthAuto,
                                           double GroundBreakTolZ, double ProfileScale, double SectionLineScale, double XsecScale, double BasemapRes,
                                           double ProfileScaleV = DH.Grading.Core.ProfileScaleFit.SameAsH, bool ProfileLegacy = true);

    internal static Values Now() => new(GradingSettings.XsecInterval, GradingSettings.XsecLeft, GradingSettings.XsecRight, GradingSettings.XsecWidthAuto,
                                        GradingSettings.GroundBreakTolZ, GradingSettings.ProfileScale, GradingSettings.SectionLineScale, GradingSettings.XsecScale, GradingSettings.BasemapRes,
                                        GradingSettings.ProfileScaleV, GradingSettings.ProfileLegacy);

    static void Set(Values v)
    {
        GradingSettings.XsecInterval = v.XsecInterval;
        GradingSettings.XsecLeft = v.XsecLeft;
        GradingSettings.XsecRight = v.XsecRight;
        GradingSettings.XsecWidthAuto = v.XsecWidthAuto;
        GradingSettings.GroundBreakTolZ = v.GroundBreakTolZ;
        GradingSettings.ProfileScale = v.ProfileScale;
        GradingSettings.SectionLineScale = v.SectionLineScale;
        GradingSettings.XsecScale = v.XsecScale;
        GradingSettings.BasemapRes = v.BasemapRes;
        GradingSettings.ProfileScaleV = v.ProfileScaleV;
        GradingSettings.ProfileLegacy = v.ProfileLegacy;
    }

    /// <summary>값 확인 — 이상한 값(손댄 도면 · 다른 판)은 그 칸만 지금 값으로.</summary>
    static Values Clean(Values v, Values now, out int bad)
    {
        int b = 0;
        double Pos(double x, double keep) { if (double.IsFinite(x) && x > 0 && x < 1e6) return x; b++; return keep; }
        double Scale(double x, double keep) { if (double.IsFinite(x) && x >= 0 && x < 1e6) return x; b++; return keep; }
        double In(double x, double[] list, double keep) { foreach (var y in list) if (System.Math.Abs(x - y) < 1e-9) return y; b++; return keep; }
        var o = new Values(Pos(v.XsecInterval, now.XsecInterval), Pos(v.XsecLeft, now.XsecLeft), Pos(v.XsecRight, now.XsecRight), v.XsecWidthAuto,
                           Pos(v.GroundBreakTolZ, now.GroundBreakTolZ), Scale(v.ProfileScale, now.ProfileScale), Scale(v.SectionLineScale, now.SectionLineScale),
                           Scale(v.XsecScale, now.XsecScale), In(v.BasemapRes, GradingSettings.BasemapResValues, now.BasemapRes),
                           DH.Grading.Core.ProfileScaleFit.CleanV(v.ProfileScaleV, Commands.SheetCommand.Scales, double.NaN) is double cv && double.IsFinite(cv) ? cv : Bad(now.ProfileScaleV),
                           v.ProfileLegacy);
        double Bad(double keep) { b++; return keep; }
        bad = b;
        return o;
    }

    /// <summary>도면 기록 읽기 — 없으면 null.</summary>
    internal static Values? Read(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        Values? o = null;
        if (nod.Contains(Key) && tr.GetObject(nod.GetAt(Key), OpenMode.ForRead) is Xrecord xr && xr.Data != null)
        {
            var a = xr.Data.AsArray();
            if (a.Length >= 10 && a[0].Value is int ver && ver >= 1)
            {
                double D(int i) => a[i].Value is double d ? d : double.NaN;
                // ★[v113.1] 판 2 = 끝에 V · 옛 규칙 표지 · 판 1(옛 도면) = V «H와 같게» + 옛 규칙(지금 그림 그대로)
                bool v2 = ver >= 2 && a.Length >= 12;
                o = new Values(D(1), D(2), D(3), a[4].Value is int w && w != 0, D(5), D(6), D(7), D(8), D(9),
                               v2 ? D(10) : DH.Grading.Core.ProfileScaleFit.SameAsH, !v2 || (a[11].Value is int lg && lg != 0));
            }
        }
        tr.Commit();
        return o;
    }

    /// <summary>도면이 바뀔 때 — 기록이 있으면 세션 값으로. 돌려줌 = 진단 한 줄(기록 없으면 빈 글).</summary>
    internal static string Apply(Database db)
    {
        var r = Read(db);
        if (r == null)
        {
            // ★[v113.1] 기록 없는 옛 도면인데 부지 종단도가 이미 있으면 그 그림 그대로(V = H와 같게 · 옛 규칙) — 새 도면은 지금 값
            if (HasSiteProfile(db)) { GradingSettings.ProfileScaleV = DH.Grading.Core.ProfileScaleFit.SameAsH; GradingSettings.ProfileLegacy = true; return "도면설정 기록 없음 · 부지 종단도가 있어 종단 V = H와 같게(옛 그림 그대로)"; }
            // 옛 규칙은 옛 도면의 성질 — 앞 도면에서 물려받지 않음(새 도면 = 새 규칙 · 자동)
            if (GradingSettings.ProfileLegacy) { GradingSettings.ProfileLegacy = false; GradingSettings.ProfileScaleV = 0; }
            return "";
        }
        var now = Now();
        var v = Clean(r.Value, now, out int bad);
        Set(v);
        return $"도면설정 = 도면 값(측점 간격 {v.XsecInterval:0.#} m · 종단뷰 축척 {(v.ProfileScale > 0 ? $"1:{v.ProfileScale:0}" : "자동")}"
             + $" · V {(v.ProfileScaleV > 0 ? $"1:{v.ProfileScaleV:0}" : v.ProfileScaleV < 0 ? "H와 같게" : "자동")}{(v.ProfileLegacy ? " · 옛 규칙" : "")})"
             + (bad > 0 ? $" · ⚠이상한 값 {bad}칸은 지금 값으로" : "");
    }

    /// <summary>★[v113.1] 부지 종단뷰(진입도로가 아닌 «DH선형» 노선의 종단뷰)가 있나.</summary>
    static bool HasSiteProfile(Database db)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            bool site = false;
            foreach (ObjectId aid in cdoc.GetAlignmentIds())
            {
                try
                {
                    if (tr.GetObject(aid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Alignment al) continue;
                    if (!al.Name.StartsWith(Commands.SectionCommand.AlignBase, System.StringComparison.Ordinal) || RoadDesign.Has(tr, aid)) continue;
                    if (al.GetProfileViewIds().Count > 0) { site = true; break; }
                }
                catch { }
            }
            tr.Commit();
            return site;
        }
        catch { return false; }
    }

    /// <summary>지금 세션 값을 도면에 — 같으면 안 씀(실행 취소 줄 · DBMOD 안 늘림). 명령 안(문서 잠김)에서 부른다.</summary>
    internal static void Pin(Database db)
    {
        var v = Now();
        if (Read(db) is Values old && old == v) return;
        using var tr = db.TransactionManager.StartTransaction();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        Xrecord xr;
        if (nod.Contains(Key)) xr = (Xrecord)tr.GetObject(nod.GetAt(Key), OpenMode.ForWrite);
        else
        {
            nod.UpgradeOpen();
            xr = new Xrecord();
            nod.SetAt(Key, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }
        xr.Data = new ResultBuffer(
            new TypedValue((int)DxfCode.Int32, Ver),
            new TypedValue((int)DxfCode.Real, v.XsecInterval), new TypedValue((int)DxfCode.Real, v.XsecLeft), new TypedValue((int)DxfCode.Real, v.XsecRight),
            new TypedValue((int)DxfCode.Int32, v.XsecWidthAuto ? 1 : 0),
            new TypedValue((int)DxfCode.Real, v.GroundBreakTolZ), new TypedValue((int)DxfCode.Real, v.ProfileScale),
            new TypedValue((int)DxfCode.Real, v.SectionLineScale), new TypedValue((int)DxfCode.Real, v.XsecScale), new TypedValue((int)DxfCode.Real, v.BasemapRes),
            new TypedValue((int)DxfCode.Real, v.ProfileScaleV), new TypedValue((int)DxfCode.Int32, v.ProfileLegacy ? 1 : 0));
        tr.Commit();
    }

    /// <summary>실패해도 그리기는 계속 — 까닭만 로그.</summary>
    internal static void TryPin(Database db, string where)
    {
        try
        {
            // ★[v112.1 검토 H3] 세션 값이 이 도면 것일 때만 — 도면을 바꾼 뒤 맞추지 않고 들어온 길이면 앞 도면 값으로 이 도면 기록을 덮지 않음
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.GetDocument(db);
            if (!GradingSettings.IsOwnedBy(doc)) { DiagLog.Append($"\n[도면설정] {where} — 세션 값이 이 도면 것이 아니라 도면에 안 적음"); return; }
            Pin(db);
        }
        catch (System.Exception ex) { try { DiagLog.Append($"\n[도면설정] {where} — 도면에 못 적음: {ex.Message}"); } catch { } }
    }
}
