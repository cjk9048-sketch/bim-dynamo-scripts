using System.Globalization;
using System.Text;

namespace DH.Grading.Core;

/// <summary>★★[v116.5 · JACK 1110 «구조물은 Revit에서 — rvt · ifc · fbx»] IFC(STEP 글) → 부재마다 세모(m · 세계 좌표). 외부 라이브러리 없음.
/// <para>읽는 형상(Body): IfcFacetedBrep(· WithVoids 바깥) · IfcShellBasedSurfaceModel · IfcFaceBasedSurfaceModel · IfcTriangulatedFaceSet · IfcPolygonalFaceSet ·
/// IfcExtrudedAreaSolid(IfcArbitraryClosedProfileDef(IfcPolyline) · IfcArbitraryProfileDefWithVoids(바깥만) · IfcRectangleProfileDef · IfcCircleProfileDef 32각) · IfcMappedItem ·
/// IfcBooleanClippingResult · IfcBooleanResult = 첫 피연산자만(구멍 · 잘림 안 함 — 셈). 못 읽은 형상은 종류별로 센다(말없이 빼지 않음).</para>
/// <para>배치 = IfcLocalPlacement 사슬(IfcAxis2Placement3D) · 길이 단위 = IfcSIUnit LENGTHUNIT(MILLI · CENTI …) · IfcConversionBasedUnit(피트 · 인치) · IFC4 IfcMapConversion(있으면 동 · 북 · 높이 · 회전 · 축척).
/// Revit IFC2X3 «공유 좌표» 내보내기는 공유 좌표 원점 · 진북이 IfcSite 배치에 들어 있어 사슬만으로 세계 좌표가 된다.</para></summary>
public static class IfcMesh
{
    public sealed class Part
    {
        public string Type = "", Name = "", Guid = "";
        public readonly List<Point3> V = new();
        public readonly List<(int A, int B, int C)> T = new();
    }

    public sealed class Result
    {
        public readonly List<Part> Parts = new();
        public double UnitM = 1;
        public int Products, NoBody, Booleans, HoleFaces, VoidShells, ProfileVoids, Openings;
        public readonly Dictionary<string, int> Skipped = new();
        public bool MapConversion;
        public string Schema = "";
        public int Tris => Parts.Sum(p => p.T.Count);
        public string Text => $"IFC {Schema} · 단위 {UnitM:0.####} m · 부재 {Parts.Count}/{Products} · 세모 {Tris:N0}"
            + (MapConversion ? " · 지도 변환(IfcMapConversion)" : "")
            + (NoBody > 0 ? $" · 형상 없는 부재 {NoBody}" : "")
            + (Openings > 0 ? $" · ⚠개구부(문 · 창 · 관 구멍) {Openings}개는 안 뚫음" : "")
            + (Booleans > 0 ? $" · ⚠불리언(구멍 · 잘림) {Booleans}개는 첫 형상만" : "")
            + (HoleFaces + VoidShells + ProfileVoids > 0 ? $" · ⚠구멍 면 {HoleFaces} · 빈 껍질 {VoidShells} · 단면 구멍 {ProfileVoids}은 바깥만" : "")
            + (Skipped.Count > 0 ? " · ⚠안 읽은 형상 " + string.Join(", ", Skipped.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}")) : "");
    }

    // ───────────── STEP 값
    abstract class V { }
    sealed class VRef : V { public int Id; }
    sealed class VStr : V { public string S = ""; }
    sealed class VNum : V { public double D; }
    sealed class VEnum : V { public string S = ""; }
    sealed class VList : V { public readonly List<V> L = new(); }
    sealed class VTyped : V { public string Name = ""; public V? Arg; }
    sealed class VNull : V { }
    sealed class Ent { public string Name = ""; public List<V> A = new(); }

    /// <summary>파일에서 읽기.</summary>
    public static Result Read(string path)
    {
        string text = File.ReadAllText(path, Encoding.UTF8);
        // ★[설치 전 검토 L] 내보내는 중(끝까지 안 쓰인) IFC는 일부 형상만 읽혀 말없이 틀린 모형이 됨 — 끝 표시가 없으면 막음
        if (text.LastIndexOf("END-ISO-10303-21", StringComparison.Ordinal) < 0) throw new FormatException("IFC 끝(END-ISO-10303-21)이 없습니다 — Revit이 아직 내보내는 중이거나 파일이 잘렸습니다");
        return Parse(text);
    }

