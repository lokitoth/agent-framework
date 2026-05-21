// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Agents.AI.Realtime.Abstractions.UnitTests;

[Experimental("MEAIREALTIME001")]
public class RealtimeAgentMetadataTests
{
    [Fact]
    public void Defaults_HaveExpectedValues()
    {
        RealtimeAgentMetadata metadata = new RealtimeAgentMetadata();

        Assert.Null(metadata.ProviderName);
        Assert.Null(metadata.ModelId);
        Assert.Equal(RealtimeModality.Text | RealtimeModality.Audio, metadata.SupportedModalities);
        Assert.False(metadata.SupportsInterruption);
        Assert.False(metadata.SupportsVideo);
    }

    [Fact]
    public void Constructor_PropagatesAllValues()
    {
        RealtimeAgentMetadata metadata = new RealtimeAgentMetadata(
            providerName: "openai.realtime",
            modelId: "gpt-4o-realtime-preview",
            supportedModalities: RealtimeModality.Text | RealtimeModality.Audio | RealtimeModality.Video,
            supportsInterruption: true,
            supportsVideo: true);

        Assert.Equal("openai.realtime", metadata.ProviderName);
        Assert.Equal("gpt-4o-realtime-preview", metadata.ModelId);
        Assert.Equal(
            RealtimeModality.Text | RealtimeModality.Audio | RealtimeModality.Video,
            metadata.SupportedModalities);
        Assert.True(metadata.SupportsInterruption);
        Assert.True(metadata.SupportsVideo);
    }

    [Theory]
    [InlineData(RealtimeModality.None)]
    [InlineData(RealtimeModality.Text)]
    [InlineData(RealtimeModality.Audio)]
    [InlineData(RealtimeModality.Video)]
    [InlineData(RealtimeModality.Text | RealtimeModality.Audio | RealtimeModality.Video)]
    public void SupportedModalities_RoundTrip(RealtimeModality modalities)
    {
        RealtimeAgentMetadata metadata = new RealtimeAgentMetadata(supportedModalities: modalities);
        Assert.Equal(modalities, metadata.SupportedModalities);
    }
}
