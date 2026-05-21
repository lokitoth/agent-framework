// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI.Realtime.Foundry;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.Foundry.UnitTests;

[Experimental("MEAIREALTIME001")]
public class FoundryEventProjectorTests
{
    [Fact]
    public void EmptyJson_ReturnsNull()
    {
        Assert.Null(FoundryEventProjector.Project(string.Empty));
    }

    [Fact]
    public void MissingType_ReturnsNull()
    {
        Assert.Null(FoundryEventProjector.Project("{ \"hello\": \"world\" }"));
    }

    [Fact]
    public void TextDelta_ProjectsAsOutputTextDelta()
    {
        RealtimeServerMessage? msg = FoundryEventProjector.Project(
            "{ \"type\": \"response.text.delta\", \"delta\": \"hi\" }");

        OutputTextAudioRealtimeServerMessage outMsg = Assert.IsType<OutputTextAudioRealtimeServerMessage>(msg);
        Assert.Equal(RealtimeServerMessageType.OutputTextDelta, outMsg.Type);
        Assert.Equal("hi", outMsg.Text);
    }

    [Fact]
    public void AudioTranscriptDelta_ProjectsAsTranscriptionDelta()
    {
        RealtimeServerMessage? msg = FoundryEventProjector.Project(
            "{ \"type\": \"response.audio_transcript.delta\", \"delta\": \"hello\" }");

        OutputTextAudioRealtimeServerMessage outMsg = Assert.IsType<OutputTextAudioRealtimeServerMessage>(msg);
        Assert.Equal(RealtimeServerMessageType.OutputAudioTranscriptionDelta, outMsg.Type);
        Assert.Equal("hello", outMsg.Text);
    }

    [Fact]
    public void AudioDelta_ProjectsAsAudioDelta()
    {
        RealtimeServerMessage? msg = FoundryEventProjector.Project(
            "{ \"type\": \"response.audio.delta\", \"delta\": \"AAA=\" }");

        OutputTextAudioRealtimeServerMessage outMsg = Assert.IsType<OutputTextAudioRealtimeServerMessage>(msg);
        Assert.Equal(RealtimeServerMessageType.OutputAudioDelta, outMsg.Type);
        Assert.Equal("AAA=", outMsg.Audio);
    }

    [Fact]
    public void ResponseDone_Projects()
    {
        RealtimeServerMessage? msg = FoundryEventProjector.Project("{ \"type\": \"response.done\" }");

        Assert.NotNull(msg);
        Assert.Equal(RealtimeServerMessageType.ResponseDone, msg!.Type);
    }

    [Fact]
    public void OutputAudioBufferCleared_ProjectsAsInterrupted()
    {
        RealtimeServerMessage? msg = FoundryEventProjector.Project(
            "{ \"type\": \"output_audio_buffer.cleared\", \"response_id\": \"r1\", \"audio_end_ms\": 1234 }");

        InterruptedRealtimeServerMessage interrupted = Assert.IsType<InterruptedRealtimeServerMessage>(msg);
        Assert.Equal("r1", interrupted.InterruptedResponseId);
        Assert.Equal(1234, interrupted.OutputAudioOffsetInBytes);
    }

    [Fact]
    public void UnknownType_PassesThroughAsBaseMessage()
    {
        RealtimeServerMessage? msg = FoundryEventProjector.Project(
            "{ \"type\": \"some.unknown.event\" }");

        Assert.NotNull(msg);
        Assert.Equal(new RealtimeServerMessageType("some.unknown.event"), msg!.Type);
        Assert.False(msg is InterruptedRealtimeServerMessage);
        Assert.False(msg is OutputTextAudioRealtimeServerMessage);
    }
}
