using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>★★★[v107 · 계획_v107.md 2 · 3] <b>[3D 보기] 옹벽 층</b> — 단면 줄마다 몸통(재료별 한 메시) · 무늬(레이어별 한 메시) · 정거장 표(정보 풍선) · 발자국(지형 «옹벽 면» 숨김).
/// <para>옹벽3D.dwg(<c>WallSolidDwg.Export</c>)와 같은 차례 · 같은 기본값(전단 · 꼭대기 올림 · 무늬 켬): <see cref="WallSweep.Make"/> → 덩어리 <see cref="WallMesh.Loft"/> · 모서리 <see cref="WallMesh.Corner"/> ·
/// 무늬 <see cref="WallFacePattern.Lay"/> → <see cref="WallMesh.Piece"/>. 점은 원점 뺀 float.</para></summary>
public static class WallScene
{
    public sealed class Part { public string Mat = ""; public float[] Pos = Array.Empty<float>(); public int[] Idx = Array.Empty<int>(); }
    public sealed class Pat { public int Wall; public string Layer = ""; public (byte R, byte G, byte B) Rgb; public float[] Pos = Array.Empty<float>(); public int[] Idx = Array.Empty<int>(); }

    public sealed class Wall
    {
        public int Id; public string Name = "", Kind = "", Src = ""; public bool Up;
        /// <summary>정거장 표 — 점마다 (x, y, 측점 S, 높이 H, 표 줄) · 원점 뺀 값.</summary>
        public float[] Sta = Array.Empty<float>();
        /// <summary>평면 범위(원점 뺀 minx, miny, maxx, maxy).</summary>
        public double[] Box = new double[4];
        public double Len, HMin, HMax;
        public readonly List<Part> Parts = new();
    }

    public sealed class Result
    {
        public readonly List<Wall> Walls = new();
        public readonly List<Pat> Pats = new();
        /// <summary>3D 옹벽 발자국(도면 좌표 · 단면 줄 + 단면 폭) — 지형 «옹벽 면»을 숨겨도 되는 자리.</summary>
        public Geometry? Foot;
        public int Lines, Runs, Corners, Fails, CornerFails, Pieces, PieceFails, PatLineFails, FanFallback, OpenEdges, NegVolume;
        public long Tris;
        public string FirstFail = "";
        public readonly Dictionary<string, double> VolByMat = new();
        public readonly Dictionary<string, int> PartsByMat = new();
        public int PatItems, PatWhole, PatCut, PatSplit, PatDropped, PatLow, PatAnchorSkipped, PatClipFail;
        public long MsBody, MsPattern;

        public string Text => $"단면 줄 {Lines} · 덩어리 {Runs} · 모서리 {Corners} · 세모 {Tris:N0}" + (Fails + CornerFails > 0 ? $" · ⚠못 지음 {Fails + CornerFails} — {FirstFail}" : "")
                              + $" · 무늬 조각 {Pieces:N0}(깐 {PatItems} · 잘림 {PatCut} · 앵커 뺌 {PatAnchorSkipped})" + (PieceFails + PatLineFails + PatClipFail > 0 ? $" · ⚠무늬 못 지음 {PieceFails + PatClipFail} · 줄 {PatLineFails}" : "")
                              + (FanFallback > 0 ? $" · 부채꼴 면 {FanFallback}" : "") + (OpenEdges > 0 ? $" · ⚠열린 변 {OpenEdges}" : "") + (NegVolume > 0 ? $" · ⚠뒤집힌 덩어리 {NegVolume}" : "")
                              + $" · 몸통 {MsBody / 1000.0:F1}초 · 무늬 {MsPattern / 1000.0:F1}초";
    }

    sealed class Acc
    {
        public readonly List<float> P = new(); public readonly List<int> I = new();
        public void Add(WallMesh.Mesh m, double ox, double oy)
        {
            int b = P.Count / 3;
            foreach (var v in m.V) { P.Add((float)(v.X - ox)); P.Add((float)(v.Y - oy)); P.Add((float)v.Z); }
            foreach (int i in m.T) I.Add(b + i);
        }
    }

