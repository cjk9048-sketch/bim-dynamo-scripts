using System.Globalization;
using System.Text;

namespace DH.CivilCoord.Core;

/// <summary>
/// IFC4(ReferenceView · 길이 m) 글 파일 쓰기 — 외부 라이브러리 없음.
/// <para>Civil 좌표는 IfcSite 배치 하나에 싣는다(위치 = 옮김 · RefDirection = 돌림 · Z = 높이 옮김). 부재 좌표는 Revit 내부 좌표(m) 그대로라
/// 숫자가 작고(정밀도), 배치 사슬(부재 → 층 → 건물 → 대지)만 곱하면 Civil 세계 좌표가 된다 — Civil DH.Grading IfcMesh · 나비스웍스가 그렇게 읽는다.
/// IfcMapConversion은 쓰지 않는다(Civil IfcMesh는 배치 사슬과 지도 변환을 둘 다 곱해 두 번 옮겨진다).</para>
/// <para>형상 = IfcTriangulatedFaceSet(Revit 입체를 세모로 · 구멍 · 잘림이 이미 반영된 모양).</para>
/// </summary>
public static class IfcWriter
{
    /// <summary>쓸 수 있는 IFC4 종류 → (Tag 뒤 꼬리 · {P} = PredefinedType, 기본 PredefinedType).</summary>
    public static readonly IReadOnlyDictionary<string, (string Tail, string Default)> Entities = new Dictionary<string, (string, string)>
    {
        ["IFCWALL"] = (",{P}", ".NOTDEFINED."),
        ["IFCSLAB"] = (",{P}", ".FLOOR."),
        ["IFCFOOTING"] = (",{P}", ".NOTDEFINED."),
        ["IFCCOLUMN"] = (",{P}", ".COLUMN."),
        ["IFCBEAM"] = (",{P}", ".BEAM."),
        ["IFCMEMBER"] = (",{P}", ".NOTDEFINED."),
        ["IFCPLATE"] = (",{P}", ".NOTDEFINED."),
        ["IFCSTAIR"] = (",{P}", ".NOTDEFINED."),
        ["IFCRAMP"] = (",{P}", ".NOTDEFINED."),
        ["IFCROOF"] = (",{P}", ".NOTDEFINED."),
        ["IFCRAILING"] = (",{P}", ".NOTDEFINED."),
        ["IFCCOVERING"] = (",{P}", ".NOTDEFINED."),
        // IfcDoor · IfcWindow: Tag, OverallHeight, OverallWidth, PredefinedType, Operation/PartitioningType, UserDefined…
        ["IFCDOOR"] = (",$,$,{P},$,$", ".DOOR."),
        ["IFCWINDOW"] = (",$,$,{P},$,$", ".WINDOW."),
        ["IFCGEOGRAPHICELEMENT"] = (",{P}", ".TERRAIN."),
        // IfcDistributionFlowElement · IfcFurnishingElement(IFC4): Tag 까지
        ["IFCFLOWSEGMENT"] = ("", ""),
        ["IFCFLOWFITTING"] = ("", ""),
        ["IFCFLOWTERMINAL"] = ("", ""),
        ["IFCFURNISHINGELEMENT"] = ("", ""),
        ["IFCBUILDINGELEMENTPROXY"] = (",{P}", ".NOTDEFINED."),
    };

