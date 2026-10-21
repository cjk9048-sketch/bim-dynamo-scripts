using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using Tri = DH.Grading.Core.WallDaylight.Tri;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Surface = Autodesk.Civil.DatabaseServices.Surface;

namespace DH.Grading.Civil;

/// <summary>★★★[v103.2 · JACK 1008 «최종 지표면을 기준으로 … 종단 횡단에도 반영» · 1020 «옹벽변환과 사면변환을 거친 지표면이 반영되게»]
/// <b>«옹벽이 든 순수면»(<c>정지순수옹벽_DH</c>)을 짓는다 — 종단을 그리는 명령이 부른다</b>(계획_v103_2.md 2-3 · 4판).
/// <para>최종 <c>정지면_DH</c> 삼각형 가운데 (가) <c>정지순수_DH</c> 발자국 안이고 (나) 옹벽 뚜껑 밖이거나 앞 구역 계획면 위인 것을 <b>한 점도 안 바꾸고</b> 옮긴다
/// (<see cref="PureWithWalls.Select"/>) → 종단 · 횡단 · 수량이 InfraWorks 지형 · 토공량과 같은 삼각형을 본다. 옹벽 · 사면 변환 때는 아무것도 더 안 한다(JACK 결정 ②).</para>
/// <para>면 도장(<c>정지면_DH</c> 핸들 · 합성 표지의 일회용 번호 · <c>정지순수_DH</c> 핸들 · <c>정지순수_DH이전</c> 핸들)이 같은 옹벽면이 있으면 되쓴다.
/// LandXML 면이라 v103.1.5 방식(새 이름의 파일 · 만든 직후 굳힘)을 따른다.</para></summary>
internal static class PureWithWallsBuilder
{
    internal const string Name = "정지순수옹벽_DH";
    internal const string TempName = "정지순수옹벽_짓는중_DH";
    const string StampKey = "DH_PUREWALLS";
    /// <summary>선형 확장사전 — 종단이 쓴 «정지면» 표면 핸들 · 면 도장 · 반영 상태 · 까닭(계획 2-4).</summary>
    internal const string AlignKey = "DH_PROFSURF";

    internal enum Kind { NoWalls = 0, WallFace = 1, NotReflected = 2 }

    internal sealed class ReflWall
    {
        public int No; public bool Up; public bool Frozen;
        public System.Collections.Generic.List<Point3> Poly = new();
        public double SlopeW = -1, FaceRun = -1, H;
        /// <summary>★[v103.3 · 계획 v103.3 2-2] 옹벽 터파기가 앞면 선을 다시 짓는 재료 — 점마다 옹벽인가 · 소단 · 머리 높이(굳은 옹벽 판 4 칸은 빈 표 — 터파기에서 빠지고 ⚠).</summary>
        public System.Collections.Generic.List<bool> IsWall = new();
        public double T, HeadZ;
        /// <summary>그 옹벽 앞면의 실제 구배 = max(SlopeW, FaceRun ÷ 단높이) — 모르면 문턱 1:0.05(계획 2-5 · 확인 낮음).</summary>
        public double FaceSlope => System.Math.Max(System.Math.Max(SlopeW, H > 1e-9 && FaceRun > 0 ? FaceRun / H : 0), 0.05);
    }

    internal sealed class Result
    {
        public Kind Kind;
        /// <summary>종단 · 횡단이 «정지면»으로 쓸 표면(옹벽면 · 정지순수_DH · 정지면_DH) — Null이면 부른 쪽이 종전대로 고른다.</summary>
        public ObjectId SurfaceId = ObjectId.Null;
        public string SurfaceName = "";
        /// <summary>면 도장 — 그린 선형에 적고, 횡단 · 검토선 · 알림이 지금 면과 견준다.</summary>
        public string Stamp = "";
        public string Why = "";
        public bool Reused;
        public int[] Missing = System.Array.Empty<int>();
        /// <summary>측점 가리기 · 걷기 대상 — 최종면에 든 옹벽(합성 표지 ∩ 유효한 뚜껑) + 순수면에 반영된 굳은 옹벽.</summary>
        public System.Collections.Generic.List<ReflWall> Walls = new();
        /// <summary>앞 구역 옹벽 가운데 종단 · 횡단에 없는 것(⚠ — 지금 구역 반영과 따로 · 계획 2-7).</summary>
        public string FrozenNote = "";
        /// <summary>★[코드 검토 중간 2 · 3] 반영은 됐어도 알릴 것 — 빠진 옹벽(관문 «예») · 짓다 나온 ⚠(뚜껑 어긋남 · 걸침 · 굳힘 실패). 있으면 자가점검 ⚠.</summary>
        public bool HasWarn => Kind == Kind.NotReflected || Missing.Length > 0 || FrozenNote.Length > 0 || (Kind == Kind.WallFace && Why.Length > 0);
        public string StatusText => Kind switch
        {
            Kind.WallFace => $"옹벽 {Walls.Count}개가 든 면({SurfaceName}{(Reused ? " · 되씀" : "")})"
                             + (Why.Length > 0 ? " · ⚠" + Why : "") + (Missing.Length > 0 ? $" · ⚠빠진 옹벽 {string.Join(",", Missing)}(정지면_DH에 없음)" : ""),
            Kind.NoWalls => Walls.Count > 0 ? $"옹벽 없음(굳은 옹벽 {Walls.Count}개는 순수면에 있음)" : "옹벽 없음",
            _ => "⚠옹벽이 반영 안 된 면 — " + Why,
        } + (FrozenNote.Length > 0 ? " · " + FrozenNote : "");
    }

