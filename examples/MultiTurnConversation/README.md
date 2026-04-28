# Dapr .NET Agent Framework - Single-Agent Session Demo

This example demonstrates multi-turn conversations backed by a durable Dapr Workflow session. The conversation history is maintained inside a long-running `SessionWorkflow`. If the process crashes mid-conversation, recovery picks up exactly where it left off.

## Prerequisites

- [.NET 8+](https://dotnet.microsoft.com/download) installed
- [Dapr CLI](https://docs.dapr.io/getting-started/install-dapr-cli/)
- [Initialized Dapr environment](https://docs.dapr.io/getting-started/installation)
- [Dapr .NET SDK](https://docs.dapr.io/developing-applications/sdks/dotnet/)
- [Ollama](https://ollama.com/) installed

## Running the example
From the `\examples` directory, start the Dapr runtime:

```sh
dapr run --app-id wfapp --dapr-grpc-port 50001 --dapr-http-port 3500 --resources-path "Components/"
```

Then run the app in another terminal with `dotnet run`. It listens on `http://localhost:5041`.

### Start a conversation
Using a tool that can submit HTTP requests, send a POST request to `http://localhost:5041/chat` with the following body, but without specifying a session ID - one will be created for you:
```json
{
  "message": "I want to plan a week-long trip to Switzerland in July."
}
```

Response:
```json
{
  "sessionId": "a1b2c3d4...",
  "response": "Switzerland in July is a wonderful choice! ..."
}
```

#### Continue the conversation:
Use the returned `sessionId` to continue. Note that the agent remembers the full conversation when you send the following body to `http://localhost:5041/chat` in another POST request:
```json
{
  "message": "What sorts of activities might I do during the day?",
  "sessionId": "a1b2c3d4..."
}
```

The agent will reference the earlier discussion about a week-long July trip because the workflow feeds all prior messages back into the LLM context automatically.


#### Resume after a restart
Stop and restart the application. The session workflow is durable - send another message with the same `sessionId` and the conversation continues seamlessly.

## Next steps
See the `TranslationReviewDemo` example for a demonstration of a multi-agent workflow with isolated history or the `ResearcherDemo` example demonstrating a multiple agents sharing a history.