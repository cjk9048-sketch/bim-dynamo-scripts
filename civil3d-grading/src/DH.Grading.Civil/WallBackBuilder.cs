using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Surface = Autodesk.Civil.DatabaseServices.Surface;

namespace DH.Grading.Civil;

/// <summary>★★★[v104 B · 계획_v104_B.md 2판 · JACK 1021 «터파기 끝에서 지정 경사로 내려오다 옹벽 꼭대기 높이에서 평탄 · 절토 옹벽만 · 따로 숨긴 면 + 종단 · 횡단 선»]
/// <b>옹벽 뒷채움 배면사면</b> — 옹벽 터파기면을 지은(되쓴) 바로 뒤 같은 트랜잭션에서.
/// <para>대상 = 터파기면 단면 기록(<c>DH_WALLSEC</c>)의 절토 줄(패널식 제외 · 모든 단) · 데이라잇 = 터파기면 테두리 중 땅에 닿은 변(흙 쪽).</para>
/// <para>수량용 기록(<c>DH_WALLBACK</c>)은 <b>터파기면에</b> 적는다 — 그림 면(<c>옹벽배면_DH</c> · 0.5 m 격자)이 Civil 관문에서 떨어져도 수량은 배면을 따른다(검토 높음 4).</para></summary>
internal static class WallBackBuilder
{
    internal const string Name = "옹벽배면_DH";
    internal const string TempName = "옹벽배면_짓는중_DH";
    internal const string Label = "옹벽배면";
    internal const string ProfName = "DH_옹벽배면";
    const string StampKey = "DH_WALLBACKSTAMP";
    const string RecKey = "DH_WALLBACK";
    const double Grid = 0.5;   // 그림용(검토 중간 9 — 0.25 m면 벽 1 km × 15 m에 24만 점)

    static bool IsOurs(string nm) => nm == Name || (nm.StartsWith(Name + "_") && int.TryParse(nm.Substring(Name.Length + 1), out _));
    internal static bool IsAnyOurs(string nm) => IsOurs(nm) || nm == TempName;

    static string ReadStamp(Transaction tr, DBObject o)
    {
        try
        {
            if (o.ExtensionDictionary.IsNull) return "";
            if (tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary d || !d.Contains(StampKey)) return "";
            if (tr.GetObject(d.GetAt(StampKey), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return "";
            var a = xr.Data.AsArray();
            return a.Length > 0 && a[0].Value is string s ? s : "";
        }
        catch { return ""; }
    }

    static void WriteXr(Transaction tr, DBObject o, string key, ResultBuffer rb)
    {
        if (!o.IsWriteEnabled) o.UpgradeOpen();
        if (o.ExtensionDictionary.IsNull) o.CreateExtensionDictionary();
        var d = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForWrite);
        var xr = new Xrecord { Data = rb };
        if (d.Contains(key)) { var old = tr.GetObject(d.GetAt(key), OpenMode.ForWrite); old.Erase(); }
        d.SetAt(key, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }

    /// <summary>★[v104 설치 전 검토 낮음 5] 기록이 이미 같은 값이면 안 쓴다 — Civil은 쓰기로 연 것만으로도 면을 «수정»으로 친다(되쓰는 [종단도]마다 터파기면이 바뀐 것처럼).</summary>
    static void WriteRecIfChanged(Transaction tr, DBObject o, System.Collections.Generic.List<(char K, object V)> enc)
    {
        try
        {
            if (!o.ExtensionDictionary.IsNull && tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead) is DBDictionary d && d.Contains(RecKey)
                && tr.GetObject(d.GetAt(RecKey), OpenMode.ForRead) is Xrecord xr && WallListStore.ValsOf(xr.Data, out _) is { } old
                && old.Count == enc.Count && old.Zip(enc).All(t => t.First.K == t.Second.K && t.First.V.Equals(t.Second.V)))
                return;
        }
        catch { }
        using var rb = WallListStore.BufferOf(enc);
        WriteXr(tr, o, RecKey, rb);
    }

    static void DropRecord(Transaction tr, DBObject o)
    {
        try
        {
            if (o.ExtensionDictionary.IsNull) return;
            var d = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead);
            if (!d.Contains(RecKey)) return;
            d.UpgradeOpen();
            var id = d.GetAt(RecKey); d.Remove(RecKey);
            try { tr.GetObject(id, OpenMode.ForWrite).Erase(); } catch { }
        }
        catch { }
    }

