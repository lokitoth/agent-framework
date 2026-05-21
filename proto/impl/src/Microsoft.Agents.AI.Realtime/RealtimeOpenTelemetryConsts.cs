// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI;

/// <summary>
/// Constants for OpenTelemetry instrumentation produced by the realtime
/// decorators. Mirrors the layout of
/// <c>Microsoft.Agents.AI.OpenTelemetryConsts</c> at a smaller scope; values
/// follow the <c>gen_ai.*</c> semantic conventions where applicable.
/// </summary>
internal static class RealtimeOpenTelemetryConsts
{
    public const string DefaultSourceName = "Microsoft.Agents.AI.Realtime";

    public const string OperationConnect = "connect";

    public static class GenAI
    {
        public const string OperationName = "gen_ai.operation.name";
        public const string SystemName = "gen_ai.system";
        public const string AgentId = "gen_ai.agent.id";
        public const string AgentName = "gen_ai.agent.name";
        public const string SessionId = "gen_ai.realtime.session.id";
    }
}
