namespace TheSequelCommittee;

public static class Analyzer
{
    // ratingSource supports: imdb | tmdb | blended | auto | rt | rt_audience | blended_rt_* | rt_only | rt_audience_only
    public static List<FranchiseRunRow> BuildRuns(
        List<MovieJoined> joined,
        string ratingSource, int minImdbVotes, double blendAlpha,
        int fallAdj, int fallCum, int fallK, int fallAvg,
        int goodThreshold = 70, int minStreak = 1,
        int firstFilmGrace = 8, bool preferOrigin = true)
    {
        var runRows = new List<FranchiseRunRow>();

        foreach (var g in joined.GroupBy(x => (x.CollectionId, x.CollectionName)))
        {
            var seq = g.OrderBy(x => x.ReleaseDate ?? DateTime.MaxValue).ThenBy(x => x.Title).ToList();

            var scores = seq.Select(x => SelectScore100(x, ratingSource, minImdbVotes, blendAlpha)).ToList();
            for (int i = 0; i < scores.Count; i++)
                if (double.IsNaN(scores[i] ?? double.NaN)) scores[i] = null;

            var legacy = AnalyzeRun(scores, fallAdj, fallCum, fallK, fallAvg);

            // --- Good flags with "first film grace" ---
            var goodFlags = new bool[scores.Count];
            for (int i = 0; i < scores.Count; i++)
            {
                var thr = goodThreshold - (i == 0 ? firstFilmGrace : 0);
                goodFlags[i] = scores[i].HasValue && scores[i]!.Value >= thr;
            }

            // --- Pick best streak of "good" films (favor start at index 0) ---
            int bestStart = -1, bestEnd = -1;
            double bestAvg = double.NegativeInfinity;
            int curStart = -1;

            void ConsiderCurrent(int start, int end)
            {
                if (start < 0 || end < start) return;
                int len = end - start + 1;
                if (len < minStreak) return;

                // average over available scores in the window
                double sum = 0; int cnt = 0;
                for (int i = start; i <= end; i++)
                    if (scores[i].HasValue) { sum += scores[i]!.Value; cnt++; }
                double avg = cnt > 0 ? sum / cnt : double.NegativeInfinity;

                // Ranking tuple:
                // 1) longer length
                // 2) if preferOrigin and start==0, give a small boost to avg (+1.0 point)
                // 3) higher avg
                // 4) earlier start (final tie-break)
                double avgWithBias = avg + ((preferOrigin && start == 0) ? 1.0 : 0.0);

                bool take = false;
                int curBestLen = (bestStart >= 0 && bestEnd >= bestStart) ? (bestEnd - bestStart + 1) : 0;
                double curBestAvgWithBias = bestAvg + ((preferOrigin && bestStart == 0) ? 1.0 : 0.0);

                if (len > curBestLen) take = true;
                else if (len == curBestLen && avgWithBias > curBestAvgWithBias) take = true;
                else if (len == curBestLen && Math.Abs(avgWithBias - curBestAvgWithBias) < 1e-9 && (start < bestStart || bestStart < 0)) take = true;

                if (take) { bestStart = start; bestEnd = end; bestAvg = avg; }
            }

            for (int i = 0; i < goodFlags.Length; i++)
            {
                if (goodFlags[i]) { if (curStart < 0) curStart = i; }
                else { if (curStart >= 0) { ConsiderCurrent(curStart, i - 1); curStart = -1; } }
            }
            if (curStart >= 0) ConsiderCurrent(curStart, goodFlags.Length - 1);

            // Fall "cliff" value (optional)
            double? cliff = null;
            if (legacy.FallIndex is int fi && fi >= 0 && fi < scores.Count && legacy.PeakIndex >= 0 && legacy.PeakIndex < scores.Count)
            {
                var peakVal = scores[legacy.PeakIndex];
                var fallVal = scores[fi];
                if (peakVal.HasValue && fallVal.HasValue) cliff = peakVal.Value - fallVal.Value;
            }

            string? fallTitle = (legacy.FallIndex is int fii && fii >= 0 && fii < seq.Count) ? seq[fii].Title : null;
            string? peakTitle = (legacy.PeakIndex >= 0 && legacy.PeakIndex < seq.Count) ? seq[legacy.PeakIndex].Title : null;

            // Good indices CSV (used for coloring in HTML)
            string goodCsv = string.Join(";", Enumerable.Range(0, goodFlags.Length).Where(i => goodFlags[i]).Select(i => i.ToString()));

            runRows.Add(new FranchiseRunRow
            {
                CollectionId = g.Key.CollectionId,
                CollectionName = g.Key.CollectionName,
                FilmCount = seq.Count,

                GoodRunLength = legacy.GoodRunLength,
                PeakIndex = legacy.PeakIndex,
                PeakTitle = peakTitle,
                FallIndex = legacy.FallIndex,
                FallTitle = fallTitle,
                CliffDrop = cliff,

                AvgFirstN = AverageFirstN(scores, Math.Min(3, scores.Count)),
                AvgAll = AverageFirstN(scores, scores.Count),
                HasAnyMissingRatings = scores.Any(x => !x.HasValue),

                StreakStartIndex = (bestStart >= 0) ? bestStart : (int?)null,
                StreakEndIndex = (bestEnd >= 0) ? bestEnd : (int?)null,
                StreakLength = (bestStart >= 0 && bestEnd >= bestStart) ? (bestEnd - bestStart + 1) : 0,
                StreakAvg = (bestStart >= 0) ? bestAvg : (double?)null,
                GoodIndicesCsv = goodCsv,
                GoodThreshold = goodThreshold // base threshold (UI shows this)
            });
        }

        return runRows;
    }

