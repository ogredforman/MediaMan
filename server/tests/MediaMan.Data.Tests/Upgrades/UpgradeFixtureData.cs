using MediaMan.Core;
using MediaMan.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Data.Tests.Upgrades;

/// <summary>
/// The data in every database fixture (<c>server/tests/fixtures/databases</c>): an install that has been set up and used.
/// <see cref="SeedAsync"/> writes it when a fixture is made; <see cref="AssertIntactAsync"/> checks it is all still there
/// after a fixture is migrated to the current schema. A schema change that moves data updates the assertions, never a
/// fixture.
/// </summary>
public static class UpgradeFixtureData
{
    public static readonly DateTimeOffset At = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    public static readonly Guid AdminRoleId = Id(1);
    public static readonly Guid ViewerRoleId = Id(2);
    public static readonly Guid OwnerId = Id(3);
    public static readonly Guid ViewerId = Id(4);
    public static readonly Guid OwnerProfileId = Id(5);
    public static readonly Guid ViewerProfileId = Id(6);
    public static readonly Guid SessionId = Id(7);
    public static readonly Guid LibraryId = Id(8);
    public static readonly Guid MovieFileId = Id(9);
    public static readonly Guid MovieFile720Id = Id(10);
    public static readonly Guid EpisodeFileId = Id(11);
    public static readonly Guid AttentionFileId = Id(12);
    public static readonly Guid MissingFileId = Id(13);
    public static readonly Guid MovieId = Id(14);
    public static readonly Guid ShowId = Id(15);
    public static readonly Guid SeasonId = Id(16);
    public static readonly Guid EpisodeId = Id(17);
    public static readonly Guid MoviePlayableId = Id(18);
    public static readonly Guid EpisodePlayableId = Id(19);
    public static readonly Guid PosterId = Id(20);
    public static readonly Guid StillId = Id(21);
    public static readonly Guid ReviewId = Id(22);
    public static readonly Guid OverrideId = Id(23);
    public static readonly Guid InviteId = Id(24);
    public static readonly Guid AuditId = Id(25);

    public const string OwnerName = "owner";
    public const string OwnerPasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$Zml4dHVyZS1zYWx0LTAx$Zml4dHVyZS1oYXNoLW5vdC1hLXJlYWwtcGFzc3dvcmQ";
    public const string OwnerTotpSecret = "JBSWY3DPEHPK3PXP";
    public const string RefreshTokenHash = "5f0c1a6e3b9d4c2a8e7f6d5c4b3a291807f6e5d4c3b2a1908f7e6d5c4b3a2918";
    public const string TmdbTokenKey = "metadata.tmdb.accessToken";
    public const string TmdbToken = "fixture-tmdb-read-access-token";
    public const string MoviePath = "/media/Movies/The Matrix (1999)/The Matrix (1999) 2160p.mkv";
    public const string AttentionPath = "/media/Movies/Unknown Thing (2020).mkv";

