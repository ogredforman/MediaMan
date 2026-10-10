using MediaMan.Server.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMediaManServer(builder.Configuration, builder.Environment);

var app = builder.Build();
app.UseMediaManErrors();
app.UseLocalNetworkGuard();
app.UseMediaManCors();
app.UseSetupGate();
app.UseMediaManAuth();
app.MapMediaManEndpoints();
app.Run();

/// <summary>Entry point; public so integration tests can host the server in-process.</summary>
public partial class Program;
