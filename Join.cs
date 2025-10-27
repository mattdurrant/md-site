namespace TheSequelCommittee;

public static class Join
{
    public static List<MovieJoined> JoinRatings(List<MemberRow> members, List<MovieRatingRow> ratings)
    {
        var lookup = ratings.ToDictionary(r => (r.CollectionId, r.MovieTmdbId));
        return members.Select(m =>
        {
            lookup.TryGetValue((m.CollectionId, m.MovieTmdbId), out var r);
            return new MovieJoined
            {
                CollectionId = m.CollectionId,
                CollectionName = m.CollectionName,
                MovieTmdbId = m.MovieTmdbId,
                ImdbId = m.ImdbId,
                Title = m.Title,
                ReleaseDate = Utils.ParseDate(m.ReleaseDate),
                Popularity = m.Popularity,
                TmdbVoteAverage = m.VoteAverage,
                TmdbVoteCount = m.VoteCount,
                ImdbRating100 = r?.ImdbRating100,
                ImdbVotes = r?.ImdbVotes,
                OmdbError = r?.Error,
                PosterPath = m.PosterPath,
                RtCriticPct = r?.RtCriticPct,
                RtAudiencePct = r?.RtAudiencePct
            };
        }).ToList();
    }
}
