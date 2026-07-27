using System.Reflection;
using Nofarma.Api.Composition;
using Nofarma.Application.Abstractions;
using Nofarma.Contracts.Diagnostics;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddNofarmaFoundation();

WebApplication app = builder.Build();

app.MapGet("/health/live", (IUtcClock clock) =>
{
    string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
    return Results.Ok(new HealthResponse(
        "Nofarma.Api",
        "Healthy",
        version,
        clock.GetCurrentInstant().Value));
});

app.Run();

public partial class Program
{
}
