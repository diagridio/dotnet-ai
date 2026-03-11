# ![Logo](https://raw.githubusercontent.com/diagridio/dotnet-ai/master/properties/diagrid_dark.png) Diagrid

[![NuGet Version](https://img.shields.io/nuget/v/Diagrid.AI.Microsoft.AgentFramework?logo=nuget&label=Latest%20version&style=flat)]

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
public seaded record StructuredAnswer(string Answer, double Confidence);

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
public seaded record StructuredAnswer(string Answer, double Confidence);

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

## Links
- [Diagrid](https://diagrid.io/)
- [Diagrid Documentation](https://docs.diagrid.io/)
- [NuGet Package](https://www.nuget.org/packages/Diagrid.AI.Microsoft.AgentFramework)
- [License](https://github.com/diagridio/dotnet-ai/blob/master/LICENSE.md)