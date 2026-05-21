SDK Layer: Wraps Provider-specific Clients in RealtimeAgent

- provider-specific transport (likely WebSocket)
- outputs: text, PCM, uPCM, uPCM

RealtimeAgent:

- Send (text, audio, video?)
- Receive (test?, audio, toolCall)
- Is Voice materially different from Video? (i.e. should we do VoiceAgent rather than RealtimeAgent?)
  - Do we need to have a more complex story than supporting both VideoData and AudioData?
  - At the end of the day, so long as we can stream chunks of bytes in and out, we have the raw data rail needed for this

- Similar as AIAgent, but no RunAsync: Only RunStreamingAsync equivalent
  - TextContent
  - AudioContent
  - FunctionCallContent/ResponseContent
  - ToolCallApprovalRequestContent/ResponseContent


- Model in all of the providers is:
  - ConnectSession(sessionConfig)
    - not real notion of reconnect to a session? (support it out of the box, but...)
    - similar to Workflows w/ "Run"
  - Loop:
    - SendText(), SendAudio()
    - Receive()

  -CloseSession()

- "Sandwich" 

Likely some form of the following:

```csharp
RealtimeAgent(AIAgent innerTextAgent, 
              TTSClientCallback(/*text -> audio*/), 
              SRClientCallback(/*audio -> text*/),
              VoiceActivityDetector? vad)
```

- Building block component for providers
  - Do we try to insist on running the underlying agent in streaming mode?  
    We can do some efforts to make it work for models that do not support streaming tokens, but this seems like it could be a sizeable "adapter problem"

  - Need to ensure we get onto the right underlying AgentSession for the inner AIAgent
  - InMemorySession: Since session "ground truth" is held at the inner AIAgent, we should be able to store only the text for InMemory / store=False cases

- What does InMemorySession look like for a generic RealtimeAgent?
  - Do we store and rehydrate audio? Or do we just keep the text around?
  - Will we get automatic transcription of incoming messages from the user on all providers?

- Supported Input/Output Formats
  - **Foundry**: PCM, G711Ulaw, G711Alaw
    - https://learn.microsoft.com/en-us/azure/ai-services/speech-service/voice-live-how-to
  - **OpenAI**: PCM(input can only be this), PCM-u (=G711Ulaw), PCM-a (=G711Alaw)
    - https://developers.openai.com/api/docs/guides/realtime
  - **Anthropic**: Not Available
  - **Amazon Nova**: audio/lpcm (always Base64 encoded?)
    - https://docs.aws.amazon.com/nova/latest/userguide/speech.html
    - Included for completeness
    - Not in-box for text agent (bedrock), so may be out of scope for now?
  - **Gemini**: PCM
    - https://ai.google.dev/gemini-api/docs/live-api
  - **Grok**: PCM, PCM-u, PCM-a
    - Included for completeness
    - Not in-box for text agent, so likely out of scope for now

- Do we support transcoding? (Likely "no", at first)


---

### OSS?

- To what extent do we want to support this?
- VibeVoice (MSFT)
- 3rd Party?
  - These SDKs need to give us an audio byte stream (so long as they support a WebSocket endpoint?)

- No such thing as a non-live session
- VAD / interruption handling? (may need experimentation to make sure we surface the right levers)


Hosting Layer

MAAI.Hosting.Realtime.WebSockets
MAAI.Hosting.Realtime.WebRTC

- FastRTC on Python?
^ frontload whichever of these Foundry wants to use as the hosting backend

Key thing to avoid: Zero Copy insofar as possible: We should be able to move a ROSpan directly from incoming packets from underlying provider through the hosting layer.
