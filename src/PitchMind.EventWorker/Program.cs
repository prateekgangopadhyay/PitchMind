using PitchMind.EventWorker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
AzureEventWorkerConfiguration.ValidateRuntimeProfile(builder.Configuration["Runtime:Profile"]);
AzureEventWorkerConfiguration.AddAzureOutboxDispatch(builder.Services, builder.Configuration);
builder.Services.AddHostedService<Worker>();
await builder.Build().RunAsync();
