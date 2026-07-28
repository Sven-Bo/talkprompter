using System;
using System.Collections.Generic;

namespace Teleprompter.Core.Matching;

/// <summary>
/// Word-level Levenshtein (edit) distance. Each word is treated as an atomic
/// symbol, so the "distance" is the number of word insertions, deletions and
/// substitutions needed to turn one sequence into another.
/// </summary>
public static class Levenshtein
{
    /// <summary>
    /// Computes the edit distance between two word sequences using two rolling
    /// rows (O(n) memory, O(n·m) time).
    /// </summary>
    public static int Distance(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0)
        {
            return b.Count;
        }

        if (b.Count == 0)
        {
            return a.Count;
        }

        int[] previous = new int[b.Count + 1];
        int[] current = new int[b.Count + 1];

        for (int j = 0; j <= b.Count; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Count; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Count; j++)
            {
                int cost = string.Equals(a[i - 1], b[j - 1], StringComparison.Ordinal) ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Count];
    }

    /// <summary>
    /// Normalized similarity in [0, 1] where 1 means identical. Defined as
    /// 1 − distance / max(lengths).
    /// </summary>
    public static double Similarity(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        int max = Math.Max(a.Count, b.Count);
        if (max == 0)
        {
            return 1.0;
        }

        return 1.0 - (double)Distance(a, b) / max;
    }
}