    public static Result Parse(string text)
    {
        var R = new Result();
        var E = Entities(text, out string schema);
        R.Openings = E.Values.Count(e => e.Name == "IFCRELVOIDSELEMENT");   // ★[설치 전 검토 M1] 개구부는 안 뚫음 — 세고 알림
        R.Schema = schema;
        var ctx = new Ctx(E, R);
        R.UnitM = ctx.LengthUnit();
        ctx.ReadMap();
        var skipTypes = new HashSet<string> { "IFCSITE", "IFCBUILDING", "IFCBUILDINGSTOREY", "IFCSPACE", "IFCOPENINGELEMENT", "IFCANNOTATION", "IFCGRID", "IFCVIRTUALELEMENT", "IFCPROJECT" };
        foreach (var (id, e) in E)
        {
            // IfcProduct = (GlobalId, OwnerHistory, Name, Description, ObjectType, ObjectPlacement, Representation, …)
            if (e.A.Count < 7 || skipTypes.Contains(e.Name)) continue;
            if (e.A[5] is not VRef pl || ctx.Get(pl.Id)?.Name != "IFCLOCALPLACEMENT") continue;
            if (e.A[6] is not VRef rp || ctx.Get(rp.Id)?.Name != "IFCPRODUCTDEFINITIONSHAPE") continue;
            R.Products++;
            var part = new Part { Type = e.Name, Name = ctx.Str(e.A[2]), Guid = ctx.Str(e.A[0]) };
            var M = ctx.Placement(pl.Id, 0);
            int before = part.T.Count;
            foreach (var shape in ctx.Refs(ctx.Get(rp.Id)!.A.ElementAtOrDefault(2)))
            {
                var sr = ctx.Get(shape); if (sr == null || sr.Name != "IFCSHAPEREPRESENTATION") continue;
                string ident = ctx.Str(sr.A.ElementAtOrDefault(1));
                if (!string.Equals(ident, "Body", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var it in ctx.Refs(sr.A.ElementAtOrDefault(3))) ctx.Item(it, M, part, 0);
            }
            if (part.T.Count == before) { R.NoBody++; continue; }
            // 세계 좌표(m) — 단위 · 지도 변환
            for (int i = 0; i < part.V.Count; i++) part.V[i] = ctx.ToWorld(part.V[i]);
            Weld(part);   // ★ 면마다 따로 넣은 꼭짓점을 한 점으로(0.1 mm) — 닫힌 껍질이 닫힌 메시가 되게(옆 DWG 솔리드 바꾸기 · 1110 키트 stifc BREP 실패)
            if (part.T.Count > 0) R.Parts.Add(part); else R.NoBody++;
        }
        return R;
    }

    /// <summary>꼭짓점 용접(0.1 mm 칸 · 이웃 칸까지) · 퇴화 세모(같은 점 둘) 뺌.</summary>
    static void Weld(Part p, double tol = 1e-4)
    {
        var map = new int[p.V.Count]; var nv = new List<Point3>(p.V.Count);
        var grid = new Dictionary<(long, long, long), List<int>>();
        for (int i = 0; i < p.V.Count; i++)
        {
            var v = p.V[i];
            long gx = (long)Math.Floor(v.X / tol), gy = (long)Math.Floor(v.Y / tol), gz = (long)Math.Floor(v.Z / tol);
            int hit = -1;
            for (long a = gx - 1; a <= gx + 1 && hit < 0; a++)
                for (long b = gy - 1; b <= gy + 1 && hit < 0; b++)
                    for (long c = gz - 1; c <= gz + 1 && hit < 0; c++)
                        if (grid.TryGetValue((a, b, c), out var l))
                            foreach (int k in l) { var w = nv[k]; if (Math.Abs(w.X - v.X) <= tol && Math.Abs(w.Y - v.Y) <= tol && Math.Abs(w.Z - v.Z) <= tol) { hit = k; break; } }
            if (hit < 0) { hit = nv.Count; nv.Add(v); if (!grid.TryGetValue((gx, gy, gz), out var l0)) grid[(gx, gy, gz)] = l0 = new(); l0.Add(hit); }
            map[i] = hit;
        }
        var nt = new List<(int, int, int)>(p.T.Count);
        foreach (var (a, b, c) in p.T) { int x = map[a], y = map[b], z = map[c]; if (x != y && y != z && x != z) nt.Add((x, y, z)); }
        // 세모가 쓰는 꼭짓점만 남김(구멍 다리 실패로 남은 점 · 안 쓴 좌표 — 옆 DWG 메시에 떠도는 점이 안 되게 · 검토 L)
        var used = new int[nv.Count]; Array.Fill(used, -1); var cv = new List<Point3>();
        int U(int i) { if (used[i] < 0) { used[i] = cv.Count; cv.Add(nv[i]); } return used[i]; }
        var ct = nt.Select(t => (U(t.Item1), U(t.Item2), U(t.Item3))).ToList();
        p.V.Clear(); p.V.AddRange(cv); p.T.Clear(); p.T.AddRange(ct);
    }

    // ───────────── 글 → 개체
    static Dictionary<int, Ent> Entities(string text, out string schema)
    {
        schema = "";
        var d = new Dictionary<int, Ent>();
        int i = 0, n = text.Length;
        int hs = text.IndexOf("FILE_SCHEMA", StringComparison.Ordinal);
        if (hs >= 0) { int q1 = text.IndexOf('\'', hs), q2 = q1 >= 0 ? text.IndexOf('\'', q1 + 1) : -1; if (q1 >= 0 && q2 > q1) schema = text[(q1 + 1)..q2]; }
        int ds = text.IndexOf("DATA;", StringComparison.Ordinal); if (ds >= 0) i = ds + 5;
        while (i < n)
        {
            int h = text.IndexOf('#', i); if (h < 0) break;
            int j = h + 1; while (j < n && char.IsDigit(text[j])) j++;
            if (j == h + 1) { i = h + 1; continue; }
            int id = int.Parse(text.AsSpan(h + 1, j - h - 1), CultureInfo.InvariantCulture);
            while (j < n && char.IsWhiteSpace(text[j])) j++;
            if (j >= n || text[j] != '=') { i = j; continue; }
            j++; while (j < n && char.IsWhiteSpace(text[j])) j++;
            int k = j; while (k < n && (char.IsLetterOrDigit(text[k]) || text[k] == '_')) k++;
            string name = text[j..k].ToUpperInvariant();
            int p = k; while (p < n && char.IsWhiteSpace(text[p])) p++;
            if (p >= n || text[p] != '(') { i = p; continue; }
            var e = new Ent { Name = name };
            var lst = ParseList(text, ref p);
            e.A = lst.L;
            d[id] = e;
            int semi = text.IndexOf(';', p); i = semi < 0 ? n : semi + 1;
        }
        return d;
    }

    /// <summary>p = '(' 자리 → 짝 ')' 다음.</summary>
    static VList ParseList(string t, ref int p, int depth = 0)
    {
        var L = new VList(); p++;
        if (depth > 256) throw new FormatException("IFC 괄호가 너무 깊음(256) — 깨진 파일");   // ★[설치 전 검토 L] 되부름 깊이 — 스택 넘침(잡을 수 없음) 막기
        int n = t.Length;
        while (p < n)
        {
            char c = t[p];
            if (char.IsWhiteSpace(c) || c == ',') { p++; continue; }
            if (c == ')') { p++; return L; }
            L.L.Add(ParseValue(t, ref p, depth + 1));
        }
        return L;
    }

    static V ParseValue(string t, ref int p, int depth = 0)
    {
        int n = t.Length; char c = t[p];
        if (c == '(') return ParseList(t, ref p, depth);
        if (c == '#') { int j = p + 1; while (j < n && char.IsDigit(t[j])) j++; var r = new VRef { Id = int.Parse(t.AsSpan(p + 1, j - p - 1), CultureInfo.InvariantCulture) }; p = j; return r; }
        if (c == '\'')
        {
            var sb = new StringBuilder(); int j = p + 1;
            while (j < n) { if (t[j] == '\'') { if (j + 1 < n && t[j + 1] == '\'') { sb.Append('\''); j += 2; continue; } break; } sb.Append(t[j]); j++; }
            p = j + 1; return new VStr { S = Decode(sb.ToString()) };
        }
        if (c == '.') { int j = t.IndexOf('.', p + 1); var r = new VEnum { S = t[(p + 1)..j] }; p = j + 1; return r; }
        if (c == '$' || c == '*') { p++; return new VNull(); }
        if (char.IsLetter(c))
        {
            int j = p; while (j < n && (char.IsLetterOrDigit(t[j]) || t[j] == '_')) j++;
            string nm = t[p..j].ToUpperInvariant(); p = j; while (p < n && char.IsWhiteSpace(t[p])) p++;
            var tv = new VTyped { Name = nm };
            if (p < n && t[p] == '(') { var l = ParseList(t, ref p, depth); tv.Arg = l.L.Count == 1 ? l.L[0] : l; }
            return tv;
        }
        {
            int j = p; while (j < n && "+-.0123456789Ee".IndexOf(t[j]) >= 0) j++;
            if (j == p) { p++; return new VNull(); }
            double.TryParse(t.AsSpan(p, j - p), NumberStyles.Float, CultureInfo.InvariantCulture, out double d);
            p = j; return new VNum { D = d };
        }
    }

    /// <summary>STEP 글자 — \X2\HHHH…\X0\ = UTF-16 · \X\HH = 8비트 · \S\c = 위 128.</summary>
    static string Decode(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(); int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '\\' && i + 3 < s.Length && s[i + 1] == 'X' && s[i + 2] == '2' && s[i + 3] == '\\')
            {
                int e = s.IndexOf("\\X0\\", i + 4, StringComparison.Ordinal); if (e < 0) e = s.Length;
                string hex = s[(i + 4)..e];
                for (int k = 0; k + 4 <= hex.Length; k += 4) if (int.TryParse(hex.AsSpan(k, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int u)) sb.Append((char)u);
                i = e + 4; continue;
            }
            if (s[i] == '\\' && i + 4 < s.Length && s[i + 1] == 'X' && s[i + 2] == '\\')
            {
                if (int.TryParse(s.AsSpan(i + 3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int u)) sb.Append((char)u);
                i += 5; continue;
            }
            if (s[i] == '\\' && i + 3 < s.Length && s[i + 1] == 'S' && s[i + 2] == '\\') { sb.Append((char)(s[i + 3] + 128)); i += 4; continue; }
            sb.Append(s[i]); i++;
        }
        return sb.ToString();
    }

    // ───────────── 행렬(4×3 — 회전 열 셋 + 이동)
    readonly struct M4
    {
        public readonly double Xx, Xy, Xz, Yx, Yy, Yz, Zx, Zy, Zz, Tx, Ty, Tz;
        public M4(double xx, double xy, double xz, double yx, double yy, double yz, double zx, double zy, double zz, double tx, double ty, double tz)
        { Xx = xx; Xy = xy; Xz = xz; Yx = yx; Yy = yy; Yz = yz; Zx = zx; Zy = zy; Zz = zz; Tx = tx; Ty = ty; Tz = tz; }
        public static readonly M4 I = new(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0);
        public Point3 Apply(double x, double y, double z) => new(Xx * x + Yx * y + Zx * z + Tx, Xy * x + Yy * y + Zy * z + Ty, Xz * x + Yz * y + Zz * z + Tz);
        public (double X, double Y, double Z) Dir(double x, double y, double z) => (Xx * x + Yx * y + Zx * z, Xy * x + Yy * y + Zy * z, Xz * x + Yz * y + Zz * z);
        /// <summary>this ∘ b (b를 먼저).</summary>
        public M4 Mul(M4 b)
        {
            var x = Dir(b.Xx, b.Xy, b.Xz); var y = Dir(b.Yx, b.Yy, b.Yz); var z = Dir(b.Zx, b.Zy, b.Zz); var t = Apply(b.Tx, b.Ty, b.Tz);
            return new M4(x.X, x.Y, x.Z, y.X, y.Y, y.Z, z.X, z.Y, z.Z, t.X, t.Y, t.Z);
        }
        public double Det => Xx * (Yy * Zz - Yz * Zy) - Yx * (Xy * Zz - Xz * Zy) + Zx * (Xy * Yz - Xz * Yy);
    }

    sealed class Ctx
    {
        readonly Dictionary<int, Ent> E; readonly Result R;
        readonly Dictionary<int, M4> plCache = new();
        double unit = 1;
        bool map; double mE, mN, mH, mCos = 1, mSin = 0, mScale = 1; bool mScaleSet;
        public Ctx(Dictionary<int, Ent> e, Result r) { E = e; R = r; }
        public Ent? Get(int id) => E.TryGetValue(id, out var e) ? e : null;
        public string Str(V? v) => v is VStr s ? s.S : v is VTyped t && t.Arg is VStr s2 ? s2.S : "";
        public double Num(V? v, double def = double.NaN) => v is VNum n ? n.D : v is VTyped t && t.Arg is VNum n2 ? n2.D : def;
        public IEnumerable<int> Refs(V? v) { if (v is VList l) foreach (var x in l.L) if (x is VRef r) yield return r.Id; }

        public double LengthUnit()
        {
            foreach (var e in E.Values)
            {
                if (e.Name == "IFCSIUNIT" && e.A.Count >= 4 && e.A[1] is VEnum k && k.S == "LENGTHUNIT")
                {
                    double f = e.A[2] is VEnum p ? p.S switch { "MILLI" => 1e-3, "CENTI" => 1e-2, "DECI" => 1e-1, "KILO" => 1e3, _ => 1 } : 1;
                    if (IsProjectUnit(e)) { unit = f; return f; }
                }
            }
            foreach (var e in E.Values)
                if (e.Name == "IFCCONVERSIONBASEDUNIT" && e.A.Count >= 4 && e.A[1] is VEnum k && k.S == "LENGTHUNIT" && e.A[3] is VRef mr && Get(mr.Id) is { } mw)
                {
                    double v = Num(mw.A.ElementAtOrDefault(0)); double b = 1;
                    if (mw.A.ElementAtOrDefault(1) is VRef br && Get(br.Id) is { Name: "IFCSIUNIT" } su && su.A.ElementAtOrDefault(2) is VEnum p) b = p.S == "MILLI" ? 1e-3 : p.S == "CENTI" ? 1e-2 : 1;
                    if (double.IsFinite(v)) { unit = v * b; return unit; }
                }
            return unit;
        }
        bool IsProjectUnit(Ent u)
        {
            // IfcUnitAssignment가 이 단위를 품는가(못 찾으면 첫 길이 단위)
            int id = E.First(kv => ReferenceEquals(kv.Value, u)).Key;
            foreach (var e in E.Values) if (e.Name == "IFCUNITASSIGNMENT" && Refs(e.A.ElementAtOrDefault(0)).Contains(id)) return true;
            return !E.Values.Any(e => e.Name == "IFCUNITASSIGNMENT");
        }

        public void ReadMap()
        {
            foreach (var e in E.Values)
            {
                if (e.Name != "IFCMAPCONVERSION" || e.A.Count < 6) continue;
                mE = Num(e.A[2], 0); mN = Num(e.A[3], 0); mH = Num(e.A[4], 0);
                double ax = Num(e.A.ElementAtOrDefault(5), 1), ay = Num(e.A.ElementAtOrDefault(6), 0), l = Math.Sqrt(ax * ax + ay * ay);
                if (l > 1e-12) { mCos = ax / l; mSin = ay / l; }
                double sc = Num(e.A.ElementAtOrDefault(7));
                if (double.IsFinite(sc) && sc > 0) { mScale = sc; mScaleSet = true; }
                map = true; R.MapConversion = true; return;
            }
        }

        /// <summary>프로젝트 단위 점 → 세계(m) — 지도 변환이 있으면 회전 · 축척 · 동 · 북 · 높이.</summary>
        public Point3 ToWorld(Point3 p)
        {
            if (!map) return new Point3(p.X * unit, p.Y * unit, p.Z * unit);
            double s = mScaleSet ? mScale : unit;
            double x = p.X * s, y = p.Y * s;
            return new Point3(mE + mCos * x - mSin * y, mN + mSin * x + mCos * y, mH + p.Z * (mScaleSet ? mScale : unit));
        }

        (double X, double Y, double Z) P3(int id)
        {
            var e = Get(id); if (e == null) return (0, 0, 0);
            if (e.A.ElementAtOrDefault(0) is VList l) return (Num(l.L.ElementAtOrDefault(0), 0), Num(l.L.ElementAtOrDefault(1), 0), Num(l.L.ElementAtOrDefault(2), 0));
            return (0, 0, 0);
        }
        (double X, double Y, double Z)? Dir3(V? v)
        {
            if (v is not VRef r || Get(r.Id) is not { } e || e.A.ElementAtOrDefault(0) is not VList l) return null;
            double x = Num(l.L.ElementAtOrDefault(0), 0), y = Num(l.L.ElementAtOrDefault(1), 0), z = Num(l.L.ElementAtOrDefault(2), 0), n = Math.Sqrt(x * x + y * y + z * z);
            return n < 1e-12 ? null : (x / n, y / n, z / n);
        }

        /// <summary>IfcAxis2Placement3D(Location, Axis, RefDirection) · 2D(Location, RefDirection).</summary>
        public M4 Axis(int id)
        {
            var e = Get(id); if (e == null) return M4.I;
            var loc = e.A.ElementAtOrDefault(0) is VRef lr ? P3(lr.Id) : (0, 0, 0);
            if (e.Name == "IFCAXIS2PLACEMENT2D")
            {
                var rd = Dir3(e.A.ElementAtOrDefault(1)) ?? (1, 0, 0);
                double l2 = Math.Sqrt(rd.X * rd.X + rd.Y * rd.Y); double cx = l2 > 1e-12 ? rd.X / l2 : 1, cy = l2 > 1e-12 ? rd.Y / l2 : 0;
                return new M4(cx, cy, 0, -cy, cx, 0, 0, 0, 1, loc.X, loc.Y, 0);
            }
            var z = Dir3(e.A.ElementAtOrDefault(1)) ?? (0, 0, 1);
            var x0 = Dir3(e.A.ElementAtOrDefault(2)) ?? (Math.Abs(z.Z) < 0.9 ? (0, 0, 1) : (1, 0, 0));
            if (Dir3(e.A.ElementAtOrDefault(2)) == null && Math.Abs(z.Z) < 0.9) x0 = (1, 0, 0);
            // X = RefDirection를 Z에 직교로
            double d = x0.X * z.X + x0.Y * z.Y + x0.Z * z.Z;
            double xx = x0.X - d * z.X, xy = x0.Y - d * z.Y, xz = x0.Z - d * z.Z, xl = Math.Sqrt(xx * xx + xy * xy + xz * xz);
            if (xl < 1e-9) { (xx, xy, xz) = Math.Abs(z.X) < 0.9 ? (1.0, 0.0, 0.0) : (0.0, 1.0, 0.0); d = xx * z.X + xy * z.Y + xz * z.Z; xx -= d * z.X; xy -= d * z.Y; xz -= d * z.Z; xl = Math.Sqrt(xx * xx + xy * xy + xz * xz); }
            xx /= xl; xy /= xl; xz /= xl;
            double yx = z.Y * xz - z.Z * xy, yy = z.Z * xx - z.X * xz, yz = z.X * xy - z.Y * xx;
            return new M4(xx, xy, xz, yx, yy, yz, z.X, z.Y, z.Z, loc.X, loc.Y, loc.Z);
        }

        /// <summary>IfcLocalPlacement(PlacementRelTo, RelativePlacement) 사슬.</summary>
        public M4 Placement(int id, int depth)
        {
            if (plCache.TryGetValue(id, out var c)) return c;
            var e = Get(id); if (e == null || depth > 64) return M4.I;
            var rel = e.A.ElementAtOrDefault(1) is VRef rr ? Axis(rr.Id) : M4.I;
            var m = e.A.ElementAtOrDefault(0) is VRef pr && Get(pr.Id)?.Name == "IFCLOCALPLACEMENT" ? Placement(pr.Id, depth + 1).Mul(rel) : rel;
            plCache[id] = m; return m;
        }

        /// <summary>IfcCartesianTransformationOperator3D(Axis1, Axis2, LocalOrigin, Scale, Axis3[, Scale2, Scale3]).</summary>
        M4 Operator(int id)
        {
            var e = Get(id); if (e == null) return M4.I;
            var a1 = Dir3(e.A.ElementAtOrDefault(0)); var a2 = Dir3(e.A.ElementAtOrDefault(1)); var a3 = Dir3(e.A.ElementAtOrDefault(4));
            var o = e.A.ElementAtOrDefault(2) is VRef or ? P3(or.Id) : (0, 0, 0);
            double s1 = Num(e.A.ElementAtOrDefault(3), 1); if (!double.IsFinite(s1)) s1 = 1;
            double s2 = e.Name.Contains("NONUNIFORM") ? Num(e.A.ElementAtOrDefault(5), s1) : s1, s3 = e.Name.Contains("NONUNIFORM") ? Num(e.A.ElementAtOrDefault(6), s1) : s1;
            var z = a3 ?? (0, 0, 1);
            var x = a1 ?? (Math.Abs(z.Z) < 0.9 ? (1, 0, 0) : (1, 0, 0));
            double d = x.X * z.X + x.Y * z.Y + x.Z * z.Z; double xx = x.X - d * z.X, xy = x.Y - d * z.Y, xz = x.Z - d * z.Z, xl = Math.Sqrt(xx * xx + xy * xy + xz * xz);
            if (xl < 1e-9)
            {
                // Axis1이 없고 Axis3가 X축에 가까움 — Axis()와 같이 다른 축으로(면으로 납작해지지 않게 · 검토 L)
                (xx, xy, xz) = Math.Abs(z.X) < 0.9 ? (1.0, 0.0, 0.0) : (0.0, 1.0, 0.0);
                d = xx * z.X + xy * z.Y + xz * z.Z; xx -= d * z.X; xy -= d * z.Y; xz -= d * z.Z; xl = Math.Sqrt(xx * xx + xy * xy + xz * xz);
            }
            xx /= xl; xy /= xl; xz /= xl;
            double yx = z.Y * xz - z.Z * xy, yy = z.Z * xx - z.X * xz, yz = z.X * xy - z.Y * xx;
            if (a2 is { } y2 && y2.X * yx + y2.Y * yy + y2.Z * yz < 0) { yx = -yx; yy = -yy; yz = -yz; }   // 거울(왼손) 연산자
            return new M4(xx * s1, xy * s1, xz * s1, yx * s2, yy * s2, yz * s2, z.X * s3, z.Y * s3, z.Z * s3, o.X, o.Y, o.Z);
        }

        void Skip(string k) { R.Skipped[k] = R.Skipped.TryGetValue(k, out int c) ? c + 1 : 1; }

        /// <summary>표현 항목 하나 → part(프로젝트 단위 · 세계 배치 M).</summary>
        public void Item(int id, M4 M, Part part, int depth)
        {
            var e = Get(id); if (e == null || depth > 16) return;
            switch (e.Name)
            {
                case "IFCFACETEDBREP": Shell(e.A.ElementAtOrDefault(0), M, part); break;
                case "IFCFACETEDBREPWITHVOIDS": Shell(e.A.ElementAtOrDefault(0), M, part); R.VoidShells += Refs(e.A.ElementAtOrDefault(1)).Count(); break;
                case "IFCSHELLBASEDSURFACEMODEL":
                case "IFCFACEBASEDSURFACEMODEL":
                    foreach (int s in Refs(e.A.ElementAtOrDefault(0))) Shell(new VRef { Id = s }, M, part); break;
                case "IFCTRIANGULATEDFACESET": TriSet(e, M, part); break;
                case "IFCPOLYGONALFACESET": PolySet(e, M, part); break;
                case "IFCEXTRUDEDAREASOLID": Extrude(e, M, part); break;
                case "IFCMAPPEDITEM":
                    {
                        if (e.A.ElementAtOrDefault(0) is not VRef mr || Get(mr.Id) is not { } map) break;
                        var origin = map.A.ElementAtOrDefault(0) is VRef ao ? Axis(ao.Id) : M4.I;
                        var target = e.A.ElementAtOrDefault(1) is VRef tr ? Operator(tr.Id) : M4.I;
                        var MM = M.Mul(target).Mul(origin);
                        if (map.A.ElementAtOrDefault(1) is VRef rep && Get(rep.Id) is { } sr)
                            foreach (int it in Refs(sr.A.ElementAtOrDefault(3))) Item(it, MM, part, depth + 1);
                        break;
                    }
                case "IFCBOOLEANCLIPPINGRESULT":
                case "IFCBOOLEANRESULT":
                    R.Booleans++;
                    if (e.A.ElementAtOrDefault(1) is VRef f1) Item(f1.Id, M, part, depth + 1);
                    break;
                default: Skip(e.Name); break;
            }
        }

        void Shell(V? sv, M4 M, Part part)
        {
            if (sv is not VRef sr || Get(sr.Id) is not { } sh) return;
            bool flip = M.Det < 0;
            foreach (int fid in Refs(sh.A.ElementAtOrDefault(0)))
            {
                var f = Get(fid); if (f == null) continue;
                var bounds = Refs(f.A.ElementAtOrDefault(0)).ToList();
                // 바깥 경계(IfcFaceOuterBound) 먼저 · 없으면 첫 경계 · 나머지 = 구멍
                int ob = bounds.FirstOrDefault(b => Get(b)?.Name == "IFCFACEOUTERBOUND"); if (ob == 0 && bounds.Count > 0) ob = bounds[0];
                List<(double X, double Y, double Z)>? Loop(int bid)
                {
                    var b0 = Get(bid); if (b0 == null) return null;
                    bool orient = !(b0.A.ElementAtOrDefault(1) is VEnum en && en.S == "F");
                    if (b0.A.ElementAtOrDefault(0) is not VRef lr || Get(lr.Id) is not { Name: "IFCPOLYLOOP" } loop) return null;
                    var pts = Refs(loop.A.ElementAtOrDefault(0)).Select(P3).ToList();
                    if (!orient) pts.Reverse();
                    return pts;
                }
                var outer = Loop(ob); if (outer == null) continue;
                var holes = bounds.Where(b => b != ob).Select(Loop).Where(l => l != null && l.Count >= 3).Select(l => l!).ToList();
                Poly(outer, M, part, flip, holes);
            }
        }

        /// <summary>평면 다각형(구멍 포함) → 세모 — 법선 방향으로 2D에 눕혀 구멍을 다리로 이은 뒤 귀 자르기 · 못 하면 바깥만 부채꼴(+구멍 면 셈).</summary>
        void Poly(List<(double X, double Y, double Z)> pts, M4 M, Part part, bool flip, List<List<(double X, double Y, double Z)>>? holes = null)
        {
            static void Trim(List<(double X, double Y, double Z)> l) { if (l.Count >= 2 && Math.Abs(l[0].X - l[^1].X) + Math.Abs(l[0].Y - l[^1].Y) + Math.Abs(l[0].Z - l[^1].Z) < 1e-12) l.RemoveAt(l.Count - 1); }
            Trim(pts); if (pts.Count < 3) return;
            var all = new List<(double X, double Y, double Z)>(pts);
            var loops = new List<List<int>> { Enumerable.Range(0, pts.Count).ToList() };
            if (holes != null) foreach (var h in holes) { Trim(h); if (h.Count < 3) continue; loops.Add(Enumerable.Range(all.Count, h.Count).ToList()); all.AddRange(h); }
            int b = part.V.Count;
            foreach (var p in all) part.V.Add(M.Apply(p.X, p.Y, p.Z));
            List<(int, int, int)>? tri = loops.Count == 1 ? Fan3D(pts) : Holed(all, loops);
            if (tri == null) { R.HoleFaces++; tri = Fan3D(pts); }
            foreach (var (a, c, d) in tri) { if (flip) part.T.Add((b + a, b + d, b + c)); else part.T.Add((b + a, b + c, b + d)); }
        }

        /// <summary>구멍 있는 평면 다각형 — 2D로 눕혀(바깥 반시계 · 구멍 시계) 구멍마다 오른쪽 끝 점에서 +U로 쏜 다리를 바깥(이미 이은 고리)에 이어 한 고리 → 귀 자르기. 못 하면 null.</summary>
        static List<(int, int, int)>? Holed(List<(double X, double Y, double Z)> p, List<List<int>> loops)
        {
            double nx = 0, ny = 0, nz = 0; var o0 = loops[0];
            for (int i = 0; i < o0.Count; i++) { var a = p[o0[i]]; var c = p[o0[(i + 1) % o0.Count]]; nx += (a.Y - c.Y) * (a.Z + c.Z); ny += (a.Z - c.Z) * (a.X + c.X); nz += (a.X - c.X) * (a.Y + c.Y); }
            double ax = Math.Abs(nx), ay = Math.Abs(ny), az = Math.Abs(nz);
            var q = p.Select(v => az >= ax && az >= ay ? (v.X, nz >= 0 ? v.Y : -v.Y) : ax >= ay ? (v.Y, nx >= 0 ? v.Z : -v.Z) : (v.Z, ny >= 0 ? v.X : -v.X)).Select(t => (U: t.Item1, W: t.Item2)).ToList();
            double Area(List<int> l) { double s = 0; for (int i = 0; i < l.Count; i++) { var a = q[l[i]]; var c = q[l[(i + 1) % l.Count]]; s += a.U * c.W - c.U * a.W; } return s / 2; }
            var ring = new List<int>(loops[0]); if (Area(ring) < 0) ring.Reverse();
            var hs = loops.Skip(1).Select(h => { var l = new List<int>(h); if (Area(l) > 0) l.Reverse(); return l; }).OrderByDescending(h => h.Max(i => q[i].U)).ToList();
            foreach (var h in hs)
            {
                int mi = h.OrderByDescending(i => q[i].U).First(); var Mq = q[mi];
                // +U 반직선이 지금 고리와 처음 만나는 변 → 그 변의 U 큰 끝을 다리 끝으로
                double best = double.PositiveInfinity; int bk = -1;
                for (int k = 0; k < ring.Count; k++)
                {
                    var A = q[ring[k]]; var B = q[ring[(k + 1) % ring.Count]];
                    if ((A.W > Mq.W) == (B.W > Mq.W)) continue;
                    double t = (Mq.W - A.W) / (B.W - A.W), u = A.U + t * (B.U - A.U);
                    if (u >= Mq.U - 1e-12 && u < best) { best = u; bk = A.U >= B.U ? k : (k + 1) % ring.Count; }
                }
                if (bk < 0) return null;
                int hm = h.IndexOf(mi);
                var ins = new List<int>();
                for (int k = 0; k <= h.Count; k++) ins.Add(h[(hm + k) % h.Count]);   // 구멍 한 바퀴 + 다시 M
                ins.Add(ring[bk]);                                                   // 다리 끝으로 돌아옴
                ring.InsertRange(bk + 1, ins);
            }
            var qq = ring.Select(i => q[i]).ToList();
            var t2 = EarDup(qq); if (t2 == null) return null;
            return t2.Select(t => (ring[t.Item1], ring[t.Item2], ring[t.Item3])).ToList();
        }

        /// <summary>다리로 이은 고리(같은 점이 두 번 나옴)도 받는 귀 자르기 — 같은 자리 점은 «안에 든 점»으로 안 봄.</summary>
        static List<(int, int, int)>? EarDup(List<(double U, double W)> q)
        {
            int n = q.Count; var idx = Enumerable.Range(0, n).ToList(); var o = new List<(int, int, int)>();
            static double Cr((double U, double W) a, (double U, double W) b, (double U, double W) c) => (b.U - a.U) * (c.W - a.W) - (b.W - a.W) * (c.U - a.U);
            static bool Same((double U, double W) a, (double U, double W) b) => Math.Abs(a.U - b.U) < 1e-12 && Math.Abs(a.W - b.W) < 1e-12;
            int guard = 0;
            while (idx.Count > 3 && guard++ < 20 * n)
            {
                bool cut = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int ia = idx[(k - 1 + idx.Count) % idx.Count], ib = idx[k], ic = idx[(k + 1) % idx.Count];
                    var A = q[ia]; var B = q[ib]; var C = q[ic];
                    if (Cr(A, B, C) <= 1e-18) continue;
                    bool inside = false;
                    foreach (int m in idx)
                    {
                        if (m == ia || m == ib || m == ic) continue;
                        var P = q[m]; if (Same(P, A) || Same(P, B) || Same(P, C)) continue;
                        if (Cr(A, B, P) >= 0 && Cr(B, C, P) >= 0 && Cr(C, A, P) >= 0) { inside = true; break; }
                    }
                    if (inside) continue;
                    o.Add((ia, ib, ic)); idx.RemoveAt(k); cut = true; break;
                }
                if (!cut) return null;
            }
            if (idx.Count == 3) o.Add((idx[0], idx[1], idx[2]));
            return o;
        }

        /// <summary>평면 다각형 세모 — 법선 방향으로 2D에 눕혀 귀 자르기(오목도). 실패하면 부채꼴.</summary>
        static List<(int, int, int)> Fan3D(List<(double X, double Y, double Z)> p)
        {
            int n = p.Count; var o = new List<(int, int, int)>();
            if (n == 3) { o.Add((0, 1, 2)); return o; }
            // 뉴웰 법선
            double nx = 0, ny = 0, nz = 0;
            for (int i = 0; i < n; i++) { var a = p[i]; var c = p[(i + 1) % n]; nx += (a.Y - c.Y) * (a.Z + c.Z); ny += (a.Z - c.Z) * (a.X + c.X); nz += (a.X - c.X) * (a.Y + c.Y); }
            double ax = Math.Abs(nx), ay = Math.Abs(ny), az = Math.Abs(nz);
            var q = new List<(double U, double W)>(n);
            foreach (var v in p) q.Add(az >= ax && az >= ay ? (v.X, nz >= 0 ? v.Y : -v.Y) : ax >= ay ? (v.Y, nx >= 0 ? v.Z : -v.Z) : (v.Z, ny >= 0 ? v.X : -v.X));
            var r = Ear(q);
            if (r != null) return r;
            for (int i = 1; i + 1 < n; i++) o.Add((0, i, i + 1));
            return o;
        }

        /// <summary>귀 자르기(반시계 2D) — 못 하면 null.</summary>
        public static List<(int, int, int)>? Ear(List<(double U, double W)> q)
        {
            int n = q.Count; var idx = Enumerable.Range(0, n).ToList(); var o = new List<(int, int, int)>();
            double area = 0; for (int i = 0; i < n; i++) { var a = q[i]; var b = q[(i + 1) % n]; area += a.U * b.W - b.U * a.W; }
            if (Math.Abs(area) < 1e-18) return null;
            if (area < 0) idx.Reverse();
            static double Cr((double U, double W) a, (double U, double W) b, (double U, double W) c) => (b.U - a.U) * (c.W - a.W) - (b.W - a.W) * (c.U - a.U);
            int guard = 0;
            while (idx.Count > 3 && guard++ < 10 * n)
            {
                bool cut = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int ia = idx[(k - 1 + idx.Count) % idx.Count], ib = idx[k], ic = idx[(k + 1) % idx.Count];
                    var A = q[ia]; var B = q[ib]; var C = q[ic];
                    if (Cr(A, B, C) <= 1e-18) continue;
                    bool inside = false;
                    foreach (int m in idx)
                    {
                        if (m == ia || m == ib || m == ic) continue;
                        var P = q[m];
                        if (Cr(A, B, P) >= 0 && Cr(B, C, P) >= 0 && Cr(C, A, P) >= 0) { inside = true; break; }
                    }
                    if (inside) continue;
                    o.Add((ia, ib, ic)); idx.RemoveAt(k); cut = true; break;
                }
                if (!cut) return null;
            }
            if (idx.Count == 3) o.Add((idx[0], idx[1], idx[2]));
            return o;
        }

