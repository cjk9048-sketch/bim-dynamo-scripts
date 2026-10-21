using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using Tri = DH.Grading.Core.WallDaylight.Tri;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Surface = Autodesk.Civil.DatabaseServices.Surface;

namespace DH.Grading.Civil;

/// <summary>★★★[v103.3 · JACK 1021 «경계를 기준으로 뒷채움거리와 사면경사로 터파기 지표면을(숨김상태로) 별도로 생성해서 종단과 횡단에서 보여주고 토공량에 반영»]
/// <b>옹벽 뒷채움 터파기면(<c>옹벽터파기_DH</c>)을 짓는다 — 종단을 그리는 명령이 부른다</b>(계획_v103_3.md 2판).
/// <para>①종단이 쓰는 정지면에 보이는 옹벽 앞면 아랫선을 모아(구간 옹벽 · 굳은 옹벽 · 번들 옹벽) ②그 면에서 벽 밑 높이 · 흙 쪽을 재고 ③바닥 띠(앞 · 뒤 · 기초 밑)마다
/// 경사 법면 조각을 Civil로 짓고 <b>삼각형만 읽어 곧바로 지운다</b> ④Core <see cref="WallDaylight.LowerEnvelope"/>가 조각들의 아래 껍질 중 기준면 Lower(정지면_DH, 원지반) 아래인 것만 남긴다
/// ⑤LandXML로 굳힌다(옹벽면과 같은 길 — 새 이름의 파일 · 되읽기 관문 · 굳힘). 붙여넣기 · 경계 · 스냅샷을 안 쓴다(메모리 1013의 죽음 길을 안 탄다).</para>
/// <para>면 도장(설정 · 정지면_DH 핸들 · 합성 번호 · 원지반 핸들 # 앞면 지문)이 같으면 되쓴다.</para></summary>
internal static class WallExcavBuilder
{
    internal const string Name = "옹벽터파기_DH";
    internal const string TempName = "옹벽터파기_짓는중_DH";
    internal const string PieceName = "옹벽터파기조각_DH";
    internal const string Label = "옹벽터파기";
    internal const string ProfName = "DH_옹벽터파기";
    const string StampKey = "DH_WALLEXCAV";
    /// <summary>선형 확장사전 — 종단이 쓴 터파기면 핸들 · 도장 · 상태 · 까닭(계획 2-4). <c>DH_PROFSURF</c>와 따로.</summary>
    internal const string AlignKey = "DH_PROFEXCAV";

    internal enum State { Off = 0, Built = 1, Failed = 2 }

    internal sealed class Result
    {
        public State State;
        public ObjectId SurfaceId = ObjectId.Null;
        public string SurfaceName = "";
        public string Stamp = "";
        public string Why = "";
        public bool Reused;
        public int Faces, Pieces;
        public WallExcav.Settings Settings = WallExcav.Settings.Default;
        public bool HasWarn => State == State.Failed || Why.Length > 0;
        public string StatusText => State switch
        {
            State.Off => "꺼짐",
            State.Built => $"지음({SurfaceName}{(Reused ? " · 되씀" : "")} · {Settings.Text})" + (Why.Length > 0 ? " · ⚠" + Why : ""),
            _ => "⚠못 지음 — " + Why,
        };
    }

    /// <summary>도장 머리 — 켬/끔 · 뒤 · 앞 · 기초 · 경사 · 정지면_DH 핸들 · 합성 번호 · 원지반 핸들(재검토 중간 5 · 6: InvariantCulture "R").</summary>
    static string HeadOf(WallExcav.Settings s, string planHandle, string nonce, string groundHandle) => $"{s.Head}|{planHandle}|{nonce}|{groundHandle}";
    internal static string StampHead(string stamp) => string.IsNullOrEmpty(stamp) ? "" : stamp.Split('#')[0];

    /// <summary>지금 도장 머리(종단을 다시 그리지 않고 견줄 때 — 횡단 · 검토선). 정지면_DH가 하나가 아니면 그 칸은 빈 값.</summary>
    internal static string CurrentHead(Database db, ObjectId groundId, out WallExcav.Settings s)
    {
        s = WallListStore.ExcavOf(db, out _);
        string ph = "", nonce = "";
        try
        {
            var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            using var tr = db.TransactionManager.StartTransaction();
            var (ids, _) = Commands.WallCompositeCommand.Candidates(civ, tr, Commands.WallCompositeCommand.PlanName, groundId);
            if (ids.Count == 1)
            {
                ph = ids[0].Handle.ToString();
                nonce = Commands.WallCompositeCommand.CompositeNonce(tr, tr.GetObject(ids[0], OpenMode.ForRead));
            }
            tr.Commit();
        }
        catch { }
        return HeadOf(s, ph, nonce, groundId.IsNull ? "" : groundId.Handle.ToString());
    }

    static string ReadStamp(Transaction tr, DBObject o, int idx)
    {
        try
        {
            if (o.ExtensionDictionary.IsNull) return "";
            if (tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary d || !d.Contains(StampKey)) return "";
            if (tr.GetObject(d.GetAt(StampKey), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return "";
            var a = xr.Data.AsArray();
            return a.Length > idx && a[idx].Value is string s ? s : "";
        }
        catch { return ""; }
    }
    static void WriteStamp(Transaction tr, DBObject o, string stamp, string info, string warn)
    {
        if (!o.IsWriteEnabled) o.UpgradeOpen();
        if (o.ExtensionDictionary.IsNull) o.CreateExtensionDictionary();
        var d = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForWrite);
        var xr = new Xrecord { Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, stamp), new TypedValue((int)DxfCode.Text, info), new TypedValue((int)DxfCode.Text, warn ?? "")) };
        if (d.Contains(StampKey)) { var old = tr.GetObject(d.GetAt(StampKey), OpenMode.ForWrite); old.Erase(); }
        d.SetAt(StampKey, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }

    static bool IsOurs(string nm) => nm == Name || (nm.StartsWith(Name + "_") && int.TryParse(nm.Substring(Name.Length + 1), out _));
    /// <summary>이름이 우리 터파기면 · 임시 · 조각인가 — <c>FindSurfaces</c>가 이름으로 줍지 않게 명시해 건너뛴다(재검토 중간 6).</summary>
    internal static bool IsAnyOurs(string nm) => IsOurs(nm) || nm == TempName || nm == PieceName || (nm.StartsWith(PieceName + "_") && int.TryParse(nm.Substring(PieceName.Length + 1), out _));

