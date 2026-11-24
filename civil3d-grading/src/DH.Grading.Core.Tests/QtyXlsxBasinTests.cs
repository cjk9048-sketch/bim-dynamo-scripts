using ClosedXML.Excel;
using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>★[검토 1124 #7] 측구가 모두 집수정보다 짧아 측구 줄이 없을 때 — 집수정 줄이 A1 제목 · A3 알림을 덮지 않음.</summary>
public class QtyXlsxBasinTests
{
    [Fact]
    public void BasinRowsDoNotOverwriteTitleWhenNoDitches()
    {
        var m = new QtyReportModel { Facility = "시험", BasinCount = 2, BasinVol = 1.234 };
        m.Basins.Add(new QtyBasin { Label = "안 0.60×0.60 × 깊이 0.9 m", Kinds = "합류 2", Count = 2, Volume = 1.234 });
        string path = Path.Combine(Path.GetTempPath(), $"qtyx_{Guid.NewGuid():N}.xlsx");
        try
        {
            QtyReportXlsx.Write(m, path);
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet(QtyReportXlsx.DitchSheet);
            Assert.Equal("수로관산근", ws.Cell("A1").GetString());
            Assert.StartsWith("U형 측구", ws.Cell("A3").GetString());
            int row = Enumerable.Range(1, 20).First(r => ws.Cell(r, 1).GetString() == "집수정");
            Assert.True(row > 3);
            Assert.Equal(2, (int)ws.Cell("AV" + row).Value.GetNumber());
            Assert.Equal("  규격", ws.Cell(row + 1, 1).GetString());
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
