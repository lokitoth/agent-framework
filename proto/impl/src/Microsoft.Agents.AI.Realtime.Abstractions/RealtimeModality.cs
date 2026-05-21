// Copyright (c) Microsoft. All rights reserved.

using System;

namespace Microsoft.Agents.AI;

/// <summary>
/// Identifies a content modality supported by a <see cref="RealtimeAgent"/>.
/// </summary>
/// <remarks>
/// The flags mirror the realtime conventions in <see cref="Microsoft.Extensions.AI.RealtimeSessionOptions.OutputModalities"/>.
/// Providers may advertise zero, one, or several modalities via
/// <see cref="RealtimeAgentMetadata.SupportedModalities"/>.
/// </remarks>
[Flags]
public enum RealtimeModality
{
    /// <summary>No modality. Used as the absent value.</summary>
    None = 0,

    /// <summary>Text input or output.</summary>
    Text = 1 << 0,

    /// <summary>Audio (PCM, μ-law, etc.) input or output.</summary>
    Audio = 1 << 1,

    /// <summary>Video frames (used by upcoming providers such as Gemini Live).</summary>
    Video = 1 << 2,
}
