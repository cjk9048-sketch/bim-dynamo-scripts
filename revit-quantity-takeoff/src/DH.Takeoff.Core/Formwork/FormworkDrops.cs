using System.Globalization;

namespace DH.Takeoff.Core.Formwork;

/// <summary>
/// 사람이 ② 3D 에서 뺀 판의 조각 하나를 다시 알아보는 열쇠(R48 — JACK 2026-10-16 «찍어서 빼기 · 판 한 장»).
/// 면 차례 · 면 참조에 기대지 않고 **모양**으로 적는다(core · revit R48 계획 검토 M2 — 면 차례는 결합을 바꾸면 다시 짜일 수 있고,
/// 면 참조는 지금 읽지 않는다): 부재 Id · 면의 바깥 법선 · 평면 자리(법선 · 점) · 조각 안의 대표 점 · 넓이.
/// 대표 점 = 조각의 넓이 중심(core-verify R48 L3 — 세모의 넓이로 잰 것)이 조각 위면 그것, 아니면(가운데에 구멍이 난 벽 · ㄷ자 조각) 가장 큰 세모의 무게 중심 —
/// 늘 조각 위의 점이라, 모양이 그대로면 제 조각을 반드시 다시 찾는다.
/// 맞추기 = 같은 부재 · 같은 평면(법선이 같고 평면 거리 5 mm 안 — 빠른 걸러내기: 다음의 품기 검사가 세모 평면에서 3 mm 안만 받아 실효는 3 mm) ·
/// 지금 조각이 대표 점을 품음(3 mm 여유) · 넓이 비 ¼ ~ 4 — 이웃이 바뀌어(나중에 침 · 결합) 조각이 커지거나 줄어도 따라간다.
/// 곡면(법선 없음)은 같은 부재 · 지금 조각이 대표 점을 1 cm 안에 품음.
/// 맞출 조각이 없어진 열쇠 · 넓이가 크게 달라진 판은 화면 · 알림에 보인다(FormworkSummary.CheckDrops).
/// </summary>
public readonly record struct DropRecord(long Element, double Nx, double Ny, double Nz, double D, double Cx, double Cy, double Cz, double Area)
{
    private const double PlaneTol = 0.005;     // 같은 평면으로 보는 거리(m) — 빠른 걸러내기(실효는 ContainTol)
    private const double ContainTol = 0.003;   // 대표 점이 조각의 테두리 밖으로 이만큼까지는 품은 것으로(m)
    private const double CurvedTol = 0.01;     // 곡면 · 세모 없는 조각: 대표 점이 이 안이면 같은 조각(m)

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>곡면 조각의 열쇠인가(법선이 없다).</summary>
    public bool Curved => Nx == 0 && Ny == 0 && Nz == 0;

    /// <summary>지금 조각의 열쇠.</summary>
    public static DropRecord Of(FaceResult face, FacePatch patch)
    {
        var (x, y, z) = PatchGeometry.Anchor(patch);
        if (face.Normal.Length != 3) return new DropRecord(face.ElementId, 0, 0, 0, 0, x, y, z, patch.Area);
        double nx = face.Normal[0], ny = face.Normal[1], nz = face.Normal[2];
        return new DropRecord(face.ElementId, nx, ny, nz, nx * x + ny * y + nz * z, x, y, z, patch.Area);
    }

    /// <summary>이 열쇠가 그 조각을 가리키는가.</summary>
    public bool Matches(FaceResult face, FacePatch patch)
    {
        if (face.ElementId != Element || !(patch.Area > 0)) return false;
        // 넓이 비: 적힌 넓이가 0 이면(손으로 고친 파일) 건너뛴다 — 넓이는 열째 자리까지 적으니 분석이 남기는 조각(1e-6 ㎡ 넘게)은 0 으로 적히지 않는다(core-verify R48 M2).
        if (Area > 0)
        {
            double ratio = patch.Area / Area;
            if (ratio < 0.25 || ratio > 4) return false;
        }
        bool curved = face.Normal.Length != 3;
        if (curved != Curved) return false;
        if (curved) return Near(patch, CurvedTol);
        double nx = face.Normal[0], ny = face.Normal[1], nz = face.Normal[2];
        if (nx * Nx + ny * Ny + nz * Nz < 1 - 1e-6) return false;
        var (x, y, z) = PatchGeometry.Anchor(patch);
        if (Math.Abs(nx * x + ny * y + nz * z - D) > PlaneTol) return false;
        return Near(patch, ContainTol);
    }

    // 지금 조각이 대표 점을 품는가: 세모가 있으면 세모 하나가 그 점을 tol 안에 품을 때, 세모가 없으면(곡면의 세모를 아직 붙이지 않은 조각) 대표 점끼리 1 cm 안.
    private bool Near(FacePatch patch, double tol)
    {
        if (patch.Triangles.Count >= 3) return PatchGeometry.Contains(patch, Cx, Cy, Cz, tol);
        var (x, y, z) = PatchGeometry.Anchor(patch);
        return Math.Sqrt((x - Cx) * (x - Cx) + (y - Cy) * (y - Cy) + (z - Cz) * (z - Cz)) <= CurvedTol;
    }

    /// <summary>파일에 적는 글(대응표 파일의 면 칸) — 좌표는 0.1 mm, 넓이는 열째 자리(1~50 mm² 의 가는 조각도 0 이 되지 않게), 법선은 여섯째 자리.</summary>
    public string Text => string.Join("|", Element.ToString(Inv), Unit(Nx), Unit(Ny), Unit(Nz), Len(D), Len(Cx), Len(Cy), Len(Cz), Area.ToString("0.##########", Inv));

    private static string Unit(double v) => v.ToString("0.######", Inv);
    private static string Len(double v) => v.ToString("0.####", Inv);

    /// <summary>파일의 글을 읽는다(모르는 꼴이면 null).</summary>
    public static DropRecord? Parse(string text)
    {
        var cells = text.Split('|');
        if (cells.Length != 9 || !long.TryParse(cells[0], NumberStyles.Integer, Inv, out long element)) return null;
        var v = new double[8];
        for (int i = 0; i < 8; i++)
            if (!double.TryParse(cells[i + 1], NumberStyles.Float, Inv, out v[i]) || !double.IsFinite(v[i])) return null;
        return new DropRecord(element, v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7]);
    }
}

