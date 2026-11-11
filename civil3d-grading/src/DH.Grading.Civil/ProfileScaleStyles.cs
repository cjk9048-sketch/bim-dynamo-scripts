using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★[v113.1 · 계획_종단방향_축척.md 2.1 · 2.4] 종단도 축척 계획 — 칸 값(H · V) · 자동 한도 · 장 나누기 · 결과.
/// <para>부지 = <see cref="Site"/>(도면설정 «1. 계획지표면») · 진입도로 = <see cref="RoadSheets"/>가 만든다. <c>null</c>로 부르면(DHSHEET 다시 씌우기) 부지 칸 · 한 장.</para></summary>
internal sealed class ProfilePlan
{
    /// <summary>H 칸(0 = 자동 · &gt; 0 = 고정).</summary>
    internal double PinH;
    /// <summary>V 칸(−1 = H와 같게 · 0 = 자동 · &gt; 0 = 고정).</summary>
    internal double PinV = ProfileScaleFit.SameAsH;
    /// <summary>자동 H 한도(옛 규칙 = 끝없음).</summary>
    internal double HMax = double.PositiveInfinity;
    /// <summary>H에서 장 수(장 나누기 — 없으면 한 장만).</summary>
    internal Func<double, int>? SheetsFor;
    /// <summary>참이면 도곽은 재기 · 고르기까지만(장 나누기를 정하는 앞 판).</summary>
    internal bool PlanOnly;
    /// <summary>★[3단계] 진입도로 — 짓기 전 도면 주석 축척(부지 종단도가 건 값 · 0 = 없음). H와 다르면 짓는 동안만 H로 바꿨다가 되돌리고 이 도로 복사본 스타일에 비율(H ÷ 이 값)을 굽는다.</summary>
    internal double KeepAnno;
    /// <summary>진입도로 노선 핸들(복사본 이름 꼬리 · 부지 = 빈 글).</summary>
    internal string RoadTag = "";
    /// <summary>★[설치 전 검토 낮음] 고정 칸이 사람이 고른 값이 아니라 앞 판 자동이 정한 값(알림 글 «고정 축척»을 안 씀).</summary>
    internal bool FromAuto;

    // ── 결과
    internal double H, V;
    internal int Sheets = 1;
    internal bool Over;
    internal string Note = "";
    internal double NeedW, NeedV;

    internal bool Legacy => PinV < 0 && double.IsPositiveInfinity(HMax);

    /// <summary>부지 — 도면설정 칸(옛 도면 = 옛 규칙).</summary>
    internal static ProfilePlan Site() => new()
    {
        PinH = GradingSettings.ProfileScale,
        PinV = GradingSettings.ProfileScaleV,
        HMax = GradingSettings.ProfileLegacy ? double.PositiveInfinity : ProfileScaleFit.AutoHMax,
    };

    /// <summary>한 줄(로그 · 알림).</summary>
    internal string Text => ProfileScaleFit.ScaleText(H, V) + (Sheets > 1 ? $" · {Sheets}장" : "");
}

/// <summary>★★[v113.1 · 계획_종단방향_축척.md 2.1 · 2.5] 종단 축척 복사본 스타일 — 수직과장(V ≠ H) · 진입도로 주석 비율(H ≠ 부지 주석 축척).
/// <para>P2 실측(1104): 복사본 뷰 스타일 <c>GraphStyle.VerticalExaggeration</c> = 5 → 표고 5 m가 모형 25 m · 밴드 그대로.
/// P3 실측: 종단 밴드 칸 · 라벨은 CANNOSCALE을 따라 커진다(비 2.500) → 진입도로를 다른 H로 그리면 부지 주석 축척에서 그 도로 몫만 비율로 키운 복사본이 필요.
/// P4 실측: 종단 밴드 스타일 <c>CopyAsSibling</c>은 라벨 스타일까지 깊게 복사(6/6 · 라벨 22칸 모두 다른 객체) — 부지 글자를 안 건드림.</para></summary>
internal static class ProfileScaleStyles
{
    internal static readonly HashSet<string> TickProps = new(StringComparer.Ordinal) { "Size", "TextHeight", "OffsetX", "OffsetY" };
    internal static readonly HashSet<string> TitleProps = new(StringComparer.Ordinal) { "TextHeight", "OffsetX", "OffsetY", "BorderGap" };
    internal static readonly HashSet<string> GridProps = new(StringComparer.Ordinal) { "AxisOffsetLeft", "AxisOffsetRight", "AxisOffsetBottom", "AxisOffsetAbove" };
    internal static readonly HashSet<string> BandProps = new(StringComparer.Ordinal) { "BandHeight", "TextHeight", "TextBoxWidth" };
    static readonly HashSet<string> LabelProps = new(StringComparer.Ordinal)
    {
        "Height", "XOffset", "YOffset", "MaxWidth", "StartPointXOffset", "StartPointYOffset", "EndPointXOffset", "EndPointYOffset", "FixedLength", "BlockHeight", "Gap",
    };

