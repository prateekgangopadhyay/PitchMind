using Azure.Identity;
using PitchMind.EventWorker;
using PitchMind.Infrastructure.Storage;
using Xunit;

namespace PitchMind.Infrastructure.Tests;

public sealed class AzureCandidateAdapterConfigurationTests
{
    [Fact]
    public void AzurePostgresDataSourceRequiresAzureHost()
    {
        var credential = new ManagedIdentityCredential(Azure.Identity.ManagedIdentityId.SystemAssigned);
        Assert.Throws<InvalidOperationException>(() => AzurePostgresDataSourceFactory.Create(
            "Host=localhost;Database=pitchmind;Username=app", credential));
    }

    [Fact]
    public void AzurePostgresDataSourceAcceptsUserPasswordAuthentication()
    {
        using var source = AzurePostgresDataSourceFactory.Create(
            "Host=pm.postgres.database.azure.com;Database=pitchmind;Username=app;Password=secret");
        Assert.NotNull(source);
    }

    [Fact]
    public void AzurePostgresDataSourceAcceptsPasswordlessFlexibleServerConnection()
    {
        var credential = new ManagedIdentityCredential(Azure.Identity.ManagedIdentityId.SystemAssigned);
        using var source = AzurePostgresDataSourceFactory.Create(
            "Host=pm.postgres.database.azure.com;Database=pitchmind;Username=app", credential);
        Assert.NotNull(source);
    }

    [Fact]
    public void AzureRedisAdapterRejectsCredentialBearingOrNonEndpointSettings()
    {
        var credential = new ManagedIdentityCredential(Azure.Identity.ManagedIdentityId.SystemAssigned);
        Assert.Throws<InvalidOperationException>(() => new AzureEntraRedisProjectionCache("https://redis.example:10000", credential));
        Assert.Throws<InvalidOperationException>(() => new AzureEntraRedisProjectionCache("redis.example:10000,password=secret", credential));
    }

    [Fact]
    public void AzureEventHubNamespaceRequiresTheAzureServiceBusHost()
    {
        Assert.Equal("pitchmind.servicebus.windows.net",
            AzureEventWorkerConfiguration.ValidateEventHubNamespace("PitchMind.servicebus.windows.net"));
        Assert.Throws<InvalidOperationException>(() => AzureEventWorkerConfiguration.ValidateEventHubNamespace("localhost:9092"));
    }
}
