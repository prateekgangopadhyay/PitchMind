using PitchMind.EventWorker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

var builder = Host.CreateApplicationBuilder(args);
var profile = builder.Configuration["Runtime:Profile"];
if (string.Equals(profile, "local", StringComparison.OrdinalIgnoreCase))
{
    if (!builder.Environment.IsDevelopment() || !builder.Configuration.GetValue("CandidateFeed:Enabled", true))
        throw new InvalidOperationException("LOCAL EventWorker requires Development and CandidateFeed:Enabled=true.");
    var postgres = builder.Configuration.GetConnectionString("PitchMind")
        ?? throw new InvalidOperationException("LOCAL candidate EventWorker requires ConnectionStrings:PitchMind.");
    var bootstrap = builder.Configuration["Kafka:BootstrapServers"] ?? "127.0.0.1:9092";
    if (!bootstrap.StartsWith("localhost:", StringComparison.Ordinal) && !bootstrap.StartsWith("127.0.0.1:", StringComparison.Ordinal))
        throw new InvalidOperationException("LOCAL candidate Kafka must use a loopback address.");
    builder.Services.AddSingleton(Npgsql.NpgsqlDataSource.Create(postgres));
    builder.Services.AddSingleton<PitchMind.Infrastructure.Feeds.CandidateFeedPersistence>();
    builder.Services.AddSingleton<PitchMind.Infrastructure.Feeds.CandidateKafkaConsumer>();
    builder.Services.AddHostedService<LocalCandidateKafkaConsumerWorker>();
}
else
{
    AzureEventWorkerConfiguration.ValidateRuntimeProfile(profile);
    AzureEventWorkerConfiguration.AddAzureOutboxDispatch(builder.Services, builder.Configuration);
    if (builder.Configuration.GetValue<bool>("CandidateFeed:Enabled"))
        AzureEventWorkerConfiguration.AddAzureCandidateFeedConsumer(builder.Services, builder.Configuration);
    if (AzureEventWorkerConfiguration.IsProjectionConsumerEnabled(builder.Configuration))
        AzureEventWorkerConfiguration.AddAzureProjectionConsumer(builder.Services, builder.Configuration);
    builder.Services.AddHostedService<Worker>();
}
await builder.Build().RunAsync();
