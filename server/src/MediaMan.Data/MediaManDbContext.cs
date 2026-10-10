using MediaMan.Core.Domain;
using MediaMan.Data.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MediaMan.Data;

/// <summary>The SQLite schema for SPEC §6.1. Storage conventions are in ADR 0003.</summary>
public class MediaManDbContext(DbContextOptions<MediaManDbContext> options, ISecretProtector secretProtector)
    : DbContext(options)
{
    public ISecretProtector SecretProtector { get; } =
        secretProtector ?? throw new ArgumentNullException(nameof(secretProtector));

    public DbSet<Library> Libraries => Set<Library>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<FileLink> FileLinks => Set<FileLink>();
    public DbSet<MatchReview> MatchReviews => Set<MatchReview>();
    public DbSet<MatchOverride> MatchOverrides => Set<MatchOverride>();

    public DbSet<Title> Titles => Set<Title>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<Playable> Playables => Set<Playable>();
    public DbSet<Artwork> Artwork => Set<Artwork>();

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<WatchState> WatchStates => Set<WatchState>();
    public DbSet<VersionPreference> VersionPreferences => Set<VersionPreference>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<Indexer> Indexers => Set<Indexer>();
    public DbSet<IndexerDefinition> IndexerDefinitions => Set<IndexerDefinition>();

    public DbSet<Download> Downloads => Set<Download>();
    public DbSet<DownloadFile> DownloadFiles => Set<DownloadFile>();
    public DbSet<DownloadRequest> DownloadRequests => Set<DownloadRequest>();
    public DbSet<SavedQualityProfile> SavedQualityProfiles => Set<SavedQualityProfile>();
    public DbSet<VpnConfig> VpnConfig => Set<VpnConfig>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        // The secret converters capture the protector, so the cached model must be per protector.
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, ProtectorModelCacheKeyFactory>();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Properties<Enum>().HaveConversion<string>();
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcUnixMillisecondsConverter>();
        configurationBuilder.Properties<TimeSpan>().HaveConversion<TimeSpanToTicksConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        LibraryModel.Configure(modelBuilder);
        CatalogModel.Configure(modelBuilder);
        AccountsModel.Configure(modelBuilder, SecretProtector);
        IndexingModel.Configure(modelBuilder);
        DownloadsModel.Configure(modelBuilder, SecretProtector);
    }

    private sealed class ProtectorModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) =>
            context is MediaManDbContext db
                ? (context.GetType(), db.SecretProtector, designTime)
                : (context.GetType(), designTime);
    }
}
