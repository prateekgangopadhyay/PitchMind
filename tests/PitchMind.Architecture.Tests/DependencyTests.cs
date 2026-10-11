using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml.Linq;

namespace PitchMind.Architecture.Tests;

public sealed class DependencyTests
{
    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<string, XDocument>>> EvaluatedProjectsCache = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

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

        Xunit.Assert.Contains(projects.Values.SelectMany(document => document.Descendants()),
            item => item.Name.LocalName == "TargetResolvedReference");
        Xunit.Assert.Contains(projects.Values.SelectMany(document => document.Descendants()),
            item => item.Name.LocalName == "TargetCompilerReference");
        var violations = ArchitectureDependencyPolicy.ValidateInternalReferences(projects, AllowedProjectReferences);
        Xunit.Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations.Take(8)));
    }

    [Xunit.Fact]
    public void Domain_HasNoPackageOrFrameworkReferences()
    {
        var root = FindRepositoryRoot();
        var domain = ReadEvaluatedProjectDocuments(root)["PitchMind.Domain"];

        var violations = ArchitectureDependencyPolicy.ValidateDomainReferences(domain);
        Xunit.Assert.Empty(violations);
    }

    [Xunit.Fact]
    public void ProductionSource_DoesNotGenerateMatchSpecificFixtures()
    {
        var root = FindRepositoryRoot();
        var productionFiles = EnumerateProductionSourceFiles(root);
        var fixtureDataMarkers = ReadFixtureDataMarkers(root);
        var numericFixtureMarkers = ReadFixtureNumericMarkers(root);
        var violations = FindFixtureDataMarkers(productionFiles, fixtureDataMarkers, numericFixtureMarkers)
            .Select(item => $"{Path.GetRelativePath(root, item.Path)}:{item.LineNumber}: {item.Line.Trim()}")
            .ToArray();

        Xunit.Assert.Empty(violations);
    }

    [Xunit.Theory]
    [Xunit.InlineData("fixture.cs")]
    [Xunit.InlineData("App.tsx")]
    public void ProductionFixtureGuard_RejectsNamedMatchDataInEveryRuntimeLanguage(string fileName)
    {
        var root = Directory.CreateTempSubdirectory("pitchmind-production-fixture-guard-");
        try
        {
            var source = Path.Combine(root.FullName, fileName);
            File.WriteAllText(source, "const team = 'Harbour FC';");

            var violations = FindFixtureDataMarkers([source], new HashSet<string>(["Harbour FC"], StringComparer.Ordinal));

            Xunit.Assert.Single(violations);
            Xunit.Assert.Equal(source, violations[0].Path);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Xunit.Fact]
    public void ProductionFixtureGuard_RejectsEventAndTeamIdsFromCandidateFeed()
    {
        var root = FindRepositoryRoot();
        var feedPath = Path.Combine(root, "data", "simulations", "manu-liv-match-dataset.json");
        using var feedStream = File.OpenRead(feedPath);
        using var feed = JsonDocument.Parse(feedStream);
        var firstEvent = feed.RootElement[0];
        var eventId = firstEvent.GetProperty("eventId").GetString();
        var teamId = firstEvent.GetProperty("teamId").GetString();
        Xunit.Assert.False(string.IsNullOrWhiteSpace(eventId));
        Xunit.Assert.False(string.IsNullOrWhiteSpace(teamId));

        var markers = ReadFixtureDataMarkers(root);
        Xunit.Assert.Contains(eventId!, markers);
        Xunit.Assert.Contains(teamId!, markers);

        var sourceRoot = Directory.CreateTempSubdirectory("pitchmind-feed-fixture-guard-");
        try
        {
            var source = Path.Combine(sourceRoot.FullName, "App.tsx");
            File.WriteAllText(source, $"const candidateFixture = {{ eventId: '{eventId}', teamId: '{teamId}' }};");

            var violations = FindFixtureDataMarkers([source], markers);

            Xunit.Assert.Single(violations);
            Xunit.Assert.Equal(source, violations[0].Path);
        }
        finally
        {
            sourceRoot.Delete(recursive: true);
        }
    }

    [Xunit.Fact]
    public void ProductionFixtureGuard_RejectsCandidateFootballNumericLiterals()
    {
        var root = FindRepositoryRoot();
        var feedPath = Path.Combine(root, "data", "simulations", "manu-liv-match-dataset.json");
        using var feedStream = File.OpenRead(feedPath);
        using var feed = JsonDocument.Parse(feedStream);
        var firstEvent = feed.RootElement[0];
        var finalEvent = feed.RootElement[feed.RootElement.GetArrayLength() - 1];
        var firstSequence = firstEvent.GetProperty("sequence").GetInt32();
        var firstClockSeconds = firstEvent.GetProperty("clockSeconds").GetInt32();
        var firstPitchX = firstEvent.GetProperty("pitchX").GetRawText();
        var firstPitchValue = decimal.Parse(firstPitchX, NumberStyles.Float, CultureInfo.InvariantCulture);
        var firstPitchEquivalent = firstPitchValue.ToString("G29", CultureInfo.InvariantCulture) + "e0";
        var firstPitchInteger = firstPitchValue.ToString("G29", CultureInfo.InvariantCulture);
        var firstPlayer = firstEvent.GetProperty("playerPositions")[0];
        var firstPlayerX = firstPlayer.GetProperty("x").GetRawText();
        var firstPlayerSpeed = firstPlayer.GetProperty("speed").GetRawText();
        var firstPlayerHeartRate = firstPlayer.GetProperty("heartRate").GetRawText();
        var firstPlayerHeartRateWithSeparator = firstPlayerHeartRate.Insert(firstPlayerHeartRate.Length / 2, "_");
        var finalSequence = finalEvent.GetProperty("sequence").GetInt32();
        var finalClockSeconds = finalEvent.GetProperty("clockSeconds").GetInt32();
        var finalPlayer = finalEvent.GetProperty("playerPositions")[0];
        var finalPlayerStamina = finalPlayer.GetProperty("stamina").GetRawText();

        var sourceRoot = Directory.CreateTempSubdirectory("pitchmind-feed-numeric-guard-");
        try
        {
            var source = Path.Combine(sourceRoot.FullName, "App.tsx");
            var csharpSource = Path.Combine(sourceRoot.FullName, "Fixture.cs");
            File.WriteAllText(source,
                $"const firstSequence = {{ sequence: {firstSequence} }};\n" +
                $"const firstClock = {{ clockSeconds: {firstClockSeconds} }};\n" +
                $"const firstPitch = {{ pitchX: {firstPitchX} }};\n" +
                $"const equivalentPitch = {{ pitchX: {firstPitchEquivalent} }};\n" +
                $"const integerPitch = {{ pitchY: {firstPitchInteger} }};\n" +
                $"const firstPlayerX = {{ x: {firstPlayerX} }};\n" +
                $"const firstPlayerSpeed = {{ speed: {firstPlayerSpeed} }};\n" +
                $"const firstPlayerHeartRate = {{ heartRate: {firstPlayerHeartRateWithSeparator} }};\n" +
                $"const finalSequence = {{ sequence: {finalSequence} }};\n" +
                $"const finalClock = {{ clockSeconds: {finalClockSeconds} }};\n" +
                $"const finalPlayer = {{ stamina: {finalPlayerStamina} }};\n" +
                "const unrelatedPageSize = 25;");
            File.WriteAllText(csharpSource,
                $"var coordinate = new {{ pitchX = {firstPitchX}m }};\n" +
                $"var player = new {{ heartRate = {firstPlayerHeartRateWithSeparator} }};");

            var violations = FindFixtureDataMarkers(
                [source, csharpSource],
                new HashSet<string>(StringComparer.Ordinal),
                ReadFixtureNumericMarkers(root));

            Xunit.Assert.Equal(13, violations.Count);
            Xunit.Assert.Contains(violations, item => item.Line.Contains($"sequence: {firstSequence}", StringComparison.Ordinal));
            Xunit.Assert.Contains(violations, item => item.Line.Contains($"clockSeconds: {firstClockSeconds}", StringComparison.Ordinal));
            Xunit.Assert.Contains(violations, item => item.Line.Contains($"pitchX: {firstPitchX}", StringComparison.Ordinal));
            Xunit.Assert.Contains(violations, item => item.Line.Contains($"pitchX: {firstPitchEquivalent}", StringComparison.Ordinal));
            Xunit.Assert.Contains(violations, item => item.Line.Contains($"pitchY: {firstPitchInteger}", StringComparison.Ordinal));
            Xunit.Assert.Contains(violations, item => item.Line.Contains($"x: {firstPlayerX}", StringComparison.Ordinal));
            Xunit.Assert.Contains(violations, item => item.Line.Contains($"speed: {firstPlayerSpeed}", StringComparison.Ordinal));
            Xunit.Assert.Contains(violations, item => item.Line.Contains($"heartRate: {firstPlayerHeartRateWithSeparator}", StringComparison.Ordinal));
            Xunit.Assert.Contains(violations, item => item.Line.Contains($"pitchX = {firstPitchX}m", StringComparison.Ordinal));
            Xunit.Assert.Contains(violations, item => item.Line.Contains($"heartRate = {firstPlayerHeartRateWithSeparator}", StringComparison.Ordinal));
            Xunit.Assert.Contains(violations, item => item.Line.Contains($"sequence: {finalSequence}", StringComparison.Ordinal));
            Xunit.Assert.Contains(violations, item => item.Line.Contains($"clockSeconds: {finalClockSeconds}", StringComparison.Ordinal));
            Xunit.Assert.Contains(violations, item => item.Line.Contains($"stamina: {finalPlayerStamina}", StringComparison.Ordinal));
        }
        finally
        {
            sourceRoot.Delete(recursive: true);
        }
    }

    private static IEnumerable<string> EnumerateProductionSourceFiles(string root)
    {
        var sourceRoots = new[]
        {
            Path.Combine(root, "src"),
            Path.Combine(root, "frontend", "src")
        };
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".js", ".jsx", ".ts", ".tsx"
        };

        return sourceRoots
            .Where(Directory.Exists)
            .SelectMany(sourceRoot => Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Where(path => !Path.GetFileName(path).Contains(".test.", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).Contains(".spec.", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlySet<string> ReadFixtureDataMarkers(string root)
    {
        var markers = new HashSet<string>(StringComparer.Ordinal)
        {
            "SyntheticFixtureGenerator", "Harbour FC", "Ridge Athletic"
        };

        var feedPath = Path.Combine(root, "data", "simulations", "manu-liv-match-dataset.json");
        if (!File.Exists(feedPath))
        {
            throw new FileNotFoundException("The candidate synthetic feed is required to check production source for fixture literals.", feedPath);
        }

        using var stream = File.OpenRead(feedPath);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The candidate synthetic feed must be a JSON array for the production-source guard.");
        }

        foreach (var sourceEvent in document.RootElement.EnumerateArray())
        {
            AddStringProperties(sourceEvent, markers, "eventId", "matchId", "teamId", "teamName", "playerName", "receiverName");
            if (!sourceEvent.TryGetProperty("playerPositions", out var positions) || positions.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var position in positions.EnumerateArray())
            {
                AddStringProperties(position, markers, "teamId", "teamName", "playerName");
            }
        }

        return markers;
    }

    private static void AddStringProperties(JsonElement element, ISet<string> markers, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    markers.Add(value);
                }
            }
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> ReadFixtureNumericMarkers(string root)
    {
        var markers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var feedPath = Path.Combine(root, "data", "simulations", "manu-liv-match-dataset.json");
        if (!File.Exists(feedPath))
        {
            throw new FileNotFoundException("The candidate synthetic feed is required to check production source for match-time literals.", feedPath);
        }

        using var stream = File.OpenRead(feedPath);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The candidate synthetic feed must be a JSON array for the production-source guard.");
        }

        foreach (var sourceEvent in document.RootElement.EnumerateArray())
        {
            AddFootballNumericMarkers(sourceEvent, markers);
        }

        return markers.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlySet<string>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static void AddFootballNumericMarkers(JsonElement element, IDictionary<string, HashSet<string>> markers)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (FootballNumericProperties.Contains(property.Name))
                {
                    AddNumberProperty(element, property.Name, markers);
                }

                AddFootballNumericMarkers(property.Value, markers);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AddFootballNumericMarkers(item, markers);
            }
        }
    }

    private static readonly HashSet<string> FootballNumericProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "sequence", "clockSeconds", "period", "pitchX", "pitchY", "ballX", "ballY", "endX", "endY",
        "jerseyNumber", "x", "y", "speed", "distanceCovered", "heatIndex", "passAccuracy", "touches",
        "stamina", "heartRate"
    };

    private static void AddNumberProperty(JsonElement element, string name, IDictionary<string, HashSet<string>> markers)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return;
        }

        if (!markers.TryGetValue(name, out var values))
        {
            values = new HashSet<string>(StringComparer.Ordinal);
            markers.Add(name, values);
        }

        values.Add(NormalizeNumericLiteral(property.GetRawText()));
    }

    private static IReadOnlyList<(string Path, string Line, int LineNumber)> FindFixtureDataMarkers(
        IEnumerable<string> files,
        IReadOnlySet<string> markers,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? numericMarkers = null)
    {
        return files
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (Path: path, Line: line, LineNumber: index + 1)))
            .Where(item => markers.Any(marker => item.Line.Contains(marker, StringComparison.Ordinal)) ||
                ContainsNumericFixtureMarker(item.Line, numericMarkers))
            .ToArray();
    }

    private static bool ContainsNumericFixtureMarker(
        string line,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? numericMarkers)
    {
        if (numericMarkers is null || numericMarkers.Count == 0)
        {
            return false;
        }

        foreach (Match match in Regex.Matches(line, @"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*[:=]\s*(?<value>-?(?:[0-9](?:_?[0-9])*(?:\.(?:[0-9](?:_?[0-9])*)?)?|\.[0-9](?:_?[0-9])*)(?:[eE][+-]?[0-9](?:_?[0-9])*)?)(?<suffix>(?:[fFdDmM]|[uU][lL]?|[lL][uU]?)?)(?![A-Za-z0-9_.])"))
        {
            if (numericMarkers.TryGetValue(match.Groups["name"].Value, out var values) &&
                values.Contains(NormalizeNumericLiteral(match.Groups["value"].Value)))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeNumericLiteral(string value) =>
        decimal.TryParse(value.Replace("_", string.Empty, StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number.ToString("G29", CultureInfo.InvariantCulture)
            : value;

    [Xunit.Fact]
    public void Domain_RejectsForgedImplicitFrameworkReferences()
    {
        var sdkPath = Path.Combine(Path.GetTempPath(), "dotnet", "sdk", "10.0.0", "Sdks");
        var explicitFramework = new XDocument(new XElement("Project",
            new XAttribute("MSBuildSDKsPath", sdkPath),
            new XElement("FrameworkReference",
                new XAttribute("Include", "Contoso.Framework"),
                new XAttribute("IsImplicitlyDefined", "true"),
                new XAttribute("DefiningProjectFullPath", Path.Combine(Path.GetTempPath(), "Directory.Build.targets")))));
        var forgedSdkFramework = new XDocument(new XElement("Project",
            new XAttribute("MSBuildSDKsPath", sdkPath),
            new XElement("FrameworkReference",
                new XAttribute("Include", "Microsoft.NETCore.App"),
                new XAttribute("IsImplicitlyDefined", "true"),
                new XAttribute("DefiningProjectFullPath", Path.Combine(Path.GetTempPath(), "Directory.Build.targets")))));

        Xunit.Assert.NotEmpty(ArchitectureDependencyPolicy.ValidateDomainReferences(explicitFramework));
        Xunit.Assert.NotEmpty(ArchitectureDependencyPolicy.ValidateDomainReferences(forgedSdkFramework));
    }

    [Xunit.Fact]
    public void Domain_RejectsTargetInjectedExternalAssemblyInputs()
    {
        var root = Directory.CreateTempSubdirectory("pitchmind-domain-external-input-");
        try
        {
            var externalAssembly = Path.Combine(root.FullName, "System.Text.Json.dll");
            File.Copy(typeof(JsonDocument).Assembly.Location, externalAssembly);
            var definingProject = Path.Combine(root.FullName, "Directory.Build.targets");
            var domain = new XDocument(new XElement("Project",
                new XElement("TargetResolvedReference",
                    new XAttribute("Include", externalAssembly),
                    new XAttribute("FullPath", externalAssembly),
                    new XAttribute("AssemblyName", "System.Text.Json"),
                    new XAttribute("DefiningProjectFullPath", definingProject)),
                new XElement("TargetCompilerReference",
                    new XAttribute("Include", externalAssembly),
                    new XAttribute("FullPath", externalAssembly),
                    new XAttribute("ReferenceAssembly", externalAssembly),
                    new XAttribute("AssemblyName", "System.Text.Json"),
                    new XAttribute("DefiningProjectFullPath", definingProject))));

            var violations = ArchitectureDependencyPolicy.ValidateDomainReferences(domain);

            Xunit.Assert.Contains(violations, violation => violation.Contains("TargetResolvedReference '" + externalAssembly, StringComparison.Ordinal));
            Xunit.Assert.Contains(violations, violation => violation.Contains("TargetCompilerReference '" + externalAssembly, StringComparison.Ordinal));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Xunit.Fact]
    public void Domain_RejectsCompilerReferenceAddedImmediatelyBeforeCoreCompile()
    {
        var root = Directory.CreateTempSubdirectory("pitchmind-late-compiler-input-");
        try
        {
            var projectPath = Path.Combine(root.FullName, "Fixture.csproj");
            var injectedAssembly = Path.Combine(AppContext.BaseDirectory, "PitchMind.Infrastructure.dll");
            File.WriteAllText(projectPath,
                "<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>" +
                "<Target Name='InjectLateCompilerReference' BeforeTargets='CoreCompile'><ItemGroup>" +
                "<ReferencePathWithRefAssemblies Include='" + injectedAssembly + "' />" +
                "</ItemGroup></Target></Project>");
            File.WriteAllText(Path.Combine(root.FullName, "Fixture.cs"), "public sealed class Fixture { }");
            RestoreFixture(projectPath);

            var evaluated = EvaluateProjectItems(projectPath, includeTargetReferences: true);
            var violations = ArchitectureDependencyPolicy.ValidateDomainReferences(evaluated);

            Xunit.Assert.Contains(evaluated.Descendants(), item => item.Name.LocalName == "TargetFinalCompilerReference" &&
                item.Attribute("FullPath")?.Value == injectedAssembly);
            Xunit.Assert.Contains(violations, violation => violation.Contains("TargetFinalCompilerReference", StringComparison.Ordinal) &&
                violation.Contains(injectedAssembly, StringComparison.Ordinal));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Xunit.Fact]
    public void Domain_RejectsCompilerReferenceMetadataMutatedImmediatelyBeforeCoreCompile()
    {
        var root = Directory.CreateTempSubdirectory("pitchmind-late-compiler-metadata-");
        try
        {
            var projectPath = Path.Combine(root.FullName, "Fixture.csproj");
            var dotnetRoot = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
            var runtimeReference = Directory.GetFiles(
                    Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref"), "System.Runtime.dll", SearchOption.AllDirectories)
                .First(path => path.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}net10.0{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
            var injectedAssembly = Path.Combine(AppContext.BaseDirectory, "PitchMind.Infrastructure.dll");
            File.WriteAllText(projectPath,
                "<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>" +
                "<Target Name='MutateLateCompilerReferenceMetadata' BeforeTargets='CoreCompile'><ItemGroup>" +
                "<ReferencePathWithRefAssemblies Update='@(ReferencePathWithRefAssemblies)' " +
                "Condition=\"'%(ReferencePathWithRefAssemblies.FullPath)' == '" + runtimeReference + "'\">" +
                "<ReferenceAssembly>" + injectedAssembly + "</ReferenceAssembly>" +
                "</ReferencePathWithRefAssemblies></ItemGroup></Target></Project>");
            File.WriteAllText(Path.Combine(root.FullName, "Fixture.cs"), "public sealed class Fixture { }");
            RestoreFixture(projectPath);

            var evaluated = EvaluateProjectItems(projectPath, includeTargetReferences: true);
            var violations = ArchitectureDependencyPolicy.ValidateDomainReferences(evaluated);

            Xunit.Assert.Contains(evaluated.Descendants(), item => item.Name.LocalName == "TargetFinalCompilerReference" &&
                item.Attribute("FullPath")?.Value == runtimeReference &&
                item.Attribute("ReferenceAssembly")?.Value == injectedAssembly);
            Xunit.Assert.Contains(violations, violation => violation.Contains("TargetFinalCompilerReference", StringComparison.Ordinal) &&
                violation.Contains(runtimeReference, StringComparison.Ordinal));
        }
        finally
        {
            root.Delete(recursive: true);
        }
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
    public void ResolvedReferenceMetadata_CannotMasqueradeAsAnAllowedProjectOutput()
    {
        var root = Directory.CreateTempSubdirectory("pitchmind-resolved-reference-identity-");
        try
        {
            var agentWorkerDirectory = Path.Combine(root.FullName, "src", "PitchMind.AgentWorker");
            var applicationDirectory = Path.Combine(root.FullName, "src", "PitchMind.Application");
            var applicationOutput = Path.Combine(applicationDirectory, "bin", "Debug", "net10.0");
            Directory.CreateDirectory(agentWorkerDirectory);
            Directory.CreateDirectory(applicationOutput);
            var applicationPath = Path.Combine(applicationOutput, "PitchMind.Application.dll");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "PitchMind.Application.dll"), applicationPath);
            var forgedPath = Path.Combine(applicationOutput, "PitchMind.Infrastructure.dll");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "PitchMind.Infrastructure.dll"), forgedPath);
            var agentWorkerProject = Path.Combine(agentWorkerDirectory, "PitchMind.AgentWorker.csproj");
            var applicationProject = Path.Combine(applicationDirectory, "PitchMind.Application.csproj");
            var document = new XDocument(new XElement("Project",
                new XAttribute("ProjectPath", agentWorkerProject),
                new XElement("ProjectReference", new XAttribute("Include", "../PitchMind.Application/PitchMind.Application.csproj"),
                    new XAttribute("FullPath", applicationProject)),
                new XElement("TargetResolvedReference", new XAttribute("Include", applicationPath),
                    new XAttribute("FullPath", applicationPath),
                    new XAttribute("ReferenceAssembly", forgedPath),
                    new XAttribute("AssemblyName", "PitchMind.Application"),
                    new XAttribute("OriginalProjectReferenceItemSpec", "../PitchMind.Application/PitchMind.Application.csproj"))));
            var allowed = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["PitchMind.AgentWorker"] = ["PitchMind.Application"],
                ["PitchMind.Application"] = ["PitchMind.Domain"],
                ["PitchMind.Domain"] = []
            };

            var violations = ArchitectureDependencyPolicy.ValidateInternalReferences(
                new Dictionary<string, XDocument>(StringComparer.Ordinal) { ["PitchMind.AgentWorker"] = document }, allowed);

            Xunit.Assert.Contains(violations, violation => violation.Contains("TargetResolvedReference", StringComparison.Ordinal) &&
                violation.Contains("PitchMind.Application.dll", StringComparison.Ordinal));
        }
        finally
        {
            root.Delete(recursive: true);
        }
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
                "<Project><ItemGroup><PackageReference Include='Contoso.Football' Version='1.0.0' />" +
                "<Reference Include='InfrastructureAlias'><HintPath>lib/InfrastructureAlias.dll</HintPath></Reference>" +
                "</ItemGroup></Project>");
            var packageFolder = Path.Combine(root.FullName, "packages");
            var packageLibrary = Path.Combine(packageFolder, "contoso.football", "1.0.0", "lib", "net10.0");
            var hintPath = Path.Combine(root.FullName, "lib");
            Directory.CreateDirectory(packageLibrary);
            Directory.CreateDirectory(hintPath);
            var infrastructureAssembly = Path.Combine(AppContext.BaseDirectory, "PitchMind.Infrastructure.dll");
            File.Copy(infrastructureAssembly, Path.Combine(hintPath, "InfrastructureAlias.dll"));
            File.Copy(infrastructureAssembly, Path.Combine(packageLibrary, "Contoso.Football.Core.dll"));
            Directory.CreateDirectory(Path.Combine(root.FullName, "obj"));
            var packageFolderJson = JsonSerializer.Serialize(packageFolder + Path.DirectorySeparatorChar);
            File.WriteAllText(Path.Combine(root.FullName, "obj", "project.assets.json"),
                "{\"version\":4,\"targets\":{\"net10.0\":{\"Contoso.Football/1.0.0\":{\"type\":\"package\",\"compile\":{\"lib/net10.0/Contoso.Football.Core.dll\":{}}}}}," +
                "\"libraries\":{\"Contoso.Football/1.0.0\":{\"type\":\"package\",\"path\":\"contoso.football/1.0.0\"}}," +
                "\"packageFolders\":{" + packageFolderJson + ":{}}}");

            var evaluated = EvaluateProjectItems(projectPath);
            AddPackageCompileAssets(evaluated, projectPath);
            Xunit.Assert.Contains(evaluated.Descendants(), element =>
                element.Name.LocalName == "PackageReference" && element.Attribute("Include")?.Value == "Contoso.Football");
            var violations = ArchitectureDependencyPolicy.ValidateInternalReferences(
                new Dictionary<string, XDocument>(StringComparer.Ordinal) { ["Fixture"] = evaluated });

            Xunit.Assert.Contains(violations, violation => violation.Contains("InfrastructureAlias", StringComparison.Ordinal));
            Xunit.Assert.Contains(violations, violation => violation.Contains("PackageAssembly 'lib/net10.0/Contoso.Football.Core.dll", StringComparison.Ordinal));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Xunit.Fact]
    public void TargetCreatedInternalReferences_AreCheckedAfterResolveReferences()
    {
        var root = Directory.CreateTempSubdirectory("pitchmind-target-reference-");
        try
        {
            var projectPath = Path.Combine(root.FullName, "Fixture.csproj");
            var libraryDirectory = Path.Combine(root.FullName, "lib");
            Directory.CreateDirectory(libraryDirectory);
            var infrastructureAssembly = Path.Combine(AppContext.BaseDirectory, "PitchMind.Infrastructure.dll");
            File.Copy(infrastructureAssembly, Path.Combine(libraryDirectory, "InfrastructureAlias.dll"));
            var runtimeFrameworkVersion = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Name;
            File.WriteAllText(projectPath,
            "<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>" +
            "<ItemGroup><Reference Include='InfrastructureAlias' /><Reference Include='HarmlessBaselineReference' /></ItemGroup>" +
                "<Target Name='InjectProjectReference' BeforeTargets='ResolveProjectReferences'><ItemGroup>" +
                "<ProjectReference Include='Dependency.csproj' />" +
                "</ItemGroup></Target>" +
                "<Target Name='InjectForgedTransitiveProjectReference' BeforeTargets='IncludeTransitiveProjectReferences'><ItemGroup>" +
                "<_TransitiveProjectReferences Include='Dependency.csproj' NuGetPackageId='Contoso.Forged' NuGetPackageVersion='1.0.0' />" +
                "</ItemGroup></Target>" +
                "<Target Name='InjectDomainFrameworkReference' BeforeTargets='ResolveFrameworkReferences'><ItemGroup>" +
                "<FrameworkReference Update='Microsoft.NETCore.App'><RuntimeFrameworkVersion>" + runtimeFrameworkVersion + "</RuntimeFrameworkVersion></FrameworkReference>" +
                "</ItemGroup></Target>" +
            "<Target Name='InjectInternalReference' BeforeTargets='ResolveReferences'><ItemGroup>" +
            "<Reference Include='InfrastructureAlias'><HintPath>lib/InfrastructureAlias.dll</HintPath></Reference>" +
            "<Reference Include='PitchMind.Infrastructure' />" +
            "<PackageReference Include='PitchMind.Domain' Version='1.0.0' />" +
            "</ItemGroup></Target></Project>");
            File.WriteAllText(Path.Combine(root.FullName, "Dependency.csproj"),
                "<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            RestoreFixture(projectPath);

            var evaluated = EvaluateProjectItems(projectPath, includeTargetReferences: true);
            evaluated.Root!.Add(new XElement("TargetResolvedReference",
                new XAttribute("Include", Path.Combine(libraryDirectory, "InfrastructureAlias.dll"))));
            var violations = ArchitectureDependencyPolicy.ValidateInternalReferences(
                new Dictionary<string, XDocument>(StringComparer.Ordinal) { ["Fixture"] = evaluated });

            Xunit.Assert.Contains(evaluated.Descendants(), item => item.Name.LocalName == "TargetReference");
            Xunit.Assert.Contains(violations, violation => violation.Contains("TargetReference 'InfrastructureAlias'", StringComparison.Ordinal));
            Xunit.Assert.Contains(evaluated.Descendants(), item => item.Name.LocalName == "TargetReference" &&
                item.Attribute("Include")?.Value == "PitchMind.Infrastructure");
            Xunit.Assert.Contains(violations, violation => violation.Contains("TargetReference 'PitchMind.Infrastructure'", StringComparison.Ordinal));
            Xunit.Assert.Contains(evaluated.Descendants(), item => item.Name.LocalName == "TargetResolvedReference" &&
                item.Attribute("Include")?.Value.EndsWith("InfrastructureAlias.dll", StringComparison.Ordinal) == true);
            Xunit.Assert.Contains(violations, violation => violation.Contains("TargetResolvedReference", StringComparison.Ordinal) &&
                violation.Contains("InfrastructureAlias.dll", StringComparison.Ordinal));
            var domainViolations = ArchitectureDependencyPolicy.ValidateDomainReferences(evaluated);
            Xunit.Assert.Contains(domainViolations, violation => violation.Contains("TargetFrameworkReference 'Microsoft.NETCore.App'", StringComparison.Ordinal));
            Xunit.Assert.Contains(domainViolations, violation => violation.Contains("TargetPackageReference 'PitchMind.Domain'", StringComparison.Ordinal));
            var injectedProject = evaluated.Descendants().Single(item => item.Name.LocalName == "TargetProjectReference" &&
                item.Attribute("DefiningProjectFullPath")?.Value == projectPath &&
                item.Attribute("FullPath")?.Value.EndsWith("Dependency.csproj", StringComparison.Ordinal) == true);
            var unexpectedProject = Path.GetFileNameWithoutExtension(injectedProject.Attribute("FullPath")?.Value);
            var graphViolations = ArchitectureDependencyPolicy.Validate(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["Fixture"] = [],
                    ["Dependency"] = []
                },
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["Fixture"] = [unexpectedProject!],
                    ["Dependency"] = []
                });

            Xunit.Assert.Contains(graphViolations, violation => violation.Contains("forbidden project reference 'Dependency'", StringComparison.Ordinal));
            Xunit.Assert.Contains(evaluated.Descendants(), item => item.Name.LocalName == "TargetProjectReference" &&
                item.Attribute("DefiningProjectFullPath")?.Value.EndsWith("Microsoft.PackageDependencyResolution.targets", StringComparison.Ordinal) == true &&
                item.Attribute("FullPath")?.Value.EndsWith("Dependency.csproj", StringComparison.Ordinal) == true);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Xunit.Fact]
    public void SdkReferenceExemptions_AreLimitedToFrameworkPackAndTransitiveProjectItems()
    {
        var root = Directory.CreateTempSubdirectory("pitchmind-sdk-reference-filter-");
        try
        {
            var sdkPath = Path.Combine(root.FullName, "sdk", "10.0.0", "Sdks");
            var sdkTargets = Path.Combine(sdkPath, "Microsoft.NET.Sdk", "targets");
            var frameworkPack = Path.Combine(root.FullName, "packs", "Microsoft.NETCore.App.Ref", "10.0.0", "ref", "net10.0");
            Directory.CreateDirectory(sdkTargets);
            Directory.CreateDirectory(frameworkPack);
            var runtimeDirectory = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
            var installedDotnetRoot = runtimeDirectory.Parent!.Parent!.Parent!.FullName;
            var systemReference = Directory.GetFiles(
                    Path.Combine(installedDotnetRoot, "packs", "Microsoft.NETCore.App.Ref"),
                    "System.Runtime.dll", SearchOption.AllDirectories)
                .First();

            File.Copy(systemReference, Path.Combine(frameworkPack, "System.Runtime.dll"));
            var internalAlias = Path.Combine(frameworkPack, "System.Collections.dll");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "PitchMind.Infrastructure.dll"), internalAlias);
            var sdkFrameworkTargets = Path.Combine(sdkTargets, "Framework.targets");
            var transitiveProjectTargets = Path.Combine(sdkTargets, "Microsoft.PackageDependencyResolution.targets");
            File.WriteAllText(sdkFrameworkTargets, "");
            File.WriteAllText(transitiveProjectTargets, "");

            Xunit.Assert.True(ArchitectureDependencyPolicy.IsTrustedSdkFrameworkAssembly(sdkFrameworkTargets, Path.Combine(frameworkPack, "System.Runtime.dll"), sdkPath));
            Xunit.Assert.False(ArchitectureDependencyPolicy.IsTrustedSdkFrameworkAssembly(sdkFrameworkTargets, internalAlias, sdkPath));
            var restoredProjects = new HashSet<(string PackageId, string Version, string ProjectPath)>
            {
                ("PitchMind.Domain", "1.0.0", Path.Combine(root.FullName, "src", "PitchMind.Domain.csproj"))
            };
            Xunit.Assert.True(IsSdkTransitiveProjectReference(transitiveProjectTargets, Path.Combine(root.FullName, "src", "PitchMind.Domain.csproj"),
                "PitchMind.Domain", "1.0.0", sdkPath, restoredProjects));
            Xunit.Assert.False(IsSdkTransitiveProjectReference(transitiveProjectTargets, Path.Combine(root.FullName, "src", "PitchMind.Other.csproj"),
                "Contoso.Forged", "1.0.0", sdkPath, restoredProjects));
            Xunit.Assert.False(IsSdkTransitiveProjectReference(sdkFrameworkTargets, Path.Combine(root.FullName, "src", "PitchMind.Domain.csproj"),
                "PitchMind.Domain", "1.0.0", sdkPath, restoredProjects));
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
                .Where(element => element.Name.LocalName is "ProjectReference" or "TargetProjectReference")
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
        var fullRoot = Path.GetFullPath(root);
        return EvaluatedProjectsCache.GetOrAdd(fullRoot, repositoryRoot => new Lazy<IReadOnlyDictionary<string, XDocument>>(() =>
        {
            var projects = Directory.GetFiles(Path.Combine(repositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories);
            return projects.ToDictionary(
                project => Path.GetFileNameWithoutExtension(project),
                project =>
                {
                    var evaluated = EvaluateProjectItems(project, includeTargetReferences: true);
                    AddPackageCompileAssets(evaluated, project);
                    return evaluated;
                },
                StringComparer.Ordinal);
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static XDocument EvaluateProjectItems(string projectPath, bool includeTargetReferences = false)
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
        startInfo.ArgumentList.Add("-getItem:ProjectReference,Reference,ReferencePath,PackageReference,FrameworkReference,COMReference,NativeReference");
        startInfo.ArgumentList.Add("-getProperty:MSBuildSDKsPath");
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-property:NuGetAudit=false");
        startInfo.ArgumentList.Add("-maxcpucount:1");
        startInfo.ArgumentList.Add("-nodeReuse:false");

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
        var sdkPath = json.RootElement.GetProperty("Properties").GetProperty("MSBuildSDKsPath").GetString();
        var evaluated = new XDocument(new XElement("Project",
            new XAttribute("ProjectPath", Path.GetFullPath(projectPath)),
            new XAttribute("MSBuildSDKsPath", sdkPath ?? string.Empty)));
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

        if (includeTargetReferences)
        {
            AddTargetCreatedReferences(evaluated);
        }

        return evaluated;
    }

    private static void AddTargetCreatedReferences(XDocument project)
    {
        var projectPath = project.Root?.Attribute("ProjectPath")?.Value ??
            throw new InvalidOperationException("Evaluated project is missing its path.");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-getTargetResult:FindReferenceAssembliesForReferences");
        startInfo.ArgumentList.Add("-getProperty:MSBuildSDKsPath");
        startInfo.ArgumentList.Add("-getItem:ProjectReference,Reference,ReferencePath,ReferencePathWithRefAssemblies,PackageReference,FrameworkReference,COMReference,NativeReference");
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-property:NuGetAudit=false");
        startInfo.ArgumentList.Add("-maxcpucount:1");
        startInfo.ArgumentList.Add("-nodeReuse:false");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start target-time MSBuild evaluation.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Target-time MSBuild evaluation timed out for '{projectPath}'.");
        }

        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Target-time MSBuild evaluation failed for '{projectPath}': {error}");
        }

        using var json = JsonDocument.Parse(output);
        var sdkPath = json.RootElement.GetProperty("Properties").GetProperty("MSBuildSDKsPath").GetString();
        var restoredProjectReferences = ReadRestoredProjectReferences(projectPath);
        var existingItems = project.Descendants()
            .Where(item => item.Name.LocalName is "ProjectReference" or "Reference" or "PackageReference" or
                "FrameworkReference" or "COMReference" or "NativeReference")
            .Select(item => TargetItemSignature(item.Name.LocalName, item))
            .ToHashSet(StringComparer.Ordinal);

        var items = json.RootElement.GetProperty("Items");
        AddTargetItems("ProjectReference", "TargetProjectReference");
        AddTargetItems("Reference", "TargetReference");
        AddTargetItems("ReferencePath", "TargetResolvedReference");
        AddTargetItems("ReferencePathWithRefAssemblies", "TargetCompilerReference");
        AddTargetItems("PackageReference", "TargetPackageReference");
        AddTargetItems("FrameworkReference", "TargetFrameworkReference");
        AddTargetItems("COMReference", "TargetCOMReference");
        AddTargetItems("NativeReference", "TargetNativeReference");

        void AddTargetItems(string itemType, string capturedItemType)
        {
            if (!items.TryGetProperty(itemType, out var references))
            {
                return;
            }

            foreach (var reference in references.EnumerateArray())
            {
                if (!reference.TryGetProperty("Identity", out var identity))
                {
                    continue;
                }

                var include = identity.GetString() ?? string.Empty;
                var definingProject = reference.TryGetProperty("DefiningProjectFullPath", out var defining) ? defining.GetString() : null;
                var fullPath = reference.TryGetProperty("FullPath", out var fullPathProperty) &&
                               fullPathProperty.ValueKind == JsonValueKind.String
                    ? fullPathProperty.GetString()
                    : null;
                var nuGetPackageId = reference.TryGetProperty("NuGetPackageId", out var packageId) ? packageId.GetString() : null;
                var nuGetPackageVersion = reference.TryGetProperty("NuGetPackageVersion", out var packageVersion) ? packageVersion.GetString() : null;
                if (itemType == "ProjectReference" && IsSdkTransitiveProjectReference(definingProject, fullPath,
                        nuGetPackageId, nuGetPackageVersion, sdkPath, restoredProjectReferences))
                {
                    continue;
                }

                if (itemType == "Reference" && ArchitectureDependencyPolicy.IsTrustedSdkFrameworkAssembly(definingProject, fullPath, sdkPath))
                {
                    continue;
                }

                var item = new XElement(itemType, new XAttribute("Include", include));
                foreach (var metadata in reference.EnumerateObject().Where(property =>
                             property.Name != "Identity" && property.Value.ValueKind == JsonValueKind.String))
                {
                    item.SetAttributeValue(metadata.Name, metadata.Value.GetString());
                }

                if (existingItems.Contains(TargetItemSignature(itemType, item)))
                {
                    continue;
                }

                item.Name = capturedItemType;
                project.Root!.Add(item);
            }
        }

        AddFinalCompilerInputs(projectPath, project);
    }

    private static void AddFinalCompilerInputs(string projectPath, XDocument project)
    {
        var captureDirectory = Directory.CreateTempSubdirectory("pitchmind-compiler-input-capture-");
        try
        {
            var targetPath = Path.Combine(captureDirectory.FullName, "CaptureCompilerInputs.targets");
            var outputPath = Path.Combine(captureDirectory.FullName, "compiler-inputs.txt");
            File.WriteAllText(targetPath,
                "<Project><Target Name='PitchMindCaptureFinalCompilerInputs' DependsOnTargets='ResolveReferences;CoreCompile'>" +
                "<WriteLinesToFile File=\"$(PitchMindCompilerInputOutput)\" " +
                "Lines=\"@(ReferencePathWithRefAssemblies-&gt;'%(Identity)|%(FullPath)|%(ReferenceAssembly)|%(AssemblyName)|%(OriginalProjectReferenceItemSpec)|%(DefiningProjectFullPath)')\" " +
                "Overwrite=\"true\" /></Target></Project>");

            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.GetDirectoryName(projectPath)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("msbuild");
            startInfo.ArgumentList.Add(projectPath);
            startInfo.ArgumentList.Add("-target:PitchMindCaptureFinalCompilerInputs");
            startInfo.ArgumentList.Add("-property:CustomAfterMicrosoftCommonTargets=" + targetPath);
            startInfo.ArgumentList.Add("-property:PitchMindCompilerInputOutput=" + outputPath);
            startInfo.ArgumentList.Add("-nologo");
            startInfo.ArgumentList.Add("-property:NuGetAudit=false");
            startInfo.ArgumentList.Add("-maxcpucount:1");
            startInfo.ArgumentList.Add("-nodeReuse:false");

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start final compiler-input capture.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"Final compiler-input capture timed out for '{projectPath}'.");
            }

            var output = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Final compiler-input capture failed for '{projectPath}': {error}{output}");
            }

            if (!File.Exists(outputPath))
            {
                throw new InvalidOperationException($"Final compiler-input capture did not produce '{outputPath}'.");
            }

            var previouslyCapturedCompilerInputs = project.Descendants()
                .Where(item => item.Name.LocalName == "TargetCompilerReference")
                .Select(item => CompilerInputSignature(
                    item.Attribute("Include")?.Value,
                    item.Attribute("FullPath")?.Value,
                    item.Attribute("ReferenceAssembly")?.Value,
                    item.Attribute("AssemblyName")?.Value,
                    item.Attribute("OriginalProjectReferenceItemSpec")?.Value,
                    item.Attribute("DefiningProjectFullPath")?.Value))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(outputPath))
            {
                var metadata = line.Split('|');
                var signature = CompilerInputSignature(metadata[0], metadata[1], metadata[2], metadata[3], metadata[4], metadata[5]);
                if (previouslyCapturedCompilerInputs.Contains(signature))
                {
                    continue;
                }

                var item = new XElement("TargetFinalCompilerReference", new XAttribute("Include", metadata[0]));
                var names = new[] { "FullPath", "ReferenceAssembly", "AssemblyName", "OriginalProjectReferenceItemSpec", "DefiningProjectFullPath" };
                for (var index = 1; index < metadata.Length && index <= names.Length; index++)
                {
                    if (!string.IsNullOrWhiteSpace(metadata[index]))
                    {
                        item.SetAttributeValue(names[index - 1], metadata[index]);
                    }
                }

                var projectReference = project.Descendants().FirstOrDefault(existing =>
                    existing.Name.LocalName == "TargetCompilerReference" &&
                    !string.IsNullOrWhiteSpace(metadata[3]) &&
                    string.Equals(existing.Attribute("AssemblyName")?.Value, metadata[3], StringComparison.Ordinal));
                if (projectReference is not null &&
                    string.IsNullOrWhiteSpace(item.Attribute("OriginalProjectReferenceItemSpec")?.Value) &&
                    !string.IsNullOrWhiteSpace(projectReference.Attribute("OriginalProjectReferenceItemSpec")?.Value))
                {
                    item.SetAttributeValue("OriginalProjectReferenceItemSpec", projectReference.Attribute("OriginalProjectReferenceItemSpec")!.Value);
                }

                project.Root!.Add(item);
            }
        }
        finally
        {
            captureDirectory.Delete(recursive: true);
        }
    }

    private static string CompilerInputSignature(params string?[] fields)
    {
        var normalized = fields.Select(field => field ?? string.Empty).ToArray();
        for (var index = 0; index < normalized.Length; index++)
        {
            if (index is 1 or 2 && !string.IsNullOrWhiteSpace(normalized[index]))
            {
                normalized[index] = Path.GetFullPath(normalized[index]);
            }
        }

        return string.Join('\u001f', normalized);
    }

    private static string TargetItemSignature(string itemType, XElement item)
    {
        var engineMetadata = new HashSet<string>(StringComparer.Ordinal)
        {
            "RootDir", "Filename", "Extension", "RelativeDir", "Directory", "RecursiveDir",
            "ModifiedTime", "CreatedTime", "AccessedTime", "DefiningProjectDirectory",
            "DefiningProjectName", "DefiningProjectExtension"
        };
        var identity = item.Attribute("Include")?.Value ?? string.Empty;
        return string.Join('\u001f', new[] { itemType, $"Include={identity}" }.Concat(item.Attributes()
            .Where(attribute => attribute.Name.LocalName != "Include" && !engineMetadata.Contains(attribute.Name.LocalName))
            .OrderBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal)
            .Select(attribute => $"{attribute.Name.LocalName}={attribute.Value}")));
    }

    private static bool IsWithinDirectory(string? path, string? directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var relative = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
        return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }

    private static HashSet<(string PackageId, string Version, string ProjectPath)> ReadRestoredProjectReferences(string projectPath)
    {
        var result = new HashSet<(string PackageId, string Version, string ProjectPath)>();
        var assetsPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
        {
            return result;
        }

        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        if (!assets.RootElement.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var library in libraries.EnumerateObject())
        {
            if (!library.Value.TryGetProperty("type", out var type) || type.GetString() != "project" ||
                !library.Value.TryGetProperty("msbuildProject", out var project) || project.GetString() is not { } projectReference)
            {
                continue;
            }

            var separator = library.Name.LastIndexOf('/');
            if (separator <= 0 || separator == library.Name.Length - 1)
            {
                continue;
            }

            result.Add((library.Name[..separator], library.Name[(separator + 1)..],
                Path.GetFullPath(projectReference, Path.GetDirectoryName(projectPath)!)));
        }

        return result;
    }

    private static bool IsSdkTransitiveProjectReference(
        string? definingProject,
        string? fullPath,
        string? packageId,
        string? packageVersion,
        string? sdkPath,
        HashSet<(string PackageId, string Version, string ProjectPath)> restoredProjectReferences)
    {
        if (string.IsNullOrWhiteSpace(sdkPath) || string.IsNullOrWhiteSpace(fullPath) ||
            string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(packageVersion) ||
            !string.Equals(Path.GetExtension(fullPath), ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expected = Path.GetFullPath(Path.Combine(sdkPath, "Microsoft.NET.Sdk", "targets", "Microsoft.PackageDependencyResolution.targets"));
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return !string.IsNullOrWhiteSpace(definingProject) && Path.GetFullPath(definingProject).Equals(expected, pathComparison) &&
               restoredProjectReferences.Contains((packageId, packageVersion, Path.GetFullPath(fullPath)));
    }

    private static void RestoreFixture(string projectPath)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--ignore-failed-sources");
        startInfo.ArgumentList.Add("-property:NuGetAudit=false");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not restore the target-reference fixture.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Fixture restore timed out for '{projectPath}'.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Fixture restore failed for '{projectPath}': {errorTask.GetAwaiter().GetResult()}");
        }

        _ = outputTask.GetAwaiter().GetResult();
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

        assets.RootElement.TryGetProperty("libraries", out var libraries);
        assets.RootElement.TryGetProperty("packageFolders", out var packageFolders);

        foreach (var target in targets.EnumerateObject())
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                if (!library.Value.TryGetProperty("type", out var type) || type.GetString() != "package")
                {
                    continue;
                }

                var packagePath = libraries.ValueKind == JsonValueKind.Object &&
                                  libraries.TryGetProperty(library.Name, out var package) &&
                                  package.TryGetProperty("path", out var path)
                    ? path.GetString()
                    : null;

                if (library.Value.TryGetProperty("compile", out var compile) && compile.ValueKind == JsonValueKind.Object)
                {
                    foreach (var assembly in compile.EnumerateObject())
                    {
                        project.Root!.Add(CreatePackageAssembly(assembly.Name, packagePath, packageFolders));
                    }
                }

                foreach (var runtimeGroup in library.Value.EnumerateObject().Where(property =>
                             (property.Name is "runtime" or "runtimeTargets" or "native") &&
                             property.Value.ValueKind == JsonValueKind.Object))
                {
                    foreach (var assembly in runtimeGroup.Value.EnumerateObject().Where(asset =>
                                 asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                    {
                        project.Root!.Add(CreatePackageAssembly(assembly.Name, packagePath, packageFolders));
                    }
                }
            }
        }
    }

    private static XElement CreatePackageAssembly(string assetPath, string? packagePath, JsonElement packageFolders)
    {
        var element = new XElement("PackageAssembly", new XAttribute("Include", assetPath));
        if (packagePath is null || packageFolders.ValueKind != JsonValueKind.Object)
        {
            return element;
        }

        foreach (var folder in packageFolders.EnumerateObject())
        {
            var resolvedPath = Path.GetFullPath(Path.Combine(folder.Name, packagePath, assetPath));
            if (File.Exists(resolvedPath))
            {
                element.SetAttributeValue("ResolvedPath", resolvedPath);
                break;
            }
        }

        return element;
    }
}