    static System.Collections.Generic.List<(ObjectId Id, string Name, string Stamp)> Existing(Transaction tr)
    {
        var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var res = new System.Collections.Generic.List<(ObjectId, string, string)>();
        foreach (ObjectId sid in civ.GetSurfaceIds())
            try { if (tr.GetObject(sid, OpenMode.ForRead) is Surface s && IsOurs(s.Name ?? "")) res.Add((sid, s.Name ?? "", ReadStamp(tr, s))); } catch { }
        return res;
    }

    /// <summary>배면 기록(터파기면에 붙어 있다) — 없거나 못 읽으면 null + 까닭.</summary>
    internal static WallExcav.BackRecord? ReadRecord(Transaction tr, DBObject o, out string why)
    {
        why = "";
        try
        {
            if (o.ExtensionDictionary.IsNull || tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary d || !d.Contains(RecKey)) { why = "배면 기록 없음"; return null; }
            if (tr.GetObject(d.GetAt(RecKey), OpenMode.ForRead) is not Xrecord xr) { why = "배면 기록이 XRecord가 아니다"; return null; }
            var vals = WallListStore.ValsOf(xr.Data, out why);
            if (vals == null) return null;
            return WallExcav.TryDecodeBack(vals, out var rec, out why) ? rec : null;
        }
        catch (System.Exception ex) { why = $"배면 기록을 못 읽었다 {ex.GetType().Name}: {ex.Message}"; return null; }
    }

    /// <summary>어느 선형도 안 쓰는 옛 그림 면을 지운다(터파기면 <c>Tidy</c>와 같은 법).</summary>
    internal static string Tidy(Transaction tr, ObjectId keep, System.Collections.Generic.HashSet<string> refd)
    {
        int erased = 0;
        foreach (var e in Existing(tr))
        {
            if (e.Id == keep || e.Id.IsErased || refd.Contains(e.Id.Handle.ToString())) continue;
            try { tr.GetObject(e.Id, OpenMode.ForWrite).Erase(); erased++; } catch { }
        }
        return erased == 0 ? "" : $"옹벽 배면 면 정리 — 아무 선형도 안 쓰는 옛 면 {erased}개 지움";
    }