    static ObjectId FindByName(Transaction tr, System.Collections.IEnumerable coll, string name)
    {
        foreach (ObjectId id in coll)
            try { if (tr.GetObject(id, OpenMode.ForRead) is CivilDb.Styles.StyleBase sb && sb.Name == name) return id; } catch { }
        return ObjectId.Null;
    }

    /// <summary>수직과장 복사본 — 이름 = 원본 + «(DH H1:h V1:v[@anno #도로])» · 없으면 원본에서 복사 · 과장 = H ÷ V(같으면 안 씀).
    /// 진입도로 비율 복사본(<paramref name="anno"/> ≠ H)은 그 도로만 쓰는 이름이라 지난 판 것은 <see cref="DropRoadClones"/>가 먼저 지운다.</summary>
    internal static ObjectId EnsureView(Database db, ObjectId srcId, double h, double v, double anno, string tag, System.Text.StringBuilder log)
    {
        var cdoc = CivilApp.CivilDocument.GetCivilDocument(db);
        using var tr = db.TransactionManager.StartTransaction();
        var src = (CivilDb.Styles.StyleBase)tr.GetObject(srcId, OpenMode.ForRead);
        string want = ProfileScaleFit.BaseName(src.Name) + ProfileScaleFit.CloneSuffix(h, v, anno, Math.Abs(anno - h) > 1e-6 && anno > 0 ? tag : null);
        ObjectId id = FindByName(tr, cdoc.Styles.ProfileViewStyles, want);
        bool made = false;
        if (id.IsNull) { id = src.CopyAsSibling(want); made = true; }
        if (id.IsNull) throw new InvalidOperationException($"축척 복사본 «{want}»을 못 만듦(같은 이름?)");
        var vs = (CivilDb.Styles.ProfileViewStyle)tr.GetObject(id, OpenMode.ForWrite);
        double ve = h / v, was;
        using (var gs = vs.GraphStyle) { was = gs.VerticalExaggeration; if (Math.Abs(was - ve) > 1e-12) gs.VerticalExaggeration = ve; }
        tr.Commit();
        log.AppendLine($"   종단 축척 복사본 «{want}» {(made ? "새로" : "있던 것")} · 수직과장 {was:0.####} → {ve:0.####}");
        return id;
    }

    /// <summary>★[3단계] 이 도로의 지난 판 복사본(뷰 · 밴드 스타일 — 이름 꼬리 « #핸들)»)을 지운다 — 옛 종단뷰를 지운 뒤에 부른다(쓰는 뷰가 없어야 지워짐).
    /// 못 지우면 이름에 «옛»을 붙여 비켜 둔다(같은 이름을 새로 만들 수 있게 · 비율을 두 번 곱하지 않게).</summary>
    internal static int DropRoadClones(Database db, string handle, System.Text.StringBuilder log)
    {
        if (string.IsNullOrEmpty(handle)) return 0;
        int n = 0, moved = 0;
        try
        {
            var all = ProfileStyleTemplate.CollectAll(db, CivilApp.CivilDocument.GetCivilDocument(db));
            using var tr = db.TransactionManager.StartTransaction();
            int q = 0;
            foreach (var (id, name) in all)
            {
                if (!ProfileScaleFit.IsRoadClone(name, handle)) continue;
                // ★[v117.5.1] 이름부터 비킨다(지운 스타일이 이름을 쥐고 있으면 다음 판의 같은 이름 CopyAsSibling이 빈 Id — st1 · st2 실측) — 못 지우면 이 이름으로 남음
                var sb = (CivilDb.Styles.StyleBase)tr.GetObject(id, OpenMode.ForWrite);
                try { sb.Name = name.Replace(" #" + handle + ")", $" #{handle} 옛{DateTime.Now:MMddHHmmss}_{++q})"); } catch { }
                try { sb.Erase(); n++; } catch { moved++; }
            }
            tr.Commit();
        }
        catch (Exception ex) { log.AppendLine("  지난 판 축척 복사본 정리 실패 — " + ex.Message); }
        if (n + moved > 0) log.AppendLine($"  지난 판 축척 복사본 지움 {n}" + (moved > 0 ? $" · 못 지워 이름 바꿈 {moved}" : ""));
        return n;
    }

