using MediaMan.Data;
using MediaMan.Server.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMediaManServer(builder.Configuration, builder.Environment);

var app = builder.Build();
app.UseMediaManErrors();
app.UseLocalNetworkGuard();
app.UseMediaManCors();
app.UseMediaManWebUi();
app.UseSetupGate();
app.UseMediaManAuth();
app.MapMediaManEndpoints();
try
{
    app.Run();
}
catch (DatabaseUpgradeException ex)
{
    // The database is as it was before the update; say so and stop instead of running on it (SPEC §9 resilience).
    DatabaseMigrator.ReportFatal(ex, Console.Error);
}

/// <summary>Entry point; public so integration tests can host the server in-process.</summary>
public partial class Program;
