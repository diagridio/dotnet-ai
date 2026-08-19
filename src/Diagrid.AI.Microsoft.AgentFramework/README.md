# ![Logo](https://raw.githubusercontent.com/diagridio/dotnet-ai/master/properties/diagrid_dark.png)

![NuGet Version](https://img.shields.io/nuget/v/Diagrid.AI.Microsoft.AgentFramework?logo=nuget&label=Latest%20version&style=flat)

Diagrid.AI.Microsoft.AgentFramework is a library that facilitates building agents using Microsoft's Agent Framework atop Dapr's Durable Workflows.

## Register Agents with dependency injection

### Simple DI registration
The following shows the simple dependency injection registration of MAF agents:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register Dapr conversation client
builder.Services.AddDaprConversationClient();

// Register agents to run within 
builder.Services.AddDaprAgents()
    .WithAgent(
        agentName: "SampleAgent",
        conversationComponentName: "converastion-ollama",
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
builder.Services.AddDaprConversationClient();
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
builder.Services.AddDaprConversationClient();
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
            message: $"Analyze and return JSON: {{\"answer\": string, \"confidence\": number}}\n{input}"),
            logger: logger)
            .ConfigureAwait(false); // Runs the agent invocation as a Dapr workflow and returns the strongly-typed result
        // ...
    }
}
```

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
builder.Services.AddDaprAgents()
    .WithAgent(
        agentName: "SkillsAgent",
        conversationComponentName: "conversation-ollama",
        instructions: "You are a helpful assistant.",
        serviceLifetime: ServiceLifetime.Singleton)
    .WithSkills("SkillsAgent", skills => skills
        .UseFileSkill("./skills/unit-converter")              // File-based: discovered from SKILL.md
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
builder.WithSkills("SkillsAgent", new AgentInlineSkill(name: "...", description: "...", instructions: "..."));
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

## Links
- [Diagrid](https://diagrid.io/)
- [Diagrid Documentation](https://docs.diagrid.io/)
- [NuGet Package](https://www.nuget.org/packages/Diagrid.AI.Microsoft.AgentFramework)
- [License](https://github.com/diagridio/dotnet-ai/blob/master/LICENSE.md)