        List<(double X, double Y, double Z)> CoordList(V? v)
        {
            var o = new List<(double, double, double)>();
            if (v is not VRef r || Get(r.Id) is not { } cl || cl.A.ElementAtOrDefault(0) is not VList l) return o;
            foreach (var x in l.L) if (x is VList c) o.Add((Num(c.L.ElementAtOrDefault(0), 0), Num(c.L.ElementAtOrDefault(1), 0), Num(c.L.ElementAtOrDefault(2), 0)));
            return o;
        }

        void TriSet(Ent e, M4 M, Part part)
        {
            // IFC4: (Coordinates, Normals, Closed, CoordIndex, PnIndex) · IFC4x3: (Coordinates, Closed, CoordIndex, Normals, PnIndex) — 목록의 목록(셋씩)인 첫 칸을 색인으로
            var co = CoordList(e.A.ElementAtOrDefault(0)); bool flip = M.Det < 0;
            VList? ci = null; VList? pn = null;
            // 색인 = 정수 셋씩의 목록(법선은 실수 — IFC4X3 차례에서도 헷갈리지 않게 · 검토 L)
            static bool IntTriples(VList l) => l.L.Count > 0 && l.L.All(x => x is VList t && t.L.Count == 3 && t.L.All(q => q is VNum n && Math.Abs(n.D - Math.Round(n.D)) < 1e-9 && n.D >= 1));
            for (int k = 1; k < e.A.Count; k++) if (e.A[k] is VList l && ci == null && IntTriples(l)) ci = l;
            for (int k = 1; k < e.A.Count; k++) if (e.A[k] is VList l && l.L.Count > 0 && l.L[0] is VNum && l != ci) pn = l;
            if (ci == null) { Skip("IFCTRIANGULATEDFACESET(색인 없음)"); return; }
            int b = part.V.Count;
            foreach (var p in co) part.V.Add(M.Apply(p.X, p.Y, p.Z));
            int Map(int i) { if (pn != null && i >= 1 && i - 1 < pn.L.Count) i = (int)Num(pn.L[i - 1], i); return b + i - 1; }
            foreach (var t in ci.L) if (t is VList tl && tl.L.Count == 3)
                {
                    int a = Map((int)Num(tl.L[0], 1)), c = Map((int)Num(tl.L[1], 1)), d = Map((int)Num(tl.L[2], 1));
                    if (a < b || c < b || d < b || a >= part.V.Count || c >= part.V.Count || d >= part.V.Count) continue;
                    part.T.Add(flip ? (a, d, c) : (a, c, d));
                }
        }

