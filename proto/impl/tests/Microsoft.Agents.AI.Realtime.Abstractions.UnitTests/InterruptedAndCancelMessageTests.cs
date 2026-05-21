// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Realtime.Abstractions.UnitTests;

[Experimental("MEAIREALTIME001")]
public class InterruptedRealtimeServerMessageTests
{
    [Fact]
    public void Type_DefaultsToWellKnownInterruptedValue()
    {
        InterruptedRealtimeServerMessage msg = new InterruptedRealtimeServerMessage();
        Assert.Equal(InterruptedRealtimeServerMessage.InterruptedType, msg.Type);
        Assert.Equal("Interrupted", msg.Type.Value);
    }

    [Fact]
    public void IsAssignableToRealtimeServerMessage()
    {
        InterruptedRealtimeServerMessage msg = new InterruptedRealtimeServerMessage();
        Assert.IsAssignableFrom<RealtimeServerMessage>(msg);
    }

    [Fact]
    public void OptionalProperties_DefaultToNull_AndRoundTrip()
    {
        InterruptedRealtimeServerMessage msg = new InterruptedRealtimeServerMessage();
        Assert.Null(msg.InterruptedResponseId);
        Assert.Null(msg.OutputAudioOffsetInBytes);

        msg.InterruptedResponseId = "resp_123";
        msg.OutputAudioOffsetInBytes = 24_000L;

        Assert.Equal("resp_123", msg.InterruptedResponseId);
        Assert.Equal(24_000L, msg.OutputAudioOffsetInBytes);
    }
}

[Experimental("MEAIREALTIME001")]
public class CancelResponseRealtimeClientMessageTests
{
    [Fact]
    public void Defaults_AreNull()
    {
        CancelResponseRealtimeClientMessage msg = new CancelResponseRealtimeClientMessage();
        Assert.Null(msg.ResponseId);
        Assert.IsAssignableFrom<RealtimeClientMessage>(msg);
    }

    [Fact]
    public void ResponseId_RoundTrips()
    {
        CancelResponseRealtimeClientMessage msg = new CancelResponseRealtimeClientMessage { ResponseId = "resp_abc" };
        Assert.Equal("resp_abc", msg.ResponseId);
    }
}
