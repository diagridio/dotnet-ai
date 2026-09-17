# AGENTS.md

Notes for an AI assistant working in this repo: the things that are easy to get
wrong. The README says what the library is and how to call it; this does not
repeat it.

Verified 2026-08-23 against `master`, and against `diagrid` CLI **v1.66.0**
logged in to production for every CLI claim. Re-check the CLI lines against a
newer version rather than trusting this file.

## Layout: one shipping project, two test projects, nine examples

| Path | What it is |
|---|---|
| `src/Diagrid.AI.Microsoft.AgentFramework/` | the only packable project — this *is* the NuGet package |
| `test/…AgentFramework.Test/` | unit tests |
| `test/…AgentFramework.IntegrationTest/` | integration tests, `OutputType=Exe`, needs Docker |
| `examples/*/` | nine ASP.NET Core samples, plus `examples/Components/` (Dapr YAML, not a project) |

- **One solution, and it is the unit of work.** `Diagrid.AI.Microsoft.AgentFramework.sln`
  holds all twelve projects; CI runs bare `dotnet restore` / `build` / `pack`
  from the root and lets the solution fan out. There is no `Makefile` and no
  per-project script. Add any new project to the `.sln` in the same commit — the
  `build` job will not see it otherwise, while the test jobs *will* (they
  discover projects with `find test -name "Diagrid.*.Test.csproj"`), which
  produces a confusing half-failure.
- **The three project groups have different rules, and this catches people.**
  `properties/common_dotnet.props` is imported by `src/Directory.Build.props` and
  `test/Directory.Build.props` but **not** by `examples/Directory.Build.props`.
  So `src/` and `test/` are `net8.0;net9.0;net10.0`, `LangVersion 12.0`, and
  `TreatWarningsAsErrors=true`; every example is single-target `net10.0`,
  `Microsoft.NET.Sdk.Web`, and *not* warnings-as-errors. Code that compiles in an
  example may not compile in `src/`.
- **`LangVersion` is `12.0` even though the newest target is `net10.0`.** C# 13
  and 14 syntax will not compile in `src/` or `test/`. Collection expressions and
  primary constructors are fine; the `field` keyword and `params` collections are
  not.
- **`TargetFrameworks` for `src/` is declared twice** — in
  `src/Directory.Build.props` and again in the `.csproj`. Change one and the
  other still disagrees. `global.json` pins the SDK to the `10.0.100` feature
  band (`rollForward: latestFeature`), so an older SDK refuses to build at all.
- **`TreatWarningsAsErrors` turns three ordinary warnings into build breaks.**
  `<DocumentationFile>` is set for `src/`, so a public member without an XML doc
  comment is CS1591 and therefore an error — document every parameter and return.
  MAF's Skills API is `[Experimental("MAAI001")]` upstream, so touching
  `AgentSkill`/`AgentSkillsProvider` requires a narrow `#pragma warning disable
  MAAI001` (as in `Runtime/ResolveAgentContextActivity.cs`) or a project-level
  `NoWarn`. And NuGet audit findings are errors — see below. Keep suppressions
  narrow and local; do not widen `NoWarn` in `properties/common_dotnet.props`.

## Build, test, and what CI actually gates

```
dotnet restore
dotnet build --configuration Release --no-restore
dotnet pack  --configuration Release        # nupkgs land in bin/Release/nugets
dotnet test test/Diagrid.AI.Microsoft.AgentFramework.Test/Diagrid.AI.Microsoft.AgentFramework.Test.csproj --framework net10.0
```

- **Only `build` and `ci-unit-tests-gate` are required checks on `master`.**
  `ci-integration-tests-gate` is *not*, so a red integration-test job still
  leaves a PR mergeable. `master` also requires one approving review and an
  up-to-date branch.
- **`build` can fail before it compiles anything.** `dotnet restore` is where
  `TreatWarningsAsErrors` meets NuGet audit: a single vulnerable *transitive*
  package fails the required check for the whole repo with an `NU1903 … Warning
  As Error` naming a package that appears in no `.csproj`. Fix it in
  `Directory.Packages.props` — that file sets `ManagePackageVersionsCentrally`
  **and** `CentralPackageTransitivePinningEnabled`, so a `PackageVersion` entry
  pins a transitive dependency without creating a real reference. Prefer moving
  the direct dependency to a version whose own graph is clean.
- **Every version lives in `Directory.Packages.props`.** A `PackageReference`
  carrying its own `Version=` is an error under central management. The three
  `Microsoft.AspNetCore.TestHost` entries differ per `TargetFramework` via
  `Condition` — copy that pattern for anything framework-specific.