        void PolySet(Ent e, M4 M, Part part)
        {
            var co = CoordList(e.A.ElementAtOrDefault(0)); bool flip = M.Det < 0;
            foreach (int fid in Refs(e.A.ElementAtOrDefault(2)))
            {
                var f = Get(fid); if (f == null || f.A.ElementAtOrDefault(0) is not VList il) continue;
                if (f.Name == "IFCINDEXEDPOLYGONALFACEWITHVOIDS") R.HoleFaces++;
                var pts = il.L.Select(x => (int)Num(x, 0) - 1).Where(i => i >= 0 && i < co.Count).Select(i => co[i]).ToList();
                Poly(pts, M, part, flip);
            }
        }

        void Extrude(Ent e, M4 M, Part part)
        {
            // (SweptArea, Position, ExtrudedDirection, Depth)
            var pf = e.A.ElementAtOrDefault(0) is VRef pr ? Profile(pr.Id) : null;
            if (pf == null || pf.Value.Outer.Count < 3) return;
            var (outer, holes) = pf.Value;
            var P = e.A.ElementAtOrDefault(1) is VRef ps ? Axis(ps.Id) : M4.I;
            var dir = Dir3(e.A.ElementAtOrDefault(2)) ?? (0, 0, 1);
            double depth = Num(e.A.ElementAtOrDefault(3), 0);
            if (!(depth > 0)) return;
            var MM = M.Mul(P); bool flip = MM.Det < 0;
            static double Ar(List<(double U, double W)> l) { double s = 0; for (int i = 0; i < l.Count; i++) { var a = l[i]; var c = l[(i + 1) % l.Count]; s += a.U * c.W - c.U * a.W; } return s; }
            if (Ar(outer) < 0) outer.Reverse();
            foreach (var h in holes) if (Ar(h) > 0) h.Reverse();
            // 뚜껑 세모(구멍 포함 — 2D 점 목록 차례: 바깥 · 구멍들)
            var flat = new List<(double X, double Y, double Z)>(); var loops = new List<List<int>>();
            void AddLoop(List<(double U, double W)> l) { loops.Add(Enumerable.Range(flat.Count, l.Count).ToList()); foreach (var p in l) flat.Add((p.U, p.W, 0)); }
            AddLoop(outer); foreach (var h in holes) AddLoop(h);
            var cap = loops.Count == 1 ? Ear(outer) : Holed(flat, loops);
            if (cap == null) { if (loops.Count > 1) R.ProfileVoids += loops.Count - 1; cap = Ear(outer) ?? Enumerable.Range(1, outer.Count - 2).Select(i => (0, i, i + 1)).ToList(); loops = new List<List<int>> { loops[0] }; }
            int n = flat.Count, b = part.V.Count;
            foreach (var p in flat) part.V.Add(MM.Apply(p.X, p.Y, 0));
            foreach (var p in flat) part.V.Add(MM.Apply(p.X + dir.X * depth, p.Y + dir.Y * depth, dir.Z * depth));
            bool up = dir.Z >= 0;   // 바닥은 아래로 · 뚜껑은 위로 보게
            void T(int a, int c, int d) { if (flip) part.T.Add((a, d, c)); else part.T.Add((a, c, d)); }
            foreach (var (a, c, d) in cap) { if (up) { T(b + a, b + d, b + c); T(b + n + a, b + n + c, b + n + d); } else { T(b + a, b + c, b + d); T(b + n + a, b + n + d, b + n + c); } }
            // 옆면 — 고리마다(바깥 반시계 · 구멍 시계 → 같은 식으로 바깥을 봄)
            foreach (var lp in loops)
                for (int k = 0; k < lp.Count; k++)
                {
                    int i = lp[k], j = lp[(k + 1) % lp.Count];
                    if (up) { T(b + i, b + j, b + n + j); T(b + i, b + n + j, b + n + i); } else { T(b + i, b + n + j, b + j); T(b + i, b + n + i, b + n + j); }
                }
        }

