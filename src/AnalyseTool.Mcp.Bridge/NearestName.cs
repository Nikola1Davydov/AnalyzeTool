namespace AnalyseTool.Mcp.Bridge
{
    /// <summary>
    /// "Did you mean" for a near miss, shared by the two places a caller can name something that does not
    /// exist: a parameter (<see cref="PayloadValidator"/>) and a command (the bridge's invoke path).
    ///
    /// Only a NEAR miss. A suggestion that is merely the least-bad of a list of unrelated names sends the
    /// caller somewhere wrong with confidence, which costs more than saying nothing — so the edit distance
    /// has to be small relative to the word it is suggesting.
    /// </summary>
    internal static class NearestName
    {
        /// <summary>The closest candidate, or null when nothing is close enough to be worth naming.</summary>
        public static string? Closest(string typed, IEnumerable<string> candidates)
        {
            string? best = null;
            int bestDistance = int.MaxValue;
            foreach (string candidate in candidates)
            {
                int distance = Distance(typed.ToLowerInvariant(), candidate.ToLowerInvariant());
                if (distance < bestDistance) { bestDistance = distance; best = candidate; }
            }
            if (best is null) return null;

            int tolerance = Math.Min(3, Math.Max(1, best.Length / 3));
            return bestDistance <= tolerance ? best : null;
        }

        /// <summary>Levenshtein distance, two rows at a time.</summary>
        private static int Distance(string a, string b)
        {
            int[] previous = new int[b.Length + 1];
            int[] current = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) previous[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int substitute = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitute);
                }
                (previous, current) = (current, previous);
            }
            return previous[b.Length];
        }
    }
}