    public static void Write(IfcJob job, string path)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 16);
        Write(job, w);
    }

    public static void Write(IfcJob job, TextWriter w)
    {
        var o = new Out(w);
        string stamp = job.Time.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        string app = "DH.CivilCoord " + job.AppVersion;
        w.Write("ISO-10303-21;\r\nHEADER;\r\n");
        w.Write("FILE_DESCRIPTION(('ViewDefinition [ReferenceView_V1.2]'),'2;1');\r\n");
        w.Write($"FILE_NAME({S(job.FileName)},'{stamp}',(''),(''),{S(app)},{S(app)},'');\r\n");
        w.Write("FILE_SCHEMA(('IFC4'));\r\nENDSEC;\r\n\r\nDATA;\r\n");

        // 만든 이 · 단위 · 표현 문맥
        int person = o.Add("IFCPERSON($,'DH',$,$,$,$,$,$)");
        int org = o.Add("IFCORGANIZATION($,'DH',$,$,$)");
        int po = o.Add($"IFCPERSONANDORGANIZATION(#{person},#{org},$)");
        int appId = o.Add($"IFCAPPLICATION(#{org},{S(job.AppVersion)},'DH Civil Coord','DH.CivilCoord')");
        long unix = new DateTimeOffset(job.Time).ToUnixTimeSeconds();
        int oh = o.Add($"IFCOWNERHISTORY(#{po},#{appId},$,.NOCHANGE.,$,$,$,{unix.ToString(CultureInfo.InvariantCulture)})");
        int uLen = o.Add("IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.)");
        int uArea = o.Add("IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.)");
        int uVol = o.Add("IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.)");
        int uAng = o.Add("IFCSIUNIT(*,.PLANEANGLEUNIT.,$,.RADIAN.)");
        int units = o.Add($"IFCUNITASSIGNMENT((#{uLen},#{uArea},#{uVol},#{uAng}))");
        int origin = o.Add("IFCCARTESIANPOINT((0.,0.,0.))");
        int ident = o.Add($"IFCAXIS2PLACEMENT3D(#{origin},$,$)");
        int north = o.Add("IFCDIRECTION((0.,1.))");   // 세계(Civil 도면) Y = 북
        int ctx = o.Add($"IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,#{ident},#{north})");
        int body = o.Add($"IFCGEOMETRICREPRESENTATIONSUBCONTEXT('Body','Model',*,*,*,*,#{ctx},$,.MODEL_VIEW.,$)");
        string projGuid = job.ProjectGuid.Length == 22 ? job.ProjectGuid : IfcGuid.Compress(IfcGuid.FromName("DH.CivilCoord/project/" + job.ProjectName));
        int project = o.Add($"IFCPROJECT({S(projGuid)},#{oh},{S(job.ProjectName)},$,$,$,$,(#{ctx}),#{units})");

        // 대지 = Civil 좌표로 옮기는 자리
        int sitePt = o.Add($"IFCCARTESIANPOINT(({F(job.Tx)},{F(job.Ty)},{F(job.Tz)}))");
        int zAxis = o.Add("IFCDIRECTION((0.,0.,1.))");
        int xAxis = o.Add($"IFCDIRECTION(({F(Math.Cos(job.Angle))},{F(Math.Sin(job.Angle))},0.))");
        int siteAx = o.Add($"IFCAXIS2PLACEMENT3D(#{sitePt},#{zAxis},#{xAxis})");
        int sitePl = o.Add($"IFCLOCALPLACEMENT($,#{siteAx})");
        int site = o.Add($"IFCSITE({S(Gid(projGuid, "site"))},#{oh},{S(job.SiteName)},$,$,#{sitePl},$,$,.ELEMENT.,$,$,$,$,$)");
        int bldPl = o.Add($"IFCLOCALPLACEMENT(#{sitePl},#{ident})");
        int bld = o.Add($"IFCBUILDING({S(Gid(projGuid, "building"))},#{oh},{S(job.BuildingName)},$,$,#{bldPl},$,$,.ELEMENT.,$,$,$)");
        o.Add($"IFCRELAGGREGATES({S(Gid(projGuid, "rel/project-site"))},#{oh},$,$,#{project},(#{site}))");
        if (job.SiteProps.Count > 0)
        {
            var sp = job.SiteProps.Select(p => o.Add($"IFCPROPERTYSINGLEVALUE({S(p.Name)},$,IFCLABEL({S(Trim255(p.Value))}),$)")).ToList();
            int pset = o.Add($"IFCPROPERTYSET({S(Gid(projGuid, "pset/site"))},#{oh},'DH_CivilFit',$,({Refs(sp)}))");
            o.Add($"IFCRELDEFINESBYPROPERTIES({S(Gid(projGuid, "rel/pset/site"))},#{oh},$,$,(#{site}),#{pset})");
        }
        o.Add($"IFCRELAGGREGATES({S(Gid(projGuid, "rel/site-building"))},#{oh},$,$,#{site},(#{bld}))");

        // 층(쓰는 부재가 있는 레벨만)
        var usedKeys = new HashSet<string>(job.Parts.Where(p => p.Shells.Any(s => s.T.Count > 0)).Select(p => p.StoreyKey));
        var storeys = new Dictionary<string, (int Id, int Pl)>();
        foreach (var st in job.Storeys.Where(s => s.Key.Length > 0 && usedKeys.Contains(s.Key)).OrderBy(s => s.Elevation))
        {
            if (storeys.ContainsKey(st.Key)) continue;
            int pl = o.Add($"IFCLOCALPLACEMENT(#{bldPl},#{ident})");
            string g = st.Guid.Length == 22 ? st.Guid : Gid(projGuid, "storey/" + st.Key);
            int id = o.Add($"IFCBUILDINGSTOREY({S(g)},#{oh},{S(st.Name)},$,$,#{pl},$,{S(st.Name)},.ELEMENT.,{F(st.Elevation)})");
            storeys[st.Key] = (id, pl);
        }
        if (storeys.Count > 0)
            o.Add($"IFCRELAGGREGATES({S(Gid(projGuid, "rel/building-storeys"))},#{oh},$,$,#{bld},({Refs(storeys.Values.Select(s => s.Id))}))");

        // 부재
        var contained = new Dictionary<int, List<int>>();
        var sb = new StringBuilder(1 << 16);
        foreach (var part in job.Parts)
        {
            var shells = part.Shells.Where(s => s.V.Count >= 3 && s.T.Count > 0).ToList();
            if (shells.Count == 0) continue;
            if (!Entities.TryGetValue(part.Entity, out var spec)) { part.Entity = "IFCBUILDINGELEMENTPROXY"; spec = Entities[part.Entity]; }
            var (hostId, hostPl) = storeys.TryGetValue(part.StoreyKey, out var s0) ? s0 : (bld, bldPl);
            int pl = o.Add($"IFCLOCALPLACEMENT(#{hostPl},#{ident})");
            var items = new List<int>();
            foreach (var sh in shells)
            {
                sb.Clear().Append("IFCCARTESIANPOINTLIST3D((");
                for (int i = 0; i < sh.V.Count; i++)
                {
                    var v = sh.V[i];
                    if (i > 0) sb.Append(',');
                    // 평면 비율은 꼭짓점 X · Y 에(높이 Z 는 그대로 — 배치는 비율을 못 담음)
                    sb.Append('(').Append(F(job.Scale * v.X)).Append(',').Append(F(job.Scale * v.Y)).Append(',').Append(F(v.Z)).Append(')');
                }
                sb.Append("))");
                int pts = o.Add(sb.ToString());
                sb.Clear().Append("IFCTRIANGULATEDFACESET(#").Append(pts).Append(",$,").Append(sh.Closed ? ".T." : ".F.").Append(",(");
                bool first = true;
                foreach (var (a, b, c) in sh.T)
                {
                    if (a == b || b == c || a == c || a < 0 || b < 0 || c < 0 || a >= sh.V.Count || b >= sh.V.Count || c >= sh.V.Count) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('(').Append(a + 1).Append(',').Append(b + 1).Append(',').Append(c + 1).Append(')');
                }
                if (first) continue;   // 쓸 세모가 없음(점 목록만 남은 것은 아무도 안 가리킴 — 괜찮음)
                sb.Append("),$)");
                items.Add(o.Add(sb.ToString()));
            }
            if (items.Count == 0) continue;
            int rep = o.Add($"IFCSHAPEREPRESENTATION(#{body},'Body','Tessellation',({Refs(items)}))");
            int pds = o.Add($"IFCPRODUCTDEFINITIONSHAPE($,$,(#{rep}))");
            string pre = part.Predefined.Length > 0 ? part.Predefined : spec.Default;
            string tail = spec.Tail.Replace("{P}", pre.Length > 0 ? pre : "$");
            string guid = part.Guid.Length == 22 ? part.Guid : Gid(projGuid, "part/" + part.Tag + "/" + part.Name);
            int el = o.Add($"{part.Entity}({S(guid)},#{oh},{S(part.Name)},$,{SO(part.ObjectType)},#{pl},#{pds},{SO(part.Tag)}{tail})");
            if (!contained.TryGetValue(hostId, out var list)) contained[hostId] = list = new List<int>();
            list.Add(el);

            if (part.Props.Count > 0)
            {
                var props = new List<int>();
                foreach (var (name, value) in part.Props)
                    props.Add(o.Add($"IFCPROPERTYSINGLEVALUE({S(name)},$,IFCLABEL({S(Trim255(value))}),$)"));
                int pset = o.Add($"IFCPROPERTYSET({S(Gid(guid, "pset"))},#{oh},'DH_Revit',$,({Refs(props)}))");
                o.Add($"IFCRELDEFINESBYPROPERTIES({S(Gid(guid, "rel/pset"))},#{oh},$,$,(#{el}),#{pset})");
            }
        }
        foreach (var (host, list) in contained)
            o.Add($"IFCRELCONTAINEDINSPATIALSTRUCTURE({S(Gid(projGuid, "rel/contained/" + host))},#{oh},$,$,({Refs(list)}),#{host})");

        w.Write("ENDSEC;\r\nEND-ISO-10303-21;\r\n");
        w.Flush();
    }

    /// <summary>쓴 부재 수(형상이 있는 것).</summary>
    public static int WrittenParts(IfcJob job) => job.Parts.Count(p => p.Shells.Any(s => s.V.Count >= 3 && s.T.Any(t => t.A != t.B && t.B != t.C && t.A != t.C)));

    private static string Gid(string seed, string what) => IfcGuid.Compress(IfcGuid.FromName("DH.CivilCoord/" + seed + "/" + what));

    private static string Refs(IEnumerable<int> ids) => string.Join(",", ids.Select(i => "#" + i.ToString(CultureInfo.InvariantCulture)));

    private static string Trim255(string s) => s.Length <= 255 ? s : s[..255];

    /// <summary>STEP 실수 — 되읽으면 같은 값(R) · 늘 소수점이 있게(정수 꼴 «12» · «1E-06» 은 REAL 이 아님 → «12.0» · «1.E-06»).</summary>
    public static string F(double v)
    {
        if (!double.IsFinite(v) || Math.Abs(v) < 1e-11) return "0.";
        string s = v.ToString("R", CultureInfo.InvariantCulture);
        int e = s.IndexOfAny(new[] { 'E', 'e' });
        if (e >= 0)
        {
            string m = s[..e], x = s[(e + 1)..];
            if (!m.Contains('.')) m += ".";
            return m + "E" + x;
        }
        return s.Contains('.') ? s : s + ".0";
    }

    /// <summary>STEP 글 — 작은따옴표 겹치기 · ASCII 밖 글자(한글 등)와 역슬래시는 \X2\HHHH\X0\ · 조종 글자는 빈칸.</summary>
    public static string S(string? s)
    {
        s ??= "";
        var sb = new StringBuilder(s.Length + 8);
        sb.Append('\'');
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c >= 0x20 && c < 0x7F && c != '\\')
            {
                if (c == '\'') sb.Append("''"); else sb.Append(c);
                i++;
                continue;
            }
            if (c < 0x20 || c == 0x7F) { sb.Append(' '); i++; continue; }
            sb.Append("\\X2\\");
            while (i < s.Length && (s[i] >= 0x7F || s[i] == '\\'))
            {
                if (s[i] == 0x7F) break;
                sb.Append(((int)s[i]).ToString("X4", CultureInfo.InvariantCulture));
                i++;
            }
            sb.Append("\\X0\\");
        }
        sb.Append('\'');
        return sb.ToString();
    }

    /// <summary>비면 $(OPTIONAL).</summary>
    private static string SO(string? s) => string.IsNullOrEmpty(s) ? "$" : S(s);

    private sealed class Out
    {
        private readonly TextWriter _w;
        private int _next = 1;
        public Out(TextWriter w) { _w = w; }
        public int Add(string body)
        {
            int id = _next++;
            _w.Write('#');
            _w.Write(id.ToString(CultureInfo.InvariantCulture));
            _w.Write('=');
            _w.Write(body);
            _w.Write(";\r\n");
            return id;
        }
    }
}