        /// <summary>단면 — 바깥 고리 · 구멍 고리들(단면 좌표).</summary>
        (List<(double U, double W)> Outer, List<List<(double U, double W)>> Holes)? Profile(int id)
        {
            var e = Get(id); if (e == null) return null;
            var none = new List<List<(double U, double W)>>();
            List<(double U, double W)> Place(int posArg, IEnumerable<(double, double)> pts)
            {
                var P = e.A.ElementAtOrDefault(posArg) is VRef ps ? Axis(ps.Id) : M4.I;
                return pts.Select(q => { var t = P.Apply(q.Item1, q.Item2, 0); return (t.X, t.Y); }).ToList();
            }
            static IEnumerable<(double, double)> Circle(double r, int k = 32) => Enumerable.Range(0, k).Select(i => (r * Math.Cos(2 * Math.PI * i / k), r * Math.Sin(2 * Math.PI * i / k)));
            double A(int k) => Num(e.A.ElementAtOrDefault(k), 0);
            switch (e.Name)
            {
                case "IFCARBITRARYCLOSEDPROFILEDEF":
                    return e.A.ElementAtOrDefault(2) is VRef cr && Curve2D(cr.Id) is { } c0 ? (c0, none) : null;
                case "IFCARBITRARYPROFILEDEFWITHVOIDS":
                    {
                        if (e.A.ElementAtOrDefault(2) is not VRef cr2 || Curve2D(cr2.Id) is not { } c1) return null;
                        var hs = Refs(e.A.ElementAtOrDefault(3)).Select(Curve2D).Where(h => h != null && h.Count >= 3).Select(h => h!).ToList();
                        return (c1, hs);
                    }
                case "IFCRECTANGLEPROFILEDEF":
                    { double hx = A(3) / 2, hy = A(4) / 2; if (!(hx > 0 && hy > 0)) return null; return (Place(2, new[] { (-hx, -hy), (hx, -hy), (hx, hy), (-hx, hy) }), none); }
                case "IFCRECTANGLEHOLLOWPROFILEDEF":
                    {
                        double hx = A(3) / 2, hy = A(4) / 2, t = A(5); if (!(hx > 0 && hy > 0)) return null;
                        var o = Place(2, new[] { (-hx, -hy), (hx, -hy), (hx, hy), (-hx, hy) });
                        return t > 0 && t < Math.Min(hx, hy) ? (o, new List<List<(double U, double W)>> { Place(2, new[] { (-hx + t, -hy + t), (-hx + t, hy - t), (hx - t, hy - t), (hx - t, -hy + t) }) }) : (o, none);
                    }
                case "IFCCIRCLEPROFILEDEF":
                    { double r = A(3); return r > 0 ? (Place(2, Circle(r)), none) : null; }
                case "IFCCIRCLEHOLLOWPROFILEDEF":
                    {
                        double r = A(3), t = A(4); if (!(r > 0)) return null;
                        return t > 0 && t < r ? (Place(2, Circle(r)), new List<List<(double U, double W)>> { Place(2, Circle(r - t).Reverse()) }) : (Place(2, Circle(r)), none);
                    }
                case "IFCELLIPSEPROFILEDEF":
                    { double a = A(3), c = A(4); return a > 0 && c > 0 ? (Place(2, Enumerable.Range(0, 32).Select(i => (a * Math.Cos(2 * Math.PI * i / 32), c * Math.Sin(2 * Math.PI * i / 32)))), none) : null; }
                case "IFCISHAPEPROFILEDEF":
                case "IFCASYMMETRICISHAPEPROFILEDEF":
                    {
                        // (…, Position, OverallWidth, OverallDepth, WebThickness, FlangeThickness, …) — 모서리 둥글기 없음
                        double w = A(3) / 2, d = A(4) / 2, tw = A(5) / 2, tf = A(6); if (!(w > 0 && d > 0 && tw > 0 && tf > 0)) return null;
                        return (Place(2, new[] { (-w, -d), (w, -d), (w, -d + tf), (tw, -d + tf), (tw, d - tf), (w, d - tf), (w, d), (-w, d), (-w, d - tf), (-tw, d - tf), (-tw, -d + tf), (-w, -d + tf) }), none);
                    }
                case "IFCTSHAPEPROFILEDEF":
                    {
                        // (…, Position, Depth, FlangeWidth, WebThickness, FlangeThickness, …) — 플랜지 위
                        double d = A(3) / 2, w = A(4) / 2, tw = A(5) / 2, tf = A(6); if (!(w > 0 && d > 0 && tw > 0 && tf > 0)) return null;
                        return (Place(2, new[] { (-tw, -d), (tw, -d), (tw, d - tf), (w, d - tf), (w, d), (-w, d), (-w, d - tf), (-tw, d - tf) }), none);
                    }
                case "IFCLSHAPEPROFILEDEF":
                    {
                        // (…, Position, Depth, Width, Thickness, …) — 무게중심 무시(바깥 상자 가운데)
                        double d = A(3), w = A(4) > 0 ? A(4) : A(3), t = A(5); if (!(d > 0 && w > 0 && t > 0)) return null;
                        double x0 = -w / 2, y0 = -d / 2;
                        return (Place(2, new[] { (x0, y0), (x0 + w, y0), (x0 + w, y0 + t), (x0 + t, y0 + t), (x0 + t, y0 + d), (x0, y0 + d) }), none);
                    }
                case "IFCUSHAPEPROFILEDEF":
                    {
                        // (…, Position, Depth, FlangeWidth, WebThickness, FlangeThickness, …)
                        double d = A(3) / 2, w = A(4), tw = A(5), tf = A(6); if (!(d > 0 && w > 0 && tw > 0 && tf > 0)) return null;
                        double x0 = -w / 2;
                        return (Place(2, new[] { (x0, -d), (x0 + w, -d), (x0 + w, -d + tf), (x0 + tw, -d + tf), (x0 + tw, d - tf), (x0 + w, d - tf), (x0 + w, d), (x0, d) }), none);
                    }
                case "IFCCSHAPEPROFILEDEF":
                    {
                        // (…, Position, Depth, Width, WallThickness, Girth, …)
                        double d = A(3) / 2, w = A(4), t = A(5), g = A(6); if (!(d > 0 && w > 0 && t > 0)) return null;
                        if (!(t < d)) { Skip("IFCCSHAPEPROFILEDEF(두께 > 높이)"); return null; }   // 깨진 단면 — 그 단면만 뺌(Clamp 예외로 IFC 전체 실패 막기 · 검토 L)
                        double x0 = -w / 2; g = Math.Clamp(g, t, d);
                        return (Place(2, new[] { (x0, -d), (x0 + w, -d), (x0 + w, -d + g), (x0 + w - t, -d + g), (x0 + w - t, -d + t), (x0 + t, -d + t), (x0 + t, d - t), (x0 + w - t, d - t), (x0 + w - t, d - g), (x0 + w, d - g), (x0 + w, d), (x0, d) }), none);
                    }
                default: Skip(e.Name); return null;
            }
        }

