using System.Collections.Generic;

namespace Teleprompter.Core.Text;

/// <summary>
/// Expands digit strings into their spoken cardinal words so that a script
/// written as "25" matches a speaker who says "twenty five". Speech engines
/// almost always emit number words, so aligning both sides on words removes a
/// common source of missed matches.
/// </summary>
public static class NumberExpander
{
    private static readonly string[] Ones =
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight",
        "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen",
        "sixteen", "seventeen", "eighteen", "nineteen"
    };

    private static readonly string[] Tens =
    {
        "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"
    };

    private static readonly (long Value, string Name)[] Scales =
    {
        (1_000_000_000L, "billion"),
        (1_000_000L, "million"),
        (1_000L, "thousand")
    };

    public static bool IsAllDigits(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        foreach (char c in s)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the spoken words for a digit string, or the original string as a
    /// single element when it cannot be expanded (too long, not numeric).
    /// </summary>
    public static IReadOnlyList<string> ToWords(string digits)
    {
        if (!IsAllDigits(digits) || digits.Length > 15 || !long.TryParse(digits, out long n))
        {
            return new[] { digits };
        }

        var words = new List<string>();
        AppendNumber(n, words);
        return words.Count == 0 ? new List<string> { "zero" } : words;
    }

    private static void AppendNumber(long n, List<string> output)
    {
        if (n == 0)
        {
            output.Add("zero");
            return;
        }

        if (n < 0)
        {
            n = -n;
        }

        foreach ((long value, string name) in Scales)
        {
            if (n >= value)
            {
                AppendThreeDigits(n / value, output);
                output.Add(name);
                n %= value;
            }
        }

        if (n > 0)
        {
            AppendThreeDigits(n, output);
        }
    }

    private static void AppendThreeDigits(long n, List<string> output)
    {
        if (n >= 100)
        {
            output.Add(Ones[n / 100]);
            output.Add("hundred");
            n %= 100;
        }

        if (n >= 20)
        {
            output.Add(Tens[n / 10]);
            n %= 10;
            if (n > 0)
            {
                output.Add(Ones[n]);
            }
        }
        else if (n > 0)
        {
            output.Add(Ones[n]);
        }
    }
}
