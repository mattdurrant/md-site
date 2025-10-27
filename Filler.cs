namespace TheSequelCommittee;

public static class Filler
{
    public static async Task<int> FillMissingCollectionPartsAsync(
        string apiKey,
        Dictionary<int, FranchiseAgg> franchises,
        List<MemberRow> members,
        int sleepMsBetweenCalls,
        int fillLimit)
    {
        using var tmdb = Tmdb.NewClient();

        var haveByCollection = members
            .GroupBy(m => m.CollectionId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.MovieTmdbId).ToHashSet());

        int added = 0;
        int processed = 0;

        foreach (var f in franchises.Values.OrderByDescending(x => x.MovieCount))
        {
            processed++;
            var cd = await Tmdb.GetCollectionAsync(tmdb, apiKey, f.CollectionId);
            if (cd?.Parts is null || cd.Parts.Count == 0)
            {
                await Task.Delay(sleepMsBetweenCalls);
                continue;
            }

            if (!haveByCollection.TryGetValue(f.CollectionId, out var have)) { have = new HashSet<int>(); haveByCollection[f.CollectionId] = have; }

            foreach (var p in cd.Parts.OrderBy(x => Utils.ParseDate(x.ReleaseDate) ?? DateTime.MaxValue).ThenBy(x => x.Title))
            {
                if (have.Contains(p.Id)) continue;

                var d = await Tmdb.GetMovieDetailsAsync(tmdb, apiKey, p.Id);

                members.Add(new MemberRow
                {
                    CollectionId = f.CollectionId,
                    CollectionName = f.Name,
                    MovieTmdbId = p.Id,
                    Title = d?.Title ?? p.Title ?? "",
                    ReleaseDate = d?.ReleaseDate ?? p.ReleaseDate,
                    Popularity = p.Popularity,
                    VoteAverage = p.VoteAverage,
                    VoteCount = p.VoteCount,
                    ImdbId = d?.ExternalIds?.ImdbId ?? "",
                    PosterPath = d?.PosterPath ?? p.PosterPath
                });
                have.Add(p.Id);
                added++;

                if (added % 10 == 0)
                    Console.WriteLine($"  [Fill] Added {added} movies … (collection {processed}/{franchises.Count})");

                if (added >= fillLimit)
                {
                    Console.WriteLine($"  [Fill] Hit fill limit ({fillLimit}). Stopping.");
                    return added;
                }

                await Task.Delay(sleepMsBetweenCalls);
            }

            await Task.Delay(sleepMsBetweenCalls);
        }

        return added;
    }
}
