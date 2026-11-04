using System.IO;
using System.Security.Cryptography;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using static DH.Grading.Core.SheetBookRules;

namespace DH.Grading.Civil;

/// <summary>★★★[v112.0 · 계획_도면화.md 4.2 · 4.3 · JACK 1103 «도곽 = 섞어서»] <b>도곽 고르기 · 이 PC 저장 · 정의(json) · 사용자 도곽 검출 · 블록으로 들여오기 · 단순 도곽</b>.
/// <para>고르는 차례: ① 이 PC 사용자 도곽(<c>%APPDATA%\DH.Grading\도곽\current.json</c> — 파일 · 해시 맞을 때) → ② 번들 K-water(<c>Contents\Frames\KWATER.dwg</c> + json — 해시 맞을 때)
/// → ③ 코드로 그리는 «단순 도곽». 고른 도곽을 못 쓰면 말한다(⚠ — 말없이 바꾸지 않음).</para>
/// <para>도면에는 <b>외부참조가 아니라 블록</b>(이름 «DH도곽_{id}_{DWG 해시8}{정의 해시4}»). 옆 DB를 통째로 넣지 않고 깨끗한 새 DB에 모형 객체만 복제해 들인다(검토 M3).</para></summary>
internal static class FrameLibrary
{
    internal const string BundledId = "KWATER";
    internal const string SimpleId = "SIMPLE";
    /// <summary>번들 K-water DWG 해시(참고자료 원본 · 1103 15:44 저장판) — 다르면 쓰지 않고 알림.</summary>
    internal const string BundledSha = "9D32ACFC8B85AF9D604BABFFD56D5F6195B9E57368E53B739898A6C23F5915E3";
    internal const string BlockPrefix = "DH도곽_";
    internal const string FrameTag = "DH_FRAME";

    internal sealed class Choice
    {
        public string Id = SimpleId;
        public string Name = "단순 도곽";
        public string? Dwg;
        public string DwgHash = "";
        public FrameDef Def = SimpleFrame();
        /// <summary>⚠ 한 줄(고른 파일 없음 · 바뀜 · 기본 도곽 없음) — 빈 글 = 문제 없음.</summary>
        public string Warn = "";
        public bool User;
        public string Label => User ? Name : Id == BundledId ? "K-water(기본)" : "단순 도곽";
    }

    /// <summary>이 PC 저장 자리 — 시험은 환경 변수 <c>DH_GRADING_FRAMES_DIR</c>(%APPDATA% 안 건드림).</summary>
    internal static string PcDir =>
        Environment.GetEnvironmentVariable("DH_GRADING_FRAMES_DIR") is { Length: > 0 } d ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DH.Grading", "도곽");

    internal static string BundleDir => Path.Combine(Path.GetDirectoryName(typeof(FrameLibrary).Assembly.Location) ?? "", "Frames");

