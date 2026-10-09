using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Core.Modules;
using MediaMan.Data;
using MediaMan.Library.Importing;
using MediaMan.Library.Scanning;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Library.Matching;

/// <summary>A "Fix match" choice: the title, and for shows the season and episodes the file holds (SPEC §7.3).</summary>
public sealed record MatchChoice(string? TitleRef, int? Season, IReadOnlyList<int>? Episodes);

public enum RelinkFailure
{
    NotFound,

    /// <summary>The file is missing, ignored or unreadable, so there's nothing to link.</summary>
    FileUnavailable,

    /// <summary>The file's library is being scanned; the scan's importer could race the relink.</summary>
    ScanRunning,

    /// <summary>The choice is invalid; <see cref="RelinkOutcome.Errors"/> says which field and why.</summary>
    Invalid,

    /// <summary>The provider doesn't know the chosen season or episode.</summary>
    EpisodeNotFound,

    /// <summary>The metadata provider kept failing.</summary>
    ProviderUnavailable,
}

/// <summary>The relinked file's title, or why the relink was refused. A refused relink changes nothing.</summary>
public sealed record RelinkOutcome(Guid? TitleId, RelinkFailure? Failure = null, IDictionary<string, string[]>? Errors = null)
{
    public static RelinkOutcome Failed(RelinkFailure failure) => new(null, failure);

