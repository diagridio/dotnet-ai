# Dapr .NET SDK - Agent Invoker Demo

This example demonstrates invoking a registered AI agent directly from DI while the execution
still runs inside Dapr Workflow activities under the hood.

## Prerequisites

- [.NET 8+](https://dotnet.microsoft.com/download) installed
- [Dapr CLI](https://docs.dapr.io/getting-started/install-dapr-cli/)
- [Initialized Dapr environment](https://docs.dapr.io/getting-started/installation)
- [Dapr .NET SDK](https://docs.dapr.io/developing-applications/sdks/dotnet/)
- [Ollama](https://ollama.com/) installed

## Running the example

From the `\examples\AI` directory, start the Dapr runtime:

```sh
dapr run --app-id invokerapp --dapr-grpc-port 50001 --dapr-http-port 3500 --resources-path "Components/"
```

Then run the app in another terminal with `dotnet run`. It listens on `http://localhost:5112`.

### Ask for a normal response

Send a POST request to `http://localhost:5112/ask`:

```json
{
  "prompt": "Give me three reasons to use workflow activities for AI calls."
}
```

You should receive a JSON response containing the agent's text.

### Ask for a structured response

Send a POST request to `http://localhost:5112/ask-typed`:

```json
{
  "prompt": "Summarize why deterministic workflows matter in one sentence."
}
```

This endpoint requests JSON output and deserializes it using a source-generated serializer context.

### Observing workflow-backed execution

Watch the application logs while making requests. You should see activity logs like:

```
Invoking agent InvokerAgent with message '...'
```

These logs indicate that even though the invocation happens via DI, the actual agent execution runs
inside a workflow activity.