/// <summary>
/// 판마다 «겹침» 기록 하나(R67 — JACK 2026-10-27 «거푸집 객체를 누르고 겹침 무시 · 공제할 때와 안 할 때를 단추로»): 면 열쇠(부재 Id · 바깥 법선 · 평면 자리) +
/// 그 면을 덮은 이웃 부재 Id + 무시(공제 안 함) / 공제. 면 열쇠는 평면이라 공제를 풀어 판 모양이 바뀌어도 그대로 맞는다(조각 열쇠 DropRecord 와 다르다).
/// 맞추기 = 같은 부재 · 법선이 같음 · 평면 거리 5 mm 안.
/// </summary>
public readonly record struct JointRecord(long Element, double Nx, double Ny, double Nz, double D, long Other, bool Ignore)
{
    private const double PlaneTol = 0.005;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>그 면(부재 · 법선 · 평면 자리)의 그 이웃에 대한 기록인가.</summary>
    public bool Matches(long element, double nx, double ny, double nz, double d, long other) =>
        element == Element && other == Other && nx * Nx + ny * Ny + nz * Nz >= 1 - 1e-6 && Math.Abs(d - D) <= PlaneTol;

    /// <summary>같은 면 · 같은 이웃인가(값은 보지 않음).</summary>
    public bool SameKey(JointRecord o) => Matches(o.Element, o.Nx, o.Ny, o.Nz, o.D, o.Other);

    /// <summary>면 열쇠의 글(대응표 파일 — 부재 Id|법선 x|y|z|평면 자리).</summary>
    public string FaceText => string.Join("|", Element.ToString(Inv), Nx.ToString("0.######", Inv), Ny.ToString("0.######", Inv), Nz.ToString("0.######", Inv), D.ToString("0.####", Inv));

    /// <summary>면 열쇠의 글 + 이웃 Id 를 읽는다(모르는 꼴이면 null).</summary>
    public static JointRecord? Parse(string face, string other, bool ignore)
    {
        var cells = face.Split('|');
        if (cells.Length != 5 || !long.TryParse(cells[0], NumberStyles.Integer, Inv, out long element) || !long.TryParse(other.Trim(), NumberStyles.Integer, Inv, out long o)) return null;
        var v = new double[4];
        for (int i = 0; i < 4; i++)
            if (!double.TryParse(cells[i + 1], NumberStyles.Float, Inv, out v[i]) || !double.IsFinite(v[i])) return null;
        return new JointRecord(element, v[0], v[1], v[2], v[3], o, ignore);
    }

    /// <summary>그 면 결과의 그 이웃에 대한 기록(평면 면만 — 곡면이면 null).</summary>
    public static JointRecord? Of(FaceResult face, long other, bool ignore) =>
        face.Normal.Length != 3 ? null : new JointRecord(face.ElementId, face.Normal[0], face.Normal[1], face.Normal[2], face.PlaneD, other, ignore);
}

