using PitchMind.Api;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class LocalPostgresConnectionPolicyTests
{
    [Theory]
    [InlineData("Host=127.0.0.1;Port=5432;Database=pitchmind")]
    [InlineData("Host=localhost;Port=5432;Database=pitchmind")]
    [InlineData("Host=::1;Port=5432;Database=pitchmind")]
    public void Compose_loopback_endpoints_are_allowed(string connectionString)
    {
        Assert.True(LocalPostgresConnectionPolicy.IsLoopbackConnectionString(connectionString));
    }

    [Theory]
    [InlineData("Host=postgres.internal;Port=5432;Database=pitchmind")]
    [InlineData("Host=10.0.0.7;Port=5432;Database=pitchmind")]
    [InlineData("Host=127.0.0.1,postgres.internal;Port=5432;Database=pitchmind")]
    [InlineData("not a connection string")]
    public void Remote_multi_host_or_invalid_endpoints_are_rejected(string connectionString)
    {
        Assert.False(LocalPostgresConnectionPolicy.IsLoopbackConnectionString(connectionString));
    }
}
