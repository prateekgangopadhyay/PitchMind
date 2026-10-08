using PitchMind.Application.Abstractions;
using PitchMind.Contracts.Health;
using PitchMind.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IMatchEventStore, InMemoryMatchEventStore>();
builder.Services.AddHealthChecks();
var app = builder.Build();
app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok", "pitchmind-api")));
app.MapHealthChecks("/health");
app.Run();

public partial class Program { }
