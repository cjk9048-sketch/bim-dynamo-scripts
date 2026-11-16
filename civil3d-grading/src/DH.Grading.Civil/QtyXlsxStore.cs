using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>★[수량산출서] 창 값 — 도면(NOD «DH_QTYXLSX_INPUT» Xrecord)에 적어 둔다. 터파기 구분(m)은 [횡단도]도 읽어 가서 횡단 수량표와 엑셀이 한 값을 쓴다.</summary>
internal sealed class QtyXlsxInput
{
    internal const string Key = "DH_QTYXLSX_INPUT";
    static readonly System.Globalization.CultureInfo Ic = System.Globalization.CultureInfo.InvariantCulture;

    public string Facility = "";
    public double DeepLimit = XsecQuantity.DeepLimit;
    public double Density = 18, Dbh = 0.22, Root = 0.25;
    /// <summary>수고 직접 입력(m) — NaN = 임상도 면적가중평균.</summary>
    public double Height = double.NaN;
    public double KRoot = 1.3, F = 0.5, W0 = 0.8, P = 0.5, KForm = 0.5, Surcharge = 1.3;

    string ToText() => string.Join("\n", new[]
    {
        "facility=" + Facility.Replace("\n", " "), "deep=" + DeepLimit.ToString("R", Ic), "density=" + Density.ToString("R", Ic), "dbh=" + Dbh.ToString("R", Ic),
        "root=" + Root.ToString("R", Ic), "height=" + (double.IsNaN(Height) ? "" : Height.ToString("R", Ic)), "kroot=" + KRoot.ToString("R", Ic), "f=" + F.ToString("R", Ic),
        "w0=" + W0.ToString("R", Ic), "p=" + P.ToString("R", Ic), "kform=" + KForm.ToString("R", Ic), "sur=" + Surcharge.ToString("R", Ic),
    });

    static QtyXlsxInput FromText(string t)
    {
        var r = new QtyXlsxInput();
        foreach (var line in t.Split('\n'))
        {
            int i = line.IndexOf('='); if (i <= 0) continue;
            string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).TrimEnd('\r');
            bool ok = double.TryParse(v, System.Globalization.NumberStyles.Float, Ic, out double d) && double.IsFinite(d);
            switch (k)
            {
                case "facility": r.Facility = v; break;
                case "deep": if (ok && d >= 1 && d <= 30) r.DeepLimit = d; break;
                case "density": if (ok && d >= 0) r.Density = d; break;
                case "dbh": if (ok && d > 0) r.Dbh = d; break;
                case "root": if (ok && d > 0) r.Root = d; break;
                case "height": r.Height = ok && d > 0 ? d : double.NaN; break;
                case "kroot": if (ok && d > 0) r.KRoot = d; break;
                case "f": if (ok && d > 0) r.F = d; break;
                case "w0": if (ok && d > 0) r.W0 = d; break;
                case "p": if (ok && d >= 0) r.P = d; break;
                case "kform": if (ok && d > 0) r.KForm = d; break;
                case "sur": if (ok && d > 0) r.Surcharge = d; break;
            }
        }
        return r;
    }

    /// <summary>도면에서 읽는다. 없으면 기본값 · 시설 이름 = 도면화 «공사명» → 없으면 «부지».</summary>
    internal static QtyXlsxInput Read(Database db)
    {
        var r = new QtyXlsxInput();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (nod.Contains(Key) && tr.GetObject(nod.GetAt(Key), OpenMode.ForRead) is Xrecord xr && xr.Data != null)
                r = FromText(string.Join("\n", xr.Data.AsArray().Select(v => v.Value as string ?? "")));
            tr.Commit();
        }
        catch { }
        if (r.Facility.Trim().Length == 0)
        {
            try { r.Facility = SheetBookInput.Read(db).Project.Trim(); } catch { }
            if (r.Facility.Length == 0) r.Facility = "부지";
        }
        return r;
    }

    internal void Write(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        Xrecord xr;
        if (nod.Contains(Key)) xr = (Xrecord)tr.GetObject(nod.GetAt(Key), OpenMode.ForWrite);
        else { nod.UpgradeOpen(); xr = new Xrecord(); nod.SetAt(Key, xr); tr.AddNewlyCreatedDBObject(xr, true); }
        xr.Data = new ResultBuffer(ToText().Split('\n').Select(s => new TypedValue((int)DxfCode.Text, s)).ToArray());
        tr.Commit();
    }

    /// <summary>[횡단도] 처음에 부른다 — 도면에 적힌 터파기 구분을 프로세스 값에 적용(횡단 수량표 = 엑셀). 못 읽으면 그대로.</summary>
    internal static void ApplyDeepLimit(Database db)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            double v = 4.5;
            if (nod.Contains(Key) && tr.GetObject(nod.GetAt(Key), OpenMode.ForRead) is Xrecord xr && xr.Data != null)
                v = FromText(string.Join("\n", xr.Data.AsArray().Select(x => x.Value as string ?? ""))).DeepLimit;
            tr.Commit();
            XsecQuantity.DeepLimit = v;
        }
        catch { }
    }
}

