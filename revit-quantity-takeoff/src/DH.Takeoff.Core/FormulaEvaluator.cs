using System.Globalization;

namespace DH.Takeoff.Core;

/// <summary>
/// 산출식(VBA 호환) 수치 평가기 — 재귀하강 파서.
/// 지원: + - * / ( ) ^(거듭제곱) 단항 ± SQRT(...) PI().  '×' 는 '*' 로 정규화.
/// 우선순위(Python/Excel 동치): () > 함수 > ^(우결합) > 단항± > * / > + -.
/// 단, 거듭제곱은 단항보다 강하게 결합: -2^2 = -(2^2) = -4, 2^-2 = 2^(-2), 2^3^2 = 2^(3^2).
/// 토큰([L1] 등)은 호출 전에 숫자로 치환되어 있어야 한다(<see cref="TakeoffEngine"/> 담당).
/// </summary>
public sealed class FormulaEvaluator
{
    private readonly string _s;
    private int _i;

    private FormulaEvaluator(string s) => _s = s;

    public static double Evaluate(string expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) return 0.0;
        var ev = new FormulaEvaluator(expr.Replace('×', '*'));
        double v = ev.ParseExpr();
        if (ev.Peek() != '\0')
            throw new FormatException($"수식 파싱 실패(잔여 '{ev._s[ev._i..]}'): {expr}");
        return v;
    }

    // expr = term (('+'|'-') term)*
    private double ParseExpr()
    {
        double v = ParseTerm();
        while (true)
        {
            char c = Peek();
            if (c == '+') { _i++; v += ParseTerm(); }
            else if (c == '-') { _i++; v -= ParseTerm(); }
            else return v;
        }
    }

    // term = factor (('*'|'/') factor)*
    private double ParseTerm()
    {
        double v = ParseFactor();
        while (true)
        {
            char c = Peek();
            if (c == '*') { _i++; v *= ParseFactor(); }
            else if (c == '/') { _i++; v /= ParseFactor(); }
            else return v;
        }
    }

    // factor = ('+'|'-') factor | power      (단항: 거듭제곱보다 약하게 결합)
    private double ParseFactor()
    {
        char c = Peek();
        if (c == '-') { _i++; return -ParseFactor(); }
        if (c == '+') { _i++; return ParseFactor(); }
        return ParsePower();
    }

    // power = primary ('^' factor)?          (base는 단항 불포함, 지수는 factor → 우결합·지수 단항 허용)
    private double ParsePower()
    {
        double b = ParsePrimary();
        if (Peek() == '^') { _i++; return Math.Pow(b, ParseFactor()); }
        return b;
    }

    // primary = '(' expr ')' | func | number
    private double ParsePrimary()
    {
        char c = Peek();
        if (c == '(') { _i++; double v = ParseExpr(); Expect(')'); return v; }
        if (char.IsLetter(c)) return ParseFunc();
        return ParseNumber();
    }

    private double ParseFunc()
    {
        int start = _i;
        while (_i < _s.Length && char.IsLetter(_s[_i])) _i++;
        string name = _s[start.._i].ToUpperInvariant();
        switch (name)
        {
            case "PI": Expect('('); Expect(')'); return Math.PI;
            case "SQRT": Expect('('); double v = ParseExpr(); Expect(')'); return Math.Sqrt(v);
            default: throw new FormatException($"알 수 없는 함수: {name}");
        }
    }

    private double ParseNumber()
    {
        SkipWs();
        int start = _i;
        while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
        if (_i == start)
            throw new FormatException($"숫자 기대 위치 오류: '{_s[start..]}'");
        return double.Parse(_s[start.._i], CultureInfo.InvariantCulture);
    }

    private void SkipWs() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }

    private char Peek() { SkipWs(); return _i < _s.Length ? _s[_i] : '\0'; }

    private void Expect(char ch)
    {
        if (Peek() != ch) throw new FormatException($"'{ch}' 기대 (위치 {_i})");
        _i++;
    }
}
