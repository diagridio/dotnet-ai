# Dapr .NET SDK - Router Demo using Microsoft Agent Framework

This example demonstrates an agent-driven router built with the Microsoft Agent Framework and Dapr Workflows.
It uses a routing agent to select the best specialist based on the registered agents and their prompts, then
uses a coordinator agent to craft a schema-focused prompt for the selected agent. If the specialist output
does not match the expected JSON shape, the workflow retries with updated coordination instructions.

The demo uses multiple Ollama conversation components to showcase routing across small local models. The entrypoint
is an agent invocation: <code>/route</code> calls the <code>RouterWorkflowAgent</code> via <code>IDaprAgentInvoker</code>,
and that agent schedules the router workflow internally. This keeps the example focused on agent invocation while
still showcasing workflow-backed resiliency under the hood.

## Prerequisites

- [.NET 8+](https://dotnet.microsoft.com/download) installed
- [Dapr CLI](https://docs.dapr.io/getting-started/install-dapr-cli/)
- [Initialized Dapr environment](https://docs.dapr.io/getting-started/installation)
- [Dapr .NET SDK](https://docs.dapr.io/developing-applications/sdks/dotnet/)
- [Ollama](https://ollama.com/) installed

## Pull the small models (Ollama)

This demo targets lightweight models so it can run locally. Pull the models used by the components:

```sh
ollama pull tinyllama
ollama pull gemma:2b
ollama pull qwen2:0.5b
```

If you prefer different small models, update the component YAML files in `examples/AI/Components`.

## Running the example

From the `\examples\` directory, start the Dapr runtime:

```sh
dapr run --app-id agentrouterapp --dapr-grpc-port 50001 --dapr-http-port 3500 --resources-path "Components/"
```

Then run the app from `\examples\RouterDemo` in another terminal:

```sh
dotnet run
```

It listens on `http://localhost:5041`.

## Try it out

### List available agents

```
GET http://localhost:5041/agents
```

### Route a request

```
POST http://localhost:5041/route
```

```json
{
  "input": "Summarize this release note into a short executive summary and bullets."
}
```

The endpoint returns the routing result directly, including:

- `agentName` and `modelComponent` selected by the router
- `expectedSchema` the specialist agent is instructed to follow
- `result` payload that matches the schema
- `attempts` showing router/coordinator/agent retries

## How it works

1. `/route` invokes **RouterWorkflowAgent** via <code>IDaprAgentInvoker</code> (which itself runs as a workflow).
2. **RouterWorkflowAgent** schedules the internal router workflow and waits for its result.
3. The workflow calls a single activity that drives the agent pipeline.
4. **RouterAgent** receives the user input plus the registered agent catalog (names, prompts, output schemas).
   It selects the most suitable specialist.
5. **CoordinatorAgent** crafts the exact message for the selected specialist and highlights the expected schema.
6. **Specialist agent** produces JSON that matches its schema. If the output is invalid, the activity re-invokes
   the coordinator with the validation error to tighten the prompt and retries the specialist.

Because the orchestration runs inside a Dapr Workflow (invoked by the agent), the routing and retries remain
deterministic while all LLM calls execute in workflow activities. This makes transient model errors or schema
mismatches easy to recover from without losing the workflow's deterministic guarantees, while keeping the HTTP
surface focused on agents instead of explicit workflow scheduling.