    /// <summary>★계획 3 — 배면 기록(터파기면에) · 그림 면을 짓거나 되쓴다. 터파기면이 지어진(되쓴) 뒤에만 · 예외를 안 던진다(못 지으면 R.Back* 에 까닭).</summary>
    internal static void Ensure(Database db, Transaction tr, ObjectId groundId, WallExcavBuilder.Result R, WallExcav.Settings st,
                                CachedGroundSurface sC, CachedGroundSurface gC, StringBuilder log)
    {
        R.BackHead = st.BackHead;
        if (R.State != WallExcavBuilder.State.Built || R.SurfaceId.IsNull) { R.BackState = WallExcavBuilder.State.Off; return; }
        ObjectId made = ObjectId.Null;
        TinSurface? exTin = null;
        try
        {
            exTin = (TinSurface)tr.GetObject(R.SurfaceId, OpenMode.ForRead);
            // ★[v104 설치 전 검토 높음 1] 끔이면 기록을 <b>지우지 않는다</b> — 터파기면은 도장이 같으면 여러 선형이 함께 쓴다. 한 선형을 «배면 끔»으로 그려 기록을 지우면
            //   켬으로 그린 다른 선형의 수량이 말없이 배면 없이 나왔다. 수량은 «지금 설정이 켬 · 같은 경사»일 때만 이 기록을 쓴다(CollectQty · ForAlignment).
            //   켬일 때는 늘 이 면 · 이 경사의 답(기록 · 대상 없음 · 데이라잇 없음 · 못 지음 = 기록 없음)으로 맞춘다 — 경사와 무관한 판정이라 같은 면을 쓰는 선형끼리 어긋나지 않는다.
            if (!st.On || !st.BackOn) { R.BackState = WallExcavBuilder.State.Off; return; }
            // ★[v104.1 설치 전 검토 높음 1] 기록 판(평탄 높이 식)을 도장에 — 없으면 v104.0.0 그림 면(끊긴 T)이 도장이 같아 그대로 되쓰여 그림과 수량이 어긋났다
            string stamp = R.Stamp + "|B" + st.BackHead + "|T" + WallExcav.BackRecordVersion;
            R.BackStamp = stamp;
            var rec = WallExcavBuilder.ReadSecRecord(tr, exTin, out string rw);
            if (rec == null) { DropRecord(tr, exTin); R.BackState = WallExcavBuilder.State.Failed; R.BackWhy = "터파기면 단면 기록을 못 읽었다 — " + rw; log.AppendLine("  옹벽 배면 — ⚠" + R.BackWhy); return; }
            var walls = WallExcav.BackWallsOf(rec);
            if (walls.Count == 0) { DropRecord(tr, exTin); R.BackState = WallExcavBuilder.State.Off; log.AppendLine("  옹벽 배면 — 대상 없음(절토 옹벽 · 패널식 제외)"); return; }
            var exTris = PureWithWallsBuilder.ReadTris(exTin, out int lost);
            if (lost > 0) { DropRecord(tr, exTin); R.BackState = WallExcavBuilder.State.Failed; R.BackWhy = $"터파기면 삼각형을 읽다 {lost}개 버림"; log.AppendLine("  옹벽 배면 — ⚠" + R.BackWhy); return; }
            double? P(double x, double y) => sC.TryGetElevation(x, y, out double z) ? z : (double?)null;
            double? G(double x, double y) => gC.TryGetElevation(x, y, out double z) ? z : (double?)null;
            double? L(double x, double y) { var p = P(x, y); var g = G(x, y); return p != null && g != null ? System.Math.Min(p.Value, g.Value) : p ?? g; }
            var eC = new CachedGroundSurface(exTin);
            double? E(double x, double y) => eC.TryGetElevation(x, y, out double z) ? z : (double?)null;
            var day = WallExcav.DaylightOf(exTris, L, walls);
            if (day.Count == 0) { DropRecord(tr, exTin); R.BackState = WallExcavBuilder.State.Off; log.AppendLine($"  옹벽 배면 — 데이라잇 없음(절토 줄 {walls.Count} · 뒤 터파기가 땅에 안 닿음) — 배면 = 땅"); return; }
            var br = new WallExcav.BackRecord { N = st.BackSlope, Walls = walls, Daylight = day };
            // ★[v105.1 · 계획_v105_1.md 2 · 8] 후보 가면 C — 여기서 한 번(정지면_DH · 원지반 · 터파기면 세모) → 기록에 적어 수량도 같은 가면 · 같은 평탄 격자
            var swM = System.Diagnostics.Stopwatch.StartNew();
            if (!WallExcav.BuildBackMask(br, P, G, exTris, WallExcav.MaskStep, out string maskNote))
            {
                DropRecord(tr, exTin); R.BackState = WallExcavBuilder.State.Failed; R.BackWhy = "배면 후보 가면을 못 지었다 — " + maskNote;
                log.AppendLine("  옹벽 배면 — ⚠" + R.BackWhy);
                return;
            }
            if (br.Mask == null || br.Mask.Runs.Count == 0) { DropRecord(tr, exTin); R.BackState = WallExcavBuilder.State.Off; log.AppendLine($"  옹벽 배면 — 대상 없음(후보 구역 빔 · {maskNote})"); return; }
            maskNote += $" · {swM.ElapsedMilliseconds}ms";
            // ★[검토 높음 4] 수량용 기록은 터파기면에 — 그림 면이 못 지어져도 수량은 배면을 따른다
            WriteRecIfChanged(tr, exTin, WallExcav.EncodeBack(br));
            R.BackRec = true;
            R.BackWhy = "";
            if (st.BackSlope < st.Slope - 1e-9) R.BackWhy = $"배면 경사 1:{st.BackSlope}가 터파기 경사 1:{st.Slope}보다 가파르다 — 그 자리 배면은 터파기선(되메우기 0)";
            // 그림 면 — 같은 도장이면 되쓴다
            var ex = Existing(tr);
            var same = ex.FirstOrDefault(e => e.Stamp == stamp && !e.Id.IsErased);
            if (!same.Id.IsNull)
            {
                R.BackState = WallExcavBuilder.State.Built; R.BackId = same.Id; R.BackName = same.Name; R.BackReused = true;
                log.AppendLine($"  옹벽 배면 — 기록 다시 씀({maskNote}) · 그림 면 되씀 {same.Name}");
                return;
            }
            var swS = System.Diagnostics.Stopwatch.StartNew();
            var tris = WallExcav.BackSurface(br, P, G, E, Grid, out string note, out int flatHits);
            note = $"{maskNote} · {note} · {swS.ElapsedMilliseconds}ms";
            if (flatHits > 0) R.BackWhy = (R.BackWhy.Length > 0 ? R.BackWhy + " · " : "") + $"평탄 없이 경사로 벽에 닿는 자리(격자 {flatHits}점) — 벽 바로 뒤 땅이 꼭대기보다 높다";
            if (tris.Count == 0 && note.StartsWith(WallExcav.BackTooBig))
            {
                // ★[v104 설치 전 검토 낮음 4] 너무 큰 상자는 «땅과 같다»가 아니다 — 수량은 배면 반영(기록은 이미 적음) · 그림만 없음을 알린다
                R.BackState = WallExcavBuilder.State.Failed; R.BackWhy = $"그림 면을 안 지었다(수량은 배면 반영) — {note}";
                log.AppendLine("  옹벽 배면 — ⚠" + R.BackWhy);
                return;
            }
            if (tris.Count == 0) { R.BackState = WallExcavBuilder.State.Off; log.AppendLine($"  옹벽 배면 — 땅과 같다(바뀜 없음 · {note})"); return; }
            string xml = GradingBuilder.NewXmlPath(db, "옹벽배면");
            WallDaylight.WriteLandXmlTin(xml, "WALLBACK", tris, 1e-7, out int np, out int nf, out int nd);
            ObjectId cid = TinSurface.CreateFromLandXML(db, TempName, xml, "WALLBACK");
            if (cid.IsNull) throw new System.InvalidOperationException("CreateFromLandXML이 빈 ObjectId를 돌려줬다");
            made = cid;
            var cTin = (TinSurface)tr.GetObject(cid, OpenMode.ForWrite);
            int civPts = -1; try { civPts = cTin.GetGeneralProperties().NumberOfPoints; } catch { }
            var theirs = PureWithWallsBuilder.ReadTris(cTin, out int l4);
            var cmp = CivilSafeMesh.Compare(tris, theirs);
            bool gate = l4 == 0 && nd == 0 && theirs.Count == nf && civPts == np && cmp.Exceptions == 0 && cmp.SymArea <= 1e-8 && cmp.Hausdorff <= 1e-6 && cmp.MaxDz <= 1e-3;
            string back = $"Civil 점 {civPts}/{np} · 삼각형 {theirs.Count}/{nf} · {cmp.Summary}";
            if (!gate)
            {
                try { cTin.Erase(); } catch { }
                made = ObjectId.Null;
                R.BackState = WallExcavBuilder.State.Failed; R.BackWhy = "그림 면을 Civil이 그대로 안 받았다(수량은 배면 반영) — " + back;
                log.AppendLine("  옹벽 배면 — ⚠" + R.BackWhy);
                return;
            }
            string frz = GradingBuilder.FreezeXmlBacked(cTin);
            string nm = GradingBuilder.UniqueName(db, tr, Name);
            cTin.Name = nm;
            try { cTin.Description = $"DH 옹벽 뒷채움 배면사면(종단 · 횡단 그림용 · 수량은 터파기면 기록) — {System.DateTime.Now:yyyy-MM-dd HH:mm} · {GradingSettings.Version} · 1:{st.BackSlope}"; } catch { }
            WriteXr(tr, cTin, StampKey, new ResultBuffer(new TypedValue((int)DxfCode.Text, stamp)));
            GradingBuilder.SetSurfaceVisible(tr, Name, false);
            made = ObjectId.Null;
            R.BackState = WallExcavBuilder.State.Built; R.BackId = cid; R.BackName = nm;
            if (frz.Length > 0) R.BackWhy = (R.BackWhy.Length > 0 ? R.BackWhy + " · " : "") + $"배면 면을 굳히지 못했다({frz})";
            log.AppendLine($"  옹벽 배면 — 지었다 {nm} · 1:{st.BackSlope} · 절토 줄 {walls.Count} · 데이라잇 {day.Count}줄 {day.Sum(d => d.Count)}점 · {note} · 되읽기 {back}"
                         + $" · {(frz.Length == 0 ? "굳힘" : "⚠굳힘 실패 " + frz)}" + (R.BackWhy.Length > 0 ? " · ⚠" + R.BackWhy : ""));
        }
        catch (System.Exception exB)
        {
            try { if (!made.IsNull && !made.IsErased) tr.GetObject(made, OpenMode.ForWrite).Erase(); } catch { }
            if (!R.BackRec && exTin != null) DropRecord(tr, exTin);   // ★[검토 높음 1] 기록을 쓰기 전에 빠졌으면 옛 경사의 기록이 남지 않게
            R.BackState = WallExcavBuilder.State.Failed; R.BackWhy = $"배면을 못 지었다({exB.GetType().Name}: {exB.Message})";
            log.AppendLine("  옹벽 배면 — ⚠" + R.BackWhy);
        }
    }
}
