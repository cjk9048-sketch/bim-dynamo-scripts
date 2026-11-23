using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★[v112.0 · 계획_도면화.md 1.5 · 4.9] <b>토공량 셈 — 면 찾기 + 두 면 세모로 직접 셈</b>(읽기만 · 임시 체적면을 안 만들어 도면을 안 바꾼다 — 1027 메모).
/// <para>[3D 보기](<c>InfraworksCommand.WriteVolumeCsv</c>)와 도면화 토공량표가 같이 쓴다 — 셈이 두 벌이면 한쪽만 고쳐진다(동작은 옛 그대로 옮김).</para></summary>
internal static class EarthworkVolume
{
    /// <summary>이름으로 면 둘(원지반 · 계획) — 같은 이름이 여럿이면 마지막(옛 <c>WriteVolumeCsv</c>와 같음) · 원지반 이름이 비면 못 찾음.</summary>
    internal static (ObjectId Ground, ObjectId Design) Find(Database db, string groundName, string designName)
    {
        ObjectId groundId = ObjectId.Null, designId = ObjectId.Null;
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            var civilDoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            foreach (ObjectId sid in civilDoc.GetSurfaceIds())
            {
                if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.TinSurface ts) continue;
                if (!string.IsNullOrEmpty(groundName) && ts.Name == groundName) groundId = sid;
                if (ts.Name == designName) designId = sid;
            }
            tr.Commit();
        }
        return (groundId, designId);
    }

    /// <summary>면의 보이는 세모(도면 좌표).</summary>
    internal static List<(Point3 A, Point3 B, Point3 C)> Tris(Database db, ObjectId surfId)
    {
        using Transaction tr = db.TransactionManager.StartTransaction();
        var tin = (Autodesk.Civil.DatabaseServices.TinSurface)tr.GetObject(surfId, OpenMode.ForRead);
        var l = new CachedGroundSurface(tin).AllTriangles().Select(t => (t.A, t.B, t.C)).ToList();
        tr.Commit();
        return l;
    }

    /// <summary>비교(계획) − 기준(원지반) 절토 · 성토 — <paramref name="mask"/>가 있으면 그 안(<paramref name="inside"/>)/밖만(Core <see cref="TinVolume"/>).
    /// <para>★[1123 속도 3번] 도면마다 결과 메모 — 열쇠 = 두 면 <b>세모 전부</b>(차례 · 좌표 비트) + 마스크 + 안/밖의 SHA-256. 입력이 비트까지 같을 때만 지난 값(값 형식이라 부르는 쪽이 못 고침).
    /// 면을 다시 지으면(HLG · 사면 고침 · 진입도로) 세모가 바뀌어 새로 셈 — 세모 읽기는 늘 함.</para></summary>
    internal static TinVolume.Result Compute(Database db, ObjectId designId, ObjectId groundId,
                                             NetTopologySuite.Geometries.Geometry? mask = null, bool inside = true)
    {
        var dt = Tris(db, designId); var gt = Tris(db, groundId);
        string key = "";
        try { key = Key(dt, gt, mask, inside); } catch { key = ""; }
        var memo = key.Length > 0 ? Memos.GetOrCreateValue(db) : null;
        if (memo != null)
            lock (memo)
                if (memo.TryGetValue(key, out var hit)) { Diag($"  토공량 메모 맞음 — 두 면 세모 그대로 · 셈 건너뜀(절 {hit.Cut:F1} · 성 {hit.Fill:F1})"); return hit; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = TinVolume.Compute(dt, gt, mask, inside);
        if (memo != null)
            lock (memo) { if (memo.Count >= 8) memo.Clear(); memo[key] = r; }
        Diag($"  토공량 셈 {sw.ElapsedMilliseconds} ms(메모 새로)");
        return r;
    }

    /// <summary>★[1123 속도 2번] 도면화 «견줌»(정지면_DH − 원지반 · 진단 로그 한 줄) — <b>셈은 안 함</b>. 3번 메모에 같은 입력의 값이 있을 때만 돌려주고 없으면 null.
    /// 메모가 비었으면 세모도 안 읽음.</summary>
    internal static TinVolume.Result? Memoized(Database db, ObjectId designId, List<(Point3 A, Point3 B, Point3 C)> groundTris)
    {
        if (!Memos.TryGetValue(db, out var memo)) return null;
        lock (memo) if (memo.Count == 0) return null;
        string key = Key(Tris(db, designId), groundTris, null, true);
        lock (memo) return memo.TryGetValue(key, out var r) ? r : null;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Database, Dictionary<string, TinVolume.Result>> Memos = new();

    static void Diag(string s) { try { DiagLog.Append(s + "\n"); } catch { } }

    /// <summary>열쇠 — 세모 수 · 세모마다 꼭짓점 9값의 비트(차례대로) · 마스크(WKB · 모든 좌표 XYZ 비트 · 정밀도 · SRID) · 안/밖.</summary>
    static string Key(List<(Point3 A, Point3 B, Point3 C)> dt, List<(Point3 A, Point3 B, Point3 C)> gt, NetTopologySuite.Geometries.Geometry? mask, bool inside)
    {
        using var h = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var buf = new byte[72 * 1024];
        int o = 0;
        void N(long v)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(o, 8), v); o += 8;
            if (o == buf.Length) { h.AppendData(buf, 0, o); o = 0; }
        }
        void W(double v) => N(BitConverter.DoubleToInt64Bits(v));
        void L(List<(Point3 A, Point3 B, Point3 C)> l)
        {
            N(l.Count);
            foreach (var (a, b, c) in l) { W(a.X); W(a.Y); W(a.Z); W(b.X); W(b.Y); W(b.Z); W(c.X); W(c.Y); W(c.Z); }
        }
        L(dt); L(gt);
        N(inside ? 1 : 0);
        N(mask == null ? 0 : 1);
        if (mask != null)
        {
            foreach (var c in mask.Coordinates) { W(c.X); W(c.Y); W(c.Z); }
            h.AppendData(buf, 0, o); o = 0;
            h.AppendData(mask.AsBinary());
            h.AppendData(System.Text.Encoding.UTF8.GetBytes($"|{mask.GeometryType}|{mask.SRID}|{mask.PrecisionModel}|{mask.Factory.PrecisionModel}|{mask.Factory.SRID}"));
        }
        if (o > 0) h.AppendData(buf, 0, o);
        return Convert.ToHexString(h.GetHashAndReset());
    }
}
