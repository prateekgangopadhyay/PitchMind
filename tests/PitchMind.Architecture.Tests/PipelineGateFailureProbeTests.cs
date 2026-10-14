using System;
using Xunit;

namespace PitchMind.Architecture.Tests;

public sealed class PipelineGateFailureProbeTests
{
    [Fact]
    public void DeliberateGateFailureProbeFailsOnlyWhenOptedIn()
    {
        Assert.NotEqual("1", Environment.GetEnvironmentVariable("PITCHMIND_GATE_FAILURE_PROBE"));
    }
}
