using System.Security.Cryptography;
using System.Text.Json.Serialization;
using MediaMan.Auth;
using MediaMan.Core;
using MediaMan.Core.Modules;
using MediaMan.Data;
using MediaMan.Library.Images;
using MediaMan.Library.Importing;
using MediaMan.Library.Matching;
using MediaMan.Library.Scanning;
using MediaMan.Library.Watching;
using MediaMan.Metadata.Images;
using MediaMan.Metadata.Tmdb;
using MediaMan.Playback.Probing;
#if MEDIAMAN_DEV
using MediaMan.Server.Dev;
#endif
using MediaMan.Server.Endpoints;
using MediaMan.Server.Events;
using MediaMan.Server.LibraryApi;
using MediaMan.Server.PlaybackApi;
using MediaMan.Server.Scanning;
using MediaMan.Server.Settings;
using MediaMan.Server.Setup;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaMan.Server.Hosting;

/// <summary>DI and pipeline wiring for the server host.</summary>
public static class ServerSetup
{
    public static IServiceCollection AddMediaManServer(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(MediaManOptions.SectionName);
        services.AddOptions<MediaManOptions>().Bind(section);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<EventBroadcaster>();
        services.AddMediaManBinding();
        AddAccounts(services);

        services.AddDbContext<MediaManDbContext>((sp, builder) =>
        {
            var options = sp.GetRequiredService<IOptions<MediaManOptions>>().Value;
            Directory.CreateDirectory(options.DataDirectory);
            MediaManDb.Configure((DbContextOptionsBuilder<MediaManDbContext>)builder, options.DatabasePath);
        });

        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
        services.AddProblemDetails(o => o.CustomizeProblemDetails = Problems.Customize);
        services.AddCors();
        services.AddSingleton<SetupState>();
        services.AddScoped<SetupService>();
        services.AddScoped<LibraryQueries>();
        services.AddScoped<HomeQueries>();
        services.AddScoped<LibraryService>();
        services.AddScoped<VersionPreferenceService>();
        services.AddScoped<PlaybackService>();
        services.AddScoped<ProgressService>();
        services.AddScoped<WatchedService>();
        services.AddScoped<MetadataSettingsService>();
        AddMetadata(services);
        AddScanning(services);

        // Hosted services start in registration order: the schema exists before anything seeds it.
        services.AddHostedService<DatabaseMigrator>();
        services.AddHostedService<MetadataSettingsLoader>();
        AddDevConfiguration(services, section.Get<MediaManOptions>()?.Dev ?? new DevOptions(), environment);
        services.AddHostedService<ScanWorker>();
        services.AddHostedService<ScanScheduler>();
        return services;
    }