    /// <summary>지금 면 도장 — <c>정지면_DH</c> 핸들 · 합성 표지의 일회용 번호(없으면 빈 칸). 정지면_DH가 하나가 아니면 빈 문자열(계획 2-6).</summary>
    internal static string CurrentStamp(Database db, ObjectId groundId)
    {
        try
        {
            var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            using var tr = db.TransactionManager.StartTransaction();
            var (ids, _) = Commands.WallCompositeCommand.Candidates(civ, tr, Commands.WallCompositeCommand.PlanName, groundId);
            string s = "";
            if (ids.Count == 1)
            {
                var plan = tr.GetObject(ids[0], OpenMode.ForRead);
                s = ids[0].Handle + "|" + Commands.WallCompositeCommand.CompositeNonce(tr, plan);
            }
            tr.Commit();
            return s;
        }
        catch { return ""; }
    }

    /// <summary>면 도장의 앞 두 칸(정지면_DH · 합성 번호) — 알림 · 횡단이 «정지면이 바뀌었나»를 이것으로 본다(순수면 핸들은 정지면 생성마다 같이 바뀐다).</summary>
    internal static string StampHead(string stamp)
    {
        if (string.IsNullOrEmpty(stamp)) return "";
        var p = stamp.Split('|');
        return p.Length >= 2 ? p[0] + "|" + p[1] : stamp;
    }

    internal static System.Collections.Generic.List<Tri> ReadTris(TinSurface s, out int lost)
    {
        var res = new System.Collections.Generic.List<Tri>();
        lost = 0;
        using var tc = s.GetTriangles(false);
        foreach (TinSurfaceTriangle t in tc)
        {
            try
            {
                var a = t.Vertex1.Location; var b = t.Vertex2.Location; var c = t.Vertex3.Location;
                res.Add(new Tri(new Point3(a.X, a.Y, a.Z), new Point3(b.X, b.Y, b.Z), new Point3(c.X, c.Y, c.Z)));
            }
            catch { lost++; }
            finally { t.Dispose(); }
        }
        return res;
    }

