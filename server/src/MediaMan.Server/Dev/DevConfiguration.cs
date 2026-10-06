using MediaMan.Core.Modules;
using MediaMan.Fakes;
using MediaMan.Server.Hosting;

namespace MediaMan.Server.Dev;

/// <summary>
/// Wires the dev configuration (SPEC §8 setup [P]): picks a fake or real implementation per dependency and
/// registers the configured media folder as an External library. Compiled into Debug builds only.
/// </summary>
public static class DevConfiguration
{
    public static IServiceCollection AddMediaManDev(this IServiceCollection services, DevOptions dev)
    {
        var providers = dev.Providers;
        AddProvider<IMetadataProvider>(services, providers.Metadata, "Metadata", "PLAN 2.1", () => new FakeMetadataProvider());
        AddProvider<IArtworkProvider>(services, providers.Artwork, "Artwork", "PLAN 2.2", () => new FakeArtworkProvider());
        AddProvider<IMediaProbe>(services, providers.MediaProbe, "MediaProbe", "PLAN 1.5", () => new FakeMediaProbe());
        AddProvider<IFileWatcher>(services, providers.FileWatcher, "FileWatcher", "PLAN 2.8", () => new FakeFileWatcher());

        services.AddHostedService<DevLibrarySeeder>();
        return services;
    }

    // Real implementations register here as their PLAN tasks land; until then choosing Real fails at startup.
    private static void AddProvider<TService>(
        IServiceCollection services, ProviderSource source, string name, string realArrivesIn, Func<TService> fake)
        where TService : class
    {
        if (source == ProviderSource.Real)
        {
            throw new InvalidOperationException(
                $"The real {name} implementation isn't available yet (it arrives in {realArrivesIn}). " +
                $"Set {MediaManOptions.SectionName}:Dev:Providers:{name} to Fake.");
        }

        services.AddSingleton(_ => fake());
    }
}