    public static Result Build(IReadOnlyList<WallExcav.SecLine> lines, double ox, double oy, bool pattern = true, bool checkClosed = false)
    {
        var r = new Result();
        var gf = NtsSupport.Factory();
        var foots = new List<Geometry>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int id = 0;
        foreach (var ln in lines)
        {
            if (ln?.Pts == null || ln.Pts.Count < 2) continue;
            r.Lines++;
            var w = new Wall { Id = ++id, Kind = WallSection.Of(ln.Kind).Name, Up = ln.Up, Src = ln.Src };
            w.Name = ln.Src.Length > 0 ? ln.Src : $"옹벽 {w.Id}";
            // 정거장 표 · 범위 · 길이 · 높이
            var sta = new List<float>(ln.Pts.Count * 5);
            double mnx = double.MaxValue, mny = double.MaxValue, mxx = double.MinValue, mxy = double.MinValue, reach = 0.5;
            w.HMin = double.MaxValue; w.HMax = double.MinValue;
            foreach (var p in ln.Pts)
            {
                sta.Add((float)(p.X - ox)); sta.Add((float)(p.Y - oy)); sta.Add((float)p.S); sta.Add((float)p.H); sta.Add(p.Row);
                mnx = Math.Min(mnx, p.X); mny = Math.Min(mny, p.Y); mxx = Math.Max(mxx, p.X); mxy = Math.Max(mxy, p.Y);
                w.HMin = Math.Min(w.HMin, p.H); w.HMax = Math.Max(w.HMax, p.H);
                foreach (var part in WallSweep.Solids(p.Shape)) foreach (var q in part.Ring) reach = Math.Max(reach, Math.Abs(q.X) + p.Shape.FaceN * Math.Max(0, q.Z));
            }
            w.Sta = sta.ToArray();
            w.Len = ln.Pts[^1].S - ln.Pts[0].S;
            w.Box = new[] { mnx - reach - ox, mny - reach - oy, mxx + reach - ox, mxy + reach - oy };
            try
            {
                var ls = gf.CreateLineString(ln.Pts.Select(p => new Coordinate(p.X, p.Y)).ToArray());
                foots.Add(ls.Buffer(reach + 0.3));
            }
            catch { }

            // 몸통
            sw.Restart();
            var byMat = new Dictionary<string, Acc>();
            WallSweep.Plan plan;
            try { plan = WallSweep.Make(ln, true); }
            catch (Exception ex) { r.Fails++; if (r.FirstFail.Length == 0) r.FirstFail = $"{ln.Src}: {ex.Message}"; r.Walls.Add(w); continue; }
            void Put(string mat, WallMesh.Mesh m)
            {
                if (m.T.Count == 0) return;
                r.FanFallback += m.FanFallback;
                double vol = m.Volume();
                if (vol <= 0) r.NegVolume++;
                r.VolByMat[mat] = (r.VolByMat.TryGetValue(mat, out double v0) ? v0 : 0) + vol;
                r.PartsByMat[mat] = (r.PartsByMat.TryGetValue(mat, out int c0) ? c0 : 0) + 1;
                if (checkClosed) r.OpenEdges += m.OpenEdges();
                if (!byMat.TryGetValue(mat, out var acc)) byMat[mat] = acc = new Acc();
                acc.Add(m, ox, oy);
                r.Tris += m.T.Count / 3;
            }
            foreach (var run in plan.Runs)
            {
                r.Runs++;
                try
                {
                    foreach (var (mat, m) in WallMesh.Loft(run))
                    {
                        if (m == null) { r.Fails++; if (r.FirstFail.Length == 0) r.FirstFail = $"{ln.Src}: {mat} 링 꼴이 정거장마다 다름"; continue; }
                        Put(mat, m);
                    }
                }
                catch (Exception ex) { r.Fails++; if (r.FirstFail.Length == 0) r.FirstFail = $"{ln.Src}: {ex.Message}"; }
            }
            foreach (var c in plan.Corners)
            {
                r.Corners++;
                try { foreach (var (mat, m) in WallMesh.Corner(c)) Put(mat, m); }
                catch (Exception ex) { r.CornerFails++; if (r.FirstFail.Length == 0) r.FirstFail = $"{ln.Src} 모서리: {ex.Message}"; }
            }
            foreach (var kv in byMat) w.Parts.Add(new Part { Mat = kv.Key, Pos = kv.Value.P.ToArray(), Idx = kv.Value.I.ToArray() });
            r.MsBody += sw.ElapsedMilliseconds;
            r.Walls.Add(w);

            // 무늬
            var spec = WallFacePattern.SpecOf(ln.Kind);
            if (!pattern || spec == null) continue;
            sw.Restart();
            try
            {
                var lay = WallFacePattern.Lay(plan, spec);
                r.PatItems += lay.Items; r.PatWhole += lay.Whole; r.PatCut += lay.Cut; r.PatSplit += lay.Split; r.PatDropped += lay.Dropped;
                r.PatLow += lay.Low; r.PatAnchorSkipped += lay.AnchorSkipped; r.PatClipFail += lay.ClipFail;
                var byLay = new Dictionary<string, Acc>();
                foreach (var pc in lay.Pieces)
                {
                    try
                    {
                        var m = WallMesh.Piece(lay.Sheets[pc.Sheet], pc);
                        if (m.T.Count == 0) { r.PieceFails++; continue; }
                        r.FanFallback += m.FanFallback;
                        if (!byLay.TryGetValue(pc.Layer, out var acc)) byLay[pc.Layer] = acc = new Acc();
                        acc.Add(m, ox, oy);
                        r.Pieces++; r.Tris += m.T.Count / 3;
                    }
                    catch { r.PieceFails++; }
                }
                foreach (var kv in byLay)
                {
                    var (cr, cg, cb) = WallFacePattern.ColorOf(kv.Key);
                    r.Pats.Add(new Pat { Wall = w.Id, Layer = kv.Key, Rgb = ((byte)cr, (byte)cg, (byte)cb), Pos = kv.Value.P.ToArray(), Idx = kv.Value.I.ToArray() });
                }
            }
            catch { r.PatLineFails++; }
            r.MsPattern += sw.ElapsedMilliseconds;
        }
        if (foots.Count > 0)
        {
            try { r.Foot = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(foots); }
            catch { r.Foot = gf.BuildGeometry(foots).Buffer(0); }
        }
        return r;
    }
}
