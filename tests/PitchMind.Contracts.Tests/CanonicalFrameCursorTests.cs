using PitchMind.Api;
using PitchMind.Application.Abstractions;
using PitchMind.Domain.Match;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class CanonicalFrameCursorTests
{
    private static readonly Guid MatchGuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string SourceHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Frame_cursor_binds_match_selected_time_both_revisions_source_and_key()
    {
        var cursor = CanonicalCursorCodec.EncodeFrame(MatchGuid, 120, 4_294_967_300L, 5, SourceHash, 42, "frame-00042");

        var decoded = CanonicalCursorCodec.DecodeFrame(cursor, MatchGuid, 120, 5);
        Assert.Equal(4_294_967_300L, decoded.CanonicalRevision);
        Assert.Equal(5, decoded.ProjectionRevision);
        Assert.Equal(42, decoded.AfterSequence);
        Assert.Equal("frame-00042", decoded.AfterFrameId);
        Assert.Equal(SourceHash, decoded.SourceSha256);

        var projection = new SelectedMatchProjection(
            new MatchState(MatchGuid.ToString("D"), 120, 1, MatchPhase.FirstHalf, 0, 0, null,
                11, 11, Array.Empty<MatchPlayerState>(), 2, "synthetic-test"),
            4_294_967_300L, 5, 42, 120, "rules-v2", SourceHash, new string('b', 64));
        CanonicalCursorCodec.ValidateFrameProjection(decoded, projection);
    }

    [Fact]
    public void Frame_cursor_rejects_changed_match_time_revision_and_projection_source()
    {
        var cursor = CanonicalCursorCodec.EncodeFrame(MatchGuid, 120, 7, 5, SourceHash, 42, "frame-42");
        Assert.Throws<ArgumentException>(() => CanonicalCursorCodec.DecodeFrame(cursor, Guid.NewGuid(), 120, 5));
        Assert.Throws<ArgumentException>(() => CanonicalCursorCodec.DecodeFrame(cursor, MatchGuid, 121, 5));
        Assert.Throws<ArgumentException>(() => CanonicalCursorCodec.DecodeFrame(cursor, MatchGuid, 120, 6));

        var decoded = CanonicalCursorCodec.DecodeFrame(cursor, MatchGuid, 120, 5);
        var changedSource = new SelectedMatchProjection(
            new MatchState(MatchGuid.ToString("D"), 120, 1, MatchPhase.FirstHalf, 0, 0, null,
                11, 11, Array.Empty<MatchPlayerState>(), 2, "synthetic-test"),
            7, 5, 42, 120, "rules-v2", new string('c', 64), new string('b', 64));
        Assert.Throws<ArgumentException>(() => CanonicalCursorCodec.ValidateFrameProjection(decoded, changedSource));
    }

    [Fact]
    public void Frame_cursor_rejects_invalid_source_hash_or_frame_key()
    {
        Assert.Throws<ArgumentException>(() => CanonicalCursorCodec.DecodeFrame(
            CanonicalCursorCodec.EncodeFrame(MatchGuid, 120, 7, 5, "not-a-hash", 42, "frame-42"), MatchGuid, 120, 5));
        Assert.Throws<ArgumentException>(() => CanonicalCursorCodec.DecodeFrame(
            CanonicalCursorCodec.EncodeFrame(MatchGuid, 120, 7, 5, SourceHash, -1, "frame-42"), MatchGuid, 120, 5));
        Assert.Throws<ArgumentException>(() => CanonicalCursorCodec.DecodeFrame(
            CanonicalCursorCodec.EncodeFrame(MatchGuid, 120, 7, 5, SourceHash, 42, " "), MatchGuid, 120, 5));
    }
}