    static System.Collections.Generic.List<(ObjectId Id, string Name, string Stamp)> Existing(Transaction tr)
    {
        var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var res = new System.Collections.Generic.List<(ObjectId, string, string)>();
        foreach (ObjectId sid in civ.GetSurfaceIds())
        {
            try
            {
                if (tr.GetObject(sid, OpenMode.ForRead) is not Surface s) continue;
                string nm = s.Name ?? "";
                if (IsOurs(nm)) res.Add((sid, nm, ReadStamp(tr, s, 0)));
            }
            catch { }
        }
        return res;
    }

    // ══ 선형 기록 ══════════════════════════════════════════════════════════

    internal static void RecordOnAlignment(Transaction tr, ObjectId alignId, Result r)
    {
        var al = tr.GetObject(alignId, OpenMode.ForWrite);
        if (al.ExtensionDictionary.IsNull) al.CreateExtensionDictionary();
        var d = (DBDictionary)tr.GetObject(al.ExtensionDictionary, OpenMode.ForWrite);
        var xr = new Xrecord
        {
            Data = new ResultBuffer(
                new TypedValue((int)DxfCode.Text, r.State == State.Built && !r.SurfaceId.IsNull ? r.SurfaceId.Handle.ToString() : ""),
                new TypedValue((int)DxfCode.Text, r.Stamp ?? ""),
                new TypedValue((int)DxfCode.Int32, (int)r.State),
                new TypedValue((int)DxfCode.Text, r.Why ?? ""))
        };
        if (d.Contains(AlignKey)) { var old = tr.GetObject(d.GetAt(AlignKey), OpenMode.ForWrite); old.Erase(); }
        d.SetAt(AlignKey, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }

    internal static (string Handle, string Stamp, State State, string Why)? ReadAlign(Transaction tr, DBObject al)
    {
        try
        {
            if (al.ExtensionDictionary.IsNull) return null;
            if (tr.GetObject(al.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary d || !d.Contains(AlignKey)) return null;
            if (tr.GetObject(d.GetAt(AlignKey), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return null;
            var a = xr.Data.AsArray();
            if (a.Length < 4) return null;
            return (a[0].Value as string ?? "", a[1].Value as string ?? "", a[2].Value is int k ? (State)k : State.Failed, a[3].Value as string ?? "");
        }
        catch { return null; }
    }

    /// <summary>어느 선형도 안 쓰는 옛 터파기면을 지우고, 본 이름이 비면 지금 면에 돌려준다(옹벽면 <c>Tidy</c>와 같은 법).</summary>
    internal static string Tidy(Transaction tr, ObjectId keep)
    {
        var refd = new System.Collections.Generic.HashSet<string>();
        try
        {
            var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            foreach (ObjectId aid in civ.GetAlignmentIds())
                try { var r = ReadAlign(tr, tr.GetObject(aid, OpenMode.ForRead)); if (r != null && r.Value.Handle.Length > 0) refd.Add(r.Value.Handle); } catch { }
        }
        catch { }
        int erased = 0; var names = new System.Collections.Generic.List<string>();
        foreach (var e in Existing(tr))
        {
            if (e.Id == keep || e.Id.IsErased || refd.Contains(e.Id.Handle.ToString())) continue;
            try { tr.GetObject(e.Id, OpenMode.ForWrite).Erase(); erased++; names.Add(e.Name); } catch { }
        }
        string renamed = "";
        try
        {
            if (!keep.IsNull && !keep.IsErased && tr.GetObject(keep, OpenMode.ForRead) is Surface s && (s.Name ?? "").StartsWith(Name + "_", System.StringComparison.Ordinal)
                && !Existing(tr).Any(e => e.Id != keep && !e.Id.IsErased && e.Name == Name))
            { s.UpgradeOpen(); renamed = s.Name + " → " + Name; s.Name = Name; }
        }
        catch { renamed = ""; }
        return erased == 0 && renamed.Length == 0 ? "" : $"옹벽 터파기면 정리 — 아무 선형도 안 쓰는 옛 면 {erased}개 지움{(names.Count > 0 ? "(" + string.Join(",", names) + ")" : "")}{(renamed.Length > 0 ? " · 이름 " + renamed : "")}";
    }

    /// <summary>종단이 쓸 목록 맨 뒤에 붙인다(밴드 순서를 안 흔든다 — FindSurfaces 주석).</summary>
    internal static void AppendPick(System.Collections.Generic.List<Commands.SectionCommand.SurfPick> surfs, Result? r)
    {
        if (r == null || r.State != State.Built || r.SurfaceId.IsNull) return;
        surfs.RemoveAll(sp => sp.Label == Label);
        surfs.Add(new Commands.SectionCommand.SurfPick(r.SurfaceId, r.SurfaceName, ProfName, Label));
    }

    /// <summary>★[계획 2-4 · 재검토 중간 5 · 6] 횡단 · 검토선 — 선형 기록을 따라 터파기면을 고른다. 거짓 = 멈춤(«종단 먼저»).</summary>
    internal static bool ForAlignment(Database db, ObjectId alignId, ObjectId groundId, System.Collections.Generic.List<Commands.SectionCommand.SurfPick> surfs,
                                      System.Action<string> say)
    {
        string now = CurrentHead(db, groundId, out var s);
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var al = tr.GetObject(alignId, OpenMode.ForRead);
            var rec = ReadAlign(tr, al);
            bool ourProfile = PureWithWallsBuilder.ReadAlign(tr, al) != null;
            tr.Commit();
            if (rec == null)
            {
                if (!s.On) return true;
                if (ourProfile) { say("옹벽 터파기를 켠 뒤 종단을 다시 안 그렸습니다 — [종단도]를 먼저 다시 그리세요."); return false; }
                say("⚠옹벽 터파기 빠짐 — 이 노선은 터파기 기록이 없습니다([종단도]를 그리면 들어갑니다).");
                return true;
            }
            var r = rec.Value;
            if (r.State == State.Off)
            {
                if (s.On) { say("옹벽 터파기를 켠 뒤 종단을 다시 안 그렸습니다 — [종단도]를 먼저 다시 그리세요."); return false; }
                return true;
            }
            if (!s.On) { say("옹벽 터파기를 끈 뒤 종단을 다시 안 그렸습니다 — [종단도]를 먼저 다시 그리세요."); return false; }
            if (StampHead(r.Stamp) != now) { say("옹벽 터파기 설정 · 정지면이 바뀌었습니다 — [종단도]를 먼저 다시 그리세요."); return false; }
            if (r.State == State.Failed) { say("⚠종단이 옹벽 터파기면을 못 지었습니다 — " + r.Why); return true; }
            ObjectId sid = ObjectId.Null;
            try { sid = db.GetObjectId(false, new Handle(System.Convert.ToInt64(r.Handle, 16)), 0); } catch { }
            string nm = "";
            bool alive = false;
            using (var tr2 = db.TransactionManager.StartTransaction())
            {
                try { alive = !sid.IsNull && !sid.IsErased && tr2.GetObject(sid, OpenMode.ForRead) is Surface s1 && (nm = s1.Name ?? "") != null; } catch { }
                tr2.Commit();
            }
            if (!alive) { say("종단이 쓴 옹벽 터파기면이 없습니다 — [종단도]를 다시 그리세요."); return false; }
            AppendPick(surfs, new Result { State = State.Built, SurfaceId = sid, SurfaceName = nm });
            if (r.Why.Length > 0) say("⚠종단이 쓴 옹벽 터파기면 — " + r.Why);
            return true;
        }
        catch (System.Exception ex) { say("⚠옹벽 터파기 기록을 못 읽었다 — 터파기 없이 그립니다: " + ex.Message); return true; }
    }

    // ══ 짓기 ════════════════════════════════════════════════════════════════

    /// <summary>★계획 2-3 · 2-4 — 터파기면을 짓거나 되쓴다(꺼짐이면 아무것도 안 함). 예외를 안 던진다(못 지으면 «못 지음» + 까닭).</summary>
    /// <param name="pure">이번 종단의 옹벽면 결과 — 그 <c>Walls</c>가 종단 정지면에 든 구간 · 굳은 옹벽이다(재검토 중간 7).</param>
    internal static Result Ensure(Database db, ObjectId groundId, PureWithWallsBuilder.Result? pure, StringBuilder log)
    {
        var evPrev = EventViewerMute.Begin();   // LandXML 가져오기 정보 한 줄에 이벤트 뷰어 창이 안 뜨게(v103.2.1과 같은 법)
        try { return EnsureCore(db, groundId, pure, log); }
        finally { EventViewerMute.End(evPrev); }
    }

    sealed class PieceWork
    {
        public WallExcav.Bottom Bottom = null!;
        public System.Collections.Generic.List<Point3> Ring = new();
        public double Rise, ZbMin, DepthMax, Reach;
        public bool Benches, Grown;
        public WallDaylight.ExcavPiece? Piece;
        public string Note = "";
    }

    private static Result EnsureCore(Database db, ObjectId groundId, PureWithWallsBuilder.Result? pure, StringBuilder log)
    {
        var R = new Result();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var st = WallListStore.ExcavOf(db, out string swhy);
        R.Settings = st;
        if (swhy.Length > 0) log.AppendLine("  옹벽 터파기 — ⚠설정 기록: " + swhy);
        if (!st.On)
        {
            R.State = State.Off;
            R.Stamp = HeadOf(st, "", "", "");
            log.AppendLine("  옹벽 터파기 — 꺼짐");
            return R;
        }
        ObjectId made = ObjectId.Null;
        var pieceIds = new System.Collections.Generic.List<ObjectId>();
        var notes = new System.Collections.Generic.List<string>();
        string failLog = "";   // ★[v103.3.1] 못 했을 때 로그에 덧붙일 앞면 · 조각 · 입력 남긴 자리
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            // 0. 지난번에 남은 임시 · 조각 면
            int temp = 0;
            foreach (ObjectId sid in civ.GetSurfaceIds())
                try { if (tr.GetObject(sid, OpenMode.ForRead) is Surface s && (s.Name == TempName || s.Name == PieceName || (s.Name ?? "").StartsWith(PieceName + "_"))) { tr.GetObject(sid, OpenMode.ForWrite).Erase(); temp++; } } catch { }
            if (temp > 0) log.AppendLine($"  옹벽 터파기 — 지난번 임시 면 {temp}개 지움");
            // 1. 면 — 정지면_DH(기준면 · 재는 면 S) · 원지반(G)
            var (planIds, planNms) = Commands.WallCompositeCommand.Candidates(civ, tr, Commands.WallCompositeCommand.PlanName, groundId);
            if (planIds.Count != 1) return Fail($"정지면_DH를 하나로 못 잡았다({(planIds.Count == 0 ? "없음" : string.Join(",", planNms))})");
            if (groundId.IsNull || groundId.IsErased || tr.GetObject(groundId, OpenMode.ForRead) is not TinSurface gTin) return Fail("원지반이 없다");
            var sTin = (TinSurface)tr.GetObject(planIds[0], OpenMode.ForRead);
            string nonce = Commands.WallCompositeCommand.CompositeNonce(tr, sTin);
            string head = HeadOf(st, planIds[0].Handle.ToString(), nonce, groundId.Handle.ToString());
            R.Stamp = head + "#";   // 아래에서 못 지어도 머리는 맞게(횡단이 «바뀜»을 헛되게 말하지 않게)
            var sC = new CachedGroundSurface(sTin);
            var gC = new CachedGroundSurface(gTin);

            // 2. 앞면 선 — 구간 · 굳은 옹벽(종단 정지면에 든 것 = pure.Walls) · 번들 옹벽
            //   ★[1021 화면 없는 Civil 시험 — 12/22 실패의 뿌리] 벽 높이 · 벽 밑은 <b>끊김 없는 정지면_DH</b>에서 잰다 — 종단 옹벽면(정지순수옹벽_DH)에는
            //     벽 위 원지반(뚜껑) · 성토 옹벽 앞 원지반이 빠져 흙 쪽 표본이 전부 «면을 못 읽음»이었다. 옹벽면은 정지면_DH 삼각형을 그대로 골라 낸 것이라 값이 같다.
            long t0 = sw.ElapsedMilliseconds;
            System.Func<double, double, double?> mAt = (x, y) => sC.TryGetElevation(x, y, out double z) ? z : (double?)null;
            var bottoms = new System.Collections.Generic.List<WallExcav.Bottom>();
            var mn = new WallExcav.MeasureNote();
            var fp = new StringBuilder();       // 앞면 지문
            int nFaces = 0;
            var stepLog = new System.Collections.Generic.List<string>();
            double gsMin = GradingSettings.MinSlope, gsRun = GradingSettings.MinFaceRun;
            foreach (var w in pure?.Walls ?? new System.Collections.Generic.List<PureWithWallsBuilder.ReflWall>())
            {
                string src = (w.Frozen ? "굳은 옹벽 " : "옹벽 ") + w.No;
                if (w.IsWall.Count != w.Poly.Count || w.Poly.Count < 3)
                {
                    notes.Add(w.Frozen ? $"옛 굳은 옹벽 {w.No} — 옹벽선 기록이 없어 터파기에서 빠짐(앞 구역을 다시 생성하면 들어감)" : $"{src} — 옹벽선 표가 없어 빠짐");
                    continue;
                }
                double slopeW = w.SlopeW >= 0 ? w.SlopeW : gsMin, faceRun = w.FaceRun >= 0 ? w.FaceRun : gsRun;
                // ★★[설치 전 검토 높음 1] 단 수는 벽 짓기와 같은 셈(폴리곤 안 최종면 최고 · 최저로 BenchCount)으로 묶는다 — «벽이 안 보일 때까지»로 세면
                //   비탈진 땅을 단으로 잡아 가짜 단이 이어질 수 있었다. 한 단 더(N+1)는 재기만 해서 벽이 1 m 넘게 보이면 ⚠(넣지 않는다).
                int nB = WallExcav.ZoneBenches(w.Poly, w.Up, w.HeadZ, w.H, mAt, System.Math.Max(1.0, GradingSettings.VertexSpacing));
                if (nB <= 0) { notes.Add($"{src} — 폴리곤 안 최종면을 못 재 단 수를 모른다(1단만)"); nB = 1; }
                int kFound = 0;
                for (int k = 1; k <= nB + 1; k++)
                {
                    var faces = WallExcav.ZoneFaces(w.Poly, w.IsWall, w.Up, w.HeadZ, k, w.H, w.T, slopeW, faceRun, src, out string fn, fromBench: k);
                    // ★[재검토 낮음 3] 2단부터도 «못 뽑았다 · 흙 쪽을 못 가렸다»는 알린다(빠진 단이 말없이 사라지지 않게) — 폴리곤 밖은 좁은 자리라 1단만
                    if (fn.Length > 0 && k <= nB && (k == 1 || !fn.Contains("앞면 선 없음"))) notes.Add(fn);
                    if (k == nB + 1)
                    {
                        var mx = new WallExcav.MeasureNote();
                        double keptX = 0;
                        foreach (var f in faces) { WallExcav.Measure(f, mAt, st.Foot, mx); keptX = mx.Kept; }
                        if (keptX > 1.0) notes.Add($"{src} — {nB}단 너머에도 벽이 {keptX:F1}m 보인다(단 수가 모자람 — 그 단은 터파기에 안 넣었다)");
                        break;
                    }
                    double keptBefore = mn.Kept;
                    foreach (var f in faces)
                    {
                        nFaces++;
                        fp.Append(f.Src).Append(f.Soil).Append(';');
                        foreach (var q in f.Pts) fp.Append(q.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(q.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
                        bottoms.AddRange(WallExcav.Measure(f, mAt, st.Foot, mn));
                    }
                    if (mn.Kept - keptBefore > 1e-6) kFound = k;
                }
                stepLog.Add($"{src} {(w.Up ? "절토" : "성토")} {kFound}단(셈 {nB}단)");
            }
            var bundles = GradingBundleStore.TryLoadAll(db, tr, out string bwhy);
            if (bundles != null)
            {
                int zi = 0;
                foreach (var b in bundles)
                {
                    zi++;
                    foreach (var (runs, up) in new[] { (b.CutWallRuns, true), (b.FillWallRuns, false) })
                    {
                        if (runs == null) continue;
                        int ri = 0;
                        foreach (var r in runs)
                        {
                            ri++;
                            string src = $"구역{zi} {(up ? "절토" : "성토")} 옹벽선{ri}({r.Bench + 1}단)";
                            var f = WallExcav.RunFace(r, b.Params.MinSlope, b.Params.MinFaceRun, src, out string why);
                            if (f == null) { notes.Add(why); continue; }
                            nFaces++;
                            fp.Append(f.Src).Append(f.Soil).Append(';');
                            foreach (var q in f.Pts) fp.Append(q.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(q.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
                            bottoms.AddRange(WallExcav.Measure(f, mAt, st.Foot, mn));
                        }
                    }
                }
            }
            else if (bwhy.Length > 0) notes.Add("번들을 못 읽어 번들 옹벽은 빠짐 — " + bwhy);
            long tFaces = sw.ElapsedMilliseconds - t0;
            if (mn.Warn) notes.Add("앞면 재기 — " + mn.Text);
            R.Faces = nFaces;
            // ★[설치 전 검토 낮음 5] 지문에 정지면 · 원지반 삼각형 지문도 — 같은 핸들로 면을 그 자리에서 고쳐도 옛 터파기면을 되쓰지 않게
            fp.Append("|S").Append(sC.Signature).Append("|G").Append(gC.Signature);
            string fpHash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                fpHash = System.Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(fp.ToString()))).Substring(0, 16);
            R.Stamp = head + "#" + fpHash + "#v1";
            string facesTxt = $"앞면 선 {nFaces}줄({string.Join(" · ", stepLog)}{(bundles != null ? $" · 번들 구역 {bundles.Count}" : "")}) · {mn.Text} · 바닥 줄 {bottoms.Count}";
            if (bottoms.Count == 0)
            {
                R.State = State.Failed; R.Why = nFaces == 0 ? "종단 정지면에 옹벽이 없다" : "옹벽 앞면에서 벽을 못 찾았다(벽 높이 0.1m 미만)";
                if (notes.Count > 0) R.Why += " · " + string.Join(" · ", notes);
                log.AppendLine($"  옹벽 터파기 — {R.StatusText} · {facesTxt}");
                tr.Commit(); return R;
            }

            // 3. 같은 도장의 면이 있으면 되쓴다
            var ex = Existing(tr);
            var same = ex.FirstOrDefault(e => e.Stamp == R.Stamp && !e.Id.IsErased);
            if (!same.Id.IsNull)
            {
                R.State = State.Built; R.SurfaceId = same.Id; R.SurfaceName = same.Name; R.Reused = true;
                try { R.Why = ReadStamp(tr, tr.GetObject(same.Id, OpenMode.ForRead), 2); } catch { }
                log.AppendLine($"  옹벽 터파기 — {R.StatusText} · {facesTxt} · {sw.ElapsedMilliseconds}ms");
                tr.Commit(); return R;
            }

            // 4. 조각 — 바닥 띠 → Build → Civil TIN → 삼각형 읽고 지움
            double? L(double x, double y)
            {
                bool hs = sC.TryGetElevation(x, y, out double zs), hg = gC.TryGetElevation(x, y, out double zg);
                return hs && hg ? System.Math.Min(zs, zg) : hs ? zs : hg ? zg : (double?)null;
            }
            long t1 = sw.ElapsedMilliseconds;
            var works = new System.Collections.Generic.List<PieceWork>();
            foreach (var b in bottoms)
            {
                var ring = WallExcav.StripRing(b, st.Back, st.Front, out string rw);
                if (ring == null) { notes.Add($"{b.Src} 띠 못 만듦 — {rw}"); continue; }
                if (rw.Length > 0) notes.Add($"{b.Src} — {rw}");
                var pw = new PieceWork { Bottom = b, Ring = ring, ZbMin = ring.Min(q => q.Z) };
                // 깊이 최대(소단 판정) · 처음 예산 — 띠 꼭짓점에서 L − z, 그리고 띠 둘레 «뒤 + 깊이 × n + 5 m» 상자 안 L 최대
                double dMax = 0, lMax = double.MinValue;
                foreach (var q in ring) { var lz = L(q.X, q.Y); if (lz != null) { dMax = System.Math.Max(dMax, lz.Value - q.Z); lMax = System.Math.Max(lMax, lz.Value); } }
                pw.DepthMax = dMax;
                // ★[v103.3.1 · JACK 1021 «켬/끔 + 두 칸»] 켜면 깊이와 상관없이 높이마다 — 법면이 소단 높이를 못 넘으면 단 모양이 저절로 소단을 안 만든다.
                //   ★[설치 전 검토 중간] 띠 깊이로 가르지 않는다 — 띠 깊이 3.0 m(소단 없음)와 3.1 m(위 사면이 완만하면 소단 4 ~ 5개) 사이에서 수량이 뛰었다(사면은 띠보다 훨씬 높이 올라갈 수 있다).
                pw.Benches = st.BenchOn;
                double reach = st.Back + System.Math.Max(0, dMax) * st.Slope + (pw.Benches ? System.Math.Floor(dMax / st.BenchH) * st.BenchW : 0) + 5.0;
                double bx0 = ring.Min(q => q.X) - reach, bx1 = ring.Max(q => q.X) + reach, by0 = ring.Min(q => q.Y) - reach, by1 = ring.Max(q => q.Y) + reach;
                for (double xx = bx0; xx <= bx1; xx += 2.0) for (double yy = by0; yy <= by1; yy += 2.0) { var lz = L(xx, yy); if (lz != null) lMax = System.Math.Max(lMax, lz.Value); }
                pw.Rise = System.Math.Max(1.0, (lMax == double.MinValue ? pw.ZbMin + 5 : lMax) - pw.ZbMin + 1.0);
                pw.Reach = st.Back + (b.WallHMax + st.Foot) * st.Slope + (pw.Benches ? System.Math.Floor((b.WallHMax + st.Foot) / st.BenchH) * st.BenchW : 0);
                works.Add(pw);
            }
            bool BuildPiece(PieceWork pw, int idx)
            {
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    try
                    {
                        var p = Params(st.Slope, pw.Rise, pw.Benches, st.BenchH, st.BenchW);
                        var vs = GradingGeometry.Build(pw.Ring, sC, p, up: true, null);
                        if (!vs.HasSlope || vs.Rings.Count == 0) { pw.Note = "법면이 안 만들어졌다"; return false; }
                        var outer = vs.Rings[^1];
                        // 바깥 링이 어디서나 기준면 위인가(전체 면에서 읽는다 — 재검토 낮음)
                        bool above = true;
                        for (int i = 0; i < outer.Count && above; i++)
                        {
                            var a = outer[i]; var c = outer[(i + 1) % outer.Count];
                            foreach (var (x, y, z) in new[] { (a.X, a.Y, a.Z), ((a.X + c.X) / 2, (a.Y + c.Y) / 2, (a.Z + c.Z) / 2) })
                            { var lz = L(x, y); if (lz != null && lz.Value >= z) { above = false; break; } }
                        }
                        if (!above)
                        {
                            if (pw.Rise >= 60) { pw.Note = "예산 60m에서도 기준면을 못 만남(JACK 결정 5의 예외)"; return false; }
                            pw.Rise = System.Math.Min(60, pw.Rise * 2); pw.Grown = true; continue;
                        }
                        // ★[1021 시험 ③] 바닥 줄도 브레이크라인으로 — 바닥이 그 줄의 높이를 따라 평평하게(띠 테두리만으로는 Civil이 바닥을 비스듬히 이었다)
                        var lines = new System.Collections.Generic.List<System.Collections.Generic.List<Point3>>(vs.CornerLines) { new System.Collections.Generic.List<Point3>(pw.Bottom.Pts) };
                        var vid = GradingBuilder.BuildVirtualSlope(db, tr, vs.Rings, PieceName, lines, groundId);
                        pieceIds.Add(vid);
                        var vTin = (TinSurface)tr.GetObject(vid, OpenMode.ForRead);
                        var tris = PureWithWallsBuilder.ReadTris(vTin, out int lost);
                        try { tr.GetObject(vid, OpenMode.ForWrite).Erase(); } catch { }
                        if (lost > 0) { pw.Note = $"조각 삼각형을 읽다 {lost}개 버림"; return false; }
                        var kept = WallExcav.DropOutside(tris, outer, out int dropped);
                        pw.Piece = new WallDaylight.ExcavPiece { Tris = kept, Outer = outer };
                        if (dropped > 0) pw.Note = $"껍질 메움 삼각형 {dropped}개 버림";
                        return true;
                    }
                    catch (System.Exception exP) { pw.Note = $"조각 짓기 예외 {exP.GetType().Name}: {exP.Message}"; return false; }
                }
                pw.Note = "예산을 늘려도 기준면을 못 만남";
                return false;
            }
            for (int i = 0; i < works.Count; i++)
                if (!BuildPiece(works[i], i)) notes.Add($"{works[i].Bottom.Src} 조각 빠짐 — {works[i].Note}");
            long tPieces = sw.ElapsedMilliseconds - t1;
            var good = works.Where(w => w.Piece != null).ToList();
            R.Pieces = good.Count;
            if (good.Count == 0) return Fail("조각을 하나도 못 지었다 · " + string.Join(" · ", notes));

            // 5. 아래 껍질 — S · G는 조각 바깥 링 상자 + 1 m 안 삼각형만
            long t2 = sw.ElapsedMilliseconds;
            var sAll = PureWithWallsBuilder.ReadTris(sTin, out int lS);
            var gAll = PureWithWallsBuilder.ReadTris(gTin, out int lG);
            if (lS + lG > 0) return Fail($"정지면 · 원지반 삼각형을 읽다 {lS + lG}개를 버렸다");
            // ★[설치 전 검토 낮음] 상자는 조각이 바뀔 때마다(예산을 늘려 다시 지은 뒤) 다시 잰다 — 첫 상자로 고정하면 늘어난 자리에 기준면이 없어 «기준면 없음» 판정 3이 났다
            System.Collections.Generic.List<Tri> sTris = null!, gTris = null!;
            void PickSG()
            {
                double ex0 = good.Min(w => w.Piece!.Outer.Min(q => q.X)) - 1, ex1 = good.Max(w => w.Piece!.Outer.Max(q => q.X)) + 1;
                double ey0 = good.Min(w => w.Piece!.Outer.Min(q => q.Y)) - 1, ey1 = good.Max(w => w.Piece!.Outer.Max(q => q.Y)) + 1;
                bool InBox(Tri t) => System.Math.Max(t.A.X, System.Math.Max(t.B.X, t.C.X)) >= ex0 && System.Math.Min(t.A.X, System.Math.Min(t.B.X, t.C.X)) <= ex1
                                  && System.Math.Max(t.A.Y, System.Math.Max(t.B.Y, t.C.Y)) >= ey0 && System.Math.Min(t.A.Y, System.Math.Min(t.B.Y, t.C.Y)) <= ey1;
                sTris = sAll.Where(InBox).ToList();
                gTris = gAll.Where(InBox).ToList();
            }
            PickSG();
            // 계측 — 환경 변수 DH_EXCAV_DUMP 폴더가 있으면 엔진 입력을 글 파일로(오프라인 재현 · 출하 PC에는 없는 변수)
            try
            {
                string? dumpDir = System.Environment.GetEnvironmentVariable("DH_EXCAV_DUMP");
                if (!string.IsNullOrWhiteSpace(dumpDir) && System.IO.Directory.Exists(dumpDir))
                    WallDaylight.DumpEnvelopeInput(System.IO.Path.Combine(dumpDir, $"excav_{System.DateTime.Now:HHmmss_fff}.txt"), sTris, gTris, good.Select(w => w.Piece!).ToList());
            }
            catch { }
            WallDaylight.EnvelopeResult env = WallDaylight.LowerEnvelope(sTris, gTris, good.Select(w => w.Piece!).ToList());
            // 예산 끝에 닿은 조각은 그 조각만 예산을 두 배로 다시 짓는다(두 번까지)
            for (int round = 0; round < 2 && env.PieceRim.Any(x => x); round++)
            {
                for (int i = 0; i < good.Count; i++)
                    if (env.PieceRim[i] && good[i].Rise < 60)
                    {
                        good[i].Rise = System.Math.Min(60, good[i].Rise * 2); good[i].Grown = true;
                        if (!BuildPiece(good[i], i)) notes.Add($"{good[i].Bottom.Src} 조각을 예산을 늘려 다시 못 지었다(앞 조각을 그대로 씀) — {good[i].Note}");   // ★[검토 낮음 6]
                    }
                PickSG();
                env = WallDaylight.LowerEnvelope(sTris, gTris, good.Select(w => w.Piece!).ToList());
            }
            // ★[v103.3.1 · JACK 1021 «체크하고 종단 그렸는데도 안나와»] 개발 PC에서 못 하면 엔진 입력을 진단이력\excav에 남긴다(최근 6개 · 오프라인 재현용 · 마지막 계산의 입력)
            if (env.Tier == 3) { string dmp = DumpOnFail(sTris, gTris, good.Select(w => w.Piece!).ToList()); if (dmp.Length > 0) failLog = dmp; }
            long tEnv = sw.ElapsedMilliseconds - t2;
            for (int i = 0; i < good.Count; i++)
            {
                if (env.PieceRim.Length > i && env.PieceRim[i]) notes.Add($"{good[i].Bottom.Src} — 예산 끝까지 팠다(기준면을 못 만남 · 결정 5의 예외)");
                else if (good[i].Grown) notes.Add($"{good[i].Bottom.Src} — 위 사면이 터파기 경사보다 가파를 수 있음(벽 뒤로 보통 거리 {good[i].Reach:F1}m보다 멀리 팠다)");
            }
            // ★[v103.3.1] 못 했을 때도 로그에 앞면 · 조각을 남긴다(1021 현장 로그엔 껍질 요약만 있어 어느 옹벽 · 어느 조각인지 못 갈랐다)
            failLog = $"{facesTxt} · 조각 {good.Count}/{works.Count}({string.Join(" ", good.Select(w => $"{w.Bottom.Src}:깊이{w.DepthMax:F1}m{(w.Benches ? "·소단" : "")}·예산{w.Rise:F0}·삼각형{w.Piece!.Tris.Count}{(w.Note.Length > 0 ? "·" + w.Note : "")}"))})"
                    + $" · 정지면 {sTris.Count} · 원지반 {gTris.Count} · 시간 앞면 {tFaces} · 조각 {tPieces} · 껍질 {tEnv}ms" + (failLog.Length > 0 ? " · " + failLog : "");
            if (env.Tier == 3 || env.Tris == null) return Fail("터파기면 계산 못 함 — " + env.Fail + $" · {env.Summary}");
            if (env.Tier == 2) notes.Add("터파기면 «거의»(판정 2) — " + env.Fail);

            // 6. LandXML → 임시 면 → 되읽기 → 굳힘
            long t3 = sw.ElapsedMilliseconds;
            var final = CivilSafeMesh.FixOutside(env.Tris, _ => true, out var ofx);
            try { string fz0 = GradingBuilder.FreezeAllXmlBacked(tr); if (fz0.Length > 0) log.AppendLine("  옹벽 터파기 — 굳힘: " + fz0); } catch { }
            try { string td = GradingBuilder.TidyXml(db, tr); if (td.Length > 0) log.AppendLine("  옹벽 터파기 — " + td); } catch { }
            string xml = GradingBuilder.NewXmlPath(db, "옹벽터파기");
            WallDaylight.WriteLandXmlTin(xml, "WALLEXCAV", final, 1e-7, out int np, out int nf, out int nd);
            ObjectId cid = TinSurface.CreateFromLandXML(db, TempName, xml, "WALLEXCAV");
            if (cid.IsNull) throw new System.InvalidOperationException("CreateFromLandXML이 빈 ObjectId를 돌려줬다");
            made = cid;
            var cTin = (TinSurface)tr.GetObject(cid, OpenMode.ForWrite);
            int civPts = -1; try { civPts = cTin.GetGeneralProperties().NumberOfPoints; } catch { }
            var theirs = PureWithWallsBuilder.ReadTris(cTin, out int l4);
            var cmp = CivilSafeMesh.Compare(final, theirs);
            bool gate = l4 == 0 && nd == 0 && theirs.Count == nf && civPts == np && cmp.Exceptions == 0 && cmp.SymArea <= 1e-8 && cmp.Hausdorff <= 1e-6 && cmp.MaxDz <= 1e-3;
            string back = $"Civil 점 {civPts}/{np} · 삼각형 {theirs.Count}/{nf} · {cmp.Summary}" + (nd > 0 ? $" · ⚠넓이 0이라 뺀 면 {nd}" : "");
            if (!gate)
            {
                // ★[v103.3.2 · 1021 17:06 현장 계측] Civil이 버리거나 바꾼 삼각형을 적는다(자리 · 넓이 · 높이(가장 긴 변에서) · 가장 짧은 변) — 까닭을 가리려고
                failLog = MissingTris(final, theirs) + " · 아래 껍질 " + env.Summary + (failLog.Length > 0 ? " · " + failLog : "");
                try { cTin.Erase(); } catch { }
                made = ObjectId.Null;
                return Fail("Civil이 넘긴 터파기면을 그대로 안 받았다 — " + back);
            }
            string frz = GradingBuilder.FreezeXmlBacked(cTin);
            if (frz.Length > 0) notes.Add($"터파기면을 굳히지 못했다({frz}) — 원본 XML이 바뀌면 면이 바뀔 수 있다");
            // 낡은 터파기면 — 선형이 안 쓰는 것만 지운다
            var refd = new System.Collections.Generic.HashSet<string>();
            foreach (ObjectId aid in civ.GetAlignmentIds())
                try { var r0 = ReadAlign(tr, tr.GetObject(aid, OpenMode.ForRead)); if (r0 != null && r0.Value.Handle.Length > 0) refd.Add(r0.Value.Handle); } catch { }
            int erased = 0, keptOld = 0;
            foreach (var e in ex)
            {
                if (e.Id.IsErased) continue;
                if (refd.Contains(e.Id.Handle.ToString())) { keptOld++; continue; }
                try { tr.GetObject(e.Id, OpenMode.ForWrite).Erase(); erased++; } catch { }
            }
            string nm = GradingBuilder.UniqueName(db, tr, Name);
            cTin.Name = nm;
            try { cTin.Description = $"DH 옹벽 뒷채움 터파기면(종단 · 횡단 · 수량용) — {System.DateTime.Now:yyyy-MM-dd HH:mm} · {GradingSettings.Version} · {st.Text}"; } catch { }
            string warn = string.Join(" · ", notes);
            WriteStamp(tr, cTin, R.Stamp, $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} · {GradingSettings.Version}", warn);
            GradingBuilder.SetSurfaceVisible(tr, Name, false);
            tr.Commit();
            made = ObjectId.Null;
            R.State = State.Built; R.SurfaceId = cid; R.SurfaceName = nm; R.Why = warn;
            long tImp = sw.ElapsedMilliseconds - t3;
            log.AppendLine($"  옹벽 터파기 — 지었다 {nm} · {st.Text} · {facesTxt} · 조각 {good.Count}/{works.Count}"
                         + $"({string.Join(" ", good.Select(w => $"{w.Bottom.Src}:깊이{w.DepthMax:F1}m{(w.Benches ? "·소단" : "")}·예산{w.Rise:F0}{(w.Note.Length > 0 ? "·" + w.Note : "")}"))})"
                         + $" · 아래 껍질 {env.Summary}" + (ofx.Flipped > 0 ? $" · Civil이 고칠 면 먼저 뒤집음 {ofx.Flipped}" : "")
                         + $" · 되읽기 {back} · {(frz.Length == 0 ? "굳힘" : "⚠굳힘 실패 " + frz)} · 옛 면 지움 {erased}(선형이 쓰는 것 {keptOld} 남김)"
                         + $" · {sw.ElapsedMilliseconds}ms(앞면 {tFaces} · 조각 {tPieces} · 껍질 {tEnv} · 가져오기 {tImp}) · 도장 {R.Stamp}"
                         + (warn.Length > 0 ? " · ⚠" + warn : ""));
            return R;

            static string MissingTris(System.Collections.Generic.IReadOnlyList<Tri> ours, System.Collections.Generic.IReadOnlyList<Tri> theirs)
            {
                static (long, long) K(Point3 q) => ((long)System.Math.Round(q.X / 1e-7), (long)System.Math.Round(q.Y / 1e-7));
                static ((long, long), (long, long), (long, long)) Key(Tri t) { var a = new[] { K(t.A), K(t.B), K(t.C) }; System.Array.Sort(a); return (a[0], a[1], a[2]); }
                var tk = new System.Collections.Generic.HashSet<((long, long), (long, long), (long, long))>(theirs.Select(Key));
                var ok = new System.Collections.Generic.HashSet<((long, long), (long, long), (long, long))>(ours.Select(Key));
                static string D(Tri t)
                {
                    double e1 = System.Math.Sqrt((t.A.X - t.B.X) * (t.A.X - t.B.X) + (t.A.Y - t.B.Y) * (t.A.Y - t.B.Y));
                    double e2 = System.Math.Sqrt((t.B.X - t.C.X) * (t.B.X - t.C.X) + (t.B.Y - t.C.Y) * (t.B.Y - t.C.Y));
                    double e3 = System.Math.Sqrt((t.C.X - t.A.X) * (t.C.X - t.A.X) + (t.C.Y - t.A.Y) * (t.C.Y - t.A.Y));
                    double ar = System.Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.C.X - t.A.X) * (t.B.Y - t.A.Y)) / 2;
                    double lo = System.Math.Max(e1, System.Math.Max(e2, e3)), sh = System.Math.Min(e1, System.Math.Min(e2, e3));
                    return string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:F4},{1:F4}) 넓이 {2:E2}㎡ · 높이 {3:F1}µm · 짧은 변 {4:F1}µm · z {5:F3}/{6:F3}/{7:F3}",
                        (t.A.X + t.B.X + t.C.X) / 3, (t.A.Y + t.B.Y + t.C.Y) / 3, ar, lo > 0 ? 2 * ar / lo * 1e6 : 0, sh * 1e6, t.A.Z, t.B.Z, t.C.Z);
                }
                var gone = ours.Where(t => !tk.Contains(Key(t))).ToList();
                var added = theirs.Where(t => !ok.Contains(Key(t))).ToList();
                return $"Civil이 버린 · 바꾼 삼각형 {gone.Count}: " + string.Join(" | ", gone.Take(4).Select(D))
                     + (added.Count > 0 ? $" · Civil이 새로 만든 삼각형 {added.Count}: " + string.Join(" | ", added.Take(4).Select(D)) : "");
            }

