namespace DH.Grading.Core.Forest;

/// <summary>★[v115 · JACK 1105 임상도] 임상도 코드 → 타일 바이트 · 나무 모양 · 키 · 수관 폭 · 간격(계획 2판 2 · 4절).
/// <para>코드 칸만 쓴다(_NM 칸은 범례 이름표로만 — index.json codes). 코드 값은 1105 조사(산림청 임상도 1:5,000 · 18 SHP)에서 잰 것.</para></summary>
public static class ForestCodes
{
    // ── DBF 글 → 타일 바이트 ──

    /// <summary>FRTP_CD «0»~«4» → 0~4 · 그 밖(빈칸 · 모름) → 255.</summary>
    public static byte Frtp(string s) => s.Length == 1 && s[0] >= '0' && s[0] <= '4' ? (byte)(s[0] - '0') : (byte)255;

    /// <summary>KOFTR_GROU «11» … → 11 · 빈칸 · 모름 → 0.</summary>
    public static byte Koftr(string s) => int.TryParse(s, out int v) && v >= 0 && v <= 254 ? (byte)v : (byte)0;

    /// <summary>DNST_CD A 소 · B 중 · C 밀 → 1 · 2 · 3 · 빈칸 0.</summary>
    public static byte Dnst(string s) => s switch { "A" => 1, "B" => 2, "C" => 3, _ => 0 };

    /// <summary>HEIGHT «NN» → NN(0~40) · 빈칸 · 모름 → 255.</summary>
    public static byte Height(string s) => int.TryParse(s, out int v) && v >= 0 && v <= 40 ? (byte)v : (byte)255;

    /// <summary>DMCLS_CD 0~3 · 빈칸 255.</summary>
    public static byte Dm(string s) => s.Length == 1 && s[0] >= '0' && s[0] <= '9' ? (byte)(s[0] - '0') : (byte)255;

    /// <summary>AGCLS_CD 1~9 · FROR_CD 1 인공 · 2 천연 — 빈칸 0.</summary>
    public static byte Small(string s) => s.Length == 1 && s[0] >= '0' && s[0] <= '9' ? (byte)(s[0] - '0') : (byte)0;

    // ── 나무 모양(4절 표) ──

    public const int Kinds = 9;
    public static readonly string[] KindNames = { "소나무형", "원뿔형", "짙은원뿔형", "참나무형", "달걀형", "길쭉형", "넓은형", "상록활엽형", "대나무" };
    /// <summary>모양이 침엽 꼴인가(수관 폭 식 · 먼 대리 모양 원뿔).</summary>
    public static bool Conifer(int kind) => kind <= 2;

    /// <summary>수종 코드 → 모양 · 혼효(77)는 -1(그루마다 0 : 3 = 5 : 5) · 모르는 코드 = -2(FRTP 기본 모양).</summary>
    public static int KindOf(byte koftr)
    {
        switch (koftr)
        {
            case 11: case 14: case 15: case 10: return 0;
            case 13: case 16: case 19: return 1;
            case 12: case 17: case 18: case 20: return 2;
            case 31: case 32: case 33: case 34: case 38: case 41: return 3;
            case 30: case 35: case 36: case 40: case 42: case 43: case 48: case 21: return 4;
            case 37: case 44: case 45: case 49: return 5;
            case 39: case 46: case 47: return 6;
            case 78: return 8;
            case 77: return -1;
        }
        if (koftr >= 60 && koftr <= 68) return 7;
        return -2;
    }

    /// <summary>FRTP 기본 모양 — 침엽 0 · 활엽 4 · 혼효 -1(섞음) · 죽림 8.</summary>
    public static int DefaultKind(byte frtp) => frtp switch { 1 => 0, 2 => 4, 3 => -1, 4 => 8, _ => 4 };

    // ── 키 · 수관 · 간격 ──

    /// <summary>키 구간 [lo, hi) m — HEIGHT 00 = null(1 m 미만 · 안 심음) · NN = [NN−1, NN+1) · 40 = [39, 42) ·
    /// 빈칸 = 죽림 8~14 · 그 밖 DMCLS 0 → 2~6 · 1 → 6~12 · 2 → 12~18 · 3 → 18~24 · 모름 → 6~12.</summary>
    public static (double Lo, double Hi)? HeightRange(byte hgt, byte dm, byte frtp)
    {
        if (hgt == 0) return null;
        if (hgt != 255)
        {
            if (hgt >= 40) return (39, 42);
            return (Math.Max(0.5, hgt - 1), hgt + 1);
        }
        if (frtp == 4) return (8, 14);
        return dm switch { 0 => (2, 6), 1 => (6, 12), 2 => (12, 18), 3 => (18, 24), _ => (6, 12) };
    }

    public const double MaxCrown = 25.0;

    /// <summary>수관 폭 W(m) — 침엽 0.28H + 0.8 · 활엽 0.45H + 1.0 · 대나무 1.5 · DMCLS 1 ×0.9 · 3 ×1.15 · 그루마다 ×<paramref name="jitter"/> · 최대 25 m.</summary>
    public static double Crown(int kind, double h, byte dm, double jitter = 1.0)
    {
        double w = kind == 8 ? 1.5 : Conifer(kind) ? 0.28 * h + 0.8 : 0.45 * h + 1.0;
        if (dm == 1) w *= 0.9; else if (dm == 3) w *= 1.15;
        return Math.Min(MaxCrown, w * jitter);
    }

    /// <summary>덮임 — 소 0.45 · 중 0.65 · 밀 0.85 · 빈칸 0.85.</summary>
    public static double Cover(byte dnst) => dnst switch { 1 => 0.45, 2 => 0.65, _ => 0.85 };

    /// <summary>간격 s = clamp(W평균 / √덮임, 2.5, 14) m · 0.5 m 단위로 반올림한 «격자 번호»(s = 번호 × 0.5).</summary>
    public static int GridNo(double wAvg, byte dnst, double density = 1.0)
    {
        double s = Math.Clamp(wAvg / Math.Sqrt(Cover(dnst)) / Math.Sqrt(Math.Max(0.05, density)), 2.5, 14);
        return Math.Max(5, (int)Math.Round(s / 0.5));
    }

    /// <summary>폴리곤 평균 수관 폭 — 키 구간 가운데 · 모양(혼효는 침엽 · 활엽 평균).</summary>
    public static double MeanCrown(int kind, (double Lo, double Hi) hr, byte dm)
    {
        double h = (hr.Lo + hr.Hi) / 2;
        if (kind == -1) return (Crown(0, h, dm) + Crown(3, h, dm)) / 2;
        return Crown(kind, h, dm);
    }
}