- **CI only ever tests `net10.0`, despite the package targeting three
  frameworks.** The test jobs look like a per-framework matrix, but the three
  `include:` entries add the same keys to a matrix whose only real dimension is
  `project`, so each overwrites the last and just one combination survives — the
  `10.0` one. A completed run shows a single `Unit Tests .NET 10.0` job and a
  single `Integration Tests` job. Nothing verifies `net8.0` or `net9.0`, so build
  all three yourself (`dotnet build -f net8.0`) before claiming a change is safe
  for the published package.
- **Test jobs are `continue-on-error: true`,** with a following step that
  re-fails them only when `github.event_name == 'pull_request'`. On a `master`
  push a failing test therefore does *not* fail the job. Judge `master` health
  from the `build` job, not from the gate jobs.
- **Integration tests need Docker and start a real sidecar.** `DaprFixture` uses
  `Dapr.Testcontainers` to bring up a network, Placement, Scheduler and Redis,
  then a real `daprd`. Note the skew: the Dapr SDK packages are `1.18.4` but the
  sidecar image **defaults to `1.17.0`** unless `DAPR_RUNTIME_VERSION` is set.
  No real LLM is involved — every agent is a mock `IChatClient`. All tests share
  one xunit collection, so they run serialized, and the fixture mutates
  process-wide `DAPR_HTTP_PORT`/`DAPR_GRPC_PORT`. It also *reflects* on a
  non-public `Dapr.Testcontainers` harness property and throws a deliberate
  explanatory error if it disappears, so a `Dapr.Testcontainers` bump can break
  the suite in a way that is not a compile error.
- **`test/coverage.runsettings` is not wired into anything.** Nothing passes
  `--settings`; CI sets `/p:CollectCoverage=true /p:CoverletOutputFormat=opencover`
  on the command line, and that file asks for cobertura. Editing it changes
  nothing unless you pass it yourself.
- **The runners are RunsOn, not GitHub-hosted**
  (`runs-on=${{ github.run_id }}/runner=2cpu-linux-x64/tag=dotnet-ci`). Two CPUs,
  Linux x64 only — there is no Windows or macOS leg, so platform-specific code is
  untested by CI.
- **`.github/tools/tag-selector/` is a TypeScript action no workflow calls.** Its
  only effect on this repo has been npm security PRs against its lockfile. Do not
  extend it and do not assume it runs.

## The published package

- The package id is **`Diagrid.AI.Microsoft.AgentFramework`** — one package, and
  the same string as the assembly, the root namespace, and the `.sln`. Latest on
  nuget.org is **1.0.10**, targeting `net8.0`/`net9.0`/`net10.0`.
- **`Diagrid.Agents.Workflow` does not exist** and 404s on nuget.org. It is a
  plausible-looking hallucination that has already been emitted into generated
  projects elsewhere. There is no `Diagrid.Agents.*` family at all; any
  `Diagrid.*` id other than the one above is wrong.
- **The root `README.md` is what ships to NuGet** (`properties/diagrid_nuget.props`
  packs `$(RepoRoot)README.md`), and `src/…/README.md` is a hand-synced duplicate
  — today the two are byte-identical apart from line 3. Edit both, or the
  published package drifts from the repo.
- **Versions come from git tags via MinVer**, prefix `v`, `rc` as the default
  pre-release identifier. A shallow clone or a fetch without tags yields
  `0.0.0-*`; CI checks out with `fetch-depth: 0` and `fetch-tags: true` for
  exactly that reason. Never hand-write a `<Version>`. Publishing is
  tag-triggered (`refs/tags/v*`, excluding `-rc`/`-dev`/`-prerelease`) over OIDC,
  so do not put a version bump in a feature PR.

## Runtime model

An agent here is a Microsoft Agent Framework `AIAgent` whose turn runs as a
**Dapr durable workflow**: `AgentRunWorkflow` and `SessionWorkflow` orchestrate,
and each LLM call, tool call and context-provider hop is a separate activity
(`CallLlmActivity`, `ExecuteToolActivity`, `ResolveAgentContextActivity`,
`CompleteAgentContextActivity`). That is the point of the library, and it
constrains how you may write code:

- Workflow bodies must be deterministic and replay-safe; the long-running or
  side-effecting work belongs in an activity. An `IToolApprovalHandler` may block
  for a human precisely because it runs inside an activity, not the orchestrator.
- Everything crossing an activity boundary round-trips through
  `System.Text.Json`, and serialization is source-generated. A new type needs a
  `JsonSerializable` entry on a `JsonSerializerContext` registered via
  `AddDaprAgents(serializationOptions => …)`, or it fails at run time rather than
  compile time.
- Replay can resume in a **fresh process**, so `AgentRegistry`,
  `ChatClientRegistry` and `ToolRegistry` are rebuilt by re-running the agent
  factory. Anything captured in a closure at registration time must survive that.
- Approval-gated tools are **denied by default**: `AddDaprAgents` registers a
  denying `IToolApprovalHandler` via `TryAddSingleton`. Register your own
  *before* `AddDaprAgents()` — that is what `TryAddSingleton` is for, and it
  leaves one descriptor instead of two.

