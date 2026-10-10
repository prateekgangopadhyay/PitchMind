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
            var replayOptions = new CandidateReplayOptions(CandidateReplayOptions.MaximumEventsPerSecond);
            await foreach (var item in feed.ReplayFromSourceSequenceAsync(1, replayOptions)) replay.Add(item);

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
            Assert.Equal(new[] { "event-b" }, (await Collect(feed.ReplayFromSourceSequenceAsync(2, replayOptions))).Select(item => item.EventId));
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
                await foreach (var _ in feed.ReplayFromSourceSequenceAsync(
                    1,
                    new CandidateReplayOptions(CandidateReplayOptions.MaximumEventsPerSecond),
                    cancellation.Token)) { }
            });
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task RateLimitedReplayRetainsSourceOrderAndWaitsBetweenEvents()
    {
        const string first = "{\"eventId\":\"event-a\",\"matchId\":\"match-a\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":0,\"period\":1,\"eventType\":\"Pass\"}";
        const string second = "{\"eventId\":\"event-b\",\"matchId\":\"match-a\",\"teamId\":\"team-b\",\"sequence\":2,\"clockSeconds\":1,\"period\":1,\"eventType\":\"Pass\"}";
        const string third = "{\"eventId\":\"event-c\",\"matchId\":\"match-a\",\"teamId\":\"team-a\",\"sequence\":3,\"clockSeconds\":2,\"period\":1,\"eventType\":\"Goal\"}";
        const string fourth = "{\"eventId\":\"event-d\",\"matchId\":\"match-a\",\"teamId\":\"team-b\",\"sequence\":4,\"clockSeconds\":3,\"period\":1,\"eventType\":\"Pass\"}";
        var fixture = await Fixture.CreateAsync($"[{first},{second},{third},{fourth}]");
        try
        {
            var feed = await new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath);
            var clock = new ManualTimeProvider();
            await using var replay = feed.ReplayFromSourceSequenceAsync(
                2,
                new CandidateReplayOptions(20, clock)).GetAsyncEnumerator();

            var firstMove = replay.MoveNextAsync();
            Assert.True(firstMove.IsCompletedSuccessfully, "The first eligible source envelope should be yielded without a pacing delay.");
            Assert.True(await firstMove);
            Assert.Equal(2, replay.Current.SourceSequence);
            Assert.Equal(second, replay.Current.RawJson);
            Assert.Equal(TimeSpan.Zero, clock.Elapsed);

            var secondMove = replay.MoveNextAsync();
            Assert.False(secondMove.IsCompleted, "Subsequent source envelopes should wait for the configured interval.");
            await clock.WaitForScheduledTimerAsync();
            Assert.Equal(TimeSpan.FromMilliseconds(50), clock.TimeUntilNextTimer);
            clock.Advance(TimeSpan.FromMilliseconds(49));
            Assert.False(secondMove.IsCompleted, "The next envelope must not be yielded before its interval elapses.");
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.True(await secondMove);
            Assert.Equal(3, replay.Current.SourceSequence);
            Assert.Equal(third, replay.Current.RawJson);
            Assert.Equal(TimeSpan.FromMilliseconds(50), clock.Elapsed);

            var thirdMove = replay.MoveNextAsync();
            Assert.False(thirdMove.IsCompleted);
            await clock.WaitForScheduledTimerAsync();
            Assert.Equal(TimeSpan.FromMilliseconds(50), clock.TimeUntilNextTimer);
            clock.Advance(TimeSpan.FromMilliseconds(49));
            Assert.False(thirdMove.IsCompleted, "The third source envelope must receive its own pacing interval.");
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.True(await thirdMove);
            Assert.Equal(4, replay.Current.SourceSequence);
            Assert.Equal(fourth, replay.Current.RawJson);
            Assert.Equal(TimeSpan.FromMilliseconds(100), clock.Elapsed);
            Assert.False(await replay.MoveNextAsync());
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task MinimumReplayRateUsesTheBoundedHundredSecondInterval()
    {
        var fixture = await Fixture.CreateAsync("[{\"eventId\":\"a\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":0,\"period\":1,\"eventType\":\"Pass\"},{\"eventId\":\"b\",\"matchId\":\"m\",\"teamId\":\"team-b\",\"sequence\":2,\"clockSeconds\":1,\"period\":1,\"eventType\":\"Pass\"}]");
        try
        {
            var feed = await new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath);
            var clock = new ManualTimeProvider();
            await using var replay = feed.ReplayFromSourceSequenceAsync(
                1,
                new CandidateReplayOptions(CandidateReplayOptions.MinimumEventsPerSecond, clock)).GetAsyncEnumerator();

            var firstMove = replay.MoveNextAsync();
            Assert.True(firstMove.IsCompletedSuccessfully);
            Assert.True(await firstMove);
            Assert.Equal(1, replay.Current.SourceSequence);
            Assert.Equal(TimeSpan.Zero, clock.Elapsed);

            var secondMove = replay.MoveNextAsync();
            Assert.False(secondMove.IsCompleted);
            await clock.WaitForScheduledTimerAsync();
            Assert.Equal(TimeSpan.FromSeconds(100), clock.TimeUntilNextTimer);
            clock.Advance(TimeSpan.FromSeconds(99) + TimeSpan.FromMilliseconds(999));
            Assert.False(secondMove.IsCompleted);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.True(await secondMove);
            Assert.Equal(2, replay.Current.SourceSequence);
            Assert.Equal(TimeSpan.FromSeconds(100), clock.Elapsed);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1000.01)]
    public void RejectsReplayRatesOutsideTheBoundedRange(double eventsPerSecond)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CandidateReplayOptions(eventsPerSecond));
    }

    [Fact]
    public async Task CancellationStopsRateLimitedReplayDuringInterEventWait()
    {
        var fixture = await Fixture.CreateAsync("[{\"eventId\":\"a\",\"matchId\":\"m\",\"teamId\":\"team-a\",\"sequence\":1,\"clockSeconds\":0,\"period\":1,\"eventType\":\"Pass\"},{\"eventId\":\"b\",\"matchId\":\"m\",\"teamId\":\"team-b\",\"sequence\":2,\"clockSeconds\":1,\"period\":1,\"eventType\":\"Pass\"}]");
        try
        {
            var feed = await new CandidateSourceFeedReader().ReadAsync(fixture.SourcePath, fixture.ManifestPath);
            using var cancellation = new CancellationTokenSource();
            await using var replay = feed.ReplayFromSourceSequenceAsync(
                1,
                new CandidateReplayOptions(0.1),
                cancellation.Token).GetAsyncEnumerator();

            Assert.True(await replay.MoveNextAsync());
            Assert.Equal(1, replay.Current.SourceSequence);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await replay.MoveNextAsync(); });
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

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private long _timestampTicks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestampTicks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_timestampTicks);
        public TimeSpan Elapsed => TimeSpan.FromTicks(_timestampTicks);

        public TimeSpan TimeUntilNextTimer
        {
            get
            {
                lock (_gate)
                {
                    var nextDue = _timers.Where(timer => timer.DueTimestamp is not null)
                        .Select(timer => timer.DueTimestamp!.Value).DefaultIfEmpty(long.MaxValue).Min();
                    return nextDue == long.MaxValue ? Timeout.InfiniteTimeSpan : TimeSpan.FromTicks(nextDue - _timestampTicks);
                }
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            lock (_gate) _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
            List<ManualTimer> due;
            lock (_gate)
            {
                _timestampTicks += elapsed.Ticks;
                due = _timers.Where(timer => timer.DueTimestamp is { } dueAt && dueAt <= _timestampTicks).ToList();
            }

            foreach (var timer in due)
                timer.FireIfDue(_timestampTicks);
        }

        public async Task WaitForScheduledTimerAsync()
        {
            for (var attempt = 0; attempt < 100 && TimeUntilNextTimer == Timeout.InfiniteTimeSpan; attempt++)
                await Task.Yield();

            Assert.NotEqual(Timeout.InfiniteTimeSpan, TimeUntilNextTimer);
        }

        private void SetDueTimestamp(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                timer.Period = period;
                timer.DueTimestamp = dueTime == Timeout.InfiniteTimeSpan
                    ? null
                    : _timestampTicks + Math.Max(0, dueTime.Ticks);
            }
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            public long? DueTimestamp { get; set; }
            public TimeSpan Period { get; set; }
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed) return false;
                owner.SetDueTimestamp(this, dueTime, period);
                return true;
            }

            public void Dispose()
            {
                _disposed = true;
                DueTimestamp = null;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void FireIfDue(long now)
            {
                if (_disposed || DueTimestamp is not { } dueAt || dueAt > now) return;
                DueTimestamp = Period == Timeout.InfiniteTimeSpan ? null : now + Period.Ticks;
                callback(state);
            }
        }
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

        public static async Task<Fixture> CreateAsync(string source)
        {
            var directory = Path.Combine(Path.GetTempPath(), "pitchmind-candidate-reader-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var sourcePath = Path.Combine(directory, "source.json");
            var manifestPath = Path.Combine(directory, "manifest.json");
            var bytes = Encoding.UTF8.GetBytes(source);
            var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            await File.WriteAllBytesAsync(sourcePath, bytes);
            await File.WriteAllTextAsync(manifestPath, Manifest(sha, bytes.Length, CountEvents(source), CountTeams(source)));
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
}
