# Dapr .NET SDK - Skills Demo using Microsoft Agent Framework

This example demonstrates [Skills](https://github.com/diagridio/dotnet-ai/issues/34) support: portable
packages of instructions, reference material, and scripts that give an agent domain-specific expertise
at runtime, discovered via all three mechanisms MAF supports and mixed together on one agent:

- **File-based** — `skills/unit-converter/SKILL.md` (+ a `references/notes.md` resource), discovered via
  `AgentSkillsProviderBuilder.UseFileSkill(...)`.
- **Inline** — a `joke-teller` skill defined directly in code with `AgentInlineSkill`.
- **Class-based** — a `greeting-formalizer` skill (`GreetingSkill : AgentClassSkill<GreetingSkill>`) whose
  script is a plain C# method attributed with `[AgentSkillScript]`.

`UseScriptApproval()` also gates skill scripts (like `greeting-formalizer`'s) behind human approval —
see `IToolApprovalHandler` and the `ConsoleApprovingToolApprovalHandler` in `Program.cs`.

## Prerequisites

- [.NET 8+](https://dotnet.microsoft.com/download) installed
- [Dapr CLI](https://docs.dapr.io/getting-started/install-dapr-cli/)
- [Initialized Dapr environment](https://docs.dapr.io/getting-started/installation)
- [Dapr .NET SDK](https://docs.dapr.io/developing-applications/sdks/dotnet/)
- [Ollama](https://ollama.com/) installed

## Running the example

To run the sample locally, run this command in the `\examples\` directory to load the Dapr runtime:

```sh
dapr run --app-id skillsapp --dapr-grpc-port 50001 --dapr-http-port 3500 --resources-path "Components/"
```

Then, in another terminal, run the app from `\examples\SkillsDemo`:

```sh
dotnet run
```

## Try it out

Ask the agent something that should trigger the file-based skill:

```sh
curl -X POST http://localhost:5041/ask \
  -H "Content-Type: application/json" \
  -d '{"prompt": "Convert 10 miles to kilometers."}'
```

The agent's system prompt only ever advertises skill *names and descriptions* (see the
`<available_skills>` block MAF generates) — watch the application logs to see it call `load_skill` for
`unit-converter` before answering, rather than having the full skill content in every prompt.

Ask for something that exercises the class-based skill's script (and the approval gate):

```sh
curl -X POST http://localhost:5041/ask \
  -H "Content-Type: application/json" \
  -d '{"prompt": "Formalize this greeting: hey there!!"}'
```

You should see `>>> Approving script call 'run_skill_script' for agent 'SkillsAgent' (demo auto-approval)`
in the application logs — that's `ConsoleApprovingToolApprovalHandler` standing in for a real human
decision. A production `IToolApprovalHandler` should await one instead (see its remarks for why that's
safe to do inside a Dapr Workflow activity).

Or just ask for a joke, to exercise the inline skill:

```sh
curl -X POST http://localhost:5041/ask \
  -H "Content-Type: application/json" \
  -d '{"prompt": "Tell me a joke."}'
```