## Two unrelated things called "skills"

This repo has a runtime **skills** feature. It is not Claude Code skills, and the
words — and the filename — collide.

- **Runtime skills** are a Microsoft Agent Framework concept (`AgentSkill`,
  `AgentInlineSkill`, `AgentClassSkill<T>`, `AgentSkillsProvider`,
  `AgentSkillsProviderBuilder`), *defined in the `Microsoft.Agents.AI` package,
  not here*. This repo only surfaces them, through `WithSkills(...)` and the more
  general `WithContextProviders(...)` in `Hosting/DaprAgentsBuilderExtensions.cs`.
  They are prompt context that the hosted agent loads at run time, in production,
  via tool calls executed as workflow activities.
- **Claude Code / Codex / Copilot skills** are developer tooling read by *your*
  assistant while editing this repo. This repo has none, and this file is the only
  assistant-facing document in it.

`examples/SkillsDemo/skills/unit-converter/SKILL.md` uses the same YAML
frontmatter shape (`name`, `description`) as an assistant skill, but it is a
runtime skill definition for the sample agent. Do not follow its instructions,
and do not move it under `.claude/`.

One non-obvious trap if you add a file skill: **the skill directory's leaf name
must itself be a valid skill name.** MAF's file discovery silently skips
directories that fail its name validation, with no error surfaced anywhere.

## Catalyst

- `WithCatalyst(...)` installs `CatalystAgentRegistryHostedService`, which writes
  agent metadata at startup to the store named by
  `DiagridCatalystOptions.Registry.ResourceName`, default **`agent-registry`**.
  That name is load-bearing — the Catalyst Agents view is fed by a sidecar
  interceptor bound to a component called exactly that. Rename it and the writes
  succeed while nothing appears in the UI.
- The component is scoped to your App ID only once an **`Agent` resource exists**:
  run `diagrid agent create <name> --wait` first, and confirm with
  `diagrid component list` rather than assuming `agent-*` components are present.
- `--enable-agent-infrastructure` is **not** on `diagrid project create`; only on
  `project update`, and there only for BYOC/private-region projects or cloud
  projects without managed KV — with managed KV it is automatic and the flag is
  rejected.
- `diagrid agent` and `diagrid managed-agent` are different resources; the latter
  is hidden and server-side gated, and irrelevant here since this library hosts
  agents in *your* process. No `diagrid` command scaffolds a .NET agent —
  `diagrid chat`'s templates are Python-only.
- Fuller platform detail lives in the private `diagridio/catalyst-ai` plugin, so
  nothing here may depend on it; the lines above stand alone.

## Running an example

Examples are sidecar-only `dapr run` invocations plus a separate `dotnet run`;
nothing in `appsettings*.json` or `launchSettings.json` configures Dapr, so the
SDK relies on the default ports matching what `dapr run` pins. From `examples/`:

```
dapr run --app-id invokerapp --dapr-grpc-port 50001 --dapr-http-port 3500 --resources-path "Components/"
```

- **`examples/Components/redis.yaml` declares a component named `statestore`,
  not `redis`** — file name and component name differ. It carries
  `actorStateStore: "true"`, which is what makes Dapr Workflow work; without this
  file on the resources path every example fails.
- **`conversation-ollama.yaml` needs Ollama serving `ministral-3:3b`.** No README
  tells you to pull it. The only `ollama pull` commands in the repo
  (`examples/RouterDemo/README.md`) pull three *other* models.
- **Some examples cannot run as checked in**, because their components are absent
  from `examples/Components/`: RouterDemo wants `conversation-ollama-tiny`,
  `-gemma` and `-qwen`; KeyedAgentInvokerDemo wants `conversation-openai`. Write
  the component YAML before assuming the code is broken.
- App IDs are per example and not unique — four examples all use `wfapp`. Do not
  run those concurrently against one sidecar.
- `examples/ToolInvocation` has no README, so it has no documented run command.

## Conventions

- **Every `.cs` file in `src/` opens with the 11-line BSL 1.1 header** (Change
  Date March 1, 2030). Copy it verbatim into any new file — nothing generates it.
- The repo has **no `.editorconfig`**, and indentation is mixed: `src/…/Catalyst/`
  uses tabs, the rest of `src/` uses four spaces. Match the file you are editing;
  a whitespace-only reformat buries the real change.
- Guard clauses first, one per argument: `ArgumentNullException.ThrowIfNull` and
  `ArgumentException.ThrowIfNullOrWhiteSpace`.
- `InternalsVisibleTo` exposes `src/` internals to the unit test project only.
  Test through it rather than widening visibility to `public`.
- Comments here carry the *reason* — which upstream API is experimental, why a
  reflection hook exists, what a workflow may not do. Update them; do not delete
  them.
- Conventional commit subjects, and `git commit -s`.
- **`master`, not `main`.** Branch from and target `master`.