internal static class ArchitectureDependencyPolicy
{
    public static IReadOnlyList<string> ValidateDomainReferences(XDocument domain)
    {
        var violations = new List<string>();
        foreach (var item in domain.Descendants())
        {
            var itemType = item.Name.LocalName;
            var forbiddenResolvedInput = (itemType is "TargetResolvedReference" or "TargetCompilerReference" or "TargetFinalCompilerReference") &&
                                         !IsTrustedSdkFrameworkInput(domain, item);
            var forbidden = itemType is "PackageReference" or "TargetPackageReference" or "Reference" or "TargetReference" or
                "TargetFrameworkReference" or "COMReference" or "TargetCOMReference" or "NativeReference" or "TargetNativeReference" ||
                itemType == "FrameworkReference" && !IsSdkImplicitFrameworkReference(domain, item);
            if (!forbidden && !forbiddenResolvedInput)
            {
                continue;
            }

            violations.Add($"Domain project has forbidden {item.Name.LocalName} '{item.Attribute("Include")?.Value}'.");
        }

        return violations;
    }

    private static bool IsTrustedSdkFrameworkInput(XDocument project, XElement item)
    {
        var sdkPath = project.Root?.Attribute("MSBuildSDKsPath")?.Value;
        var definingProject = item.Attribute("DefiningProjectFullPath")?.Value;
        var fullPath = item.Attribute("FullPath")?.Value ?? item.Attribute("Include")?.Value;
        if (!IsTrustedSdkFrameworkAssembly(definingProject, fullPath, sdkPath))
        {
            return false;
        }

        var referenceAssembly = item.Attribute("ReferenceAssembly")?.Value;
        return string.IsNullOrWhiteSpace(referenceAssembly) ||
               IsTrustedSdkFrameworkAssembly(definingProject, referenceAssembly, sdkPath);
    }

