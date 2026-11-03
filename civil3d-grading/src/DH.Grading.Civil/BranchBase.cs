using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;
using CivilDb = Autodesk.Civil.DatabaseServices;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Civil;

/// <summary>★★★[도로 마무리 ① · 계획_도로마무리.md 5.5 · 계획_v111_8.md 7.5] 가지 도로의 바탕 = «바탕면(정지면_DH) + 본 도로 면» 메모리 합침.
/// <para>부지 위에 도로를 얹듯 본 도로 위에 가지를 얹는다 — 가지 끝 마구리 · 사면이 본 도로 포장 · 사면에 닿아 최종면 이음매가 맞는다.
/// 합치기 = 최종지표면과 같은 <see cref="FinalSurface.Compose"/>(본 도로가 또 가지면 그 본 도로도 · 깊이 차례) → <see cref="CachedGroundSurface"/>(세모 그대로 —
/// 높이 · 땅 꺾임(③ <see cref="IGroundBreaks"/>) 모두 합친 면에서).</para>
/// <para>끝 맞춤을 켠 도로(<see cref="RoadDesign.EndsOn"/>)만 — 옛 도로는 바탕 그대로(말없이 안 바꿈). 본 도로 면이 없으면 그 본 도로는 빼고(알림) · 합치기에 실패하면 바탕 그대로 + ⚠ 알림.</para></summary>
internal static class BranchBase
{
    /// <summary>본 도로 하나 — 노선 핸들 · 노선 · 도로 면 · 이름 · 깊이(뿌리 = 0 · 그 위 가지 1 …).</summary>
    internal sealed record Main(long Handle, ObjectId Aid, ObjectId Surf, string Name, int Rank);

    /// <summary>끝 판정 결과 → 노선마다 깊이(본 도로가 없으면 0 · 가지 = 1 + 본 도로 깊이 중 큰 것). <paramref name="endsOn"/>이 거짓인 도로는 가지로 안 봄(옛 도로 = 바탕 그대로).
    /// 고리는 지나온 노선을 다시 만나면 끊음(같은 입력 = 같은 결과 — 핸들 차례).</summary>
    internal static Dictionary<long, int> Ranks(IReadOnlyList<RoadEnds.Result> all, Func<long, bool> endsOn)
    {
        var by = new Dictionary<long, RoadEnds.Result>();
        foreach (var r in all) by[r.Handle] = r;
        var rank = new Dictionary<long, int>();
        int Of(long h, HashSet<long> path)
        {
            if (rank.TryGetValue(h, out int k)) return k;
            int best = 0;
            if (by.TryGetValue(h, out var r) && endsOn(h) && path.Add(h))
            {
                foreach (long m in MainsOf(r).OrderBy(x => x)) if (!path.Contains(m) && by.ContainsKey(m)) best = Math.Max(best, 1 + Of(m, path));
                path.Remove(h);
            }
            rank[h] = best;
            return best;
        }
        foreach (long h in by.Keys.OrderBy(x => x)) Of(h, new HashSet<long>());
        return rank;
    }

    /// <summary>끝 하나라도 다른 도로 포장 안(가지)이면 그 본 도로 핸들들.</summary>
    static IEnumerable<long> MainsOf(RoadEnds.Result r)
    {
        foreach (var e in new[] { r.Start, r.Finish })
            if (e != null && e.Kind == RoadEnds.Kind.Branch && e.Main != 0 && e.Main != r.Handle) yield return e.Main;
    }

    /// <summary>이 노선의 본 도로 사슬(본 → 그 본 …) — 도로 면이 있는 것만 · 깊이 차례. <paramref name="note"/> = 도로 면이 없어 뺀 본 도로(빈 글 = 없음).</summary>
    internal static List<Main> Mains(Transaction tr, Database db, IReadOnlyList<RoadEnds.Result>? all, ObjectId aid, out string note)
    {
        note = "";
        var o = new List<Main>();
        if (all == null || all.Count < 2) return o;
        var by = all.ToDictionary(r => r.Handle);
        long me = aid.Handle.Value;
        if (!by.TryGetValue(me, out var mine)) return o;
        var designs = new Dictionary<long, (ObjectId Aid, RoadDesign? D)>();
        (ObjectId Aid, RoadDesign? D) Rd(long h)
        {
            if (designs.TryGetValue(h, out var v)) return v;
            v = (ObjectId.Null, null);
            try { if (db.TryGetObjectId(new Handle(h), out var id) && !id.IsErased) v = (id, RoadDesign.Read(tr, id)); } catch { }
            designs[h] = v;
            return v;
        }
        bool On(long h) => h == me || Rd(h).D?.EndsOn == true;
        var rank = Ranks(all, On);
        // 사슬 — 내 본 도로 · 그 본 도로가 끝 맞춤 도로면 그 본 도로 …(지나온 것은 다시 안 감)
        var seen = new HashSet<long> { me }; var todo = new Queue<long>(MainsOf(mine).OrderBy(x => x));
        var lost = new List<string>();
        while (todo.Count > 0)
        {
            long h = todo.Dequeue();
            if (!seen.Add(h)) continue;
            var (mid, d) = Rd(h);
            if (d == null || mid.IsNull) continue;
            string nm = "";
            try { nm = ((CivilDb.Alignment)tr.GetObject(mid, OpenMode.ForRead)).Name ?? ""; } catch { }
            ObjectId sid = ObjectId.Null;
            try
            {
                if (d.SurfaceHandle.Length > 0 && db.TryGetObjectId(new Handle(Convert.ToInt64(d.SurfaceHandle, 16)), out var s) && !s.IsErased
                    && tr.GetObject(s, OpenMode.ForRead) is CivilDb.TinSurface ts && FinalSurfaceBuilder.IsRoadSurface(ts)) sid = s;
            }
            catch { }
            if (sid.IsNull) lost.Add(nm);
            else o.Add(new Main(h, mid, sid, nm, rank.TryGetValue(h, out int k) ? k : 0));
            if (d.EndsOn && by.TryGetValue(h, out var rh)) foreach (long m in MainsOf(rh).OrderBy(x => x)) todo.Enqueue(m);
        }
        if (lost.Count > 0) note = $"⚠본 도로 «{string.Join("», «", lost)}» 지표면이 없어 가지 바탕에서 뺌 — 본 도로를 먼저 [업데이트]";
        return o.OrderBy(m => m.Rank).ThenBy(m => m.Handle).ToList();
    }