/// <summary>★[수량산출서] 마지막 [횡단도]가 낸 측점별 수량 — 도면별 · 부지 / 진입도로 따로(이 세션 메모리). 없으면 «횡단 없음 → [횡단도] 먼저».</summary>
internal static class QtyKeeper
{
    internal sealed record Snap(List<(string Name, double St)> Stations,
        Dictionary<string, XsecQty> Map, Dictionary<string, QtyLedger> Ledgers, double Limit);

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Database, Snap> Site = new(), Road = new();

    internal static void Store(Database db, bool road, List<(string Name, double St)> st, Dictionary<string, XsecQty> map, Dictionary<string, QtyLedger> led)
    {
        var t = road ? Road : Site;
        t.Remove(db); t.Add(db, new Snap(st, map, led, XsecQuantity.DeepLimit));
    }

    internal static Snap? Of(Database db, bool road) => (road ? Road : Site).TryGetValue(db, out var s) ? s : null;

    /// <summary>스냅 → 모델 측점. 원장(지층별)이 있으면 그 값 · 없으면 XsecQty.</summary>
    internal static List<QtyStation> Stations(Snap s, List<string> notes)
    {
        var res = new List<QtyStation>();
        bool otherRock = false;
        double N(double a, double b) => double.IsNaN(a) ? b : a;
        double Add(double a, double b) => double.IsNaN(a) ? b : double.IsNaN(b) ? a : a + b;
        foreach (var (name, st) in s.Stations)
        {
            s.Map.TryGetValue(name, out var q);
            bool hasQ = s.Map.ContainsKey(name);
            s.Ledgers.TryGetValue(name, out var led);
            var r = new QtyStation { Name = name, Sta = st };
            if (led != null)
            {
                r.Fill = N(led.Get(QtyKey.OfFill()), hasQ ? q.Fill : double.NaN);
                r.Backfill = N(led.Get(QtyKey.OfBackfill()), hasQ ? q.Backfill : double.NaN);
                r.Cut = led.Get(QtyKey.OfCut(RockClass.Soil)); r.CutWeathered = led.Get(QtyKey.OfCut(RockClass.Weathered));
                r.ExcSoilLe = led.Get(QtyKey.OfExc(RockClass.Soil, DepthClass.Le, WaterClass.Land));
                r.ExcSoilGt = led.Get(QtyKey.OfExc(RockClass.Soil, DepthClass.Gt, WaterClass.Land));
                r.ExcSoilWater = Add(led.Get(QtyKey.OfExc(RockClass.Soil, DepthClass.Le, WaterClass.Water)), led.Get(QtyKey.OfExc(RockClass.Soil, DepthClass.Gt, WaterClass.Water)));
                r.ExcWeathered = Add(led.Get(QtyKey.OfExc(RockClass.Weathered, DepthClass.Le, WaterClass.Land)), led.Get(QtyKey.OfExc(RockClass.Weathered, DepthClass.Gt, WaterClass.Land)));
                r.ExcWeatheredWater = Add(led.Get(QtyKey.OfExc(RockClass.Weathered, DepthClass.Le, WaterClass.Water)), led.Get(QtyKey.OfExc(RockClass.Weathered, DepthClass.Gt, WaterClass.Water)));
                foreach (var k in led.Keys)
                    if ((k.Kind == QtyKeyKind.Cut || k.Kind == QtyKeyKind.Exc) && k.Rock != RockClass.Soil && k.Rock != RockClass.Weathered && led.Get(k) > 0) otherRock = true;
                // 원장에 절토 · 터파기 열쇠가 하나도 없으면(지층 안 쓴 도면) 횡단 전체 값을 쓴다
                if (double.IsNaN(r.Cut) && hasQ) r.Cut = q.Cut;
                if (double.IsNaN(r.ExcSoilLe) && double.IsNaN(r.ExcSoilGt) && hasQ) { r.ExcSoilLe = q.ExcShallow; r.ExcSoilGt = q.ExcDeep; }
            }
            else if (hasQ) { r.Fill = q.Fill; r.Cut = q.Cut; r.Backfill = q.Backfill; r.ExcSoilLe = q.ExcShallow; r.ExcSoilGt = q.ExcDeep; }
            if (hasQ) { r.FillW = q.FillW; r.CutW = q.CutW; }
            if (led != null)
            {
                // ★[JACK 1116] 횡단 수량표 오른쪽 칸 — 면고르기 · 벌개 · 표토는 측점별 길이(2D 산출서 «길이» 열)
                double sF = led.Get(QtyKey.OfRight(QtyKeyKind.Smooth, false)), sC = led.Get(QtyKey.OfRight(QtyKeyKind.Smooth, true));
                if (!double.IsNaN(sF) || !double.IsNaN(sC)) { r.FillW = N(sF, 0); r.CutW = N(sC, 0); }
                r.Clear = Add(led.Get(QtyKey.OfRight(QtyKeyKind.Clear, false)), led.Get(QtyKey.OfRight(QtyKeyKind.Clear, true)));
                r.Strip = Add(led.Get(QtyKey.OfRight(QtyKeyKind.Strip, false)), led.Get(QtyKey.OfRight(QtyKeyKind.Strip, true)));
                r.Bench = led.Get(QtyKey.OfRight(QtyKeyKind.Bench, false));   // ★[JACK 1116] 층따기 단면적
            }
            res.Add(r);
        }
        if (otherRock) notes.Add("연암 · 보통암 · 경암 수량은 표에 넣지 않음(토사 · 풍화암만)");
        return res;
    }
}
