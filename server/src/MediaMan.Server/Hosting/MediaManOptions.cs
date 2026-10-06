using MediaMan.Core.Domain;

namespace MediaMan.Server.Hosting;

/// <summary>Server configuration, bound from the <c>MediaMan</c> section.</summary>
public sealed class MediaManOptions
{
    public const string SectionName = "MediaMan";
    public const string DatabaseFileName = "mediaman.db";

    /// <summary>Holds the database, the per-install key file and the artwork cache (SPEC §9 data portability).</summary>
    public string DataDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaMan");

    public string DatabasePath => Path.Combine(DataDirectory, DatabaseFileName);

    /// <summary>Dev configuration (SPEC §8 setup [P]); honoured only in the Development environment of a Debug build.</summary>
    public DevOptions Dev { get; set; } = new();
}

/// <summary>Where an external dependency comes from in the dev configuration.</summary>
public enum ProviderSource
{
    Fake,
    Real,
}

/// <summary>The dev configuration: one media folder registered as an External library, with no setup wizard.</summary>
public sealed class DevOptions
{
    /// <summary>The media folder to register; the dev configuration is off while this is empty.</summary>
    public string? MediaPath { get; set; }

    /// <summary>Library name; defaults to the media folder's name.</summary>
    public string? LibraryName { get; set; }

    public ContentType ContentType { get; set; } = ContentType.Mixed;

    public DevProviders Providers { get; set; } = new();
}

/// <summary>Fake or real implementation per SPEC §10.3 dependency.</summary>
public sealed class DevProviders
{
    public ProviderSource Metadata { get; set; } = ProviderSource.Fake;
    public ProviderSource Artwork { get; set; } = ProviderSource.Fake;
    public ProviderSource MediaProbe { get; set; } = ProviderSource.Fake;
    public ProviderSource FileWatcher { get; set; } = ProviderSource.Fake;
}
