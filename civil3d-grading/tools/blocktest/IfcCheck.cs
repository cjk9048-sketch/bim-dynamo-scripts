// ── S238 IFC ★[v116.5 · JACK 1110 «구조물은 Revit에서 — rvt · ifc · fbx» → IFC 읽기] Core IfcMesh — 만든 IFC 글(현장 값 없음 · 좌표는 지어낸 값)
using System.Globalization;
using DH.Grading.Core;

static class IfcCheck
{
    const string Head = "ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION(('t'),'2;1');\nFILE_NAME('t.ifc','',(''),(''),'t','t','');\nFILE_SCHEMA(('IFC2X3'));\nENDSEC;\nDATA;\n";
    const string Tail = "ENDSEC;\nEND-ISO-10303-21;\n";
    /// <summary>공통 머리 — mm 단위 · 문맥 #7 · 원점 #6 · 대지 배치 #12(지어낸 동 200000 · 북 550000 · 높이 10 m · RefDirection 30°).</summary>
    static string Base(bool rotate = true) =>
        "#1=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);\n#2=IFCUNITASSIGNMENT((#1));\n#3=IFCCARTESIANPOINT((0.,0.,0.));\n#4=IFCDIRECTION((0.,0.,1.));\n#5=IFCDIRECTION((1.,0.,0.));\n" +
        "#6=IFCAXIS2PLACEMENT3D(#3,#4,#5);\n#7=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,0.01,#6,$);\n" +
        $"#10=IFCCARTESIANPOINT((200000000.,550000000.,10000.));\n#9=IFCDIRECTION(({(rotate ? Math.Cos(Math.PI / 6) : 1).ToString("R", CultureInfo.InvariantCulture)},{(rotate ? Math.Sin(Math.PI / 6) : 0).ToString("R", CultureInfo.InvariantCulture)},0.));\n" +
        "#11=IFCAXIS2PLACEMENT3D(#10,#4,#9);\n#12=IFCLOCALPLACEMENT($,#11);\n#13=IFCSITE('s',$,'Default',$,$,#12,$,$,.ELEMENT.,$,$,0.,$,$);\n" +
        "#20=IFCCARTESIANPOINT((0.,0.,0.));\n#21=IFCAXIS2PLACEMENT3D(#20,#4,#5);\n#22=IFCLOCALPLACEMENT(#12,#21);\n#30=IFCCARTESIANPOINT((0.,0.));\n#31=IFCDIRECTION((1.,0.));\n#32=IFCAXIS2PLACEMENT2D(#30,#31);\n";
    static string Prod(int id, int shape) => $"#{id}=IFCPRODUCTDEFINITIONSHAPE($,$,(#{shape}));\n#{id + 1}=IFCBUILDINGELEMENTPROXY('g{id}',$,'p{id}',$,$,#22,#{id},$,$);\n";