    internal static string Sha(string path)
    {
        using var f = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(f));
    }

    sealed class Current
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public string file { get; set; } = "";
        public string sha256 { get; set; } = "";
        public string source { get; set; } = "";
        public string saved { get; set; } = "";
    }

    static Current? ReadCurrent()
    {
        try
        {
            string p = Path.Combine(PcDir, "current.json");
            if (!File.Exists(p)) return null;
            return System.Text.Json.JsonSerializer.Deserialize<Current>(File.ReadAllText(p, Encoding.UTF8));
        }
        catch { return null; }
    }

    /// <summary>쓸 도곽 — ① 사용자 → ② 번들 K-water → ③ 단순 도곽(⚠는 <see cref="Choice.Warn"/>).</summary>
    internal static Choice Resolve()
    {
        string warn = "";
        var cur = ReadCurrent();
        if (cur != null && cur.id == SimpleId) return new Choice();   // ★[v112.2] 도면화 창에서 «단순 도곽»을 고름
        if (cur != null && cur.file.Length > 0)
        {
            string dwg = Path.Combine(PcDir, cur.file);
            string js = Path.ChangeExtension(dwg, ".frame.json");
            if (!File.Exists(dwg)) warn = $"고른 도곽 파일 없음 → 기본으로({cur.name})";
            else
            {
                string h = "";
                try { h = Sha(dwg); } catch { }
                var def = File.Exists(js) ? FrameDef.FromJson(File.ReadAllText(js, Encoding.UTF8)) : null;
                if (!string.Equals(h, cur.sha256, StringComparison.OrdinalIgnoreCase)) warn = $"고른 도곽 파일이 바뀜 → 기본으로({cur.name} · {Short(h)} ≠ {Short(cur.sha256)})";
                else if (def == null) warn = $"고른 도곽 정의 없음 → 기본으로({cur.name})";
                else return new Choice { Id = cur.id, Name = cur.name, Dwg = dwg, DwgHash = h, Def = def, User = true };
            }
        }
        var b = Bundled(out string bw);
        if (b != null) { b.Warn = warn; return b; }
        return new Choice { Warn = string.Join(" · ", new[] { warn, bw }.Where(t => t.Length > 0)) };
    }

    static string Short(string h) => h.Length >= 8 ? h.Substring(0, 8) : h;

    /// <summary>번들 K-water — 파일 · 정의 · 해시가 맞을 때만(아니면 null + 까닭).</summary>
    internal static Choice? Bundled(out string warn)
    {
        warn = "";
        string dwg = Path.Combine(BundleDir, BundledId + ".dwg"), js = Path.Combine(BundleDir, BundledId + ".frame.json");
        if (!File.Exists(dwg)) { warn = "기본 도곽 파일 없음 → 단순 도곽"; return null; }
        string h = "";
        try { h = Sha(dwg); } catch { }
        if (!string.Equals(h, BundledSha, StringComparison.OrdinalIgnoreCase)) { warn = $"기본 도곽 파일이 바뀜({Short(h)}) → 단순 도곽"; return null; }
        var def = File.Exists(js) ? FrameDef.FromJson(File.ReadAllText(js, Encoding.UTF8)) : null;
        if (def == null) { warn = "기본 도곽 정의 없음 → 단순 도곽"; return null; }
        return new Choice { Id = BundledId, Name = def.Name, Dwg = dwg, DwgHash = h, Def = def };
    }

    // ══════════════════════════ 도면설정 «4. 도곽» ══════════════════════════

    /// <summary>[바꾸기…] — DWG를 읽어 검출 → 이 PC에 복사본 · 정의 · current.json(고르는 순간 저장 — 도면설정 [저장]과 따로). 돌려줌 = (됨, 한 줄).</summary>
    internal static (bool Ok, string Msg) Pick(string path)
    {
        try
        {
            if (!File.Exists(path)) return (false, "파일 없음");
            string h = Sha(path);
            string name = Path.GetFileNameWithoutExtension(path);
            var def = DetectFile(path, name, out string err);
            if (def == null) return (false, err);
            Directory.CreateDirectory(PcDir);
            string safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string file = $"{safe}_{Short(h)}.dwg";
            string dst = Path.Combine(PcDir, file);
            if (!File.Exists(dst) || !string.Equals(Sha(dst), h, StringComparison.OrdinalIgnoreCase)) File.Copy(path, dst, true);
            File.WriteAllText(Path.ChangeExtension(dst, ".frame.json"), def.ToJson(), new UTF8Encoding(false));
            var cur = new Current { id = "U" + Short(h), name = name, file = file, sha256 = h, source = path, saved = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
            File.WriteAllText(Path.Combine(PcDir, "current.json"),
                System.Text.Json.JsonSerializer.Serialize(cur, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
                new UTF8Encoding(false));
            try { DiagLog.Append($"\n■ 도곽 고름 — {path} · {Short(h)} · 칸 {string.Join(" · ", def.Found)}" + (def.Notes.Count > 0 ? " · ⚠" + string.Join(" · ", def.Notes) : "") + $"\n  {def.ToJson().Replace("\n", "\n  ")}\n"); } catch { }
            return (true, def.Notes.Count > 0 ? string.Join(" · ", def.Notes) : "");
        }
        catch (System.Exception ex) { return (false, ex.Message); }
    }

    /// <summary>★[v112.2 · JACK 1104 «도곽을 선택하는 기능 · 기본은 수공 것»] 고를 수 있는 도곽 — K-water(기본 · 파일이 맞을 때) · 이 PC에 저장한 도곽(전에 고른 DWG — 파일 · 정의가 있는 것) · 단순 도곽.</summary>
    internal static List<(string Id, string Label)> Items()
    {
        var l = new List<(string, string)>();
        if (Bundled(out _) != null) l.Add((BundledId, "K-water(기본)"));
        try
        {
            if (Directory.Exists(PcDir))
                foreach (var js in Directory.GetFiles(PcDir, "*.frame.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    string dwg = js.Substring(0, js.Length - ".frame.json".Length) + ".dwg";
                    if (!File.Exists(dwg)) continue;
                    var def = FrameDef.FromJson(File.ReadAllText(js, Encoding.UTF8));
                    string file = Path.GetFileName(dwg);
                    l.Add(("F:" + file, def?.Name is { Length: > 0 } n ? n : Path.GetFileNameWithoutExtension(file)));
                }
        }
        catch { }
        l.Add((SimpleId, "단순 도곽"));
        return l;
    }

    /// <summary>지금 쓸 도곽의 <see cref="Items"/> 열쇠.</summary>
    internal static string CurrentItemId()
    {
        var c = Resolve();
        return c.User && c.Dwg != null ? "F:" + Path.GetFileName(c.Dwg) : c.Id == BundledId ? BundledId : SimpleId;
    }

    /// <summary>★[v112.2] 목록에서 고름 — K-water = 기본(current.json 지움) · 단순 = current.json «SIMPLE» · 저장한 도곽 = 그 파일(해시 다시 잼). 돌려줌 = (됨, 까닭).</summary>
    internal static (bool Ok, string Msg) Select(string itemId)
    {
        try
        {
            if (itemId == BundledId) { Reset(); return (true, ""); }
            Directory.CreateDirectory(PcDir);
            var opt = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            Current cur;
            if (itemId == SimpleId) cur = new Current { id = SimpleId, name = "단순 도곽", saved = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
            else if (itemId.StartsWith("F:", StringComparison.Ordinal))
            {
                string file = itemId.Substring(2);
                if (Path.GetFileName(file) != file) return (false, "파일 이름이 이상합니다");
                string dwg = Path.Combine(PcDir, file), js = Path.ChangeExtension(dwg, ".frame.json");
                if (!File.Exists(dwg) || !File.Exists(js)) return (false, "저장한 도곽 파일이 없습니다");
                var def = FrameDef.FromJson(File.ReadAllText(js, Encoding.UTF8));
                if (def == null) return (false, "도곽 정의를 못 읽었습니다");
                string h = Sha(dwg);
                cur = new Current { id = "U" + Short(h), name = def.Name is { Length: > 0 } n ? n : Path.GetFileNameWithoutExtension(file), file = file, sha256 = h, saved = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
            }
            else return (false, "모르는 도곽");
            File.WriteAllText(Path.Combine(PcDir, "current.json"), System.Text.Json.JsonSerializer.Serialize(cur, opt), new UTF8Encoding(false));
            try { DiagLog.Append($"\n■ 도곽 고름(도면화 창) — {cur.name} · {itemId}"); } catch { }
            return (true, "");
        }
        catch (System.Exception ex) { return (false, ex.Message); }
    }

    /// <summary>[기본으로] — current.json만 지움(복사본은 남김 — 다시 고를 때 해시 같으면 재사용).</summary>
    internal static void Reset()
    {
        try { string p = Path.Combine(PcDir, "current.json"); if (File.Exists(p)) File.Delete(p); } catch { }
    }

    /// <summary>도면설정 도곽 줄 — (이름, ⚠ 딱지 글, 풍선 글).</summary>
    internal static (string Name, string Badge, string Tip) Status()
    {
        var c = Resolve();
        var notes = new List<string>();
        if (c.Warn.Length > 0) notes.Add(c.Warn);
        notes.AddRange(c.Def.Notes);
        string badge = notes.Count == 0 ? "" : c.Warn.Length > 0 ? (c.Warn.Contains("없음") ? "파일 없음" : "파일 바뀜") : c.Def.Notes.Any(n => n.StartsWith("칸 못")) ? "칸 못 찾음" : "그림 자리 작음";
        string tip = (c.Dwg ?? "(코드로 그림)") + (c.DwgHash.Length > 0 ? $"\nSHA256 {Short(c.DwgHash)}…" : "")
                   + "\n칸: " + string.Join(" · ", c.Def.Found.Select(f => c.Def.Cells.TryGetValue(f, out var cell) ? $"{f} [{string.Join(",", cell.Rect.Select(v => v.ToString("0.#")))}]" : f))
                   + (notes.Count > 0 ? "\n⚠ " + string.Join("\n⚠ ", notes) : "");
        return (c.Label, badge, tip);
    }

    /// <summary>옆 DB(읽기 전용)에서 선분 · 글자 · 속성 정의를 모아 Core 검출에 건넨다 — 모형이 비었으면 첫 배치 종이 공간 · 블록 안 한 단계까지 터서.</summary>
    internal static FrameDef? DetectFile(string path, string name, out string error)
    {
        error = "";
        try
        {
            using var side = new Database(false, true);
            side.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
            side.CloseInput(true);
            var segs = new List<Seg>(); var txts = new List<Txt>(); var atts = new List<AttDef>();
            using (var tr = side.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(side), OpenMode.ForRead);
                if (!space.Cast<ObjectId>().Any())
                {
                    var ld = (DBDictionary)tr.GetObject(side.LayoutDictionaryId, OpenMode.ForRead);
                    foreach (DBDictionaryEntry de in ld)
                    {
                        var lo = (Layout)tr.GetObject(de.Value, OpenMode.ForRead);
                        if (lo.ModelType) continue;
                        space = (BlockTableRecord)tr.GetObject(lo.BlockTableRecordId, OpenMode.ForRead);
                        break;
                    }
                }
                Collect(tr, space, Matrix3d.Identity, 0, segs, txts, atts);
                tr.Commit();
            }
            return DetectFrame(segs, txts, atts, name, out error);
        }
        catch (System.Exception ex) { error = "도곽 파일을 못 읽음 — " + ex.Message; return null; }
    }

    static void Collect(Transaction tr, BlockTableRecord btr, Matrix3d xf, int depth, List<Seg> segs, List<Txt> txts, List<AttDef> atts)
    {
        foreach (ObjectId id in btr)
        {
            Entity? e;
            try { e = tr.GetObject(id, OpenMode.ForRead) as Entity; } catch { continue; }
            if (e == null) continue;
            try
            {
                switch (e)
                {
                    case Line ln:
                        { var a = ln.StartPoint.TransformBy(xf); var b = ln.EndPoint.TransformBy(xf); segs.Add(new(a.X, a.Y, b.X, b.Y)); break; }
                    case Polyline pl:
                        for (int i = 0; i < pl.NumberOfVertices - (pl.Closed ? 0 : 1); i++)
                        {
                            if (pl.GetSegmentType(i) != SegmentType.Line) continue;
                            var a = pl.GetPoint3dAt(i).TransformBy(xf); var b = pl.GetPoint3dAt((i + 1) % pl.NumberOfVertices).TransformBy(xf);
                            segs.Add(new(a.X, a.Y, b.X, b.Y));
                        }
                        break;
                    case AttributeDefinition ad when depth == 0:
                        { var p = ad.Position.TransformBy(xf); atts.Add(new(ad.Tag ?? "", ad.Prompt ?? "", p.X, p.Y)); break; }
                    case DBText t when e is not AttributeDefinition:
                        { var x = t.GeometricExtents; var a = x.MinPoint.TransformBy(xf); var b = x.MaxPoint.TransformBy(xf); txts.Add(new(t.TextString ?? "", Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y))); break; }
                    case MText mt:
                        { var x = mt.GeometricExtents; var a = x.MinPoint.TransformBy(xf); var b = x.MaxPoint.TransformBy(xf); txts.Add(new(mt.Text ?? "", Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y))); break; }
                    case BlockReference br when depth == 0:
                        {
                            var inner = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                            if (!inner.IsFromExternalReference) Collect(tr, inner, xf * br.BlockTransform, depth + 1, segs, txts, atts);
                            foreach (ObjectId aid in br.AttributeCollection)
                                if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar && !ar.Invisible)
                                {
                                    var x = ar.GeometricExtents; var a = x.MinPoint.TransformBy(xf); var b = x.MaxPoint.TransformBy(xf);
                                    txts.Add(new(ar.TextString ?? "", Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
                                }
                            break;
                        }
                }
            }
            catch { }
        }
    }

    // ══════════════════════════ 도면에 넣기(블록) ══════════════════════════

    /// <summary>블록 이름 — 정의 json(+ 이번 지울 칸)이 바뀌어도 새 이름(검토 M4).</summary>
    internal static string BlockName(Choice c, string clearMask)
        => c.Dwg == null ? $"{BlockPrefix}{SimpleId}_{Hash8(c.Def.ToJson() + clearMask).Substring(0, 4)}"
                         : $"{BlockPrefix}{c.Id}_{Short(c.DwgHash)}{Hash8(c.Def.ToJson() + clearMask).Substring(0, 4)}";

    /// <summary>도곽 블록 정의를 도면에 — 같은 이름 · 같은 표식이면 그대로 씀. 바깥 트랜잭션 밖에서 부른다(Database.Insert).
    /// <paramref name="clearCells"/> = 이번에 지울 칸(«always» + 값이 있는 «ifValue»). 돌려줌 = 정의 id(실패 = Null).</summary>
    internal static ObjectId Import(Database db, Choice c, IReadOnlyCollection<string> clearCells, System.Text.StringBuilder log, out bool created)
    {
        created = false;
        string mask = string.Join(",", clearCells.OrderBy(s => s, StringComparer.Ordinal));
        string name = BlockName(c, mask);
        string jh = Hash8(c.Def.ToJson() + mask).Substring(0, 4);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(name))
            {
                var old = (BlockTableRecord)tr.GetObject(bt[name], OpenMode.ForRead);
                var tag = ReadTag(tr, old);
                if (tag.Length >= 3 && tag[2] == jh) { tr.Commit(); log.AppendLine($"  도곽 블록 «{name}» 있음 — 그대로"); return old.ObjectId; }
                for (int i = 2; bt.Has(name); i++) name = $"{BlockName(c, mask)}_{i}";
                log.AppendLine($"  ⚠같은 이름 블록의 표식이 달라(손으로 고친 정의?) 새 이름 «{name}»");
            }
            tr.Commit();
        }
        var before = BlockNames(db);
        ObjectId bid;
        if (c.Dwg == null) bid = MakeSimple(db, c.Def, name, log);
        else
        {
            using var side = new Database(false, true);
            side.ReadDwgFile(c.Dwg, FileOpenMode.OpenForReadAndAllShare, true, "");
            side.CloseInput(true);
            using var clean = new Database(true, true);
            var ids = new ObjectIdCollection();
            using (var tr = side.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(side), OpenMode.ForRead);
                foreach (ObjectId id in ms) ids.Add(id);
                if (ids.Count == 0)
                {
                    var ld = (DBDictionary)tr.GetObject(side.LayoutDictionaryId, OpenMode.ForRead);
                    foreach (DBDictionaryEntry de in ld)
                    {
                        var lo = (Layout)tr.GetObject(de.Value, OpenMode.ForRead);
                        if (lo.ModelType) continue;
                        foreach (ObjectId id in (BlockTableRecord)tr.GetObject(lo.BlockTableRecordId, OpenMode.ForRead))
                            if (tr.GetObject(id, OpenMode.ForRead) is not Viewport) ids.Add(id);
                        break;
                    }
                }
                tr.Commit();
            }
            if (ids.Count > 0)
            {
                var map = new IdMapping();
                side.WblockCloneObjects(ids, SymbolUtilityServices.GetBlockModelSpaceId(clean), map, DuplicateRecordCloning.Ignore, false);
            }
            clean.Insunits = UnitsValue.Undefined;   // INSERT 명령 단위 환산이 끼지 않게(R5)
            bid = db.Insert(name, clean, true);
        }
        if (bid.IsNull) return bid;
        created = true;
        var nested = BlockNames(db).Except(before).Where(n => n != name).ToList();
        int erased = 0;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var btr = (BlockTableRecord)tr.GetObject(bid, OpenMode.ForWrite);
            btr.Units = UnitsValue.Undefined;
            var def = c.Def;
            var rects = clearCells.Where(def.Cells.ContainsKey).Select(n => def.Cells[n].Rect).Concat(def.ClearOnly).ToList();
            double k = def.K, ox = def.Origin.Length >= 2 ? def.Origin[0] : 0, oy = def.Origin.Length >= 2 ? def.Origin[1] : 0;
            bool Hit(Extents3d x) => rects.Any(r => CenterIn((x.MinPoint.X - ox) * k, (x.MinPoint.Y - oy) * k, (x.MaxPoint.X - ox) * k, (x.MaxPoint.Y - oy) * k, r));
            if (c.Dwg != null && rects.Count > 0)
                foreach (ObjectId id in btr)
                {
                    try
                    {
                        var e = tr.GetObject(id, OpenMode.ForRead);
                        if (e is AttributeDefinition) continue;
                        if ((e is DBText || e is MText) && Hit(((Entity)e).GeometricExtents)) { e.UpgradeOpen(); e.Erase(); erased++; }
                        else if (e is BlockReference br)
                            foreach (ObjectId aid in br.AttributeCollection)
                                if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar && Hit(ar.GeometricExtents)) { ar.UpgradeOpen(); ar.Erase(); erased++; }
                    }
                    catch { }
                }
            WriteTag(tr, btr, new[] { c.Id, c.DwgHash, jh }.Concat(nested).ToArray());
            tr.Commit();
        }
        log.AppendLine($"  도곽 블록 «{name}» 들임 — 박힌 글 {erased}개 지움 · 함께 들어온 정의 {(nested.Count == 0 ? "없음" : string.Join(" · ", nested))}");
        return bid;
    }

    static List<string> BlockNames(Database db)
    {
        var l = new List<string>();
        using var tr = db.TransactionManager.StartTransaction();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId id in bt) try { l.Add(((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name); } catch { }
        tr.Commit();
        return l;
    }

    internal static string[] ReadTag(Transaction tr, DBObject o)
    {
        try
        {
            if (o.ExtensionDictionary.IsNull) return Array.Empty<string>();
            var xd = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead);
            if (!xd.Contains(FrameTag)) return Array.Empty<string>();
            var xr = (Xrecord)tr.GetObject(xd.GetAt(FrameTag), OpenMode.ForRead);
            return xr.Data?.AsArray().Select(v => v.Value?.ToString() ?? "").ToArray() ?? Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }

    static void WriteTag(Transaction tr, DBObject o, string[] vals)
    {
        if (o.ExtensionDictionary.IsNull) o.CreateExtensionDictionary();
        var xd = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForWrite);
        var xr = new Xrecord { Data = new ResultBuffer(vals.Select(v => new TypedValue((int)DxfCode.Text, v)).ToArray()) };
        if (xd.Contains(FrameTag)) { var old = (Xrecord)tr.GetObject(xd.GetAt(FrameTag), OpenMode.ForWrite); old.Data = xr.Data; }
        else { xd.SetAt(FrameTag, xr); tr.AddNewlyCreatedDBObject(xr, true); }
    }

    /// <summary>«단순 도곽» 블록 — 코드로 그림(바깥 · 테두리 · 띠 · 칸 · 이름표). 파일이 없어도 도면화가 멈추지 않게(내 판단 11).</summary>
    static ObjectId MakeSimple(Database db, FrameDef def, string name, System.Text.StringBuilder log)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
        var btr = new BlockTableRecord { Name = name, Units = UnitsValue.Undefined, Origin = Point3d.Origin };
        ObjectId bid = bt.Add(btr);
        tr.AddNewlyCreatedDBObject(btr, true);
        ObjectId kst = Commands.ImportGisCommand.EnsureKoreanTextStyle(db, tr);
        void Add(Entity e) { e.Layer = "0"; e.ColorIndex = 0; btr.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); }
        void Rect(double[] r, double w)
        {
            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(r[0], r[1]), 0, w, w); pl.AddVertexAt(1, new Point2d(r[2], r[1]), 0, w, w);
            pl.AddVertexAt(2, new Point2d(r[2], r[3]), 0, w, w); pl.AddVertexAt(3, new Point2d(r[0], r[3]), 0, w, w);
            pl.Closed = true; Add(pl);
        }
        Rect(def.Outer, 0); Rect(def.Border, 0.7);
        var (lines, labels) = SimpleFrameDrawing();
        foreach (var l in lines) Add(new Line(new Point3d(l.X0, l.Y0, 0), new Point3d(l.X1, l.Y1, 0)));
        foreach (var t in labels)
        {
            var tx = new DBText { TextString = t.T, Height = t.H, HorizontalMode = TextHorizontalMode.TextCenter, VerticalMode = TextVerticalMode.TextVerticalMid };
            tx.Position = tx.AlignmentPoint = new Point3d(t.X, t.Y, 0);
            if (!kst.IsNull) tx.TextStyleId = kst;
            Add(tx);
        }
        tr.Commit();
        log.AppendLine($"  ⚠단순 도곽(코드로 그림) «{name}»");
        return bid;
    }

    /// <summary>탭을 지운 뒤 — 참조 없는 «DH도곽_*»과 그 표식에 적힌 함께 들어온 정의(다른 데서 안 쓸 때만 · Purge로 확인) · «DH_방위표». 트랜잭션 하나.</summary>
    internal static int PurgeUnused(Database db, System.Text.StringBuilder log)
    {
        int n = 0;
        var names = new List<string>();
        var nested = new List<string>();
        try
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    bool ours = btr.Name.StartsWith(BlockPrefix, StringComparison.Ordinal) || btr.Name == PlanSheet.NorthBlock;
                    if (!ours || btr.IsLayout) continue;
                    if (btr.GetBlockReferenceIds(true, false).Count > 0) continue;
                    var tag = ReadTag(tr, btr);
                    if (tag.Length > 3) nested.AddRange(tag.Skip(3));
                    btr.UpgradeOpen(); btr.Erase(); n++; names.Add(btr.Name);
                }
                tr.Commit();
            }
            // 함께 들어온 정의 — 다른 데서 안 쓸 때만(지운 도곽 정의가 물러난 뒤 Purge가 남기는 것만 지움)
            if (nested.Count > 0)
            {
                using var tr = db.TransactionManager.StartTransaction();
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var cand = new ObjectIdCollection();
                foreach (string nm in nested.Distinct()) if (bt.Has(nm)) cand.Add(bt[nm]);
                if (cand.Count > 0) db.Purge(cand);
                foreach (ObjectId id in cand) { var o = (BlockTableRecord)tr.GetObject(id, OpenMode.ForWrite); names.Add(o.Name); o.Erase(); n++; }
                tr.Commit();
            }
        }
        catch (System.Exception ex) { log.AppendLine("  도곽 블록 정리 실패 — " + ex.Message); }
        if (n > 0) log.AppendLine($"  안 쓰는 도곽 블록 {n}개 정리: {string.Join(" · ", names)}");
        return n;
    }
}
