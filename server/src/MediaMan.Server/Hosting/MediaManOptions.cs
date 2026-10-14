using MediaMan.Core.Domain;

namespace MediaMan.Server.Hosting;

/// <summary>Server configuration, bound from the <c>MediaMan</c> section.</summary>
public sealed class MediaManOptions
{
    public const string SectionName = "MediaMan";
    public const string DatabaseFileName = "mediaman.db";
    public const string KeyFileName = "mediaman.key";

    /// <summary>Holds the database, the per-install key file and the artwork cache (SPEC §9 data portability).</summary>
    public string DataDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaMan");

    public string DatabasePath => Path.Combine(DataDirectory, DatabaseFileName);

    /// <summary>The per-install key (ADR 0008). Back it up with the database: without it, stored secrets are unreadable.</summary>
    public string KeyPath => Path.Combine(DataDirectory, KeyFileName);

    /// <summary>Copies of the database taken before an upgrade changes its schema (SPEC §9 resilience).</summary>
    public string BackupDirectory => Path.Combine(DataDirectory, "backups");

    /// <summary>Dev configuration (SPEC §8 setup [P]); honoured only in the Development environment of a Debug build.</summary>
    public DevOptions Dev { get; set; } = new();

    public ServerOptions Server { get; set; } = new();

    public ToolsOptions Tools { get; set; } = new();

    public MetadataOptions Metadata { get; set; } = new();

    public WebUiOptions Web { get; set; } = new();

    /// <summary>
    /// Web UI origins on another origin than the server (the desktop app's UI, the Vite dev server) that may call the
    /// API from a browser (CORS). Empty allows none; bearer tokens, not cookies, so credentials are never allowed.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];
}

/// <summary>How the server listens (SPEC §7.1 step 3); <see cref="ServerBinding"/> applies it.</summary>
public sealed class ServerOptions
{
    /// <summary>The default port; the SPEC doesn't fix one.</summary>
    public const int DefaultPort = 8420;

    /// <summary>Local listens on loopback only, Server on the local network. Setup records the choice (PLAN 2.4b).</summary>
    public ServerMode Mode { get; set; } = ServerMode.Local;

    public int Port { get; set; } = DefaultPort;
}

/// <summary>Metadata provider settings (SPEC §8 metadata [M]); Settings → Metadata edits them in PLAN 2.22.</summary>
public sealed class MetadataOptions
{
    public TmdbSettings Tmdb { get; set; } = new();
}

public sealed class TmdbSettings
{
    /// <summary>A TMDB v4 read-access token overriding the shipped one. A secret: never logged (SPEC §9).</summary>
    public string? AccessToken { get; set; }

    public string Language { get; set; } = "en-US";
}

/// <summary>Where the external media tools live (SPEC §10.1). Bare names are looked up on PATH.</summary>
public sealed class ToolsOptions
{
    /// <summary>Overrides the bundled ffprobe; unset means the bundled copy, else <c>ffprobe</c> on PATH (<see cref="BundledTools"/>).</summary>
    public string? FfprobePath { get; set; }
}

/// <summary>The built web UI the server serves (<see cref="WebUi"/>, ADR 0009).</summary>
public sealed class WebUiOptions
{
    /// <summary>Overrides the UI folder; unset means <c>web</c> beside the server when present, else no UI.</summary>
    public string? RootPath { get; set; }
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

    /// <summary>
    /// Treats requests without a token as the dev Superadmin, so the dev configuration runs without signing in.
    /// Off unless set; it has no effect outside the dev configuration.
    /// </summary>
    public bool AutoSignIn { get; set; }

    /// <summary>
    /// Seeds the dev Superadmin and the media folder's library on startup. Off, the server starts un-set-up and
    /// answers <c>423 setup.required</c> until the setup wizard runs (the web e2e run tests setup this way).
    /// </summary>
    public bool Seed { get; set; } = true;

    public DevProviders Providers { get; set; } = new();
}

/// <summary>Fake or real implementation per SPEC §10.3 dependency.</summary>
public sealed class DevProviders
{
    public ProviderSource Metadata { get; set; } = ProviderSource.Fake;
    public ProviderSource Artwork { get; set; } = ProviderSource.Fake;

    /// <summary>Fetches artwork images; Fake serves generated solid-color images with no network.</summary>
    public ProviderSource Images { get; set; } = ProviderSource.Fake;

    public ProviderSource MediaProbe { get; set; } = ProviderSource.Fake;
    public ProviderSource FileWatcher { get; set; } = ProviderSource.Fake;
}
