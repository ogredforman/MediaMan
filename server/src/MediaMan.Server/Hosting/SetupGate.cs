using MediaMan.Data;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Server.Hosting;

/// <summary>
/// Whether first-run setup is complete: a Superadmin exists (SPEC §7.1). Once true it stays true, since the Superadmin
/// can't be deleted (SPEC §5), so it's cached for the life of the process.
/// </summary>
public sealed class SetupState
{
    private volatile bool _complete;

    /// <summary>Serializes setup attempts in this process, so concurrent wizards can't both pass the checks.</summary>
    public SemaphoreSlim Lock { get; } = new(1, 1);

    public async ValueTask<bool> IsCompleteAsync(MediaManDbContext db, CancellationToken cancellationToken)
    {
        if (!_complete && await db.Users.AsNoTracking().AnyAsync(u => u.IsSuperadmin, cancellationToken))
        {
            _complete = true;
        }

        return _complete;
    }

    public void MarkComplete() => _complete = true;
}

/// <summary>The route answers before setup is complete: health and setup only (SPEC §7.1 step 1).</summary>
public sealed record AllowedBeforeSetupMetadata;

/// <summary>Until setup is complete, refuses every route except health and setup with <c>423 setup.required</c>.</summary>
public sealed class SetupGate(RequestDelegate next, SetupState state)
{
    public async Task InvokeAsync(HttpContext http)
    {
        // Framework endpoints (e.g. 405) and unknown paths aren't routes; they run nothing of ours.
        if (http.GetEndpoint() is not RouteEndpoint endpoint
            || endpoint.Metadata.GetMetadata<AllowedBeforeSetupMetadata>() is not null
            || await state.IsCompleteAsync(http.RequestServices.GetRequiredService<MediaManDbContext>(), http.RequestAborted))
        {
            await next(http);
            return;
        }

        await Problems.Result(StatusCodes.Status423Locked, "setup.required", "MediaMan isn't set up yet.",
            "This server has no owner account yet, so nothing else is available until setup is done.",
            "Open MediaMan and finish the setup wizard.").ExecuteAsync(http);
    }
}

public static class SetupGateExtensions
{
    public static TBuilder AllowedBeforeSetup<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AllowedBeforeSetupMetadata());

    /// <summary>Runs after the local-network guard and before authentication.</summary>
    public static WebApplication UseSetupGate(this WebApplication app)
    {
        app.UseMiddleware<SetupGate>();
        return app;
    }
}
