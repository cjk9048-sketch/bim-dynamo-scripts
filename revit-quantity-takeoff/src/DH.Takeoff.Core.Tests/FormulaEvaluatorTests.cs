using DH.Takeoff.Core;
using Xunit;

namespace DH.Takeoff.Core.Tests;

public class FormulaEvaluatorTests
{
    [Theory]
    [InlineData("0.4*0.5*3.8", 0.76)]            // box 콘크리트
    [InlineData("(0.4*2+0.5)*3.0", 3.9)]         // 거푸집 형
    [InlineData("2^2", 4)]                        // 거듭제곱
    [InlineData("0.3^2*4.95", 0.4455)]            // 기둥 C1 콘크리트(단위)
    [InlineData("(0.5^2+0.5^2)*1/2", 0.25)]       // *1/2 계열
    [InlineData("-0.5+1", 0.5)]                   // 단항 -
    [InlineData("3.95×0.4×0.5", 0.79)]            // × 정규화
    [InlineData("(27.6*25.4+2.7*22.6+0.3*10.2)*0.1", 76.512)] // L1 복합사각(인스턴스)
    [InlineData("-2^2", -4)]        // 단항- vs 거듭제곱 우선순위: -(2^2)
    [InlineData("2^-2", 0.25)]      // 지수의 단항-
    [InlineData("2^3^2", 512)]      // ^ 우결합: 2^(3^2)=2^9
    public void Evaluates(string expr, double expected)
        => Assert.Equal(expected, FormulaEvaluator.Evaluate(expr), 6);

    [Fact]
    public void SqrtSupported()
        => Assert.Equal(System.Math.Sqrt(0.5), FormulaEvaluator.Evaluate("SQRT(0.5^2+0.5^2)"), 9);

    [Fact]
    public void PiSupported()
        => Assert.Equal(System.Math.PI * 4, FormulaEvaluator.Evaluate("PI()*4"), 9);

    [Fact]
    public void EmptyIsZero() => Assert.Equal(0.0, FormulaEvaluator.Evaluate(""));

    [Theory]
    [InlineData("(1+2")]    // 괄호 불균형
    [InlineData("1+*2")]    // 연산자 오류
    [InlineData("foo(2)")]  // 알 수 없는 함수
    public void RejectsMalformed(string expr)
        => Assert.ThrowsAny<System.FormatException>(() => FormulaEvaluator.Evaluate(expr));
}
