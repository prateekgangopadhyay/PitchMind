using System.Diagnostics;
using System.Text.Json;
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
        var projects = ReadEvaluatedProjectDocuments(root);

        Xunit.Assert.Empty(ArchitectureDependencyPolicy.ValidateInternalReferences(projects));
    }

    [Xunit.Fact]
    public void Domain_HasNoPackageOrFrameworkReferences()
    {
        var root = FindRepositoryRoot();
        var domain = ReadEvaluatedProjectDocuments(root)["PitchMind.Domain"];

        Xunit.Assert.DoesNotContain(domain.Descendants(), element =>
            element.Name.LocalName is "PackageReference" or "Reference" or "COMReference" or "NativeReference" ||
            element.Name.LocalName == "FrameworkReference" &&
            !string.Equals(element.Attribute("IsImplicitlyDefined")?.Value, "true", StringComparison.OrdinalIgnoreCase));
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
                "<Reference Include='InfrastructureAlias'><HintPath>..\\lib\\PitchMind.Infrastructure.dll</HintPath></Reference>" +
                "<PackageReference Include='PitchMind.Domain' Version='1.0.0' />" +
                "<PackageAssembly Include='lib/net10.0/PitchMind.Contracts.dll' /></ItemGroup></Project>")
        };

        var violations = ArchitectureDependencyPolicy.ValidateInternalReferences(projects);

        Xunit.Assert.Equal(4, violations.Count);
        Xunit.Assert.Contains(violations, violation => violation.Contains("Reference 'PitchMind.Infrastructure", StringComparison.Ordinal));
        Xunit.Assert.Contains(violations, violation => violation.Contains("Reference 'InfrastructureAlias", StringComparison.Ordinal));
        Xunit.Assert.Contains(violations, violation => violation.Contains("PackageReference 'PitchMind.Domain", StringComparison.Ordinal));
        Xunit.Assert.Contains(violations, violation => violation.Contains("PackageAssembly 'lib/net10.0/PitchMind.Contracts.dll", StringComparison.Ordinal));
    }

    [Xunit.Fact]
    public void EvaluatedImportsHintPathsAndPackageAssets_AreChecked()
    {
        var root = Directory.CreateTempSubdirectory("pitchmind-architecture-");
        try
        {
            var projectPath = Path.Combine(root.FullName, "Fixture.csproj");
            File.WriteAllText(projectPath,
                "<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root.FullName, "Directory.Build.targets"),
                "<Project><ItemGroup><Reference Include='InfrastructureAlias'><HintPath>$(MSBuildThisFileDirectory)lib/PitchMind.Infrastructure.dll</HintPath></Reference>" +
                "<PackageReference Include='Contoso.Football' Version='1.0.0' /></ItemGroup></Project>");
            Directory.CreateDirectory(Path.Combine(root.FullName, "obj"));
            File.WriteAllText(Path.Combine(root.FullName, "obj", "project.assets.json"),
                """
                {"version":4,"targets":{"net10.0":{"Contoso.Football/1.0.0":{"type":"package","compile":{"lib/net10.0/PitchMind.Domain.dll":{}}}}}}
                """);

            var evaluated = EvaluateProjectItems(projectPath);
            AddPackageCompileAssets(evaluated, projectPath);
            Xunit.Assert.Contains(evaluated.Descendants(), element =>
                element.Name.LocalName == "PackageReference" && element.Attribute("Include")?.Value == "Contoso.Football");
            var violations = ArchitectureDependencyPolicy.ValidateInternalReferences(
                new Dictionary<string, XDocument>(StringComparer.Ordinal) { ["Fixture"] = evaluated });

            Xunit.Assert.Contains(violations, violation => violation.Contains("InfrastructureAlias", StringComparison.Ordinal));
            Xunit.Assert.Contains(violations, violation => violation.Contains("PitchMind.Domain.dll", StringComparison.Ordinal));
        }
        finally
        {
            root.Delete(recursive: true);
        }
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
        var sourceProjects = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories);
        var sourcePaths = sourceProjects
            .Select(Path.GetFullPath)
            .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        return ReadEvaluatedProjectDocuments(root).ToDictionary(
            project => project.Key,
            project => project.Value.Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element =>
                {
                    var include = element.Attribute("FullPath")?.Value ?? element.Attribute("Include")?.Value;
                    if (string.IsNullOrWhiteSpace(include))
                    {
                        return "<ProjectReference without Include>";
                    }

                    var sourceProject = project.Value.Root?.Attribute("ProjectPath")?.Value;
                    var fullPath = Path.GetFullPath(include, Path.GetDirectoryName(sourceProject)!);
                    return sourcePaths.Contains(fullPath)
                        ? Path.GetFileNameWithoutExtension(fullPath)
                        : $"<ProjectReference outside src: {fullPath}>";
                })
                .Order(StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, XDocument> ReadEvaluatedProjectDocuments(string root)
    {
        var projects = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories);
        return projects.ToDictionary(
            project => Path.GetFileNameWithoutExtension(project),
            project =>
            {
                var evaluated = EvaluateProjectItems(project);
                AddPackageCompileAssets(evaluated, project);
                return evaluated;
            },
            StringComparer.Ordinal);
    }

    private static XDocument EvaluateProjectItems(string projectPath)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-getItem:ProjectReference,Reference,PackageReference,FrameworkReference,COMReference,NativeReference");
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-property:NuGetAudit=false");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet msbuild.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"MSBuild evaluation timed out for '{projectPath}'.");
        }

        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"MSBuild evaluation failed for '{projectPath}': {error}");
        }

        using var json = JsonDocument.Parse(output);
        var evaluated = new XDocument(new XElement("Project", new XAttribute("ProjectPath", Path.GetFullPath(projectPath))));
        var itemRoot = json.RootElement.GetProperty("Items");
        foreach (var itemType in new[] { "ProjectReference", "Reference", "PackageReference", "FrameworkReference", "COMReference", "NativeReference" })
        {
            if (!itemRoot.TryGetProperty(itemType, out var items))
            {
                continue;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("Identity", out var identity))
                {
                    continue;
                }

                var element = new XElement(itemType, new XAttribute("Include", identity.GetString() ?? string.Empty));
                foreach (var metadata in item.EnumerateObject().Where(property => property.Name != "Identity" && property.Value.ValueKind == JsonValueKind.String))
                {
                    element.SetAttributeValue(metadata.Name, metadata.Value.GetString());
                }

                evaluated.Root!.Add(element);
            }
        }

        return evaluated;
    }

    private static void AddPackageCompileAssets(XDocument project, string projectPath)
    {
        var assetsPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
        {
            return;
        }

        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        if (!assets.RootElement.TryGetProperty("targets", out var targets))
        {
            return;
        }

        foreach (var target in targets.EnumerateObject())
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                if (!library.Value.TryGetProperty("type", out var type) || type.GetString() != "package")
                {
                    continue;
                }

                if (library.Value.TryGetProperty("compile", out var compile) && compile.ValueKind == JsonValueKind.Object)
                {
                    foreach (var assembly in compile.EnumerateObject())
                    {
                        project.Root!.Add(new XElement("PackageAssembly", new XAttribute("Include", assembly.Name)));
                    }
                }

                foreach (var runtimeGroup in library.Value.EnumerateObject().Where(property =>
                             (property.Name is "runtime" or "runtimeTargets" or "native") &&
                             property.Value.ValueKind == JsonValueKind.Object))
                {
                    foreach (var assembly in runtimeGroup.Value.EnumerateObject().Where(asset =>
                                 asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                    {
                        project.Root!.Add(new XElement("PackageAssembly", new XAttribute("Include", assembly.Name)));
                    }
                }
            }
        }
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
                         element.Name.LocalName is "Reference" or "PackageReference" or "PackageAssembly" or
                             "COMReference" or "NativeReference"))
            {
                var include = item.Attribute("Include")?.Value;
                var candidates = new[]
                    {
                        include,
                        item.Attribute("HintPath")?.Value,
                        item.Attribute("FullPath")?.Value,
                        item.Element("HintPath")?.Value
                    };
                if (!candidates.Any(IsInternalAssemblyReference))
                {
                    continue;
                }

                violations.Add(
                    $"Project '{project}' uses direct {item.Name.LocalName} '{include}' for an internal PitchMind assembly; use a mapped ProjectReference.");
            }
        }

        return violations;
    }

    private static bool IsInternalAssemblyReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var identity = value.Split(',', 2)[0].Trim().Replace('\\', '/');
        var assemblyName = identity[(identity.LastIndexOf('/') + 1)..];
        if (assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            assemblyName = assemblyName[..^4];
        }

        return assemblyName.StartsWith("PitchMind.", StringComparison.Ordinal);
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
