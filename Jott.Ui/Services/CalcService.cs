using NCalc;
using System;
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

        // For follow-up calculations (e.g. "2+3= 5+3="), take only the expression
        // after the last '=' in the LHS — the rest is already-evaluated history.
        // Without this, NCalc sees "2+3= 5+3" and treats '=' as equality (returns 0).
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
            // NCalcSync 5.x treats ^ as bitwise XOR; convert to Pow() for exponentiation.
            string processedLhs = ConvertCaretToPow(expr);
            var expression = new Expression(processedLhs);
            object value = expression.Evaluate();

            if (value == null)
            {
                return false;
            }

            double d = Convert.ToDouble(value);

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

    /// <summary>
    /// Rewrites every top-level <c>^</c> as <c>Pow(base, exp)</c> so that
    /// NCalcSync 5.x (which treats <c>^</c> as bitwise XOR) evaluates it as
    /// exponentiation. Only the immediately adjacent primary expression
    /// (number or parenthesised group) is used as each operand, so surrounding
    /// additive/multiplicative terms are left in place.
    /// </summary>
    private static string ConvertCaretToPow(string expr)
    {
        int depth = 0;

        for (int i = 0; i < expr.Length; i++)
        {
            char c = expr[i];
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
            }
            else if (c == '^' && depth == 0)
            {
                // Left operand: the primary immediately before ^
                int leftEnd = i - 1;
                while (leftEnd >= 0 && expr[leftEnd] == ' ')
                {
                    leftEnd--;
                }

                int leftStart;
                if (leftEnd >= 0 && expr[leftEnd] == ')')
                {
                    int d = 0;
                    int j = leftEnd;
                    while (j >= 0)
                    {
                        if (expr[j] == ')')
                        {
                            d++;
                        }
                        else if (expr[j] == '(')
                        {
                            d--; if (d == 0)
                            {
                                break;
                            }
                        }
                        j--;
                    }
                    leftStart = j;
                }
                else
                {
                    int j = leftEnd;
                    while (j >= 0 && (char.IsDigit(expr[j]) || expr[j] == '.'))
                    {
                        j--;
                    }

                    leftStart = j + 1;
                }

                // Right operand: the primary immediately after ^
                int rightStart = i + 1;
                while (rightStart < expr.Length && expr[rightStart] == ' ')
                {
                    rightStart++;
                }

                int rightEnd;
                if (rightStart < expr.Length && expr[rightStart] == '(')
                {
                    int d = 0;
                    int j = rightStart;
                    while (j < expr.Length)
                    {
                        if (expr[j] == '(')
                        {
                            d++;
                        }
                        else if (expr[j] == ')')
                        {
                            d--; if (d == 0)
                            {
                                break;
                            }
                        }
                        j++;
                    }
                    rightEnd = j;
                }
                else
                {
                    int j = rightStart;
                    while (j < expr.Length && (char.IsDigit(expr[j]) || expr[j] == '.'))
                    {
                        j++;
                    }

                    rightEnd = j - 1;
                }

                string prefix = expr.Substring(0, leftStart);
                string leftOp = expr.Substring(leftStart, leftEnd - leftStart + 1).Trim();
                string rightOp = expr.Substring(rightStart, rightEnd - rightStart + 1).Trim();
                string suffix = expr.Substring(rightEnd + 1);

                // Recurse on right operand for right-associativity (e.g. 2^3^2 → Pow(2,Pow(3,2)))
                string processedRight = ConvertCaretToPow(rightOp);
                string rebuilt = $"{prefix}Pow({leftOp},{processedRight}){suffix}";
                return ConvertCaretToPow(rebuilt);
            }
        }

        return expr;
    }

    private static string FormatResult(double value)
    {
        if (value == Math.Floor(value) && Math.Abs(value) < 1e15)
        {
            return ((long)value).ToString();
        }

        return value.ToString("G6");
    }
}
