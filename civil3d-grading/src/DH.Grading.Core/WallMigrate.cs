using System;
using System.Collections.Generic;
using System.Linq;

namespace DH.Grading.Core;

/// <summary>★★[v117.x · 옛 옹벽 옮기기 — DHWALLMIGRATE · 계획 2026-11-10_옹벽추가_안쪽소단 3-3 + 검토 M5] 옛 모양(기록 Shape 0 = v114~v116 선 바깥 계단) 부지 선 옹벽을
/// <b>같은 그은 선(Src)</b>으로 지금 부지 규칙(다단 — 단이 필요 없으면 저절로 수직 · 또는 수직만)으로 다시 짓는다. 저절로 옮기지 않는다(명령을 쳐야 옮김).
/// <para>옮긴 칸 = 새로 그은 옹벽(<c>WallLineFlow.Make</c> 수직 · 다단)과 같은 칸 · 옮기지 못한 칸은 손대지 않는다(바이트 같음).
/// 이웃한 옛 옹벽을 함께 옮길 때는 옹벽마다 목록을 저장한 뒤 다음 옹벽을 판정한다(검토 M5 — 앞서 옮긴 옹벽의 새 바꿀 자리와 견줌).</para></summary>
public static class WallMigrate
{
    /// <summary>옛 모양 부지 선 옹벽인가(종류 2 · 모양 0).</summary>
    public static bool IsOld(WallRec w) => w.Kind == 2 && w.Shape == 0;

    /// <summary>옮길 수 없는 까닭(빈 값 = 옮길 수 있음) — «대기»(합성에 안 든 옹벽 — 다음 [옹벽 추가]가 치운다) · 그은 선 원본 없음.</summary>
    public static string SkipWhy(WallRec w)
        => !IsOld(w) ? "옛 모양이 아닙니다"
         : w.State != 1 ? "«대기»(합성에 안 든 옹벽) — [옹벽 합성] 뒤에 옮기세요"
         : w.Src.Count < 2 ? "그은 선 원본이 없습니다"
         : "";

    /// <summary>도면을 열 때 한 번 알림(옮기지는 않음).</summary>
    public static string Notice(int n) => $"옛 모양 선 옹벽 {n}개 — [DHWALLMIGRATE]로 새 모양으로 옮길 수 있습니다";

    /// <summary>지은 결과로 칸을 새 모양으로 — <c>WallLineFlow.Make</c>가 새로 그은 수직 · 다단 옹벽에 적는 칸과 같다
    /// (모양 2 = 수직 · 3 = 다단(단높이 · 소단 · 첫 소단 = 쓴 값 · 판 11 구간 · 끝 비탈 · 자동 첫 소단 표지 = 손 값 없이 그은 것과 같음) · 단높이/소단 칸 0 · 천단 계단 없음 · 블록 9 · 선 바깥 최대 0).
    /// 옮김은 첫 소단 · 구간을 손으로 주지 않는다(자동 — <paramref name="tierZ1Hand"/> 거짓) · 이웃 소단 자리 겹침은 다단 짓기가 막는다(막을 다각형 = 앞서 옮긴 새 자리 포함).
    /// 번호 · State · Src · SrcHandle · 바꿈/물려받기 · LastNote · CompNonce는 그대로. 면 핸들 · 막음 · P0 · 시각은 부른 쪽(Civil)이 적는다.</summary>
    public static void Apply(WallRec w, WallByLine.Built b, IReadOnlyList<Point3> line, double slopeW, double faceRun, double grid, double reach, bool tierZ1Hand = false)
    {
        if (!b.Ok) throw new ArgumentException("못 지은 결과로는 옮기지 않는다");
        var (bench, foot) = WallByLine.SamplesOf(b);
        w.Kind = 2; w.RulerKind = 2;
        w.Up = b.Shape == 3 ? b.Dir > 0 : b.HasCut;
        w.Bench = -1; w.H = b.HMax; w.T = 0; w.T0 = 0; w.T1 = 0;
        w.HeadZ = b.Faces.Count > 0 ? b.Faces.Max(f => f.Top.Count > 0 ? f.Top.Max(q => q.Z) : f.Level) : 0;
        w.Seg = line.ToList(); w.Line = line.ToList();
        w.Poly = b.Poly; w.IsWall = b.IsWall;
        w.SlopeW = slopeW; w.FaceRun = faceRun; w.Grid = grid; w.Reach = reach;
        w.Hc = w.Tc = w.Hf = w.Tf = 0;
        w.FootMax = 0; w.FootAtX = b.FootAt.X; w.FootAtY = b.FootAt.Y;
        w.TopStep = false; w.BlockLen = 9;
        w.ZoneF = b.ZoneF; w.ZoneC = b.ZoneC; w.Faces = b.Faces;
        w.BenchSamples = bench; w.FootSamples = foot;
        w.Shape = b.Shape == 3 ? 3 : 2;
        if (w.Shape == 3)
        {
            w.TierH = b.TierH; w.TierB = b.TierB; w.TierZ1 = b.TierZ1;
            // ★[판 11 · WallLineFlow.Make와 같음] 구간 · 끝 비탈 · 한 구간 자동 첫 소단 표지(다시 짓기마다 지금 면으로 다시 셈)
            w.TierSecs = b.TierSecsUsed; w.TierEnd = b.TierEnd; w.TierZ1Auto = b.TierSecsUsed.Count < 2 && !tierZ1Hand;
        }
        else { w.TierH = w.TierB = w.TierZ1 = double.NaN; w.TierSecs = new(); w.TierEnd = false; w.TierZ1Auto = false; }
        // 지금 P0 위에 새로 지었다 — 옛 보류 · 순수옹벽 · 뚜껑은 버린다(합성이 다시 채운다 · Rebuild 성공과 같음)
        w.Hold = ""; w.PureHandle = ""; w.Cap = null;
    }
}