    /// <summary>값이 있는 double 속성(get/set)을 k배 — 이름표에 든 것만.
    /// <para>★[v117.7.1] 원래 값을 <b>모두 먼저</b> 읽고 각각 «원래 × k»로 쓴 뒤 다시 읽어 어긋난 것을 다시 쓴다(차례와 무관).
    /// ★[v117.8 · xvslot 실측] 횡단 축 눈금 OffsetX k²배의 원인은 차례가 아니었다 — 한 객체 안 칸끼리는 서로 안 바뀐다(진입도로 종단 스타일 칸 공유 0).
    /// 중심축 · 왼쪽축이 띄우기 칸을 함께 써서 두 번 곱한 것 → <see cref="XsecScaleStyles"/>.ScaleView가 스타일 전체를 먼저 읽는다.</para></summary>
    internal static int ScaleProps(object o, HashSet<string> names, double k)
    {
        var want = new List<(System.Reflection.PropertyInfo P, double V)>();
        foreach (var pi in o.GetType().GetProperties())
        {
            if (!names.Contains(pi.Name) || pi.PropertyType != typeof(double) || !pi.CanWrite || !pi.CanRead) continue;
            try { double v = (double)pi.GetValue(o)!; if (double.IsFinite(v) && v != 0) want.Add((pi, v * k)); } catch { }
        }
        int c = 0;
        foreach (var (pi, v) in want)
            try { pi.SetValue(o, v); c++; } catch { }
        // 뒤 칸을 쓰다 앞 칸이 바뀌었으면 다시(두 번까지 — 서로 물고 늘어지면 마지막 쓴 값)
        for (int pass = 0; pass < 2; pass++)
        {
            bool again = false;
            foreach (var (pi, v) in want)
                try
                {
                    double now = (double)pi.GetValue(o)!;
                    if (Math.Abs(now - v) > 1e-9 * Math.Max(1.0, Math.Abs(v))) { pi.SetValue(o, v); again = true; }
                }
                catch { }
            if (!again) break;
        }
        return c;
    }

    /// <summary>Civil 묶음 속성(PropertyDouble — <c>Value</c>)을 k배.</summary>
    static int ScaleWrapped(object grp, double k)
    {
        int c = 0;
        foreach (var pi in grp.GetType().GetProperties())
        {
            if (!LabelProps.Contains(pi.Name)) continue;
            try
            {
                var w = pi.GetValue(grp);
                var vp = w?.GetType().GetProperty("Value");
                if (vp == null || vp.PropertyType != typeof(double) || !vp.CanWrite) continue;
                double v = (double)vp.GetValue(w)!;
                if (double.IsFinite(v) && v != 0) { vp.SetValue(w, v * k); c++; }
                (w as IDisposable)?.Dispose();
            }
            catch { }
        }
        return c;
    }