    static double Vol(IfcMesh.Result r)
    {
        double v = 0;
        foreach (var p in r.Parts) foreach (var (a, b, c) in p.T) { var A = p.V[a]; var B = p.V[b]; var C = p.V[c]; v += (A.X * (B.Y * C.Z - B.Z * C.Y) - A.Y * (B.X * C.Z - B.Z * C.X) + A.Z * (B.X * C.Y - B.Y * C.X)) / 6; }
        return v;
    }
    /// <summary>국소(대지 원점 200000 · 550000 · 10) 기준 부피(큰 좌표 소거 오차 없게).</summary>
    static double VolLocal(IfcMesh.Result r, double ox, double oy, double oz)
    {
        double v = 0;
        foreach (var p in r.Parts) foreach (var (a, b, c) in p.T)
            {
                var A = p.V[a]; var B = p.V[b]; var C = p.V[c];
                double ax = A.X - ox, ay = A.Y - oy, az = A.Z - oz, bx = B.X - ox, by = B.Y - oy, bz = B.Z - oz, cx = C.X - ox, cy = C.Y - oy, cz = C.Z - oz;
                v += (ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx)) / 6;
            }
        return v;
    }
    static double PlanArea(IfcMesh.Result r)
    {
        double s = 0;
        foreach (var p in r.Parts) foreach (var (a, b, c) in p.T) { var A = p.V[a]; var B = p.V[b]; var C = p.V[c]; s += ((B.X - A.X) * (C.Y - A.Y) - (B.Y - A.Y) * (C.X - A.X)) / 2; }
        return s;
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S238 IFC 읽기(Core IfcMesh — 만든 IFC · 지어낸 좌표) ==");
        const double OX = 200000, OY = 550000, OZ = 10;
        // ① 돌출 상자 10 × 6 × 4 m(mm) · 대지 배치 30° 돌림 → 부피 240 · 가운데 = 대지 원점 · 높이 10 ~ 14
        {
            string t = Head + Base() + "#33=IFCRECTANGLEPROFILEDEF(.AREA.,$,#32,10000.,6000.);\n#34=IFCEXTRUDEDAREASOLID(#33,#21,#4,4000.);\n#35=IFCSHAPEREPRESENTATION(#7,'Body','SweptSolid',(#34));\n" + Prod(36, 35) + Tail;
            var r = IfcMesh.Parse(t);
            double v = VolLocal(r, OX, OY, OZ);
            var all = r.Parts.SelectMany(p => p.V).ToList();
            double cx = all.Average(p => p.X) - OX, cy = all.Average(p => p.Y) - OY, z0 = all.Min(p => p.Z), z1 = all.Max(p => p.Z);
            // 30° 돌린 상자 꼭짓점 하나(+5, +3) → (5cos30 − 3sin30, 5sin30 + 3cos30)
            double ex = 5 * Math.Cos(Math.PI / 6) - 3 * Math.Sin(Math.PI / 6), ey = 5 * Math.Sin(Math.PI / 6) + 3 * Math.Cos(Math.PI / 6);
            bool corner = all.Any(p => Math.Abs(p.X - OX - ex) < 1e-6 && Math.Abs(p.Y - OY - ey) < 1e-6);
            check("S238 IFC① 돌출 상자 mm → m · 대지 배치 30° · 부피 240 · 높이 10 ~ 14 · 돌린 꼭짓점 자리", r.Parts.Count == 1 && Math.Abs(v - 240) < 1e-6 && Math.Abs(cx) < 1e-6 && Math.Abs(cy) < 1e-6 && Math.Abs(z0 - 10) < 1e-9 && Math.Abs(z1 - 14) < 1e-9 && corner && r.Skipped.Count == 0,
                $"{r.Text} · 부피 {v:F6} · 가운데 ({cx:F6}, {cy:F6}) · z {z0:F3} ~ {z1:F3} · 꼭짓점 {corner}");
        }
        // ② 구멍 있는 면(4 × 4 바깥 · 2 × 2 구멍) — 평면 넓이 = 16 − 4 · 바깥만 쓰지 않음
        {
            string t = Head + Base(false) +
                "#40=IFCCARTESIANPOINT((0.,0.,0.));\n#41=IFCCARTESIANPOINT((4000.,0.,0.));\n#42=IFCCARTESIANPOINT((4000.,4000.,0.));\n#43=IFCCARTESIANPOINT((0.,4000.,0.));\n" +
                "#44=IFCCARTESIANPOINT((1000.,1000.,0.));\n#45=IFCCARTESIANPOINT((1000.,3000.,0.));\n#46=IFCCARTESIANPOINT((3000.,3000.,0.));\n#47=IFCCARTESIANPOINT((3000.,1000.,0.));\n" +
                "#50=IFCPOLYLOOP((#40,#41,#42,#43));\n#51=IFCPOLYLOOP((#44,#45,#46,#47));\n#52=IFCFACEOUTERBOUND(#50,.T.);\n#53=IFCFACEBOUND(#51,.T.);\n#54=IFCFACE((#52,#53));\n" +
                "#55=IFCOPENSHELL((#54));\n#56=IFCSHELLBASEDSURFACEMODEL((#55));\n#57=IFCSHAPEREPRESENTATION(#7,'Body','SurfaceModel',(#56));\n" + Prod(58, 57) + Tail;
            var r = IfcMesh.Parse(t);
            double a = PlanArea(r);
            check("S238 IFC② 구멍 있는 면 — 평면 넓이 16 − 4 = 12 ㎡(구멍을 다리로 이어 귀 자르기)", Math.Abs(a - 12) < 1e-6 && r.HoleFaces == 0, $"{r.Text} · 넓이 {a:F6}");
        }
        // ③ 반복 부재(IfcMappedItem · 축척 0.5 · 옮김 +10 m Y) → 부피 240/8 = 30 · Y 가운데 = 대지 + 10
        {
            string t = Head + Base(false) + "#33=IFCRECTANGLEPROFILEDEF(.AREA.,$,#32,10000.,6000.);\n#34=IFCEXTRUDEDAREASOLID(#33,#21,#4,4000.);\n#35=IFCSHAPEREPRESENTATION(#7,'Body','SweptSolid',(#34));\n" +
                "#60=IFCREPRESENTATIONMAP(#21,#35);\n#61=IFCCARTESIANPOINT((0.,10000.,0.));\n#62=IFCCARTESIANTRANSFORMATIONOPERATOR3D($,$,#61,0.5,$);\n#63=IFCMAPPEDITEM(#60,#62);\n#64=IFCSHAPEREPRESENTATION(#7,'Body','MappedRepresentation',(#63));\n" + Prod(65, 64) + Tail;
            var r = IfcMesh.Parse(t);
            double v = VolLocal(r, OX, OY, OZ), cy = r.Parts.SelectMany(p => p.V).Average(p => p.Y) - OY;
            check("S238 IFC③ 반복 부재(축척 0.5 · +10 m) — 부피 30 · 가운데 Y +10", Math.Abs(v - 30) < 1e-6 && Math.Abs(cy - 10) < 1e-6, $"{r.Text} · 부피 {v:F6} · Y {cy:F6}");
        }
        // ④ IFC4 IfcMapConversion(동 · 북 · 높이 · 90° · 축척 0.001) — 국소 (1000, 0, 0) mm → (E, N + 1, H)
        {
            string t = Head.Replace("IFC2X3", "IFC4") +
                "#1=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);\n#2=IFCUNITASSIGNMENT((#1));\n#3=IFCCARTESIANPOINT((0.,0.,0.));\n#4=IFCDIRECTION((0.,0.,1.));\n#5=IFCDIRECTION((1.,0.,0.));\n#6=IFCAXIS2PLACEMENT3D(#3,#4,#5);\n" +
                "#7=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,0.01,#6,$);\n#8=IFCPROJECTEDCRS('EPSG:5186',$,$,$,$,$,$);\n#9=IFCMAPCONVERSION(#7,#8,200000.,550000.,10.,0.,1.,0.001);\n" +
                "#12=IFCLOCALPLACEMENT($,#6);\n#22=IFCLOCALPLACEMENT(#12,#6);\n#30=IFCCARTESIANPOINT((0.,0.));\n#31=IFCDIRECTION((1.,0.));\n#32=IFCAXIS2PLACEMENT2D(#30,#31);\n" +
                "#33=IFCRECTANGLEPROFILEDEF(.AREA.,$,#32,2000.,2000.);\n#21=IFCAXIS2PLACEMENT3D(#3,#4,#5);\n#34=IFCEXTRUDEDAREASOLID(#33,#21,#4,1000.);\n#35=IFCSHAPEREPRESENTATION(#7,'Body','SweptSolid',(#34));\n" + Prod(36, 35) + Tail;
            var r = IfcMesh.Parse(t);
            var all = r.Parts.SelectMany(p => p.V).ToList();
            // 국소 (1000, 1000) mm 꼭짓점 → 90° 돌림 → (-1, +1) m → (E − 1, N + 1)
            bool hit = all.Any(p => Math.Abs(p.X - (OX - 1)) < 1e-6 && Math.Abs(p.Y - (OY + 1)) < 1e-6);
            double z1 = all.Max(p => p.Z);
            check("S238 IFC④ IFC4 지도 변환(동 · 북 · 높이 10 · 90° · 0.001) — 꼭짓점 (E − 1, N + 1) · 높이 10 ~ 11", r.MapConversion && hit && Math.Abs(z1 - 11) < 1e-9, $"{r.Text} · 꼭짓점 {hit} · z 최대 {z1:F6}");
        }
        // ⑤ I형 단면(폭 200 · 높이 400 · 복부 10 · 플랜지 20 mm) 1 m 돌출 → 부피 = (2·0.2·0.02 + 0.36·0.01)·1
        {
            string t = Head + Base(false) + "#33=IFCISHAPEPROFILEDEF(.AREA.,$,#32,200.,400.,10.,20.,$,$,$);\n#34=IFCEXTRUDEDAREASOLID(#33,#21,#4,1000.);\n#35=IFCSHAPEREPRESENTATION(#7,'Body','SweptSolid',(#34));\n" + Prod(36, 35) + Tail;
            var r = IfcMesh.Parse(t);
            double v = VolLocal(r, OX, OY, OZ), want = 2 * 0.2 * 0.02 + 0.36 * 0.01;
            check("S238 IFC⑤ I형 단면 돌출 — 부피 = 플랜지 둘 + 복부", Math.Abs(v - want) < 1e-9, $"{r.Text} · 부피 {v:F8} / {want:F8}");
        }
        // ⑥ 호 섞인 단면(IfcCompositeCurve = 반원 IfcTrimmedCurve(IfcCircle r 1 m) + 지름 IfcPolyline) 1 m 돌출 → 부피 ≈ π/2(5° 나눔 — 현 오차)
        {
            string t = Head + Base(false) +
                "#40=IFCCARTESIANPOINT((1000.,0.));\n#41=IFCCARTESIANPOINT((-1000.,0.));\n#42=IFCCIRCLE(#32,1000.);\n#43=IFCTRIMMEDCURVE(#42,(#40),(#41),.T.,.CARTESIAN.);\n#44=IFCCOMPOSITECURVESEGMENT(.CONTINUOUS.,.T.,#43);\n" +
                "#45=IFCPOLYLINE((#41,#40));\n#46=IFCCOMPOSITECURVESEGMENT(.CONTINUOUS.,.T.,#45);\n#47=IFCCOMPOSITECURVE((#44,#46),.F.);\n#48=IFCARBITRARYCLOSEDPROFILEDEF(.AREA.,$,#47);\n" +
                "#34=IFCEXTRUDEDAREASOLID(#48,#21,#4,1000.);\n#35=IFCSHAPEREPRESENTATION(#7,'Body','SweptSolid',(#34));\n" + Prod(36, 35) + Tail;
            var r = IfcMesh.Parse(t);
            double v = VolLocal(r, OX, OY, OZ), poly = 36 * 0.5 * Math.Sin(Math.PI / 36);   // 반원을 5°씩 36토막 낸 다각형 넓이
            check("S238 IFC⑥ 호 섞인 단면(반원 + 지름) — 부피 = 5° 다각형 반원 넓이 × 1 m", Math.Abs(v - poly) < 1e-9 && r.Skipped.Count == 0, $"{r.Text} · 부피 {v:F8} / {poly:F8}(참 반원 {Math.PI / 2:F6})");
        }
        // ⑦ 모르는 형상(IfcSweptDiskSolid)은 세고 알림 · 형상 없는 부재로 · 예외 없음
        {
            string t = Head + Base(false) + "#40=IFCPOLYLINE((#3,#10));\n#41=IFCSWEPTDISKSOLID(#40,100.,$,$,$);\n#35=IFCSHAPEREPRESENTATION(#7,'Body','AdvancedSweptSolid',(#41));\n" + Prod(36, 35) + Tail;
            var r = IfcMesh.Parse(t);
            check("S238 IFC⑦ 모르는 형상 — 세고(안 읽은 형상) 알림 · 부재 0/1 · 예외 없음", r.Parts.Count == 0 && r.Products == 1 && r.NoBody == 1 && r.Skipped.ContainsKey("IFCSWEPTDISKSOLID") && r.Text.Contains("⚠안 읽은 형상"), r.Text);
        }
        // ⑧ 한글 이름(\X2\…\X0\) · 문자열 속 작은따옴표('') 풀이
        {
            string t = Head + Base(false) + "#33=IFCRECTANGLEPROFILEDEF(.AREA.,$,#32,1000.,1000.);\n#34=IFCEXTRUDEDAREASOLID(#33,#21,#4,1000.);\n#35=IFCSHAPEREPRESENTATION(#7,'Body','SweptSolid',(#34));\n" +
                "#36=IFCPRODUCTDEFINITIONSHAPE($,$,(#35));\n#37=IFCBUILDINGELEMENTPROXY('g',$,'\\X2\\AD6C C870\\X0\\ ''A''',$,$,#22,#36,$,$);\n" + Tail;
            var r = IfcMesh.Parse(t.Replace("AD6C C870", "AD6CC870"));
            string nm = r.Parts.FirstOrDefault()?.Name ?? "";
            check("S238 IFC⑧ 이름 풀이 — \\X2\\ 한글 · '' 작은따옴표", nm == "구조 'A'", $"«{nm}»");
        }
    }
}
