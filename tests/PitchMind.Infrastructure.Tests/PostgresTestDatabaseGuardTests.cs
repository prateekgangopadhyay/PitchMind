using Xunit;

namespace PitchMind.Infrastructure.Tests;

public sealed class PostgresTestDatabaseGuardTests
{
    [Theory]
    [InlineData("Host=localhost;Database=postgres;Username=postgres")]
    [InlineData("Host=127.0.0.1;Database=pitchmind;Username=postgres")]
    [InlineData("Host=database.example.com;Database=pitchmind_test_0123456789abcdef0123456789abcdef;Username=postgres")]
    [InlineData("Host=localhost,127.0.0.1;Database=pitchmind_test_0123456789abcdef0123456789abcdef;Username=postgres")]
    [InlineData("Host=localhost;Database=pitchmind_test_not-a-guid;Username=postgres")]
    public void RegularOrMistargetedDatabaseTargetsAreRejectedWithoutConnecting(string connectionString)
    {
        Assert.False(PostgresTestDatabaseGuard.TryValidate(connectionString, out _));
        Assert.Throws<InvalidOperationException>(() =>
            PostgresTestDatabaseGuard.RequireSafeConnectionString(connectionString));
    }

    [Theory]
    [InlineData("Host=localhost;Database=pitchmind_test_0123456789abcdef0123456789abcdef;Username=postgres")]
    [InlineData("Host=127.0.0.1;Database=pitchmind_test_0123456789abcdef0123456789abcdef;Username=postgres")]
    [InlineData("Host=::1;Database=pitchmind_test_0123456789abcdef0123456789abcdef;Username=postgres")]
    public void LocalDisposableDatabaseTargetsAreAllowed(string connectionString)
    {
        Assert.True(PostgresTestDatabaseGuard.TryValidate(connectionString, out var reason), reason);
    }
}