    static string ReadStamp(Transaction tr, DBObject o)
    {
        try
        {
            if (o.ExtensionDictionary.IsNull) return "";
            if (tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary d || !d.Contains(StampKey)) return "";
            if (tr.GetObject(d.GetAt(StampKey), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return "";
            var a = xr.Data.AsArray();
            return a.Length >= 1 && a[0].Value is string s ? s : "";
        }
        catch { return ""; }
    }
    /// <summary>★[코드 검토 중간 3] 지을 때 나온 ⚠ — 되쓸 때도 같은 ⚠를 다시 보인다(셋째 값 · 옛 표지에는 없음 = 빈 값).</summary>
    static string ReadWarn(Transaction tr, DBObject o)
    {
        try
        {
            if (o.ExtensionDictionary.IsNull) return "";
            if (tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary d || !d.Contains(StampKey)) return "";
            if (tr.GetObject(d.GetAt(StampKey), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return "";
            var a = xr.Data.AsArray();
            return a.Length >= 3 && a[2].Value is string s ? s : "";
        }
        catch { return ""; }
    }
    static void WriteStamp(Transaction tr, DBObject o, string stamp, string info, string warn = "")
    {
        if (!o.IsWriteEnabled) o.UpgradeOpen();
        if (o.ExtensionDictionary.IsNull) o.CreateExtensionDictionary();
        var d = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForWrite);
        var xr = new Xrecord { Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, stamp), new TypedValue((int)DxfCode.Text, info), new TypedValue((int)DxfCode.Text, warn ?? "")) };
        if (d.Contains(StampKey)) { var old = tr.GetObject(d.GetAt(StampKey), OpenMode.ForWrite); old.Erase(); }
        d.SetAt(StampKey, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }

    /// <summary>옹벽면 후보(이름 = 본 이름 또는 본 이름_N).</summary>
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
                if (nm == Name || (nm.StartsWith(Name + "_") && int.TryParse(nm.Substring(Name.Length + 1), out _))) res.Add((sid, nm, ReadStamp(tr, s)));
            }
            catch { }
        }
        return res;
    }

    /// <summary>선형들이 «쓴 면»으로 적은 표면 핸들 — 낡은 옹벽면은 이것에 없을 때만 지운다(재검토 중간 7).</summary>
    internal static System.Collections.Generic.HashSet<string> ReferencedHandles(Transaction tr)
    {
        var set = new System.Collections.Generic.HashSet<string>();
        try
        {
            var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            foreach (ObjectId aid in civ.GetAlignmentIds())
            {
                try
                {
                    var al = tr.GetObject(aid, OpenMode.ForRead);
                    var r = ReadAlign(tr, al);
                    if (r != null && r.Value.Handle.Length > 0) set.Add(r.Value.Handle);
                }
                catch { }
            }
        }
        catch { }
        return set;
    }

    /// <summary>★[v103.2 · 화면 없는 Civil 시험 ⑧] 선형에 «쓴 면»을 적은 <b>뒤</b> — 아무 선형도 안 쓰는 옛 옹벽면을 지우고, 본 이름이 비면 지금 면에 돌려준다.
    /// 짓는 순간(<see cref="Ensure"/>)에는 다시 그릴 옛 선형이 옛 면을 물고 있어 못 지운다(옛 면이 본 이름을 쥐고 새 면은 «_2») — 종단이 옛 선형을 지운 뒤 여기서 치운다.</summary>
    /// <param name="keep">이번 종단이 쓴 «정지면» 표면(옹벽면이 아니어도 된다 — 그때는 옛 옹벽면만 치운다).</param>
    internal static string Tidy(Transaction tr, Database db, ObjectId keep)
    {
        var refd = ReferencedHandles(tr);
        int erased = 0;
        var names = new System.Collections.Generic.List<string>();   // ★[코드 검토 낮음 6] 손으로 그 면에서 뜬 종단은 기록이 없어 못 센다 — 지운 이름을 남긴다
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
            {
                s.UpgradeOpen();
                renamed = s.Name + " → " + Name;
                s.Name = Name;
            }
        }
        catch { renamed = ""; }
        return erased == 0 && renamed.Length == 0 ? "" : $"옹벽면 정리 — 아무 선형도 안 쓰는 옛 옹벽면 {erased}개 지움{(names.Count > 0 ? "(" + string.Join(",", names) + ")" : "")}{(renamed.Length > 0 ? " · 이름 " + renamed : "")}";
    }

    /// <summary>선형에 «쓴 면»을 적는다 — 표면 핸들 · 면 도장 · 반영 상태 · 까닭 · 빠진 옹벽(계획 2-4 · 4판 검토 높음).</summary>
    internal static void RecordOnAlignment(Transaction tr, ObjectId alignId, Result r, ObjectId usedPadId)
    {
        var al = tr.GetObject(alignId, OpenMode.ForWrite);
        if (al.ExtensionDictionary.IsNull) al.CreateExtensionDictionary();
        var d = (DBDictionary)tr.GetObject(al.ExtensionDictionary, OpenMode.ForWrite);
        var xr = new Xrecord
        {
            Data = new ResultBuffer(
                new TypedValue((int)DxfCode.Text, usedPadId.IsNull ? "" : usedPadId.Handle.ToString()),
                new TypedValue((int)DxfCode.Text, r.Stamp ?? ""),
                new TypedValue((int)DxfCode.Int32, (int)r.Kind),
                new TypedValue((int)DxfCode.Text, r.Why ?? ""),
                new TypedValue((int)DxfCode.Text, string.Join(",", r.Missing)))
        };
        if (d.Contains(AlignKey)) { var old = tr.GetObject(d.GetAt(AlignKey), OpenMode.ForWrite); old.Erase(); }
        d.SetAt(AlignKey, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }

    static readonly System.Collections.Generic.HashSet<string> _notified = new();
    /// <summary>화면 없는 Civil 시험 칸 — «이어서»에서 옹벽면을 못 지었을 때 묻는 창 대신 이 답(없으면 «멈춤»).</summary>
    internal static bool? AnswerForTest;

    /// <summary>★[v103.2 · 계획 2-7 ①] «이어서» 들머리 — 옹벽면을 못 지었으면 묻는다: 앞 구역 옹벽 N개가 종단 · 횡단에 안 들어간다 · [반영 없이 이어서 / 멈춤(기본)].
    /// 굳을 옹벽(넣음 − 관문 «예»)이 없으면 묻지 않고 계속. 참 = 계속.</summary>
    internal static bool AskAppend(Autodesk.AutoCAD.ApplicationServices.Document doc, Result r)
    {
        var db = doc.Database;
        System.Collections.Generic.List<int> nos = new();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (WallListStore.TryLoad(db, tr, out _, out var ws, out _))
            {
                var yes = CompositeGate.PeekYes(doc.Name);
                nos = ws.Where(w => w.State == 1 && !yes.Contains(w.No)).Select(w => w.No).OrderBy(n => n).ToList();
            }
            tr.Commit();
        }
        catch { }
        if (nos.Count == 0) return true;
        string msg = $"앞 구역 옹벽 {nos.Count}개({string.Join(",", nos)})가 종단 · 횡단에 반영되지 않습니다.\n\n{r.Why}\n\n"
                   + "새 구역을 이어서 만들면 이 옹벽은 정지면_DH이전에 굳어 다시 반영할 길이 없습니다.\n\n"
                   + "예: 반영 없이 이어서 만듭니다\n아니오: 멈춥니다(아무것도 안 바뀝니다)";
        bool go;
        if (SelfCheckLog.Headless) go = AnswerForTest ?? false;
        else
        {
            var ans = System.Windows.MessageBox.Show(msg, "DH 정지 — 이어서", System.Windows.MessageBoxButton.YesNo,
                                                     System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
            go = ans == System.Windows.MessageBoxResult.Yes;
        }
        try { DiagLog.AppendCarry($"\n■ 이어서 — 옹벽면을 못 지어 물었다: 옹벽 {string.Join(",", nos)} · {r.Why} → {(go ? "예 — 반영 없이 이어서" : "아니오 — 멈춤")}\n"); } catch { }
        try { doc.Editor.WriteMessage(go ? $"\n[DHGRADE] ⚠옹벽 {string.Join(",", nos)}을 종단 · 횡단에 반영하지 않고 이어서 만듭니다."
                                         : "\n[DHGRADE] 멈췄습니다 — 아무것도 안 바꿨습니다."); } catch { }
        return go;
    }

    /// <summary>★[v103.2 · 계획 2-6] 정지면 생성 · 옹벽 합성의 출구 — 그려 둔 선형(<c>DH선형…</c>)이 적은 면 도장이 지금과 다르면 명령줄 한 줄.
    /// 알림창은 없고, 같은 도면 · 같은 면 도장으로는 세션에 한 번만. 적힌 것이 없는 선형은 «옛 면 기준일 수 있습니다».</summary>
    internal static void NotifyStale(Autodesk.AutoCAD.ApplicationServices.Document doc)
    {
        try
        {
            var db = doc.Database;
            var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            ObjectId g = Commands.SectionCommand.FindSurfaces(db, civ).Find(sp => sp.Label == "원지반").SurfId;
            string now = StampHead(CurrentStamp(db, g));
            int stale = 0, unknown = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId aid in civ.GetAlignmentIds())
                {
                    try
                    {
                        if (tr.GetObject(aid, OpenMode.ForRead) is not Alignment al || !(al.Name ?? "").StartsWith(Commands.SectionCommand.AlignBase, System.StringComparison.Ordinal)) continue;
                        var r = ReadAlign(tr, al);
                        if (r == null) unknown++;
                        else if (StampHead(r.Value.Stamp) != now) stale++;
                    }
                    catch { }
                }
                tr.Commit();
            }
            if (stale + unknown == 0 || !_notified.Add(doc.Name + "|" + now)) return;
            string msg = stale > 0
                ? $"그려 둔 종단 · 횡단(선형 {stale}개)은 옛 면 기준입니다" + (unknown > 0 ? $"(그 밖에 {unknown}개는 옛 면 기준일 수 있습니다)" : "") + " — 종단 버튼을 다시 누르면 지금 면으로 그립니다."
                : $"그려 둔 종단 · 횡단(선형 {unknown}개)은 옛 면 기준일 수 있습니다 — 종단 버튼을 다시 누르면 지금 면으로 그립니다.";
            try { doc.Editor.WriteMessage("\n[종단 · 횡단] " + msg); } catch { }
            try { DiagLog.Append("\n■ 옛 면 기준 알림 — " + msg); } catch { }
        }
        catch { }
    }

    /// <summary>선형에 적힌 «쓴 면» — 없으면 null(옛 도면 · 사용자가 만든 선형).</summary>
    internal static (string Handle, string Stamp, Kind Kind, string Why, string Missing)? ReadAlign(Transaction tr, DBObject al)
    {
        try
        {
            if (al.ExtensionDictionary.IsNull) return null;
            if (tr.GetObject(al.ExtensionDictionary, OpenMode.ForRead) is not DBDictionary d || !d.Contains(AlignKey)) return null;
            if (tr.GetObject(d.GetAt(AlignKey), OpenMode.ForRead) is not Xrecord xr || xr.Data == null) return null;
            var a = xr.Data.AsArray();
            if (a.Length < 4) return null;
            return (a[0].Value as string ?? "", a[1].Value as string ?? "", a[2].Value is int k ? (Kind)k : Kind.NotReflected, a[3].Value as string ?? "",
                    a.Length >= 5 ? a[4].Value as string ?? "" : "");
        }
        catch { return null; }
    }

    /// <summary>지금 최종면에 든 옹벽(합성 표지 ∩ 유효한 뚜껑 — 4판 검토 중간 2) + 순수면에 반영된 굳은 옹벽 · 지금 면 도장 앞 두 칸.
    /// 짓지 않고 읽기만 한다 — 측점 명령 · 막대 수집(종단을 다시 안 그리는 길)이 쓴다. 표지 번호 가운데 유효한 뚜껑이 없는 것이 있으면 그 옹벽은 빠진다(⚠는 종단이 이미 적었다).</summary>
    internal static System.Collections.Generic.List<ReflWall> CurrentWalls(Database db, Transaction tr, ObjectId groundId, out string stampHead)
    {
        var res = new System.Collections.Generic.List<ReflWall>();
        stampHead = "";
        try
        {
            var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
            if (WallListStore.TryLoad(db, tr, out var head, out var walls, out _))
            {
                foreach (var f in head.Frozen.Where(f => f.Reflected))
                    res.Add(new ReflWall { No = f.No, Up = f.Up, Frozen = true, Poly = f.Poly, SlopeW = f.SlopeW, FaceRun = f.FaceRun, H = f.H, IsWall = f.IsWall, T = f.T, HeadZ = f.HeadZ });
                var (ids, _) = Commands.WallCompositeCommand.Candidates(civ, tr, Commands.WallCompositeCommand.PlanName, groundId);
                if (ids.Count == 1)
                {
                    var plan = tr.GetObject(ids[0], OpenMode.ForRead);
                    string nonce = Commands.WallCompositeCommand.CompositeNonce(tr, plan);
                    stampHead = ids[0].Handle + "|" + nonce;
                    var nos = Commands.WallCompositeCommand.IsComposite(tr, plan) ? Commands.WallCompositeCommand.CompositeWallNos(tr, plan) : null;
                    if (nos != null && nonce.Length > 0)
                        foreach (int no in nos)
                        {
                            var w = walls.FirstOrDefault(x => x.No == no);
                            if (w == null || w.CompNonce != nonce || w.Cap == null) continue;
                            res.Add(new ReflWall { No = w.No, Up = w.Up, Poly = w.Poly, SlopeW = w.SlopeW, FaceRun = w.FaceRun, H = w.H, IsWall = w.IsWall, T = w.T, HeadZ = w.HeadZ });
                        }
                }
            }
        }
        catch { }
        return res;
    }

    /// <summary>★계획 2-3 — 옹벽면을 짓거나 되쓴다. 짓는 데 실패해도 예외를 안 던진다(종단은 <c>정지순수_DH</c>로 그리되 ⚠ — 6).</summary>
    /// <param name="groundId">원지반(후보에서 뺀다).</param>
    internal static Result Ensure(Database db, ObjectId groundId, StringBuilder log)
    {
        // ★[v103.2.1 · JACK 1021 스샷 «종단 눌렀을 때 저런 정보창»] 계획부지 생성 · 옹벽 합성처럼 이벤트 뷰어 <b>자동 알림만</b> 끈다(기록은 남는다 · EventViewerMute) —
        //   옹벽면 가져오기(LandXML)가 남기는 정보 한 줄(«V1.1 이전 알고리즘 … 인접이 없습니다»)에 창이 떴다. 종단 · 횡단이 부르는 길이라 DoGrade · DHWALLCOMP의 끄기 밖이었다
        var evPrev = EventViewerMute.Begin();
        try { return EnsureCore(db, groundId, log); }
        finally { EventViewerMute.End(evPrev); }
    }

    private static Result EnsureCore(Database db, ObjectId groundId, StringBuilder log)
    {
        var R = new Result();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        ObjectId made = ObjectId.Null;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            // 0. 지난번에 남은 임시 면
            int temp = 0;
            foreach (ObjectId sid in civ.GetSurfaceIds())
                try { if (tr.GetObject(sid, OpenMode.ForRead) is Surface s && s.Name == TempName) { tr.GetObject(sid, OpenMode.ForWrite).Erase(); temp++; } } catch { }
            if (temp > 0) log.AppendLine($"  옹벽면 — 지난번 임시 면 {temp}개 지움");
            // 1. 정지면_DH · 순수면
            var (planIds, planNms) = Commands.WallCompositeCommand.Candidates(civ, tr, Commands.WallCompositeCommand.PlanName, groundId);
            var (pureIds, _) = Commands.WallCompositeCommand.Candidates(civ, tr, Commands.SectionCommand.PurePadSurfaceBase, groundId);
            var (prevIds, _) = Commands.WallCompositeCommand.Candidates(civ, tr, Commands.SectionCommand.PurePadSurfaceBase + "이전", groundId);
            ObjectId pureId = pureIds.Count > 0 ? pureIds[0] : ObjectId.Null;
            R.SurfaceId = pureId;
            // 목록 · 굳은 옹벽
            bool listOk = WallListStore.TryLoad(db, tr, out var head, out var walls, out string lwhy);
            if (listOk)
            {
                foreach (var f in head.Frozen.Where(f => f.Reflected))
                    R.Walls.Add(new ReflWall { No = f.No, Up = f.Up, Frozen = true, Poly = f.Poly, SlopeW = f.SlopeW, FaceRun = f.FaceRun, H = f.H, IsWall = f.IsWall, T = f.T, HeadZ = f.HeadZ });
                // ★[계획 2-7] 앞 구역 옹벽 가운데 종단 · 횡단에 없는 것 — «반영 없이 이어서»로 굳은 칸 · 목록에도 굳은 칸에도 없는 옹벽 면(옛 판에서 «이어서»로 굳은 것 등)
                var lost = head.Frozen.Where(f => !f.Reflected).Select(f => f.No).OrderBy(n => n).ToList();
                var known = new System.Collections.Generic.HashSet<int>(walls.Select(w => w.No).Concat(head.Frozen.Select(f => f.No)));
                var orphan = new System.Collections.Generic.SortedSet<int>();
                foreach (ObjectId sid in civ.GetSurfaceIds())
                    try { if (tr.GetObject(sid, OpenMode.ForRead) is Surface s && WallTags.IsWallSurface(s.Name, out int no) && !known.Contains(no)) orphan.Add(no); } catch { }
                if (lost.Count > 0) R.FrozenNote = $"⚠앞 구역 옹벽 {string.Join(",", lost)}은 «반영 없이 이어서»로 굳어 종단 · 횡단에 없습니다";
                if (orphan.Count > 0) R.FrozenNote += (R.FrozenNote.Length > 0 ? " · " : "") + $"⚠목록에 없는 옹벽 {string.Join(",", orphan)}(옛 판에서 «이어서»로 굳은 앞 구역 옹벽 등)은 종단 · 횡단에 반영되지 않습니다";
            }
            if (planIds.Count != 1)
            {
                R.Kind = planIds.Count == 0 ? Kind.NoWalls : Kind.NotReflected;
                if (planIds.Count > 1) R.Why = $"정지면_DH를 하나로 못 잡았다({string.Join(",", planNms)}) — 옹벽 반영 여부를 모른다";
                log.AppendLine($"  옹벽면 — {R.StatusText}");
                tr.Commit(); return R;
            }
            var plan = (TinSurface)tr.GetObject(planIds[0], OpenMode.ForRead);
            bool isComp = Commands.WallCompositeCommand.IsComposite(tr, plan);
            int[]? nos = isComp ? Commands.WallCompositeCommand.CompositeWallNos(tr, plan) : null;
            string nonce = isComp ? Commands.WallCompositeCommand.CompositeNonce(tr, plan) : "";
            if (listOk)
            {
                var st = WallList.CompositeState(walls, nos);
                R.Missing = st.Missing.ToArray();
            }
            if (pureId.IsNull) { R.SurfaceId = planIds[0]; R.SurfaceName = planNms[0]; }
            R.Stamp = $"{planIds[0].Handle}|{nonce}|{(pureId.IsNull ? "" : pureId.Handle.ToString())}|{(prevIds.Count > 0 ? prevIds[0].Handle.ToString() : "")}";
            if (!isComp || nos == null || nos.Length == 0)
            {
                R.Kind = R.Missing.Length > 0 ? Kind.NotReflected : Kind.NoWalls;
                if (R.Missing.Length > 0) R.Why = $"옹벽 {string.Join(",", R.Missing)}이(가) 정지면_DH에 합성 안 됐다";
                if (isComp && (nos == null || nos.Length == 0)) { R.Kind = Kind.NotReflected; R.Why = "v102 합성(번호 없는 표지) — 옹벽을 다시 합성(DHWALLCOMP)하세요"; }
                log.AppendLine($"  옹벽면 — {R.StatusText} · 정지면 {planNms[0]} · {(isComp ? "합성" : "합성 아님")}");
                tr.Commit(); return R;
            }
            // 2. 표지 번호마다 유효한 뚜껑
            if (!listOk) { R.Kind = Kind.NotReflected; R.Why = "옹벽 목록을 못 읽었다 — " + lwhy; log.AppendLine("  옹벽면 — " + R.StatusText); tr.Commit(); return R; }
            var caps = new System.Collections.Generic.List<(int No, PureWithWalls.Cap Cap)>();
            var polys = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.IReadOnlyList<Point3>>();
            var old = new System.Collections.Generic.List<int>();
            var gone = new System.Collections.Generic.List<int>();
            foreach (int no in nos)
            {
                var w = walls.FirstOrDefault(x => x.No == no);
                // ★[계획 4판 낮음 4] 목록에 없는 번호(사면으로 되돌린 옹벽 등 — 표지가 낡았다)는 «옛 판»이 아니다
                if (w == null) { gone.Add(no); continue; }
                if (string.IsNullOrEmpty(nonce) || w.CompNonce != nonce || w.Cap == null) { old.Add(no); continue; }
                caps.Add((no, w.Cap));
                polys[no] = w.Poly;
                R.Walls.Add(new ReflWall { No = w.No, Up = w.Up, Poly = w.Poly, SlopeW = w.SlopeW, FaceRun = w.FaceRun, H = w.H, IsWall = w.IsWall, T = w.T, HeadZ = w.HeadZ });
            }
            if (old.Count + gone.Count > 0)
            {
                R.Kind = Kind.NotReflected;
                R.Walls.RemoveAll(x => !x.Frozen);
                R.Why = string.Join(" · ", new[]
                {
                    old.Count > 0 ? $"옹벽 {string.Join(",", old)}은(는) 옛 판으로 합성돼 뚜껑이 없다" : "",
                    gone.Count > 0 ? $"옹벽 {string.Join(",", gone)}은(는) 목록에서 빠졌는데(사면으로 되돌림 등) 정지면_DH 합성 표지에 남아 있다" : "",
                }.Where(x => x.Length > 0)) + " — 반영하려면 다시 합성(DHWALLCOMP · 옹벽 하나에 약 20초) 뒤 종단을 다시 그리세요";
                log.AppendLine("  옹벽면 — " + R.StatusText);
                tr.Commit(); return R;
            }
            if (pureId.IsNull)
            {
                R.Kind = Kind.NotReflected; R.Walls.RemoveAll(x => !x.Frozen);
                R.Why = "정지순수_DH가 없다 — 옹벽면을 못 짓는다";
                log.AppendLine("  옹벽면 — " + R.StatusText); tr.Commit(); return R;
            }
            // 3. 같은 도장의 옹벽면이 있으면 되쓴다
            var ex = Existing(tr);
            var same = ex.FirstOrDefault(e => e.Stamp == R.Stamp && !e.Id.IsErased);
            if (!same.Id.IsNull)
            {
                R.Kind = Kind.WallFace; R.SurfaceId = same.Id; R.SurfaceName = same.Name; R.Reused = true;
                try { R.Why = ReadWarn(tr, tr.GetObject(same.Id, OpenMode.ForRead)); } catch { }
                log.AppendLine($"  옹벽면 — {R.StatusText} · 도장 {R.Stamp} · {sw.ElapsedMilliseconds}ms");
                tr.Commit(); return R;
            }
            // 4. 짓는다
            long tRead0 = sw.ElapsedMilliseconds;
            var finT = ReadTris(plan, out int l1);
            var pureT = ReadTris((TinSurface)tr.GetObject(pureId, OpenMode.ForRead), out int l2);
            System.Collections.Generic.List<Tri>? prevT = null; int l3 = 0;
            if (prevIds.Count > 0 && tr.GetObject(prevIds[0], OpenMode.ForRead) is TinSurface prevS) prevT = ReadTris(prevS, out l3);
            long tRead = sw.ElapsedMilliseconds - tRead0;
            if (l1 + l2 + l3 > 0) { R.Kind = Kind.NotReflected; R.Walls.RemoveAll(x => !x.Frozen); R.Why = $"표면 삼각형을 읽다 {l1 + l2 + l3}개를 버렸다"; log.AppendLine("  옹벽면 — " + R.StatusText); tr.Commit(); return R; }
            var sel = PureWithWalls.Select(finT, pureT, prevT, caps, polys);
            if (sel.Fail.Length > 0) { R.Kind = Kind.NotReflected; R.Walls.RemoveAll(x => !x.Frozen); R.Why = sel.Fail; log.AppendLine("  옹벽면 — " + R.StatusText); tr.Commit(); return R; }
            // 5. 자가검증
            var warn = new System.Collections.Generic.List<string>();
            foreach (var ck in sel.Caps)
            {
                if (!ck.Match) warn.Add($"옹벽 {ck.No} 뚜껑이 합성 때와 다르다(Civil 뚜껑 안 {ck.Count}/{ck.SavedCount}면 · 넓이 {ck.Area:F3}/{ck.SavedArea:F3}㎡ · 걸침 {ck.Straddle}({ck.StraddleArea:F3}㎡)) — 계획선 끝이 그만큼 움직일 수 있다");
                if (ck.KeptInPoly == 0) warn.Add($"옹벽 {ck.No} 폴리곤 안에 남긴 삼각형이 없다(순수면 붙여넣기가 반쪽?)");
            }
            if (sel.PureStraddle > 0) warn.Add($"정지순수_DH 발자국 안팎에 걸친 정지면_DH 삼각형 {sel.PureStraddle}개({sel.PureStraddleArea:F3}㎡) @{sel.PureStraddleAt}");
            var kept = CivilSafeMesh.FixOutside(sel.Kept, _ => true, out var ofx);
            // 6. LandXML → 임시 면 → 되읽기 → 굳힘
            try { string fz0 = GradingBuilder.FreezeAllXmlBacked(tr); if (fz0.Length > 0) log.AppendLine("  옹벽면 — 굳힘: " + fz0); } catch { }
            try { string td = GradingBuilder.TidyXml(db, tr); if (td.Length > 0) log.AppendLine("  옹벽면 — " + td); } catch { }
            string xml = GradingBuilder.NewXmlPath(db, "옹벽면");
            WallDaylight.WriteLandXmlTin(xml, "PUREWALLFACE", kept, 1e-7, out int np, out int nf, out int nd);
            long t0 = sw.ElapsedMilliseconds;
            ObjectId cid = TinSurface.CreateFromLandXML(db, TempName, xml, "PUREWALLFACE");
            if (cid.IsNull) throw new System.InvalidOperationException("CreateFromLandXML이 빈 ObjectId를 돌려줬다");
            made = cid;
            var cTin = (TinSurface)tr.GetObject(cid, OpenMode.ForWrite);
            long tCreate = sw.ElapsedMilliseconds - t0;
            int civPts = -1; try { civPts = cTin.GetGeneralProperties().NumberOfPoints; } catch { }
            var theirs = ReadTris(cTin, out int l4);
            var cmp = CivilSafeMesh.Compare(kept, theirs);
            bool gate = l4 == 0 && nd == 0 && theirs.Count == nf && civPts == np && cmp.Exceptions == 0 && cmp.SymArea <= 1e-8 && cmp.Hausdorff <= 1e-6 && cmp.MaxDz <= 1e-3;
            string back = $"Civil 점 {civPts}/{np} · 삼각형 {theirs.Count}/{nf} · {cmp.Summary}" + (nd > 0 ? $" · ⚠넓이 0이라 뺀 면 {nd}" : "");
            if (!gate)
            {
                try { cTin.Erase(); } catch { }
                made = ObjectId.Null;
                R.Kind = Kind.NotReflected; R.Walls.RemoveAll(x => !x.Frozen);
                R.Why = "Civil이 넘긴 옹벽면을 그대로 안 받았다 — " + back;
                log.AppendLine("  옹벽면 — " + R.StatusText);
                tr.Commit(); return R;
            }
            string frz = GradingBuilder.FreezeXmlBacked(cTin);
            // ★[코드 검토 중간 3] 굳힘 실패도 ⚠ — 굳히지 못한 XML 면은 원본 파일을 다시 읽을 수 있다
            if (frz.Length > 0) warn.Add($"옹벽면을 굳히지 못했다({frz}) — 원본 XML이 바뀌면 면이 바뀔 수 있다");
            // 낡은 옹벽면 — 그것으로 그린 선형이 하나도 안 남았으면 지운다(★[코드 검토 낮음 6] 손으로 그 면에서 뜬 종단은 기록이 없어 못 센다 — 지운 이름을 로그에)
            var refd = ReferencedHandles(tr);
            int erased = 0, keptOld = 0;
            var erasedNames = new System.Collections.Generic.List<string>();
            foreach (var e in ex)
            {
                if (e.Id.IsErased) continue;
                if (refd.Contains(e.Id.Handle.ToString())) { keptOld++; continue; }
                try { tr.GetObject(e.Id, OpenMode.ForWrite).Erase(); erased++; erasedNames.Add(e.Name); } catch { }
            }
            string nm = GradingBuilder.UniqueName(db, tr, Name);
            cTin.Name = nm;
            try { cTin.Description = $"DH 옹벽이 든 순수면(종단 · 횡단용) — {System.DateTime.Now:yyyy-MM-dd HH:mm} · {GradingSettings.Version} · 옹벽 {string.Join(",", caps.Select(c => c.No))}"; } catch { }
            WriteStamp(tr, cTin, R.Stamp, $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} · {GradingSettings.Version}", string.Join(" · ", warn));
            GradingBuilder.SetSurfaceVisible(tr, Name, false);
            tr.Commit();
            made = ObjectId.Null;
            R.Kind = Kind.WallFace; R.SurfaceId = cid; R.SurfaceName = nm;
            if (warn.Count > 0) R.Why = string.Join(" · ", warn);
            log.AppendLine($"  옹벽면 — 지었다 {nm} · 읽음 최종 {finT.Count} · 순수 {pureT.Count}{(prevT != null ? $" · 이전 {prevT.Count}" : "")} · 순수면 밖 {sel.OutsidePure} · 뚜껑으로 뺌 {sel.CapRemoved} · 앞 구역이라 남긴 뚜껑 {sel.CapKeptPrev} · 남김 {sel.Kept.Count}" +
                           $" · 옹벽별 뚜껑 {string.Join(" ", sel.Caps.Select(c => $"{c.No}:{c.Count}/{c.SavedCount}면 {c.Area:F1}㎡{(c.Match ? "" : "⚠")}{(c.BitSame ? "" : "(비트 다름)")} 폴리곤 안 남김 {c.KeptInPoly}"))}" +
                           (ofx.Flipped > 0 ? $" · Civil이 고칠 면 먼저 뒤집음 {ofx.Flipped}" : "") +
                           $" · 되읽기 {back} · {(frz.Length == 0 ? "굳힘" : "⚠굳힘 실패 " + frz)} · 낡은 옹벽면 지움 {erased}{(erasedNames.Count > 0 ? "(" + string.Join(",", erasedNames) + ")" : "")}(선형이 쓰는 것 {keptOld} 남김)" +
                           $" · {sw.ElapsedMilliseconds}ms(읽기 {tRead} · 발자국 {sel.MsFoot} · 고르기 {sel.MsSelect} · 가져오기 {tCreate}) · 도장 {R.Stamp}" +
                           (warn.Count > 0 ? " · ⚠" + string.Join(" · ", warn) : ""));
            return R;
        }
        catch (System.Exception ex)
        {
            log.AppendLine($"  옹벽면 — ⚠짓다 터졌다({ex.GetType().Name}: {ex.Message})");
            try
            {
                if (!made.IsNull)
                {
                    using var tr2 = db.TransactionManager.StartTransaction();
                    try { if (!made.IsErased) tr2.GetObject(made, OpenMode.ForWrite).Erase(); } catch { }
                    tr2.Commit();
                }
            }
            catch { }
            R.Kind = Kind.NotReflected; R.Walls.RemoveAll(x => !x.Frozen);
            R.Why = $"옹벽면을 못 지었다({ex.GetType().Name}: {ex.Message})";
            if (R.SurfaceId.IsNull)
            {
                try
                {
                    using var tr3 = db.TransactionManager.StartTransaction();
                    var (pi, _) = Commands.WallCompositeCommand.Candidates(civ, tr3, Commands.SectionCommand.PurePadSurfaceBase, groundId);
                    if (pi.Count > 0) R.SurfaceId = pi[0];
                    tr3.Commit();
                }
                catch { }
            }
            return R;
        }
    }
}
