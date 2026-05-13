using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Jott.Ui.Services;

/// <summary>
/// Evaluates inline mathematical expressions typed in buffer text areas.
/// </summary>
internal static class CalcService
{
    private static readonly Regex DigitPattern = new Regex(@"\d");
    private static readonly Regex OperatorPattern = new Regex(@"[+\-*/%^()]");

    private static readonly char[] AdjacentBlockChars = { '>', '<', '!', '=' };

    /// <summary>
    /// Attempts to evaluate the mathematical expression on the current line.
    /// </summary>
    /// <param name="lineUpToAndIncludingEquals">
    /// Text from the start of the current line through the '=' character just typed.
    /// </param>
    /// <param name="result">
    /// The formatted numeric result when evaluation succeeds; otherwise <c>null</c>.
    /// </param>
    /// <returns>
    /// <c>true</c> if the line matched the calculator trigger and evaluated successfully;
    /// <c>false</c> to let the '=' be inserted normally with no side effects.
    /// </returns>
    public static bool TryEvaluate(string lineUpToAndIncludingEquals, out string result)
    {
        result = null;

        if (string.IsNullOrEmpty(lineUpToAndIncludingEquals))
        {
            return false;
        }

        if (!lineUpToAndIncludingEquals.EndsWith("="))
        {
            return false;
        }

        bool hasSpaceBefore = lineUpToAndIncludingEquals.Length >= 2
            && lineUpToAndIncludingEquals[lineUpToAndIncludingEquals.Length - 2] == ' ';

        string lhs = hasSpaceBefore
            ? lineUpToAndIncludingEquals.Substring(0, lineUpToAndIncludingEquals.Length - 2)
            : lineUpToAndIncludingEquals.Substring(0, lineUpToAndIncludingEquals.Length - 1);

        if (lhs.Length > 0)
        {
            char adjacent = lhs[lhs.Length - 1];
            if (Array.IndexOf(AdjacentBlockChars, adjacent) >= 0)
            {
                return false;
            }
        }

        int lastPriorEquals = lhs.LastIndexOf('=');
        string expr = lastPriorEquals >= 0
            ? lhs.Substring(lastPriorEquals + 1).TrimStart()
            : lhs;

        if (!DigitPattern.IsMatch(expr) || !OperatorPattern.IsMatch(expr))
        {
            return false;
        }

        try
        {
            double d = Evaluate(expr);

            if (double.IsInfinity(d) || double.IsNaN(d))
            {
                return false;
            }

            result = FormatResult(d);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static double Evaluate(string expr)
    {
        int pos = 0;
        double result = ParseAdditive(expr, ref pos);
        SkipWhitespace(expr, ref pos);
        if (pos < expr.Length)
        {
            throw new FormatException("Unexpected character");
        }

        return result;
    }

    private static double ParseAdditive(string expr, ref int pos)
    {
        double left = ParseMultiplicative(expr, ref pos);
        while (true)
        {
            SkipWhitespace(expr, ref pos);
            if (pos >= expr.Length)
            {
                break;
            }

            char op = expr[pos];
            if (op != '+' && op != '-')
            {
                break;
            }

            pos++;
            double right = ParseMultiplicative(expr, ref pos);
            left = op == '+' ? left + right : left - right;
        }

        return left;
    }

    private static double ParseMultiplicative(string expr, ref int pos)
    {
        double left = ParseUnary(expr, ref pos);
        while (true)
        {
            SkipWhitespace(expr, ref pos);
            if (pos >= expr.Length)
            {
                break;
            }

            char op = expr[pos];
            if (op != '*' && op != '/' && op != '%')
            {
                break;
            }

            pos++;
            double right = ParseUnary(expr, ref pos);
            if (op == '*')
            {
                left *= right;
            }
            else if (op == '/')
            {
                left /= right;
            }
            else
            {
                left %= right;
            }
        }

        return left;
    }

    private static double ParseUnary(string expr, ref int pos)
    {
        SkipWhitespace(expr, ref pos);
        if (pos >= expr.Length)
        {
            throw new FormatException("Expected value");
        }

        if (expr[pos] == '-')
        {
            pos++;
            return -ParseUnary(expr, ref pos);
        }

        if (expr[pos] == '+')
        {
            pos++;
            return ParseUnary(expr, ref pos);
        }

        return ParseExponential(expr, ref pos);
    }

    private static double ParseExponential(string expr, ref int pos)
    {
        double left = ParsePrimary(expr, ref pos);
        SkipWhitespace(expr, ref pos);
        if (pos < expr.Length && expr[pos] == '^')
        {
            pos++;
            double right = ParseUnary(expr, ref pos);
            return Math.Pow(left, right);
        }

        return left;
    }

    private static double ParsePrimary(string expr, ref int pos)
    {
        SkipWhitespace(expr, ref pos);
        if (pos >= expr.Length)
        {
            throw new FormatException("Expected value");
        }

        if (expr[pos] == '(')
        {
            pos++;
            double value = ParseAdditive(expr, ref pos);
            SkipWhitespace(expr, ref pos);
            if (pos >= expr.Length || expr[pos] != ')')
            {
                throw new FormatException("Expected ')'");
            }

            pos++;
            return value;
        }

        int start = pos;
        while (pos < expr.Length && (char.IsDigit(expr[pos]) || expr[pos] == '.'))
        {
            pos++;
        }

        if (pos == start)
        {
            throw new FormatException("Expected number");
        }

        string numStr = expr.Substring(start, pos - start);
        if (!double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double num))
        {
            throw new FormatException("Invalid number");
        }

        return num;
    }

    private static void SkipWhitespace(string expr, ref int pos)
    {
        while (pos < expr.Length && expr[pos] == ' ')
        {
            pos++;
        }
    }

    private static string FormatResult(double value)
    {
        if (value == Math.Floor(value) && Math.Abs(value) < 1e15)
        {
            return ((long)value).ToString();
        }

        return value.ToString("G3");
    }
}
