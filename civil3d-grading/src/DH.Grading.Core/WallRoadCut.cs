using System.Globalization;

namespace DH.Grading.Core;

/// <summary>★★[JACK 1124 «진입도로로 인해 옹벽부가 깎일 경우 옹벽 객체도 수정되어야 해» · 답 «도로 자리만 잘라냄»] 부지 옹벽의 진입도로 자리 몫 빼기.
/// <para>자리 = 진입도로 면 발자국(<see cref="RoadFootprint.FromTris(IReadOnlyList{WallDaylight.Tri})"/> 합 — 부지 노리선 · 사면선을 자르는 것과 같은 자리 · 최종면에서 도로가 덮는 자리).
/// 그 안 벽은 빼고 경계에서 수직으로 끊는다(새 벽 · 도로 따라 꺾기 없음). 옹벽 기록(<see cref="WallList"/>)은 그대로 — 바닥 줄(3D · 터파기 · 단면 · 수량)과 평면 옹벽선만 자른다.</para></summary>
public static class WallRoadCut
{
    /// <summary>이보다 짧은 잘린 조각은 버린다(m — 경계를 스친 부스러기).</summary>
    public const double MinPiece = 0.5;   // ★[검토 낮음 4] 5 cm → 0.5 m — 평면 옹벽선(WallRoadCutPlan)도 같은 값

    /// <summary>자른 셈 — 바닥 줄 수 · 잘린 줄 · 다 빠진 줄 · 남은 조각 · 길이(전 → 후).</summary>
    public sealed class Note
    {
        public int Lines, Cut, Dropped, Pieces, Skipped;
        public double LenBefore, LenAfter;
        public double Removed => LenBefore - LenAfter;
        public string Text => string.Format(CultureInfo.InvariantCulture,
            "진입도로 자리 옹벽 제외 — 바닥 줄 {0}개 중 {1}개 자름({2}개 전부 빠짐 · 남은 조각 {3}) · 길이 {4:F1} → {5:F1} m(−{6:F1} m){7}",
            Lines, Cut, Dropped, Pieces, LenBefore, LenAfter, Removed, Skipped > 0 ? $" · ⚠점마다 값이 어긋나 못 자른 줄 {Skipped}개(그대로)" : "");
    }

    /// <summary>바닥 줄들을 자른다 — (조각, 원래 줄). 안 잘린 줄은 같은 객체 그대로(소단 폭 표 · 지문이 안 바뀐다). <paramref name="cut"/> null이면 그대로.</summary>
    public static List<(WallExcav.Bottom Piece, WallExcav.Bottom Src)> Bottoms(IReadOnlyList<WallExcav.Bottom> bottoms, RoadFootprint.Cutter? cut, Note note)
    {
        var res = new List<(WallExcav.Bottom, WallExcav.Bottom)>(bottoms.Count);
        foreach (var b in bottoms)
        {
            note.Lines++;
            double len = Len(b.Pts);
            note.LenBefore += len;
            if (cut == null) { res.Add((b, b)); note.LenAfter += len; continue; }
            var ps = Bottom(b, cut, out bool clipped, out bool skipped);
            if (skipped) note.Skipped++;
            if (!clipped) { res.Add((b, b)); note.LenAfter += len; continue; }
            note.Cut++;
            if (ps.Count == 0) note.Dropped++;
            foreach (var p in ps) { res.Add((p, b)); note.Pieces++; note.LenAfter += Len(p.Pts); }
        }
        return res;
    }

