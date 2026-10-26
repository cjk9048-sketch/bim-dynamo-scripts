using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★★[v104.1 · 계획_v104_1.md 2 · JACK 1022 «종단이든 횡단이든 옹벽 모양을 하늘색 선으로만 표시해 놨잖아 — 이젠 단면을 직접 도면에 삽입해줘»]
/// <b>종단 · 횡단 뷰에 옹벽 단면 넣기</b> — 터파기 단면 기록(<c>DH_WALLSEC</c> · 수량과 같은 단면)을 자른 자리에 그린다.
/// <list type="bullet">
/// <item>모양: 재료마다 윤곽선(구조물 실선 · 비구조물 파선 · 버림) + 형식 이름 인출(화살표 + 글) — JACK 인터뷰 · 해치 · 치수 없음.</item>
/// <item>각도: 자르는 선과 앞면이 비스듬하면 그만큼 가로로 늘인다(JACK 2차 «각도대로 늘여서 맞춤») · sinθ &lt; 0.25면 안 그리고 센다.</item>
/// <item>레이어: 건설CALS/EC 전자도면 작성표준 V2.0 — 횡단 □C(CC-GSTR-NEWC · NEWD · LEAN · CC-LEAL · CC-LEAT) · 종단 □R(CR-STRU-NEWC · CR-LEAL · CR-LEAT).
/// 이미 있는(회사 템플릿) 레이어는 안 바꾼다 — 파선은 그 레이어가 실선이면 개체 선종류로.</item>
/// <item>지우기: 우리가 그린 것에만 XData(<see cref="App"/> · «XSEC»/«PROF» · 뷰 핸들) — CALS 레이어에 손으로 그린 구조물은 안 지운다(계획 검토 중간 9).</item>
/// </list></summary>
internal static class WallSectionDraw
{
    internal const string App = "DH_WALLSECDRAW";
    internal const string KindXsec = "XSEC", KindProf = "PROF";
    /// <summary>자르는 선과 앞면 사잇각 sin 하한(약 15°) — 이보다 비스듬하면 4배 넘게 늘어나 못 알아본다(내 판단 · 옹벽 자리 (전)(후) 횡단처럼 벽과 거의 나란한 표본선).</summary>
    internal const double MinSin = 0.25;
    /// <summary>인출 글 종이 높이(mm) — CALS T25.</summary>
    internal const double LabelMm = 2.5;

    internal sealed class Layers
    {
        public ObjectId Line, Dash, Lean, Leal, Leat;
        /// <summary>파선 레이어가 이미 파선이다(우리가 만들었거나 템플릿이 파선) — 개체는 ByLayer.</summary>
        public bool DashByLayer;
        /// <summary>그 파선 레이어를 우리가 DASHED로 만들었다 — 선종류 축척은 이때 · 개체 선종류일 때만(템플릿 선종류는 무늬 길이가 달라 안 건드린다 · 설치 전 검토 낮음 6).</summary>
        public bool DashOurs;
        public string? DashLt;
        public double DashLts;
    }

    /// <summary>파선 선종류를 싣고 축척을 잰다 — 그리기 트랜잭션 <b>밖에서</b> 부른다(선종류 파일 싣기).</summary>
    internal static (string? Lt, double Lts) Prepare(Database db)
    {
        string? lt = Commands.SectionCommand.LoadLinetype(db, "DASHED");
        return (lt, lt == null ? 0 : Commands.SectionCommand.LtScaleFor(db, lt));
    }

    internal static Layers XsecLayers(Database db, Transaction tr, (string? Lt, double Lts) dash)
    {
        var L = new Layers { DashLt = dash.Lt, DashLts = dash.Lts };
        L.Line = Ensure(db, tr, "CC-GSTR-NEWC", 4, null, out _, out _);
        L.Dash = Ensure(db, tr, "CC-GSTR-NEWD", 4, dash.Lt, out bool dashOk, out bool made);
        L.DashByLayer = dashOk; L.DashOurs = dashOk && made;
        L.Lean = Ensure(db, tr, "CC-GSTR-LEAN", 3, null, out _, out _);
        L.Leal = Ensure(db, tr, "CC-LEAL", 1, null, out _, out _);
        L.Leat = Ensure(db, tr, "CC-LEAT", 3, null, out _, out _);
        return L;
    }

