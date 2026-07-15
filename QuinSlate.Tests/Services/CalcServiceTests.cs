using QuinSlate.Ui.Services;

namespace QuinSlate.Tests.Services;

public sealed class CalcServiceTests
{
    [Fact]
    public void NullInput_ReturnsFalse()
    {
        Assert.False(CalcService.TryEvaluate(null, out _));
    }

    [Fact]
    public void EmptyInput_ReturnsFalse()
    {
        Assert.False(CalcService.TryEvaluate(string.Empty, out _));
    }

    [Fact]
    public void NoSpaceBeforeEquals_EvaluatesCorrectly()
    {
        Assert.True(CalcService.TryEvaluate("1 + 2=", out string result));
        Assert.Equal("3", result);
    }

    [Theory]
    [InlineData("a >= b =")]
    [InlineData("x == y =")]
    [InlineData("x != y =")]
    [InlineData("x <= y =")]
    [InlineData("a >= b=")]
    [InlineData("x == y=")]
    [InlineData("x != y=")]
    [InlineData("x <= y=")]
    public void AdjacentOperatorGuard_ReturnsFalse(string input)
    {
        Assert.False(CalcService.TryEvaluate(input, out _));
    }

    [Theory]
    [InlineData("status =")]
    [InlineData("name =")]
    [InlineData("foo =")]
    public void NoDigitOrOperator_ReturnsFalse(string input)
    {
        Assert.False(CalcService.TryEvaluate(input, out _));
    }

    [Fact]
    public void SimpleAddition_ReturnsResult()
    {
        Assert.True(CalcService.TryEvaluate("450 + 38.50 =", out string result));
        Assert.Equal("488.5", result);
    }

    [Fact]
    public void Exponentiation_ReturnsWholeNumber()
    {
        Assert.True(CalcService.TryEvaluate("2^10 =", out string result));
        Assert.Equal("1024", result);
    }

    [Fact]
    public void Exponentiation_WithSurroundingTerms_NoSpace()
    {
        Assert.True(CalcService.TryEvaluate("3+3^2-2=", out string result));
        Assert.Equal("10", result);
    }

    [Fact]
    public void Exponentiation_WithSurroundingTerms_WithSpaces()
    {
        Assert.True(CalcService.TryEvaluate("3+3^2 - 2 + 10=", out string result));
        Assert.Equal("20", result);
    }

    [Fact]
    public void WholeNumberResult_NoDecimalPoint()
    {
        Assert.True(CalcService.TryEvaluate("1024 * 1 =", out string result));
        Assert.Equal("1024", result);
    }

    [Fact]
    public void FractionalResult_ThreeSignificantDigits()
    {
        Assert.True(CalcService.TryEvaluate("1 / 3 =", out string result));
        Assert.Equal("0.333", result);
    }

    [Fact]
    public void TrailingZerosStripped()
    {
        Assert.True(CalcService.TryEvaluate("1.1 * 2 =", out string result));
        Assert.Equal("2.2", result);
    }

    [Fact]
    public void UnaryMinus_EvaluatesCorrectly()
    {
        Assert.True(CalcService.TryEvaluate("-5 * 3 =", out string result));
        Assert.Equal("-15", result);
    }

    [Fact]
    public void ParenthesesAndMultiplication_ReturnsResult()
    {
        Assert.True(CalcService.TryEvaluate("(450 + 38.50) * 1.21 =", out string result));
        Assert.NotNull(result);
    }

    [Fact]
    public void DivisionByZero_ReturnsFalse()
    {
        Assert.False(CalcService.TryEvaluate("1 / 0 =", out _));
    }

    [Fact]
    public void Modulo_ReturnsResult()
    {
        Assert.True(CalcService.TryEvaluate("10 % 3 =", out string result));
        Assert.Equal("1", result);
    }

    [Fact]
    public void WhitespaceInsideExpression_EvaluatesCorrectly()
    {
        Assert.True(CalcService.TryEvaluate("4 * ( 2 + 1 ) =", out string result));
        Assert.Equal("12", result);
    }

    [Fact]
    public void FollowUpCalculation_SpaceSeparated_EvaluatesRightSegment()
    {
        // After "2+3= 5", the user types "+3=" → full line is "2+3= 5+3="
        Assert.True(CalcService.TryEvaluate("2+3= 5+3=", out string result));
        Assert.Equal("8", result);
    }

    [Fact]
    public void FollowUpCalculation_WithExponent_EvaluatesRightSegment()
    {
        // After "4+4^4 = 260", the user types "+1=" → full line is "4+4^4 = 260+1="
        Assert.True(CalcService.TryEvaluate("4+4^4 = 260+1=", out string result));
        Assert.Equal("261", result);
    }

    [Fact]
    public void FollowUpCalculation_Chained_EvaluatesLastSegment()
    {
        // Three chained evaluations on one line
        Assert.True(CalcService.TryEvaluate("2+3= 5+3= 8*2=", out string result));
        Assert.Equal("16", result);
    }

    [Fact]
    public void InputNotEndingWithEquals_ReturnsFalse()
    {
        Assert.False(CalcService.TryEvaluate("1 + 2", out _));
    }

