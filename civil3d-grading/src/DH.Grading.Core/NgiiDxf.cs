using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DH.Grading.Core;

/// <summary>★★★[JACK 0901 "브이월드에서 받은 DXF 수치지도를 인식해서 해당 도엽의 원지반을 생성"]
///
/// <para><b>수치지도를 열지 않고</b> 등고선과 표고점만 뽑아 온다. 국토지리정보원 수치지도는
/// <b>R12 ASCII DXF</b>라 글자 그대로 읽힌다 — 도면으로 열면 수만 개 객체가 통째로 올라오는데
/// 우리가 쓰는 것은 그중 세 겹뿐이다.</para>
///
/// <para><b>레이어 이름이 곧 지형지물 코드</b>다(별표1 수치지도 지형지물 표준코드).
/// 그 표에서 F001·F002 계열은 <b>정확히 16개</b>뿐이고, 그중 <c>713x</c> 넷이 <b>글자(수치)</b>다 —
/// 나머지가 우리가 받을 것이다.</para>
///
/// <para>★<b>글자는 뺀다.</b> <c>F0017131</c>(등고수치)·<c>F0027132</c>(표고점수치)는 표고를 적어 둔
/// <b>글씨</b>다. 표고가 0으로 들어 있어 그냥 받으면 지표면이 바닥으로 꺼진다.</para>
///
/// <para>★★<b>표고 0은 버린다</b>(JACK 지시). 측량이 안 된 자리라 0이 들어간 것이지
/// 해발 0m가 아니다 — 한 가닥만 섞여도 지표면에 <b>절벽</b>이 생긴다.</para></summary>
public static class NgiiDxf
{
    /// <summary>등고선 한 가닥.</summary>
    public sealed class Contour
    {
        public string Layer = "";
        public double Elev;
        /// <summary>계곡선인가(굵게 그릴 것).</summary>
        public bool IsIndex;
        /// <summary>닫힌 고리인가 — 봉우리·웅덩이 등고선이 그렇다.</summary>
        public bool Closed;
        public List<Point3> Pts = new();
    }

    /// <summary>한 도엽에서 뽑아 온 것.</summary>
    public sealed class Sheet
    {
        public string File = "";
        public List<Contour> Contours = new();
        public List<Point3> Spots = new();

        /// <summary>버린 것들 — <b>조용히 버리지 않는다</b>. 몇 개를 왜 버렸는지 말해야 한다.</summary>
        public int DroppedZeroContours, DroppedZeroSpots, DroppedShort;

        /// <summary>표고가 정점마다 제각각이라 못 믿은 가닥 — 0으로 버린 것과 구별한다.</summary>
        public int DroppedMixed;

        /// <summary>읽은 레이어별 개수(진단용).</summary>
        public Dictionary<string, int> ByLayer = new(StringComparer.OrdinalIgnoreCase);

        public int VertexCount { get { int n = 0; foreach (var c in Contours) n += c.Pts.Count; return n; } }
    }

    // ── 지형지물 표준코드 판정 ────────────────────────────────────────────────

    /// <summary>등고선인가 — <b>표고를 가진 곡선 코드만</b> 받는다.
    /// <para>★★★[JACK 0901 "F001이나 F002로 시작하더라도 등고선이 아니고 그냥 지형 객체인
    /// 것들도 있어. 그런 것들은 <b>표고가 없어</b> 주의해야 해"]</para>
    /// <para>그래서 <b>앞자리로 걸러 받지 않는다.</b> 표준코드 표에서 실제로 표고를 들고 있는
    /// 열 개만 받는다 — <c>F00171</c> + (1=볼록지 | 2=오목지) + (0~4: 미분류·주·간·조·계곡선).
    /// <c>F0010000</c>(등고선 미분류)도 뺀다 — 이름만 등고선이고 표고가 없는 자리다.</para>
    /// <para>안 좁히면 표고 없는 지형선이 <b>표고 0</b>으로 들어오는데, 0을 버리는 규칙이
    /// 그것을 다 걸러 주긴 해도 "버림 300가닥" 같은 겁나는 보고가 나온다.</para></summary>
    public static bool IsContourLayer(string layer)
    {
        string s = Norm(layer);
        if (s.Length != 8 || !s.StartsWith("F00171", StringComparison.Ordinal)) return false;
        return (s[6] == '1' || s[6] == '2') && s[7] >= '0' && s[7] <= '4';
    }