            static string DumpOnFail(System.Collections.Generic.List<Tri> s, System.Collections.Generic.List<Tri> g, System.Collections.Generic.List<WallDaylight.ExcavPiece> p)
            {
                if (!M0Dump.IsDevPc()) return "";
                try
                {
                    string root = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(DiagLog.FilePath))!, "진단이력", "excav");
                    System.IO.Directory.CreateDirectory(root);
                    string path = System.IO.Path.Combine(root, $"excav_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    WallDaylight.DumpEnvelopeInput(path, s, g, p);
                    foreach (var old in new System.IO.DirectoryInfo(root).GetFiles("excav_*.txt").OrderByDescending(f => f.Name).Skip(6))
                        try { old.Delete(); } catch { }
                    return "(개발 PC) 입력을 남김 " + path;
                }
                catch (System.Exception exD) { return "(개발 PC) 입력 남기기 실패 " + exD.Message; }
            }

            Result Fail(string why)
            {
                R.State = State.Failed;
                if (R.Stamp.Length == 0) R.Stamp = HeadOf(st, "", "", groundId.IsNull ? "" : groundId.Handle.ToString());   // ★[검토 낮음 10] 빈 도장이면 횡단이 엉뚱한 «바뀜»을 말한다
                R.Why = why + (notes.Count > 0 && !why.Contains(notes[0]) ? " · " + string.Join(" · ", notes) : "");
                log.AppendLine($"  옹벽 터파기 — {R.StatusText} · {sw.ElapsedMilliseconds}ms" + (failLog.Length > 0 ? "\n    " + failLog : ""));
                try { tr.Commit(); } catch { }
                return R;
            }
        }
        catch (System.Exception ex)
        {
            log.AppendLine($"  옹벽 터파기 — ⚠짓다 터졌다({ex.GetType().Name}: {ex.Message})");
            try
            {
                using var tr2 = db.TransactionManager.StartTransaction();
                foreach (var id in pieceIds.Append(made))
                    try { if (!id.IsNull && !id.IsErased) tr2.GetObject(id, OpenMode.ForWrite).Erase(); } catch { }
                tr2.Commit();
            }
            catch { }
            R.State = State.Failed;
            R.Why = $"터파기면을 못 지었다({ex.GetType().Name}: {ex.Message})";
            return R;
        }
    }

    /// <summary>터파기 제원 — 경사 n · 소단을 두면 <paramref name="benchH"/>마다 <paramref name="benchW"/> 소단(★[v103.3.1 · JACK 1021] 도킹바 «소단» 켬 · 높이 · 폭 — 종전 고정 «5 m 넘으면 3 m마다 1 m»)
    /// · 아니면 한 장의 법면(DHEXCAV와 같은 꼴).</summary>
    static GradingParams Params(double n, double rise, bool benches, double benchH, double benchW)
    {
        double sl = System.Math.Max(n, GradingSettings.MinSlope);
        double bh = benches ? benchH : rise + 1.0;
        double bw = benches ? benchW : 0.0;
        return new GradingParams
        {
            CutBenchHeight = bh, FillBenchHeight = bh,
            CutBenchWidth = bw, FillBenchWidth = bw,
            CutSlope = sl, FillSlope = sl,
            CellSize = GradingSettings.CellSize,
            MaxBenches = benches ? (int)System.Math.Ceiling(rise / benchH) + 2 : 4,
            MaxRise = rise,
            VertexSpacing = GradingSettings.VertexSpacing,
            MinSlope = GradingSettings.MinSlope,
            WallGateSlope = GradingSettings.WallGateSlope,
            MinFaceRun = GradingSettings.MinFaceRun,
            MiterConvex = GradingSettings.MiterConvex,
            MiterLimit = GradingSettings.MiterLimit,
        };
    }
}