    private static double? SelectScore100(MovieJoined x, string ratingSource, int minImdbVotes, double blendAlpha)
    {
        double? imdb100 = (x.ImdbVotes.HasValue && x.ImdbVotes.Value >= minImdbVotes) ? x.ImdbRating100 : null;
        double? tmdb100 = x.TmdbVoteAverage > 0 ? x.TmdbVoteAverage * 10.0 : (double?)null;
        double? rtCrit = x.RtCriticPct;
        double? rtAud = x.RtAudiencePct;

        return ratingSource switch
        {
            "rt_only" => rtCrit,
            "rt_audience_only" => rtAud,
            "rt" => rtCrit ?? tmdb100 ?? imdb100,
            "rt_audience" => rtAud ?? tmdb100 ?? imdb100,
            "blended_rt_tmdb" => Blend(rtCrit ?? tmdb100, tmdb100 ?? rtCrit, blendAlpha),
            "blended_rt_imdb" => Blend(rtCrit ?? imdb100, imdb100 ?? rtCrit, blendAlpha),
            "imdb" => imdb100,
            "tmdb" => tmdb100,
            "blended" => Blend(imdb100 ?? tmdb100, tmdb100 ?? imdb100, blendAlpha),
            _ => rtCrit ?? imdb100 ?? tmdb100
        };
    }

    private static double? Blend(double? a, double? b, double alpha)
    {
        if (a.HasValue && b.HasValue) return a.Value * alpha + b.Value * (1 - alpha);
        return a ?? b;
    }

    // Legacy fall-off (unchanged)
    public static RunAnalysis AnalyzeRun(IReadOnlyList<double?> ratings, int D_adj = 10, int D_cum = 18, int k = 2, int T_avg = 65)
    {
        if (ratings.Count == 0) return new(0, null, 0);

        int peakIdx = -1; double peakVal = double.MinValue;
        for (int i = 0; i < ratings.Count; i++)
        {
            var v = ratings[i];
            if (v.HasValue && v.Value > peakVal) { peakVal = v.Value; peakIdx = i; }
        }
        if (peakIdx < 0) return new(0, null, 0);

        int? fallIdx = null;

        for (int i = peakIdx + 1; i < ratings.Count; i++)
        {
            var cur = ratings[i];
            var prev = ratings[i - 1];

            bool adj = false, cum = false, roll = false;

            if (cur.HasValue && prev.HasValue) adj = (prev.Value - cur.Value) >= D_adj;
            if (cur.HasValue) cum = (peakVal - cur.Value) >= D_cum;

            if (i - k + 1 >= 0)
            {
                int cnt = 0; double sum = 0;
                for (int j = i - k + 1; j <= i; j++)
                    if (ratings[j].HasValue) { sum += ratings[j]!.Value; cnt++; }

                if (cnt == k) roll = (sum / k) < T_avg;
            }

            if (adj || cum || roll) { fallIdx = i; break; }
        }

        int goodRunLen = fallIdx is null ? ratings.Count : Math.Max(0, fallIdx.Value);
        return new(peakIdx, fallIdx, goodRunLen);
    }

    private static double? AverageFirstN(IReadOnlyList<double?> xs, int n)
    {
        if (n <= 0 || xs.Count == 0) return null;
        int take = Math.Min(n, xs.Count);
        double sum = 0; int cnt = 0;
        for (int i = 0; i < take; i++)
            if (xs[i].HasValue) { sum += xs[i]!.Value; cnt++; }
        return cnt == 0 ? (double?)null : sum / cnt;
    }
}
