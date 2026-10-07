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

    public ToolsOptions Tools { get; set; } = new();
}

/// <summary>Where the external media tools live (SPEC §10.1). Bare names are looked up on PATH.</summary>
public sealed class ToolsOptions
{
    public string FfprobePath { get; set; } = "ffprobe";

    /// <summary>MPV, used only by the dev-only play endpoint (SPEC §13); the desktop app launches its own MPV.</summary>
    public string MpvPath { get; set; } = "mpv";
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

    /// <summary>The player behind the dev-only play endpoint; Real launches MPV on this host, Fake only records.</summary>
    public ProviderSource Player { get; set; } = ProviderSource.Real;
}
