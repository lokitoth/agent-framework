// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI;

/// <summary>
/// Provides JSON serialization helpers shared by the realtime agent surface.
/// </summary>
/// <remarks>
/// Mirrors <c>AgentAbstractionsJsonUtilities</c>. For the proto we expose a
/// single shared <see cref="JsonSerializerOptions"/> with web defaults, no
/// source-generation context wired up yet. Source-generated
/// <see cref="JsonSerializerContext"/> is a follow-up tracked against the
/// realtime proto once the surface stabilizes.
/// </remarks>
[Experimental("MEAIREALTIME001")]
public static class RealtimeAgentJsonUtilities
{
    /// <summary>Gets the default <see cref="JsonSerializerOptions"/> used by realtime types.</summary>
    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions();

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        options.MakeReadOnly();
        return options;
    }
}
