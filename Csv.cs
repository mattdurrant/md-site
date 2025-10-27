using System.Globalization;
using System.Text;

namespace TheSequelCommittee;

public static class Csv
{
    private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    public static string BuildFranchisesCsv(IEnumerable<FranchiseAgg> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("collection_id,collection_name,movie_count,sum_popularity,total_vote_count,avg_vote_weighted,score,max_popularity");
        foreach (var r in rows)
        {
            sb.Append(r.CollectionId).Append(",");
            sb.Append(Utils.CsvEscape(r.Name)).Append(",");
            sb.Append(r.MovieCount).Append(",");
            sb.Append(r.SumPopularity.ToString(CI)).Append(",");
            sb.Append(r.TotalVoteCount).Append(",");
            sb.Append(r.AvgVoteWeighted.ToString("0.###", CI)).Append(",");
            sb.Append(r.Score.ToString("0.######", CI)).Append(",");
            sb.Append(r.MaxPopularity.ToString(CI)).AppendLine();
        }
        return sb.ToString();
    }

    public static string BuildMembersCsv(IEnumerable<MemberRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("collection_id,collection_name,movie_tmdb_id,title,release_date,popularity,vote_average,vote_count,imdb_id,poster_path");
        foreach (var r in rows.OrderBy(x => x.CollectionId).ThenBy(x => x.ReleaseDate ?? "9999-12-31"))
        {
            sb.Append(r.CollectionId).Append(",");
            sb.Append(Utils.CsvEscape(r.CollectionName)).Append(",");
            sb.Append(r.MovieTmdbId).Append(",");
            sb.Append(Utils.CsvEscape(r.Title)).Append(",");
            sb.Append(Utils.CsvEscape(r.ReleaseDate ?? "")).Append(",");
            sb.Append(r.Popularity.ToString(CI)).Append(",");
            sb.Append(r.VoteAverage.ToString("0.###", CI)).Append(",");
            sb.Append(r.VoteCount).Append(",");
            sb.Append(Utils.CsvEscape(r.ImdbId)).Append(",");
            sb.Append(Utils.CsvEscape(r.PosterPath ?? "")).AppendLine();
        }
        return sb.ToString();
    }

    public static string BuildMovieRatingsCsv(IEnumerable<MovieJoined> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("collection_id,collection_name,movie_tmdb_id,imdb_id,title,release_date,imdb_rating_100,imdb_votes,tmdb_vote_avg_x10,tmdb_vote_count,rt_critic_pct,rt_audience_pct,omdb_error,poster_path");
        foreach (var r in rows.OrderBy(x => x.CollectionId).ThenBy(x => x.ReleaseDate ?? DateTime.MaxValue))
        {
            sb.Append(r.CollectionId).Append(",");
            sb.Append(Utils.CsvEscape(r.CollectionName)).Append(",");
            sb.Append(r.MovieTmdbId).Append(",");
            sb.Append(Utils.CsvEscape(r.ImdbId)).Append(",");
            sb.Append(Utils.CsvEscape(r.Title)).Append(",");
            sb.Append(r.ReleaseDate?.ToString("yyyy-MM-dd") ?? "").Append(",");
            sb.Append(r.ImdbRating100?.ToString("0.##", CI) ?? "").Append(",");
            sb.Append(r.ImdbVotes?.ToString() ?? "").Append(",");
            sb.Append((r.TmdbVoteAverage * 10.0).ToString("0.##", CI)).Append(",");
            sb.Append(r.TmdbVoteCount).Append(",");
            sb.Append(r.RtCriticPct?.ToString("0.##", CI) ?? "").Append(",");
            sb.Append(r.RtAudiencePct?.ToString("0.##", CI) ?? "").Append(",");
            sb.Append(Utils.CsvEscape(r.OmdbError ?? "")).Append(",");
            sb.Append(Utils.CsvEscape(r.PosterPath ?? "")).AppendLine();
        }
        return sb.ToString();
    }

    // NEW: write franchise_runs.csv
    public static string BuildRunCsv(IEnumerable<FranchiseRunRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("collection_id,collection_name,film_count,good_run_len,peak_index,peak_title,fall_index,fall_title,cliff_drop,avg_first_n,avg_all,missing_ratings,streak_start,streak_end,streak_len,streak_avg,good_indices,good_thresh");
        foreach (var r in rows.OrderBy(x => x.CollectionId))
        {
            sb.Append(r.CollectionId).Append(",");
            sb.Append(Utils.CsvEscape(r.CollectionName)).Append(",");
            sb.Append(r.FilmCount).Append(",");
            sb.Append(r.GoodRunLength).Append(",");
            sb.Append(r.PeakIndex).Append(",");
            sb.Append(Utils.CsvEscape(r.PeakTitle ?? "")).Append(",");
            sb.Append(r.FallIndex?.ToString() ?? "").Append(",");
            sb.Append(Utils.CsvEscape(r.FallTitle ?? "")).Append(",");
            sb.Append(r.CliffDrop?.ToString("0.##", CI) ?? "").Append(",");
            sb.Append(r.AvgFirstN?.ToString("0.##", CI) ?? "").Append(",");
            sb.Append(r.AvgAll?.ToString("0.##", CI) ?? "").Append(",");
            sb.Append(r.HasAnyMissingRatings ? "1" : "0").Append(",");
            sb.Append(r.StreakStartIndex?.ToString() ?? "").Append(",");
            sb.Append(r.StreakEndIndex?.ToString() ?? "").Append(",");
            sb.Append(r.StreakLength).Append(",");
            sb.Append(r.StreakAvg?.ToString("0.##", CI) ?? "").Append(",");
            sb.Append(Utils.CsvEscape(r.GoodIndicesCsv ?? "")).Append(",");
            sb.Append(r.GoodThreshold).AppendLine();
        }
        return sb.ToString();
    }

