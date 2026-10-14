using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace MediaMan.Server.Hosting;

/// <summary>
/// Serves the built web UI (<c>web/dist</c>) under <see cref="RequestPath"/>, next to the API at the root (SPEC §5 one UI
/// everywhere, ADR 0009). Middleware, not endpoints: requests that match an API route never reach it, so every API route
/// keeps its permission, and the UI's own files are the only thing reachable signed out. The local-network guard still
/// runs first.
/// </summary>
public static partial class WebUi
{
    /// <summary>Where the UI lives; matches the web build's Vite <c>base</c> and the desktop app's <c>serverUiUrl</c>.</summary>
    public const string RequestPath = "/app";

    /// <summary>The folder beside the server that a distribution puts the built UI in.</summary>
    public const string FolderName = "web";

    public const string IndexFile = "index.html";

    /// <summary>
    /// The UI folder: <c>MediaMan:Web:RootPath</c> when set, else <see cref="FolderName"/> beside the server when it
    /// holds an <see cref="IndexFile"/>, else null (no UI is served: development and tests use the Vite dev server).
    /// </summary>
    public static string? Resolve(string? configured, string baseDirectory, Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var bundled = Path.Combine(baseDirectory, FolderName);
        return fileExists(Path.Combine(bundled, IndexFile)) ? bundled : null;
    }

    /// <summary>Runs after the local-network guard and CORS, before the setup gate and auth.</summary>
    public static WebApplication UseMediaManWebUi(this WebApplication app)
    {
        var configured = app.Services.GetRequiredService<IOptions<MediaManOptions>>().Value.Web.RootPath;
        var root = Resolve(configured, AppContext.BaseDirectory, File.Exists);
        if (root is null)
        {
            LogNoUi(app.Logger);
            return app;
        }

        if (!File.Exists(Path.Combine(root, IndexFile)))
        {
            LogMissingIndex(app.Logger, root);
            return app;
        }

        LogServing(app.Logger, root);
        var files = new PhysicalFileProvider(root);
        app.UseMiddleware<WebUiRedirects>();
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            RequestPath = RequestPath,
            OnPrepareResponse = context => SetHeaders(context.Context.Response, context.Context.Request.Path),
        });
        app.UseMiddleware<WebUiFallback>(files);
        return app;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "No built web UI beside the server; only the API is served")]
    private static partial void LogNoUi(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The web UI folder {WebRoot} has no index.html, so only the API is served; rebuild the web UI or fix MediaMan:Web:RootPath")]
    private static partial void LogMissingIndex(ILogger logger, string webRoot);

    [LoggerMessage(Level = LogLevel.Information, Message = "Serving the web UI from {WebRoot} at /app/")]
    private static partial void LogServing(ILogger logger, string webRoot);

    internal static bool IsUiRequest(HttpContext http) =>
        http.GetEndpoint() is null && (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method));

    /// <summary>Hashed build assets never change; everything else (index.html, icons) is revalidated.</summary>
    internal static void SetHeaders(HttpResponse response, PathString path)
    {
        response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        response.Headers[HeaderNames.CacheControl] = path.StartsWithSegments(RequestPath + "/assets", StringComparison.Ordinal)
            ? "public, max-age=31536000, immutable"
            : "no-cache";
    }
}

/// <summary>Sends <c>/</c> and <c>/app</c> to <c>/app/</c>, so the server's address opens the UI.</summary>
public sealed class WebUiRedirects(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http)
    {
        var path = http.Request.Path;
        if (WebUi.IsUiRequest(http) && (!path.HasValue || path.Value == "/" || path.Value == WebUi.RequestPath))
        {
            http.Response.Redirect(WebUi.RequestPath + "/" + http.Request.QueryString);
            return Task.CompletedTask;
        }

        return next(http);
    }
}

/// <summary>
/// The UI's own routes (<c>/app/titles/…</c>) fall back to its main page, so a reload or a shared link opens the app.
/// Only paths under <see cref="WebUi.RequestPath"/> without a file extension: a missing asset stays a 404, and nothing
/// outside the UI's path (API routes, unknown API paths) is ever answered with the page. So a UI route's last segment
/// must not contain a dot (today they're names and UUIDs).
/// </summary>
public sealed class WebUiFallback(RequestDelegate next, IFileProvider files)
{
    public async Task InvokeAsync(HttpContext http)
    {
        if (!WebUi.IsUiRequest(http)
            || !http.Request.Path.StartsWithSegments(WebUi.RequestPath, StringComparison.Ordinal, out var rest)
            || Path.HasExtension(rest.Value))
        {
            await next(http);
            return;
        }

        var index = files.GetFileInfo(WebUi.IndexFile);
        if (!index.Exists)
        {
            await next(http);
            return;
        }

        WebUi.SetHeaders(http.Response, http.Request.Path);
        http.Response.ContentType = "text/html; charset=utf-8";
        http.Response.ContentLength = index.Length;
        if (HttpMethods.IsHead(http.Request.Method))
        {
            return;
        }

        await http.Response.SendFileAsync(index, http.RequestAborted);
    }
}