    /// <summary>계곡선인가 — 굵게 그리는 그것. 볼록지·오목지 모두 끝자리 4.</summary>
    public static bool IsIndexContourLayer(string layer)
    {
        string s = Norm(layer);
        return IsContourLayer(s) && s[7] == '4';
    }

    /// <summary>표고점인가 — <b>표고점 블록 하나뿐</b>이다.
    /// <para>★★★[JACK 0901 "특히 <b>F002로 시작하는 건 표고점 블록 외엔 쓰면 안 돼</b>"]</para>
    /// <para>표준코드 F002 계열은 셋뿐인데 <c>F0027132</c>는 글자, <c>F0020000</c>은 미분류다.
    /// 표고를 실제로 들고 있는 것은 <c>F0027217</c> 하나다 — 나머지를 받으면
    /// <b>표고 0인 점</b>이 지표면에 박혀 그 자리가 바닥으로 꺼진다.</para></summary>
    public const string SpotCode = "F0027217";

    public static bool IsSpotLayer(string layer) =>
        string.Equals(Norm(layer), SpotCode, StringComparison.Ordinal);

    /// <summary>레이어 이름 정리 — 대소문자·공백만 맞춘다.</summary>
    private static string Norm(string layer) => (layer ?? "").Trim().ToUpperInvariant();

    // ── 읽기 ──────────────────────────────────────────────────────────────────

