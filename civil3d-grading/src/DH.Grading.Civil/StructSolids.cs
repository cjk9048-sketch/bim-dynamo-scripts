using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace DH.Grading.Civil;

/// <summary>★★[v116.5 · JACK 1110 «Civil 3D 자체에 Revit 구조물을 투영해 종단 · 횡단에 넣는 기능 — 그런 방식» · 인터뷰 «Civil 투영 기능에 맞추기»]
/// 불러온 구조물을 도면 안 3D 솔리드로도 둔다(«DH-구조물-솔리드») — Civil [객체를 횡단/종단 뷰에 투영]은 도면 안 3D 솔리드 · 블록 · 멀티뷰 블록 · 점을 받고
/// 외부참조 안 객체는 고르지 못하는 것으로 보여서(원본은 외부참조 그대로 — 우리 3D · 지형 · 종단/횡단은 그것을 씀).
/// <para>원본 안 3D 솔리드는 그대로 복사(정확) · 메시(서브D · 폴리페이스)는 원본 객체 하나 = 솔리드 하나로 바꿈(도면 안 메시 → ConvertToSolid · 부피 확인 · 안 되면 감김 뒤집어 한 번 더 — 옹벽 솔리드와 같은 법) ·
/// 열린 메시 · 못 바꾼 것은 세고 알림. 큰 좌표 ACIS 정밀(계획 v115 3절) — 국소 원점에서 만들고 옮긴다.</para>
/// <para>★[검토 H1] 열쇠(원본 해시 + 삽입 변환)가 같으면 그대로 둔다 — 핸들이 바뀌면 Civil 투영이 끊김. 꼬리표 = <see cref="StructDraw.KindSolid"/>(종단 · 횡단 · 초기화가 안 지움 · 지우기 · 원본 없어짐은 지움).</para></summary>
internal static class StructSolids
{
    internal const string Layer = "DH-구조물-솔리드";

