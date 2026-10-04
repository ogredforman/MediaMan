// Host wiring (DI, config, endpoints, SSE) arrives in PLAN 0.4.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();
