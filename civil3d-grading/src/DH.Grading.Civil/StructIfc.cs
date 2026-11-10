using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★★[v116.5 · JACK 1110 «구조물은 Revit에서 — rvt · ifc · fbx»] IFC → 옆 DWG(«이름.ifc.dh.dwg» · 부재마다 SubDMesh · m · 레이어 = IFC 종류) → 지금 Revit DWG 흐름 그대로(외부참조 · 형상 · 3D · 지형 · 종단/횡단).
/// <para>IFC가 바뀌면 [다시 반영](<see cref="StructImport.ReloadChanged"/>)이 옆 DWG를 다시 만든다(옆 DWG가 IFC보다 오래되면).
/// RVT · FBX · Navisworks는 Civil 3D 2026이 형상을 읽을 길이 없어(1110 조사 — 조정 모델 = NWC/NWD 화면만 · FBXIMPORT 2019판에서 없어짐 · Revit FBX = 피트 · 좌표 선택 없음) 까닭과 내보내는 법만 알린다.</para></summary>
internal static class StructIfc
{
    internal const string Suffix = ".dh.dwg";
    internal static bool IsSide(string path) => path.EndsWith(".ifc" + Suffix, System.StringComparison.OrdinalIgnoreCase);
    internal static string IfcOf(string side) => side[..^Suffix.Length];