    public static List<FranchiseAgg> LoadFranchisesCsv(string path)
    {
        var rows = new List<FranchiseAgg>();
        foreach (var (idx, cols) in ReadCsv(path))
        {
            if (idx == 0) continue;
            rows.Add(new FranchiseAgg
            {
                CollectionId = ToInt(cols, 0),
                Name = Get(cols, 1),
                MovieCount = ToInt(cols, 2),
                SumPopularity = ToDouble(cols, 3),
                TotalVoteCount = ToInt(cols, 4),
                AvgVoteWeighted = ToDouble(cols, 5),
                Score = ToDouble(cols, 6),
                MaxPopularity = ToDouble(cols, 7)
            });
        }
        return rows;
    }

    public static List<MemberRow> LoadMembersCsv(string path)
    {
        var rows = new List<MemberRow>();
        foreach (var (idx, cols) in ReadCsv(path))
        {
            if (idx == 0) continue;
            rows.Add(new MemberRow
            {
                CollectionId = ToInt(cols, 0),
                CollectionName = Get(cols, 1),
                MovieTmdbId = ToInt(cols, 2),
                Title = Get(cols, 3),
                ReleaseDate = Get(cols, 4),
                Popularity = ToDouble(cols, 5),
                VoteAverage = ToDouble(cols, 6),
                VoteCount = ToInt(cols, 7),
                ImdbId = Get(cols, 8),
                PosterPath = Get(cols, 9)
            });
        }
        return rows;
    }

    public static List<MovieRatingRow> LoadMovieRatingsCsv(string path)
    {
        var rows = new List<MovieRatingRow>();
        foreach (var (idx, cols) in ReadCsv(path))
        {
            if (idx == 0) continue;
            rows.Add(new MovieRatingRow(
                CollectionId: ToInt(cols, 0),
                MovieTmdbId: ToInt(cols, 2),
                ImdbId: Get(cols, 3),
                ImdbRating100: ToNullableDouble(cols, 6),
                ImdbVotes: ToNullableInt(cols, 7),
                Error: Get(cols, 12),
                RtCriticPct: ToNullableDouble(cols, 10),
                RtAudiencePct: ToNullableDouble(cols, 11)
            ));
        }
        return rows;
    }

    // --- CSV parsing helpers ---
    private static IEnumerable<(int rowIndex, List<string> cols)> ReadCsv(string path)
    {
        using var sr = new StreamReader(path, Encoding.UTF8);
        string? line; int row = 0;
        while ((line = sr.ReadLine()) is not null)
        {
            var cols = ParseCsvLine(line);
            while (UnclosedQuotesCount(line) % 2 == 1)
            {
                var next = sr.ReadLine();
                if (next is null) break;
                line += "\n" + next;
                cols = ParseCsvLine(line);
            }
            yield return (row++, cols);
        }
    }

    private static int UnclosedQuotesCount(string s)
    {
        int cnt = 0; bool inQuote = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"')
            {
                if (i + 1 < s.Length && s[i + 1] == '"') { i++; continue; }
                inQuote = !inQuote;
                if (inQuote) cnt++;
            }
        }
        return cnt;
    }

    private static List<string> ParseCsvLine(string s)
    {
        var cols = new List<string>(); var sb = new StringBuilder(); bool inQuote = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (inQuote)
            {
                if (c == '"' && i + 1 < s.Length && s[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') inQuote = false;
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inQuote = true;
                else if (c == ',') { cols.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
        }
        cols.Add(sb.ToString());
        return cols;
    }

    private static string Get(List<string> cols, int i) => i < cols.Count ? cols[i] : "";
    private static int ToInt(List<string> cols, int i) => int.TryParse(Get(cols, i), out var v) ? v : 0;
    private static int? ToNullableInt(List<string> cols, int i) => int.TryParse(Get(cols, i), out var v) ? v : (int?)null;
    private static double ToDouble(List<string> cols, int i) => double.TryParse(Get(cols, i), NumberStyles.Any, CI, out var v) ? v : 0.0;
    private static double? ToNullableDouble(List<string> cols, int i) => double.TryParse(Get(cols, i), NumberStyles.Any, CI, out var v) ? v : (double?)null;
}