    /// <summary>Writes the fixture data through the current model; secrets are encrypted with the test key.</summary>
    public static async Task SeedAsync(MediaManDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        db.Roles.AddRange(
            new Role { Id = AdminRoleId, Name = "Admin", Permissions = [Permissions.LibraryView, Permissions.ServerControl], IsPreset = true },
            new Role { Id = ViewerRoleId, Name = "Viewer", Permissions = [Permissions.LibraryView], IsPreset = true });
        db.Users.AddRange(
            new User
            {
                Id = OwnerId,
                Username = OwnerName,
                PasswordHash = OwnerPasswordHash,
                RoleId = AdminRoleId,
                IsSuperadmin = true,
                TotpSecret = OwnerTotpSecret,
                CreatedAt = At,
            },
            new User { Id = ViewerId, Username = "viewer", PasswordHash = OwnerPasswordHash, RoleId = ViewerRoleId, CreatedAt = At, Disabled = true });
        db.Profiles.AddRange(
            new Profile
            {
                Id = OwnerProfileId,
                UserId = OwnerId,
                Name = "Owner",
                Avatar = "purple",
                AudioLangPrefs = ["ja", "en"],
                SubtitleLangPrefs = ["en"],
            },
            new Profile { Id = ViewerProfileId, UserId = ViewerId, Name = "Viewer" });
        db.Sessions.Add(new Session
        {
            Id = SessionId,
            UserId = OwnerId,
            DeviceName = "Desktop",
            RefreshTokenHash = RefreshTokenHash,
            CreatedAt = At,
            LastSeenAt = At.AddHours(1),
            ExpiresAt = At.AddDays(30),
            ActiveProfileId = OwnerProfileId,
        });
        db.Invites.Add(new Invite
        {
            Id = InviteId,
            TokenHash = "invite-token-hash",
            RoleId = ViewerRoleId,
            CreatedBy = OwnerId,
            ExpiresAt = At.AddDays(7),
            MaxUses = 2,
            Uses = 1,
        });

        db.Libraries.Add(new Library
        {
            Id = LibraryId,
            Name = "Media",
            Kind = LibraryKind.External,
            ContentType = ContentType.Mixed,
            RootPaths = ["/media/Movies", "/media/Shows"],
            ScanIntervalMinutes = 60,
            LastScanFinishedAt = At,
        });
        db.MediaFiles.AddRange(
            new MediaFile
            {
                Id = MovieFileId,
                LibraryId = LibraryId,
                Path = MoviePath,
                SizeBytes = 40_000_000_000,
                Mtime = At.AddDays(-10),
                AddedAt = At.AddDays(-9),
                Status = MediaFileStatus.Matched,
                Parsed = new ParsedRelease { Title = "The Matrix", Year = 1999, Resolution = Resolution.P2160, HdrFormat = "HDR10" },
                Probe = new ProbeResult
                {
                    Container = "matroska",
                    Duration = TimeSpan.FromMinutes(136),
                    HdrFormat = "HDR10",
                    Streams =
                    [
                        new StreamInfo { Index = 0, Type = MediaStreamType.Video, Codec = "hevc", Width = 3840, Height = 2160, IsDefault = true },
                        new StreamInfo { Index = 1, Type = MediaStreamType.Audio, Codec = "truehd", Language = "eng", Channels = 8 },
                    ],
                },
            },
            new MediaFile
            {
                Id = MovieFile720Id,
                LibraryId = LibraryId,
                Path = "/media/Movies/The Matrix (1999)/The Matrix (1999) 720p.mkv",
                SizeBytes = 4_000_000_000,
                Mtime = At.AddDays(-8),
                AddedAt = At.AddDays(-8),
                Status = MediaFileStatus.Matched,
                Parsed = new ParsedRelease { Title = "The Matrix", Year = 1999, Resolution = Resolution.P720 },
            },
            new MediaFile
            {
                Id = EpisodeFileId,
                LibraryId = LibraryId,
                Path = "/media/Shows/Breaking Bad/Season 01/Breaking Bad S01E01.mkv",
                SizeBytes = 2_000_000_000,
                Mtime = At.AddDays(-5),
                AddedAt = At.AddDays(-5),
                Status = MediaFileStatus.Matched,
                Parsed = new ParsedRelease { Title = "Breaking Bad", Season = 1, Episodes = [1] },
            },
            new MediaFile
            {
                Id = AttentionFileId,
                LibraryId = LibraryId,
                Path = AttentionPath,
                SizeBytes = 1_000,
                Mtime = At.AddDays(-2),
                AddedAt = At.AddDays(-2),
                Status = MediaFileStatus.NeedsAttention,
                AttentionReason = "ambiguous",
            },
            new MediaFile
            {
                Id = MissingFileId,
                LibraryId = LibraryId,
                Path = "/media/Movies/Gone (2001).mkv",
                SizeBytes = 500,
                Mtime = At.AddDays(-30),
                AddedAt = At.AddDays(-30),
                Status = MediaFileStatus.Missing,
                MissingSince = At.AddDays(-1),
            });

        db.Titles.AddRange(
            new Title
            {
                Id = MovieId,
                Kind = TitleKind.Movie,
                Name = "The Matrix",
                Year = 1999,
                Overview = "A hacker learns the truth.",
                Genres = ["Action", "Science Fiction"],
                Rating = 8.2,
                RuntimeMinutes = 136,
                ExternalIds = new TitleExternalIds { Tmdb = "movie/603", Imdb = "tt0133093" },
            },
            new Title
            {
                Id = ShowId,
                Kind = TitleKind.Show,
                Name = "Breaking Bad",
                Year = 2008,
                Genres = ["Drama"],
                ExternalIds = new TitleExternalIds { Tmdb = "tv/1396" },
            });
        db.Seasons.Add(new Season { Id = SeasonId, TitleId = ShowId, Number = 1, Name = "Season 1" });
        db.Episodes.Add(new Episode { Id = EpisodeId, SeasonId = SeasonId, Number = 1, Name = "Pilot", AirDate = new DateOnly(2008, 1, 20) });
        db.Playables.AddRange(Playable.ForMovie(MoviePlayableId, MovieId), Playable.ForEpisode(EpisodePlayableId, EpisodeId));
        db.FileLinks.AddRange(
            new FileLink { MediaFileId = MovieFileId, PlayableId = MoviePlayableId, Part = 1 },
            new FileLink { MediaFileId = MovieFile720Id, PlayableId = MoviePlayableId, Part = 1 },
            new FileLink { MediaFileId = EpisodeFileId, PlayableId = EpisodePlayableId, Part = 1 });
        db.Artwork.AddRange(
            new Artwork { Id = PosterId, TitleId = MovieId, Type = ArtworkType.Poster, Url = "https://image.tmdb.org/t/p/original/matrix.jpg", LocalPath = "artwork/poster.jpg", DominantColor = "#1A2B3C" },
            new Artwork { Id = StillId, EpisodeId = EpisodeId, Type = ArtworkType.Still, Url = "https://image.tmdb.org/t/p/original/pilot.jpg" });
        db.MatchReviews.Add(new MatchReview
        {
            Id = ReviewId,
            MediaFileId = AttentionFileId,
            Reason = "ambiguous",
            Candidates = [new MatchCandidate(ExternalId.Parse("tmdb:movie/1"), 0.7, TitleKind.Movie, "Unknown Thing", 2020)],
        });
        db.MatchOverrides.Add(new MatchOverride
        {
            Id = OverrideId,
            LibraryId = LibraryId,
            Path = MoviePath,
            TitleRef = ExternalId.Parse("tmdb:movie/603"),
            Kind = TitleKind.Movie,
            CreatedAt = At,
        });

        db.WatchStates.AddRange(
            new WatchState { ProfileId = OwnerProfileId, PlayableId = MoviePlayableId, PositionSeconds = 1234.5, DurationSeconds = 8160, LastWatchedAt = At },
            new WatchState { ProfileId = OwnerProfileId, PlayableId = EpisodePlayableId, DurationSeconds = 3480, Watched = true, LastWatchedAt = At.AddMinutes(-90) });
        db.VersionPreferences.Add(new VersionPreference
        {
            ProfileId = OwnerProfileId,
            TitleId = MovieId,
            MediaFileId = MovieFile720Id,
            Resolution = "720p",
            UpdatedAt = At,
        });
        db.AuditEvents.Add(new AuditEvent { Id = AuditId, UserId = OwnerId, Action = "setup.completed", Target = "server", At = At });
        db.Settings.AddRange(
            new Setting { Key = "server.mode", Value = "server" },
            new Setting { Key = TmdbTokenKey, Value = db.SecretProtector.Protect(TmdbToken) });

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Checks, through the current model, that everything <see cref="SeedAsync"/> wrote reads back the same.</summary>
    public static async Task AssertIntactAsync(MediaManDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var owner = await db.Users.AsNoTracking().SingleAsync(u => u.Id == OwnerId, cancellationToken);
        owner.Username.Should().Be(OwnerName);
        owner.PasswordHash.Should().Be(OwnerPasswordHash);
        owner.IsSuperadmin.Should().BeTrue();
        owner.RoleId.Should().Be(AdminRoleId);
        owner.TotpSecret.Should().Be(OwnerTotpSecret, "secrets still decrypt with the install's key");
        owner.CreatedAt.Should().Be(At);
        (await db.Users.AsNoTracking().SingleAsync(u => u.Id == ViewerId, cancellationToken)).Disabled.Should().BeTrue();
        (await db.Roles.AsNoTracking().SingleAsync(r => r.Id == AdminRoleId, cancellationToken)).Permissions
            .Should().Equal(Permissions.LibraryView, Permissions.ServerControl);

        var profile = await db.Profiles.AsNoTracking().SingleAsync(p => p.Id == OwnerProfileId, cancellationToken);
        profile.Name.Should().Be("Owner");
        profile.Avatar.Should().Be("purple");
        profile.AudioLangPrefs.Should().Equal("ja", "en");
        profile.SubtitleLangPrefs.Should().Equal("en");

        var session = await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == SessionId, cancellationToken);
        session.RefreshTokenHash.Should().Be(RefreshTokenHash);
        session.ActiveProfileId.Should().Be(OwnerProfileId);
        session.ExpiresAt.Should().Be(At.AddDays(30));
        (await db.Invites.AsNoTracking().SingleAsync(i => i.Id == InviteId, cancellationToken)).Uses.Should().Be(1);

        var library = await db.Libraries.AsNoTracking().SingleAsync(l => l.Id == LibraryId, cancellationToken);
        library.Kind.Should().Be(LibraryKind.External);
        library.RootPaths.Should().Equal("/media/Movies", "/media/Shows");
        library.ScanIntervalMinutes.Should().Be(60);
        library.LastScanFinishedAt.Should().Be(At);

        var movieFile = await db.MediaFiles.AsNoTracking().SingleAsync(f => f.Id == MovieFileId, cancellationToken);
        movieFile.Path.Should().Be(MoviePath);
        movieFile.SizeBytes.Should().Be(40_000_000_000);
        movieFile.Mtime.Should().Be(At.AddDays(-10));
        movieFile.AddedAt.Should().Be(At.AddDays(-9));
        movieFile.Status.Should().Be(MediaFileStatus.Matched);
        movieFile.Parsed!.Resolution.Should().Be(Resolution.P2160);
        movieFile.Probe!.Duration.Should().Be(TimeSpan.FromMinutes(136));
        movieFile.Probe.Streams.Should().HaveCount(2);
        var attention = await db.MediaFiles.AsNoTracking().SingleAsync(f => f.Id == AttentionFileId, cancellationToken);
        attention.Status.Should().Be(MediaFileStatus.NeedsAttention);
        attention.AttentionReason.Should().Be("ambiguous");
        (await db.MediaFiles.AsNoTracking().SingleAsync(f => f.Id == MissingFileId, cancellationToken)).MissingSince.Should().Be(At.AddDays(-1));

        var movie = await db.Titles.AsNoTracking().SingleAsync(t => t.Id == MovieId, cancellationToken);
        movie.Name.Should().Be("The Matrix");
        movie.Genres.Should().Equal("Action", "Science Fiction");
        movie.Rating.Should().Be(8.2);
        movie.ExternalIds.Tmdb.Should().Be("movie/603");
        movie.ExternalIds.Imdb.Should().Be("tt0133093");
        (await db.Episodes.AsNoTracking().SingleAsync(e => e.Id == EpisodeId, cancellationToken)).AirDate.Should().Be(new DateOnly(2008, 1, 20));
        (await db.Playables.AsNoTracking().SingleAsync(p => p.Id == EpisodePlayableId, cancellationToken)).EpisodeId.Should().Be(EpisodeId);
        (await db.FileLinks.AsNoTracking().CountAsync(l => l.PlayableId == MoviePlayableId, cancellationToken)).Should().Be(2);
        var poster = await db.Artwork.AsNoTracking().SingleAsync(a => a.Id == PosterId, cancellationToken);
        poster.TitleId.Should().Be(MovieId);
        poster.DominantColor.Should().Be("#1A2B3C");
        (await db.MatchReviews.AsNoTracking().SingleAsync(r => r.Id == ReviewId, cancellationToken)).Candidates
            .Should().ContainSingle().Which.TitleRef.Should().Be(ExternalId.Parse("tmdb:movie/1"));
        (await db.MatchOverrides.AsNoTracking().SingleAsync(o => o.Id == OverrideId, cancellationToken)).TitleRef
            .Should().Be(ExternalId.Parse("tmdb:movie/603"));

        var resume = await db.WatchStates.AsNoTracking()
            .SingleAsync(w => w.ProfileId == OwnerProfileId && w.PlayableId == MoviePlayableId, cancellationToken);
        resume.PositionSeconds.Should().Be(1234.5);
        resume.LastWatchedAt.Should().Be(At);
        (await db.WatchStates.AsNoTracking()
            .SingleAsync(w => w.ProfileId == OwnerProfileId && w.PlayableId == EpisodePlayableId, cancellationToken)).Watched.Should().BeTrue();
        (await db.VersionPreferences.AsNoTracking().SingleAsync(v => v.ProfileId == OwnerProfileId, cancellationToken)).MediaFileId
            .Should().Be(MovieFile720Id);
        (await db.AuditEvents.AsNoTracking().SingleAsync(a => a.Id == AuditId, cancellationToken)).Action.Should().Be("setup.completed");

        (await db.Settings.AsNoTracking().SingleAsync(s => s.Key == "server.mode", cancellationToken)).Value.Should().Be("server");
        var token = await db.Settings.AsNoTracking().SingleAsync(s => s.Key == TmdbTokenKey, cancellationToken);
        db.SecretProtector.Unprotect(token.Value).Should().Be(TmdbToken);
    }

    private static Guid Id(int n) => Guid.Parse($"0199d0c0-0000-7000-8000-{n:D12}");
}