    /// <summary>
    /// The per-install key is created on first run, when the first context needs the secret protector (ADR 0008).
    /// One protector per process: the EF model is cached per protector instance.
    /// </summary>
    private static void AddAccounts(IServiceCollection services)
    {
        services.AddSingleton(sp => InstallKey.LoadOrCreate(sp.GetRequiredService<IOptions<MediaManOptions>>().Value.KeyPath));
        services.AddSingleton<ISecretProtector>(sp =>
        {
            var key = sp.GetRequiredService<InstallKey>().Derive(InstallKey.SecretsAtRest);
            try
            {
                return new AesGcmSecretProtector(key);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        });
        services.AddSingleton(sp =>
        {
            var key = sp.GetRequiredService<InstallKey>().Derive(InstallKey.AccessTokens);
            try
            {
                return new AccessTokens(key, sp.GetRequiredService<IClock>());
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        });
        services.AddSingleton(sp =>
        {
            var key = sp.GetRequiredService<InstallKey>().Derive(InstallKey.ArtworkUrls);
            try
            {
                return new UrlSigner(key, sp.GetRequiredService<IClock>());
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        });
        services.AddSingleton(PasswordHasher.Default);
        services.AddScoped<AccountService>();
        services.AddScoped<SessionService>();
        services.AddScoped<ProfileService>();

        // Bearer tokens are validated exactly as AccessTokens validates them, times included (IClock). No authority is
        // set, so nothing is fetched. PermissionEnforcement turns the result into 401s and 403s.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<AccessTokens>((o, tokens) =>
        {
            o.TokenValidationParameters = tokens.ValidationParameters;
            o.MapInboundClaims = false;
        });
    }

    /// <summary>
    /// TMDB is the metadata provider (SPEC §8 metadata [M]), and its artwork is cached locally. Each owns one long-lived
    /// HttpClient that recycles pooled connections, and logs nothing, so neither the token nor the requests reach the
    /// logs (SPEC §9).
    /// </summary>
    private static void AddMetadata(IServiceCollection services)
    {
        services.AddSingleton<TmdbTokenOverride>();
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MediaManOptions>>().Value;
            var tokenOverride = sp.GetRequiredService<TmdbTokenOverride>();
            var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) })
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            return new TmdbMetadataProvider(
                http,
                new TmdbOptions
                {
                    AccessToken = options.Metadata.Tmdb.AccessToken,
                    AccessTokenOverride = () => tokenOverride.Token,
                    Language = options.Metadata.Tmdb.Language,
                    CacheDirectory = Path.Combine(options.DataDirectory, "cache", "tmdb"),
                },
                sp.GetRequiredService<IClock>());
        });
        services.AddSingleton<IMetadataProvider>(sp => sp.GetRequiredService<TmdbMetadataProvider>());

        // Artwork comes from the URLs the provider gave; the cache lives next to the database (SPEC §9 data portability).
        services.AddSingleton(_ => new HttpImageDownloader(
            new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) }) { Timeout = TimeSpan.FromSeconds(30) }));
        services.AddSingleton<IImageDownloader>(sp => sp.GetRequiredService<HttpImageDownloader>());
        services.AddSingleton(sp => new ArtworkCacheOptions
        {
            Directory = Path.Combine(sp.GetRequiredService<IOptions<MediaManOptions>>().Value.DataDirectory, "cache", "artwork"),
        });
        services.AddScoped<ArtworkCache>();
    }

    /// <summary>
    /// The scan pipeline. The media probe runs ffprobe as a separate process from <c>MediaMan:Tools:FfprobePath</c>
    /// (SPEC §10.1). The pipeline is built through factories, which DI's build-time validation skips, so a host without a
    /// probe or metadata provider still starts; <see cref="ScanCoordinator.IsAvailable"/> gates use.
    /// </summary>
    private static void AddScanning(IServiceCollection services)
    {
        services.AddSingleton(sp => new FfprobeMediaProbe(new FfprobeOptions
        {
            ExecutablePath = sp.GetRequiredService<IOptions<MediaManOptions>>().Value.Tools.FfprobePath,
        }));
        services.AddSingleton<IMediaProbe>(sp => sp.GetRequiredService<FfprobeMediaProbe>());
        services.AddSingleton(new MatcherOptions());
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<LibraryScanner>(sp));
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<LibraryMatcher>(sp));
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<MetadataImporter>(sp));
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<MatchFixer>(sp));
        services.AddSingleton<ScanCoordinator>();
        services.AddSingleton<IFileWatcher, FileSystemFileWatcher>();
    }

    /// <summary>
    /// The dev configuration (SPEC §8 setup [P]) is on in the Development environment when a dev media path is set.
    /// Release builds don't contain the fakes, so they refuse to start with it rather than silently ignore it.
    /// </summary>
    private static void AddDevConfiguration(IServiceCollection services, DevOptions dev, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() || string.IsNullOrWhiteSpace(dev.MediaPath))
        {
            return;
        }

#if MEDIAMAN_DEV
        services.AddMediaManDev(dev);
#else
        throw new InvalidOperationException(
            "The dev configuration needs a Debug build of the server. " +
            $"Build with -c Debug, or clear {MediaManOptions.SectionName}:Dev:MediaPath.");
#endif
    }

    /// <summary>Turns unhandled exceptions and bare error status codes into <c>Problem</c> responses.</summary>
    public static WebApplication UseMediaManErrors(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }

    /// <summary>
    /// Lets the configured UI origins (<see cref="MediaManOptions.AllowedOrigins"/>) call the API; runs before the
    /// setup gate and auth so preflights are answered and their <c>401</c>/<c>423</c> responses stay readable.
    /// </summary>
    public static WebApplication UseMediaManCors(this WebApplication app)
    {
        var origins = app.Services.GetRequiredService<IOptions<MediaManOptions>>().Value.AllowedOrigins
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim().TrimEnd('/'))
            .ToArray();
        if (origins.Length == 0)
        {
            return app;
        }

        app.UseCors(policy => policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod());
        return app;
    }

    /// <summary>Reads the bearer token, then enforces each route's declared permission (SPEC §5, §9).</summary>
    public static WebApplication UseMediaManAuth(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseMiddleware<PermissionEnforcement>();
        return app;
    }

    public static WebApplication MapMediaManEndpoints(this WebApplication app)
    {
        app.MapHealthEndpoints();
        app.MapSetupEndpoints();
        app.MapSettingsEndpoints();
        app.MapAuthEndpoints();
        app.MapAccountEndpoints();
        app.MapProfileEndpoints();
        app.MapEventEndpoints();
        app.MapLibraryEndpoints();
        app.MapHomeEndpoints();
        app.MapMatchEndpoints();
        app.MapVersionPreferenceEndpoints();
        app.MapArtworkEndpoints();
        app.MapPlaybackEndpoints();
        app.MapWatchedEndpoints();
        return app;
    }
}