    /// <summary>라벨 스타일 하나의 글자 · 선 · 블록 · 눈금 · 테두리 크기를 k배.</summary>
    internal static int ScaleLabel(Transaction tr, ObjectId lsId, double k, HashSet<ObjectId> done)
    {
        if (lsId.IsNull || !done.Add(lsId)) return 0;
        int c = 0;
        try
        {
            if (tr.GetObject(lsId, OpenMode.ForWrite) is not CivilDb.Styles.LabelStyle ls) return 0;
            foreach (CivilDb.Styles.LabelStyleComponentType t in Enum.GetValues(typeof(CivilDb.Styles.LabelStyleComponentType)))
            {
                ObjectIdCollection comps;
                try { comps = ls.GetComponents(t); } catch { continue; }
                if (comps == null) continue;
                foreach (ObjectId cid in comps)
                {
                    try
                    {
                        var comp = tr.GetObject(cid, OpenMode.ForWrite);
                        foreach (var gp in comp.GetType().GetProperties())
                        {
                            if (gp.Name is not ("Text" or "Line" or "Block" or "Tick" or "Border")) continue;
                            try { var g = gp.GetValue(comp); if (g != null) { c += ScaleWrapped(g, k); (g as IDisposable)?.Dispose(); } } catch { }
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
        return c;
    }

    /// <summary>뷰 스타일의 축 눈금 · 축 제목 · 그래프 제목 · 축 오프셋(종이 값)을 k배.</summary>
    static int ScaleView(CivilDb.Styles.ProfileViewStyle vs, double k)
    {
        int c = 0;
        foreach (var ax in new[] { vs.LeftAxis, vs.RightAxis, vs.TopAxis, vs.BottomAxis })
            using (ax)
            {
                // 수평 기하 눈금은 위 · 아래 축에만 있다(왼쪽 · 오른쪽은 읽기만 해도 예외 — V113 R3 실측)
                foreach (var get in new Func<CivilDb.Styles.AxisTickStyle>[] { () => ax.MajorTickStyle, () => ax.MinorTickStyle, () => ax.HorizontalGeometryTickStyle })
                    try { using var t = get(); c += ScaleProps(t, TickProps, k); } catch { }
                try { using var ti = ax.TitleStyle; c += ScaleProps(ti, TitleProps, k); } catch { }
            }
        try { using var gs = vs.GridStyle; c += ScaleProps(gs, GridProps, k); } catch { }
        try { using var gr = vs.GraphStyle; using var ts = gr.TitleStyle; c += ScaleProps(ts, TitleProps, k); } catch { }
        return c;
    }

    /// <summary>★★[3단계] 진입도로 종단뷰들 — 뷰 스타일(이미 이 도로 복사본) · 밴드 스타일(이 도로 복사본으로 바꿈 · 라벨까지 깊은 복사)의 종이 크기와 밴드 칸 간격을 k배.
    /// 짓는 동안 CANNOSCALE = H였던 모양을 부지 주석 축척(anno)에서 그대로 보이게 한다(종이 크기 × k × anno = 종이 × H). 돌려줌 = 한 줄.</summary>
    internal static string BakeRatio(Database db, IReadOnlyList<ObjectId> pvIds, double h, double v, double anno, string tag, double k, System.Text.StringBuilder log)
    {
        if (!(k > 0) || Math.Abs(k - 1) < 1e-9 || pvIds.Count == 0) return "";
        var (nView, nBand, nProp, nLabel) = Bake(db, pvIds, ProfileScaleFit.CloneSuffix(h, v, anno, tag), tag, k, log);
        string msg = $"주석 비율 ×{k:0.###}(1:{h:0} ÷ 부지 1:{anno:0}) — 뷰 스타일 {nView} · 밴드 복사본 {nBand} · 크기 칸 {nProp} · 라벨 칸 {nLabel}";
        log.AppendLine("  " + msg);
        return msg;
    }

    /// <summary>★★[v117.5.1 · JACK 1111 «진입도로 종단을 PVI 수정 · [업데이트] 뒤 부지 종단을 만들거나 고치면 진입도로 종단 축척이 이상하게 변함»]
    /// 그려 둔 진입도로 종단뷰들을 주석 축척 <paramref name="a0"/>(그린 때) → <paramref name="a1"/>(지금)에서 같은 꼴로 — 이 도로 복사본 스타일(없으면 지금 쓰는 스타일에서 복사)에 a0 ÷ a1을 곱한다.
    /// <para>실측(pvs1 · pvs2): 부지 [종단도]가 도면 주석 축척을 1:500 → 1:100으로 걸면 1:500으로 그린 도로 종단뷰의 밴드 칸 · 밴드 글자 · 축 눈금 글자가 모두 1/5로 줄었다(CANNOSCALE을 따름) — 도곽 · 표고바는 그대로.</para>
    /// 돌려줌 = 한 줄(비율이 1이거나 뷰가 없으면 빈 글).</summary>
    internal static string Rebake(Database db, IReadOnlyList<ObjectId> pvIds, double h, double v, double a0, double a1, string tag, System.Text.StringBuilder log)
    {
        if (!(a0 > 0) || !(a1 > 0) || Math.Abs(a0 - a1) < 1e-6 || pvIds.Count == 0) return "";
        double k = a0 / a1;
        var (nView, nBand, nProp, nLabel) = Bake(db, pvIds, ProfileScaleFit.CloneSuffix(h, v, a1, tag), tag, k, log);
        return $"주석 1:{a0:0} → 1:{a1:0} · ×{k:0.###} — 뷰 스타일 {nView} · 밴드 복사본 {nBand} · 크기 칸 {nProp} · 라벨 칸 {nLabel}";
    }

    /// <summary>비율 굽기 본체 — 뷰 스타일: 이 도로 복사본이면 그 자리에서 k배(이름 꼬리를 <paramref name="suffix"/>로) · 아니면(부지와 함께 쓰는 스타일) 이 도로 복사본을 만들어 걸고 k배.
    /// 밴드 스타일: 늘 새 복사본(라벨까지 깊은 복사)에 k배 · 밴드 칸 간격도 k배 · 갈아 낀 지난 판 이 도로 복사본은 끝에 지운다. 같은 이름이 남아 있으면 «옛»을 붙여 비켜 둔다.</summary>
    static (int View, int Band, int Prop, int Label) Bake(Database db, IReadOnlyList<ObjectId> pvIds, string suffix, string tag, double k, System.Text.StringBuilder log)
    {
        int nView = 0, nBand = 0, nProp = 0, nLabel = 0;
        var doneStyle = new HashSet<ObjectId>(); var doneLabel = new HashSet<ObjectId>();
        var bandMap = new Dictionary<ObjectId, ObjectId>();
        var viewMap = new Dictionary<ObjectId, ObjectId>();
        var replaced = new HashSet<ObjectId>();
        var mine = new Dictionary<string, ObjectId>(StringComparer.Ordinal);   // 이 도로 복사본 이름 → id(같은 이름 비키기)
        var cdoc = CivilApp.CivilDocument.GetCivilDocument(db);
        try { foreach (var (sid0, nm0) in ProfileStyleTemplate.CollectAll(db, cdoc)) if (ProfileScaleFit.IsRoadClone(nm0, tag)) mine[nm0] = sid0; } catch { }
        using var tr = db.TransactionManager.StartTransaction();
        // 새로 지을 이름이 이미 있으면(지난 판 찌꺼기) 비켜 둠 — CopyAsSibling · 이름 바꾸기가 같은 이름에서 실패하지 않게
        void MakeRoom(string want, ObjectId keep)
        {
            if (!mine.TryGetValue(want, out var old) || old == keep || old.IsErased) return;
            try { ((CivilDb.Styles.StyleBase)tr.GetObject(old, OpenMode.ForWrite)).Name = want.Replace(" #" + tag + ")", $" #{tag} 옛{DateTime.Now:MMddHHmmss})"); } catch { }
            mine.Remove(want);
        }
        foreach (var pvId in pvIds)
        {
            if (pvId.IsNull || pvId.IsErased || tr.GetObject(pvId, OpenMode.ForWrite) is not CivilDb.ProfileView pv) continue;
            // 뷰 스타일 — 이 도로 복사본만 키운다(부지와 함께 쓰던 것이면 복사본을 만들어 건다 — 부지 것을 키우지 않게)
            var vs0 = (CivilDb.Styles.StyleBase)tr.GetObject(pv.StyleId, OpenMode.ForRead);
            string vsName = vs0.Name ?? "", vWant = ProfileScaleFit.BaseName(vsName) + suffix;
            if (ProfileScaleFit.IsRoadClone(vsName, tag))
            {
                if (doneStyle.Add(pv.StyleId))
                {
                    var vsw = (CivilDb.Styles.ProfileViewStyle)tr.GetObject(pv.StyleId, OpenMode.ForWrite);
                    nProp += ScaleView(vsw, k); nView++;
                    if (vsName != vWant) { MakeRoom(vWant, pv.StyleId); try { vsw.Name = vWant; mine[vWant] = pv.StyleId; } catch { } }
                }
            }
            else
            {
                if (!viewMap.TryGetValue(pv.StyleId, out var vc))
                {
                    MakeRoom(vWant, ObjectId.Null);
                    vc = vs0.CopyAsSibling(vWant); mine[vWant] = vc;
                    if (vc.IsNull) throw new InvalidOperationException($"뷰 스타일 복사본 «{vWant}»을 못 만듦(같은 이름?)");
                    nProp += ScaleView((CivilDb.Styles.ProfileViewStyle)tr.GetObject(vc, OpenMode.ForWrite), k); nView++;
                    viewMap[pv.StyleId] = vc; doneStyle.Add(vc);
                }
                pv.StyleId = vc;
            }
            using var items = pv.Bands.GetBottomBandItems();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var sid = it.BandStyleId;
                if (sid.IsNull) continue;
                if (!bandMap.TryGetValue(sid, out var cid))
                {
                    var src = (CivilDb.Styles.StyleBase)tr.GetObject(sid, OpenMode.ForRead);
                    string want = ProfileScaleFit.BaseName(src.Name) + suffix;
                    if (ProfileScaleFit.IsRoadClone(src.Name, tag)) replaced.Add(sid);
                    MakeRoom(want, ObjectId.Null);
                    cid = src.CopyAsSibling(want); mine[want] = cid;
                    if (cid.IsNull) throw new InvalidOperationException($"밴드 스타일 복사본 «{want}»을 못 만듦(같은 이름?)");
                    var cp = tr.GetObject(cid, OpenMode.ForWrite);
                    nProp += ScaleProps(cp, BandProps, k);
                    foreach (var pi in cp.GetType().GetProperties().Where(p => p.PropertyType == typeof(ObjectId) && p.Name.EndsWith("LabelStyleId", StringComparison.Ordinal)))
                    {
                        try
                        {
                            var lid = (ObjectId)pi.GetValue(cp)!;
                            var lsrc = (ObjectId)pi.GetValue(src)!;
                            if (!lid.IsNull && lid == lsrc) { log.AppendLine($"   ⚠밴드 «{src.Name}» {pi.Name} 라벨이 원본과 같은 객체(얕은 복사) — 그 라벨은 안 키움"); continue; }
                            nLabel += ScaleLabel(tr, lid, k, doneLabel);
                        }
                        catch { }
                    }
                    bandMap[sid] = cid; nBand++;
                }
                it.BandStyleId = cid;
                try { it.Gap *= k; } catch { }
                try { if (it.StaggerLineHeight > 0) it.StaggerLineHeight *= k; } catch { }
            }
            pv.Bands.SetBottomBandItems(items);
        }
        tr.Commit();
        // 갈아 낀 지난 판 이 도로 밴드 복사본 — 이 도로 뷰들만 쓰던 것이라 이제 아무도 안 씀
        if (replaced.Count > 0)
            try
            {
                using var trE = db.TransactionManager.StartTransaction();
                int nE = 0;
                int q = 0;
                foreach (var id in replaced)
                    try
                    {
                        var sb = (CivilDb.Styles.StyleBase)trE.GetObject(id, OpenMode.ForWrite);   // 이름부터 비킴(DropRoadClones와 같은 까닭)
                        try { sb.Name = (sb.Name ?? "").Replace(" #" + tag + ")", $" #{tag} 옛{DateTime.Now:MMddHHmmss}_{++q})"); } catch { }
                        sb.Erase(); nE++;
                    }
                    catch { }
                trE.Commit();
                log.AppendLine($"   갈아 낀 지난 판 밴드 복사본 지움 {nE}/{replaced.Count}");
            }
            catch (Exception ex) { log.AppendLine("   지난 판 밴드 복사본 지우기 실패 — " + ex.Message); }
        return (nView, nBand, nProp, nLabel);
    }
}
