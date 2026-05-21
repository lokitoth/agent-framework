// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using Microsoft.Agents.AI.Foundry.Hosting.Realtime;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Foundry.Hosting.Realtime.UnitTests;

[Experimental("MEAIREALTIME001")]
public class VoiceLiveInvocationsEventEncoderTests
{
    private static string Encode(RealtimeServerMessage msg)
    {
        VoiceLiveInvocationsEventEncoder encoder = new();
        using MemoryStream buf = new();
        encoder.Encode(msg, buf);
        return Encoding.UTF8.GetString(buf.ToArray());
    }

    [Fact]
    public void TranscriptDelta_EmitsExpected_VoiceLiveFrame()
    {
        OutputTextAudioRealtimeServerMessage msg = new(RealtimeServerMessageType.OutputAudioTranscriptionDelta) { Text = "hello" };

        string frame = Encode(msg);

        Assert.StartsWith("data: ", frame);
        Assert.EndsWith("\n\n", frame);
        Assert.Contains("\"type\":\"output_audio_transcription.delta\"", frame);
        Assert.Contains("\"delta\":\"hello\"", frame);
    }

    [Fact]
    public void TextDelta_AlsoMapsTo_TranscriptionDelta()
    {
        OutputTextAudioRealtimeServerMessage msg = new(RealtimeServerMessageType.OutputTextDelta) { Text = "hi" };

        string frame = Encode(msg);
        Assert.Contains("\"type\":\"output_audio_transcription.delta\"", frame);
        Assert.Contains("\"delta\":\"hi\"", frame);
    }

    [Fact]
    public void ResponseDone_EmitsDoneSentinel()
    {
        RealtimeServerMessage msg = new() { Type = RealtimeServerMessageType.ResponseDone };

        string frame = Encode(msg);
        Assert.Contains("\"type\":\"done\"", frame);
    }

    [Fact]
    public void UnhandledType_EmitsNothing()
    {
        RealtimeServerMessage msg = new() { Type = new RealtimeServerMessageType("custom.unknown") };

        VoiceLiveInvocationsEventEncoder encoder = new();
        using MemoryStream buf = new();
        int written = encoder.Encode(msg, buf);
        Assert.Equal(0, written);
    }

    [Fact]
    public void NullMessage_Throws()
    {
        VoiceLiveInvocationsEventEncoder encoder = new();
        using MemoryStream buf = new();
        Assert.Throws<ArgumentNullException>(() => encoder.Encode(null!, buf));
    }
}

[Experimental("MEAIREALTIME001")]
public class NeutralRealtimeInvocationsEventEncoderTests
{
    [Fact]
    public void TextDelta_EmitsTextDeltaFrame()
    {
        OutputTextAudioRealtimeServerMessage msg = new(RealtimeServerMessageType.OutputTextDelta) { Text = "neutral" };

        NeutralRealtimeInvocationsEventEncoder encoder = new();
        using MemoryStream buf = new();
        encoder.Encode(msg, buf);
        string frame = Encoding.UTF8.GetString(buf.ToArray());

        Assert.Contains("\"type\":\"text.delta\"", frame);
        Assert.Contains("\"delta\":\"neutral\"", frame);
    }

    [Fact]
    public void ResponseDone_EmitsDone()
    {
        NeutralRealtimeInvocationsEventEncoder encoder = new();
        using MemoryStream buf = new();
        encoder.Encode(new RealtimeServerMessage { Type = RealtimeServerMessageType.ResponseDone }, buf);
        string frame = Encoding.UTF8.GetString(buf.ToArray());

        Assert.Contains("\"type\":\"done\"", frame);
    }
}
