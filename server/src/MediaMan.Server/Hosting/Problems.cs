using Microsoft.AspNetCore.Http.HttpResults;

namespace MediaMan.Server.Hosting;

/// <summary>Fills in the contract's <c>Problem</c> extensions: a stable <c>code</c> and the user's next <c>action</c> (SPEC §11).</summary>
public static class Problems
{
    public const string CodeKey = "code";
    public const string ActionKey = "action";

    public static void Customize(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var problem = context.ProblemDetails;
        var status = problem.Status ?? context.HttpContext.Response.StatusCode;

        problem.Instance ??= context.HttpContext.Request.Path;
        problem.Extensions.TryAdd(CodeKey, CodeFor(status));
        problem.Extensions.TryAdd(ActionKey, ActionFor(status));
    }

    /// <summary>A problem response with a specific <paramref name="code"/> and <paramref name="action"/>.</summary>
    public static ProblemHttpResult Result(int status, string code, string title, string detail, string action) =>
        TypedResults.Problem(detail, statusCode: status, title: title, extensions: new Dictionary<string, object?>
        {
            [CodeKey] = code,
            [ActionKey] = action,
        });

    public static string CodeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "bad_request",
        StatusCodes.Status401Unauthorized => "unauthorized",
        StatusCodes.Status403Forbidden => "forbidden",
        StatusCodes.Status404NotFound => "not_found",
        StatusCodes.Status405MethodNotAllowed => "method_not_allowed",
        StatusCodes.Status409Conflict => "conflict",
        StatusCodes.Status429TooManyRequests => "rate_limited",
        >= 500 => "internal_error",
        _ => "request_failed",
    };

    public static string ActionFor(int status) => status switch
    {
        StatusCodes.Status401Unauthorized => "Sign in again.",
        StatusCodes.Status403Forbidden => "Ask an admin for access.",
        StatusCodes.Status404NotFound => "Check the address, or go back to the home screen.",
        StatusCodes.Status429TooManyRequests => "Wait a moment, then try again.",
        >= 500 => "Try again. If it keeps happening, check the server logs.",
        _ => "Check the request and try again.",
    };
}