    /// <summary>바닥 줄 하나 — 진입도로 자리 밖 조각들(경계 점은 선분 따라 보간 · 점마다 값 Toe · H · Rf · Rb도 같은 비로).
    /// <paramref name="clipped"/> 거짓이면 손대지 않았다(돌려준 목록 = 원래 줄 하나). 점마다 값 수가 점 수와 다르면 자르지 않는다(<paramref name="skipped"/>).</summary>
    public static List<WallExcav.Bottom> Bottom(WallExcav.Bottom b, RoadFootprint.Cutter cut, out bool clipped, out bool skipped)
    {
        clipped = false; skipped = false;
        var pts = b.Pts;
        int n = pts.Count;
        var vals = new[] { b.Toe, b.H, b.Rf, b.Rb };
        if (n < 2) return new List<WallExcav.Bottom> { b };
        if (vals.Any(v => v.Count != 0 && v.Count != n)) { skipped = true; return new List<WallExcav.Bottom> { b }; }
        var res = new List<WallExcav.Bottom>();
        WallExcav.Bottom? cur = null; bool open = false;
        for (int i = 0; i + 1 < n; i++)
        {
            var iv = cut.Outside(pts[i], pts[i + 1]);
            if (iv.Count != 1 || iv[0].T0 > 0 || iv[0].T1 < 1) clipped = true;
            bool cont = open && iv.Count > 0 && iv[0].T0 <= 1e-12;
            if (!cont) Flush();
            for (int k = 0; k < iv.Count; k++)
            {
                var (t0, t1) = iv[k];
                if (k > 0 || !cont) { Flush(); cur = Like(b); Add(cur, i, t0); }
                Add(cur!, i, t1);
            }
            open = iv.Count > 0 && iv[^1].T1 >= 1 - 1e-12;
            if (!open) Flush();
        }
        bool tailOpen = cur != null;
        Flush();
        if (!clipped) return new List<WallExcav.Bottom> { b };
        // 닫힌 줄 — 첫 조각이 시작점에서 · 끝 조각이 끝점(= 시작점)에서 밖이면 하나로
        if (tailOpen && res.Count >= 2 && n > 2 && pts[0].X == pts[^1].X && pts[0].Y == pts[^1].Y && res[0].Pts[0].X == pts[0].X && res[0].Pts[0].Y == pts[0].Y)
        {
            var tail = res[^1]; res.RemoveAt(res.Count - 1);
            tail.Pts.AddRange(res[0].Pts.Skip(1));
            var tv = new[] { tail.Toe, tail.H, tail.Rf, tail.Rb }; var hv = new[] { res[0].Toe, res[0].H, res[0].Rf, res[0].Rb };
            for (int j = 0; j < 4; j++) if (tv[j].Count > 0) tv[j].AddRange(hv[j].Skip(1));
            res[0] = tail;
        }
        res.RemoveAll(p => p.Pts.Count < 2 || Len(p.Pts) < MinPiece);
        foreach (var p in res) p.WallHMax = p.H.Count > 0 ? p.H.Max() : b.WallHMax;
        return res;

        void Flush() { if (cur != null && cur.Pts.Count >= 2) res.Add(cur); cur = null; }
        void Add(WallExcav.Bottom c, int i, double t)
        {
            var a = pts[i]; var q = pts[i + 1];
            c.Pts.Add(t <= 0 ? a : t >= 1 ? q : new Point3(a.X + (q.X - a.X) * t, a.Y + (q.Y - a.Y) * t, a.Z + (q.Z - a.Z) * t));
            var cv = new[] { c.Toe, c.H, c.Rf, c.Rb };
            for (int j = 0; j < 4; j++)
                if (vals[j].Count == n) cv[j].Add(t <= 0 ? vals[j][i] : t >= 1 ? vals[j][i + 1] : vals[j][i] + (vals[j][i + 1] - vals[j][i]) * t);
        }
    }

    static WallExcav.Bottom Like(WallExcav.Bottom b) => new()
    {
        Soil = b.Soil, Src = b.Src, WallHMax = b.WallHMax, Up = b.Up, SlopeW = b.SlopeW, Group = b.Group, Step = b.Step, Top = b.Top,
    };

    /// <summary>열린 선 2D 길이.</summary>
    public static double Len(IReadOnlyList<Point3> l)
    {
        double s = 0;
        for (int i = 0; i + 1 < l.Count; i++) s += Math.Sqrt((l[i + 1].X - l[i].X) * (l[i + 1].X - l[i].X) + (l[i + 1].Y - l[i].Y) * (l[i + 1].Y - l[i].Y));
        return s;
    }
}
