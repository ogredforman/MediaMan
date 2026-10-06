using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Core.Modules;
using MediaMan.Data;
using MediaMan.Parsing;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Library.Scanning;

/// <summary>Counts from one scan. <see cref="Unreadable"/> overlaps the others: it counts probed files that failed.</summary>
public sealed record ScanResult(int Added, int Changed, int Unchanged, int Restored, int Missing, int Unreadable, int Purged);

/// <summary>
/// Scans a library's roots and brings its MediaFiles up to date (SPEC §7.2 steps 1–3 and 6): new and changed files
/// are probed and parsed and left <see cref="MediaFileStatus.Pending"/> for the matcher, files that fail to probe
/// are <see cref="MediaFileStatus.NeedsAttention"/>, and files no longer on disk are <see cref="MediaFileStatus.Missing"/>
/// (or keep <see cref="MediaFileStatus.Ignored"/>) with <see cref="MediaFile.MissingSince"/> set until <see cref="MissingRetention"/> passes. It only reads under the roots; it never writes there (SPEC §5).
/// </summary>
public sealed class LibraryScanner(MediaManDbContext db, IMediaProbe probe, IClock clock)
{
    public const string UnreadableReason = "unreadable";

    /// <summary>How long a missing file and its FileLinks are kept, so a remounted drive keeps its watch state.</summary>
    public static readonly TimeSpan MissingRetention = TimeSpan.FromDays(30);

    // Folder depth limit, so a symlink loop can't recurse forever.
    private const int MaxDepth = 32;
    private const int SaveBatchSize = 100;

    public async Task<ScanResult> ScanAsync(Guid libraryId, CancellationToken ct)
    {
        var library = await db.Libraries.AsNoTracking().FirstOrDefaultAsync(l => l.Id == libraryId, ct)
            ?? throw new KeyNotFoundException($"Library {libraryId} does not exist.");
        var stored = await db.MediaFiles.Where(f => f.LibraryId == libraryId).ToDictionaryAsync(f => f.Path, StringComparer.Ordinal, ct);
        var linked = (await db.FileLinks
                .Where(l => db.MediaFiles.Any(f => f.Id == l.MediaFileId && f.LibraryId == libraryId))
                .Select(l => l.MediaFileId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        int added = 0, changed = 0, unchanged = 0, restored = 0, unreadable = 0, sinceSave = 0;

        foreach (var root in library.RootPaths)
        {
            foreach (var found in Walk(root))
            {
                ct.ThrowIfCancellationRequested();
                if (!seen.Add(found.Path))
                {
                    continue;
                }

                stored.TryGetValue(found.Path, out var file);
                var wasMissing = file?.MissingSince is not null;
                var isChanged = file is null || file.SizeBytes != found.SizeBytes || file.Mtime != found.Mtime;

                if (!isChanged)
                {
                    if (wasMissing)
                    {
                        if (file!.Status != MediaFileStatus.Ignored)
                        {
                            file.Status = file.Probe is null ? MediaFileStatus.NeedsAttention
                                : linked.Contains(file.Id) ? MediaFileStatus.Matched
                                : MediaFileStatus.Pending;
                            file.AttentionReason = file.Probe is null ? UnreadableReason : null;
                        }

                        file.MissingSince = null;
                        restored++;
                    }
                    else
                    {
                        unchanged++;
                    }

                    continue;
                }

                ProbeResult? result;
                try
                {
                    result = await probe.ProbeAsync(found.Path, ct);
                }
                catch (FileNotFoundException)
                {
                    // Deleted between listing and probing: treat it as not seen, so a stored file is marked missing.
                    seen.Remove(found.Path);
                    continue;
                }
                catch (InvalidDataException)
                {
                    result = null;
                    unreadable++;
                }

                if (file is null)
                {
                    file = new MediaFile { Id = Ids.New(clock), LibraryId = libraryId, Path = found.Path };
                    db.MediaFiles.Add(file);
                    stored[found.Path] = file;
                    added++;
                }
                else if (wasMissing)
                {
                    restored++;
                }
                else
                {
                    changed++;
                }

                file.SizeBytes = found.SizeBytes;
                file.Mtime = found.Mtime;
                file.Probe = result;
                file.Parsed = ReleaseParser.Parse(Path.GetFileName(found.Path), found.ParentDirs);
                file.MissingSince = null;
                if (result is null)
                {
                    file.Status = MediaFileStatus.NeedsAttention;
                    file.AttentionReason = UnreadableReason;
                }
                else
                {
                    // A user's Ignored choice survives the file changing.
                    file.Status = file.Status == MediaFileStatus.Ignored ? MediaFileStatus.Ignored : MediaFileStatus.Pending;
                    file.AttentionReason = null;
                }

                if (++sinceSave >= SaveBatchSize)
                {
                    await db.SaveChangesAsync(ct);
                    sinceSave = 0;
                }
            }
        }

        var now = clock.UtcNow;
        var missing = 0;
        var purged = new List<MediaFile>();
        foreach (var file in stored.Values.Where(f => !seen.Contains(f.Path)))
        {
            if (file.MissingSince is null)
            {
                // An Ignored file keeps its status while away, so the user's choice survives a remount.
                if (file.Status != MediaFileStatus.Ignored)
                {
                    file.Status = MediaFileStatus.Missing;
                    file.AttentionReason = null;
                }

                file.MissingSince = now;
                missing++;
            }
            else if (file.MissingSince is { } since && now - since >= MissingRetention)
            {
                purged.Add(file);
            }
        }

        // FileLinks and MatchReviews go with the file (cascade delete).
        db.MediaFiles.RemoveRange(purged);
        await db.SaveChangesAsync(ct);
        return new ScanResult(added, changed, unchanged, restored, missing, unreadable, purged.Count);
    }

    private sealed record FoundFile(string Path, long SizeBytes, DateTimeOffset Mtime, IReadOnlyList<string> ParentDirs);

    /// <summary>
    /// Video files under <paramref name="root"/>, skipping extras folders, samples and anything unreadable.
    /// A root that doesn't exist or can't be read yields nothing, so its files are marked missing.
    /// </summary>
    private static IEnumerable<FoundFile> Walk(string root)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false };
        var pending = new Stack<(DirectoryInfo Dir, string[] ParentDirs)>();
        pending.Push((new DirectoryInfo(Path.GetFullPath(root)), []));

        while (pending.Count > 0)
        {
            var (dir, parents) = pending.Pop();
            if (!TryList(dir, options, out var entries))
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (entry is DirectoryInfo sub)
                {
                    if (parents.Length < MaxDepth && !ScanRules.IsExtrasFolder(sub.Name))
                    {
                        pending.Push((sub, [.. parents, sub.Name]));
                    }
                }
                else if (entry is FileInfo info && ScanRules.ShouldScan(info.Name, info.Length))
                {
                    yield return new FoundFile(info.FullName, info.Length, ToStoredPrecision(info.LastWriteTimeUtc), parents);
                }
            }
        }
    }

    private static bool TryList(DirectoryInfo dir, EnumerationOptions options, out List<FileSystemInfo> entries)
    {
        try
        {
            entries = [.. dir.EnumerateFileSystemInfos("*", options)];
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            entries = [];
            return false;
        }
    }

    // Timestamps are stored as Unix milliseconds, so compare at that precision or every rescan sees a change.
    private static DateTimeOffset ToStoredPrecision(DateTime utc) =>
        DateTimeOffset.FromUnixTimeMilliseconds(new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds());
}
