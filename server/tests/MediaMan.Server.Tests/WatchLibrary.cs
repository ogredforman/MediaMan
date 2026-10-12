using MediaMan.Core.Domain;
using MediaMan.Data;

namespace MediaMan.Server.Tests;

/// <summary>
/// A library for watch-state tests, with real temp files: a movie, and a show with a special, S01E01–E03 (E03's file
/// missing) and S02E01. Episode Playables are keyed "season x episode", e.g. "1x2".
/// </summary>
internal sealed record WatchLibrary(Guid MovieTitle, Guid Movie, Guid ShowTitle, IReadOnlyDictionary<string, Guid> Episodes)
{
    public static async Task<WatchLibrary> SeedAsync(MediaManDbContext db, string media, CancellationToken ct)
    {
        var libraryId = Guid.CreateVersion7();
        db.Libraries.Add(new Core.Domain.Library
        {
            Id = libraryId,
            Name = "Media",
            Kind = LibraryKind.External,
            ContentType = ContentType.Mixed,
            RootPaths = [media],
        });

        void AddFile(Guid playableId, string name, MediaFileStatus status)
        {
            var path = Path.Combine(media, name);
            File.WriteAllText(path, name);
            var file = new MediaFile
            {
                Id = Guid.CreateVersion7(),
                LibraryId = libraryId,
                Path = path,
                SizeBytes = name.Length,
                Mtime = DateTimeOffset.UnixEpoch,
                Status = status,
                MissingSince = status == MediaFileStatus.Missing ? DateTimeOffset.UnixEpoch : null,
            };
            db.MediaFiles.Add(file);
            db.FileLinks.Add(new FileLink { MediaFileId = file.Id, PlayableId = playableId, Part = 1 });
        }

        var movie = new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Movie, Name = "Alien", Year = 1979 };
        var moviePlayable = Playable.ForMovie(Guid.CreateVersion7(), movie.Id);
        db.Titles.Add(movie);
        db.Playables.Add(moviePlayable);
        AddFile(moviePlayable.Id, "Alien.mkv", MediaFileStatus.Matched);

        var show = new Title { Id = Guid.CreateVersion7(), Kind = TitleKind.Show, Name = "The Office", Year = 2005 };
        db.Titles.Add(show);
        var seasons = new Dictionary<int, Season>();
        var episodes = new Dictionary<string, Guid>();
        foreach (var (season, number, name, status) in new (int, int, string?, MediaFileStatus)[]
        {
            (0, 1, "Special", MediaFileStatus.Matched),
            (1, 1, "Pilot", MediaFileStatus.Matched),
            (1, 2, "Diversity Day", MediaFileStatus.Matched),
            (1, 3, "Health Care", MediaFileStatus.Missing),
            (2, 1, "The Dundies", MediaFileStatus.Matched),
        })
        {
            if (!seasons.TryGetValue(season, out var s))
            {
                s = new Season { Id = Guid.CreateVersion7(), TitleId = show.Id, Number = season };
                seasons[season] = s;
                db.Seasons.Add(s);
            }

            var episode = new Episode { Id = Guid.CreateVersion7(), SeasonId = s.Id, Number = number, Name = name };
            var playable = Playable.ForEpisode(Guid.CreateVersion7(), episode.Id);
            db.Episodes.Add(episode);
            db.Playables.Add(playable);
            AddFile(playable.Id, $"{season}x{number}.mkv", status);
            episodes[$"{season}x{number}"] = playable.Id;
        }

        await db.SaveChangesAsync(ct);
        return new WatchLibrary(movie.Id, moviePlayable.Id, show.Id, episodes);
    }
}
