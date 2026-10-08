using System.Reflection;
using PitchMind.Domain.Events;
using PitchMind.Application.Abstractions;
using PitchMind.Infrastructure.Storage;

namespace PitchMind.Architecture.Tests;

public sealed class DependencyTests
{
    [Xunit.Fact]
    public void Domain_HasNoExternalAssemblyReferences()
    {
        var domain = typeof(MatchEvent).Assembly;
        var dependencies = domain.GetReferencedAssemblies().Select(x => x.Name ?? "").ToArray();
        Xunit.Assert.DoesNotContain(dependencies, name =>
            name.StartsWith("Azure.", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
            name.StartsWith("PitchMind.Application", StringComparison.Ordinal) ||
            name.StartsWith("PitchMind.Infrastructure", StringComparison.Ordinal));
    }

    [Xunit.Fact]
    public void DependencyDirection_IsInward()
    {
        var application = typeof(IMatchEventStore).Assembly;
        var infrastructure = typeof(InMemoryMatchEventStore).Assembly;
        Xunit.Assert.DoesNotContain(application.GetReferencedAssemblies(),
            assembly => assembly.Name == infrastructure.GetName().Name);
        Xunit.Assert.Contains(infrastructure.GetReferencedAssemblies(),
            assembly => assembly.Name == application.GetName().Name);
    }
}
