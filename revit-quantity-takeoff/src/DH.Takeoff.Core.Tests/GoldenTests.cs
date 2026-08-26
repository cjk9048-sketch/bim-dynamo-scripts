using System.Globalization;
using System.Text.Json;
using DH.Takeoff.Core;
using Xunit;

namespace DH.Takeoff.Core.Tests;

/// <summary>
/// 골든 회귀 — 실 워크북 추출 데이터(samples/)로 Quantity_Report 합계를 재현.
/// golden_verify.py(Python 참조구현)와 동일 결과를 C# 엔진이 내는지 검증.
/// </summary>
public class GoldenTests
{
    private static string DataPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "data", name);

    [Fact]
    public void Reproduces_Golden_Discipline_Totals()
    {
        var (model, golden) = LoadFixture();
        var instances = LoadInstances();
        var res = new TakeoffEngine(model).Compute(instances);

        // 무근콘크리트·거푸집 3종: 센트 단위 일치 / 철근콘크리트: ROUND 행분할 잔차(±0.05)
        var tol = new Dictionary<string, double>
        {
            ["무근콘크리트"] = 0.011,
            ["거푸집(합판6회)"] = 0.011,
            ["거푸집(합판4회)"] = 0.011,
            ["거푸집(합판3회)"] = 0.011,
            ["철근콘크리트"] = 0.05,
        };

        Assert.NotEmpty(golden);
        foreach (var (disc, g) in golden)
        {
            Assert.True(res.DisciplineTotal.ContainsKey(disc), $"공종 누락: {disc}");
            double got = res.DisciplineTotal[disc];
            double t = tol.GetValueOrDefault(disc, 0.05);
            Assert.True(System.Math.Abs(got - g) <= t,
                $"{disc}: 계산={got} 골든={g} 차이={got - g:0.###} (허용 ±{t})");
        }
    }

    [Fact]
    public void Column_Concrete_Uses_Representative_Convention()
    {
        var (model, _) = LoadFixture();
        var res = new TakeoffEngine(model).Compute(LoadInstances());
        // 기둥 C1 콘크리트 = 대표 1개 ≈ 0.45 (60개 합산 26.73 아님)
        double c1 = res.CodeSubtotal["철근콘크리트"]["C1"];
        Assert.True(System.Math.Abs(c1 - 0.45) <= 0.011, $"C1 철근콘크리트={c1} (기대 ≈0.45)");
    }

    // ---- 로더 ----

    private sealed class FixtureDto
    {
        public Dictionary<string, Dictionary<string, string>> Formulas { get; set; } = new();
        public Dictionary<string, double> Calc { get; set; } = new();
        public List<List<string>> Representative { get; set; } = new();
        public Dictionary<string, double> GoldenDisc { get; set; } = new();
    }

    private static (WorkbookModel, Dictionary<string, double>) LoadFixture()
    {
        var json = File.ReadAllText(DataPath("golden-fixture.json"));
        var dto = JsonSerializer.Deserialize<FixtureDto>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var model = new WorkbookModel
        {
            Formulas = dto.Formulas,
            Calc = dto.Calc,
        };
        foreach (var pair in dto.Representative)
            if (pair.Count == 2) model.Representative.Add((pair[0], pair[1]));
        return (model, dto.GoldenDisc);
    }

    private static List<Instance> LoadInstances()
    {
        var lines = File.ReadAllLines(DataPath("ReservoirData.csv"));
        var list = new List<Instance>();
        // header(1행) skip. 열: 0=Code,1=Class,2=Category,3=L1..10=ETC
        string[] dimKeys = { "L1", "L2", "L3", "W1", "W2", "W3", "H", "ETC" };
        for (int r = 1; r < lines.Length; r++)
        {
            var line = lines[r].TrimStart('﻿');
            if (string.IsNullOrWhiteSpace(line)) continue;
            var f = line.Split(',');
            if (f.Length < 11) continue;
            var inst = new Instance
            {
                Code = f[0].Trim(),
                Cls = f[1].Trim(),
                Category = f[2].Trim(),
            };
            for (int k = 0; k < dimKeys.Length; k++)
                inst.Dims[dimKeys[k]] = ParseD(f[3 + k]);
            list.Add(inst);
        }
        return list;
    }

    private static double ParseD(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0.0;
}
