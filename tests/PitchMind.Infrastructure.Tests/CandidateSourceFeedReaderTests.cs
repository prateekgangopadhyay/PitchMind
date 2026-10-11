using System.Security.Cryptography;
using System.Text;
using PitchMind.Infrastructure.Simulation.Candidates;
using Xunit;

namespace PitchMind.Infrastructure.Tests;

public sealed class CandidateSourceFeedReaderTests
{
    [Fact]
    public async Task ReadsRepositoryCandidateWithPinnedHashAndRetainsEveryRawRecord()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(repositoryRoot, "data", "simulations", "manu-liv-match-dataset.json");
        var manifestPath = Path.Combine(repositoryRoot, "data", "simulations", "manu-liv-candidate-manifest.json");
        var originalBytes = await File.ReadAllBytesAsync(sourcePath);
        using var originalDocument = System.Text.Json.JsonDocument.Parse(originalBytes);

        var feed = await new CandidateSourceFeedReader().ReadAsync(sourcePath, manifestPath);
        var rawEvents = originalDocument.RootElement.EnumerateArray().Select(item => item.GetRawText()).ToArray();

        Assert.False(feed.IsCanonical);
        Assert.Equal("repository-owner-confirmed", feed.Provenance);
        Assert.Equal("repository-owner-confirmed", feed.RedistributionPermission);
        Assert.False(feed.CoordinateSemanticsVerified);
        Assert.False(feed.PlayerMetricsValidated);
        Assert.False(feed.CanonicalMappingsApproved);
        Assert.Equal(2_199, feed.Events.Count);
        Assert.Equal(rawEvents, feed.Events.Select(item => item.RawJson));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(originalBytes)), feed.SourceSha256);
    }

    [Fact]
    public async Task ReadsPinnedCandidateAndReplaysRawEnvelopesInSourceOrder()
    {
        const string first = "{ \"eventId\":\"event-a\", \"matchId\":\"match-a\", \"teamId\":\"team-a\", \"sequence\":1, \"clockSeconds\":0, \"period\":1, \"eventType\":\"Pass\", \"pitchX\":1.2500, \"pitchY\":9e-1, \"playerPositions\":[{\"heartRate\":123}] }";
        const string second = "{\"eventId\":\"event-b\",\"matchId\":\"match-a\",\"teamId\":\"team-b\",\"sequence\":2,\"clockSeconds\":7,\"period\":1,\"eventType\":\"Goal\",\"pitchX\":98.00}";
        var fixture = await Fixture.CreateAsync($"[\n {first},\n {second}\n]");
        try
        {
            var feed = await new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath);
            var replay = new List<CandidateSourceEnvelope>();
            await foreach (var item in feed.ReplayFromSourceSequenceAsync(1)) replay.Add(item);

            Assert.False(feed.IsCanonical);
            Assert.Equal("candidate_not_canonical", feed.Disposition);
            Assert.Equal(fixture.SourceSha256, feed.SourceSha256);
            Assert.Equal(new long[] { 1, 2 }, replay.Select(item => item.SourceSequence));
            Assert.Equal(new[] { first, second }, replay.Select(item => item.RawJson));
            Assert.Equal("1.2500", RawProperty(replay[0].RawJson, "pitchX"));
            Assert.Equal("9e-1", RawProperty(replay[0].RawJson, "pitchY"));
            Assert.Equal("123", RawNestedProperty(replay[0].RawJson, "playerPositions", "heartRate"));
            Assert.Equal("0", replay[0].RawClockSeconds);
            Assert.Equal("1", replay[0].RawPeriod);
            Assert.Equal(new[] { "event-b" }, (await Collect(feed.ReplayFromSourceSequenceAsync(2))).Select(item => item.EventId));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task RejectsSourceBytesThatDoNotMatchManifestPin()
    {
        var fixture = await Fixture.CreateAsync("[{\"eventId\":\"e\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":0,\"period\":1,\"eventType\":\"Pass\"}]");
        try
        {
            await File.AppendAllTextAsync(fixture.SourcePath, " ");
            await Assert.ThrowsAsync<InvalidDataException>(() => new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task RejectsDuplicateSourceEventIds()
    {
        var fixture = await Fixture.CreateAsync("[{\"eventId\":\"same\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":0,\"period\":1,\"eventType\":\"Pass\"},{\"eventId\":\"same\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":2,\"clockSeconds\":1,\"period\":1,\"eventType\":\"Pass\"}]");
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task RejectsMixedMatchIds()
    {
        var fixture = await Fixture.CreateAsync("[{\"eventId\":\"a\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":0,\"period\":1,\"eventType\":\"Pass\"},{\"eventId\":\"b\",\"matchId\":\"other\",\"teamId\":\"team-a\",\"sequence\":2,\"clockSeconds\":1,\"period\":1,\"eventType\":\"Pass\"}]");
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"teamId\":null,")]
    [InlineData("\"teamId\":\"   \",")]
    [InlineData("\"teamId\":42,")]
    public async Task RejectsMissingOrMalformedTeamIdOnAnyEvent(string teamProperty)
    {
        var invalidTeam = teamProperty.Length == 0 ? "" : teamProperty;
        var source = $"[{{\"eventId\":\"a\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":0,\"period\":1,\"eventType\":\"Pass\"}},"
            + $"{{\"eventId\":\"b\",\"matchId\":\"m\",\"teamId\":\"team-b\",\"sequence\":2,\"clockSeconds\":1,\"period\":1,\"eventType\":\"Pass\"}},"
            + $"{{\"eventId\":\"c\",\"matchId\":\"m\",{invalidTeam}\"sequence\":3,\"clockSeconds\":2,\"period\":1,\"eventType\":\"Pass\"}}]";
        var fixture = await Fixture.CreateAsync(source);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task RejectsInRangeClockRegressionInSourceSequence()
    {
        var fixture = await Fixture.CreateAsync("[{\"eventId\":\"a\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":100,\"period\":1,\"eventType\":\"Pass\"},{\"eventId\":\"b\",\"matchId\":\"m\",\"teamId\":\"team-b\",\"sequence\":2,\"clockSeconds\":99,\"period\":1,\"eventType\":\"Pass\"}]");
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task RejectsManifestAboveBoundedSize()
    {
        var fixture = await Fixture.CreateAsync("[{\"eventId\":\"e\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":0,\"period\":1,\"eventType\":\"Pass\"}]");
        try
        {
            await using (var manifest = new FileStream(fixture.ManifestPath, FileMode.Open, FileAccess.Write))
                manifest.SetLength(262_145);
            await Assert.ThrowsAsync<InvalidDataException>(() => new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task RejectsSourceAboveBoundedSize()
    {
        var fixture = await Fixture.CreateAsync("[{\"eventId\":\"e\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":0,\"period\":1,\"eventType\":\"Pass\"}]");
        try
        {
            await using (var source = new FileStream(fixture.SourcePath, FileMode.Open, FileAccess.Write))
                source.SetLength(50_000_001);
            await Assert.ThrowsAsync<InvalidDataException>(() => new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task RejectsGrowthAfterOpenWithOnlyOneOverflowByteRead()
    {
        const int maximumBytes = 8;
        var stream = new GrowthAfterOpenStream(Encoding.UTF8.GetBytes(new string('x', 1_024)), initialLength: 4);

        await Assert.ThrowsAsync<InvalidDataException>(() => CandidateSourceFeedReader.ReadBoundedBytesAsync(
            stream,
            maximumBytes,
            "Candidate source",
            CancellationToken.None));

        Assert.Equal(maximumBytes + 1, stream.BytesReturned);
    }

    [Fact]
    public async Task IncrementalSourceReaderRejectsGrowthAfterInitialLengthCheckAtCapPlusOne()
    {
        const int maximumBytes = 8;
        var source = new GrowthAfterOpenStream(Encoding.UTF8.GetBytes(new string('x', 1_024)), initialLength: 4);
        using var bounded = new CandidateSourceFeedReader.BoundedHashingReadStream(source, maximumBytes);
        var buffer = new byte[32];

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            while (await bounded.ReadAsync(buffer.AsMemory()) != 0) { }
        });

        Assert.Equal(maximumBytes + 1, source.BytesReturned);
    }

    [Fact]
    public async Task RejectsEventNestingBeyondTheExplicitJsonDepthLimit()
    {
        var nested = "1";
        for (var depth = 0; depth < 65; depth++) nested = "{\"child\":" + nested + "}";
        var source = "[{\"eventId\":\"e\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":0,\"period\":1,\"eventType\":\"Pass\",\"nested\":" + nested + "]";
        var fixture = await Fixture.CreateAsync(source, eventCountOverride: 1, teamCountOverride: 1);
        try
        {
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task RejectsNonWhitespaceAfterTheEventArray()
    {
        var fixture = await Fixture.CreateAsync("[{\"eventId\":\"e\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":0,\"period\":1,\"eventType\":\"Pass\"}]");
        try
        {
            await File.AppendAllTextAsync(fixture.SourcePath, "false");
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Theory]
    [InlineData(5762, 1)]
    [InlineData(-1, 1)]
    [InlineData(0, 99)]
    [InlineData(0, -1)]
    public async Task RejectsSourceTimeOutsideManifestBounds(int clockSeconds, int period)
    {
        var source = $"[{{\"eventId\":\"e\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":{clockSeconds},\"period\":{period},\"eventType\":\"Pass\"}}]";
        var fixture = await Fixture.CreateAsync(source);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task RejectsUnsupportedManifestVersion()
    {
        var fixture = await Fixture.CreateAsync("[{\"eventId\":\"e\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":0,\"period\":1,\"eventType\":\"Pass\"}]");
        try
        {
            var manifest = await File.ReadAllTextAsync(fixture.ManifestPath);
            await File.WriteAllTextAsync(fixture.ManifestPath, manifest.Replace("\"manifestVersion\":\"1.0.0\"", "\"manifestVersion\":\"2.0.0\"", StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidDataException>(() => new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task RejectsManifestEventCountThatDoesNotMatchTheSource()
    {
        var fixture = await Fixture.CreateAsync("[{\"eventId\":\"e\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":0,\"period\":1,\"eventType\":\"Pass\"}]");
        try
        {
            var manifest = await File.ReadAllTextAsync(fixture.ManifestPath);
            await File.WriteAllTextAsync(fixture.ManifestPath, manifest.Replace("\"requiredEventCounts\":{}", "\"requiredEventCounts\":{\"Pass\":2}", StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidDataException>(() => new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task RejectsManifestEventMarkerWithWrongClockOrPeriod()
    {
        var fixture = await Fixture.CreateAsync("[{\"eventId\":\"e\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":0,\"period\":1,\"eventType\":\"Pass\"}]");
        try
        {
            var manifest = await File.ReadAllTextAsync(fixture.ManifestPath);
            await File.WriteAllTextAsync(fixture.ManifestPath, manifest.Replace("\"requiredEventMarkers\":{}", "\"requiredEventMarkers\":{\"Pass\":{\"clockSeconds\":1,\"period\":1}}", StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidDataException>(() => new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task RejectsDuplicateSourcePropertiesInsteadOfSelectingAnAmbiguousValue()
    {
        var fixture = await Fixture.CreateAsync("[{\"eventId\":\"e\",\"eventId\":\"other\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":0,\"period\":1,\"eventType\":\"Pass\"}]");
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task RejectsManifestThatWouldPromoteUnreviewedCoordinatesOrMappings()
    {
        var fixture = await Fixture.CreateAsync("[{\"eventId\":\"e\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":0,\"period\":1,\"eventType\":\"Pass\"}]");
        try
        {
            var manifest = await File.ReadAllTextAsync(fixture.ManifestPath);
            await File.WriteAllTextAsync(fixture.ManifestPath, manifest.Replace("\"mappingStatus\":\"not_approved\"", "\"mappingStatus\":\"approved\"", StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidDataException>(() => new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task CancellationStopsReplay()
    {
        var fixture = await Fixture.CreateAsync("[{\"eventId\":\"e\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":0,\"period\":1,\"eventType\":\"Pass\"}]");
        try
        {
            var feed = await new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in feed.ReplayFromSourceSequenceAsync(1, cancellation.Token)) { }
            });
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task CancellationStopsSourceParsingWhileWaitingForMoreBytes()
    {
        var fixture = await Fixture.CreateAsync("[{\"eventId\":\"e\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":0,\"period\":1,\"eventType\":\"Pass\"}]");
        try
        {
            var sourceBytes = await File.ReadAllBytesAsync(fixture.SourcePath);
            var manifestBytes = await File.ReadAllBytesAsync(fixture.ManifestPath);
            await using var source = new BlockAfterFirstByteStream(sourceBytes);
            using var cancellation = new CancellationTokenSource();

            var read = new CandidateSourceFeedReader().ReadAsync(
                source,
                sourceBytes.Length,
                manifestBytes,
                cancellation.Token);

            await source.SecondReadStarted.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static string RawProperty(string rawJson, string name)
    {
        using var document = System.Text.Json.JsonDocument.Parse(rawJson);
        return document.RootElement.GetProperty(name).GetRawText();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "data", "simulations", "manu-liv-match-dataset.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository candidate fixture from the test output directory.");
    }

    private static string RawNestedProperty(string rawJson, string arrayName, string propertyName)
    {
        using var document = System.Text.Json.JsonDocument.Parse(rawJson);
        return document.RootElement.GetProperty(arrayName)[0].GetProperty(propertyName).GetRawText();
    }

    private static async Task<List<CandidateSourceEnvelope>> Collect(IAsyncEnumerable<CandidateSourceEnvelope> source)
    {
        var result = new List<CandidateSourceEnvelope>();
        await foreach (var item in source) result.Add(item);
        return result;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory;
        private Fixture(string directory, string sourcePath, string manifestPath, string sourceSha256)
        {
            _directory = directory;
            SourcePath = sourcePath;
            ManifestPath = manifestPath;
            SourceSha256 = sourceSha256;
        }

        public string SourcePath { get; }
        public string ManifestPath { get; }
        public string SourceSha256 { get; }

        public static async Task<Fixture> CreateAsync(string source, int eventCountOverride = -1, int teamCountOverride = -1)
        {
            var directory = Path.Combine(Path.GetTempPath(), "pitchmind-candidate-reader-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var sourcePath = Path.Combine(directory, "source.json");
            var manifestPath = Path.Combine(directory, "manifest.json");
            var bytes = Encoding.UTF8.GetBytes(source);
            var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            await File.WriteAllBytesAsync(sourcePath, bytes);
            var eventCount = eventCountOverride >= 0 ? eventCountOverride : CountEvents(source);
            var teamCount = teamCountOverride >= 0 ? teamCountOverride : CountTeams(source);
            await File.WriteAllTextAsync(manifestPath, Manifest(sha, bytes.Length, eventCount, teamCount));
            return new Fixture(directory, sourcePath, manifestPath, sha);
        }

        private static int CountEvents(string source)
        {
            using var document = System.Text.Json.JsonDocument.Parse(source);
            return document.RootElement.GetArrayLength();
        }

        private static int CountTeams(string source)
        {
            using var document = System.Text.Json.JsonDocument.Parse(source);
            return document.RootElement.EnumerateArray()
                .Select(item => item.TryGetProperty("teamId", out var teamId) && teamId.ValueKind == System.Text.Json.JsonValueKind.String
                    ? teamId.GetString()
                    : null)
                .Where(teamId => !string.IsNullOrWhiteSpace(teamId))
                .Distinct(StringComparer.Ordinal)
                .Count();
        }

        private static string Manifest(string sha, int size, int count, int teamCount) => $$"""
        {
          "manifestVersion":"1.0.0",
          "status":"candidate_not_canonical",
          "fixtureId":"test-candidate",
          "source":{"sha256":"{{sha}}","byteSize":{{size}},"expectedEventCount":{{count}},"sequenceStart":1,"sequenceEnd":{{count}},"synthetic":true,"provenance":"repository-owner-confirmed","redistributionPermission":"repository-owner-confirmed"},
          "match":{"expectedMatchCount":1,"expectedTeamCount":{{teamCount}},"expectedPeriods":[1,2],"requiredEventCounts":{},"requiredEventMarkers":{},"sourceClockStart":0,"sourceClockEnd":5761},
          "coordinates":{"status":"unverified","unit":null,"axisConvention":null,"pitchDimensionsMetres":null},
          "playerMetrics":{"status":"unvalidated","unitsAndValidity":"unverified","mayBePresentedAsObservedPerformance":false},
          "canonicalization":{"mappingStatus":"not_approved","mayCreateCanonicalEvents":false}
        }
        """;

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }

    private sealed class GrowthAfterOpenStream(byte[] content, long initialLength) : MemoryStream(content, writable: false)
    {
        public int BytesReturned { get; private set; }
        public override long Length => initialLength;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = base.Read(buffer.Span);
            BytesReturned += count;
            return ValueTask.FromResult(count);
        }
    }

    private sealed class BlockAfterFirstByteStream(byte[] content) : Stream
    {
        private readonly TaskCompletionSource _secondReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readCount;
        private bool _firstByteRead;

        public Task SecondReadStarted => _secondReadStarted.Task;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => content.Length;
        public override long Position { get => _firstByteRead ? 1 : 0; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _readCount) == 1)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (buffer.IsEmpty) return 0;
                buffer.Span[0] = content[0];
                _firstByteRead = true;
                return 1;
            }

            _secondReadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadArrayAsync(buffer, offset, count, cancellationToken);

        private async Task<int> ReadArrayAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(Span<byte> buffer) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