    [Fact]
    public void UnaryPlus_EvaluatesCorrectly()
    {
        Assert.True(CalcService.TryEvaluate("+5 + 3 =", out string result));
        Assert.Equal("8", result);
    }

    [Fact]
    public void UnclosedParenthesis_ReturnsFalse()
    {
        Assert.False(CalcService.TryEvaluate("(1 + 2 =", out _));
    }

    [Fact]
    public void TrailingGarbage_ReturnsFalse()
    {
        Assert.False(CalcService.TryEvaluate("1 + 2 abc =", out _));
    }

    [Fact]
    public void LargeWholeNumber_UsesScientificNotation()
    {
        // 2^50 = 1125899906842624, which is >= 1e15, so FormatResult uses G3
        Assert.True(CalcService.TryEvaluate("2^50 =", out string result));
        Assert.Equal("1.13E+15", result);
    }

    [Fact]
    public void FollowUpCalculation_ScientificNotationResult_Evaluates()
    {
        // After "(3+3)^67= 1.37E+52", the user types " - 1=" → the previous result
        // (formatted in scientific notation) must parse as an operand.
        Assert.True(CalcService.TryEvaluate("(3+3)^67=1.37E+52 - 1=", out string result));
        Assert.Equal("1.37E+52", result);
    }

    [Theory]
    [InlineData("2e3 + 1=", "2001")]
    [InlineData("1E3+1=", "1001")]
    [InlineData("1.5e-2 * 2=", "0.03")]
    [InlineData("1.37E+52 * 1=", "1.37E+52")]
    public void ScientificNotationOperand_EvaluatesCorrectly(string input, string expected)
    {
        Assert.True(CalcService.TryEvaluate(input, out string result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("2E + 1=")]
    [InlineData("2e- + 1=")]
    public void BareExponentSuffix_ReturnsFalse(string input)
    {
        Assert.False(CalcService.TryEvaluate(input, out _));
    }

    [Theory]
    [InlineData("((2+3)*(4-1))^2 =", "225")]
    [InlineData("(2+3*4)/(2^2-2)=", "7")]
    [InlineData("((2+3)^2 - 5) / 2^2 =", "5")]
    [InlineData("(((1+2)*(3+4))^2 % 100)=", "41")]
    [InlineData("((((((((((1+1))))))))))*2 =", "4")]
    [InlineData("( 2 + 3 ) ^ 2 =", "25")]
    public void NestedParentheses_EvaluateCorrectly(string input, string expected)
    {
        Assert.True(CalcService.TryEvaluate(input, out string result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("2^2^3 =", "256")] // right-associative: 2^(2^3), not (2^2)^3 = 64
    [InlineData("2^-2 =", "0.25")]
    [InlineData("-3^2 + (-3)^2 =", "0")] // unary minus binds looser than ^: -(3^2) + 9
    [InlineData("2^(1/2) =", "1.414")]
    [InlineData("0^0 =", "1")]
    [InlineData("2e2^2 =", "40000")]
    public void Exponentiation_TrickyCases_EvaluateCorrectly(string input, string expected)
    {
        Assert.True(CalcService.TryEvaluate(input, out string result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("10 - 2 - 3 =", "5")]
    [InlineData("100 / 10 / 2 =", "5")]
    [InlineData("10 % 4 % 3 =", "2")]
    [InlineData("7 % 2.5 =", "2")]
    [InlineData("5.5 % 2 =", "1.5")]
    public void LeftAssociativeChains_EvaluateCorrectly(string input, string expected)
    {
        Assert.True(CalcService.TryEvaluate(input, out string result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("-(3+4)*-2 =", "14")]
    [InlineData("2---3 =", "-1")] // 2 - (-(-3))
    [InlineData("(2-5)*(4+1) =", "-15")]
    [InlineData("(1e2 + 2E1) / 1e2 =", "1.2")]
    public void UnarySignsAndMixedOperands_EvaluateCorrectly(string input, string expected)
    {
        Assert.True(CalcService.TryEvaluate(input, out string result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("(1+2))*3 =")] // unbalanced closing parenthesis
    [InlineData("2 ** 3 =")] // doubled operator
    [InlineData("() + 1 =")] // empty group
    [InlineData("1 + (2 * (3 - )) =")] // dangling operator inside nesting
    [InlineData("2(3+4) =")] // implicit multiplication is not supported
    [InlineData("5 % 0 =")] // NaN
    [InlineData("2^1024 =")] // overflows double to infinity
    public void MalformedOrNonFinite_ReturnsFalse(string input)
    {
        Assert.False(CalcService.TryEvaluate(input, out _));
    }

    [Fact]
    public void DeeplyNestedParentheses_ReturnsFalse_DoesNotCrash()
    {
        // A pathological run of opening parentheses must fail gracefully rather than
        // recurse until the stack overflows (StackOverflowException is uncatchable).
        var expr = new string('(', 5000) + "1" + new string(')', 5000) + "=";
        Assert.False(CalcService.TryEvaluate(expr, out _));
    }
}
