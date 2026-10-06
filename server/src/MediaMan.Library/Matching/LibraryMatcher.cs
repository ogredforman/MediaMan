using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Core.Modules;
using MediaMan.Data;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Library.Matching;

/// <summary>Matcher tuning (SPEC §7.2 steps 4–5 and 7).</summary>
public sealed record MatcherOptions
{
    /// <summary>The score a candidate needs to be linked without review (assumed 0.85 until tuned, SPEC §15).</summary>
    public double Threshold { get; init; } = 0.85;

    /// <summary>The best candidate must beat the runner-up by more than this, or the file goes to review.</summary>
    public double AmbiguityMargin { get; init; } = 0.1;

    /// <summary>How many search results are scored and kept on a MatchReview.</summary>
    public int MaxCandidates { get; init; } = 5;

    /// <summary>Waits between attempts after a provider error; one retry per entry.</summary>
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(15)];
}

/// <summary>A confident match, for <see cref="Importing.MetadataImporter"/> to turn into Titles and FileLinks.</summary>
public sealed record MatchDecision(
    Guid MediaFileId,
    ExternalId TitleRef,
    TitleKind Kind,
    int? Season,
    IReadOnlyList<int> Episodes,
    int? AbsoluteEpisode,
    double Score);

/// <summary>
/// One matcher run: confident <see cref="Decisions"/>, the number of files sent to review, and the number left
/// <see cref="MediaFileStatus.Pending"/> because the provider failed.
/// </summary>
public sealed record MatchRunResult(IReadOnlyList<MatchDecision> Decisions, int Reviewed, int Deferred);

/// <summary>
/// Matches a library's <see cref="MediaFileStatus.Pending"/> files against the metadata provider (SPEC §7.2 steps 4–5
/// and 7). At or above the threshold, with a clear lead over the runner-up, it returns a <see cref="MatchDecision"/>
/// and leaves the file Pending for the importer. Otherwise it writes a MatchReview with the top candidates and marks
/// the file <see cref="MediaFileStatus.NeedsAttention"/>; it never links a best guess. Provider errors are retried
/// with backoff; if they persist, the run stops calling the provider and the remaining files stay Pending for the
/// next scan, never marked wrong.
/// </summary>
public sealed class LibraryMatcher(MediaManDbContext db, IMetadataProvider provider, IClock clock, MatcherOptions options)
{
    public const string NoMatchReason = "no-match";
    public const string LowConfidenceReason = "low-confidence";
    public const string AmbiguousReason = "ambiguous";

    public async Task<MatchRunResult> MatchPendingAsync(Guid libraryId, CancellationToken ct)
    {
        var files = await db.MediaFiles
            .Where(f => f.LibraryId == libraryId && f.Status == MediaFileStatus.Pending)
            .OrderBy(f => f.Path)
            .ToListAsync(ct);
        var fileIds = files.Select(f => f.Id).ToList();
        var reviews = await db.MatchReviews
            .Where(r => fileIds.Contains(r.MediaFileId))
            .ToDictionaryAsync(r => r.MediaFileId, ct);

        var run = new ProviderSession(provider, options);
        var decisions = new List<MatchDecision>();
        var reviewed = 0;
        var deferred = 0;
        foreach (var file in files)
        {
            if (run.ProviderDown)
            {
                deferred++;
                continue;
            }

            List<MatchCandidate> ranked;
            try
            {
                ranked = await RankAsync(run, file.Parsed, ct);
            }
            catch (ProviderUnavailableException)
            {
                deferred++;
                continue;
            }

            var reason = Judge(ranked);
            reviews.TryGetValue(file.Id, out var existing);
            if (reason is null)
            {
                var parsed = file.Parsed!;
                decisions.Add(new MatchDecision(
                    file.Id,
                    ranked[0].TitleRef,
                    MatchScorer.ExpectedKind(parsed),
                    parsed.Season,
                    parsed.Episodes,
                    parsed.AbsoluteEpisode,
                    ranked[0].Score));
                if (existing is not null)
                {
                    db.MatchReviews.Remove(existing);
                }

                continue;
            }

            var candidates = ranked.Take(options.MaxCandidates).ToList();
            if (existing is null)
            {
                db.MatchReviews.Add(new MatchReview { Id = Ids.New(clock), MediaFileId = file.Id, Candidates = candidates, Reason = reason });
            }
            else
            {
                existing.Candidates = candidates;
                existing.Reason = reason;
            }

            file.Status = MediaFileStatus.NeedsAttention;
            file.AttentionReason = reason;
            reviewed++;
        }

        // One save at the end: if the run is cancelled or fails, nothing is lost but staged reviews, and those
        // files are still Pending, so the next run redoes them.
        await db.SaveChangesAsync(ct);
        return new MatchRunResult(decisions, reviewed, deferred);
    }

    /// <summary>Null when the best candidate is confident and clear of the runner-up; otherwise the review reason.</summary>
    private string? Judge(List<MatchCandidate> ranked)
    {
        if (ranked.Count == 0)
        {
            return NoMatchReason;
        }

        if (ranked[0].Score < options.Threshold)
        {
            return LowConfidenceReason;
        }

        return ranked.Count > 1 && ranked[0].Score - ranked[1].Score <= options.AmbiguityMargin ? AmbiguousReason : null;
    }

    /// <summary>The scored candidates, best first.</summary>
    private async Task<List<MatchCandidate>> RankAsync(ProviderSession run, ParsedRelease? parsed, CancellationToken ct)
    {
        if (parsed?.Title is not { Length: > 0 } title)
        {
            return [];
        }

        var kind = MatchScorer.ExpectedKind(parsed);
        var results = await run.SearchAsync(new TitleQuery(title, parsed.Year, kind), ct);
        var ranked = new List<MatchCandidate>();
        foreach (var candidate in results.Take(options.MaxCandidates))
        {
            var details = kind == TitleKind.Show && candidate.Kind == TitleKind.Show
                ? await run.GetTitleAsync(candidate.Id, ct)
                : null;
            ranked.Add(new MatchCandidate(
                candidate.Id,
                Math.Round(MatchScorer.Score(parsed, candidate, details), 4),
                candidate.Kind,
                candidate.Name,
                candidate.Year,
                candidate.PosterUrl));
        }

        // OrderByDescending is stable, so equal scores keep the provider's order.
        return [.. ranked.OrderByDescending(c => c.Score)];
    }
}