/// <summary>조각의 모양 셈(뺀 판 열쇠가 쓴다).</summary>
internal static class PatchGeometry
{
    /// <summary>
    /// 조각 안의 대표 점: 넓이 중심(세모 넓이의 무게로 잰 것)이 조각 위면 그것, 아니면 가장 큰 세모의 무게 중심(구멍 난 벽 · ㄷ자 조각의 넓이 중심은
    /// 조각 밖일 수 있다). 세모가 없으면 가장 큰 고리 꼭짓점의 평균. 세모로 잰 값만 조각에 담아 둔다 — 곡면의 세모는 Analyze 뒤에 붙는다
    /// (FormworkReader.AttachCurved — 붙기 전에 잰 값을 담아 두면 붙은 뒤에도 그 값을 쓴다).
    /// </summary>
    public static (double x, double y, double z) Anchor(FacePatch patch)
    {
        if (patch.AnchorCache is { } cached) return cached;
        double sx = 0, sy = 0, sz = 0, total = 0, best = 0;
        (double, double, double) largest = (0, 0, 0);
        var t = patch.Triangles;
        for (int i = 0; i + 2 < t.Count; i += 3)
        {
            double[] a = t[i], b = t[i + 1], c = t[i + 2];
            double area = TriangleArea(a, b, c);
            if (!(area > 0)) continue;
            double gx = (a[0] + b[0] + c[0]) / 3, gy = (a[1] + b[1] + c[1]) / 3, gz = (a[2] + b[2] + c[2]) / 3;
            sx += area * gx; sy += area * gy; sz += area * gz;
            total += area;
            if (area > best) { best = area; largest = (gx, gy, gz); }
        }
        if (!(total > 0))
        {
            var ring = patch.Outline.OrderByDescending(r => r.Count).FirstOrDefault();
            return ring is { Count: > 0 } ? (ring.Average(p => p[0]), ring.Average(p => p[1]), ring.Average(p => p[2])) : (0, 0, 0);
        }
        (double x, double y, double z) anchor = (sx / total, sy / total, sz / total);
        if (!Contains(patch, anchor.x, anchor.y, anchor.z, 0)) anchor = largest;
        patch.AnchorCache = anchor;
        return anchor;
    }

    /// <summary>점이 조각 위인가: 조각의 세모 하나가 그 점을 품는다(세모의 평면에서 tol 안 · 세모 안이거나 변에서 tol 안). 세모가 없으면 거짓.</summary>
    public static bool Contains(FacePatch patch, double x, double y, double z, double tol)
    {
        var t = patch.Triangles;
        for (int i = 0; i + 2 < t.Count; i += 3)
            if (InTriangle(t[i], t[i + 1], t[i + 2], x, y, z, tol)) return true;
        return false;
    }

    private static double TriangleArea(double[] a, double[] b, double[] c)
    {
        double ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2], vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
        double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
        return Math.Sqrt(nx * nx + ny * ny + nz * nz) / 2;
    }

    private static bool InTriangle(double[] a, double[] b, double[] c, double px, double py, double pz, double tol)
    {
        double ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2], vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
        double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
        double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (!(len > 1e-12)) return false;
        nx /= len; ny /= len; nz /= len;
        double wx = px - a[0], wy = py - a[1], wz = pz - a[2];
        double off = wx * nx + wy * ny + wz * nz;
        if (Math.Abs(off) > tol + 1e-9) return false;
        // 평면에 내린 점의 무게 좌표(세모 안이면 셋 다 0 이상 — 셈의 끝자리 오차만큼은 안으로 본다).
        double qx = wx - off * nx, qy = wy - off * ny, qz = wz - off * nz;
        double d00 = ux * ux + uy * uy + uz * uz, d01 = ux * vx + uy * vy + uz * vz, d11 = vx * vx + vy * vy + vz * vz;
        double d20 = qx * ux + qy * uy + qz * uz, d21 = qx * vx + qy * vy + qz * vz;
        double denom = d00 * d11 - d01 * d01;
        if (!(Math.Abs(denom) > 1e-24)) return false;
        double v = (d11 * d20 - d01 * d21) / denom, w = (d00 * d21 - d01 * d20) / denom, u = 1 - v - w;
        const double slack = -1e-9;
        if (u >= slack && v >= slack && w >= slack) return true;
        if (tol <= 0) return false;
        // 세모 밖: 세 변까지의 거리가 tol 안이면 품은 것으로(이웃 세모와의 이음매 · 테두리 바로 밖).
        double[] q = { a[0] + qx, a[1] + qy, a[2] + qz };
        return SegmentDistance(q, a, b) <= tol || SegmentDistance(q, b, c) <= tol || SegmentDistance(q, c, a) <= tol;
    }

    private static double SegmentDistance(double[] p, double[] a, double[] b)
    {
        double abx = b[0] - a[0], aby = b[1] - a[1], abz = b[2] - a[2];
        double apx = p[0] - a[0], apy = p[1] - a[1], apz = p[2] - a[2];
        double len2 = abx * abx + aby * aby + abz * abz;
        double s = len2 > 0 ? Math.Clamp((apx * abx + apy * aby + apz * abz) / len2, 0, 1) : 0;
        double dx = apx - s * abx, dy = apy - s * aby, dz = apz - s * abz;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
