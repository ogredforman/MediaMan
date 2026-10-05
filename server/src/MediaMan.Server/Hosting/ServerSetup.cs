using MediaMan.Core;
using MediaMan.Data;
using MediaMan.Server.Endpoints;
using MediaMan.Server.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaMan.Server.Hosting;

/// <summary>DI and pipeline wiring for the server host.</summary>
public static class ServerSetup
{
    public static IServiceCollection AddMediaManServer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MediaManOptions>().Bind(configuration.GetSection(MediaManOptions.SectionName));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<EventBroadcaster>();

        services.AddDbContext<MediaManDbContext>((sp, builder) =>
        {
            var options = sp.GetRequiredService<IOptions<MediaManOptions>>().Value;
            Directory.CreateDirectory(options.DataDirectory);
            MediaManDb.Configure((DbContextOptionsBuilder<MediaManDbContext>)builder, options.DatabasePath);
        });

        services.AddProblemDetails(o => o.CustomizeProblemDetails = Problems.Customize);
        return services;
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
        return app;
    }
}
