# Keyed Agent Invoker Demo

This demo shows how to register multiple Dapr Conversation chat clients using the conversation component name as the key via the `WithAgent` helper, map each component name to a distinct agent, and then pick the keyed client when invoking an agent.

## Behavior

- `conversation-ollama` uses the `KeyedAgent.Ollama` agent and returns concise answers.
- `conversation-openai` uses the `KeyedAgent.OpenAI` agent and returns JSON with `answer` and `confidence`.

## Run

1. Ensure you have Dapr installed and the conversation components configured.
2. Start the app:

```bash
dotnet run
```

3. Invoke with a conversation component name:

```bash
curl -X POST http://localhost:5000/ask/conversation-ollama -H "Content-Type: application/json" -d "{\"prompt\":\"Hello\"}"
```

Replace `conversation-ollama` with `conversation-openai` (or your own component names) to select a different conversation component.