    /// <summary>못 읽는 확장자면 까닭(아니면 null).</summary>
    internal static string? NotReadable(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".rvt" or ".rfa" => "RVT · RFA는 Civil 3D 2026이 열지 못합니다 — Revit에서 [내보내기] → IFC(좌표 기준 = 공유 좌표) 또는 DWG(공유 좌표 · 3D 솔리드)로 내보낸 파일을 불러오세요",
            ".fbx" => "FBX는 Civil 3D 2026에 가져오기 명령이 없고(AutoCAD 2019판에서 없어짐) Revit FBX는 좌표 기준을 고를 수 없어 자리가 맞지 않습니다 — Revit에서 IFC(공유 좌표) 또는 DWG로 내보내세요",
            ".ifczip" or ".ifcxml" => "IFC 압축(ifczip) · XML(ifcxml)은 읽지 못합니다 — Revit에서 IFC(.ifc · 글 형식)로 내보내세요",
            ".nwc" or ".nwd" => "Navisworks 파일은 Civil에서 화면 표시(조정 모델)만 되고 형상을 읽을 수 없어 지형 · 종단 · 횡단에 쓸 수 없습니다 — Revit에서 IFC(공유 좌표) 또는 DWG로 내보내세요",
            _ => null,
        };
    }

    /// <summary>★[v116.7 · JACK 1110 «원점 근처»] Revit IFC 머리 «CoordinateReference [CoordinateBase: …]»가 «프로젝트 기준점»이면 한 줄 안내(아니면 빈 글).
    /// 그 꼴은 공유 좌표 옮김 값을 표현 문맥(WorldCoordinateSystem · TrueNorth)에만 싣는다 — 이 애드인은 회전 뜻을 견본 없이 정하지 않아 안 읽는다.</summary>
    internal static string CoordBaseHint(string ifc)
    {
        try
        {
            using var fs = new FileStream(ifc, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buf = new byte[Math.Min(8192, (int)Math.Min(int.MaxValue, fs.Length))];
            int n = fs.Read(buf, 0, buf.Length);
            string head = System.Text.Encoding.ASCII.GetString(buf, 0, n);
            int i = head.IndexOf("CoordinateBase:", System.StringComparison.Ordinal); if (i < 0) return "";
            int j = head.IndexOfAny(new[] { ',', ']' }, i); if (j < 0) return "";
            string v = head[(i + 15)..j];
            // STEP \X2\ … \X0\(UTF-16 16진) 풀기
            v = System.Text.RegularExpressions.Regex.Replace(v, @"\\X2\\([0-9A-Fa-f]+)\\X0\\", m =>
            {
                var hx = m.Groups[1].Value; var sb = new System.Text.StringBuilder();
                for (int k = 0; k + 3 < hx.Length; k += 4) sb.Append((char)System.Convert.ToInt32(hx.Substring(k, 4), 16));
                return sb.ToString();
            }).Trim();
            bool basePt = v.Contains("프로젝트 기준점") || v.IndexOf("Project Base Point", System.StringComparison.OrdinalIgnoreCase) >= 0;
            return basePt ? "이 IFC는 «프로젝트 기준점» 좌표로 내보낸 파일입니다 — Revit 내보내기 설정의 좌표 기준을 «공유 좌표»로 바꿔 다시 내보내세요" : "";
        }
        catch { return ""; }
    }

    /// <summary>IFC → 옆 DWG. 돌려줌 = 옆 DWG 경로(못 하면 null) · <paramref name="note"/> = IFC 읽은 결과 또는 까닭.</summary>
    internal static string? ToDwg(string ifc, out string note, string? sidePath = null)
    {
        IfcMesh.Result r;
        // ★[설치 전 검토 M6] 큰 IFC는 메모리를 파일 크기의 8~10배 씀 — 300 MB 넘으면 막음
        long size = 0; try { size = new FileInfo(ifc).Length; } catch { }
        if (size > 300L * 1024 * 1024) { note = $"IFC가 너무 큽니다({size / 1048576.0:F0} MB · 한도 300 MB) — Revit에서 필요한 부재만 내보내세요"; return null; }
        try { r = IfcMesh.Read(ifc); }
        catch (System.Exception ex) { note = "IFC를 못 읽음 — " + ex.Message; return null; }
        note = r.Text;
        if (r.Parts.Count == 0) { note = (r.Products == 0 ? "IFC에 부재(형상 있는 개체)가 없습니다 — 종류만 든 라이브러리 파일일 수 있습니다" : "IFC 부재에 읽을 형상이 없습니다") + " · " + r.Text; return null; }
        // ★[검토 M5] 단위 · 축척이 잘못되면(지도 변환 축척을 두 번 등) 모형이 몇 mm로 줄어든다 — 0.1 m보다 작으면 막음
        {
            double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
            foreach (var p in r.Parts) foreach (var v in p.V) { x0 = System.Math.Min(x0, v.X); x1 = System.Math.Max(x1, v.X); y0 = System.Math.Min(y0, v.Y); y1 = System.Math.Max(y1, v.Y); }
            if (System.Math.Max(x1 - x0, y1 - y0) < 0.1) { note = $"IFC 모형 크기가 {System.Math.Max(x1 - x0, y1 - y0) * 1000:F1} mm — 단위나 좌표 축척이 맞지 않습니다 · " + r.Text; return null; }
        }
        string side = sidePath ?? ifc + Suffix;
        string tmp = side + ".tmp";
        int failedParts = 0;
        try
        {
            using (var sdb = new Database(true, true))
            {
                sdb.Insunits = UnitsValue.Meters;
                using (var tr = sdb.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(sdb), OpenMode.ForWrite);
                    var lt = (LayerTable)tr.GetObject(sdb.LayerTableId, OpenMode.ForWrite);
                    var lays = new Dictionary<string, ObjectId>();
                    ObjectId Lay(string type)
                    {
                        // ★[검토 M1] 레이어 이름 = IFC 종류 + 우리 규칙 낱말(StructUnits — 계단 · 난간 · 문 · 창 · 설비 = 해치 안 함 · 지형 = 뺌)
                        string t = type.StartsWith("IFC") ? type[3..] : type;
                        string tag = t.Contains("STAIR") ? "_STRS" : t.Contains("RAILING") ? "_RAIL" : t.Contains("DOOR") ? "_DOOR" : t.Contains("WINDOW") ? "_WIND"
                                   : t.StartsWith("FLOW") || t.StartsWith("DISTRIBUTION") || t.Contains("FURNISHING") || t.Contains("FURNITURE") ? "_EQPM" : t.Contains("GEOGRAPHIC") ? "_TOPO" : "";
                        string nm = "IFC_" + t + tag;
                        if (lays.TryGetValue(nm, out var id)) return id;
                        if (!lt.Has(nm)) { var rec = new LayerTableRecord { Name = nm }; id = lt.Add(rec); tr.AddNewlyCreatedDBObject(rec, true); }
                        else id = lt[nm];
                        lays[nm] = id; return id;
                    }
                    foreach (var p in r.Parts)
                    {
                        if (p.T.Count == 0) continue;
                        // ★[설치 전 검토 M4] 부재 하나가 안 되어도 나머지는 넣음(세고 알림) · 도면에 못 넣은 객체는 바로 버림(검토 M3)
                        SubDMesh? sd = null;
                        try
                        {
                            var pts = new Point3dCollection(); foreach (var v in p.V) pts.Add(new Point3d(v.X, v.Y, v.Z));
                            var fa = new Int32Collection(); foreach (var (a, b, c) in p.T) { fa.Add(3); fa.Add(a); fa.Add(b); fa.Add(c); }
                            sd = new SubDMesh();
                            sd.SetDatabaseDefaults(sdb);
                            sd.SetSubDMesh(pts, fa, 0);
                            sd.LayerId = Lay(p.Type);
                            ms.AppendEntity(sd); tr.AddNewlyCreatedDBObject(sd, true);
                            sd = null;
                        }
                        catch { failedParts++; }
                        finally { sd?.Dispose(); }
                    }
                    tr.Commit();
                }
                sdb.UpdateExt(true);
                sdb.SaveAs(tmp, DwgVersion.Current);
            }
            File.Move(tmp, side, true);   // 한 번에 바꿈(검토 L — 반쯤 쓴 옆 DWG가 남지 않게)
            // ★[설치 전 검토 M2] 옆 DWG를 만든 IFC 서명(크기 · 시각) — 바뀜 검사는 같음 견주기(시계가 다른 PC · NAS에서 매번 다시 만들지 않게)
            try { File.WriteAllText(side + ".src", SigOf(ifc)); } catch { }
            if (failedParts > 0) note += $" · ⚠DWG로 못 넣은 부재 {failedParts}";
            return side;
        }
        catch (System.Exception ex) { note = $"IFC를 DWG로 못 바꿈({side}) — {ex.Message} · {r.Text}"; return null; }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
    }

    /// <summary>IFC 서명 — 크기 · 마지막 쓴 시각(틱).</summary>
    static string SigOf(string ifc) { var fi = new FileInfo(ifc); return fi.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + fi.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture); }

    /// <summary>IFC가 옆 DWG를 만든 뒤 바뀌었나 — 옆 DWG의 서명(«.src»)과 지금 IFC 서명이 다르면(같음 견주기 · 시각 순서 아님). IFC가 없으면 아님(eTransmit — 옆 DWG 그대로).</summary>
    internal static bool Stale(string side)
    {
        try
        {
            string ifc = IfcOf(side);
            if (!File.Exists(ifc)) return false;
            if (!File.Exists(side)) return true;
            string sig = File.Exists(side + ".src") ? File.ReadAllText(side + ".src").Trim() : "";
            return sig != SigOf(ifc);
        }
        catch { return false; }
    }
}
