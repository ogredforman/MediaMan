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
using MediaMan.Metadata.Images;
using MediaMan.Metadata.Tmdb;
#if MEDIAMAN_DEV
using MediaMan.Server.Dev;
#endif
using MediaMan.Server.Endpoints;
using MediaMan.Server.Events;
using MediaMan.Server.LibraryApi;
using MediaMan.Server.Scanning;
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
        AddAccounts(services);

        services.AddDbContext<MediaManDbContext>((sp, builder) =>
        {
            var options = sp.GetRequiredService<IOptions<MediaManOptions>>().Value;
            Directory.CreateDirectory(options.DataDirectory);
            MediaManDb.Configure((DbContextOptionsBuilder<MediaManDbContext>)builder, options.DatabasePath);
        });

        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
        services.AddProblemDetails(o => o.CustomizeProblemDetails = Problems.Customize);
        services.AddScoped<LibraryQueries>();
        AddMetadata(services);
        AddScanning(services);

        // Hosted services start in registration order: the schema exists before anything seeds it.
        services.AddHostedService<DatabaseMigrator>();
        AddDevConfiguration(services, section.Get<MediaManOptions>()?.Dev ?? new DevOptions(), environment);
        services.AddHostedService<ScanWorker>();
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
        services.AddSingleton(PasswordHasher.Default);
        services.AddScoped<AccountService>();
    }

    /// <summary>
    /// TMDB is the metadata provider (SPEC §8 metadata [M]), and its artwork is cached locally. Each owns one long-lived
    /// HttpClient that recycles pooled connections, and logs nothing, so neither the token nor the requests reach the
    /// logs (SPEC §9).
    /// </summary>
    private static void AddMetadata(IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MediaManOptions>>().Value;
            var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) })
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            return new TmdbMetadataProvider(
                http,
                new TmdbOptions
                {
                    AccessToken = options.Metadata.Tmdb.AccessToken,
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
    /// The scan pipeline. The media probe is registered only by the dev configuration for now, so the pipeline is
    /// built through factories, which DI's build-time validation skips; <see cref="ScanCoordinator.IsAvailable"/> gates use.
    /// </summary>
    private static void AddScanning(IServiceCollection services)
    {
        services.AddSingleton(new MatcherOptions());
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<LibraryScanner>(sp));
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<LibraryMatcher>(sp));
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<MetadataImporter>(sp));
        services.AddSingleton<ScanCoordinator>();
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

    public static WebApplication MapMediaManEndpoints(this WebApplication app)
    {
        app.MapHealthEndpoints();
        app.MapEventEndpoints();
        app.MapLibraryEndpoints();
        app.MapArtworkEndpoints();
#if MEDIAMAN_DEV
        // Dev-only (SPEC §13): the player is registered only by the dev configuration, so elsewhere this route is a 404.
        if (app.Services.GetService<IDevPlayer>() is not null)
        {
            app.MapDevPlayEndpoints();
        }
#endif
        return app;
    }
}
