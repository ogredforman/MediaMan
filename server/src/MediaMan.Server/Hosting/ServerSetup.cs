using System.Text.Json.Serialization;
using MediaMan.Core;
using MediaMan.Data;
using MediaMan.Library.Importing;
using MediaMan.Library.Matching;
using MediaMan.Library.Scanning;
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
        services.AddSingleton<ISecretProtector, UnavailableSecretProtector>();

        services.AddDbContext<MediaManDbContext>((sp, builder) =>
        {
            var options = sp.GetRequiredService<IOptions<MediaManOptions>>().Value;
            Directory.CreateDirectory(options.DataDirectory);
            MediaManDb.Configure((DbContextOptionsBuilder<MediaManDbContext>)builder, options.DatabasePath);
        });

        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
        services.AddProblemDetails(o => o.CustomizeProblemDetails = Problems.Customize);
        services.AddScoped<LibraryQueries>();
        AddScanning(services);

        // Hosted services start in registration order: the schema exists before anything seeds it.
        services.AddHostedService<DatabaseMigrator>();
        AddDevConfiguration(services, section.Get<MediaManOptions>()?.Dev ?? new DevOptions(), environment);
        services.AddHostedService<ScanWorker>();
        return services;
    }

    /// <summary>
    /// The scan pipeline. Its providers are registered only by the dev configuration until Stage 2, so the pipeline is
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