    /// <summary>합친 바탕의 열쇠 — 바탕 세모 지문 + 본 도로(노선 · 면 핸들 · 깊이). 같으면 다시 안 합침(본 도로 면은 다시 만들 때마다 새 핸들).</summary>
    internal static string Key(CachedGroundSurface plain, List<Main> mains)
        => plain.Signature.ToString("x16") + "|" + string.Join(",", mains.Select(m => $"{m.Handle:X}:{m.Surf.Handle}:{m.Rank}"));

    /// <summary>바탕 세모 + 본 도로 면들 → 합친 면(<see cref="FinalSurface.Compose"/> — 최종지표면과 같은 규칙). 실패면 null(<paramref name="note"/> = ⚠까닭).</summary>
    internal static CachedGroundSurface? Compose(Transaction tr, CachedGroundSurface plain, List<Main> mains, out string note)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var layers = new List<FinalSurface.Layer>();
        foreach (var m in mains)
        {
            var ts = (CivilDb.TinSurface)tr.GetObject(m.Surf, OpenMode.ForRead);
            var t = PureWithWallsBuilder.ReadTris(ts, out int lost);
            if (lost > 0) { note = $"⚠가지 바탕 못 합침 — 본 도로 «{m.Name}» 지표면 세모 {lost}개를 못 읽음(바탕면만으로 계산)"; return null; }
            layers.Add(new FinalSurface.Layer(t, m.Name, m.Handle, m.Rank));
        }
        List<Tri>? fin;
        FinalSurface.Report rep;
        try { fin = FinalSurface.Compose(plain.AllTriangles(), layers, null, out rep); }
        catch (Exception ex) { note = "⚠가지 바탕 못 합침 — " + ex.Message + "(바탕면만으로 계산)"; return null; }
        if (fin == null) { note = "⚠가지 바탕 못 합침 — " + rep.Fail + "(바탕면만으로 계산)"; return null; }
        var res = new CachedGroundSurface(fin);
        note = $"가지 바탕 = 바탕면 + 본 도로 «{string.Join("», «", mains.Select(m => m.Name))}» · 세모 {fin.Count:N0} · {sw.ElapsedMilliseconds} ms";
        try { DiagLog.Append("\n[진입도로 가지 바탕] " + note + "\n  " + rep.Text.Replace("\n", "\n  ")); } catch { }
        return res;
    }

    /// <summary>세션 밖(노리선 · 최종면 까닭)에서 — 그 도로 엔진이 쓰는 바탕(가지면 합친 면 · 아니면 <paramref name="plain"/> 그대로). 돌려줌 = 바탕 · <paramref name="note"/> = 알림.</summary>
    internal static IGroundSurface For(Transaction tr, Database db, ObjectId aid, RoadDesign d, CachedGroundSurface plain, List<RoadEnds.Floor>? floors, out string note)
    {
        note = "";
        if (!d.EndsOn) return plain;
        List<RoadEnds.Result>? all;
        try { all = RoadRanges.SolveAll(tr, db, aid, floors ?? PadFloors.Load(db, tr, out _), null, false); }
        catch (Exception ex) { note = "⚠끝 판정 실패 — " + ex.Message; return plain; }
        var mains = Mains(tr, db, all, aid, out string mn);
        note = mn;
        if (mains.Count == 0) return plain;
        var c = Compose(tr, plain, mains, out string cn);
        note = string.Join(" · ", new[] { mn, cn }.Where(t => t.Length > 0));
        return c ?? (IGroundSurface)plain;
    }

    /// <summary>최종지표면 층 차례(깊이 — 가지는 본 도로 뒤 · 위) — 노선 핸들 → 깊이. 도로가 둘 미만이거나 판정 실패면 빈 표(모두 0 = 핸들 차례 — 지금과 같음).</summary>
    internal static Dictionary<long, int> LayerRanks(Transaction tr, Database db)
    {
        try
        {
            var roads = RoadRanges.Roads(tr, db);
            if (roads.Count < 2) return new();
            var on = new Dictionary<long, bool>();
            foreach (var id in roads) { try { on[id.Handle.Value] = RoadDesign.Read(tr, id)?.EndsOn == true; } catch { } }
            if (!on.ContainsValue(true)) return new();
            var all = RoadRanges.SolveAll(tr, db, roads[0], PadFloors.Load(db, tr, out _), null, false);
            return all == null ? new() : Ranks(all, h => on.TryGetValue(h, out bool b) && b);
        }
        catch (Exception ex) { try { DiagLog.Append("\n[최종지표면] 가지 차례 못 셈(핸들 차례로) — " + ex.Message); } catch { } return new(); }
    }
}