        List<(double U, double W)>? Curve2D(int id)
        {
            var e = Get(id); if (e == null) return null;
            if (e.Name == "IFCPOLYLINE")
            {
                var l = Refs(e.A.ElementAtOrDefault(0)).Select(P3).Select(p => (p.X, p.Y)).ToList();
                if (l.Count >= 2 && Math.Abs(l[0].X - l[^1].X) + Math.Abs(l[0].Y - l[^1].Y) < 1e-12) l.RemoveAt(l.Count - 1);
                return l;
            }
            if (e.Name == "IFCINDEXEDPOLYCURVE" && e.A.ElementAtOrDefault(0) is VRef pl && Get(pl.Id) is { } cl && cl.A.ElementAtOrDefault(0) is VList ll)
            {
                var l = ll.L.OfType<VList>().Select(c => (Num(c.L.ElementAtOrDefault(0), 0), Num(c.L.ElementAtOrDefault(1), 0))).ToList();
                if (l.Count >= 2 && Math.Abs(l[0].Item1 - l[^1].Item1) + Math.Abs(l[0].Item2 - l[^1].Item2) < 1e-12) l.RemoveAt(l.Count - 1);
                if (e.A.ElementAtOrDefault(1) is VList) Skip("IFCINDEXEDPOLYCURVE(호 · 토막 — 꼭짓점만)");
                return l;
            }
            if (e.Name == "IFCCOMPOSITECURVE")
            {
                // (Segments, SelfIntersect) · 토막 = IfcCompositeCurveSegment(Transition, SameSense, ParentCurve)
                var l = new List<(double U, double W)>();
                foreach (int sid in Refs(e.A.ElementAtOrDefault(0)))
                {
                    var sg = Get(sid); if (sg == null || sg.A.ElementAtOrDefault(2) is not VRef pc) continue;
                    var part = Open2D(pc.Id); if (part == null) { Skip("IFCCOMPOSITECURVE 토막 " + (Get(pc.Id)?.Name ?? "?")); return null; }
                    if (sg.A.ElementAtOrDefault(1) is VEnum ss && ss.S == "F") part.Reverse();
                    foreach (var p in part) if (l.Count == 0 || Math.Abs(l[^1].U - p.U) + Math.Abs(l[^1].W - p.W) > 1e-9) l.Add(p);
                }
                if (l.Count >= 2 && Math.Abs(l[0].U - l[^1].U) + Math.Abs(l[0].W - l[^1].W) < 1e-9) l.RemoveAt(l.Count - 1);
                return l.Count >= 3 ? l : null;
            }
            if (e.Name == "IFCTRIMMEDCURVE" && Open2D(id) is { } tc && tc.Count >= 3) return tc;
            Skip(e.Name + "(단면 곡선)");
            return null;
        }

