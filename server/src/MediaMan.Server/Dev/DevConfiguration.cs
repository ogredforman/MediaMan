using MediaMan.Core.Modules;
using MediaMan.Fakes;
using MediaMan.Library.Watching;
using MediaMan.Metadata.Images;
using MediaMan.Metadata.Tmdb;
using MediaMan.Playback.Probing;
using MediaMan.Server.Hosting;

namespace MediaMan.Server.Dev;

/// <summary>
/// Wires the dev configuration (SPEC §8 setup [P]): picks a fake or real implementation per dependency and
/// registers the configured media folder as an External library and a dev Superadmin. Compiled into Debug builds only.
/// </summary>
public static class DevConfiguration
{
    public static IServiceCollection AddMediaManDev(this IServiceCollection services, DevOptions dev)
    {
        var providers = dev.Providers;
        AddProvider<IMetadataProvider>(services, providers.Metadata, "Metadata", "PLAN 2.1", () => new FakeMetadataProvider(),
            sp => sp.GetRequiredService<TmdbMetadataProvider>());
        AddProvider<IArtworkProvider>(services, providers.Artwork, "Artwork", "a later stage", () => new FakeArtworkProvider());
        AddProvider<IImageDownloader>(services, providers.Images, "Images", "PLAN 2.2", () => new FakeImageDownloader(),
            sp => sp.GetRequiredService<HttpImageDownloader>());
        AddProvider<IMediaProbe>(services, providers.MediaProbe, "MediaProbe", "PLAN 1.5", () => new FakeMediaProbe(),
            sp => sp.GetRequiredService<FfprobeMediaProbe>());
        AddProvider<IFileWatcher>(services, providers.FileWatcher, "FileWatcher", "PLAN 2.8", () => new FakeFileWatcher(),
            _ => new FileSystemFileWatcher());

        services.AddSingleton(new DevAccount(dev.AutoSignIn));
        if (dev.Seed)
        {
            services.AddHostedService<DevAccountSeeder>();
            services.AddHostedService<DevLibrarySeeder>();
        }
        return services;
    }

    // Real implementations register here as their PLAN tasks land; until then choosing Real fails at startup.
    private static void AddProvider<TService>(
        IServiceCollection services,
        ProviderSource source,
        string name,
        string realArrivesIn,
        Func<TService> fake,
        Func<IServiceProvider, TService>? real = null)
        where TService : class
    {
        if (source == ProviderSource.Real && real is not null)
        {
            services.AddSingleton(real);
            return;
        }

        if (source == ProviderSource.Real)
        {
            throw new InvalidOperationException(
                $"The real {name} implementation isn't available yet (it arrives in {realArrivesIn}). " +
                $"Set {MediaManOptions.SectionName}:Dev:Providers:{name} to Fake.");
        }

        services.AddSingleton(_ => fake());
    }
}
