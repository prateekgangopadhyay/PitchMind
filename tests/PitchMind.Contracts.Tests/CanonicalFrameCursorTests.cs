using PitchMind.Api;
using PitchMind.Application.Abstractions;
using System.Text;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class CanonicalFrameCursorTests
{
    private static readonly Guid MatchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string SourceHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Frame_cursor_round_trips_selected_context_source_and_keyset()
    {
        var token = CanonicalCursorCodec.EncodeFrame(MatchId, 3308, 4, SourceHash, 42, "frame-00042");

        var decoded = CanonicalCursorCodec.DecodeFrame(token, MatchId, 3308, 4);

        Assert.Equal(42, decoded.AfterSequence);
        Assert.Equal("frame-00042", decoded.AfterFrameId);
        Assert.Equal(SourceHash, decoded.SourceSha256);
        CanonicalCursorCodec.ValidateFrameProjection(decoded,
            new SelectedMatchProjection(null!, 4, 42, 3308, "algorithm-v1", SourceHash, SourceHash));
    }

    [Fact]
    public void Frame_cursor_rejects_tampered_and_mismatched_context()
    {
        var token = CanonicalCursorCodec.EncodeFrame(MatchId, 3308, 4, SourceHash, 42, "frame-00042");

        Assert.Throws<ArgumentException>(() => CanonicalCursorCodec.DecodeFrame(token[..^1] + "!", MatchId, 3308, 4));
        Assert.Throws<ArgumentException>(() => CanonicalCursorCodec.DecodeFrame(token, Guid.NewGuid(), 3308, 4));
        Assert.Throws<ArgumentException>(() => CanonicalCursorCodec.DecodeFrame(token, MatchId, 3309, 4));
        Assert.Throws<ArgumentException>(() => CanonicalCursorCodec.DecodeFrame(token, MatchId, 3308, 5));
        var nullHashPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{{\"matchId\":\"{MatchId:D}\",\"selectedTimeSeconds\":3308,\"canonicalRevision\":4,\"sourceSha256\":null,\"afterSequence\":42,\"afterFrameId\":\"frame-00042\"}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Throws<ArgumentException>(() => CanonicalCursorCodec.DecodeFrame(nullHashPayload, MatchId, 3308, 4));

        var decoded = CanonicalCursorCodec.DecodeFrame(token, MatchId, 3308, 4);
        Assert.Throws<ArgumentException>(() => CanonicalCursorCodec.ValidateFrameProjection(decoded,
            new SelectedMatchProjection(null!, 4, 42, 3308, "algorithm-v1", new string('b', 64), SourceHash)));
    }
}