        /// <summary>열린 2D 곡선 토막 — IfcPolyline · IfcTrimmedCurve(IfcCircle · IfcEllipse · IfcLine 바탕 · 데카르트 또는 매개변수 자르기) — 원호는 5°마다.</summary>
        List<(double U, double W)>? Open2D(int id)
        {
            var e = Get(id); if (e == null) return null;
            if (e.Name == "IFCPOLYLINE") return Refs(e.A.ElementAtOrDefault(0)).Select(P3).Select(p => (U: p.X, W: p.Y)).ToList();
            if (e.Name != "IFCTRIMMEDCURVE" || e.A.ElementAtOrDefault(0) is not VRef bcr || Get(bcr.Id) is not { } bc) return null;
            bool sense = !(e.A.ElementAtOrDefault(3) is VEnum se && se.S == "F");
            (double U, double W)? TrimPt(V? v) { if (v is VList tl) foreach (var x in tl.L) if (x is VRef r && Get(r.Id)?.Name == "IFCCARTESIANPOINT") { var p = P3(r.Id); return (p.X, p.Y); } return null; }
            double? TrimPar(V? v) { if (v is VList tl) foreach (var x in tl.L) if (x is VTyped t && t.Name == "IFCPARAMETERVALUE" && t.Arg is VNum n) return n.D; return null; }
            var p1 = TrimPt(e.A.ElementAtOrDefault(1)); var p2 = TrimPt(e.A.ElementAtOrDefault(2));
            if (bc.Name == "IFCLINE")
            {
                if (p1 != null && p2 != null) return new List<(double U, double W)> { p1.Value, p2.Value };
                return null;
            }
            if (bc.Name is "IFCCIRCLE" or "IFCELLIPSE")
            {
                var P = bc.A.ElementAtOrDefault(0) is VRef ps ? Axis(ps.Id) : M4.I;
                double ra = Num(bc.A.ElementAtOrDefault(1), 0), rb = bc.Name == "IFCELLIPSE" ? Num(bc.A.ElementAtOrDefault(2), ra) : ra;
                if (!(ra > 0 && rb > 0)) return null;
                // 원 자리(국소) 각 — 데카르트 자르기를 먼저 · 없으면 매개변수(평면각 단위)
                double Ang((double U, double W) q) { double dx = q.U - P.Tx, dy = q.W - P.Ty; double lx = dx * P.Xx + dy * P.Xy, ly = dx * P.Yx + dy * P.Yy; return Math.Atan2(ly / rb, lx / ra); }
                double a1, a2;
                if (p1 != null && p2 != null) { a1 = Ang(p1.Value); a2 = Ang(p2.Value); }
                else if (TrimPar(e.A.ElementAtOrDefault(1)) is { } t1 && TrimPar(e.A.ElementAtOrDefault(2)) is { } t2) { a1 = t1 * AngleUnit(); a2 = t2 * AngleUnit(); }
                else return null;
                if (!double.IsFinite(a1) || !double.IsFinite(a2)) return null;   // ★[설치 전 검토 L] 무한 매개변수 — 끝없는 고리 막기
                double sweep = sense ? a2 - a1 : a1 - a2;
                sweep %= 2 * Math.PI; if (sweep <= 1e-12) sweep += 2 * Math.PI;
                int k = Math.Max(2, (int)Math.Ceiling(sweep / (5 * Math.PI / 180)));
                var o = new List<(double U, double W)>(k + 1);
                for (int i = 0; i <= k; i++)
                {
                    double a = a1 + (sense ? 1 : -1) * sweep * i / k;
                    var t = P.Apply(ra * Math.Cos(a), rb * Math.Sin(a), 0); o.Add((t.X, t.Y));
                }
                if (p1 != null) o[0] = p1.Value; if (p2 != null) o[^1] = p2.Value;
                return o;
            }
            return null;
        }

        double? angleUnit;
        /// <summary>평면각 단위(라디안 배수) — IfcSIUnit PLANEANGLEUNIT = 1 · IfcConversionBasedUnit(DEGREE) = π/180 · 없으면 라디안.</summary>
        double AngleUnit()
        {
            if (angleUnit is { } u) return u;
            u = 1;
            foreach (var e in E.Values)
                if (e.Name == "IFCCONVERSIONBASEDUNIT" && e.A.ElementAtOrDefault(1) is VEnum k && k.S == "PLANEANGLEUNIT" && e.A.ElementAtOrDefault(3) is VRef mr && Get(mr.Id) is { } mw)
                { double v = Num(mw.A.ElementAtOrDefault(0)); if (double.IsFinite(v) && v > 0) u = v; break; }
            angleUnit = u; return u;
        }
    }
}
