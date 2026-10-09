using System.Xml.Linq;

namespace PitchMind.Architecture.Tests;

public sealed class DependencyTests
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedProjectReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["PitchMind.Domain"] = [],
            ["PitchMind.Contracts"] = [],
            ["PitchMind.Application"] = ["PitchMind.Domain"],
            ["PitchMind.Infrastructure"] = ["PitchMind.Application"],
            ["PitchMind.Api"] = ["PitchMind.Application", "PitchMind.Infrastructure", "PitchMind.Contracts"],
            ["PitchMind.EventWorker"] = ["PitchMind.Application"],
            ["PitchMind.AgentWorker"] = ["PitchMind.Application"]
        };

    [Xunit.Fact]
    public void SourceProjects_FollowTheDocumentedDependencyMap()
    {
        var root = FindRepositoryRoot();
        var actual = ReadProjectReferences(root);

        Xunit.Assert.Empty(ArchitectureDependencyPolicy.Validate(AllowedProjectReferences, actual));
    }

    [Xunit.Fact]
    public void SourceProjects_DoNotReferenceInternalAssembliesOrPackagesOutsideTheProjectMap()
    {
        var root = FindRepositoryRoot();
        var projects = ReadProjectDocuments(root);

        Xunit.Assert.Empty(ArchitectureDependencyPolicy.ValidateInternalReferences(projects));
    }

    [Xunit.Fact]
    public void Domain_HasNoPackageOrFrameworkReferences()
    {
        var root = FindRepositoryRoot();
        var domain = XDocument.Load(Path.Combine(root, "src", "PitchMind.Domain", "PitchMind.Domain.csproj"));

        Xunit.Assert.DoesNotContain(domain.Descendants(), element =>
            element.Name.LocalName is "PackageReference" or "FrameworkReference" or "Reference" or
                "COMReference" or "NativeReference");
    }

    [Xunit.Fact]
    public void ForbiddenDomainReference_IsRejectedByTheArchitecturePolicy()
    {
        var invalid = AllowedProjectReferences.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
        invalid["PitchMind.Domain"] = ["PitchMind.Infrastructure"];

        var violations = ArchitectureDependencyPolicy.Validate(AllowedProjectReferences, invalid);

        Xunit.Assert.Contains(violations, violation =>
            violation.Contains("PitchMind.Domain", StringComparison.Ordinal) &&
            violation.Contains("PitchMind.Infrastructure", StringComparison.Ordinal));
    }

    [Xunit.Fact]
    public void InternalAssemblyAndPackageReferences_AreRejectedOutsideTheProjectMap()
    {
        var projects = new Dictionary<string, XDocument>(StringComparer.Ordinal)
        {
            ["PitchMind.Api"] = XDocument.Parse(
                "<Project><ItemGroup><Reference Include='PitchMind.Infrastructure, Version=1.0.0.0' />" +
                "<Reference Include='..\\lib\\PitchMind.Infrastructure.dll' />" +
                "<PackageReference Include='PitchMind.Domain' Version='1.0.0' /></ItemGroup></Project>")
        };

        var violations = ArchitectureDependencyPolicy.ValidateInternalReferences(projects);

        Xunit.Assert.Equal(3, violations.Count);
        Xunit.Assert.Contains(violations, violation => violation.Contains("Reference 'PitchMind.Infrastructure", StringComparison.Ordinal));
        Xunit.Assert.Contains(violations, violation => violation.Contains("..\\lib\\PitchMind.Infrastructure.dll", StringComparison.Ordinal));
        Xunit.Assert.Contains(violations, violation => violation.Contains("PackageReference 'PitchMind.Domain", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PitchMind.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find PitchMind.slnx above the test output directory.");
    }

    private static IReadOnlyDictionary<string, string[]> ReadProjectReferences(string root)
    {
        return ReadProjectDocuments(root).ToDictionary(
            project => project.Key,
            project => project.Value
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => Path.GetFileNameWithoutExtension(include!))
                .Order(StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, XDocument> ReadProjectDocuments(string root)
    {
        var projects = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories);
        return projects.ToDictionary(
            project => Path.GetFileNameWithoutExtension(project),
            XDocument.Load,
            StringComparer.Ordinal);
    }
}

internal static class ArchitectureDependencyPolicy
{
    public static IReadOnlyList<string> ValidateInternalReferences(
        IReadOnlyDictionary<string, XDocument> projects)
    {
        var violations = new List<string>();

        foreach (var (project, document) in projects)
        {
            foreach (var item in document.Descendants().Where(element =>
                         element.Name.LocalName is "Reference" or "PackageReference"))
            {
                var include = item.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                {
                    continue;
                }

                var identity = include.Split(',', 2)[0].Trim();
                var normalizedIdentity = identity.Replace('\\', '/');
                var packageOrAssembly = normalizedIdentity[(normalizedIdentity.LastIndexOf('/') + 1)..];
                if (packageOrAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    packageOrAssembly = packageOrAssembly[..^4];
                }

                if (packageOrAssembly.StartsWith("PitchMind.", StringComparison.Ordinal))
                {
                    violations.Add(
                        $"Project '{project}' uses direct {item.Name.LocalName} '{include}' for an internal PitchMind assembly; use a mapped ProjectReference.");
                }
            }
        }

        return violations;
    }

    public static IReadOnlyList<string> Validate(
        IReadOnlyDictionary<string, string[]> allowed,
        IReadOnlyDictionary<string, string[]> actual)
    {
        var violations = new List<string>();

        foreach (var project in allowed.Keys.Except(actual.Keys, StringComparer.Ordinal))
        {
            violations.Add($"Expected source project '{project}' was not found.");
        }

        foreach (var project in actual.Keys.Except(allowed.Keys, StringComparer.Ordinal))
        {
            violations.Add($"Unexpected source project '{project}' has no documented dependency policy.");
        }

        foreach (var (project, references) in actual)
        {
            if (!allowed.TryGetValue(project, out var permitted))
            {
                continue;
            }

            foreach (var reference in references.Except(permitted, StringComparer.Ordinal))
            {
                violations.Add($"Project '{project}' has forbidden project reference '{reference}'.");
            }

            foreach (var missing in permitted.Except(references, StringComparer.Ordinal))
            {
                violations.Add($"Project '{project}' is missing documented project reference '{missing}'.");
            }
        }

        return violations;
    }
}
