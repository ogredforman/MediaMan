using System.Security.Cryptography;
using MediaMan.Core;
using MediaMan.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Data.Tests;

public class SchemaTests
{
    private static readonly DateTimeOffset _at = new(2026, 10, 4, 12, 30, 15, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Library_graph_round_trips()
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);
        var library = new Library { Id = Guid.NewGuid(), Name = "Shows", Kind = LibraryKind.External, ContentType = ContentType.Shows, RootPaths = ["/media/shows"], ScanIntervalMinutes = 60 };
        var title = new Title { Id = Guid.NewGuid(), Kind = TitleKind.Show, Name = "Example", Year = 2020, Genres = ["Drama"], ExternalIds = { Tmdb = "1399" } };
        var season = new Season { Id = Guid.NewGuid(), TitleId = title.Id, Number = 1 };
        var episode = new Episode { Id = Guid.NewGuid(), SeasonId = season.Id, Number = 1, AirDate = new DateOnly(2020, 1, 1) };
        var playable = Playable.ForEpisode(Guid.NewGuid(), episode.Id);
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            LibraryId = library.Id,
            Path = Path.Combine("media", "shows", "Example.S01E01.mkv"),
            SizeBytes = 1_000_000,
            Mtime = _at,
            Status = MediaFileStatus.Matched,
            Parsed = new ParsedRelease { Title = "Example", Season = 1, Episodes = [1], Resolution = Resolution.P1080 },
            Probe = new ProbeResult { Container = "matroska", Duration = TimeSpan.FromMinutes(42), Streams = [new StreamInfo { Index = 0, Type = MediaStreamType.Video, Codec = "hevc" }] },
        };

        await using (var write = db.CreateContext())
        {
            write.AddRange(library, title, season, episode, playable, file);
            write.Add(new FileLink { MediaFileId = file.Id, PlayableId = playable.Id, Part = 0 });
            write.Add(new MatchReview { Id = Guid.NewGuid(), MediaFileId = file.Id, Reason = "low score", Candidates = [new MatchCandidate(ExternalId.Parse("tmdb:1399"), 0.4)] });
            await write.SaveChangesAsync(Ct);
        }

        await using var read = db.CreateContext();
        var storedFile = await read.MediaFiles.SingleAsync(Ct);
        storedFile.Should().BeEquivalentTo(file);
        (await read.Titles.SingleAsync(t => t.ExternalIds.Tmdb == "1399", Ct)).Genres.Should().Equal("Drama");
        (await read.Episodes.SingleAsync(Ct)).AirDate.Should().Be(new DateOnly(2020, 1, 1));
        (await read.MatchReviews.SingleAsync(Ct)).Candidates.Should().Equal(new MatchCandidate(ExternalId.Parse("tmdb:1399"), 0.4));
        (await read.FileLinks.SingleAsync(Ct)).PlayableId.Should().Be(playable.Id);
    }

    [Fact]
    public async Task Downloads_and_requests_round_trip()
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);
        var role = new Role { Id = Guid.NewGuid(), Name = "Admin", Permissions = ["downloads.manage"] };
        var user = new User { Id = Guid.NewGuid(), Username = "owner", PasswordHash = "hash", RoleId = role.Id, CreatedAt = _at };
        var download = new Download { Id = Guid.NewGuid(), InfoHash = InfoHash.Parse(new string('a', 40)), Name = "Example", SavePath = Path.Combine("managed", "downloads"), RequestedBy = user.Id, State = DownloadState.Seeding, SeedTimeLimit = TimeSpan.FromHours(12) };
        var filter = new QualityFilter { Resolutions = [Resolution.P2160], DynamicRanges = [DynamicRange.DolbyVision] };
        var request = new DownloadRequest
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TitleRef = ExternalId.Parse("tmdb:1399"),
            Filter = filter,
            Release = new ReleaseResult { IndexerId = Guid.NewGuid().ToString("D"), Title = "Example.2160p", MagnetUri = "magnet:?xt=urn:btih:" + new string('a', 40), Parsed = new ParsedRelease { Title = "Example" }, Quality = new ReleaseQuality { DynamicRange = DynamicRange.Hdr } },
        };

        await using (var write = db.CreateContext())
        {
            write.AddRange(role, user, download, request);
            write.Add(new DownloadFile { DownloadId = download.Id, Index = 0, Path = "Example.mkv", SizeBytes = 10, Priority = FilePriority.Normal });
            write.Add(new SavedQualityProfile { Id = Guid.NewGuid(), UserId = user.Id, Name = "4K", Filter = filter });
            await write.SaveChangesAsync(Ct);
        }

        await using var read = db.CreateContext();
        (await read.Downloads.SingleAsync(d => d.InfoHash == download.InfoHash, Ct)).Should().BeEquivalentTo(download);
        (await read.DownloadRequests.SingleAsync(Ct)).Should().BeEquivalentTo(request);
        (await read.SavedQualityProfiles.SingleAsync(Ct)).Filter.Should().BeEquivalentTo(filter);
        (await read.Users.SingleAsync(Ct)).CreatedAt.Should().Be(_at);
    }

    [Fact]
    public async Task Playable_kind_must_match_its_owner()
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);
        await using var context = db.CreateContext();
        var title = new Title { Id = Guid.NewGuid(), Kind = TitleKind.Movie, Name = "Film" };
        context.AddRange(title, new Playable { Id = Guid.NewGuid(), Kind = PlayableKind.Episode, TitleId = title.Id });

        var act = () => context.SaveChangesAsync(Ct);

        (await act.Should().ThrowAsync<DbUpdateException>()).WithInnerException<Microsoft.Data.Sqlite.SqliteException>()
            .WithMessage("*CK_Playables_KindMatchesOwner*");
    }

    [Fact]
    public async Task Artwork_must_have_exactly_one_owner()
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);
        await using var context = db.CreateContext();
        var title = new Title { Id = Guid.NewGuid(), Kind = TitleKind.Show, Name = "Show" };
        var season = new Season { Id = Guid.NewGuid(), TitleId = title.Id, Number = 1 };
        context.AddRange(title, season, new Artwork { Id = Guid.NewGuid(), TitleId = title.Id, SeasonId = season.Id, Type = ArtworkType.Poster, Url = "https://image/1" });

        var act = () => context.SaveChangesAsync(Ct);

        (await act.Should().ThrowAsync<DbUpdateException>()).WithInnerException<Microsoft.Data.Sqlite.SqliteException>()
            .WithMessage("*CK_Artwork_ExactlyOneOwner*");
    }

    [Fact]
    public async Task Only_one_superadmin_can_exist()
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);
        await using var context = db.CreateContext();
        var role = new Role { Id = Guid.NewGuid(), Name = "Admin" };
        context.AddRange(
            role,
            new User { Id = Guid.NewGuid(), Username = "first", PasswordHash = "h", RoleId = role.Id, IsSuperadmin = true },
            new User { Id = Guid.NewGuid(), Username = "second", PasswordHash = "h", RoleId = role.Id, IsSuperadmin = true });

        var act = () => context.SaveChangesAsync(Ct);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Usernames_are_unique_ignoring_case()
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);
        await using var context = db.CreateContext();
        var role = new Role { Id = Guid.NewGuid(), Name = "User" };
        context.AddRange(
            role,
            new User { Id = Guid.NewGuid(), Username = "alex", PasswordHash = "h", RoleId = role.Id },
            new User { Id = Guid.NewGuid(), Username = "ALEX", PasswordHash = "h", RoleId = role.Id });

        var act = () => context.SaveChangesAsync(Ct);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Deleting_a_library_removes_its_files()
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);
        await using (var write = db.CreateContext())
        {
            var library = new Library { Id = Guid.NewGuid(), Name = "Movies" };
            write.AddRange(library, new MediaFile { Id = Guid.NewGuid(), LibraryId = library.Id, Path = "a.mkv" });
            await write.SaveChangesAsync(Ct);
        }

        await using var context = db.CreateContext();
        await context.Libraries.ExecuteDeleteAsync(Ct);

        (await context.MediaFiles.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Secrets_are_encrypted_at_rest()
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);
        var role = new Role { Id = Guid.NewGuid(), Name = "Admin" };
        await using (var write = db.CreateContext())
        {
            write.AddRange(role, new User { Id = Guid.NewGuid(), Username = "owner", PasswordHash = "h", RoleId = role.Id, TotpSecret = "JBSWY3DPEHPK3PXP" });
            write.Add(new VpnConfig { Mode = VpnMode.Socks5, SocksHost = "proxy", SocksPort = 1080, SocksUsername = "vpn-user", SocksPassword = "hunter2" });
            await write.SaveChangesAsync(Ct);
        }

        using (var connection = db.OpenRawConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """SELECT "TotpSecret" FROM "Users" UNION ALL SELECT "SocksUsername" FROM "VpnConfig" UNION ALL SELECT "SocksPassword" FROM "VpnConfig";""";
            using var reader = await command.ExecuteReaderAsync(Ct);
            var stored = new List<string>();
            while (await reader.ReadAsync(Ct))
            {
                stored.Add(reader.GetString(0));
            }

            stored.Should().HaveCount(3).And.OnlyContain(s => s.StartsWith("v1:", StringComparison.Ordinal));
            string.Concat(stored).Should().NotContainAny("JBSWY3DPEHPK3PXP", "vpn-user", "hunter2");
        }

        await using var read = db.CreateContext();
        (await read.Users.SingleAsync(Ct)).TotpSecret.Should().Be("JBSWY3DPEHPK3PXP");
        var vpn = await read.VpnConfig.SingleAsync(Ct);
        vpn.SocksUsername.Should().Be("vpn-user");
        vpn.SocksPassword.Should().Be("hunter2");
    }

    [Fact]
    public async Task Secrets_cannot_be_read_with_another_key()
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);
        var role = new Role { Id = Guid.NewGuid(), Name = "Admin" };
        await using (var write = db.CreateContext())
        {
            write.AddRange(role, new User { Id = Guid.NewGuid(), Username = "owner", PasswordHash = "h", RoleId = role.Id, TotpSecret = "JBSWY3DPEHPK3PXP" });
            await write.SaveChangesAsync(Ct);
        }

        await using var read = db.CreateContext(new AesGcmSecretProtector(new byte[AesGcmSecretProtector.KeySizeBytes]));
        var act = () => read.Users.SingleAsync(Ct);

        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task Vpn_config_is_a_single_row()
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);
        await using (var write = db.CreateContext())
        {
            write.Add(new VpnConfig { Mode = VpnMode.Interface, InterfaceId = "wg0" });
            await write.SaveChangesAsync(Ct);
        }

        await using var context = db.CreateContext();
        context.Add(new VpnConfig { Mode = VpnMode.NoneAccepted });
        var act = () => context.SaveChangesAsync(Ct);

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