    /// <summary>종단 □R — «신규 구조물 파선» 칸이 없어 비구조물도 CR-STRU-NEWC에 개체 선종류 파선(계획 1 · 내 판단) · 버림도 같은 레이어(□R에 린 칸 없음).</summary>
    internal static Layers ProfLayers(Database db, Transaction tr, (string? Lt, double Lts) dash)
    {
        var L = new Layers { DashLt = dash.Lt, DashLts = dash.Lts, DashByLayer = false };
        L.Line = Ensure(db, tr, "CR-STRU-NEWC", 4, null, out _, out _);
        L.Dash = L.Line; L.Lean = L.Line;
        L.Leal = Ensure(db, tr, "CR-LEAL", 1, null, out _, out _);
        L.Leat = Ensure(db, tr, "CR-LEAT", 3, null, out _, out _);
        return L;
    }

    /// <summary>레이어 — 없으면 CALS 색 · 선종류로 만든다(<paramref name="dashed"/> = 그 레이어가 파선으로 그려지는가). 있으면 손대지 않는다.</summary>
    static ObjectId Ensure(Database db, Transaction tr, string name, short aci, string? lt, out bool dashed, out bool created)
    {
        dashed = false; created = false;
        var lt0 = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt0.Has(name))
        {
            var id0 = lt0[name];
            if (lt != null && tr.GetObject(id0, OpenMode.ForRead) is LayerTableRecord ex
                && tr.GetObject(ex.LinetypeObjectId, OpenMode.ForRead) is LinetypeTableRecord ltr0)
                dashed = ltr0.NumDashes > 0;   // 템플릿 레이어가 이미 파선(A-DASHD01 등)
            return id0;
        }
        var id = Commands.SectionCommand.EnsureLayer(db, tr, name, aci);
        created = true;
        if (lt != null)
            try
            {
                var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (ltt.Has(lt) && tr.GetObject(id, OpenMode.ForWrite) is LayerTableRecord rec) { rec.LinetypeObjectId = ltt[lt]; dashed = true; }
            }
            catch { }
        return id;
    }

    internal const string WhyOff = "옹벽 터파기 꺼짐", WhyNoAlignRec = "옹벽 터파기 기록 없음";
    /// <summary>안 그린 까닭이 «터파기를 안 켬»인가(안내 한 줄) — 그 밖(못 지음 · 단면 기록 없음 등)은 ⚠로 말한다(설치 전 검토 낮음 9 — «단면 기록 없음»도 «기록 없음»에 걸렸다).</summary>
    internal static bool IsOff(string why) => why == WhyOff || why == WhyNoAlignRec;

    /// <summary>선형 기록 → 터파기면 → 단면 기록. 없으면 null + 까닭(터파기 꺼짐 · 못 지음 · 옛 면).</summary>
    internal static WallExcav.SecRecord? RecordFor(Database db, Transaction tr, DBObject al, out string why)
    {
        why = "";
        var r = WallExcavBuilder.ReadAlign(tr, al);
        if (r == null) { why = WhyNoAlignRec; return null; }
        if (r.Value.State != WallExcavBuilder.State.Built || r.Value.Handle.Length == 0)
        { why = r.Value.State == WallExcavBuilder.State.Off ? WhyOff : "옹벽 터파기면을 못 지음"; return null; }
        ObjectId sid = ObjectId.Null;
        try { sid = db.GetObjectId(false, new Handle(System.Convert.ToInt64(r.Value.Handle, 16)), 0); } catch { }
        if (sid.IsNull || sid.IsErased) { why = "옹벽 터파기면이 없다"; return null; }
        return WallExcavBuilder.ReadSecRecord(tr, tr.GetObject(sid, OpenMode.ForRead), out why);
    }

    /// <summary>뷰 좌표 아핀 — 범위 안 세 점으로 (축 값, 높이) → 도면 (x, y)를 한 번 잰다(세로 과장 포함 · 범위 밖 꼭짓점도 그린다 — 검토 중간 7).</summary>
    internal static System.Func<double, double, Point2d>? Affine(double aLo, double aHi, double zLo, double zHi, System.Func<double, double, Point2d?> find)
    {
        double aA = (aLo + aHi) / 2, zA = (zLo + zHi) / 2, dA = (aHi - aLo) / 4, dZ = (zHi - zLo) / 4;
        if (!(dA > 1e-9) || !(dZ > 1e-9)) return null;
        var p0 = find(aA, zA); var p1 = find(aA + dA, zA); var p2 = find(aA, zA + dZ);
        if (p0 == null || p1 == null || p2 == null) return null;
        double x0 = p0.Value.X, y0 = p0.Value.Y;
        double ax = (p1.Value.X - x0) / dA, ay = (p1.Value.Y - y0) / dA, bx = (p2.Value.X - x0) / dZ, by = (p2.Value.Y - y0) / dZ;
        return (a, z) => new Point2d(x0 + (a - aA) * ax + (z - zA) * bx, y0 + (a - aA) * ay + (z - zA) * by);
    }

    /// <summary>★[v105.1 · JACK 1023 «단이 많으면 라벨은 중간에 하나만 표시»] 한 뷰의 단면들 중 인출을 달 것 — 축 범위가 겹치거나 1 m 안이면 한 묶음(단 쌓임) ·
    /// 묶음 안 형식 · 흙 쪽 방향마다 가운데 단 하나(높이 차례 · 둘이면 위 단). <paramref name="along"/> = 단면마다 교점 축 값.
    /// 돌려주는 값 = 인출 단(글 높이 몇 줄 올리나 — 그 묶음 안 차례 · −1이면 인출 없음). ★[설치 전 검토 낮음] 종전처럼 뷰 전체로 세면 멀리 떨어진 묶음끼리도 쌓여 열 번째 글이 공중에 떴다.</summary>
    internal static int[] PickLabels(IReadOnlyList<WallExcav.CutSec> cs, IReadOnlyList<double> along)
    {
        int n = cs.Count;
        var pick = Enumerable.Repeat(-1, n).ToArray();
        var info = new (double Lo, double Hi, double Top)[n];
        for (int i = 0; i < n; i++)
        {
            var c = cs[i];
            double a = along[i] + c.Sign * c.Shape.XFront * c.Stretch, b = along[i] + c.Sign * c.Shape.XBack * c.Stretch;
            double top = c.Shape.Parts.Where(p => p.Structure).SelectMany(p => p.Ring).Select(q => q.Z).DefaultIfEmpty(0).Max();
            info[i] = (System.Math.Min(a, b), System.Math.Max(a, b), c.Toe + top);
        }
        var order = Enumerable.Range(0, n).OrderBy(i => info[i].Lo).ToList();
        int k = 0;
        while (k < order.Count)
        {
            var grp = new List<int> { order[k] };
            double hi = info[order[k]].Hi;
            int m = k + 1;
            while (m < order.Count && info[order[m]].Lo <= hi + 1.0) { grp.Add(order[m]); hi = System.Math.Max(hi, info[order[m]].Hi); m++; }
            int lvl = 0;
            foreach (var byKind in grp.GroupBy(i => (cs[i].Kind, cs[i].Sign)))   // ★[검토 낮음 4] 흙 쪽 방향이 같은 것끼리만 — 좁은 길을 사이에 둔 마주 보는 벽은 따로
            {
                var tiers = byKind.OrderByDescending(i => info[i].Top).ToList();
                pick[tiers[(tiers.Count - 1) / 2]] = lvl++;
            }
            k = m;
        }
        return pick;
    }

    /// <summary>★[v105.1 · JACK 1023 «배면사면 기울기 라벨 누락» · «뒷채움 사면 기울기 라벨 누락»] 기울기 라벨 재료 — 선형 기록의 옹벽 터파기면 · 배면 그림 면 · 지금 설정의 경사.
    /// 종단 · 횡단은 지금 설정으로 터파기면을 지은 뒤에만 그린다(횡단은 «[종단도] 먼저» 관문) — 그래서 설정 경사가 곧 그린 면의 경사다.</summary>
    internal sealed class SlopeSrc
    {
        public CachedGroundSurface? Exc, Back;
        public double NExc, NBack, BenchW;
        public bool BenchOn;
        /// <summary>★[v105.3] 라벨을 쓸지 — 3D 면이 있거나(종단) 2D 선이 있거나(횡단 · 가로지르는 벽만).</summary>
        public bool UseExc, UseBack;
    }

    internal static SlopeSrc? SlopeSources(Database db, Transaction tr, DBObject al)
    {
        try
        {
            var st = WallListStore.ExcavOf(db, out _);
            var src = new SlopeSrc { NExc = st.Slope, NBack = st.BackSlope, BenchOn = st.BenchOn, BenchW = st.BenchW };
            CachedGroundSurface? Open(string h)
            {
                if (string.IsNullOrEmpty(h)) return null;
                try
                {
                    var id = db.GetObjectId(false, new Handle(System.Convert.ToInt64(h, 16)), 0);
                    return !id.IsNull && !id.IsErased && tr.GetObject(id, OpenMode.ForRead) is CivilDb.TinSurface t ? new CachedGroundSurface(t) : null;
                }
                catch { return null; }
            }
            var a = WallExcavBuilder.ReadAlign(tr, al);
            if (a != null && a.Value.State == WallExcavBuilder.State.Built) src.Exc = Open(a.Value.Handle);
            var b = WallExcavBuilder.ReadAlignBack(tr, al);
            if (b != null && b.Value.State == WallExcavBuilder.State.Built && st.BackOn) src.Back = Open(b.Value.Handle);
            // ★[설치 전 검토 낮음] 배면 숫자는 터파기면에 붙은 배면 기록의 경사(그린 면과 같은 판) — 설정만 바뀌고 면이 아직이면 다른 숫자가 붙었다
            try
            {
                if (src.Back != null && a != null && !string.IsNullOrEmpty(a.Value.Handle))
                {
                    var eid = db.GetObjectId(false, new Handle(System.Convert.ToInt64(a.Value.Handle, 16)), 0);
                    var brec = WallBackBuilder.ReadRecord(tr, tr.GetObject(eid, OpenMode.ForRead), out _);
                    if (brec != null && brec.N > 0) src.NBack = brec.N;
                }
            }
            catch { }
            src.UseExc = src.Exc != null; src.UseBack = src.Back != null;
            return src.Exc == null && src.Back == null ? null : src;
        }
        catch { return null; }
    }

    static string SlopeText(double n) => "1:" + n.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>★[v105.1 · 계획 검토 중간 3] 면의 평면 기울기 크기 |∇z|(±0.1 m 중앙 차분) — 축 기울기만 보면 벽 따라 가는 종단에서 기운 평탄 · 바닥 계단에도 라벨이 붙었다. 못 재면 NaN.</summary>
    internal static double Grad(CachedGroundSurface? s, double x, double y)
    {
        if (s == null) return double.NaN;
        const double d = 0.1;
        if (!s.TryGetElevation(x + d, y, out double a) || !s.TryGetElevation(x - d, y, out double b) || !s.TryGetElevation(x, y + d, out double c) || !s.TryGetElevation(x, y - d, out double e)) return double.NaN;
        double gx = (a - b) / (2 * d), gy = (c - e) / (2 * d);
        return System.Math.Sqrt(gx * gx + gy * gy);
    }

    /// <summary>뷰 축 <paramref name="a"/>(증가 · 0.1 m 안팎)를 따라 잰 터파기면 <paramref name="eZ"/> · 배면 <paramref name="bZ"/>(NaN = 없음)에서 경사 구간을 찾아 «1:n» 글을 선과 나란히 쓴다.
    /// <list type="bullet">
    /// <item>배면: 면의 평면 기울기 |∇B|가 1/n의 ±10% 안 · |dB/da| ≥ 0.05 · 터파기선과 2 cm 넘게 떨어진 구간(배면이 터파기선을 따르는 자리는 빼고) — 소단 폭 + 0.6 m 안 틈은 잇고 구간마다 하나.</item>
    /// <item>터파기: |∇E|가 1/m의 ±10% 안 · |dE/da| ≥ 0.05인 구간 — 소단(평평 ≤ 소단 폭 + 0.6 m)으로만 떨어진 같은 쪽 구간은 한 덩어리 · 덩어리마다 가운데 구간에 하나.</item>
    /// <item>그린 길이가 글 폭보다 짧으면 안 쓴다(짧은 앞 법면 등) — 터파기 덩어리는 가운데 토막이 짧으면 소단까지 묶은 현을 따라. 숫자 = 설정 경사(3D 면의 경사 — 비스듬한 뷰에서는 선이 더 완만해 보인다).</item>
    /// </list> 쓴 글 수.</summary>
    internal static int DrawSlopeLabels(Database db, Transaction tr, BlockTableRecord ms, double[] a, double[] eZ, double[] bZ, double[] gE, double[] gB, System.Func<double, double, Point2d> map,
                                        double txtH, SlopeSrc src, Layers L, ObjectId kst, string kind, string view)
    {
        bool Near(double g, double nn) => !double.IsNaN(g) && nn > 0 && System.Math.Abs(g * nn - 1) <= 0.1;
        int n = a.Length, made = 0;
        if (n < 3) return 0;
        // 조건을 만족하는 변(i → i+1)의 연속 구간 — gap 변 이하로 끊긴 것은 잇는다
        List<(int I0, int I1)> Runs(double[] z, System.Func<int, bool> ok, int gap)
        {
            var r = new List<(int, int)>();
            int s = -1, lastOk = -10;
            for (int i = 0; i + 1 < n; i++)
            {
                if (!ok(i)) continue;
                // ★[설치 전 검토 낮음] 메우는 변도 두 끝 높이가 있어야 — NaN 한 점이 끼면 길이가 NaN이 되어 그 구간 라벨이 말없이 빠졌다
                if (s >= 0 && i - lastOk - 1 <= gap && Enumerable.Range(lastOk + 1, i - lastOk - 1).All(g => Def(z, g))) { lastOk = i; continue; }
                if (s >= 0) r.Add((s, lastOk + 1));
                s = i; lastOk = i;
            }
            if (s >= 0) r.Add((s, lastOk + 1));
            return r;
        }
        double Sl(double[] z, int i) => (z[i + 1] - z[i]) / System.Math.Max(1e-9, a[i + 1] - a[i]);
        bool Def(double[] z, int i) => !double.IsNaN(z[i]) && !double.IsNaN(z[i + 1]);
        double Len(double[] z, int i0, int i1) { double s = 0; for (int i = i0; i < i1; i++) s += (map(a[i + 1], z[i + 1]) - map(a[i], z[i])).Length; return s; }
        // ★[v105.1 · 화면 없는 Civil V39 — 횡단 축척에서 소단 사이 법면 한 토막(약 4 m)이 글 폭 1.5배에 못 미쳐 «1:1.0»이 하나도 안 붙었다] 글 폭 1배면 쓴다
        bool below = false;   // 터파기 라벨은 선 아래(땅 쪽) — 위쪽은 배면 선이 지나 겹친다
        bool Put(double[] z, int i0, int i1, string text)
        {
            double w = text.Length * 0.8 * txtH;
            double tot = Len(z, i0, i1);
            if (tot < w) return false;
            // 그린 길이 가운데 점과 그 곁 ±w/2 현(글이 놓일 자리의 선 방향)
            Point2d At(double t)
            {
                double acc = 0;
                for (int i = i0; i < i1; i++)
                {
                    var p0 = map(a[i], z[i]); var p1 = map(a[i + 1], z[i + 1]);
                    double l = (p1 - p0).Length;
                    if (acc + l >= t || i == i1 - 1) { double f = l < 1e-12 ? 0 : System.Math.Clamp((t - acc) / l, 0, 1); return p0 + (p1 - p0) * f; }
                    acc += l;
                }
                return map(a[i1], z[i1]);
            }
            var M = At(tot / 2);
            var d = At(System.Math.Min(tot, tot / 2 + w / 2)) - At(System.Math.Max(0, tot / 2 - w / 2));
            if (d.Length < 1e-9) return false;
            if (d.X < 0) d = -d;                                       // 읽는 방향(왼 → 오른)
            var u = d / d.Length; var nn = new Vector2d(-u.Y, u.X);    // 선 위쪽(글 아래 변이 선에 붙는다)
            var t = new DBText
            {
                TextString = text,
                Height = txtH,
                Rotation = System.Math.Atan2(u.Y, u.X),
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = below ? TextVerticalMode.TextTop : TextVerticalMode.TextBottom,
            };
            t.SetDatabaseDefaults(db);
            t.LayerId = L.Leat;
            if (!kst.IsNull) t.TextStyleId = kst;
            double off = below ? -0.4 * txtH : 0.4 * txtH;
            t.AlignmentPoint = new Point3d(M.X + nn.X * off, M.Y + nn.Y * off, 0);
            Add(db, tr, ms, t, kind, view); made++;
            return true;
        }
        // 한 토막도 짧으면 소단까지 묶은 덩어리의 현(처음 → 끝)을 따라 — 소단 모서리를 넘게 조금 더 띄운다
        bool PutChord(double[] z, int i0, int i1, string text)
        {
            double w = text.Length * 0.8 * txtH;
            var p0 = map(a[i0], z[i0]); var p1 = map(a[i1], z[i1]);
            var d = p1 - p0;
            if (d.Length < w) return false;
            if (d.X < 0) d = -d;
            var u = d / d.Length; var nn = new Vector2d(-u.Y, u.X);
            var M = new Point2d((p0.X + p1.X) / 2, (p0.Y + p1.Y) / 2);
            var t = new DBText { TextString = text, Height = txtH, Rotation = System.Math.Atan2(u.Y, u.X), HorizontalMode = TextHorizontalMode.TextCenter, VerticalMode = below ? TextVerticalMode.TextTop : TextVerticalMode.TextBottom };
            t.SetDatabaseDefaults(db);
            t.LayerId = L.Leat;
            if (!kst.IsNull) t.TextStyleId = kst;
            double off = below ? -0.9 * txtH : 0.9 * txtH;
            t.AlignmentPoint = new Point3d(M.X + nn.X * off, M.Y + nn.Y * off, 0);
            Add(db, tr, ms, t, kind, view); made++;
            return true;
        }
        double benchGap = (src.BenchOn ? src.BenchW : 0) + 0.6;
        // 경사 구간들을 덩어리로(같은 쪽 · 사이 틈 ≤ 소단 폭 + 0.6 m · 틈에 면이 이어짐) → 덩어리마다 하나 — 가운데(그린 길이) 토막에 · 짧으면 덩어리 현을 따라
        void Groups(double[] z, List<(int I0, int I1)> runs, double gap, string text)
        {
            int k = 0;
            while (k < runs.Count)
            {
                int sgn = System.Math.Sign(z[runs[k].I1] - z[runs[k].I0]);
                int m = k + 1;
                while (m < runs.Count && a[runs[m].I0] - a[runs[m - 1].I1] <= gap && System.Math.Sign(z[runs[m].I1] - z[runs[m].I0]) == sgn
                       && Enumerable.Range(runs[m - 1].I1, runs[m].I0 - runs[m - 1].I1 + 1).All(i => !double.IsNaN(z[i]))) m++;
                double tot = 0; for (int r = k; r < m; r++) tot += Len(z, runs[r].I0, runs[r].I1);
                double acc = 0; int mid = k;
                for (int r = k; r < m; r++) { double l = Len(z, runs[r].I0, runs[r].I1); if (acc + l >= tot / 2) { mid = r; break; } acc += l; }
                if (!Put(z, runs[mid].I0, runs[mid].I1, text) && m - k > 1) PutChord(z, runs[k].I0, runs[m - 1].I1, text);
                k = m;
            }
        }
        // 배면 — 터파기선과 겹친 자리(소단 위 B = E)로 끊긴 토막도 한 덩어리(검토 낮음 5 · 화면 없는 Civil V39 — 1.5 m 겹침으로 «1:1.5»가 둘 붙었다)
        if (src.UseBack)
            Groups(bZ, Runs(bZ, i => Def(bZ, i) && System.Math.Abs(Sl(bZ, i)) >= 0.05 && Near(gB[i], src.NBack) && Near(gB[i + 1], src.NBack)
                                 && (!Def(eZ, i) || (bZ[i] > eZ[i] + 0.02 && bZ[i + 1] > eZ[i + 1] + 0.02)), 2), benchGap + 1.0, SlopeText(src.NBack));
        // 터파기 — 소단으로 떨어진 같은 쪽 구간 묶기 · 선 아래
        below = true;
        if (src.UseExc)
            Groups(eZ, Runs(eZ, i => Def(eZ, i) && System.Math.Abs(Sl(eZ, i)) >= 0.05 && Near(gE[i], src.NExc) && Near(gE[i + 1], src.NExc), 2), benchGap, SlopeText(src.NExc));
        return made;
    }

    /// <summary>단면 하나 — 조각마다 닫힌 꺾은선 + 인출(맨 위 앞 꼭짓점에서 앞쪽 위로 · <paramref name="stagger"/>번째 단은 한 글 높이씩 더 올린다 — 검토 낮음 12).
    /// <paramref name="along0"/> = 교점의 축 값(횡단 오프셋 · 종단 측점). <paramref name="label"/> = 인출을 단다(여러 단이면 가운데 하나만 — <see cref="PickLabels"/>). 그린 개체 수.</summary>
    internal static int DrawOne(Database db, Transaction tr, BlockTableRecord ms, WallExcav.CutSec c, double along0, System.Func<double, double, Point2d> map,
                                double txtH, int stagger, Layers L, ObjectId kst, string kind, string view, bool label = true)
    {
        int n = 0;
        Point2d P(double X, double Z) => map(along0 + c.Sign * X * c.Stretch, c.Toe + Z);
        foreach (var part in c.Shape.Parts.OrderBy(p => p.Structure ? 1 : 0))   // 비구조물 먼저(구조물 실선이 위에)
        {
            if (part.Ring.Count < 3) continue;
            double area2 = 0;                                                  // ★[설치 전 검토 낮음 7] 넓이 0 조각(버림 두께 0 · 메쌓기 배수층 폭 0)은 납작한 선이라 안 그림
            for (int i = 0; i < part.Ring.Count; i++) { var a0 = part.Ring[i]; var b0 = part.Ring[(i + 1) % part.Ring.Count]; area2 += a0.X * b0.Z - b0.X * a0.Z; }
            if (System.Math.Abs(area2) < 2e-6) continue;
            var pl = new Polyline();
            for (int i = 0; i < part.Ring.Count; i++) pl.AddVertexAt(i, P(part.Ring[i].X, part.Ring[i].Z), 0, 0, 0);
            pl.Closed = true;
            pl.SetDatabaseDefaults(db);
            bool lean = part.Material == "버림";
            bool dash = !part.Structure || part.Fraction < 1 - 1e-12;          // 비구조물 · 부벽 평균 조각
            pl.LayerId = lean ? L.Lean : dash ? L.Dash : L.Line;
            bool ownLt = dash && !L.DashByLayer && L.DashLt != null;
            if (ownLt) pl.Linetype = L.DashLt;
            if ((ownLt || (dash && L.DashOurs)) && L.DashLts > 0) pl.LinetypeScale = L.DashLts;
            Add(db, tr, ms, pl, kind, view); n++;
        }
        if (!label) return n;
        // 인출 — 구조물 맨 위(같으면 가장 앞) 꼭짓점
        var top = c.Shape.Parts.Where(p => p.Structure).SelectMany(p => p.Ring).DefaultIfEmpty(c.Shape.Parts.SelectMany(p => p.Ring).FirstOrDefault())
                   .OrderByDescending(q => q.Z).ThenBy(q => q.X).First();
        var A = P(top.X, top.Z);
        var pf = map(along0 - c.Sign, c.Toe); var pb = map(along0, c.Toe);
        double fx = pf.X - pb.X >= 0 ? 1 : -1;                                 // 앞(흙 반대쪽)이 도면 어느 쪽인가
        string name = WallSection.Of(c.Kind).Name;
        var P1 = new Point2d(A.X + fx * 1.2 * txtH, A.Y + (2.2 + 1.6 * stagger) * txtH);
        double w = name.Length * txtH * 0.95 + 0.6 * txtH;
        var P2 = new Point2d(P1.X + fx * w, P1.Y);
        var ld = new Polyline();
        ld.AddVertexAt(0, A, 0, 0, 0); ld.AddVertexAt(1, P1, 0, 0, 0); ld.AddVertexAt(2, P2, 0, 0, 0);
        ld.SetDatabaseDefaults(db); ld.LayerId = L.Leal;
        Add(db, tr, ms, ld, kind, view); n++;
        var dv = A - P1; double dl = dv.Length;
        if (dl > 1e-9)
        {
            var u = dv / dl; var nn = new Vector2d(-u.Y, u.X);
            var b = A - u * (0.9 * txtH);
            var ar = new Solid(new Point3d(A.X, A.Y, 0), new Point3d(b.X + nn.X * 0.3 * txtH, b.Y + nn.Y * 0.3 * txtH, 0), new Point3d(b.X - nn.X * 0.3 * txtH, b.Y - nn.Y * 0.3 * txtH, 0));
            ar.SetDatabaseDefaults(db); ar.LayerId = L.Leal;
            Add(db, tr, ms, ar, kind, view); n++;
        }
        var t = new DBText
        {
            TextString = name,
            Height = txtH,
            HorizontalMode = fx > 0 ? TextHorizontalMode.TextLeft : TextHorizontalMode.TextRight,
            VerticalMode = TextVerticalMode.TextBottom,
        };
        t.SetDatabaseDefaults(db);
        t.LayerId = L.Leat;
        if (!kst.IsNull) t.TextStyleId = kst;
        t.AlignmentPoint = new Point3d(P1.X + fx * 0.3 * txtH, P1.Y + 0.25 * txtH, 0);   // ★정렬이면 기준은 AlignmentPoint 하나(Position을 같이 넣지 않는다)
        Add(db, tr, ms, t, kind, view); n++;
        return n;
    }

    internal const string LayExc2D = "DH-횡단옹벽터파기", LayBack2D = "DH-횡단옹벽배면";

    /// <summary>★★★[v105.3 · 계획_v105_3.md 5 · JACK 1026 «가로지르는 벽만 (노란 선)»] 횡단 2D 터파기선 · 배면사면 꺾은선 — 수량(CollectQty)이 지은 <b>그 객체</b>를 그대로(꼭짓점 = 수량 꺾임점).
    /// <list type="bullet">
    /// <item>터파기: 구간마다 한 줄(<see cref="LayExc2D"/> · 마젠타 — 지금까지 Civil 단면 «DH_횡단면_터파기»와 같은 색) · 도랑 뒤(앞면 선 = 깎기면)는 땅까지 수직.</item>
    /// <item>배면: Ω 안 토막마다(<see cref="LayBack2D"/> · 흰색 — 계획면 색).</item>
    /// </list> 그린 개체 수.</summary>
    internal static int DrawXs2D(Database db, Transaction tr, BlockTableRecord ms, WallExcav.Xs2D x2, System.Func<double, double, Point2d> map, string kind, string view)
    {
        int made = 0;
        var lE = Commands.SectionCommand.EnsureLayer(db, tr, LayExc2D, Commands.SectionCommand.ExcavAci);
        var lB = Commands.SectionCommand.EnsureLayer(db, tr, LayBack2D, 7);
        void Put(List<Point2d> pts, ObjectId lay)
        {
            var q = new List<Point2d>();
            foreach (var p in pts) if (q.Count == 0 || (p - q[^1]).Length > 1e-9) q.Add(p);
            if (q.Count < 2) return;
            var pl = new Polyline();
            for (int i = 0; i < q.Count; i++) pl.AddVertexAt(i, q[i], 0, 0, 0);
            pl.SetDatabaseDefaults(db);
            pl.LayerId = lay;
            Add(db, tr, ms, pl, kind, view); made++;
        }
        bool Edge(double a) => a <= x2.AMin + 1e-9 || a >= x2.AMax - 1e-9;
        bool Day(double a) { double z = x2.EMin(a, out _), l = x2.Lz(a); return !double.IsNaN(z) && !double.IsNaN(l) && System.Math.Abs(z - l) <= 1e-3; }
        foreach (var (lo, hi) in x2.Spans)
        {
            var pts = new List<Point2d>();
            void Vert(double a) { double l = x2.Lz(a), z = x2.EMin(a, out _); if (!Day(a) && !Edge(a) && !double.IsNaN(l) && !double.IsNaN(z) && z < l) pts.Add(map(a, l)); }
            Vert(lo);
            foreach (double a in x2.Breaks)
            {
                if (a < lo - 1e-9 || a > hi + 1e-9) continue;
                double z = x2.EMin(a, out _);
                if (!double.IsNaN(z)) pts.Add(map(a, z));
            }
            Vert(hi);
            Put(pts, lE);
        }
        foreach (var bk in x2.Backs)
        {
            var run = new List<Point2d>();
            foreach (double a in x2.Breaks.Where(a => a >= bk.Lo - 1e-9 && a <= bk.Hi + 1e-9))
            {
                double b = x2.B(a, out bool ins, out _);
                if (ins) run.Add(map(a, b));
                else { Put(run, lB); run = new List<Point2d>(); }
            }
            Put(run, lB);
        }
        return made;
    }

    static void Add(Database db, Transaction tr, BlockTableRecord ms, Entity e, string kind, string view)
    {
        GradingBuilder.EnsureRegApp(db, tr, App);
        e.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, App),
                                   new TypedValue((int)DxfCode.ExtendedDataAsciiString, kind),
                                   new TypedValue((int)DxfCode.ExtendedDataAsciiString, view));
        ms.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true);
    }

    static bool TagOf(Entity e, out string kind, out string view)
    {
        kind = view = "";
        using var rb = e.GetXDataForApplication(App);
        if (rb == null) return false;
        var a = rb.AsArray();
        if (a.Length > 1) kind = a[1].Value as string ?? "";
        if (a.Length > 2) view = a[2].Value as string ?? "";
        return true;
    }

    /// <summary>우리 단면만 지운다 — <paramref name="kind"/> null = 둘 다 · <paramref name="view"/> null = 모든 뷰. 모형 공간만(우리는 모형에만 그린다). 모아 두고 밖에서 지운다(훑으며 지우지 않는다 — §71).</summary>
    internal static int EraseTagged(Database db, Transaction tr, string? kind, string? view)
    {
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        var kill = new List<ObjectId>();
        foreach (ObjectId id in ms)
        {
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is Entity e && TagOf(e, out string k, out string v) && (kind == null || k == kind) && (view == null || v == view)) kill.Add(id);
            }
            catch { }
        }
        int n = 0;
        foreach (var id in kill) try { tr.GetObject(id, OpenMode.ForWrite, false, true).Erase(); n++; } catch { }   // 잠긴 레이어여도(설치 전 검토 낮음 5 — 못 지우면 다시 그릴 때마다 겹쳐 쌓인다)
        return n;
    }
}
