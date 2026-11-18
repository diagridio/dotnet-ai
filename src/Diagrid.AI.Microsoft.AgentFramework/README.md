# ![Logo](https://raw.githubusercontent.com/diagridio/dotnet-ai/master/properties/diagrid_dark.png)

![NuGet Version](https://img.shields.io/nuget/v/Diagrid.AI.Microsoft.AgentFramework?logo=nuget&label=Latest%20version&style=flat)
[![NuGet Version](https://img.shields.io/nuget/v/Diagrid.AI.Identity?logo=nuget&label=Diagrid.AI.Identity&style=flat)](https://www.nuget.org/packages/Diagrid.AI.Identity)

Durable execution for [Microsoft Agent Framework](https://github.com/microsoft/agent-framework)
agents in .NET. `Diagrid.AI.Microsoft.AgentFramework` runs each agent turn on
[Dapr Workflow](https://docs.dapr.io/developing-applications/building-blocks/workflow/), so an
agent that crashes or restarts mid-run picks up where it left off instead of starting over. Run
it against your own Dapr sidecar, or against [Diagrid Catalyst](https://www.diagrid.io/catalyst)
for a managed workflow engine.

## Install

```sh
dotnet add package Diagrid.AI.Microsoft.AgentFramework
```

To verify Catalyst user tokens on inbound requests (see [Identity](https://github.com/diagridio/dotnet-ai#identity)), also add:

```sh
dotnet add package Diagrid.AI.Identity
```

Both packages target `net8.0`, `net9.0` and `net10.0`.

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download): .NET 8 or later for your app. Building
  this repository and its examples needs the .NET 10 SDK, which `global.json` pins
  (`10.0.100`, rolling forward to later feature bands).
- A Dapr sidecar with workflow support, one of:
  - [Dapr CLI](https://docs.dapr.io/getting-started/install-dapr-cli/) with an
    [initialized Dapr environment](https://docs.dapr.io/getting-started/install-dapr-selfhost/)
    and a state store that has `actorStateStore: "true"`, like
    [`examples/Components/redis.yaml`](https://github.com/diagridio/dotnet-ai/blob/master/examples/Components/redis.yaml).
  - The [Diagrid CLI](https://docs.diagrid.io/catalyst/references/cli-reference/) and a
    Catalyst project, which provides the workflow engine and state store for you.
- A Dapr [conversation component](https://docs.dapr.io/developing-applications/building-blocks/conversation/)
  for the LLM each agent calls. The examples use
  [`examples/Components/conversation-ollama.yaml`](https://github.com/diagridio/dotnet-ai/blob/master/examples/Components/conversation-ollama.yaml),
  which needs [Ollama](https://ollama.com/) serving `ministral-3:3b` (`ollama pull ministral-3:3b`).

## How it works

An agent here is a Microsoft Agent Framework `AIAgent` whose turn runs as a Dapr workflow. Each
LLM call, tool call and context-provider step is a separate workflow activity, and the sidecar
records the result of every completed activity in the workflow's history. If the process crashes
or is redeployed mid-turn, the workflow replays from that history in the new process and resumes
at the first activity that had not finished: completed LLM and tool calls are not repeated.

You call agents the same way either way: through `IDaprAgentInvoker` from ordinary code, or from
inside your own Dapr workflows with `context.GetAgent(...)`.

## Run an example

[`examples/AgentInvokerDemo`](https://github.com/diagridio/dotnet-ai/tree/master/examples/AgentInvokerDemo)
is the smallest end-to-end agent. With Dapr initialized and Ollama running, start the sidecar from
the `examples` directory:

```sh
dapr run --app-id invokerapp --dapr-grpc-port 50001 --dapr-http-port 3500 --resources-path "Components/"
```

Then, in another terminal, run the app from `examples/AgentInvokerDemo` and ask the agent something:

```sh
dotnet run
curl -X POST http://localhost:5041/ask -H "Content-Type: application/json" \
  -d '{"prompt": "Give me three reasons to use workflow activities for AI calls."}'
```

To run against Catalyst instead, start the app with `diagrid dev run -- dotnet run`, as
[`examples/IdentityDemo`](https://github.com/diagridio/dotnet-ai/tree/master/examples/IdentityDemo)
does. The conversation component then has to exist in your Catalyst project and point at an
LLM provider that Catalyst can reach, so a local Ollama will not do.

## Register Agents with dependency injection

### Simple DI registration
The following shows the simple dependency injection registration of MAF agents:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register an agent; each run executes as Dapr Workflow activities
builder.Services.AddDaprAgents()
    .WithAgent(
        agentName: "SampleAgent",
        conversationComponentName: "conversation-ollama",
        instructions: "You are a helpful assistant. Answer normally unless the prompt asks for JSON.",
        serviceLifetime: ServiceLifetime.Singleton);

var app = builder.Build();
```

### Register Agents with typed deserialization contexts
The following elaborates to show how agent responses can be coerced into typed and deserialized JSON responses: 

```csharp
// Register the record that the result will be deserialized into
public sealed record StructuredAnswer(string Answer, double Confidence);

// Register the context used to deserialize the result - additional types need only be added with more `JsonSerializable` attributes
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StructuredAnswer))]
public partial class AgentInvokerJsonContext : JsonSerializerContext;

// Program startup
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDaprAgents(serializationOptions => 
{
    serializationOptions.AddContext(() => AgentInvokerJsonContext.Default);
}).WithAgent(
    agentName: "SampleAgent",
    conversationComponentName: "conversation-ollama",
    instructions: "You are a helpful assistant. Answer normally unless the prompt asks for JSON.",
    serviceLifetime: ServiceLifetime.Singleton);

var app = builder.Build();
```

### Register Agents alongside Dapr Workflows
The following shows how Dapr Workflows can be registered alongside agent registrations:

```csharp
// Register the record that the result will be deserialized into
public sealed record StructuredAnswer(string Answer, double Confidence);

// Register the context used to deserialize the result - additional types need only be added with more `JsonSerializable` attributes
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StructuredAnswer))]
public partial class AgentInvokerJsonContext : JsonSerializerContext;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDaprAgents(serializationOptions => 
{
    serializationOptions.AddContext(() => AgentInvokerJsonContext.Default); // Necessary to deserialize the workflow results to strongly typed values
}, workflowOptions => 
{
    workflowOptions.RegisterWorkflow<SampleWorkflow>(); // Register workflow types normally here
}).WithAgent(
    agentName: "SampleAgent",
    conversationComponentName: "conversation-ollama",
    instructions: "You are a helpful assistant. Answer normally unless the prompt asks for JSON.",
    serviceLifetime: ServiceLifetime.Singleton);

var app = builder.Build();
```

## Using Agents
Agents can be invoked in a variety of ways. The following examples show the most common approaches.

### Via `IDaprAgentInvoker` injection
In this example, the `IDaprAgentInvoker` is registered via any of the above approaches with dependency injection and is used to provision an instance of the named agent.
```csharp
public sealed record AskRequest(string Prompt);
app.MapPost("/ask", async (IDaprAgentInvoker invoker, AskRequest request, CancellationToken ct = default) => 
{
    var agent = invoker.GetAgent("SampleAgent"); // Retrieves the instance of the registered agent
    var response = await invoker.RunAgentAsync(agent, request.Prompt, cancellationToken: ct);
    return Results.Ok(new { response = response.Text });
});
```

### Within Dapr Workflow context
In this example, we access an instance of a registered Agent from within a Dapr Workflow context.
```csharp
public sealed partial class SampleWorkflow : Workflow<string, string>
{
    public override async Task<string> RunAsync(WorkflowContext context, string input)
    {
        var logger = context.CreateReplaySafeLogger(nameof(SampleWorkflow));
        var agent = context.GetAgent("SampleAgent"); // Retrieves the instance of the registered agent
        var result = await context.RunAgentAndDeserializeAsync<StructuredAnswer>(
            agent: agent,
            message: $"Analyze and return JSON: {{\"answer\": string, \"confidence\": number}}\n{input}",
            logger: logger)
            .ConfigureAwait(false); // Runs the agent invocation as a Dapr workflow and returns the strongly-typed result
        // ...
    }
}
```

## LLM call timeouts
Each LLM call is bounded by a timeout, 100 seconds unless you change it. A slow model, such as one
running locally, can need more. Set a default for every agent, override it per agent, or pass
`Timeout.InfiniteTimeSpan` to disable it:

```csharp
builder.Services.AddDaprAgents()
    .WithAgent(
        agentName: "SampleAgent",
        conversationComponentName: "conversation-ollama",
        instructions: "You are a helpful assistant.")
    .WithLlmTimeout(TimeSpan.FromMinutes(3))                  // every agent
    .WithLlmTimeout("SampleAgent", TimeSpan.FromMinutes(10)); // this agent only
```

The timeout applies to one LLM call, not to a whole agent run. A call that exceeds it fails its
activity with a `TimeoutException` naming the agent.

> `AddDaprAgents()` registers the Dapr Conversation client itself, without the 100-second
> `HttpClient` cap. If you register your own with `AddDaprConversationClient(...)` *before*
> `AddDaprAgents()`, yours is kept, and its `HttpClient.Timeout` (100 seconds by default) still
> applies on top of `WithLlmTimeout`, so set it with `UseTimeout(...)` as well.

## Skills
Skills are portable packages of instructions, reference material, and scripts that give an agent
domain-specific expertise at runtime — complementary to Tools. They're discovered via a skills
provider, advertised by name and description only in the agent's system prompt, and loaded on demand
through `load_skill`/`read_skill_resource`/`run_skill_script` tool calls, keeping full skill content
out of every prompt until the agent actually needs it.

> Skills build on MAF's `AgentSkill`/`AgentSkillsProvider` APIs, which are marked
> `[Experimental("MAAI001")]` upstream (evaluation purposes only) — `WithSkills(...)` carries the same
> marker.

### Registering skills
Skills can be sourced three ways — file-based, inline, and class-based — mixed freely on the same
agent via `AgentSkillsProviderBuilder`:

```csharp
// Resolve file skills against the build output, which is where the csproj's
// <Content Include="skills\**\*" CopyToOutputDirectory="PreserveNewest" /> item puts them.
var skillPath = Path.Combine(AppContext.BaseDirectory, "skills", "unit-converter");

builder.Services.AddDaprAgents()
    .WithAgent(
        agentName: "SkillsAgent",
        conversationComponentName: "conversation-ollama",
        instructions: "You are a helpful assistant.",
        serviceLifetime: ServiceLifetime.Singleton)
    .WithSkills("SkillsAgent", skills => skills
        // File-based: discovered from SKILL.md. MAF requires a script runner whenever any
        // file-based source is configured, even if that skill defines no scripts itself.
        .UseFileSkill(skillPath, scriptRunner: (_, _, _, _, _) =>
            throw new NotSupportedException("The unit-converter skill has no scripts."))
        .UseSkill(new AgentInlineSkill(                       // Inline: defined directly in code
            name: "joke-teller",
            description: "Tells a short, work-appropriate joke on request.",
            instructions: "When asked for a joke, tell exactly one short, clean joke."))
        .UseSkill(new GreetingSkill())                        // Class-based: AgentClassSkill<T>
        .UseScriptApproval());                                // Require approval before running scripts

var app = builder.Build();
```

A single skill (or a plain list) can also be attached directly, without the builder:
```csharp
builder.Services.AddDaprAgents()
    .WithSkills("SkillsAgent", new AgentInlineSkill(name: "...", description: "...", instructions: "..."));
```

Any `AIContextProvider` — not just skills — can be attached to an agent the same way, via
`WithContextProviders(...)`.

### Script approval
Skill-bundled scripts can require human approval before they run
(`AgentSkillsProviderBuilder.UseScriptApproval()`). Implement `IToolApprovalHandler` and register it
*before* calling `AddDaprAgents()` to decide whether a given call is allowed to proceed — without one
registered, every approval-required call is denied by default:

```csharp
public sealed class SlackApprovalHandler : IToolApprovalHandler
{
    public async Task<ToolApprovalDecision> RequestApprovalAsync(ToolApprovalRequest request, CancellationToken ct = default)
    {
        // Runs inside a Dapr Workflow *activity*, so it's safe to await a real decision here —
        // e.g. post to Slack and poll a data store the response updates out of band.
        var approved = await AwaitHumanDecisionAsync(request, ct);
        return approved ? ToolApprovalDecision.Approve() : ToolApprovalDecision.Deny("Declined in Slack.");
    }
}

builder.Services.AddSingleton<IToolApprovalHandler, SlackApprovalHandler>();
builder.Services.AddDaprAgents() /* ... */;
```

See [`examples/SkillsDemo`](https://github.com/diagridio/dotnet-ai/tree/master/examples/SkillsDemo) for
a complete, runnable example covering all three discovery mechanisms plus script approval.

## Usage analytics

`AddDaprAgents()` reports one anonymous usage event the first time it's called, once per
package per process. NuGet publishes aggregate download counts only, so this is how Diagrid
sees which package versions run on which platforms. Because it is one event per process, a
Kubernetes deployment produces one event per replica per restart: the numbers count process
starts, not deployments or users.

**What is sent:** the package name and version, operating system, architecture, the .NET
runtime version, the agent framework and the installed version of its library (`framework`,
`framework_version`), `kind=agent`, whether the process points at Catalyst or at a plain Dapr
sidecar (`target`), and whether it runs under a CI variable (`ci`). Nothing else: no
application data, configuration, app IDs, prompts, or hostnames. The receiving service is
[Scarf](https://scarf.sh). It derives coarse company and location information from the request
IP. How Scarf handles that data is described in
[Scarf's privacy policy](https://about.scarf.sh/privacy-policy).

**It never gets in the way:** the request runs on a background task with a one second HTTP
timeout, every failure is swallowed, and the caller never waits. Blocked egress and air-gapped
clusters behave normally. Nothing is written to your application's output — there is no
logger to configure for it either.

To opt out, set any of these environment variables before starting your application:

```bash
export DO_NOT_TRACK=1
# or
export SCARF_NO_ANALYTICS=1
# or
export DIAGRID_NO_ANALYTICS=1
```

## Identity

`Diagrid.AI.Identity` verifies the inbound Catalyst user token on every request and carries it
on outbound on-behalf-of calls. Two lines install it:

> The whole surface is marked `[Experimental("DGRDID001")]`, so it may change outside a major
> release. Suppress the diagnostic to opt in:
> `<NoWarn>$(NoWarn);DGRDID001</NoWarn>`.

```csharp
builder.Services.AddDiagridIdentity(cfg => cfg.Scopes = ["agent.invoke"]);
app.UseDiagridIdentity();

app.MapGet("/whoami", (HttpContext ctx) => Results.Ok(ctx.GetVerifiedUser()!.Subject));
```

A third registers the client outbound calls go out on. It is a plain `HttpClient` from
`IHttpClientFactory`, so it goes anywhere one goes, and it reads the caller's token at send
time rather than when the client is built:

```csharp
builder.Services.AddDiagridIdentityHttpClient();
```

### Discovery precedence

The issuer, audience and JWKS endpoint come from four sources, highest precedence first:

1. **Explicit configuration** — `cfg.Issuer`, `cfg.Audience`, `cfg.JwksUri`.
2. **The local sidecar's `/v1.0/metadata`** — probed at `http://127.0.0.1:$DAPR_HTTP_PORT`
   (or `$CATALYST_DAPR_HTTP_PORT`). Asked before the remote one: a deployed in-cluster app
   keeps its loopback call rather than paying for a network round trip.
3. **The remote sidecar's `/v1.0/metadata`** — probed at `$DAPR_HTTP_ENDPOINT`, authenticated
   with `$DAPR_API_TOKEN` when set. This is the source `diagrid dev run` supplies.
4. **Environment variables** — `DIAGRID_DP_SENTRY_ISSUER` and `DIAGRID_DP_SENTRY_AUDIENCE`.

The JWKS endpoint itself resolves explicit first, then the value the sidecar advertised when
its issuer is the one that resolved, then `issuer + /jwks.json`. If no source supplies an
issuer, every token-carrying request is refused with 503 `oauth.not_configured`.

`cfg.AllowInsecureJwks = true` accepts a non-loopback plaintext `http://` JWKS endpoint. It
relaxes plain HTTP only — `file://` and every other scheme stay refused — and it is a
local-development escape hatch: signing keys fetched over plaintext can be substituted by
anyone on the path, which gives up the guarantee that a verified token was signed by
dp-Sentry. Loopback endpoints are exempt without it.

`cfg.RequireAuth` governs the no-token case only. When `true` (the default) a request with no
`X-Diagrid-User-Token` is refused with 401 `oauth.missing_token`; when `false` it reaches the
handler and `GetVerifiedUser()` returns `null`. Either way a token that **is** present is
always verified, and an invalid one is always refused.

### Status and error codes

Every rejection is `{"error":"<code>"}` with `Cache-Control: no-store`.

| Status | Code | When |
| --- | --- | --- |
| 401 | `oauth.missing_token` | No `X-Diagrid-User-Token` header and `RequireAuth` is on |
| 401 | `oauth.decode_error` | The token is not a well-formed JWT |
| 401 | `oauth.invalid_signature` | The signature did not verify against the key set |
| 401 | `oauth.expired` | The token's `exp` has passed (120s clock skew allowed) |
| 401 | `oauth.invalid_issuer` | The token's `iss` does not match the resolved issuer |
| 401 | `oauth.invalid_audience` | The token's `aud` does not match the resolved audience |
| 401 | `oauth.invalid_token` | Any other claim failure — a missing `exp`/`iss`/`sub`, or a disallowed `alg` |
| 403 | `oauth.missing_scope` | The verified token lacks a scope the route requires |
| 503 | `oauth.not_configured` | No source supplied identity coordinates, or the JWKS endpoint is unusable |
| 503 | `oauth.verifier_unavailable` | Key material is not loaded yet, or no key matches the token's `kid` |

Claim checks run in one order across every Diagrid SDK — required claims, then `exp`, then
`iss`, then `aud` — so a token with two defects yields the same code wherever it is sent.

See [`examples/IdentityDemo`](https://github.com/diagridio/dotnet-ai/tree/master/examples/IdentityDemo)
for a complete, runnable example.

## Links
- [Diagrid](https://diagrid.io/)
- [Diagrid Documentation](https://docs.diagrid.io/)
- [Catalyst quickstarts](https://docs.diagrid.io/catalyst/quickstarts/)
- [NuGet: Diagrid.AI.Microsoft.AgentFramework](https://www.nuget.org/packages/Diagrid.AI.Microsoft.AgentFramework)
- [NuGet: Diagrid.AI.Identity](https://www.nuget.org/packages/Diagrid.AI.Identity)
- [License](https://github.com/diagridio/dotnet-ai/blob/master/LICENSE.md)

### Diagrid AI SDKs for other languages
- [Python](https://github.com/diagridio/python-ai)
- [TypeScript](https://github.com/diagridio/typescript-ai)
- [Go](https://github.com/diagridio/go-ai)
- [Java](https://github.com/diagridio/java-ai)
- [Rust](https://github.com/diagridio/rust-ai)