    public static bool IsTrustedSdkFrameworkAssembly(string? definingProject, string? fullPath, string? sdkPath)
    {
        if (string.IsNullOrWhiteSpace(sdkPath) || string.IsNullOrWhiteSpace(definingProject) || string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        var sdkRoot = Directory.GetParent(sdkPath);
        var dotnetRoot = sdkRoot?.Parent?.Parent?.FullName;
        if (dotnetRoot is null || string.IsNullOrWhiteSpace(sdkRoot?.FullName) ||
            !IsPathWithinDirectory(definingProject, sdkRoot.FullName))
        {
            return false;
        }

        var frameworkPackRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        if (!IsPathWithinDirectory(fullPath, frameworkPackRoot) || !File.Exists(fullPath))
        {
            return false;
        }

        try
        {
            return AssemblyName.GetAssemblyName(fullPath).Name?.StartsWith("PitchMind.", StringComparison.Ordinal) != true;
        }
        catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or IOException)
        {
            return false;
        }
    }

    private static bool IsSdkImplicitFrameworkReference(XDocument project, XElement item)
    {
        if (!string.Equals(item.Attribute("Include")?.Value, "Microsoft.NETCore.App", StringComparison.Ordinal) ||
            !string.Equals(item.Attribute("IsImplicitlyDefined")?.Value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sdkPath = project.Root?.Attribute("MSBuildSDKsPath")?.Value;
        var definingProject = item.Attribute("DefiningProjectFullPath")?.Value;
        if (string.IsNullOrWhiteSpace(sdkPath) || string.IsNullOrWhiteSpace(definingProject))
        {
            return false;
        }

        var expected = Path.GetFullPath(Path.Combine(sdkPath, "Microsoft.NET.Sdk", "targets", "Microsoft.NET.Sdk.DefaultItems.props"));
        return Path.GetFullPath(definingProject).Equals(expected,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    public static IReadOnlyList<string> ValidateInternalReferences(
        IReadOnlyDictionary<string, XDocument> projects,
        IReadOnlyDictionary<string, string[]>? allowedProjectReferences = null)
    {
        var violations = new List<string>();

        foreach (var (project, document) in projects)
        {
            var projectPath = document.Root?.Attribute("ProjectPath")?.Value;
            var projectDirectory = string.IsNullOrWhiteSpace(projectPath) ? null : Path.GetDirectoryName(projectPath);
            foreach (var item in document.Descendants().Where(element =>
                         element.Name.LocalName is "Reference" or "TargetReference" or "TargetResolvedReference" or "TargetCompilerReference" or "TargetFinalCompilerReference" or "PackageReference" or "PackageAssembly" or
                             "TargetPackageReference" or "COMReference" or "TargetCOMReference" or "NativeReference" or "TargetNativeReference"))
            {
                if ((item.Name.LocalName is "TargetResolvedReference" or "TargetCompilerReference" or "TargetFinalCompilerReference") &&
                    IsMappedProjectAssembly(project, item, document, projectDirectory, allowedProjectReferences))
                {
                    continue;
                }

                var include = item.Attribute("Include")?.Value;
                var candidates = new[]
                    {
                        include,
                        item.Attribute("HintPath")?.Value,
                        item.Attribute("FullPath")?.Value,
                        item.Attribute("ResolvedPath")?.Value,
                        item.Attribute("ReferenceAssembly")?.Value,
                        item.Element("HintPath")?.Value,
                        ResolveProjectRelativePath(item.Attribute("HintPath")?.Value, projectDirectory),
                        ResolveProjectRelativePath(item.Attribute("FullPath")?.Value, projectDirectory),
                        ResolveProjectRelativePath(item.Attribute("ResolvedPath")?.Value, projectDirectory),
                        ResolveProjectRelativePath(item.Attribute("ReferenceAssembly")?.Value, projectDirectory),
                        ResolveProjectRelativePath(item.Element("HintPath")?.Value, projectDirectory)
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

    private static bool IsMappedProjectAssembly(
        string sourceProject,
        XElement item,
        XDocument document,
        string? projectDirectory,
        IReadOnlyDictionary<string, string[]>? allowedProjectReferences)
    {
        var assemblyPath = item.Attribute("FullPath")?.Value ?? item.Attribute("Include")?.Value;
        var assemblyName = item.Attribute("AssemblyName")?.Value ??
            (string.IsNullOrWhiteSpace(assemblyPath) ? null : Path.GetFileNameWithoutExtension(assemblyPath));
        var projectReference = item.Attribute("OriginalProjectReferenceItemSpec")?.Value;
        if (string.IsNullOrWhiteSpace(assemblyName) || string.IsNullOrWhiteSpace(projectReference) ||
            !string.Equals(Path.GetFileNameWithoutExtension(projectReference), assemblyName, StringComparison.Ordinal))
        {
            return false;
        }

        if (allowedProjectReferences is null || !allowedProjectReferences.ContainsKey(sourceProject))
        {
            return false;
        }

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(allowedProjectReferences[sourceProject]);
        while (pending.TryPop(out var projectName))
        {
            if (!reachable.Add(projectName) || !allowedProjectReferences.TryGetValue(projectName, out var dependencies))
            {
                continue;
            }

            foreach (var dependency in dependencies)
            {
                pending.Push(dependency);
            }
        }

        if (!reachable.Contains(assemblyName))
        {
            return false;
        }

        var sourcePath = assemblyPath;
        var sourceDocumentDirectory = projectDirectory;
        var sourceProjectRoot = sourceDocumentDirectory is null ? null : Path.GetDirectoryName(sourceDocumentDirectory);
        var repositoryRoot = sourceProjectRoot is null ? null : Path.GetDirectoryName(sourceProjectRoot);
        if (repositoryRoot is null || string.IsNullOrWhiteSpace(sourcePath))
        {
            return false;
        }

        var expectedProjectDirectory = Path.Combine(repositoryRoot, "src", assemblyName);
        var expectedProjectPath = Path.Combine(expectedProjectDirectory, $"{assemblyName}.csproj");
        var resolvedProjectReference = ResolveProjectRelativePath(projectReference, projectDirectory);
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var allowedOutputDirectory = item.Name.LocalName == "TargetCompilerReference" ? "obj" : "bin";
        if (resolvedProjectReference is null || !Path.GetFullPath(resolvedProjectReference).Equals(Path.GetFullPath(expectedProjectPath), pathComparison) ||
            !IsPathWithinDirectory(sourcePath, Path.Combine(expectedProjectDirectory, allowedOutputDirectory)))
        {
            return false;
        }

        try
        {
            if (!string.Equals(AssemblyName.GetAssemblyName(sourcePath).Name, assemblyName, StringComparison.Ordinal))
            {
                return false;
            }

            var referenceAssembly = item.Attribute("ReferenceAssembly")?.Value;
            return string.IsNullOrWhiteSpace(referenceAssembly) ||
                   (IsPathWithinDirectory(referenceAssembly, Path.Combine(expectedProjectDirectory, "obj")) &&
                    string.Equals(AssemblyName.GetAssemblyName(referenceAssembly).Name, assemblyName, StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or IOException)
        {
            return false;
        }
    }

    private static bool IsPathWithinDirectory(string? path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var relative = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
        return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }

    private static string? ResolveProjectRelativePath(string? value, string? projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(projectDirectory))
        {
            return value;
        }

        try
        {
            return Path.GetFullPath(value, projectDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return value;
        }
    }

    private static bool IsInternalAssemblyReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var rawValue = value.Trim();
        if (File.Exists(rawValue))
        {
            try
            {
                if (AssemblyName.GetAssemblyName(rawValue).Name?.StartsWith("PitchMind.", StringComparison.Ordinal) == true)
                {
                    return true;
                }
            }
            catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or IOException)
            {
                // Not every HintPath or package asset points to a managed assembly.
            }
        }

        var identity = rawValue.Split(',', 2)[0].Trim().Replace('\\', '/');
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