    /// <summary>DXF 하나에서 등고선·표고점을 뽑는다.</summary>
    /// <param name="why">못 읽은 이유(성공이면 <c>null</c>).</param>
    public static Sheet Read(string path, out string why)
    {
        why = null;
        var sh = new Sheet { File = path };
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            // ★<b>바이너리 DXF는 우리 소관이 아니다</b> — 첫 22바이트로 갈린다.
            //   조용히 0가닥을 돌려주면 "자료가 없는 도엽"으로 오해한다.
            var head = new byte[22];
            int got = fs.Read(head, 0, head.Length);
            if (got == head.Length &&
                System.Text.Encoding.ASCII.GetString(head, 0, 18) == "AutoCAD Binary DXF")
            {
                why = "바이너리 DXF입니다 — CAD에서 열어 'DXF(ASCII)'로 다시 저장해 주세요.";
                return sh;
            }
            fs.Position = 0;

            // ★한글이 섞여 있어도 <b>줄이 깨지지 않게</b> Latin1로 읽는다.
            //   우리가 보는 값(코드·좌표·레이어명)은 전부 ASCII고, 한글은 TEXT 값에만 있어 안 본다.
            //   CP949는 후행 바이트에 줄바꿈(0x0A)이 안 나오므로 줄 나누기가 어긋나지 않는다.
            using var sr = new StreamReader(fs, System.Text.Encoding.Latin1, false, 1 << 16);
            Parse(sr, sh);
            if (sh.Contours.Count == 0 && sh.Spots.Count == 0)
                why = "이 파일에서 등고선·표고점 레이어를 찾지 못했습니다(F001·F002 계열).";
        }
        catch (Exception ex) { why = ex.Message; }
        return sh;
    }

    private const int MinPts = 2;

    /// <summary>DXF 한 벌을 훑는다.
    /// <para>★<b>지금 엔티티의 값은 다 모았다가 끝날 때 쓴다.</b> 코드가 어떤 순서로 오든
    /// 상관없게 하기 위해서다 — 예전에는 좌표(10)를 만난 순간에 만들었더니
    /// 그보다 <b>먼저 오는 고도(38)</b>를 놓쳐 LWPOLYLINE 등고선이 통째로 버려졌다(검토 0901).</para></summary>
    private static void Parse(TextReader sr, Sheet sh)
    {
        string section = null;
        string entity = null;
        bool inEntities = false;

        // ── 지금 읽고 있는 엔티티에서 모은 값들(코드 0마다 비운다)
        string layer = null;
        double x = 0, y = 0, z = 0, elev38 = 0;
        bool hasX = false, hasY = false, hasZ = false, has38 = false;
        int flags70 = 0;
        var lwPts = new List<Point3>();     // LWPOLYLINE의 정점(엔티티 안에 다 들어 있다)
        double lwx = 0; bool lwHasX = false;

        // ── POLYLINE 하나(VERTEX 여럿에 걸쳐 있다)
        Contour open = null;

        void ResetEntity()
        {
            layer = null;
            x = y = z = elev38 = 0;
            hasX = hasY = hasZ = has38 = false;
            flags70 = 0;
            lwPts.Clear(); lwHasX = false;
        }

        void EndEntity()
        {
            if (entity == null || !inEntities) return;
            switch (entity)
            {
                case "POLYLINE":
                    // ★앞 가닥이 SEQEND 없이 끝났을 수 있다 — <b>버리기 전에 마감</b>한다(검토 0901).
                    //   안 그러면 그 가닥은 결과에도 버림 수에도 안 잡혀 조용히 사라진다.
                    CloseContour(sh, ref open);
                    // ★POLYLINE 자신의 좌표는 <b>(0,0,표고)</b> 더미다 — 점으로 쓰면 도면 원점에 가짜가 쌓인다.
                    //   표고만 받아 둔다. 고도(38)가 있으면 그것이 우선이다(2D 폴리선).
                    if (IsContourLayer(layer))
                        open = new Contour
                        {
                            Layer = layer,
                            Elev = has38 ? elev38 : (hasZ ? z : 0),
                            IsIndex = IsIndexContourLayer(layer),
                            Closed = (flags70 & 1) != 0,
                        };
                    break;

                case "VERTEX":
                    if (open != null && hasX && hasY)
                        open.Pts.Add(new Point3(x, y, hasZ ? z : open.Elev));
                    break;

                case "SEQEND":
                    CloseContour(sh, ref open);
                    break;

                case "LWPOLYLINE":
                    // 정점이 엔티티 안에 다 들어 있으므로 <b>여기서 통째로</b> 마감한다.
                    if (IsContourLayer(layer) && lwPts.Count > 0)
                    {
                        double e = has38 ? elev38 : (hasZ ? z : 0);
                        var c2 = new Contour
                        {
                            Layer = layer,
                            Elev = e,
                            IsIndex = IsIndexContourLayer(layer),
                            Closed = (flags70 & 1) != 0,
                        };
                        foreach (var p in lwPts) c2.Pts.Add(new Point3(p.X, p.Y, e));
                        CloseContour(sh, ref c2);
                    }
                    break;

                case "POINT":
                case "INSERT":
                    // ★표고점은 <b>블록(INSERT)</b>으로 온다 — 삽입점 Z가 표고다.
                    if (IsSpotLayer(layer) && hasX && hasY)
                    {
                        Bump(sh, layer);
                        if (!hasZ || Math.Abs(z) < 1e-9) sh.DroppedZeroSpots++;
                        else sh.Spots.Add(new Point3(x, y, z));
                    }
                    break;
            }
        }

        string code, val;
        while ((code = sr.ReadLine()) != null)
        {
            val = sr.ReadLine();
            if (val == null) break;
            code = code.Trim();

            if (code == "0")
            {
                EndEntity();
                string v = val.Trim();
                if (v == "SECTION") { section = "?"; inEntities = false; entity = null; }
                else if (v == "ENDSEC")
                {
                    CloseContour(sh, ref open);
                    section = null; inEntities = false; entity = null;
                }
                else if (v == "EOF") break;
                else if (inEntities) { entity = v; ResetEntity(); }
                continue;
            }

            if (section == "?" && code == "2")
            {
                section = val.Trim();
                // ★★<b>BLOCKS는 읽지 않는다.</b> 블록 <i>정의</i> 안에도 같은 레이어의 도형이 있어서,
                //   같이 읽으면 <b>블록 원점(0,0)에 유령 도형</b>이 잔뜩 생긴다.
                inEntities = section == "ENTITIES";
                continue;
            }
            if (!inEntities || entity == null) continue;

            switch (code)
            {
                case "8": layer = val.Trim(); break;
                case "70": if (int.TryParse(val.Trim(), out int f)) flags70 = f; break;
                case "10":
                    if (entity == "LWPOLYLINE") { if (D(val, out double lx)) { lwx = lx; lwHasX = true; } }
                    else if (D(val, out double vx)) { x = vx; hasX = true; }
                    break;
                case "20":
                    if (entity == "LWPOLYLINE")
                    {
                        if (lwHasX && D(val, out double ly)) lwPts.Add(new Point3(lwx, ly, 0));
                        lwHasX = false;
                    }
                    else if (D(val, out double vy)) { y = vy; hasY = true; }
                    break;
                case "30": if (D(val, out double vz)) { z = vz; hasZ = true; } break;
                case "38":
                    // 2D 폴리선의 고도 — 좌표(30)가 0일 때 이것이 진짜 표고다.
                    if (D(val, out double el)) { elev38 = el; has38 = true; }
                    break;
            }
        }
        EndEntity();
        CloseContour(sh, ref open);
    }

    /// <summary>모으던 등고선 한 가닥을 마감한다 — <b>버릴 것은 여기서 버린다</b>.</summary>
    private static void CloseContour(Sheet sh, ref Contour c)
    {
        if (c == null) return;
        var cur = c; c = null;
        Bump(sh, cur.Layer);

        // ★짧은 것부터 본다 — 정점이 없는 가닥을 "표고 0"으로 세면 진단이 사람을 속인다(검토 0901).
        if (cur.Pts.Count < MinPts) { sh.DroppedShort++; return; }

        double elev = cur.Elev;
        if (Math.Abs(elev) < 1e-9)
        {
            // ★★<b>정점 하나만 믿지 않는다</b>(검토 0901). 예전에는 <b>처음 만난</b> 0 아닌 Z를 그대로
            //   가닥 전체에 발라 버려, 버려야 할 가닥이 <b>없던 등고선으로 되살아났다</b>.
            //   과반이 같은 값을 말할 때만 그 값을 쓴다.
            elev = Majority(cur.Pts, out int agree);
            if (Math.Abs(elev) < 1e-9) { sh.DroppedZeroContours++; return; }
            if (agree * 2 < cur.Pts.Count) { sh.DroppedMixed++; return; }
        }

        var pts = new List<Point3>(cur.Pts.Count);
        foreach (var p in cur.Pts) pts.Add(new Point3(p.X, p.Y, elev));
        cur.Pts = pts;
        cur.Elev = elev;
        sh.Contours.Add(cur);
    }

    /// <summary>정점 표고 중 <b>가장 많이 나온 값</b>(0은 세지 않는다). 없으면 0.</summary>
    private static double Majority(List<Point3> pts, out int count)
    {
        var tally = new Dictionary<long, int>();
        foreach (var p in pts)
        {
            if (Math.Abs(p.Z) < 1e-9) continue;
            long k = (long)Math.Round(p.Z * 1000.0);      // mm 단위로 묶는다
            tally.TryGetValue(k, out int n);
            tally[k] = n + 1;
        }
        long best = 0; count = 0;
        foreach (var kv in tally) if (kv.Value > count) { count = kv.Value; best = kv.Key; }
        return count == 0 ? 0 : best / 1000.0;
    }

    private static void Bump(Sheet sh, string layer)
    {
        if (string.IsNullOrEmpty(layer)) return;
        sh.ByLayer.TryGetValue(layer, out int n);
        sh.ByLayer[layer] = n + 1;
    }

    private static bool D(string s, out double v) =>
        double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    // ── 여러 도엽 합치기 ──────────────────────────────────────────────────────

    /// <summary>★[JACK 0901 "계획부지가 두 도엽 이상에 걸쳐지면 여러 개 선택해서 <b>연결된 하나의 지표면</b>"]
    /// <para><b>표고점은 겹친 것을 걸러 낸다</b> — 도엽 경계의 같은 점이 두 번 들어온다.</para>
    /// <para>★<b>등고선은 안 거른다.</b> 도엽 경계에서 한 가닥이 둘로 잘려 들어오는데
    /// 그 둘은 서로 다른 선이라 지우면 지형이 끊긴다. 겹치는 자리는 표고가 같으므로
    /// 삼각망이 얇은 조각을 만들 뿐 형상은 유지된다 — <b>지우는 쪽이 더 위험하다</b>.</para></summary>
    public static Sheet Merge(IReadOnlyList<Sheet> sheets, double dupTolM = 0.05)
    {
        var all = new Sheet { File = $"{sheets.Count}장 합침" };
        foreach (var s in sheets)
        {
            all.Contours.AddRange(s.Contours);
            all.DroppedZeroContours += s.DroppedZeroContours;
            all.DroppedZeroSpots += s.DroppedZeroSpots;
            all.DroppedShort += s.DroppedShort;
            all.DroppedMixed += s.DroppedMixed;
            foreach (var kv in s.ByLayer)
            {
                all.ByLayer.TryGetValue(kv.Key, out int n);
                all.ByLayer[kv.Key] = n + kv.Value;
            }
        }

        double tol = Math.Max(1e-6, dupTolM);
        var grid = new HashSet<(long, long, long)>();
        foreach (var s in sheets)
            foreach (var p in s.Spots)
            {
                var key = ((long)Math.Round(p.X / tol), (long)Math.Round(p.Y / tol), (long)Math.Round(p.Z / tol));
                if (grid.Add(key)) all.Spots.Add(p);
            }
        return all;
    }

    /// <summary>겹친 표고점을 몇 개 걸렀는지 — 보고에 싣는다.</summary>
    public static int DuplicateSpots(IReadOnlyList<Sheet> sheets, Sheet merged)
    {
        int raw = 0;
        foreach (var s in sheets) raw += s.Spots.Count;
        return raw - merged.Spots.Count;
    }

    // ── 범위 ──────────────────────────────────────────────────────────────────

    /// <summary>가져온 것의 좌표 범위. 비었으면 <c>false</c>.</summary>
    public static bool Extent(Sheet sh, out double x0, out double y0, out double x1, out double y1)
    {
        x0 = y0 = double.MaxValue; x1 = y1 = double.MinValue;
        bool any = false;
        foreach (var c in sh.Contours)
            foreach (var p in c.Pts)
            {
                any = true;
                if (p.X < x0) x0 = p.X; if (p.X > x1) x1 = p.X;
                if (p.Y < y0) y0 = p.Y; if (p.Y > y1) y1 = p.Y;
            }
        foreach (var p in sh.Spots)
        {
            any = true;
            if (p.X < x0) x0 = p.X; if (p.X > x1) x1 = p.X;
            if (p.Y < y0) y0 = p.Y; if (p.Y > y1) y1 = p.Y;
        }
        if (!any) { x0 = y0 = x1 = y1 = 0; }
        return any;
    }

    /// <summary>표고 범위 — 0이 섞였는지 사람이 눈으로 확인할 수 있게.</summary>
    public static bool ElevRange(Sheet sh, out double zmin, out double zmax)
    {
        zmin = double.MaxValue; zmax = double.MinValue;
        bool any = false;
        foreach (var c in sh.Contours) { any = true; if (c.Elev < zmin) zmin = c.Elev; if (c.Elev > zmax) zmax = c.Elev; }
        foreach (var p in sh.Spots) { any = true; if (p.Z < zmin) zmin = p.Z; if (p.Z > zmax) zmax = p.Z; }
        if (!any) { zmin = zmax = 0; }
        return any;
    }
}
