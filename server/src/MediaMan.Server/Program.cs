using MediaMan.Server.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMediaManServer(builder.Configuration);

var app = builder.Build();
app.UseMediaManErrors();
app.MapMediaManEndpoints();
app.Run();

/// <summary>Entry point; public so integration tests can host the server in-process.</summary>
public partial class Program;