    /// <summary>한 구조물의 솔리드 — 열쇠가 같으면 그대로 · 다르면 지우고 다시. 돌려줌 = (솔리드 수, 못 바꾼 수, 그대로 둠).</summary>
    internal static (int Made, int Failed) Draw(Database db, Transaction tr, StructImport.Loaded L, System.Text.StringBuilder? log)
    {
        if (tr.GetObject(L.Insert, OpenMode.ForRead) is not BlockReference br) return (0, 0);
        // 열쇠 = 원본 해시 + 삽입 변환의 해시(XData 글 255자 한도 · 검토 L) · 뒤에 «#솔리드 수»(사람이 지운 솔리드는 다시 — 검토 L)
        // ★[v117.4] «|면1» = 같은 평면 세모를 면 하나로 만든 솔리드(MeshFaces) — 옛 판 솔리드(대각선)는 한 번 다시 만듦(핸들 바뀜)
        string key = Hash(StructImport.KeyOf(L.Rec, br) + "|면1");
        var mine = StructDraw.FindTagged(db, tr, (k, _, id) => k == StructDraw.KindSolid && id == L.Rec.Id);
        if (mine.Count > 0 && mine.All(m => m.View == key + "#" + mine.Count))
        {
            log?.AppendLine($"  [구조물] {L.Rec.Name} Civil 투영용 솔리드 {mine.Count} 그대로(원본 · 자리 같음 — 핸들 유지)");
            return (mine.Count, 0);
        }
        foreach (var (id, _) in mine) try { tr.GetObject(id, OpenMode.ForWrite, false, true).Erase(); } catch { }
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        var lay = StructDraw.EnsureLayer(db, tr, Layer, 30);
        // 국소 원점 = 모형 원점(StructImport.ReadSoup와 같음 — 범위 최소 1 m 내림)
        var O = new Vector3d(L.Model.Ox, L.Model.Oy, L.Model.Oz);
        var toLocal = Matrix3d.Displacement(-O); var toWorld = Matrix3d.Displacement(O);
        int made = 0, failed = 0, heavy = 0;
        const int MaxFaces = 20000;   // ★[설치 전 검토 M5] 세모가 이보다 많은 부재는 솔리드로 안 바꿈(면 하나 = 세모 하나 — Civil이 멈춤 · 도면이 부풂) — 세고 알림
        // ★[설치 전 검토 M3] 도면에 못 넣은 객체는 바로 버림(나중에 다른 스레드가 지우다 Civil이 튕기지 않게)
        void Put(Solid3d s)
        {
            try
            {
                s.TransformBy(toWorld);
                s.SetDatabaseDefaults(db); s.LayerId = lay;
                StructDraw.TagSolid(db, tr, s, L.Rec.Id, key);
                ms.AppendEntity(s); tr.AddNewlyCreatedDBObject(s, true); made++;
            }
            catch { if (s.ObjectId.IsNull) s.Dispose(); failed++; }
        }
        // 메시 → 솔리드: 도면 안 임시 메시에서 바꾸고 지움 · 부피 > 0 확인 · 안 되면 감김 뒤집어 한 번 더
        Solid3d? Try(Point3dCollection v, Int32Collection f)
        {
            SubDMesh? sd = null;
            try
            {
                sd = new SubDMesh(); sd.SetDatabaseDefaults(db); sd.SetSubDMesh(v, f, 0);
                ms.AppendEntity(sd); tr.AddNewlyCreatedDBObject(sd, true);
                var s = sd.ConvertToSolid(false, true);   // 같은 평면 면은 합침(검토 M5)
                if (s == null) return null;
                double vol = 0; try { vol = s.MassProperties.Volume; } catch { }
                if (!(vol > 1e-9)) { s.Dispose(); return null; }
                return s;
            }
            catch { return null; }
            finally
            {
                try
                {
                    if (sd != null)
                    {
                        if (sd.ObjectId.IsNull) sd.Dispose();
                        else if (!sd.IsErased) sd.Erase();
                    }
                }
                catch { }
            }
        }
        Solid3d? TryBoth(Point3dCollection v, Int32Collection f)
        {
            var s = Try(v, f); if (s != null) return s;
            var g = new Int32Collection(); foreach (int k in f) g.Add(k); Flip(g);
            return Try(v, g);
        }
        int nMerged = 0, facesIn = 0, facesOut = 0, mergeFail = 0, volBad = 0; double maxRes = 0;
        Solid3d? FromMesh(Point3dCollection v, Int32Collection f)
        {
            int nf = 0; for (int i = 0; i < f.Count; i += f[i] + 1) nf++;
            if (nf > MaxFaces) { heavy++; return null; }
            // ★★[v117.4 · JACK 1111 «벽 솔리드 한 면에 대각선»] 같은 평면 세모를 n각 면 하나로(Core MeshFaces 주석 — 세계 좌표 반올림 3e-9 m가 ConvertToSolid 합침을 막던 것) ·
            //   n각 면으로 안 되면 지금처럼 세모로
            try
            {
                var pv = new List<DH.Grading.Core.Point3>(v.Count); foreach (Point3d p in v) pv.Add(new DH.Grading.Core.Point3(p.X, p.Y, p.Z));
                var fl = new List<int[]>(nf);
                for (int i = 0; i < f.Count; i += f[i] + 1) { var a = new int[f[i]]; for (int k = 0; k < a.Length; k++) a[k] = f[i + 1 + k]; fl.Add(a); }
                var mr = DH.Grading.Core.MeshFaces.MergeCoplanar(pv, fl);
                if (!mr.Bail && mr.Faces.Count < fl.Count)
                {
                    var v2 = new Point3dCollection(); foreach (int k in mr.Verts) v2.Add(v[k]);
                    var f2 = new Int32Collection(); foreach (var a in mr.Faces) { f2.Add(a.Length); foreach (int k in a) f2.Add(k); }
                    var s2 = TryBoth(v2, f2);
                    // ★[계획 검토 MEDIUM 5] 만든 솔리드 부피 = 메시 부피(1e-6)여야 씀 — 다르면 이 부재는 세모로
                    double want = System.Math.Abs(DH.Grading.Core.MeshFaces.Volume(pv, fl)), got = 0;
                    if (s2 != null) { try { got = s2.MassProperties.Volume; } catch { } }
                    if (s2 != null && System.Math.Abs(got - want) <= want * 1e-6 + 1e-9)
                    { nMerged++; facesIn += fl.Count; facesOut += mr.Faces.Count; maxRes = System.Math.Max(maxRes, mr.MaxRes); return s2; }
                    if (s2 != null) { s2.Dispose(); volBad++; }
                    mergeFail++;
                }
            }
            catch { mergeFail++; }
            return TryBoth(v, f);
        }
        void Walk(ObjectId btrId, Matrix3d M, int depth)
        {
            if (depth > 16 || tr.GetObject(btrId, OpenMode.ForRead) is not BlockTableRecord btr) return;
            foreach (ObjectId id in btr)
            {
                Entity? e = null;
                try { e = tr.GetObject(id, OpenMode.ForRead) as Entity; } catch { }
                if (e == null || !e.Visible) continue;
                if (DH.Grading.Core.StructUnits.IsTerrain(e.Layer)) continue;   // ★[검토 M2] 지형(뺀 부재)은 솔리드로 안 둠
                try
                {
                    switch (e)
                    {
                        case BlockReference b2: Walk(b2.BlockTableRecord, M * b2.BlockTransform, depth + 1); break;
                        case Solid3d s0:
                            {
                                var c = (Solid3d)s0.Clone();
                                try { c.TransformBy(toLocal * M); } catch { c.Dispose(); failed++; break; }
                                Put(c); break;
                            }
                        case SubDMesh sd0:
                            {
                                var v = new Point3dCollection(); foreach (Point3d p in sd0.Vertices) v.Add(p.TransformBy(toLocal * M));
                                var f = new Int32Collection(); foreach (int k in sd0.FaceArray) f.Add(k);
                                if (Det(M) < 0) Flip(f);
                                var s = FromMesh(v, f); if (s != null) Put(s); else failed++;
                                break;
                            }
                        case PolyFaceMesh pfm:
                            {
                                var v = new Point3dCollection(); var f = new Int32Collection();
                                foreach (ObjectId sid in pfm)
                                {
                                    var so = tr.GetObject(sid, OpenMode.ForRead);
                                    if (so is PolyFaceMeshVertex pv) v.Add(pv.Position.TransformBy(toLocal * M));
                                    else if (so is FaceRecord fr)
                                    {
                                        var ix = new List<int>();
                                        for (short q = 0; q < 4; q++) { int k = System.Math.Abs((int)fr.GetVertexAt(q)); if (k > 0 && !ix.Contains(k - 1)) ix.Add(k - 1); }
                                        if (ix.Count < 3) continue;
                                        f.Add(ix.Count); foreach (int k in ix) f.Add(k);
                                    }
                                }
                                if (Det(M) < 0) Flip(f);
                                if (v.Count < 4 || f.Count == 0) { failed++; break; }
                                var s = FromMesh(v, f); if (s != null) Put(s); else failed++;
                                break;
                            }
                    }
                }
                catch { failed++; }
            }
        }
        Walk(br.BlockTableRecord, br.BlockTransform, 0);
        // 만든 수를 꼬리표에(다음 [다시 반영]이 «그대로»를 가를 때 · 지운 솔리드가 있으면 다시 만듦)
        foreach (var (sid, _) in StructDraw.FindTagged(db, tr, (k, v, id) => k == StructDraw.KindSolid && id == L.Rec.Id && v == key))
            try { if (tr.GetObject(sid, OpenMode.ForWrite) is Entity se) StructDraw.TagSolid(db, tr, se, L.Rec.Id, key + "#" + made); } catch { }
        log?.AppendLine($"  [구조물] {L.Rec.Name} Civil 투영용 솔리드 {made}"
                        + (nMerged > 0 ? $" · 같은 평면 = 면 하나 {nMerged}개(면 {facesIn:N0} → {facesOut:N0} · 평면 어긋남 최대 {maxRes:0.#E+0} m)" : "")
                        + (mergeFail > 0 ? $" · ⚠면 합친 메시로 못 바꿔 세모 그대로 {mergeFail}" + (volBad > 0 ? $"(그중 부피 다름 {volBad})" : "") : "") + (failed > 0 ? $" · ⚠솔리드로 못 바꾼 메시 {failed}(열린 메시 등 — Civil 투영에서 빠짐)" : "")
                        + (heavy > 0 ? $" · ⚠세모 {MaxFaces:N0}개 넘는 무거운 부재 {heavy}는 솔리드로 안 바꿈(Civil 투영에서 빠짐)" : ""));
        return (made, failed + heavy);
    }

    static string Hash(string s)
    {
        var h = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s));
        return System.Convert.ToHexString(h, 0, 12);
    }

    static double Det(Matrix3d m) => m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);

    /// <summary>면 목록(개수, 색인…)의 감김을 뒤집음(대칭 삽입 · 바꾸기 재시도).</summary>
    static void Flip(Int32Collection f)
    {
        int i = 0;
        while (i < f.Count)
        {
            int n = f[i];
            for (int a = 0, b = n - 1; a < b; a++, b--) { int t = f[i + 1 + a]; f[i + 1 + a] = f[i + 1 + b]; f[i + 1 + b] = t; }
            i += n + 1;
        }
    }
}