    public static RelinkOutcome Invalid(string field, string message) =>
        new(null, RelinkFailure.Invalid, new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>
/// "Needs attention" and "Fix match" (SPEC §7.3): a manual catalog search, and relinking a file to the title the user
/// picked. A relink runs the importer on the choice in one transaction and records it as a <see cref="MatchOverride"/>
/// for the path, which <see cref="LibraryMatcher"/> follows on later scans. Only rows change; files on disk are never touched.
/// </summary>
public sealed class MatchFixer(MediaManDbContext db, IMetadataProvider provider, MetadataImporter importer, IClock clock, MatcherOptions options)
{
    public const int MaxSearchResults = 20;

    /// <summary>The provider's results, best first; null when the provider failed.</summary>
    public async Task<IReadOnlyList<TitleCandidate>?> SearchAsync(TitleQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            // One attempt: the user is waiting and can search again.
            return [.. (await provider.SearchAsync(query, ct)).Take(MaxSearchResults)];
        }
        catch (Exception e) when (ProviderSession.IsProviderError(e, ct))
        {
            return null;
        }
    }

    /// <param name="scanRunning">
    /// Whether a library's scan is running; a relink waits for it to finish. The check isn't atomic with the relink: a scan
    /// starting in between waits on SQLite's write lock, and its importer skips the file once it's no longer Pending.
    /// </param>
    public async Task<RelinkOutcome> RelinkAsync(Guid mediaFileId, MatchChoice choice, Func<Guid, bool> scanRunning, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(scanRunning);
        var file = await db.MediaFiles.SingleOrDefaultAsync(f => f.Id == mediaFileId, ct);
        if (file is null)
        {
            return RelinkOutcome.Failed(RelinkFailure.NotFound);
        }

        if (file.Status is MediaFileStatus.Missing or MediaFileStatus.Ignored || file.AttentionReason == LibraryScanner.UnreadableReason)
        {
            return RelinkOutcome.Failed(RelinkFailure.FileUnavailable);
        }

        if (scanRunning(file.LibraryId))
        {
            return RelinkOutcome.Failed(RelinkFailure.ScanRunning);
        }

        if (!ExternalId.TryParse(choice.TitleRef, out var titleRef) || titleRef.Provider != provider.Key)
        {
            return RelinkOutcome.Invalid("titleRef", "Pick a title from the search results.");
        }

        TitleKind kind;
        var stored = await db.Titles.WithExternalId(titleRef).FirstOrDefaultAsync(ct);
        if (stored is not null)
        {
            kind = stored.Kind;
        }
        else
        {
            try
            {
                kind = (await new ProviderSession(provider, options).GetTitleAsync(titleRef, ct)).Kind;
            }
            catch (KeyNotFoundException)
            {
                return RelinkOutcome.Invalid("titleRef", "The catalog has no title with this id. Search again and pick one of the results.");
            }
            catch (ProviderUnavailableException)
            {
                return RelinkOutcome.Failed(RelinkFailure.ProviderUnavailable);
            }
        }

        var (season, episodes, absolute, error) = Map(kind, choice, file.Parsed);
        if (error is not null)
        {
            return error;
        }

        var decision = new MatchDecision(file.Id, titleRef, kind, season, episodes, absolute, 1);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        file.Status = MediaFileStatus.Pending;
        file.AttentionReason = null;
        await db.SaveChangesAsync(ct);

        var imported = await importer.ImportAsync([decision], ct);
        if (imported.Linked == 0)
        {
            // Undoes the importer's writes too: the file keeps its status, review and links. With the title resolved and
            // the file Pending, the importer links nothing only when the provider failed or an episode isn't listed.
            await transaction.RollbackAsync(ct);
            return RelinkOutcome.Failed(imported.Deferred > 0 ? RelinkFailure.ProviderUnavailable : RelinkFailure.EpisodeNotFound);
        }

        var recorded = await db.MatchOverrides.SingleOrDefaultAsync(o => o.LibraryId == file.LibraryId && o.Path == file.Path, ct);
        if (recorded is null)
        {
            recorded = new MatchOverride { Id = Ids.New(clock), LibraryId = file.LibraryId, Path = file.Path };
            db.MatchOverrides.Add(recorded);
        }

        recorded.TitleRef = titleRef;
        recorded.Kind = kind;
        recorded.Season = season;
        recorded.Episodes = [.. episodes];
        recorded.AbsoluteEpisode = absolute;
        recorded.CreatedAt = clock.UtcNow;
        await db.MatchReviews.Where(r => r.MediaFileId == file.Id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new RelinkOutcome(await TitleOfAsync(file.Id, ct));
    }

    /// <summary>The season and episodes to link: the choice's, or for a show without one, what the parser read.</summary>
    private static (int? Season, IReadOnlyList<int> Episodes, int? Absolute, RelinkOutcome? Error) Map(
        TitleKind kind, MatchChoice choice, ParsedRelease? parsed)
    {
        var episodes = choice.Episodes ?? [];
        if (kind == TitleKind.Movie)
        {
            return choice.Season is not null || episodes.Count > 0
                ? (null, [], null, RelinkOutcome.Invalid("episodes", "A movie has no seasons or episodes. Leave them out, or pick a show."))
                : (null, [], null, null);
        }

        if (episodes.Count > 0)
        {
            if (choice.Season is not { } season)
            {
                return (null, [], null, RelinkOutcome.Invalid("season", "Pick the season these episodes are in."));
            }

            return season < 0 || episodes.Any(e => e < 1)
                ? (null, [], null, RelinkOutcome.Invalid("episodes", "Seasons start at 0 (specials) and episodes at 1."))
                : (season, [.. episodes.Distinct()], null, null);
        }

        if (choice.Season is not null)
        {
            return (null, [], null, RelinkOutcome.Invalid("episodes", "Pick the episodes this file holds."));
        }

        if (parsed is { Episodes.Count: > 0 })
        {
            return (parsed.Season, parsed.Episodes, null, null);
        }

        return parsed?.AbsoluteEpisode is { } absolute
            ? (null, [], absolute, null)
            : (null, [], null, RelinkOutcome.Invalid("episodes", "The file name doesn't say which episode this is. Pick the season and episodes."));
    }

    private async Task<Guid?> TitleOfAsync(Guid mediaFileId, CancellationToken ct)
    {
        var playable = await db.FileLinks
            .Where(l => l.MediaFileId == mediaFileId)
            .OrderBy(l => l.Part)
            .Join(db.Playables, l => l.PlayableId, p => p.Id, (_, p) => p)
            .FirstAsync(ct);
        if (playable.TitleId is { } titleId)
        {
            return titleId;
        }

        return await db.Episodes
            .Where(e => e.Id == playable.EpisodeId)
            .Join(db.Seasons, e => e.SeasonId, s => s.Id, (_, s) => s.TitleId)
            .SingleAsync(ct);
    }
}
